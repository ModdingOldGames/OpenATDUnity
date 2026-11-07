using OpenAT.Sim.Modding;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using UnityEngine;

// Example view code mod (docs/MODDING.md "Rooms and screens"): a new room with id 1000
// (RoomIds.FirstCustom). No graphics of its own: it draws a panel with the bitmap fonts of the install.
// Registered from OnLoad so it gets a name in the lobby list; a [Room(1000)] attribute on the class
// would register it as well.
namespace OpenAT.Mods.HelloRoom
{
    public sealed class HelloRoomMod : IOpenATMod
    {
        public const int RoomId = RoomIds.FirstCustom;

        public void OnLoad(ModContext ctx)
        {
            ctx.RegisterRoom(RoomId, () => new HelloRoom(), "Hello room (mod)");
            ctx.Log("room " + RoomId + " registered");
        }
    }

    public sealed class HelloRoom : RoomScreen
    {
        const int ClickId = 1;
        int _clicks;

        protected override string Library => null;      // no room graphics: black canvas
        protected override string Background => null;

        protected override void Hotspots(InputState input)
        {
            Hot(200, 250, 440, 290, ClickId);
            Exit(0, 380, 639, 439);                     // bottom strip leaves (right click too)
        }

        protected override void OnClick(int id)
        {
            if (id == ClickId) _clicks++;
        }

        protected override void DrawRoom(Screen2D s)
        {
            int x = RX;
            s.FillRect(new RectInt(x + 60, 60, 520, 300), new Color32(30, 60, 90, 255));
            s.FillRect(new RectInt(x + 200, 250, 240, 40), new Color32(60, 120, 160, 255));
            s.FillRect(new RectInt(x, 380, 640, 60), new Color32(50, 30, 30, 255));
            var big = App.Ui.Kit.BigGrey;
            var small = App.Ui.Kit.SmallWhite;
            big.Draw(s, x + 320, 90, "HELLO FROM A MOD", TextAlign.Center);
            small.Draw(s, x + 320, 140, "Room " + RoomId + " - Mods.Examples/HelloRoom", TextAlign.Center);
            var w = App.Session?.World;
            if (w != null)
            {
                small.Draw(s, x + 320, 170, $"Day {w.Clock.Date + 1}, {w.Clock.ClockText} - {w.Local.Airline}", TextAlign.Center);
                // State of another mod, if it is installed (read through the saved extension bag).
                if (w.Extensions.TryGetValue("mod.routebonusevent", out var st) && st != null)
                {
                    var f = st.GetType().GetField("RouteId");
                    int route = f != null ? (int)f.GetValue(st) : 0;
                    var r = route != 0 ? w.RouteById(route) : null;
                    small.Draw(s, x + 320, 200, r != null
                        ? "Route of the week: " + w.Data.Cities.Find(r.From)?.Name + " - " + w.Data.Cities.Find(r.To)?.Name
                        : "Route of the week: none yet", TextAlign.Center);
                }
            }
            small.Draw(s, x + 320, 262, "Clicked " + _clicks + " times", TextAlign.Center);
            small.Draw(s, x + 320, 402, "Leave", TextAlign.Center);
        }
    }
}
