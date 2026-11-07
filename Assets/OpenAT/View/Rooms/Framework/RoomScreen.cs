using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.View.Render;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// Base of every room screen (the original's CStdRaum, rooms-and-ui-1 §2–§9): a 640x440 room canvas
    /// centred horizontally (side areas filled), background from the room GLI, per-frame hotspots with
    /// cursor/tooltip, exit areas (id 999), right-click leave, ambient sounds, first-visit help, the
    /// room's menu slot and dialog, the full-width status bar and advisor layer.
    ///
    /// Subclasses override <see cref="Animate"/> (characters, always), <see cref="Hotspots"/> (hover
    /// rects, only while no dialog/menu is open), <see cref="OnClick"/> (hotspot id clicked),
    /// <see cref="DrawRoom"/> (into the canvas) and optionally <see cref="DrawOverlay"/>.
    /// A room may also be instantiated as a phone pane (<see cref="IsPhonePane"/>): then only the
    /// background and animations are used.
    /// </summary>
    public abstract class RoomScreen : GameScreen, IStatusBarHost, IDialogStage
    {
        public const int ExitId = 999;
        public const int CanvasWidth = 640, CanvasHeight = 440;

        /// <summary>Original room id (<see cref="RoomIds"/>); set from <see cref="RoomAttribute"/> by the registry.</summary>
        public int RoomId { get; internal set; }
        /// <summary>Room graphics library, e.g. "room/bank.gli" (null = none).</summary>
        protected abstract string Library { get; }
        /// <summary>Background bitmap in <see cref="Library"/>, e.g. "BANK".</summary>
        protected abstract string Background { get; }
        /// <summary>Who reacts to inventory items shown in this room (−1 none, part 1 §9.4).</summary>
        protected virtual int DefaultDialogPartner => -1;
        /// <summary>
        /// Global ambient volume (0..100) set every time the room is entered, not as a phone pane (the original's
        /// per-room global-volume call); null keeps the current one. Default: the central <see cref="RoomAmbience"/> table.
        /// </summary>
        public virtual int? AmbientVolume => RoomAmbience.For(RoomId);

        /// <summary>True when the room only serves as the picture of a phone call.</summary>
        public bool IsPhonePane { get; internal set; }
        /// <summary>Overrides the canvas x while drawn as a phone pane.</summary>
        internal int? PaneOriginX;

        public MenuHost Menus { get; private set; }
        public DialogSession Dialog { get; private set; }
        protected readonly StatusBar Bar = new StatusBar();

        /// <summary>Screen x of the room canvas (centred 640 area, or the phone-pane position).</summary>
        public int CanvasX => PaneOriginX ?? OX;
        /// <summary>Shorthand for <see cref="CanvasX"/> in drawing code.</summary>
        protected int RX => CanvasX;

        Texture2D _bg;
        int _hoverId = -1;
        readonly AmbientFx[] _fx = new AmbientFx[5];
        readonly System.Random _rng = new System.Random();
        bool _entered;

        sealed class AmbientFx { public string File; public int AvgMs; public float Volume; public float Next; }

        protected RoomScreen()
        {
            foreach (RoomAttribute a in GetType().GetCustomAttributes(typeof(RoomAttribute), false)) { RoomId = a.Id; break; }
        }

        // ------------------------------------------------------------------ lifecycle

        public override void OnEnter()
        {
            if (!IsPhonePane) App.EnsureSession();   // before Prepare: rooms set up from the game state
            Prepare();
            if (IsPhonePane) return;
            App.Rooms.Attach(this);
            App.Advisors.NoComments();
            if (AmbientVolume is int vol) App.Audio.SetGlobalAmbientVolume(vol);
            SetRoomVisited();
            if (!_entered) { _entered = true; OnRoomEnter(); }
        }

        /// <summary>Shared set-up for normal rooms and phone panes.</summary>
        internal void Prepare()
        {
            if (Menus != null) return;
            Menus = new MenuHost(App, () => CanvasX);
            Dialog = new DialogSession(this);
            _bg = Library != null && Background != null ? Assets.Texture(Library, Background) : null;
            OnCreate();
        }

        /// <summary>Called once when the room is created (also for phone panes): set up characters here.</summary>
        protected virtual void OnCreate() { }
        /// <summary>Called once when the player enters (not for phone panes): ambience, hints, perks.</summary>
        protected virtual void OnRoomEnter() { }

        public override void OnExit()
        {
            Dialog?.Stop();
            App.Audio.StopVoice();
            if (_ambientLoop != null) { App.Audio.StopLoop(_ambientLoop); _ambientLoop = null; }
            OnLeaveRoom();
            if (Library != null) Assets.Unload(Library);
        }

        protected virtual void OnLeaveRoom() { }

        AudioSource _ambientLoop;

        /// <summary>
        /// A continuously looping room sound at the effects volume (the original's looped room effects, e.g.
        /// the museum or the snoring kiosk man); stopped when the room is left. Not for phone panes.
        /// </summary>
        protected void PlayAmbientLoop(string file)
        {
            if (IsPhonePane || App.Audio.EffectsVolume <= 0) return;
            if (_ambientLoop != null) App.Audio.StopLoop(_ambientLoop);
            _ambientLoop = App.Audio.PlayLoop("SOUND/" + file, App.Audio.EffectsVolume);
        }

        /// <summary>Stops the loop started by <see cref="PlayAmbientLoop"/>.</summary>
        protected void StopAmbientLoop()
        {
            if (_ambientLoop != null) { App.Audio.StopLoop(_ambientLoop); _ambientLoop = null; }
        }

        /// <summary>First visit: the assistant explains the room (part 1 §5.4).</summary>
        void SetRoomVisited()
        {
            int desc = RoomIds.DescriptionIndex(RoomId);
            bool first = !App.Rooms.WasVisited(RoomId);
            App.Rooms.MarkVisited(RoomId);
            if (desc == 0 || !first) return;
            // Not in the tutorial mission: the scripted assistant explains instead (part 1 §5.4).
            if (App.Session != null && App.Session.World.Difficulty == Sim.Mission.Tutorial) return;
            int mask = PlayerPrefs.GetInt("OpenAT.RoomDescriptions", 0);
            if ((mask & (1 << desc)) != 0) return;
            PlayerPrefs.SetInt("OpenAT.RoomDescriptions", mask | (1 << desc));
            int textRoom = RoomIds.IsOffice(RoomId) ? 10 : desc == 2 ? 11 : RoomId;
            var text = Assets.StdText.Get("Tuto", 10000 + textRoom);
            if (text != null) App.Advisors.Queue(AdvisorKind.Assistant, text);
        }

        /// <summary>Ambient sound slot 0..4 (part 1 §2.6): plays every avgWait × 50..149 % ms.</summary>
        protected void SetBackgroundFx(int slot, string file, int avgWaitMs, int startWaitMs = 0, int volumePct = 100)
        {
            if (PlayerPrefs.GetFloat(GameAudio.PrefAmbience, 1f) <= 0) return;
            _fx[slot] = new AmbientFx
            {
                File = "SOUND/" + file, AvgMs = avgWaitMs, Volume = volumePct / 100f,
                Next = Time.unscaledTime + (avgWaitMs * (50 + _rng.Next(100)) / 100f - startWaitMs) / 1000f,
            };
        }

        // ------------------------------------------------------------------ dialog / stage

        public void StartDialog(int talker, DialogMedium medium = DialogMedium.Air, int par1 = -1, int par2 = -1, int par3 = -1) =>
            Dialog.Start(talker, medium, par1, par2, par3);

        public bool IsDialogOpen => Dialog != null && Dialog.IsOpen;

        /// <summary>The animated figure of a talker in this room (override in rooms with characters).</summary>
        public virtual SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => null;

        public virtual bool IsOwnOffice => false;

        public virtual void OnDialogStopped(int talker) { }

        // ------------------------------------------------------------------ hotspots

        /// <summary>
        /// Registers a hotspot rect for this frame (inclusive, canvas coordinates). Returns true when
        /// hovered; the last hovered hotspot gets the click (<see cref="OnClick"/>).
        /// </summary>
        protected bool Hot(int x1, int y1, int x2, int y2, int id, int tooltip = 0, CursorLook look = CursorLook.Hot)
        {
            var m = App.InputNow.Mouse;
            int x = m.x - RX, y = m.y;
            App.Ui.Targets.Add(x1 + RX, y1, x2 + RX, y2);   // gamepad target (OpenAT)
            if (x < x1 || x > x2 || y < y1 || y > y2) return false;
            _hoverId = id;
            App.Ui.Cursor = look;
            if (tooltip != 0) App.Ui.Tooltip.Set(tooltip);
            return true;
        }

        /// <summary>An exit area: EXIT cursor, click leaves the room (part 1 §8.3).</summary>
        protected bool Exit(int x1, int y1, int x2, int y2) => Hot(x1, y1, x2, y2, ExitId, 0, CursorLook.Exit);

        /// <summary>Per-frame hover pass (only when no dialog and no menu is open).</summary>
        protected virtual void Hotspots(InputState input) { }

        /// <summary>A hotspot was clicked. Exit (999) is handled by the base.</summary>
        protected virtual void OnClick(int id) { }

        /// <summary>Animations; called every frame (also during dialogs, menus and as a phone pane).</summary>
        public virtual void Animate(float dt) { }

        // ------------------------------------------------------------------ update

        public override void Update(InputState input, float dt)
        {
            if (Menus == null) Prepare();
            Animate(dt);
            Menus.Animate(dt);
            PumpAmbience();
            App.Advisors.Update(dt, CanvasX, input, !Menus.IsOpen);
            int barTop = Screen.Height - StatusBar.Height;
            bool inBar = input.Mouse.y >= barTop;

            if (Menus.IsOpen)
            {
                if (inBar) Bar.Update(this, input);
                Menus.Update(input, dt);
                return;
            }
            if (Bar.Update(this, input)) return;
            if (HandleKeys(input)) return;
            if (IsDialogOpen)
            {
                Dialog.Update(input, dt);
                return;
            }

            _hoverId = -1;
            // With the pad the hotspots are registered (as gamepad targets) also while the cursor is in the status bar,
            // so the d-pad can come back up (OpenAT); they cannot be hovered from there.
            if (!inBar || input.PadActive) Hotspots(input);
            if (input.LeftDown && !input.Consumed && !inBar && _hoverId >= 0)
            {
                input.Consume();
                if (Tutorial.TutorialView.BlockClick(this, _hoverId)) return;   // the tutorial's click gates
                if (_hoverId == ExitId) Leave();
                else OnClick(_hoverId);
                return;
            }
            // Right click leaves the room; the pad's B does so wherever its cursor is (OpenAT).
            if (input.RightDown && !input.Consumed && (!inBar || input.PadActive))
            {
                input.Consume();
                if (Tutorial.TutorialView.BlockRightClick(this)) return;
                Leave();
            }
        }

        /// <summary>Leaves this room (pops one location, part 9 §6.2).</summary>
        protected virtual void Leave() => App.Rooms.Leave();

        bool HandleKeys(InputState input)
        {
            var session = App.Session;
            // Esc/F2/F3/F4 and Tab share the room hotkeys' activity gate (no tutorial, no dialog, 8-18 h, not in the
            // evening screen, ...); F2 also opens the options through the room window's own handler (part 1 §7.1/§7.2).
            if (RoomHotkeys.HandleOptionKeys(App, input, IsDialogOpen)) return true;
            if (session == null) return false;
            // Space while the tutorial runs skips the whole tutorial (part 1 §7.1).
            if (input.Space && !IsDialogOpen && Tutorial.TutorialView.Active) { Tutorial.TutorialView.Cancel(App); return true; }
            foreach (char c in input.Typed)
            {
                if (c == '+') session.Local.GameSpeed = (session.Local.GameSpeed + 1) % 4;
                else if (c == '-') session.Local.GameSpeed = (session.Local.GameSpeed + 3) % 4;
            }
            if (input.Tab && RoomHotkeys.GlobalKeysAllowed(App, session.World, IsDialogOpen))
            {
                // The evening sequence itself is run by GameFlow (rooms-and-ui-9 §6.4).
                Menus.Open(RequestMenu.Create(RequestMenu.CallItADay, null, () => GameFlow.CallItADay(App)), new Vector2Int(320, 220), 0.17f);
                return true;
            }
            // Walk-to-room hotkeys (rooms-and-ui-1 §7.2): leave all rooms and walk there.
            return RoomHotkeys.Handle(App, input, IsDialogOpen);
        }

        void PumpAmbience()
        {
            float now = Time.unscaledTime;
            foreach (var f in _fx)
            {
                if (f == null || now < f.Next) continue;
                App.Audio.PlayAmbient(f.File, f.Volume);
                f.Next = now + f.AvgMs * (50 + _rng.Next(100)) / 100000f;
            }
        }

        /// <summary>Inventory item clicked in the status bar (part 1 §9.4): generic part.</summary>
        public virtual void UseItem(int slot, int item)
        {
            if (IsDialogOpen) return;
            // The items domain decides; gifts open the default partner's dialog (10000 + item).
            Items.ItemUseView.Use(this, slot, item, RoomId, DefaultDialogPartner);
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(Screen2D s)
        {
            DrawCanvas(s, true);
            Tutorial.TutorialView.DrawGlows(s, CanvasX, RoomId, TutorialBlockOrigin);
            Dialog.Draw(s);
            if (ShowsStatusBar) Bar.Draw(s, this, App.InputNow);
            Menus.Draw(s, App.InputNow);
            App.Advisors.Draw(s, CanvasX);
            DrawOverlay(s);
        }

        /// <summary>Canvas position the tutorial's planner markers are relative to (null = no planner window shown).</summary>
        protected virtual Vector2Int? TutorialBlockOrigin => null;

        /// <summary>Full-screen rooms (taller than 440) hide the status bar (part 1 §9.2).</summary>
        protected bool ShowsStatusBar => _bg == null || _bg.height <= CanvasHeight;

        /// <summary>Background (+ side fill) and room content.</summary>
        internal void DrawCanvas(Screen2D s, bool sides)
        {
            int x = RX;
            if (_bg != null)
            {
                if (sides && x > 0)
                {
                    WideFill.Sides(s, _bg, x, 0);
                    // Dim the side fill further so the room canvas reads as the playing area.
                    var dim = new Color32(0, 0, 0, 120);
                    s.FillRect(new RectInt(0, 0, x, _bg.height), dim);
                    s.FillRect(new RectInt(x + CanvasWidth, 0, s.Width - x - CanvasWidth, _bg.height), dim);
                }
                s.Draw(_bg, x, 0);
            }
            else if (sides) s.FillRect(new RectInt(0, 0, s.Width, CanvasHeight), Color.black);
            s.PushClip(new RectInt(x, 0, CanvasWidth, _bg != null ? _bg.height : CanvasHeight));
            DrawRoom(s);
            s.PopClip();
        }

        /// <summary>Room content (animations, objects) in canvas coordinates offset by <see cref="RX"/>.</summary>
        protected virtual void DrawRoom(Screen2D s) { }

        /// <summary>Drawn on top of everything of the room (tips, share charts, debug).</summary>
        protected virtual void DrawOverlay(Screen2D s) { }

        /// <summary>Clock state helpers for rooms.</summary>
        protected GameClock Clock => App.Session?.World.Clock;
    }

    /// <summary>Marks a <see cref="RoomScreen"/> with its original room id(s) for the <see cref="RoomRegistry"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RoomAttribute : Attribute
    {
        public readonly int Id;
        public RoomAttribute(int id) => Id = id;
    }

    /// <summary>
    /// Room id → factory. Rooms with a <see cref="RoomAttribute"/> and a parameterless constructor are
    /// found automatically; extensions call <see cref="Register"/> (new ids ≥ <see cref="RoomIds.FirstCustom"/>,
    /// or an original id to replace that room).
    /// </summary>
    public static class RoomRegistry
    {
        static Dictionary<int, Func<RoomScreen>> _map;

        static Dictionary<int, Func<RoomScreen>> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<int, Func<RoomScreen>>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || !typeof(RoomScreen).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        foreach (RoomAttribute a in t.GetCustomAttributes(typeof(RoomAttribute), false))
                        {
                            var type = t;
                            int id = a.Id;
                            if (!_map.ContainsKey(id)) _map[id] = () => { var r = (RoomScreen)Activator.CreateInstance(type); r.RoomId = id; return r; };
                        }
                    }
                }
                return _map;
            }
        }

        public static void Register(int id, Func<RoomScreen> create) =>
            Map[id] = () => { var r = create(); r.RoomId = id; return r; };

        public static bool Has(int id) => Map.ContainsKey(id);
        public static IEnumerable<int> Ids => Map.Keys;

        public static RoomScreen Create(int id) => Map.TryGetValue(id, out var f) ? f() : null;
    }
}
