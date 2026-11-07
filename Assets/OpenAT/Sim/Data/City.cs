using OpenAT.Sim.Core;

namespace OpenAT.Sim.Data
{
    /// <summary>A city from data/city.csv (docs/spec/findings/data-tables.md §1.1).</summary>
    public sealed class City : IContent
    {
        public const int IdBase = 0x1000000;

        /// <summary>Runtime id (csv id + 0x1000000).</summary>
        public int Id { get; set; }
        /// <summary>Position in the table (= row order). Several systems index by it.</summary>
        public int Index;
        public string Name;
        public string Region;
        /// <summary>1 Europe, 2 Americas, 3 Africa/India, 4 Asia/Oceania.</summary>
        public int AreaCode;
        /// <summary>The active 3-letter code shown in the game: <see cref="IataCode"/> when the "real codes"
        /// option is on (the original's default), otherwise <see cref="GoodCode"/> (see GameData.UseRealCodes).</summary>
        public string ShortCode;
        /// <summary>The "readable" code from city.csv column 5 (e.g. a city-name abbreviation).</summary>
        public string GoodCode;
        /// <summary>The real airport code from city.csv column 6.</summary>
        public string IataCode;
        /// <summary>Voice sample base name, or null.</summary>
        public string Wave;
        public int TextResBase, TextCount;
        public string PhotoName;
        public int PhotoCount;
        public int Population;
        /// <summary>Globe position in whole degrees: x = longitude (east +), y = latitude (north −).</summary>
        public int GlobeX, GlobeY;
        /// <summary>Flat map position (same units, nudged for labels).</summary>
        public int MapX, MapY;
        /// <summary>Monthly branch office rent.</summary>
        public int OfficeRent;
        /// <summary>0 base game, 1 first add-on, 2 Deluxe.</summary>
        public int NewInAddOn;

        public override string ToString() => Name;
    }
}
