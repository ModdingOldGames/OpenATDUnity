using OpenAT.Sim.Core;

namespace OpenAT.Sim.Save
{
    public enum AutosaveReason
    {
        /// <summary>The clock crossed 17:00 on a day that has not been ended (sim-core.md §2.4).</summary>
        FiveOClock = 0,
        /// <summary>Call it a day (sim-core.md §1.3).</summary>
        CallItADay = 1,
    }

    /// <summary>
    /// The game wants the autosave written to slot <see cref="SaveGame.AutosaveSlot"/> (name: std text
    /// <c>Misc</c> 5000; after the 17:00 save the view also posts advisor message <c>Advice</c> 500).
    /// Only published when <see cref="GameOptions.Autosave"/> is on. The view does the IO.
    /// <para><b>Save after <see cref="World.Tick"/> has returned</b>, not inside the handler: the 17:00
    /// event is published in the middle of a tick (minute systems), and a save taken there would resume
    /// with the rest of that tick skipped. The call-it-a-day event comes from a command (between ticks),
    /// so saving at once is fine there, but deferring works for both.</para>
    /// </summary>
    public sealed class AutosaveRequested : ISimEvent
    {
        public readonly AutosaveReason Reason;
        public readonly int Slot = SaveGame.AutosaveSlot;
        public AutosaveRequested(AutosaveReason reason) => Reason = reason;
    }

    /// <summary>Minute hook: 17:00 autosave request (sim-core.md §2.4 "At 17:00"). Order 9900 = after the minute work.</summary>
    public sealed class AutosaveSystem : SimSystem
    {
        public const string SystemName = "save.autosave";
        public override string Name => SystemName;
        public override int Order => 9900;

        public override void OnMinute(World w)
        {
            var c = w.Clock;
            if (c.Hour != 17 || c.Minute != 0) return;
            if (!w.Options.Autosave || w.CallItADay || w.GameEnded || c.Frozen || c.State != DayState.Playing) return;
            w.Events.Publish(new AutosaveRequested(AutosaveReason.FiveOClock));
        }
    }
}
