using OpenAT.Sim.Core;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// The drinker at Rick's talks the staff out of the strike (rooms-and-ui-3b §13b, click on Rick 6000):
    /// once he has started (<see cref="DrinkerEndsStrikeCommand"/>, countdown 10, end type 3) the remaining
    /// countdown becomes 4 game minutes.
    /// </summary>
    public sealed class DrinkerHurriesStrikeEndCommand : ItemPlayerCommand
    {
        public DrinkerHurriesStrikeEndCommand(int player) : base(player) { }

        protected override CommandResult Check(World w, Player p)
        {
            if (!p.TrinkerTrust) return CommandResult.Fail("no-trust");
            return p.StrikeRunning && p.StrikeEndType == 3 && p.StrikeEndCountdown > 0 ? CommandResult.Success : CommandResult.Fail("no-strike");
        }

        protected override void Run(World w, Player p) { p.StrikeEndCountdown = 4; p.StrikeEndType = 3; }
    }
}
