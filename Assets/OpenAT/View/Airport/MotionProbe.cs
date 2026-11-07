using System;
using System.Globalization;
using System.Text;
using OpenAT.Sim.Hall;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// Test hook <c>-openat-motionlog N</c>: once the local figure walks, logs N frames of
    /// "frame dt ticks x y viewX screenX dir phase" (one line per rendered frame) to measure how the
    /// figure and the camera move per frame. Lines start with <c>OpenAT motion:</c>; a summary follows.
    /// <c>-openat-motionstress</c> imitates the editor's play mode: no vSync (only the 60 fps target), 0-18 ms of
    /// random extra work per frame (some frames overrun) and one 400 ms hitch before the measurement starts.
    /// </summary>
    static class MotionProbe
    {
        static readonly int Frames = ParseFrames();
        static int _logged, _frame, _lastScreenX = int.MinValue, _lastX = int.MinValue, _lastViewX;
        static int _startX = int.MinValue, _startY;
        static int _moves, _runFrames, _maxRun, _backSteps, _camOnlyFrames;
        static readonly int[] Gaps = new int[8];
        static readonly StringBuilder Sb = new StringBuilder();

        static int ParseFrames()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-openat-motionlog" && int.TryParse(args[i + 1], out int n)) return n;
            return 0;
        }

        public static bool Active => Frames > 0 && _logged < Frames;

        static readonly bool Stress = Array.IndexOf(Environment.GetCommandLineArgs(), "-openat-motionstress") >= 0;
        static System.Random _stressRand;
        static int _stressFrames;

        static void StressFrame()
        {
            if (_stressRand == null) { _stressRand = new System.Random(7); QualitySettings.vSyncCount = 0; }
            _stressFrames++;
            System.Threading.Thread.Sleep(_stressFrames == 30 ? 400 : _stressRand.Next(19));
        }

        /// <summary>Called once per rendered frame after the hall's camera update.</summary>
        public static void Frame(Person me, int viewX, int ticks)
        {
            if (!Active || me == null) return;
            if (Stress) StressFrame();
            if (_logged == 0)
            {
                // Start when the figure starts walking (its position changes).
                if (_startX == int.MinValue) { _startX = me.ScreenPos.X; _startY = me.ScreenPos.Y; }
                if (me.ScreenPos.X == _startX && me.ScreenPos.Y == _startY) return;
            }
            _frame++;
            int sx = me.ScreenPos.X - viewX;
            if (_lastX != int.MinValue)
            {
                bool figure = me.ScreenPos.X != _lastX, cam = viewX != _lastViewX;
                if (figure)
                {
                    if (_runFrames > 0 && _runFrames < Gaps.Length) Gaps[_runFrames]++;
                    if (_runFrames > _maxRun) _maxRun = _runFrames;
                    _moves++;
                    _runFrames = 1;
                }
                else _runFrames++;
                if (cam && !figure) _camOnlyFrames++;
                // The figure's on-screen position moving against its walking direction (camera out of step).
                int dWorld = me.ScreenPos.X - _lastX, dScreen = sx - _lastScreenX;
                if (dScreen != 0 && (dWorld == 0 || Math.Sign(dScreen) != Math.Sign(dWorld))) _backSteps++;
            }
            _lastX = me.ScreenPos.X;
            _lastViewX = viewX;
            _lastScreenX = sx;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "OpenAT motion: {0} {1:0.00} {2} {3} {4} {5} {6} {7} {8}",
                _frame, Time.unscaledDeltaTime * 1000f, ticks, me.ScreenPos.X, me.ScreenPos.Y, viewX, sx, me.LookDir, me.Phase));
            _logged++;
            if (_logged == Frames)
            {
                Sb.Clear();
                Sb.Append("OpenAT motion summary: frames ").Append(_frame).Append(", figure moves ").Append(_moves)
                  .Append(", frames per figure step histogram");
                for (int i = 1; i < Gaps.Length; i++) Sb.Append(' ').Append(i).Append(':').Append(Gaps[i]);
                Sb.Append(", longest hold ").Append(_maxRun).Append(", camera-only frames ").Append(_camOnlyFrames)
                  .Append(", screen-x jitter frames ").Append(_backSteps);
                Debug.Log(Sb.ToString());
            }
        }
    }
}
