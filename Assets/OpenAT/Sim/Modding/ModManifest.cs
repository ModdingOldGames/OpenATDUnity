using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Sim.Modding
{
    /// <summary>A dependency of a mod: another mod's id and an optional minimum version.</summary>
    public sealed class ModDependency
    {
        public string Id;
        public string MinVersion;
        public bool Optional;
        public override string ToString() => Id + (MinVersion != null ? ">=" + MinVersion : "") + (Optional ? " (optional)" : "");
    }

    /// <summary>
    /// <c>Mods/&lt;ModName&gt;/mod.json</c> (docs/MODDING.md):
    /// <code>
    /// { "id": "lowfuelprices", "name": "Low fuel prices", "version": "1.0.0", "author": "...",
    ///   "description": "...", "dependencies": ["otherid", "x>=1.2", { "id": "y", "optional": true }],
    ///   "loadOrder": 0, "enabled": true, "assemblies": ["MyMod.dll"] }
    /// </code>
    /// </summary>
    public sealed class ModManifest
    {
        public const string FileName = "mod.json";

        public string Id;
        public string Name;
        public string Version = "0.0.0";
        public string Author;
        public string Description;
        public readonly List<ModDependency> Dependencies = new List<ModDependency>();
        /// <summary>Lower loads first (after dependencies); ties by id.</summary>
        public int LoadOrder;
        public bool Enabled = true;
        /// <summary>Assemblies to load (relative to the mod folder); null = every *.dll in the folder.</summary>
        public List<string> Assemblies;
        /// <summary>The mod folder.</summary>
        public string Directory;

        public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;
        public override string ToString() => Id + " " + Version;

        public static ModManifest Read(string folder)
        {
            string path = Path.Combine(folder, FileName);
            var m = Parse(File.ReadAllText(path));
            m.Directory = folder;
            if (string.IsNullOrEmpty(m.Id)) m.Id = Path.GetFileName(folder.TrimEnd('/', '\\'));
            return m;
        }

        public static ModManifest Parse(string json)
        {
            var o = Json.ParseObject(json);
            var m = new ModManifest
            {
                Id = o.Str("id"),
                Name = o.Str("name"),
                Version = o.Str("version", "0.0.0"),
                Author = o.Str("author"),
                Description = o.Str("description"),
                LoadOrder = o.Int("loadOrder"),
                Enabled = o.Bool("enabled", true),
            };
            if (m.Id != null) m.Id = m.Id.Trim();
            if (m.Id != null && !ValidId(m.Id)) throw new FormatException("mod.json: id '" + m.Id + "' may only contain letters, digits, '.', '-' and '_'");
            if (o.Arr("dependencies") is List<object> deps)
                foreach (var d in deps)
                {
                    if (d is string s) m.Dependencies.Add(ParseDependency(s));
                    else if (d is JsonObject dj)
                    {
                        var dep = ParseDependency(dj.Str("id") ?? throw new FormatException("mod.json: dependency without id"));
                        dep.MinVersion = dj.Str("version", dep.MinVersion)?.TrimStart('>', '=', ' ');
                        dep.Optional = dj.Bool("optional");
                        m.Dependencies.Add(dep);
                    }
                }
            if (o.Arr("assemblies") is List<object> asm)
            {
                m.Assemblies = new List<string>();
                foreach (var a in asm) if (a is string s) m.Assemblies.Add(s);
            }
            return m;
        }

        static ModDependency ParseDependency(string s)
        {
            s = s.Trim();
            int i = s.IndexOf(">=", StringComparison.Ordinal);
            return i < 0 ? new ModDependency { Id = s } : new ModDependency { Id = s.Substring(0, i).Trim(), MinVersion = s.Substring(i + 2).Trim() };
        }

        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return false;
            return true;
        }

        /// <summary>Compares dotted versions numerically ("1.10" &gt; "1.9"); non-numeric parts compare ordinally.</summary>
        public static int CompareVersions(string a, string b)
        {
            var x = (a ?? "0").Split('.', '-', '+');
            var y = (b ?? "0").Split('.', '-', '+');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                string p = i < x.Length ? x[i] : "0", q = i < y.Length ? y[i] : "0";
                int c = int.TryParse(p, out int pi) && int.TryParse(q, out int qi) ? pi.CompareTo(qi) : string.CompareOrdinal(p, q);
                if (c != 0) return c;
            }
            return 0;
        }
    }
}
