using System;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.AI
{
    /// <summary>Where a robot takes passenger orders from (ai-robots.md §5.3 limits differ per source).</summary>
    public enum OfferSource { LastMinute, TravelAgency, Abroad }

    public static partial class RobotExecutor
    {
        /// <summary>Acceptance limits of §5.3 per source: free-point horizon and the pass-2 distance bands (km).</summary>
        readonly struct Limits
        {
            public readonly int FreeHorizon, MaxKm, MidKm, NearKm;
            public Limits(int horizon, int max, int mid, int near) { FreeHorizon = horizon; MaxKm = max; MidKm = mid; NearKm = near; }
            public static Limits Of(OfferSource s) =>
                s == OfferSource.LastMinute ? new Limits(47, 15000, 6000, 4000)
                : s == OfferSource.TravelAgency ? new Limits(71, 15000, 6000, 3000)
                : new Limits(47, 9000, 5000, 3000);
        }

        // ------------------------------------------------------------------ rooms

        static void Agent(Ctx c, OfferSource src)
        {
            var w = c.W; var p = c.P; var s = c.S;
            if (!s.RouteMode)
            {
                var board = src == OfferSource.LastMinute ? OfferBoard.LastMinute : OfferBoard.TravelAgency;
                w.Execute(new VisitOfferBoardCommand(p.Index, board));
                TakeOrders(c, src == OfferSource.LastMinute ? w.LastMinute : w.TravelAgency, src, -1);
                w.Execute(new VisitOfferBoardCommand(p.Index, board, -1, 3));
            }
            s.WorkCountdown = c.Work(src == OfferSource.LastMinute ? RobotAction.LastMinute : RobotAction.TravelAgency);
            s.TimeAgents = w.Clock.Time;
        }

        static void CargoOffice(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            if (c.Has(RobotFeature.ShortFlights) && p.Planes.Count == 4) return; // the original leaves before the countdown
            if (!s.RouteMode)
            {
                w.Execute(new VisitOfferBoardCommand(p.Index, OfferBoard.FreightOffice));
                TakeCargo(c, w.FreightOffice, -1);
                w.Execute(new VisitOfferBoardCommand(p.Index, OfferBoard.FreightOffice));
            }
            s.WorkCountdown = c.Work(RobotAction.Cargo);
            s.TimeAgents = w.Clock.Time;
        }

        /// <summary>Office step 3: phone every branch city of the robot or a cooperating airline (no call charges, §9.2).</summary>
        static void AbroadOrders(Ctx c)
        {
            var w = c.W; var p = c.P;
            int home = w.CityIndex(w.HomeCity);
            for (int n = 0; n < w.ForeignContracts.Length; n++)
            {
                if (n == home) continue;
                bool present = p.BranchAt(n).Rank != 0;
                for (int k = 0; k < Player.Count && !present; k++)
                    if (!w.Players[k].IsOut && p.Cooperates[k] && w.Players[k].BranchAt(n).Rank != 0) present = true;
                if (!present) continue;

                w.Execute(new VisitOfferBoardCommand(p.Index, OfferBoard.Branch, n));
                TakeOrders(c, w.ForeignContracts[n], OfferSource.Abroad, n);
                if (!c.S.RouteMode && !(c.Has(RobotFeature.ShortFlights) && p.Planes.Count == 4))
                    TakeCargo(c, w.ForeignFreight[n], n);
                w.Execute(new VisitOfferBoardCommand(p.Index, OfferBoard.Branch, n, 3));
            }
        }

        // ------------------------------------------------------------------ §5.3 passenger orders

        /// <summary>
        /// The plane's free point (§5.3): destination and landing + 1 h of its last plan entry, in hours from
        /// today 00:00; an empty plan starts at its start city now + 2 h (day 0 at 9:00: + slot/2). Never
        /// earlier than now + 2 h.
        /// </summary>
        public static void FreePoint(World w, Player p, Plane plane, out int city, out int hour)
        {
            int now = w.Clock.Hour, date = w.Clock.Date;
            city = plane.Plan.StartCity;
            hour = now + 2;
            if (date == 0 && now == 9) hour += p.Index / 2;
            var f = plane.Plan.Entries;
            for (int d = FlightPlan.Size - 1; d >= 0; d--)
                if (f[d].Kind != FlightKind.None)
                {
                    city = f[d].To;
                    hour = f[d].LandHour + 1 + (f[d].LandDate - date) * 24;
                    break;
                }
            if (hour < now + 2) hour = now + 2;
        }

        static bool ShortFlightsReject(Ctx c, Contract o) =>
            c.Has(RobotFeature.ShortFlights) && c.P.Planes.Count == 4 && (c.W.Distance(o.From, o.To) > 3000000 || o.Persons <= 90);

        static bool TutorialLimitOk(Ctx c, OfferSource src)
        {
            if (src == OfferSource.Abroad || !c.Has(RobotFeature.TutorialLimit)) return true;
            // Orders taken since the game start (the original reads the never-cleared day value).
            return c.P.Statistics.Total(StatId.Orders) < c.W.Clock.Date * 2 + 3 + (c.Slot & 1);
        }

        static void TakeOrders(Ctx c, ContractBoard board, OfferSource src, int cityIndex)
        {
            var w = c.W; var p = c.P;
            var lim = Limits.Of(src);
            foreach (var plane in p.Planes.ToList())
                for (int level = 0; level <= 2; level++)
                {
                    FreePoint(w, p, plane, out int city, out int hour);
                    if (hour < 0 || hour >= lim.FreeHorizon) continue;
                    if (level < 2)
                    {
                        for (int e = ContractBoard.Slots - 1; e >= 0; e--)
                        {
                            var o = board.Offers[e];
                            if (o == null || o.Premium == 0 || o.From != city || ShortFlightsReject(c, o)) continue;
                            int cost = w.ContractBasePrice(city, o.From, 100);
                            if (cost <= (long)o.Premium * 8 / 10 && TutorialLimitOk(c, src))
                            {
                                TakeOrder(c, board, src, cityIndex, e, plane, hour);
                                break;
                            }
                        }
                    }
                    else
                    {
                        int best = -1, min = int.MaxValue;
                        for (int e = ContractBoard.Slots - 1; e >= 0; e--)
                        {
                            var o = board.Offers[e];
                            if (o == null || o.Premium == 0) continue;
                            if (hour + w.FlightHours(city, o.From, plane.Speed) >= 47) continue;
                            int dist = w.Distance(city, o.From);
                            if (dist >= min || ShortFlightsReject(c, o)) continue;
                            min = dist; best = e;
                        }
                        if (best < 0) continue;
                        if (min < lim.MaxKm * 1000 && (min < lim.MidKm * 1000 || hour < 34) && (min < lim.NearKm * 1000 || hour < 24))
                        {
                            var o = board.Offers[best];
                            int cost = w.ContractBasePrice(city, o.From, 100);
                            if (cost <= o.Premium && TutorialLimitOk(c, src)) TakeOrder(c, board, src, cityIndex, best, plane, hour);
                        }
                    }
                }
        }

        /// <summary>
        /// Accepts one passenger offer and writes it at the plane's free point (§5.3 "On accept"). Not
        /// checked, like the original: seats and the allowed date window (Q7).
        /// </summary>
        static bool TakeOrder(Ctx c, ContractBoard board, OfferSource src, int cityIndex, int slot, Plane plane, int hour)
        {
            var w = c.W; var p = c.P;
            if (!RobotShortcuts.HasFreeEntries(plane, 1)) return false;
            int id;
            var kind = src == OfferSource.LastMinute ? OfferBoard.LastMinute : src == OfferSource.TravelAgency ? OfferBoard.TravelAgency : OfferBoard.Branch;
            var cmd = new AcceptContractCommand(p.Index, kind, slot, cityIndex) { IgnoreOpeningHours = true };
            if (w.Execute(cmd).Ok) id = cmd.ContractId;
            else if (src == OfferSource.Abroad) id = RobotShortcuts.AcceptForeignContract(w, p, cityIndex, slot); // a cooperating airline's branch
            else return false;
            if (id == 0) return false;

            RobotShortcuts.AppendFlight(w, p, plane, FlightKind.Contract, id, w.Clock.Date + hour / 24, hour % 24);
            ContractUsage.UpdateContracts(w, p);
            RobotShortcuts.DelayFlightsIfNecessary(w, p);
            c.Log($"takes order {src} {Short(w, board.Offers[slot])} for {plane.Name}");
            return true;
        }

        static string Short(World w, Contract o) => o == null ? "?" : ContractUsage.CityPair(w, o.From, o.To);

        // ------------------------------------------------------------------ §5.4 cargo

        static void TakeCargo(Ctx c, FreightBoard board, int cityIndex)
        {
            var w = c.W; var p = c.P;
            long bonus = c.Has(RobotFeature.MuchFreight) && c.Slot == 2 ? 150000 : 0;
            bool office = cityIndex < 0;
            bool free = c.Has(RobotFeature.FreeFreight);
            foreach (var plane in p.Planes.ToList())
            {
                int cap = plane.Seats / 10;
                if (cap <= 0) continue;
                for (int level = 0; level <= 2; level++)
                {
                    FreePoint(w, p, plane, out int city, out int hour);
                    if (hour < 0 || hour >= 71) continue;
                    if (level < 2)
                    {
                        for (int e = FreightBoard.Slots - 1; e >= 0; e--)
                        {
                            var o = board.Offers[e];
                            if (o == null || !IsOpen(o, office) || o.From != city) continue;
                            long cost = (long)w.ContractBasePrice(o.From, o.To, 100) * 2 * (o.Tons / cap + 1);
                            if (cost <= (long)o.Premium * 8 / 10 + bonus || (free && o.Premium == 0))
                            {
                                TakeFreight(c, board, cityIndex, e, plane, hour);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Visit the open jobs by increasing distance of their departure city. Q15: the original does
                        // not recompute the free point between acceptances; FixCargoFreePoint moves it on after
                        // each accepted job and restarts the scan from the new point.
                        int minMin = 0, taken = 0;
                        while (true)
                        {
                            int best = -1, min = int.MaxValue;
                            for (int e = FreightBoard.Slots - 1; e >= 0; e--)
                            {
                                var o = board.Offers[e];
                                if (o == null || !IsOpen(o, office)) continue;
                                if (hour + w.FlightHours(city, o.From, plane.Speed) >= 47) continue;
                                int dist = w.Distance(city, o.From);
                                if (dist < min && dist > minMin) { min = dist; best = e; }
                            }
                            if (best < 0) break;
                            minMin = min;
                            if (min < 15000000 && (min < 6000000 || hour < 58) && (min < 3000000 || hour < 48))
                            {
                                var o = board.Offers[best];
                                long cost = (long)w.ContractBasePrice(o.From, o.To, 100) * (o.Tons / cap + 1) + w.ContractBasePrice(city, o.From, 100);
                                if ((cost <= o.Premium + bonus || (free && o.Premium == 0)) && TakeFreight(c, board, cityIndex, best, plane, hour)
                                    && w.AI.Options.FixCargoFreePoint)
                                {
                                    if (++taken >= FreightBoard.Slots) break;
                                    FreePoint(w, p, plane, out city, out hour);
                                    if (hour < 0 || hour >= 71) break;
                                    minMin = 0;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Open cargo offer: premium ≥ 0 at the cargo office (0 = free relief cargo), &gt; 0 on branch lists.</summary>
        static bool IsOpen(FreightContract o, bool office) => office ? o.Premium >= 0 : o.Premium > 0;

        /// <summary>Accepts a cargo job and writes one flight per trip of (seats / 10) tons (§5.4 "On accept").</summary>
        static bool TakeFreight(Ctx c, FreightBoard board, int cityIndex, int slot, Plane plane, int hour)
        {
            var w = c.W; var p = c.P;
            var offer = board.Offers[slot];
            int cap = plane.Seats / 10;
            int trips = (offer.Tons + cap - 1) / cap;
            if (trips <= 0) trips = 1;
            if (!RobotShortcuts.HasFreeEntries(plane, trips * 2)) return false;
            int id;
            var cmd = new AcceptFreightCommand(p.Index, cityIndex < 0 ? OfferBoard.FreightOffice : OfferBoard.BranchFreight, slot, cityIndex);
            if (w.Execute(cmd).Ok) id = cmd.FreightId;
            else if (cityIndex >= 0) id = RobotShortcuts.AcceptForeignFreight(w, p, cityIndex, slot);
            else return false;
            if (id == 0) return false;

            for (int t = 0; t < trips; t++)
            {
                RobotShortcuts.AppendFlight(w, p, plane, FlightKind.Freight, id, w.Clock.Date + hour / 24, hour % 24);
                RobotShortcuts.DelayFlightsIfNecessary(w, p);
            }
            c.Log($"takes cargo {ContractUsage.CityPair(w, offer.From, offer.To)} {offer.Tons}t in {trips} trips for {plane.Name}");
            return true;
        }
    }
}
