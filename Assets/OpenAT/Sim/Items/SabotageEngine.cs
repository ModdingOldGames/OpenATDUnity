using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Sympathy of one boss towards another (−1000..1000), changed by pranks and chocolates
    /// (items-security-special.md §1.3, §3.4). Owned by the AI domain; the default keeps the values in
    /// <see cref="WorldItems.Sympathy"/>.
    /// </summary>
    public interface IRivalSympathy : IRule
    {
        int Get(World w, int who, int towards);
        void Change(World w, int who, int towards, int delta);
    }

    public sealed class LedgerSympathy : IRivalSympathy
    {
        public int Get(World w, int who, int towards) => w.Items.Sympathy[who, towards];
        public void Change(World w, int who, int towards, int delta)
        {
            int v = w.Items.Sympathy[who, towards] + delta;
            w.Items.Sympathy[who, towards] = Math.Max(-1000, Math.Min(1000, v));
        }
    }

    /// <summary>
    /// Ordering and executing rival pranks (items-security-special.md §1, sim-core.md §2.4 step 10,
    /// §2.8 step 5, §6.7; economy.md §6.4, §17.3).
    /// </summary>
    public static class SabotageEngine
    {
        /// <summary>Non-dialog refusals of <see cref="CheckOrder"/>.</summary>
        public const int BadVictim = -1, NotOffered = -2, BadTarget = -3;

        /// <summary>Highest option offered in a family at the saboteur's trust (§1.2).</summary>
        public static int OfferedOptions(World w, Player s, PrankFamily f)
        {
            int n = Math.Min(SabotageTables.Cap(f), s.Sabotage.Trust);
            if (f == PrankFamily.Special && w.Options.RouteTheftReachable && s.Sabotage.Trust >= 5) n = 6;
            return n;
        }

        /// <summary>True when the victim's security blocks the prank (checked only when ordering, §2.3).</summary>
        public static bool Blocked(Player victim, PrankFamily f, int mode) =>
            SecurityBits.Has(victim.SecurityFlags, SabotageTables.BlockingBit(f, mode));

        /// <summary>
        /// Checks a human order like the saboteur dialog (rooms-and-ui-3b §9): 0 = accepted, otherwise the
        /// <c>Sabo</c> text id of the refusal (1021 no trust, 1045 victim out, 1100 pending, 2090..2098
        /// protected, 6000 cannot afford, 1300 victim has no laptop) or a negative code.
        /// </summary>
        public static int CheckOrder(World w, Player s, int victim, PrankFamily f, int mode, int target = -1, bool targetChosen = true)
        {
            if (s.Sabotage.Trust == 0) return SaboText.NoTrust;
            if (victim < 0 || victim >= Player.Count || victim == s.Index) return BadVictim;
            var v = w.Players[victim];
            if (v.IsOut) return SaboText.VictimOut;
            if (s.Sabotage.AnyPending) return SaboText.AlreadyPending;
            if (mode < 1 || mode > OfferedOptions(w, s, f)) return NotOffered;
            if (Blocked(v, f, mode)) return SabotageTables.RefusalText(f, mode);
            long price = SabotageTables.Price(f, mode);
            bool unchecked56 = f == PrankFamily.Special && mode >= 5 && !w.Options.FixSabotageSpecialAffordability;
            if (!unchecked56 && s.Money - price < Player.DebtLimit) return SaboText.CannotAfford;
            if (f == PrankFamily.Person && mode == 2 && !v.HasItem(ItemId.Laptop)) return SaboText.NoLaptop;
            if (targetChosen)
            {
                if (f == PrankFamily.Plane || (f == PrankFamily.Special && mode == 5))
                    if (v.Planes.Find(target) == null) return BadTarget;
                if (f == PrankFamily.Special && mode == 6)
                    if (target < 0 || target >= v.RentRoutes.Length || v.RentRoutes[target].Rank == 0) return BadTarget;
            }
            return 0;
        }

        /// <summary>Marks the victim's "security needed" bit of a prank (§2.4).</summary>
        public static void MarkNeeded(Player victim, PrankFamily f, int mode)
        {
            int bit = SabotageTables.BlockingBit(f, mode);
            if (bit >= 0) victim.SecurityNeeded |= 1 << bit;
        }

        /// <summary>
        /// Commits a human order (dialog ids 1070, 1251..1254, 1081..1084, 1090, 1091): pending state, payment
        /// (2080) with the bodyguard rebate, trust growth (plane +1 up to 5; person/special k → max(trust, k + 1);
        /// special 5/6: none in the original, k + 1 with <see cref="GameOptions.FixSpecialPrankTrust"/>).
        /// </summary>
        public static void Commit(World w, Player s, int victim, PrankFamily f, int mode, int target)
        {
            var v = w.Players[victim];
            var st = s.Sabotage;
            long price = SabotageTables.Price(f, mode);
            switch (f)
            {
                case PrankFamily.Plane:
                    MarkNeeded(v, f, mode);
                    st.PlaneMode = mode; st.PlaneVictim = victim; st.PlaneTarget = target; st.PlaneArmed = false; st.PlaneForeign = false;
                    st.PlaneSelecting = 0;
                    // With the special 5/6 fix trust can exceed 5; a plane prank must not lower it again.
                    st.Trust = w.Options.FixSpecialPrankTrust ? Math.Max(st.Trust, Math.Min(5, st.Trust + 1)) : Math.Min(5, st.Trust + 1);
                    break;
                case PrankFamily.Person:
                    MarkNeeded(v, f, mode);
                    st.PersonMode = mode; st.PersonVictim = victim; st.PersonForeign = false;
                    st.Trust = Math.Max(st.Trust, mode + 1);
                    break;
                case PrankFamily.Special:
                    // The original's special 5/6 trust step uses the person dialog's id base and yields a
                    // negative value, so route theft (trust 6) is never unlocked; it also forgets the
                    // victim's "needed" bit for 5/6 (robots set it).
                    if (mode <= 4 || w.Options.FixSpecialPrankNeeded) MarkNeeded(v, f, mode);
                    if (mode <= 4 || w.Options.FixSpecialPrankTrust) st.Trust = Math.Max(st.Trust, mode + 1);
                    st.SpecialMode = mode; st.SpecialVictim = victim; st.SpecialForeign = false;
                    if (mode >= 5) st.PlaneTarget = target;
                    break;
            }
            s.Book(-price, MoneyReason.SabotageJob);
            s.BodyguardRebate(price);
            w.Events.Publish(new PrankOrdered(s, v, f, mode, price));
        }

        /// <summary>
        /// A robot's order (ai-robots ACTION_SABOTAGE, §1.5): free of charge, no trust change here (the AI
        /// raises its trust itself). Returns false when the victim's security refuses it (an "annoyance").
        /// On success the victim's needed bit is set (robots also mark it for special 5).
        /// </summary>
        public static bool RobotOrder(World w, Player robot, int victim, PrankFamily f, int mode, int target = -1)
        {
            if (victim < 0 || victim >= Player.Count || victim == robot.Index || mode < 1) return false;
            var v = w.Players[victim];
            if (v.IsOut || robot.Sabotage.AnyPending) return false;
            if (f == PrankFamily.Person && mode == 2 && !v.HasItem(ItemId.Laptop)) return false;
            if (Blocked(v, f, mode)) { robot.Sabotage.Annoyances++; return false; }
            MarkNeeded(v, f, mode);
            var st = robot.Sabotage;
            if (f == PrankFamily.Plane) { st.PlaneMode = mode; st.PlaneVictim = victim; st.PlaneTarget = target; st.PlaneArmed = false; st.PlaneForeign = false; }
            else if (f == PrankFamily.Person) { st.PersonMode = mode; st.PersonVictim = victim; st.PersonForeign = false; }
            else { st.SpecialMode = mode; st.SpecialVictim = victim; st.SpecialForeign = false; if (mode >= 5) st.PlaneTarget = target; }
            return true;
        }

        // ================================================================== foreign sabotage (§1.6)

        /// <summary>
        /// Mission "crisis management" (ATFS 6, sim-core.md §2.6 step 29): every non-local airline without a
        /// pending prank and with hints &lt; 90 gets an anonymous prank against a random airline with planes.
        /// Generator seeded with Date + the local player's money.
        /// </summary>
        public static void ForeignSabotage(World w)
        {
            var r = new TeakRand(unchecked((int)(w.Clock.Date + w.Players[w.LocalPlayer].Money)));
            for (int c = 0; c < Player.Count; c++)
            {
                if (c == w.LocalPlayer) continue;
                var p = w.Players[c];
                var st = p.Sabotage;
                if (p.IsOut || st.Hints >= 90 || st.AnyPending) continue;
                var v = w.Players[r.Rand(4)];
                if (v.IsOut || v.Index == p.Index || v.Planes.Count == 0) continue;
                int fam = r.Rand(3);
                int mode = fam == 2 ? r.Rand(5) + 1 : r.Rand(4) + 1;
                int plane = RandomPlaneId(v, r);
                var f = fam == 0 ? PrankFamily.Plane : fam == 1 ? PrankFamily.Person : PrankFamily.Special;
                if (f == PrankFamily.Person && mode == 2 && !v.HasItem(ItemId.Laptop)) continue;
                if (Blocked(v, f, mode)) continue;
                st.PlaneVictim = st.PersonVictim = st.SpecialVictim = v.Index;
                st.PlaneTarget = plane;
                st.PlaneArmed = false;
                if (f == PrankFamily.Plane) { st.PlaneMode = mode; st.PlaneForeign = true; }
                else if (f == PrankFamily.Person) { st.PersonMode = mode; st.PersonForeign = true; }
                else { st.SpecialMode = mode; st.SpecialForeign = true; }
            }
        }

        /// <summary>A random plane id of the airline (the original's GetRandomUsedIndex: uniform over used slots).</summary>
        public static int RandomPlaneId(Player v, TeakRand r)
        {
            var l = v.Planes.ToList();
            if (l.Count == 0) return -1;
            return l[r.Rand(l.Count)].Id;
        }

        // ================================================================== hourly plane pranks (§1.3, sim-core §2.4 step 10)

        /// <summary>
        /// On the hour: every pending plane prank is armed once its plane stands on the ground; an armed
        /// prank fires when the plane is in the air (modes 1, 2, 4, 5) or, for the flat tyre, at the hour of
        /// its next departure while still on the ground. A sold plane lets the prank lapse.
        /// </summary>
        public static void HourlyPlanePranks(World w)
        {
            for (int c = 0; c < Player.Count; c++)
            {
                var s = w.Players[c];
                var st = s.Sabotage;
                if (st.PlaneMode == 0) continue;
                if (st.PlaneVictim < 0) { st.PlaneMode = 0; continue; }
                var v = w.Players[st.PlaneVictim];
                var plane = v.Planes.Find(st.PlaneTarget);
                if (plane == null) { st.PlaneMode = 0; st.PlaneForeign = false; continue; }
                if (!st.PlaneArmed && PlaneLocation.IsOnGround(plane.Location)) st.PlaneArmed = true;
                if (!st.PlaneArmed) continue;

                bool now = false;
                if (st.PlaneMode != 3 && plane.Location < 0) now = true;
                if (st.PlaneMode == 3 && plane.Location >= 0)
                {
                    int e = plane.Plan.NextStart;
                    if (e >= 0 && plane.Plan.Entries[e].StartAbs == w.Clock.Date * 24 + w.Clock.Hour) now = true;
                }
                if (!now) continue;
                FirePlanePrank(w, s, v, plane);
            }
        }

        static void FirePlanePrank(World w, Player s, Player v, Plane plane)
        {
            var st = s.Sabotage;
            int mode = st.PlaneMode;
            bool foreign = st.PlaneForeign;
            bool hasFlight = plane.Plan.NextFlight >= 0;
            if (!hasFlight && w.Options.FixSabotageRetrigger) return;

            v.Statistics.Add(StatId.Accidents, 1);
            s.Statistics.Add(StatId.SabotageCommitted, 1);
            // QUIRK (§1.3, Q-2): without a next flight nothing else happens and the prank stays pending.
            if (!hasFlight) return;

            ref var fe = ref plane.Plan.Entries[plane.Plan.NextFlight];
            if (!foreign) st.Hints += SabotageTables.PlaneHints[mode];
            v.ApplySabotageEconomics(mode);
            w.Rules.Get<IRivalSympathy>().Change(w, v.Index, s.Index, SabotageTables.PlaneSympathy[mode]);
            if (fe.Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(fe.ObjectId);
                if (ri >= 0 && ri < v.RentRoutes.Length)
                    v.RentRoutes[ri].Image = v.RentRoutes[ri].Image * Player.SabotageRouteImagePercent[mode] / 100;
            }

            if (mode == 3)
            {
                int e = plane.Plan.NextStart;
                if (e >= 0)
                {
                    ref var ns = ref plane.Plan.Entries[e];
                    ns.StartHour++;
                    if (ns.StartHour >= 24) { ns.StartHour = 0; ns.StartDate++; }
                    plane.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
                    if (!foreign)
                    {
                        // QUIRK (§1.3, Q-8): the delay is counted for the saboteur.
                        var who = w.Options.FixSabotageDelayStatistic ? v : s;
                        who.Statistics.Add(StatId.Delays, 1);
                    }
                    FlightPlanner.Check(w, v, plane);
                    Contracts.ContractUsage.UpdateContracts(w, v);
                }
                if (v.Owner == Owner.Human) w.Advise(v, 2304, plane.Name);
            }

            var act = Record(w, foreign ? SabotageAct.Foreign : s.Index, v.Index, PrankFamily.Plane, mode);
            w.Newspapers.AddOverride(0, 2000 + mode, SabotageTables.PlanePicture[mode], 0, (v.Index == w.LocalPlayer ? 50 : 0) + mode, v.Airline);
            st.PlaneMode = 0;
            st.PlaneForeign = false;
            st.PlaneArmed = false;
            w.Events.Publish(new PrankExecuted(act, s, v, plane.Name));
            if (v.Owner == Owner.Human && !v.IsOut)
                w.Events.Publish(new Fleet.LetterSent(v, "Lett", 500 + mode, plane.Name));
        }

        static SabotageAct Record(World w, int perpetrator, int victim, PrankFamily f, int mode)
        {
            var act = new SabotageAct { Perpetrator = perpetrator, Victim = victim, Family = f, Mode = mode, ActCode = SabotageAct.Code(f, mode), Date = w.Clock.Date };
            w.Items.Acts.Add(act);
            return act;
        }

        // ================================================================== morning (§2.8)

        /// <summary>
        /// 09:00, before the boss's review (sim-core.md §2.8 steps 1 and 3): victims of the listed acts get
        /// DaysWithoutSabotage = 0; the report is built and exposed culprits (hints ≥ 100) pay hints × 10 000
        /// to the victim (2200 / 2201, statistic penalties), once per listed act.
        /// </summary>
        public static void MorningReport(World w)
        {
            var report = new SabotageReport { Date = w.Clock.Date };
            int escalation = 0, lastVictim = -1;
            foreach (var act in w.Items.Acts)
            {
                if (act.Victim >= 0 && act.Victim < Player.Count) w.Players[act.Victim].Counters.DaysWithoutSabotage = 0;
                var line = new SabotageReportLine { Act = act };
                line.IntroId = act.Victim == lastVictim ? 2055 : 2050 + escalation;
                if (act.Victim != lastVictim) escalation = Math.Min(escalation + 1, 3);
                lastVictim = act.Victim;
                int hints = act.IsForeign ? 0 : w.Players[act.Perpetrator].Sabotage.Hints;
                if (act.IsForeign || hints < 15) line.CommentId = 2070;
                else if (hints < 40) line.CommentId = 2071;
                else if (hints < 100) line.CommentId = 2072;
                else
                {
                    line.CommentId = 2073;
                    var culprit = w.Players[act.Perpetrator];
                    var victim = w.Players[act.Victim];
                    long fine = (long)hints * 10000;
                    line.Fine = fine;
                    culprit.Book(-fine, MoneyReason.SabotageFine);
                    culprit.Statistics.Add(StatId.ExpensePenalties, -fine);
                    victim.Book(fine, MoneyReason.SabotageCompensation);
                    w.Events.Publish(new SabotageFined(culprit, victim, fine));
                }
                report.Lines.Add(line);
            }
            w.Items.Report = report;
        }

        /// <summary>
        /// Leaving the morning briefing (sim-core.md §2.8 step 5, Aufsicht destructor): the listed acts are
        /// cleared, then every airline's pending person and special pranks are executed in airline order and
        /// listed for the next briefing. Idempotent per date.
        /// </summary>
        public static void ExecuteMorningPranks(World w)
        {
            var items = w.Items;
            if (items.MorningExecutedDate == w.Clock.Date) return;
            items.MorningExecutedDate = w.Clock.Date;
            items.Acts.Clear();
            for (int c = 0; c < Player.Count; c++)
            {
                var s = w.Players[c];
                if (s.IsOut) continue;
                if (s.Sabotage.PersonMode != 0) ExecutePerson(w, s);
                if (s.Sabotage.SpecialMode != 0) ExecuteSpecial(w, s);
            }
        }

        static void ExecutePerson(World w, Player s)
        {
            var st = s.Sabotage;
            int mode = st.PersonMode;
            bool foreign = st.PersonForeign;
            st.PersonMode = 0; st.PersonForeign = false;
            if (st.PersonVictim < 0) return;
            var v = w.Players[st.PersonVictim];
            if (!foreign) s.Statistics.Add(StatId.SabotageCommitted, 1);
            var sym = w.Rules.Get<IRivalSympathy>();
            switch (mode)
            {
                case 1:
                    if (!foreign) st.Hints += 8;
                    sym.Change(w, v.Index, s.Index, -10);
                    v.Sick = true;
                    break;
                case 2:
                    if (v.HasItem(ItemId.Laptop) && v.LaptopVirus == 0) v.LaptopVirus = 1;
                    break;
                case 3:
                    if (!foreign) st.Hints += 25;
                    sym.Change(w, v.Index, s.Index, -50);
                    v.OfficeState = 1;
                    w.Events.Publish(new OfficeBombArmed(v));
                    break;
                case 4:
                    if (!foreign) st.Hints += 40;
                    v.StrikePlanned = true;
                    break;
            }
            var act = Record(w, foreign ? SabotageAct.Foreign : s.Index, v.Index, PrankFamily.Person, mode);
            w.Events.Publish(new PrankExecuted(act, s, v, null));
        }

        static void ExecuteSpecial(World w, Player s)
        {
            var st = s.Sabotage;
            int mode = st.SpecialMode;
            bool foreign = st.SpecialForeign;
            st.SpecialMode = 0; st.SpecialForeign = false;
            if (st.SpecialVictim < 0) return;
            var v = w.Players[st.SpecialVictim];
            s.Statistics.Add(StatId.SabotageCommitted, 1); // counted even for foreign acts (original)
            string planeName = null;
            switch (mode)
            {
                case 1:
                    if (!foreign) st.Hints += 8;
                    v.BrochureFrom = s.Index;
                    break;
                case 2:
                    if (!foreign) st.Hints += 15;
                    v.PhonesDown = true;
                    break;
                case 3:
                    if (!foreign) st.Hints += 25;
                    v.PressStatement = true;
                    {
                        var local = w.Players[w.LocalPlayer];
                        if (local.Owner == Owner.Human) w.Events.Publish(new Fleet.LetterSent(local, "Lett", 509, v.Airline, v.Name, v.Airline));
                    }
                    if (v.Index == w.LocalPlayer) w.Advise(v, 2020);
                    RecalculateRoutePassengers(w, v);
                    break;
                case 4:
                    v.Book(-1000000, MoneyReason.SabotageMoneyTransfer);
                    if (!foreign) { s.Book(1000000, MoneyReason.SabotageMoneyTransfer); st.Hints += 30; }
                    break;
                case 5:
                    if (!foreign) st.Hints += 50;
                    {
                        var plane = v.Planes.Find(st.PlaneTarget);
                        if (plane != null) { plane.PseudoProblem = 15; planeName = plane.Name; }
                    }
                    break;
                case 6:
                    st.Hints += 70; // always, even for foreign acts (original)
                    StealRoute(w, s, v, st.PlaneTarget);
                    if (v.Index == w.LocalPlayer) w.Advise(v, 2021);
                    break;
            }
            var act = Record(w, foreign ? SabotageAct.Foreign : s.Index, v.Index, PrankFamily.Special, mode);
            w.Events.Publish(new PrankExecuted(act, s, v, planeName));
        }

        /// <summary>Press release: all route flights of the victim are recalculated at once.</summary>
        static void RecalculateRoutePassengers(World w, Player v)
        {
            if (!w.Rules.Has<IRoutePassengerRule>()) return;
            var rule = w.Rules.Get<IRoutePassengerRule>();
            foreach (var pl in v.Planes.ToList())
            {
                var f = pl.Plan.Entries;
                for (int e = 0; e < FlightPlan.Size; e++)
                    if (f[e].Kind == FlightKind.Route) rule.Calculate(w, v, pl, ref f[e]);
            }
        }

        /// <summary>
        /// Route theft (special 6): the route and its reverse are taken from the victim. QUIRK: the hand-over to
        /// the saboteur never happens (byte compared with −1, reproduced by <see cref="RouteOps.Revoke"/>);
        /// <see cref="GameOptions.FixRouteTheftTransfer"/> hands it over when the saboteur has no licence.
        /// </summary>
        public static void StealRoute(World w, Player s, Player v, int routeIndex)
        {
            if (routeIndex < 0 || routeIndex >= w.RouteTable.Count) return;
            var route = w.RouteTable[routeIndex];
            int back = -1;
            for (int d = 0; d < w.RouteTable.Count; d++)
                if (w.RouteTable[d].From == route.To && w.RouteTable[d].To == route.From) { back = d; break; }
            TakeOne(w, s, v, routeIndex);
            if (back >= 0) TakeOne(w, s, v, back);
        }

        static void TakeOne(World w, Player s, Player v, int ri)
        {
            if (ri >= v.RentRoutes.Length || v.RentRoutes[ri].Rank == 0) return;
            // The intended guard is "the new owner holds no licence"; the original's test (rank == −1) never
            // holds, so Revoke only removes. The fix marks the free licence so Revoke's hand-over branch runs
            // (the saboteur takes the victim's rank, nobody moves up).
            if (w.Options.FixRouteTheftTransfer && s.RentRoutes[ri].Rank == 0) s.RentRoutes[ri].Rank = -1;
            RouteOps.Revoke(w, v, ri, s.Index);
        }

        // ================================================================== night (§2.7 step 7)

        /// <summary>Per airline new day: sabotage day effects cleared, hints −3 (floor 0).</summary>
        public static void NightlyReset(World w, Player p)
        {
            p.BrochureFrom = -1;
            p.PhonesDown = false;
            p.PressStatement = false;
            p.Sick = false;
            p.Sabotage.Hints -= Math.Min(3, p.Sabotage.Hints);
        }
    }
}
