using System;
using System.IO;

namespace OpenAT.Formats
{
    /// <summary>
    /// 8-bit palette image (IFF "PBM " chunky or "ILBM" planar, e.g. gli/EarthAll.lbm — the globe
    /// textures). Supports ByteRun1 compression. Pixels are palette indices, top-down.
    /// </summary>
    public sealed class IlbmImage
    {
        public int Width, Height;
        public byte[] Indices;
        /// <summary>256 entries RGBA32 (r | g&lt;&lt;8 | b&lt;&lt;16 | a&lt;&lt;24).</summary>
        public uint[] Palette = new uint[256];

        public static IlbmImage Parse(byte[] d)
        {
            if (d.Length < 12 || d[0] != 'F' || d[1] != 'O' || d[2] != 'R' || d[3] != 'M') throw new InvalidDataException("not IFF");
            bool chunky = d[8] == 'P' && d[9] == 'B' && d[10] == 'M';
            var img = new IlbmImage();
            int planes = 8, compression = 0, pos = 12;
            byte[] body = null;
            while (pos + 8 <= d.Length)
            {
                string id = "" + (char)d[pos] + (char)d[pos + 1] + (char)d[pos + 2] + (char)d[pos + 3];
                int len = BE32(d, pos + 4);
                int data = pos + 8;
                switch (id)
                {
                    case "BMHD":
                        img.Width = BE16(d, data);
                        img.Height = BE16(d, data + 2);
                        planes = d[data + 8];
                        compression = d[data + 10];
                        break;
                    case "CMAP":
                        for (int i = 0; i < len / 3 && i < 256; i++)
                            img.Palette[i] = d[data + i * 3] | ((uint)d[data + i * 3 + 1] << 8) | ((uint)d[data + i * 3 + 2] << 16) | 0xFF000000u;
                        break;
                    case "BODY":
                        body = new byte[Math.Min(len, d.Length - data)];
                        Buffer.BlockCopy(d, data, body, 0, body.Length);
                        break;
                }
                pos = data + len + (len & 1);
            }
            if (body == null || img.Width <= 0) throw new InvalidDataException("IFF without BODY/BMHD");

            int rowBytesPlanar = ((img.Width + 15) / 16) * 2;
            int rowBytes = chunky ? img.Width + (img.Width & 1) : rowBytesPlanar * planes;
            var raw = compression == 1 ? Unpack(body, rowBytes * img.Height) : body;
            img.Indices = new byte[img.Width * img.Height];
            for (int y = 0; y < img.Height; y++)
            {
                int row = y * rowBytes;
                if (chunky)
                {
                    Buffer.BlockCopy(raw, row, img.Indices, y * img.Width, Math.Min(img.Width, Math.Max(0, raw.Length - row)));
                    continue;
                }
                for (int x = 0; x < img.Width; x++)
                {
                    int v = 0;
                    for (int p = 0; p < planes; p++)
                    {
                        int b = row + p * rowBytesPlanar + (x >> 3);
                        if (b < raw.Length && (raw[b] & (0x80 >> (x & 7))) != 0) v |= 1 << p;
                    }
                    img.Indices[y * img.Width + x] = (byte)v;
                }
            }
            return img;
        }

        static byte[] Unpack(byte[] src, int size)
        {
            var dst = new byte[size];
            int s = 0, o = 0;
            while (o < size && s < src.Length)
            {
                sbyte n = (sbyte)src[s++];
                if (n >= 0)
                {
                    int count = n + 1;
                    for (int i = 0; i < count && o < size && s < src.Length; i++) dst[o++] = src[s++];
                }
                else if (n != -128)
                {
                    int count = -n + 1;
                    byte b = s < src.Length ? src[s++] : (byte)0;
                    for (int i = 0; i < count && o < size; i++) dst[o++] = b;
                }
            }
            return dst;
        }

        static int BE32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
        static int BE16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

        public byte[] ToRgba()
        {
            var o = new byte[Width * Height * 4];
            for (int i = 0; i < Indices.Length; i++)
            {
                uint c = Palette[Indices[i]];
                o[i * 4] = (byte)c; o[i * 4 + 1] = (byte)(c >> 8); o[i * 4 + 2] = (byte)(c >> 16); o[i * 4 + 3] = 255;
            }
            return o;
        }
    }
}
