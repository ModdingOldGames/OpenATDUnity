using System;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Shared fixture: a world without install data, finance setup applied.</summary>
    static class FinanceFixture
    {
        public static World Make(int difficulty = Mission.FreeGame, GameOptions options = null, ListOperationsBridge bridge = null)
        {
            var w = new World(new GameData(), options);
            w.Difficulty = difficulty;
            w.LocalPlayer = 0;
            for (int c = 0; c < Player.Count; c++)
            {
                w.Players[c].Owner = c == 0 ? Owner.Human : Owner.Computer;
                w.Players[c].Airline = "Air" + c;
            }
            if (bridge != null) w.Rules.Set<IOperationsBridge>(bridge);
            w.Clock.InitCalendar(0);
            w.Clock.Date = 0;
            w.Clock.Time = GameClock.DayStart;
            new FinanceSetup().Apply(w, new NewGameSettings { Difficulty = difficulty });
            return w;
        }
    }

    public class FinanceBankTests
    {
        [Fact]
        public void CreditLimit_FormulaAndQuirk()
        {
            var w = FinanceFixture.Make(options: OriginalOptions.Game());
            var p = w.Players[0];
            p.Money = 2000000; p.Credit = 0;
            Assert.Equal(1000000, p.CreditLimit());
            p.Money = 100000;
            Assert.Equal(150000, p.CreditLimit());
            // QUIRK: 200 000 − cr with negative cash gives a large limit.
            p.Money = -2000000;
            Assert.Equal(1200000, p.CreditLimit());
            p.Money = 1000000; p.Credit = 300000;
            Assert.Equal(50000, p.CreditLimit());

            var fixedWorld = FinanceFixture.Make(options: new GameOptions { FixCreditLimit = true });
            var q = fixedWorld.Players[0];
            q.Money = -2000000; q.Credit = 0;
            Assert.Equal(200000, q.CreditLimit());
        }

        [Fact]
        public void Loans_TakeAndRepay()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.Money = 2000000;
            Assert.False(w.Execute(new TakeLoanCommand(0, 1000001)).Ok);
            Assert.False(w.Execute(new TakeLoanCommand(0, 500, false)).Ok);
            Assert.False(w.Execute(new TakeLoanCommand(0, 100000, byPhone: true)).Ok);
            Assert.True(w.Execute(new TakeLoanCommand(0, 600000)).Ok);
            Assert.Equal(600000, p.Credit);
            Assert.Equal(2600000, p.Money);
            Assert.Equal(MoneyReason.LoanTaken, p.Statement[p.Statement.Count - 1].Reason);
            // Repay ignores cash (QUIRK 4).
            p.Money = 0;
            Assert.True(w.Execute(new RepayLoanCommand(0, 600000)).Ok);
            Assert.Equal(0, p.Credit);
            Assert.Equal(-600000, p.Money);
            Assert.False(w.Execute(new RepayLoanCommand(0, 1)).Ok);
        }

        [Fact]
        public void Interest_QuirkChargesLoanRateOnCash()
        {
            var w = FinanceFixture.Make(options: OriginalOptions.Game());
            var p = w.Players[0];
            p.Money = 1000000; p.Credit = 500000; p.SavingsRate = 3; p.LoanRate = 10;
            w.Rules.Get<IInterestRule>().BookOvernightInterest(w, p);
            // +1 000 000·3/100/365 = 82, then −1 000 082·10/100/365 = −273.
            Assert.Equal(1000000 + 82 - 273, p.Money);
            Assert.Equal(82, p.Today.DepositInterest);
            Assert.Equal(273, p.Today.LoanInterest);

            // Negative cash: deposit rate on the overdraft, then loan "interest" becomes income.
            p.Money = -365000; p.Credit = 100000;
            p.Today.Clear();
            w.Rules.Get<IInterestRule>().BookOvernightInterest(w, p);
            Assert.Equal(-365000 - 30 + 100, p.Money);
            Assert.Equal(130, p.Today.LoanInterest); // both bookings counted as expense
        }

        [Fact]
        public void Interest_FixChargesTheLoan()
        {
            var w = FinanceFixture.Make(options: new GameOptions { FixLoanInterest = true });
            var p = w.Players[0];
            p.Money = 1000000; p.Credit = 500000; p.SavingsRate = 3; p.LoanRate = 10;
            w.Rules.Get<IInterestRule>().BookOvernightInterest(w, p);
            Assert.Equal(1000000 + 82 - 136, p.Money);
        }

        [Fact]
        public void BankBin_FirstCollectorGetsMoney()
        {
            var w = FinanceFixture.Make();
            w.MoneyInBankBin = true;
            long before = w.Players[0].Money;
            Assert.True(w.Execute(new CollectBankBinMoneyCommand(0)).Ok);
            Assert.Equal(before + 100000, w.Players[0].Money);
            Assert.False(w.Execute(new CollectBankBinMoneyCommand(1)).Ok);
        }

        [Fact]
        public void Setup_StartValuesPerMission()
        {
            var w = FinanceFixture.Make(Mission.Tutorial, OriginalOptions.Game());
            var h = w.Players[0];
            var ai = w.Players[1];
            Assert.Equal(5000000, h.Money);
            Assert.Equal(5000000, ai.Money);
            Assert.Equal(0, h.Bonus);
            Assert.Equal(4800000, ai.Bonus); // QUIRK |Mc − Mh|
            Assert.Equal(8000, h.SharesOwned[0]);
            Assert.Equal(800000, h.ShareCostBasis[0]);
            Assert.Equal(5000000, h.HistoricMoney);
            Assert.Equal(508, w.KerosenePast[9]);
            Assert.Equal(500, w.Kerosene);

            var addon = FinanceFixture.Make(11);
            Assert.Equal(10000000, addon.Players[0].Credit);
            Assert.Equal(15, addon.Players[0].LoanRate);
            Assert.Equal(300, FinanceFixture.Make(16).Players[2].Image);

            var fixedBonus = FinanceFixture.Make(Mission.Tutorial, new GameOptions { FixComputerBonusAbs = true });
            Assert.Equal(0, fixedBonus.Players[1].Bonus);
        }

        [Fact]
        public void WealthTax_Brackets()
        {
            var p = FinanceFixture.Make().Players[0];
            p.Money = 100000000000000; // 1e14: only the 5e13 bracket is exceeded
            Assert.Equal(5000000000000, p.WealthTax());
            p.Money = 1000;
            Assert.Equal(0, p.WealthTax());
        }

        [Fact]
        public void SecurityFees_FixedAndPerPlane()
        {
            var p = FinanceFixture.Make().Players[0];
            p.SecurityFlags = 0x0001 | 0x0040 | 0x0200; // office, planes inside, bit 9 (free)
            Assert.Equal(25000 + 30000 * 3, p.SecurityCosts(3));
        }

        [Fact]
        public void Rents_DailyShareAndGateAlwaysBooked()
        {
            var bridge = new ListOperationsBridge();
            bridge.RentLists[0].Add(new RentItem(MoneyReason.GateRent, 1000));
            bridge.RentLists[0].Add(new RentItem(MoneyReason.BranchRent, 3100));
            var w = FinanceFixture.Make(bridge: bridge);
            var p = w.Players[0];
            long before = p.Money;
            p.BookRents();
            Assert.Equal(before - 33 - 103, p.Money);
            Assert.Equal(-136, p.Statistics.Today(StatId.ExpenseRents));
            Assert.Equal(33, p.Today.GateRent);
            // gate line + branch line, no route line
            Assert.Equal(2, p.Statement.Count);
        }
    }

    public class FinanceShareTests
    {
        [Fact]
        public void Hourly_PriceStepsMatchFormula()
        {
            var w = FinanceFixture.Make();
            w.Clock.Date = 3;
            w.Clock.Time = GameClock.At(14);
            var p = w.Players[2];
            p.SharePrice = 120;
            p.Dividend = 10;
            p.TrustedDividend = 20; // > dividend → (20+10)/2 = 15
            int noise = new TeakRand(3 + 0 + 14).Rand(1000);
            w.Rules.Get<ISharePriceRule>().Hourly(w, p, noise);

            double k = (120.0 * 29 + 10 * 15) / 30.0;
            k += Math.Sin((3 * 24 + 14) / 50.0) * k / 180.0;
            k += Math.Sin((3 * 24 + 100 + 14) / (2 + 30.0)) * k / 150.0;
            k += Math.Sin(noise) * k / 50.0;
            Assert.Equal(15, p.TrustedDividend);
            Assert.Equal(k, p.SharePrice, 9);
        }

        [Fact]
        public void Hourly_SameNoiseForEveryAirline()
        {
            var w = FinanceFixture.Make();
            w.Clock.Date = 5;
            w.Clock.Time = GameClock.At(11);
            foreach (var p in w.Players) { p.SharePrice = 100; p.TrustedDividend = 10; p.Dividend = 10; }
            new SharePriceSystem().OnHour(w);
            int noise = new TeakRand(5 + 11).Rand(1000);
            for (int c = 0; c < Player.Count; c++)
            {
                double k = 100.0;
                k += Math.Sin((5 * 24 + 11) / 50.0) * k / 180.0;
                k += Math.Sin((5 * 24 + 100 + 11) / (c + 30.0)) * k / 150.0;
                k += Math.Sin(noise) * k / 50.0;
                Assert.Equal(k, w.Players[c].SharePrice, 9);
            }
        }

        [Fact]
        public void Overnight_CompanyValuePenalties()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.SharePrice = 100;
            var v = p.Statistics[StatId.CompanyValue];
            v.Days[1] = 1000000; v.Days[2] = 7000000;
            w.Rules.Get<ISharePriceRule>().OvernightPenalty(w, p);
            Assert.Equal(100 * 0.97 * 0.97 * 0.95, p.SharePrice, 9);
        }

        [Fact]
        public void Trust_RisesSlowlyFallsFast()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            var rule = w.Rules.Get<ISharePriceRule>();
            p.Dividend = 20; p.TrustedDividend = 10;
            p.Today.Clear(); p.Today.DepositInterest = 5; // S > 0
            w.Clock.Date = 2; rule.UpdateTrust(w, p);
            Assert.Equal(10, p.TrustedDividend); // even date: no rise
            w.Clock.Date = 3; rule.UpdateTrust(w, p);
            Assert.Equal(11, p.TrustedDividend);
            p.Today.Clear(); p.Today.Staff = 100; // S < 0, trust ≥ div/2 → −1
            rule.UpdateTrust(w, p);
            Assert.Equal(10, p.TrustedDividend);
            p.TrustedDividend = 5; // below div/2 → +1 on odd dates
            rule.UpdateTrust(w, p);
            Assert.Equal(6, p.TrustedDividend);
        }

        [Fact]
        public void BuyAndSell_ChunksFeesAndQuirk()
        {
            var w = FinanceFixture.Make();
            var buyer = w.Players[1];
            var target = w.Players[0];
            buyer.Money = 1000000;
            Assert.Equal(2000, w.FreeFloat(target));
            Assert.False(w.Execute(new BuySharesCommand(1, 0, 2001)).Ok);
            Assert.True(w.Execute(new BuySharesCommand(1, 0, 1500)).Ok);
            // 1000·100 + 500·111.11 = 155 555; + 10 % + 100.
            Assert.Equal(1000000 - 171210, buyer.Money);
            Assert.Equal(1500, buyer.SharesOwned[0]);
            Assert.Equal(171210, buyer.ShareCostBasis[0]);
            Assert.Equal(100.0 * 10000 / 9000 * 10000 / 9500, target.SharePrice, 9);

            var w2 = FinanceFixture.Make(options: OriginalOptions.Game());
            var seller = w2.Players[0];
            long m = seller.Money;
            Assert.True(w2.Execute(new SellSharesCommand(0, 0, 1000)).Ok);
            // 100 000 − 10 % + 100 (QUIRK: +100).
            Assert.Equal(m + 90100, seller.Money);
            Assert.Equal(700000, seller.ShareCostBasis[0]);
            Assert.Equal(90.0, seller.SharePrice, 9);

            var w3 = FinanceFixture.Make(options: new GameOptions { FixShareSaleFee = true });
            long m3 = w3.Players[0].Money;
            w3.Execute(new SellSharesCommand(0, 0, 1000));
            Assert.Equal(m3 + 89900, w3.Players[0].Money);
        }

        [Fact]
        public void Buy_RefusedPastDebtLimit()
        {
            var w = FinanceFixture.Make();
            w.Players[1].Money = -950000;
            Assert.Equal("debt-limit", w.Execute(new BuySharesCommand(1, 0, 1000)).Reason);
        }

        [Fact]
        public void Issue_ProceedsFeeDilution()
        {
            var w = FinanceFixture.Make(options: OriginalOptions.Game());
            var p = w.Players[0];
            long m = p.Money;
            Assert.True(w.Execute(new IssueSharesCommand(0, 50, 5)).Ok);
            // N = 5000 at 95: +475 000, fee 47 500, dilution 2000·(100 − 98.33) = 3333.
            Assert.Equal(m + 475000 - 47500 - 3333, p.Money);
            // Default (fixed): no dilution compensation for the free float.
            var wf = FinanceFixture.Make();
            long mf = wf.Players[0].Money;
            Assert.True(wf.Execute(new IssueSharesCommand(0, 50, 5)).Ok);
            Assert.Equal(mf + 475000 - 47500, wf.Players[0].Money);
            Assert.Equal(15000, p.SharesIssued);
            Assert.Equal(8000, p.SharesOwned[0]);
            Assert.Equal(1475000.0 / 15000, p.SharePrice, 9);
            // 3 → market takes 80 %, the issuer keeps the rest.
            var w2 = FinanceFixture.Make();
            w2.Execute(new IssueSharesCommand(0, 100, 3));
            Assert.Equal(8000 + 2000, w2.Players[0].SharesOwned[0]);
            // Not enough issuable shares.
            Assert.Equal("too-few-shares", w2.Execute(new IssueSharesCommand(0, 10, 5)).Reason);
        }

        [Fact]
        public void IssueShares_BooksTheOfferedRoundedCount()
        {
            // Room 15 050: the bank offers 25 % of 15 000 (rounded to 100) = 3 750.
            var w = FinanceFixture.Make(options: new GameOptions { FixShareIssueRounding = true });
            var p = w.Players[0];
            p.MaxShares = p.SharesIssued + 15050;
            Assert.Equal(15000, p.IssuableShares);
            Assert.Equal(3750, p.SharesForPercent(25));
            Assert.True(w.Execute(new IssueSharesCommand(0, 25, 5)).Ok);
            Assert.Equal(10000 + 3750, p.SharesIssued);

            // Original quirk: 25 % of the unrounded room (3 762) is booked.
            var w2 = FinanceFixture.Make(options: new GameOptions { FixShareIssueRounding = false });
            var p2 = w2.Players[0];
            p2.MaxShares = p2.SharesIssued + 15050;
            Assert.Equal(3762, p2.SharesForPercent(25));
            w2.Execute(new IssueSharesCommand(0, 25, 5));
            Assert.Equal(10000 + 3762, p2.SharesIssued);
        }

        [Fact]
        public void BuySharesToMajority_MakesTakeoverRequestable()
        {
            // The bank form's path: buy the free float, the rival issues shares, buy past 50 %.
            var w = FinanceFixture.Make();
            var me = w.Players[0];
            var t = w.Players[1];
            me.Money = 50_000_000;
            Assert.True(w.Execute(new BuySharesCommand(0, 1, w.FreeFloat(t))).Ok);
            Assert.Equal("below-half", w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Absorb)).Reason);
            t.SharesIssued += 10000; // a sold issue: new shares on the market
            Assert.True(w.Execute(new BuySharesCommand(0, 1, w.FreeFloat(t))).Ok);
            Assert.True(me.SharesOwned[1] * 2 >= t.SharesIssued);
            Assert.True(w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Absorb)).Ok);
        }

        [Fact]
        public void Dividends_PaidReceivedAndYearlyStatQuirk()
        {
            var w = FinanceFixture.Make(options: OriginalOptions.Game());
            var p0 = w.Players[0];
            var p1 = w.Players[1];
            p1.SharesOwned[0] = 1000;
            long m0 = p0.Money, m1 = p1.Money;
            p0.BookDividends();
            p1.BookDividends();
            Assert.Equal(m0 - 2000 * 10 / 365, p0.Money);
            Assert.Equal(-20000, p0.Statistics.Today(StatId.ExpenseOther));
            Assert.Equal(m1 - 2000 * 10 / 365 + 1000 * 10 / 365, p1.Money);
            Assert.Equal(10000, p1.Statistics.Today(StatId.IncomeOther));

            // Default (fixed): the statistic records the daily amount.
            var f = FinanceFixture.Make();
            f.Players[0].BookDividends();
            Assert.Equal(-(2000 * 10 / 365), f.Players[0].Statistics.Today(StatId.ExpenseOther));
        }

        [Fact]
        public void Takeover_RequestRulesAndLiquidation()
        {
            var w = FinanceFixture.Make();
            var me = w.Players[0];
            var t = w.Players[1];
            Assert.Equal("no-shares", w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Liquidate)).Reason);
            me.SharesOwned[1] = 4999;
            Assert.Equal("below-half", w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Liquidate)).Reason);
            me.SharesOwned[1] = 6000;
            t.SharesOwned[1] = 0;
            w.Players[2].SharesOwned[1] = 2000;
            t.Money = 1000000; t.Credit = 3000000;
            Assert.True(w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Liquidate)).Ok);
            Assert.Equal(TakeoverMode.Liquidate, w.PendingTakeover);

            long m0 = me.Money, m2 = w.Players[2].Money;
            w.ExecuteTakeover();
            // (Money − Credit) = −2 000 000 split 6000:2000 (shareholders inherit the debt).
            Assert.Equal(m0 - 1500000, me.Money);
            Assert.Equal(m2 - 500000, w.Players[2].Money);
            Assert.True(t.IsOut);
            Assert.Equal(0, me.SharesOwned[1]);
            Assert.Equal(TakeoverMode.None, w.PendingTakeover);
        }

        [Fact]
        public void Takeover_AbsorbMovesMoneyAndShares()
        {
            var w = FinanceFixture.Make();
            var me = w.Players[0];
            var t = w.Players[1];
            me.SharesOwned[1] = 6000;
            w.Players[2].SharesOwned[1] = 500;
            t.Money = 700000; t.Credit = 200000;
            long m0 = me.Money;
            w.Execute(new RequestTakeoverCommand(0, 1, TakeoverMode.Absorb));
            w.ExecuteTakeover();
            Assert.Equal(m0 + 700000, me.Money);
            Assert.Equal(200000, me.Credit);
            Assert.Equal(20000, me.SharesIssued);
            Assert.Equal(500, w.Players[2].SharesOwned[0]);
            Assert.Equal(0, w.Players[2].SharesOwned[1]);
            Assert.True(t.IsOut);
        }
    }

    public class FinanceKeroseneTests
    {
        [Fact]
        public void Price_WalkWithFixedSeed()
        {
            var w = FinanceFixture.Make();
            for (int day = 1; day <= 30; day++)
            {
                w.Clock.Date = day;
                int before = w.Kerosene;
                int prev9 = w.KerosenePast[9];
                w.UpdateKerosenePrice();
                var r = new TeakRand(day);
                int a = r.Rand(21) - 10;
                int b = r.Rand(20) < 3 ? 1 : 0;
                int c = r.Rand(41) - 20;
                int r6 = r.Rand(6);
                int expected = before + a + b * c + (int)(Math.Sin((day + r6) / 3.0) * 20) + (int)(Math.Sin(day * 1.7) * 20);
                expected = Math.Max(300, Math.Min(700, expected));
                Assert.Equal(expected, w.Kerosene);
                Assert.Equal(expected, w.KerosenePast[9]);
                Assert.Equal(prev9, w.KerosenePast[8]);
            }
        }

        [Fact]
        public void Price_InitialHistoryBackfill()
        {
            var w = FinanceFixture.Make();
            var r = new TeakRand(0);
            int v = 508;
            for (int c = 8; c >= 0; c--)
            {
                int a = r.Rand(20), b = r.Rand(6);
                v = v + a - 10 - (int)(Math.Sin((0 - c + b) / 3.0) * 20) - (int)(Math.Sin((0 - c) * 1.7) * 20);
                Assert.Equal(v, w.KerosenePast[c]);
            }
        }

        [Fact]
        public void Price_CrisisStaysInRange()
        {
            var w = FinanceFixture.Make(49);
            for (int day = 1; day <= 60; day++)
            {
                w.Clock.Date = day;
                w.UpdateKerosenePrice();
                Assert.InRange(w.Kerosene, 300, 700);
            }
        }

        [Fact]
        public void Tank_BuyRebateAveragePriceAndFlightDraw()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.Money = 20000000;
            p.Tank = 60000;
            Assert.True(w.Execute(new BuyKeroseneCommand(0, KeroseneKind.Normal, 10000)).Ok);
            Assert.Equal(20000000 - 4750000, p.Money); // 5 % rebate at ≥ 10 000
            Assert.Equal(475.0, p.TankPrice, 9);
            Assert.True(w.Execute(new BuyKeroseneCommand(0, KeroseneKind.Cheap, 10000)).Ok); // 250·10 000 − 5 %
            Assert.Equal((10000 * 475.0 + 2375000) / 20000, p.TankPrice, 9);
            Assert.Equal(10000, p.BadKerosene);
            Assert.Equal(KeroseneKind.Cheap, p.KeroseneKind);
            Assert.False(w.Execute(new BuyKeroseneCommand(0, KeroseneKind.Normal, 40001)).Ok); // over capacity

            p.TankOpen = true;
            Assert.Equal(1, w.FuelCost.BadKeroseneFactor(w, p)); // exactly 50 % cheap is not > 50
            var draw = p.DrawTankFuel(300);
            Assert.Equal(300, draw.FromTank);
            Assert.Equal((long)(300 * p.TankPrice), draw.ImputedCost);
            Assert.Equal(19700, p.TankContents);
            Assert.Equal(9700, p.BadKerosene);

            p.TankOpen = false;
            Assert.Equal(3, w.FuelCost.BadKeroseneFactor(w, p)); // closed tank, cheap kind
        }

        [Fact]
        public void Tanks_Purchase()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            long m = p.Money;
            Assert.True(w.Execute(new BuyTankCommand(0, 3, 5)).Ok);
            Assert.Equal(100 + 500, p.Tank);
            Assert.Equal(m - 300000, p.Money);
        }

        [Fact]
        public void Maintenance_FeesPerPlane()
        {
            var p = FinanceFixture.Make().Players[0];
            Assert.Equal(48000L * 3 / 30, p.MechanicFeesPerDay(3));
            p.MechMode = 3;
            Assert.Equal(150000 / 30, p.MechanicCostPerPlanePerDay());
            long m = p.Money;
            p.BookMaintenance(1000, 2);
            Assert.Equal(m - 1000 - 10000, p.Money);
            Assert.Equal(-11000, p.Statistics.Today(StatId.Maintenance));
        }
    }

    public class FinanceAdvertisingTests
    {
        [Fact]
        public void Gains_MatchTheTable()
        {
            int[] company = { 0, 0, 0, 1, 4, 50 };
            int[] route = { 0, 0, 0, 1, 4, 46 };
            int[] allCompany = { 0, 0, 2, 9, 59, 800 };
            int[] allRoute = { 0, 0, 3, 10, 68, 148 };
            for (int t = 0; t < 6; t++)
            {
                Assert.Equal(company[t], Advertising.CompanyImageGain(AdType.Company, t));
                Assert.Equal(route[t], Advertising.RouteImageGain(AdType.Route, t));
                Assert.Equal(allCompany[t], Advertising.CompanyImageGain(AdType.Everything, t));
                Assert.Equal(allRoute[t], Advertising.RouteImageGain(AdType.Everything, t));
            }
            Assert.Equal(60000000, Advertising.PriceOf(AdType.Everything, 5));
        }

        [Fact]
        public void Campaign_ImageCostAndRouteClamp()
        {
            var bridge = new ListOperationsBridge();
            bridge.RouteImages[0][7] = 90;
            var w = FinanceFixture.Make(bridge: bridge);
            var p = w.Players[0];
            p.Money = 100000000;
            Assert.True(w.Execute(new AdCampaignCommand(0, AdType.Everything, 5)).Ok);
            Assert.Equal(800, p.Image);
            Assert.Equal(100, bridge.RouteImages[0][7]);
            Assert.Equal(100000000 - 60000000, p.Money);
            Assert.Equal(MoneyReason.AdCampaignBase + 5, p.Statement[p.Statement.Count - 1].Reason);
            Assert.Equal("route-not-rented", w.Execute(new AdCampaignCommand(0, AdType.Route, 3, 99)).Reason);
            p.Money = -999000;
            Assert.Equal("debt-limit", w.Execute(new AdCampaignCommand(0, AdType.Company, 1)).Reason);
        }
    }

    public class FinanceStatisticsTests
    {
        [Fact]
        public void BalanceSheet_SidesAndOrderPremiums()
        {
            var b = new OpenAT.Sim.Finance.BalanceSheet();
            b.Record(2030, 5000);      // tickets
            b.Record(2061, -300);      // order premium: absolute amount, into the orders field
            b.Record(MoneyReason.DepositInterest, 20);
            b.Record(MoneyReason.Salaries, -1200);
            b.Record(MoneyReason.Maintenance, -800);
            b.Record(2021, -1000);     // kerosene
            Assert.Equal(300, b.Orders);
            Assert.Equal(5000 + 300 + 20, b.Income);
            Assert.Equal(1200 + 800 + 1000, b.Expense);
            Assert.Equal(b.Income - b.Expense, b.Total);
        }

        [Fact]
        public void DayRoll_KeepsTodayAndMonthSums()
        {
            var s = new PlayerStatistics();
            s.Add(StatId.Passengers, 10);
            s.Roll(1);
            Assert.Equal(10, s.Today(StatId.Passengers)); // QUIRK: running total
            Assert.Equal(10, s.PastDay(StatId.Passengers, 1));
            s.Add(StatId.Passengers, 5);
            s.Set(StatId.Money, 777);
            s.Roll(30); // month roll first: sum of Days[0..29]
            Assert.Equal(15 + 10, s[StatId.Passengers].Months[0]);
            Assert.Equal(777, s[StatId.Money].Months[0]);
        }

        [Fact]
        public void DayRoll_Fixed_AdditiveSeriesStartAtZero()
        {
            var s = new PlayerStatistics();
            s.Add(StatId.Passengers, 10);
            s.Set(StatId.Money, 777);
            s.Roll(1, clearAdditive: true);
            Assert.Equal(0, s.Today(StatId.Passengers));
            Assert.Equal(10, s.PastDay(StatId.Passengers, 1));
            Assert.Equal(777, s.Today(StatId.Money));          // snapshot series keep their value
            s.Add(StatId.Passengers, 5);
            Assert.Equal(15, s.Total(StatId.Passengers));
            s.Roll(30, clearAdditive: true);
            Assert.Equal(15, s[StatId.Passengers].Months[0]);  // a real sum of the days

            // The world wires the switch: default clears, off keeps the running total.
            foreach (bool fix in new[] { true, false })
            {
                var w = FinanceFixture.Make();
                w.Options.FixStatisticsDayShift = fix;
                var p = w.Players[0];
                p.Statistics.Add(StatId.Flights, 3);
                new StatisticsRollSystem().OnPlayerNewDay(w, p);
                Assert.Equal(fix ? 0 : 3, p.Statistics.Today(StatId.Flights));
                Assert.Equal(3, p.Statistics.Total(StatId.Flights));
            }
        }

        [Fact]
        public void Update_SnapshotSeries()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[1].Add(new PlaneSnapshot { Id = 1, Value = 1000000, RequiredPilots = 2, RequiredAttendants = 4, MaxAttendants = 4 });
            var w = FinanceFixture.Make(bridge: bridge);
            var ai = w.Players[1];
            ai.Money = 500000; ai.Credit = 100000; ai.Image = 200;
            w.UpdateStatistics(ai);
            var s = ai.Statistics;
            Assert.Equal(1, s.Today(StatId.Planes));
            Assert.Equal(100000, s.Today(StatId.Debt));
            // value = planes + own 8000·100 + money − credit
            Assert.Equal(1000000 + 800000 + 400000, s.Today(StatId.CompanyValue));
            Assert.Equal(6, s.Today(StatId.Employees));
            Assert.Equal(6 * (200 + 2500) / 3000, s.Today(StatId.StaffSatisfaction));
            Assert.Equal(20, s.Today(StatId.Awareness));
            Assert.Equal(8000, s.Today(StatId.SharesHeldBase + 1));
        }
    }

    public class FinanceDayTests
    {
        [Fact]
        public void FullDay_RunsTheNightSequence()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.SecurityFlags = 1;
            w.StartWorkingDay();
            int k9 = w.KerosenePast[9];
            int guard = 0;
            while (!(w.Clock.Date == 1 && w.Clock.State == DayState.Morning) && guard++ < 100000) w.Tick();
            Assert.Equal(1, w.Clock.Date);
            Assert.Equal(k9, w.KerosenePast[8]);
            Assert.Contains(p.Statement, l => l.Separator);
            Assert.Equal(0, p.Today.Total);
            // Overnight interest landed in yesterday's sheet: 1 500 000·3/100/365 = 123.
            Assert.Equal(123, p.Yesterday.DepositInterest);
            Assert.Equal(1500000 + 123 - 25000 - 2000 * 10 / 365, p.Money); // interest, security, dividends
            Assert.Equal(1500000, p.MoneyPast[19]);
            Assert.Equal(21, p.DaysWithoutStrike);
            Assert.False(w.GameEnded);
        }
    }
}
