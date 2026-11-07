namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Security subscription bits of <see cref="Player.SecurityFlags"/> (items-security-special.md §2.2,
    /// economy.md §14). The nightly fees are booked by finance (<see cref="Player.SecurityCosts"/>).
    /// </summary>
    public static class SecurityBits
    {
        public const int Office = 0, Laptop = 1, Personnel = 2, Bank = 3, RouteBoard = 4, Administration = 5;
        public const int PlaneInterior = 6, PlaneExterior = 7, Gate = 8, GateLevel2 = 10, GateLevel3 = 11;
        /// <summary>Rows of the security menu (Misc 8000..8008); row 8 is the gate level cycle.</summary>
        public const int Rows = 9;
        public const int GateMask = (1 << Gate) | (1 << GateLevel2) | (1 << GateLevel3);

        public static bool Has(int flags, int bit) => bit >= 0 && (flags & (1 << bit)) != 0;

        /// <summary>Gate scanner level 0..3 from the flags.</summary>
        public static int GateLevel(int flags) =>
            Has(flags, GateLevel3) ? 3 : Has(flags, GateLevel2) ? 2 : Has(flags, Gate) ? 1 : 0;

        /// <summary>
        /// One click on a menu row (§2.1): rows 0..7 toggle their bit; the gate row cycles
        /// off → L1 (bit 8) → L2 (+bit 10) → L3 (+bit 11) → off. The laptop row needs a laptop.
        /// </summary>
        public static int Toggle(int flags, int row, bool ownsLaptop)
        {
            if (row < 0 || row >= Rows) return flags;
            if (row == Laptop && !ownsLaptop) return flags;
            if (row != Gate) return flags ^ (1 << row);
            if (Has(flags, GateLevel3)) return flags & ~GateMask;
            if (Has(flags, GateLevel2)) return flags | (1 << GateLevel3);
            if (Has(flags, Gate)) return flags | (1 << GateLevel2);
            return flags | (1 << Gate);
        }
    }
}
