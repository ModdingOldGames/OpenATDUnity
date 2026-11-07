using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenAT.Formats
{
    /// <summary>
    /// "GLIB" graphics library (.gli / .glj): a directory of named chunks, each named graphic being an
    /// uncompressed 16-bit RGB565 surface. Names are up to 8 ASCII characters (e.g. "BANK", "MONI01").
    /// See docs/spec/formats/gli.md.
    /// </summary>
    public sealed class GliLibrary
    {
        public sealed class Entry
        {
            public string Name;
            public int Offset;
            public int Width, Height, Pitch, BitDepth;
            public int DataSize;
            public uint RMask, GMask, BMask;
        }

        const int ChunkGfx = 1;

        readonly byte[] _data;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly List<Entry> _ordered = new List<Entry>();
        readonly Dictionary<string, Bitmap16> _cache = new Dictionary<string, Bitmap16>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<Entry> Entries => _ordered;
        public string SourceName { get; }

        public GliLibrary(byte[] data, string sourceName = null)
        {
            _data = data;
            SourceName = sourceName;
            Parse();
        }

        public static GliLibrary Load(string path) => new GliLibrary(File.ReadAllBytes(path), Path.GetFileName(path));

        public static bool IsGli(byte[] data) =>
            data.Length >= 8 && data[0] == 'G' && data[1] == 'L' && data[2] == 'I' && data[3] == 'B';

        void Parse()
        {
            if (!IsGli(_data)) throw new InvalidDataException("Not a GLIB file: " + SourceName);
            // Header: magic(4) length(4) ... files @34, directory offset @38.
            int files = BitConverter.ToInt32(_data, 34);
            int pos = BitConverter.ToInt32(_data, 38);
            for (int i = 0; i < files && pos + 5 <= _data.Length; i++)
            {
                int size = BitConverter.ToInt32(_data, pos);
                int type = _data[pos + 4];
                if (type == ChunkGfx && pos + 17 <= _data.Length)
                {
                    string name = ReadName(pos + 5);
                    int offset = BitConverter.ToInt32(_data, pos + 13);
                    var e = ReadImageHeader(name, offset);
                    if (e != null)
                    {
                        _entries[name] = e;
                        _ordered.Add(e);
                    }
                }
                if (size <= 0) break;
                pos += size;
            }
        }

        string ReadName(int at)
        {
            var sb = new StringBuilder(8);
            for (int i = 0; i < 8; i++)
            {
                byte b = _data[at + i];
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        Entry ReadImageHeader(string name, int offset)
        {
            if (offset < 0 || offset + 76 > _data.Length) return null;
            int headerLen = BitConverter.ToInt32(_data, offset);
            int size = BitConverter.ToInt32(_data, offset + 4);
            int w = BitConverter.ToInt32(_data, offset + 8);
            int h = BitConverter.ToInt32(_data, offset + 12);
            int bpp = BitConverter.ToInt32(_data, offset + 24);
            if (w <= 0 || h <= 0 || size <= 0) return null;
            if (bpp == 0) bpp = 16;
            uint rm = BitConverter.ToUInt32(_data, offset + 32), gm = BitConverter.ToUInt32(_data, offset + 36), bm = BitConverter.ToUInt32(_data, offset + 40);
            if (bpp != 16 && (rm | gm | bm) == 0) { rm = 0xFF0000; gm = 0xFF00; bm = 0xFF; }
            return new Entry
            {
                RMask = rm, GMask = gm, BMask = bm,
                Name = name,
                Offset = offset + (headerLen > 0 ? headerLen : 76),
                Width = w,
                Height = h,
                BitDepth = bpp,
                DataSize = size,
                Pitch = size / h,
            };
        }

        public bool Contains(string name) => _entries.ContainsKey(name);

        public Entry GetEntry(string name) => _entries.TryGetValue(name, out var e) ? e : null;

        /// <summary>Decodes the named graphic (cached). Returns null if missing.</summary>
        public Bitmap16 Get(string name)
        {
            if (_cache.TryGetValue(name, out var b)) return b;
            var e = GetEntry(name);
            if (e == null) return null;
            b = Decode(e);
            _cache[name] = b;
            return b;
        }

        public Bitmap16 Decode(Entry e)
        {
            var px = new ushort[e.Width * e.Height];
            if (e.BitDepth == 16)
            {
                int rowBytes = Math.Min(e.Pitch, e.Width * 2);
                for (int y = 0; y < e.Height; y++)
                {
                    int src = e.Offset + y * e.Pitch;
                    if (src + rowBytes > _data.Length) break;
                    Buffer.BlockCopy(_data, src, px, y * e.Width * 2, rowBytes);
                }
            }
            else
            {
                // 24/32-bit images (a few small icons): convert through the channel masks to RGB565.
                int bytes = e.BitDepth / 8;
                for (int y = 0; y < e.Height; y++)
                for (int x = 0; x < e.Width; x++)
                {
                    int src = e.Offset + y * e.Pitch + x * bytes;
                    if (src + bytes > _data.Length) break;
                    uint v = 0;
                    for (int k = 0; k < bytes; k++) v |= (uint)_data[src + k] << (8 * k);
                    px[y * e.Width + x] = Bitmap16.FromRgb(Channel(v, e.RMask), Channel(v, e.GMask), Channel(v, e.BMask));
                }
            }
            return new Bitmap16(e.Width, e.Height, px);
        }

        static int Channel(uint v, uint mask)
        {
            if (mask == 0) return 0;
            int shift = 0;
            while (((mask >> shift) & 1) == 0) shift++;
            uint max = mask >> shift;
            return (int)(((v & mask) >> shift) * 255 / max);
        }
    }
}
