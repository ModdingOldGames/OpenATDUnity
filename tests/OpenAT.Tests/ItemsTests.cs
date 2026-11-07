using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Items;
using Xunit;

namespace OpenAT.Tests
{
    public class ItemsInventoryTests
    {
        static ItemUsed Use(World w, int player, int item, int room, bool partner = false, bool? upper = null)
        {
            var p = w.Players[player];
            var c = new UseItemCommand(player, p.Items.IndexOf(item), new ItemUseContext { Room = room, HasPartner = partner, UpperFloor = upper });
            Assert.True(w.Execute(c).Ok);
            return c.Result;
        }

        static World Running()
        {
            var w = ItemsWorld.Make();
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            return w;
        }

        // ------------------------------------------------------------------ §3.1 inventory

        [Fact]
        public void Inventory_SixSlots_PackedAndReadOnly()
        {
            var inv = new Inventory();
            Assert.True(inv.IsReadOnly);
            for (int i = 0; i < 6; i++) Assert.True(inv.Add(20 - i));
            Assert.False(inv.Add(ItemId.Pills));   // full: fails silently
            Assert.Equal(new[] { 15, 16, 17, 18, 19, 20 }, inv.ToArray());
            inv.Remove(17);
            Assert.Equal(new[] { 15, 16, 18, 19, 20, 0xFF }, inv.ToArray());
            // the status bar reads Player.Items as an IList<int> field
            var f = typeof(Player).GetField("Items");
            Assert.NotNull(f);
            Assert.IsAssignableFrom<System.Collections.Generic.IList<int>>(f.GetValue(new Player(0)));
        }

        [Fact]
        public void WorldItems_OncePerDayForEverybody()
        {
            var w = Running();
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Glove)).Ok);
            Assert.False(w.Execute(new PickUpItemCommand(1, ItemId.Glove)).Ok);
            Assert.True(w.Execute(new PickUpItemCommand(1, ItemId.Pliers)).Ok);
            w.Items.RestoreDailyItems();
            Assert.True(w.Execute(new PickUpItemCommand(1, ItemId.Glove)).Ok);
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Pliers)).Ok);
        }

        [Fact]
        public void FullInventory_PickUpFails()
        {
            var w = Running();
            var p = w.Players[0];
            for (int i = 0; i < 6; i++) p.Items.Add(ItemId.Cigar);
            Assert.Equal("inventory-full", w.Execute(new PickUpItemCommand(0, ItemId.Glove)).Reason);
            Assert.True(w.Items.Glove);
        }

        // ------------------------------------------------------------------ §3.3 trade chains

        [Fact]
        public void Chain_BraHorseshoeDrinker()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Bra)).Ok);
            Assert.False(w.Execute(new ReceiveRewardCommand(0, ItemId.Horseshoe)).Ok);
            Assert.Equal(801, Use(w, 0, ItemId.Bra, ItemRooms.DutyFree, true).DialogPar);
            Assert.True(p.DutyTrust);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.Horseshoe)).Ok);
            Assert.Equal(800, Use(w, 0, ItemId.Horseshoe, ItemRooms.Ricks, true).DialogPar);
            Assert.True(p.TrinkerTrust);
            // no more bras after the drinker's trust
            Assert.False(w.Execute(new PickUpItemCommand(0, ItemId.Bra)).Ok);
            // a strike is ended by the drinker after a countdown of 10
            p.StrikeHours = 5;
            Assert.True(w.Execute(new DrinkerEndsStrikeCommand(0)).Ok);
            Assert.Equal(10, p.StrikeEndCountdown);
        }

        [Fact]
        public void Chain_SpiderDartDiskette()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Spider)).Ok);
            // without saboteur trust the spider is kept
            Assert.Equal(3000, Use(w, 0, ItemId.Spider, ItemRooms.Sabotage, true).DialogPar);
            Assert.True(p.HasItem(ItemId.Spider));
            p.Sabotage.Trust = 1;
            Use(w, 0, ItemId.Spider, ItemRooms.Sabotage, true);
            Assert.True(p.SpiderTrust);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.Dart)).Ok);
            // the dart works only in Normal-or-harder missions or the free game: here free game
            Assert.Equal(8001, Use(w, 0, ItemId.Dart, ItemRooms.Werbung, true).DialogPar);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.Diskette)).Ok);
            // diskette cures the virus
            p.Items.Add(ItemId.Laptop);
            p.LaptopVirus = 1;
            var r = Use(w, 0, ItemId.Diskette, ItemRooms.Laptop);
            Assert.Equal(0, p.LaptopVirus);
            Assert.Equal("leave-laptop", r.Screen);
        }

        [Fact]
        public void Chain_PostcardPills_CureSickness()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Postcard)).Ok);
            Assert.Equal(300, Use(w, 0, ItemId.Postcard, ItemRooms.Personnel(0), true).DialogPar);
            Assert.True(p.SeligTrust);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.Pills)).Ok);
            Assert.Equal(ItemUseOutcome.Gift, Use(w, 0, ItemId.Pills, ItemRooms.Bank, true).Outcome);   // not sick
            p.Sick = true;
            Use(w, 0, ItemId.Pills, ItemRooms.Airport);
            Assert.False(p.Sick);
            Assert.False(p.HasItem(ItemId.Pills));
        }

        [Fact]
        public void Chain_GloveRedBullStinkBomb()
        {
            var w = Running();
            var p = w.Players[0];
            var shock = new UseVendingMachineCommand(0);
            p.Drunk = 50;
            w.Execute(shock);
            Assert.False(shock.GotRedBull);
            Assert.Equal(0, p.Drunk);   // the shock sobers him up
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Glove)).Ok);
            Assert.True(w.Execute(new UseVendingMachineCommand(0)).Ok);
            Assert.True(p.HasItem(ItemId.RedBull));
            Assert.False(p.HasItem(ItemId.Glove));
            Assert.Equal(1010, Use(w, 0, ItemId.RedBull, ItemRooms.Kiosk, true).DialogPar);
            Assert.True(p.KioskTrust);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.StinkBomb)).Ok);
            Assert.False(p.KioskTrust);
            // upper floor: refused; lower floor: dropped
            Assert.Equal(ItemEngine.RequestNoStench, Use(w, 0, ItemId.StinkBomb, ItemRooms.Airport, upper: true).RequestId);
            Assert.Equal(ItemUseOutcome.Applied, Use(w, 0, ItemId.StinkBomb, ItemRooms.Airport, upper: false).Outcome);
            Assert.False(p.HasItem(ItemId.StinkBomb));
        }

        [Fact]
        public void Chain_PaperClipGlue()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.Equal("fracht-1010", w.Execute(new PickUpItemCommand(0, ItemId.Glue)).Reason);
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.PaperClip)).Ok);
            Assert.Equal(1020, Use(w, 0, ItemId.PaperClip, ItemRooms.Fracht, true).DialogPar);
            Assert.Equal(1, w.Items.GlueState);
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Glue)).Ok);
            Assert.False(w.Execute(new PickUpItemCommand(1, ItemId.Glue)).Ok);
            Assert.Equal(ItemEngine.RequestNoGlue, Use(w, 0, ItemId.Glue, ItemRooms.Airport, upper: false).RequestId);
            Assert.Equal(ItemUseOutcome.Applied, Use(w, 0, ItemId.Glue, ItemRooms.Airport, upper: true).Outcome);
            w.Items.RestoreDailyItems();
            Assert.Equal(0, w.Items.GlueState);
        }

        [Fact]
        public void Chain_PerfumeSlime()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Perfume)).Ok);
            Assert.Equal(ItemUseOutcome.Gift, Use(w, 0, ItemId.Perfume, ItemRooms.Werkstatt, true).Outcome);
            w.Execute(new SetWorkshopSlimedCommand());
            Use(w, 0, ItemId.Perfume, ItemRooms.Werkstatt, true);
            Assert.True(p.HasItem(ItemId.StinkPerfume));
            Use(w, 0, ItemId.StinkPerfume, ItemRooms.Airport);
            Assert.Equal(ItemEngine.StenchUnits, p.Stench);
        }

        [Fact]
        public void BeerAndChocolates_DrinkEatGive()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.Beer)).Ok);
            Assert.False(w.Execute(new BuyDutyFreeCommand(0, ItemId.Beer)).Ok);   // only if not owned
            Assert.Equal(2, Use(w, 0, ItemId.Beer, ItemRooms.Werkstatt, true).DialogPar);
            Assert.Equal(1, p.MechTrust);
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.Beer)).Ok);
            Assert.Equal("gulps.raw", Use(w, 0, ItemId.Beer, ItemRooms.Airport).Sound);
            Assert.Equal(ItemEngine.DrunkPerDose, p.Drunk);

            int sym = w.Rules.Get<IRivalSympathy>().Get(w, 1, 0);
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.SpikedChocolates)).Ok);
            Assert.False(w.Execute(new BuyDutyFreeCommand(0, ItemId.Chocolates)).Ok);   // neither kind owned
            Assert.True(w.Execute(new OfferChocolatesCommand(0, 1)).Ok);
            Assert.Equal(sym - 10, w.Rules.Get<IRivalSympathy>().Get(w, 1, 0));
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.Chocolates)).Ok);
            Assert.True(w.Execute(new OfferChocolatesCommand(0, 1)).Ok);
            Assert.Equal(sym, w.Rules.Get<IRivalSympathy>().Get(w, 1, 0));
        }

        [Fact]
        public void DutyFree_PricesFromSpec3_2()
        {
            var w = Running();
            var p = w.Players[0];
            long m = p.Money;
            w.Execute(new BuyDutyFreeCommand(0, ItemId.Filofax)); Assert.Equal(m -= 5000, p.Money);
            w.Execute(new BuyDutyFreeCommand(0, ItemId.Phone)); Assert.Equal(m -= 300000, p.Money);
            w.Execute(new BuyDutyFreeCommand(0, ItemId.Beer)); Assert.Equal(m -= 5000, p.Money);
            w.Execute(new BuyDutyFreeCommand(0, ItemId.Chocolates)); Assert.Equal(m -= 10000, p.Money);
            // MG only from day 1 on and while the saboteur's trust is 0
            Assert.Equal("not-offered", w.Execute(new BuyDutyFreeCommand(0, ItemId.MachineGun)).Reason);
            w.Clock.Date = 1;
            Assert.True(w.Execute(new BuyDutyFreeCommand(0, ItemId.MachineGun)).Ok);
            Assert.Equal(m - 80000, p.Money);
            Assert.Equal(MoneyReason.ShopPurchase, p.Statement[p.Statement.Count - 1].Reason);
        }

        [Fact]
        public void Catalog_MatchesInstalledTexts()
        {
            var inst = GameInstall.TryLocate();
            if (inst == null) return;
            var d = GameData.Load(inst);
            var c = new ItemCatalog();
            c.Load(d.StdText);
            Assert.Equal(new long[] { 10000, 30000, 50000, 100000 }, c.LaptopPrice);
            Assert.Equal(80000, c.MachineGunPrice);
            Assert.Equal(5000, c.FilofaxPrice);
            Assert.Equal(300000, c.PhonePrice);
            Assert.Equal(5000, c.BeerPrice);
            Assert.Equal(10000, c.ChocolatesPrice);
        }

        // ------------------------------------------------------------------ §3.2 / §3.5 laptop

        [Fact]
        public void Laptop_LockedOneSalePerDay_UpgradesAndBattery()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.Equal("duty-3", w.Execute(new BuyLaptopCommand(0)).Reason);
            w.Clock.Date = 8;
            long m = p.Money;
            Assert.True(w.Execute(new BuyLaptopCommand(0)).Ok);
            Assert.Equal(m - 10000, p.Money);
            Assert.Equal(1, p.LaptopQuality);
            Assert.Equal(40, p.LaptopBattery);
            Assert.Equal("duty-1", w.Execute(new BuyLaptopCommand(1)).Reason);   // one per day for the airport
            long[] prices = { 30000, 50000, 100000 };
            int[] battery = { 80, 200, 1440 };
            for (int i = 0; i < 3; i++)
            {
                w.Items.LaptopSoldTo = -1;
                m = p.Money;
                Assert.True(w.Execute(new BuyLaptopCommand(0)).Ok);
                Assert.Equal(m - prices[i], p.Money);
                Assert.Equal(battery[i], p.LaptopBattery);
            }
            Assert.Equal(4, p.LaptopQuality);
            Assert.Equal(1, p.Items.Used);   // the old one is replaced
            w.Items.LaptopSoldTo = -1;
            Assert.Equal("max-model", w.Execute(new BuyLaptopCommand(0)).Reason);
        }

        [Fact]
        public void Laptop_FullInventory_RefusedByDefault_ChargedInOriginal()
        {
            foreach (bool fix in new[] { true, false })
            {
                var w = Running();
                w.Options.FixLaptopFullInventory = fix;
                w.Clock.Date = 8;
                var p = w.Players[0];
                while (p.Items.HasSpace) p.Items.Add(ItemId.Beer);
                long m = p.Money;
                var r = w.Execute(new BuyLaptopCommand(0));
                if (fix)
                {
                    Assert.Equal("inventory-full", r.Reason);
                    Assert.Equal(m, p.Money);
                }
                else
                {
                    Assert.True(r.Ok);
                    Assert.Equal(m - 10000, p.Money);
                    Assert.False(p.HasItem(ItemId.Laptop));
                }
            }
        }

        [Fact]
        public void Laptop_BatteryDrainsPerMinute_RechargesAtNight()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            p.Items.Add(ItemId.Laptop);
            p.LaptopQuality = 1;
            p.LaptopBattery = 40;
            Assert.True(w.Execute(new SetLaptopOpenCommand(0, true)).Ok);
            int low = 0;
            w.Events.Subscribe<LaptopBatteryLow>(e => low++);
            OpsWorld.RunUntil(w, 0, 10, 0);
            Assert.Equal(0, p.LaptopBattery);
            Assert.Equal(2, low);
            OpsWorld.RunUntil(w, 1, 9, 0);
            Assert.Equal(40, p.LaptopBattery);
        }

        [Fact]
        public void Laptop_VirusRepairAtDutyFree()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            p.Items.Add(ItemId.Laptop);
            p.LaptopQuality = 2;
            p.LaptopVirus = 1;
            Assert.Equal("virus", w.Execute(new BuyLaptopCommand(0)).Reason);
            Assert.True(w.Execute(new LaptopRepairCommand(0)).Ok);
            Assert.False(p.HasItem(ItemId.Laptop));
            Assert.Equal(2, p.LaptopVirus);
            Assert.False(w.Execute(new LaptopRepairCommand(0)).Ok);   // repaired overnight
            OpsWorld.RunUntil(w, 1, 9, 0);
            Assert.Equal(3, p.LaptopVirus);
            Assert.True(w.Execute(new LaptopRepairCommand(0)).Ok);
            Assert.True(p.HasItem(ItemId.Laptop));
            Assert.Equal(0, p.LaptopVirus);
        }

        [Fact]
        public void Night_GlowingCoalCools_OfficeRepaired()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            p.Items.Add(ItemId.GlowingCoal);
            p.OfficeState = 2;
            p.Caffeine = 100;
            OpsWorld.RunUntil(w, 1, 9, 0);
            Assert.True(p.HasItem(ItemId.Coal));
            Assert.False(p.HasItem(ItemId.GlowingCoal));
            Assert.Equal(0, p.OfficeState);
            Assert.Equal(0, p.Caffeine);
        }

        [Fact]
        public void GlowingCoal_BurnsTheOffers()
        {
            var w = Running();
            var p = w.Players[0];
            p.Items.Add(ItemId.GlowingCoal);
            w.LastMinute.Offers[0].Premium = 5000;
            w.LastMinute.Offers[3].Premium = 7000;
            var r = Use(w, 0, ItemId.GlowingCoal, ItemRooms.LastMinute, true);
            Assert.Equal("fireauft.raw", r.Sound);
            foreach (var o in w.LastMinute.Offers) Assert.True(o.Premium <= 0);
            Assert.True(p.HasItem(ItemId.GlowingCoal));   // not consumed
        }
    }

    public class ItemsSecurityTests
    {
        [Fact]
        public void Toggle_RowsAndGateCycle()
        {
            int f = 0;
            f = SecurityBits.Toggle(f, SecurityBits.Gate, false); Assert.Equal(1, SecurityBits.GateLevel(f));
            f = SecurityBits.Toggle(f, SecurityBits.Gate, false); Assert.Equal(2, SecurityBits.GateLevel(f));
            f = SecurityBits.Toggle(f, SecurityBits.Gate, false); Assert.Equal(3, SecurityBits.GateLevel(f));
            Assert.Equal((1 << 8) | (1 << 10) | (1 << 11), f);
            f = SecurityBits.Toggle(f, SecurityBits.Gate, false); Assert.Equal(0, f);
            Assert.Equal(0, SecurityBits.Toggle(0, SecurityBits.Laptop, false));     // needs a laptop
            Assert.Equal(2, SecurityBits.Toggle(0, SecurityBits.Laptop, true));
            Assert.Equal(0, SecurityBits.Toggle(1, SecurityBits.Office, false));
        }

        [Fact]
        public void Fees_Spec14_PerPlaneAndFlat()
        {
            var p = new Player(0);
            p.SecurityFlags = 0;
            for (int row = 0; row < 8; row++) p.SecurityFlags = SecurityBits.Toggle(p.SecurityFlags, row, true);
            for (int i = 0; i < 3; i++) p.SecurityFlags = SecurityBits.Toggle(p.SecurityFlags, SecurityBits.Gate, true);
            // flat 25+20+25+30+30+30 = 160 000; per plane 30+30+40+50+50 = 200 000
            Assert.Equal(160000 + 200000 * 3, p.SecurityCosts(3));
        }

        [Fact]
        public void Pliers_PowerOutage_ThreeDays()
        {
            var w = ItemsWorld.Make();
            w.Clock.Frozen = false;
            var p = w.Players[1];
            foreach (var q in w.Players) q.SecurityFlags = 0x1FF;
            Assert.True(w.Execute(new PickUpItemCommand(1, ItemId.Pliers)).Ok);
            var use = new UseItemCommand(1, p.Items.IndexOf(ItemId.Pliers), new ItemUseContext { Room = ItemRooms.Airport, NearSecurityDoor = true });
            Assert.True(w.Execute(use).Ok);
            Assert.Equal(ItemUseOutcome.Applied, use.Result.Outcome);
            foreach (var q in w.Players) Assert.Equal(0, q.SecurityFlags);
            Assert.Equal(3, w.Items.SecurityOutageDays);
            Assert.Equal("security-closed", w.Execute(new ToggleSecurityCommand(0, 0)).Reason);
            Assert.False(w.Rules.Get<OpenAT.Sim.AI.IRivalActions>().SecurityOfficeOpen(w));
            w.Clock.Frozen = true;
            OpsWorld.RunUntil(w, 3, 9, 0);
            Assert.Equal(0, w.Items.SecurityOutageDays);
            Assert.True(w.Execute(new ToggleSecurityCommand(0, 0)).Ok);
        }

        [Fact]
        public void Pliers_CutRivalOfficePower()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[0];
            p.Items.Add(ItemId.Pliers);
            Assert.True(w.Execute(new CutOfficePowerCommand(0, 2)).Ok);
            Assert.Equal(3, w.Players[2].OfficeState);
            Assert.False(p.HasItem(ItemId.Pliers));
        }

        [Fact]
        public void Robot_BuysOnlyNeededProtection()
        {
            var w = ItemsWorld.Make();
            var p = w.Players[1];
            p.SecurityNeeded = 1 << SecurityBits.Office;
            var r = new TeakRand(5);
            for (int i = 0; i < 50; i++) ItemSystems.RobotVisitSecurity(w, p, r);
            Assert.Equal(1 << SecurityBits.Office, p.SecurityFlags);
            p.Money = 0;
            for (int i = 0; i < 50; i++) ItemSystems.RobotVisitSecurity(w, p, r);
            Assert.Equal(0, p.SecurityFlags);
        }

        [Fact]
        public void FlightSabotageState_IsTheItemsDomains()
        {
            var w = ItemsWorld.Make();
            Assert.IsType<ItemsFlightSabotageState>(w.Rules.Get<OpenAT.Sim.Fleet.IFlightSabotageState>());
            Assert.IsType<ItemsRivalActions>(w.Rules.Get<OpenAT.Sim.AI.IRivalActions>());
            w.Players[2].SecurityFlags = 1 << 10;
            Assert.Equal(1 << 10, w.Rules.Get<OpenAT.Sim.Fleet.IFlightSabotageState>().SecurityFlags(w, w.Players[2]));
        }
    }

    public class ItemsSpecialRoomTests
    {
        [Fact]
        public void RocketParts_PerPartPrices_NotTheSdlBug()
        {
            var w = ItemsWorld.Make(Mission.Rocket);
            var p = w.Players[0];
            p.Money = 300000000;
            Assert.Equal("nasa-5096", w.Execute(new BuySpacePartCommand(0, SpaceParts.Tower)).Reason);
            long total = 0;
            int[] order = { 0, 1, 2, 3, 4, 5, 8, 6, 7, 9 };   // menu order
            long[] price = { 200000, 400000, 600000, 5000000, 8000000, 10000000, 20000000, 25000000, 50000000, 85000000 };
            for (int i = 0; i < order.Length; i++)
            {
                long m = p.Money;
                Assert.True(w.Execute(new BuySpacePartCommand(0, order[i])).Ok, "part " + order[i]);
                Assert.Equal(m - price[i], p.Money);
                total += price[i];
            }
            Assert.Equal(204200000, total);
            Assert.Equal(SpaceParts.AllMask, p.Counters.RocketFlags);
            Assert.Equal("nasa-5098", w.Execute(new BuySpacePartCommand(0, 0)).Reason);
            Assert.Equal(MoneyReason.RocketPart, p.Statement[p.Statement.Count - 1].Reason);
        }

        [Fact]
        public void RocketParts_Prerequisites_AndCashOnly()
        {
            var w = ItemsWorld.Make(Mission.Rocket);
            var p = w.Players[0];
            p.Counters.RocketFlags = SpaceParts.RocketStage1 | (1 << SpaceParts.Airframe);
            Assert.Equal("nasa-5197", w.Execute(new BuySpacePartCommand(0, SpaceParts.Boosters)).Reason);
            p.Counters.RocketFlags = SpaceParts.RocketStage1;
            Assert.Equal("nasa-5196", w.Execute(new BuySpacePartCommand(0, SpaceParts.Wings)).Reason);
            p.Money = 4999999;
            Assert.Equal("nasa-7000", w.Execute(new BuySpacePartCommand(0, SpaceParts.Airframe)).Reason);
        }

        [Fact]
        public void RocketParts_RobotPriceByBit_Quirk()
        {
            var w = ItemsWorld.Make(Mission.Rocket, new GameOptions { FixRobotRocketPrices = false });
            var r = w.Players[1];
            r.Counters.RocketFlags = 0x3F;   // bits 0..5
            long m = r.Money;
            Assert.Equal(6, ItemSystems.RobotBuySpacePart(w, r));
            Assert.Equal(m - 20000000, r.Money);  // humans pay 25 M for bit 6
            Assert.Equal(25000000, SpaceParts.Price(Mission.Rocket, 6));
        }

        [Fact]
        public void RocketParts_RobotPaysHumanPrice_Fixed()
        {
            var w = ItemsWorld.Make(Mission.Rocket);
            var r = w.Players[1];
            r.Counters.RocketFlags = 0x3F;
            long m = r.Money;
            Assert.Equal(6, ItemSystems.RobotBuySpacePart(w, r));
            Assert.Equal(m - 25000000, r.Money);
        }

        [Fact]
        public void StationParts_StagesAndPrices()
        {
            var w = ItemsWorld.Make(SpaceParts.StationMission);
            var p = w.Players[0];
            p.Money = 300000000;
            Assert.Equal("not-offered", w.Execute(new BuySpacePartCommand(0, 3)).Reason);
            long[] price = { 1000000, 2000000, 3000000, 2000000, 10000000, 20000000, 35000000, 50000000, 35000000, 80000000 };
            long total = 0;
            for (int b = 0; b < 10; b++)
            {
                long m = p.Money;
                Assert.True(w.Execute(new BuySpacePartCommand(0, b)).Ok, "bit " + b);
                Assert.Equal(m - price[b], p.Money);
                total += price[b];
            }
            Assert.Equal(238000000, total);
            var w2 = ItemsWorld.Make(SpaceParts.StationMission);
            w2.Players[0].Money = 999999;
            Assert.Equal("nasa-8300", w2.Execute(new BuySpacePartCommand(0, 0)).Reason);
        }

        [Fact]
        public void Nasa_OnlyInTheSpaceMissions_TelescopeCostsOne()
        {
            var w = ItemsWorld.Make();
            Assert.Equal("no-nasa", w.Execute(new BuySpacePartCommand(0, 0)).Reason);
            long m = w.Players[0].Money;
            Assert.True(w.Execute(new UseTelescopeCommand(0)).Ok);
            Assert.Equal(m - 1, w.Players[0].Money);
            Assert.Equal(MoneyReason.SpecialRoomEntry, w.Players[0].Statement[w.Players[0].Statement.Count - 1].Reason);
        }
    }
}
