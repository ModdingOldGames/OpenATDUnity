using System;
using System.IO;
using System.Linq;
using OpenAT.Formats;

namespace OpenAT.Cli
{
    /// <summary>
    /// Developer tool. Output of the dump commands is decoded game data: write it to a local scratch
    /// folder only, never into the repository.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0) { Usage(); return 1; }
            try
            {
                switch (args[0])
                {
                    case "decode": Decode(args[1], args[2]); return 0;
                    case "gli": Gli(args[1], args.Length > 2 ? args[2] : null); return 0;
                    case "font": Font(args[1], args[2]); return 0;
                    case "res": Res(args[1], args.Length > 2 ? args[2] : null); return 0;
                    case "csv": Csv(args[1], args.Length > 2 ? int.Parse(args[2]) : 5); return 0;
                    case "lbm": { var im = IlbmImage.Parse(File.ReadAllBytes(args[1])); Console.WriteLine($"{im.Width}x{im.Height}"); Png.Write(args[2], im.Width, im.Height, im.ToRgba()); return 0; }
                    case "scan": Scan(args.Length > 1 ? args[1] : null); return 0;
                    case "sim": return SimCommand.Run(args);
                    default: Usage(); return 1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 2;
            }
        }

        static void Usage()
        {
            Console.WriteLine("openat decode <in> <out>         unpack xtRLE file");
            Console.WriteLine("openat gli <file> [outdir]       list (and dump PNGs of) a GLIB library");
            Console.WriteLine("openat font <file.mcf> <out.png> dump font glyph strip");
            Console.WriteLine("openat res <file.res> [group]    list text resource entries");
            Console.WriteLine("openat csv <file.csv> [rows]     show first rows of a table");
            Console.WriteLine("openat lbm <file.lbm> <out.png>     dump IFF image");
            Console.WriteLine("openat scan [installDir]         parse every known asset, report failures");
            Console.WriteLine("openat sim --days N --mission M [--seed S] [--hall] [--trace]");
            Console.WriteLine("                                 headless game, 4 computer airlines, daily table");
            Console.WriteLine("     [--save-at D --save FILE]   ... and save the game after day D");
            Console.WriteLine("openat sim --load FILE --days N  continue a saved game for N days (prints the state hash)");
            Console.WriteLine("openat sim ... --mods DIR  load the mods in DIR (repeatable; also OPENAT_MODS_DIR, --no-mods ignores it)");
        }

        static void Decode(string input, string output) => File.WriteAllBytes(output, XtRle.Decode(File.ReadAllBytes(input)));

        static void Gli(string file, string outDir)
        {
            var lib = GliLibrary.Load(file);
            if (outDir != null) Directory.CreateDirectory(outDir);
            foreach (var e in lib.Entries)
            {
                Console.WriteLine($"{e.Name,-8} {e.Width,4}x{e.Height,-4} pitch {e.Pitch} bpp {e.BitDepth}");
                if (outDir == null) continue;
                var b = lib.Decode(e);
                Png.Write(Path.Combine(outDir, e.Name + ".png"), b.Width, b.Height, b.ToRgba());
            }
        }

        static void Font(string file, string png)
        {
            var f = McfFont.Parse(File.ReadAllBytes(file));
            Console.WriteLine($"cell {f.CellWidth}x{f.CellHeight} chars {f.LoChar}..{f.HiChar} flags {f.Flags}");
            var rgba = f.ToRgbaStrip(out int w, out int h);
            Png.Write(png, w, h, rgba);
        }

        static void Res(string file, string group)
        {
            var r = TextResource.Parse(File.ReadAllBytes(file));
            Console.WriteLine($"{r.Count} entries");
            foreach (var (g, id) in r.Keys().Take(group == null ? 40 : int.MaxValue))
            {
                string gs = TagName(g);
                if (group != null && gs != group) continue;
                Console.WriteLine($"{gs}:{id} {r.Get(g, id)}");
            }
        }

        static string TagName(int g)
        {
            var chars = new[] { (char)(g & 0xFF), (char)((g >> 8) & 0xFF), (char)((g >> 16) & 0xFF), (char)((g >> 24) & 0xFF) };
            return chars.All(c => c >= 32 && c < 127) ? new string(chars) : g.ToString();
        }

        static void Csv(string file, int rows)
        {
            var t = CsvTable.Parse(File.ReadAllBytes(file));
            Console.WriteLine($"{t.Rows.Count} rows");
            foreach (var r in t.Rows.Take(rows)) Console.WriteLine(string.Join(" | ", r));
        }

        static void Scan(string dir)
        {
            var install = dir != null ? GameInstall.Open(dir) : GameInstall.TryLocate();
            if (install == null) throw new Exception("install not found");
            int ok = 0, bad = 0;
            void Try(string what, Action a)
            {
                try { a(); ok++; }
                catch (Exception e) { bad++; Console.WriteLine($"FAIL {what}: {e.Message}"); }
            }
            foreach (var folder in new[] { "gli", "room" })
                foreach (var f in install.List(folder).Where(f => f.EndsWith(".gli", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".glj", StringComparison.OrdinalIgnoreCase)))
                    Try(f, () =>
                    {
                        var data = install.ReadAllBytes(f);
                        if (!GliLibrary.IsGli(data)) throw new Exception("no GLIB magic: " + BitConverter.ToString(data, 0, 8));
                        var lib = new GliLibrary(data, f);
                        if (lib.Entries.Count == 0) throw new Exception("no images");
                        foreach (var e in lib.Entries) lib.Decode(e);
                    });
            foreach (var f in install.List("misc", ".mcf")) Try(f, () => McfFont.Parse(install.ReadAllBytes(f)));
            foreach (var f in install.List("misc", ".res")) Try(f, () => { var r = TextResource.Parse(install.ReadAllBytes(f)); if (r.Count == 0) throw new Exception("empty"); });
            foreach (var f in install.List("data", ".csv")) Try(f, () => { var t = CsvTable.Parse(install.ReadAllBytes(f)); if (t.Rows.Count == 0) throw new Exception("empty"); });
            Console.WriteLine($"ok {ok}, failed {bad}");
        }
    }
}
