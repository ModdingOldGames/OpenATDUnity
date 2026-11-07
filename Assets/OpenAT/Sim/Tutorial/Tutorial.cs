using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Systems;

namespace OpenAT.Sim.Tutorial
{
    /// <summary>
    /// The scripted tutorial of mission 0 (sim-core.md §2.2, docs/spec/findings/tutorial.md): whether it still
    /// runs and how far it got. Saved with the game (sim-core.md §5.2 "tutorial running", "tutorial step").
    /// Steps are the original's numbers (999, 1000, 1100, 1230, 1240, 1310, 1400, 1401, 1500..1507, 1600,
    /// 1602..1605, 2000, 2002); the assistant's text of a step has the same id in the <c>Tuto</c> group.
    /// </summary>
    public sealed class TutorialState
    {
        /// <summary>The tutorial runs (the assistant still guides the player).</summary>
        public bool Active;
        /// <summary>Current step (0 = not started yet).</summary>
        public int Step;
        /// <summary>The assistant stays on screen between her lines ("monologue" mode).</summary>
        public bool Monolog;
    }

    /// <summary>Well-known steps of the tutorial script.</summary>
    public static class TutorialStep
    {
        public const int NotStarted = 0, Start = 999, Welcome = 1000, GoToBoss = 1100, AtBoss = 1230, Briefing = 1240,
            GoToOffice = 1310, InOffice = 1400, ClickGlobe = 1401, InPlanner = 1500, PlannerIntro = 1501,
            OpenPlanes = 1502, OpenPlane = 1503, OpenOrders = 1504, TakeOrder = 1505, PlaceOrder = 1506,
            BackToHall = 1507, InHall = 1600, PassengersComing = 1602, CheckingIn = 1603, Waiting = 1604,
            PlaneAtGate = 1605, PlaneLeft = 2000, Farewell = 2002;
    }

    /// <summary>
    /// The tutorial moved on: the assistant drops her current line when <see cref="SkipCurrent"/> is set
    /// (the original's "next message") and then says <see cref="Texts"/> (<c>Tuto</c> ids, formatted with
    /// <see cref="Args"/>). <see cref="To"/> = −1 means the tutorial ended.
    /// </summary>
    public sealed class TutorialAdvanced : ISimEvent
    {
        public readonly Player Player;
        public readonly int From, To;
        public readonly int[] Texts;
        public readonly object[] Args;
        public readonly bool SkipCurrent;
        public TutorialAdvanced(Player p, int from, int to, bool skipCurrent, int[] texts, object[] args)
        {
            Player = p; From = from; To = to; SkipCurrent = skipCurrent;
            Texts = texts ?? Array.Empty<int>(); Args = args ?? Array.Empty<object>();
        }
    }

    /// <summary>The player cancelled the tutorial (status-bar button or Space).</summary>
    public sealed class TutorialCancelled : ISimEvent
    {
        public readonly int Step;
        /// <summary>The working day was started by the cancel (cancelled at 09:00 around the briefing).</summary>
        public readonly bool DayStarted;
        public TutorialCancelled(int step, bool dayStarted) { Step = step; DayStarted = dayStarted; }
    }

    /// <summary>The script steps the simulation itself detects, plus the gates other domains ask.</summary>
    public static class TutorialEngine
    {
        public static bool Running(World w) => w.Tutorial.Active && w.Difficulty == Mission.Tutorial;

        public static bool At(World w, int step) => w.Tutorial.Active && w.Tutorial.Step == step;

        /// <summary>Sets the step and tells the view what the assistant says.</summary>
        public static void Advance(World w, int to, bool skipCurrent, object[] args, params int[] texts)
        {
            int from = w.Tutorial.Step;
            w.Tutorial.Step = to;
            w.Events.Publish(new TutorialAdvanced(w.Local, from, to, skipCurrent, texts, args));
        }

        // ------------------------------------------------------------------ gates (original checks)

        /// <summary>
        /// Computer players act and figures walk only once the tutorial reached step 1100 (or it does not run;
        /// sim-core.md §2.3 step 3.1 and 3.3, ai-robots.md §3).
        /// </summary>
        public static bool FiguresMayMove(World w) =>
            w.Difficulty != Mission.Tutorial || !w.Tutorial.Active || w.Tutorial.Step >= TutorialStep.GoToBoss;

        /// <summary>Room windows open only before the script started or from step 1200 on (sim-core.md §2.3 step 2).</summary>
        public static bool RoomWindowsAllowed(World w) =>
            w.Difficulty != Mission.Tutorial || !w.Tutorial.Active || w.Tutorial.Step == 0 || w.Tutorial.Step >= 1200;

        /// <summary>While the figure walks to the office on its own (step 1310) hall clicks are ignored.</summary>
        public static bool HallClicksBlocked(World w) => At(w, TutorialStep.GoToOffice);

        // ------------------------------------------------------------------ steps found by the simulation

        /// <summary>
        /// Step 1600: the first departing passenger of the local airline appears at the left-most of 101
        /// random birthplaces (all drawn from <paramref name="rand"/>, like the original), then 1602.
        /// Returns true when this call created the figure (the caller then skips its normal spawn).
        /// </summary>
        public static bool SpawnFirstPassenger(World w, Hall.HallState hall, TeakRand rand, int airline, int planeId, int planIndex)
        {
            if (airline != w.LocalPlayer || !At(w, TutorialStep.InHall)) return false;
            var p = hall.RandomRune(Hall.RuneId.Creation, 0, rand);
            for (int f = 0; f < 100; f++)
            {
                var pp = hall.RandomRune(Hall.RuneId.Creation, 0, rand);
                if (pp != null && (p == null || pp.Value.X < p.Value.X)) p = pp;
            }
            int koffer = rand.Rand(2);
            int clan = hall.CustomerId(0, koffer, rand);
            if (p != null && clan >= 0) hall.AddPersonToQueue(clan, p.Value, Hall.Reason.Flying, airline, planeId, planIndex, Hall.Mood.Ticket);
            Advance(w, TutorialStep.PassengersComing, true, null, 1602);
            return true;
        }

        /// <summary>A passenger of the local airline joined the check-in queue (1602 → 1603).</summary>
        public static void PassengerAtCheckIn(World w, int airline)
        {
            if (airline == w.LocalPlayer && w.Tutorial.Step == TutorialStep.PassengersComing) Advance(w, TutorialStep.CheckingIn, true, null, 1603);
        }

        /// <summary>A passenger of the local airline reached the waiting zone (1603 → 1604).</summary>
        public static void PassengerInWaitingZone(World w, int airline)
        {
            if (airline == w.LocalPlayer && At(w, TutorialStep.CheckingIn)) Advance(w, TutorialStep.Waiting, true, null, 1604);
        }

        /// <summary>A plane of the local airline stopped at its gate (1604 → 1605).</summary>
        public static void PlaneReachedGate(World w, int airline)
        {
            if (airline == w.LocalPlayer && At(w, TutorialStep.Waiting)) Advance(w, TutorialStep.PlaneAtGate, true, null, 1605);
        }

        /// <summary>A plane of the local airline taxies away from its gate with passengers (1605 → 2000, monologue).</summary>
        public static void PlaneLeftGate(World w, int airline)
        {
            if (airline != w.LocalPlayer || !At(w, TutorialStep.PlaneAtGate)) return;
            w.Tutorial.Monolog = true;
            Advance(w, TutorialStep.PlaneLeft, true, null, 1606, 2000);
        }
    }

    // ---------------------------------------------------------------------- commands (used by the view's script)

    /// <summary>
    /// One step of the tutorial script that needs the view's knowledge (the assistant fell silent, the player
    /// is in a certain room, clicked a certain thing): moves from <see cref="From"/> to <see cref="To"/>
    /// and lets the assistant say <see cref="Texts"/>. <see cref="End"/> ends the tutorial afterwards.
    /// </summary>
    public sealed class TutorialStepCommand : ICommand<World>
    {
        public readonly int From, To;
        public readonly bool SkipCurrent, End;
        /// <summary>null = unchanged.</summary>
        public readonly bool? Monolog;
        public readonly int[] Texts;
        public readonly object[] Args;

        public TutorialStepCommand(int from, int to, int[] texts, object[] args = null, bool skipCurrent = false, bool? monolog = null, bool end = false)
        {
            From = from; To = to; Texts = texts ?? Array.Empty<int>(); Args = args; SkipCurrent = skipCurrent; Monolog = monolog; End = end;
        }

        public CommandResult Validate(World w)
        {
            if (!w.Tutorial.Active) return CommandResult.Fail("tutorial.off");
            if (w.Tutorial.Step != From) return CommandResult.Fail("tutorial.step");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            if (Monolog.HasValue) w.Tutorial.Monolog = Monolog.Value;
            TutorialEngine.Advance(w, To, SkipCurrent, Args, Texts);
            if (End) w.Tutorial.Active = false;
        }
    }

    /// <summary>
    /// The player cancels the tutorial (rooms-and-ui-1 §9.5 tutorial button, Space): the tutorial ends; when
    /// that happens at 09:00 around the briefing (steps 1240/1300) the working day starts.
    /// </summary>
    public sealed class CancelTutorialCommand : ICommand<World>
    {
        public CommandResult Validate(World w) => w.Tutorial.Active ? CommandResult.Success : CommandResult.Fail("tutorial.off");

        public void Apply(World w)
        {
            var t = w.Tutorial;
            t.Monolog = false;
            t.Active = false;
            bool start = w.Clock.Hour == 9 && w.Clock.Minute == 0 && (t.Step == 1300 || t.Step == TutorialStep.Briefing)
                         && w.Clock.State == DayState.Morning;
            if (start) w.Execute(new Missions.StartWorkingDayCommand());
            w.Events.Publish(new TutorialCancelled(t.Step, start));
        }
    }

    // ---------------------------------------------------------------------- setup and night

    /// <summary>Mission 0 starts with the tutorial running (sim-core.md §2.1).</summary>
    public sealed class TutorialSetup : ISetupStep
    {
        public string Name => "tutorial";
        public int Order => 960;

        public void Apply(World w, NewGameSettings s)
        {
            w.Tutorial.Active = w.Difficulty == Mission.Tutorial;
            w.Tutorial.Step = TutorialStep.NotStarted;
            w.Tutorial.Monolog = false;
        }
    }

    /// <summary>Midnight step 8 (sim-core.md §2.6): the tutorial flag ends with the first day.</summary>
    public sealed class TutorialNightSystem : SimSystem
    {
        public override string Name => "tutorial.night";
        public override int Order => 800;

        public override void OnNewDay(World w)
        {
            w.Tutorial.Active = false;
            w.Tutorial.Monolog = false;
        }
    }

    public static class TutorialModule
    {
        public static void Register(World w)
        {
            w.SetupSteps.Add(new TutorialSetup());
            w.Systems.Add(new TutorialNightSystem());
        }
    }
}
