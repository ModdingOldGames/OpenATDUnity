using OpenAT.Sim.AI;

namespace OpenAT.Sim
{
    /// <summary>
    /// Computer players (docs/spec/findings/ai-robots.md). Registration of the AI domain: rules (the brain,
    /// the mission feature matrix, the items bridge, the sympathy ledger), systems and the new-game step.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>World-level AI state: tuning table, AI quirk fixes, seed, trace.</summary>
        public AiState AI = new AiState();

        partial void InitAI()
        {
            if (!Rules.Has<IRobotFeatures>()) Rules.Set<IRobotFeatures>(RobotFeatureTable.CreateOriginal());
            if (!Rules.Has<IRobotBrain>()) Rules.Set<IRobotBrain>(new OriginalRobotBrain());
            // Replaced by the items/sabotage domain in InitItems() (runs after this).
            if (!Rules.Has<IRivalActions>()) Rules.Set<IRivalActions>(new NoRivalActions());
            // Sympathy is the AI's state (Player.Sympathy); the items domain changes it through this rule.
            if (!Rules.Has<Items.IRivalSympathy>()) Rules.Set<Items.IRivalSympathy>(new PlayerSympathyLedger());
            AiSystems.Register(this);
        }
    }
}

namespace OpenAT.Sim.AI
{
    /// <summary>The items domain's sympathy rule backed by <see cref="Player.Sympathy"/> (no clamp, like the original).</summary>
    public sealed class PlayerSympathyLedger : Items.IRivalSympathy
    {
        public int Get(World w, int who, int towards) => w.Players[who].Sympathy[towards];
        public void Change(World w, int who, int towards, int delta) => w.Players[who].ChangeSympathy(towards, delta);
    }
}
