using System;
using System.IO;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Modding;
using OpenAT.Sim.Save;

namespace OpenAT.Cli
{
    /// <summary>
    /// `openat sim --days N --mission M [--seed S] [--hall] [--trace]`: a headless game with all four
    /// airlines as computers, printing one table row per airline per day (for regression diffs).
    /// Mission is the original difficulty value (-1 free game, 0..5, 11..20, 41..50).
    /// Save games: `--save-at D --save FILE` writes the game after day D (at the next 09:00);
    /// `--load FILE --days N` continues a saved game for N days. The last line is the deep state hash,
    /// so `sim --days 15 --save-at 5 --save f` and `sim --load f --days 10` must end with the same hash.
    /// Mods: `--mods DIR` (repeatable; a mod folder or a folder of mods) plus OPENAT_MODS_DIR, loaded and
    /// applied exactly like the Unity build does (docs/MODDING.md); `--no-mods` ignores the environment.
    /// </summary>
    static class SimCommand
    {
        public static int Run(string[] args)
        {
            int days = 20, mission = Mission.FreeGame, seed = 12345;
            bool hall = false, trace = false;
            int statement = -1, saveAt = -1;
            string dir = null, saveFile = null, loadFile = null;
            var modDirs = new System.Collections.Generic.List<string>();
            bool modEnv = true;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--days": days = int.Parse(args[++i]); break;
                    case "--mission": mission = int.Parse(args[++i]); break;
                    case "--seed": seed = int.Parse(args[++i]); break;
                    case "--install": dir = args[++i]; break;
                    case "--hall": hall = true; break;
                    case "--trace": trace = true; break;
                    case "--statement": statement = int.Parse(args[++i]); break;
                    case "--save-at": saveAt = int.Parse(args[++i]); break;
                    case "--save": saveFile = args[++i]; break;
                    case "--load": loadFile = args[++i]; break;
                    case "--mods": modDirs.Add(args[++i]); break;
                    case "--no-mods": modEnv = false; break;
                    default: Console.Error.WriteLine("unknown option " + args[i]); return 1;
                }
            }
            var install = dir != null ? GameInstall.Open(dir) : GameInstall.TryLocate();
            if (install == null) { Console.Error.WriteLine("install not found (set OPENAT_GAME_DIR)"); return 1; }

            var data = GameData.Load(install);
            FinanceData.Load(data, install);
            if (hall)
            {
                HallModule.Register();
                HallContent.Attach(data, HallData.Load(install));
            }
            if (saveFile != null && saveAt < 0) saveAt = days;

            var modOptions = new ModLoadOptions { Log = m => Console.WriteLine("# " + m) };
            modOptions.Roots.AddRange(modDirs);
            if (modEnv) modOptions.AddEnvironmentRoots();
            if (modOptions.Roots.Count > 0) ModLoader.Load(modOptions).ApplyData(data);

            HeadlessRunner runner;
            if (loadFile != null)
            {
                // A saved hall needs its content; the loader adds the hall systems itself.
                if (!hall) HallContent.Attach(data, HallData.Load(install));
                SaveHeader header;
                using (var f = File.OpenRead(loadFile)) header = SaveGame.ReadHeader(f);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                World loaded;
                System.Collections.Generic.List<string> warnings;
                using (var f = File.OpenRead(loadFile)) loaded = SaveGame.Read(f, data, out warnings);
                Console.WriteLine($"# loaded {loadFile} in {sw.ElapsedMilliseconds} ms: \"{header.Name}\", mission {header.Mission}, day {header.Date} " +
                                  $"({header.Day}.{header.Month}.{header.Year}, weekday {header.Weekday}) {header.ClockText}, {header.Airline}");
                foreach (var x in warnings) Console.WriteLine("# warning: " + x);
                foreach (var x in ModSet.CheckSave(loaded)) Console.WriteLine("# mods: " + x);
                runner = new HeadlessRunner(loaded);
                mission = loaded.Difficulty;
            }
            else
            {
                runner = HeadlessRunner.NewGame(data, mission, seed);
                if (hall && runner.World.Hall == null) HallModule.EnsureHall(runner.World);
                try { runner.World.DesignCatalog = PlaneDesignCatalog.Load(install); } catch { /* keep the default catalogue */ }
            }
            var w = runner.World;
            if (trace) w.AI.Trace = Console.WriteLine;
            w.Events.Subscribe<ModNewsEvent>(e => Console.WriteLine($"# d{w.Clock.Date} {w.Clock.ClockText} news {e}"));
            if (statement >= 0)
                w.Events.Subscribe<MoneyChanged>(e => { if (e.Player.Index == statement) Console.WriteLine($"#   d{w.Clock.Date} {w.Clock.ClockText} P{statement} reason {e.Reason}: {e.Amount}"); });

            Console.WriteLine(loadFile != null
                ? $"# openat sim: loaded game, mission {mission}, {days} days, hall {(w.Hall != null ? "on" : "off")}"
                : $"# openat sim: mission {mission}, seed {seed}, {days} days, hall {(w.Hall != null ? "on" : "off")}");
            Console.WriteLine(HeadlessRunner.Header());
            int played = 0;
            runner.RunDays(days, (day, rows) =>
            {
                foreach (var r in rows) Console.WriteLine(HeadlessRunner.Format(day, r));
                if (++played == saveAt && saveFile != null)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    SaveGame.WriteFile(w, saveFile, "SIM DAY " + w.Clock.Date);
                    Console.WriteLine($"# saved {saveFile} ({new FileInfo(saveFile).Length} bytes, {sw.ElapsedMilliseconds} ms), state hash {SaveGame.StateHash(w)}");
                }
            });
            foreach (var x in runner.Winners) Console.WriteLine($"# mission won by P{x.Player} on the morning of day {x.Date}");
            foreach (var x in runner.Outs) Console.WriteLine($"# P{x.Player} out on day {x.Date}: {x.Reason}");
            Console.WriteLine($"# day {w.Clock.Date}, state hash {SaveGame.StateHash(w)} (restore count {w.RestoreCount})");
            return 0;
        }
    }
}
