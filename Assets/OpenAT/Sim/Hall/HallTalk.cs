using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Talking to another airline's figure in the hall (rooms-and-ui-9 §8.3, rooms-and-ui-3 §8 "face-to-face"):
    /// a click on another player's figure (within 640 px, that player in the hall, not stuck, not talking, not
    /// called it a day) looks for a free tile next to it — north, south, then west or east depending on the side —
    /// and walks there. A computer partner stops at once and turns towards the player; when the walk ends the
    /// hall publishes <see cref="HallRequest.TalkToPlayer"/> and the view runs the competitor dialog.
    /// </summary>
    public sealed class WalkToPlayerCommand : ICommand<World>
    {
        public readonly int Player, Other;
        public WalkToPlayerCommand(int player, int other) { Player = player; Other = other; }

        public CommandResult Validate(World w)
        {
            var h = w.Hall;
            if (h == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count || Other < 0 || Other >= OpenAT.Sim.Player.Count || Player == Other) return CommandResult.Fail("hall.player");
            if (w.Players[Other].IsOut || w.Players[Player].IsOut) return CommandResult.Fail("hall.player");
            if (w.Clock.Frozen) return CommandResult.Fail("hall.no-time");
            var hp = h.Players[Player];
            var ho = h.Players[Other];
            var me = h.PlayerPerson(Player);
            var it = h.PlayerPerson(Other);
            if (me == null || it == null) return CommandResult.Fail("hall.no-figure");
            if (!hp.InHall || !ho.InHall) return CommandResult.Fail("hall.in-room");
            if (hp.IsWalkingToPlayer != -1 || me.StatePar != 0 || it.StatePar != 0) return CommandResult.Fail("hall.busy");
            if (ho.IsStuck > 0 || ho.TalkPartner != 0 || w.Players[Other].CalledItADay) return CommandResult.Fail("hall.other-busy");
            if (Math.Abs(it.Position.X - me.Position.X) >= 640 || Math.Abs(it.Position.Y - me.Position.Y) >= 640) return CommandResult.Fail("hall.too-far");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var h = w.Hall;
            var me = h.PlayerPerson(Player);
            var it = h.PlayerPerson(Other);
            var a = HallState.TileOfPosition(it.Position);
            Pt? target = null;
            int phase = 0;
            int plate = h.P(a.X, a.Y);
            if ((plate & 128) != 0) { target = new Pt(a.X, a.Y - 1); phase = 0; }
            if ((plate & 32) != 0) { target = new Pt(a.X, a.Y + 1); phase = 2; }
            if (it.Position.X > me.Position.X)
            {
                if ((plate & 16) != 0) { target = (h.P(a.X - 1, a.Y) & 16) != 0 ? new Pt(a.X - 2, a.Y) : new Pt(a.X - 1, a.Y); phase = 3; }
            }
            else if ((plate & 64) != 0) { target = (h.P(a.X + 1, a.Y) & 64) != 0 ? new Pt(a.X + 2, a.Y) : new Pt(a.X + 1, a.Y); phase = 1; }
            it.LookDir = 8;
            if (target == null) return;   // no room to stand next to it
            h.WalkToTile(Player, target.Value);
            h.Players[Player].IsWalkingToPlayer = Other;
            if (w.Players[Other].IsComputer)
            {
                // The computer stops and turns to the player; it stays until the talk ends.
                it.Phase = phase;
                it.Dir = 8;
                h.Players[Other].IsWalkingToPlayer = Player;
                h.Players[Other].TalkPartner = Player + 1;
                h.Players[Other].WalkActive = false;
            }
            h.Players[Player].TalkPartner = Other + 1;
        }
    }

    /// <summary>The face-to-face talk ended (or the walk to the partner was given up): both figures are free again.</summary>
    public sealed class EndTalkCommand : ICommand<World>
    {
        public readonly int Player;
        public EndTalkCommand(int player) => Player = player;

        public CommandResult Validate(World w) => w.Hall == null ? CommandResult.Fail("hall.none") : CommandResult.Success;

        public void Apply(World w)
        {
            var h = w.Hall;
            int other = h.Players[Player].TalkPartner - 1;
            h.Players[Player].TalkPartner = 0;
            h.Players[Player].IsWalkingToPlayer = -1;
            if (other >= 0 && other < h.Players.Length && h.Players[other].TalkPartner == Player + 1)
            {
                h.Players[other].TalkPartner = 0;
                h.Players[other].IsWalkingToPlayer = -1;
            }
        }
    }
}
