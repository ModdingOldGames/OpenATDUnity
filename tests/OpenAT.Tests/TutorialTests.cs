using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Rivals;
using OpenAT.Sim.Save;
using OpenAT.Sim.Tutorial;
using Xunit;
using Xunit.Abstractions;

namespace OpenAT.Tests
{
    /// <summary>The mission-0 tutorial state (Sim/Tutorial) and the sweep's small sim additions.</summary>
    public class TutorialTests
    {
        readonly ITestOutputHelper _out;
        public TutorialTests(ITestOutputHelper o) => _out = o;

        static World Plain(int difficulty)
        {
            var w = new World(new GameData());
            w.Difficulty = difficulty;
            new TutorialSetup().Apply(w, new NewGameSettings { Difficulty = difficulty });
            return w;
        }

        [Fact]
        public void Setup_StartsOnlyInMissionZero()
        {
            Assert.True(Plain(Mission.Tutorial).Tutorial.Active);
            Assert.Equal(0, Plain(Mission.Tutorial).Tutorial.Step);
            Assert.False(Plain(Mission.FreeGame).Tutorial.Active);
            Assert.False(Plain(Mission.Passengers).Tutorial.Active);
        }

        [Fact]
        public void Gates_RobotsAndFiguresWaitForStep1100_WindowsFor1200()
        {
            var w = Plain(Mission.Tutorial);
            Assert.True(TutorialEngine.FiguresMayMove(w) == false || w.Tutorial.Step >= 1100);
            Assert.True(TutorialEngine.RoomWindowsAllowed(w)); // step 0: not started yet
            w.Tutorial.Step = TutorialStep.Welcome;
            Assert.False(TutorialEngine.FiguresMayMove(w));
            Assert.False(TutorialEngine.RoomWindowsAllowed(w));
            w.Tutorial.Step = TutorialStep.GoToBoss;
            Assert.True(TutorialEngine.FiguresMayMove(w));
            Assert.False(TutorialEngine.RoomWindowsAllowed(w));
            w.Tutorial.Step = TutorialStep.AtBoss;
            Assert.True(TutorialEngine.RoomWindowsAllowed(w));
            w.Tutorial.Step = TutorialStep.GoToOffice;
            Assert.True(TutorialEngine.HallClicksBlocked(w));
            w.Tutorial.Active = false;
            w.Tutorial.Step = TutorialStep.Welcome;
            Assert.True(TutorialEngine.FiguresMayMove(w));
        }

        [Fact]
        public void StepCommand_ChecksTheStep_PublishesTextsAndEnds()
        {
            var w = Plain(Mission.Tutorial);
            var got = new List<TutorialAdvanced>();
            w.Events.Subscribe<TutorialAdvanced>(e => got.Add(e));
            Assert.False(w.Execute(new TutorialStepCommand(TutorialStep.Start, TutorialStep.Welcome, new[] { 1000 })).Ok);
            Assert.True(w.Execute(new TutorialStepCommand(0, TutorialStep.Start, null)).Ok);
            Assert.True(w.Execute(new TutorialStepCommand(TutorialStep.Start, TutorialStep.Welcome, new[] { 1000, 1001 }, monolog: true)).Ok);
            Assert.True(w.Tutorial.Monolog);
            Assert.Equal(new[] { 1000, 1001 }, got[1].Texts);
            Assert.True(w.Execute(new TutorialStepCommand(TutorialStep.Welcome, TutorialStep.Farewell, new[] { 2004 }, monolog: false, end: true)).Ok);
            Assert.False(w.Tutorial.Active);
            Assert.False(w.Execute(new TutorialStepCommand(TutorialStep.Farewell, 1, null)).Ok);
        }

        [Fact]
        public void Cancel_AtNineDuringTheBriefing_StartsTheDay()
        {
            var w = Plain(Mission.Tutorial);
            w.Clock.Time = GameClock.DayStart;
            w.Clock.Frozen = true;
            w.Clock.State = DayState.Morning;
            w.Tutorial.Step = TutorialStep.Briefing;
            Assert.True(w.Execute(new CancelTutorialCommand()).Ok);
            Assert.False(w.Tutorial.Active);
            Assert.Equal(DayState.Playing, w.Clock.State);
            Assert.False(w.Execute(new CancelTutorialCommand()).Ok);
        }

        [Fact]
        public void PlaneSteps_FollowTheLocalAirlineOnly()
        {
            var w = Plain(Mission.Tutorial);
            w.Tutorial.Step = TutorialStep.Waiting;
            TutorialEngine.PlaneReachedGate(w, 1);
            Assert.Equal(TutorialStep.Waiting, w.Tutorial.Step);
            TutorialEngine.PlaneReachedGate(w, 0);
            Assert.Equal(TutorialStep.PlaneAtGate, w.Tutorial.Step);
            TutorialEngine.PlaneLeftGate(w, 0);
            Assert.Equal(TutorialStep.PlaneLeft, w.Tutorial.Step);
            Assert.True(w.Tutorial.Monolog);
        }

        [Fact]
        public void StateIsSaved()
        {
            var w = Plain(Mission.Tutorial);
            w.Tutorial.Step = TutorialStep.OpenOrders;
            w.Tutorial.Monolog = true;
            w.Players[2].BoredOfPlayer = true;
            var f = w.Tutorial;
            Assert.Contains("Tutorial", SaveGame.Dump(w, false));
            Assert.Contains("BoredOfPlayer", SaveGame.Dump(w, false));
            Assert.Equal(TutorialStep.OpenOrders, f.Step);
        }

        [Fact]
        public void RivalBored_SetAndClearedOvernight()
        {
            var w = FinanceFixture.Make();
            Assert.True(w.Execute(new RivalBoredCommand(2)).Ok);
            Assert.True(w.Players[2].BoredOfPlayer);
            w.RunPlayerNewDays();
            Assert.False(w.Players[2].BoredOfPlayer);
        }

        // ------------------------------------------------------------------ with the install (hall)

        static GameData _data;
        static HallData _hall;

        static bool Load()
        {
            if (_data != null) return true;
            var install = GameInstall.TryLocate();
            if (install == null) return false;
            _hall = HallData.Load(install);
            _data = GameData.Load(install);
            HallModule.Register();
            BriefingRecorder.Register();
            HallContent.Attach(_data, _hall);
            return true;
        }

        [Fact]
        public void TutorialFlight_PassengersCheckInWaitAndThePlaneLeaves()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Difficulty = Mission.Tutorial, Seed = 12345 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            Assert.True(w.Tutorial.Active);
            w.Tutorial.Step = TutorialStep.PlaceOrder;
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            var p = w.Local;
            OpenAT.Sim.Contracts.Contract order = null;
            foreach (var c in p.Contracts.Items) if (c.From == w.HomeCity) { order = c; break; }
            Assert.NotNull(order);
            Plane plane = null;
            foreach (var pl in p.Planes.Items) { plane = pl; break; }
            var cmd = new PlaceFlightCommand(0, plane.Id, FlightKind.Contract, order.Id, w.Clock.Date, w.Clock.Hour + 2);
            var r = w.Execute(cmd);
            Assert.True(r.Ok, r.ToString());
            w.Tutorial.Step = TutorialStep.InHall;
            var steps = new List<int>();
            w.Events.Subscribe<TutorialAdvanced>(e => steps.Add(e.To));
            for (int i = 0; i < 200000 && w.Tutorial.Step != TutorialStep.PlaneLeft && w.Clock.Hour < 17; i++) w.Tick();
            foreach (var e in plane.Plan.Entries) if (e.Kind != 0) _out.WriteLine($"entry kind {e.Kind} {e.StartDate}/{e.StartHour} gate {e.Gate} pax {e.Passengers} arrived {e.PArrived}");
            _out.WriteLine("steps " + string.Join(",", steps) + " at " + w.Clock.ClockText);
            Assert.Equal(new[] { TutorialStep.PassengersComing, TutorialStep.CheckingIn, TutorialStep.Waiting, TutorialStep.PlaneAtGate, TutorialStep.PlaneLeft }, steps.ToArray());
        }

        [Fact]
        public void AirportExpansion_DecisionOrder_PaysAndBuildsAtMidnight()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Difficulty = Mission.FreeGame, Seed = 12345 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            // Day 0: the 4 gates are rented (one per airline), but it is too early.
            Assert.Equal(AirportExpansion.TooEarlyText, AirportExpansion.Decide(w));
            w.Players[3].Gates.Clear();
            Assert.Equal(AirportExpansion.FreeGatesText, AirportExpansion.Decide(w));
            for (int g = 0; g < 8; g++) if (!w.Players[0].Gates.HasGate(g)) w.Players[0].Gates.Rent(g, 1000);
            w.Clock.Date = 8;
            Assert.Equal(AirportExpansion.OfferText, AirportExpansion.Decide(w));
            long money = w.Players[0].Money;
            int gates = w.Hall.Layout.GateCount;
            Assert.True(w.Execute(new RequestAirportExpansionCommand(0)).Ok);
            Assert.Equal(money - AirportExpansion.Price, w.Players[0].Money);
            Assert.Equal(AirportExpansion.OrderedText, AirportExpansion.Decide(w));
            w.Execute(new StartWorkingDayCommand());
            for (int i = 0; i < 2_000_000 && w.Clock.Date == 8; i++) w.Tick();
            Assert.False(w.ExpandAirportPending);
            Assert.Equal(2, w.Hall.Layout.Levels[AirportExpansion.CheckInSegment]);
            Assert.Equal(2, w.Hall.Layout.Levels[AirportExpansion.WaitZoneSegment]);
            Assert.Equal(2, w.Hall.Layout.Levels[AirportExpansion.SuitcaseSegment]);
            Assert.Equal(gates + 1, w.Hall.Layout.GateCount);
            Assert.Equal(AirportExpansion.TooSoonText, AirportExpansion.Decide(w));
        }

        [Fact]
        public void Expansion_MaximumOnEasyMissions()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Difficulty = Mission.Passengers, Seed = 1 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            for (int g = 0; g < 8; g++) if (!w.Players[0].Gates.HasGate(g)) w.Players[0].Gates.Rent(g, 1000);
            w.Clock.Date = 10;
            Assert.Equal(AirportExpansion.OfferText, AirportExpansion.Decide(w));
            w.Hall.Layout.Levels[AirportExpansion.CheckInSegment] = 2;
            Assert.Equal(AirportExpansion.MaximumText, AirportExpansion.Decide(w));
        }

        [Fact]
        public void TalkInTheHall_WalkUpToAStandingComputer_RequestsTheDialog()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Difficulty = Mission.FreeGame, Seed = 12345 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            var h = w.Hall;
            // Keep both figures in the hall and standing: player 1 stands near player 0.
            var me = h.PlayerPerson(0);
            var it = h.PlayerPerson(1);
            me.StatePar = 0; it.StatePar = 0;
            h.Players[0].DirectToRoom = 0; h.Players[1].DirectToRoom = 0;
            it.Position = me.Position + new Pt(44 * 4, 0);
            var got = new List<HallRequest>();
            w.Events.Subscribe<HallRequest>(e => { if (e.Kind == HallRequest.TalkToPlayer) got.Add(e); });
            var r = w.Execute(new WalkToPlayerCommand(0, 1));
            _out.WriteLine("walk to player: " + r);
            Assert.True(r.Ok);
            Assert.Equal(1, h.Players[1].TalkPartner);   // the computer waits for player 0
            var pos = it.Position;
            for (int i = 0; i < 3000 && got.Count == 0; i++) w.Tick();
            Assert.Single(got);
            Assert.Equal(0, got[0].Player);
            Assert.Equal(1, got[0].Room);
            Assert.Equal(pos, it.Position);              // it stood still
            Assert.True(w.Execute(new EndTalkCommand(0)).Ok);
            Assert.Equal(0, h.Players[1].TalkPartner);
            Assert.Equal(0, h.Players[0].TalkPartner);
        }

        [Fact]
        public void ToiletPause_HeldThenWalksOut()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Difficulty = Mission.FreeGame, Seed = 12345 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            w.Execute(new ReturnToHallCommand(0));
            Rooms1HallWalkTests.StayInHall(w);
            var flush = new List<HallToiletFlush>();
            w.Events.Subscribe<HallToiletFlush>(e => flush.Add(e));
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.WcF)).Ok);
            var hp = w.Hall.Players[0];
            for (int i = 0; i < 20000 && hp.OnToilet == 0; i++) w.Tick();
            Assert.Equal(RoomIds.WcF, hp.OnToilet);
            var me = w.Hall.PlayerPerson(0);
            var at = me.Position;
            for (int i = 0; i < 60; i++) w.Tick();
            Assert.Equal(at, me.Position);                // held at the door
            for (int i = 0; i < 200 && hp.OnToilet != 0; i++) w.Tick();
            Assert.Equal(0, hp.OnToilet);
            Assert.Equal(2, flush.Count);
            Assert.False(flush[0].Done);
            Assert.True(flush[1].Done);
            for (int i = 0; i < 400; i++) w.Tick();
            Assert.NotEqual(at, me.Position);             // walked out
        }

        [Fact]
        public void WorstAd_SendsASignedLetterToHumans()
        {
            var w = FinanceFixture.Make();
            w.Players[1].Name = "Boss1";
            w.Events.Publish(new OpenAT.Sim.Finance.WorstAdLetter(w.Players[0], w.Players[1], OpenAT.Sim.Finance.AdType.Company, 0));
            w.Events.Publish(new OpenAT.Sim.Finance.WorstAdLetter(w.Players[2], w.Players[1], OpenAT.Sim.Finance.AdType.Company, 0));
            Assert.Equal(1, w.Players[0].Mail.Count);
            var l = w.Players[0].Mail.Letters[0];
            Assert.Equal(9900, l.TextId);
            Assert.Equal(9902, l.SenderTextId);
            Assert.Equal("Air1", l.Args[0]);
            Assert.Equal("Boss1", l.SenderArgs[0]);
            Assert.Equal(0, w.Players[2].Mail.Count); // computers get no letters
        }
    }
}
