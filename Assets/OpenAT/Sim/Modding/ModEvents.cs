using OpenAT.Sim.Core;

namespace OpenAT.Sim.Modding
{
    /// <summary>
    /// A news item of a mod, in the mod's own words (mods have no entries in the game's text files).
    /// The view shows it as an advisor message to the local player when <see cref="PlayerIndex"/> is −1
    /// or the local player; headless runs can log it.
    /// </summary>
    public sealed class ModNewsEvent : ISimEvent
    {
        public readonly string ModId;
        public readonly string Text;
        /// <summary>Airline the news is for; −1 = everyone.</summary>
        public readonly int PlayerIndex;

        public ModNewsEvent(string modId, string text, int playerIndex = -1)
        {
            ModId = modId;
            Text = text;
            PlayerIndex = playerIndex;
        }

        public override string ToString() => "[" + ModId + "] " + Text;
    }
}
