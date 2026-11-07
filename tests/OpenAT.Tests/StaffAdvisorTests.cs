using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Staff;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Staff and advisors (staff-and-advisors.md §4, §6, §9): salaries per plane, strike notices, advisor remarks.</summary>
    public class StaffAdvisorTests
    {
        static Worker Add(World w, int type, int talent, int salary, int employer, int sex = 0)
        {
            var wk = new Worker { Name = "W" + w.Workers.Count, Type = type, Talent = talent, Salary = salary, OriginalSalary = salary, Employer = employer, Sex = sex };
            w.Workers.Workers.Add(wk);
            return wk;
        }

        static List<AdvisorMessage> Capture(World w)
        {
            var l = new List<AdvisorMessage>();
            w.Events.Subscribe<AdvisorMessage>(l.Add);
            return l;
        }

        static (World w, ListOperationsBridge b) WithPlanes(params (int id, string name, int cond)[] planes)
        {
            var b = new ListOperationsBridge();
            foreach (var p in planes) b.PlaneLists[0].Add(new PlaneSnapshot { Id = p.id, Name = p.name, Condition = p.cond, RequiredPilots = 1, RequiredAttendants = 1, MaxAttendants = 1 });
            return (FinanceFixture.Make(bridge: b), b);
        }

        [Fact]
        public void Salaries_ChargedToTheAssignedPlanesBalance()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Owner = Owner.Human;
            var pl = OpsWorld.AddPlane(w, p);
            Add(w, WorkerType.Pilot, 50, 6000, 0);
            Add(w, WorkerType.Attendant, 50, 4500, 0);
            Add(w, (int)AdvisorType.Kerosene, 50, 9000, 0); // advisors are paid but belong to no plane
            p.MapWorkers(false);
            for (int i = 0; i < 7; i++) pl.Balances[i] = 0;
            long m = p.Money;
            p.BookSalaries();
            Assert.Equal(-(6000 / 30 + 4500 / 30), pl.Balances[0]);
            Assert.Equal(m - 19500 / 30, p.Money);
        }

        [Fact]
        public void StrikeStart_ToldOnceFrom0910_WithLetter508()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            var letters = new List<LetterSent>();
            w.Events.Subscribe<LetterSent>(letters.Add);
            p.StrikeHours = 7;
            w.Clock.Time = GameClock.At(9, 5);
            Assert.False(StrikeStartNotifiedCommand.Due(w, p));
            w.Clock.Time = GameClock.At(9, 11);
            Assert.True(w.Execute(new StrikeStartNotifiedCommand(0)).Ok);
            Assert.True(p.StrikeNotified);
            Assert.Single(letters);
            Assert.Equal(508, letters[0].TextId);
            Assert.Equal(7, letters[0].Args[0]);
            Assert.False(w.Execute(new StrikeStartNotifiedCommand(0)).Ok); // once
            w.Clock.Time = GameClock.At(18, 0);
            p.StrikeNotified = false;
            Assert.False(StrikeStartNotifiedCommand.Due(w, p));
            // A computer airline is marked but gets no letter.
            var c = w.Players[1];
            c.StrikeHours = 3;
            w.Clock.Time = GameClock.At(10, 0);
            Assert.True(w.Execute(new StrikeStartNotifiedCommand(1)).Ok);
            Assert.Single(letters);
        }

        [Fact]
        public void BribeDrunk_NeedsHorseshoeTrust_CountdownTenAndTakesOver()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.StrikeHours = 5;
            Assert.False(w.Execute(new EndStrikeCommand(0, StrikeEndOption.BribeDrunk)).Ok);
            p.TrinkerTrust = true;
            Assert.True(w.Execute(new EndStrikeCommand(0, StrikeEndOption.Threaten)).Ok);
            Assert.Equal(4, p.StrikeEndCountdown);
            // The drinker takes over a running negotiation (as the original).
            Assert.True(w.Execute(new EndStrikeCommand(0, StrikeEndOption.BribeDrunk)).Ok);
            Assert.Equal(10, p.StrikeEndCountdown);
            Assert.Equal(3, p.StrikeEndType);
            Assert.False(w.Execute(new EndStrikeCommand(0, StrikeEndOption.BribeDrunk)).Ok); // already on it
            Assert.True(w.Execute(new OpenAT.Sim.Items.DrinkerHurriesStrikeEndCommand(0)).Ok);
            Assert.Equal(4, p.StrikeEndCountdown);
        }

        [Fact]
        public void PersonnelRemark_ToleranceAndOrder()
        {
            var (w, _) = WithPlanes((1, "A", 100));
            var p = w.Players[0];
            p.PilotSurplus = 3; p.AttendantSurplus = 5;
            Assert.Null(p.PersonnelAdvisorRemark(3)); // no advisor
            Add(w, (int)AdvisorType.Personnel, 40, 9000, 0);
            var m = p.PersonnelAdvisorRemark(3);
            Assert.Equal(1101, m.TextId);
            Assert.True(m.Comment);
            Assert.Equal(1100, p.PersonnelAdvisorRemark(1).TextId);
            p.PilotSurplus = -2; p.AttendantSurplus = -1;
            m = p.PersonnelAdvisorRemark(3);
            Assert.Equal(1104, m.TextId);
            Assert.Equal(new object[] { 2, 1 }, m.Args);
            p.AttendantSurplus = 0;
            Assert.Equal(1102, p.PersonnelAdvisorRemark(3).TextId);
            p.PilotSurplus = 0; p.AttendantSurplus = -1;
            Assert.Equal(1103, p.PersonnelAdvisorRemark(3).TextId);
            p.AttendantSurplus = 0;
            Assert.Null(p.PersonnelAdvisorRemark(3));
        }

        [Fact]
        public void PersonnelRemark_AfterSellingAPlane()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Owner = Owner.Human;
            var pl = OpsWorld.AddPlane(w, p);
            Add(w, (int)AdvisorType.Personnel, 40, 9000, 0);
            Add(w, WorkerType.Pilot, 50, 6000, 0);
            Add(w, WorkerType.Pilot, 50, 6000, 0);
            p.MapWorkers(false);
            var msgs = Capture(w);
            Assert.True(w.Execute(new SellPlaneCommand(0, pl.Id) { IgnoreOpeningHours = true }).Ok);
            Assert.Contains(msgs, m => m.TextId == 1100 && (int)m.Args[0] == 2 && m.Comment);
        }

        [Fact]
        public void Chatter_Timing()
        {
            var w = FinanceFixture.Make();
            w.Clock.State = DayState.Playing;
            w.Clock.Frozen = false;
            w.Clock.Time = AdvisorChatter.IntervalUnits * 36; // 09:12:57
            Assert.True(AdvisorChatter.IsDue(w));
            w.Clock.Time += GameClock.UnitsPerTick;
            Assert.False(AdvisorChatter.IsDue(w));
            w.Clock.Time = AdvisorChatter.IntervalUnits * 36;
            w.CallItADay = true;
            Assert.False(AdvisorChatter.IsDue(w));
            w.CallItADay = false;
            w.Clock.Time = AdvisorChatter.IntervalUnits * 30; // before 09:00
            Assert.False(AdvisorChatter.IsDue(w));
        }

        [Fact]
        public void Chatter_PersonnelWarnings()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            var msgs = Capture(w);
            Add(w, (int)AdvisorType.Personnel, 40, 9000, 0);
            AdvisorChatter.Speak(w, p, 0);
            Assert.Empty(msgs);
            var a = Add(w, WorkerType.Attendant, 50, 4000, 0, sex: 0);
            var b = Add(w, WorkerType.Pilot, 50, 4000, 0, sex: 1);
            a.Happiness = -50; b.Happiness = -41;
            AdvisorChatter.Speak(w, p, 1); // kerosene picked, not hired: silent
            Assert.Empty(msgs);
            AdvisorChatter.Speak(w, p, 0);
            Assert.Equal(2211, msgs[0].TextId); // the last unwarned one: male pilot
            Assert.Equal(b.Name, msgs[0].Args[0]);
            AdvisorChatter.Speak(w, p, 0);
            Assert.Equal(2202, msgs[1].TextId); // then the female attendant
            AdvisorChatter.Speak(w, p, 0);
            Assert.Equal(2, msgs.Count); // all warned today

            for (int i = 0; i < 10; i++) Add(w, WorkerType.Pilot, 50, 4000, 0).Happiness = -60;
            AdvisorChatter.Speak(w, p, 0);
            Assert.Equal(2299, msgs[2].TextId); // 12 unhappy: one group warning, all marked
            AdvisorChatter.Speak(w, p, 0);
            Assert.Equal(3, msgs.Count);
        }

        [Fact]
        public void Chatter_KeroseneTips()
        {
            var (w, _) = WithPlanes((1, "A", 100));
            var p = w.Players[0];
            var msgs = Capture(w);
            Add(w, (int)AdvisorType.Kerosene, 40, 9000, 0);
            p.Money = 100000;
            p.Tank = 100;
            w.Kerosene = 650; p.TankOpen = false; p.TankContents = 10;
            AdvisorChatter.Speak(w, p, 1);
            Assert.Equal(3011, msgs[0].TextId);
            w.Kerosene = 350; p.TankContents = 60;
            AdvisorChatter.Speak(w, p, 1);
            Assert.Equal(3012, msgs[1].TextId); // capacity 100 < 4000 × 1 plane
            p.TankContents = 20;
            w.Kerosene = 460;
            int[] hist = { 0, 0, 0, 0, 0, 520, 500, 480, 450, 460 };
            for (int i = 0; i < 10; i++) w.KerosenePast[i] = hist[i];
            AdvisorChatter.Speak(w, p, 1);
            Assert.Equal(3010, msgs[2].TextId);
            p.TankContents = 40; // a third or more: no tip
            AdvisorChatter.Speak(w, p, 1);
            Assert.Equal(3, msgs.Count);
        }

        [Fact]
        public void Chatter_PlaneAnalystWearWarnings()
        {
            var (w, b) = WithPlanes((1, "Alpha", 20), (2, "Bravo", 29), (3, "Charlie", 50));
            var p = w.Players[0];
            var msgs = Capture(w);
            Add(w, (int)AdvisorType.PlaneAnalyst, 40, 9000, 0);
            p.MechMode = 0;
            AdvisorChatter.Speak(w, p, 2);
            Assert.Equal(4011, msgs[0].TextId);
            Assert.Equal("Alpha, Bravo", msgs[0].Args[0]);
            Assert.Equal(4030, msgs[0].AppendTextId);
            p.MechMode = 3;
            AdvisorChatter.Speak(w, p, 2);
            Assert.Single(msgs);
            b.PlaneLists[0].RemoveRange(0, 2);
            p.MechMode = 1;
            AdvisorChatter.Speak(w, p, 2);
            Assert.Equal(4020, msgs[1].TextId);
            Assert.Equal("Charlie", msgs[1].Args[0]);
            Assert.Equal(4031, msgs[1].AppendTextId);
            p.MechMode = 2;
            AdvisorChatter.Speak(w, p, 2);
            Assert.Equal(0, msgs[2].AppendTextId);
        }

        [Fact]
        public void Informer_RivalMoves()
        {
            var w = FinanceFixture.Make();
            var me = w.Players[0];
            var rival = w.Players[1];
            var msgs = Capture(w);
            var slip = w.Auctions.Gates[0];
            slip.ObjectId = 3; slip.Player = 0; slip.Price = 1000;
            Assert.True(AuctionBoard.Bid(w, rival, slip));
            Assert.Empty(msgs); // no informer
            Add(w, (int)AdvisorType.Informer, 40, 9000, 0);
            slip.Player = 0;
            Assert.True(AuctionBoard.Bid(w, rival, slip));
            Assert.Equal(9001, msgs[0].TextId);
            Assert.Equal(rival.Airline, msgs[0].Args[1]);
            // The human outbidding a computer is no news.
            Assert.True(AuctionBoard.Bid(w, me, slip));
            Assert.Single(msgs);

            w.Events.Publish(new RivalMove { Rival = rival, Kind = RivalMoveKind.IssuedShares, Count = 5000 });
            Assert.Equal(9004, msgs[1].TextId);
            w.Events.Publish(new PlaneTraded { Player = rival, Price = 1234567, Source = "museum" });
            Assert.Equal(9000, msgs[2].TextId);
            Assert.Equal(1234567L, msgs[2].Args[2]);
            w.Events.Publish(new PlaneTraded { Player = me, Price = 1, Source = "museum" });
            Assert.Equal(3, msgs.Count);

            rival.Money = 10000000;
            me.SharesIssued = 100000; me.SharePrice = 50;
            long n = RobotShortcuts.BuyFreeFloat(w, rival, me, false);
            Assert.True(n > 0);
            Assert.Equal(9005, msgs[3].TextId);
            Assert.Equal(n, msgs[3].Args[2]);
        }

        [Fact]
        public void Kerosene_EntryRemarkKeepsTheTankTest_AndGradeSwitchesOnChoice()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.Money = 100000;
            w.Kerosene = 350;
            Assert.Equal(0, p.KeroseneEntryRemark()); // no advisor
            Add(w, (int)AdvisorType.Kerosene, 40, 9000, 0);
            Assert.Equal(3000, p.KeroseneEntryRemark());
            p.Tank = 0; p.TankContents = 0;
            Assert.Equal(0, p.KeroseneEntryRemark()); // no tank, no "buy now"
            w.Kerosene = 650;
            Assert.Equal(3001, p.KeroseneEntryRemark());

            Assert.True(w.Execute(new ChooseKeroseneCommand(0, KeroseneKind.Cheap)).Ok);
            Assert.Equal(KeroseneKind.Cheap, p.KeroseneKind);
        }

        static GameData _data;
        static HallData _hall;

        [Fact]
        public void FitnessTrainer_SetsTheHallWalkSpeed()
        {
            if (_data == null)
            {
                var install = GameInstall.TryLocate();
                if (install == null) return;
                _hall = HallData.Load(install);
                _data = GameData.Load(install);
                HallModule.Register();
                HallContent.Attach(_data, _hall);
            }
            var w = World.NewGame(_data, new NewGameSettings { Seed = 4711 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            Assert.Equal(3, w.Hall.Players[0].WalkSpeed);
            var t = Add(w, (int)AdvisorType.Fitness, 75, 9000, WorkerType.Jobless);
            Assert.True(w.Execute(new HireWorkerCommand(0, w.Workers.Workers.IndexOf(t))).Ok);
            Assert.Equal(4, w.Hall.Players[0].WalkSpeed);
            Assert.True(w.Execute(new FireWorkerCommand(0, w.Workers.Workers.IndexOf(t))).Ok);
            Assert.Equal(3, w.Hall.Players[0].WalkSpeed);
            // Computer airlines have no trainers.
            w.Players[1].UpdateWalkSpeed();
            Assert.Equal(3, w.Hall.Players[1].WalkSpeed);
        }
    }
}
