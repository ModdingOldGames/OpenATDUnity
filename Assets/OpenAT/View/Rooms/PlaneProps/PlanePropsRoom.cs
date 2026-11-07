using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.PlaneProps
{
    /// <summary>
    /// Plane equipment (rooms-and-ui-7 §5, planes-and-flights §6): comfort page (seats, trays, decor,
    /// food, flight attendants) and technical page (tyres, engines, safety, cockpit, first-class share)
    /// flipped by a sliding animation; previous/next plane, copy/paste of the configuration, OK leaves.
    /// Targets go through <see cref="SetEquipmentCommand"/> and are refitted later by the sim.
    /// </summary>
    [Room(RoomIds.PlaneProps)]
    [ScreenName("planeprops")]
    public sealed class PlanePropsRoom : RoomScreen
    {
        const string Lib = "room/planprop.gli";
        protected override string Library => Lib;
        protected override string Background => null;

        sealed class Memory { public int PlaneId = -1; public float Page = 100; public bool UsedOnce; }
        Memory _mem;
        float _ap = 100;
        int _dir;
        int _index = -1;
        float _blinkUntil;
        int _hoverRow = -1, _hoverLevel = -1;
        float _repeatAt; int _repeatId;

        const int IdPrev = 10, IdCopy = 11, IdOk = 12, IdPaste = 13, IdNext = 14, IdCrewMinus = 15, IdCrewPlus = 16, IdToTech = 90, IdToComfort = 91, IdFcMinus = 115, IdFcPlus = 116;
        static readonly Vector2Int[] StarOffsets = { V(498, 8), V(498, 87), V(498, 167), V(498, 284), V(66, 8), V(66, 87), V(66, 167), V(66, 246) };
        /// <summary>Equipment item of each star row (comfort: seats, trays, decor, food; technical: tyres, engines, safety, cockpit).</summary>
        static readonly int[] RowItem = { Equip.Seats, Equip.Trays, Equip.Deco, Equip.Food, Equip.Tyres, Equip.Engines, Equip.Safety, Equip.Electronics };
        static readonly Vector2Int[] SeatOffsets = { V(95, 130), V(94, 124), V(75, 104) };
        static readonly Vector2Int[] FoodA = { V(116, 98), V(116, 90), V(116, 102), V(116, 108), V(116, 108), V(116, 109), V(135, 110), V(135, 112), V(135, 118) };
        static readonly Vector2Int[] FoodB = { V(104, 999), V(116, 286), V(116, 330), V(116, 292), V(116, 303), V(116, 330), V(135, 330), V(135, 325), V(135, 999) };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        World W => App.Session?.World;
        Player P => App.Session?.Local;
        List<Plane> Planes => P.Planes.ToList();
        Plane Current => _index >= 0 && _index < Planes.Count ? Planes[_index] : null;

        protected override void OnCreate()
        {
            var p = P;
            if (p == null) { _mem = new Memory(); return; }
            if (!p.Extensions.TryGetValue("openat.planeprops", out var v) || !(v is Memory m)) p.Extensions["openat.planeprops"] = m = new Memory();
            _mem = m;
            _ap = m.Page;
            var list = Planes;
            _index = list.FindIndex(x => x.Id == m.PlaneId);
            if (_index < 0 && list.Count > 0) _index = 0;
        }

        protected override void OnRoomEnter()
        {
            if (P != null) P.InPlanePropRoom = true;
            if (!_mem.UsedOnce) { _mem.UsedOnce = true; _blinkUntil = Time.unscaledTime + 5; }
        }

        protected override void OnLeaveRoom()
        {
            if (P != null) P.InPlanePropRoom = false;
            _mem.PlaneId = Current?.Id ?? -1;
            _mem.Page = _ap > 50 ? 100 : 0;
        }

        public override void Animate(float dt)
        {
            if (_dir != 0)
            {
                _ap = Mathf.Clamp(_ap + _dir * dt * 100, 0, 100);
                if (_ap <= 0 || _ap >= 100) _dir = 0;
            }
        }

        bool Comfort => _ap >= 100;
        bool Technical => _ap <= 0;
        int HeadX => (int)(229 * (100 - _ap) / 100);

        // ------------------------------------------------------------------ input

        protected override void Hotspots(InputState input)
        {
            _hoverRow = _hoverLevel = -1;
            var pl = Current;
            if (pl == null) { Exit(0, 0, 639, 439); return; }
            if (!Comfort && !Technical) return;
            int hx = HeadX;
            if (_index > 0) Hot(hx + 134, 26, hx + 164, 44, IdPrev, 3100);
            Hot(hx + 164, 26, hx + 190, 50, IdCopy, 3101);
            if (Comfort) { Hot(hx + 172, 50, hx + 192, 55, IdToTech, 3105); Hot(hx + 178, 56, hx + 195, 69, IdToTech, 3105); Hot(hx + 182, 60, hx + 207, 67, IdToTech, 3105); }
            if (Technical) { Hot(hx + 224, 48, hx + 243, 52, IdToComfort, 3106); Hot(hx + 218, 53, hx + 239, 56, IdToComfort, 3106); Hot(hx + 210, 57, hx + 234, 64, IdToComfort, 3106); }
            if (Hot(hx + 190, 26, hx + 226, 62, IdOk, 3102, CursorLook.Exit)) ShowRefitCost();
            Hot(hx + 226, 26, hx + 253, 50, IdPaste, 3103);
            if (_index < Planes.Count - 1) Hot(hx + 253, 26, hx + 282, 44, IdNext, 3104);
            int first = Comfort ? 0 : 4;
            for (int r = first; r < first + 4; r++)
                for (int c = 0; c < 3; c++)
                {
                    var o = StarOffsets[r];
                    if (Hot(o.x + 47 * c, o.y, o.x + 47 * c + 43, o.y + 36, 1000 + r * 10 + c, (Comfort ? 3200 : 3204) + (r - first))) { _hoverRow = r; _hoverLevel = c; }
                }
            if (Comfort)
            {
                Hot(497, 367, 541, 402, IdCrewMinus, 3210);
                Hot(592, 367, 636, 402, IdCrewPlus, 3211);
            }
            else
            {
                Hot(65, 367, 106, 402, IdFcMinus, 3213);
                Hot(161, 367, 204, 402, IdFcPlus, 3212);
            }
            // Hold on prev/next: auto-repeat (800 ms, then 400 ms).
            if (input.LeftHeld && (_repeatId == IdPrev || _repeatId == IdNext) && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + 0.4f;
                Step(_repeatId == IdPrev ? -1 : 1);
            }
            if (!input.LeftHeld) _repeatId = 0;
        }

        void ShowRefitCost()
        {
            long sum = FlightOps.PendingRefitCost(P);
            if (sum == 0 || App.Advisors.Showing) return;
            var raw = Assets.StdText.Get("Schd", sum > 0 ? 1820 : 1821);
            if (raw != null) App.Advisors.Queue(AdvisorKind.Assistant, DialogText.Format(raw, new object[] { UiKit.Thousands(System.Math.Abs(sum)) }), AdvisorUrgency.Comment);
        }

        void Step(int d)
        {
            int n = Planes.Count;
            int ni = Mathf.Clamp(_index + d, 0, n - 1);
            if (ni != _index) { _index = ni; App.Audio.Play("SOUND/click.raw"); }
        }

        protected override void OnClick(int id)
        {
            var pl = Current;
            var w = W;
            switch (id)
            {
                case IdPrev: Step(-1); _repeatId = id; _repeatAt = Time.unscaledTime + 0.8f; return;
                case IdNext: Step(1); _repeatId = id; _repeatAt = Time.unscaledTime + 0.8f; return;
                case IdCopy: w.Execute(new CopyPasteEquipmentCommand(P.Index, pl.Id, false)); App.Audio.Play("SOUND/click.raw"); return;
                case IdPaste:
                    if (!w.Execute(new CopyPasteEquipmentCommand(P.Index, pl.Id, true)).Ok) Refused();
                    return;
                case IdOk: Leave(); return;
                case IdToTech: _dir = -1; _blinkUntil = 0; return;
                case IdToComfort: _dir = 1; return;
                case IdCrewMinus: w.Execute(new ChangeAttendantsCommand(P.Index, pl.Id, -1)); return;
                case IdCrewPlus: w.Execute(new ChangeAttendantsCommand(P.Index, pl.Id, 1)); return;
                case IdFcMinus: w.Execute(new ChangeFirstClassCommand(P.Index, pl.Id, false)); return;
                case IdFcPlus: w.Execute(new ChangeFirstClassCommand(P.Index, pl.Id, true)); return;
            }
            if (id >= 1000)
            {
                int r = (id - 1000) / 10, c = (id - 1000) % 10;
                if (w.Execute(new SetEquipmentCommand(P.Index, pl.Id, RowItem[r], c)).Ok) App.Audio.Play("SOUND/click.raw");
                else Refused();
            }
        }

        void Refused()
        {
            var raw = Assets.StdText.Get("Schd", 1810);
            if (raw != null) App.Advisors.Queue(AdvisorKind.Assistant, raw);
        }

        // ------------------------------------------------------------------ drawing

        int Target(Plane pl, int item) => _hoverRow >= 0 && RowItem[_hoverRow] == item ? _hoverLevel : pl.EquipmentTarget[item];

        public override void Draw(Screen2D s)
        {
            // Full canvas built from the two page backgrounds (no room background bitmap).
            s.FillRect(new RectInt(0, 0, s.Width, 440), Color.black);
            base.Draw(s);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            var pl = Current;
            // Technical page underneath, comfort page slides over it from the left (§5 page state).
            if (_ap < 100)
            {
                int tx = -(int)(_ap * 229 / 100);
                s.Draw(Assets.Texture(Lib, "BACK2"), ox + tx, 0);
                if (pl != null) DrawTechnical(s, ox + tx, pl);
            }
            if (_ap > 0)
            {
                int cx = (int)((100 - _ap) * 229 / 100);
                s.Draw(Assets.Texture(Lib, "BACK"), ox + cx, 0);
                if (pl != null) DrawComfort(s, ox + cx, pl);
            }
            DrawHeadline(s, ox, pl);
        }

        void DrawHeadline(Screen2D s, int ox, Plane pl)
        {
            int hx = ox + HeadX;
            s.Draw(Assets.Texture(Lib, "MENUMASK"), hx, 2);
            if (pl != null) App.Ui.Kit.BigGrey.Draw(s, hx + 210, 3, pl.Name, TextAlign.Center);
            var m = App.InputNow.Mouse;
            int lx = m.x - hx, ly = m.y;
            bool In(int x1, int y1, int x2, int y2) => lx >= x1 && lx <= x2 && ly >= y1 && ly <= y2;
            bool active = (Comfort || Technical) && !Menus.IsOpen;
            if (active && _index > 0 && In(134, 26, 164, 44)) s.Draw(Assets.Texture(Lib, "LEFT"), hx + 135, 27);
            if (active && In(164, 26, 190, 50)) s.Draw(Assets.Texture(Lib, "COPY"), hx + 164, 27);
            if (active && In(190, 26, 226, 62)) s.Draw(Assets.Texture(Lib, "OK"), hx + 191, 27);
            if (active && In(226, 26, 253, 50)) s.Draw(Assets.Texture(Lib, "PASTE"), hx + 210, 27);
            if (active && _index < Planes.Count - 1 && In(253, 26, 282, 44)) s.Draw(Assets.Texture(Lib, "RIGHT"), hx + 248, 27);
            bool blink = Time.unscaledTime < _blinkUntil && ((int)(Time.unscaledTime * 2) & 1) == 0;
            if (!Comfort) s.Draw(Assets.Texture(Lib, "USELOFF"), hx + 172, 50);
            else if (blink || (active && In(172, 50, 207, 69))) s.Draw(Assets.Texture(Lib, "USEL"), hx + 172, 50);
            if (!Technical) s.Draw(Assets.Texture(Lib, "USEROFF"), hx + 210, 50);
            else if (active && In(210, 48, 243, 64)) s.Draw(Assets.Texture(Lib, "USER"), hx + 210, 50);
        }

        void DrawStars(Screen2D s, int ox, Plane pl, int row)
        {
            var o = StarOffsets[row];
            int item = RowItem[row];
            int cur = pl.Equipment[item], tgt = pl.EquipmentTarget[item];
            s.Draw(Assets.Texture(Lib, "STERN" + (cur + 1)), ox + o.x + 47 * cur, o.y);
            int frame = (int)(Time.unscaledTime / 0.15f) % 8 + 1;
            if (tgt != cur) s.Draw(Assets.Texture(Lib, "HIGH0" + frame), ox + o.x + 47 * tgt - 1, o.y - 1);
            if (_hoverRow == row) s.Draw(Assets.Texture(Lib, "HIGH0" + frame), ox + o.x + 47 * _hoverLevel - 1, o.y - 1);
        }

        string CostLabel(long v) => (Assets.StdText.Get("Schd", 1800) ?? "") + " " + UiKit.Money(v);

        void DrawComfort(Screen2D s, int ox, Plane pl)
        {
            int seat = Target(pl, Equip.Seats), tray = Target(pl, Equip.Trays), deco = Target(pl, Equip.Deco), food = Target(pl, Equip.Food);
            if (deco < 2) s.Draw(Assets.Texture(Lib, "DECO" + (deco + 1)), ox, 27);
            var so = SeatOffsets[seat];
            s.Draw(Assets.Texture(Lib, "SITZ" + (seat + 1) + "_" + (tray + 1)), ox + so.x, so.y);
            int fi = seat * 3 + tray;
            var ea = Assets.Texture(Lib, "ESSEN" + (food + 1) + "A");
            var eb = Assets.Texture(Lib, "ESSEN" + (food + 1) + "B");
            if (FoodA[fi].y < 999) s.Draw(ea, ox + so.x + FoodA[fi].x - ea.width / 2, so.y + FoodA[fi].y - ea.height / 2);
            if (FoodB[fi].y < 999) s.Draw(eb, ox + so.x + FoodB[fi].x - eb.width / 2, so.y + FoodB[fi].y - eb.height / 2);
            for (int r = 0; r < 4; r++) DrawStars(s, ox, pl, r);
            var f = App.Ui.Kit.SmallBlack;
            long sum = 0;
            int[] ys = { 58, 138, 218 };
            for (int r = 0; r < 3; r++)
            {
                int item = RowItem[r], t = Target(pl, item);
                if (t == pl.Equipment[item]) continue;
                long c = Equip.ChangeCost(item, pl.Equipment[item], t, pl.Seats);
                sum += c;
                f.Draw(s, ox + 631, ys[r], CostLabel(c), TextAlign.Right);
            }
            if (sum != 0) f.Draw(s, ox + 631, 252, CostLabel(sum), TextAlign.Right);
            f.Draw(s, ox + 631, 334, UiKit.Thousands((long)Equip.Costs[Equip.Food][food] * pl.MaxPassengers) + " $ " + (Assets.StdText.Get("Schd", 1801) ?? ""), TextAlign.Right);
            // Flight attendants.
            int max = pl.MaxAttendants;
            if (max >= 10) s.Draw(Assets.Texture(Lib, "KL_" + (max / 10 % 10)), ox + 551, 374);
            s.Draw(Assets.Texture(Lib, "KL_" + (max % 10)), ox + 567, 374);
            int baseCrew = System.Math.Min(pl.CrewAttendants, pl.Attendants), extra = System.Math.Max(0, pl.CrewAttendants - pl.Attendants), open = System.Math.Max(0, max - pl.CrewAttendants);
            int x = ox + 473;
            x = Strip(s, x, "S_A", baseCrew, false);
            x = Strip(s, x, "S_B", extra, false);
            Strip(s, x, "S_C", open, extra > 0);
            if (App.Session != null && (P.SecurityFlags & (1 << 6)) != 0) s.Draw(Assets.Texture(Lib, "BADGE"), ox + 420, 352);
        }

        int Strip(Screen2D s, int x, string set, int n, bool joined)
        {
            for (int i = 0; i < n; i++)
            {
                string part = i == 0 ? (joined ? "S_BC_2" : set + "_1") : i == n - 1 ? set + "_3" : set + "_2";
                if (n == 1) part = joined ? "S_BC_2" : set + "_1";
                var t = Assets.Texture(Lib, part);
                s.Draw(t, x - (joined && i == 0 ? 9 : 0), 408);
                x += t.width - (joined && i == 0 ? 9 : 0);
            }
            return x;
        }

        void DrawTechnical(Screen2D s, int ox, Plane pl)
        {
            string[] pics = { "REIFEN", "TURBINE", "SOS", "COCKPIT" };
            Vector2Int[] pos = { V(325, 74), V(444, 42), V(305, 247), V(442, 237) };
            for (int r = 0; r < 4; r++)
            {
                int t = Target(pl, RowItem[r + 4]);
                if (t > 0) s.Draw(Assets.Texture(Lib, pics[r] + (t + 1)), ox + pos[r].x, pos[r].y);
            }
            for (int r = 4; r < 8; r++) DrawStars(s, ox, pl, r);
            var f = App.Ui.Kit.SmallBlack;
            long sum = 0;
            int[] ys = { 58, 137, 216, 295 };
            for (int r = 0; r < 4; r++)
            {
                int item = RowItem[r + 4], t = Target(pl, item);
                if (t == pl.Equipment[item]) continue;
                long c = Equip.ChangeCost(item, pl.Equipment[item], t, pl.Seats);
                sum += c;
                f.Draw(s, ox + 8, ys[r], CostLabel(c));
            }
            if (sum != 0) f.Draw(s, ox + 8, 329, CostLabel(sum));
            // First-class share.
            long total = pl.MaxPassengers + 2L * pl.MaxPassengersFC;
            int pct = total > 0 ? (int)((2L * pl.MaxPassengersFC * 100 / total + 5) / 10) : 0;
            pct = Mathf.Clamp(pct, 0, 10);
            if (pct >= 10) s.Draw(Assets.Texture(Lib, "KL_1"), ox + 117, 374);
            s.Draw(Assets.Texture(Lib, "KL_" + (pct % 10)), ox + 133, 374);
            s.Draw(Assets.Texture(Lib, (pct * 10) + "_PRZ"), ox + 62, 408);
        }
    }
}
