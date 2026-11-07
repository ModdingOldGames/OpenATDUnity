using System;
using OpenAT.Formats;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// The 26 carried item types (rooms-and-ui-1 §9.4; items-security-special.md §3). The value is the
    /// original's item id: icon <c>ITEMnn</c>, tooltip <c>2000 + id</c>, "what is that for" reaction
    /// <c>10000 + id</c>. <see cref="None"/> (0xFF) marks an empty inventory slot.
    /// </summary>
    public static class ItemId
    {
        public const int Laptop = 0, Phone = 1, Filofax = 2, MachineGun = 3, Beer = 4, Cigar = 5, Oil = 6,
            Postcard = 7, Pills = 8, Spider = 9, Dart = 10, Diskette = 11, Bra = 12, Horseshoe = 13,
            Chocolates = 14, SpikedChocolates = 15, PaperClip = 16, Glue = 17, Glove = 18, RedBull = 19,
            StinkBomb = 20, GlowingCoal = 21, Coal = 22, Pliers = 23, Perfume = 24, StinkPerfume = 25;

        public const int Count = 26;
        public const int None = 0xFF;

        public static bool IsValid(int id) => id >= 0 && id < Count;
    }

    /// <summary>
    /// Room ids used by item, prank and special-room rules (rooms-and-ui-9 §2). Hall rooms share the
    /// numbers of <see cref="Hall.RoomIds"/>; the pseudo rooms (globe, laptop) are listed here.
    /// </summary>
    public static class ItemRooms
    {
        public const int Airport = 1, Globe = 6, Laptop = 7;
        public const int OfficeA = 10, PersonnelA = 11;
        public const int DutyFree = 80, Ricks = 84, RouteBox = 85, Bank = 100, Kiosk = 101, LastMinute = 103, ArabAir = 104;
        public const int Makler = 105, Aufsicht = 107, Werbung = 109, Werkstatt = 110, Reisebuero = 111, Sabotage = 112;
        public const int WcF = 113, WcM = 114, Nasa = 115, Insel = 116, Rushmore = 118, Fracht = 121, Weltall = 122, Security = 123;

        public static int Office(int player) => OfficeA + 10 * player;
        public static int Personnel(int player) => PersonnelA + 10 * player;
        public static bool IsAirport(int room) => room == 0 || room == Airport;
        /// <summary>Owner of an office room 10/20/30/40, or −1.</summary>
        public static int OfficeOwner(int room) => room >= 10 && room <= 40 && room % 10 == 0 ? room / 10 - 1 : -1;
    }

    /// <summary>
    /// Shop prices (items-security-special.md §3.2, economy.md §15.2). The original reads them from the
    /// text resource (<c>Item 2000 + n</c>, names <c>Item 1000 + n</c>, n = laptop quality 0..3 or the
    /// duty-free dialog id 400/500/600/700/801); the defaults below are the values of the installed
    /// resource and are replaced by <see cref="Load"/> when the install's numbers parse.
    /// </summary>
    public sealed class ItemCatalog
    {
        /// <summary>Price text ids of the shop goods (n of <c>Item 2000 + n</c>).</summary>
        public const int MachineGunPriceId = 400, FilofaxPriceId = 500, PhonePriceId = 600, BeerPriceId = 700, ChocolatesPriceId = 801;

        /// <summary>Laptop model price by current quality 0..3 (the purchase upgrades to quality + 1).</summary>
        public readonly long[] LaptopPrice = { 10000, 30000, 50000, 100000 };
        public long MachineGunPrice = 80000, FilofaxPrice = 5000, PhonePrice = 300000, BeerPrice = 5000, ChocolatesPrice = 10000;

        /// <summary>Daily laptop battery in game minutes by quality 0..4 (sim-core.md §2.7 step 10).</summary>
        public static readonly int[] LaptopBattery = { 0, 40, 80, 200, 1440 };

        /// <summary>Reads the prices from <c>Item 2000 + n</c> of the standard texts (numbers only).</summary>
        public void Load(TextResource std)
        {
            if (std == null) return;
            for (int q = 0; q < 4; q++) LaptopPrice[q] = Read(std, q, LaptopPrice[q]);
            MachineGunPrice = Read(std, MachineGunPriceId, MachineGunPrice);
            FilofaxPrice = Read(std, FilofaxPriceId, FilofaxPrice);
            PhonePrice = Read(std, PhonePriceId, PhonePrice);
            BeerPrice = Read(std, BeerPriceId, BeerPrice);
            ChocolatesPrice = Read(std, ChocolatesPriceId, ChocolatesPrice);
        }

        static long Read(TextResource std, int n, long fallback)
        {
            string s;
            try { s = std.Get("Item", 2000 + n); } catch (Exception) { return fallback; }
            return ParseNumber(s, fallback);
        }

        /// <summary>Integer value of a price text: its digits (thousand separators ignored), like the original's atoi of the text.</summary>
        public static long ParseNumber(string s, long fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            long v = 0;
            bool any = false;
            foreach (char ch in s)
            {
                if (ch >= '0' && ch <= '9') { v = v * 10 + (ch - '0'); any = true; if (v > 1000000000000L) return fallback; }
                else if (ch == '.' || ch == ',' || ch == ' ' || ch == '\'') continue;
                else if (any) break;
            }
            return any ? v : fallback;
        }

        /// <summary>Price of a duty-free good by its item id, or −1 when it is not sold directly.</summary>
        public long DirectPrice(int item)
        {
            switch (item)
            {
                case ItemId.MachineGun: return MachineGunPrice;
                case ItemId.Filofax: return FilofaxPrice;
                case ItemId.Phone: return PhonePrice;
                case ItemId.Beer: return BeerPrice;
                case ItemId.Chocolates: case ItemId.SpikedChocolates: return ChocolatesPrice;
            }
            return -1;
        }

        /// <summary>Statement text id (<c>Item 1000 + n</c>) of a duty-free good.</summary>
        public static int LedgerTextId(int item)
        {
            switch (item)
            {
                case ItemId.MachineGun: return 1000 + MachineGunPriceId;
                case ItemId.Filofax: return 1000 + FilofaxPriceId;
                case ItemId.Phone: return 1000 + PhonePriceId;
                case ItemId.Beer: return 1000 + BeerPriceId;
                case ItemId.Chocolates: case ItemId.SpikedChocolates: return 1000 + ChocolatesPriceId;
            }
            return 0;
        }
    }
}
