using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Contracts
{
    /// <summary>Where a contract came from (statistics and events).</summary>
    public enum ContractSource { TravelAgency, LastMinute, Branch, Start, ManagerOrder, SparePart, FreightOffice, BranchFreight }

    /// <summary>Planning state of a contract ("InPlan", contracts-and-routes.md §2.1).</summary>
    public static class PlanState
    {
        public const int NotPlanned = 0;
        public const int Planned = 1;
        public const int Done = -1;
    }

    /// <summary>
    /// A passenger contract ("CAuftrag", contracts-and-routes.md §2.1). In offer lists a
    /// <see cref="Premium"/> ≤ 0 marks an empty slot.
    /// </summary>
    public sealed class Contract : IAlbumItem
    {
        public int Id { get; set; }
        /// <summary>City ids.</summary>
        public int From, To;
        /// <summary>Passengers; all must fit into one plane (economy + first class).</summary>
        public int Persons;
        /// <summary>First and last allowed departure day (inclusive).</summary>
        public int Date, LastDate;
        public int Premium;
        public int Penalty;
        /// <summary><see cref="PlanState"/>.</summary>
        public int InPlan;
        /// <summary>1 = plan fine, 0 = a planned flight violates the contract.</summary>
        public int Okay = 1;
        /// <summary>Add-on mission 9 manager order flag (never set by the original generator, §2.10).</summary>
        public bool ManagerOrder;

        public Contract Clone() => (Contract)MemberwiseClone();

        public override string ToString() => $"contract {From:X}->{To:X} {Persons}p d{Date}-{LastDate} +{Premium}/-{Penalty} plan{InPlan}";
    }

    /// <summary>
    /// A freight contract ("CFracht", contracts-and-routes.md §5.1). In the cargo office list
    /// <see cref="Premium"/> &lt; 0 marks an empty slot; 0 is a valid free relief cargo.
    /// </summary>
    public sealed class FreightContract : IAlbumItem
    {
        public int Id { get; set; }
        public int From, To;
        public int Tons;
        /// <summary>Tons not yet flown.</summary>
        public int TonsLeft;
        /// <summary>Tons neither flown nor planned.</summary>
        public int TonsOpen;
        public int Date, LastDate;
        public int Premium;
        public int Penalty;
        public int InPlan;
        public int Okay = 1;

        public FreightContract Clone() => (FreightContract)MemberwiseClone();

        public override string ToString() => $"freight {From:X}->{To:X} {Tons}t ({TonsLeft} left) d{Date}-{LastDate} +{Premium}/-{Penalty}";
    }

    /// <summary>A board of 6 passenger offers with its own random stream and refill counter (§2.5).</summary>
    public sealed class ContractBoard
    {
        public const int Slots = 6;
        public readonly Contract[] Offers = new Contract[Slots];
        public readonly Core.TeakRand Random = new Core.TeakRand();
        /// <summary>Refill tick counter (+1 every 5 game minutes, 6 after a full refill).</summary>
        public int RefillTicks = 6;

        public ContractBoard() { for (int i = 0; i < Slots; i++) Offers[i] = new Contract { Premium = 0 }; }

        public bool IsEmpty(int slot) => Offers[slot] == null || Offers[slot].Premium <= 0;
    }

    /// <summary>A board of 6 freight offers (§5).</summary>
    public sealed class FreightBoard
    {
        public const int Slots = 6;
        public readonly FreightContract[] Offers = new FreightContract[Slots];
        public readonly Core.TeakRand Random = new Core.TeakRand();
        public int RefillTicks = 6;

        public FreightBoard() { for (int i = 0; i < Slots; i++) Offers[i] = new FreightContract { Premium = -1 }; }
    }
}
