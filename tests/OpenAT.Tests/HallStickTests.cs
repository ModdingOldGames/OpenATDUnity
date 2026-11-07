using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Stick walking (OpenAT, GameOptions.GamepadSupport) against the real hall (needs the install).</summary>
    public class HallStickTests
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

        /// <summary>A world on day 0 with the human figure standing in the hall after the briefing.</summary>
        static World InHall(bool gamepad = true)
        {
            var w = World.NewGame(_data, new NewGameSettings { Seed = 777 }, new GameOptions { GamepadSupport = gamepad });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            Rooms1HallWalkTests.StayInHall(w);
            for (int i = 0; i < 40; i++) w.Tick();
            return w;
        }

        /// <summary>Holds the stick for some ticks (one command per tick, as the view sends one per frame).</summary>
        static void Hold(World w, int x, int y, int tilt, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                w.Execute(new StickWalkCommand(0, x, y, tilt));
                w.Tick();
            }
        }

        static int Moved(int tilt, int x)
        {
            var w = InHall();
            int x0 = w.Hall.PlayerPerson(0).Position.X;
            Hold(w, x, 0, tilt, 40);
            return System.Math.Abs(w.Hall.PlayerPerson(0).Position.X - x0);
        }

        [Fact]
        public void PaceFollowsTheTilt()
        {
            Assert.Equal(0, StickWalkCommand.PaceFor(0));
            Assert.Equal(StickWalkCommand.MinPace, StickWalkCommand.PaceFor(1));
            Assert.Equal(100, StickWalkCommand.PaceFor(StickWalkCommand.WalkTilt));
            Assert.Equal(StickWalkCommand.MaxPace, StickWalkCommand.PaceFor(1000));
            for (int t = 1; t < 1000; t += 37) Assert.True(StickWalkCommand.PaceFor(t + 37) >= StickWalkCommand.PaceFor(t));
        }

        [Fact]
        public void PushingTheStickFurtherWalksFaster()
        {
            if (!Load()) return;
            int slow = Moved(250, 1), walk = Moved(StickWalkCommand.WalkTilt, 1), jog = Moved(800, 1), run = Moved(1000, 1);
            Assert.True(slow > 0, "a slight push walks");
            Assert.True(walk > slow, $"walk {walk} > slow {slow}");
            Assert.True(jog > walk, $"jog {jog} > walk {walk}");
            Assert.True(run > jog, $"run {run} > jog {jog}");
        }

        [Fact]
        public void TheStickDirectionIsTheWalkingDirection()
        {
            if (!Load()) return;
            var w = InHall();
            int x0 = w.Hall.PlayerPerson(0).Position.X;
            Hold(w, 1, 0, 800, 20);
            int x1 = w.Hall.PlayerPerson(0).Position.X;
            Assert.True(x1 > x0, $"right: {x0} -> {x1}");
        }

        [Fact]
        public void ReleasingTheStickStops()
        {
            if (!Load()) return;
            var w = InHall();
            Hold(w, 1, 0, 900, 30);
            Assert.True(w.Execute(new StickWalkCommand(0, 0, 0, 0)).Ok);
            for (int i = 0; i < 30; i++) w.Tick();
            var at = w.Hall.PlayerPerson(0).Position;
            for (int i = 0; i < 30; i++) w.Tick();
            Assert.Equal(at, w.Hall.PlayerPerson(0).Position);
            Assert.Equal(0, w.Hall.Players[0].StickPace);
            Assert.False(w.Execute(new StickWalkCommand(0, 0, 0, 0)).Ok);   // nothing to stop
        }

        [Fact]
        public void AClickEndsStickWalking()
        {
            if (!Load()) return;
            var w = InHall();
            Hold(w, 1, 0, 300, 10);
            var me = w.Hall.PlayerPerson(0);
            Assert.True(w.Execute(new WalkToTileCommand(0, HallState.TileOfPosition(me.Position))).Ok);
            Assert.True(w.Execute(new WalkToClickCommand(0, new Pt(me.Position.X, 300))).Ok);
            Assert.Equal(0, w.Hall.Players[0].StickPace);
        }

        [Fact]
        public void TheOptionSwitchesItOff()
        {
            if (!Load()) return;
            var w = InHall(gamepad: false);
            Assert.False(w.Execute(new StickWalkCommand(0, 1, 0, 900)).Ok);
        }

        [Fact]
        public void StickWalkingIsDeterministic()
        {
            if (!Load()) return;
            var a = InHall();
            var b = InHall();
            foreach (var w in new[] { a, b })
            {
                Hold(w, 1, 1, 600, 25);
                Hold(w, -1, 0, 1000, 25);
                Hold(w, 0, -1, 200, 25);
            }
            Assert.Equal(a.Hall.PlayerPerson(0).Position, b.Hall.PlayerPerson(0).Position);
        }
    }
}
