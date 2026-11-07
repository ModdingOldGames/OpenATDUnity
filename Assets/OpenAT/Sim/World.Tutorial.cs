namespace OpenAT.Sim
{
    public sealed partial class World
    {
        /// <summary>The scripted tutorial of mission 0 (Sim/Tutorial): running flag, step, monologue mode.</summary>
        public readonly OpenAT.Sim.Tutorial.TutorialState Tutorial = new OpenAT.Sim.Tutorial.TutorialState();

        /// <summary>Tutorial (Sim/Tutorial): setup step and the midnight end.</summary>
        partial void InitTutorial();

        partial void InitTutorial() => OpenAT.Sim.Tutorial.TutorialModule.Register(this);
    }
}
