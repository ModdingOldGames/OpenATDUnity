using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Items;
using OpenAT.Sim.Staff;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;
using HallIds = OpenAT.Sim.Hall.RoomIds;

namespace OpenAT.View.Rooms.Items
{
    /// <summary>
    /// What the local player notices of pranks and toilet visits (items-security-special.md §1.3,
    /// rooms-and-ui-2 §4.8 / §4.19, rooms-and-ui-8 §3):
    /// <list type="bullet">
    /// <item>own plane hit (letter Lett 501..505) or broken down (506/507) or a strike (508): the fax popup
    /// MENU_SABOTAGEFAX when the fax option is on and nothing else is open, otherwise the secretary's advisor
    /// line (Advi 2301 + mode for pranks);</item>
    /// <item>a prank the local player ordered went off: the saboteur calls (phone ring, phone pane with the
    /// saboteur's room, Sabo 2000), or talks directly when the player is in his room;</item>
    /// <item>the figure reached a toilet door: the input-blocking toilet pause MENU_WC_F/M;</item>
    /// <item>the walk target is a bombed office: MENU_REQUEST_DESTROYED (the sim already handed out the glowing coal).</item>
    /// </list>
    /// A small MonoBehaviour like <see cref="GameFlow"/>; it subscribes to the sim's events and never changes sim
    /// state except through commands.
    /// </summary>
    public sealed class SabotageNotifications : MonoBehaviour
    {
        World _world;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        /// <summary>Faxes waiting for a moment without dialog or menu (letter id, args).</summary>
        readonly Queue<(int id, object[] args)> _faxes = new Queue<(int, object[])>();
        int _toiletRoom;
        bool _destroyed;
        static bool _callPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.SabotageNotifications");
            DontDestroyOnLoad(go);
            go.AddComponent<SabotageNotifications>();
        }

        /// <summary>The saboteur room asks on entry whether a call is waiting (then he talks directly).</summary>
        internal static bool TakeCallFor(GameApp app)
        {
            if (!_callPending) return false;
            _callPending = false;
            return true;
        }

        static GameApp App => GameApp.Instance;

        void Bind(World w)
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _faxes.Clear();
            _callPending = false;
            _toiletRoom = 0;
            _destroyed = false;
            _world = w;
            if (w == null) return;
            _subs.Add(w.Events.Subscribe<PrankExecuted>(OnPrank));
            _subs.Add(w.Events.Subscribe<LetterSent>(OnLetter));
            _subs.Add(w.Events.Subscribe<HallRequest>(OnHallRequest));
        }

        void OnPrank(PrankExecuted e)
        {
            var w = _world;
            if (w == null || e.Act == null || e.Act.IsForeign) return;
            if (e.Saboteur != null && e.Saboteur.Index == w.LocalPlayer && e.Saboteur.Owner == Owner.Human) _callPending = true;
        }

        void OnLetter(LetterSent e)
        {
            var w = _world;
            if (w == null || e.Player == null || e.Player.Index != w.LocalPlayer || e.Group != "Lett") return;
            if (e.TextId < 501 || e.TextId > 508) return;
            _faxes.Enqueue((e.TextId, e.Args));
        }

        void OnHallRequest(HallRequest e)
        {
            var w = _world;
            if (w == null || e.Player != w.LocalPlayer) return;
            if (e.Kind == HallRequest.Toilet) _toiletRoom = e.Room;
            else if (e.Kind == HallRequest.Destroyed) _destroyed = true;
        }

        void Update()
        {
            var app = App;
            if (app == null) return;
            var w = app.Session?.World;
            if (w != _world) Bind(w);
            if (w == null || app.Assets == null) return;
            foreach (var sc in app.Screens.All) if (sc is OptionsScreen) return;

            var host = TopHost(app);
            if (_toiletRoom != 0 && host?.Menus != null && !host.Menus.IsOpen)
            {
                host.Menus.Open(new WcMenu(_toiletRoom), null);
                _toiletRoom = 0;
            }
            if (_destroyed && host?.Menus != null && !host.Menus.IsOpen)
            {
                host.Menus.Open(RequestMenu.Create(RequestMenu.Destroyed, null, null), new Vector2Int(320, 220), 0.17f);
                _destroyed = false;
            }

            bool busy = GameFlow.UiBusy(app) || w.Clock.State != DayState.Playing;
            // Strike start (sim-core.md §6.1): once, at the first quiet moment from 09:10; the command marks it
            // told and sends the letter Lett 508, which queues the fax below.
            if (host?.Menus != null && !busy && StrikeStartNotifiedCommand.Due(w, w.Local))
                w.Execute(new StrikeStartNotifiedCommand(w.LocalPlayer));
            if (_faxes.Count > 0 && host?.Menus != null && !busy)
            {
                var (id, args) = _faxes.Dequeue();
                int mode = id - 500;
                // The strike fax shows whatever the fax option says (the original opens it directly).
                bool faxOn = mode == 8 || OptionsScreen.Get("Fax");
                if (faxOn)
                {
                    host.Menus.Open(new SabotageFaxMenu(mode, args), new Vector2Int(320, 220), 0.17f);
                    app.Session.Local.GameSpeed = 0;
                    var raw = app.Assets.StdText.Get("Advi", 2308);
                    if (raw != null) app.Advisors.Queue(AdvisorKind.Assistant, raw, AdvisorUrgency.Comment);
                }
                else if (mode <= 5)
                {
                    var raw = app.Assets.StdText.Get("Advi", 2301 + mode);
                    if (raw != null) app.Advisors.Queue(AdvisorKind.Assistant, DialogText.Format(raw, args ?? Array.Empty<object>()));
                }
            }

            if (_callPending && !busy && app.Rooms.Phone == null)
            {
                if (app.Rooms.CurrentRoom == HallIds.Sabotage)
                {
                    var room = app.Rooms.Current;
                    if (room != null && !room.IsDialogOpen && !room.Menus.IsOpen)
                    {
                        _callPending = false;
                        app.Session.Local.GameSpeed = 0;
                        room.StartDialog(Talkers.Sabotage, DialogMedium.Air, 2000);
                    }
                }
                else
                {
                    var pane = RoomRegistry.Create(HallIds.Sabotage);
                    if (pane == null) { _callPending = false; return; }
                    _callPending = false;
                    app.Session.Local.GameSpeed = 0;
                    app.Audio.Play("SOUND/Phone.raw");
                    var screen = new PhoneCallScreen(pane, Talkers.Sabotage, 2000);
                    app.Rooms.Phone = screen;
                    app.Screens.Push(screen);
                }
            }
        }

        static IStatusBarHost TopHost(GameApp app)
        {
            if (app.Rooms.Current != null) return app.Rooms.Current;
            var all = app.Screens.All;
            for (int i = all.Count - 1; i >= 0; i--) if (all[i] is IStatusBarHost h) return h;
            return null;
        }
    }

    /// <summary>
    /// MENU_SABOTAGEFAX (rooms-and-ui-2 §4.8) on fax.gli FAX: the letter text (Lett 500 + kind, small black,
    /// (30,70)-(250,190)) and the picture of the act at (30,180); any click closes.
    /// </summary>
    public sealed class SabotageFaxMenu : PopupMenu
    {
        const string Lib = "gli/fax.gli";
        static readonly string[] Pictures = { null, "SALZ", "SKELETT", "REIFEN", "FEUER", "SUPERMAN", "KAPUTT", "KAPUTT", "STREIK" };
        readonly int _kind;
        readonly object[] _args;
        public SabotageFaxMenu(int kind, object[] args) { _kind = kind; _args = args ?? Array.Empty<object>(); }
        public override int Type => MenuIds.SabotageFax;
        public override Vector2Int Size => new Vector2Int(268, 388);
        public override Texture Background => Assets.Texture(Lib, "FAX");

        public override void OnOpen() => App.Audio.Play("SOUND/printer.raw");

        public override void Update(InputState input, Vector2Int l)
        {
            if (input.LeftDown) Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var raw = Assets.StdText.Get("Lett", 500 + _kind);
            if (raw != null) Kit.SmallBlack.DrawBlock(s, new RectInt(x + 30, y + 70, 220, 120), DialogText.Format(raw, _args), Color.white);
            string pic = _kind >= 0 && _kind < Pictures.Length ? Pictures[_kind] : null;
            if (pic != null && Assets.Has(Lib, pic)) s.Draw(Assets.Texture(Lib, pic), x + 30, y + 180);
        }
    }

    /// <summary>
    /// MENU_WC_F / MENU_WC_M (rooms-and-ui-2 §4.19): an invisible pause that blocks input (right click ignored)
    /// while the sim holds the figure at the toilet door (<c>HallPlayer.OnToilet</c>). The sim times it: after
    /// 20 ticks <see cref="HallToiletFlush"/> sounds the flush (in the ladies' room players 1..3 may pick up
    /// the perfume); after 80 ticks (40 for player 0 in the gents') the figure walks out through the hall and the
    /// pause closes.
    /// </summary>
    public sealed class WcMenu : PopupMenu
    {
        readonly int _room;
        IDisposable _sub;
        public WcMenu(int room) { _room = room; }
        public override int Type => _room == HallIds.WcF ? MenuIds.WcF : MenuIds.WcM;
        public override Vector2Int Size => new Vector2Int(1, 1);

        public override void OnOpen()
        {
            var w = App.Session?.World;
            if (w != null) _sub = w.Events.Subscribe<HallToiletFlush>(OnFlush);
        }

        void OnFlush(HallToiletFlush e)
        {
            var s = App.Session;
            if (s == null || e.Player != s.World.LocalPlayer || e.Done) return;
            int me = e.Player;
            bool ladies = e.Room == HallIds.WcF;
            string snd;
            if (!ladies) snd = me == 0 ? "ToiletFM.raw" : "Toilet.raw";
            else if (me == 0) snd = "Toilet.raw";
            else { int r = UnityEngine.Random.Range(0, 100); snd = r > 93 ? "TOILE3MF.RAW" : r > 60 ? "Toile2MF.raw" : "TOILETMF.RAW"; }
            App.Audio.Play("SOUND/" + snd);
            if (ladies && me != 0 && s.World.Items.Perfume) s.World.Execute(new PickUpItemCommand(me, ItemId.Perfume));
        }

        public override void Update(InputState input, Vector2Int l)
        {
            var s = App.Session;
            var hp = s?.World.Hall?.Players[s.World.LocalPlayer];
            if (hp == null || hp.OnToilet == 0) { _sub?.Dispose(); _sub = null; Close(); return; }
            input.Consume();
        }

        public override void OnClosed() { _sub?.Dispose(); _sub = null; }
        public override void OnRightClick() { }
        public override bool OnKey(InputState input) => true;
        public override void Draw(Screen2D s, int x, int y, Vector2Int l) { }
    }
}
