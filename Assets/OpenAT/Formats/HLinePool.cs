using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenAT.Formats
{
    /// <summary>
    /// "HLine" sprite pool (gli/*.pol): palettised sprites stored as horizontal runs of opaque pixels,
    /// where long runs point into a shared byte pool (the pool of this file or of up to two base pools).
    /// Used for the people walking through the airport (clan%d / clang%d / clanw%d.pol) and their
    /// x-ray images at the security check (skelett.pol). See docs/spec/formats/hline-pol.md.
    /// </summary>
    public sealed class HLinePool
    {
        /// <summary>Run selector values stored in the top nibble of a run reference.</summary>
        public const int RefOwn = 0, RefBase1 = 1, RefBase2 = 2;

        /// <summary>Runs this long or shorter carry their palette indices inline.</summary>
        public const int MaxInlineRun = 4;

        const int HeaderInts = 7;
        const int RunBytes = 6;
        const int PaletteBytes = 512;

        public readonly struct Run
        {
            public readonly byte X;
            public readonly byte Length;
            /// <summary>Inline pixel bytes (Length &lt;= 4, first pixel in the low byte) or pool reference.</summary>
            public readonly uint Data;

            public Run(byte x, byte length, uint data) { X = x; Length = length; Data = data; }

            public bool IsInline => Length <= MaxInlineRun;
            public int PoolSelector => (int)(Data >> 28);
            public int PoolOffset => (int)(Data & 0x0FFFFFFF);
        }

        public sealed class Entry
        {
            /// <summary>Graphic id: up to 8 ASCII characters packed little-endian.</summary>
            public ulong Id;
            public string Name;
            public int Width, Height;
            /// <summary>Number of runs on each row (length = Height).</summary>
            public byte[] RunsPerRow;
            public Run[] Runs;
            public int Index;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<ulong, Entry> _byId = new Dictionary<ulong, Entry>();

        public string SourceName { get; }
        public HLinePool Base1 { get; }
        public HLinePool Base2 { get; }

        // Header statistics (written by the original compressor; informational).
        public int PoolSize { get; private set; }
        public int PoolCapacity { get; private set; }
        public int BytesReal { get; private set; }
        public int BytesCompressed { get; private set; }
        public int BytesAdministration { get; private set; }
        public int LinesInPool { get; private set; }
        public int LinesRepeated { get; private set; }

        /// <summary>The shared palette-index byte pool.</summary>
        public byte[] Pool { get; private set; } = Array.Empty<byte>();

        /// <summary>256 RGB565 colours as stored in the file (index 0 is never drawn).</summary>
        public ushort[] Palette { get; } = new ushort[256];

        public IReadOnlyList<Entry> Entries => _entries;

        public HLinePool(byte[] data, string sourceName = null, HLinePool base1 = null, HLinePool base2 = null)
        {
            SourceName = sourceName;
            Base1 = base1;
            Base2 = base2;
            Parse(data ?? throw new ArgumentNullException(nameof(data)));
        }

        public static HLinePool Load(string path, HLinePool base1 = null, HLinePool base2 = null) =>
            new HLinePool(File.ReadAllBytes(path), Path.GetFileName(path), base1, base2);

        // ---------------------------------------------------------------------------------------
        // Parsing
        // ---------------------------------------------------------------------------------------

        sealed class Reader
        {
            readonly byte[] _d;
            public int Pos;
            readonly string _name;
            public Reader(byte[] d, string name) { _d = d; _name = name; }

            void Need(int n)
            {
                if (n < 0 || Pos + n > _d.Length)
                    throw new InvalidDataException($"HLine pool truncated at {Pos} (+{n}): {_name}");
            }

            public int I32() { Need(4); int v = BitConverter.ToInt32(_d, Pos); Pos += 4; return v; }
            public ulong U64() { Need(8); ulong v = BitConverter.ToUInt64(_d, Pos); Pos += 8; return v; }
            public byte U8() { Need(1); return _d[Pos++]; }
            public uint U32() { Need(4); uint v = BitConverter.ToUInt32(_d, Pos); Pos += 4; return v; }
            public byte[] Bytes(int n) { Need(n); var b = new byte[n]; Buffer.BlockCopy(_d, Pos, b, 0, n); Pos += n; return b; }
            public int Remaining => _d.Length - Pos;

            /// <summary>Array prefix: element count followed by a cursor value that readers ignore.</summary>
            public int ArrayCount(int elemSize)
            {
                int n = I32();
                I32();
                if (n < 0 || (long)n * elemSize > Remaining)
                    throw new InvalidDataException($"HLine pool: bad array length {n} at {Pos - 8}: {_name}");
                return n;
            }
        }

        void Parse(byte[] data)
        {
            if (data.Length < HeaderInts * 4 + 8) throw new InvalidDataException("HLine pool too small: " + SourceName);
            var r = new Reader(data, SourceName);
            PoolSize = r.I32();
            PoolCapacity = r.I32();
            BytesReal = r.I32();
            BytesCompressed = r.I32();
            BytesAdministration = r.I32();
            LinesInPool = r.I32();
            LinesRepeated = r.I32();
            if (PoolSize < 0) throw new InvalidDataException("HLine pool: negative pool size: " + SourceName);

            // Minimum object record: id(8) size(8) two empty arrays(16).
            int count = r.ArrayCount(32);
            for (int i = 0; i < count; i++)
            {
                var e = new Entry { Index = i };
                e.Id = r.U64();
                e.Name = NameFromId(e.Id);
                e.Width = r.I32();
                e.Height = r.I32();
                int runs = r.ArrayCount(RunBytes);
                e.Runs = new Run[runs];
                for (int k = 0; k < runs; k++)
                {
                    byte x = r.U8();
                    byte len = r.U8();
                    e.Runs[k] = new Run(x, len, r.U32());
                }
                int rows = r.ArrayCount(1);
                e.RunsPerRow = r.Bytes(rows);
                if (e.Width < 0 || e.Height < 0) throw new InvalidDataException("HLine pool: negative size: " + SourceName);
                _entries.Add(e);
                // The original searches from the end, so the last duplicate wins.
                _byId[e.Id] = e;
            }

            Pool = r.Bytes(Math.Min(PoolSize, r.Remaining));
            if (r.Remaining >= PaletteBytes)
                for (int c = 0; c < 256; c++)
                    Palette[c] = (ushort)(data[r.Pos + 2 * c] | (data[r.Pos + 2 * c + 1] << 8));
        }

        // ---------------------------------------------------------------------------------------
        // Ids
        // ---------------------------------------------------------------------------------------

        /// <summary>Packs up to 8 ASCII characters little-endian (first character in the low byte).</summary>
        public static ulong IdFromName(string name)
        {
            ulong id = 0;
            for (int i = 0; i < name.Length && i < 8; i++) id |= (ulong)(byte)name[i] << (8 * i);
            return id;
        }

        public static string NameFromId(ulong id)
        {
            var sb = new StringBuilder(8);
            for (int i = 0; i < 8; i++)
            {
                char ch = (char)((id >> (8 * i)) & 0xFF);
                if (ch == 0) break;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Expands a numbered frame range as used by clan.csv ("NAME01" with count 3 gives NAME01,
        /// NAME02, NAME03): the last character counts up and carries into the one before it at '9'.
        /// </summary>
        public static List<string> ExpandSequence(string first, int count)
        {
            var list = new List<string>(Math.Max(0, count));
            if (string.IsNullOrEmpty(first)) return list;
            var chars = first.ToCharArray();
            int last = chars.Length - 1;
            for (int i = 0; i < count; i++)
            {
                list.Add(new string(chars));
                chars[last]++;
                if (chars[last] > '9' && last > 0)
                {
                    chars[last] = '0';
                    chars[last - 1]++;
                }
            }
            return list;
        }

        public bool Contains(string name) => _byId.ContainsKey(IdFromName(name));
        public Entry Find(ulong id) => _byId.TryGetValue(id, out var e) ? e : null;
        public Entry Find(string name) => Find(IdFromName(name));

        // ---------------------------------------------------------------------------------------
        // Decoding
        // ---------------------------------------------------------------------------------------

        byte[] PoolFor(int selector)
        {
            switch (selector)
            {
                case RefOwn: return Pool;
                case RefBase1: return Base1?.Pool;
                case RefBase2: return Base2?.Pool;
                default: return null;
            }
        }

        /// <summary>True when every pool-referencing run of the entry can be resolved with the
        /// base pools supplied at construction.</summary>
        public bool CanResolve(Entry e)
        {
            foreach (var run in e.Runs)
            {
                if (run.IsInline) continue;
                var p = PoolFor(run.PoolSelector);
                if (p == null || run.PoolOffset + run.Length > p.Length) return false;
            }
            return true;
        }

        /// <summary>
        /// Decodes an entry to palette indices (row-major, Width*Height). Index 0 = transparent.
        /// Runs whose pool cannot be resolved are left transparent unless <paramref name="strict"/>.
        /// </summary>
        public byte[] DecodeIndices(Entry e, bool strict = false)
        {
            var dst = new byte[e.Width * e.Height];
            int ri = 0;
            int rows = Math.Min(e.Height, e.RunsPerRow.Length);
            for (int y = 0; y < rows; y++)
            {
                int rowBase = y * e.Width;
                for (int n = e.RunsPerRow[y]; n > 0 && ri < e.Runs.Length; n--, ri++)
                {
                    var run = e.Runs[ri];
                    int len = Math.Min(run.Length, e.Width - run.X);
                    if (len <= 0) continue;
                    if (run.IsInline)
                    {
                        for (int k = 0; k < len; k++)
                            dst[rowBase + run.X + k] = (byte)(run.Data >> (8 * k));
                        continue;
                    }
                    var pool = PoolFor(run.PoolSelector);
                    int off = run.PoolOffset;
                    if (pool == null || off + len > pool.Length)
                    {
                        if (strict)
                            throw new InvalidDataException(
                                $"{SourceName}: run of '{e.Name}' references pool {run.PoolSelector} @{off}+{len}, not available");
                        continue;
                    }
                    Buffer.BlockCopy(pool, off, dst, rowBase + run.X, len);
                }
            }
            return dst;
        }

        /// <summary>Palette entry as the game displays it (RGB565; the game drops the lowest green bit).
        /// Never returns the colour key 0, so opaque black stays opaque in a <see cref="Bitmap16"/>.</summary>
        public ushort DisplayColor(int index)
        {
            ushort c = (ushort)(Palette[index & 0xFF] & ~0x0020);
            return c == Bitmap16.ColorKey ? (ushort)0x0020 : c;
        }

        /// <summary>Decodes an entry to an RGB565 bitmap (0 = transparent) using this pool's palette.</summary>
        public Bitmap16 Decode(Entry e, bool strict = false)
        {
            var idx = DecodeIndices(e, strict);
            var lut = new ushort[256];
            for (int c = 1; c < 256; c++) lut[c] = DisplayColor(c);
            var px = new ushort[idx.Length];
            for (int i = 0; i < idx.Length; i++) px[i] = lut[idx[i]];
            return new Bitmap16(e.Width, e.Height, px);
        }

        public Bitmap16 Decode(string name, bool strict = false)
        {
            var e = Find(name);
            return e == null ? null : Decode(e, strict);
        }

        /// <summary>The point the game places on a person's foot position: horizontal centre of the
        /// frame, bottom row (before the per-clan offsets from clan.csv are applied).</summary>
        public static (int X, int Y) FootAnchor(Entry e) => (e.Width / 2, e.Height - 1);
    }
}
