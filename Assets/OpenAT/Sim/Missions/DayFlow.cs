using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Missions
{
    // ====================================================================== day-flow commands

    /// <summary>
    /// "Call it a day" (rooms-and-ui-9 §6.4): the player's day is over. In single player (and once every
    /// active human has called it a day) the world's flag is set, which ends the working day at the next
    /// tick (<see cref="Systems.DaySystem"/>) and runs the clock at night speed.
    /// </summary>
    public sealed class CallItADayCommand : ICommand<World>
    {
        public readonly int Player;
        public CallItADayCommand(int player) => Player = player;

        public CommandResult Validate(World w)
        {
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("bad-player");
            if (w.Clock.State != DayState.Playing) return CommandResult.Fail("not-playing");
            // At exactly 09:00 the night state would fall straight back into the briefing (DaySystem).
            if (w.Clock.Time <= GameClock.DayStart) return CommandResult.Fail("before-nine");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            w.Players[Player].CalledItADay = true;
            bool all = true;
            foreach (var p in w.Players)
                if (!p.IsOut && p.IsHuman && !p.CalledItADay) all = false;
            if (all) w.CallItADay = true;
        }
    }

    /// <summary>
    /// Leaving the morning briefing (rooms-and-ui-6 §3.6 step 9, rooms-and-ui-9 §6.3): the clock runs
    /// again (DayState 2) and the figures stand in front of their offices in the hall.
    /// </summary>
    public sealed class StartWorkingDayCommand : ICommand<World>
    {
        public CommandResult Validate(World w) =>
            w.Clock.State == DayState.Morning ? CommandResult.Success : CommandResult.Fail("not-morning");

        public void Apply(World w)
        {
            w.StartWorkingDay();
            if (w.Hall == null) return;
            w.Hall.PlaceAfterBriefing();
            // Then the human figures walk to their own offices (sim-core §2.8 step 5; computer players get
            // their walk from the AI work cycle). Not when an office bomb is armed (the camera follows the victim).
            bool bomb = false;
            foreach (var p in w.Players) if (!p.IsOut && p.OfficeState == 1) bomb = true;
            foreach (var p in w.Players)
                if (!p.IsOut && p.Owner == Owner.Human && !bomb)
                    w.Hall.WalkToRoom(p.Index, OpenAT.Sim.Hall.RoomIds.OfficeA + 10 * p.Index);
        }
    }

    /// <summary>
    /// "Wait" on the room-occupied request (rooms-and-ui-9 §5.3 c, step 6): remember the room; the
    /// framework walks there as soon as it is free. Room 0 clears the wish.
    /// </summary>
    public sealed class WaitForRoomCommand : ICommand<World>
    {
        public readonly int Player, Room;
        public WaitForRoomCommand(int player, int room) { Player = player; Room = room; }

        public CommandResult Validate(World w)
        {
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count) return CommandResult.Fail("bad-player");
            return CommandResult.Success;
        }

        public void Apply(World w) => w.Hall.Players[Player].WaitForRoom = Room;
    }

    // ====================================================================== briefing log

    /// <summary>
    /// What the 09:00 morning review decided (sim-core.md §2.8 step 3): the mission report, every
    /// airline's review and who left the game, in publishing order. The boss's briefing dialogue and the
    /// short written briefing (MENU_BRIEFING) present it; the decisions themselves were already applied
    /// by <see cref="MorningReviewSystem"/>.
    /// </summary>
    public sealed class BriefingLog
    {
        public const string Key = "missions.briefing";

        public int Date = -1;
        public MissionReport Report;
        public readonly List<AirlineReviewed> Reviews = new List<AirlineReviewed>();
        public readonly List<PlayerOut> Outs = new List<PlayerOut>();
        public readonly List<MissionWon> Winners = new List<MissionWon>();
        public GameOver GameOver;
        public TakeoverExecuted Takeover;

        /// <summary>The briefing of the current day (or an empty one when the review has not run).</summary>
        public static BriefingLog Of(World w)
        {
            if (w.Extensions.TryGetValue(Key, out var v) && v is BriefingLog b) return b;
            b = new BriefingLog();
            w.Extensions[Key] = b;
            return b;
        }

        public AirlineReviewed ReviewOf(int player)
        {
            foreach (var r in Reviews) if (r.Player.Index == player) return r;
            return null;
        }

        /// <summary>Why an airline left the game this morning, or null.</summary>
        public OutReason? OutReasonOf(int player)
        {
            foreach (var o in Outs) if (o.Player.Index == player) return o.Reason;
            return null;
        }

        public bool Won(int player)
        {
            foreach (var m in Winners) if (m.Player.Index == player) return true;
            return false;
        }

        /// <summary>
        /// Something the player must hear (MENU_BRIEFING exclamation, rooms-and-ui-2 §4.27): a win, a
        /// loss, a takeover or an elimination. Then the short briefing cannot be skipped.
        /// </summary>
        public bool HasNews => Winners.Count > 0 || Outs.Count > 0 || GameOver != null || Takeover != null || (Report != null && Report.Ended);

        public bool HasNewsFor(int player) =>
            Won(player) || OutReasonOf(player) != null || (Takeover != null && (Takeover.Target.Index == player || Takeover.Acquirer.Index == player));

        internal void Reset(int date)
        {
            Date = date;
            Report = null;
            Reviews.Clear(); Outs.Clear(); Winners.Clear();
            GameOver = null;
            Takeover = null;
        }
    }

    /// <summary>
    /// Records the morning review's events into <see cref="BriefingLog"/>: subscribes just before the
    /// review (order 299) and unsubscribes right after it (order 301).
    /// </summary>
    public static class BriefingRecorder
    {
        static readonly object Lock = new object();
        static bool _registered;

        /// <summary>Adds the recorder to every world created from now on (idempotent).</summary>
        public static void Register()
        {
            lock (Lock)
            {
                if (_registered) return;
                _registered = true;
                World.SystemFactories.Add(() => new Open());
                World.SystemFactories.Add(() => new Close());
            }
        }

        /// <summary>Adds the recorder to an existing world (created before <see cref="Register"/>).</summary>
        public static void Ensure(World w)
        {
            if (w.Systems.Find(Open.SystemName) != null) return;
            w.Systems.Add(new Open());
            w.Systems.Add(new Close());
        }

        sealed class Open : SimSystem
        {
            public const string SystemName = "missions.briefing-open";
            public override string Name => SystemName;
            public override int Order => MorningReviewSystemOrder - 1;

            public override void OnMorning(World w)
            {
                var log = BriefingLog.Of(w);
                log.Reset(w.Clock.Date);
                var subs = new List<System.IDisposable>
                {
                    w.Events.Subscribe<MissionReport>(e => log.Report = e),
                    w.Events.Subscribe<AirlineReviewed>(e => log.Reviews.Add(e)),
                    w.Events.Subscribe<PlayerOut>(e => log.Outs.Add(e)),
                    w.Events.Subscribe<MissionWon>(e => log.Winners.Add(e)),
                    w.Events.Subscribe<GameOver>(e => log.GameOver = e),
                    w.Events.Subscribe<TakeoverExecuted>(e => log.Takeover = e),
                };
                w.Extensions[Key] = subs;
            }
        }

        sealed class Close : SimSystem
        {
            public override string Name => "missions.briefing-close";
            public override int Order => MorningReviewSystemOrder + 1;

            public override void OnMorning(World w)
            {
                if (!w.Extensions.TryGetValue(Key, out var v) || !(v is List<System.IDisposable> subs)) return;
                foreach (var s in subs) s.Dispose();
                w.Extensions.Remove(Key);
            }
        }

        const string Key = "missions.briefing-subscriptions";
        const int MorningReviewSystemOrder = 300;
    }
}
