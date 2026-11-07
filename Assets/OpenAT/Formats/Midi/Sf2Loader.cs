using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Formats.Midi
{
    /// <summary>
    /// SoundFont 2 (RIFF 'sfbk') reader. Flattens preset zones x instrument zones into
    /// <see cref="Region"/>s. Supported generators: sample address offsets, key/velocity range,
    /// sampleID, overriding root key, coarse/fine/scale tuning, sample modes (loop), volume
    /// envelope incl. key scaling, pan, initial attenuation, exclusive class, vibrato LFO.
    /// Modulators are not parsed; the SF2 default modulators are implied by the synth
    /// (velocity and CC7/CC11 to attenuation, CC10 pan, mod wheel vibrato, pitch wheel).
    /// </summary>
    public static class Sf2Loader
    {
        const int GenStartAddrs = 0, GenEndAddrs = 1, GenStartLoopAddrs = 2, GenEndLoopAddrs = 3, GenStartAddrsCoarse = 4,
            GenVibLfoToPitch = 6, GenEndAddrsCoarse = 12, GenPan = 17, GenDelayVibLfo = 21, GenFreqVibLfo = 22,
            GenDelayVolEnv = 33, GenAttackVolEnv = 34, GenHoldVolEnv = 35, GenDecayVolEnv = 36, GenSustainVolEnv = 37,
            GenReleaseVolEnv = 38, GenKeynumToVolEnvHold = 39, GenKeynumToVolEnvDecay = 40, GenInstrument = 41,
            GenKeyRange = 43, GenVelRange = 44, GenStartLoopAddrsCoarse = 45, GenInitialAttenuation = 48,
            GenEndLoopAddrsCoarse = 50, GenCoarseTune = 51, GenFineTune = 52, GenSampleId = 53, GenSampleModes = 54,
            GenScaleTuning = 56, GenExclusiveClass = 57, GenOverridingRootKey = 58, GenCount = 61;

        struct Gen { public ushort Op; public short Amount; }
        struct Bag { public ushort GenIndex; }
        struct Header { public string Name; public int A, B, BagIndex; }
        struct SampleHeader { public string Name; public uint Start, End, LoopStart, LoopEnd, Rate; public byte Pitch; public sbyte Correction; public ushort Type; }

        sealed class Zone
        {
            public readonly short[] G = new short[GenCount];
            public readonly bool[] Set = new bool[GenCount];
            public int LoKey, HiKey = 127, LoVel, HiVel = 127;
        }

        public static SoundBank Load(byte[] d)
        {
            if (Riff.Tag(d, 0) != "RIFF" || Riff.Tag(d, 8) != "sfbk") throw new InvalidDataException("Not a SoundFont 2 file");
            var bank = new SoundBank { Kind = "SF2" };
            short[] pool = null;
            Riff.Chunk? phdr = null, pbag = null, pgen = null, inst = null, ibag = null, igen = null, shdr = null;
            foreach (var c in Riff.Children(d, 12, 8 + Riff.S32(d, 4)))
            {
                if (c.ListType == "INFO")
                {
                    foreach (var i in Riff.Children(d, c.Offset, c.End))
                        if (i.Id == "INAM") bank.Name = Riff.ZString(d, i.Offset, i.Size);
                }
                else if (c.ListType == "sdta")
                {
                    foreach (var s in Riff.Children(d, c.Offset, c.End))
                    {
                        if (s.Id != "smpl") continue;
                        pool = new short[s.Size / 2];
                        Buffer.BlockCopy(d, s.Offset, pool, 0, pool.Length * 2);
                        if (!BitConverter.IsLittleEndian)
                            for (int k = 0; k < pool.Length; k++) pool[k] = (short)((pool[k] << 8) | ((pool[k] >> 8) & 0xFF));
                    }
                }
                else if (c.ListType == "pdta")
                {
                    foreach (var s in Riff.Children(d, c.Offset, c.End))
                    {
                        switch (s.Id)
                        {
                            case "phdr": phdr = s; break;
                            case "pbag": pbag = s; break;
                            case "pgen": pgen = s; break;
                            case "inst": inst = s; break;
                            case "ibag": ibag = s; break;
                            case "igen": igen = s; break;
                            case "shdr": shdr = s; break;
                        }
                    }
                }
            }
            if (pool == null || phdr == null || pbag == null || pgen == null || inst == null || ibag == null || igen == null || shdr == null)
                throw new InvalidDataException("SoundFont is missing sdta/pdta chunks");

            var presets = ReadHeaders(d, phdr.Value, 38, true);
            var instruments = ReadHeaders(d, inst.Value, 22, false);
            var pbags = ReadBags(d, pbag.Value);
            var ibags = ReadBags(d, ibag.Value);
            var pgens = ReadGens(d, pgen.Value);
            var igens = ReadGens(d, igen.Value);
            var samples = ReadSamples(d, shdr.Value);

            // Instrument zones (global zone merged as defaults).
            var instZones = new List<Zone>[Math.Max(0, instruments.Count - 1)];
            for (int i = 0; i < instZones.Length; i++)
                instZones[i] = BuildZones(instruments[i].BagIndex, instruments[i + 1].BagIndex, ibags, igens, GenSampleId, true);

            for (int p = 0; p + 1 < presets.Count; p++)
            {
                var ph = presets[p];
                var pz = BuildZones(ph.BagIndex, presets[p + 1].BagIndex, pbags, pgens, GenInstrument, false);
                var regions = new List<Region>();
                foreach (var z in pz)
                {
                    if (!z.Set[GenInstrument]) continue;
                    int ii = (ushort)z.G[GenInstrument];
                    if (ii >= instZones.Length) continue;
                    foreach (var iz in instZones[ii])
                    {
                        if (!iz.Set[GenSampleId]) continue;
                        int lk = Math.Max(z.LoKey, iz.LoKey), hk = Math.Min(z.HiKey, iz.HiKey);
                        int lv = Math.Max(z.LoVel, iz.LoVel), hv = Math.Min(z.HiVel, iz.HiVel);
                        if (lk > hk || lv > hv) continue;
                        int si = (ushort)iz.G[GenSampleId];
                        if (si >= samples.Count) continue;
                        var r = MakeRegion(iz, z, samples[si], pool);
                        if (r == null) continue;
                        r.LoKey = (byte)lk; r.HiKey = (byte)hk; r.LoVel = (byte)lv; r.HiVel = (byte)hv;
                        regions.Add(r);
                    }
                }
                bool drum = ph.B == 128;
                bank.Add(new Instrument
                {
                    Name = ph.Name,
                    Program = ph.A & 0x7F,
                    Bank = drum ? 0 : ph.B * 128,
                    IsDrumKit = drum,
                    Regions = regions.ToArray(),
                });
            }
            return bank;
        }

        static Region MakeRegion(Zone iz, Zone pz, SampleHeader s, short[] pool)
        {
            if ((s.Type & 0x8000) != 0) return null; // ROM sample
            int I(int g) => iz.G[g];
            int P(int g) => pz.G[g]; // preset values are additive offsets (0 when unset)

            var r = new Region { Data = pool, SampleRate = (int)Math.Max(1, s.Rate) };
            r.Start = (int)s.Start + I(GenStartAddrs) + I(GenStartAddrsCoarse) * 32768;
            r.End = (int)s.End + I(GenEndAddrs) + I(GenEndAddrsCoarse) * 32768;
            r.LoopStart = (int)s.LoopStart + I(GenStartLoopAddrs) + I(GenStartLoopAddrsCoarse) * 32768;
            r.LoopEnd = (int)s.LoopEnd + I(GenEndLoopAddrs) + I(GenEndLoopAddrsCoarse) * 32768;
            int modes = I(GenSampleModes) & 3;
            r.Loop = modes == 1 ? LoopMode.Continuous : modes == 3 ? LoopMode.UntilRelease : LoopMode.None;

            int root = I(GenOverridingRootKey);
            r.RootKey = root >= 0 && root <= 127 ? root : s.Pitch <= 127 ? s.Pitch : 60;
            r.TuneCents = (I(GenCoarseTune) + P(GenCoarseTune)) * 100 + I(GenFineTune) + P(GenFineTune) + s.Correction;
            r.ScaleTuning = I(GenScaleTuning) + P(GenScaleTuning);

            r.AttenuationDb = Math.Max(0, I(GenInitialAttenuation) + P(GenInitialAttenuation)) / 10f;
            r.Pan = Math.Max(-1f, Math.Min(1f, (I(GenPan) + P(GenPan)) / 500f));

            r.DelaySec = Region.TimecentsToSeconds(I(GenDelayVolEnv) + P(GenDelayVolEnv));
            r.AttackSec = Region.TimecentsToSeconds(I(GenAttackVolEnv) + P(GenAttackVolEnv));
            r.HoldSec = Region.TimecentsToSeconds(I(GenHoldVolEnv) + P(GenHoldVolEnv));
            r.DecaySec = Region.TimecentsToSeconds(I(GenDecayVolEnv) + P(GenDecayVolEnv));
            r.ReleaseSec = Region.TimecentsToSeconds(I(GenReleaseVolEnv) + P(GenReleaseVolEnv));
            r.SustainDb = Math.Max(0, Math.Min(96, (I(GenSustainVolEnv) + P(GenSustainVolEnv)) / 10f));
            // SF2: tc += keynumTo* * (60 - key)
            r.HoldKeyTc = -(I(GenKeynumToVolEnvHold) + P(GenKeynumToVolEnvHold));
            r.DecayKeyTc = -(I(GenKeynumToVolEnvDecay) + P(GenKeynumToVolEnvDecay));
            r.ExclusiveClass = I(GenExclusiveClass);

            r.LfoHz = (float)(8.176 * Math.Pow(2.0, (I(GenFreqVibLfo) + P(GenFreqVibLfo)) / 1200.0));
            r.LfoDelaySec = Region.TimecentsToSeconds(I(GenDelayVibLfo) + P(GenDelayVibLfo));
            r.LfoToPitchCents = I(GenVibLfoToPitch) + P(GenVibLfoToPitch);
            r.Validate();
            return r.End - r.Start >= 2 ? r : null;
        }

        static List<Zone> BuildZones(int bagStart, int bagEnd, List<Bag> bags, List<Gen> gens, int terminal, bool instrumentLevel)
        {
            var zones = new List<Zone>();
            Zone global = null;
            for (int b = bagStart; b < bagEnd && b + 1 < bags.Count; b++)
            {
                var z = new Zone();
                if (instrumentLevel) InitDefaults(z);
                if (global != null)
                {
                    Array.Copy(global.G, z.G, GenCount);
                    Array.Copy(global.Set, z.Set, GenCount);
                    z.LoKey = global.LoKey; z.HiKey = global.HiKey; z.LoVel = global.LoVel; z.HiVel = global.HiVel;
                    z.Set[terminal] = false;
                }
                bool hasTerminal = false;
                for (int g = bags[b].GenIndex; g < bags[b + 1].GenIndex && g < gens.Count; g++)
                {
                    var gen = gens[g];
                    if (gen.Op == GenKeyRange) { z.LoKey = gen.Amount & 0xFF; z.HiKey = (gen.Amount >> 8) & 0xFF; continue; }
                    if (gen.Op == GenVelRange) { z.LoVel = gen.Amount & 0xFF; z.HiVel = (gen.Amount >> 8) & 0xFF; continue; }
                    if (gen.Op >= GenCount) continue;
                    z.G[gen.Op] = gen.Amount;
                    z.Set[gen.Op] = true;
                    if (gen.Op == terminal) hasTerminal = true;
                }
                if (!hasTerminal)
                {
                    if (b == bagStart && global == null) { global = z; continue; } // global zone
                    continue;
                }
                zones.Add(z);
            }
            return zones;
        }

        static void InitDefaults(Zone z)
        {
            z.G[8] = 13500; // initialFilterFc
            z.G[GenDelayVolEnv] = z.G[GenAttackVolEnv] = z.G[GenHoldVolEnv] = z.G[GenDecayVolEnv] = z.G[GenReleaseVolEnv] = -12000;
            z.G[25] = z.G[26] = z.G[27] = z.G[28] = z.G[30] = -12000; // mod env times
            z.G[GenDelayVibLfo] = z.G[23] = -12000;
            z.G[46] = z.G[47] = -1;
            z.G[GenScaleTuning] = 100;
            z.G[GenOverridingRootKey] = -1;
        }

        static List<Header> ReadHeaders(byte[] d, Riff.Chunk c, int size, bool preset)
        {
            var list = new List<Header>();
            for (int p = c.Offset; p + size <= c.End; p += size)
            {
                var h = new Header { Name = Riff.ZString(d, p, 20) };
                if (preset) { h.A = Riff.U16(d, p + 20); h.B = Riff.U16(d, p + 22); h.BagIndex = Riff.U16(d, p + 24); }
                else h.BagIndex = Riff.U16(d, p + 20);
                list.Add(h);
            }
            return list;
        }

        static List<Bag> ReadBags(byte[] d, Riff.Chunk c)
        {
            var list = new List<Bag>();
            for (int p = c.Offset; p + 4 <= c.End; p += 4) list.Add(new Bag { GenIndex = Riff.U16(d, p) });
            return list;
        }

        static List<Gen> ReadGens(byte[] d, Riff.Chunk c)
        {
            var list = new List<Gen>();
            for (int p = c.Offset; p + 4 <= c.End; p += 4) list.Add(new Gen { Op = Riff.U16(d, p), Amount = Riff.S16(d, p + 2) });
            return list;
        }

        static List<SampleHeader> ReadSamples(byte[] d, Riff.Chunk c)
        {
            var list = new List<SampleHeader>();
            for (int p = c.Offset; p + 46 <= c.End; p += 46)
            {
                list.Add(new SampleHeader
                {
                    Name = Riff.ZString(d, p, 20),
                    Start = Riff.U32(d, p + 20),
                    End = Riff.U32(d, p + 24),
                    LoopStart = Riff.U32(d, p + 28),
                    LoopEnd = Riff.U32(d, p + 32),
                    Rate = Riff.U32(d, p + 36),
                    Pitch = d[p + 40],
                    Correction = (sbyte)d[p + 41],
                    Type = Riff.U16(d, p + 44),
                });
            }
            return list;
        }
    }
}
