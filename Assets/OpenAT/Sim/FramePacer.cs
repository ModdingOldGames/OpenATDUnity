namespace OpenAT.Sim
{
    /// <summary>
    /// Evens out the real frame time fed into <see cref="SimDriver"/>. The original steps the simulation at 20 Hz
    /// and repaints after every step, so each step is on screen for the same time (airport-and-people.md §12.1).
    /// Rendering at 60 fps a step should be shown for exactly 3 frames; but the measured frame time jitters around
    /// 16.7 ms, and with the 50 ms quantum sitting exactly on a 3-frame boundary that noise lands steps 2, 3 or 4
    /// frames apart, so the figures move unevenly. The pacer snaps each typical frame time to the display's
    /// frame period. That period is learnt from the measured frames and replaced by the refresh/target period when
    /// it comes within 2 % of it (released beyond 3 %). The snapped-off difference is kept (slowly forgotten, so zero-mean noise costs no
    /// time) and handed back one whole frame at a time when it grows past a frame. Untypical frames
    /// (hitches, focus loss) pass through unchanged, so the driver's slow-frame rule still applies to them.
    /// </summary>
    public sealed class FramePacer
    {
        double _nominal, _residual, _locked;
        int _offFrames, _learnt;

        /// <summary>The nominal frame time (ms) currently used, 0 before the first frame.</summary>
        public double NominalMs => _nominal;

        /// <param name="realMs">measured duration of the last frame</param>
        /// <param name="displayMs">the display's frame period under vSync (0 = unknown)</param>
        /// <param name="targetMs">the target frame period (0 = none); both are hints, used when the measured frames match them</param>
        public double Pace(double realMs, double displayMs = 0, double targetMs = 0)
        {
            if (realMs <= 0) return 0;
            // The frame period: starts at the display/target hint, a running mean over the first 50 frames, then a
            // slow moving average. Untypical frames (hitches, focus loss, a changed rate) pass through unpaced.
            if (_nominal <= 0) { _nominal = displayMs > 0 ? displayMs : targetMs > 0 ? targetMs : realMs; _learnt = 1; }
            if (realMs < _nominal * 1.5 && realMs > _nominal * 0.5)
            {
                if (_learnt < 50) _learnt++;
                _nominal += (realMs - _nominal) / System.Math.Min(_learnt, 50);
                _offFrames = 0;
            }
            else
            {
                if (++_offFrames > 10) { _nominal = realMs; _learnt = 1; _offFrames = 0; }   // the frame rate changed for good
                return realMs;
            }
            // Lock onto the refresh or target period within 2 %, release it beyond 3 % (the learnt value is noisy).
            if (_locked > 0 && !Near(_nominal, _locked, 0.03)) _locked = 0;
            if (_locked <= 0 || (_locked != displayMs && _locked != targetMs))
                _locked = Near(_nominal, displayMs, 0.02) ? displayMs : Near(_nominal, targetMs, 0.02) ? targetMs : 0;
            double period = _locked > 0 ? _locked : _nominal;

            int k = (int)System.Math.Round(realMs / period);
            double paced = k * period;
            _residual = _residual * Forget + (realMs - paced);
            if (_residual >= period) { paced += period; _residual -= period; }
            else if (_residual <= -period) { paced -= period; _residual += period; }
            return paced;
        }

        const double Forget = 0.95;

        static bool Near(double v, double c, double tolerance) => c > 0 && System.Math.Abs(v - c) < c * tolerance;
    }
}
