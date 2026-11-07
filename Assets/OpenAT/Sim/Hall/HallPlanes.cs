using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Where a plane is drawn at the home airport ("AirportPos", "TargetX", airport-and-people.md §9.2,
    /// planes-and-flights.md §3.2). <see cref="Ort"/> uses the plane location codes −1 landing roll,
    /// −2 take-off, −3 behind the windows; 0 = not shown.
    /// </summary>
    public sealed class HallPlane
    {
        public int Airline, PlaneId;
        public int Ort;
        /// <summary>Foot point: runway planes in the half-speed parallax space, close-ups in hall pixels.</summary>
        public Pt Pos;
        public int TargetX;
        /// <summary>Start hour of the departure being boarded ("Startzeit"); 255 = arrived, unloading.</summary>
        public int StartHour = 255;
        public int Gate = -1;
        /// <summary>Operations location seen at the last step (transition detection).</summary>
        public int LastLocation = int.MinValue;

        public override string ToString() => $"plane {Airline}/{PlaneId} ort {Ort} at {Pos} -> {TargetX} start {StartHour} gate {Gate}";
    }

    /// <summary>A landing announcement is due (§9.2, y = 39 of the landing roll; voiced 09:00–18:00).</summary>
    public sealed class HallLandingAnnouncement : ISimEvent
    {
        public readonly int Airline, FromCity;
        public HallLandingAnnouncement(int airline, int fromCity) { Airline = airline; FromCity = fromCity; }
    }

    /// <summary>
    /// Plane animation at the home airport ("CPlane::DoOneStep", sim-core.md §2.3 step 3.4). Deterministic
    /// and Sim-side so that boarding (persons wait until the plane stands at the gate) and the arriving
    /// passengers (spawned when the plane reaches the gate) follow the planes.
    ///
    /// The operations domain owns the gameplay state (<see cref="Plane.Location"/>, booked by sim-time
    /// milestones, see <see cref="HomeAirportTiming"/>). This model starts its sequences when operations
    /// changes a plane's location (landing at :15, to the gate at :30, take-off) and then moves the plane
    /// with the original's speeds: landing roll +6 px per step, taxiing at clamp(|dx|/30, 3, 10) to
    /// the gate's RUNE_2WAIT x + 200, take-off +7 px with a climb. A sequence that outlives its
    /// operations phase finishes on screen (the plane taxis out and climbs away).
    /// </summary>
    public sealed partial class HallState
    {
        /// <summary>Animated planes (records stay for every plane seen; <see cref="HallPlane.Ort"/> 0 = hidden).</summary>
        public readonly List<HallPlane> Planes = new List<HallPlane>();
        int _planeSkip;
        int _landKey = -1, _landCount, _departKey = -1, _departCount;

        public HallPlane FindPlane(int airline, int planeId)
        {
            foreach (var r in Planes) if (r.Airline == airline && r.PlaneId == planeId) return r;
            return null;
        }

        /// <summary>The plane stands at its gate behind the windows (Ort −3 at its TargetX) — boarding test (§7.1).</summary>
        public bool PlaneAtGate(int airline, int planeId)
        {
            var r = FindPlane(airline, planeId);
            if (r == null || r.Ort != PlaneLocation.AtGate || r.Pos.X != r.TargetX) return false;
            var plane = airline >= 0 && airline < Player.Count ? World.Players[airline].Planes.Find(planeId) : null;
            return plane != null && plane.Location == PlaneLocation.AtGate;
        }

        /// <summary>
        /// One plane step per tick while any plane is animated, else every 11th tick (skip counter of 10,
        /// sim-core.md §2.3 step 3.4).
        /// </summary>
        internal void StepPlanes()
        {
            if (_planeSkip > 0) { _planeSkip--; return; }
            bool any = false;
            for (int c = 0; c < Player.Count; c++)
            {
                var p = World.Players[c];
                if (p.IsOut) continue;
                foreach (var plane in p.Planes.Items)
                {
                    var r = FindPlane(c, plane.Id);
                    if (r == null) { r = new HallPlane { Airline = c, PlaneId = plane.Id }; Planes.Add(r); }
                    if (plane.Location != r.LastLocation)
                    {
                        OnLocationChanged(r, plane);
                        r.LastLocation = plane.Location;
                    }
                    if (r.Ort != 0) { StepPlane(r, plane); any = true; }
                }
            }
            // Planes sold, lost or of airlines that left the game.
            for (int i = Planes.Count - 1; i >= 0; i--)
            {
                var owner = World.Players[Planes[i].Airline];
                if (owner.IsOut || owner.Planes.Find(Planes[i].PlaneId) == null) Planes.RemoveAt(i);
            }
            _planeSkip = any ? 0 : 10;
        }

        TeakRand HourRand() => new TeakRand(Clock.Date + Clock.Hour);
        int HourKey => Clock.Date * 24 + Clock.Hour;

        int GateTargetX(int gate) => 200 + RuneOr(RuneId.ToWait, gate, null, new Pt(RightEnd / 2, 0)).X;

        static ref FlightEntry CurrentEntry(Plane plane) => ref plane.Plan.Entries[plane.Plan.NextFlight];

        void OnLocationChanged(HallPlane r, Plane plane)
        {
            switch (plane.Location)
            {
                case PlaneLocation.Landing:
                {
                    // Landings of one hour are staggered: −100, then −(105 + rand 30) per plane (Sim.cpp :15 step).
                    if (_landKey != HourKey) { _landKey = HourKey; _landCount = -100; }
                    r.Ort = PlaneLocation.Landing;
                    r.Pos = new Pt((LeftEnd * 3 + RightEnd * 5) / 8 / 2 + _landCount, 22 + _landCount / 2);
                    r.StartHour = 255;
                    _landCount -= 105 + HourRand().Rand(30);
                    break;
                }
                case PlaneLocation.AtGate:
                    if (plane.GateStartHour != 255)
                    {
                        // Departure at :30: from beyond the right end to the gate, staggered by 50 + rand 30.
                        if (_departKey != HourKey) { _departKey = HourKey; _departCount = 100; }
                        int gate = plane.Plan.NextFlight >= 0 ? CurrentEntry(plane).Gate : -1;
                        r.Ort = PlaneLocation.AtGate;
                        r.Pos = new Pt(RightEnd + _departCount + 200, 191);
                        r.Gate = gate;
                        r.TargetX = GateTargetX(gate);
                        r.StartHour = plane.GateStartHour & 0xFF;
                        _departCount += 50 + HourRand().Rand(30);
                    }
                    else if (r.Ort == 0) BehindWindowsAfterLanding(r, plane); // landing roll not seen (hall created later)
                    break;
                case PlaneLocation.TakingOff:
                    if (r.Ort == 0) { r.Ort = PlaneLocation.TakingOff; r.Pos = new Pt(-40, 26 + 18); }
                    break;
            }
        }

        void BehindWindowsAfterLanding(HallPlane r, Plane plane)
        {
            int gate = plane.Plan.NextFlight >= 0 ? CurrentEntry(plane).Gate : -1;
            if (gate < 0) { r.Ort = 0; return; }
            r.Ort = PlaneLocation.AtGate;
            r.Pos = new Pt(RightEnd + 600, 190);
            r.Gate = gate;
            r.TargetX = GateTargetX(gate);
            r.StartHour = 255;
        }

        void StepPlane(HallPlane r, Plane plane)
        {
            switch (r.Ort)
            {
                case PlaneLocation.Landing:
                    r.Pos.X += 6;
                    if (r.Pos.Y < 22 + 18) r.Pos.Y++;
                    if (r.Pos.Y == 21 + 18 && plane.Plan.NextFlight >= 0)
                    {
                        ref var e = ref CurrentEntry(plane);
                        if (e.Kind != FlightKind.Auto && e.Kind != FlightKind.Freight && !World.CallItADay && Clock.Time >= 9 * 60000 && Clock.Time <= 18 * 60000)
                            World.Events.Publish(new HallLandingAnnouncement(r.Airline, e.From));
                    }
                    if (r.Pos.X > RightEnd / 2 + 600)
                    {
                        if (plane.Location == PlaneLocation.Landing) { r.Pos.X -= 6; break; } // roll still running in operations
                        if (plane.Location == PlaneLocation.AtGate && plane.GateStartHour == 255) BehindWindowsAfterLanding(r, plane);
                        else r.Ort = 0; // no gate, or not touching home: not shown any more
                    }
                    break;

                case PlaneLocation.TakingOff:
                    r.Pos.X += 7;
                    if (r.Pos.X * 2 > (LeftEnd + RightEnd * 3) / 4 && (r.Pos.X & 1) == 0) r.Pos.Y--;
                    if (r.Pos.Y < -30) r.Ort = 0; // airborne (operations books the flight by sim time)
                    break;

                case PlaneLocation.AtGate:
                {
                    int speed = Math.Max(3, Math.Min(10, Math.Abs(r.Pos.X - r.TargetX) / 30));
                    int hour = Clock.Hour, minute = Clock.Minute;
                    if (r.Pos.X > r.TargetX)
                    {
                        r.Pos.X -= speed;
                        r.Pos.Y = 190;
                        if (r.Pos.X <= r.TargetX)
                        {
                            r.Pos.X = r.TargetX;
                            OpenAT.Sim.Tutorial.TutorialEngine.PlaneReachedGate(World, r.Airline);
                            if (plane.Location == PlaneLocation.AtGate)
                            {
                                if (r.StartHour == 255) UnloadAtGate(r, plane);
                                else CallBoarding(r.Gate);
                            }
                        }
                    }
                    else if (r.StartHour != 255 && ((r.StartHour == hour && minute >= 55) || r.StartHour < hour || (r.StartHour == 23 && hour == 0)))
                    {
                        // Boarded: taxi out to the runway, then take off.
                        r.Pos.X -= speed;
                        OpenAT.Sim.Tutorial.TutorialEngine.PlaneLeftGate(World, r.Airline);
                        r.Pos.Y = 190;
                        if (r.Pos.X < LeftEnd - 180) { r.Ort = PlaneLocation.TakingOff; r.Pos = new Pt(-40, 26 + 18); }
                    }
                    else if (r.StartHour == 255 && (minute >= 45 || Math.Abs(r.Pos.X - r.TargetX) > 20))
                    {
                        // Unloaded: taxi away to the parking position.
                        r.Pos.X -= speed;
                        r.Pos.Y = 190;
                        if (r.Pos.X < LeftEnd - 180) r.Ort = 0;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// A departing plane reached its gate (§7.1 step 6): every waiting or seated passenger of that gate
        /// stands up (frees the seat) and walks to the scanner.
        /// </summary>
        internal void CallBoarding(int gate)
        {
            if (gate < 0 || Flights == null) return;
            for (int c = Persons.Count - 1; c >= 0; c--)
            {
                var p = Persons[c];
                if (!p.Alive) continue;
                int st = p.BaseState;
                if (st != PState.Waiting && st != PState.SitWaiting) continue;
                if (!Flights.TryGetFlight(p.FlightAirline, p.FlightPlaneId, p.FlightPlaneIndex, out int g, out _) || g != gate) continue;
                if (st == PState.SitWaiting)
                {
                    int col = (p.Position.X + 4400) / 44 - 100, row = (p.Position.Y + 2200) / 22 - 100;
                    int i = col + Plate.SpareColumns;
                    int bit = p.LookDir == 7 ? 1 << ((row - 1) * 2 + 1) : 1 << ((row + 1) * 2);
                    if ((uint)i < (uint)Seats.Length) Seats[i] = (ushort)(Seats[i] & ~bit);
                }
                if (p.Dir == 6) p.Dir = 4;
                if (p.Dir == 7) p.Dir = 1;
                p.State = PState.ToScanner;
                p.Target = RuneOr(RuneId.Scanner, gate, null, p.Target);
            }
        }

        /// <summary>
        /// An arriving plane reached its gate (§6.6 "Arriving passengers", planes-and-flights.md §13): one
        /// figure per 15 passengers (economy and first class separately, plus one each) at the gate's
        /// WAITPLANE rune, with a mood judged from condition, cabin quality, crew and price. The spawn spot
        /// time is rounded to 80 ticks; the generator is seeded gate + start hour + hour + date.
        /// </summary>
        internal void UnloadAtGate(HallPlane r, Plane plane)
        {
            if (plane.Plan.NextFlight < 0) return;
            var w = World;
            var owner = w.Players[r.Airline];
            ref var e = ref CurrentEntry(plane);
            if (!e.IsPassengerFlight) return;
            var spot = RandomRune(RuneId.WaitPlane, e.Gate, null);
            if (spot == null) return;

            int tooExpensive = 0;
            if (e.Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(e.ObjectId);
                long c1 = ri >= 0 ? owner.RentRoutes[ri].TicketPrice : 0, c2 = (long)w.ReferencePrice(e.From, e.To) * 3;
                if (c2 > 0 && c1 > c2) tooExpensive = (int)(c1 * 100 / c2);
                if (c2 > 0 && c1 > c2 * 2) tooExpensive = (int)(c1 * 100 / (c2 * 2));
                if (tooExpensive > 100) tooExpensive = 100;
            }
            var crew = w.Rules.Get<ICrewMapping>().Crew(w, owner, plane);
            int quality = plane.Equipment[Equip.Seats] + plane.Equipment[Equip.Food] + plane.Equipment[Equip.Trays] + plane.Equipment[Equip.Deco]
                          + crew.Attendants - plane.Attendants;
            int crewQuality = plane.PersonalQuality;
            int airline = r.Airline;
            int brochure = w.Rules.Get<IFlightSabotageState>().BrochureTarget(w, owner);
            if (brochure >= 0 && brochure < Player.Count) airline = brochure;

            Queue.SetSpotTime(spot.Value, (Clock.TimeSlice + 50) / 80 * 80);
            var rand = new TeakRand(e.Gate + e.StartHour + Clock.Hour + Clock.Date);
            if (e.Passengers > 0)
                for (int c = e.Passengers / HallFlightLink.PassengersPerPerson; c >= 0; c--)
                {
                    int mood;
                    if (rand.Rand(70) > plane.Condition) mood = Mood.Repair;
                    else if (quality > 4 && rand.Rand(100) < 40) mood = Mood.Stars2;
                    else if (quality > 8 && rand.Rand(100) < 55) mood = Mood.Stars3;
                    else if (rand.Rand(65) > crewQuality) mood = Mood.Sick;
                    else if (tooExpensive != 0 && rand.Rand(130) < tooExpensive) mood = Mood.Expensive;
                    else mood = Mood.Smile;
                    int koffer = rand.Rand(2) - 1;
                    AddPersonToQueue(CustomerId(1, koffer, rand), spot.Value, Reason.Leaving, airline, 0, 0, mood);
                }
            if (e.PassengersFC > 0)
                for (int c = e.PassengersFC / HallFlightLink.PassengersPerPerson; c >= 0; c--)
                {
                    int mood;
                    if (rand.Rand(90) > plane.Condition) mood = Mood.RepairFC;
                    else if (quality > 8 && rand.Rand(100) < 40) mood = Mood.Stars2FC;
                    else if (quality > 10 && rand.Rand(100) < 55) mood = Mood.Stars3FC;
                    else if (rand.Rand(100) > crewQuality) mood = Mood.SickFC;
                    else mood = Mood.SmileFC; // the first-class price check never fires in the original
                    int koffer = rand.Rand(2) - 1;
                    AddPersonToQueue(CustomerId(1, koffer, rand), spot.Value, Reason.Leaving, airline, 0, 0, mood, firstClass: true);
                }
        }
    }

    /// <summary>Step 3.4 of the per-tick sequence: plane animation at the home airport. Order 340 (after persons 300).</summary>
    public sealed class HallPlaneSystem : SimSystem
    {
        public const string SystemName = "hall.planes";
        public override string Name => SystemName;
        public override int Order => 340;

        public override void OnTick(World w)
        {
            if (w.Paused) return;
            w.Hall?.StepPlanes();
        }
    }
}
