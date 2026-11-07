using OpenAT.Sim.Core;

namespace OpenAT.Sim
{
    /// <summary>
    /// Converts real time into simulation ticks with the original's speed rules (sim-core.md §1.4):
    /// 20 ticks per real second × the speed multiplier, at most one multiplied 50 ms quantum per frame
    /// (slow frames lose time instead of catching up).
    /// </summary>
    public sealed class SimDriver
    {
        public static readonly int[] Multipliers = { 1, 2, 4, 8, 150, 600 };
        public const int WarpLevel = 4, NightLevel = 5;

        readonly World _world;
        double _msCarry;

        /// <summary>Warp walking (Space in the hall).</summary>
        public bool Warp;
        /// <summary>Clock-click acceleration factor (×5 left / ×20 right button held on the clock).</summary>
        public int ClockClickFactor = 1;

        /// <summary>Set (e.g. by an event handler) to end the current batch after the running tick; cleared when used.
        /// The view uses it to write the autosave right after the tick that requested it.</summary>
        public bool EndBatch;

        public SimDriver(World world) => _world = world;

        /// <summary>The effective speed level for this frame.</summary>
        public int EffectiveLevel
        {
            get
            {
                var w = _world;
                var c = w.Clock;
                int d = 3;
                bool anyHuman = false;
                foreach (var p in w.Players)
                {
                    if (p.IsOut || p.IsComputer || p.CalledItADay) continue;
                    d = anyHuman ? System.Math.Min(d, p.GameSpeed) : p.GameSpeed;
                    anyHuman = true;
                }
                if (w.CallItADay || c.Time < GameClock.DayStart) d = NightLevel;
                if ((c.Time < GameClock.At(8) || c.Time >= GameClock.DayEnd) && c.State == DayState.Night) d = NightLevel;
                if (Warp) d = WarpLevel;
                return d;
            }
        }

        /// <summary>Ticks run by the last <see cref="Advance"/> call (the hall moves its camera once per frame that ran a tick).</summary>
        public int LastRun { get; private set; }

        /// <summary>Advances by real milliseconds; returns ticks run.</summary>
        public int Advance(double realMs)
        {
            LastRun = 0;
            _msCarry += realMs;
            // The epsilon keeps three paced 60 Hz frames (3 × 16.67 ms) from landing a hair under the 50 ms quantum.
            int n = (int)((_msCarry + 1e-6) / 50.0);
            if (n <= 0) return 0;
            _msCarry -= n * 50.0;
            if (n > 400) n = 400;

            int factor = Multipliers[EffectiveLevel] * System.Math.Max(1, ClockClickFactor);
            int ticks = System.Math.Min(n * factor, factor);
            int run = 0;
            for (int i = 0; i < ticks; i++)
            {
                if (_world.Paused) break;
                bool frozenBefore = _world.Clock.Frozen;
                int before = _world.Clock.Time;
                _world.Tick();
                run++;
                if (EndBatch) { EndBatch = false; break; }
                // A batch ends early at the 08:00 / 09:00 crossings and when the clock freezes.
                int after = _world.Clock.Time;
                if (!frozenBefore && _world.Clock.Frozen) break;
                if (before < GameClock.At(8) && after >= GameClock.At(8)) break;
                if (before < GameClock.DayStart && after >= GameClock.DayStart) break;
            }
            LastRun = run;
            return run;
        }
    }
}
