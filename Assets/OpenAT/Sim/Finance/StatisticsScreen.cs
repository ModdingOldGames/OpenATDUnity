using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Finance
{
    /// <summary>Row kinds of the statistics screen (rooms-and-ui-8 §11.3).</summary>
    public enum StatRowKind { Blank, Heading, Value, Currency, SinglePercent, Percent, SumCurrency, SumDiff, ShareValue }

    /// <summary>One label row: <c>STAT</c> text id, kind and series.</summary>
    public readonly struct StatRow
    {
        public readonly int TextId;
        public readonly StatRowKind Kind;
        public readonly int Stat;
        public StatRow(int text, StatRowKind kind, int stat = -1) { TextId = text; Kind = kind; Stat = stat; }

        /// <summary>A plotted value row (sums, headings and blanks are not plotted).</summary>
        public bool IsSeries => Kind == StatRowKind.Value || Kind == StatRowKind.Currency || Kind == StatRowKind.SinglePercent || Kind == StatRowKind.Percent;
        public bool IsTickable => IsSeries;
    }

    /// <summary>
    /// The three label groups of the statistics screen (rooms-and-ui-8 §11.6) and the table / graph
    /// values (§11.3, §11.4). Pure read-only computations over <see cref="PlayerStatistics"/>.
    /// </summary>
    public static class StatisticsScreen
    {
        public const int MaxRows = 16;
        /// <summary>Selectable graph periods in days (the code's list incl. 21 and 270).</summary>
        public static readonly int[] Periods = { 7, 14, 21, 30, 90, 180, 270, 360 };

        static StatRow H(int t) => new StatRow(t, StatRowKind.Heading);
        static StatRow C(int t, int s) => new StatRow(t, StatRowKind.Currency, s);
        static StatRow V(int t, int s) => new StatRow(t, StatRowKind.Value, s);
        static readonly StatRow B = new StatRow(0, StatRowKind.Blank);

        public static readonly StatRow[][] Groups =
        {
            new[]
            {
                H(8002), C(8003, StatId.IncomeRoutes), C(8004, StatId.IncomeOrders), C(8006, StatId.IncomeOther),
                new StatRow(8014, StatRowKind.SumCurrency), B,
                H(8007), C(8008, StatId.ExpenseRents), C(8009, StatId.Salaries), C(8010, StatId.ExpenseKerosene),
                C(8011, StatId.Maintenance), C(8012, StatId.ExpensePenalties), C(8006, StatId.ExpenseOther),
                new StatRow(8014, StatRowKind.SumCurrency), B, new StatRow(8015, StatRowKind.SumDiff),
            },
            new[]
            {
                H(8100), V(8101, StatId.SharesIssued), C(8102, StatId.SharePrice),
                V(8103, StatId.SharesHeldBase), V(8104, StatId.SharesHeldBase + 1), V(8105, StatId.SharesHeldBase + 2), V(8106, StatId.SharesHeldBase + 3),
                new StatRow(8107, StatRowKind.ShareValue), B,
                H(8110), C(8114, StatId.Money), C(8116, StatId.Debt), C(8115, StatId.CompanyValue), new StatRow(8113, StatRowKind.SinglePercent, StatId.Awareness),
            },
            new[]
            {
                H(8200), V(8201, StatId.Planes), V(8202, StatId.Routes), V(8221, StatId.Branches), V(8203, StatId.Flights),
                V(8204, StatId.Orders), V(8205, StatId.LastMinuteOrders), V(8206, StatId.Delays), V(8207, StatId.Accidents),
                V(8208, StatId.SabotageCommitted), B,
                H(8210), V(8211, StatId.Passengers), new StatRow(8212, StatRowKind.Percent, StatId.SatisfiedPassengers),
                V(8111, StatId.Employees), new StatRow(8112, StatRowKind.Percent, StatId.StaffSatisfaction),
            },
        };

        /// <summary>Raw sample of a series row: past day <paramref name="index"/> or past month (rows of kind Percent → % of the row above).</summary>
        public static long Sample(Player p, StatRow[] rows, int row, int index, bool monthly)
        {
            var r = rows[row];
            long v = Raw(p, r.Stat, index, monthly);
            if (r.Kind != StatRowKind.Percent) return v;
            long d = row > 0 && rows[row - 1].Stat >= 0 ? Raw(p, rows[row - 1].Stat, index, monthly) : 0;
            return d == 0 ? 0 : v * 100 / d;
        }

        static long Raw(Player p, int stat, int index, bool monthly)
        {
            var s = p.Statistics[stat];
            if (monthly) return index >= 0 && index < s.Months.Length ? s.Months[index] : 0;
            return index >= 0 && index < s.Days.Length ? s.Days[index] : 0;
        }

        /// <summary>
        /// Today's value of every row for one airline (§11.3 value formatting): null for headings and
        /// blanks. Sums add the ticked value rows since the previous sum; the difference row adds the
        /// sums; the share value needs the share-price row (row 2) ticked and sums the ticked holdings.
        /// </summary>
        public static long?[] TableValues(World w, Player p, int group, bool[] ticked)
        {
            var rows = Groups[group];
            var res = new long?[rows.Length];
            long sum = 0, diff = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                switch (r.Kind)
                {
                    case StatRowKind.Blank:
                    case StatRowKind.Heading:
                        break;
                    case StatRowKind.SumCurrency:
                        res[i] = sum; diff += sum; sum = 0;
                        break;
                    case StatRowKind.SumDiff:
                        res[i] = diff; diff = 0;
                        break;
                    case StatRowKind.ShareValue:
                    {
                        long v = 0;
                        if (ticked[2])
                            for (int k = 3; k <= 6; k++)
                                if (ticked[k]) v += p.Statistics.Today(rows[k].Stat) * w.Players[k - 3].Statistics.Today(StatId.SharePrice);   // that day's recorded price
                        res[i] = v;
                        break;
                    }
                    default:
                        res[i] = Sample(p, rows, i, 0, false);
                        if (ticked[i]) sum += res[i].Value;
                        break;
                }
            }
            return res;
        }

        /// <summary>Next shorter / longer period (zoom buttons).</summary>
        public static int StepPeriod(int days, int dir)
        {
            int i = System.Array.IndexOf(Periods, days);
            if (i < 0) i = 0;
            i = System.Math.Max(0, System.Math.Min(Periods.Length - 1, i + dir));
            return Periods[i];
        }
    }

    /// <summary>
    /// What the statistics screen remembers between visits (rooms-and-ui-8 §11.7): group, players shown,
    /// graph sheet down, period, ticks per group. Kept on the airline (saved with the game).
    /// </summary>
    public sealed class StatisticsScreenState
    {
        public const string Key = "finance.statisticsScreen";
        public int Group;
        public int PlayerMask = 15;
        public bool GraphDown;
        public int Days = 7;   // a new game opens the graph on one week (sim-core §3.6)
        public readonly bool[][] Ticked = { NewTicks(), NewTicks(), NewTicks() };

        static bool[] NewTicks()
        {
            var t = new bool[StatisticsScreen.MaxRows];
            for (int i = 0; i < t.Length; i++) t[i] = true;
            return t;
        }

        public static StatisticsScreenState Of(Player p)
        {
            if (p.Extensions.TryGetValue(Key, out var v) && v is StatisticsScreenState s) return s;
            s = new StatisticsScreenState();
            p.Extensions[Key] = s;
            return s;
        }

        /// <summary>Label click (§11.5): a heading toggles its block, a row toggles itself, shift = solo in its block.</summary>
        public void Click(int row, bool solo)
        {
            var rows = StatisticsScreen.Groups[Group];
            var t = Ticked[Group];
            if (row < 0 || row >= rows.Length) return;
            int start = row, end = row;
            while (start > 0 && rows[start].Kind != StatRowKind.Heading) start--;
            end = start + 1;
            while (end < rows.Length && rows[end].Kind != StatRowKind.Heading) end++;
            if (rows[row].Kind == StatRowKind.Heading)
            {
                for (int i = row + 1; i < end; i++) if (rows[i].IsTickable) t[i] = !t[i];
                return;
            }
            if (!rows[row].IsTickable) return;
            if (solo)
            {
                for (int i = start; i < end; i++) if (rows[i].IsTickable) t[i] = false;
                t[row] = true;
            }
            else t[row] = !t[row];
        }
    }
}
