using OpenAT.Sim.Airport;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim
{
    /// <summary>Flight counters that missions and statistics read (planes-and-flights.md §14).</summary>
    public sealed class OperationsCounters
    {
        /// <summary>Departures today ("NumFlights", reset when leaving the morning briefing).</summary>
        public int FlightsToday;
        /// <summary>Economy passengers of route and contract flights ("NumPassengers").</summary>
        public long Passengers;
        /// <summary>Miles flown ("NumMiles").</summary>
        public long Miles;
        /// <summary>Contract flights booked ("NumAuftraege").</summary>
        public int Contracts;
        /// <summary>Freight tons delivered ("NumFracht") and free relief tons ("NumFrachtFree").</summary>
        public long Freight, FreightFree;
        /// <summary>Add-on mission 9 order flights ("NumOrderFlights") and today's count.</summary>
        public int OrderFlights, OrderFlightsToday;
    }

    /// <summary>Fleet, gates and branch offices of an airline (planes-and-flights.md §3–8, contracts-and-routes.md §7–8).</summary>
    public sealed partial class Player
    {
        /// <summary>The airline's planes (album, sorted by name after purchases).</summary>
        public readonly Album<Plane> Planes = new Album<Plane>(0, 10);
        public readonly PlayerGates Gates = new PlayerGates();
        /// <summary>Branch offices, indexed by city table index.</summary>
        public RentCity[] RentCities = new RentCity[0];
        public readonly OperationsCounters FlightCounters = new OperationsCounters();

        /// <summary>The player is inside the equipment room: pending refits wait (§6.2). Set by the view.</summary>
        public bool InPlanePropRoom;
        /// <summary>The player is dragging a flight in the planner: the 7-day horizon cut waits (§10.2 step 7). Set by the view.</summary>
        public bool PlannerDragging;
        /// <summary>First route flight already flown (newspaper headline once).</summary>
        public bool HasFlownRoutes;
        /// <summary>"DoRoutes": human route-usage counter (AI helper).</summary>
        public int RouteUsage;
        /// <summary>Times a flight found no free gate today (AI gate bidding).</summary>
        public int OutOfGates;
        public bool VisitedMuseumToday;
        /// <summary>Equipment template of the copy/paste buttons (§6.6).</summary>
        public int[] EquipmentClipboard;
        public double AttendantRatioClipboard;

        public RentCity BranchAt(int cityIndex) => cityIndex >= 0 && cityIndex < RentCities.Length ? RentCities[cityIndex] : default;

        /// <summary>Number of planes owned.</summary>
        public int PlaneCount => Planes.Count;
    }
}
