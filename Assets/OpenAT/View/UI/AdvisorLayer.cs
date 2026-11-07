using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>Advisor figures (rooms-and-ui-1 §10.1). Values 2..7 share the generic figure.</summary>
    public enum AdvisorKind { Assistant = 0, Personnel = 1, Kerosene = 2, Route = 3, Contract = 4, Money = 5, Info = 6, Plane = 7, Fitness = 8, Security = 9 }

    public enum AdvisorUrgency { Normal, Urgent, Comment }

    /// <summary>
    /// Advisor message layer (rooms-and-ui-1 §10): a queue of advisor texts shown by figures sliding up
    /// at the right edge of the room canvas with a speech bubble, plus the player's own portrait that
    /// slides in at the bottom-left during dialogs (§10.2) and animates its mouth while the player
    /// "speaks". Sim <see cref="AdvisorMessage"/> events for the local player are queued automatically.
    /// </summary>
    public sealed class AdvisorLayer
    {
        public const float TickSeconds = 0.05f;
        const int SlideStep = 9;
        static readonly int[] PlayerSlide = { 115, 115, 120, 106 };
        static readonly int[] PlayerPhoneSlide = { 114, 122, 120, 111 };
        static readonly int[] MouthCycle = { 0, 2, 1, 3, 2, 3, 0, 1, 2 };

        sealed class Message
        {
            public AdvisorKind Kind;
            public string Text;
            public AdvisorUrgency Urgency;
        }

        readonly GameApp _app;
        readonly List<Message> _queue = new List<Message>();
        Message _current, _last;
        int _slide;           // pixels risen above the bar
        int _talkTicks, _blinkIn = 60, _ticker;
        bool _leaving;
        float _acc;
        IDisposable _sub;
        World _subscribedWorld;

        // Player portrait (dialog mode).
        bool _playerShown;
        int _playerSlide;
        public bool PlayerPhone;
        /// <summary>Talking counter in ticks (2 x text length, §4.4); mouth animates while &gt; 0.</summary>
        public int PlayerTalk;
        /// <summary>Optional mouth-open probe (voice look-ahead); null = text-length timing only.</summary>
        public Func<bool> PlayerMouthOpen;
        /// <summary>Hide the portrait (own office / "don't display player").</summary>
        public bool HidePlayer;

        public AdvisorLayer(GameApp app) => _app = app;

        public bool HasLast => _last != null;
        public bool Showing => _current != null;

        /// <summary>
        /// Nothing left to say: no queued message and the current one (if any) has been said (the original's
        /// "IsSilent": the figure may still be sliding away or, in monologue mode, standing by).
        /// </summary>
        public bool Silent => _queue.Count == 0 && (_current == null || _leaving || (_talkTicks <= 0 && _slide >= SlideHeight(_current.Kind)));

        /// <summary>Monologue mode (tutorial): the advisor stays up between lines instead of sliding away.</summary>
        public bool Monolog;

        /// <summary>The original's "next message": the current line ends now (the next one follows).</summary>
        public void SkipCurrent() { if (_current != null) _talkTicks = 0; }

        /// <summary>Drops every queued message (tutorial cancel).</summary>
        public void ClearQueue() { _queue.Clear(); if (_current != null) { _talkTicks = 0; _leaving = true; } }
        public bool PlayerShown => _playerShown || _playerSlide > 0;

        void EnsureSubscribed()
        {
            var w = _app.Session?.World;
            if (w == _subscribedWorld) return;
            _sub?.Dispose();
            _subscribedWorld = w;
            _queue.Clear(); _current = null; _slide = 0;
            if (w != null) _sub = w.Events.Subscribe<AdvisorMessage>(OnSimMessage);
        }

        void OnSimMessage(AdvisorMessage m)
        {
            if (m.Player != _app.Session?.Local) return;
            var raw = _app.Assets.StdText.Get("Advi", m.TextId);
            if (raw == null) return;
            string text = Dialog.DialogText.Format(raw, m.Args ?? Array.Empty<object>());
            if (m.AppendTextId != 0)
            {
                var more = _app.Assets.StdText.Get("Advi", m.AppendTextId);
                if (more != null) text = text + " " + more;
            }
            Queue((AdvisorKind)Mathf.Clamp(m.Advisor, 0, 9), text, m.Comment ? AdvisorUrgency.Comment : AdvisorUrgency.Normal);
        }

        /// <summary>Queues a text (§10.1: urgent goes first, a comment replaces the advisor's comment).</summary>
        public void Queue(AdvisorKind kind, string text, AdvisorUrgency urgency = AdvisorUrgency.Normal)
        {
            if (urgency == AdvisorUrgency.Comment)
            {
                _queue.RemoveAll(q => q.Kind == kind && q.Urgency == AdvisorUrgency.Comment);
                if (_current != null && _current.Kind == kind && _current.Urgency == AdvisorUrgency.Comment)
                {
                    if (string.IsNullOrEmpty(text)) { _leaving = true; return; }
                    _current.Text = text; _talkTicks = 10 + 2 * text.Length; return;
                }
                if (string.IsNullOrEmpty(text)) return;
            }
            if (string.IsNullOrEmpty(text)) return;
            foreach (var q in _queue) if (q.Text == text) return;
            if (_current != null && _current.Text == text) return;
            if (_queue.Count >= 15) return;
            var msg = new Message { Kind = kind, Text = text, Urgency = urgency };
            if (urgency == AdvisorUrgency.Urgent) _queue.Insert(0, msg); else _queue.Add(msg);
        }

        public void RepeatLast()
        {
            if (_last != null) Queue(_last.Kind, _last.Text, _last.Urgency == AdvisorUrgency.Comment ? AdvisorUrgency.Comment : AdvisorUrgency.Urgent);
        }

        /// <summary>Drops pending comments (leaving a room, §2.5).</summary>
        public void NoComments() => _queue.RemoveAll(q => q.Urgency == AdvisorUrgency.Comment);

        /// <summary>Skips the current message (click on the figure or its bubble).</summary>
        public void Skip() { if (_current != null) _talkTicks = 0; }

        /// <summary>Dialog mode on/off: the player portrait slides in and advisors slide away (§4.12).</summary>
        public void SetDialogMode(bool on, bool phone = false)
        {
            _playerShown = on;
            PlayerPhone = phone;
            if (on && _current != null) _leaving = true;
            if (!on) PlayerTalk = 0;
        }

        public void Update(float dt, int canvasX, InputState input, bool allowClick)
        {
            EnsureSubscribed();
            // At most one second of ticks per frame; a broken or huge frame time (NaN, infinity, a long stall)
            // must not turn into an endless catch-up (a float that large no longer changes by 0.05).
            if (float.IsNaN(dt) || dt < 0) dt = 0;
            _acc = Mathf.Min(_acc + dt, 1f);
            while (_acc >= TickSeconds) { _acc -= TickSeconds; Tick(); }

            if (allowClick && _current != null && !_leaving && input.LeftDown && !input.Consumed)
            {
                var r = FigureRect(canvasX);
                if (r.Contains(input.Mouse) || BubbleRect(canvasX).Contains(input.Mouse))
                {
                    Skip();
                    input.Consume();
                }
            }
        }

        void Tick()
        {
            _ticker++;
            // Player portrait.
            int pmax = PlayerSlideHeight();
            if (_playerShown) _playerSlide = Mathf.Min(pmax, _playerSlide + SlideStep);
            else _playerSlide = Mathf.Max(0, _playerSlide - SlideStep);
            if (PlayerTalk > 0) PlayerTalk--;

            if (_current == null)
            {
                if (_queue.Count > 0 && !_playerShown && _playerSlide == 0)
                {
                    _current = _queue[0]; _queue.RemoveAt(0);
                    _last = _current;
                    _talkTicks = 10 + 2 * _current.Text.Length;
                    _leaving = false;
                }
                return;
            }
            int max = SlideHeight(_current.Kind);
            if (!_leaving)
            {
                if (_slide < max) { _slide = Mathf.Min(max, _slide + SlideStep); return; }
                if (--_blinkIn <= 0) _blinkIn = 30 + (_ticker * 37) % 150;
                if (_talkTicks > 0) { _talkTicks--; return; }
                // Next message of the same advisor follows directly.
                int idx = _queue.FindIndex(q => q.Kind == _current.Kind);
                if (idx >= 0 && !_playerShown)
                {
                    _current = _queue[idx]; _queue.RemoveAt(idx); _last = _current;
                    _talkTicks = 10 + 2 * _current.Text.Length;
                    return;
                }
                // Monologue: the figure waits for her next line (unless something else must show).
                if (Monolog && !_playerShown && _queue.Count == 0) return;
                _leaving = true;
            }
            _slide = Mathf.Max(0, _slide - SlideStep);
            if (_slide == 0) { _current = null; _leaving = false; }
        }

        int PlayerSlideHeight()
        {
            int p = Mathf.Clamp(_app.Session?.World.LocalPlayer ?? 0, 0, 3);
            return PlayerPhone ? PlayerPhoneSlide[p] : PlayerSlide[p];
        }

        static int SlideHeight(AdvisorKind k)
        {
            switch (k)
            {
                case AdvisorKind.Assistant: return 126;
                case AdvisorKind.Fitness:
                case AdvisorKind.Security: return 120;
                default: return 142;
            }
        }

        string FigureName(AdvisorKind k, int frame)
        {
            string set = k == AdvisorKind.Assistant ? "BERA1_" : k == AdvisorKind.Fitness ? "BERA8_" : k == AdvisorKind.Security ? "BERA9_" : "BERA2_";
            return set + (frame + 1).ToString("00");
        }

        Texture2D Figure(int frame) => _app.Assets.Texture(UiKit.AdvisorLib, FigureName(_current.Kind, frame));

        RectInt FigureRect(int canvasX)
        {
            if (_current == null) return new RectInt();
            var t = Figure(0);
            int baseY = _app.Screen.Height - StatusBar.Height;
            return new RectInt(canvasX + 640 - t.width, baseY - _slide, t.width, _slide);
        }

        static Vector2Int BubbleAnchor(AdvisorKind k) =>
            k == AdvisorKind.Assistant || k == AdvisorKind.Security || k == AdvisorKind.Fitness ? new Vector2Int(26, 56) : new Vector2Int(10, 84);

        // Bubble bitmaps and their text rects (§10.1).
        static readonly string[] Bubbles = { "BLASE0", "BLASE1", "BLASE2", "BLASE3" };
        static readonly RectInt[] BubbleText =
        {
            new RectInt(15, 6, 251, 11), new RectInt(19, 8, 247, 24), new RectInt(32, 8, 276, 44), new RectInt(30, 9, 468, 56),
        };

        int ChooseBubble(BitmapFont f, string text)
        {
            for (int i = 0; i < Bubbles.Length; i++)
            {
                var r = BubbleText[i];
                int h = f.DrawBlock(null, new RectInt(0, 0, r.width, 1000), text, Color.black, TextAlign.Left, true);
                if (h <= r.height + 2) return i;
            }
            return Bubbles.Length - 1;
        }

        RectInt BubbleRect(int canvasX)
        {
            if (_current == null) return new RectInt();
            var kit = _app.Ui.Kit;
            int b = ChooseBubble(kit.SmallBlack, _current.Text);
            var tex = kit.Std(Bubbles[b]);
            var fr = FigureRect(canvasX);
            var a = BubbleAnchor(_current.Kind);
            return new RectInt(fr.x + a.x - tex.width, fr.y + a.y - 14, tex.width, tex.height);
        }

        /// <summary>Draws portraits and advisors. Call after the status bar (settled ones go above menus).</summary>
        public void Draw(Screen2D s, int canvasX)
        {
            var kit = _app.Ui.Kit;
            if (kit == null) return;
            int baseY = s.Height - StatusBar.Height;
            s.PushClip(new RectInt(0, 0, s.Width, baseY));
            if (_playerSlide > 0 && !HidePlayer)
            {
                int p = Mathf.Clamp(_app.Session?.World.LocalPlayer ?? 0, 0, 3);
                int frame = 0;
                bool talking = PlayerTalk > 0 && (PlayerMouthOpen == null || PlayerMouthOpen());
                if (talking) frame = MouthCycle[(_ticker / 3) % MouthCycle.Length];
                if (PlayerPhone) frame += 4;
                var t = _app.Assets.Texture(UiKit.AdvisorLib, "BER9" + (p + 1) + "_" + (frame + 1).ToString("00"));
                s.Draw(t, canvasX, baseY - _playerSlide);
            }
            if (_current != null && _slide > 0)
            {
                int frame = 0;
                if (_slide >= SlideHeight(_current.Kind) && !_leaving)
                {
                    if (_talkTicks > 0) frame = 1 + (_ticker / 3) % 2;
                    else if (_blinkIn < 3) frame = 3;
                }
                var t = Figure(frame);
                s.Draw(t, canvasX + 640 - t.width, baseY - _slide);
                if (_slide >= SlideHeight(_current.Kind) && !_leaving)
                {
                    var br = BubbleRect(canvasX);
                    int b = ChooseBubble(kit.SmallBlack, _current.Text);
                    s.Draw(kit.WhiteTrans(UiKit.StdLib, Bubbles[b]), br.x, br.y);
                    var tr = BubbleText[b];
                    kit.SmallBlack.DrawBlock(s, new RectInt(br.x + tr.x, br.y + tr.y, tr.width, tr.height + 4), _current.Text, Color.white);
                }
            }
            s.PopClip();
        }
    }
}
