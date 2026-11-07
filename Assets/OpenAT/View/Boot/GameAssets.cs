using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Loads and caches original assets from the user's install as Unity objects.
    /// Bitmaps are addressed as (library path, name), e.g. ("room/bank.gli", "BANK").
    /// Missing assets return magenta placeholders so screens still work during development.
    /// </summary>
    public sealed class GameAssets
    {
        public readonly GameInstall Install;

        readonly Dictionary<string, GliLibrary> _libs = new Dictionary<string, GliLibrary>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, BitmapFont> _fonts = new Dictionary<string, BitmapFont>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TextResource> _texts = new Dictionary<string, TextResource>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AudioClip> _sounds = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        Texture2D _missing;

        public TextResource.Language Language = TextResource.Language.English;

        public GameAssets(GameInstall install)
        {
            Install = install;
            // The install's language (misc/sabbel.dat) picks the text segments and the hotkey table, as in the original.
            var sabbel = install.Find(GameInstall.Folders.Misc, "sabbel.dat");
            Language = TextResource.InstallLanguage(sabbel != null ? System.IO.File.ReadAllBytes(sabbel) : null);
        }

        // ------------------------------------------------------------------ graphics

        public GliLibrary Library(string path)
        {
            if (_libs.TryGetValue(path, out var lib)) return lib;
            var file = Install.Find(path);
            lib = file != null ? GliLibrary.Load(file) : null;
            _libs[path] = lib;
            if (lib == null) Debug.LogWarning("OpenAT: missing graphics library " + path);
            return lib;
        }

        /// <summary>Texture for a named bitmap; transparent where the original colour key is.</summary>
        public Texture2D Texture(string library, string name)
        {
            string key = library + "|" + name;
            if (_textures.TryGetValue(key, out var t)) return t;
            var bmp = Library(library)?.Get(name);
            t = bmp != null ? ToTexture(bmp, key) : Missing;
            _textures[key] = t;
            return t;
        }

        public Bitmap16 Bitmap(string library, string name) => Library(library)?.Get(name);

        public bool Has(string library, string name) => Library(library)?.Contains(name) ?? false;

        public static Texture2D ToTexture(Bitmap16 bmp, string name = null, bool keyed = true)
        {
            var t = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name ?? "bitmap",
            };
            t.LoadRawTextureData(bmp.ToRgba(keyed));
            t.Apply(false, false);
            return t;
        }

        public Texture2D Missing
        {
            get
            {
                if (_missing != null) return _missing;
                _missing = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var px = new Color32[64];
                for (int i = 0; i < 64; i++) px[i] = ((i / 8 + i) & 1) == 0 ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 255);
                _missing.SetPixels32(px);
                _missing.Apply();
                return _missing;
            }
        }

        /// <summary>Drops cached textures of a library (called when leaving a room to save memory).</summary>
        public void Unload(string library)
        {
            var prefix = library + "|";
            var remove = new List<string>();
            foreach (var kv in _textures)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) remove.Add(kv.Key);
            foreach (var k in remove)
            {
                if (_textures[k] != _missing) UnityEngine.Object.Destroy(_textures[k]);
                _textures.Remove(k);
            }
            _libs.Remove(library);
        }

        // ------------------------------------------------------------------ fonts

        /// <summary>Font from misc/ by file name, e.g. "NORMFONT.MCF" or "norm_bl.mcf".</summary>
        public BitmapFont Font(string file)
        {
            if (_fonts.TryGetValue(file, out var f)) return f;
            var path = Install.Find(GameInstall.Folders.Misc, file);
            f = path != null ? new BitmapFont(McfFont.Parse(System.IO.File.ReadAllBytes(path))) : null;
            if (f != null)
            {
                f.Font.BaseLetterFallback = BaseLetterFallback;
                // The original sets single line spacing on its main UI fonts at start-up (rooms-and-ui-1 §4);
                // every other font keeps the 1.5 default.
                if (SingleSpaced.Contains(System.IO.Path.GetFileName(file))) f.Font.LineSpace = 1f;
            }
            _fonts[file] = f;
            return f;
        }

        static readonly HashSet<string> SingleSpaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "status.mcf", "norm_bl.mcf", "norm_gr.mcf", "norm_rt.mcf", "norm_wh.mcf", "norm_x.mcf", "norm_pl.mcf",
            "dlgfont1.mcf", "dlgfont2.mcf", "dlgfont3.mcf", "norm_drk.mcf",
        };

        /// <summary>PlayerPrefs key of the "accented letters as base letters" option (OpenAT addition, off = original).</summary>
        public const string PrefBaseLetterFallback = "OpenAT.Opt.BaseLetterFallback";

        /// <summary>
        /// <see cref="McfFont.BaseLetterFallback"/> for every font: loaded ones now, later ones on load.
        /// Initialised from <see cref="PrefBaseLetterFallback"/>; set by the options screen.
        /// </summary>
        public bool BaseLetterFallback
        {
            get => _baseLetterFallback ?? (_baseLetterFallback = PlayerPrefs.GetInt(PrefBaseLetterFallback, 0) != 0).Value;
            set
            {
                _baseLetterFallback = value;
                foreach (var f in _fonts.Values) if (f != null) f.Font.BaseLetterFallback = value;
            }
        }
        bool? _baseLetterFallback;

        // ------------------------------------------------------------------ texts

        /// <summary>Text resource from misc/, e.g. "std_ger.res".</summary>
        public TextResource Texts(string file)
        {
            if (_texts.TryGetValue(file, out var r)) return r;
            var path = Install.Find(GameInstall.Folders.Misc, file);
            r = path != null ? TextResource.Parse(System.IO.File.ReadAllBytes(path)) : new TextResource();
            r.Lang = Language;
            _texts[file] = r;
            return r;
        }

        public TextResource StdText => Texts("std_ger.res");

        OpenAT.Sim.Data.GameData _gameData;
        /// <summary>Simulation tables (cities, plane types, routes, ...) loaded once.</summary>
        public OpenAT.Sim.Data.GameData GameData
        {
            get
            {
                if (_gameData != null) return _gameData;
                _gameData = OpenAT.View.Modding.ModBoot.Prepare(OpenAT.Sim.Data.GameData.Load(Install, Language));
                // Options page "real codes" (original default on): pick the active city codes.
                _gameData.UseRealCodes(PlayerPrefs.GetInt("OpenAT.Opt.RealCodes", 1) != 0);
                return _gameData;
            }
        }
        public TextResource DialogText => Texts("dlg_ger.res");

        // ------------------------------------------------------------------ sounds

        /// <summary>Sound from a path relative to the install, e.g. "SOUND/CLICK.RAW".</summary>
        public AudioClip Sound(string relativePath)
        {
            if (_sounds.TryGetValue(relativePath, out var c)) return c;
            var path = Install.Find(relativePath);
            if (path != null)
            {
                var samples = RawSound.ToFloat(System.IO.File.ReadAllBytes(path));
                c = AudioClip.Create(relativePath, Math.Max(1, samples.Length), RawSound.Channels, RawSound.SampleRate, false);
                c.SetData(samples, 0);
            }
            _sounds[relativePath] = c;
            return c;
        }
    }
}
