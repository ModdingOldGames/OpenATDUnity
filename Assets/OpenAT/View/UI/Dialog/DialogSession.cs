using System;
using System.Collections.Generic;
using System.Reflection;
using OpenAT.Sim;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.UI.Dialog
{
    /// <summary>What hosts a dialog: a room (face to face) or a phone-call overlay.</summary>
    public interface IDialogStage
    {
        GameApp App { get; }
        /// <summary>Screen x of the 640x440 room canvas.</summary>
        int CanvasX { get; }
        MenuHost Menus { get; }
        /// <summary>The partner's animated figure (null if none).</summary>
        SmackerPerson PartnerAnimation(int talker, DialogMedium medium);
        /// <summary>True in the player's own office (big player figure instead of the portrait).</summary>
        bool IsOwnOffice { get; }
        void OnDialogStopped(int talker);
    }

    /// <summary>A partner's dialog graph (rooms-and-ui-3b). <see cref="Click"/> receives the clicked id.</summary>
    public interface IDialogScript
    {
        /// <summary>Shows the opening node for <see cref="DialogSession.Par1"/> etc.</summary>
        void Open(DialogSession d);
        /// <summary>Handles "text <paramref name="id"/> was clicked". Show nothing = the dialog ends.</summary>
        void Click(DialogSession d, int id);
    }

    /// <summary>Marks an <see cref="IDialogScript"/> with a parameterless constructor for the talker ids it serves.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DialogScriptAttribute : Attribute
    {
        public readonly int[] Talkers;
        public DialogScriptAttribute(params int[] talkers) => Talkers = talkers;
    }

    /// <summary>Registry of dialog scripts per talker (found by attribute; extensions may <see cref="Register"/>).</summary>
    public static class DialogScripts
    {
        static Dictionary<int, IDialogScript> _map;

        static Dictionary<int, IDialogScript> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<int, IDialogScript>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || !typeof(IDialogScript).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        var a = t.GetCustomAttribute<DialogScriptAttribute>();
                        if (a == null) continue;
                        var inst = (IDialogScript)Activator.CreateInstance(t);
                        foreach (var id in a.Talkers) if (!_map.ContainsKey(id)) _map[id] = inst;
                    }
                }
                return _map;
            }
        }

        public static void Register(int talker, IDialogScript script) => Map[talker] = script;
        public static IDialogScript Get(int talker) => Map.TryGetValue(talker, out var s) ? s : null;
    }

    /// <summary>
    /// Data-driven hooks into dialogs: later domains attach effects to node ids without touching the
    /// tree ("when Bank/101 is clicked, open the loan menu"). A handler returns true when it handled
    /// the click (the tree is then skipped).
    /// </summary>
    public static class DialogActions
    {
        static readonly Dictionary<(int, int), List<Func<DialogSession, bool>>> _click = new Dictionary<(int, int), List<Func<DialogSession, bool>>>();
        static readonly Dictionary<int, List<Func<DialogSession, bool>>> _open = new Dictionary<int, List<Func<DialogSession, bool>>>();

        public static void On(int talker, int id, Func<DialogSession, bool> handler)
        {
            if (!_click.TryGetValue((talker, id), out var l)) _click[(talker, id)] = l = new List<Func<DialogSession, bool>>();
            l.Add(handler);
        }

        public static void On(int talker, int from, int to, Func<DialogSession, bool> handler)
        {
            for (int i = from; i <= to; i++) On(talker, i, handler);
        }

        public static void OnOpen(int talker, Func<DialogSession, bool> handler)
        {
            if (!_open.TryGetValue(talker, out var l)) _open[talker] = l = new List<Func<DialogSession, bool>>();
            l.Add(handler);
        }

        internal static bool TryClick(DialogSession d, int id)
        {
            if (!_click.TryGetValue((d.Partner, id), out var l)) return false;
            foreach (var h in l) if (h(d)) return true;
            return false;
        }

        internal static bool TryOpen(DialogSession d)
        {
            if (!_open.TryGetValue(d.Partner, out var l)) return false;
            foreach (var h in l) if (h(d)) return true;
            return false;
        }
    }

    /// <summary>
    /// A dialog graph written as data: node id → action. Unhandled ids end the dialog (part 3 §3.3).
    /// <code>
    /// new DialogTree("Bank").Open(d => d.Choices(101, 103)).On(109, d => d.Choices(101, 103))
    /// </code>
    /// </summary>
    public class DialogTree : IDialogScript
    {
        public readonly string Group;
        Action<DialogSession> _open;
        readonly Dictionary<int, Action<DialogSession, int>> _nodes = new Dictionary<int, Action<DialogSession, int>>();

        public DialogTree(string group) => Group = group;

        public DialogTree Open(Action<DialogSession> a) { _open = a; return this; }
        public DialogTree On(int id, Action<DialogSession> a) { _nodes[id] = (d, _) => a(d); return this; }
        public DialogTree On(int from, int to, Action<DialogSession, int> a) { for (int i = from; i <= to; i++) _nodes[i] = a; return this; }
        public DialogTree On(int[] ids, Action<DialogSession> a) { foreach (var i in ids) _nodes[i] = (d, _) => a(d); return this; }
        /// <summary>Shortcut: clicking <paramref name="id"/> shows the partner line <paramref name="next"/>.</summary>
        public DialogTree Say(int id, int next) => On(id, d => d.Say(next));
        /// <summary>Shortcut: clicking any of <paramref name="ids"/> offers choices from..to.</summary>
        public DialogTree Choices(int[] ids, int from, int to) => On(ids, d => d.Choices(from, to));

        void IDialogScript.Open(DialogSession d) => _open?.Invoke(d);

        void IDialogScript.Click(DialogSession d, int id)
        {
            if (_nodes.TryGetValue(id, out var a)) a(d, id);
        }
    }

    /// <summary>How choice-list arguments are distributed (part 1 §4.2 variant 3).</summary>
    public enum ChoiceArgs { Same = 0, OnePerOption = 1, TwoPerOption = 2 }

    /// <summary>
    /// The dialog runner of one stage (rooms-and-ui-1 §4, rooms-and-ui-3 §3–§5): partner and player
    /// bubbles, choice lists with highlight, the click → node flow, voice playback with lip-sync and
    /// auto-advance, the number card, menus with re-entry, and the partner's moods.
    /// </summary>
    public sealed class DialogSession
    {
        public const string PrefBubbles = "OpenAT.SpeechBubbles";
        const int LineDist = 5;
        static readonly Vector2Int[] PlayerAnchor = { new Vector2Int(73, 377), new Vector2Int(73, 389), new Vector2Int(82, 385), new Vector2Int(82, 385) };
        static readonly Vector2Int[] BigPlayerAnchor = { new Vector2Int(210, 235), new Vector2Int(220, 240), new Vector2Int(220, 240), new Vector2Int(220, 240) };

        enum Kind { None, Partner, Player, Choices }

        readonly IDialogStage _stage;
        GameApp App => _stage.App;

        public int Partner { get; private set; } = -1;
        public DialogMedium Medium { get; private set; }
        public int Par1 = -1, Par2 = -1, Par3 = -1;
        public TalkerInfo Info { get; private set; }
        /// <summary>Set by scripts when the next line's voice may be empty (click-through allowed, part 1 §4.11).</summary>
        public bool CanCancelEmpty;
        /// <summary>Per-visit "already greeted" flags (part 3 §3.1 TalkedToA/B).</summary>
        public bool TalkedToA, TalkedToB;

        public World World => App.Session?.World;
        public Player Player => App.Session?.Local;
        public GameAssets Assets => App.Assets;
        public IDialogStage Stage => _stage;

        Kind _kind;
        string _group;
        int _idFrom, _idTo;
        readonly List<string> _texts = new List<string>(), _raws = new List<string>();
        int _highlight = -1;
        float _talkUntil, _lastClickTime;
        int _lastClickId = -1;
        bool _bubbleReady, _produced, _echo;
        VoiceLine _voice;
        int _voiceDelayFrames = -1;
        bool _voiceStarted;
        public readonly NumberWindow Numbers = new NumberWindow();
        IDialogScript _script;

        public DialogSession(IDialogStage stage)
        {
            _stage = stage;
            stage.Menus.MenuClosed += OnMenuClosed;
        }

        public bool IsOpen => Partner >= 0 || _kind != Kind.None;
        public bool BubbleShown => _kind != Kind.None;
        SmackerPerson Anim => Partner >= 0 ? _stage.PartnerAnimation(Partner, Medium) : null;
        static bool BubblesOption => PlayerPrefs.GetInt(PrefBubbles, 1) != 0;
        bool VoiceOn => App.Audio != null && App.Audio.VoiceVolume > 0.001f;

        // ------------------------------------------------------------------ script API

        /// <summary>Starts a dialog (part 1 §4.12). par1 picks the opening (−1 default, 10000+item = item reaction).</summary>
        public void Start(int partner, DialogMedium medium = DialogMedium.Air, int par1 = -1, int par2 = -1, int par3 = -1)
        {
            if (IsOpen) Stop();
            Partner = partner; Medium = medium; Par1 = par1; Par2 = par2; Par3 = par3;
            Info = Talkers.Get(partner);
            _script = DialogScripts.Get(partner);
            App.Advisors.SetDialogMode(true, medium == DialogMedium.Phone);
            App.Advisors.HidePlayer = _stage.IsOwnOffice;
            Anim?.SetDesired(Mood.Listening);
            _produced = false;
            if (!DialogActions.TryOpen(this)) _script?.Open(this);
            if (!_produced) Stop();
        }

        /// <summary>The partner says text <paramref name="id"/> of its group.</summary>
        public void Say(int id, params object[] args) => SayIn(Info?.Group, id, args);

        public void SayIn(string group, int id, params object[] args)
        {
            var raw = DialogText.Get(Assets, group, id, args) ?? ("[" + group + ":" + id + "]");
            ShowPartner(group, id, raw);
        }

        /// <summary>A composed partner line with a virtual id (only used for branching).</summary>
        public void SayText(int virtualId, string text) => ShowPartner(_group ?? Info?.Group, virtualId, text);

        /// <summary>The player chooses among texts from..to (from == to: a single player line).</summary>
        public void Choices(int from, int to, ChoiceArgs mode = ChoiceArgs.Same, params object[] args) =>
            ChoicesIn(Info?.Group, from, to, mode, args);

        public void ChoicesIn(string group, int from, int to, ChoiceArgs mode = ChoiceArgs.Same, params object[] args)
        {
            BeginBubble();
            _group = group;
            _idFrom = from; _idTo = Math.Min(to, from + 9);
            for (int id = _idFrom; id <= _idTo; id++)
            {
                object[] a = args;
                int k = id - _idFrom;
                if (mode == ChoiceArgs.OnePerOption) a = k < args.Length ? new[] { args[k] } : Array.Empty<object>();
                else if (mode == ChoiceArgs.TwoPerOption) a = 2 * k + 1 < args.Length ? new[] { args[2 * k], args[2 * k + 1] } : Array.Empty<object>();
                var raw = DialogText.Get(Assets, group, id, a) ?? "";
                _raws.Add(raw);
                _texts.Add(DialogText.StripVoice(raw));
            }
            _kind = _idFrom == _idTo ? Kind.Player : Kind.Choices;
            _highlight = -1;
            Anim?.SetDesired(Mood.Listening);
            _talkUntil = 0;
            _bubbleReady = true;
            if (_kind == Kind.Player) StartPlayerVoice(_raws[0]);
        }

        public void PlayerSays(int id, params object[] args) => Choices(id, id, ChoiceArgs.Same, args);

        /// <summary>Shows the number card (texts 999xxxx have no voice).</summary>
        public void ShowNumbers(int id, params object[] args)
        {
            var t = DialogText.Get(Assets, Info?.Group, id, args);
            Numbers.Set(App.Ui.Kit, t != null ? DialogText.StripVoice(t) : null);
        }

        /// <summary>Opens a menu; when it closes the partner says <paramref name="reEntry"/> (−1 = dialog ends).</summary>
        public void OpenMenu(PopupMenu menu, int reEntry = -1, Vector2Int? zoomFrom = null, float minZoom = 1f)
        {
            _produced = true;
            menu.ReEntry = reEntry;
            CloseBubble();
            if (reEntry >= 0) Anim?.SetDesired(Mood.Listening);
            else { Anim?.SetDesired(Mood.Idle); App.Advisors.SetDialogMode(false); }
            _stage.Menus.Open(menu, zoomFrom, minZoom);
        }

        /// <summary>Keep the dialog alive without showing anything new (e.g. waiting for an animation).</summary>
        public void Stay() => _produced = true;

        /// <summary>Ends the dialog (part 1 §4.12 StopDialog).</summary>
        public void End() { _produced = true; Stop(); }

        public void Stop()
        {
            if (!IsOpen) return;
            int t = Partner;
            App.Audio.StopVoice();
            Anim?.SetDesired(Mood.Idle);
            App.Advisors.SetDialogMode(false);
            App.Advisors.HidePlayer = false;
            CloseBubble();
            Numbers.Set(App.Ui.Kit, null);
            Partner = -1;
            Info = null;
            _script = null;
            _stage.OnDialogStopped(t);
        }

        // ------------------------------------------------------------------ internals

        void BeginBubble()
        {
            _produced = true;
            App.Audio.StopVoice();
            _voice = null; _voiceStarted = false; _voiceDelayFrames = -1;
            _texts.Clear(); _raws.Clear();
            CanCancelEmpty = false;
            _echo = false;
        }

        void ShowPartner(string group, int id, string raw)
        {
            BeginBubble();
            _group = group;
            _kind = Kind.Partner;
            _idFrom = id; _idTo = 0;
            _raws.Add(raw);
            _texts.Add(DialogText.StripVoice(raw));
            var anim = Anim;
            anim?.SetDesired(Mood.Talking);
            _talkUntil = Time.unscaledTime + 0.1f * _texts[0].Length;
            _bubbleReady = anim == null;
            if (VoiceOn)
            {
                _voice = VoiceLine.Build(Assets, DialogText.VoiceParts(raw, Partner == Talkers.Competitor ? Par1 : App.Session?.World.LocalPlayer ?? 0, World));
                if (_voice != null && (_voice.Clip == null || _voice.AnyMissing)) _voice = null;
                if (_voice != null)
                {
                    if (anim == null) PlayVoice();
                    else _voiceDelayFrames = 3;
                }
            }
        }

        void StartPlayerVoice(string raw)
        {
            int me = App.Session?.World.LocalPlayer ?? 0;
            App.Advisors.PlayerTalk = 2 * DialogText.StripVoice(raw).Length;
            App.Advisors.PlayerMouthOpen = null;
            if (!VoiceOn) return;
            _voice = VoiceLine.Build(Assets, DialogText.VoiceParts(raw, me, World));
            if (_voice != null && (_voice.Clip == null || _voice.AnyMissing)) _voice = null;
            if (_voice == null) return;
            PlayVoice();
            var v = _voice;
            App.Advisors.PlayerMouthOpen = () => App.Audio.VoicePlaying && v.MouthOpen(App.Audio.VoiceSample, 200);
        }

        void PlayVoice()
        {
            App.Audio.PlayVoice(_voice.Clip);
            _voiceStarted = true;
        }

        void CloseBubble()
        {
            _kind = Kind.None;
            _texts.Clear(); _raws.Clear();
            _highlight = -1;
            App.Advisors.PlayerTalk = 0;
        }

        bool BubbleVisible =>
            BubblesOption || !VoiceOn || _kind == Kind.Choices || _voice == null;

        void OnMenuClosed(PopupMenu m)
        {
            if (Partner < 0) return;
            var hour = World?.Clock.Hour ?? 9;
            if (m.ReEntry >= 0 && hour < 18)
            {
                App.Advisors.SetDialogMode(true, Medium == DialogMedium.Phone);
                Say(m.ReEntry);
            }
            else if (_kind == Kind.None)
            {
                // Quiet end: the partner goes idle (part 3 §3.6).
                Anim?.SetDesired(Mood.Idle);
                App.Advisors.SetDialogMode(false);
                int t = Partner;
                Partner = -1; Info = null; _script = null;
                _stage.OnDialogStopped(t);
            }
        }

        void Dispatch(int id)
        {
            CloseBubble();
            _produced = false;
            if (!DialogActions.TryClick(this, id)) _script?.Click(this, id);
            if (!_produced && !_stage.Menus.IsOpen) Stop();
        }

        /// <summary>Per-frame update; call while <see cref="IsOpen"/> (menus are handled by the stage first).</summary>
        public void Update(InputState input, float dt)
        {
            if (!IsOpen) return;
            var anim = Anim;
            float now = Time.unscaledTime;
            if (_kind == Kind.Partner)
            {
                if (!_bubbleReady && (anim == null || anim.Current == Mood.Talking || now >= _talkUntil)) _bubbleReady = true;
                if (_voiceDelayFrames > 0 && anim != null && anim.Current == Mood.Talking && --_voiceDelayFrames == 0) PlayVoice();
                if (_voice != null && _voiceStarted && App.Audio.VoicePlaying)
                {
                    _talkUntil = now + 0.1f * _texts[0].Length;
                    anim?.SetDesired(_voice.MouthOpen(App.Audio.VoiceSample, 400) ? Mood.Talking : Mood.Listening);
                }
                else if (_voice == null && now >= _talkUntil) anim?.SetDesired(Mood.Listening);
                else if (_voice != null && _voiceStarted && !App.Audio.VoicePlaying) anim?.SetDesired(Mood.Listening);
            }

            if (_kind == Kind.Choices) UpdateHighlight(input);

            // Voiced single lines advance by themselves (part 1 §4.7).
            if ((_kind == Kind.Partner || _kind == Kind.Player) && _voice != null && _voiceStarted && !App.Audio.VoicePlaying)
            {
                ClickCurrent(true);
                return;
            }

            if (_stage.Menus.IsOpen) return;
            if (input.RightDown && !input.Consumed)
            {
                input.Consume();
                Stop();
                return;
            }
            if (input.LeftDown && !input.Consumed && _kind != Kind.None)
            {
                input.Consume();
                ClickCurrent(false);
            }
            if (_kind != Kind.None) App.Ui.Cursor = _kind == Kind.Choices && _highlight < 0 ? CursorLook.Normal : CursorLook.Hot;
        }

        void ClickCurrent(bool auto)
        {
            var anim = Anim;
            float now = Time.unscaledTime;
            if (!auto && _kind == Kind.Partner && anim != null && !_bubbleReady && !CanCancelEmpty) return;
            int id;
            if (_kind == Kind.Partner || _kind == Kind.Player) id = _idFrom;
            else if (_kind == Kind.Choices && _highlight >= 0) id = _idFrom + _highlight;
            else return;
            if (!auto && id == _lastClickId && now - _lastClickTime < 1f) return;
            _lastClickId = id; _lastClickTime = now;

            if (_kind == Kind.Choices && VoiceOn && !_echo)
            {
                // Echo: the chosen option is spoken as a single player line first (part 3 §3.3).
                string raw = _raws[_highlight];
                BeginBubble();
                _kind = Kind.Player; _idFrom = _idTo = id;
                _raws.Add(raw); _texts.Add(DialogText.StripVoice(raw));
                _bubbleReady = true;
                _echo = true;
                StartPlayerVoice(raw);
                if (_voice == null) { _echo = false; Dispatch(id); }
                return;
            }
            Dispatch(id);
        }

        // ------------------------------------------------------------------ layout & drawing

        Vector2Int PlayerAnchorPos()
        {
            int me = Mathf.Clamp(App.Session?.World.LocalPlayer ?? 0, 0, 3);
            return _stage.IsOwnOffice ? BigPlayerAnchor[me] : PlayerAnchor[me];
        }

        bool VersusPlayer => Partner == Talkers.Competitor;

        /// <summary>Player bubble layout in canvas coordinates: option tops and heights, bubble rect.</summary>
        void LayoutPlayer(BitmapFont font, out int[] tops, out int[] heights, out RectInt rect, out int textX, out int textW)
        {
            var a = PlayerAnchorPos();
            bool office = _stage.IsOwnOffice;
            int left = office ? a.x - 15 : a.x;
            int right = VersusPlayer ? 560 : 630;
            textX = left + 48;
            textW = (VersusPlayer ? 550 : 615) - textX;
            int n = _texts.Count;
            heights = new int[n]; tops = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                heights[i] = font.DrawBlock(null, new RectInt(0, 0, textW, 400), _texts[i], Color.white, TextAlign.Left, true);
                total += heights[i] + LineDist;
            }
            total = total - LineDist + 1;
            int top = 430 - total / 2 > a.y ? a.y - total / 2 : 430 - total;
            int y = top;
            for (int i = 0; i < n; i++) { tops[i] = y; y += heights[i] + LineDist; }
            rect = new RectInt(left, top - 10, right - left, total + 20);
        }

        int _choiceHash;
        object _choiceKey;

        void UpdateHighlight(InputState input)
        {
            var font = App.Ui.Kit.Dialog;
            LayoutPlayer(font, out var tops, out var heights, out var rect, out _, out _);
            int my = input.Mouse.y;
            int hit = -1;
            for (int i = 0; i < tops.Length; i++) App.Ui.Targets.Add(new RectInt(rect.x, tops[i], rect.width, heights[i] + LineDist));   // gamepad (OpenAT)
            // New answers: a new key, so the pad cursor goes to the first one (OpenAT).
            int hash = string.Join("|", _texts).GetHashCode();
            if (hash != _choiceHash || _choiceKey == null) { _choiceHash = hash; _choiceKey = new object(); }
            App.Ui.Targets.ModalKey = _choiceKey;
            if (input.Mouse.x >= 0)
                for (int i = 0; i < tops.Length; i++)
                    if (my >= tops[i] && my < tops[i] + heights[i] + LineDist) { hit = i; break; }
            if (hit != _highlight)
            {
                if (hit >= 0) App.Audio.Play("SOUND/change.raw");
                _highlight = hit;
            }
        }

        public void Draw(Screen2D s)
        {
            var kit = App.Ui.Kit;
            int cx = _stage.CanvasX;
            if (_kind == Kind.None) { Numbers.Draw(s, kit, cx); return; }
            if (_kind == Kind.Partner && !_bubbleReady) return;
            Numbers.Draw(s, kit, cx);
            if (!BubbleVisible) return;
            if (_kind == Kind.Partner) DrawPartner(s, kit, cx);
            else DrawPlayer(s, kit, cx);
        }

        void DrawPartner(Screen2D s, UiKit kit, int cx)
        {
            var font = kit.DialogPartner;
            bool phone = Medium == DialogMedium.Phone;
            var pos = phone && Info?.PhoneBubblePos != null ? Info.PhoneBubblePos.Value : Info?.BubblePos ?? new Vector2Int(378, 200);
            var style = phone && Info?.PhoneBubblePos != null ? Info.PhoneStyle : Info?.Style ?? BubbleStyle.Above;
            if (Partner == Talkers.Passenger && Par2 >= 0) pos = new Vector2Int(Par2, Par3);
            string text = _texts[0];
            int bx = pos.x, by = pos.y;
            int L = VersusPlayer ? 80 : 5;
            int tx, tw;
            switch (style)
            {
                case BubbleStyle.Left: tx = L + 45; tw = bx - 40 - tx; break;
                case BubbleStyle.Right: tx = bx + 50; tw = 600 - tx; break;
                default: tx = 80; tw = 480; break;
            }
            tw = Mathf.Max(60, tw);
            int h = font.DrawBlock(null, new RectInt(0, 0, tw, 440), text, Color.white, TextAlign.Left, true);
            int x1, y1, x2, y2, ty; Vector2Int entry;
            switch (style)
            {
                case BubbleStyle.Below: x1 = 50; x2 = 600; y1 = by - 8; y2 = by + h + 8; ty = by; entry = new Vector2Int(bx, y1); break;
                case BubbleStyle.Left: x1 = L; x2 = bx - 10; y1 = by - h / 2 - 8; y2 = by + h / 2 + 8; ty = by - h / 2; entry = new Vector2Int(x2, by); break;
                case BubbleStyle.Right: x1 = bx + 10; x2 = 630; y1 = by - h / 2 - 8; y2 = by + h / 2 + 8; ty = by - h / 2; entry = new Vector2Int(x1, by); break;
                default: x1 = 50; x2 = 600; y1 = by - h - 8; y2 = by + 8; ty = by - h; entry = new Vector2Int(bx, y2); break;
            }
            SpeechBubble.Draw(s, kit, cx + x1, y1, cx + x2, y2, new Vector2Int(cx + entry.x, entry.y));
            font.DrawBlock(s, new RectInt(cx + tx, ty, tw, h + 2), text, Color.white);
        }

        void DrawPlayer(Screen2D s, UiKit kit, int cx)
        {
            var font = kit.Dialog;
            LayoutPlayer(font, out var tops, out var heights, out var rect, out int textX, out int textW);
            var a = PlayerAnchorPos();
            SpeechBubble.Draw(s, kit, cx + rect.x, rect.y, cx + rect.xMax, rect.yMax,
                _stage.IsOwnOffice ? (Vector2Int?)null : new Vector2Int(cx + a.x, a.y));
            bool list = _kind == Kind.Choices;
            for (int i = 0; i < _texts.Count; i++)
            {
                var f = list && i == _highlight ? kit.DialogLight : font;
                if (list) f.Draw(s, cx + rect.x + 30, tops[i], "#");
                f.DrawBlock(s, new RectInt(cx + textX, tops[i], textW, heights[i] + 2), _texts[i], Color.white);
            }
        }
    }
}
