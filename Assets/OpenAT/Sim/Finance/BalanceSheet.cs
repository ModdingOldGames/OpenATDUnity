namespace OpenAT.Sim.Finance
{
    /// <summary>
    /// The daily income/expense sheet ("Bilanz", economy.md §3). Fields hold absolute amounts.
    /// Copied to "yesterday" at the end of each night.
    /// </summary>
    public sealed class BalanceSheet
    {
        public long Tickets, Orders, Kerosene, LoanInterest, DepositInterest, GateRent, BranchRent, RouteRent;
        public long Penalties, Staff, Maintenance, DividendsPaid, DividendsReceived;

        public void Clear()
        {
            Tickets = Orders = Kerosene = LoanInterest = DepositInterest = GateRent = BranchRent = RouteRent = 0;
            Penalties = Staff = Maintenance = DividendsPaid = DividendsReceived = 0;
        }

        public BalanceSheet Clone() => (BalanceSheet)MemberwiseClone();

        /// <summary>Sum of the income side.</summary>
        public long Income => DepositInterest + Tickets + Orders + DividendsReceived;

        /// <summary>Sum of the expense side (positive).</summary>
        public long Expense => LoanInterest + Kerosene + Staff + Penalties + Maintenance + GateRent + BranchRent + RouteRent + DividendsPaid;

        /// <summary>Income minus expenses.</summary>
        public long Total => Income - Expense;

        /// <summary>Adds |amount| (clamped to int range) to the field the reason maps to.</summary>
        public void Record(int reason, long amount)
        {
            long a = amount < 0 ? -amount : amount;
            if (a > int.MaxValue) a = int.MaxValue;
            switch (reason)
            {
                case MoneyReason.LoanInterest:
                case MoneyReason.OverdraftInterest: LoanInterest += a; break;
                case MoneyReason.DepositInterest: DepositInterest += a; break;
                case 2021: Kerosene += a; break;
                case 2030: Tickets += a; break;
                case MoneyReason.GateRent: GateRent += a; break;
                case MoneyReason.BranchRent: BranchRent += a; break;
                case MoneyReason.RouteRent: RouteRent += a; break;
                case MoneyReason.ContractPenalty: Penalties += a; break;
                case 2061: Orders += a; break;
                case MoneyReason.Salaries: Staff += a; break;
                case MoneyReason.Maintenance: Maintenance += a; break;
                case MoneyReason.DividendsPaid: DividendsPaid += a; break;
                case MoneyReason.DividendsReceived: DividendsReceived += a; break;
            }
        }
    }
}
