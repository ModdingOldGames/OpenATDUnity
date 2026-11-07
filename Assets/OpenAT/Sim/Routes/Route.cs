using OpenAT.Sim.Core;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Routes
{
    /// <summary>
    /// A directed route of the world's route table ("CRoute", contracts-and-routes.md §3.1): a per-game
    /// copy of <see cref="RouteDef"/> plus the shared demand pool <see cref="Demand"/> ("Bedarf").
    /// </summary>
    public sealed class Route : IContent
    {
        public int Id { get; set; }
        /// <summary>Index in the world's route table (sorted by departure city name).</summary>
        public int Index;
        /// <summary>1 regional, 2 long-haul.</summary>
        public int Level;
        public int From, To;
        /// <summary>Monthly licence fee of the table.</summary>
        public int Rent;
        public double Factor;
        /// <summary>Persons waiting today, shared by all airlines.</summary>
        public int Demand;
        public bool NewInDeluxe;
        /// <summary>Added by the route mission (contracts-and-routes.md §3.1).</summary>
        public bool Synthetic;

        public static Route FromDef(RouteDef d) => new Route
        {
            Id = d.Id, Index = d.Index, Level = d.Level, From = d.FromCity, To = d.ToCity,
            Rent = d.Rent, Factor = d.Factor, NewInDeluxe = d.NewInDeluxe,
        };

        public bool Touches(int city) => From == city || To == city;

        public override string ToString() => $"route {Index} {From:X}->{To:X}";
    }

    /// <summary>One airline's licence state for one directed route ("CRentRoute", §3.1). Defaults as in the original.</summary>
    public sealed class RentRoute
    {
        /// <summary>0 = not licensed, 1–3 = licence rank.</summary>
        public int Rank;
        /// <summary>Days since the last flight (cap 99).</summary>
        public int LastFlown = 99;
        /// <summary>Average days between flights: set to 30 when licensed; the original never updates it (bug, §11.1),
        /// with <see cref="GameOptions.FixRouteAvgFlown"/> it is a nightly moving average of <see cref="LastFlown"/>.</summary>
        public int AvgFlown;
        /// <summary>Moving average seat load % (economy / first class).</summary>
        public int Load, LoadFC;
        /// <summary>Moving average of the % of route demand carried ("RoutenAuslastung").</summary>
        public int RouteShare;
        /// <summary>Passengers carried today (for the nightly share update).</summary>
        public int CarriedToday;
        /// <summary>Route image 0..100 (a byte).</summary>
        public int Image;
        /// <summary>Monthly fee this airline pays.</summary>
        public int Rent;
        public int TicketPrice, TicketPriceFC;
        /// <summary>AI-only loss-days counter.</summary>
        public int DaysWithLoss;
        /// <summary>Days with share &lt; 10 %; for unlicensed routes the cooldown clock since a revocation (99 = none).</summary>
        public int DaysLow = 99;

        public bool Licensed => Rank != 0;
    }
}
