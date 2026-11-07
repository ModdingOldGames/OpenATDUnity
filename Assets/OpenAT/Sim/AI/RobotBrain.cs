using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Hall;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// The whole computer-player behaviour behind one rule, so a mod can replace the AI completely:
    /// <c>world.Rules.Set&lt;IRobotBrain&gt;(new MyBrain())</c>. The systems in <see cref="AiSystems"/> only
    /// call these hooks. The default is <see cref="OriginalRobotBrain"/> (ai-robots.md).
    /// </summary>
    public interface IRobotBrain : IRule
    {
        /// <summary>New game, per airline (§2.1): sympathy, cooperation, cleared robot state.</summary>
        void NewGame(World w, Player p);
        /// <summary>Per-airline new day step 18 ("RobotInit", §2.2), also once at game start.</summary>
        void MorningInit(World w, Player p);
        /// <summary>Every tick 09:00 &lt; t ≤ 18:00 for every airline that is not out (§3.1–3.2).</summary>
        void Pump(World w, Player p);
        /// <summary>The robot's figure entered a room (§3.3 "Arrival").</summary>
        void Arrived(World w, Player p, int room);
        /// <summary>The robot found its target room occupied (§3.3 "Room busy on arrival").</summary>
        void RoomBusy(World w, Player p, int room);
        /// <summary>Nightly route planning and ticket prices for robots in route mode (§6.2, per-airline step 22).</summary>
        void PlanRoutes(World w, Player p);
    }

    /// <summary>The original robots (ai-robots.md).</summary>
    public class OriginalRobotBrain : IRobotBrain
    {
        public virtual void NewGame(World w, Player p)
        {
            for (int d = 0; d < Player.Count; d++)
            {
                p.Sympathy[d] = d == (p.Index + 1) % Player.Count ? 60 : 50;
                p.Cooperates[d] = p.IsComputer && d != p.Index && w.Players[d].IsComputer;
            }
            p.Robot = new RobotState();
        }

        public virtual void MorningInit(World w, Player p)
        {
            if (!p.IsComputer || p.IsOut) return;
            var s = p.Robot;
            var f = w.Rules.Get<IRobotFeatures>();
            var t = w.AI.Tuning;
            int date = w.Clock.Date, slot = p.Index;

            s.WorkCountdown = t.InitialIdleTicks + t.InitialIdlePerSlot * slot;
            s.ClearQueue();
            s.StandStill = 0;
            s.Stunned = 0;
            // Everybody starts the day in the airport (sim-core.md §1.3): a robot still inside yesterday's room leaves it.
            if (w.Hall != null && !w.Hall.Players[p.Index].InHall) w.Execute(new ReturnToHallCommand(p.Index));
            s.InRoom = 0;
            s.WalkTarget = 0;
            s.WalkTicksLeft = -1;
            s.Queue[1] = RobotAction.StartDay;
            s.Queue[2] = RobotAction.Personnel;
            if (!s.RouteMode)
            {
                bool freightSecond = f.Has(w, RobotFeature.MuchFreight) || (((slot + date) & 3) == 0 && f.Has(w, RobotFeature.Freight));
                if (f.Has(w, RobotFeature.TravelHolding))
                {
                    if (((slot + date) & 1) != 0)
                    {
                        s.Queue[3] = RobotAction.LastMinute;
                        s.Queue[4] = RobotAction.TravelAgency;
                        if (freightSecond) s.Queue[2] = RobotAction.Cargo;
                    }
                    else
                    {
                        s.Queue[3] = RobotAction.TravelAgency;
                        s.Queue[4] = RobotAction.LastMinute;
                        if (f.Has(w, RobotFeature.RunFreight)) s.Queue[1] = RobotAction.Cargo;
                        else if (freightSecond) s.Queue[2] = RobotAction.Cargo;
                    }
                }
                else if (f.Has(w, RobotFeature.Freight)) s.Queue[3] = RobotAction.Cargo;
            }
            s.TimeOffice = s.TimePersonnel = s.TimeBoard = s.TimeAgents = -1;
            RobotPlanner.Plan(w, p);
        }

        public virtual void Pump(World w, Player p)
        {
            var s = p.Robot;
            if (s.Stunned > 0)
            {
                if (--s.Stunned == 0 && !s.RunningToToilet && p.IsComputer)
                {
                    if (s.Queue[0] == RobotAction.None && s.Queue[1] == RobotAction.None) RobotPlanner.Plan(w, p);
                    s.LastAction = RobotAction.None;
                    s.Current = RobotAction.None;
                    s.WalkTarget = 0;
                    s.WalkTicksLeft = -1;
                }
                return;
            }
            if (!p.IsComputer) return;

            bool concourse = s.InRoom == 0;
            bool countdownWalk = s.WalkTicksLeft >= 0;

            // §3.2 step 4: stuck detection.
            bool idle = concourse && !IsMoving(w, p, s);
            if (idle || (w.CallItADay && s.WalkTicksLeft <= 0))
            {
                if (s.StandStill > w.AI.Tuning.StandStillLimit)
                {
                    s.Current = RobotAction.None;
                    s.WalkTarget = 0;
                    s.WalkTicksLeft = -1;
                    RobotPlanner.Plan(w, p);
                }
                else if (s.WorkCountdown <= 0 && (w.CallItADay || FigureIdle(w, p))) s.StandStill++;
            }
            else s.StandStill = 0;

            // Step 5: still on the way.
            if (s.Current != RobotAction.None && concourse)
            {
                if (countdownWalk && s.WalkTicksLeft > 0 && --s.WalkTicksLeft == 0) Arrived(w, p, s.WalkTarget);
                return;
            }

            // Step 6: busy in the room.
            if (s.WorkCountdown > 0) { s.WorkCountdown--; return; }

            // Step 7: next action.
            if (s.Current == RobotAction.None && s.Queue[1] != RobotAction.None)
            {
                for (int c = 0; c < RobotState.QueueSize - 1; c++) s.Queue[c] = s.Queue[c + 1];
                s.Queue[RobotState.QueueSize - 1] = RobotAction.None;
                if (!concourse) LeaveRoom(w, p);

                var lastLast = s.LastAction;
                s.LastAction = s.Current;
                var f = w.Rules.Get<IRobotFeatures>();
                int minute = w.Clock.Minute;
                bool notBox = s.Current != RobotAction.RouteBox;
                bool run = notBox && minute > 0 && ((p.Index == 2 && (minute / 5) % 2 == 0) || f.Has(w, RobotFeature.AllRun));
                if (!StartWalk(w, p, RobotTuning.RoomOf(w, p, s.Current), run))
                {
                    w.AI.Log(w, p, "walk refused: " + s.Current);
                    s.LastAction = lastLast;
                    s.Current = RobotAction.None;
                    return;
                }
                w.AI.Log(w, p, "walks to " + s.Current);
            }

            // Step 8.
            if (s.Queue[0] == RobotAction.None && s.Queue[1] == RobotAction.None) RobotPlanner.Plan(w, p);
        }

        public virtual void Arrived(World w, Player p, int room)
        {
            var s = p.Robot;
            if (!p.IsComputer) return;
            bool viaHall = w.Hall != null && s.WalkTicksLeft < 0;
            s.WalkTarget = 0;
            s.WalkTicksLeft = -1;
            if (!viaHall && RobotRooms.IsShared(room) && w.AI.RoomOccupancy[room] > 0)
            {
                RoomBusy(w, p, room);
                return;
            }
            s.InRoom = room;
            if (room > 0 && room < w.AI.RoomOccupancy.Length) w.AI.RoomOccupancy[room]++;
            if (s.Current == RobotAction.None) { s.WorkCountdown = 2; return; }
            RobotExecutor.Execute(w, p);
        }

        public virtual void RoomBusy(World w, Player p, int room)
        {
            var s = p.Robot;
            if (!p.IsComputer || s.Current == RobotAction.None) return;
            var blocked = s.Current;
            var next = s.Queue[1];
            if (next == RobotAction.None)
                next = w.Rules.Get<IRivalActions>().OfficeDestroyed(w, p) ? RobotAction.Personnel : RobotAction.Office;
            // [—, next, blocked, rest…]
            for (int c = RobotState.QueueSize - 1; c >= 3; c--) s.Queue[c] = s.Queue[c - 1];
            s.Queue[2] = blocked;
            s.Queue[1] = next;
            s.Queue[0] = RobotAction.None;
            s.WalkTarget = 0;
            s.WalkTicksLeft = -1;
            w.AI.Log(w, p, "room busy: " + blocked);
        }

        public virtual void PlanRoutes(World w, Player p) => RobotRoutePlanner.PlanRoutes(w, p);

        // ------------------------------------------------------------------ walking (§3.3)

        static bool IsMoving(World w, Player p, RobotState s)
        {
            if (s.WalkTicksLeft > 0) return true;
            if (w.Hall == null || s.WalkTicksLeft >= 0) return false;
            return w.Hall.Players[p.Index].WalkActive;
        }

        static bool FigureIdle(World w, Player p)
        {
            if (w.Hall == null) return true;
            var me = w.Hall.PlayerPerson(p.Index);
            return me == null || me.StatePar == 0;
        }

        /// <summary>Leaves the current room (back to the concourse).</summary>
        public static void LeaveRoom(World w, Player p)
        {
            var s = p.Robot;
            if (s.InRoom > 0 && s.InRoom < w.AI.RoomOccupancy.Length && w.AI.RoomOccupancy[s.InRoom] > 0) w.AI.RoomOccupancy[s.InRoom]--;
            s.InRoom = 0;
            if (w.Hall != null && !w.Hall.Players[p.Index].InHall) w.Execute(new ReturnToHallCommand(p.Index));
        }

        /// <summary>
        /// Starts the walk to a room (§3.3): refused for closed or missing rooms. With a hall the figure walks
        /// with <see cref="WalkToRoomCommand"/> and the action runs on <see cref="RoomRequested"/>; without a
        /// hall (headless) or after call-it-a-day a countdown replaces the walk.
        /// </summary>
        public static bool StartWalk(World w, Player p, int room, bool run)
        {
            var s = p.Robot;
            if (room <= 0 || RobotRooms.IsClosed(w, room) || !RobotRooms.Exists(w, room)) return false;
            s.WalkTarget = room;
            if (w.Hall != null && !w.CallItADay)
            {
                var r = w.Execute(new WalkToRoomCommand(p.Index, room, run));
                if (!r.Ok) { s.WalkTarget = 0; return false; }
                var me = w.Hall.PlayerPerson(p.Index);
                if (me != null) me.Running = run;
                s.WalkTicksLeft = -1;
                return true;
            }
            int ticks = w.AI.Tuning.TeleportWalkTicks;
            if (w.Hall != null)
            {
                // After "call it a day": the Manhattan distance to the door in pixels (§3.3).
                var me = w.Hall.PlayerPerson(p.Index);
                var door = w.Hall.RandomRune(RuneId.ToShop, room, w.PersonRandCreate);
                if (me != null && door != null)
                {
                    var a = me.Position; var b = door.Value;
                    int d = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                    if ((a.Y >= 4000) != (b.Y >= 4000)) d -= 5000;
                    ticks = Math.Max(1, d);
                }
            }
            else if (run) ticks /= 2;
            s.WalkTicksLeft = Math.Max(1, ticks);
            return true;
        }
    }

    /// <summary>Room rules the robots need without a hall (sim-core.md §1.5, ai-robots.md §3.3).</summary>
    public static class RobotRooms
    {
        /// <summary>Opening hours (the hall's own check when a hall exists) and the security-office power cut.</summary>
        public static bool IsClosed(World w, int room)
        {
            if (room == RoomIds.Security && !w.Rules.Get<IRivalActions>().SecurityOfficeOpen(w)) return true;
            if (w.Hall != null) return w.Hall.ClosedMenu(room) >= 0;
            int t = w.Clock.Time, wd = w.Clock.Weekday;
            switch (room)
            {
                case RoomIds.Shop1: return t < OpeningHours.DutyOpen || ((wd == 5 || wd == 6) && t > OpeningHours.DutyClose);
                case RoomIds.ArabAir: return t < OpeningHours.ArabOpen || wd == 6;
                case RoomIds.LastMinute: return t > OpeningHours.LastClose || wd == 5;
                case RoomIds.Museum: return t < OpeningHours.MuseOpen || wd == 5 || wd == 6;
                case RoomIds.Reisebuero: return t > OpeningHours.ReisClose;
                case RoomIds.Makler: return t > OpeningHours.MaklClose;
                case RoomIds.Werbung:
                    int d = w.Difficulty;
                    return (d >= 2 || d == Mission.FreeGame) && (t < OpeningHours.WerbOpen || wd == 5 || wd == 6);
            }
            return false;
        }

        /// <summary>The room exists on this airport (hall runes; without a hall: by mission).</summary>
        public static bool Exists(World w, int room)
        {
            if (w.Hall != null) return w.Hall.Layout.HasRune(RuneId.ToShop, room);
            switch (room)
            {
                case RoomIds.Nasa: return w.Difficulty == Mission.Rocket || w.Difficulty == Mission.AddOnLast;
                case RoomIds.Insel: return w.Difficulty == Mission.Rocket;
                case RoomIds.LastMinute:
                case RoomIds.Reisebuero: return w.TravelHolding;
            }
            return true;
        }

        /// <summary>Shared rooms can be occupied by one airline at a time (offices, personnel rooms and toilets cannot).</summary>
        public static bool IsShared(int room) => room >= 80 && room != RoomIds.WcF && room != RoomIds.WcM;
    }
}
