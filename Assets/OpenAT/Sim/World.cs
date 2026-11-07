using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;

namespace OpenAT.Sim
{
    /// <summary>
    /// The complete game state plus the machinery that advances it. Partial class: subsystems add
    /// their world-level state in their own files (World.Routes.cs, World.Kerosene.cs, ...).
    ///
    /// Everything that changes state goes through <see cref="Execute{T}"/> (player/AI commands) or a
    /// registered <see cref="ISimSystem"/> (time-driven processes). Formulas are looked up in
    /// <see cref="Rules"/>; observers subscribe to <see cref="Events"/>.
    /// </summary>
    public sealed partial class World
    {
        public readonly GameData Data;
        public readonly GameOptions Options;
        public readonly GameClock Clock = new GameClock();
        public readonly RuleSet Rules = new RuleSet();
        public readonly SimPipeline Systems = new SimPipeline();
        public readonly EventBus Events = new EventBus();
        public readonly Player[] Players = new Player[Player.Count];

        /// <summary>Mission value (see <see cref="Mission"/>).</summary>
        public int Difficulty = Mission.FreeGame;
        /// <summary>Home airport city id (shared by all airlines).</summary>
        public int HomeCity;
        /// <summary>Index of the human player on this machine.</summary>
        public int LocalPlayer;

        /// <summary>Global pedestrian random streams (reseeded every game minute).</summary>
        public readonly TeakRand PersonRandCreate = new TeakRand();
        public readonly TeakRand PersonRandMisc = new TeakRand();

        public bool Paused;
        /// <summary>Global call-it-a-day (mirrors the local player in single player).</summary>
        public bool CallItADay;

        /// <summary>State owned by extensions, keyed by a unique name.</summary>
        public readonly Dictionary<string, object> Extensions = new Dictionary<string, object>();

        public World(GameData data, GameOptions options = null)
        {
            Data = data;
            Options = options ?? new GameOptions();
            for (int i = 0; i < Player.Count; i++) Players[i] = new Player(i) { World = this };
            InitSubsystems();
        }

        /// <summary>Hook for partial files to register their default systems and rules.</summary>
        partial void InitSubsystems();

        public Player Local => Players[LocalPlayer];

        public IEnumerable<Player> ActivePlayers
        {
            get { foreach (var p in Players) if (!p.IsOut) yield return p; }
        }

        public City City(int id) => Data.Cities[id];

        // ------------------------------------------------------------------ commands

        /// <summary>Validates and applies a command. Publishes <see cref="CommandExecuted"/>.</summary>
        public CommandResult Execute<T>(T command) where T : ICommand<World>
        {
            var r = command.Validate(this);
            if (r.Ok)
            {
                command.Apply(this);
                Events.Publish(new CommandExecuted(command));
            }
            return r;
        }

        // ------------------------------------------------------------------ time

        /// <summary>
        /// Advances the simulation by one tick (sim-core.md §2.3/2.4): tick systems, clock +30 units,
        /// minute/hour hooks on change, midnight new-day sequence.
        /// </summary>
        public void Tick()
        {
            if (Paused) return;
            Systems.RunTick(this);
            if (Clock.Frozen) return;

            int oldMinute = Clock.Minute;
            Clock.Time += GameClock.UnitsPerTick;
            Clock.TicksPlayed++;

            if (Clock.Time >= GameClock.UnitsPerDay) RunNewDay();

            int minute = Clock.Minute;
            if (minute != oldMinute)
            {
                PersonRandCreate.SetSeed(minute * 333 + Clock.Hour * 7);
                PersonRandMisc.SetSeed(minute * 333 + Clock.Hour * 7);
                Systems.RunMinute(this);
                if (minute < oldMinute) Systems.RunHour(this);
            }

            if (Clock.Time == GameClock.DayStart) Clock.TimeSlice = 0;
            else Clock.TimeSlice++;
        }

        /// <summary>Midnight: world sequence, per-player sequence (at its slot), date +1.</summary>
        void RunNewDay()
        {
            Systems.RunNewDay(this);
            Events.Publish(new DayChanged(Clock.Date));
        }

        /// <summary>Called by the date system at the point of the midnight sequence where Date increments.</summary>
        public void AdvanceDate()
        {
            // Weekday of the coming day = Sunday-based weekday of yesterday (sim-core.md §1.2).
            Clock.Weekday = GameClock.SundayBasedWeekday(Clock.StartTime + (long)Clock.Date * 86400);
            Clock.Date++;
            Clock.Time = 0;
            Clock.TimeSlice = 0;
        }

        /// <summary>Runs <see cref="ISimSystem.OnPlayerNewDay"/> for every active airline in index order.</summary>
        public void RunPlayerNewDays()
        {
            foreach (var p in Players)
                if (!p.IsOut) Systems.RunPlayerNewDay(this, p);
        }

        /// <summary>Starts the working day (leaving the morning briefing).</summary>
        public void StartWorkingDay()
        {
            Clock.Frozen = false;
            Clock.State = DayState.Playing;
        }

        // ------------------------------------------------------------------ randomness helpers

        /// <summary>A throw-away generator seeded like the original's local generators.</summary>
        public static TeakRand LocalRandom(int seed) => new TeakRand(seed);
    }

    // ---------------------------------------------------------------------- core events

    public sealed class CommandExecuted : ISimEvent
    {
        public readonly object Command;
        public CommandExecuted(object c) => Command = c;
    }

    public sealed class DayChanged : ISimEvent
    {
        public readonly int NewDate;
        public DayChanged(int d) => NewDate = d;
    }
}
