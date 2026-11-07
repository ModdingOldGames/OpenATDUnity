using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// Time hooks of the computer players. Orders are the original step numbers × 100 (docs/IMPLEMENTING.md):
    /// per tick after the hall (sim-core.md §2.3 step 3.3), per airline new day steps 9, 18 and 22
    /// (§2.7), the midnight sympathy drift (§2.6 step 28).
    /// </summary>
    public static class AiSystems
    {
        public static void Register(World w)
        {
            w.Systems.Add(new PumpSystem());
            w.Systems.Add(new CalledPlayerDecaySystem());
            w.Systems.Add(new RobotInitSystem());
            w.Systems.Add(new RoutePlanSystem());
            w.Systems.Add(new SympathyDriftSystem());
            w.Systems.Add(new FlightCountSystem());
            w.SetupSteps.Add(new AiSetup());

            // Walking through the hall: the hall reports arrivals and occupied rooms.
            w.Events.Subscribe<RoomRequested>(e =>
            {
                var p = w.Players[e.Player];
                if (p.IsComputer && !p.IsOut) w.Rules.Get<IRobotBrain>().Arrived(w, p, e.Room);
            });
            w.Events.Subscribe<HallRequest>(e =>
            {
                if (e.Player < 0 || e.Player >= Player.Count) return;
                var p = w.Players[e.Player];
                if (!p.IsComputer || p.IsOut) return;
                if (e.Kind == HallRequest.ThrownOut || e.Kind == HallRequest.ThrownOutBoard || e.Kind == HallRequest.Forbidden)
                    w.Rules.Get<IRobotBrain>().RoomBusy(w, p, e.Room);
            });
            w.Events.Subscribe<FlightBooked>(e =>
            {
                w.AI.FlightsToday[e.Player.Index]++;
                w.AI.FlightsTotal[e.Player.Index]++;
            });
        }

        /// <summary>
        /// The robot pump (sim-core.md §2.3 step 3.3, ai-robots.md §3.1): every tick while
        /// 09:00 &lt; time ≤ 18:00 and the clock runs; not gated by call-it-a-day. Every 10 game minutes,
        /// when nobody is inside a room, the hall's room locks are force-released (anti-deadlock).
        /// </summary>
        public sealed class PumpSystem : SimSystem
        {
            public const string SystemName = "ai.pump";
            public override string Name => SystemName;
            public override int Order => 330;

            public override void OnTick(World w)
            {
                var c = w.Clock;
                if (c.Frozen || c.Time <= GameClock.DayStart || c.Time > GameClock.DayEnd) return;
                if (!OpenAT.Sim.Tutorial.TutorialEngine.FiguresMayMove(w)) return;   // the tutorial holds the robots until step 1100
                var brain = w.Rules.Get<IRobotBrain>();
                foreach (var p in w.Players)
                    if (!p.IsOut) brain.Pump(w, p);
            }

            public override void OnMinute(World w)
            {
                var h = w.Hall;
                if (h == null || w.Clock.Minute % 10 != 0) return;
                int t = w.Clock.Time;
                if (t <= GameClock.DayStart || t > GameClock.DayEnd) return;
                foreach (var hp in h.Players) if (!hp.InHall) return;
                Array.Clear(h.RoomBusy, 0, h.RoomBusy.Length);
            }
        }

        /// <summary>Per-airline step 9: on odd days the "called the human" counter decays.</summary>
        public sealed class CalledPlayerDecaySystem : SimSystem
        {
            public override string Name => "ai.called-player-decay";
            public override int Order => 900;
            public override void OnPlayerNewDay(World w, Player p)
            {
                if ((w.Clock.Date & 1) != 0 && p.Robot.CalledPlayer > 0) p.Robot.CalledPlayer--;
            }
        }

        /// <summary>Per-airline step 18: robot re-initialisation ("RobotInit").</summary>
        public sealed class RobotInitSystem : SimSystem
        {
            public override string Name => "ai.robot-init";
            public override int Order => 1800;
            public override void OnNewDay(World w)
            {
                Array.Clear(w.AI.RoomOccupancy, 0, w.AI.RoomOccupancy.Length);
            }
            public override void OnPlayerNewDay(World w, Player p)
            {
                if (p.IsComputer) w.Rules.Get<IRobotBrain>().MorningInit(w, p);
            }
        }

        /// <summary>Per-airline step 22: nightly route planning of robots in route mode.</summary>
        public sealed class RoutePlanSystem : SimSystem
        {
            public override string Name => "ai.plan-routes";
            public override int Order => 2200;
            public override void OnPlayerNewDay(World w, Player p)
            {
                if (p.IsComputer && p.Robot.RouteMode) w.Rules.Get<IRobotBrain>().PlanRoutes(w, p);
            }
        }

        /// <summary>
        /// Midnight step 28 (sim-core.md §2.6): sympathy drops towards humans who earn much more today, then
        /// every |sympathy| &gt; 70 decays by 69/70.
        /// </summary>
        public sealed class SympathyDriftSystem : SimSystem
        {
            public override string Name => "ai.sympathy-drift";
            public override int Order => 2800;
            public override void OnNewDay(World w) => Drift(w);

            public static void Drift(World w)
            {
                for (int c = 0; c < Player.Count; c++)
                    for (int d = 0; d < Player.Count; d++)
                    {
                        var pc = w.Players[c]; var pd = w.Players[d];
                        long bc = pc.Today.Total, bd = pd.Today.Total;
                        int human = pd.IsComputer ? 0 : 1;
                        if (c != d && pc.Sympathy[d] < 25 && bd > 0)
                        {
                            if (pc.Sympathy[d] < -50 && bd > bc)
                            {
                                if (bd > bc + 1000000) pc.Sympathy[d] -= 10 * human;
                                if (bd > bc + 100000) pc.Sympathy[d] -= 2 * human;
                            }
                            else if (bd > bc * 2)
                            {
                                if (bd > bc + 1000000) pc.Sympathy[d] -= 5 * human;
                                if (bd > bc + 100000) pc.Sympathy[d] -= 1 * human;
                            }
                        }
                        if (Math.Abs(pc.Sympathy[d]) > 70) pc.Sympathy[d] = pc.Sympathy[d] * 69 / 70;
                    }
            }
        }

        /// <summary>Resets the per-day flight counters of <see cref="AiState"/> (reports only).</summary>
        public sealed class FlightCountSystem : SimSystem
        {
            public override string Name => "ai.flight-count";
            public override int Order => 50;
            public override void OnNewDay(World w) => Array.Clear(w.AI.FlightsToday, 0, w.AI.FlightsToday.Length);
        }

        /// <summary>
        /// New game (ai-robots.md §2.1): sympathy and cooperation for everyone, robot state cleared, then the
        /// first "RobotInit" (sim-core.md §3.6 global part, after the hall).
        /// </summary>
        public sealed class AiSetup : ISetupStep
        {
            public string Name => "ai";
            public int Order => 950;

            public void Apply(World w, NewGameSettings s)
            {
                w.AI.Seed = s.Seed;
                var brain = w.Rules.Get<IRobotBrain>();
                foreach (var p in w.Players) brain.NewGame(w, p);
                foreach (var p in w.Players)
                {
                    p.WalkRandom.SetSeed(w.Clock.Date + p.Index);
                    if (p.IsComputer) brain.MorningInit(w, p);
                }
            }
        }
    }
}
