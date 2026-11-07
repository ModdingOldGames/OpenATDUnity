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
    /// <summary>The room framework's day flow against the real hall (needs the install).</summary>
    public class Rooms1HallFlowTests
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

        static void Run(World w, Func<bool> until, int max)
        {
            for (int i = 0; i < max && !until(); i++) w.Tick();
        }

        [Fact]
        public void Briefing_ThenWalkIntoTheBank_ThenBackToTheHall()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { Seed = 12345 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            var h = w.Hall;
            Assert.Equal(DayState.Morning, w.Clock.State);
            Run(w, () => false, 20); // the clock stands at 09:00 during the briefing
            Assert.Equal(GameClock.DayStart, w.Clock.Time);

            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok); // the framework left the boss office
            Rooms1HallWalkTests.StayInHall(w); // a click on the spot cancels the walk to the office
            var got = new List<RoomRequested>();
            w.Events.Subscribe<RoomRequested>(e => got.Add(e));
            Run(w, () => w.Clock.Time > GameClock.DayStart + GameClock.UnitsPerMinute, 1000);
            Run(w, () => false, 500); // standing long enough for the idle gimmick (LookDir 9)
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.Bank)).Ok);
            Run(w, () => got.Exists(e => e.Player == 0), 8000);
            Assert.Contains(got, e => e.Player == 0 && e.Room == RoomIds.Bank);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            Run(w, () => false, 200);
            Assert.True(h.Players[0].InHall);
        }
    }
}
