using System.Linq;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using Xunit;

namespace OpenAT.Tests
{
    public class FleetTests
    {
        // ------------------------------------------------------------------ planes (planes-and-flights.md §3–4)

        [Fact]
        public void MarketValue_NewPlaneIs85PercentOfListPrice()
        {
            var w = OpsWorld.NewWorld();
            var pl = OpsWorld.AddPlane(w, w.Players[0]);
            Assert.Equal(2002, pl.BuildYear);
            Assert.Equal(34850000, pl.MarketValue);              // 41 000 000 · 100/10000 · 100 · 102/120
            Assert.Equal(31365000, PlaneMarket.SalePrice(pl));    // × 9/10
            pl.Sponsored = true;
            Assert.Equal(3485000, pl.MarketValue);
            Assert.Equal(126, pl.MaxPassengers);                  // 168 · 6/8
            Assert.Equal(21, pl.MaxPassengersFC);                 // 168 · 1/8
        }

        [Fact]
        public void PlaneNames_UniqueAcrossFleetsAndNeverEntryZero()
        {
            var w = OpsWorld.NewWorld();
            var names = Enumerable.Range(0, 7).Select(i => OpsWorld.AddPlane(w, w.Players[i % 4]).Name).ToList();
            Assert.Equal(7, names.Distinct().Count());
            Assert.DoesNotContain("zero", names);
            // tier 1 exhausted (7 names) → tier 2 (entry 0 never), then the fallback
            var n8 = OpsWorld.AddPlane(w, w.Players[0]).Name;
            var n9 = OpsWorld.AddPlane(w, w.Players[0]).Name;
            Assert.Equal(new[] { "Second", "Third" }, new[] { n8, n9 }.OrderBy(x => x).ToArray());
            Assert.Equal(PlaneMarket.FallbackName, OpsWorld.AddPlane(w, w.Players[0]).Name);
        }

        [Fact]
        public void PlaneNames_InUseCheckIsCaseSensitive()
        {
            var w = OpsWorld.NewWorld();
            var pl = OpsWorld.AddPlane(w, w.Players[1]);
            Assert.True(PlaneMarket.IsNameInUse(w, pl.Name));
            Assert.False(PlaneMarket.IsNameInUse(w, pl.Name.ToUpperInvariant()));
            Assert.False(PlaneMarket.IsNameInUse(w, pl.Name.ToLowerInvariant()));
        }

        [Fact]
        public void BuyCommand_ChecksDebtLimitAndQuantities()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Money = 1000000;
            Assert.Equal("too-expensive", w.Execute(new BuyPlaneCommand(0, 100, 1)).Reason);
            p.Money = 50000000;
            Assert.Equal("bad-quantity", w.Execute(new BuyPlaneCommand(0, 100, 4)).Reason);
            Assert.True(w.Execute(new BuyPlaneCommand(0, 100, 1)).Ok);
            Assert.Equal(50000000 - 41000000, p.Money);
            Assert.Equal(1, p.Planes.Count);
            // availability (§2.2): free game lets type 105 (FirstMission 0) exist from day 0
            Assert.True(PlaneMarket.IsAvailable(w, w.Data.PlaneTypeByCsvId(105)));
        }

        [Fact]
        public void UsedPlanes_DeterministicPerDateAndSlot()
        {
            var w = OpsWorld.NewWorld();
            var a = PlaneMarket.CreateUsedPlane(w, 1);
            var b = PlaneMarket.CreateUsedPlane(w, 1);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.BuildYear, b.BuildYear);
            Assert.Equal(a.Condition, b.Condition);
            Assert.InRange(a.Condition, 20, 100);
            // build year rule (§4.3): first year < 1990 → 1990 − Rand(1990 − first), else 1996 − Rand(…)
            var t = w.Data.PlaneTypes[a.TypeId];
            if (t.FirstYearBuilt < 1990) Assert.InRange(a.BuildYear, t.FirstYearBuilt + 1, 1990);
            else Assert.InRange(a.BuildYear, 1995, 1996);
            Assert.Equal(a.Condition, a.TargetCondition);
        }

        // ------------------------------------------------------------------ wear and maintenance (§7)

        [Fact]
        public void Wear_PerDeparture()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            var rule = new OriginalPlaneWear();
            Assert.Equal(100, rule.ApplyWear(w, p, pl, 1000000, 1));    // built 2002: (1)·13/15 = 0
            pl.BuildYear = 1970;
            Assert.Equal(97, rule.ApplyWear(w, p, pl, 1000000, 1));     // (1)·45/15 = 3
            Assert.Equal(91, rule.ApplyWear(w, p, pl, 8008035, 1));     // 8 008 035·10/40 040 174 = 2 → (1+2)·45/15 = 9
            Assert.Equal(91, rule.ApplyWear(w, p, pl, 1000000, 3));     // bad kerosene: 3 · 3
        }

        [Fact]
        public void Maintenance_EngineerModeAndRepairPricing()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.MechMode = 3;
            p.Money = 0;
            var pl = OpsWorld.AddPlane(w, p);
            pl.Condition = 50; pl.WorstCondition = 50; pl.TargetCondition = 100;
            Maintenance.NightlyUpkeep(w, p);
            Assert.Equal(68, pl.Condition);
            // (68−50)·10·1.0·(2100−2002)/100·(200−68)/100 = 232 (float, truncated) + 150 000/30
            Assert.Equal(-(232 + 5000), p.Money);

            p.Money = 0;
            pl.Condition = 50; pl.WorstCondition = 20;
            Maintenance.NightlyUpkeep(w, p);
            Assert.Equal(68, pl.Condition);
            Assert.Equal(48, pl.WorstCondition);
            // improvement 68 − (20+20) = 28 → 28 · 41 000 000 / 110 = 10 436 363
            Assert.Equal(-(10436363 + 232 + 5000), p.Money);

            // target below condition − 1: no work, no balance shift, fee still charged
            p.Money = 0;
            pl.Condition = 80; pl.TargetCondition = 70;
            Maintenance.NightlyUpkeep(w, p);
            Assert.Equal(80, pl.Condition);
            Assert.Equal(-5000, p.Money);
        }

        [Fact]
        public void Maintenance_RepairCostOverflow_FixedByDefault()
        {
            foreach (bool fix in new[] { true, false })
            {
                var w = OpsWorld.NewWorld();
                w.Options.FixRepairCostOverflow = fix;
                var p = w.Players[0];
                p.MechMode = 3;
                p.Money = 0;
                var pl = OpsWorld.AddPlane(w, p);
                pl.Price = 200000000;
                pl.Condition = 50; pl.WorstCondition = 33; pl.TargetCondition = 100;
                Maintenance.NightlyUpkeep(w, p);
                Assert.Equal(68, pl.Condition);
                // improvement 68 − (33 + 20) = 15: 15 · 200 000 000 exceeds 2^31
                long part1 = fix ? 15L * 200000000 / 110 : unchecked(15 * 200000000) / 110;
                Assert.Equal(-(part1 + 232 + 5000), p.Money);
                if (fix) Assert.True(p.Money < 0); else Assert.True(part1 < 0);   // the original's bill turns into income
            }
        }

        // ------------------------------------------------------------------ equipment (§6)

        [Fact]
        public void Equipment_ChangeCosts()
        {
            Assert.Equal(-1000000, Equip.ChangeCost(Equip.Safety, 1, 0, 168));
            Assert.Equal(168 * (3000 - 150), Equip.ChangeCost(Equip.Seats, 0, 2, 168));
            Assert.Equal(0, Equip.ChangeCost(Equip.Food, 0, 2, 168));
            Assert.Equal(8000000 - 750000, Equip.ChangeCost(Equip.Electronics, 1, 2, 168));
        }

        [Fact]
        public void Equipment_AppliedOnGroundAndRefusedOverMoney()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            p.Money = 100000;
            Assert.Equal("too-expensive", w.Execute(new SetEquipmentCommand(0, pl.Id, Equip.Electronics, 2)).Reason);
            p.Money = 1000000;
            Assert.True(w.Execute(new SetEquipmentCommand(0, pl.Id, Equip.Seats, 2)).Ok);
            p.InPlanePropRoom = true;
            FlightOps.ApplyRefits(w);
            Assert.Equal(0, pl.Equipment[Equip.Seats]);
            p.InPlanePropRoom = false;
            FlightOps.ApplyRefits(w);
            Assert.Equal(2, pl.Equipment[Equip.Seats]);
            Assert.Equal(1000000 - 478800, p.Money);
        }

        [Fact]
        public void FirstClassSplit_StepsTenPercent()
        {
            var w = OpsWorld.NewWorld();
            var pl = OpsWorld.AddPlane(w, w.Players[0]);
            Assert.True(ChangeFirstClassCommand.Compute(pl, true, out int eco, out int fc));
            Assert.Equal((102, 33), (eco, fc));    // 25 % → 30 % → 40 %: FC = 168·40/2/100
            Assert.True(ChangeFirstClassCommand.Compute(pl, false, out eco, out fc));
            Assert.Equal((136, 16), (eco, fc));
        }

        // ------------------------------------------------------------------ designer (§5)

        static PlaneDesign SmallDesign(PlaneDesignCatalog cat, bool symmetric = true)
        {
            var d = new PlaneDesign { Name = "Test" };
            foreach (var id in new[] { 100, 200, 300, 400, 600, 1000 }) d.AddById(cat, id);
            if (symmetric) d.AddById(cat, 1010);
            return d;
        }

        [Fact]
        public void Designer_DerivedStatistics()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            Assert.Equal(37, cat.Parts.Count);
            Assert.Equal(307, cat.Relations.Count);
            var s = SmallDesign(cat).Stats(cat);
            Assert.Equal(18600000, s.Cost);
            Assert.Equal(95, s.Seats);
            Assert.Equal(45500, s.Weight);
            Assert.Equal(15200, s.Power);
            Assert.Equal(414, s.Speed);       // (15200−6000)·730/44000+270 = 422 → cap 400: (2·422+400)/3
            Assert.Equal(2200, s.Burn);
            Assert.Equal(35000, s.Tank);      // 7 · wing weight
            Assert.Equal(6210, s.Range);      // 35000/2200 = 15 h · 414
            Assert.Equal(3, s.Pilots);
            Assert.Equal(4, s.Attendants);
            Assert.Equal(35, s.Noise);
            Assert.Equal(5, s.Maintenance);
            Assert.True(SmallDesign(cat).IsBuildable(cat));
            Assert.Equal(8300, SmallDesign(cat, false).Error(cat));
        }

        [Fact]
        public void Designer_PlacementBlocksSlots()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            var d = new PlaneDesign();
            int rel(int id) => cat.Relations.First(r => r.Id == id).Index;
            Assert.True(cat.CanAttach(d, rel(100)));
            Assert.False(cat.CanAttach(d, rel(200)));   // no body yet
            d.AddById(cat, 100);
            Assert.False(cat.CanAttach(d, rel(101)));   // second body: slot B* blocked
            Assert.True(cat.CanAttach(d, rel(400)));
            d.AddById(cat, 400);
            Assert.False(cat.CanAttach(d, rel(401)));   // R4 blocks Rx
        }

        [Fact]
        public void Designer_BuyCreatesPlaneFromStats()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Money = 100000000;
            Assert.True(w.Execute(new BuyDesignedPlaneCommand(0, SmallDesign(w.DesignCatalog), 1)).Ok);
            var pl = p.Planes.Items.Single();
            Assert.Equal(-1, pl.TypeId);
            Assert.Equal(414, pl.Speed);
            Assert.Equal(6210, pl.Range);
            Assert.Equal(35, pl.Noise);
            Assert.Equal(1.05f, pl.MaintenanceFactor, 3);
            Assert.Equal(95 * 6 / 8, pl.MaxPassengers);
            Assert.Equal(100000000 - 18600000, p.Money);
        }

        // ------------------------------------------------------------------ plans (§10)

        static Route RouteBetween(World w, int a, int b) => w.RouteTable.First(r => r.From == OpsWorld.C(a) && r.To == OpsWorld.C(b));

        [Fact]
        public void Plan_InsertsFerryAndPushesForward()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var bc = RouteBetween(w, 2, 3);
            RouteOps.Rent(w, p, bc.From, bc.To, bc.Rent);
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, bc.Id, 0, 12)).Ok);
            var f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
            Assert.Equal(2, f.Count);
            // §10.2 step 4: ferry Alpha → Bravo (3 h) from hour + 2 = 11, landing 14; step 6 pushes the
            // route to landing + 1 = 15 (a tight gap moves the real flight later)
            Assert.Equal(FlightKind.Auto, f[0].Kind);
            Assert.Equal((OpsWorld.C(1), OpsWorld.C(2)), (f[0].From, f[0].To));
            Assert.Equal((11, 14), (f[0].StartHour, f[0].LandHour));
            Assert.Equal((FlightKind.Route, 15, 18), (f[1].Kind, f[1].StartHour, f[1].LandHour));

            // a Charlie → Bravo flight at 14: ferry Alpha → Charlie (4 h) 11–15, then 16–19, Bravo → Charlie 20–23
            var cb = RouteBetween(w, 3, 2);
            RouteOps.Rent(w, p, cb.From, cb.To, cb.Rent);
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, cb.Id, 0, 14) { ClickHour = 22 }).Ok);
            f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
            Assert.Equal(3, f.Count);
            Assert.Equal((FlightKind.Auto, 11, 15), (f[0].Kind, f[0].StartHour, f[0].LandHour));
            Assert.Equal((cb.Id, 16, 19), (f[1].ObjectId, f[1].StartHour, f[1].LandHour));
            Assert.Equal((bc.Id, 20, 23), (f[2].ObjectId, f[2].StartHour, f[2].LandHour));
        }

        [Fact]
        public void Plan_LateFerry_WrapsToNextDay_FixedByDefault()
        {
            foreach (bool fix in new[] { true, false })
            {
                var w = OpsWorld.NewWorld();
                w.Options.FixFlightHourWrap = fix;
                w.Clock.Time = GameClock.At(22);
                var p = w.Players[0];
                OpsWorld.UsePlaneCrew(w);
                var pl = OpsWorld.AddPlane(w, p);
                var bc = RouteBetween(w, 2, 3);
                RouteOps.Rent(w, p, bc.From, bc.To, bc.Rent);
                Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, bc.Id, 1, 1)).Ok);
                var ferry = pl.Plan.Entries.First(e => e.Kind == FlightKind.Auto);
                // ferry from the start city at "now + 2" = 24:00; the route right after it leaves no room to move it later
                if (fix) Assert.Equal((1, 0), (ferry.StartDate, ferry.StartHour));
                else Assert.Equal((0, 24), (ferry.StartDate, ferry.StartHour));
            }
        }

        [Fact]
        public void Plan_StartTodayForcedTwoHoursAhead_AndHorizonCut()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 0, 9)).Ok);
            Assert.Equal(11, pl.Plan.Entries.First(e => e.Kind != 0).StartHour);
            Assert.Equal("outside-week", w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 7, 10)).Reason);
        }

        [Fact]
        public void Plan_RoutesRepeatWeekly_ContractsAreOneOff()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 0, 12));
            w.Clock.Date = 1;  // the night after day 0
            FlightPlanner.Rollover(w, p, pl);
            var f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
            var route = f.Single(e => e.Kind == FlightKind.Route);
            Assert.Equal((7, 12), (route.StartDate, route.StartHour));
            Assert.Equal(48, route.HoursBefore);
            Assert.Equal(OpsWorld.C(2), pl.Plan.StartCity);
            // the plan now starts in Bravo: ferry back to Alpha before day 7
            Assert.Equal(FlightKind.Auto, f[0].Kind);
        }

        [Fact]
        public void Plan_ExtendRepeatsThePattern()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            var ba = RouteBetween(w, 2, 1);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 0, 12));
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ba.Id, 0, 16));
            w.Execute(new ExtendPlanCommand(0, pl.Id));
            var f = pl.Plan.Entries.Where(e => e.Kind != 0).ToList();
            // pattern 12..19 → R = 19 − 12 + 1 = 8 h; repeated while it ends before day 7
            Assert.All(f, e => Assert.Equal(FlightKind.Route, e.Kind));
            Assert.Equal(20, f[2].StartHour);
            Assert.True(f.Count >= 38 && f.Count <= FlightPlan.Size, "count " + f.Count);
            Assert.True(f.Last().StartDate <= 6);
        }

        [Fact]
        public void CanBeSold_BlockedByBookedFlights()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            Assert.True(FlightPlanner.CanBeSold(w, pl));
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 1, 12));
            Assert.False(FlightPlanner.CanBeSold(w, pl));
            Assert.Equal("flights-planned", w.Execute(new SellPlaneCommand(0, pl.Id) { IgnoreOpeningHours = true }).Reason);
        }

        // ------------------------------------------------------------------ execution (§11–13)

        [Fact]
        public void Delay_CrewBelowRequirement()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Owner = Owner.Human;
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.StartWorkingDay();
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 0, 12));
            pl.CrewPilots = 1;
            int delays = 0, reason = 0;
            w.Events.Subscribe<FlightDelayed>(e => { delays++; reason = e.ReasonTextId; });
            OpsWorld.RunUntil(w, 0, 11, 1);
            Assert.Equal(1, delays);
            Assert.Equal(2350, reason);
            Assert.Equal(13, pl.Plan.Entries.First(e => e.Kind == FlightKind.Route).StartHour);
            // computer airlines are never delayed (sim-core.md §2.4 QUIRK)
            p.Owner = Owner.Computer;
            OpsWorld.RunUntil(w, 0, 12, 1);
            Assert.Equal(1, delays);
        }

        [Fact]
        public void Departure_BooksAndLandsAbroad()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[1];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            pl.Location = OpsWorld.C(2);
            pl.Plan.StartCity = OpsWorld.C(2);
            var bc = RouteBetween(w, 2, 3);
            RouteOps.Rent(w, p, bc.From, bc.To, bc.Rent);
            w.StartWorkingDay();
            w.Execute(new PlaceFlightCommand(1, pl.Id, FlightKind.Route, bc.Id, 0, 12));
            FlightBooked booked = null;
            w.Events.Subscribe<FlightBooked>(e => booked = e);
            long before = p.Money;
            OpsWorld.RunUntil(w, 0, 12, 31);
            Assert.NotNull(booked);
            Assert.Equal(PlaneLocation.Airborne, pl.Location);
            Assert.Equal(before + booked.Saldo, p.Money);
            Assert.True(booked.Passengers > 0);
            Assert.Equal(booked.Passengers * (long)booked.Entry.TicketPrice, booked.Income);
            OpsWorld.RunUntil(w, 0, 15, 16);
            Assert.Equal(OpsWorld.C(3), pl.Location);
            Assert.True(p.RentRoutes[bc.Index].CarriedToday > 0);
        }

        [Fact]
        public void HiddenBonus_PaidOutOnBooking()
        {
            HiddenBonus(false);
            HiddenBonus(true);
        }

        static void HiddenBonus(bool fix)
        {
            // §12.4 step 8: Saldo += min(Bonus, |Saldo|/10); original: Bonus −= min(Bonus, |new Saldo|/10),
            // fixed (default): Bonus −= the amount paid.
            var w = OpsWorld.NewWorld();
            w.Options.FixHiddenBonusDeduction = fix;
            var p = w.Players[2];
            p.Owner = Owner.Computer;
            p.Bonus = 1000000;
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.Execute(new PlaceFlightCommand(2, pl.Id, FlightKind.Route, ab.Id, 0, 12));
            int idx = System.Array.FindIndex(pl.Plan.Entries, e => e.Kind == FlightKind.Route);
            FlightBooked b = null;
            w.Events.Subscribe<FlightBooked>(e => b = e);
            FlightOps.BookFlight(w, p, pl, idx);
            long raw = b.Income - b.FuelCost - b.FoodCost;
            long bonus1 = System.Math.Abs(raw) / 10;
            Assert.Equal(raw + bonus1, b.Saldo);
            Assert.Equal(1000000 - (fix ? bonus1 : System.Math.Abs(raw + bonus1) / 10), p.Bonus);
        }

        [Fact]
        public void Satisfaction_GroupsOfFifteenCoverAllPassengers()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 0, 12));
            pl.Plan.UpdateNextFlight(0, 12);
            ref var e = ref pl.Plan.Entries[pl.Plan.NextFlight];
            e.Passengers = 47; e.PassengersFC = 5;
            PassengersEvaluated r = null;
            w.Events.Subscribe<PassengersEvaluated>(x => r = x);
            FlightOps.EvaluatePassengers(w, p, pl);
            Assert.Equal(52, r.Satisfied + r.Unsatisfied);
            pl.Condition = 0;   // Rand(70) > 0 almost always → unhappy
            FlightOps.EvaluatePassengers(w, p, pl);
            Assert.True(r.Unsatisfied >= 45);
        }
    }
}
