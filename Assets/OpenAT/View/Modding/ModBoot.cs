using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Data;
using OpenAT.Sim.Modding;
using OpenAT.View.Boot;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Modding
{
    /// <summary>
    /// Loads the mods in the Unity build (docs/MODDING.md) with the same <see cref="ModLoader"/> the
    /// headless runs use: before the first scene, from <c>&lt;exe folder&gt;/Mods</c>,
    /// <c>Application.persistentDataPath/Mods</c>, <c>OPENAT_MODS_DIR</c> and <c>-openat-mods DIR</c>
    /// (repeatable); <c>-openat-nomods</c> loads none. Mods switched off in the mod list
    /// (<see cref="ModsScreen"/>, PlayerPrefs) are skipped. Rooms and screens of code mods are registered
    /// here; the game data gets the mods in <see cref="Prepare"/> (called by <see cref="GameAssets.GameData"/>).
    /// </summary>
    public static class ModBoot
    {
        public const string PrefDisabled = "OpenAT.Mods.Disabled";

        /// <summary>The session's mods (empty set when none are installed).</summary>
        public static ModSet Mods { get; private set; } = new ModSet();

        static readonly Dictionary<int, string> RoomNames = new Dictionary<int, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            try { Load(); }
            catch (Exception e) { Debug.LogError("OpenAT mods: loading failed, playing without mods: " + e); Mods = new ModSet(); }
        }

        static void Load()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.FindIndex(args, a => string.Equals(a, "-openat-nomods", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                Debug.Log("OpenAT mods: off (-openat-nomods)");
                return;
            }
            // Player: dataPath is <exe>_Data, so its parent is the exe folder; editor: the project folder.
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            var options = ModLoadOptions.Standard(exeDir, Application.persistentDataPath);
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-openat-mods", StringComparison.OrdinalIgnoreCase)) options.Roots.Add(args[i + 1]);
            foreach (var id in DisabledIds()) options.Disabled.Add(id);
            options.Log = Debug.Log;
            Mods = ModLoader.Load(options);
            RegisterViewContent(Mods);
        }

        /// <summary>Applies the mods to freshly loaded game data (content, tunables; worlds then get the world hooks).</summary>
        public static GameData Prepare(GameData data)
        {
            if (Mods.All.Count == 0 || data.Mods != null) return data;
            try { return Mods.ApplyData(data); }
            catch (Exception e) { Debug.LogError("OpenAT mods: applying to the game data failed: " + e); return data; }
        }

        /// <summary>Lobby / debug list name of a mod room (null for original rooms).</summary>
        public static string RoomName(int id) => RoomNames.TryGetValue(id, out var n) ? n : null;

        // ------------------------------------------------------------------ the player's mod list

        public static HashSet<string> DisabledIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in PlayerPrefs.GetString(PrefDisabled, "").Split(',')) if (s.Trim().Length > 0) set.Add(s.Trim());
            return set;
        }

        /// <summary>Switches a mod on or off for the next start (mods are loaded once per run).</summary>
        public static void SetEnabled(string id, bool on)
        {
            var set = DisabledIds();
            if (on) set.Remove(id); else set.Add(id);
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            PlayerPrefs.SetString(PrefDisabled, string.Join(",", list));
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ rooms and screens

        static void RegisterViewContent(ModSet set)
        {
            foreach (var m in set.Active)
            {
                // [Room(id)] / [ScreenName] classes of the mod's assemblies (the registries scan lazily, maybe before the mod was loaded).
                foreach (var asm in m.Assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                    Array.Sort(types, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        var type = t;
                        if (typeof(RoomScreen).IsAssignableFrom(t))
                            foreach (RoomAttribute a in t.GetCustomAttributes(typeof(RoomAttribute), false))
                            {
                                RoomRegistry.Register(a.Id, () => (RoomScreen)Activator.CreateInstance(type));
                                if (!RoomNames.ContainsKey(a.Id)) RoomNames[a.Id] = m.Manifest.DisplayName;
                                Debug.Log($"OpenAT mods: {m.Id}: room {a.Id} ({t.Name})");
                            }
                        if (typeof(GameScreen).IsAssignableFrom(t))
                            foreach (ScreenNameAttribute a in t.GetCustomAttributes(typeof(ScreenNameAttribute), false))
                            {
                                ScreenFactory.Register(a.Name, () => (GameScreen)Activator.CreateInstance(type));
                                Debug.Log($"OpenAT mods: {m.Id}: screen '{a.Name}' ({t.Name})");
                            }
                    }
                }
            }
            // Explicit registrations (ModContext.RegisterRoom / RegisterScreen) win over attributes.
            foreach (var r in set.Rooms)
            {
                var room = r;
                RoomRegistry.Register(room.Id, () => room.Factory() as RoomScreen
                    ?? throw new InvalidCastException($"mod {room.ModId}: room {room.Id} factory did not return a RoomScreen"));
                RoomNames[room.Id] = room.Name ?? RoomName(room.Id) ?? room.ModId;
                Debug.Log($"OpenAT mods: {room.ModId}: room {room.Id}{(room.Name != null ? " \"" + room.Name + "\"" : "")}");
            }
            foreach (var s in set.Screens)
            {
                var screen = s;
                ScreenFactory.Register(screen.Name, () => screen.Factory() as GameScreen
                    ?? throw new InvalidCastException($"mod {screen.ModId}: screen '{screen.Name}' factory did not return a GameScreen"));
                Debug.Log($"OpenAT mods: {screen.ModId}: screen '{screen.Name}'");
            }
        }
    }

    /// <summary>
    /// Per-game view services for mods, re-bound on every session change: mod news
    /// (<see cref="ModNewsEvent"/>) becomes an advisor message for the local player.
    /// </summary>
    public sealed class ModViewServices : MonoBehaviour
    {
        World _world;
        IDisposable _sub;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (ModBoot.Mods.ActiveCount == 0) return;
            var go = new GameObject("OpenAT.ModViewServices");
            DontDestroyOnLoad(go);
            go.AddComponent<ModViewServices>();
        }

        void Update()
        {
            var app = GameApp.Instance;
            var w = app?.Session?.World;
            if (w == _world) return;
            _sub?.Dispose();
            _sub = null;
            _world = w;
            if (w == null) return;
            _sub = w.Events.Subscribe<ModNewsEvent>(e =>
            {
                Debug.Log("OpenAT mods: news " + e);
                if (e.PlayerIndex < 0 || e.PlayerIndex == w.LocalPlayer) GameApp.Instance?.Advisors.Queue(AdvisorKind.Info, e.Text);
            });
        }
    }
}
