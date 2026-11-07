using System.Linq;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using Xunit;

namespace OpenAT.Tests
{
    public class WorldTests
    {
        static GameData Data() => GameInstall.TryLocate() is GameInstall i ? GameData.Load(i) : null;

        [Fact]
        public void Data_LoadsOriginalTables()
        {
            var d = Data();
            if (d == null) return;
            Assert.Equal(75, d.Cities.Count);
            Assert.Equal(26, d.PlaneTypes.Count);
            Assert.Equal(350, d.Routes.Count);
            var b737 = d.PlaneTypeByCsvId(100);
            Assert.NotNull(b737);
            Assert.Equal(29, d.PlaneTypes.All[0].Index);
            Assert.True(b737.Seats > 100 && b737.Speed > 500);
            // routes sorted by departure city name
            for (int i = 1; i < d.Routes.Count; i++)
                Assert.True(string.CompareOrdinal(d.Cities[d.Routes[i - 1].FromCity].Name, d.Cities[d.Routes[i].FromCity].Name) <= 0);
        }

        sealed class Recorder : SimSystem
        {
            public readonly System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();
            public override string Name => "test.recorder";
            public override int Order => 5000;
            public override void OnHour(World w) => Log.Add("h" + w.Clock.Hour);
            public override void OnNewDay(World w) => Log.Add("nd");
            public override void OnPlayerNewDay(World w, Player p) => Log.Add("p" + p.Index);
            public override void OnMorning(World w) => Log.Add("m" + w.Clock.Date);
            public override void OnEvening(World w) => Log.Add("e");
        }

        [Fact]
        public void Clock_RunsADayWithHooksInOrder()
        {
            var w = new World(new GameData());
            var rec = new Recorder();
            w.Systems.Add(rec);
            w.Clock.InitCalendar(0);
            w.StartWorkingDay();
            int guard = 0;
            while (!(w.Clock.Date == 1 && w.Clock.State == DayState.Morning) && guard++ < 100000) w.Tick();
            Assert.Equal(1, w.Clock.Date);
            Assert.True(w.Clock.Frozen);
            Assert.Equal(GameClock.DayStart, w.Clock.Time);
            var log = string.Join(",", rec.Log);
            Assert.StartsWith("h10,h11,h12,h13,h14,h15,h16,h17,h18,e,h19", log);
            Assert.Contains("h23,p0,p1,p2,p3,nd,h0,h1", log);
            Assert.EndsWith("h9,m1", log);
            // 24h of ticks at 30 units each
            Assert.Equal(GameClock.UnitsPerDay / GameClock.UnitsPerTick, (int)w.Clock.TicksPlayed);
        }

        [Fact]
        public void NewGame_HumansFindTheWelcomeLetter()
        {
            var data = Data();
            if (data == null) return;
            var w = World.NewGame(data, new NewGameSettings { Seed = 1 });
            var l = Assert.Single(w.Players[w.LocalPlayer].Mail.Letters);
            Assert.Equal("Lett", l.Group);
            Assert.Equal(1500, l.TextId);
            Assert.Equal(1502, l.SenderTextId);
            for (int c = 0; c < Player.Count; c++) if (c != w.LocalPlayer) Assert.Equal(0, w.Players[c].Mail.Count);
            var r = World.NewGame(data, new NewGameSettings { Seed = 1, Difficulty = Mission.Rocket });
            Assert.Equal(2, r.Players[r.LocalPlayer].Mail.Count);
        }

        [Fact]
        public void NewGame_HumanStartingPlanesHaveTheirCrewOnBoard()
        {
            var d = Data();
            if (d == null) return;
            var w = World.NewGame(d, new NewGameSettings { Seed = 3 });
            var p = w.Players[w.LocalPlayer];
            Assert.NotEmpty(p.Planes.Items);
            foreach (var pl in p.Planes.Items)
            {
                Assert.Equal(pl.Pilots, pl.CrewPilots);
                Assert.Equal(pl.Attendants, pl.CrewAttendants);
            }
        }

        [Fact]
        public void NewGame_RecapitalizesNames()
        {
            Assert.Equal("Abc De-Fg", World.Recapitalize("ABC DE-FG"));
            var d = Data();
            if (d == null) return;
            var w = World.NewGame(d, new NewGameSettings { PlayerNames = new[] { "MAX MUSTER", null, null, null }, AirlineNames = new[] { "SKY-HIGH AIR", null, null, null } });
            Assert.Equal("Max Muster", w.Players[0].Name);
            Assert.Equal("Sky-High Air", w.Players[0].Airline);
        }

        [Fact]
        public void NewGame_SetsUpAirlines()
        {
            var d = Data();
            if (d == null) return;
            var w = World.NewGame(d, new NewGameSettings { Difficulty = Mission.FreeGame, LocalPlayer = 1 });
            Assert.True(w.Players[1].IsHuman);
            Assert.True(w.Players[0].IsComputer);
            Assert.False(string.IsNullOrEmpty(w.Players[0].Airline));
            Assert.NotEqual(0, w.HomeCity);
            Assert.Equal(0, w.Clock.Date);
            Assert.True(w.Clock.Frozen);
        }

        [Fact]
        public void Calendar_Weekday()
        {
            Assert.Equal(4, GameClock.SundayBasedWeekday(0)); // 1970-01-01 Thursday
            var c = new GameClock();
            c.InitCalendar(0);
            Assert.Equal(3, c.StartWeekday); // Monday-based: Thursday = 3
        }
    }
}
