using OpenAT.Sim.Finance;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>
    /// Recurring costs (rents, security, maintenance contract, wealth tax), the bodyguard rebate,
    /// statistics and mission counters of an airline (economy.md §9.8, §12.4, §13, §14, §15.5, §16).
    /// </summary>
    public sealed partial class Player
    {
        /// <summary>Monthly mechanic fee per plane by contract level 0..3 (economy.md §12.4, gRepairPrice).</summary>
        public static readonly long[] MechanicMonthlyFee = { 1200, 18000, 48000, 150000 };

        /// <summary>Security subscription bits 0..11 and their nightly price (economy.md §14); per-plane bits marked.</summary>
        public static readonly long[] SecurityFee = { 25000, 20000, 25000, 30000, 30000, 30000, 30000, 30000, 40000, 0, 50000, 50000 };
        public const int SecurityPerPlaneMask = 0x0040 | 0x0080 | 0x0100 | 0x0400 | 0x0800;

        /// <summary>Maintenance contract level 0 cleaner, 1 apprentice, 2 mechanic, 3 engineer.</summary>
        public int MechMode = 2;
        /// <summary>Mechanic trust; levels 2..3 are offered at 2 (set by the workshop dialogue).</summary>
        public int MechTrust;
        /// <summary>Subscribed security services (bitmask). Effects belong to the security domain.</summary>
        public int SecurityFlags;
        /// <summary>"Image got worse" (in-flight breakdown) for the boss's next review.</summary>
        public bool ImageGotWorse;
        /// <summary>Set when the maintenance of the current night has been booked.</summary>
        public bool MaintenanceBookedToday;

        /// <summary>The 40 statistic series (economy.md §16).</summary>
        public readonly PlayerStatistics Statistics = new PlayerStatistics();
        /// <summary>Mission counters (sim-core.md §3.4).</summary>
        public readonly MissionCounters Counters = new MissionCounters();

        /// <summary>
        /// Security-guard rebate after a purchase (economy.md §9.8): talent &gt; 20 → price/100·(talent/10)
        /// credited (3130) with advisor message <c>Advi 6000</c>. Returns the rebate.
        /// </summary>
        public long BodyguardRebate(long price, bool inDutyFree = false)
        {
            int q = AdvisorQuality(AdvisorType.Security);
            if (q <= 20) return 0;
            World?.Events.Publish(new AdvisorMessage(this, (int)AdvisorType.Security, inDutyFree ? 6001 : 6000, price, q / 10));
            long delta = price / 100 * (q / 10);
            Book(delta, MoneyReason.BodyguardRebate);
            return delta;
        }

        int BridgePlaneCount() => World != null ? World.OperationsBridge.Planes(World, this).Count : 0;

        /// <summary>Nightly security fees: fixed items + per-plane items × planes (economy.md §14).</summary>
        public long SecurityCosts(int planes)
        {
            long fix = 0, perPlane = 0;
            for (int b = 0; b < SecurityFee.Length; b++)
                if ((SecurityFlags & (1 << b)) != 0)
                {
                    if (((1 << b) & SecurityPerPlaneMask) != 0) perPlane += SecurityFee[b];
                    else fix += SecurityFee[b];
                }
            return fix + perPlane * planes;
        }

        /// <summary>Books the nightly security fees (3503, stat other expenses) when &gt; 0.</summary>
        public void BookSecurityCosts()
        {
            long c = SecurityCosts(BridgePlaneCount());
            if (c <= 0) return;
            Book(-c, MoneyReason.SecurityFees);
            Statistics.Add(StatId.ExpenseOther, -c);
        }

        /// <summary>
        /// Daily rents (economy.md §13, original <c>BookBuroRent</c>): Σ rent/30 per item for gates
        /// (2050, always booked), branches (2051) and routes (2052, both only when non-zero); stat 34.
        /// </summary>
        public void BookRents()
        {
            var rents = World != null ? World.OperationsBridge.Rents(World, this) : null;
            int[] reasons = { MoneyReason.GateRent, MoneyReason.BranchRent, MoneyReason.RouteRent };
            foreach (int reason in reasons)
            {
                long fee = 0;
                if (rents != null) foreach (var r in rents) if (r.Reason == reason) fee -= r.MonthlyRent / 30;
                if (fee != 0 || reason == MoneyReason.GateRent) Book(fee, reason);
                Statistics.Add(StatId.ExpenseRents, fee);
            }
        }

        /// <summary>Mechanic fee per plane and day (monthly fee/30), also charged to a repaired plane's P&amp;L.</summary>
        public long MechanicCostPerPlanePerDay() => MechanicMonthlyFee[MechMode] / 30;

        /// <summary>Contract fees of the night: fee·planes/30 (economy.md §12.4).</summary>
        public long MechanicFeesPerDay(int planes) => MechanicMonthlyFee[MechMode] * planes / 30;

        /// <summary>
        /// Books the night's maintenance (economy.md §12.4): −(extra repair cost + fee·planes/30) as 3110
        /// and stat 19. Called by the operations domain after the repair loop with its extra cost; the
        /// finance fallback calls it with 0 if nobody did.
        /// </summary>
        public void BookMaintenance(long extraRepairCost, int planes)
        {
            long total = extraRepairCost + MechanicFeesPerDay(planes);
            Book(-total, MoneyReason.Maintenance);
            Statistics.Add(StatId.Maintenance, -total);
            MaintenanceBookedToday = true;
        }

        /// <summary>Wealth-tax brackets (economy.md §15.5).</summary>
        public static readonly long[] WealthTaxThresholds =
        {
            50000000000000, 100000000000000, 200000000000000, 400000000000000, 800000000000000,
            1200000000000000, 1600000000000000, 1700000000000000, 1800000000000000, 1900000000000000,
        };

        /// <summary>Tax = Σ (Money − T)/10 over the brackets exceeded.</summary>
        public long WealthTax()
        {
            long tax = 0;
            foreach (var t in WealthTaxThresholds) if (Money > t) tax += (Money - t) / 10;
            return tax;
        }
    }
}
