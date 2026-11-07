using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Save
{
    /// <summary>How a type is written.</summary>
    enum TypeKind : byte
    {
        Unknown = 0,
        Primitive = 1,
        String = 2,
        Enum = 3,
        Object = 4,
        Struct = 5,
        Array = 6,
        List = 7,
        Dictionary = 8,
        HashSet = 9,
        Queue = 10,
        Stack = 11,
        Skip = 12,
        TypeValue = 13,
        DateTime = 14,
        Unsupported = 15,
    }

    /// <summary>A saved field of an object or struct type.</summary>
    sealed class SaveField
    {
        public readonly string Name;
        public readonly FieldInfo Info;
        public readonly Type Type;
        public readonly bool ReadOnly;
        /// <summary>The field holds an extension bag (<c>Dictionary&lt;string, object&gt;</c>): merged into the fresh one on load.</summary>
        public readonly bool Merge;

        public SaveField(string name, FieldInfo f)
        {
            Name = name;
            Info = f;
            Type = f.FieldType;
            ReadOnly = f.IsInitOnly;
            Merge = f.FieldType == typeof(Dictionary<string, object>);
        }
    }

    /// <summary>Cached reflection data of a type.</summary>
    sealed class SaveType
    {
        public readonly Type Type;
        public readonly TypeKind Kind;
        public readonly SaveField[] Fields;
        readonly Dictionary<string, SaveField> _byName;
        public readonly Type Element, Key, Value;
        public readonly ConstructorInfo DefaultCtor;
        public readonly bool PrimitiveElements;
        public readonly MethodInfo AddMethod;

        public SaveType(Type t, bool machinery)
        {
            Type = t;
            Kind = SaveTypes.KindOf(t, machinery);
            if (Kind == TypeKind.Object || Kind == TypeKind.Struct)
            {
                var list = new List<SaveField>();
                var names = new HashSet<string>();
                var chain = new List<Type>();
                for (var x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType) chain.Add(x);
                chain.Reverse();
                foreach (var level in chain)
                {
                    var fs = level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    Array.Sort(fs, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                    foreach (var f in fs)
                    {
                        if (SaveTypes.IsSkippedField(f, machinery)) continue;
                        string name = f.Name;
                        if (!names.Add(name)) { name = level.Name + "." + f.Name; names.Add(name); }
                        list.Add(new SaveField(name, f));
                    }
                }
                Fields = list.ToArray();
                _byName = new Dictionary<string, SaveField>();
                foreach (var f in Fields) _byName[f.Name] = f;
                if (Kind == TypeKind.Object) DefaultCtor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            }
            else if (Kind == TypeKind.Array)
            {
                Element = t.GetElementType();
                PrimitiveElements = Element.IsPrimitive && Element != typeof(IntPtr) && Element != typeof(UIntPtr);
            }
            else if (Kind == TypeKind.List || Kind == TypeKind.HashSet || Kind == TypeKind.Queue || Kind == TypeKind.Stack)
            {
                Element = t.GetGenericArguments()[0];
                string add = Kind == TypeKind.Queue ? "Enqueue" : Kind == TypeKind.Stack ? "Push" : "Add";
                AddMethod = t.GetMethod(add, new[] { Element });
                DefaultCtor = t.GetConstructor(Type.EmptyTypes);
            }
            else if (Kind == TypeKind.Dictionary)
            {
                var a = t.GetGenericArguments();
                Key = a[0];
                Value = a[1];
                DefaultCtor = t.GetConstructor(Type.EmptyTypes);
            }
        }

        public SaveField Field(string name) => _byName != null && _byName.TryGetValue(name, out var f) ? f : null;

        public object NewObject()
        {
            if (DefaultCtor != null) return DefaultCtor.Invoke(null);
#pragma warning disable SYSLIB0050
            return FormatterServices.GetUninitializedObject(Type);
#pragma warning restore SYSLIB0050
        }
    }

    /// <summary>
    /// The type rules of the save serializer. Everything reachable from the <see cref="World"/> through
    /// instance fields (public or private) is saved, except:
    /// <list type="bullet">
    /// <item>static content and machinery: <see cref="Data.GameData"/>, <see cref="Registry{T}"/>,
    /// <see cref="RuleSet"/>, <see cref="SimPipeline"/>, <see cref="EventBus"/>, rules, systems, setup
    /// steps, delegates (and collections of them) — the loaded world gets fresh ones;</item>
    /// <item>types and fields marked <see cref="NotSavedAttribute"/> (or [NonSerialized]);</item>
    /// <item><see cref="IContent"/> objects registered in the world's game data are written as (type, id)
    /// references and resolved against the game data on load.</item>
    /// </list>
    /// Extensions can exclude further types with <see cref="Exclude"/>.
    /// </summary>
    public static class SaveTypes
    {
        static readonly object Lock = new object();
        static readonly List<Type> Excluded = new List<Type>
        {
            typeof(Data.GameData), typeof(Registry<>), typeof(RuleSet), typeof(SimPipeline), typeof(EventBus),
            typeof(Delegate), typeof(ISimSystem), typeof(IRule), typeof(ISetupStep), typeof(Formats.TextResource),
            // Content and caches derived from it (rebuilt by the fresh world or by ISaveHooks).
            typeof(Hall.HallData), typeof(Hall.HallLayout), typeof(Data.CityDistanceCache),
        };
        /// <summary>Still excluded in the machinery-inclusive hash (they hold delegates or content).</summary>
        static readonly List<Type> AlwaysExcluded = new List<Type>
        {
            typeof(Data.GameData), typeof(Registry<>), typeof(EventBus), typeof(Delegate), typeof(ISetupStep), typeof(Formats.TextResource),
            typeof(Hall.HallData), typeof(Hall.HallLayout), typeof(Data.CityDistanceCache),
        };
        static readonly Dictionary<Type, SaveType> Cache = new Dictionary<Type, SaveType>();
        static readonly Dictionary<Type, SaveType> CacheMachinery = new Dictionary<Type, SaveType>();
        static readonly Dictionary<Type, bool> SkipCache = new Dictionary<Type, bool>();
        static readonly Dictionary<Type, bool> SkipCacheMachinery = new Dictionary<Type, bool>();

        /// <summary>Never save values of <paramref name="t"/> (or of types derived from / implementing it).</summary>
        public static void Exclude(Type t)
        {
            lock (Lock)
            {
                if (!Excluded.Contains(t)) Excluded.Add(t);
                if (!AlwaysExcluded.Contains(t)) AlwaysExcluded.Add(t);
                Cache.Clear(); CacheMachinery.Clear(); SkipCache.Clear(); SkipCacheMachinery.Clear();
            }
        }

        internal static SaveType Get(Type t, bool machinery)
        {
            lock (Lock)
            {
                var c = machinery ? CacheMachinery : Cache;
                if (!c.TryGetValue(t, out var st)) c[t] = st = new SaveType(t, machinery);
                return st;
            }
        }

        /// <summary>True when values of this runtime/declared type are not saved.</summary>
        internal static bool IsSkipped(Type t, bool machinery)
        {
            lock (Lock)
            {
                var c = machinery ? SkipCacheMachinery : SkipCache;
                if (c.TryGetValue(t, out var s)) return s;
                c[t] = false; // recursion guard
                s = ComputeSkipped(t, machinery);
                c[t] = s;
                return s;
            }
        }

        static bool ComputeSkipped(Type t, bool machinery)
        {
            if (t.IsArray) return IsSkipped(t.GetElementType(), machinery);
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(HashSet<>) || def == typeof(Queue<>) || def == typeof(Stack<>))
                    return IsSkipped(t.GetGenericArguments()[0], machinery);
                if (def == typeof(Dictionary<,>))
                {
                    var a = t.GetGenericArguments();
                    return IsSkipped(a[1], machinery) && a[1] != typeof(object);
                }
            }
            if (t.IsDefined(typeof(NotSavedAttribute), false)) return true;
            foreach (var x in machinery ? AlwaysExcluded : Excluded)
            {
                if (x.IsGenericTypeDefinition)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == x) return true;
                }
                else if (x.IsAssignableFrom(t)) return true;
            }
            return false;
        }

        internal static bool IsSkippedField(FieldInfo f, bool machinery)
        {
            #pragma warning disable SYSLIB0050
            if (f.IsDefined(typeof(NotSavedAttribute), false) || f.IsNotSerialized) return true;
#pragma warning restore SYSLIB0050
            var t = f.FieldType;
            if (t.IsPointer || t.IsByRef) return true;
            // object/interface fields are decided per value.
            return t != typeof(object) && IsSkipped(t, machinery);
        }

        internal static TypeKind KindOf(Type t, bool machinery)
        {
            if (IsSkipped(t, machinery)) return TypeKind.Skip;
            if (t == typeof(string)) return TypeKind.String;
            if (t.IsEnum) return TypeKind.Enum;
            if (t.IsPrimitive || t == typeof(decimal)) return t == typeof(IntPtr) || t == typeof(UIntPtr) ? TypeKind.Unsupported : TypeKind.Primitive;
            if (t == typeof(DateTime)) return TypeKind.DateTime;
            if (typeof(Type).IsAssignableFrom(t)) return TypeKind.TypeValue;
            if (t.IsArray) return TypeKind.Array;
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(List<>)) return TypeKind.List;
                if (def == typeof(Dictionary<,>)) return TypeKind.Dictionary;
                if (def == typeof(HashSet<>)) return TypeKind.HashSet;
                if (def == typeof(Queue<>)) return TypeKind.Queue;
                if (def == typeof(Stack<>)) return TypeKind.Stack;
                if (def == typeof(Nullable<>)) return TypeKind.Unsupported; // never a runtime type
            }
            if (t.IsPointer || t.IsByRef || t.IsAbstract || t.IsInterface) return TypeKind.Unsupported;
            // Framework types are only saved through the explicit cases above (their private layout differs
            // between runtimes), except plain field structs like KeyValuePair and ValueTuple.
            string ns = t.Namespace ?? "";
            if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
            {
                bool ok = t.IsValueType && t.IsGenericType &&
                          (t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) || t.FullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal));
                if (!ok) return TypeKind.Unsupported;
            }
            return t.IsValueType ? TypeKind.Struct : TypeKind.Object;
        }
    }
}
