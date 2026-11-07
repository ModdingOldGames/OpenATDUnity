using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Items
{
    /// <summary>Base for an airline's item / prank / security command: player index and out check.</summary>
    public abstract class ItemPlayerCommand : ICommand<World>
    {
        public readonly int PlayerIndex;
        protected ItemPlayerCommand(int player) { PlayerIndex = player; }

        public CommandResult Validate(World w)
        {
            if (PlayerIndex < 0 || PlayerIndex >= Player.Count) return CommandResult.Fail("bad-player");
            var p = w.Players[PlayerIndex];
            if (p.IsOut) return CommandResult.Fail("player-out");
            return Check(w, p);
        }

        public void Apply(World w) => Run(w, w.Players[PlayerIndex]);
        protected abstract CommandResult Check(World w, Player p);
        protected abstract void Run(World w, Player p);
    }

    // ====================================================================== inventory

    /// <summary>
    /// Clicks an inventory slot (rooms-and-ui-1 §9.4). The outcome (dialog to open, popup, screen, sound) is
    /// published as <see cref="ItemUsed"/> and kept in <see cref="Result"/>. Not while the clock is stopped.
    /// </summary>
    public sealed class UseItemCommand : ItemPlayerCommand
    {
        public readonly int Slot;
        public readonly ItemUseContext Context;
        public ItemUsed Result { get; private set; }
        public UseItemCommand(int player, int slot, ItemUseContext context) : base(player) { Slot = slot; Context = context; }

        protected override CommandResult Check(World w, Player p)
        {
            if (Slot < 0 || Slot >= Inventory.Slots || p.Items[Slot] == ItemId.None) return CommandResult.Fail("empty-slot");
            if (w.Clock.Frozen) return CommandResult.Fail("no-time");
            return CommandResult.Success;
        }

        protected override void Run(World w, Player p)
        {
            Result = ItemEngine.Use(w, p, p.Items[Slot], Context);
            w.Events.Publish(Result);
        }
    }

    /// <summary>Discards an item (right click, after the confirmation popup).</summary>
    public sealed class DropItemCommand : ItemPlayerCommand
    {
        public readonly int Slot;
        public DropItemCommand(int player, int slot) : base(player) { Slot = slot; }
        protected override CommandResult Check(World w, Player p) =>
            Slot < 0 || Slot >= Inventory.Slots || p.Items[Slot] == ItemId.None ? CommandResult.Fail("empty-slot") : CommandResult.Success;
        protected override void Run(World w, Player p)
        {
            int item = p.Items.RemoveAt(Slot);
            w.Events.Publish(new InventoryChanged(p, item, false, "drop"));
        }
    }

    /// <summary>
    /// Takes an item lying in a room (glove, paper clip, glue, perfume, pliers, postcard, glowing coal, bra,
    /// spider, oil can; items-security-special.md §3.3). Glowing coal: only while some office is destroyed.
    /// </summary>
    public sealed class PickUpItemCommand : ItemPlayerCommand
    {
        public readonly int Item;
        public PickUpItemCommand(int player, int item) : base(player) { Item = item; }

        protected override CommandResult Check(World w, Player p)
        {
            if (!p.Items.HasSpace) return CommandResult.Fail("inventory-full");
            var wi = w.Items;
            switch (Item)
            {
                case ItemId.Glove: return wi.Glove && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.PaperClip: return wi.PaperClip && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Glue:
                    if (wi.GlueState == 0) return CommandResult.Fail("fracht-1010");
                    return wi.GlueState == 1 && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Perfume: return wi.Perfume ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Pliers: return wi.Pliers ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Postcard: return wi.Postcard && !p.SeligTrust && w.Difficulty != Mission.Tutorial ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.GlowingCoal:
                    bool bombed = false;
                    foreach (var q in w.Players) if (!q.IsOut && q.OfficeState == 2) bombed = true;
                    return bombed && wi.GlowingCoal && p.Owner == Owner.Human ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Bra:
                    return !p.HasItem(ItemId.Bra) && !p.HasItem(ItemId.Horseshoe) && !p.TrinkerTrust && w.Difficulty != Mission.Tutorial
                        ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Spider: return !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("not-there");
                case ItemId.Oil:
                    if (p.MechTrust == 0) return CommandResult.Fail("mech-20");
                    return !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("not-there");
            }
            return CommandResult.Fail("not-a-found-item");
        }

        protected override void Run(World w, Player p) => ItemEngine.TryPickUp(w, p, Item);
    }

    /// <summary>
    /// A trade-chain reward handed out in a dialog (horseshoe, dart, diskette, pills, stink bomb) or the
    /// vending machine turning the glove into Red Bull (items-security-special.md §3.3).
    /// </summary>
    public sealed class ReceiveRewardCommand : ItemPlayerCommand
    {
        public readonly int Item;
        public ReceiveRewardCommand(int player, int item) : base(player) { Item = item; }

        protected override CommandResult Check(World w, Player p)
        {
            if (Item == ItemId.RedBull) return p.HasItem(ItemId.Glove) ? CommandResult.Success : CommandResult.Fail("no-glove");
            if (!p.Items.HasSpace) return CommandResult.Fail("inventory-full");
            switch (Item)
            {
                case ItemId.Horseshoe: return p.DutyTrust && !p.HasItem(Item) && !p.TrinkerTrust ? CommandResult.Success : CommandResult.Fail("no-trust");
                case ItemId.Dart:
                    return p.SpiderTrust && p.Sabotage.Trust > 0 && !p.HasItem(ItemId.Dart) && !p.HasItem(ItemId.Diskette) && !p.WerbungTrust
                        ? CommandResult.Success : CommandResult.Fail("no-trust");
                case ItemId.Diskette: return p.WerbungTrust && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("no-trust");
                case ItemId.Pills: return p.SeligTrust && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("no-trust");
                case ItemId.StinkBomb: return p.KioskTrust && !p.HasItem(Item) ? CommandResult.Success : CommandResult.Fail("no-trust");
            }
            return CommandResult.Fail("not-a-reward");
        }

        protected override void Run(World w, Player p) => ItemEngine.TryReward(w, p, Item);
    }

    /// <summary>The vending machine without a glove: electric shock (cosmetic smoke), sobers the boss up.</summary>
    public sealed class UseVendingMachineCommand : ItemPlayerCommand
    {
        /// <summary>True when the glove became Red Bull, false for the shock.</summary>
        public bool GotRedBull { get; private set; }
        public UseVendingMachineCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p)
        {
            GotRedBull = ItemEngine.TryReward(w, p, ItemId.RedBull);
            if (!GotRedBull) p.Drunk = 0;
        }
    }

    /// <summary>Offers the chocolates to a rival boss in a face-to-face talk (items-security-special.md §3.4).</summary>
    public sealed class OfferChocolatesCommand : ItemPlayerCommand
    {
        public readonly int Rival;
        public OfferChocolatesCommand(int player, int rival) : base(player) { Rival = rival; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Rival < 0 || Rival >= Player.Count || Rival == p.Index) return CommandResult.Fail("bad-rival");
            return p.HasItem(ItemId.Chocolates) || p.HasItem(ItemId.SpikedChocolates) ? CommandResult.Success : CommandResult.Fail("no-chocolates");
        }
        protected override void Run(World w, Player p) => ItemEngine.OfferChocolates(w, p, w.Players[Rival]);
    }

    /// <summary>Opens / closes the portable planner (battery drains while open). Set by the laptop room.</summary>
    public sealed class SetLaptopOpenCommand : ItemPlayerCommand
    {
        public readonly bool Open;
        public SetLaptopOpenCommand(int player, bool open) : base(player) { Open = open; }
        protected override CommandResult Check(World w, Player p) =>
            !Open || p.HasItem(ItemId.Laptop) ? CommandResult.Success : CommandResult.Fail("no-laptop");
        protected override void Run(World w, Player p) => p.LaptopOpen = Open;
    }

    // ====================================================================== duty free (rooms-and-ui-7 §9)

    /// <summary>Buys a duty-free good: machine gun, filofax, mobile phone, beer, chocolates (14) or spiked chocolates (15).</summary>
    public sealed class BuyDutyFreeCommand : ItemPlayerCommand
    {
        public readonly int Item;
        public BuyDutyFreeCommand(int player, int item) : base(player) { Item = item; }
        protected override CommandResult Check(World w, Player p)
        {
            if (w.Items.Catalog.DirectPrice(Item) < 0) return CommandResult.Fail("not-sold");
            if (!ItemEngine.Offered(w, p, Item)) return CommandResult.Fail("not-offered");
            if (!p.Items.HasSpace) return CommandResult.Fail("inventory-full");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => ItemEngine.BuyDirect(w, p, Item);
    }

    /// <summary>
    /// The laptop counter: buys / upgrades the laptop (one sale per day for the airport, from day 8).
    /// Refusals: "duty-3" (locked, Duty 97/98 with 8 − date days), "duty-1" (sold today, Duty 99/100),
    /// "virus" (use <see cref="LaptopRepairCommand"/>), "max-model", "inventory-full" (FixLaptopFullInventory).
    /// </summary>
    public sealed class BuyLaptopCommand : ItemPlayerCommand
    {
        public BuyLaptopCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p)
        {
            switch (ItemEngine.LaptopState(w, p))
            {
                case ItemEngine.LaptopOffer.VirusDialog: return CommandResult.Fail("virus");
                case ItemEngine.LaptopOffer.MaxModel: return CommandResult.Fail("max-model");
                case ItemEngine.LaptopOffer.Locked: return CommandResult.Fail("duty-3");
                case ItemEngine.LaptopOffer.SoldToday: return CommandResult.Fail("duty-1");
            }
            // The original charges a new laptop even when it cannot be handed over (all slots full).
            if (w.Options.FixLaptopFullInventory && !p.HasItem(ItemId.Laptop) && !p.Items.HasSpace)
                return CommandResult.Fail("inventory-full");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p) => ItemEngine.BuyLaptop(w, p);
    }

    /// <summary>Virus dialogs at the duty free (Duty 2000/2020): hand in the infected laptop, or collect the repaired one.</summary>
    public sealed class LaptopRepairCommand : ItemPlayerCommand
    {
        public LaptopRepairCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p)
        {
            if (p.LaptopVirus == 1 && p.HasItem(ItemId.Laptop)) return CommandResult.Success;
            if (p.LaptopVirus == 3) return p.Items.HasSpace ? CommandResult.Success : CommandResult.Fail("inventory-full");
            return CommandResult.Fail("nothing-to-repair");
        }
        protected override void Run(World w, Player p) => ItemEngine.LaptopRepair(w, p);
    }

    // ====================================================================== room events

    /// <summary>Walking into a rival's intact office carrying the pliers (rooms-and-ui-9 §5.3 a): power cut for the day.</summary>
    public sealed class CutOfficePowerCommand : ItemPlayerCommand
    {
        public readonly int Victim;
        public CutOfficePowerCommand(int player, int victim) : base(player) { Victim = victim; }
        protected override CommandResult Check(World w, Player p)
        {
            if (Victim < 0 || Victim >= Player.Count || Victim == p.Index) return CommandResult.Fail("bad-victim");
            if (!p.HasItem(ItemId.Pliers)) return CommandResult.Fail("no-pliers");
            return w.Players[Victim].OfficeState == 0 ? CommandResult.Success : CommandResult.Fail("office-not-intact");
        }
        protected override void Run(World w, Player p) => ItemEngine.CutOfficePower(w, p, w.Players[Victim]);
    }

    /// <summary>The bomb victim reached his office door: it explodes (office unusable for the rest of the day).</summary>
    public sealed class ExplodeOfficeCommand : ItemPlayerCommand
    {
        public ExplodeOfficeCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => p.OfficeState == 1 ? CommandResult.Success : CommandResult.Fail("no-bomb");
        protected override void Run(World w, Player p) => ItemSystems.Explode(w, p);
    }

    /// <summary>The workshop's slime animation finished (rooms-and-ui-7 §4): the floor is slimed until the next 6th day.</summary>
    public sealed class SetWorkshopSlimedCommand : ICommand<World>
    {
        public CommandResult Validate(World w) => CommandResult.Success;
        public void Apply(World w) => w.Items.Slimed = 2;
    }

    /// <summary>
    /// Talking to the drinker at Rick's with drinker trust while the own staff is on strike: the strike ends
    /// after a countdown of 10 game minutes (end type 3).
    /// </summary>
    public sealed class DrinkerEndsStrikeCommand : ItemPlayerCommand
    {
        public DrinkerEndsStrikeCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p)
        {
            if (!p.TrinkerTrust) return CommandResult.Fail("no-trust");
            return Staff.EndStrikeCommand.DrinkerCanEnd(p) ? CommandResult.Success : CommandResult.Fail("no-strike");
        }
        protected override void Run(World w, Player p) => Staff.EndStrikeCommand.DrinkerStarts(p);
    }

    /// <summary>Records the NASA farewell (first exit attempt shows Nasa 9999).</summary>
    public sealed class NasaFarewellCommand : ItemPlayerCommand
    {
        public NasaFarewellCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => p.TalkedToNasa ? CommandResult.Fail("already") : CommandResult.Success;
        protected override void Run(World w, Player p) => p.TalkedToNasa = true;
    }

    // ====================================================================== security (§2)

    /// <summary>One click on a row of the security menu (§2.1). Refused while the office is closed (power outage).</summary>
    public sealed class ToggleSecurityCommand : ItemPlayerCommand
    {
        public readonly int Row;
        public ToggleSecurityCommand(int player, int row) : base(player) { Row = row; }
        protected override CommandResult Check(World w, Player p)
        {
            if (w.Items.SecurityOutageDays > 0) return CommandResult.Fail("security-closed");
            if (Row < 0 || Row >= SecurityBits.Rows) return CommandResult.Fail("bad-row");
            if (Row == SecurityBits.Laptop && !p.HasItem(ItemId.Laptop)) return CommandResult.Fail("no-laptop");
            return CommandResult.Success;
        }
        protected override void Run(World w, Player p)
        {
            p.SecurityFlags = SecurityBits.Toggle(p.SecurityFlags, Row, p.HasItem(ItemId.Laptop));
            w.Events.Publish(new SecurityChanged(p, p.SecurityFlags));
        }
    }

    // ====================================================================== special rooms (§4)

    /// <summary>
    /// Buys a rocket or space-station part at NASA (§4.1; in person or by phone). Refusals carry the Nasa text:
    /// rocket "nasa-5098" owned, "nasa-5096/5097/5196/5197" prerequisite, "nasa-7000" money; station
    /// "nasa-8301" owned, "nasa-8300" money. The per-part price is charged (not the SDL port's bug).
    /// </summary>
    public sealed class BuySpacePartCommand : ItemPlayerCommand
    {
        public readonly int Bit;
        public BuySpacePartCommand(int player, int bit) : base(player) { Bit = bit; }

        protected override CommandResult Check(World w, Player p)
        {
            if (!SpaceParts.HasNasa(w.Difficulty)) return CommandResult.Fail("no-nasa");
            if (Bit < 0 || Bit >= SpaceParts.Parts) return CommandResult.Fail("bad-part");
            int flags = p.Counters.RocketFlags;
            long price = SpaceParts.Price(w.Difficulty, Bit);
            if (SpaceParts.IsRocketMission(w.Difficulty))
            {
                if ((flags & (1 << Bit)) != 0) return CommandResult.Fail("nasa-5098");
                int pre = SpaceParts.RocketPrerequisites(Bit);
                if ((flags & pre) != pre)
                {
                    int t = SpaceParts.RocketPrerequisiteText(Bit, flags);
                    return CommandResult.Fail(t != 0 ? "nasa-" + t : "not-offered");
                }
                if (p.Money < price) return CommandResult.Fail("nasa-7000");
            }
            else
            {
                if (!SpaceParts.StationOffered(Bit, flags)) return CommandResult.Fail((flags & (1 << Bit)) != 0 ? "nasa-8301" : "not-offered");
                if ((flags & (1 << Bit)) != 0) return CommandResult.Fail("nasa-8301");
                if (p.Money < price) return CommandResult.Fail("nasa-8300");
            }
            return CommandResult.Success;
        }

        protected override void Run(World w, Player p) => ItemSystems.AddSpacePart(w, p, Bit, SpaceParts.Price(w.Difficulty, Bit));
    }

    /// <summary>Looking through the telescope (island / space / Mount Rushmore): one coin (3300).</summary>
    public sealed class UseTelescopeCommand : ItemPlayerCommand
    {
        public UseTelescopeCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Run(World w, Player p) => p.Book(-1, MoneyReason.SpecialRoomEntry);
    }
}
