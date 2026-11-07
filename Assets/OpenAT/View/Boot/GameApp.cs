using System;
using System.IO;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.Screens;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Entry point, created automatically after the first scene loads (no scene setup needed).
    /// Locates the original install (command line, env var, remembered choice, known paths), loads
    /// assets and runs the screen stack: update with input, then draw into the virtual framebuffer.
    /// Also owns the frame-level UI (cursor, tooltip, fades, pause), the advisor layer and the room host.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const string PrefInstallDir = "OpenAT.InstallDir";

        public static GameApp Instance { get; private set; }

        public Screen2D Screen { get; private set; }
        public GameAssets Assets { get; private set; }
        public ScreenStack Screens { get; private set; }
        public GameAudio Audio { get; private set; }
        public readonly InputState Input = new InputState();
        public AutoRunner Auto { get; private set; }
        public MusicPlayer Music { get; private set; }
        /// <summary>Cursor, tooltips, crossfade, pause.</summary>
        public GameUi Ui { get; private set; }
        /// <summary>Advisor messages and the player's dialog portrait.</summary>
        public AdvisorLayer Advisors { get; private set; }
        /// <summary>The local player's room stack (enter/leave rooms, phone calls).</summary>
        public RoomHost Rooms { get; private set; }

        /// <summary>The running game, or null in menus.</summary>
        public GameSession Session { get; set; }

        /// <summary>Simulated input injected by the auto runner (tests); null = real devices.</summary>
        internal InputState InjectedInput;

        /// <summary>The input snapshot of this frame (real or injected).</summary>
        public InputState InputNow => InjectedInput ?? Input;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("OpenAT");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            // Scripted runs (-openat-auto) keep running when the window loses focus; a normal game pauses
            // like the original (no catch-up after an inactive period).
            if (!string.IsNullOrEmpty(CommandLine.Value("-openat-auto"))) Application.runInBackground = true;
            Screen = gameObject.AddComponent<Screen2D>();
            Screen.Widescreen = PlayerPrefs.GetInt("OpenAT.Widescreen", 1) != 0;
            Audio = gameObject.AddComponent<GameAudio>();
            Screens = new ScreenStack(this);
            Ui = new GameUi(this);
            // Gamepad Y / X (OpenAT): the planner's and the own office's walk-to hotkeys.
            Ui.Pad.PlannerKey = () => Assets == null ? '\0' : RoomHotkeys.KeyFor(RoomIds.Globe, RoomHotkeys.LayoutOf(Assets.Language));
            Ui.Pad.OfficeKey = () => Assets == null ? '\0' : RoomHotkeys.KeyFor(RoomHotkeys.OwnOffice, RoomHotkeys.LayoutOf(Assets.Language));
            Ui.Fade.Enabled = PlayerPrefs.GetInt("OpenAT.Fade", 1) != 0;
            Advisors = new AdvisorLayer(this);
            Rooms = new RoomHost(this);
            Auto = AutoRunner.FromCommandLine(this);
            StartPadTools();

            var install = LocateInstall();
            if (install != null) StartWithInstall(install);
            else Screens.Set(new InstallPromptScreen());
            Screens.ApplyPending();
        }

        GameInstall LocateInstall()
        {
            string fromArgs = CommandLine.Value("-openat-game");
            string remembered = PlayerPrefs.GetString(PrefInstallDir, null);
            // A portable copy: the original game's files in a "Game" folder next to the executable.
            string portable = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Game");
            if (string.IsNullOrEmpty(fromArgs) && GameInstall.IsValid(portable)) fromArgs = portable;
            return GameInstall.TryLocate(fromArgs) ?? GameInstall.TryLocate(remembered);
        }

        public bool TryUseInstall(string dir)
        {
            if (!GameInstall.IsValid(dir)) return false;
            PlayerPrefs.SetString(PrefInstallDir, dir);
            PlayerPrefs.Save();
            StartWithInstall(GameInstall.Open(dir));
            return true;
        }

        void StartWithInstall(GameInstall install)
        {
            Assets = new GameAssets(install);
            Audio.Assets = Assets;
            Ui.Init(Assets);
            if (Music == null) Music = MusicPlayer.Create(transform, Assets);
            string start = CommandLine.Value("-openat-screen");
            GameScreen first = null;
            if (start != null && int.TryParse(start, out int roomId) && RoomRegistry.Has(roomId)) first = RoomRegistry.Create(roomId);
            Screens.Set(first ?? ScreenFactory.Create(start) ?? new TitleScreen());
        }

        // ------------------------------------------------------------------ game session helpers

        /// <summary>
        /// Makes sure a game is running (rooms opened directly, e.g. <c>-openat-screen bank</c>, need
        /// one for the status bar): starts a default free game.
        /// </summary>
        public GameSession EnsureSession()
        {
            if (Session != null || Assets == null) return Session;
            try
            {
                Session = GameSession.NewGame(Assets, new NewGameSettings { LocalPlayer = 0, Seed = 12345 });
                AfterNewGame();
            }
            catch (Exception e)
            {
                Debug.LogError("OpenAT: could not start a game: " + e);
            }
            return Session;
        }

        void AfterNewGame()
        {
            // Without the boss office the morning briefing cannot run: start the working day directly.
            if (!RoomRegistry.Has(RoomIds.Aufsicht)) Session.World.StartWorkingDay();
        }

        /// <summary>
        /// Starts a new game from the wizard and shows the airport: the "hall" screen if one exists,
        /// else the placeholder lobby with the player's office opened on top.
        /// </summary>
        public void StartNewGame(NewGameSettings settings)
        {
            Rooms.Reset();
            Session = GameSession.NewGame(Assets, settings);
            AfterNewGame();
            Music?.Stop();
            var hall = ScreenFactory.Create("hall");
            if (hall != null) { Screens.Set(hall); return; }
            Screens.Set(new LobbyScreen());
            Rooms.Enter(RoomIds.OfficeOf(settings.LocalPlayer));
        }

        /// <summary>Opens the in-game options (Esc/F2, F3 load, F4 save; rooms-and-ui-4 §4.1).</summary>
        public void OpenOptions(OptionsPage page = OptionsPage.Start)
        {
            foreach (var s in Screens.All) if (s is OptionsScreen) return;
            Screens.Push(new OptionsScreen(Session != null, page));
        }

        // ------------------------------------------------------------------ frame

        /// <summary>Gamepad play (<see cref="GameOptions.GamepadSupport"/>); outside a game (menus) the stored choice.</summary>
        bool GamepadEnabled => Session?.World?.Options.GamepadSupport ?? Extras.Get(nameof(GameOptions.GamepadSupport));

        /// <summary>
        /// True when Unity recompiled the scripts during Play mode: the editor reloads the code but not this object's
        /// non-serialised state (screens, UI, session), so nothing can run. Play mode is stopped once with a message.
        /// </summary>
        bool LostToScriptReload()
        {
            if (Ui != null && Screens != null && Screen != null) return false;
            if (_reloadReported) return true;
            _reloadReported = true;
            Debug.LogWarning("OpenAT: the scripts were recompiled during Play mode and the running game did not survive it. "
                + "Start Play mode again (Preferences > General > Script Changes While Playing: Recompile After Finished Playing avoids this).");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            return true;
        }
        bool _reloadReported;

        // ------------------------------------------------------------------ virtual pad (recording / test tool)

        VirtualPad _virtualPad;
        bool _overlayFlag;
        string _padShot;

        /// <summary>Starts the virtual DualShock 4 and the controller overlay when requested (docs/VIRTUALPAD.md).</summary>
        void StartPadTools()
        {
            int port = VirtualPad.RequestedPort();
            if (port > 0) _virtualPad = new VirtualPad(port) { Shot = path => _padShot = path, GameState = PadGameState, Targets = PadTargetList, Record = RecordCommand, Music = MusicCommand };
            _overlayFlag = CommandLine.Has("-openat-padoverlay");
        }

        bool OverlayShown => (_virtualPad != null || _overlayFlag) && VirtualPad.OverlayOn;

        /// <summary>For the virtual pad's <c>state</c>: canvas cursor, whether the pad drives, the focused target, the top screen.</summary>
        string PadGameState()
        {
            var m = InputNow.Mouse;
            int i = Ui.Targets.At(m);
            string focus = i < 0 ? "none" : Ui.Targets.Rects[i].x + "," + Ui.Targets.Rects[i].y + "," + Ui.Targets.Rects[i].width + "x" + Ui.Targets.Rects[i].height;
            var top = Screens.Top;
            string screen = top is Rooms.Framework.RoomScreen rs ? "room" + rs.RoomId : top?.GetType().Name ?? "none";
            var home = Ui.Targets.Home;
            return "cursor=" + m.x + "," + m.y + " pad=" + (Ui.Pad.Active ? "yes" : "no") + " focus=" + focus + " targets=" + Ui.Targets.Rects.Count
                + " home=" + (home.HasValue ? home.Value.x + "," + home.Value.y : "centre") + " screen=" + screen
                + " fasttravel=" + Ui.FastTravel.StateText;
        }

        /// <summary>The game's own video and sound recorder (OpenAT dev tool, <see cref="GameRecorder"/>).</summary>
        public readonly GameRecorder Recorder = new GameRecorder();

        string RecordCommand(string args)
        {
            var p = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return "error record start <file.mp4> [fps] | stop | status";
            switch (p[0])
            {
                case "start":
                    {
                        if (p.Length < 2) return "error record start <file.mp4> [fps]";
                        string rest = p[1].Trim(); int fps = 30;
                        int sp = rest.LastIndexOf(' ');
                        if (sp > 0 && int.TryParse(rest.Substring(sp + 1), out int f)) { fps = f; rest = rest.Substring(0, sp).Trim(); }
                        return Recorder.Start(Screen.Target, rest.Trim('"'), fps);
                    }
                case "stop": return Recorder.Stop();
                case "status": return Recorder.Status;
            }
            return "error record start <file.mp4> [fps] | stop | status";
        }

        /// <summary>Music on/off like Options > Sound (stored), for recordings without music.</summary>
        string MusicCommand(string arg)
        {
            if (arg != "on" && arg != "off") return "music=" + (PlayerPrefs.GetInt(OptionsScreen.Prefix + "Midi", 1) != 0 ? "on" : "off");
            PlayerPrefs.SetInt(OptionsScreen.Prefix + "Midi", arg == "on" ? 1 : 0);
            PlayerPrefs.Save();
            if (arg == "off") Music?.Stop();
            return "ok music=" + arg + (arg == "on" ? " (plays from the next piece)" : "");
        }

        string PadTargetList()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in Ui.Targets.Rects) sb.Append(r.x).Append(',').Append(r.y).Append(',').Append(r.width).Append('x').Append(r.height).Append(' ');
            return sb.ToString().TrimEnd();
        }

        string PadLabel(UnityEngine.InputSystem.Gamepad g) =>
            g == null ? "" : _virtualPad != null && g == _virtualPad.Device ? g.name : g.displayName;

        void OnDestroy()
        {
            _virtualPad?.Dispose();
            _virtualPad = null;
        }

        void Update()
        {
            if (LostToScriptReload()) return;
            _virtualPad?.Pump();
            var input = InjectedInput ?? Input;
            if (InjectedInput == null) Input.Capture(Screen.MousePosition);
            // The gamepad (OpenAT) uses the cursor look and targets of the previous frame.
            Ui.Pad.Apply(input, Ui.Targets, Screen.Width, Screen.Height, Ui.Cursor != CursorLook.Normal, GamepadEnabled, Time.unscaledDeltaTime);
            Auto?.Tick(Time.unscaledDeltaTime);
            Ui.BeginFrame(input);
            Ui.Targets.ScreenKey = Screens.Top;   // gamepad: a new screen sends the cursor to its home (OpenAT)
            if (!Ui.Paused) Session?.Update(Time.unscaledDeltaTime);
            // Gamepad fast travel (OpenAT): Share opens the menu; while it is open it takes the input.
            if (input.PadFastTravel && !Ui.FastTravel.IsOpen) Ui.FastTravel.TryOpen();
            else if (Ui.FastTravel.IsOpen) Ui.FastTravel.Update(input);
            if (Ui.FastTravel.IsOpen) input.Consume();
            Screens.Update(input, Time.deltaTime);
            Screens.ApplyPending();
            Ui.Pad.EndFrame(input);
            if (InjectedInput != null) InjectedInput.ResetEdges();
        }

        void LateUpdate()
        {
            if (LostToScriptReload()) return;
            Screen.BeginFrame(Color.black);
            Screens.Draw(Screen);
            Ui.Draw(Screen, InputNow, Time.unscaledDeltaTime);
            if (OverlayShown)
                PadOverlay.Draw(Screen, Ui.Pad.Active, VirtualPad.OverlayCorner, VirtualPad.OverlayScale,
                    PadLabel(UnityEngine.InputSystem.Gamepad.current));
            Screen.EndFrame();
            Auto?.AfterFrame();
            Recorder.Capture(Screen.Target);
            if (_padShot != null) { SaveFramebuffer(_padShot); Debug.Log("OpenAT virtual pad: saved " + _padShot); _padShot = null; }
        }

        /// <summary>Writes the current framebuffer to a PNG file (used by tests and screenshots).</summary>
        public void SaveFramebuffer(string path)
        {
            var rt = Screen.Target;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }
    }

    /// <summary>
    /// The start arguments: the process's command line, plus in the editor the arguments stored by the editor bridge
    /// (EditorPrefs <see cref="EditorArgsPref"/>, tools/virtualpad: <c>padctl.py editor args ...</c>), so a Play session can start
    /// like the built player with <c>-openat-scenario</c>, <c>-openat-screen</c> and so on.
    /// </summary>
    public static class CommandLine
    {
        /// <summary>EditorPrefs key of the editor bridge's start arguments; per project, since EditorPrefs are machine-wide.</summary>
        public static string EditorArgsPref => "OpenAT.EditorArgs:" + Application.dataPath;

        public static string[] Args
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
                var extra = UnityEditor.EditorPrefs.GetString(EditorArgsPref, "");
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    var l = new System.Collections.Generic.List<string>(args);
                    l.AddRange(extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    args = l.ToArray();
                }
#endif
                return args;
            }
        }

        public static bool Has(string name) => Array.Exists(Args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        public static string Value(string name)
        {
            var args = Args;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
