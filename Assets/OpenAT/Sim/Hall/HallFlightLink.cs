using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Optional companion of <see cref="IHallFlightLink"/>: the long version of a flight display text
    /// (full city names) used for the tooltip, where the display itself shows the city codes
    /// (airport-and-people.md §9.3 "A full-name version is kept for the tooltip").
    /// </summary>
    public interface IHallFlightTooltips
    {
        /// <summary>Tooltip of flight display <paramref name="index"/> (0..29 check-in, 30..59 gates), "" when blank.</summary>
        string FlightDisplayTooltip(int index);
    }

    /// <summary>
    /// The hall's view of the operations model (planes, flight plans, gates): departing passengers for
    /// real flights (airport-and-people.md §6.6, sim-core.md §2.3 step 8), gate owners (§9.3
    /// "GateMapper"), the check-in and gate flight displays (§9.3, sim-core.md §2.4 hour step 4) and the
    /// plane-at-gate test used by boarding (§7.1, backed by <see cref="HallState.PlaneAtGate"/>).
    /// Created for every hall by <see cref="HallFlights.Factory"/>.
    /// </summary>
    public sealed class HallFlightLink : IHallFlightLink, IHallFlightTooltips
    {
        /// <summary>Displays per half (check-in 0..29, gates 30..59), fixed by the hall's room ids.</summary>
        public const int DisplaysPerHalf = 30;
        /// <summary>Passengers one departing figure stands for, and after 100 spawned (CUSTOMERS_PER_PERSON / ..B).</summary>
        public const int PassengersPerPerson = 15, PassengersPerPersonLater = 45;

        readonly World _w;
        readonly string[] _short = new string[2 * DisplaysPerHalf];
        readonly string[] _long = new string[2 * DisplaysPerHalf];
        int _stampDate = int.MinValue, _stampHour = -1;

        public HallFlightLink(World w)
        {
            _w = w;
            for (int i = 0; i < _short.Length; i++) _short[i] = _long[i] = "";
        }

        Player PlayerAt(int airline) => airline >= 0 && airline < Player.Count ? _w.Players[airline] : null;

        // ------------------------------------------------------------------ flights of persons

        public bool TryGetFlight(int airline, int planeId, int flightIndex, out int gate, out int startHour)
        {
            gate = GateCode.None; startHour = 0;
            var plane = PlayerAt(airline)?.Planes.Find(planeId);
            if (plane == null || flightIndex < 0 || flightIndex >= FlightPlan.Size) return false;
            ref var e = ref plane.Plan.Entries[flightIndex];
            if (e.Kind == FlightKind.None) return false;
            gate = e.Gate;
            startHour = e.StartHour;
            return true;
        }

        public bool PlaneAtGate(int airline, int planeId) => _w.Hall != null && _w.Hall.PlaneAtGate(airline, planeId);

        public int SecurityFlags(int airline)
        {
            var p = PlayerAt(airline);
            return p == null ? 0 : _w.Rules.Get<IFlightSabotageState>().SecurityFlags(_w, p);
        }

        /// <summary>
        /// Departing passengers (§6.6 "Departing passengers"): for each plane whose next start is a route or
        /// contract flight from home with a gate, starting next hour: two tries per call, each succeeding
        /// with chance 1/6 (always from minute 20), add one figure at a random CREATION rune until the
        /// booked passengers have arrived. A figure counts 15 passengers (45 once 100 have arrived);
        /// first-class passengers come first.
        /// </summary>
        public void AddNewPassengers(World w, HallState hall)
        {
            var rand = w.PersonRandCreate;
            int date = w.Clock.Date, hour = w.Clock.Hour, minute = w.Clock.Minute;
            for (int c = 0; c < Player.Count; c++)
            {
                var p = w.Players[c];
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.Items)
                {
                    int e = plane.Plan.NextStart;
                    if (e < 0) continue;
                    ref var f = ref plane.Plan.Entries[e];
                    if (f.Kind != FlightKind.Route && f.Kind != FlightKind.Contract) continue;
                    int booked = f.Passengers + f.PassengersFC;
                    if (f.StartHour - 1 != hour || f.StartDate != date || f.PArrived >= booked || f.Gate == GateCode.None || f.From != w.HomeCity) continue;
                    for (int n = 0; n < 2; n++)
                    {
                        if (rand.Rand(6) != 0 && minute < 20) continue;
                        bool firstClass = f.PArrived < f.PassengersFC;
                        // Tutorial step 1600: the first passenger comes from the far left (Sim/Tutorial).
                        if (!OpenAT.Sim.Tutorial.TutorialEngine.SpawnFirstPassenger(w, hall, rand, c, plane.Id, e))
                        {
                            // Argument order of the original call (evaluated right to left): birthplace,
                            // luggage choice, then the clan draw, all from the creation stream.
                            var birth = hall.RandomRune(RuneId.Creation, 0, rand);
                            int koffer = rand.Rand(2);
                            int clan = hall.CustomerId(0, koffer, rand);
                            if (birth != null && clan >= 0)
                                hall.AddPersonToQueue(clan, birth.Value, Reason.Flying, c, plane.Id, e,
                                    firstClass ? Mood.TicketFC : Mood.Ticket, firstClass);
                        }
                        f.PArrived += f.PArrived < 100 ? PassengersPerPerson : PassengersPerPersonLater;
                        if (f.PArrived > booked) f.PArrived = booked;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ gates and displays

        /// <summary>Owner of a gate number: the last airline (in index order) with a rented record of it.</summary>
        public int GateOwner(int gate)
        {
            int owner = -1;
            for (int c = 0; c < Player.Count; c++)
            {
                var p = _w.Players[c];
                if (p.IsOut) continue;
                var g = p.Gates.Gates;
                for (int d = 0; d < g.Length; d++)
                    if (g[d].Used && g[d].Number == gate) owner = c;
            }
            return owner;
        }

        public string FlightDisplayText(int index)
        {
            EnsureFresh();
            return (uint)index < (uint)_short.Length ? _short[index] : "";
        }

        public string FlightDisplayTooltip(int index)
        {
            EnsureFresh();
            return (uint)index < (uint)_long.Length ? _long[index] : "";
        }

        void EnsureFresh()
        {
            if (_stampDate != _w.Clock.Date || _stampHour != _w.Clock.Hour) Repaint();
        }

        /// <summary>Forces a repaint at the next query (plan or gate changes outside the hourly refresh).</summary>
        public void Invalidate() => _stampDate = int.MinValue;

        /// <summary>
        /// Refreshes the display texts ("RepaintTextBricks", §9.3): for each plane's next start (route or
        /// contract with a gate ≥ 0, from home, today, this or next hour) its gate's check-in and gate
        /// displays show "FROM-TO hh:00" with hh = start + 1; for each plane's current flight landing at
        /// home this hour the gate display shows the landing hour. The smallest hour wins per display.
        /// Done at every hour change (<see cref="HallFlightDisplaySystem"/>) and on first use.
        /// </summary>
        public void Repaint()
        {
            var w = _w;
            _stampDate = w.Clock.Date;
            _stampHour = w.Clock.Hour;
            int date = w.Clock.Date, hour = w.Clock.Hour, home = w.HomeCity;
            var printed = new int[_short.Length];
            for (int i = 0; i < _short.Length; i++) { _short[i] = _long[i] = ""; printed[i] = 999; }

            for (int c = 0; c < Player.Count; c++)
            {
                foreach (var plane in w.Players[c].Planes.Items)
                {
                    int e = plane.Plan.NextStart;
                    if (e >= 0)
                    {
                        ref var f = ref plane.Plan.Entries[e];
                        if ((f.Kind == FlightKind.Route || f.Kind == FlightKind.Contract) && f.Gate >= 0 && f.Gate < DisplaysPerHalf
                            && f.StartDate == date && (f.StartHour - 1 == hour || f.StartHour == hour) && f.From == home)
                        {
                            Print(printed, f.Gate, f.From, f.To, f.StartHour + 1);
                            Print(printed, DisplaysPerHalf + f.Gate, f.From, f.To, f.StartHour + 1);
                        }
                    }
                    e = plane.Plan.NextFlight;
                    if (e >= 0)
                    {
                        ref var f = ref plane.Plan.Entries[e];
                        if ((f.Kind == FlightKind.Route || f.Kind == FlightKind.Contract) && f.Gate >= 0 && f.Gate < DisplaysPerHalf
                            && f.LandDate == date && f.LandHour == hour && f.To == home)
                            Print(printed, DisplaysPerHalf + f.Gate, f.From, f.To, f.LandHour);
                    }
                }
            }
        }

        void Print(int[] printed, int slot, int from, int to, int hh)
        {
            if (hh >= printed[slot]) return;
            var a = _w.Data.Cities.Find(from);
            var b = _w.Data.Cities.Find(to);
            if (a == null || b == null) return;
            _short[slot] = Code(a) + "-" + Code(b) + " " + hh + ":00";
            _long[slot] = a.Name + "-" + b.Name + " " + hh + ":00";
            printed[slot] = hh;
        }

        static string Code(Data.City c) => string.IsNullOrEmpty(c.ShortCode) ? c.Name : c.ShortCode;
    }

    /// <summary>Gate count from the hall layout: the number of RUNE_2WAIT runes (contracts-and-routes.md §6.1); 4 without a hall.</summary>
    public sealed class HallGateCount : IAirportGateCount
    {
        public int Fallback = 4;
        public int GateCount(World w) => w.Hall != null ? w.Hall.Layout.GateCount : Fallback;
    }

    /// <summary>Hall ↔ operations wiring: the flight link factory, the gate-count rule and the systems.</summary>
    public static class HallFlights
    {
        /// <summary>Creates the flight link of a new hall; replace to plug in another flight model.</summary>
        public static Func<World, IHallFlightLink> Factory = w => new HallFlightLink(w);

        /// <summary>Replaces the operations default (<see cref="FixedGateCount"/>); a rule set by an extension stays.</summary>
        internal static void RegisterRules(World w)
        {
            if (!w.Rules.Has<IAirportGateCount>() || w.Rules.Get<IAirportGateCount>() is FixedGateCount)
                w.Rules.Set<IAirportGateCount>(new HallGateCount());
        }

        internal static void AddSystems(World w)
        {
            if (w.Systems.Find(HallPlaneSystem.SystemName) == null) w.Systems.Add(new HallPlaneSystem());
            if (w.Systems.Find(HallFlightDisplaySystem.SystemName) == null) w.Systems.Add(new HallFlightDisplaySystem());
        }
    }

    /// <summary>Hour step 4 (sim-core.md §2.4): repaint the flight displays. Order 400 = after the delays (200).</summary>
    public sealed class HallFlightDisplaySystem : SimSystem
    {
        public const string SystemName = "hall.flight-displays";
        public override string Name => SystemName;
        public override int Order => 400;
        public override void OnHour(World w)
        {
            if (w.Hall?.Flights is HallFlightLink l) l.Repaint();
        }
    }
}
