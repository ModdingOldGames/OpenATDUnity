using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenAT.Formats.Midi
{
    /// <summary>One event of a parsed MIDI file, with its absolute time.</summary>
    public struct MidiEvent
    {
        /// <summary>Absolute position in ticks.</summary>
        public long Tick;
        /// <summary>Absolute position in seconds (tempo map applied).</summary>
        public double Seconds;
        /// <summary>Index of the source track.</summary>
        public int Track;
        /// <summary>Status byte: 0x80..0xEF channel message, 0xF0/0xF7 sysex, 0xFF meta.</summary>
        public byte Status;
        public byte Data1;
        public byte Data2;
        /// <summary>Meta event type (only when <see cref="Status"/> is 0xFF).</summary>
        public byte MetaType;
        /// <summary>Payload of meta and sysex events, otherwise null.</summary>
        public byte[] Data;

        public bool IsChannelMessage => Status >= 0x80 && Status < 0xF0;
        public bool IsMeta => Status == 0xFF;
        public int Channel => Status & 0x0F;
        public int Command => Status & 0xF0;
        public string Text => Data == null ? null : Encoding.GetEncoding(28591).GetString(Data);

        public override string ToString()
        {
            if (IsMeta) return $"{Tick} meta {MetaType:X2} {Text}";
            return $"{Tick} {Status:X2} {Data1} {Data2}";
        }
    }

    /// <summary>
    /// Standard MIDI File (SMF format 0/1/2, optionally wrapped in RIFF RMID) parser.
    /// All tracks are merged into one time-ordered event list; a tempo map converts ticks to seconds.
    /// Recognised loop points: marker/cue texts "loopStart"/"loopEnd" (also "loop start", "[", "]"),
    /// CC 111 (loop start, RPG Maker convention) and CC 116/117 (EMIDI loop start/end).
    /// </summary>
    public sealed class MidiFile
    {
        public const byte MetaSequenceNumber = 0x00, MetaText = 0x01, MetaCopyright = 0x02, MetaTrackName = 0x03,
            MetaInstrument = 0x04, MetaLyric = 0x05, MetaMarker = 0x06, MetaCue = 0x07, MetaChannelPrefix = 0x20,
            MetaEndOfTrack = 0x2F, MetaTempo = 0x51, MetaSmpte = 0x54, MetaTimeSignature = 0x58, MetaKeySignature = 0x59;

        /// <summary>A tempo change: from <see cref="Tick"/> on, a quarter note lasts <see cref="MicrosecondsPerQuarter"/>.</summary>
        public struct TempoChange
        {
            public long Tick;
            public double Seconds;
            public int MicrosecondsPerQuarter;
            public double Bpm => 60000000.0 / MicrosecondsPerQuarter;
        }

        public int Format { get; private set; }
        public int TrackCount { get; private set; }
        /// <summary>Ticks per quarter note (or ticks per second for SMPTE files, see <see cref="IsSmpte"/>).</summary>
        public int Division { get; private set; }
        public bool IsSmpte { get; private set; }

        /// <summary>All events of all tracks, sorted by tick (stable: track order, then file order).</summary>
        public MidiEvent[] Events { get; private set; }
        public TempoChange[] Tempos { get; private set; }
        public string[] TrackNames { get; private set; }

        /// <summary>Length up to the last end-of-track (or last event).</summary>
        public long DurationTicks { get; private set; }
        public double DurationSeconds { get; private set; }

        /// <summary>Loop start in ticks, -1 when the file has no loop start marker.</summary>
        public long LoopStartTick { get; private set; } = -1;
        /// <summary>Loop end in ticks, -1 when the file has no loop end marker.</summary>
        public long LoopEndTick { get; private set; } = -1;
        public bool HasLoopPoints => LoopStartTick >= 0 || LoopEndTick >= 0;

        public static MidiFile Load(string path) => Parse(File.ReadAllBytes(path));

        public static MidiFile Parse(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var f = new MidiFile();
            f.Read(data);
            return f;
        }

        /// <summary>Converts an absolute tick position into seconds using the tempo map.</summary>
        public double TicksToSeconds(long tick)
        {
            if (IsSmpte) return tick / (double)Division;
            var t = Tempos;
            int lo = 0, hi = t.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (t[mid].Tick <= tick) lo = mid; else hi = mid - 1;
            }
            return t[lo].Seconds + (tick - t[lo].Tick) * (t[lo].MicrosecondsPerQuarter / 1e6) / Division;
        }

        // ------------------------------------------------------------------ parsing

        struct Raw
        {
            public MidiEvent Ev;
            public int Order;
        }

        void Read(byte[] d)
        {
            int pos = 0, end = d.Length;
            // RIFF RMID wrapper: RIFF <size> RMID data <size> <SMF>
            if (end >= 20 && Tag(d, 0) == "RIFF" && Tag(d, 8) == "RMID")
            {
                int p = 12;
                while (p + 8 <= end)
                {
                    int sz = (int)ReadLE32(d, p + 4);
                    if (Tag(d, p) == "data") { pos = p + 8; end = Math.Min(end, pos + sz); break; }
                    p += 8 + sz + (sz & 1);
                }
            }
            if (end - pos < 14 || Tag(d, pos) != "MThd") throw new InvalidDataException("Not a MIDI file (missing MThd)");
            int hlen = (int)ReadBE32(d, pos + 4);
            Format = ReadBE16(d, pos + 8);
            int declaredTracks = ReadBE16(d, pos + 10);
            int div = ReadBE16(d, pos + 12);
            if ((div & 0x8000) != 0)
            {
                IsSmpte = true;
                int fps = -(sbyte)(div >> 8);
                if (fps == 29) fps = 30; // 29.97 drop frame: close enough
                Division = Math.Max(1, fps * (div & 0xFF));
            }
            else Division = Math.Max(1, div);
            pos += 8 + hlen;

            var raw = new List<Raw>(4096);
            var names = new List<string>();
            int track = 0;
            long lastTick = 0;
            while (pos + 8 <= end && (track < declaredTracks || declaredTracks == 0 || Tag(d, pos) == "MTrk"))
            {
                string tag = Tag(d, pos);
                long len = ReadBE32(d, pos + 4);
                int start = pos + 8;
                int stop = (int)Math.Min(end, start + len);
                pos = stop;
                if (tag != "MTrk") continue;
                names.Add(null);
                long trackEnd = ReadTrack(d, start, stop, track, raw, names);
                lastTick = Math.Max(lastTick, trackEnd);
                track++;
            }
            TrackCount = track;
            TrackNames = names.ToArray();

            // Stable merge by tick: sort by (tick, track, order).
            raw.Sort((a, b) =>
            {
                int c = a.Ev.Tick.CompareTo(b.Ev.Tick);
                if (c != 0) return c;
                c = a.Ev.Track.CompareTo(b.Ev.Track);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });

            // Tempo map
            var tempos = new List<TempoChange> { new TempoChange { Tick = 0, Seconds = 0, MicrosecondsPerQuarter = 500000 } };
            foreach (var r in raw)
            {
                var e = r.Ev;
                if (e.Status != 0xFF || e.MetaType != MetaTempo || e.Data == null || e.Data.Length < 3) continue;
                int us = (e.Data[0] << 16) | (e.Data[1] << 8) | e.Data[2];
                if (us <= 0) continue;
                var last = tempos[tempos.Count - 1];
                double sec = last.Seconds + (e.Tick - last.Tick) * (last.MicrosecondsPerQuarter / 1e6) / Division;
                if (last.Tick == e.Tick) tempos[tempos.Count - 1] = new TempoChange { Tick = e.Tick, Seconds = last.Seconds, MicrosecondsPerQuarter = us };
                else tempos.Add(new TempoChange { Tick = e.Tick, Seconds = sec, MicrosecondsPerQuarter = us });
            }
            Tempos = tempos.ToArray();

            var events = new MidiEvent[raw.Count];
            for (int i = 0; i < events.Length; i++)
            {
                var e = raw[i].Ev;
                e.Seconds = TicksToSeconds(e.Tick);
                events[i] = e;
                DetectLoop(ref e);
            }
            Events = events;
            if (events.Length > 0) lastTick = Math.Max(lastTick, events[events.Length - 1].Tick);
            DurationTicks = lastTick;
            DurationSeconds = TicksToSeconds(lastTick);
        }

        void DetectLoop(ref MidiEvent e)
        {
            if (e.IsMeta && (e.MetaType == MetaMarker || e.MetaType == MetaCue || e.MetaType == MetaText) && e.Data != null)
            {
                string t = e.Text.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (LoopStartTick < 0 && (t == "loopstart" || t == "[" || t == "loop")) LoopStartTick = e.Tick;
                else if (LoopEndTick < 0 && (t == "loopend" || t == "]")) LoopEndTick = e.Tick;
            }
            else if (e.Command == 0xB0)
            {
                if ((e.Data1 == 111 || e.Data1 == 116) && LoopStartTick < 0) LoopStartTick = e.Tick;
                else if (e.Data1 == 117 && LoopEndTick < 0) LoopEndTick = e.Tick;
            }
        }

        static long ReadTrack(byte[] d, int p, int stop, int track, List<Raw> raw, List<string> names)
        {
            long tick = 0;
            int running = 0;
            int order = 0;
            while (p < stop)
            {
                long delta = ReadVarLen(d, ref p, stop);
                if (delta < 0) break;
                tick += delta;
                if (p >= stop) break;
                int status = d[p];
                if (status < 0x80)
                {
                    if (running == 0) { p++; continue; } // stray data byte, skip
                    status = running;
                }
                else p++;

                var ev = new MidiEvent { Tick = tick, Track = track, Status = (byte)status };
                if (status == 0xFF)
                {
                    running = 0;
                    if (p >= stop) break;
                    ev.MetaType = d[p++];
                    long len = ReadVarLen(d, ref p, stop);
                    if (len < 0) break;
                    int n = (int)Math.Min(len, stop - p);
                    ev.Data = new byte[n];
                    Buffer.BlockCopy(d, p, ev.Data, 0, n);
                    p += n;
                    if (ev.MetaType == MetaTrackName && names[track] == null) names[track] = ev.Text;
                    if (ev.MetaType == MetaEndOfTrack)
                    {
                        raw.Add(new Raw { Ev = ev, Order = order++ });
                        return tick;
                    }
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    running = 0;
                    long len = ReadVarLen(d, ref p, stop);
                    if (len < 0) break;
                    int n = (int)Math.Min(len, stop - p);
                    ev.Data = new byte[n];
                    Buffer.BlockCopy(d, p, ev.Data, 0, n);
                    p += n;
                }
                else if (status >= 0xF1)
                {
                    // System common/realtime messages are not valid in SMF; skip their data bytes.
                    running = 0;
                    int skip = status == 0xF2 ? 2 : status == 0xF1 || status == 0xF3 ? 1 : 0;
                    p += skip;
                    continue;
                }
                else
                {
                    running = status;
                    int cmd = status & 0xF0;
                    int nData = cmd == 0xC0 || cmd == 0xD0 ? 1 : 2;
                    if (p + nData > stop) break;
                    ev.Data1 = (byte)(d[p++] & 0x7F);
                    if (nData == 2) ev.Data2 = (byte)(d[p++] & 0x7F);
                }
                raw.Add(new Raw { Ev = ev, Order = order++ });
            }
            return tick;
        }

        static long ReadVarLen(byte[] d, ref int p, int stop)
        {
            long v = 0;
            for (int i = 0; i < 4; i++)
            {
                if (p >= stop) return -1;
                byte b = d[p++];
                v = (v << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return v;
            }
            return v;
        }

        static string Tag(byte[] d, int p) => p + 4 <= d.Length ? Encoding.ASCII.GetString(d, p, 4) : "";
        static int ReadBE16(byte[] d, int p) => (d[p] << 8) | d[p + 1];
        static long ReadBE32(byte[] d, int p) => ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];
        static long ReadLE32(byte[] d, int p) => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
    }
}
