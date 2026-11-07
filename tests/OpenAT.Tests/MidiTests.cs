using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenAT.Formats;
using OpenAT.Formats.Midi;
using Xunit;

namespace OpenAT.Tests
{
    public class MidiTests
    {
        // ------------------------------------------------------------------ SMF builders

        static byte[] Vlq(long v)
        {
            var bytes = new List<byte> { (byte)(v & 0x7F) };
            while ((v >>= 7) > 0) bytes.Insert(0, (byte)(0x80 | (v & 0x7F)));
            return bytes.ToArray();
        }

        static byte[] Track(params byte[][] events)
        {
            var body = events.SelectMany(e => e).ToArray();
            var t = new List<byte>(Encoding.ASCII.GetBytes("MTrk"));
            t.AddRange(new[] { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length });
            t.AddRange(body);
            return t.ToArray();
        }

        static byte[] Ev(long delta, params byte[] data) => Vlq(delta).Concat(data).ToArray();

        static byte[] Smf(int format, int division, params byte[][] tracks)
        {
            var h = new List<byte>(Encoding.ASCII.GetBytes("MThd")) { 0, 0, 0, 6, 0, (byte)format, 0, (byte)tracks.Length, (byte)(division >> 8), (byte)division };
            foreach (var t in tracks) h.AddRange(t);
            return h.ToArray();
        }

        static readonly byte[] EndOfTrack = { 0xFF, 0x2F, 0x00 };

        /// <summary>
        /// Format 1, 96 ticks/quarter. Tempo 120 bpm, then 240 bpm from tick 192.
        /// Track 1: C4 at 0..96, E4 at 192..288 (note-offs via running status note-on vel 0), a sysex in between.
        /// </summary>
        static byte[] TwoNoteSong() => Smf(1, 96,
            Track(
                Ev(0, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20),        // 500000 us/quarter
                Ev(192, 0xFF, 0x51, 0x03, 0x03, 0xD0, 0x90),      // 250000 us/quarter
                Ev(96, EndOfTrack)),
            Track(
                Ev(0, 0xFF, 0x03, 0x04, (byte)'L', (byte)'e', (byte)'a', (byte)'d'),
                Ev(0, 0xC0, 0x00),
                Ev(0, 0x90, 60, 100),
                Ev(96, 60, 0),                                     // running status note off
                Ev(48, 0xF0, 0x03, 0x7E, 0x09, 0xF7),              // sysex: skipped by the sequencer
                Ev(48, 0x90, 64, 90),
                Ev(96, 64, 0),
                Ev(0, EndOfTrack)));

        [Fact]
        public void Smf_ParsesEventsTempoMapAndRunningStatus()
        {
            var f = MidiFile.Parse(TwoNoteSong());
            Assert.Equal(1, f.Format);
            Assert.Equal(2, f.TrackCount);
            Assert.Equal(96, f.Division);
            Assert.Equal("Lead", f.TrackNames[1]);
            Assert.Equal(2, f.Tempos.Length);
            Assert.Equal(1.0, f.Tempos[1].Seconds, 6);

            var notes = f.Events.Where(e => e.Command == 0x90).ToArray();
            Assert.Equal(4, notes.Length);
            Assert.Equal(new long[] { 0, 96, 192, 288 }, notes.Select(e => e.Tick).ToArray());
            Assert.Equal(new byte[] { 100, 0, 90, 0 }, notes.Select(e => e.Data2).ToArray());
            Assert.Equal(0.0, notes[0].Seconds, 6);
            Assert.Equal(0.5, notes[1].Seconds, 6);
            Assert.Equal(1.0, notes[2].Seconds, 6);
            Assert.Equal(1.25, notes[3].Seconds, 6); // 96 ticks at 240 bpm
            Assert.Single(f.Events, e => e.Status == 0xF0);
            Assert.Equal(288, f.DurationTicks);
            Assert.Equal(1.25, f.DurationSeconds, 6);
            Assert.False(f.HasLoopPoints);
            // Events are globally time ordered.
            Assert.True(f.Events.Zip(f.Events.Skip(1), (a, b) => a.Tick <= b.Tick).All(x => x));
        }

        [Fact]
        public void Smf_Format0_MultiByteDeltas_LoopMarkers_Rmid()
        {
            var smf = Smf(0, 480, Track(
                Ev(0, 0xB0, 7, 100),
                Ev(0, 10, 64),                                      // running status CC
                Ev(200, 0xFF, 0x06, 0x09).Concat(Encoding.ASCII.GetBytes("loopStart")).ToArray(),
                Ev(0, 0x99, 36, 127),
                Ev(16384, 0x89, 36, 0),                              // 3-byte delta
                Ev(0, 0xFF, 0x06, 0x07).Concat(Encoding.ASCII.GetBytes("loopEnd")).ToArray(),
                Ev(0, EndOfTrack)));
            var f = MidiFile.Parse(smf);
            Assert.Equal(0, f.Format);
            var cc = f.Events.Where(e => e.Command == 0xB0).ToArray();
            Assert.Equal(2, cc.Length);
            Assert.Equal(10, cc[1].Data1);
            Assert.Equal(64, cc[1].Data2);
            Assert.Equal(200, f.LoopStartTick);
            Assert.Equal(200 + 16384, f.LoopEndTick);
            var off = f.Events.Single(e => e.Command == 0x80);
            Assert.Equal(9, off.Channel);
            Assert.Equal(16584, off.Tick);
            Assert.Equal(16584 / 480.0 * 0.5, off.Seconds, 6);

            // Same data wrapped in RIFF RMID.
            var rmid = new List<byte>(Encoding.ASCII.GetBytes("RIFF"));
            rmid.AddRange(BitConverter.GetBytes(4 + 8 + smf.Length));
            rmid.AddRange(Encoding.ASCII.GetBytes("RMIDdata"));
            rmid.AddRange(BitConverter.GetBytes(smf.Length));
            rmid.AddRange(smf);
            Assert.Equal(f.Events.Length, MidiFile.Parse(rmid.ToArray()).Events.Length);
        }

        [Fact]
        public void Smf_RejectsGarbage()
        {
            Assert.Throws<InvalidDataException>(() => MidiFile.Parse(new byte[20]));
        }

        // ------------------------------------------------------------------ synth / sequencer

        static float Rms(float[] buf, int from, int to)
        {
            double s = 0;
            for (int i = from; i < to; i++) s += buf[i] * (double)buf[i];
            return (float)Math.Sqrt(s / Math.Max(1, to - from));
        }

        static float[] RenderSong(MidiSequencer seq, double seconds)
        {
            int frames = (int)(seconds * seq.SampleRate);
            var all = new float[frames * 2];
            var block = new float[512 * 2];
            for (int done = 0; done < frames; done += 512)
            {
                int n = Math.Min(512, frames - done);
                seq.Render(block, n);
                Array.Copy(block, 0, all, done * 2, n * 2);
            }
            return all;
        }

        [Fact]
        public void Sequencer_EventTimingIsSampleAccurate()
        {
            const int rate = 48000;
            var seq = new MidiSequencer(BuiltinBank.Create(), rate);
            seq.Load(MidiFile.Parse(TwoNoteSong()));
            seq.Play(false);
            var pcm = RenderSong(seq, 2.0);
            int Frame(double s) => (int)(s * rate) * 2;

            // Silent before the first note's attack, sounding during each note, release tail after.
            Assert.True(Rms(pcm, Frame(0.05), Frame(0.45)) > 0.01f);
            Assert.True(Rms(pcm, Frame(0.85), Frame(0.99)) < Rms(pcm, Frame(0.05), Frame(0.45)) * 0.05f);
            Assert.True(Rms(pcm, Frame(1.02), Frame(1.2)) > 0.01f);
            Assert.True(Rms(pcm, Frame(1.8), Frame(2.0)) < 0.001f);

            // Onset of note 2 lands within one millisecond of 1.0 s.
            int onset = -1;
            for (int i = Frame(0.99); i < Frame(1.1); i += 2)
                if (Math.Abs(pcm[i]) > 0.001f) { onset = i / 2; break; }
            Assert.InRange(onset, rate - 2, rate + rate / 1000);
            Assert.False(seq.IsPlaying);
        }

        [Fact]
        public void Sequencer_LoopsAndTracksPosition()
        {
            var seq = new MidiSequencer(BuiltinBank.Create(), 44100);
            seq.Load(MidiFile.Parse(TwoNoteSong()));
            seq.Play(true);
            RenderSong(seq, 3.0); // song is 1.25 s
            Assert.True(seq.IsPlaying);
            Assert.Equal(2, seq.LoopCount);
            Assert.InRange(seq.PositionSeconds, 0.49, 0.51);
            seq.Stop();
            Assert.False(seq.IsPlaying);
        }

        [Fact]
        public void Synth_PitchMatchesKey()
        {
            const int rate = 48000;
            var synth = new MidiSynth(BuiltinBank.Create(), rate);
            synth.ProgramChange(0, 80); // square lead, looped
            synth.NoteOn(0, 69, 100);
            var buf = new float[rate * 2];
            synth.Render(buf, rate);
            Assert.InRange(EstimateHz(buf, rate, rate / 4, rate / 2), 438.5, 441.5);

            // Pitch bend +2 semitones (default range) -> B4.
            synth.PitchBend(0, 16383);
            synth.Render(buf, rate);
            Assert.InRange(EstimateHz(buf, rate, rate / 4, rate / 2), 491, 496.5);
        }

        /// <summary>Fundamental via autocorrelation of the left channel between 60 and 2000 Hz.</summary>
        static double EstimateHz(float[] interleaved, int rate, int start, int len)
        {
            var x = new double[len];
            for (int i = 0; i < len; i++) x[i] = interleaved[(start + i) * 2];
            int minLag = rate / 2000, maxLag = rate / 60;
            double best = double.MinValue;
            int bestLag = minLag;
            var corr = new double[maxLag + 2];
            for (int lag = minLag; lag <= maxLag + 1; lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < len; i++) s += x[i] * x[i + lag];
                corr[lag] = s / (len - lag);
            }
            // First strong peak (avoids octave errors).
            double max = corr.Skip(minLag).Max();
            for (int lag = minLag + 1; lag <= maxLag; lag++)
                if (corr[lag] > 0.9 * max && corr[lag] >= corr[lag - 1] && corr[lag] >= corr[lag + 1]) { bestLag = lag; best = corr[lag]; break; }
            // Parabolic interpolation.
            double a = corr[bestLag - 1], b = corr[bestLag], c = corr[bestLag + 1];
            double shift = 0.5 * (a - c) / (a - 2 * b + c);
            return rate / (bestLag + shift);
        }

        [Fact]
        public void Synth_PolyphonyLimitAndNoAllocationInRender()
        {
            var synth = new MidiSynth(BuiltinBank.Create(), 44100, 64);
            var buf = new float[1024 * 2];
            synth.Render(buf, 1024); // warm up (JIT)
            for (int i = 0; i < 100; i++) synth.NoteOn(i % 9, 30 + i % 60, 100);
            Assert.Equal(64, synth.ActiveVoiceCount);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 50; k++)
            {
                synth.NoteOn(k % 16, 40 + k, 90);
                synth.ControlChange(k % 16, 7, k);
                synth.PitchBend(k % 16, k * 300);
                synth.Render(buf, 0, 1024, 2);
                synth.NoteOff(k % 16, 40 + k);
            }
            var seq = new MidiSequencer(synth);
            seq.Load(MidiFile.Parse(TwoNoteSong()));
            seq.Play(true);
            seq.Render(buf, 1024); // JIT
            for (int k = 0; k < 50; k++) seq.Render(buf, 0, 512, 2); // JIT of the measured overload
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++) seq.Render(buf, 0, 512, 2);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void Synth_SustainPedalHoldsNotes()
        {
            var synth = new MidiSynth(BuiltinBank.Create(), 44100);
            var buf = new float[4410 * 2];
            synth.ProgramChange(0, 16); // organ: sustains while held
            synth.ControlChange(0, 64, 127);
            synth.NoteOn(0, 60, 100);
            synth.NoteOff(0, 60);
            synth.Render(buf, 4410);
            Assert.Equal(1, synth.ActiveVoiceCount);
            synth.ControlChange(0, 64, 0);
            for (int i = 0; i < 10; i++) synth.Render(buf, 4410);
            Assert.Equal(0, synth.ActiveVoiceCount);
        }

        [Fact]
        public void Synth_RendersMultiChannelLayouts()
        {
            var synth = new MidiSynth(BuiltinBank.Create(), 48000);
            synth.ControlChange(0, 10, 0); // hard left
            synth.NoteOn(0, 60, 127);
            var buf = new float[256 * 6];
            for (int i = 0; i < buf.Length; i++) buf[i] = 9f;
            synth.Render(buf, 0, 256, 6);
            float left = 0, right = 0, other = 0;
            for (int f = 0; f < 256; f++)
            {
                left += Math.Abs(buf[f * 6]); right += Math.Abs(buf[f * 6 + 1]);
                for (int c = 2; c < 6; c++) other += Math.Abs(buf[f * 6 + c]);
            }
            Assert.True(left > 0.1f);
            Assert.True(right < left * 0.01f);
            Assert.Equal(0f, other);
        }

        // ------------------------------------------------------------------ SoundFont 2

        /// <summary>Minimal SF2: one looped 100-sample sine cycle at 44.1 kHz (441 Hz) rooted at A4.</summary>
        static byte[] TinySf2()
        {
            byte[] Chunk(string id, byte[] body)
            {
                var c = new List<byte>(Encoding.ASCII.GetBytes(id));
                c.AddRange(BitConverter.GetBytes(body.Length));
                c.AddRange(body);
                if ((body.Length & 1) != 0) c.Add(0);
                return c.ToArray();
            }
            byte[] List(string type, params byte[][] chunks) => Chunk("LIST", Encoding.ASCII.GetBytes(type).Concat(chunks.SelectMany(x => x)).ToArray());
            byte[] Name(string s) { var b = new byte[20]; Encoding.ASCII.GetBytes(s).CopyTo(b, 0); return b; }
            byte[] U16(int v) => BitConverter.GetBytes((ushort)v);
            byte[] U32(long v) => BitConverter.GetBytes((uint)v);
            byte[] Gen(int op, int amount) => U16(op).Concat(BitConverter.GetBytes((short)amount)).ToArray();

            var smpl = new List<byte>();
            for (int i = 0; i < 100; i++) smpl.AddRange(BitConverter.GetBytes((short)(Math.Sin(2 * Math.PI * i / 100) * 20000)));
            smpl.AddRange(new byte[46 * 2]);

            byte[] Phdr(string n, int preset, int bank, int bag) => Name(n).Concat(U16(preset)).Concat(U16(bank)).Concat(U16(bag)).Concat(new byte[12]).ToArray();
            byte[] Inst(string n, int bag) => Name(n).Concat(U16(bag)).ToArray();
            byte[] Shdr(string n, long start, long end, long ls, long le, int pitch) =>
                Name(n).Concat(U32(start)).Concat(U32(end)).Concat(U32(ls)).Concat(U32(le)).Concat(U32(44100))
                    .Concat(new[] { (byte)pitch, (byte)0 }).Concat(U16(0)).Concat(U16(1)).ToArray();

            var pdta = List("pdta",
                Chunk("phdr", Phdr("Test", 0, 0, 0).Concat(Phdr("EOP", 0, 0, 1)).ToArray()),
                Chunk("pbag", U16(0).Concat(U16(0)).Concat(U16(1)).Concat(U16(0)).ToArray()),
                Chunk("pmod", new byte[10]),
                Chunk("pgen", Gen(41, 0).Concat(Gen(0, 0)).ToArray()),
                Chunk("inst", Inst("Sine", 0).Concat(Inst("EOI", 1)).ToArray()),
                Chunk("ibag", U16(0).Concat(U16(0)).Concat(U16(4)).Concat(U16(0)).ToArray()),
                Chunk("imod", new byte[10]),
                Chunk("igen", Gen(43, 0 | (127 << 8)).Concat(Gen(54, 1)).Concat(Gen(48, 30)).Concat(Gen(53, 0)).Concat(Gen(0, 0)).ToArray()),
                Chunk("shdr", Shdr("Sine", 0, 100, 0, 100, 69).Concat(Shdr("EOS", 0, 0, 0, 0, 0)).ToArray()));
            var body = Encoding.ASCII.GetBytes("sfbk")
                .Concat(List("INFO", Chunk("ifil", U16(2).Concat(U16(1)).ToArray()), Chunk("INAM", Encoding.ASCII.GetBytes("Tiny\0"))))
                .Concat(List("sdta", Chunk("smpl", smpl.ToArray())))
                .Concat(pdta).ToArray();
            return Chunk("RIFF", body);
        }

        [Fact]
        public void Sf2_LoadsAndPlaysAtCorrectPitch()
        {
            var path = Path.Combine(Path.GetTempPath(), "openat_tiny_" + Guid.NewGuid().ToString("N") + ".sf2");
            File.WriteAllBytes(path, TinySf2());
            try
            {
                var bank = SoundBank.Load(path);
                Assert.Equal("SF2", bank.Kind);
                Assert.Equal("Tiny", bank.Name);
                var inst = bank.Find(0, 0, false);
                Assert.NotNull(inst);
                var r = Assert.Single(inst.Regions);
                Assert.Equal(69, r.RootKey);
                Assert.Equal(LoopMode.Continuous, r.Loop);
                Assert.Equal(100, r.LoopEnd);
                Assert.Equal(3f, r.AttenuationDb, 3);
                Assert.Same(inst, bank.Find(0, 5, false)); // unknown program falls back

                const int rate = 44100;
                var synth = new MidiSynth(bank, rate);
                synth.NoteOn(0, 69, 127);
                var buf = new float[rate * 2];
                synth.Render(buf, rate);
                Assert.InRange(EstimateHz(buf, rate, rate / 4, rate / 2), 439.5, 442.5);
                synth.AllSoundOff();
                synth.NoteOn(0, 81, 127); // one octave up
                synth.Render(buf, rate);
                Assert.InRange(EstimateHz(buf, rate, rate / 4, rate / 2), 878, 886);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void SoundBank_LoadBestFallsBackToBuiltin()
        {
            var b = SoundBank.LoadBest(Path.Combine(Path.GetTempPath(), "does-not-exist.sf2"));
            Assert.NotNull(b);
            Assert.NotNull(b.Find(0, 0, false));
            Assert.NotNull(b.Find(0, 0, true));
            var builtin = BuiltinBank.Create();
            Assert.Equal(128, builtin.Instruments.Count(i => !i.IsDrumKit));
            Assert.True(builtin.Find(0, 0, true).IsDrumKit);
        }

        // ------------------------------------------------------------------ machine-dependent

        [Fact]
        public void Dls_WindowsGmBank()
        {
            if (!File.Exists(SoundBank.WindowsGmDls)) return; // not on Windows / no gm.dls
            var bank = SoundBank.Load(SoundBank.WindowsGmDls);
            Assert.Equal("DLS", bank.Kind);
            for (int p = 0; p < 128; p++) Assert.NotNull(bank.Find(0, p, false));
            var drums = bank.Find(0, 0, true);
            Assert.True(drums.IsDrumKit);
            Assert.Contains(drums.Regions, r => r.Matches(36, 100)); // kick
            Assert.Contains(drums.Regions, r => r.ExclusiveClass != 0); // hi-hat group

            const int rate = 44100;
            var synth = new MidiSynth(bank, rate);
            synth.ProgramChange(0, 73); // flute
            synth.NoteOn(0, 69, 100);
            var buf = new float[rate * 2];
            synth.Render(buf, rate);
            Assert.InRange(EstimateHz(buf, rate, rate / 4, rate / 2), 434, 446);
        }

        [Fact]
        public void GameSongs_ParseAndRender()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return; // no game files on this machine
            var songs = gi.List(GameInstall.Folders.Sound, ".mid").ToList();
            Assert.NotEmpty(songs);
            foreach (var s in songs)
            {
                var f = MidiFile.Parse(gi.ReadAllBytes(s));
                Assert.True(f.DurationSeconds > 60, s);
                Assert.Contains(f.Events, e => e.Command == 0x90 && e.Channel == 9);
            }
            var title = MidiFile.Parse(gi.ReadAllBytes(songs.First(x => x.EndsWith("title.mid", StringComparison.OrdinalIgnoreCase))));
            Assert.InRange(title.DurationSeconds, 140, 148);

            var seq = new MidiSequencer(SoundBank.LoadBest(), 48000);
            seq.Load(title);
            seq.Play(false);
            var pcm = RenderSong(seq, 10);
            Assert.True(Rms(pcm, 0, pcm.Length) > 0.005f);
            Assert.True(pcm.All(x => x >= -1f && x <= 1f));
        }
    }
}
