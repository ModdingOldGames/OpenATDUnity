using OpenAT.Formats;
using OpenAT.Sim;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// The OpenAT improvements the player can switch off (options, "OpenAT extras"). Each is a boolean
    /// <see cref="GameOptions"/> field; the choice is kept in PlayerPrefs and applied to every game that starts or
    /// is loaded, and to the running game when it changes. Off gives the original's interface for that part.
    /// </summary>
    public static class Extras
    {
        public sealed class Switch
        {
            public readonly string Field, En, De;
            public Switch(string field, string en, string de) { Field = field; En = en; De = de; }
            public string Label(TextResource.Language l) => l == TextResource.Language.German ? De : En;
        }

        /// <summary>Labels fit the split-flap board (at most 16 characters).</summary>
        public static readonly Switch[] All =
        {
            new Switch(nameof(GameOptions.PlannerEnhancements), "PLANNER TOOLS", "PLANER-EXTRAS"),
            new Switch(nameof(GameOptions.PlannerHiResFilofax), "HI-RES FILOFAX", "FILOFAX HD"),
            new Switch(nameof(GameOptions.PlannerHiResLaptop), "HI-RES LAPTOP", "LAPTOP HD"),
            new Switch(nameof(GameOptions.GamepadSupport), "GAMEPAD", "GAMEPAD"),
            new Switch(nameof(GameOptions.SkipStartWalk), "SKIP START WALK", "STARTGANG WEG"),
        };

        const string Prefix = "OpenAT.Extra.";

        public static bool Get(string field) => PlayerPrefs.GetInt(Prefix + field, Default(field) ? 1 : 0) != 0;

        static bool Default(string field) => (bool)typeof(GameOptions).GetField(field).GetValue(new GameOptions());

        /// <summary>Stores a choice and switches it in the running game, if any.</summary>
        public static void Set(GameApp app, string field, bool on)
        {
            PlayerPrefs.SetInt(Prefix + field, on ? 1 : 0);
            PlayerPrefs.Save();
            app.Session?.World.Execute(new SetOptionCommand(field, on));
        }

        /// <summary>The stored choices onto a game's options (a new or loaded game, before it runs).</summary>
        public static void ApplyTo(GameOptions o)
        {
            foreach (var s in All) typeof(GameOptions).GetField(s.Field).SetValue(o, Get(s.Field));
        }
    }
}
