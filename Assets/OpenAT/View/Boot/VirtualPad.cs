using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// A virtual PlayStation 4 controller for recordings and tests without a physical pad (OpenAT dev tool,
    /// docs/VIRTUALPAD.md). It adds an Input System <see cref="DualShock4GamepadHID"/> device and feeds it the
    /// device's own state format ("D4VS"), so the game reads it through <see cref="Gamepad.current"/> exactly like a
    /// connected DualShock 4. No driver is installed; only this game sees the device.
    /// <para>
    /// Control: text commands over UDP on 127.0.0.1 (default port 47800), sent by tools/virtualpad (panel or CLI):
    /// <c>ls x y</c>, <c>rs x y</c> (−1..1, y up), <c>l2 v</c>, <c>r2 v</c> (0..1), <c>down b</c>, <c>up b</c>,
    /// <c>tap b [ms]</c>, <c>reset</c>, <c>state</c> (answers with the values the Input System reads back),
    /// <c>overlay on|off|tl|tr|bl|br|scale n</c>, <c>shot path</c> (saves the next frame as PNG). Buttons: cross circle square triangle l1 r1 l3 r3 share options ps
    /// touchpad and the d-pad: dup ddown dleft dright. Several commands per datagram, separated by ';' or new lines.
    /// </para>
    /// Enabled by <c>-openat-virtualpad[=port]</c>, the environment variable OPENAT_VIRTUALPAD (1 or a port), or in
    /// the editor by the menu OpenAT > Virtual DualShock 4.
    /// </summary>
    public sealed class VirtualPad : IDisposable
    {
        public const int DefaultPort = 47800;
        public const string EditorPref = "OpenAT.VirtualPad";

        /// <summary>The device's native state (same layout as the Input System's DualShock 4 HID report).</summary>
        [StructLayout(LayoutKind.Explicit, Size = 9)]
        struct D4State : IInputStateTypeInfo
        {
            public FourCC format => new FourCC('D', '4', 'V', 'S');
            [FieldOffset(0)] public byte lx;
            [FieldOffset(1)] public byte ly;
            [FieldOffset(2)] public byte rx;
            [FieldOffset(3)] public byte ry;
            [FieldOffset(4)] public byte buttons1;
            [FieldOffset(5)] public byte buttons2;
            [FieldOffset(6)] public byte buttons3;
            [FieldOffset(7)] public byte l2;
            [FieldOffset(8)] public byte r2;
        }

        public readonly int Port;
        public DualShock4GamepadHID Device { get; private set; }
        /// <summary>Set by the app: saves the next rendered frame to a PNG file (command <c>shot path</c>).</summary>
        public Action<string> Shot;
        /// <summary>Set by the app: the game side of the <c>state</c> answer (cursor, who drives, focused target, screen).</summary>
        public Func<string> GameState;
        /// <summary>Set by the app: the gamepad targets of the last frame (command <c>targets</c>).</summary>
        public Func<string> Targets;
        /// <summary>Set by the app: <c>record start file.mp4 [fps]</c> / <c>record stop</c> / <c>record status</c>, and <c>music on|off</c>.</summary>
        public Func<string, string> Record, Music;

        Vector2 _ls, _rs;
        float _l2, _r2;
        // Buttons by name; tap releases are timed (unscaled time).
        readonly System.Collections.Generic.Dictionary<string, bool> _held = new System.Collections.Generic.Dictionary<string, bool>();
        readonly System.Collections.Generic.Dictionary<string, float> _tapUntil = new System.Collections.Generic.Dictionary<string, float>();
        bool _dirty = true;
        float _lastSent;

        UdpClient _udp;
        Thread _thread;
        volatile bool _running;
        readonly ConcurrentQueue<(string text, IPEndPoint from)> _inbox = new ConcurrentQueue<(string, IPEndPoint)>();

        /// <summary>Overlay settings the commands change (read by <see cref="UI.PadOverlay"/>).</summary>
        public static bool OverlayOn = true;
        public static string OverlayCorner = "br";
        public static float OverlayScale = 1f;

        static readonly string[] Buttons =
            { "cross", "circle", "square", "triangle", "l1", "r1", "l3", "r3", "share", "options", "ps", "touchpad", "dup", "ddown", "dleft", "dright" };

        /// <summary>The port when the virtual pad is switched on, else 0.</summary>
        public static int RequestedPort()
        {
            foreach (var a in CommandLine.Args)
            {
                if (a == "-openat-virtualpad") return DefaultPort;
                if (a.StartsWith("-openat-virtualpad=", StringComparison.Ordinal) && int.TryParse(a.Substring(19), out int p)) return p;
            }
            var env = Environment.GetEnvironmentVariable("OPENAT_VIRTUALPAD");
            if (!string.IsNullOrEmpty(env)) return int.TryParse(env, out int ep) && ep > 1 ? ep : DefaultPort;
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(EditorPref, false)) return DefaultPort;
#endif
            return 0;
        }

        public VirtualPad(int port)
        {
            Port = port;
            foreach (var b in Buttons) _held[b] = false;
            // The control panel has the focus while recording: keep running, and keep the devices enabled and their
            // state when the game window is not focused (the Input System resets and disables them by default).
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            Device = InputSystem.AddDevice<DualShock4GamepadHID>("Virtual DualShock 4");
            Device.MakeCurrent();
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                _running = true;
                _thread = new Thread(Receive) { IsBackground = true, Name = "OpenAT virtual pad" };
                _thread.Start();
                Debug.Log("OpenAT virtual pad: DualShock 4 device '" + Device.name + "' added, listening on 127.0.0.1:" + port);
            }
            catch (SocketException e)
            {
                Debug.LogError("OpenAT virtual pad: cannot listen on 127.0.0.1:" + port + ": " + e.Message);
            }
        }

        void Receive()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var data = _udp.Receive(ref any);
                    _inbox.Enqueue((Encoding.UTF8.GetString(data), any));
                }
                catch (SocketException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        /// <summary>Once per frame on the main thread: runs the received commands and sends the state to the device.</summary>
        public void Pump()
        {
            while (_inbox.TryDequeue(out var msg))
                foreach (var line in msg.text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string reply = Run(line.Trim());
                    if (reply != null) Reply(reply, msg.from);
                }
            // A device disabled on an earlier focus change (before the setting above took effect) is enabled again.
            if (Device != null && Device.added && !Device.enabled) { InputSystem.EnableDevice(Device); _dirty = true; }
            float now = Time.unscaledTime;
            foreach (var b in Buttons)
                if (_tapUntil.TryGetValue(b, out float until) && until > 0 && now >= until) { _held[b] = false; _tapUntil[b] = 0; _dirty = true; }
            // Resend now and then so the device never looks idle to the Input System.
            if (_dirty || now - _lastSent > 0.5f) Send();
        }

        void Reply(string text, IPEndPoint to)
        {
            try { var b = Encoding.UTF8.GetBytes(text); _udp?.Send(b, b.Length, to); } catch (SocketException) { }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        string Run(string line)
        {
            if (line.Length == 0) return null;
            var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (p[0].ToLowerInvariant())
                {
                    case "ls": _ls = Clamp(new Vector2(F(p[1]), F(p[2]))); break;
                    case "rs": _rs = Clamp(new Vector2(F(p[1]), F(p[2]))); break;
                    case "l2": _l2 = Mathf.Clamp01(F(p[1])); break;
                    case "r2": _r2 = Mathf.Clamp01(F(p[1])); break;
                    case "down": SetButton(p[1], true); break;
                    case "up": SetButton(p[1], false); break;
                    case "tap":
                        SetButton(p[1], true);
                        _tapUntil[p[1].ToLowerInvariant()] = Time.unscaledTime + (p.Length > 2 ? F(p[2]) : 120f) / 1000f;
                        break;
                    case "reset":
                        _ls = _rs = Vector2.zero; _l2 = _r2 = 0;
                        foreach (var b in Buttons) { _held[b] = false; _tapUntil[b] = 0; }
                        break;
                    case "state": return StateText();
                    case "ping": return "pong";
                    case "targets": return Targets?.Invoke() ?? "";
                    case "record": return Record?.Invoke(line.Substring(line.IndexOf(' ') + 1).Trim()) ?? "error no recorder";
                    case "music": return Music?.Invoke(p.Length > 1 ? p[1].ToLowerInvariant() : "") ?? "error";
                    case "shot":
                        Shot?.Invoke(line.Substring(line.IndexOf(' ') + 1).Trim());
                        return "shot";
                    case "overlay":
                        switch (p[1].ToLowerInvariant())
                        {
                            case "on": OverlayOn = true; break;
                            case "off": OverlayOn = false; break;
                            case "scale": OverlayScale = Mathf.Clamp(F(p[2]), 0.5f, 3f); break;
                            default: OverlayCorner = p[1].ToLowerInvariant(); break;
                        }
                        return null;
                    default: return "error unknown command: " + p[0];
                }
                _dirty = true;
                return null;
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is FormatException || e is ArgumentException)
            {
                return "error " + line + ": " + e.Message;
            }
        }

        static Vector2 Clamp(Vector2 v) => v.sqrMagnitude > 1 ? v.normalized : v;

        void SetButton(string name, bool on)
        {
            name = name.ToLowerInvariant();
            if (!_held.ContainsKey(name)) throw new ArgumentException("unknown button " + name);
            _held[name] = on;
            if (!on) _tapUntil[name] = 0;
        }

        static byte Axis(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v + 1f) * 0.5f * 255f), 0, 255);
        static byte Unit(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        void Send()
        {
            if (Device == null || !Device.added) return;
            bool u = _held["dup"], d = _held["ddown"], l = _held["dleft"], r = _held["dright"];
            int hat = u && r ? 1 : d && r ? 3 : d && l ? 5 : u && l ? 7 : u ? 0 : r ? 2 : d ? 4 : l ? 6 : 8;
            var s = new D4State
            {
                // The device reads y inverted (0 = up).
                lx = Axis(_ls.x), ly = Axis(-_ls.y), rx = Axis(_rs.x), ry = Axis(-_rs.y),
                buttons1 = (byte)(hat | (_held["square"] ? 0x10 : 0) | (_held["cross"] ? 0x20 : 0) | (_held["circle"] ? 0x40 : 0) | (_held["triangle"] ? 0x80 : 0)),
                buttons2 = (byte)((_held["l1"] ? 1 : 0) | (_held["r1"] ? 2 : 0) | (_l2 > 0.5f ? 4 : 0) | (_r2 > 0.5f ? 8 : 0)
                                  | (_held["share"] ? 0x10 : 0) | (_held["options"] ? 0x20 : 0) | (_held["l3"] ? 0x40 : 0) | (_held["r3"] ? 0x80 : 0)),
                buttons3 = (byte)((_held["ps"] ? 1 : 0) | (_held["touchpad"] ? 2 : 0)),
                l2 = Unit(_l2), r2 = Unit(_r2),
            };
            InputSystem.QueueStateEvent(Device, s);
            _dirty = false;
            _lastSent = Time.unscaledTime;
        }

        /// <summary>The values the Input System reads back from the device (what the game sees), one line.</summary>
        string StateText()
        {
            var g = Device;
            if (g == null) return "error no device";
            string V(Vector2 v) => v.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("device=").Append(g.name).Append(" current=").Append(Gamepad.current == g ? "yes" : "no")
              .Append(" enabled=").Append(g.enabled ? "yes" : "no").Append(" focus=").Append(Application.isFocused ? "game" : "other");
            sb.Append(" ls=").Append(V(g.leftStick.ReadValue())).Append(" rs=").Append(V(g.rightStick.ReadValue()));
            sb.Append(" l2=").Append(g.leftTrigger.ReadValue().ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" r2=").Append(g.rightTrigger.ReadValue().ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" dpad=").Append(V(g.dpad.ReadValue())).Append(" pressed=");
            void B(string n, UnityEngine.InputSystem.Controls.ButtonControl c) { if (c.isPressed) sb.Append(n).Append(','); }
            B("cross", g.buttonSouth); B("circle", g.buttonEast); B("square", g.buttonWest); B("triangle", g.buttonNorth);
            B("l1", g.leftShoulder); B("r1", g.rightShoulder); B("l3", g.leftStickButton); B("r3", g.rightStickButton);
            B("share", g.selectButton); B("options", g.startButton); B("ps", g.playStationButton); B("touchpad", g.touchpadButton);
            var gs = GameState?.Invoke();
            return sb.ToString().TrimEnd(',') + (string.IsNullOrEmpty(gs) ? "" : " " + gs);
        }

        public void Dispose()
        {
            _running = false;
            try { _udp?.Close(); } catch (SocketException) { }
            _udp = null;
            if (Device != null && Device.added) InputSystem.RemoveDevice(Device);
            Device = null;
        }
    }
}
