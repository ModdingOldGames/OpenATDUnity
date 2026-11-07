using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Staff;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.RouteBox
{
    /// <summary>
    /// Route office (rooms-and-ui-6 §8): the world map with all routes (grey = not offered, white =
    /// offered, airline colours = rented), the paged route list with filters (all / rentable / mine), the
    /// info sheet of the hovered route, per-airline map toggles. Clicking a route rents or drops it after a
    /// confirmation (<see cref="RentRouteCommand"/>, <see cref="DropRouteCommand"/>).
    /// </summary>
    [Room(RoomIds.RouteBox)]
    [ScreenName("routebox")]
    public sealed class RouteBoxRoom : RoomScreen
    {
        const string Lib = "room/routebox.gli";
        protected override string Library => Lib;
        protected override string Background => "ROUTEBOX";
        const int Rows = 22;
        const int ClickPrev = 10, ClickNext = 11, ClickFilter = 20, ClickRoute = 100, ClickToggle = 30, ClickClips = 200;
        static readonly Vector2Int[] TogglePos = { new Vector2Int(241, 267), new Vector2Int(248, 335), new Vector2Int(304, 258), new Vector2Int(313, 328) };
        static readonly Color32[] PlayerColour = { new Color32(0, 128, 255, 255), new Color32(0, 255, 64, 255), new Color32(255, 0, 0, 255), new Color32(255, 255, 0, 255) };

        World W => App.Session?.World;
        Player P => App.Session?.Local;

        sealed class State { public int Page, Filter; public bool[] Show = { true, false, false, false }; }
        State _st;
        int _hover = -1, _commented = -1;
        int _clickRoute = -1, _clickFilter, _clickToggle;
        List<int> _rows = new List<int>();
        bool[] _offered;

        protected override void OnCreate()
        {
            var p = P;
            if (p != null)
            {
                if (!p.Extensions.TryGetValue("openat.routebox", out var v) || !(v is State s)) p.Extensions["openat.routebox"] = s = new State();
                _st = s;
            }
            else _st = new State();
        }

        protected override void OnRoomEnter() => Refresh();

        void Refresh()
        {
            if (W == null) return;
            int n = W.RouteTable.Count;
            _offered = new bool[n];
            for (int i = 0; i < n; i++) _offered[i] = P.RentRoutes[i].Licensed || RouteOps.IsRentable(W, P, i);
            _rows.Clear();
            for (int i = 0; i < n; i++)
            {
                var r = W.RouteTable[i];
                if (r.From > r.To) continue;
                bool mine = P.RentRoutes[i].Licensed;
                if (_st.Filter == 1 && (mine || !_offered[i])) continue;
                if (_st.Filter == 2 && !mine) continue;
                _rows.Add(i);
            }
            _rows.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(W.City(W.RouteTable[a].From).Name, W.City(W.RouteTable[b].From).Name);
                return c != 0 ? c : string.CompareOrdinal(W.City(W.RouteTable[a].To).Name, W.City(W.RouteTable[b].To).Name);
            });
            int pages = Pages;
            if (_st.Page >= pages) _st.Page = pages - 1;
        }

        int Pages => Mathf.Max(1, (_rows.Count + Rows - 1) / Rows);

        // ------------------------------------------------------------------ map projection (§8.3)

        Vector2 MapPos(int cityId)
        {
            var c = W.City(cityId);
            float x = c.MapX * 1.925f * 42 / 60 + 198, y = c.MapY * 1.925f * 95 / 90 + 150;
            return new Vector2(19 + x, 28 + y);
        }

        /// <summary>Map segments of a route (two when it wraps around the date line).</summary>
        IEnumerable<(Vector2 a, Vector2 b)> Segments(int ri)
        {
            var r = W.RouteTable[ri];
            var a = MapPos(r.From); var b = MapPos(r.To);
            var ca = W.City(r.From); var cb = W.City(r.To);
            if (Mathf.Abs(ca.MapX - cb.MapX) < 180) { yield return (a, b); yield break; }
            // Wrap: each end runs to its panel edge (x 1 / 437 of the panel) at the interpolated height.
            var left = a.x < b.x ? a : b; var right = a.x < b.x ? b : a;
            float leftEdge = 19 + 1, rightEdge = 19 + 437;
            float dl = left.x - leftEdge, dr = rightEdge - right.x;
            float t = dl + dr > 0 ? dl / (dl + dr) : 0.5f;
            float ym = Mathf.Lerp(left.y, right.y, t);
            yield return (left, new Vector2(leftEdge, ym));
            yield return (right, new Vector2(rightEdge, ym));
        }

        static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ input

        bool InfoAdvisor => P != null && P.HasAdvisor(AdvisorType.Informer);
        /// <summary>The paper clips still hold the info sheet and the airline toggles (taken by someone today: both are gone).</summary>
        bool Clips => W != null && W.Items.PaperClip;

        protected override void Hotspots(InputState input)
        {
            if (W == null) return;
            var m = input.Mouse; int mx = m.x - RX, my = m.y;
            _hover = -1;
            // Player toggles.
            for (int p = 0; p < 4 && Clips; p++)
            {
                if (p != P.Index && !InfoAdvisor) continue;
                var tp = TogglePos[p];
                var tex = Assets.Texture(Lib, "P" + (p + 1) + "_01");
                if (Hot(tp.x, tp.y, tp.x + tex.width - 1, tp.y + tex.height - 1, ClickToggle + p)) _clickToggle = p;
            }
            // Map: nearest route line within 10 px.
            bool excluded = (mx >= 9 && mx <= 200 && my >= 268 && my <= 434) || (mx >= 230 && mx <= 361 && my >= 254 && my <= 387);
            if (mx >= 17 && mx <= 434 && my >= 27 && my <= 295 && !excluded)
            {
                float best = 10; int bi = -1;
                var pt = new Vector2(mx, my);
                for (int i = 0; i < W.RouteTable.Count; i++)
                {
                    if (W.RouteTable[i].From > W.RouteTable[i].To) continue;
                    foreach (var s in Segments(i)) { float d = DistToSeg(pt, s.a, s.b); if (d < best) { best = d; bi = i; } }
                }
                if (bi >= 0 && Hot(17, 27, 434, 295, ClickRoute)) { _hover = bi; _clickRoute = bi; }
            }
            // List rows.
            if (mx >= 461 && mx <= 632 && my >= 29 && my < 29 + Rows * 13)
            {
                int k = (my - 29) / 13, idx = _st.Page * Rows + k;
                if (idx < _rows.Count && Hot(461, 29, 632, 314, ClickRoute)) { _hover = _rows[idx]; _clickRoute = _rows[idx]; }
            }
            // The paper clips (item for the glue trade), while nobody has taken them today.
            if (Clips && !P.HasItem(ItemId.PaperClip))
            {
                Hot(0, 245, 36, 287, ClickClips); Hot(186, 276, 214, 322, ClickClips);
                Hot(225, 269, 245, 314, ClickClips); Hot(347, 280, 383, 320, ClickClips);
            }
            // Filters and pages.
            int[,] fr = { { 456, 328, 500, 361 }, { 501, 330, 541, 363 }, { 542, 331, 582, 362 } };
            for (int f = 0; f < 3; f++)
                if (f != _st.Filter && Hot(fr[f, 0], fr[f, 1], fr[f, 2], fr[f, 3], ClickFilter + f, 4600 + f)) _clickFilter = f;
            if (_st.Page > 0) Hot(449, 314, 469, 332, ClickPrev, 0, CursorLook.Left);
            if (_st.Page < Pages - 1) Hot(607, 314, 628, 332, ClickNext, 0, CursorLook.Right);
            Exit(444, 380, 639, 439);
            AdviseHover();
        }

        void AdviseHover()
        {
            if (_hover == _commented) return;
            _commented = _hover;
            if (_hover < 0 || !P.HasAdvisor(AdvisorType.Route)) return;
            var a = RouteOps.Advice(W, P, _hover);
            var raw = Assets.StdText.Get("Advi", a.Text);
            if (raw == null) return;
            string tip = a.Text == 8000 ? DialogText.Format(raw, new object[] { W.City(a.City1).Name, W.City(a.City2).Name })
                : a.Text == 8020 ? raw : DialogText.Format(raw, new object[] { W.City(a.City1).Name });
            var remark = a.Remark >= 0 ? Assets.StdText.Get("Advi", a.Remark) : null;
            if (remark != null) tip += " " + remark;
            App.Advisors.Queue(AdvisorKind.Route, tip, AdvisorUrgency.Comment);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickPrev) { _st.Page--; App.Audio.Play("SOUND/paper.raw"); return; }
            if (id == ClickNext) { _st.Page++; App.Audio.Play("SOUND/paper.raw"); return; }
            if (id >= ClickFilter && id < ClickFilter + 3) { _st.Filter = _clickFilter; _st.Page = 0; Refresh(); return; }
            if (id >= ClickToggle && id < ClickToggle + 4) { _st.Show[_clickToggle] = !_st.Show[_clickToggle]; return; }
            if (id == ClickRoute && _clickRoute >= 0) RouteClicked(_clickRoute);
            if (id == ClickClips)
            {
                var r = W.Execute(new PickUpItemCommand(P.Index, ItemId.PaperClip));
                if (!r.Ok && r.Reason == "inventory-full")
                    Menus.Open(RequestMenu.Create(RequestMenu.Items, null, null), new Vector2Int(App.InputNow.Mouse.x - RX, App.InputNow.Mouse.y), 0.17f);
            }
        }

        void RouteClicked(int ri)
        {
            var r = W.RouteTable[ri];
            var rr = P.RentRoutes[ri];
            object[] args = { W.City(r.From).Name, W.City(r.To).Name, (long)r.Rent / 30, (long)r.Rent };
            var zoom = new Vector2Int(540, 170);
            if (rr.Licensed)
            {
                if (RouteOps.InUse(W, P, ri)) { Menus.Open(RequestMenu.Create(15, null, null), zoom, 0.17f); return; }
                args[2] = (long)rr.Rent / 30; args[3] = (long)rr.Rent;
                Menus.Open(RequestMenu.Create(RequestMenu.KillRoute, args, () => { W.Execute(new DropRouteCommand(P.Index, ri)); Refresh(); }), zoom, 0.17f);
                return;
            }
            if (!_offered[ri]) { Menus.Open(RequestMenu.Create(rr.DaysLow < 7 ? 16 : 14, null, null), zoom, 0.17f); return; }
            int rev = W.ReverseRouteIndex(ri);
            if (RouteOps.NextRank(W, rev >= 0 ? rev : ri) > 3) { Menus.Open(RequestMenu.Create(13, null, null), zoom, 0.17f); return; }
            Menus.Open(RequestMenu.Create(RequestMenu.RentRoute, args, () =>
            {
                if (W.Execute(new RentRouteCommand(P.Index, ri)).Ok) App.Audio.Play("SOUND/paptake.raw");
                Refresh();
            }), zoom, 0.17f);
        }

        // ------------------------------------------------------------------ drawing

        void Line(Screen2D s, Vector2 a, Vector2 b, Color32 c, int thick)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                s.FillRect(new RectInt(RX + Mathf.RoundToInt(p.x) - thick / 2, Mathf.RoundToInt(p.y) - thick / 2, thick, thick), c);
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            if (W == null || _offered == null) return;
            int sel = _hover, selRev = sel >= 0 ? W.ReverseRouteIndex(sel) : -1;
            var grey = new Color32(0x5e, 0x5e, 0x5e, 255); var tan = new Color32(0xd0, 0xa8, 0x90, 255);
            s.PushClip(new RectInt(RX + 19, 28, 418, 241));
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < W.RouteTable.Count; i++)
                {
                    var r = W.RouteTable[i];
                    if (r.From > r.To) continue;
                    bool rented = false;
                    for (int p = 0; p < 4; p++) if (_st.Show[p] && !W.Players[p].IsOut && W.Players[p].RentRoutes[i].Licensed) rented = true;
                    int kind = rented ? 2 : _offered[i] ? 1 : 0;
                    if (kind != pass) continue;
                    int thick = i == sel || i == selRev ? 3 : 1;
                    foreach (var sg in Segments(i))
                    {
                        if (kind == 0) Line(s, sg.a, sg.b, (i & 1) == 0 ? grey : tan, thick);
                        else if (kind == 1) Line(s, sg.a, sg.b, new Color32(255, 255, 255, 255), thick);
                        else
                        {
                            // Several renters: the line is split into coloured parts.
                            var cols = new List<Color32>();
                            for (int p = 0; p < 4; p++) if (_st.Show[p] && !W.Players[p].IsOut && W.Players[p].RentRoutes[i].Licensed) cols.Add(PlayerColour[p]);
                            for (int k = 0; k < cols.Count; k++)
                                Line(s, Vector2.Lerp(sg.a, sg.b, k / (float)cols.Count), Vector2.Lerp(sg.a, sg.b, (k + 1) / (float)cols.Count), cols[k], thick + 1);
                        }
                    }
                }
            foreach (var mc in W.MissionCityIds)
                if (mc != 0) { var p = MapPos(mc); s.Draw(Assets.Texture(Lib, "HERE"), RX + (int)p.x - 3, (int)p.y - 3); }
            s.PopClip();

            // Route list.
            var f = App.Ui.Kit.SmallBlack; var fg = App.Ui.Kit.SmallGrey;
            for (int k = 0; k < Rows; k++)
            {
                int idx = _st.Page * Rows + k;
                if (idx >= _rows.Count) break;
                int ri = _rows[idx];
                var r = W.RouteTable[ri];
                int y = 29 + k * 13;
                bool mine = P.RentRoutes[ri].Licensed;
                if (ri == _hover && (mine || _offered[ri])) s.FillRect(new RectInt(RX + 458, y - 1, 177, 13), new Color32(255, 255, 255, 90));
                if (mine) s.Draw(Assets.Texture(Lib, "HAKEN"), RX + 461, y + 2);
                string t = W.City(r.From).Name + " - " + W.City(r.To).Name;
                var font = mine || _offered[ri] ? f : fg;
                while (t.Length > 1 && font.Measure(t) > 150) t = t.Substring(0, t.Length - 1);
                font.Draw(s, RX + 470, y, t);
            }
            f.Draw(s, RX + 547, 29 + Rows * 13 + 1, (_st.Page + 1) + " / " + Pages, TextAlign.Center);
            if (_st.Page > 0) s.Draw(Assets.Texture(Lib, "LINKS"), RX + 451, 313);
            if (_st.Page < Pages - 1) s.Draw(Assets.Texture(Lib, "RECHTS"), RX + 607, 313);
            s.Draw(Assets.Texture(Lib, "FILTER" + (_st.Filter + 1)), RX + 453, 326);

            if (!Clips)
            {
                // Without the clips the sheet and the toggles have fallen off the board.
                var np = Assets.Texture(Lib, "NOPAPER");
                s.Draw(np, RX, 440 - np.height);
                return;
            }

            // Player toggles.
            var m = App.InputNow.Mouse;
            for (int p = 0; p < 4; p++)
            {
                if (W.Players[p].IsOut) continue;
                var tp = TogglePos[p];
                var tex0 = Assets.Texture(Lib, "P" + (p + 1) + "_01");
                bool hov = m.x - RX >= tp.x && m.x - RX < tp.x + tex0.width && m.y >= tp.y && m.y < tp.y + tex0.height && (p == P.Index || InfoAdvisor);
                s.Draw(Assets.Texture(Lib, "P" + (p + 1) + (_st.Show[p] ? "_03" : hov ? "_02" : "_01")), RX + tp.x, tp.y);
            }
            DrawInfoSheet(s);
        }

        void DrawInfoSheet(Screen2D s)
        {
            if (_hover < 0) return;
            var r = W.RouteTable[_hover];
            var rr = P.RentRoutes[_hover];
            var f = App.Ui.Kit.SmallBlack;
            int x = RX + 30, y = 279, rx = RX + 196;
            string Std(string g, int id) => Assets.StdText.Get(g, id) ?? "";
            f.Draw(s, x, y, W.City(r.From).Name + " - " + W.City(r.To).Name); y += 14;
            void Row(string l, string v) { f.Draw(s, x, y, l); f.Draw(s, rx, y, v, TextAlign.Right); y += 11; }
            Row(Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(r.From, r.To) / 1000));
            Row(Std("Rout", 998), UiKit.Thousands((long)(r.Factor * 100)));
            Row(Std("Rout", 999), UiKit.Thousands(r.Demand));
            Row(Std("Rout", 1006), UiKit.Money(rr.Licensed ? rr.Rent : r.Rent));
            if (rr.Licensed) Row(Std("Rout", 1003), rr.RouteShare + "%");
            bool any = false;
            foreach (var q in W.Players) if (!q.IsOut && q.RentRoutes[_hover].Licensed) any = true;
            if (!any) return;
            y += 3;
            f.Draw(s, x, y, Std("Rout", 1016)); y += 11;
            foreach (var q in W.Players)
            {
                var qr = q.RentRoutes[_hover];
                if (q.IsOut || !qr.Licensed || y > 425) continue;
                bool known = q == P || InfoAdvisor;
                string name = known ? q.Airline : Std("Rout", 997);
                Row(qr.Rank + ". " + name, known && InfoAdvisor ? qr.RouteShare + "%" : "");
            }
        }
    }
}
