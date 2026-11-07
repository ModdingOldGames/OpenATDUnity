using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Items
{
    /// <summary>
    /// Inventory clicks in the status bar (rooms-and-ui-1 §9.4) for rooms and the airport hall: the items
    /// domain decides (<see cref="UseItemCommand"/> → <see cref="ItemUsed"/>), the view shows the outcome —
    /// the partner's reaction (dialog opening 10000 + item or the effect's opening), the "not here" notice,
    /// the laptop / phone book / filofax, a sound. Discarding goes through <see cref="DropItemCommand"/>.
    /// </summary>
    public static class ItemUseView
    {
        /// <summary>Uses the item in <paramref name="slot"/>; <paramref name="partner"/> = the room's default dialog partner (−1 none).</summary>
        public static void Use(IStatusBarHost host, int slot, int item, int room, int partner)
        {
            var app = host.App;
            var s = app.Session;
            if (s == null) return;
            var w = s.World;
            var p = s.Local;
            var ctx = new ItemUseContext
            {
                Room = room,
                HasPartner = partner >= 0,
                DialogOrMenuOpen = host.IsDialogOpen || (host.Menus != null && host.Menus.IsOpen),
            };
            var cmd = new UseItemCommand(p.Index, slot, ctx);
            var r = w.Execute(cmd);
            var u = cmd.Result;
            if (!r.Ok || u == null) return;
            if (u.Sound != null) app.Audio.Play("SOUND/" + u.Sound);
            var from = new Vector2Int(248 + 60 * slot, 446);
            var roomScreen = host as RoomScreen;
            switch (u.Outcome)
            {
                case ItemUseOutcome.Gift:
                    roomScreen?.StartDialog(partner, DialogMedium.Air, u.DialogPar);
                    break;
                case ItemUseOutcome.Applied:
                    if (u.DialogPar > 0 && roomScreen != null)
                    {
                        int talker = AppliedTalker(u.DialogPar, room, partner);
                        if (talker >= 0) roomScreen.StartDialog(talker, DialogMedium.Air, u.DialogPar);
                    }
                    if (u.Screen == "leave-laptop" && app.Rooms.CurrentRoom == RoomIds.Laptop) app.Rooms.Leave();
                    break;
                case ItemUseOutcome.Refused:
                    host.Menus?.Open(RequestMenu.Create(u.RequestId, null, null), from, 0.17f, screenOrigin: true);
                    break;
                case ItemUseOutcome.OpenScreen:
                    switch (u.Screen)
                    {
                        case "laptop": app.Rooms.Enter(RoomIds.Laptop); break;
                        // TODO(rooms-and-ui-5 §1): the office phone animation before the phone book.
                        case "phonebook":
                        case "office-phone": host.Menus?.Open(new FilofaxMenu(2), from, 0.17f, screenOrigin: true); break;
                        case "filofax": host.Menus?.Open(new FilofaxMenu(1), from, 0.17f, screenOrigin: true); break;
                    }
                    break;
            }
        }

        /// <summary>
        /// Who answers an item that had its effect (rooms-and-ui-1 §9.4): the original names the talker of each trade
        /// explicitly, so it speaks even when the room has no default partner right now (the energy drink wakes the
        /// sleeping kiosk man); the horseshoe goes to the drinker at Rick's, not to Rick.
        /// </summary>
        static int AppliedTalker(int par, int room, int partner)
        {
            switch (room)
            {
                case RoomIds.Ricks: return par == 800 ? Talkers.Trinker : partner;
                case RoomIds.Kiosk: return Talkers.Kiosk;
                case RoomIds.ArabAir: return Talkers.Arab;
                case RoomIds.Werkstatt: return Talkers.Mechaniker;
                case RoomIds.DutyFree: return Talkers.DutyFree;
                case RoomIds.Fracht: return Talkers.Fracht;
                case RoomIds.Sabotage: return Talkers.Sabotage;
                case RoomIds.Werbung: return Talkers.Werbung;
            }
            return partner;
        }

        /// <summary>Right click → "throw away?" → yes: <see cref="DropItemCommand"/>; leaving the laptop if it was the laptop.</summary>
        public static void Drop(OpenAT.View.Boot.GameApp app, Player p, int slot)
        {
            var w = app.Session?.World;
            if (w == null) return;
            int item = p.Items[slot];
            if (!w.Execute(new DropItemCommand(p.Index, slot)).Ok) return;
            if (item == ItemId.Laptop && app.Rooms.CurrentRoom == RoomIds.Laptop) app.Rooms.Leave();
        }
    }
}
