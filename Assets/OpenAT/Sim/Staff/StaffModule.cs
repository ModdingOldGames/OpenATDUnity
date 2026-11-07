using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Staff
{
    /// <summary>What a computer airline did that the informer reports (<c>Advi</c> 9001, 9002, 9004, 9005).</summary>
    public enum RivalMoveKind { OutbidGate, OutbidBranch, IssuedShares, BoughtShares }

    /// <summary>
    /// A move of one airline that affects another (published by the auction board and the computer players'
    /// share dealings): <see cref="Previous"/> is the outbid leader (auctions) or the airline whose shares were
    /// bought; <see cref="ObjectId"/> the gate number or city table index; <see cref="Count"/> the shares.
    /// </summary>
    public sealed class RivalMove : ISimEvent
    {
        public Player Rival;
        public RivalMoveKind Kind;
        public Player Previous;
        public int ObjectId;
        public long Count;
    }

    /// <summary>
    /// Staff domain wiring (economy.md §9, staff-and-advisors.md §9): walk speed every night, the random
    /// advisor chatter, the personnel advisor's remark after plane purchases and sales, and the informer's
    /// messages about computer airlines (<c>Advi</c> 9000..9006).
    /// </summary>
    public static class StaffModule
    {
        public static void Register(World w)
        {
            w.Systems.Add(new WalkSpeedSystem());
            w.Systems.Add(new AdvisorChatterSystem());
            w.Events.Subscribe<CommandExecuted>(e => AfterCommand(w, e.Command));
            w.Events.Subscribe<PlaneTraded>(e => OnPlaneTraded(w, e));
            w.Events.Subscribe<RouteLicenceChanged>(e => OnRouteChanged(w, e));
            w.Events.Subscribe<RivalMove>(e => OnRivalMove(w, e));
        }

        /// <summary>Buying or selling planes: the personnel advisor comments on the crew (tolerance 1).</summary>
        static void AfterCommand(World w, object cmd)
        {
            if (cmd is BuyPlaneCommand || cmd is BuyDesignedPlaneCommand || cmd is BuyUsedPlaneCommand || cmd is SellPlaneCommand)
            {
                int i = ((PlayerCommand)cmd).PlayerIndex;
                if (i >= 0 && i < Player.Count) w.Players[i].UpdatePersonnelAdvisor(1);
            }
        }

        /// <summary>The local airline's informer, or null if it has none.</summary>
        static Player Informed(World w)
        {
            var me = w.Local;
            return me != null && me.HasAdvisor(AdvisorType.Informer) ? me : null;
        }

        static void Tell(World w, Player me, int textId, params object[] args) =>
            w.Events.Publish(new AdvisorMessage(me, (int)AdvisorType.Informer, textId, args));

        /// <summary>A computer airline bought a used plane at the museum (9000: name, airline, price).</summary>
        static void OnPlaneTraded(World w, PlaneTraded e)
        {
            if (e.Player == null || !e.Player.IsComputer || e.Sold || e.Source != "museum") return;
            var me = Informed(w);
            if (me != null) Tell(w, me, 9000, e.Player.Name, e.Player.Airline, e.Price);
        }

        /// <summary>A computer airline gave up a losing route (9006: name, airline, from, to).</summary>
        static void OnRouteChanged(World w, RouteLicenceChanged e)
        {
            if (e.Player == null || e.Change != "robot-gave-up" || e.RouteIndex < 0 || e.RouteIndex >= w.RouteTable.Count) return;
            var me = Informed(w);
            if (me == null) return;
            var leg = w.RouteTable[e.RouteIndex];
            Tell(w, me, 9006, e.Player.Name, e.Player.Airline, w.City(leg.From).Name, w.City(leg.To).Name);
        }

        /// <summary>Outbid on a gate (9001) or branch (9002), shares issued (9004), buying your shares (9005).</summary>
        static void OnRivalMove(World w, RivalMove e)
        {
            var r = e.Rival;
            if (r == null || !r.IsComputer) return;
            var me = Informed(w);
            if (me == null) return;
            switch (e.Kind)
            {
                case RivalMoveKind.OutbidGate:
                    if (e.Previous == me) Tell(w, me, 9001, r.Name, r.Airline);
                    break;
                case RivalMoveKind.OutbidBranch:
                    if (e.Previous == me && e.ObjectId >= 0 && e.ObjectId < w.Data.Cities.Count)
                        Tell(w, me, 9002, r.Name, r.Airline, w.Data.Cities.All[e.ObjectId].Name);
                    break;
                case RivalMoveKind.IssuedShares:
                    Tell(w, me, 9004, r.Name, r.Airline, e.Count);
                    break;
                case RivalMoveKind.BoughtShares:
                    if (e.Previous == me) Tell(w, me, 9005, r.Name, r.Airline, e.Count);
                    break;
            }
        }
    }

    /// <summary>§2.7 step 16 (after salaries, original <c>UpdateWalkSpeed</c> in the airline's new day).</summary>
    public sealed class WalkSpeedSystem : SimSystem
    {
        public override string Name => "staff.walk-speed";
        public override int Order => 1650;
        public override void OnPlayerNewDay(World w, Player p) => p.UpdateWalkSpeed();
    }

    /// <summary>
    /// Random advisor chatter (original <c>RandomBeraterMessage</c>, staff-and-advisors.md §9.10): during the
    /// working day (09:00–18:00, not after calling it a day) every <see cref="AdvisorChatter.IntervalUnits"/>
    /// each airline in turn picks one of three advisor kinds at random; it speaks only if hired and its
    /// condition holds (<see cref="AdvisorChatter.Speak"/>).
    /// </summary>
    public sealed class AdvisorChatterSystem : SimSystem
    {
        public override string Name => "staff.advisor-chatter";
        public override int Order => 9000;
        public override void OnTick(World w)
        {
            if (!AdvisorChatter.IsDue(w)) return;
            var r = new TeakRand(w.Clock.Date + w.Clock.Time);
            foreach (var p in w.Players) AdvisorChatter.Speak(w, p, r.Rand(3));
        }
    }

    /// <summary>The three kinds of random advisor remarks (staff-and-advisors.md §9.1, §9.2, §9.7).</summary>
    public static class AdvisorChatter
    {
        /// <summary>
        /// Game time between two chances: 512 ticks (15 360 units, about 15 game minutes). The original tests
        /// <c>(time + steps left in the frame) mod 1024 == 0</c> in its main loop, which depends on the frame
        /// rate and game speed (at normal speed with a fast frame rate it practically never fires; sped up it
        /// fires about every 512 ticks). OpenAT uses that cadence on the game clock, so the chatter is
        /// deterministic and independent of the frame rate.
        /// </summary>
        public const int IntervalUnits = 512 * GameClock.UnitsPerTick;

        public static bool IsDue(World w)
        {
            var c = w.Clock;
            return c.State == DayState.Playing && !c.Frozen && !w.CallItADay
                && c.Time >= GameClock.DayStart && c.Time < GameClock.DayEnd && c.Time % IntervalUnits == 0;
        }

        /// <summary>One chance for one airline: <paramref name="which"/> 0 personnel, 1 kerosene, 2 plane analyst.</summary>
        public static void Speak(World w, Player p, int which)
        {
            if (which == 0 && p.HasAdvisor(AdvisorType.Personnel)) Personnel(w, p);
            else if (which == 1 && p.HasAdvisor(AdvisorType.Kerosene)) Kerosene(w, p);
            else if (which == 2 && p.HasAdvisor(AdvisorType.PlaneAnalyst)) Planes(w, p);
        }

        /// <summary>
        /// Unhappy staff (happiness &lt; −40): the last one in pool order not warned today is named
        /// (<c>Advi</c> 2200 + 10·sex, +1 pilot, +2 attendant); with more than 10 unhappy people one group
        /// warning (2299) instead, and all of them count as warned today.
        /// </summary>
        static void Personnel(World w, Player p)
        {
            var pool = w.Workers.Workers;
            Worker named = null;
            int n = 0;
            foreach (var wk in pool)
                if (wk.Employer == p.Index && wk.Happiness < -40)
                {
                    if (!wk.WarnedToday) named = wk;
                    n++;
                }
            if (named == null) return;
            int adv = (int)AdvisorType.Personnel;
            if (n > 10)
            {
                foreach (var wk in pool) if (wk.Employer == p.Index && wk.Happiness < -40) wk.WarnedToday = true;
                w.Events.Publish(new AdvisorMessage(p, adv, 2299));
                return;
            }
            named.WarnedToday = true;
            int id = 2200 + named.Sex * 10 + (named.IsPilot ? 1 : named.IsAttendant ? 2 : 0);
            w.Events.Publish(new AdvisorMessage(p, adv, id, named.Name));
        }

        /// <summary>
        /// Kerosene tips, first match: price &gt; 600, valve closed and fuel in the tank → open it (3011);
        /// price &lt; 400, tank more than half full, cash &gt; 20 000 and capacity &lt; 4 000 per plane → buy
        /// tanks (3012); price &lt; 470 after falling for three days, bottoming out yesterday and rising
        /// today, tank under a third full, cash &gt; 20 000 → buy fuel (3010).
        /// </summary>
        static void Kerosene(World w, Player p)
        {
            int k = w.Kerosene;
            var past = w.KerosenePast;
            int adv = (int)AdvisorType.Kerosene;
            int planes = w.OperationsBridge.Planes(w, p).Count;
            if (k > 600 && !p.TankOpen && p.TankContents > 0)
                w.Events.Publish(new AdvisorMessage(p, adv, 3011));
            else if (k < 400 && p.TankContents > p.Tank / 2 && p.Money > 20000 && p.Tank < planes * 4000)
                w.Events.Publish(new AdvisorMessage(p, adv, 3012));
            else if (k < 470 && past[9] > past[8] && past[7] > past[8] && past[6] > past[7] && past[5] > past[6]
                     && p.TankContents < p.Tank / 3 && p.Money > 20000)
                w.Events.Publish(new AdvisorMessage(p, adv, 3010));
        }

        /// <summary>
        /// Fleet wear warning, only with workshop modes 0–2: the planes below condition 30 (4010 one,
        /// 4011 several), or if none those below 55 (4020/4021); modes 0 and 1 append a hint about the
        /// workshop mode (4030/4031).
        /// </summary>
        static void Planes(World w, Player p)
        {
            int mode = p.MechMode;
            if (mode < 0 || mode > 2) return;
            var planes = w.OperationsBridge.Planes(w, p);
            foreach (int limit in new[] { 30, 55 })
            {
                var names = new List<string>();
                foreach (var pl in planes) if (pl.Condition < limit) names.Add(pl.Name);
                if (names.Count == 0) continue;
                int id = (limit == 30 ? 4010 : 4020) + (names.Count > 1 ? 1 : 0);
                var m = new AdvisorMessage(p, (int)AdvisorType.PlaneAnalyst, id, string.Join(", ", names));
                m.AppendTextId = mode == 0 ? 4030 : mode == 1 ? 4031 : 0;
                w.Events.Publish(m);
                return;
            }
        }
    }
}
