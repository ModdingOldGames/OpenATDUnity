namespace OpenAT.Sim.Stats
{
    /// <summary>
    /// Ids of the 40 statistic series per airline (economy.md §16.3, sim-core.md §4.1). The values
    /// are the original's indexes; 24 is unused. Kind "set" = snapshot written by
    /// <see cref="World.UpdateStatistics(Player)"/>, "add" = running total fed by the owning domain.
    /// </summary>
    public static class StatId
    {
        public const int Count = 40;

        public const int Money = 0;              // set
        public const int Debt = 1;               // set
        public const int Planes = 2;             // set
        public const int Passengers = 3;         // add (flights)
        public const int HomePassengers = 4;     // add (flights)
        public const int SharePrice = 5;         // set
        public const int Flights = 6;            // add (flights)
        public const int Orders = 7;             // add (contracts)
        public const int LastMinuteOrders = 8;   // add (contracts)
        public const int Penalties = 9;          // add (contracts)
        public const int CompanyValue = 10;      // set
        public const int SatisfiedPassengers = 11;   // add (flights)
        public const int UnsatisfiedPassengers = 12; // add (flights)
        public const int StaffSatisfaction = 13; // set
        public const int Delays = 14;            // add (flights)
        public const int Accidents = 15;         // add (sabotage)
        public const int SabotageCommitted = 16; // add (sabotage)
        public const int Employees = 17;         // set
        public const int MissionProgress = 18;   // set
        public const int Maintenance = 19;       // add (finance, overnight)
        public const int Branches = 20;          // set
        public const int Routes = 21;            // set
        public const int Salaries = 22;          // set
        public const int Awareness = 23;         // set
        public const int SharesIssued = 25;      // set
        /// <summary>Shares held of airline 0..3: <c>SharesHeldBase + airline</c>.</summary>
        public const int SharesHeldBase = 26;    // set
        public const int IncomeRoutes = 30;      // add (flights)
        public const int IncomeOrders = 31;      // add (flights)
        public const int IncomeSales = 32;       // never written
        public const int IncomeOther = 33;       // add (finance and others)
        public const int ExpenseRents = 34;      // add (finance)
        public const int ExpenseKerosene = 35;   // add (flights + tank)
        public const int ExpensePenalties = 36;  // add (contracts, sabotage fines)
        public const int ExpensePurchases = 37;  // never written
        public const int ExpenseOther = 38;      // add (finance and others)
        public const int FreightOrders = 39;     // add (freight)

        /// <summary>Series fed by <see cref="PlayerStatistics.Add"/> ("add" above): daily values that start from 0
        /// each day once the day shift is fixed (<see cref="GameOptions.FixStatisticsDayShift"/>).</summary>
        public static bool IsAdditive(int id)
        {
            switch (id)
            {
                case Passengers: case HomePassengers: case Flights: case Orders: case LastMinuteOrders:
                case Penalties: case SatisfiedPassengers: case UnsatisfiedPassengers: case Delays: case Accidents:
                case SabotageCommitted: case Maintenance: case FreightOrders:
                    return true;
                default:
                    return id >= IncomeRoutes && id <= ExpenseOther;
            }
        }

        /// <summary>Series whose month value is the sum of the days (economy.md §16.1).</summary>
        public static bool MonthIsSumOfDays(int id)
        {
            switch (id)
            {
                case Passengers: case HomePassengers: case Flights: case Orders: case LastMinuteOrders:
                case Penalties: case SatisfiedPassengers: case UnsatisfiedPassengers: case StaffSatisfaction:
                case Delays: case Accidents: case Maintenance: case Salaries:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// One statistic series (economy.md §16.1, original <c>SValue</c>): 31 day values (index 0 =
    /// today) and 12 month values. Original bug: the day shift does not clear <c>Days[0]</c>, so additive
    /// series are running totals since the game start (and their month sums add up those totals).
    /// Fixed by default (<see cref="GameOptions.FixStatisticsDayShift"/>): additive series start each day
    /// at 0. <see cref="Total"/> keeps the running total for the rules the original built on it.
    /// </summary>
    public sealed class StatSeries
    {
        public readonly long[] Days = new long[31];
        public readonly long[] Months = new long[12];
        /// <summary>Sum of everything added since the game start (what the original's Days[0] holds).</summary>
        public long Total;

        public void Clear()
        {
            System.Array.Clear(Days, 0, Days.Length);
            System.Array.Clear(Months, 0, Months.Length);
            Total = 0;
        }

        public long this[int pastDay] => Days[pastDay];

        public void Set(long value) => Days[0] = value;
        public void Add(long value) { Days[0] += value; Total += value; }

        /// <summary>Overnight day shift: Days[1..30] ← Days[0..29]; Days[0] keeps its value unless
        /// <paramref name="clearToday"/> (the fix for additive series).</summary>
        public void NewDay(bool clearToday = false)
        {
            System.Array.Copy(Days, 0, Days, 1, 30);
            if (clearToday) Days[0] = 0;
        }

        /// <summary>Month shift; Months[0] = sum of the visible days or today's value.</summary>
        public void NewMonth(bool sumOfDays, int date)
        {
            System.Array.Copy(Months, 0, Months, 1, 11);
            Months[0] = sumOfDays ? Sum(date) : Days[0];
        }

        /// <summary>Sum of Days[0..min(29, date)] (original <c>GetSum</c>).</summary>
        public long Sum(int date)
        {
            long s = 0;
            for (int c = 0; c < 30; c++) if (c <= date) s += Days[c];
            return s;
        }
    }

    /// <summary>The 40 series of one airline.</summary>
    public sealed class PlayerStatistics
    {
        public readonly StatSeries[] Series = new StatSeries[StatId.Count];

        public PlayerStatistics()
        {
            for (int i = 0; i < Series.Length; i++) Series[i] = new StatSeries();
        }

        public StatSeries this[int id] => Series[id];

        /// <summary>Adds to today's value of a series (original <c>AddAtPastDay(0, v)</c>).</summary>
        public void Add(int id, long value) => Series[id].Add(value);
        /// <summary>Sets today's value of a series (original <c>SetAtPastDay(0, v)</c>).</summary>
        public void Set(int id, long value) => Series[id].Set(value);
        public long Today(int id) => Series[id].Days[0];
        public long PastDay(int id, int day) => Series[id].Days[day];
        /// <summary>Running total since the game start of an additive series (the original's Days[0]).</summary>
        public long Total(int id) => Series[id].Total;

        public void Clear() { foreach (var s in Series) s.Clear(); }

        /// <summary>Per-airline new day step 8 (sim-core.md §2.7): month roll on Date % 30 == 0, then day roll.</summary>
        /// <param name="clearAdditive">Start the additive series at 0 for the new day (<see cref="GameOptions.FixStatisticsDayShift"/>).</param>
        public void Roll(int date, bool clearAdditive = false)
        {
            if (date % 30 == 0)
                for (int c = 0; c < StatId.Count; c++) Series[c].NewMonth(StatId.MonthIsSumOfDays(c), date);
            for (int c = 0; c < StatId.Count; c++) Series[c].NewDay(clearAdditive && StatId.IsAdditive(c));
        }
    }
}
