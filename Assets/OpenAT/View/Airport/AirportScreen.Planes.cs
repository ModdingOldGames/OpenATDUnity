using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// Planes at the home airport (airport-and-people.md §9.2, "CPlaneTypes::BlitPlaneAt"): runway planes
    /// (landing roll, take-off) as the small per-airline icon in the half-speed parallax space, and the
    /// close-up planes behind the windows built from nose + one cabin segment per 60 seats + tail +
    /// engines, each piece with the airline's colour overlay. Drawn after the horizon and before the
    /// bricks, so the window bricks lie over them. Positions come from the Sim (<see cref="HallState.Planes"/>).
    /// </summary>
    public sealed partial class AirportScreen
    {
        const string PlaneLib = "gli/glplanes.gli";
        /// <summary>The original's universal plane bitmap list, in its index order.</summary>
        static readonly string[] PlaneBms =
        {
            "999SB", "999SV", "999SR", "999SG",
            "FLUG07", "FLUG08", "FLUG09", "FLUG10", "FLUG11",
            "FLUGB01", "FLUGB02", "FLUGB03", "FLUGB06", "FLUGB07",
            "FLUGV01", "FLUGV02", "FLUGV03", "FLUGV06", "FLUGV07",
            "FLUGR01", "FLUGR02", "FLUGR03", "FLUGR06", "FLUGR07",
            "FLUGG01", "FLUGG02", "FLUGG03", "FLUGG06", "FLUGG07",
        };

        bool _debugCameraDone;
        static GameAssets _planeAssets;
        static readonly Dictionary<string, HallSprite> PlaneSprites = new Dictionary<string, HallSprite>();

        HallSprite PlaneBm(int index, bool opaque = false)
        {
            if (_planeAssets != Assets) { PlaneSprites.Clear(); _planeAssets = Assets; }
            string name = PlaneBms[index];
            string key = (opaque ? "o|" : "k|") + name;
            if (PlaneSprites.TryGetValue(key, out var sp)) return sp;
            var bmp = Assets.Bitmap(PlaneLib, name);
            sp = bmp == null ? null : new HallSprite { Tex = GameAssets.ToTexture(bmp, "plane " + name, keyed: !opaque), W = bmp.Width, H = bmp.Height };
            PlaneSprites[key] = sp;
            return sp;
        }

        string FlightDisplayTooltip(int index) =>
            _h.Flights is IHallFlightTooltips t ? t.FlightDisplayTooltip(index) : _h.Flights?.FlightDisplayText(index);

        void DrawPlanes(Screen2D s)
        {
            DebugFlights.HallShown(App);
            if (!_debugCameraDone)
            {
                _debugCameraDone = true;
                if (DebugFlights.CameraX is int cx) { ViewX = cx; ClampView(); _manualScroll = true; }
            }
            var planes = _h.Planes;
            // Runway planes first (landing −1 / take-off −2), then the close-ups behind the windows.
            for (int pass = 0; pass < 2; pass++)
                foreach (var r in planes)
                {
                    if (r.Ort == 0) continue;
                    bool runway = r.Ort == PlaneLocation.Landing || r.Ort == PlaneLocation.TakingOff;
                    if (runway != (pass == 0)) continue;
                    var plane = (uint)r.Airline < (uint)Player.Count ? _w.Players[r.Airline].Planes.Find(r.PlaneId) : null;
                    if (plane == null) continue;
                    if (runway) DrawRunwayPlane(s, r.Airline, r.Pos.X - ViewX / 2, r.Pos.Y + 14);
                    else if (plane.TypeId == -1 && plane.Design != null) DrawDesignedPlane(s, plane.Design, r.Pos.X - ViewX, r.Pos.Y - (ViewY - 5));
                    else DrawCloseUpPlane(s, r.Airline, plane.Seats / 60, r.Pos.X - ViewX, r.Pos.Y - (ViewY - 5));
                }
        }

        /// <summary>Owner logos of a rented gate (§9.3): tiny logos at +(20,5) and +(134,5) on the gate
        /// wall (520), the small logo at +(17,76) on the departure counter (522).</summary>
        void DrawGateLogos(Screen2D s, int brickId, int owner, int x, int y)
        {
            if ((uint)owner >= (uint)Player.Count) return;
            if (brickId == 520)
            {
                var tiny = _g.Std("TLOGO" + owner);
                if (tiny == null) return;
                DrawSprite(s, tiny, x + 20, y + 5);
                DrawSprite(s, tiny, x + 134, y + 5);
            }
            else if (brickId == 522)
            {
                var small = _g.Std("SLOGO" + owner);
                if (small != null) DrawSprite(s, small, x + 17, y + 76);
            }
        }

        void DrawRunwayPlane(Screen2D s, int owner, int x, int y)
        {
            var sp = PlaneBm(owner & 3);
            if (sp != null) DrawSprite(s, sp, x - sp.W / 2, y - sp.H);
        }

        /// <summary>
        /// A designer plane behind the windows ("CXPlane::BlitPlaneAt" size 2, rooms-and-ui-7 §6): every
        /// part's side-view bitmap <c>P2D_&lt;part&gt;</c> (glstd.gli) at the foot point + the part's stored
        /// 2-D position, in the design's part order. No airline colours (the original has none for designs).
        /// </summary>
        void DrawDesignedPlane(Screen2D s, OpenAT.Sim.Data.PlaneDesign d, int x, int y)
        {
            if (x + 2000 < 0 || x - 2000 > s.Width) return;
            foreach (var p in d.Parts)
            {
                var sp = _g.Std("P2D_" + p.ShortName);
                if (sp != null) DrawSprite(s, sp, x + p.Pos2dX, y + p.Pos2dY);
            }
        }

        /// <summary>Close-up plane with its foot point at (x, y): pieces left to right, centred on x.</summary>
        void DrawCloseUpPlane(Screen2D s, int owner, int cabins, int x, int y)
        {
            int o = 9 + (owner & 3) * 5;
            HallSprite noseBase = PlaneBm(9), cabinBase = PlaneBm(10, true), tailBase = PlaneBm(11), wide = PlaneBm(4);
            HallSprite nose = PlaneBm(o), cabin = PlaneBm(o + 1, true), tail = PlaneBm(o + 2);
            HallSprite engines1 = PlaneBm(8), engines2 = PlaneBm(7);
            if (noseBase == null || cabinBase == null || tailBase == null || nose == null || cabin == null || tail == null) return;
            if (x + 2000 < 0 || x - 2000 > s.Width) return;

            int dx = -(nose.W + tail.W + cabin.W * cabins) / 2;
            DrawSprite(s, noseBase, x + dx, y - noseBase.H);
            if (owner != 0) DrawSprite(s, nose, x + dx, y - noseBase.H);
            dx += nose.W;
            for (int c = 0; c < cabins; c++)
            {
                DrawSprite(s, cabinBase, x + dx, y - cabinBase.H);
                if (owner != 0) DrawSprite(s, cabin, x + dx, y - cabinBase.H);
                dx += cabin.W;
            }
            int wideW = wide?.W ?? 0;
            if (engines1 != null) DrawSprite(s, engines1, x - wideW / 2 + 28, y - 20 - 42 - 57);
            DrawSprite(s, tailBase, x + dx, y - tailBase.H + 19);
            if (owner != 0) DrawSprite(s, tail, x + dx, y - tailBase.H + 19);
            if (engines2 != null) DrawSprite(s, engines2, x - wideW / 2, y - 20 - 8 - 19);
        }
    }
}
