using System;
using System.IO;

namespace OpenAT.Formats
{
    /// <summary>
    /// Bitmap font (.mcf). 44-byte header, then glyph pixels (8-bit palette indices, one
    /// Width x Height cell per character from LoChar..HiChar stacked vertically), a BGRA palette,
    /// a 256-byte advance-width table and a 256-byte glyph map. Index 0 / black is transparent.
    /// The glyph map gives, per character code, the code of the cell that holds its picture: the
    /// cells above 0x7F are packed (e.g. 'ß' 0xDF is drawn from cell 0x81, 'ä' 0xE4 from 0x82), so
    /// glyphs must never be indexed by the character code directly. Fonts whose map is all zero
    /// (NORMFONT, Dlgfont4) store their cells in code order.
    /// </summary>
    public sealed class McfFont
    {
        public int CellWidth { get; private set; }
        public int CellHeight { get; private set; }
        public int LoChar { get; private set; }
        public int HiChar { get; private set; }
        public int Flags { get; private set; }
        /// <summary>Pixel indices: (HiChar-LoChar+1) cells of CellWidth x CellHeight, top-down.</summary>
        public byte[] Pixels { get; private set; }
        /// <summary>Palette as RGBA32 (index beyond the stored palette is transparent).</summary>
        public uint[] Palette { get; private set; }
        public byte[] Widths { get; private set; }
        /// <summary>Character code → code of the glyph cell (see the class summary).</summary>
        public byte[] GlyphMap { get; private set; }
        /// <summary>
        /// Draw a letter whose cell is blank in this font (the small fonts only have the German
        /// umlauts and ß) with its unaccented base letter instead of a gap. Off by default (the
        /// original's blank cell); an OpenAT addition, see docs/QUIRKS.md.
        /// </summary>
        public bool BaseLetterFallback = false;

        bool _identityMap;
        // Unaccented base letter per Windows-1252 code (0 = none).
        static readonly char[] BaseLetter = BuildBaseLetters();

        static char[] BuildBaseLetters()
        {
            var t = new char[256];
            void Set(int from, string bases) { for (int i = 0; i < bases.Length; i++) if (bases[i] != '-') t[from + i] = bases[i]; }
            Set(0xC0, "AAAAAA-CEEEEIIII-NOOOOO-OUUUUY--");
            Set(0xE0, "aaaaaa-ceeeeiiii-nooooo-ouuuuy-y");
            t[0x8A] = 'S'; t[0x8E] = 'Z'; t[0x9A] = 's'; t[0x9E] = 'z'; t[0x9F] = 'Y';
            return t;
        }

        /// <summary>Line spacing factor used by block text (1.5 lines in the original).</summary>
        public float LineSpace = 1.5f;

        public static McfFont Parse(byte[] d)
        {
            if (d.Length < 44) throw new InvalidDataException("mcf too short");
            var f = new McfFont();
            int headerSize = BitConverter.ToUInt16(d, 0);
            f.Flags = BitConverter.ToUInt16(d, 4);
            f.CellWidth = BitConverter.ToUInt16(d, 6);
            f.CellHeight = BitConverter.ToUInt16(d, 8);
            int numColors = BitConverter.ToUInt16(d, 18);
            f.LoChar = BitConverter.ToUInt16(d, 22);
            f.HiChar = BitConverter.ToUInt16(d, 24);
            int szPixels = BitConverter.ToInt32(d, 28);
            int szColors = BitConverter.ToInt32(d, 32);
            int pos = headerSize > 0 ? headerSize : 44;

            f.Pixels = new byte[szPixels];
            Buffer.BlockCopy(d, pos, f.Pixels, 0, Math.Min(szPixels, d.Length - pos));
            pos += szPixels;

            f.Palette = new uint[256];
            for (int i = 0; i < numColors && i * 4 + 3 < szColors; i++)
            {
                int p = pos + i * 4;
                if (p + 3 >= d.Length) break;
                uint b = d[p], g = d[p + 1], r = d[p + 2];
                uint a = (r | g | b) == 0 ? 0u : 255u;
                f.Palette[i] = r | (g << 8) | (b << 16) | (a << 24);
            }
            pos += szColors;

            f.Widths = new byte[256];
            f.GlyphMap = new byte[256];
            if (pos + 256 <= d.Length) Buffer.BlockCopy(d, pos, f.Widths, 0, 256);
            pos += 256;
            if (pos + 256 <= d.Length) Buffer.BlockCopy(d, pos, f.GlyphMap, 0, 256);
            f._identityMap = true;
            foreach (var b in f.GlyphMap) if (b != 0) { f._identityMap = false; break; }
            return f;
        }

        /// <summary>
        /// Cell index (0-based, in the glyph strip) of single-byte character <paramref name="ch"/>, or
        /// −1 when the font has no picture for it (mapped outside LoChar..HiChar).
        /// </summary>
        public int GlyphCell(int ch)
        {
            if (ch < 0 || ch > 255) return -1;
            var cache = BaseLetterFallback ? _cellsFallback : _cellsPlain;
            if (cache == null)
            {
                cache = new int[256];
                for (int c = 0; c < 256; c++) cache[c] = ResolveCell(c);
                if (BaseLetterFallback) _cellsFallback = cache; else _cellsPlain = cache;
            }
            return cache[ch];
        }

        int[] _cellsFallback, _cellsPlain;

        int ResolveCell(int ch)
        {
            int cell = MappedCell(ch);
            if (BaseLetterFallback && ch >= 0 && ch <= 255 && BaseLetter[ch] != 0 && (cell < 0 || !CellHasInk(cell)))
            {
                int b = MappedCell(BaseLetter[ch]);
                if (b >= 0) return b;
            }
            return cell;
        }

        int MappedCell(int ch)
        {
            if (ch < 0 || ch > 255) return -1;
            int code = _identityMap ? ch : GlyphMap[ch];
            return code >= LoChar && code <= HiChar ? code - LoChar : -1;
        }

        bool CellHasInk(int cell)
        {
            int n = CellWidth * CellHeight, start = cell * n;
            for (int i = start; i < start + n && i < Pixels.Length; i++)
                if ((Palette[Pixels[i]] >> 24) != 0) return true;
            return false;
        }

        public bool HasGlyph(int ch) => GlyphCell(ch) >= 0;

        /// <summary>Horizontal advance of a single-byte character in pixels (fixed cell width unless flag bit 0).</summary>
        public int Advance(int ch)
        {
            if (ch < 0 || ch > 255) return 0;
            // A letter drawn with its base letter's glyph (fallback) also takes that letter's width.
            if (BaseLetter[ch] != 0 && GlyphCell(ch) >= 0 && GlyphCell(ch) != MappedCell(ch)) ch = BaseLetter[ch];
            int w = (Flags & 1) != 0 ? Widths[ch] : CellWidth;
            if (w == 0 && ch == ' ') w = Math.Max(1, CellWidth / 2);
            return w;
        }

        /// <summary>Advance of a string character (Unicode → original single-byte code first).</summary>
        public int Advance(char c) => Advance(TextEncoding.ToByte(c));

        public int MeasureWidth(string s)
        {
            int w = 0;
            foreach (char c in s) w += Advance(c);
            return w;
        }

        /// <summary>Palette index at (x,y) of the glyph of single-byte character <paramref name="ch"/> (0 if none).</summary>
        public byte GlyphPixel(int ch, int x, int y)
        {
            int cell = GlyphCell(ch);
            if (cell < 0 || x < 0 || y < 0 || x >= CellWidth || y >= CellHeight) return 0;
            int i = (cell * CellHeight + y) * CellWidth + x;
            return i >= 0 && i < Pixels.Length ? Pixels[i] : (byte)0;
        }

        /// <summary>The whole glyph strip as RGBA32 (CellWidth x CellHeight*count), top-down.</summary>
        public byte[] ToRgbaStrip(out int width, out int height)
        {
            int count = HiChar - LoChar + 1;
            width = CellWidth;
            height = CellHeight * count;
            var o = new byte[width * height * 4];
            for (int i = 0; i < width * height && i < Pixels.Length; i++)
            {
                uint c = Palette[Pixels[i]];
                o[i * 4] = (byte)c; o[i * 4 + 1] = (byte)(c >> 8); o[i * 4 + 2] = (byte)(c >> 16); o[i * 4 + 3] = (byte)(c >> 24);
            }
            return o;
        }
    }
}
