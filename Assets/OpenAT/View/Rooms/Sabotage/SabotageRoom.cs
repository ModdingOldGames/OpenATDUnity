using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Items;
using OpenAT.View.Rooms.Makler;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Sabotage
{
    /// <summary>
    /// The saboteur in the basement (rooms-and-ui-8 §3): steam, smoke, the burning fuse, the camel's eye,
    /// the dartboard (dart chain), the pliers on the desk and the saboteur who types with his back turned
    /// until spoken to. Orders go through <see cref="SaboDialog"/> → <see cref="OrderPrankCommand"/>.
    /// Also the phone pane of the saboteur's call after a prank went off (dialog opening 2000).
    /// </summary>
    [Room(RoomIds.Sabotage)]
    [ScreenName("sabotage")]
    public sealed class SabotageRoom : RoomScreen
    {
        internal const string Lib = "room/sabotage.gli";
        protected override string Library => Lib;
        protected override string Background => "SABOTAGE";
        protected override int DefaultDialogPartner => Talkers.Sabotage;
        const int ClickMan = 10, ClickDart = 800;

        SmackerPerson _man;
        FrameAnimation _steam, _fuse, _eye, _smoke;
        bool _eyeRequested;
        UnityEngine.AudioSource _loop;

        Player P => App.Session?.Local;
        World W => App.Session?.World;

        protected override void OnCreate()
        {
            _steam = new FrameAnimation(Lib, "DAMPF", 8, AnimMode.Repeat, 300, 3, 300, "zisch.raw", showFirst: false, first: 0);
            _smoke = new FrameAnimation(Lib, "RAUCH", 11, AnimMode.Repeat, 0, 5, 0, showFirst: true, first: 0);
            _eye = new FrameAnimation(Lib, "AUGE", 7, AnimMode.Never, 0, 7, 300, showFirst: false, first: 0);
            // The burning fuse only shows up now and then (1 in 10 visits).
            if (Random.Range(0, 10) == 0) _fuse = new FrameAnimation(Lib, "FEUER", 3, AnimMode.Repeat, 0, 2, 0, "lunte.raw", first: 0);

            _man = new SmackerPerson(Assets, App.Audio);
            int bx = 413, by = 255, fx = 155, fy = 102;
            _man.Clip(0, "S_clic.smk", bx, by, Mood.Idle, "A9:0 A4:10 E1:1");
            _man.Clip(1, "s_turn.smk", 275, 101, Mood.Idle, "E1:2 E1:3", canCancel: false, sound: "S_Turn.raw");
            _man.Clip(2, "s_rede.smk", fx, fy, Mood.Talking, "A9:2 E1:3 E1:6", 8, 8);
            _man.Clip(3, "s_wait.smk", fx, fy, Mood.Listening, "A9:3 A1:4 A1:5 E1:2 E1:6", 2, 2, 20, 20);
            _man.Clip(4, "s_left.smk", fx, fy, Mood.Listening, "A9:3 E1:2 E1:6", canCancel: false);
            _man.Clip(5, "s_rigt.smk", fx, fy, Mood.Listening, "A9:3 E1:2 E1:6", canCancel: false);
            _man.Clip(6, "s_raus.smk", fx, fy, Mood.Idle, "A9:7 E1:2 E1:3", canCancel: false).OnStart = () => _eyeRequested = true;
            _man.Clip(7, "s_wait.smk", fx, fy, Mood.Idle, "A9:7 A1:11 A1:6 A1:8 A1:9 E1:2 E1:3", 1, 1, 20, 20);
            _man.Clip(8, "s_left.smk", fx, fy, Mood.Idle, "A9:7 E1:2 E1:3", canCancel: false);
            _man.Clip(9, "s_rigt.smk", fx, fy, Mood.Idle, "A9:7 E1:2 E1:3", canCancel: false);
            _man.Clip(10, "S_clicB.smk", bx, by, Mood.Idle, "A9:0 E1:1");
            _man.Clip(11, "S_WaitK.smk", fx, fy, Mood.Idle, "A9:7", 1, 1, 1, 1);
            // A phone pane shows him facing the caller.
            if (IsPhonePane) _man.Decision = 7;
        }

        protected override void OnRoomEnter()
        {
            App.Audio.Play("SOUND/morse.raw");
            _loop = App.Audio.PlayLoop("SOUND/SaboBack.raw", 0.4f);
            // The saboteur's call when the player is already here (rooms-and-ui-8 §3 "also opened as phone window").
            if (SabotageNotifications.TakeCallFor(App)) StartDialog(Talkers.Sabotage, DialogMedium.Air, 2000);
        }

        protected override void OnLeaveRoom()
        {
            if (_loop != null) App.Audio.StopLoop(_loop);
            _loop = null;
            _man.Dispose();
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Sabotage ? _man : null;

        bool Turned => _man.ActiveClip >= 0 && _man.ActiveClip != 0 && _man.ActiveClip != 1 && _man.ActiveClip != 10;

        bool DartTaken => P != null && (P.HasItem(ItemId.Dart) || P.HasItem(ItemId.Diskette) || P.WerbungTrust);

        public override void Animate(float dt)
        {
            _man.Update(dt);
            _steam.Update(dt, App.Audio);
            _smoke.Update(dt, App.Audio);
            _fuse?.Update(dt, App.Audio);
            if (_eyeRequested) { _eyeRequested = false; _eye.Start(App.Audio); }
            _eye.Update(dt, App.Audio);
        }

        protected override void Hotspots(InputState input)
        {
            // Registered from the lowest to the highest priority (the last match wins; spec: first match wins).
            if (Turned) { Hot(376, 106, 546, 440, ClickMan); Hot(288, 227, 631, 323, ClickMan); }
            else Hot(422, 265, 548, 439, ClickMan);
            if (!DartTaken) Hot(138, 9, 211, 120, ClickDart);
            Exit(86, 98, 209, 439);

            // Click-only spots without a cursor change (§3 hotspot table).
            if (!input.LeftDown) return;
            int mx = input.Mouse.x - RX, my = input.Mouse.y;
            if (_fuse != null && mx >= 298 && mx <= 337 && my >= 395 && my <= 423) _fuse = null;
            var w = W;
            if (w != null && w.Items.Pliers && mx >= 384 && mx <= 426 && my >= 204 && my <= 263)
            {
                var r = w.Execute(new PickUpItemCommand(P.Index, ItemId.Pliers));
                if (!r.Ok && r.Reason == "inventory-full")
                    Menus.Open(RequestMenu.Create(RequestMenu.Items, null, null), new Vector2Int(mx, my), 0.17f);
                input.Consume();
            }
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMan) StartDialog(Talkers.Sabotage, DialogMedium.Air, 0);
            else if (id == ClickDart) StartDialog(Talkers.Sabotage, DialogMedium.Air, 800);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int x = RX;
            if (DartTaken) s.Draw(Assets.Texture(Lib, "OHNEDART"), x + 148, 42);
            if (W != null && W.Items.Pliers && !IsPhonePane) s.Draw(Assets.Texture(Lib, "PINCERS"), x + 369, 188);
            _steam.Draw(s, Assets, x + 423, 0);
            _smoke.Draw(s, Assets, x + 344, 299);
            _fuse?.Draw(s, Assets, x + 287, 386);
            _eye.Draw(s, Assets, x + 20, 37);
            if (Turned) s.Draw(Assets.Texture(Lib, "S_BASE16"), x + 377, 194);
            _man.Draw(s, x);
        }
    }

    /// <summary>
    /// The saboteur (rooms-and-ui-3b §9): victim, category, prank; plane pranks and impounding pick a plane
    /// (<see cref="SabotageTargetMenus.Planes"/>), route theft a route. Validation and payment are the items
    /// domain's (<see cref="SabotageEngine.CheckOrder"/>, <see cref="OrderPrankCommand"/>); a refusal
    /// "sabo-&lt;id&gt;" is the line the saboteur says.
    /// </summary>
    [DialogScript(Talkers.Sabotage)]
    public sealed class SaboDialog : DialogTree
    {
        /// <summary>The order being put together (survives the plane/route menu, which restarts the dialog).</summary>
        internal static int Victim = -1, Mode, Target = -1;

        static int[] Others(DialogSession d)
        {
            var r = new int[3];
            int k = 0;
            for (int i = 0; i < Player.Count && k < 3; i++) if (i != d.Player.Index) r[k++] = i;
            return r;
        }

        /// <summary>Text id from a refusal reason "sabo-1100" (0 when it is not a saboteur line).</summary>
        static int Line(CommandResult r)
        {
            if (r.Ok || r.Reason == null || !r.Reason.StartsWith("sabo-")) return 0;
            return int.TryParse(r.Reason.Substring(5), out int id) ? id : 0;
        }

        static void Order(DialogSession d, PrankFamily f, int mode, int target, int success)
        {
            var r = d.World.Execute(new OrderPrankCommand(d.Player.Index, Victim, f, mode, target));
            if (r.Ok) { d.Stage.App.Audio.Play("SOUND/pay.raw"); if (success > 0) d.Say(success); else d.End(); return; }
            Refuse(d, r);
        }

        /// <summary>The saboteur's refusal line (protected victims are named: Sabo 2090..2098 "%s").</summary>
        static void Refuse(DialogSession d, CommandResult r)
        {
            int line = Line(r);
            if (line > 0) d.Say(line, VictimAirline(d)); else d.End();
        }

        static string VictimAirline(DialogSession d) => Victim >= 0 && Victim < Player.Count ? d.World.Players[Victim].Airline : "";

        public SaboDialog() : base("Sabo")
        {
            Open(d =>
            {
                var p = d.Player;
                int par = d.Par1;
                if (par == 2000 || par == 1070 || par == 1090 || par == 1091 || par == 1071 || par >= 10000) { d.Say(par); return; }
                if (p.Sabotage.Trust == 0) { d.Say(SaboText.NoTrust); return; }   // also for the spider: he keeps it
                if (par == 3000) { d.Say(3000); return; }
                if (par == 800) { d.Say(p.SpiderTrust ? 3002 : 3001); return; }
                if (!p.Sabotage.AnyPending) { d.Say(d.TalkedToA ? 1022 : 1020); d.TalkedToA = true; return; }
                d.Say(1025);
            });
            On(new[] { 1020, 1022, 1045, 1500 }, d =>
            {
                var o = Others(d);
                var w = d.World;
                var args = new object[3];
                for (int k = 0; k < 3; k++) args[k] = w.Players[o[k]].Airline;
                d.Choices(1030, 1033, ChoiceArgs.OnePerOption, args);
            });
            On(1030, 1032, (d, id) =>
            {
                Victim = Others(d)[id - 1030];
                d.Say(d.World.Players[Victim].IsOut ? SaboText.VictimOut : 1040);
            });
            On(1040, d => d.Choices(1047, 1049));
            On(1047, d => Category(d, PrankFamily.Plane, 1050));
            On(1048, d => Category(d, PrankFamily.Person, 1250));
            On(1049, d => Category(d, PrankFamily.Special, 1080));
            On(new[] { 1050, 1250, 1080 }, d => d.Say(1071));
            On(1051, 1055, (d, id) =>
            {
                var r = d.World.Execute(new SelectPlanePrankCommand(d.Player.Index, Victim, id - 1050));
                if (!r.Ok) { Refuse(d, r); return; }
                Mode = id - 1050;
                d.Say(1060);
            });
            On(1060, d => d.OpenMenu(SabotageTargetMenus.Planes(d, MenuIds.SabotagePlane, 1070), 1071, new Vector2Int(320, 220), 0.17f));
            On(1070, d => Order(d, PrankFamily.Plane, Mode, Target, 0));
            On(1251, 1254, (d, id) => Order(d, PrankFamily.Person, id - 1250, -1, 1160));
            On(1081, 1084, (d, id) => Order(d, PrankFamily.Special, id - 1080, -1, 1160));
            On(1085, d =>
            {
                int code = SabotageEngine.CheckOrder(d.World, d.Player, Victim, PrankFamily.Special, 5, -1, targetChosen: false);
                if (code > 0) { d.Say(code, VictimAirline(d)); return; }
                if (code < 0) { d.End(); return; }
                d.OpenMenu(SabotageTargetMenus.Planes(d, MenuIds.SabotagePlane, 1090), 1071, new Vector2Int(320, 220), 0.17f);
            });
            On(1090, d => Order(d, PrankFamily.Special, 5, Target, 1160));
            On(1086, d =>
            {
                int code = SabotageEngine.CheckOrder(d.World, d.Player, Victim, PrankFamily.Special, 6, -1, targetChosen: false);
                if (code > 0) { d.Say(code, VictimAirline(d)); return; }
                if (code < 0) { d.End(); return; }
                d.OpenMenu(SabotageTargetMenus.Routes(d, 1091), 1071, new Vector2Int(320, 220), 0.17f);
            });
            On(1091, d => Order(d, PrankFamily.Special, 6, Target, 1160));
            On(3002, d => { d.World.Execute(new ReceiveRewardCommand(d.Player.Index, ItemId.Dart)); d.End(); });
        }

        static void Category(DialogSession d, PrankFamily f, int first)
        {
            if (d.Player.Sabotage.AnyPending) { d.Say(SaboText.AlreadyPending); return; }
            int n = SabotageEngine.OfferedOptions(d.World, d.Player, f);
            d.Choices(first, first + n);
        }
    }

    /// <summary>
    /// MENU_SABOTAGEPLANE / MENU_SABOTAGEROUTE (rooms-and-ui-2 §4.1, rooms-and-ui-8 §3): the victim's planes
    /// (hovering shows the plane card on the left page) or rented routes. Picking restarts the saboteur's
    /// dialog with the confirmation line; right click cancels (re-entry 1071).
    /// </summary>
    public static class SabotageTargetMenus
    {
        public static NotepadListMenu Planes(DialogSession d, int type, int confirmLine)
        {
            var w = d.World;
            var v = w.Players[SaboDialog.Victim];
            var m = new NotepadListMenu(type) { Title0 = v.Airline };
            foreach (var pl in v.Planes.ToList())
                m.Rows.Add(new NotepadListMenu.Row { Name = pl.Name, Value = pl.Condition + "%", Tag = pl.Id });
            var app = d.Stage.App;
            m.DrawTip = (s, x, y, i) =>
            {
                var pl = v.Planes.Find((int)m.Rows[i].Tag);
                PlaneTips.DrawPlane(s, app.Assets, app.Ui.Kit, x + 6, y + 6, pl, computerOwned: true);
            };
            m.Picked = (i, row) => Restart(app, (int)row.Tag, confirmLine);
            return m;
        }

        public static NotepadListMenu Routes(DialogSession d, int confirmLine)
        {
            var w = d.World;
            var v = w.Players[SaboDialog.Victim];
            var m = new NotepadListMenu(MenuIds.SabotageRoute) { Title0 = v.Airline };
            for (int i = 0; i < v.RentRoutes.Length && i < w.RouteTable.Count; i++)
            {
                var rr = v.RentRoutes[i];
                if (rr == null || rr.Rank == 0) continue;
                var route = w.RouteTable[i];
                // Each licence covers both directions; list a city pair once.
                if (route.From > route.To && Rented(w, v, route.To, route.From)) continue;
                m.Rows.Add(new NotepadListMenu.Row { Name = w.City(route.From).Name + " - " + w.City(route.To).Name, Value = rr.Image + "%", Tag = i });
            }
            var app = d.Stage.App;
            m.Picked = (i, row) => Restart(app, (int)row.Tag, confirmLine);
            return m;
        }

        static bool Rented(World w, Player v, int from, int to)
        {
            for (int i = 0; i < v.RentRoutes.Length && i < w.RouteTable.Count; i++)
                if (v.RentRoutes[i] != null && v.RentRoutes[i].Rank != 0 && w.RouteTable[i].From == from && w.RouteTable[i].To == to) return true;
            return false;
        }

        static void Restart(OpenAT.View.Boot.GameApp app, int target, int line)
        {
            SaboDialog.Target = target;
            app.Rooms.Current?.StartDialog(Talkers.Sabotage, DialogMedium.Air, line);
        }
    }
}
