using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// The airport hall (airport-and-people.md §3, §5, §8, §9, §12): parallax horizon per clip segment,
    /// the season bricks merged with the persons by depth key, shadows, doors, shutters and animated
    /// bricks, the inertial follow camera, edge/keyboard scrolling and click-to-walk.
    ///
    /// Widescreen: the viewport is Screen.Width × 440 virtual px. The camera's left edge is clamped to
    /// [LeftEnd, RightEnd + 320 − W]: the original allowed the right screen edge to reach RightEnd + 320
    /// (the workshop end of the authored art) and the left edge to reach LeftEnd, so a wider view only
    /// moves the right limit left and never shows space outside the authored hall. The horizon layers
    /// tile across the full width; the follow camera centres the player at W/2.
    /// </summary>
    [ScreenName("hall")]
    public sealed partial class AirportScreen : GameScreen, IStatusBarHost
    {
        public const int HallHeight = 440;

        World _w;
        HallState _h;
        HallGraphics _g;

        // camera (ViewPos and the follow state of FocusCameraOnPos, §5.3)
        public int ViewX, ViewY;
        int _camSpeedX, _camSpeedY, _lastPlayerPos, _lastCamSpeedX, _acceptedCamSpeedX;
        readonly int[] _lastDeltas = { -1000, -1000, -1000, -1000, -1000 };
        float _lastScrollTime = -10;
        bool _manualScroll;
        int _manualSpeed;
        int _edgeZone; // −1 left, +1 right, 0 none
        bool _scrollFast;
        bool _centred;

        // animation ticker (§8.3: real ms >> 5)
        double _tickerMs;
        long Ticker => (long)(_tickerMs / 32.0);
        readonly Dictionary<int, (int serial, long start)> _triggerStarts = new Dictionary<int, (int, long)>();

        // input
        float _lastClick = -10;
        Vector2Int _lastClickPos;
        int _hoverRoom;
        int _pendingReturn = -1;
        float _returnAt;
        IDisposable _roomSub;
        Vector2Int _mouse;
        float _hoverSince;
        IDisposable _soundSub;

        // shared frame UI (rooms-and-ui-9 §8.5): status bar, the hall's menu slot, advisors
        readonly StatusBar _bar = new StatusBar();
        MenuHost _menus;
        public MenuHost Menus => _menus;
        public bool IsDialogOpen => false;
        public int CanvasX => OX;

        int W => Screen.Width;
        int MinViewX => _h.LeftEnd;
        int MaxViewX => Math.Max(_h.LeftEnd, _h.RightEnd + 320 - W);
        int Local => _w.LocalPlayer;
        static readonly bool DebugFlow = Array.IndexOf(Environment.GetCommandLineArgs(), "-openat-debugflow") >= 0;

        /// <summary>The hall screen of the running game (its camera feeds the ambience, <see cref="HallAmbience"/>).</summary>
        internal static AirportScreen Shown;
        /// <summary>Viewport width in virtual pixels.</summary>
        internal int ViewWidth => W;

        public override void OnEnter()
        {
            Shown = this;
            if (App.Rooms.Locations.Count == 0) App.Audio.SetGlobalAmbientVolume(RoomAmbience.Hall);
            _menus = new MenuHost(App, () => OX);
            _w = HallBoot.EnsureHallSession(App);
            _h = _w?.Hall;
            if (_h == null) return;
            _g = new HallGraphics(Assets, _h.Data, _w.Clock.Season);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _g.Preload(_h.Layout, _h.TodayInGame);
            Debug.Log("OpenAT hall: graphics preloaded in " + sw.ElapsedMilliseconds + " ms");
            _soundSub = _w.Events.Subscribe<HallSound>(OnSound);
            // Standalone test boot (-openat-screen hall without a room framework): rooms are not hosted,
            // so the figure steps back out of the room after a moment.
            if (HallBoot.Standalone)
                _roomSub = _w.Events.Subscribe<RoomRequested>(e =>
                {
                    Debug.Log("OpenAT hall: " + e);
                    if (e.Player == Local) { _pendingReturn = e.Player; _returnAt = Time.unscaledTime + 1.5f; }
                    else _w.Execute(new ReturnToHallCommand(e.Player));
                });
            CenterCameraOnPlayer();
        }

        public override void OnExit()
        {
            _soundSub?.Dispose();
            _roomSub?.Dispose();
            if (Shown == this) { Shown = null; App.Audio.StopAmbience(); }
            if (App.Session != null) App.Session.Driver.Warp = false;
            _g?.Dispose();
        }

        void OnSound(HallSound e)
        {
            if (!_h.Players[Local].InHall) return;
            if (Math.Abs(ViewX + W / 2 - e.X) < 400) App.Audio.Play("SOUND/" + e.File, 0.6f);
        }

        // ------------------------------------------------------------------ update

        public override void Update(InputState input, float dt)
        {
            if (_h == null) return;
            if (_w.Hall != _h) { _h = _w.Hall; if (_h == null) return; }
            _tickerMs += dt * 1000.0;
            var me = _h.PlayerPerson(Local);
            UpdateWarp(input, me);
            if (_pendingReturn >= 0 && Time.unscaledTime > _returnAt)
            {
                _w.Execute(new ReturnToHallCommand(_pendingReturn));
                _pendingReturn = -1;
            }

            App.Advisors.Update(dt, OX, input, !_menus.IsOpen);
            _menus.Animate(dt);
            bool inBar = input.Mouse.y >= HallHeight;
            if (_menus.IsOpen)
            {
                if (inBar) _bar.Update(this, input);
                _menus.Update(input, dt);
                _edgeZone = 0;
            }
            else if (!_bar.Update(this, input) && !HandleKeys(input)) HandleInput(input, me);
            else { _edgeZone = 0; _hoverRoom = 0; }
            StickWalk(!_menus.IsOpen && !App.Ui.FastTravel.IsOpen);
            UpdateCursor(input, me);

            // One camera step per frame in which the simulation ran (§5.3: the original moves the camera once per
            // repaint after a sim step). A separate real-time clock for the camera beats against the 20 Hz figure
            // steps: the figure and the view then move on different frames and the figure jitters on screen.
            int ticks = App.Session?.TicksThisFrame ?? 0;
            if (ticks > 0 && me != null) FocusCamera(me.ScreenPos);
            if (MotionProbe.Active) MotionProbe.Frame(me, ViewX, ticks);
        }

        void HandleInput(InputState input, Person me)
        {
            var m = input.Mouse;
            _mouse = m;
            bool inHall = m.y >= 0 && m.y < HallHeight && m.x >= 0 && m.x < W;
            AddPadTargets();

            // Edge scrolling zones (§5.4; WS: right zone at W − 10).
            _edgeZone = 0;
            if (inHall && m.x <= 10 && ViewX > MinViewX) _edgeZone = -1;
            if (inHall && m.x >= W - 10 && ViewX < MaxViewX) _edgeZone = 1;
            if (input.Left && ViewX > MinViewX) _edgeZone = -1;
            if (input.Right && ViewX < MaxViewX) _edgeZone = 1;
            if (App.Ui.Pad.Walk != Vector2.zero) _edgeZone = 0;   // the camera follows a stick-walking figure (OpenAT)
            _scrollFast = input.LeftHeld || input.Left || input.Right;

            // The hall uses the hover point for thought bubbles (UI input, not game state).
            _h.Hover = inHall ? new Pt(m.x + ViewX, m.y + ViewY) : (Pt?)null;

            // Hover: marked areas give tooltips.
            int room = inHall ? _h.MarkedArea(new Pt(m.x + ViewX, m.y + ViewY)) : 0;
            if (room == RoomIds.Wall) room = 0;
            if (room != _hoverRoom) { _hoverRoom = room; _hoverSince = Time.unscaledTime; }

            if (input.RightDown && inHall)
            {
                // Right click: back to the player (jump when far away; the original crossfades).
                _manualScroll = false;
                _manualSpeed = 0;
                if (me != null && Math.Abs(me.ScreenPos.X - ViewX - W / 2) > W) CenterCameraOnPlayer();
            }

            if (input.LeftDown && !input.Consumed && inHall && _edgeZone == 0 && me != null)
            {
                bool dbl = Time.unscaledTime - _lastClick < 0.4f && (m - _lastClickPos).sqrMagnitude < 64;
                _lastClick = Time.unscaledTime;
                _lastClickPos = m;
                if (!_stickOn) _padCam = false;   // a click walk gets the original camera
                int px = me.Position.X - ViewX;
                if (px > -5 && px < W + 5) _manualScroll = false;
                var click = new Pt(m.x + ViewX, m.y + ViewY);
                if (App.Session != null) App.Session.Driver.Warp = false;
                // A click on another airline's figure walks next to it for a talk (rooms-and-ui-9 §8.3).
                for (int c = 0; c < Sim.Player.Count; c++)
                {
                    if (c == Local) continue;
                    var other = _h.PlayerPerson(c);
                    if (other == null || !_h.Players[c].InHall) continue;
                    int fx = other.ScreenPos.X - 7, fy = other.ScreenPos.Y + 1;
                    if (click.X < fx - 14 || click.X > fx + 14 || click.Y < fy - 60 || click.Y > fy) continue;
                    var tr = _w.Execute(new WalkToPlayerCommand(Local, c));
                    if (DebugFlow) Debug.Log("OpenAT hall: walk to player " + c + ": " + tr);
                    if (tr.Ok) return;
                }
                var r = _w.Execute(new WalkToClickCommand(Local, click, dbl));
                if (r.Ok && dbl) App.Audio.Click();
                if (!r.Ok && DebugFlow) Debug.Log("OpenAT hall: click ignored at " + _w.Clock.ClockText + ": " + r);
            }
        }

        // ------------------------------------------------------------------ gamepad (OpenAT)

        bool _stickOn;
        bool _padCam;   // the stick-walk follow camera is in charge (OpenAT, GameOptions.PadFollowCamera)

        /// <summary>Doors, counters and displays in view are gamepad targets.</summary>
        void AddPadTargets()
        {
            foreach (var a in _h.Layout.Areas)
            {
                if (a.Par == 0 || a.Par == RoomIds.Wall) continue;
                var r = new RectInt(a.P1.X - ViewX, a.P1.Y - ViewY, a.P2.X - a.P1.X + 1, a.P2.Y - a.P1.Y + 1);
                if (r.xMax <= 0 || r.x >= W || r.yMax <= 0 || r.y >= HallHeight) continue;
                App.Ui.Targets.Add(r);
            }
        }

        /// <summary>
        /// The left stick walks the figure (<see cref="StickWalkCommand"/>): eight directions, the pace from how far the
        /// stick is pushed. Letting go (or a menu opening) stops the figure.
        /// </summary>
        void StickWalk(bool allowed)
        {
            var v = allowed ? App.Ui.Pad.Walk : Vector2.zero;
            float mag = v.magnitude;
            int tilt = Mathf.Clamp(Mathf.RoundToInt(mag * 1000), 0, 1000);
            if (tilt == 0)
            {
                if (_stickOn) _w.Execute(new StickWalkCommand(Local, 0, 0, 0));
                _stickOn = false;
                return;
            }
            int dx = Mathf.Abs(v.x) > 0.38f * mag ? Math.Sign(v.x) : 0, dy = Mathf.Abs(v.y) > 0.38f * mag ? Math.Sign(v.y) : 0;
            if (!_w.Execute(new StickWalkCommand(Local, dx, dy, tilt)).Ok) return;
            _stickOn = true;
            _manualScroll = false;
            _padCam = _w.Options.PadFollowCamera;
            if (App.Session != null) App.Session.Driver.Warp = false;
        }

        /// <summary>Inventory click in the status bar (rooms-and-ui-1 §9.4): laptop, phone, filofax.</summary>
        public void UseItem(int slot, int item)
        {
            // The items domain decides (glue, stink bomb, pliers at the security door, phone book, ...).
            Rooms.Items.ItemUseView.Use(this, slot, item, Rooms.Framework.RoomIds.Airport, -1);
        }

        /// <summary>Esc/F2 options, F3/F4 load/save, Tab call it a day, +/- game speed (rooms-and-ui-1 §7).</summary>
        bool HandleKeys(InputState input)
        {
            // Esc/F2/F3/F4 and Tab share the room hotkeys' activity gate (no tutorial, 8-18 h, ...); F2 also opens the
            // options through the room window's own key handler (rooms-and-ui-1 §7.1/§7.2).
            if (Rooms.Framework.RoomHotkeys.HandleOptionKeys(App, input, _menus.IsOpen)) return true;
            var session = App.Session;
            if (session == null) return false;
            foreach (char c in input.Typed)
            {
                if (c == '+') session.Local.GameSpeed = (session.Local.GameSpeed + 1) % 4;
                else if (c == '-') session.Local.GameSpeed = (session.Local.GameSpeed + 3) % 4;
            }
            if (input.Tab && Rooms.Framework.RoomHotkeys.GlobalKeysAllowed(App, _w, _menus.IsOpen))
            {
                _menus.Open(RequestMenu.Create(RequestMenu.CallItADay, null, () => Rooms.Framework.GameFlow.CallItADay(App)), new Vector2Int(320, 220), 0.17f);
                return true;
            }
            return Rooms.Framework.RoomHotkeys.Handle(App, input, _menus.IsOpen);
        }

        /// <summary>
        /// Edge-scroll arrows, the hot cursor and tooltip over marked areas, and the walking feet when the
        /// player's figure walks outside the view (airport-and-people.md §5.4; WS: right limit W + 10).
        /// </summary>
        void UpdateCursor(InputState input, Person me)
        {
            if (_menus.IsOpen || input.Mouse.y >= HallHeight) return;
            var ui = App.Ui;
            if (_edgeZone < 0 && input.Mouse.x <= 10) { ui.Cursor = CursorLook.Left; return; }
            if (_edgeZone > 0 && input.Mouse.x >= W - 10) { ui.Cursor = CursorLook.Right; return; }
            if (_hoverRoom != 0)
            {
                // Any marked area (doors, flight displays) shows the hot cursor; the walking feet only
                // replace the normal cursor, so they do not show over an area.
                ui.Cursor = CursorLook.Hot;
                string text = TooltipText(_hoverRoom);
                if (!string.IsNullOrEmpty(text)) ui.Tooltip.Set(text);
                return;
            }
            if (me != null && me.Dir != 8 && _h.Players[Local].InHall)
            {
                int px = me.ScreenPos.X - ViewX;
                if (px < -40) { ui.Cursor = CursorLook.WalkLeft; return; }
                if (px > W + 10) { ui.Cursor = CursorLook.WalkRight; return; }
            }
        }

        /// <summary>Space while the figure walks: warp walking (×150) until it arrives or leaves the hall.</summary>
        void UpdateWarp(InputState input, Person me)
        {
            var driver = App.Session?.Driver;
            if (driver == null || me == null) return;
            var hp = _h.Players[Local];
            if (input.Space && !_menus.IsOpen && Tutorial.TutorialView.Active) { Tutorial.TutorialView.Cancel(App); return; }   // skip the tutorial
            if (input.Space && hp.InHall && me.Dir != 8) driver.Warp = true;
            if (driver.Warp && (!hp.InHall || (me.Dir == 8 && hp.PrimaryTarget == hp.TertiaryTarget)))
            {
                driver.Warp = false;
                CenterCameraOnPlayer();
            }
        }

        // ------------------------------------------------------------------ camera (§5.2, §5.3)

        static int Inertia(int a, int b)
        {
            if (a <= b) return a >= b ? 0 : -(int)Math.Sqrt((b - a + 1) / 2.0 + 0.5);
            return (int)Math.Sqrt((a - b + 1) / 2.0 + 0.5);
        }

        void ClampView()
        {
            if (ViewX < MinViewX) ViewX = MinViewX;
            if (ViewX > MaxViewX) ViewX = MaxViewX;
        }

        /// <summary>
        /// Puts the camera where a loaded game left it (the saved view position): no snap to the player; the view
        /// stays in "manual" mode until the player walks again (then the follow camera takes over).
        /// </summary>
        public void RestoreCamera(int x, int y)
        {
            ViewX = x;
            ViewY = y;
            _centred = true;
            _manualScroll = true;
            _manualSpeed = _camSpeedX = _camSpeedY = _lastCamSpeedX = _acceptedCamSpeedX = 0;
            var me = _h?.PlayerPerson(Local);
            if (me != null) _lastPlayerPos = me.ScreenPos.X;
            for (int i = 0; i < _lastDeltas.Length; i++) _lastDeltas[i] = 0;
        }

        public void CenterCameraOnPlayer()
        {
            var me = _h?.PlayerPerson(Local);
            if (me == null) return;
            ViewX = me.ScreenPos.X - W / 2;
            ClampView();
            if (me.ScreenPos.Y < 155) ViewY = -10;
        }

        /// <summary>One camera step at 20 Hz (FocusCameraOnPos in scroll mode, generalised to width W).</summary>
        void FocusCamera(Pt pos)
        {
            int size = W / 2;
            if (App.Session != null && App.Session.Driver.Warp) { _camSpeedX = _camSpeedY = _lastCamSpeedX = _acceptedCamSpeedX = 0; }
            Array.Copy(_lastDeltas, 1, _lastDeltas, 0, 4);
            _lastDeltas[4] = pos.X - _lastPlayerPos;
            int avg = _lastDeltas[0];
            for (int c = 1; c < 5; c++)
            {
                if (Math.Abs(avg / c - _lastDeltas[c]) > 8) { avg = 0; break; }
                avg += _lastDeltas[c];
            }
            avg /= 5;
            _lastPlayerPos = pos.X;

            // Manual (edge / key) scrolling: ±2 per step up to 14 (44 with the button held), decays by 4.
            if (_edgeZone != 0)
            {
                _camSpeedX = _camSpeedY = 0;
                int cap = _scrollFast ? 44 : 14;
                if (_edgeZone < 0) { if (_manualSpeed > -cap) _manualSpeed -= 2; if (_manualSpeed < -44) _manualSpeed = -44; }
                else { if (_manualSpeed < cap) _manualSpeed += 2; if (_manualSpeed > 44) _manualSpeed = 44; }
                _manualScroll = true;
                _padCam = false;
            }
            if (_edgeZone == 0 || (!_scrollFast && Math.Abs(_manualSpeed) > 8))
            {
                if (_manualSpeed > 0) _manualSpeed = Math.Max(0, _manualSpeed - 4);
                if (_manualSpeed < 0) _manualSpeed = Math.Min(0, _manualSpeed + 4);
            }
            if (_manualScroll)
            {
                ViewX += _manualSpeed;
                ClampView();
            }
            else
            {
                _manualSpeed = 0;
                int tx = pos.X - size;
                int uncorrected = tx;
                if (tx < MinViewX) tx = MinViewX;
                if (tx > MaxViewX) tx = MaxViewX;
                int ty = pos.Y - 480 / 3;
                if (ty < -10) ty = -10;
                if (ty > 0) ty = 0;

                float now = Time.unscaledTime;
                if (_padCam)
                {
                    // Stick walking (OpenAT): move with the figure and close 35 % of the gap to the centre each step, so
                    // the figure stays centred while it walks and the view catches up within half a second. No inertia,
                    // no waiting for the figure to near the edge. At the hall's ends the target is clamped: no lead.
                    int d = tx == uncorrected ? _lastDeltas[4] : 0;
                    int e = tx - ViewX;
                    int step = (int)Math.Round(e * 0.35 + d * 0.65);
                    if (Math.Abs(e) <= 2) step = e;
                    ViewX += step;
                    _camSpeedX = _lastCamSpeedX = _acceptedCamSpeedX = 0;
                    if (step != 0) _lastScrollTime = now;
                    // After the stick is let go, centre the figure, then hand back to the original camera.
                    if (!_stickOn && ViewX == tx || !_w.Options.PadFollowCamera) _padCam = false;
                }
                else if (Math.Abs(uncorrected - ViewX) > size * 2 / 3 || _camSpeedX != 0 || now - _lastScrollTime < 0.5f)
                {
                    int tmp = Inertia(tx, tx + (ViewX - tx) * 2) + avg;
                    if (tmp > _camSpeedX) _camSpeedX++;
                    if (tmp < _camSpeedX) _camSpeedX--;
                    if (tmp * _camSpeedX < 0 || Math.Abs(avg - _camSpeedX) > 4)
                    {
                        if (tmp > _camSpeedX) _camSpeedX++;
                        if (tmp < _camSpeedX) _camSpeedX--;
                    }
                    // Speed changes only count once they persist for two steps.
                    if (_acceptedCamSpeedX != _camSpeedX)
                    {
                        if (_lastCamSpeedX == _camSpeedX || _camSpeedX == 0) _acceptedCamSpeedX = _camSpeedX;
                        else { _lastCamSpeedX = _camSpeedX; _camSpeedX = _acceptedCamSpeedX; }
                    }
                }
                else if (_h.Players[Local].DidntMove > 5 * 20)
                {
                    int tmp = Inertia(tx, ViewX);
                    if (tmp > _camSpeedX) _camSpeedX++;
                    if (tmp < _camSpeedX) _camSpeedX--;
                }

                int vy = Inertia(ty, ViewY);
                if (vy > 0)
                {
                    if (vy > _camSpeedY + 3 || (vy > _camSpeedY && vy < 4)) _camSpeedY++;
                    if (vy < _camSpeedY) _camSpeedY = vy;
                }
                else if (vy < 0)
                {
                    if (vy < _camSpeedY - 3 || (vy < _camSpeedY && vy > -4)) _camSpeedY--;
                    if (vy > _camSpeedY) _camSpeedY = vy;
                }
                else _camSpeedY = vy;

                // Never overshoot a standing player.
                if (_lastDeltas[4] == 0 && Math.Abs(_camSpeedX) <= 2 && (ViewX + _camSpeedX - tx) * (ViewX - tx) <= 0) _camSpeedX = 0;
                if (_camSpeedX != 0) _lastScrollTime = now;
                ViewX += _camSpeedX;
                ViewY += _camSpeedY;
                if (ViewX < MinViewX) ViewX = MinViewX;
                _acceptedCamSpeedX = _camSpeedX;
            }
            // Near the right end the view may not stay scrolled up.
            if (ViewY < 0 && ViewX > (_h.LeftEnd + _h.RightEnd * 4) / 5) ViewY++;
            if (ViewX > _h.RightEnd - 800) ViewY = 0;
            if (ViewY < -10) ViewY = -10;
            if (ViewY > 0) ViewY = 0;
            ClampView();
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(Screen2D s)
        {
            if (_h == null)
            {
                s.FillRect(new RectInt(0, 0, s.Width, 480), new Color32(20, 20, 30, 255));
                return;
            }
            // Centre once the real viewport width is known (the framebuffer is resized at the first frame).
            if (!_centred && _h.PlayerPerson(Local) != null) { CenterCameraOnPlayer(); _centred = true; }
            s.FillRect(new RectInt(0, 0, s.Width, HallHeight), new Color32(0, 0, 0, 255));
            s.PushClip(new RectInt(0, 0, s.Width, HallHeight));
            DrawHorizon(s);
            DrawPlanes(s); // AirportScreen.Planes.cs: runway and behind-the-windows planes (§9.2)
            DrawBuildsAndPersons(s);
            s.PopClip();
            _bar.Draw(s, this, App.InputNow);
            _menus.Draw(s, App.InputNow);
            App.Advisors.Draw(s, OX);
        }

        void DrawSprite(Screen2D s, HallSprite sp, int x, int y, byte alpha = 255)
        {
            if (sp?.Tex == null) return;
            if (x + sp.W < 0 || x > s.Width || y + sp.H < 0 || y > HallHeight) return;
            s.Draw(sp.Tex, x, y, new Color32(255, 255, 255, alpha));
        }

        void DrawHorizon(Screen2D s)
        {
            var data = _h.Data;
            var layers = new BrickDef[5];
            for (int c = 0; c < 5; c++) layers[c] = data.Brick(5000 + c);
            int[] sizes = new int[5];
            for (int c = 0; c < 5; c++) sizes[c] = layers[c]?.Width ?? 0;
            var clips = _h.Layout.ClipMarkers;
            int drawn = 0;
            for (int c = 1; c < clips.Count; c++)
            {
                var prev = clips[c - 1];
                if (prev.Type != RuneId.ClipFull && clips[c].X >= ViewX)
                {
                    int x0 = Math.Max(drawn - ViewX, 0), x1 = Math.Min(clips[c].X - ViewX, s.Width);
                    int y0 = prev.Type == RuneId.ClipMiddle ? 168 : 0;
                    if (x1 > x0)
                    {
                        s.PushClip(new RectInt(x0, y0, x1 - x0, HallHeight - y0));
                        DrawParallax(s, layers, sizes);
                        s.PopClip();
                    }
                }
                drawn = clips[c].X;
                if (drawn >= ViewX + s.Width) break;
            }
            // Runway end at the workshop.
            var runway = data.Brick(5006);
            if (runway != null) DrawSprite(s, _g.Brick(runway.Frames.Length > 0 ? runway.Frames[0] : null, runway.NonTrans == 1), _h.RightEnd - ViewX - 20 - 89, 34 + 44 + 72 + 70);
        }

        void DrawParallax(Screen2D s, BrickDef[] layers, int[] sizes)
        {
            int pair = sizes[0] + sizes[1];
            if (pair > 0 && layers[0] != null && layers[1] != null)
            {
                var a = _g.Brick(layers[0].Frames[0], layers[0].NonTrans == 1);
                var b = _g.Brick(layers[1].Frames[0], layers[1].NonTrans == 1);
                int bx = ((100000 - ViewX * 16) >> 6) % pair - pair;
                for (int d = s.Width / pair + 2; d >= 0; d--)
                {
                    DrawSprite(s, a, bx + d * pair, 0);
                    DrawSprite(s, b, bx + sizes[0] + d * pair, 0);
                }
            }
            int[] k = { 0, 0, 30, 42, 56 };
            int[] y = { 0, 0, 34, 78, 150 };
            for (int c = 2; c < 5; c++)
            {
                if (sizes[c] <= 0 || layers[c] == null) continue;
                var sp = _g.Brick(layers[c].Frames[0], layers[c].NonTrans == 1);
                int bx = ((100000 - ViewX * k[c]) >> 6) % sizes[c] - sizes[c];
                for (int d = s.Width / sizes[c] + 2; d >= 0; d--) DrawSprite(s, sp, bx + d * sizes[c], y[c]);
            }
        }

        static int PersonKey(Person p) => ((p.ScreenPos.Y < 170 ? 60 : 20) << 16) + p.ScreenPos.Y;

        void DrawBuildsAndPersons(Screen2D s)
        {
            var builds = _h.Layout.Builds;
            var persons = _h.Persons;
            int bi = 0, pi = 0;
            int lo = ViewX - 400, hi = ViewX + s.Width + 400;
            while (bi < builds.Count || pi < persons.Count)
            {
                // skip runes and builds far off-screen
                while (bi < builds.Count && (builds[bi].BrickId >= 2000 || builds[bi].X > hi || builds[bi].X + builds[bi].Brick.Width < lo)) bi++;
                int t1 = bi < builds.Count ? builds[bi].SortKey : int.MaxValue;
                int t2 = pi < persons.Count ? PersonKey(persons[pi]) : int.MaxValue;
                if (t1 == int.MaxValue && t2 == int.MaxValue) break;
                if (t2 <= t1) { DrawPerson(s, persons[pi]); pi++; }
                else { DrawBuild(s, builds[bi]); bi++; }
            }
        }

        // ------------------------------------------------------------------ bricks (§8)

        /// <summary>Frame of a brick for an explicit or default phase (BRICK::BlitAt).</summary>
        int Frame(BrickDef b, int phase = -1, bool forceAnim = false)
        {
            int n = b.Frames.Length;
            if (n == 0) return -1;
            int speed = forceAnim && b.AnimSpeed == 0 ? 1 : b.AnimSpeed;
            if (speed == 0) return 0;
            int f = phase;
            if (b.Triggered == 0 || forceAnim)
            {
                if (phase == -1) f = (int)(Ticker / speed % n);
                else if (phase >= 100) f = (int)((phase - 100 + Ticker / speed) % n);
            }
            if (b.Triggered == 90 && b.WaitSum > 0)
            {
                long target = Ticker / speed % b.WaitSum;
                for (int c = 0; c < n; c++) { target -= b.WaitTimes[c]; if (target <= 0) { f = c; break; } }
            }
            if (f < 0) f = 0;
            return f < n ? f : n - 1;
        }

        void DrawFrame(Screen2D s, BrickDef b, int frame, int x, int y)
        {
            if (frame < 0 || frame >= b.Frames.Length) return;
            var sp = _g.Brick(b.Frames[frame], b.NonTrans == 1);
            // Glass bricks blend 50 % (transparency option, on by default) when not animated.
            byte alpha = b.NonTrans == 2 && b.AnimSpeed == 0 ? (byte)128 : (byte)255;
            DrawSprite(s, sp, x, y, alpha);
        }

        static bool InIds(int id, int a, int b) => id >= a && id <= b;

        void DrawBuild(Screen2D s, in Build bd)
        {
            var b = bd.Brick;
            int id = b.Id;
            int x = bd.X - ViewX, y = bd.Y - ViewY;
            if (x + b.Width < -2 || x > s.Width) return;

            if (b.Triggered == 1)
            {
                if (id == 841) return;                               // kiosk man: only while the newspaper is interesting
                if (id == 852) { DrawFrame(s, b, Frame(b, -1, true), x, y); return; } // sleeping kiosk man
                if (id == 421) { DrawFrame(s, b, _w != null && !_w.Items.PaperClip && b.Frames.Length > 1 ? 1 : 0, x, y); return; } // route box pin board: frame 1 once the paper clips are taken
                int f = 0;
                if ((uint)bd.Par < (uint)_h.TriggerSerial.Length && b.AnimSpeed > 0)
                {
                    int serial = _h.TriggerSerial[bd.Par];
                    if (!_triggerStarts.TryGetValue(bd.Par, out var st)) { st = (serial, -1); _triggerStarts[bd.Par] = st; }
                    if (st.serial != serial) { st = (serial, Ticker); _triggerStarts[bd.Par] = st; }
                    if (st.start >= 0)
                    {
                        long e = (Ticker - st.start) / b.AnimSpeed;
                        if (e >= b.Frames.Length) _triggerStarts[bd.Par] = (serial, -1); else f = (int)e;
                    }
                }
                DrawFrame(s, b, f, x, y);
                return;
            }
            if (b.Triggered == 2)
            {
                // Roll-up shutter: clipped to its rectangle, shifted up by the open fraction.
                int open = ShutterOpen(b.BaseY);
                var sp = _g.Brick(b.Frames.Length > 0 ? b.Frames[0] : null, b.NonTrans == 1);
                if (sp == null) return;
                s.PushClip(new RectInt(x, y, sp.W, sp.H));
                DrawSprite(s, sp, x, y - (sp.H * open >> 8));
                s.PopClip();
                return;
            }
            if (InIds(id, 718, 729)) { DrawFrame(s, b, Frame(b, (uint)bd.Par < (uint)_h.Doors.Length ? _h.Doors[bd.Par].Winkel : 0), x, y); return; }
            if (InIds(id, 760, 775)) return; // gate video placeholders: only with the enhanced gate (bit 8)
            int belt = ((bd.X + bd.Y / 2) / 44 + bd.Y / 22);
            if (InIds(id, 450, 452)) { DrawFrame(s, b, Frame(b, belt * 2 % 7 + 100), x, y); return; }
            if (InIds(id, 453, 455)) { DrawFrame(s, b, Frame(b, belt % 7 + 100), x, y); return; }
            if (id == 457 || id == 458) { DrawFrame(s, b, Frame(b, 6 - belt * 2 % 7 + 100), x, y); return; }
            int owner = _h.Flights?.GateOwner(bd.Par) ?? -1;
            if (id == 492) { DrawFrame(s, b, Frame(b, owner + 1), x, y); return; }
            if (id == 337 || id == 338) { DrawFrame(s, b, Frame(b, owner >= 0 ? (_h.Flights.SecurityFlags(owner) & 1) : 0), x, y); return; }
            if (id == 521 || id == 522)
            {
                int sec = owner >= 0 ? _h.Flights.SecurityFlags(owner) : 0;
                DrawFrame(s, b, Frame(b, (sec & (1 << 10)) != 0 ? 2 : (sec & (1 << 8)) != 0 ? 1 : 0), x, y);
                DrawGateLogos(s, id, owner, x, y); // AirportScreen.Planes.cs
                return;
            }
            DrawFrame(s, b, Frame(b), x, y);
            if (id == 500 || id == 520) DrawFlightDisplay(s, bd, x, y);
            if (id == 520) DrawGateLogos(s, id, owner, x, y);
        }

        /// <summary>Text bricks over the check-in (500) and gate (520) counters (§9.3), when the flight domain provides texts.</summary>
        void DrawFlightDisplay(Screen2D s, in Build bd, int x, int y)
        {
            if (_h.Flights == null) return;
            string text = bd.BrickId == 500 ? _h.Flights.FlightDisplayText(bd.Par) : _h.Flights.FlightDisplayText(30 + bd.Par);
            if (string.IsNullOrEmpty(text)) return;
            var font = Assets.Font("small_wh.mcf") ?? Assets.Font("norm_wh.mcf");
            if (font == null) return;
            s.PushClip(new RectInt(x + (bd.BrickId == 500 ? 6 : 43), y + (bd.BrickId == 500 ? 2 : 5), 132, 12));
            font.Draw(s, x + (bd.BrickId == 500 ? 6 : 43), y + (bd.BrickId == 500 ? 2 : 5), text);
            s.PopClip();
        }

        /// <summary>Shutter opening 0..256 by shop (brick BaseY), from the clock (§8.2).</summary>
        int ShutterOpen(int shop)
        {
            int t = _w.Clock.Time, wd = _w.Clock.Weekday;
            bool weekend = wd == 5 || wd == 6;
            switch (shop)
            {
                case 0:
                    if (weekend && t > OpeningHours.DutyClose + 2000) return 0;
                    if (weekend && t > OpeningHours.DutyClose) return 256 - (t - OpeningHours.DutyClose) * 256 / 2000;
                    if (t < OpeningHours.DutyOpen - 2000) return 0;
                    if (t > OpeningHours.DutyOpen) return 256;
                    return (t - (OpeningHours.DutyOpen - 2000)) * 256 / 2000;
                case 1:
                    if (t < OpeningHours.ArabOpen - 2000 || wd == 6) return 0;
                    if (t > OpeningHours.ArabOpen) return 256;
                    return (t - (OpeningHours.ArabOpen - 2000)) * 256 / 2000;
                case 2:
                    if (t > OpeningHours.LastClose + 2000 || wd == 5) return 0;
                    if (t < OpeningHours.LastClose) return 256;
                    return 256 - (t - OpeningHours.LastClose) * 256 / 2000;
                case 3:
                    if (t < OpeningHours.MuseOpen - 2000 || weekend) return 0;
                    if (t > OpeningHours.MuseOpen) return 256;
                    return (t - (OpeningHours.MuseOpen - 2000)) * 256 / 2000;
                case 4:
                    if (t > OpeningHours.ReisClose + 2000) return 0;
                    if (t < OpeningHours.ReisClose) return 256;
                    return 256 - (t - OpeningHours.ReisClose) * 256 / 2000;
                case 5: return t > OpeningHours.MaklClose ? 0 : 256;
                case 6: return t < OpeningHours.WerbOpen || weekend ? 0 : 256;
            }
            return 256;
        }

        // ------------------------------------------------------------------ persons (§6.2)

        void DrawPerson(Screen2D s, Person p)
        {
            var k = _h.Clan(p.ClanId);
            if (k == null) return;
            var foot = new Pt(p.ScreenPos.X - ViewX - 7, p.ScreenPos.Y - ViewY + 1);
            bool sitting = !k.IsPlayer && (p.LookDir == 6 || p.LookDir == 7);
            if (!sitting && k.Group <= 30 && !k.IsPlayer)
                foot += new Pt(p.FlightPlaneIndex & 3, (p.FlightPlaneIndex >> 2) & 3);
            if (foot.X + 200 < 0 || foot.X > s.Width + 100) return;

            int dir = p.LookDir;
            bool walkway = p.Position.Y / 22 + 5 == 13 && (_h.P(p.Position.X / 44, p.Position.Y / 22 + 5) & 240) == 80;
            bool running = k.IsPlayer ? p.Running && !walkway : p.Running;
            if (dir == 4) foot += new Pt(k.GimmickOffsetX, k.GimmickOffsetY);
            int slot, faktor = Math.Max(1, k.Faktor), frame;
            if (dir < 8 && running) { slot = dir + 10; faktor *= 2; } else slot = Math.Min(dir, 13);
            int phase = p.Phase;
            // Phone pose (§6.2: standing frames 4..7). A computer figure held on the phone stands in its walking direction.
            bool phone = k.IsPlayer && OnThePhone(k.Type - ClanType.Player1);
            if (phone && dir < 4 && k.Type - ClanType.Player1 != Local) { phase = dir; dir = 8; slot = ClanDef.Stand; }
            if (dir < 8 || dir == 9)
            {
                int n = k.FrameCount(slot);
                if (n == 0) return;
                if (phase / faktor >= n) phase = 0;
                frame = phase / faktor;
            }
            else
            {
                slot = ClanDef.Stand;
                int n = k.FrameCount(slot);
                if (n == 0) return;
                if (phase >= n) phase = 0;
                frame = phase;
                if (phone && dir == 8 && n >= 8) frame = (phase & 3) + 4;
            }
            if (!(foot.X > -60 && foot.X < s.Width + 50)) return;

            // Shadow first (not for luggage, the stuck/wave slot, or a player going to/from security).
            var hp = k.IsPlayer ? _h.Players[k.Type - ClanType.Player1] : null;
            bool security = hp != null && (hp.DirectToRoom == RoomIds.Security || hp.ExRoom == RoomIds.Security);
            if (k.Type != ClanType.Suitcase && slot != ClanDef.Wave && !security)
            {
                var sl = k.Shadows[slot];
                if (sl != null && sl.Length > 0)
                {
                    var sh = _g.Shadow(dir < 8 || dir == 9 ? sl[frame % sl.Length] : sl[phase % sl.Length]);
                    if (sh != null) DrawSprite(s, sh, foot.X - sh.W / 2 + k.ShadowOffsetX, foot.Y - (sh.H - 1) + k.ShadowOffsetY);
                }
            }
            var sp = _g.PersonFrame(k, slot, frame);
            if (sp != null) DrawSprite(s, sp, foot.X - sp.W / 2 + k.OffsetX, foot.Y - (sp.H - 1) + k.OffsetY);

            DrawMood(s, p, k);
        }

        /// <summary>
        /// The airline's figure is on the phone: the local player while a phone dialog runs over the hall (the original);
        /// a computer airline the human is phoning when the <see cref="GameOptions.ComputerPhonePose"/> addition is on.
        /// </summary>
        bool OnThePhone(int player)
        {
            if ((uint)player >= (uint)Player.Count) return false;
            if (player == Local)
            {
                if (App.Rooms.Phone != null) return true;
                foreach (var sc in App.Screens.All) if (sc is Rooms.Rivals.RivalCallScreen r && r.OnPhone) return true;
                return false;
            }
            return _w.Options.ComputerPhonePose && _w.Players[player].IsComputer && _h.Players[player].PhonePartner != 0;
        }

        /// <summary>Thought bubble with the fade-in/out grade (§6.7).</summary>
        void DrawMood(Screen2D s, Person p, ClanDef k)
        {
            if (p.MoodCountdown <= 0 || p.MoodCountdown > Mood.CountStart) return;
            if (!(p.ScreenPos.Y > 219 || p.ScreenPos.Y < 150)) return;
            int grade = 3;
            if (Mood.CountStart - p.MoodCountdown < 11) grade = 8 - (Mood.CountStart - p.MoodCountdown) / 2;
            else if (p.MoodCountdown < 11) grade = 8 - p.MoodCountdown / 2;
            if (grade < -1 || grade > 8) return;
            var bubble = _g.Std("MOOD" + Mood.BitmapNumber(p.Mood).ToString("00"));
            if (bubble == null) return;
            var first = _g.PersonFrame(k, 0, 0);
            int sh = first?.H ?? 60;
            int x = p.ScreenPos.X - ViewX + 7 - bubble.W * 4 / 5, y = p.ScreenPos.Y - ViewY - bubble.H - sh - 1;
            byte alpha = (byte)Mathf.Clamp((8 - grade) * 255 / 8, 0, 255);
            DrawSprite(s, bubble, x, y, alpha);
            // Passenger opinions carry the small logo of the flight's airline in the bubble's box at (8,10).
            if (Mood.ShowsAirlineLogo(p.Mood) && (uint)p.FlightAirline < (uint)Player.Count)
                DrawSprite(s, _g.Std("SLOGO" + p.FlightAirline), x + 8, y + 10, alpha);
        }

        // ------------------------------------------------------------------ overlays

        string TooltipText(int room)
        {
            if (room >= RoomIds.FlightDisplay && room < RoomIds.FlightDisplay + 60) return FlightDisplayTooltip(room - RoomIds.FlightDisplay);
            string t = Assets.StdText.Get("Tool", 5000 + room);
            if (t == null) return null;
            if (RoomIds.IsOfficeOrPersonnel(room))
            {
                int owner = room / 10 - 1;
                if (owner >= 0 && owner < Player.Count && t.Contains("%s")) t = t.Replace("%s", _w.Players[owner].Airline ?? "");
            }
            return t;
        }
    }
}
