using OpenAT.Sim;
using OpenAT.Sim.Data;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// A running game: the simulation world plus the real-time driver. Screens read the world and send
    /// commands; <see cref="GameApp"/> advances the driver every frame while a session exists.
    /// </summary>
    public sealed class GameSession
    {
        public readonly World World;
        public readonly SimDriver Driver;

        /// <summary>Set by screens that stop simulated time (options screen, modal pause).</summary>
        public bool HoldTime;

        public GameSession(World world)
        {
            World = world;
            // The player's OpenAT extras choices (options), then the command-line test switches.
            Extras.ApplyTo(world.Options);
            ApplyCommandLine(world.Options);
            Driver = new SimDriver(world);
        }

        public Player Local => World.Local;

        /// <summary>Starts a new game with the given settings using data from the install.</summary>
        public static GameSession NewGame(GameAssets assets, NewGameSettings settings, GameOptions options = null)
        {
            var data = assets.GameData;
            if (options == null && data?.Mods != null) options = data.Mods.CreateOptions();
            return new GameSession(World.NewGame(data, settings, ApplyCommandLine(options ?? new GameOptions())));
        }

        /// <summary>
        /// Test hook: <c>-openat-option Name[=true|false]</c> (repeatable) switches a boolean <see cref="GameOptions"/>
        /// field for new games, e.g. <c>-openat-option ComputerPhonePose</c>.
        /// </summary>
        static GameOptions ApplyCommandLine(GameOptions o)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-openat-option") continue;
                string[] kv = args[i + 1].Split('=');
                var f = typeof(GameOptions).GetField(kv[0]);
                if (f == null || f.FieldType != typeof(bool)) { UnityEngine.Debug.LogWarning("OpenAT: unknown option " + kv[0]); continue; }
                bool on = kv.Length < 2 || kv[1] != "false";
                f.SetValue(o, on);
                UnityEngine.Debug.Log("OpenAT: option " + kv[0] + " = " + on);
            }
            return o;
        }

        readonly FramePacer _pacer = new FramePacer();
        int _tickFrame = -1, _ticks;

        /// <summary>Test hook <c>-openat-nopacing</c>: feed the raw frame time to the driver (for before/after measurements).</summary>
        static readonly bool NoPacing = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-openat-nopacing") >= 0;

        /// <summary>Simulation ticks run in the current rendered frame (0 when time is held or the game paused).</summary>
        public int TicksThisFrame => _tickFrame == UnityEngine.Time.frameCount ? _ticks : 0;

        public void Update(float realSeconds)
        {
            if (HoldTime) return;
            double ms = realSeconds * 1000.0;
            if (!NoPacing) ms = _pacer.Pace(ms, DisplayPeriodMs(), TargetPeriodMs());
            _ticks = Driver.Advance(ms);
            _tickFrame = UnityEngine.Time.frameCount;
        }

        /// <summary>The display's frame period when vSync paces the frames, else 0.</summary>
        static double DisplayPeriodMs()
        {
            if (UnityEngine.QualitySettings.vSyncCount <= 0) return 0;
            double hz = UnityEngine.Screen.currentResolution.refreshRateRatio.value;
            return hz > 1 ? 1000.0 * UnityEngine.QualitySettings.vSyncCount / hz : 0;
        }

        static double TargetPeriodMs()
        {
            int fps = UnityEngine.Application.targetFrameRate;
            return fps > 0 ? 1000.0 / fps : 0;
        }
    }
}
