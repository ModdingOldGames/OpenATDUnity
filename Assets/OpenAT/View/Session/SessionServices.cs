using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Core;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Save;
using OpenAT.View.Boot;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Rivals;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Session
{
    /// <summary>
    /// Per-game services of the view that react to sim events, re-bound whenever <see cref="GameApp.Session"/>
    /// changes (new game, loaded game):
    /// <list type="bullet">
    /// <item>Autosave (sim-core.md §5.1, rooms-and-ui-4 §4.3): <see cref="AutosaveRequested"/> sets a flag and ends the
    /// driver batch after that tick (<see cref="SimDriver.EndBatch"/>);
    /// the file is written right after it (this runs after GameApp), into slot 11 named <c>Misc</c> 5000, when the
    /// autosave option is on. After the 17:00 save the assistant says <c>Advi</c> 500.</item>
    /// <item>Campaign progress: a <see cref="MissionWon"/> of the local player unlocks the next mission.</item>
    /// <item>Rival calls: a <see cref="RobotPhoneCall"/> to the local player rings the phone and opens the
    /// competitor dialog over the current room when the player is reachable (in a room, no dialog/menu
    /// open; ai-robots.md §5.1); otherwise the call is dropped.</item>
    /// </list>
    /// Test hooks: <c>-openat-load N</c> loads slot N at start (like the original's <c>/load</c>),
    /// <c>-openat-savedir DIR</c> uses another save folder, <c>-openat-rivalcall P</c> lets airline P call
    /// once the local player is in a room during the day, <c>-openat-unlockall</c> unlocks every mission.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after GameApp: the autosave is written in the frame of the tick that asked for it
    public sealed class SessionServices : MonoBehaviour
    {
        World _world;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        AutosaveReason? _autosave;
        int _pendingCall = -1;
        bool _bootDone;
        int _testCall = -2;
        static readonly bool _camDebug = Flag("-openat-camdebug");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.SessionServices");
            DontDestroyOnLoad(go);
            go.AddComponent<SessionServices>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }

        static bool Flag(string name) => Array.FindIndex(Environment.GetCommandLineArgs(), a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) >= 0;

        void Update()
        {
            var app = GameApp.Instance;
            if (app == null) return;
            if (!_bootDone && app.Assets != null) Boot(app);
            var w = app.Session?.World;
            if (w != _world) Bind(w);
            if (w == null) return;

            if (_autosave.HasValue)
            {
                var reason = _autosave.Value;
                _autosave = null;
                WriteAutosave(app, reason);
            }
            if (SaveSlots.PendingCamera.HasValue)
                foreach (var s in app.Screens.All)
                    if (s is Airport.AirportScreen hall && s != SaveSlots.ReplacedHall)
                    {
                        hall.RestoreCamera(SaveSlots.PendingCamera.Value.x, SaveSlots.PendingCamera.Value.y);
                        Debug.Log("OpenAT load: airport camera restored to " + hall.ViewX + "," + hall.ViewY);
                        SaveSlots.PendingCamera = null;
                        break;
                    }
            if (_camDebug && Time.frameCount % 30 == 0)
                foreach (var s in app.Screens.All) if (s is Airport.AirportScreen h) Debug.Log("OpenAT camdebug f" + Time.frameCount + " " + w.Clock.ClockText + " view " + h.ViewX + "," + h.ViewY + " me " + w.Hall?.PlayerPerson(w.LocalPlayer)?.ScreenPos.X);
            TestCall(app, w);
            if (_pendingCall >= 0)
            {
                int caller = _pendingCall;
                _pendingCall = -1;
                if (CanTakeCall(app, w)) Ring(app, w, caller);
                else Debug.Log("OpenAT rival call from airline " + caller + " dropped: player not reachable");
            }
        }

        void Boot(GameApp app)
        {
            _bootDone = true;
            if (Flag("-openat-unlockall")) MissionProgressStore.UnlockAll();
            var load = Arg("-openat-load");
            if (load != null && int.TryParse(load, out int slot) && !SaveSlots.Load(app, slot)) Debug.LogError("OpenAT: -openat-load " + slot + " failed");
        }

        void Bind(World w)
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _autosave = null;
            _pendingCall = -1;
            _world = w;
            if (w == null) return;
            _subs.Add(w.Events.Subscribe<AutosaveRequested>(e =>
            {
                _autosave = e.Reason;
                var d = GameApp.Instance?.Session?.Driver;
                if (d != null) d.EndBatch = true;   // no further ticks before the file is written
            }));
            _subs.Add(w.Events.Subscribe<MissionWon>(e => { if (e.Player != null && e.Player.Index == w.LocalPlayer) MissionProgressStore.RecordWin(w.Difficulty); }));
            _subs.Add(w.Events.Subscribe<RobotPhoneCall>(e => { if (e.Human != null && e.Human.Index == w.LocalPlayer && e.Robot != null) _pendingCall = e.Robot.Index; }));
        }

        // ------------------------------------------------------------------ autosave

        static void WriteAutosave(GameApp app, AutosaveReason reason)
        {
            if (!OptionsScreen.Get("Autosave")) return;
            string name = app.Assets.StdText.GetOr("Misc", 5000);
            if (!SaveSlots.Save(app, SaveSlots.Autosave, name)) return;
            if (reason == AutosaveReason.FiveOClock)
            {
                var text = app.Assets.StdText.Get("Advi", 500);
                if (text != null) app.Advisors.Queue(AdvisorKind.Assistant, text, AdvisorUrgency.Urgent);
            }
        }

        // ------------------------------------------------------------------ rival calls

        /// <summary>
        /// The view's part of "okay to call this player" (rooms-and-ui-3 §6.3): a room window is open, no
        /// dialog, menu, phone call or options, the day is running. The sim checked phone item, time, stuck.
        /// </summary>
        static bool CanTakeCall(GameApp app, World w)
        {
            if (w.Clock.State != DayState.Playing || w.CallItADay || w.Local.CalledItADay) return false;
            var room = app.Rooms.Current;
            if (room == null || app.Screens.Top != room) return false;
            if (GameFlow.UiBusy(app) || app.Rooms.Phone != null || RivalCallScreen.IsOpen(app)) return false;
            return true;
        }

        static void Ring(GameApp app, World w, int caller)
        {
            Debug.Log("OpenAT rival call: airline " + caller + " (" + w.Players[caller].Airline + ") calls");
            app.Audio.Play("SOUND/phone.raw");
            w.Local.GameSpeed = 0;
            if (app.Session != null) app.Session.Driver.Warp = false;
            app.Screens.Push(new RivalCallScreen(caller));
        }

        void TestCall(GameApp app, World w)
        {
            if (_testCall == -2)
            {
                _testCall = -1;
                var a = Arg("-openat-rivalcall");
                if (a != null && int.TryParse(a, out int p) && p >= 0 && p < Player.Count) _testCall = p;
            }
            if (_testCall < 0 || _testCall == w.LocalPlayer) return;
            if (!CanTakeCall(app, w) || w.Clock.Time < GameClock.DayStart + 20 * GameClock.UnitsPerMinute) return;
            int caller = _testCall;
            _testCall = -1;
            Ring(app, w, caller);
        }
    }
}
