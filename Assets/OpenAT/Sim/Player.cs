using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim
{
    /// <summary>Who controls an airline.</summary>
    public enum Owner { Human = 0, Computer = 1, RemoteHuman = 2 }

    /// <summary>
    /// One of the four airlines. This is a partial class: each subsystem keeps its own state in its
    /// own file (Player.Finance.cs, Player.Fleet.cs, ...), so features can be added without touching
    /// unrelated code. Extension state that is not part of OpenAT itself goes into
    /// <see cref="Extensions"/>.
    /// </summary>
    public sealed partial class Player
    {
        public const int Count = 4;

        /// <summary>0..3, also the logo and colour index.</summary>
        public readonly int Index;
        public string Name;
        public string Airline;
        /// <summary>Two-letter airline code.</summary>
        public string Abbreviation;
        public Owner Owner;
        /// <summary>Removed from the game (bankrupt, taken over, image too low).</summary>
        public bool IsOut;

        public bool IsHuman => Owner != Owner.Computer;
        public bool IsComputer => Owner == Owner.Computer;
        public bool IsActive => !IsOut;

        /// <summary>Own speed level 0..3 (sim-core.md §1.4).</summary>
        public int GameSpeed;
        public bool CalledItADay;

        /// <summary>Per-airline random streams (sim-core.md §4.2).</summary>
        public readonly TeakRand WalkRandom = new TeakRand();
        public readonly TeakRand ExtraRandom = new TeakRand();

        /// <summary>State owned by extensions, keyed by a unique name.</summary>
        public readonly Dictionary<string, object> Extensions = new Dictionary<string, object>();

        public Player(int index) { Index = index; }

        public override string ToString() => $"P{Index} {Airline}";
    }
}
