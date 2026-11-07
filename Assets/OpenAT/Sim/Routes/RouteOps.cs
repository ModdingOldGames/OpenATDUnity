using System;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Routes
{
    /// <summary>
    /// Route licences, prices and the daily route bookkeeping (contracts-and-routes.md §3.2–3.6,
    /// §3.10, sim-core.md §2.7 step 11).
    /// </summary>
    public static class RouteOps
    {
        /// <summary>Default economy ticket when licensing: RefPrice × 2 rounded down to a multiple of 10 (§3.3, §3.6).</summary>
        public static int DefaultTicket(World w, int from, int to) => w.FuelCost.Estimate(w, null, from, to, 800, 800) * 3 / 180 * 2 * 2 / 10 * 10;

        /// <summary>
        /// "RentRoute" helper: both directions of the pair get rank = 1 + number of airlines holding that
        /// direction, the given monthly fee, image/load 0, LastFlown = AvgFlown = 30, default prices.
        /// </summary>
        public static void Rent(World w, Player p, int city1, int city2, int rent)
        {
            for (int c = 0; c < w.RouteTable.Count; c++)
            {
                var r = w.RouteTable[c];
                if (!((r.From == city1 && r.To == city2) || (r.From == city2 && r.To == city1))) continue;
                int n = 1;
                foreach (var q in w.Players) if (q.RentRoutes[c].Rank != 0) n++;
                var rr = p.RentRoutes[c];
                rr.Rank = n;
                rr.Rent = rent;
                rr.Image = 0;
                rr.LastFlown = 30;
                rr.AvgFlown = 30;
                rr.Load = 0;
                rr.DaysWithLoss = 0;
                rr.TicketPrice = DefaultTicket(w, city1, city2);
                rr.TicketPriceFC = rr.TicketPrice * 2;
            }
        }

        /// <summary>
        /// Route office eligibility (§3.3): touches home, or shares an endpoint with a route where this
        /// airline carries ≥ 20 % of demand; not within 7 days of a revocation.
        /// </summary>
        public static bool IsRentable(World w, Player p, int routeIndex)
        {
            var routes = w.RouteTable;
            var rr = p.RentRoutes[routeIndex];
            if (rr.Rank != 0) return false;
            if (rr.DaysLow < 7) return false;
            var d = routes[routeIndex];
            if (d.Touches(w.HomeCity)) return true;
            for (int c = routes.Count - 1; c >= 0; c--)
            {
                if (p.RentRoutes[c].RouteShare < 20) continue;
                var r = routes[c];
                if (r.From == d.From || r.From == d.To || r.To == d.From || r.To == d.To) return true;
            }
            return false;
        }

        /// <summary>Rank a new licensee would get: 1 + the highest rank any active airline holds.</summary>
        public static int NextRank(World w, int routeIndex)
        {
            int rank = 1;
            foreach (var q in w.Players)
                if (!q.IsOut && q.RentRoutes[routeIndex].Rank >= rank) rank = q.RentRoutes[routeIndex].Rank + 1;
            return rank;
        }

        /// <summary>Inhabitants above which the route advisor calls a city big.</summary>
        public const int AdvisorBigCity = 2000000;

        /// <summary>
        /// Route advisor remark on hovering a route (routes-and-orders.md §2.6). <c>Text</c> is the Advi id:
        /// 8020 "blocked for now" (unheld and within 7 days of a revocation, nothing else), otherwise the
        /// city-size verdict 8000 (both big, args <c>City1</c>, <c>City2</c>), 8001 (one big, arg <c>City1</c>)
        /// or 8002 (neither, arg <c>City1</c> = destination). <c>Remark</c> is appended: 8010 popular
        /// (factor &gt; level + 0.5), 8011 unpopular (factor &lt; level − 0.3), −1 none. Talent does not matter.
        /// </summary>
        public static (int Text, int City1, int City2, int Remark) Advice(World w, Player p, int routeIndex)
        {
            var rr = p.RentRoutes[routeIndex];
            if (rr.Rank == 0 && rr.DaysLow < 7) return (8020, 0, 0, -1);
            var r = w.RouteTable[routeIndex];
            bool bigFrom = w.City(r.From).Population > AdvisorBigCity, bigTo = w.City(r.To).Population > AdvisorBigCity;
            (int, int, int) verdict = bigFrom && bigTo ? (8000, r.From, r.To)
                : bigFrom ? (8001, r.From, 0)
                : bigTo ? (8001, r.To, 0)
                : (8002, r.To, 0);
            int remark = r.Factor > r.Level + 0.5 ? 8010 : r.Factor < r.Level - 0.3 ? 8011 : -1;
            return (verdict.Item1, verdict.Item2, verdict.Item3, remark);
        }

        /// <summary>True if any plan entry of the airline uses the route or its reverse (drop refusal, §3.4).</summary>
        public static bool InUse(World w, Player p, int routeIndex)
        {
            int rev = w.ReverseRouteIndex(routeIndex);
            int id1 = w.RouteTable[routeIndex].Id, id2 = rev >= 0 ? w.RouteTable[rev].Id : int.MinValue;
            foreach (var pl in p.Planes.Items)
                if (pl.Plan.Contains(FlightKind.Route, id1, id2)) return true;
            return false;
        }

        /// <summary>
        /// Licensing at the route office (§3.3): no up-front payment; both directions get the max-based
        /// rank, the table fee, default prices and cooldown counter 0.
        /// </summary>
        public static void RentAtOffice(World w, Player p, int routeIndex)
        {
            int rev = w.ReverseRouteIndex(routeIndex);
            int rank = NextRank(w, rev >= 0 ? rev : routeIndex);
            var r = w.RouteTable[routeIndex];
            Rent(w, p, r.From, r.To, r.Rent);
            p.RentRoutes[routeIndex].DaysLow = 0;
            p.RentRoutes[routeIndex].Rank = rank;
            if (rev >= 0)
            {
                p.RentRoutes[rev].DaysLow = 0;
                p.RentRoutes[rev].Rank = rank;
            }
            w.Events.Publish(new RouteLicenceChanged { Player = p, RouteIndex = routeIndex, Change = "rented" });
        }

        /// <summary>Voluntary drop (§3.4): both directions rank 0 with no cooldown (99); higher-ranked holders move up.</summary>
        public static void Drop(World w, Player p, int routeIndex)
        {
            int rev = w.ReverseRouteIndex(routeIndex);
            foreach (var q in w.Players)
            {
                if (q.IsOut) continue;
                if (q.RentRoutes[routeIndex].Rank > p.RentRoutes[routeIndex].Rank) q.RentRoutes[routeIndex].Rank--;
                if (rev >= 0 && q.RentRoutes[rev].Rank > p.RentRoutes[rev].Rank) q.RentRoutes[rev].Rank--;
            }
            p.RentRoutes[routeIndex].Rank = 0;
            p.RentRoutes[routeIndex].DaysLow = 99;
            if (rev >= 0) { p.RentRoutes[rev].Rank = 0; p.RentRoutes[rev].DaysLow = 99; }
            w.Events.Publish(new RouteLicenceChanged { Player = p, RouteIndex = routeIndex, Change = "dropped" });
        }

        /// <summary>
        /// "RouteWegnehmen" (§3.5): removes one direction's licence (or transfers it to
        /// <paramref name="newOwner"/>), higher-ranked holders move up, cooldown counter 0, and future plan
        /// entries on it are deleted.
        /// </summary>
        public static void Revoke(World w, Player p, int routeIndex, int newOwner = -1)
        {
            if (newOwner == -1 || w.Players[newOwner].RentRoutes[routeIndex].Rank != -1)
            {
                foreach (var q in w.Players)
                    if (!q.IsOut && q.RentRoutes[routeIndex].Rank > p.RentRoutes[routeIndex].Rank) q.RentRoutes[routeIndex].Rank--;
            }
            else
            {
                var n = w.Players[newOwner].RentRoutes[routeIndex];
                var route = w.RouteTable[routeIndex];
                n.Rank = p.RentRoutes[routeIndex].Rank;
                n.DaysLow = 0;
                n.Rent = route.Rent;
                n.Image = 0;
                n.LastFlown = 30;
                n.AvgFlown = 30;
                n.Load = 0;
                n.DaysWithLoss = 0;
                n.TicketPrice = DefaultTicket(w, route.From, route.To);
                n.TicketPriceFC = n.TicketPrice * 2;
            }
            p.RentRoutes[routeIndex].Rank = 0;
            p.RentRoutes[routeIndex].DaysLow = 0;

            int id = w.RouteTable[routeIndex].Id;
            int date = w.Clock.Date;
            foreach (var pl in p.Planes.ToList())
            {
                var f = pl.Plan.Entries;
                for (int e = FlightPlan.Size - 1; e >= 0; e--)
                {
                    if (f[e].Kind != FlightKind.Route || f[e].ObjectId != id) continue;
                    if (f[e].StartDate > date || (f[e].StartHour >= 1 && f[e].StartDate == date))
                    {
                        if (e == 0) pl.Plan.StartCity = f[e].To;
                        f[e].Kind = FlightKind.None;
                        pl.Plan.UpdatePointers(date, w.Clock.Hour);
                        FlightPlanner.Check(w, p, pl);
                        GatePlanner.PlanGates(w, p);
                    }
                }
            }
            w.Events.Publish(new RouteLicenceChanged { Player = p, RouteIndex = routeIndex, Change = "revoked" });
        }

        /// <summary>
        /// Daily route bookkeeping (sim-core.md §2.7 step 11, contracts-and-routes.md §3.5): LastFlown +1 (and the
        /// AvgFlown average with <see cref="GameOptions.FixRouteAvgFlown"/>),
        /// share = (4·share + carried·100/potential)/5 (≤ 100), low-use days, warning letters on days 10
        /// and 15 and revocation on day 20 (one letter per pair); unlicensed routes count the cooldown up to 99.
        /// </summary>
        public static void DailyUpdate(World w, Player p)
        {
            var demand = w.Rules.Get<IRouteDemandRule>();
            for (int c = 0; c < w.RouteTable.Count; c++)
            {
                var rr = p.RentRoutes[c];
                var r = w.RouteTable[c];
                if (rr.Rank != 0)
                {
                    if (rr.LastFlown < 99) rr.LastFlown++;
                    // The original sets AvgFlown to 30 when licensing and never updates it, so the "rarely flown"
                    // share penalty is the same for every holder. FixRouteAvgFlown keeps it as a moving average
                    // of the days since the last flight, weighted like the route share (4:1).
                    if (w.Options.FixRouteAvgFlown) rr.AvgFlown = (rr.AvgFlown * 4 + rr.LastFlown) / 5;
                    int potential = demand.Potential(w, r);
                    if (potential <= 0) potential = 1; // the original would divide by zero (open question §11.5)
                    rr.RouteShare = (rr.RouteShare * 4 + rr.CarriedToday * 100 / potential) / 5;
                    if (rr.RouteShare > 100) rr.RouteShare = 100;
                    rr.CarriedToday = 0;
                    if (rr.RouteShare < 10)
                    {
                        rr.DaysLow++;
                        if (p.Owner == Owner.Human && !p.IsOut && r.From < r.To)
                        {
                            if (rr.DaysLow == 10) w.Events.Publish(new LetterSent(p, "Lett", 3000, w.City(r.From).Name, w.City(r.To).Name, rr.RouteShare, 10));
                            if (rr.DaysLow == 15) w.Events.Publish(new LetterSent(p, "Lett", 3000, w.City(r.From).Name, w.City(r.To).Name, rr.RouteShare, 5));
                            if (rr.DaysLow == 20) w.Events.Publish(new LetterSent(p, "Lett", 3100, w.City(r.From).Name, w.City(r.To).Name));
                        }
                        if (rr.DaysLow == 20) Revoke(w, p, c);
                    }
                    else rr.DaysLow = 0;
                }
                else if (rr.DaysLow < 99) rr.DaysLow++;
            }
        }

        /// <summary>
        /// Ticket price setting from the planner route block (§3.6): economy clamped to [0, 16·RefPrice],
        /// first class to [0, 48·RefPrice]; the economy price is copied to the reverse direction (first
        /// class is not); existing future entries are re-priced ("UpdateTicketpreise").
        /// </summary>
        public static void SetPrices(World w, Player p, int routeIndex, int economy, int firstClass)
        {
            var r = w.RouteTable[routeIndex];
            int refPrice = w.ReferencePrice(r.From, r.To);
            economy = Math.Max(0, Math.Min(economy, 16 * refPrice));
            firstClass = Math.Max(0, Math.Min(firstClass, 48 * refPrice));
            var rr = p.RentRoutes[routeIndex];
            rr.TicketPrice = economy;
            rr.TicketPriceFC = firstClass;
            UpdateEntryPrices(w, p, routeIndex, rr.TicketPrice, rr.TicketPriceFC);
            int rev = w.ReverseRouteIndex(routeIndex);
            if (rev >= 0)
            {
                p.RentRoutes[rev].TicketPrice = economy;
                UpdateEntryPrices(w, p, rev, p.RentRoutes[rev].TicketPrice, p.RentRoutes[rev].TicketPriceFC);
            }
        }

        /// <summary>Price presets ½, 1, 2, 4 × RefPrice (rounded to ×10); first class = 2 × economy.</summary>
        public static int Preset(World w, int routeIndex, int preset)
        {
            var r = w.RouteTable[routeIndex];
            int refPrice = w.ReferencePrice(r.From, r.To);
            int v;
            switch (preset) { case 0: v = refPrice / 2; break; case 1: v = refPrice; break; case 2: v = refPrice * 2; break; default: v = refPrice * 4; break; }
            return v / 10 * 10;
        }

        /// <summary>"UpdateTicketpreise": future entries of the route take the new prices; raised prices recompute passengers.</summary>
        public static void UpdateEntryPrices(World w, Player p, int routeIndex, int economy, int firstClass)
        {
            int id = w.RouteTable[routeIndex].Id;
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var rule = w.Rules.Get<IRoutePassengerRule>();
            foreach (var pl in p.Planes.ToList())
            {
                var f = pl.Plan.Entries;
                for (int d = FlightPlan.Size - 1; d >= 0; d--)
                {
                    ref var e = ref f[d];
                    if (e.Kind != FlightKind.Route || e.ObjectId != id || !e.IsFuture(date, hour)) continue;
                    if (economy > e.TicketPrice || firstClass > e.TicketPriceFC)
                    {
                        e.TicketPrice = economy;
                        e.TicketPriceFC = firstClass;
                        e.HoursBefore = ((e.StartDate - date) * 24 + (e.StartHour - hour)) & 0xFF;
                        rule.Calculate(w, p, pl, ref e);
                    }
                    e.TicketPrice = economy;
                    e.TicketPriceFC = firstClass;
                }
            }
        }

        /// <summary>Nightly demand update (sim-core.md §2.6 step 22): day 0 = potential × 4.27, then (6·old + that)/7.</summary>
        public static void UpdateDemand(World w)
        {
            var rule = w.Rules.Get<IRouteDemandRule>();
            foreach (var r in w.RouteTable) r.Demand = rule.NextDemand(w, r);
        }
    }
}
