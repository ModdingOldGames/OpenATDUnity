using OpenAT.Sim.Finance;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>Bank: credit limit, loans, overnight interest, finance-advisor perk (economy.md §5).</summary>
    public sealed partial class Player
    {
        /// <summary>Credit limit for a new loan (economy.md §5.1), via <see cref="ICreditLimitRule"/>.</summary>
        public long CreditLimit() => World != null ? World.Rules.Get<ICreditLimitRule>().CreditLimit(World, this) : CreditLimitRule.Original(this, false);

        /// <summary>Takes a loan (economy.md §5.1): amounts below 1000 do nothing. Caller validated the limit.</summary>
        public void TakeLoan(long amount)
        {
            if (amount < 1000) return;
            Credit += amount;
            Book(amount, MoneyReason.LoanTaken);
        }

        /// <summary>Repays (part of) the loan; cash is not checked (economy.md §5.1, quirk 4).</summary>
        public void RepayLoan(long amount)
        {
            if (amount <= 0) return;
            if (amount > Credit) amount = Credit;
            Credit -= amount;
            Book(-amount, MoneyReason.LoanRepaid);
        }

        /// <summary>Largest repayable amount: Credit (original) or limited by cash down to the overdraft limit (fix).</summary>
        public long MaxRepayment()
        {
            if (World != null && World.Options.FixLoanRepayCash)
            {
                long cash = Money - DebtLimit;
                return System.Math.Max(0, System.Math.Min(Credit, cash));
            }
            return Credit;
        }

        /// <summary>Interest shown in the bank form: monthly SollZins·Credit/1200 and yearly SollZins·Credit/100.</summary>
        public long LoanInterestMonthly => LoanRate * Credit / 1200;
        public long LoanInterestYearly => LoanRate * Credit / 100;

        /// <summary>
        /// Finance-advisor perk on entering the bank (economy.md §5.1): talent &gt; 75 and the loan rate
        /// still at the base rate of Normal (6), Hard (10) or Final (15) → (loan + deposit)/2.
        /// Returns true when the rate changed (advisor message <c>Advi 5000</c>).
        /// </summary>
        public bool ApplyFinanceAdvisorPerk()
        {
            if (World == null || AdvisorQuality(AdvisorType.Finance) <= 75) return false;
            int d = World.Difficulty;
            bool eligible = (d == Mission.ConnectWorld && LoanRate == 6) || (d == Mission.Image && LoanRate == 10) || (d == Mission.Rocket && LoanRate == 15);
            if (!eligible && World.Options.FixFinanceAdvisorPerk)
                eligible = LoanRate == Mission.Start(d).LoanRate && LoanRate > (LoanRate + SavingsRate) / 2;
            if (!eligible) return false;
            LoanRate = (LoanRate + SavingsRate) / 2;
            World.Events.Publish(new AdvisorMessage(this, (int)AdvisorType.Finance, 5000));
            return true;
        }
    }
}
