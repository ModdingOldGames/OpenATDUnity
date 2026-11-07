using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim
{
    /// <summary>Contracts and freight orders held by an airline (contracts-and-routes.md §2, §5).</summary>
    public sealed partial class Player
    {
        /// <summary>Passenger contracts (album: capacity 40, grows by 10 when fewer than 3 slots are free).</summary>
        public readonly Album<Contract> Contracts = new Album<Contract>(40, 10, 3);
        public readonly Album<FreightContract> Freights = new Album<FreightContract>(40, 10, 3);
        /// <summary>The airline's contract list random stream (start contracts, sim-core.md §4.2).</summary>
        public readonly TeakRand ContractRandom = new TeakRand();
        /// <summary>Branch cities already phoned (greeting dialogue shown once), by city index.</summary>
        public bool[] CalledCities = new bool[0];
    }
}
