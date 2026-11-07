namespace OpenAT.Sim.Missions
{
    /// <summary>The three mission campaigns (rooms-and-ui-4 §3.4–3.5).</summary>
    public enum Campaign { None = -1, Base = 0, AddOn = 1, Atfs = 2 }

    /// <summary>
    /// Campaign progress (sim-core.md §3.4, §5.5; rooms-and-ui-3b §3 "Unlock"): winning a mission unlocks the
    /// next one of its campaign; the highest unlocked mission per campaign is stored with the options (not
    /// in the save). The rocket / station finals play an outro movie (rooms-and-ui-4 §6.2).
    /// </summary>
    public static class MissionProgress
    {
        public static Campaign CampaignOf(int difficulty)
        {
            if (Mission.IsBase(difficulty)) return Campaign.Base;
            if (Mission.IsAddOn(difficulty)) return Campaign.AddOn;
            if (Mission.IsAtfs(difficulty)) return Campaign.Atfs;
            return Campaign.None;
        }

        public static int First(Campaign c) => c == Campaign.Base ? Mission.Tutorial : c == Campaign.AddOn ? Mission.AddOnFirst : c == Campaign.Atfs ? Mission.AtfsFirst : Mission.FreeGame;
        public static int Last(Campaign c) => c == Campaign.Base ? Mission.Rocket : c == Campaign.AddOn ? Mission.AddOnLast : c == Campaign.Atfs ? Mission.AtfsLast : Mission.FreeGame;

        /// <summary>Highest unlocked mission when nothing is stored yet: the first of the campaign.</summary>
        public static int DefaultMax(Campaign c) => First(c);

        /// <summary>The mission a win of <paramref name="difficulty"/> unlocks, or <see cref="Mission.FreeGame"/> for none (finals, free game).</summary>
        public static int UnlockedByWin(int difficulty)
        {
            var c = CampaignOf(difficulty);
            if (c == Campaign.None || difficulty >= Last(c)) return Mission.FreeGame;
            return difficulty + 1;
        }

        /// <summary>The stored maximum after a win of <paramref name="difficulty"/> (never lowers it).</summary>
        public static int MaxAfterWin(int storedMax, int difficulty)
        {
            int next = UnlockedByWin(difficulty);
            return next > storedMax ? next : storedMax;
        }

        public static bool IsLocked(int difficulty, int storedMax) => CampaignOf(difficulty) != Campaign.None && difficulty > storedMax;

        /// <summary>
        /// Movie after the game-over menu of a won game (rooms-and-ui-4 §6.2): base final → <c>intro/outro.smk</c>,
        /// add-on final → <c>intro/outro2.smk</c>, none otherwise (no ATFS outro). <paramref name="rightClick"/>
        /// reproduces the quirk that a right click on the menu never plays the add-on outro (rooms-and-ui-2 §2.8).
        /// </summary>
        public static string OutroAfterWin(int difficulty, bool rightClick = false)
        {
            if (difficulty == Mission.Rocket) return "intro/outro.smk";
            if (difficulty == Mission.AddOnLast && !rightClick) return "intro/outro2.smk";
            return null;
        }

        /// <summary>Mission number in the save info text (Misc 1150): 1-based within the campaign.</summary>
        public static int NumberInCampaign(int difficulty)
        {
            var c = CampaignOf(difficulty);
            return c == Campaign.None ? 0 : difficulty - First(c) + 1;
        }
    }
}
