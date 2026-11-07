using System.Collections.Generic;
using OpenAT.View.Rooms.Framework;

namespace OpenAT.View
{
    /// <summary>
    /// The global ambient volume each location sets when the player enters it (airport-and-people.md §10.1,
    /// rooms-and-ui-1 §2.6): the airport hall 100, most rooms 30..60, the evening screen and the options 0.
    /// A location without an entry leaves the volume as it was (laptop, planner, statistics, notice board,
    /// world map, toilets). Phone panes never change it. One central table so rooms need no code for it;
    /// a room can still override <see cref="RoomScreen.AmbientVolume"/>, and mods can change entries with
    /// <see cref="Set"/> / <see cref="Remove"/>.
    /// </summary>
    public static class RoomAmbience
    {
        /// <summary>The airport hall (and the value before any room was entered).</summary>
        public const int Hall = 100;
        /// <summary>The in-game options screen.</summary>
        public const int Options = 0;

        static readonly Dictionary<int, int> Volumes = new Dictionary<int, int>
        {
            [RoomIds.Abend] = 0,
            [RoomIds.Globe] = 40,
            [RoomIds.OfficeA] = 40, [RoomIds.OfficeB] = 40, [RoomIds.OfficeC] = 40, [RoomIds.OfficeD] = 40,
            [RoomIds.PersonalA] = 40, [RoomIds.PersonalB] = 40, [RoomIds.PersonalC] = 40, [RoomIds.PersonalD] = 40,
            [RoomIds.DutyFree] = 40,
            [RoomIds.Ricks] = 50,
            [RoomIds.RouteBox] = 60,
            [RoomIds.Bank] = 40,
            [RoomIds.Kiosk] = 60,
            [RoomIds.Museum] = 40,
            [RoomIds.LastMinute] = 60,
            [RoomIds.ArabAir] = 60,
            [RoomIds.Makler] = 40,
            [RoomIds.Aufsicht] = 30,
            [RoomIds.Werbung] = 40,
            [RoomIds.Werkstatt] = 40,
            [RoomIds.Reisebuero] = 60,
            [RoomIds.Sabotage] = 40,
            [RoomIds.Nasa] = 40,
            [RoomIds.Insel] = 60,
            [RoomIds.Rushmore] = 60,
            [RoomIds.WeltAll] = 60,
            [RoomIds.PlaneProps] = 40,
            [RoomIds.Fracht] = 60,
            [RoomIds.Security] = 60,
            [RoomIds.Editor] = 60,
            [RoomIds.Designer] = 60,
        };

        /// <summary>The volume (0..100) a room sets on entry, or null when it keeps the current one.</summary>
        public static int? For(int room) => Volumes.TryGetValue(room, out int v) ? v : (int?)null;

        /// <summary>Sets (or replaces) a room's entry volume (0..100).</summary>
        public static void Set(int room, int volume) => Volumes[room] = volume < 0 ? 0 : volume > 100 ? 100 : volume;

        /// <summary>The room no longer changes the volume.</summary>
        public static void Remove(int room) => Volumes.Remove(room);
    }
}
