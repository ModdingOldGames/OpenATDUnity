using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>Playback mode of a <see cref="FrameAnimation"/> (rooms-and-ui-8 §0.4).</summary>
    public enum AnimMode { Once, Repeat, Random, Never }

    /// <summary>
    /// A simple bitmap-sequence animation of a room ("CAnimation", rooms-and-ui-8 §0.4): frames
    /// PREFIX01..NN (or a custom first index) from the room GLI, a prelude before the first cycle, a
    /// speed (ticks per frame), a wait between cycles and an optional sound per cycle. Times are in
    /// ticks of the original's real-time ticker, 32 ms each (sim-core "TickerTime", rooms-and-ui-8 §0.4).
    /// </summary>
    public sealed class FrameAnimation
    {
        /// <summary>Length of one animation tick in seconds (the original ticker counts real milliseconds / 32).</summary>
        public const float TickSeconds = 0.032f;

        readonly string _lib, _sound;
        string _prefix;
        readonly int _count, _first, _digits;
        readonly AnimMode _mode;
        readonly bool _showFirst;
        readonly float _speed, _cyclicWait;
        readonly System.Random _rng = new System.Random();
        float _timer;
        int _frame = -1;
        bool _running;

        public FrameAnimation(string lib, string prefix, int count, AnimMode mode, int prelude, int speed, int cyclicWait = 0,
            string sound = null, bool showFirst = true, int first = 1, int digits = 2)
        {
            _lib = lib; _prefix = prefix; _count = count; _mode = mode; _speed = Mathf.Max(1, speed) * TickSeconds;
            _cyclicWait = cyclicWait * TickSeconds; _sound = sound; _showFirst = showFirst; _first = first; _digits = digits;
            _timer = prelude * TickSeconds;
            _frame = showFirst ? 0 : -1;
        }

        /// <summary>Current frame index (0-based, −1 = nothing shown); a finished ONCE animation keeps its last frame.</summary>
        public int Frame => _frame;

        /// <summary>
        /// Swaps the bitmap set without restarting the timing (the original's re-size with the old counter start),
        /// e.g. the broker's wall flap once the bra was taken.
        /// </summary>
        public string Prefix { get => _prefix; set => _prefix = value; }

        /// <summary>Restarts the cycle now (easter-egg clicks, NEVER-mode triggers).</summary>
        public void Start(GameAudio audio)
        {
            _running = true;
            _frame = 0;
            _timer = _speed;
            if (_sound != null) audio?.Play("SOUND/" + _sound);
        }

        public void Update(float dt, GameAudio audio)
        {
            if (_mode == AnimMode.Never && !_running) return;
            _timer -= dt;
            if (_timer > 0) return;
            if (_mode == AnimMode.Random)
            {
                _frame = _rng.Next(_count);
                _timer += _speed;
                return;
            }
            if (!_running)
            {
                Start(audio);
                return;
            }
            _frame++;
            _timer += _speed;
            if (_frame < _count) return;
            _running = false;
            // ONCE stays on its last frame (the original's GetFrame clamps to the last phase); the others rewind.
            if (_mode == AnimMode.Once) { _frame = _count - 1; _timer = float.MaxValue; return; }
            _frame = _showFirst ? 0 : -1;
            if (_mode == AnimMode.Repeat) _timer = Mathf.Max(_speed, _cyclicWait);
            else if (_mode == AnimMode.Never) _timer = float.MaxValue;
        }

        public void Draw(Screen2D s, GameAssets assets, int x, int y)
        {
            if (_frame < 0) return;
            string name = _prefix + (_first + _frame).ToString(new string('0', _digits));
            if (!assets.Has(_lib, name)) return;
            s.Draw(assets.Texture(_lib, name), x, y);
        }
    }
}
