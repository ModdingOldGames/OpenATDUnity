using System;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// Brain state of one computer airline (ai-robots.md §1–§6): action queue, timers, planning flags.
    /// Lives on <see cref="Player.Robot"/>; humans have one too (all zero).
    /// </summary>
    public sealed class RobotState
    {
        public const int QueueSize = 5;

        /// <summary>Slot 0 = the action being walked to / executed, 1..4 = queued.</summary>
        public readonly RobotAction[] Queue = new RobotAction[QueueSize];
        /// <summary>Id of the last started action ("no immediate repeat" rule).</summary>
        public RobotAction LastAction;
        /// <summary>Ticks left in the current room (§5).</summary>
        public int WorkCountdown;
        /// <summary>Ticks standing still on the concourse (§3.2 step 4).</summary>
        public int StandStill;
        /// <summary>Stun ticks (toilet, items); set by other domains through <see cref="Stun"/>.</summary>
        public int Stunned;
        public bool RunningToToilet;

        /// <summary>Last visit times (time of day in clock units, −1 = not today). Compared like the original (unsigned, Q1/Q2).</summary>
        public int TimeOffice = -1, TimePersonnel = -1, TimeBoard = -1, TimeAgents = -1;

        /// <summary>"DoRoutes": full route mode (nightly route planner, no agent visits).</summary>
        public bool RouteMode;
        /// <summary>"WantToDoRoutes": still order mode, but buys routes and route ads.</summary>
        public bool WantsRoutes;
        public bool SavingForPlane, SavingForRocket;
        /// <summary>"BuyBigPlane": km range needed for the longest owned leg, 0 = none.</summary>
        public int NeedsBiggerPlane;
        /// <summary>"Recently called the human" counter (2 after a call, −1 every odd day).</summary>
        public int CalledPlayer;
        // ---- walking / room presence (hall-independent bookkeeping)
        /// <summary>Room the robot is in according to the AI (0 = concourse).</summary>
        public int InRoom;
        /// <summary>Room the robot is walking to (0 = none).</summary>
        public int WalkTarget;
        /// <summary>Countdown walk (no hall, or after call-it-a-day); −1 = walking through the hall.</summary>
        public int WalkTicksLeft = -1;

        // ---- duplicate guard (§5.1)
        internal int LastExecDate = -1, LastExecTime = -1;
        internal RobotAction LastExecAction;

        // ---- statistics (tests, reports)
        public int ActionsExecuted;
        public readonly int[] ActionCounts = new int[Enum.GetValues(typeof(RobotAction)).Length];

        public RobotAction Current { get => Queue[0]; set => Queue[0] = value; }
        public bool IsWalking => WalkTarget != 0;

        public void ClearQueue() { for (int i = 0; i < QueueSize; i++) Queue[i] = RobotAction.None; }

        /// <summary>Stuns the robot (it does nothing for <paramref name="ticks"/>; the current action is dropped afterwards).</summary>
        public void Stun(int ticks) { if (ticks > Stunned) Stunned = ticks; }

        public override string ToString() => $"[{string.Join(",", Queue)}] last {LastAction} work {WorkCountdown} room {InRoom} walk {WalkTarget}{(RouteMode ? " routes" : WantsRoutes ? " wants-routes" : "")}";
    }

    /// <summary>
    /// Fixes for original AI bugs (ai-robots.md §10), on by default; all off = original behaviour. Kept here (not in
    /// <see cref="GameOptions"/>) so the AI domain stays self-contained; listed in docs/QUIRKS.md.
    /// </summary>
    public sealed class AiOptions
    {
        /// <summary>Q1: office timer compares "now − last visit &gt; 3 h" as intended.</summary>
        public bool FixOfficeTimer = true;
        /// <summary>Q2: late-day board clause compares "now − last visit &gt; 1 h" as intended.</summary>
        public bool FixLateBoardTimer = true;
        /// <summary>Q3: a leg counts as losing only when the price is below the base cost.</summary>
        public bool FixRouteLossTest = true;
        /// <summary>Q13: the big-plane museum visit buys at most one plane and respects the plane caps.</summary>
        public bool FixMuseumMultiBuy = true;
        /// <summary>Q18: the fill loop divides demand by (planes on leg + 1) and counts the planes it assigns.</summary>
        public bool FixDemandComparison = true;
        /// <summary>Q19: competitors are counted with each owner's own "recently flown" value.</summary>
        public bool FixCompetitorCount = true;
        /// <summary>Q22: the "not saving for the rocket" guard covers all plane-saving cases.</summary>
        public bool FixSavingPrecedence = true;
        /// <summary>Q24: the nightly route wipe clears future route entries (the original clears future contract entries).</summary>
        public bool FixRouteWipe = true;
        /// <summary>Q11: the mechanic visit keeps the robot in the room (<see cref="RobotTuning.MechanicFixedTicks"/>) instead of leaving at once.</summary>
        public bool FixMechanicVisitTime = true;
        /// <summary>Q15: cargo pass 2 moves the plane's free point on after every accepted job.</summary>
        public bool FixCargoFreePoint = true;
        /// <summary>Q16: order entries also set a plane's last city in the route survey (dead branch in the original).</summary>
        public bool FixRouteSurveyOrders = true;
        /// <summary>Q17: a rescue flight added late in the day starts on the next day instead of keeping an hour ≥ 24.</summary>
        public bool FixRescueHourWrap = true;
        /// <summary>Branch bids test who leads the branch slip (the original reads the route slip of the same row).</summary>
        public bool FixBranchBidTable = true;
        /// <summary>Route box: "rent per passenger" uses the route table's licence fee (the original reads the robot's own, empty licence record).</summary>
        public bool FixRouteBoxRentTable = true;

        public AiOptions Clone() => (AiOptions)MemberwiseClone();
    }

    /// <summary>World-level state of the AI domain.</summary>
    public sealed class AiState
    {
        public RobotTuning Tuning = new RobotTuning();
        public AiOptions Options = new AiOptions();
        /// <summary>Seed for the places where the original used the wall clock (Q20); from <see cref="NewGameSettings.Seed"/>.</summary>
        public int Seed = 12345;
        /// <summary>Predefined designer plane of the mission (DESIGNER_BUY); set by the integrator from the install. Null = none.</summary>
        public PlaneDesign MissionDesign;
        /// <summary>Room occupancy kept by the AI when the world has no hall (index = room id).</summary>
        public readonly int[] RoomOccupancy = new int[256];
        /// <summary>Optional trace of robot decisions (tests, CLI). Null = off.</summary>
        public Action<string> Trace;

        /// <summary>Flights booked today per airline (reset at midnight).</summary>
        public readonly int[] FlightsToday = new int[Player.Count];
        public readonly int[] FlightsTotal = new int[Player.Count];

        internal void Log(World w, Player p, string msg)
        {
            Trace?.Invoke($"d{w.Clock.Date} {w.Clock.ClockText} P{p.Index} {msg}");
        }
    }
}
