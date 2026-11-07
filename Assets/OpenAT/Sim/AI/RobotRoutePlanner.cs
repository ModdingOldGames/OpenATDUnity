using System;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// "RobotPlanRoutes" (ai-robots.md §6.2): every night (per-airline step 22, after plane upkeep) a robot in
    /// route mode rebuilds its schedule over its route legs and re-prices them.
    /// </summary>
    public static class RobotRoutePlanner
    {
        /// <summary>Lead time written into the robot's route entries (capped at 48 by the passenger rule).</summary>
        public const int HoursBefore = 50;
        /// <summary>A plane stops receiving flights once it has one landing this many days ahead.</summary>
        public const int FillDays = 5;

        const int Unusable = -1, Unknown = 0;
        const int LegFull = int.MaxValue;

        public static void PlanRoutes(World w, Player p)
        {
            if (!p.IsComputer || p.IsOut) return;
            var s = p.Robot;
            var routes = w.RouteTable;
            var planes = p.Planes.ToList();
            var opt = w.AI.Options;
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var lastCity = new int[planes.Count];
            var planesOnLeg = new int[routes.Count];

            // 1. Wipe and survey. QUIRK Q24: the original deletes future *contract* entries here (its comment
            //    speaks of route plans); route entries persist through the weekly rollover.
            int wipeKind = opt.FixRouteWipe ? FlightKind.Route : FlightKind.Contract;
            for (int k = 0; k < planes.Count; k++)
            {
                var f = planes[k].Plan.Entries;
                for (int d = FlightPlan.Size - 1; d >= 0; d--)
                {
                    if (f[d].Kind == wipeKind && f[d].StartDate > date) f[d].Kind = FlightKind.None;
                    if (f[d].Kind == FlightKind.Route)
                    {
                        int ri = w.RouteIndexOf(f[d].ObjectId);
                        if (ri >= 0) planesOnLeg[ri]++;
                        lastCity[k] = f[d].To; // ends with the lowest-index route entry
                    }
                    // Q16: the original's second test repeats the route condition, so order entries never set the
                    // last city; FixRouteSurveyOrders lets them count (with the entry's own destination).
                    else if (opt.FixRouteSurveyOrders && f[d].Kind == FlightKind.Contract)
                        lastCity[k] = f[d].To;
                }
            }

            // 2. Rescue endangered legs: up to 4 planes get one flight on each leg with ≥ 15 low days.
            for (int c = 0; c < routes.Count; c++)
            {
                var rr = p.RentRoutes[c];
                if (!rr.Licensed || rr.DaysLow < 15) continue;
                int found = 0;
                for (int k = 0; k < planes.Count && found < 4; k++)
                {
                    var plane = planes[k];
                    if (!RobotShortcuts.HasFreeEntries(plane, 1)) continue;
                    // Q17: the original applies the midnight fix-up to the entry with the plane's index, so the new
                    // entry can keep an hour ≥ 24. FixRescueHourWrap wraps the new flight's start instead.
                    int sd = date, sh = hour + 2;
                    if (opt.FixRescueHourWrap && sh >= 24) { sh -= 24; sd++; }
                    bool kept = RobotShortcuts.AppendFlight(w, p, plane, FlightKind.Route, routes[c].Id, sd, sh, HoursBefore, planGates: false);
                    if (!opt.FixRescueHourWrap && k < FlightPlan.Size)
                    {
                        ref var slip = ref plane.Plan.Entries[k];
                        while (slip.StartHour >= 24) { slip.StartHour -= 24; slip.StartDate++; }
                    }
                    lastCity[k] = kept ? routes[c].To : Unusable;
                    if (kept) found++;
                }
            }

            GatePlanner.PlanGates(w, p);

            // 4. Range check: the longest owned leg, unless some plane covers it.
            s.NeedsBiggerPlane = 0;
            for (int c = 0; c < routes.Count; c++)
                if (p.RentRoutes[c].Licensed) s.NeedsBiggerPlane = Math.Max(s.NeedsBiggerPlane, w.Distance(routes[c].From, routes[c].To) / 1000);
            foreach (var plane in planes) if (plane.Range >= s.NeedsBiggerPlane) s.NeedsBiggerPlane = 0;

            // 5. Fill loop.
            int guard = 10000;
            bool added;
            do
            {
                added = false;
                int best = -1;
                for (int c = 0; c < routes.Count; c++)
                {
                    if (!p.RentRoutes[c].Licensed || planesOnLeg[c] == LegFull) continue;
                    if (best == -1) { best = c; continue; }
                    // QUIRK Q18: both sides are divided by the candidate's count, which never changes.
                    long mine = routes[c].Demand / (planesOnLeg[c] + 1);
                    long theirs = opt.FixDemandComparison ? routes[best].Demand / (planesOnLeg[best] + 1) : routes[best].Demand / (planesOnLeg[c] + 1);
                    if (mine > theirs) best = c;
                }
                if (best == -1) break;

                var leg = routes[best];
                int legDist = w.Distance(leg.From, leg.To);
                int pick = -1, pickDist = 0;
                for (int k = 0; k < planes.Count; k++)
                {
                    if (lastCity[k] == Unusable || legDist > planes[k].Range * 1000) continue;
                    if (lastCity[k] == Unknown) { pick = k; pickDist = 0; }
                    else
                    {
                        int dd = w.Distance(lastCity[k], leg.From);
                        if (pick == -1 || dd < pickDist) { pick = k; pickDist = dd; }
                    }
                }
                // No plane left for the top leg, or its plane has no free entry (the leg is then marked
                // full): nothing was added, so the loop ends for tonight, as in the original. With the route
                // wipe fixed (Q24) the plans are rebuilt every night, so a top leg no plane can reach (out of
                // range) would leave the robot with no route flights at all: it is skipped instead.
                if (pick == -1)
                {
                    if (opt.FixRouteWipe) { planesOnLeg[best] = LegFull; added = true; }
                    continue;
                }
                var pl = planes[pick];
                if (!RobotShortcuts.HasFreeEntries(pl, 1)) { planesOnLeg[best] = LegFull; continue; }
                bool kept2 = RobotShortcuts.AppendFlight(w, p, pl, FlightKind.Route, leg.Id, date, hour + 2, HoursBefore, planGates: false);
                lastCity[pick] = kept2 ? leg.To : Unusable;
                if (opt.FixDemandComparison && kept2) planesOnLeg[best]++;
                added = true;
                foreach (var e in pl.Plan.Entries)
                    if (e.Kind != FlightKind.None && e.LandDate >= date + FillDays) { lastCity[pick] = Unusable; break; }
            }
            while (added && --guard > 0);

            GatePlanner.PlanGates(w, p);

            // 7. Ferry flights on an owned leg become route flights (this is how the way back gets flown).
            var paxRule = w.Rules.Get<IRoutePassengerRule>();
            foreach (var plane in planes)
            {
                var f = plane.Plan.Entries;
                for (int e = 0; e < FlightPlan.Size; e++)
                {
                    if (f[e].Kind != FlightKind.Auto) continue;
                    for (int c = 0; c < routes.Count; c++)
                    {
                        if (!p.RentRoutes[c].Licensed || routes[c].From != f[e].From || routes[c].To != f[e].To) continue;
                        f[e].Kind = FlightKind.Route;
                        f[e].ObjectId = routes[c].Id;
                        f[e].Okay = Fleet.EntryOkay.Ok;
                        f[e].HoursBefore = HoursBefore;
                        f[e].TicketPrice = p.RentRoutes[c].TicketPrice;
                        f[e].TicketPriceFC = p.RentRoutes[c].TicketPriceFC;
                        paxRule.Calculate(w, p, plane, ref f[e]);
                        f[e].PArrived = 0;
                    }
                }
            }

            // 8. Ticket prices.
            PriceLegs(w, p);
        }

        /// <summary>Ticket prices and loss tracking of every owned leg (§6.2 step 8).</summary>
        public static void PriceLegs(World w, Player p)
        {
            var routes = w.RouteTable;
            var opt = w.AI.Options;
            var f = w.Rules.Get<IRobotFeatures>();
            for (int c = 0; c < routes.Count; c++)
            {
                var rr = p.RentRoutes[c];
                if (!rr.Licensed) continue;
                var leg = routes[c];

                int owners = 0;
                foreach (var q in w.Players)
                {
                    if (q.IsOut || !q.RentRoutes[c].Licensed) continue;
                    int flown = opt.FixCompetitorCount ? q.RentRoutes[c].LastFlown : rr.LastFlown; // QUIRK Q19
                    if (flown < 5) owners++;
                }

                int cost = w.FuelCost.Estimate(w, null, leg.From, leg.To, 800, 800) * 3 / 180;
                rr.TicketPrice = cost * 2 * 3 / 10 * 10;
                rr.TicketPriceFC = rr.TicketPrice * 2;
                if (owners <= 1) continue;

                foreach (var q in w.Players)
                {
                    if (q.IsOut || !q.RentRoutes[c].Licensed) continue;
                    if ((opt.FixCompetitorCount ? q.RentRoutes[c].LastFlown : rr.LastFlown) >= 5) continue;
                    var qr = q.RentRoutes[c];
                    rr.TicketPrice = Math.Min(rr.TicketPrice, qr.TicketPrice * 2 / 10 * 10);
                    rr.TicketPriceFC = Math.Min(rr.TicketPriceFC, qr.TicketPriceFC * 2 / 10 * 10);
                }
                foreach (var q in w.Players)
                {
                    if (q.IsOut || !q.RentRoutes[c].Licensed || p.Sympathy[q.Index] >= -40) continue;
                    if ((opt.FixCompetitorCount ? q.RentRoutes[c].LastFlown : rr.LastFlown) >= 5) continue;
                    var qr = q.RentRoutes[c];
                    rr.TicketPrice = Math.Min(rr.TicketPrice, qr.TicketPrice / 10 * 10 - 10);
                    rr.TicketPriceFC = Math.Min(rr.TicketPriceFC, qr.TicketPriceFC / 10 * 10 - 10);
                }
                rr.TicketPrice = Math.Max(rr.TicketPrice, cost / 2 / 10 * 10);
                rr.TicketPriceFC = Math.Max(rr.TicketPriceFC, cost / 2 / 10 * 10 * 2);
                if (p.Money < 100000)
                {
                    rr.TicketPrice = Math.Max(rr.TicketPrice, cost / 10 * 10);
                    rr.TicketPriceFC = Math.Max(rr.TicketPriceFC, cost / 10 * 10 * 2);
                }
                if (p.Money < 0)
                {
                    rr.TicketPrice = Math.Max(rr.TicketPrice, cost * 2 / 10 * 10);
                    rr.TicketPriceFC = Math.Max(rr.TicketPriceFC, cost * 2 / 10 * 10 * 2);
                }

                // Loss tracking. QUIRK Q3: the test always says "losing".
                bool losing = opt.FixRouteLossTest ? rr.TicketPrice < cost / 10 * 10 : true;
                if (losing) rr.DaysWithLoss++; else rr.DaysWithLoss--;
                if (rr.DaysWithLoss > 10 && !f.Has(w, RobotFeature.NeverGiveRouteUp))
                {
                    foreach (var q in w.Players)
                        if (!q.IsOut && q.RentRoutes[c].Rank > rr.Rank) q.RentRoutes[c].Rank--;
                    rr.Rank = 0;
                    rr.DaysLow = 99;
                    w.Events.Publish(new RouteLicenceChanged { Player = p, RouteIndex = c, Change = "robot-gave-up" });
                }
            }
        }
    }
}
