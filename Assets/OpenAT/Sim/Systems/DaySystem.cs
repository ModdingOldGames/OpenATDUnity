using OpenAT.Sim.Core;

namespace OpenAT.Sim.Systems
{
    /// <summary>
    /// The day-state machine and the fixed points of the midnight sequence (sim-core.md §1.3, §2.6,
    /// §2.7): 18:00 ends the working day, midnight advances the date (step 13) and runs every airline's
    /// new day (step 18), 09:00 freezes the clock for the morning briefing.
    /// Other systems use orders around these steps (step × 100).
    /// </summary>
    public sealed class DaySystem : SimSystem
    {
        public const string SystemName = "core.day";
        public override string Name => SystemName;
        public override int Order => 0;

        /// <summary>Midnight step numbers (× 100) of the fixed points handled here.</summary>
        public const int StepAdvanceDate = 1300;
        public const int StepPlayerNewDay = 1800;

        public override void OnTick(World w)
        {
            var c = w.Clock;
            // End of the working day.
            if (c.State == DayState.Playing && !c.Frozen && (c.Time >= GameClock.DayEnd || w.CallItADay))
            {
                c.State = DayState.Evening;
                w.Systems.RunEvening(w);
                w.Events.Publish(new EveningStarted(c.Date));
                // The evening screen hands over to the night fast-forward.
                c.State = DayState.Night;
            }
            // Morning: the clock stops at exactly 09:00 for the briefing.
            if (c.State == DayState.Night && c.Time == GameClock.DayStart)
            {
                c.State = DayState.Morning;
                c.Frozen = true;
                c.TimeSlice = 0;
                w.Systems.RunMorning(w);
                w.Events.Publish(new MorningStarted(c.Date));
            }
        }

        public override void OnNewDay(World w)
        {
            // Reseed the pedestrian streams with the old date (step 1).
            w.PersonRandCreate.SetSeed(w.Clock.Date);
            w.PersonRandMisc.SetSeed(w.Clock.Date);
        }

        public override void OnPlayerNewDay(World w, Player p)
        {
            // Step 1 of the per-airline sequence.
            p.WalkRandom.SetSeed(w.Clock.Date + p.Index);
            p.ExtraRandom.SetSeed(0);
        }
    }

    /// <summary>Advances the date at its slot in the midnight sequence (step 13).</summary>
    public sealed class AdvanceDateSystem : SimSystem
    {
        public override string Name => "core.advance-date";
        public override int Order => DaySystem.StepAdvanceDate;
        public override void OnNewDay(World w) => w.AdvanceDate();
    }

    /// <summary>Runs every airline's new day at its slot in the midnight sequence (step 18).</summary>
    public sealed class PlayerNewDaySystem : SimSystem
    {
        public override string Name => "core.player-new-day";
        public override int Order => DaySystem.StepPlayerNewDay;
        public override void OnNewDay(World w) => w.RunPlayerNewDays();
    }

    /// <summary>Clears per-day flags at the end of the midnight sequence (steps 30–31).</summary>
    public sealed class EndOfNightSystem : SimSystem
    {
        public override string Name => "core.end-of-night";
        public override int Order => 3000;
        public override void OnNewDay(World w)
        {
            foreach (var p in w.Players) p.CalledItADay = false;
            w.CallItADay = false;
        }
    }

    public sealed class EveningStarted : ISimEvent { public readonly int Date; public EveningStarted(int d) => Date = d; }
    public sealed class MorningStarted : ISimEvent { public readonly int Date; public MorningStarted(int d) => Date = d; }
}
