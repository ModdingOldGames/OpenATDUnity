using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenAT.Formats
{
    /// <summary>
    /// Tables from data/*.csv (usually xtRLE packed), parsed with the original's rules
    /// (docs/spec/findings/data-tables.md §0.3–0.5):
    /// lines end at any run of CR/LF/SUB (no blank records); the first line is a header and skipped;
    /// fields split on ';' and 0x08 with runs of delimiters collapsing (strtok semantics: there are no
    /// empty fields). Use <see cref="Atoi"/>/<see cref="Atof"/> for C-style number parsing.
    /// </summary>
    public sealed class CsvTable
    {
        /// <summary>Data rows (header excluded).</summary>
        public readonly List<string[]> Rows = new List<string[]>();
        public string[] Header = new string[0];
        /// <summary>Raw lines (header excluded), for loaders that need "rest of line" columns.</summary>
        public readonly List<string> Lines = new List<string>();

        public static CsvTable Parse(byte[] raw, bool collapseDelimiters = true, bool hasHeader = true)
        {
            var t = new CsvTable();
            var text = TextEncoding.Decode(XtRle.DecodeIfPacked(raw));
            bool first = true;
            foreach (var line in SplitLines(text))
            {
                var cells = collapseDelimiters ? SplitStrtok(line) : line.Split(';');
                if (first && hasHeader) { t.Header = cells; first = false; continue; }
                first = false;
                t.Rows.Add(cells);
                t.Lines.Add(line);
            }
            return t;
        }

        public static IEnumerable<string> SplitLines(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int start = i;
                while (i < text.Length && !IsLineEnd(text[i])) i++;
                if (i > start) yield return text.Substring(start, i - start);
                while (i < text.Length && IsLineEnd(text[i])) i++;
            }
        }

        static bool IsLineEnd(char c) => c == '\r' || c == '\n' || c == (char)0x1A;

        static bool IsDelimiter(char c) => c == ';' || c == (char)0x08;

        /// <summary>Splits like C strtok with the delimiter set ";\b": empty fields disappear.</summary>
        public static string[] SplitStrtok(string line)
        {
            var cells = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && IsDelimiter(line[i])) i++;
                if (i >= line.Length) break;
                int start = i;
                while (i < line.Length && !IsDelimiter(line[i])) i++;
                cells.Add(line.Substring(start, i - start));
            }
            return cells.ToArray();
        }

        /// <summary>
        /// The text after the first <paramref name="column"/> fields of a raw line (strtok rules),
        /// for trailing free-text columns that may contain ';'.
        /// </summary>
        public static string RestOfLine(string line, int column)
        {
            int i = 0;
            for (int c = 0; c < column; c++)
            {
                while (i < line.Length && IsDelimiter(line[i])) i++;
                while (i < line.Length && !IsDelimiter(line[i])) i++;
            }
            while (i < line.Length && IsDelimiter(line[i])) i++;
            return i < line.Length ? line.Substring(i) : "";
        }

        /// <summary>C atoi: optional whitespace and sign, then leading digits; anything else stops (0 if none).</summary>
        public static int Atoi(string s)
        {
            if (s == null) return 0;
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            bool neg = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
            long v = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v < int.MaxValue) v = v * 10 + (s[i++] - '0');
            if (v > int.MaxValue) v = int.MaxValue;
            return (int)(neg ? -v : v);
        }

        public static long Atol64(string s)
        {
            if (s == null) return 0;
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            bool neg = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
            long v = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
            return neg ? -v : v;
        }

        /// <summary>C atof: the longest valid prefix with '.' as decimal point ("1,1" → 1, ".8" → 0.8).</summary>
        public static double Atof(string s)
        {
            if (s == null) return 0;
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i; i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                if (i < s.Length && char.IsDigit(s[i])) { while (i < s.Length && char.IsDigit(s[i])) i++; }
                else i = save;
            }
            var part = s.Substring(start, i - start);
            return double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Cell or "" when the row is short.</summary>
        public static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : "";
    }
}
