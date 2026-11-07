using OpenAT.Sim.Contracts;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// Last Minute counter (rooms-and-ui-6 §6): six same-day orders as slips, the clerk who calls the
    /// decorative crane, the monitor; closes at 16:00 (and Saturdays).
    /// </summary>
    [Room(RoomIds.LastMinute)]
    [ScreenName("lastminute")]
    public sealed class LastMinuteRoom : OrderCounterRoom
    {
        protected override string Library => "room/lastmin.gli";
        protected override string Background => "LASTMIN";
        protected override OfferBoard Board => OfferBoard.LastMinute;
        static readonly Vector2Int[] Pos = { V(66, 79), V(126, 212), V(145, 116), V(33, 250), V(128, 289), V(15, 150) };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        protected override Vector2Int[] SlotPos => Pos;
        protected override Vector2Int TipPos => new Vector2Int(300, 125);
        protected override bool Open => CounterHours.LastMinuteOpen(W);
        protected override float FlySeconds => 0.25f;

        SmackerPerson _girl, _crane, _monitor;
        bool _craneLeft, _craneArrived;

        protected override void OnCreate()
        {
            _girl = new SmackerPerson(Assets, App.Audio);
            _girl.Clip(0, "LMwait.smk", 333, 225, Mood.Idle, "A6:0 A2:1 A3:2 A1:3 A1:4", 1, 1, 25, 60);
            _girl.Clip(1, "LMgrins.smk", 333, 225, Mood.Idle, "A1:0", canCancel: false);
            _girl.Clip(2, "LMtaste.smk", 333, 225, Mood.Idle, "A1:0", canCancel: false).OnFrame = f => { if (f == 4) _monitor.Decision = _monitor.ActiveClip == 0 ? 1 : 0; };
            _girl.Clip(3, "LMwaitK.smk", 333, 225, Mood.Idle, "A1:0", canCancel: false);
            var press = _girl.Clip(4, "LMknopf.smk", 333, 225, Mood.Idle, "A1:5", canCancel: false, sound: "kran1.raw");
            press.OnEnd = () => { _craneArrived = false; _crane.Decision = _craneLeft ? 3 : 2; };
            _girl.Clip(5, "LMknopfW.smk", 333, 225, Mood.Idle, "A1:5", canCancel: false).Decide = () => _craneArrived ? 6 : -1;
            _girl.Clip(6, "LMknopfZ.smk", 333, 225, Mood.Idle, "A1:0", canCancel: false);

            _crane = new SmackerPerson(Assets, App.Audio);
            _crane.Clip(0, "KranRS.smk", 300, 129, Mood.Idle, "A1:0", 1, 1, 20, 20);
            _crane.Clip(1, "KranLS.smk", 300, 129, Mood.Idle, "A1:1", 1, 1, 20, 20);
            var left = _crane.Clip(2, "KranL.smk", 300, 129, Mood.Idle, "A1:1", canCancel: false, sound: "kran2.raw");
            left.OnFrame = f => { if (f == 21) _craneArrived = true; };
            left.OnEnd = () => { _craneLeft = true; _craneArrived = true; };
            var right = _crane.Clip(3, "KranR.smk", 300, 129, Mood.Idle, "A1:0", canCancel: false, sound: "kran2.raw");
            right.OnFrame = f => { if (f == 21) _craneArrived = true; };
            right.OnEnd = () => { _craneLeft = false; _craneArrived = true; };

            _monitor = new SmackerPerson(Assets, App.Audio);
            _monitor.Clip(0, "Moni.smk", 448, 318, Mood.Idle, "A1:0");
            _monitor.Clip(1, "MoniA.smk", 448, 318, Mood.Idle, "A1:1");
        }

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            SetBackgroundFx(0, "Pap3.raw", 25000);
        }

        public override void Animate(float dt)
        {
            base.Animate(dt);
            _girl.Update(dt);
            _crane.Update(dt);
            _monitor.Update(dt);
        }

        protected override void Hotspots(InputState input)
        {
            Exit(560, 0, 639, 439);
            CardHotspots();
        }

        protected override void OnClick(int id)
        {
            if (id >= 0 && id < 6) Take(id);
        }

        protected override void DrawRoom(Screen2D s)
        {
            _crane.Draw(s, RX);
            _monitor.Draw(s, RX);
            _girl.Draw(s, RX);
            if (W == null) return;
            for (int i = 0; i < 6; i++)
            {
                if (Fly[i] >= 0) { var c = FlyCard[i]; if (c != null) DrawCard(s, i, c.From, c.To, new Vector2Int(16, 440)); continue; }
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
