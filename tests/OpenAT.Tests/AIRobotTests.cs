using System.Linq;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Routes;
using Xunit;
using Xunit.Abstractions;

namespace OpenAT.Tests
{
    /// <summary>Computer players (ai-robots.md): data tables, planner rules, headless games with the install.</summary>
    public class AIRobotTests
    {
        readonly ITestOutputHelper _out;
        public AIRobotTests(ITestOutputHelper output) => _out = output;

        static GameInstall _install;
        static readonly object Lock = new object();

        static GameData LoadData(bool withHall = false)
        {
            lock (Lock)
            {
                _install = _install ?? GameInstall.TryLocate();
                if (_install == null) return null;
                var d = GameData.Load(_install);
                FinanceData.Load(d, _install);
                if (withHall)
                {
                    HallModule.Register();
                    HallContent.Attach(d, HallData.Load(_install));
                }
                return d;
            }
        }

        // ------------------------------------------------------------------ feature matrix (§8)

        [Fact]
        public void FeatureTable_RowsAndLevels()
        {
            var t = RobotFeatureTable.CreateOriginal();
            Assert.Equal(6, RobotFeatureTable.LevelOf(Mission.FreeGame));
            Assert.Equal(3, RobotFeatureTable.LevelOf(Mission.ConnectWorld));
            Assert.Equal(12, RobotFeatureTable.LevelOf(16));
            Assert.Equal(26, RobotFeatureTable.LevelOf(Mission.AtfsLast));
            // spot checks against the matrix
            Assert.False(t.Get(RobotFeature.Sabotage, 0));
            Assert.True(t.Get(RobotFeature.Sabotage, 6));
            Assert.True(t.Get(RobotFeature.SuggestRoutes, 3));
            Assert.True(t.Get(RobotFeature.ForceRoutes, RobotFeatureTable.LevelOf(16)));
            Assert.False(t.Get(RobotFeature.TravelHolding, RobotFeatureTable.LevelOf(16)));
            Assert.True(t.Get(RobotFeature.Nasa, 5));
            Assert.True(t.Get(RobotFeature.Nasa, RobotFeatureTable.LevelOf(20)));
            Assert.True(t.Get(RobotFeature.Tanks, RobotFeatureTable.LevelOf(49)));
            Assert.True(t.Get(RobotFeature.BonusX8, 6));
            // TRAVELHOLDING agrees with the operations domain's world flag
            for (int d = -1; d <= 50; d++)
            {
                int l = RobotFeatureTable.LevelOf(d);
                if (l < 0) continue;
                var w = new World(OpsWorld.Data()) { Difficulty = d };
                Assert.Equal(w.TravelHolding, t.Get(RobotFeature.TravelHolding, l));
            }
            // a mod can switch single cells
            t.Set(RobotFeature.Sabotage, 0, true);
            Assert.True(t.Get(RobotFeature.Sabotage, 0));
        }

        [Fact]
        public void DefaultPickTable_HasTheDocumentedWeights()
        {
            var t = new RobotTuning().DefaultPicks;
            Assert.Equal(31, t.Count);
            int W(RobotAction a) => t.Count(x => x == a);
            Assert.Equal(3, W(RobotAction.Security));
            Assert.Equal(2, W(RobotAction.Sabotage));
            Assert.Equal(2, W(RobotAction.Cargo));
            Assert.Equal(1, W(RobotAction.Nasa));
            Assert.Equal(2, W(RobotAction.Designer));
        }

        // ------------------------------------------------------------------ init & planner (§2, §4)

        static World SmallWorld(int difficulty = Mission.FreeGame)
        {
            var w = OpsWorld.NewWorld(difficulty);
            for (int c = 0; c < Player.Count; c++)
            {
                w.Players[c].Owner = Owner.Computer;
                w.Rules.Get<IRobotBrain>().NewGame(w, w.Players[c]);
                OpsWorld.AddPlane(w, w.Players[c]);
            }
            w.Clock.Time = GameClock.At(9, 30);
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            return w;
        }

        [Fact]
        public void NewGame_SympathyAndCooperation()
        {
            var w = SmallWorld();
            w.Players[3].Owner = Owner.Human;
            foreach (var p in w.Players) w.Rules.Get<IRobotBrain>().NewGame(w, p);
            Assert.Equal(60, w.Players[0].Sympathy[1]);
            Assert.Equal(50, w.Players[0].Sympathy[2]);
            Assert.Equal(60, w.Players[3].Sympathy[0]);
            Assert.True(w.Players[0].Cooperates[1]);
            Assert.False(w.Players[0].Cooperates[3]);
            Assert.False(w.Players[3].Cooperates[0]);
        }

        [Fact]
        public void MorningInit_OpeningSequence()
        {
            var w = SmallWorld();
            w.Clock.Date = 2; // slot 1: (1 + 2) odd → last minute, travel agency
            var p = w.Players[1];
            w.Rules.Get<IRobotBrain>().MorningInit(w, p);
            var q = p.Robot.Queue;
            Assert.Equal(RobotAction.StartDay, q[1]);
            Assert.Equal(RobotAction.Personnel, q[2]);
            Assert.Equal(RobotAction.LastMinute, q[3]);
            Assert.Equal(RobotAction.TravelAgency, q[4]);
            Assert.Equal(20, p.Robot.WorkCountdown);

            var p2 = w.Players[2]; // (2 + 2) even, (2+2)&3 == 0 and FRACHT → cargo as the second visit
            w.Rules.Get<IRobotBrain>().MorningInit(w, p2);
            Assert.Equal(RobotAction.Cargo, p2.Robot.Queue[2]);
            Assert.Equal(RobotAction.TravelAgency, p2.Robot.Queue[3]);
            Assert.Equal(RobotAction.LastMinute, p2.Robot.Queue[4]);
        }

        [Fact]
        public void Planner_PriorityChainAndOfficeQuirk()
        {
            var w = SmallWorld().WithOriginalAi();
            var p = w.Players[2];
            var s = p.Robot;
            int now = w.Clock.Time;
            RobotPlanner.Plan(w, p);
            Assert.Equal(RobotAction.Personnel, s.Queue[1]); // never visited today (§4.1 row 6)

            // Personnel, board and agents just visited: Q1 makes the office "always due".
            s.ClearQueue();
            s.TimePersonnel = s.TimeBoard = s.TimeAgents = now;
            s.TimeOffice = now - 1000;
            RobotPlanner.Plan(w, p);
            Assert.Equal(RobotAction.Office, s.Queue[1]);

            // With the fix the office was visited a moment ago → random pick, never a repeat of the last action.
            w.AI.Options.FixOfficeTimer = true;
            s.ClearQueue();
            s.LastAction = RobotAction.Bank;
            RobotPlanner.Plan(w, p);
            Assert.NotEqual(RobotAction.None, s.Queue[1]);

            Assert.NotEqual(RobotAction.Bank, s.Queue[1]);
        }

        [Fact]
        public void Planner_RandomPicksRespectFilters()
        {
            var w = SmallWorld(Mission.Tutorial); // no sabotage, no cargo, no ads, no route box
            var p = w.Players[3];
            var s = p.Robot;
            for (int i = 0; i < 400; i++)
            {
                s.ClearQueue();
                s.TimePersonnel = s.TimeBoard = s.TimeAgents = s.TimeOffice = w.Clock.Time;
                w.AI.Options.FixOfficeTimer = true;
                s.LastAction = RobotAction.Personnel;
                RobotPlanner.Plan(w, p);
                var a = s.Queue[1];
                Assert.NotEqual(RobotAction.Sabotage, a);
                Assert.NotEqual(RobotAction.Petrol, a);
                Assert.NotEqual(RobotAction.Cargo, a);
                Assert.NotEqual(RobotAction.Advertising, a);
                Assert.NotEqual(RobotAction.RouteBox, a);
                Assert.NotEqual(RobotAction.Nasa, a);
                Assert.NotEqual(RobotAction.Designer, a);
                Assert.NotEqual(RobotAction.Security, a);
                Assert.NotEqual(RobotAction.Personnel, a);
                w.Clock.Time += 30;
            }
        }

        [Fact]
        public void RoomBusy_RetriesTheBlockedActionAfterTheNextOne()
        {
            var w = SmallWorld();
            var p = w.Players[0];
            var s = p.Robot;
            s.ClearQueue();
            s.Queue[0] = RobotAction.Bank;
            s.Queue[1] = RobotAction.Kiosk;
            s.Queue[2] = RobotAction.Museum;
            w.Rules.Get<IRobotBrain>().RoomBusy(w, p, 100);
            Assert.Equal(new[] { RobotAction.None, RobotAction.Kiosk, RobotAction.Bank, RobotAction.Museum, RobotAction.None }, s.Queue);

            s.ClearQueue();
            s.Queue[0] = RobotAction.Bank;
            w.Rules.Get<IRobotBrain>().RoomBusy(w, p, 100);
            Assert.Equal(RobotAction.Office, s.Queue[1]);
            Assert.Equal(RobotAction.Bank, s.Queue[2]);
        }

        [Fact]
        public void Pump_WithoutHall_WalksExecutesAndWorks()
        {
            var w = SmallWorld();
            var p = w.Players[0];
            w.Rules.Get<IRobotBrain>().MorningInit(w, p);
            var brain = w.Rules.Get<IRobotBrain>();
            for (int i = 0; i < 2000; i++) { brain.Pump(w, p); w.Clock.Time += 30; }
            Assert.True(p.Robot.ActionCounts[(int)RobotAction.StartDay] == 1);
            Assert.True(p.Robot.ActionsExecuted >= 3, p.Robot.ToString());
        }

        [Fact]
        public void TicketPrices_SixTimesBaseCost()
        {
            var w = SmallWorld();
            var p = w.Players[0];
            int ri = w.RouteIndex(OpsWorld.C(1), OpsWorld.C(2));
            RouteOps.RentAtOffice(w, p, ri);
            RobotRoutePlanner.PriceLegs(w, p);
            var r = w.RouteTable[ri];
            int cost = w.FuelCost.Estimate(w, null, r.From, r.To, 800, 800) * 3 / 180;
            Assert.Equal(cost * 6 / 10 * 10, p.RentRoutes[ri].TicketPrice);
            Assert.Equal(p.RentRoutes[ri].TicketPrice * 2, p.RentRoutes[ri].TicketPriceFC);
        }

        [Fact]
        public void Mechanic_RoomTime_FixedByDefault_Q11()
        {
            foreach (bool fix in new[] { true, false })
            {
                var w = SmallWorld();
                w.AI.Options.FixMechanicVisitTime = fix;
                var p = w.Players[1];
                OpsWorld.AddPlane(w, p);
                p.Robot.Current = RobotAction.Mechanic;
                p.Robot.WorkCountdown = 0;
                RobotExecutor.Execute(w, p);
                if (fix) Assert.True(p.Robot.WorkCountdown > 0);
                else Assert.Equal(0, p.Robot.WorkCountdown);     // walks out at once
            }
        }

        [Fact]
        public void RouteLoss_QuirkDropsSharedLegAfterElevenNights()
        {
            var w = SmallWorld().WithOriginalAi();
            var a = w.Players[0];
            var b = w.Players[1];
            int ri = w.RouteIndex(OpsWorld.C(1), OpsWorld.C(2));
            RouteOps.RentAtOffice(w, a, ri);
            RouteOps.RentAtOffice(w, b, ri);
            a.RentRoutes[ri].LastFlown = 0; // a has flown it: competitors are counted (Q19)
            for (int n = 0; n < 10; n++) RobotRoutePlanner.PriceLegs(w, a);
            Assert.True(a.RentRoutes[ri].Licensed);
            RobotRoutePlanner.PriceLegs(w, a);
            Assert.False(a.RentRoutes[ri].Licensed);           // Q3: always "losing"
            Assert.Equal(1, b.RentRoutes[ri].Rank);           // b moved up
            Assert.True(a.RentRoutes[w.ReverseRouteIndex(ri)].Licensed); // only this direction is dropped
        }

        [Fact]
        public void Sabotage_ProtectionBits()
        {
            Assert.Equal(6, RobotExecutor.ProtectionBit(SabotageFamily.Plane, 2));
            Assert.Equal(7, RobotExecutor.ProtectionBit(SabotageFamily.Plane, 3));
            Assert.Equal(0, RobotExecutor.ProtectionBit(SabotageFamily.Office, 3));
            Assert.Equal(1, RobotExecutor.ProtectionBit(SabotageFamily.Office, 2));
            Assert.Equal(8, RobotExecutor.ProtectionBit(SabotageFamily.Special, 5));
            Assert.Equal(3, RobotExecutor.ProtectionBit(SabotageFamily.Special, 4));
        }

        [Fact]
        public void Sabotage_VirusFallsBackToCoffee_WhenNoLaptopIsCarried()
        {
            var w = ItemsWorld.Make();
            var v = w.Players[1];
            v.LaptopQuality = 2;                       // owns a laptop grade, but it is not carried (e.g. in repair)
            Assert.Equal(1, RobotExecutor.PersonalActFor(2, v));
            v.Items.Add(OpenAT.Sim.Items.ItemId.Laptop);
            Assert.Equal(2, RobotExecutor.PersonalActFor(2, v));
            v.LaptopQuality = 0;                       // carried counts, whatever the grade field says
            Assert.Equal(2, RobotExecutor.PersonalActFor(2, v));
            Assert.Equal(3, RobotExecutor.PersonalActFor(3, w.Players[2]));
        }

        // ------------------------------------------------------------------ headless games with the install

        HeadlessRunner Run(int mission, int days, int seed = 4711, bool hall = false)
        {
            var d = LoadData(hall);
            if (d == null) return null;
            var r = HeadlessRunner.NewGame(d, mission, seed);
            if (hall) Assert.NotNull(r.World.Hall);
            _startEquity = r.World.Players.Select(Equity).ToArray();
            r.RunDays(days);
            _out.WriteLine($"mission {mission}, seed {seed}, hall {hall}");
            _out.WriteLine(r.Table());
            foreach (var x in r.Winners) _out.WriteLine($"won by P{x.Player} on day {x.Date}");
            foreach (var x in r.Outs) _out.WriteLine($"P{x.Player} out on day {x.Date}: {x.Reason}");
            return r;
        }

        long[] _startEquity;

        /// <summary>Cash − credit + market value of the planes (robots turn cash into used planes).</summary>
        static long Equity(Player p) => p.Money - p.Credit + p.Planes.Items.Sum(pl => (long)pl.MarketValue);

        void AssertTypicalGame(HeadlessRunner r, int mission)
        {
            var w = r.World;
            Assert.Empty(r.Outs);
            int flights = 0, contracts = 0, richer = 0;
            for (int c = 0; c < Player.Count; c++)
            {
                var p = w.Players[c];
                int f = r.Days.Sum(d => d[c].Flights);
                flights += f;
                contracts += p.Robot.ActionCounts[(int)RobotAction.LastMinute] + p.Robot.ActionCounts[(int)RobotAction.TravelAgency];
                Assert.True(f > 20, $"P{c} flew only {f} flights");
                Assert.True(p.Robot.ActionsExecuted > r.Days.Count * 15, $"P{c} executed only {p.Robot.ActionsExecuted} actions");
                Assert.True(p.Money > Player.DebtGameOver, $"P{c} money {p.Money}");
                if (Equity(p) > _startEquity[c]) richer++;
                Assert.True(p.Planes.Count >= 2);
            }
            Assert.True(richer >= 3, "most robots should earn money: " + richer);
            Assert.True(contracts > 0);
            // The plans stay valid: sorted, inside the week.
            foreach (var p in w.Players)
                foreach (var pl in p.Planes.Items)
                {
                    var f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
                    for (int i = 1; i < f.Count; i++) Assert.True(f[i - 1].StartAbs <= f[i].StartAbs);
                    Assert.All(f, e => Assert.True(e.StartDate < w.Clock.Date + 7));
                }
        }

        [Fact]
        public void Headless_FreeGame_25Days()
        {
            var r = Run(Mission.FreeGame, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.FreeGame);
            // Robots buy used planes once they have more than 5 M (§4.1 row 1).
            Assert.True(r.World.Players.Sum(p => p.Planes.Count) > 8, "no used planes bought");
            // In the free game nobody suggests routes; route mode needs a neighbour or > 6 planes after day 30.
            Assert.All(r.World.Players, p => Assert.False(p.Robot.RouteMode));
        }


        // The campaign is numbered from 1 in the game: mission 1 = difficulty 0 (tutorial), mission 2 =
        // difficulty 1, mission 4 = difficulty 3 (the route mission), sim-core.md §3.3.

        [Fact]
        public void Headless_Mission1_Tutorial_25Days()
        {
            var r = Run(Mission.Tutorial, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.Tutorial);
            // TUTORIALLIMIT: orders taken per day stay below 2·day + 3 (+1 for odd slots) at the counters.
            foreach (var p in r.World.Players) Assert.Equal(0, p.Robot.ActionCounts[(int)RobotAction.Sabotage]);
        }

        [Fact]
        public void Headless_Mission2_25Days()
        {
            var r = Run(Mission.Passengers, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.Passengers);
        }

        [Fact]
        public void Headless_Mission4_RouteMission_25Days()
        {
            var r = Run(Mission.ConnectWorld, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.ConnectWorld);
            // SUGGESTROUTES: every robot "wants routes" from its first office visit and rents legs (route box),
            // but full route mode needs the local human to connect more cities than the robot's slot (none here).
            Assert.All(r.World.Players, p => Assert.True(p.Robot.WantsRoutes));
            Assert.Contains(r.Days, d => d.Any(x => x.RouteLegs > 0));
        }

        [Fact]
        public void Headless_Mission3_25Days()
        {
            var r = Run(Mission.Profit, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.Profit);
        }

        [Fact]
        public void Headless_Mission5_Hard_25Days()
        {
            var r = Run(Mission.Image, 25);
            if (r == null) return;
            AssertTypicalGame(r, Mission.Image);
            // Hard: money-based advertising raises some robot's image (§5.2 "Advertising").
            Assert.Contains(r.World.Players, p => p.Image > 0);
        }

        [Fact]
        public void Headless_AddOn6_ForcedRouteMode_FliesRoutes()
        {
            var r = Run(16, 12);
            if (r == null) return;
            var w = r.World;
            Assert.All(w.Players, p => Assert.True(p.Robot.RouteMode));
            foreach (var p in w.Players)
            {
                Assert.True(r.Days.Skip(2).Sum(d => d[p.Index].Flights) > 10, $"P{p.Index} route flights");
                // With the route wipe fixed (Q24) plans are rebuilt nightly, so only a robot owning a leg some
                // plane can reach still has route entries; one whose legs are all out of range waits for a bigger plane.
                bool flyable = false;
                for (int i = 0; i < w.RouteTable.Count; i++)
                    if (p.RentRoutes[i].Licensed && p.Planes.Items.Any(pl => w.Distance(w.RouteTable[i].From, w.RouteTable[i].To) <= pl.Range * 1000)) flyable = true;
                if (flyable) Assert.Contains(p.Planes.Items, pl => pl.Plan.Entries.Any(e => e.Kind == FlightKind.Route));
                else Assert.True(p.Robot.NeedsBiggerPlane > 0, $"P{p.Index} should want a bigger plane");
                Assert.Equal(0, p.Robot.ActionCounts[(int)RobotAction.LastMinute]);
            }
        }

        [Fact]
        public void Headless_WithHall_RobotsWalkAndWork()
        {
            var r = Run(Mission.FreeGame, 6, hall: true);
            if (r == null) return;
            foreach (var day in r.Days)
                foreach (var row in day)
                    Assert.True(row.Actions >= 10, $"P{row.Player} executed {row.Actions} actions in a day");
            Assert.All(r.World.Players, p => Assert.True(r.Days.Sum(d => d[p.Index].Flights) > 10));
        }

        [Fact]
        public void Headless_SameSeed_SameGame()
        {
            var a = Run(Mission.Image, 8, 777);
            if (a == null) return;
            var b = Run(Mission.Image, 8, 777);
            Assert.Equal(a.Table(), b.Table());
            for (int c = 0; c < Player.Count; c++) Assert.Equal(a.World.Players[c].Money, b.World.Players[c].Money);
            var h1 = Run(Mission.FreeGame, 3, 31, hall: true);
            var h2 = Run(Mission.FreeGame, 3, 31, hall: true);
            Assert.Equal(h1.Table(), h2.Table());
        }

        [Fact]
        public void Headless_HumanCallsItADay_RobotsKeepWorking()
        {
            var d = LoadData(withHall: true);
            if (d == null) return;
            var owners = new[] { Owner.Human, Owner.Computer, Owner.Computer, Owner.Computer };
            var r = HeadlessRunner.NewGame(d, Mission.FreeGame, 5, owners);
            var w = r.World;
            int afterCall = 0;
            for (int day = 0; day < 3; day++)
            {
                int start = w.Clock.Date;
                bool called = false;
                int before = 0;
                while (!(w.Clock.Date > start && w.Clock.State == DayState.Morning))
                {
                    if (w.Clock.State == DayState.Morning && w.Clock.Frozen) w.Execute(new OpenAT.Sim.Missions.StartWorkingDayCommand());
                    if (!called && w.Clock.Time >= GameClock.At(11))
                    {
                        Assert.True(w.Execute(new OpenAT.Sim.Missions.CallItADayCommand(0)).Ok);
                        called = true;
                        before = w.Players.Skip(1).Sum(p => p.Robot.ActionsExecuted);
                    }
                    w.Tick();
                    if (called && w.Clock.Time == GameClock.DayEnd) afterCall += w.Players.Skip(1).Sum(p => p.Robot.ActionsExecuted) - before;
                }
            }
            _out.WriteLine("robot actions after call-it-a-day: " + afterCall);
            // The pump is not gated by call-it-a-day (sim-core.md §2.3): robots work on until 18:00 (countdown walks).
            Assert.True(afterCall > 30, "robot actions after call-it-a-day: " + afterCall);
            Assert.Equal(0, w.Players[0].Robot.ActionsExecuted);
        }

        [Fact]
        public void Brain_CanBeReplaced()
        {
            var d = LoadData();
            if (d == null) return;
            var r = HeadlessRunner.NewGame(d, Mission.FreeGame, 1);
            r.World.Rules.Set<IRobotBrain>(new IdleBrain());
            r.RunDays(2);
            Assert.All(r.World.Players, p => Assert.Equal(0, p.Robot.ActionsExecuted));
        }

        sealed class IdleBrain : OriginalRobotBrain
        {
            public override void Pump(World w, Player p) { }
        }
    }
}
