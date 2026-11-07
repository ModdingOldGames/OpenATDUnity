using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>Pending takeover choice (economy.md §6.8).</summary>
    public enum TakeoverMode { None = 0, Absorb = 1, Liquidate = 2 }

    /// <summary>A takeover is executed at the morning briefing (newspaper special, boss dialogue 5000).</summary>
    public sealed class TakeoverExecuted : ISimEvent
    {
        public readonly Player Acquirer, Target;
        public readonly TakeoverMode Mode;
        public TakeoverExecuted(Player a, Player t, TakeoverMode m) { Acquirer = a; Target = t; Mode = m; }
    }

    /// <summary>
    /// World-level finance state (economy.md): staff pool, newspapers, bank bin, pending takeover,
    /// statistics refresh, and the registration of the finance domain's rules, systems and setup steps.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>The staff pool (economy.md §9).</summary>
        public readonly WorkerPool Workers = new WorkerPool();
        /// <summary>The three newspapers (economy.md §16.5).</summary>
        public readonly Newspapers Newspapers = new Newspapers();
        /// <summary>Banknotes in the bank's waste bin (economy.md §2.3).</summary>
        public bool MoneyInBankBin;
        /// <summary>Pending takeover ("Overtake"), executed at the next morning review.</summary>
        public TakeoverMode PendingTakeover;
        public int TakeoverAcquirer = -1, TakeoverTarget = -1;
        /// <summary>The game has ended (mission over or local player out).</summary>
        public bool GameEnded;
        /// <summary>Stand-in for the original's C rand() of the flavour letters (seeded at setup).</summary>
        public readonly TeakRand FlavourRandom = new TeakRand();

        /// <summary>The finance domain's view of planes/rents/routes (default: none).</summary>
        public IOperationsBridge OperationsBridge => Rules.Has<IOperationsBridge>() ? Rules.Get<IOperationsBridge>() : NullBridge;
        static readonly IOperationsBridge NullBridge = new NullOperationsBridge();

        partial void InitFinance()
        {
            Rules.Set<IOperationsBridge>(NullBridge);
            Rules.Set<ICreditLimitRule>(new CreditLimitRule());
            Rules.Set<IInterestRule>(new InterestRule());
            Rules.Set<ISharePriceRule>(new SharePriceRule());
            Rules.Set<IKerosenePriceRule>(new KerosenePriceRule());
            Rules.Set<ICompanyValueRule>(new CompanyValueRule());
            Rules.Set<IMissionRule>(new MissionRuleOriginal());
            foreach (var s in FinanceSystems.Create()) Systems.Add(s);
            StaffModule.Register(this);
            SetupSteps.Add(new FinanceSetup());
            SetupSteps.Add(new StartingCrewSetup());
        }

        // ------------------------------------------------------------------ helpers for other domains

        /// <summary>Multiplies an airline's share price (sabotage ×0.95..×0.75, incidents ×0.7, ATFS 10 ×0.2; economy.md §6.4).</summary>
        public void SharePriceShock(Player p, double factor) => p.ShockSharePrice(factor);

        /// <summary>Shares of <paramref name="target"/> not held by any active airline (economy.md §6.1).</summary>
        public long FreeFloat(Player target)
        {
            long n = target.SharesIssued;
            foreach (var p in Players) if (!p.IsOut) n -= p.SharesOwned[target.Index];
            return n;
        }

        /// <summary>Refreshes the snapshot statistics of every airline (evening screen, briefing, statistics screen).</summary>
        public void UpdateStatistics()
        {
            foreach (var p in Players) UpdateStatistics(p);
        }

        /// <summary>
        /// Snapshot series of one airline (economy.md §16.2–16.3, original <c>UpdateStatistics</c>):
        /// money, debt, planes, price, company value, employees, staff satisfaction, mission progress,
        /// branches, routes, salaries, awareness, shares.
        /// </summary>
        public void UpdateStatistics(Player p)
        {
            var s = p.Statistics;
            var bridge = OperationsBridge;
            var planes = bridge.Planes(this, p);
            s.Set(StatId.Money, p.Money);
            s.Set(StatId.Debt, p.Money > 0 ? p.Credit : p.Credit - p.Money);
            s.Set(StatId.Planes, planes.Count);
            s.Set(StatId.SharePrice, (long)p.SharePrice);
            s.Set(StatId.CompanyValue, Rules.Get<ICompanyValueRule>().CompanyValue(this, p));
            OfferHighscore(p);

            if (p.IsHuman)
            {
                int n = 0, sat = 0;
                long salaries = 0;
                foreach (var wk in Workers.Workers)
                    if (wk.Employer == p.Index) { n++; sat += (wk.Happiness + 100) / 2; salaries += wk.Salary; }
                s.Set(StatId.Employees, n);
                s.Set(StatId.StaffSatisfaction, n > 0 ? sat / 100 : 0);
                s.Set(StatId.Salaries, -salaries);
            }
            else
            {
                long pil = 0, att = 0;
                foreach (var pl in planes) { pil += pl.RequiredPilots; att += pl.RequiredAttendants; }
                long emp = pil + att;
                s.Set(StatId.Employees, emp);
                s.Set(StatId.StaffSatisfaction, (int)emp * (System.Math.Min(500, p.Image) + 2500) / 3000);
                s.Set(StatId.Salaries, -VirtualSalaries(p, pil, att));
            }

            SetMissionProgress(p);
            s.Set(StatId.Branches, bridge.BranchCount(this, p));
            s.Set(StatId.Routes, bridge.RouteCount(this, p));
            s.Set(StatId.Awareness, p.Image / 10);
            s.Set(StatId.SharesIssued, p.SharesIssued);
            for (int c = 0; c < Player.Count; c++) s.Set(StatId.SharesHeldBase + c, p.SharesOwned[c]);
        }

        /// <summary>AI salary figure (economy.md §9.9): original salaries of a pool slice (skipping 2·index) ×1.25.</summary>
        long VirtualSalaries(Player p, long pilots, long attendants)
        {
            long e = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int type = pass == 0 ? WorkerType.Pilot : WorkerType.Attendant;
                long d = pass == 0 ? pilots : attendants;
                int ignore = p.Index * 2;
                foreach (var wk in Workers.Workers)
                    if (wk.Type == type)
                    {
                        ignore--;
                        if (ignore < 0)
                        {
                            d--;
                            if (d < 0) break;
                            e += wk.OriginalSalary;
                        }
                    }
            }
            return e + e / 4;
        }

        void SetMissionProgress(Player p)
        {
            var s = p.Statistics;
            var k = p.Counters;
            var rule = Rules.Get<IMissionRule>();
            switch (Difficulty)
            {
                case Mission.Tutorial: s.Set(StatId.MissionProgress, k.Orders * 100 / 5); break;
                case Mission.Passengers: s.Set(StatId.MissionProgress, k.Passengers * 100 / MissionTargets.Passengers); break;
                case Mission.Profit:
                    // QUIRK 9: passengers ÷ profit target.
                    s.Set(StatId.MissionProgress, Options.FixProfitMissionProgress ? p.Profit * 100 / MissionTargets.Profit : k.Passengers * 100 / MissionTargets.Profit);
                    break;
                case Mission.ConnectWorld: s.Set(StatId.MissionProgress, k.ConnectFlags * 100 / MissionTargets.Flags); break;
                case Mission.Image: s.Set(StatId.MissionProgress, p.Image * 100 / MissionTargets.Image); break;
                case Mission.Rocket: s.Set(StatId.MissionProgress, CountBits(k.RocketFlags) * 10); break;
                case 11: s.Set(StatId.MissionProgress, p.Credit); break;
                case 12: s.Set(StatId.MissionProgress, k.Freight); break;
                case 13: s.Set(StatId.MissionProgress, k.ReliefFreight); break;
                case 14: s.Set(StatId.MissionProgress, k.Miles); break;
                case 15: s.Set(StatId.MissionProgress, k.ServicePoints); break;
                case 16: case 17: case 18: s.Set(StatId.MissionProgress, rule.Rating(this, p)); break;
            }
        }

        static int CountBits(int f) { int n = 0; for (int c = 0; c < 32; c++) if ((f & (1 << c)) != 0) n++; return n; }

        // ------------------------------------------------------------------ elimination and takeover

        /// <summary>
        /// Removes an airline (economy.md §17.1): out, every holding of and by it zeroed. Publishes
        /// <see cref="PlayerOut"/>, and <see cref="GameOver"/> when it is the local player.
        /// </summary>
        public void RemovePlayer(Player p, OutReason reason)
        {
            p.IsOut = true;
            foreach (var o in Players)
            {
                o.SharesOwned[p.Index] = 0;
                p.SharesOwned[o.Index] = 0;
            }
            Events.Publish(new PlayerOut(p, reason));
            if (p.Index == LocalPlayer)
            {
                GameEnded = true;
                Events.Publish(new GameOver(p, reason));
            }
        }

        /// <summary>
        /// Executes the pending takeover (economy.md §6.8, boss dialogue 5010/5020/5030): absorb or
        /// liquidate, statistics refresh, target out.
        /// </summary>
        public void ExecuteTakeover()
        {
            if (PendingTakeover == TakeoverMode.None || TakeoverAcquirer < 0 || TakeoverTarget < 0) return;
            var acq = Players[TakeoverAcquirer];
            var tgt = Players[TakeoverTarget];
            var mode = PendingTakeover;
            var bridge = OperationsBridge;
            Events.Publish(new TakeoverExecuted(acq, tgt, mode));

            if (mode == TakeoverMode.Absorb)
            {
                foreach (var wk in Workers.Workers)
                    if (wk.Employer == tgt.Index) { wk.Employer = acq.Index; wk.PlaneId = -1; }

                long pilots = 0, attendants = 0;
                foreach (var pl in bridge.Planes(this, tgt)) { pilots += pl.RequiredPilots; attendants += pl.RequiredAttendants; }
                bridge.AbsorbOperations(this, acq, tgt);

                if (tgt.Owner != Owner.Human)
                    foreach (var wk in Workers.Workers)
                    {
                        if (wk.Employer == WorkerType.Reserve && wk.Type == WorkerType.Pilot && pilots > 0) { wk.Employer = acq.Index; wk.PlaneId = -1; pilots--; }
                        else if (wk.Employer == WorkerType.Reserve && wk.Type == WorkerType.Attendant && attendants > 0) { wk.Employer = acq.Index; wk.PlaneId = -1; attendants--; }
                    }
                acq.MapWorkers(false);

                acq.Book(tgt.Money, MoneyReason.TakeoverAbsorbed);
                acq.Credit += tgt.Credit;
                acq.SharesIssued += tgt.SharesIssued;
                for (int c = 0; c < Player.Count; c++)
                {
                    acq.SharesOwned[c] += tgt.SharesOwned[c];
                    acq.ShareCostBasis[c] += tgt.ShareCostBasis[c];
                }
                foreach (var o in Players)
                    if (!o.IsOut)
                    {
                        if (o != tgt && o != acq)
                        {
                            o.SharesOwned[acq.Index] += o.SharesOwned[tgt.Index];
                            o.ShareCostBasis[acq.Index] += o.ShareCostBasis[tgt.Index];
                        }
                        o.SharesOwned[tgt.Index] = 0;
                        o.ShareCostBasis[tgt.Index] = 0;
                    }
                foreach (var o in Players) o.SharesOwned[tgt.Index] = 0;
            }
            else
            {
                foreach (var wk in Workers.Workers) if (wk.Employer == tgt.Index) wk.Employer = WorkerType.Reserve;
                tgt.ChangeMoneySilently(bridge.LiquidateOperations(this, tgt));
                for (int c = 0; c < Player.Count; c++)
                    if (tgt.SharesOwned[c] != 0)
                    {
                        tgt.ChangeMoneySilently((long)(tgt.SharesOwned[c] * Players[c].SharePrice));
                        tgt.SharesOwned[c] = 0;
                    }
                long d = 0;
                foreach (var o in Players) if (!o.IsOut && o.SharesOwned[tgt.Index] != 0) d += o.SharesOwned[tgt.Index];
                if (d > 0)
                    foreach (var o in Players)
                        if (!o.IsOut && o.SharesOwned[tgt.Index] != 0)
                            o.Book((tgt.Money - tgt.Credit) * o.SharesOwned[tgt.Index] / d, MoneyReason.LiquidationPayout, tgt.Airline);
                foreach (var o in Players) o.SharesOwned[tgt.Index] = 0;
            }

            UpdateStatistics(acq);
            UpdateStatistics(tgt);
            PendingTakeover = TakeoverMode.None;
            TakeoverAcquirer = TakeoverTarget = -1;
            RemovePlayer(tgt, mode == TakeoverMode.Absorb ? OutReason.Absorbed : OutReason.Liquidated);
        }
    }
}
