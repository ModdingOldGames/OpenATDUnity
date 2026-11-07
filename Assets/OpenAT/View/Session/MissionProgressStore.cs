using OpenAT.Sim;
using OpenAT.Sim.Missions;
using UnityEngine;

namespace OpenAT.View.Session
{
    /// <summary>
    /// The highest unlocked mission per campaign ("max difficulty", sim-core.md §5.5), kept with the options
    /// in PlayerPrefs (plain values; the original obfuscates them). Defaults: base 0, add-on 11, ATFS 41.
    /// </summary>
    public static class MissionProgressStore
    {
        const string Prefix = "OpenAT.Progress.MaxMission.";

        static string Key(Campaign c) => Prefix + c;

        public static int Max(Campaign c) => c == Campaign.None ? Mission.FreeGame : PlayerPrefs.GetInt(Key(c), MissionProgress.DefaultMax(c));

        public static bool IsLocked(int difficulty) => MissionProgress.IsLocked(difficulty, Max(MissionProgress.CampaignOf(difficulty)));

        /// <summary>The local player won <paramref name="difficulty"/>: unlock the next mission of its campaign.</summary>
        public static void RecordWin(int difficulty)
        {
            var c = MissionProgress.CampaignOf(difficulty);
            if (c == Campaign.None) return;
            int max = MissionProgress.MaxAfterWin(Max(c), difficulty);
            if (max == Max(c)) return;
            PlayerPrefs.SetInt(Key(c), max);
            PlayerPrefs.Save();
            Debug.Log("OpenAT: mission " + difficulty + " won, campaign " + c + " unlocked up to " + max);
        }

        /// <summary>Debug unlock (the original's cheat code): every mission of every campaign.</summary>
        public static void UnlockAll()
        {
            foreach (var c in new[] { Campaign.Base, Campaign.AddOn, Campaign.Atfs }) PlayerPrefs.SetInt(Key(c), MissionProgress.Last(c));
            PlayerPrefs.Save();
        }
    }
}
