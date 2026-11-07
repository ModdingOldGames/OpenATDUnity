using System;

namespace OpenAT.Formats.Midi
{
    /// <summary>
    /// Tiny procedurally generated General MIDI bank used when no DLS/SF2 bank is available:
    /// one looped single-cycle waveform per GM family (16 families x 8 programs) with a
    /// family-specific envelope, plus synthesized one-shot drum sounds on the GM percussion keys.
    /// </summary>
    public static class BuiltinBank
    {
        const int CycleLength = 512;
        const int RootKey = 60;
        static readonly double RootHz = 440.0 * Math.Pow(2.0, (RootKey - 69) / 12.0);

        enum Wave { Sine, Triangle, Saw, Square, Organ, Pulse, Noise }

        // Per GM family: wave, attack, decay, sustain (dB attenuation), release.
        static readonly (Wave wave, float a, float d, float s, float r, float att)[] Families =
        {
            (Wave.Triangle, 0.002f, 1.8f, 40f, 0.25f, 3f),  // 0 piano
            (Wave.Sine,     0.001f, 0.9f, 96f, 0.30f, 2f),  // 1 chromatic percussion
            (Wave.Organ,    0.005f, 0.1f, 1f,  0.06f, 6f),  // 2 organ
            (Wave.Triangle, 0.002f, 1.2f, 50f, 0.20f, 2f),  // 3 guitar
            (Wave.Saw,      0.003f, 0.6f, 14f, 0.08f, 6f),  // 4 bass
            (Wave.Saw,      0.080f, 0.5f, 3f,  0.30f, 9f),  // 5 strings
            (Wave.Saw,      0.120f, 0.5f, 3f,  0.40f, 10f), // 6 ensemble
            (Wave.Pulse,    0.030f, 0.3f, 3f,  0.12f, 8f),  // 7 brass
            (Wave.Square,   0.020f, 0.3f, 2f,  0.10f, 9f),  // 8 reed
            (Wave.Sine,     0.030f, 0.2f, 1f,  0.15f, 4f),  // 9 pipe
            (Wave.Square,   0.005f, 0.2f, 2f,  0.10f, 10f), // 10 synth lead
            (Wave.Saw,      0.250f, 1.0f, 4f,  0.60f, 11f), // 11 synth pad
            (Wave.Triangle, 0.100f, 1.0f, 6f,  0.50f, 6f),  // 12 synth fx
            (Wave.Triangle, 0.002f, 0.8f, 60f, 0.20f, 2f),  // 13 ethnic
            (Wave.Sine,     0.001f, 0.4f, 96f, 0.20f, 6f),  // 14 percussive
            (Wave.Noise,    0.010f, 0.5f, 20f, 0.30f, 12f), // 15 sound effects
        };

        public static SoundBank Create()
        {
            var bank = new SoundBank { Kind = "Builtin", Name = "OpenAT built-in oscillators" };
            var waves = new short[7][];
            for (int w = 0; w < waves.Length; w++) waves[w] = MakeCycle((Wave)w);
            int cycleRate = (int)Math.Round(CycleLength * RootHz);

            for (int p = 0; p < 128; p++)
            {
                var f = Families[p / 8];
                var data = waves[(int)f.wave];
                var r = new Region
                {
                    Data = data, Start = 0, End = data.Length, LoopStart = 0, LoopEnd = data.Length,
                    Loop = LoopMode.Continuous, SampleRate = cycleRate, RootKey = RootKey,
                    AttackSec = f.a, DecaySec = f.d, SustainDb = f.s, ReleaseSec = f.r, AttenuationDb = f.att,
                };
                // Noise "cycle" is longer so it does not sound pitched.
                if (f.wave == Wave.Noise) r.SampleRate = 44100;
                bank.Add(new Instrument { Name = "Builtin " + p, Program = p, Regions = new[] { r } });
            }
            bank.Add(MakeDrumKit());
            return bank;
        }

        static short[] MakeCycle(Wave w)
        {
            int n = w == Wave.Noise ? 44100 : CycleLength;
            var s = new short[n];
            var rng = new Random(12345);
            for (int i = 0; i < n; i++)
            {
                double ph = (double)i / n, v = 0;
                switch (w)
                {
                    case Wave.Sine: v = Math.Sin(2 * Math.PI * ph); break;
                    case Wave.Triangle: v = Additive(ph, k => (k & 1) == 1 ? ((k / 2) % 2 == 0 ? 1.0 : -1.0) / (k * k) * 8 / (Math.PI * Math.PI) : 0, 15); break;
                    case Wave.Saw: v = Additive(ph, k => 2 / Math.PI * ((k & 1) == 1 ? 1.0 : -1.0) / k, 32) * 0.8; break;
                    case Wave.Square: v = Additive(ph, k => (k & 1) == 1 ? 4 / Math.PI / k : 0, 31) * 0.7; break;
                    case Wave.Pulse: v = Additive(ph, k => 2 / Math.PI / k * Math.Sin(Math.PI * k * 0.25) * 1.4, 32) * 0.7; break;
                    case Wave.Organ: v = (Math.Sin(2 * Math.PI * ph) + 0.6 * Math.Sin(4 * Math.PI * ph) + 0.4 * Math.Sin(6 * Math.PI * ph) + 0.25 * Math.Sin(8 * Math.PI * ph)) / 2.0; break;
                    case Wave.Noise: v = rng.NextDouble() * 2 - 1; break;
                }
                s[i] = ToShort(v * 0.9);
            }
            return s;
        }

        static double Additive(double ph, Func<int, double> amp, int harmonics)
        {
            double v = 0;
            for (int k = 1; k <= harmonics; k++) v += amp(k) * Math.Sin(2 * Math.PI * k * ph);
            return v;
        }

        static short ToShort(double v) => (short)Math.Max(-32767, Math.Min(32767, Math.Round(v * 32767)));

        // ------------------------------------------------------------------ drums

        enum Drum { Kick, Snare, ClosedHat, OpenHat, Tom, Crash, Clap, Click }

        static Instrument MakeDrumKit()
        {
            const int rate = 22050;
            var sounds = new short[8][];
            for (int i = 0; i < sounds.Length; i++) sounds[i] = MakeDrum((Drum)i, rate);
            var regions = new System.Collections.Generic.List<Region>();
            for (int key = 27; key <= 87; key++)
            {
                Drum kind; float tune = 0; int cls = 0;
                switch (key)
                {
                    case 35: case 36: kind = Drum.Kick; break;
                    case 38: case 40: kind = Drum.Snare; break;
                    case 37: case 39: kind = Drum.Clap; break;
                    case 42: case 44: kind = Drum.ClosedHat; cls = 1; break;
                    case 46: kind = Drum.OpenHat; cls = 1; break;
                    case 41: case 43: case 45: case 47: case 48: case 50:
                        kind = Drum.Tom; tune = (key - 45) * 150; break;
                    case 49: case 51: case 52: case 53: case 55: case 57: case 59: kind = Drum.Crash; break;
                    default: kind = key < 35 || key > 81 ? Drum.Click : Drum.Clap; tune = (key - 60) * 50; break;
                }
                var data = sounds[(int)kind];
                regions.Add(new Region
                {
                    LoKey = (byte)key, HiKey = (byte)key, Data = data, Start = 0, End = data.Length,
                    SampleRate = rate, RootKey = key, TuneCents = tune, ScaleTuning = 0,
                    AttackSec = 0.0005f, DecaySec = 20f, SustainDb = 0, ReleaseSec = 2f, AttenuationDb = 2f,
                    ExclusiveClass = cls, Loop = LoopMode.None,
                });
            }
            return new Instrument { Name = "Builtin drums", IsDrumKit = true, Program = 0, Regions = regions.ToArray() };
        }

        static short[] MakeDrum(Drum kind, int rate)
        {
            float len = kind switch { Drum.Kick => 0.35f, Drum.Snare => 0.25f, Drum.ClosedHat => 0.06f, Drum.OpenHat => 0.4f, Drum.Tom => 0.4f, Drum.Crash => 1.2f, Drum.Clap => 0.2f, _ => 0.03f };
            int n = (int)(len * rate);
            var s = new short[n];
            var rng = new Random(777 + (int)kind);
            double phase = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate, env = Math.Exp(-t / (len * 0.25)), noise = rng.NextDouble() * 2 - 1, v;
                // one-pole high-pass for cymbals
                hp = 0.6 * (hp + noise - prev); prev = noise;
                switch (kind)
                {
                    case Drum.Kick: phase += 2 * Math.PI * (45 + 110 * Math.Exp(-t * 30)) / rate; v = Math.Sin(phase) * env; break;
                    case Drum.Tom: phase += 2 * Math.PI * (110 + 60 * Math.Exp(-t * 20)) / rate; v = Math.Sin(phase) * env; break;
                    case Drum.Snare: phase += 2 * Math.PI * 185 / rate; v = (0.5 * Math.Sin(phase) * Math.Exp(-t * 30) + 0.7 * noise) * env; break;
                    case Drum.Clap: v = noise * env * (t < 0.03 ? (Math.Sin(t * 600) > 0 ? 1 : 0.3) : 1); break;
                    case Drum.ClosedHat: case Drum.OpenHat: case Drum.Crash: v = hp * env * 0.8; break;
                    default: v = noise * env; break;
                }
                s[i] = ToShort(v * 0.9);
            }
            return s;
        }
    }
}
