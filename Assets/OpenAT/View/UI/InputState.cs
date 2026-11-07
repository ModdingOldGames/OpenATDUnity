using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenAT.View.UI
{
    /// <summary>Per-frame input snapshot in virtual pixels. Screens read this instead of devices.</summary>
    public sealed class InputState
    {
        public Vector2Int Mouse;
        public bool LeftDown, LeftUp, LeftHeld;
        public bool RightDown, RightUp, RightHeld;
        public float Wheel;
        /// <summary>Characters typed this frame (text entry, cheat codes, + and - keys).</summary>
        public readonly List<char> Typed = new List<char>();
        public bool Escape, Enter, Backspace, Space;
        public bool Left, Right, Up, Down;
        /// <summary>Edge-triggered navigation / function keys (pressed this frame).</summary>
        public bool LeftPressed, RightPressed, UpPressed, DownPressed;
        public bool F1, F2, F3, F4, Tab, Delete, PauseKey;
        public bool Ctrl, Shift;
        /// <summary>Set by a screen that handled the click so overlays below ignore it.</summary>
        public bool Consumed;

        // Text entry. Characters come from Keyboard.onTextInput (layout, shift and dead keys handled by the
        // OS). The hook is static, so it is reset when the editor enters play mode without a domain reload
        // (the old keyboard/delegate would otherwise survive), and re-attached whenever Keyboard.current changes
        // (device added/removed/replaced). Until the first text event arrives, plain key presses are translated
        // as a fallback, so typing works even if the text callback never fires (e.g. editor focus quirks).
        static readonly List<char> _textBuffer = new List<char>();
        static Keyboard _hooked;
        static bool _textEventsSeen;
        static char _fallbackChar;
        static int _fallbackFrame = -100;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (_hooked != null) _hooked.onTextInput -= OnText;
            _hooked = null;
            _textEventsSeen = false;
            _fallbackFrame = -100;
            _textBuffer.Clear();
        }

        static void HookText(Keyboard k)
        {
            if (k == _hooked) return;
            if (_hooked != null) _hooked.onTextInput -= OnText;
            _hooked = k;
            if (k != null) k.onTextInput += OnText;
        }

        static void OnText(char c)
        {
            bool first = !_textEventsSeen;
            _textEventsSeen = true;
            // The first real text event may trail the key press the fallback already turned into a character.
            if (first && Time.frameCount - _fallbackFrame <= 2 && char.ToUpperInvariant(c) == char.ToUpperInvariant(_fallbackChar)) return;
            if (c >= ' ' && c != (char)127) _textBuffer.Add(c);
        }

        /// <summary>Fallback when no text events arrive: A-Z, 0-9, space, minus, period, plus from key presses.</summary>
        static void FallbackText(Keyboard k)
        {
            if (k == null || _textEventsSeen || _textBuffer.Count > 0 || !k.anyKey.wasPressedThisFrame) return;
            bool shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
            for (var key = Key.A; key <= Key.Z; key++)
            {
                var kc = k[key];
                if (!kc.wasPressedThisFrame) continue;
                // The key's label in the current layout (Z/Y on a German keyboard), else its US position.
                string label = kc.displayName;
                char ch = label != null && label.Length == 1 && char.IsLetter(label[0]) ? label[0] : (char)('a' + (key - Key.A));
                _textBuffer.Add(shift ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
            }
            for (var key = Key.Digit1; key <= Key.Digit0; key++)
                if (k[key].wasPressedThisFrame) _textBuffer.Add(key == Key.Digit0 ? '0' : (char)('1' + (key - Key.Digit1)));
            if (k.spaceKey.wasPressedThisFrame) _textBuffer.Add(' ');
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) _textBuffer.Add('-');
            if (k.periodKey.wasPressedThisFrame) _textBuffer.Add('.');
            if (k.numpadPlusKey.wasPressedThisFrame) _textBuffer.Add('+');
            if (_textBuffer.Count > 0) { _fallbackChar = _textBuffer[_textBuffer.Count - 1]; _fallbackFrame = Time.frameCount; }
        }

        public void Capture(Vector2Int mouse)
        {
            Mouse = mouse;
            var m = UnityEngine.InputSystem.Mouse.current;
            LeftDown = m != null && m.leftButton.wasPressedThisFrame;
            LeftUp = m != null && m.leftButton.wasReleasedThisFrame;
            LeftHeld = m != null && m.leftButton.isPressed;
            RightDown = m != null && m.rightButton.wasPressedThisFrame;
            RightUp = m != null && m.rightButton.wasReleasedThisFrame;
            RightHeld = m != null && m.rightButton.isPressed;
            Wheel = m != null ? m.scroll.ReadValue().y : 0;
            var k = Keyboard.current;
            HookText(k);
            Escape = k != null && k.escapeKey.wasPressedThisFrame;
            Enter = k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
            Backspace = k != null && k.backspaceKey.wasPressedThisFrame;
            Space = k != null && k.spaceKey.wasPressedThisFrame;
            Left = k != null && k.leftArrowKey.isPressed;
            Right = k != null && k.rightArrowKey.isPressed;
            Up = k != null && k.upArrowKey.isPressed;
            Down = k != null && k.downArrowKey.isPressed;
            LeftPressed = k != null && k.leftArrowKey.wasPressedThisFrame;
            RightPressed = k != null && k.rightArrowKey.wasPressedThisFrame;
            UpPressed = k != null && k.upArrowKey.wasPressedThisFrame;
            DownPressed = k != null && k.downArrowKey.wasPressedThisFrame;
            F1 = k != null && k.f1Key.wasPressedThisFrame;
            F2 = k != null && k.f2Key.wasPressedThisFrame;
            F3 = k != null && k.f3Key.wasPressedThisFrame;
            F4 = k != null && k.f4Key.wasPressedThisFrame;
            Tab = k != null && k.tabKey.wasPressedThisFrame;
            Delete = k != null && k.deleteKey.wasPressedThisFrame;
            PauseKey = k != null && k.pauseKey.wasPressedThisFrame;
            Ctrl = k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
            Shift = k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
            FallbackText(k);
            Typed.Clear();
            Typed.AddRange(_textBuffer);
            _textBuffer.Clear();
            Consumed = false;
        }

        /// <summary>Clears one-frame flags (used for injected input).</summary>
        public void ResetEdges()
        {
            if (LeftDown) { LeftDown = false; LeftUp = true; LeftHeld = false; } else LeftUp = false;
            if (RightDown) { RightDown = false; RightUp = true; RightHeld = false; } else RightUp = false;
            Escape = Enter = Backspace = Space = false;
            LeftPressed = RightPressed = UpPressed = DownPressed = false;
            F1 = F2 = F3 = F4 = Tab = Delete = PauseKey = false;
            Typed.Clear();
            Wheel = 0;
            Consumed = false;
        }

        public bool Clicked(RectInt r) => LeftDown && !Consumed && r.Contains(Mouse);
        public bool Hover(RectInt r) => r.Contains(Mouse);

        /// <summary>Inclusive-rect hover test in the original's (x1,y1)-(x2,y2) notation, offset by (ox, oy).</summary>
        public bool In(int ox, int oy, int x1, int y1, int x2, int y2) =>
            Mouse.x >= ox + x1 && Mouse.x <= ox + x2 && Mouse.y >= oy + y1 && Mouse.y <= oy + y2;

        public bool AnyKeyOrClick => LeftDown || RightDown || Escape || Enter || Space || Typed.Count > 0;

        public void Consume() => Consumed = true;
    }
}
