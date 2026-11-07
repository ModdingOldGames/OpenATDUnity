using OpenAT.Sim.Contracts;

namespace OpenAT.Sim
{
    /// <summary>
    /// Global offer pools, shared by all airlines (contracts-and-routes.md §2.5): Last Minute, travel
    /// agency, cargo office and per-city branch lists.
    /// </summary>
    public sealed partial class World
    {
        public readonly ContractBoard LastMinute = new ContractBoard();
        public readonly ContractBoard TravelAgency = new ContractBoard();
        public readonly FreightBoard FreightOffice = new FreightBoard();
        /// <summary>Branch offer lists per city index ("AuslandsAuftraege").</summary>
        public ContractBoard[] ForeignContracts = new ContractBoard[0];
        /// <summary>Branch freight lists per city index ("AuslandsFrachten").</summary>
        public FreightBoard[] ForeignFreight = new FreightBoard[0];

        void EnsureForeignBoards()
        {
            int n = Data.Cities.Count;
            if (ForeignContracts.Length != n)
            {
                ForeignContracts = new ContractBoard[n];
                ForeignFreight = new FreightBoard[n];
                for (int i = 0; i < n; i++) { ForeignContracts[i] = new ContractBoard(); ForeignFreight[i] = new FreightBoard(); }
            }
        }
    }
}
