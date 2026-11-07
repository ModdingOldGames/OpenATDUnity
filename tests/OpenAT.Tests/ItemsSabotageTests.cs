using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Synthetic world for the items domain: operations data, player 0 human, money set.</summary>
    static class ItemsWorld
    {
        public static World Make(int difficulty = Mission.FreeGame, GameOptions options = null)
        {
            var w = OpsWorld.NewWorld(difficulty);
            if (options != null)
            {
                var o = new World(OpsWorld.Data(), options);
                o.Difficulty = difficulty;
                o.HomeCity = OpsWorld.C(1);
                o.Clock.InitCalendar(0);
                o.Clock.Time = GameClock.DayStart;
                o.EnsureOperationsArrays();
                OpenAT.Sim.Routes.RouteOps.UpdateDemand(o);
                w = o;
            }
            for (int c = 0; c < Player.Count; c++)
            {
                var p = w.Players[c];
                p.Owner = c == 0 ? Owner.Human : Owner.Computer;
                p.Airline = "Air" + c;
                p.Money = 100000000;
            }
            w.LocalPlayer = 0;
            return w;
        }

        public static CommandResult Order(World w, int saboteur, int victim, PrankFamily f, int mode, int target = -1) =>
            w.Execute(new OrderPrankCommand(saboteur, victim, f, mode, target));
    }

    public class ItemsSabotageTests
    {
        // ------------------------------------------------------------------ §1.2 access / trust

        [Fact]
        public void Order_NeedsTrustFromTheMachineGun()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            var r = ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1);
            Assert.Equal("sabo-1021", r.Reason);
            // §1.2: the MG used at the Arab Air counter is the only way to trust 1.
            w.Clock.Date = 1;
            w.Clock.Frozen = false;
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.MachineGun)).Ok);
            Assert.Equal(100000000 - 80000, p.Money);
            var use = new UseItemCommand(0, p.Items.IndexOf(ItemId.MachineGun), new ItemUseContext { Room = ItemRooms.ArabAir, HasPartner = true });
            Assert.True(w.Execute(use).Ok);
            Assert.Equal(1, use.Result.DialogPar);
            Assert.Equal(1, p.Sabotage.Trust);
            Assert.False(p.HasItem(ItemId.MachineGun));
            // trust 1: only option 1 of each family
            Assert.Equal("not-offered", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 2).Reason);
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1).Ok);
        }

        [Fact]
        public void Prices_AndTrustGrowth_Spec1_3()
        {
            long[] plane = { 1000, 5000, 10000, 50000, 100000 };
            long[] person = { 10000, 25000, 50000, 250000 };
            long[] special = { 100000, 500000, 1000000, 2000000, 5000000, 8000000 };
            for (int m = 1; m <= 5; m++) Assert.Equal(plane[m - 1], SabotageTables.Price(PrankFamily.Plane, m));
            for (int m = 1; m <= 4; m++) Assert.Equal(person[m - 1], SabotageTables.Price(PrankFamily.Person, m));
            for (int m = 1; m <= 6; m++) Assert.Equal(special[m - 1], SabotageTables.Price(PrankFamily.Special, m));

            var w = ItemsWorld.Make(options: OriginalOptions.Game(o => o.RandomStartDay = false));
            var s = w.Players[0];
            var victim = w.Players[1];
            var pl = OpsWorld.AddPlane(w, victim);
            s.Sabotage.Trust = 1;
            long before = s.Money;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok);
            Assert.Equal(before - 1000, s.Money);
            Assert.Equal(2, s.Sabotage.Trust);   // plane: +1
            Assert.Equal("sabo-1100", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1).Reason); // only one pending
            s.Sabotage.ClearPending();

            Assert.True(ItemsWorld.Order(w, 0, 2, PrankFamily.Person, 2 - 1).Ok);   // person 1 → max(2, 2)
            Assert.Equal(2, s.Sabotage.Trust);
            s.Sabotage.ClearPending();
            Assert.True(ItemsWorld.Order(w, 0, 2, PrankFamily.Special, 2).Ok);      // special 2 → 3
            Assert.Equal(3, s.Sabotage.Trust);
            s.Sabotage.ClearPending();
            s.Sabotage.Trust = 4;
            Assert.True(ItemsWorld.Order(w, 0, 2, PrankFamily.Person, 4).Ok);       // person 4 → 5
            Assert.Equal(5, s.Sabotage.Trust);
            s.Sabotage.ClearPending();
            for (int i = 0; i < 3; i++) { Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok); s.Sabotage.ClearPending(); }
            Assert.Equal(5, s.Sabotage.Trust);   // plane cap 5

            // QUIRK: special 5 gives no trust; special 6 (needs 6) is never offered.
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 5, pl.Id).Ok);
            Assert.Equal(5, s.Sabotage.Trust);
            s.Sabotage.ClearPending();
            Assert.Equal("not-offered", ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 6, 0).Reason);
            Assert.Equal(5, SabotageEngine.OfferedOptions(w, s, PrankFamily.Special));
        }

        [Fact]
        public void RouteTheft_OptionMakesItReachable()
        {
            var w = ItemsWorld.Make(options: new GameOptions { RandomStartDay = false, RouteTheftReachable = true });
            var s = w.Players[0];
            s.Sabotage.Trust = 5;
            Assert.Equal(6, SabotageEngine.OfferedOptions(w, s, PrankFamily.Special));
        }

        [Fact]
        public void SpecialPrank5_Fixed_GivesTrust6_AndMarksNeeded()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = OpsWorld.AddPlane(w, v);
            s.Sabotage.Trust = 5;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 5, pl.Id).Ok);
            Assert.Equal(6, s.Sabotage.Trust);
            Assert.NotEqual(0, v.SecurityNeeded & (1 << SabotageTables.BlockingBit(PrankFamily.Special, 5)));
            s.Sabotage.ClearPending();
            Assert.Equal(6, SabotageEngine.OfferedOptions(w, s, PrankFamily.Special));   // route theft reachable
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok);
            Assert.Equal(6, s.Sabotage.Trust);   // a plane prank does not lower it back to 5

            // Original: no trust, no needed bit.
            var o = ItemsWorld.Make(options: OriginalOptions.Game(x => x.RandomStartDay = false));
            var os = o.Players[0];
            var ov = o.Players[1];
            var opl = OpsWorld.AddPlane(o, ov);
            os.Sabotage.Trust = 5;
            Assert.True(ItemsWorld.Order(o, 0, 1, PrankFamily.Special, 5, opl.Id).Ok);
            Assert.Equal(5, os.Sabotage.Trust);
            Assert.Equal(0, ov.SecurityNeeded & (1 << SabotageTables.BlockingBit(PrankFamily.Special, 5)));
        }

        [Fact]
        public void Affordability_DebtLimit_AndSpecial56Unchecked()
        {
            var w = ItemsWorld.Make(options: OriginalOptions.Game(o => o.RandomStartDay = false));
            var s = w.Players[0];
            var pl = OpsWorld.AddPlane(w, w.Players[1]);
            s.Sabotage.Trust = 5;
            s.Money = -1000000 + 49999;
            Assert.Equal("sabo-6000", ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 4, pl.Id).Reason);
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 3, pl.Id).Ok);
            s.Sabotage.ClearPending();
            // QUIRK: the impound (5 000 000) is charged without a debt check.
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 5, pl.Id).Ok);
            Assert.True(s.Money < Player.DebtLimit);
        }

        [Fact]
        public void LaptopVirus_NeedsVictimLaptop()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            s.Sabotage.Trust = 2;
            Assert.Equal("sabo-1300", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 2).Reason);
            Assert.Equal(100000000, s.Money);
        }

        // ------------------------------------------------------------------ §2.2 security blocking

        [Fact]
        public void Security_BlocksMatchingPranks_WithQuirkMapping()
        {
            Assert.Equal(SecurityBits.PlaneInterior, SabotageTables.BlockingBit(PrankFamily.Plane, 1));
            Assert.Equal(SecurityBits.PlaneInterior, SabotageTables.BlockingBit(PrankFamily.Plane, 2));
            Assert.Equal(SecurityBits.PlaneExterior, SabotageTables.BlockingBit(PrankFamily.Plane, 3));
            Assert.Equal(SecurityBits.PlaneExterior, SabotageTables.BlockingBit(PrankFamily.Plane, 4));
            Assert.Equal(SecurityBits.PlaneInterior, SabotageTables.BlockingBit(PrankFamily.Plane, 5));
            Assert.Equal(SecurityBits.Office, SabotageTables.BlockingBit(PrankFamily.Person, 1));
            Assert.Equal(SecurityBits.Laptop, SabotageTables.BlockingBit(PrankFamily.Person, 2));
            Assert.Equal(SecurityBits.Office, SabotageTables.BlockingBit(PrankFamily.Person, 3));
            Assert.Equal(SecurityBits.Personnel, SabotageTables.BlockingBit(PrankFamily.Person, 4));
            Assert.Equal(SecurityBits.Gate, SabotageTables.BlockingBit(PrankFamily.Special, 1));        // QUIRK: brochures ← gate
            Assert.Equal(SecurityBits.Administration, SabotageTables.BlockingBit(PrankFamily.Special, 2));
            Assert.Equal(SecurityBits.Administration, SabotageTables.BlockingBit(PrankFamily.Special, 3));
            Assert.Equal(SecurityBits.Bank, SabotageTables.BlockingBit(PrankFamily.Special, 4));
            Assert.Equal(SecurityBits.Gate, SabotageTables.BlockingBit(PrankFamily.Special, 5));
            Assert.Equal(SecurityBits.Administration, SabotageTables.BlockingBit(PrankFamily.Special, 6)); // QUIRK: route theft ← administration

            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            s.Sabotage.Trust = 5;
            // Route board protection blocks nothing.
            v.SecurityFlags = 1 << SecurityBits.RouteBoard;
            for (int m = 1; m <= 4; m++) { Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, m).Ok); s.Sabotage.ClearPending(); }
            v.SecurityFlags = 1 << SecurityBits.Office;
            long before = s.Money;
            Assert.Equal("sabo-2090", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1).Reason);
            Assert.Equal("sabo-2090", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 3).Reason);
            Assert.Equal(before, s.Money);   // nothing charged
            v.SecurityFlags = 1 << SecurityBits.Gate;
            Assert.Equal("sabo-2098", ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 1).Reason);
            v.SecurityFlags = 1 << SecurityBits.Bank;
            Assert.Equal("sabo-2093", ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 4).Reason);
            v.SecurityFlags = 1 << SecurityBits.Personnel;
            Assert.Equal("sabo-2092", ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 4).Reason);
            // Gate levels 2/3 block nothing extra.
            v.SecurityFlags = (1 << SecurityBits.GateLevel2) | (1 << SecurityBits.GateLevel3);
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 1).Ok);
            // §2.4: the order set the "needed" bit.
            Assert.True((v.SecurityNeeded & (1 << SecurityBits.Gate)) != 0);
        }

        [Fact]
        public void Security_CheckedOnlyWhenOrdering()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            s.Sabotage.Trust = 1;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1).Ok);
            v.SecurityFlags = 1 << SecurityBits.Office;  // bought afterwards: no effect (§2.3)
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.True(v.Sick);
        }

        // ------------------------------------------------------------------ §1.3 plane pranks

        static Plane GroundPlaneWithFlight(World w, Player v, int startHour)
        {
            var pl = OpsWorld.AddPlane(w, v);
            pl.Location = OpsWorld.C(1);
            pl.Plan.Entries[0] = new FlightEntry
            {
                Kind = FlightKind.Auto, ObjectId = -1, From = OpsWorld.C(1), To = OpsWorld.C(2), Gate = GateCode.None,
                StartDate = w.Clock.Date, StartHour = startHour, LandDate = w.Clock.Date, LandHour = startHour + 3,
            };
            pl.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            return pl;
        }

        [Fact]
        public void PlanePrank_ArmsOnGround_FiresInTheAir()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = GroundPlaneWithFlight(w, v, 12);
            s.Sabotage.Trust = 2;
            v.Image = 100;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 2, pl.Id).Ok);
            long vMoney = v.Money;
            double price = v.SharePrice;
            int sym = w.Rules.Get<IRivalSympathy>().Get(w, 1, 0);
            SabotageEngine.HourlyPlanePranks(w);
            Assert.True(s.Sabotage.PlaneArmed);
            Assert.Equal(2, s.Sabotage.PlaneMode);       // on the ground: not yet
            pl.Location = PlaneLocation.Airborne;
            SabotageEngine.HourlyPlanePranks(w);
            Assert.Equal(0, s.Sabotage.PlaneMode);
            Assert.Equal(vMoney - 2000, v.Money);
            Assert.Equal(price * 0.9, v.SharePrice, 6);
            Assert.Equal(98, v.Image);
            Assert.Equal(4, s.Sabotage.Hints);
            Assert.Equal(1, v.Statistics.Today(StatId.Accidents));
            Assert.Equal(1, s.Statistics.Today(StatId.SabotageCommitted));
            Assert.Single(w.Items.Acts);
            Assert.Equal(2, w.Items.Acts[0].ActCode);
            Assert.Equal(sym - 15, w.Rules.Get<IRivalSympathy>().Get(w, 1, 0));
        }

        [Fact]
        public void PlanePrank_NoNextFlight_Retriggers_Quirk()
        {
            var w = ItemsWorld.Make(options: OriginalOptions.Game(o => o.RandomStartDay = false));
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = OpsWorld.AddPlane(w, v);
            pl.Location = OpsWorld.C(1);
            s.Sabotage.Trust = 1;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok);
            SabotageEngine.HourlyPlanePranks(w);
            pl.Location = PlaneLocation.Airborne;
            SabotageEngine.HourlyPlanePranks(w);
            SabotageEngine.HourlyPlanePranks(w);
            Assert.Equal(1, s.Sabotage.PlaneMode);       // still pending
            Assert.Equal(2, v.Statistics.Today(StatId.Accidents));
            Assert.Equal(0, s.Sabotage.Hints);

            var f = ItemsWorld.Make(options: new GameOptions { RandomStartDay = false, FixSabotageRetrigger = true });
            var fv = f.Players[1];
            var fp = OpsWorld.AddPlane(f, fv);
            fp.Location = OpsWorld.C(1);
            f.Players[0].Sabotage.Trust = 1;
            Assert.True(ItemsWorld.Order(f, 0, 1, PrankFamily.Plane, 1, fp.Id).Ok);
            SabotageEngine.HourlyPlanePranks(f);
            fp.Location = PlaneLocation.Airborne;
            SabotageEngine.HourlyPlanePranks(f);
            Assert.Equal(0, fv.Statistics.Today(StatId.Accidents));
        }

        [Fact]
        public void FlatTyre_DelaysDeparture_StatisticToSaboteur()
        {
            var w = ItemsWorld.Make(options: OriginalOptions.Game(o => o.RandomStartDay = false));
            w.Clock.Time = GameClock.At(11, 0);
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = GroundPlaneWithFlight(w, v, 11);
            s.Sabotage.Trust = 3;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 3, pl.Id).Ok);
            SabotageEngine.HourlyPlanePranks(w);
            Assert.Equal(0, s.Sabotage.PlaneMode);
            Assert.Equal(12, pl.Plan.Entries[0].StartHour);
            Assert.Equal(1, s.Statistics.Today(StatId.Delays));   // QUIRK: saboteur
            Assert.Equal(0, v.Statistics.Today(StatId.Delays));
            Assert.Equal(10, s.Sabotage.Hints);

            // Default (fixed): the delay is the victim's.
            var f = ItemsWorld.Make();
            f.Clock.Time = GameClock.At(11, 0);
            var fpl = GroundPlaneWithFlight(f, f.Players[1], 11);
            f.Players[0].Sabotage.Trust = 3;
            Assert.True(ItemsWorld.Order(f, 0, 1, PrankFamily.Plane, 3, fpl.Id).Ok);
            SabotageEngine.HourlyPlanePranks(f);
            Assert.Equal(0, f.Players[0].Statistics.Today(StatId.Delays));
            Assert.Equal(1, f.Players[1].Statistics.Today(StatId.Delays));
        }

        [Fact]
        public void PlanePrank_LapsesWhenThePlaneIsSold()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = OpsWorld.AddPlane(w, v);
            s.Sabotage.Trust = 1;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok);
            v.Planes.Remove(pl.Id);
            SabotageEngine.HourlyPlanePranks(w);
            Assert.False(s.Sabotage.AnyPending);
        }

        // ------------------------------------------------------------------ §1.3 person / special pranks (morning)

        [Fact]
        public void MorningPranks_Effects()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var b = w.Players[1];
            var c = w.Players[2];
            var d = w.Players[3];
            b.Items.Add(ItemId.Laptop);
            // robots place orders without paying
            Assert.True(SabotageEngine.RobotOrder(w, b, 0, PrankFamily.Person, 1));
            Assert.True(SabotageEngine.RobotOrder(w, c, 1, PrankFamily.Person, 2));
            Assert.True(SabotageEngine.RobotOrder(w, d, 2, PrankFamily.Person, 4));
            s.Sabotage.Trust = 4;
            Assert.True(ItemsWorld.Order(w, 0, 3, PrankFamily.Special, 4).Ok);
            long sMoney = s.Money, dMoney = d.Money;
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.True(s.Sick);
            Assert.Equal(1, b.LaptopVirus);
            Assert.True(c.StrikePlanned);
            Assert.Equal(dMoney - 1000000, d.Money);
            Assert.Equal(sMoney + 1000000, s.Money);
            Assert.Equal(8, b.Sabotage.Hints);
            Assert.Equal(0, c.Sabotage.Hints);   // virus: no hints
            Assert.Equal(40, d.Sabotage.Hints);
            Assert.Equal(30, s.Sabotage.Hints);
            Assert.Equal(4, w.Items.Acts.Count);
            Assert.Equal(2075, w.Items.Acts[1].ActCode);
            Assert.Equal(2094, w.Items.Acts[0].ActCode);
            // idempotent per date
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.Equal(4, w.Items.Acts.Count);
        }

        [Fact]
        public void Specials_Brochure_Phones_Press_Impound()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            var pl = OpsWorld.AddPlane(w, v);
            s.Sabotage.Trust = 5;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 1).Ok);
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.Equal(0, v.BrochureFrom);
            Assert.Equal(0, w.Rules.Get<IFlightSabotageState>().BrochureTarget(w, v));

            w.Clock.Date++;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 3).Ok);
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.True(w.Rules.Get<IFlightSabotageState>().PressStatement(w, v));

            w.Clock.Date++;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 5, pl.Id).Ok);
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.Equal(15, pl.PseudoProblem);

            w.Clock.Date++;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 2).Ok);
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.True(v.PhonesDown);
            SabotageEngine.NightlyReset(w, v);
            Assert.False(v.PhonesDown);
            Assert.False(v.PressStatement);
            Assert.Equal(-1, v.BrochureFrom);
        }

        [Fact]
        public void OfficeBomb_ExplodesAtTheDoor()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            s.Sabotage.Trust = 3;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 3).Ok);
            SabotageEngine.ExecuteMorningPranks(w);
            Assert.Equal(1, v.OfficeState);
            Assert.True(w.Execute(new ExplodeOfficeCommand(1)).Ok);
            Assert.Equal(2, v.OfficeState);
            // the glowing coal can be taken by a human while an office is destroyed
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.GlowingCoal)).Ok);
            Assert.True(s.HasItem(ItemId.GlowingCoal));
            Assert.False(w.Execute(new PickUpItemCommand(0, ItemId.GlowingCoal)).Ok);
        }

        [Fact]
        public void RouteTheft_RemovesBothDirections_GivesNothing_Quirk()
        {
            foreach (bool fix in new[] { false, true })
            {
                var w = ItemsWorld.Make(options: new GameOptions { RandomStartDay = false, FixRouteTheftTransfer = fix });
                var s = w.Players[0];
                var v = w.Players[1];
                OpenAT.Sim.Routes.RouteOps.Rent(w, v, OpsWorld.C(1), OpsWorld.C(2), 30000);
                int ri = -1, back = -1;
                for (int i = 0; i < w.RouteTable.Count; i++)
                {
                    if (w.RouteTable[i].From == OpsWorld.C(1) && w.RouteTable[i].To == OpsWorld.C(2)) ri = i;
                    if (w.RouteTable[i].From == OpsWorld.C(2) && w.RouteTable[i].To == OpsWorld.C(1)) back = i;
                }
                Assert.NotEqual(0, v.RentRoutes[ri].Rank);
                s.Sabotage.Trust = 6; // only a cheat gets here
                Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Special, 6, ri).Ok);
                SabotageEngine.ExecuteMorningPranks(w);
                Assert.Equal(0, v.RentRoutes[ri].Rank);
                Assert.Equal(0, v.RentRoutes[back].Rank);
                Assert.Equal(70, s.Sabotage.Hints);
                Assert.Equal(fix ? 1 : 0, s.RentRoutes[ri].Rank);
                Assert.Equal(fix ? 1 : 0, s.RentRoutes[back].Rank);
            }
        }

        // ------------------------------------------------------------------ §1.4 evidence, exposure, fines

        [Fact]
        public void Hints_DecayThreePerNight()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            p.Sabotage.Hints = 7;
            SabotageEngine.NightlyReset(w, p);
            Assert.Equal(4, p.Sabotage.Hints);
            SabotageEngine.NightlyReset(w, p);
            SabotageEngine.NightlyReset(w, p);
            Assert.Equal(0, p.Sabotage.Hints);
        }

        [Fact]
        public void Briefing_CommentsByHints_FineAtHundred()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            int[] hints = { 14, 15, 39, 40, 99, 100, 120 };
            int[] comment = { 2070, 2071, 2071, 2072, 2072, 2073, 2073 };
            for (int i = 0; i < hints.Length; i++)
            {
                w.Items.Acts.Clear();
                w.Items.Acts.Add(new SabotageAct { Perpetrator = 0, Victim = 1, Family = PrankFamily.Plane, Mode = 1, ActCode = 1 });
                s.Sabotage.Hints = hints[i];
                long sm = s.Money, vm = v.Money;
                v.Counters.DaysWithoutSabotage = 9;
                SabotageEngine.MorningReport(w);
                var line = w.Items.Report.Lines[0];
                Assert.Equal(comment[i], line.CommentId);
                long fine = hints[i] >= 100 ? hints[i] * 10000L : 0;
                Assert.Equal(fine, line.Fine);
                Assert.Equal(sm - fine, s.Money);
                Assert.Equal(vm + fine, v.Money);
                Assert.Equal(0, v.Counters.DaysWithoutSabotage);
            }
            // Hints are not reset by the fine: every further act is fined again.
            Assert.Equal(120, s.Sabotage.Hints);
            Assert.Equal(-(100 + 120) * 10000L, s.Statistics.Today(StatId.ExpensePenalties));
        }

        [Fact]
        public void Briefing_ForeignActsAreAnonymous()
        {
            var w = ItemsWorld.Make();
            w.Items.Acts.Add(new SabotageAct { Perpetrator = SabotageAct.Foreign, Victim = 1, Family = PrankFamily.Person, Mode = 1, ActCode = 2075 });
            SabotageEngine.MorningReport(w);
            Assert.Equal(2070, w.Items.Report.Lines[0].CommentId);
            Assert.Equal(0, w.Items.Report.Lines[0].Fine);
        }

        [Fact]
        public void PlanePrank5_AloneExposes()
        {
            Assert.Equal(100, SabotageTables.Hints(PrankFamily.Plane, 5));
        }

        // ------------------------------------------------------------------ §1.6 foreign sabotage

        [Fact]
        public void ForeignSabotage_Crisis_Deterministic_NoHints()
        {
            var w1 = ItemsWorld.Make(ItemSystems.CrisisMission);
            var w2 = ItemsWorld.Make(ItemSystems.CrisisMission);
            foreach (var w in new[] { w1, w2 })
                for (int c = 0; c < Player.Count; c++) OpsWorld.AddPlane(w, w.Players[c]);
            SabotageEngine.ForeignSabotage(w1);
            SabotageEngine.ForeignSabotage(w2);
            Assert.False(w1.Players[0].Sabotage.AnyPending);   // never the local player
            for (int c = 1; c < Player.Count; c++)
            {
                var a = w1.Players[c].Sabotage; var b = w2.Players[c].Sabotage;
                Assert.Equal(a.PlaneMode, b.PlaneMode); Assert.Equal(a.PersonMode, b.PersonMode); Assert.Equal(a.SpecialMode, b.SpecialMode);
                if (a.AnyPending) Assert.True(a.PlaneForeign || a.PersonForeign || a.SpecialForeign);
            }
            SabotageEngine.ExecuteMorningPranks(w1);
            for (int c = 1; c < Player.Count; c++)
                if (!w1.Players[c].Sabotage.PlaneForeign) Assert.Equal(0, w1.Players[c].Sabotage.Hints);
        }

        // ------------------------------------------------------------------ full day cycle (sim-core §2.6–2.8)

        [Fact]
        public void DayCycle_ReportAtNine_ExecuteWhenLeavingTheBriefing()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            s.Sabotage.Trust = 1;
            w.StartWorkingDay();
            w.Tick();
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Person, 1).Ok);
            OpsWorld.RunUntil(w, 1, 9, 0);
            w.Tick();   // the 9:00 morning sequence runs on the next tick
            Assert.Equal(DayState.Morning, w.Clock.State);
            // Day 1, 9:00: nothing listed yet (the prank runs when leaving this briefing).
            Assert.Empty(w.Items.Report.Lines);
            Assert.False(w.Players[1].Sick);
            OpsWorld.RunUntil(w, 1, 9, 30);
            Assert.True(w.Players[1].Sick);
            Assert.Equal(8, s.Sabotage.Hints);
            OpsWorld.RunUntil(w, 2, 9, 0);
            w.Tick();
            // Day 2 briefing lists it; hints decayed by 3 overnight.
            Assert.Single(w.Items.Report.Lines);
            Assert.Equal(5, s.Sabotage.Hints);
            Assert.False(w.Players[1].Sick);   // cured overnight
            Assert.Equal(0, w.Players[1].Counters.DaysWithoutSabotage);
        }
    }
}
