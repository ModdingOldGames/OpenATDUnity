using System;
using System.IO;
using OpenAT.Formats.Midi;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View
{
    /// <summary>
    /// Background music: plays the original MIDI songs (e.g. "SOUND/title.mid") through OpenAT's own
    /// software synthesizer, rendered on Unity's audio thread in <see cref="OnAudioFilterRead"/>.
    /// Instruments come from the user's machine at run time (see <see cref="SoundBank.LoadBest"/>):
    /// <see cref="SoundBankPath"/> if set, else the system GM bank (Windows gm.dls), else built-in oscillators.
    ///
    /// Put this on its own GameObject (OnAudioFilterRead filters the AudioSource of the same object);
    /// <see cref="Create"/> does that for you.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        public GameAssets Assets;
        /// <summary>Optional .sf2/.dls chosen by the user; overrides the system bank when it loads.</summary>
        public string SoundBankPath;
        public int MaxVoices = 64;

        AudioSource _source;
        MidiSequencer _seq;
        SoundBank _bank;
        int _rate;
        float _volume = 1f;
        MidiFile _currentFile;
        bool _currentLoop;

        /// <summary>Install-relative path of the current song, or null.</summary>
        public string Current { get; private set; }
        public bool IsPlaying => _seq != null && _seq.IsPlaying;
        public double PositionSeconds => _seq?.PositionSeconds ?? 0;
        public double DurationSeconds => _seq?.DurationSeconds ?? 0;
        /// <summary>The loaded sound bank (null until the first <see cref="Play"/> or <see cref="Preload"/>).</summary>
        public SoundBank Bank => _bank;

        /// <summary>Music volume 0..1.</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Max(0f, value);
                if (_seq != null) _seq.Volume = _volume;
            }
        }

        /// <summary>Creates a child GameObject carrying an AudioSource and a MusicPlayer.</summary>
        public static MusicPlayer Create(Transform parent, GameAssets assets)
        {
            var go = new GameObject("OpenAT Music");
            go.transform.SetParent(parent, false);
            go.AddComponent<AudioSource>();
            var m = go.AddComponent<MusicPlayer>();
            m.Assets = assets;
            return m;
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.priority = 0;
            _source.volume = 1f;
            _rate = AudioSettings.outputSampleRate;
            // A silent looping carrier keeps the source (and with it our filter) running.
            _source.clip = AudioClip.Create("OpenAT music carrier", _rate, 2, _rate, false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            _seq = null;
        }

        /// <summary>Loads the sound bank now instead of on the first <see cref="Play"/>.</summary>
        public void Preload() => EnsureSequencer();

        /// <summary>
        /// Plays a song given relative to the install root (e.g. "SOUND/title.mid"); absolute paths
        /// also work. Calling it again for the song that is already playing only updates the loop flag.
        /// </summary>
        public void Play(string relativePathInInstall, bool loop)
        {
            if (string.IsNullOrEmpty(relativePathInInstall)) { Stop(); return; }
            if (IsPlaying && string.Equals(Current, relativePathInInstall, StringComparison.OrdinalIgnoreCase))
            {
                _seq.Loop = loop;
                return;
            }

            string path = Assets?.Install?.Find(relativePathInInstall);
            if (path == null && File.Exists(relativePathInInstall)) path = relativePathInInstall;
            if (path == null)
            {
                Debug.LogWarning("OpenAT: missing music " + relativePathInInstall);
                return;
            }

            MidiFile file;
            try { file = MidiFile.Load(path); }
            catch (Exception e)
            {
                Debug.LogWarning("OpenAT: cannot read music " + relativePathInInstall + ": " + e.Message);
                return;
            }

            EnsureSequencer();
            _seq.Load(file);
            _seq.Volume = _volume;
            _seq.Play(loop);
            _currentFile = file;
            _currentLoop = loop;
            Current = relativePathInInstall;
            if (!_source.isPlaying) _source.Play();
        }

        /// <summary>Stops the music; sounding notes fade out with their release.</summary>
        public void Stop()
        {
            _seq?.Stop();
            Current = null;
            _currentFile = null;
        }

        void EnsureSequencer()
        {
            if (_seq != null) return;
            if (_bank == null)
            {
                _bank = SoundBank.LoadBest(SoundBankPath, msg => Debug.LogWarning("OpenAT: " + msg));
                Debug.Log($"OpenAT: music sound bank {_bank.Kind} '{_bank.Name}' ({_bank.Instruments.Count} instruments)");
            }
            var seq = new MidiSequencer(_bank, _rate, MaxVoices) { Volume = _volume };
            _seq = seq;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            int rate = AudioSettings.outputSampleRate;
            if (rate == _rate || rate <= 0) return;
            _rate = rate;
            _seq = null;
            if (_source != null)
            {
                _source.clip = AudioClip.Create("OpenAT music carrier", _rate, 2, _rate, false);
                if (_currentFile != null) _source.Play();
            }
            if (_currentFile != null)
            {
                EnsureSequencer();
                _seq.Load(_currentFile);
                _seq.Play(_currentLoop);
            }
        }

        // Audio thread.
        void OnAudioFilterRead(float[] data, int channels)
        {
            var seq = _seq;
            if (seq == null || channels <= 0) return;
            seq.Render(data, 0, data.Length / channels, channels);
        }
    }
}
