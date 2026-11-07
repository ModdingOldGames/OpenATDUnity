using System;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// "RobotExecuteAction" (ai-robots.md §5): runs the current action at once when the robot enters the
    /// room, then sets the work countdown. Actions go through the same commands as the human UI wherever
    /// the original robot does the same thing; the robot-only shortcuts are in <see cref="RobotShortcuts"/>.
    /// </summary>
    public static partial class RobotExecutor
    {
        /// <summary>Everything an action needs.</summary>
        public sealed class Ctx
        {
            public readonly World W;
            public readonly Player P;
            public readonly RobotState S;
            public readonly IRobotFeatures F;
            public readonly IRivalActions R;
            /// <summary>The action's local generator (the original seeded it from the wall clock, Q20).</summary>
            public readonly TeakRand Rnd;
            /// <summary>Disliked rival of this action (§5.1), −1 = none.</summary>
            public int Dislike = -1;

            public Ctx(World w, Player p)
            {
                W = w; P = p; S = p.Robot;
                F = w.Rules.Get<IRobotFeatures>();
                R = w.Rules.Get<IRivalActions>();
                Rnd = new TeakRand(unchecked(w.AI.Seed * 31 + w.Clock.Date * 1440017 + w.Clock.Time + p.Index * 7919));
            }

            public bool Has(RobotFeature f) => F.Has(W, f);
            public int Slot => P.Index;
            public int Work(RobotAction a) => W.AI.Tuning.Work(a);
            public void Log(string m) => W.AI.Log(W, P, m);
        }

        public static void Execute(World w, Player p)
        {
            if (!p.IsComputer || p.IsOut) return;
            var s = p.Robot;
            var c = new Ctx(w, p);
            var action = s.Current;

            // Duplicate guard (§5.1).
            if (s.LastExecDate == w.Clock.Date && s.LastExecTime == w.Clock.Time && s.LastExecAction == action)
            {
                s.Current = RobotAction.None;
                return;
            }
            s.LastExecDate = w.Clock.Date; s.LastExecTime = w.Clock.Time; s.LastExecAction = action;

            PickDislikedRival(c);
            MaybePhoneHuman(c, action);

            s.ActionsExecuted++;
            s.ActionCounts[(int)action]++;
            c.Log("executes " + action);

            switch (action)
            {
                case RobotAction.None: s.WorkCountdown = 2; break;
                case RobotAction.StartDay:
                case RobotAction.Office: Office(c); break;
                case RobotAction.Personnel: s.TimePersonnel = w.Clock.Time; s.WorkCountdown = c.Work(action); break;
                case RobotAction.Broker: Broker(c); break;
                case RobotAction.Petrol: Petrol(c); break;
                case RobotAction.Sabotage: Sabotage(c); break;
                case RobotAction.Bank: Bank(c); break;
                case RobotAction.EmitShares: RobotShortcuts.EmitShares(c); s.WorkCountdown = c.Work(action); break;
                case RobotAction.Mechanic: Mechanic(c); break;
                case RobotAction.Nasa: Nasa(c); break;
                case RobotAction.Telescope:
                case RobotAction.Ricks:
                case RobotAction.Kiosk: s.WorkCountdown = c.Work(action); break;
                case RobotAction.Museum: MuseumBigPlane(c); break;
                case RobotAction.BuyUsedPlane: BuyUsedPlane(c); break;
                case RobotAction.DutyFree: DutyFree(c); break;
                case RobotAction.LastMinute: Agent(c, OfferSource.LastMinute); break;
                case RobotAction.TravelAgency: Agent(c, OfferSource.TravelAgency); break;
                case RobotAction.Cargo: CargoOffice(c); break;
                case RobotAction.Board: Board(c); break;
                case RobotAction.RouteBox: RouteBox(c); break;
                case RobotAction.Security: Security(c); break;
                case RobotAction.Security2: Security2(c); break;
                case RobotAction.Designer: Designer(c); break;
                case RobotAction.Advertising: Advertising(c); break;
                case RobotAction.RaiseMoney: s.WorkCountdown = c.Work(action); break;
            }

            // Epilogue (§5.1).
            if (p.Index == 2 && s.WorkCountdown > 2) s.WorkCountdown /= 2;
            if (c.Has(RobotFeature.WorkVeryQuick) && s.WorkCountdown > 4) s.WorkCountdown /= 4;
            else if (c.Has(RobotFeature.WorkQuick) && s.WorkCountdown > 2) s.WorkCountdown /= 2;
            foreach (var pl in p.Planes.ToList()) FlightPlanner.Check(w, p, pl);
            s.Current = RobotAction.None;
        }

        // ------------------------------------------------------------------ §5.1 prologue

        static void PickDislikedRival(Ctx c)
        {
            var w = c.W; var p = c.P;
            int n = 0;
            for (int d = 0; d < Player.Count; d++) if (p.Sympathy[d] < 0 && !w.Players[d].IsOut) n++;
            if (n == 0) { c.Rnd.Rand(2); return; }
            n = c.Rnd.Rand(n) + 1;
            for (int d = 0; d < Player.Count; d++)
                if (p.Sympathy[d] < 0 && !w.Players[d].IsOut && --n == 0) { c.Dislike = d; return; }
        }

        static void MaybePhoneHuman(Ctx c, RobotAction action)
        {
            var w = c.W; var p = c.P;
            int t = c.Rnd.Rand(4);
            var h = w.Players[t];
            if (h.IsComputer || h.IsOut || !c.R.CanPhone(w, h)) return;
            bool coop = p.Cooperates[t];
            if (!((coop && p.Sympathy[t] < -20) || (!coop && p.Sympathy[t] > 50 && c.S.CalledPlayer == 0))) return;
            if (action != RobotAction.StartDay && !c.R.HasMobilePhone(w, p)) return;
            if (t == w.LocalPlayer) c.S.CalledPlayer = 2;
            w.Events.Publish(new RobotPhoneCall(p, h));
        }

        // ------------------------------------------------------------------ office (§5.2)

        static void Office(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            int date = w.Clock.Date, slot = c.Slot;

            // 1. Route-mode switch.
            if (!s.RouteMode)
            {
                var prev = w.Players[(slot + 3) % Player.Count];
                int prevRoutes = prev.IsComputer ? (prev.Robot.RouteMode ? 1 : 0) : prev.RouteUsage;
                if (c.Has(RobotFeature.SuggestRoutes) || (date > slot + 30 && p.Planes.Count > 6) || (date > slot + 15 && (prevRoutes == 1 || prevRoutes > 20)))
                {
                    int legs = p.LicensedRouteCount;
                    if (p.Image < 150)
                    {
                        s.WantsRoutes = true;
                        if (Bits(w.Local.Counters.ConnectFlags) > slot) s.RouteMode = true;
                    }
                    else if (legs > 2) s.RouteMode = true;
                }
                if (c.Has(RobotFeature.ForceRoutes)) s.RouteMode = true;
                if (s.RouteMode) c.Log("switches to route mode");
            }

            // 2. Saving goals.
            if ((p.Image > -100 && !s.RouteMode) || p.Image > 100 || s.RouteMode)
            {
                int n = 0, mine = p.Planes.Count;
                foreach (var o in w.Players) if (!o.IsOut && o != p && mine <= o.Planes.Count) n++;
                bool guard = !s.SavingForRocket;
                if (w.AI.Options.FixSavingPrecedence)
                {
                    if (guard && (n >= 3 || (n >= 2 && slot == 2) || (n >= 1 && slot == 0))) s.SavingForPlane = true;
                }
                else if ((guard && n >= 3) || (n >= 2 && slot == 2) || (n >= 1 && slot == 0)) s.SavingForPlane = true; // QUIRK Q22
                if (guard && c.Dislike >= 0 && mine + 2 <= w.Players[c.Dislike].Planes.Count) s.SavingForPlane = true;
                if (c.Dislike >= 0 && mine > 3 && p.Counters.RocketFlags <= w.Players[c.Dislike].Counters.RocketFlags && c.Has(RobotFeature.Nasa))
                {
                    s.SavingForPlane = false;
                    s.SavingForRocket = true;
                }
            }

            // 3. Orders by phone from the branch offices.
            if (!s.RouteMode && c.Has(RobotFeature.Abroad) && (!c.Has(RobotFeature.MuchFreight) || w.Clock.Hour > 9))
                AbroadOrders(c);

            // 4. A strike may simply end.
            if (p.StrikeRunning && c.Rnd.Rand(6) == 0)
            {
                p.StrikeHours = 0;
                p.StrikeEndCountdown = 0;
                w.Events.Publish(new StrikeEnded(p, 3));
            }

            // 5. Luxury upgrades (flat 170 000 per step).
            if (c.Has(RobotFeature.Luxury) && p.Planes.Count > 0 && p.Money > 200000) Luxury(c);

            s.TimeOffice = w.Clock.Time;
            s.WorkCountdown = c.Work(RobotAction.Office);
        }

        static int Bits(int v) { int n = 0; for (; v != 0; v &= v - 1) n++; return n; }

        /// <summary>Flat price of one luxury upgrade step (planes-and-flights.md §6.2).</summary>
        public const long LuxuryStepPrice = 170000;

        static void Luxury(Ctx c)
        {
            var p = c.P;
            long m = p.Money;
            bool go = m > 25000000 || (m > 15000000 ? c.Rnd.Rand(2) == 0 : m > 2500000 ? c.Rnd.Rand(3) == 0 : c.Rnd.Rand(5) == 0);
            if (!go) return;
            long count = 1 + (m > 10000000 ? m / 10000000 : 0);
            // The 7 cabin/technical components (food is a running cost).
            int[] items = { Equip.Seats, Equip.Trays, Equip.Deco, Equip.Engines, Equip.Tyres, Equip.Electronics, Equip.Safety };
            for (long u = 0; u < count; u++)
            {
                bool done = false;
                for (int attempt = 0; attempt < 3 && !done; attempt++)
                {
                    var planes = p.Planes.ToList();
                    if (planes.Count == 0) return;
                    var pl = planes[c.Rnd.Rand(planes.Count)];
                    for (int roll = 0; roll < 4 && !done; roll++)
                    {
                        int it = items[c.Rnd.Rand(items.Length)];
                        if (pl.EquipmentTarget[it] >= 2) continue;
                        pl.EquipmentTarget[it]++;
                        p.Book(-LuxuryStepPrice, MoneyReason.PlaneRefit, pl.Name);
                        done = true;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ small rooms (§5.2)

        static void Broker(Ctx c)
        {
            var p = c.P;
            if (p.Planes.Count > 0 && !c.Has(RobotFeature.TravelHolding) && c.Rnd.Rand(8) == 0)
            {
                var planes = p.Planes.ToList();
                var pl = planes[c.Rnd.Rand(planes.Count)];
                int it = c.Rnd.Rand(Equip.Count);
                if (it != Equip.Food && pl.EquipmentTarget[it] < 2) pl.EquipmentTarget[it]++; // free of charge (Q4)
            }
            c.S.WorkCountdown = c.Work(RobotAction.Broker);
        }

        static void Petrol(Ctx c)
        {
            var w = c.W; var p = c.P;
            if (c.Has(RobotFeature.Tanks))
            {
                int price = Player.PricePerBarrel(w.Kerosene, KeroseneKind.Normal);
                if (w.Kerosene < 500 && p.Money > 1000000 && price > 0)
                {
                    long reserve = w.Kerosene >= 400 ? 2500000 : w.Kerosene >= 350 ? 2000000 : 1500000;
                    long units = (p.Money - reserve) / price;
                    units = Math.Min(units, p.Tank - p.TankContents);
                    if (units > 0) w.Execute(new BuyKeroseneCommand(p.Index, KeroseneKind.Normal, (int)Math.Min(units, int.MaxValue)));
                }
            }
            c.S.WorkCountdown = c.Work(RobotAction.Petrol);
        }

        static void Mechanic(Ctx c)
        {
            var w = c.W; var p = c.P;
            if (c.Has(RobotFeature.RepairPlanes))
            {
                long budget = Math.Min(int.MaxValue, p.Money);
                foreach (var pl in p.Planes.ToList())
                {
                    pl.TargetCondition = Math.Min(pl.WorstCondition + 19, 100);
                    while (pl.TargetCondition < 100 && budget > 300000)
                    {
                        if (budget > 3000000 && pl.TargetCondition < 92) { pl.TargetCondition = Math.Min(pl.TargetCondition + 15, 100); budget -= 3000000; }
                        else { pl.TargetCondition = Math.Min(pl.TargetCondition + 3, 100); budget -= 300000; }
                    }
                }
                w.Execute(new SetMechModeCommand(p.Index, 3));
            }
            else if (c.Has(RobotFeature.GoodPlanes))
            {
                foreach (var pl in p.Planes.Items) pl.TargetCondition = Math.Min(pl.TargetCondition + 3, 100);
                w.Execute(new SetMechModeCommand(p.Index, 3));
            }
            else
            {
                int cap = c.Slot == 3 ? 70 : 100;
                foreach (var pl in p.Planes.Items) pl.TargetCondition = Math.Min(pl.TargetCondition + 2, cap);
                if (p.MechMode == 2) w.Execute(new SetMechModeCommand(p.Index, 3));
            }
            // Q11: every mechanic branch of the original leaves before the work countdown is set, so the robot
            // walks out at once; FixMechanicVisitTime gives it the usual room time.
            if (w.AI.Options.FixMechanicVisitTime) c.S.WorkCountdown = w.AI.Tuning.MechanicFixedTicks;
        }

        static void Nasa(Ctx c)
        {
            var w = c.W; var p = c.P;
            // Final mission rocket / add-on 10 station: at most the lowest missing part per visit, paid, with
            // the robots' price-by-bit quirk (the special-rooms helper owns the part tables and headlines).
            if ((w.Difficulty == Mission.Rocket || w.Difficulty == Mission.AddOnLast) && Items.ItemSystems.RobotBuySpacePart(w, p) >= 0)
                c.S.SavingForRocket = false;
            c.S.WorkCountdown = c.Work(RobotAction.Nasa);
        }

        static void DutyFree(Ctx c)
        {
            var w = c.W; var p = c.P;
            if (!c.R.LaptopSoldToday(w) && c.R.LaptopQuality(w, p) < 4 && w.Clock.Date > 7 && p.Money > 100000) c.R.GiveLaptop(w, p);
            if (!c.R.HasMobilePhone(w, p) && w.Clock.Date > c.Slot && p.Money > 100000) c.R.GiveMobilePhone(w, p);
            c.S.WorkCountdown = c.Work(RobotAction.DutyFree);
        }

        static void Designer(Ctx c)
        {
            var w = c.W; var p = c.P;
            var d = w.AI.MissionDesign;
            if (c.Has(RobotFeature.DesignerBuy) && d != null && d.IsBuildable(w.DesignCatalog))
            {
                long cost = d.Stats(w.DesignCatalog).Cost;
                int guard = 50;
                while (p.Money - cost > 6000000 && guard-- > 0)
                    if (!w.Execute(new BuyDesignedPlaneCommand(p.Index, d, 1)).Ok) break;
            }
            c.S.WorkCountdown = c.Work(RobotAction.Designer);
        }

        // ------------------------------------------------------------------ museum (§5.2)

        static void MuseumBigPlane(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            w.Execute(new EnterMuseumCommand(p.Index));
            if (s.NeedsBiggerPlane <= 0 || c.Has(RobotFeature.HoardMoney)) { s.WorkCountdown = c.Work(RobotAction.Museum); return; }
            bool fix = w.AI.Options.FixMuseumMultiBuy;
            bool bought = false;
            for (int scan = 0; scan < 3 && !(fix && bought); scan++)
                for (int slot = 0; slot < w.UsedPlanes.Length && !(fix && bought); slot++)
                {
                    var o = w.UsedPlanes[slot];
                    if (o == null) continue;
                    if (fix && RobotPlanner.PlaneCapReached(w, p, true)) continue;
                    if (o.BuildYear > 1960 && o.Condition > 40 && o.MarketValue < p.Money + 1000000 && o.Range >= s.NeedsBiggerPlane)
                        if (BuyOffer(c, slot)) { s.NeedsBiggerPlane = 0; bought = true; } // QUIRK Q13: later scans accept any range
                }
            s.WorkCountdown = w.AI.Tuning.MuseumBusyTicks;
        }

        static void BuyUsedPlane(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            w.Execute(new EnterMuseumCommand(p.Index));
            if (!RobotPlanner.PlaneCapReached(w, p, true))
                for (int slot = 0; slot < w.UsedPlanes.Length; slot++)
                {
                    var o = w.UsedPlanes[slot];
                    if (o == null || o.BuildYear <= 1950 || o.Condition <= 65 || o.MarketValue >= p.Money - 1000000) continue;
                    if (BuyOffer(c, slot)) { s.SavingForPlane = false; break; }
                }
            s.WorkCountdown = c.Work(RobotAction.BuyUsedPlane);
        }

        /// <summary>Buys a museum offer with the human command and applies the robot's set-up (§5.2 "Museum").</summary>
        static bool BuyOffer(Ctx c, int slot)
        {
            var w = c.W; var p = c.P;
            var before = new System.Collections.Generic.HashSet<int>();
            foreach (var pl in p.Planes.Items) before.Add(pl.Id);
            if (!w.Execute(new BuyUsedPlaneCommand(p.Index, slot)).Ok) return false;
            Plane plane = null;
            foreach (var pl in p.Planes.Items) if (!before.Contains(pl.Id)) plane = pl;
            if (plane == null) return true;
            plane.MaxAttendants = Math.Min(plane.Attendants * 2, (int)(plane.Attendants * PlaneMarket.AverageAttendantRatio(w, p)));
            for (int i = 0; i < Equip.Count; i++) plane.EquipmentTarget[i] = 1;
            plane.EquipmentTarget[Equip.Food] = 0;
            if (c.Slot == 0) plane.EquipmentTarget[Equip.Seats] = plane.EquipmentTarget[Equip.Trays] = plane.EquipmentTarget[Equip.Deco] = 2;
            if (c.Slot == 1) plane.EquipmentTarget[Equip.Engines] = plane.EquipmentTarget[Equip.Tyres] = plane.EquipmentTarget[Equip.Electronics] = plane.EquipmentTarget[Equip.Safety] = 2;
            c.Log("bought used plane " + plane.Name + " for " + plane.MarketValue);
            return true;
        }

        // ------------------------------------------------------------------ board (§5.2 "Board / Aufsicht")

        static void Board(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            var a = w.Auctions;

            // Gates: only when the "out of gates" counter exceeds the fleet (never in the original, Q9).
            if (p.OutOfGates > p.Planes.Count)
            {
                int best = -1; long cheapest = long.MaxValue;
                for (int i = 0; i < AuctionBoard.Slots; i++)
                {
                    var g = a.Gates[i];
                    if (g.ObjectId < 0 || g.Player == p.Index) continue;
                    if (g.Price < cheapest || g.Player == c.Dislike || c.Slot == 0) { cheapest = g.Price; best = i; }
                }
                if (best >= 0) w.Execute(new BidCommand(p.Index, AuctionKind.Gate, best));
            }

            // Branch offices.
            if ((!s.SavingForPlane && !s.SavingForRocket) || w.Clock.Date < 5 || c.Rnd.Rand(25) == 0 || (c.Slot == 0 && c.Rnd.Rand(3) == 0))
                for (int i = 0; i < AuctionBoard.Slots; i++)
                {
                    var slip = a.Cities[i];
                    if (slip.Player == p.Index || slip.ObjectId < 0 || p.RentCities[slip.ObjectId].Rank != 0) continue;
                    bool want = slip.Player != -1
                        ? (p.Sympathy[slip.Player] < 40 || c.Slot == 0 || c.Rnd.Rand(10) == 0 || (w.Clock.Date > 10 && c.Rnd.Rand(5) == 0))
                        : c.Rnd.Rand(3) == 0;
                    if (!want) continue;
                    // The original tests the *route* slip of the same row here; route slips are never posted, so
                    // its leader is −1 and the test holds whenever the robot dislikes nobody. FixBranchBidTable
                    // tests the branch slip itself.
                    int leader = w.AI.Options.FixBranchBidTable ? slip.Player : a.Routes[i].Player;
                    bool routeRow = leader == c.Dislike || c.Slot == 0 || c.Has(RobotFeature.Abroad);
                    if (p.Yesterday.Total > (long)slip.Price * 10 && p.Credit * 2 < p.Money * 3 && p.Money > 0 && routeRow)
                        w.Execute(new BidCommand(p.Index, AuctionKind.City, i));
                }

            s.TimeBoard = w.Clock.Time;
            s.WorkCountdown = c.Work(RobotAction.Board);
        }

        // ------------------------------------------------------------------ route box (§5.2)

        static void RouteBox(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            var routes = w.RouteTable;
            int owned = 0;
            bool shaky = false;
            for (int i = 0; i < routes.Count; i++)
                if (p.RentRoutes[i].Licensed) { owned++; if (p.RentRoutes[i].DaysLow > 1) { shaky = true; break; } }

            if (!shaky && (s.RouteMode || s.WantsRoutes) && p.Planes.Count > owned / 2 * 3 / 2 && owned < routes.Count)
            {
                var buyable = new bool[routes.Count];
                for (int d = routes.Count - 1; d >= 0; d--)
                    if (!p.RentRoutes[d].Licensed && routes[d].Touches(w.HomeCity)) buyable[d] = true;
                for (int k = routes.Count - 1; k >= 0; k--)
                {
                    if (p.RentRoutes[k].RouteShare < 20) continue;
                    var rk = routes[k];
                    for (int d = routes.Count - 1; d >= 0; d--)
                    {
                        var rd = routes[d];
                        if (!p.RentRoutes[d].Licensed && (rk.From == rd.From || rk.From == rd.To || rk.To == rd.From || rk.To == rd.To)) buyable[d] = true;
                    }
                }
                for (int d = routes.Count - 1; d >= 0; d--)
                    if (!p.RentRoutes[d].Licensed && p.RentRoutes[d].DaysLow < 7) buyable[d] = false;

                int best = -1; long bestValue = 0;
                var demand = w.Rules.Get<IRouteDemandRule>();
                for (int k = 0; k < routes.Count; k++)
                {
                    if (!buyable[k]) continue;
                    if (!new RentRouteCommand(p.Index, k).Validate(w).Ok) continue; // rank or eligibility refused by the route office
                    if (c.Has(RobotFeature.RouteMission))
                    {
                        bool stop = false;
                        foreach (int m in w.MissionCityIds)
                        {
                            if (m == 0) continue;
                            var r = routes[k];
                            if ((r.From == w.HomeCity && r.To == m) || (r.To == w.HomeCity && r.From == m))
                            {
                                best = k;
                                if (c.Rnd.Rand(2) == 0) { stop = true; break; }
                            }
                        }
                        if (stop) break;
                    }
                    else
                    {
                        // "Rent per passenger": the original reads the airline's own licence record, which holds 0 (or a stale
                        // fee) for unowned legs, so the first such candidate usually wins; FixRouteBoxRentTable uses
                        // the route table's licence fee as intended.
                        int pot = Math.Max(1, demand.Potential(w, routes[k]));
                        long v = (w.AI.Options.FixRouteBoxRentTable ? routes[k].Rent : p.RentRoutes[k].Rent) / pot;
                        if (best == -1 || v < bestValue) { bestValue = v; best = k; }
                    }
                }
                if (best >= 0 && w.Execute(new RentRouteCommand(p.Index, best)).Ok)
                    c.Log($"rents route {w.City(routes[best].From).Name}-{w.City(routes[best].To).Name}");
            }
            s.WorkCountdown = c.Work(RobotAction.RouteBox);
        }

        // ------------------------------------------------------------------ advertising (§5.2)

        /// <summary>
        /// Size of a robot's airline campaign (image-and-advertising.md §4.4). Route-minded or hard advertisers:
        /// the joke size every fourth day, else by cash. Others: the joke size with ≤ 1.8 M cash or every
        /// third day, else by image; then, with more than five planes and not on a third day, one size more
        /// (max 4), also on top of the joke size.
        /// </summary>
        public static int CampaignSize(bool byCash, long money, int image, int planes, int date)
        {
            if (byCash)
                return date % 4 == 0 ? 0 : money <= 1800000 ? 1 : money <= 2500000 ? 2 : money <= 3000000 ? 3 : money <= 4000000 ? 4 : 5;
            int n = money <= 1800000 || date % 3 == 0 ? 0 : image > -20 ? 1 : image > -40 ? 2 : image > -60 ? 3 : 4;
            if (planes > 5 && date % 3 != 0) n = Math.Min(n + 1, 4);
            return n;
        }

        static void Advertising(Ctx c)
        {
            var w = c.W; var p = c.P; var s = c.S;
            if (!c.Has(RobotFeature.HoardMoney))
            {
                bool routes = s.WantsRoutes || s.RouteMode;
                bool saving = s.SavingForPlane || s.SavingForRocket;
                // 1. Route ad on the first owned leg with a low route image.
                if (routes && p.Money > 500000)
                    for (int i = 0; i < w.RouteTable.Count; i++)
                    {
                        var rr = p.RentRoutes[i];
                        if (!rr.Licensed) continue;
                        if (rr.Image < 70 || (rr.Image < 80 && !saving) || (rr.Image < 100 && !saving && p.Money > 2000000))
                        {
                            RobotShortcuts.RouteAd(c, i);
                            break;
                        }
                    }
                // 2. Airline campaign.
                int nextImage = w.Players[(c.Slot + 1) % 3].Image;
                bool campaign = ((p.Image < 0 || (routes && p.Image < 300)) && p.Money > 1500000 && !saving)
                    || (p.Money > 150000 && c.Has(RobotFeature.MuchAdvertising) && (p.Image + 10 < nextImage || (c.Dislike >= 0 && p.Image + 10 < w.Players[c.Dislike].Image)))
                    || (p.Image < 1000 && p.Money - p.Credit > 4000000 && !saving);
                if (campaign)
                {
                    int n = CampaignSize(s.RouteMode || c.Has(RobotFeature.HardAdvertising), p.Money, p.Image, p.Planes.Count, w.Clock.Date);
                    var r = w.Execute(new AdCampaignCommand(p.Index, AdType.Company, n));
                    if (r.Ok) c.Log("airline campaign tier " + n);
                }
            }
            s.WorkCountdown = c.Work(RobotAction.Advertising);
        }
    }
}
