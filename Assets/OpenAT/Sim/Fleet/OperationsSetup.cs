using System;
using System.Collections.Generic;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// New-game steps of the operations domain (sim-core.md §3.5–3.6, planes-and-flights.md §4.2).
    /// Orders: 100 world (mission cities, route demand), 200 airlines (branch, gate, starting planes,
    /// starting contracts), 300 starting routes and branches, 400 crew and gates, 500 boards, auction
    /// postings and museum. Starting planes are not charged (the original books them before the
    /// starting money is assigned, which has the same effect), so the finance step may run in any order.
    /// </summary>
    public static class OperationsSetup
    {
        public static void Register(World w)
        {
            w.SetupSteps.Add(new Step("ops.world", 100, WorldSetup));
            w.SetupSteps.Add(new Step("ops.airlines", 200, Airlines));
            w.SetupSteps.Add(new Step("ops.start-routes", 300, StartRoutes));
            w.SetupSteps.Add(new Step("ops.crew-gates", 400, CrewAndGates));
            w.SetupSteps.Add(new Step("ops.boards", 500, Boards));
            w.SetupSteps.Add(new Step("ops.mission-design", 510, MissionDesign));
        }

        sealed class Step : ISetupStep
        {
            readonly Action<World, NewGameSettings> _apply;
            public Step(string name, int order, Action<World, NewGameSettings> apply) { Name = name; Order = order; _apply = apply; }
            public string Name { get; }
            public int Order { get; }
            public void Apply(World w, NewGameSettings s) => _apply(w, s);
        }

        /// <summary>DESIGNER_BUY missions: the AI's prebuilt design (data-tables.md §2.4).</summary>
        static void MissionDesign(World w, NewGameSettings s)
        {
            if (w.Data.MissionDesigns.TryGetValue(w.Difficulty, out var d)) w.AI.MissionDesign = d.Clone();
        }

        static void WorldSetup(World w, NewGameSettings s)
        {
            if (w.Difficulty == Mission.ConnectWorld) CreateMissionCities(w);
            if (w.Difficulty == 13) CreateEarthquakeCity(w);
            w.EnsureOperationsArrays();
            w.TravelAgency.RefillTicks = w.LastMinute.RefillTicks = w.FreightOffice.RefillTicks = 6;
            w.MuseumRefillTicks = 6;
            RouteOps.UpdateDemand(w);
        }

        /// <summary>
        /// Mission cities of "connect the world" (sim-core.md §3.5): six defaults by name; one equal to home
        /// or farther than 12 000 km is replaced by the most populous fitting city; missing home routes are
        /// added as a synthetic pair (level 1, rent 6000, factor 1).
        /// </summary>
        public static void CreateMissionCities(World w)
        {
            var cities = w.Data.Cities;
            if (cities.Count == 0) return;
            var map = new List<City>(cities.All);
            // stable ascending sort by population
            var idx = new List<(City c, int i)>();
            for (int i = 0; i < map.Count; i++) idx.Add((map[i], i));
            idx.Sort((a, b) => a.c.Population != b.c.Population ? a.c.Population.CompareTo(b.c.Population) : a.i.CompareTo(b.i));
            map = idx.ConvertAll(x => x.c);

            string[][] names =
            {
                new[] { "Río de Janeiro", "Rio de Janeiro" },
                new[] { "Nova Iorque", "Nueva York", "New York" },
                new[] { "Tóquio", "Tokio", "Tokyo" },
                new[] { "Deli", "Delhi", "Dillí" },
                new[] { "Joanesburgo", "Johanesburgo", "Johannesburg" },
                new[] { "Moskau", "Moskou", "Moscovo", "Moscou", "Mosca", "Moscú", "Moscow", "Moskva" },
            };
            for (int c = 0; c < 6; c++)
            {
                w.MissionCityIds[c] = 0;
                foreach (var n in names[c]) { var city = w.Data.CityByName(n); if (city != null) { w.MissionCityIds[c] = city.Id; break; } }
            }
            for (int c = 0; c < 6; c++)
            {
                int m = w.MissionCityIds[c];
                if (m != w.HomeCity && m != 0 && w.Distance(m, w.HomeCity) <= 12000000) continue;
                for (int d = map.Count - 1; d >= 0; d--)
                {
                    int id = map[d].Id;
                    if (id == w.HomeCity || w.Distance(id, w.HomeCity) > 12000000) continue;
                    if (Array.IndexOf(w.MissionCityIds, id) >= 0) continue;
                    w.MissionCityIds[c] = id;
                    break;
                }
            }
            int nextId = 0;
            foreach (var r in w.RouteTable) nextId = Math.Max(nextId, r.Id);
            for (int c = 0; c < 6; c++)
            {
                if (w.RouteIndex(w.HomeCity, w.MissionCityIds[c]) >= 0) continue;
                var a = new Route { Id = ++nextId, Level = 1, From = w.HomeCity, To = w.MissionCityIds[c], Rent = 6000, Factor = 1, Demand = 0, Synthetic = true };
                var b = new Route { Id = ++nextId, Level = 1, From = a.To, To = a.From, Rent = 6000, Factor = 1, Demand = 0, Synthetic = true };
                a.Index = w.RouteTable.Count; w.RouteTable.Add(a);
                b.Index = w.RouteTable.Count; w.RouteTable.Add(b);
            }
        }

        /// <summary>Earthquake city of add-on mission 3 (sim-core.md §3.5): nearest to 5000 km from home, near the equator if possible.</summary>
        public static void CreateEarthquakeCity(World w)
        {
            var all = w.Data.Cities.All;
            int best = -1, bestDiff = 2000000000;
            for (int c = all.Count - 1; c >= 0; c--)
            {
                int diff = Math.Abs(w.Distance(all[c].Id, w.HomeCity) - 5000000);
                if (diff < bestDiff && Math.Abs(all[c].GlobeY) < 20) { best = c; bestDiff = diff; }
            }
            if (bestDiff > 2000000)
                for (int c = all.Count - 1; c >= 0; c--)
                {
                    int diff = Math.Abs(w.Distance(all[c].Id, w.HomeCity) - 5000000);
                    if (diff < bestDiff) { best = c; bestDiff = diff; }
                }
            w.CrisisCityId = best >= 0 ? all[best].Id : 0;
        }

        /// <summary>Starting plane types by mission (sim-core.md §3.6): 100 + 114 (Diff 2), 100 + 104 (Diff 3), else 100 + 105.</summary>
        public static int[] StartingTypes(int difficulty)
        {
            if (difficulty == Mission.Profit) return new[] { 100, 114 };
            if (difficulty == Mission.ConnectWorld) return new[] { 100, 104 };
            return new[] { 100, 105 };
        }

        static void Airlines(World w, NewGameSettings s)
        {
            int homeIndex = w.CityIndex(w.HomeCity);
            var homeCity = w.Data.Cities.Find(w.HomeCity);
            for (int c = 0; c < Player.Count; c++)
            {
                var p = w.Players[c];
                for (int i = 0; i < p.RentCities.Length; i++) p.RentCities[i] = default;
                if (homeIndex >= 0) { p.RentCities[homeIndex].Rank = 1; p.RentCities[homeIndex].Rent = homeCity.OfficeRent; }
                for (int i = 0; i < p.RentRoutes.Length; i++) p.RentRoutes[i] = new RentRoute();

                p.Gates.Clear();
                p.Gates.Rent(c, 1000);

                p.Planes.Clear();
                var names = new TeakRand(1);
                foreach (var typeId in StartingTypes(w.Difficulty))
                {
                    var t = w.Data.PlaneTypeByCsvId(typeId);
                    if (t == null) continue;
                    var pl = PlaneMarket.BuyNew(w, p, t, names, free: true);
                    w.Events.Publish(new PlaneTraded { Player = p, Plane = pl, Price = 0, Source = "start" });
                }
                foreach (var pl in p.Planes.Items)
                {
                    pl.Sponsored = true;
                    if (w.Difficulty == 11) pl.Condition = pl.WorstCondition = pl.TargetCondition = 60;
                    if (w.Difficulty == 17) pl.Condition = pl.WorstCondition = pl.TargetCondition = 35;
                }

                p.Contracts.Clear();
                p.Freights.Clear();
                w.CalcPlayerMaximums(true);
                if (w.TravelHolding && w.Difficulty != 19)
                {
                    p.ContractRandom.SetSeed(s.Seed + c);
                    for (int k = 0; k < 2; k++)
                    {
                        var a = ContractGenerator.Begin(w, 0, p.ContractRandom);
                        p.Contracts.Add(a);
                        w.Events.Publish(new ContractAccepted { Player = p, Contract = a, Source = ContractSource.Start });
                    }
                }
            }
        }

        /// <summary>
        /// Starting route and branches (sim-core.md §3.6; Diff &gt; 3 except add-on 2): airline c rents the
        /// (c+1)-th home route scanning the table from the end (the level-1 preference is ineffective,
        /// QUIRK); with travel holding the foreign end becomes a branch; then each airline gets one of
        /// the four nearest cities more than 1000 km from home.
        /// </summary>
        static void StartRoutes(World w, NewGameSettings s)
        {
            if (!(w.Difficulty > Mission.ConnectWorld && w.Difficulty != 12)) return;
            var routes = w.RouteTable;
            var cities = w.Data.Cities;
            for (int c = 0; c < Player.Count; c++)
            {
                int e = c + 1;
                for (int d = routes.Count - 1; d >= 0; d--)
                {
                    var r = routes[d];
                    if (!r.Touches(w.HomeCity)) continue;
                    if (--e > 0) continue;
                    RouteOps.Rent(w, w.Players[c], r.From, r.To, r.Rent);
                    if (w.TravelHolding)
                    {
                        if (r.From != w.HomeCity) GiveBranch(w, c, w.CityIndex(r.From));
                        if (r.To != w.HomeCity) GiveBranch(w, c, w.CityIndex(r.To));
                    }
                    break;
                }
            }

            var near = new int[4];
            for (int c = 0; c < 4; c++)
            {
                int dist = int.MaxValue, best = -1;
                for (int d = cities.Count - 1; d >= 0; d--)
                {
                    if (cities.All[d].Id == w.HomeCity) continue;
                    int delta = w.Distance(cities.All[d].Id, w.HomeCity);
                    for (int k = 0; k < c; k++) if (near[k] == d) delta = int.MaxValue;
                    if (delta > 1000000 && delta < dist) { dist = delta; best = d; }
                }
                near[c] = best;
            }
            if (w.TravelHolding)
                for (int c = 0; c < 4; c++)
                    for (int d = 0; d < 4; d++)
                    {
                        int city = near[(c + d) % 4];
                        if (city < 0) continue;
                        if (w.Players[c].RentCities[city].Rank != 0) continue;
                        GiveBranch(w, c, city);
                        break;
                    }
        }

        static void GiveBranch(World w, int player, int cityIndex)
        {
            if (cityIndex < 0) return;
            int n = 1;
            for (int f = 0; f < Player.Count; f++)
                if (f != player && w.Players[f].RentCities[cityIndex].Rank != 0) n++;
            w.Players[player].RentCities[cityIndex].Rank = n;
            w.Players[player].RentCities[cityIndex].Rent = w.Data.Cities.All[cityIndex].OfficeRent;
        }

        static void CrewAndGates(World w, NewGameSettings s)
        {
            foreach (var p in w.Players)
            {
                w.Rules.Get<ICrewMapping>().MapCrew(w, p);
                GatePlanner.PlanGates(w, p);
            }
        }

        static void Boards(World w, NewGameSettings s)
        {
            w.Auctions.Randomize(w, 2);
            ContractBoards.FillAll(w, s.Seed, setupOrder: true);
            PlaneMarket.CreateUsedPlanes(w);
        }
    }
}
