using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Airport
{
    public enum AuctionKind { City, Route, Gate }

    /// <summary>
    /// Bid on an auction slip (contracts-and-routes.md §6.2): +10 % and lead. Branches only if the
    /// bidder has no branch in that city. <see cref="OnEntry"/> = the slip as it was when the bidder
    /// entered the room (one raise per visit).
    /// </summary>
    public sealed class BidCommand : PlayerCommand
    {
        public AuctionKind Kind;
        public int Slot;
        public AuctionSlip OnEntry;
        public BidCommand(int player, AuctionKind kind, int slot, AuctionSlip onEntry = null) : base(player) { Kind = kind; Slot = slot; OnEntry = onEntry; }

        AuctionSlip S(World w)
        {
            if (Slot < 0 || Slot >= AuctionBoard.Slots) return null;
            switch (Kind) { case AuctionKind.City: return w.Auctions.Cities[Slot]; case AuctionKind.Route: return w.Auctions.Routes[Slot]; default: return w.Auctions.Gates[Slot]; }
        }

        protected override CommandResult Check(World w, Player p)
        {
            var s = S(w);
            if (s == null) return CommandResult.Fail("bad-slot");
            if (Kind == AuctionKind.Route ? s.ObjectId <= 0 : s.ObjectId < 0) return CommandResult.Fail("empty-slot");
            if (Kind == AuctionKind.City && p.RentCities[s.ObjectId].Rank != 0) return CommandResult.Fail("has-branch");
            if (s.Player == p.Index && (OnEntry == null || OnEntry.Player == s.Player)) return CommandResult.Fail("leading");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => AuctionBoard.Bid(w, p, S(w), OnEntry);
    }

    /// <summary>Close a branch office (contracts-and-routes.md §8): rank 0; regained only through an auction. The home city cannot be dropped here.</summary>
    public sealed class DropBranchCommand : PlayerCommand
    {
        public int CityIndex;
        public DropBranchCommand(int player, int cityIndex) : base(player) { CityIndex = cityIndex; }

        protected override CommandResult Check(World w, Player p)
        {
            if (CityIndex < 0 || CityIndex >= p.RentCities.Length) return CommandResult.Fail("bad-city");
            if (p.RentCities[CityIndex].Rank == 0) return CommandResult.Fail("no-branch");
            if (w.CityIdAt(CityIndex) == w.HomeCity) return CommandResult.Fail("home");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => p.RentCities[CityIndex].Rank = 0;
    }
}
