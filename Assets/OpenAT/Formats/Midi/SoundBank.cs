using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAT.Formats.Midi
{
    public enum LoopMode : byte
    {
        /// <summary>Play once to the end of the sample.</summary>
        None = 0,
        /// <summary>Loop forever (also during release).</summary>
        Continuous = 1,
        /// <summary>Loop while the key is held, then play on to the end.</summary>
        UntilRelease = 2,
    }

    /// <summary>
    /// A key/velocity zone of an instrument: which sample to play and how. Sound bank formats
    /// (DLS, SF2, built-in) are all flattened into this model when loaded.
    /// </summary>
    public sealed class Region
    {
        public byte LoKey = 0, HiKey = 127, LoVel = 0, HiVel = 127;

        /// <summary>16-bit PCM. May be shared with other regions (SF2 has one big pool).</summary>
        public short[] Data;
        public int Start, End;            // [Start, End) in Data
        public int LoopStart, LoopEnd;    // [LoopStart, LoopEnd) in Data
        public LoopMode Loop;
        public int SampleRate = 44100;

        public int RootKey = 60;
        /// <summary>Fine/coarse tuning offset in cents.</summary>
        public float TuneCents;
        /// <summary>Pitch tracking in cents per key (100 = normal, 0 = fixed pitch).</summary>
        public float ScaleTuning = 100;

        /// <summary>Static attenuation in dB (positive = quieter).</summary>
        public float AttenuationDb;
        /// <summary>-1 (left) .. +1 (right).</summary>
        public float Pan;

        // Volume envelope. Times in seconds, sustain as attenuation in dB.
        public float DelaySec, AttackSec = 0.001f, HoldSec, DecaySec = 0.001f, ReleaseSec = 0.05f;
        public float SustainDb;
        /// <summary>Decay/hold time scaling in timecents per key, relative to key 60.</summary>
        public float DecayKeyTc, HoldKeyTc;

        // Vibrato LFO (sine).
        public float LfoHz = 5f, LfoDelaySec;
        /// <summary>Constant LFO pitch depth in cents.</summary>
        public float LfoToPitchCents;
        /// <summary>LFO pitch depth in cents at full modulation wheel.</summary>
        public float ModToPitchCents = 50f;

        /// <summary>Notes with the same non-zero class on a channel cut each other off (hi-hats).</summary>
        public int ExclusiveClass;

        public bool Matches(int key, int vel) => key >= LoKey && key <= HiKey && vel >= LoVel && vel <= HiVel;

        public Region Clone() => (Region)MemberwiseClone();

        /// <summary>Clamps sample/loop offsets to the data and disables invalid loops.</summary>
        public void Validate()
        {
            int n = Data?.Length ?? 0;
            Start = Math.Max(0, Math.Min(Start, n));
            End = Math.Max(Start, Math.Min(End, n));
            if (Loop != LoopMode.None && (LoopStart < Start || LoopEnd > End || LoopEnd - LoopStart < 2))
                Loop = LoopMode.None;
        }

        public static float TimecentsToSeconds(double tc) => tc <= -32768 ? 0f : (float)Math.Pow(2.0, tc / 1200.0);
    }

    public sealed class Instrument
    {
        public string Name;
        public int Bank;     // MSB*128 + LSB for melodic banks
        public int Program;
        public bool IsDrumKit;
        public Region[] Regions = Array.Empty<Region>();

        public override string ToString() => $"{(IsDrumKit ? "drum " : "")}{Bank}:{Program} {Name} ({Regions.Length} regions)";
    }

    /// <summary>A loaded set of instruments addressed by bank/program (General MIDI semantics).</summary>
    public sealed class SoundBank
    {
        public const string WindowsGmDls = @"C:\Windows\System32\drivers\gm.dls";
        /// <summary>Environment variable that may point at a .sf2 or .dls file to use instead of the defaults.</summary>
        public const string EnvVar = "OPENAT_SOUNDBANK";

        public string Name;
        /// <summary>"DLS", "SF2" or "Builtin".</summary>
        public string Kind;
        public string SourcePath;
        public readonly List<Instrument> Instruments = new List<Instrument>();

        readonly Dictionary<int, Instrument> _map = new Dictionary<int, Instrument>();
        Instrument _anyMelodic, _anyDrum;

        static int Key(bool drum, int bank, int program) => (drum ? 1 << 24 : 0) | ((bank & 0x3FFF) << 7) | (program & 0x7F);

        public void Add(Instrument inst)
        {
            Instruments.Add(inst);
            int k = Key(inst.IsDrumKit, inst.Bank, inst.Program);
            if (!_map.ContainsKey(k)) _map[k] = inst;
            if (inst.IsDrumKit) { if (_anyDrum == null || inst.Program == 0 && inst.Bank == 0) _anyDrum = inst; }
            else if (_anyMelodic == null) _anyMelodic = inst;
        }

        /// <summary>
        /// Finds the instrument for a bank/program, falling back to bank 0 (GM capital tone) and
        /// then to the standard drum kit / any melodic instrument. Allocation-free.
        /// </summary>
        public Instrument Find(int bank, int program, bool drum)
        {
            if (_map.TryGetValue(Key(drum, bank, program), out var i)) return i;
            if (_map.TryGetValue(Key(drum, (bank >> 7) << 7, program), out i)) return i; // ignore LSB
            if (_map.TryGetValue(Key(drum, 0, program), out i)) return i;
            if (drum)
            {
                if (_map.TryGetValue(Key(true, 0, 0), out i)) return i;
                return _anyDrum;
            }
            return _anyMelodic;
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Loads a .dls or .sf2 file (by content, not extension).</summary>
        public static SoundBank Load(string path)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 12 || Riff.Tag(data, 0) != "RIFF") throw new InvalidDataException("Not a RIFF sound bank: " + path);
            string form = Riff.Tag(data, 8);
            SoundBank b;
            if (form == "DLS ") b = DlsLoader.Load(data);
            else if (form == "sfbk") b = Sf2Loader.Load(data);
            else throw new InvalidDataException("Unknown sound bank form '" + form + "': " + path);
            b.SourcePath = path;
            if (string.IsNullOrEmpty(b.Name)) b.Name = Path.GetFileNameWithoutExtension(path);
            return b;
        }

        /// <summary>Candidate system sound banks, in priority order, for the current machine.</summary>
        public static IEnumerable<string> SystemCandidates()
        {
            var env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env)) yield return env;
            var win = Environment.GetEnvironmentVariable("WINDIR");
            if (!string.IsNullOrEmpty(win)) yield return Path.Combine(win, "System32", "drivers", "gm.dls");
            yield return WindowsGmDls;
            // macOS ships a DLS bank with CoreAudio.
            yield return "/System/Library/Components/CoreAudio.component/Contents/Resources/gs_instruments.dls";
            // Common Linux SoundFont locations (fluid-soundfont-gm, timgm6mb, musescore-general).
            yield return "/usr/share/sounds/sf2/default-GM.sf2";
            yield return "/usr/share/sounds/sf2/FluidR3_GM.sf2";
            yield return "/usr/share/soundfonts/default.sf2";
            yield return "/usr/share/soundfonts/FluidR3_GM.sf2";
            yield return "/usr/share/sounds/sf2/TimGM6mb.sf2";
            yield return "/usr/share/sounds/sf2/MuseScore_General.sf2";
        }

        /// <summary>
        /// Best available bank: an explicit user .sf2/.dls (if given and loadable), then the system
        /// GM bank (Windows gm.dls, macOS gs_instruments.dls, Linux SoundFonts), then the built-in
        /// oscillator bank. Never throws; <paramref name="log"/> receives a line per failed candidate.
        /// </summary>
        public static SoundBank LoadBest(string userBankPath = null, Action<string> log = null)
        {
            if (!string.IsNullOrEmpty(userBankPath))
            {
                var b = TryLoad(userBankPath, log);
                if (b != null) return b;
            }
            foreach (var c in SystemCandidates())
            {
                var b = TryLoad(c, null);
                if (b != null) return b;
            }
            log?.Invoke("No DLS/SF2 sound bank found, using built-in oscillator instruments.");
            return BuiltinBank.Create();
        }

        static SoundBank TryLoad(string path, Action<string> log)
        {
            try
            {
                if (!File.Exists(path)) { log?.Invoke("Sound bank not found: " + path); return null; }
                var b = Load(path);
                return b.Instruments.Count > 0 ? b : null;
            }
            catch (Exception e)
            {
                log?.Invoke("Sound bank " + path + " failed to load: " + e.Message);
                return null;
            }
        }
    }

    /// <summary>Minimal little-endian RIFF chunk helpers.</summary>
    static class Riff
    {
        public struct Chunk
        {
            public string Id;
            /// <summary>LIST/RIFF form type, otherwise null.</summary>
            public string ListType;
            /// <summary>Start of the payload (after the form type for lists).</summary>
            public int Offset;
            /// <summary>Header start (the chunk id).</summary>
            public int HeaderOffset;
            public int Size;
            public int End => Offset + Size;
        }

        public static string Tag(byte[] d, int p) => p + 4 <= d.Length ? System.Text.Encoding.ASCII.GetString(d, p, 4) : "";
        public static ushort U16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));
        public static short S16(byte[] d, int p) => (short)(d[p] | (d[p + 1] << 8));
        public static int S32(byte[] d, int p) => d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);
        public static uint U32(byte[] d, int p) => (uint)S32(d, p);

        /// <summary>Sub-chunks within [start, end).</summary>
        public static List<Chunk> Children(byte[] d, int start, int end)
        {
            var list = new List<Chunk>();
            end = Math.Min(end, d.Length);
            int p = start;
            while (p + 8 <= end)
            {
                var c = new Chunk { Id = Tag(d, p), HeaderOffset = p };
                int size = S32(d, p + 4);
                if (size < 0 || p + 8 + size > end) size = end - p - 8;
                c.Offset = p + 8;
                c.Size = size;
                if ((c.Id == "LIST" || c.Id == "RIFF") && size >= 4)
                {
                    c.ListType = Tag(d, p + 8);
                    c.Offset += 4;
                    c.Size -= 4;
                }
                list.Add(c);
                p += 8 + size + (size & 1);
            }
            return list;
        }

        public static string ZString(byte[] d, int p, int max)
        {
            int n = 0;
            while (n < max && p + n < d.Length && d[p + n] != 0) n++;
            return System.Text.Encoding.ASCII.GetString(d, p, n).Trim();
        }
    }
}
