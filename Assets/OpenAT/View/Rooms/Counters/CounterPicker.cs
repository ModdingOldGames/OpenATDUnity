using System;
using System.Collections.Generic;
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
    /// The plane list of the counters' planner panel (OpenAT): the panel opens on the planes that can fly the
    /// tried offer (range, flight time, seats), those already at the departure city first. Clicking one shows
    /// its week for the placement trial; clicking the plane name in the title bar comes back to the list.
    /// Drawn at the window's real resolution when <see cref="Sim.GameOptions.PlannerHiResLaptop"/> is on.
    /// </summary>
    public abstract partial class OrderCounterRoom
    {
        /// <summary>The panel shows the plane list instead of a week.</summary>
        bool _picking;
        int _pickScroll;
        PlannerFonts _panelFonts;
        IFont _panelCodes;

        bool PanelFine => Enhanced && W.Options.PlannerHiResLaptop;
        int PickRowH => PanelFine ? 18 : 26;
        int PickRows => PlannerRoom.ClientH / PickRowH;

        /// <summary>Fonts of the panel: the planner's high-resolution set, or the original fonts.</summary>
        PlannerFonts PanelFonts
        {
            get
            {
                var kit = App.Ui.Kit;
                if (PanelFine) return _panelFonts ?? (_panelFonts = PlannerRoom.FineFonts(kit, true));
                return new PlannerFonts { Black = kit.SmallBlack, Red = kit.SmallRed, Grey = kit.SmallGrey, Dark = kit.SmallGrey, Title = kit.NormalGreen ?? kit.SmallBlack };
            }
        }

        /// <summary>Bar city codes of the panel's grid (null: the original's fonts).</summary>
        IFont PanelCodes => PanelFine ? _panelCodes ?? (_panelCodes = PlannerRoom.FineCodeFont(App.Ui.Kit)) : null;

        /// <summary>Planes that can fly the tried offer, those standing at its departure city first.</summary>
        List<Plane> Eligible()
        {
            var l = Planes().FindAll(CanFly);
            if (_tryOffer == null) return l;
            int from = _tryOffer.From;
            var near = l.FindAll(p => PlaneLocation.IsOnGround(p.Location) && p.Location == from);
            l.RemoveAll(p => near.Contains(p));
            near.AddRange(l);
            return near;
        }

        void OpenPicker()
        {
            _picking = true;
            var l = Eligible();
            int i = l.FindIndex(p => p.Id == _panelPlane);
            _pickScroll = i < 0 ? 0 : Math.Max(0, Math.Min(i, l.Count - PickRows));
            App.Audio.Play("SOUND/click.raw");
        }

        void PickPlane(Plane pl)
        {
            _panelPlane = pl.Id;
            _picking = false;
            App.Audio.Play("SOUND/paptake.raw");
            OnTrialPlaneChanged();
            PadFollow();
        }

        /// <summary>Row under a window-relative point of the plane list, or −1.</summary>
        int PickRowAt(Vector2Int l)
        {
            var cm = l - new Vector2Int(21, 22);
            if (cm.x < 0 || cm.x >= PlannerRoom.ClientW || cm.y < 0 || cm.y >= PickRows * PickRowH) return -1;
            return cm.y / PickRowH;
        }

        /// <summary>Plane list input (hover, click, wheel); true when the list is shown.</summary>
        bool PickerInput(InputState input, Vector2Int l)
        {
            if (!_picking) return false;
            var list = Eligible();
            int row = PickRowAt(l);
            bool onRow = row >= 0 && _pickScroll + row < list.Count;
            // The window's page arrows (laptop window geometry) turn a page.
            bool up = InL(l, 0, 23, 19, 44) && _pickScroll > 0, down = InL(l, 0, 165, 19, 186) && _pickScroll + PickRows < list.Count;
            if (onRow || up || down) App.Ui.Cursor = CursorLook.Hot;
            if ((up || down) && input.LeftDown && !input.Consumed)
            {
                input.Consume();
                _pickScroll = Math.Max(0, Math.Min(_pickScroll + (up ? -PickRows : PickRows), list.Count - PickRows));
                App.Audio.Play("SOUND/click.raw");
                return true;
            }
            if (input.Wheel != 0 && InL(l, 21, 22, 21 + PlannerRoom.ClientW, 22 + PlannerRoom.ClientH))
                _pickScroll = Math.Max(0, Math.Min(_pickScroll - Math.Sign(input.Wheel), list.Count - PickRows));
            if (onRow && input.LeftDown && !input.Consumed) { input.Consume(); PickPlane(list[_pickScroll + row]); }
            return true;
        }

        string WhereText(Plane pl)
        {
            if (PlaneLocation.IsOnGround(pl.Location)) return Std("Schd", 1105) + " " + (W.City(pl.Location)?.Name ?? "?");
            int abs = W.Clock.Date * 24 + W.Clock.Hour, to = pl.Plan.StartCity;
            foreach (var e in pl.Plan.Entries) if (e.Kind != 0 && e.StartAbs <= abs && e.LandAbs >= abs) { to = e.To; break; }
            return Std("Schd", 1102) + " " + (W.City(to)?.Name ?? "?");
        }

        void DrawPicker(Screen2D s, Vector2Int client, Vector2Int cm)
        {
            var fs = PanelFonts;
            var list = Eligible();
            int rh = PickRowH, rows = PickRows;
            _pickScroll = Math.Max(0, Math.Min(_pickScroll, list.Count - rows));
            if (list.Count == 0)
            {
                fs.Red.DrawBlock(s, new RectInt(client.x + 2, client.y + 4, PlannerRoom.ClientW - 4, 60), PT.Get("pick.none"), Color.white);
                return;
            }
            int hover = cm.x >= 0 && cm.x < PlannerRoom.ClientW && cm.y >= 0 ? cm.y / rh : -1;
            bool fine = PanelFine;
            for (int k = 0; k < rows && _pickScroll + k < list.Count; k++)
            {
                var pl = list[_pickScroll + k];
                int y = client.y + k * rh;
                if (k == hover) s.FillRect(new RectInt(client.x, y, PlannerRoom.ClientW, rh), Highlight);
                bool here = PlaneLocation.IsOnGround(pl.Location) && pl.Location == _tryOffer?.From;
                var f = PlaneRed(pl) ? fs.Red : fs.Black;
                string cap = _tryOffer != null && _tryOffer.Freight ? UiKit.Unit(UiKit.UnitT, pl.Seats / 10) : (pl.MaxPassengers + pl.MaxPassengersFC).ToString();
                int cw = f.Measure(cap);
                f.Draw(s, client.x, y + 1, OrderCounterRoom.ClipF(f, pl.Name, 165 - cw));
                f.Draw(s, client.x + 168, y + 1, cap, TextAlign.Right);
                var g = here ? fs.Black : fs.Grey;
                string cond = UiKit.Unit(UiKit.UnitPercent, pl.Condition);
                g.Draw(s, client.x, y + (fine ? 9 : 11), OrderCounterRoom.ClipF(g, WhereText(pl), 160 - g.Measure(cond)));
                g.Draw(s, client.x + 168, y + (fine ? 9 : 11), cond, TextAlign.Right);
            }
        }

        /// <summary><see cref="Clip(BitmapFont, string, int)"/> for any font.</summary>
        static string ClipF(IFont f, string t, int w)
        {
            if (t == null) return "";
            while (t.Length > 1 && f.Measure(t) > w) t = t.Substring(0, t.Length - 1);
            return t;
        }
    }
}
