using System;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// Drawing of a plane's flight-plan week (rooms-and-ui-5b §5), shared by the planner rooms and the
    /// counters' planner panel (OpenAT). It draws from the world it is given, so it can also show a trial's
    /// isolated copy. Geometry: grid at (<see cref="PlannerRoom.GridX"/>, <see cref="PlannerRoom.GridY"/>) of
    /// the client, 24 cells of <see cref="PlannerRoom.CellW"/> × 7 rows of <see cref="PlannerRoom.CellH"/>.
    /// </summary>
    public static class WeekGrid
    {
        const int GridX = PlannerRoom.GridX, GridY = PlannerRoom.GridY, CellW = PlannerRoom.CellW, CellH = PlannerRoom.CellH;
        public static readonly Color32 FerryDots = new Color32(60, 60, 60, 255), NowLine = new Color32(210, 0, 0, 255);

        /// <summary>Sched sprite frame n (frame 0 = SCHED01, the laptop grid).</summary>
        public static Texture2D Sched(GameAssets a, int frame) => a.Texture("gli/glstd.gli", "SCHED" + (frame + 1).ToString("00"));

        /// <summary>The laptop's grid background, day labels (hovered row red), past part of today and, enhanced, the "now" line.</summary>
        public static void DrawBackground(Screen2D s, GameAssets a, UiKit kit, World w, Vector2Int o, int hoverRow, bool enhanced)
        {
            s.Draw(Sched(a, 0), o.x + 21, o.y - 2);
            int today = w.Clock.Date;
            for (int d = 0; d < 7; d++)
                (d == hoverRow ? kit.SmallRed : kit.SmallBlack).Draw(s, o.x + 2, o.y + 19 + 19 * d, a.StdText.Get("Schd", 3000 + PlannerRoom.Weekday(w, today + d)) ?? "");
            int past = w.Clock.MinuteOfDay / 10;
            s.FillRect(new RectInt(o.x + GridX, o.y + GridY, Math.Min(144, past), CellH), new Color32(0, 0, 0, 80));
            if (enhanced) s.FillRect(new RectInt(o.x + GridX + Math.Min(143, past), o.y + GridY - 1, 1, CellH + 1), NowLine);
        }

        /// <summary>Every flight of a plan in the visible week, one segment per day row (bars may cross midnight).</summary>
        public static void DrawEntries(Screen2D s, GameAssets a, UiKit kit, World w, FlightEntry[] f, Vector2Int o, bool enhanced, IFont codes = null)
        {
            int today = w.Clock.Date;
            int first = today * 24, last = first + 7 * 24 - 1;
            string Code(int id) => w.City(id)?.ShortCode ?? "?";
            for (int i = 0; i < FlightPlan.Size; i++)
            {
                var e = f[i];
                if (e.Kind == 0 || e.LandAbs < first || e.StartAbs > last) continue;
                int cells = e.LandAbs - e.StartAbs + 1;
                bool locked = !e.IsFuture(today, w.Clock.Hour);
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
                        DrawBar(s, a, kit, x, y, e.Kind, cells, Code(e.From), Code(e.To), e.Okay, locked, abs - e.StartAbs, segEnd - e.StartAbs, codes);
                        // OpenAT: ferry flights carry a dotted line, so they differ from other bars without colour.
                        if (enhanced && e.Kind == FlightKind.Auto)
                            for (int px = x + (abs - e.StartAbs) * CellW + 1; px < x + (segEnd - e.StartAbs + 1) * CellW - 1; px += 2)
                                s.FillRect(new RectInt(px, y + CellH - 4, 1, 1), FerryDots);
                        if (e.GateWarning && abs == e.StartAbs) s.Draw(Sched(a, 18), o.x + GridX + (abs - rowStart) * CellW, y + 2);
                    }
                    abs = segEnd + 1;
                }
            }
        }

        /// <summary>Calls <paramref name="segment"/> with the grid rect of each day-row segment of a flight.</summary>
        public static void Segments(World w, FlightEntry e, Vector2Int o, Action<RectInt> segment)
        {
            int today = w.Clock.Date;
            int abs = e.StartAbs;
            while (abs <= e.LandAbs)
            {
                int rowStart = abs - abs % 24, segEnd = Math.Min(e.LandAbs, rowStart + 23), row = rowStart / 24 - today;
                if (row >= 0 && row < 7)
                    segment(new RectInt(o.x + GridX + (abs - rowStart) * CellW, o.y + GridY + row * CellH, (segEnd - abs + 1) * CellW, CellH));
                abs = segEnd + 1;
            }
        }

        /// <summary>A flight bar of <paramref name="cells"/> hour cells starting at (x,y) (§5.2).</summary>
        public static void DrawBar(Screen2D s, GameAssets a, UiKit kit, int x, int y, int kind, int cells, string fromCode, string toCode, int okay, bool locked, int firstCell = 0, int lastCell = int.MaxValue, IFont codes = null)
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
                s.Draw(Sched(a, frame), x + i * CellW, y);
                if (okay != 0 && (kind == FlightKind.Contract || kind == FlightKind.Freight))
                    s.Draw(Sched(a, i == 0 ? 41 : i == cells - 1 ? 45 : 44), x + i * CellW, y);
            }
            if (locked && cells > 1 && firstCell <= 1 && lastCell >= 1) s.Draw(Sched(a, 17), x + CellW, y + 1);
            DrawBarCodes(s, kit, x, y, cells, fromCode, toCode, firstCell, lastCell, codes);
        }

        /// <summary>
        /// The two city codes of a bar (§5.2): departure on the upper line from the bar's left end, arrival on
        /// the lower line right-aligned at its right end, so both fit even on a short bar. Flights under 4 h
        /// use the very small font (shifted 2 px down / right); flights over 6 h move both codes one cell
        /// inward. A code is only drawn on the row of its own end of the bar, and only if it stays on that row
        /// (a bar that starts late in the day or lands early in the morning loses that code).
        /// <paramref name="codes"/> (OpenAT high-resolution planner) replaces both bitmap fonts.
        /// </summary>
        static void DrawBarCodes(Screen2D s, UiKit kit, int x, int y, int cells, string fromCode, string toCode, int firstCell, int lastCell, IFont codes)
        {
            int hours = cells - 1;
            bool tiny = hours < 4, inward = hours > 6;
            IFont f = codes ?? (tiny ? kit.VerySmall : kit.SmallBlack);
            if (f == null) return;
            int nudge = tiny && codes == null ? 2 : 0;
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
    }
}
