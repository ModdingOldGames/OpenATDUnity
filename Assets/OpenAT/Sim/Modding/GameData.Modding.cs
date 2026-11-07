using OpenAT.Sim.Modding;

namespace OpenAT.Sim.Data
{
    /// <summary>Mod state of the content tables (docs/MODDING.md).</summary>
    public sealed partial class GameData
    {
        /// <summary>Balance numbers with every mod override applied (each world copies it: <see cref="World.Tunables"/>).</summary>
        public readonly Tunables Tunables = Tunables.CreateDefault();

        /// <summary>The mods applied to this data (null = none). Worlds built from it run the mods' world hooks.</summary>
        public ModSet Mods;
    }
}
