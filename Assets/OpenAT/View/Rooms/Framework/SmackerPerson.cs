using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>Character moods (rooms-and-ui-3 §4.2).</summary>
    public enum Mood { None = 0, Idle = 1, Holy = 2, Talking = 3, AngryTalking = 4, Listening = 5, Letter = 6 }

    /// <summary>One clip of a <see cref="SmackerPerson"/>: an smk file from video/ (empty = invisible pause).</summary>
    public sealed class SmackerClip
    {
        public string File;
        public string Sound;
        public Vector2Int Pos;
        public Mood Mood;
        public int RepeatMin = 1, RepeatMax = 1;
        /// <summary>Hold time of the last frame in 1/20 s.</summary>
        public int WaitMin, WaitMax;
        public bool CanCancel = true;
        /// <summary>Successors: kind 'A' always, 'S' only while staying in the mood, 'E' only as exit.</summary>
        public readonly List<(char kind, int weight, int target)> Next = new List<(char, int, int)>();
        /// <summary>Optional hooks: called at clip start, at clip end, and on each new frame.</summary>
        public Action OnStart, OnEnd;
        public Action<int> OnFrame;
        /// <summary>Optional per-clip decision variable (the original's clip "decision" pointer): a value ≥ 0
        /// picks the next clip directly when this clip ends.</summary>
        public Func<int> Decide;
    }

    /// <summary>
    /// The clip/mood engine for animated characters (rooms-and-ui-3 §4.2): a weighted graph of Smacker
    /// clips; the dialog sets a desired mood and the graph walks towards it through exit clips. Frames
    /// are decoded with <see cref="SmackerVideo"/>; palette index 0 is transparent.
    /// </summary>
    public sealed class SmackerPerson
    {
        public readonly List<SmackerClip> Clips = new List<SmackerClip>();
        public Mood Desired = Mood.Idle;
        public Mood Accept1, Accept2;
        /// <summary>When ≥ 0 forces the next clip (the original's decision variable).</summary>
        public int Decision = -1;
        public bool Visible = true;

        public int ActiveClip { get; private set; } = -1;
        public Mood Current => ActiveClip >= 0 ? Clips[ActiveClip].Mood : Mood.None;
        public int Frame => _video?.CurrentFrame ?? 0;

        readonly GameAssets _assets;
        readonly GameAudio _audio;
        readonly System.Random _rng;
        SmackerVideo _video;
        Texture2D _tex;
        byte[] _rgba;
        int _repeat;
        float _frameTimer, _waitTimer;
        bool _waiting, _cancelGuard;

        static readonly Dictionary<string, byte[]> _fileCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public SmackerPerson(GameAssets assets, GameAudio audio, int seed = 0)
        {
            _assets = assets;
            _audio = audio;
            _rng = new System.Random(seed != 0 ? seed : Environment.TickCount);
        }

        /// <summary>
        /// Adds a clip. <paramref name="next"/> lists successors as "A9:3 E1:7" (kind, weight, clip).
        /// </summary>
        public SmackerClip Clip(int index, string file, int x, int y, Mood mood, string next,
            int repMin = 1, int repMax = 1, int waitMin = 0, int waitMax = 0, bool canCancel = true, string sound = null)
        {
            while (Clips.Count <= index) Clips.Add(new SmackerClip { Mood = Mood.Idle });
            var c = new SmackerClip
            {
                File = file, Sound = sound, Pos = new Vector2Int(x, y), Mood = mood,
                RepeatMin = repMin, RepeatMax = repMax, WaitMin = waitMin, WaitMax = waitMax, CanCancel = canCancel,
            };
            foreach (var tok in (next ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = tok.IndexOf(':');
                c.Next.Add((tok[0], tok[1] - '0', int.Parse(tok.Substring(colon + 1))));
            }
            Clips[index] = c;
            return c;
        }

        public void SetDesired(Mood m, Mood accept1 = Mood.None, Mood accept2 = Mood.None)
        {
            Desired = m; Accept1 = accept1; Accept2 = accept2;
        }

        /// <summary>Stops the active clip immediately and picks the next one.</summary>
        public void ForceNextClip() => NextClip();

        public void Update(float dt)
        {
            if (Clips.Count == 0) return;
            if (ActiveClip < 0) { Start(0); return; }
            var clip = Clips[ActiveClip];
            if (clip.CanCancel && !_waiting && !_cancelGuard && (Desired != clip.Mood && Desired != Mood.None || Decision >= 0))
            {
                NextClip();
                // A cancelled clip that leads to another clip of the wrong mood plays out once.
                _cancelGuard = Current != Desired;
                return;
            }
            if (_waiting)
            {
                _waitTimer -= dt;
                if (_waitTimer <= 0) { _waiting = false; NextClip(); }
                return;
            }
            if (_video == null)
            {
                // Empty clip: an invisible pause of its wait time.
                _waiting = true;
                _waitTimer = Wait(clip);
                if (_waitTimer <= 0) { _waiting = false; NextClip(); }
                return;
            }
            _frameTimer -= dt;
            int guard = 0;
            while (_frameTimer <= 0 && guard++ < 4)
            {
                _frameTimer += (float)(1.0 / Math.Max(1.0, _video.FrameRate));
                if (_video.CurrentFrame >= _video.FrameCount - 1)
                {
                    _repeat--;
                    if (Desired != clip.Mood && Desired != Mood.None) _repeat = 0;
                    if (_repeat <= 0)
                    {
                        float w = Wait(clip);
                        if (w > 0) { _waiting = true; _waitTimer = w; return; }
                        NextClip();
                        return;
                    }
                    _video.Rewind();
                    if (clip.Sound != null && clip.Mood != Mood.Talking) PlaySound(clip);
                }
                if (!_video.DecodeNextFrame()) { NextClip(); return; }
                Upload();
                clip.OnFrame?.Invoke(_video.CurrentFrame);
            }
        }

        float Wait(SmackerClip c) => c.WaitMax <= 0 ? 0 : _rng.Next(c.WaitMin, c.WaitMax + 1) / 20f;

        void PlaySound(SmackerClip c) => _audio?.Play("video/" + c.Sound);

        void Start(int index)
        {
            ActiveClip = index;
            var c = Clips[index];
            c.OnStart?.Invoke();
            _repeat = _rng.Next(c.RepeatMin, Math.Max(c.RepeatMin, c.RepeatMax) + 1);
            _waiting = false;
            _video = null;
            if (!string.IsNullOrEmpty(c.File))
            {
                var data = Load(c.File);
                if (data != null)
                {
                    try { _video = SmackerVideo.Open(data); }
                    catch (Exception e) { Debug.LogWarning("OpenAT: smk " + c.File + ": " + e.Message); }
                }
                if (_video != null && _video.DecodeNextFrame()) { Upload(); _frameTimer = (float)(1.0 / Math.Max(1.0, _video.FrameRate)); }
                if (c.Sound != null) PlaySound(c);
            }
        }

        byte[] Load(string file)
        {
            if (_fileCache.TryGetValue(file, out var d)) return d;
            var path = _assets.Install.Find("video/" + file);
            d = path != null ? System.IO.File.ReadAllBytes(path) : null;
            if (d == null) Debug.LogWarning("OpenAT: missing video/" + file);
            _fileCache[file] = d;
            return d;
        }

        void NextClip()
        {
            _cancelGuard = false;
            var cur = Clips[ActiveClip];
            cur.OnEnd?.Invoke();
            int next;
            int d;
            if (Decision >= 0) { next = Decision; Decision = -1; }
            else if (cur.Decide != null && (d = cur.Decide()) >= 0) next = d;
            else next = Choose(cur);
            if (next < 0 || next >= Clips.Count) next = 0;
            Start(next);
        }

        int Choose(SmackerClip cur)
        {
            var desired = Desired == Mood.None ? cur.Mood : Desired;
            if (desired == cur.Mood)
            {
                int r = Pick(cur, (k, m) => k != 'E' && m == desired);
                return r >= 0 ? r : Pick(cur, (k, m) => true);
            }
            bool InSet(Mood m) => m == desired || m == cur.Mood || (Accept1 != Mood.None && m == Accept1) || (Accept2 != Mood.None && m == Accept2);
            int c;
            if ((c = Pick(cur, (k, m) => k == 'E' && m == desired)) >= 0) return c;
            if ((c = Pick(cur, (k, m) => k == 'E' && InSet(m))) >= 0) return c;
            if ((c = Pick(cur, (k, m) => k != 'S' && m == desired)) >= 0) return c;
            if ((c = Pick(cur, (k, m) => k != 'S' && InSet(m))) >= 0) return c;
            return Pick(cur, (k, m) => true);
        }

        int Pick(SmackerClip cur, Func<char, Mood, bool> ok)
        {
            int total = 0;
            foreach (var n in cur.Next)
                if (n.target < Clips.Count && ok(n.kind, Clips[n.target].Mood)) total += Math.Max(1, n.weight);
            if (total == 0) return -1;
            int r = _rng.Next(total);
            foreach (var n in cur.Next)
            {
                if (n.target >= Clips.Count || !ok(n.kind, Clips[n.target].Mood)) continue;
                r -= Math.Max(1, n.weight);
                if (r < 0) return n.target;
            }
            return -1;
        }

        void Upload()
        {
            int w = _video.Width, h = _video.DisplayHeight;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "smk" };
                _rgba = new byte[w * h * 4];
            }
            var idx = _video.Indices;
            var pal = _video.Palette;
            bool doubled = h != _video.Height;
            bool interlaced = _video.IsYInterlaced;
            for (int y = 0; y < h; y++)
            {
                int sy = doubled ? y / 2 : y;
                bool blank = doubled && interlaced && (y & 1) == 1;
                int s = sy * w, d = y * w * 4;
                for (int x = 0; x < w; x++, d += 4)
                {
                    int i = idx[s + x];
                    if (i == 0 || blank) { _rgba[d + 3] = 0; continue; }
                    uint c = pal[i];
                    _rgba[d] = (byte)c; _rgba[d + 1] = (byte)(c >> 8); _rgba[d + 2] = (byte)(c >> 16); _rgba[d + 3] = 255;
                }
            }
            _tex.LoadRawTextureData(_rgba);
            _tex.Apply(false, false);
        }

        /// <summary>Draws the current frame at its clip position relative to the room origin.</summary>
        public void Draw(Screen2D s, int originX, int originY = 0)
        {
            if (!Visible || ActiveClip < 0 || _video == null || _tex == null) return;
            var p = Clips[ActiveClip].Pos;
            s.Draw(_tex, originX + p.x, originY + p.y);
        }

        public void Dispose()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = null;
            _video = null;
        }
    }
}
