using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Records a phone call between two airlines in the hall state (<see cref="HallPlayer.PhonePartner"/>), so the view
    /// can draw the phone pose (airport-and-people.md §6.2: standing frames 4..7). The original shows the pose only for
    /// the local player's own figure while it stands in the hall with a phone dialog open. With the addition
    /// <see cref="GameOptions.ComputerPhonePose"/> a computer airline called by the human also stops where it is and
    /// shows the pose until the call ends (<see cref="HallTick"/> holds it like a face-to-face partner).
    /// <c>Partner = -1</c> hangs up.
    /// </summary>
    public sealed class PhoneCallCommand : ICommand<World>
    {
        public readonly int Player, Partner;
        public PhoneCallCommand(int player, int partner) { Player = player; Partner = partner; }

        public CommandResult Validate(World w)
        {
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count) return CommandResult.Fail("hall.player");
            if (Partner >= OpenAT.Sim.Player.Count || Partner == Player) return CommandResult.Fail("hall.player");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var players = w.Hall.Players;
            int old = players[Player].PhonePartner - 1;
            if (old >= 0 && players[old].PhonePartner == Player + 1) players[old].PhonePartner = 0;
            players[Player].PhonePartner = 0;
            if (Partner < 0) return;
            players[Player].PhonePartner = Partner + 1;
            players[Partner].PhonePartner = Player + 1;
        }
    }
}
