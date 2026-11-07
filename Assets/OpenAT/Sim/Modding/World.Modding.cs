using OpenAT.Sim.Modding;
using OpenAT.Sim.Save;

namespace OpenAT.Sim
{
    /// <summary>
    /// Mod integration of the world (docs/MODDING.md): the world's copy of the tunables and the world
    /// hooks of the mods attached to the game data (<see cref="Data.GameData.Mods"/>). Runs at the end
    /// of construction, after every domain registered its default systems and rules, so mods can
    /// replace or decorate them; for loaded games the saved state is laid over the world afterwards.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Balance numbers read by the rules (defaults = original; overridden by mods). Not saved.</summary>
        [NotSaved] public Tunables Tunables;

        partial void InitModding()
        {
            Tunables = Data?.Tunables?.Clone() ?? Tunables.CreateDefault();
            Data?.Mods?.ApplyWorld(this);
        }
    }
}
