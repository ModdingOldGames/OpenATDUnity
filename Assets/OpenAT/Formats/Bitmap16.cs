using System;

namespace OpenAT.Formats
{
    /// <summary>
    /// A 16-bit RGB565 image as stored by the original (top-down rows). Pixel value 0 is the
    /// transparent colour key for sprites.
    /// </summary>
    public sealed class Bitmap16
    {
        public readonly int Width;
        public readonly int Height;
        public readonly ushort[] Pixels;

        public Bitmap16(int width, int height, ushort[] pixels = null)
        {
            Width = width;
            Height = height;
            Pixels = pixels ?? new ushort[width * height];
            if (Pixels.Length < width * height) throw new ArgumentException("pixel buffer too small");
        }

        public ushort this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }

        public const ushort ColorKey = 0;

        public static uint ToRgba32(ushort c)
        {
            uint r = (uint)(c >> 11) & 0x1F, g = (uint)(c >> 5) & 0x3F, b = (uint)c & 0x1F;
            r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
            return r | (g << 8) | (b << 16) | 0xFF000000u;
        }

        public static ushort FromRgb(int r, int g, int b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

        /// <summary>RGBA32 bytes, bottom-up when <paramref name="flipY"/> (Unity texture order).
        /// Colour-key pixels become fully transparent when <paramref name="keyed"/>.</summary>
        public byte[] ToRgba(bool keyed = true, bool flipY = false)
        {
            var dst = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                int sy = flipY ? Height - 1 - y : y;
                int si = sy * Width, di = y * Width * 4;
                for (int x = 0; x < Width; x++, di += 4)
                {
                    ushort c = Pixels[si + x];
                    if (keyed && c == ColorKey) continue;
                    uint v = ToRgba32(c);
                    dst[di] = (byte)v; dst[di + 1] = (byte)(v >> 8); dst[di + 2] = (byte)(v >> 16); dst[di + 3] = 255;
                }
            }
            return dst;
        }
    }
}
