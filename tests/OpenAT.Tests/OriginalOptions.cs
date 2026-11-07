using System.Reflection;
using OpenAT.Sim;
using OpenAT.Sim.AI;

namespace OpenAT.Tests
{
    /// <summary>
    /// Options that restore the original game's bugs (docs/QUIRKS.md): every <c>Fix*</c> switch off.
    /// Tests of original behaviour use these; tests of the fixed behaviour use the defaults.
    /// </summary>
    static class OriginalOptions
    {
        /// <summary>GameOptions with every Fix* switch off; other options keep their defaults unless changed by <paramref name="tweak"/>.</summary>
        public static GameOptions Game(System.Action<GameOptions> tweak = null)
        {
            var o = new GameOptions();
            foreach (var f in typeof(GameOptions).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(bool) && f.Name.StartsWith("Fix")) f.SetValue(o, false);
            tweak?.Invoke(o);
            return o;
        }

        /// <summary>Turns every AI Fix* switch of the world off.</summary>
        public static World WithOriginalAi(this World w)
        {
            foreach (var f in typeof(AiOptions).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(bool) && f.Name.StartsWith("Fix")) f.SetValue(w.AI.Options, false);
            return w;
        }
    }
}
