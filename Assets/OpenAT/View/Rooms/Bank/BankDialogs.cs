using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Bank
{
    /// <summary>Female clerk: account and loans (rooms-and-ui-3b §1).</summary>
    [DialogScript(Talkers.Banker2)]
    public sealed class LoanClerkDialog : DialogTree
    {
        public LoanClerkDialog() : base("Bank")
        {
            Open(d =>
            {
                if (d.Par1 >= 10000) d.Say(d.Par1);
                else if (d.Par1 == 101) d.Say(161);
                else if (d.Par1 == 100) d.Say(160);
                else if (d.Par1 == 102) d.Say(162);
                else if (d.Medium == DialogMedium.Phone) d.Say(109);
                else d.Choices(101, 103);
            });
            On(109, d => d.Choices(101, 103));
            On(101, d =>
            {
                var p = d.Player;
                long limit = p.CreditLimit() / 1000 * 1000;
                if (limit <= 0) d.Say(120);
                else if (d.Medium == DialogMedium.Phone) d.Say(150);
                else d.OpenMenu(new LoanMenu(true, limit), -1, new Vector2Int(240, 147), 0.1f);
            });
            On(102, d =>
            {
                var p = d.Player;
                if (p.Credit <= 0) d.Say(140);
                else if (d.Medium == DialogMedium.Phone) d.Say(150);
                else d.OpenMenu(new LoanMenu(false, p.Credit / 2), -1, new Vector2Int(240, 147), 0.1f);
            });
            On(100, d =>
            {
                var p = d.Player;
                if (p.Credit > 0)
                {
                    d.Say(110);
                    d.ShowNumbers(9990110, UiKit.Thousands(p.Money), UiKit.Thousands(p.Credit), p.LoanRate);
                }
                else
                {
                    d.Say(111);
                    d.ShowNumbers(9990111, UiKit.Thousands(p.Money));
                }
            });
        }
    }

    /// <summary>Male clerk: shares, takeovers, share issues, dividend (rooms-and-ui-3b §2).</summary>
    [DialogScript(Talkers.Banker1)]
    public sealed class ShareClerkDialog : DialogTree
    {
        static readonly int[] MainMenu = { 900, 901, 3000, 3001, 3015, 3033, 3051, 2014, 2024, 2114, 2124, 2514, 2532 };
        static readonly int[] IssuePercent = { 10, 25, 50, 75, 100 };
        static readonly int[] Discounts = { 5, 3, 1 };

        static object[] AirlineAndFree(World w)
        {
            var a = new object[8];
            for (int k = 0; k < 4; k++)
            {
                var t = w.Players[k];
                long held = 0;
                foreach (var o in w.Players) if (!o.IsOut) held += o.SharesOwned[k];
                a[2 * k] = t.Airline;
                a[2 * k + 1] = UiKit.Thousands(t.IsOut ? 0 : t.SharesIssued - held);
            }
            return a;
        }

        static object[] AirlineAndOwn(World w, Player me)
        {
            var a = new object[8];
            for (int k = 0; k < 4; k++) { a[2 * k] = w.Players[k].Airline; a[2 * k + 1] = UiKit.Thousands(w.Players[k].IsOut ? 0 : me.SharesOwned[k]); }
            return a;
        }

        static object[] AirlinePercent(World w, Player me)
        {
            var a = new object[8];
            for (int k = 0; k < 4; k++)
            {
                var t = w.Players[k];
                a[2 * k] = t.Airline;
                a[2 * k + 1] = t.IsOut || t.SharesIssued == 0 ? 0L : me.SharesOwned[k] * 100 / t.SharesIssued;
            }
            return a;
        }

        public ShareClerkDialog() : base("Bank")
        {
            Open(d =>
            {
                var w = d.World;
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 == 20) { d.Say(920, d.Player.Name, d.Player.Airline); return; }
                if (w.PendingTakeover != TakeoverMode.None)
                {
                    d.Say(d.Medium == DialogMedium.Phone ? 906 : 905, w.Players[w.TakeoverTarget].Airline, w.Players[w.TakeoverAcquirer].Airline);
                    return;
                }
                d.Say(d.Medium == DialogMedium.Phone ? 901 : 900);
            });
            Choices(MainMenu, 1000, 1005);
            On(920, d => d.Choices(921, 921));

            // Buying shares.
            On(1000, d => { if (d.Medium == DialogMedium.Phone) d.Say(1200); else d.Say(2000); });
            On(new[] { 2000, 2016 }, d => d.Choices(2010, 2014, ChoiceArgs.TwoPerOption, AirlineAndFree(d.World)));
            On(new[] { 2030, 2031 }, d => d.Choices(2020, 2024, ChoiceArgs.TwoPerOption, AirlineAndFree(d.World)));
            On(2010, 2013, (d, id) => Trade(d, id - 2010, true));
            On(2020, 2023, (d, id) => Trade(d, id - 2020, true));

            // Selling shares.
            On(1001, d => { if (d.Medium == DialogMedium.Phone) d.Say(1200); else d.Say(2100); });
            On(new[] { 2100, 2116 }, d => d.Choices(2110, 2114, ChoiceArgs.TwoPerOption, AirlineAndOwn(d.World, d.Player)));
            On(new[] { 2130, 2131 }, d => d.Choices(2120, 2124, ChoiceArgs.TwoPerOption, AirlineAndOwn(d.World, d.Player)));
            On(2110, 2113, (d, id) => Trade(d, id - 2110, false));
            On(2120, 2123, (d, id) => Trade(d, id - 2120, false));

            // Shareholder rights / takeover.
            On(1002, d => d.Say(2500));
            On(new[] { 2500, 2520, 2521, 2522, 2523 }, d => d.Choices(2510, 2514, ChoiceArgs.TwoPerOption, AirlinePercent(d.World, d.Player)));
            On(2510, 2513, (d, id) =>
            {
                int k = id - 2510;
                d.Par1 = k;
                var w = d.World; var me = d.Player; var t = w.Players[k];
                if (k == me.Index) d.Say(2520);
                else if (t.IsOut) d.Say(2521);
                else if (me.SharesOwned[k] == 0) d.Say(2525);
                else if (me.SharesOwned[k] < t.SharesIssued / 2)
                {
                    d.Say(2522);
                    d.ShowNumbers(9992522, (me.SharesOwned[k] * 100 / System.Math.Max(1, t.SharesIssued)).ToString());
                }
                else if (t.SharesOwned[me.Index] >= me.SharesIssued * 3 / 10)
                {
                    // The number card shows the target's stake in us, the stake the refusal is about
                    // (the original shows our stake in the target there, a slip).
                    d.Say(2523, t.Airline);
                    d.ShowNumbers(9992523, (t.SharesOwned[me.Index] * 100 / System.Math.Max(1, me.SharesIssued)).ToString());
                }
                else d.Say(2524, t.Airline);
            });
            On(2524, d => d.Choices(2530, 2532));
            On(2530, 2531, (d, id) =>
            {
                var mode = id == 2530 ? TakeoverMode.Absorb : TakeoverMode.Liquidate;
                d.World.Execute(new RequestTakeoverCommand(d.Player.Index, d.Par1, mode));
                d.Say(2540);
            });

            // Issuing new shares.
            On(1003, d =>
            {
                var p = d.Player;
                if (p.IssuableShares < 10000) d.Say(3000);
                else if (p.SharePrice < 10) d.Say(3001);
                else
                {
                    d.Say(3002);
                    d.ShowNumbers(9993002, UiKit.Thousands(p.SharesIssued), UiKit.Thousands(p.IssuableShares));
                }
            });
            On(3002, d =>
            {
                var p = d.Player;
                var args = new object[5];
                for (int i = 0; i < 5; i++) args[i] = p.SharesForPercent(IssuePercent[i]);
                d.Choices(3010, 3015, ChoiceArgs.OnePerOption, args);
            });
            On(3010, 3014, (d, id) => { d.Par1 = IssuePercent[id - 3010]; d.Say(3020); });
            On(3020, d => d.Say(3021));
            On(3021, d => { d.Say(3022); d.ShowNumbers(9993022, (long)d.Player.SharePrice); });
            On(3022, d =>
            {
                long k = (long)d.Player.SharePrice;
                d.Choices(3030, 3033, ChoiceArgs.OnePerOption, k - 5, k - 3, k - 1);
            });
            On(3030, 3032, (d, id) =>
            {
                var p = d.Player;
                int disc = Discounts[id - 3030];
                d.Par3 = disc;
                long n = p.SharesForPercent(d.Par1), price = (long)p.SharePrice - disc;
                d.Say(3040);
                d.ShowNumbers(9993040, UiKit.Thousands(n), price, (long)p.SharePrice, UiKit.Thousands(Player.IssueFee(n, price)));
            });
            On(3040, d => d.Choices(3050, 3051));
            On(3050, d =>
            {
                var p = d.Player;
                // Same count as offered and booked (Player.SharesForPercent; FixShareIssueRounding).
                long n = p.SharesForPercent(d.Par1);
                d.World.Execute(new IssueSharesCommand(p.Index, d.Par1, d.Par3));
                d.Say(3060, UiKit.Thousands(n));
            });

            // Dividend.
            On(1004, d =>
            {
                if (d.Medium == DialogMedium.Phone) d.Say(1200);
                else d.OpenMenu(new DividendMenu());
            });
        }

        static void Trade(DialogSession d, int k, bool buy)
        {
            if (d.World.Players[k].IsOut) { d.Say(buy ? 2016 : 2116); return; }
            d.OpenMenu(new ShareMenu(k, buy), buy ? 2030 : 2130);
        }
    }

    /// <summary>
    /// MENU_BANK (rooms-and-ui-2 §4.16): take or repay a loan on kredit.gli; the amount is entered with
    /// the calculator and clamped; OK books through <see cref="TakeLoanCommand"/> / <see cref="RepayLoanCommand"/>
    /// and the clerk answers (dialog openings 100/101/102).
    /// </summary>
    public sealed class LoanMenu : PopupMenu
    {
        const string Lib = "gli/kredit.gli";
        readonly bool _take;
        readonly long _limit;
        long _amount;

        public LoanMenu(bool take, long preset) { _take = take; _limit = preset; _amount = preset; }

        public override int Type => MenuIds.Bank;
        public override Vector2Int Size => new Vector2Int(318, 200);
        public override Texture Background => Assets.Texture(Lib, "KREDIT");

        Player P => App.Session.Local;

        // Repay: the repayable maximum (Credit, or limited by cash with FixLoanRepayCash), so OK never asks for more than the command allows.
        long Max => _take ? _limit : P.MaxRepayment();

        public override void Update(InputState input, Vector2Int l)
        {
            bool amount = Hot(l, 56, 81, 318, 96, _take ? 4103 : 4102);
            bool ok = Hot(l, 204, 156, 271, 187, _take ? 4110 : 4100);
            bool cancel = Hot(l, 111, 159, 198, 190, _take ? 4111 : 4101);
            if (!input.LeftDown) return;
            _amount = System.Math.Max(0, System.Math.Min(_amount, Max));
            if (amount)
                Host.Calculator.Open(ScreenPos + new Vector2Int(276, 60), v => _amount = System.Math.Max(0, System.Math.Min(v, Max)));
            else if (ok) Confirm();
            else if (cancel) { App.Audio.Play("SOUND/pap2.raw"); Close(); }
        }

        void Confirm()
        {
            var s = App.Session;
            var room = App.Rooms.Current;
            Close();
            int answer;
            if (_take)
            {
                if (_amount >= 1000 && s.World.Execute(new TakeLoanCommand(P.Index, _amount)).Ok) answer = 101;
                else answer = 100;
            }
            else
            {
                // The clerk confirms only a repayment that was booked; nothing repaid ends the dialog quietly.
                long amt = System.Math.Max(0, System.Math.Min(_amount, Max));
                answer = amt > 0 && s.World.Execute(new RepayLoanCommand(P.Index, amt)).Ok ? 102 : -1;
            }
            if (answer >= 0) room?.StartDialog(Talkers.Banker2, DialogMedium.Air, answer);
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var p = P;
            var f = Kit.BankBlack;
            Kit.BankRed.Draw(s, x + 58, y + 17, p.Name);
            string Kont(int id) => Std("Kont", id);
            void Row(int ry, string label, string value) { f.Draw(s, x + 58, y + ry, label); f.Draw(s, x + 270, y + ry, value, TextAlign.Right); }
            Row(35, Kont(1000), UiKit.Money(p.Credit));
            Row(45, Kont(1001), p.LoanRate + "%");
            Row(55, Kont(1002), UiKit.Money(p.LoanRate * p.Credit / 1200));
            Row(65, Kont(1003), UiKit.Money(p.LoanRate * p.Credit / 100));
            long amt = System.Math.Max(0, System.Math.Min(_amount, Max));
            Row(83, Kont(_take ? 1004 : 1005), UiKit.Money(amt));
            long nc = _take ? p.Credit + amt : p.Credit - amt;
            Row(103, Kont(1006), UiKit.Money(nc));
            Row(113, Kont(1001), p.LoanRate + "%");
            Row(123, Kont(1002), UiKit.Money(p.LoanRate * nc / 1200));
            Row(133, Kont(1003), UiKit.Money(p.LoanRate * nc / 100));
            if (l.x >= 204 && l.x <= 271 && l.y >= 156 && l.y <= 187) s.Draw(Assets.Texture(Lib, "OK"), x + 215, y + 156);
            if (l.x >= 111 && l.x <= 198 && l.y >= 159 && l.y <= 190) s.Draw(Assets.Texture(Lib, "CANCEL"), x + 136, y + 169);
            if (l.x >= 56 && l.y >= 81 && l.y <= 96) HoverBand(s, new RectInt(x + 56, y + 81, 262, 15));
        }
    }

    /// <summary>
    /// MENU_AKTIE (rooms-and-ui-2 §4.15): buy or sell shares of one airline on form.gli. The amount is
    /// entered as a count, a percentage of the issued shares or money with the calculator and clamped to
    /// the free float (buy) or our holding (sell); it starts at half of that. OK trades through
    /// <see cref="BuySharesCommand"/> / <see cref="SellSharesCommand"/> (the 10 % fee and the price impact are
    /// the Sim's); the clerk then asks for the next airline (2030/2130, 2031/2131 after a cancel), or says
    /// Bank 6000 when the purchase would pass the overdraft limit.
    /// </summary>
    public sealed class ShareMenu : PopupMenu
    {
        const string Lib = "gli/form.gli";
        // Editable cells of the trade row: count, percent of the issued shares, money.
        static readonly int[,] Cells = { { 140, 198 }, { 200, 238 }, { 240, 316 } };
        readonly int _target;
        readonly bool _buy;
        long _amount;
        int _cell = -1;

        public ShareMenu(int target, bool buy) { _target = target; _buy = buy; }

        public override int Type => MenuIds.Aktie;
        public override Vector2Int Size => new Vector2Int(330, 160);
        public override Texture Background => Assets.Texture(Lib, "FORM");

        World W => App.Session.World;
        Player P => App.Session.Local;
        Player T => W.Players[_target];
        /// <summary>Price in whole units, as the form shows and computes it; a broken price counts as 0.</summary>
        long Price => (long)Finite(T.SharePrice);
        long Limit => _buy ? W.FreeFloat(T) : P.SharesOwned[_target];
        long Clamp(long v) => System.Math.Max(0, System.Math.Min(v, Limit));

        public override void OnOpen() => _amount = Limit / 2;

        public override void Update(InputState input, Vector2Int l)
        {
            int cell = -1;
            for (int c = 0; c < 3; c++)
                if (Hot(l, Cells[c, 0], 71, Cells[c, 1], 85, 4220 + c)) cell = c;
            bool ok = Hot(l, 237, 125, 304, 156, _buy ? 4200 : 4210);
            bool cancel = Hot(l, 153, 133, 208, 159, _buy ? 4201 : 4211);
            if (!input.LeftDown) return;
            if (cell >= 0)
            {
                _cell = cell;
                Host.Calculator.Open(ScreenPos + new Vector2Int(318, 51), v => { _amount = Clamp(FromCalculator(cell, v)); _cell = -1; }, () => _cell = -1);
            }
            else if (ok) Confirm();
            else if (cancel) { App.Audio.Play("SOUND/pap2.raw"); OnRightClick(); }
        }

        long FromCalculator(int cell, long v)
        {
            if (cell == 0) return v;
            if (cell == 1) return System.Math.Min(100, v) * T.SharesIssued / 100;
            return Price > 0 ? v / Price : 0;
        }

        /// <summary>Cancel (button or right click): the clerk's "well, okay" variant of the follow-up question.</summary>
        public override void OnRightClick()
        {
            if (ReEntry == 2030 || ReEntry == 2130) ReEntry++;
            Close();
        }

        void Confirm()
        {
            var w = W;
            var p = P;
            long n = Clamp(_amount);
            if (_buy)
            {
                if (PlayerPrefs.GetFloat(GameAudio.PrefAmbience, 1f) > 0) App.Audio.Play("SOUND/printer.raw");
                if (n > 0)
                {
                    var r = w.Execute(new BuySharesCommand(p.Index, _target, n));
                    if (!r.Ok && r.Reason == "debt-limit") ReEntry = 6000;
                }
            }
            else if (n > 0) w.Execute(new SellSharesCommand(p.Index, _target, n));
            Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.BankBlack;
            var t = T;
            var p = P;
            long k = Price, issued = t.SharesIssued;
            long own = p.SharesOwned[_target], n = Clamp(_amount), after = _buy ? own + n : own - n;
            string Text(int id) => Std("Akti", id);
            void Cells3(BitmapFont font, int ry, long count)
            {
                font.Draw(s, x + 140, y + ry, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                font.Draw(s, x + 200, y + ry, UiKit.Unit(UiKit.UnitPercent, issued > 0 ? count * 100 / issued : 0));
                font.Draw(s, x + 240, y + ry, UiKit.Money(count * k));
            }

            Kit.BankRed.Draw(s, x + 60, y + 10, Text(_buy ? 3000 : 3001));
            f.Draw(s, x + 90, y + 27, Text(1001));
            f.Draw(s, x + 140, y + 27, Text(3010));
            f.Draw(s, x + 200, y + 27, Text(3011));
            f.Draw(s, x + 240, y + 27, Text(3012));

            f.Draw(s, x + 30, y + 40, Text(1000));
            f.Draw(s, x + 90, y + 40, UiKit.Money(k));
            Cells3(f, 40, W.FreeFloat(t));

            f.Draw(s, x + 30, y + 60, Text(3020));
            Cells3(f, 60, own);

            for (int c = 0; c < 3; c++)
                if (_cell == c || (!Host.Calculator.IsOpen && l.x >= Cells[c, 0] && l.x <= Cells[c, 1] && l.y >= 71 && l.y <= 85))
                    HoverBand(s, new RectInt(x + Cells[c, 0], y + 71, Cells[c, 1] - Cells[c, 0], 14));
            f.Draw(s, x + 30, y + 73, Text(_buy ? 3021 : 3022));
            Cells3(Kit.SmallBlack, 73, n);

            f.Draw(s, x + 30, y + 86, Text(3023));
            Cells3(f, 86, after);

            // Approximate: the trade at today's price, without fee and price impact (as the label says).
            f.Draw(s, x + 30, y + 111, Text(3030));
            f.Draw(s, x + 220, y + 111, UiKit.Money(p.Money - (long)((after - own) * Finite(t.SharePrice))));

            DrawButtons(s, x, y, l, Assets);
        }

        static double Finite(double v) => double.IsNaN(v) || v < 0 ? 0 : v > 1e15 ? 1e15 : v;

        /// <summary>form.gli OK/CANCEL overlays (rooms-and-ui-2 §4.13): OK while hovered, CANCEL except while hovered.</summary>
        internal static void DrawButtons(Screen2D s, int x, int y, Vector2Int l, GameAssets assets)
        {
            bool hovOk = l.x >= 237 && l.x <= 304 && l.y >= 125 && l.y <= 156;
            bool hovCancel = l.x >= 153 && l.x <= 208 && l.y >= 133 && l.y <= 159;
            if (hovOk) s.Draw(assets.Texture(Lib, "OK"), x + 248, y + 125);
            if (!hovCancel) s.Draw(assets.Texture(Lib, "CANCEL"), x + 168, y + 137);
        }
    }

    /// <summary>
    /// MENU_SETRENDITE (rooms-and-ui-2 §4.14): the dividend per share on form.gli, entered with the
    /// calculator and clamped to 0..25; OK books it through <see cref="SetDividendCommand"/>. The payout
    /// lines show what the night booking charges: outside shares × dividend per year and per day (the
    /// original multiplies by all issued shares, including our own, which are never paid out).
    /// </summary>
    public sealed class DividendMenu : PopupMenu
    {
        const string Lib = "gli/form.gli";
        int _dividend;
        bool _editing;

        public override int Type => MenuIds.SetRendite;
        public override Vector2Int Size => new Vector2Int(330, 160);
        public override Texture Background => Assets.Texture(Lib, "FORM");

        Player P => App.Session.Local;

        public override void OnOpen() => _dividend = P.Dividend;

        public override void Update(InputState input, Vector2Int l)
        {
            bool cell = Hot(l, 180, 35, 325, 49, 4320);
            bool ok = Hot(l, 237, 125, 304, 156, 4300);
            bool cancel = Hot(l, 153, 133, 208, 159, 4301);
            if (!input.LeftDown) return;
            if (cell)
            {
                _editing = true;
                Host.Calculator.Open(ScreenPos + new Vector2Int(322, 15), v =>
                {
                    _dividend = (int)System.Math.Max(0, System.Math.Min(v, Player.MaxDividend));
                    _editing = false;
                }, () => _editing = false);
            }
            else if (ok)
            {
                App.Session.World.Execute(new SetDividendCommand(P.Index, _dividend));
                App.Audio.Play("SOUND/pap2.raw");
                Close();
            }
            else if (cancel) Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.BankBlack;
            var p = P;
            long outside = p.SharesIssued - p.SharesOwned[p.Index];
            Kit.BankRed.Draw(s, x + 56, y + 10, Std("Akti", 2000));
            if (_editing || (!Host.Calculator.IsOpen && l.x >= 180 && l.x <= 325 && l.y >= 35 && l.y <= 49))
                HoverBand(s, new RectInt(x + 180, y + 35, 145, 14));
            f.Draw(s, x + 56, y + 37, Std("Akti", 2001));
            f.Draw(s, x + 56, y + 50, Std("Akti", 2002));
            f.Draw(s, x + 56, y + 63, Std("Akti", 2003));
            Kit.SmallBlack.Draw(s, x + 320, y + 37, UiKit.Unit(UiKit.UnitPercent, _dividend), TextAlign.Right);
            f.Draw(s, x + 320, y + 50, UiKit.Money(_dividend * outside), TextAlign.Right);
            f.Draw(s, x + 320, y + 63, UiKit.Money(_dividend * outside / 365), TextAlign.Right);
            ShareMenu.DrawButtons(s, x, y, l, Assets);
        }
    }
}
