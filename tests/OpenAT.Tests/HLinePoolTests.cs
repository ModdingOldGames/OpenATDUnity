using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenAT.Formats;
using Xunit;

namespace OpenAT.Tests
{
    public class HLinePoolTests
    {
        // Builds a pool file: header, objects, pool bytes, palette.
        static byte[] BuildPool(byte[] pool, ushort[] palette, params (string name, int w, int h, (byte x, byte len, uint data)[] runs, byte[] perRow)[] objs)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(pool.Length); bw.Write(pool.Length + 100);
            for (int i = 0; i < 5; i++) bw.Write(0);
            bw.Write(objs.Length); bw.Write(0);
            foreach (var o in objs)
            {
                bw.Write(HLinePool.IdFromName(o.name));
                bw.Write(o.w); bw.Write(o.h);
                bw.Write(o.runs.Length); bw.Write(0);
                foreach (var r in o.runs) { bw.Write(r.x); bw.Write(r.len); bw.Write(r.data); }
                bw.Write(o.perRow.Length); bw.Write(0);
                bw.Write(o.perRow);
            }
            bw.Write(pool);
            for (int i = 0; i < 256; i++) bw.Write(i < palette.Length ? palette[i] : (ushort)0);
            return ms.ToArray();
        }

        [Fact]
        public void Ids_PackLittleEndian()
        {
            ulong id = HLinePool.IdFromName("AB");
            Assert.Equal((ulong)'A' | ((ulong)'B' << 8), id);
            Assert.Equal("SCHW_N01", HLinePool.NameFromId(HLinePool.IdFromName("SCHW_N01")));
        }

        [Fact]
        public void ExpandSequence_CarriesDecimal()
        {
            Assert.Equal(new[] { "X_08", "X_09", "X_10", "X_11" }, HLinePool.ExpandSequence("X_08", 4));
            Assert.Empty(HLinePool.ExpandSequence("X_01", 0));
        }

        [Fact]
        public void Decode_InlineOwnAndBaseRuns()
        {
            var pal = new ushort[256];
            pal[1] = 0xF800; pal[2] = 0x07E0; pal[3] = 0x001F; pal[4] = 0x0000; pal[5] = 0xFFFF;

            var baseData = BuildPool(new byte[] { 9, 9, 5, 5, 5, 5, 5 }, pal);
            var basePool = new HLinePool(baseData, "base");

            var data = BuildPool(new byte[] { 1, 2, 3, 1, 2, 3 }, pal,
                ("OBJ", 8, 3, new (byte, byte, uint)[]
                {
                    (1, 2, 0x0201u),              // row 0: inline [1,2] at x=1
                    (5, 1, 0x04u),                // row 0: inline [4] (black) at x=5
                    (0, 6, 0x00000000u),          // row 1: own pool @0, 6 px
                    (2, 5, 0x10000002u),          // row 2: base1 pool @2, 5 px
                }, new byte[] { 2, 1, 1 }));
            var p = new HLinePool(data, "t", basePool);

            Assert.Single(p.Entries);
            var e = p.Find("OBJ");
            Assert.NotNull(e);
            Assert.True(p.CanResolve(e));

            var idx = p.DecodeIndices(e, strict: true);
            Assert.Equal(new byte[] { 0, 1, 2, 0, 0, 4, 0, 0 }, idx.Take(8).ToArray());
            Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3, 0, 0 }, idx.Skip(8).Take(8).ToArray());
            Assert.Equal(new byte[] { 0, 0, 5, 5, 5, 5, 5, 0 }, idx.Skip(16).Take(8).ToArray());

            var bm = p.Decode(e);
            Assert.Equal(0, bm[0, 0]);
            Assert.Equal(0xF800, bm[1, 0]);
            Assert.Equal(0x07C0, bm[2, 0]);            // lowest green bit dropped like the game
            Assert.NotEqual(Bitmap16.ColorKey, bm[5, 0]); // opaque black stays opaque
            Assert.Equal(0xFFDF, bm[2, 2]);

            // Without the base pool the base-referencing run is unresolvable.
            var alone = new HLinePool(data, "t");
            Assert.False(alone.CanResolve(alone.Entries[0]));
            Assert.Throws<InvalidDataException>(() => alone.DecodeIndices(alone.Entries[0], strict: true));
        }

        [Fact]
        public void Truncated_Throws()
        {
            var data = BuildPool(new byte[] { 1, 2 }, new ushort[0], ("A", 1, 1, new (byte, byte, uint)[] { (0, 1, 1) }, new byte[] { 1 }));
            Assert.Throws<InvalidDataException>(() => new HLinePool(data.Take(60).ToArray()));
        }

        [Fact]
        public void Install_AllPoolsParseAndAreConsistent()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return; // no game files on this machine
            int files = 0;
            foreach (var rel in gi.List(GameInstall.Folders.Gli, ".pol"))
            {
                var p = new HLinePool(gi.ReadAllBytes(rel), rel);
                files++;
                Assert.Equal(p.PoolSize, p.Pool.Length);
                foreach (var e in p.Entries)
                {
                    Assert.Equal(e.Height, e.RunsPerRow.Length);
                    Assert.Equal(e.Runs.Length, e.RunsPerRow.Sum(b => b));
                    Assert.All(e.Runs, r => Assert.True(r.X + r.Length <= e.Width && r.Length > 0 && (r.IsInline || r.PoolSelector <= 2)));
                }
            }
            Assert.True(files > 200, $"expected the clan/clang/clanw/skelett pools, found {files} in {gi.Root}");
        }

        [Fact]
        public void Install_ClanFramesMatchGlclanSilhouettes()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return;
            string P(string f) => gi.Find(GameInstall.Folders.Gli, f);
            if (P("clan89.pol") == null || P("glclan.gli") == null || P("skelett.pol") == null) return;

            // Slot 89 (first CSV clan) has no base; clang/clanw build on it.
            var main = HLinePool.Load(P("clan89.pol"));
            var gim = HLinePool.Load(P("clang89.pol"), main);
            var gli = GliLibrary.Load(P("glclan.gli"));
            var skel = HLinePool.Load(P("skelett.pol"));

            void Check(HLinePool pool, string name)
            {
                var e = pool.Find(name);
                Assert.NotNull(e);
                Assert.True(pool.CanResolve(e), name);
                var bm = pool.Decode(e, strict: true);
                var src = gli.Get(name);
                Assert.NotNull(src);
                Assert.Equal(src.Width, bm.Width);
                Assert.Equal(src.Height, bm.Height);
                for (int i = 0; i < bm.Pixels.Length; i++)
                    Assert.Equal(src.Pixels[i] != 0, bm.Pixels[i] != 0);
            }

            foreach (var n in HLinePool.ExpandSequence("SCHW_N01", 3)) Check(main, n);
            Check(gim, "SCHW_W03");
            Check(skel, "SK_O01");

            // Slot 88 shares the palette of slot 89 and uses it as base 1.
            if (P("clan88.pol") != null)
            {
                var c88 = HLinePool.Load(P("clan88.pol"), main);
                foreach (var e in c88.Entries) Assert.True(c88.CanResolve(e), e.Name);
            }
        }
    }
}
