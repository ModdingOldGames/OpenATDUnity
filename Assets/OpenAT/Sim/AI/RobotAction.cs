using System.Collections.Generic;
using OpenAT.Sim.Hall;

namespace OpenAT.Sim.AI
{
    /// <summary>What a robot goes to do (ai-robots.md §3.2, §5.2). Each action belongs to one room.</summary>
    public enum RobotAction
    {
        None = 0,
        /// <summary>The morning's first office visit ("start day"); same work as <see cref="Office"/>.</summary>
        StartDay,
        Office,
        Personnel,
        Bank,
        EmitShares,
        /// <summary>Kerosene dealer (Arab Air).</summary>
        Petrol,
        Sabotage,
        Kiosk,
        Mechanic,
        /// <summary>Museum visit looking for a plane with enough range ("needs a bigger plane").</summary>
        Museum,
        BuyUsedPlane,
        DutyFree,
        /// <summary>Airport manager's notice board (Aufsicht).</summary>
        Board,
        Nasa,
        Broker,
        Ricks,
        RouteBox,
        Designer,
        Security,
        /// <summary>Security office with the bolt cutter (cuts the power).</summary>
        Security2,
        Advertising,
        /// <summary>Agent 1: last-minute desk.</summary>
        LastMinute,
        /// <summary>Agent 2: travel agency.</summary>
        TravelAgency,
        /// <summary>Agent 3: cargo office.</summary>
        Cargo,
        Telescope,
        /// <summary>Placeholder of the original, never queued.</summary>
        RaiseMoney,
    }

    /// <summary>
    /// Tunable numbers of the robots (data, not code): the random pick table, work countdowns, walking
    /// fallback. Defaults are the original's values (ai-robots.md §4.2, §5.2). A copy lives on every
    /// world (<see cref="AiState.Tuning"/>) so a mod can change it per game.
    /// </summary>
    public sealed class RobotTuning
    {
        /// <summary>
        /// The random default pick (ai-robots.md §4.2): one entry per weight unit, 31 entries, in the
        /// original's table order (the order matters for the random stream).
        /// </summary>
        public List<RobotAction> DefaultPicks = new List<RobotAction>
        {
            RobotAction.Petrol, RobotAction.Broker, RobotAction.Sabotage, RobotAction.Sabotage,
            RobotAction.Office, RobotAction.Office, RobotAction.Bank, RobotAction.Bank,
            RobotAction.Personnel, RobotAction.Personnel, RobotAction.Kiosk, RobotAction.Mechanic,
            RobotAction.Mechanic, RobotAction.Museum, RobotAction.LastMinute, RobotAction.TravelAgency,
            RobotAction.Cargo, RobotAction.Cargo, RobotAction.DutyFree, RobotAction.Advertising,
            RobotAction.Advertising, RobotAction.Board, RobotAction.Telescope, RobotAction.Ricks,
            RobotAction.RouteBox, RobotAction.Nasa, RobotAction.Security, RobotAction.Security,
            RobotAction.Security, RobotAction.Designer, RobotAction.Designer,
        };

        /// <summary>Work countdown (ticks in the room) per action (ai-robots.md §5.2); 0 = leaves at once.</summary>
        public Dictionary<RobotAction, int> WorkTicks = new Dictionary<RobotAction, int>
        {
            [RobotAction.StartDay] = 100, [RobotAction.Office] = 100, [RobotAction.Personnel] = 100,
            [RobotAction.Broker] = 100, [RobotAction.Petrol] = 100, [RobotAction.Bank] = 100,
            [RobotAction.EmitShares] = 120, [RobotAction.Nasa] = 100, [RobotAction.Telescope] = 100,
            [RobotAction.Ricks] = 100, [RobotAction.Kiosk] = 100, [RobotAction.BuyUsedPlane] = 140,
            [RobotAction.DutyFree] = 100, [RobotAction.LastMinute] = 140, [RobotAction.TravelAgency] = 140,
            [RobotAction.Cargo] = 140, [RobotAction.Board] = 140, [RobotAction.RouteBox] = 140,
            [RobotAction.Security] = 140, [RobotAction.Security2] = 20, [RobotAction.Designer] = 140,
            [RobotAction.Advertising] = 140, [RobotAction.RaiseMoney] = 140,
            // Mechanic: no countdown is set (QUIRK Q11). Museum and sabotage depend on the outcome.
            [RobotAction.Mechanic] = 0, [RobotAction.Museum] = 100, [RobotAction.Sabotage] = 100,
        };

        /// <summary>Museum work when a big plane was eligible; sabotage work when a job was placed.</summary>
        public int MuseumBusyTicks = 200, SabotageBusyTicks = 200;

        /// <summary>Mechanic room time when <see cref="AiOptions.FixMechanicVisitTime"/> is on (the usual 100 ticks).</summary>
        public int MechanicFixedTicks = 100;

        /// <summary>Morning idle: <see cref="InitialIdleTicks"/> + <see cref="InitialIdlePerSlot"/> × slot (ai-robots.md §2.2).</summary>
        public int InitialIdleTicks = 10, InitialIdlePerSlot = 10;

        /// <summary>Ticks a robot stands still on the concourse before it re-plans (§3.2 step 4).</summary>
        public int StandStillLimit = 10;

        /// <summary>
        /// Walking time in ticks when the world has no hall (headless tests): a fixed estimate of the
        /// original's walk across the concourse at base speed. Running halves it.
        /// </summary>
        public int TeleportWalkTicks = 300;

        /// <summary>Guard against endless re-rolls in the planner (the original has none; never reached with the default table).</summary>
        public int MaxPlannerRolls = 1000;

        public int Work(RobotAction a) => WorkTicks.TryGetValue(a, out int t) ? t : 100;

        public RobotTuning Clone()
        {
            var c = (RobotTuning)MemberwiseClone();
            c.DefaultPicks = new List<RobotAction>(DefaultPicks);
            c.WorkTicks = new Dictionary<RobotAction, int>(WorkTicks);
            return c;
        }

        /// <summary>Room each action walks to (ai-robots.md §3.2 step 7).</summary>
        public static int RoomOf(World w, Player p, RobotAction a)
        {
            switch (a)
            {
                case RobotAction.StartDay:
                case RobotAction.Office: return RoomIds.OfficeA + 10 * p.Index;
                case RobotAction.Personnel: return RoomIds.PersonnelA + 10 * p.Index;
                case RobotAction.Bank:
                case RobotAction.EmitShares:
                case RobotAction.RaiseMoney: return RoomIds.Bank;
                case RobotAction.Petrol: return RoomIds.ArabAir;
                case RobotAction.Sabotage: return RoomIds.Sabotage;
                case RobotAction.Kiosk: return RoomIds.Kiosk;
                case RobotAction.Mechanic: return RoomIds.Werkstatt;
                case RobotAction.Museum:
                case RobotAction.BuyUsedPlane: return RoomIds.Museum;
                case RobotAction.DutyFree: return RoomIds.Shop1;
                case RobotAction.Board: return RoomIds.Aufsicht;
                case RobotAction.Nasa: return RoomIds.Nasa;
                case RobotAction.Broker: return RoomIds.Makler;
                case RobotAction.Ricks: return RoomIds.Ricks;
                case RobotAction.RouteBox: return RoomIds.RouteBox;
                case RobotAction.Designer: return RoomIds.Designer;
                case RobotAction.Security:
                case RobotAction.Security2: return RoomIds.Security;
                case RobotAction.Advertising: return RoomIds.Werbung;
                case RobotAction.LastMinute: return RoomIds.LastMinute;
                case RobotAction.TravelAgency: return RoomIds.Reisebuero;
                case RobotAction.Cargo: return RoomIds.Fracht;
                case RobotAction.Telescope: return w.Difficulty == Mission.Rocket ? RoomIds.Insel : RoomIds.Rushmore;
            }
            return 0;
        }

        /// <summary>Chit-chat rooms (filters of ai-robots.md §4.2 step 2).</summary>
        public static bool IsChitChat(RobotAction a) => a == RobotAction.Telescope || a == RobotAction.Ricks || a == RobotAction.Kiosk;
    }
}
