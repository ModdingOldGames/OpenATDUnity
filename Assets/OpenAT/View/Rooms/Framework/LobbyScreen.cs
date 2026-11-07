using System.Collections.Generic;
using System.Linq;
using OpenAT.View.Render;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// Stand-in for the airport hall while it does not exist: a list of the implemented rooms (door
    /// tooltips "Tool" 5000 + room id) to enter, with the status bar. Replaced by the "hall" screen
    /// as soon as one is registered (<see cref="RoomHost.CreateAirportBase"/>).
    /// </summary>
    [ScreenName("lobby")]
    public sealed class LobbyScreen : GameScreen, IStatusBarHost
    {
        readonly StatusBar _bar = new StatusBar();
        MenuHost _menus;
        List<int> _rooms;
        /// <summary>Talkers that can be phoned (their room exists): placeholder for the phone book.</summary>
        List<int> _phone;
        int Rows => _rooms.Count + _phone.Count;

        public MenuHost Menus => _menus;
        public bool IsDialogOpen => false;
        public int CanvasX => OX;

        public override void OnEnter()
        {
            App.EnsureSession();
            _menus = new MenuHost(App, () => OX);
            int me = App.Session?.World.LocalPlayer ?? 0;
            // Mod rooms (ids from 1000) first: the list is longer than the screen.
            _rooms = RoomRegistry.Ids.Where(id => id != RoomIds.Airport
                && (!RoomIds.IsOffice(id) || id == RoomIds.OfficeOf(me))
                && (id % 10 != 1 || id > RoomIds.PersonalD || id == RoomIds.PersonalOf(me)))
                .OrderBy(id => id >= RoomIds.FirstCustom ? 0 : 1).ThenBy(id => id).ToList();
            _phone = new List<int>();
            for (int t = 0; t < 27; t++)
            {
                var info = UI.Dialog.Talkers.Get(t);
                if (info != null && info.Room >= 0 && info.PhoneBubblePos != null && RoomRegistry.Has(info.Room)) _phone.Add(t);
            }
        }

        public void UseItem(int slot, int item)
        {
            if (item == 2) _menus.Open(new FilofaxMenu(1), new Vector2Int(248 + 60 * slot, 446), 0.17f, screenOrigin: true);
            if (item == 1) _menus.Open(new FilofaxMenu(2), new Vector2Int(248 + 60 * slot, 446), 0.17f, screenOrigin: true);
        }

        int Row(Vector2Int m)
        {
            int x = m.x - OX, y = m.y;
            if (x < 180 || x > 460 || y < 100) return -1;
            int r = (y - 100) / 22;
            return r < Rows ? r : -1;
        }

        public override void Update(InputState input, float dt)
        {
            App.Advisors.Update(dt, OX, input, !_menus.IsOpen);
            _menus.Animate(dt);
            if (_menus.IsOpen) { _menus.Update(input, dt); return; }
            if (_bar.Update(this, input)) return;
            if (input.Escape || input.F2) { App.OpenOptions(); return; }
            int r = Row(input.Mouse);
            if (r >= 0)
            {
                App.Ui.Cursor = CursorLook.Hot;
                if (input.LeftDown)
                {
                    if (r < _rooms.Count) App.Rooms.Enter(_rooms[r]);
                    else App.Rooms.PhoneCall(_phone[r - _rooms.Count]);
                }
            }
        }

        public override void Draw(Screen2D s)
        {
            s.FillRect(new RectInt(0, 0, s.Width, 440), new Color32(24, 30, 40, 255));
            var f = App.Ui.Kit.BigGrey;
            f.Draw(s, OX + 320, 50, "OpenAT - airport placeholder", TextAlign.Center);
            var m = App.InputNow.Mouse;
            for (int i = 0; i < Rows; i++)
            {
                int y = 100 + 22 * i;
                if (Row(m) == i) s.FillRect(new RectInt(OX + 180, y - 2, 280, 22), new Color32(32, 255, 100, 60));
                string name = i < _rooms.Count
                    ? UI.Dialog.DialogText.Format(Modding.ModBoot.RoomName(_rooms[i]) ?? Assets.StdText.Get("Tool", 5000 + _rooms[i]) ?? ("Room " + _rooms[i]), new object[] { App.Session?.Local.Airline })
                    : "Phone: " + (Assets.StdText.Get("Filo", 2000 + System.Array.FindIndex(FilofaxMenu.PhoneEntries, e => e.talker == _phone[i - _rooms.Count])) ?? UI.Dialog.Talkers.Get(_phone[i - _rooms.Count]).Name);
                f.Draw(s, OX + 200, y, name);
            }
            _bar.Draw(s, this, App.InputNow);
            _menus.Draw(s, App.InputNow);
            App.Advisors.Draw(s, OX);
        }
    }
}
