using System;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Nightly plane upkeep (sim-core.md §2.7 step 21, planes-and-flights.md §7.2–7.3): for every plane
    /// (album last → first) the weekly plan rollover, then mechanic work and repair pricing; then the
    /// "tycoon" mission's plane events and one booking of the total (reason 3110).
    /// </summary>
    public static class Maintenance
    {
        /// <summary>Monthly mechanic fee per plane by mode ("gRepairPrice").</summary>
        public static readonly int[] RepairPrice = { 1200, 18000, 48000, 150000 };

        public static void NightlyUpkeep(World w, Player p)
        {
            int mode = Math.Max(0, Math.Min(3, w.Rules.Get<IMechanicLevel>().Level(w, p)));
            int daily = RepairPrice[mode] / 30;
            var rule = w.Rules.Get<IMaintenanceRule>();
            var rand = new TeakRand(w.Clock.Date);
            long sum = 0;

            for (int c = p.Planes.Capacity - 1; c >= 0; c--)
            {
                var pl = p.Planes[c];
                if (pl == null) continue;
                FlightPlanner.Rollover(w, p, pl);
                if (pl.Condition >= pl.TargetCondition + 2) continue;

                pl.Balances[0] -= daily;
                for (int d = 6; d >= 1; d--) pl.Balances[d] = pl.Balances[d - 1];
                pl.Balances[0] = 0;

                int oldZ = pl.Condition;
                if (pl.Condition < pl.WorstCondition) pl.WorstCondition = pl.Condition;
                pl.Condition = rule.DailyCondition(w, p, pl, c, mode, rand);
                if (pl.Condition > pl.TargetCondition) pl.Condition = pl.TargetCondition;

                long improvement = 0;
                if (pl.Condition > pl.WorstCondition + 20)
                {
                    improvement = pl.Condition - (pl.WorstCondition + 20);
                    pl.WorstCondition = pl.Condition < 20 ? 0 : pl.Condition - 20;
                }
                pl.MaintenanceCosts += daily;
                if (pl.Condition > oldZ)
                {
                    // The original multiplies in 32 bits: for very expensive planes it wraps negative and the
                    // night's repair pays the player. FixRepairCostOverflow (default) multiplies in 64 bits.
                    long part1 = w.Options.FixRepairCostOverflow ? improvement * pl.Price / 110 : unchecked((int)improvement * pl.Price) / 110;
                    int part2 = (int)((pl.Condition - oldZ) * 10 * pl.MaintenanceFactor * (2100 - pl.BuildYear) / 100 * (200 - pl.Condition) / 100);
                    sum += part1 + part2;
                    pl.MaintenanceCosts += part1 + part2;
                    pl.WorstCondition = Math.Max(pl.WorstCondition, pl.Condition - 20);
                }
            }

            if (w.Difficulty == Mission.AtfsLast) Atfs10PlaneEvents(w, p);

            long total = sum + (long)RepairPrice[mode] * p.Planes.Count / 30;
            w.Rules.Get<IMaintenanceBooking>().Book(w, p, sum, p.Planes.Count, mode);
            w.Events.Publish(new MaintenanceBooked { Player = p, Amount = total });
        }

        /// <summary>
        /// Plane events of the "tycoon" mission (sim-core.md §3.6): day 35 every plane −40 condition and
        /// target ≤ condition + 10; day 40 one random plane removed. (Day 18 share price and day 55 image
        /// belong to the missions/finance domain.)
        /// </summary>
        static void Atfs10PlaneEvents(World w, Player p)
        {
            int date = w.Clock.Date;
            if (date == 40 && p.Planes.Count > 0)
            {
                // "GetRandomUsedIndex" without a generator uses C rand(); here a seed of Date + airline.
                var r = new TeakRand(date + p.Index);
                int k = r.Rand(p.Planes.Count) + 1;
                for (int c = p.Planes.Capacity - 1; c >= 0; c--)
                    if (p.Planes[c] != null && --k == 0) { p.Planes.Remove(p.Planes[c].Id); break; }
                ContractUsage.UpdateContracts(w, p);
                w.Rules.Get<ICrewMapping>().MapCrew(w, p);
            }
            if (date == 35)
                foreach (var pl in p.Planes.Items)
                {
                    pl.Condition = Math.Max(0, pl.Condition - 40);
                    pl.TargetCondition = Math.Min(pl.TargetCondition, pl.Condition + 10);
                }
        }
    }
}
