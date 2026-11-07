using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;
using PT = OpenAT.View.Rooms.Planner.PlannerTexts;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// The flight detail card of the planner improvements (hovering a flight): kind, full city names,
    /// departure and landing, gate, the original's validity text, then the caller's notes. Shared by the
    /// planner rooms and the schedule panel at the order counters.
    /// </summary>
    public static class FlightCard
    {
        public const int Width = 190;
        public const float Delay = 0.35f;
        static readonly Color32 Paper = new Color32(236, 228, 204, 255), Ink = new Color32(40, 40, 40, 255);

        public static string KindName(int kind) => kind == FlightKind.Route ? PT.Get("kind.route") : kind == FlightKind.Contract ? PT.Get("kind.contract")
            : kind == FlightKind.Freight ? PT.Get("kind.freight") : PT.Get("kind.auto");

        public static string DayHour(World w, GameAssets a, int abs) =>
            (a.StdText.Get("Schd", 3000 + PlannerRoom.Weekday(w, abs / 24)) ?? "") + " " + (abs % 24).ToString("00") + ":00";

        /// <summary>The card's lines for flight <paramref name="e"/> of <paramref name="plane"/> (player <paramref name="p"/> of world <paramref name="w"/>).</summary>
        public static List<(string text, IFont font)> Lines(World w, GameAssets a, UiKit kit, Player p, Plane plane, FlightEntry e)
        {
            var lines = new List<(string, IFont)>
            {
                (KindName(e.Kind), kit.SmallBlack),
                ((w.City(e.From)?.Name ?? "?") + " - " + (w.City(e.To)?.Name ?? "?"), kit.SmallBlack),
                (PT.Get("dep") + ": " + DayHour(w, a, e.StartAbs), kit.SmallBlack),
                (PT.Get("arr") + ": " + DayHour(w, a, e.LandAbs), kit.SmallBlack),
            };
            if (e.GateWarning || e.Gate == GateCode.None) lines.Add((PT.Get("gate.none"), kit.SmallRed));
            else if (e.Gate == GateCode.NotHome) lines.Add((PT.Get("gate.away"), kit.SmallGrey));
            else if (e.Gate >= 0) lines.Add((PT.Get("gate", e.Gate + 1), kit.SmallBlack));
            if (e.Okay != 0) lines.Add((a.StdText.Get("Auft", 2320 + e.Okay) ?? "", kit.SmallRed));
            if (p != null && plane != null)
            {
                FlightOps.EstimateResult(w, p, plane, e, out long income, out long cost);
                lines.Add((PT.Get("money.inout", UiKit.Money(income), UiKit.Money(cost)), kit.SmallBlack));
                long net = income - cost;
                lines.Add((PT.Get(net < 0 ? "money.loss" : "money.profit", UiKit.Money(Math.Abs(net))), net < 0 ? kit.SmallRed : kit.SmallBlack));
            }
            return lines;
        }

        /// <summary>A paper card with wrapped lines at (x, y), kept on screen above the status bar; returns its rect.</summary>
        public static RectInt Draw(Screen2D s, int x, int y, int w, List<(string text, IFont font)> lines, bool measureOnly = false)
        {
            int h = 6;
            foreach (var (t, f) in lines) h += f.DrawBlock(s, new RectInt(0, 0, w - 10, 400), t ?? "", Color.white, TextAlign.Left, true) + 1;
            x = Mathf.Clamp(x, 2, s.Width - w - 2);
            y = Mathf.Clamp(y, 2, s.Height - StatusBar.Height - h - 2);
            var r = new RectInt(x, y, w, h);
            if (measureOnly) return r;
            s.FillRect(r, Paper);
            s.FrameRect(r, Ink);
            int yy = y + 4;
            foreach (var (t, f) in lines) yy += f.DrawBlock(s, new RectInt(x + 5, yy, w - 10, 400), t ?? "", Color.white) + 1;
            return r;
        }
    }
}
