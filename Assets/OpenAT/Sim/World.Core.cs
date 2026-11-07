using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Systems;

namespace OpenAT.Sim
{
    /// <summary>A step of new-game initialisation (sim-core.md §3). Ordered like systems.</summary>
    public interface ISetupStep
    {
        string Name { get; }
        int Order { get; }
        void Apply(World w, NewGameSettings settings);
    }

    /// <summary>Choices made in the new-game wizard.</summary>
    public sealed class NewGameSettings
    {
        public int Difficulty = Mission.FreeGame;
        /// <summary>Home airport city id; 0 = default by language.</summary>
        public int HomeCity;
        public int LocalPlayer;
        /// <summary>Owner per airline; defaults: local player human, others computer.</summary>
        public Owner[] Owners;
        public string[] PlayerNames = new string[Player.Count];
        public string[] AirlineNames = new string[Player.Count];
        public string[] Abbreviations = new string[Player.Count];
        /// <summary>Calendar start (unix seconds). Null = choose like the original (random day of 1970 or now).</summary>
        public long? StartTime;
        /// <summary>Seed for the places where the original used the wall clock or C rand().</summary>
        public int Seed = 12345;
    }

    public sealed partial class World
    {
        /// <summary>New-game steps; subsystems and extensions add theirs.</summary>
        public static readonly List<Func<ISetupStep>> SetupStepFactories = new List<Func<ISetupStep>>();

        /// <summary>Default systems; subsystems append factories in their own files.</summary>
        public static readonly List<Func<ISimSystem>> SystemFactories = new List<Func<ISimSystem>>
        {
            () => new DaySystem(),
            () => new AdvanceDateSystem(),
            () => new PlayerNewDaySystem(),
            () => new EndOfNightSystem(),
        };

        /// <summary>New-game steps of this world (filled by the domain Init* hooks and extensions).</summary>
        public readonly List<ISetupStep> SetupSteps = new List<ISetupStep>();

        partial void InitSubsystems()
        {
            foreach (var f in SystemFactories) Systems.Add(f());
            foreach (var f in SetupStepFactories) SetupSteps.Add(f());
            // Each domain registers its systems, rules and setup steps in its own file.
            InitFinance();
            InitOperations();
            InitAI();
            InitItems();
            InitSave();
            InitMail();
            InitTutorial();
            InitRivals();
            RegisterDefaultRules();
            // Last: mods see (and may replace or decorate) every default system and rule (Sim/Modding).
            InitModding();
        }

        /// <summary>Tunables and the world hooks of the data's mods (Sim/Modding/World.Modding.cs).</summary>
        partial void InitModding();

        /// <summary>Economy, staff, kerosene, statistics, missions (Sim/Finance, Sim/Missions).</summary>
        partial void InitFinance();
        /// <summary>Planes, flights, contracts, routes, freight, gates, branches, auctions (Sim/Fleet, Sim/Contracts, Sim/Routes).</summary>
        partial void InitOperations();
        /// <summary>Computer players (Sim/AI).</summary>
        partial void InitAI();
        /// <summary>Items, pranks, security, special rooms (Sim/Items).</summary>
        partial void InitItems();

        /// <summary>Registers the default (original) rule implementations. Each domain file adds to it.</summary>
        void RegisterDefaultRules()
        {
            foreach (var r in RuleFactories) r(this);
        }

        public static readonly List<Action<World>> RuleFactories = new List<Action<World>>();

        /// <summary>Creates and initialises a new game.</summary>
        public static World NewGame(Data.GameData data, NewGameSettings s, GameOptions options = null)
        {
            // No explicit options: the defaults with the mods' option edits (Sim/Modding).
            if (options == null && data?.Mods != null) options = data.Mods.CreateOptions();
            var w = new World(data, options);
            w.Difficulty = s.Difficulty;
            w.LocalPlayer = s.LocalPlayer;
            var steps = new List<ISetupStep> { new CoreSetup() };
            steps.AddRange(w.SetupSteps);
            // Stable sort by order.
            var ordered = new List<(ISetupStep s, int i)>();
            for (int i = 0; i < steps.Count; i++) ordered.Add((steps[i], i));
            ordered.Sort((a, b) => a.s.Order != b.s.Order ? a.s.Order.CompareTo(b.s.Order) : a.i.CompareTo(b.i));
            steps = ordered.ConvertAll(x => x.s);
            foreach (var step in steps) step.Apply(w, s);
            return w;
        }

        /// <summary>Identity, clock and home airport (sim-core.md §3.1–3.2, §3.6 global part).</summary>
        /// <summary>First letter and every letter after a space or hyphen in upper case, all others lower.</summary>
        public static string Recapitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var b = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                bool start = i == 0 || s[i - 1] == ' ' || s[i - 1] == '-';
                b.Append(start ? char.ToUpperInvariant(s[i]) : char.ToLowerInvariant(s[i]));
            }
            return b.ToString();
        }

        sealed class CoreSetup : ISetupStep
        {
            public string Name => "core";
            public int Order => 0;

            public void Apply(World w, NewGameSettings s)
            {
                var txt = w.Data.StdText;
                for (int c = 0; c < Player.Count; c++)
                {
                    var p = w.Players[c];
                    p.Owner = s.Owners != null ? s.Owners[c] : (c == s.LocalPlayer ? Owner.Human : Owner.Computer);
                    // The split-flap wizard works in capitals; the game itself shows the names re-capitalised
                    // (rooms-and-ui-4 §3.1): first letter and letters after a space or hyphen upper, the rest lower.
                    p.Name = Recapitalize(s.PlayerNames[c] ?? txt?.Get("NewG", 1000 + 10 * c) ?? "Player " + (c + 1));
                    p.Airline = Recapitalize(s.AirlineNames[c] ?? txt?.Get("NewG", 1001 + 10 * c) ?? "Airline " + (c + 1));
                    p.Abbreviation = s.Abbreviations[c] ?? txt?.Get("NewG", 1002 + 10 * c) ?? "A" + (c + 1);
                    p.GameSpeed = p.IsComputer ? 3 : 0;
                }

                long start = s.StartTime ?? (w.Options.RandomStartDay ? new Core.TeakRand(s.Seed).Rand(365) * 86400L : 0);
                w.Clock.InitCalendar(start);
                w.Clock.Date = 0;
                w.Clock.Time = Core.GameClock.DayStart;
                w.Clock.Frozen = true;
                w.Clock.State = Core.DayState.Morning;

                w.HomeCity = s.HomeCity != 0 ? s.HomeCity : DefaultHome(w);
            }

            /// <summary>
            /// Default home airport by install language (routes-and-orders.md §7): London (English), Paris
            /// (French), Rome (Italian), Madrid (Spanish, Portuguese), Berlin (Dutch, German and all others).
            /// City names are matched in the install's own spelling.
            /// </summary>
            static int DefaultHome(World w)
            {
                var lang = w.Data.StdText?.Lang ?? Formats.TextResource.Language.English;
                string name;
                switch (lang)
                {
                    case Formats.TextResource.Language.English: name = "London"; break;
                    case Formats.TextResource.Language.French: name = "Paris"; break;
                    case Formats.TextResource.Language.Italian: name = "Roma"; break;
                    case Formats.TextResource.Language.Spanish:
                    case Formats.TextResource.Language.Portuguese: name = "Madrid"; break;
                    case Formats.TextResource.Language.Dutch: name = "Berlijn"; break;
                    default: name = "Berlin"; break;
                }
                var c = w.Data.CityByName(name) ?? w.Data.CityByName("Berlin");
                return c != null ? c.Id : w.Data.Cities.All[0].Id;
            }
        }
    }
}
