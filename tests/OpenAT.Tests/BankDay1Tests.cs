using System;
using System.Threading.Tasks;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Testing;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Regression net for "the game froze when entering the bank on day 1 of a free game with planned
    /// flights": walks the local figure into the bank, keeps the clock running while it stays there and
    /// drives every Sim-side bank operation the room's dialogs and menus use, each under a time limit.
    /// </summary>
    public class BankDay1Tests
    {
        static GameData _data;
        static HallData _hall;
        static readonly object Gate = new object();

        static bool Load()
        {
            lock (Gate)
            {
                if (_data != null) return true;
                var install = GameInstall.TryLocate();
                if (install == null) return false;
                _hall = HallData.Load(install);
                _data = GameData.Load(install);
                HallContent.Attach(_data, _hall);
                return true;
            }
        }

        const string Day1Json = @"{ ""mission"": -1, ""advanceDays"": 1, ""skipBriefing"": true,
            ""planes"": [ { ""type"": ""100"", ""count"": 1 } ], ""routes"": 2, ""orders"": 4, ""planFlights"": true }";

        static World StartDay1(int seed = 12345)
        {
            var s = Scenario.Parse(Day1Json);
            s.Seed = seed;
            var w = World.NewGame(_data, s.Settings(_data));
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            s.Apply(w);
            return w;
        }

        /// <summary>Runs <paramref name="body"/> and fails instead of hanging when it takes too long.</summary>
        static void WithTimeout(int seconds, Action body)
        {
            var t = Task.Run(body);
            Assert.True(t.Wait(TimeSpan.FromSeconds(seconds)), "timed out (endless loop?)");
            if (t.Exception != null) throw t.Exception;
        }

        [Theory]
        [InlineData(12345)]
        [InlineData(1)]
        [InlineData(4711)]
        public void FreeGameDay1_WalkIntoBankAndStay_DoesNotHang(int seed)
        {
            if (!Load()) return;
            WithTimeout(120, () =>
            {
                var w = StartDay1(seed);
                Assert.Equal(1, w.Clock.Date);
                Assert.Equal(DayState.Playing, w.Clock.State);
                bool planned = false;
                foreach (var pl in w.Local.Planes.Items) foreach (var e in pl.Plan.Entries) if (e.Kind == OpenAT.Sim.Fleet.FlightKind.Contract) planned = true;
                Assert.True(planned);

                bool entered = false;
                using (w.Events.Subscribe<RoomRequested>(e => { if (e.Player == w.LocalPlayer && e.Room == RoomIds.Bank) entered = true; }))
                {
                    Assert.True(w.Execute(new WalkToRoomCommand(w.LocalPlayer, RoomIds.Bank)).Ok);
                    for (int i = 0; i < 20000 && !entered; i++) w.Tick();
                }
                Assert.True(entered, "the figure never reached the bank");

                // What the room does on entry (the view also remembers the visit in the player's extensions),
                // then the rest of the day spent inside, with the 17:00 autosave written from the bank.
                w.Execute(new EnterBankCommand(w.LocalPlayer));
                w.Local.Extensions["openat.visitedRooms"] = new System.Collections.Generic.HashSet<int> { RoomIds.Bank };
                int saves = 0;
                using (w.Events.Subscribe<OpenAT.Sim.Save.AutosaveRequested>(_ => saves++))
                {
                    int date = w.Clock.Date;
                    for (int i = 0; i < 400000 && w.Clock.Date == date && w.Clock.State == DayState.Playing; i++)
                    {
                        w.Tick();
                        if (i % 500 == 0) ExerciseBankViewValues(w);
                    }
                }
                Assert.True(w.Clock.Date > 1 || w.Clock.Time >= GameClock.At(17, 1), "the day did not run on: " + w.Clock.ClockText);
                var ms = new System.IO.MemoryStream();
                OpenAT.Sim.Save.SaveGame.Write(w, ms, "BANK");
                ms.Position = 0;
                var back = OpenAT.Sim.Save.SaveGame.Read(ms, w.Data);
                Assert.Equal(w.Local.Money, back.Local.Money);
            });
        }

        /// <summary>
        /// The bank hotkey pressed in the own office (where the planner is): the view sends the walk first and
        /// then leaves the room (RoomHotkeys.Walk), so the figure starts from inside the office.
        /// </summary>
        [Theory]
        [InlineData(12345)]
        [InlineData(4711)]
        public void FreeGameDay1_HotkeyFromOfficeToBank_Arrives(int seed)
        {
            if (!Load()) return;
            WithTimeout(120, () =>
            {
                var w = StartDay1(seed);
                int me = w.LocalPlayer, office = RoomIds.OfficeA + 10 * me;
                int arrived = 0;
                using (w.Events.Subscribe<RoomRequested>(e => { if (e.Player == me) arrived = e.Room; }))
                {
                    Assert.True(w.Execute(new WalkToRoomCommand(me, office)).Ok);
                    for (int i = 0; i < 20000 && arrived != office; i++) w.Tick();
                    Assert.Equal(office, arrived);
                    for (int i = 0; i < 200; i++) w.Tick();

                    w.Execute(new WalkToRoomCommand(me, RoomIds.Bank));
                    w.Execute(new ReturnToHallCommand(me));
                    for (int i = 0; i < 20000 && arrived != RoomIds.Bank; i++) w.Tick();
                    Assert.Equal(RoomIds.Bank, arrived);
                }
                w.Execute(new EnterBankCommand(me));
                for (int i = 0; i < 20000; i++) w.Tick();
                ExerciseBankViewValues(w);
            });
        }

        [Fact]
        public void FreeGameDay1_AllBankOperations_TerminateAndStayFinite()
        {
            if (!Load()) return;
            WithTimeout(120, () =>
            {
                var w = StartDay1();
                var p = w.Local;
                int me = p.Index;

                ExerciseBankViewValues(w);

                // Loans: the limit, to the limit, repay to zero, over-limit and silly amounts refused.
                long limit = p.CreditLimit();
                Assert.InRange(limit, 0, int.MaxValue);
                if (limit >= 1000) Assert.True(w.Execute(new TakeLoanCommand(me, limit / 1000 * 1000)).Ok);
                Assert.False(w.Execute(new TakeLoanCommand(me, long.MaxValue)).Ok);
                Assert.False(w.Execute(new TakeLoanCommand(me, long.MinValue)).Ok);
                Assert.False(w.Execute(new RepayLoanCommand(me, long.MinValue)).Ok);
                Assert.True(w.Execute(new RepayLoanCommand(me, p.MaxRepayment())).Ok || p.Credit == 0);
                ExerciseBankViewValues(w);

                // Shares: buy and sell rivals and own shares, the whole free float in one go.
                for (int k = 0; k < Player.Count; k++)
                {
                    var t = w.Players[k];
                    if (t.IsOut) continue;
                    w.Execute(new BuySharesCommand(me, k, 1000));
                    w.Execute(new BuySharesCommand(me, k, w.FreeFloat(t)));
                    Assert.False(w.Execute(new BuySharesCommand(me, k, long.MaxValue)).Ok);
                    Assert.False(double.IsNaN(t.SharePrice));
                    w.Execute(new SellSharesCommand(me, k, p.SharesOwned[k]));
                    Assert.False(double.IsNaN(t.SharePrice));
                    ExerciseBankViewValues(w);
                }

                // Dividend and every share-issue variant.
                Assert.True(w.Execute(new SetDividendCommand(me, Player.MaxDividend)).Ok);
                Assert.False(w.Execute(new SetDividendCommand(me, int.MinValue)).Ok);
                foreach (int pct in new[] { 10, 25, 50, 75, 100 })
                    foreach (int disc in new[] { 5, 3, 1 })
                    {
                        w.Execute(new IssueSharesCommand(me, pct, disc));
                        ExerciseBankViewValues(w);
                    }

                // Several days and nights with interest, dividends and price moves.
                for (int d = 0; d < 3; d++)
                {
                    for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++)
                    {
                        if (w.Clock.State == DayState.Playing && !w.Local.CalledItADay) w.Execute(new OpenAT.Sim.Missions.CallItADayCommand(me));
                        w.Tick();
                    }
                    ExerciseBankViewValues(w);
                    w.Execute(new OpenAT.Sim.Missions.StartWorkingDayCommand());
                }
            });
        }

        [Fact]
        public void BuyingAWholeTinyIssue_KeepsTheTargetPriceFinite()
        {
            var w = FinanceFixture.Make();
            var me = w.Players[0];
            var t = w.Players[1];
            t.SharesIssued = 1000;       // one chunk equals the whole issue
            Array.Clear(t.SharesOwned, 0, t.SharesOwned.Length);
            Assert.True(w.Execute(new BuySharesCommand(0, 1, 1000)).Ok);
            Assert.False(double.IsInfinity(t.SharePrice) || double.IsNaN(t.SharePrice));
            Assert.Equal(1000, me.SharesOwned[1]);
            Assert.True(w.Execute(new SellSharesCommand(0, 1, 1000)).Ok);
            Assert.False(double.IsInfinity(t.SharePrice) || double.IsNaN(t.SharePrice));
        }

        [Fact]
        public void HourlyPrice_RecoversFromABrokenPrice()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[2];
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
            {
                p.SharePrice = bad;
                w.Rules.Get<ISharePriceRule>().Hourly(w, p, 7);
                Assert.True(p.SharePrice >= 0 && !double.IsInfinity(p.SharePrice));
            }
        }

        /// <summary>The numbers the bank room, its dialogs, the loan form and the share charts compute.</summary>
        static void ExerciseBankViewValues(World w)
        {
            var me = w.Local;
            long limit = me.CreditLimit();
            Assert.InRange(limit, 0, int.MaxValue);
            _ = me.MaxRepayment();
            _ = me.LoanInterestMonthly + me.LoanInterestYearly;
            _ = me.IssuableShares;
            foreach (int pct in new[] { 10, 25, 50, 75, 100 })
            {
                long n = me.SharesForPercent(pct) / 100 * 100;
                long price = (long)me.SharePrice - 5;
                _ = Player.IssueFee(n, price);
            }
            foreach (var t in w.Players)
            {
                _ = w.FreeFloat(t);
                _ = t.DividendYield;
                for (int d = 0; d < 10; d++) Assert.False(double.IsNaN(t.SharePrices[d]), "NaN share price of " + t.Index);
                if (t.SharesIssued != 0) _ = me.SharesOwned[t.Index] * 100 / t.SharesIssued;
            }
            _ = w.Rules.Get<ICompanyValueRule>().CompanyValue(w, me);
            Assert.InRange(me.Statement.Count, 0, Player.StatementLines);
        }
    }
}
