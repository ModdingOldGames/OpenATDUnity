using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Staff;

namespace OpenAT.Sim.Finance
{
    /// <summary>A strike started (sim-core.md §6.1): fax + letter <c>Lett 508</c> with the hours, headline queued.</summary>
    public sealed class StrikeStarted : ISimEvent
    {
        public readonly Player Player;
        public readonly int Hours;
        public StrikeStarted(Player p, int hours) { Player = p; Hours = hours; }
    }

    /// <summary>A strike ended; <see cref="EndType"/> 1 threat, 2 raise, 3 waited / drunk (personnel call 1000 + type).</summary>
    public sealed class StrikeEnded : ISimEvent
    {
        public readonly Player Player;
        public readonly int EndType;
        public StrikeEnded(Player p, int endType) { Player = p; EndType = endType; }
    }

    /// <summary>Random flavour letter (sim-core.md §2.7 step 20): texts <c>Lett 2000+10c .. 2002+10c</c>.</summary>
    public sealed class FlavourLetter : ISimEvent
    {
        public readonly Player Player;
        public readonly int Index;
        public FlavourLetter(Player p, int c) { Player = p; Index = c; }
        public int TextBase => 2000 + 10 * Index;
    }

    /// <summary>
    /// The finance domain's systems. Orders = step × 100 of sim-core.md §2.6 (midnight, OnNewDay),
    /// §2.7 (per airline, OnPlayerNewDay) and §2.4 (minute/hour lists).
    /// </summary>
    public static class FinanceSystems
    {
        public static IEnumerable<ISimSystem> Create()
        {
            // per airline (§2.7)
            yield return new DailyResetSystem();
            yield return new AiImageBonusSystem();
            yield return new AiBonusSystem();
            yield return new StatisticsRollSystem();
            yield return new MoneyAndShareHistorySystem();
            yield return new InterestAndSecuritySystem();
            yield return new RentSystem();
            yield return new SalarySystem();
            yield return new DividendSystem();
            yield return new FlavourLetterSystem();
            yield return new MaintenanceSystem();
            yield return new WealthTaxSystem();
            // midnight (§2.6)
            yield return new KerosenePriceSystem();
            yield return new ComparisonHeadlineSystem();
            yield return new NewspaperSystem();
            yield return new LabourMarketSystem();
            yield return new BalanceSheetRolloverSystem();
            // minute / hour (§2.4)
            yield return new StrikeCountdownSystem();
            yield return new StrikeHourSystem();
            yield return new SharePriceSystem();
            // evening / morning
            yield return new EveningStatisticsSystem();
            yield return new MorningReviewSystem();
        }
    }

    // ====================================================================== per airline (§2.7)

    /// <summary>§2.7 step 3: DaysWithoutStrike +1, DaysWithoutSabotage +1, per-night flags.</summary>
    public sealed class DailyResetSystem : SimSystem
    {
        public override string Name => "finance.daily-reset";
        public override int Order => 300;
        public override void OnPlayerNewDay(World w, Player p)
        {
            p.DaysWithoutStrike++;
            p.Counters.DaysWithoutSabotage++;
            p.MaintenanceBookedToday = false;
        }
    }

    /// <summary>§2.7 step 4: computers in IMAGEBONUS missions (add-on 6) get image + Date % 3.</summary>
    public sealed class AiImageBonusSystem : SimSystem
    {
        public override string Name => "finance.ai-image-bonus";
        public override int Order => 400;
        public override void OnPlayerNewDay(World w, Player p)
        {
            if (p.IsComputer && MissionTargets.ImageBonus(w.Difficulty)) p.ChangeImage(w.Clock.Date % 3);
        }
    }

    /// <summary>
    /// §2.7 step 5 (economy.md §18, sim-core.md §3.3): a computer whose hidden bonus is 0 gets +100 000
    /// for each of: human rating &gt; own, ·2/3 &gt; own, /2 &gt; own (inverted for add-on 1); then ×2/×4/×8.
    /// </summary>
    public sealed class AiBonusSystem : SimSystem
    {
        public override string Name => "finance.ai-bonus";
        public override int Order => 500;
        public override void OnPlayerNewDay(World w, Player p)
        {
            if (!p.IsComputer || p.Bonus != 0) return;
            var rule = w.Rules.Get<IMissionRule>();
            long neg = MissionTargets.LowerIsBetter(w.Difficulty) ? -1 : 1;
            long human = rule.Rating(w, w.Players[w.LocalPlayer]);
            long own = rule.Rating(w, p) * neg;
            if (human * neg > own) p.Bonus += 100000;
            if (human * neg * 2 / 3 > own) p.Bonus += 100000;
            if (human * neg / 2 > own) p.Bonus += 100000;
            p.Bonus *= MissionTargets.BonusMultiplier(w.Difficulty);
        }
    }

    /// <summary>§2.7 step 8: statistics month roll (Date % 30 == 0) and day roll.</summary>
    public sealed class StatisticsRollSystem : SimSystem
    {
        public override string Name => "finance.statistics-roll";
        public override int Order => 800;
        public override void OnPlayerNewDay(World w, Player p) => p.Statistics.Roll(w.Clock.Date, w.Options.FixStatisticsDayShift);
    }

    /// <summary>§2.7 step 12: money history, share price history, company-value penalty, MaxAktien ×1.05.</summary>
    public sealed class MoneyAndShareHistorySystem : SimSystem
    {
        public override string Name => "finance.money-share-history";
        public override int Order => 1200;
        public override void OnPlayerNewDay(World w, Player p)
        {
            System.Array.Copy(p.MoneyPast, 1, p.MoneyPast, 0, p.MoneyPast.Length - 1);
            p.MoneyPast[p.MoneyPast.Length - 1] = p.Money;
            for (int c = 9; c >= 1; c--) p.SharePrices[c] = p.SharePrices[c - 1];
            w.Rules.Get<ISharePriceRule>().OvernightPenalty(w, p);
            p.MaxShares = System.Math.Min(p.MaxShares * 105 / 100, Player.MaxSharesCap);
        }
    }

    /// <summary>§2.7 step 14: overnight interest (QUIRK, economy.md §5.3) and security fees (§14).</summary>
    public sealed class InterestAndSecuritySystem : SimSystem
    {
        public override string Name => "finance.interest-security";
        public override int Order => 1400;
        public override void OnPlayerNewDay(World w, Player p)
        {
            w.Rules.Get<IInterestRule>().BookOvernightInterest(w, p);
            p.BookSecurityCosts();
        }
    }

    /// <summary>§2.7 step 15: rents (economy.md §13).</summary>
    public sealed class RentSystem : SimSystem
    {
        public override string Name => "finance.rents";
        public override int Order => 1500;
        public override void OnPlayerNewDay(World w, Player p) => p.BookRents();
    }

    /// <summary>§2.7 step 16: salaries, humans only (economy.md §9.5).</summary>
    public sealed class SalarySystem : SimSystem
    {
        public override string Name => "finance.salaries";
        public override int Order => 1600;
        public override void OnPlayerNewDay(World w, Player p) => p.BookSalaries();
    }

    /// <summary>§2.7 step 19: dividends paid/received, then investor trust (economy.md §6.3, §6.7).</summary>
    public sealed class DividendSystem : SimSystem
    {
        public override string Name => "finance.dividends";
        public override int Order => 1900;
        public override void OnPlayerNewDay(World w, Player p)
        {
            p.BookDividends();
            w.Rules.Get<ISharePriceRule>().UpdateTrust(w, p);
        }
    }

    /// <summary>
    /// §2.7 step 20: repeat { c = rand() % 80; stop if c &gt; 16; letter c to humans }. The original's
    /// unseeded C rand() is replaced by <see cref="World.FlavourRandom"/> (deterministic).
    /// </summary>
    public sealed class FlavourLetterSystem : SimSystem
    {
        public override string Name => "finance.flavour-letters";
        public override int Order => 2000;
        public override void OnPlayerNewDay(World w, Player p)
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                int c = w.FlavourRandom.Rand() % 80;
                if (c > 16) break;
                if (p.Owner == Owner.Human && !p.IsOut) w.Events.Publish(new FlavourLetter(p, c));
            }
        }
    }

    /// <summary>
    /// §2.7 step 21 (finance part, after the operations domain's plane upkeep at 2100): ATFS 10 day
    /// events (day 18 share price ×0.2, day 55 image −200, days 20/45 humans' staff −45 happiness)
    /// and the maintenance booking fallback (fees only) when operations did not book it.
    /// </summary>
    public sealed class MaintenanceSystem : SimSystem
    {
        public override string Name => "finance.maintenance";
        public override int Order => 2150;
        public override void OnPlayerNewDay(World w, Player p)
        {
            int date = w.Clock.Date;
            if (w.Difficulty == 50)
            {
                if (date == 18) p.ShockSharePrice(0.20);
                if (date == 55) p.ChangeImage(-200);
                if (!p.IsComputer && (date == 20 || date == 45)) w.Workers.AddHappiness(p.Index, -45);
            }
            if (!p.MaintenanceBookedToday) p.BookMaintenance(0, w.OperationsBridge.Planes(w, p).Count);
        }
    }

    /// <summary>§2.7 step 23: wealth tax (3600).</summary>
    public sealed class WealthTaxSystem : SimSystem
    {
        public override string Name => "finance.wealth-tax";
        public override int Order => 2300;
        public override void OnPlayerNewDay(World w, Player p)
        {
            long tax = p.WealthTax();
            if (tax != 0) p.Book(-tax, MoneyReason.WealthTax);
        }
    }

    // ====================================================================== midnight (§2.6)

    /// <summary>§2.6 step 17: kerosene price (seed = the new Date).</summary>
    public sealed class KerosenePriceSystem : SimSystem
    {
        public override string Name => "finance.kerosene-price";
        public override int Order => 1700;
        public override void OnNewDay(World w) => w.UpdateKerosenePrice();
    }

    /// <summary>§2.6 step 21: comparison and rocket headlines.</summary>
    public sealed class ComparisonHeadlineSystem : SimSystem
    {
        public override string Name => "finance.comparison-headlines";
        public override int Order => 2100;
        public override void OnNewDay(World w) => w.Newspapers.Comparison(w);
    }

    /// <summary>§2.6 step 23: newspapers (filler/chain, tokens, overrides, priority sort).</summary>
    public sealed class NewspaperSystem : SimSystem
    {
        public override string Name => "finance.newspapers";
        public override int Order => 2300;
        public override void OnNewDay(World w)
        {
            var n = w.Newspapers;
            n.Reload(w, Data.FinanceData.Headlines(w.Data));
            n.Interpolate(w);
            n.SortByPriority();
            w.Events.Publish(new NewspapersUpdated(n.IsInteresting));
        }
    }

    /// <summary>§2.6 step 27: labour market (economy.md §9.2).</summary>
    public sealed class LabourMarketSystem : SimSystem
    {
        public override string Name => "finance.labour-market";
        public override int Order => 2700;
        public override void OnNewDay(World w) => w.Workers.NewDay(w);
    }

    /// <summary>§2.6 step 30: statement separator line, yesterday's sheet = today's, today's cleared.</summary>
    public sealed class BalanceSheetRolloverSystem : SimSystem
    {
        public override string Name => "finance.balance-rollover";
        public override int Order => 3000;
        public override void OnNewDay(World w)
        {
            foreach (var p in w.Players)
            {
                p.AddStatementLine(new StatementLine { Separator = true });
                p.Yesterday = p.Today.Clone();
                p.Today.Clear();
            }
        }
    }

    // ====================================================================== minute / hour (§2.4)

    /// <summary>§2.4 minute step 7: negotiated strike end countdown (game minutes).</summary>
    public sealed class StrikeCountdownSystem : SimSystem
    {
        public override string Name => "finance.strike-countdown";
        public override int Order => 700;
        public override void OnMinute(World w)
        {
            foreach (var p in w.Players)
            {
                if (p.IsOut || p.StrikeHours == 0 || p.StrikeEndCountdown == 0) continue;
                if (--p.StrikeEndCountdown == 0)
                {
                    p.StrikeHours = 0;
                    if (p.Owner == Owner.Human) p.StrikeNotified = false;
                    w.Events.Publish(new StrikeEnded(p, p.StrikeEndType));
                }
            }
        }
    }

    /// <summary>
    /// §2.4 minute step 8, on hour change (sim-core.md §6.1, economy.md §9.7): strike planning at
    /// 10:00 for the local human, strike start 07:00–17:00 when a plane stands or lands within 2 h,
    /// otherwise the running strike's hour countdown (waited out: −10 happiness).
    /// </summary>
    public sealed class StrikeHourSystem : SimSystem
    {
        public override string Name => "finance.strike-hour";
        public override int Order => 800;

        public override void OnMinute(World w)
        {
            if (w.Clock.Minute != 0) return;
            int hour = w.Clock.Hour, date = w.Clock.Date;
            var pool = w.Workers;
            int local = w.LocalPlayer;

            if (hour == 10)
            {
                var lp = w.Players[local];
                if (!lp.IsOut && lp.StrikeHours == 0 && !lp.StrikePlanned)
                {
                    int mood = pool.Mood(local);
                    if ((mood < 20 && lp.DaysWithoutStrike > 7) || (mood < 0 && lp.DaysWithoutStrike > 3)) lp.StrikePlanned = true;
                }
            }

            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                if (p.StrikePlanned && hour > 6 && hour < 18 && !w.CallItADay)
                {
                    bool any = false;
                    foreach (var pl in w.OperationsBridge.Planes(w, p))
                    {
                        if (pl.StandingAtCity) any = true;
                        if (pl.NextLandingHour >= 0 && pl.NextLandingDate == date &&
                            (pl.NextLandingHour == hour || pl.NextLandingHour == hour + 1 || pl.NextLandingHour == hour + 2)) any = true;
                    }
                    if (!any) continue;
                    p.StrikePlanned = false;
                    p.StrikeEndCountdown = 0;
                    p.StrikeNotified = false;
                    var r = new TeakRand(date + hour);
                    if (p.DaysWithoutStrike > 10) p.StrikeHours = 2;
                    else if (pool.AverageHappiness(local) < -10) p.StrikeHours = 72;
                    else p.StrikeHours = 4 + r.Rand(20);
                    p.DaysWithoutStrike = 0;
                    w.Newspapers.AddOverride(1, 2090, "STREIK", 0, 25 + (p.Index == local ? 10 : 0), p.Airline);
                    w.Events.Publish(new StrikeStarted(p, p.StrikeHours));
                }
                else if (p.StrikeHours > 0)
                {
                    p.StrikeHours--;
                    if (p.StrikeHours == 0 && p.Owner == Owner.Human)
                    {
                        p.StrikeNotified = false;
                        p.StrikeEndType = 3;
                        pool.AddHappiness(p.Index, -10);
                        w.Events.Publish(new StrikeEnded(p, 3));
                    }
                }
            }
        }
    }

    /// <summary>
    /// §2.4 hour step 1 (economy.md §6.2): share price drift of every airline (including out ones);
    /// the noise generator is seeded Date + minute + hour and recreated per airline.
    /// </summary>
    public sealed class SharePriceSystem : SimSystem
    {
        public override string Name => "finance.share-prices";
        public override int Order => 100;
        public override void OnHour(World w)
        {
            var rule = w.Rules.Get<ISharePriceRule>();
            foreach (var p in w.Players)
            {
                var r = new TeakRand(w.Clock.Date + w.Clock.Minute + w.Clock.Hour);
                rule.Hourly(w, p, r.Rand(1000));
            }
        }
    }

    // ====================================================================== evening

    /// <summary>Evening screen opens (sim-core.md §1.3): statistics refresh for every airline.</summary>
    public sealed class EveningStatisticsSystem : SimSystem
    {
        public override string Name => "finance.evening-statistics";
        public override int Order => 100;
        public override void OnEvening(World w) => w.UpdateStatistics();
    }
}
