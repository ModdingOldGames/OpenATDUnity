using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Stats;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Statistics
{
    /// <summary>
    /// The statistics screen (rooms-and-ui-8 §11), opened with the airline logo of the status bar: three
    /// label groups (income/expenses, shares/finance, operations) with ticks, a table of today's values
    /// per airline, and the graph sheet that slides down over it with period zoom and airline toggles.
    /// Data and row semantics: <see cref="StatisticsScreen"/>; remembered settings: <see cref="StatisticsScreenState"/>.
    /// </summary>
    [Room(RoomIds.Statistics)]
    [ScreenName("statistics")]
    public sealed class StatisticsRoom : RoomScreen
    {
        const string Lib = "room/statistk.gli";
        protected override string Library => Lib;
        protected override string Background => "STATISTK";

        const int Down = 329;
        const int GraphL = 6, GraphT = 30, GraphR = 439, GraphB = 329;
        static readonly Color32[] Bright = { C(0x0080ff), C(0x00ff40), C(0xff0000), C(0xffff00) };
        static readonly Color32[] Dark = { C(0x00407f), C(0x007f20), C(0x7f0000), C(0x7f7f00) };
        static Color32 C(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        StatisticsScreenState _st;
        float _sheetY, _speed;
        float _days;
        float _step;
        int _refresh;
        int _hoverRow = -1, _hoverButton = -1;
        int _pressed = -1;
        float _pressedUntil;

        World W => App.Session?.World;

        protected override void OnRoomEnter()
        {
            var w = W;
            if (w == null) return;
            w.UpdateStatistics();
            _st = StatisticsScreenState.Of(w.Local);
            _sheetY = _st.GraphDown ? Down : 0;
            _days = _st.Days;
        }

        protected override void OnLeaveRoom()
        {
            if (_st != null) _st.GraphDown = _sheetY > Down / 2;
        }

        StatRow[] Rows => StatisticsScreen.Groups[_st?.Group ?? 0];

        public override void Animate(float dt)
        {
            if (_st == null) return;
            // Original steps are frame counted (~20 fps): sheet ease-out and zoom 2/3 per step.
            _step += dt;
            while (_step >= 0.05f)
            {
                _step -= 0.05f;
                if (_speed != 0)
                {
                    float target = _speed > 0 ? Down : 0;
                    float remaining = Mathf.Abs(target - _sheetY);
                    float mag = Mathf.Abs(_speed) + 10;
                    while (mag > 1 && mag * (mag + 1) / 2 / 3 > remaining) mag--;
                    _speed = Mathf.Sign(_speed) * Mathf.Max(1, mag);
                    _sheetY += _speed;
                    if (_sheetY >= Down) { _sheetY = Down; _speed = 0; }
                    if (_sheetY <= 0) { _sheetY = 0; _speed = 0; }
                }
                int diff = _st.Days - (int)_days;
                if (diff != 0) _days += diff / 3 * 2 == 0 ? diff : diff / 3 * 2;   // original: (new - cur) / 3 * 2
                // The original refreshes the statistics every 8th frame while the room is open.
                if (++_refresh % 8 == 0) W?.UpdateStatistics();
            }
        }

        // ------------------------------------------------------------------ input

        int LabelRow(Vector2Int m)
        {
            int x = m.x - RX, y = m.y;
            var rows = Rows;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Kind == StatRowKind.Blank) continue;
                int o2 = rows[i].Kind == StatRowKind.Heading ? -18 : -11;
                int top = 56 + 20 * i, bottom = 66 + 20 * i;
                if (x >= 45 + o2 && x <= 155 && y >= top - 5 && y <= bottom + 4) return i;
            }
            return -1;
        }

        bool SheetDown => _sheetY >= Down;

        protected override void Hotspots(InputState input)
        {
            if (_st == null) return;
            var w = W;
            _hoverRow = LabelRow(input.Mouse);
            if (_hoverRow >= 0) App.Ui.Cursor = CursorLook.Hot;
            _hoverButton = -1;
            int sy = (int)_sheetY;
            if (Hot(190, 11 + sy, 639, 38 + sy, 100)) _hoverButton = 100;
            for (int k = 0; k < 4; k++)
            {
                if (w.Players[k].IsOut) continue;
                if (Hot(190 + 110 * k, 370, 299 + 110 * k, 429, 110 + k))
                {
                    _hoverButton = 110 + k;
                    App.Ui.Tooltip.Set(w.Players[k].Airline + " (" + w.Players[k].Name + ")");
                }
            }
            for (int g = 0; g < 3; g++)
                if (Hot(10 + 60 * g, 370, 65 + 60 * g, 429, 120 + g, 3300 + g)) _hoverButton = 120 + g;
            if (SheetDown)
            {
                if (Hot(374, 11, 408, 36, 130)) _hoverButton = 130;
                if (Hot(412, 11, 446, 36, 131)) _hoverButton = 131;
            }
        }

        public override void Update(InputState input, float dt)
        {
            // Label clicks are handled here (they carry the shift modifier).
            if (_st != null && input.LeftDown && !input.Consumed && !Menus.IsOpen && !IsDialogOpen && input.Mouse.y < 440)
            {
                int r = LabelRow(input.Mouse);
                if (r >= 0)
                {
                    input.Consume();
                    _st.Click(r, input.Shift);
                    App.Audio.Play("SOUND/change.raw");
                }
            }
            base.Update(input, dt);
        }

        protected override void OnClick(int id)
        {
            var w = W;
            _pressed = id; _pressedUntil = Time.unscaledTime + 0.3f;
            if (id == 100) { _speed = SheetDown || _speed > 0 ? -25 : 25; return; }
            if (id >= 110 && id <= 113)
            {
                if (SheetDown) _st.PlayerMask ^= 1 << (id - 110);
                return;
            }
            if (id >= 120 && id <= 122) { _st.Group = id - 120; return; }
            if (id == 130) _st.Days = StatisticsScreen.StepPeriod(_st.Days, -1);
            if (id == 131) _st.Days = StatisticsScreen.StepPeriod(_st.Days, 1);
        }

        // ------------------------------------------------------------------ drawing

        protected override void DrawRoom(Screen2D s)
        {
            if (_st == null) return;
            var w = W;
            int ox = RX;
            var kit = App.Ui.Kit;
            DrawDayLabel(s, ox, w);
            DrawLabels(s, ox);
            if (_sheetY < Down) DrawTable(s, ox, w);
            if (_sheetY > 0) DrawSheet(s, ox, w);
            DrawButtons(s, ox, w);
            s.Draw(Assets.Texture(Lib, "TOPSAVER"), ox + 374, 0);   // always, over the buttons (original order)
        }

        void DrawDayLabel(Screen2D s, int ox, World w)
        {
            var f = App.Ui.Kit.DialogPartner;
            string text = (w.Clock.Date + 1) + ". " + Std("STAT", 9000);
            int d = w.Difficulty;
            if ((d == 13 || d == 14 || d == 16) && (int)(Time.unscaledTime / 3) % 2 == 1)
            {
                int target = d == 14 ? Sim.Missions.MissionTargets.MilesDays : Sim.Missions.MissionTargets.Days;
                int left = target - w.Clock.Date;
                text = Format(Std("STAT", left == 1 ? 9011 : 9010), left);
            }
            f.Draw(s, ox + 97, 15, text, TextAlign.Center);
        }

        void DrawLabels(Screen2D s, int ox)
        {
            var rows = Rows;
            var t = _st.Ticked[_st.Group];
            var black = App.Ui.Kit.SmallBlack;
            var plastic = Assets.Font("norm_pl.mcf");
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                if (r.Kind == StatRowKind.Blank) continue;
                int top = 56 + 20 * i;
                string label = Std("STAT", r.TextId);
                if (r.Kind == StatRowKind.Heading) plastic.Draw(s, ox + 27, top, label);
                else if (!r.IsSeries) plastic.Draw(s, ox + 34, top, label);
                else
                {
                    black.Draw(s, ox + 45, top, label);
                    if (t[i]) s.Draw(Assets.Texture(Lib, "HAKCHEN"), ox + 34, top);
                }
                if (i == _hoverRow && _sheetY < Down) s.FillRect(new RectInt(ox + 190, top - 7, 439, 21), new Color32(255, 255, 255, 70));
            }
        }

        void DrawTable(Screen2D s, int ox, World w)
        {
            var t = _st.Ticked[_st.Group];
            var rows = Rows;
            for (int p = 0; p < 4; p++)
            {
                var pl = w.Players[p];
                if (pl.IsOut) continue;
                var f = App.Ui.Kit.Stat(p + 1);
                var vals = StatisticsScreen.TableValues(w, pl, _st.Group, t);
                int right = ox + 190 + 109 * (p + 1) - 5;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (!vals[i].HasValue) continue;
                    f.Draw(s, right, 56 + 20 * i, FormatValue(rows[i].Kind, vals[i].Value), TextAlign.Right);
                }
            }
        }

        static string FormatValue(StatRowKind k, long v)
        {
            switch (k)
            {
                case StatRowKind.Currency:
                case StatRowKind.SumCurrency:
                case StatRowKind.SumDiff:
                case StatRowKind.ShareValue:
                    return UiKit.Money(v);
                case StatRowKind.SinglePercent:
                case StatRowKind.Percent:
                    return UiKit.Unit(UiKit.UnitPercent, v);
                default:
                    return v.ToString(System.Globalization.CultureInfo.InvariantCulture);   // plain count, no separators
            }
        }

        void DrawSheet(Screen2D s, int ox, World w)
        {
            int top = 40 - 360 + (int)_sheetY;
            s.PushClip(new RectInt(ox + 190, 10, 440, 360));
            s.Draw(Assets.Texture(Lib, "DECKEL"), ox + 190, top);
            int days = Mathf.Max(1, Mathf.RoundToInt(_days));
            string cap = days <= 30 ? days + " " + Std("STAT", 9001) : days / 30 + " " + Std("STAT", 9002);
            App.Ui.Kit.Stat(2).Draw(s, ox + 190 + 263, top + 12, cap);
            DrawGraph(s, ox + 190, top, w, days);
            // Zoom buttons ride on the sheet.
            if (_hoverButton == 130 || (_pressed == 130 && Time.unscaledTime < _pressedUntil)) s.Draw(Assets.Texture(Lib, "ZOOMOUT"), ox + 374, top + 2);
            if (_hoverButton == 131 || (_pressed == 131 && Time.unscaledTime < _pressedUntil)) s.Draw(Assets.Texture(Lib, "ZOOMIN"), ox + 412, top + 2);
            s.PopClip();
        }

        void DrawGraph(Screen2D s, int sx, int sy, World w, int days)
        {
            if (w.Clock.Date == 0) return;
            var rows = Rows;
            var t = _st.Ticked[_st.Group];
            bool monthly = days > 30;
            // Original rules (rooms-and-ui-8 §11.5): history = min(period, date + 1) days. Monthly view: nothing
            // is drawn before 30 days of history; then min(11, history) month points are drawn (older months
            // without data count as zero and run off the left edge), spaced width / (period / 30); the scale
            // uses months 0..min(11, ceil(period / 30)).
            int history = Mathf.Min(days, w.Clock.Date + 1);
            if (monthly && history / 30 == 0) return;
            int count = monthly ? Mathf.Min(11, history) : Mathf.Min(history, 30);
            int scanCount = monthly ? Mathf.Min(11, (days + 29) / 30) : count;
            float spacing = monthly ? 433f / (days / 30f) : 433f / days;
            long min = 0, max = 0;
            for (int p = 0; p < 4; p++)
            {
                if (w.Players[p].IsOut || (_st.PlayerMask & (1 << p)) == 0) continue;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (!rows[i].IsSeries || !t[i]) continue;
                    for (int d = 0; d <= scanCount; d++)
                    {
                        long v = StatisticsScreen.Sample(w.Players[p], rows, i, d, monthly);
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
            }
            if (max == min) max = min + 1;
            float scale = (GraphB - GraphT) / (float)(max - min);
            float baseline = sy + GraphB + min * scale;
            s.PushClip(new RectInt(sx + GraphL, sy + GraphT, GraphR - GraphL + 1, GraphB - GraphT + 1));
            bool drew = false;
            var marker = new Texture[4];
            for (int p = 0; p < 4; p++) marker[p] = Assets.Texture(Lib, "POBEL_" + (p + 1));
            bool highlight = _hoverRow >= 0 && rows[_hoverRow].Kind != StatRowKind.Heading && rows[_hoverRow].Kind != StatRowKind.Blank;   // any non-heading row, sums included
            for (int p = 0; p < 4; p++)
            {
                if (w.Players[p].IsOut || (_st.PlayerMask & (1 << p)) == 0) continue;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (!rows[i].IsSeries || !t[i]) continue;
                    var col = highlight && i != _hoverRow ? Dark[p] : Bright[p];
                    Vector2 prev = default;
                    for (int d = 0; d <= count; d++)
                    {
                        long v = StatisticsScreen.Sample(w.Players[p], rows, i, d, monthly);
                        var pt = new Vector2(sx + GraphR - d * spacing, baseline - v * scale);
                        if (d > 0) { Line(s, prev, pt, col); drew = true; }
                        s.Draw(marker[p], Mathf.RoundToInt(pt.x) - 2, Mathf.RoundToInt(pt.y) - 2);
                        prev = pt;
                    }
                }
            }
            if (drew) s.FillRect(new RectInt(sx + GraphL, Mathf.RoundToInt(baseline), GraphR - GraphL, 1), Color.white);
            s.PopClip();
        }

        static void Line(Screen2D s, Vector2 a, Vector2 b, Color32 c)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                s.FillRect(new RectInt(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), 1, 1), c);
            }
        }

        void DrawButtons(Screen2D s, int ox, World w)
        {
            bool pressed(int id) => _pressed == id && Time.unscaledTime < _pressedUntil;
            int sy = (int)_sheetY;
            // Sheet arrow.
            string arrow = null;
            if (SheetDown) arrow = _hoverButton == 100 ? "UPHIGH" : null;
            else if (_speed > 0 || pressed(100)) arrow = "DOWN";
            else if (_hoverButton == 100) arrow = "DOWNHIGH";
            if (arrow != null) s.Draw(Assets.Texture(Lib, arrow), ox + 390, 10 + sy);
            // Airline toggles: only active in graph view.
            for (int k = 0; k < 4; k++)
            {
                if (w.Players[k].IsOut) continue;
                bool on = !SheetDown || (_st.PlayerMask & (1 << k)) != 0;
                bool hov = _hoverButton == 110 + k && SheetDown;
                // ON = selected, SU = selected and hovered, HI = hovered only (rooms-and-ui-8 §11.3).
                string n = "SWTCH" + (k + 1) + (on ? (hov ? "SU" : "ON") : hov ? "HI" : null);
                if (on || hov) s.Draw(Assets.Texture(Lib, n), ox + 190 + 110 * k, 370);
            }
            // Group radio buttons.
            int me = w.LocalPlayer + 1;
            for (int g = 0; g < 3; g++)
            {
                bool sel = _st.Group == g, hov = _hoverButton == 120 + g;
                string prefix = g == 0 ? "BUTA" + me : g == 1 ? "BUTB" : "BUTC";
                string n = sel ? prefix + "ON" : hov ? prefix + "HI" : g == 0 ? prefix + "OFF" : null;
                if (n != null) s.Draw(Assets.Texture(Lib, n), ox + 10 + 60 * g, 370);
            }
        }

        protected override void DrawOverlay(Screen2D s)
        {
            // The exit button covers the airline logo of the status bar (left edge of the bar).
            var m = App.InputNow.Mouse;
            bool hov = m.x < 80 && m.y >= 440;
            s.Draw(Assets.Texture(Lib, hov ? "EXITHIGH" : "EXIT"), 0, 440);
        }

        string Std(string g, int id) => Assets.StdText.Get(g, id) ?? "";
        static string Format(string f, params object[] a) => UI.Dialog.DialogText.Format(f, a);
    }
}
