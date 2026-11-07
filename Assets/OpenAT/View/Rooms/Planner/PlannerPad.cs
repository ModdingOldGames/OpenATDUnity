using System;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// Gamepad play in the planner (OpenAT, <see cref="Sim.GameOptions.GamepadSupport"/>). The planner reports its list
    /// rows, page buttons, plane buttons, fleet rows and every hour cell of a visible week grid as gamepad targets, so
    /// a flight is scheduled without pointing: A on a plane opens its week and puts the cursor on the first order;
    /// d-pad to an order, A takes the post-it and the cursor jumps onto the grid; left/right move it one hour,
    /// up/down one day; A places it, B drops it, and either way the cursor goes back to the list. The hour cells are
    /// targets only while a post-it is carried; otherwise each day row and each planned flight is one target (A on a
    /// flight picks it up to move it), so the d-pad crosses the week in one press. LB / RB step through the planes of the plane schedule under the
    /// cursor (or the front one), keeping a carried post-it.
    /// </summary>
    public abstract partial class PlannerRoom
    {
        bool PadOn => W != null && W.Options.GamepadSupport;

        void AddPadTargets()
        {
            if (!PadOn) return;
            var t = App.Ui.Targets;
            foreach (var b in AllBlocks)
            {
                if (b.Closing) continue;
                var bs = BlockScreen(b);
                for (int side = 0; side < 2; side++)
                {
                    bool right = side == 1;
                    if (right && !b.Double) continue;
                    var pane = b.PaneOf(right);
                    if (pane.Type == BlockType.None) continue;
                    var g = PGeom(b, right);
                    if (pane.Page > 0) t.Add(Offset(g.Prev, bs));
                    if (pane.Page < pane.Pages - 1) t.Add(Offset(g.Next, bs));
                    if (!pane.IsIndex) t.Add(Offset(g.Index, bs));
                    if (pane.IsIndex)
                    {
                        int rh = RowH(pane), size = PageSize(pane);
                        int n = Math.Min(size, ListCount(b, pane.Type) - pane.Page * size);
                        var o = bs + g.InputOrigin;
                        for (int row = 0; row < n; row++) t.Add(new RectInt(o.x, o.y + row * rh, ClientW, rh));
                    }
                    else if (!right && GridVisible(b))
                    {
                        var o = bs + g.Client;
                        if (!Carry.Empty)
                        {
                            for (int d = 0; d < 7; d++)
                                for (int h = 0; h < 24; h++)
                                    t.Add(new RectInt(o.x + GridX + h * CellW, o.y + GridY + d * CellH, CellW, CellH));
                        }
                        else AddFlightTargets(t, SelectedPlane(b), o);
                    }
                }
                if (PlaneNavRects(b, false, out var pr, out var nx)) { t.Add(Offset(pr, bs)); t.Add(Offset(nx, bs)); }
                AddChromeTargets(b, bs, t);
            }
            AddRoomTargets(t);
            t.Home = PadHome();
            if (_padHomeNext && t.Home.HasValue) { App.Ui.Pad.Warp(t.Home.Value); _padHomeNext = false; }
            if (FleetPanelShown)
            {
                var r = FleetRect;
                int top = FleetListTop(r), rows = Math.Min(FleetRows(r), FleetList().Count - Filter.Scroll);
                for (int row = 0; row < rows; row++) t.Add(new RectInt(RX + r.x, top + row * FleetRowH, r.width, FleetRowH));
            }
        }

        /// <summary>Set when the pad opened something whose home target should get the cursor next frame.</summary>
        protected bool _padHomeNext;

        /// <summary>Room controls outside the blocks as gamepad targets (the globe room: closed filofax, globe buttons).</summary>
        protected virtual void AddRoomTargets(PadTargets t) { }

        /// <summary>Home for the touchpad: the room's own (<see cref="RoomHome"/>), else the front block's first plane row,
        /// or its first order row when a plane's week is open.</summary>
        Vector2Int? PadHome()
        {
            var home = RoomHome();
            if (home.HasValue) return home;
            var b = TopBlock;
            if (b == null) return null;
            var bs = BlockScreen(b);
            if (b.Left.IsIndex && ListCount(b, b.Left.Type) > 0)
                return bs + PGeom(b, false).InputOrigin + new Vector2Int(ClientW / 2, RowH(b.Left) / 2);
            if (b.Double && b.Right.IsIndex && ListCount(b, b.Right.Type) > 0)
                return bs + PGeom(b, true).InputOrigin + new Vector2Int(ClientW / 2, RowH(b.Right) / 2);
            return null;
        }

        protected virtual Vector2Int? RoomHome() => null;

        /// <summary>The block's own controls (tabs, ...) as gamepad targets, block-relative rects offset by <paramref name="bs"/>.</summary>
        protected virtual void AddChromeTargets(Block b, Vector2Int bs, PadTargets t) { }

        /// <summary>
        /// The week without a carried post-it: each day row is a target (the order rows beside it line up with the days),
        /// and each planned flight a smaller one inside it (one rect per day row it covers), so the d-pad reaches a day
        /// from the list and then steps between that day's flights.
        /// </summary>
        void AddFlightTargets(PadTargets t, Sim.Fleet.Plane pl, Vector2Int o)
        {
            if (pl == null) return;
            for (int d = 0; d < 7; d++) t.Add(new RectInt(o.x + GridX, o.y + GridY + d * CellH, 24 * CellW, CellH));
            int first = W.Clock.Date * 24, last = first + 7 * 24 - 1;
            foreach (var e in pl.Plan.Entries)
            {
                if (e.Kind == 0) continue;
                int s = Math.Max(e.StartAbs, first), l = Math.Min(e.LandAbs, last);
                for (int h = s; h <= l;)
                {
                    int row = (h - first) / 24, end = Math.Min(l, first + row * 24 + 23);
                    t.Add(new RectInt(o.x + GridX + (h % 24) * CellW, o.y + GridY + row * CellH, (end - h + 1) * CellW, CellH));
                    h = end + 1;
                }
            }
        }

        /// <summary>A plane opened with the pad: the cursor goes to the first row of the right-hand list (the orders).</summary>
        void PadAfterOpenPlane(Block b, InputState input)
        {
            if (!input.PadActive || !PadOn || !b.Double || !b.Right.IsIndex || ListCount(b, b.Right.Type) == 0) return;
            var g = PGeom(b, true);
            App.Ui.Pad.Warp(BlockScreen(b) + g.InputOrigin + new Vector2Int(ClientW / 2, RowH(b.Right) / 2));
        }

        protected static RectInt Offset(RectInt r, Vector2Int by) => new RectInt(r.x + by.x, r.y + by.y, r.width, r.height);

        /// <summary>Where the pad cursor was when it took the post-it (it goes back there after placing).</summary>
        Vector2Int? _padReturn;

        /// <summary>
        /// A post-it taken with the pad: the cursor jumps onto the week grid so that the post-it starts at the first free
        /// hour after the plane's last planned flight (at least two hours from now), where the d-pad then adjusts it.
        /// </summary>
        void PadAfterTake(Block b, InputState input)
        {
            if (!input.PadActive || !PadOn || Carry.Empty || !GridVisible(b)) return;
            var pl = SelectedPlane(b);
            int abs = W.Clock.Date * 24 + W.Clock.Hour + 2;
            foreach (var e in pl.Plan.Entries) if (e.Kind != 0 && e.LandAbs + 1 > abs) abs = e.LandAbs + 1;
            int day = abs / 24 - W.Clock.Date, row = Mathf.Clamp(day, 0, 6), hour = day == row ? abs % 24 : 23;
            var c = PGeom(b, false).Client;
            // The post-it is centred on the cursor (SnapPostIt): half its width right of its first cell.
            int x = Mathf.Min(c.x + GridX + hour * CellW + CarryCells(pl) * CellW / 2 - 2, c.x + GridX + 24 * CellW - 1);
            int y = c.y + GridY + row * CellH + CellH / 2;
            _padReturn = input.Mouse;
            _carryTakenThisClick = false;   // letting go of A is not a drag-and-drop onto the grid
            App.Ui.Pad.Warp(BlockScreen(b) + new Vector2Int(x, y));
        }

        /// <summary>The post-it is placed: the pad cursor goes back to the list it came from.</summary>
        void PadAfterPlace(InputState input)
        {
            if (input.PadActive && _padReturn.HasValue) App.Ui.Pad.Warp(_padReturn.Value);
            _padReturn = null;
        }

        /// <summary>LB / RB: previous / next plane in the plane schedule under the cursor, else the front one.</summary>
        void PadShoulder(InputState input)
        {
            if (input.PadShoulder == 0 || !PadOn || !Enhanced) return;
            var b = HitBlock(input.Mouse);
            if (b == null || !b.Left.IsDetail(BlockType.Planes)) b = TopBlock;
            if (b == null || !b.Left.IsDetail(BlockType.Planes)) return;
            JumpPlane(b, b.Left, input.PadShoulder);
            input.PadShoulder = 0;
        }
    }
}
