using OpenAT.Sim.AI;

namespace OpenAT.Sim
{
    /// <summary>Computer-player state of an airline (ai-robots.md §2.1).</summary>
    public sealed partial class Player
    {
        /// <summary>Feelings of this airline towards each airline (index = other airline), start 50 / 60 for the next slot.</summary>
        public readonly int[] Sympathy = { 50, 50, 50, 50 };
        /// <summary>"Kooperation": robots cooperate with every other robot (shared branch offices for phone orders).</summary>
        public readonly bool[] Cooperates = new bool[Count];
        /// <summary>The robot brain's state (empty for humans).</summary>
        public RobotState Robot = new RobotState();

        /// <summary>Changes this airline's sympathy towards <paramref name="other"/> (the items domain uses it for sabotage victims).</summary>
        public void ChangeSympathy(int other, int delta)
        {
            if (other >= 0 && other < Count) Sympathy[other] += delta;
        }
    }
}
