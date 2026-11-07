using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Formats
{
    /// <summary>
    /// One of the (up to seven) audio tracks declared in a Smacker header.
    /// </summary>
    public sealed class SmackerAudioTrack
    {
        public int Index { get; internal set; }
        /// <summary>True when the header declares data for this track.</summary>
        public bool Present { get; internal set; }
        /// <summary>DPCM/Huffman packed (otherwise raw PCM).</summary>
        public bool Compressed { get; internal set; }
        /// <summary>Bink-style audio (bits 27/26 of the rate field); not supported, data is skipped.</summary>
        public bool Unsupported { get; internal set; }
        /// <summary>8 or 16. 8-bit data is unsigned, 16-bit is signed little-endian.</summary>
        public int Bits { get; internal set; }
        public int Channels { get; internal set; }
        public int SampleRate { get; internal set; }
        /// <summary>Largest per-frame chunk size declared in the header (bytes).</summary>
        public int MaxChunkSize { get; internal set; }
    }

    /// <summary>
    /// Smacker video (.smk, signatures SMK2 and SMK4). 104-byte little-endian header
    /// (size, frame count, frame rate, flags, audio track sizes/rates, Huffman tree sizes),
    /// then a per-frame size table (low two bits are flags), a per-frame type table
    /// (bit 0 = palette chunk, bits 1..7 = audio track 0..6 chunk), the packed Huffman trees
    /// (MMAP, MCLR, FULL, TYPE "big trees") and the frame data.
    /// Each frame is: optional palette delta chunk, optional audio chunks, then the
    /// LSB-first video bitstream coding 4x4 blocks (mono / full / void / solid).
    /// Frames are decoded sequentially into an 8-bit index buffer plus a 256-entry palette.
    /// </summary>
    public sealed class SmackerVideo
    {
        public const int HeaderSize = 104;
        public const int MaxAudioTracks = 7;

        const uint FlagRingFrame = 1, FlagYInterlaced = 2, FlagYDoubled = 4;

        /// <summary>Block run lengths indexed by bits 2..7 of a TYPE code.</summary>
        static readonly int[] RunTable = BuildRunTable();

        byte[] _d;
        int[] _frameOffset;
        int[] _frameSize;
        byte[] _frameType;
        bool[] _keyFrame;
        BigTree _mmap, _mclr, _full, _type;
        uint[] _palScratch = new uint[256];
        short[][] _audio = new short[MaxAudioTracks][];
        readonly byte[] _blockTmp = new byte[16];

        /// <summary>True for SMK4 (enables the extra FULL block sub-modes).</summary>
        public bool IsSmk4 { get; private set; }
        public int Width { get; private set; }
        /// <summary>Stored (decoded) height. Display height is twice this when <see cref="IsYDoubled"/> or <see cref="IsYInterlaced"/>.</summary>
        public int Height { get; private set; }
        /// <summary>Height the original player shows (stored lines doubled or interlaced with black).</summary>
        public int DisplayHeight => (IsYDoubled || IsYInterlaced) ? Height * 2 : Height;
        /// <summary>Number of frames, not counting the ring frame.</summary>
        public int FrameCount { get; private set; }
        /// <summary>Raw header frame-rate field (&gt;0 ms per frame, &lt;0 units of 10 us, 0 = 10 fps).</summary>
        public int FrameRateRaw { get; private set; }
        public double FrameRate { get; private set; }
        public uint Flags { get; private set; }
        /// <summary>The file carries an extra frame that turns the last frame back into frame 0 (seamless looping).</summary>
        public bool HasRingFrame => (Flags & FlagRingFrame) != 0;
        public bool IsYInterlaced => (Flags & FlagYInterlaced) != 0;
        public bool IsYDoubled => (Flags & FlagYDoubled) != 0;
        public SmackerAudioTrack[] AudioTracks { get; private set; }

        /// <summary>When true, <see cref="DecodeNextFrame"/> wraps around after the last frame (via the ring frame if present).</summary>
        public bool Loop;

        /// <summary>Index of the frame currently held in <see cref="Indices"/> (-1 before the first decode).</summary>
        public int CurrentFrame { get; private set; } = -1;
        /// <summary>Current frame, Width*Height palette indices, top-down rows.</summary>
        public byte[] Indices { get; private set; }
        /// <summary>Current palette as RGBA32 (r | g&lt;&lt;8 | b&lt;&lt;16 | a&lt;&lt;24), alpha always 255 (index 0 is an ordinary colour).</summary>
        public uint[] Palette { get; private set; }
        /// <summary>True when the last decoded frame carried a palette chunk.</summary>
        public bool PaletteChanged { get; private set; }

        /// <summary>Track 0 PCM of the last decoded frame, interleaved signed 16-bit (8-bit data is expanded). Empty when none.</summary>
        public short[] LastAudio => _audio[0];
        public int SampleRate => AudioTracks[0].SampleRate;
        public int Channels => AudioTracks[0].Channels;
        public int Bits => AudioTracks[0].Bits;
        public bool HasAudio => AudioTracks[0].Present;

        /// <summary>Frame-size table key-frame bit (frame index 0..FrameCount, the last being the ring frame).</summary>
        public bool IsKeyFrame(int frame) => _keyFrame[frame];

        /// <summary>PCM of a track for the last decoded frame (see <see cref="LastAudio"/>).</summary>
        public short[] GetLastAudio(int track) => _audio[track];

        SmackerVideo() { }

        public static SmackerVideo Open(byte[] data)
        {
            if (data == null || data.Length < HeaderSize) throw new InvalidDataException("smk too short");
            if (data[0] != 'S' || data[1] != 'M' || data[2] != 'K' || (data[3] != '2' && data[3] != '4'))
                throw new InvalidDataException("not a Smacker file");
            var v = new SmackerVideo { _d = data, IsSmk4 = data[3] == '4' };
            v.Width = BitConverter.ToInt32(data, 4);
            v.Height = BitConverter.ToInt32(data, 8);
            v.FrameCount = BitConverter.ToInt32(data, 12);
            v.FrameRateRaw = BitConverter.ToInt32(data, 16);
            v.Flags = BitConverter.ToUInt32(data, 20);
            if (v.Width <= 0 || v.Height <= 0 || v.Width > 8192 || v.Height > 8192 || v.FrameCount < 0)
                throw new InvalidDataException("bad smk dimensions");

            int r = v.FrameRateRaw;
            v.FrameRate = r > 0 ? 1000.0 / r : r < 0 ? 100000.0 / -(double)r : 10.0;

            v.AudioTracks = new SmackerAudioTrack[MaxAudioTracks];
            for (int t = 0; t < MaxAudioTracks; t++)
            {
                uint rate = BitConverter.ToUInt32(data, 72 + t * 4);
                v.AudioTracks[t] = new SmackerAudioTrack
                {
                    Index = t,
                    MaxChunkSize = BitConverter.ToInt32(data, 24 + t * 4),
                    Present = (rate & 0x40000000) != 0,
                    Compressed = (rate & 0x80000000) != 0,
                    Bits = (rate & 0x20000000) != 0 ? 16 : 8,
                    Channels = (rate & 0x10000000) != 0 ? 2 : 1,
                    Unsupported = (rate & 0x0C000000) != 0,
                    SampleRate = (int)(rate & 0xFFFFFF),
                };
                v._audio[t] = Array.Empty<short>();
            }

            int treesSize = BitConverter.ToInt32(data, 52);
            int stored = v.FrameCount + (v.HasRingFrame ? 1 : 0);
            long tablesEnd = HeaderSize + (long)stored * 5;
            if (tablesEnd + treesSize > data.Length || treesSize < 0) throw new InvalidDataException("smk truncated");

            v._frameOffset = new int[stored];
            v._frameSize = new int[stored];
            v._frameType = new byte[stored];
            v._keyFrame = new bool[stored];
            long off = tablesEnd + treesSize;
            for (int i = 0; i < stored; i++)
            {
                uint s = BitConverter.ToUInt32(data, HeaderSize + i * 4);
                v._keyFrame[i] = (s & 1) != 0;
                long size = s & ~3u;
                if (off > data.Length) off = data.Length;
                v._frameOffset[i] = (int)off;
                v._frameSize[i] = (int)Math.Min(size, data.Length - off);
                v._frameType[i] = data[HeaderSize + stored * 4 + i];
                off += size;
            }

            var br = new BitReader(data, (int)tablesEnd, (int)(tablesEnd + treesSize));
            v._mmap = BigTree.Read(br);
            v._mclr = BigTree.Read(br);
            v._full = BigTree.Read(br);
            v._type = BigTree.Read(br);

            v.Indices = new byte[v.Width * v.Height];
            v.Palette = new uint[256];
            v.ResetPalette();
            return v;
        }

        public static SmackerVideo Open(string path) => Open(File.ReadAllBytes(path));

        /// <summary>Back to the start: clears image and palette; the next decode yields frame 0.</summary>
        public void Rewind()
        {
            Array.Clear(Indices, 0, Indices.Length);
            ResetPalette();
            CurrentFrame = -1;
            PaletteChanged = false;
            for (int t = 0; t < MaxAudioTracks; t++) _audio[t] = Array.Empty<short>();
        }

        void ResetPalette()
        {
            // Entries never set by a palette chunk are opaque black.
            for (int i = 0; i < 256; i++) Palette[i] = 0xFF000000u;
        }

        /// <summary>
        /// Decodes the next frame into <see cref="Indices"/>/<see cref="Palette"/>/<see cref="LastAudio"/>.
        /// Returns false at the end of the video (unless <see cref="Loop"/> is set).
        /// </summary>
        public bool DecodeNextFrame()
        {
            int next = CurrentFrame + 1;
            if (next >= FrameCount)
            {
                if (!Loop || FrameCount == 0) return false;
                if (HasRingFrame)
                {
                    // The ring frame is a delta from the last frame back to frame 0.
                    DecodeFrame(FrameCount);
                    CurrentFrame = 0;
                    return true;
                }
                Rewind();
                next = 0;
            }
            DecodeFrame(next);
            CurrentFrame = next;
            return true;
        }

        void DecodeFrame(int f)
        {
            int pos = _frameOffset[f];
            int end = pos + _frameSize[f];
            byte type = _frameType[f];

            PaletteChanged = false;
            if ((type & 1) != 0 && pos < end)
            {
                int len = _d[pos] * 4;
                if (len == 0) len = 1;
                int pend = Math.Min(pos + len, end);
                DecodePalette(pos + 1, pend);
                PaletteChanged = true;
                pos += len;
            }

            for (int t = 0; t < MaxAudioTracks; t++)
            {
                _audio[t] = Array.Empty<short>();
                if ((type & (2 << t)) == 0) continue;
                if (pos + 4 > end) break;
                int len = BitConverter.ToInt32(_d, pos);
                if (len < 4 || pos + len > end) len = end - pos;
                try { _audio[t] = DecodeAudio(AudioTracks[t], pos + 4, pos + len); }
                catch (InvalidDataException) { _audio[t] = Array.Empty<short>(); }
                pos += len;
            }

            if (pos < end) DecodeVideo(pos, end);
        }

        static uint Rgb(int r, int g, int b)
        {
            // 6-bit to 8-bit by bit replication; identical to the expansion table ffmpeg uses.
            r &= 0x3f; g &= 0x3f; b &= 0x3f;
            uint R = (uint)((r << 2) | (r >> 4)), G = (uint)((g << 2) | (g >> 4)), B = (uint)((b << 2) | (b >> 4));
            return R | (G << 8) | (B << 16) | 0xFF000000u;
        }

        void DecodePalette(int pos, int end)
        {
            // Copies always read the palette as it was before this chunk.
            var old = _palScratch;
            Array.Copy(Palette, old, 256);
            int i = 0;
            while (i < 256 && pos < end)
            {
                int c = _d[pos++];
                if ((c & 0x80) != 0)
                {
                    i += (c & 0x7f) + 1; // keep entries
                }
                else if ((c & 0x40) != 0)
                {
                    int n = (c & 0x3f) + 1;
                    if (pos >= end) break;
                    int src = _d[pos++];
                    for (int k = 0; k < n && i < 256 && src < 256; k++) Palette[i++] = old[src++];
                }
                else
                {
                    if (pos + 2 > end) break;
                    Palette[i++] = Rgb(c, _d[pos], _d[pos + 1]);
                    pos += 2;
                }
            }
        }

        void DecodeVideo(int pos, int end)
        {
            var br = new BitReader(_d, pos, end);
            _mmap.ResetCache(); _mclr.ResetCache(); _full.ResetCache(); _type.ResetCache();

            int w = Width, h = Height;
            int bw = (w + 3) >> 2, bh = (h + 3) >> 2;
            int blocks = bw * bh;
            bool partial = (w & 3) != 0 || (h & 3) != 0;
            byte[] px = Indices;
            int blk = 0;
            while (blk < blocks)
            {
                int v = _type.Decode(br);
                int kind = v & 3;
                int run = RunTable[(v >> 2) & 0x3f];
                int data = v >> 8;
                int mode = 0;
                if (kind == 1 && IsSmk4)
                {
                    if (br.ReadBit() != 0) mode = 1;
                    else if (br.ReadBit() != 0) mode = 2;
                }
                if (kind == 2) { blk += run; continue; } // void: keep previous pixels

                for (; run > 0 && blk < blocks; run--, blk++)
                {
                    int bx = (blk % bw) << 2, by = (blk / bw) << 2;
                    bool clip = partial && (bx + 4 > w || by + 4 > h);
                    // Fast path writes straight into the frame; clipped blocks go via tmp.
                    if (!clip)
                    {
                        int o = by * w + bx;
                        switch (kind)
                        {
                            case 0: MonoBlock(br, px, o, w); break;
                            case 1: FullBlock(br, px, o, w, mode); break;
                            default:
                                byte col = (byte)data;
                                for (int y = 0; y < 4; y++, o += w)
                                { px[o] = col; px[o + 1] = col; px[o + 2] = col; px[o + 3] = col; }
                                break;
                        }
                    }
                    else
                    {
                        var t = _blockTmp;
                        switch (kind)
                        {
                            case 0: MonoBlock(br, t, 0, 4); break;
                            case 1: FullBlock(br, t, 0, 4, mode); break;
                            default: for (int k = 0; k < 16; k++) t[k] = (byte)data; break;
                        }
                        for (int y = 0; y < 4 && by + y < h; y++)
                            for (int x = 0; x < 4 && bx + x < w; x++)
                                px[(by + y) * w + bx + x] = t[y * 4 + x];
                    }
                }
            }
        }

        void MonoBlock(BitReader br, byte[] px, int o, int stride)
        {
            int colors = _mclr.Decode(br);
            int map = _mmap.Decode(br);
            byte hi = (byte)(colors >> 8), lo = (byte)colors;
            for (int y = 0; y < 4; y++, o += stride, map >>= 4)
            {
                px[o] = (map & 1) != 0 ? hi : lo;
                px[o + 1] = (map & 2) != 0 ? hi : lo;
                px[o + 2] = (map & 4) != 0 ? hi : lo;
                px[o + 3] = (map & 8) != 0 ? hi : lo;
            }
        }

        void FullBlock(BitReader br, byte[] px, int o, int stride, int mode)
        {
            switch (mode)
            {
                case 0: // each row: right pixel pair, then left pixel pair
                    for (int y = 0; y < 4; y++, o += stride)
                    {
                        int a = _full.Decode(br);
                        px[o + 2] = (byte)a; px[o + 3] = (byte)(a >> 8);
                        int b = _full.Decode(br);
                        px[o] = (byte)b; px[o + 1] = (byte)(b >> 8);
                    }
                    break;
                case 1: // SMK4 double: one code per two rows, each byte covers 2x2
                    for (int y = 0; y < 2; y++)
                    {
                        int a = _full.Decode(br);
                        byte l = (byte)a, r = (byte)(a >> 8);
                        for (int k = 0; k < 2; k++, o += stride)
                        { px[o] = l; px[o + 1] = l; px[o + 2] = r; px[o + 3] = r; }
                    }
                    break;
                default: // SMK4 half: two codes per row pair, rows duplicated vertically
                    for (int y = 0; y < 2; y++)
                    {
                        int right = _full.Decode(br);
                        int left = _full.Decode(br);
                        for (int k = 0; k < 2; k++, o += stride)
                        {
                            px[o] = (byte)left; px[o + 1] = (byte)(left >> 8);
                            px[o + 2] = (byte)right; px[o + 3] = (byte)(right >> 8);
                        }
                    }
                    break;
            }
        }

        short[] DecodeAudio(SmackerAudioTrack trk, int pos, int end)
        {
            if (!trk.Present || trk.Unsupported || end <= pos) return Array.Empty<short>();
            if (!trk.Compressed)
            {
                int n = end - pos;
                if (trk.Bits == 16)
                {
                    var s = new short[n / 2];
                    for (int i = 0; i < s.Length; i++) s[i] = (short)(_d[pos + i * 2] | (_d[pos + i * 2 + 1] << 8));
                    return s;
                }
                var s8 = new short[n];
                for (int i = 0; i < n; i++) s8[i] = (short)((_d[pos + i] - 128) << 8);
                return s8;
            }

            if (end - pos < 4) return Array.Empty<short>();
            int unpacked = BitConverter.ToInt32(_d, pos);
            var br = new BitReader(_d, pos + 4, end);
            if (br.ReadBit() == 0) return Array.Empty<short>();
            int stereo = br.ReadBit();
            int is16 = br.ReadBit();
            int bytesPer = is16 + 1;
            int total = unpacked / bytesPer;
            if (unpacked < 0 || total > 16 * 1024 * 1024) throw new InvalidDataException("bad smk audio size");

            var trees = new ByteTree[1 << (stereo + is16)];
            for (int i = 0; i < trees.Length; i++) trees[i] = ByteTree.Read(br);

            var outp = new short[total];
            int ch = stereo + 1;
            if (total < ch) return Array.Empty<short>();
            if (is16 != 0)
            {
                // Initial samples: right channel first, each stored high byte first.
                int[] pred = new int[2];
                for (int c = stereo; c >= 0; c--)
                {
                    int hi = br.ReadBits(8);
                    int lo = br.ReadBits(8);
                    pred[c] = (short)((hi << 8) | lo);
                }
                for (int c = 0; c < ch; c++) outp[c] = (short)pred[c];
                for (int i = ch; i < total; i++)
                {
                    int c = i & stereo;
                    int delta = trees[c * 2].Decode(br) | (trees[c * 2 + 1].Decode(br) << 8);
                    pred[c] = (short)(pred[c] + delta);
                    outp[i] = (short)pred[c];
                }
            }
            else
            {
                int[] pred = new int[2];
                for (int c = stereo; c >= 0; c--) pred[c] = br.ReadBits(8);
                for (int c = 0; c < ch; c++) outp[c] = (short)((pred[c] - 128) << 8);
                for (int i = ch; i < total; i++)
                {
                    int c = i & stereo;
                    pred[c] = (pred[c] + trees[c].Decode(br)) & 0xFF;
                    outp[i] = (short)((pred[c] - 128) << 8);
                }
            }
            return outp;
        }

        /// <summary>
        /// Expands the current frame to RGBA32 bytes (Width*Height*4). With flipY the first output
        /// row is the bottom image row (Unity texture order).
        /// </summary>
        public void ToRgba(byte[] dst, bool flipY)
        {
            int w = Width, h = Height;
            if (dst == null || dst.Length < w * h * 4) throw new ArgumentException("dst too small");
            var pal = Palette;
            var src = Indices;
            for (int y = 0; y < h; y++)
            {
                int s = y * w;
                int d = (flipY ? h - 1 - y : y) * w * 4;
                for (int x = 0; x < w; x++, d += 4)
                {
                    uint c = pal[src[s + x]];
                    dst[d] = (byte)c; dst[d + 1] = (byte)(c >> 8); dst[d + 2] = (byte)(c >> 16); dst[d + 3] = (byte)(c >> 24);
                }
            }
        }

        static int[] BuildRunTable()
        {
            var t = new int[64];
            for (int i = 0; i < 59; i++) t[i] = i + 1;
            for (int i = 59; i < 64; i++) t[i] = 128 << (i - 59);
            return t;
        }

        /// <summary>LSB-first bit reader; reads past the end yield zero bits.</summary>
        sealed class BitReader
        {
            readonly byte[] _d;
            readonly int _end;
            int _pos;      // next byte to load
            uint _buf;     // pending bits, LSB first
            int _cnt;

            public BitReader(byte[] d, int start, int end)
            {
                _d = d; _pos = start; _end = Math.Min(end, d.Length);
            }

            void Fill()
            {
                while (_cnt <= 24)
                {
                    uint b = _pos < _end ? _d[_pos] : 0u;
                    _pos++;
                    _buf |= b << _cnt;
                    _cnt += 8;
                }
            }

            public int ReadBit()
            {
                if (_cnt == 0) Fill();
                int b = (int)(_buf & 1);
                _buf >>= 1; _cnt--;
                return b;
            }

            public int ReadBits(int n)
            {
                if (_cnt < n) Fill();
                int v = (int)(_buf & ((1u << n) - 1));
                _buf >>= n; _cnt -= n;
                return v;
            }

            /// <summary>Next up to 24 bits without consuming them.</summary>
            public uint Peek(int n)
            {
                if (_cnt < n) Fill();
                return _buf & ((1u << n) - 1);
            }

            public void Skip(int n) { _buf >>= n; _cnt -= n; }
        }

        /// <summary>
        /// Prefix-code tree stored as a flat child array: entries &gt;= 0 are internal node indices,
        /// entries &lt; 0 are leaves (~payload). A lookup table resolves the first <see cref="TableBits"/> bits.
        /// </summary>
        abstract class PrefixTree
        {
            protected const int TableBits = 10;
            protected readonly List<int> Nodes = new List<int>();
            protected int Root;             // node index or ~payload
            int[] _table;                   // per prefix: (entry << 5) | consumedBits, or partial node
            int[] _nodes;

            protected int Walk(BitReader br)
            {
                int n = Root;
                if (n < 0) return ~n;
                uint peek = br.Peek(TableBits);
                int e = _table[peek];
                int bits = e & 31;
                br.Skip(bits);
                n = e >> 5;
                while (n >= 0) n = _nodes[2 * n + br.ReadBit()];
                return ~n;
            }

            protected void BuildTable()
            {
                _nodes = Nodes.ToArray();
                _table = new int[1 << TableBits];
                if (Root < 0) return;
                for (int p = 0; p < _table.Length; p++)
                {
                    int n = Root, used = 0;
                    while (n >= 0 && used < TableBits) n = Nodes[2 * n + ((p >> used++) & 1)];
                    _table[p] = (n << 5) | used;
                }
            }

            protected int NewNode()
            {
                Nodes.Add(0); Nodes.Add(0);
                return Nodes.Count / 2 - 1;
            }
        }

        /// <summary>8-bit Huffman tree: presence bit, recursive tree (1 = node, 0 = 8-bit leaf), 0 terminator bit.</summary>
        sealed class ByteTree : PrefixTree
        {
            public static ByteTree Read(BitReader br)
            {
                var t = new ByteTree();
                if (br.ReadBit() == 0) { t.Root = ~0; t.BuildTable(); return t; }
                t.Root = t.ReadNode(br, 0);
                br.ReadBit();
                t.BuildTable();
                return t;
            }

            int ReadNode(BitReader br, int depth)
            {
                if (depth > 32) throw new InvalidDataException("smk huffman tree too deep");
                if (br.ReadBit() == 0) return ~br.ReadBits(8);
                int n = NewNode();
                int l = ReadNode(br, depth + 1);
                int r = ReadNode(br, depth + 1);
                Nodes[2 * n] = l; Nodes[2 * n + 1] = r;
                return n;
            }

            public int Decode(BitReader br) => Walk(br);
        }

        /// <summary>
        /// 16-bit "big tree": leaves are (low tree code | high tree code &lt;&lt; 8); leaves equal to one of
        /// three escape values instead return a recently-used value cache, which is updated after
        /// every decode and reset at the start of each frame.
        /// </summary>
        sealed class BigTree : PrefixTree
        {
            const int CacheLeaf = 0x10000;
            readonly int[] _esc = new int[3];
            readonly int[] _cache = new int[3];
            ByteTree _lo, _hi;

            public static BigTree Read(BitReader br)
            {
                var t = new BigTree();
                if (br.ReadBit() == 0) { t.Root = ~0; t.BuildTable(); return t; }
                t._lo = ByteTree.Read(br);
                t._hi = ByteTree.Read(br);
                for (int i = 0; i < 3; i++) t._esc[i] = br.ReadBits(16);
                t.Root = t.ReadNode(br, 0);
                br.ReadBit();
                t.BuildTable();
                return t;
            }

            int ReadNode(BitReader br, int depth)
            {
                if (depth > 64) throw new InvalidDataException("smk big tree too deep");
                if (br.ReadBit() == 0)
                {
                    int v = _lo.Decode(br) | (_hi.Decode(br) << 8);
                    if (v == _esc[0]) v = CacheLeaf;
                    else if (v == _esc[1]) v = CacheLeaf + 1;
                    else if (v == _esc[2]) v = CacheLeaf + 2;
                    return ~v;
                }
                int n = NewNode();
                int l = ReadNode(br, depth + 1);
                int r = ReadNode(br, depth + 1);
                Nodes[2 * n] = l; Nodes[2 * n + 1] = r;
                return n;
            }

            public void ResetCache() { _cache[0] = _cache[1] = _cache[2] = 0; }

            public int Decode(BitReader br)
            {
                int v = Walk(br);
                if (v >= CacheLeaf) v = _cache[v - CacheLeaf];
                if (_cache[0] != v)
                {
                    _cache[2] = _cache[1];
                    _cache[1] = _cache[0];
                    _cache[0] = v;
                }
                return v;
            }
        }
    }
}
