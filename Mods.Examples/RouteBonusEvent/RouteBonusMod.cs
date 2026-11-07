using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Modding;
using OpenAT.Sim.Routes;

// Example code mod (docs/MODDING.md): "route bonus of the week".
// - a SimSystem (order 2150: at midnight, right before the route demand update "ops.route-demand" at 2200)
//   picks a random route every 7 days (days 1, 8, 15, ...) with the world's deterministic TeakRand,
// - a rule decorator lifts that route's demand to its boosted target for seven days,
// - the news is published as a ModNewsEvent (the view shows it as an advisor message) and as the mod's
//   own typed event RouteBonusStarted,
// - its state (current route, end date, history) lives in World.Extensions["mod.routebonusevent"],
//   so it is saved with the game,
// - a named command "routebonus.start" starts a bonus on demand (tests, debug console).
namespace OpenAT.Mods.RouteBonusEvent
{
    public sealed class RouteBonusMod : IOpenATMod
    {
        public const string ModId = "routebonusevent";
        public const string StateKey = "mod." + ModId;
        public const string PercentKey = "routeBonus.percent";

        public void OnLoad(ModContext ctx)
        {
            ctx.DefineTunable(PercentKey, 150, "Demand of the route of the week in percent of its normal target.", 100, 1000, integer: true);
            ctx.AddSystem(() => new RouteBonusSystem());
            ctx.DecorateRule<IRouteDemandRule>(inner => new BonusDemand(inner));
            ctx.RegisterCommand("routebonus.start", (w, args) => new StartRouteBonusCommand(args.Length > 0 ? int.Parse(args[0]) : -1));
            ctx.Log("route bonus of the week ready");
        }
    }

    /// <summary>Saved state of the mod (plain fields: the save system writes them by reflection).</summary>
    public sealed class RouteBonusState
    {
        /// <summary>Boosted route id, 0 = none.</summary>
        public int RouteId;
        /// <summary>First day without the bonus.</summary>
        public int UntilDate;
        /// <summary>Bonuses so far (also part of the random seed).</summary>
        public int Count;
        public List<int> History = new List<int>();

        public bool IsActive(World w) => RouteId != 0 && w.Clock.Date < UntilDate;
    }

    public sealed class RouteBonusStarted : ISimEvent
    {
        public readonly int RouteId, UntilDate;
        public RouteBonusStarted(int routeId, int until) { RouteId = routeId; UntilDate = until; }
    }

    public sealed class RouteBonusSystem : SimSystem
    {
        public const string SystemName = "mod.routebonus";
        public override string Name => SystemName;
        public override int Order => 2150;

        public override void OnNewDay(World w)
        {
            var s = ModState.Get<RouteBonusState>(w, RouteBonusMod.StateKey);
            int date = w.Clock.Date;
            if (s.RouteId != 0 && date >= s.UntilDate) s.RouteId = 0;
            if (date % 7 != 1 || w.RouteTable.Count == 0) return;
            var rand = new TeakRand(date * 7919 + s.Count);
            Start(w, s, w.RouteTable[rand.Rand(w.RouteTable.Count)]);
        }

        internal static void Start(World w, RouteBonusState s, Route route)
        {
            s.RouteId = route.Id;
            s.UntilDate = w.Clock.Date + 7;
            s.Count++;
            s.History.Add(route.Id);
            int pct = w.Tunables.Int(RouteBonusMod.PercentKey, 150);
            string from = w.Data.Cities.Find(route.From)?.Name ?? "?", to = w.Data.Cities.Find(route.To)?.Name ?? "?";
            w.Events.Publish(new RouteBonusStarted(route.Id, s.UntilDate));
            w.Events.Publish(new ModNewsEvent(RouteBonusMod.ModId, $"Travel boom! Demand from {from} to {to} is up {pct - 100}% this week."));
        }
    }

    /// <summary>Decorates the demand rule: the route of the week jumps to its boosted target.</summary>
    public sealed class BonusDemand : IRouteDemandRule
    {
        readonly IRouteDemandRule _inner;
        public BonusDemand(IRouteDemandRule inner) => _inner = inner;

        static bool Boosted(World w, Route r) =>
            w.Extensions.TryGetValue(RouteBonusMod.StateKey, out var v) && v is RouteBonusState s && s.RouteId == r.Id && s.IsActive(w);

        public int Potential(World w, Route r)
        {
            int p = _inner.Potential(w, r);
            return Boosted(w, r) ? (int)((long)p * w.Tunables.Int(RouteBonusMod.PercentKey, 150) / 100) : p;
        }

        public int NextDemand(World w, Route r)
        {
            int d = _inner.NextDemand(w, r);
            if (!Boosted(w, r)) return d;
            int target = (int)(Potential(w, r) * w.Tunables.Get(TunableKeys.RouteDemandFactor, 4.27));
            return Math.Max(d, target);
        }
    }

    /// <summary>Starts a bonus now (route index, −1 = random): an ordinary command, usable by UI, AI and tests.</summary>
    public sealed class StartRouteBonusCommand : ICommand<World>
    {
        readonly int _index;
        public StartRouteBonusCommand(int routeIndex) => _index = routeIndex;

        public CommandResult Validate(World w) =>
            w.RouteTable.Count == 0 ? CommandResult.Fail("no-routes")
            : _index >= w.RouteTable.Count ? CommandResult.Fail("bad-route") : CommandResult.Success;

        public void Apply(World w)
        {
            var s = ModState.Get<RouteBonusState>(w, RouteBonusMod.StateKey);
            int i = _index >= 0 ? _index : new TeakRand(w.Clock.Date * 31 + s.Count).Rand(w.RouteTable.Count);
            RouteBonusSystem.Start(w, s, w.RouteTable[i]);
        }
    }
}
