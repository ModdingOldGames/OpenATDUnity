using OpenAT.Sim.Core;

namespace OpenAT.Sim.Data
{
    /// <summary>A plane model from data/planetyp.csv (docs/spec/findings/data-tables.md §1.3).</summary>
    public sealed class PlaneType : IContent
    {
        public const int IdBase = 0x10000000;

        /// <summary>Runtime id (csv id + 0x10000000).</summary>
        public int Id { get; set; }
        /// <summary>The csv id (100..126).</summary>
        public int CsvId => Id - IdBase;
        /// <summary>Album slot: the first row is slot 29, the next 28, ... (back-filled album).</summary>
        public int Index;
        public string Manufacturer;
        public string Name;
        public string NotepadPhoto;
        public int PhotoCount;
        /// <summary>Mission (difficulty value) from which it is available, and day within it.</summary>
        public int FirstMission, FirstDay;
        public int FirstYearBuilt;
        public int Wingspan, Length, Height;
        public int TakeoffWeight;
        public int Seats;
        /// <summary>Cruise speed km/h.</summary>
        public int Speed;
        /// <summary>Range km.</summary>
        public int Range;
        public string Engines;
        public int Thrust;
        public int Pilots, Attendants;
        /// <summary>Tank size litres.</summary>
        public int TankSize;
        /// <summary>Fuel burn litres/hour.</summary>
        public int FuelBurn;
        public int Price;
        public double MaintenanceFactor;
        /// <summary>Description (original text, read at run time).</summary>
        public string Comment;

        /// <summary>Cargo payload per flight in tonnes.</summary>
        public int CargoTons => Seats / 10;

        public string FullName => Manufacturer + " " + Name;
        public override string ToString() => FullName;
    }
}
