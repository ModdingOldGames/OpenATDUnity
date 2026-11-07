using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// Credits (rooms-and-ui-4 §5): credits.gli BACK centred with the LEFT/RIGHT frames on top; lines
    /// from std "Cred" 1000.. (until "EOF") scroll up 1 px per 60 ms (Ctrl: 6x faster), 40 px per line,
    /// in the big grey font. Never ends by itself; any click or key returns to the main menu.
    /// </summary>
    [ScreenName("credits")]
    public sealed class CreditsScreen : GameScreen
    {
        const string Lib = "room/credits.gli";
        readonly List<string> _lines = new List<string>();
        float _pos = -2, _acc;

        public override void OnEnter()
        {
            for (int id = 1000; id < 3000; id++)
            {
                var t = Assets.StdText.Get("Cred", id);
                if (t == null || t.Trim() == "EOF") break;
                _lines.Add(t.Length <= 1 ? "" : t);
            }
        }

        public override void Update(InputState input, float dt)
        {
            _acc += dt;
            float step = input.Ctrl ? 0.01f : 0.06f;
            while (_acc >= step) { _acc -= step; _pos += 1; }
            if (input.LeftDown || input.RightDown || input.Escape || input.Enter || input.Space || input.Typed.Count > 0)
                App.Screens.Set(new MainMenuScreen());
        }

        public override void Draw(Screen2D s)
        {
            var back = Assets.Texture(Lib, "BACK");
            WideFill.DrawCentered(s, back);
            int ox = OX;
            var f = App.Ui.Kit.BigGrey;
            s.PushClip(new RectInt(ox, 0, 640, 480));
            int first = Mathf.Max(0, (int)(_pos / 40) - 13);
            for (int i = first; i < _lines.Count; i++)
            {
                int y = 480 + i * 40 - (int)_pos;
                if (y > 480) break;
                if (y < -40) continue;
                f.Draw(s, ox + 40 + 280, y, _lines[i], TextAlign.Center);
            }
            s.PopClip();
            s.Draw(Assets.Texture(Lib, "LEFT"), ox, 0);
            var right = Assets.Texture(Lib, "RIGHT");
            s.Draw(right, ox + 640 - right.width, 0);
        }

        public override void OnExit() => Assets.Unload(Lib);
    }

    /// <summary>
    /// Full-screen Smacker playback with sound (intro/outro, rooms-and-ui-4 §6): centred over black,
    /// MIDI paused while it plays; a click, Esc or the last frame continues with <see cref="_next"/>.
    /// </summary>
    [ScreenName("intro")]
    public sealed class VideoScreen : GameScreen
    {
        readonly string _file;
        readonly Func<GameScreen> _next;
        SmackerVideo _video;
        Texture2D _tex;
        byte[] _rgba;
        float _frameTimer;
        AudioSource _src;
        readonly Queue<float> _audio = new Queue<float>();
        readonly object _lock = new object();
        int _channels = 1;

        public VideoScreen() : this("intro/intro.smk", () => new MainMenuScreen()) { }
        public VideoScreen(string file, Func<GameScreen> next) { _file = file; _next = next; }

        public override void OnEnter()
        {
            var path = Assets.Install.Find(_file);
            if (path == null) { Done(); return; }
            try { _video = SmackerVideo.Open(path); }
            catch (Exception e) { Debug.LogWarning("OpenAT: " + _file + ": " + e.Message); Done(); return; }
            App.Music?.Stop();
            _tex = new Texture2D(_video.Width, _video.DisplayHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _rgba = new byte[_video.Width * _video.DisplayHeight * 4];
            if (_video.HasAudio && _video.SampleRate > 0)
            {
                _channels = Mathf.Max(1, _video.Channels);
                var clip = AudioClip.Create("smk-audio", _video.SampleRate * 3600, _channels, _video.SampleRate, true, OnRead);
                _src = App.gameObject.AddComponent<AudioSource>();
                _src.clip = clip; _src.loop = false; _src.volume = App.Audio.EffectsVolume;
                _src.Play();
            }
            NextFrame();
        }

        void OnRead(float[] data)
        {
            lock (_lock)
                for (int i = 0; i < data.Length; i++) data[i] = _audio.Count > 0 ? _audio.Dequeue() : 0f;
        }

        void NextFrame()
        {
            if (!_video.DecodeNextFrame()) { Done(); return; }
            var pcm = _video.LastAudio;
            if (_src != null && pcm != null && pcm.Length > 0)
                lock (_lock) foreach (var v in pcm) _audio.Enqueue(v / 32768f);
            var idx = _video.Indices; var pal = _video.Palette;
            int w = _video.Width, h = _video.DisplayHeight;
            bool doubled = h != _video.Height;
            for (int y = 0; y < h; y++)
            {
                int sy = doubled ? y / 2 : y;
                bool blank = doubled && _video.IsYInterlaced && (y & 1) == 1;
                for (int x = 0; x < w; x++)
                {
                    uint c = blank ? 0xFF000000u : pal[idx[sy * w + x]];
                    int d = (y * w + x) * 4;
                    _rgba[d] = (byte)c; _rgba[d + 1] = (byte)(c >> 8); _rgba[d + 2] = (byte)(c >> 16); _rgba[d + 3] = 255;
                }
            }
            _tex.LoadRawTextureData(_rgba);
            _tex.Apply(false, false);
        }

        void Done()
        {
            if (_done) return;
            _done = true;
            App.Screens.Set(_next());
        }
        bool _done;

        public override void Update(InputState input, float dt)
        {
            if (_video == null || _done) return;
            if (input.LeftDown || input.RightDown || input.Escape) { Done(); return; }
            _frameTimer += dt;
            float frame = (float)(1.0 / Math.Max(1.0, _video.FrameRate));
            int guard = 0;
            while (_frameTimer >= frame && guard++ < 3 && !_done) { _frameTimer -= frame; NextFrame(); }
            if (guard >= 3) _frameTimer = 0;
        }

        public override void Draw(Screen2D s)
        {
            if (_tex == null) return;
            s.Draw(_tex, (s.Width - _tex.width) / 2, (s.Height - _tex.height) / 2);
        }

        public override void OnExit()
        {
            if (_src != null) { _src.Stop(); UnityEngine.Object.Destroy(_src); }
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (PlayerPrefs.GetInt(OptionsScreen.Prefix + "Midi", 1) != 0) App.Music?.Play("SOUND/title.mid", true);
        }
    }
}
