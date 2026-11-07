using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Walking right after the morning briefing and computer players inside rooms, against the real hall
    /// (needs the install). rooms-and-ui-9 §5.3-§6.1, airport-and-people.md §6.4, ai-robots.md §3.2/§5.1.
    /// </summary>
    public class Rooms1HallWalkTests
    {
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

        static World NewWorld(int seed = 12345)
        {
            var w = World.NewGame(_data, new NewGameSettings { Seed = seed });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            return w;
        }

        /// <summary>Plays to the given day's briefing and leaves it like the room framework does.</summary>
        static void LeaveBriefingOfDay(World w, int day)
        {
            for (int d = 0; ; d++)
            {
                for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
                Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
                Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
                StayInHall(w);
                if (d == day) return;
                for (int i = 0; i < 100; i++) w.Tick();
                Assert.True(w.Execute(new CallItADayCommand(0)).Ok);
            }
        }

        /// <summary>The human figure walks to its office after the briefing; a click on its own tile cancels that.</summary>
        internal static void StayInHall(World w)
        {
            var me = w.Hall.PlayerPerson(0);
            Assert.True(w.Execute(new WalkToTileCommand(0, HallState.TileOfPosition(me.Position))).Ok);
        }

        /// <summary>
        /// Day-0 start walk (sim-core §2.1): the figures come up the escalator and walk to the manager's office.
        /// The original takes about 13 s from "start" to the briefing (measured live, ~2.5 s of it loading),
        /// i.e. roughly 200-220 ticks of walking at 20 ticks per second; OpenAT must stay in that range.
        /// </summary>
        [Fact]
        public void StartWalk_ReachesTheBossOfficeInAboutTenSeconds()
        {
            if (!Load()) return;
            var w = NewWorld();
            for (int p = 0; p < Player.Count; p++) Assert.True(w.Execute(new WalkToRoomCommand(p, RoomIds.Aufsicht)).Ok);
            var arrived = new int[Player.Count];
            for (int p = 0; p < Player.Count; p++) arrived[p] = -1;
            int t = 0;
            w.Events.Subscribe<RoomRequested>(e => { if (e.Room == RoomIds.Aufsicht && arrived[e.Player] < 0) arrived[e.Player] = t; });
            for (; t < 2000 && System.Array.IndexOf(arrived, -1) >= 0; t++) w.Tick();
            Assert.True(w.Clock.Frozen);                         // the clock stands at 09:00 the whole time
            Assert.InRange(arrived[0], 150, 320);                // ~7.5..16 s at 20 ticks per second
            foreach (int a in arrived) Assert.InRange(a, 0, 400);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void AfterTheBriefing_TheHumanWalksIntoItsOffice(int day)
        {
            if (!Load()) return;
            var w = NewWorld();
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
            if (day > 0)
            {
                LeaveBriefingOfDay(w, 0);
                for (int i = 0; i < 100; i++) w.Tick();
                Assert.True(w.Execute(new CallItADayCommand(0)).Ok);
                for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
            }
            int entered = 0;
            w.Events.Subscribe<RoomRequested>(e => { if (e.Player == 0 && entered == 0) entered = e.Room; });
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            for (int i = 0; i < 6000 && entered == 0; i++) w.Tick();
            Assert.Equal(RoomIds.OfficeA, entered);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void ClickRightAfterTheBriefing_WalksTheFigure(int day)
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, day);
            Assert.Equal(GameClock.DayStart, w.Clock.Time); // still 09:00: the clock has not ticked yet
            var me = w.Hall.PlayerPerson(0);
            var start = me.Position;
            // A floor tile south-east of the figure, on the other floor.
            var click = new Pt(me.ScreenPos.X + 300, me.ScreenPos.Y + 150);
            Assert.True(w.Execute(new WalkToClickCommand(0, click)).Ok);
            var target = w.Hall.Players[0].PrimaryTarget;
            for (int i = 0; i < 3000 && w.Hall.Players[0].WalkActive; i++) w.Tick();
            Assert.NotEqual(start, me.Position);
            Assert.Equal(target, HallState.TileOfPosition(me.Position));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 34)]
        [InlineData(1, 0)]
        [InlineData(1, 34)]
        public void RoomWalkRightAfterTheBriefing_ReachesTheRoom(int day, int delayTicks)
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, day);
            for (int i = 0; i < delayTicks; i++) w.Tick();
            int entered = 0;
            w.Events.Subscribe<RoomRequested>(e => { if (e.Player == 0) entered = e.Room; });
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.Bank)).Ok);
            for (int i = 0; i < 6000 && entered == 0; i++) w.Tick();
            Assert.Equal(RoomIds.Bank, entered);
        }

        [Fact]
        public void ComputerPlayers_StayInTheirRoomForTheWork_ThenLeaveOnTheirOwn()
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, 0);
            var h = w.Hall;
            var enteredAt = new Dictionary<int, (int room, int tick)>();
            var stays = new List<(int player, int room, int ticks)>();
            int tick = 0;
            w.Events.Subscribe<RoomRequested>(e =>
            {
                if (w.Players[e.Player].IsComputer) enteredAt[e.Player] = (e.Room, tick);
            });
            // Nobody sends the robots back to the hall: the Sim AI must do it (no view-side timer).
            for (; tick < 33 * 60 * 3; tick++)
            {
                w.Tick();
                for (int p = 0; p < Player.Count; p++)
                {
                    if (!w.Players[p].IsComputer || !enteredAt.TryGetValue(p, out var e)) continue;
                    var hp = h.Players[p];
                    if (!hp.InHall)
                    {
                        // Inside: the hall knows the room and counts it as occupied (rooms-and-ui-9 §5.4).
                        Assert.Equal(e.room, hp.Room);
                        if (e.room != RoomIds.WcF && e.room != RoomIds.WcM) Assert.True(h.RoomBusy[e.room] >= 1, $"room {e.room} not busy");
                        Assert.Equal(e.room, w.Players[p].Robot.InRoom);
                    }
                    else
                    {
                        stays.Add((p, e.room, tick - e.tick));
                        enteredAt.Remove(p);
                    }
                }
            }
            Assert.NotEmpty(stays);
            // Each robot stays for its own work countdown (ai-robots.md §5.1, ticks; slot 2 works twice as fast; the
            // mechanic sets none, Q11) and comes out by itself; the longer jobs (office 100, agents 140) are seen as such.
            foreach (var s in stays) Assert.True(s.ticks >= 1, $"p{s.player} room {s.room}: {s.ticks} ticks");
            Assert.Contains(stays, s => s.ticks >= 40);
        }

        [Fact]
        public void HumanWalkingIntoARoomARobotOccupies_IsThrownOut()
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, 0);
            var h = w.Hall;
            // Wait until a robot is inside a shared room (not an office), then send the human there.
            int room = 0, robot = -1;
            for (int i = 0; i < 33 * 60 * 3 && room == 0; i++)
            {
                w.Tick();
                for (int p = 1; p < Player.Count; p++)
                {
                    int r = h.Players[p].Room;
                    if (w.Players[p].IsComputer && r >= 80 && r != RoomIds.WcF && r != RoomIds.WcM && r != RoomIds.Airport
                        && w.Execute(new WalkToRoomCommand(0, r)).Ok) { room = r; robot = p; break; }
                }
            }
            if (room == 0) return; // no robot visited a shared room in time (data dependent)
            // Keep the robot inside (as if its work took long) so the arrival is deterministic.
            var thrown = new List<HallRequest>();
            int entered = 0;
            w.Events.Subscribe<HallRequest>(e => { if (e.Player == 0 && e.Kind == HallRequest.ThrownOut) thrown.Add(e); });
            w.Events.Subscribe<RoomRequested>(e => { if (e.Player == 0) entered = e.Room; });
            for (int i = 0; i < 6000 && thrown.Count == 0 && entered == 0; i++)
            {
                w.Players[robot].Robot.WorkCountdown = 1000;
                w.Tick();
            }
            Assert.Equal(0, entered);
            Assert.Contains(thrown, e => e.Room == room);
        }

        /// <summary>
        /// Leaving a room that is only entered as the walk target (rooms-and-ui-9 §5.3) must not walk the figure
        /// straight back in: it stands on the approach rune, and the walk target used to stay set, so the route
        /// box re-opened on every right click.
        /// </summary>
        [Theory]
        [InlineData(RoomIds.RouteBox)]
        [InlineData(RoomIds.Ricks)]
        public void LeavingATargetOnlyRoom_DoesNotReEnterIt(int room)
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, 0);
            int entered = 0;
            w.Events.Subscribe<RoomRequested>(e => { if (e.Player == 0 && e.Room == room) entered++; });
            Assert.True(w.Execute(new WalkToRoomCommand(0, room)).Ok);
            for (int i = 0; i < 6000 && entered == 0; i++) w.Tick();
            Assert.Equal(1, entered);
            for (int i = 0; i < 20; i++) w.Tick();
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            Assert.Equal(0, w.Hall.Players[0].DirectToRoom);
            for (int i = 0; i < 400; i++) w.Tick();
            Assert.Equal(1, entered);
            Assert.Equal(0, w.Hall.Players[0].Room);
        }

        /// <summary>
        /// The cola machine (airport-and-people §8.3, items-security-special §3.3): without the glove the boss gets the
        /// shock (the skeleton animation starts); with the glove only the drink comes out. Both ask the view for the sound.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ColaMachine_ShockOnlyWithoutGlove(bool glove)
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, 0);
            if (glove) Assert.True(w.Players[0].Items.Add(OpenAT.Sim.Items.ItemId.Glove));
            if (!w.Execute(new WalkToRoomCommand(0, RoomIds.Electro)).Ok) return;   // no cola machine at this airport
            int requests = 0;
            w.Events.Subscribe<HallRequest>(e => { if (e.Player == 0 && e.Kind == HallRequest.ColaMachine) requests++; });
            int before = Sum(w.Hall.TriggerSerial);
            for (int i = 0; i < 6000 && requests == 0; i++) w.Tick();
            Assert.Equal(1, requests);
            Assert.Equal(glove ? before : before + 1, Sum(w.Hall.TriggerSerial));
        }

        /// <summary>
        /// Maximum stay (rooms-and-ui-9 §5.5): two game hours in a shared room push the human out (the room framework
        /// closes the room and a laptop above it) and bar the room for two hours; offices have no limit.
        /// </summary>
        [Fact]
        public void StayingTwoHoursInASharedRoom_PushesOutAndForbids()
        {
            if (!Load()) return;
            var w = NewWorld();
            LeaveBriefingOfDay(w, 0);
            var h = w.Hall;
            var hp = h.Players[0];
            var pushed = new List<HallRequest>();
            w.Events.Subscribe<HallRequest>(e => { if (e.Player == 0 && e.Kind == HallRequest.StayedTooLong) pushed.Add(e); });
            hp.Room = RoomIds.Museum;
            int start = w.Clock.Time;
            h.UpdateStayLimit();
            w.Clock.Time = start + HallState.StayLimit - 1;
            h.UpdateStayLimit();
            Assert.Empty(pushed);
            w.Clock.Time = start + HallState.StayLimit;
            h.UpdateStayLimit();
            Assert.Single(pushed);
            Assert.Equal(RoomIds.Museum, pushed[0].Room);
            Assert.Equal(RoomIds.Museum, hp.LocationForbidden);
            // Back in the hall: the bar lifts two hours later.
            hp.Room = 0;
            w.Clock.Time = start + 2 * HallState.StayLimit - 1;
            h.UpdateStayLimit();
            Assert.Equal(RoomIds.Museum, hp.LocationForbidden);
            w.Clock.Time = start + 2 * HallState.StayLimit;
            h.UpdateStayLimit();
            Assert.Equal(0, hp.LocationForbidden);
            // The own office has no limit.
            hp.Room = RoomIds.OfficeA;
            h.UpdateStayLimit();
            w.Clock.Time += 3 * HallState.StayLimit;
            h.UpdateStayLimit();
            Assert.Single(pushed);
        }

        static int Sum(int[] a) { int s = 0; foreach (int v in a) s += v; return s; }
    }
}
