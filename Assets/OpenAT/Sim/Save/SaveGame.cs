using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Save
{
    /// <summary>
    /// What the load menu shows for a slot (rooms-and-ui-4 §4.3), readable without the body. The view
    /// builds the info string (std text <c>Misc</c> 1150 / 1151) from these fields.
    /// </summary>
    public sealed class SaveHeader
    {
        public int FormatVersion;
        /// <summary>Program that wrote the file (<see cref="SaveGame.ProgramVersion"/>).</summary>
        public string ProgramVersion;
        /// <summary>Slot name typed by the player (max 20 characters in the original UI).</summary>
        public string Name;
        /// <summary>Mission value (<see cref="Mission"/>).</summary>
        public int Mission;
        /// <summary>Day counter (0 = first day).</summary>
        public int Date;
        /// <summary>Displayed weekday, 0 = Monday.</summary>
        public int Weekday;
        /// <summary>Calendar day of month and month (1..12) of the current day.</summary>
        public int Day, Month, Year;
        /// <summary>Time of day in clock units.</summary>
        public int Time;
        public int LocalPlayer;
        /// <summary>The local player's airline name.</summary>
        public string Airline;
        public string[] Airlines = new string[Player.Count];
        public Owner[] Owners = new Owner[Player.Count];
        public int HumanCount;
        /// <summary>Number of cities of the game data the save was written with.</summary>
        public int CityCount;
        public long TicksPlayed;
        /// <summary>Times this game has been loaded before (the original's restore counter).</summary>
        public int RestoreCount;

        public string ClockText => $"{Time / Core.GameClock.UnitsPerHour,2}:{Time / Core.GameClock.UnitsPerMinute % 60:00}";
    }

    /// <summary>
    /// Save games (sim-core.md §5). The whole <see cref="World"/> object graph is written by reflection
    /// (public and private instance fields), so state added to any domain is saved without extra code;
    /// see <see cref="SaveTypes"/> for what is skipped, <see cref="NotSavedAttribute"/> and
    /// <see cref="ISaveHooks"/> for caches. Loading constructs a fresh <c>World(data, options)</c> (so
    /// the current rules and systems are used) and overlays the saved state onto it.
    ///
    /// File layout: magic "OATS", u16 format version, i32 header length, header (BinaryWriter), then
    /// the body: a tagged object graph (the game options, then the world). Not compatible with the
    /// original's save files (not needed).
    /// </summary>
    public static class SaveGame
    {
        public const int FormatVersion = 1;
        public const string ProgramVersion = "OpenAT 0.1";
        /// <summary>The autosave slot (sim-core.md §5.1): the 12th, hidden on the save page.</summary>
        public const int AutosaveSlot = 11;
        public const int SlotCount = 12;
        static readonly byte[] Magic = { (byte)'O', (byte)'A', (byte)'T', (byte)'S' };

        /// <summary>File name of a slot ("Game0.sav" .. "Game11.sav") inside the save folder chosen by the caller.</summary>
        public static string SlotFileName(int slot) => "Game" + slot + ".sav";

        // ------------------------------------------------------------------ write

        public static void Write(World w, Stream s, string name)
        {
            var header = HeaderOf(w, name);
            var body = new ByteWriter();
            var g = new GraphWriter(body, w);
            g.WriteValue(w.Options);
            g.WriteRoot();

            var hs = new MemoryStream();
            using (var bw = new BinaryWriter(hs, System.Text.Encoding.UTF8, true)) WriteHeader(bw, header);
            var hb = hs.ToArray();

            s.Write(Magic, 0, Magic.Length);
            var tmp = new byte[6];
            tmp[0] = (byte)FormatVersion; tmp[1] = (byte)(FormatVersion >> 8);
            tmp[2] = (byte)hb.Length; tmp[3] = (byte)(hb.Length >> 8); tmp[4] = (byte)(hb.Length >> 16); tmp[5] = (byte)(hb.Length >> 24);
            s.Write(tmp, 0, 6);
            s.Write(hb, 0, hb.Length);
            s.Write(body.Buffer, 0, body.Length);
        }

        /// <summary>Saves into a file (written to a temporary name first, then moved over the old file).</summary>
        public static void WriteFile(World w, string path, string name)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (var f = File.Create(tmp)) Write(w, f, name);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static SaveHeader HeaderOf(World w, string name)
        {
            var c = w.Clock;
            var cal = c.CalendarDate;
            var h = new SaveHeader
            {
                FormatVersion = FormatVersion, ProgramVersion = ProgramVersion, Name = name ?? "",
                Mission = w.Difficulty, Date = c.Date, Weekday = c.DisplayWeekday, Day = cal.Day, Month = cal.Month, Year = cal.Year,
                Time = c.Time, LocalPlayer = w.LocalPlayer, Airline = w.Local.Airline ?? "", CityCount = w.Data?.Cities.Count ?? 0,
                TicksPlayed = c.TicksPlayed, RestoreCount = w.RestoreCount,
            };
            for (int i = 0; i < Player.Count; i++)
            {
                h.Airlines[i] = w.Players[i].Airline ?? "";
                h.Owners[i] = w.Players[i].Owner;
                if (w.Players[i].IsHuman && !w.Players[i].IsOut) h.HumanCount++;
            }
            return h;
        }

        static void WriteHeader(BinaryWriter b, SaveHeader h)
        {
            b.Write(h.ProgramVersion ?? "");
            b.Write(h.Name ?? "");
            b.Write(h.Mission);
            b.Write(h.Date);
            b.Write(h.Weekday);
            b.Write(h.Day); b.Write(h.Month); b.Write(h.Year);
            b.Write(h.Time);
            b.Write(h.LocalPlayer);
            b.Write(h.Airline ?? "");
            b.Write(Player.Count);
            for (int i = 0; i < Player.Count; i++) { b.Write(h.Airlines[i] ?? ""); b.Write((int)h.Owners[i]); }
            b.Write(h.HumanCount);
            b.Write(h.CityCount);
            b.Write(h.TicksPlayed);
            b.Write(h.RestoreCount);
            // New header fields go here (readers stop at the header length).
        }

        // ------------------------------------------------------------------ read

        /// <summary>Reads only the header (the load menu). Throws <see cref="SaveException"/> for a file that is not a save.</summary>
        public static SaveHeader ReadHeader(Stream s) => ParseHeader(ReadHeaderBytes(s, out _));

        /// <summary>Header of a slot file, or null when the file is missing or not a valid save (an "empty" slot).</summary>
        public static SaveHeader TryReadHeaderFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var f = File.OpenRead(path)) return ReadHeader(f);
            }
            catch (Exception e) when (e is SaveException || e is IOException || e is EndOfStreamException || e is UnauthorizedAccessException) { return null; }
        }

        static byte[] ReadHeaderBytes(Stream s, out int version)
        {
            var pre = ReadExactly(s, 10);
            for (int i = 0; i < 4; i++) if (pre[i] != Magic[i]) throw new SaveException("not an OpenAT save game");
            version = pre[4] | pre[5] << 8;
            if (version < 1 || version > FormatVersion) throw new SaveException("unsupported save format version " + version);
            int len = pre[6] | pre[7] << 8 | pre[8] << 16 | pre[9] << 24;
            if (len < 0 || len > 1 << 20) throw new SaveException("bad save header");
            return ReadExactly(s, len);
        }

        static SaveHeader ParseHeader(byte[] hb)
        {
            try
            {
                using (var b = new BinaryReader(new MemoryStream(hb)))
                {
                    var h = new SaveHeader { FormatVersion = FormatVersion };
                    h.ProgramVersion = b.ReadString();
                    h.Name = b.ReadString();
                    h.Mission = b.ReadInt32();
                    h.Date = b.ReadInt32();
                    h.Weekday = b.ReadInt32();
                    h.Day = b.ReadInt32(); h.Month = b.ReadInt32(); h.Year = b.ReadInt32();
                    h.Time = b.ReadInt32();
                    h.LocalPlayer = b.ReadInt32();
                    h.Airline = b.ReadString();
                    int n = b.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        string a = b.ReadString();
                        var o = (Owner)b.ReadInt32();
                        if (i < Player.Count) { h.Airlines[i] = a; h.Owners[i] = o; }
                    }
                    h.HumanCount = b.ReadInt32();
                    h.CityCount = b.ReadInt32();
                    h.TicksPlayed = b.ReadInt64();
                    h.RestoreCount = b.ReadInt32();
                    return h;
                }
            }
            catch (EndOfStreamException e) { throw new SaveException("bad save header", e); }
        }

        /// <summary>Loads a game. Content (cities, plane types, ...) comes from <paramref name="data"/>; a world with a hall needs the hall content attached to it (<see cref="Hall.HallContent"/>).</summary>
        public static World Read(Stream s, GameData data) => Read(s, data, out _);

        /// <param name="warnings">Saved state that could not be restored (types or fields that no longer exist, changed field types).</param>
        public static World Read(Stream s, GameData data, out List<string> warnings)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ParseHeader(ReadHeaderBytes(s, out _));
            var body = ReadToEnd(s);

            // Options first: the world is constructed with them (its rules and systems may depend on them).
            var r = new GraphReader(new ByteReader(body, 0, body.Length), data);
            var options = r.ReadDetached(typeof(GameOptions)) as GameOptions ?? new GameOptions();
            var w = new World(data, options);
            r.AttachRoot(w);
            r.ReadRoot();
            foreach (var h in r.Hooks) h.OnAfterLoad(w);
            warnings = r.Warnings;
            return w;
        }

        public static World ReadFile(string path, GameData data)
        {
            using (var f = File.OpenRead(path)) return Read(f, data);
        }

        // ------------------------------------------------------------------ state hash

        /// <summary>
        /// Deep hash of the saved state (and, with <paramref name="includeMachinery"/>, of the rules' and
        /// systems' fields): equal hashes mean the worlds will save identically. Dictionaries and sets
        /// are hashed in key order; the restore counter is ignored. For tests and regression diffs.
        /// </summary>
        public static string StateHash(World w, bool includeMachinery = false)
        {
            // The restore counter is left out: a load changes it, and a loaded game must hash like the original.
            int restores = w.RestoreCount;
            w.RestoreCount = 0;
            try { return Hash(w, includeMachinery); }
            finally { w.RestoreCount = restores; }
        }

        static string Hash(World w, bool includeMachinery)
        {
            if (includeMachinery) _ = w.Systems.Systems; // build the pipeline's sorted cache on both sides
            var body = new ByteWriter();
            var g = new GraphWriter(body, w, includeMachinery, canonical: true);
            g.WriteValue(w.Options);
            g.WriteRoot();
            if (includeMachinery)
            {
                g.WriteValue(w.Rules);
                g.WriteValue(w.Systems);
            }
            ulong h = 14695981039346656037UL;
            var buf = body.Buffer;
            for (int i = 0; i < body.Length; i++) { h ^= buf[i]; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// <summary>Size and object count of a save of <paramref name="w"/> (diagnostics).</summary>
        public static (int Bytes, int Objects, int Types) Measure(World w)
        {
            var body = new ByteWriter();
            var g = new GraphWriter(body, w);
            g.WriteValue(w.Options);
            g.WriteRoot();
            return (body.Length, g.ObjectCount, g.TypeCount);
        }

        /// <summary>Text dump of the saved state, one "path = value" line per field (diff two worlds whose hashes differ).</summary>
        public static string Dump(World w, bool includeMachinery = false)
        {
            if (includeMachinery) _ = w.Systems.Systems;
            return StateDump.Of(w, includeMachinery);
        }

        /// <summary>
        /// Lint for state types (tests): saved classes without a parameterless constructor (a private one is
        /// enough) are created without running their field initialisers, so their unsaved fields (delegates,
        /// [NotSaved], caches) would be null/0 after loading unless an <see cref="ISaveHooks"/> restores them.
        /// Returns one line per such type and field.
        /// </summary>
        public static List<string> Audit(World w)
        {
            var g = new GraphWriter(new ByteWriter(), w);
            g.WriteValue(w.Options);
            g.WriteRoot();
            var issues = new List<string>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var t in g.Types)
            {
                var st = SaveTypes.Get(t, false);
                // The world itself is never created by the loader (the saved state is overlaid onto a fresh one).
                if (st.Kind != TypeKind.Object || st.DefaultCtor != null || t == typeof(World)) continue;
                foreach (var f in t.GetFields(flags))
                    if (SaveTypes.IsSkippedField(f, false)) issues.Add(TypeNames.Of(t) + "." + f.Name);
            }
            issues.Sort(string.CompareOrdinal);
            return issues;
        }

        // ------------------------------------------------------------------ io helpers

        static byte[] ReadExactly(Stream s, int n)
        {
            var b = new byte[n];
            int o = 0;
            while (o < n)
            {
                int k = s.Read(b, o, n - o);
                if (k <= 0) throw new SaveException("save file truncated");
                o += k;
            }
            return b;
        }

        static byte[] ReadToEnd(Stream s)
        {
            var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
