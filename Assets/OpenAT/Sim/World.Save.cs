using OpenAT.Sim.Core;
using OpenAT.Sim.Save;

namespace OpenAT.Sim
{
    /// <summary>
    /// Save-game integration of the world (sim-core.md §5, Sim/Save): the autosave requests and the
    /// on-load fixups. The state itself is saved generically by <see cref="SaveGame"/>.
    /// </summary>
    public sealed partial class World : ISaveHooks
    {
        /// <summary>Times this game was loaded ("restore counter", sim-core.md §5.3).</summary>
        public int RestoreCount;

        partial void InitSave();

        partial void InitSave()
        {
            Systems.Add(new AutosaveSystem());
            // Call it a day writes the autosave too (sim-core.md §1.3), once the world's flag is set.
            Events.Subscribe<CommandExecuted>(e =>
            {
                if (e.Command is Missions.CallItADayCommand && CallItADay && Options.Autosave && !GameEnded)
                    Events.Publish(new AutosaveRequested(AutosaveReason.CallItADay));
            });
        }

        void ISaveHooks.OnBeforeSave(World w) { }

        /// <summary>On-load fixups (sim-core.md §5.3). Runs after every other object's hook.</summary>
        void ISaveHooks.OnAfterLoad(World w)
        {
            RestoreCount++;
            // Before 18:00 everyone's call-it-a-day is cleared: the player continues the day.
            if (Clock.Time < GameClock.DayEnd)
            {
                CallItADay = false;
                foreach (var p in Players) p.CalledItADay = false;
            }
            // Game data with more cities/routes than the save (content added since): grow the per-airline arrays.
            EnsureOperationsArrays();
        }
    }
}
