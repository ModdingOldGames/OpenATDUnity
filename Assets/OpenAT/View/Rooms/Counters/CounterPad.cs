using OpenAT.Sim.Contracts;
using Plane = OpenAT.Sim.Fleet.Plane;
using OpenAT.View.Rooms.Planner;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// Gamepad play in the counters' planner panel (OpenAT, <see cref="Sim.GameOptions.GamepadSupport"/>): the plane
    /// list rows, the window's buttons, the card's buttons and every hour cell of the week grid are gamepad targets,
    /// so an offer is tried and planned with the d-pad and A. LB / RB step through the planes that can fly it.
    /// Home targets for the touchpad, and where the pad cursor goes by itself: the first offer's planner tab (panel
    /// closed); the first plane of the list (panel opened); the plane's week on the offer's first day, with the trial
    /// post-it centred there (plane chosen); "Take and plan" / "Apply plan" (trial placed).
    /// </summary>
    public abstract partial class OrderCounterRoom
    {
        /// <summary>The pad changed the panel's state: its home target gets the cursor next frame.</summary>
        bool _padHomeNext;

        bool PadOn => W != null && W.Options.GamepadSupport;

        /// <summary>A step done with the pad: move the cursor to the next home target.</summary>
        void PadFollow() { if (App.InputNow.PadActive && PadOn) _padHomeNext = true; }

        /// <summary>Panel closed: the touchpad goes to the planner tab of the first offer (top to bottom, left to right)
        /// that one of the planes can fly, else of the first offer.</summary>
        protected void PadRoomHome()
        {
            if (!PadOn || _panelOpen) return;
            RectInt? best = null;
            bool bestFlyable = false;
            var planes = Planes();
            for (int i = 0; i < 6; i++)
            {
                if (!SlotFilled(i) || SlotBusy(i)) continue;
                var r = TabRect(i);
                var o = OfferFingerprint.Of(W, Board, i);
                bool flyable = o != null && planes.Exists(pl => WhyNot(pl, o) == null);
                bool first = best == null || r.y < best.Value.y - 8 || System.Math.Abs(r.y - best.Value.y) <= 8 && r.x < best.Value.x;
                if (best == null || flyable && !bestFlyable || flyable == bestFlyable && first) { best = r; bestFlyable = flyable; }
            }
            if (best.HasValue) App.Ui.Targets.Home = new Vector2Int(RX + best.Value.x + best.Value.width / 2, best.Value.y + best.Value.height / 2);
        }

        /// <summary>Absolute start hour of the first gap that fits the tried offer in the plane's live plan, or −1.</summary>
        int PadFreeSlot(Plane pl)
        {
            int dur = TryCells(pl) + 1;   // flight hours plus a turnaround hour
            int from = Mathf.Max(W.Clock.Date * 24 + W.Clock.Hour + 2, _tryOffer.Date * 24);
            int to = Mathf.Min((_tryOffer.LastDate + 1) * 24 - 1, (W.Clock.Date + 7) * 24 - 1) - dur;
            for (int h = from; h <= to; h++)
            {
                bool free = true;
                foreach (var e in pl.Plan.Entries)
                    if (e.Kind != 0 && e.StartAbs <= h + dur && e.LandAbs >= h - 1) { free = false; h = e.LandAbs + 1; break; }
                if (free) return h;
            }
            return -1;
        }

        Vector2Int? PadPanelHome(Vector2Int o)
        {
            if (_picking)
            {
                // The first plane on the page with a gap that fits the offer, else the first plane.
                var list = Eligible();
                if (list.Count <= _pickScroll) return null;
                int pick = 0;
                for (int k = 0; k < PickRows && _pickScroll + k < list.Count; k++)
                    if (PadFreeSlot(list[_pickScroll + k]) >= 0) { pick = k; break; }
                return new Vector2Int(o.x + 21 + PlannerRoom.ClientW / 2, o.y + 22 + pick * PickRowH + PickRowH / 2);
            }
            if (!_tryCarry && (CanApply || CanTakeAndApply))
                return new Vector2Int(ApplyBtn.x + RX + ApplyBtn.width / 2, ApplyBtn.y + ApplyBtn.height / 2);
            var pl = P.Planes.Find(_panelPlane);
            if (!_tryCarry || pl == null || _tryOffer == null) return null;
            // The first gap in the plane's plan that fits the flight inside the offer's dates (at least two hours from
            // now), else the offer's first day; the post-it hangs centred on the cursor.
            int start = PadFreeSlot(pl);
            if (start < 0) start = Mathf.Max(W.Clock.Date * 24 + W.Clock.Hour + 2, _tryOffer.Date * 24 + 9);
            int row = Mathf.Clamp(start / 24 - W.Clock.Date, 0, 6), hour = start % 24;
            int x = o.x + 21 + PlannerRoom.GridX + hour * PlannerRoom.CellW + TryCells(pl) * PlannerRoom.CellW / 2 - 2;
            x = Mathf.Min(x, o.x + 21 + PlannerRoom.GridX + 24 * PlannerRoom.CellW - 1);
            return new Vector2Int(x, o.y + 22 + PlannerRoom.GridY + row * PlannerRoom.CellH + PlannerRoom.CellH / 2);
        }

        void PanelPadTargets(InputState input)
        {
            if (W == null || !W.Options.GamepadSupport) return;
            var t = App.Ui.Targets;
            var o = PanelOrigin + new Vector2Int(RX, 0);
            void L(int x1, int y1, int x2, int y2) => t.Add(o.x + x1, o.y + y1, o.x + x2, o.y + y2);
            void C(RectInt r) => t.Add(new RectInt(r.x + RX, r.y, r.width, r.height));
            var home = PadPanelHome(o);
            t.Home = home;
            if (_padHomeNext && home.HasValue) { App.Ui.Pad.Warp(home.Value); _padHomeNext = false; }
            L(173, 3, 191, 19);
            C(CloseBtn);
            if (CanReset) C(ResetBtn);
            if (CanApply || CanTakeAndApply) C(ApplyBtn);
            if (_picking)
            {
                int n = Mathf.Min(PickRows, Eligible().Count - _pickScroll);
                for (int row = 0; row < n; row++) t.Add(new RectInt(o.x + 21, o.y + 22 + row * PickRowH, PlannerRoom.ClientW, PickRowH));
                if (_pickScroll > 0) L(0, 23, 19, 44);
                if (_pickScroll + PickRows < Eligible().Count) L(0, 165, 19, 186);
                return;
            }
            L(150, 4, 160, 18);
            L(161, 4, 172, 18);
            L(41, 3, 145, 19);
            for (int d = 0; d < 7; d++)
                for (int h = 0; h < 24; h++)
                    t.Add(new RectInt(o.x + 21 + PlannerRoom.GridX + h * PlannerRoom.CellW, o.y + 22 + PlannerRoom.GridY + d * PlannerRoom.CellH,
                        PlannerRoom.CellW, PlannerRoom.CellH));
            if (input.PadShoulder != 0) { StepPlane(input.PadShoulder); input.PadShoulder = 0; PadFollow(); }
        }
    }
}
