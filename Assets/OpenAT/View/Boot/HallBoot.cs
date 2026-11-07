using OpenAT.Sim;
using OpenAT.Sim.Hall;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Hooks the hall into the app without touching GameApp: registers the hall systems before any
    /// world is created and teaches <see cref="HallContent"/> how to load the hall tables from the
    /// install. Also provides the test wiring for <c>-openat-screen hall</c>.
    /// </summary>
    public static class HallBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            HallModule.Register();
            HallContent.Resolver = data =>
            {
                var install = GameApp.Instance?.Assets?.Install;
                return install != null ? HallData.Load(install) : null;
            };
        }

        /// <summary>
        /// Returns the running world with a hall. Without a session (started directly with
        /// <c>-openat-screen hall</c>) a free game is created with default settings, the figures are
        /// placed in front of their offices as after the morning briefing, and the working day starts.
        /// </summary>
        /// <summary>True when the hall created its own session (test boot without the room framework).</summary>
        public static bool Standalone { get; private set; }

        public static World EnsureHallSession(GameApp app)
        {
            if (app.Assets == null) return null;
            Init();
            bool created = false;
            if (app.Session == null)
            {
                app.Session = GameSession.NewGame(app.Assets, new NewGameSettings());
                created = true;
                Standalone = System.Type.GetType("OpenAT.View.Rooms.Framework.RoomHost, OpenAT.View") == null;
            }
            var w = app.Session.World;
            if (w.Hall == null) HallModule.EnsureHall(w, HallContent.For(w.Data) ?? HallData.Load(app.Assets.Install));
            if (created && w.Hall != null)
            {
                w.Hall.PlaceAfterBriefing();
                w.StartWorkingDay();
            }
            return w;
        }
    }
}
