using System;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Airport
{
    /// <summary>
    /// Gate planning ("PlanGates", planes-and-flights.md §10.8, contracts-and-routes.md §7) and gate
    /// renumbering ("ReformGates", sim-core.md §2.6 step 25).
    /// </summary>
    public static class GatePlanner
    {
        /// <summary>
        /// Re-plans the gates of an airline over a 7-day × 24-hour occupancy table: a home departure
        /// needs a free gate in its departure hour and the hour before (occupying both), an arrival
        /// occupies the hour before its landing hour; slots outside 2..166 are not considered; future
        /// flights get the k-th rented gate's number or −1; full hours warn the overlapping flights of
        /// human airlines; flights not touching home get −2.
        /// </summary>
        public static void PlanGates(World w, Player p)
        {
            var g = p.Gates;
            int date = w.Clock.Date, hour = w.Clock.Hour, home = w.HomeCity;
            for (int i = 0; i < PlayerGates.Hours; i++) g.Occupancy[i] = 0;
            foreach (var plane in p.Planes.Items)
                for (int d = FlightPlan.Size - 1; d >= 0; d--) plane.Plan.Entries[d].GateWarning = false;

            foreach (var plane in p.Planes.Items)
            {
                var f = plane.Plan.Entries;
                for (int d = FlightPlan.Size - 1; d >= 0; d--)
                {
                    ref var e = ref f[d];
                    if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract) continue;
                    if (e.From != home && e.To != home) { e.Gate = GateCode.NotHome; continue; }

                    bool future = e.IsFuture(date, hour);
                    if (future) e.Gate = GateCode.None;
                    bool departure = e.From == home;
                    int tmp = departure ? e.StartHour + (e.StartDate - date) * 24 : e.LandHour - 1 + (e.LandDate - date) * 24;
                    if (tmp <= 1 || tmp >= PlayerGates.Hours - 1) continue;

                    if (departure)
                    {
                        if (g.Occupancy[tmp] < g.NumRented && g.Occupancy[tmp - 1] < g.NumRented)
                        {
                            int k = Math.Max(g.Occupancy[tmp], g.Occupancy[tmp - 1]);
                            if (future) e.Gate = g.Gates[k].Number;
                            g.Occupancy[tmp - 1] = g.Occupancy[tmp] = (k + 1) & 0xFF;
                        }
                        else
                        {
                            if (future) e.Gate = GateCode.None;
                            g.Occupancy[tmp] = g.Occupancy[tmp - 1] = (g.NumRented + 1) & 0xFF;
                            if (p.Owner == Owner.Human) WarnDeparture(w, p, tmp);
                        }
                    }
                    else
                    {
                        if (g.Occupancy[tmp] < g.NumRented)
                        {
                            if (future) e.Gate = g.Gates[g.Occupancy[tmp]].Number;
                            g.Occupancy[tmp]++;
                        }
                        else
                        {
                            if (future) e.Gate = GateCode.None;
                            g.Occupancy[tmp] = (g.NumRented + 1) & 0xFF;
                            if (p.Owner == Owner.Human) WarnArrival(w, p, tmp);
                        }
                    }
                }
            }
        }

        static void WarnDeparture(World w, Player p, int tmp)
        {
            int date = w.Clock.Date, home = w.HomeCity;
            foreach (var plane in p.Planes.Items)
            {
                var f = plane.Plan.Entries;
                for (int i = FlightPlan.Size - 1; i >= 0; i--)
                {
                    ref var e = ref f[i];
                    if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract) continue;
                    if (e.From == home && Math.Abs(e.StartHour + (e.StartDate - date) * 24 - tmp) < 2) e.GateWarning = true;
                    else if (e.To == home)
                    {
                        int l = e.LandHour + (e.LandDate - date) * 24;
                        if (l == tmp || l == tmp + 1) e.GateWarning = true;
                    }
                }
            }
        }

        static void WarnArrival(World w, Player p, int tmp)
        {
            int date = w.Clock.Date, home = w.HomeCity;
            foreach (var plane in p.Planes.Items)
            {
                var f = plane.Plan.Entries;
                for (int i = FlightPlan.Size - 1; i >= 0; i--)
                {
                    ref var e = ref f[i];
                    if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract) continue;
                    int s = e.StartHour + (e.StartDate - date) * 24;
                    if (e.From == home && (s == tmp || s == tmp + 1)) e.GateWarning = true;
                    else if (e.To == home && e.LandHour - 1 + (e.LandDate - date) * 24 == tmp) e.GateWarning = true;
                }
            }
        }

        /// <summary>Renumbers all rented gates of all active airlines 0, 1, 2, … in airline order ("ReformGates").</summary>
        public static void Renumber(World w)
        {
            int n = 0;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                for (int d = 0; d < p.Gates.NumRented && d < PlayerGates.Records; d++)
                    if (p.Gates.Gates[d].Used) { p.Gates.Gates[d].Number = n; n++; }
            }
        }
    }
}
