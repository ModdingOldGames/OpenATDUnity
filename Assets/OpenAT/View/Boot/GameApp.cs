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
            Ui.Fade.Enabled = PlayerPrefs.GetInt("OpenAT.Fade", 1) != 0;
            Advisors = new AdvisorLayer(this);
            Rooms = new RoomHost(this);
            Auto = AutoRunner.FromCommandLine(this);

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

        void Update()
        {
            if (LostToScriptReload()) return;
            var input = InjectedInput ?? Input;
            if (InjectedInput == null) Input.Capture(Screen.MousePosition);
            Auto?.Tick(Time.unscaledDeltaTime);
            Ui.BeginFrame(input);
            if (!Ui.Paused) Session?.Update(Time.unscaledDeltaTime);
            Screens.Update(input, Time.deltaTime);
            Screens.ApplyPending();
            if (InjectedInput != null) InjectedInput.ResetEdges();
        }

        void LateUpdate()
        {
            if (LostToScriptReload()) return;
            Screen.BeginFrame(Color.black);
            Screens.Draw(Screen);
            Ui.Draw(Screen, InputNow, Time.unscaledDeltaTime);
            Screen.EndFrame();
            Auto?.AfterFrame();
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

    static class CommandLine
    {
        public static string Value(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
