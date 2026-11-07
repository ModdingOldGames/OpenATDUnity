using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using OpenAT.View.Screens;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// The airport announcer (rooms-and-ui-3 §5.6, airport-and-people.md §10/§12): a queue of samples played one after
    /// the other from <c>SOUND/</c> at the announcements volume × the global ambient volume squared, so inside a room
    /// (global 30..60) it is heard faintly and in the evening or the options not at all. The queue only advances
    /// between 09:00 and 18:00; it is emptied when another game is loaded or the session ends, not on room changes.
    /// <list type="bullet">
    /// <item>Landing (<see cref="HallLandingAnnouncement"/>, 09:00–18:00, not after call-it-a-day): <c>der&lt;airline&gt;flug</c>,
    /// the origin city's wave and <c>gelande2</c>, when nothing is being announced and the city has a recorded wave;
    /// another airline's landing is skipped while one of the local airline's planes is landing.</item>
    /// <item>Half-hourly notice (<see cref="HallAnnouncements"/>): at hh:30 of the even hours, when nothing is queued or
    /// playing, the clock runs (no pause, no options, not frozen), the figure is not warping and the local game
    /// speed is below 2. Checked every frame, so a notice that ends within its minute is repeated, as in the original.</item>
    /// </list>
    /// </summary>
    public sealed class HallAnnouncer : MonoBehaviour
    {
        readonly Queue<string> _queue = new Queue<string>();
        AudioSource _src;
        World _world;
        IDisposable _sub;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.HallAnnouncer");
            DontDestroyOnLoad(go);
            go.AddComponent<HallAnnouncer>();
        }

        void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
        }

        static float Volume
        {
            get
            {
                float a = PlayerPrefs.GetFloat(OptionsScreen.Prefix + "Announcements", 1f);
                float g = GameApp.Instance?.Audio != null ? GameApp.Instance.Audio.GlobalAmbient : 1f;
                return a * g * g;
            }
        }

        bool Busy => _queue.Count > 0 || (_src != null && _src.isPlaying);

        void OnLanding(HallLandingAnnouncement e)
        {
            var app = GameApp.Instance;
            var w = _world;
            if (app?.Assets == null || w == null || Busy || w.CallItADay) return;
            if (w.Clock.Hour < 9 || w.Clock.Hour >= 18) return;
            var city = w.Data.Cities.Find(e.FromCity);
            if (city == null || string.IsNullOrEmpty(city.Wave) || city.Wave == "-") return;
            if (e.Airline != w.LocalPlayer)
                foreach (var pl in w.Local.Planes.ToList())
                    if (pl.Location == PlaneLocation.Landing) return;
            string wave = city.Wave.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) ? city.Wave.Substring(0, city.Wave.Length - 4) : city.Wave;
            Debug.Log("OpenAT announcer: " + w.Clock.ClockText + " landing of airline " + e.Airline + " from " + wave);
            _queue.Enqueue("der" + e.Airline + "flug");
            _queue.Enqueue(wave);
            _queue.Enqueue("gelande2");
        }

        void HalfHourNotice(GameApp app, World w)
        {
            var c = w.Clock;
            if (Busy || w.CallItADay || w.Paused || c.Frozen || !HallAnnouncements.NoticeDue(c)) return;
            var session = app.Session;
            if (session == null || session.HoldTime || session.Driver.Warp || w.Local.GameSpeed >= 2) return;
            string sample = HallAnnouncements.NoticeSample(c);
            Debug.Log("OpenAT announcer: " + c.ClockText + " notice " + sample);
            _queue.Enqueue(sample);
        }

        void Update()
        {
            var app = GameApp.Instance;
            var w = app?.Session?.World;
            if (w != _world)
            {
                _sub?.Dispose();
                _sub = null;
                _world = w;
                _queue.Clear();
                if (_src.isPlaying) _src.Stop();
                if (w != null) _sub = w.Events.Subscribe<HallLandingAnnouncement>(OnLanding);
            }
            if (w == null || app.Assets == null || w.Hall == null) return;
            HalfHourNotice(app, w);
            if (_src.isPlaying) return;   // the volume is fixed when a sample starts
            if (w.Clock.Time <= GameClock.DayStart || w.Clock.Time >= GameClock.DayEnd || _queue.Count == 0) return;
            var clip = app.Assets.Sound("SOUND/" + _queue.Dequeue() + ".raw");
            if (clip == null) return;
            _src.clip = clip;
            _src.volume = Volume;
            _src.Play();
            Debug.Log("OpenAT announcer: plays " + clip.name + " at volume " + _src.volume.ToString("0.00"));
        }
    }
}
