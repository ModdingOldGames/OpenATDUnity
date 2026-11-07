using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Formats.Midi
{
    /// <summary>
    /// Downloadable Sounds (DLS level 1 and the level-2 subset that maps onto <see cref="Region"/>)
    /// collection reader, e.g. Windows' own General MIDI bank (drivers\gm.dls).
    /// Supports lins/ins (insh, lrgn/rgn|rgn2 with rgnh, wsmp, wlnk, lart|lar2 with art1|art2),
    /// ptbl and wvpl/wave with 8/16-bit PCM. Articulation: volume envelope (EG1), pan, attenuation,
    /// tuning, key tracking, LFO vibrato (constant and mod-wheel).
    /// </summary>
    public static class DlsLoader
    {
        // Connection sources
        const int SrcNone = 0x0000, SrcLfo = 0x0001, SrcKeyNumber = 0x0003;
        const int CtrlNone = 0x0000, CtrlModWheel = 0x0081;
        // Connection destinations
        const int DstAttenuation = 0x0001, DstPitch = 0x0003, DstPan = 0x0004;
        const int DstLfoFrequency = 0x0104, DstLfoStartDelay = 0x0105;
        const int DstEg1Attack = 0x0206, DstEg1Decay = 0x0207, DstEg1Release = 0x0209, DstEg1Sustain = 0x020A,
            DstEg1Delay = 0x020B, DstEg1Hold = 0x020C;

        const uint DrumFlag = 0x80000000;

        sealed class Wave
        {
            public short[] Data;
            public int SampleRate;
            public Wsmp Wsmp;
        }

        sealed class Wsmp
        {
            public int UnityNote = 60;
            public float FineTuneCents;
            public float GainDb;
            public bool HasLoop;
            public int LoopType; // 0 forward, 1 release
            public int LoopStart, LoopLength;
        }

        public static SoundBank Load(byte[] d)
        {
            if (Riff.Tag(d, 0) != "RIFF" || Riff.Tag(d, 8) != "DLS ") throw new InvalidDataException("Not a DLS collection");
            var top = Riff.Children(d, 12, 8 + Riff.S32(d, 4));
            var bank = new SoundBank { Kind = "DLS" };

            // ---- wave pool (needed before instruments resolve their links)
            var waves = new List<Wave>();
            var waveOffsets = new List<int>();
            uint[] cues = null;
            foreach (var c in top)
            {
                if (c.Id == "ptbl" && c.Size >= 8)
                {
                    int cb = Riff.S32(d, c.Offset);
                    int n = Riff.S32(d, c.Offset + 4);
                    cues = new uint[Math.Max(0, Math.Min(n, (c.Size - cb) / 4))];
                    for (int i = 0; i < cues.Length; i++) cues[i] = Riff.U32(d, c.Offset + cb + i * 4);
                }
                else if (c.ListType == "wvpl")
                {
                    foreach (var w in Riff.Children(d, c.Offset, c.End))
                    {
                        if (w.ListType != "wave") continue;
                        waveOffsets.Add(w.HeaderOffset - c.Offset);
                        waves.Add(ReadWave(d, w));
                    }
                }
                else if (c.ListType == "INFO")
                {
                    foreach (var i in Riff.Children(d, c.Offset, c.End))
                        if (i.Id == "INAM") bank.Name = Riff.ZString(d, i.Offset, i.Size);
                }
            }
            // Map pool-table index -> wave.
            var byOffset = new Dictionary<int, Wave>();
            for (int i = 0; i < waves.Count; i++) byOffset[waveOffsets[i]] = waves[i];
            Wave WaveAt(int tableIndex)
            {
                if (cues != null && tableIndex >= 0 && tableIndex < cues.Length && byOffset.TryGetValue((int)cues[tableIndex], out var w)) return w;
                return tableIndex >= 0 && tableIndex < waves.Count ? waves[tableIndex] : null;
            }

            // ---- instruments
            foreach (var c in top)
            {
                if (c.ListType != "lins") continue;
                foreach (var ic in Riff.Children(d, c.Offset, c.End))
                {
                    if (ic.ListType != "ins ") continue;
                    var inst = ReadInstrument(d, ic, WaveAt);
                    if (inst != null) bank.Add(inst);
                }
            }
            return bank;
        }

        static Instrument ReadInstrument(byte[] d, Riff.Chunk ic, Func<int, Wave> waveAt)
        {
            var inst = new Instrument();
            Dictionary<int, int> globalArt = null;
            var rgnChunks = new List<Riff.Chunk>();
            foreach (var c in Riff.Children(d, ic.Offset, ic.End))
            {
                if (c.Id == "insh" && c.Size >= 12)
                {
                    uint ulBank = Riff.U32(d, c.Offset + 4);
                    uint ulInst = Riff.U32(d, c.Offset + 8);
                    inst.IsDrumKit = (ulBank & DrumFlag) != 0;
                    inst.Bank = (int)(((ulBank >> 8) & 0x7F) * 128 + (ulBank & 0x7F));
                    inst.Program = (int)(ulInst & 0x7F);
                }
                else if (c.ListType == "lrgn")
                {
                    foreach (var r in Riff.Children(d, c.Offset, c.End))
                        if (r.ListType == "rgn " || r.ListType == "rgn2") rgnChunks.Add(r);
                }
                else if (c.ListType == "lart" || c.ListType == "lar2") globalArt = ReadArticulation(d, c, null);
                else if (c.ListType == "INFO")
                {
                    foreach (var i in Riff.Children(d, c.Offset, c.End))
                        if (i.Id == "INAM") inst.Name = Riff.ZString(d, i.Offset, i.Size);
                }
            }

            var regions = new List<Region>();
            foreach (var rc in rgnChunks)
            {
                var reg = new Region();
                Wsmp wsmp = null;
                int tableIndex = -1;
                Dictionary<int, int> art = null;
                foreach (var c in Riff.Children(d, rc.Offset, rc.End))
                {
                    if (c.Id == "rgnh" && c.Size >= 12)
                    {
                        reg.LoKey = (byte)Math.Min(127, (int)Riff.U16(d, c.Offset));
                        reg.HiKey = (byte)Math.Min(127, (int)Riff.U16(d, c.Offset + 2));
                        reg.LoVel = (byte)Math.Min(127, (int)Riff.U16(d, c.Offset + 4));
                        reg.HiVel = (byte)Math.Min(127, (int)Riff.U16(d, c.Offset + 6));
                        if (reg.HiVel == 0 && reg.LoVel == 0) reg.HiVel = 127; // some DLS1 writers leave it zero
                        reg.ExclusiveClass = Riff.U16(d, c.Offset + 10);
                    }
                    else if (c.Id == "wsmp") wsmp = ReadWsmp(d, c);
                    else if (c.Id == "wlnk" && c.Size >= 12) tableIndex = Riff.S32(d, c.Offset + 8);
                    else if (c.ListType == "lart" || c.ListType == "lar2") art = ReadArticulation(d, c, art);
                }
                var wave = waveAt(tableIndex);
                if (wave == null || wave.Data == null || wave.Data.Length < 2) continue;
                wsmp ??= wave.Wsmp ?? new Wsmp();

                reg.Data = wave.Data;
                reg.SampleRate = wave.SampleRate;
                reg.Start = 0;
                reg.End = wave.Data.Length;
                reg.RootKey = wsmp.UnityNote;
                reg.TuneCents = wsmp.FineTuneCents;
                reg.AttenuationDb = -wsmp.GainDb;
                if (wsmp.HasLoop)
                {
                    reg.Loop = wsmp.LoopType == 1 ? LoopMode.UntilRelease : LoopMode.Continuous;
                    reg.LoopStart = wsmp.LoopStart;
                    reg.LoopEnd = wsmp.LoopStart + wsmp.LoopLength;
                }

                // Region articulation overrides instrument articulation connection by connection.
                var merged = new Dictionary<int, int>();
                if (globalArt != null) foreach (var kv in globalArt) merged[kv.Key] = kv.Value;
                if (art != null) foreach (var kv in art) merged[kv.Key] = kv.Value;
                ApplyArticulation(reg, merged);
                reg.Validate();
                regions.Add(reg);
            }
            inst.Regions = regions.ToArray();
            return inst;
        }

        static int ConnKey(int src, int ctrl, int dst) => (src << 20) ^ (ctrl << 12) ^ dst;

        static Dictionary<int, int> ReadArticulation(byte[] d, Riff.Chunk list, Dictionary<int, int> into)
        {
            into ??= new Dictionary<int, int>();
            foreach (var c in Riff.Children(d, list.Offset, list.End))
            {
                if ((c.Id != "art1" && c.Id != "art2") || c.Size < 8) continue;
                int cb = Riff.S32(d, c.Offset);
                int n = Riff.S32(d, c.Offset + 4);
                int p = c.Offset + cb;
                for (int i = 0; i < n && p + 12 <= c.End; i++, p += 12)
                {
                    int src = Riff.U16(d, p), ctrl = Riff.U16(d, p + 2), dst = Riff.U16(d, p + 4);
                    int scale = Riff.S32(d, p + 8);
                    into[ConnKey(src, ctrl, dst)] = scale;
                }
            }
            return into;
        }

        static void ApplyArticulation(Region r, Dictionary<int, int> a)
        {
            bool Get(int src, int ctrl, int dst, out double v)
            {
                if (a.TryGetValue(ConnKey(src, ctrl, dst), out int s)) { v = s; return true; }
                v = 0;
                return false;
            }
            static double Tc(double s) => s == int.MinValue ? -32768 : s / 65536.0;

            // DLS defaults: no attack/decay/hold, full sustain, (practically) no release.
            double attack = -32768, decay = -32768, release = -32768, delay = -32768, hold = -32768, sustainPct = 100;
            if (Get(SrcNone, CtrlNone, DstEg1Attack, out var v)) attack = Tc(v);
            if (Get(SrcNone, CtrlNone, DstEg1Decay, out v)) decay = Tc(v);
            if (Get(SrcNone, CtrlNone, DstEg1Release, out v)) release = Tc(v);
            if (Get(SrcNone, CtrlNone, DstEg1Delay, out v)) delay = Tc(v);
            if (Get(SrcNone, CtrlNone, DstEg1Hold, out v)) hold = Tc(v);
            if (Get(SrcNone, CtrlNone, DstEg1Sustain, out v)) sustainPct = v / 65536.0 / 10.0;
            if (Get(SrcKeyNumber, CtrlNone, DstEg1Decay, out v))
            {
                // DLS: tc += scale * key / 128. Express relative to key 60.
                double perKey = v / 65536.0 / 128.0;
                r.DecayKeyTc = (float)perKey;
                if (decay > -32768) decay += perKey * 60;
            }
            if (Get(SrcKeyNumber, CtrlNone, DstEg1Hold, out v))
            {
                double perKey = v / 65536.0 / 128.0;
                r.HoldKeyTc = (float)perKey;
                if (hold > -32768) hold += perKey * 60;
            }
            r.AttackSec = Region.TimecentsToSeconds(attack);
            r.DecaySec = Region.TimecentsToSeconds(decay);
            r.ReleaseSec = Region.TimecentsToSeconds(release);
            r.DelaySec = Region.TimecentsToSeconds(delay);
            r.HoldSec = Region.TimecentsToSeconds(hold);
            sustainPct = Math.Max(0, Math.Min(100, sustainPct));
            // Sustain is a percentage of the dB range of the envelope (0% = -96 dB).
            r.SustainDb = (float)(96.0 * (1.0 - sustainPct / 100.0));

            if (Get(SrcNone, CtrlNone, DstPan, out v)) r.Pan = (float)Math.Max(-1, Math.Min(1, v / 65536.0 / 10.0 / 50.0));
            if (Get(SrcNone, CtrlNone, DstAttenuation, out v)) r.AttenuationDb += (float)(-v / 655360.0);
            if (Get(SrcNone, CtrlNone, DstPitch, out v)) r.TuneCents += (float)(v / 65536.0);
            if (Get(SrcKeyNumber, CtrlNone, DstPitch, out v)) r.ScaleTuning = (float)(v / 65536.0 / 128.0);

            if (Get(SrcNone, CtrlNone, DstLfoFrequency, out v)) r.LfoHz = (float)(440.0 * Math.Pow(2.0, (v / 65536.0 - 6900.0) / 1200.0));
            if (Get(SrcNone, CtrlNone, DstLfoStartDelay, out v)) r.LfoDelaySec = Region.TimecentsToSeconds(Tc(v));
            if (Get(SrcLfo, CtrlNone, DstPitch, out v)) r.LfoToPitchCents = (float)(v / 65536.0);
            if (Get(SrcLfo, CtrlModWheel, DstPitch, out v)) r.ModToPitchCents = (float)(v / 65536.0);
        }

        static Wsmp ReadWsmp(byte[] d, Riff.Chunk c)
        {
            if (c.Size < 20) return null;
            var w = new Wsmp();
            int cb = Riff.S32(d, c.Offset);
            w.UnityNote = Math.Min(127, (int)Riff.U16(d, c.Offset + 4));
            w.FineTuneCents = Riff.S16(d, c.Offset + 6);
            w.GainDb = (float)(Riff.S32(d, c.Offset + 8) / 655360.0);
            int loops = Riff.S32(d, c.Offset + 16);
            int p = c.Offset + cb;
            if (loops > 0 && p + 16 <= c.End)
            {
                w.HasLoop = true;
                w.LoopType = Riff.S32(d, p + 4);
                w.LoopStart = Riff.S32(d, p + 8);
                w.LoopLength = Riff.S32(d, p + 12);
            }
            return w;
        }

        static Wave ReadWave(byte[] d, Riff.Chunk wc)
        {
            var w = new Wave();
            int channels = 1, bits = 16, format = 1;
            Riff.Chunk? data = null;
            foreach (var c in Riff.Children(d, wc.Offset, wc.End))
            {
                if (c.Id == "fmt " && c.Size >= 16)
                {
                    format = Riff.U16(d, c.Offset);
                    channels = Math.Max(1, (int)Riff.U16(d, c.Offset + 2));
                    w.SampleRate = Riff.S32(d, c.Offset + 4);
                    bits = Riff.U16(d, c.Offset + 14);
                }
                else if (c.Id == "wsmp") w.Wsmp = ReadWsmp(d, c);
                else if (c.Id == "data") data = c;
            }
            if (data == null || format != 1 || (bits != 8 && bits != 16) || w.SampleRate <= 0) return w;
            var dc = data.Value;
            int bytesPerFrame = channels * bits / 8;
            int frames = dc.Size / bytesPerFrame;
            var s = new short[frames];
            for (int i = 0, p = dc.Offset; i < frames; i++, p += bytesPerFrame)
                s[i] = bits == 16 ? Riff.S16(d, p) : (short)((d[p] - 128) << 8); // first channel only
            w.Data = s;
            return w;
        }
    }
}
