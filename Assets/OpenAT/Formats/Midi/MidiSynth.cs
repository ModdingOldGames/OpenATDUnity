using System;

namespace OpenAT.Formats.Midi
{
    /// <summary>
    /// Real-time sample-playback General MIDI synthesizer rendering interleaved float PCM.
    /// Voices play <see cref="Region"/>s from a <see cref="SoundBank"/> with linear interpolation,
    /// looping, a DAHDSR volume envelope, vibrato LFO, pitch bend (RPN 0 range), channel
    /// volume/expression/pan, sustain pedal and drums on channel 10. Polyphony is fixed at
    /// construction; when it is exhausted the quietest released voice (else the oldest) is stolen.
    ///
    /// <see cref="Render"/> and the event methods perform no heap allocation. The class is not
    /// thread-safe: drive it from one thread, or let <see cref="MidiSequencer"/> do the locking.
    /// </summary>
    public sealed class MidiSynth
    {
        public const int MaxBlock = 64;
        public const int DrumChannel = 9;

        /// <summary>Fixed headroom so that typical GM arrangements do not clip.</summary>
        public float Headroom = 0.6f;
        /// <summary>User volume, 0..1 (values above 1 amplify).</summary>
        public float MasterVolume = 1f;

        public int SampleRate { get; }
        public SoundBank Bank { get; private set; }
        public int MaxVoices => _voices.Length;

        /// <summary>Number of voices currently sounding.</summary>
        public int ActiveVoiceCount
        {
            get
            {
                int n = 0;
                foreach (var v in _voices) if (v.Active) n++;
                return n;
            }
        }

        /// <summary>Peak absolute sample value written since the last <see cref="ResetPeak"/> (before clamping).</summary>
        public float Peak { get; private set; }
        public void ResetPeak() => Peak = 0;

        readonly Channel[] _channels = new Channel[16];
        readonly Voice[] _voices;
        readonly float[] _mixL = new float[MaxBlock], _mixR = new float[MaxBlock];
        readonly float _invRate;
        long _age;

        public MidiSynth(SoundBank bank, int sampleRate, int maxVoices = 64)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            Bank = bank ?? BuiltinBank.Create();
            SampleRate = sampleRate;
            _invRate = 1f / sampleRate;
            _voices = new Voice[Math.Max(1, maxVoices)];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
            for (int i = 0; i < 16; i++) _channels[i] = new Channel { Index = i };
            Reset();
        }

        /// <summary>Replaces the sound bank (silences everything). Not allocation-free; call off the audio thread or under the sequencer lock.</summary>
        public void SetBank(SoundBank bank)
        {
            Bank = bank ?? BuiltinBank.Create();
            Reset();
        }

        // ------------------------------------------------------------------ channel state

        sealed class Channel
        {
            public int Index;
            public int Program, BankMsb, BankLsb;
            public int Volume, Expression, Pan, Mod;
            public int Bend;           // 0..16383, center 8192
            public float BendRangeCents, FineTuneCents, CoarseTuneCents;
            public bool Sustain;
            public int RpnMsb, RpnLsb, DataMsb, DataLsb;
            public Instrument Instrument;
            public bool IsDrum => Index == DrumChannel;

            // Derived per-control values.
            public float Gain;         // volume^2 * expression^2
            public float PanValue;     // -1..1

            public void ResetControllers()
            {
                Volume = 100; Expression = 127; Pan = 64; Mod = 0; Bend = 8192;
                BendRangeCents = 200; FineTuneCents = 0; CoarseTuneCents = 0;
                Sustain = false; RpnMsb = RpnLsb = 127; DataMsb = DataLsb = 0;
                Update();
            }

            public void Update()
            {
                float v = Volume / 127f, e = Expression / 127f;
                Gain = v * v * e * e;
                PanValue = Pan <= 0 ? -1f : (Pan - 64) / 63f;
                if (PanValue > 1f) PanValue = 1f;
            }

            public float PitchCents => (Bend - 8192) / 8192f * BendRangeCents + FineTuneCents + CoarseTuneCents;
        }

        /// <summary>Resets all channels (GM defaults, programs 0) and silences all voices.</summary>
        public void Reset()
        {
            foreach (var v in _voices) v.Active = false;
            foreach (var c in _channels)
            {
                c.Program = 0; c.BankMsb = 0; c.BankLsb = 0;
                c.ResetControllers();
                c.Instrument = Bank.Find(0, 0, c.IsDrum);
            }
        }

        // ------------------------------------------------------------------ MIDI input

        /// <summary>Dispatches a raw channel message (status 0x80..0xEF).</summary>
        public void Process(int status, int data1, int data2)
        {
            int ch = status & 0x0F;
            switch (status & 0xF0)
            {
                case 0x80: NoteOff(ch, data1); break;
                case 0x90: if (data2 == 0) NoteOff(ch, data1); else NoteOn(ch, data1, data2); break;
                case 0xB0: ControlChange(ch, data1, data2); break;
                case 0xC0: ProgramChange(ch, data1); break;
                case 0xE0: PitchBend(ch, (data2 << 7) | data1); break;
                // 0xA0 poly aftertouch and 0xD0 channel pressure are ignored.
            }
        }

        public void ProgramChange(int ch, int program)
        {
            var c = _channels[ch & 15];
            c.Program = program & 0x7F;
            c.Instrument = c.IsDrum ? Bank.Find(0, c.Program, true) : Bank.Find(c.BankMsb * 128 + c.BankLsb, c.Program, false);
        }

        public void PitchBend(int ch, int value14)
        {
            _channels[ch & 15].Bend = Math.Max(0, Math.Min(16383, value14));
        }

        public void ControlChange(int ch, int cc, int value)
        {
            var c = _channels[ch & 15];
            switch (cc)
            {
                case 0: c.BankMsb = value; break;
                case 32: c.BankLsb = value; break;
                case 1: c.Mod = value; break;
                case 7: c.Volume = value; c.Update(); break;
                case 10: c.Pan = value; c.Update(); break;
                case 11: c.Expression = value; c.Update(); break;
                case 64:
                    c.Sustain = value >= 64;
                    if (!c.Sustain)
                        foreach (var v in _voices)
                            if (v.Active && v.Channel == c.Index && v.Sustained) { v.Sustained = false; Release(v); }
                    break;
                case 6: c.DataMsb = value; ApplyRpn(c); break;
                case 38: c.DataLsb = value; ApplyRpn(c); break;
                case 98: case 99: c.RpnMsb = c.RpnLsb = 127; break; // NRPN: unsupported, deselect RPN
                case 100: c.RpnLsb = value; break;
                case 101: c.RpnMsb = value; break;
                case 120: // all sound off
                    foreach (var v in _voices) if (v.Active && v.Channel == c.Index) v.Active = false;
                    break;
                case 121: c.ResetControllers(); break;
                case 123: case 124: case 125: case 126: case 127: AllNotesOff(c.Index); break;
            }
        }

        static void ApplyRpn(Channel c)
        {
            if (c.RpnMsb != 0) return;
            switch (c.RpnLsb)
            {
                case 0: c.BendRangeCents = c.DataMsb * 100 + c.DataLsb; break;
                case 1: c.FineTuneCents = (((c.DataMsb << 7) | c.DataLsb) - 8192) / 8192f * 100f; break;
                case 2: c.CoarseTuneCents = (c.DataMsb - 64) * 100f; break;
            }
        }

        /// <summary>Releases all held notes of one channel (or all channels when <paramref name="ch"/> is -1).</summary>
        public void AllNotesOff(int ch = -1)
        {
            foreach (var v in _voices)
            {
                if (!v.Active || (ch >= 0 && v.Channel != ch)) continue;
                v.Sustained = false;
                Release(v);
            }
            if (ch < 0) foreach (var c in _channels) c.Sustain = false;
        }

        /// <summary>Immediately silences every voice.</summary>
        public void AllSoundOff()
        {
            foreach (var v in _voices) v.Active = false;
        }

        public void NoteOff(int ch, int key)
        {
            var c = _channels[ch & 15];
            foreach (var v in _voices)
            {
                if (!v.Active || v.Channel != c.Index || v.Key != key || v.Released) continue;
                if (c.Sustain) v.Sustained = true;
                else Release(v);
            }
        }

        public void NoteOn(int ch, int key, int velocity)
        {
            var c = _channels[ch & 15];
            var inst = c.Instrument;
            if (inst == null || velocity <= 0) return;
            key &= 0x7F;
            velocity = Math.Min(127, velocity);

            // A retriggered key releases its previous note.
            foreach (var v in _voices)
                if (v.Active && v.Channel == c.Index && v.Key == key && !v.Released) { v.Sustained = false; Release(v); }

            var regions = inst.Regions;
            long startAge = _age;
            for (int i = 0; i < regions.Length; i++)
            {
                var r = regions[i];
                if (!r.Matches(key, velocity) || r.Data == null) continue;
                if (r.ExclusiveClass != 0)
                    foreach (var v in _voices)
                        if (v.Active && v.Channel == c.Index && v.Region.ExclusiveClass == r.ExclusiveClass && v.Age <= startAge)
                            v.Kill();
                StartVoice(Allocate(), c, r, key, velocity);
            }
        }

        Voice Allocate()
        {
            Voice best = null;
            foreach (var v in _voices) if (!v.Active) return v;
            // Steal: quietest released voice, otherwise the oldest.
            float quietest = float.MaxValue;
            foreach (var v in _voices)
                if (v.Released && v.LastAmp < quietest) { quietest = v.LastAmp; best = v; }
            if (best != null) return best;
            long oldest = long.MaxValue;
            foreach (var v in _voices)
                if (v.Age < oldest) { oldest = v.Age; best = v; }
            return best;
        }

        void StartVoice(Voice v, Channel c, Region r, int key, int velocity)
        {
            v.Active = true;
            v.Channel = c.Index;
            v.Key = key;
            v.Region = r;
            v.Age = ++_age;
            v.Released = v.Sustained = false;
            v.Looping = r.Loop != LoopMode.None;
            v.Pos = r.Start;
            v.RateRatio = (double)r.SampleRate / SampleRate;
            v.BaseCents = (key - r.RootKey) * r.ScaleTuning + r.TuneCents;
            float vel = velocity / 127f;
            v.NoteGain = vel * vel * DbToAmp(r.AttenuationDb) / 32768f;
            v.PrevL = v.PrevR = 0;
            v.LfoPhase = 0;
            v.LfoDelay = r.LfoDelaySec;
            v.LastAmp = 0;

            // Envelope with key scaling of hold/decay (relative to key 60).
            v.Stage = Stage.Delay;
            v.StageTime = r.DelaySec;
            v.Level = 0;
            v.AttDb = 96;
            v.Attack = r.AttackSec;
            v.Hold = r.HoldKeyTc != 0 && r.HoldSec > 0 ? r.HoldSec * MathF.Pow(2f, r.HoldKeyTc * (key - 60) / 1200f) : r.HoldSec;
            v.Decay = r.DecayKeyTc != 0 ? r.DecaySec * MathF.Pow(2f, r.DecayKeyTc * (key - 60) / 1200f) : r.DecaySec;
            v.SustainDb = r.SustainDb;
            v.ReleaseTime = Math.Max(0.006f, r.ReleaseSec);
        }

        void Release(Voice v)
        {
            if (v.Released) return;
            v.Released = true;
            if (v.Region.Loop == LoopMode.UntilRelease) v.Looping = false;
            v.EnterRelease();
        }

        static float DbToAmp(float db) => MathF.Pow(10f, -db / 20f);

        // ------------------------------------------------------------------ voices

        enum Stage : byte { Delay, Attack, Hold, Decay, Sustain, Release }

        sealed class Voice
        {
            public bool Active, Released, Sustained, Looping;
            public int Channel, Key;
            public long Age;
            public Region Region;
            public double Pos, RateRatio;
            public float BaseCents, NoteGain;
            public float PrevL, PrevR, LastAmp;
            public float LfoPhase, LfoDelay;

            public Stage Stage;
            public float StageTime, Level, AttDb;
            public float Attack, Hold, Decay, SustainDb, ReleaseTime;

            public void EnterRelease()
            {
                if (Stage == Stage.Delay || Stage == Stage.Attack)
                    AttDb = Level > 0.0000158f ? -20f * MathF.Log10(Level) : 96f;
                Stage = Stage.Release;
            }

            /// <summary>Very fast fade-out (exclusive class cut, stealing).</summary>
            public void Kill()
            {
                Released = true;
                Looping = Looping && Region.Loop == LoopMode.Continuous;
                ReleaseTime = 0.005f;
                EnterRelease();
            }

            /// <summary>Advances the envelope by dt seconds; returns the amplitude, or -1 when finished.</summary>
            public float Advance(float dt)
            {
                while (true)
                {
                    switch (Stage)
                    {
                        case Stage.Delay:
                            if (StageTime > dt) { StageTime -= dt; return 0f; }
                            dt -= StageTime;
                            Stage = Stage.Attack;
                            Level = 0;
                            continue;
                        case Stage.Attack:
                            if (Attack <= 0f) Level = 1f;
                            else Level += dt / Attack;
                            if (Level < 1f) return Level;
                            dt = Attack > 0f ? (Level - 1f) * Attack : dt;
                            Level = 1f;
                            AttDb = 0f;
                            Stage = Stage.Hold;
                            StageTime = Hold;
                            continue;
                        case Stage.Hold:
                            if (StageTime > dt) { StageTime -= dt; return 1f; }
                            dt -= StageTime;
                            Stage = Stage.Decay;
                            AttDb = 0f;
                            continue;
                        case Stage.Decay:
                        {
                            float target = SustainDb;
                            if (Decay <= 0f) AttDb = target;
                            else AttDb += 96f * dt / Decay;
                            if (AttDb < target) return DbToAmp(AttDb);
                            float over = Decay > 0f ? (AttDb - target) * Decay / 96f : 0f;
                            AttDb = target;
                            if (target >= 96f) return -1f;
                            Stage = Stage.Sustain;
                            dt = over;
                            continue;
                        }
                        case Stage.Sustain:
                            return DbToAmp(AttDb);
                        default: // Release
                            AttDb += 96f * dt / ReleaseTime;
                            if (AttDb >= 90f) return -1f;
                            return DbToAmp(AttDb);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>Renders <paramref name="frames"/> stereo frames into an interleaved L/R buffer (overwrites).</summary>
        public void Render(float[] interleaved, int frames) => Render(interleaved, 0, frames, 2);

        /// <summary>
        /// Renders <paramref name="frames"/> frames into <paramref name="buffer"/> starting at
        /// <paramref name="offset"/> with <paramref name="channels"/> interleaved channels
        /// (1 = mono mix, 2 = stereo, more: L/R in the first two, others silent). Overwrites.
        /// </summary>
        public void Render(float[] buffer, int offset, int frames, int channels)
        {
            if (channels < 1) return;
            int pos = offset;
            while (frames > 0)
            {
                int n = Math.Min(frames, MaxBlock);
                Array.Clear(_mixL, 0, n);
                Array.Clear(_mixR, 0, n);
                for (int i = 0; i < _voices.Length; i++)
                    if (_voices[i].Active) RenderVoice(_voices[i], n);

                float g = Headroom * MasterVolume, peak = Peak;
                for (int i = 0; i < n; i++)
                {
                    float l = _mixL[i] * g, r = _mixR[i] * g;
                    float al = l < 0 ? -l : l, ar = r < 0 ? -r : r;
                    if (al > peak) peak = al;
                    if (ar > peak) peak = ar;
                    if (al > Knee) l = SoftClip(l, al);
                    if (ar > Knee) r = SoftClip(r, ar);
                    if (channels == 2) { buffer[pos] = l; buffer[pos + 1] = r; }
                    else if (channels == 1) buffer[pos] = 0.5f * (l + r);
                    else
                    {
                        buffer[pos] = l; buffer[pos + 1] = r;
                        for (int k = 2; k < channels; k++) buffer[pos + k] = 0f;
                    }
                    pos += channels;
                }
                Peak = peak;
                frames -= n;
            }
        }

        const float Knee = 0.85f;

        /// <summary>Soft knee limiter above <see cref="Knee"/>; output never exceeds 1.</summary>
        static float SoftClip(float x, float ax)
        {
            float y = Knee + (1f - Knee) * MathF.Tanh((ax - Knee) / (1f - Knee));
            return x < 0 ? -y : y;
        }

        void RenderVoice(Voice v, int n)
        {
            var c = _channels[v.Channel];
            var r = v.Region;
            float dt = n * _invRate;
            float amp = v.Advance(dt);
            bool finishing = amp < 0f;
            if (finishing) amp = 0f;
            v.LastAmp = amp;

            // Pitch: key offset + channel bend/tuning + vibrato.
            float cents = v.BaseCents + c.PitchCents;
            float depth = r.LfoToPitchCents + r.ModToPitchCents * (c.Mod / 127f);
            if (depth != 0f)
            {
                if (v.LfoDelay > 0f) v.LfoDelay -= dt;
                else
                {
                    v.LfoPhase += r.LfoHz * dt;
                    if (v.LfoPhase >= 1f) v.LfoPhase -= (int)v.LfoPhase;
                    cents += depth * MathF.Sin(v.LfoPhase * 6.2831853f);
                }
            }
            double inc = v.RateRatio * MathF.Pow(2f, cents / 1200f);

            // Gain and constant-power pan.
            float pan = c.PanValue + r.Pan;
            if (pan < -1f) pan = -1f; else if (pan > 1f) pan = 1f;
            float angle = (pan + 1f) * 0.78539816f;
            float g = amp * v.NoteGain * c.Gain;
            float gl = g * MathF.Cos(angle), gr = g * MathF.Sin(angle);

            float curL = v.PrevL, curR = v.PrevR;
            float dl = (gl - curL) / n, dr = (gr - curR) / n;
            v.PrevL = gl; v.PrevR = gr;

            short[] d = r.Data;
            double p = v.Pos;
            int end = r.End, ls = r.LoopStart, le = r.LoopEnd;
            double loopLen = le - ls;
            bool looping = v.Looping;
            var L = _mixL; var R = _mixR;
            for (int i = 0; i < n; i++)
            {
                int ip = (int)p;
                float fr = (float)(p - ip);
                int ip1 = ip + 1;
                if (looping) { if (ip1 >= le) ip1 -= le - ls; }
                else if (ip1 >= end) ip1 = end - 1;
                float s0 = d[ip];
                float s = s0 + (d[ip1] - s0) * fr;
                curL += dl; curR += dr;
                L[i] += s * curL;
                R[i] += s * curR;
                p += inc;
                if (looping) { while (p >= le) p -= loopLen; }
                else if (p >= end) { finishing = true; break; }
            }
            v.Pos = p;
            if (finishing) v.Active = false;
        }
    }
}
