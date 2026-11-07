using OpenAT.Sim.Finance;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Bank
{
    /// <summary>
    /// The bank (rooms-and-ui-6 §2): two bankers (shares / loans), the carnivorous plant and its fly,
    /// the wall monitor, the share-price charts and the money in the waste bin.
    /// </summary>
    [Room(RoomIds.Bank)]
    [ScreenName("bank")]
    public sealed class BankRoom : RoomScreen
    {
        const string Lib = "room/bank.gli";
        protected override string Library => Lib;
        protected override string Background => "BANK";
        protected override int DefaultDialogPartner => Talkers.Banker1;

        const int ClickBanker1 = 10, ClickBanker2 = 11, ClickMoney = 20;

        SmackerPerson _man, _woman, _plant, _fly;
        float _monitorT;
        // Desk monitor MONI01..05 (rooms-and-ui-6 §2.2): speed 200 ticks of the 32 ms real-time ticker = 6.4 s per picture.
        const int MonitorFrames = 5;
        const float MonitorFrameSeconds = 200 * FrameAnimation.TickSeconds;
        bool _flyCaught;
        int _tipPlayer = -1;

        static readonly uint[] ChartColours = { 0x0080ff, 0x00ff40, 0xff0000, 0xffff00 };
        static readonly Vector2Int[] TopLeft = { V(388, 41), V(414, 49), V(388, 99), V(414, 99) };
        static readonly Vector2Int[] TopRight = { V(408, 47), V(427, 50), V(408, 99), V(427, 99) };
        static readonly Vector2Int[] BottomLeft = { V(388, 94), V(414, 92), V(388, 152), V(414, 146) };
        static readonly Vector2Int[] BottomRight = { V(408, 94), V(427, 94), V(408, 148), V(427, 142) };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        protected override void OnCreate()
        {
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "BM_Wait.smk", 430, 160, Mood.Idle, "A6:0 A4:1 E1:2", 2, 4);
            _man.Clip(1, "BM_Scrib.smk", 430, 160, Mood.Idle, "A1:0", canCancel: false);
            _man.Clip(2, "BM_Turn.smk", 430, 160, Mood.Idle, "A1:4 E1:3 E1:4", canCancel: false);
            _man.Clip(3, "BM_RedeW.smk", 430, 160, Mood.Listening, "A9:3 A2:8 E1:4 E1:7", 2, 5);
            _man.Clip(4, "BM_Rede.smk", 430, 160, Mood.Talking, "A9:5 E1:3 E1:7");
            _man.Clip(5, "BM_Taste.smk", 430, 160, Mood.Talking, "A1:6 E1:3 E1:7", canCancel: false, sound: "BM_Taste.raw");
            _man.Clip(6, "BM_Rede.smk", 430, 160, Mood.Talking, "A9:6 A1:5 E1:3 E1:7");
            _man.Clip(7, "BM_TurnZ.smk", 430, 160, Mood.Idle, "A1:0", canCancel: false);
            _man.Clip(8, "BM_RedeK.smk", 430, 160, Mood.Listening, "A1:3 E1:4 E1:7", waitMin: 1, waitMax: 1);

            _woman = new SmackerPerson(Assets, App.Audio);
            _woman.Clip(0, "BF_Wait.smk", 252, 150, Mood.Idle, "A6:0 A4:1 E1:2", 2, 4);
            _woman.Clip(1, "BF_Work.smk", 252, 150, Mood.Idle, "A1:0", 1, 3, canCancel: false);
            _woman.Clip(2, "BF_Turn.smk", 252, 150, Mood.Idle, "A1:3 E1:4", canCancel: false);
            _woman.Clip(3, "BF_RedeW.smk", 252, 150, Mood.Listening, "A3:3 A2:7 A1:5 E1:4 E1:6", 5, 5);
            _woman.Clip(4, "BF_Rede.smk", 252, 150, Mood.Talking, "A9:4 E1:3 E1:6");
            _woman.Clip(5, "BF_Taste.smk", 252, 150, Mood.Listening, "A1:3 E1:3 E1:4 E1:7", canCancel: false);
            _woman.Clip(6, "BF_RedeW.smk", 252, 150, Mood.Idle, "A1:3 A1:4 A1:8", waitMin: 7, waitMax: 7, canCancel: false);
            _woman.Clip(7, "BF_RedeK.smk", 252, 150, Mood.Listening, "A1:3 E1:3 E1:4 E1:7", waitMin: 1, waitMax: 1, canCancel: false);
            _woman.Clip(8, "BF_RedeW.smk", 252, 150, Mood.Idle, "A1:3 A1:4 A1:9", waitMin: 7, waitMax: 7, canCancel: false);
            _woman.Clip(9, "BF_TurnZ.smk", 252, 150, Mood.Idle, "A9:0", canCancel: false);

            // Plant and fly: when the fly is catchable (fly frame 24) the plant snaps (plant frame 8 = caught).
            _plant = new SmackerPerson(Assets, App.Audio);
            _plant.Clip(0, "BlumW.smk", 406, 122, Mood.Idle, "A9:0 A2:2 A2:3", waitMin: 30, waitMax: 50);
            _plant.Clip(1, "Blum.smk", 406, 122, Mood.Idle, "A1:2", canCancel: false, sound: "SnakEat.raw").OnFrame = f => { if (f == 8) _flyCaught = true; };
            _plant.Clip(2, "BlumZug.smk", 406, 122, Mood.Idle, "A1:0", canCancel: false, sound: "BlumZug.raw");
            _plant.Clip(3, "BlumZugB.smk", 406, 122, Mood.Idle, "A6:0", sound: "BlumZugB.raw");

            _fly = new SmackerPerson(Assets, App.Audio);
            _fly.Clip(0, "", 504, 99, Mood.Idle, "A5:1 A5:4");
            _fly.Clip(1, "fliege.smk", 504, 99, Mood.Idle, "A5:2", 3, 6);
            _fly.Clip(2, "fliege.smk", 504, 99, Mood.Idle, "A5:2 E1:3", 3, 6).OnFrame = f => { if (f == 24 && !_flyCaught) _plant.Decision = 1; };
            _fly.Clip(3, "", 504, 99, Mood.Listening, "A5:3", waitMin: 99, waitMax: 99);
            _fly.Clip(4, "", 504, 99, Mood.Idle, "A5:4", waitMin: 99, waitMax: 99);
            // TODO(rooms-and-ui-6 §2.4): modem animation while the player owns the anti-hacking item (security flag 8).
        }

        protected override void OnRoomEnter()
        {
            SetBackgroundFx(0, "Tippen.raw", 12000);
            SetBackgroundFx(1, "printer.raw", 30000);
            var s = App.Session;
            if (s != null) s.World.Execute(new EnterBankCommand(s.World.LocalPlayer));
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) =>
            talker == Talkers.Banker1 ? _man : talker == Talkers.Banker2 ? _woman : null;

        public override void Animate(float dt)
        {
            _monitorT = (_monitorT + dt) % (MonitorFrames * MonitorFrameSeconds);
            _man.Update(dt);
            _woman.Update(dt);
            _plant.Update(dt);
            _fly.Update(dt);
            if (_flyCaught && _fly.Desired != Mood.Listening) { _fly.SetDesired(Mood.Listening); _plant.Decision = -1; }
        }

        bool MoneyInBin => App.Session != null && App.Session.World.MoneyInBankBin;

        protected override void Hotspots(InputState input)
        {
            Hot(462, 207, 601, 352, ClickBanker1);
            Hot(251, 155, 335, 262, ClickBanker2);
            Exit(62, 64, 173, 186);
            if (MoneyInBin) Hot(288, 388, 326, 410, ClickMoney);
        }

        protected override void OnClick(int id)
        {
            switch (id)
            {
                case ClickBanker1: StartDialog(Talkers.Banker1); break;
                case ClickBanker2: StartDialog(Talkers.Banker2); break;
                case ClickMoney:
                    var s = App.Session;
                    if (s != null && s.World.Execute(new CollectBankBinMoneyCommand(s.World.LocalPlayer)).Ok)
                        StartDialog(Talkers.Banker1, DialogMedium.Air, 20);
                    break;
            }
        }

        public override void Update(InputState input, float dt)
        {
            base.Update(input, dt);
            // Share tip: hovering a wall chart (rooms-and-ui-6 §2.3).
            _tipPlayer = -1;
            var m = input.Mouse;
            int x = m.x - RX, y = m.y;
            if (!Menus.IsOpen && !IsDialogOpen && x >= 378 && x <= 436 && y >= 33 && y <= 160)
            {
                int p = (x - 378) / 30 + 2 * ((y - 33) / 64);
                var w = App.Session?.World;
                if (w != null && p >= 0 && p < 4 && !w.Players[p].IsOut) _tipPlayer = p;
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            if (MoneyInBin) s.Draw(Assets.Texture(Lib, "MONEY"), ox + 281, 368);
            DrawCharts(s, ox);
            int frame = (int)(_monitorT / MonitorFrameSeconds) % MonitorFrames + 1;
            s.Draw(Assets.Texture(Lib, "MONI0" + frame), ox + 187, 200);
            _fly.Draw(s, ox);
            _plant.Draw(s, ox);
            _woman.Draw(s, ox);
            _man.Draw(s, ox);
        }

        void DrawCharts(Screen2D s, int ox)
        {
            var w = App.Session?.World;
            if (w == null) return;
            for (int c = 0; c < 4; c++)
            {
                var p = w.Players[c];
                if (p.IsOut) continue;
                double max = 0;
                for (int d = 0; d < 10; d++) max = System.Math.Max(max, Finite(p.SharePrices[9 - d]));
                max += max / 2;
                if (!(max > 0) || double.IsInfinity(max)) continue;
                uint rgb = ChartColours[c];
                var col = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                Vector2 prev = default;
                for (int d = 0; d < 10; d++)
                {
                    float fx = d / 9f, fy = (float)(Finite(p.SharePrices[9 - d]) / max);
                    // Bilinear point in the perspective quad of this chart.
                    Vector2 top = Vector2.Lerp(TopLeft[c], TopRight[c], fx), bottom = Vector2.Lerp(BottomLeft[c], BottomRight[c], fx);
                    var pt = Vector2.Lerp(bottom, top, fy);
                    if (d > 0) Line(s, ox, prev, pt, col);
                    prev = pt;
                }
            }
        }

        /// <summary>A broken price (NaN, infinite or negative) draws as 0 instead of poisoning the chart scale.</summary>
        static double Finite(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;

        static void Line(Screen2D s, int ox, Vector2 a, Vector2 b, Color32 c)
        {
            if (float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y)) return;
            // One dot per pixel of the longer axis; the charts are a few dozen pixels, so a huge count means bad input.
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))), 1, 640);
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                s.FillRect(new RectInt(ox + Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), 1, 1), c);
            }
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (_tipPlayer < 0) return;
            // Share tip card (gli/tip.gli BLOC1) at screen (163,23) of the canvas.
            var w = App.Session?.World;
            if (w == null) return;
            int x = RX + 163, y = 23;
            s.Draw(Assets.Texture("gli/tip.gli", "BLOC1"), x, y);
            DrawShareTip(s, x, y, w, w.Local, w.Players[_tipPlayer]);
        }

        /// <summary>
        /// The share tip card (rooms-and-ui-6 §2.3 "Share tip card contents"): airline heading, the share on the market
        /// (price, dividend, yield), the viewer's holding (count, cost, value), every player's stake and a 10-day chart.
        /// Labels are the install's <c>Akti</c> texts; (x, y) is the card's top-left corner on the screen.
        /// </summary>
        void DrawShareTip(Screen2D s, int x, int y, OpenAT.Sim.World w, OpenAT.Sim.Player viewer, OpenAT.Sim.Player a)
        {
            var f = App.Ui.Kit.SmallBlack;
            string T(int id) => Assets.StdText.Get("Akti", id) ?? "";
            void Text(int tx, int ty, string text) => f.Draw(s, x + tx, y + ty, text ?? "");
            int ai = a.Index;
            long price = (long)Finite(a.SharePrice);

            Text(32, 3, a.Airline);

            Text(32, 20, T(1000));
            for (int i = 0; i < 3; i++) Text(42, 31 + 11 * i, T(1001 + i));
            Text(98, 32, UiKit.Money(price));
            Text(98, 42, a.Dividend.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Text(98, 53, UiKit.Unit(UiKit.UnitPercent, price > 0 ? a.Dividend * 100L / price : 0));

            Text(32, 75, T(1004));
            // Value column at x 98 as in the original, moved right when a label is longer (the English
            // "market value" label is 62 px from x 42 and ran into its value). Block 1 keeps x 98: its
            // values sit left of the mini chart.
            int vx = 98;
            for (int i = 0; i < 3; i++)
            {
                Text(42, 86 + 11 * i, T(1005 + i));
                vx = System.Math.Max(vx, 42 + f.Measure(T(1005 + i)) + 4);
            }
            long owned = viewer != null ? viewer.SharesOwned[ai] : 0;
            Text(vx, 86, owned.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Text(vx, 97, UiKit.Money(viewer != null ? viewer.ShareCostBasis[ai] : 0));
            Text(vx, 108, UiKit.Money(price * owned));

            Text(32, 130, T(1008));
            for (int c = 0; c < 4; c++)
            {
                var p = w.Players[c];
                Text(42, 141 + c * 11, p.Name);
                Text(150, 141 + c * 11, UiKit.Unit(UiKit.UnitPercent, a.SharesIssued > 0 ? p.SharesOwned[ai] * 100 / a.SharesIssued : 0));
            }

            // Mini chart: mid line, axes, then the price line (oldest day on the left), scale 0..1.5 x the 10-day high.
            Line(s, x, new Vector2(140, y + 48), new Vector2(200, y + 48), Rgb(0x084848));
            Line(s, x, new Vector2(140, y + 79), new Vector2(140, y + 17), Rgb(0x080808));
            Line(s, x, new Vector2(140, y + 79), new Vector2(200, y + 79), Rgb(0x080808));
            long max = 0;
            for (int d = 0; d < 10; d++) max = System.Math.Max(max, (long)Finite(a.SharePrices[d]));
            max += max / 2;
            if (max <= 0) return;
            var col = Rgb(ChartColours[ai]);
            for (int k = 0; k < 9; k++)
            {
                var p0 = new Vector2(141 + 60 * k / 9, y + 79 - (float)(Finite(a.SharePrices[9 - k]) * 61 / max));
                var p1 = new Vector2(141 + 60 * (k + 1) / 9, y + 79 - (float)(Finite(a.SharePrices[8 - k]) * 61 / max));
                Line(s, x, p0, p1, col);
            }
        }

        static Color32 Rgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
