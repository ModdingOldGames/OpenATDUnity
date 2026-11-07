using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenAT.Sim;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.UI.Dialog
{
    /// <summary>
    /// Text helpers for dialog lines: printf-style formatting of resource texts and the embedded
    /// voice markers <c>[[...]]</c> (rooms-and-ui-3 §5.1).
    /// </summary>
    public static class DialogText
    {
        /// <summary>
        /// Minimal printf: %s %d %i %u %li %ld %lu %x and %%; flags/width are accepted and applied to
        /// numbers (zero padding / width). Missing arguments print nothing.
        /// </summary>
        public static string Format(string fmt, object[] args)
        {
            if (fmt == null) return "";
            if (fmt.IndexOf('%') < 0) return fmt;
            args = args ?? Array.Empty<object>();
            var sb = new StringBuilder(fmt.Length + 16);
            int a = 0;
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c != '%' || i + 1 >= fmt.Length) { sb.Append(c); continue; }
                int j = i + 1;
                if (fmt[j] == '%') { sb.Append('%'); i = j; continue; }
                bool zero = false, left = false;
                while (j < fmt.Length && (fmt[j] == '0' || fmt[j] == '-' || fmt[j] == '+' || fmt[j] == ' '))
                {
                    if (fmt[j] == '0') zero = true;
                    if (fmt[j] == '-') left = true;
                    j++;
                }
                int width = 0;
                while (j < fmt.Length && char.IsDigit(fmt[j])) width = width * 10 + (fmt[j++] - '0');
                while (j < fmt.Length && (fmt[j] == 'l' || fmt[j] == 'h')) j++;
                if (j >= fmt.Length) { sb.Append(fmt, i, fmt.Length - i); break; }
                char conv = fmt[j];
                object arg = a < args.Length ? args[a] : null;
                string v;
                switch (conv)
                {
                    case 's': v = arg?.ToString() ?? ""; a++; break;
                    case 'd': case 'i': case 'u': v = arg == null ? "" : Convert.ToInt64(arg, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); a++; break;
                    case 'x': case 'X': v = arg == null ? "" : Convert.ToInt64(arg, CultureInfo.InvariantCulture).ToString(conv == 'x' ? "x" : "X"); a++; break;
                    case 'c': v = arg?.ToString() ?? ""; a++; break;
                    default: sb.Append(fmt, i, j - i + 1); i = j; continue;
                }
                if (v.Length < width) v = left ? v.PadRight(width) : v.PadLeft(width, zero && conv != 's' ? '0' : ' ');
                sb.Append(v);
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>Display text: voice markers removed, double spaces collapsed, trimmed.</summary>
        public static string StripVoice(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw ?? "";
            var sb = new StringBuilder(raw.Length);
            int i = 0;
            while (i < raw.Length)
            {
                int m = raw.IndexOf("[[", i, StringComparison.Ordinal);
                if (m < 0) { sb.Append(raw, i, raw.Length - i); break; }
                sb.Append(raw, i, m - i);
                int e = raw.IndexOf("]]", m + 2, StringComparison.Ordinal);
                if (e < 0) { i = raw.Length; break; }
                i = e + 2;
            }
            var s = sb.ToString();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        /// <summary>
        /// Install-relative voice files referenced by a (formatted) line, in order. "P1" in player
        /// lines is replaced by the speaking player's directory P1..P4. Dynamic words ([[*]]) are
        /// matched against airline names (nameN.raw) and city names (city code).
        /// </summary>
        public static List<string> VoiceParts(string raw, int speakingPlayer, World world)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(raw)) return parts;
            string lastDir = null;
            int i = 0;
            while (true)
            {
                int m = raw.IndexOf("[[", i, StringComparison.Ordinal);
                if (m < 0) break;
                int e = raw.IndexOf("]]", m + 2, StringComparison.Ordinal);
                if (e < 0) break;
                string tag = raw.Substring(m + 2, e - m - 2).Replace('/', '\\');
                i = e + 2;
                if (tag == "*")
                {
                    if (lastDir == null) continue;
                    int next = raw.IndexOf("[[", i, StringComparison.Ordinal);
                    string follow = (next < 0 ? raw.Substring(i) : raw.Substring(i, next - i)).TrimStart();
                    string word = MatchWord(follow, world);
                    if (word != null) parts.Add("VOICE/" + lastDir + "/" + word + ".raw");
                    continue;
                }
                if (tag.StartsWith("P1\\", StringComparison.OrdinalIgnoreCase))
                    tag = "P" + (Mathf.Clamp(speakingPlayer, 0, 3) + 1) + tag.Substring(2);
                int bs = tag.IndexOf('\\');
                lastDir = bs > 0 ? tag.Substring(0, bs) : tag;
                if (tag.EndsWith("\\")) continue; // directory only
                parts.Add("VOICE/" + tag.Replace('\\', '/') + ".raw");
            }
            return parts;
        }

        static string MatchWord(string follow, World world)
        {
            if (world == null) return null;
            for (int k = 0; k < Player.Count; k++)
            {
                var name = world.Players[k].Airline;
                if (!string.IsNullOrEmpty(name) && follow.StartsWith(name.Trim(), StringComparison.OrdinalIgnoreCase)) return "name" + (k + 1);
            }
            foreach (var c in world.Data.Cities.All)
                if (!string.IsNullOrEmpty(c.Name) && follow.StartsWith(c.Name, StringComparison.OrdinalIgnoreCase))
                    return c.GoodCode ?? c.ShortCode;
            return null; // plane types / numbers: TODO (numbers are silent in the shipped game anyway)
        }

        /// <summary>The line from dlg_ger.res, formatted; null if missing.</summary>
        public static string Get(GameAssets assets, string group, int id, object[] args)
        {
            var t = assets.DialogText.Get(group, id);
            return t == null ? null : Format(t, args);
        }
    }

    /// <summary>
    /// A voice line assembled from its marker parts (concatenated into one clip) plus the
    /// "mouth open" detector used for lip-sync (rooms-and-ui-3 §4.3: 10 samples over the next
    /// ~1000 samples after a look-ahead; open when any |sample| &gt; 512 of 32768).
    /// </summary>
    public sealed class VoiceLine
    {
        public AudioClip Clip;
        public float[] Samples;
        public bool AnyMissing;

        static readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        public static VoiceLine Build(GameAssets assets, IList<string> parts)
        {
            if (parts == null || parts.Count == 0) return null;
            var list = new List<float[]>();
            bool missing = false;
            int total = 0;
            foreach (var p in parts)
            {
                if (!_cache.TryGetValue(p, out var s))
                {
                    var path = assets.Install.Find(p);
                    s = path != null ? Formats.RawSound.ToFloat(System.IO.File.ReadAllBytes(path)) : null;
                    if (_cache.Count > 400) _cache.Clear();
                    _cache[p] = s;
                }
                if (s == null || s.Length == 0) { missing = true; continue; }
                list.Add(s); total += s.Length;
            }
            if (total == 0) return new VoiceLine { AnyMissing = true };
            var all = new float[total];
            int o = 0;
            foreach (var s in list) { Array.Copy(s, 0, all, o, s.Length); o += s.Length; }
            var clip = AudioClip.Create("voice", total, Formats.RawSound.Channels, Formats.RawSound.SampleRate, false);
            clip.SetData(all, 0);
            return new VoiceLine { Clip = clip, Samples = all, AnyMissing = missing };
        }

        public bool MouthOpen(int playbackSample, float lookAheadMs)
        {
            if (Samples == null) return false;
            int start = playbackSample + (int)(lookAheadMs * Formats.RawSound.SampleRate / 1000f);
            for (int k = 0; k < 10; k++)
            {
                int idx = start + k * 100;
                if (idx >= 0 && idx < Samples.Length && Mathf.Abs(Samples[idx]) > 512f / 32768f) return true;
            }
            return false;
        }
    }
}
