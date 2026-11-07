using System;

namespace OpenAT.Formats.Midi
{
    /// <summary>
    /// Plays a <see cref="MidiFile"/> through a <see cref="MidiSynth"/> with sample-accurate event
    /// timing. Thread-safe: <see cref="Render(float[], int, int, int)"/> is meant to be called from an
    /// audio thread while <see cref="Load"/>/<see cref="Play"/>/<see cref="Stop"/> come from another
    /// thread. Rendering is allocation-free.
    /// Looping uses the file's loop markers when present, otherwise the whole song.
    /// </summary>
    public sealed class MidiSequencer
    {
        readonly object _lock = new object();

        MidiFile _file;
        MidiEvent[] _events = Array.Empty<MidiEvent>();
        long[] _eventFrames = Array.Empty<long>();
        int _next;
        long _frame, _endFrame, _loopStartFrame, _loopEndFrame;
        int _loopStartIndex;
        volatile bool _playing;

        public MidiSynth Synth { get; }
        public int SampleRate => Synth.SampleRate;
        public MidiFile File => _file;

        /// <summary>Restart at the loop start when the song (or loop end marker) is reached.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>Master volume 0..1.</summary>
        public float Volume
        {
            get => Synth.MasterVolume;
            set => Synth.MasterVolume = Math.Max(0f, value);
        }

        public bool IsPlaying => _playing;
        /// <summary>Number of times playback has wrapped around since <see cref="Play"/>.</summary>
        public int LoopCount { get; private set; }
        public double PositionSeconds => (double)_frame / SampleRate;
        public double DurationSeconds => _file?.DurationSeconds ?? 0;

        public MidiSequencer(MidiSynth synth)
        {
            Synth = synth ?? throw new ArgumentNullException(nameof(synth));
        }

        public MidiSequencer(SoundBank bank, int sampleRate, int maxVoices = 64) : this(new MidiSynth(bank, sampleRate, maxVoices)) { }

        /// <summary>Loads a song (stops the current one). Allocates; do not call from the audio thread.</summary>
        public void Load(MidiFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var events = file.Events;
            var frames = new long[events.Length];
            for (int i = 0; i < events.Length; i++) frames[i] = ToFrame(events[i].Seconds);
            long end = ToFrame(file.DurationSeconds);
            long loopStart = file.LoopStartTick >= 0 ? ToFrame(file.TicksToSeconds(file.LoopStartTick)) : 0;
            long loopEnd = file.LoopEndTick > 0 ? ToFrame(file.TicksToSeconds(file.LoopEndTick)) : end;
            if (loopEnd <= loopStart) { loopStart = 0; loopEnd = end; }
            int loopIndex = 0;
            while (loopIndex < frames.Length && frames[loopIndex] < loopStart) loopIndex++;

            lock (_lock)
            {
                _playing = false;
                _file = file;
                _events = events;
                _eventFrames = frames;
                _endFrame = end;
                _loopStartFrame = loopStart;
                _loopEndFrame = loopEnd;
                _loopStartIndex = loopIndex;
                _next = 0;
                _frame = 0;
                Synth.Reset();
            }
        }

        long ToFrame(double seconds) => (long)Math.Round(seconds * SampleRate);

        /// <summary>Starts the loaded song from the beginning.</summary>
        public void Play(bool loop)
        {
            lock (_lock)
            {
                Loop = loop;
                if (_file == null) return;
                Synth.Reset();
                _next = 0;
                _frame = 0;
                LoopCount = 0;
                _playing = true;
            }
        }

        public void Play() => Play(Loop);

        /// <summary>Stops playback; sounding notes are released (fade out naturally) unless <paramref name="immediate"/>.</summary>
        public void Stop(bool immediate = false)
        {
            lock (_lock)
            {
                _playing = false;
                if (immediate) Synth.AllSoundOff();
                else Synth.AllNotesOff();
            }
        }

        /// <summary>Pauses without resetting the position (held notes are released).</summary>
        public void Pause() => Stop();

        /// <summary>Continues from the current position.</summary>
        public void Resume()
        {
            lock (_lock) { if (_file != null) _playing = true; }
        }

        /// <summary>Jumps to a position; controller/program state is chased from the start.</summary>
        public void Seek(double seconds)
        {
            lock (_lock)
            {
                long target = Math.Max(0, Math.Min(_endFrame, ToFrame(seconds)));
                Synth.Reset();
                int i = 0;
                for (; i < _events.Length && _eventFrames[i] < target; i++)
                {
                    var e = _events[i];
                    int cmd = e.Status & 0xF0;
                    if (cmd == 0xB0 || cmd == 0xC0 || cmd == 0xE0) Synth.Process(e.Status, e.Data1, e.Data2);
                }
                _next = i;
                _frame = target;
            }
        }

        public void Render(float[] interleaved, int frames) => Render(interleaved, 0, frames, 2);

        public void Render(float[] buffer, int frames, int channels) => Render(buffer, 0, frames, channels);

        /// <summary>Renders the next <paramref name="frames"/> frames (overwrites the buffer).</summary>
        public void Render(float[] buffer, int offset, int frames, int channels)
        {
            lock (_lock)
            {
                int done = 0;
                while (done < frames)
                {
                    int n = frames - done;
                    if (_playing)
                    {
                        while (_next < _events.Length && _eventFrames[_next] <= _frame)
                            Dispatch(ref _events[_next++]);

                        long stop = Loop ? _loopEndFrame : _endFrame;
                        if (_frame >= stop)
                        {
                            if (Loop && stop > _loopStartFrame)
                            {
                                Synth.AllNotesOff();
                                _frame = _loopStartFrame;
                                _next = _loopStartIndex;
                                LoopCount++;
                            }
                            else
                            {
                                _playing = false;
                                Synth.AllNotesOff();
                            }
                            continue;
                        }
                        long until = stop;
                        if (_next < _events.Length && _eventFrames[_next] < until) until = _eventFrames[_next];
                        n = (int)Math.Max(1, Math.Min(n, until - _frame));
                    }
                    Synth.Render(buffer, offset + done * channels, n, channels);
                    done += n;
                    if (_playing) _frame += n;
                }
            }
        }

        void Dispatch(ref MidiEvent e)
        {
            if (e.Status < 0x80 || e.Status >= 0xF0) return; // meta (tempo already applied) and sysex
            Synth.Process(e.Status, e.Data1, e.Data2);
        }
    }
}
