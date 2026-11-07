using OpenAT.Sim.Finance;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Items
{
    /// <summary>Where and how an item is used (rooms-and-ui-1 §9.4). Filled by the view.</summary>
    public struct ItemUseContext
    {
        /// <summary>Room the player is in (0 or 1 = airport hall, <see cref="ItemRooms"/>).</summary>
        public int Room;
        /// <summary>The room has a default dialog partner (the "gift" reaction).</summary>
        public bool HasPartner;
        /// <summary>A dialog or menu is open (most room effects need neither).</summary>
        public bool DialogOrMenuOpen;
        /// <summary>Airport floor when no hall model is available: true upper, false lower, null = ask the hall.</summary>
        public bool? UpperFloor;
        /// <summary>Next to the security office entrance (no hall model available).</summary>
        public bool? NearSecurityDoor;
    }

    /// <summary>
    /// Inventory, shop and item-use rules (items-security-special.md §3; rooms-and-ui-1 §9.4,
    /// rooms-and-ui-7 §9). Called by the item commands.
    /// </summary>
    public static class ItemEngine
    {
        public const int StinkBombTicks = 33 * 60 * 2;
        public const int DrunkPerDose = 400, CaffeinePerDrink = 20 * 120, StenchUnits = 2000, GlueStuckTicks = 600;
        /// <summary>Hall request popups (MENU_REQUEST sub ids) for "not here".</summary>
        public const int RequestNoGlue = 30, RequestNoStench = 31;

        /// <summary>Gives an item if a slot is free (the original's BuyItem). Publishes <see cref="InventoryChanged"/>.</summary>
        public static bool Give(World w, Player p, int item, string source)
        {
            if (!p.Items.Add(item)) return false;
            w.Events.Publish(new InventoryChanged(p, item, true, source));
            return true;
        }

        public static bool Take(World w, Player p, int item, string source)
        {
            if (!p.Items.Remove(item)) return false;
            w.Events.Publish(new InventoryChanged(p, item, false, source));
            return true;
        }

        // ================================================================== duty free (rooms-and-ui-7 §9)

        /// <summary>Whether a direct duty-free good is on offer to the player (hotspot conditions).</summary>
        public static bool Offered(World w, Player p, int item)
        {
            switch (item)
            {
                case ItemId.MachineGun: return !p.HasItem(ItemId.MachineGun) && w.Clock.Date > 0 && p.Sabotage.Trust == 0;
                case ItemId.Filofax: case ItemId.Phone: case ItemId.Beer: return !p.HasItem(item);
                case ItemId.Chocolates: case ItemId.SpikedChocolates: return !p.HasItem(ItemId.Chocolates) && !p.HasItem(ItemId.SpikedChocolates);
            }
            return false;
        }

        /// <summary>
        /// Direct purchase (ids 400/500/600/700, chocolates 3001/3002): free slot, price booked as 9999 with
        /// statistic "other", then the bodyguard rebate. No debt check (the original has none).
        /// </summary>
        public static void BuyDirect(World w, Player p, int item)
        {
            long price = w.Items.Catalog.DirectPrice(item);
            Give(w, p, item, "shop");
            p.Book(-price, MoneyReason.ShopPurchase, "Item:" + ItemCatalog.LedgerTextId(item));
            p.Statistics.Add(StatId.ExpenseOther, -price);
            p.BodyguardRebate(price, true);
            w.Events.Publish(new ItemBought(p, item, price));
        }

        /// <summary>Laptop counter outcome (rooms-and-ui-7 §9 "Laptop").</summary>
        public enum LaptopOffer { Buy, Locked, SoldToday, VirusDialog, MaxModel }

        public static LaptopOffer LaptopState(World w, Player p)
        {
            if (p.LaptopVirus >= 1 && p.LaptopVirus <= 3) return LaptopOffer.VirusDialog;
            if (p.LaptopQuality >= 4 && p.HasItem(ItemId.Laptop)) return LaptopOffer.MaxModel;
            if (w.Clock.Date <= 7) return LaptopOffer.Locked;
            if (w.Items.LaptopSoldTo != -1) return LaptopOffer.SoldToday;
            return LaptopOffer.Buy;
        }

        /// <summary>
        /// Laptop purchase: the old one is dropped, the new model given (if there is space), price
        /// <c>Item 2000 + quality</c> with the rebate, quality + 1, battery of the new model, sale lock.
        /// A quality-4 laptop that is not owned starts again from quality 3.
        /// </summary>
        public static void BuyLaptop(World w, Player p, bool free = false)
        {
            if (p.LaptopQuality >= 4 && !p.HasItem(ItemId.Laptop)) p.LaptopQuality = 3;
            int q = System.Math.Min(p.LaptopQuality, 3);
            long price = w.Items.Catalog.LaptopPrice[q];
            Take(w, p, ItemId.Laptop, "upgrade");
            Give(w, p, ItemId.Laptop, "shop");
            if (!free)
            {
                p.Book(-price, MoneyReason.ShopPurchase, "Item:" + (1000 + q));
                p.Statistics.Add(StatId.ExpenseOther, -price);
                p.BodyguardRebate(price, true);
            }
            p.LaptopQuality = q + 1;
            p.LaptopBattery = ItemCatalog.LaptopBattery[p.LaptopQuality];
            w.Items.LaptopSoldTo = p.Index;
            w.Events.Publish(new ItemBought(p, ItemId.Laptop, free ? 0 : price));
        }

        /// <summary>Virus dialog at the duty free: state 1 → the saleswoman keeps the laptop (2); state 3 → returns it clean.</summary>
        public static bool LaptopRepair(World w, Player p)
        {
            if (p.LaptopVirus == 1 && p.HasItem(ItemId.Laptop))
            {
                Take(w, p, ItemId.Laptop, "repair");
                p.LaptopVirus = 2;
                p.LaptopOpen = false;
                return true;
            }
            if (p.LaptopVirus == 3 && p.Items.HasSpace)
            {
                Give(w, p, ItemId.Laptop, "repair");
                p.LaptopVirus = 0;
                return true;
            }
            return false;
        }

        // ================================================================== found items (§3.3)

        /// <summary>
        /// Picks up a world or chain item where the room offers it. Returns false when the item is not there
        /// (already taken today, condition not met) or the inventory is full.
        /// </summary>
        public static bool TryPickUp(World w, Player p, int item)
        {
            var wi = w.Items;
            switch (item)
            {
                case ItemId.Glove:
                    if (!wi.Glove || p.HasItem(item) || !Give(w, p, item, "found")) return false;
                    wi.Glove = false; return true;
                case ItemId.PaperClip:
                    if (!wi.PaperClip || p.HasItem(item) || !Give(w, p, item, "found")) return false;
                    wi.PaperClip = false; return true;
                case ItemId.Glue:
                    if (wi.GlueState != 1 || p.HasItem(item) || !Give(w, p, item, "found")) return false;
                    wi.GlueState = 2; return true;
                case ItemId.Perfume:
                    if (!wi.Perfume || !Give(w, p, item, "found")) return false;
                    wi.Perfume = false; return true;
                case ItemId.Pliers:
                    if (!wi.Pliers || !Give(w, p, item, "found")) return false;
                    wi.Pliers = false; return true;
                case ItemId.Postcard:
                    if (!wi.Postcard || p.SeligTrust || w.Difficulty == Mission.Tutorial || !Give(w, p, item, "found")) return false;
                    wi.Postcard = false; return true;
                case ItemId.GlowingCoal:
                    if (!wi.GlowingCoal || p.Owner != Owner.Human || !Give(w, p, item, "found")) return false;
                    wi.GlowingCoal = false; return true;
                case ItemId.Bra:
                    if (p.HasItem(ItemId.Bra) || p.HasItem(ItemId.Horseshoe) || p.TrinkerTrust || w.Difficulty == Mission.Tutorial) return false;
                    return Give(w, p, item, "found");
                case ItemId.Spider:
                    if (p.HasItem(item)) return false;
                    return Give(w, p, item, "found");
                case ItemId.Oil:
                    if (p.HasItem(item) || p.MechTrust == 0) return false;
                    return Give(w, p, item, "dialog");
            }
            return false;
        }

        /// <summary>
        /// Reward dialogs of the trade chains (§3.3): horseshoe (duty free, DutyTrust), dart (saboteur,
        /// SpiderTrust + saboteur trust), diskette (advertising, WerbungTrust), pills (personnel, SeligTrust),
        /// stink bomb (kiosk, KioskTrust — reset), Red Bull (vending machine with the glove).
        /// </summary>
        public static bool TryReward(World w, Player p, int item)
        {
            switch (item)
            {
                case ItemId.Horseshoe:
                    if (!p.DutyTrust || p.HasItem(item) || p.TrinkerTrust) return false;
                    return Give(w, p, item, "dialog");
                case ItemId.Dart:
                    if (!p.SpiderTrust || p.Sabotage.Trust == 0 || p.HasItem(ItemId.Dart) || p.HasItem(ItemId.Diskette) || p.WerbungTrust) return false;
                    return Give(w, p, item, "dialog");
                case ItemId.Diskette:
                    if (!p.WerbungTrust || p.HasItem(item)) return false;
                    return Give(w, p, item, "dialog");
                case ItemId.Pills:
                    if (!p.SeligTrust || p.HasItem(item)) return false;
                    return Give(w, p, item, "dialog");
                case ItemId.StinkBomb:
                    if (!p.KioskTrust || p.HasItem(item)) return false;
                    if (!Give(w, p, item, "dialog")) return false;
                    p.KioskTrust = false;
                    return true;
                case ItemId.RedBull:
                    if (!p.HasItem(ItemId.Glove)) return false;
                    p.Items.Replace(ItemId.Glove, ItemId.RedBull);
                    w.Events.Publish(new InventoryChanged(p, ItemId.Glove, false, "vending"));
                    w.Events.Publish(new InventoryChanged(p, ItemId.RedBull, true, "vending"));
                    return true;
            }
            return false;
        }

        // ================================================================== use (rooms-and-ui-1 §9.4)

        static bool KioskBlocks(World w, int room) => room == ItemRooms.Kiosk && !w.Newspapers.IsInteresting;

        /// <summary>Applies an item click; returns what the view should do.</summary>
        public static ItemUsed Use(World w, Player p, int item, ItemUseContext ctx)
        {
            int room = ctx.Room;
            bool free = !ctx.DialogOrMenuOpen;
            bool airport = ItemRooms.IsAirport(room);
            ItemUsed R(ItemUseOutcome o) => new ItemUsed(p, item, room, o);
            ItemUsed Gift() => ctx.HasPartner ? new ItemUsed(p, item, room, ItemUseOutcome.Gift) { DialogPar = 10000 + item } : R(ItemUseOutcome.Nothing);
            ItemUsed Dialog(int par) { Take(w, p, item, "use"); return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { DialogPar = par }; }

            switch (item)
            {
                case ItemId.Laptop:
                    return free ? new ItemUsed(p, item, room, ItemUseOutcome.OpenScreen) { Screen = "laptop" } : R(ItemUseOutcome.Nothing);
                case ItemId.Phone:
                    // Phones-dead prank (special 2): the mobile phone does not work today.
                    if (!free || p.PhonesDown) return R(ItemUseOutcome.Nothing);
                    return new ItemUsed(p, item, room, ItemUseOutcome.OpenScreen) { Screen = room == ItemRooms.Office(p.Index) ? "office-phone" : "phonebook" };
                case ItemId.Filofax:
                    return free ? new ItemUsed(p, item, room, ItemUseOutcome.OpenScreen) { Screen = "filofax" } : R(ItemUseOutcome.Nothing);

                case ItemId.MachineGun:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.ArabAir) { p.Sabotage.Trust = System.Math.Max(1, p.Sabotage.Trust); return Dialog(1); }
                    return Gift();
                case ItemId.Beer:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Werkstatt) { p.MechTrust = System.Math.Max(1, p.MechTrust); p.MechAngry = 0; return Dialog(2); }
                    if (ctx.HasPartner) return Gift();
                    Take(w, p, item, "use");
                    p.Drunk += DrunkPerDose;
                    return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { Sound = "gulps.raw" };
                case ItemId.Oil:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Globe) { Take(w, p, item, "use"); p.GlobeOiled = true; return R(ItemUseOutcome.Applied); }
                    return Gift();
                case ItemId.Pills:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (p.Sick) { Take(w, p, item, "use"); p.Sick = false; return R(ItemUseOutcome.Applied); }
                    return Gift();
                case ItemId.Postcard:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Personnel(p.Index)) { p.SeligTrust = true; return Dialog(300); }
                    return Gift();
                case ItemId.Spider:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Sabotage)
                    {
                        if (p.Sabotage.Trust != 0) { p.SpiderTrust = true; Take(w, p, item, "use"); }
                        return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { DialogPar = 3000 };
                    }
                    return Gift();
                case ItemId.Dart:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Werbung && (w.Difficulty >= 2 || w.Difficulty == Mission.FreeGame)) { p.WerbungTrust = true; return Dialog(8001); }
                    return Gift();
                case ItemId.Diskette:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (p.LaptopVirus != 0 && p.HasItem(ItemId.Laptop))
                    {
                        Take(w, p, item, "use");
                        p.LaptopVirus = 0;
                        return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { Screen = room == ItemRooms.Laptop ? "leave-laptop" : null };
                    }
                    return Gift();
                case ItemId.Bra:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.DutyFree) { p.DutyTrust = true; return Dialog(801); }
                    return Gift();
                case ItemId.Horseshoe:
                    if (KioskBlocks(w, room)) return R(ItemUseOutcome.Nothing);
                    if (free && room == ItemRooms.Ricks) { p.TrinkerTrust = true; return Dialog(800); }
                    return Gift();
                case ItemId.Chocolates:
                case ItemId.SpikedChocolates:
                    if (ctx.HasPartner) return Gift();
                    Take(w, p, item, "use");
                    if (item == ItemId.SpikedChocolates) p.Drunk += DrunkPerDose;
                    return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { Sound = "eating.raw" };
                case ItemId.PaperClip:
                    if (free && room == ItemRooms.Fracht && w.Items.GlueState == 0 && !p.HasItem(ItemId.Glue))
                    {
                        w.Items.GlueState = 1;
                        return Dialog(1020);
                    }
                    return Gift();
                case ItemId.Glue:
                    if (airport)
                    {
                        if (!Upper(w, p, ctx)) return new ItemUsed(p, item, room, ItemUseOutcome.Refused) { RequestId = RequestNoGlue };
                        if (PlaceGlue(w, p)) { Take(w, p, item, "use"); return R(ItemUseOutcome.Applied); }
                        return R(ItemUseOutcome.Nothing);
                    }
                    return Gift();
                case ItemId.RedBull:
                    if (room == ItemRooms.Kiosk) { p.KioskTrust = true; return Dialog(1010); }
                    if (ctx.HasPartner) return Gift();
                    Take(w, p, item, "use");
                    p.Caffeine += CaffeinePerDrink;
                    if (w.Hall != null) w.Hall.Players[p.Index].Koffein = p.Caffeine;
                    return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { Sound = "gulps.raw" };
                case ItemId.StinkBomb:
                    if (airport)
                    {
                        if (Upper(w, p, ctx)) return new ItemUsed(p, item, room, ItemUseOutcome.Refused) { RequestId = RequestNoStench };
                        PlaceStinkBomb(w, p);
                        Take(w, p, item, "use");
                        return R(ItemUseOutcome.Applied);
                    }
                    return Gift();
                case ItemId.GlowingCoal:
                    if (room == ItemRooms.LastMinute || room == ItemRooms.Reisebuero)
                    {
                        var board = room == ItemRooms.LastMinute ? w.LastMinute : w.TravelAgency;
                        int burnt = 0;
                        for (int c = board.Offers.Length - 1; c >= 0; c--)
                            if (board.Offers[c] != null && board.Offers[c].Premium > 0) { board.Offers[c].Premium = 0; burnt++; }
                        return new ItemUsed(p, item, room, ItemUseOutcome.Applied) { Sound = burnt > 0 ? "fireauft.raw" : null };
                    }
                    return Gift();
                case ItemId.Pliers:
                    if (ctx.HasPartner) return Gift();
                    if (airport && NearSecurity(w, p, ctx))
                    {
                        Take(w, p, item, "use");
                        PowerOutage(w, p);
                        return R(ItemUseOutcome.Applied);
                    }
                    return R(ItemUseOutcome.Nothing);
                case ItemId.Perfume:
                    if (room == ItemRooms.Werkstatt && w.Items.Slimed != -1)
                    {
                        while (p.Items.Replace(ItemId.Perfume, ItemId.StinkPerfume)) { }
                        w.Events.Publish(new InventoryChanged(p, ItemId.StinkPerfume, true, "use"));
                        return R(ItemUseOutcome.Applied);
                    }
                    return Gift();
                case ItemId.StinkPerfume:
                    if (airport) { p.Stench = StenchUnits; Take(w, p, item, "use"); return R(ItemUseOutcome.Applied); }
                    return R(ItemUseOutcome.Nothing);
                case ItemId.Glove:
                case ItemId.Cigar:
                case ItemId.Coal:
                    // The original answers cigar and coal with the coal reaction line.
                    if (!ctx.HasPartner) return R(ItemUseOutcome.Nothing);
                    return new ItemUsed(p, item, room, ItemUseOutcome.Gift) { DialogPar = 10000 + (item == ItemId.Glove ? ItemId.Glove : ItemId.Coal) };
            }
            return R(ItemUseOutcome.Nothing);
        }

        // ================================================================== hall effects

        static bool Upper(World w, Player p, ItemUseContext ctx)
        {
            var me = w.Hall?.PlayerPerson(p.Index);
            if (me != null) return me.Position.Y >= 4000;
            return ctx.UpperFloor ?? false;
        }

        static bool NearSecurity(World w, Player p, ItemUseContext ctx)
        {
            var me = w.Hall?.PlayerPerson(p.Index);
            if (me != null) return w.Hall.RuneParNear(me.Position, new Pt(22, 22), RuneId.ToShop) == ItemRooms.Security;
            return ctx.NearSecurityDoor ?? false;
        }

        static int AnimationClan(HallState h, int group)
        {
            var clans = h.Data.Clans;
            for (int c = 0; c < clans.Length; c++)
                if (clans[c] != null && clans[c].Type == ClanType.Animation && clans[c].Group == group) return c;
            return -1;
        }

        /// <summary>
        /// Glue (SIM::AddGlueSabotage): only while the boss stands still; the floor tile in front (facing
        /// 0 N, 1 E, 2 S, 3 W) if that exit is open. Without a hall model the glue is always placed.
        /// </summary>
        public static bool PlaceGlue(World w, Player p)
        {
            var h = w.Hall;
            var me = h?.PlayerPerson(p.Index);
            if (me == null) { w.Events.Publish(new FloorSabotagePlaced(p, ItemId.Glue, -1, -1)); return true; }
            var hp = h.Players[p.Index];
            if (me.Dir != 8 || hp.NewDir != 8) return false;
            int x = me.Position.X / 44, y = (me.Position.Y - 5000) / 22;
            int plate = h.P(x, y);
            switch (me.Phase)
            {
                case 0: if ((plate & Plate.ExitN) == 0) return false; y--; break;
                case 1: if ((plate & Plate.ExitE) == 0) return false; x++; break;
                case 2: if ((plate & Plate.ExitS) == 0) return false; y++; break;
                case 3: if ((plate & Plate.ExitW) == 0) return false; x--; break;
                default: return false;
            }
            int clan = AnimationClan(h, 10);
            if (clan >= 0) h.AddPerson(h.CreatePerson(clan, new Pt(x * 44, y * 22 + 5000), Hall.Reason.Shopping, -1, -1, -1, Mood.None));
            w.Events.Publish(new FloorSabotagePlaced(p, ItemId.Glue, x, y));
            return true;
        }

        /// <summary>Stink bomb (SIM::AddStenchSabotage): a stink cloud at the boss's feet for 3960 ticks.</summary>
        public static void PlaceStinkBomb(World w, Player p)
        {
            var h = w.Hall;
            var me = h?.PlayerPerson(p.Index);
            if (me == null) { w.Events.Publish(new FloorSabotagePlaced(p, ItemId.StinkBomb, -1, -1)); return; }
            int clan = AnimationClan(h, 20);
            if (clan >= 0)
            {
                var cloud = h.CreatePerson(clan, me.Position, Hall.Reason.Shopping, -1, -1, -1, Mood.None);
                cloud.WaitCount = StinkBombTicks;
                h.AddPerson(cloud);
            }
            w.Events.Publish(new FloorSabotagePlaced(p, ItemId.StinkBomb, me.Position.X / 44, me.Position.Y / 22 + 5));
        }

        /// <summary>Pliers at the security office (§2.5): every airline's protections off, office closed 3 days.</summary>
        public static void PowerOutage(World w, Player by)
        {
            w.Items.SecurityOutageDays = 3;
            foreach (var q in w.Players)
                if (!q.IsOut && q.SecurityFlags != 0) { q.SecurityFlags = 0; w.Events.Publish(new SecurityChanged(q, 0)); }
            w.Events.Publish(new SecurityPowerOutage(by, 3));
        }

        /// <summary>
        /// Walking into a rival's intact office while carrying the pliers ("wrong room", rooms-and-ui-9 §5.3 a):
        /// the pliers are used up and that office's power is cut for the day.
        /// </summary>
        public static bool CutOfficePower(World w, Player by, Player victim)
        {
            if (victim == by || victim.OfficeState != 0 || !by.HasItem(ItemId.Pliers)) return false;
            Take(w, by, ItemId.Pliers, "use");
            victim.OfficeState = 3;
            w.Items.OfficePowerCutToday = true;
            w.Events.Publish(new OfficePowerCut(by, victim));
            return true;
        }

        /// <summary>
        /// Offering chocolates to a rival boss in a face-to-face talk (§3.4): robot sympathy +10 (normal) or
        /// −10 (spiked); a human rival gets drunk from spiked ones. The box is consumed.
        /// </summary>
        public static void OfferChocolates(World w, Player p, Player rival)
        {
            // The normal box is tested first (the original's order); the result is clamped to ±1000 like the dialog does.
            int item = p.HasItem(ItemId.Chocolates) ? ItemId.Chocolates : ItemId.SpikedChocolates;
            if (!Take(w, p, item, "gift")) return;
            if (rival.IsComputer)
            {
                var sym = w.Rules.Get<IRivalSympathy>();
                int before = sym.Get(w, rival.Index, p.Index);
                int after = System.Math.Max(-1000, System.Math.Min(1000, before + (item == ItemId.SpikedChocolates ? -10 : 10)));
                sym.Change(w, rival.Index, p.Index, after - before);
            }
            else if (item == ItemId.SpikedChocolates) rival.Drunk += DrunkPerDose;
        }
    }
}
