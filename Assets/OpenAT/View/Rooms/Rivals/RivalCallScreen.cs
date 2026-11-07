using OpenAT.Sim;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Rivals
{
    /// <summary>
    /// A computer airline phones the local player (ai-robots.md §5.1, rooms-and-ui-3 §6.3): an overlay over
    /// the current room that runs the competitor dialog (<see cref="RivalDialog"/>) on the mobile, with the
    /// caller's portrait mirrored at the bottom right of the room canvas (x = 640 − width, same slide table
    /// as the player's portrait; mouth frames every 150 ms while it talks; rooms-and-ui-3 §4.3). With a
    /// computer partner a bubble left unanswered for 15 s makes it lose interest (<c>Play</c> 2499), and a
    /// second 15 s ends the call (§8 timeouts).
    /// </summary>
    public sealed class RivalCallScreen : GameScreen, IDialogStage
    {
        const int SlideStep = 9;
        static readonly int[] PhoneSlide = { 114, 122, 120, 111 };
        static readonly int[] MouthCycle = { 0, 2, 1, 3, 2, 3, 0, 1, 2 };
        const float BoredSeconds = 15f;

        readonly int _caller;
        readonly bool _callerLeads;
        DialogSession _dialog;
        MenuHost _menus;
        int _slide;
        float _acc, _talkUntil, _idle;
        int _ticker;
        bool _bored, _closing;

        /// <summary>The caller wants to talk about the cooperation (it called for that).</summary>
        public bool TopicPending = true;

        /// <param name="caller">Calling airline.</param>
        /// <param name="callerLeads">True when the computer called (par2 = 1: we answer the phone first).</param>
        /// <param name="inPerson">Face to face in the hall (medium "air", the non-phone portrait frames).</param>
        public RivalCallScreen(int caller, bool callerLeads = true, bool inPerson = false) { _caller = caller; _callerLeads = callerLeads; _inPerson = inPerson; }

        readonly bool _inPerson;
        static readonly int[] PersonSlide = { 115, 115, 120, 106 };

        public static bool IsOpen(Boot.GameApp app)
        {
            foreach (var s in app.Screens.All) if (s is RivalCallScreen) return true;
            return false;
        }

        public override bool Opaque => false;
        public int CanvasX => OX;
        public MenuHost Menus => _menus;
        public bool IsOwnOffice => false;
        public SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => null;
        public int Caller => _caller;

        public override void OnEnter()
        {
            _menus = new MenuHost(App, () => CanvasX);
            _dialog = new DialogSession(this);
            _dialog.Start(Talkers.Competitor, _inPerson ? DialogMedium.Air : DialogMedium.Phone, _caller, _callerLeads ? 1 : 0);
            // Both airlines are on the phone (the hall draws the phone pose, Sim/Hall/HallPhone.cs).
            var w = App.Session?.World;
            if (OnPhone && w?.Hall != null) w.Execute(new Sim.Hall.PhoneCallCommand(w.LocalPlayer, _caller));
        }

        /// <summary>The call runs on the phone (not face to face in the hall).</summary>
        public bool OnPhone => !_inPerson;

        public override void OnExit()
        {
            _dialog?.Stop();
            App.Audio.StopVoice();
            // A face-to-face talk frees both figures in the hall.
            var w = App.Session?.World;
            if (_inPerson && w?.Hall != null) w.Execute(new Sim.Hall.EndTalkCommand(w.LocalPlayer));
            if (!_inPerson && w?.Hall != null) w.Execute(new Sim.Hall.PhoneCallCommand(w.LocalPlayer, -1));
        }

        public void OnDialogStopped(int talker) => _closing = true;

        /// <summary>Called by the dialog when the caller says a line (mouth animation).</summary>
        public void PartnerTalks(int id) => _talkUntil = Time.unscaledTime + 2.5f;

        public override void Update(InputState input, float dt)
        {
            _acc += dt;
            while (_acc >= AdvisorLayer.TickSeconds)
            {
                _acc -= AdvisorLayer.TickSeconds;
                _ticker++;
                int max = (_inPerson ? PersonSlide : PhoneSlide)[Mathf.Clamp(_caller, 0, 3)];
                _slide = _closing ? Mathf.Max(0, _slide - SlideStep) : Mathf.Min(max, _slide + SlideStep);
            }
            App.Advisors.Update(dt, CanvasX, input, false);
            if (_closing)
            {
                if (_slide == 0) App.Screens.Remove(this);
                return;
            }
            _menus.Animate(dt);
            if (_menus.IsOpen) { _menus.Update(input, dt); return; }
            if (input.LeftDown || input.RightDown) _idle = 0;
            else _idle += dt;
            if (_dialog.IsOpen)
            {
                if (_idle > BoredSeconds && _dialog.BubbleShown && !App.Audio.VoicePlaying)
                {
                    _idle = 0;
                    if (_bored) { _dialog.Stop(); return; }
                    _bored = true;
                    // The partner is bored of the player: no more answers today (saved, §8 timeouts).
                    App.Session?.World.Execute(new Sim.Rivals.RivalBoredCommand(_caller));
                    _dialog.Say(2499);
                    PartnerTalks(2499);
                }
                _dialog.Update(input, dt);
            }
            else if (input.LeftDown || input.RightDown) input.Consume();
        }

        public override void Draw(Screen2D s)
        {
            int cx = CanvasX;
            int baseY = s.Height - StatusBar.Height;
            if (_slide > 0)
            {
                bool talking = Time.unscaledTime < _talkUntil || (App.Audio.VoicePlaying && _dialog != null && _dialog.BubbleShown);
                int frame = talking ? MouthCycle[(int)(Time.unscaledTime / 0.15f) % MouthCycle.Length] : 0;
                var t = App.Assets.Texture(UiKit.AdvisorLib, "BER9" + (Mathf.Clamp(_caller, 0, 3) + 1) + "_" + (frame + (_inPerson ? 0 : 4) + 1).ToString("00"));
                s.PushClip(new RectInt(0, 0, s.Width, baseY));
                s.Draw(t, cx + 640 - t.width, baseY - _slide, new RectInt(0, 0, t.width, t.height), new Color32(255, 255, 255, 255), flipX: true);
                s.PopClip();
            }
            _dialog?.Draw(s);
            _menus?.Draw(s, App.InputNow);
            App.Advisors.Draw(s, cx);
        }
    }
}
