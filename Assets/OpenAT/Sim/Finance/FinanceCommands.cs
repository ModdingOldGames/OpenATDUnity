using System.Linq;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Finance
{
    /// <summary>Base for an airline's command: player index, out/phone checks.</summary>
    public abstract class PlayerFinanceCommand : ICommand<World>
    {
        public readonly int PlayerIndex;
        /// <summary>Issued by phone (bank/personnel actions are refused by phone).</summary>
        public readonly bool ByPhone;
        protected PlayerFinanceCommand(int player, bool byPhone = false) { PlayerIndex = player; ByPhone = byPhone; }

        public CommandResult Validate(World w)
        {
            if (PlayerIndex < 0 || PlayerIndex >= Player.Count) return CommandResult.Fail("bad-player");
            var p = w.Players[PlayerIndex];
            if (p.IsOut) return CommandResult.Fail("player-out");
            return Check(w, p);
        }

        public void Apply(World w) => Run(w, w.Players[PlayerIndex]);

        protected abstract CommandResult Check(World w, Player p);
        protected abstract void Run(World w, Player p);
        protected CommandResult NotByPhone() => ByPhone ? CommandResult.Fail("by-phone") : CommandResult.Success;
    }

    // ====================================================================== bank (economy.md §5)

    /// <summary>Take a loan (economy.md §5.1): limit &gt; 0, amount ≤ limit, ≥ 1000; not by phone (Bank 120/150).</summary>
    public sealed class TakeLoanCommand : PlayerFinanceCommand
    {
        public readonly long Amount;
        public TakeLoanCommand(int player, long amount, bool byPhone = false) : base(player, byPhone) { Amount = amount; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            long limit = p.CreditLimit();
            if (limit <= 0) return CommandResult.Fail("no-credit");
            if (Amount > limit) return CommandResult.Fail("over-limit");
            if (Amount < 1000) return CommandResult.Fail("too-small");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.TakeLoan(Amount);
    }

    /// <summary>Repay a loan (economy.md §5.1): needs a loan, amount ≤ Credit; cash not checked (QUIRK 4).</summary>
    public sealed class RepayLoanCommand : PlayerFinanceCommand
    {
        public readonly long Amount;
        public RepayLoanCommand(int player, long amount, bool byPhone = false) : base(player, byPhone) { Amount = amount; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (p.Credit <= 0) return CommandResult.Fail("no-loan");
            if (Amount <= 0 || Amount > p.MaxRepayment()) return CommandResult.Fail("bad-amount");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.RepayLoan(Amount);
    }

    /// <summary>Set the dividend 0..25 per share per year (economy.md §6.7); not by phone (Bank 1200).</summary>
    public sealed class SetDividendCommand : PlayerFinanceCommand
    {
        public readonly int Dividend;
        public SetDividendCommand(int player, int dividend, bool byPhone = false) : base(player, byPhone) { Dividend = dividend; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            return Dividend < 0 || Dividend > Player.MaxDividend ? CommandResult.Fail("bad-dividend") : CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.Dividend = Dividend;
    }

    /// <summary>Buy shares (economy.md §6.5): target active, 1..free float, DEBT_LIMIT on count·price (Bank 6000).</summary>
    public sealed class BuySharesCommand : PlayerFinanceCommand
    {
        public readonly int Target;
        public readonly long Count;
        public BuySharesCommand(int player, int target, long count, bool byPhone = false) : base(player, byPhone) { Target = target; Count = count; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (Target < 0 || Target >= Player.Count || w.Players[Target].IsOut) return CommandResult.Fail("target-out");
            var t = w.Players[Target];
            if (Count <= 0 || Count > w.FreeFloat(t)) return CommandResult.Fail("bad-amount");
            long price = (long)(Count * t.SharePrice);
            if (price != 0 && p.Money - price < Player.DebtLimit) return CommandResult.Fail("debt-limit");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.BuyShares(w.Players[Target], Count);
    }

    /// <summary>Sell shares (economy.md §6.5): target active, 1..holdings.</summary>
    public sealed class SellSharesCommand : PlayerFinanceCommand
    {
        public readonly int Target;
        public readonly long Count;
        public SellSharesCommand(int player, int target, long count, bool byPhone = false) : base(player, byPhone) { Target = target; Count = count; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (Target < 0 || Target >= Player.Count || w.Players[Target].IsOut) return CommandResult.Fail("target-out");
            if (Count <= 0 || Count > p.SharesOwned[Target]) return CommandResult.Fail("bad-amount");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.SellShares(w.Players[Target], Count);
    }

    /// <summary>
    /// Issue new shares (economy.md §6.6): percent 10/25/50/75/100 of (Max − Anz), price ⌊K⌋ − 5/3/1.
    /// Refused below 10 000 issuable (Bank 3000) or a price under 10 (3001).
    /// </summary>
    public sealed class IssueSharesCommand : PlayerFinanceCommand
    {
        public readonly int Percent, Discount;
        public IssueSharesCommand(int player, int percent, int discount, bool byPhone = false) : base(player, byPhone) { Percent = percent; Discount = discount; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (p.IssuableShares < 10000) return CommandResult.Fail("too-few-shares");
            if (p.SharePrice < 10) return CommandResult.Fail("price-too-low");
            if (Percent != 10 && Percent != 25 && Percent != 50 && Percent != 75 && Percent != 100) return CommandResult.Fail("bad-percent");
            if (Discount != 5 && Discount != 3 && Discount != 1) return CommandResult.Fail("bad-discount");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.IssueShares(p.SharesForPercent(Percent), Discount);
    }

    /// <summary>
    /// Request a takeover (economy.md §6.8, Bank 2510..2531): humans only; target not self (2520), not out
    /// (2521), holdings &gt; 0 (2525), ≥ 50 % (2522), the target holds &lt; 30 % of ours (2523). Executes at
    /// the next morning review.
    /// </summary>
    public sealed class RequestTakeoverCommand : PlayerFinanceCommand
    {
        public readonly int Target;
        public readonly TakeoverMode Mode;
        public RequestTakeoverCommand(int player, int target, TakeoverMode mode, bool byPhone = false) : base(player, byPhone) { Target = target; Mode = mode; }
        protected override CommandResult Check(World w, Player p)
        {
            if (ByPhone) return CommandResult.Fail("by-phone");
            if (p.IsComputer) return CommandResult.Fail("humans-only");
            if (Mode == TakeoverMode.None) return CommandResult.Fail("bad-mode");
            if (Target == p.Index) return CommandResult.Fail("self");
            if (Target < 0 || Target >= Player.Count || w.Players[Target].IsOut) return CommandResult.Fail("target-out");
            var t = w.Players[Target];
            if (p.SharesOwned[Target] == 0) return CommandResult.Fail("no-shares");
            if (p.SharesOwned[Target] < t.SharesIssued / 2) return CommandResult.Fail("below-half");
            if (t.SharesOwned[p.Index] >= p.SharesIssued * 3 / 10) return CommandResult.Fail("target-holds-30");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p)
        {
            w.PendingTakeover = Mode;
            w.TakeoverAcquirer = p.Index;
            w.TakeoverTarget = Target;
        }
    }

    /// <summary>Pick up the banknotes in the bank's waste bin: +100 000 (2006), bin emptied for everyone (economy.md §2.3).</summary>
    public sealed class CollectBankBinMoneyCommand : PlayerFinanceCommand
    {
        public const long Amount = 100000;
        public CollectBankBinMoneyCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => w.MoneyInBankBin ? CommandResult.Success : CommandResult.Fail("no-money");
        protected override void Run(World w, Player p)
        {
            w.MoneyInBankBin = false;
            p.Book(Amount, MoneyReason.BankBinMoney);
        }
    }

    /// <summary>Entering the bank: the finance-advisor interest perk (economy.md §5.1).</summary>
    public sealed class EnterBankCommand : PlayerFinanceCommand
    {
        public EnterBankCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p) => p.ApplyFinanceAdvisorPerk();
    }

    // ====================================================================== kerosene (economy.md §12.2)

    /// <summary>
    /// Buy kerosene of a kind into the tank (sets the kind permanently, even for 0 barrels): ≤ free tank
    /// space and affordable down to −1 000 000 (Arab 6000).
    /// </summary>
    public sealed class BuyKeroseneCommand : PlayerFinanceCommand
    {
        public readonly KeroseneKind Kind;
        public readonly int Barrels;
        public BuyKeroseneCommand(int player, KeroseneKind kind, int barrels) : base(player) { Kind = kind; Barrels = barrels; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Barrels < 0 || Barrels > p.Tank - p.TankContents) return CommandResult.Fail("bad-amount");
            long cost = Player.TankPurchaseCost(Player.PricePerBarrel(w.Kerosene, Kind), Barrels);
            if (cost != 0 && p.Money - cost < Player.DebtLimit) return CommandResult.Fail("debt-limit");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.BuyKerosene(Kind, Barrels);
    }

    /// <summary>Pick a fuel grade in the Petrol Air dialog (Arab 400..402): the kind switches at once, before any amount is chosen.</summary>
    public sealed class ChooseKeroseneCommand : PlayerFinanceCommand
    {
        public readonly KeroseneKind Kind;
        public ChooseKeroseneCommand(int player, KeroseneKind kind) : base(player) { Kind = kind; }
        protected override CommandResult Check(World w, Player p) =>
            Kind >= KeroseneKind.Premium && Kind <= KeroseneKind.Cheap ? CommandResult.Success : CommandResult.Fail("bad-kind");
        protected override void Run(World w, Player p) => p.ChooseKerosene(Kind);
    }

    /// <summary>Buy storage tanks: model 0..3 × multiplier 1/2/3/5/10 (economy.md §12.2).</summary>
    public sealed class BuyTankCommand : PlayerFinanceCommand
    {
        public readonly int Model, Multiplier;
        public BuyTankCommand(int player, int model, int multiplier) : base(player) { Model = model; Multiplier = multiplier; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Model < 0 || Model > 3) return CommandResult.Fail("bad-model");
            if (Multiplier != 1 && Multiplier != 2 && Multiplier != 3 && Multiplier != 5 && Multiplier != 10) return CommandResult.Fail("bad-multiplier");
            if (p.Money - (long)Player.TankPricePerUnit[Model] * Multiplier < Player.DebtLimit) return CommandResult.Fail("debt-limit");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.BuyTank(Model, Multiplier);
    }

    /// <summary>Toggle "planes refuel from the tank" (office kerosene menu).</summary>
    public sealed class SetTankOpenCommand : PlayerFinanceCommand
    {
        public readonly bool Open;
        public SetTankOpenCommand(int player, bool open) : base(player) { Open = open; }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p) => p.TankOpen = Open;
    }

    // ====================================================================== advertising (economy.md §10)

    /// <summary>Run an ad campaign (type, tier 0..5, route for route ads); DEBT_LIMIT check (Werb 6000).</summary>
    public sealed class AdCampaignCommand : PlayerFinanceCommand
    {
        public readonly AdType Type;
        public readonly int Tier, RouteId;
        public AdCampaignCommand(int player, AdType type, int tier, int routeId = 0) : base(player) { Type = type; Tier = tier; RouteId = routeId; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Tier < 0 || Tier > 5) return CommandResult.Fail("bad-tier");
            if (Type == AdType.Route && !w.OperationsBridge.RentedRouteIds(w, p).Contains(RouteId)) return CommandResult.Fail("route-not-rented");
            if (p.Money - Advertising.PriceOf(Type, Tier) < Player.DebtLimit) return CommandResult.Fail("debt-limit");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => Advertising.Run(w, p, Type, Tier, RouteId);
    }

    // ====================================================================== upkeep contracts

    /// <summary>
    /// Choose the maintenance contract level 0..3 (economy.md §12.4). The cleaner (0) is only offered
    /// once the mechanic trusts the player (MechTrust 2), as the dialogue's choice range is coded.
    /// </summary>
    public sealed class SetMechModeCommand : PlayerFinanceCommand
    {
        public readonly int Level;
        public SetMechModeCommand(int player, int level) : base(player) { Level = level; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Level < 0 || Level > 3) return CommandResult.Fail("bad-level");
            if (Level == 0 && p.MechTrust != 2) return CommandResult.Fail("not-offered");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => p.MechMode = Level;
    }

    /// <summary>Set the security subscriptions (bitmask, economy.md §14). Effects belong to the security domain.</summary>
    public sealed class SetSecurityFlagsCommand : PlayerFinanceCommand
    {
        public readonly int Flags;
        public SetSecurityFlagsCommand(int player, int flags) : base(player) { Flags = flags; }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p) => p.SecurityFlags = Flags;
    }
}
