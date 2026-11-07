using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Sim.Data
{
    /// <summary>
    /// The player's folder of designer planes (planes-and-flights.md §5.1, rooms-and-ui-7 §3/§6): one
    /// <c>.plane</c> file per design in the original file format, the editor's work-in-progress is
    /// <c>data.plane</c>, new designs get free numbered names <c>data&lt;N&gt;.plane</c>. Designs are
    /// shared files, not game state. File order (prev/next, buy list) is case-insensitive alphabetical
    /// (OQ-10: the original takes the directory listing order, which is alphabetical on NTFS).
    /// </summary>
    public sealed class PlaneDesignLibrary
    {
        public const string Extension = ".plane";
        public const string WorkFile = "data" + Extension;

        public readonly string Directory;
        public readonly PlaneDesignCatalog Catalog;

        public PlaneDesignLibrary(string directory, PlaneDesignCatalog catalog)
        {
            Directory = directory; Catalog = catalog;
        }

        public void EnsureDirectory()
        {
            try { System.IO.Directory.CreateDirectory(Directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public string PathOf(string fileName) => Path.Combine(Directory, fileName);

        /// <summary>All design file names (no directory), sorted.</summary>
        public List<string> Files()
        {
            var l = new List<string>();
            if (!System.IO.Directory.Exists(Directory)) return l;
            foreach (var f in System.IO.Directory.GetFiles(Directory, "*" + Extension))
            {
                var n = Path.GetFileName(f);
                if (n.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) l.Add(n);
            }
            l.Sort((a, b) =>
            {
                int c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a, b);
            });
            return l;
        }

        public bool Exists(string fileName) => File.Exists(PathOf(fileName));

        /// <summary>Loads a design; an empty or unreadable file gives an empty design (null name = unchanged default).</summary>
        public PlaneDesign Load(string fileName)
        {
            try
            {
                var data = File.ReadAllBytes(PathOf(fileName));
                if (data.Length == 0) return new PlaneDesign();
                var d = PlaneDesign.ReadPln(data);
                // Drop parts the catalogue does not know (damaged or foreign files).
                d.Parts.RemoveAll(p => Catalog.IndexOfPart(p.ShortName) < 0 || p.RelationIndex < 0 || p.RelationIndex >= Catalog.Relations.Count);
                return d;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is IndexOutOfRangeException)
            {
                return null;
            }
        }

        public bool Save(string fileName, PlaneDesign d)
        {
            try
            {
                EnsureDirectory();
                File.WriteAllBytes(PathOf(fileName), d.WritePln(Catalog));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        public void Delete(string fileName)
        {
            try { File.Delete(PathOf(fileName)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>The file <paramref name="add"/> steps after <paramref name="current"/> (wrapping); the first file when
        /// <paramref name="current"/> is not in the folder; null when the folder is empty ("GetMatchingNext").</summary>
        public string Next(string current, int add)
        {
            var l = Files();
            if (l.Count == 0) return null;
            for (int i = 0; i < l.Count; i++)
                if (string.Equals(l[i], current, StringComparison.OrdinalIgnoreCase))
                    return l[((i + add) % l.Count + l.Count) % l.Count];
            return l[0];
        }

        /// <summary>First free numbered work file name: data.plane, data1.plane, data2.plane, ... ("CreateNumeratedFreeFilename").</summary>
        public string NewFileName()
        {
            for (int c = 0; c < 100000; c++)
            {
                string n = "data" + (c == 0 ? "" : c.ToString()) + Extension;
                if (!Exists(n)) return n;
            }
            return null;
        }

        /// <summary>Number part of a numbered file name ("data12.plane" → "12"), used for the default design name.</summary>
        public static string NumberPart(string fileName)
        {
            if (fileName == null) return "";
            var n = Path.GetFileNameWithoutExtension(fileName);
            return n.StartsWith("data", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : "";
        }

        /// <summary>Buildable designs for the designer's list (MENU_BUYXPLANE), in file order.</summary>
        public List<(string file, PlaneDesign design)> Buildable()
        {
            var l = new List<(string, PlaneDesign)>();
            foreach (var f in Files())
            {
                var d = Load(f);
                if (d != null && d.IsBuildable(Catalog)) l.Add((f, d));
            }
            return l;
        }
    }
}
