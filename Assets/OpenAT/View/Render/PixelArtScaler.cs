using System.Collections.Generic;
using UnityEngine;

namespace OpenAT.View.Render
{
    /// <summary>
    /// Smoothed upscales of pixel-art bitmaps for high-resolution drawing (OpenAT). A bitmap is enlarged
    /// three times with the Scale3x rules (edges follow diagonals instead of staircases) and sampled
    /// bilinearly, so it can be drawn at any window scale. Transparent pixels take the colour of an opaque
    /// neighbour, so bilinear filtering does not darken the edges.
    /// </summary>
    public static class PixelArtScaler
    {
        public const int Factor = 3;
        static readonly Dictionary<Texture2D, Texture2D> Cache = new Dictionary<Texture2D, Texture2D>();

        /// <summary>The smoothed version of <paramref name="src"/> (cached), or null when it cannot be read.</summary>
        public static Texture2D Smoothed(Texture2D src)
        {
            if (src == null) return null;
            if (Cache.TryGetValue(src, out var t) && t != null) return t;
            if (!src.isReadable || src.format != TextureFormat.RGBA32) { Cache[src] = null; return null; }
            if (Cache.Count > 512) Purge();
            t = Build(src);
            Cache[src] = t;
            return t;
        }

        static void Purge()
        {
            var dead = new List<Texture2D>();
            foreach (var kv in Cache) if (kv.Key == null) { dead.Add(kv.Key); if (kv.Value != null) Object.Destroy(kv.Value); }
            foreach (var k in dead) Cache.Remove(k);
        }

        static Texture2D Build(Texture2D src) => Build(src.GetPixels32(), src.width, src.height, src.name);

        /// <summary>A smoothed upscale of <paramref name="p"/> (w x h pixels, rows in texture order).</summary>
        public static Texture2D Build(Color32[] p, int w, int h, string name)
        {
            int W = w * Factor, H = h * Factor;
            var o = new Color32[W * H];
            bool Eq(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            Color32 At(int x, int y) => p[Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1)];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 a = At(x - 1, y - 1), b = At(x, y - 1), c = At(x + 1, y - 1);
                    Color32 d = At(x - 1, y), e = At(x, y), f = At(x + 1, y);
                    Color32 g = At(x - 1, y + 1), hh = At(x, y + 1), i = At(x + 1, y + 1);
                    Color32 e0 = e, e1 = e, e2 = e, e3 = e, e5 = e, e6 = e, e7 = e, e8 = e;
                    if (!Eq(b, hh) && !Eq(d, f))
                    {
                        bool db = Eq(d, b), bf = Eq(b, f), dh = Eq(d, hh), hf = Eq(hh, f);
                        if (db) e0 = d;
                        if ((db && !Eq(e, c)) || (bf && !Eq(e, a))) e1 = b;
                        if (bf) e2 = f;
                        if ((db && !Eq(e, g)) || (dh && !Eq(e, a))) e3 = d;
                        if ((bf && !Eq(e, i)) || (hf && !Eq(e, c))) e5 = f;
                        if (dh) e6 = d;
                        if ((dh && !Eq(e, i)) || (hf && !Eq(e, g))) e7 = hh;
                        if (hf) e8 = f;
                    }
                    int ox = x * Factor, oy = y * Factor;
                    o[oy * W + ox] = e0; o[oy * W + ox + 1] = e1; o[oy * W + ox + 2] = e2;
                    o[(oy + 1) * W + ox] = e3; o[(oy + 1) * W + ox + 1] = e; o[(oy + 1) * W + ox + 2] = e5;
                    o[(oy + 2) * W + ox] = e6; o[(oy + 2) * W + ox + 1] = e7; o[(oy + 2) * W + ox + 2] = e8;
                }
            Bleed(o, W, H);
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = name + " (smoothed)",
            };
            t.SetPixels32(o);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Gives transparent pixels the colour of an opaque neighbour (alpha stays 0).</summary>
        static void Bleed(Color32[] o, int w, int h)
        {
            var src = (Color32[])o.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = y * w + x;
                    if (src[k].a != 0) continue;
                    for (int n = 0; n < 4; n++)
                    {
                        int nx = x + (n == 0 ? -1 : n == 1 ? 1 : 0), ny = y + (n == 2 ? -1 : n == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var q = src[ny * w + nx];
                        if (q.a == 0) continue;
                        o[k] = new Color32(q.r, q.g, q.b, 0);
                        break;
                    }
                }
        }
    }
}
