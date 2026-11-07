using System;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Sim-time milestones that replace the original's frame-driven home-airport animation
    /// (planes-and-flights.md §11.3, §11.5, open question 1): minutes after the phase start.
    /// </summary>
    public static class HomeAirportTiming
    {
        /// <summary>Landing roll from L:15 ends at L:20.</summary>
        public const int LandingRoll = 5;
        /// <summary>Arrival at the gate (passengers disembark, satisfaction evaluated) at L:25.</summary>
        public const int TaxiToGate = 5;
        /// <summary>The plane leaves the gate from minute 45 and is parked at L:50.</summary>
        public const int LeaveGateMinute = 45, ParkedMinute = 50;
        /// <summary>Boarding ends at S:55 (taxi out, take-off); the flight is booked at S:58.</summary>
        public const int BoardingEndMinute = 55, TakeOff = 3;
    }

    /// <summary>
    /// Flight execution (planes-and-flights.md §11–13, sim-core.md §2.4, §6.2–6.4): hourly passenger
    /// refresh and delays, departures at :30 with booking, landings at :15, satisfaction, breakdowns
    /// and in-flight incidents, refits on the ground.
    /// </summary>
    public static class FlightOps
    {
        public const int CustomersPerPerson = 15;
        /// <summary>Food cost per economy passenger by catering level (§6.1).</summary>
        public static int FoodCost(int level) => Equip.Costs[Equip.Food][Math.Max(0, Math.Min(2, level))];

        static bool HasEntry(Plane plane) => plane.Plan.NextFlight >= 0;
        static ref FlightEntry Current(Plane plane) => ref plane.Plan.Entries[plane.Plan.NextFlight];

        // ------------------------------------------------------------------ booking

        /// <summary>
        /// "BookFlight" (planes-and-flights.md §12.4), called at departure: load averages, income and
        /// costs, statistics, balance sheet, hidden computer bonus, seat conversion, money, tank drain,
        /// route/contract effects, image effects, passenger counter, wear.
        /// </summary>
        public static void BookFlight(World w, Player p, Plane plane, int index)
        {
            ref var e = ref plane.Plan.Entries[index];
            var fuel = w.FuelCost;
            var account = w.Rules.Get<IFuelAccount>();
            int bkf = fuel.BadKeroseneFactor(w, p);

            if (p.Owner == Owner.Human && e.Kind == FlightKind.Route)
            {
                p.RouteUsage++;
                if (p.RouteUsage == 1) p.RouteUsage++;
            }

            w.Rules.Get<IRoutePassengerRule>().Calculate(w, p, plane, ref e);

            int pax = e.Passengers, paxFC = e.PassengersFC;
            if (plane.MaxPassengers > 0) plane.Load = plane.Load == 0 ? pax * 100 / plane.MaxPassengers : (plane.Load + pax * 100 / plane.MaxPassengers) / 2;
            else plane.Load = 0;
            if (plane.MaxPassengersFC > 0) plane.LoadFC = plane.LoadFC == 0 ? paxFC * 100 / plane.MaxPassengersFC : (plane.LoadFC + paxFC * 100 / plane.MaxPassengersFC) / 2;
            else plane.LoadFC = 0;

            int dist = w.Distance(e.From, e.To);
            plane.Kilometers += dist / 1000;
            plane.TotalPassengers += pax;
            p.FlightCounters.Miles += dist / 1609;

            long income = 0, expense = 0, fuelCost, food = 0;
            int freightTons = 0;
            bool freightFree = false;
            Contract contract = e.Kind == FlightKind.Contract ? p.Contracts.Find(e.ObjectId) : null;
            FreightContract freight = e.Kind == FlightKind.Freight ? p.Freights.Find(e.ObjectId) : null;

            fuelCost = fuel.Booking(w, p, e.From, e.To, plane.Burn, plane.Speed);
            if (e.Kind == FlightKind.Contract && e.Okay != EntryOkay.Ok)
                expense = fuelCost;
            else if (e.Kind == FlightKind.Freight)
            {
                expense = fuelCost;
                if (freight != null && freight.TonsLeft > 0)
                {
                    int cap = plane.Seats / 10;
                    freightTons = Math.Min(freight.TonsLeft, cap);
                    p.FlightCounters.Freight += freightTons;
                    if (freight.Premium == 0) { p.FlightCounters.FreightFree += freightTons; freightFree = true; }
                    freight.TonsLeft -= cap;
                    if (freight.TonsLeft <= 0)
                    {
                        freight.TonsLeft = 0;
                        income = freight.Premium;
                    }
                }
            }
            else
            {
                income = Income(w, p, plane, ref e, contract);
                expense = fuelCost;
            }
            if (e.IsPassengerFlight)
            {
                food = (long)pax * FoodCost(plane.Equipment[Equip.Food]);
                expense += food;
            }

            if (e.Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(e.ObjectId);
                if (ri >= 0)
                {
                    p.RentRoutes[ri].CarriedToday += pax;
                    var r = w.RouteTable[ri];
                    for (int c = w.RouteTable.Count - 1; c >= 0; c--)
                        if (w.RouteTable[c].From == r.To && w.RouteTable[c].To == r.From)
                        {
                            p.RentRoutes[c].CarriedToday += pax + paxFC;
                            break;
                        }
                }
            }

            p.Today.Kerosene += expense;
            plane.Balances[0] -= expense;
            if (e.Kind == FlightKind.Route) p.Today.Tickets += income;
            bool contractCounted = false;
            if (e.Kind == FlightKind.Contract) { p.Today.Orders += income; p.FlightCounters.Contracts++; contractCounted = true; }
            plane.Balances[0] += income;

            long saldo = income - expense;
            long bonus1 = Math.Min(p.Bonus, Math.Abs(saldo) / 10);
            saldo += bonus1;
            // The original recomputes the deduction from the already increased result, so the pool shrinks by
            // a different amount than was paid out; FixHiddenBonusDeduction (default) deducts what was paid.
            long bonus2 = w.Options.FixHiddenBonusDeduction ? bonus1 : Math.Min(p.Bonus, Math.Abs(saldo) / 10);
            p.Bonus -= bonus2;

            if (e.Kind != FlightKind.Auto)
            {
                bool freightFlight = e.Kind == FlightKind.Freight;
                if (plane.SeatsRemoved != freightFlight)
                {
                    plane.SeatsRemoved = freightFlight;
                    p.Book(-15000, MoneyReason.FreightSwitch, plane.Name);
                }
            }
            p.Book(saldo, MoneyReason.FlightResultBase + e.Kind, ContractUsage.CityPair(w, e.From, e.To));
            p.Profit += saldo;

            bool orderCounted = false;
            long tankCost = 0;
            int used = 0;
            if (account.TankOpen(w, p))
            {
                int units = fuel.FuelUnits(dist, plane.Burn, plane.Speed);
                used = account.Drain(w, p, units, plane.Name, out tankCost);
                plane.Balances[0] -= tankCost;
            }

            if (e.Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(e.ObjectId);
                if (ri >= 0)
                {
                    var rr = p.RentRoutes[ri];
                    rr.LastFlown = 0;
                    if (!p.HasFlownRoutes)
                    {
                        int before = 0;
                        foreach (var q in w.Players) if (q.HasFlownRoutes) before++;
                        w.Events.Publish(new FirstRouteFlight { Player = p, AirlinesBefore = before });
                        p.HasFlownRoutes = true;
                    }
                    if (plane.MaxPassengers > 0) rr.Load = rr.Load == 0 ? pax * 100 / plane.MaxPassengers : (rr.Load * 3 + pax * 100 / plane.MaxPassengers) / 4;
                    else rr.Load = 0;
                    if (plane.MaxPassengersFC > 0) rr.LoadFC = rr.LoadFC == 0 ? paxFC * 100 / plane.MaxPassengersFC : (rr.LoadFC * 3 + paxFC * 100 / plane.MaxPassengersFC) / 4;
                    else rr.LoadFC = 0;
                    var route = w.RouteTable[ri];
                    route.Demand -= pax;
                    if (route.Demand < 0) route.Demand = 0;
                    if (rr.Image < 100) rr.Image++;
                }
            }
            else if (e.Kind == FlightKind.Contract && contract != null)
            {
                if (e.Okay == EntryOkay.Ok || e.Okay == EntryOkay.WrongDay) contract.InPlan = PlanState.Done;
                else w.Advise(p, 2313, plane.Name);
                if (w.Difficulty == 19) orderCounted = CountOrderFlight(w, p, contract);
            }

            if (e.IsPassengerFlight) ImageEffects(w, p, plane, ref e);
            if (e.IsPassengerFlight) p.FlightCounters.Passengers += pax;

            plane.Condition = w.Rules.Get<IPlaneWearRule>().ApplyWear(w, p, plane, dist, bkf);

            w.Events.Publish(new FlightBooked
            {
                Player = p, Plane = plane, Entry = e, Kind = e.Kind, DistanceM = dist,
                Income = income, FuelCost = expense - food, FoodCost = food, TankCost = tankCost, TankUnitsUsed = used,
                Saldo = saldo, BonusPaid = bonus1, Passengers = pax, PassengersFC = paxFC,
                TouchesHome = e.IsPassengerFlight && (e.From == w.HomeCity || e.To == w.HomeCity),
                FreightTons = freightTons, FreightFree = freightFree, ContractCounted = contractCounted, OrderFlightCounted = orderCounted,
            });
        }

        /// <summary>Income of a flight ("GetEinnahmen", §12.4 step 5).</summary>
        public static long Income(World w, Player p, Plane plane, ref FlightEntry e, Contract contract)
        {
            switch (e.Kind)
            {
                case FlightKind.Route: return (long)e.TicketPrice * e.Passengers + (long)e.TicketPriceFC * e.PassengersFC;
                case FlightKind.Contract: return contract?.Premium ?? 0;
                case FlightKind.Auto: return (long)plane.Seats * w.Distance(e.From, e.To) / 1000 / 40;
                case FlightKind.Freight: return p.Freights.Find(e.ObjectId)?.Premium ?? 0;
            }
            return 0;
        }

        /// <summary>Add-on mission 9 order flight counting (§12.4 step 11).</summary>
        static bool CountOrderFlight(World w, Player p, Contract c)
        {
            int k = w.Clock.Date + p.Index;
            int limit = 5 - (k % 5 == 1 ? 1 : 0) - (k % 11 == 2 ? 1 : 0) - (k % 7 == 0 ? 1 : 0) - k % 2;
            if ((!p.IsComputer && c.ManagerOrder) || p.FlightCounters.OrderFlightsToday < limit)
            {
                p.FlightCounters.OrderFlights++;
                p.FlightCounters.OrderFlightsToday++;
                return true;
            }
            return false;
        }

        /// <summary>Image effects of a route or contract flight (planes-and-flights.md §12.8, contracts-and-routes.md §3.9).</summary>
        static void ImageEffects(World w, Player p, Plane plane, ref FlightEntry e)
        {
            var target = p;
            int brochure = w.Rules.Get<IFlightSabotageState>().BrochureTarget(w, p);
            if (brochure >= 0 && brochure < Player.Count)
            {
                target = w.Players[brochure];
                if (p.Owner == Owner.Human) w.Advise(p, 2357, plane.Name, target.Airline);
            }
            int ri = e.Kind == FlightKind.Route ? w.RouteIndexOf(e.ObjectId) : -1;
            var rr = ri >= 0 ? p.RentRoutes[ri] : null;
            var crew = w.Rules.Get<ICrewMapping>().Crew(w, p, plane);
            if (!p.IsComputer)
            {
                int q = crew.Quality;
                if (q < 50 && e.ObjectId % 10 == 0) target.ChangeImage(-1);
                if (q > 90 && e.ObjectId % 10 == 0) target.ChangeImage(1);
                if (rr != null && rr.Image > 0) rr.Image--;
            }
            int add = 0;
            if (plane.Condition < 20) add -= 50;
            if (plane.Condition < 60) add -= 10;
            if (plane.Condition > 98) add += 10;
            for (int i = Equip.Seats; i <= Equip.Deco; i++)
            {
                if (plane.Equipment[i] == 0) add--;
                if (plane.Equipment[i] == 2) add += 2;
            }
            if (!p.IsComputer && plane.Attendants > 0) add += 3 * crew.Attendants / plane.Attendants;
            if (e.Kind == FlightKind.Route)
            {
                long c1 = e.TicketPrice;
                long c2 = (long)w.ReferencePrice(e.From, e.To) * 3;
                if (p.IsComputer) c2 = c2 * 9 / 10;
                if (c1 < c2 / 2) add += 10;
                if (c1 > c2) add -= 10;
                if (c1 > c2 + c2 / 2) add -= 10;
                if (c1 > c2 + c2) add -= 10;
            }
            target.ChangeImage(add / 10);
            if (rr != null && rr.Image + add / 10 > 0)
            {
                rr.Image += add / 10;
                if (rr.Image > 100) rr.Image = 100;
                if (rr.Image < 0) rr.Image = 0;
            }
        }

        // ------------------------------------------------------------------ satisfaction

        /// <summary>
        /// "CalculateHappyPassengers" (planes-and-flights.md §13): route/contract passengers in groups of
        /// 15 (plus the remainder), each group judged by condition, cabin quality, crew and price.
        /// </summary>
        public static void EvaluatePassengers(World w, Player p, Plane plane)
        {
            if (!HasEntry(plane)) return;
            ref var e = ref Current(plane);
            if (!e.IsPassengerFlight) return;
            var crewState = w.Rules.Get<ICrewMapping>().Crew(w, p, plane);
            int quality = plane.Equipment[Equip.Seats] + plane.Equipment[Equip.Food] + plane.Equipment[Equip.Trays] + plane.Equipment[Equip.Deco]
                        + crewState.Attendants - plane.Attendants;
            var rand = new TeakRand(p.Index + w.Clock.Date + w.Clock.Hour + quality + e.From);
            int tooExpensive = 0;
            if (e.Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(e.ObjectId);
                if (ri >= 0)
                {
                    long c1 = p.RentRoutes[ri].TicketPrice;
                    long c2 = (long)w.ReferencePrice(e.From, e.To) * 3;
                    if (c2 > 0 && c1 > c2) tooExpensive = (int)Math.Min(100, c1 * 100 / c2);
                }
            }
            int crew = crewState.Quality;
            int total = e.Passengers + e.PassengersFC;
            int sat = 0, unsat = 0;
            for (int c = total / CustomersPerPerson; c >= 0; c--)
            {
                int n = c == 0 ? total % CustomersPerPerson : CustomersPerPerson;
                bool happy;
                if (rand.Rand(70) > plane.Condition) happy = false;
                else if (quality > 4 && rand.Rand(100) < 40) happy = true;
                else if (quality > 8 && rand.Rand(100) < 55) happy = true;
                else if (rand.Rand(65) > crew) happy = false;
                else if (tooExpensive != 0 && rand.Rand(130) < tooExpensive) happy = false;
                else happy = true;
                if (happy) sat += n; else unsat += n;
            }
            w.Events.Publish(new PassengersEvaluated { Player = p, Plane = plane, Satisfied = sat, Unsatisfied = unsat });
        }

        // ------------------------------------------------------------------ hourly

        /// <summary>
        /// Hour change, per plane of every airline (sim-core.md §2.4 hour steps 2–3): Problem and
        /// PseudoProblem −1, passenger recount of all route entries, next-flight pointer, then the delay
        /// checks for flights of non-computer airlines that start next hour.
        /// </summary>
        public static void Hourly(World w)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var paxRule = w.Rules.Get<IRoutePassengerRule>();
            var strikes = w.Rules.Get<IStrikeStatus>();
            var crewRule = w.Rules.Get<ICrewMapping>();
            foreach (var p in w.Players)
            {
                foreach (var plane in p.Planes.ToList())
                {
                    if (plane.Problem > 0) plane.Problem--;
                    if (plane.PseudoProblem > 0) plane.PseudoProblem--;
                    var f = plane.Plan.Entries;
                    for (int e = 0; e < FlightPlan.Size; e++)
                        if (f[e].Kind == FlightKind.Route) paxRule.Calculate(w, p, plane, ref f[e]);
                    plane.Plan.UpdateNextFlight(date, hour);

                    for (int e = FlightPlan.Size - 1; e >= 0; e--)
                    {
                        if (p.IsComputer || f[e].Kind == 0) continue;
                        if (f[e].StartDate * 24 + f[e].StartHour - 1 != date * 24 + hour) continue;
                        int reason = 0;
                        bool delay = false;
                        if (plane.PseudoProblem > 0) { delay = true; reason = 2358; }
                        if (strikes.IsStriking(w, p)) { delay = true; reason = 2356; }
                        else if (CrewShort(crewRule.Crew(w, p, plane), plane)) { delay = true; reason = 2350; }
                        else if (plane.Condition < 15) { delay = true; reason = 2351; }
                        else if (f[e].Kind == FlightKind.Contract && f[e].Okay == EntryOkay.WrongDay) { delay = true; reason = 2352; }
                        else if (plane.Problem > 0) { delay = true; reason = 2355; }
                        if (!delay) continue;

                        if (p.Owner == Owner.Human) w.Advise(p, reason, plane.Name, w.City(f[e].From).Name);
                        f[e].StartHour++;
                        if (f[e].StartHour >= 24) { f[e].StartHour -= 24; f[e].StartDate++; }
                        w.Events.Publish(new FlightDelayed { Player = p, Plane = plane, EntryIndex = e, ReasonTextId = reason });
                        plane.Plan.UpdatePointers(date, hour);
                        FlightPlanner.Check(w, p, plane);
                        ContractUsage.UpdateContracts(w, p);
                    }
                    plane.Plan.UpdateNextStart(date, hour);
                }
            }
        }

        static bool CrewShort(CrewState c, Plane plane) => c.Pilots < plane.Pilots || c.Attendants < plane.Attendants;

        // ------------------------------------------------------------------ landings (:15)

        /// <summary>Landings at :15 (planes-and-flights.md §11.5, sim-core.md §2.4).</summary>
        public static void Landings(World w)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.ToList())
                {
                    if (!HasEntry(plane)) continue;
                    ref var e = ref Current(plane);
                    if (e.LandHour != hour || e.LandDate != date) continue;
                    int city = e.To;

                    if (e.Kind == FlightKind.Contract && e.Passengers == 0)
                        foreach (var other in p.Planes.Items)
                            if (other.Location == city && other.Problem > 1)
                            {
                                other.Problem = Math.Max(1, other.Problem - 15);
                                w.Advise(p, 2320, w.City(city).Name, other.Problem);
                            }

                    if (city == w.HomeCity)
                    {
                        plane.Location = PlaneLocation.Landing;
                        plane.PhaseStartMinute = w.AbsoluteMinute;
                        plane.Unloaded = false;
                    }
                    else
                    {
                        if (e.Kind != FlightKind.Auto && e.Kind != FlightKind.Freight) EvaluatePassengers(w, p, plane);
                        plane.Location = city;
                        plane.GlobeX = w.City(city).GlobeX;
                        plane.GlobeY = w.City(city).GlobeY;
                        w.Events.Publish(new FlightLanded { Player = p, Plane = plane, Entry = e, City = city });
                    }
                }
            }
        }

        // ------------------------------------------------------------------ departures (:30)

        /// <summary>
        /// Departures at :30 (planes-and-flights.md §11.3): home departures of route/contract flights with
        /// a gate go to the gate (booked at take-off); all others are airborne and booked at once, a
        /// gate-less home departure costs image −2.
        /// </summary>
        public static void Departures(World w)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.ToList())
                {
                    if (!HasEntry(plane)) continue;
                    int idx = plane.Plan.NextFlight;
                    ref var e = ref plane.Plan.Entries[idx];
                    if (e.Kind == 0 || e.StartDate != date || e.StartHour != hour) continue;
                    p.FlightCounters.FlightsToday++;
                    int city = e.From;
                    if (city == w.HomeCity && e.Gate != GateCode.None && e.Gate != GateCode.NotHome && e.Kind != FlightKind.Auto && e.Kind != FlightKind.Freight)
                    {
                        plane.Location = PlaneLocation.AtGate;
                        plane.GateStartHour = e.StartHour & 0xFF;
                        plane.PhaseStartMinute = w.AbsoluteMinute;
                    }
                    else
                    {
                        plane.Location = PlaneLocation.Airborne;
                        bool noGate = city == w.HomeCity && e.Gate == GateCode.None && e.Kind != FlightKind.Auto && e.Kind != FlightKind.Freight;
                        BookFlight(w, p, plane, idx);
                        if (noGate)
                        {
                            w.Advise(p, 2353, plane.Name, w.City(city).Name);
                            p.ChangeImage(-2);
                            // QUIRK: the original never increments OutOfGates (the AI's gate-bidding trigger).
                            if (w.Options.FixOutOfGatesCounter) p.OutOfGates++;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ home airport milestones

        /// <summary>
        /// Every minute: advances planes in the home-airport states (landing roll, gate, take-off) by sim
        /// time (<see cref="HomeAirportTiming"/>), with the original's effects at each milestone.
        /// </summary>
        public static void HomeAirportStep(World w)
        {
            long now = w.AbsoluteMinute;
            int hour = w.Clock.Hour, minute = w.Clock.Minute;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.ToList())
                {
                    switch (plane.Location)
                    {
                        case PlaneLocation.Landing:
                            if (now - plane.PhaseStartMinute >= HomeAirportTiming.LandingRoll) EndLandingRoll(w, p, plane, now);
                            break;
                        case PlaneLocation.AtGate:
                            if (plane.GateStartHour == 255)
                            {
                                if (!plane.Unloaded && now - plane.PhaseStartMinute >= HomeAirportTiming.TaxiToGate)
                                {
                                    plane.Unloaded = true;
                                    EvaluatePassengers(w, p, plane);
                                }
                                bool late = now - plane.PhaseStartMinute >= 60;
                                if (plane.Unloaded && (minute >= HomeAirportTiming.ParkedMinute || late))
                                {
                                    plane.Location = w.HomeCity;
                                    PublishLanded(w, p, plane, false);
                                }
                            }
                            else
                            {
                                int s = plane.GateStartHour;
                                if ((s == hour && minute >= HomeAirportTiming.BoardingEndMinute) || s < hour || (s == 23 && hour == 0))
                                {
                                    plane.Location = PlaneLocation.TakingOff;
                                    plane.PhaseStartMinute = now;
                                }
                            }
                            break;
                        case PlaneLocation.TakingOff:
                            if (now - plane.PhaseStartMinute >= HomeAirportTiming.TakeOff)
                            {
                                if (HasEntry(plane)) BookFlight(w, p, plane, plane.Plan.NextFlight);
                                plane.Location = PlaneLocation.Airborne;
                            }
                            break;
                    }
                }
            }
        }

        static void PublishLanded(World w, Player p, Plane plane, bool noGate)
        {
            var e = HasEntry(plane) ? Current(plane) : FlightEntry.Empty;
            w.Events.Publish(new FlightLanded { Player = p, Plane = plane, Entry = e, City = w.HomeCity, AtHome = true, NoGate = noGate });
            var home = w.Data.Cities.Find(w.HomeCity);
            if (home != null) { plane.GlobeX = home.GlobeX; plane.GlobeY = home.GlobeY; }
        }

        /// <summary>End of the landing roll at home (planes-and-flights.md §11.5).</summary>
        static void EndLandingRoll(World w, Player p, Plane plane, long now)
        {
            if (!HasEntry(plane)) { plane.Location = w.HomeCity; return; }
            ref var e = ref Current(plane);
            if (e.Kind != FlightKind.Auto && e.Kind != FlightKind.Freight && e.Gate != GateCode.NotHome)
            {
                if (e.Gate == GateCode.None)
                {
                    int ri = e.Kind == FlightKind.Route ? w.RouteIndexOf(e.ObjectId) : -1;
                    int unhappy = (e.Passengers / CustomersPerPerson + 1) * CustomersPerPerson;
                    w.Events.Publish(new PassengersEvaluated { Player = p, Plane = plane, Unsatisfied = unhappy });
                    plane.Location = w.HomeCity;
                    w.Advise(p, 2354, plane.Name, w.City(e.To).Name);
                    p.ChangeImage(-2);
                    if (ri >= 0 && p.Image % 10 == 0)
                    {
                        var rr = p.RentRoutes[ri];
                        rr.Image = rr.Image > 1 ? rr.Image - 2 : 0;
                    }
                    EvaluatePassengers(w, p, plane);
                    PublishLanded(w, p, plane, true);
                }
                else
                {
                    plane.Location = PlaneLocation.AtGate;
                    plane.GateStartHour = 255;
                    plane.PhaseStartMinute = now;
                    plane.Unloaded = false;
                }
            }
            else
            {
                plane.Location = e.To;
                PublishLanded(w, p, plane, false);
            }
        }

        // ------------------------------------------------------------------ incidents (every 4 minutes)

        /// <summary>
        /// Every 4th game minute (sim-core.md §2.4, §6.3–6.4, planes-and-flights.md §7.6): for airborne
        /// planes whose flight started at least an hour ago, a quality incident roll (only when condition
        /// &lt; 80) and a seasonal roll from one generator seeded Date+hour+minute (recreated per plane,
        /// QUIRK); then the globe position update.
        /// </summary>
        public static void Incidents(World w)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour, minute = w.Clock.Minute;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.ToList())
                {
                    if (plane.Location != PlaneLocation.Airborne || !HasEntry(plane)) continue;
                    ref var e = ref Current(plane);
                    if (!(date > e.StartDate || (date == e.StartDate && hour >= e.StartHour + 1))) continue;

                    var r = new TeakRand(date + hour + minute);
                    if (plane.Condition < 80 && r.Rand(5000) > plane.Condition + 4920)
                    {
                        if (e.IsPassengerFlight && e.Passengers > 0)
                        {
                            w.Rules.Get<IIncidentEconomics>().QualityIncident(w, p);
                            if (e.Kind == FlightKind.Route)
                            {
                                int ri = w.RouteIndexOf(e.ObjectId);
                                if (ri >= 0) p.RentRoutes[ri].Image = p.RentRoutes[ri].Image * 8 / 10;
                            }
                            int code = r.Rand(9);
                            plane.AddIncident(date, w.Clock.Time, code);
                            w.Advise(p, 2360 + code, plane.Name);
                            w.Events.Publish(new PlaneIncident { Player = p, Plane = plane, Code = code });
                        }
                    }
                    if (r.Rand(5000) > 4980)
                    {
                        int season = w.Clock.Season;
                        bool exposed = season == 1 ? plane.Equipment[Equip.Safety] != 2 : plane.Equipment[Equip.Electronics] != 2;
                        if (exposed)
                        {
                            w.Rules.Get<IIncidentEconomics>().SeasonalIncident(w, p);
                            plane.AddIncident(date, w.Clock.Time, 10 + season);
                            w.Advise(p, 2370 + season, plane.Name);
                            w.Events.Publish(new PlaneIncident { Player = p, Plane = plane, Code = 10 + season, Seasonal = true });
                        }
                    }

                    if (hour == 8 || (hour >= 9 && hour < 18 && !w.CallItADay) || minute == 0) UpdateGlobePosition(w, plane, ref e);
                }
            }
        }

        static void UpdateGlobePosition(World w, Plane plane, ref FlightEntry e)
        {
            var a = w.Data.Cities.Find(e.From);
            var b = w.Data.Cities.Find(e.To);
            if (a == null || b == null) return;
            long adv = (e.LandHour - e.StartHour - 1) + 24L * (e.LandDate - e.StartDate);
            adv *= 60000;
            if (adv == 0) adv = 60000;
            long time = w.Clock.Time;
            if (e.StartHour * 60000L <= time) adv = (time - (e.StartHour + 1) * 60000L) * 256 / adv;
            else adv = (24 * 60000L - ((e.StartHour + 1) * 60000L - time)) * 256 / adv;
            if (Math.Abs(b.GlobeX - a.GlobeX) < 180) plane.GlobeX = (int)((a.GlobeX * (256 - adv) + b.GlobeX * adv) / 256);
            else if (b.GlobeX > a.GlobeX) plane.GlobeX = (int)(a.GlobeX + (-(360 - (b.GlobeX - a.GlobeX)) * adv) / 256);
            else plane.GlobeX = (int)(a.GlobeX + ((360 - (a.GlobeX - b.GlobeX)) * adv) / 256);
            plane.GlobeY = (int)((a.GlobeY * (256 - adv) + b.GlobeY * adv) / 256);
        }

        // ------------------------------------------------------------------ breakdowns (hour change)

        /// <summary>
        /// Technical problems (planes-and-flights.md §7.5, sim-core.md §6.2): at each hour change 06–16,
        /// not after call-it-a-day, local human only, if no plane has a problem and the fleet has ≥ 2
        /// planes. One generator seeded Date+hour+minute; planes last→first; candidates are airborne and
        /// land this hour today.
        /// </summary>
        public static void Breakdowns(World w)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            if (w.CallItADay || hour <= 5 || hour >= 17) return;
            var p = w.Local;
            if (p.IsOut || p.IsComputer) return;
            foreach (var pl in p.Planes.Items) if (pl.Problem != 0) return;
            if (p.Planes.Count < 2) return;
            var r = new TeakRand(date + hour + w.Clock.Minute);
            for (int c = p.Planes.Capacity - 1; c >= 0; c--)
            {
                var plane = p.Planes[c];
                if (plane == null || plane.Location != PlaneLocation.Airborne || !HasEntry(plane)) continue;
                ref var e = ref Current(plane);
                if (e.LandDate != date || e.LandHour != hour) continue;
                if (!(r.Rand(10) == 0 && r.Rand(100) > plane.Condition && plane.Condition < 90)) continue;

                int a = 4 + r.Rand(101 - plane.Condition) / 3;
                plane.Problem = a + (r.Rand(4) == 0 ? 15 : 0);
                Contract spare = null;
                if (e.To == w.HomeCity) plane.Problem = Math.Max(4, plane.Problem - 15);
                else if (plane.Problem > 20)
                {
                    spare = new Contract
                    {
                        From = w.HomeCity, To = e.To, Persons = 0, Date = date,
                        LastDate = date + (plane.Problem - (24 - hour)) / 24, InPlan = 0, Okay = 0, Premium = 0, Penalty = 0,
                    };
                    p.Contracts.Add(spare);
                }
                if (p.Owner == Owner.Human && !p.IsOut)
                {
                    if (spare != null) w.Events.Publish(new LetterSent(p, "Lett", 507, plane.Name, plane.Problem, w.City(w.HomeCity).Name, w.City(e.To).Name));
                    else w.Events.Publish(new LetterSent(p, "Lett", 506, plane.Name, plane.Problem));
                }
                if (!w.CallItADay) w.Advise(p, 2309, plane.Name);
                w.Events.Publish(new PlaneBreakdown { Player = p, Plane = plane, Hours = plane.Problem, SparePartContract = spare });
                break;
            }
        }

        // ------------------------------------------------------------------ refits on the ground

        /// <summary>
        /// Applies pending equipment changes of planes on the ground at a city while the owner is not in
        /// the equipment room (or the day has ended); humans pay (reason 2110), computers switch free
        /// (planes-and-flights.md §6.2).
        /// </summary>
        public static void ApplyRefits(World w)
        {
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                if (p.InPlanePropRoom && !w.CallItADay) continue;
                foreach (var plane in p.Planes.Items)
                {
                    if (!PlaneLocation.IsOnGround(plane.Location)) continue;
                    long cost = 0;
                    for (int i = 0; i < Equip.Count; i++)
                    {
                        if (plane.Equipment[i] == plane.EquipmentTarget[i]) continue;
                        cost += Equip.ChangeCost(i, plane.Equipment[i], plane.EquipmentTarget[i], plane.Seats);
                        plane.Equipment[i] = plane.EquipmentTarget[i];
                    }
                    if (cost != 0 && p.IsHuman)
                    {
                        p.Book(-cost, MoneyReason.PlaneRefit, plane.Name);
                        w.Events.Publish(new PlaneRefitted { Player = p, Plane = plane, Cost = cost });
                    }
                }
            }
        }

        /// <summary>Sum of all pending refit costs of the fleet ("CalcPlanePropSum", §6.2).</summary>
        public static long PendingRefitCost(Player p)
        {
            long s = 0;
            foreach (var plane in p.Planes.Items)
                for (int i = 0; i < Equip.Count; i++)
                    if (plane.Equipment[i] != plane.EquipmentTarget[i])
                        s += Equip.ChangeCost(i, plane.Equipment[i], plane.EquipmentTarget[i], plane.Seats);
            return s;
        }
    }
}
