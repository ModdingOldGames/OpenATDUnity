using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Planner;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;
using PT = OpenAT.View.Rooms.Planner.PlannerTexts;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// "Try in planner" at the order counters (OpenAT planner improvements, <see cref="GameOptions.PlannerEnhancements"/>).
    /// A small tab on every offer opens a laptop-style plane schedule in the same room. On wide screens it sits
    /// in a column beside the room (the room moves left while it is open); otherwise it is a movable overlay.
    /// The counter stays as it is: clicking an offer still takes it at once. The panel never leaves or
    /// re-enters the room, and it does not need a laptop.
    /// </summary>
    public abstract partial class OrderCounterRoom
    {
        const int PanelW = 199, WinH = 210, DockColW = PlannerRoom.DockColW, TabId = 200;
        static readonly Color32 Paper = new Color32(236, 228, 204, 255), PaperDark = new Color32(206, 196, 166, 255);
        static readonly Color32 Ink = new Color32(40, 40, 40, 255), Highlight = new Color32(32, 160, 255, 60);

        protected bool Enhanced => W != null && W.Options.PlannerEnhancements && !Tutorial.TutorialView.Active;

        bool _panelOpen, _panelDrag, _panelMouse, _dragMoved;
        int _panelPlane = -1;
        Vector2Int _panelPos = new Vector2Int(-1, -1), _panelGrab;
        // The offer being tried: its slot and its content when the tab was clicked.
        int _trySlot = -1;
        OfferFingerprint _tryOffer;

        /// <summary>Default overlay position (canvas) when the panel does not fit beside the room.</summary>
        protected virtual Vector2Int PanelHome => new Vector2Int(437, 4);

        bool PanelExpanded => Enhanced && App.Screen.Width - Screen2D.BaseWidth - PlannerRoom.DockGap >= DockColW;
        /// <summary>True while the mouse is over the open panel (the room's hotspots stay quiet).</summary>
        protected bool PanelHasMouse => _panelOpen && _panelMouse;

        /// <summary>For the rooms' hotspot pass: true (and no hovered offer) while the panel has the mouse.</summary>
        protected bool PanelBlocksHotspots()
        {
            if (!PanelHasMouse) return false;
            Hover = -1;
            return true;
        }

        void ApplyPanelLayout()
        {
            if (!Enhanced) _panelOpen = false;
            PaneOriginX = _panelOpen && PanelExpanded
                ? Math.Max(0, (App.Screen.Width - Screen2D.BaseWidth - PlannerRoom.DockGap - DockColW) / 2) : (int?)null;
        }

        int CardH => PanelExpanded ? 440 - 8 - WinH - 4 : 170;
        Vector2Int PanelOrigin => PanelExpanded ? new Vector2Int(Screen2D.BaseWidth + PlannerRoom.DockGap, 4) : _panelPos;
        RectInt PanelRect { get { var o = PanelOrigin; return new RectInt(o.x, o.y, PanelW, WinH + 4 + CardH); } }
        RectInt CardRect { get { var o = PanelOrigin; return new RectInt(o.x, o.y + WinH + 4, PanelW, CardH); } }
        RectInt CloseBtn { get { var c = CardRect; return new RectInt(c.xMax - 50, c.yMax - 17, 46, 14); } }

        static bool In(RectInt r, Vector2Int p) => p.x >= r.xMin && p.x < r.xMax && p.y >= r.yMin && p.y < r.yMax;
        static bool InL(Vector2Int l, int x1, int y1, int x2, int y2) => l.x >= x1 && l.x <= x2 && l.y >= y1 && l.y <= y2;

        // ------------------------------------------------------------------ tabs on the offers

        /// <summary>Canvas rect of an offer's tab (top right of the card by default).</summary>
        protected virtual RectInt TabRect(int slot) { var r = SlotRect(slot); return new RectInt(r.xMax - 15, r.yMin + 1, 14, 12); }

        /// <summary>Hotspots of the "try in planner" tabs; registered after the cards so a tab wins over its card.</summary>
        protected void TabHotspots()
        {
            if (!Enhanced) return;
            PadRoomHome();
            for (int i = 0; i < 6; i++)
            {
                if (!SlotFilled(i) || SlotBusy(i)) continue;
                var r = TabRect(i);
                if (Hot(r.xMin, r.yMin, r.xMax - 1, r.yMax - 1, TabId + i)) App.Ui.Tooltip.Set(PT.Get("try"));
            }
        }

        /// <summary>A tab click opens the planner panel for that offer; true when <paramref name="id"/> was a tab.</summary>
        protected bool TabClick(int id)
        {
            if (id < TabId || id >= TabId + 6) return false;
            OpenPanel(id - TabId);
            return true;
        }

        protected void DrawTabs(Screen2D s)
        {
            if (!Enhanced || W == null) return;
            var m = App.InputNow.Mouse;
            for (int i = 0; i < 6; i++)
            {
                if (!SlotFilled(i) || SlotBusy(i)) continue;
                var r = TabRect(i);
                var sr = new RectInt(RX + r.x, r.y, r.width, r.height);
                bool hot = In(sr, m) || (_panelOpen && _trySlot == i);
                s.FillRect(sr, hot ? PaperDark : Paper);
                s.FrameRect(sr, Ink);
                // A tiny week grid: the planner's sign.
                for (int k = 0; k < 3; k++) s.FillRect(new RectInt(sr.x + 3, sr.y + 3 + k * 3, 8, 1), Ink);
                s.FillRect(new RectInt(sr.x + 6, sr.y + 2, 1, 8), Ink);
            }
        }

        void OpenPanel(int slot)
        {
            _panelOpen = true;
            _trySlot = slot;
            _tryOffer = OfferFingerprint.Of(W, Board, slot);
            if (_panelPos.x < 0) _panelPos = PanelHome;
            if (P.Planes.Find(_panelPlane) == null || !CanFly(P.Planes.Find(_panelPlane)))
            {
                var l = Planes();
                var ok = l.Find(CanFly);
                _panelPlane = ok?.Id ?? (l.Count > 0 ? l[0].Id : -1);
            }
            // The panel starts on the list of planes that can fly the offer.
            _picking = true;
            _pickScroll = 0;
            App.Audio.Play("SOUND/paptake.raw");
            OnTrialOpened();
            PadFollow();
        }

        void ClosePanel()
        {
            _panelOpen = false;
            _panelDrag = false;
            _picking = false;
            _trySlot = -1;
            _tryOffer = null;
            OnTrialClosed();
        }

        /// <summary>True while the board still shows the tried offer (a slot number alone is not the offer).</summary>
        bool OfferThere => _tryOffer != null && _tryOffer.SameAs(OfferFingerprint.Of(W, Board, _trySlot));

        // ------------------------------------------------------------------ planes

        static bool PlaneRed(Plane pl) => pl.CrewPilots < pl.Pilots || pl.CrewAttendants < pl.Attendants || pl.Problem > 0;

        /// <summary>Own planes in the planner's order: problems first, then by name.</summary>
        List<Plane> Planes()
        {
            var l = P.Planes.ToList();
            l.Sort((x, y) =>
            {
                int rx = PlaneRed(x) ? 0 : 1, ry = PlaneRed(y) ? 0 : 1;
                return rx != ry ? rx - ry : string.CompareOrdinal(x.Name, y.Name);
            });
            return l;
        }

        /// <summary>Range, flight time and (passengers) seats against the tried offer, as the planner's lists check them.</summary>
        bool CanFly(Plane pl) => pl != null && WhyNot(pl) == null;

        string WhyNot(Plane pl) => WhyNot(pl, _tryOffer);

        string WhyNot(Plane pl, OfferFingerprint o)
        {
            if (o == null) return null;
            if (W.Distance(o.From, o.To) > pl.Range * 1000 || W.FlightHours(o.From, o.To, pl.Speed) >= 24) return PT.Get("plane.range");
            if (!o.Freight && o.Amount > pl.MaxPassengers + pl.MaxPassengersFC) return PT.Get("plane.small");
            return null;
        }

        void StepPlane(int d)
        {
            // Steps through the planes that can fly the offer (all planes when none can).
            var l = Eligible();
            if (l.Count == 0) l = Planes();
            if (l.Count == 0) return;
            int i = l.FindIndex(p => p.Id == _panelPlane);
            _panelPlane = l[((i < 0 ? 0 : i + d) % l.Count + l.Count) % l.Count].Id;
            App.Audio.Play("SOUND/click.raw");
            OnTrialPlaneChanged();
        }

        // ------------------------------------------------------------------ input

        /// <summary>Panel input before the room's own (hotspots, cards). Consumes clicks over the panel.</summary>
        void PanelInput(InputState input)
        {
            _panelMouse = false;
            _hoverOn = false;
            if (!_panelOpen) return;
            TrialRefresh();
            PanelPadTargets(input);
            if (input.Escape) { input.Escape = false; ClosePanel(); return; }
            var c = new Vector2Int(input.Mouse.x - RX, input.Mouse.y);
            if (_panelDrag)
            {
                if (input.LeftHeld)
                {
                    var np = new Vector2Int(Mathf.Clamp(c.x - _panelGrab.x, -PanelW / 2, Screen2D.BaseWidth - PanelW / 2), Mathf.Clamp(c.y - _panelGrab.y, 0, 440 - 40));
                    if (_dragMoved || (np - _panelPos).sqrMagnitude > 4) { _dragMoved = true; _panelPos = np; }
                }
                else
                {
                    _panelDrag = false;
                    // A click on the plane name without dragging opens the plane list.
                    if (!_dragMoved && !_picking && TitleName(c - PanelOrigin)) OpenPicker();
                }
                _panelMouse = true;
                App.Ui.Cursor = CursorLook.Hot;
                input.Consume();
                return;
            }
            var r = PanelRect;
            if (!In(r, c)) { TrialOutsideInput(input, c); return; }
            _panelMouse = true;
            var o = PanelOrigin;
            var l = c - o;   // window-relative
            // Hover.
            bool steps = !_picking;
            if (InL(l, 173, 3, 191, 19) || steps && InL(l, 150, 4, 172, 18) || In(CloseBtn, c) || steps && TitleName(l)) App.Ui.Cursor = CursorLook.Hot;
            if (steps && InL(l, 150, 4, 160, 18)) App.Ui.Tooltip.Set(PT.Get("prev"));
            if (steps && InL(l, 161, 4, 172, 18)) App.Ui.Tooltip.Set(PT.Get("next"));
            if (steps && TitleName(l)) App.Ui.Tooltip.Set(PT.Get("pick"));
            if (InL(l, 173, 3, 191, 19)) App.Ui.Tooltip.Set(PT.Get("close"));
            if (steps && input.Wheel != 0 && InL(l, 0, 0, PanelW, 20)) StepPlane(input.Wheel > 0 ? -1 : 1);
            if (PickerInput(input, l))
            {
                if (input.RightDown && !input.Consumed) { input.Consume(); ClosePanel(); return; }
                if (!input.LeftDown || input.Consumed) return;
                input.Consume();
                if (InL(l, 173, 3, 191, 19) || In(CloseBtn, c)) { ClosePanel(); return; }
                if (!PanelExpanded && InL(l, 0, 0, 149, 20)) { _panelDrag = true; _dragMoved = false; _panelGrab = c - o; }
                return;
            }
            TrialGridHover(input, l);
            if (input.RightDown && !input.Consumed) { input.Consume(); if (!TrialRightClick()) ClosePanel(); return; }
            if (!input.LeftDown || input.Consumed) return;
            input.Consume();
            if (InL(l, 173, 3, 191, 19) || In(CloseBtn, c)) { ClosePanel(); return; }
            if (InL(l, 150, 4, 160, 18)) { StepPlane(-1); return; }
            if (InL(l, 161, 4, 172, 18)) { StepPlane(1); return; }
            if (TrialClick(input, c, l)) return;
            if (PanelExpanded && TitleName(l)) { OpenPicker(); return; }
            if (!PanelExpanded && InL(l, 0, 0, 149, 20)) { _panelDrag = true; _dragMoved = false; _panelGrab = c - o; }
        }

        /// <summary>The plane name in the title bar (window-relative).</summary>
        static bool TitleName(Vector2Int l) => InL(l, 41, 3, 145, 19);

        // ------------------------------------------------------------------ drawing

        protected void DrawPanel(Screen2D s)
        {
            if (!_panelOpen || W == null) return;
            if (PanelExpanded)
            {
                int x0 = RX + Screen2D.BaseWidth;
                s.FillRect(new RectInt(x0, 0, s.Width - x0, 440), new Color32(0, 0, 0, 215));
            }
            var po = PanelOrigin;
            var o = new Vector2Int(RX + po.x, po.y);
            var m = App.InputNow.Mouse;
            var l = m - o;
            const string lib = "room/globe_b.gli";
            bool held = App.InputNow.LeftHeld;
            s.SmoothArt = PanelFine;
            s.Draw(Assets.Texture(lib, "WINDOW"), o.x, o.y);
            var pl = P.Planes.Find(_panelPlane);
            var title = PanelFonts.Title;
            if (_picking)
                title.Draw(s, o.x + 41, o.y + 7, ClipF(title, PT.Get("pick"), 104));
            else
            {
                if (TitleName(l)) s.FillRect(new RectInt(o.x + 39, o.y + 4, 108, 14), Highlight);
                title.Draw(s, o.x + 41, o.y + 7, ClipF(title, pl?.Name ?? PT.Get("noplanes"), 104));
                foreach (var (x1, label) in new[] { (150, "<"), (161, ">") })
                {
                    if (InL(l, x1, 4, x1 + 10, 18)) s.FillRect(new RectInt(o.x + x1, o.y + 4, 11, 14), Highlight);
                    title.Draw(s, o.x + x1 + 5, o.y + 5, label, TextAlign.Center);
                }
            }
            if (InL(l, 173, 3, 191, 19)) s.Draw(Assets.Texture(lib, held ? "CLOSE2" : "CLOSE1"), o.x + 174, o.y);
            var client = o + new Vector2Int(21, 22);
            if (_picking)
            {
                string back = "BACK" + BlockType.Planes;
                if (Assets.Has(lib, back)) s.Draw(Assets.Texture(lib, back), client.x - 1, client.y - 2);
                s.PushClip(new RectInt(client.x - 2, client.y - 2, PlannerRoom.ClientW + 4, PlannerRoom.ClientH + 2));
                DrawPicker(s, client, l - new Vector2Int(21, 22));
                s.PopClip();
                int pages = Math.Max(1, (Eligible().Count + PickRows - 1) / PickRows);
                title.Draw(s, o.x + 104, o.y + 195, (_pickScroll / PickRows + 1) + "/" + pages, TextAlign.Center);
            }
            else if (pl != null)
            {
                var gw = TrialWorld ?? W;
                var gpl = gw.Players[P.Index].Planes.Find(pl.Id) ?? pl;
                var cm = l - new Vector2Int(21, 22);
                int hovRow = cm.x >= PlannerRoom.GridX && cm.x < PlannerRoom.GridX + 24 * PlannerRoom.CellW && cm.y >= PlannerRoom.GridY && cm.y < PlannerRoom.GridY + 7 * PlannerRoom.CellH
                    ? (cm.y - PlannerRoom.GridY) / PlannerRoom.CellH : -1;
                s.PushClip(new RectInt(client.x - 2, client.y - 2, PlannerRoom.ClientW + 4, PlannerRoom.ClientH + 2));
                WeekGrid.DrawBackground(s, Assets, App.Ui.Kit, gw, client, hovRow, true);
                DrawOfferWindow(s, client);
                WeekGrid.DrawEntries(s, Assets, App.Ui.Kit, gw, gpl.Plan.Entries, client, true, PanelCodes);
                DrawTrialOverlay(s, gw, gpl, client, cm);
                s.PopClip();
                var list = Planes();
                title.Draw(s, o.x + 104, o.y + 195, (list.FindIndex(p => p.Id == pl.Id) + 1) + "/" + list.Count, TextAlign.Center);
            }
            s.SmoothArt = false;
            DrawPanelCard(s, pl);
            DrawTrialHoverCard(s);
        }

        /// <summary>Days outside the offer's date window are shaded, as for a carried post-it (§5.3).</summary>
        void DrawOfferWindow(Screen2D s, Vector2Int client)
        {
            if (_tryOffer == null) return;
            int today = W.Clock.Date;
            for (int d = 0; d < 7; d++)
                if (today + d < _tryOffer.Date || today + d > _tryOffer.LastDate)
                    s.Draw(WeekGrid.Sched(Assets, 51), client.x + PlannerRoom.GridX - 1, client.y + PlannerRoom.GridY + d * PlannerRoom.CellH);
        }

        string Std(string g, int id) => Assets.StdText.Get(g, id) ?? "";
        string Day(int date) => Std("Schd", 3010 + PlannerRoom.Weekday(W, date));
        string DayHour(int abs) => Std("Schd", 3000 + PlannerRoom.Weekday(W, abs / 24)) + " " + (abs % 24).ToString("00") + ":00";

        /// <summary>Lines of the info card below the schedule.</summary>
        void DrawPanelCard(Screen2D s, Plane pl)
        {
            var cr = CardRect;
            var c = new RectInt(RX + cr.x, cr.y, cr.width, cr.height);
            var m = App.InputNow.Mouse;
            var kit = App.Ui.Kit;
            var f = kit.SmallBlack;
            s.FillRect(c, Paper);
            s.FrameRect(c, Ink);
            int y = c.y + 3;
            void Line(string t, BitmapFont font = null) { if (y > c.yMax - 30) return; y += (font ?? f).DrawBlock(s, new RectInt(c.x + 5, y, c.width - 10, 60), t ?? "", Color.white) + 1; }
            kit.SmallBlack.Draw(s, c.x + 5, y, PT.Get("try")); y += 13;
            if (_tryOffer != null)
            {
                var a = W.City(_tryOffer.From); var b = W.City(_tryOffer.To);
                Line(Clip(f, (a?.Name ?? "?") + " - " + (b?.Name ?? "?"), c.width - 10));
                string amount = _tryOffer.Freight ? UiKit.Unit(UiKit.UnitT, _tryOffer.Amount) : _tryOffer.Amount + " " + Std("Auft", 1000).TrimEnd(':');
                string when = _tryOffer.Date == _tryOffer.LastDate ? Day(_tryOffer.LastDate) : Std("Schd", 3009) + " " + Day(_tryOffer.LastDate);
                Line(amount + ", " + when + ", " + UiKit.Money(_tryOffer.Premium));
            }
            if (_picking) Line(PT.Get(Eligible().Count > 0 ? "pick.how" : "pick.none"), kit.SmallGrey);
            else if (pl == null) Line(PT.Get("noplanes"), kit.SmallGrey);
            else if (WhyNot(pl) is string why) Line(why, kit.SmallRed);
            if (!OfferThere && !TrialAccepted) Line(PT.Get("try.gone"), kit.SmallRed);
            else if (!_picking) DrawTrialLines(Line);
            DrawTrialButtons(s, c, m);
            bool hot = In(new RectInt(RX + CloseBtn.x, CloseBtn.y, CloseBtn.width, CloseBtn.height), m);
            var cb = new RectInt(RX + CloseBtn.x, CloseBtn.y, CloseBtn.width, CloseBtn.height);
            s.FillRect(cb, hot ? PaperDark : Paper);
            s.FrameRect(cb, Ink);
            f.Draw(s, cb.x + cb.width / 2, cb.y + 1, PT.Get("close"), TextAlign.Center);
        }
    }
}
