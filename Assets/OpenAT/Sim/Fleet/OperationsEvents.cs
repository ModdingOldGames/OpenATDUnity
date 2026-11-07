using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Fleet
{
    // Events of the operations domain. They carry the numbers that the finance statistics need
    // (planes-and-flights.md §14), so the statistics owner can count without reaching into flights.

    /// <summary>A flight was booked at departure (planes-and-flights.md §12.4).</summary>
    public sealed class FlightBooked : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        /// <summary>Copy of the entry as booked.</summary>
        public FlightEntry Entry;
        public int Kind;
        public int DistanceM;
        /// <summary>Income E (ticket revenue, premium, ferry credit).</summary>
        public long Income;
        /// <summary>Market fuel cost of the booking (tank units excluded).</summary>
        public long FuelCost;
        /// <summary>Food cost (route and contract flights).</summary>
        public long FoodCost;
        /// <summary>Calculatory cost of tank fuel used (no cash movement).</summary>
        public long TankCost;
        public int TankUnitsUsed;
        /// <summary>Flight result after the hidden computer bonus (booked with reason 2099 + kind).</summary>
        public long Saldo;
        public long BonusPaid;
        public int Passengers, PassengersFC;
        /// <summary>Route/contract flight touching the home airport (STAT_PASSAGIERE_HOME).</summary>
        public bool TouchesHome;
        /// <summary>Freight tons delivered by this flight (0 otherwise) and whether they were free relief cargo.</summary>
        public int FreightTons;
        public bool FreightFree;
        /// <summary>Contract premium credited (contract counter +1 when true).</summary>
        public bool ContractCounted;
        /// <summary>Add-on mission 9 order flight counted.</summary>
        public bool OrderFlightCounted;
        /// <summary>Home departure of a route/contract flight without a gate (image −2, advice 2353).</summary>
        public bool NoGate;
    }

    /// <summary>A flight landed (planes-and-flights.md §11.5) and its passengers were evaluated (§13).</summary>
    public sealed class FlightLanded : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public FlightEntry Entry;
        public int City;
        public bool AtHome;
        /// <summary>Home arrival without a gate.</summary>
        public bool NoGate;
    }

    /// <summary>Satisfaction result of an arrival (STAT_ZUFR/UNZUFR_PASSAGIERE, planes-and-flights.md §13).</summary>
    public sealed class PassengersEvaluated : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public int Satisfied, Unsatisfied;
    }

    /// <summary>A departure was delayed by one hour (STAT_VERSPAETUNG, planes-and-flights.md §11.2).</summary>
    public sealed class FlightDelayed : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public int EntryIndex;
        /// <summary>Advisor text id (Advi 2350–2358) naming the cause.</summary>
        public int ReasonTextId;
    }

    /// <summary>A plane got a technical problem (planes-and-flights.md §7.5).</summary>
    public sealed class PlaneBreakdown : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public int Hours;
        public Contract SparePartContract;
    }

    /// <summary>
    /// An in-flight incident (planes-and-flights.md §7.6). Quality incidents: the finance domain applies
    /// share price ×0.7 and trusted dividend −1 (if &gt; 4); seasonal incidents cost 75 000 (booked here).
    /// </summary>
    public sealed class PlaneIncident : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public int Code;
        public bool Seasonal;
    }

    /// <summary>Plane bought (broker, museum, designer) or sold (museum).</summary>
    public sealed class PlaneTraded : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public long Price;
        public bool Sold;
        /// <summary>"broker", "museum", "designer", "start".</summary>
        public string Source;
    }

    /// <summary>Nightly maintenance was booked (reason 3110, STAT_WARTUNG).</summary>
    public sealed class MaintenanceBooked : ISimEvent
    {
        public Player Player;
        public long Amount;
    }

    /// <summary>Equipment refit cost or refund booked (reason 2110; STAT_A/E_SONSTIGES).</summary>
    public sealed class PlaneRefitted : ISimEvent
    {
        public Player Player;
        public Plane Plane;
        public long Cost;
    }

    /// <summary>A contract or freight order was accepted (per-day contract statistics).</summary>
    public sealed class ContractAccepted : ISimEvent
    {
        public Player Player;
        public Contract Contract;
        public FreightContract Freight;
        public ContractSource Source;
    }

    /// <summary>A contract penalty was paid (STAT_A_STRAFEN), or a no-penalty expiry letter sent.</summary>
    public sealed class ContractExpired : ISimEvent
    {
        public Player Player;
        public Contract Contract;
        public FreightContract Freight;
        public long Penalty;
    }

    /// <summary>A letter for the player's in-tray (text group/id read from the install by the view).</summary>
    public sealed class LetterSent : ISimEvent
    {
        public readonly Player Player;
        /// <summary>Resource group ("Lett", "Misc").</summary>
        public readonly string Group;
        /// <summary>First text id (subject); the body/closing follow at +1/+2 where the original does so.</summary>
        public readonly int TextId;
        public readonly object[] Args;
        public LetterSent(Player p, string group, int textId, params object[] args) { Player = p; Group = group; TextId = textId; Args = args; }
    }

    /// <summary>The first route flight of an airline: newspaper headline (Misc 2010 + airlines that flew before).</summary>
    public sealed class FirstRouteFlight : ISimEvent
    {
        public Player Player;
        public int AirlinesBefore;
    }

    /// <summary>Route licence changes.</summary>
    public sealed class RouteLicenceChanged : ISimEvent
    {
        public Player Player;
        public int RouteIndex;
        /// <summary>"rented", "dropped", "revoked", "auction".</summary>
        public string Change;
    }

    /// <summary>Auction board resolution result (one per won slip).</summary>
    public sealed class AuctionWon : ISimEvent
    {
        public Player Player;
        /// <summary>"city", "route", "gate".</summary>
        public string Kind;
        public int ObjectId;
        public long Price;
    }
}
