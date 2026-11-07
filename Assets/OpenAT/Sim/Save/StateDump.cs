using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenAT.Sim.Save
{
    /// <summary>
    /// Text dump of the saved state ("path = value" per line, shared objects as "#id"), following the
    /// same rules as the save writer. For diffing two worlds when their <see cref="SaveGame.StateHash"/>
    /// differ (tests, `openat sim --dump`).
    /// </summary>
    sealed class StateDump
    {
        readonly StringBuilder _sb = new StringBuilder();
        readonly Dictionary<object, int> _ids = new Dictionary<object, int>(RefEq.Instance);
        readonly World _root;
        readonly bool _machinery;
        readonly ContentIndex _content;

        StateDump(World root, bool machinery)
        {
            _root = root;
            _machinery = machinery;
            _content = new ContentIndex(root.Data);
            _ids[root] = 0;
        }

        public static string Of(World w, bool machinery)
        {
            var d = new StateDump(w, machinery);
            d.Value("Options", w.Options);
            d.Fields("World", SaveTypes.Get(w.GetType(), machinery), w);
            if (machinery)
            {
                d.Value("Rules", w.Rules);
                d.Value("Systems", w.Systems);
            }
            return d._sb.ToString();
        }

        void Line(string path, string v) => _sb.Append(path).Append(" = ").Append(v).Append('\n');

        void Fields(string path, SaveType st, object o)
        {
            foreach (var f in st.Fields) Value(path + "." + f.Name, f.Info.GetValue(o));
        }

        void Value(string path, object v)
        {
            if (v == null) { Line(path, "null"); return; }
            if (ReferenceEquals(v, _root)) { Line(path, "<world>"); return; }
            var t = v.GetType();
            if (t.IsEnum) { Line(path, t.Name + "." + v); return; }
            if (v is string s) { Line(path, "\"" + s + "\""); return; }
            if (v is double dd) { Line(path, dd.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is float ff) { Line(path, ff.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t.IsPrimitive || v is decimal || v is DateTime) { Line(path, Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            var st = SaveTypes.Get(t, _machinery);
            switch (st.Kind)
            {
                case TypeKind.Skip: Line(path, "<not saved " + t.Name + ">"); return;
                case TypeKind.TypeValue: Line(path, "typeof(" + TypeNames.Of((Type)v) + ")"); return;
                case TypeKind.Unsupported: Line(path, "<unsupported " + t.FullName + ">"); return;
                case TypeKind.Struct: Fields(path, st, v); return;
            }
            if (_ids.TryGetValue(v, out var id)) { Line(path, "#" + id); return; }
            if (v is Core.IContent c && _content.IsRegistered(v)) { Line(path, "content " + t.Name + " " + c.Id); return; }
            id = _ids.Count;
            _ids[v] = id;
            switch (st.Kind)
            {
                case TypeKind.Object:
                    Line(path, "#" + id + " " + t.Name);
                    Fields(path, st, v);
                    return;
                case TypeKind.Array:
                {
                    var a = (Array)v;
                    Line(path, "#" + id + " " + t.Name + " length " + a.Length);
                    int i = 0;
                    foreach (var x in a) Value(path + "[" + i++ + "]", x);
                    return;
                }
                case TypeKind.Dictionary:
                {
                    var d = (IDictionary)v;
                    Line(path, "#" + id + " dictionary count " + d.Count);
                    var keys = new List<object>();
                    foreach (DictionaryEntry e in d) keys.Add(e.Key);
                    keys.Sort((x, y) => string.CompareOrdinal(Convert.ToString(x, CultureInfo.InvariantCulture), Convert.ToString(y, CultureInfo.InvariantCulture)));
                    foreach (var k in keys) Value(path + "[" + Convert.ToString(k, CultureInfo.InvariantCulture) + "]", d[k]);
                    return;
                }
                default:
                {
                    var items = new List<object>();
                    foreach (var x in (IEnumerable)v) items.Add(x);
                    Line(path, "#" + id + " " + t.Name + " count " + items.Count);
                    for (int i = 0; i < items.Count; i++) Value(path + "[" + i + "]", items[i]);
                    return;
                }
            }
        }
    }
}
