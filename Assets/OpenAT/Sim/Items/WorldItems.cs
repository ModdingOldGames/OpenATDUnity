using System.Collections.Generic;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Airport-wide item and prank state (items-security-special.md §1.4, §2.5, §3.1): the once-per-day
    /// world items, the laptop sale lock, the security-office outage, the workshop slime, the prank list
    /// for the next briefing and the fallback sympathy ledger.
    /// </summary>
    public sealed class WorldItems
    {
        public readonly ItemCatalog Catalog = new ItemCatalog();

        // ---- world items, once per day for everybody (§3.1); restored at midnight (sim-core §2.6 step 2)
        public bool Glove = true, PaperClip = true, GlowingCoal = true, Perfume = true, Pliers = true, Postcard = true;
        /// <summary>Glue at the freight counter: 0 locked, 1 unlocked by the paper clip, 2 taken.</summary>
        public int GlueState;

        /// <summary>Airline that bought today's laptop, or −1 (one laptop sale per day for the airport).</summary>
        public int LaptopSoldTo = -1;
        /// <summary>Days the security office stays closed after the pliers sabotage (−1 per night).</summary>
        public int SecurityOutageDays;
        /// <summary>Workshop slime: −1 clean, 2 slimed (set by the workshop room's animation, reset every 6th day).</summary>
        public int Slimed = -1;
        /// <summary>An office power cut happened today (airport door graphics reload at night).</summary>
        public bool OfficePowerCutToday;

        /// <summary>Executed pranks waiting for the next briefing ("SabotageActs").</summary>
        public readonly List<SabotageAct> Acts = new List<SabotageAct>();
        /// <summary>This morning's report (built at 9:00 from <see cref="Acts"/>).</summary>
        public SabotageReport Report = new SabotageReport();
        /// <summary>Date of the last overnight prank execution (leaving the briefing), −1 = none yet.</summary>
        public int MorningExecutedDate = -1;

        /// <summary>Default sympathy store [who feels, towards whom] when no AI sympathy rule is registered.</summary>
        public readonly int[,] Sympathy = new int[Player.Count, Player.Count];

        /// <summary>Restores the world items for a new day (sim-core.md §2.6 step 2).</summary>
        public void RestoreDailyItems()
        {
            Glove = PaperClip = GlowingCoal = Perfume = Pliers = Postcard = true;
            GlueState = 0;
        }
    }
}
