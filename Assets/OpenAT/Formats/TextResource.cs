using System;
using System.Collections.Generic;
using System.Text;

namespace OpenAT.Formats
{
    /// <summary>
    /// Text resource file (.res, usually xtRLE packed). Line based:
    ///   <c>&gt;GROUP</c>   starts a group (4-char tag packed little-endian into an int, or a number)
    ///   <c>&gt;&gt;ID</c>   starts an entry (same encoding)
    ///   two leading spaces: a text line appended to the current entry
    ///   <c>//</c> comments to end of line.
    /// A text may contain language segments <c>D::German E::English ...</c>; the requested language is
    /// selected with fallback to English, then German.
    /// </summary>
    public sealed class TextResource
    {
        /// <summary>Language indices of the original (the index into <see cref="LanguageLetters"/>).</summary>
        public enum Language { German = 0, English = 1, French = 2, Taiwanese = 3, Polish = 4, Dutch = 5, Italian = 6, Spanish = 7, Portuguese = 8, Brazilian = 9 }

        /// <summary>Language letters by index, as used by the original's language segments.</summary>
        public const string LanguageLetters = "DEFTPNISOBJKLMNQRTUV";

        readonly Dictionary<long, string> _texts = new Dictionary<long, string>();
        public Language Lang { get; set; } = Language.English;

        public int Count => _texts.Count;

        public static TextResource Parse(byte[] raw)
        {
            var res = new TextResource();
            var data = XtRle.DecodeIfPacked(raw);
            int group = -1, id = -1;
            bool haveEntry = false;
            int pos = 0;
            while (pos < data.Length)
            {
                int start = pos;
                while (pos < data.Length && data[pos] != '\r' && data[pos] != '\n' && data[pos] != 0x1A) pos++;
                string line = TextEncoding.Decode(data, start, pos - start);
                while (pos < data.Length && (data[pos] == '\r' || data[pos] == '\n' || data[pos] == 0x1A)) pos++;

                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line.Substring(0, comment);
                line = line.TrimEnd(' ');

                if (line.Length >= 2 && line[0] == '>' && line[1] == '>')
                {
                    id = ParseKey(line.Substring(2));
                    haveEntry = true;
                }
                else if (line.Length >= 1 && line[0] == '>')
                {
                    group = ParseKey(line.Substring(1));
                }
                else if (line.Length > 2 && line[0] == ' ' && line[1] == ' ' && haveEntry)
                {
                    long k = Key(group, id);
                    string text = line.Substring(2);
                    if (res._texts.TryGetValue(k, out var existing)) res._texts[k] = existing + text;
                    else res._texts[k] = text;
                }
            }
            return res;
        }

        /// <summary>4-character tags are packed little-endian (as the original reads them as an int).</summary>
        public static int ParseKey(string s)
        {
            s = s.Trim();
            if (s.Length == 4 && !IsNumber(s)) return Tag(s);
            int.TryParse(s, out int v);
            return v;
        }

        static bool IsNumber(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }

        public static int Tag(string fourChars)
        {
            if (fourChars.Length != 4) throw new ArgumentException("tag must have 4 characters");
            return fourChars[0] | (fourChars[1] << 8) | (fourChars[2] << 16) | (fourChars[3] << 24);
        }

        static long Key(int group, int id) => ((long)(uint)group << 32) | (uint)id;

        public bool Contains(int group, int id) => _texts.ContainsKey(Key(group, id));
        public bool Contains(string group, int id) => Contains(Tag(group), id);

        /// <summary>Raw entry text including all language segments, or null.</summary>
        public string GetRaw(int group, int id) => _texts.TryGetValue(Key(group, id), out var t) ? t : null;

        public string Get(int group, int id) => SelectLanguage(GetRaw(group, id), Lang);
        public string Get(string group, int id) => Get(Tag(group), id);

        /// <summary>Text or a visible placeholder (so missing ids show up during development).</summary>
        public string GetOr(string group, int id, string fallback = null) =>
            Get(group, id) ?? fallback ?? "[" + group + ":" + id + "]";

        public IEnumerable<(int group, int id)> Keys()
        {
            foreach (var k in _texts.Keys) yield return ((int)(k >> 32), (int)(k & 0xFFFFFFFF));
        }

        /// <summary>Picks the segment of <paramref name="lang"/> from "X::text Y::text" strings.</summary>
        /// <summary>
        /// The install's language from <c>misc/sabbel.dat</c> (data-tables §sabbel.dat: the first four bytes, little-endian
        /// index into <see cref="LanguageLetters"/>). Without the file the original starts in German; an index outside the
        /// known languages falls back to English.
        /// </summary>
        public static Language InstallLanguage(byte[] sabbel)
        {
            if (sabbel == null || sabbel.Length < 4) return Language.German;
            int v = sabbel[0] | (sabbel[1] << 8) | (sabbel[2] << 16) | (sabbel[3] << 24);
            return v >= 0 && v <= (int)Language.Brazilian ? (Language)v : Language.English;
        }

        public static string SelectLanguage(string text, Language lang)
        {
            if (text == null) return null;
            if (text.Length < 3 || text[1] != ':' || text[2] != ':') return text;
            string s = Segment(text, LanguageLetters[(int)lang]);
            if (s == null) s = Segment(text, 'E');
            if (s == null) s = Segment(text, 'D');
            return s ?? text;
        }

        static string Segment(string text, char letter)
        {
            // A marker is a language letter followed by "::"; a segment runs until the letter of the
            // next marker (or the end of the text). One trailing space is dropped.
            for (int i = 0; i + 2 < text.Length; i++)
            {
                if (text[i] != letter || text[i + 1] != ':' || text[i + 2] != ':') continue;
                int start = i + 3;
                int end = start;
                while (end < text.Length && !(end + 2 < text.Length && text[end + 1] == ':' && text[end + 2] == ':')) end++;
                if (end + 2 >= text.Length) end = text.Length;
                var s = text.Substring(start, Math.Max(0, end - start));
                if (s.Length > 0 && s[s.Length - 1] == ' ') s = s.Substring(0, s.Length - 1);
                return s;
            }
            return null;
        }
    }
}
