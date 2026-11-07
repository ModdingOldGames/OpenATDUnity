using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Systems;
using OpenAT.View.Boot;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;
using HallIds = OpenAT.Sim.Hall.RoomIds;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// The game-day loop and the hall ↔ room link for the local player (rooms-and-ui-9 §5.3, §6.3, §6.4):
    /// <list type="bullet">
    /// <item>09:00 (clock frozen in the morning state): every room is left and the boss office opens for the
    /// briefing; leaving it starts the working day (<see cref="StartWorkingDayCommand"/>).</item>
    /// <item>18:00 / call it a day (<see cref="EveningStarted"/>): rooms are left and the evening screen
    /// (room 8) shows the night passing until the next briefing.</item>
    /// <item>Hall events: <see cref="RoomRequested"/> enters the room, <see cref="HallRequest"/> opens the
    /// matching request menu; the hall's <c>UiBusy</c> / <c>IsRoomBusy</c> read the room stack.</item>
    /// </list>
    /// A small MonoBehaviour next to <see cref="GameApp"/>; it never changes sim state directly, only
    /// through commands.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        /// <summary>Game minutes a figure without a window and without an AI (not the local player, not a computer) stays in a room.</summary>
        public static int ComputerRoomMinutes = 10;

        World _world;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        bool _eveningPending;
        int _briefedDate = -1;
        // Start walk to the boss office on day 0: 0 not started, 1 walking, -1 done or not wanted.
        int _startWalk;
        float _startWalkSince;
        bool _startWaitHeld;

        void ReleaseStartWait(GameApp app)
        {
            if (!_startWaitHeld) return;
            _startWaitHeld = false;
            if (app.Ui.WaitCounter > 0) app.Ui.WaitCounter--;
        }
        int _deferredRoom;
        readonly List<(int player, int date, int time)> _aiInRoom = new List<(int, int, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSystems() => BriefingRecorder.Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.GameFlow");
            DontDestroyOnLoad(go);
            go.AddComponent<GameFlow>();
        }

        static GameApp App => GameApp.Instance;

        /// <summary>The local player calls it a day (Tab, airport exit, office coat).</summary>
        public static void CallItADay(GameApp app)
        {
            var s = app.Session;
            if (s != null) s.World.Execute(new CallItADayCommand(s.World.LocalPlayer));
        }

        /// <summary>Leaves the morning briefing: the working day starts and the player is back in the hall.</summary>
        public static void EndBriefing(GameApp app)
        {
            var s = app.Session;
            if (s == null) return;
            s.World.Execute(new StartWorkingDayCommand());
            if (app.Rooms.CurrentRoom == RoomIds.Aufsicht || app.Rooms.CurrentRoom == RoomIds.Tafel) app.Rooms.LeaveAll();
        }

        // Test hooks for scripted runs: -openat-newgame [mission] starts a game like the wizard does,
        // -openat-timewarp N multiplies the clock speed during the working day (like holding the mouse on
        // the clock), -openat-walkto ROOM [-openat-walkday D] walks the figure to a door at 09:10 of day D
        // (-openat-walkat M: M minutes after 09:00 instead of 10),
        // -openat-debugflow logs the screen stack and the hall state every 120 frames.
        bool _newGameDone;
        static readonly bool _debugFlow = Array.IndexOf(Environment.GetCommandLineArgs(), "-openat-debugflow") >= 0;
        int _timewarp = -1;

        // -openat-scenario FILE: new game set up by a test scenario (Sim/Testing/Scenario.cs, tools/scenarios).
        Sim.Testing.Scenario _scenario;
        int _scenarioStep;

        void ScenarioHook(GameApp app)
        {
            if (_scenarioStep == 0)
            {
                _scenarioStep = -1;
                var arg = CommandLine.Args;
                int i = Array.FindIndex(arg, a => string.Equals(a, "-openat-scenario", StringComparison.OrdinalIgnoreCase));
                if (i < 0 || i + 1 >= arg.Length) return;
                try { _scenario = Sim.Testing.Scenario.Parse(System.IO.File.ReadAllText(arg[i + 1])); }
                catch (Exception e) { Debug.LogError("OpenAT scenario: cannot read " + arg[i + 1] + ": " + e.Message); return; }
                app.StartNewGame(_scenario.Settings(app.Assets.GameData));
                _startWalk = -1;   // a scenario starts where it says, without the day-0 walk
                _scenarioStep = 1;
                return;
            }
            if (_scenarioStep != 1 || app.Session == null) return;
            _scenarioStep = 2;
            var w = app.Session.World;
            _scenario.Apply(w);
            _eveningPending = false;   // evenings that passed during the headless day advance
            if (w.Clock.State != DayState.Morning) _briefedDate = w.Clock.Date;
            foreach (var warn in _scenario.Warnings) Debug.LogWarning("OpenAT scenario: " + warn);
            Debug.Log("OpenAT scenario: applied, day " + w.Clock.Date + " " + w.Clock.ClockText.Trim() + ", money " + w.Local.Money);
            int room = ScenarioRoom(_scenario.Room);
            if (room > 0 && !app.Rooms.Enter(room)) Debug.LogWarning("OpenAT scenario: cannot open room " + _scenario.Room);
        }

        /// <summary>A room id, or a room's screen name ("laptop", "bank").</summary>
        static int ScenarioRoom(string room)
        {
            if (string.IsNullOrEmpty(room)) return 0;
            if (int.TryParse(room, out int id)) return id;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var t in types)
                {
                    // A class may carry several [Room] ids (the telescope rooms): the first one is the room's own id.
                    var ids = t.GetCustomAttributes(typeof(RoomAttribute), false);
                    if (ids.Length == 0) continue;
                    foreach (ScreenNameAttribute n in t.GetCustomAttributes(typeof(ScreenNameAttribute), false))
                        if (string.Equals(n.Name, room, StringComparison.OrdinalIgnoreCase)) return ((RoomAttribute)ids[0]).Id;
                }
            }
            return 0;
        }

        void TestHooks(GameApp app)
        {
            if (app.Assets != null) ScenarioHook(app);
            if (!_newGameDone && app.Assets != null)
            {
                _newGameDone = true;
                var arg = Environment.GetCommandLineArgs();
                for (int i = 0; i < arg.Length; i++)
                {
                    if (!string.Equals(arg[i], "-openat-newgame", StringComparison.OrdinalIgnoreCase)) continue;
                    int mission = Mission.FreeGame;
                    if (i + 1 < arg.Length && int.TryParse(arg[i + 1], out int m)) mission = m;
                    app.StartNewGame(new NewGameSettings { Difficulty = mission, LocalPlayer = 0 });
                }
            }
            if (_timewarp < 0)
            {
                _timewarp = 1;
                var arg = Environment.GetCommandLineArgs();
                for (int i = 0; i < arg.Length - 1; i++)
                    if (string.Equals(arg[i], "-openat-timewarp", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[i + 1], out _timewarp);
            }
            if (_timewarp > 1 && app.Session != null && app.Session.World.Clock.State == DayState.Playing) app.Session.Driver.ClockClickFactor = _timewarp;
            else if (_timewarp > 1 && app.Session != null) app.Session.Driver.ClockClickFactor = 1;
            // -openat-walkto ROOM: once the working day runs, the figure walks to that room's door.
            if (_walkTo < 0)
            {
                _walkTo = 0;
                var arg = Environment.GetCommandLineArgs();
                for (int i = 0; i < arg.Length - 1; i++)
                {
                    if (string.Equals(arg[i], "-openat-walkto", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[i + 1], out _walkTo);
                    if (string.Equals(arg[i], "-openat-walkday", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[i + 1], out _walkDay);
                    if (string.Equals(arg[i], "-openat-walkat", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg[i + 1], out _walkAt);
                }
            }
            var w = app.Session?.World;
            // -openat-evening: call it a day as soon as the working day runs, so the evening screen opens
            // through the real flow (scripted screenshots of room 8).
            if (_evening < 0) _evening = Array.IndexOf(Environment.GetCommandLineArgs(), "-openat-evening") >= 0 ? 1 : 0;
            if (_evening == 1 && w != null && w.Clock.State == DayState.Playing && w.Clock.Time > GameClock.DayStart)
            {
                Debug.Log("OpenAT flow: evening hook: " + w.Execute(new CallItADayCommand(w.LocalPlayer)));
                _evening = 2;
            }
            if (_walkTo > 0 && w?.Hall != null && w.Clock.Date >= _walkDay && w.Clock.State == DayState.Playing && w.Clock.Time > GameClock.DayStart + _walkAt * GameClock.UnitsPerMinute)
            {
                var r = w.Execute(new WalkToRoomCommand(w.LocalPlayer, _walkTo));
                Debug.Log("OpenAT flow: walk to room " + _walkTo + ": " + r);
                _walkTo = 0;
                _timewarp = 1; // walk at normal speed
                app.Session.Driver.ClockClickFactor = 1;
            }
        }
        int _walkTo = -1, _walkDay, _walkAt = 10;
        int _evening = -1;

        void Update()
        {
            var app = App;
            if (app == null) return;
            TestHooks(app);
            if (_debugFlow && Time.frameCount % 120 == 0)
            {
                var sb = new System.Text.StringBuilder("OpenAT flow: screens");
                foreach (var sc in app.Screens.All) sb.Append(' ').Append(sc.GetType().Name);
                sb.Append(" rooms ").Append(string.Join(",", app.Rooms.Locations));
                var hw = app.Session?.World;
                if (hw?.Hall != null)
                {
                    var hp = hw.Hall.Players[hw.LocalPlayer];
                    var me = hw.Hall.PlayerPerson(hw.LocalPlayer);
                    sb.Append(" | ").Append(hw.Clock.ClockText).Append(" room ").Append(hp.Room).Append(" direct ").Append(hp.DirectToRoom)
                      .Append(" ex ").Append(hp.ExRoom).Append(" wait ").Append(hp.WaitForRoom).Append(" thrown ").Append(hp.ThrownOutOfRoom)
                      .Append(" walkToPlayer ").Append(hp.IsWalkingToPlayer).Append(" busy107 ").Append(hw.Hall.RoomBusy[107])
                      .Append(" me ").Append(me);
                }
                Debug.Log(sb.ToString());
            }
            var w = app?.Session?.World;
            if (w != _world) Bind(w);
            if (w == null || app.Assets == null) return;
            if (w.Hall != null && w.Hall != _wiredHall) Wire(w, w.Hall);
            foreach (var sc in app.Screens.All) if (sc is OptionsScreen) return;

            var c = w.Clock;
            bool based = HasAirportBase(app);

            // Evening: leave everything, show the day-end screen.
            if (_eveningPending)
            {
                _eveningPending = false;
                ClosePhone(app);
                if (app.Rooms.CurrentRoom != RoomIds.Abend)
                {
                    app.Rooms.LeaveAll();
                    if (!app.Rooms.Enter(RoomIds.Abend)) Debug.Log("OpenAT: no evening screen");
                }
                return;
            }

            // A room the hall asked for while the tutorial still keeps windows closed (step < 1200).
            if (_deferredRoom != 0 && Sim.Tutorial.TutorialEngine.RoomWindowsAllowed(w))
            {
                int room = _deferredRoom;
                _deferredRoom = 0;
                if (w.Hall == null || w.Hall.Players[w.LocalPlayer].Room == room) app.Rooms.Enter(room);
            }

            // Mission 0: the tutorial script walks the figure to the boss and opens the briefing itself.
            if (c.State == DayState.Morning && c.Frozen && c.Date == 0 && Sim.Tutorial.TutorialEngine.Running(w) && based)
            {
                if (w.Hall != null) PumpHall(app, w);
                return;
            }

            // Morning briefing.
            if (c.State == DayState.Morning && c.Frozen)
            {
                int room = app.Rooms.CurrentRoom;
                if (room == RoomIds.Aufsicht || room == RoomIds.Tafel) { ReleaseStartWait(app); _briefedDate = c.Date; return; }
                if (!based && room != RoomIds.Abend)
                {
                    // A room opened directly (test boot, -openat-screen bank): no briefing, just let time run.
                    if (app.Rooms.Locations.Count > 0)
                    {
                        w.Execute(new StartWorkingDayCommand());
                        // No walk to the office here: the figure stays where the test put it.
                        var me = w.Hall?.PlayerPerson(w.LocalPlayer);
                        if (me != null) w.Execute(new WalkToTileCommand(w.LocalPlayer, HallState.TileOfPosition(me.Position)));
                    }
                    return;
                }
                if (_briefedDate == c.Date && room != RoomIds.Abend) return;
                // First morning of a new game (sim-core §2.1): every figure walks from its start point to the
                // boss office; the briefing opens when the player's figure arrives (the hall's room request).
                if (c.Date == 0 && w.Hall != null && room != RoomIds.Abend && app.Rooms.Locations.Count == 0 && _startWalk >= 0)
                {
                    if (_startWalk == 0)
                    {
                        foreach (var p in w.Players)
                            if (!p.IsOut) w.Execute(new WalkToRoomCommand(p.Index, RoomIds.Aufsicht));
                        _startWalkSince = Time.unscaledTime;
                        _startWalk = 1;
                        // The original shows the WAIT cursor while the figures walk (verified live).
                        if (!_startWaitHeld) { app.Ui.WaitCounter++; _startWaitHeld = true; }
                    }
                    // Safety net: a figure that cannot get there opens the briefing directly after 60 s.
                    // OpenAT (SkipStartWalk): Esc, Space, a right click or the pad's B / Cross / Start skip the walk.
                    var inp = app.InputNow;
                    bool skip = w.Options.SkipStartWalk && Time.unscaledTime - _startWalkSince > 0.5f
                                && (inp.Escape || inp.Space || inp.RightDown || inp.PadActive && inp.LeftDown);
                    if (!skip && Time.unscaledTime - _startWalkSince < 60f) { PumpHall(app, w); return; }
                    _startWalk = -1;
                    ReleaseStartWait(app);
                }
                _briefedDate = c.Date;
                ClosePhone(app);
                app.Rooms.LeaveAll();
                if (!app.Rooms.Enter(RoomIds.Aufsicht)) w.Execute(new StartWorkingDayCommand());
                return;
            }

            if (w.Hall != null) PumpHall(app, w);
        }

        static bool HasAirportBase(GameApp app)
        {
            foreach (var sc in app.Screens.All)
                if (sc is IStatusBarHost && !(sc is RoomScreen)) return true;
            return false;
        }

        static void ClosePhone(GameApp app)
        {
            if (app.Rooms.Phone == null) return;
            app.Screens.Remove(app.Rooms.Phone);
            app.Rooms.Phone = null;
        }

        // ------------------------------------------------------------------ binding

        void Bind(World w)
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _aiInRoom.Clear();
            _eveningPending = false;
            _briefedDate = -1;
            _startWalk = _scenarioStep > 0 ? -1 : 0;   // scenarios skip the day-0 walk
            if (_startWaitHeld && App != null) ReleaseStartWait(App);
            _deferredRoom = 0;
            _world = w;
            if (w == null) { RoomHost.WalkHandler = null; return; }
            BriefingRecorder.Ensure(w);
            _subs.Add(w.Events.Subscribe<EveningStarted>(_ => _eveningPending = true));
            _subs.Add(w.Events.Subscribe<RoomRequested>(OnRoomRequested));
            _subs.Add(w.Events.Subscribe<HallRequest>(OnHallRequest));
            _wiredHall = null;
            RoomHost.WalkHandler = null;
            if (w.Hall != null) Wire(w, w.Hall);
        }

        HallState _wiredHall;

        void Wire(World w, HallState h)
        {
            _wiredHall = h;
            h.UiBusy = p => p == w.LocalPlayer && UiBusy(App);
            h.IsRoomBusy = (room, except) => IsRoomBusy(App, w, room, except);
            RoomHost.WalkHandler = room =>
            {
                var hp = w.Hall?.Players[w.LocalPlayer];
                if (hp == null) return false;
                return w.Execute(new WalkToRoomCommand(w.LocalPlayer, room)).Ok;
            };
        }

        /// <summary>The local player has a menu or dialog open (the hall stops some walk-ins).</summary>
        public static bool UiBusy(GameApp app)
        {
            if (app == null) return false;
            if (app.Rooms.Phone != null) return true;
            var r = app.Rooms.Current;
            if (r != null && (r.IsDialogOpen || (r.Menus != null && r.Menus.IsOpen))) return true;
            foreach (var sc in app.Screens.All)
                if (sc is IStatusBarHost host && !(sc is RoomScreen) && host.Menus != null && host.Menus.IsOpen) return true;
            return false;
        }

        /// <summary>
        /// rooms-and-ui-9 §5.3: another active airline is in the room. The local player's locations come
        /// from the room stack, the others from the hall. The airport and the boss office at 09:00 never count.
        /// </summary>
        public static bool IsRoomBusy(GameApp app, World w, int room, int except)
        {
            if (room == HallIds.Airport || room == 0) return false;
            if (room == HallIds.Aufsicht && w.Clock.Time == GameClock.DayStart) return false;
            for (int p = 0; p < Player.Count; p++)
            {
                if (p == except || w.Players[p].IsOut) continue;
                if (p == w.LocalPlayer && app != null) { foreach (int l in app.Rooms.Locations) if (l == room) return true; }
                else if (w.Hall != null && w.Hall.Players[p].Room == room) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ hall events

        void OnRoomRequested(RoomRequested e)
        {
            var app = App;
            var w = _world;
            if (app == null || w == null) return;
            if (e.Player != w.LocalPlayer)
            {
                // Computer players: the Sim AI runs the room action, keeps the figure inside for its work
                // countdown and sends it back out (ai-robots.md §3.2, §5.1; rooms-and-ui-9 §6.1 "Robot").
                // Only a non-computer figure without a window (no such owner in single player) uses the fallback.
                if (!w.Players[e.Player].IsComputer)
                    _aiInRoom.Add((e.Player, w.Clock.Date, w.Clock.Time + ComputerRoomMinutes * GameClock.UnitsPerMinute));
                return;
            }
            Debug.Log("OpenAT flow: hall requests room " + e.Room);
            if (!Sim.Tutorial.TutorialEngine.RoomWindowsAllowed(w)) { _deferredRoom = e.Room; return; }   // tutorial: window later
            if (!app.Rooms.Enter(e.Room))
            {
                Debug.Log("OpenAT: room " + e.Room + " requested by the hall is not available");
                w.Execute(new ReturnToHallCommand(e.Player));
            }
        }

        void OnHallRequest(HallRequest e)
        {
            var app = App;
            var w = _world;
            if (app == null || w == null || e.Player != w.LocalPlayer) return;
            if (e.Kind == HallRequest.StayedTooLong)
            {
                // Stayed too long (rooms-and-ui-9 §5.5): the room and every location above it close, so a laptop or
                // globe opened in the room goes too; open menus and dialogs go with their windows.
                Debug.Log("OpenAT flow: stayed too long in room " + e.Room);
                app.Rooms.LeaveFrom(e.Room);
                return;
            }
            var host = TopHost(app);
            var menus = host?.Menus;
            if (menus == null || menus.IsOpen) return;
            var from = new Vector2Int(320, 220);
            var hp = w.Hall?.Players[e.Player];
            switch (e.Kind)
            {
                case HallRequest.CallItADay:
                    if (w.Clock.State == DayState.Playing)
                        menus.Open(RequestMenu.Create(RequestMenu.CallItADay, null, () => CallItADay(app)), from, 0.17f);
                    break;
                case HallRequest.Phone:
                    menus.Open(new FilofaxMenu(2), from, 0.17f);
                    break;
                case HallRequest.FlightMonitor:
                    menus.Open(new OpenAT.View.Airport.FluegeMenu(), from, 0.175f);
                    break;
                case HallRequest.ColaMachine:
                {
                    // Glove: the drink comes out (vending sound); without: the shock (fuse sound). Both at the effects
                    // volume times the global ambient volume, like the hall's other sounds (airport-and-people §10.1).
                    var cmd = new OpenAT.Sim.Items.UseVendingMachineCommand(e.Player);
                    if (!w.Execute(cmd).Ok) break;
                    app.Audio.Play(cmd.GotRedBull ? "SOUND/vending.raw" : "SOUND/fused.raw", app.Audio.GlobalAmbient);
                    break;
                }
                case HallRequest.WrongRoom:
                case HallRequest.WrongRoomOut:
                {
                    int owner = e.Room / 10 - 1;
                    var args = new object[] { owner >= 0 && owner < Player.Count ? w.Players[owner].Airline : "" };
                    int own = hp != null ? hp.ThrownOutOfRoom : RoomIds.OfficeOf(e.Player);
                    menus.Open(RequestMenu.Create(e.Kind == HallRequest.WrongRoom ? RequestMenu.WrongRoom : RequestMenu.WrongRoom2, args,
                        () => w.Execute(new WalkToRoomCommand(e.Player, own))), from, 0.17f);
                    break;
                }
                case HallRequest.ThrownOut:
                case HallRequest.ThrownOutBoard:
                {
                    var args = new object[] { w.Players[e.Player].Airline };
                    int room = e.Room;
                    menus.Open(RequestMenu.Create(e.Kind == HallRequest.ThrownOut ? RequestMenu.ThrownOut : RequestMenu.ThrownOut2, args,
                        () => w.Execute(new WaitForRoomCommand(e.Player, room)), () => w.Execute(new WaitForRoomCommand(e.Player, 0))), from, 0.17f);
                    break;
                }
                case HallRequest.Forbidden:
                    menus.Open(RequestMenu.Create(RequestMenu.Forbidden, null, null), from, 0.17f);
                    break;
                case HallRequest.Closed:
                    menus.Open(new ClosedMenu(e.Arg), from, 0.17f);
                    break;
                case HallRequest.Toilet:
                    // TODO(rooms-and-ui-2 §4.19 MENU_WC_F/M): the input-blocking toilet pause and its sounds.
                    break;
                case HallRequest.TalkToPlayer:
                    // Face to face with a computer airline (rooms-and-ui-3 §8): we walked up to it, so it speaks first (par2 0).
                    if (e.Room >= 0 && e.Room < Player.Count && w.Players[e.Room].IsComputer && !Rivals.RivalCallScreen.IsOpen(app))
                        app.Screens.Push(new Rivals.RivalCallScreen(e.Room, callerLeads: false, inPerson: true) { TopicPending = false });
                    else w.Execute(new EndTalkCommand(e.Player));
                    break;
            }
        }

        static IStatusBarHost TopHost(GameApp app)
        {
            if (app.Rooms.Current != null) return app.Rooms.Current;
            var all = app.Screens.All;
            for (int i = all.Count - 1; i >= 0; i--) if (all[i] is IStatusBarHost h) return h;
            return null;
        }

        void PumpHall(GameApp app, World w)
        {
            // Computer figures come back out of their rooms.
            for (int i = _aiInRoom.Count - 1; i >= 0; i--)
            {
                var a = _aiInRoom[i];
                if (w.Clock.Date > a.date || w.Clock.Time >= a.time || w.Clock.State != DayState.Playing)
                {
                    w.Execute(new ReturnToHallCommand(a.player));
                    _aiInRoom.RemoveAt(i);
                }
            }
            // "Wait for the room" (rooms-and-ui-9 §5.3 step 6).
            var hp = w.Hall.Players[w.LocalPlayer];
            if (hp.WaitForRoom != 0 && hp.InHall && w.Clock.State == DayState.Playing && !IsRoomBusy(app, w, hp.WaitForRoom, w.LocalPlayer))
            {
                int room = hp.WaitForRoom;
                w.Execute(new WaitForRoomCommand(w.LocalPlayer, 0));
                w.Execute(new WalkToRoomCommand(w.LocalPlayer, room));
            }
        }
    }
}
