using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Items;
using OpenAT.Sim.Staff;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.ArabAir
{
    /// <summary>
    /// Petrol Air / Arab Air counter (rooms-and-ui-8 §2): the clerk (kerosene grades, tanks), the
    /// kerosene price chart on the wall (tooltip with today's price), the radio. Buying goes through
    /// MENU_BUYKEROSIN (<see cref="BuyKeroseneMenu"/>) and the tank choices (<see cref="BuyTankCommand"/>).
    /// </summary>
    [Room(RoomIds.ArabAir)]
    [ScreenName("arabair")]
    public sealed class ArabAirRoom : RoomScreen
    {
        const string Lib = "room/arab.gli";
        protected override string Library => Lib;
        protected override string Background => "ARAB_AUS";
        protected override int DefaultDialogPartner => Talkers.Arab;
        const int ClickClerk = 10, ClickGlove = 12, ClickChart = 30;

        SmackerPerson _man;

        protected override void OnCreate()
        {
            int x = 425, y = 108;
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "ArabW.smk", x, y, Mood.Idle, "A9:0 E1:1");
            _man.Clip(1, "ArabA.smk", x, y, Mood.Idle, "E1:2 E1:4 E1:6", canCancel: false);
            _man.Clip(2, "ArabL.smk", x, y, Mood.Listening, "A9:2 E5:4 E5:6", 1, 1, 50, 50);
            _man.Clip(3, "ArabP.smk", x, y, Mood.Talking, "A9:4", 1, 1, 20, 20);
            _man.Clip(4, "ArabS.smk", x, y, Mood.Talking, "A9:4 E1:2 E1:6");
            _man.Clip(5, "ArabZ.smk", x, y, Mood.Idle, "A9:0", canCancel: false, sound: "arabz.raw");
            _man.Clip(6, "ArabG.smk", x, y, Mood.Idle, "A9:5", canCancel: false);
        }

        protected override void OnRoomEnter()
        {
            App.Audio.Play("SOUND/arab.raw");
            var s = App.Session;
            if (s == null) return;
            var p = s.Local;
            int id = p.KeroseneEntryRemark();
            var raw = id != 0 ? Assets.StdText.Get("Advi", id) : null;
            if (raw != null) App.Advisors.Queue(AdvisorKind.Kerosene, raw);
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Arab ? _man : null;

        public override void Animate(float dt) => _man.Update(dt);

        /// <summary>The glove lies on the counter once per day for the whole airport (items-security-special §3.3).</summary>
        bool GloveThere => App.Session != null && App.Session.World.Items.Glove && !App.Session.Local.HasItem(ItemId.Glove);

        protected override void Hotspots(InputState input)
        {
            // Registered from the lowest to the highest priority (the last match wins): the exits beat the glove.
            if (GloveThere) Hot(314, 280, 386, 342, ClickGlove);
            Exit(0, 232, 144, 398);
            Exit(0, 288, 314, 380);
            Exit(0, 351, 639, 439);
            Hot(437, 89, 600, 323, ClickClerk);
            var w = App.Session?.World;
            if (w != null && Hot(313, 136, 371, 176, ClickChart, 0, CursorLook.Normal))
            {
                App.Ui.Tooltip.Set(DialogText.Format(Assets.StdText.Get("Tool", 4500) ?? "%li", new object[] { (long)w.Kerosene }));
                App.Ui.Tooltip.ShowNow();
            }
            int mx = input.Mouse.x - RX, my = input.Mouse.y;
            if (input.LeftDown && mx >= 179 && mx <= 242 && my >= 215 && my <= 261) App.Audio.Play("SOUND/radio.raw");
        }

        protected override void OnClick(int id)
        {
            if (id == ClickClerk) StartDialog(Talkers.Arab, DialogMedium.Air, 0);
            else if (id == ClickGlove) App.Session.World.Execute(new PickUpItemCommand(App.Session.Local.Index, ItemId.Glove));
        }

        protected override void DrawRoom(Screen2D s)
        {
            DrawChart(s);
            _man.Draw(s, RX);
            if (GloveThere) s.Draw(Assets.Texture(Lib, "GLOVE"), RX + 314, 280);
        }

        /// <summary>The kerosene price history in the skewed wall chart (red, auto-scaled).</summary>
        void DrawChart(Screen2D s)
        {
            var w = App.Session?.World;
            if (w == null) return;
            var past = w.KerosenePast;
            double max = 1;
            foreach (var v in past) max = System.Math.Max(max, v);
            max *= 1.2;
            Vector2 TL = new Vector2(313, 136), TR = new Vector2(371, 146), BL = new Vector2(313, 166), BR = new Vector2(371, 176);
            Vector2 prev = default;
            var red = new Color32(0xff, 0x20, 0x20, 255);
            int n = past.Length;
            for (int d = 0; d < n; d++)
            {
                float fx = d / (float)(n - 1), fy = (float)(past[n - 1 - d] / max);
                var pt = Vector2.Lerp(Vector2.Lerp(BL, BR, fx), Vector2.Lerp(TL, TR, fx), fy);
                if (d > 0)
                {
                    int k = Mathf.Max(1, Mathf.CeilToInt((pt - prev).magnitude));
                    for (int i = 0; i <= k; i++)
                    {
                        var q = Vector2.Lerp(prev, pt, i / (float)k);
                        s.FillRect(new RectInt(RX + Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), 1, 1), red);
                    }
                }
                prev = pt;
            }
        }
    }

    /// <summary>
    /// MENU_BUYKEROSIN (rooms-and-ui-2 §4.13): amount entry by barrels, percent of the tank or money with
    /// the calculator; clamped to free tank space and the overdraft; OK buys (<see cref="BuyKeroseneCommand"/>).
    /// </summary>
    public sealed class BuyKeroseneMenu : PopupMenu
    {
        const string Lib = "gli/buykero.gli";
        readonly KeroseneKind _kind;
        long _barrels;
        int _cell = -1;
        public BuyKeroseneMenu(KeroseneKind kind) { _kind = kind; }
        public override int Type => MenuIds.BuyKerosin;
        public override Vector2Int Size => new Vector2Int(330, 160);
        public override Vector2Int? FixedPos => new Vector2Int(155, 140);
        public override Texture Background => Assets.Texture(Lib, "FORM");
        Player P => App.Session.Local;
        int Price => Player.PricePerBarrel(App.Session.World.Kerosene, _kind);

        long Clamp(long b) => System.Math.Max(0, System.Math.Min(b, P.MaxKeroseneBarrels(_kind)));

        public override void Update(InputState input, Vector2Int l)
        {
            int cell = Hot(l, 126, 87, 198, 100) ? 0 : Hot(l, 198, 87, 234, 100) ? 1 : Hot(l, 234, 87, 327, 100) ? 2 : -1;
            bool ok = Hot(l, 237, 125, 304, 156, 4510);
            bool cancel = Hot(l, 153, 133, 208, 159, 4511);
            if (!input.LeftDown) return;
            if (cell >= 0)
            {
                _cell = cell;
                Host.Calculator.Open(ScreenPos + new Vector2Int(318, 67), v =>
                {
                    long b = cell == 0 ? v : cell == 1 ? System.Math.Min(100, v) * P.Tank / 100 : Price > 0 ? v / Price : 0;
                    _barrels = Clamp(b);
                    _cell = -1;
                }, () => _cell = -1);
            }
            else if (ok) Buy();
            else if (cancel) { ReEntry = 650; Close(); }
        }

        void Buy()
        {
            var w = App.Session.World;
            long cost = Player.TankPurchaseCost(Price, _barrels);
            var room = App.Rooms.Current;
            ReEntry = -1;
            Close();
            if (cost > 0 && !P.CanAfford(cost)) { room?.StartDialog(Talkers.Arab, DialogMedium.Air, 6000); return; }
            w.Execute(new BuyKeroseneCommand(P.Index, _kind, (int)_barrels));
            room?.StartDialog(Talkers.Arab, DialogMedium.Air, 700);
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            var p = P;
            long b = Clamp(_barrels);
            long cost = Player.TankPurchaseCost(Price, b);
            int Pct(long v) => p.Tank > 0 ? (int)(v * 100 / p.Tank) : 0;
            f.Draw(s, x + 56, y + 10, Std("Tank", 2000));
            f.Draw(s, x + 56, y + 37, Std("Tank", 2010)); f.Draw(s, x + 200, y + 37, UiKit.Money(Price), TextAlign.Right);
            f.Draw(s, x + 56, y + 50, Std("Tank", 2011)); f.Draw(s, x + 200, y + 50, UiKit.Thousands(p.Tank), TextAlign.Right);
            f.Draw(s, x + 20, y + 76, Std("Tank", 2020)); f.Draw(s, x + 196, y + 76, UiKit.Thousands(p.TankContents), TextAlign.Right); f.Draw(s, x + 232, y + 76, Pct(p.TankContents) + "%", TextAlign.Right);
            int[,] cells = { { 126, 198 }, { 198, 234 }, { 234, 327 } };
            for (int c = 0; c < 3; c++)
                if (_cell == c || (l.x >= cells[c, 0] && l.x <= cells[c, 1] && l.y >= 87 && l.y <= 100))
                    s.FillRect(new RectInt(x + cells[c, 0], y + 87, cells[c, 1] - cells[c, 0], 13), new Color32(32, 255, 100, 90));
            f.Draw(s, x + 20, y + 89, Std("Tank", 2021)); f.Draw(s, x + 196, y + 89, UiKit.Thousands(b), TextAlign.Right); f.Draw(s, x + 232, y + 89, Pct(b) + "%", TextAlign.Right); f.Draw(s, x + 325, y + 89, UiKit.Money(cost), TextAlign.Right);
            f.Draw(s, x + 20, y + 102, Std("Tank", 2022)); f.Draw(s, x + 196, y + 102, UiKit.Thousands(p.TankContents + b), TextAlign.Right); f.Draw(s, x + 232, y + 102, Pct(p.TankContents + b) + "%", TextAlign.Right);
            long gross = (long)Price * b;
            f.Draw(s, x + 56, y + 120, Std("Tank", 2030)); f.Draw(s, x + 120 + 60, y + 120, UiKit.Money(gross - cost), TextAlign.Right);
            bool hovOk = l.x >= 237 && l.x <= 304 && l.y >= 125 && l.y <= 156, hovCancel = l.x >= 153 && l.x <= 208 && l.y >= 133 && l.y <= 159;
            if (hovOk) s.Draw(Assets.Texture(Lib, "OK"), x + 248, y + 125);
            if (!hovCancel) s.Draw(Assets.Texture(Lib, "CANCEL"), x + 168, y + 137);
        }
    }

    /// <summary>Arab Air clerk (rooms-and-ui-3b §8).</summary>
    [DialogScript(Talkers.Arab)]
    public sealed class ArabDialog : DialogTree
    {
        static readonly int[] TankSize = { 10000, 30000, 50000, 100000 };
        static readonly int[] Multipliers = { 1, 2, 3, 5, 10 };

        static object[] Prices(DialogSession d)
        {
            int m = d.World.Kerosene;
            return new object[] { (long)Player.PricePerBarrel(m, KeroseneKind.Premium), (long)Player.PricePerBarrel(m, KeroseneKind.Normal), (long)Player.PricePerBarrel(m, KeroseneKind.Cheap) };
        }

        public ArabDialog() : base("Arab")
        {
            Open(d =>
            {
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 == 1) { d.Say(1010); return; }   // the violin case was handed over (saboteur trust 1)
                if (d.Par1 == 700 || d.Par1 == 6000 || d.Par1 == 650) { d.Say(d.Par1); return; }
                if (d.Medium == DialogMedium.Phone) d.Say(101);
                else if (d.TalkedToA) d.Say(102);
                else d.Say(100);
                d.TalkedToA = true;
            });
            On(new[] { 100, 101, 102, 650 }, d => d.Choices(200, 203));
            On(new[] { 250, 301, 403, 700, 904 }, d => d.Choices(201, 203));
            On(200, d => d.Say(250));
            On(201, d =>
            {
                bool phone = d.Medium == DialogMedium.Phone;
                d.Say(phone ? 301 : 300);
                d.ShowNumbers(phone ? 9990301 : 9990300, Prices(d));
            });
            On(300, d => d.Choices(400, 403, ChoiceArgs.OnePerOption, Prices(d)));
            On(400, 402, (d, id) =>
            {
                d.Par2 = id - 400;
                var p = d.Player;
                // The grade switches as soon as it is picked (also by phone, and if no amount is bought).
                d.World.Execute(new ChooseKeroseneCommand(p.Index, (KeroseneKind)Mathf.Clamp(d.Par2, 0, 2)));
                if (p.HasAdvisor(AdvisorType.Kerosene))
                {
                    var raw = d.Assets.StdText.Get("Advi", 3020 + d.Par2);
                    if (raw != null) d.Stage.App.Advisors.Queue(AdvisorKind.Kerosene, raw, AdvisorUrgency.Comment);
                }
                d.Say(500);
            });
            On(new[] { 500, 620 }, d =>
            {
                if (d.Medium == DialogMedium.Phone) { d.Choices(201, 203); return; }
                d.OpenMenu(new BuyKeroseneMenu((KeroseneKind)Mathf.Clamp(d.Par2, 0, 2)), 650, new Vector2Int(320, 220), 0.1f);
            });
            On(604, d => d.Say(620));
            On(202, d => d.Say(800));
            On(1010, d => d.Say(1020));
            On(800, d => d.Choices(900, 904));
            On(900, 903, (d, id) => { d.Par3 = id - 900; d.Say(680); });
            On(680, d =>
            {
                long unit = Player.TankPricePerUnit[d.Par3];
                var args = new object[6];
                args[0] = "";
                for (int i = 0; i < 5; i++) args[i + 1] = UiKit.Thousands(unit * Multipliers[i]);
                d.Choices(690, 695, ChoiceArgs.OnePerOption, args);
            });
            On(690, d => d.Say(650));
            On(691, 695, (d, id) =>
            {
                int mult = Multipliers[id - 691];
                if (!d.World.Execute(new BuyTankCommand(d.Player.Index, d.Par3, mult)).Ok) { d.Say(6000); return; }
                d.Say(700);
            });
        }
    }
}
