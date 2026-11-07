using System.Collections.Generic;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Glue between the operations domain and the finance/staff domain (docs/spec/integration.md):
    /// implementations of the operations rule interfaces on top of the finance APIs, the finance
    /// domain's <see cref="IOperationsBridge"/> over the operations state, and the statistics/mission
    /// counter updates from operations events. All finance-specific code of the operations domain is in
    /// this file.
    /// </summary>
    public static class FinanceIntegration
    {
        public static void Register(World w)
        {
            w.Rules.Set<IOperationsBridge>(new FleetOperationsBridge());
            w.Rules.Set<IFuelAccount>(new PlayerFuelAccount());
            w.Rules.Set<IMechanicLevel>(new PlayerMechanicLevel());
            w.Rules.Set<IStrikeStatus>(new PlayerStrikeStatus());
            w.Rules.Set<ICrewMapping>(new WorkerCrewMapping());
            w.Rules.Set<IPurchaseRebate>(new BodyguardPurchaseRebate());
            w.Rules.Set<IMaintenanceBooking>(new FinanceMaintenanceBooking());
            w.Rules.Set<IIncidentEconomics>(new FinanceIncidentEconomics());
            w.Rules.Set<IFlightSabotageState>(new SecurityFlagsSabotageState());
            SubscribeStatistics(w);
            w.Systems.Add(new OperationsSystems.Lambda("ops.connect-flags", 200, morning: ConnectFlags));
        }

        // ------------------------------------------------------------------ rules over finance state

        sealed class PlayerFuelAccount : IFuelAccount
        {
            public int Kind(World w, Player p) => (int)p.KeroseneKind;
            public bool TankOpen(World w, Player p) => p.TankOpen;
            public int TankContent(World w, Player p) => p.TankContents;
            public double TankPrice(World w, Player p) => p.TankPrice;
            public int BadKerosene(World w, Player p) => p.BadKerosene;
            public int Drain(World w, Player p, int units, string planeName, out long imputedCost)
            {
                var d = p.DrawTankFuel(units, planeName);
                imputedCost = d.ImputedCost;
                return d.FromTank;
            }
        }

        sealed class PlayerMechanicLevel : IMechanicLevel
        {
            public int Level(World w, Player p) => p.MechMode;
        }

        sealed class PlayerStrikeStatus : IStrikeStatus
        {
            public bool IsStriking(World w, Player p) => p.StrikeRunning;
        }

        /// <summary>Crew mapping by the staff domain ("MapWorkers"); the plane fields mirror the result.</summary>
        sealed class WorkerCrewMapping : ICrewMapping
        {
            public void MapCrew(World w, Player p)
            {
                p.MapWorkers(false);
                foreach (var pl in p.Planes.Items)
                {
                    var c = p.Crew(pl.Id);
                    pl.CrewPilots = c.Pilots;
                    pl.CrewAttendants = c.Attendants;
                    pl.PersonalQuality = c.Quality;
                }
            }

            public CrewState Crew(World w, Player p, Plane plane)
            {
                var c = p.Crew(plane.Id);
                return new CrewState { Pilots = c.Pilots, Attendants = c.Attendants, Quality = c.Quality };
            }
        }

        sealed class BodyguardPurchaseRebate : IPurchaseRebate
        {
            public void Apply(World w, Player p, long price) => p.BodyguardRebate(price);
        }

        sealed class FinanceMaintenanceBooking : IMaintenanceBooking
        {
            public void Book(World w, Player p, long extra, int planes, int mode) => p.BookMaintenance(extra, planes);
        }

        sealed class FinanceIncidentEconomics : IIncidentEconomics
        {
            public void QualityIncident(World w, Player p) => p.ApplyBreakdownEconomics();
            public void SeasonalIncident(World w, Player p) => p.ApplySeasonIncidentCost();
        }

        sealed class SecurityFlagsSabotageState : IFlightSabotageState
        {
            public bool PressStatement(World w, Player p) => false;
            public int BrochureTarget(World w, Player p) => -1;
            public int SecurityFlags(World w, Player p) => p.SecurityFlags;
        }

        // ------------------------------------------------------------------ statistics and mission counters

        static void SubscribeStatistics(World w)
        {
            w.Events.Subscribe<FlightBooked>(e =>
            {
                var s = e.Player.Statistics;
                var c = e.Player.Counters;
                c.Miles += e.DistanceM / 1609;
                if (e.Kind == FlightKind.Route || e.Kind == FlightKind.Contract)
                {
                    s.Add(StatId.Passengers, e.Passengers + e.PassengersFC);
                    s.Add(StatId.Flights, 1);
                    if (e.TouchesHome) s.Add(StatId.HomePassengers, e.Passengers + e.PassengersFC);
                    c.Passengers += e.Passengers;
                }
                if (e.Kind == FlightKind.Route) s.Add(StatId.IncomeRoutes, e.Income);
                if (e.Kind == FlightKind.Contract) s.Add(StatId.IncomeOrders, e.Income);
                if (e.ContractCounted) c.Orders++;
                if (e.OrderFlightCounted) c.OrderFlights++;
                s.Add(StatId.ExpenseKerosene, -(e.FuelCost + e.FoodCost));
                if (e.FreightTons > 0)
                {
                    c.Freight += e.FreightTons;
                    if (e.FreightFree) c.ReliefFreight += e.FreightTons;
                }
            });
            w.Events.Subscribe<PassengersEvaluated>(e =>
            {
                e.Player.Statistics.Add(StatId.SatisfiedPassengers, e.Satisfied);
                e.Player.Statistics.Add(StatId.UnsatisfiedPassengers, e.Unsatisfied);
            });
            w.Events.Subscribe<FlightDelayed>(e => e.Player.Statistics.Add(StatId.Delays, 1));
            w.Events.Subscribe<ContractAccepted>(e =>
            {
                if (e.Freight != null) e.Player.Statistics.Add(StatId.FreightOrders, 1);
                else if (e.Source == ContractSource.LastMinute) e.Player.Statistics.Add(StatId.LastMinuteOrders, 1);
                else if (e.Source != ContractSource.SparePart) e.Player.Statistics.Add(StatId.Orders, 1);
            });
            w.Events.Subscribe<ContractExpired>(e => { if (e.Penalty > 0) e.Player.Statistics.Add(StatId.ExpensePenalties, -e.Penalty); });
            w.Events.Subscribe<PlaneRefitted>(e =>
            {
                if (e.Cost > 0) e.Player.Statistics.Add(StatId.ExpenseOther, -e.Cost);
                else e.Player.Statistics.Add(StatId.IncomeOther, -e.Cost);
            });
            w.Events.Subscribe<PlaneTraded>(e =>
            {
                if (e.Source == "start") return;
                if (e.Sold) e.Player.Statistics.Add(StatId.IncomeOther, e.Price);
                else e.Player.Statistics.Add(StatId.ExpenseOther, -e.Price);
            });
        }

        /// <summary>
        /// "Connect the world" mission flags (sim-core.md §3.5), at 9:00 before the mission review: for
        /// every route flight in a plan on a route the airline rents with load &gt; 20, set the bit of the
        /// matching directed pair home ↔ mission city; the counter is the number of set bits.
        /// </summary>
        public static void ConnectFlags(World w)
        {
            if (w.Difficulty != Mission.ConnectWorld) return;
            foreach (var p in w.Players)
            {
                int bits = 0;
                foreach (var pl in p.Planes.Items)
                    foreach (var e in pl.Plan.Entries)
                    {
                        if (e.Kind != FlightKind.Route) continue;
                        int ri = w.RouteIndexOf(e.ObjectId);
                        if (ri < 0 || !p.RentRoutes[ri].Licensed || p.RentRoutes[ri].Load <= 20) continue;
                        for (int i = 0; i < 6; i++)
                        {
                            int m = w.MissionCityIds[i];
                            if (m == 0) continue;
                            if (e.From == w.HomeCity && e.To == m) bits |= 1 << i;
                            if (e.From == m && e.To == w.HomeCity) bits |= 1 << (i + 6);
                        }
                    }
                int n = 0;
                for (int b = 0; b < 12; b++) if ((bits & (1 << b)) != 0) n++;
                p.Counters.ConnectFlags = n;
            }
        }
    }

    /// <summary>The finance domain's view of planes, rents and routes (docs/spec/integration.md "Finance needs from Operations").</summary>
    public sealed class FleetOperationsBridge : IOperationsBridge
    {
        public IReadOnlyList<PlaneSnapshot> Planes(World w, Player p)
        {
            var l = new List<PlaneSnapshot>();
            foreach (var pl in p.Planes.Items)
            {
                int nh = -1, nd = -1;
                if (pl.Plan.NextFlight >= 0) { nh = pl.Plan.Entries[pl.Plan.NextFlight].LandHour; nd = pl.Plan.Entries[pl.Plan.NextFlight].LandDate; }
                l.Add(new PlaneSnapshot
                {
                    Id = pl.Id, Name = pl.Name, Condition = pl.Condition, Value = pl.MarketValue,
                    RequiredPilots = pl.Pilots, RequiredAttendants = pl.Attendants, MaxAttendants = pl.MaxAttendants,
                    StandingAtCity = PlaneLocation.IsOnGround(pl.Location), NextLandingHour = nh, NextLandingDate = nd,
                    IsDesigned = pl.TypeId == -1, Seats = pl.Seats, FuelUse = pl.Burn, Speed = pl.Speed,
                    SeatLevel = pl.Equipment[Equip.Seats], TrayLevel = pl.Equipment[Equip.Trays], DecorLevel = pl.Equipment[Equip.Deco],
                    EngineLevel = pl.Equipment[Equip.Engines], TyreLevel = pl.Equipment[Equip.Tyres],
                    ElectronicsLevel = pl.Equipment[Equip.Electronics], SafetyLevel = pl.Equipment[Equip.Safety],
                });
            }
            return l;
        }

        public IReadOnlyList<RentItem> Rents(World w, Player p)
        {
            var l = new List<RentItem>();
            for (int c = 0; c < PlayerGates.Records; c++) if (p.Gates.Gates[c].Used) l.Add(new RentItem(MoneyReason.GateRent, p.Gates.Gates[c].Rent));
            for (int c = 0; c < p.RentCities.Length; c++) if (p.RentCities[c].Rank != 0) l.Add(new RentItem(MoneyReason.BranchRent, p.RentCities[c].Rent));
            for (int c = 0; c < p.RentRoutes.Length; c++) if (p.RentRoutes[c].Rank != 0) l.Add(new RentItem(MoneyReason.RouteRent, p.RentRoutes[c].Rent));
            return l;
        }

        public int BranchCount(World w, Player p)
        {
            int n = 0;
            foreach (var r in p.RentCities) if (r.Rank != 0) n++;
            return n;
        }

        public int RouteCount(World w, Player p) => p.LicensedRouteCount;

        public IReadOnlyList<int> RentedRouteIds(World w, Player p)
        {
            var l = new List<int>();
            for (int c = 0; c < p.RentRoutes.Length; c++) if (p.RentRoutes[c].Rank != 0) l.Add(w.RouteTable[c].Id);
            return l;
        }

        public int GetRouteImage(World w, Player p, int routeId)
        {
            int i = w.RouteIndexOf(routeId);
            return i >= 0 && p.RentRoutes[i].Rank != 0 ? p.RentRoutes[i].Image : 0;
        }

        public void SetRouteImage(World w, Player p, int routeId, int image)
        {
            int i = w.RouteIndexOf(routeId);
            if (i >= 0 && p.RentRoutes[i].Rank != 0) p.RentRoutes[i].Image = image < 0 ? 0 : image > 100 ? 100 : image;
        }

        /// <summary>
        /// Takeover "absorb" (economy.md §6.8): planes move with cleared plans to the home airport, gates
        /// move, routes and branches move when the acquirer lacks them or the target's rank is better
        /// (the worse rank is given up; holders behind it move up).
        /// </summary>
        public void AbsorbOperations(World w, Player acquirer, Player target)
        {
            foreach (var pl in target.Planes.ToList())
            {
                target.Planes.Remove(pl.Id);
                for (int i = 0; i < FlightPlan.Size; i++) pl.Plan.Entries[i] = FlightEntry.Empty;
                pl.Plan.StartCity = w.HomeCity;
                pl.Plan.NextFlight = pl.Plan.NextStart = -1;
                pl.Location = w.HomeCity;
                acquirer.Planes.Add(pl);
            }
            PlaneMarket.SortFleet(acquirer);
            for (int c = 0; c < PlayerGates.Records; c++)
                if (target.Gates.Gates[c].Used) acquirer.Gates.Rent(target.Gates.Gates[c].Number, target.Gates.Gates[c].Rent);
            target.Gates.Clear();

            for (int r = 0; r < w.RouteTable.Count; r++)
            {
                var t = target.RentRoutes[r];
                var a = acquirer.RentRoutes[r];
                if (t.Rank == 0) continue;
                if (a.Rank == 0)
                {
                    a.Rank = t.Rank; a.Rent = t.Rent; a.TicketPrice = t.TicketPrice; a.TicketPriceFC = t.TicketPriceFC;
                    a.Image = t.Image; a.LastFlown = t.LastFlown; a.AvgFlown = t.AvgFlown; a.DaysLow = 0;
                }
                else if (t.Rank < a.Rank)
                {
                    int gone = a.Rank;
                    a.Rank = t.Rank;
                    foreach (var q in w.Players) if (q != acquirer && q != target && q.RentRoutes[r].Rank > gone) q.RentRoutes[r].Rank--;
                }
                else
                {
                    int gone = t.Rank;
                    foreach (var q in w.Players) if (q != target && q.RentRoutes[r].Rank > gone) q.RentRoutes[r].Rank--;
                }
                t.Rank = 0;
            }

            for (int c = 0; c < target.RentCities.Length; c++)
            {
                var t = target.RentCities[c];
                if (t.Rank == 0) continue;
                if (acquirer.RentCities[c].Rank == 0 || t.Rank < acquirer.RentCities[c].Rank) acquirer.RentCities[c] = t;
                target.RentCities[c] = default;
            }
            w.Rules.Get<ICrewMapping>().MapCrew(w, acquirer);
            foreach (var pl in acquirer.Planes.Items) FlightPlanner.Check(w, acquirer, pl);
        }

        /// <summary>Takeover "liquidate": planes sold at full value (returned, not booked); gates, routes and branches freed.</summary>
        public long LiquidateOperations(World w, Player target)
        {
            long sum = 0;
            foreach (var pl in target.Planes.ToList()) { sum += pl.MarketValue; target.Planes.Remove(pl.Id); }
            target.Gates.Clear();
            for (int r = 0; r < w.RouteTable.Count; r++)
                if (target.RentRoutes[r].Rank != 0)
                {
                    int gone = target.RentRoutes[r].Rank;
                    foreach (var q in w.Players) if (q != target && q.RentRoutes[r].Rank > gone) q.RentRoutes[r].Rank--;
                    target.RentRoutes[r].Rank = 0;
                }
            for (int c = 0; c < target.RentCities.Length; c++) target.RentCities[c] = default;
            target.Contracts.Clear();
            target.Freights.Clear();
            return sum;
        }
    }
}
