using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OpenAT.Sim.Modding
{
    /// <summary>Where to look for mods and which to skip.</summary>
    public sealed class ModLoadOptions
    {
        /// <summary>Folders searched in order: each is a mod folder (has mod.json) or contains mod folders. Missing folders are ignored.</summary>
        public readonly List<string> Roots = new List<string>();
        /// <summary>Mod ids switched off by the player (the options screen's mod list).</summary>
        public readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>False = data mods only (DLLs are not loaded).</summary>
        public bool LoadCode = true;
        /// <summary>Log sink (Console.WriteLine, Debug.Log); every line is also kept in <see cref="ModSet.Messages"/>.</summary>
        public Action<string> Log;

        /// <summary>
        /// The standard roots: <c>&lt;exe folder&gt;/Mods</c>, <c>&lt;user data&gt;/Mods</c> and every folder in the
        /// environment variable <c>OPENAT_MODS_DIR</c> (separated by ';' or the platform's path separator).
        /// </summary>
        public static ModLoadOptions Standard(string exeFolder, string userDataFolder)
        {
            var o = new ModLoadOptions();
            if (!string.IsNullOrEmpty(exeFolder)) o.Roots.Add(Path.Combine(exeFolder, "Mods"));
            if (!string.IsNullOrEmpty(userDataFolder)) o.Roots.Add(Path.Combine(userDataFolder, "Mods"));
            o.AddEnvironmentRoots();
            return o;
        }

        public ModLoadOptions AddEnvironmentRoots()
        {
            var env = Environment.GetEnvironmentVariable("OPENAT_MODS_DIR");
            if (!string.IsNullOrEmpty(env))
                foreach (var p in env.Split(';', Path.PathSeparator))
                    if (p.Trim().Length > 0) Roots.Add(p.Trim());
            return this;
        }
    }

    /// <summary>
    /// Discovers, orders and loads mods (docs/MODDING.md): <c>&lt;root&gt;/&lt;Mod&gt;/mod.json</c>, data files
    /// <c>&lt;Mod&gt;/data/*.json</c>, code <c>&lt;Mod&gt;/*.dll</c> (classes implementing <see cref="IOpenATMod"/>).
    /// Every failure (bad JSON, missing dependency, a DLL that does not load, an exception in OnLoad)
    /// is logged and only that mod is skipped. Usable headless (no Unity).
    /// </summary>
    public static class ModLoader
    {
        /// <summary>Assemblies the game provides; copies of them in a mod folder are never loaded.</summary>
        static readonly string[] HostAssemblies = { "OpenAT.Sim", "OpenAT.Formats", "OpenAT.View", "netstandard", "mscorlib", "System", "UnityEngine" };

        public static ModSet Load(ModLoadOptions options)
        {
            var set = new ModSet(options.Log);
            var candidates = Discover(options, set);
            var ordered = Order(candidates, set);
            set.SetLoadOrder(ordered);
            foreach (var m in ordered)
            {
                LoadOne(m, options, set);
                if (m.IsActive) Log(set, m);
            }
            if (set.All.Count == 0) set.Log("no mods found in " + string.Join("; ", options.Roots));
            return set;
        }

        /// <summary>Loads the mods of one folder (tests, <c>openat sim --mods DIR</c>).</summary>
        public static ModSet Load(string root, Action<string> log = null)
        {
            var o = new ModLoadOptions { Log = log };
            o.Roots.Add(root);
            return Load(o);
        }

        static void Log(ModSet set, LoadedMod m)
        {
            var parts = new List<string>();
            if (m.DataFiles.Count > 0) parts.Add(m.DataFiles.Count + " data file(s)");
            if (m.Instances.Count > 0) parts.Add("code: " + string.Join(", ", m.Instances.ConvertAll(i => i.GetType().FullName)));
            set.Log($"{m.Id} {m.Manifest.Version} loaded ({(parts.Count > 0 ? string.Join("; ", parts) : "nothing to apply")})");
        }

        // ------------------------------------------------------------------ discovery

        static List<LoadedMod> Discover(ModLoadOptions options, ModSet set)
        {
            var found = new List<LoadedMod>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in options.Roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                var folders = new List<string>();
                if (File.Exists(Path.Combine(root, ModManifest.FileName))) folders.Add(root);
                else
                {
                    var subs = Directory.GetDirectories(root);
                    Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
                    foreach (var d in subs) if (File.Exists(Path.Combine(d, ModManifest.FileName))) folders.Add(d);
                }
                foreach (var dir in folders)
                {
                    var m = new LoadedMod();
                    set.Add(m);
                    try { m.Manifest = ModManifest.Read(dir); }
                    catch (Exception e)
                    {
                        m.Manifest = new ModManifest { Id = Path.GetFileName(dir.TrimEnd('/', '\\')), Directory = dir };
                        set.Fail(m, "mod.json: " + e.Message);
                        continue;
                    }
                    if (!ids.Add(m.Id))
                    {
                        m.Status = ModStatus.Duplicate;
                        m.Error = "another mod with id '" + m.Id + "' was found first";
                        set.Log($"{m.Id}: skipped ({dir}): {m.Error}");
                        continue;
                    }
                    if (!m.Manifest.Enabled || options.Disabled.Contains(m.Id))
                    {
                        m.Status = ModStatus.Disabled;
                        set.Log($"{m.Id}: disabled");
                        continue;
                    }
                    m.Status = ModStatus.Loaded;
                    found.Add(m);
                }
            }
            return found;
        }

        // ------------------------------------------------------------------ ordering

        /// <summary>Dependencies first; among ready mods the lowest (loadOrder, id). Missing/too old dependencies and cycles fail the mod.</summary>
        static List<LoadedMod> Order(List<LoadedMod> mods, ModSet set)
        {
            // Dependency checks repeat until stable: a failed mod can break the mods depending on it.
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in mods)
                {
                    if (!m.IsActive) continue;
                    foreach (var d in m.Manifest.Dependencies)
                    {
                        var dep = mods.Find(x => x.IsActive && string.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase));
                        if (dep == null)
                        {
                            if (d.Optional) continue;
                            set.Fail(m, "missing dependency " + d);
                            changed = true;
                            break;
                        }
                        if (d.MinVersion != null && ModManifest.CompareVersions(dep.Manifest.Version, d.MinVersion) < 0)
                        {
                            set.Fail(m, $"dependency {d} found in version {dep.Manifest.Version}");
                            changed = true;
                            break;
                        }
                    }
                }
            }

            var ordered = new List<LoadedMod>();
            var done = new HashSet<LoadedMod>();
            var pending = mods.FindAll(m => m.IsActive);
            while (pending.Count > 0)
            {
                LoadedMod best = null;
                foreach (var m in pending)
                {
                    bool ready = true;
                    foreach (var d in m.Manifest.Dependencies)
                    {
                        var dep = pending.Find(x => string.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase));
                        if (dep != null && !done.Contains(dep)) { ready = false; break; }
                    }
                    if (!ready) continue;
                    if (best == null || m.Manifest.LoadOrder < best.Manifest.LoadOrder
                        || (m.Manifest.LoadOrder == best.Manifest.LoadOrder && string.CompareOrdinal(m.Id, best.Id) < 0)) best = m;
                }
                if (best == null)
                {
                    foreach (var m in pending) set.Fail(m, "dependency cycle");
                    break;
                }
                pending.Remove(best);
                done.Add(best);
                ordered.Add(best);
            }
            return ordered;
        }

        // ------------------------------------------------------------------ loading

        static void LoadOne(LoadedMod m, ModLoadOptions options, ModSet set)
        {
            var dir = m.Manifest.Directory;
            // Data first (checked before anything of the mod is registered).
            var staged = new ModRegistrations();
            var dataDir = Path.Combine(dir, "data");
            if (Directory.Exists(dataDir))
            {
                var files = Directory.GetFiles(dataDir, "*.json", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    string rel = f.Substring(dir.Length).TrimStart('/', '\\').Replace('\\', '/');
                    try { DataMod.Compile(rel, File.ReadAllText(f), staged, m.Id, set.Log); }
                    catch (Exception e)
                    {
                        set.Fail(m, rel + ": " + e.Message);
                        return;
                    }
                    m.DataFiles.Add(rel);
                }
            }

            var entries = new List<IOpenATMod>();
            List<string> dlls;
            try { dlls = AssemblyFiles(m); }
            catch (Exception e)
            {
                set.Fail(m, e.Message);
                return;
            }
            if (dlls.Count > 0 && !options.LoadCode) set.Log($"{m.Id}: code not loaded (data mods only)");
            else
            {
                foreach (var path in dlls)
                {
                    Assembly asm;
                    try { asm = ModAssemblies.Load(path, dir); }
                    catch (Exception e)
                    {
                        set.Fail(m, Path.GetFileName(path) + " does not load: " + e.Message);
                        return;
                    }
                    m.Assemblies.Add(asm);
                    try { entries.AddRange(EntryPoints(asm)); }
                    catch (Exception e)
                    {
                        set.Fail(m, Path.GetFileName(path) + ": " + Describe(e));
                        return;
                    }
                }
            }
            m.Reg.AddFrom(staged);
            if (entries.Count > 0) set.RunOnLoad(m, entries);
        }

        static List<string> AssemblyFiles(LoadedMod m)
        {
            var dir = m.Manifest.Directory;
            var list = new List<string>();
            if (m.Manifest.Assemblies != null)
            {
                foreach (var a in m.Manifest.Assemblies)
                {
                    var p = Path.Combine(dir, a);
                    if (!File.Exists(p)) throw new FileNotFoundException("mod.json names " + a + ", which does not exist");
                    list.Add(p);
                }
                return list;
            }
            var files = Directory.GetFiles(dir, "*.dll");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) if (!IsHostAssembly(Path.GetFileNameWithoutExtension(f))) list.Add(f);
            return list;
        }

        internal static bool IsHostAssembly(string n)
        {
            foreach (var h in HostAssemblies) if (string.Equals(n, h, StringComparison.OrdinalIgnoreCase)) return true;
            return n.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Public non-abstract <see cref="IOpenATMod"/> classes with a parameterless constructor, by full name.</summary>
        static List<IOpenATMod> EntryPoints(Assembly asm)
        {
            var types = new List<Type>();
            foreach (var t in asm.GetTypes())
                if (t.IsClass && !t.IsAbstract && t.IsPublic && typeof(IOpenATMod).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null) types.Add(t);
            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return types.ConvertAll(t => (IOpenATMod)Activator.CreateInstance(t));
        }

        static string Describe(Exception e)
        {
            if (e is ReflectionTypeLoadException r && r.LoaderExceptions != null && r.LoaderExceptions.Length > 0 && r.LoaderExceptions[0] != null)
                return "types do not load (built against another game version?): " + r.LoaderExceptions[0].Message;
            if (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }
    }

    /// <summary>
    /// Loads mod assemblies from bytes (no file lock, no probing of the mod folder) and resolves their
    /// references to the game's own assemblies: a mod compiled against <c>OpenAT.Sim.dll</c> binds to
    /// the Sim of the host, whichever assembly that is (OpenAT.Sim in Unity, the CLI or test assembly
    /// headless). Other references are looked up in the folders of loaded mods.
    /// </summary>
    public static class ModAssemblies
    {
        static readonly object Lock = new object();
        static readonly Dictionary<string, Assembly> ByPath = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> Folders = new List<string>();
        static bool _hooked;

        public static Assembly Load(string path, string modFolder)
        {
            lock (Lock)
            {
                Hook();
                if (!Folders.Contains(modFolder)) Folders.Add(modFolder);
                string full = Path.GetFullPath(path);
                // The same file is loaded once per process (a second copy would duplicate every type).
                string key = full + "|" + File.GetLastWriteTimeUtc(full).Ticks;
                if (ByPath.TryGetValue(key, out var a)) return a;
                byte[] raw = File.ReadAllBytes(full);
                string pdb = Path.ChangeExtension(full, ".pdb");
                a = File.Exists(pdb) ? Assembly.Load(raw, File.ReadAllBytes(pdb)) : Assembly.Load(raw);
                ByPath[key] = a;
                return a;
            }
        }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.Equals(name, "OpenAT.Sim", StringComparison.OrdinalIgnoreCase)) return typeof(World).Assembly;
            if (string.Equals(name, "OpenAT.Formats", StringComparison.OrdinalIgnoreCase)) return typeof(Formats.GameInstall).Assembly;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (!a.IsDynamic && string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            string[] folders;
            lock (Lock) folders = Folders.ToArray();
            foreach (var f in folders)
            {
                var p = Path.Combine(f, name + ".dll");
                if (File.Exists(p) && !ModLoader.IsHostAssembly(name))
                {
                    try { return Load(p, f); } catch (Exception) { /* fall through */ }
                }
            }
            return null;
        }
    }
}
