namespace OpenAT.Sim.Fleet
{
    /// <summary>Object type of a flight-plan entry (planes-and-flights.md §9).</summary>
    public static class FlightKind
    {
        public const int None = 0;
        public const int Route = 1;
        /// <summary>Passenger contract ("Auftrag").</summary>
        public const int Contract = 2;
        /// <summary>Automatic empty transfer (ferry) flight.</summary>
        public const int Auto = 3;
        public const int Freight = 4;
    }

    /// <summary>Validity code of a plan entry (planes-and-flights.md §9).</summary>
    public static class EntryOkay
    {
        public const int Ok = 0;
        public const int WrongDay = 1;
        public const int Done = 2;
        public const int TooSmall = 3;
    }

    /// <summary>Gate value of a plan entry.</summary>
    public static class GateCode
    {
        /// <summary>No gate free at the home airport.</summary>
        public const int None = -1;
        /// <summary>The flight does not touch the home airport.</summary>
        public const int NotHome = -2;
    }

    /// <summary>
    /// One flight-plan entry ("CFlugplanEintrag", planes-and-flights.md §9). A value type so that the
    /// original's slot copies (insertions shift entries) behave identically. Deleting an entry only sets
    /// <see cref="Kind"/> to 0; the other fields keep their values like in the original.
    /// </summary>
    public struct FlightEntry
    {
        public int Kind;
        /// <summary>Route id, contract id or freight id.</summary>
        public int ObjectId;
        /// <summary>City ids.</summary>
        public int From, To;
        /// <summary>Day and hour (0–23) of the departure block.</summary>
        public int StartDate, StartHour;
        public int LandDate, LandHour;
        /// <summary>Booked economy / first-class passengers (freight: tons carried in <see cref="Passengers"/>).</summary>
        public int Passengers, PassengersFC;
        /// <summary>Passengers already spawned at the home terminal (visual).</summary>
        public int PArrived;
        /// <summary>Lead time in hours between placing and starting (a byte in the original).</summary>
        public int HoursBefore;
        public int TicketPrice, TicketPriceFC;
        /// <summary>Gate number ≥ 0, or <see cref="GateCode"/>.</summary>
        public int Gate;
        public bool GateWarning;
        /// <summary><see cref="EntryOkay"/>.</summary>
        public int Okay;

        public static FlightEntry Empty => new FlightEntry { Kind = FlightKind.None, ObjectId = -1, Gate = GateCode.None };

        public bool IsEmpty => Kind == FlightKind.None;
        public bool IsPassengerFlight => Kind == FlightKind.Route || Kind == FlightKind.Contract;
        /// <summary>Departure as absolute hour (day × 24 + hour).</summary>
        public int StartAbs => StartDate * 24 + StartHour;
        public int LandAbs => LandDate * 24 + LandHour;

        /// <summary>Starts after "now + 1 h": the original's test for flights that may still be edited.</summary>
        public bool IsFuture(int date, int hour) => StartDate > date || (StartDate == date && StartHour > hour + 1);

        public override string ToString() => $"k{Kind} {From:X}->{To:X} {StartDate}/{StartHour}-{LandDate}/{LandHour} p{Passengers}/{PassengersFC} g{Gate}";
    }

    /// <summary>
    /// A plane's flight plan ("CFlugplan", planes-and-flights.md §9): 60 entries (10 per day × 6),
    /// <see cref="StartCity"/> = where the plan begins (location after the last completed flight).
    /// </summary>
    public sealed class FlightPlan
    {
        public const int Size = 60;

        public int StartCity;
        public readonly FlightEntry[] Entries = new FlightEntry[Size];
        /// <summary>First non-empty entry whose landing is not before the current hour, or −1.</summary>
        public int NextFlight = -1;
        /// <summary>First non-empty entry with start + 1 ≥ current hour, or −1.</summary>
        public int NextStart = -1;

        public FlightPlan()
        {
            for (int i = 0; i < Size; i++) Entries[i] = FlightEntry.Empty;
        }

        public void UpdateNextFlight(int date, int hour)
        {
            NextFlight = -1;
            for (int e = 0; e < Size; e++)
            {
                ref var f = ref Entries[e];
                if (f.Kind != 0 && (f.LandDate > date || (f.LandDate == date && f.LandHour >= hour))) { NextFlight = e; break; }
            }
        }

        public void UpdateNextStart(int date, int hour)
        {
            NextStart = -1;
            for (int e = 0; e < Size; e++)
            {
                ref var f = ref Entries[e];
                if (f.Kind != 0 && (f.StartDate > date || (f.StartDate == date && f.StartHour + 1 >= hour))) { NextStart = e; break; }
            }
        }

        public void UpdatePointers(int date, int hour)
        {
            UpdateNextFlight(date, hour);
            UpdateNextStart(date, hour);
        }

        public bool Contains(int kind, int id1, int id2 = int.MinValue)
        {
            for (int e = 0; e < Size; e++)
                if (Entries[e].Kind == kind && (Entries[e].ObjectId == id1 || Entries[e].ObjectId == id2)) return true;
            return false;
        }

        /// <summary>Number of non-empty entries.</summary>
        public int Count
        {
            get { int n = 0; for (int e = 0; e < Size; e++) if (Entries[e].Kind != 0) n++; return n; }
        }
    }
}
