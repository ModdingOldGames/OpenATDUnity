using System.Collections.Generic;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim
{
    /// <summary>
    /// World-level state of the operations domain (planes, flights, contracts, routes, gates, branches,
    /// auctions) and its registration (<see cref="InitOperations"/>). See docs/spec/findings/
    /// planes-and-flights.md and contracts-and-routes.md.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Per-pair distance cache (contracts-and-routes.md §1.2).</summary>
        public CityDistanceCache Distances;
        /// <summary>Designer part catalogue (planes-and-flights.md §5.2); replace with <see cref="PlaneDesignCatalog.Load"/> for the install's values.</summary>
        public PlaneDesignCatalog DesignCatalog = PlaneDesignCatalog.CreateDefault();

        /// <summary>Route mission target cities (sim-core.md §3.5), city ids (0 = none).</summary>
        public readonly int[] MissionCityIds = new int[6];
        /// <summary>Earthquake city of add-on mission 3 (sim-core.md §3.5), city id or 0.</summary>
        public int CrisisCityId;

        /// <summary>Museum offers (planes-and-flights.md §4.3); null = empty slot.</summary>
        public readonly Plane[] UsedPlanes = new Plane[3];
        public int MuseumRefillTicks = 6;

        public readonly AuctionBoard Auctions = new AuctionBoard();

        // Contract feasibility maxima (contracts-and-routes.md §2.3, "CalcPlayerMaximums").
        public int PlayerMaxPassengers = 90, PlayerMaxLength = 2000 * 1000, PlayerMinPassengers = 90, PlayerMinLength = 2000 * 1000;
        int _maximumsHour = -1;

        partial void InitOperations()
        {
            Distances = new CityDistanceCache(Data.Cities);
            if (Data.DesignCatalog != null) DesignCatalog = Data.DesignCatalog;
            BuildRouteTable();
            EnsureOperationsArrays();

            SetDefault<IFuelAccount>(new NoTankFuelAccount());
            SetDefault<IFlightFuelCost>(new OriginalFlightFuelCost());
            SetDefault<IMechanicLevel>(new DefaultMechanicLevel());
            SetDefault<IStrikeStatus>(new NoStrikes());
            SetDefault<ICrewMapping>(new CountCrewMapping());
            SetDefault<IFlightSabotageState>(new NoFlightSabotage());
            SetDefault<IPurchaseRebate>(new NoPurchaseRebate());
            SetDefault<IAirportGateCount>(new FixedGateCount());
            SetDefault<IRoutePassengerRule>(new OriginalRoutePassengers());
            SetDefault<IPlaneWearRule>(new OriginalPlaneWear());
            SetDefault<IMaintenanceRule>(new OriginalMaintenance());
            SetDefault<IRouteDemandRule>(new OriginalRouteDemand());
            SetDefault<IMaintenanceBooking>(new DirectMaintenanceBooking());
            SetDefault<IIncidentEconomics>(new BasicIncidentEconomics());

            OperationsSystems.Register(this);
            OperationsSetup.Register(this);
            // Finance/staff domain present: use its fuel, crew, mechanic, strike and statistics APIs.
            FinanceIntegration.Register(this);
        }

        void SetDefault<T>(T rule) where T : class, IRule
        {
            if (!Rules.Has<T>()) Rules.Set(rule);
        }

        /// <summary>Copies the route template table into this world (data-tables.md §1.2; demand is per game).</summary>
        void BuildRouteTable()
        {
            RouteTable.Clear();
            foreach (var d in Data.Routes) RouteTable.Add(Route.FromDef(d));
            for (int i = 0; i < RouteTable.Count; i++) RouteTable[i].Index = i;
        }

        /// <summary>Sizes the per-airline arrays that follow the city and route tables.</summary>
        public void EnsureOperationsArrays()
        {
            int nc = Data.Cities.Count, nr = RouteTable.Count;
            foreach (var p in Players)
            {
                if (p.RentCities.Length != nc) System.Array.Resize(ref p.RentCities, nc);
                if (p.CalledCities.Length != nc) System.Array.Resize(ref p.CalledCities, nc);
                if (p.RentRoutes.Length != nr)
                {
                    int old = p.RentRoutes.Length;
                    System.Array.Resize(ref p.RentRoutes, nr);
                    for (int i = old; i < nr; i++) p.RentRoutes[i] = new RentRoute();
                }
            }
            EnsureForeignBoards();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Distance in metres between two city ids.</summary>
        public int Distance(int cityA, int cityB) => Distances.Get(cityA, cityB);

        /// <summary>Flight duration in hours at <paramref name="speedKmh"/> (contracts-and-routes.md §1.3).</summary>
        public int FlightHours(int cityA, int cityB, int speedKmh) => CityGeo.FlightHours(Distance(cityA, cityB), speedKmh);

        public int CityIndex(int cityId) => Data.Cities.IndexOf(cityId);

        public int CityIdAt(int index) => index >= 0 && index < Data.Cities.Count ? Data.Cities.All[index].Id : 0;

        public Route RouteById(int id)
        {
            foreach (var r in RouteTable) if (r.Id == id) return r;
            return null;
        }

        public int RouteIndexOf(int id)
        {
            for (int i = 0; i < RouteTable.Count; i++) if (RouteTable[i].Id == id) return i;
            return -1;
        }

        public int RouteIndex(int from, int to)
        {
            for (int i = 0; i < RouteTable.Count; i++) if (RouteTable[i].From == from && RouteTable[i].To == to) return i;
            return -1;
        }

        /// <summary>Index of the reverse direction (first match from the lowest index), or −1.</summary>
        public int ReverseRouteIndex(int routeIndex)
        {
            if (routeIndex < 0 || routeIndex >= RouteTable.Count) return -1;
            var r = RouteTable[routeIndex];
            return RouteIndex(r.To, r.From);
        }

        /// <summary>Rule shortcut: flight fuel cost formulas.</summary>
        public IFlightFuelCost FuelCost => Rules.Get<IFlightFuelCost>();

        /// <summary>
        /// Reference fare base of a city pair (planes-and-flights.md §12.2, contracts-and-routes.md §3.6):
        /// Estimate(800 l/h, 800 km/h, no player) × 3 / 180 × 2. Follows today's kerosene price.
        /// </summary>
        public int ReferencePrice(int from, int to) => FuelCost.Estimate(this, null, from, to, 800, 800) * 3 / 180 * 2;

        /// <summary>Contract base price Base(f) = ceil(fuel/100) × f (contracts-and-routes.md §2.2).</summary>
        public int ContractBasePrice(int from, int to, int factor) => (FuelCost.Estimate(this, null, from, to, 8000, 700) + 99) / 100 * factor;

        /// <summary>
        /// Recomputes the contract feasibility maxima from the human airlines' planes (contracts-and-routes.md
        /// §2.3), at most once per game hour unless forced. Quirk kept: the comparison uses the configured
        /// seats but stores the type seat count; lengths are converted to metres with 32-bit overflow.
        /// </summary>
        public void CalcPlayerMaximums(bool force = false)
        {
            if (_maximumsHour == Clock.Hour && !force) return;
            _maximumsHour = Clock.Hour;
            int maxP = 90, maxL = 2000, minP = int.MaxValue, minL = int.MaxValue;
            foreach (var p in Players)
            {
                if (p.IsOut || p.IsComputer) continue;
                foreach (var pl in p.Planes.Items)
                {
                    int conf = pl.MaxPassengers + pl.MaxPassengersFC;
                    if (conf > maxP) maxP = pl.Seats;
                    if (pl.Range > maxL) maxL = pl.Range;
                    if (conf < minP) minP = pl.Seats;
                    if (pl.Range < minL) minL = pl.Range;
                }
            }
            PlayerMaxPassengers = maxP;
            PlayerMinPassengers = minP;
            PlayerMaxLength = unchecked(maxL * 1000);
            PlayerMinLength = unchecked(minL * 1000);
        }

        /// <summary>Advisor message for a player (std text group "Advi").</summary>
        public void Advise(Player p, int textId, params object[] args) => Events.Publish(new AdvisorMessage(p, AdvisorMessage.Secretary, textId, args));

        /// <summary>The original's "RobotUse(ROBOT_USE_TRAVELHOLDING)" world flag (sim-core.md §3.7): off only in add-on mission 6.</summary>
        public bool TravelHolding => Difficulty != 16;

        /// <summary>Absolute game minute (day × 1440 + minute of day).</summary>
        public long AbsoluteMinute => (long)Clock.Date * 1440 + Clock.MinuteOfDay;

        /// <summary>Random-access to every plane of an airline by id.</summary>
        public Plane FindPlane(Player p, int planeId) => p.Planes.Find(planeId);
    }
}
