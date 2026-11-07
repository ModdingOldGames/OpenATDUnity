using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>Cursor looks (rooms-and-ui-1 §6.1).</summary>
    public enum CursorLook { Normal = 0, Hot = 1, Exit = 3, Left = 4, Right = 5, MoveH = 6, MoveV = 7, Wait = 100, None = 101, WalkLeft = 102, WalkRight = 103 }

    /// <summary>
    /// Frame-level UI overlay owned by <see cref="GameApp"/>: software cursor, text tooltip box,
    /// room crossfade and pause picture (rooms-and-ui-1 §5.1, §6.1, §11.2, §11.3). Screens set the
    /// cursor look and tooltip every frame during their update; <see cref="BeginFrame"/> resets them
    /// ("last writer wins", like the original's per-frame hotspot pass). Drawn after all screens.
    /// </summary>
    public sealed class GameUi
    {
        readonly GameApp _app;
        public UiKit Kit { get; private set; }

        public CursorLook Cursor;
        /// <summary>While &gt; 0 the sand-glass cursor is shown.</summary>
        public int WaitCounter;
        /// <summary>Set false to use the Windows cursor instead of the original bitmaps.</summary>
        public bool SoftwareCursor = true;

        public readonly Tooltip Tooltip = new Tooltip();
        /// <summary>Gamepad play (OpenAT): the pad and the clickable targets the screens report each frame.</summary>
        public readonly GamepadInput Pad = new GamepadInput();
        public readonly PadTargets Targets = new PadTargets();
        /// <summary>The gamepad's fast-travel menu (OpenAT).</summary>
        public readonly FastTravelMenu FastTravel;
        public readonly Crossfade Fade = new Crossfade();

        /// <summary>Pause (Pause key or app deactivation); a left click unpauses (§11.3).</summary>
        public bool Paused { get; private set; }
        float _pauseBlend;

        public GameUi(GameApp app) { _app = app; FastTravel = new FastTravelMenu(app); }

        public void Init(GameAssets assets)
        {
            Kit = new UiKit(assets);
            UnityEngine.Cursor.visible = !SoftwareCursor;
        }

        public void SetPaused(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            if (_app.Session != null) _app.Session.World.Paused = on;
        }

        /// <summary>Resets per-frame state. Called before screens update.</summary>
        public void BeginFrame(InputState input)
        {
            Cursor = CursorLook.Normal;
            Tooltip.BeginFrame();
            Targets.Begin(_app.Screen.Width, _app.Screen.Height);
            if (input.LeftDown)
            {
                Tooltip.HideNow();
                if (Paused) { SetPaused(false); input.Consume(); }
            }
            if (input.F1) Tooltip.ShowNow();
            if (input.PauseKey && _app.Session != null) SetPaused(!Paused);
        }

        public void Draw(Screen2D s, InputState input, float dt)
        {
            if (Kit == null) return;
            // Pause picture blends in over ~8 frames (§11.3).
            _pauseBlend = Mathf.MoveTowards(_pauseBlend, Paused ? 1 : 0, dt * 2.5f);
            if (_pauseBlend > 0)
            {
                var pic = Kit.Assets.Texture("room/pause.gli", "PAUSE");
                s.FillRect(new RectInt(0, 0, s.Width, s.Height), new Color32(0, 0, 0, (byte)(_pauseBlend * 160)));
                s.Draw(pic, (s.Width - pic.width) / 2, 0, new Color32(255, 255, 255, (byte)(_pauseBlend * 255)));
            }
            if (input.PadActive && !Paused && !FastTravel.IsOpen) DrawPadFocus(s, input.Mouse);
            FastTravel.Draw(s, Kit);
            if (!FastTravel.IsOpen) Tooltip.Draw(s, Kit, input.Mouse);
            Fade.Draw(s, dt);
            DrawCursor(s, input.Mouse);
        }

        /// <summary>Gamepad focus (OpenAT): a thin frame around the target under the pad cursor, the pad's hover.</summary>
        void DrawPadFocus(Screen2D s, Vector2Int m)
        {
            int i = Targets.At(m);
            if (i < 0) return;
            var r = Targets.Rects[i];
            var c = new Color32(255, 230, 120, 200);
            s.FillRect(new RectInt(r.x - 1, r.y - 1, r.width + 2, 1), c);
            s.FillRect(new RectInt(r.x - 1, r.yMax, r.width + 2, 1), c);
            s.FillRect(new RectInt(r.x - 1, r.y, 1, r.height), c);
            s.FillRect(new RectInt(r.xMax, r.y, 1, r.height), c);
        }

        void DrawCursor(Screen2D s, Vector2Int m)
        {
            if (!SoftwareCursor || m.x < 0) return;
            var look = Paused ? CursorLook.Normal : WaitCounter > 0 ? CursorLook.Wait : Cursor;
            string name; int ox = 0, oy = 0; string lib = UiKit.BasisLib;
            int ticker = (int)(Time.unscaledTime * 1000) / 32;
            switch (look)
            {
                case CursorLook.None: return;
                case CursorLook.Hot: name = "CURSORH"; break;
                case CursorLook.Exit: name = "EXIT" + (1 + (ticker / 2) % 12).ToString("00"); ox = 15; oy = 12; break;
                case CursorLook.Left: name = "CURSORL"; ox = 2; oy = 16; break;
                case CursorLook.Right: name = "CURSORR"; ox = 29; oy = 16; break;
                case CursorLook.MoveH: name = "CURSORV"; ox = 16; break;
                case CursorLook.MoveV: name = "CURSORW"; oy = 16; break;
                case CursorLook.Wait: name = "CURSORS"; ox = 16; oy = 16; break;
                case CursorLook.WalkLeft: lib = UiKit.StdLib; name = "CWALKL" + (ticker % 9 + 1).ToString("00"); ox = 24; oy = 13; break;
                case CursorLook.WalkRight: lib = UiKit.StdLib; name = "CWALKR" + (ticker % 9 + 1).ToString("00"); ox = 24; oy = 13; break;
                default: name = "CURSOR"; break;
            }
            s.Draw(Kit.Assets.Texture(lib, name), m.x - ox, m.y - oy);
        }
    }

    /// <summary>
    /// Hover text box (rooms-and-ui-1 §5.1): appears after 600 ms on the same id, disappears after
    /// 5 s; F1 shows it at once; a left click hides it. Id &gt; 0 = std_ger.res "Tool" id.
    /// </summary>
    public sealed class Tooltip
    {
        int _id, _lastId;
        string _text, _lastText;
        float _since;
        bool _forced;

        public void BeginFrame()
        {
            _lastId = _id; _lastText = _text;
            _id = 0; _text = null;
        }

        /// <summary>Tooltip text id from std_ger.res group "Tool".</summary>
        public void Set(int toolId) { _id = toolId; _text = null; }

        /// <summary>Literal tooltip text (the original's negative ids).</summary>
        public void Set(string text) { _id = text == null ? 0 : -1; _text = text; }

        public void HideNow() { _since = -8f; _forced = false; }
        public void ShowNow() { _forced = true; _since = 0.6f; }

        public void Draw(Screen2D s, UiKit kit, Vector2Int mouse)
        {
            bool same = _id == _lastId && _text == _lastText;
            if (!same) { _since = 0; _forced = false; }
            else _since += Time.unscaledDeltaTime;
            if (_id == 0) return;
            if (!_forced && (_since < 0.6f || _since > 5f)) return;
            string text = _id > 0 ? kit.Assets.StdText.Get("Tool", _id) : _text;
            if (string.IsNullOrEmpty(text)) return;
            DrawBox(s, kit, mouse, text);
        }

        public static void DrawBox(Screen2D s, UiKit kit, Vector2Int mouse, string text)
        {
            var font = kit.BigGrey;
            int tw = font.Measure(text) + 16;
            // Rounded up to a multiple of the 28 px tile, plus 2 (§5.1).
            int w = ((tw + 27) / 28) * 28 + 2;
            int x = mouse.x + 16 - w / 2, y;
            int barTop = s.Height - 41;
            if (mouse.y < barTop)
            {
                y = mouse.y + 32;
                if (y + 28 > s.Height - 28) y = mouse.y - 32;
            }
            else
            {
                y = Mathf.Min(mouse.y, s.Height - 28);
                x = mouse.x + 32 + w < s.Width - 10 ? mouse.x + 32 : mouse.x - 5 - w;
            }
            x = Mathf.Clamp(x, 2, s.Width - 1 - w);
            var t1 = kit.WhiteTrans(UiKit.StdLib, "TOOLTIP1");
            var t2 = kit.WhiteTrans(UiKit.StdLib, "TOOLTIP2");
            var t3 = kit.WhiteTrans(UiKit.StdLib, "TOOLTIP3");
            s.Draw(t1, x, y);
            for (int px = x + 28; px < x + w - 28; px += 28)
                s.Draw(t2, px, y, new RectInt(0, 0, Mathf.Min(28, x + w - 28 - px), 27), Color.white);
            s.Draw(t3, x + w - 28, y);
            font.Draw(s, x + w / 2, y + (27 - font.LineHeight) / 2 + 1, text, TextAlign.Center);
        }
    }

    /// <summary>
    /// Room crossfade (rooms-and-ui-9 §9.1): the old frame is captured, then blended out over the
    /// new one in 8 steps (~0.4 s, the original's 8 frames at 20 fps). Snapshot covers the whole
    /// (possibly wide) framebuffer.
    /// </summary>
    public sealed class Crossfade
    {
        public const int Steps = 8;
        public const float StepSeconds = 0.05f;
        public bool Enabled = true;
        RenderTexture _snap;
        float _t = -1;

        public bool Running => _t >= 0;

        /// <summary>Captures the last presented frame as the "old" picture and starts the blend.</summary>
        public void Start(Screen2D s)
        {
            if (!Enabled || s.Target == null) return;
            var src = s.Target;
            if (_snap == null || _snap.width != src.width || _snap.height != src.height)
            {
                if (_snap != null) { _snap.Release(); Object.Destroy(_snap); }
                _snap = new RenderTexture(src.width, src.height, 0, src.format) { filterMode = FilterMode.Point, name = "OpenAT fade" };
                _snap.Create();
            }
            Graphics.CopyTexture(src, _snap);
            _t = 0;
        }

        public void Draw(Screen2D s, float dt)
        {
            if (_t < 0 || _snap == null) return;
            int step = (int)(_t / StepSeconds);
            if (step >= Steps || _snap.width != s.Target.width) { _t = -1; return; }
            byte a = (byte)(255 * (Steps - step) / (Steps + 1));
            // The render texture is sampled bottom-up; flip the source rect vertically.
            s.DrawScaled(_snap, new Rect(0, 0, s.Width, s.Height), new Rect(0, _snap.height, _snap.width, -_snap.height), new Color32(255, 255, 255, a));
            _t += Mathf.Min(dt, StepSeconds);
        }
    }
}
