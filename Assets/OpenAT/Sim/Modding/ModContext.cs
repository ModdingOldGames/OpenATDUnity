using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Modding
{
    /// <summary>
    /// Entry point of a code mod: a public class with a parameterless constructor in a DLL in the mod
    /// folder. <see cref="OnLoad"/> runs once when the mods are loaded (before any game data or world
    /// exists); it only <em>registers</em> things on the context, which are then applied to the game data
    /// and to every world (new game and loaded game, headless and in Unity alike). See docs/MODDING.md.
    /// </summary>
    public interface IOpenATMod
    {
        void OnLoad(ModContext ctx);
    }

    /// <summary>What a mod registered (applied later, in mod load order and registration order).</summary>
    sealed class ModRegistrations
    {
        public readonly List<Action<GameData>> DataEdits = new List<Action<GameData>>();
        public readonly List<Action<GameOptions>> OptionEdits = new List<Action<GameOptions>>();
        public readonly List<Action<World>> WorldHooks = new List<Action<World>>();
        public readonly List<ModRoom> Rooms = new List<ModRoom>();
        public readonly List<ModScreen> Screens = new List<ModScreen>();
        public readonly List<KeyValuePair<string, Func<World, string[], ICommand<World>>>> Commands = new List<KeyValuePair<string, Func<World, string[], ICommand<World>>>>();

        public bool Empty => DataEdits.Count == 0 && OptionEdits.Count == 0 && WorldHooks.Count == 0 && Rooms.Count == 0 && Screens.Count == 0 && Commands.Count == 0;

        public void AddFrom(ModRegistrations o)
        {
            DataEdits.AddRange(o.DataEdits);
            OptionEdits.AddRange(o.OptionEdits);
            WorldHooks.AddRange(o.WorldHooks);
            Rooms.AddRange(o.Rooms);
            Screens.AddRange(o.Screens);
            Commands.AddRange(o.Commands);
        }
    }

    /// <summary>A room a mod adds or replaces (the factory returns an <c>OpenAT.View</c> RoomScreen; used by the view only).</summary>
    public sealed class ModRoom
    {
        public string ModId;
        public int Id;
        public Func<object> Factory;
        /// <summary>Name for debug lists (the lobby) when the game texts have none.</summary>
        public string Name;
    }

    /// <summary>A named screen a mod adds (the factory returns an <c>OpenAT.View</c> GameScreen).</summary>
    public sealed class ModScreen
    {
        public string ModId;
        public string Name;
        public Func<object> Factory;
    }

    /// <summary>
    /// The API a mod uses in <see cref="IOpenATMod.OnLoad"/>. Everything is recorded and applied
    /// later: content edits once to the game data, world hooks to every world when it is constructed
    /// (after the default systems and rules exist, before the new-game steps run or a save is laid over
    /// it). Exceptions of a mod are caught and logged; the mod is then skipped (or its failing hook).
    /// </summary>
    public sealed class ModContext
    {
        internal readonly ModRegistrations Reg = new ModRegistrations();
        readonly Action<string> _log;

        public readonly ModManifest Manifest;
        public string Id => Manifest.Id;
        /// <summary>The mod folder (read your own files from here).</summary>
        public string Directory => Manifest.Directory;
        /// <summary>Key of the mod's world/player state in <see cref="World.Extensions"/> / <see cref="Player.Extensions"/>: "mod.&lt;id&gt;".</summary>
        public string StateKey => "mod." + Manifest.Id;

        internal ModContext(ModManifest manifest, Action<string> log)
        {
            Manifest = manifest;
            _log = log;
        }

        public void Log(string message) => _log?.Invoke("[" + Id + "] " + message);

        // ------------------------------------------------------------------ content (game data)

        /// <summary>Changes the game content once it is loaded (plane types, cities, routes, tables...).</summary>
        public void EditData(Action<GameData> edit) => Reg.DataEdits.Add(edit ?? throw new ArgumentNullException(nameof(edit)));

        /// <summary>Adds a plane type, or replaces the one with the same id.</summary>
        public void AddPlaneType(PlaneType type) => EditData(d => d.PlaneTypes.Register(type));

        /// <summary>Defines a new tunable number of this mod (read it with <c>w.Tunables.Get(key)</c>).</summary>
        public void DefineTunable(string key, double def, string description = null, double min = double.MinValue, double max = double.MaxValue, bool integer = false) =>
            EditData(d => d.Tunables.Define(key, def, description, min, max, integer));

        /// <summary>Overrides a tunable (built-in: <see cref="TunableKeys"/>).</summary>
        public void SetTunable(string key, double value) => EditData(d =>
        {
            var warn = d.Tunables.Set(key, value, Id);
            if (warn != null) Log(warn);
        });

        /// <summary>Changes the default options of new games (games started without explicit options).</summary>
        public void EditNewGameOptions(Action<GameOptions> edit) => Reg.OptionEdits.Add(edit ?? throw new ArgumentNullException(nameof(edit)));

        // ------------------------------------------------------------------ per world

        /// <summary>Runs for every world (new or loaded) after its default systems and rules are registered.</summary>
        public void OnWorldCreated(Action<World> hook) => Reg.WorldHooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));

        /// <summary>Adds a system to every world's pipeline (exceptions in it are logged and the system is switched off for that world).</summary>
        public void AddSystem(Func<ISimSystem> create) => OnWorldCreated(w => w.Systems.Add(new GuardedSystem(Id, create(), _log)));

        /// <summary>Replaces the system with the same name.</summary>
        public void ReplaceSystem(Func<ISimSystem> create) => OnWorldCreated(w => w.Systems.Replace(new GuardedSystem(Id, create(), _log)));

        public void RemoveSystem(string name) => OnWorldCreated(w => w.Systems.Remove(name));

        /// <summary>Adds a new-game step (runs once when a new game is set up; ordered like the built-in steps).</summary>
        public void AddSetupStep(Func<ISetupStep> create) => OnWorldCreated(w => w.SetupSteps.Add(create()));

        /// <summary>Sets (replaces) a rule: <c>ctx.SetRule&lt;IInterestRule&gt;(w =&gt; new MyInterest())</c>.</summary>
        public void SetRule<T>(Func<World, T> create) where T : class, IRule => OnWorldCreated(w => w.Rules.Set(create(w)));

        /// <summary>Wraps the current rule: <c>ctx.DecorateRule&lt;IRouteDemandRule&gt;(inner =&gt; new MyDemand(inner))</c>.</summary>
        public void DecorateRule<T>(Func<T, T> wrap) where T : class, IRule => OnWorldCreated(w => w.Rules.Decorate(wrap));

        public void DecorateRule<T>(Func<World, T, T> wrap) where T : class, IRule => OnWorldCreated(w => w.Rules.Decorate<T>(inner => wrap(w, inner)));

        /// <summary>Subscribes to a sim event in every world (handler exceptions are logged, not thrown).</summary>
        public void Subscribe<T>(Action<World, T> handler) where T : ISimEvent => OnWorldCreated(w => w.Events.Subscribe<T>(e =>
        {
            try { handler(w, e); }
            catch (Exception ex) { _log?.Invoke("[" + Id + "] event handler " + typeof(T).Name + " failed: " + ex); }
        }));

        // ------------------------------------------------------------------ commands

        /// <summary>
        /// Names a command factory (debug console, scripts, the CLI): <c>ModSet.CreateCommand(name, world, args)</c>.
        /// Commands themselves need no registration: any <see cref="ICommand{TWorld}"/> can be executed with <see cref="World.Execute{T}"/>.
        /// </summary>
        public void RegisterCommand(string name, Func<World, string[], ICommand<World>> factory) =>
            Reg.Commands.Add(new KeyValuePair<string, Func<World, string[], ICommand<World>>>(name, factory ?? throw new ArgumentNullException(nameof(factory))));

        // ------------------------------------------------------------------ view (ignored headless)

        /// <summary>
        /// Adds or replaces a room. The factory must return an <c>OpenAT.View.Rooms.Framework.RoomScreen</c>.
        /// New rooms use ids from 1000 (<c>RoomIds.FirstCustom</c>). Rooms marked <c>[Room(id)]</c> in a mod assembly are registered automatically.
        /// </summary>
        public void RegisterRoom(int id, Func<object> factory, string name = null) =>
            Reg.Rooms.Add(new ModRoom { ModId = Id, Id = id, Factory = factory ?? throw new ArgumentNullException(nameof(factory)), Name = name });

        /// <summary>Adds a named screen (<c>-openat-screen NAME</c>); the factory must return an <c>OpenAT.View.UI.GameScreen</c>.</summary>
        public void RegisterScreen(string name, Func<object> factory) =>
            Reg.Screens.Add(new ModScreen { ModId = Id, Name = name, Factory = factory ?? throw new ArgumentNullException(nameof(factory)) });

        // ------------------------------------------------------------------ saved state

        /// <summary>The mod's world state (created on first use, saved with the game under <see cref="StateKey"/>).</summary>
        public T State<T>(World w) where T : class, new() => ModState.Get<T>(w, StateKey);

        /// <summary>The mod's state of one airline (saved with the game).</summary>
        public T PlayerState<T>(Player p) where T : class, new() => ModState.Get<T>(p, StateKey);
    }

    /// <summary>Typed access to extension state (<see cref="World.Extensions"/> / <see cref="Player.Extensions"/>).</summary>
    public static class ModState
    {
        public static T Get<T>(World w, string key) where T : class, new() => Get<T>(w.Extensions, key);
        public static T Get<T>(Player p, string key) where T : class, new() => Get<T>(p.Extensions, key);

        static T Get<T>(Dictionary<string, object> bag, string key) where T : class, new()
        {
            if (bag.TryGetValue(key, out var v) && v is T t) return t;
            t = new T();
            bag[key] = t;
            return t;
        }
    }

    /// <summary>
    /// Wraps a mod's system: an exception is logged once and the system is switched off for that
    /// world (the game keeps running). <see cref="Inner"/> is the mod's system.
    /// </summary>
    public sealed class GuardedSystem : ISimSystem
    {
        public readonly ISimSystem Inner;
        public readonly string ModId;
        readonly Action<string> _log;
        bool _failed;

        public GuardedSystem(string modId, ISimSystem inner, Action<string> log)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            ModId = modId;
            _log = log;
        }

        public string Name => Inner.Name;
        public int Order => Inner.Order;
        public bool Failed => _failed;

        void Fail(Exception e, string hook)
        {
            _failed = true;
            _log?.Invoke($"[{ModId}] system {Inner.Name}.{hook} failed and is switched off: {e}");
        }

        // No closures: OnTick runs every tick.
        public void OnTick(World w) { if (_failed) return; try { Inner.OnTick(w); } catch (Exception e) { Fail(e, nameof(OnTick)); } }
        public void OnMinute(World w) { if (_failed) return; try { Inner.OnMinute(w); } catch (Exception e) { Fail(e, nameof(OnMinute)); } }
        public void OnHour(World w) { if (_failed) return; try { Inner.OnHour(w); } catch (Exception e) { Fail(e, nameof(OnHour)); } }
        public void OnNewDay(World w) { if (_failed) return; try { Inner.OnNewDay(w); } catch (Exception e) { Fail(e, nameof(OnNewDay)); } }
        public void OnPlayerNewDay(World w, Player p) { if (_failed) return; try { Inner.OnPlayerNewDay(w, p); } catch (Exception e) { Fail(e, nameof(OnPlayerNewDay)); } }
        public void OnMorning(World w) { if (_failed) return; try { Inner.OnMorning(w); } catch (Exception e) { Fail(e, nameof(OnMorning)); } }
        public void OnEvening(World w) { if (_failed) return; try { Inner.OnEvening(w); } catch (Exception e) { Fail(e, nameof(OnEvening)); } }
    }
}
