using OpenAT.Sim.Finance;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>Stock market state and trading of one airline (economy.md §6).</summary>
    public sealed partial class Player
    {
        public const long MaxSharesCap = 250000000;
        public const int MaxDividend = 25;

        /// <summary>Shares issued ("AnzAktien") and the issue ceiling ("MaxAktien").</summary>
        public long SharesIssued = 10000, MaxShares = 20000;
        /// <summary>Holdings of each airline's shares ("OwnsAktien") and their cost basis ("AktienWert").</summary>
        public readonly long[] SharesOwned = new long[Count];
        public readonly long[] ShareCostBasis = new long[Count];
        /// <summary>Price history ("Kurse"): [0] current, [1..9] previous closes.</summary>
        public readonly double[] SharePrices = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 };
        /// <summary>Dividend per share per year (0..25) and the lagged "investor trust".</summary>
        public int Dividend = 10, TrustedDividend = 10;

        public double SharePrice { get => SharePrices[0]; set => SharePrices[0] = value; }

        /// <summary>Applies a price factor (sabotage, incidents, scripted events; economy.md §6.4), floor 0.</summary>
        public void ShockSharePrice(double factor)
        {
            SharePrice *= factor;
            if (SharePrice < 0) SharePrice = 0;
        }

        /// <summary>Changes investor trust; clamped at <paramref name="floor"/> (sabotage clamps at 0).</summary>
        public void ChangeTrust(int delta, int floor = 0)
        {
            TrustedDividend += delta;
            if (TrustedDividend < floor) TrustedDividend = floor;
        }

        /// <summary>
        /// Buys shares of <paramref name="target"/> at the bank (economy.md §6.5): chunks of ≤ 1000, each
        /// raising the price by Anz/(Anz − m); pays total + 10 % + 100 (3150). Returns the amount paid.
        /// The caller has validated the count and the DEBT_LIMIT check.
        /// </summary>
        public long BuyShares(Player target, long count)
        {
            long price = 0;
            int t = target.Index;
            while (count > 0)
            {
                long m = System.Math.Min(1000, count);
                SharesOwned[t] += m;
                price += (long)(m * target.SharePrice);
                // Rise by Anz/(Anz − m); a chunk as large as the whole issue would divide by zero (price stays then).
                if (target.SharesIssued - m > 0) target.SharePrice = target.SharePrice * target.SharesIssued / (target.SharesIssued - m);
                if (!(target.SharePrice >= 0)) target.SharePrice = 0;
                count -= m;
            }
            if (price != 0)
            {
                price = price + price / 10 + 100;
                Book(-price, MoneyReason.SharesBought);
                ShareCostBasis[t] += price;
                Statistics.Add(StatId.ExpenseOther, -price);
            }
            return price;
        }

        /// <summary>
        /// Sells shares (economy.md §6.5): cost basis scaled first, then chunks of ≤ 1000 lowering the price
        /// by (Anz − m)/Anz; receives proceeds − 10 % + 100 (QUIRK; fix: − 100) (3151). Returns the amount received.
        /// </summary>
        public long SellShares(Player target, long count)
        {
            int t = target.Index;
            long price = 0;
            if (count > 0 && SharesOwned[t] > 0)
                ShareCostBasis[t] = ShareCostBasis[t] * (SharesOwned[t] - count) / SharesOwned[t];
            while (count > 0)
            {
                long m = System.Math.Min(1000, count);
                SharesOwned[t] -= m;
                price += (long)(m * target.SharePrice);
                if (target.SharesIssued > 0) target.SharePrice = target.SharePrice * (target.SharesIssued - m) / target.SharesIssued;
                if (!(target.SharePrice >= 0)) target.SharePrice = 0;
                count -= m;
            }
            if (price != 0)
            {
                bool fix = World != null && World.Options.FixShareSaleFee;
                price = price - price / 10 + (fix ? -100 : 100);
                Book(price, MoneyReason.SharesSold);
                Statistics.Add(StatId.IncomeOther, price);
            }
            return price;
        }

        /// <summary>Shares that may be issued now: (MaxAktien − AnzAktien) rounded down to 100 (economy.md §6.6).</summary>
        public long IssuableShares => (MaxShares - SharesIssued) / 100 * 100;

        /// <summary>
        /// Number of new shares for a percentage choice (10/25/50/75/100). The original offers
        /// pct % of <see cref="IssuableShares"/> (rounded to 100) but books pct % of the unrounded
        /// room (QUIRK); with <see cref="GameOptions.FixShareIssueRounding"/> the offered count is booked.
        /// </summary>
        public long SharesForPercent(int percent) =>
            World != null && World.Options.FixShareIssueRounding
                ? percent * IssuableShares / 100
                : percent * (MaxShares - SharesIssued) / 100;

        /// <summary>Market takes all (discount 5), 80 % (3) or 60 % (1) of the new shares.</summary>
        public static long MarketTakes(long n, int discount) => discount == 5 ? n : discount == 3 ? n * 8 / 10 : n * 6 / 10;

        /// <summary>Bank fee: 10 % of the nominal value of all new shares, rounded down to 100.</summary>
        public static long IssueFee(long n, long issuePrice) => n * issuePrice / 10 / 100 * 100;

        /// <summary>
        /// Issues <paramref name="n"/> new shares at ⌊K⌋ − <paramref name="discount"/> (economy.md §6.6):
        /// proceeds (3162), fee (3160), new price, dilution compensation (3161), holdings.
        /// </summary>
        public void IssueShares(long n, int discount)
        {
            var w = World;
            long oldPrice = (long)SharePrice;
            long e = (long)(SharePrice - discount);
            long m = MarketTakes(n, discount);
            long fee = IssueFee(n, e);
            Book(m * e, MoneyReason.ShareIssueProceeds);
            Book(-fee, MoneyReason.ShareIssueFee);
            Statistics.Add(StatId.IncomeOther, m * e - fee);

            SharePrice = (SharePrice * SharesIssued + (double)e * m) / (SharesIssued + m);
            if (SharePrice < 0) SharePrice = 0;

            // Dilution compensation: outside shares including the free float (QUIRK 7).
            long outside = SharesIssued - SharesOwned[Index];
            if (w != null && w.Options.FixDilutionFreeFloat)
            {
                outside = 0;
                foreach (var o in w.Players) if (o != this) outside += o.SharesOwned[Index];
            }
            Book(-(long)(outside * (oldPrice - SharePrice)), MoneyReason.ShareIssueDilution);
            if (w != null)
                foreach (var o in w.Players)
                    if (o != this)
                    {
                        long comp = (long)(o.SharesOwned[Index] * (oldPrice - SharePrice));
                        o.Book(comp, MoneyReason.ShareIssueDilution);
                        o.Statistics.Add(StatId.IncomeOther, comp);
                    }

            SharesIssued += n;
            SharesOwned[Index] += n - m;
        }

        /// <summary>
        /// Overnight dividends (economy.md §6.7): pays (Anz − own)·Div/365 (3140), receives
        /// Σ holdings·Div/365 of other active airlines (3141). QUIRK: the statistics get the yearly
        /// amount, even when 0 (fix: daily amount).
        /// </summary>
        public void BookDividends()
        {
            bool fix = World != null && World.Options.FixDividendStatistics;
            long paid = (SharesIssued - SharesOwned[Index]) * Dividend;
            if (paid != 0) Book(-paid / 365, MoneyReason.DividendsPaid);
            Statistics.Add(StatId.ExpenseOther, fix ? -paid / 365 : -paid);

            long got = 0;
            if (World != null)
                foreach (var o in World.Players)
                    if (o != this && !o.IsOut) got += SharesOwned[o.Index] * o.Dividend;
            if (got != 0) Book(got / 365, MoneyReason.DividendsReceived);
            Statistics.Add(StatId.IncomeOther, fix ? got / 365 : got);
        }

        /// <summary>Dividend yield shown in the share tooltip: Div·100/K %.</summary>
        public double DividendYield => SharePrice > 0 ? Dividend * 100 / SharePrice : 0;
    }
}
