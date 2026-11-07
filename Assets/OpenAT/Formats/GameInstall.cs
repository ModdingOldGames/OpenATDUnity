using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Formats
{
    /// <summary>
    /// The user's own Airline Tycoon Deluxe installation (read-only). All original assets are read
    /// from here at run time; nothing is ever copied into the OpenAT build.
    /// File lookup is case-insensitive so the same install works on case-sensitive file systems.
    /// </summary>
    public sealed class GameInstall
    {
        public const string EnvVar = "OPENAT_GAME_DIR";

        public static readonly string[] KnownLocations =
        {
            @"C:\Program Files (x86)\GOG Galaxy\Games\Airline Tycoon Deluxe",
            @"C:\GOG Games\Airline Tycoon Deluxe",
            @"C:\Program Files (x86)\GOG.com\Airline Tycoon Deluxe",
            @"C:\Program Files (x86)\Steam\steamapps\common\Airline Tycoon Deluxe",
        };

        /// <summary>Well-known sub folders of the install.</summary>
        public static class Folders
        {
            public const string Data = "data";
            public const string Gli = "gli";
            public const string Room = "room";
            public const string Misc = "misc";
            public const string Intro = "intro";
            public const string Video = "video";
            public const string Sound = "SOUND";
            public const string Voice = "VOICE";
        }

        public string Root { get; }

        readonly Dictionary<string, string> _index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        GameInstall(string root)
        {
            Root = root;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Normalize(file.Substring(root.Length).TrimStart('\\', '/'));
                _index[rel] = file;
            }
        }

        /// <summary>True when <paramref name="dir"/> looks like an ATD install.</summary>
        public static bool IsValid(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "At.exe")) || Directory.Exists(Path.Combine(dir, "gli")) && Directory.Exists(Path.Combine(dir, "room"));
        }

        public static GameInstall Open(string dir)
        {
            if (!IsValid(dir)) throw new DirectoryNotFoundException("Not an Airline Tycoon Deluxe install: " + dir);
            return new GameInstall(Path.GetFullPath(dir));
        }

        /// <summary>Env var, then known locations. Returns null when none is found.</summary>
        public static GameInstall TryLocate(string preferred = null)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(preferred)) candidates.Add(preferred);
            var env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env)) candidates.Add(env);
            candidates.AddRange(KnownLocations);
            foreach (var c in candidates)
                if (IsValid(c)) return new GameInstall(Path.GetFullPath(c));
            return null;
        }

        static string Normalize(string rel) => rel.Replace('\\', '/');

        /// <summary>Full path of a file given relative to the install root, or null.</summary>
        public string Find(string relativePath)
        {
            return _index.TryGetValue(Normalize(relativePath), out var p) ? p : null;
        }

        public string Find(string folder, string file) => Find(folder + "/" + file);

        public bool Exists(string relativePath) => Find(relativePath) != null;

        public string Require(string relativePath)
        {
            return Find(relativePath) ?? throw new FileNotFoundException("Missing game file: " + relativePath);
        }

        public byte[] ReadAllBytes(string relativePath) => File.ReadAllBytes(Require(relativePath));

        /// <summary>Reads a file, transparently unpacking xtRLE containers.</summary>
        public byte[] ReadDecoded(string relativePath) => XtRle.DecodeIfPacked(ReadAllBytes(relativePath));

        public IEnumerable<string> List(string folder, string extension = null)
        {
            var prefix = Normalize(folder).TrimEnd('/') + "/";
            foreach (var key in _index.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (key.IndexOf('/', prefix.Length) >= 0) continue;
                if (extension != null && !key.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                yield return key;
            }
        }
    }
}
