using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Routes
{
    /// <summary>
    /// License a route at the route office (contracts-and-routes.md §3.3). Refusals: rank above 3
    /// (NORENTROUTE1 → "rank-full"), not eligible (NORENTROUTE2 → "not-eligible"), cooldown after a
    /// revocation (NORENTROUTE4 → "cooldown").
    /// </summary>
    public sealed class RentRouteCommand : PlayerCommand
    {
        public int RouteIndex;
        public RentRouteCommand(int player, int routeIndex) : base(player) { RouteIndex = routeIndex; }

        protected override CommandResult Check(World w, Player p)
        {
            if (RouteIndex < 0 || RouteIndex >= w.RouteTable.Count) return CommandResult.Fail("no-route");
            var rr = p.RentRoutes[RouteIndex];
            if (rr.Licensed) return CommandResult.Fail("already-rented");
            if (!RouteOps.IsRentable(w, p, RouteIndex))
                return rr.DaysLow < 7 ? CommandResult.Fail("cooldown") : CommandResult.Fail("not-eligible");
            if (RouteOps.NextRank(w, RouteIndex) > 3) return CommandResult.Fail("rank-full");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => RouteOps.RentAtOffice(w, p, RouteIndex);
    }

    /// <summary>Drop a route licence (both directions, §3.4); refused while a plan entry uses either direction (NORENTROUTE3).</summary>
    public sealed class DropRouteCommand : PlayerCommand
    {
        public int RouteIndex;
        public DropRouteCommand(int player, int routeIndex) : base(player) { RouteIndex = routeIndex; }

        protected override CommandResult Check(World w, Player p)
        {
            if (RouteIndex < 0 || RouteIndex >= w.RouteTable.Count) return CommandResult.Fail("no-route");
            if (!p.RentRoutes[RouteIndex].Licensed) return CommandResult.Fail("not-rented");
            if (RouteOps.InUse(w, p, RouteIndex)) return CommandResult.Fail("in-use");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => RouteOps.Drop(w, p, RouteIndex);
    }

    /// <summary>Set the ticket prices of a licensed route (§3.6; clamps, economy copied to the reverse direction).</summary>
    public sealed class SetTicketPricesCommand : PlayerCommand
    {
        public int RouteIndex, Economy, FirstClass;
        public SetTicketPricesCommand(int player, int routeIndex, int economy, int firstClass) : base(player) { RouteIndex = routeIndex; Economy = economy; FirstClass = firstClass; }

        /// <summary>Preset ½·, 1·, 2·, 4·RefPrice (index 0..3) with first class = 2 × economy.</summary>
        public static SetTicketPricesCommand Preset(World w, int player, int routeIndex, int preset)
        {
            int eco = RouteOps.Preset(w, routeIndex, preset);
            return new SetTicketPricesCommand(player, routeIndex, eco, eco * 2);
        }

        protected override CommandResult Check(World w, Player p)
        {
            if (RouteIndex < 0 || RouteIndex >= w.RouteTable.Count) return CommandResult.Fail("no-route");
            if (!p.RentRoutes[RouteIndex].Licensed) return CommandResult.Fail("not-rented");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => RouteOps.SetPrices(w, p, RouteIndex, Economy, FirstClass);
    }
}
