using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Frame pacing (airport-and-people.md §12.1): the sim steps at 20 Hz; rendering at 60 fps every step must
    /// stay on screen for exactly 3 frames, even when the measured frame times jitter.
    /// </summary>
    public class FramePacingTests
    {
        const double Hz60 = 1000.0 / 60;

        static SimDriver Driver()
        {
            var w = new World(new GameData());
            w.LocalPlayer = 0;
            w.Players[0].Owner = Owner.Human;
            w.Clock.InitCalendar(0);
            w.Clock.Time = GameClock.DayStart + 60;
            return new SimDriver(w);
        }

        /// <summary>Frames between consecutive frames that ran ticks.</summary>
        static List<int> Gaps(IEnumerable<double> frames, FramePacer pacer, double display, double target)
        {
            var d = Driver();
            var gaps = new List<int>();
            int since = 0;
            foreach (double f in frames)
            {
                since++;
                double ms = pacer != null ? pacer.Pace(f, display, target) : f;
                if (d.Advance(ms) > 0) { gaps.Add(since); since = 0; }
            }
            return gaps;
        }

        /// <summary>Frame times of a display with period <paramref name="mean"/> whose measured timestamps jitter by ±jitter
        /// (a late frame is followed by an early one, as under vSync).</summary>
        static IEnumerable<double> Jittery(int n, double mean, double jitter, int seed)
        {
            var r = new Random(seed);
            double prev = 0;
            for (int i = 0; i < n; i++)
            {
                double noise = (r.NextDouble() * 2 - 1) * jitter;
                yield return mean + noise - prev;
                prev = noise;
            }
        }

        /// <summary>Frame times with independent noise (a throttled loop without vSync).</summary>
        static IEnumerable<double> Noisy(int n, double mean, double jitter, int seed)
        {
            var r = new Random(seed);
            for (int i = 0; i < n; i++) yield return mean + (r.NextDouble() * 2 - 1) * jitter;
        }

        [Fact]
        public void RawJitteryFrames_StepUnevenly()
        {
            // Without pacing, ±1.5 ms of noise around 16.67 ms moves the 50 ms quantum between frames 2, 3 and 4.
            var gaps = Gaps(Jittery(3000, Hz60, 1.5, 1), null, 0, 0);
            Assert.Contains(gaps, g => g != 3);
        }

        [Theory]
        [InlineData(0.3)]
        [InlineData(1.5)]
        [InlineData(4.0)]
        public void PacedJitteryFrames_StepEveryThirdFrame(double jitter)
        {
            var gaps = Gaps(Jittery(3000, Hz60, jitter, 2), new FramePacer(), Hz60, Hz60);
            // Past the first second every step is shown for exactly three frames.
            for (int i = 20; i < gaps.Count; i++) Assert.Equal(3, gaps[i]);
            Assert.InRange(gaps.Count, 990, 1001);
        }

        [Fact]
        public void PacedNoisyFrames_MostlyStepEveryThirdFrame()
        {
            // Independent noise lets the real time drift away from the paced time; it is handed back one frame
            // at a time, so only a few steps are shown for 2 or 4 frames.
            var raw = Gaps(Noisy(6000, Hz60, 3.0, 5), null, 0, 0);
            var paced = Gaps(Noisy(6000, Hz60, 3.0, 5), new FramePacer(), 0, Hz60);
            int Off(List<int> g) { int n = 0; foreach (int x in g) if (x != 3) n++; return n; }
            Assert.True(Off(paced) * 10 < Off(raw), "paced " + Off(paced) + " vs raw " + Off(raw));
            Assert.True(Off(paced) < paced.Count / 20);
        }

        [Fact]
        public void PacedFrames_KeepTheTotalTime()
        {
            // A display clearly slower than the target (57 fps): its own period is learnt and the paced time adds
            // up to the real time.
            var p = new FramePacer();
            double real = 0, paced = 0;
            foreach (double f in Noisy(5000, 1000.0 / 57, 2.0, 3)) { real += f; paced += p.Pace(f, 0, Hz60); }
            Assert.InRange(p.NominalMs, 17.3, 17.8);
            Assert.InRange(paced / real, 0.995, 1.005);
        }

        [Fact]
        public void NearlySixtyHz_SnapsToSixtyAndLosesLittle()
        {
            // 59.5 fps without vSync (an editor play mode holding the 60 fps target): steps stay three frames apart;
            // the game clock runs at most 2 % slow (slow frames lose time, like the original's driver).
            var p = new FramePacer();
            var d = Driver();
            int since = 0, odd = 0, steps = 0;
            string log = "";
            double real = 0, paced = 0;
            foreach (double f in Noisy(6000, 1000.0 / 59.5, 1.0, 6))
            {
                since++;
                double ms = p.Pace(f, 0, Hz60);
                real += f; paced += ms;
                if (d.Advance(ms) > 0) { if (steps++ > 20 && since != 3) { odd++; log += steps + ":" + since + "@" + p.NominalMs.ToString("0.00") + " "; } since = 0; }
            }
            Assert.True(odd == 0, log);
            Assert.InRange(paced / real, 0.98, 1.0001);
        }

        [Fact]
        public void Hitches_PassThrough()
        {
            var p = new FramePacer();
            for (int i = 0; i < 60; i++) p.Pace(Hz60, Hz60, Hz60);
            Assert.Equal(400.0, p.Pace(400.0, Hz60, Hz60));
            Assert.Equal(Hz60, p.Pace(Hz60 + 0.4, Hz60, Hz60), 6);
        }

        [Fact]
        public void LearnsAnUnknownRate()
        {
            // No hints (vSync off, no target): 144 Hz frames are learnt and snapped to their own period.
            var p = new FramePacer();
            double last = 0;
            foreach (double f in Jittery(500, 1000.0 / 144, 0.5, 4)) last = p.Pace(f);
            Assert.InRange(p.NominalMs, 6.8, 7.1);
            Assert.InRange(last, 0, 2 * 7.1);
        }

        [Fact]
        public void ThreePacedFramesMakeOneQuantum()
        {
            var d = Driver();
            int ticked = 0;
            for (int i = 0; i < 300; i++) if (d.Advance(Hz60) > 0) { ticked++; Assert.Equal(2, i % 3); }
            Assert.Equal(100, ticked);
        }
    }
}
