using System;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Passenger count of a flight entry ("CalcPassengers", planes-and-flights.md §12.3,
    /// contracts-and-routes.md §3.7). Route flights recompute only while the start is more than one
    /// hour away (frozen from S−2:00); contract flights take min(seats, persons) economy, rest first class.
    /// </summary>
    public sealed class OriginalRoutePassengers : IRoutePassengerRule
    {
        public void Calculate(World w, Player p, Plane plane, ref FlightEntry e)
        {
            if (e.Kind == FlightKind.Route)
            {
                var route = w.RouteById(e.ObjectId);
                if (route == null) return;
                e.From = route.From;
                e.To = route.To;
                int dur = w.FlightHours(e.From, e.To, plane.Speed);
                e.LandHour = (e.StartHour + dur) % 24;
                e.LandDate = e.StartDate + (e.LandHour < e.StartHour ? 1 : 0);

                int date = w.Clock.Date, hour = w.Clock.Hour;
                if (!(e.StartDate > date || (e.StartDate == date && hour + 1 < e.StartHour))) return;

                int ri = route.Index;
                int basePrice = w.ReferencePrice(route.From, route.To);
                var sab = w.Rules.Get<IFlightSabotageState>();
                bool press = sab.PressStatement(w, p);

                // ---- economy
                {
                    int t = e.TicketPrice;
                    Weights(w, p, ri, t, basePrice, 3, 5, 6, 150, out long mine, out long total);
                    long x = total > 0 ? route.Demand * mine / total : 0;
                    x = Math.Min(x, plane.MaxPassengers + plane.MaxPassengers / 2);
                    if (e.HoursBefore > 48) e.HoursBefore = 48;
                    if (e.HoursBefore < 48) x = x * (e.HoursBefore + 48) / 96;
                    int cost = basePrice * 3;
                    if (t > cost && cost > 10 && t > 0) x = x * (cost - 10) / t;
                    x = Night(x, e.StartHour, e.LandHour);
                    x = ImageAndNoise(p, ri, plane.Noise, x, 60, 80, 100, 110);
                    if (total > 0) x = Math.Min(x, route.Demand * mine / total);
                    x = Math.Min(x, plane.MaxPassengers);
                    e.Passengers = (int)(ushort)x;
                    if (press) e.Passengers = 1 + e.Passengers % 3;
                }

                // ---- first class (the original uses the economy price here; FixFirstClassDemand uses the FC price)
                {
                    int t = w.Options.FixFirstClassDemand ? e.TicketPriceFC : e.TicketPrice;
                    Weights(w, p, ri, t, basePrice, 9, 15, 18, 170, out long mine, out long total, true);
                    long x = total > 0 ? route.Demand * mine / total : 0;
                    x = Math.Min(x, plane.MaxPassengersFC + plane.MaxPassengersFC / 2);
                    if (e.HoursBefore > 72) e.HoursBefore = 72;
                    if (e.HoursBefore < 72) x = x * (e.HoursBefore + 48) / 120;
                    int cost = basePrice * 3;
                    if (t > cost * 3 && cost > 10 && t > 0) x = x * (cost - 10) / t;
                    x = Night(x, e.StartHour, e.LandHour);
                    int flags = sab.SecurityFlags(w, p);
                    int lux = plane.Equipment[Equip.Seats] + plane.Equipment[Equip.Food] + plane.Equipment[Equip.Trays] + plane.Equipment[Equip.Deco]
                            + plane.Equipment[Equip.Engines] + plane.Equipment[Equip.Tyres] + plane.Equipment[Equip.Electronics] + plane.Equipment[Equip.Safety]
                            + ((flags & (1 << 10)) != 0 ? 1 : 0) + ((flags & (1 << 11)) != 0 ? 1 : 0);
                    if (lux < 6) x = 0;
                    else if (lux <= 18) x = x * (lux - 6) / 8;
                    x = ImageAndNoise(p, ri, plane.Noise, x, 40, 60, 80, 100);
                    if (total > 0) x = Math.Min(x, route.Demand * mine / total);
                    x = Math.Min(x, plane.MaxPassengersFC);
                    e.PassengersFC = (int)(ushort)x;
                    if (press) e.PassengersFC = 1 + e.PassengersFC % 3;
                }
            }
            else if (e.Kind == FlightKind.Contract)
            {
                var c = p.Contracts.Find(e.ObjectId);
                if (c == null) return;
                e.Passengers = (int)(ushort)Math.Min(plane.MaxPassengers, c.Persons);
                e.PassengersFC = (int)(ushort)(c.Persons - e.Passengers);
            }
        }

        /// <summary>
        /// Share weights of the four airlines (§12.3 step 1). QUIRK: every licensee's weight uses the
        /// planning flight's price <paramref name="t"/>, so price does not shift market share.
        /// </summary>
        static void Weights(World w, Player me, int routeIndex, int t, int basePrice, int th1, int th2, int th3, int rank1,
                            out long mine, out long total, bool fc = false)
        {
            mine = 0; total = 0;
            int own = t;
            foreach (var q in w.Players)
            {
                t = own;
                var rr = q.RentRoutes[routeIndex];
                long g;
                if (rr.Rank != 0)
                {
                    if (w.Options.FairRouteCompetition && q != me) t = fc ? rr.TicketPriceFC : rr.TicketPrice;
                    g = t != 0 ? 10000 / t : 100000;
                    if (t > basePrice * th1) g = g * 9 / 10;
                    if (t > basePrice * th2) g = g * 9 / 10;
                    if (t > basePrice * th3) g = g * 8 / 10;
                    if (g == 0) g = 1;
                    if (rr.Rank == 1) g = g * rank1 / 100;
                    if (rr.Rank == 2) g = g * 120 / 100;
                    if (rr.AvgFlown > 4)
                    {
                        g = g * 10 / (10 + Math.Min(rr.AvgFlown - 3, 10));
                        if (g < 1) g = 1;
                    }
                    if (q.IsComputer) g += g / 4;
                }
                else g = q == me ? 1 : 0;
                if (q == me) mine = g;
                total += g;
            }
        }

        static long Night(long x, int start, int land)
        {
            if (start < 5 || start > 22) x = x * 5 / 6;
            if (land < 5 || land > 22) x = x * 5 / 6;
            return x;
        }

        static long ImageAndNoise(Player p, int routeIndex, int noise, long x, int n1, int n2, int n3, int n4)
        {
            int image = p.RentRoutes[routeIndex].Image * 4 + p.Image + 200;
            if (image < 0) image = 0;
            if (image > 1000) image = 1000;
            x = (ushort)(x * (400 + image) / 1100);
            if (noise > n1) x = (ushort)(x * 1000 / 1100);
            if (noise > n2) x = (ushort)(x * 1000 / 1100);
            if (noise > n3) x = (ushort)(x * 1000 / 1100);
            if (noise > n4) x = (ushort)(x * 1000 / 1100);
            return x;
        }
    }
}
