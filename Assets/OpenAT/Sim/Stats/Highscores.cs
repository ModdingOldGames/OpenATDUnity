using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Stats
{
    /// <summary>One row of the highscore table: player name, the session it belongs to, the company value.</summary>
    public sealed class HighscoreEntry
    {
        public string Name = EmptyName;
        public uint Session;
        public long Score;

        /// <summary>Name shown in an unused row.</summary>
        public const string EmptyName = "---";

        public bool IsEmpty => Score == 0 && Session == 0;
    }

    /// <summary>
    /// The six-entry highscore table (sim-core.md §5.4, rooms-and-ui-4 §3.8). It lives outside saves; the
    /// view keeps it in the save folder as plain text (the original obfuscated it; we do not).
    /// <para>An offer from a session that already has a row only raises that row (when better) and re-sorts;
    /// a new session is inserted at the first row it beats, pushing the last row out.</para>
    /// </summary>
    public sealed class HighscoreTable
    {
        public const int Size = 6;

        readonly HighscoreEntry[] _rows = new HighscoreEntry[Size];

        public HighscoreTable()
        {
            for (int i = 0; i < Size; i++) _rows[i] = new HighscoreEntry();
        }

        public HighscoreEntry this[int rank] => _rows[rank];

        /// <summary>Offers a score. Returns true when the table changed (and should be written).</summary>
        public bool Offer(string name, uint session, long score)
        {
            name = Clean(name);
            for (int c = 0; c < Size; c++)
            {
                if (_rows[c].Session != session || _rows[c].IsEmpty) continue;
                if (score <= _rows[c].Score) return false;
                _rows[c].Score = score;
                _rows[c].Name = name;
                // Move the raised row up to its place (the others keep their order).
                for (int k = c; k > 0 && _rows[k].Score > _rows[k - 1].Score; k--)
                {
                    var t = _rows[k]; _rows[k] = _rows[k - 1]; _rows[k - 1] = t;
                }
                return true;
            }
            for (int c = 0; c < Size; c++)
            {
                if (score <= _rows[c].Score) continue;
                for (int d = Size - 1; d > c; d--) _rows[d] = _rows[d - 1];
                _rows[c] = new HighscoreEntry { Name = name, Session = session, Score = score };
                return true;
            }
            return false;
        }

        static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return HighscoreEntry.EmptyName;
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name) sb.Append(ch == ';' || ch == '\n' || ch == '\r' ? ' ' : ch);
            return sb.ToString().Trim();
        }

        /// <summary>Six lines <c>name;session;score</c>.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var r in _rows)
                sb.Append(r.Name).Append(';').Append(r.Session.ToString(CultureInfo.InvariantCulture)).Append(';')
                  .Append(r.Score.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads <see cref="Serialize"/> output; unreadable lines become empty rows.</summary>
        public static HighscoreTable Parse(string text)
        {
            var t = new HighscoreTable();
            if (string.IsNullOrEmpty(text)) return t;
            var lines = text.Replace("\r", "").Split('\n');
            var rows = new List<HighscoreEntry>();
            foreach (var line in lines)
            {
                if (rows.Count == Size) break;
                if (line.Length == 0) continue;
                int b = line.LastIndexOf(';');
                int a = b > 0 ? line.LastIndexOf(';', b - 1) : -1;
                var e = new HighscoreEntry();
                if (a > 0 && uint.TryParse(line.Substring(a + 1, b - a - 1), NumberStyles.None, CultureInfo.InvariantCulture, out uint s)
                    && long.TryParse(line.Substring(b + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) && v > 0)
                {
                    e.Name = line.Substring(0, a); e.Session = s; e.Score = v;
                }
                rows.Add(e);
            }
            // Keep the table sorted even if the file was edited by hand.
            for (int i = 1; i < rows.Count; i++)
                for (int k = i; k > 0 && rows[k].Score > rows[k - 1].Score; k--) { var x = rows[k]; rows[k] = rows[k - 1]; rows[k - 1] = x; }
            for (int i = 0; i < rows.Count; i++) t._rows[i] = rows[i];
            return t;
        }
    }

    /// <summary>
    /// A human's company value was refreshed (evening, statistics, briefing) in a session that did not cheat:
    /// the view offers it to the highscore table.
    /// </summary>
    public sealed class HighscoreOffered : ISimEvent
    {
        public Player Player;
        public uint Session;
        public long Score;
    }
}
