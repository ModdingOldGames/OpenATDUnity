using System.Collections.Generic;
using OpenAT.View.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace OpenAT.View.UI
{
    /// <summary>
    /// On-screen PS4 controller (OpenAT dev tool for recordings, docs/VIRTUALPAD.md): a pixel-art DualShock 4 rendered
    /// in Blender and reduced to the game's look (tools/virtualpad/blender, sprites in Resources/PadOverlay). Every
    /// part is drawn from the values the Input System reports for <see cref="Gamepad.current"/> in this frame, the
    /// values the game reads: pressed buttons swap to their pressed sprite, the stick caps move with the sticks, the
    /// L2/R2 gauges fill with the triggers, and each stick's push is shown in percent. The light bar is blue while the
    /// pad drives the game and grey while mouse or keyboard do.
    /// Shown with the virtual pad (<see cref="Boot.VirtualPad"/>) or with <c>-openat-padoverlay</c>.
    /// </summary>
    public static class PadOverlay
    {
        /// <summary>The sprite frame (pixels at scale 1) and the extra rows for the trigger gauges and the device line.</summary>
        const int FrameW = 200, FrameH = 124, Top = 16, Bottom = 12;
        public const int W = FrameW, H = Top + FrameH + Bottom;
        /// <summary>How far a stick cap moves at full push, in sprite pixels.</summary>
        const float CapTravel = 5f;

        sealed class Sprite { public Texture2D Tex; public RectInt Rect; }
        static Dictionary<string, Sprite> _sprites;

        static readonly Color32 GaugeBack = new Color32(20, 22, 28, 255), GaugeEdge = new Color32(6, 6, 9, 255),
            GaugeFill = new Color32(124, 178, 232, 255), BarOn = new Color32(70, 140, 255, 255), BarOff = new Color32(90, 94, 104, 255);

        static Dictionary<string, Sprite> Sprites
        {
            get
            {
                if (_sprites != null) return _sprites;
                _sprites = new Dictionary<string, Sprite>();
                var index = Resources.Load<TextAsset>("PadOverlay/index");
                if (index == null) return _sprites;
                foreach (var line in index.text.Split('\n'))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length < 5) continue;
                    var data = Resources.Load<TextAsset>("PadOverlay/" + p[0]);
                    if (data == null) continue;
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "pad " + p[0] };
                    t.LoadImage(data.bytes);
                    FlipRows(t);   // PNGs load bottom-up; the renderer takes textures top-down like the game's own
                    _sprites[p[0]] = new Sprite { Tex = t, Rect = new RectInt(int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4])) };
                }
                return _sprites;
            }
        }

        static void FlipRows(Texture2D t)
        {
            var px = t.GetPixels32();
            int w = t.width, h = t.height;
            var o = new Color32[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, o, (h - 1 - y) * w, w);
            t.SetPixels32(o);
            t.Apply(false, true);
        }

        /// <summary>Draws the overlay in a corner ("tl", "tr", "bl", "br") at a scale; bottom corners keep clear of the status bar.</summary>
        public static void Draw(Screen2D s, bool padDrives, string corner, float scale, string label)
        {
            var sp = Sprites;
            if (!sp.ContainsKey("base")) return;
            float k = scale, w = W * k, h = H * k, m = 6;
            float ox = corner == "tl" || corner == "bl" ? m : s.Width - w - m;
            float oy = (corner == "tl" || corner == "tr" ? m : s.Height - StatusBar.Height - h - m) + Top * k;   // frame origin

            void Part(string name, float dx = 0, float dy = 0, Color32? tint = null)
            {
                if (!sp.TryGetValue(name, out var p)) return;
                s.DrawScaled(p.Tex, new Rect(ox + (p.Rect.x + dx) * k, oy + (p.Rect.y + dy) * k, p.Rect.width * k, p.Rect.height * k),
                    new Rect(0, 0, p.Tex.width, p.Tex.height), tint ?? new Color32(255, 255, 255, 255));
            }

            var g = Gamepad.current;
            Part("base");
            Part("lightbar", 0, 0, padDrives && g != null ? BarOn : BarOff);
            float labelY = oy + (FrameH + 1) * k;
            if (g == null)
            {
                Label(s, "no gamepad", ox + w, labelY);
                return;
            }

            var dp = g.dpad;
            if (dp.up.isPressed) Part("dup");
            if (dp.down.isPressed) Part("ddown");
            if (dp.left.isPressed) Part("dleft");
            if (dp.right.isPressed) Part("dright");
            if (g.buttonNorth.isPressed) Part("triangle");
            if (g.buttonEast.isPressed) Part("circle");
            if (g.buttonSouth.isPressed) Part("cross");
            if (g.buttonWest.isPressed) Part("square");
            if (g.leftShoulder.isPressed) Part("l1");
            if (g.rightShoulder.isPressed) Part("r1");
            if (g.selectButton.isPressed) Part("share");
            if (g.startButton.isPressed) Part("options");
            if (g is DualShockGamepad ds && ds.touchpadButton.isPressed) Part("touchpad");
            if (g is DualShock4GamepadHID d4 && d4.playStationButton.isPressed) Part("ps");

            // Stick caps move by whole sprite pixels, so they stay pixel art.
            var ls = g.leftStick.ReadValue();
            var rs = g.rightStick.ReadValue();
            Part(g.leftStickButton.isPressed ? "lcap_p" : "lcap", Mathf.Round(ls.x * CapTravel), Mathf.Round(-ls.y * CapTravel));
            Part(g.rightStickButton.isPressed ? "rcap_p" : "rcap", Mathf.Round(rs.x * CapTravel), Mathf.Round(-rs.y * CapTravel));

            // Trigger gauges above the shoulders, and how far each stick is pushed (what the game reads).
            Gauge(s, sp, "l1", g.leftTrigger.ReadValue(), ox, oy, k, false);
            Gauge(s, sp, "r1", g.rightTrigger.ReadValue(), ox, oy, k, true);
            Percent(s, sp, "lcap", ls.magnitude, ox, oy, k);
            Percent(s, sp, "rcap", rs.magnitude, ox, oy, k);

            Label(s, label, ox + w, labelY);
        }

        static void Gauge(Screen2D s, Dictionary<string, Sprite> sp, string shoulder, float v, float ox, float oy, float k, bool fromRight)
        {
            if (!sp.TryGetValue(shoulder, out var p)) return;
            float x = ox + p.Rect.x * k, y = oy + (p.Rect.y - 7) * k, gw = p.Rect.width * k, gh = 3 * k;
            s.FillRect(new Rect(x - k, y - k, gw + 2 * k, gh + 2 * k), GaugeEdge);
            s.FillRect(new Rect(x, y, gw, gh), GaugeBack);
            float fill = Mathf.Round(Mathf.Clamp01(v) * p.Rect.width) * k;
            s.FillRect(new Rect(fromRight ? x + gw - fill : x, y, fill, gh), GaugeFill);
            Print(s, Pct(v), fromRight ? x - 3 * k : x + gw + 3 * k, y - 3 * k, fromRight ? TextAlign.Right : TextAlign.Left);
        }

        static void Percent(Screen2D s, Dictionary<string, Sprite> sp, string cap, float v, float ox, float oy, float k)
        {
            if (!sp.TryGetValue(cap, out var p)) return;
            Print(s, Pct(v), ox + (p.Rect.x + p.Rect.width / 2f) * k, oy + (p.Rect.yMax + 5) * k, TextAlign.Center);
        }

        static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100) + "%";

        static void Print(Screen2D s, string text, float x, float y, TextAlign align)
        {
            var f = Boot.GameApp.Instance?.Ui?.Kit?.SmallGrey;
            f?.Draw(s, Mathf.RoundToInt(x), Mathf.RoundToInt(y), text, align);
        }

        /// <summary>The device line under the controller, right-aligned at <paramref name="right"/>.</summary>
        static void Label(Screen2D s, string text, float right, float y) => Print(s, text, right - 4, y, TextAlign.Right);
    }
}
