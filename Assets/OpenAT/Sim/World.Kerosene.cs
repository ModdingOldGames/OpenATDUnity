using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim
{
    /// <summary>
    /// Kerosene market (economy.md §12.1, sim-core.md §6.5). Owned by the finance domain: the daily
    /// price process is <see cref="KerosenePriceSystem"/> with the formula in
    /// <see cref="IKerosenePriceRule"/>; other domains only read <see cref="Kerosene"/> (or use
    /// <see cref="Player.FuelPricePerBarrel"/> / <see cref="FuelCost"/>).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Today's market price per barrel (300..700).</summary>
        public int Kerosene = 500;
        /// <summary>Price history, newest at index 9.</summary>
        public readonly int[] KerosenePast = new int[10];

        /// <summary>
        /// Game start (economy.md §2.3): price 500, history[9] = 508, entries 8..0 back-filled by the
        /// noisy walk (seed Date, unclamped): past[c] = past[c+1] + Rand(20) − 10
        /// − ⌊sin((Date − c + Rand(6))/3)·20⌋ − ⌊sin((Date − c)·1.7)·20⌋.
        /// </summary>
        public void InitKerosene()
        {
            Kerosene = Tunables?.Int(Modding.TunableKeys.KeroseneStart, 500) ?? 500;   // tunable (Sim/Modding), original 500
            KerosenePast[9] = Kerosene + 8;
            int date = Clock.Date;
            var r = new TeakRand(date);
            for (int c = KerosenePast.Length - 2; c >= 0; c--)
            {
                int a = r.Rand(20);
                int b = r.Rand(6);
                KerosenePast[c] = KerosenePast[c + 1] + a - 10
                    - (int)(System.Math.Sin((date - c + b) / 3.0) * 20)
                    - (int)(System.Math.Sin((date - c) * 1.7) * 20);
            }
        }

        /// <summary>Midnight step 17: shift the history, new price, store it as history[9].</summary>
        public void UpdateKerosenePrice()
        {
            for (int c = 0; c < KerosenePast.Length - 1; c++) KerosenePast[c] = KerosenePast[c + 1];
            Kerosene = Rules.Get<IKerosenePriceRule>().NextPrice(this, Kerosene);
            KerosenePast[9] = Kerosene;
        }
    }
}
