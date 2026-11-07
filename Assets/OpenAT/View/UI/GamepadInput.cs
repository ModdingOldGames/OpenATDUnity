using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenAT.View.UI
{
    /// <summary>
    /// Gamepad play (OpenAT, <see cref="Sim.GameOptions.GamepadSupport"/>), for the Steam Deck, the Legion Go and any
    /// Xbox or PlayStation pad. The pad drives the same <see cref="InputState"/> the mouse and keyboard fill, so every
    /// screen works with it unchanged:
    /// <list type="bullet">
    /// <item>right stick: the cursor, with an acceleration curve, a dead zone, slower over a hotspot, and a pull onto
    /// the nearest target (<see cref="PadTargets"/>) when the stick is let go;</item>
    /// <item>d-pad: jumps the cursor to the next target in that direction (held: repeats); without targets it acts as
    /// the arrow keys;</item>
    /// <item>LB / RB: <see cref="InputState.PadShoulder"/> for the screen (the planner steps through the planes);
    /// when no screen takes it, the cursor cycles through the targets in reading order;</item>
    /// <item>Buttons through <see cref="PadBindings"/> (rebindable in the options); defaults: A click (held: drag), B back
    /// (right button), Start options (Esc), Y the fast-travel menu (<see cref="FastTravelMenu"/>), X walk to the planner,
    /// Select (Share) end the day (Tab), R3 tooltip (F1), LT / RT scroll (wheel), touchpad cursor home, Y / X: walk to the planner / the own office (room hotkeys),
    /// DualShock touchpad press: cursor to the screen's home target (<see cref="PadTargets.Home"/>) or the middle;</item>
    /// <item>left stick: <see cref="Walk"/>, read by the hall to walk the figure (<see cref="Sim.Hall.StickWalkCommand"/>).</item>
    /// </list>
    /// Whatever was used last wins: moving the mouse or pressing a key hands the cursor back to the mouse.
    /// </summary>
    public sealed class GamepadInput
    {
        const float StickDeadZone = 0.18f, WalkDeadZone = 0.2f;
        /// <summary>Cursor speed at full tilt, canvas pixels per second; slower over a hotspot.</summary>
        const float CursorSpeed = 520f, HotspotSlowdown = 0.45f;
        /// <summary>Pull onto a target when the stick is let go within this many pixels of it.</summary>
        const int SnapRange = 14;
        /// <summary>D-pad navigation: extra distance for targets outside the current target's row (or column).</summary>
        const float OffBeamPenalty = 40f;
        /// <summary>D-pad navigation: extra distance for status-bar targets, so the d-pad does not drift into the bar.</summary>
        const float BarPenalty = 260f;
        const float RepeatDelay = 0.38f, RepeatRate = 0.085f, WheelRate = 0.12f;

        /// <summary>The pad was the last device used: the cursor and buttons come from it.</summary>
        public bool Active { get; private set; }
        /// <summary>Left stick after the dead zone: x right, y down, length 0..1 (zero when not <see cref="Active"/>).</summary>
        public Vector2 Walk { get; private set; }
        /// <summary>Hotkey letters for Y and X (set by the room framework: planner, own office), 0 = none.</summary>
        public System.Func<char> PlannerKey, OfficeKey;

        Vector2 _cursor;
        /// <summary>The real mouse position of the previous frame (not the pad cursor).</summary>
        Vector2Int _lastMouse = new Vector2Int(int.MinValue, 0), _written = new Vector2Int(int.MinValue, 0);
        bool _aiming, _mouseSeen, _mute;
        System.Action<PadButton> _listen;

        /// <summary>Rebinding: the next gamepad button pressed goes to <paramref name="done"/> and does nothing else.</summary>
        public void ListenForButton(System.Action<PadButton> done) => _listen = done;
        public bool Listening => _listen != null;
        public void CancelListen() => _listen = null;
        object _modal, _screen;
        Vector2Int _repeatDir;
        float _repeatAt, _wheelAt;
        int _pendingShoulder;

        /// <summary>
        /// Reads the pad and merges it into <paramref name="input"/> (already captured from mouse and keyboard). Uses the
        /// targets of the previous frame. <paramref name="hot"/>: the cursor look of the previous frame was not the normal one.
        /// </summary>
        public void Apply(InputState input, PadTargets targets, int screenW, int screenH, bool hot, bool enabled, float dt)
        {
            int shoulder = _pendingShoulder;
            _pendingShoulder = 0;
            var g = Gamepad.current;
            if (!enabled || g == null) { Active = false; Walk = Vector2.zero; return; }

            // Mouse or keyboard used: they take over.
            bool mouseUsed = input.Mouse != _lastMouse && input.Mouse != _written && _lastMouse.x != int.MinValue;
            _lastMouse = input.Mouse;
            if (mouseUsed || input.LeftDown || input.RightDown || input.Wheel != 0 || input.Typed.Count > 0 || input.Escape || input.Enter)
            {
                Active = false;
                _cursor = input.Mouse;
                if (mouseUsed || input.LeftDown || input.RightDown) _mouseSeen = true;
            }

            var rs = DeadZone(g.rightStick.ReadValue(), StickDeadZone);
            var ls = DeadZone(g.leftStick.ReadValue(), WalkDeadZone);
            var dpad = g.dpad.ReadValue();
            bool any = rs != Vector2.zero || ls != Vector2.zero || dpad != Vector2.zero || AnyButton(g)
                       || g.leftTrigger.ReadValue() > 0.3f || g.rightTrigger.ReadValue() > 0.3f;
            // The pad cursor starts where the mouse was, or in the middle of the screen when the mouse has not been used.
            bool justActivated = any && !Active;
            if (any && !Active)
            {
                Active = true;
                _cursor = _mouseSeen && input.Mouse.x >= 0 ? (Vector2)input.Mouse
                    : targets.Home.HasValue ? (Vector2)targets.Home.Value : new Vector2(screenW / 2, screenH / 2);
            }
            // A new screen (room, menu page): the cursor goes to its home target, when it has one.
            if (targets.ScreenKey != null && !ReferenceEquals(targets.ScreenKey, _screen))
            {
                if (_screen != null && targets.Home.HasValue) _cursor = targets.Home.Value;
                _screen = targets.ScreenKey;
            }
            // DualShock touchpad press: to the screen's home target (e.g. the planner's first plane), else the middle.
            if (!_mute && PadBindings.Pressed(g, PadAction.Home))
                _cursor = targets.Home.HasValue ? (Vector2)targets.Home.Value : new Vector2(screenW / 2, screenH / 2);
            if (!Active) { Walk = Vector2.zero; return; }

            Walk = new Vector2(ls.x, -ls.y);

            // Right stick: the cursor.
            if (rs != Vector2.zero)
            {
                float m = rs.magnitude;
                float speed = CursorSpeed * m * m * (hot && m < 0.85f ? HotspotSlowdown : 1f);
                _cursor += new Vector2(rs.x, -rs.y) / m * speed * Mathf.Min(dt, 0.1f);
                _aiming = true;
            }
            else if (_aiming)
            {
                _aiming = false;
                Snap(targets);
            }

            // D-pad: next target in that direction, with auto-repeat.
            var dir = new Vector2Int(dpad.x > 0.5f ? 1 : dpad.x < -0.5f ? -1 : 0, dpad.y > 0.5f ? -1 : dpad.y < -0.5f ? 1 : 0);
            if (dir != Vector2Int.zero)
            {
                bool fire = dir != _repeatDir || Time.unscaledTime >= _repeatAt;
                // The press that wakes the pad up only puts the cursor on the screen's home target.
                if (fire && justActivated && targets.Home.HasValue) { fire = false; _repeatDir = dir; _repeatAt = Time.unscaledTime + RepeatDelay; }
                if (fire)
                {
                    _repeatAt = Time.unscaledTime + (dir != _repeatDir ? RepeatDelay : RepeatRate);
                    _repeatDir = dir;
                    if (targets.Rects.Count > 0) Navigate(targets, dir);
                    else
                    {
                        input.LeftPressed |= dir.x < 0; input.RightPressed |= dir.x > 0;
                        input.UpPressed |= dir.y < 0; input.DownPressed |= dir.y > 0;
                    }
                }
                if (targets.Rects.Count == 0)
                {
                    input.Left |= dir.x < 0; input.Right |= dir.x > 0; input.Up |= dir.y < 0; input.Down |= dir.y > 0;
                }
            }
            else _repeatDir = Vector2Int.zero;

            // Shoulders: offered to the screens this frame; cycled next frame when nobody took them.
            if (shoulder != 0 && targets.Rects.Count > 0) Cycle(targets, shoulder);
            int sh = _mute ? 0 : (PadBindings.Pressed(g, PadAction.NextTarget) ? 1 : 0) - (PadBindings.Pressed(g, PadAction.PrevTarget) ? 1 : 0);
            input.PadShoulder = sh;

            // A popup that just opened (or new dialog answers): the cursor goes to its first option.
            if (targets.ModalKey != null && !ReferenceEquals(targets.ModalKey, _modal))
            {
                int first = FirstInReadingOrder(targets);
                if (first >= 0) _cursor = Center(targets.Rects[first]);
            }
            _modal = targets.ModalKey;

            _cursor.x = Mathf.Clamp(_cursor.x, 0, screenW - 1);
            _cursor.y = Mathf.Clamp(_cursor.y, 0, screenH - 1);
            input.Mouse = _written = new Vector2Int(Mathf.RoundToInt(_cursor.x), Mathf.RoundToInt(_cursor.y));
            input.PadActive = true;

            // Buttons, through the bindings (PadBindings; Options > Other > OpenAT extras > Gamepad buttons).
            if (_listen != null)
            {
                // Rebinding: the next button pressed is the answer; it does nothing else.
                var pressed = PadBindings.FirstPressed(g);
                if (pressed != PadButton.None) { var done = _listen; _listen = null; _mute = true; done(pressed); }
                return;
            }
            if (_mute) { if (AnyButton(g)) return; _mute = false; }   // until the rebinding press is let go
            input.LeftDown |= PadBindings.Pressed(g, PadAction.Click); input.LeftHeld |= PadBindings.Held(g, PadAction.Click);
            input.LeftUp |= PadBindings.Released(g, PadAction.Click);
            input.RightDown |= PadBindings.Pressed(g, PadAction.Back); input.RightHeld |= PadBindings.Held(g, PadAction.Back);
            input.RightUp |= PadBindings.Released(g, PadAction.Back);
            input.Escape |= PadBindings.Pressed(g, PadAction.Options);
            input.PadFastTravel |= PadBindings.Pressed(g, PadAction.FastTravel);
            input.Tab |= PadBindings.Pressed(g, PadAction.EndDay);
            input.F1 |= PadBindings.Pressed(g, PadAction.Tooltip);
            if (PadBindings.Pressed(g, PadAction.WalkPlanner)) Hotkey(input, PlannerKey);
            if (PadBindings.Pressed(g, PadAction.WalkOffice)) Hotkey(input, OfficeKey);
            float wheel = PadBindings.Value(g, PadAction.ScrollDown) - PadBindings.Value(g, PadAction.ScrollUp);
            if (Mathf.Abs(wheel) > 0.3f)
            {
                if (Time.unscaledTime >= _wheelAt) { input.Wheel += wheel > 0 ? -1 : 1; _wheelAt = Time.unscaledTime + WheelRate / Mathf.Abs(wheel); }
            }
            else _wheelAt = 0;
        }

        /// <summary>Puts the pad cursor somewhere (a screen guiding the next step, e.g. the planner after taking a post-it).</summary>
        public void Warp(Vector2Int p) { if (Active) _cursor = p; }

        /// <summary>The screens left the shoulder press of this frame untouched: cycle the targets next frame.</summary>
        public void EndFrame(InputState input)
        {
            if (input.PadShoulder != 0) _pendingShoulder = input.PadShoulder;
        }

        static void Hotkey(InputState input, System.Func<char> key)
        {
            char c = key?.Invoke() ?? '\0';
            if (c != '\0') input.Typed.Add(c);
        }

        static bool AnyButton(Gamepad g) =>
            g.buttonSouth.isPressed || g.buttonEast.isPressed || g.buttonNorth.isPressed || g.buttonWest.isPressed
            || g.leftShoulder.isPressed || g.rightShoulder.isPressed || g.startButton.isPressed || g.selectButton.isPressed
            || g.leftStickButton.isPressed || g.rightStickButton.isPressed
            || (g is UnityEngine.InputSystem.DualShock.DualShockGamepad d && d.touchpadButton.isPressed);

        static Vector2 DeadZone(Vector2 v, float dz)
        {
            float m = v.magnitude;
            if (m < dz) return Vector2.zero;
            return v / m * Mathf.Min(1f, (m - dz) / (1f - dz));
        }

        static Vector2 Center(RectInt r) => new Vector2(r.x + (r.width - 1) / 2f, r.y + (r.height - 1) / 2f);

        /// <summary>Pulls a cursor that stopped near a target onto it (not when it already is on one).</summary>
        void Snap(PadTargets t)
        {
            var p = Vector2Int.RoundToInt(_cursor);
            if (t.At(p) >= 0) return;
            float best = SnapRange * SnapRange + 1;
            Vector2 to = _cursor;
            foreach (var r in t.Rects)
            {
                var q = new Vector2(Mathf.Clamp(_cursor.x, r.xMin, r.xMax - 1), Mathf.Clamp(_cursor.y, r.yMin, r.yMax - 1));
                float d = (q - _cursor).sqrMagnitude;
                if (d < best) { best = d; to = Inside(r, q); }
            }
            _cursor = to;
        }

        /// <summary>A point of a rect near <paramref name="q"/>, a few pixels in from its edges (the centre of small rects).</summary>
        static Vector2 Inside(RectInt r, Vector2 q)
        {
            var c = Center(r);
            float mx = Mathf.Min(3, (r.width - 1) / 2f), my = Mathf.Min(3, (r.height - 1) / 2f);
            return new Vector2(Mathf.Clamp(q.x, r.xMin + mx, r.xMax - 1 - mx), Mathf.Clamp(q.y, r.yMin + my, r.yMax - 1 - my));
        }

        /// <summary>Moves the cursor to the best target in a direction: ahead of the cursor, near its line.</summary>
        void Navigate(PadTargets t, Vector2Int dir)
        {
            var p = Vector2Int.RoundToInt(_cursor);
            int cur = t.At(p);
            var from = cur >= 0 ? Center(t.Rects[cur]) : _cursor;
            var d = (Vector2)dir;
            int best = -1;
            float bestScore = float.MaxValue;
            // The "beam": the current target's extent across the direction of travel (rows for left/right, columns for up/down).
            float b0, b1;
            if (cur >= 0) { var cr = t.Rects[cur]; b0 = dir.x != 0 ? cr.yMin : cr.xMin; b1 = dir.x != 0 ? cr.yMax : cr.xMax; }
            else { b0 = dir.x != 0 ? _cursor.y : _cursor.x; b1 = b0 + 1; }
            for (int i = 0; i < t.Rects.Count; i++)
            {
                if (i == cur || !t.Usable(i)) continue;
                // From the room, only Down goes into the status bar.
                if (t.IsBar(i) && dir.y <= 0 && (cur < 0 || !t.IsBar(cur))) continue;
                var r = t.Rects[i];
                if (cur >= 0 && t.Rects[cur].Overlaps(r) && Contains(r, t.Rects[cur])) continue;   // a larger rect around the current one
                // Nearest point of the target to the line through the start.
                var q = new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax - 1), Mathf.Clamp(from.y, r.yMin, r.yMax - 1));
                var v = q - from;
                float along = Vector2.Dot(v, d);
                if (along < 1f) { v = Center(r) - from; along = Vector2.Dot(v, d); if (along < 1f) continue; }
                float across = Mathf.Abs(v.x * d.y - v.y * d.x);
                float score = along + across * 3f;
                // Targets overlapping the beam are preferred: the others pay a fixed penalty, so a near target one row
                // off still beats a far one in the same row.
                float r0 = dir.x != 0 ? r.yMin : r.xMin, r1 = dir.x != 0 ? r.yMax : r.xMax;
                if (!(r0 < b1 && r1 > b0)) score += OffBeamPenalty;
                if (t.IsBar(i) && (cur < 0 || !t.IsBar(cur))) score += BarPenalty;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) _cursor = Settle(t, Center(t.Rects[best]));
        }

        /// <summary>
        /// A point a jump lands on: when a smaller target lies under it (a flight inside a day row), the centre of that
        /// one, so the focused target and the clicked point always agree (an edge would start an edge drag).
        /// </summary>
        static Vector2 Settle(PadTargets t, Vector2 p)
        {
            int i = t.At(Vector2Int.RoundToInt(p));
            return i >= 0 ? Center(t.Rects[i]) : p;
        }

        static bool Contains(RectInt outer, RectInt inner) =>
            inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;

        /// <summary>The first usable target, top to bottom and left to right (status bar last), or −1.</summary>
        static int FirstInReadingOrder(PadTargets t)
        {
            int best = -1;
            for (int i = 0; i < t.Rects.Count; i++)
            {
                if (!t.Usable(i) || t.IsBar(i)) continue;
                if (best < 0) { best = i; continue; }
                RectInt a = t.Rects[i], b = t.Rects[best];
                if (a.y < b.y - 4 || System.Math.Abs(a.y - b.y) <= 4 && a.x < b.x) best = i;
            }
            return best;
        }

        /// <summary>LB/RB: the previous/next target in reading order (top to bottom, left to right).</summary>
        void Cycle(PadTargets t, int step)
        {
            var order = new List<int>();
            for (int i = 0; i < t.Rects.Count; i++) if (t.Usable(i)) order.Add(i);
            if (order.Count == 0) return;
            order.Sort((x, y) =>
            {
                var a = t.Rects[x]; var b = t.Rects[y];
                int ay = a.y + a.height / 2, by = b.y + b.height / 2;
                if (System.Math.Abs(ay - by) > 4) return ay.CompareTo(by);
                return a.x.CompareTo(b.x);
            });
            int cur = t.At(Vector2Int.RoundToInt(_cursor));
            int k = cur >= 0 ? order.IndexOf(cur) : (step > 0 ? -1 : 0);
            k = ((k + step) % order.Count + order.Count) % order.Count;
            _cursor = Settle(t, Center(t.Rects[order[k]]));
        }
    }
}
