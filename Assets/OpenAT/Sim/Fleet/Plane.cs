using System.Collections.Generic;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Fleet
{
    /// <summary>Location codes ("Ort", planes-and-flights.md §3.2). Values ≥ 0 are city ids (on the ground there).</summary>
    public static class PlaneLocation
    {
        /// <summary>Landing roll at the home airport.</summary>
        public const int Landing = -1;
        /// <summary>Taking off at the home airport.</summary>
        public const int TakingOff = -2;
        /// <summary>Taxiing behind the windows / at a gate of the home airport (boarding or deboarding).</summary>
        public const int AtGate = -3;
        public const int Airborne = -5;

        public static bool IsOnGround(int ort) => ort >= 0;
    }

    /// <summary>Equipment items (planes-and-flights.md §6.1). Index into <see cref="Plane.Equipment"/>.</summary>
    public static class Equip
    {
        public const int Seats = 0, Food = 1, Trays = 2, Deco = 3, Engines = 4, Tyres = 5, Electronics = 6, Safety = 7;
        public const int Count = 8;

        /// <summary>Price per level (§6.1): per seat unit for seats/trays/deco, per economy passenger and flight for food, per plane otherwise.</summary>
        public static readonly int[][] Costs =
        {
            new[] { 300, 1200, 3000 },
            new[] { 10, 20, 50 },
            new[] { 100, 800, 3400 },
            new[] { 500, 1500, 6000 },
            new[] { 100000, 750000, 3500000 },
            new[] { 50000, 300000, 900000 },
            new[] { 600000, 1500000, 8000000 },
            new[] { 0, 2000000, 6000000 },
        };

        public static bool IsPerSeat(int item) => item == Seats || item == Trays || item == Deco;

        /// <summary>Cost of switching <paramref name="item"/> from <paramref name="from"/> to <paramref name="to"/> (§6.1; negative = refund; food free).</summary>
        public static int ChangeCost(int item, int from, int to, int seatUnits)
        {
            if (item == Food || from == to) return 0;
            int d = Costs[item][to] - Costs[item][from] / 2;
            return IsPerSeat(item) ? seatUnits * d : d;
        }
    }

    /// <summary>One entry of a plane's incident list (planes-and-flights.md §3.3).</summary>
    public struct PlaneIncidentRecord
    {
        public int Date;
        public int Time;
        /// <summary>0..8 quality incidents, 10 + season seasonal incidents (§7.6).</summary>
        public int Code;
    }

    /// <summary>
    /// An individual plane ("CPlane", planes-and-flights.md §3). All game logic reads the per-plane
    /// copies of the type data (<see cref="Speed"/>, <see cref="Range"/>, ...), so designer and
    /// catalogue planes are handled uniformly.
    /// </summary>
    public sealed class Plane : IAlbumItem
    {
        public int Id { get; set; }
        public string Name;
        /// <summary>Plane type id (<see cref="PlaneType.Id"/>), or −1 for a designer plane.</summary>
        public int TypeId = -1;
        /// <summary>Designer blueprint when <see cref="TypeId"/> is −1.</summary>
        public PlaneDesign Design;

        // ---- per-plane copies of the type data ("pt*")
        public string Maker = "", Model = "", Comment = "";
        public int FirstYear;
        /// <summary>Range km.</summary>
        public int Range;
        /// <summary>Cruise speed km/h.</summary>
        public int Speed;
        /// <summary>Seat units of the type (a first-class seat uses 2).</summary>
        public int Seats;
        public int Pilots, Attendants;
        public int Tank;
        /// <summary>Fuel burn l/h.</summary>
        public int Burn;
        public int Price;
        public float MaintenanceFactor = 1;
        /// <summary>Noise ("ptLaerm"), 0 for catalogue types.</summary>
        public int Noise;

        public int BuildYear;
        /// <summary>Starting-fleet flag: resale value / 10.</summary>
        public bool Sponsored;

        /// <summary><see cref="PlaneLocation"/> code or city id.</summary>
        public int Location;
        /// <summary>Condition 0..100 ("Zustand", byte semantics).</summary>
        public int Condition, TargetCondition, WorstCondition;
        /// <summary>Hours of remaining technical breakdown.</summary>
        public int Problem;
        /// <summary>Hours the plane is held (sabotage impound).</summary>
        public int PseudoProblem;
        public int NumIncidents;
        /// <summary>Last 10 incidents, newest first.</summary>
        public readonly List<PlaneIncidentRecord> Incidents = new List<PlaneIncidentRecord>();

        /// <summary>Current equipment levels 0..2, index <see cref="Equip"/>.</summary>
        public readonly int[] Equipment = new int[Equip.Count];
        /// <summary>Levels chosen in the equipment room, applied when on the ground (§6.2).</summary>
        public readonly int[] EquipmentTarget = new int[Equip.Count];
        /// <summary>Configured economy / first-class seats.</summary>
        public int MaxPassengers, MaxPassengersFC;
        /// <summary>Seats removed for freight ("OhneSitze", §12.6).</summary>
        public bool SeatsRemoved;

        // ---- crew (§8)
        public int CrewPilots, CrewAttendants;
        /// <summary>Desired attendants (type value .. 2 × type value).</summary>
        public int MaxAttendants;
        /// <summary>Average talent of the assigned crew (60 if none).</summary>
        public int PersonalQuality = 60;

        // ---- statistics (§3.6)
        /// <summary>Daily balance of the last 7 days, index 0 = today.</summary>
        public readonly long[] Balances = new long[7];
        public int Load, LoadFC;
        public long Kilometers, TotalPassengers, MaintenanceCosts;

        public readonly FlightPlan Plan = new FlightPlan();

        // ---- home-airport animation milestones (sim-time driven, see HomeAirportTiming)
        /// <summary>Copy of the departing flight's start hour while at the gate; 255 = arrived, unloading.</summary>
        public int GateStartHour = 255;
        /// <summary>Absolute game minute at which the current home-airport phase began.</summary>
        public long PhaseStartMinute;
        /// <summary>Passengers of the current arrival have left the plane (satisfaction evaluated).</summary>
        public bool Unloaded;
        /// <summary>Globe position (degrees, visual; interpolated in flight).</summary>
        public int GlobeX, GlobeY;

        public int ConfiguredSeatUnits => MaxPassengers + 2 * MaxPassengersFC;

        /// <summary>Market value (planes-and-flights.md §4.5): Price·Z/10000·Z·(year − 1900)/120 (64-bit, left to right), /10 if sponsored.</summary>
        public int MarketValue
        {
            get
            {
                long v = (long)Price * Condition / 10000 * Condition * (BuildYear - 1900) / 120;
                int rc = unchecked((int)v);
                if (Sponsored) rc /= 10;
                return rc;
            }
        }

        /// <summary>Sum of the 7-day balances.</summary>
        public long BalanceSum { get { long s = 0; foreach (var b in Balances) s += b; return s; } }

        public void AddIncident(int date, int time, int code)
        {
            Incidents.Insert(0, new PlaneIncidentRecord { Date = date, Time = time, Code = code });
            if (Incidents.Count > 10) Incidents.RemoveAt(10);
            NumIncidents++;
        }

        /// <summary>Creates a plane of a catalogue type (planes-and-flights.md §3.7 defaults).</summary>
        public static Plane FromType(PlaneType t, string name, int condition, int buildYear, int homeCity)
        {
            var p = new Plane
            {
                Name = name, TypeId = t.Id,
                Maker = t.Manufacturer, Model = t.Name, Comment = t.Comment,
                FirstYear = t.FirstYearBuilt, Range = t.Range, Speed = t.Speed, Seats = t.Seats,
                Pilots = t.Pilots, Attendants = t.Attendants, Tank = t.TankSize, Burn = t.FuelBurn,
                Price = t.Price, MaintenanceFactor = (float)t.MaintenanceFactor, Noise = 0,
            };
            p.InitCommon(condition, buildYear, homeCity);
            p.MaxAttendants = t.Attendants;
            return p;
        }

        /// <summary>Creates a plane from a designer blueprint (planes-and-flights.md §5.5).</summary>
        public static Plane FromDesign(PlaneDesign d, PlaneDesignCatalog cat, string name, int buildYear, int homeCity)
        {
            var s = d.Stats(cat);
            var p = new Plane
            {
                Name = name, TypeId = -1, Design = d.Clone(),
                Maker = "", Model = d.Name, Comment = "", FirstYear = 2002,
                Range = s.Range, Speed = s.Speed, Seats = s.Seats, Pilots = s.Pilots, Attendants = s.Attendants,
                Tank = s.Tank, Burn = s.Burn, Price = (int)s.Cost, Noise = s.Noise,
                MaintenanceFactor = (float)((s.Maintenance + 100.0) / 100.0),
            };
            p.InitCommon(100, buildYear, homeCity);
            p.MaxAttendants = s.Attendants;
            return p;
        }

        void InitCommon(int condition, int buildYear, int homeCity)
        {
            Condition = TargetCondition = WorstCondition = condition & 0xFF;
            BuildYear = buildYear;
            Location = homeCity;
            MaxPassengers = Seats * 6 / 8;
            MaxPassengersFC = Seats * 1 / 8;
            Plan.StartCity = homeCity;
        }

        /// <summary>Deep copy (museum offer copied into a fleet).</summary>
        public Plane Clone()
        {
            // Copy every mutable public field, then the readonly containers element by element.
            var c = new Plane();
            foreach (var f in typeof(Plane).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                if (!f.IsInitOnly) f.SetValue(c, f.GetValue(this));
            c.Id = 0;
            for (int i = 0; i < Equip.Count; i++) { c.Equipment[i] = Equipment[i]; c.EquipmentTarget[i] = EquipmentTarget[i]; }
            for (int i = 0; i < 7; i++) c.Balances[i] = Balances[i];
            c.Incidents.AddRange(Incidents);
            c.Plan.StartCity = Plan.StartCity;
            for (int i = 0; i < FlightPlan.Size; i++) c.Plan.Entries[i] = Plan.Entries[i];
            c.Plan.NextFlight = Plan.NextFlight;
            c.Plan.NextStart = Plan.NextStart;
            c.Design = Design?.Clone();
            return c;
        }

        public override string ToString() => Name + " (" + Maker + " " + Model + ")";
    }
}
