using OpenAT.Sim.Items;

namespace OpenAT.Sim
{
    /// <summary>
    /// Carried items, item-chain trust flags, prank state and the boss's own condition (drunk, caffeine,
    /// sick, ...) of an airline (items-security-special.md §1, §3). Changed only through the item and
    /// sabotage commands and the items systems (Sim/Items).
    /// </summary>
    public sealed partial class Player
    {
        /// <summary>The six inventory slots, read by the status bar (0xFF = empty).</summary>
        public readonly Inventory Items = new Inventory();

        // ---- laptop (§3.2, §3.5)
        /// <summary>Laptop model 0 (none bought yet) .. 4.</summary>
        public int LaptopQuality;
        /// <summary>Battery left today in game minutes (40/80/200/1440 by model, refilled at night).</summary>
        public int LaptopBattery;
        /// <summary>0 clean, 1 infected, 2 handed to the duty-free saleswoman, 3 repaired and waiting there.</summary>
        public int LaptopVirus;
        /// <summary>The portable planner is open (battery drains 1 per game minute). Set by the view.</summary>
        public bool LaptopOpen;

        // ---- trade-chain trust (§3.3)
        public bool DutyTrust, TrinkerTrust, SpiderTrust, WerbungTrust, SeligTrust;
        /// <summary>Energy drink given to the kiosk (one stink bomb; reset when it is handed out).</summary>
        public bool KioskTrust;
        /// <summary>Workshop mechanic is angry (0 calm, 1 warned, 2 throws out); beer calms him.</summary>
        public int MechAngry;
        /// <summary>The office globe has been oiled (no squeak).</summary>
        public bool GlobeOiled;
        /// <summary>The NASA salesman's farewell was shown (first exit attempt).</summary>
        public bool TalkedToNasa;

        // ---- the boss's condition
        /// <summary>Drunk: mouse wobble units, −1 per sim step.</summary>
        public int Drunk;
        /// <summary>Energy drink: extra walking steps while &gt; 0 (2400 ticks), −1 per sim step in office hours.</summary>
        public int Caffeine;
        /// <summary>Stink perfume: the boss stinks (2000 units, −1 per sim step).</summary>
        public int Stench;
        /// <summary>Bad coffee prank: forced toilet runs for the day ("SickTokay").</summary>
        public bool Sick;

        // ---- sabotage day effects on this airline (§1.3)
        /// <summary>Phones dead today (special 2): no mobile phone, cannot be called.</summary>
        public bool PhonesDown;
        /// <summary>Fake brochures today (special 1): airline whose image gets this airline's flight image, or −1.</summary>
        public int BrochureFrom = -1;
        /// <summary>Press release today (special 3): route flights book 1 + n mod 3 passengers per class.</summary>
        public bool PressStatement;
        /// <summary>Office: 0 normal, 1 bomb planted, 2 exploded (rest of the day), 3 power cut (pliers).</summary>
        public int OfficeState;

        /// <summary>Dealings with the saboteur as the ordering airline.</summary>
        public readonly SabotageState Sabotage = new SabotageState();
        /// <summary>Security flags this airline has needed (bits of pranks ordered against it; robots buy only those).</summary>
        public int SecurityNeeded;

        /// <summary>Owns the item.</summary>
        public bool HasItem(int item) => Items.Has(item);
    }
}
