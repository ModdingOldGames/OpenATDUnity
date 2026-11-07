using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Personal
{
    /// <summary>
    /// The personnel office of an airline (rooms-and-ui-6 §1): the hiring clerk ("a" talker, right desk)
    /// with her letter / paper-ball / magazine loop, the staff clerk ("b" talker, left desk) who bins the
    /// letters, the pills on the desk, and the anti-sabotage overlay.
    /// </summary>
    [Room(RoomIds.PersonalA), Room(RoomIds.PersonalB), Room(RoomIds.PersonalC), Room(RoomIds.PersonalD)]
    [ScreenName("personal")]
    public sealed class PersonalRoom : RoomScreen
    {
        const string Lib = "room/personal.gli";
        protected override string Library => Lib;
        protected override string Background => "PERSONAL";

        const int ClickA = 10, ClickB = 11, ClickPills = 20;

        /// <summary>Owner of this office (room 11/21/31/41 → airline 0..3).</summary>
        int Owner => RoomId >= RoomIds.PersonalA && RoomId <= RoomIds.PersonalD ? RoomId / 10 - 1 : App.Session?.World.LocalPlayer ?? 0;
        int TalkerA => Talkers.Personal1a + 2 * Owner;
        int TalkerB => Talkers.Personal1b + 2 * Owner;
        protected override int DefaultDialogPartner => TalkerA;

        SmackerPerson _woman, _man, _paper;
        // Shared state of the clerks' loop (the original's clip variables).
        int _paperOnTable = -1, _paperDropping = -1, _flight = -1;
        bool _stack = true, _magazine = true;
        float _flightT;

        protected override void OnCreate()
        {
            const int wx = 340, wy = 113;
            _woman = new SmackerPerson(Assets, App.Audio);
            _woman.Clip(0, "PFBrief.smk", wx, wy, Mood.Idle, "A9:1", canCancel: false, sound: "PFBrief.raw").OnFrame = f => { if (f == 1) _stack = !_stack; };
            _woman.Clip(1, "PFLeseB.smk", wx, wy, Mood.Idle, "A9:2", waitMin: 30, waitMax: 90).OnStart = () => _paperOnTable = 2;
            var throwClip = _woman.Clip(2, "PFWerf.smk", wx, wy, Mood.Idle, "A9:0 A2:3 E1:6", canCancel: false, sound: "PFWerf.raw");
            throwClip.OnFrame = f => { if (f == 5) { _flight = 0; } };
            throwClip.Decide = () => _paperOnTable != -1 ? (_woman.Desired == Mood.Talking || _woman.Desired == Mood.Listening ? 6 : 3) : -1;
            _woman.Clip(3, "PFWild.smk", wx, wy, Mood.Idle, "A9:4", canCancel: false, sound: "PFWild.raw").OnEnd = () => _magazine = false;
            _woman.Clip(4, "PFLeseW.smk", wx, wy, Mood.Idle, "A9:5", waitMin: 70, waitMax: 130).Decide = () => _paperOnTable != -1 ? 4 : -1;
            _woman.Clip(5, "PFWildZ.smk", wx, wy, Mood.Idle, "A9:0 E1:6", canCancel: false, sound: "PFWildZ.raw").OnStart = () => _magazine = true;
            _woman.Clip(6, "PFTurn.smk", wx, wy, Mood.Idle, "E1:8 E1:7 E1:9", canCancel: false);
            _woman.Clip(7, "PFRede.smk", wx, wy, Mood.Talking, "A9:7 E1:8 E1:9");
            _woman.Clip(8, "PFTurnW.smk", wx, wy, Mood.Listening, "A9:8 A1:10 E1:7 E1:9", waitMin: 20, waitMax: 20);
            _woman.Clip(9, "PFTurnZ.smk", wx, wy, Mood.Idle, "A9:0", canCancel: false);
            _woman.Clip(10, "PFTurnK.smk", wx, wy, Mood.Listening, "A9:8", canCancel: false);

            const int mx = 130, my = 80;
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "PMLese.smk", mx, my, Mood.Idle, "A1:0 A1:1 E1:3").Decide = () => _paperOnTable;
            _man.Clip(1, "PMBlat.smk", mx, my, Mood.Idle, "A9:0", canCancel: false).Decide = () => _paperOnTable;
            _man.Clip(2, "PMKorb.smk", mx, my, Mood.Idle, "A9:7", canCancel: false).OnFrame = f => { if (f == 4) _paperOnTable = -1; };
            _man.Clip(3, "PMTurn.smk", mx, my, Mood.Idle, "E1:4 E1:5 A1:5 E1:6", canCancel: false);
            _man.Clip(4, "PMReden.smk", mx, my, Mood.Talking, "A9:4 E1:5 E1:6");
            _man.Clip(5, "PMWait.smk", mx, my, Mood.Listening, "A9:5 A1:8 E1:4 E1:6", waitMin: 99, waitMax: 99);
            _man.Clip(6, "PMTurnZ.smk", mx, my, Mood.Idle, "A9:0", canCancel: false);
            _man.Clip(7, "PMKorbB.smk", mx, my, Mood.Idle, "A9:1 E1:3", canCancel: false).OnStart = () => _paperDropping = 1;
            _man.Clip(8, "PMWaitK.smk", mx, my, Mood.Listening, "A9:5", canCancel: false);

            _paper = new SmackerPerson(Assets, App.Audio);
            _paper.Clip(0, "", 133, 260, Mood.Listening, "A1:0", waitMin: 2, waitMax: 2).Decide = () => _paperDropping;
            _paper.Clip(1, "Brief.smk", 133, 260, Mood.Idle, "A9:0", canCancel: false).OnEnd = () => _paperDropping = -1;
            _paper.SetDesired(Mood.None);
        }

        protected override void OnRoomEnter()
        {
            // The personnel advisor's staffing remark (original UpdatePersonalberater(3), Advi 1100..1104).
            var p = App.Session?.Local;
            var m = p?.PersonnelAdvisorRemark(3);
            var raw = m != null ? Assets.StdText.Get("Advi", m.TextId) : null;
            if (raw != null) App.Advisors.Queue(AdvisorKind.Personnel, DialogText.Format(raw, m.Args), AdvisorUrgency.Comment);
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) =>
            talker == TalkerA ? _woman : talker == TalkerB ? _man : null;

        public override void Animate(float dt)
        {
            _woman.Update(dt);
            _man.Update(dt);
            _paper.Update(dt);
            if (_flight == 0)
            {
                // A new paper-ball flight: 95 % up and away, 5 % a lob towards the camera (§1.4).
                _flight = Random.Range(0, 100) < 95 ? 1 : 2;
                _flightT = 0;
            }
            if (_flight > 0) _flightT += dt;
        }

        bool HasPills => App.Session?.World.Players[Owner].HasItem(Sim.Items.ItemId.Pills) ?? false;

        protected override void Hotspots(InputState input)
        {
            Exit(20, 0, 183, 293);
            Hot(385, 145, 566, 352, ClickA);
            Hot(177, 94, 329, 248, ClickB);
            if (!HasPills) Hot(394, 378, 446, 425, ClickPills);
        }

        protected override void OnClick(int id)
        {
            switch (id)
            {
                case ClickA: StartDialog(TalkerA); break;
                case ClickB: StartDialog(TalkerB); break;
                case ClickPills: StartDialog(TalkerA, DialogMedium.Air, 20); break;
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            var p = App.Session?.World.Players[Owner];
            if (p != null && (p.SecurityFlags & 4) != 0) s.Draw(Assets.Texture(Lib, "NOSABO"), ox + 276, 0);
            if (_magazine) s.Draw(Assets.Texture(Lib, "ZEITSCHR"), ox + 483, 225);
            if (_paperOnTable != -1) s.Draw(Assets.Texture(Lib, "MITTELB"), ox + 342, 224);
            if (_stack) s.Draw(Assets.Texture(Lib, "BRIEF"), ox + 361, 292);
            _woman.Draw(s, ox);
            _man.Draw(s, ox);
            _paper.Draw(s, ox);
            DrawPaperBall(s, ox);
        }

        void DrawPaperBall(Screen2D s, int ox)
        {
            if (_flight <= 0) return;
            float ms = _flightT * 1000f;
            float x, y, size;
            if (_flight == 1)
            {
                size = 5;
                x = 491 + ms / 2; y = 143 - ms / 10;
                // Ends past the right edge of the room canvas (WS: the canvas clips it).
                if (x > 640) { _flight = -1; return; }
            }
            else if (ms <= 400)
            {
                size = 5 + ms * 95 / 400; x = 491 - 166 * ms / 400; y = 143 + 20 * ms / 400;
            }
            else
            {
                float t = ms - 400;
                size = 100 - t * 95 / 400; x = 325 - 166 * t / 400; y = 163 + 225 * t / 400;
                if (x < 159 || size <= 0) { _flight = -1; return; }
            }
            var ball = Assets.Texture(Lib, "KUGEL");
            float w = ball.width * size / 100f, h = ball.height * size / 100f;
            s.DrawScaled(ball, new Rect(ox + x - w / 2, y - h / 2, w, h), new Rect(0, 0, ball.width, ball.height), Color.white);
        }
    }
}
