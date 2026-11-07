using OpenAT.Sim.Core;

namespace OpenAT.Sim.Rivals
{
    /// <summary>
    /// The effect of a line in the talk between a human and a computer airline (rooms-and-ui-3b §19): the
    /// computer's sympathy towards the human changes (clamped to ±1000) and/or the cooperation of both
    /// airlines is switched on or off. The dialog itself (lines, turns) belongs to the view.
    /// </summary>
    public sealed class RivalTalkCommand : ICommand<World>
    {
        public const int SympathyLimit = 1000;
        public readonly int Partner, Human, SympathyDelta;
        /// <summary>null = unchanged; true/false = both airlines (do not) cooperate.</summary>
        public readonly bool? Cooperation;

        public RivalTalkCommand(int partner, int human, int sympathyDelta, bool? cooperation = null)
        {
            Partner = partner; Human = human; SympathyDelta = sympathyDelta; Cooperation = cooperation;
        }

        public CommandResult Validate(World w)
        {
            if (Partner < 0 || Partner >= Player.Count || Human < 0 || Human >= Player.Count || Partner == Human) return CommandResult.Fail("bad-player");
            if (w.Players[Partner].IsOut || w.Players[Human].IsOut) return CommandResult.Fail("player-out");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var p = w.Players[Partner];
            if (SympathyDelta != 0)
            {
                long s = (long)p.Sympathy[Human] + SympathyDelta;
                p.Sympathy[Human] = (int)(s > SympathyLimit ? SympathyLimit : s < -SympathyLimit ? -SympathyLimit : s);
            }
            if (Cooperation.HasValue)
            {
                p.Cooperates[Human] = Cooperation.Value;
                w.Players[Human].Cooperates[Partner] = Cooperation.Value;
            }
        }
    }
}

namespace OpenAT.Sim.Rivals
{
    /// <summary>
    /// The computer partner lost interest in a talk (its "bored" line, <c>Play</c> 2499): it does not answer the
    /// human's calls any more today (rooms-and-ui-3 §6.3, §8).
    /// </summary>
    public sealed class RivalBoredCommand : ICommand<World>
    {
        public readonly int Partner;
        public RivalBoredCommand(int partner) => Partner = partner;

        public CommandResult Validate(World w) =>
            Partner < 0 || Partner >= Player.Count ? CommandResult.Fail("bad-player") : CommandResult.Success;

        public void Apply(World w) => w.Players[Partner].BoredOfPlayer = true;
    }

    /// <summary>Per-airline new day (sim-core.md §2.7 step 3, daily state): the airline is no longer bored of anyone.</summary>
    public sealed class RivalsNewDaySystem : SimSystem
    {
        public override string Name => "rivals.new-day";
        public override int Order => 300;
        public override void OnPlayerNewDay(World w, Player p) => p.BoredOfPlayer = false;
    }
}
