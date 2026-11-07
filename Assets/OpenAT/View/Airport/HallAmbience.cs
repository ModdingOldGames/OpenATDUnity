using System;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.Airport
{
    /// <summary>
    /// Levels of the airport's three ambient loops (airport-and-people.md §10.1), fed into
    /// <see cref="GameAudio.SetAmbientLevel"/> at 20 Hz:
    /// <list type="bullet">
    /// <item>crowd (<c>Raunen</c>): 4 × persons near the view + 25 (× 2/3 upstairs), moving by 1 per step — only while the
    /// hall is shown; inside rooms it keeps its last level, scaled by the room's global ambient volume;</item>
    /// <item>planes behind the windows (<c>jetfield</c>): the taxiing planes' loudness by distance from the view centre,
    /// moving by up to 3 per step — always, also from inside rooms (the original computes it in the plane step);</item>
    /// <item>airfield (<c>jetout</c>): louder towards the workshop end — only while the hall is shown.</item>
    /// </list>
    /// The view position is the hall camera's last one. WS: the view centre is ViewX + width / 2.
    /// </summary>
    public sealed class HallAmbience : MonoBehaviour
    {
        const float StepSeconds = 0.05f;
        float _acc;
        int _people = -1, _planes;
        bool _wasShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.HallAmbience");
            DontDestroyOnLoad(go);
            go.AddComponent<HallAmbience>();
        }

        void Update()
        {
            var app = GameApp.Instance;
            var w = app?.Session?.World;
            var h = w?.Hall;
            var view = AirportScreen.Shown;
            if (app?.Audio == null || h == null || view == null) { _people = -1; _planes = 0; _wasShown = false; return; }
            _acc += Time.unscaledDeltaTime;
            if (_acc < StepSeconds) return;
            _acc = Mathf.Min(_acc - StepSeconds, StepSeconds);

            int viewX = view.ViewX, width = view.ViewWidth;
            bool shown = app.Rooms.Locations.Count == 0;
            if (shown && !_wasShown) _people = -1;   // the airport view starts the crowd level afresh
            _wasShown = shown;

            int planes = PlanesLevel(h, viewX, width);
            if (_planes < planes) _planes = Math.Min(_planes + 3, planes);
            if (_planes > planes) _planes = Math.Max(_planes - 3, planes);
            app.Audio.SetAmbientLevel(GameAudio.AmbientJetOutside, _planes);

            if (!shown) return;
            int people = PeopleOnScreen(h, w.LocalPlayer, viewX, width);
            if (_people < 0) _people = people;
            else if (_people < people) _people++;
            else if (_people > people) _people--;
            app.Audio.SetAmbientLevel(GameAudio.AmbientPeople, _people * 4 + 25);
            app.Audio.SetAmbientLevel(GameAudio.AmbientJetField, 150 - (h.RightEnd - (viewX + width / 2)) / 6);
        }

        /// <summary>Persons whose foot point lies within (−260, width + 250) of the view; × 2/3 when the player is upstairs.</summary>
        public static int PeopleOnScreen(HallState h, int local, int viewX, int width)
        {
            int n = 0;
            foreach (var p in h.Persons)
            {
                if (!p.Alive) continue;
                int x = p.ScreenPos.X - viewX - 7;
                if (x > -260 && x < width + 250) n++;
            }
            var me = h.PlayerPerson(local);
            if (me != null && me.Position.Y >= 5000) n = n * 2 / 3;
            return n;
        }

        /// <summary>
        /// Sum over planes taxiing behind the windows of (speed + 3) × T / 6, speed = clamp(|x − target| / 30, 3, 10),
        /// T = clamp(150 − |x − view centre| / 4, 0, 200), damped inside full-clip stretches by 170 / (170 + distance to
        /// the stretch's nearer end).
        /// </summary>
        public static int PlanesLevel(HallState h, int viewX, int width)
        {
            int sum = 0;
            var clips = h.Layout.ClipMarkers;
            foreach (var r in h.Planes)
            {
                if (r.Ort != PlaneLocation.AtGate) continue;
                int x = r.Pos.X;
                int speed = Math.Max(3, Math.Min(10, Math.Abs(x - r.TargetX) / 30));
                int t = 150 - Math.Abs(x - width / 2 - viewX) / 4;
                if (t < 0) t = 0;
                if (t > 200) t = 200;
                int drawn = 0;
                for (int c = 1; c < clips.Count; c++)
                {
                    if (clips[c - 1].Type == RuneId.ClipFull && x >= drawn && x <= clips[c].X)
                        t = t * 170 / (170 + Math.Min(Math.Abs(x - drawn), Math.Abs(x - clips[c].X)));
                    drawn = clips[c].X;
                }
                sum += (speed + 3) * t / 6;
            }
            return sum;
        }
    }
}
