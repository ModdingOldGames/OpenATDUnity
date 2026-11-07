using System.Collections.Generic;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View
{
    /// <summary>
    /// Sound effects, ambience and voice playback for original .raw sounds. Volumes follow the
    /// options screen (0..1). Voice lines are AudioClips built by the dialog engine.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public const string PrefEffects = "OpenAT.EffectsVolume", PrefVoice = "OpenAT.VoiceVolume", PrefAmbience = "OpenAT.AmbienceVolume";

        public GameAssets Assets;
        public float EffectsVolume = 1f;
        public float VoiceVolume = 1f;
        public float AmbienceVolume = 1f;

        AudioSource _fx, _voice;
        readonly List<AudioSource> _loops = new List<AudioSource>();

        void Awake()
        {
            _fx = gameObject.AddComponent<AudioSource>();
            _voice = gameObject.AddComponent<AudioSource>();
            _fx.playOnAwake = _voice.playOnAwake = false;
            EffectsVolume = PlayerPrefs.GetFloat(PrefEffects, 1f);
            VoiceVolume = PlayerPrefs.GetFloat(PrefVoice, 1f);
            AmbienceVolume = PlayerPrefs.GetFloat(PrefAmbience, 1f);
        }

        public void SaveVolumes()
        {
            PlayerPrefs.SetFloat(PrefEffects, EffectsVolume);
            PlayerPrefs.SetFloat(PrefVoice, VoiceVolume);
            PlayerPrefs.SetFloat(PrefAmbience, AmbienceVolume);
            PlayerPrefs.Save();
        }

        /// <summary>Plays e.g. "SOUND/CLICK.RAW".</summary>
        public void Play(string path, float volume = 1f)
        {
            var clip = Assets?.Sound(path);
            if (clip != null) _fx.PlayOneShot(clip, volume * EffectsVolume);
        }

        /// <summary>Ambient one-shot (room background sounds) at the ambience volume.</summary>
        public void PlayAmbient(string path, float volume = 1f)
        {
            var clip = Assets?.Sound(path);
            if (clip != null) _fx.PlayOneShot(clip, volume * AmbienceVolume);
        }

        public void PlayVoice(string path)
        {
            var clip = Assets?.Sound(path);
            if (clip != null) PlayVoice(clip);
        }

        public void PlayVoice(AudioClip clip)
        {
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = VoiceVolume;
            _voice.Play();
        }

        public bool VoicePlaying => _voice.isPlaying;
        /// <summary>Current voice playback position in samples (for lip-sync look-ahead).</summary>
        public int VoiceSample => _voice.isPlaying ? _voice.timeSamples : 0;
        public AudioClip VoiceClip => _voice.clip;
        public void StopVoice() => _voice.Stop();

        public void Click() => Play("SOUND/CLICK.RAW");

        /// <summary>Starts a looping sound (e.g. options ambience sample); returns a handle for <see cref="StopLoop"/>.</summary>
        public AudioSource PlayLoop(string path, float volume)
        {
            var clip = Assets?.Sound(path);
            if (clip == null) return null;
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.volume = volume; src.Play();
            _loops.Add(src);
            return src;
        }

        public void StopLoop(AudioSource src)
        {
            if (src == null) return;
            _loops.Remove(src);
            Destroy(src);
        }

        // ------------------------------------------------------------------ airport ambience (airport-and-people.md §10)

        /// <summary>Ambient loop channels: crowd murmur, planes taxiing behind the windows, the airfield.</summary>
        public const int AmbientPeople = 0, AmbientJetOutside = 1, AmbientJetField = 2;
        // The original's "outside" and "field" file names look swapped relative to their channel names; kept.
        static readonly string[] AmbientFiles = { "SOUND/Raunen.raw", "SOUND/jetfield.raw", "SOUND/jetout.raw" };
        static readonly string[] FlyBySounds = { "prop", "flyby", "flyby2", "flyby3", "flyby4", "flyby5" };

        readonly AudioSource[] _ambient = new AudioSource[3];
        readonly int[] _ambientLevel = new int[3];
        AudioSource _flyBy;
        float _nextFlyBy = -1;
        readonly System.Random _flyByRng = new System.Random();

        /// <summary>
        /// The global ambient volume in percent (0..100). Every location sets it on entry (<see cref="RoomAmbience"/>):
        /// it scales the airport's ambient loops, the random plane fly-bys and (squared) the airport announcements, so
        /// the hall is heard quietly from inside a room and not at all in the evening or the options.
        /// </summary>
        public int GlobalAmbientVolume { get; private set; } = RoomAmbience.Hall;

        /// <summary>Global ambient volume as a 0..1 factor.</summary>
        public float GlobalAmbient => GlobalAmbientVolume / 100f;

        /// <summary>
        /// Sets the global ambient volume (0..100) and re-mixes the loops. Quirk of the original kept: the re-mix
        /// silences the two plane loops (its recalculation turns their level into a decibel value, which always
        /// clamps to 0), so inside rooms only the crowd is heard until the hall sets the plane loops again.
        /// </summary>
        public void SetGlobalAmbientVolume(int percent)
        {
            int v = Mathf.Clamp(percent, 0, 100);
            if (v != GlobalAmbientVolume) Debug.Log("OpenAT audio: global ambient volume " + v);
            GlobalAmbientVolume = v;
            _ambientLevel[AmbientJetOutside] = _ambientLevel[AmbientJetField] = 0;
            for (int c = 0; c < _ambient.Length; c++) ApplyAmbient(c);
            if (_flyBy != null) _flyBy.volume = PlaneVolume * GlobalAmbient;
        }

        /// <summary>Sets one ambient channel's level (0..100, before the global volume); the hall calls it every frame.</summary>
        public void SetAmbientLevel(int channel, int level)
        {
            if ((uint)channel >= (uint)_ambient.Length) return;
            _ambientLevel[channel] = Mathf.Clamp(level, 0, 100);
            ApplyAmbient(channel);
        }

        /// <summary>Stops the ambient loops (the game session ended).</summary>
        public void StopAmbience()
        {
            for (int c = 0; c < _ambient.Length; c++)
            {
                _ambientLevel[c] = 0;
                if (_ambient[c] != null) _ambient[c].Stop();
            }
            if (_flyBy != null) _flyBy.Stop();
            _nextFlyBy = -1;
            GlobalAmbientVolume = RoomAmbience.Hall;
        }

        static float PlaneVolume => PlayerPrefs.GetFloat(Screens.OptionsScreen.Prefix + "Planes", 1f);

        void ApplyAmbient(int c)
        {
            // level × global, then the ambience option (the original's 0..7 setting / 8).
            float v = Mathf.Clamp(_ambientLevel[c] * GlobalAmbientVolume / 100, 0, 100) / 100f * AmbienceVolume * 7f / 8f;
            var src = _ambient[c];
            if (v <= 0)
            {
                if (src != null && src.isPlaying) src.Stop();
                return;
            }
            if (src == null)
            {
                var clip = Assets?.Sound(AmbientFiles[c]);
                if (clip == null) return;
                src = _ambient[c] = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.clip = clip;
                Debug.Log("OpenAT audio: ambient loop " + AmbientFiles[c]);
            }
            src.volume = v;
            if (!src.isPlaying) src.Play();
        }

        /// <summary>
        /// Random plane fly-bys (airport-and-people.md §10): while a game runs (not paused, no options) and sound
        /// effects are on, one of six fly-by sounds every 15..54 s of real time (the first at once) at the plane
        /// volume × the global ambient volume, wherever the player is.
        /// </summary>
        void Update()
        {
            var session = GameApp.Instance?.Session;
            if (session == null || Assets == null) { _nextFlyBy = -1; return; }
            if (session.HoldTime || session.World.Paused || EffectsVolume <= 0) return;
            float now = Time.unscaledTime;
            if (_nextFlyBy >= 0 && now < _nextFlyBy) return;
            _nextFlyBy = now + 15 + _flyByRng.Next(40);
            var clip = Assets.Sound("SOUND/" + FlyBySounds[_flyByRng.Next(FlyBySounds.Length)] + ".raw");
            if (clip == null) return;
            if (_flyBy == null) { _flyBy = gameObject.AddComponent<AudioSource>(); _flyBy.playOnAwake = false; }
            _flyBy.Stop();
            _flyBy.clip = clip;
            _flyBy.volume = PlaneVolume * GlobalAmbient;
            _flyBy.Play();
        }
    }
}
