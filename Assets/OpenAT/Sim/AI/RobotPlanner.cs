using OpenAT.Sim.Core;
using OpenAT.Sim.Hall;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// "RobotPlan" (ai-robots.md §4): when both the current and the next queue slot are empty, fill slot 1
    /// (sometimes 2) from the priority chain, else from the weighted random table with its filters. All
    /// rolls use the airline's walk generator (reseeded with Date + slot every night).
    /// </summary>
    public static class RobotPlanner
    {
        const int Hour = GameClock.UnitsPerHour;

        public static void Plan(World w, Player p)
        {
            if (!p.IsComputer || p.IsOut) return;
            var s = p.Robot;
            if (s.Queue[0] != RobotAction.None || s.Queue[1] != RobotAction.None) return;

            var ctx = new Ctx(w, p);
            var pick = PriorityChain(ctx);
            bool fromChain = pick != RobotAction.None;
            int guard = w.AI.Tuning.MaxPlannerRolls;

            while (true)
            {
                if (!fromChain) pick = RandomPick(ctx);
                fromChain = false;
                if (--guard < 0) { pick = RobotAction.Personnel; break; }

                // §4.3: equal to the last action → re-roll with the reduced filter set.
                bool restart = false;
                while (pick == s.LastAction && !restart)
                {
                    if (--guard < 0) break;
                    pick = ReducedPick(ctx, out restart);
                }
                if (restart) continue;
                if (guard < 0) { pick = RobotAction.Personnel; break; }
                if (RejectedAfterwards(ctx, ref pick)) continue;
                break;
            }

            // Follow-ups (§4.3).
            var f = ctx.F;
            if (w.Clock.Date > 1 && s.Queue[2] == RobotAction.None && p.WalkRandom.Rand(2) == 0)
            {
                if (p.Index == 0 && f.Has(w, RobotFeature.Sabotage) && (pick == RobotAction.Kiosk || pick == RobotAction.DutyFree || pick == RobotAction.Bank))
                    s.Queue[2] = RobotAction.Sabotage;
                if (p.Index == 1 && (pick == RobotAction.Kiosk || pick == RobotAction.DutyFree || pick == RobotAction.Sabotage))
                    s.Queue[2] = RobotAction.Bank;
            }
            if (f.Has(w, RobotFeature.MuchFreight) && (pick == RobotAction.LastMinute || pick == RobotAction.TravelAgency))
            {
                s.Queue[2] = pick;
                pick = RobotAction.Cargo;
            }
            if (f.Has(w, RobotFeature.FreeFreight) && (pick == RobotAction.LastMinute || pick == RobotAction.Museum))
            {
                s.Queue[2] = pick;
                pick = RobotAction.Cargo;
            }
            s.Queue[1] = pick;
        }

        sealed class Ctx
        {
            public readonly World W;
            public readonly Player P;
            public readonly RobotState S;
            public readonly IRobotFeatures F;
            public readonly IRivalActions R;
            public Ctx(World w, Player p) { W = w; P = p; S = p.Robot; F = w.Rules.Get<IRobotFeatures>(); R = w.Rules.Get<IRivalActions>(); }
            public bool Has(RobotFeature f) => F.Has(W, f);
            public int Roll(int n) => P.WalkRandom.Rand(n);
            public RobotAction Draw() { var t = W.AI.Tuning.DefaultPicks; return t[P.WalkRandom.Rand(t.Count)]; }
        }

        /// <summary>Unsigned "a − b" of the original clock arithmetic (Q1/Q2).</summary>
        static uint Diff(int a, int b) => unchecked((uint)(a - b));

        /// <summary>§4.1 priority chain; <see cref="RobotAction.None"/> = fall through to the random pick.</summary>
        static RobotAction PriorityChain(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            int now = w.Clock.Time, wd = w.Clock.Weekday, date = w.Clock.Date;

            if (p.Money > 5000000 && p.Image > -100 && !s.SavingForRocket && now > OpeningHours.MuseOpen && wd != 5 && wd != 6 && !p.VisitedMuseumToday)
                return RobotAction.BuyUsedPlane;
            if (c.Has(RobotFeature.GoodPlanes) && date % 4 == p.Index && w.Clock.Hour == 9 && w.Clock.Minute <= 35)
                return RobotAction.Mechanic;
            if (s.WantsRoutes && p.Image < 150 && c.Roll(4) == 0)
                return RobotAction.Advertising;
            if (s.NeedsBiggerPlane > 0 && p.Money > 100000 && c.Roll(4) == 0 && !c.Has(RobotFeature.HoardMoney))
                return RobotAction.Museum;
            if (date + 1 >= p.Index && p.MaxShares - p.SharesIssued >= 25000 && (p.Money < 3000000 || p.Credit > 1000000 || c.Has(RobotFeature.EmitMuchShares)))
                return RobotAction.EmitShares;
            if (Diff(now, s.TimePersonnel) > 3 * Hour)
                return RobotAction.Personnel;
            bool lateBoard = w.AI.Options.FixLateBoardTimer ? Diff(now, s.TimeBoard) > 1 * Hour : Diff(s.TimeBoard, now) > 1 * Hour;
            if (Diff(now, s.TimeBoard) > 3 * Hour || (lateBoard && now > 16 * Hour && c.Has(RobotFeature.LateBoard)))
                return RobotAction.Board;
            if (Diff(now, s.TimeAgents) > 90000 && !s.RouteMode)
            {
                s.Queue[2] = RobotAction.TravelAgency;
                return RobotAction.LastMinute;
            }
            bool officeDue = w.AI.Options.FixOfficeTimer ? Diff(now, s.TimeOffice) > 3 * Hour : Diff(s.TimeOffice, now) > 3 * Hour;
            if (officeDue && !c.R.OfficeDestroyed(w, p))
                return RobotAction.Office;
            return RobotAction.None;
        }

        /// <summary>§4.2: weighted draw with the full filter set, repeated until a candidate survives.</summary>
        static RobotAction RandomPick(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            int date = w.Clock.Date;
            int guard = w.AI.Tuning.MaxPlannerRolls;
            while (guard-- > 0)
            {
                RobotAction a;
                // Steps 1–5, repeated until different from the last action (step 6).
                while (true)
                {
                    if (guard-- < 0) return RobotAction.Personnel;
                    a = c.Draw();
                    if (a == RobotAction.Nasa && !c.Has(RobotFeature.Nasa)) continue;
                    if (RobotTuning.IsChitChat(a))
                    {
                        if (c.Has(RobotFeature.OftenMechanic)) a = RobotAction.Mechanic;
                        else if (c.Has(RobotFeature.ExtremeSabotage)) a = RobotAction.Sabotage;
                        else if (c.Has(RobotFeature.NoChitChat)) continue;
                    }
                    if (p.Index == 0 && date > 1 && c.Roll(4) == 0) a = RobotAction.Sabotage;
                    if (p.Index == 1 && date > 1 && c.Roll(3) == 0) a = RobotAction.Bank;
                    if (a == RobotAction.Museum && PlaneCapReached(c, false)) continue;
                    if (a == RobotAction.Broker && !c.Has(RobotFeature.Broker)) continue;
                    if (a == RobotAction.Petrol && !c.Has(RobotFeature.PetrolAir)) continue;
                    if (a != s.LastAction) break;
                }
                // Steps 7–9.
                if (a == RobotAction.Office && c.R.LaptopQuality(w, p) > 2) continue;
                if (IsAgent12(a) && s.RouteMode && p.HasFlownRoutes && (w.Clock.Time & 15) != 0) continue;
                if (!PassesWorldFilters(c, a)) continue;
                return a;
            }
            return RobotAction.Personnel;
        }

        /// <summary>The reduced re-roll of §4.3 (no slot overrides, plane caps, broker/petrol or laptop filters).</summary>
        static RobotAction ReducedPick(Ctx c, out bool restartFull)
        {
            restartFull = false;
            int guard = c.W.AI.Tuning.MaxPlannerRolls;
            while (guard-- > 0)
            {
                var a = c.Draw();
                if (RobotTuning.IsChitChat(a))
                {
                    if (c.Has(RobotFeature.OftenMechanic)) a = RobotAction.Mechanic;
                    else if (c.Has(RobotFeature.NoChitChat)) { restartFull = true; return a; }
                }
                if (a == RobotAction.Nasa && !c.Has(RobotFeature.Nasa)) continue;
                if (!PassesWorldFilters(c, a)) continue;
                return a;
            }
            return RobotAction.Personnel;
        }

        /// <summary>Filters 8 (travel holding) and 9 (features, rooms on this airport).</summary>
        static bool PassesWorldFilters(Ctx c, RobotAction a)
        {
            var w = c.W;
            if (IsAgent12(a) && !c.Has(RobotFeature.TravelHolding)) return false;
            if (a == RobotAction.Cargo && !c.Has(RobotFeature.Freight)) return false;
            if (a == RobotAction.Advertising && !c.Has(RobotFeature.Advertising)) return false;
            if (a == RobotAction.Nasa && !RobotRooms.Exists(w, RoomIds.Nasa)) return false;
            if (a == RobotAction.Telescope && !RobotRooms.Exists(w, RoomIds.Rushmore) && !RobotRooms.Exists(w, RoomIds.Insel)) return false;
            return true;
        }

        /// <summary>The post-filters of §4.3 that send the planner back to the random pick (true = rejected).</summary>
        static bool RejectedAfterwards(Ctx c, ref RobotAction a)
        {
            var w = c.W; var p = c.P; var s = c.S;
            int date = w.Clock.Date;
            if (a == RobotAction.Designer && !c.Has(RobotFeature.Designer)) return true;
            if (a == RobotAction.Security && !c.Has(RobotFeature.SecurityOffice)) return true;
            if (c.Has(RobotFeature.MuchAdvertising) && a == RobotAction.Bank && (w.Clock.Hour & 1) == 0) a = RobotAction.Advertising;
            if (a == RobotAction.Petrol && date == 0) return true;
            if (a == RobotAction.Sabotage && date < 1 + p.Index) return true;
            if (a == RobotAction.Petrol && date == 1 && p.Index != 2) return true;
            if (a == RobotAction.Office && c.R.OfficeDestroyed(w, p)) return true;
            if (a == RobotAction.Office && w.Clock.Hour >= 10 && c.R.LaptopWorking(w, p) && c.Roll(100) < 60)
            {
                s.TimeOffice = w.Clock.Time;
                return true;
            }
            if (a == s.LastAction) return true;
            if (!c.Has(RobotFeature.Sabotage) && (a == RobotAction.Petrol || a == RobotAction.Sabotage)) return true;
            if (!c.Has(RobotFeature.RouteBox) && a == RobotAction.RouteBox) return true;
            return false;
        }

        static bool IsAgent12(RobotAction a) => a == RobotAction.LastMinute || a == RobotAction.TravelAgency;

        /// <summary>Plane caps of MAX4/5/10PLANES (and, for purchases, GROSSESKONTO: fewer than 3).</summary>
        static bool PlaneCapReached(Ctx c, bool includeHoard) => PlaneCapReached(c.W, c.P, includeHoard);

        internal static bool PlaneCapReached(World w, Player p, bool includeHoard)
        {
            var f = w.Rules.Get<IRobotFeatures>();
            int n = p.Planes.Count;
            if (includeHoard && f.Has(w, RobotFeature.HoardMoney) && n >= 3) return true;
            if (f.Has(w, RobotFeature.Max4Planes) && n >= 4) return true;
            if (f.Has(w, RobotFeature.Max5Planes) && n >= 5) return true;
            if (f.Has(w, RobotFeature.Max10Planes) && n >= 10) return true;
            return false;
        }
    }
}
