using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Missions;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Tafel;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Aufsicht
{
    /// <summary>
    /// The airport supervisor's office (rooms-and-ui-6 §3): the boss, the four managers at the 09:00
    /// briefing (figures, halos, desk objects), the desk monitor, the miniature notice board with the
    /// open auction slips, the glass window onto the hall, and the door to the notice board (room 108).
    ///
    /// Morning flow (§3.6): on entering with the clock frozen at 09:00 the boss dialogue starts with
    /// opening 1 (day 0, or the short-briefing option off) or the written summary MENU_BRIEFING opens.
    /// Leaving — the end of the dialogue, right click when allowed, the summary's "leave" button — runs
    /// <see cref="GameFlow.EndBriefing"/> (the working day starts).
    /// </summary>
    [Room(RoomIds.Aufsicht)]
    [ScreenName("aufsicht")]
    public sealed class AufsichtRoom : RoomScreen
    {
        const string Lib = "room/aufsicht.gli";
        protected override string Library => Lib;
        protected override string Background => "AUFSICHT";
        protected override int DefaultDialogPartner => Talkers.Boss;

        const int ClickBoard = 10, ClickBoss = 11;

        SmackerPerson _boss;
        readonly SmackerPerson[] _players = new SmackerPerson[4];
        readonly SmackerPerson[] _halos = new SmackerPerson[4];
        /// <summary>Airlines not at the table (out of the game, or no game running).</summary>
        bool Out(int c) { var w = App.Session?.World; return w == null || w.Players[c].IsOut; }
        // Boss look decision variables (Aufsicht.cpp ExitFromMiddle / Left / Right).
        int _exitMiddle = -1, _exitLeft = -1, _exitRight = -1;
        int _look; // −1 left, 0 middle, 1 right
        float _monitorT;
        int _monitorFrame;

        /// <summary>The 09:00 briefing is pending (clock frozen in the morning state).</summary>
        public bool IsMorning
        {
            get
            {
                var c = App.Session?.World.Clock;
                return c != null && c.State == DayState.Morning && c.Frozen;
            }
        }

        bool ShowManagers
        {
            get
            {
                var c = App.Session?.World.Clock;
                return c != null && c.Hour == 9 && c.Minute == 0;
            }
        }

        protected override void OnCreate()
        {
            const int bx = 344, by = 105;
            _boss = new SmackerPerson(Assets, App.Audio);
            _boss.Clip(0, "bb_wait.smk", bx, by, Mood.Idle, "A9:0 E1:1 E1:11", waitMin: 5, waitMax: 5).Decide = () => _exitMiddle;
            _boss.Clip(1, "bb_base.smk", bx, by, Mood.Talking, "A9:1 A1:3 E1:0 E1:11").Decide = () => _exitMiddle;
            _boss.Clip(2, "bb_fing.smk", bx, by, Mood.Talking, "A1:1", canCancel: false).OnStart = () => _exitMiddle = -1;
            _boss.Clip(3, "bb_hand.smk", bx, by, Mood.Talking, "A1:1", canCancel: false);
            _boss.Clip(4, "bb_left.smk", bx, by, Mood.Talking, "A1:5", canCancel: false).OnEnd = () => _exitMiddle = -1;
            _boss.Clip(5, "bb_leftR.smk", bx, by, Mood.Talking, "A1:5 E1:6").Decide = () => _exitLeft;
            _boss.Clip(6, "bb_leftZ.smk", bx, by, Mood.Talking, "A1:0", canCancel: false).OnEnd = () => _exitLeft = -1;
            _boss.Clip(7, "bb_righ.smk", bx, by, Mood.Talking, "A1:8", canCancel: false).OnEnd = () => _exitMiddle = -1;
            _boss.Clip(8, "bb_righR.smk", bx, by, Mood.Talking, "A1:8 E1:9").Decide = () => _exitRight;
            _boss.Clip(9, "bb_righZ.smk", bx, by, Mood.Talking, "A1:1", canCancel: false).OnEnd = () => _exitRight = -1;
            _boss.Clip(10, "bb_turnR.smk", bx, by, Mood.Talking, "A1:8", canCancel: false).OnEnd = () => _exitLeft = -1;
            _boss.Clip(11, "bb_wait.smk", bx, by, Mood.Listening, "A9:11 E1:1 E1:0", waitMin: 5, waitMax: 5).Decide = () => _exitMiddle;
            // Glare at everyone (after a sabotage report): left, right, left, right, back.
            _boss.Clip(12, "bb_left.smk", bx, by, Mood.Idle, "A1:13", canCancel: false).OnEnd = () => _exitMiddle = -1;
            _boss.Clip(13, "bb_turnR.smk", bx, by, Mood.Idle, "A1:14", canCancel: false);
            _boss.Clip(14, "bb_turnL.smk", bx, by, Mood.Idle, "A1:15", canCancel: false);
            _boss.Clip(15, "bb_turnR.smk", bx, by, Mood.Idle, "A1:16", canCancel: false);
            _boss.Clip(16, "bb_righZ.smk", bx, by, Mood.Idle, "A1:0", canCancel: false);

            CreateManagers();
        }

        void CreateManagers()
        {
            SmackerPerson P(int seed) => new SmackerPerson(Assets, App.Audio);
            var p0 = _players[0] = P(0);
            p0.Clip(0, "Pb_wait.smk", 70, 135, Mood.Idle, "A6:0 A1:1 A1:4 E1:1", waitMin: 5, waitMax: 5);
            p0.Clip(1, "Pb_guck.smk", 70, 135, Mood.Idle, "A1:2", canCancel: false);
            p0.Clip(2, "Pb_guckW.smk", 70, 135, Mood.Idle, "A7:3 A1:7 E1:8", waitMin: 30, waitMax: 60);
            p0.Clip(3, "Pb_guckZ.smk", 70, 135, Mood.Idle, "A1:0", canCancel: false);
            p0.Clip(4, "Pb_fing.smk", 70, 135, Mood.Idle, "A1:5", canCancel: false);
            p0.Clip(5, "Pb_fingW.smk", 70, 135, Mood.Idle, "A1:6", waitMin: 20, waitMax: 80, canCancel: false);
            p0.Clip(6, "Pb_fingZ.smk", 70, 135, Mood.Idle, "A1:0", canCancel: false);
            p0.Clip(7, "Pb_GuckK.smk", 70, 135, Mood.Idle, "A1:0", waitMin: 1, waitMax: 1, canCancel: false);
            p0.Clip(8, "Pb_guckW.smk", 70, 135, Mood.Holy, "A1:8 E1:3", waitMin: 9999, waitMax: 9999).OnStart = () => Halo(0);

            var p1 = _players[1] = P(1);
            p1.Clip(0, "Pv_wait.smk", 165, 110, Mood.Idle, "A8:0 A1:5 A2:1 A2:2 E1:6", waitMin: 15, waitMax: 45);
            p1.Clip(1, "Pv_hand.smk", 165, 110, Mood.Idle, "A1:0", canCancel: false);
            p1.Clip(2, "Pv_handB.smk", 165, 110, Mood.Idle, "A1:3", canCancel: false);
            p1.Clip(3, "Pv_handW.smk", 165, 110, Mood.Idle, "A1:4", waitMin: 40, waitMax: 90, canCancel: false);
            p1.Clip(4, "Pv_handZ.smk", 165, 110, Mood.Idle, "A1:0", canCancel: false);
            p1.Clip(5, "Pv_WaitK.smk", 165, 110, Mood.Idle, "A1:0", waitMin: 1, waitMax: 1, canCancel: false);
            p1.Clip(6, "Pv_wait.smk", 165, 110, Mood.Holy, "A1:6 E1:0", waitMin: 9999, waitMax: 9999).OnStart = () => Halo(1);

            var p2 = _players[2] = P(2);
            p2.Clip(0, "Pr_wait.smk", 422, 142, Mood.Idle, "A2:0 A1:1 A1:4 E1:5", waitMin: 5, waitMax: 5);
            p2.Clip(1, "Pr_auf.smk", 422, 142, Mood.Idle, "A1:2", canCancel: false);
            p2.Clip(2, "Pr_aufw.smk", 422, 142, Mood.Idle, "A1:3", waitMin: 40, waitMax: 120);
            p2.Clip(3, "Pr_aufz.smk", 422, 142, Mood.Idle, "A1:0", canCancel: false);
            p2.Clip(4, "Pr_uhr.smk", 422, 142, Mood.Idle, "A1:0", canCancel: false);
            p2.Clip(5, "Pr_wait.smk", 422, 142, Mood.Holy, "A1:5 E1:0", waitMin: 9999, waitMax: 9999).OnStart = () => Halo(2);

            var p3 = _players[3] = P(3);
            p3.Clip(0, "Pj_wait.smk", 446, 186, Mood.Idle, "A9:0 A1:4 A1:1 A1:2 A1:3 E1:5", 2, 2, 15, 20);
            p3.Clip(1, "Pj_arm.smk", 446, 186, Mood.Idle, "A1:0", canCancel: false);
            p3.Clip(2, "Pj_fing.smk", 446, 186, Mood.Idle, "A1:0", waitMin: 5, waitMax: 5);
            p3.Clip(3, "Pj_schu.smk", 446, 186, Mood.Idle, "A1:0", canCancel: false);
            p3.Clip(4, "Pj_k.smk", 446, 186, Mood.Idle, "A1:0", waitMin: 1, waitMax: 1, canCancel: false);
            p3.Clip(5, "Pj_wait.smk", 446, 186, Mood.Holy, "A1:5 E1:0", 2, 2, 9999, 9999).OnStart = () => Halo(3);

            var haloPos = new[] { new Vector2Int(94, 119), new Vector2Int(165, 93), new Vector2Int(528, 130), new Vector2Int(583, 164) };
            for (int c = 0; c < 4; c++)
            {
                var h = _halos[c] = P(10 + c);
                int k = c;
                h.Clip(0, "BombeW.smk", haloPos[c].x, haloPos[c].y, Mood.Idle, "A1:0", waitMin: 50, waitMax: 50);
                h.Clip(1, "P" + (c + 1) + "Halo.smk", haloPos[c].x, haloPos[c].y, Mood.Idle, "A1:0");
            }
        }

        void Halo(int c) => _halos[c].Decision = 1;

        /// <summary>Makes the boss turn to the managers on the left (players 0/1) or right (2/3), or back to the middle.</summary>
        public void Look(int side)
        {
            if (side == _look) return;
            if (side < 0) { _exitRight = _look > 0 ? 9 : -1; _exitMiddle = 4; _exitLeft = -1; }
            else if (side > 0) { if (_look < 0) _exitLeft = 10; else _exitMiddle = 7; _exitRight = -1; }
            else { _exitLeft = 6; _exitRight = 9; _exitMiddle = -1; }
            _look = side;
        }

        /// <summary>Raised finger (sabotage scolding).</summary>
        public void Finger() => _exitMiddle = 2;

        public void LookAtPlayer(int player) => Look(player < 2 ? -1 : 1);

        protected override void OnRoomEnter()
        {
            // In the tutorial the script starts the boss's talk itself once the assistant is done (step 1230).
            if (IsMorning && !Tutorial.TutorialView.Active) StartBriefing(false);
        }

        /// <summary>Opens the briefing: the boss dialogue, or first the written summary (short-briefing option).</summary>
        public void StartBriefing(bool forceDialog)
        {
            var w = App.Session?.World;
            if (w == null) return;
            if (forceDialog || w.Clock.Date == 0 || !OptionsScreen.Get("ShortBriefing"))
            {
                StartDialog(Talkers.Boss, DialogMedium.Air, 1);
                App.Advisors.HidePlayer = true;
            }
            else Menus.Open(new BriefingMenu(this));
        }

        /// <summary>
        /// Skipping the briefing is forbidden when there is news nobody may miss (§3.6 step 7): a win, a loss, a
        /// takeover, or a sabotage report that names a culprit (someone was fined, items-security-special §1.4);
        /// in the tutorial until step 1310.
        /// </summary>
        public bool CanSkip => CanSkipBriefing(App.Session?.World);

        public static bool CanSkipBriefing(World w)
        {
            if (w == null) return true;
            if (Tutorial.TutorialView.Active && w.Tutorial.Step <= Sim.Tutorial.TutorialStep.GoToOffice) return false;
            if (w.Items.Report != null && w.Items.Report.Date == w.Clock.Date && w.Items.Report.HasExposure) return false;
            return !BriefingLog.Of(w).HasNews && !w.GameEnded;
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Boss ? _boss : null;

        public override void OnDialogStopped(int talker)
        {
            if (IsMorning && !Menus.IsOpen && !IsPhonePane) GameFlow.EndBriefing(App);
        }

        protected override void Leave()
        {
            if (IsMorning)
            {
                if (CanSkip) GameFlow.EndBriefing(App);
                return;
            }
            base.Leave();
        }

        public override void Update(InputState input, float dt)
        {
            // In the morning a right click skips the whole briefing (when allowed), not just the line.
            if (IsMorning && input.RightDown && !input.Consumed && !Menus.IsOpen && input.Mouse.y < 440 && IsDialogOpen)
            {
                input.Consume();
                if (CanSkip) { Dialog.Stop(); }
                return;
            }
            base.Update(input, dt);
        }

        public override void Animate(float dt)
        {
            _boss.Update(dt);
            if (ShowManagers)
                for (int c = 0; c < 4; c++)
                    if (!Out(c)) { _players[c].Update(dt); _halos[c].Update(dt); }
            _monitorT += dt;
            if (_monitorT >= 0.15f) { _monitorT = 0; _monitorFrame = Random.Range(0, 3); }
        }

        protected override void Hotspots(InputState input)
        {
            if (IsMorning) return;
            Exit(64, 18, 172, 240);
            Hot(269, 41, 362, 137, ClickBoard);
            Hot(400, 100, 505, 241, ClickBoss);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickBoard) App.Rooms.Enter(RoomIds.Tafel);
            else if (id == ClickBoss) StartDialog(Talkers.Boss, DialogMedium.Air, 2);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            var wd = App.Session?.World;
            if (wd != null && !(wd.Items.Postcard && !wd.Local.SeligTrust && wd.Difficulty != Mission.Tutorial))
                s.Draw(Assets.Texture(Lib, "NOCARD"), ox + 260, 106);
            DrawMiniBoard(s, ox);
            // Window onto the hall: people near the office drawn large, then tinted glass and frame at (422,0).
            DrawHallWindow(s, ox);
            s.Draw(Assets.Texture(Lib, "TRANS"), ox + 422, 0, new Color32(255, 255, 255, 128));
            s.Draw(Assets.Texture(Lib, "OPAQUE"), ox + 422, 0);
            _boss.Draw(s, ox);
            bool morning = ShowManagers;
            if (morning)
            {
                if (!Out(1)) s.Draw(Assets.Texture(Lib, "OBJ_PV"), ox + 224, 201);
                if (!Out(0))
                {
                    s.Draw(Assets.Texture(Lib, "FRAUFUSS"), ox + 138, 373);
                    s.Draw(Assets.Texture(Lib, "OBJ_PB"), ox + 72, 245);
                }
            }
            s.Draw(Assets.Texture(Lib, "MONI0" + (_monitorFrame + 1)), ox + 329, 188);
            if (!morning) return;
            if (!Out(2)) s.Draw(Assets.Texture(Lib, "OBJ_PR"), ox + 403, 233);
            if (!Out(3)) s.Draw(Assets.Texture(Lib, "OBJ_PJ"), ox + 242, 249);
            if (!Out(1)) { _players[1].Draw(s, ox); _halos[1].Draw(s, ox); }
            for (int c = 0; c < 4; c++)
                if (c != 1 && !Out(c)) { _players[c].Draw(s, ox); _halos[c].Draw(s, ox); }
        }

        Airport.HallGraphics _hallGfx;
        Vector2Int? _windowAnchor;

        protected override void OnLeaveRoom()
        {
            _hallGfx?.Dispose();
            _hallGfx = null;
        }

        /// <summary>
        /// The hall seen through the window (§3.3): every person whose screen position, taken relative to the office's
        /// approach rune (RUNE_2SHOP − 5000 + 93, then (−22, −33)), lies within 150 px in y is turned into a side
        /// view — x = 640 − 4·dy − 2·dx, y = 220 + dx — and drawn pixel-doubled when 380 &lt; x &lt; 700 and
        /// |y − 220| &lt; 40, facing a quarter turn further, clipped to (422,0)-(640,174).
        /// </summary>
        void DrawHallWindow(Screen2D s, int ox)
        {
            var w = App.Session?.World;
            var h = w?.Hall;
            if (h == null || IsPhonePane) return;
            if (_windowAnchor == null)
            {
                var r = h.RandomRune(Sim.Hall.RuneId.ToShop, RoomIds.Aufsicht, null);
                if (r == null) return;
                _windowAnchor = new Vector2Int(r.Value.X - 22, r.Value.Y - 5000 + 93 - 33);
            }
            if (_hallGfx == null) _hallGfx = new Airport.HallGraphics(Assets, h.Data, w.Clock.Season);
            var a = _windowAnchor.Value;
            s.PushClip(new RectInt(ox + 422, 0, 218, 174));
            foreach (var p in h.Persons)
            {
                if (!p.Alive) continue;
                var k = h.Clan(p.ClanId);
                if (k == null) continue;
                int dx = p.ScreenPos.X - a.x, dy = p.ScreenPos.Y - a.y;
                if (System.Math.Abs(dy) >= 150) continue;
                int x = 640 - dy * 4 - dx * 2, y = 220 + dx;
                if (x <= 380 || x >= 700 || System.Math.Abs(y - 220) >= 40) continue;
                int dir = p.LookDir, phase = p.Phase;
                if (dir < 4) dir = (dir + 1) & 3;
                if (dir == 8) phase = phase < 4 ? (phase + 1) % 4 : (phase + 1) % 4 + 4;
                int slot, frame;
                if (dir < 8 || dir == 9)
                {
                    slot = dir;
                    int n = k.FrameCount(slot);
                    if (n == 0) continue;
                    frame = phase / System.Math.Max(1, k.Faktor);
                    if (frame >= n) frame = 0;
                }
                else
                {
                    slot = Sim.Hall.ClanDef.Stand;
                    int n = k.FrameCount(slot);
                    if (n == 0) continue;
                    frame = phase >= n ? 0 : phase;
                }
                var sp = _hallGfx.PersonFrame(k, slot, frame);
                if (sp?.Tex == null) continue;
                // Pixel-doubled, the foot point at (x, y): left = x − (w/2)·2, top = y − 2h.
                s.DrawScaled(sp.Tex, new Rect(ox + x - sp.W / 2 * 2, y - sp.H * 2, sp.W * 2, sp.H * 2), new Rect(0, 0, sp.W, sp.H), new Color32(255, 255, 255, 255));
            }
            s.PopClip();
        }

        /// <summary>Small copies of the open auction slips on the wall board (§3.4; city index 0 shown, B-3.1 fixed).</summary>
        void DrawMiniBoard(Screen2D s, int ox)
        {
            var w = App.Session?.World;
            if (w == null) return;
            var me = w.Local;
            for (int c = 0; c < 7; c++)
            {
                var city = w.Auctions.Cities[c];
                if (city.ObjectId >= 0 && city.ObjectId < me.RentCities.Length && me.RentCities[city.ObjectId].Rank == 0)
                    MiniNote(s, ox, TafelLayout.CitySlot(c), c);
                if (w.Auctions.Gates[c].ObjectId != -1) MiniNote(s, ox, TafelLayout.GateSlot(c), c);
            }
        }

        void MiniNote(Screen2D s, int ox, Vector2Int pos, int c)
        {
            int x = (pos.x - 91) * 74 / 441 + 274, y = (pos.y - 20) * 68 / 366 + 55;
            s.Draw(Assets.Texture(Lib, "ZETTELK" + (c % 3)), ox + x, y);
        }
    }
}
