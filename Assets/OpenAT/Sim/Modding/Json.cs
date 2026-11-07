using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenAT.Sim.Modding
{
    /// <summary>
    /// A JSON object that keeps its keys in file order (deterministic application of data mods).
    /// Lookups are case-insensitive.
    /// </summary>
    public sealed class JsonObject
    {
        readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int Count => _items.Count;
        public IReadOnlyList<KeyValuePair<string, object>> Entries => _items;

        public void Set(string key, object value)
        {
            if (_index.TryGetValue(key, out int i)) _items[i] = new KeyValuePair<string, object>(key, value);
            else { _index[key] = _items.Count; _items.Add(new KeyValuePair<string, object>(key, value)); }
        }

        public bool Has(string key) => _index.ContainsKey(key);
        public object this[string key] => _index.TryGetValue(key, out int i) ? _items[i].Value : null;

        public string Str(string key, string def = null)
        {
            var v = this[key];
            return v is string s ? s : v is double || v is bool ? Json.ToInvariantString(v) : def;
        }
        public double Num(string key, double def = 0) => Json.TryNumber(this[key], out double d) ? d : def;
        public int Int(string key, int def = 0) => Json.TryNumber(this[key], out double d) ? (int)d : def;
        public bool Bool(string key, bool def = false) => this[key] is bool b ? b : def;
        public JsonObject Obj(string key) => this[key] as JsonObject;
        public List<object> Arr(string key) => this[key] as List<object>;
    }

    /// <summary>
    /// Minimal JSON reader (RFC 8259 plus // and /* */ comments and trailing commas, for hand-written mod
    /// files). Values: <see cref="JsonObject"/>, <c>List&lt;object&gt;</c>, string, double, bool, null.
    /// No external packages (the Sim targets netstandard2.1 without dependencies).
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.Ws();
            var v = p.Value();
            p.Ws();
            if (!p.End) throw p.Error("unexpected text after the value");
            return v;
        }

        public static JsonObject ParseObject(string text) =>
            Parse(text) as JsonObject ?? throw new FormatException("JSON: the file must contain an object");

        public static bool TryNumber(object v, out double d)
        {
            switch (v)
            {
                case double x: d = x; return true;
                case bool b: d = b ? 1 : 0; return true;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                default: d = 0; return false;
            }
        }

        public static string ToInvariantString(object v) =>
            v is double d ? d.ToString("R", CultureInfo.InvariantCulture) : v is bool b ? (b ? "true" : "false") : v?.ToString();

        sealed class Parser
        {
            readonly string _s;
            int _p;
            public Parser(string s) { _s = s; _p = s.Length > 0 && s[0] == '﻿' ? 1 : 0; }
            public bool End => _p >= _s.Length;

            public FormatException Error(string msg)
            {
                int line = 1, col = 1;
                for (int i = 0; i < _p && i < _s.Length; i++) { if (_s[i] == '\n') { line++; col = 1; } else col++; }
                return new FormatException($"JSON: {msg} at line {line}, column {col}");
            }

            public void Ws()
            {
                while (_p < _s.Length)
                {
                    char c = _s[_p];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _p++; continue; }
                    if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '/') { while (_p < _s.Length && _s[_p] != '\n') _p++; continue; }
                    if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '*')
                    {
                        int e = _s.IndexOf("*/", _p + 2, StringComparison.Ordinal);
                        if (e < 0) throw Error("unterminated comment");
                        _p = e + 2;
                        continue;
                    }
                    break;
                }
            }

            public object Value()
            {
                if (End) throw Error("value expected");
                char c = _s[_p];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            void Word(string w)
            {
                if (string.CompareOrdinal(_s, _p, w, 0, w.Length) != 0) throw Error("'" + w + "' expected");
                _p += w.Length;
            }

            JsonObject Object()
            {
                var o = new JsonObject();
                _p++;
                Ws();
                if (!End && _s[_p] == '}') { _p++; return o; }
                while (true)
                {
                    Ws();
                    if (!End && _s[_p] == '}') { _p++; return o; }   // trailing comma
                    if (End || _s[_p] != '"') throw Error("property name expected");
                    string key = String();
                    Ws();
                    if (End || _s[_p] != ':') throw Error("':' expected");
                    _p++;
                    Ws();
                    o.Set(key, Value());
                    Ws();
                    if (End) throw Error("'}' expected");
                    if (_s[_p] == ',') { _p++; continue; }
                    if (_s[_p] == '}') { _p++; return o; }
                    throw Error("',' or '}' expected");
                }
            }

            List<object> Array()
            {
                var a = new List<object>();
                _p++;
                Ws();
                if (!End && _s[_p] == ']') { _p++; return a; }
                while (true)
                {
                    Ws();
                    if (!End && _s[_p] == ']') { _p++; return a; }
                    a.Add(Value());
                    Ws();
                    if (End) throw Error("']' expected");
                    if (_s[_p] == ',') { _p++; continue; }
                    if (_s[_p] == ']') { _p++; return a; }
                    throw Error("',' or ']' expected");
                }
            }

            string String()
            {
                _p++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("unterminated string");
                    char c = _s[_p++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("unterminated escape");
                    char e = _s[_p++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_p + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _p += 4;
                            break;
                        default: throw Error("bad escape '\\" + e + "'");
                    }
                }
            }

            double Number()
            {
                int start = _p;
                if (_s[_p] == '-') _p++;
                while (_p < _s.Length && "0123456789.eE+-".IndexOf(_s[_p]) >= 0) _p++;
                string t = _s.Substring(start, _p - start);
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw Error("bad number '" + t + "'");
                return d;
            }
        }
    }
}
