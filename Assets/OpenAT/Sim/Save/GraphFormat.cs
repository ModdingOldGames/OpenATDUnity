using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Save
{
    /// <summary>
    /// Value tags of the body. Every value is tagged, so a reader can step over fields it no longer
    /// knows. Types are defined inline on first use (name + field names) and then referenced by index.
    /// Reference objects get ids in write order; a repeated reference is written as <see cref="Ref"/>.
    /// </summary>
    enum Tag : byte
    {
        Null = 0, False = 1, True = 2,
        I8 = 3, U8 = 4, I16 = 5, U16 = 6, I32 = 7, U32 = 8, I64 = 9, U64 = 10,
        F32 = 11, F64 = 12, Char = 13, Decimal = 14, String = 15,
        Enum = 16, TypeValue = 17, DateTime = 18,
        Ref = 19, Root = 20, Content = 21, Skip = 22,
        Struct = 23, Object = 24, Array = 25, List = 26, Dictionary = 27, HashSet = 28, Queue = 29, Stack = 30,
    }

    sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq Instance = new RefEq();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }

    /// <summary>
    /// <see cref="IContent"/> objects of the game data (every <see cref="Registry{T}"/> or
    /// <c>List&lt;T&gt;</c> field of <see cref="Data.GameData"/> whose T is content), by type and id.
    /// </summary>
    sealed class ContentIndex
    {
        readonly Dictionary<Type, Dictionary<int, object>> _byType = new Dictionary<Type, Dictionary<int, object>>();

        public ContentIndex(Data.GameData data)
        {
            if (data == null) return;
            foreach (var f in typeof(Data.GameData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var t = f.FieldType;
                if (!t.IsGenericType) continue;
                var def = t.GetGenericTypeDefinition();
                if (def != typeof(Registry<>) && def != typeof(List<>)) continue;
                var el = t.GetGenericArguments()[0];
                if (!typeof(IContent).IsAssignableFrom(el)) continue;
                var v = f.GetValue(data);
                IEnumerable items = def == typeof(Registry<>) ? (IEnumerable)t.GetProperty("All").GetValue(v) : (IEnumerable)v;
                if (items == null) continue;
                if (!_byType.TryGetValue(el, out var map)) _byType[el] = map = new Dictionary<int, object>();
                foreach (var o in items)
                    if (o is IContent c && !map.ContainsKey(c.Id)) map[c.Id] = o;
            }
        }

        Dictionary<int, object> MapFor(Type t)
        {
            for (var x = t; x != null; x = x.BaseType)
                if (_byType.TryGetValue(x, out var m)) return m;
            return null;
        }

        public bool IsRegistered(object o) =>
            o is IContent c && MapFor(o.GetType()) is Dictionary<int, object> m && m.TryGetValue(c.Id, out var r) && ReferenceEquals(r, o);

        public object Resolve(Type t, int id) => t != null && MapFor(t) is Dictionary<int, object> m && m.TryGetValue(id, out var r) ? r : null;
    }

    // ====================================================================== writer

    sealed class GraphWriter
    {
        readonly ByteWriter _o;
        readonly World _root;
        readonly bool _machinery;
        readonly bool _canonical;
        readonly ContentIndex _content;
        readonly Dictionary<object, int> _ids = new Dictionary<object, int>(RefEq.Instance);
        readonly Dictionary<Type, int> _types = new Dictionary<Type, int>();
        int _nextId = 1;

        public int ObjectCount => _nextId;
        public int TypeCount => _types.Count;
        public IEnumerable<Type> Types => _types.Keys;

        /// <param name="machinery">Also write rules and systems (state hash only).</param>
        /// <param name="canonical">Sort dictionary and set entries by key (state hash only).</param>
        public GraphWriter(ByteWriter o, World root, bool machinery = false, bool canonical = false)
        {
            _o = o;
            _root = root;
            _machinery = machinery;
            _canonical = canonical;
            _content = new ContentIndex(root.Data);
            _ids[root] = 0;
        }

        public void WriteRoot()
        {
            var st = SaveTypes.Get(_root.GetType(), _machinery);
            _o.Byte((byte)Tag.Object);
            WriteType(st);
            ((ISaveHooks)_root).OnBeforeSave(_root);
            WriteFields(st, _root);
        }

        void WriteType(SaveType st)
        {
            if (_types.TryGetValue(st.Type, out var id)) { _o.UVar((ulong)id); return; }
            id = _types.Count;
            _types[st.Type] = id;
            _o.UVar((ulong)id);
            _o.Str(TypeNames.Of(st.Type));
            _o.Byte((byte)st.Kind);
            if (st.Kind == TypeKind.Object || st.Kind == TypeKind.Struct)
            {
                _o.UVar((ulong)st.Fields.Length);
                foreach (var f in st.Fields) _o.Str(f.Name);
            }
        }

        void WriteFields(SaveType st, object obj)
        {
            foreach (var f in st.Fields)
            {
                try { WriteValue(f.Info.GetValue(obj)); }
                catch (SaveException e) { throw new SaveException(f.Name + "." + e.Message, e.InnerException); }
            }
        }

        public void WriteValue(object v)
        {
            if (v == null) { _o.Byte((byte)Tag.Null); return; }
            if (ReferenceEquals(v, _root)) { _o.Byte((byte)Tag.Root); return; }
            var t = v.GetType();
            if (t.IsEnum)
            {
                if (SaveTypes.IsSkipped(t, _machinery)) { _o.Byte((byte)Tag.Skip); return; }
                _o.Byte((byte)Tag.Enum);
                WriteType(SaveTypes.Get(t, _machinery));
                var u = Enum.GetUnderlyingType(t);
                _o.Var(u == typeof(ulong) ? unchecked((long)Convert.ToUInt64(v)) : Convert.ToInt64(v));
                return;
            }
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: _o.Byte((byte)((bool)v ? Tag.True : Tag.False)); return;
                case TypeCode.SByte: _o.Byte((byte)Tag.I8); _o.Var((sbyte)v); return;
                case TypeCode.Byte: _o.Byte((byte)Tag.U8); _o.UVar((byte)v); return;
                case TypeCode.Int16: _o.Byte((byte)Tag.I16); _o.Var((short)v); return;
                case TypeCode.UInt16: _o.Byte((byte)Tag.U16); _o.UVar((ushort)v); return;
                case TypeCode.Int32: _o.Byte((byte)Tag.I32); _o.Var((int)v); return;
                case TypeCode.UInt32: _o.Byte((byte)Tag.U32); _o.UVar((uint)v); return;
                case TypeCode.Int64: _o.Byte((byte)Tag.I64); _o.Var((long)v); return;
                case TypeCode.UInt64: _o.Byte((byte)Tag.U64); _o.UVar((ulong)v); return;
                case TypeCode.Char: _o.Byte((byte)Tag.Char); _o.UVar((char)v); return;
                case TypeCode.Single: _o.Byte((byte)Tag.F32); _o.Fixed32(BitConverter.ToUInt32(BitConverter.GetBytes((float)v), 0)); return;
                case TypeCode.Double: _o.Byte((byte)Tag.F64); _o.Fixed64((ulong)BitConverter.DoubleToInt64Bits((double)v)); return;
                case TypeCode.String: _o.Byte((byte)Tag.String); _o.Str((string)v); return;
                case TypeCode.Decimal:
                    _o.Byte((byte)Tag.Decimal);
                    foreach (var b in decimal.GetBits((decimal)v)) _o.Fixed32((uint)b);
                    return;
                case TypeCode.DateTime:
                    var d = (DateTime)v;
                    _o.Byte((byte)Tag.DateTime); _o.Var(d.Ticks); _o.Byte((byte)d.Kind);
                    return;
            }

            var st = SaveTypes.Get(t, _machinery);
            switch (st.Kind)
            {
                case TypeKind.Skip: _o.Byte((byte)Tag.Skip); return;
                case TypeKind.TypeValue: _o.Byte((byte)Tag.TypeValue); _o.Str(TypeNames.Of((Type)v)); return;
                case TypeKind.Unsupported:
                    throw new SaveException($" type {TypeNames.Of(t)} cannot be saved; mark the field or type [NotSaved] (and rebuild it in ISaveHooks.OnAfterLoad), or use a supported collection");
                case TypeKind.Struct:
                    _o.Byte((byte)Tag.Struct);
                    WriteType(st);
                    WriteFields(st, v);
                    return;
            }

            if (_ids.TryGetValue(v, out var id)) { _o.Byte((byte)Tag.Ref); _o.UVar((ulong)id); return; }
            if (v is IContent c && _content.IsRegistered(v))
            {
                _o.Byte((byte)Tag.Content);
                WriteType(st);
                _o.Var(c.Id);
                return;
            }
            _ids[v] = _nextId++;

            switch (st.Kind)
            {
                case TypeKind.Object:
                    _o.Byte((byte)Tag.Object);
                    WriteType(st);
                    if (v is ISaveHooks h) h.OnBeforeSave(_root);
                    WriteFields(st, v);
                    return;
                case TypeKind.Array:
                    WriteArray(st, (Array)v);
                    return;
                case TypeKind.List:
                {
                    var l = (IList)v;
                    _o.Byte((byte)Tag.List);
                    WriteType(st);
                    _o.UVar((ulong)l.Count);
                    for (int i = 0; i < l.Count; i++) Item(i, l[i]);
                    return;
                }
                case TypeKind.Dictionary:
                {
                    var dct = (IDictionary)v;
                    _o.Byte((byte)Tag.Dictionary);
                    WriteType(st);
                    _o.UVar((ulong)dct.Count);
                    var keys = new List<object>(dct.Count);
                    foreach (DictionaryEntry e in dct) keys.Add(e.Key);
                    if (_canonical) SortCanonical(keys);
                    foreach (var k in keys)
                    {
                        WriteValue(k);
                        try { WriteValue(dct[k]); }
                        catch (SaveException e) { throw new SaveException("[" + k + "]." + e.Message, e.InnerException); }
                    }
                    return;
                }
                case TypeKind.HashSet:
                case TypeKind.Queue:
                case TypeKind.Stack:
                {
                    var items = new List<object>();
                    foreach (var x in (IEnumerable)v) items.Add(x);
                    if (_canonical && st.Kind == TypeKind.HashSet) SortCanonical(items);
                    _o.Byte((byte)(st.Kind == TypeKind.HashSet ? Tag.HashSet : st.Kind == TypeKind.Queue ? Tag.Queue : Tag.Stack));
                    WriteType(st);
                    _o.UVar((ulong)items.Count);
                    for (int i = 0; i < items.Count; i++) Item(i, items[i]);
                    return;
                }
            }
            throw new SaveException($" unexpected type {TypeNames.Of(t)}");
        }

        void Item(int i, object x)
        {
            try { WriteValue(x); }
            catch (SaveException e) { throw new SaveException("[" + i + "]." + e.Message, e.InnerException); }
        }

        void WriteArray(SaveType st, Array a)
        {
            _o.Byte((byte)Tag.Array);
            WriteType(st);
            int rank = a.Rank;
            _o.UVar((ulong)rank);
            for (int r = 0; r < rank; r++) _o.UVar((ulong)a.GetLength(r));
            if (st.PrimitiveElements)
            {
                _o.Byte(1);
                _o.Block(a, Buffer.ByteLength(a));
                return;
            }
            _o.Byte(0);
            if (rank == 1)
            {
                for (int i = 0; i < a.Length; i++) Item(i, a.GetValue(i));
                return;
            }
            var idx = new int[rank];
            for (int n = 0; n < a.Length; n++)
            {
                Item(n, a.GetValue(idx));
                for (int r = rank - 1; r >= 0; r--)
                {
                    if (++idx[r] < a.GetLength(r)) break;
                    idx[r] = 0;
                }
            }
        }

        static void SortCanonical(List<object> keys)
        {
            keys.Sort((x, y) =>
            {
                if (x is string sx && y is string sy) return string.CompareOrdinal(sx, sy);
                if (x is IComparable cx && x.GetType() == y?.GetType()) return cx.CompareTo(y);
                return string.CompareOrdinal(x?.ToString(), y?.ToString());
            });
        }
    }

    // ====================================================================== reader

    sealed class GraphReader
    {
        sealed class TypeEntry
        {
            public string Name;
            public Type Type;
            public TypeKind Kind;
            public SaveType Save;
            /// <summary>Current field per saved field (null = no longer exists).</summary>
            public SaveField[] Fields;
        }

        static readonly object SkipMarker = new object();

        readonly ByteReader _in;
        World _root;
        readonly ContentIndex _content;
        readonly List<object> _objs = new List<object>();
        readonly List<TypeEntry> _types = new List<TypeEntry>();
        readonly HashSet<object> _claimed = new HashSet<object>(RefEq.Instance);
        readonly List<ISaveHooks> _hooks = new List<ISaveHooks>();
        public readonly List<string> Warnings = new List<string>();

        public GraphReader(ByteReader input, Data.GameData data)
        {
            _in = input;
            _content = new ContentIndex(data);
            _objs.Add(null); // id 0 = the world, attached once it is constructed
        }

        /// <summary>The world the root fields are read into (constructed after the options were read).</summary>
        public void AttachRoot(World w)
        {
            _root = w;
            _objs[0] = w;
        }

        public IReadOnlyList<ISaveHooks> Hooks => _hooks;

        /// <summary>Reads a value that precedes the root (options): no existing object.</summary>
        public object ReadDetached(Type expected)
        {
            var v = ReadValue(expected, null, false, false);
            return v == SkipMarker ? null : v;
        }

        public void ReadRoot()
        {
            if (_root == null) throw new InvalidOperationException("AttachRoot first");
            if ((Tag)_in.Byte() != Tag.Object) throw new SaveException("body does not start with the world");
            var te = ReadType();
            if (te.Type != _root.GetType()) throw new SaveException("body root is " + te.Name);
            ReadFields(te, _root, true);
            _hooks.Add((ISaveHooks)_root);
        }

        TypeEntry ReadType()
        {
            int id = _in.Int();
            if (id < _types.Count) return _types[id];
            if (id != _types.Count) throw new SaveException("corrupt type table");
            var e = new TypeEntry { Name = _in.Str() };
            e.Kind = (TypeKind)_in.Byte();
            e.Type = TypeNames.Resolve(e.Name);
            if (e.Type == null) Warnings.Add("unknown type " + e.Name + " (skipped)");
            else e.Save = SaveTypes.Get(e.Type, false);
            if (e.Kind == TypeKind.Object || e.Kind == TypeKind.Struct)
            {
                int n = _in.Int();
                e.Fields = new SaveField[n];
                for (int i = 0; i < n; i++)
                {
                    string name = _in.Str();
                    e.Fields[i] = e.Save?.Field(name);
                }
            }
            _types.Add(e);
            return e;
        }

        void ReadFields(TypeEntry te, object obj, bool overlayChildren)
        {
            for (int i = 0; i < te.Fields.Length; i++)
            {
                var f = te.Fields[i];
                object existing = f != null && obj != null ? f.Info.GetValue(obj) : null;
                object v;
                try { v = ReadValue(f?.Type ?? typeof(object), existing, f != null && overlayChildren && f.ReadOnly, f != null && overlayChildren && f.Merge); }
                catch (SaveException e) { throw new SaveException((f?.Name ?? "?") + "." + e.Message, e.InnerException); }
                if (f == null || obj == null || v == SkipMarker || ReferenceEquals(v, existing)) continue;
                if (Convert(f.Type, ref v)) f.Info.SetValue(obj, v);
                else Warnings.Add($"{te.Name}.{f.Name}: saved value of type {v?.GetType().Name ?? "null"} does not fit {f.Type.Name} (kept default)");
            }
        }

        /// <summary>Fits a read value to a (possibly changed) field type. False = incompatible.</summary>
        static bool Convert(Type target, ref object v)
        {
            var u = Nullable.GetUnderlyingType(target);
            if (v == null) return !target.IsValueType || u != null;
            var tt = u ?? target;
            if (tt.IsInstanceOfType(v)) return true;
            try
            {
                if (tt.IsEnum && (v is IConvertible)) { v = Enum.ToObject(tt, System.Convert.ToInt64(v)); return true; }
                if ((tt.IsPrimitive || tt == typeof(decimal)) && v is IConvertible)
                {
                    v = v.GetType().IsEnum ? System.Convert.ChangeType(System.Convert.ToInt64(v), tt) : System.Convert.ChangeType(v, tt);
                    return true;
                }
            }
            catch (Exception) { /* incompatible */ }
            return false;
        }

        bool CanReuse(object existing, Type t) => existing != null && t != null && existing.GetType() == t && !_claimed.Contains(existing);

        object ReadValue(Type expected, object existing, bool overlay, bool merge)
        {
            var tag = (Tag)_in.Byte();
            switch (tag)
            {
                case Tag.Null: return null;
                case Tag.False: return false;
                case Tag.True: return true;
                case Tag.I8: return (sbyte)_in.Var();
                case Tag.U8: return (byte)_in.UVar();
                case Tag.I16: return (short)_in.Var();
                case Tag.U16: return (ushort)_in.UVar();
                case Tag.I32: return (int)_in.Var();
                case Tag.U32: return (uint)_in.UVar();
                case Tag.I64: return _in.Var();
                case Tag.U64: return _in.UVar();
                case Tag.Char: return (char)_in.UVar();
                case Tag.F32: return BitConverter.ToSingle(BitConverter.GetBytes(_in.Fixed32()), 0);
                case Tag.F64: return BitConverter.Int64BitsToDouble((long)_in.Fixed64());
                case Tag.Decimal:
                {
                    var bits = new int[4];
                    for (int i = 0; i < 4; i++) bits[i] = (int)_in.Fixed32();
                    return new decimal(bits);
                }
                case Tag.String: return _in.Str();
                case Tag.DateTime: { long ticks = _in.Var(); return new DateTime(ticks, (DateTimeKind)_in.Byte()); }
                case Tag.TypeValue: return TypeNames.Resolve(_in.Str());
                case Tag.Enum:
                {
                    var te = ReadType();
                    long n = _in.Var();
                    return te.Type != null && te.Type.IsEnum ? Enum.ToObject(te.Type, n) : (object)n;
                }
                case Tag.Root: return _root ?? throw new SaveException("world referenced before it exists");
                case Tag.Skip: return SkipMarker;
                case Tag.Ref:
                {
                    int id = _in.Int();
                    if (id >= _objs.Count) throw new SaveException("bad reference");
                    return _objs[id];
                }
                case Tag.Content:
                {
                    var te = ReadType();
                    int id = (int)_in.Var();
                    var c = _content.Resolve(te.Type, id);
                    if (c == null) Warnings.Add($"content {te.Name} {id} not in the game data");
                    return c;
                }
                case Tag.Struct:
                {
                    var te = ReadType();
                    object box = te.Type != null ? Activator.CreateInstance(te.Type) : null;
                    ReadFields(te, box, false);
                    return box;
                }
                case Tag.Object:
                {
                    var te = ReadType();
                    object obj = null;
                    bool reused = false;
                    if (te.Type != null)
                    {
                        if (overlay && CanReuse(existing, te.Type)) { obj = existing; reused = true; _claimed.Add(obj); }
                        else obj = te.Save.NewObject();
                    }
                    _objs.Add(obj);
                    ReadFields(te, obj, reused);
                    if (obj is ISaveHooks h) _hooks.Add(h);
                    return obj;
                }
                case Tag.Array: return ReadArray(existing, overlay);
                case Tag.List:
                case Tag.HashSet:
                case Tag.Queue:
                case Tag.Stack:
                    return ReadCollection(tag, existing);
                case Tag.Dictionary: return ReadDictionary(existing, merge);
            }
            throw new SaveException("bad value tag " + (int)tag);
        }

        object ReadArray(object existing, bool overlay)
        {
            var te = ReadType();
            int rank = _in.Int();
            var lengths = new int[rank];
            long total = 1;
            for (int r = 0; r < rank; r++) { lengths[r] = _in.Int(); total *= lengths[r]; }
            bool block = _in.Byte() == 1;
            Array a = null;
            bool reused = false;
            if (te.Type != null && te.Type.IsArray && te.Type.GetArrayRank() == rank)
            {
                if (CanReuse(existing, te.Type) && SameLengths((Array)existing, lengths)) { a = (Array)existing; reused = true; _claimed.Add(a); }
                else a = Array.CreateInstance(te.Type.GetElementType(), lengths);
            }
            _objs.Add(a);
            if (block)
            {
                if (!_in.Block(a, a != null ? Buffer.ByteLength(a) : -1) && a != null) Warnings.Add("array block size mismatch for " + te.Name);
                return a;
            }
            var el = a?.GetType().GetElementType() ?? typeof(object);
            var idx = new int[rank];
            for (long n = 0; n < total; n++)
            {
                object old = reused ? (rank == 1 ? a.GetValue(idx[0]) : a.GetValue(idx)) : null;
                object v;
                try { v = ReadValue(el, old, overlay && reused, false); }
                catch (SaveException e) { throw new SaveException("[" + n + "]." + e.Message, e.InnerException); }
                if (a != null && v != SkipMarker && !ReferenceEquals(v, old) && Convert(el, ref v))
                {
                    if (rank == 1) a.SetValue(v, idx[0]); else a.SetValue(v, idx);
                }
                for (int r = rank - 1; r >= 0; r--)
                {
                    if (++idx[r] < lengths[r]) break;
                    idx[r] = 0;
                }
            }
            return a;
        }

        static bool SameLengths(Array a, int[] lengths)
        {
            if (a.Rank != lengths.Length) return false;
            for (int r = 0; r < lengths.Length; r++) if (a.GetLength(r) != lengths[r]) return false;
            return true;
        }

        object ReadCollection(Tag tag, object existing)
        {
            var te = ReadType();
            int count = _in.Int();
            object coll = null;
            var st = te.Save;
            if (st != null && (st.Kind == TypeKind.List || st.Kind == TypeKind.HashSet || st.Kind == TypeKind.Queue || st.Kind == TypeKind.Stack))
            {
                if (CanReuse(existing, te.Type))
                {
                    coll = existing;
                    _claimed.Add(coll);
                    te.Type.GetMethod("Clear", Type.EmptyTypes).Invoke(coll, null);
                }
                else coll = st.DefaultCtor.Invoke(null);
            }
            _objs.Add(coll);
            var el = st?.Element ?? typeof(object);
            var items = new object[count];
            for (int i = 0; i < count; i++)
            {
                object v;
                try { v = ReadValue(el, null, false, false); }
                catch (SaveException e) { throw new SaveException("[" + i + "]." + e.Message, e.InnerException); }
                if (v == SkipMarker || !Convert(el, ref v)) v = el.IsValueType ? Activator.CreateInstance(el) : null;
                items[i] = v;
            }
            if (coll == null) return null;
            if (coll is IList list) { foreach (var x in items) list.Add(x); return coll; }
            var add = new object[1];
            if (st.Kind == TypeKind.Stack)
                for (int i = count - 1; i >= 0; i--) { add[0] = items[i]; st.AddMethod.Invoke(coll, add); }
            else
                for (int i = 0; i < count; i++) { add[0] = items[i]; st.AddMethod.Invoke(coll, add); }
            return coll;
        }

        object ReadDictionary(object existing, bool merge)
        {
            var te = ReadType();
            int count = _in.Int();
            IDictionary d = null;
            var st = te.Save;
            if (st != null && st.Kind == TypeKind.Dictionary)
            {
                if (CanReuse(existing, te.Type))
                {
                    d = (IDictionary)existing;
                    _claimed.Add(d);
                    if (!merge) d.Clear();
                }
                else d = (IDictionary)st.DefaultCtor.Invoke(null);
            }
            _objs.Add(d);
            var kt = st?.Key ?? typeof(object);
            var vt = st?.Value ?? typeof(object);
            for (int i = 0; i < count; i++)
            {
                var k = ReadValue(kt, null, false, false);
                object v;
                try { v = ReadValue(vt, null, false, false); }
                catch (SaveException e) { throw new SaveException("[" + k + "]." + e.Message, e.InnerException); }
                if (d == null || k == null || k == SkipMarker || v == SkipMarker) continue;
                if (!Convert(kt, ref k) || !Convert(vt, ref v)) { Warnings.Add($"{te.Name}: entry {k} does not fit (dropped)"); continue; }
                d[k] = v;
            }
            return d;
        }
    }
}
