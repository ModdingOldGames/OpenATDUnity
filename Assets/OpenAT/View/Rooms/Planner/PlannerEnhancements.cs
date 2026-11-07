using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;
using PT = OpenAT.View.Rooms.Planner.PlannerTexts;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// OpenAT planner improvements (<see cref="GameOptions.PlannerEnhancements"/>, off during the tutorial):
    /// the fleet index with search and filters, docked plane schedules in the extra width of wide screens
    /// (the room's 640 px artwork moves left, the dock fills the right), previous/next plane, the flight
    /// detail card, precise departure setting and notices that describe what a planning command actually did.
    /// Every change still goes through the same operations commands as the original controls.
    /// </summary>
    public abstract partial class PlannerRoom
    {
        public const int DockColW = 205, DockSlotH = 210, DockGap = 3, FleetRowH = 26;
        const float CardDelay = 0.35f, NoticeSeconds = 4f;

        static readonly Color32 Paper = new Color32(236, 228, 204, 255), PaperDark = new Color32(206, 196, 166, 255);
        static readonly Color32 Ink = new Color32(40, 40, 40, 255), DockBack = new Color32(0, 0, 0, 215);
        static readonly Color32 SelFrame = new Color32(255, 255, 255, 255), SelFrame2 = new Color32(0, 0, 0, 255);

        /// <summary>Improvements active: the option is on and the tutorial is not running.</summary>
        protected bool Enhanced => W != null && W.Options.PlannerEnhancements && !Tutorial.TutorialView.Active;

        /// <summary>Dock columns that fit beside the 640 px room (0 = compact, original layout).</summary>
        protected int DockColumns => Enhanced ? Math.Max(0, (App.Screen.Width - Screen2D.BaseWidth - DockGap) / DockColW) : 0;
        protected bool Expanded => DockColumns > 0;

        /// <summary>Moves the room canvas left so the dock fits on its right (centred as a group).</summary>
        protected void ApplyLayout()
        {
            int cols = DockColumns;
            PaneOriginX = cols > 0 ? Math.Max(0, (App.Screen.Width - Screen2D.BaseWidth - DockGap - cols * DockColW) / 2) : (int?)null;
            if (cols > 0) _fleetPopup = false;
        }

        // ------------------------------------------------------------------ dock geometry (canvas coordinates)

        int DockX(int col) => Screen2D.BaseWidth + DockGap + col * DockColW;

        /// <summary>Number of docked schedules shown at the current width.</summary>
        int DockCapacity
        {
            get
            {
                int cols = DockColumns;
                if (cols <= 0) return 0;
                return cols == 1 ? 1 : 2 * (cols - 1);
            }
        }

        Vector2Int DockSlot(int i)
        {
            int cols = DockColumns;
            if (cols == 1) return new Vector2Int(DockX(0), 440 - DockSlotH - 4);
            return new Vector2Int(DockX(1 + i / 2), 6 + (i % 2) * (DockSlotH + 8));
        }

        /// <summary>Fleet index rect (canvas): the first dock column, or the compact popup.</summary>
        RectInt FleetRect
        {
            get
            {
                if (Expanded)
                {
                    bool shared = DockColumns == 1 && VisibleDocked().Count > 0;
                    return new RectInt(DockX(0), 4, 199, shared ? 440 - DockSlotH - 12 : 432);
                }
                return new RectInt(220, 30, 199, 380);
            }
        }

        readonly List<Block> _dockVisible = new List<Block>();
        readonly List<Block> _allBlocks = new List<Block>();

        /// <summary>The docked schedules on screen, positioned in their slots.</summary>
        protected List<Block> VisibleDocked()
        {
            _dockVisible.Clear();
            if (!Expanded || State == null) return _dockVisible;
            int cap = DockCapacity;
            for (int i = 0; i < State.Docked.Count && i < cap; i++)
            {
                var b = State.Docked[i];
                b.Pos = DockSlot(i);
                _dockVisible.Add(b);
            }
            return _dockVisible;
        }

        /// <summary>Docked schedules (front) followed by the room's own blocks: hit order.</summary>
        protected IReadOnlyList<Block> AllBlocks
        {
            get
            {
                _allBlocks.Clear();
                _allBlocks.AddRange(VisibleDocked());
                foreach (var b in Blocks) _allBlocks.Add(b);
                return _allBlocks;
            }
        }

        /// <summary>Docked schedules use the laptop's single-window look in both rooms.</summary>
        protected bool Lap(Block b) => IsLaptop || (b != null && b.Style == Block.StyleDock);

        /// <summary>Pane geometry including docked schedules (laptop single window, left pane).</summary>
        protected PaneGeom PGeom(Block b, bool right)
        {
            if (b.Style != Block.StyleDock) return Geom(b, right);
            return new PaneGeom
            {
                Client = new Vector2Int(21, 22), Title = new Vector2Int(41, 7), PageNo = new Vector2Int(104, 195),
                Prev = PaneGeom.R(0, 23, 19, 44), Next = PaneGeom.R(0, 165, 19, 186), Index = PaneGeom.R(16, 0, 33, 19),
                InputShift = new Vector2Int(-1, -1),
            };
        }

        protected bool IsDocked(int planeId)
        {
            foreach (var b in VisibleDocked()) if (b.Left.IsDetail(BlockType.Planes) && b.Left.SelectedId == planeId) return true;
            return false;
        }

        /// <summary>Docks a plane's schedule (or undocks it). Compact laptop: opens a laptop window instead.</summary>
        protected void TogglePin(Plane pl)
        {
            if (!Expanded) { OpenPlaneWindow(pl); return; }
            int i = State.Docked.FindIndex(d => d.Left.IsDetail(BlockType.Planes) && d.Left.SelectedId == pl.Id);
            Block b;
            if (i >= 0)
            {
                b = State.Docked[i];
                State.Docked.RemoveAt(i);
                if (i < DockCapacity) return;   // shown: undock; kept from a wider screen: show it again
            }
            else
            {
                b = new Block { Style = Block.StyleDock, Double = false, WindowType = BlockType.Planes };
                b.Left.Type = BlockType.Planes;
                b.Left.IsIndex = false;
                b.Left.SelectedId = pl.Id;
                b.Left.Page = 0;
                UpdatePages(b.Left, b);
            }
            State.Docked.Insert(0, b);
            // A new schedule replaces the oldest one when the dock is full (hidden ones would look docked).
            while (State.Docked.Count > Math.Max(1, DockCapacity)) State.Docked.RemoveAt(State.Docked.Count - 1);
            App.Audio.Play("SOUND/click.raw");
        }

        void ValidateDocked()
        {
            if (State == null || P == null) return;
            State.Docked.RemoveAll(b => b.Left.Type == BlockType.Planes && !b.Left.IsIndex && P.Planes.Find(b.Left.SelectedId) == null);
            foreach (var b in State.Docked) { UpdatePages(b.Left, b); b.Closing = false; }
        }

        // ------------------------------------------------------------------ room hooks for the fleet index

        /// <summary>Shows a plane in the room's main planner (filofax page / front laptop window).</summary>
        protected abstract void OpenPlaneFromFleet(Plane pl);
        /// <summary>Opens a plane in an additional window (laptop); false where the room has none.</summary>
        protected virtual bool OpenPlaneWindow(Plane pl) => false;
        protected virtual bool CanOpenPlaneWindow => false;
        /// <summary>The plane shown in the room's main planner, or −1.</summary>
        protected abstract int MainPlaneId { get; }
        /// <summary>Optional "arrange windows" action of the room (laptop).</summary>
        protected virtual bool CanArrange => false;
        protected virtual void ArrangeWindows() { }

        // ------------------------------------------------------------------ fleet list

        FleetFilter Filter => State.Fleet;

        static string TypeOf(Plane pl) => (pl.Maker + " " + pl.Model).Trim();

        int NowAbs => W.Clock.Date * 24 + W.Clock.Hour;

        bool HasFuturePlan(Plane pl)
        {
            int now = NowAbs;
            foreach (var e in pl.Plan.Entries) if (e.Kind != 0 && e.LandAbs >= now) return true;
            return false;
        }

        bool StateMatches(Plane pl, int state)
        {
            switch (state)
            {
                case FleetFilter.Problem: return PlaneRed(pl);
                case FleetFilter.Idle: return !HasFuturePlan(pl);
                case FleetFilter.Airborne: return !PlaneLocation.IsOnGround(pl.Location);
                case FleetFilter.Ground: return PlaneLocation.IsOnGround(pl.Location);
            }
            return true;
        }

        /// <summary>The fleet in the planner's order (problems first, then by name), filtered.</summary>
        protected List<Plane> FleetList()
        {
            var l = PlaneList(null);
            if (!Enhanced || !Filter.Active) return l;
            string q = Filter.Search.Trim();
            return l.FindAll(pl =>
                (q.Length == 0 || (pl.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || TypeOf(pl).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                && (Filter.Type.Length == 0 || TypeOf(pl) == Filter.Type)
                && StateMatches(pl, Filter.State));
        }

        List<string> FleetTypes()
        {
            var l = new List<string>();
            foreach (var pl in P.Planes.Items) { var t = TypeOf(pl); if (!l.Contains(t)) l.Add(t); }
            l.Sort(string.CompareOrdinal);
            return l;
        }

        /// <summary>Previous / next plane of a plane detail in the filtered fleet order (wraps around).</summary>
        protected void JumpPlane(Block b, Pane pane, int dir)
        {
            if (_dragMode != 0) return;   // never switch planes during an edge drag
            var l = FleetList();
            if (l.FindIndex(p => p.Id == pane.SelectedId) < 0) l = PlaneList(null);   // the open plane is filtered out
            int i = l.FindIndex(p => p.Id == pane.SelectedId);
            if (i < 0 || l.Count == 0) return;
            int page = pane.Page;
            pane.SelectedId = l[(i + dir + l.Count) % l.Count].Id;
            UpdatePages(pane, b);
            pane.Page = Mathf.Min(page, pane.Pages - 1);
            ClearSelection();
            PaperSound();
        }

        // ------------------------------------------------------------------ fleet panel state

        bool _fleetPopup, _searchFocus;
        int _pinHoverRow = -1;

        protected bool FleetPanelShown => Enhanced && (Expanded || _fleetPopup);

        protected void OpenFleetPopup()
        {
            if (Expanded) { _searchFocus = true; return; }
            _fleetPopup = true;
            _searchFocus = false;
            KeepMainVisible();
            PaperSound();
        }

        void CloseFleetPopup() { _fleetPopup = false; _searchFocus = false; }

        int FleetListTop(RectInt r) => r.y + 54;
        int FleetRows(RectInt r) => Math.Max(1, (r.yMax - FleetListTop(r) - 2) / FleetRowH);

        void KeepMainVisible()
        {
            var r = FleetRect;
            var l = FleetList();
            int i = l.FindIndex(p => p.Id == MainPlaneId);
            int rows = FleetRows(r);
            if (i >= 0 && (i < Filter.Scroll || i >= Filter.Scroll + rows)) Filter.Scroll = Math.Max(0, i - rows / 2);
            ClampScroll(l.Count, rows);
        }

        void ClampScroll(int count, int rows) => Filter.Scroll = Mathf.Clamp(Filter.Scroll, 0, Math.Max(0, count - rows));

        // Panel-relative controls.
        static RectInt SearchBox(RectInt r) => new RectInt(r.x + 4, r.y + 16, r.width - 52, 16);
        static RectInt ResetBtn(RectInt r) => new RectInt(r.xMax - 46, r.y + 17, 42, 14);
        static RectInt TypeBtn(RectInt r) => new RectInt(r.x + 4, r.y + 35, (r.width - 12) / 2, 14);
        static RectInt StateBtn(RectInt r) => new RectInt(r.x + 8 + (r.width - 12) / 2, r.y + 35, (r.width - 12) / 2, 14);
        RectInt ArrangeBtn(RectInt r) => new RectInt(r.xMax - 60, r.y + 2, 56, 12);
        static RectInt PinBtn(RectInt r, int row, int top) => new RectInt(r.xMax - 20, top + row * FleetRowH + 6, 16, 14);

        static bool In(RectInt r, Vector2Int p) => p.x >= r.xMin && p.x < r.xMax && p.y >= r.yMin && p.y < r.yMax;

        /// <summary>Keyboard while the improvements are on: search typing, Esc closes cards/popups. Before the base keys.</summary>
        void EnhancedKeys(InputState input)
        {
            if (_searchFocus)
            {
                foreach (char ch in input.Typed)
                    if ((char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '.' || ch == '/') && Filter.Search.Length < 24)
                        Filter.Search += ch;
                if (input.Backspace && Filter.Search.Length > 0) Filter.Search = Filter.Search.Substring(0, Filter.Search.Length - 1);
                if (input.Typed.Count > 0 || input.Backspace) Filter.Scroll = 0;
                input.Typed.Clear();
                input.Backspace = false;
                if (input.Enter) { _searchFocus = false; input.Enter = false; }
                if (input.Escape) { _searchFocus = false; input.Escape = false; }
            }
            if (input.Escape && _sel.Active) { ClearSelection(); input.Escape = false; }
            if (input.Escape && _fleetPopup) { CloseFleetPopup(); input.Escape = false; }
        }

        /// <summary>Fleet panel input; true when the mouse is over the panel (input handled).</summary>
        bool FleetInput(InputState input, Vector2Int canvas)
        {
            if (!FleetPanelShown) return false;
            var r = FleetRect;
            if (!In(r, canvas))
            {
                if (_fleetPopup && (input.LeftDown || input.RightDown) && !input.Consumed) { CloseFleetPopup(); input.Consume(); return true; }
                if (input.LeftDown) _searchFocus = false;
                return false;
            }
            var l = FleetList();
            int rows = FleetRows(r), top = FleetListTop(r);
            if (input.Wheel != 0) { Filter.Scroll -= Math.Sign(input.Wheel) * 2; ClampScroll(l.Count, rows); }
            _pinHoverRow = -1;
            int row = canvas.y >= top ? (canvas.y - top) / FleetRowH : -1;
            int idx = row >= 0 && row < rows ? Filter.Scroll + row : -1;
            bool pinnable = Expanded || CanOpenPlaneWindow;
            if (idx >= 0 && idx < l.Count)
            {
                App.Ui.Cursor = CursorLook.Hot;
                if (pinnable && In(PinBtn(r, row, top), canvas))
                {
                    _pinHoverRow = row;
                    App.Ui.Tooltip.Set(Expanded && IsDocked(l[idx].Id) ? PT.Get("unpin") : PT.Get("pin"));
                }
            }
            if (In(SearchBox(r), canvas) || In(ResetBtn(r), canvas) || In(TypeBtn(r), canvas) || In(StateBtn(r), canvas)) App.Ui.Cursor = CursorLook.Hot;
            if (In(TypeBtn(r), canvas)) App.Ui.Tooltip.Set(PT.Get("filter.type"));
            if (In(StateBtn(r), canvas)) App.Ui.Tooltip.Set(PT.Get("filter.status"));
            if (CanArrange && In(ArrangeBtn(r), canvas)) { App.Ui.Cursor = CursorLook.Hot; }

            if (input.RightDown && !input.Consumed)
            {
                input.Consume();
                if (In(TypeBtn(r), canvas)) CycleType(-1);
                else if (In(StateBtn(r), canvas)) CycleState(-1);
                else if (_fleetPopup) CloseFleetPopup();
                return true;
            }
            if (!input.LeftDown || input.Consumed) return true;
            input.Consume();
            _searchFocus = In(SearchBox(r), canvas);
            if (_searchFocus) return true;
            if (In(ResetBtn(r), canvas)) { Filter.Reset(); KeepMainVisible(); App.Audio.Play("SOUND/click.raw"); return true; }
            if (In(TypeBtn(r), canvas)) { CycleType(1); return true; }
            if (In(StateBtn(r), canvas)) { CycleState(1); return true; }
            if (CanArrange && In(ArrangeBtn(r), canvas)) { ArrangeWindows(); return true; }
            if (idx < 0 || idx >= l.Count) return true;
            var pl = l[idx];
            if (pinnable && In(PinBtn(r, row, top), canvas)) { TogglePin(pl); return true; }
            OpenPlaneFromFleet(pl);
            ClearSelection();
            if (_fleetPopup) CloseFleetPopup();
            return true;
        }

        void CycleType(int d)
        {
            var types = FleetTypes();
            int i = types.IndexOf(Filter.Type);   // −1 = all
            int n = types.Count + 1;
            i = ((i + 1 + d) % n + n) % n - 1;
            Filter.Type = i < 0 ? "" : types[i];
            Filter.Scroll = 0;
            App.Audio.Play("SOUND/click.raw");
        }

        void CycleState(int d)
        {
            Filter.State = ((Filter.State + d) % FleetFilter.StateCount + FleetFilter.StateCount) % FleetFilter.StateCount;
            Filter.Scroll = 0;
            App.Audio.Play("SOUND/click.raw");
        }

        static string StateName(int s) => s == FleetFilter.Problem ? PT.Get("status.problem") : s == FleetFilter.Idle ? PT.Get("status.idle")
            : s == FleetFilter.Airborne ? PT.Get("status.air") : s == FleetFilter.Ground ? PT.Get("status.ground") : PT.Get("status.all");

        void Button(Screen2D s, RectInt b, string text, bool hot, bool on = false)
        {
            s.FillRect(b, on ? PaperDark : hot ? new Color32(250, 244, 226, 255) : Paper);
            s.FrameRect(b, Ink);
            F.Draw(s, b.x + b.width / 2, b.y + 1, Clip(F, text, b.width - 4), TextAlign.Center);
        }

        void DrawFleetPanel(Screen2D s)
        {
            if (!FleetPanelShown || P == null) return;
            var rc = FleetRect;
            var r = new RectInt(RX + rc.x, rc.y, rc.width, rc.height);
            var m = App.InputNow.Mouse;
            var mc = new Vector2Int(m.x - RX, m.y);
            s.FillRect(r, Paper);
            s.FrameRect(r, Ink);
            var l = FleetList();
            int total = P.Planes.Count;
            Kit.SmallBlack.Draw(s, r.x + 4, r.y + 2, PT.Get("fleet"));
            if (CanArrange) Button(s, Shift(ArrangeBtn(rc)), PT.Get("arrange"), In(ArrangeBtn(rc), mc));
            else F.Draw(s, r.xMax - 4, r.y + 2, PT.Get("count", l.Count, total), TextAlign.Right);
            if (CanArrange) F.Draw(s, r.x + 4 + F.Measure(PT.Get("fleet")) + 6, r.y + 2, PT.Get("count", l.Count, total));
            // Search box with caret.
            var sb = Shift(SearchBox(rc));
            s.FillRect(sb, new Color32(255, 255, 255, 255));
            s.FrameRect(sb, _searchFocus ? new Color32(32, 120, 220, 255) : Ink);
            if (Filter.Search.Length == 0 && !_searchFocus) Kit.SmallGrey.Draw(s, sb.x + 3, sb.y + 1, PT.Get("search"));
            else
            {
                string t = Filter.Search;
                while (t.Length > 0 && F.Measure(t) > sb.width - 8) t = t.Substring(1);
                F.Draw(s, sb.x + 3, sb.y + 1, t);
                if (_searchFocus && ((int)(Time.unscaledTime * 2) & 1) == 0) s.FillRect(new RectInt(sb.x + 4 + F.Measure(t), sb.y + 2, 1, 10), Ink);
            }
            Button(s, Shift(ResetBtn(rc)), PT.Get("clear"), In(ResetBtn(rc), mc), false);
            Button(s, Shift(TypeBtn(rc)), Filter.Type.Length == 0 ? PT.Get("type.all") : Filter.Type, In(TypeBtn(rc), mc), Filter.Type.Length > 0);
            Button(s, Shift(StateBtn(rc)), StateName(Filter.State), In(StateBtn(rc), mc), Filter.State != FleetFilter.AnyState);
            s.FillRect(new RectInt(r.x, r.y + 51, r.width, 1), Ink);

            int rows = FleetRows(rc), top = FleetListTop(rc);
            ClampScroll(l.Count, rows);
            if (l.Count == 0)
            {
                Kit.SmallGrey.DrawBlock(s, new RectInt(r.x + 6, top + 6, r.width - 12, 40), total == 0 ? PT.Get("noplanes") : PT.Get("none"), Color.white);
                return;
            }
            // Eligibility while carrying an object: planes that cannot fly it are grey (as in the plane list).
            int need = 0, from = 0, to = 0;
            bool filter = !Carry.Empty && CarryCitiesPublic(out from, out to);
            if (filter && Carry.Kind == FlightKind.Contract) need = P.Contracts.Find(Carry.Id)?.Persons ?? 0;
            int main = MainPlaneId;
            bool pinnable = Expanded || CanOpenPlaneWindow;
            s.PushClip(new RectInt(r.x + 1, top, r.width - 2, rows * FleetRowH));
            for (int k = 0; k < rows && Filter.Scroll + k < l.Count; k++)
            {
                var pl = l[Filter.Scroll + k];
                int y = top + k * FleetRowH;
                var row = new RectInt(r.x + 1, y, r.width - 2, FleetRowH);
                bool hov = mc.y >= y && mc.y < y + FleetRowH && In(rc, mc);
                if (pl.Id == main) s.FillRect(row, PaperDark);
                if (hov) s.FillRect(row, Highlight);
                bool grey = filter && (W.Distance(from, to) > pl.Range * 1000 || Duration(pl, from, to) >= 24 || need > pl.MaxPassengers + pl.MaxPassengersFC);
                var f = grey ? Kit.SmallGrey : PlaneRed(pl) ? Kit.SmallRed : F;
                f.Draw(s, r.x + 4, y + 2, Clip(f, pl.Name, 112));
                f.Draw(s, r.x + 124, y + 2, UiKit.Unit(UiKit.UnitPercent, pl.Condition));
                string where = PlaneLocation.IsOnGround(pl.Location)
                    ? Std("Schd", 1105) + " " + (W.City(pl.Location)?.Name ?? "?")
                    : Std("Schd", 1102) + " " + (W.City(NextDestination(pl))?.Name ?? "?");
                f.Draw(s, r.x + 4, y + 13, Clip(f, where, pinnable ? 168 : 190));
                if (pinnable)
                {
                    var pb = PinBtn(rc, k, top); pb.x += RX;
                    bool docked = Expanded && IsDocked(pl.Id);
                    if (hov || docked)
                    {
                        s.FillRect(pb, docked ? PaperDark : Paper);
                        s.FrameRect(pb, Ink);
                        F.Draw(s, pb.x + 8, pb.y + 1, docked ? "x" : "+", TextAlign.Center);
                    }
                }
                if (k < rows - 1) s.FillRect(new RectInt(r.x + 3, y + FleetRowH - 1, r.width - 6, 1), PaperDark);
            }
            s.PopClip();
            // Scroll position.
            if (l.Count > rows)
            {
                int h = rows * FleetRowH;
                int th = Math.Max(10, h * rows / l.Count), ty = top + (h - th) * Filter.Scroll / Math.Max(1, l.Count - rows);
                s.FillRect(new RectInt(r.xMax - 3, ty, 2, th), Ink);
            }
        }

        RectInt Shift(RectInt r) => new RectInt(r.x + RX, r.y, r.width, r.height);

        // ------------------------------------------------------------------ docked schedule chrome

        static bool InL(Vector2Int l, int x1, int y1, int x2, int y2) => l.x >= x1 && l.x <= x2 && l.y >= y1 && l.y <= y2;

        bool DockChromeClick(Block b, Vector2Int l)
        {
            if (InL(l, 173, 3, 191, 19)) { State.Docked.Remove(b); App.Audio.Play("SOUND/click.raw"); return true; }
            return false;
        }

        void DockChromeHover(Block b, Vector2Int l)
        {
            if (InL(l, 173, 3, 191, 19)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(PT.Get("unpin")); }
        }

        void DrawDockChrome(Screen2D s, Block b, int x, int y)
        {
            const string lib = "room/globe_b.gli";
            s.Draw(Assets.Texture(lib, "WINDOW"), x, y);
            if (!b.Left.IsIndex) s.Draw(Assets.Texture(lib, "INHALT0"), x + 16, y);
        }

        void DrawDockOverlay(Screen2D s, Block b, int x, int y, Vector2Int l)
        {
            const string lib = "room/globe_b.gli";
            bool held = App.InputNow.LeftHeld;
            var pane = b.Left;
            var g = PGeom(b, false);
            if (pane.Page > 0 && PaneGeom.In(g.Prev, l)) s.Draw(Assets.Texture(lib, "UPLEFT" + (held ? "2" : "1")), x, y + 22);
            if (pane.Page < pane.Pages - 1 && PaneGeom.In(g.Next, l)) s.Draw(Assets.Texture(lib, "DWNLEFT" + (held ? "2" : "1")), x, y + 168);
            if (!pane.IsIndex && PaneGeom.In(g.Index, l)) s.Draw(Assets.Texture(lib, held ? "INHALT2" : "INHALT1"), x + 16, y);
            if (pane.Pages > 1)
            {
                int ty = 43 + 95 * pane.Page / (pane.Pages - 1);
                s.Draw(Assets.Texture(lib, "SCROLLL"), x + 5, y + ty);
            }
            if (InL(l, 173, 3, 191, 19)) s.Draw(Assets.Texture(lib, held ? "CLOSE2" : "CLOSE1"), x + 174, y);
        }

        // ------------------------------------------------------------------ previous / next plane buttons (filofax, docked)

        /// <summary>Block-relative rects of the previous/next plane buttons of a plane detail, or false.</summary>
        protected virtual bool PlaneNavRects(Block b, bool right, out RectInt prev, out RectInt next)
        {
            prev = next = default;
            if (!Enhanced || right || !b.Left.IsDetail(BlockType.Planes) || b.Style == Block.StyleLaptop) return false;
            var g = PGeom(b, false);
            if (b.Style == Block.StyleDock) { prev = new RectInt(150, 4, 11, 14); next = new RectInt(161, 4, 11, 14); return true; }
            prev = new RectInt(g.Title.x + 146, g.Title.y - 1, 11, 13);
            next = new RectInt(g.Title.x + 158, g.Title.y - 1, 11, 13);
            return true;
        }

        /// <summary>Block-relative rect of a plane detail's title (opens the fleet popup in compact mode).</summary>
        protected RectInt TitleRect(Block b)
        {
            var g = PGeom(b, false);
            return new RectInt(g.Title.x - 2, g.Title.y - 2, b.Style == Block.StyleFilofax ? 144 : 104, 15);
        }

        bool NavClick(Block b, Vector2Int l)
        {
            if (!PlaneNavRects(b, false, out var pr, out var nx)) return false;
            if (In(pr, l)) { JumpPlane(b, b.Left, -1); return true; }
            if (In(nx, l)) { JumpPlane(b, b.Left, 1); return true; }
            if (!Expanded && b.Style == Block.StyleFilofax && In(TitleRect(b), l)) { OpenFleetPopup(); return true; }
            return false;
        }

        bool NavHover(Block b, Vector2Int l)
        {
            if (!PlaneNavRects(b, false, out var pr, out var nx)) return false;
            if (In(pr, l)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(PT.Get("prev")); return true; }
            if (In(nx, l)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(PT.Get("next")); return true; }
            if (!Expanded && b.Style == Block.StyleFilofax && In(TitleRect(b), l)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(PT.Get("pick")); return true; }
            return false;
        }

        void DrawNav(Screen2D s, Block b, Vector2Int bs, Vector2Int l)
        {
            if (!PlaneNavRects(b, false, out var pr, out var nx)) return;
            var font = b.Style == Block.StyleDock ? TitleFontFor(b) : F;
            foreach (var (rect, label) in new[] { (pr, "<"), (nx, ">") })
            {
                var r = new RectInt(bs.x + rect.x, bs.y + rect.y, rect.width, rect.height);
                if (In(rect, l)) s.FillRect(r, Highlight);
                font.Draw(s, r.x + rect.width / 2, r.y + 1, label, TextAlign.Center);
            }
        }

        // ------------------------------------------------------------------ flight detail card (hover)

        Block _cardBlock;
        Plane _cardPlane;
        int _cardEntry = -1;
        float _cardSince;
        int _cardKey;

        void SetHoverCard(Block b, Plane pl, int ei)
        {
            if (!Enhanced) return;
            int key = pl.Id * 64 + ei;
            if (key != _cardKey) { _cardKey = key; _cardSince = Time.unscaledTime; }
            _cardBlock = b; _cardPlane = pl; _cardEntry = ei;
        }

        string DayHour(int abs) => FlightCard.DayHour(W, Assets, abs);

        List<(string text, IFont font)> CardLines(Plane pl, FlightEntry e)
        {
            var lines = FlightCard.Lines(W, Assets, Kit, P, pl, e);
            if (e.Kind == FlightKind.Auto) lines.Add((PT.Get("auto.why"), Kit.SmallGrey));
            else if (!e.IsFuture(W.Clock.Date, W.Clock.Hour)) lines.Add((PT.Get("locked"), Kit.SmallGrey));
            else lines.Add((PT.Get("howto"), Kit.SmallGrey));
            return lines;
        }

        /// <summary>A paper card with wrapped lines at (x, y) clamped to the screen; returns its rect.</summary>
        RectInt DrawCard(Screen2D s, int x, int y, int w, List<(string text, IFont font)> lines, bool measureOnly = false) =>
            FlightCard.Draw(s, x, y, w, lines, measureOnly);

        void DrawHoverCard(Screen2D s)
        {
            if (_cardPlane == null || _cardEntry < 0 || Time.unscaledTime - _cardSince < CardDelay || _sel.Active && _sel.Block == _cardBlock) return;
            var e = _cardPlane.Plan.Entries[_cardEntry];
            if (e.Kind == 0) return;
            var m = App.InputNow.Mouse;
            DrawCard(s, m.x + 14, m.y + 18, FlightCard.Width, CardLines(_cardPlane, e));
        }

        // ------------------------------------------------------------------ selected flight: precise departure

        struct Selection
        {
            public bool Active;
            public Block Block;
            public int PlaneId, Kind, ObjectId, StartAbs;
            public string Result;
        }

        Selection _sel;

        protected void ClearSelection() => _sel = default;

        bool SelectionEntry(out Plane pl, out int ei)
        {
            pl = null; ei = -1;
            if (!_sel.Active) return false;
            pl = P.Planes.Find(_sel.PlaneId);
            if (pl == null) return false;
            ei = FindEntry(pl, _sel.Kind, _sel.ObjectId, _sel.StartAbs);
            return ei >= 0 && pl.Plan.Entries[ei].Kind == _sel.Kind && pl.Plan.Entries[ei].ObjectId == _sel.ObjectId;
        }

        void Select(Block b, Plane pl, FlightEntry e)
        {
            _sel = new Selection { Active = true, Block = b, PlaneId = pl.Id, Kind = e.Kind, ObjectId = e.ObjectId, StartAbs = e.StartAbs };
            App.Audio.Play("SOUND/click.raw");
        }

        bool SelectionValid()
        {
            if (!_sel.Active) return false;
            if (!SelectionEntry(out var pl, out int ei) || !pl.Plan.Entries[ei].IsFuture(W.Clock.Date, W.Clock.Hour)
                || !AllBlocksContain(_sel.Block) || SelectedPlane(_sel.Block) != pl || !GridVisible(_sel.Block)) { ClearSelection(); return false; }
            return true;
        }

        bool AllBlocksContain(Block b) { foreach (var x in AllBlocks) if (x == b && !x.Closing) return true; return false; }

        /// <summary>Selection card rect (canvas-independent screen coordinates) beside its block.</summary>
        RectInt SelCardRect(Screen2D s)
        {
            var bs = BlockScreen(_sel.Block);
            int w = 170, h = 104;
            // Beside the block, inside the room canvas (docked schedules: to their left, over the room).
            int right = _sel.Block.Style == Block.StyleDock ? bs.x - 2 : Expanded ? RX + Screen2D.BaseWidth - 2 : s.Width - 2;
            int x = _sel.Block.Style == Block.StyleDock ? bs.x - w - 2 : Math.Min(bs.x + _sel.Block.Size.x + 2, right - w);
            if (x < 2) x = 2;
            int y = Mathf.Clamp(bs.y + 24, 2, Screen.Height - StatusBar.Height - h - 2);
            return new RectInt(x, y, w, h);
        }

        RectInt SelBtn(RectInt c, int row, bool plus) => new RectInt(c.x + (plus ? 150 : 52), c.y + 34 + row * 16, 14, 13);

        bool SelectionInput(InputState input)
        {
            if (!SelectionValid()) return false;
            var c = SelCardRect(App.Screen);
            var m = input.Mouse;
            if (input.RightDown && !input.Consumed) { ClearSelection(); input.Consume(); return true; }
            if (!In(c, m)) return false;
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 2; k++)
                    if (In(SelBtn(c, row, k == 1), m)) App.Ui.Cursor = CursorLook.Hot;
            if (!input.LeftDown || input.Consumed) { return true; }
            input.Consume();
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 2; k++)
                    if (In(SelBtn(c, row, k == 1), m))
                    {
                        int d = (k == 1 ? 1 : -1) * (row == 0 ? 24 : 1);
                        Action a = () => StepSelection(d, input.Shift);
                        a();
                        RepeatWhileHeld(a);
                        return true;
                    }
            return true;
        }

        void StepSelection(int deltaHours, bool chain)
        {
            if (!SelectionEntry(out var pl, out int ei)) return;
            int want = pl.Plan.Entries[ei].StartAbs + deltaHours;
            int date = Mathf.FloorToInt(want / 24f), h = want - date * 24;
            var before = (FlightEntry[])pl.Plan.Entries.Clone();
            var cmd = new MoveFlightCommand(P.Index, pl.Id, ei, date, h, chain);
            var r = W.Execute(cmd);
            if (!r.Ok) { _sel.Result = FailText(r.Reason); return; }
            int actual = cmd.NewDate * 24 + cmd.NewHour;
            int ni = FindEntry(pl, _sel.Kind, _sel.ObjectId, actual);
            _sel.StartAbs = ni >= 0 ? pl.Plan.Entries[ni].StartAbs : actual;
            _sel.Result = Describe(before, pl, _sel.Kind, _sel.ObjectId, _sel.StartAbs, want, true);
            App.Audio.Play("SOUND/click.raw");
        }

        void DrawSelectionCard(Screen2D s)
        {
            if (!SelectionValid() || !SelectionEntry(out var pl, out int ei)) return;
            var e = pl.Plan.Entries[ei];
            var c = SelCardRect(s);
            var m = App.InputNow.Mouse;
            s.FillRect(c, Paper);
            s.FrameRect(c, Ink);
            Kit.SmallBlack.Draw(s, c.x + 5, c.y + 3, PT.Get("sel.title"));
            F.Draw(s, c.x + 5, c.y + 17, Clip(F, Code(e.From) + " - " + Code(e.To) + "  " + FlightCard.KindName(e.Kind), c.width - 10));
            string[] label = { PT.Get("sel.day"), PT.Get("sel.hour") };
            string[] value = { Std("Schd", 3000 + Weekday(W, e.StartDate)), e.StartHour.ToString("00") + ":00" };
            for (int row = 0; row < 2; row++)
            {
                int y = c.y + 34 + row * 16;
                F.Draw(s, c.x + 5, y + 1, label[row]);
                for (int k = 0; k < 2; k++)
                {
                    var b = SelBtn(c, row, k == 1);
                    s.FillRect(b, In(b, m) ? PaperDark : Paper);
                    s.FrameRect(b, Ink);
                    F.Draw(s, b.x + 7, b.y + 1, k == 1 ? ">" : "<", TextAlign.Center);
                }
                F.Draw(s, c.x + 109, y + 1, value[row], TextAlign.Center);
            }
            if (!string.IsNullOrEmpty(_sel.Result)) F.DrawBlock(s, new RectInt(c.x + 5, c.y + 68, c.width - 10, 24), _sel.Result, Color.white);
            Kit.SmallGrey.Draw(s, c.x + 5, c.yMax - 12, Clip(Kit.SmallGrey, PT.Get("sel.close"), c.width - 10));
        }

        /// <summary>Outline of the selected flight's bar segments in a grid (drawn by <see cref="DrawGrid"/>).</summary>
        protected void DrawSelectionOutline(Screen2D s, Block b, Plane pl, Vector2Int o)
        {
            if (!_sel.Active || _sel.Block != b || _sel.PlaneId != pl.Id || !SelectionEntry(out _, out int ei)) return;
            var e = pl.Plan.Entries[ei];
            int today = W.Clock.Date;
            int abs = e.StartAbs;
            while (abs <= e.LandAbs)
            {
                int rowStart = abs - abs % 24, segEnd = Math.Min(e.LandAbs, rowStart + 23), row = rowStart / 24 - today;
                if (row >= 0 && row < 7)
                {
                    var r = new RectInt(o.x + GridX + (abs - rowStart) * CellW - 1, o.y + GridY + row * CellH - 1, (segEnd - abs + 1) * CellW + 2, CellH + 1);
                    s.FrameRect(r, SelFrame);
                    s.FrameRect(new RectInt(r.x - 1, r.y - 1, r.width + 2, r.height + 2), SelFrame2);
                }
                abs = segEnd + 1;
            }
        }

        // ------------------------------------------------------------------ refusal notices

        string _notice;
        float _noticeUntil;
        Vector2Int _noticeAt;

        protected void Notice(string text, Vector2Int at)
        {
            if (!Enhanced || string.IsNullOrEmpty(text)) return;
            _notice = text; _noticeAt = at; _noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        void DrawNotice(Screen2D s)
        {
            if (_notice == null || Time.unscaledTime > _noticeUntil) return;
            DrawCard(s, _noticeAt.x - 95, _noticeAt.y + 22, 200, new List<(string, IFont)> { (_notice, F) });
        }

        protected static string FailText(string reason) => FailTextFor(reason);

        /// <summary>Own-words text for a planning command's refusal reason.</summary>
        public static string FailTextFor(string reason)
        {
            switch (reason)
            {
                case "outside-week": case "too-late": case "no-object": case "no-plane": case "bad-entry": return PT.Get("fail." + reason);
                case "too-far": return PT.Get("plane.range");
                case "contract-done": case "contract-planned": return PT.Get("fail.planned");
            }
            return PT.Get("fail.other", reason);
        }

        /// <summary>
        /// What a planning command did to a plane's plan, from the plans before and after: the object's
        /// resulting departure (and the wanted one when they differ), automatic ferry flights added or
        /// removed, and other flights whose departure changed.
        /// </summary>
        protected string Describe(FlightEntry[] before, Plane pl, int kind, int objectId, int actualAbs, int wantedAbs, bool moved)
        {
            var after = pl.Plan.Entries;
            string when = DayHour(actualAbs);
            if (wantedAbs != actualAbs) when = PT.Get("res.req", when, DayHour(wantedAbs));
            var parts = new List<string> { PT.Get(moved ? "res.moved" : "res.placed", when) };
            int autoBefore = 0, autoAfter = 0;
            foreach (var e in before) if (e.Kind == FlightKind.Auto) autoBefore++;
            foreach (var e in after) if (e.Kind == FlightKind.Auto) autoAfter++;
            if (autoAfter > autoBefore) parts.Add(PT.Get("res.ferry", autoAfter - autoBefore));
            else if (autoAfter < autoBefore) parts.Add(PT.Get("res.ferrygone", autoBefore - autoAfter));
            int shifted = 0;
            foreach (var e in before)
            {
                if (e.Kind == 0 || e.Kind == FlightKind.Auto || (e.Kind == kind && e.ObjectId == objectId)) continue;
                bool same = false, exists = false;
                foreach (var a in after)
                {
                    if (a.Kind != e.Kind || a.ObjectId != e.ObjectId) continue;
                    exists = true;
                    if (a.StartAbs == e.StartAbs) { same = true; break; }
                }
                if (exists && !same) shifted++;
            }
            if (shifted > 0) parts.Add(PT.Get("res.shift", shifted));
            return string.Join("; ", parts);
        }

        // ------------------------------------------------------------------ frame hooks

        /// <summary>Per-frame input of the improvements before the planner's own; true when consumed.</summary>
        bool EnhancedInput(InputState input, Vector2Int canvas)
        {
            _cardPlane = null; _cardEntry = -1;
            if (!Enhanced) { _fleetPopup = false; _searchFocus = false; ClearSelection(); return false; }
            if (SelectionInput(input)) return true;
            return FleetInput(input, canvas);
        }

        /// <summary>Dock, popups, cards and notices: drawn over the room, before the carried post-it.</summary>
        protected void DrawEnhancements(Screen2D s)
        {
            if (!Enhanced || P == null) return;
            if (Expanded)
            {
                int x0 = RX + Screen2D.BaseWidth;
                s.FillRect(new RectInt(x0, 0, s.Width - x0, 440), DockBack);
                DrawBlockList(s, VisibleDocked());
            }
            DrawFleetPanel(s);
            DrawSelectionCard(s);
            DrawHoverCard(s);
            DrawNotice(s);
        }
    }
}
