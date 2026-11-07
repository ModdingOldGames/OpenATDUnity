using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>
    /// Shared UI resources of the room layer: the original's named fonts (rooms-and-ui-1 §4, §5, §9),
    /// shared bitmaps from gli/glstd.gli and gli/glbasis.gli, and "white-transparent" variants of
    /// bubble and tooltip pieces (§4.7: bubble white is drawn ~75 % opaque, black is transparent).
    /// </summary>
    public sealed class UiKit
    {
        public const string StdLib = "gli/glstd.gli";
        public const string BasisLib = "gli/glbasis.gli";
        public const string AdvisorLib = "gli/glberatr.gli";

        readonly GameAssets _a;
        readonly Dictionary<string, Texture2D> _whiteTrans = new Dictionary<string, Texture2D>();

        public UiKit(GameAssets assets) => _a = assets;

        public GameAssets Assets => _a;

        // Original font roles -> files in misc/.
        public BitmapFont Cash => _a.Font("status.mcf");
        public BitmapFont SmallBlack => _a.Font("norm_bl.mcf");
        public BitmapFont SmallGrey => _a.Font("norm_gr.mcf");
        public BitmapFont SmallRed => _a.Font("norm_rt.mcf");
        public BitmapFont SmallWhite => _a.Font("norm_wh.mcf");
        public BitmapFont VerySmall => _a.Font("small_bl.mcf");
        public BitmapFont Dialog => _a.Font("dlgfont1.mcf");
        public BitmapFont DialogLight => _a.Font("dlgfont2.mcf");
        public BitmapFont DialogPartner => _a.Font("dlgfont1.mcf");
        public BitmapFont BigGrey => _a.Font("dlgfont3.mcf");
        public BitmapFont BigWhite => _a.Font("dlgfont3.mcf");
        public BitmapFont NormalGreen => _a.Font("norm_ve.mcf");
        public BitmapFont NormalGrey => _a.Font("norm_drk.mcf");
        public BitmapFont BankBlack => _a.Font("bank_bl.mcf");
        public BitmapFont BankRed => _a.Font("bank_ro.mcf");
        public BitmapFont Stat(int n) => _a.Font("stat_" + n + ".mcf");

        public Texture2D Std(string name) => _a.Texture(StdLib, name);
        public bool HasStd(string name) => _a.Has(StdLib, name);

        /// <summary>
        /// A bitmap where the dominant light colour (the bubble/tooltip "white") is semi-transparent
        /// (25 % background shows through), colour key 0 is fully transparent, everything else opaque.
        /// </summary>
        public Texture2D WhiteTrans(string library, string name)
        {
            string key = library + "|" + name;
            if (_whiteTrans.TryGetValue(key, out var t)) return t;
            var bmp = _a.Bitmap(library, name);
            if (bmp == null) { t = _a.Missing; _whiteTrans[key] = t; return t; }
            ushort white = DominantColour(bmp);
            var rgba = bmp.ToRgba(true);
            for (int i = 0; i < bmp.Pixels.Length; i++)
                if (bmp.Pixels[i] == white && white != 0) rgba[i * 4 + 3] = 215;
            t = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = key + "#wt",
            };
            t.LoadRawTextureData(rgba);
            t.Apply(false, false);
            _whiteTrans[key] = t;
            return t;
        }

        static ushort DominantColour(Bitmap16 bmp)
        {
            var count = new Dictionary<ushort, int>();
            ushort best = 0; int bestN = 0;
            foreach (var p in bmp.Pixels)
            {
                if (p == Bitmap16.ColorKey) continue;
                count.TryGetValue(p, out int n);
                count[p] = ++n;
                if (n > bestN) { bestN = n; best = p; }
            }
            return best;
        }

        /// <summary>
        /// The thousands separator: like the original, the Windows system locale's separator when it is one
        /// of <c>. , : / ' `</c>, otherwise a dot (so "1,500,000" on an English system, "1.500.000" on a
        /// German one). Override with the environment variable OPENAT_THOUSANDS (one character).
        /// </summary>
        public static char ThousandsSeparator => _sep != '\0' ? _sep : (_sep = DetectSeparator());
        static char _sep;

        static char DetectSeparator()
        {
            string s = System.Environment.GetEnvironmentVariable("OPENAT_THOUSANDS");
            if (string.IsNullOrEmpty(s))
            {
                try
                {
                    if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        var buf = new System.Text.StringBuilder(4);
                        if (GetLocaleInfoW(0x0800 /* system default */, unchecked((int)0x80000000) | 0x0F /* no user override | thousand */, buf, buf.Capacity) > 0)
                            s = buf.ToString();
                    }
                }
                catch (System.Exception) { s = null; }
                if (string.IsNullOrEmpty(s)) s = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
            }
            char c = string.IsNullOrEmpty(s) ? '.' : s[0];
            return ".,:/'`".IndexOf(c) >= 0 ? c : '.';
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetLocaleInfoW(int locale, int type, System.Text.StringBuilder data, int size);

        /// <summary>Unit format indices of misc/ein_ger.res (data-tables.md §4.2; ids 1000 + index).</summary>
        public const int UnitKm = 0, UnitKmh = 1, UnitM = 2, UnitKn = 3, UnitL = 4, UnitLh = 5, UnitMoney = 6, UnitPercent = 7,
            UnitYears = 8, UnitColonMoney = 9, UnitKg = 10, UnitT = 11, UnitMioMoney = 12, UnitMio = 13;

        static readonly string[] UnitFallback = { "%s km", "%s km/h", "%s m", "%s kN", "%s l", "%s l/h", "%s $", "%s%%", "%s", ": %s $", "%s kg", "%s t", "%s Mio $", " Mio" };

        /// <summary>
        /// A number with thousands separators put into the install's unit format (e.g. "$%s" in the English
        /// release, so money reads "$99,000"), like the original's unit table (CEinheit).
        /// </summary>
        public static string Unit(int index, long value)
        {
            string fmt = UnitFormat(index);
            int at = fmt.IndexOf("%s", System.StringComparison.Ordinal);
            string num = Thousands(value);
            string r = at >= 0 ? fmt.Substring(0, at) + num + fmt.Substring(at + 2) : num + fmt;
            return r.Replace("%%", "%");
        }

        /// <summary>The raw unit format string (printf style, "%s" = number) of the install, or a fallback.</summary>
        public static string UnitFormat(int index)
        {
            string fmt = null;
            try { fmt = GameApp.Instance?.Assets?.Texts("ein_ger.res")?.Get(1000, 1000 + index); } catch (System.Exception) { fmt = null; }
            if (string.IsNullOrEmpty(fmt)) fmt = index >= 0 && index < UnitFallback.Length ? UnitFallback[index] : "%s";
            return fmt;
        }

        /// <summary>Money in the install's currency format; from 10^9 on in millions (the "Mio" format).</summary>
        public static string Money(long value) =>
            // Compared on both sides instead of Math.Abs, which throws for long.MinValue (e.g. a NaN cast to long).
            value >= 1000000000L || value <= -1000000000L ? Unit(UnitMioMoney, value / 1000000) : Unit(UnitMoney, value);

        /// <summary>Thousands separators like the original's money display ("1,500,000" / "1.500.000").</summary>
        public static string Thousands(long v) => Thousands(v, ThousandsSeparator);

        public static string Thousands(long v, char sep)
        {
            bool neg = v < 0;
            ulong u = neg ? (ulong)(-(v + 1)) + 1 : (ulong)v;
            var s = u.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(sep);
                sb.Append(s[i]);
            }
            return (neg ? "-" : "") + sb;
        }
    }
}
