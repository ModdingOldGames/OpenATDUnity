using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using OpenAT.Sim.AI;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Modding
{
    /// <summary>
    /// JSON data mods (<c>Mods/&lt;Mod&gt;/data/*.json</c>, docs/MODDING.md "Data mods"). Every file is
    /// an object with optional sections, applied in this order:
    /// <list type="bullet">
    /// <item><c>tunables</c>: { "kerosene.max": 350, ... } (built-in keys in <see cref="TunableKeys"/>)</item>
    /// <item><c>options</c>: new-game defaults of <see cref="GameOptions"/> by field name</item>
    /// <item><c>planeTypes</c>: [ { "id": 100, "price": 30000000 }, { "id": 140, "basedOn": 100, "name": "..." } ] (csv ids)</item>
    /// <item><c>cities</c>: [ { "name": "Berlin", "population": 4000000 } ] (existing cities only)</item>
    /// <item><c>routes</c>: [ { "from": "Berlin", "to": "London", "factor": 1.5 } ] (changes or adds, both directions)</item>
    /// <item><c>robotFeatures</c>: { "Sabotage": "------|-|----------|----------", "Freight": { "free": false } }</item>
    /// <item><c>robotTuning</c>: { "workTicks": { "Bank": 60 }, "defaultPicks": [...], "standStillLimit": 5 }</item>
    /// </list>
    /// The file is parsed and checked when the mod loads (errors fail the mod before anything is
    /// applied); the edits run when the game data is loaded / a world is created.
    /// </summary>
    public static class DataMod
    {
        static readonly string[] Sections = { "tunables", "options", "planeTypes", "cities", "routes", "robotFeatures", "robotTuning", "comment", "$schema" };

        /// <summary>Parses one data file into registrations. Throws <see cref="FormatException"/> for invalid content.</summary>
        internal static void Compile(string fileName, string json, ModRegistrations reg, string modId, Action<string> log)
        {
            var root = Json.ParseObject(json);
            foreach (var kv in root.Entries)
                if (Array.FindIndex(Sections, s => string.Equals(s, kv.Key, StringComparison.OrdinalIgnoreCase)) < 0)
                    log?.Invoke($"{modId}: {fileName}: unknown section '{kv.Key}' (ignored)");

            if (root.Obj("tunables") is JsonObject tun) CompileTunables(tun, reg, modId, log);
            if (root.Obj("options") is JsonObject opt) CompileOptions(opt, reg);
            if (root.Arr("planeTypes") is List<object> pts) CompilePlaneTypes(pts, reg);
            if (root.Arr("cities") is List<object> cities) CompileCities(cities, reg);
            if (root.Arr("routes") is List<object> routes) CompileRoutes(routes, reg);
            if (root.Obj("robotFeatures") is JsonObject rf) CompileRobotFeatures(rf, reg, modId, log);
            if (root.Obj("robotTuning") is JsonObject rt) CompileRobotTuning(rt, reg);
        }

        // ------------------------------------------------------------------ tunables

        static void CompileTunables(JsonObject o, ModRegistrations reg, string modId, Action<string> log)
        {
            var values = new List<KeyValuePair<string, double>>();
            foreach (var kv in o.Entries)
            {
                if (!Json.TryNumber(kv.Value, out double d)) throw new FormatException("tunables." + kv.Key + ": number expected");
                values.Add(new KeyValuePair<string, double>(kv.Key, d));
            }
            reg.DataEdits.Add(data =>
            {
                foreach (var kv in values)
                {
                    if (!data.Tunables.Has(kv.Key))
                    {
                        // A key of a code mod that is not loaded, or a typo: defined so it can be read, but reported.
                        log?.Invoke($"{modId}: unknown tunable '{kv.Key}' (defined with this value)");
                        data.Tunables.Define(kv.Key, kv.Value);
                    }
                    var warn = data.Tunables.Set(kv.Key, kv.Value, modId);
                    if (warn != null) log?.Invoke(modId + ": " + warn);
                }
            });
        }

        // ------------------------------------------------------------------ options

        static void CompileOptions(JsonObject o, ModRegistrations reg)
        {
            var sets = new List<KeyValuePair<FieldInfo, object>>();
            foreach (var kv in o.Entries)
            {
                var f = FindField(typeof(GameOptions), kv.Key) ?? throw new FormatException("options: GameOptions has no field '" + kv.Key + "'");
                sets.Add(new KeyValuePair<FieldInfo, object>(f, ConvertValue(kv.Value, f.FieldType, "options." + kv.Key)));
            }
            reg.OptionEdits.Add(opt => { foreach (var s in sets) s.Key.SetValue(opt, s.Value); });
        }

        // ------------------------------------------------------------------ plane types

        static readonly string[] PlaneTypeKeys = { "id", "basedOn" };

        static void CompilePlaneTypes(List<object> items, ModRegistrations reg)
        {
            var list = new List<(int csv, int basedOn, List<KeyValuePair<FieldInfo, object>> sets)>();
            foreach (var item in items)
            {
                var o = item as JsonObject ?? throw new FormatException("planeTypes: object expected");
                if (!o.Has("id")) throw new FormatException("planeTypes: entry without \"id\" (csv id, e.g. 100)");
                int csv = o.Int("id");
                int basedOn = o.Has("basedOn") ? o.Int("basedOn") : -1;
                list.Add((csv, basedOn, Fields(typeof(PlaneType), o, PlaneTypeKeys, "planeTypes[" + csv + "]", "Id", "Index")));
            }
            reg.DataEdits.Add(data =>
            {
                foreach (var (csv, basedOn, sets) in list)
                {
                    var existing = data.PlaneTypeByCsvId(csv);
                    PlaneType t;
                    if (existing != null && basedOn < 0) t = existing;
                    else
                    {
                        var src = basedOn >= 0 ? data.PlaneTypeByCsvId(basedOn) ?? throw new FormatException("planeTypes[" + csv + "]: basedOn " + basedOn + " does not exist") : null;
                        if (src == null && existing == null) throw new FormatException("planeTypes[" + csv + "]: a new plane type needs \"basedOn\" (its graphics and missing values are taken from that type)");
                        t = Copy(src ?? existing);
                        t.Id = csv + PlaneType.IdBase;
                        t.Index = existing != null ? existing.Index : FreePlaneSlot(data, csv);
                    }
                    foreach (var s in sets) s.Key.SetValue(t, s.Value);
                    data.PlaneTypes.Register(t);
                }
            });
        }

        static PlaneType Copy(PlaneType p)
        {
            var c = new PlaneType();
            foreach (var f in typeof(PlaneType).GetFields(BindingFlags.Instance | BindingFlags.Public)) if (!f.IsInitOnly) f.SetValue(c, f.GetValue(p));
            c.Id = p.Id;
            return c;
        }

        static int FreePlaneSlot(GameData data, int csv)
        {
            var used = new HashSet<int>();
            foreach (var t in data.PlaneTypes.All) used.Add(t.Index);
            for (int i = GameData.MaxPlaneTypes - 1; i >= 0; i--) if (!used.Contains(i)) return i;
            throw new FormatException("planeTypes[" + csv + "]: no free plane slot (at most " + GameData.MaxPlaneTypes + " plane types)");
        }

        // ------------------------------------------------------------------ cities

        static void CompileCities(List<object> items, ModRegistrations reg)
        {
            var list = new List<(string name, int csv, List<KeyValuePair<FieldInfo, object>> sets)>();
            foreach (var item in items)
            {
                var o = item as JsonObject ?? throw new FormatException("cities: object expected");
                string name = o.Str("name");
                int csv = o.Has("id") ? o.Int("id") : -1;
                if (name == null && csv < 0) throw new FormatException("cities: entry needs \"name\" or \"id\" (csv id)");
                list.Add((name, csv, Fields(typeof(City), o, new[] { "id", "name" }, "cities[" + (name ?? csv.ToString(CultureInfo.InvariantCulture)) + "]", "Id", "Index")));
            }
            reg.DataEdits.Add(data =>
            {
                foreach (var (name, csv, sets) in list)
                {
                    var c = csv >= 0 ? data.Cities.Find(csv + City.IdBase) : data.CityByName(name);
                    if (c == null)
                        throw new FormatException("cities: '" + (name ?? csv.ToString(CultureInfo.InvariantCulture)) + "' is not in the game data (new cities are not supported: the map, routes and per-airline tables depend on the city table)");
                    foreach (var s in sets) s.Key.SetValue(c, s.Value);
                }
            });
        }

        // ------------------------------------------------------------------ routes

        static void CompileRoutes(List<object> items, ModRegistrations reg)
        {
            var list = new List<(string from, string to, bool both, List<KeyValuePair<FieldInfo, object>> sets)>();
            foreach (var item in items)
            {
                var o = item as JsonObject ?? throw new FormatException("routes: object expected");
                string from = o.Str("from"), to = o.Str("to");
                if (from == null || to == null) throw new FormatException("routes: entry needs \"from\" and \"to\" (city names or csv ids)");
                list.Add((from, to, o.Bool("bothWays", true), Fields(typeof(RouteDef), o, new[] { "from", "to", "bothWays" }, "routes[" + from + "-" + to + "]", "Id", "Index", "FromCity", "ToCity", "Demand")));
            }
            reg.DataEdits.Add(data =>
            {
                foreach (var (from, to, both, sets) in list)
                {
                    var a = CityRef(data, from);
                    var b = CityRef(data, to);
                    if (a == b) throw new FormatException("routes: " + from + "-" + to + " starts and ends in the same city");
                    Route(data, a, b, sets);
                    if (both) Route(data, b, a, sets);
                }
            });
        }

        static City CityRef(GameData data, string s) =>
            (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int csv) ? data.Cities.Find(csv + City.IdBase) : data.CityByName(s))
            ?? throw new FormatException("routes: unknown city '" + s + "'");

        static void Route(GameData data, City a, City b, List<KeyValuePair<FieldInfo, object>> sets)
        {
            var r = data.Routes.Find(x => x.FromCity == a.Id && x.ToCity == b.Id);
            if (r == null)
            {
                int id = RouteDef.IdBase;
                foreach (var x in data.Routes) if (x.Id >= id) id = x.Id + 1;
                // Appended (index = position): the original table is sorted by departure city, extensions come last.
                r = new RouteDef { Id = id, Index = data.Routes.Count, Level = 1, FromCity = a.Id, ToCity = b.Id, Rent = 30000, Factor = 1.0, NewInDeluxe = a.NewInAddOn == 2 || b.NewInAddOn == 2 };
                data.Routes.Add(r);
            }
            foreach (var s in sets) s.Key.SetValue(r, s.Value);
        }

        // ------------------------------------------------------------------ robots

        static void CompileRobotFeatures(JsonObject o, ModRegistrations reg, string modId, Action<string> log)
        {
            var cells = new List<(RobotFeature f, int level, bool on)>();
            foreach (var kv in o.Entries)
            {
                if (!Enum.TryParse(kv.Key, true, out RobotFeature f) || !Enum.IsDefined(typeof(RobotFeature), f))
                    throw new FormatException("robotFeatures: unknown feature '" + kv.Key + "'");
                if (kv.Value is string row)
                {
                    string c = row.Replace("|", "");
                    if (c.Length != RobotFeatureTable.Levels) throw new FormatException($"robotFeatures.{kv.Key}: a row needs {RobotFeatureTable.Levels} cells (base 0-5 | free | add-on 1-10 | ATFS 1-10)");
                    for (int l = 0; l < c.Length; l++) cells.Add((f, l, c[l] == 'X' || c[l] == 'x'));
                }
                else if (kv.Value is JsonObject lv)
                {
                    foreach (var cell in lv.Entries)
                    {
                        int level = LevelIndex(cell.Key);
                        if (level < 0) throw new FormatException($"robotFeatures.{kv.Key}: unknown level '{cell.Key}' (base0..base5, free, addon1..addon10, atfs1..atfs10, all or 0..26)");
                        if (!(cell.Value is bool on)) throw new FormatException($"robotFeatures.{kv.Key}.{cell.Key}: true/false expected");
                        if (level == int.MaxValue) for (int l = 0; l < RobotFeatureTable.Levels; l++) cells.Add((f, l, on));
                        else cells.Add((f, level, on));
                    }
                }
                else throw new FormatException("robotFeatures." + kv.Key + ": row string or object expected");
            }
            reg.WorldHooks.Add(w =>
            {
                if (!(w.Rules.Get<IRobotFeatures>() is RobotFeatureTable t))
                {
                    log?.Invoke(modId + ": robotFeatures ignored: the feature rule was replaced by a code mod");
                    return;
                }
                foreach (var (f, level, on) in cells) t.Set(f, level, on);
            });
        }

        /// <summary>base0..base5 → 0..5, free → 6, addon1..10 → 7..16, atfs1..10 → 17..26, "all", or a number.</summary>
        static int LevelIndex(string k)
        {
            k = k.Trim().ToLowerInvariant();
            if (k == "all") return int.MaxValue;
            if (k == "free" || k == "freegame") return 6;
            if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n >= 0 && n < RobotFeatureTable.Levels ? n : -1;
            int Num(string prefix) => k.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(k.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : -1;
            int b = Num("base"); if (b >= 0 && b <= 5) return b;
            int a = Num("addon"); if (a >= 1 && a <= 10) return 6 + a;
            int t = Num("atfs"); if (t >= 1 && t <= 10) return 16 + t;
            return -1;
        }

        static void CompileRobotTuning(JsonObject o, ModRegistrations reg)
        {
            var work = new List<(RobotAction a, int ticks)>();
            List<RobotAction> picks = null;
            var sets = new List<KeyValuePair<FieldInfo, object>>();
            foreach (var kv in o.Entries)
            {
                if (string.Equals(kv.Key, "workTicks", StringComparison.OrdinalIgnoreCase))
                {
                    var wt = kv.Value as JsonObject ?? throw new FormatException("robotTuning.workTicks: object expected");
                    foreach (var e in wt.Entries)
                    {
                        var a = Action(e.Key, "robotTuning.workTicks");
                        if (!Json.TryNumber(e.Value, out double d)) throw new FormatException("robotTuning.workTicks." + e.Key + ": number expected");
                        work.Add((a, (int)d));
                    }
                }
                else if (string.Equals(kv.Key, "defaultPicks", StringComparison.OrdinalIgnoreCase))
                {
                    var arr = kv.Value as List<object> ?? throw new FormatException("robotTuning.defaultPicks: array expected");
                    picks = new List<RobotAction>();
                    foreach (var x in arr) picks.Add(Action(x as string, "robotTuning.defaultPicks"));
                    if (picks.Count == 0) throw new FormatException("robotTuning.defaultPicks: at least one action needed");
                }
                else
                {
                    var f = FindField(typeof(RobotTuning), kv.Key);
                    if (f == null || (f.FieldType != typeof(int) && f.FieldType != typeof(bool))) throw new FormatException("robotTuning: unknown number '" + kv.Key + "'");
                    sets.Add(new KeyValuePair<FieldInfo, object>(f, ConvertValue(kv.Value, f.FieldType, "robotTuning." + kv.Key)));
                }
            }
            reg.WorldHooks.Add(w =>
            {
                var t = w.AI.Tuning;
                foreach (var (a, ticks) in work) t.WorkTicks[a] = ticks;
                if (picks != null) t.DefaultPicks = new List<RobotAction>(picks);
                foreach (var s in sets) s.Key.SetValue(t, s.Value);
            });
        }

        static RobotAction Action(string name, string where) =>
            name != null && Enum.TryParse(name, true, out RobotAction a) && Enum.IsDefined(typeof(RobotAction), a) ? a : throw new FormatException(where + ": unknown robot action '" + name + "'");

        // ------------------------------------------------------------------ reflection helpers

        static FieldInfo FindField(Type t, string name)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) && !f.IsInitOnly) return f;
            return null;
        }

        /// <summary>The settable public fields named in <paramref name="o"/> (except <paramref name="skipKeys"/>), with converted values.</summary>
        static List<KeyValuePair<FieldInfo, object>> Fields(Type t, JsonObject o, string[] skipKeys, string where, params string[] forbidden)
        {
            var list = new List<KeyValuePair<FieldInfo, object>>();
            foreach (var kv in o.Entries)
            {
                if (Array.FindIndex(skipKeys, k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)) >= 0) continue;
                if (Array.FindIndex(forbidden, k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)) >= 0)
                    throw new FormatException(where + ": '" + kv.Key + "' cannot be changed");
                var f = FindField(t, kv.Key) ?? throw new FormatException(where + ": " + t.Name + " has no field '" + kv.Key + "'");
                list.Add(new KeyValuePair<FieldInfo, object>(f, ConvertValue(kv.Value, f.FieldType, where + "." + kv.Key)));
            }
            return list;
        }

        static object ConvertValue(object v, Type t, string where)
        {
            try
            {
                if (t == typeof(string)) return v == null ? null : v as string ?? Json.ToInvariantString(v);
                if (t == typeof(bool)) return v is bool b ? b : throw new FormatException("true/false expected");
                if (t.IsEnum)
                {
                    if (v is string s) return Enum.Parse(t, s, true);
                    if (Json.TryNumber(v, out double e)) return Enum.ToObject(t, (long)e);
                }
                if (!Json.TryNumber(v, out double d)) throw new FormatException("number expected");
                if (t == typeof(double)) return d;
                if (t == typeof(float)) return (float)d;
                if (t == typeof(int)) return checked((int)d);
                if (t == typeof(long)) return checked((long)d);
                if (t == typeof(short)) return checked((short)d);
                if (t == typeof(byte)) return checked((byte)d);
                throw new FormatException("fields of type " + t.Name + " cannot be set from data");
            }
            catch (Exception e) when (!(e is FormatException f && f.Message.StartsWith(where, StringComparison.Ordinal)))
            {
                throw new FormatException(where + ": " + e.Message, e);
            }
        }
    }
}
