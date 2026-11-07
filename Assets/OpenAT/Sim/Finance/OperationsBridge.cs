using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Finance
{
    /// <summary>
    /// What the finance domain needs to know about one plane of an airline. Filled by the operations
    /// domain through <see cref="IOperationsBridge.Planes"/> (see docs/spec/integration.md
    /// "Finance needs from Operations").
    /// </summary>
    public struct PlaneSnapshot
    {
        /// <summary>Stable plane id (the id workers store as their assigned plane).</summary>
        public int Id;
        public string Name;
        /// <summary>Condition ("Zustand") 0..100.</summary>
        public int Condition;
        /// <summary>Valuation <c>CalculatePrice</c>, already ÷10 for sponsored planes (economy.md §7).</summary>
        public long Value;
        /// <summary>Required pilots / attendants of the plane (type values "ptAnzPiloten"/"ptAnzBegleiter").</summary>
        public int RequiredPilots, RequiredAttendants;
        /// <summary>Maximum cabin crew including luxury slots ("MaxBegleiter", economy.md §9.4).</summary>
        public int MaxAttendants;
        /// <summary>Standing at a city (original <c>Ort</c> &gt; 0) — strike start check (sim-core.md §6.1).</summary>
        public bool StandingAtCity;
        /// <summary>Landing hour and date of the next flight, or −1 if there is none.</summary>
        public int NextLandingHour, NextLandingDate;
        /// <summary>Self-designed plane (no type id; ATFS 5/8).</summary>
        public bool IsDesigned;
        /// <summary>Seats ("ptPassagiere"), fuel use per hour ("ptVerbrauch"), speed ("ptGeschwindigkeit").</summary>
        public int Seats, FuelUse, Speed;
        /// <summary>Equipment levels 0..2: seats, trays, decor, engines, tyres, electronics, safety.</summary>
        public int SeatLevel, TrayLevel, DecorLevel, EngineLevel, TyreLevel, ElectronicsLevel, SafetyLevel;
    }

    /// <summary>One monthly rent of an airline (gate 2050, branch 2051, route 2052; economy.md §13).</summary>
    public struct RentItem
    {
        public int Reason;
        public long MonthlyRent;
        public RentItem(int reason, long monthlyRent) { Reason = reason; MonthlyRent = monthlyRent; }
    }

    /// <summary>
    /// The finance domain's view of the operations domain (planes, gates, branches, routes). The
    /// default <see cref="NullOperationsBridge"/> reports an airline without any operations; the
    /// integrator registers the real implementation with <c>world.Rules.Set&lt;IOperationsBridge&gt;(...)</c>.
    /// </summary>
    public interface IOperationsBridge : IRule
    {
        /// <summary>Planes of the airline in album index order (economy.md §7, §9.4, §12.4).</summary>
        IReadOnlyList<PlaneSnapshot> Planes(World w, Player p);
        /// <summary>All monthly rents of the airline (gates with any rent, branches and routes with rank &gt; 0).</summary>
        IReadOnlyList<RentItem> Rents(World w, Player p);
        int BranchCount(World w, Player p);
        int RouteCount(World w, Player p);
        /// <summary>Ids of the rented routes (both directions are separate ids).</summary>
        IReadOnlyList<int> RentedRouteIds(World w, Player p);
        /// <summary>Route image 0..100 of a route the airline rents (0 if not rented).</summary>
        int GetRouteImage(World w, Player p, int routeId);
        void SetRouteImage(World w, Player p, int routeId, int image);
        /// <summary>
        /// Takeover "absorb" (economy.md §6.8): move planes (plans cleared, at home), gates, routes and
        /// branches from <paramref name="target"/> to <paramref name="acquirer"/>; remove the target's
        /// passengers. Workers, money and shares are moved by the finance domain.
        /// </summary>
        void AbsorbOperations(World w, Player acquirer, Player target);
        /// <summary>Takeover "liquidate": sell all planes at full value (returned, not booked), free gates/routes/branches.</summary>
        long LiquidateOperations(World w, Player target);
    }

    /// <summary>Default bridge: no planes, no rents, no routes.</summary>
    public sealed class NullOperationsBridge : IOperationsBridge
    {
        static readonly PlaneSnapshot[] NoPlanes = new PlaneSnapshot[0];
        static readonly RentItem[] NoRents = new RentItem[0];
        static readonly int[] NoRoutes = new int[0];

        public IReadOnlyList<PlaneSnapshot> Planes(World w, Player p) => NoPlanes;
        public IReadOnlyList<RentItem> Rents(World w, Player p) => NoRents;
        public int BranchCount(World w, Player p) => 0;
        public int RouteCount(World w, Player p) => 0;
        public IReadOnlyList<int> RentedRouteIds(World w, Player p) => NoRoutes;
        public int GetRouteImage(World w, Player p, int routeId) => 0;
        public void SetRouteImage(World w, Player p, int routeId, int image) { }
        public void AbsorbOperations(World w, Player acquirer, Player target) { }
        public long LiquidateOperations(World w, Player target) => 0;
    }

    /// <summary>
    /// Test/integration helper: a bridge backed by plain lists that can be edited directly.
    /// </summary>
    public sealed class ListOperationsBridge : IOperationsBridge
    {
        public readonly List<PlaneSnapshot>[] PlaneLists = new List<PlaneSnapshot>[Player.Count];
        public readonly List<RentItem>[] RentLists = new List<RentItem>[Player.Count];
        public readonly int[] Branches = new int[Player.Count];
        public readonly Dictionary<int, int>[] RouteImages = new Dictionary<int, int>[Player.Count];

        public ListOperationsBridge()
        {
            for (int i = 0; i < Player.Count; i++)
            {
                PlaneLists[i] = new List<PlaneSnapshot>();
                RentLists[i] = new List<RentItem>();
                RouteImages[i] = new Dictionary<int, int>();
            }
        }

        public IReadOnlyList<PlaneSnapshot> Planes(World w, Player p) => PlaneLists[p.Index];
        public IReadOnlyList<RentItem> Rents(World w, Player p) => RentLists[p.Index];
        public int BranchCount(World w, Player p) => Branches[p.Index];
        public int RouteCount(World w, Player p) => RouteImages[p.Index].Count;

        public IReadOnlyList<int> RentedRouteIds(World w, Player p)
        {
            var ids = new List<int>(RouteImages[p.Index].Keys);
            ids.Sort();
            return ids;
        }

        public int GetRouteImage(World w, Player p, int routeId) => RouteImages[p.Index].TryGetValue(routeId, out var v) ? v : 0;
        public void SetRouteImage(World w, Player p, int routeId, int image) { if (RouteImages[p.Index].ContainsKey(routeId)) RouteImages[p.Index][routeId] = image; }

        public void AbsorbOperations(World w, Player acquirer, Player target)
        {
            PlaneLists[acquirer.Index].AddRange(PlaneLists[target.Index]);
            PlaneLists[target.Index].Clear();
            RentLists[acquirer.Index].AddRange(RentLists[target.Index]);
            RentLists[target.Index].Clear();
        }

        public long LiquidateOperations(World w, Player target)
        {
            long sum = 0;
            foreach (var pl in PlaneLists[target.Index]) sum += pl.Value;
            PlaneLists[target.Index].Clear();
            RentLists[target.Index].Clear();
            RouteImages[target.Index].Clear();
            return sum;
        }
    }
}
