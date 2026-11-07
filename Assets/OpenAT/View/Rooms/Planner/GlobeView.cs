using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// The 3D earth of the globe room and the laptop (rooms-and-ui-5 §4): an orthographic sphere of
    /// radius 184 textured from the 512-wide equirectangular earth image (gli/EarthGlo.lbm, EarthLap.lbm
    /// or EarthVir.lbm with the palette of EarthAll.lbm), lit by gli/shade.pcx (0..63 light level, colour
    /// × (level+5)/40 with blue overflow into red/green). Rotation <see cref="Alpha"/> is in 1/65536 turns.
    /// City and plane positions use the original projection (§4.2). Route/flight lines and city dots are
    /// rasterised into the globe picture (§4.4).
    /// </summary>
    public sealed class GlobeView
    {
        public const int Radius = 184;
        const int Size = 2 * Radius + 2;               // bitmap 370x370, sphere centre at (185,185)
        const int C = Radius + 1;
        /// <summary>Sphere centre relative to the globe origin ("GlobeOffset"), measured on the room art.</summary>
        public static readonly Vector2Int CentreOffset = new Vector2Int(178, 183);

        static short[] _texRow, _texCol;          // per bitmap pixel; -1 = outside the sphere
        static byte[] _shade;

        readonly Color32[] _mix = new Color32[256 * 64];
        readonly byte[] _earth;
        readonly int _earthW, _earthH;
        readonly Color32[] _pixels = new Color32[Size * Size];
        readonly Texture2D _tex;
        int _renderedAlpha = int.MinValue;
        int _renderedSignature;

        /// <summary>Rotation, 65536 = 360° (the original's EarthAlpha).</summary>
        public int Alpha;

        public struct Line { public float Lon1, Lat1, Lon2, Lat2; public Color32 Colour; public int Steps; }
        /// <summary>Lines and dots rasterised into the globe; call <see cref="Invalidate"/> after changing them.</summary>
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<(float lon, float lat, Color32 c)> Dots = new List<(float, float, Color32)>();
        int _overlaySignature;

        public GlobeView(GameAssets assets, string earthFile)
        {
            var img = LoadLbm(assets, earthFile);
            var pal = LoadLbm(assets, "gli/EarthAll.lbm");
            if (img != null)
            {
                _earth = img.Indices; _earthW = img.Width; _earthH = img.Height;
                bool hasPal = false;
                foreach (var c in img.Palette) if ((c & 0xFFFFFF) != 0) { hasPal = true; break; }
                BuildMix(hasPal || pal == null ? img.Palette : pal.Palette);
            }
            else { _earth = new byte[512 * 500]; _earthW = 512; _earthH = 500; BuildMix(new uint[256]); }
            BuildTables(assets);
            _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "globe" };
        }

        static IlbmImage LoadLbm(GameAssets a, string path)
        {
            var f = a.Install.Find(path);
            if (f == null) return null;
            try { return IlbmImage.Parse(System.IO.File.ReadAllBytes(f)); } catch { return null; }
        }

        void BuildMix(uint[] pal)
        {
            for (int i = 0; i < 256; i++)
            {
                uint c = pal[i];
                // IlbmImage palette is 0x00BBGGRR or 0x00RRGGBB? Both readers in Formats use R in the low byte (like Smacker).
                int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF);
                for (int l = 0; l < 64; l++)
                {
                    int rr = r * (l + 5) / 40, gg = g * (l + 5) / 40, bb = b * (l + 5) / 40;
                    if (bb > 255) { int o = bb - 255; rr += o; gg += o; bb = 255; }
                    rr = Math.Min(255, rr); gg = Math.Min(255, gg);
                    if (rr == 0 && gg == 0 && bb == 0) bb = 8;
                    _mix[i * 64 + l] = new Color32((byte)rr, (byte)gg, (byte)bb, 255);
                }
            }
        }

        void BuildTables(GameAssets assets)
        {
            if (_texRow != null) return;
            _texRow = new short[Size * Size];
            _texCol = new short[Size * Size];
            _shade = new byte[Size * Size];
            var shade = LoadPcx(assets.Install.Find("gli/shade.pcx"), out int sw, out int sh);
            for (int i = 0; i < _texRow.Length; i++) _texRow[i] = -1;
            for (int y = 0; y <= 2 * Radius; y++)
            {
                int dy = y - Radius;
                double xs = Math.Sqrt(Radius * Radius - dy * dy);
                int row = (int)(250 + Math.Asin(dy / (double)Radius) * (2 / Math.PI) * 250);
                int ixs = (int)xs;
                for (int x = -ixs; x <= ixs; x++)
                {
                    double q = xs > 0 ? Math.Max(-1, Math.Min(1, x / xs)) : 0;
                    int col = (int)(128 + Math.Asin(q) * (2 / Math.PI) * 128);
                    int bx = C + x, by = C + dy;
                    int k = by * Size + bx;
                    _texRow[k] = (short)row;
                    _texCol[k] = (short)col;
                    int sx = x + Radius, sy = y;
                    int level;
                    if (shade != null && sx < sw && sy < sh) level = shade[sy * sw + sx] & 63;
                    else
                    {
                        // Fallback: light from the upper left.
                        double nz = Math.Sqrt(Math.Max(0, 1 - (x * x + dy * dy) / (double)(Radius * Radius)));
                        double l = 0.25 + 0.75 * Math.Max(0, (-x * 0.4 - dy * 0.5) / Radius + nz * 0.75);
                        level = (int)Math.Max(0, Math.Min(63, l * 50));
                    }
                    _shade[k] = (byte)level;
                }
            }
        }

        /// <summary>Minimal 8-bit RLE PCX reader (shade.pcx: light levels as pixel values).</summary>
        static byte[] LoadPcx(string path, out int w, out int h)
        {
            w = h = 0;
            if (path == null) return null;
            try
            {
                var d = System.IO.File.ReadAllBytes(path);
                if (d.Length < 128 || d[0] != 10 || d[3] != 8) return null;
                int x0 = d[4] | d[5] << 8, y0 = d[6] | d[7] << 8, x1 = d[8] | d[9] << 8, y1 = d[10] | d[11] << 8;
                int bpl = d[66] | d[67] << 8;
                w = x1 - x0 + 1; h = y1 - y0 + 1;
                var px = new byte[w * h];
                int p = 128;
                var line = new byte[bpl];
                for (int y = 0; y < h; y++)
                {
                    int n = 0;
                    while (n < bpl && p < d.Length)
                    {
                        byte b = d[p++];
                        if ((b & 0xC0) == 0xC0) { int cnt = b & 0x3F; byte v = p < d.Length ? d[p++] : (byte)0; while (cnt-- > 0 && n < bpl) line[n++] = v; }
                        else line[n++] = b;
                    }
                    Array.Copy(line, 0, px, y * w, Math.Min(w, bpl));
                }
                return px;
            }
            catch { return null; }
        }

        public void Invalidate() => _renderedAlpha = int.MinValue;

        // ------------------------------------------------------------------ projection (§4.2)

        /// <summary>
        /// Projects a globe position (lon east +, lat north −, degrees) to an offset from the sphere centre.
        /// Returns false when the point is on the back side. <paramref name="u"/> is the view angle (32768 = centre).
        /// </summary>
        public bool Project(float lon, float lat, out Vector2 offset, out int u)
        {
            u = ((int)((lon + 180) * 65536 / 360) - Alpha + 16384 - 1250) & 0xFFFF;
            float py = (float)(185 * Math.Sin(lat * 2.65 / 240 * Math.PI / 2));
            float yy = Math.Min(Radius, Math.Abs(py));
            float px = (float)(Math.Sin((u / 32768.0 - 1) * Math.PI) * Math.Sqrt(Radius * Radius - yy * yy));
            offset = new Vector2(px, py);
            return u > 16386 && u < 49152;
        }

        public bool Project(float lon, float lat, out Vector2 offset) => Project(lon, lat, out offset, out _);

        /// <summary>Rotation target for a city detail (§4.3).</summary>
        public static int TargetFor(int lon) => ((lon + 170) * 177 - 16000 + 1300) & 0xFFFF;

        /// <summary>Moves <see cref="Alpha"/> 2000 units toward a target by the shorter way; true when reached.</summary>
        public bool TurnTowards(int target)
        {
            int diff = (short)(target - Alpha);
            if (Math.Abs(diff) < 2000) { Alpha = target & 0xFFFF; return true; }
            Alpha = (Alpha + Math.Sign(diff) * 2000) & 0xFFFF;
            return false;
        }

        // ------------------------------------------------------------------ rendering

        public void SetOverlaySignature(int s) => _overlaySignature = s;

        void Render()
        {
            int shift = (Alpha & 0xFFFF) >> 7;
            var clear = new Color32(0, 0, 0, 0);
            for (int k = 0; k < _pixels.Length; k++)
            {
                int row = _texRow[k];
                if (row < 0) { _pixels[k] = clear; continue; }
                int col = (_texCol[k] + shift) & 511;
                int r = Math.Min(_earthH - 1, Math.Max(0, row));
                int idx = _earth[r * _earthW + Math.Min(_earthW - 1, col)];
                _pixels[k] = _mix[idx * 64 + _shade[k]];
            }
            foreach (var l in Lines) RasterLine(l);
            foreach (var d in Dots)
            {
                if (!Project(d.lon, d.lat, out var o, out int u)) continue;
                var c = Fade(d.c, u);
                for (int yy = -1; yy <= 1; yy++)
                    for (int xx = -1; xx <= 1; xx++) Put((int)Math.Round(o.x) + xx, (int)Math.Round(o.y) + yy, c);
            }
            _tex.SetPixels32(_pixels);
            _tex.Apply(false, false);
        }

        static Color32 Fade(Color32 c, int u)
        {
            int f = Math.Max(0, 255 - Math.Abs(u - 32768) / 64);
            return new Color32((byte)(c.r * f / 255), (byte)(c.g * f / 255), (byte)(c.b * f / 255), 255);
        }

        void Put(int x, int y, Color32 c)
        {
            // Texture rows are uploaded top-down by Screen2D convention: row 0 = top.
            int bx = C + x, by = C + y;
            if (bx < 0 || by < 0 || bx >= Size || by >= Size || _texRow[by * Size + bx] < 0) return;
            _pixels[by * Size + bx] = c;
        }

        void RasterLine(Line l)
        {
            float dl = l.Lon2 - l.Lon1;
            if (dl > 180) dl -= 360; else if (dl < -180) dl += 360;
            int steps = Math.Max(1, l.Steps);
            bool prevOk = false; Vector2 prev = default;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float lon = l.Lon1 + dl * t, lat = l.Lat1 + (l.Lat2 - l.Lat1) * t;
                bool ok = Project(lon, lat, out var o, out int u);
                if (ok && prevOk) Seg(prev, o, Fade(l.Colour, u));
                prev = o; prevOk = ok;
            }
        }

        void Seg(Vector2 a, Vector2 b, Color32 c)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                Put(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), c);
            }
        }

        /// <summary>Step count of a flight line by distance (§4.4).</summary>
        public static int StepsFor(int distanceM)
        {
            int km = distanceM / 1000;
            return km < 500 ? 1 : km < 1000 ? 2 : km < 2000 ? 4 : km < 5000 ? 8 : 16;
        }

        /// <summary>Draws the sphere with its centre at <paramref name="centre"/> (screen pixels).</summary>
        public void Draw(Screen2D s, Vector2Int centre)
        {
            if (_renderedAlpha != Alpha || _renderedSignature != _overlaySignature)
            {
                _renderedAlpha = Alpha;
                _renderedSignature = _overlaySignature;
                Render();
            }
            s.Draw(_tex, centre.x - C, centre.y - C);
        }

        public void Dispose()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
        }
    }
}
