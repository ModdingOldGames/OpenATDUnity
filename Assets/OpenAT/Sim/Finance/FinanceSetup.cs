using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Staff;

namespace OpenAT.Sim.Finance
{
    /// <summary>
    /// New-game finance state (economy.md §2, sim-core.md §3.3, §3.6): money, loan, hidden bonus,
    /// rates, image, shares, fuel, mechanic level, strike state, statistics; kerosene history,
    /// staff pool and today's applicants, bank-bin money, newspapers.
    /// Order 800: after the operations setup (starting planes are bought before the money is set).
    /// </summary>
    public sealed class FinanceSetup : ISetupStep
    {
        public const int StepOrder = 800;
        public string Name => "finance.setup";
        public int Order => StepOrder;

        public void Apply(World w, NewGameSettings s)
        {
            int d = w.Difficulty;
            var start = Mission.Start(d);
            bool atfs = Mission.IsAtfs(d);

            w.InitKerosene();
            w.Workers.Init(w.Data.Workers());
            w.Workers.NewDay(w);
            w.Workers.EnsureAdvisor(w, (int)AdvisorType.Personnel);
            w.Workers.EnsureAdvisor(w, (int)AdvisorType.Order);
            w.Workers.EnsureAdvisor(w, (int)AdvisorType.PlaneAnalyst);

            // Tunables (Sim/Modding); the defaults leave the mission values unchanged.
            var tun = w.Tunables ?? Modding.Tunables.CreateDefault();
            int moneyPct = tun.Int(Modding.TunableKeys.StartMoneyPercent);
            long Money(long m) => moneyPct == 100 ? m : m * moneyPct / 100;
            foreach (var p in w.Players)
            {
                p.Money = Money(start.HumanMoney);
                p.Credit = start.Credit;
                long bonus = p.IsComputer ? Money(start.ComputerMoney) - Money(start.HumanMoney) : 0;
                if (!atfs && bonus < 0) bonus = w.Options.FixComputerBonusAbs ? 0 : -bonus;
                p.Bonus = bonus;
                p.LoanRate = start.LoanRate + tun.Int(Modding.TunableKeys.LoanRateDelta);
                p.SavingsRate = start.SavingsRate + tun.Int(Modding.TunableKeys.SavingsRateDelta);
                System.Array.Clear(p.MoneyPast, 0, p.MoneyPast.Length);
                p.Statement.Clear();
                p.HistoricMoney = p.Money;
                p.Today.Clear();
                p.Yesterday.Clear();
                p.Profit = 0;
                p.Image = d == 16 ? 300 : 0;

                p.SharesIssued = 10000;
                p.MaxShares = 20000;
                p.Dividend = 10;
                p.TrustedDividend = 10;
                for (int c = 0; c < 10; c++) p.SharePrices[c] = tun.Int(Modding.TunableKeys.ShareStartPrice);
                for (int c = 0; c < Player.Count; c++) { p.SharesOwned[c] = 0; p.ShareCostBasis[c] = 0; }
                p.SharesOwned[p.Index] = p.SharesIssued * 8 / 10;
                p.ShareCostBasis[p.Index] = (long)(p.SharePrice * p.SharesOwned[p.Index]);

                p.KeroseneKind = KeroseneKind.Normal;
                p.Tank = tun.Int(Modding.TunableKeys.TankStartCapacity);
                p.TankContents = 0;
                p.TankPrice = 0;
                p.TankOpen = false;
                p.BadKerosene = 0;
                if (p.IsComputer && (d == 49 || d == 50)) { p.Tank = 5000; p.TankOpen = true; }

                p.MechMode = 2;
                p.MechTrust = 0;
                p.SecurityFlags = 0;
                p.ImageGotWorse = false;

                p.StrikeHours = 0;
                p.StrikeNotified = false;
                p.StrikeEndCountdown = 0;
                p.StrikeEndType = 0;
                p.StrikePlanned = false;
                p.DaysWithoutStrike = 20;

                p.Counters.Clear();
                p.Statistics.Clear();
            }

            w.MoneyInBankBin = new TeakRand((int)w.Clock.StartTime).Rand(100) > 75;
            w.PendingTakeover = TakeoverMode.None;
            w.TakeoverAcquirer = w.TakeoverTarget = -1;
            w.GameEnded = false;
            w.FlavourRandom.SetSeed(s.Seed);

            var n = w.Newspapers;
            n.Init();
            n.Reload(w, w.Data.Headlines());
            n.Interpolate(w);
            n.Comparison(w);
        }
    }

    /// <summary>
    /// Starting crew (economy.md §2.3, sim-core.md §3.6): humans get exactly the pilots and attendants
    /// their starting planes require, random reserve workers (seed StartTime + index); then every
    /// airline's crew is mapped. Order 850 (needs the starting planes).
    /// </summary>
    public sealed class StartingCrewSetup : ISetupStep
    {
        public const int StepOrder = 850;
        public string Name => "finance.starting-crew";
        public int Order => StepOrder;

        public void Apply(World w, NewGameSettings s)
        {
            var pool = w.Workers.Workers;
            foreach (var p in w.Players)
            {
                if (p.IsComputer || pool.Count == 0) continue;
                var r = new TeakRand((int)(w.Clock.StartTime + p.Index));
                long pilots = 0, attendants = 0;
                foreach (var pl in w.OperationsBridge.Planes(w, p)) { pilots += pl.RequiredPilots; attendants += pl.RequiredAttendants; }
                Hire(pool, r, p, WorkerType.Pilot, pilots);
                Hire(pool, r, p, WorkerType.Attendant, attendants);
            }
            // Map the workers and copy the crew onto the planes: the operations step (order 400) mapped
            // before anybody was hired, so without this the starting planes would show no crew.
            var crew = w.Rules.Get<Fleet.ICrewMapping>();
            foreach (var p in w.Players)
            {
                if (crew != null) crew.MapCrew(w, p);
                else p.MapWorkers(false);
            }
        }

        static void Hire(System.Collections.Generic.List<Worker> pool, TeakRand r, Player p, int type, long count)
        {
            for (long d = 0; d < count; d++)
            {
                Worker wk = null;
                for (int guard = 0; guard < 1000000; guard++)
                {
                    var cand = pool[r.Rand(pool.Count)];
                    if (cand.Employer == WorkerType.Reserve && cand.Type == type) { wk = cand; break; }
                }
                if (wk == null) return;
                wk.Employer = p.Index;
                wk.PlaneId = -1;
            }
        }
    }
}
