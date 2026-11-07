using System;
using System.Collections.Generic;
using System.Reflection;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Save;

namespace OpenAT.Sim.Modding
{
    public enum ModStatus
    {
        /// <summary>Loaded and applied.</summary>
        Loaded,
        /// <summary>Switched off (mod.json "enabled": false or the player's mod list).</summary>
        Disabled,
        /// <summary>Broken (bad mod.json, data file, missing dependency, exception in code): skipped.</summary>
        Failed,
        /// <summary>Another mod with the same id was found first.</summary>
        Duplicate,
    }

    /// <summary>One discovered mod and what became of it.</summary>
    public sealed class LoadedMod
    {
        public ModManifest Manifest;
        public ModStatus Status;
        /// <summary>Why the mod failed (or was skipped).</summary>
        public string Error;
        public readonly List<string> DataFiles = new List<string>();
        public readonly List<Assembly> Assemblies = new List<Assembly>();
        public readonly List<IOpenATMod> Instances = new List<IOpenATMod>();
        internal readonly ModRegistrations Reg = new ModRegistrations();

        public string Id => Manifest?.Id;
        public bool IsActive => Status == ModStatus.Loaded;
        public bool HasCode => Assemblies.Count > 0;
        public override string ToString() => $"{Id} {Manifest?.Version} [{Status}]{(Error != null ? ": " + Error : "")}";
    }

    /// <summary>
    /// The mods of a session, in load order (dependencies first, then <see cref="ModManifest.LoadOrder"/>,
    /// then id). Created by <see cref="ModLoader"/>; <see cref="ApplyData"/> attaches it to a
    /// <see cref="GameData"/>, after which every <see cref="World"/> built from that data gets the mods'
    /// world hooks (<see cref="ApplyWorld"/>, called from the world's constructor). The same code path
    /// runs headless (tests, <c>openat sim --mods DIR</c>) and in Unity, so games are identical.
    /// </summary>
    public sealed class ModSet
    {
        public const string StampKey = "openat.mods";

        readonly List<LoadedMod> _mods = new List<LoadedMod>();
        readonly List<string> _messages = new List<string>();
        readonly Action<string> _log;

        public ModSet(Action<string> log = null) => _log = log;

        /// <summary>Every discovered mod (also disabled and failed ones), active ones in load order.</summary>
        public IReadOnlyList<LoadedMod> All => _mods;
        public IEnumerable<LoadedMod> Active { get { foreach (var m in _mods) if (m.IsActive) yield return m; } }
        public int ActiveCount { get { int n = 0; foreach (var m in _mods) if (m.IsActive) n++; return n; } }
        /// <summary>Every log line written while loading and applying.</summary>
        public IReadOnlyList<string> Messages => _messages;

        public LoadedMod Find(string id) => _mods.Find(m => m.Id == id && m.IsActive);

        internal void Add(LoadedMod m) => _mods.Add(m);

        /// <summary>Puts the active mods first, in load order (application iterates this list); the others follow in discovery order.</summary>
        internal void SetLoadOrder(List<LoadedMod> ordered)
        {
            var rest = _mods.FindAll(m => !ordered.Contains(m));
            _mods.Clear();
            _mods.AddRange(ordered);
            _mods.AddRange(rest);
        }

        public void Log(string message)
        {
            lock (_messages) _messages.Add(message);
            _log?.Invoke("OpenAT mods: " + message);
        }

        internal void Fail(LoadedMod m, string error)
        {
            m.Status = ModStatus.Failed;
            m.Error = error;
            Log($"{m.Id ?? m.Manifest?.Directory}: FAILED, skipped: {error}");
        }

        // ------------------------------------------------------------------ registration from code

        /// <summary>
        /// Adds a code mod instance directly (tests, or a host that compiles a mod in). Runs its
        /// <see cref="IOpenATMod.OnLoad"/> with failure isolation, like a mod loaded from a DLL.
        /// </summary>
        public LoadedMod AddCodeMod(ModManifest manifest, IOpenATMod mod)
        {
            var m = new LoadedMod { Manifest = manifest, Status = ModStatus.Loaded };
            _mods.Add(m);
            RunOnLoad(m, new[] { mod });
            if (m.IsActive) Log($"{m.Id} {manifest.Version} loaded (code: {mod.GetType().FullName})");
            return m;
        }

        /// <summary>Calls OnLoad of each entry class; any exception discards all of the mod's registrations.</summary>
        internal void RunOnLoad(LoadedMod m, IEnumerable<IOpenATMod> mods)
        {
            var staged = new ModRegistrations();
            foreach (var mod in mods)
            {
                var ctx = new ModContext(m.Manifest, Log);
                try { mod.OnLoad(ctx); }
                catch (Exception e)
                {
                    Fail(m, mod.GetType().FullName + ".OnLoad threw " + e.GetType().Name + ": " + e.Message);
                    return;
                }
                staged.AddFrom(ctx.Reg);
                m.Instances.Add(mod);
            }
            m.Reg.AddFrom(staged);
        }

        // ------------------------------------------------------------------ application

        /// <summary>
        /// Applies the content edits and tunable overrides of every active mod to <paramref name="data"/>
        /// (once) and attaches the set (<see cref="GameData.Mods"/>). Returns <paramref name="data"/>.
        /// </summary>
        public GameData ApplyData(GameData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Mods == this) return data;
            if (data.Mods != null) throw new InvalidOperationException("the game data already has mods applied");
            data.Mods = this;
            foreach (var m in _mods)
            {
                if (!m.IsActive) continue;
                foreach (var edit in m.Reg.DataEdits)
                {
                    try { edit(data); }
                    catch (Exception e) { Fail(m, "content edit failed: " + e.Message); break; }
                }
            }
            foreach (var line in data.Tunables.Changes()) Log("tunable " + line);
            return data;
        }

        /// <summary>Default options for a new game with the mods' option edits applied.</summary>
        public GameOptions CreateOptions()
        {
            var o = new GameOptions();
            foreach (var m in _mods)
            {
                if (!m.IsActive) continue;
                foreach (var edit in m.Reg.OptionEdits)
                {
                    try { edit(o); }
                    catch (Exception e) { Log($"{m.Id}: option edit failed: {e.Message}"); }
                }
            }
            return o;
        }

        /// <summary>Runs every active mod's world hooks on a freshly constructed world (from <c>World.InitModding</c>).</summary>
        public void ApplyWorld(World w)
        {
            if (ActiveCount == 0) return;
            foreach (var m in _mods)
            {
                if (!m.IsActive) continue;
                foreach (var hook in m.Reg.WorldHooks)
                {
                    try { hook(w); }
                    catch (Exception e) { Log($"{m.Id}: world hook failed (skipped for this world): {e.GetType().Name}: {e.Message}"); }
                }
            }
            var stamp = ModState.Get<ModStamp>(w, StampKey);
            stamp.Update(this);
        }

        /// <summary>"id version" of the active mods in load order.</summary>
        public List<string> Signature()
        {
            var l = new List<string>();
            foreach (var m in _mods) if (m.IsActive) l.Add(m.Id + " " + m.Manifest.Version);
            return l;
        }

        // ------------------------------------------------------------------ registries for hosts

        public IEnumerable<ModRoom> Rooms { get { foreach (var m in _mods) if (m.IsActive) foreach (var r in m.Reg.Rooms) yield return r; } }
        public IEnumerable<ModScreen> Screens { get { foreach (var m in _mods) if (m.IsActive) foreach (var s in m.Reg.Screens) yield return s; } }

        public IEnumerable<string> CommandNames { get { foreach (var m in _mods) if (m.IsActive) foreach (var c in m.Reg.Commands) yield return c.Key; } }

        /// <summary>Builds a command registered with <see cref="ModContext.RegisterCommand"/> (later mods override earlier ones); null = unknown.</summary>
        public ICommand<World> CreateCommand(string name, World w, params string[] args)
        {
            Func<World, string[], ICommand<World>> f = null;
            foreach (var m in _mods)
                if (m.IsActive)
                    foreach (var c in m.Reg.Commands)
                        if (string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase)) f = c.Value;
            return f?.Invoke(w, args ?? new string[0]);
        }

        // ------------------------------------------------------------------ save compatibility

        /// <summary>
        /// Differences between the mods a loaded game was saved with and the mods active now (empty =
        /// compatible). Call after <see cref="SaveGame.Read(System.IO.Stream, GameData)"/>.
        /// </summary>
        public static List<string> CheckSave(World w)
        {
            var issues = new List<string>();
            var now = w.Data?.Mods?.Signature() ?? new List<string>();
            List<string> saved = null;
            if (w.Extensions.TryGetValue(StampKey, out var v) && v is ModStamp s && s.LoadedFromSave) saved = s.SavedWith;
            saved = saved ?? new List<string>();
            foreach (var x in saved)
            {
                string id = x.Split(' ')[0];
                var cur = now.Find(n => n.Split(' ')[0] == id);
                if (cur == null) issues.Add("saved with mod " + x + ", which is not loaded now (its state is ignored)");
                else if (cur != x) issues.Add("saved with mod " + x + ", loaded now: " + cur);
            }
            foreach (var n in now)
            {
                string id = n.Split(' ')[0];
                if (!saved.Exists(x => x.Split(' ')[0] == id)) issues.Add("mod " + n + " was not active when the game was saved");
            }
            return issues;
        }
    }

    /// <summary>
    /// Saved list of the mods a game runs with (in <see cref="World.Extensions"/> under
    /// <see cref="ModSet.StampKey"/>; only present when mods are active). Updated before every save.
    /// </summary>
    public sealed class ModStamp : ISaveHooks
    {
        /// <summary>"id version" of the mods active when the game was (last) saved.</summary>
        public List<string> Mods = new List<string>();
        /// <summary>After loading: the list read from the file.</summary>
        [NotSaved] public List<string> SavedWith;
        [NotSaved] public bool LoadedFromSave;

        public void Update(ModSet set) => Mods = set.Signature();

        void ISaveHooks.OnBeforeSave(World w) => Mods = w.Data?.Mods?.Signature() ?? new List<string>();

        void ISaveHooks.OnAfterLoad(World w)
        {
            SavedWith = new List<string>(Mods);
            LoadedFromSave = true;
            Mods = w.Data?.Mods?.Signature() ?? new List<string>();
        }
    }
}
