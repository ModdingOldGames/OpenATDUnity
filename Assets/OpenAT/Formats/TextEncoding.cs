using System.Text;

namespace OpenAT.Formats
{
    /// <summary>
    /// The original stores text as single bytes (Windows-1252 style). We keep every byte as the
    /// char with the same code (Latin-1 mapping) so fonts can index glyphs by (int)ch directly.
    /// </summary>
    public static class TextEncoding
    {
        public static string Decode(byte[] data, int offset, int count)
        {
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++) sb.Append((char)data[offset + i]);
            return sb.ToString();
        }

        public static string Decode(byte[] data) => Decode(data, 0, data.Length);

        // Windows-1252 0x80..0x9F (0 = unassigned).
        static readonly char[] Cp1252High =
        {
            '\u20AC', '\0', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021', '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\0', '\u017D', '\0',
            '\0', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\0', '\u017E', '\u0178',
        };

        /// <summary>
        /// Original single-byte code of a string character. Text read from the install keeps its byte
        /// as the char code (<= 0xFF, returned as is); characters typed by the player or written in C#
        /// are Unicode, so the Windows-1252 specials (euro sign, low quotes, OE/S/Z with caron, trademark ...) are mapped back to their byte.
        /// Anything else becomes '?'.
        /// </summary>
        public static int ToByte(char c)
        {
            if (c <= 0xFF) return c;
            for (int i = 0; i < Cp1252High.Length; i++) if (Cp1252High[i] == c) return 0x80 + i;
            return '?';
        }

        /// <summary>The original uses byte 0xB5 inside texts as a forced line break.</summary>
        public const char LineBreak = (char)0xB5;
    }
}
