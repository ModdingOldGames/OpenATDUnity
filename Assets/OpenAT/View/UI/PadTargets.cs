using System.Collections.Generic;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>
    /// Gamepad targets (OpenAT, <see cref="Sim.GameOptions.GamepadSupport"/>): the clickable rects of the current
    /// frame, in canvas coordinates. Screens add them while they test the mouse (room hotspots, menu lines, dialog
    /// answers, planner rows and grid cells, ...); <see cref="GamepadInput"/> uses the rects of the previous frame to
    /// pull the stick cursor onto a target, to step between targets with the d-pad and to cycle them with the
    /// shoulder buttons. Rects that cover most of the screen are not targets.
    /// </summary>
    public sealed class PadTargets
    {
        readonly List<RectInt> _rects = new List<RectInt>();
        readonly List<bool> _bar = new List<bool>();
        int _maxArea = 640 * 480 / 4;

        /// <summary>The open popup (menu, dialog answers, calculator) this frame, or null. While one is open the d-pad stays on
        /// its targets (not the status bar), and when a new one opens the pad cursor goes to its first option.</summary>
        public object ModalKey;
        /// <summary>The screen that reported the targets; when it changes, the pad cursor goes to its <see cref="Home"/>.</summary>
        public object ScreenKey;

        /// <summary>True for a status-bar target (index as <see cref="Rects"/>): the d-pad goes there only on purpose.</summary>
        public bool IsBar(int i) => _bar[i];

        public IReadOnlyList<RectInt> Rects => _rects;

        /// <summary>The screen's starting point for the d-pad (the touchpad jumps there), or null: the middle of the screen.</summary>
        public Vector2Int? Home;

        /// <summary>Starts a frame's collection (the previous one has been used by then).</summary>
        public void Begin(int screenW, int screenH)
        {
            _rects.Clear();
            _bar.Clear();
            Home = null;
            ModalKey = null;
            ScreenKey = null;
            _maxArea = screenW * screenH / 4;
        }

        public void Add(RectInt r) => Add(r, false);

        public void Add(RectInt r, bool bar)
        {
            if (r.width <= 0 || r.height <= 0 || r.width * r.height > _maxArea) return;
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Equals(r)) return;
            _rects.Add(r);
            _bar.Add(bar);
        }

        /// <summary>A status-bar target.</summary>
        public void AddBar(int x1, int y1, int x2, int y2) => Add(new RectInt(x1, y1, x2 - x1 + 1, y2 - y1 + 1), true);

        /// <summary>A target the d-pad may use now (inside an open popup, the status bar is not).</summary>
        public bool Usable(int i) => ModalKey == null || !_bar[i];

        /// <summary>Adds an inclusive (x1,y1)-(x2,y2) rect, the original's notation.</summary>
        public void Add(int x1, int y1, int x2, int y2) => Add(new RectInt(x1, y1, x2 - x1 + 1, y2 - y1 + 1));

        /// <summary>Index of the smallest target containing a point, or −1.</summary>
        public int At(Vector2Int p)
        {
            int best = -1, area = int.MaxValue;
            for (int i = 0; i < _rects.Count; i++)
            {
                var r = _rects[i];
                if (!r.Contains(p)) continue;
                int a = r.width * r.height;
                if (a < area) { area = a; best = i; }
            }
            return best;
        }
    }
}
