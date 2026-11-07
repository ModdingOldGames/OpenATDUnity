using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Staff
{
    /// <summary>Hire an applicant (economy.md §9.3): employer = player, no plane, auto-assignment. No fee, no limit.</summary>
    public sealed class HireWorkerCommand : PlayerFinanceCommand
    {
        public readonly int Worker;
        public HireWorkerCommand(int player, int worker, bool byPhone = false) : base(player, byPhone) { Worker = worker; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (Worker < 0 || Worker >= w.Workers.Count) return CommandResult.Fail("bad-worker");
            return w.Workers[Worker].Employer == WorkerType.Jobless ? CommandResult.Success : CommandResult.Fail("not-applicant");
        }
        protected override void Run(World w, Player p)
        {
            var wk = w.Workers[Worker];
            wk.Employer = p.Index;
            wk.PlaneId = -1;
            p.MapWorkers(true);
        }
    }

    /// <summary>Fire an employee (economy.md §9.3): back to the applicants (re-hirable today), no severance.</summary>
    public sealed class FireWorkerCommand : PlayerFinanceCommand
    {
        public readonly int Worker;
        public FireWorkerCommand(int player, int worker, bool byPhone = false) : base(player, byPhone) { Worker = worker; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (Worker < 0 || Worker >= w.Workers.Count) return CommandResult.Fail("bad-worker");
            return w.Workers[Worker].Employer == p.Index ? CommandResult.Success : CommandResult.Fail("not-employee");
        }
        protected override void Run(World w, Player p)
        {
            var wk = w.Workers[Worker];
            wk.Employer = WorkerType.Jobless;
            wk.PlaneId = -1;
            p.MapWorkers(true);
        }
    }

    /// <summary>Raise (+10 %) or cut (−10 %) one employee's salary (economy.md §9.6).</summary>
    public sealed class ChangeSalaryCommand : PlayerFinanceCommand
    {
        public readonly int Worker;
        public readonly bool Raise;
        public ChangeSalaryCommand(int player, int worker, bool raise) : base(player) { Worker = worker; Raise = raise; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Worker < 0 || Worker >= w.Workers.Count) return CommandResult.Fail("bad-worker");
            return w.Workers[Worker].Employer == p.Index ? CommandResult.Success : CommandResult.Fail("not-employee");
        }
        protected override void Run(World w, Player p) => p.ChangeSalary(w.Workers[Worker], Raise);
    }

    /// <summary>
    /// Raise all (Jobs 600: also cancels a planned strike and pumps morale) or cut all (810)
    /// (economy.md §9.6).
    /// </summary>
    public sealed class ChangeAllSalariesCommand : PlayerFinanceCommand
    {
        public readonly bool Raise;
        public ChangeAllSalariesCommand(int player, bool raise) : base(player) { Raise = raise; }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p)
        {
            p.ChangeAllSalaries(Raise);
            if (Raise)
            {
                p.StrikePlanned = false;
                p.PumpMorale();
            }
        }
    }

    /// <summary>Move an employee (pilot/attendant) to a plane; surplus crew is freed (economy.md §9.3).</summary>
    public sealed class AssignWorkerCommand : PlayerFinanceCommand
    {
        public readonly int Worker, PlaneId;
        public AssignWorkerCommand(int player, int worker, int planeId) : base(player) { Worker = worker; PlaneId = planeId; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Worker < 0 || Worker >= w.Workers.Count) return CommandResult.Fail("bad-worker");
            var wk = w.Workers[Worker];
            if (wk.Employer != p.Index) return CommandResult.Fail("not-employee");
            if (!wk.IsPilot && !wk.IsAttendant) return CommandResult.Fail("not-crew");
            if (PlaneId != -1)
            {
                bool found = false;
                foreach (var pl in w.OperationsBridge.Planes(w, p)) if (pl.Id == PlaneId) found = true;
                if (!found) return CommandResult.Fail("bad-plane");
            }
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p)
        {
            w.Workers[Worker].PlaneId = PlaneId;
            p.MapWorkerOverflow(true);
        }
    }

    /// <summary>Ways to end a strike (economy.md §9.7, Jobs 960/961, drunk 6000).</summary>
    public enum StrikeEndOption { RaiseSalaries = 2, Threaten = 1, BribeDrunk = 3 }

    /// <summary>
    /// End a running strike: raise all (end in 2 minutes, morale pump), threaten (−20 happiness,
    /// 4 minutes) or let the drinker at Rick's talk to the staff (needs the horseshoe trust; 10 minutes,
    /// cut to 4 by his follow-up line, see <see cref="Items.DrinkerHurriesStrikeEndCommand"/>; no mood change).
    /// </summary>
    public sealed class EndStrikeCommand : PlayerFinanceCommand
    {
        public readonly StrikeEndOption Option;
        public EndStrikeCommand(int player, StrikeEndOption option) : base(player) { Option = option; }

        /// <summary>
        /// The drinker takes the job (Rick 6000): drinker trust, a strike running and he is not already on it
        /// (countdown running with end type 3 → Rick 6001). As in the original he takes over a running
        /// negotiated countdown.
        /// </summary>
        public static bool DrinkerCanEnd(Player p) =>
            p.TrinkerTrust && p.StrikeHours > 0 && !(p.StrikeEndCountdown > 0 && p.StrikeEndType == 3);

        /// <summary>The drinker starts talking the staff out of the strike: countdown 10 game minutes, end type 3.</summary>
        public static void DrinkerStarts(Player p)
        {
            p.StrikeEndCountdown = 10;
            p.StrikeEndType = 3;
        }

        protected override CommandResult Check(World w, Player p)
        {
            if (Option == StrikeEndOption.BribeDrunk)
                return !p.TrinkerTrust ? CommandResult.Fail("no-trust") : DrinkerCanEnd(p) ? CommandResult.Success : CommandResult.Fail("no-strike");
            return p.StrikeHours > 0 && p.StrikeEndCountdown == 0 ? CommandResult.Success : CommandResult.Fail("no-strike");
        }
        protected override void Run(World w, Player p)
        {
            switch (Option)
            {
                case StrikeEndOption.RaiseSalaries:
                    p.StrikeNotified = false;
                    p.StrikeEndType = 2;
                    p.StrikeEndCountdown = 2;
                    p.ChangeAllSalaries(true);
                    p.PumpMorale();
                    break;
                case StrikeEndOption.Threaten:
                    p.StrikeNotified = false;
                    p.StrikeEndType = 1;
                    p.StrikeEndCountdown = 4;
                    w.Workers.AddHappiness(p.Index, -20);
                    break;
                case StrikeEndOption.BribeDrunk:
                    DrinkerStarts(p);
                    break;
            }
        }
    }
}
