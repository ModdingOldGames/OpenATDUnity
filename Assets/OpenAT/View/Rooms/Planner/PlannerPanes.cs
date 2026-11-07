using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>Pane contents of the planner blocks (rooms-and-ui-5b §4, §5).</summary>
    public abstract partial class PlannerRoom
    {
        protected const string Std2 = "gli/glstd.gli";
        static readonly Color32 Highlight = new Color32(32, 160, 255, 60);
        static readonly Color32 GreyOut = new Color32(120, 120, 120, 120);

        protected BitmapFont TitleFont => IsLaptop ? Kit.NormalGreen ?? Kit.SmallBlack : Kit.SmallBlack;
        protected BitmapFont F => Kit.SmallBlack;

        /// <summary>Sched sprite frame n (frame 0 = SCHED01, the laptop grid).</summary>
        protected Texture2D Sched(int frame) => Assets.Texture(Std2, "SCHED" + (frame + 1).ToString("00"));

        protected static string Clip(BitmapFont f, string t, int w)
        {
            if (t == null) return "";
            if (f.Measure(t) <= w) return t;
            while (t.Length > 1 && f.Measure(t + "..") > w) t = t.Substring(0, t.Length - 1);
            return t + "..";
        }

        protected string Money(long v) => UiKit.Money(v);

        string CityName(int id) => W.City(id)?.Name ?? "?";
        string Code(int id) => W.City(id)?.ShortCode ?? "?";

        /// <summary>"From - To" fitting in <paramref name="w"/> px: full names, name–code, code–code (§4.3).</summary>
        protected string PairText(BitmapFont f, int from, int to, int w)
        {
            string a = CityName(from) + " - " + CityName(to);
            if (f.Measure(a) <= w) return a;
            a = CityName(from) + " - " + Code(to);
            if (f.Measure(a) <= w) return a;
            return Code(from) + " - " + Code(to);
        }

        string DateText(int date, int last)
        {
            string wd(int d) => Std("Schd", 3010 + Weekday(W, d));
            if (date == last) return wd(last);
            return Std("Schd", 3009) + " " + wd(last);
        }

        // ------------------------------------------------------------------ blocks

        /// <summary>Draws all blocks back to front, then the carried post-it.</summary>
        protected void DrawBlocks(Screen2D s)
        {
            var mouse = App.InputNow.Mouse;
            var bl = Blocks;
            for (int i = bl.Count - 1; i >= 0; i--)
            {
                var b = bl[i];
                var sp = BlockScreen(b);
                var l = mouse - sp;
                DrawBlockChrome(s, b, sp.x, sp.y, l);
                if (!BlockContentVisible(b)) continue;
                DrawPane(s, b, false, sp, l);
                if (b.Double) DrawPane(s, b, true, sp, l);
                DrawBlockOverlay(s, b, sp.x, sp.y, l);
            }
        }

        protected virtual bool BlockContentVisible(Block b) => true;

        /// <summary>The post-it on the cursor (§6.1), snapped over a visible grid.</summary>
        protected void DrawCarry(Screen2D s)
        {
            if (Carry.Empty) return;
            var m = App.InputNow.Mouse;
            var b = HitBlock(m);
            var pl = b != null && GridVisible(b) ? SelectedPlane(b) : null;
            int cells = CarryCells(pl ?? SelectedPlane(TopBlock));
            int x = m.x - cells * CellW / 2, y = m.y - CellH / 2;
            if (pl != null && SnapPostIt(b, m - BlockScreen(b), out int row, out int col))
            {
                var o = BlockScreen(b) + Geom(b, false).Client;
                x = o.x + GridX + col * CellW + (IsLaptop ? 1 : 0);
                y = o.y + GridY + row * CellH;
            }
            if (!CarryCitiesPublic(out int from, out int to)) return;
            DrawBar(s, x, y, Carry.Kind, cells, Code(from), Code(to), 0, false, true);
        }

        bool CarryCitiesPublic(out int from, out int to)
        {
            from = to = 0;
            switch (Carry.Kind)
            {
                case FlightKind.Route: { var r = W.RouteById(Carry.Id); if (r == null) return false; from = r.From; to = r.To; return true; }
                case FlightKind.Contract: { var o = P.Contracts.Find(Carry.Id); if (o == null) return false; from = o.From; to = o.To; return true; }
                case FlightKind.Freight: { var o = P.Freights.Find(Carry.Id); if (o == null) return false; from = o.From; to = o.To; return true; }
            }
            return false;
        }

        /// <summary>A flight bar of <paramref name="cells"/> hour cells starting at (x,y) (§5.2).</summary>
        protected void DrawBar(Screen2D s, int x, int y, int kind, int cells, string fromCode, string toCode, int okay, bool locked, bool full = true, int firstCell = 0, int lastCell = int.MaxValue)
        {
            int baseFrame = kind == FlightKind.Route ? 1 : kind == FlightKind.Contract ? 6 : kind == FlightKind.Freight ? 20 : 11;
            for (int i = 0; i < cells; i++)
            {
                if (i < firstCell || i > lastCell) continue;
                int frame;
                if (kind == FlightKind.Auto)
                    frame = i == 0 ? 11 : i == cells - 1 ? 15 : (i % 2 == 1 ? 13 : 14);
                else
                    frame = baseFrame + (i == 0 ? 0 : i == cells - 1 ? 4 : i == 1 ? 1 : i == cells - 2 ? 3 : 2);
                s.Draw(Sched(frame), x + i * CellW, y);
                if (okay != 0 && (kind == FlightKind.Contract || kind == FlightKind.Freight))
                    s.Draw(Sched(i == 0 ? 41 : i == cells - 1 ? 45 : 44), x + i * CellW, y);
            }
            if (locked && cells > 1 && firstCell <= 1 && lastCell >= 1) s.Draw(Sched(17), x + CellW, y + 1);
            DrawBarCodes(s, x, y, cells, fromCode, toCode, firstCell, lastCell);
        }

        /// <summary>
        /// The two city codes of a bar (§5.2): departure on the upper line from the bar's left end, arrival on
        /// the lower line right-aligned at its right end, so both fit even on a short bar. Flights under 4 h
        /// use the very small font (shifted 2 px down / right); flights over 6 h move both codes one cell
        /// inward. A code is only drawn on the row of its own end of the bar, and only if it stays on that row
        /// (a bar that starts late in the day or lands early in the morning loses that code).
        /// </summary>
        void DrawBarCodes(Screen2D s, int x, int y, int cells, string fromCode, string toCode, int firstCell, int lastCell)
        {
            int hours = cells - 1;
            bool tiny = hours < 4, inward = hours > 6;
            var f = tiny ? Kit.VerySmall : Kit.SmallBlack;
            if (f == null) return;
            int nudge = tiny ? 2 : 0;
            // Departure: needs the first cells of the bar (start + 2/3/4 hours) on the start row.
            int fromNeed = tiny ? 2 : inward ? 4 : 3;
            if (firstCell == 0 && lastCell >= Math.Min(fromNeed, hours))
                f.Draw(s, x + (inward ? CellW : 2), y + 1 + nudge, fromCode);
            // Arrival: on the landing row, which must hold at least three hours before the landing cell
            // (two for the very small font).
            int land = cells - 1;
            int rowHours = firstCell > 0 ? land - firstCell : int.MaxValue;
            if (lastCell >= land && (rowHours > 2 || (tiny && rowHours == 2)))
                f.Draw(s, x + cells * CellW + (inward ? -4 : 1) + nudge, y + 10, toCode, TextAlign.Right);
        }

        // ------------------------------------------------------------------ panes

        void DrawPane(Screen2D s, Block b, bool right, Vector2Int bs, Vector2Int l)
        {
            var g = Geom(b, right);
            var pane = b.PaneOf(right);
            var o = bs + g.Client;
            var cm = l - g.Client;
            bool tip = right && TipBlock == b && TipPlane != null && TipEntry >= 0;
            // A cities window always shows the map on the right; with the hovered city (or the one open on
            // the left) marked, named in the title bar and its numbers below (rooms-and-ui-5b §4.6).
            if (right && pane.Type == BlockType.None && b.Left.Type == BlockType.Cities)
            {
                int cid = CityTipBlock == b && CityTipId >= 0 ? CityTipId : !b.Left.IsIndex ? b.Left.SelectedId : -1;
                DrawCityTip(s, o, bs + g.Title, cid >= 0 ? W.City(cid) : null);
                return;
            }
            if (pane.Type == BlockType.None && !tip) return;
            bool grid = !right && pane.IsDetail(BlockType.Planes) && pane.Page == 0;
            if (IsLaptop && !grid)
            {
                int wt = tip ? b.Left.Type : pane.Type;
                if (Assets.Has("room/globe_b.gli", "BACK" + wt)) s.Draw(Assets.Texture("room/globe_b.gli", "BACK" + wt), o.x - 1, o.y - 2);
            }
            s.PushClip(new RectInt(o.x - 2, o.y - 2, ClientW + 4, ClientH + 2));
            if (tip) DrawFlightTip(s, o);
            else if (pane.IsIndex) DrawList(s, b, pane, right, o, cm - g.InputShift);   // rows hit from the original's mouse origin
            else
                switch (pane.Type)
                {
                    case BlockType.Planes: DrawPlaneDetail(s, b, pane, o, cm); break;
                    case BlockType.Orders: DrawOrderDetail(s, pane, o, cm); break;
                    case BlockType.Freight: DrawFreightDetail(s, pane, o, cm); break;
                    case BlockType.Routes: DrawRouteDetail(s, pane, o, cm); break;
                    case BlockType.Cities: DrawCityDetail(s, pane, o, cm); break;
                    case BlockType.Info: DrawInfoDetail(s, pane, o, cm); break;
                }
            s.PopClip();
            // Title and page number.
            if (!tip)
            {
                string title = PaneTitle(pane);
                var tf = TitleFont;
                var tfont = pane.IsDetail(BlockType.Planes) && P.Planes.Find(pane.SelectedId) is Plane tp && PlaneRed(tp) ? Kit.SmallRed : tf;
                int tx = bs.x + g.Title.x + (IsLaptop && right && pane.IsIndex ? 60 : 0);
                tfont.Draw(s, tx, bs.y + g.Title.y, Clip(tfont, title, IsLaptop ? 130 : 170));
                if (!IsLaptop) s.FillRect(new RectInt(bs.x + g.Title.x, bs.y + g.Title.y + 12, 170, 1), new Color32(0x30, 0x30, 0x30, 255));
                // The page number is always shown (also "1/1"), in the title font (green on the laptop).
                if (pane.Pages >= 1)
                    tf.Draw(s, bs.x + g.PageNo.x, bs.y + g.PageNo.y, (pane.Page + 1) + "/" + pane.Pages, TextAlign.Center);
            }
        }

        string PaneTitle(Pane pane)
        {
            if (pane.IsIndex)
                switch (pane.Type)
                {
                    case BlockType.Cities: return Std("Schd", 1400);
                    case BlockType.Planes: return Std("Schd", 1000);
                    case BlockType.Orders: return Std("Schd", 1200);
                    case BlockType.Routes: return Std("Schd", 1100);
                    case BlockType.Freight: return Std("Schd", 1300);
                    case BlockType.Info: return Std("Schd", 1500);
                }
            switch (pane.Type)
            {
                case BlockType.Cities: return CityName(pane.SelectedId);
                case BlockType.Planes: return P.Planes.Find(pane.SelectedId)?.Name ?? "";
                case BlockType.Orders: { var c = P.Contracts.Find(pane.SelectedId); return c == null ? "" : Code(c.From) + " - " + Code(c.To); }
                case BlockType.Freight: { var c = P.Freights.Find(pane.SelectedId); return c == null ? "" : Code(c.From) + " - " + Code(c.To); }
                case BlockType.Routes: { if (pane.SelectedId < 0 || pane.SelectedId >= W.RouteTable.Count) return ""; var r = W.RouteTable[pane.SelectedId]; return Code(r.From) + " - " + Code(r.To); }
                // The daily balance is titled with the day it covers (yesterday; the original names today).
                case BlockType.Info: return pane.SelectedId == 1 ? Std("XPrt", 2100) + " " + Std("Schd", 3010 + Weekday(W, W.Clock.Date - 1)) : Std("XPrt", 2000 + pane.SelectedId);
            }
            return "";
        }

        // ------------------------------------------------------------------ lists

        void DrawList(Screen2D s, Block b, Pane pane, bool right, Vector2Int o, Vector2Int cm)
        {
            int ps = BlockType.ListPageSize(pane.Type), rh = BlockType.RowHeight(pane.Type);
            int first = pane.Page * ps;
            int hoverRow = cm.x >= 0 && cm.x < ClientW && cm.y >= 0 ? cm.y / rh : -1;
            switch (pane.Type)
            {
                case BlockType.Cities:
                    {
                        var l = CityList();
                        for (int k = 0; k < ps && first + k < l.Count; k++)
                        {
                            var c = l[first + k];
                            bool own = P.BranchAt(c.Index).Owned;
                            if (k == hoverRow) s.FillRect(new RectInt(o.x, o.y + k * rh, ClientW, rh), Highlight);
                            (own ? Kit.SmallBlack : Kit.SmallGrey).Draw(s, o.x + 2, o.y + k * rh + 1, Clip(F, c.Name, 168));
                        }
                        break;
                    }
                case BlockType.Info:
                    for (int k = 0; k < 4; k++)
                    {
                        if (k == hoverRow) s.FillRect(new RectInt(o.x, o.y + k * rh, ClientW, rh), Highlight);
                        F.Draw(s, o.x + 2, o.y + k * rh + 1, Std("XPrt", 2000 + k));
                    }
                    break;
                case BlockType.Planes:
                    {
                        var l = PlaneList(b);
                        int need = 0, from = 0, to = 0;
                        bool filter = (!Carry.Empty && CarryCitiesPublic(out from, out to));
                        if (filter && Carry.Kind == FlightKind.Contract) need = P.Contracts.Find(Carry.Id)?.Persons ?? 0;
                        for (int k = 0; k < ps && first + k < l.Count; k++)
                        {
                            var pl = l[first + k];
                            int y = o.y + k * rh;
                            if (k == hoverRow) s.FillRect(new RectInt(o.x, y, ClientW, rh), Highlight);
                            bool grey = filter && (W.Distance(from, to) > pl.Range * 1000 || Duration(pl, from, to) >= 24 || need > pl.MaxPassengers + pl.MaxPassengersFC);
                            var f = grey ? Kit.SmallGrey : PlaneRed(pl) ? Kit.SmallRed : F;
                            // Name at x 0, condition left-aligned at x 123, location line below at x 0, all in the
                            // row's font (black, or red for a crew shortfall/problem); verified on the running original.
                            f.Draw(s, o.x, y + 1, Clip(f, pl.Name, 115));
                            f.Draw(s, o.x + 123, y + 1, UiKit.Unit(UiKit.UnitPercent, pl.Condition));
                            string where = PlaneLocation.IsOnGround(pl.Location)
                                ? Std("Schd", 1105) + " " + CityName(pl.Location)
                                : Std("Schd", 1102) + " " + CityName(NextDestination(pl));
                            f.Draw(s, o.x, y + 11, Clip(f, where, 168));
                        }
                        break;
                    }
                case BlockType.Orders:
                case BlockType.Freight:
                case BlockType.Routes:
                    DrawRightList(s, b, pane, o, first, ps, rh, hoverRow);
                    break;
            }
        }

        int NextDestination(Plane pl)
        {
            int abs = W.Clock.Date * 24 + W.Clock.Hour;
            foreach (var e in pl.Plan.Entries) if (e.Kind != 0 && e.StartAbs <= abs && e.LandAbs >= abs) return e.To;
            return pl.Plan.StartCity;
        }

        void DrawRightList(Screen2D s, Block b, Pane pane, Vector2Int o, int first, int ps, int rh, int hoverRow)
        {
            var info = Assets.Texture(Std2, "INFO01");
            bool gridOpen = GridVisible(b);
            for (int k = 0; k < ps; k++)
            {
                int idx = first + k;
                int id = -1, from = 0, to = 0;
                string line1Right = "", line2 = "", line2Right = "", post = "POST_SB";
                switch (pane.Type)
                {
                    case BlockType.Orders:
                        {
                            var l = OrderList();
                            if (idx >= l.Count) continue;
                            var c = l[idx]; id = c.Id; from = c.From; to = c.To;
                            line1Right = "(" + c.Persons + ")";
                            line2 = DateText(c.Date, c.LastDate);
                            line2Right = Money(c.Premium);
                            post = c.InPlan != 0 && c.Okay == 0 ? "POST_SG2" : "POST_SG";
                            break;
                        }
                    case BlockType.Freight:
                        {
                            var l = FreightList();
                            if (idx >= l.Count) continue;
                            var c = l[idx]; id = c.Id; from = c.From; to = c.To;
                            line1Right = "(" + c.TonsOpen + "t)";
                            line2 = DateText(c.Date, c.LastDate);
                            line2Right = Money(c.Premium);
                            post = c.Okay == 0 ? "POST_SB2" : "POST_SR";
                            break;
                        }
                    case BlockType.Routes:
                        {
                            var l = RouteList();
                            if (idx >= l.Count) continue;
                            id = l[idx];
                            var r = W.RouteTable[id]; from = r.From; to = r.To;
                            int n = 0;
                            foreach (var pl in P.Planes.Items) foreach (var e in pl.Plan.Entries) if (e.Kind == FlightKind.Route && e.ObjectId == r.Id) n++;
                            line2 = n + Std("Rout", 1015);
                            line2Right = Money(P.RentRoutes[id].TicketPrice);
                            post = "POST_SB";
                            break;
                        }
                }
                int y = o.y + k * rh;
                int flags = RowFlags(b, pane.Type, id);
                bool grey = gridOpen && flags != 0;
                if (k == hoverRow && !grey) s.FillRect(new RectInt(o.x, y, ClientW, rh), Highlight);
                var f = grey ? Kit.SmallGrey : F;
                s.Draw(Assets.Texture(Std2, post), o.x + 1, y + 2);
                s.Draw(info, o.x + 3, y + 12);
                int rw = f.Measure(line1Right);
                f.Draw(s, o.x + 16, y + 1, PairText(f, from, to, Math.Min(142, 150 - rw)));
                f.Draw(s, o.x + 168, y + 1, line1Right, TextAlign.Right);
                f.Draw(s, o.x + 16, y + 12, line2);
                f.Draw(s, o.x + 168, y + 12, line2Right, TextAlign.Right);
            }
        }

        // ------------------------------------------------------------------ plane detail

        void DrawPlaneDetail(Screen2D s, Block b, Pane pane, Vector2Int o, Vector2Int cm)
        {
            var pl = P.Planes.Find(pane.SelectedId);
            if (pl == null) return;
            switch (pane.Page)
            {
                case 0: DrawGrid(s, b, pl, o, cm); break;
                case 1:
                    {
                        // Technical data (rooms-and-ui-5b §4.2): labels at x 2, values left-aligned at x 75,
                        // fixed rows with gaps between type, figures, crew and build year; units from the
                        // install's unit table.
                        void Row(int y, string label, string value) { F.Draw(s, o.x + 2, o.y + y, label); F.Draw(s, o.x + 75, o.y + y, value); }
                        F.Draw(s, o.x + 2, o.y + 4, Clip(F, (pl.Maker + " " + pl.Model).Trim(), 168));
                        Row(25, Std("Plne", 1000), pl.MaxPassengers + " (" + pl.MaxPassengersFC + ")");
                        F.Draw(s, o.x + 2, o.y + 37, Std("Plne", 1017));
                        F.Draw(s, o.x + 75, o.y + 36, UiKit.Unit(UiKit.UnitT, pl.Seats / 10));
                        Row(48, Std("Plne", 1001), UiKit.Unit(UiKit.UnitKm, pl.Range));
                        Row(60, Std("Plne", 1002), UiKit.Unit(UiKit.UnitKmh, pl.Speed));
                        Row(73, Std("Plne", 1008), UiKit.Unit(UiKit.UnitL, pl.Tank));
                        Row(85, Std("Plne", 1009), UiKit.Unit(UiKit.UnitLh, pl.Burn));
                        Row(107, Std("Plne", 1041), DialogText.Format(Std("Plne", 1014), new object[] { (long)pl.CrewPilots, (long)pl.Pilots }));
                        Row(119, Std("Plne", 1011), DialogText.Format(Std("Plne", 1014), new object[] { (long)pl.CrewAttendants, (long)pl.MaxAttendants }));
                        Row(136, Std("Plne", 1013), pl.BuildYear.ToString());
                        bool hov = cm.x >= 0 && cm.x <= 169 && cm.y >= 155 && cm.y <= 166;
                        if (hov) s.FillRect(new RectInt(o.x, o.y + 154, 170, 13), Highlight);
                        F.Draw(s, o.x + 2, o.y + 155, Std("Plne", 1030));
                        break;
                    }
                case 2:
                    {
                        // Crew page (rooms-and-ui-5b §4.2): heading (2,4), "pilots" (10,17), then the assigned
                        // workers by name and one "missing" line per empty seat at x 18, 13 px apart, in the
                        // normal grey font; then "attendants" the same way.
                        var grey = Kit.NormalGrey ?? Kit.SmallGrey;
                        int y = o.y + 4;
                        F.Draw(s, o.x + 2, y, Std("Plne", 1040)); y += 13;
                        F.Draw(s, o.x + 10, y, Std("Plne", 1041)); y += 13;
                        foreach (var wk in W.Workers.Workers)
                            if (wk.Employer == P.Index && wk.IsPilot && wk.PlaneId == pl.Id) { grey.Draw(s, o.x + 18, y, wk.Name ?? ""); y += 13; }
                        for (int i = pl.CrewPilots; i < pl.Pilots; i++) { grey.Draw(s, o.x + 18, y, Std("Plne", 1045)); y += 13; }
                        F.Draw(s, o.x + 10, y, Std("Plne", 1042)); y += 13;
                        foreach (var wk in W.Workers.Workers)
                            if (wk.Employer == P.Index && wk.IsAttendant && wk.PlaneId == pl.Id) { grey.Draw(s, o.x + 18, y, wk.Name ?? ""); y += 13; }
                        for (int i = pl.CrewAttendants; i < pl.Attendants; i++) { grey.Draw(s, o.x + 18, y, Std("Plne", 1045)); y += 13; }
                        break;
                    }
                case 3:
                    {
                        int y = o.y;
                        F.Draw(s, o.x, y, Std("Plne", 1050)); y += 16;
                        void Row(string label, string value) { F.Draw(s, o.x, y, label); F.Draw(s, o.x + 168, y, value, TextAlign.Right); y += 12; }
                        Row(Std("Plne", 1051), UiKit.Thousands(pl.TotalPassengers));
                        Row(Std("Plne", 1052), UiKit.Unit(UiKit.UnitKm, pl.Kilometers));
                        Row(Std("Plne", 1053), pl.Load + "% / " + pl.LoadFC + "%");
                        y += 6;
                        long sum = pl.BalanceSum;
                        F.Draw(s, o.x, y, Std("XPrt", 2002));
                        (sum < 0 ? Kit.SmallRed : F).Draw(s, o.x + 168, y, Money(sum), TextAlign.Right);
                        break;
                    }
                default:
                    F.Draw(s, o.x, o.y, Clip(F, (pl.Maker + " " + pl.Model).Trim(), 168));
                    F.DrawBlock(s, new RectInt(o.x, o.y + 14, 168, 150), pl.Comment ?? "", Color.white);
                    break;
            }
        }

        /// <summary>The flight-plan week grid (§5).</summary>
        void DrawGrid(Screen2D s, Block b, Plane pl, Vector2Int o, Vector2Int cm)
        {
            int today = W.Clock.Date;
            if (IsLaptop) s.Draw(Sched(0), o.x + 21, o.y - 2);
            else s.Draw(Assets.Texture("room/globe_a.gli", "TIMETAB1"), o.x + 21, o.y - 2);
            // Day labels: short weekday names in the small black font at (2, 19 + 19·row), today first.
            for (int d = 0; d < 7; d++)
                F.Draw(s, o.x + 2, o.y + 19 + 19 * d, Std("Schd", 3000 + Weekday(W, today + d)));
            // Past time of today.
            int past = W.Clock.MinuteOfDay / 10;
            s.FillRect(new RectInt(o.x + GridX, o.y + GridY, Math.Min(144, past), CellH), new Color32(0, 0, 0, 80));
            // Breakdown bar.
            if (pl.Problem > 0)
            {
                int start = W.Clock.Hour, len = pl.Problem;
                for (int h = 0; h < len && start + h < 7 * 24; h++)
                {
                    int abs = start + h;
                    s.FillRect(new RectInt(o.x + GridX + abs % 24 * CellW, o.y + GridY + abs / 24 * CellH, CellW, CellH), new Color32(255, 0, 0, 70));
                }
            }
            // Date window of the carried / hovered contract (§5.3).
            int winFrom = -1, winTo = -1;
            if (Carry.Kind == FlightKind.Contract) { var c = P.Contracts.Find(Carry.Id); if (c != null) { winFrom = c.Date; winTo = c.LastDate; } }
            else if (Carry.Kind == FlightKind.Freight) { var c = P.Freights.Find(Carry.Id); if (c != null) { winFrom = c.Date; winTo = c.LastDate; } }
            if (winFrom >= 0)
                for (int d = 0; d < 7; d++)
                    if (today + d < winFrom || today + d > winTo) s.Draw(Sched(51), o.x + GridX - 1, o.y + GridY + d * CellH);

            var f = pl.Plan.Entries;
            int first = today * 24, last = first + 7 * 24 - 1;
            for (int i = 0; i < FlightPlan.Size; i++)
            {
                var e = f[i];
                if (e.Kind == 0 || e.LandAbs < first || e.StartAbs > last) continue;
                int cells = e.LandAbs - e.StartAbs + 1;
                bool locked = !e.IsFuture(today, W.Clock.Hour);
                // Bars may cross midnight: draw one segment per day row.
                int abs = e.StartAbs;
                while (abs <= e.LandAbs)
                {
                    int rowStart = abs - abs % 24;
                    int segEnd = Math.Min(e.LandAbs, rowStart + 23);
                    int row = rowStart / 24 - today;
                    if (row >= 0 && row < 7)
                    {
                        int x = o.x + GridX + (e.StartAbs - rowStart) * CellW;
                        int y = o.y + GridY + row * CellH;
                        DrawBar(s, x, y, e.Kind, cells, Code(e.From), Code(e.To), e.Okay, locked, true, abs - e.StartAbs, segEnd - e.StartAbs);
                        if (e.GateWarning && abs == e.StartAbs) s.Draw(Sched(18), o.x + GridX + (abs - rowStart) * CellW, y + 2);
                    }
                    abs = segEnd + 1;
                }
            }
            // Buttons.
            if (IsLaptop) s.Draw(Sched(40), o.x + 24, o.y + 150);
            else s.Draw(Sched(54), o.x + 22, o.y + 150);
            bool ext = cm.x >= 22 && cm.x <= 58 && cm.y >= 150 && cm.y <= 164, del = cm.x >= 135 && cm.x <= 170 && cm.y >= 150 && cm.y <= 164;
            if (ext) s.Draw(Sched(IsLaptop ? 52 : 55), o.x + (IsLaptop ? 24 : 22), o.y + 150);
            if (del) s.Draw(Sched(IsLaptop ? 53 : 56), o.x + (IsLaptop ? 133 : 131), o.y + 150);
        }

        /// <summary>Right-pane tip of the hovered flight (§4.6).</summary>
        void DrawFlightTip(Screen2D s, Vector2Int o)
        {
            var e = TipPlane.Plan.Entries[TipEntry];
            int y = o.y;
            string title = e.Kind == FlightKind.Route ? Std("Rout", 900) : e.Kind == FlightKind.Contract ? Std("Auft", 900) : e.Kind == FlightKind.Freight ? Std("Auft", 902) : Std("Auft", 901);
            TitleFont.Draw(s, o.x, y, title); y += 16;
            F.Draw(s, o.x, y, PairText(F, e.From, e.To, 168)); y += 14;
            void Row(string label, string value) { F.Draw(s, o.x, y, label); F.Draw(s, o.x + 168, y, value, TextAlign.Right); y += 12; }
            Row(Std("Schd", 3000 + Weekday(W, e.StartDate)), e.StartHour + ":00 - " + e.LandHour + ":00");
            Row(Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(e.From, e.To) / 1000));
            if (e.Kind == FlightKind.Freight) Row(Std("Auft", 1008), UiKit.Unit(UiKit.UnitT, e.Passengers));
            else if (e.Kind != FlightKind.Auto) Row(Std("Auft", 1000), e.Passengers + " / " + e.PassengersFC);
            if (e.Kind == FlightKind.Route) Row(Std("Rout", 1001), Money(e.TicketPrice));
            if (e.Kind == FlightKind.Contract)
            {
                var c = P.Contracts.Find(e.ObjectId);
                if (c != null) Row(Std("Auft", 1001), Money(c.Premium));
            }
            Row(Std("Auft", 1004), Money(W.FuelCost.Estimate(W, P, e.From, e.To, TipPlane.Burn, TipPlane.Speed)));
            if (e.Okay != 0) Kit.SmallRed.DrawBlock(s, new RectInt(o.x, y + 4, 168, 40), Std("Auft", 2320 + e.Okay), Color.white);
        }

        /// <summary>City tip in the right pane: small world map with the city, population, rent, branch.</summary>
        void DrawCityTip(Screen2D s, Vector2Int o, Vector2Int title, Sim.Data.City c)
        {
            // Map at client (-4,-2) on the laptop, (-9,-11) in the filofax; marker at the city's map position;
            // "Name (code)" in the title bar; population and office rent at y 105/116 with values at x 90;
            // then the airlines with a branch there by rank (all airlines at the home airport).
            var map = IsLaptop ? Assets.Texture("room/globe_b.gli", "KARTE") : Assets.Texture("room/globe_a.gli", "KARTE");
            var mo = IsLaptop ? new Vector2Int(-4, -2) : new Vector2Int(-9, -11);
            s.Draw(map, o.x + mo.x, o.y + mo.y);
            if (c == null) return;
            var mapOrigin = o + (IsLaptop ? new Vector2Int(-18, -20) : new Vector2Int(-9, -11));
            int px = mapOrigin.x + c.MapX * 34 / 60 + 97, py = mapOrigin.y + c.MapY * 73 / 90 + 80;
            s.FillRect(new RectInt(px - 1, py - 2, 3, 5), new Color32(255, 255, 255, 255));
            s.FillRect(new RectInt(px - 2, py - 1, 5, 3), new Color32(255, 255, 255, 255));
            TitleFont.Draw(s, title.x, title.y, Clip(TitleFont, c.Name + " (" + c.ShortCode + ")", IsLaptop ? 130 : 170));
            int y = o.y - 15 + 120;
            F.Draw(s, o.x, y, Std("City", 1000)); F.Draw(s, o.x + 90, y, c.Population.ToString());
            var br = P.BranchAt(c.Index);
            F.Draw(s, o.x, y + 11, Std("City", 1001)); F.Draw(s, o.x + 90, y + 11, Money(br.Owned ? br.Rent : c.OfficeRent));
            bool home = c.Id == W.HomeCity;
            for (int k = 0; k < Player.Count; k++)
            {
                var q = W.Players[k];
                if (home) F.Draw(s, o.x, y + 2 + 11 * (k + 2), q.Airline ?? "");
                else
                {
                    var qb = q.BranchAt(c.Index);
                    if (qb.Owned && qb.Rank > 0) F.Draw(s, o.x, y + 2 + 11 * (qb.Rank + 1), qb.Rank + "." + q.Airline);
                }
            }
        }

        // ------------------------------------------------------------------ order / route / city details

        void DrawOrderDetail(Screen2D s, Pane pane, Vector2Int o, Vector2Int cm)
        {
            var c = P.Contracts.Find(pane.SelectedId);
            if (c == null) return;
            if (pane.Page == 1) { F.DrawBlock(s, new RectInt(o.x, o.y, 168, 160), Std("Auft", 2500), Color.white); return; }
            // Full city names at y 1; rows at y 28 (passengers), 40 (date), 53 (premium), 66 (penalty), 92
            // (distance) with values left-aligned at x 75 (rooms-and-ui-5b §4.4, verified live).
            F.Draw(s, o.x, o.y + 1, CityName(c.From) + " - " + CityName(c.To));
            void Row(int y, string label, string value) { F.Draw(s, o.x, o.y + y, label); F.Draw(s, o.x + 75, o.y + y, value); }
            Row(28, Std("Auft", 1000), c.Persons.ToString());
            Row(40, Std("Auft", 1003), DateText(c.Date, c.LastDate));
            Row(53, Std("Auft", 1001), Money(c.Premium));
            Row(66, Std("Auft", 1002), Money(c.Penalty));
            Row(92, Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(c.From, c.To) / 1000));
            if (c.InPlan == PlanState.NotPlanned)
            {
                bool hov = cm.x >= 0 && cm.x <= 169 && cm.y >= 118 && cm.y <= 134;
                if (hov) { s.FillRect(new RectInt(o.x, o.y + 118, 170, 17), Highlight); App.Ui.Cursor = CursorLook.Hot; }
                s.Draw(Sched(57), o.x, o.y + 118);
                F.DrawBlock(s, new RectInt(o.x + 20, o.y + 118, 150, 30), Std("Auft", c.Penalty > 0 ? 2401 : 2400), Color.white);
            }
        }

        void DrawFreightDetail(Screen2D s, Pane pane, Vector2Int o, Vector2Int cm)
        {
            var c = P.Freights.Find(pane.SelectedId);
            if (c == null) return;
            if (pane.Page == 1) { F.DrawBlock(s, new RectInt(o.x, o.y, 168, 160), Std("Auft", 2501), Color.white); return; }
            int y = o.y;
            F.Draw(s, o.x, y, PairText(F, c.From, c.To, 168)); y += 16;
            void Row(string label, string value) { F.Draw(s, o.x, y, label); F.Draw(s, o.x + 168, y, value, TextAlign.Right); y += 13; }
            Row(Std("Auft", 1008), UiKit.Unit(UiKit.UnitT, c.Tons));
            Row(Std("Auft", 1010), UiKit.Unit(UiKit.UnitT, c.TonsLeft));
            Row(Std("Auft", 1009), UiKit.Unit(UiKit.UnitT, c.TonsOpen));
            Row(Std("Auft", 1003), DateText(c.Date, c.LastDate));
            Row(Std("Auft", 1001), Money(c.Premium));
            Row(Std("Auft", 1002), Money(c.Penalty));
            Row(Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(c.From, c.To) / 1000));
            if (c.InPlan == 0 && c.TonsLeft == c.Tons)
            {
                bool hov = cm.x >= 0 && cm.x <= 169 && cm.y >= 135 && cm.y <= 151;
                if (hov) { s.FillRect(new RectInt(o.x, o.y + 135, 170, 17), Highlight); App.Ui.Cursor = CursorLook.Hot; }
                s.Draw(Sched(57), o.x, o.y + 135);
                F.DrawBlock(s, new RectInt(o.x + 20, o.y + 135, 150, 30), Std("Auft", c.Penalty > 0 ? 2401 : 2400), Color.white);
            }
        }

        void DrawRouteDetail(Screen2D s, Pane pane, Vector2Int o, Vector2Int cm)
        {
            int ri = pane.SelectedId;
            if (ri < 0 || ri >= W.RouteTable.Count) return;
            var r = W.RouteTable[ri];
            var rr = P.RentRoutes[ri];
            F.Draw(s, o.x, o.y, PairText(F, r.From, r.To, 168));
            void Row(int y, string label, string value) { F.Draw(s, o.x, o.y + y, label); F.Draw(s, o.x + 146, o.y + y, value, TextAlign.Right); }
            if (pane.Page == 0)
            {
                Row(27, Std("Rout", 1001), Money(rr.TicketPrice));
                Row(40, Std("Rout", 1105), Money(rr.TicketPriceFC));
                for (int k = 0; k < 2; k++)
                {
                    int y = o.y + 27 + 13 * k;
                    F.Draw(s, o.x + 150, y, "+");
                    F.Draw(s, o.x + 163, y, "-");
                }
                for (int k = 0; k < 4; k++)
                {
                    int y = 66 + 13 * k;
                    bool hov = cm.x >= 0 && cm.x < 148 && cm.y >= y && cm.y < y + 13;
                    if (hov) s.FillRect(new RectInt(o.x, o.y + y - 1, 168, 13), Highlight);
                    Row(y, Std("Rout", 1102 + k), Money(RouteOps.Preset(W, ri, k)));
                }
                // Competitors' prices (informer needed).
                if (P.HasAdvisor(Sim.Staff.AdvisorType.Informer))
                {
                    int y = 128;
                    foreach (var q in W.Players)
                    {
                        if (q == P || q.IsOut || y > 160) continue;
                        var qr = q.RouteState(ri);
                        Row(y, Clip(F, q.Airline, 100), qr != null && qr.Licensed ? Money(qr.TicketPrice) : "-");
                        y += 13;
                    }
                }
            }
            else if (pane.Page == 1)
            {
                int y = 16;
                Row(y, Std("Rout", 1014), UiKit.Unit(UiKit.UnitKm, W.Distance(r.From, r.To) / 1000)); y += 13;
                Row(y, Std("Rout", 1004), rr.Image.ToString()); y += 13;
                Row(y, Std("Rout", 1011), rr.Rank.ToString()); y += 13;
                Row(y, Std("Rout", 1013), r.Demand.ToString()); y += 13;
                Row(y, Std("Rout", 1017), ""); y += 13;
                Row(y, Std("Rout", 1018), rr.Load + "% / " + rr.LoadFC + "%"); y += 13;
                Row(y, Std("Rout", 1019), rr.RouteShare + "%"); y += 13;
                Row(y, Std("Rout", 1006), Money(rr.Rent)); y += 13;
                bool hov = cm.x >= 2 && cm.x <= 169 && cm.y >= 144 && cm.y <= 157;
                if (hov) { s.FillRect(new RectInt(o.x + 2, o.y + 142, 168, 16), Highlight); App.Ui.Cursor = CursorLook.Hot; }
                s.Draw(Sched(57), o.x + 2, o.y + 142);
                F.Draw(s, o.x + 24, o.y + 145, Std("Rout", 1200));
            }
            else F.DrawBlock(s, new RectInt(o.x, o.y + 16, 168, 150), Std("Auft", 2502), Color.white);
        }

        void DrawCityDetail(Screen2D s, Pane pane, Vector2Int o, Vector2Int cm)
        {
            var c = W.City(pane.SelectedId);
            if (c == null) return;
            if (pane.Page == 0)
            {
                // Rows at y 1/14/27/40 with values left-aligned at x 85 (rooms-and-ui-5b §4.1, verified live).
                void Row(int y, string label, string value) { F.Draw(s, o.x, o.y + y, label); F.Draw(s, o.x + 85, o.y + y, value); }
                Row(1, Std("City", 1000), UiKit.Thousands(c.Population));
                var br = P.BranchAt(c.Index);
                Row(14, Std("City", 1001), Money(br.Owned ? br.Rent : c.OfficeRent));
                if (br.Owned)
                {
                    Row(27, Std("City", 1002), br.Rank.ToString());
                    Row(40, Std("City", 1003), br.Image + "%");
                    if (c.Id != W.HomeCity)
                    {
                        bool hov = cm.x >= 0 && cm.x <= 169 && cm.y >= 66 && cm.y < 79;
                        if (hov) { s.FillRect(new RectInt(o.x, o.y + 65, 170, 13), Highlight); App.Ui.Cursor = CursorLook.Hot; }
                        F.Draw(s, o.x, o.y + 66, Std("City", 1010));
                    }
                }
                return;
            }
            if (pane.Page <= c.TextCount)
            {
                F.DrawBlock(s, new RectInt(o.x, o.y, 168, 168), Assets.StdText.Get("City", c.TextResBase + pane.Page - 1) ?? "", Color.white);
                return;
            }
            if (string.IsNullOrEmpty(c.PhotoName)) return;
            int n = pane.Page - c.TextCount - 1;
            string lib = "gli/" + c.PhotoName.ToLowerInvariant() + PhotoExt;
            string name = c.PhotoName.Substring(0, c.PhotoName.Length - 1) + (char)(c.PhotoName[c.PhotoName.Length - 1] + n);
            if (!Assets.Has(lib, name)) return;
            var t = Assets.Texture(lib, name);
            s.Draw(t, o.x + (ClientW - t.width) / 2, o.y + (ClientH - t.height) / 2);
        }

        // ------------------------------------------------------------------ info (experts, §4.5)

        protected int InfoPages(int entry)
        {
            switch (entry)
            {
                case 1: return P.AdvisorQuality(Sim.Staff.AdvisorType.Finance) >= 80 ? 3 : 1;
                // An intro page, then 13 planes a page (an empty fleet still gets one empty table page).
                case 2: return P.HasAdvisor(Sim.Staff.AdvisorType.Finance) ? Math.Max(0, (P.Planes.Count - 1) / 13) + 2 : 1;
                case 3: return P.HasAdvisor(Sim.Staff.AdvisorType.Informer) ? 3 : 1;
            }
            return 1;
        }

        void DrawInfoDetail(Screen2D s, Pane pane, Vector2Int o, Vector2Int cm)
        {
            void Row(int y, string label, string value, bool red = false) { F.Draw(s, o.x, o.y + y, Clip(F, label, 110)); (red ? Kit.SmallRed : F).Draw(s, o.x + 168, o.y + y, value, TextAlign.Right); }
            void Remark(string text) => F.DrawBlock(s, new RectInt(o.x + 2, o.y + 27, 170, 143), text ?? "", Color.white);
            switch (pane.SelectedId)
            {
                case 0:
                    {
                        if (IsLaptop) s.Draw(Sched(0), o.x + 21, o.y - 2);
                        else s.Draw(Assets.Texture("room/globe_a.gli", "TIMETAB1"), o.x + 21, o.y - 2);
                        int today = W.Clock.Date;
                        for (int d = 0; d < 7; d++) Kit.VerySmall.Draw(s, o.x + 1, o.y + 21 + 19 * d, Std("Schd", 3000 + Weekday(W, today + d)));
                        var occ = P.Gates.Occupancy;
                        int rented = P.Gates.NumRented;
                        var red = new Color32(220, 0, 0, 255);
                        for (int h = 0; h < occ.Length && h < 7 * 24; h++)
                        {
                            if (occ[h] <= 0) continue;
                            int hgt = Math.Min(18, occ[h] * 16 / (rented + 1));
                            var r = new RectInt(o.x + GridX + h % 24 * CellW, o.y + GridY + h / 24 * CellH + CellH - 1 - hgt, CellW, hgt);
                            if (occ[h] > rented) s.FillRect(r, red); else s.FrameRect(r, red);
                        }
                        break;
                    }
                case 1:
                    {
                        // Daily balance (rooms-and-ui-5b �4.5): no finance advisor or talent < 80 -> a remark; else
                        // yesterday's sheet in thousands on 13 px rows, label left at x 2, figure right-aligned at x 172.
                        if (!P.HasAdvisor(Sim.Staff.AdvisorType.Finance)) { Remark(Std("XPrt", 3000)); break; }
                        if (P.AdvisorQuality(Sim.Staff.AdvisorType.Finance) < 80) { Remark(Std("XPrt", 3001)); break; }
                        var y = P.Yesterday;
                        void Line(int row, int text, long value)
                        {
                            F.Draw(s, o.x + 2, o.y + row * 13, Std("XPrt", text));
                            F.Draw(s, o.x + 172, o.y + row * 13, UiKit.Thousands(value / 1000), TextAlign.Right);
                        }
                        if (pane.Page == 0)
                        {
                            F.Draw(s, o.x + 2, o.y, Std("XPrt", 3400));
                            long[] v = { y.DepositInterest, y.DividendsReceived, y.Tickets, y.Orders };
                            for (int i = 0; i < v.Length; i++) Line(2 + i, 3401 + i, v[i]);
                            Line(7, 3604, y.Income);
                        }
                        else if (pane.Page == 1)
                        {
                            F.Draw(s, o.x + 2, o.y, Std("XPrt", 3500));
                            long[] v = { y.LoanInterest, y.DividendsPaid, y.Kerosene, y.Staff, y.Penalties, y.Maintenance, y.GateRent, y.BranchRent, y.RouteRent };
                            for (int i = 0; i < v.Length; i++) Line(2 + i, 3501 + i, v[i]);
                            Line(12, 3604, y.Expense);
                        }
                        else
                        {
                            F.Draw(s, o.x + 2, o.y, Std("XPrt", 3600));
                            Line(2, 3601, y.Income);
                            Line(3, 3602, y.Expense);
                            Line(5, 3604, y.Total);
                        }
                        break;
                    }
                case 2:
                    {
                        // Weekly earnings per plane: needs a finance advisor (any talent); page 0 explains the
                        // report, then 13 planes a page, a losing plane in red (name and figure).
                        if (!P.HasAdvisor(Sim.Staff.AdvisorType.Finance)) { Remark(Std("XPrt", 3000)); break; }
                        if (pane.Page == 0) { Remark(Std("XPrt", 3009)); break; }
                        var l = P.Planes.ToList();
                        int first = (pane.Page - 1) * 13;
                        for (int k = 0; k < 13 && first + k < l.Count; k++)
                        {
                            var pl = l[first + k];
                            long sum = pl.BalanceSum;
                            var f = sum < 0 ? Kit.SmallRed : F;
                            string value = Money(sum);
                            int vx = o.x + 170 - f.Measure(value);
                            f.Draw(s, vx, o.y + 13 * k, value);
                            f.Draw(s, o.x, o.y + 13 * k, Clip(f, pl.Name, vx - o.x - 6));
                        }
                        break;
                    }
                case 3:
                    {
                        if (!P.HasAdvisor(Sim.Staff.AdvisorType.Informer)) { Remark(Std("XPrt", 3002)); break; }
                        int yy = 0;
                        foreach (var q in W.Players)
                        {
                            if (q.IsOut) continue;
                            Kit.SmallBlack.Draw(s, o.x, o.y + yy, Clip(F, q.Airline, 168)); yy += 12;
                            if (pane.Page == 0) { Row(yy, Std("XPrt", 3300), ((q.Money) / 1000000.0).ToString("0.0")); yy += 12; Row(yy, Std("XPrt", 3301), (q.Credit / 1000000.0).ToString("0.0")); }
                            else if (pane.Page == 1) { Row(yy, Std("XPrt", 3302), q.Planes.Count.ToString()); yy += 12; Row(yy, Std("XPrt", 3303), q.Image.ToString()); }
                            else
                            {
                                int br = 0; foreach (var rc in q.RentCities) if (rc.Owned) br++;
                                Row(yy, Std("XPrt", 3304), q.LicensedRouteCount.ToString()); yy += 12; Row(yy, Std("XPrt", 3305), br.ToString());
                            }
                            yy += 16;
                        }
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ globe overlays

        /// <summary>Recomputes globe lines (selected planes' flights, laptop routes/cities) and plane sprites.</summary>
        protected void PrepareGlobe(IEnumerable<Plane> selected, bool laptopRoutes)
        {
            Globe.Lines.Clear();
            Globe.Dots.Clear();
            int sig = 17;
            var red = new Color32(255, 0, 0, 255);
            foreach (var pl in selected)
            {
                foreach (var e in pl.Plan.Entries)
                {
                    if (e.Kind == 0) continue;
                    var a = W.City(e.From); var z = W.City(e.To);
                    if (a == null || z == null) continue;
                    Globe.Lines.Add(new GlobeView.Line { Lon1 = a.GlobeX, Lat1 = a.GlobeY, Lon2 = z.GlobeX, Lat2 = z.GlobeY, Colour = red, Steps = GlobeView.StepsFor(W.Distance(e.From, e.To)) });
                    sig = sig * 31 + e.From * 7 + e.To;
                }
            }
            if (laptopRoutes)
                for (int p = 0; p < 4; p++)
                {
                    var q = W.Players[p];
                    int lvl = State.DisplayRoutes[p];
                    if (lvl == 0 || q.IsOut) continue;
                    sig = sig * 31 + p * 3 + lvl;
                    var yellow = new Color32(255, 255, 0, 255);
                    for (int ci = 0; ci < q.RentCities.Length; ci++)
                        if (q.RentCities[ci].Owned) { var c = W.Data.Cities.All[ci]; Globe.Dots.Add((c.GlobeX, c.GlobeY, yellow)); }
                    for (int ri = 0; ri < W.RouteTable.Count && ri < q.RentRoutes.Length; ri++)
                    {
                        var r = W.RouteTable[ri];
                        if (!q.RentRoutes[ri].Licensed || r.Level != lvl || r.From > r.To) continue;
                        var a = W.City(r.From); var z = W.City(r.To);
                        Globe.Lines.Add(new GlobeView.Line { Lon1 = a.GlobeX, Lat1 = a.GlobeY, Lon2 = z.GlobeX, Lat2 = z.GlobeY, Colour = red, Steps = GlobeView.StepsFor(W.Distance(r.From, r.To)) });
                        sig = sig * 31 + ri;
                    }
                }
            Globe.SetOverlaySignature(sig);
        }

        static readonly string[] Colour = { "B", "V", "R", "J" };

        /// <summary>Plane sprites (§4.4): behind the globe before it, in front after it.</summary>
        protected void DrawGlobeWithPlanes(Screen2D s, string spriteLib)
        {
            var centre = new Vector2Int(RX, 0) + GlobeOrigin + GlobeView.CentreOffset;
            var front = new List<(Plane pl, int p, Vector2 pos)>();
            for (int p = 0; p < 4; p++)
            {
                if (!ShowPlanesOf(p) || W.Players[p].IsOut) continue;
                foreach (var pl in W.Players[p].Planes.Items)
                {
                    bool vis = Globe.Project(pl.GlobeX, pl.GlobeY, out var o);
                    var pos = new Vector2(centre.x + o.x, centre.y + o.y);
                    if (vis) front.Add((pl, p, pos));
                }
            }
            Globe.Draw(s, centre);
            foreach (var f in front)
            {
                int frame = PlaneHeading(f.pl);
                string name = "PL_" + Colour[f.p] + frame.ToString("00");
                int jx = f.pl.Name != null && f.pl.Name.Length > 1 ? f.pl.Name[0] % 3 - 1 : 0, jy = f.pl.Name != null && f.pl.Name.Length > 1 ? f.pl.Name[1] % 3 - 1 : 0;
                s.Draw(Assets.Texture(spriteLib, name), Mathf.RoundToInt(f.pos.x) - 9 + jx, Mathf.RoundToInt(f.pos.y) - 9 + jy);
            }
        }

        /// <summary>Sprite frame: 0 on the ground, 1..8 by heading of the current leg (§4.4).</summary>
        int PlaneHeading(Plane pl)
        {
            if (PlaneLocation.IsOnGround(pl.Location)) return 0;
            int abs = W.Clock.Date * 24 + W.Clock.Hour;
            foreach (var e in pl.Plan.Entries)
            {
                if (e.Kind == 0 || e.StartAbs > abs || e.LandAbs < abs) continue;
                var a = W.City(e.From); var z = W.City(e.To);
                if (a == null || z == null) break;
                Globe.Project(a.GlobeX, a.GlobeY, out var pa);
                Globe.Project(z.GlobeX, z.GlobeY, out var pz);
                var d = pz - pa;
                if (d.sqrMagnitude < 1) break;
                // 1 = north, clockwise in 45° steps.
                double ang = Math.Atan2(d.x, -d.y) * 180 / Math.PI;
                int sector = (int)Math.Floor(((ang + 360 + 22.5) % 360) / 45);
                return sector + 1;
            }
            return 0;
        }
    }
}
