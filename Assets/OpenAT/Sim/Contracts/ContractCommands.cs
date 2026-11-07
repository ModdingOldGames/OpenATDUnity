using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Contracts
{
    /// <summary>Which offer list a command refers to.</summary>
    public enum OfferBoard { TravelAgency, LastMinute, Branch, FreightOffice, BranchFreight }

    /// <summary>Opening hours of the counters (contracts-and-routes.md §2.6, sim-core.md §1.5).</summary>
    public static class CounterHours
    {
        /// <summary>Last Minute: until 16:00, closed on Saturday (weekday 5).</summary>
        public static bool LastMinuteOpen(World w) => w.Clock.Weekday != 5 && w.Clock.Time < Core.GameClock.At(16);
        /// <summary>Travel agency: until 17:00.</summary>
        public static bool TravelAgencyOpen(World w) => w.Clock.Time < Core.GameClock.At(17);

        /// <summary>
        /// Branch lists need a branch in the city, own or of a cooperating airline that is still in the game
        /// (routes-and-orders.md §6); the home city is excluded.
        /// </summary>
        public static bool HasBranch(World w, Player p, int cityIndex)
        {
            if (cityIndex < 0 || cityIndex >= p.RentCities.Length || w.CityIdAt(cityIndex) == w.HomeCity) return false;
            if (p.RentCities[cityIndex].Rank != 0) return true;
            for (int k = 0; k < Player.Count; k++)
            {
                var q = w.Players[k];
                if (k != p.Index && p.Cooperates[k] && !q.IsOut && cityIndex < q.RentCities.Length && q.RentCities[cityIndex].Rank != 0) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Entering/leaving a counter or opening a branch list: trickle refill (contracts-and-routes.md §2.5).
    /// <see cref="Minimum"/> is the AI's "at least n filled" mode.
    /// </summary>
    public sealed class VisitOfferBoardCommand : PlayerCommand
    {
        public OfferBoard Board;
        public int CityIndex = -1;
        public int Minimum;
        public VisitOfferBoardCommand(int player, OfferBoard board, int cityIndex = -1, int minimum = 0) : base(player) { Board = board; CityIndex = cityIndex; Minimum = minimum; }

        protected override CommandResult Check(World w, Player p)
        {
            if ((Board == OfferBoard.Branch || Board == OfferBoard.BranchFreight) && (CityIndex < 0 || CityIndex >= w.ForeignContracts.Length))
                return CommandResult.Fail("bad-city");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            switch (Board)
            {
                case OfferBoard.TravelAgency: ContractBoards.RefillTravelAgency(w, Minimum); break;
                case OfferBoard.LastMinute: ContractBoards.RefillLastMinute(w, Minimum); break;
                case OfferBoard.FreightOffice: ContractBoards.RefillFreightOffice(w, Minimum); break;
                case OfferBoard.Branch:
                    ContractBoards.RefillForeign(w, CityIndex, Minimum);
                    ContractBoards.RefillForeignFreight(w, CityIndex, Minimum);
                    if (CityIndex < p.CalledCities.Length) p.CalledCities[CityIndex] = true;
                    break;
                case OfferBoard.BranchFreight: ContractBoards.RefillForeignFreight(w, CityIndex, Minimum); break;
            }
        }
    }

    /// <summary>
    /// Accept a passenger offer (contracts-and-routes.md §2.7): immediate and irrevocable; the contract is
    /// copied into the airline's album and the slot becomes empty (premium 0).
    /// </summary>
    public sealed class AcceptContractCommand : PlayerCommand
    {
        public OfferBoard Board;
        public int Slot;
        public int CityIndex = -1;
        /// <summary>Skip the opening-hours test (phone calls, AI that is already inside).</summary>
        public bool IgnoreOpeningHours;
        /// <summary>Id of the new contract after Apply.</summary>
        public int ContractId;

        public AcceptContractCommand(int player, OfferBoard board, int slot, int cityIndex = -1) : base(player) { Board = board; Slot = slot; CityIndex = cityIndex; }

        ContractBoard B(World w)
        {
            switch (Board)
            {
                case OfferBoard.TravelAgency: return w.TravelAgency;
                case OfferBoard.LastMinute: return w.LastMinute;
                case OfferBoard.Branch: return CityIndex >= 0 && CityIndex < w.ForeignContracts.Length ? w.ForeignContracts[CityIndex] : null;
            }
            return null;
        }

        protected override CommandResult Check(World w, Player p)
        {
            var b = B(w);
            if (b == null) return CommandResult.Fail("bad-board");
            if (Slot < 0 || Slot >= ContractBoard.Slots || b.IsEmpty(Slot)) return CommandResult.Fail("empty-slot");
            if (!IgnoreOpeningHours)
            {
                if (Board == OfferBoard.LastMinute && !CounterHours.LastMinuteOpen(w)) return CommandResult.Fail("closed");
                if (Board == OfferBoard.TravelAgency && !CounterHours.TravelAgencyOpen(w)) return CommandResult.Fail("closed");
            }
            if (Board == OfferBoard.Branch && !CounterHours.HasBranch(w, p, CityIndex)) return CommandResult.Fail("no-branch");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var b = B(w);
            var c = b.Offers[Slot].Clone();
            c.InPlan = PlanState.NotPlanned;
            c.Okay = 1;
            ContractId = p.Contracts.Add(c);
            b.Offers[Slot].Premium = 0;
            var src = Board == OfferBoard.LastMinute ? ContractSource.LastMinute : Board == OfferBoard.Branch ? ContractSource.Branch : ContractSource.TravelAgency;
            w.Events.Publish(new ContractAccepted { Player = p, Contract = c, Source = src });
        }
    }

    /// <summary>Accept a freight offer (contracts-and-routes.md §5.3); the slot becomes empty (−1 at the cargo office, 0 on branch lists).</summary>
    public sealed class AcceptFreightCommand : PlayerCommand
    {
        public OfferBoard Board;
        public int Slot;
        public int CityIndex = -1;
        public int FreightId;

        public AcceptFreightCommand(int player, OfferBoard board, int slot, int cityIndex = -1) : base(player) { Board = board; Slot = slot; CityIndex = cityIndex; }

        FreightBoard B(World w)
        {
            if (Board == OfferBoard.FreightOffice) return w.FreightOffice;
            if (Board == OfferBoard.BranchFreight || Board == OfferBoard.Branch)
                return CityIndex >= 0 && CityIndex < w.ForeignFreight.Length ? w.ForeignFreight[CityIndex] : null;
            return null;
        }

        protected override CommandResult Check(World w, Player p)
        {
            var b = B(w);
            if (b == null) return CommandResult.Fail("bad-board");
            if (Slot < 0 || Slot >= FreightBoard.Slots) return CommandResult.Fail("empty-slot");
            bool office = Board == OfferBoard.FreightOffice;
            if (office ? b.Offers[Slot].Premium < 0 : b.Offers[Slot].Premium <= 0) return CommandResult.Fail("empty-slot");
            if (!office && !CounterHours.HasBranch(w, p, CityIndex)) return CommandResult.Fail("no-branch");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var b = B(w);
            var f = b.Offers[Slot].Clone();
            f.InPlan = PlanState.NotPlanned;
            f.Okay = 1;
            f.TonsOpen = f.TonsLeft = f.Tons;
            FreightId = p.Freights.Add(f);
            b.Offers[Slot].Premium = Board == OfferBoard.FreightOffice ? -1 : 0;
            w.Events.Publish(new ContractAccepted { Player = p, Freight = f, Source = Board == OfferBoard.FreightOffice ? ContractSource.FreightOffice : ContractSource.BranchFreight });
        }
    }

    /// <summary>
    /// Voluntary cancel from the filofax (contracts-and-routes.md §2.9): pays the full penalty at once
    /// and removes the contract (or freight order) and its not-yet-started plan entries.
    /// </summary>
    public sealed class CancelContractCommand : PlayerCommand
    {
        public int ContractId;
        public bool Freight;
        public CancelContractCommand(int player, int contractId, bool freight = false) : base(player) { ContractId = contractId; Freight = freight; }

        protected override CommandResult Check(World w, Player p)
        {
            if (Freight ? p.Freights.Find(ContractId) == null : p.Contracts.Find(ContractId) == null) return CommandResult.Fail("no-contract");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            int penalty, from, to, kind = Freight ? FlightKind.Freight : FlightKind.Contract;
            if (Freight) { var f = p.Freights.Find(ContractId); penalty = f.Penalty; from = f.From; to = f.To; }
            else { var c = p.Contracts.Find(ContractId); penalty = c.Penalty; from = c.From; to = c.To; }
            // The original books a cancelled passenger order as freight penalty and vice versa (swapped
            // reason codes, a bug); OpenAT books each with its own reason. The penalty goes into the
            // "expense: penalties" statistic as in the original. It also counts against the "earn X"
            // mission like an expired order's penalty: the original forgets that here, which would let a
            // player dodge the mission loss by cancelling instead of letting the order expire.
            if (penalty > 0)
            {
                p.Book(-penalty, Freight ? MoneyReason.FreightPenalty : MoneyReason.ContractPenalty, ContractUsage.CityPair(w, from, to));
                p.Statistics.Add(StatId.ExpensePenalties, -penalty);
                p.Profit -= penalty;
            }
            foreach (var pl in p.Planes.Items)
            {
                var e = pl.Plan.Entries;
                bool changed = false;
                for (int i = 0; i < FlightPlan.Size; i++)
                    if (e[i].Kind == kind && e[i].ObjectId == ContractId && e[i].IsFuture(w.Clock.Date, w.Clock.Hour)) { e[i].Kind = 0; changed = true; }
                if (changed) { pl.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour); FlightPlanner.Check(w, p, pl); }
            }
            if (Freight) p.Freights.Remove(ContractId); else p.Contracts.Remove(ContractId);
            ContractUsage.UpdateContracts(w, p);
            ContractUsage.UpdateFreight(w, p);
        }
    }

    /// <summary>
    /// Contract advisor rating of an offer (contracts-and-routes.md §2.7): returns the advisor text id
    /// (Advi 7000–7004, 7010–7013). Freight uses 7100+ (Tons/22 flights, 7105 for free cargo).
    /// </summary>
    public static class ContractAdvisor
    {
        public static int Rate(World w, Player p, Contract c) => Rate(w, p, c, out _);

        /// <summary>As <see cref="Rate(World, Player, Contract)"/>; <paramref name="plane"/> is the plane named by 7013.</summary>
        public static int Rate(World w, Player p, Contract c, out Plane plane)
        {
            plane = null;
            int cost = w.ContractBasePrice(c.From, c.To, 100);
            int dist = w.Distance(c.From, c.To);
            bool range = false, seats = false, both = false;
            foreach (var pl in p.Planes.Items)
            {
                bool r = dist <= pl.Range * 1000 && w.FlightHours(c.From, c.To, pl.Speed) < 24;
                // Configured seats (economy + first class), not the type's seat count.
                bool s = pl.MaxPassengers + pl.MaxPassengersFC >= c.Persons;
                range |= r; seats |= s; both |= r && s;
                if (s && FerryInWindow(w, pl, c.From, c.To, c.Date, c.LastDate)) { plane = pl; return 7013; }
            }
            if (!range) return 7010;
            if (!seats) return 7011;
            if (!both) return 7012;
            return Rating(cost, c.Premium, c.Penalty, 7000);
        }

        public static int RateFreight(World w, Player p, FreightContract f) => RateFreight(w, p, f, out _);

        /// <summary>
        /// Freight rating: 7113 a future ferry flight in the window can carry it instead (no tonnage test, as
        /// in the original; <paramref name="plane"/> is that plane), 7110 no plane has the range, 7105 relief
        /// cargo, else the profit rating against Base(100) × ⌊tons/22⌋ trips (at least one).
        /// </summary>
        public static int RateFreight(World w, Player p, FreightContract f, out Plane plane)
        {
            plane = null;
            int dist = w.Distance(f.From, f.To);
            bool range = false;
            foreach (var pl in p.Planes.Items)
                range |= dist <= pl.Range * 1000 && w.FlightHours(f.From, f.To, pl.Speed) < 24;
            foreach (var pl in p.Planes.Items)
                if (FerryInWindow(w, pl, f.From, f.To, f.Date, f.LastDate)) { plane = pl; return 7113; }
            if (!range) return 7110;
            if (f.Premium == 0) return 7105;
            long cost = (long)w.ContractBasePrice(f.From, f.To, 100) * System.Math.Max(1, f.Tons / 22);
            return Rating(cost, f.Premium, f.Penalty, 7100);
        }

        /// <summary>A not-yet-started ferry flight of <paramref name="pl"/> on the pair, departing within [first, last].</summary>
        static bool FerryInWindow(World w, Plane pl, int from, int to, int first, int last)
        {
            var e = pl.Plan.Entries;
            for (int i = 0; i < FlightPlan.Size; i++)
                if (e[i].Kind == FlightKind.Auto && e[i].IsFuture(w.Clock.Date, w.Clock.Hour) && e[i].From == from && e[i].To == to
                    && e[i].StartDate >= first && e[i].StartDate <= last) return true;
            return false;
        }

        static int Rating(long cost, long premium, int penalty, int base0)
        {
            if (penalty == 0) return base0;
            if (cost * 10 <= premium * 5) return base0 + 1;
            if (cost * 10 <= premium * 9) return base0 + 2;
            if (cost * 10 <= premium * 14) return base0 + 3;
            return base0 + 4;
        }
    }
}
