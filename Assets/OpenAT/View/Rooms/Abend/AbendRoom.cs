using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Abend
{
    /// <summary>
    /// The evening / day-end screen (rooms-and-ui-8 §12): the airport by day and by night from the
    /// season's abend&lt;n&gt;.gli, crossfaded at dusk (20–22 h) and dawn (5–7 h) while the night runs at
    /// full speed; hourly plane and animal sounds. No hotspots and no exit: <see cref="GameFlow"/> opens
    /// it at the end of the working day and replaces it by the boss office at 09:00.
    /// </summary>
    [Room(RoomIds.Abend)]
    [ScreenName("abend")]
    public sealed class AbendRoom : RoomScreen
    {
        static readonly string[] PlaneSounds = { "PROP.RAW", "FLYBY.RAW", "FLYBY2.RAW", "FLYBY3.RAW", "flyby4.raw", "flyby5.raw" };

        int Season => App?.Session?.World.Clock.Season ?? 0;
        string Lib(int season) => "room/abend" + season + ".gli";
        protected override string Library => Lib(Season);
        protected override string Background => "NACHT";

        int _lastHour = -1, _nightSeason = -1;
        float _busyUntil;
        readonly System.Random _rng = new System.Random();

        protected override void OnRoomEnter()
        {
            var c = Clock;
            if (c == null) return;
            _lastHour = c.Hour;
            _nightSeason = Season;
            if (_rng.Next(3) == 0) PlayOne(c.Time <= Sim.Core.GameClock.At(17) ? "SOUND/BIRDS.RAW" : "SOUND/Hupe.raw");
        }

        /// <summary>The screen plays one sound at a time: a new one only when the last has finished (§12.5).</summary>
        void PlayOne(string path)
        {
            if (Time.unscaledTime < _busyUntil) return;
            var clip = Assets.Sound(path);
            if (clip == null) return;
            App.Audio.Play(path);
            _busyUntil = Time.unscaledTime + clip.length;
        }

        /// <summary>The night runs on its own: the original shows the WAIT cursor the whole time.</summary>
        protected override void Hotspots(UI.InputState input) => App.Ui.Cursor = UI.CursorLook.Wait;

        /// <summary>Right click / Esc only end the "sequence" (the night keeps running); there is no way out.</summary>
        protected override void Leave() { }

        public override void Animate(float dt)
        {
            var c = Clock;
            if (c == null || c.Hour == _lastHour) return;
            int h = c.Hour;
            _lastHour = h;
            if (_rng.Next(5) == 0)
            {
                if (_rng.Next(3) < 2) PlayOne("SOUND/" + PlaneSounds[_rng.Next(PlaneSounds.Length)]);
                else
                {
                    string[] animals = h >= 10 && h <= 21 ? new[] { "chilp.raw", "Chilp2.raw", "Bark.raw" }
                        : h >= 22 || h <= 2 ? new[] { "Eule.raw", "loon.raw", "zirpen.raw" }
                        : h == 6 ? new[] { "hahn.raw" } : null;
                    if (animals != null) PlayOne("SOUND/" + animals[_rng.Next(animals.Length)]);
                }
            }
            else if (h == 6 && _rng.Next(3) == 0) PlayOne("SOUND/hahn.raw");
        }

        /// <summary>Day-picture weight 0..8 by game hour (§12.4).</summary>
        public static int DayWeight(int hour, int minute)
        {
            if (hour >= 8 && hour <= 19) return 8;
            if (hour <= 4 || hour >= 23) return 0;
            if (hour >= 5 && hour <= 7) return ((hour - 5) * 60 + minute) * 8 / 180;
            return 8 - ((hour - 20) * 60 + minute) * 8 / 180;
        }

        protected override void DrawRoom(Screen2D s)
        {
            var c = Clock;
            if (c == null) return;
            string lib = Lib(Season);
            int day = DayWeight(c.Hour, c.Minute);
            int ox = RX;
            // A season change during the night swaps only the day picture; the night picture stays (§12.3).
            s.Draw(Assets.Texture(Lib(_nightSeason >= 0 ? _nightSeason : Season), "NACHT"), ox, 0);
            if (day > 0) s.Draw(Assets.Texture(lib, "AIRPORT"), ox, 0, new Color32(255, 255, 255, (byte)(day * 255 / 8)));
        }
    }
}
