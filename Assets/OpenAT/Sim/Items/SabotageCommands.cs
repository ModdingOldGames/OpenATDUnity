using OpenAT.Sim.Core;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Chooses a plane prank in the saboteur dialog (Sabo 1051..1055): validates it like the dialog and marks
    /// the victim's "security needed" bit before the plane is picked (§1.2). Refusal reasons are
    /// "sabo-&lt;text id&gt;" (see <see cref="SabotageEngine.CheckOrder"/>).
    /// </summary>
    public sealed class SelectPlanePrankCommand : ItemPlayerCommand
    {
        public readonly int Victim, Mode;
        public SelectPlanePrankCommand(int player, int victim, int mode) : base(player) { Victim = victim; Mode = mode; }
        protected override CommandResult Check(World w, Player p) =>
            OrderPrankCommand.Result(SabotageEngine.CheckOrder(w, p, Victim, PrankFamily.Plane, Mode, targetChosen: false));
        protected override void Run(World w, Player p)
        {
            SabotageEngine.MarkNeeded(w.Players[Victim], PrankFamily.Plane, Mode);
            p.Sabotage.PlaneSelecting = Mode;
        }
    }

    /// <summary>
    /// Orders and pays a prank at the saboteur (human; items-security-special.md §1.2–1.3). <see cref="Target"/>:
    /// victim plane id for plane pranks and special 5, route table index for special 6. Refusals are
    /// "sabo-1021" (no trust), "sabo-1045" (victim out), "sabo-1100" (pending), "sabo-2090".."sabo-2098"
    /// (protected), "sabo-6000" (money), "sabo-1300" (no laptop), "bad-victim", "not-offered", "bad-target".
    /// </summary>
    public sealed class OrderPrankCommand : ItemPlayerCommand
    {
        public readonly int Victim, Mode, Target;
        public readonly PrankFamily Family;

        public OrderPrankCommand(int player, int victim, PrankFamily family, int mode, int target = -1) : base(player)
        { Victim = victim; Family = family; Mode = mode; Target = target; }

        internal static CommandResult Result(int code)
        {
            if (code == 0) return CommandResult.Success;
            switch (code)
            {
                case SabotageEngine.BadVictim: return CommandResult.Fail("bad-victim");
                case SabotageEngine.NotOffered: return CommandResult.Fail("not-offered");
                case SabotageEngine.BadTarget: return CommandResult.Fail("bad-target");
            }
            return CommandResult.Fail("sabo-" + code);
        }

        protected override CommandResult Check(World w, Player p) => Result(SabotageEngine.CheckOrder(w, p, Victim, Family, Mode, Target));
        protected override void Run(World w, Player p) => SabotageEngine.Commit(w, p, Victim, Family, Mode, Target);
    }
}
