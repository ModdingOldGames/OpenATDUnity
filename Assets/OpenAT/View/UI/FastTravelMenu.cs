using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenAT.View.UI
{
    /// <summary>
    /// Gamepad fast travel (OpenAT, <see cref="Sim.GameOptions.GamepadSupport"/>): the fast-travel button (Triangle / Y by
    /// default, <see cref="PadBindings"/>) opens a radial menu of the places
    /// the walk-to-room hotkeys reach (the language's hotkey table, names from the game's own tooltips). The left
    /// stick, the right stick or the d-pad points at a place; Cross sends the figure running there (it leaves the
    /// current room first, exactly like the hotkey pressed twice); Circle or the fast-travel button closes the menu. While it is open
    /// the game time holds and the screens get no input. Offered only when the room hotkeys are allowed.
    /// </summary>
    public sealed class FastTravelMenu
    {
        public bool IsOpen { get; private set; }

        readonly GameApp _app;
        readonly List<(int target, string name)> _entries = new List<(int, string)>();
        int _sel;
        float _openedAt, _noteUntil;
        string _note;
        Vector2Int _dpadPrev;

        public FastTravelMenu(GameApp app) => _app = app;

        /// <summary>For the virtual pad's state: the selected entry while open.</summary>
        public string StateText => IsOpen && _entries.Count > 0 ? _sel + ":" + _entries[_sel].name.Replace(' ', '_') : "closed";

        // Places in the menu, clockwise from the top (only those in the language's hotkey table are shown).
        static readonly int[] Order =
        {
            RoomHotkeys.OwnOffice, RoomIds.Globe, RoomIds.Reisebuero, RoomIds.LastMinute, Sim.Hall.RoomIds.Fracht, Sim.Hall.RoomIds.Bank,
            Sim.Hall.RoomIds.Makler, Sim.Hall.RoomIds.Werkstatt, Sim.Hall.RoomIds.ArabAir, Sim.Hall.RoomIds.RouteBox, RoomIds.Werbung,
            RoomIds.Aufsicht, RoomHotkeys.OwnPersonnel, Sim.Hall.RoomIds.Kiosk, Sim.Hall.RoomIds.Museum, Sim.Hall.RoomIds.Ricks,
            Sim.Hall.RoomIds.Shop1, Sim.Hall.RoomIds.Designer, Sim.Hall.RoomIds.Security, Sim.Hall.RoomIds.Nasa, RoomHotkeys.Telescope,
        };

        /// <summary>Opens the menu when fast travel is possible now; true when it opened.</summary>
        public bool TryOpen()
        {
            var w = _app.Session?.World;
            if (w == null || !w.Options.GamepadSupport || !RoomHotkeys.KeysAllowed(_app, w, false)) return false;
            var top = _app.Screens.Top;
            if (top is RoomScreen rs && (rs.IsDialogOpen || rs.Menus != null && rs.Menus.IsOpen)) return false;
            if (!(top is RoomScreen) && !(top is Airport.AirportScreen)) return false;
            BuildEntries();
            if (_entries.Count == 0) return false;
            IsOpen = true;
            _openedAt = Time.unscaledTime;
            _note = null;
            if (_app.Session != null) _app.Session.HoldTime = true;
            _app.Audio.Play("SOUND/click.raw");
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_app.Session != null) _app.Session.HoldTime = false;
        }

        void BuildEntries()
        {
            _entries.Clear();
            var layout = RoomHotkeys.LayoutOf(_app.Assets.Language);
            var available = new HashSet<int>(RoomHotkeys.Targets(layout));
            var w = _app.Session.World;
            foreach (int t in Order)
            {
                if (!available.Contains(t)) continue;
                string name = Name(t, w);
                if (!string.IsNullOrEmpty(name)) _entries.Add((t, name));
            }
            if (_sel >= _entries.Count) _sel = 0;
        }

        string Name(int target, Sim.World w)
        {
            bool de = _app.Assets.Language == TextResource.Language.German;
            int room = target;
            if (target == RoomHotkeys.OwnOffice) room = RoomIds.OfficeOf(w.LocalPlayer);
            else if (target == RoomHotkeys.OwnPersonnel) room = RoomIds.PersonalOf(w.LocalPlayer);
            else if (target == RoomIds.Globe) return de ? "Planer (Globus)" : "Planner (globe)";
            else if (target == RoomHotkeys.Telescope) return de ? "Fernrohr" : "Telescope";
            string t = _app.Assets.StdText.Get("Tool", 5000 + room);
            if (t == null) return null;
            if (t.Contains("%s")) t = t.Replace("%s", w.Local.Airline ?? "");
            return t.Trim();
        }

        /// <summary>Takes the frame's input while open (the screens then see none).</summary>
        public void Update(InputState input)
        {
            if (!IsOpen) return;
            var g = Gamepad.current;
            if (g != null && _entries.Count > 0)
            {
                // A stick pointing somewhere selects the entry in that direction.
                var v = g.leftStick.ReadValue();
                if (v.magnitude < 0.5f) v = g.rightStick.ReadValue();
                if (v.magnitude >= 0.5f)
                {
                    float a = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;   // 0 = up, clockwise
                    if (a < 0) a += 360;
                    _sel = Mathf.RoundToInt(a / (360f / _entries.Count)) % _entries.Count;
                }
                // D-pad: right/down = next clockwise, left/up = previous.
                var d = g.dpad.ReadValue();
                var dir = new Vector2Int(d.x > 0.5f ? 1 : d.x < -0.5f ? -1 : 0, d.y > 0.5f ? 1 : d.y < -0.5f ? -1 : 0);
                if (dir != _dpadPrev && dir != Vector2Int.zero)
                {
                    int step = dir.x > 0 || dir.y < 0 ? 1 : -1;
                    _sel = (_sel + step + _entries.Count) % _entries.Count;
                    _app.Audio.Play("SOUND/change.raw");
                }
                _dpadPrev = dir;
                if (PadBindings.Pressed(g, PadAction.Click)) Go();
                else if (PadBindings.Pressed(g, PadAction.Back) || PadBindings.Pressed(g, PadAction.FastTravel) && Time.unscaledTime - _openedAt > 0.1f) Close();
            }
            if (input.Escape) Close();
            // Nothing below reacts while the menu is (or just was) open.
            input.LeftDown = input.LeftHeld = input.LeftUp = input.RightDown = input.RightHeld = input.RightUp = false;
            input.Escape = input.Enter = input.Tab = input.Space = false;
            input.LeftPressed = input.RightPressed = input.UpPressed = input.DownPressed = false;
            input.Left = input.Right = input.Up = input.Down = false;
            input.Typed.Clear();
            input.Wheel = 0;
            input.PadShoulder = 0;
            input.Consume();
        }

        void Go()
        {
            var (target, name) = _entries[_sel];
            Close();
            if (RoomHotkeys.Travel(_app, target, true)) _app.Audio.Play("SOUND/click.raw");
            else
            {
                _note = name;
                _noteUntil = Time.unscaledTime + 1.5f;
            }
        }

        /// <summary>The menu: a ring of places around the selected one's name, in the game's fonts.</summary>
        public void Draw(Screen2D s, UiKit kit)
        {
            if (!IsOpen)
            {
                if (_note != null && Time.unscaledTime < _noteUntil)
                    kit.SmallRed?.Draw(s, s.Width / 2, 200, _note + (_app.Assets.Language == TextResource.Language.German ? ": jetzt nicht erreichbar" : ": not reachable now"), TextAlign.Center);
                return;
            }
            int cx = s.Width / 2, cy = 210, n = _entries.Count;
            float rx = Mathf.Min(310, s.Width / 2 - 70), ry = 172;
            s.FillRect(new Rect(0, 0, s.Width, s.Height), new Color32(0, 0, 0, 170));
            // Ring.
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2 / 96;
                s.FillRect(new Rect(cx + Mathf.Sin(a) * (rx - 40) - 1, cy - Mathf.Cos(a) * (ry - 28) - 1, 2, 2), new Color32(120, 140, 170, 160));
            }
            var font = kit.SmallBlack;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                int x = cx + Mathf.RoundToInt(Mathf.Sin(a) * rx), y = cy - Mathf.RoundToInt(Mathf.Cos(a) * ry);
                string label = Short(_entries[i].name);
                int w = (font?.Measure(label) ?? 60) + 8;
                bool sel = i == _sel;
                var box = new RectInt(x - w / 2, y - 7, w, 14);
                s.FillRect(new RectInt(box.x - 1, box.y - 1, box.width + 2, box.height + 2), sel ? new Color32(255, 230, 120, 255) : new Color32(20, 22, 28, 255));
                s.FillRect(box, sel ? new Color32(255, 246, 200, 255) : new Color32(222, 214, 190, 255));
                font?.Draw(s, x, y - 4, label, TextAlign.Center);
                if (sel)
                {
                    // Pointer from the centre towards the selection.
                    for (int k = 4; k < 18; k++)
                        s.FillRect(new Rect(cx + Mathf.Sin(a) * (rx - 40) * k / 18f - 1.5f, cy - Mathf.Cos(a) * (ry - 28) * k / 18f - 1.5f, 3, 3), new Color32(255, 230, 120, 220));
                }
            }
            // Centre plate: the selected place and the buttons.
            var big = kit.NormalGreen ?? kit.SmallBlack;
            int pw = Mathf.Max(170, (big?.Measure(_entries[_sel].name) ?? 150) + 24);
            s.FillRect(new RectInt(cx - pw / 2 - 1, cy - 15, pw + 2, 42), new Color32(255, 230, 120, 255));
            s.FillRect(new RectInt(cx - pw / 2, cy - 14, pw, 40), new Color32(16, 20, 30, 255));
            big?.Draw(s, cx, cy - 8, _entries[_sel].name, TextAlign.Center);
            bool de = _app.Assets.Language == TextResource.Language.German;
            string go = PadBindings.Name(PadBindings.Get(PadAction.Click)), back = PadBindings.Name(PadBindings.Get(PadAction.Back));
            kit.SmallGrey?.Draw(s, cx, cy + 10, de ? go + ": hinlaufen   " + back + ": zurueck" : go + ": run there   " + back + ": back", TextAlign.Center);
        }

        static string Short(string t) => t.Length > 15 ? t.Substring(0, 14) + "." : t;
    }
}
