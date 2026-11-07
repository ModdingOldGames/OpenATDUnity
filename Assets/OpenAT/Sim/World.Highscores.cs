using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>Highscore hook of the world (sim-core.md §5.4). The table itself is kept by the view.</summary>
    public sealed partial class World
    {
        /// <summary>
        /// Id of this game session in the highscore table (one row per session). Saved with the game, so a loaded
        /// game keeps raising its own row. 0 = not assigned yet (new game or an older save): assigned on first use.
        /// </summary>
        public uint SessionId;

        /// <summary>The session used cheats: its scores never reach the highscore table. Saved, so loading does not clear it.</summary>
        public bool CheatedSession;

        /// <summary>
        /// Called by <see cref="UpdateStatistics(Player)"/> after the company value was refreshed: a human's value is
        /// offered to the highscore table (<see cref="HighscoreOffered"/>) unless the session cheated.
        /// </summary>
        void OfferHighscore(Player p)
        {
            if (!p.IsHuman || CheatedSession) return;
            if (SessionId == 0) SessionId = NewSessionId();
            Events.Publish(new HighscoreOffered { Player = p, Session = SessionId, Score = p.Statistics.Today(StatId.CompanyValue) });
        }

        /// <summary>
        /// Deterministic stand-in for the original's clock-based id: a hash of the new-game seed, the calendar start
        /// and the local airline (never 0).
        /// </summary>
        uint NewSessionId()
        {
            unchecked
            {
                uint h = 2166136261u;
                void Mix(long v) { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 16777619u; } }
                Mix(AI.Seed);
                Mix(Clock.StartTime);
                foreach (char c in Local.Airline ?? "") Mix(c);
                h &= 0x7fffffff;
                return h == 0 ? 1u : h;
            }
        }
    }
}
