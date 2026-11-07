using OpenAT.Sim.Routes;

namespace OpenAT.Sim
{
    /// <summary>Route licences of an airline (contracts-and-routes.md §3).</summary>
    public sealed partial class Player
    {
        /// <summary>Licence state per directed route, indexed by the world route table index.</summary>
        public RentRoute[] RentRoutes = new RentRoute[0];

        public RentRoute RouteState(int routeIndex) => routeIndex >= 0 && routeIndex < RentRoutes.Length ? RentRoutes[routeIndex] : null;

        public int LicensedRouteCount
        {
            get { int n = 0; foreach (var r in RentRoutes) if (r != null && r.Rank != 0) n++; return n; }
        }
    }
}
