using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>Block-relative layout of one pane (rooms-and-ui-5b §2).</summary>
    public struct PaneGeom
    {
        public Vector2Int Client, Title, PageNo;
        /// <summary>Hit rects (block-relative, inclusive corners stored as RectInt x,y,x2,y2 via <see cref="In"/>).</summary>
        public RectInt Prev, Next, Index;
        /// <summary>
        /// Where the original measures the mouse inside the client area, relative to <see cref="Client"/> (where it draws).
        /// The laptop draws its lists one pixel right/below the mouse origin on the left page and one pixel left/below
        /// on the right page, so its list rows are hit from one pixel higher (Laptop.cpp vs. Block.cpp, verified live).
        /// </summary>
        public Vector2Int InputShift;
        public Vector2Int InputOrigin => Client + InputShift;
        public static RectInt R(int x1, int y1, int x2, int y2) => new RectInt(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
        public static bool In(RectInt r, Vector2Int p) => p.x >= r.xMin && p.x < r.xMax && p.y >= r.yMin && p.y < r.yMax;
    }

    /// <summary>
    /// Shared base of the globe room (filofax) and the laptop (rooms-and-ui-5, 5b): the earth globe,
    /// planner blocks with list/detail panes, the flight-plan week grid and the full planner state
    /// machine (pick up post-its from lists or the grid, place / swap / drag-and-drop them, move flights
    /// by their edges, extend / clear plans). Every change goes through the operations commands.
    /// </summary>
    public abstract partial class PlannerRoom : RoomScreen
    {
        public const int ClientW = 172, ClientH = 170;
        // Grid geometry (client-relative, §5.1).
        public const int GridX = 24, GridY = 17, CellW = 6, CellH = 19;

        protected PlannerState State;
        protected GlobeView Globe;
        protected Carry Carry;
        /// <summary>Screen position where the current post-it was taken (drag-and-drop, §6.4).</summary>
        Vector2Int _carryFrom;
        bool _carryTakenThisClick;

        // Window move.
        Block _moving;
        Vector2Int _moveGrab;
        // Edge drag (§6.5).
        int _dragMode, _dragPlane, _dragKind, _dragObject, _dragStartAbs;
        // Auto-repeat of page buttons.
        Action _repeat;
        float _repeatAt;
        // Double click (§6.6).
        float _lastClickTime;
        Vector2Int _lastClickPos;
        // Hovered flight for the right-pane tip (§4.6).
        protected Block TipBlock;
        protected int TipEntry = -1;
        protected Plane TipPlane;
        /// <summary>City row hovered in a city list (right-pane city tip, 5b §4.1).</summary>
        protected Block CityTipBlock;
        protected int CityTipId = -1;
        // Turning the globe with a hold control.
        protected int TurnDir;
        int _autoTarget = -1;

        protected World W => App.Session?.World;
        protected Player P => App.Session?.Local;
        protected UiKit Kit => App.Ui.Kit;

        // ------------------------------------------------------------------ room specifics

        protected abstract bool IsLaptop { get; }
        /// <summary>Globe origin ("GlobeOffset") in canvas coordinates.</summary>
        protected abstract Vector2Int GlobeOrigin { get; }
        /// <summary>Live blocks, front first.</summary>
        protected abstract IReadOnlyList<Block> Blocks { get; }
        protected abstract PaneGeom Geom(Block b, bool right);
        /// <summary>Block chrome hits (close, move strip, tabs, switches). Returns true when handled.</summary>
        protected abstract bool ChromeClick(Block b, Vector2Int local, InputState input);
        protected abstract void ChromeHover(Block b, Vector2Int local);
        protected abstract void DrawBlockChrome(Screen2D s, Block b, int x, int y, Vector2Int local);
        protected abstract void DrawBlockOverlay(Screen2D s, Block b, int x, int y, Vector2Int local);
        /// <summary>Right click on a block outside detail clients: hide / close.</summary>
        protected abstract void BlockRightClick(Block b);
        protected abstract void BringToFront(Block b);
        /// <summary>An own plane was clicked on the globe (§2.5, §3.5).</summary>
        protected abstract void OpenPlaneFromGlobe(Plane pl, Vector2Int canvasPos);
        protected abstract bool ShowPlanesOf(int player);
        /// <summary>Clamp a block position while moving.</summary>
        protected abstract Vector2Int ClampBlock(Block b, Vector2Int pos);
        protected virtual string PhotoExt => IsLaptop ? ".glj" : ".gli";
        /// <summary>True while the planner ignores input (empty laptop battery); may handle its own buttons.</summary>
        protected virtual bool InputBlocked(InputState input) => false;

        protected override void OnCreate()
        {
            Office.LetterMailbox.Attach(App);
            State = PlannerState.Of(P);
            Globe = new GlobeView(Assets, EarthFile);
            if (!State.AlphaInitialised && W != null && W.City(W.HomeCity) is Sim.Data.City home)
            {
                State.AlphaInitialised = true;
                State.EarthAlpha = GlobeView.TargetFor(home.GlobeX);
            }
            Globe.Alpha = State.EarthAlpha;
        }

        protected virtual string EarthFile => "gli/EarthGlo.lbm";

        protected override void OnRoomEnter()
        {
            if (P != null) ValidateBlocks();
        }

        protected override void OnLeaveRoom()
        {
            if (P != null) P.PlannerDragging = false;
            if (State != null && Globe != null) State.EarthAlpha = Globe.Alpha;
            Globe?.Dispose();
        }

        /// <summary>On entry: details of objects that no longer exist go back to the index (§1.2).</summary>
        protected void ValidateBlocks()
        {
            foreach (var b in Blocks) ValidatePanes(b);
        }

        protected void ValidatePanes(Block b)
        {
            foreach (var pane in new[] { b.Left, b.Right })
            {
                if (pane.IsIndex) continue;
                bool ok = true;
                switch (pane.Type)
                {
                    case BlockType.Planes: ok = P.Planes.Find(pane.SelectedId) != null; break;
                    case BlockType.Orders: ok = P.Contracts.Find(pane.SelectedId) != null; break;
                    case BlockType.Freight: ok = P.Freights.Find(pane.SelectedId) != null; break;
                    case BlockType.Routes: ok = pane.SelectedId >= 0 && pane.SelectedId < W.RouteTable.Count && P.RentRoutes[pane.SelectedId].Licensed; break;
                }
                if (!ok) BackToIndex(pane);
            }
            UpdatePages(b.Left, b);
            UpdatePages(b.Right, b);
        }

        // ------------------------------------------------------------------ helpers

        protected string Std(string group, int id) => Assets.StdText.Get(group, id) ?? "";

        protected void Advise(int id, params object[] args)
        {
            var raw = Assets.StdText.Get("Advi", id);
            if (raw != null) App.Advisors.Queue(AdvisorKind.Assistant, DialogText.Format(raw, args));
        }

        protected Vector2Int BlockScreen(Block b) => new Vector2Int(RX + b.Pos.x, b.Pos.y);

        protected Block HitBlock(Vector2Int screen)
        {
            foreach (var b in Blocks)
            {
                if (b.Closing) continue;
                var l = screen - BlockScreen(b);
                if (l.x >= 0 && l.y >= 0 && l.x < b.Size.x && l.y < b.Size.y && BlockPixelHit(b, l)) return b;
            }
            return null;
        }

        protected virtual bool BlockPixelHit(Block b, Vector2Int local) => true;

        protected Block TopBlock { get { foreach (var b in Blocks) if (!b.Closing) return b; return null; } }

        protected Plane SelectedPlane(Block b) => b != null && b.Left.IsDetail(BlockType.Planes) ? P.Planes.Find(b.Left.SelectedId) : null;

        /// <summary>The block whose plane grid is visible (left pane: plane detail, page 0).</summary>
        protected bool GridVisible(Block b) => b != null && b.Left.IsDetail(BlockType.Planes) && b.Left.Page == 0 && SelectedPlane(b) != null;

        public static int Weekday(World w, int date) => (w.Clock.StartWeekday + date) % 7;

        protected int Duration(Plane pl, int from, int to) => W.FlightHours(from, to, pl?.Speed ?? 700);

        bool CarryCities(Carry c, out int from, out int to)
        {
            from = to = 0;
            switch (c.Kind)
            {
                case FlightKind.Route: { var r = W.RouteById(c.Id); if (r == null) return false; from = r.From; to = r.To; return true; }
                case FlightKind.Contract: { var o = P.Contracts.Find(c.Id); if (o == null) return false; from = o.From; to = o.To; return true; }
                case FlightKind.Freight: { var o = P.Freights.Find(c.Id); if (o == null) return false; from = o.From; to = o.To; return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ update / input

        public override void Animate(float dt)
        {
            if (Globe == null) return;
            if (TurnDir != 0)
            {
                Globe.Alpha = (Globe.Alpha + TurnDir * (int)(1000 * 20 * Math.Min(dt, 0.1f))) & 0xFFFF;
                _autoTarget = -1;
            }
            else if (_autoTarget >= 0 && Globe.TurnTowards(_autoTarget)) _autoTarget = -1;
            if (State != null) State.EarthAlpha = Globe.Alpha;
        }

        /// <summary>Starts the automatic turn to a city (§4.3).</summary>
        protected void TurnToCity(int cityId)
        {
            var c = W.City(cityId);
            if (c != null) _autoTarget = GlobeView.TargetFor(c.GlobeX);
        }

        public override void Update(InputState input, float dt)
        {
            if (P == null || Menus == null) { base.Update(input, dt); return; }
            TurnDir = 0;
            P.PlannerDragging = _dragMode != 0;
            TipBlock = null; TipEntry = -1; TipPlane = null; CityTipBlock = null; CityTipId = -1;
            bool inBar = input.Mouse.y >= Screen.Height - StatusBar.Height;
            if (InputBlocked(input)) { base.Update(input, dt); return; }
            if (!Menus.IsOpen && !IsDialogOpen && !inBar) PlannerInput(input);
            else if (!input.LeftHeld) { _moving = null; EndEdgeDrag(); _repeat = null; }
            base.Update(input, dt);
        }

        void PlannerInput(InputState input)
        {
            var m = input.Mouse;
            var canvas = new Vector2Int(m.x - RX, m.y);

            // Window move.
            if (_moving != null)
            {
                if (input.LeftHeld) _moving.Pos = ClampBlock(_moving, canvas - _moveGrab);
                else _moving = null;
                App.Ui.Cursor = CursorLook.Hot;
                input.Consume();
                return;
            }
            // Edge drag.
            if (_dragMode != 0)
            {
                if (input.LeftHeld) { EdgeDragStep(input); App.Ui.Cursor = CursorLook.MoveH; input.Consume(); return; }
                EndEdgeDrag();
            }
            // Auto-repeat of page buttons (1 s, then every 130 ms).
            if (_repeat != null)
            {
                if (!input.LeftHeld) _repeat = null;
                else if (Time.unscaledTime >= _repeatAt) { _repeat(); _repeatAt = Time.unscaledTime + 0.13f; }
            }

            var hb = HitBlock(m);

            if (input.RightDown && !input.Consumed)
            {
                // Tutorial up to step 1506: right click does nothing at all (rooms-and-ui-5b §6.5).
                if (Tutorial.TutorialView.BlockRightClick(this)) { input.Consume(); return; }
                if (!Carry.Empty) { Carry = Carry.None; input.Consume(); return; }
                if (hb != null)
                {
                    input.Consume();
                    var l = m - BlockScreen(hb);
                    for (int side = 0; side < 2; side++)
                    {
                        bool right = side == 1;
                        if (right && !hb.Double) continue;
                        var g = Geom(hb, right);
                        if (InClient(g, l) && !hb.PaneOf(right).IsIndex) { BackToIndex(hb.PaneOf(right)); UpdatePages(hb.PaneOf(right), hb); return; }
                    }
                    BlockRightClick(hb);
                    return;
                }
                return; // base: leave the room
            }

            if (input.LeftDown && !input.Consumed)
            {
                bool dbl = Time.unscaledTime - _lastClickTime < 0.35f && (m - _lastClickPos).sqrMagnitude < 16;
                _lastClickTime = Time.unscaledTime; _lastClickPos = m;
                if (hb != null)
                {
                    input.Consume();
                    BringToFront(hb);
                    var l = m - BlockScreen(hb);
                    if (BlockLeftDown(hb, l, input, dbl)) return;
                    return;
                }
                if (WorldLeftDown(canvas, input)) { input.Consume(); return; }
                if (!Carry.Empty) { input.Consume(); return; }
            }

            if (input.LeftUp && _carryTakenThisClick)
            {
                _carryTakenThisClick = false;
                if (!Carry.Empty && (m - _carryFrom).sqrMagnitude > 100 && Tutorial.TutorialView.ShortcutsAllowed)
                {
                    // Drag-and-drop: the release counts as a placement click (§6.4).
                    var tb = HitBlock(m);
                    if (tb != null && GridVisible(tb) && InGrid(tb, m - BlockScreen(tb), out _, out _)) Place(tb, m - BlockScreen(tb), input);
                    else Carry = Carry.None;
                }
            }

            // Hover feedback.
            if (hb != null)
            {
                var l = m - BlockScreen(hb);
                ChromeHover(hb, l);
                HoverPanes(hb, l, input);
                if (!Carry.Empty) App.Ui.Cursor = CursorLook.Hot;
            }
            else WorldHover(canvas, input);
        }

        protected static bool InClient(PaneGeom g, Vector2Int l) =>
            l.x >= g.Client.x - 1 && l.x < g.Client.x + ClientW && l.y >= g.Client.y - 1 && l.y < g.Client.y + ClientH;

        /// <summary>Starts dragging a block by its title strip.</summary>
        protected void BeginMove(Block b, Vector2Int local) { _moving = b; _moveGrab = local; }

        /// <summary>Registers an auto-repeat action for a held button.</summary>
        protected void RepeatWhileHeld(Action a) { _repeat = a; _repeatAt = Time.unscaledTime + 1f; }

        bool BlockLeftDown(Block b, Vector2Int l, InputState input, bool dbl)
        {
            if (ChromeClick(b, l, input)) return true;
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                if (right && !b.Double) continue;
                var g = Geom(b, right);
                var pane = b.PaneOf(right);
                if (pane.Type == BlockType.None) continue;
                if (pane.Page > 0 && PaneGeom.In(g.Prev, l)) { PageStep(b, pane, -1); RepeatWhileHeld(() => PageStep(b, pane, -1)); return true; }
                if (pane.Page < pane.Pages - 1 && PaneGeom.In(g.Next, l)) { PageStep(b, pane, 1); RepeatWhileHeld(() => PageStep(b, pane, 1)); return true; }
                if (!pane.IsIndex && PaneGeom.In(g.Index, l)) { BackToIndex(pane); UpdatePages(pane, b); PaperSound(); return true; }
                if (InClient(g, l)) { PaneClick(b, pane, right, l - (pane.IsIndex ? g.InputOrigin : g.Client), input, dbl); return true; }
            }
            return false;
        }

        protected void PageStep(Block b, Pane pane, int d)
        {
            int np = Mathf.Clamp(pane.Page + d, 0, Math.Max(0, pane.Pages - 1));
            if (np == pane.Page) return;
            pane.Page = np;
            PaperSound();
        }

        protected void PaperSound() { if (!IsLaptop) App.Audio.Play("SOUND/paper.raw"); }

        protected void BackToIndex(Pane pane)
        {
            int sel = pane.SelectedId;
            pane.IsIndex = true;
            UpdatePagesFor(pane);
            // Reopen on the page that contains the previous selection.
            int idx = IndexOfItem(pane.Type, sel);
            pane.Page = idx >= 0 ? idx / BlockType.ListPageSize(pane.Type) : 0;
            pane.Page = Mathf.Clamp(pane.Page, 0, Math.Max(0, pane.Pages - 1));
        }

        protected void OpenDetail(Block b, Pane pane, int id)
        {
            pane.IsIndex = false;
            pane.SelectedId = id;
            pane.Page = 0;
            UpdatePages(pane, b);
            PaperSound();
            if (pane.Type == BlockType.Cities) TurnToCity(id);
        }

        // ------------------------------------------------------------------ world (outside blocks)

        /// <summary>Globe plane click; subclasses add icons and buttons. True when handled.</summary>
        protected virtual bool WorldLeftDown(Vector2Int canvas, InputState input)
        {
            var pl = PlaneAtGlobe(canvas);
            if (pl != null) { OpenPlaneFromGlobe(pl, canvas); return true; }
            return false;
        }

        protected virtual void WorldHover(Vector2Int canvas, InputState input)
        {
            var pl = PlaneAtGlobe(canvas);
            if (pl != null) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(pl.Name); }
            if (!Carry.Empty) App.Ui.Cursor = CursorLook.Hot;
        }

        /// <summary>Screen position (canvas) of a plane on the globe, or false if behind / not shown.</summary>
        protected bool PlaneGlobePos(Plane pl, out Vector2 canvas)
        {
            canvas = default;
            if (!Globe.Project(pl.GlobeX, pl.GlobeY, out var o)) return false;
            int jitter = pl.Name != null && pl.Name.Length > 1 ? (pl.Name[0] % 3) - 1 : 0, jy = pl.Name != null && pl.Name.Length > 1 ? (pl.Name[1] % 3) - 1 : 0;
            canvas = GlobeOrigin + GlobeView.CentreOffset + o + new Vector2(jitter, jy);
            return true;
        }

        protected Plane PlaneAtGlobe(Vector2Int canvas)
        {
            if (P == null || !ShowPlanesOf(P.Index)) return null;
            foreach (var pl in P.Planes.Items)
                if (PlaneGlobePos(pl, out var c) && Mathf.Abs(canvas.x - c.x) <= 9 && Mathf.Abs(canvas.y - c.y) <= 9) return pl;
            return null;
        }

        // ------------------------------------------------------------------ pane clicks

        void PaneClick(Block b, Pane pane, bool right, Vector2Int c, InputState input, bool dbl)
        {
            if (pane.IsIndex) { ListClick(b, pane, right, c, input, dbl); return; }
            switch (pane.Type)
            {
                case BlockType.Planes: PlaneDetailClick(b, pane, c, input); break;
                case BlockType.Orders: OrderDetailClick(b, pane, c, false); break;
                case BlockType.Freight: OrderDetailClick(b, pane, c, true); break;
                case BlockType.Routes: RouteDetailClick(b, pane, c, input); break;
                case BlockType.Cities: CityDetailClick(b, pane, c); break;
            }
        }

        void ListClick(Block b, Pane pane, bool right, Vector2Int c, InputState input, bool dbl)
        {
            int rh = BlockType.RowHeight(pane.Type);
            if (c.y < 0) return;
            int row = c.y / rh;
            int idx = pane.Page * BlockType.ListPageSize(pane.Type) + row;
            if (row >= BlockType.ListPageSize(pane.Type)) return;
            switch (pane.Type)
            {
                case BlockType.Planes:
                    {
                        var l = PlaneList(b);
                        if (idx >= l.Count || !Tutorial.TutorialView.PlaneDetailAllowed) break;
                        OpenDetail(b, pane, l[idx].Id);
                        if (Tutorial.TutorialView.At(Sim.Tutorial.TutorialStep.OpenPlane)) Tutorial.TutorialView.PlaneOpened(l[idx].Name);
                        break;
                    }
                case BlockType.Cities:
                    {
                        var l = CityList();
                        if (idx < l.Count) OpenDetail(b, pane, l[idx].Id);
                        break;
                    }
                case BlockType.Info:
                    if (idx < 4) OpenDetail(b, pane, idx);
                    break;
                case BlockType.Orders:
                case BlockType.Routes:
                case BlockType.Freight:
                    {
                        int id = RightListId(pane.Type, idx);
                        if (id < 0) return;
                        bool info = c.x >= 1 && c.x <= 15 && c.y - row * rh >= 10;
                        if (info) { if (!Tutorial.TutorialView.BlockOrderInfo()) OpenDetail(b, pane, id); return; }
                        if (dbl && GridVisible(b) && !Carry.Empty && pane.Type != BlockType.Freight && Tutorial.TutorialView.ShortcutsAllowed) { Append(b); return; }
                        TakePostIt(b, pane.Type, id, input);
                        if (!Carry.Empty && pane.Type == BlockType.Orders && Tutorial.TutorialView.At(Sim.Tutorial.TutorialStep.TakeOrder)) Tutorial.TutorialView.OrderTaken();
                        break;
                    }
            }
        }

        int RightListId(int type, int idx)
        {
            switch (type)
            {
                case BlockType.Orders: { var l = OrderList(); return idx < l.Count ? l[idx].Id : -1; }
                case BlockType.Freight: { var l = FreightList(); return idx < l.Count ? l[idx].Id : -1; }
                case BlockType.Routes: { var l = RouteList(); return idx < l.Count ? l[idx] : -1; }
            }
            return -1;
        }

        /// <summary>Row flags against the selected plane (§4.3): 1 planned, 2 out of range / ≥ 24 h, 4 too small.</summary>
        protected int RowFlags(Block b, int type, int id)
        {
            var pl = SelectedPlane(b);
            int flags = 0;
            int from = 0, to = 0, persons = 0;
            switch (type)
            {
                case BlockType.Orders:
                    {
                        var o = P.Contracts.Find(id);
                        if (o == null) return 0;
                        if (o.InPlan != PlanState.NotPlanned) flags |= 1;
                        from = o.From; to = o.To; persons = o.Persons;
                        break;
                    }
                case BlockType.Freight:
                    {
                        var o = P.Freights.Find(id);
                        if (o == null) return 0;
                        if (o.TonsOpen <= 0) flags |= 1;
                        from = o.From; to = o.To;
                        break;
                    }
                case BlockType.Routes:
                    {
                        var r = W.RouteTable[id];
                        from = r.From; to = r.To;
                        break;
                    }
            }
            if (pl != null)
            {
                if (W.Distance(from, to) > pl.Range * 1000 || Duration(pl, from, to) >= 24) flags |= 2;
                if (type == BlockType.Orders && persons > pl.MaxPassengers + pl.MaxPassengersFC) flags |= 4;
            }
            return flags;
        }

        /// <summary>Pick up a list row as a post-it, or refuse with an advisor line (§4.3).</summary>
        void TakePostIt(Block b, int type, int id, InputState input)
        {
            int flags = RowFlags(b, type, id);
            if (type == BlockType.Orders)
            {
                var o = P.Contracts.Find(id);
                if (o.InPlan == PlanState.Done) { Advise(2311); return; }
                if (o.InPlan == PlanState.Planned)
                {
                    var holder = PlaneCarrying(FlightKind.Contract, id);
                    if (holder != null && holder != SelectedPlane(b)) Advise(2300, holder.Name); else Advise(2312);
                    return;
                }
                if ((flags & 2) != 0) { Advise(2310); return; }
                if ((flags & 4) != 0) { Advise(2316); return; }
                Carry = new Carry { Kind = FlightKind.Contract, Id = id };
            }
            else if (type == BlockType.Freight)
            {
                if ((flags & 2) != 0) { Advise(2310); return; }
                if ((flags & 1) != 0) { Advise(2330); return; }
                Carry = new Carry { Kind = FlightKind.Freight, Id = id };
            }
            else
            {
                if ((flags & 2) != 0) { Advise(2310); return; }
                Carry = new Carry { Kind = FlightKind.Route, Id = W.RouteTable[id].Id };
            }
            _carryFrom = input.Mouse;
            _carryTakenThisClick = true;
            App.Audio.Play("SOUND/paptake.raw");
        }

        protected Plane PlaneCarrying(int kind, int id)
        {
            foreach (var pl in P.Planes.Items)
                if (pl.Plan.Contains(kind, id)) return pl;
            return null;
        }

        // ------------------------------------------------------------------ grid

        /// <summary>Cell under a block-local point: day row 0..6 and hour 0..23.</summary>
        protected bool InGrid(Block b, Vector2Int l, out int row, out int hour)
        {
            var c = l - Geom(b, false).Client;
            row = hour = -1;
            if (c.x < GridX || c.x > GridX + 24 * CellW - 1 || c.y < GridY || c.y > GridY + 7 * CellH - 1) return false;
            hour = (c.x - GridX) / CellW;
            row = (c.y - GridY) / CellH;
            return true;
        }

        /// <summary>Index of the plan entry covering absolute hour <paramref name="abs"/>, or −1.</summary>
        protected static int EntryAt(Plane pl, int abs)
        {
            var f = pl.Plan.Entries;
            for (int i = 0; i < FlightPlan.Size; i++)
                if (f[i].Kind != 0 && f[i].StartAbs <= abs && f[i].LandAbs >= abs) return i;
            return -1;
        }

        void PlaneDetailClick(Block b, Pane pane, Vector2Int c, InputState input)
        {
            var pl = SelectedPlane(b);
            if (pl == null) return;
            if (pane.Page == 1)
            {
                if (c.x >= 0 && c.x <= 169 && c.y >= 155 && c.y <= 166)
                    Menus.Open(new TextEntryMenu(MenuIds.RenamePlane, 3099, pl.Name, name => W.Execute(new RenamePlaneCommand(P.Index, pl.Id, name)), new object[] { pl.Name }));
                return;
            }
            if (pane.Page != 0) return;
            var l = c + Geom(b, false).Client;
            // Extend / delete buttons (§5.1, §6.7).
            if (c.x >= 22 && c.x <= 58 && c.y >= 150 && c.y <= 164)
            {
                W.Execute(new ExtendPlanCommand(P.Index, pl.Id));
                App.Audio.Play("SOUND/click.raw");
                return;
            }
            if (c.x >= 135 && c.x <= 170 && c.y >= 150 && c.y <= 164)
            {
                int id = pl.Id;
                Menus.Open(RequestMenu.Create(RequestMenu.KillPlan, new object[] { pl.Name }, () => W.Execute(new ClearPlanCommand(P.Index, id))),
                    new Vector2Int(320, 220), 0.17f);
                return;
            }
            if (!InGrid(b, l, out int row, out int hour)) return;
            if (!Carry.Empty) { Place(b, l, input); return; }
            int abs = (W.Clock.Date + row) * 24 + hour;
            int ei = EntryAt(pl, abs);
            if (ei < 0) return;
            var e = pl.Plan.Entries[ei];
            if (e.Kind == FlightKind.Auto || !e.IsFuture(W.Clock.Date, W.Clock.Hour)) return;
            // Edges start a move (§6.5).
            if (abs == e.StartAbs || abs == e.LandAbs)
            {
                _dragMode = abs == e.StartAbs ? 1 : 2;
                _dragPlane = pl.Id; _dragKind = e.Kind; _dragObject = e.ObjectId; _dragStartAbs = e.StartAbs;
                _dragGrabOffset = abs - e.StartAbs;
                return;
            }
            // Pick up (§6.2); Shift with a route keeps it in the plan.
            if (!(input.Shift && e.Kind == FlightKind.Route))
                W.Execute(new RemoveFlightCommand(P.Index, pl.Id, ei));
            Carry = new Carry { Kind = e.Kind, Id = e.ObjectId };
            _carryFrom = input.Mouse;
            _carryTakenThisClick = true;
            App.Audio.Play("SOUND/paptake.raw");
        }

        int _dragGrabOffset;

        void EdgeDragStep(InputState input)
        {
            var b = TopBlock;
            var pl = P.Planes.Find(_dragPlane);
            if (b == null || pl == null || !GridVisible(b) || SelectedPlane(b) != pl) { EndEdgeDrag(); return; }
            int ei = FindEntry(pl, _dragKind, _dragObject, _dragStartAbs);
            if (ei < 0 || !pl.Plan.Entries[ei].IsFuture(W.Clock.Date, W.Clock.Hour)) { EndEdgeDrag(); return; }
            var l = input.Mouse - BlockScreen(b);
            var c = l - Geom(b, false).Client;
            int hour = Mathf.FloorToInt((c.x - GridX) / (float)CellW);
            int row = Mathf.Clamp(Mathf.FloorToInt((c.y - GridY) / (float)CellH), 0, 6);
            int abs = (W.Clock.Date + row) * 24 + hour - _dragGrabOffset;
            int date = Mathf.FloorToInt(abs / 24f), h = abs - date * 24;
            MoveFlightCommand.Clamp(W, ref date, ref h);
            if (date * 24 + h == pl.Plan.Entries[ei].StartAbs) return;
            var cmd = new MoveFlightCommand(P.Index, pl.Id, ei, date, h, input.Shift);
            if (W.Execute(cmd).Ok)
            {
                int ni = FindEntry(pl, _dragKind, _dragObject, cmd.NewDate * 24 + cmd.NewHour);
                _dragStartAbs = ni >= 0 ? pl.Plan.Entries[ni].StartAbs : cmd.NewDate * 24 + cmd.NewHour;
            }
        }

        static int FindEntry(Plane pl, int kind, int obj, int startAbs)
        {
            var f = pl.Plan.Entries;
            int best = -1;
            for (int i = 0; i < FlightPlan.Size; i++)
            {
                if (f[i].Kind != kind || f[i].ObjectId != obj) continue;
                if (f[i].StartAbs == startAbs) return i;
                if (best < 0) best = i;
            }
            return best;
        }

        void EndEdgeDrag()
        {
            if (_dragMode == 0) return;
            _dragMode = 0;
            if (P == null) return;
            P.PlannerDragging = false;
            var pl = P.Planes.Find(_dragPlane);
            if (pl != null) FlightPlanner.AfterEdit(W, P, pl);
        }

        /// <summary>Post-it width in cells for the carried object (duration + 1, §6.1).</summary>
        protected int CarryCells(Plane pl)
        {
            if (!CarryCities(Carry, out int from, out int to)) return 1;
            return Duration(pl, from, to) + 1;
        }

        /// <summary>Snapped grid origin of the post-it centred on the cursor (§6.1); false when not over the grid.</summary>
        protected bool SnapPostIt(Block b, Vector2Int local, out int row, out int col)
        {
            var c = local - Geom(b, false).Client;
            int w = CarryCells(SelectedPlane(b)) * CellW;
            col = Mathf.FloorToInt((c.x - w / 2f + 3 - GridX) / CellW);
            row = Mathf.FloorToInt((c.y - CellH / 2f + 9 - GridY) / CellH);
            return InGrid(b, local, out _, out _);
        }

        /// <summary>Placement click (§6.3).</summary>
        void Place(Block b, Vector2Int local, InputState input)
        {
            var pl = SelectedPlane(b);
            if (pl == null || Carry.Empty) return;
            if (!CarryCities(Carry, out int from, out int to)) { Carry = Carry.None; return; }
            if (W.Distance(from, to) > pl.Range * 1000 || Duration(pl, from, to) >= 24) { Advise(2310); return; }
            SnapPostIt(b, local, out int row, out int col);
            InGrid(b, local, out int crow, out int chour);
            var cmd = new PlaceFlightCommand(P.Index, pl.Id, Carry.Kind, Carry.Id, W.Clock.Date + row, col)
            { ClickDate = W.Clock.Date + crow, ClickHour = chour };
            // Tutorial step 1506: the first order goes to today, two hours from now (5b §6.3 step 3).
            var forced = Tutorial.TutorialView.ForcedPlacement();
            if (forced.HasValue)
                cmd = new PlaceFlightCommand(P.Index, pl.Id, Carry.Kind, Carry.Id, forced.Value.date, forced.Value.hour)
                { ClickDate = forced.Value.date, ClickHour = forced.Value.hour };
            var r = W.Execute(cmd);
            if (r.Ok && forced.HasValue) Tutorial.TutorialView.OrderPlaced();
            if (!r.Ok)
            {
                switch (r.Reason)
                {
                    case "too-far": Advise(2310); break;
                    case "contract-done": Advise(2311); break;
                    case "contract-planned": Advise(2312); break;
                    case "route-not-rented": Advise(2314); break;
                }
                return;
            }
            App.Audio.Play("SOUND/pap2.raw");
            _carryTakenThisClick = false;
            if (cmd.PickedUpKind != 0 && cmd.PickedUpKind != FlightKind.Auto) { Carry = new Carry { Kind = cmd.PickedUpKind, Id = cmd.PickedUpId }; return; }
            if (Carry.Kind == FlightKind.Route && input.Shift) return;
            if (Carry.Kind == FlightKind.Freight)
            {
                var f = P.Freights.Find(Carry.Id);
                if (f != null && f.TonsOpen > 0) return;
            }
            Carry = Carry.None;
        }

        /// <summary>Double-click on a list row: append after the last planned flight (§6.6).</summary>
        void Append(Block b)
        {
            var pl = SelectedPlane(b);
            if (pl == null || Carry.Empty) return;
            int last = W.Clock.Date * 24 + W.Clock.Hour + 2;
            foreach (var e in pl.Plan.Entries) if (e.Kind != 0 && e.LandAbs + 1 > last) last = e.LandAbs + 1;
            var cmd = new PlaceFlightCommand(P.Index, pl.Id, Carry.Kind, Carry.Id, last / 24, last % 24) { ClickDate = W.Clock.Date + 7, ClickHour = 0 };
            var r = W.Execute(cmd);
            if (!r.Ok && r.Reason == "too-far") Advise(2310);
            Carry = Carry.None;
        }

        // ------------------------------------------------------------------ detail clicks

        void OrderDetailClick(Block b, Pane pane, Vector2Int c, bool freight)
        {
            if (pane.Page != 0) return;
            int y0 = freight ? 135 : 118;
            if (c.x < 0 || c.x > 169 || c.y < y0 || c.y > y0 + 16) return;
            int id = pane.SelectedId;
            if (freight)
            {
                var f = P.Freights.Find(id);
                if (f == null || f.InPlan != 0 || f.TonsLeft != f.Tons) return;
                Menus.Open(RequestMenu.Create(f.Penalty > 0 ? RequestMenu.KillFAuftrag : RequestMenu.KillFAuftrag0,
                    new object[] { W.City(f.From).Name, W.City(f.To).Name, (long)f.Penalty }, () => Cancel(id, true)), new Vector2Int(320, 220), 0.17f);
            }
            else
            {
                var o = P.Contracts.Find(id);
                if (o == null || o.InPlan != PlanState.NotPlanned) return;
                Menus.Open(RequestMenu.Create(o.Penalty > 0 ? RequestMenu.KillAuftrag : RequestMenu.KillAuftrag0,
                    new object[] { W.City(o.From).Name, W.City(o.To).Name, (long)o.Penalty }, () => Cancel(id, false)), new Vector2Int(320, 220), 0.17f);
            }
        }

        void Cancel(int id, bool freight)
        {
            W.Execute(new CancelContractCommand(P.Index, id, freight));
            if (Carry.Kind == (freight ? FlightKind.Freight : FlightKind.Contract) && Carry.Id == id) Carry = Carry.None;
            ValidateBlocks();
        }

        void RouteDetailClick(Block b, Pane pane, Vector2Int c, InputState input)
        {
            int ri = pane.SelectedId;
            if (ri < 0 || ri >= W.RouteTable.Count) return;
            var rr = P.RentRoutes[ri];
            if (pane.Page == 0)
            {
                int Step(int x) => x >= 148 && x < 160 ? 10 : x >= 160 && x <= 172 ? -10 : 0;
                if (c.y >= 27 && c.y < 39 && Step(c.x) != 0)
                {
                    int d = Step(c.x);
                    Action a = () => W.Execute(new SetTicketPricesCommand(P.Index, ri, P.RentRoutes[ri].TicketPrice + d, P.RentRoutes[ri].TicketPriceFC));
                    a(); RepeatWhileHeld(a);
                    return;
                }
                if (c.y >= 40 && c.y < 52 && Step(c.x) != 0)
                {
                    int d = Step(c.x);
                    Action a = () => W.Execute(new SetTicketPricesCommand(P.Index, ri, P.RentRoutes[ri].TicketPrice, P.RentRoutes[ri].TicketPriceFC + d));
                    a(); RepeatWhileHeld(a);
                    return;
                }
                for (int k = 0; k < 4; k++)
                    if (c.y >= 66 + 13 * k && c.y < 79 + 13 * k && c.x < 148)
                    {
                        W.Execute(SetTicketPricesCommand.Preset(W, P.Index, ri, k));
                        App.Audio.Play("SOUND/click.raw");
                        return;
                    }
                return;
            }
            if (pane.Page == 1 && c.x >= 2 && c.x <= 169 && c.y >= 144 && c.y <= 157)
            {
                int rev = W.ReverseRouteIndex(ri);
                foreach (var pl in P.Planes.Items)
                    if (pl.Plan.Contains(FlightKind.Route, W.RouteTable[ri].Id, rev >= 0 ? W.RouteTable[rev].Id : int.MinValue))
                    { Advise(2315, pl.Name); return; }
                var r = W.RouteTable[ri];
                Menus.Open(RequestMenu.Create(RequestMenu.KillRoute, new object[] { W.City(r.From).Name, W.City(r.To).Name, (long)rr.Rent / 30, (long)rr.Rent },
                    () => { W.Execute(new DropRouteCommand(P.Index, ri)); ValidateBlocks(); }), new Vector2Int(320, 220), 0.17f);
            }
        }

        void CityDetailClick(Block b, Pane pane, Vector2Int c)
        {
            if (pane.Page != 0) return;
            int id = pane.SelectedId;
            int ci = W.CityIndex(id);
            if (!P.BranchAt(ci).Owned || id == W.HomeCity) return;
            if (c.y >= 66 && c.y < 79 && c.x <= 169)
                Menus.Open(RequestMenu.Create(RequestMenu.KillCity, null, () =>
                {
                    W.Execute(new Sim.Airport.DropBranchCommand(P.Index, ci));
                    foreach (var bl in Blocks) foreach (var pn in new[] { bl.Left, bl.Right }) if (pn.IsDetail(BlockType.Cities) && pn.SelectedId == id) BackToIndex(pn);
                }), new Vector2Int(320, 220), 0.17f);
        }

        // ------------------------------------------------------------------ hover

        void HoverPanes(Block b, Vector2Int l, InputState input)
        {
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                if (right && !b.Double) continue;
                var g = Geom(b, right);
                var pane = b.PaneOf(right);
                if (pane.Page > 0 && PaneGeom.In(g.Prev, l)) { App.Ui.Cursor = CursorLook.Hot; return; }
                if (pane.Page < pane.Pages - 1 && PaneGeom.In(g.Next, l)) { App.Ui.Cursor = CursorLook.Hot; return; }
                if (!pane.IsIndex && PaneGeom.In(g.Index, l)) { App.Ui.Cursor = CursorLook.Hot; return; }
                if (!InClient(g, l)) continue;
                var c = l - (pane.IsIndex ? g.InputOrigin : g.Client);
                if (pane.IsIndex)
                {
                    int rh = BlockType.RowHeight(pane.Type);
                    int row = c.y / Math.Max(1, rh);
                    if (c.y >= 0 && row < BlockType.ListPageSize(pane.Type) && ListCount(b, pane.Type) > pane.Page * BlockType.ListPageSize(pane.Type) + row)
                    {
                        App.Ui.Cursor = CursorLook.Hot;
                        if (pane.Type == BlockType.Cities && !right) { CityTipBlock = b; CityTipId = CityList()[pane.Page * BlockType.ListPageSize(pane.Type) + row].Id; }
                    }
                    continue;
                }
                if (pane.Type == BlockType.Planes && pane.Page == 0 && !right)
                {
                    var pl = SelectedPlane(b);
                    if (pl == null) continue;
                    if (c.x >= 22 && c.x <= 58 && c.y >= 150 && c.y <= 164) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(3021); continue; }
                    if (c.x >= 135 && c.x <= 170 && c.y >= 150 && c.y <= 164) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(3022); continue; }
                    if (!InGrid(b, l, out int row, out int hour)) continue;
                    int abs = (W.Clock.Date + row) * 24 + hour;
                    int ei = EntryAt(pl, abs);
                    if (ei < 0) continue;
                    var e = pl.Plan.Entries[ei];
                    if (b == TopBlock) { TipBlock = b; TipEntry = ei; TipPlane = pl; }
                    if (!Carry.Empty || e.Kind == FlightKind.Auto) continue;
                    bool unlocked = e.IsFuture(W.Clock.Date, W.Clock.Hour);
                    if (unlocked && (abs == e.StartAbs || abs == e.LandAbs)) App.Ui.Cursor = CursorLook.MoveH;
                    else if (unlocked) App.Ui.Cursor = CursorLook.Hot;
                }
                else if (pane.Type == BlockType.Routes && pane.Page == 0 && c.y >= 27 && c.y < 118) App.Ui.Cursor = CursorLook.Hot;
            }
        }

        protected int ListCount(Block b, int type)
        {
            switch (type)
            {
                case BlockType.Planes: return PlaneList(b).Count;
                case BlockType.Cities: return CityList().Count;
                case BlockType.Orders: return OrderList().Count;
                case BlockType.Freight: return FreightList().Count;
                case BlockType.Routes: return RouteList().Count;
                case BlockType.Info: return 4;
            }
            return 0;
        }

        // ------------------------------------------------------------------ data lists

        protected bool PlaneRed(Plane pl) => pl.CrewPilots < pl.Pilots || pl.CrewAttendants < pl.Attendants || pl.Problem > 0;

        protected List<Plane> PlaneList(Block b)
        {
            var l = P.Planes.ToList();
            // Filter while a right detail shows "planes using this" (§4.2).
            if (b != null && b.Left.Type == BlockType.Planes && !b.Right.IsIndex)
            {
                var r = b.Right;
                if ((r.Type == BlockType.Orders || r.Type == BlockType.Freight) && r.Page == 1)
                    l = l.FindAll(pl => pl.Plan.Contains(r.Type == BlockType.Orders ? FlightKind.Contract : FlightKind.Freight, r.SelectedId));
                else if (r.Type == BlockType.Routes && r.Page == 2 && r.SelectedId >= 0 && r.SelectedId < W.RouteTable.Count)
                {
                    int rev = W.ReverseRouteIndex(r.SelectedId);
                    int a = W.RouteTable[r.SelectedId].Id, z = rev >= 0 ? W.RouteTable[rev].Id : int.MinValue;
                    l = l.FindAll(pl => pl.Plan.Contains(FlightKind.Route, a, z));
                }
            }
            l.Sort((x, y) =>
            {
                int rx = PlaneRed(x) ? 0 : 1, ry = PlaneRed(y) ? 0 : 1;
                return rx != ry ? rx - ry : string.CompareOrdinal(x.Name, y.Name);
            });
            return l;
        }

        List<Sim.Data.City> _cities;
        protected List<Sim.Data.City> CityList()
        {
            if (_cities != null) return _cities;
            _cities = new List<Sim.Data.City>(W.Data.Cities.All);
            _cities.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return _cities;
        }

        protected List<Contract> OrderList()
        {
            var l = P.Contracts.ToList().FindAll(c => c.InPlan != PlanState.Done);
            l.Sort((a, b) =>
            {
                int oa = a.Okay != 0 && a.InPlan != 0 ? 1 : 0, ob = b.Okay != 0 && b.InPlan != 0 ? 1 : 0;
                if (oa != ob) return oa - ob;
                if (a.LastDate != b.LastDate) return a.LastDate - b.LastDate;
                return string.CompareOrdinal(W.City(a.From).Name, W.City(b.From).Name);
            });
            return l;
        }

        protected List<FreightContract> FreightList()
        {
            var l = P.Freights.ToList().FindAll(c => c.InPlan != -1 && c.TonsLeft > 0);
            l.Sort((a, b) => a.LastDate != b.LastDate ? a.LastDate - b.LastDate : string.CompareOrdinal(W.City(a.From).Name, W.City(b.From).Name));
            return l;
        }

        /// <summary>Rented route indices sorted by from/to name.</summary>
        protected List<int> RouteList()
        {
            var l = new List<int>();
            for (int i = 0; i < W.RouteTable.Count && i < P.RentRoutes.Length; i++) if (P.RentRoutes[i].Licensed) l.Add(i);
            l.Sort((a, b) =>
            {
                var ra = W.RouteTable[a]; var rb = W.RouteTable[b];
                int c = string.CompareOrdinal(W.City(ra.From).Name, W.City(rb.From).Name);
                return c != 0 ? c : string.CompareOrdinal(W.City(ra.To).Name, W.City(rb.To).Name);
            });
            return l;
        }

        protected int IndexOfItem(int type, int id)
        {
            switch (type)
            {
                case BlockType.Planes: return P.Planes.ToList().FindIndex(p => p.Id == id);
                case BlockType.Cities: return CityList().FindIndex(c => c.Id == id);
                case BlockType.Orders: return OrderList().FindIndex(c => c.Id == id);
                case BlockType.Freight: return FreightList().FindIndex(c => c.Id == id);
                case BlockType.Routes: return RouteList().IndexOf(id);
                case BlockType.Info: return id;
            }
            return -1;
        }

        protected void UpdatePages(Pane pane, Block b) { UpdatePagesFor(pane, b); }

        protected void UpdatePagesFor(Pane pane, Block b = null)
        {
            int pages = 1;
            if (pane.Type == BlockType.None) pages = 1;
            else if (pane.IsIndex)
            {
                int n = ListCount(b, pane.Type);
                pages = Math.Max(1, (n + BlockType.ListPageSize(pane.Type) - 1) / BlockType.ListPageSize(pane.Type));
            }
            else
                switch (pane.Type)
                {
                    case BlockType.Planes: pages = 5; break;
                    case BlockType.Orders: case BlockType.Freight: pages = 2; break;
                    case BlockType.Routes: pages = 3; break;
                    case BlockType.Cities:
                        {
                            var c = W.City(pane.SelectedId);
                            pages = c == null ? 1 : 1 + c.TextCount + c.PhotoCount;
                            break;
                        }
                    case BlockType.Info: pages = InfoPages(pane.SelectedId); break;
                }
            pane.Pages = pages;
            if (pane.Page >= pages) pane.Page = pages - 1;
            if (pane.Page < 0) pane.Page = 0;
        }
    }
}
