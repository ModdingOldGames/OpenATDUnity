using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Formats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// .mcf glyph lookup: the second 256-byte table is a character → glyph-cell map, the cells above
    /// 0x7F are packed (ß, umlauts and accents live in cells that differ from their code).
    /// </summary>
    public class FontTests
    {
        /// <summary>Builds a tiny font: 1x1 cells for codes 32..(32+cells-1), cell i has pixel value i+1 (cell 0 transparent).</summary>
        static McfFont Synthetic(int cells, byte[] map, bool varWidth = true)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((ushort)44); w.Write((ushort)0x100); w.Write((ushort)(varWidth ? 3 : 2));
            w.Write((ushort)1); w.Write((ushort)1);                          // cell 1x1
            w.Write(new byte[8]); w.Write((ushort)256);                       // ... numColors
            w.Write((ushort)32); w.Write((ushort)32); w.Write((ushort)(32 + cells - 1));
            w.Write((ushort)0); w.Write(cells); w.Write(1024); w.Write(cells); w.Write(0);
            Assert.Equal(44, ms.Length);
            for (int i = 0; i < cells; i++) w.Write((byte)(i + 1));
            for (int i = 0; i < 256; i++) { byte g = (byte)(i == 1 ? 0 : i); w.Write(g); w.Write(g); w.Write(g); w.Write((byte)0); }   // cell 0 (value 1) is blank
            var widths = new byte[256];
            for (int i = 0; i < 256; i++) widths[i] = (byte)(i % 7 + 1);
            w.Write(widths);
            w.Write(map);
            return McfFont.Parse(ms.ToArray());
        }

        [Fact]
        public void GlyphCellFollowsTheMapNotTheCode()
        {
            var map = new byte[256];
            for (int i = 0; i < 128; i++) map[i] = (byte)i;
            map[0xDF] = 0x22;   // 'ß' drawn from cell code 0x22
            map[0xE4] = 0xFF;   // mapped outside the strip: no glyph
            var f = Synthetic(16, map);
            Assert.Equal(0x22 - 32, f.GlyphCell(0xDF));
            Assert.Equal(0x22 - 32 + 1, f.GlyphPixel(0xDF, 0, 0));
            Assert.Equal(1, f.GlyphCell('!'));
            Assert.Equal(-1, f.GlyphCell(0xE4));
            Assert.False(f.HasGlyph(0xE4));
            Assert.Equal(0, f.GlyphPixel(0xE4, 0, 0));
        }

        [Fact]
        public void AllZeroMapMeansCellsInCodeOrder()
        {
            var f = Synthetic(16, new byte[256]);
            Assert.Equal(0, f.GlyphCell(' '));
            Assert.Equal(15, f.GlyphCell(32 + 15));
            Assert.Equal(-1, f.GlyphCell(32 + 16));
        }

        [Fact]
        public void BlankAccentedLetterFallsBackToBaseLetter()
        {
            var map = new byte[256];
            for (int i = 0; i < 128; i++) map[i] = (byte)i;
            map[0xE9] = 32;   // 'é' mapped to cell 0, whose pixel value 1 is black = transparent
            var f = Synthetic(80, map);
            f.BaseLetterFallback = true;
            Assert.Equal('e' - 32, f.GlyphCell(0xE9));
            Assert.Equal(f.Advance('e'), f.Advance(0xE9));
            f.BaseLetterFallback = false;
            Assert.Equal(0, f.GlyphCell(0xE9));
        }

        [Fact]
        public void FixedWidthUnlessFlagBit0()
        {
            var f = Synthetic(16, new byte[256], varWidth: false);
            Assert.Equal(1, f.Advance('A'));
            var v = Synthetic(16, new byte[256], varWidth: true);
            Assert.Equal('A' % 7 + 1, v.Advance('A'));
        }

        [Fact]
        public void UnicodeCharsMapToWindows1252Bytes()
        {
            Assert.Equal(0xDF, TextEncoding.ToByte('ß'));
            Assert.Equal(0xE4, TextEncoding.ToByte('ä'));
            Assert.Equal(0x80, TextEncoding.ToByte('€'));
            Assert.Equal(0x84, TextEncoding.ToByte('„'));
            Assert.Equal(0x8A, TextEncoding.ToByte('Š'));
            Assert.Equal(0x9F, TextEncoding.ToByte('Ÿ'));
            Assert.Equal('?', TextEncoding.ToByte('一'));
            // Bytes decoded from the install round-trip unchanged.
            for (int b = 0; b < 256; b++) Assert.Equal(b, TextEncoding.ToByte(TextEncoding.Decode(new[] { (byte)b })[0]));
        }

        static bool HasInk(McfFont f, int ch)
        {
            int cell = f.GlyphCell(ch);
            if (cell < 0) return false;
            for (int y = 0; y < f.CellHeight; y++)
                for (int x = 0; x < f.CellWidth; x++)
                    if ((f.Palette[f.GlyphPixel(ch, x, y)] >> 24) != 0) return true;
            return false;
        }

        /// <summary>
        /// Every non-ASCII letter used in the shipped name tables (cities, plane names, people) has a
        /// visible glyph with a non-zero advance in every mapped font. Before the fix 'ß' and the
        /// umlauts were drawn from the cell of their own code, which is blank (a gap).
        /// </summary>
        [Fact]
        public void InstallNameLettersHaveVisibleGlyphs()
        {
            var install = GameInstall.TryLocate();
            if (install == null) return;
            var letters = new SortedSet<int>();
            foreach (var t in new[] { "data/city.csv", "data/pnames.csv", "data/Names.csv", "data/planetyp.csv", "data/piloten.csv", "data/clan.csv" })
            {
                if (!install.Exists(t)) continue;
                foreach (var b in install.ReadDecoded(t))
                    if (b >= 0xC0 && b != 0xD7 && b != 0xF7) letters.Add(b);
            }
            letters.Add(0xDF); letters.Add(0xE4); letters.Add(0xF6); letters.Add(0xFC);
            letters.Add(0xC4); letters.Add(0xD6); letters.Add(0xDC);

            int checkedFonts = 0;
            foreach (var file in install.List("misc", ".mcf"))
            {
                McfFont f;
                try { f = McfFont.Parse(install.ReadAllBytes(file)); } catch (Exception) { continue; }  // plain_r.mcf is not a font
                f.BaseLetterFallback = true;  // accented letters the original leaves blank use the addition
                if (f.HiChar < 0xFF - 1) continue;   // NORMFONT / Dlgfont4: ASCII-range fonts without a map
                checkedFonts++;
                Assert.True(HasInk(f, 'A'), file + " A");
                foreach (var ch in letters)
                {
                    Assert.True(f.Advance(ch) > 0, $"{file} advance of 0x{ch:X2}");
                    Assert.True(HasInk(f, ch), $"{file} glyph of 0x{ch:X2}");
                }
                // Without the fallback the umlauts and ß still have their own pictures.
                f.BaseLetterFallback = false;
                foreach (var ch in new[] { 0xDF, 0xE4, 0xF6, 0xFC, 0xC4, 0xD6, 0xDC }) Assert.True(HasInk(f, ch), $"{file} own glyph of 0x{ch:X2}");
                f.BaseLetterFallback = true;
                // ß and ä are drawn from different, non-identity cells.
                Assert.NotEqual(0xDF - f.LoChar, f.GlyphCell(0xDF));
                Assert.NotEqual(f.GlyphCell(0xDF), f.GlyphCell(0xE4));
            }
            Assert.True(checkedFonts >= 10);
        }
    }
}
