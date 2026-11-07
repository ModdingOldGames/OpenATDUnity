using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenAT.Cli
{
    /// <summary>Minimal RGBA PNG writer for dumping decoded assets to a local scratch folder.</summary>
    static class Png
    {
        public static void Write(string path, int w, int h, byte[] rgba)
        {
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BE(ihdr, 0, w); BE(ihdr, 4, h);
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(fs, "IHDR", ihdr);
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgba, y * w * 4, w * 4);
            }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(zs); }
            Chunk(fs, "IDAT", z.ToArray());
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
            var t = Encoding.ASCII.GetBytes(type);
            s.Write(t); s.Write(data);
            uint crc = Crc(t, 0xFFFFFFFF); crc = Crc(data, crc) ^ 0xFFFFFFFF;
            var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
        }

        static uint[] _table;
        static uint Crc(byte[] d, uint c)
        {
            if (_table == null)
            {
                _table = new uint[256];
                for (uint n = 0; n < 256; n++) { uint k = n; for (int i = 0; i < 8; i++) k = (k & 1) != 0 ? 0xEDB88320 ^ (k >> 1) : k >> 1; _table[n] = k; }
            }
            foreach (var b in d) c = _table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c;
        }
    }
}
