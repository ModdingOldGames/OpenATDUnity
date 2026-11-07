using OpenAT.Sim.Core;

namespace OpenAT.Sim.Data
{
    /// <summary>
    /// A directed route from data/routen.csv (each csv row creates A→B and B→A).
    /// Mutable world state of the route (demand) lives here too because the original keeps it in
    /// the route table (docs/spec/findings/data-tables.md §1.2, contracts-and-routes.md).
    /// </summary>
    public sealed class RouteDef : IContent
    {
        public const int IdBase = 0x1000000;

        public int Id { get; set; }
        /// <summary>Index after sorting by departure city name.</summary>
        public int Index;
        /// <summary>1 = regional, 2 = long-haul.</summary>
        public int Level;
        public int FromCity, ToCity;
        /// <summary>Monthly licence fee.</summary>
        public int Rent;
        public double Factor;
        /// <summary>Demand ("Bedarf"), updated nightly.</summary>
        public int Demand;
        public bool NewInDeluxe;

        public override string ToString() => $"route {Id:X} {FromCity:X}->{ToCity:X}";
    }
}
