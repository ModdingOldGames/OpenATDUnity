using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Data
{
    /// <summary>
    /// Static content tables loaded from the original data files (plus any extension content).
    /// Loaded once per session; the World keeps a reference. Parsing follows
    /// docs/spec/findings/data-tables.md.
    /// </summary>
    public sealed partial class GameData
    {
        public readonly Registry<City> Cities = new Registry<City>();
        public readonly Registry<PlaneType> PlaneTypes = new Registry<PlaneType>();
        /// <summary>Route template table; a world copies it (demand is per game).</summary>
        public readonly List<RouteDef> Routes = new List<RouteDef>();
        public readonly List<string> PlaneNamesPreferred = new List<string>();
        public readonly List<string> PlaneNamesFallback = new List<string>();

        public TextResource StdText;
        public TextResource DialogText;

        /// <summary>Loads from an install. Missing optional files are skipped.</summary>
        /// <summary>
        /// The original's "real codes" option (CITIES::UseRealKuerzel, default on): every city's active
        /// <see cref="City.ShortCode"/> becomes its real airport code, or its readable code when off.
        /// </summary>
        public void UseRealCodes(bool real)
        {
            foreach (var c in Cities.All)
            {
                if (c.GoodCode == null) c.GoodCode = c.ShortCode;
                c.ShortCode = real && !string.IsNullOrEmpty(c.IataCode) ? c.IataCode : c.GoodCode;
            }
        }

        public static GameData Load(GameInstall install, TextResource.Language lang = TextResource.Language.English)
        {
            var d = new GameData();
            if (install.Exists("misc/std_ger.res")) { d.StdText = TextResource.Parse(install.ReadAllBytes("misc/std_ger.res")); d.StdText.Lang = lang; }
            if (install.Exists("misc/dlg_ger.res")) { d.DialogText = TextResource.Parse(install.ReadAllBytes("misc/dlg_ger.res")); d.DialogText.Lang = lang; }
            d.LoadCities(CsvTable.Parse(install.ReadAllBytes("data/city.csv")));
            d.LoadPlaneTypes(CsvTable.Parse(install.ReadAllBytes("data/planetyp.csv")));
            d.LoadRoutes(CsvTable.Parse(install.ReadAllBytes("data/routen.csv")));
            if (install.Exists("data/pnames.csv")) d.LoadPlaneNames(CsvTable.Parse(install.ReadAllBytes("data/pnames.csv")));
            FinanceData.Load(d, install);
            d.LoadDomainTables(install);
            d.LoadDesigner(install);
            return d;
        }

        /// <summary>Hook for other domains' tables (implemented in their own partial files).</summary>
        partial void LoadDomainTables(GameInstall install);

        public void LoadCities(CsvTable t)
        {
            int index = 0;
            foreach (var r in t.Rows)
            {
                if (r.Length < 18) continue;
                var c = new City
                {
                    Id = CsvTable.Atoi(r[0]) + City.IdBase,
                    Index = index++,
                    Name = r[1].Trim(),
                    Region = r[2].TrimEnd(),
                    AreaCode = CsvTable.Atoi(r[3]),
                    ShortCode = r[4].Trim(),
                    GoodCode = r[4].Trim(),
                    IataCode = r[5].Trim(),
                    Wave = r[6].Trim() == "-" ? null : r[6].Trim(),
                    TextResBase = CsvTable.Atoi(r[7]),
                    TextCount = CsvTable.Atoi(r[8]),
                    PhotoName = r[9].Trim(),
                    PhotoCount = CsvTable.Atoi(r[10]),
                    Population = CsvTable.Atoi(r[11]),
                    GlobeX = CsvTable.Atoi(r[12]),
                    GlobeY = CsvTable.Atoi(r[13]),
                    MapX = CsvTable.Atoi(r[14]),
                    MapY = CsvTable.Atoi(r[15]),
                    OfficeRent = CsvTable.Atoi(r[16]),
                    NewInAddOn = CsvTable.Atoi(r[17]),
                };
                if (Cities.Contains(c.Id)) throw new FormatException("duplicate city id " + c.Id);
                Cities.Register(c);
            }
        }

        public const int MaxPlaneTypes = 30;

        public void LoadPlaneTypes(CsvTable t)
        {
            int slot = MaxPlaneTypes - 1;
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                if (r.Length < 23) continue;
                var p = new PlaneType
                {
                    Id = CsvTable.Atoi(r[0]) + PlaneType.IdBase,
                    Index = slot--,
                    Manufacturer = r[1].Trim(),
                    Name = r[2].Trim(),
                    NotepadPhoto = r[3].Trim(),
                    PhotoCount = CsvTable.Atoi(r[4]),
                    FirstMission = CsvTable.Atoi(r[5]),
                    FirstDay = CsvTable.Atoi(r[6]),
                    FirstYearBuilt = CsvTable.Atoi(r[7]),
                    Wingspan = CsvTable.Atoi(r[8]),
                    Length = CsvTable.Atoi(r[9]),
                    Height = CsvTable.Atoi(r[10]),
                    TakeoffWeight = CsvTable.Atoi(r[11]),
                    Seats = CsvTable.Atoi(r[12]),
                    Speed = CsvTable.Atoi(r[13]),
                    Range = CsvTable.Atoi(r[14]),
                    Engines = r[15].Trim(),
                    Thrust = CsvTable.Atoi(r[16]),
                    Pilots = CsvTable.Atoi(r[17]),
                    Attendants = CsvTable.Atoi(r[18]),
                    TankSize = CsvTable.Atoi(r[19]),
                    FuelBurn = CsvTable.Atoi(r[20]),
                    Price = CsvTable.Atoi(r[21]),
                    MaintenanceFactor = CsvTable.Atof(r[22]),
                    Comment = CsvTable.RestOfLine(t.Lines[i], 23),
                };
                PlaneTypes.Register(p);
            }
        }

        public City CityByName(string name)
        {
            foreach (var c in Cities.All)
                if (string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        public void LoadRoutes(CsvTable t)
        {
            Routes.Clear();
            int nextId = RouteDef.IdBase;
            foreach (var r in t.Rows)
            {
                if (r.Length < 5) continue;
                var a = CityByName(r[1]) ?? throw new FormatException("routen.csv: unknown city " + r[1]);
                var b = CityByName(r[2]) ?? throw new FormatException("routen.csv: unknown city " + r[2]);
                if (Routes.Exists(x => x.FromCity == a.Id && x.ToCity == b.Id)) continue;
                int level = CsvTable.Atoi(r[0]), rent = CsvTable.Atoi(r[3]);
                double factor = CsvTable.Atof(r[4]);
                bool deluxe = a.NewInAddOn == 2 || b.NewInAddOn == 2;
                Routes.Add(new RouteDef { Id = nextId++, Level = level, FromCity = a.Id, ToCity = b.Id, Rent = rent, Factor = factor, NewInDeluxe = deluxe });
                Routes.Add(new RouteDef { Id = nextId++, Level = level, FromCity = b.Id, ToCity = a.Id, Rent = rent, Factor = factor, NewInDeluxe = deluxe });
            }
            // Sorted by departure city display name (ordinal byte compare, stable).
            var sorted = new List<(RouteDef r, int i)>();
            for (int i = 0; i < Routes.Count; i++) sorted.Add((Routes[i], i));
            sorted.Sort((x, y) =>
            {
                int c = string.CompareOrdinal(Cities[x.r.FromCity].Name, Cities[y.r.FromCity].Name);
                return c != 0 ? c : x.i.CompareTo(y.i);
            });
            Routes.Clear();
            for (int i = 0; i < sorted.Count; i++) { sorted[i].r.Index = i; Routes.Add(sorted[i].r); }
        }

        public void LoadPlaneNames(CsvTable t)
        {
            foreach (var r in t.Rows)
            {
                if (r.Length < 2) continue;
                if (CsvTable.Atoi(r[1]) == 1) { if (PlaneNamesPreferred.Count < 200) PlaneNamesPreferred.Add(r[0].Trim()); }
                else if (PlaneNamesFallback.Count < 100) PlaneNamesFallback.Add(r[0].Trim());
            }
        }

        /// <summary>Plane type by its csv id (100..126).</summary>
        public PlaneType PlaneTypeByCsvId(int csvId) => PlaneTypes.Find(csvId + PlaneType.IdBase);
    }
}
