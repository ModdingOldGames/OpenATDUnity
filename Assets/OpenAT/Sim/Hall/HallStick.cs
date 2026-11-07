using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Walking the local figure with an analog stick (OpenAT, <see cref="GameOptions.GamepadSupport"/>). The view sends
    /// the stick direction and how far it is pushed; the figure walks towards a tile a few steps ahead in that
    /// direction, with the original's steering, dodging, doors and stairs, at a pace that follows the stick:
    /// <see cref="MinPace"/> % of a walking step per tick barely pushed, 100 % (the normal walk) at
    /// <see cref="WalkTilt"/>, then running that keeps getting faster up to <see cref="MaxPace"/> % (three steps per
    /// tick, as fast as the original's coffee boost) when pushed fully. Releasing the stick stops the figure on its tile.
    /// </summary>
    public sealed class StickWalkCommand : ICommand<World>
    {
        public const int MinPace = 25, MaxPace = 300;
        /// <summary>Stick tilt (per mille) that gives the normal walking pace; more makes the figure run.</summary>
        public const int WalkTilt = 600;

        public readonly int Player, X, Y, Tilt;

        /// <param name="x">Column direction (−1 left, 0, 1 right).</param>
        /// <param name="y">Row direction (−1 towards the back wall, 0, 1 towards the front).</param>
        /// <param name="tilt">How far the stick is pushed, 0..1000 per mille; 0 stops.</param>
        public StickWalkCommand(int player, int x, int y, int tilt)
        {
            Player = player; X = Math.Sign(x); Y = Math.Sign(y); Tilt = Math.Max(0, Math.Min(1000, tilt));
        }

        /// <summary>Pace for a tilt: linear from <see cref="MinPace"/> to 100 up to <see cref="WalkTilt"/>, then up to <see cref="MaxPace"/>.</summary>
        public static int PaceFor(int tilt)
        {
            if (tilt <= 0) return 0;
            if (tilt <= WalkTilt) return MinPace + (100 - MinPace) * tilt / WalkTilt;
            return 100 + (MaxPace - 100) * (Math.Min(tilt, 1000) - WalkTilt) / (1000 - WalkTilt);
        }

        public CommandResult Validate(World w)
        {
            if (!w.Options.GamepadSupport) return CommandResult.Fail("hall.stick-off");
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= global::OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("hall.player");
            var hp = w.Hall.Players[Player];
            // Releasing the stick always ends stick walking, whatever else holds.
            if (Tilt == 0 || (X == 0 && Y == 0)) return hp.StickPace != 0 ? CommandResult.Success : CommandResult.Fail("hall.stick-idle");
            // Moving: the same gates as a click on the floor.
            if (w.Clock.Time < GameClock.DayStart) return CommandResult.Fail("hall.before-nine");
            if (w.Clock.Frozen) return CommandResult.Fail("hall.no-time");
            if (OpenAT.Sim.Tutorial.TutorialEngine.HallClicksBlocked(w)) return CommandResult.Fail("hall.tutorial");
            if (!hp.InHall) return CommandResult.Fail("hall.in-room");
            if (hp.IsStuck > 0 || hp.RunningToToilet) return CommandResult.Fail("hall.busy");
            var me = w.Hall.PlayerPerson(Player);
            if (me == null) return CommandResult.Fail("hall.no-figure");
            if (me.StatePar != 0) return CommandResult.Fail("hall.auto-walking");
            return CommandResult.Success;
        }

        public void Apply(World w) => w.Hall.StickWalk(Player, X, Y, Tilt, w);
    }

    public sealed partial class HallState
    {
        /// <summary>Tiles ahead of the figure that a stick walk aims at.</summary>
        const int StickReach = 3;

        internal void StickWalk(int player, int x, int y, int tilt, World w)
        {
            var hp = Players[player];
            var me = PlayerPerson(player);
            if (me == null) return;
            var tile = TileOfPosition(me.Position);
            if (tilt == 0 || (x == 0 && y == 0))
            {
                hp.StickPace = hp.StickAcc = hp.StickX = hp.StickY = 0;
                me.Running = false;
                if (hp.WalkActive && me.StatePar == 0) WalkToTile(player, tile);
                return;
            }
            if (hp.TalkPartner != 0) new EndTalkCommand(player).Apply(w);   // walking away ends a pending talk
            hp.WaitForRoom = 0;
            hp.DirectToRoom = 0;
            if (me.LookDir == 9)
            {
                me.Dir = 8;
                me.LookDir = Clan(me.ClanId)?.GimmickArt2 ?? 2;
                me.Phase = 0;
            }
            int pace = StickWalkCommand.PaceFor(tilt);
            if (hp.StickPace == 0) hp.StickAcc = 100;   // the first tick after a standstill steps at once
            hp.StickPace = pace;
            me.Running = pace > 100;
            // Aim again when the direction changes, when the figure has stopped, or when it is close to the aim.
            var aim = hp.PrimaryTarget;
            bool near = Math.Abs(aim.X - tile.X) <= 1 && Math.Abs(aim.Y - tile.Y) <= 1;
            if (x == hp.StickX && y == hp.StickY && hp.WalkActive && !near) return;
            hp.StickX = x; hp.StickY = y;
            bool up = tile.Y < 5;
            int minRow = up ? 0 : 5, maxRow = up ? 2 : 14;
            for (int k = StickReach; k >= 1; k--)
            {
                var t = new Pt(tile.X + x * k, Math.Max(minRow, Math.Min(maxRow, tile.Y + y * k)));
                if (t == tile) continue;
                int v = P(t.X, t.Y);
                if ((v & Plate.Players) == 0 || (v & Plate.Exits) == 0) continue;
                hp.PrimaryTarget = t;
                UpdateWaypoints(player);
                return;
            }
            // Nothing walkable that way: stand.
            if (hp.WalkActive) WalkToTile(player, tile);
        }

        /// <summary>
        /// Steps of a stick-walking figure this tick (see <see cref="HallPlayer.StickPace"/>): −1 when the figure is
        /// not stick-driven or not walking (the original's one step, plus one more when running), else 0 to 3.
        /// </summary>
        int StickSteps(HallPlayer hp, Person me)
        {
            if (hp.StickPace == 0 || hp.IsStuck > 0 || !hp.WalkActive) return -1;
            hp.StickAcc += hp.StickPace;
            int n = Math.Min(me.Running ? 3 : 1, hp.StickAcc / 100);
            hp.StickAcc = Math.Min(100, hp.StickAcc - n * 100);
            return n;
        }
    }
}
