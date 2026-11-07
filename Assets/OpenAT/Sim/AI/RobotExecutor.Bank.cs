using System;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.AI
{
    public static partial class RobotExecutor
    {
        /// <summary>Share-stake target of MAX20PERCENT and the price it applies from (ai-robots.md §5.2 "Bank" step 4).</summary>
        public const int OwnStakeTargetPercent = 20, OwnStakeMinPrice = 200;

        /// <summary>
        /// Bank visit (ai-robots.md §5.2, economy.md §18). Dividend through the human command; loans and
        /// share trades are the robot's direct edits (<see cref="RobotShortcuts"/>, no fees, no limits, Q8/Q10).
        /// </summary>
        static void Bank(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            int slot = c.Slot, date = w.Clock.Date;

            // 1. Dividend.
            if (slot == 1 || c.Has(RobotFeature.HighSharePrice)) w.Execute(new SetDividendCommand(p.Index, 25));
            else if (c.Rnd.Rand(10) == 0)
            {
                int d = p.Dividend;
                if (c.Rnd.Rand(5) == 0) d++;
                if (c.Rnd.Rand(10) == 0) d++;
                w.Execute(new SetDividendCommand(p.Index, Math.Max(5, Math.Min(25, d))));
            }

            bool payback = c.Has(RobotFeature.PaybackCredit), maxCredit = c.Has(RobotFeature.MaxCredit);

            // 2. Pay back the starting credit (add-on 1).
            if (payback && p.Money > 750000 && date > 1 && !maxCredit)
                RobotShortcuts.RepayCredit(p, Math.Min(int.MaxValue, Math.Min(p.Credit, p.Money - 250000)));

            // 3. Credit (no credit-limit check, Q8).
            long target = 1000000 + 50000L * date;
            if ((slot == 1 || maxCredit) && p.Credit < target && !payback)
                RobotShortcuts.Borrow(p, Math.Min(int.MaxValue, target - p.Credit));
            else if (p.Money > 1500000 && p.Credit > 0 && slot != 1 && !maxCredit)
                RobotShortcuts.RepayCredit(p, Math.Min(int.MaxValue, Math.Min(p.Credit, p.Money - 1500000)));
            else if (p.Money < 1000000 && slot != 1 && !payback)
                RobotShortcuts.Borrow(p, Math.Min(int.MaxValue, 1400000 - p.Money));

            // 4. Keep the own stake at 20 % (proceeds capped at 20 000, Q10).
            if (c.Has(RobotFeature.Max20Percent) && p.SharesIssued > 0 && p.SharesOwned[p.Index] * 100 / p.SharesIssued > OwnStakeTargetPercent && p.SharePrice >= OwnStakeMinPrice)
            {
                long sells = p.SharesOwned[p.Index] - p.SharesIssued * OwnStakeTargetPercent / 100;
                if (sells > 0) RobotShortcuts.SellOwnStake(p, sells, 20000);
            }

            // 5. Fire sale at the robot's own price (Q10).
            if ((p.Credit > 1000000 && slot == 2 && c.Has(RobotFeature.SellShares)) || p.Credit > 3000000)
                if (slot != 1 || p.Money < -100000)
                    RobotShortcuts.FireSale(w, p);

            // 6. Buy the disliked rival's free float.
            bool cheap = p.SharePrice < p.Dividend / 2;
            if (!s.SavingForPlane && !s.SavingForRocket && !c.Has(RobotFeature.DontBuyAnyShares)
                && ((p.Money > 3000000 && p.Credit < 1000000) || (p.Money > 2000000 && cheap)) && c.Dislike >= 0)
            {
                long n = RobotShortcuts.BuyFreeFloat(w, p, w.Players[c.Dislike], cheap);
                if (n > 0) c.Log($"buys {n} shares of P{c.Dislike}");
            }

            // 7. Buy back own shares (practically dead: MAX20PERCENT is on almost everywhere).
            if (!payback && !c.Has(RobotFeature.DontBuyAnyShares) && !c.Has(RobotFeature.Max20Percent))
            {
                bool lowDebt = p.Credit < 1000000 || slot == 1;
                if ((p.Money > 3000000 && lowDebt && c.Has(RobotFeature.BuyOwnShares)) || (p.Money > 6000000 && lowDebt) || (p.Money > 2000000 && cheap))
                    RobotShortcuts.BuyFreeFloat(w, p, p, cheap);
            }

            s.WorkCountdown = c.Work(RobotAction.Bank);
        }
    }
}
