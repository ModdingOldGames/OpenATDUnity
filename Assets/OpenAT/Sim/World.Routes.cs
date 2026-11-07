using System.Collections.Generic;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim
{
    /// <summary>The world's route table with the shared demand pools (contracts-and-routes.md §3.1–3.2).</summary>
    public sealed partial class World
    {
        /// <summary>Directed routes, sorted by departure city name; mission routes are appended.</summary>
        public readonly List<Route> RouteTable = new List<Route>();
    }
}
