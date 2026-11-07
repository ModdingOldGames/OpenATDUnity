using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// The half-hourly airport notice (rooms-and-ui-3 §5.6, airport-and-people.md §10): at hh:30 of the even hours of
    /// the working day (10:30, 12:30, 14:30, 16:30) the announcer plays one notice sample. The choice is a local
    /// generator seeded with date + start time + hour (it does not touch the world's random streams): 1 in 10 the
    /// vehicle notice, otherwise the zone notice when (hour / 2) is even, else the general notice. The view
    /// (<c>HallAnnouncer</c>) adds its own conditions (nothing being announced, no warp, game speed below 2, ...).
    /// </summary>
    public static class HallAnnouncements
    {
        /// <summary>Sample names (files <c>SOUND/&lt;name&gt;.raw</c>).</summary>
        public const string Vehicle = "Fahrzeug", Zone = "ZoneOK", Notice = "Hinweis";

        /// <summary>The clock is in a notice minute: from 09:00 to 18:00, minute 30 of an even hour.</summary>
        public static bool NoticeDue(GameClock c) =>
            c.Time >= GameClock.DayStart && c.Time <= GameClock.DayEnd && c.Minute == 30 && (c.Hour & 1) == 0;

        /// <summary>Which notice plays at this hour (same answer for the whole minute).</summary>
        public static string NoticeSample(GameClock c)
        {
            var r = new TeakRand((int)(c.Date + c.StartTime + c.Hour));
            if (r.Rand(10) == 0) return Vehicle;
            return ((c.Hour / 2) & 1) == 0 ? Zone : Notice;
        }
    }
}
