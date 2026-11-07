using System.Runtime.CompilerServices;

namespace OpenAT.Tests
{
    /// <summary>
    /// The hall module and the briefing recorder add systems to static factory lists that every new world
    /// copies. Test classes run in parallel, so registering them lazily from a test let worlds created at
    /// the same moment see different system sets (random "collection was modified" errors and lockstep
    /// differences). Register them once, before any test runs.
    /// </summary>
    static class TestAssemblySetup
    {
        [ModuleInitializer]
        internal static void Init()
        {
            OpenAT.Sim.Hall.HallModule.Register();
            OpenAT.Sim.Missions.BriefingRecorder.Register();
        }
    }
}
