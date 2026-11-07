using System;
using System.Collections.Generic;
using System.Globalization;
using OpenAT.Sim.Save;

namespace OpenAT.Sim.Modding
{
    /// <summary>Description of one tunable number: key, original default, valid range.</summary>
    public sealed class TunableDef
    {
        public readonly string Key;
        public readonly double Default;
        public readonly double Min, Max;
        /// <summary>Whole numbers only (values are rounded when set).</summary>
        public readonly bool Integer;
        /// <summary>What the number does and the spec section it comes from.</summary>
        public readonly string Description;

        public TunableDef(string key, double def, string description, double min = double.MinValue, double max = double.MaxValue, bool integer = true)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("tunable key missing");
            Key = key; Default = def; Description = description ?? ""; Min = min; Max = max; Integer = integer;
        }
    }

    /// <summary>Keys of the built-in tunables (defaults = the original game; see <see cref="Tunables.BuiltIn"/>).</summary>
    public static class TunableKeys
    {
        public const string KeroseneStart = "kerosene.start";
        public const string KeroseneMin = "kerosene.min";
        public const string KeroseneMax = "kerosene.max";
        public const string RouteDemandFactor = "route.demandFactor";
        public const string RoutePotentialPercent = "route.potentialPercent";
        public const string StartMoneyPercent = "finance.startMoneyPercent";
        public const string LoanRateDelta = "finance.loanRateDelta";
        public const string SavingsRateDelta = "finance.savingsRateDelta";
        public const string ShareStartPrice = "shares.startPrice";
        public const string PlaneWearPercent = "fleet.wearPercent";
        public const string SeasonalIncidentCost = "incident.seasonalCost";
        public const string QualityIncidentImageLoss = "incident.qualityImageLoss";
        public const string TankStartCapacity = "fuel.tankStartCapacity";
    }

    /// <summary>
    /// Central table of balance numbers that rules read instead of hard-coded constants
    /// (docs/MODDING.md "Tunables"). Defaults reproduce the original game exactly. The game data holds
    /// the table with every data/code mod override applied (<see cref="Data.GameData.Tunables"/>);
    /// each <see cref="World"/> gets its own copy (<see cref="World.Tunables"/>) so a code mod may
    /// change values per game. Not saved: a loaded game uses the values of the mods loaded now.
    /// Keys iterate in definition order (deterministic).
    /// </summary>
    [NotSaved]
    public sealed class Tunables
    {
        /// <summary>The built-in tunables with the original's values.</summary>
        public static readonly IReadOnlyList<TunableDef> BuiltIn = new[]
        {
            new TunableDef(TunableKeys.KeroseneStart, 500, "Kerosene market price per barrel at game start (economy.md §2.3); history[9] = start + 8.", 1, 100000),
            new TunableDef(TunableKeys.KeroseneMin, 300, "Lowest kerosene market price (economy.md §12.1 clamp).", 1, 100000),
            new TunableDef(TunableKeys.KeroseneMax, 700, "Highest kerosene market price (economy.md §12.1 clamp).", 1, 100000),
            new TunableDef(TunableKeys.RouteDemandFactor, 4.27, "Route demand target = potential × this (contracts-and-routes.md §3.2).", 0, 1000, integer: false),
            new TunableDef(TunableKeys.RoutePotentialPercent, 100, "Route passenger potential in percent (contracts-and-routes.md §3.2; 100 = original).", 0, 100000),
            new TunableDef(TunableKeys.StartMoneyPercent, 100, "Starting cash of every airline in percent of the mission's value (economy.md §2.3).", 0, 100000),
            new TunableDef(TunableKeys.LoanRateDelta, 0, "Percentage points added to the mission's loan interest rate (economy.md §5.3).", -100, 100),
            new TunableDef(TunableKeys.SavingsRateDelta, 0, "Percentage points added to the mission's deposit interest rate (economy.md §5.3).", -100, 100),
            new TunableDef(TunableKeys.ShareStartPrice, 100, "Share price at game start (economy.md §2.3).", 1, 1000000),
            new TunableDef(TunableKeys.PlaneWearPercent, 100, "Plane wear per departure in percent (planes-and-flights.md §7.1).", 0, 10000),
            new TunableDef(TunableKeys.SeasonalIncidentCost, 75000, "Cost of a seasonal incident (planes-and-flights.md incidents).", 0, 1000000000),
            new TunableDef(TunableKeys.QualityIncidentImageLoss, 2, "Image lost by a quality incident.", -1000, 1000),
            new TunableDef(TunableKeys.TankStartCapacity, 100, "Kerosene tank capacity (barrels) at game start (economy.md §12.2).", 0, 100000000),
        };

        readonly List<TunableDef> _defs = new List<TunableDef>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<double> _values = new List<double>();
        /// <summary>Who changed a value last (mod id), for diagnostics; null = default.</summary>
        readonly List<string> _source = new List<string>();

        /// <summary>A table with the built-in tunables at their original values.</summary>
        public static Tunables CreateDefault()
        {
            var t = new Tunables();
            foreach (var d in BuiltIn) t.Define(d);
            return t;
        }

        public IReadOnlyList<TunableDef> Defs => _defs;
        public bool Has(string key) => key != null && _index.ContainsKey(key);
        public TunableDef Def(string key) => _index.TryGetValue(key, out int i) ? _defs[i] : null;

        /// <summary>Adds a tunable (mods define their own). Defining an existing key again keeps the existing definition and value.</summary>
        public void Define(TunableDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (_index.ContainsKey(def.Key)) return;
            _index[def.Key] = _defs.Count;
            _defs.Add(def);
            _values.Add(def.Default);
            _source.Add(null);
        }

        public void Define(string key, double def, string description = null, double min = double.MinValue, double max = double.MaxValue, bool integer = false) =>
            Define(new TunableDef(key, def, description, min, max, integer));

        public double Get(string key)
        {
            if (_index.TryGetValue(key, out int i)) return _values[i];
            throw new KeyNotFoundException("unknown tunable " + key);
        }

        public double Get(string key, double fallback) => _index.TryGetValue(key, out int i) ? _values[i] : fallback;
        public int Int(string key) => (int)Get(key);
        public int Int(string key, int fallback) => _index.TryGetValue(key, out int i) ? (int)_values[i] : fallback;
        public long Long(string key) => (long)Get(key);

        public bool IsDefault(string key) => _index.TryGetValue(key, out int i) && _values[i] == _defs[i].Default;
        public string SourceOf(string key) => _index.TryGetValue(key, out int i) ? _source[i] : null;

        /// <summary>Sets a value, clamped to the definition's range (whole numbers are rounded). Returns a warning or null.</summary>
        public string Set(string key, double value, string source = null)
        {
            if (!_index.TryGetValue(key, out int i)) throw new KeyNotFoundException("unknown tunable " + key);
            var d = _defs[i];
            string warning = null;
            if (double.IsNaN(value)) return "tunable " + key + ": not a number (ignored)";
            if (d.Integer && value != Math.Round(value)) { value = Math.Round(value); warning = $"tunable {key}: rounded to {value}"; }
            if (value < d.Min || value > d.Max)
            {
                value = Math.Max(d.Min, Math.Min(d.Max, value));
                warning = $"tunable {key}: clamped to {value.ToString(CultureInfo.InvariantCulture)}";
            }
            _values[i] = value;
            _source[i] = source;
            return warning;
        }

        public Tunables Clone()
        {
            var t = new Tunables();
            t._defs.AddRange(_defs);
            foreach (var kv in _index) t._index[kv.Key] = kv.Value;
            t._values.AddRange(_values);
            t._source.AddRange(_source);
            return t;
        }

        /// <summary>"key = value" lines of the changed values (logs, the CLI).</summary>
        public IEnumerable<string> Changes()
        {
            for (int i = 0; i < _defs.Count; i++)
                if (_values[i] != _defs[i].Default)
                    yield return $"{_defs[i].Key} = {_values[i].ToString("R", CultureInfo.InvariantCulture)} (default {_defs[i].Default.ToString("R", CultureInfo.InvariantCulture)}{(_source[i] != null ? ", by " + _source[i] : "")})";
        }
    }
}
