using OpenAT.Sim;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Stats;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Aufsicht
{
    /// <summary>
    /// MENU_BRIEFING (rooms-and-ui-2 §4.27): the written morning summary of all four airlines, shown
    /// instead of the boss's speech when the short-briefing option is on (not on day 0). "Hear the
    /// briefing" (or right click) starts the dialogue; "leave" starts the day, offered only when there
    /// is no exclamation mark (nothing anybody may miss).
    /// </summary>
    public sealed class BriefingMenu : PopupMenu
    {
        const string Lib = "gli/briefing.gli";
        readonly AufsichtRoom _room;

        public BriefingMenu(AufsichtRoom room) => _room = room;

        public override int Type => MenuIds.Briefing;
        public override Vector2Int Size => new Vector2Int(640, 440);
        public override Vector2Int? FixedPos => Vector2Int.zero;
        public override Texture Background => Assets.Texture(Lib, "MENU");

        World W => App.Session.World;
        bool CanLeave => AufsichtRoom.CanSkipBriefing(W);

        static bool In(Vector2Int l, int x1, int y1, int x2, int y2) => l.x >= x1 && l.x <= x2 && l.y >= y1 && l.y <= y2;

        public override void Update(InputState input, Vector2Int l)
        {
            bool hear = Hot(l, 136, 385, 312, 418);
            bool leave = CanLeave && Hot(l, 325, 385, 500, 418);
            if (!input.LeftDown) return;
            if (hear) Hear();
            else if (leave) { Close(); GameFlow.EndBriefing(App); }
        }

        public override void OnRightClick() => Hear();

        void Hear()
        {
            Close();
            _room.StartBriefing(true);
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var w = W;
            var log = BriefingLog.Of(w);
            // Every line in the big grey font (rooms-and-ui-2 §4.27): the free game shows the passengers
            // metric of the first mission; the rating is read when the card is drawn.
            var f = Kit.BigGrey;
            var rule = w.Rules.Get<IMissionRule>();
            int diff = w.Difficulty == Mission.FreeGame ? Mission.Passengers : w.Difficulty;
            for (int i = 0; i < Player.Count; i++)
            {
                var p = w.Players[i];
                int px = x + 61 + 274 * (i & 1), py = y + 83 + 153 * (i / 2);
                s.Draw(Assets.Texture(Lib, "PLAYER" + (p.IsOut ? i + 4 : i)), px, py + 3);
                Kit.BigGrey.Draw(s, px + 51, py + 5, p.Airline ?? "");
                if (!p.IsOut || log.OutReasonOf(i) != null)
                {
                    var st = p.Statistics;
                    f.Draw(s, px + 51, py + 33, Std("Misc", 1300 + diff));
                    if (!p.IsOut && rule != null) f.Draw(s, px + 240, py + 33, RatingText(w, rule, p, diff), TextAlign.Right);
                    long emp = st.Today(StatId.Employees);
                    long happy = emp > 0 ? st.Today(StatId.StaffSatisfaction) * 100 / emp : 0;
                    var rv = log.ReviewOf(i);
                    long cond = rv != null ? rv.AverageCondition : AverageCondition(w, p);
                    Row(s, f, px, py + 66, 6010, Mathf.Clamp(p.Image / 10, -100, 100));
                    Row(s, f, px, py + 84, 6011, happy);
                    Row(s, f, px, py + 102, 6012, cond);
                }
                if (log.HasNewsFor(i)) s.Draw(Assets.Texture(Lib, "AUSRUF"), px + 8, py + 105);
                // TODO(rooms-and-ui-2 §4.27): SABOT icon on sabotage victims once sabotage acts are recorded.
            }
            s.Draw(Assets.Texture(Lib, In(l, 136, 385, 312, 418) ? "BRIFO02" : "BRIFO01"), x + 136, y + 385);
            if (CanLeave) s.Draw(Assets.Texture(Lib, In(l, 325, 385, 500, 418) ? "BRIAB02" : "BRIAB01"), x + 325, y + 385);
        }

        void Row(Screen2D s, BitmapFont f, int px, int y, int text, long value)
        {
            f.Draw(s, px + 51, y, Std("Misc", text));
            f.Draw(s, px + 240, y, UiKit.Unit(UiKit.UnitPercent, value), TextAlign.Right);
        }

        /// <summary>Mission progress text: profit mission in the ": money" unit, image mission in percent,
        /// ATFS 7 as "a/b", otherwise a plain number (from 10 million on in millions).</summary>
        static string RatingText(World w, IMissionRule rule, Player p, int diff)
        {
            long r = rule.Rating(w, p);
            if (diff == Mission.Profit) return UiKit.Unit(UiKit.UnitColonMoney, r);
            if (diff == Mission.Image) return UiKit.Unit(UiKit.UnitPercent, r);
            if (diff == 47) return UiKit.Thousands(r) + "/" + UiKit.Thousands(rule.Rating(w, p, true));
            return r < 10000000 ? UiKit.Thousands(r) : UiKit.Thousands(r / 1000000) + UiKit.UnitFormat(UiKit.UnitMio);
        }

        static long AverageCondition(World w, Player p)
        {
            var planes = w.OperationsBridge.Planes(w, p);
            if (planes.Count == 0) return 0;
            long sum = 0;
            foreach (var pl in planes) sum += pl.Condition;
            return sum / planes.Count;
        }
    }

    /// <summary>
    /// MENU_GAMEOVER (rooms-and-ui-2 §4.26): the screen greys out behind a white flash (cam.raw) and shows
    /// GAMEOVER (lost) or OKAY (won) centred at (320,330). A click after 3 s (boss room) ends the game and
    /// returns to the main menu, after the outro movie when the final base or add-on mission was won
    /// (rooms-and-ui-4 §6.2). A right click does the same, except that it never plays the add-on outro (quirk, §2.8).
    /// </summary>
    public sealed class GameOverMenu : PopupMenu
    {
        const string Lib = "gli/gameover.gli";
        readonly bool _lost;
        float _t;

        public GameOverMenu(bool lost) => _lost = lost;

        public override int Type => MenuIds.GameOver;
        public override Vector2Int Size => new Vector2Int(640, 440);
        public override Vector2Int? FixedPos => Vector2Int.zero;

        public override void OnOpen()
        {
            _t = 0;
            App.Audio.Play("SOUND/cam.raw");
            if (App.Session != null) App.Session.HoldTime = true;
        }

        public override void Update(InputState input, Vector2Int l)
        {
            _t += Time.unscaledDeltaTime;
            if (input.LeftDown && _t > 3f) Quit(false);
        }

        public override void OnRightClick() { if (_t > 3f) Quit(true); }

        void Quit(bool rightClick)
        {
            Close();
            var app = App;
            int mission = app.Session?.World.Difficulty ?? Mission.FreeGame;
            string outro = _lost ? null : MissionProgress.OutroAfterWin(mission, rightClick);
            app.Session = null;
            app.Rooms.Reset();
            if (outro != null) app.Screens.Set(new VideoScreen(outro, () => new MainMenuScreen()));
            else app.Screens.Set(new MainMenuScreen());
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            // Grey veil over the whole (wide) frame; the first 270 ms flash white.
            s.FillRect(new RectInt(0, 0, s.Width, s.Height), new Color32(70, 70, 70, 190));
            if (_t < 0.27f) s.FillRect(new RectInt(0, 0, s.Width, s.Height), new Color32(255, 255, 255, (byte)(255 * (1 - _t / 0.27f))));
            var pic = Assets.Texture(Lib, _lost ? "GAMEOVER" : "OKAY");
            s.Draw(pic, x + 320 - pic.width / 2, y + 330 - pic.height / 2);
        }
    }
}
