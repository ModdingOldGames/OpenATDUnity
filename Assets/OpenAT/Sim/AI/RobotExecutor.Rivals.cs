using System;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;

namespace OpenAT.Sim.AI
{
    public static partial class RobotExecutor
    {
        /// <summary>
        /// Security bit that protects against a job (ai-robots.md §5.2): plane 1,2 → 6, 3,4 → 7; office 1,3 → 0,
        /// 2 → 1, 4 → 2; special 1,5 → 8, 2,3 → 5, 4 → 3. −1 = none.
        /// </summary>
        public static int ProtectionBit(SabotageFamily f, int severity)
        {
            switch (f)
            {
                case SabotageFamily.Plane: return severity == 1 || severity == 2 ? 6 : severity == 3 || severity == 4 ? 7 : -1;
                case SabotageFamily.Office: return severity == 1 || severity == 3 ? 0 : severity == 2 ? 1 : severity == 4 ? 2 : -1;
                default: return severity == 1 || severity == 5 ? 8 : severity == 2 || severity == 3 ? 5 : severity == 4 ? 3 : -1;
            }
        }

        /// <summary>
        /// Saboteur visit (ai-robots.md §5.2 "Sabotage"): decides whether and against whom; the job itself is
        /// written by the items domain (<see cref="IRivalActions.PlaceSabotage"/>). Robots pay nothing.
        /// </summary>
        static void Sabotage(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S; var r = c.R;
            int slot = c.Slot, date = w.Clock.Date;
            bool extreme = c.Has(RobotFeature.ExtremeSabotage);

            int target = c.Dislike;
            if (target == -1 && (extreme || c.Rnd.Rand(3) == 0 || slot == 0))
            {
                target = c.Rnd.Rand(Player.Count);
                if (extreme && (target == slot || w.Players[target].IsOut))
                {
                    var rule = w.Rules.Get<IMissionRule>();
                    long best = -9999;
                    for (int k = 0; k < Player.Count; k++)
                        if (!w.Players[k].IsOut) { long v = rule.Rating(w, w.Players[k]); if (v > best) { best = v; target = k; } }
                }
                if (target == slot || w.Players[target].IsOut) target = -1;
            }

            s.WorkCountdown = c.Work(RobotAction.Sabotage);
            int hints = r.SuspicionLevel(w, p);
            if (hints >= 100 || p.Image <= -500 || r.HasPendingSabotage(w, p)) return;

            bool go = ((date + slot) % 3 != 0 || extreme || c.Rnd.Rand(4) == 0 || (slot == 0 && c.Rnd.Rand(2) == 0))
                && target != -1 && target != slot
                && (hints < 80 || (hints < 90 && p.Credit < 1000000 && p.Money > 3000000 && slot == 1 && (date & 3) == 0))
                && (w.Players[target].IsComputer || c.Has(RobotFeature.MuchSabotage));
            if (!go) return;

            var victim = w.Players[target];
            int trust = Math.Min(4, r.SaboteurTrust(w, p) + 1);
            r.SetSaboteurTrust(w, p, trust);

            // Up to 2 passes (5 with EXTREME); the hour of day mod 3 selects the family.
            int plane = 0, office = 0, special = 0, planeId = 0, specialPlaneId = 0, annoyance = 0;
            int passes = 2 + (extreme ? 3 : 0);
            for (int pass = 1; pass <= passes; pass++)
            {
                switch (w.Clock.Hour % 3)
                {
                    case 0:
                        if (victim.Planes.Count > 0)
                        {
                            plane = Math.Min(4, trust);
                            if (pass > 2 && plane > 0) plane = c.Rnd.Rand(plane);
                            if (slot == 0 && c.Rnd.Rand(3) != 0) plane = Math.Min(plane, 2);
                            if (slot == 2 && c.Rnd.Rand(3) != 0) plane = Math.Min(plane, 3);
                            planeId = RandomPlaneId(c, victim);
                        }
                        break;
                    case 1:
                        office = Math.Min(4, trust);
                        if (pass > 2 && office > 0) office = c.Rnd.Rand(office);
                        if (slot == 0 && c.Rnd.Rand(3) != 0) office = 1;
                        if (slot == 2 && c.Rnd.Rand(3) != 0) office = Math.Min(office, 3);
                        if (office == 1 && c.Rnd.Rand(2) == 0) office++; // the original used the unseeded C rand() here (Q20)
                        office = PersonalActFor(office, victim);
                        break;
                    default:
                        if (victim.Planes.Count > 0)
                        {
                            special = Math.Min(5, trust);
                            if (pass > 2 && special > 0) special = c.Rnd.Rand(special);
                            if (slot == 0 && c.Rnd.Rand(3) != 0) special = 1;
                            if (special == 5) specialPlaneId = RandomPlaneId(c, victim);
                        }
                        break;
                }

                // The victim's security office cancels matching jobs.
                if (Blocked(victim, SabotageFamily.Plane, ref plane)) annoyance++;
                if (Blocked(victim, SabotageFamily.Office, ref office)) annoyance++;
                if (Blocked(victim, SabotageFamily.Special, ref special)) annoyance++;
                if (plane != 0 || office != 0 || special != 0) break;
            }

            bool any = false;
            any |= Place(c, victim, SabotageFamily.Plane, plane, planeId);
            any |= Place(c, victim, SabotageFamily.Office, office, 0);
            any |= Place(c, victim, SabotageFamily.Special, special, special == 5 ? specialPlaneId : 0);
            if (annoyance > 0 && !any)
                w.Events.Publish(new RobotSabotageDecided { Robot = p, Target = victim, Blocked = true });

            // Everything blocked: take the bolt cutter and cut the security office's power next.
            if (!any && plane == 0 && office == 0 && special == 0 && annoyance >= 2 && (date & 1) == 0
                && r.BoltCutterAvailable(w) && RobotRooms.Exists(w, RoomIds.Security) && r.TakeBoltCutter(w, p))
                s.Queue[1] = RobotAction.Security2;

            s.WorkCountdown = w.AI.Tuning.SabotageBusyTicks;
        }

        /// <summary>The laptop virus (personal act 2) needs the victim to carry a laptop; otherwise the coffee act (1).</summary>
        public static int PersonalActFor(int act, Player victim) => act == 2 && !victim.HasItem(ItemId.Laptop) ? 1 : act;

        static int RandomPlaneId(Ctx c, Player victim)
        {
            var planes = victim.Planes.ToList();
            return planes.Count == 0 ? 0 : planes[c.Rnd.Rand(planes.Count)].Id;
        }

        static bool Blocked(Player victim, SabotageFamily f, ref int severity)
        {
            if (severity == 0) return false;
            int bit = ProtectionBit(f, severity);
            if (bit < 0 || (victim.SecurityFlags & (1 << bit)) == 0) return false;
            severity = 0;
            return true;
        }


        static bool Place(Ctx c, Player victim, SabotageFamily f, int severity, int planeId)
        {
            if (severity <= 0) return false;
            bool ok = c.R.PlaceSabotage(c.W, c.P, victim, f, severity, planeId);
            c.W.Events.Publish(new RobotSabotageDecided { Robot = c.P, Target = victim, Family = f, Severity = severity, Placed = ok });
            c.Log($"sabotage {f} {severity} against P{victim.Index}: {(ok ? "placed" : "refused")}");
            return ok;
        }

        // ------------------------------------------------------------------ security office (§5.2)

        static void Security(Ctx c)
        {
            // Two random protection bits 0..8: switched on when rich and needed, off when money ≤ 1 M.
            // The shared helper of the security domain applies it (fees are billed nightly as for humans).
            Items.ItemSystems.RobotVisitSecurity(c.W, c.P, c.Rnd);
            c.S.WorkCountdown = c.Work(RobotAction.Security);
        }

        static void Security2(Ctx c)
        {
            c.R.CutSecurityPower(c.W, c.P);
            c.S.WorkCountdown = c.Work(RobotAction.Security2);
        }
    }
}
