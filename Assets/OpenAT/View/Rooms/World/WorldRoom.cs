using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Items;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Planner;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.WorldPhone
{
    /// <summary>
    /// The long-distance call to a branch office (rooms-and-ui-8 §10): the earth turned to the called city
    /// with a satellite in front. Normally only seen as the phone pane of a branch call
    /// (<see cref="BranchListMenu"/> → <see cref="PhoneBookLists.PlaceBranchCall"/>); <see cref="City"/> and
    /// <see cref="Owner"/> are set before the call. As a normal room it shows the home city.
    /// </summary>
    [Room(RoomIds.World)]
    [ScreenName("world")]
    public sealed class WorldRoom : RoomScreen
    {
        const string Lib = "room/world.gli";
        protected override string Library => Lib;
        protected override string Background => "WORLD";
        static readonly Vector2Int GlobeOrigin = new Vector2Int(100, 20);

        /// <summary>City index of the call (−1 = the home city).</summary>
        public static int City = -1;
        /// <summary>Airline whose branch is called (the local player or a cooperating partner).</summary>
        public static int Owner = -1;

        GlobeView _globe;
        FrameAnimation _sender;
        float _lightTimer;
        bool _lightOn;

        OpenAT.Sim.World W => App.Session?.World;

        /// <summary>The city index to show (falls back to the home city).</summary>
        public static int CityIndex(OpenAT.Sim.World w)
        {
            if (w == null) return -1;
            if (City >= 0 && City < w.Data.Cities.Count) return City;
            return w.CityIndex(w.HomeCity);
        }

        protected override void OnCreate()
        {
            _globe = new GlobeView(Assets, "gli/EarthGlo.lbm");
            _sender = new FrameAnimation(Lib, "SENDER", 3, AnimMode.Repeat, 0, 5, 0, first: 1, digits: 1);
            var w = W;
            int ci = CityIndex(w);
            if (w == null || ci < 0) return;
            var c = w.Data.Cities.All[ci];
            _globe.Alpha = GlobeView.TargetFor(c.GlobeX);
            // Bubble of the branch clerk next to the city (canvas coordinates of the phone pane: the pane
            // shows room x 170.. at canvas x 378..). Low cities get a bubble above, others one to the left.
            var info = Talkers.Get(Talkers.World);
            if (info != null && _globe.Project(c.GlobeX, c.GlobeY, out var o))
            {
                int rx = GlobeOrigin.x + GlobeView.CentreOffset.x + (int)o.x;
                int ry = GlobeOrigin.y + GlobeView.CentreOffset.y + (int)o.y;
                int px = rx - (info.HandyOffset) + 378;
                if (ry > 200) { info.PhoneBubblePos = new Vector2Int(px + 20, ry - 30); info.PhoneStyle = BubbleStyle.Above; }
                else { info.PhoneBubblePos = new Vector2Int(px + 35, ry); info.PhoneStyle = BubbleStyle.Left; }
                info.BubblePos = info.PhoneBubblePos.Value;
                info.Style = info.PhoneStyle;
            }
        }

        protected override void OnLeaveRoom()
        {
            _globe?.Dispose();
            App.Advisors.NoComments();
        }

        public override void Animate(float dt)
        {
            _sender?.Update(dt, App.Audio);
            _lightTimer -= dt;
            if (_lightTimer <= 0) { _lightOn = !_lightOn; _lightTimer = _lightOn ? 0.1f : 0.5f; }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int x = RX;
            _globe?.Draw(s, new Vector2Int(x + GlobeOrigin.x + GlobeView.CentreOffset.x, GlobeOrigin.y + GlobeView.CentreOffset.y));
            if (Assets.Has(Lib, "SAT1")) s.Draw(Assets.Texture(Lib, "SAT1"), x + 30, 285);
            if (_lightOn && Assets.Has(Lib, "SATLICHT")) s.Draw(Assets.Texture(Lib, "SATLICHT"), x + 183, 389);
            _sender?.Draw(s, Assets, x + 203, 147);
        }
    }

    /// <summary>
    /// Branch-office phone (rooms-and-ui-3b §18, group "Welt"): greeting on the first call of the day, small
    /// talk, then the foreign orders of that city (MENU_AUSLANDSAUFTRAG); "goodbye" returns to the branch list.
    /// </summary>
    [DialogScript(Talkers.World)]
    public sealed class WorldDialog : DialogTree
    {
        static int CityOf(DialogSession d) => WorldRoom.CityIndex(d.World);
        static string CityName(DialogSession d) { int c = CityOf(d); return c >= 0 ? d.World.Data.Cities.All[c].Name : ""; }
        static bool Called(DialogSession d)
        {
            int c = CityOf(d);
            var cc = d.Player?.CalledCities;
            return cc != null && c >= 0 && c < cc.Length && cc[c];
        }
        static string OwnerAirline(DialogSession d)
        {
            int o = WorldRoom.Owner;
            return o >= 0 && o < d.World.Players.Length ? d.World.Players[o].Airline : d.Player.Airline;
        }

        static void OpenOrders(DialogSession d)
        {
            var w = d.World; var p = d.Player;
            int city = CityOf(d);
            bool first = !Called(d);
            if (city >= 0) w.Execute(new VisitOfferBoardCommand(p.Index, OfferBoard.Branch, city));
            var menu = new ForeignOrdersMenu(city) { FirstCall = first };
            var app = d.Stage.App;
            if (!first) menu.Closed = () => PhoneBookLists.ReopenBranchList(app);
            d.OpenMenu(menu, first ? 5000 : -1, new Vector2Int(480, 220), 0.1f);
        }

        public WorldDialog() : base("Welt")
        {
            Open(d =>
            {
                if (Called(d)) { OpenOrders(d); return; }
                int ci = CityOf(d);
                var city = ci >= 0 ? d.World.Data.Cities.All[ci] : null;
                bool wave = city != null && !string.IsNullOrEmpty(city.Wave) && d.Assets.DialogText.Get("Welt", 1000) != null;
                if (wave) d.Say(1000, OwnerAirline(d), CityName(d));
                else d.PlayerSays(2000, d.Player.Airline, CityName(d));
            });
            On(1000, d => d.PlayerSays(2000, d.Player.Airline, CityName(d)));
            On(2000, d => d.Choices(3000, 3002, ChoiceArgs.Same, CityName(d)));
            On(3000, d => d.Say(4000 + (d.World.Clock.Date + System.Math.Max(0, CityOf(d))) % 4));
            On(4000, 4003, (d, id) => d.Choices(3001, 3002, ChoiceArgs.Same, CityName(d)));
            On(5000, d => d.Choices(3001, 3002, ChoiceArgs.Same, CityName(d)));
            On(3001, d => OpenOrders(d));
            On(3002, d =>
            {
                var app = d.Stage.App;
                d.End();
                PhoneBookLists.ReopenBranchList(app);
            });
        }
    }

    /// <summary>
    /// MENU_AUSLANDSAUFTRAG (rooms-and-ui-2 §4.20): the six passenger and six freight offers of a branch
    /// city on listauft.gli, plus "back". Hover shows the order on the left page; clicking takes it at once.
    /// </summary>
    public sealed class ForeignOrdersMenu : PopupMenu
    {
        const string Lib = "gli/listauft.gli";
        readonly int _city;
        int _hover = -1;
        /// <summary>The city was not marked as called before this menu opened (back → dialog line 5000).</summary>
        public bool FirstCall;

        public ForeignOrdersMenu(int city) { _city = city; }
        public override int Type => MenuIds.Auslandsauftrag;
        public override Vector2Int Size => new Vector2Int(388, 240);
        public override Vector2Int? FixedPos => new Vector2Int(126, 100);
        public override Texture Background => Assets.Texture(Lib, "BLOCK");

        OpenAT.Sim.World W => App.Session.World;
        Player P => App.Session.Local;
        bool ValidCity => _city >= 0 && _city < W.ForeignContracts.Length;
        Contract Pass(int r) => ValidCity && !W.ForeignContracts[_city].IsEmpty(r) ? W.ForeignContracts[_city].Offers[r] : null;
        FreightContract Cargo(int r) { var f = ValidCity ? W.ForeignFreight[_city].Offers[r] : null; return f != null && f.Premium > 0 ? f : null; }

        public override void Update(InputState input, Vector2Int l)
        {
            _hover = -1;
            if (l.x >= 212 && l.x <= 384 && l.y >= 33)
            {
                int r = (l.y - 33) / 13;
                if ((r <= 5 && Pass(r) != null) || (r >= 7 && r <= 12 && Cargo(r - 7) != null) || r == 14) { _hover = r; App.Ui.Cursor = CursorLook.Hot; }
            }
            if (!input.LeftDown || _hover < 0) return;
            if (_hover == 14) { Back(); return; }
            if (_hover <= 5)
            {
                var cmd = new AcceptContractCommand(P.Index, OfferBoard.Branch, _hover, _city) { IgnoreOpeningHours = true };
                if (W.Execute(cmd).Ok) App.Audio.Play("SOUND/paper.raw");
            }
            else if (W.Execute(new AcceptFreightCommand(P.Index, OfferBoard.BranchFreight, _hover - 7, _city)).Ok) App.Audio.Play("SOUND/paper.raw");
        }

        void Back()
        {
            ReEntry = FirstCall ? 5000 : -1;
            Close();
        }

        public override void OnRightClick() => Back();

        string Route(int from, int to)
        {
            var a = W.City(from); var b = W.City(to);
            return (a?.Name ?? "?") + " - " + (b?.Name ?? "?");
        }

        static string Fit(BitmapFont f, string t, int w)
        {
            if (f.Measure(t) <= w) return t;
            while (t.Length > 1 && f.Measure(t + "...") > w) t = t.Substring(0, t.Length - 1);
            return t + "...";
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            if (ValidCity && _city < W.Data.Cities.Count) f.Draw(s, x + 214, y + 12, W.Data.Cities.All[_city].Name);
            if (_hover >= 0) HoverBand(s, new RectInt(x + 212, y + 33 + 13 * _hover, 172, 15));
            for (int r = 0; r < 6; r++)
            {
                var c = Pass(r);
                if (c != null) f.Draw(s, x + 214, y + 38 + 13 * r, Fit(f, Route(c.From, c.To), 170));
                var fr = Cargo(r);
                if (fr != null) f.Draw(s, x + 214, y + 38 + 13 * (r + 7), Fit(f, Route(fr.From, fr.To), 170));
            }
            f.Draw(s, x + 214, y + 38 + 13 * 14 - 4, Std("Misc", 1100));
            if (_hover >= 0 && _hover != 14) DrawTip(s, x, y);
        }

        /// <summary>Order details on the left page (headline at (6,6), body from (6,28)).</summary>
        void DrawTip(Screen2D s, int x, int y)
        {
            var f = Kit.SmallBlack;
            int lx = x + 10, rx = x + 200, ly = y + 6;
            bool freight = _hover >= 7;
            int from, to, date, last; long premium, penalty; string amount;
            if (freight)
            {
                var c = Cargo(_hover - 7); if (c == null) return;
                from = c.From; to = c.To; date = c.Date; last = c.LastDate; premium = c.Premium; penalty = c.Penalty;
                amount = Std("Auft", 1008) + "|" + UiKit.Unit(UiKit.UnitT, c.Tons);
            }
            else
            {
                var c = Pass(_hover); if (c == null) return;
                from = c.From; to = c.To; date = c.Date; last = c.LastDate; premium = c.Premium; penalty = c.Penalty;
                amount = Std("Auft", 1000) + "|" + c.Persons;
            }
            f.Draw(s, lx, ly, Std("Auft", freight ? 902 : 900)); ly += 22;
            f.Draw(s, lx, ly, Fit(f, W.City(from)?.Name ?? "", 190)); ly += 11;
            f.Draw(s, lx, ly, Fit(f, "- " + (W.City(to)?.Name ?? ""), 190)); ly += 16;
            void Row(string label, string value) { f.Draw(s, lx, ly, label); f.Draw(s, rx, ly, value, TextAlign.Right); ly += 12; }
            var parts = amount.Split('|');
            Row(parts[0], parts[1]);
            int today = W.Clock.Date;
            Row(Std("Auft", 1003), date == last ? "+" + (last - today) : "+" + System.Math.Max(0, date - today) + ".." + (last - today));
            Row(Std("Auft", 1001), UiKit.Money(premium));
            Row(Std("Auft", 1002), UiKit.Money(penalty));
            Row(Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(from, to) / 1000));
            // TODO(rooms-and-ui-2 §4.20): plane-size icons and the order advisor's comment.
        }
    }

    /// <summary>
    /// MENU_BRANCHLIST (rooms-and-ui-2 §4.9): the cities (not the home city) where the player or a
    /// cooperating partner has a branch, 15 per page; partner branches carry the partner's abbreviation.
    /// </summary>
    public sealed class BranchListMenu : PopupMenu
    {
        const string Lib = "gli/telefons.gli";
        const int PerPage = 15;
        int _page;
        readonly List<(int city, int owner)> _rows = new List<(int, int)>();

        public override int Type => MenuIds.BranchList;
        public override Vector2Int Size => new Vector2Int(204, 210);
        public override Vector2Int? FixedPos => new Vector2Int(218, 115);
        public override Texture Background => Assets.Texture(Lib, "BLOCK");

        public override void OnOpen()
        {
            _rows.Clear();
            var w = App.Session?.World; var p = App.Session?.Local;
            if (w == null) return;
            for (int c = 0; c < w.Data.Cities.Count; c++)
            {
                if (w.CityIdAt(c) == w.HomeCity) continue;
                if (p.BranchAt(c).Rank != 0) { _rows.Add((c, p.Index)); continue; }
                for (int k = 0; k < w.Players.Length; k++)
                    if (k != p.Index && !w.Players[k].IsOut && p.Cooperates[k] && w.Players[k].BranchAt(c).Rank != 0) { _rows.Add((c, k)); break; }
            }
        }

        string Text(int i)
        {
            var w = App.Session.World;
            var r = _rows[i];
            string n = w.Data.Cities.All[r.city].Name;
            return r.owner != App.Session.Local.Index ? n + " (" + w.Players[r.owner].Abbreviation + ")" : n;
        }

        public override void Update(InputState input, Vector2Int l)
        {
            bool prev = Hot(l, 12, 187, 36, 208, 0, CursorLook.Left);
            bool next = (_page + 1) * PerPage < _rows.Count && Hot(l, 190, 196, 203, 209, 0, CursorLook.Right);
            int hit = -1;
            if (!prev && !next && l.x >= 32 && l.x <= 204 && l.y >= 4)
            {
                int line = (l.y - 4) / 13, i = _page * PerPage + line;
                if (line < PerPage && i < _rows.Count) { hit = i; App.Ui.Cursor = CursorLook.Hot; }
            }
            if (!input.LeftDown) return;
            if (prev)
            {
                Paper();
                if (_page > 0) _page--;
                else Host.Open(new FilofaxMenu(2));
            }
            else if (next) { _page++; Paper(); }
            else if (hit >= 0)
            {
                var r = _rows[hit];
                Close();
                PhoneBookLists.PlaceBranchCall(App, r.owner, r.city);
            }
        }

        public override void OnRightClick() { Paper(); Host.Open(new FilofaxMenu(2)); }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            s.PushClip(new RectInt(x, y, 204, 210));
            for (int line = 0; line < PerPage; line++)
            {
                int i = _page * PerPage + line;
                if (i >= _rows.Count) break;
                if (l.x >= 32 && l.y >= 4 + 13 * line && l.y < 17 + 13 * line && l.y < 187) HoverBand(s, new RectInt(x + 32, y + 4 + 13 * line, 172, 15));
                f.Draw(s, x + 34, y + 7 + 13 * line, Text(i));
            }
            s.PopClip();
            s.Draw(Assets.Texture(Lib, "LEFT"), x + 12, y + 187);
            if ((_page + 1) * PerPage < _rows.Count) s.Draw(Assets.Texture(Lib, "RIGHT"), x + 190, y + 196);
        }
    }

    /// <summary>
    /// MENU_PLAYERLIST (rooms-and-ui-2 §4.9): the competitors to phone at their office (rows 1..3) or on the
    /// mobile (rows 6..8, only if they own one).
    /// </summary>
    public sealed class PlayerListMenu : PopupMenu
    {
        const string Lib = "gli/telefons.gli";
        public override int Type => MenuIds.PlayerList;
        public override Vector2Int Size => new Vector2Int(204, 210);
        public override Vector2Int? FixedPos => new Vector2Int(218, 115);
        public override Texture Background => Assets.Texture(Lib, "BLOCK");

        /// <summary>Player index of a row (−1 = heading/empty) and whether it is a mobile call.</summary>
        int PlayerOf(int row, out bool mobile)
        {
            mobile = row >= 6;
            int k = row >= 1 && row <= 3 ? row - 1 : row >= 6 && row <= 8 ? row - 6 : -1;
            if (k < 0) return -1;
            int me = App.Session.World.LocalPlayer, n = 0;
            for (int p = 0; p < App.Session.World.Players.Length; p++)
            {
                if (p == me) continue;
                if (n++ == k) return p;
            }
            return -1;
        }

        bool Callable(int row)
        {
            int p = PlayerOf(row, out bool mobile);
            if (p < 0) return false;
            var q = App.Session.World.Players[p];
            return !q.IsOut && (!mobile || q.HasItem(ItemId.Phone));
        }

        public override void Update(InputState input, Vector2Int l)
        {
            bool back = Hot(l, 12, 187, 36, 208, 0, CursorLook.Left);
            int row = l.x >= 32 && l.x <= 204 && l.y >= 4 ? (l.y - 4) / 13 : -1;
            bool hot = !back && row >= 0 && Callable(row);
            if (hot) App.Ui.Cursor = CursorLook.Hot;
            if (!input.LeftDown) return;
            if (back) { Paper(); Host.Open(new FilofaxMenu(2)); }
            else if (hot)
            {
                Close();
                CallCompetitor(App, PlayerOf(row, out bool mobile), mobile);
            }
        }

        public override void OnRightClick() { Paper(); Host.Open(new FilofaxMenu(2)); }

        /// <summary>
        /// Phoning a computer airline (rooms-and-ui-3 §6.3): busy when our phones are down; no answer on the office
        /// line when it is not in its office, or when it is stuck or bored of us; busy when it is talking; otherwise
        /// dial, the call costs 1, and the competitor dialog runs on the phone with us speaking first (par2 = 0).
        /// </summary>
        public static void CallCompetitor(GameApp app, int partner, bool mobile)
        {
            var w = app.Session?.World;
            if (w == null || partner < 0 || partner == w.LocalPlayer) return;
            var other = w.Players[partner];
            var hp = w.Hall?.Players[partner];
            if (w.Local.PhonesDown || app.Rooms.Phone != null) { app.Audio.Play("SOUND/busy.raw"); return; }
            bool inOffice = hp == null || hp.Room == RoomIds.OfficeOf(partner);
            if ((!mobile && !inOffice) || (hp != null && hp.IsStuck > 0)) { app.Audio.Play("SOUND/noanswer.raw"); return; }
            if (Rivals.RivalCallScreen.IsOpen(app)) { app.Audio.Play("SOUND/busy.raw"); return; }
            if (other.BoredOfPlayer) { app.Audio.Play("SOUND/noanswer.raw"); return; }
            app.Audio.Play(Random.value < 0.5f ? "SOUND/dial1.raw" : "SOUND/dial2.raw");
            w.Local.Book(-1, Sim.Finance.MoneyReason.PhoneBase);
            app.Screens.Push(new Rivals.RivalCallScreen(partner, callerLeads: false) { TopicPending = false });
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var w = App.Session.World;
            var f = Kit.SmallBlack;
            f.Draw(s, x + 34, y + 7, Std("Misc", 1130));
            f.Draw(s, x + 34, y + 7 + 13 * 5, Std("Misc", 1131));
            for (int row = 1; row <= 8; row++)
            {
                int p = PlayerOf(row, out _);
                if (p < 0) continue;
                bool ok = Callable(row);
                if (ok && l.x >= 32 && l.y >= 4 + 13 * row && l.y < 17 + 13 * row) HoverBand(s, new RectInt(x + 32, y + 4 + 13 * row, 172, 15));
                (ok ? f : Kit.SmallGrey).Draw(s, x + 40, y + 7 + 13 * row, w.Players[p].Airline ?? "");
            }
            s.Draw(Assets.Texture(Lib, "LEFT"), x + 12, y + 187);
        }
    }

    /// <summary>Hooks of the phone book's two list slots (competitors 254, branches 117) and branch calls.</summary>
    public static class PhoneBookLists
    {
        /// <summary>Opens the sub-list of a phone-book slot; false if <paramref name="room"/> is not a list slot.</summary>
        public static bool Open(GameApp app, MenuHost host, int room)
        {
            PopupMenu m = room == 254 ? new PlayerListMenu() : room == RoomIds.World ? new BranchListMenu() : null;
            if (m == null || host == null) return false;
            app.Audio.Play("SOUND/paper.raw");
            host.Open(m);
            return true;
        }

        /// <summary>Phones a branch: dial tone and call cost come from <see cref="RoomHost.PhoneCall"/>; the pane shows <see cref="WorldRoom"/>.</summary>
        public static bool PlaceBranchCall(GameApp app, int owner, int city)
        {
            WorldRoom.City = city;
            WorldRoom.Owner = owner;
            return app.Rooms.PhoneCall(Talkers.World, city);
        }

        /// <summary>Back to the branch list after a branch call (on the room or hall below the phone pane).</summary>
        public static void ReopenBranchList(GameApp app)
        {
            IStatusBarHost host = app.Rooms.Current;
            if (host == null)
            {
                var all = app.Screens.All;
                for (int i = all.Count - 1; i >= 0 && host == null; i--) if (all[i] is IStatusBarHost h) host = h;
            }
            if (host?.Menus == null || host.Menus.IsOpen) return;
            host.Menus.Open(new BranchListMenu());
        }
    }
}
