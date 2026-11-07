using OpenAT.Formats;
using UnityEngine;

namespace OpenAT.View.Render
{
    public enum TextAlign { Left, Center, Right }

    /// <summary>Text drawing in virtual pixels: the original bitmap fonts, or OpenAT's high-resolution <see cref="ScaledBitmapFont"/>.</summary>
    public interface IFont
    {
        int LineHeight { get; }
        int BlockLineHeight { get; }
        int Measure(string s);
        int Draw(Screen2D s, int x, int y, string text, Color32 tint, TextAlign align = TextAlign.Left);
        int Draw(Screen2D s, int x, int y, string text, TextAlign align = TextAlign.Left);
        int DrawBlock(Screen2D s, RectInt box, string text, Color32 tint, TextAlign align = TextAlign.Left, bool measureOnly = false);
    }

    /// <summary>
    /// Draws text with an original .mcf font. Glyphs live in one texture strip (top-down);
    /// character codes are the original single-byte codes (see Formats/TextEncoding), the cell of a
    /// character comes from the font's glyph map (<see cref="McfFont.GlyphCell"/>).
    /// </summary>
    public sealed class BitmapFont : IFont
    {
        public readonly McfFont Font;
        public readonly Texture2D Texture;

        public int LineHeight => Font.CellHeight;
        public int BlockLineHeight => Mathf.RoundToInt(Font.CellHeight * Font.LineSpace);

        public BitmapFont(McfFont font)
        {
            Font = font;
            var rgba = font.ToRgbaStrip(out int w, out int h);
            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Texture.LoadRawTextureData(rgba);
            Texture.Apply(false, true);
        }

        public int Measure(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0, best = 0;
            foreach (char c in s)
            {
                if (c == '\n' || c == TextEncoding.LineBreak) { best = Mathf.Max(best, w); w = 0; continue; }
                w += Font.Advance(c);
            }
            return Mathf.Max(best, w);
        }

        /// <summary>Single line (newlines move down one line). Returns the x after the last glyph.</summary>
        public int Draw(Screen2D s, int x, int y, string text, Color32 tint, TextAlign align = TextAlign.Left)
        {
            if (string.IsNullOrEmpty(text)) return x;
            if (align != TextAlign.Left)
            {
                int w = Measure(text);
                x -= align == TextAlign.Center ? w / 2 : w;
            }
            int startX = x;
            foreach (char ch in text)
            {
                int c = TextEncoding.ToByte(ch);
                if (ch == '\n' || ch == TextEncoding.LineBreak) { x = startX; y += BlockLineHeight; continue; }
                int cell = c == ' ' ? -1 : Font.GlyphCell(c);   // the font's glyph map, never the code itself
                if (cell >= 0)
                {
                    s.Draw(Texture, x, y, new RectInt(0, cell * Font.CellHeight, Font.CellWidth, Font.CellHeight), tint);
                }
                x += Font.Advance(c);
            }
            return x;
        }

        public int Draw(Screen2D s, int x, int y, string text, TextAlign align = TextAlign.Left) =>
            Draw(s, x, y, text, Color.white, align);

        /// <summary>Word-wrapped text inside <paramref name="box"/>. Returns the height used.</summary>
        public int DrawBlock(Screen2D s, RectInt box, string text, Color32 tint, TextAlign align = TextAlign.Left, bool measureOnly = false)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int y = box.y;
            int i = 0;
            var line = new System.Text.StringBuilder();
            while (i <= text.Length)
            {
                // Collect the next line.
                line.Clear();
                int lineWidth = 0;
                int lastBreak = -1, lastBreakWidth = 0;
                int start = i;
                bool forced = false;
                while (i < text.Length)
                {
                    char ch = text[i];
                    if (ch == '\n' || ch == TextEncoding.LineBreak) { forced = true; i++; break; }
                    int adv = Font.Advance(ch);
                    if (lineWidth + adv > box.width && line.Length > 0)
                    {
                        if (lastBreak >= 0)
                        {
                            line.Length = lastBreak;
                            lineWidth = lastBreakWidth;
                            i = start + lastBreak + 1;
                        }
                        break;
                    }
                    if (ch == ' ') { lastBreak = line.Length; lastBreakWidth = lineWidth; }
                    line.Append(ch);
                    lineWidth += adv;
                    i++;
                }
                if (!measureOnly && y + LineHeight <= box.yMax)
                {
                    int x = align == TextAlign.Left ? box.x : align == TextAlign.Center ? box.x + (box.width - lineWidth) / 2 : box.xMax - lineWidth;
                    Draw(s, x, y, line.ToString(), tint);
                }
                y += BlockLineHeight;
                if (i >= text.Length && !forced) break;
                if (i >= text.Length && forced) break;
            }
            return y - box.y;
        }
    }
}
