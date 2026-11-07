using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Finance
{
    /// <summary>Credit limit for new loans (economy.md §5.1).</summary>
    public interface ICreditLimitRule : IRule
    {
        long CreditLimit(World w, Player p);
    }

    /// <summary>
    /// economy.md §5.1: <c>cr = (Money − Credit)/2 − Credit</c>; if Credit &lt; 200 000 then
    /// <c>cr = max(cr, 200 000 − cr)</c> (QUIRK: uses cr; <see cref="GameOptions.FixCreditLimit"/> uses
    /// Credit); clamped to 0..2³¹−1.
    /// </summary>
    public sealed class CreditLimitRule : ICreditLimitRule
    {
        public long CreditLimit(World w, Player p) => Original(p, w.Options.FixCreditLimit);

        public static long Original(Player p, bool fix)
        {
            long cr = (p.Money - p.Credit) / 2 - p.Credit;
            if (p.Credit < 200000) cr = Math.Max(cr, 200000 - (fix ? p.Credit : cr));
            return Math.Min(int.MaxValue, Math.Max(0, cr));
        }
    }

    /// <summary>Overnight interest of one airline (economy.md §5.3, sim-core.md §2.7 step 14).</summary>
    public interface IInterestRule : IRule
    {
        void BookOvernightInterest(World w, Player p);
    }

    /// <summary>
    /// economy.md §5.3, in order, each on the updated balance:
    /// 1. Money &gt; 0: + Money·HabenZins/100/365 (2001, stat other income).
    /// 2. Money &lt; 0: + Money·HabenZins/100/365 (2002; QUIRK: deposit rate; fix: loan rate).
    /// 3. Credit &gt; 0: + (−Money·SollZins/100/365) (2000; QUIRK: on cash; fix <see cref="GameOptions.FixLoanInterest"/>:
    ///    −Credit·SollZins/100/365).
    /// </summary>
    public sealed class InterestRule : IInterestRule
    {
        public void BookOvernightInterest(World w, Player p)
        {
            var s = p.Statistics;
            if (p.Money > 0)
            {
                long a = p.Money * p.SavingsRate / 100 / 365;
                p.Book(a, MoneyReason.DepositInterest);
                s.Add(StatId.IncomeOther, a);
            }
            if (p.Money < 0)
            {
                int rate = w.Options.FixOverdraftInterest ? p.LoanRate : p.SavingsRate;
                long a = p.Money * rate / 100 / 365;
                p.Book(a, MoneyReason.OverdraftInterest);
                s.Add(StatId.ExpenseOther, a);
            }
            if (p.Credit > 0)
            {
                long a = w.Options.FixLoanInterest ? -p.Credit * p.LoanRate / 100 / 365 : -p.Money * p.LoanRate / 100 / 365;
                p.Book(a, MoneyReason.LoanInterest);
                s.Add(StatId.ExpenseOther, a);
            }
        }
    }

    /// <summary>Share price processes (economy.md §6.2–6.3).</summary>
    public interface ISharePriceRule : IRule
    {
        /// <summary>Hourly drift of airline <paramref name="p"/>; <paramref name="noise"/> = Rand(1000) of the hour's generator.</summary>
        void Hourly(World w, Player p, int noise);
        /// <summary>Overnight company-value penalty on today's price.</summary>
        void OvernightPenalty(World w, Player p);
        /// <summary>Investor-trust update after dividends (S = today's balance-sheet result).</summary>
        void UpdateTrust(World w, Player p);
    }

    /// <summary>The original share price model (economy.md §6.2, §6.3, sim-core.md §6.6).</summary>
    public sealed class SharePriceRule : ISharePriceRule
    {
        public void Hourly(World w, Player p, int noise)
        {
            if (p.TrustedDividend > p.Dividend) p.TrustedDividend = (p.TrustedDividend + p.Dividend) / 2;
            int date = w.Clock.Date, hour = w.Clock.Hour, c = p.Index;
            double k = p.SharePrice;
            k = (k * 29 + 10 * p.TrustedDividend) / 30.0;
            k += Math.Sin((date * 24 + hour) / 50.0) * k / 180.0;
            k += Math.Sin((date * 24 + 100 + hour) / (c + 30.0)) * k / 150.0;
            k += Math.Sin(noise) * k / 50.0;
            if (!(k >= 0) || double.IsInfinity(k)) k = 0;   // also NaN / infinity, which would stick forever
            p.SharePrice = k;
        }

        public void OvernightPenalty(World w, Player p)
        {
            var v = p.Statistics[StatId.CompanyValue];
            long v1 = v[1], v2 = v[2];
            if (v1 < v2) p.SharePrice *= 0.97;
            if (v1 < v2 - 1000000) p.SharePrice *= 0.97;
            if (v1 < v2 - 5000000) p.SharePrice *= 0.95;
        }

        public void UpdateTrust(World w, Player p)
        {
            long s = p.Today.Total;
            bool odd = (w.Clock.Date & 1) != 0;
            if (s > 0 && odd && p.TrustedDividend < p.Dividend) p.TrustedDividend++;
            if (s < 0)
            {
                if (p.TrustedDividend < p.Dividend / 2) { if (odd) p.TrustedDividend++; }
                else p.TrustedDividend--;
            }
        }
    }

    /// <summary>Kerosene market price (economy.md §12.1, sim-core.md §6.5).</summary>
    public interface IKerosenePriceRule : IRule
    {
        /// <summary>Today's new price from yesterday's price and the shifted history; seed = Date.</summary>
        int NextPrice(World w, int price);
    }

    /// <summary>
    /// economy.md §12.1. Rand draws are evaluated left to right (Q-9): Rand(21), Rand(20), Rand(41), Rand(6).
    /// Fuel-crisis missions (ATFS 9; ATFS 10 days 3–10, 35–55) add the crisis jump and 20 noise steps.
    /// </summary>
    public sealed class KerosenePriceRule : IKerosenePriceRule
    {
        public const int Min = 300, Max = 700;

        static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        static int Noise(TeakRand r)
        {
            int a = r.Rand(21) - 10;
            int b = r.Rand(20) < 3 ? 1 : 0;
            int c = r.Rand(41) - 20;
            return a + b * c;
        }

        public static bool Crisis(int difficulty, int date) =>
            difficulty == 49 || (difficulty == 50 && ((date >= 3 && date <= 10) || (date >= 35 && date <= 55)));

        public int NextPrice(World w, int price)
        {
            // Range from the tunables (Sim/Modding; default 300..700 = Min/Max).
            int min = w.Tunables?.Int(Modding.TunableKeys.KeroseneMin, Min) ?? Min, max = w.Tunables?.Int(Modding.TunableKeys.KeroseneMax, Max) ?? Max;
            int date = w.Clock.Date;
            var r = new TeakRand(date);
            int noise = Noise(r);
            int r6 = r.Rand(6);
            price += noise + (int)(Math.Sin((date + r6) / 3.0) * 20) + (int)(Math.Sin(date * 1.7) * 20);
            if (Crisis(w.Difficulty, date))
            {
                var past = w.KerosenePast;
                if (r.Rand(20) > 2) price += price / 4;
                else if (past[8] > 550 && past[7] > 550 && past[6] > 550) price -= price / 2;
                price = Clamp(price, min, max);
                for (int c = 0; c < 20; c++) price += Noise(r);
            }
            return Clamp(price, min, max);
        }
    }

    /// <summary>Company value (economy.md §7).</summary>
    public interface ICompanyValueRule : IRule
    {
        long CompanyValue(World w, Player p);
    }

    /// <summary>Σ plane values + Σ holdings·price (truncated per airline) + Money − Credit.</summary>
    public sealed class CompanyValueRule : ICompanyValueRule
    {
        public long CompanyValue(World w, Player p)
        {
            long value = 0;
            foreach (var pl in w.OperationsBridge.Planes(w, p)) value += pl.Value;
            for (int c = 0; c < Player.Count; c++)
                if (p.SharesOwned[c] != 0) value += (long)(p.SharesOwned[c] * w.Players[c].SharePrice);
            value += p.Money - p.Credit;
            return value;
        }
    }
}
