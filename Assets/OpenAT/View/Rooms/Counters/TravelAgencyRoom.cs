using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// Travel agency (rooms-and-ui-6 §5): six abroad orders around the home airport as slips on the
    /// board, the typing clerk with her monitor, the snake in the terrarium and the spider it hunts (the spider can
    /// be caught while it runs, the start of the spider → dart → diskette chain); closes at 17:00.
    /// </summary>
    [Room(RoomIds.Reisebuero)]
    [ScreenName("travelagency")]
    public sealed class TravelAgencyRoom : OrderCounterRoom
    {
        protected override string Library => "room/reiseb.gli";
        protected override string Background => "REISEB";
        protected override OfferBoard Board => OfferBoard.TravelAgency;
        static readonly Vector2Int[] Pos = { V(499, 79), V(439, 212), V(420, 116), V(532, 250), V(453, 289), V(535, 150) };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        protected override Vector2Int[] SlotPos => Pos;
        protected override Vector2Int TipPos => new Vector2Int(160, 125);
        protected override bool Open => CounterHours.TravelAgencyOpen(W);

        const int ClickSpider = 20;
        SmackerPerson _girl, _monitor, _snake, _spider;
        AudioSource _waves;
        /// <summary>The snake has committed to eating the spider (the original's comm variable is no longer −1).</summary>
        bool _snakeStrikes;

        /// <summary>Snake and spider exist from Normal difficulty on and in the free game.</summary>
        bool SnakeMode => W != null && (W.Difficulty > 0 || W.Difficulty == Mission.FreeGame);
        bool HasSpider => App.Session?.Local.HasItem(ItemId.Spider) ?? true;

        protected override void OnCreate()
        {
            _girl = new SmackerPerson(Assets, App.Audio);
            _girl.Clip(0, "RBwait.smk", 200, 227, Mood.Idle, "A5:0 A2:1 A3:3", 1, 1, 40, 80);
            _girl.Clip(1, "RBdrehH.smk", 200, 227, Mood.Idle, "A1:2", canCancel: false);
            _girl.Clip(2, "RBdrehF.smk", 200, 227, Mood.Idle, "A1:0", canCancel: false);
            _girl.Clip(3, "RBtaste.smk", 200, 227, Mood.Idle, "A1:0", canCancel: false).OnFrame = f => { if (f == 4) _monitor.Decision = Random.Range(0, 4); };
            _monitor = new SmackerPerson(Assets, App.Audio);
            string[] screens = { "MoniR.smk", "MoniRE.smk", "MoniRC.smk", "MoniRD.smk" };
            for (int i = 0; i < 4; i++) _monitor.Clip(i, screens[i], 170, 318, Mood.Idle, "A1:" + i);
            if (!SnakeMode) return;
            _snake = new SmackerPerson(Assets, App.Audio);
            _snake.Clip(0, "SnakW.smk", 564, 213, Mood.Idle, "A8:0 A2:1", 1, 1, 20, 80).OnStart = () => _snakeStrikes = false;
            _snake.Clip(1, "SnakZung.smk", 564, 213, Mood.Idle, "A1:0", canCancel: false, sound: "snakzung.raw");
            _snake.Clip(2, "SnakEat.smk", 494, 194, Mood.Idle, "A1:0", sound: "snakeat.raw").OnStart = () => _snakeStrikes = false;
            // The spider only while the local player has none (rooms-and-ui-6 §5.2; B-5.1: not pumped otherwise).
            if (HasSpider) return;
            _spider = new SmackerPerson(Assets, App.Audio);
            _spider.Clip(0, "BLASEW.smk", 380, 320, Mood.Idle, "A9:0 A1:1 A2:2", 3, 3, 20, 60);
            // At frame 11 of its run the snake strikes and eats it.
            _spider.Clip(1, "Spider.smk", 380, 320, Mood.Idle, "A1:2").OnFrame = f =>
            {
                if (f == 11 && _snake != null) { _snakeStrikes = true; _snake.Decision = 2; }
            };
            _spider.Clip(2, "BLASEW.smk", 380, 320, Mood.Idle, "A9:0 A2:1 A1:2", 9, 9, 198, 198);
            // Once caught, no further spider runs this visit.
            _spider.Clips[0].Decide = _spider.Clips[2].Decide = () => HasSpider ? 2 : -1;
        }

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            SetBackgroundFx(0, "Pap3.raw", 25000);
            SetBackgroundFx(1, "moeve.raw", 50000);
            _waves = App.Audio.PlayLoop("SOUND/wellen.raw", 0.4f);
        }

        protected override void OnLeaveRoom()
        {
            base.OnLeaveRoom();
            if (_waves != null) App.Audio.StopLoop(_waves);
        }

        public override void Animate(float dt)
        {
            base.Animate(dt);
            _girl.Update(dt);
            _monitor.Update(dt);
            _snake?.Update(dt);
            _spider?.Update(dt);
        }

        protected override void Hotspots(InputState input)
        {
            Exit(0, 0, 80, 439);
            CardHotspots();
            // The running spider can be caught until the snake commits: first along its path, then at its end.
            if (_spider != null && !_snakeStrikes && _spider.ActiveClip == 1 && !HasSpider)
            {
                int f = _spider.Frame;
                if (f < 10) Hot(327, 322, 415, 389, ClickSpider);
                else if (f < 15) Hot(407, 365, 455, 410, ClickSpider);
            }
        }

        protected override void OnClick(int id)
        {
            if (id == ClickSpider)
            {
                var s = App.Session;
                if (!s.World.Execute(new PickUpItemCommand(s.Local.Index, ItemId.Spider)).Ok) return;
                // The spider vanishes (its idle clip) and the snake goes back to waiting.
                _spider.Decision = 2;
                _snake.Decision = 0;
                _snakeStrikes = false;
                return;
            }
            if (id >= 0 && id < 6) Take(id);
        }

        protected override void DrawRoom(Screen2D s)
        {
            _monitor.Draw(s, RX);
            _girl.Draw(s, RX);
            _spider?.Draw(s, RX);
            _snake?.Draw(s, RX);
            if (W == null) return;
            for (int i = 0; i < 6; i++)
            {
                if (Fly[i] >= 0) { var c = FlyCard[i]; if (c != null) DrawCard(s, i, c.From, c.To, new Vector2Int(590, 440)); continue; }
                if (!SlotFilled(i)) continue;
                var o = Offer(i);
                DrawCard(s, i, o.From, o.To, default);
            }
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (Hover < 0 || IsDialogOpen || Menus.IsOpen || !SlotFilled(Hover)) return;
            var o = Offer(Hover);
            DrawOrderTip(s, o.From, o.To, o.Persons, 0, o.Date, o.LastDate, o.Premium, o.Penalty, false);
        }
    }
}
