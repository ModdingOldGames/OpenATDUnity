using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Telescope
{
    /// <summary>
    /// Which view the airport telescope shows (rooms-and-ui-8 §6): the rocket islands in "The Final
    /// Frontier", the space stations in "Beyond the Final Frontier", Mount Rushmore otherwise.
    /// The hall may request any of the three ids for the telescope rune, so the factories of ids 116,
    /// 118 and 122 all create the room of the running mission (the room keeps the requested id as its
    /// <see cref="RoomScreen.RoomId"/>; only the first-visit text and the visited flag depend on it).
    /// </summary>
    public static class TelescopeRedirect
    {
        public static int For(int difficulty) =>
            difficulty == Mission.Rocket ? RoomIds.Insel : difficulty == SpaceParts.StationMission ? RoomIds.WeltAll : RoomIds.Rushmore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            foreach (int id in new[] { RoomIds.Insel, RoomIds.Rushmore, RoomIds.WeltAll })
            {
                int requested = id;
                RoomRegistry.Register(id, () =>
                {
                    var w = GameApp.Instance?.Session?.World;
                    int room = w != null ? For(w.Difficulty) : requested;
                    if (room == RoomIds.Insel) return new InselRoom();
                    if (room == RoomIds.WeltAll) return new WeltAllRoom();
                    return new RushmoreRoom();
                });
            }
        }
    }

    /// <summary>
    /// Shared part of the three telescope views (rooms-and-ui-8 §6): one coin per look (3300), the eight
    /// corner exits of the eyepiece, and for the two panoramas (Insel, WeltAll) the 1280 px scroll with
    /// speed model, the binocular mask and the blinking arrows of the very first look.
    /// WIDESCREEN: the panorama continues (dimmed) left and right of the 640 eyepiece instead of the
    /// mirrored side fill of ordinary rooms; the scroll range stays [0, 640] as in the original.
    /// </summary>
    public abstract class TelescopeRoom : RoomScreen
    {
        public const string PrefFirstUse = "OpenAT.TelescopeUsed";
        const string ViewKey = "openat.telescopeX";
        const float Tick = 0.05f;              // one original frame (assumed 20 fps)
        const int ScrollLeft = 6010, ScrollRight = 6011;

        /// <summary>True for the scrolling 1280 px panoramas.</summary>
        protected virtual bool Panorama => false;
        protected abstract string Lib { get; }

        protected World W => App.Session?.World;
        protected Player P => App.Session?.Local;

        int _speed, _zone, _zoneFrame = -10;
        float _acc, _blinkUntil;

        /// <summary>Panorama x at the eyepiece's left edge (per player, shared by island and space).</summary>
        protected int ViewX
        {
            get
            {
                var p = P;
                return p != null && p.Extensions.TryGetValue(ViewKey, out var v) && v is int i ? i : 0;
            }
            set { var p = P; if (p != null) p.Extensions[ViewKey] = Mathf.Clamp(value, 0, 640); }
        }

        protected override void OnRoomEnter()
        {
            var w = W; var p = P;
            if (w != null && p != null) w.Execute(new UseTelescopeCommand(p.Index));
            App.Audio.Play("SOUND/coin.raw");
            if (Panorama && PlayerPrefs.GetInt(PrefFirstUse, 0) == 0)
            {
                PlayerPrefs.SetInt(PrefFirstUse, 1);
                _blinkUntil = Time.unscaledTime + 5f;
            }
        }

        protected override void Hotspots(InputState input)
        {
            if (Panorama)
            {
                int mx = input.Mouse.x - RX;
                int view = ViewX;
                if (mx <= 10 && view > 0 && Hot(0, 0, 10, 439, ScrollLeft, 0, CursorLook.Left)) { _zone = -1; _zoneFrame = Time.frameCount; }
                if (mx >= 630 && view < 640 && Hot(630, 0, 639, 439, ScrollRight, 0, CursorLook.Right)) { _zone = 1; _zoneFrame = Time.frameCount; }
            }
            // Eyepiece corners (§6); registered last so they win over the scroll strips.
            Exit(0, 0, 100, 50); Exit(0, 0, 50, 100); Exit(0, 390, 100, 440); Exit(0, 340, 50, 440);
            Exit(590, 0, 640, 100); Exit(540, 0, 640, 50); Exit(590, 340, 640, 440); Exit(540, 390, 640, 440);
        }

        public override void Animate(float dt)
        {
            if (!Panorama) return;
            _acc += Mathf.Min(dt, 0.25f);
            while (_acc >= Tick)
            {
                _acc -= Tick;
                bool inZone = Time.frameCount - _zoneFrame <= 1 && !IsDialogOpen && !Menus.IsOpen;
                bool held = App.InputNow.LeftHeld;
                int dir = inZone ? _zone : 0;
                int limit = held ? 44 : 14;
                int mag = Math.Sign(_speed) == dir ? Math.Abs(_speed) : 0;
                if (dir != 0 && mag <= limit)
                {
                    // Hovering a scroll strip: accelerate by 2 per frame up to 14 (44 with the button held).
                    _speed = Mathf.Clamp(dir * Math.Min(limit, mag + 2), -66, 66);
                }
                else
                {
                    // Decay towards 0 (also when the button is released at high speed).
                    if (_speed > 0) _speed = Math.Max(0, _speed - 4); else if (_speed < 0) _speed = Math.Min(0, _speed + 4);
                }
                if (_speed != 0)
                {
                    int v = ViewX + _speed;
                    if (v <= 0 || v >= 640) _speed = 0;
                    ViewX = v;
                }
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>The scene with panorama x 0 at screen x <paramref name="ox"/>.</summary>
        protected virtual void DrawScene(Screen2D s, int ox) { }

        protected override void DrawRoom(Screen2D s)
        {
            if (!Panorama) return;
            DrawScene(s, RX - ViewX);
            DrawEyepiece(s);
        }

        public override void Draw(Screen2D s)
        {
            if (!Panorama) { base.Draw(s); return; }
            int x = RX;
            s.FillRect(new RectInt(0, 0, s.Width, CanvasHeight), Color.black);
            s.PushClip(new RectInt(0, 0, s.Width, CanvasHeight));
            DrawScene(s, x - ViewX);
            s.PopClip();
            if (x > 0)
            {
                var dim = new Color32(0, 0, 0, 170);
                s.FillRect(new RectInt(0, 0, x, CanvasHeight), dim);
                s.FillRect(new RectInt(x + CanvasWidth, 0, s.Width - x - CanvasWidth, CanvasHeight), dim);
            }
            s.PushClip(new RectInt(x, 0, CanvasWidth, CanvasHeight));
            DrawEyepiece(s);
            s.PopClip();
            Dialog.Draw(s);
            if (ShowsStatusBar) Bar.Draw(s, this, App.InputNow);
            Menus.Draw(s, App.InputNow);
            App.Advisors.Draw(s, CanvasX);
            DrawOverlay(s);
        }

        /// <summary>Binocular mask (§6.1) and the first-use arrows (§6.2), canvas relative.</summary>
        void DrawEyepiece(Screen2D s)
        {
            int x = RX;
            // The edge pieces scale the brightness of what lies below (level 8 unchanged .. 0 black): drawn as
            // black with the darkness as alpha. The corner pieces are solid black blocks of their bitmap's size.
            void A(string n, int px, int py) { var t = Shade(n); if (t != null) s.Draw(t, x + px, py); }
            void O(string n, int px, int py) { var b = Assets.Bitmap(Lib, n); if (b != null) s.FillRect(new RectInt(x + px, py, b.Width, b.Height), Color.black); }
            A("ATOP", 108, 0); A("ABOTTOM", 108, 411);
            A("AL1", 39, 30); A("AL2", 0, 95); A("AL3", 39, 346);
            A("AR1", 513, 30); A("AR2", 589, 94); A("AR3", 515, 346);
            O("O1", 532, 0); O("O1", 532, 410); O("O1", 0, 0); O("O1", 0, 410);
            O("O2", 601, 30); O("O2", 601, 346); O("O2", 0, 30); O("O2", 0, 346);
            if (Time.unscaledTime < _blinkUntil && ((int)(Time.unscaledTime * 2) & 1) == 0)
            {
                var l = Assets.Texture(UiKit.BasisLib, "CURSORL");
                var r = Assets.Texture(UiKit.BasisLib, "CURSORR");
                s.Draw(l, x, 220);
                s.Draw(r, x + 640 - r.width, 220);
            }
        }

        static readonly Dictionary<string, Texture2D> _shades = new Dictionary<string, Texture2D>();

        /// <summary>An edge piece as a darkening overlay: black with the piece's darkness level as alpha (keyed pixels black).</summary>
        Texture2D Shade(string name)
        {
            string key = Lib + ":" + name;
            if (_shades.TryGetValue(key, out var t) && t != null) return t;
            var b = Assets.Bitmap(Lib, name);
            if (b == null) return null;
            var px = new Color32[b.Width * b.Height];
            for (int yy = 0; yy < b.Height; yy++)
                for (int xx = 0; xx < b.Width; xx++)
                {
                    ushort c = b.Pixels[yy * b.Width + xx];
                    uint rgba = Bitmap16.ToRgba32(c);
                    int r = (int)(rgba & 0xFF), g = (int)((rgba >> 8) & 0xFF), bl = (int)((rgba >> 16) & 0xFF);
                    // The pieces store a brightness level 0..8 in the blue channel: 8 = the picture unchanged (inside
                    // the eyepiece), lower = darker rim, 0 (the key colour) = black outside the eyepiece.
                    int level = c == Bitmap16.ColorKey ? 0 : Math.Min(8, (bl * 31 + 127) / 255);
                    byte a = (byte)((8 - level) * 255 / 8);
                    // Same row order as GameAssets.ToTexture (Screen2D samples textures top-down).
                    px[yy * b.Width + xx] = new Color32(0, 0, 0, a);
                }
            t = new Texture2D(b.Width, b.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "shade-" + name };
            t.SetPixels32(px);
            t.Apply(false, false);
            _shades[key] = t;
            return t;
        }

        protected Texture2D Tex(string name) => Assets.Has(Lib, name) ? Assets.Texture(Lib, name) : null;

        protected void DrawTex(Screen2D s, string name, int x, int y)
        {
            var t = Tex(name);
            if (t != null) s.Draw(t, x, y);
        }
    }

    // ====================================================================== island (§7)

    /// <summary>
    /// The rocket launch islands (rooms-and-ui-8 §7, mission "The Final Frontier"): each active airline's
    /// rocket grows part by part from <c>Counters.RocketFlags</c>; water with rippling reflections, a boat
    /// and a seagull. The ripple is a per-row horizontal shift of the composed reflection layer (the
    /// original's per-pixel motion mask is simplified to a uniform strength).
    /// </summary>
    [Room(RoomIds.Insel)]
    [ScreenName("insel")]
    public sealed class InselRoom : TelescopeRoom
    {
        const string L = "room/insel.gli";
        protected override string Lib => L;
        protected override string Library => L;
        protected override string Background => null;
        protected override bool Panorama => true;

        static readonly Vector2Int[] Anchor = { new Vector2Int(115, 82), new Vector2Int(406, 83), new Vector2Int(738, 87), new Vector2Int(1048, 86) };
        static readonly Vector2Int[] RefAnchor = { new Vector2Int(115, 164), new Vector2Int(406, 155), new Vector2Int(738, 163), new Vector2Int(1048, 172) };
        static readonly int[] DrawOrder = { 0, 1, 2, 3, 9, 4, 6, 5, 8, 7 };
        /// <summary>Second digit of the part bitmap per bit; bits 0, 1, 3, 7 are shared uncoloured pieces.</summary>
        static readonly int[] PartDigit = { 0, 1, 2, 3, 5, 7, 6, 9, 8, 4 };
        static readonly bool[] Shared = { true, true, false, true, false, false, false, true, false, false };
        const int WaterY = 232, WaterH = 440 - WaterY;

        Texture2D _reflex;
        int _reflexSig = int.MinValue;
        readonly System.Random _rng = new System.Random();
        float _boatStart, _birdStart;
        bool _gliding;
        float _birdY = 22, _glideRoll;
        AudioSource _waves;

        static string PartName(int player, int bit, bool reflection)
        {
            string n = Shared[bit] ? "ROCK0" + PartDigit[bit] : "ROCK" + player + PartDigit[bit];
            // Reflection of bit 6 for players 2 and 3 uses player 1's piece (asset sharing of the original, kept).
            if (reflection && bit == 6 && player >= 2) n = "ROCK16";
            return reflection ? "R" + n : n;
        }

        protected override void OnCreate()
        {
            float now = Time.unscaledTime;
            _boatStart = now;
            _birdStart = now + 2f + (float)_rng.NextDouble() * 5f;
        }

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            SetBackgroundFx(0, "moeve.raw", 50000);
            if (PlayerPrefs.GetFloat(GameAudio.PrefAmbience, 1f) > 0) _waves = App.Audio.PlayLoop("SOUND/wellen.raw", 0.5f);
        }

        protected override void OnLeaveRoom()
        {
            App.Audio.StopLoop(_waves);
            _waves = null;
            if (_reflex != null) UnityEngine.Object.Destroy(_reflex);
            _reflex = null;
        }

        int Signature()
        {
            var w = W;
            if (w == null) return 0;
            int sig = 17;
            foreach (var p in w.Players) sig = sig * 31 + (p.IsOut ? -1 : p.Counters.RocketFlags);
            return sig;
        }

        /// <summary>Reflection layer: REFLEX, each rocket's reflected parts (bottom at the anchor), the island reflection.</summary>
        void BuildReflection()
        {
            var layer = new Bitmap16(1280, WaterH);
            void Blit(string name, int x, int y)
            {
                var b = Assets.Bitmap(L, name);
                if (b == null) return;
                for (int yy = 0; yy < b.Height; yy++)
                {
                    int ty = y + yy;
                    if (ty < 0 || ty >= layer.Height) continue;
                    for (int xx = 0; xx < b.Width; xx++)
                    {
                        int tx = x + xx;
                        if (tx < 0 || tx >= layer.Width) continue;
                        ushort c = b.Pixels[yy * b.Width + xx];
                        if (c != Bitmap16.ColorKey) layer.Pixels[ty * layer.Width + tx] = c;
                    }
                }
            }
            Blit("REFLEX", 0, 0);
            var w = W;
            if (w != null)
                for (int n = 0; n < Math.Min(4, w.Players.Length); n++)
                {
                    var p = w.Players[n];
                    if (p.IsOut) continue;
                    int flags = p.Counters.RocketFlags;
                    foreach (int bit in DrawOrder)
                    {
                        if ((flags & (1 << bit)) == 0) continue;
                        string name = PartName(n, bit, true);
                        var b = Assets.Bitmap(L, name);
                        if (b != null) Blit(name, RefAnchor[n].x, RefAnchor[n].y - b.Height);
                    }
                }
            Blit("REFINSEL", 10, 4);
            if (_reflex != null) UnityEngine.Object.Destroy(_reflex);
            _reflex = GameAssets.ToTexture(layer, "insel-reflex");
        }

        static int Ripple(float ms, int row) => (int)(Math.Sin((ms + Math.Sin(row / 10.0) * 2400) / 500.0) * 7);

        void DrawRippled(Screen2D s, Texture2D tex, int x, int y, float ms, int phase)
        {
            for (int r = 0; r < tex.height; r++)
                s.Draw(tex, x + Ripple(ms, r + phase), y + r, new RectInt(0, r, tex.width, 1), Color.white);
        }

        public override void Animate(float dt)
        {
            base.Animate(dt);
            float now = Time.unscaledTime;
            // Boat: 140 s crossing, then a pause of 2..7 s.
            if (now - _boatStart > 140f) _boatStart = now + 2f + (float)_rng.NextDouble() * 5f;
            // Seagull: same rhythm; gliding on flap frame 4 (1/10 to start, 1/7 to stop, per original frame).
            if (now - _birdStart > 140f) { _birdStart = now + 2f + (float)_rng.NextDouble() * 5f; _birdY = 22; _gliding = false; }
            if (now > _birdStart)
            {
                _glideRoll += dt;
                while (_glideRoll >= 0.05f)
                {
                    _glideRoll -= 0.05f;
                    int frame = (int)((now - _birdStart) * 10) % 8;
                    if (!_gliding && frame == 4 && _rng.Next(10) == 0) _gliding = true;
                    else if (_gliding && _rng.Next(7) == 0) _gliding = false;
                }
                if (_gliding) _birdY += 4f * dt;
            }
        }

        protected override void DrawScene(Screen2D s, int ox)
        {
            var w = W;
            int sig = Signature();
            if (_reflex == null || sig != _reflexSig) { _reflexSig = sig; BuildReflection(); }
            float now = Time.unscaledTime, ms = now * 1000f;
            DrawTex(s, "INSEL", ox, 0);
            DrawRippled(s, _reflex, ox, WaterY, ms, 0);

            int bt = (int)((now - _boatStart) * 1000f);
            bool boat = bt >= 0;
            int bx = 1300 - bt / 80, by = 264 + (bx - 1280) / 60;
            var boatM = Tex("BOATM");
            if (boat && boatM != null) DrawRippled(s, boatM, ox + bx - 14, by + 61, ms, by + 61 - WaterY);

            if (w != null)
                for (int n = 0; n < Math.Min(4, w.Players.Length); n++)
                {
                    var p = w.Players[n];
                    if (p.IsOut) continue;
                    int flags = p.Counters.RocketFlags;
                    foreach (int bit in DrawOrder)
                        if ((flags & (1 << bit)) != 0) DrawTex(s, PartName(n, bit, false), ox + Anchor[n].x, Anchor[n].y);
                }

            if (boat)
            {
                DrawTex(s, "BOAT", ox + bx, by);
                var spray = Tex("BOATW0" + (bt / 100 % 3 + 1));
                if (spray != null) s.Draw(spray, ox + bx, by + 57, new Color32(255, 255, 255, 128));
            }
            var front = Tex("FRONT");
            if (front != null) s.Draw(front, ox, 440 - front.height);
            if (now > _birdStart)
            {
                float t = now - _birdStart;
                int frame = _gliding ? 4 : (int)(t * 10) % 8;
                DrawTex(s, "VOGEL0" + (frame + 1), ox + (int)(t * 1000f / 25f), (int)_birdY);
            }
        }
    }

    // ====================================================================== space (§9)

    /// <summary>
    /// The space stations (rooms-and-ui-8 §9, mission "Beyond the Final Frontier"): each airline's station
    /// modules with its colour, lit windows once power plant and solar panels are owned, the airline flag
    /// on a complete station, shooting stars and a wandering UFO. Unlike the original, left-clicking an
    /// eyepiece corner leaves (deliberate fix). Stations are drawn for bankrupt airlines too (as the original).
    /// </summary>
    [Room(RoomIds.WeltAll)]
    [ScreenName("weltall")]
    public sealed class WeltAllRoom : TelescopeRoom
    {
        const string L = "room/weltall.gli";
        protected override string Lib => L;
        protected override string Library => L;
        protected override string Background => null;
        protected override bool Panorama => true;

        const int RingA = 1, RingB = 2, RingC = 4, Oxygen = 8, Com = 16, Main = 32, Living = 64, Office = 128, Power = 256, Solar = 512, All = 1023;
        static readonly Vector2Int[] Anchor = { new Vector2Int(70, 60), new Vector2Int(370, 160), new Vector2Int(710, 60), new Vector2Int(1000, 100) };
        static readonly Vector2Int[] FlagPos = { new Vector2Int(142, 63), new Vector2Int(442, 163), new Vector2Int(782, 63), new Vector2Int(1072, 103) };
        static readonly string[] Colour = { "B", "G", "R", "Y" };
        static readonly Vector2[] StarVel = { new Vector2(5, 9) * (4000f / 256f), new Vector2(-11, 13) * (4000f / 256f), new Vector2(18, 12) * (4000f / 256f) };

        struct Star { public Vector2 Pos; public int Type; }
        readonly List<Star> _stars = new List<Star>();
        readonly System.Random _rng = new System.Random();
        float _starClock;
        readonly SmackerPerson[] _flags = new SmackerPerson[4];
        SmackerPerson _ufo;
        Vector2 _ufoPos = new Vector2(640, 800), _ufoVel = new Vector2(0, -12);
        int _ufoSteps = 50;
        float _ufoWait, _ufoAcc;

        protected override void OnCreate()
        {
            for (int n = 0; n < 4; n++)
            {
                _flags[n] = new SmackerPerson(Assets, App.Audio);
                _flags[n].Clip(0, "Flag" + Colour[n] + ".smk", 0, 0, Mood.Idle, "A9:0");
            }
            _ufo = new SmackerPerson(Assets, App.Audio);
            _ufo.Clip(0, "Ufo.smk", 0, 0, Mood.Idle, "A9:0");
            _ufoWait = 3f + (float)_rng.NextDouble() * 2f;
        }

        protected override void OnLeaveRoom()
        {
            foreach (var f in _flags) f?.Dispose();
            _ufo?.Dispose();
        }

        int Flags(int n)
        {
            var w = W;
            return w != null && n < w.Players.Length ? w.Players[n].Counters.RocketFlags : 0;
        }

        public override void Animate(float dt)
        {
            base.Animate(dt);
            for (int n = 0; n < 4; n++) if ((Flags(n) & All) == All) _flags[n].Update(dt);
            _ufo.Update(dt);

            // Shooting stars: once per second a 1/3 chance, at most 20 at once.
            _starClock += dt;
            while (_starClock >= 1f)
            {
                _starClock -= 1f;
                if (_stars.Count < 20 && _rng.Next(3) == 0)
                    _stars.Add(new Star { Pos = new Vector2(_rng.Next(1580), 0), Type = _rng.Next(3) });
            }
            for (int i = _stars.Count - 1; i >= 0; i--)
            {
                var st = _stars[i];
                st.Pos += StarVel[st.Type] * dt;
                if (st.Pos.y >= 500) _stars.RemoveAt(i); else _stars[i] = st;
            }

            // UFO: per-frame movement of the original replayed at 20 steps per second.
            if (_ufoWait > 0) { _ufoWait -= dt; return; }
            _ufoAcc += Mathf.Min(dt, 0.25f);
            while (_ufoAcc >= 0.05f && _ufoWait <= 0)
            {
                _ufoAcc -= 0.05f;
                _ufoPos += _ufoVel;
                if (_ufoPos.x < -300 || _ufoPos.y < -300 || _ufoPos.x > 1580 || _ufoPos.y > 810)
                {
                    _ufoPos = new Vector2(640, 800);
                    _ufoVel = new Vector2(0, -4);
                    _ufoSteps = 140;
                    _ufoWait = 13f + (float)_rng.NextDouble() * 2f;
                    break;
                }
                if (--_ufoSteps > 0) continue;
                var v = _rng.Next(6) == 0 ? new Vector2(0, -500) : new Vector2(_rng.Next(-150, 150), _rng.Next(-90, 90));
                _ufoSteps = Math.Max(2, (int)(v.magnitude / 12));
                _ufoVel = v / _ufoSteps;
                _ufoWait = 1f + (float)_rng.NextDouble();
            }
        }

        protected override void DrawScene(Screen2D s, int ox)
        {
            DrawTex(s, "WELTALL", ox, 0);
            foreach (var st in _stars) DrawTex(s, "STAR" + (st.Type + 1), ox + (int)st.Pos.x - 150, (int)st.Pos.y - 20);
            for (int n = 0; n < 4; n++)
            {
                int f = Flags(n);
                if (f == 0) continue;
                int ax = ox + Anchor[n].x, ay = Anchor[n].y;
                string c = Colour[n];
                bool lit = (f & Solar) != 0 && (f & Power) != 0;
                void Part(int bit, string name, int x, int y, string overlay, int x2, int y2)
                {
                    if ((f & bit) == 0) return;
                    if (name != null) DrawTex(s, name, ax + x, ay + y);
                    if (overlay != null) DrawTex(s, overlay, ax + x2, ay + y2);
                }
                Part(RingC, "RING3", 44, 41, "RING3" + c, 88, 44);
                Part(RingB, "RING2", 49, 65, "RING2" + c, 94, 67);
                Part(RingA, "RING1", 47, 82, "RING1" + c, 89, 84);
                Part(Com, "COMM", 97, 105, lit ? "COMML" : null, 103, 116);
                Part(Oxygen, null, 0, 0, "O2" + c, 50, 31);
                Part(Office, "BUSINES", 90, 13, lit ? "BUSINESL" : null, 90, 13);
                Part(Main, "CENTRAL", 5, 4, lit ? "CENTRALL" : null, 30, 34);
                Part(Living, "LIVING", 10, 114, lit ? "LIVINGL" : null, 10, 114);
                Part(Solar, "SOLAR", 71, 36, null, 0, 0);
                if ((f & All) == All) _flags[n].Draw(s, ox + FlagPos[n].x, FlagPos[n].y);
            }
            _ufo.Draw(s, ox + (int)_ufoPos.x, (int)_ufoPos.y);
        }
    }

    // ====================================================================== Mount Rushmore (§8)

    /// <summary>
    /// Mount Rushmore (rooms-and-ui-8 §8): the airlines ranked by the current mission rating (lowest first
    /// in the freight-tonnage add-on mission), logo and "rating / secondary rating" text per rank. Fixed
    /// 640x440 picture (pillarboxed like any room).
    /// </summary>
    [Room(RoomIds.Rushmore)]
    [ScreenName("rushmore")]
    public sealed class RushmoreRoom : TelescopeRoom
    {
        const string L = "room/rushmore.gli";
        protected override string Lib => L;
        protected override string Library => L;
        protected override string Background => "RUSHMORE";
        static readonly int[] RowY = { 261, 302, 346, 388 };
        /// <summary>DIFF_ADDON01: the lowest value ranks first.</summary>
        const int LowestWinsMission = Mission.AddOnFirst;

        protected override void DrawRoom(Screen2D s)
        {
            var w = W;
            if (w == null) return;
            var rule = w.Rules.Get<IMissionRule>();
            int n = Math.Min(4, w.Players.Length);
            var order = new List<(int player, long key, long a, long b)>();
            for (int i = 0; i < n; i++)
            {
                var p = w.Players[i];
                long a = rule != null ? rule.Rating(w, p) : 0, b = rule != null ? rule.Rating(w, p, true) : 0;
                order.Add((i, w.Difficulty == LowestWinsMission ? -a : a, a, b));
            }
            order.Sort((x, y) => y.key != x.key ? y.key.CompareTo(x.key) : x.player.CompareTo(y.player));
            int diff = w.Difficulty == Mission.FreeGame ? Mission.Passengers : w.Difficulty;
            string fmt = Assets.StdText.Get("Misc", 1200 + diff) ?? "%s / %s";
            var font = App.Ui.Kit.BigGrey;
            for (int r = 0; r < order.Count; r++)
            {
                var e = order[r];
                if (w.Players[e.player].IsOut) continue;   // the row stays empty (no compaction)
                int y = RowY[r];
                DrawTex(s, "LOGO0" + e.player, RX + 181, y);
                string text = DialogText.Format(fmt, new object[] { UiKit.Thousands(e.a), UiKit.Thousands(e.b) });
                s.PushClip(new RectInt(RX, 0, 640, 440));
                font.Draw(s, RX + 225, y - 1, text);
                s.PopClip();
            }
        }
    }
}
