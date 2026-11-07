using System;
using System.Collections.Generic;
using System.Linq;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Small synthetic world for operations unit tests (no install needed).</summary>
    public static class OpsWorld
    {
        public static OpenAT.Sim.Data.City MakeCity(int csvId, string name, int lon, int lat, int area = 1, int pop = 1000000, int rent = 30000) =>
            new OpenAT.Sim.Data.City { Id = csvId + OpenAT.Sim.Data.City.IdBase, Name = name, ShortCode = name.Substring(0, 3).ToUpperInvariant(), AreaCode = area, GlobeX = lon, GlobeY = lat, Population = pop, OfficeRent = rent };

        /// <summary>Cities on the equator 9° apart (≈ 1001 km), plus one far away.</summary>
        public static GameData Data()
        {
            var d = new GameData();
            var cities = new[]
            {
                MakeCity(1, "Alpha", 0, 0, 1, 2000000), MakeCity(2, "Bravo", 9, 0, 1, 1000000), MakeCity(3, "Charlie", 18, 0, 1, 500000),
                MakeCity(4, "Delta", 27, 0, 2, 800000), MakeCity(5, "Echo", 120, 0, 3, 3000000),
                MakeCity(6, "Foxtrot", 30, 5, 2, 700000), MakeCity(7, "Golf", 125, 5, 3, 900000),
                MakeCity(8, "Hotel", -20, 10, 4, 600000), MakeCity(9, "India", -25, 12, 4, 400000),
            };
            for (int i = 0; i < cities.Length; i++) { cities[i].Index = i; d.Cities.Register(cities[i]); }
            d.PlaneTypes.Register(new PlaneType { Id = 100 + PlaneType.IdBase, Index = 29, Manufacturer = "Boeing", Name = "737", FirstYearBuilt = 1988, Seats = 168, Speed = 815, Range = 3870, Pilots = 2, Attendants = 3, FuelBurn = 3050, Price = 41000000, MaintenanceFactor = 1 });
            d.PlaneTypes.Register(new PlaneType { Id = 105 + PlaneType.IdBase, Index = 28, Manufacturer = "Boeing", Name = "757", FirstYearBuilt = 1998, Seats = 240, Speed = 890, Range = 6455, Pilots = 2, Attendants = 5, FuelBurn = 4194, Price = 69000000, MaintenanceFactor = 1 });
            d.PlaneTypes.Register(new PlaneType { Id = 114 + PlaneType.IdBase, Index = 27, Manufacturer = "Tupolev", Name = "Tu 154", FirstYearBuilt = 1972, Seats = 180, Speed = 900, Range = 3000, Pilots = 2, Attendants = 3, FuelBurn = 9600, Price = 10440000, MaintenanceFactor = 1 });
            int id = RouteDef.IdBase;
            void Pair(int a, int b, int rent, double f)
            {
                d.Routes.Add(new RouteDef { Id = id++, Level = 1, FromCity = a + OpenAT.Sim.Data.City.IdBase, ToCity = b + OpenAT.Sim.Data.City.IdBase, Rent = rent, Factor = f });
                d.Routes.Add(new RouteDef { Id = id++, Level = 1, FromCity = b + OpenAT.Sim.Data.City.IdBase, ToCity = a + OpenAT.Sim.Data.City.IdBase, Rent = rent, Factor = f });
            }
            Pair(1, 2, 30000, 1.0);
            Pair(1, 3, 30000, 1.0);
            Pair(2, 3, 30000, 1.0);
            Pair(3, 4, 30000, 1.0);
            for (int i = 0; i < d.Routes.Count; i++) d.Routes[i].Index = i;
            foreach (var n in new[] { "zero", "Spirit", "Hope", "Glory", "Pride", "Star", "Comet", "Falcon" }) d.PlaneNamesPreferred.Add(n);
            foreach (var n in new[] { "zero2", "Second", "Third" }) d.PlaneNamesFallback.Add(n);
            return d;
        }

        public static int C(int csvId) => csvId + OpenAT.Sim.Data.City.IdBase;

        public static World NewWorld(int difficulty = Mission.FreeGame)
        {
            var w = new World(Data(), new GameOptions { RandomStartDay = false });
            w.Difficulty = difficulty;
            w.HomeCity = C(1);
            w.Clock.InitCalendar(0);
            w.Clock.Time = GameClock.DayStart;
            w.EnsureOperationsArrays();
            RouteOps.UpdateDemand(w);
            return w;
        }

        /// <summary>Adds a plane of the given type to an airline without charging it.</summary>
        public static Plane AddPlane(World w, Player p, int typeCsv = 100)
        {
            var pl = PlaneMarket.BuyNew(w, p, w.Data.PlaneTypeByCsvId(typeCsv), new TeakRand(w.Clock.Date), free: true);
            pl.CrewPilots = pl.Pilots;
            pl.CrewAttendants = pl.Attendants;
            return pl;
        }

        /// <summary>Uses the stand-alone crew rule (plane fields) so tests can set crews directly.</summary>
        public static void UsePlaneCrew(World w) => w.Rules.Set<ICrewMapping>(new CountCrewMapping());

        public static void RunUntil(World w, int date, int hour, int minute = 0)
        {
            int guard = 0;
            while ((w.Clock.Date < date || (w.Clock.Date == date && w.Clock.Time < GameClock.At(hour, minute))) && guard++ < 10000000)
            {
                if (w.Clock.Frozen) w.StartWorkingDay();
                w.Tick();
            }
        }
    }

    public class OperationsTests
    {
        static GameData InstallData() => GameInstall.TryLocate() is GameInstall i ? GameData.Load(i) : null;

        // ------------------------------------------------------------------ cities (contracts-and-routes.md §1)

        [Fact]
        public void Distance_GreatCircleOnTheEquator()
        {
            var d = OpsWorld.Data();
            // 9° of longitude: trunc(9 · 40 040 174 / 360) with the original's float steps
            int dist = CityGeo.Distance(d.Cities[OpsWorld.C(1)], d.Cities[OpsWorld.C(2)]);
            Assert.InRange(dist, 1000990, 1001020);
            Assert.Equal(0, CityGeo.Distance(d.Cities[OpsWorld.C(1)], d.Cities[OpsWorld.C(1)]));
            // 120° = 40 040 174 / 3 ≈ 13 346 724 m, under the cap
            Assert.InRange(CityGeo.Distance(d.Cities[OpsWorld.C(1)], d.Cities[OpsWorld.C(5)]), 13346600, 13346800);
        }

        [Fact]
        public void FlightHours_CeilPlusOneMinimumTwo()
        {
            // 1 001 004 m at 815 km/h: 1228 → ceil(1.228) = 2, +1 = 3 hours
            Assert.Equal(3, CityGeo.FlightHours(1001004, 815));
            // short hop: (300000/815 + 999)/1000 + 1 = 1 + 1 = 2
            Assert.Equal(2, CityGeo.FlightHours(300000, 815));
            Assert.Equal(2, CityGeo.FlightHours(0, 815));
            // 16 440 km at 555 km/h: 29 622 → 30 + 1 = 31 (≥ 24: not plannable)
            Assert.Equal(31, CityGeo.FlightHours(16440000, 555));
        }

        [Fact]
        public void RandomCity_WalksFromTheHighestIndexDown()
        {
            var d = OpsWorld.Data();
            var r1 = new TeakRand(77);
            int n = d.Cities.Count;
            int k = new TeakRand(77).Rand(n) + 1;
            int id = CityGeo.RandomCity(d.Cities, r1);
            Assert.Equal(d.Cities.All[n - k].Id, id);
            // area filter: only area-1 cities (indices 0..2)
            for (int s = 0; s < 50; s++) Assert.Equal(1, d.Cities[CityGeo.RandomCity(d.Cities, new TeakRand(s), 1)].AreaCode);
        }

        // ------------------------------------------------------------------ installed data

        [Fact]
        public void InstallRoutes_LengthsWithinSpecRange()
        {
            var d = InstallData();
            if (d == null) return;
            var w = new World(d);
            int min = int.MaxValue, max = 0;
            foreach (var r in w.RouteTable) { int x = w.Distance(r.From, r.To); min = Math.Min(min, x); max = Math.Max(max, x); }
            // contracts-and-routes.md §1.2: route lengths 222 km .. 16 440 km
            Assert.InRange(min / 1000, 220, 224);
            Assert.Equal(16440, max / 1000);
        }

        [Fact]
        public void InstallDesigner_CatalogueAndPlnLoad()
        {
            var inst = GameInstall.TryLocate();
            if (inst == null) return;
            var cat = PlaneDesignCatalog.Load(inst);
            Assert.Equal(37, cat.Parts.Count);
            Assert.Equal(307, cat.Relations.Count);
            var d = PlaneDesign.ReadPln(inst.ReadAllBytes("misc/pl45.pln"));
            Assert.Equal("Miss5solver", d.Name);
            Assert.Equal(60100000, d.Stats(cat).Cost);
            Assert.True(d.IsBuildable(cat));
        }

        // ------------------------------------------------------------------ headless game

        static void RunGame(int difficulty, int days, out World w)
        {
            w = null;
            var d = InstallData();
            if (d == null) return;
            var s = new NewGameSettings { Difficulty = difficulty, LocalPlayer = 0, StartTime = 0, Seed = 4711 };
            w = World.NewGame(d, s, new GameOptions { RandomStartDay = false });
            w.Systems.Remove(OpenAT.Sim.AI.AiSystems.PumpSystem.SystemName); // operations plausibility with idle computers (robots ignore contract windows, ai-robots Q7)
            OpsWorld.RunUntil(w, days, 9);
        }

        [Fact]
        public void HeadlessGame_FreeGame_TenDays()
        {
            RunGame(Mission.FreeGame, 0, out var w0);
            if (w0 == null) return;
            // setup state (sim-core.md §3.6)
            foreach (var p in w0.Players)
            {
                Assert.Equal(2, p.Planes.Count);
                Assert.All(p.Planes.Items, pl => Assert.True(pl.Sponsored));
                Assert.Equal(1, p.Gates.NumRented);
                Assert.Equal(1, p.RentCities[w0.CityIndex(w0.HomeCity)].Rank);
                Assert.Equal(2, p.Contracts.Count);
                Assert.Equal(0, p.LicensedRouteCount);
            }
            var names = w0.Players.SelectMany(p => p.Planes.Items).Select(pl => pl.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.Equal(new[] { 0, 1, 2, 3 }, w0.Players.Select(p => p.Gates.Gates[0].Number));
            Assert.All(w0.UsedPlanes, u => Assert.NotNull(u));
            Assert.All(w0.TravelAgency.Offers, o => Assert.True(o.Premium > 0));
            Assert.All(w0.RouteTable, r => Assert.True(r.Demand > 0));

            RunGame(Mission.FreeGame, 10, out var w);
            Assert.Equal(10, w.Clock.Date);
            AssertPlausible(w);
        }

        [Fact]
        public void HeadlessGame_Mission2_TenDays_WithFlights()
        {
            var d = InstallData();
            if (d == null) return;
            var s = new NewGameSettings { Difficulty = Mission.Passengers, LocalPlayer = 0, StartTime = 0, Seed = 99 };
            var w = World.NewGame(d, s, new GameOptions { RandomStartDay = false });
            w.Systems.Remove(OpenAT.Sim.AI.AiSystems.PumpSystem.SystemName); // operations plausibility with idle computers (robots ignore contract windows, ai-robots Q7)
            var p = w.Local;
            Assert.Equal(new[] { 100, 105 }, p.Planes.Items.Select(x => w.Data.PlaneTypes[x.TypeId].CsvId).OrderBy(x => x).ToArray());

            // license the first home route and fly it with the first plane, plus the start contracts
            int ri = Enumerable.Range(0, w.RouteTable.Count).First(i => w.RouteTable[i].From == w.HomeCity
                       && w.Distance(w.RouteTable[i].From, w.RouteTable[i].To) < 2500000);
            Assert.True(w.Execute(new RentRouteCommand(0, ri)).Ok);
            var plane = p.Planes.Items.First();
            w.StartWorkingDay();
            var r = w.Execute(new PlaceFlightCommand(0, plane.Id, FlightKind.Route, w.RouteTable[ri].Id, 0, 12));
            Assert.True(r.Ok, r.ToString());
            w.Execute(new ExtendPlanCommand(0, plane.Id));
            var other = p.Planes.Items.Skip(1).First();
            foreach (var c in p.Contracts.ToList())
                w.Execute(new PlaceFlightCommand(0, other.Id, FlightKind.Contract, c.Id, c.Date, 14));

            int booked = 0, landed = 0;
            w.Events.Subscribe<FlightBooked>(e => booked++);
            w.Events.Subscribe<FlightLanded>(e => landed++);
            OpsWorld.RunUntil(w, 10, 9);
            Assert.Equal(10, w.Clock.Date);
            AssertPlausible(w);
            Assert.True(booked > 5, "flights booked: " + booked);
            Assert.True(landed > 5, "flights landed: " + landed);
            Assert.True(p.FlightCounters.Passengers > 0);
            Assert.True(p.RentRoutes[ri].Licensed);
        }

        static void AssertPlausible(World w)
        {
            foreach (var p in w.Players)
            {
                Assert.True(p.Money > -100000000 && p.Money < 1000000000, p + " money " + p.Money);
                foreach (var pl in p.Planes.Items)
                {
                    Assert.InRange(pl.Condition, 0, 100);
                    Assert.True(pl.Location >= 0 || pl.Location == PlaneLocation.Airborne || pl.Location == PlaneLocation.AtGate
                                || pl.Location == PlaneLocation.Landing || pl.Location == PlaneLocation.TakingOff);
                    // plans stay sorted and inside the week
                    var f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
                    for (int i = 1; i < f.Count; i++) Assert.True(f[i - 1].StartAbs <= f[i].StartAbs);
                    Assert.All(f, e => Assert.True(e.StartDate < w.Clock.Date + 7));
                }
                foreach (var c in p.Contracts.Items) Assert.True(c.LastDate >= w.Clock.Date - 2);
            }
            Assert.All(w.RouteTable, r => Assert.True(r.Demand >= 0));
            for (int g = 0; g < AuctionBoard.Slots; g++) Assert.Equal(-1, w.Auctions.Gates[g].Player);
        }
    }
}
