namespace OpenAT.Sim.Core
{
    /// <summary>Phase of the game day (docs/spec/findings/sim-core.md §1.3).</summary>
    public enum DayState
    {
        Morning = 1,   // 09:00 briefing, clock frozen
        Playing = 2,
        Evening = 3,   // evening screen open
        Night = 4,     // fast-forward to the next morning
    }

    /// <summary>
    /// Game time in the original's units (sim-core.md §1.1): 1000 units = 1 minute, one tick adds 30
    /// units, the day runs 0..1 440 000. <see cref="Date"/> counts days from 0.
    /// </summary>
    public sealed class GameClock
    {
        public const int UnitsPerMinute = 1000;
        public const int UnitsPerHour = 60 * UnitsPerMinute;
        public const int UnitsPerDay = 24 * UnitsPerHour;
        /// <summary>Time added per tick (the original's constant GameSpeed).</summary>
        public const int UnitsPerTick = 30;
        /// <summary>Simulation ticks per real second at speed level 0.</summary>
        public const int TicksPerSecond = 20;

        public const int DayStart = 9 * UnitsPerHour;
        public const int DayEnd = 18 * UnitsPerHour;

        /// <summary>Time of day in units (0..UnitsPerDay).</summary>
        public int Time = DayStart;
        /// <summary>Day counter, 0 = first day.</summary>
        public int Date;
        public DayState State = DayState.Morning;
        /// <summary>Clock frozen ("bNoTime"): briefing and evening hand-over.</summary>
        public bool Frozen = true;
        /// <summary>Ticks played today (reset at 9:00 and midnight).</summary>
        public int TimeSlice;
        public long TicksPlayed;

        /// <summary>Calendar start (seconds since 1970-01-01) and the derived weekday of day 0 (0 = Monday).</summary>
        public long StartTime;
        public int StartWeekday;
        /// <summary>Weekday used for opening hours (0 = Monday .. 6 = Sunday), see sim-core.md §1.2.</summary>
        public int Weekday;

        public int Hour => Time / UnitsPerHour;
        public int Minute => Time / UnitsPerMinute % 60;
        public int MinuteOfDay => Time / UnitsPerMinute;

        public static int At(int hour, int minute = 0) => hour * UnitsPerHour + minute * UnitsPerMinute;

        /// <summary>Weekday shown on screen for the current day (0 = Monday).</summary>
        public int DisplayWeekday => (StartWeekday + Date) % 7;

        public string ClockText => $"{Hour,2}:{Minute:00}";

        /// <summary>Calendar date of the current day (UTC).</summary>
        public System.DateTime CalendarDate => new System.DateTime(1970, 1, 1).AddSeconds(StartTime).AddDays(Date);

        /// <summary>Season index 0 spring, 1 summer, 2 autumn, 3 winter (sim-core.md §1.2).</summary>
        public int Season
        {
            get
            {
                var d = CalendarDate;
                int v = d.Month * 30 + d.Day;
                if (v >= 95 && v <= 170) return 0;
                if (v >= 171 && v <= 251) return 1;
                if (v >= 252 && v <= 379) return 2;
                return 3;
            }
        }

        /// <summary>Sunday-based weekday (0 = Sunday) of a unix timestamp, like C's tm_wday.</summary>
        public static int SundayBasedWeekday(long unixSeconds)
        {
            long days = unixSeconds >= 0 ? unixSeconds / 86400 : (unixSeconds - 86399) / 86400;
            return (int)(((days + 4) % 7 + 7) % 7); // 1970-01-01 was a Thursday
        }

        public void InitCalendar(long startTime)
        {
            StartTime = startTime;
            StartWeekday = (SundayBasedWeekday(startTime) + 6) % 7;
            Weekday = StartWeekday;
        }
    }
}
