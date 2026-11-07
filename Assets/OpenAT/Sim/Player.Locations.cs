using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim
{
    public sealed partial class Player
    {
        /// <summary>
        /// The airline's room stack as last reported by its view, bottom first, without the airport and the
        /// options (sim-core.md §5.2 "the room stack (10 entries)"). Recorded right before a save; on load the
        /// view re-enters the top room (§5.3, rooms-and-ui-9 §6.5).
        /// </summary>
        public int[] RoomStack = Array.Empty<int>();

        /// <summary>Airport camera (left/top edge in hall pixels) at the last save (§5.2 "camera/view state"); null = unknown.</summary>
        public int? CameraX, CameraY;
    }

    /// <summary>The view reports the player's current location stack and airport camera (before saving).</summary>
    public sealed class RecordRoomStackCommand : ICommand<World>
    {
        public const int MaxDepth = 10;
        public readonly int PlayerIndex;
        public readonly int[] Rooms;
        public readonly int? CameraX, CameraY;

        public RecordRoomStackCommand(int player, int[] rooms, int? cameraX = null, int? cameraY = null)
        {
            PlayerIndex = player; Rooms = rooms ?? Array.Empty<int>(); CameraX = cameraX; CameraY = cameraY;
        }

        public CommandResult Validate(World w)
        {
            if (PlayerIndex < 0 || PlayerIndex >= Player.Count) return CommandResult.Fail("bad-player");
            if (Rooms.Length > MaxDepth) return CommandResult.Fail("too-deep");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var p = w.Players[PlayerIndex];
            p.RoomStack = (int[])Rooms.Clone();
            p.CameraX = CameraX;
            p.CameraY = CameraY;
        }
    }
}
