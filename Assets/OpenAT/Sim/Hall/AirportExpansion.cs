using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim
{
    public sealed partial class World
    {
        /// <summary>An airport expansion was bought today; it is built at midnight (sim-core.md §2.6 step 16).</summary>
        public bool ExpandAirportPending;
        /// <summary>Day of the last expansion (−100 = never).</summary>
        public int LastAirportExpansion = -100;
    }
}

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Buying more check-in and gate capacity from the airport boss (rooms-and-ui-6 §3.8, economy.md §15.4,
    /// airport-and-people.md §2.2 "Levels"): the request is decided in a fixed order, a yes costs 1 000 000
    /// (reason 3170), and the next midnight raises the check-in and waiting-zone segments by one level
    /// (the baggage belt follows the waiting zone) and rebuilds the hall layout.
    /// </summary>
    public static class AirportExpansion
    {
        public const long Price = 1000000;
        /// <summary>Segment indexes of the layout (airport-and-people.md §2.1).</summary>
        public const int CheckInSegment = 1, SuitcaseSegment = 7, WaitZoneSegment = 8;
        /// <summary>Boss line ids of the decision (group Boss).</summary>
        public const int FreeGatesText = 4110, MaximumText = 4111, OrderedText = 4112, OfferText = 4113, TooEarlyText = 4114, TooSoonText = 4115;

        /// <summary>Check-in level of the current hall (1 = start).</summary>
        public static int CheckInLevel(World w) => w.Hall?.Layout.Levels[CheckInSegment] ?? 1;

        /// <summary>A gate number of the current airport is rented by no active airline.</summary>
        public static bool FreeGates(World w)
        {
            int n = w.Rules.Has<IAirportGateCount>() ? w.Rules.Get<IAirportGateCount>().GateCount(w) : 4;
            for (int g = 0; g < n; g++)
            {
                bool taken = false;
                foreach (var p in w.Players) if (!p.IsOut && p.Gates.HasGate(g)) taken = true;
                if (!taken) return true;
            }
            return false;
        }

        /// <summary>The boss's answer to "I need more gates" (rooms-and-ui-3b §3, node 4011), in the original order.</summary>
        public static int Decide(World w)
        {
            if (FreeGates(w)) return FreeGatesText;
            int level = CheckInLevel(w);
            if (level >= 5 || (level >= 2 && w.Difficulty <= Mission.ConnectWorld && w.Difficulty != Mission.FreeGame)) return MaximumText;
            if (w.Clock.Date < 8) return TooEarlyText;
            if (w.Clock.Date - w.LastAirportExpansion < 3) return TooSoonText;
            if (w.ExpandAirportPending) return OrderedText;
            return OfferText;
        }

        /// <summary>Midnight: builds a pending expansion (one level more for check-in, waiting zone and belt).</summary>
        public static void Build(World w)
        {
            if (!w.ExpandAirportPending) return;
            w.ExpandAirportPending = false;
            var h = w.Hall;
            if (h == null) return;
            var levels = (int[])h.Layout.Levels.Clone();
            levels[CheckInSegment]++;
            levels[WaitZoneSegment]++;
            levels[SuitcaseSegment] = levels[WaitZoneSegment];
            w.LastAirportExpansion = w.Clock.Date;
            h.ResetLayout(new HallLayout(h.Data, h.Layout.Difficulty, levels));
            w.Events.Publish(new AirportExpanded(levels[CheckInSegment]));
        }
    }

    /// <summary>The player orders the expansion (boss node 4120): 1 000 000 now, built at midnight.</summary>
    public sealed class RequestAirportExpansionCommand : ICommand<World>
    {
        public readonly int Player;
        public RequestAirportExpansionCommand(int player) => Player = player;

        public CommandResult Validate(World w)
        {
            if (Player < 0 || Player >= OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("bad-player");
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            return AirportExpansion.Decide(w) == AirportExpansion.OfferText ? CommandResult.Success : CommandResult.Fail("expansion.refused");
        }

        public void Apply(World w)
        {
            w.ExpandAirportPending = true;
            w.Players[Player].Book(-AirportExpansion.Price, MoneyReason.AirportExpansion);
        }
    }

    /// <summary>The hall was rebuilt with more check-in counters and gates.</summary>
    public sealed class AirportExpanded : ISimEvent
    {
        public readonly int CheckInLevel;
        public AirportExpanded(int level) => CheckInLevel = level;
    }

    /// <summary>Midnight step 16 (sim-core.md §2.6): the airport expansion, after the date moved on (step 13).</summary>
    public sealed class AirportExpansionSystem : SimSystem
    {
        public const string SystemName = "hall.expansion";
        public override string Name => SystemName;
        public override int Order => 1600;
        public override void OnNewDay(World w) => AirportExpansion.Build(w);
    }
}
