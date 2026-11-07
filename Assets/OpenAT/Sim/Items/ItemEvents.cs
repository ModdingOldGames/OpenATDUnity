using OpenAT.Sim.Core;

namespace OpenAT.Sim.Items
{
    /// <summary>An item entered or left an inventory. <see cref="Source"/>: "shop", "found", "dialog", "use", "drop", ...</summary>
    public sealed class InventoryChanged : ISimEvent
    {
        public readonly Player Player;
        public readonly int Item;
        public readonly bool Added;
        public readonly string Source;
        public InventoryChanged(Player p, int item, bool added, string source) { Player = p; Item = item; Added = added; Source = source; }
    }

    /// <summary>How using an item turned out (rooms-and-ui-1 §9.4).</summary>
    public enum ItemUseOutcome
    {
        /// <summary>No effect here and no partner: nothing happens.</summary>
        Nothing,
        /// <summary>The room's dialog partner reacts with the canned line (dialog opening <see cref="ItemUsed.DialogPar"/> = 10000 + item).</summary>
        Gift,
        /// <summary>The item's special effect was applied (it may open a dialog, see <see cref="ItemUsed.DialogPar"/>).</summary>
        Applied,
        /// <summary>Not possible here: show the request popup <see cref="ItemUsed.RequestId"/> (e.g. glue below / stink bomb above).</summary>
        Refused,
        /// <summary>Open a screen: laptop room, phone book, filofax (<see cref="ItemUsed.Screen"/>).</summary>
        OpenScreen,
    }

    /// <summary>
    /// Result of <see cref="UseItemCommand"/> for the view: dialog to start (talker by room, opening
    /// <see cref="DialogPar"/>), popup, screen, sound (<see cref="Sound"/>: "gulps.raw", "eating.raw", "fireauft.raw").
    /// </summary>
    public sealed class ItemUsed : ISimEvent
    {
        public readonly Player Player;
        public readonly int Item;
        public readonly int Room;
        public readonly ItemUseOutcome Outcome;
        public int DialogPar;
        public int RequestId;
        /// <summary>"laptop", "phonebook", "filofax", "office-phone".</summary>
        public string Screen;
        public string Sound;
        public ItemUsed(Player p, int item, int room, ItemUseOutcome o) { Player = p; Item = item; Room = room; Outcome = o; }
    }

    /// <summary>A shop purchase or refusal at the duty-free shop (rooms-and-ui-7 §9).</summary>
    public sealed class ItemBought : ISimEvent
    {
        public readonly Player Player;
        public readonly int Item;
        public readonly long Price;
        public ItemBought(Player p, int item, long price) { Player = p; Item = item; Price = price; }
    }

    /// <summary>A prank was ordered (and paid) at the saboteur.</summary>
    public sealed class PrankOrdered : ISimEvent
    {
        public readonly Player Saboteur;
        public readonly Player Victim;
        public readonly PrankFamily Family;
        public readonly int Mode;
        public readonly long Price;
        public PrankOrdered(Player s, Player v, PrankFamily f, int mode, long price) { Saboteur = s; Victim = v; Family = f; Mode = mode; Price = price; }
    }

    /// <summary>
    /// A prank went off. View: saboteur's call (Sabo 2000) for the local saboteur; letter <c>Lett 500 + mode</c>
    /// and fax/advisor (<c>Advi 2301 + mode</c>) for a local victim of a plane prank; explosion etc.
    /// </summary>
    public sealed class PrankExecuted : ISimEvent
    {
        public readonly SabotageAct Act;
        public readonly Player Saboteur;
        public readonly Player Victim;
        /// <summary>Victim plane name (plane pranks, impound), or null.</summary>
        public readonly string PlaneName;
        public PrankExecuted(SabotageAct act, Player saboteur, Player victim, string planeName) { Act = act; Saboteur = saboteur; Victim = victim; PlaneName = planeName; }
    }

    /// <summary>The boss names a culprit and fines him (Boss 2073/2074).</summary>
    public sealed class SabotageFined : ISimEvent
    {
        public readonly Player Culprit;
        public readonly Player Victim;
        public readonly long Fine;
        public SabotageFined(Player c, Player v, long fine) { Culprit = c; Victim = v; Fine = fine; }
    }

    /// <summary>The bomb victim walks to his office (camera follows him, rooms-and-ui-6 §3.6).</summary>
    public sealed class OfficeBombArmed : ISimEvent
    {
        public readonly Player Victim;
        public OfficeBombArmed(Player v) { Victim = v; }
    }

    /// <summary>The office bomb went off (expl.smk, explode.raw); the office is unusable for the day.</summary>
    public sealed class OfficeExploded : ISimEvent
    {
        public readonly Player Victim;
        public OfficeExploded(Player v) { Victim = v; }
    }

    /// <summary>Someone's office power was cut with the pliers (dark overlay for the day).</summary>
    public sealed class OfficePowerCut : ISimEvent
    {
        public readonly Player By, Victim;
        public OfficePowerCut(Player by, Player victim) { By = by; Victim = victim; }
    }

    /// <summary>The pliers were used at the security office: all protections off, closed for <see cref="Days"/>.</summary>
    public sealed class SecurityPowerOutage : ISimEvent
    {
        public readonly Player By;
        public readonly int Days;
        public SecurityPowerOutage(Player by, int days) { By = by; Days = days; }
    }

    /// <summary>Security subscriptions changed (menu click or outage).</summary>
    public sealed class SecurityChanged : ISimEvent
    {
        public readonly Player Player;
        public readonly int Flags;
        public SecurityChanged(Player p, int flags) { Player = p; Flags = flags; }
    }

    /// <summary>The coffee prank sends the boss to the toilet (WC room id).</summary>
    public sealed class ToiletRun : ISimEvent
    {
        public readonly Player Player;
        public readonly int Room;
        public ToiletRun(Player p, int room) { Player = p; Room = room; }
    }

    /// <summary>A rocket or station part was bought (fanfare, salesman button clip; Nasa 5099 / 8302).</summary>
    public sealed class SpacePartBought : ISimEvent
    {
        public readonly Player Player;
        public readonly int Bit;
        public readonly long Price;
        public SpacePartBought(Player p, int bit, long price) { Player = p; Bit = bit; Price = price; }
    }

    /// <summary>Laptop battery warning (2 minutes left) or empty (planner inert).</summary>
    public sealed class LaptopBatteryLow : ISimEvent
    {
        public readonly Player Player;
        public readonly int Left;
        public LaptopBatteryLow(Player p, int left) { Player = p; Left = left; }
    }

    /// <summary>Glue or a stink cloud was placed on the hall floor (hall tile, upper floor flag).</summary>
    public sealed class FloorSabotagePlaced : ISimEvent
    {
        public readonly Player Player;
        public readonly int Item;
        public readonly int TileX, TileY;
        public FloorSabotagePlaced(Player p, int item, int x, int y) { Player = p; Item = item; TileX = x; TileY = y; }
    }
}
