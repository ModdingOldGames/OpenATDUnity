using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Missions;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Registers the items domain (called from <c>World.InitItems</c>): systems, rules and event hooks.
    /// Orders follow the original sequences × 100 (sim-core.md §2.4, §2.6, §2.7, §2.8).
    /// </summary>
    public static class ItemsModule
    {
        public static void Register(World w)
        {
            w.Items.Catalog.Load(w.Data?.StdText);
            if (!w.Rules.Has<IRivalSympathy>()) w.Rules.Set<IRivalSympathy>(new LedgerSympathy());
            w.Rules.Set<IFlightSabotageState>(new ItemsFlightSabotageState());
            w.Rules.Set<AI.IRivalActions>(new ItemsRivalActions());
            foreach (var s in ItemSystems.Create()) w.Systems.Add(s);

            // Leaving the morning briefing executes the overnight pranks (sim-core.md §2.8 step 5).
            w.Events.Subscribe<CommandExecuted>(e => { if (e.Command is StartWorkingDayCommand) SabotageEngine.ExecuteMorningPranks(w); });
            // An airline leaving the game (bankrupt, taken over) loses its pending orders.
            w.Events.Subscribe<PlayerOut>(e => e.Player.Sabotage.ClearPending());
            w.Events.Subscribe<TakeoverExecuted>(e => e.Target.Sabotage.ClearPending());
        }
    }

    /// <summary>Sabotage aftermath read by the flights (planes-and-flights.md §12.3, §12.8).</summary>
    public sealed class ItemsFlightSabotageState : IFlightSabotageState
    {
        public bool PressStatement(World w, Player p) => p.PressStatement;
        public int BrochureTarget(World w, Player p) => p.BrochureFrom;
        public int SecurityFlags(World w, Player p) => p.SecurityFlags;
    }

    /// <summary>The items domain's time-driven systems.</summary>
    public static class ItemSystems
    {
        public static System.Collections.Generic.IEnumerable<ISimSystem> Create()
        {
            // §2.6 midnight (world)
            yield return new Step("items.daily-restore", 200, newDay: w => w.Items.RestoreDailyItems());
            yield return new Step("items.security-outage", 300, newDay: w => { if (w.Items.SecurityOutageDays > 0) w.Items.SecurityOutageDays--; });
            yield return new Step("items.slime-reset", 600, newDay: w => { if (w.Clock.Date % 6 == 0) w.Items.Slimed = -1; });
            yield return new Step("items.laptop-lock", 1000, newDay: w => w.Items.LaptopSoldTo = -1);
            yield return new Step("items.office-reload", 1200, newDay: w => w.Items.OfficePowerCutToday = false);
            yield return new Step("items.foreign-sabotage", 2900, newDay: w => { if (w.Difficulty == CrisisMission) SabotageEngine.ForeignSabotage(w); });

            // §2.7 per airline
            yield return new Step("items.daily-reset", 310, playerNewDay: DailyReset);
            yield return new Step("items.laptop-virus", 600, playerNewDay: (w, p) =>
            {
                if (p.LaptopVirus == 2) p.LaptopVirus = 3;
                if (p.IsComputer) p.LaptopVirus = 0;
            });
            yield return new Step("items.sabotage-night", 700, playerNewDay: SabotageEngine.NightlyReset);
            yield return new Step("items.laptop-battery", 1000, playerNewDay: (w, p) => p.LaptopBattery = ItemCatalog.LaptopBattery[Math.Max(0, Math.Min(4, p.LaptopQuality))]);

            // §2.4 minute / hour
            yield return new Step("items.office-blocks", 900, minute: OfficeBlocks);
            yield return new Step("items.plane-pranks", 1000, minute: w => { if (w.Clock.Minute == 0) SabotageEngine.HourlyPlanePranks(w); });
            yield return new Step("items.laptop-drain", 1100, minute: LaptopDrain);
            yield return new Step("items.impound-computers", 250, hour: ImpoundComputers);

            // per tick: the boss's condition, toilet runs, the bomb, leaving the briefing
            yield return new Step("items.tick", 400, tick: Tick);

            // §2.8 morning: report and fines before the boss's review (order 300)
            yield return new Step("items.sabotage-report", 250, morning: SabotageEngine.MorningReport);
        }

        /// <summary>Mission "crisis management" (DIFF_ATFS06).</summary>
        public const int CrisisMission = 46;

        /// <summary>§2.7 step 3: office repaired, stench, caffeine, mechanic anger reset; glowing coal cools down.</summary>
        static void DailyReset(World w, Player p)
        {
            p.OfficeState = 0;
            p.Stench = 0;
            p.Caffeine = 0;
            p.MechAngry = 0;
            while (p.Items.Replace(ItemId.GlowingCoal, ItemId.Coal)) { }
        }

        /// <summary>
        /// §2.4 minute step 9: on the hour between 8:00 and 18:00 (and at 9:00 on day 0), not after
        /// call-it-a-day, the hall's condition blocks 20..23 close the way to a bombed office.
        /// </summary>
        static void OfficeBlocks(World w)
        {
            var c = w.Clock;
            if (w.Hall == null || c.Minute != 0 || w.CallItADay) return;
            if (!((c.Hour >= 8 && c.Hour <= 18) || (c.Date == 0 && c.Hour == 9))) return;
            for (int i = 0; i < Player.Count; i++) w.Hall.SetConditionBlock(20 + i, w.Players[i].OfficeState == 2);
        }

        static void LaptopDrain(World w)
        {
            if (w.Clock.State != DayState.Playing) return;
            foreach (var p in w.Players)
            {
                if (p.IsOut || !p.LaptopOpen || p.LaptopBattery <= 0) continue;
                p.LaptopBattery--;
                if (p.LaptopBattery == 2 || p.LaptopBattery == 0) w.Events.Publish(new LaptopBatteryLow(p, p.LaptopBattery));
            }
        }

        /// <summary>
        /// <see cref="GameOptions.FixImpoundComputerPlanes"/>: the impound prank also holds computer airlines'
        /// departures due next hour (the original's delay check skips them, sim-core.md §2.4 hour step 3).
        /// </summary>
        static void ImpoundComputers(World w)
        {
            if (!w.Options.FixImpoundComputerPlanes) return;
            int date = w.Clock.Date, hour = w.Clock.Hour;
            foreach (var p in w.Players)
            {
                if (p.IsOut || !p.IsComputer) continue;
                foreach (var plane in p.Planes.ToList())
                {
                    if (plane.PseudoProblem <= 0) continue;
                    var f = plane.Plan.Entries;
                    for (int e = FlightPlan.Size - 1; e >= 0; e--)
                    {
                        if (f[e].Kind == 0 || f[e].StartDate * 24 + f[e].StartHour - 1 != date * 24 + hour) continue;
                        f[e].StartHour++;
                        if (f[e].StartHour >= 24) { f[e].StartHour -= 24; f[e].StartDate++; }
                        w.Events.Publish(new FlightDelayed { Player = p, Plane = plane, EntryIndex = e, ReasonTextId = 2358 });
                        plane.Plan.UpdatePointers(date, hour);
                        FlightPlanner.Check(w, p, plane);
                        Contracts.ContractUsage.UpdateContracts(w, p);
                    }
                }
            }
        }

        static void Tick(World w)
        {
            var c = w.Clock;
            // Leaving the briefing by any path (the command, or the view starting the clock directly).
            if (c.State == DayState.Playing && !c.Frozen && w.Items.MorningExecutedDate != c.Date) SabotageEngine.ExecuteMorningPranks(w);

            bool officeHours = c.Time >= GameClock.DayStart && c.Time < 18 * 60000;
            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                if (p.Drunk > 0) p.Drunk--;
                if (p.Stench > 0) p.Stench--;
                if (p.Caffeine > 0 && officeHours && !c.Frozen) p.Caffeine--;
                var h = w.Hall;
                if (h != null)
                {
                    var hp = h.Players[p.Index];
                    hp.Koffein = p.Caffeine;
                    // The bomb victim reached his office door.
                    if (p.OfficeState == 1 && hp.Room == ItemRooms.Office(p.Index)) Explode(w, p);
                }
                if (p.Sick) SickStep(w, p);
            }
        }

        /// <summary>
        /// Coffee prank (§1.3): 09:00–18:00, every 127th tick (not at minute 0), a 1-in-4 roll with the
        /// player's extra generator seeded by the time slice sends the boss running to the toilet (player 0
        /// uses the ladies' room).
        /// </summary>
        static void SickStep(World w, Player p)
        {
            var c = w.Clock;
            if (c.Frozen || w.CallItADay || c.TimeSlice % 127 != 0 || c.Minute == 0 || c.Hour < 9 || c.Hour >= 18) return;
            var h = w.Hall;
            if (h != null)
            {
                var hp = h.Players[p.Index];
                if (hp.RunningToToilet || hp.IsStuck > 0) return;
            }
            p.ExtraRandom.SetSeed(c.TimeSlice);
            if (p.ExtraRandom.Rand(4) != 0) return;
            int room = p.Index == 0 ? ItemRooms.WcF : ItemRooms.WcM;
            if (h != null)
            {
                var hp = h.Players[p.Index];
                if (hp.Room == ItemRooms.WcF || hp.Room == ItemRooms.WcM) return;
                // The original leaves every room for the run ("LeaveAllRooms") and stuns the boss for 20 ticks.
                // A computer is taken out of its room here; a human's room view is left by the view, so the run
                // only starts once the human's figure is in the hall (otherwise the walk could never begin and
                // the "running to the toilet" state would never end).
                if (!hp.InHall)
                {
                    if (!p.IsComputer) return;
                    OpenAT.Sim.AI.OriginalRobotBrain.LeaveRoom(w, p);
                }
                h.WalkToRoom(p.Index, room);
                var me = h.PlayerPerson(p.Index);
                if (me != null && me.StatePar == 0)
                {
                    if (p.IsComputer) p.Robot.Stun(20);
                    me.Running = true;
                    hp.RunningToToilet = true;
                    me.Mood = Hall.Mood.Toilet;
                    me.MoodCountdown = Math.Max(Hall.Mood.CountStart - 16, me.MoodCountdown);
                }
            }
            w.Events.Publish(new ToiletRun(p, room));
        }

        /// <summary>The office bomb goes off (state 1 → 2).</summary>
        public static void Explode(World w, Player p)
        {
            if (p.OfficeState != 1) return;
            p.OfficeState = 2;
            w.Events.Publish(new OfficeExploded(p));
        }

        /// <summary>Sets a rocket/station bit and pays (3400). Robots pass their own price (<see cref="SpaceParts.RobotPrice"/>).</summary>
        public static void AddSpacePart(World w, Player p, int bit, long price)
        {
            p.Counters.RocketFlags |= 1 << bit;
            p.Book(-price, MoneyReason.RocketPart);
            w.Events.Publish(new SpacePartBought(p, bit, price));
        }

        /// <summary>
        /// A robot's NASA visit (§4.1): the lowest missing bit, bought when its price (rocket table indexed by
        /// bit in the original, see FixRobotRocketPrices) is below the robot's money. Returns the bit bought or −1.
        /// </summary>
        public static int RobotBuySpacePart(World w, Player p)
        {
            int bit = SpaceParts.LowestMissing(p.Counters.RocketFlags);
            if (bit < 0) return -1;
            long price = SpaceParts.RobotPrice(w.Difficulty, bit, !w.Options.FixRobotRocketPrices);
            if (price >= p.Money) return -1;
            AddSpacePart(w, p, bit, price);
            return bit;
        }

        /// <summary>
        /// A robot's security visit (ACTION_VISITSECURITY, §2.4): two passes; rich (money &gt; 1 000 000) →
        /// switch on a random bit 0..8 if it is in the needed mask; otherwise switch a random bit 0..8 off.
        /// </summary>
        public static void RobotVisitSecurity(World w, Player p, TeakRand rand)
        {
            for (int pass = 1; pass <= 2; pass++)
            {
                if (p.Money > 1000000)
                {
                    int bit = 1 << rand.Rand(9);
                    if ((p.SecurityNeeded & bit) != 0) p.SecurityFlags |= bit;
                }
                else p.SecurityFlags &= ~(1 << rand.Rand(9));
            }
            w.Events.Publish(new SecurityChanged(p, p.SecurityFlags));
        }

        /// <summary>A system from delegates.</summary>
        sealed class Step : SimSystem
        {
            readonly string _name;
            readonly int _order;
            readonly Action<World> _tick, _minute, _hour, _newDay, _morning;
            readonly Action<World, Player> _playerNewDay;

            public Step(string name, int order, Action<World> tick = null, Action<World> minute = null, Action<World> hour = null,
                        Action<World> newDay = null, Action<World, Player> playerNewDay = null, Action<World> morning = null)
            { _name = name; _order = order; _tick = tick; _minute = minute; _hour = hour; _newDay = newDay; _playerNewDay = playerNewDay; _morning = morning; }

            public override string Name => _name;
            public override int Order => _order;
            public override void OnTick(World w) => _tick?.Invoke(w);
            public override void OnMinute(World w) => _minute?.Invoke(w);
            public override void OnHour(World w) => _hour?.Invoke(w);
            public override void OnNewDay(World w) => _newDay?.Invoke(w);
            public override void OnPlayerNewDay(World w, Player p) => _playerNewDay?.Invoke(w, p);
            public override void OnMorning(World w) => _morning?.Invoke(w);
        }
    }
}
