using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Staff
{
    /// <summary>
    /// The player has been told that the staff went on strike (sim-core.md §6.1 "Notifications"): once per
    /// strike, from 09:10 until 18:00, at the first moment without dialog or menu (the view decides that
    /// moment and shows the strike fax, MENU_SABOTAGEFAX mode 8). Marks the start as told and puts the
    /// in-tray letter <c>Lett 508</c> (strike hours) into a human airline's mail.
    /// </summary>
    public sealed class StrikeStartNotifiedCommand : ICommand<World>
    {
        public readonly int Player;
        public StrikeStartNotifiedCommand(int player) => Player = player;

        /// <summary>A strike is running and the player has not been told yet (09:10–18:00, not after calling it a day).</summary>
        public static bool Due(World w, Player p) =>
            p.StrikeHours > 0 && !p.StrikeNotified && !p.IsOut && !w.CallItADay
            && w.Clock.Hour >= 9 && w.Clock.Hour < 18 && (w.Clock.Minute > 10 || w.Clock.Hour > 9);

        public CommandResult Validate(World w)
        {
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count) return CommandResult.Fail("bad-player");
            return Due(w, w.Players[Player]) ? CommandResult.Success : CommandResult.Fail("strike.not-due");
        }

        public void Apply(World w)
        {
            var p = w.Players[Player];
            p.StrikeNotified = true;
            if (p.Owner == Owner.Human) w.Events.Publish(new LetterSent(p, "Lett", 508, p.StrikeHours));
        }
    }

    /// <summary>
    /// The personnel manager has phoned the player that the strike is over (sim-core.md §6.1 "Notifications";
    /// rooms-and-ui-3 §6.3): the end is told once (<see cref="Player.StrikeNotified"/>) and the end type is used up.
    /// The call itself (phone pane or face to face in the personnel office, dialog <c>Jobs</c> 1000 + end type) is the view's.
    /// </summary>
    public sealed class StrikeEndNotifiedCommand : ICommand<World>
    {
        public readonly int Player;
        public StrikeEndNotifiedCommand(int player) => Player = player;

        /// <summary>The strike is over and the player has not been told yet.</summary>
        public static bool Due(World w, Player p) =>
            p.StrikeHours == 0 && !p.StrikeNotified && p.StrikeEndType != 0 && w.Clock.Hour >= 9 && w.Clock.Hour < 18;

        public CommandResult Validate(World w)
        {
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count) return CommandResult.Fail("bad-player");
            return Due(w, w.Players[Player]) ? CommandResult.Success : CommandResult.Fail("strike.not-due");
        }

        public void Apply(World w)
        {
            var p = w.Players[Player];
            p.StrikeNotified = true;
            p.StrikeEndType = 0;
        }
    }
}
