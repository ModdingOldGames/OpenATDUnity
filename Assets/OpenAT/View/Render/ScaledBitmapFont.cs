using System.Collections.Generic;
using OpenAT.Formats;
using UnityEngine;

namespace OpenAT.View.Render
{
    /// <summary>
    /// An original .mcf font drawn at a fractional size with the window's real pixels (OpenAT high-resolution
    /// planner): the glyph strip is upscaled with <see cref="PixelArtScaler"/> and sampled bilinearly, and
    /// every glyph starts on a render-texture pixel. Same letters as <see cref="BitmapFont"/>, smaller.
    /// </summary>
    public sealed class ScaledBitmapFont : IFont
    {
        static readonly Dictionary<BitmapFont, Texture2D> Smooth = new Dictionary<BitmapFont, Texture2D>();

        readonly BitmapFont _src;
        readonly float _scale;

        public ScaledBitmapFont(BitmapFont src, float scale)
        {
            _src = src;
            _scale = scale;
        }

        McfFont F => _src.Font;
        public int LineHeight => Mathf.CeilToInt(F.CellHeight * _scale);
        public int BlockLineHeight => Mathf.CeilToInt(_src.BlockLineHeight * _scale);

        Texture2D Texture
        {
            get
            {
                if (Smooth.TryGetValue(_src, out var t) && t != null) return t;
                var raw = F.ToRgbaStrip(out int w, out int h);
                var px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], raw[i * 4 + 3]);
                return Smooth[_src] = PixelArtScaler.Build(px, w, h, "font");
            }
        }

        static bool IsBreak(char c) => c == '\n' || c == TextEncoding.LineBreak;

        float Adv(char c) => F.Advance(c) * _scale;

        float MeasureF(string s)
        {
            float w = 0, best = 0;
            foreach (char c in s)
            {
                if (IsBreak(c)) { best = Mathf.Max(best, w); w = 0; continue; }
                w += Adv(c);
            }
            return Mathf.Max(best, w);
        }

        public int Measure(string s) => string.IsNullOrEmpty(s) ? 0 : Mathf.CeilToInt(MeasureF(s));

        public int Draw(Screen2D s, int x, int y, string text, TextAlign align = TextAlign.Left) => Draw(s, x, y, text, Color.white, align);

        public int Draw(Screen2D s, int x, int y, string text, Color32 tint, TextAlign align = TextAlign.Left)
        {
            if (string.IsNullOrEmpty(text)) return x;
            float w = MeasureF(text);
            float fx = x;
            if (align != TextAlign.Left) fx -= align == TextAlign.Center ? w / 2 : w;
            var tex = Texture;
            int k = PixelArtScaler.Factor;
            float iw = 1f / tex.width, ih = 1f / tex.height;
            // Glyph size in render-texture pixels (the screen's transform and density).
            var unit = s.ToDevice(new Rect(0, 0, F.CellWidth * _scale, F.CellHeight * _scale));
            var org = s.ToDevice(new Rect(fx, y, 0, 0)).position;
            float pen = 0, line = 0, step = unit.width / (F.CellWidth * _scale);
            float lineStep = Mathf.Round(_src.BlockLineHeight * _scale * unit.height / (F.CellHeight * _scale));
            foreach (char ch in text)
            {
                if (IsBreak(ch)) { pen = 0; line += lineStep; continue; }
                int c = TextEncoding.ToByte(ch);
                int cell = c == ' ' ? -1 : F.GlyphCell(c);
                if (cell >= 0)
                {
                    var r = new Rect(Mathf.Round(org.x + pen), Mathf.Round(org.y) + line, unit.width, unit.height);
                    // Half a texel inside the cell, so the neighbouring glyphs do not bleed in.
                    float u0 = 0.5f * iw, u1 = (F.CellWidth * k - 0.5f) * iw;
                    float v0 = (cell * F.CellHeight * k + 0.5f) * ih, v1 = ((cell + 1) * F.CellHeight * k - 0.5f) * ih;
                    s.DrawDeviceQuad(tex, r, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), tint);
                }
                pen += F.Advance(c) * _scale * step;
            }
            return Mathf.CeilToInt(fx + w);
        }

        /// <summary>Word-wrapped text inside <paramref name="box"/>. Returns the height used.</summary>
        public int DrawBlock(Screen2D s, RectInt box, string text, Color32 tint, TextAlign align = TextAlign.Left, bool measureOnly = false)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float lh = _src.BlockLineHeight * _scale;
            int lines = 0, i = 0;
            while (i < text.Length)
            {
                // The longest run that fits, broken after a space when possible.
                int start = i, lastSpace = -1;
                float w = 0;
                bool forced = false;
                while (i < text.Length)
                {
                    char ch = text[i];
                    if (IsBreak(ch)) { forced = true; break; }
                    float adv = Adv(ch);
                    if (w + adv > box.width && i > start) break;
                    if (ch == ' ') lastSpace = i;
                    w += adv;
                    i++;
                }
                int end = i;
                if (!forced && i < text.Length && lastSpace > start) { end = lastSpace; i = lastSpace + 1; }
                else if (forced) i++;
                int y = box.y + Mathf.RoundToInt(lines * lh);
                if (!measureOnly && y + LineHeight <= box.yMax + 1)
                {
                    string line = text.Substring(start, end - start).TrimEnd();
                    int x = align == TextAlign.Left ? box.x : align == TextAlign.Center ? box.x + box.width / 2 : box.xMax;
                    Draw(s, x, y, line, tint, align);
                }
                lines++;
            }
            return Mathf.CeilToInt(lines * lh);
        }
    }
}
