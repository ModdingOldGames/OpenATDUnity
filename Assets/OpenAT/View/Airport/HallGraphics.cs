using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>A decoded sprite: texture plus its pixel size.</summary>
    public sealed class HallSprite
    {
        public Texture2D Tex;
        public int W, H;
    }

    /// <summary>
    /// Textures for the hall: season brick frames (gli/glbrick&lt;season&gt;.gli), person frames from the
    /// HLine pools (clan/clang/clanw&lt;slot&gt;.pol, decoded once and cached by pool + frame name),
    /// shadow darkness maps and thought bubbles (gli/glstd.gli). See docs/spec/formats/hline-pol.md.
    /// </summary>
    public sealed class HallGraphics
    {
        const string StdLib = "gli/glstd.gli";
        readonly GameAssets _assets;
        readonly HallData _data;
        readonly GliLibrary _bricks;
        readonly Dictionary<string, HallSprite> _brickTex = new Dictionary<string, HallSprite>();
        readonly Dictionary<string, HLinePool> _pools = new Dictionary<string, HLinePool>();
        readonly HashSet<string> _missingPools = new HashSet<string>();
        readonly Dictionary<string, HallSprite> _personTex = new Dictionary<string, HallSprite>();
        readonly Dictionary<string, HallSprite> _shadowTex = new Dictionary<string, HallSprite>();
        readonly Dictionary<string, HallSprite> _stdTex = new Dictionary<string, HallSprite>();
        public readonly int Season;

        public HallGraphics(GameAssets assets, HallData data, int season)
        {
            _assets = assets;
            _data = data;
            Season = season;
            var path = assets.Install.Find("gli/glbrick" + season + ".gli") ?? assets.Install.Find("gli/glbrick0.gli");
            _bricks = path != null ? GliLibrary.Load(path) : null;
        }

        // ------------------------------------------------------------------ bricks

        /// <summary>Brick frame texture; opaque bricks keep black pixels.</summary>
        public HallSprite Brick(string name, bool opaque)
        {
            if (name == null) return null;
            string key = (opaque ? "o|" : "k|") + name;
            if (_brickTex.TryGetValue(key, out var s)) return s;
            var bmp = _bricks?.Get(name);
            s = bmp == null ? null : new HallSprite { Tex = GameAssets.ToTexture(bmp, "brick " + name, keyed: !opaque), W = bmp.Width, H = bmp.Height };
            _brickTex[key] = s;
            return s;
        }

        // ------------------------------------------------------------------ persons

        /// <summary>Pool file for a clan and phase slot (4 → clang, 9 → clanw, else clan).</summary>
        static string PoolName(int slot, int phase) => (phase == ClanDef.Gimmick ? "clang" : phase == ClanDef.WaitGimmick ? "clanw" : "clan") + slot + ".pol";

        /// <summary>Loads a pool with its base pools (hline-pol.md "Base pools").</summary>
        HLinePool Pool(string file)
        {
            if (_pools.TryGetValue(file, out var p)) return p;
            if (_missingPools.Contains(file)) return null;
            string path = _assets.Install.Find("gli/" + file);
            if (path == null) { _missingPools.Add(file); return null; }

            int slot = ParseSlot(file, out string kind);
            var me = slot >= 0 && slot < _data.Clans.Length ? _data.Clans[slot] : null;
            var next = slot + 1 < _data.Clans.Length ? _data.Clans[slot + 1] : null;
            bool shared = me != null && next != null && me.Type != ClanType.Suitcase
                          && string.Equals(me.Palette, next.Palette, System.StringComparison.OrdinalIgnoreCase);
            HLinePool b1 = null, b2 = null;
            if (kind == "clan") { if (shared) b1 = Pool("clan" + (slot + 1) + ".pol"); }
            else if (shared) { b1 = Pool("clan" + (slot + 1) + ".pol"); b2 = Pool("clan" + slot + ".pol"); }
            else { b1 = Pool("clan" + slot + ".pol"); if (kind == "clanw") b2 = Pool("clang" + slot + ".pol"); }
            try { p = HLinePool.Load(path, b1, b2); }
            catch (System.Exception e) { Debug.LogWarning("OpenAT: bad pool " + file + ": " + e.Message); _missingPools.Add(file); return null; }
            _pools[file] = p;
            return p;
        }

        static int ParseSlot(string file, out string kind)
        {
            string stem = file.Substring(0, file.Length - 4);
            int i = stem.Length;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            kind = stem.Substring(0, i);
            return int.TryParse(stem.Substring(i), out var n) ? n : -1;
        }

        /// <summary>Person frame of a clan phase slot (null when the frame does not exist).</summary>
        public HallSprite PersonFrame(ClanDef clan, int phase, int frame)
        {
            var list = clan.Phases[phase];
            if (list == null || list.Length == 0) return null;
            if (frame < 0 || frame >= list.Length) frame = 0;
            string file = PoolName(clan.Slot, phase);
            string key = file + "|" + list[frame];
            if (_personTex.TryGetValue(key, out var s)) return s;
            var pool = Pool(file);
            var e = pool?.Find(list[frame]);
            if (e != null && e.Width > 0 && e.Height > 0)
            {
                var bmp = pool.Decode(e);
                s = new HallSprite { Tex = GameAssets.ToTexture(bmp, "person " + list[frame]), W = bmp.Width, H = bmp.Height };
            }
            _personTex[key] = s;
            return s;
        }

        /// <summary>Shadow image as a darkness map: pixel value v (0..8) darkens to v/8 (§12.2 BlitAlpha).</summary>
        public HallSprite Shadow(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_shadowTex.TryGetValue(name, out var s)) return s;
            var bmp = _assets.Bitmap(StdLib, name);
            if (bmp != null)
            {
                var px = new Color32[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    int v = bmp.Pixels[y * bmp.Width + x];
                    if (v == 0 || v > 8) continue;
                    px[y * bmp.Width + x] = new Color32(0, 0, 0, (byte)((8 - v) * 255 / 8));
                }
                var t = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "shadow " + name };
                // Texture rows are uploaded top-down (Screen2D convention).
                t.SetPixels32(px);
                t.Apply(false, false);
                s = new HallSprite { Tex = t, W = bmp.Width, H = bmp.Height };
            }
            _shadowTex[name] = s;
            return s;
        }

        /// <summary>A keyed bitmap from glstd.gli (thought bubbles, status bar pieces).</summary>
        public HallSprite Std(string name)
        {
            if (_stdTex.TryGetValue(name, out var s)) return s;
            var bmp = _assets.Bitmap(StdLib, name);
            s = bmp == null ? null : new HallSprite { Tex = _assets.Texture(StdLib, name), W = bmp.Width, H = bmp.Height };
            _stdTex[name] = s;
            return s;
        }

        /// <summary>Decodes every brick frame of a layout and the pools of the clans in game, so scrolling
        /// never stalls on first use.</summary>
        public void Preload(HallLayout layout, int[] todayInGame)
        {
            var seen = new HashSet<BrickDef>();
            foreach (var b in layout.Builds)
            {
                if (b.BrickId >= 2000 || !seen.Add(b.Brick)) continue;
                foreach (var f in b.Brick.Frames) Brick(f, b.Brick.NonTrans == 1);
            }
            for (int id = 5000; id <= 5006; id++)
                if (_data.Brick(id) is BrickDef h && h.Frames.Length > 0) Brick(h.Frames[0], h.NonTrans == 1);
            for (int slot = 0; slot < _data.Clans.Length; slot++)
            {
                var c = _data.Clans[slot];
                if (c == null || (todayInGame[slot] & 127) == 0) continue;
                for (int ph = 0; ph < ClanDef.Slots; ph++)
                {
                    var list = c.Phases[ph];
                    if (list == null) continue;
                    for (int f = 0; f < list.Length; f++) PersonFrame(c, ph, f);
                    var sh = c.Shadows[ph];
                    if (sh != null) foreach (var n in sh) Shadow(n);
                }
            }
        }

        public void Dispose()
        {
            foreach (var d in new[] { _brickTex, _personTex, _shadowTex })
                foreach (var s in d.Values) if (s?.Tex != null) Object.Destroy(s.Tex);
            _brickTex.Clear(); _personTex.Clear(); _shadowTex.Clear();
        }
    }
}
