using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace OpenAT.EditorTools
{
    /// <summary>
    /// Lets a script or an agent drive the Unity editor without screen control (docs/VIRTUALPAD.md, "Editor bridge"):
    /// text commands over UDP on 127.0.0.1:47799, sent with <c>tools/virtualpad/padctl.py editor ...</c>. Each command
    /// answers with one line. The game's own pad channel (47800) works only while the game runs; this one works in
    /// edit mode too.
    /// <list type="bullet">
    /// <item><c>status</c>: play mode, pause, compiling, virtual pad switch, start arguments</item>
    /// <item><c>play</c> / <c>stop</c> / <c>pause</c> / <c>unpause</c>: Play mode</item>
    /// <item><c>virtualpad on|off</c>: the menu switch OpenAT > Virtual DualShock 4 (for the next Play session)</item>
    /// <item><c>args ...</c> / <c>args clear</c>: start arguments for the next Play session, e.g.
    /// <c>args -openat-scenario tools/scenarios/gamepad-demo.json</c></item>
    /// <item><c>refresh</c>: import changed files and recompile (only outside Play mode)</item>
    /// <item><c>focus</c>: bring the Game view to the front</item>
    /// <item><c>quit</c>: close the editor (for a headless editor started only for a recording or a test)</item>
    /// </list>
    /// The environment variable OPENAT_EDITOR_BRIDGE=0 switches it off; a number uses that port instead.
    /// </summary>
    [InitializeOnLoad]
    static class EditorBridge
    {
        public const int DefaultPort = 47799;
        const string PadPref = "OpenAT.VirtualPad";
        /// <summary>Per project (EditorPrefs are machine-wide); read by OpenAT.View.Boot.CommandLine.</summary>
        static string ArgsPref => "OpenAT.EditorArgs:" + Application.dataPath;

        static UdpClient _udp;
        static Thread _thread;
        static volatile bool _running;
        static readonly ConcurrentQueue<(string text, IPEndPoint from)> Inbox = new ConcurrentQueue<(string, IPEndPoint)>();

        static EditorBridge()
        {
            var env = Environment.GetEnvironmentVariable("OPENAT_EDITOR_BRIDGE");
            if (env == "0") return;
            int port = int.TryParse(env, out int p) && p > 1 ? p : DefaultPort;
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException e)
            {
                Debug.LogWarning("OpenAT editor bridge: cannot listen on 127.0.0.1:" + port + ": " + e.Message);
                return;
            }
            _running = true;
            _thread = new Thread(Receive) { IsBackground = true, Name = "OpenAT editor bridge" };
            _thread.Start();
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Close;
            EditorApplication.quitting += Close;
        }

        static void Close()
        {
            _running = false;
            try { _udp?.Close(); } catch (SocketException) { }
            _udp = null;
            EditorApplication.update -= Pump;
        }

        static void Receive()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try { Inbox.Enqueue((Encoding.UTF8.GetString(_udp.Receive(ref any)), any)); }
                catch (SocketException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        static void Pump()
        {
            while (Inbox.TryDequeue(out var m))
            {
                string reply;
                try { reply = Run(m.text.Trim()); }
                catch (Exception e) { reply = "error " + e.Message; }
                try { var b = Encoding.UTF8.GetBytes(reply); _udp?.Send(b, b.Length, m.from); } catch (SocketException) { }
            }
        }

        static string Status() =>
            "playing=" + (EditorApplication.isPlaying ? "yes" : "no") + " paused=" + (EditorApplication.isPaused ? "yes" : "no")
            + " compiling=" + (EditorApplication.isCompiling ? "yes" : "no") + " virtualpad=" + (EditorPrefs.GetBool(PadPref, false) ? "on" : "off")
            + " args=" + EditorPrefs.GetString(ArgsPref, "");

        static string Run(string line)
        {
            var p = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return "error empty";
            string rest = p.Length > 1 ? p[1].Trim() : "";
            switch (p[0].ToLowerInvariant())
            {
                case "status": return Status();
                case "play":
                    if (EditorApplication.isCompiling) return "error compiling, try again";
                    EditorApplication.isPlaying = true;
                    return "ok entering play mode";
                case "stop": EditorApplication.isPlaying = false; return "ok leaving play mode";
                case "pause": EditorApplication.isPaused = true; return "ok";
                case "unpause": EditorApplication.isPaused = false; return "ok";
                case "virtualpad":
                    EditorPrefs.SetBool(PadPref, rest == "on");
                    return "ok virtualpad=" + (rest == "on" ? "on" : "off") + " (applies to the next Play session)";
                case "args":
                    EditorPrefs.SetString(ArgsPref, rest == "clear" ? "" : rest);
                    return "ok args=" + EditorPrefs.GetString(ArgsPref, "") + " (applies to the next Play session)";
                case "refresh":
                    if (EditorApplication.isPlaying) return "error stop Play mode first (a recompile ends the session)";
                    AssetDatabase.Refresh();
                    return "ok refreshing";
                case "quit":
                    EditorApplication.delayCall += () => EditorApplication.Exit(0);
                    return "ok quitting the editor (unsaved changes are lost)";
                case "focus":
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                    return "ok";
                default: return "error unknown command: " + p[0];
            }
        }
    }
}
