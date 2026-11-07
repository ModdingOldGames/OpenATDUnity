using OpenAT.Sim.Finance;

namespace OpenAT.Sim
{
    /// <summary>
    /// Economic side of events owned by other domains (economy.md §6.4, §8.3, §8.4). The calling
    /// domain applies the non-economic parts (route image, breakdown records, messages).
    /// </summary>
    public sealed partial class Player
    {
        static readonly double[] SabotagePriceFactor = { 1, 0.95, 0.90, 0.85, 0.75, 1 };
        static readonly long[] SabotageDamage = { 0, 0, 2000, 8000, 40000, 70000 };
        static readonly int[] SabotageImage = { 0, 0, -2, -3, -8, 0 };
        /// <summary>Route image percentage of flight-time sabotage types 1..5 (image = image·pct/100, applied by the sabotage domain).</summary>
        public static readonly int[] SabotageRouteImagePercent = { 100, 99, 95, 90, 50, 100 };

        /// <summary>
        /// Flight-time sabotage type 1..5 hits this airline (economy.md §6.4): price ×0.95/0.90/0.85/0.75/—,
        /// trust −type (floor 0), damage 2 000/8 000/40 000/70 000 (3500), image −2/−3/−8.
        /// </summary>
        public void ApplySabotageEconomics(int type)
        {
            if (type < 1 || type > 5) return;
            if (SabotagePriceFactor[type] != 1) ShockSharePrice(SabotagePriceFactor[type]);
            ChangeTrust(-type, 0);
            if (SabotageDamage[type] != 0) Book(-SabotageDamage[type], MoneyReason.SabotageDamage);
            if (SabotageImage[type] != 0) ChangeImage(SabotageImage[type]);
        }

        /// <summary>
        /// In-flight breakdown from poor condition with passengers (economy.md §6.4/§8.3, sim-core.md §6.3):
        /// image −2, "image got worse" for the boss, share price ×0.7, trust −1 if &gt; 4.
        /// </summary>
        public void ApplyBreakdownEconomics()
        {
            ChangeImage(-2);
            ImageGotWorse = true;
            ShockSharePrice(0.7);
            if (TrustedDividend > 4) TrustedDividend--;
        }

        /// <summary>Seasonal incident without the matching upgrade (economy.md §8.4): −75 000 (3501).</summary>
        public void ApplySeasonIncidentCost() => Book(-75000, MoneyReason.SeasonIncident);
    }
}
