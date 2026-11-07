using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Formats;

namespace OpenAT.Sim.Hall
{
    /// <summary>One row of data/brick.csv (airport-and-people.md §2.1, data-tables.md §1.4).</summary>
    public sealed class BrickDef
    {
        public int Id;
        public string Description;
        /// <summary>0 colour-keyed, 1 opaque, 2 glass (50 % blend when the transparency option is on).</summary>
        public int NonTrans;
        /// <summary>0 loop, 1 one-shot, 2 shutter, 90 duration-weighted frames.</summary>
        public int Triggered;
        public int Layer, AnimSpeed, FloorOffset, BaseX, BaseY, GridX, GridY, MinY, MaxY, ObstacleType;
        /// <summary>Frame graphic names in the season brick library.</summary>
        public string[] Frames = Array.Empty<string>();
        public int[] WaitTimes = Array.Empty<int>();
        public int WaitSum;
        /// <summary>Size of the first frame (from the brick library; 0 when missing).</summary>
        public int Width, Height;

        public bool IsRune => Id >= 2000 && Id < 3000;

        /// <summary>Frame index for a looping/weighted brick at the given ticker time (§8.3).</summary>
        public int LoopFrame(long ticker, int phase = -1)
        {
            int n = Frames.Length;
            if (n == 0) return 0;
            if (AnimSpeed == 0) return phase >= 0 && phase < 100 ? Math.Min(phase, n - 1) : 0;
            int f = phase;
            if (Triggered == 0)
            {
                if (phase == -1) f = (int)(ticker / AnimSpeed % n);
                else if (phase >= 100) f = (int)((phase - 100 + ticker / AnimSpeed) % n);
            }
            if (Triggered == 90 && WaitSum > 0)
            {
                long target = ticker / AnimSpeed % WaitSum;
                for (int c = 0; c < n; c++)
                {
                    target -= WaitTimes[c];
                    if (target <= 0) { f = c; break; }
                }
            }
            if (f < 0) f = 0;
            return f % n;
        }
    }

    /// <summary>One person type of data/clan.csv (airport-and-people.md §6.1). Indexed by album slot.</summary>
    public sealed class ClanDef
    {
        public const int Slots = 14;
        /// <summary>Internal phase slots.</summary>
        public const int Walk = 0, Gimmick = 4, Wave = 5, SitN = 6, SitS = 7, Stand = 8, WaitGimmick = 9, Run = 10;

        /// <summary>Album slot = pool file number (clan&lt;Slot&gt;.pol).</summary>
        public int Slot;
        /// <summary>Row index in clan.csv (0-based record).</summary>
        public int Record;
        public int Type, Group, UpdateNow, Wkeit, WalkSpeed, Faktor, HasSuitcase, GimmickArt1, GimmickArt2;
        public int OffsetX, OffsetY, GimmickOffsetX, GimmickOffsetY, ShadowOffsetX, ShadowOffsetY, SkelettOffsetX, SkelettOffsetY;
        public string Palette;
        public string Comment;
        public int FloorOffset;
        /// <summary>Frame names per internal slot: person (clan/clang/clanw pools), shadow (glstd), skeleton (skelett.pol).</summary>
        public readonly string[][] Phases = new string[Slots][];
        public readonly string[][] Shadows = new string[Slots][];
        public readonly string[][] Skeleton = new string[Slots][];

        public int FrameCount(int slot) => Phases[slot]?.Length ?? 0;
        public bool IsPlayer => ClanType.IsPlayer(Type);
    }

    /// <summary>One hall segment file (misc/&lt;Name&gt;&lt;NNN&gt;.dat, §2.2).</summary>
    public sealed class SegmentFile
    {
        public struct Record { public int BrickId; public int X, Y; public byte Par; public bool Used; }
        public string Name;
        public readonly List<Record> Records = new List<Record>();

        public static SegmentFile Parse(byte[] d, string name)
        {
            var f = new SegmentFile { Name = name };
            int count = BitConverter.ToInt32(d, 0);
            int pos = 8;
            var recs = new Record[count];
            for (int i = 0; i < count; i++, pos += 13)
                recs[i] = new Record { BrickId = BitConverter.ToInt32(d, pos) - 0x10000000, X = BitConverter.ToInt32(d, pos + 4), Y = BitConverter.ToInt32(d, pos + 8), Par = d[pos + 12] };
            // Album trailer: lastId, id buffer (count, offset, ids). Id 0 = empty slot.
            pos += 4;
            int ids = pos + 8 <= d.Length ? BitConverter.ToInt32(d, pos) : 0;
            pos += 8;
            for (int i = 0; i < count; i++)
            {
                bool used = i >= ids || pos + 4 * i + 4 > d.Length || BitConverter.ToUInt32(d, pos + 4 * i) != 0;
                recs[i].Used = used;
                f.Records.Add(recs[i]);
            }
            return f;
        }
    }

    /// <summary>
    /// Static hall content read from the install: brick and clan tables, brick sizes, segment files.
    /// No game state; one instance can serve many worlds.
    /// </summary>
    public sealed class HallData
    {
        /// <summary>Segment names in hall order (§2.2 "File naming and selection").</summary>
        public static readonly string[] SegmentNames = { "Left", "Check", "Ofice", "Entry", "Shops", "Cafe", "Secur", "Suit", "WaitZ", "Right" };
        /// <summary>Difficulty used for the free game's layout files.</summary>
        public const int FreeGameMap = 7;
        public const int MaxClans = 90;

        public readonly Dictionary<int, BrickDef> Bricks = new Dictionary<int, BrickDef>();
        /// <summary>Clans by album slot (null for unused slots).</summary>
        public readonly ClanDef[] Clans = new ClanDef[MaxClans];
        /// <summary>Lookup of segment files by lower-case file name (e.g. "left701.dat").</summary>
        readonly Dictionary<string, SegmentFile> _segments = new Dictionary<string, SegmentFile>(StringComparer.OrdinalIgnoreCase);
        readonly Func<string, byte[]> _readMisc;

        public HallData(Func<string, byte[]> readMisc) => _readMisc = readMisc;

        public BrickDef Brick(int id) => Bricks.TryGetValue(id, out var b) ? b : null;

        /// <summary>Loads everything from an install. <paramref name="season"/> selects glbrick&lt;n&gt;.gli for the brick sizes.</summary>
        public static HallData Load(GameInstall install, int season = 0)
        {
            var h = new HallData(name => install.Find("misc/" + name) is string p ? File.ReadAllBytes(p) : null);
            h.LoadBricks(CsvTable.Parse(install.ReadAllBytes("data/brick.csv")));
            h.LoadClans(CsvTable.Parse(install.ReadAllBytes("data/clan.csv")));
            string gli = install.Find("gli/glbrick" + season + ".gli") ?? install.Find("gli/glbrick0.gli");
            if (gli != null)
            {
                var lib = GliLibrary.Load(gli);
                h.ApplySizes(name => lib.GetEntry(name) is GliLibrary.Entry e ? (e.Width, e.Height) : (0, 0));
            }
            return h;
        }

        public void LoadBricks(CsvTable t)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                if (r.Length < 15) continue;
                var b = new BrickDef
                {
                    Id = CsvTable.Atoi(r[0]),
                    Description = r[1],
                    NonTrans = CsvTable.Atoi(r[3]),
                    Triggered = CsvTable.Atoi(r[4]),
                    Layer = CsvTable.Atoi(r[5]),
                    AnimSpeed = CsvTable.Atoi(r[6]),
                    FloorOffset = CsvTable.Atoi(r[7]),
                    BaseX = CsvTable.Atoi(r[8]),
                    BaseY = CsvTable.Atoi(r[9]),
                    GridX = CsvTable.Atoi(r[10]),
                    GridY = CsvTable.Atoi(r[11]),
                    MinY = CsvTable.Atoi(r[12]),
                    MaxY = CsvTable.Atoi(r[13]),
                    ObstacleType = CsvTable.Atoi(r[14]),
                };
                // The frame list is the rest of the line, split on spaces; "NAME:n" weights a frame.
                var rest = CsvTable.RestOfLine(t.Lines[i], 15);
                var names = new List<string>();
                var waits = new List<int>();
                foreach (var tok in rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var s = tok.Trim('"', ';', '\b');
                    if (s.Length == 0) continue;
                    int colon = s.IndexOf(':');
                    if (colon >= 0)
                    {
                        b.Triggered = 90;
                        waits.Add(CsvTable.Atoi(s.Substring(colon + 1)));
                        s = s.Substring(0, colon);
                    }
                    else waits.Add(1);
                    names.Add(s);
                }
                b.Frames = names.ToArray();
                b.WaitTimes = waits.ToArray();
                foreach (var w in b.WaitTimes) b.WaitSum += w;
                Bricks[b.Id] = b;
            }
        }

        /// <summary>Sets brick sizes from a name → (w,h) lookup (first frame).</summary>
        public void ApplySizes(Func<string, (int w, int h)> size)
        {
            foreach (var b in Bricks.Values)
                if (b.Frames.Length > 0) (b.Width, b.Height) = size(b.Frames[0]);
        }

        public void LoadClans(CsvTable t)
        {
            int rows = t.Rows.Count / 3;
            for (int k = 0; k < rows && k < MaxClans - 1; k++)
            {
                var a = t.Rows[3 * k];
                var sh = t.Rows[3 * k + 1];
                var sk = t.Rows[3 * k + 2];
                var c = new ClanDef
                {
                    Record = k,
                    Slot = MaxClans - 1 - k,
                    Type = CsvTable.Atoi(Cell(a, 0)),
                    Group = CsvTable.Atoi(Cell(a, 1)),
                    Comment = Cell(a, 2),
                    UpdateNow = CsvTable.Atoi(Cell(a, 3)),
                    Wkeit = CsvTable.Atoi(Cell(a, 4)) & 0xFF,
                    WalkSpeed = CsvTable.Atoi(Cell(a, 5)),
                    Faktor = Math.Max(1, CsvTable.Atoi(Cell(a, 6))),
                    HasSuitcase = CsvTable.Atoi(Cell(a, 7)),
                    GimmickArt1 = CsvTable.Atoi(Cell(a, 8)),
                    GimmickArt2 = CsvTable.Atoi(Cell(a, 9)),
                    OffsetX = CsvTable.Atoi(Cell(a, 10)),
                    OffsetY = CsvTable.Atoi(Cell(a, 11)),
                    Palette = Cell(a, 12),
                    GimmickOffsetX = CsvTable.Atoi(Cell(sh, 8)),
                    GimmickOffsetY = CsvTable.Atoi(Cell(sh, 9)),
                    ShadowOffsetX = CsvTable.Atoi(Cell(sh, 10)),
                    ShadowOffsetY = CsvTable.Atoi(Cell(sh, 11)),
                    SkelettOffsetX = CsvTable.Atoi(Cell(sk, 10)),
                    SkelettOffsetY = CsvTable.Atoi(Cell(sk, 11)),
                };
                c.FloorOffset = c.Group == 70 ? -15 : 0;
                ReadPhaseLists(a, c.Phases);
                ReadPhaseLists(sh, c.Shadows);
                ReadPhaseLists(sk, c.Skeleton);
                Clans[c.Slot] = c;
            }
        }

        static string Cell(string[] r, int i) => CsvTable.Cell(r, i);

        /// <summary>The 14 lists start at column 13: walk/run use (label, name, count), stand uses one
        /// explicit list, the others (name, count). The csv order is remapped to the internal slots.</summary>
        static void ReadPhaseLists(string[] row, string[][] dst)
        {
            int col = 13;
            for (int c = 0; c < 14; c++)
            {
                string spec; int count;
                if (c == 12) { spec = Cell(row, col++); count = -1; }
                else
                {
                    if (c < 8) col++;
                    spec = Cell(row, col++);
                    count = CsvTable.Atoi(Cell(row, col++));
                }
                int slot = c < 4 ? c : c < 8 ? 10 + c - 4 : c - 4;
                dst[slot] = Expand(spec, count);
            }
        }

        static string[] Expand(string spec, int count)
        {
            if (string.IsNullOrEmpty(spec)) return Array.Empty<string>();
            if (count == -1) return spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return HLinePool.ExpandSequence(spec.Trim(), Math.Max(0, count)).ToArray();
        }

        // ---------------------------------------------------------------- segments

        /// <summary>Segment file for hall <paramref name="hall"/> (0..9) at a level, falling back to lower
        /// difficulties until a file exists (§2.2). Returns null when none exists.</summary>
        public SegmentFile Segment(int hall, int difficulty, int level)
        {
            int d = difficulty < 0 ? FreeGameMap : difficulty;
            for (; d >= 0; d--)
            {
                string file = SegmentNames[hall] + (100 * d + level).ToString("000") + ".dat";
                if (_segments.TryGetValue(file, out var s)) return s;
                var bytes = _readMisc(file);
                if (bytes == null) continue;
                s = SegmentFile.Parse(bytes, file);
                _segments[file] = s;
                return s;
            }
            return null;
        }
    }
}
