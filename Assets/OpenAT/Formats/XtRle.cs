using System;
using System.IO;

namespace OpenAT.Formats
{
    /// <summary>
    /// "xtRLE" container used for the .csv tables, .res texts and several .dat files.
    /// Layout (see docs/spec/formats/xtrle.md): 6-byte magic "xtRLE\0", int32 version, int32 unpacked
    /// size (version >= 0x101), then runs. A run byte with the high bit set is followed by (n &amp; 0x7F)
    /// literal bytes (each XORed with 0xA5 from version 0x102 on); otherwise the next byte repeats n times.
    /// </summary>
    public static class XtRle
    {
        static readonly byte[] Magic = { (byte)'x', (byte)'t', (byte)'R', (byte)'L', (byte)'E', 0 };

        public static bool IsPacked(byte[] data)
        {
            if (data == null || data.Length < Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return false;
            return true;
        }

        public static byte[] DecodeIfPacked(byte[] data) => IsPacked(data) ? Decode(data) : data;

        public static byte[] Decode(byte[] data)
        {
            if (!IsPacked(data)) throw new InvalidDataException("Not an xtRLE stream");
            int pos = Magic.Length;
            int version = BitConverter.ToInt32(data, pos); pos += 4;
            byte key = version >= 0x102 ? (byte)0xA5 : (byte)0;
            int size;
            if (version >= 0x101) { size = BitConverter.ToInt32(data, pos); pos += 4; }
            else size = -1;

            var output = new MemoryStream(size > 0 ? size : data.Length * 2);
            while (pos < data.Length && (size < 0 || output.Length < size))
            {
                int n = data[pos++];
                if ((n & 0x80) != 0)
                {
                    n &= 0x7F;
                    for (int i = 0; i < n && pos < data.Length; i++) output.WriteByte((byte)(data[pos++] ^ key));
                }
                else
                {
                    if (pos >= data.Length) break;
                    byte b = data[pos++];
                    for (int i = 0; i < n; i++) output.WriteByte(b);
                }
            }
            var result = output.ToArray();
            if (size >= 0 && result.Length > size) Array.Resize(ref result, size);
            return result;
        }

        /// <summary>Packs data (used for our own save/config files and for round-trip tests).</summary>
        public static byte[] Encode(byte[] data, int version = 0x102)
        {
            byte key = version >= 0x102 ? (byte)0xA5 : (byte)0;
            var o = new MemoryStream();
            o.Write(Magic, 0, Magic.Length);
            o.Write(BitConverter.GetBytes(version), 0, 4);
            o.Write(BitConverter.GetBytes(data.Length), 0, 4);
            int i = 0;
            while (i < data.Length)
            {
                int run = 1;
                while (i + run < data.Length && run < 127 && data[i + run] == data[i]) run++;
                if (run >= 3)
                {
                    o.WriteByte((byte)run); o.WriteByte(data[i]); i += run; continue;
                }
                int start = i, len = 0;
                while (i < data.Length && len < 127)
                {
                    if (i + 2 < data.Length && data[i] == data[i + 1] && data[i] == data[i + 2]) break;
                    i++; len++;
                }
                o.WriteByte((byte)(0x80 | len));
                for (int k = 0; k < len; k++) o.WriteByte((byte)(data[start + k] ^ key));
            }
            return o.ToArray();
        }
    }
}
