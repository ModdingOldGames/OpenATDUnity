using System;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Time-driven systems of the operations domain. Orders are the original step numbers × 100:
    /// midnight world sequence (sim-core.md §2.6), per-airline new day (§2.7), minute/hour steps (§2.4).
    /// </summary>
    public static class OperationsSystems
    {
        public const string Prefix = "ops.";

        public static void Register(World w)
        {
            // §2.6 midnight (world)
            w.Systems.Add(new Lambda("ops.boards-refill", 1400, newDay: ContractBoardsNight));
            w.Systems.Add(new Lambda("ops.museum", 2000, newDay: PlaneMarket.CreateUsedPlanes));
            w.Systems.Add(new Lambda("ops.route-demand", 2200, newDay: RouteOps.UpdateDemand));
            w.Systems.Add(new Lambda("ops.auction-resolve", 2400, newDay: x => x.Auctions.Resolve(x)));
            w.Systems.Add(new Lambda("ops.gates-renumber", 2500, newDay: GatePlanner.Renumber));
            w.Systems.Add(new Lambda("ops.auction-post", 2600, newDay: x => x.Auctions.Randomize(x, Math.Min(x.Clock.Date + 2, 7))));

            // §2.7 per airline
            w.Systems.Add(new Lambda("ops.daily-reset", 300, playerNewDay: (x, p) => { p.OutOfGates = 0; p.VisitedMuseumToday = false; }));
            w.Systems.Add(new Lambda("ops.routes-daily", 1100, playerNewDay: RouteOps.DailyUpdate));
            // Rents (step 15) are booked by the finance domain (Player.BookRents, fed by IOperationsBridge.Rents).
            w.Systems.Add(new Lambda("ops.contracts-daily", 1700, playerNewDay: ContractUsage.DailyUpdate));
            w.Systems.Add(new Lambda("ops.plane-upkeep", 2100, playerNewDay: Maintenance.NightlyUpkeep));

            // §2.4 minute / hour steps
            w.Systems.Add(new Lambda("ops.refill-ticks", 200, minute: x => { if (x.Clock.Minute % 5 == 0) ContractBoards.Tick(x); }));
            w.Systems.Add(new Lambda("ops.breakdowns", 800, minute: x => { if (x.Clock.Minute == 0) FlightOps.Breakdowns(x); }));
            w.Systems.Add(new Lambda("ops.incidents", 1200, minute: x => { if (x.Clock.Minute % 4 == 0) FlightOps.Incidents(x); }));
            w.Systems.Add(new Lambda("ops.landings", 3000, minute: x => { if (x.Clock.Minute == 15) FlightOps.Landings(x); }));
            w.Systems.Add(new Lambda("ops.departures", 3100, minute: x => { if (x.Clock.Minute == 30) FlightOps.Departures(x); }));
            w.Systems.Add(new Lambda("ops.home-airport", 3200, minute: FlightOps.HomeAirportStep));
            w.Systems.Add(new Lambda("ops.refits", 3300, minute: FlightOps.ApplyRefits));
            w.Systems.Add(new Lambda("ops.flights-hourly", 200, hour: FlightOps.Hourly));

            // §2.8 morning
            w.Systems.Add(new Lambda("ops.morning", 300, morning: Morning));
        }

        static void ContractBoardsNight(World w) => ContractBoards.FillAll(w);

        /// <summary>
        /// Morning (sim-core.md §2.8): add-on mission 9 manager orders (5 per human airline, order-flight
        /// counters reset; day 0 starts at 2 or 3 by airline parity), today's flight counter reset.
        /// </summary>
        static void Morning(World w)
        {
            if (w.Difficulty == 19)
                foreach (var p in w.Players)
                {
                    if (p.IsOut) continue;
                    p.FlightCounters.OrderFlightsToday = w.Clock.Date == 0 ? 2 + (p.Index & 1) : 0;
                    if (p.IsComputer) continue;
                    for (int c = 0; c < 5; c++)
                    {
                        var a = ContractGenerator.ManagerOrder(w, c / 2, p.ContractRandom);
                        p.Contracts.Add(a);
                        w.Events.Publish(new ContractAccepted { Player = p, Contract = a, Source = ContractSource.ManagerOrder });
                    }
                }
            foreach (var p in w.Players) if (!p.IsOut) p.FlightCounters.FlightsToday = 0;
        }

        /// <summary>A system built from delegates (one stable name per hook).</summary>
        public sealed class Lambda : SimSystem
        {
            readonly string _name;
            readonly int _order;
            readonly Action<World> _minute, _hour, _newDay, _morning;
            readonly Action<World, Player> _playerNewDay;

            public Lambda(string name, int order, Action<World> minute = null, Action<World> hour = null, Action<World> newDay = null,
                          Action<World, Player> playerNewDay = null, Action<World> morning = null)
            {
                _name = name; _order = order; _minute = minute; _hour = hour; _newDay = newDay; _playerNewDay = playerNewDay; _morning = morning;
            }

            public override string Name => _name;
            public override int Order => _order;
            public override void OnMinute(World w) => _minute?.Invoke(w);
            public override void OnHour(World w) => _hour?.Invoke(w);
            public override void OnNewDay(World w) => _newDay?.Invoke(w);
            public override void OnPlayerNewDay(World w, Player p) => _playerNewDay?.Invoke(w, p);
            public override void OnMorning(World w) => _morning?.Invoke(w);
        }
    }
}
