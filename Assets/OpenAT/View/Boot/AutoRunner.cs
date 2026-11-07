using System;
using System.Collections.Generic;
using System.Globalization;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Scripted input for automated checks: <c>-openat-auto "wait 30; click 320 240; shot out.png; quit"</c>.
    /// Commands: <c>wait N</c> (frames), <c>move X Y</c>, <c>click X Y</c>, <c>rclick X Y</c>,
    /// <c>key esc|enter|space|f2|f3|f4|tab|backspace</c>, <c>type TEXT</c> (typed characters), <c>mod ctrl|shift|ctrl+shift|none</c> (held modifiers), <c>shot PATH</c>, <c>quit</c>; drag-and-drop with <c>down X Y</c> (press and hold),
    /// <c>move X Y</c> (while held) and <c>up X Y</c> (release); <c>money N</c> sets the local player's cash, <c>room N</c> enters a room (test scripts); <c>live</c> switches to the real devices for the rest of the run. Coordinates are virtual pixels;
    /// prefix X with <c>c</c> (e.g. <c>c320</c>) to offset by the centring margin of 640-wide layouts.
    /// </summary>
    public sealed class AutoRunner
    {
        readonly GameApp _app;
        readonly Queue<string> _commands;
        int _waitFrames;
        bool _holding;
        string _pendingShot;
        readonly InputState _input = new InputState();

        AutoRunner(GameApp app, string script)
        {
            _app = app;
            _commands = new Queue<string>();
            foreach (var c in script.Split(';'))
                if (c.Trim().Length > 0) _commands.Enqueue(c.Trim());
            _app.InjectedInput = _input;
        }

        public static AutoRunner FromCommandLine(GameApp app)
        {
            var script = CommandLine.Value("-openat-auto");
            return string.IsNullOrEmpty(script) ? null : new AutoRunner(app, script);
        }

        /// <summary>Extension commands: return frames to wait (&gt;0 = handled and wait), 0 = handled or unknown.</summary>
        public static Func<GameApp, string[], int> Extra;

        int X(string s) => s.StartsWith("c") ? _app.Screen.CenterX + int.Parse(s.Substring(1), CultureInfo.InvariantCulture) : int.Parse(s, CultureInfo.InvariantCulture);

        public void Tick(float dt)
        {
            if (_holding) { _input.LeftHeld = true; _input.LeftUp = false; }
            if (_waitFrames > 0) { _waitFrames--; return; }
            while (_commands.Count > 0 && _waitFrames == 0)
            {
                var parts = _commands.Dequeue().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    switch (parts[0])
                    {
                        case "wait": _waitFrames = int.Parse(parts[1]); break;
                        case "move": _input.Mouse = new Vector2Int(X(parts[1]), int.Parse(parts[2])); break;
                        case "click":
                            _input.Mouse = new Vector2Int(X(parts[1]), int.Parse(parts[2]));
                            _input.LeftDown = true; _input.LeftHeld = true;
                            _waitFrames = 2;
                            return;
                        case "down":
                            _input.Mouse = new Vector2Int(X(parts[1]), int.Parse(parts[2]));
                            _input.LeftDown = true; _input.LeftHeld = true; _holding = true;
                            _waitFrames = 2;
                            return;
                        case "up":
                            _input.Mouse = new Vector2Int(X(parts[1]), int.Parse(parts[2]));
                            _input.LeftDown = false; _input.LeftHeld = false; _input.LeftUp = true; _holding = false;
                            _waitFrames = 2;
                            return;
                        case "rclick":
                            _input.Mouse = new Vector2Int(X(parts[1]), int.Parse(parts[2]));
                            _input.RightDown = true; _input.RightHeld = true;
                            _waitFrames = 2;
                            return;
                        case "key":
                            if (parts[1] == "esc") _input.Escape = true;
                            if (parts[1] == "enter") _input.Enter = true;
                            if (parts[1] == "space") _input.Space = true;
                            if (parts[1] == "f2") _input.F2 = true;
                            if (parts[1] == "f3") _input.F3 = true;
                            if (parts[1] == "f4") _input.F4 = true;
                            if (parts[1] == "tab") _input.Tab = true;
                            if (parts[1] == "backspace") _input.Backspace = true;
                            _waitFrames = 1;
                            return;
                        case "mod":
                            // Held modifier keys for the following commands: "mod ctrl", "mod shift", "mod ctrl+shift", "mod none".
                            _input.Ctrl = parts[1].Contains("ctrl");
                            _input.Shift = parts[1].Contains("shift");
                            break;
                        case "type":
                            // Typed characters (save names etc.): the rest of the command.
                            foreach (char ch in string.Join(" ", parts, 1, parts.Length - 1)) _input.Typed.Add(ch);
                            _waitFrames = 1;
                            return;
                        case "money":
                            // Test helper: set the local player's cash (scripted screenshots only).
                            var ses = _app.EnsureSession();
                            if (ses != null) ses.Local.Money = long.Parse(parts[1], CultureInfo.InvariantCulture);
                            break;
                        case "planename":
                            // Test helper: rename a local plane, "planename <index> <name with spaces>" (font checks).
                            var sp = _app.EnsureSession();
                            int pi = int.Parse(parts[1], CultureInfo.InvariantCulture);
                            if (sp != null && pi >= 0 && pi < sp.Local.Planes.Count) sp.Local.Planes[pi].Name = string.Join(" ", parts, 2, parts.Length - 2);
                            break;
                        case "room":
                            // Test helper: enter a room on top of the current location.
                            _app.Rooms.Enter(int.Parse(parts[1], CultureInfo.InvariantCulture));
                            _waitFrames = 2;
                            return;
                        case "shot": _pendingShot = parts[1]; _waitFrames = 1; return;
                        case "pad":
                            // Virtual gamepad (tests of the gamepad path): "pad lx ly rx ry [a b x y lb rb start select up down left right l3 r3 lt rt]".
                            Pad(parts);
                            Debug.Log("OpenAT auto: pad, " + _app.Ui.Targets.Rects.Count + " targets, cursor " + _app.InputNow.Mouse + (_app.Ui.Pad.Active ? " (pad)" : ""));
                            _waitFrames = 1;
                            return;
                        // Hand input back to the real mouse and keyboard (tests of the device path); the script goes on.
                        case "live": _app.InjectedInput = null; break;
                        default:
                            // Commands added by other modules (test helpers); they return the frames to wait.
                            if (Extra != null) { int wf = Extra(_app, parts); if (wf > 0) { _waitFrames = wf; return; } }
                            break;
                        case "quit":
                            Application.Quit();
#if UNITY_EDITOR
                            UnityEditor.EditorApplication.isPlaying = false;
#endif
                            return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("OpenAT auto: bad command '" + string.Join(" ", parts) + "': " + e.Message);
                }
            }
        }

        UnityEngine.InputSystem.Gamepad _pad;

        void Pad(string[] parts)
        {
            if (_pad == null) _pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            _pad.MakeCurrent();
            float F(int i) => parts.Length > i ? float.Parse(parts[i], CultureInfo.InvariantCulture) : 0f;
            var st = new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = new Vector2(F(1), F(2)), rightStick = new Vector2(F(3), F(4)) };
            for (int i = 5; i < parts.Length; i++)
            {
                switch (parts[i])
                {
                    case "a": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South); break;
                    case "b": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.East); break;
                    case "x": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.West); break;
                    case "y": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.North); break;
                    case "lb": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder); break;
                    case "rb": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder); break;
                    case "start": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Start); break;
                    case "select": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Select); break;
                    case "up": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadUp); break;
                    case "down": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown); break;
                    case "left": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft); break;
                    case "right": st = st.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight); break;
                    case "lt": st.leftTrigger = 1; break;
                    case "rt": st.rightTrigger = 1; break;
                }
            }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(_pad, st);
        }

        public void AfterFrame()
        {
            if (_pendingShot == null) return;
            _app.SaveFramebuffer(_pendingShot);
            Debug.Log("OpenAT auto: saved " + _pendingShot);
            _pendingShot = null;
        }
    }
}
