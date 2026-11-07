using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Office
{
    /// <summary>
    /// MENU_KEROSIN (rooms-and-ui-2 §4.12): the tank depot card on oilmenu.gli — market price, own average
    /// price, capacity, contents (barrels and %), the valve line (toggles <see cref="SetTankOpenCommand"/>,
    /// only when not empty) and the close line.
    /// </summary>
    public sealed class KeroseneMenu : PopupMenu
    {
        const string Lib = "gli/oilmenu.gli";
        public override int Type => MenuIds.Kerosin;
        public override Vector2Int Size => new Vector2Int(256, 199);
        public override Texture Background => Assets.Texture(Lib, "OILMENU");

        Player P => App.Session.Local;
        bool Empty => P.TankContents <= 0;

        public override void Update(InputState input, Vector2Int l)
        {
            bool valve = !Empty && Hot(l, 18, 98, 255, 118);
            bool close = Hot(l, 18, 141, 255, 173);
            if (!input.LeftDown) return;
            if (valve) { App.Session.World.Execute(new SetTankOpenCommand(P.Index, !P.TankOpen)); App.Audio.Play("SOUND/click.raw"); }
            else if (close) Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            var w = App.Session.World;
            var p = P;
            f.Draw(s, x + 18, y + 8, Std("Tank", 1000));
            // Values left-aligned at x 160 like the original (rooms-and-ui-2 MENU_KEROSIN).
            void Row(int ry, string label, string value) { f.Draw(s, x + 18, y + ry, label); f.Draw(s, x + 160, y + ry, value); }
            Row(28, Std("Tank", 1030), UiKit.Money(w.Kerosene));
            Row(41, Std("Tank", 1031), UiKit.Money((long)p.TankPrice));
            Row(58, Std("Tank", 1001), UiKit.Thousands(p.Tank) + " Barrel");
            int pct = p.Tank > 0 ? (int)((long)p.TankContents * 100 / p.Tank) : 0;
            Row(71, Std("Tank", 1002), UiKit.Thousands(p.TankContents) + " Barrel (" + pct + "%)");
            bool hv = !Empty && l.x >= 18 && l.x <= 255 && l.y >= 98 && l.y <= 118;
            if (hv) HoverBand(s, new RectInt(x + 14, y + 96, 230, 40));
            f.DrawBlock(s, new RectInt(x + 18, y + 100, 232, 40), Std("Tank", Empty ? 1012 : p.TankOpen ? 1011 : 1010), Color.white);
            bool hc = l.x >= 18 && l.x <= 255 && l.y >= 141 && l.y <= 173;
            if (hc) HoverBand(s, new RectInt(x + 14, y + 140, 230, 16));
            f.Draw(s, x + 18, y + 143, Std("Tank", 1020));
        }
    }
}
