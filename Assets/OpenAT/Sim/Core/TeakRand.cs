namespace OpenAT.Sim.Core
{
    /// <summary>
    /// The original game's random generator: a 32-bit linear congruential generator
    /// (docs/spec/findings/sim-core.md §4.2). Every economic process seeds its own instance from game
    /// state (date, hour, minute, start time), so using the same generator and the same seeding points
    /// reproduces the original's outcomes. This is the only randomness allowed in the Sim.
    /// </summary>
    public sealed class TeakRand
    {
        /// <summary>LCG multiplier as executed by the original (its comment says 7381; the code uses 214013).</summary>
        public const uint Multiplier = 214013;
        public const uint Increment = 2531011;

        public uint Seed { get; private set; }
        public uint Value { get; private set; }

        public TeakRand() { }
        public TeakRand(uint seed) => SetSeed(seed);
        public TeakRand(int seed) => SetSeed((uint)seed);

        public void SetSeed(uint seed) { Seed = seed; Value = seed; }
        public void SetSeed(int seed) => SetSeed((uint)seed);

        /// <summary>Restores both seed and current value (save games).</summary>
        public void Restore(uint seed, uint value) { Seed = seed; Value = value; }

        /// <summary>Back to the state right after seeding.</summary>
        public void Reset() => Value = Seed;

        /// <summary>Next raw value 0..65535.</summary>
        public int Rand()
        {
            unchecked { Value = Value * Multiplier + Increment; }
            return (int)(Value >> 16);
        }

        /// <summary>0..n-1 (0 when n &lt;= 0).</summary>
        public int Rand(int n) => n <= 0 ? 0 : Rand() % n;

        /// <summary>min..max inclusive.</summary>
        public int Rand(int min, int max) => max < min ? min : Rand() % (max - min + 1) + min;

        /// <summary>Convenience: true with probability percent/100.</summary>
        public bool Chance(int percent) => Rand(100) < percent;
    }
}
