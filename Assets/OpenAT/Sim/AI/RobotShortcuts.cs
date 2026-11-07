using System;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// The original robots' bypasses (ai-robots.md §5, §9, economy.md §18), kept apart from the shared
    /// commands so it is obvious where robots do something a human cannot. Each method names what it skips.
    /// </summary>
    public static class RobotShortcuts
    {
        // ------------------------------------------------------------------ flight plans

        /// <summary>At least <paramref name="n"/> empty plan entries.</summary>
        public static bool HasFreeEntries(Plane plane, int n)
        {
            int free = 0;
            foreach (var e in plane.Plan.Entries) if (e.Kind == FlightKind.None && ++free >= n) return true;
            return false;
        }

        /// <summary>
        /// Writes a flight into the plan the way the original robot does (ai-robots.md §5.3 note, §6.2):
        /// straight into an empty entry (the last one if free, else the first free; "plan full" = refused),
        /// then the shared validator sorts it in and inserts ferry flights. Skipped compared with the human
        /// planner (<see cref="PlaceFlightCommand"/>): the range check and the replace-the-covered-flight
        /// rule. Returns false when the plan is full or the validator removed the entry again.
        /// </summary>
        public static bool AppendFlight(World w, Player p, Plane plane, int kind, int objectId, int startDate, int startHour,
                                        int hoursBefore = -1, bool planGates = true)
        {
            var f = plane.Plan.Entries;
            int slot = FlightPlan.Size - 1;
            if (f[slot].Kind != FlightKind.None)
            {
                slot = -1;
                for (int i = 0; i < FlightPlan.Size; i++) if (f[i].Kind == FlightKind.None) { slot = i; break; }
                if (slot < 0) return false;
            }
            int before = CountEntries(plane, kind, objectId);
            ref var e = ref f[slot];
            e.Kind = kind;
            e.ObjectId = objectId;
            e.Okay = EntryOkay.Ok;
            e.StartDate = startDate;
            e.StartHour = startHour;
            e.Gate = GateCode.None;
            e.GateWarning = false;
            e.Passengers = e.PassengersFC = 0;
            if (kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(objectId);
                e.TicketPrice = p.RentRoutes[ri].TicketPrice;
                e.TicketPriceFC = p.RentRoutes[ri].TicketPriceFC;
            }
            if (!FlightPlanner.RefreshCities(w, p, ref e)) { e.Kind = FlightKind.None; return false; }
            e.HoursBefore = hoursBefore >= 0 ? hoursBefore : ((e.StartDate - w.Clock.Date) * 24 + (e.StartHour - w.Clock.Hour)) & 0xFF;
            w.Rules.Get<IRoutePassengerRule>().Calculate(w, p, plane, ref e);
            e.PArrived = 0;
            plane.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            FlightPlanner.Check(w, p, plane, true, planGates);
            return CountEntries(plane, kind, objectId) > before;
        }

        static int CountEntries(Plane plane, int kind, int id)
        {
            int n = 0;
            foreach (var e in plane.Plan.Entries) if (e.Kind == kind && e.ObjectId == id) n++;
            return n;
        }

        /// <summary>
        /// "DelayFlightsIfNecessary" (ai-robots.md §6.4), robot only: plan gates, then push every route or
        /// contract flight touching the home airport that has no gate back by one hour, at most 6 pushes per
        /// plane. Only flights that may still be edited are pushed (the original also touched past entries).
        /// </summary>
        public static void DelayFlightsIfNecessary(World w, Player p)
        {
            if (!p.IsComputer || p.IsOut) return;
            GatePlanner.PlanGates(w, p);
            int home = w.HomeCity, date = w.Clock.Date, hour = w.Clock.Hour;
            foreach (var plane in p.Planes.ToList())
            {
                int delays = 0;
                bool again = true;
                while (again && delays < 6)
                {
                    again = false;
                    var f = plane.Plan.Entries;
                    for (int d = 0; d < FlightPlan.Size; d++)
                    {
                        ref var e = ref f[d];
                        if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract) continue;
                        if (e.From != home && e.To != home) continue;
                        if (e.Gate != GateCode.None || !e.IsFuture(date, hour)) continue;
                        e.StartHour++;
                        while (e.StartHour >= 24) { e.StartHour -= 24; e.StartDate++; }
                        delays++;
                        plane.Plan.UpdatePointers(date, hour);
                        FlightPlanner.Check(w, p, plane);
                        again = true;
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ offers

        /// <summary>
        /// Takes a branch offer through a cooperating airline's office (ai-robots.md §5.2 office step 3): the
        /// human command requires an own branch. Same effect as <see cref="AcceptContractCommand"/>; no call charges.
        /// </summary>
        public static int AcceptForeignContract(World w, Player p, int cityIndex, int slot)
        {
            if (cityIndex < 0 || cityIndex >= w.ForeignContracts.Length) return 0;
            var b = w.ForeignContracts[cityIndex];
            if (b.IsEmpty(slot)) return 0;
            var c = b.Offers[slot].Clone();
            c.InPlan = PlanState.NotPlanned;
            c.Okay = 1;
            int id = p.Contracts.Add(c);
            b.Offers[slot].Premium = 0;
            w.Events.Publish(new ContractAccepted { Player = p, Contract = c, Source = ContractSource.Branch });
            return id;
        }

        /// <summary>Freight counterpart of <see cref="AcceptForeignContract"/>.</summary>
        public static int AcceptForeignFreight(World w, Player p, int cityIndex, int slot)
        {
            if (cityIndex < 0 || cityIndex >= w.ForeignFreight.Length) return 0;
            var b = w.ForeignFreight[cityIndex];
            if (b.Offers[slot].Premium <= 0) return 0;
            var f = b.Offers[slot].Clone();
            f.InPlan = PlanState.NotPlanned;
            f.Okay = 1;
            f.TonsOpen = f.TonsLeft = f.Tons;
            int id = p.Freights.Add(f);
            b.Offers[slot].Premium = 0;
            w.Events.Publish(new ContractAccepted { Player = p, Freight = f, Source = ContractSource.BranchFreight });
            return id;
        }

        // ------------------------------------------------------------------ bank (Q8, Q10)

        /// <summary>Loan by direct edit: no credit limit, no statement line, no statistics (Q8).</summary>
        public static void Borrow(Player p, long amount)
        {
            if (amount <= 0) return;
            p.Credit += amount;
            p.ChangeMoneySilently(amount);
        }

        /// <summary>Repayment by direct edit.</summary>
        public static void RepayCredit(Player p, long amount)
        {
            if (amount <= 0) return;
            amount = Math.Min(amount, p.Credit);
            p.Credit -= amount;
            p.ChangeMoneySilently(-amount);
        }

        /// <summary>Sells own shares down to the 20 % stake; the proceeds are capped (Q10). Direct edit, no fee, price unchanged.</summary>
        public static void SellOwnStake(Player p, long count, long cap)
        {
            p.SharesOwned[p.Index] -= count;
            long proceeds = (long)(p.SharePrice * count);
            p.ChangeMoneySilently(cap > 0 ? Math.Min(proceeds, cap) : proceeds);
        }

        /// <summary>
        /// Fire sale (bank step 5): up to 20 000 shares per company, all at the robot's *own* price (Q10); own
        /// shares only after day 10 (up to day 20 only while more than half stays); stops once money &gt; 2 M.
        /// </summary>
        public static void FireSale(World w, Player p)
        {
            for (int k = 0; k < Player.Count; k++)
            {
                if (p.SharesOwned[k] > 0 && (k != p.Index || w.Clock.Date > 10))
                {
                    long sells = Math.Min(p.SharesOwned[k], 20000);
                    if (k != p.Index || w.Clock.Date > 20 || p.SharesOwned[k] - sells > p.SharesIssued / 2)
                    {
                        p.SharesOwned[k] -= sells;
                        p.ChangeMoneySilently((long)(p.SharePrice * sells));
                    }
                }
                if (p.Money > 2000000) break;
            }
        }

        /// <summary>
        /// Buys the free float of <paramref name="target"/> (bank steps 6/7): at most (money − 3 M, or − 2 M
        /// in the "cheap" case) / price, rounded down to 100; no fee, no debt check; the price rises to
        /// price × issued / (issued − bought / 2). Returns the number of shares bought.
        /// </summary>
        public static long BuyFreeFloat(World w, Player p, Player target, bool cheap)
        {
            long n = w.FreeFloat(target);
            if (target.SharePrice <= 0) return 0;
            long budget = p.Money - (cheap ? 2000000 : 3000000);
            n = Math.Min(n, (long)(budget / target.SharePrice) / 100 * 100);
            if (n <= 0) return 0;
            p.SharesOwned[target.Index] += n;
            p.ChangeMoneySilently(-(long)(n * target.SharePrice));
            long denom = target.SharesIssued - n / 2;
            if (denom > 0) target.SharePrice = target.SharePrice * target.SharesIssued / denom;
            if (target.SharePrice < 0) target.SharePrice = 0;
            if (target != p) w.Events.Publish(new Staff.RivalMove { Rival = p, Kind = Staff.RivalMoveKind.BoughtShares, Previous = target, Count = n });
            return n;
        }

        /// <summary>
        /// Share issue of the robots (ai-robots.md §5.2 "Emit shares"): the whole issuable remainder; 80 % to
        /// the market at price − 5 (a human gets that price only when the market takes everything); fee,
        /// proceeds and dilution compensation booked as for humans; the robot keeps 20 % and immediately
        /// buys half of the issue at the issue price by direct edit (not slot 3 without REBUYSHARES);
        /// then the 20 % stake rule at full price.
        /// </summary>
        public static void EmitShares(RobotExecutor.Ctx c)
        {
            var w = c.W; var p = c.P;
            long n = p.IssuableShares;
            if (n <= 0) return;
            long m = n * 8 / 10;
            double oldPrice = p.SharePrice;
            long e = (long)(p.SharePrice - 5);
            long fee = Player.IssueFee(n, e);
            p.Book(-fee, MoneyReason.ShareIssueFee);
            p.Book(m * e, MoneyReason.ShareIssueProceeds);
            p.Statistics.Add(StatId.IncomeOther, m * e - fee);
            p.SharePrice = (p.SharePrice * p.SharesIssued + (double)e * m) / (p.SharesIssued + m);
            if (p.SharePrice < 0) p.SharePrice = 0;

            long drop = (long)oldPrice;
            long outside = p.SharesIssued - p.SharesOwned[p.Index];
            if (w.Options.FixDilutionFreeFloat)
            {
                outside = 0;
                foreach (var o in w.Players) if (o != p) outside += o.SharesOwned[p.Index];
            }
            p.Book(-(long)(outside * (drop - p.SharePrice)), MoneyReason.ShareIssueDilution);
            foreach (var o in w.Players)
            {
                if (o == p) continue;
                long comp = (long)(o.SharesOwned[p.Index] * (drop - p.SharePrice));
                if (comp != 0) o.Book(comp, MoneyReason.ShareIssueDilution);
            }
            p.SharesIssued += n;
            p.SharesOwned[p.Index] += n - m;
            c.Log($"issues {n} shares at {e}");
            w.Events.Publish(new Staff.RivalMove { Rival = p, Kind = Staff.RivalMoveKind.IssuedShares, Count = n });

            if (c.Slot != 3 || c.Has(RobotFeature.RebuyShares))
            {
                p.SharesOwned[p.Index] += n / 2;
                p.ChangeMoneySilently(-(n / 2) * e);
            }
            if (c.Has(RobotFeature.Max20Percent) && p.SharesIssued > 0
                && p.SharesOwned[p.Index] * 100 / p.SharesIssued > RobotExecutor.OwnStakeTargetPercent && p.SharePrice >= RobotExecutor.OwnStakeMinPrice)
            {
                long sells = p.SharesOwned[p.Index] - p.SharesIssued * RobotExecutor.OwnStakeTargetPercent / 100;
                if (sells > 0) SellOwnStake(p, sells, 0);
            }
        }

        // ------------------------------------------------------------------ advertising

        /// <summary>Route-ad tier the robots buy (its price is charged), and the route image it gives them.</summary>
        public const int RouteAdTier = 3, RouteAdImageDivisor = 2000;

        /// <summary>
        /// The robots' route ad (ai-robots.md §5.2 "Advertising" step 1): route image + price/2000 (= +28 for
        /// the 56 000 tier), clamped 0..100, on this direction only, paying the tier price (a human's route
        /// ad of that tier gives +1 on both directions).
        /// </summary>
        public static void RouteAd(RobotExecutor.Ctx c, int routeIndex)
        {
            var w = c.W; var p = c.P;
            long price = Advertising.PriceOf(AdType.Route, RouteAdTier);
            var rr = p.RentRoutes[routeIndex];
            rr.Image = Math.Max(0, Math.Min(100, rr.Image + (int)(price / RouteAdImageDivisor)));
            p.Statistics.Add(StatId.ExpenseOther, -price);
            p.Book(-price, MoneyReason.AdCampaignBase + RouteAdTier);
            c.Log("route ad on route " + routeIndex);
        }
    }
}
