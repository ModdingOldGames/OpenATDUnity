using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Office
{
    /// <summary>
    /// The player's office (rooms-and-ui-5 §1): background per airline (room/buero_a..d.gli "BUERO"),
    /// the seated player figure (PL{n}*.smk) reading letters and phoning, the door overlays, the in-tray,
    /// and the hotspots: globe (flight planning), kerosene tank card, coat (call it a day), letters,
    /// phone book and the equipment room.
    /// </summary>
    [Room(RoomIds.OfficeA), Room(RoomIds.OfficeB), Room(RoomIds.OfficeC), Room(RoomIds.OfficeD)]
    [ScreenName("office")]
    public sealed class OfficeRoom : RoomScreen
    {
        const int ClickGlobe = 10, ClickKerosene = 11, ClickCoat = 12, ClickLetters = 13, ClickPhone = 14, ClickPlanes = 15;

        int Owner => RoomId >= RoomIds.OfficeA && RoomId <= RoomIds.OfficeD ? RoomId / 10 - 1 : App.Session?.World.LocalPlayer ?? 0;
        int Local => App.Session?.World.LocalPlayer ?? 0;
        string Lib => "room/buero_" + (char)('a' + Owner) + ".gli";
        protected override string Library => Lib;
        protected override string Background => "BUERO";
        public override bool IsOwnOffice => Owner == Local;

        SmackerPerson _me, _clock;
        bool _onPhone, _reading;

        protected override void OnCreate()
        {
            LetterMailbox.Attach(App);
            int n = Owner + 1;
            string P(string s) => "PL" + n + s + ".smk";
            _me = new SmackerPerson(Assets, App.Audio);
            _me.Clip(0, P("Wait"), 40, 150, Mood.Idle, "A9:0 A1:1 A1:2 A1:3 E1:4 E1:7", 1, 1, 10, 30);
            _me.Clip(1, P("Blat"), 40, 150, Mood.Idle, "A1:0", canCancel: false, sound: "PL" + n + "BLAT.raw");
            _me.Clip(2, P("Scri1"), 40, 150, Mood.Idle, "A1:0", canCancel: false, sound: "pl" + n + "scri1.raw");
            _me.Clip(3, P("Scri2"), 40, 150, Mood.Idle, "A1:0", canCancel: false, sound: "pl" + n + "scri2.raw");
            _me.Clip(4, P("Brief"), 40, 150, Mood.Letter, "A1:5", canCancel: false).OnEnd = OpenLetters;
            _me.Clip(5, P("BrifW"), 40, 150, Mood.Letter, "A9:5 E1:6");
            _me.Clip(6, P("BrifZ"), 40, 150, Mood.Idle, "A1:0", canCancel: false);
            _me.Clip(7, P("Telef"), 40, 150, Mood.Listening, "A1:8", canCancel: false, sound: "pl" + n + "telef.raw").OnEnd = OpenPhoneBook;
            _me.Clip(8, P("TeleW"), 40, 150, Mood.Listening, "A9:8 E1:9 E1:10");
            _me.Clip(9, P("TeleR"), 40, 150, Mood.Talking, "A9:9 E1:8 E1:10");
            _me.Clip(10, P("TeleZ"), 40, 150, Mood.Idle, "A1:0", canCancel: false, sound: "pl" + n + "telez.raw");
            if (Owner == 1)
            {
                _clock = new SmackerPerson(Assets, App.Audio);
                _clock.Clip(0, "Uhr.smk", 420, 109, Mood.Idle, "A1:0", sound: "Uhr.raw");
            }
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => null;

        protected override void OnRoomEnter()
        {
            var s = App.Session;
            if (s != null && s.World.Clock.Date > 4) { /* hint "Rick" belongs to the hint system (not written yet) */ }
        }

        bool HasLetters => App.Session != null && LettersMenu.Source(App.Session.Local).Count > 0;
        bool HasPlanes => App.Session != null && App.Session.Local.Planes.Count > 0;

        void OpenLetters()
        {
            if (!_reading) return;
            var m = new LettersMenu();
            m.Closed = () => { _reading = false; _me.SetDesired(Mood.Idle); };
            Menus.Open(m, new Vector2Int(379, 382), 0.1f);
        }

        void OpenPhoneBook()
        {
            if (!_onPhone) return;
            var m = new FilofaxMenu(2);
            m.Closed = () => { if (App.Rooms.Phone == null) { _onPhone = false; _me.SetDesired(Mood.Idle); } };
            Menus.Open(m, new Vector2Int(363, 321), 0.1f);
        }

        public override void Animate(float dt)
        {
            _me.Update(dt);
            _clock?.Update(dt);
            // After a phone call from the office the figure hangs up.
            if (_onPhone && !Menus.IsOpen && App.Rooms.Phone == null && _me.ActiveClip == 8) { _onPhone = false; _me.SetDesired(Mood.Idle); }
        }

        protected override void Hotspots(InputState input)
        {
            if (_me.ActiveClip == 6) return; // putting the letter down
            // Registered from lowest to highest priority: the last hovered one gets the click.
            if (HasPlanes) Hot(396, 279, 490, 359, ClickPlanes, 4402);
            Hot(322, 302, 432, 368, ClickPhone, 4405);
            if (HasLetters) Hot(318, 368, 450, 431, ClickLetters, 4404);
            if (Local == 0) Hot(396, 42, 533, 173, ClickCoat, 4403);
            else Hot(454, 102, 524, 266, ClickCoat, 4403);
            Hot(301, 66, 416, 160, ClickKerosene, 4401);
            Hot(275, 162, 386, 236, ClickGlobe, 4400);
            Exit(535, 52, 639, 292);
        }

        protected override void OnClick(int id)
        {
            switch (id)
            {
                case ClickGlobe: App.Rooms.Enter(RoomIds.Globe); break;
                case ClickKerosene: Menus.Open(new KeroseneMenu(), new Vector2Int(357, 107), 0.1f); break;
                case ClickCoat:
                    if (Clock != null && Clock.State == Sim.Core.DayState.Playing)
                        Menus.Open(RequestMenu.Create(RequestMenu.CallItADay, null, () => GameFlow.CallItADay(App)), new Vector2Int(480, 170), 0.17f);
                    break;
                case ClickLetters: _reading = true; _me.SetDesired(Mood.Letter); break;
                case ClickPhone: _onPhone = true; _me.SetDesired(Mood.Listening); break;
                case ClickPlanes: App.Rooms.Enter(RoomIds.PlaneProps); break;
            }
        }

        public override void UseItem(int slot, int item)
        {
            // The mobile phone in the office uses the desk phone animation (§1.1).
            if (item == 2 && !IsDialogOpen && !Menus.IsOpen) { _onPhone = true; _me.SetDesired(Mood.Listening); return; }
            base.UseItem(slot, item);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            _clock?.Draw(s, ox);
            s.Draw(Assets.Texture(Lib, "DOOR_O"), ox + 521, 53);
            s.Draw(Assets.Texture(Lib, "DOOR_T"), ox + 542, 67, new Color32(255, 255, 255, 140));
            if (App.Session != null && (App.Session.Local.SecurityFlags & (1 << 5)) != 0) s.Draw(Assets.Texture(Lib, "NOSABO"), ox + 390, 309);
            if (HasLetters) s.Draw(Assets.Texture(Lib, "LETTERS"), ox + 351, 390);
            _me.Draw(s, ox);
            // Power cut (§1.6): the dark overlay over everything; hotspots still work.
            if (App.Session != null && App.Session.World.Players[Owner].OfficeState == 3)
            {
                s.Draw(Assets.Texture("room/buerodrk.gli", "BUERO"), ox, 0);
                if (ox > 0) { s.FillRect(new RectInt(0, 0, ox, 440), new Color32(0, 0, 0, 160)); s.FillRect(new RectInt(ox + 640, 0, ox, 440), new Color32(0, 0, 0, 160)); }
            }
        }
    }
}
