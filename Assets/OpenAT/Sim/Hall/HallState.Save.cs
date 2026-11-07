using System;
using OpenAT.Sim.Save;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Save-game hooks of the hall: the static <see cref="Layout"/> is derived content (hall data +
    /// mission + segment levels) and is rebuilt from the saved levels instead of being written; the
    /// default room-busy delegate is restored; the hall systems are added to the fresh world.
    /// </summary>
    public sealed partial class HallState : ISaveHooks
    {
        /// <summary>Layout parameters of the save (filled right before saving).</summary>
        int[] _savedLevels;
        int _savedDifficulty;

        /// <summary>For the save loader: runs the field initialisers (UiBusy, Sympathy, lists); the saved fields are set afterwards.</summary>
        HallState() { }

        void ISaveHooks.OnBeforeSave(World w)
        {
            _savedLevels = Layout != null ? (int[])Layout.Levels.Clone() : null;
            _savedDifficulty = Layout?.Difficulty ?? w.Difficulty;
        }

        void ISaveHooks.OnAfterLoad(World w)
        {
            var data = HallContent.For(w.Data)
                       ?? throw new SaveException("the save has an airport hall, but no hall content is attached to the game data (HallContent.Attach)");
            Layout = new HallLayout(data, _savedDifficulty, _savedLevels ?? HallLayout.DefaultLevels());
            if (Plates == null || Plates.Length != Layout.InitialPlates.Length)
                throw new SaveException("saved hall grid does not match the hall content");
            if (IsRoomBusy == null) IsRoomBusy = DefaultIsRoomBusy;

            // A world built before HallModule.Register (or without it) has no hall systems yet.
            if (w.Systems.Find(HallSystem.SystemName) == null)
            {
                w.Systems.Add(new HallSystem());
                w.Systems.Add(new HallNightSystem());
                w.Systems.Add(new HallNewDaySystem());
            }
            HallFlights.AddSystems(w);
            HallFlights.RegisterRules(w);
        }
    }
}
