using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim
{
    /// <summary>One account statement line (economy.md §4).</summary>
    public struct StatementLine
    {
        public long Amount;
        public int Reason;
        public string Detail;
        public bool ByPhone;
        public bool Separator;
    }

    /// <summary>Money booked on a player's account.</summary>
    public sealed class MoneyChanged : ISimEvent
    {
        public readonly Player Player;
        public readonly long Amount;
        public readonly int Reason;
        public MoneyChanged(Player p, long amount, int reason) { Player = p; Amount = amount; Reason = reason; }
    }

    public sealed class ImageChanged : ISimEvent
    {
        public readonly Player Player;
        public readonly int Delta;
        public ImageChanged(Player p, int delta) { Player = p; Delta = delta; }
    }

    /// <summary>Core finance state and the central booking routine (economy.md §1, §3, §4).</summary>
    public sealed partial class Player
    {
        public const long DebtWarn1 = -500000, DebtWarn2 = -1000000, DebtWarn3 = -3000000;
        public const long DebtLimit = -1000000;
        public const long DebtGameOver = -5000000;
        public const int ImageMin = -1000, ImageMax = 1000;
        public const int StatementLines = 100;

        /// <summary>Owning world (set when the world is created).</summary>
        public World World { get; internal set; }

        public long Money;
        public long Credit;
        /// <summary>Hidden computer bonus still to be paid out (sim-core.md §3.3).</summary>
        public long Bonus;
        public readonly long[] MoneyPast = new long[20];
        /// <summary>Loan / deposit interest in % per year.</summary>
        public int LoanRate, SavingsRate;
        public BalanceSheet Today = new BalanceSheet();
        public BalanceSheet Yesterday = new BalanceSheet();
        public readonly List<StatementLine> Statement = new List<StatementLine>();
        /// <summary>Balance before the oldest visible statement line.</summary>
        public long HistoricMoney;
        /// <summary>Company image −1000..1000.</summary>
        public int Image;
        /// <summary>Profit counter of the "earn X" mission.</summary>
        public long Profit;

        /// <summary>
        /// The central booking routine (economy.md §3): debt warnings, overflow guard, balance,
        /// statement line, balance-sheet field by reason, event.
        /// </summary>
        public void Book(long amount, int reason, string detail = null)
        {
            long old = Money;
            if (amount > 0 && Money > long.MaxValue - amount) return;
            Money += amount;
            AddStatementLine(new StatementLine { Amount = amount, Reason = reason, Detail = detail });
            Today.Record(reason, amount);
            World?.Events.Publish(new MoneyChanged(this, amount, reason));
            if (World != null) CheckDebtWarnings(old, Money);
        }

        /// <summary>Changes money without statement line or balance sheet (the original's bypasses, §3).</summary>
        public void ChangeMoneySilently(long amount)
        {
            Money += amount;
            World?.Events.Publish(new MoneyChanged(this, amount, 0));
        }

        public void AddStatementLine(StatementLine line)
        {
            if (Statement.Count >= StatementLines)
            {
                HistoricMoney += Statement[0].Amount;
                Statement.RemoveAt(0);
            }
            Statement.Add(line);
        }

        /// <summary>Advisor warnings when crossing the debt thresholds downwards.</summary>
        public static readonly long[] DebtWarnings = { DebtWarn1, DebtWarn2, DebtWarn3 };

        void CheckDebtWarnings(long old, long now)
        {
            for (int i = 0; i < DebtWarnings.Length; i++)
                if (old > DebtWarnings[i] && now < DebtWarnings[i])
                    World.Events.Publish(new AdvisorMessage(this, AdvisorMessage.Secretary, 2380 + i));
        }

        /// <summary>Changes the company image, clamped to −1000..1000.</summary>
        public void ChangeImage(int delta)
        {
            int before = Image;
            Image = System.Math.Max(ImageMin, System.Math.Min(ImageMax, Image + delta));
            if (Image != before) World?.Events.Publish(new ImageChanged(this, Image - before));
        }

        /// <summary>True when the player may spend <paramref name="amount"/> without passing the overdraft limit.</summary>
        public bool CanAfford(long amount) => Money - amount >= DebtLimit;
    }

    /// <summary>A message for the player from an advisor (text id in std_ger.res group "Advi").</summary>
    public sealed class AdvisorMessage : ISimEvent
    {
        public const int Secretary = 0;
        public readonly Player Player;
        public readonly int Advisor;
        public readonly int TextId;
        public readonly object[] Args;
        /// <summary>Optional second "Advi" text appended after a space (0 = none), e.g. the workshop hint after a wear warning.</summary>
        public int AppendTextId;
        /// <summary>A "comment" (MESSAGE_COMMENT): replaces the advisor's previous comment, dropped when the mouse leaves.</summary>
        public bool Comment;
        public AdvisorMessage(Player p, int advisor, int textId, params object[] args) { Player = p; Advisor = advisor; TextId = textId; Args = args; }
    }
}
