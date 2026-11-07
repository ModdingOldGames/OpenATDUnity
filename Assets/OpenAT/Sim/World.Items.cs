using OpenAT.Sim.Items;

namespace OpenAT.Sim
{
    /// <summary>Items, pranks, security and special rooms (Sim/Items, items-security-special.md).</summary>
    public sealed partial class World
    {
        /// <summary>Airport-wide item and prank state.</summary>
        public readonly WorldItems Items = new WorldItems();

        partial void InitItems()
        {
            ItemsModule.Register(this);
        }
    }
}
