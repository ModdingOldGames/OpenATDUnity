using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// The local player's location stack and room windows (rooms-and-ui-9 §4, §6): the airport is the
    /// base (whatever screen lies under the rooms — the hall, or a placeholder), rooms are pushed on
    /// top. Only the top room has a live screen: entering a sub-room destroys the room below and
    /// leaving re-creates it fresh (state lives in the sim, not in room objects). Room changes
    /// crossfade (§9.1). Phone calls show the partner's room as a slide-in pane (part 1 §3).
    /// </summary>
    public sealed class RoomHost
    {
        public const int MaxDepth = 10;
        readonly GameApp _app;
        readonly List<int> _stack = new List<int>();

        public RoomHost(GameApp app) => _app = app;

        /// <summary>The live room screen (top of the stack), or null at the airport.</summary>
        public RoomScreen Current { get; private set; }
        /// <summary>Top location id (<see cref="RoomIds.Airport"/> when no room is open).</summary>
        public int CurrentRoom => _stack.Count > 0 ? _stack[_stack.Count - 1] : RoomIds.Airport;
        public IReadOnlyList<int> Locations => _stack;
        public PhoneCallScreen Phone { get; internal set; }

        /// <summary>Called when the airport base is needed but none exists (no hall yet).</summary>
        public static System.Func<GameScreen> CreateAirportBase = () => ScreenFactory.Create("hall") ?? new LobbyScreen();

        bool FadeAllowed
        {
            get
            {
                if (PlayerPrefs.GetInt("OpenAT.Fade", 1) == 0) return false;
                var c = _app.Session?.World.Clock;
                return c == null || c.Hour > 9 || c.Date == 0;
            }
        }

        void Fade(int room)
        {
            if (room != RoomIds.Laptop && FadeAllowed) _app.Ui.Fade.Start(_app.Screen);
        }

        /// <summary>Forgets all locations (new game / back to the main menu).</summary>
        public void Reset()
        {
            _stack.Clear();
            Current = null;
            Phone = null;
            WalkToGlobe = false;
        }

        /// <summary>
        /// After loading a game (rooms-and-ui-9 §6.5): the saved stack becomes the location stack and only its
        /// top room is created (no walk, no hall request). Unknown rooms and the airport are skipped.
        /// </summary>
        public void Restore(IEnumerable<int> rooms)
        {
            Reset();
            foreach (int r in rooms)
                if (r != RoomIds.Airport && r != 0 && RoomRegistry.Has(r) && !_stack.Contains(r) && _stack.Count < MaxDepth) _stack.Add(r);
            if (_stack.Count == 0) return;
            var top = RoomRegistry.Create(_stack[_stack.Count - 1]);
            if (top == null) { _stack.Clear(); return; }
            Current = top;
            _app.Screens.Push(top);
        }

        /// <summary>A room screen was entered directly (e.g. created by name); adopt it as the top location.</summary>
        internal void Attach(RoomScreen r)
        {
            if (Current == r) return;
            Current = r;
            int id = r.RoomId;
            if (!_stack.Contains(id)) _stack.Add(id);
        }

        /// <summary>Enters a room on top of the current location (no-op if already on the stack).</summary>
        /// <summary>
        /// Set by the globe hotkey when the figure first has to walk to its office: entering the own office then opens
        /// the globe straight away (the office stays below it on the stack, its screen is never shown).
        /// </summary>
        public bool WalkToGlobe;

        public bool Enter(int room)
        {
            if (_stack.Contains(room) || _stack.Count >= MaxDepth) return false;
            var session = _app.Session;
            if (WalkToGlobe && session != null && room == RoomIds.OfficeOf(session.World.LocalPlayer) && RoomRegistry.Has(RoomIds.Globe))
            {
                WalkToGlobe = false;
                if (Current != null) _app.Screens.Remove(Current);
                Current = null;
                _stack.Add(room);
                return Enter(RoomIds.Globe);
            }
            var r = RoomRegistry.Create(room);
            if (r == null)
            {
                Debug.Log("OpenAT: room " + room + " not implemented");
                return false;
            }
            Fade(room);
            if (Current != null) _app.Screens.Remove(Current);
            _stack.Add(room);
            Current = r;
            _app.Screens.Push(r);
            return true;
        }

        /// <summary>Leaves the top room; the location below is re-created (part 9 §6.2).</summary>
        public void Leave()
        {
            if (_stack.Count == 0) return;
            int left = _stack[_stack.Count - 1];
            Fade(left);
            _stack.RemoveAt(_stack.Count - 1);
            if (Current != null) _app.Screens.Remove(Current);
            Current = null;
            _app.Advisors.NoComments();
            if (_stack.Count > 0)
            {
                var below = RoomRegistry.Create(_stack[_stack.Count - 1]);
                if (below != null) { Current = below; _app.Screens.Push(below); return; }
                _stack.Clear();
            }
            BackInHall();
            EnsureAirportBase();
        }

        /// <summary>Leaves every room (hotkeys, filofax walk): back to the airport.</summary>
        public void LeaveAll()
        {
            if (_stack.Count == 0) return;
            Fade(CurrentRoom);
            _stack.Clear();
            if (Current != null) _app.Screens.Remove(Current);
            Current = null;
            BackInHall();
            EnsureAirportBase();
        }

        /// <summary>
        /// Leaves <paramref name="room"/> and every location above it (a room's LEAVING flag spreads upwards, e.g. to a
        /// laptop opened inside it); the location below is re-created. No-op when the room is not on the stack.
        /// </summary>
        public void LeaveFrom(int room)
        {
            int i = _stack.IndexOf(room);
            if (i < 0) return;
            if (i == 0) { LeaveAll(); return; }
            while (_stack.Count > i + 1) _stack.RemoveAt(_stack.Count - 1);
            Leave();
        }

        /// <summary>The location stack fell back to the airport: the hall figure steps out of the door.</summary>
        void BackInHall()
        {
            var w = _app.Session?.World;
            if (w?.Hall != null) Debug.Log("OpenAT flow: back in the hall: " + w.Execute(new Sim.Hall.ReturnToHallCommand(w.LocalPlayer)));
            _app.Audio.SetGlobalAmbientVolume(RoomAmbience.Hall);   // the airport view sets 100 when it is shown again
            ReturnedToAirport?.Invoke();
        }

        /// <summary>Raised when the last room was left (the player is back at the airport).</summary>
        public event System.Action ReturnedToAirport;

        /// <summary>Enter, or leave if it is the current room (status-bar logo / options toggles).</summary>
        public void ToggleRoom(int room)
        {
            if (CurrentRoom == room) Leave(); else Enter(room);
        }

        /// <summary>
        /// Go to a room "on foot". TODO(rooms-and-ui-9 §4.1 WalkToRoom): the airport hall should make the
        /// figure walk there (<see cref="WalkHandler"/>); without a hall the room is entered directly.
        /// </summary>
        public void WalkTo(int room)
        {
            if (WalkHandler != null && WalkHandler(room)) { LeaveAll(); return; }
            LeaveAll();
            Enter(room);
        }

        /// <summary>Set by the airport hall: start walking to a room; return false if not possible.</summary>
        public static System.Func<int, bool> WalkHandler;

        void EnsureAirportBase()
        {
            // The screens below the room stay; only when nothing is left a base is created.
            int others = 0;
            foreach (var s in _app.Screens.All) if (!(s is RoomScreen) && !(s is PhoneCallScreen)) others++;
            if (others == 0) _app.Screens.Push(CreateAirportBase());
        }

        // ------------------------------------------------------------------ visited rooms

        HashSet<int> Visited
        {
            get
            {
                var p = _app.Session?.Local;
                if (p == null) return _noSession;
                if (!p.Extensions.TryGetValue("openat.visitedRooms", out var v) || !(v is HashSet<int> set))
                    p.Extensions["openat.visitedRooms"] = set = new HashSet<int>();
                return set;
            }
        }
        readonly HashSet<int> _noSession = new HashSet<int>();

        static int Collapse(int room) => RoomIds.IsOffice(room) ? RoomIds.OfficeA : room == RoomIds.PersonalB || room == RoomIds.PersonalC || room == RoomIds.PersonalD ? RoomIds.PersonalA : room;

        public bool WasVisited(int room) => Visited.Contains(Collapse(room));
        public void MarkVisited(int room) => Visited.Add(Collapse(room));

        // ------------------------------------------------------------------ phone

        /// <summary>Opening hours of phone partners (part 9 §3.1): Arab/Museum/Werbung/Makler.</summary>
        public bool PhoneReachable(int talker)
        {
            var info = Talkers.Get(talker);
            if (info == null || info.Room < 0 || !RoomRegistry.Has(info.Room)) return false;
            if (CurrentRoom == info.Room) return false;
            var c = _app.Session?.World.Clock;
            if (c == null) return true;
            int h = c.Hour, wd = c.Weekday;
            switch (info.Room)
            {
                case RoomIds.ArabAir: return h >= 11 && wd != 6;
                case RoomIds.Museum: return h >= 11 && wd < 5;
                case RoomIds.Makler: return h < 16;
                case RoomIds.Werbung: return h >= 12 && wd < 5;
            }
            return true;
        }

        /// <summary>Calls a talker: busy tone, or a phone pane with the partner's room and a phone dialog.</summary>
        public bool PhoneCall(int talker, int par1 = -1)
        {
            if (Phone != null || !PhoneReachable(talker))
            {
                _app.Audio.Play("SOUND/busy.raw");
                return false;
            }
            var info = Talkers.Get(talker);
            var pane = RoomRegistry.Create(info.Room);
            if (pane == null) { _app.Audio.Play("SOUND/busy.raw"); return false; }
            _app.Audio.Play(Random.value < 0.5f ? "SOUND/dial1.raw" : "SOUND/dial2.raw");
            var p = _app.Session?.Local;
            // TODO(economy §15.1): the call cost line and per-unit charges belong to the finance domain.
            if (p != null) p.Book(-1, Sim.Finance.MoneyReason.PhoneBase);
            var screen = new PhoneCallScreen(pane, talker, par1);
            Phone = screen;
            _app.Screens.Push(screen);
            return true;
        }
    }

    /// <summary>
    /// A phone call overlay (part 1 §3): the partner's room is drawn as a 340 px slice that slides in
    /// from the right (canvas x 800 → 378, ~333 px/s) with a slanted left edge; the dialog runs on this
    /// overlay; when it ends the pane slides out and the overlay closes.
    /// </summary>
    public sealed class PhoneCallScreen : GameScreen, IDialogStage
    {
        const int Rest = 378, Off = 800, Slice = 340;
        readonly RoomScreen _pane;
        readonly int _talker, _par1;
        float _x = Off;
        int _dir = -1;
        DialogSession _dialog;
        MenuHost _menus;

        public PhoneCallScreen(RoomScreen pane, int talker, int par1) { _pane = pane; _talker = talker; _par1 = par1; }

        public override bool Opaque => false;
        public int CanvasX => OX;
        public MenuHost Menus => _menus;
        public bool IsOwnOffice => false;
        public SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => _pane.PartnerAnimation(talker, medium);

        public override void OnEnter()
        {
            _pane.App = App;
            _pane.IsPhonePane = true;
            _pane.Prepare();
            _menus = new MenuHost(App, () => CanvasX);
            _dialog = new DialogSession(this);
            _dialog.Start(_talker, DialogMedium.Phone, _par1);
        }

        public void OnDialogStopped(int talker) => _dir = 1;

        public override void Update(InputState input, float dt)
        {
            _pane.Animate(dt);
            if (_dir != 0)
            {
                _x += _dir * dt * 333f;
                if (_dir < 0 && _x <= Rest) { _x = Rest; _dir = 0; }
                if (_dir > 0 && _x >= Off)
                {
                    App.Rooms.Phone = null;
                    App.Screens.Remove(this);
                    return;
                }
            }
            App.Advisors.Update(dt, CanvasX, input, false);
            _menus.Animate(dt);
            if (_menus.IsOpen) { _menus.Update(input, dt); return; }
            if (_dialog.IsOpen) _dialog.Update(input, dt);
            else if (input.LeftDown || input.RightDown) input.Consume();
        }

        public override void Draw(Screen2D s)
        {
            int cx = CanvasX;
            int px = cx + Mathf.RoundToInt(_x);
            var info = Talkers.Get(_talker);
            int offset = info?.HandyOffset ?? 160;
            // Slanted left edge: 9 bands of 52 px from y=-24, each band 13 px further left than the one above.
            var bar = App.Ui.Kit.WhiteTrans(UiKit.StdLib, "DIALOG");
            for (int b = 0; b < 9; b++)
            {
                int y0 = -24 + 52 * b, left = px - 13 * b;
                int clipX0 = Mathf.Max(left, cx), clipX1 = Mathf.Min(cx + 640, px + Slice);
                if (clipX1 <= clipX0) continue;
                s.PushClip(new RectInt(clipX0, Mathf.Max(0, y0), clipX1 - clipX0, Mathf.Min(440, y0 + 52) - Mathf.Max(0, y0)));
                _pane.PaneOriginX = px - offset;
                _pane.DrawCanvas(s, false);
                s.PopClip();
                s.PushClip(new RectInt(cx, 0, 640, 440));
                s.Draw(bar, left - 6, y0);
                s.PopClip();
            }
            _dialog.Draw(s);
            _menus.Draw(s, App.InputNow);
            App.Advisors.Draw(s, cx);
        }
    }
}
