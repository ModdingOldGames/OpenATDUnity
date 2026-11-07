using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// MENU_FLUEGE (rooms-and-ui-2 §4.18): the arrivals/departures monitor of the home airport, opened by walking
    /// onto a flight monitor in the hall. Home city centred on top, then up to 4 arrivals and 4 departures of all
    /// airlines' route and order flights, scanned hour by hour from the previous hour on: time, airline dot and
    /// abbreviation, "From-To" (shortened to codes when too wide), gate ("-" none, "ext" external, else number).
    /// A marker blinks next to flights within an hour whose plane is at or near the airport. Any click closes.
    /// QUIRK (original): the hour test compares the clock hour without wrapping, so only today's flights appear,
    /// and departures show the start hour + 1.
    /// </summary>
    public sealed class FluegeMenu : PopupMenu
    {
        const string Lib = "gli/fluege.gli";
        float _t;

        public override int Type => MenuIds.Fluege;
        public override Vector2Int Size => new Vector2Int(360, 300);
        public override Vector2Int? FixedPos => new Vector2Int(140, 70);
        public override Texture Background => Assets.Texture(Lib, "MONITOR");

        public override void Update(InputState input, Vector2Int l)
        {
            _t += Time.unscaledDeltaTime;
            if (input.LeftDown) { input.Consume(); Close(); }
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var w = App.Session?.World;
            if (w == null) return;
            var f = Kit.SmallWhite;
            var home = w.Data.Cities.Find(w.HomeCity);
            f.DrawBlock(s, new RectInt(x + 63, y + 32, 247, 14), home?.Name ?? "", Color.white, TextAlign.Center);
            f.Draw(s, x + 89, y + 49, Std("Misc", 1500), TextAlign.Right);
            f.Draw(s, x + 123, y + 49, Std("Misc", 1502));
            f.Draw(s, x + 280, y + 49, Std("Misc", 1503));
            f.Draw(s, x + 89, y + 154, Std("Misc", 1500), TextAlign.Right);
            f.Draw(s, x + 123, y + 154, Std("Misc", 1501));
            f.Draw(s, x + 280, y + 154, Std("Misc", 1503));
            bool blinkOn = ((int)(_t * 20f) / 4) % 2 == 0;   // alternates every 4 ticks
            Section(s, x, y, w, arrivals: true, blinkOn);
            Section(s, x, y, w, arrivals: false, blinkOn);
        }

        void Section(Screen2D s, int x, int y, World w, bool arrivals, bool blinkOn)
        {
            int time = w.Clock.Hour - 1, row = 0, baseY = arrivals ? 62 : 167;
            for (int pass = 1; pass < 48 && row < 4; pass++, time++)
            {
                for (int c = 0; c < Player.Count && row < 4; c++)
                {
                    var p = w.Players[c];
                    if (arrivals && p.IsOut) continue;
                    foreach (var plane in p.Planes.ToList())
                    {
                        if (row >= 4) break;
                        foreach (var e in plane.Plan.Entries)
                        {
                            if (row >= 4) break;
                            if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract) continue;
                            if (e.Gate == GateCode.NotHome) continue;
                            bool hit = arrivals
                                ? e.LandDate - w.Clock.Date == (time + 1) / 24 && e.LandHour == time + 1 && e.To == w.HomeCity
                                : e.StartDate - w.Clock.Date == time / 24 && e.StartHour == time && e.From == w.HomeCity;
                            if (!hit) continue;
                            DrawRow(s, x, y + baseY + 13 * row, w, c, plane, e, arrivals, blinkOn);
                            row++;
                        }
                    }
                }
            }
        }

        void DrawRow(Screen2D s, int x, int ry, World w, int airline, Plane plane, FlightEntry e, bool arrivals, bool blinkOn)
        {
            var f = Kit.SmallWhite;
            int hour = arrivals ? e.LandHour : e.StartHour + 1;
            bool near = arrivals
                ? e.LandDate == w.Clock.Date && Mathf.Abs(e.LandHour - w.Clock.Hour) <= 1
                  && (plane.Location == PlaneLocation.AtGate || plane.Location == w.HomeCity || plane.Location == PlaneLocation.Landing)
                : e.StartDate == w.Clock.Date && Mathf.Abs(e.StartHour + 1 - w.Clock.Hour) <= 1 && plane.Location == PlaneLocation.AtGate;
            if (near) s.Draw(Assets.Texture(Lib, blinkOn ? "BLINK1" : "BLINK2"), x + 40, ry + 3);
            f.Draw(s, x + 57, ry, hour.ToString().PadLeft(2) + ":00");
            s.Draw(Assets.Texture(Lib, "DOT" + (airline + 1)), x + 90, ry + 1);
            f.Draw(s, x + 101, ry, w.Players[airline].Abbreviation ?? "");
            var from = w.Data.Cities.Find(e.From);
            var to = w.Data.Cities.Find(e.To);
            string text = (from?.Name ?? "") + "-" + (to?.Name ?? "");
            if (f.Measure(text) > 167) text = (from?.Name ?? "") + "-" + (to?.ShortCode ?? "");
            if (f.Measure(text) > 167) text = (from?.ShortCode ?? "") + "-" + (to?.ShortCode ?? "");
            f.Draw(s, x + 123, ry, text);
            string gate = e.Gate == GateCode.None ? "-" : e.Gate == GateCode.NotHome ? "ext" : (e.Gate + 1).ToString();
            f.Draw(s, x + 280, ry, gate);
        }
    }
}
