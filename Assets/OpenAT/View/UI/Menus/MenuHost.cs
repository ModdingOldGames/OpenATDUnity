using System;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI.Menus
{
    /// <summary>Original menu type numbers (rooms-and-ui-2 §1), kept for saves, logs and extensions.</summary>
    public static class MenuIds
    {
        public const int None = 0, Bank = 1, Filofax = 2, Personal = 3, SellPlane = 4, Request = 5, BuyPlane = 6,
            Letters = 7, AdRoute = 9, SetRendite = 10, Aktie = 11, Fluege = 13, WcF = 14, WcM = 15, RenamePlane = 16,
            GameOver = 17, BranchList = 18, PlayerList = 19, Auslandsauftrag = 20, PlaneCosts = 21, PlaneRepairs = 22,
            PlaneJob = 23, Kerosin = 25, BuyKerosin = 26, Kontoauszug = 28, SabotagePlane = 29, SabotageFax = 30,
            Pannen = 31, Closed = 32, Extrablatt = 33, QuitMessage = 34, Chat = 35, Briefing = 36, Broadcast = 37,
            CallItADay = 38, EnterTcpIp = 39, EnterProtect = 40, Security = 41, RenameEditPlane = 42,
            SabotageRoute = 43, BuyXPlane = 44;
    }

    /// <summary>
    /// A popup/list menu (rooms-and-ui-2). Coordinates inside the menu are "menu-local" (relative to its
    /// top-left). Menus are modal for the room but game time keeps running (§2.6).
    /// </summary>
    public abstract class PopupMenu
    {
        public MenuHost Host { get; internal set; }
        protected GameApp App => Host.App;
        protected UiKit Kit => App.Ui.Kit;
        protected GameAssets Assets => App.Assets;

        /// <summary>Original MENU_* number (<see cref="MenuIds"/>).</summary>
        public abstract int Type { get; }
        public abstract Vector2Int Size { get; }
        /// <summary>Background picture used for the zoom animation (may be null).</summary>
        public virtual Texture Background => null;
        /// <summary>Canvas position; null = centred in the 640x440 room area (§2.2).</summary>
        public virtual Vector2Int? FixedPos => null;

        /// <summary>Dialog line the partner says when the menu closes (-1 = none, §3.6 of part 3).</summary>
        public int ReEntry = -1;
        /// <summary>Called after the menu closed (any reason).</summary>
        public Action Closed;

        /// <summary>Screen position of the menu's top-left while open.</summary>
        public Vector2Int ScreenPos { get; internal set; }

        public virtual void OnOpen() { }
        /// <summary>Hover and clicks; <paramref name="local"/> = mouse in menu coordinates.</summary>
        public virtual void Update(InputState input, Vector2Int local) { }
        public abstract void Draw(Screen2D s, int x, int y, Vector2Int local);
        /// <summary>Right click: generic cancel (§2.8).</summary>
        public virtual void OnRightClick() => Close();
        public virtual bool OnKey(InputState input) => false;
        public virtual void OnClosed() { }

        public void Close() => Host?.Close(this);

        /// <summary>Inclusive menu-local rect test; sets cursor and tooltip when hovering.</summary>
        protected bool Hot(Vector2Int l, int x1, int y1, int x2, int y2, int tooltip = 0, CursorLook look = CursorLook.Hot)
        {
            PadTarget(x1, y1, x2, y2);
            if (l.x < x1 || l.x > x2 || l.y < y1 || l.y > y2) return false;
            App.Ui.Cursor = look;
            if (tooltip != 0) App.Ui.Tooltip.Set(tooltip);
            return true;
        }

        /// <summary>A gamepad target in menu coordinates (inclusive corners), OpenAT.</summary>
        protected void PadTarget(int x1, int y1, int x2, int y2) =>
            App.Ui.Targets.Add(new RectInt(ScreenPos.x + x1, ScreenPos.y + y1, x2 - x1 + 1, y2 - y1 + 1));

        protected string Std(string group, int id) => Assets.StdText.Get(group, id) ?? "";

        /// <summary>
        /// Hover highlight of list rows and form cells (stands in for the original's text recolouring, rooms-and-ui-2 §2.5).
        /// Not for the text buttons of request-style menus: those show only the hot cursor.
        /// </summary>
        protected static void HoverBand(Screen2D s, RectInt r) => s.FillRect(r, new Color32(32, 255, 100, 70));

        protected void Paper() => App.Audio.Play("SOUND/paper.raw");
    }

    /// <summary>
    /// The room's single menu slot (rooms-and-ui-2 §2): opening with optional zoom from a point,
    /// shrinking back on close, right-click cancel, the calculator on top, and dialog re-entry.
    /// </summary>
    public sealed class MenuHost
    {
        public readonly GameApp App;
        readonly Func<int> _canvasX;
        public PopupMenu Current { get; private set; }
        public readonly Calculator Calculator = new Calculator();

        // Zoom state (§2.3): counter 0..100, start point in screen pixels, minimum scale.
        float _zoom = 100;
        Vector2 _from;
        float _minZoom = 1;
        PopupMenu _closing;
        Vector2Int _closingPos;

        /// <summary>Raised when a menu closes; the dialog engine uses it for re-entry.</summary>
        public event Action<PopupMenu> MenuClosed;

        public MenuHost(GameApp app, Func<int> canvasX)
        {
            App = app;
            _canvasX = canvasX;
            Calculator.App = app;
        }

        public bool IsOpen => Current != null;

        /// <summary>
        /// Opens a menu. <paramref name="zoomFrom"/> is a point in 640x440 canvas coordinates (or screen
        /// coordinates when <paramref name="screenOrigin"/>); <paramref name="minZoom"/> 1 = no zoom.
        /// </summary>
        public void Open(PopupMenu menu, Vector2Int? zoomFrom = null, float minZoom = 1f, bool screenOrigin = false)
        {
            if (Current != null) CloseSilently();
            App.Audio.StopVoice();
            menu.Host = this;
            Current = menu;
            _closing = null;
            _minZoom = zoomFrom.HasValue ? minZoom : 1f;
            var p = zoomFrom ?? Vector2Int.zero;
            _from = screenOrigin ? (Vector2)p : new Vector2(p.x + _canvasX(), p.y);
            _zoom = _minZoom >= 1f ? 100 : 0;
            Place(menu);
            menu.OnOpen();
        }

        void Place(PopupMenu m)
        {
            var pos = m.FixedPos ?? new Vector2Int((640 - m.Size.x) / 2, (440 - m.Size.y) / 2);
            m.ScreenPos = new Vector2Int(pos.x + _canvasX(), pos.y);
        }

        public void Close(PopupMenu m)
        {
            if (m == null || m != Current) return;
            Calculator.Cancel();
            Current = null;
            _closing = _minZoom < 1f ? m : null;
            _closingPos = m.ScreenPos;
            m.OnClosed();
            m.Closed?.Invoke();
            MenuClosed?.Invoke(m);
        }

        void CloseSilently()
        {
            var m = Current;
            Current = null;
            Calculator.Cancel();
            m?.OnClosed();
        }

        /// <summary>Handles input while a menu is open. Returns true when the menu took the input.</summary>
        public bool Update(InputState input, float dt)
        {
            Animate(dt);
            if (Current == null) return false;
            Place(Current);
            if (Calculator.IsOpen)
            {
                App.Ui.Targets.ModalKey = Calculator;   // gamepad: the keys are the targets (OpenAT)
                Calculator.Update(input);
                if (input.RightDown) { Calculator.Cancel(); input.Consume(); }
                input.Consume();
                return true;
            }
            var local = input.Mouse - Current.ScreenPos;
            App.Ui.Targets.ModalKey = Current;   // gamepad: the menu's options are the targets (OpenAT)
            if (Current.OnKey(input)) return true;
            var m = Current;
            m.Update(input, local);
            if (Current == m && input.RightDown && !input.Consumed)
            {
                m.OnRightClick();
                input.Consume();
            }
            if (input.LeftDown) input.Consume();
            return true;
        }

        /// <summary>Zoom in/out progress; call every frame (also while no menu is open).</summary>
        public void Animate(float dt)
        {
            if (_animatedFrame == Time.frameCount) return;
            _animatedFrame = Time.frameCount;
            float speed = 100f / 0.2f; // 4 original frames ≈ 200 ms
            if (Current != null && _zoom < 100) _zoom = Mathf.Min(100, _zoom + speed * dt);
            if (Current == null && _closing != null)
            {
                _zoom -= speed * dt;
                if (_zoom <= 0) _closing = null;
            }
        }
        int _animatedFrame = -1;

        public void Draw(Screen2D s, InputState input)
        {
            if (Current != null)
            {
                var m = Current;
                if (_zoom >= 100) m.Draw(s, m.ScreenPos.x, m.ScreenPos.y, input.Mouse - m.ScreenPos);
                else DrawZoomed(s, m, m.ScreenPos, _zoom / 100f);
                if (Calculator.IsOpen) Calculator.Draw(s);
            }
            else if (_closing != null && _zoom > 0) DrawZoomed(s, _closing, _closingPos, _zoom / 100f);
        }

        void DrawZoomed(Screen2D s, PopupMenu m, Vector2Int target, float z)
        {
            var bg = m.Background;
            if (bg == null) return;
            // Top-left moves linearly from the start point to the menu position; scale min..1.
            float scale = Mathf.Lerp(_minZoom, 1f, z);
            float x = Mathf.Lerp(_from.x, target.x, z), y = Mathf.Lerp(_from.y, target.y, z);
            s.DrawScaled(bg, new Rect(x, y, m.Size.x * scale, m.Size.y * scale), new Rect(0, 0, bg.width, bg.height), Color.white);
        }
    }

    /// <summary>
    /// Pocket calculator number entry (rooms-and-ui-2 §3): rechner.gli, digits up to 99,999,999,
    /// C clears, OK confirms, X cancels; keys 0-9, Enter, Backspace/Delete, Esc. Starts at 0.
    /// </summary>
    public sealed class Calculator
    {
        const string Lib = "gli/rechner.gli";
        public GameApp App;
        public bool IsOpen { get; private set; }
        public long Value;
        Vector2Int _pos;
        Action<long> _ok;
        Action _cancel;
        string _pressedKey;
        float _pressedUntil;

        static readonly (string key, int x, int y)[] Keys =
        {
            ("7", 6, 65), ("8", 36, 65), ("9", 66, 65), ("4", 6, 85), ("5", 36, 85), ("6", 66, 85),
            ("1", 6, 105), ("2", 36, 105), ("3", 66, 105), ("0", 6, 125), ("OK", 36, 125), ("C", 67, 46), ("X", 5, 45),
        };

        public void Open(Vector2Int screenPos, Action<long> onOk, Action onCancel = null)
        {
            IsOpen = true;
            Value = 0; // the original ignores the preset value (§3)
            _pos = screenPos;
            _ok = onOk;
            _cancel = onCancel;
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            var c = _cancel; _cancel = null; _ok = null;
            c?.Invoke();
        }

        void Press(string key)
        {
            App.Audio.Play("SOUND/calc.raw");
            _pressedKey = key; _pressedUntil = Time.unscaledTime + 0.3f;
            switch (key)
            {
                case "OK": { IsOpen = false; var ok = _ok; _ok = null; _cancel = null; ok?.Invoke(Value); break; }
                case "X": Cancel(); break;
                case "C": Value = 0; break;
                default: if (Value < 10000000) Value = Value * 10 + (key[0] - '0'); break;
            }
        }

        static Vector2Int KeySize(string k) => k == "OK" ? new Vector2Int(61, 22) : new Vector2Int(29, 19);

        public void Update(InputState input)
        {
            foreach (char c in input.Typed) if (c >= '0' && c <= '9') Press(c.ToString());
            if (input.Enter) Press("OK");
            else if (input.Backspace || input.Delete) Press("C");
            else if (input.Escape) Press("X");
            // Gamepad targets (OpenAT): the keys.
            foreach (var k in Keys)
            {
                var ks = KeySize(k.key);
                App.Ui.Targets.Add(new RectInt(_pos.x + k.x + 4, _pos.y + k.y + 4, ks.x - 8, ks.y - 8));
            }
            if (!input.LeftDown || !IsOpen) return;
            var l = input.Mouse - _pos;
            foreach (var k in Keys)
            {
                var sz = KeySize(k.key);
                if (l.x >= k.x + 4 && l.x < k.x + sz.x - 4 && l.y >= k.y + 4 && l.y < k.y + sz.y - 4) { Press(k.key); break; }
            }
        }

        public void Draw(Screen2D s)
        {
            var a = App.Assets;
            s.Draw(a.Texture(Lib, "RECHNER"), _pos.x, _pos.y);
            if (Time.unscaledTime < _pressedUntil && _pressedKey != null)
            {
                foreach (var k in Keys)
                    if (k.key == _pressedKey)
                    {
                        string bmp = k.key == "OK" ? "TASTE_OK" : k.key == "C" ? "TASTE_C" : k.key == "X" ? "TASTE_X" : "T" + k.key;
                        s.Draw(a.Texture(Lib, bmp), _pos.x + k.x, _pos.y + k.y);
                    }
            }
            if (Value <= 0) return;
            string digits = Value.ToString();
            for (int i = 0; i < digits.Length && i < 8; i++)
            {
                int d = digits[digits.Length - 1 - i] - '0';
                s.Draw(a.Texture(Lib, "D" + d), _pos.x + 82 - i * 10, _pos.y + 19);
                if (i == 3 && digits.Length > 3) s.Draw(a.Texture(Lib, "DOT"), _pos.x + 61, _pos.y + 33);
                if (i == 6 && digits.Length > 6) s.Draw(a.Texture(Lib, "DOT"), _pos.x + 31, _pos.y + 33);
            }
        }
    }
}
