using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace OpenAT.Sim.Save
{
    /// <summary>
    /// Assembly-independent type names: <c>Namespace.Name</c>, nested types with '+', generic arguments
    /// in angle brackets (<c>System.Collections.Generic.List`1&lt;OpenAT.Sim.Fleet.Plane&gt;</c>), arrays
    /// as <c>[]</c> / <c>[,]</c>. Resolved against every loaded assembly, so a save written by the Unity
    /// build (assembly OpenAT.Sim) loads in the test/CLI builds and vice versa.
    /// </summary>
    static class TypeNames
    {
        static readonly object Lock = new object();
        static readonly Dictionary<Type, string> Names = new Dictionary<Type, string>();
        static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

        public static string Of(Type t)
        {
            lock (Lock)
            {
                if (Names.TryGetValue(t, out var n)) return n;
                var sb = new StringBuilder();
                Append(sb, t);
                n = sb.ToString();
                Names[t] = n;
                return n;
            }
        }

        static void Append(StringBuilder sb, Type t)
        {
            if (t.IsArray)
            {
                Append(sb, t.GetElementType());
                sb.Append('[').Append(',', t.GetArrayRank() - 1).Append(']');
                return;
            }
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                var def = t.GetGenericTypeDefinition();
                sb.Append(def.FullName).Append('<');
                var args = t.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    Append(sb, args[i]);
                }
                sb.Append('>');
                return;
            }
            sb.Append(t.FullName ?? t.Name);
        }

        /// <summary>The type, or null when no loaded assembly defines it (removed code / missing mod).</summary>
        public static Type Resolve(string name)
        {
            lock (Lock)
            {
                if (Types.TryGetValue(name, out var t)) return t;
                int pos = 0;
                t = Parse(name, ref pos);
                if (pos != name.Length) t = null;
                Types[name] = t;
                return t;
            }
        }

        static Type Parse(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && s[pos] != '<' && s[pos] != '>' && s[pos] != ',' && s[pos] != '[') pos++;
            string baseName = s.Substring(start, pos - start);
            Type t;
            if (pos < s.Length && s[pos] == '<')
            {
                pos++;
                var args = new List<Type>();
                while (true)
                {
                    var a = Parse(s, ref pos);
                    args.Add(a);
                    if (pos >= s.Length) return null;
                    if (s[pos] == ',') { pos++; continue; }
                    if (s[pos] == '>') { pos++; break; }
                    return null;
                }
                var def = FindSimple(baseName);
                if (def == null || args.Contains(null)) t = null;
                else
                {
                    try { t = def.MakeGenericType(args.ToArray()); }
                    catch (ArgumentException) { t = null; }
                }
            }
            else t = FindSimple(baseName);
            while (pos < s.Length && s[pos] == '[')
            {
                int rank = 1;
                pos++;
                while (pos < s.Length && s[pos] == ',') { rank++; pos++; }
                if (pos >= s.Length || s[pos] != ']') return null;
                pos++;
                if (t != null) t = rank == 1 ? t.MakeArrayType() : t.MakeArrayType(rank);
            }
            return t;
        }

        static Type FindSimple(string fullName)
        {
            var t = Type.GetType(fullName, false);
            if (t != null) return t;
            // Our own assembly first (the usual case), then everything loaded.
            t = typeof(TypeNames).Assembly.GetType(fullName, false);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                try
                {
                    t = a.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch (Exception) { /* some assemblies refuse reflection; skip them */ }
            }
            return null;
        }
    }
}
