using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Flight-plan algorithms (planes-and-flights.md §10): validation with automatic ferry flights
    /// ("CheckFlugplaene"), the extend button ("ExtendFlugplaene"), the nightly weekly rollover
    /// ("FlugplaeneFortfuehren") and the sell check ("CanBeSold").
    /// </summary>
    public static class FlightPlanner
    {
        public static int Duration(World w, Plane plane, int from, int to) => w.FlightHours(from, to, plane.Speed);

        /// <summary>A ferry inserted at "now + 2" late in the evening would keep a start hour of 24 or 25 in the
        /// original; with <see cref="GameOptions.FixFlightHourWrap"/> it moves to the next day.</summary>
        static void WrapStart(ref FlightEntry e)
        {
            while (e.StartHour >= 24) { e.StartHour -= 24; e.StartDate++; }
        }

        static void SetLanding(World w, Plane plane, ref FlightEntry e)
        {
            int dur = Duration(w, plane, e.From, e.To);
            e.LandHour = (e.StartHour + dur) % 24;
            e.LandDate = e.StartDate + (e.StartHour + dur) / 24;
        }

        /// <summary>
        /// The original's in-place sort: empty entries to the end, and (when <paramref name="byTime"/>)
        /// non-empty entries by (start date, start hour). Stable.
        /// </summary>
        public static void Sort(FlightEntry[] f, bool byTime)
        {
            for (int i = 1; i < f.Length; i++)
            {
                var x = f[i];
                int j = i - 1;
                while (j >= 0 && Before(ref x, ref f[j], byTime)) { f[j + 1] = f[j]; j--; }
                f[j + 1] = x;
            }
        }

        static bool Before(ref FlightEntry a, ref FlightEntry b, bool byTime)
        {
            if (b.Kind == 0) return a.Kind != 0;
            if (a.Kind == 0) return false;
            if (!byTime) return false;
            return a.StartDate < b.StartDate || (a.StartDate == b.StartDate && a.StartHour < b.StartHour);
        }

        /// <summary>
        /// Plan validation "CheckFlugplaene" (planes-and-flights.md §10.2), run after every edit, delay and
        /// rollover: drop future automatic flights, refresh future entries from their objects, insert
        /// ferry flights from the start city and between non-matching legs, push flights forward,
        /// cut the 7-day horizon (unless the human is dragging in the planner), move ferry flights as late
        /// as possible, then re-plan gates.
        /// </summary>
        public static void Check(World w, Player p, Plane plane, bool sort = true, bool planGates = true)
        {
            var plan = plane.Plan;
            var f = plan.Entries;
            int date = w.Clock.Date, hour = w.Clock.Hour;
            int n = FlightPlan.Size;

            Sort(f, sort);

            for (int c = n - 1; c >= 0; c--)
            {
                ref var e = ref f[c];
                if (!e.IsFuture(date, hour)) continue;
                if (e.Kind == FlightKind.Auto) { e.Kind = FlightKind.None; continue; }
                if (e.Kind == FlightKind.None) continue;
                if (!RefreshCities(w, p, ref e)) { e.Kind = FlightKind.None; continue; }
                int dur = Duration(w, plane, e.From, e.To);
                e.LandHour = (e.StartHour + dur) % 24;
                e.LandDate = e.StartDate + (e.LandHour < e.StartHour ? 1 : 0);
                if (dur >= 24)
                {
                    e.Kind = FlightKind.None;
                    w.Advise(p, 2310);
                }
            }

            Sort(f, sort);

            if (f[0].Kind != 0 && f[0].From != plan.StartCity)
            {
                for (int d = n - 1; d > 0; d--) f[d] = f[d - 1];
                f[0].Kind = FlightKind.Auto;
                f[0].From = plan.StartCity;
                f[0].To = f[1].From;
                f[0].StartDate = date;
                f[0].StartHour = hour + 2;
                if (w.Options.FixFlightHourWrap) WrapStart(ref f[0]);
                SetLanding(w, plane, ref f[0]);
            }

            for (int c = 0; c < n - 1; c++)
            {
                if (f[c + 1].Kind == 0) break;
                int nextFrom = f[c + 1].From;
                if (nextFrom == f[c].To) continue;
                for (int d = n - 1; d > c + 1; d--) f[d] = f[d - 1];
                ref var a = ref f[c + 1];
                a.Kind = FlightKind.Auto;
                a.From = f[c].To;
                a.To = nextFrom;
                a.StartHour = f[c].LandHour;
                a.StartDate = f[c].LandDate;
                if (a.StartDate < date) a.StartDate = date;
                if (a.StartDate == date && a.StartHour <= hour + 1) a.StartHour = hour + 2;
                if (w.Options.FixFlightHourWrap) WrapStart(ref a);
                SetLanding(w, plane, ref a);
            }

            for (int c = 0; c < n - 1; c++)
            {
                if (f[c + 1].Kind == 0) break;
                int tTime = (f[c].LandHour + 1) % 24;
                int tDate = f[c].LandDate + (f[c].LandHour + 1) / 24;
                ref var nx = ref f[c + 1];
                if (nx.StartDate < tDate)
                {
                    nx.StartDate = tDate;
                    nx.StartHour = tTime;
                    SetLanding(w, plane, ref nx);
                }
                else if (nx.StartDate == tDate && nx.StartHour < tTime)
                {
                    nx.StartHour = tTime;
                    SetLanding(w, plane, ref nx);
                }
            }

            if (!(p.Owner == Owner.Human && p.PlannerDragging))
                for (int c = 0; c < n; c++)
                {
                    if (f[c].Kind == 0) break;
                    if (f[c].StartDate >= date + 7)
                    {
                        f[c].Kind = FlightKind.None;
                        if (c > 0 && f[c - 1].Kind == FlightKind.Auto) f[c - 1].Kind = FlightKind.None;
                    }
                }

            for (int c = 0; c < n - 1; c++)
            {
                if (f[c + 1].Kind == 0) break;
                ref var a = ref f[c];
                if (a.Kind != FlightKind.Auto || !a.IsFuture(date, hour)) continue;
                int tTime = (f[c + 1].StartHour - 1 + 24) % 24;
                int tDate = f[c + 1].StartDate - (f[c + 1].StartHour == 0 ? 1 : 0);
                if (a.LandDate < tDate || (a.LandDate == tDate && a.LandHour < tTime))
                {
                    a.LandDate = tDate;
                    a.LandHour = tTime;
                    int dur = Duration(w, plane, a.From, a.To);
                    a.StartHour = (a.LandHour - dur + 480) % 24;
                    a.StartDate = a.LandDate + (a.LandHour - dur - 23) / 24;
                }
            }

            if (planGates) GatePlanner.PlanGates(w, p);
        }

        /// <summary>Copies from/to from the entry's object; false when the object no longer exists.</summary>
        public static bool RefreshCities(World w, Player p, ref FlightEntry e)
        {
            switch (e.Kind)
            {
                case FlightKind.Route:
                    {
                        var r = w.RouteById(e.ObjectId);
                        if (r == null) return false;
                        e.From = r.From; e.To = r.To;
                        return true;
                    }
                case FlightKind.Contract:
                    {
                        var c = p.Contracts.Find(e.ObjectId);
                        if (c == null) return false;
                        e.From = c.From; e.To = c.To;
                        return true;
                    }
                case FlightKind.Freight:
                    {
                        var c = p.Freights.Find(e.ObjectId);
                        if (c == null) return false;
                        e.From = c.From; e.To = c.To;
                        return true;
                    }
            }
            return true;
        }

        /// <summary>
        /// "Extend plan" (planes-and-flights.md §10.4): repeats the block from the first route flight
        /// starting today or later to the end of the plan, shifted by its length R, while it still ends
        /// before day Date+7. Only routes still rented are copied; hour normalisation uses "&gt; 24" in the original
        /// (hour 24 survives; <see cref="GameOptions.FixFlightHourWrap"/> wraps it);
        /// copies are clamped to the 60 slots. The caller validates the plan without re-sorting.
        /// </summary>
        public static void Extend(World w, Player p, Plane plane)
        {
            var f = plane.Plan.Entries;
            int n = FlightPlan.Size, date = w.Clock.Date;
            int c;
            for (c = 0; c < n; c++) if (f[c].Kind == 0) break;
            int anzOkay = c, anzSource = c;
            for (c = 0; c < n; c++) if (f[c].Kind == FlightKind.Route && f[c].StartDate >= date) break;
            if (c >= n || anzOkay <= 0) return;
            int sourceStart = c;
            anzSource -= c;
            if (anzSource <= 0) return;
            ref var last = ref f[sourceStart + anzSource - 1];
            ref var first = ref f[sourceStart];
            int rapport = last.LandAbs - first.StartAbs + 1;
            if (last.To != first.From)
                rapport += Duration(w, plane, last.From, first.To) + 1;
            if (rapport <= 0) return;

            while (f[anzOkay - 1].LandDate * 24 + f[anzOkay - 1].LandHour + rapport < (date + 7) * 24)
            {
                if (anzOkay + anzSource > n) break;
                for (c = 0; c < anzSource; c++)
                {
                    var src = f[anzOkay + c - anzSource];
                    if (src.Kind != FlightKind.Route) continue;
                    int ri = w.RouteIndexOf(src.ObjectId);
                    if (ri < 0 || !p.RentRoutes[ri].Licensed) continue;
                    var e = src;
                    e.StartHour += rapport;
                    e.LandHour += rapport;
                    int wrap = w.Options.FixFlightHourWrap ? 23 : 24; // the original wraps only above 24
                    while (e.LandHour > wrap) { e.LandHour -= 24; e.LandDate++; }
                    while (e.StartHour > wrap) { e.StartHour -= 24; e.StartDate++; }
                    f[anzOkay + c] = e;
                }
                anzOkay += anzSource;
            }
        }

        /// <summary>
        /// Nightly rollover "FlugplaeneFortfuehren" (planes-and-flights.md §10.7): landed contracts,
        /// ferries and freight are deleted (start city follows); landed route flights on routes still
        /// rented move +7 days (lead time 48 h, economy price refreshed), packed after the latest landing;
        /// then the plan is validated and ferry flights equal to a route leg become that route.
        /// </summary>
        public static void Rollover(World w, Player p, Plane plane)
        {
            var plan = plane.Plan;
            var f = plan.Entries;
            int date = w.Clock.Date;
            int lastDate = 0, lastTime = 0;
            for (int c = 0; c < FlightPlan.Size; c++)
            {
                if (f[c].Kind == 0) continue;
                if (f[c].LandDate > lastDate || (f[c].LandDate == lastDate && f[c].LandHour > lastTime))
                {
                    lastDate = f[c].LandDate;
                    lastTime = f[c].LandHour + 1;
                    if (lastTime >= 24) { lastTime = 0; lastDate++; }
                }
            }

            for (int c = 0; c < FlightPlan.Size; c++)
            {
                ref var e = ref f[c];
                if (e.LandDate >= date) continue;
                if (e.Kind == FlightKind.Contract || e.Kind == FlightKind.Auto || e.Kind == FlightKind.Freight)
                {
                    plan.StartCity = e.To;
                    e.Kind = FlightKind.None;
                }
                if (e.Kind == FlightKind.Route)
                {
                    int ri = w.RouteIndexOf(e.ObjectId);
                    if (ri >= 0 && p.RentRoutes[ri].Licensed)
                    {
                        plan.StartCity = e.To;
                        e.StartDate += 7;
                        e.LandDate += 7;
                        e.PArrived = 0;
                        e.HoursBefore = 48;
                        e.TicketPrice = p.RentRoutes[ri].TicketPrice;
                        if (e.StartDate < lastDate || (e.StartDate == lastDate && e.StartHour < lastTime))
                        {
                            int delta = (lastDate * 24 + lastTime) - (e.StartDate * 24 + e.StartHour);
                            e.LandHour += delta;
                            e.StartHour += delta;
                            while (e.StartHour >= 24) { e.StartHour -= 24; e.StartDate++; }
                            while (e.LandHour >= 24) { e.LandHour -= 24; e.LandDate++; }
                            lastDate = e.LandDate;
                            lastTime = e.LandHour + 1;
                            while (lastTime >= 24) { lastTime -= 24; lastDate++; }
                        }
                    }
                    if (e.Kind == FlightKind.Route && e.LandDate < date) e.Kind = FlightKind.None;
                }
            }

            Check(w, p, plane);

            for (int c = 0; c < FlightPlan.Size; c++)
            {
                if (f[c].Kind != FlightKind.Auto) continue;
                for (int d = 0; d < FlightPlan.Size; d++)
                    if (f[d].Kind == FlightKind.Route && f[c].From == f[d].From && f[c].To == f[d].To)
                    {
                        f[c].Kind = f[d].Kind;
                        f[c].ObjectId = f[d].ObjectId;
                        f[c].TicketPrice = f[d].TicketPrice;
                    }
            }
        }

        /// <summary>
        /// "CanBeSold" (planes-and-flights.md §4.4): no route, contract or freight flight lands after
        /// now − 2 h. Automatic flights do not block.
        /// </summary>
        public static bool CanBeSold(World w, Plane plane)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var f = plane.Plan.Entries;
            for (int c = FlightPlan.Size - 1; c >= 0; c--)
            {
                if (f[c].Kind != FlightKind.Route && f[c].Kind != FlightKind.Contract && f[c].Kind != FlightKind.Freight) continue;
                if (f[c].LandDate > date || (f[c].LandDate == date && f[c].LandHour > hour - 2)) return false;
            }
            return true;
        }

        /// <summary>Largest passenger need of a not-yet-landed contract or freight flight (freight: tons × 10).</summary>
        public static int MaxPassengerOpenFlight(World w, Player p, Plane plane)
        {
            int rc = 0;
            foreach (var e in plane.Plan.Entries)
            {
                if (e.LandDate < w.Clock.Date) continue;
                if (e.Kind == FlightKind.Contract)
                {
                    var c = p.Contracts.Find(e.ObjectId);
                    if (c != null && c.Persons > rc) rc = c.Persons;
                }
                else if (e.Kind == FlightKind.Freight)
                {
                    var c = p.Freights.Find(e.ObjectId);
                    if (c != null && c.Tons * 10 > rc) rc = c.Tons * 10;
                }
            }
            return rc;
        }

        /// <summary>Runs the plan validation, usage flags and gate plan after an edit, like the planner does.</summary>
        public static void AfterEdit(World w, Player p, Plane plane, bool sort = true)
        {
            plane.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            Check(w, p, plane, sort);
            ContractUsage.UpdateContracts(w, p);
            ContractUsage.UpdateFreight(w, p);
        }
    }
}
