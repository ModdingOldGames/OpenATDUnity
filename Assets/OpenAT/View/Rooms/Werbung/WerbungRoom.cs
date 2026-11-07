using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Werbung
{
    /// <summary>
    /// The advertising agency (rooms-and-ui-7 §7): the ad man, the office boy, the monitor, the coffee
    /// machine and the sparking socket, the season poster; campaigns through
    /// <see cref="AdCampaignCommand"/>. Below the "Normal" mission (except the free game) the agency is
    /// closed: nowerb.gli, no talker, no animations.
    /// </summary>
    [Room(RoomIds.Werbung)]
    [ScreenName("werbung")]
    public sealed class WerbungRoom : RoomScreen
    {
        bool Closed
        {
            get
            {
                var w = App?.Session?.World;
                int d = w?.Difficulty ?? Mission.FreeGame;
                return d < Mission.ConnectWorld && d != Mission.FreeGame;
            }
        }

        string Lib => Closed ? "room/nowerb.gli" : "room/werbung.gli";
        protected override string Library => Lib;
        protected override string Background => "WERBUNG";
        protected override int DefaultDialogPartner => Closed ? -1 : Talkers.Werbung;

        const int ClickMan = 10, ClickDisk = 800, ClickScreen = 900, ClickFuse = 901;

        SmackerPerson _man;
        FrameAnimation _boy, _screen, _cafe, _sparks;

        protected override void OnCreate()
        {
            if (Closed) return;
            const int x = 290, y = 240;
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "WA_copyC.smk", x, y, Mood.Idle, "A9:0 A3:1 A2:2 E1:3", sound: "wa_copyc.raw");
            _man.Clip(1, "WA_copy.smk", x, y, Mood.Idle, "A9:0 E1:3", sound: "wa_copy.raw");
            _man.Clip(2, "WA_wait.smk", x, y, Mood.Idle, "A9:0 E1:3", waitMin: 30, waitMax: 60);
            _man.Clip(3, "WA_turn.smk", x, y, Mood.Idle, "A9:4 E1:4 E1:5", canCancel: false);
            _man.Clip(4, "WA_redeA.smk", x, y, Mood.Talking, "A9:4 E1:5 E1:7");
            _man.Clip(5, "WA_redeW.smk", x, y, Mood.Listening, "A9:5 A1:6 E1:4 E1:7", waitMin: 2, waitMax: 2);
            _man.Clip(6, "WA_redeK.smk", x, y, Mood.Listening, "A9:5 E1:4 E1:7", waitMin: 2, waitMax: 2);
            _man.Clip(7, "WA_turnZ.smk", x, y, Mood.Idle, "A9:0", canCancel: false);

            string lib = Lib;
            _boy = new FrameAnimation(lib, "BOY_", 3, AnimMode.Random, 0, 25);
            _screen = new FrameAnimation(lib, "SCREEN", 6, AnimMode.Repeat, 250, 4);
            _cafe = new FrameAnimation(lib, "CAFE", 15, AnimMode.Repeat, 280, 3, 380, "Kaffee.raw");
            _sparks = new FrameAnimation(lib, "SPRATZL", 5, AnimMode.Repeat, 280, 1, 190, "Spratzel.raw", showFirst: false, first: 0, digits: 1);
        }

        protected override void OnRoomEnter()
        {
            // TODO(rooms-and-ui-1 §5.4): the hint HINT_WERBUNG after day 4.
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Werbung ? _man : null;

        public override void Animate(float dt)
        {
            if (Closed) return;
            _man.Update(dt);
            _boy.Update(dt, App.Audio);
            _screen.Update(dt, App.Audio);
            _cafe.Update(dt, App.Audio);
            _sparks.Update(dt, App.Audio);
        }

        bool HasDiskette => App.Session?.Local.HasItem(ItemId.Diskette) ?? false;

        protected override void Hotspots(InputState input)
        {
            Exit(0, 145, 70, 381);
            if (Closed)
            {
                if (ClickIn(input, 178, 72, 183, 88) || ClickIn(input, 447, 85, 470, 100)) App.Audio.Play("SOUND/Fused.raw");
                return;
            }
            Hot(302, 257, 454, 419, ClickMan);
            if (!HasDiskette) Hot(74, 274, 111, 310, ClickDisk);
            if (ClickIn(input, 287, 136, 322, 164) || ClickIn(input, 322, 131, 351, 156) || ClickIn(input, 354, 126, 393, 150))
                _screen.Start(App.Audio);
        }

        /// <summary>Click-only easter eggs: no cursor change.</summary>
        bool ClickIn(InputState input, int x1, int y1, int x2, int y2)
        {
            if (!input.LeftDown) return false;
            int x = input.Mouse.x - RX, y = input.Mouse.y;
            return x >= x1 && x <= x2 && y >= y1 && y <= y2;
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMan) StartDialog(Talkers.Werbung, DialogMedium.Air, 1);
            else if (id == ClickDisk) StartDialog(Talkers.Werbung, DialogMedium.Air, 800);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            var w = App.Session?.World;
            int season = w?.Clock.Season ?? 0;
            s.Draw(Assets.Texture(Lib, "WERBUNG" + season), ox + (Closed ? 491 : 495), 0);
            if (Closed) return;
            var p = w?.Local;
            if (HasDiskette) s.Draw(Assets.Texture(Lib, "OHNEDISK"), ox + 82, 281);
            if (p != null && (p.SecurityFlags & 32) != 0) s.Draw(Assets.Texture(Lib, "NOSABO"), ox + 139, 241);
            _boy.Draw(s, Assets, ox + 486, 139);
            _screen.Draw(s, Assets, ox + 287, 77);
            _cafe.Draw(s, Assets, ox + 161, 30);
            _sparks.Draw(s, Assets, ox + 388, 79);
            _man.Draw(s, ox);
        }
    }

    /// <summary>The ad man (rooms-and-ui-3b §10): image report, company / route / everything campaigns in six sizes.</summary>
    [DialogScript(Talkers.Werbung)]
    public sealed class WerbungDialog : DialogTree
    {
        public const string MetKey = "openat.metWerbung";

        static object[] Prices(int type)
        {
            var a = new object[6];
            for (int k = 0; k < 6; k++) a[k] = UiKit.Thousands(Advertising.PriceOf((AdType)type, k));
            return a;
        }

        public WerbungDialog() : base("Werb")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 == 8001) { d.Say(8001); return; }
                if (d.Par1 == 800) { d.Say(p.WerbungTrust ? 8002 : 8000); return; }
                if (d.Par1 == 3501) { d.Say(3501); return; }
                if (d.Par1 >= 10000) { d.End(); return; }
                if (d.Medium == DialogMedium.Phone) d.Say(1001);
                else if (d.TalkedToA) d.Say(1003);
                else if (p.Extensions.ContainsKey(MetKey)) d.Say(1002);
                else d.Say(1000);
                d.TalkedToA = true;
                p.Extensions[MetKey] = true;
            });
            On(new[] { 1000, 1001, 1002, 1003, 3400, 3401 }, d => d.Choices(2000, 2004));
            On(new[] { 2500, 2501, 2502, 2503, 2504, 2505, 2506, 2507, 2508, 2509, 2520 }, d => d.Choices(2001, 2004));
            On(2000, d =>
            {
                int image = Mathf.Clamp(d.Player.Image, -1000, 1000);
                d.Say(Advertising.ImageReportText(image), d.Player.Airline);
                if (image != 0) d.ShowNumbers(9992050, (long)Mathf.Abs(image));
            });
            On(2001, d => { d.Par1 = 0; d.Say(3000); });
            On(2003, d => { d.Par1 = 2; d.Say(3000); });
            On(2002, d => { d.Par1 = 1; d.Say(3500); });
            On(3500, d =>
            {
                if (d.Medium == DialogMedium.Phone) { d.Say(3400); return; }
                d.OpenMenu(AdRouteMenu.Create(d), 3400);
            });
            On(3501, d => { d.Par1 = 1; d.Say(3001); });
            On(3000, d => d.Say(3001));
            On(3001, d => d.Choices(4000, 4006, ChoiceArgs.OnePerOption, Prices(d.Par1)));
            On(4000, 4005, (d, id) => { d.Par3 = id - 4000; d.Say(5000 + id - 4000); });
            On(5000, 5005, (d, id) =>
            {
                int tier = id - 5000;
                var type = (AdType)Mathf.Clamp(d.Par1, 0, 2);
                var r = d.World.Execute(new AdCampaignCommand(d.Player.Index, type, tier, type == AdType.Route ? d.Par2 : 0));
                if (!r.Ok) { d.Say(6000); return; }
                d.Say(3401);
            });
            // 8001: the dart was given (the items domain sets WerbungTrust); 8002: the diskette.
            On(8001, d => d.End());
            On(8002, d => { d.World.Execute(new ReceiveRewardCommand(d.Player.Index, ItemId.Diskette)); d.End(); });
        }
    }

    /// <summary>MENU_ADROUTE (rooms-and-ui-2 §4.1): pick one of the rented routes for a route campaign.</summary>
    public static class AdRouteMenu
    {
        public static NotepadListMenu Create(DialogSession d)
        {
            var w = d.World;
            var p = d.Player;
            var m = new NotepadListMenu(MenuIds.AdRoute, "gli/listrout.gli");
            var bridge = w.OperationsBridge;
            foreach (int id in bridge.RentedRouteIds(w, p))
            {
                var def = w.Data.Routes.Find(r => r.Id == id);
                if (def == null) continue;
                m.Rows.Add(new NotepadListMenu.Row { Name = Name(w, def.FromCity) + " - " + Name(w, def.ToCity), Value = bridge.GetRouteImage(w, p, id) + "%", Tag = id });
            }
            var stage = d.Stage;
            m.Picked = (i, row) =>
            {
                if (stage is RoomScreen room) room.StartDialog(Talkers.Werbung, DialogMedium.Air, 3501, (int)row.Tag);
            };
            return m;
        }

        static string Name(World w, int cityId)
        {
            foreach (var c in w.Data.Cities.All) if (c.Id == cityId) return c.Name;
            return "?";
        }
    }
}
