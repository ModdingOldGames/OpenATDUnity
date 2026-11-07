using OpenAT.Sim;
using OpenAT.Sim.Data;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Makler
{
    /// <summary>
    /// Plane and plane-type cards drawn on the left "tip page" of the notepad lists (rooms-and-ui-2 §4.1:
    /// headline at (6,6), body from (6,28)) and on the museum tip block. Labels from std "Plne".
    /// </summary>
    public static class PlaneTips
    {
        static string Std(GameAssets a, string g, int id) => a.StdText.Get(g, id) ?? "";

        // Layout (rooms-and-ui-7 §0 plane tips, verified on the running original): headline at (x, y),
        // contents from (x, y + 22); labels at the contents x, values left-aligned 70 px further; block gaps
        // between the size, performance, crew and price blocks.
        const int ValueX = 70;

        static void Row(Screen2D s, UiKit kit, int x, int y, string label, string value)
        {
            kit.SmallBlack.Draw(s, x, y, label);
            kit.SmallBlack.Draw(s, x + ValueX, y, value);
        }

        /// <summary>A catalogue type (broker list).</summary>
        public static void DrawType(Screen2D s, GameAssets a, UiKit kit, int x, int y, PlaneType t, int width = 196)
        {
            if (t == null) return;
            kit.SmallBlack.Draw(s, x, y, t.Manufacturer ?? "");
            kit.SmallBlack.Draw(s, x, y + 12, t.Name ?? "");
            int cy = y + 22 + 15;
            Row(s, kit, x, cy, Std(a, "Plne", 1000), t.Seats.ToString());
            Row(s, kit, x, cy + 11, Std(a, "Plne", 1017), UiKit.Unit(UiKit.UnitT, t.Seats / 10));
            Row(s, kit, x, cy + 22, Std(a, "Plne", 1001), UiKit.Unit(UiKit.UnitKm, t.Range));
            cy += 4;
            Row(s, kit, x, cy + 33, Std(a, "Plne", 1002), UiKit.Unit(UiKit.UnitKmh, t.Speed));
            cy += 11;
            Row(s, kit, x, cy + 33, Std(a, "Plne", 1007), UiKit.Unit(UiKit.UnitKn, t.Thrust));
            Row(s, kit, x, cy + 44, Std(a, "Plne", 1008), UiKit.Unit(UiKit.UnitL, t.TankSize));
            Row(s, kit, x, cy + 55, Std(a, "Plne", 1009), UiKit.Unit(UiKit.UnitLh, t.FuelBurn));
            cy += 4 + 15;
            Row(s, kit, x, cy + 66, Std(a, "Plne", 1010), t.Pilots.ToString());
            Row(s, kit, x, cy + 77, Std(a, "Plne", 1011), t.Attendants.ToString());
            cy += 4;
            Row(s, kit, x, cy + 88, Std(a, "Plne", 1012), UiKit.Money(t.Price));
        }

        /// <summary>
        /// An individual plane (museum offers, own planes, workshop, sabotage target). <paramref name="ownPlane"/>
        /// adds the condition line (workshop); <paramref name="computerOwned"/> shows the type's full crew.
        /// The price defaults to the plane's market value.
        /// </summary>
        public static void DrawPlane(Screen2D s, GameAssets a, UiKit kit, int x, int y, Plane p, long? price = null, int width = 196,
            string extraLabel = null, string extraValue = null, bool ownPlane = false, bool computerOwned = false, int contentsDy = 22)
        {
            if (p == null) return;
            kit.SmallBlack.Draw(s, x, y, p.Name ?? "");
            int cy = y + contentsDy;   // menus: headline (6,6), contents (6,28); tip blocks: (32,3) / (32,20)
            kit.SmallBlack.Draw(s, x, cy - 5, p.Maker ?? "");
            kit.SmallBlack.Draw(s, x, cy + 7, p.Model ?? "");
            cy += 12 + 15;
            Row(s, kit, x, cy, Std(a, "Plne", 1000), p.Seats.ToString());
            Row(s, kit, x, cy + 11, Std(a, "Plne", 1017), UiKit.Unit(UiKit.UnitT, p.Seats / 10));
            Row(s, kit, x, cy + 22, Std(a, "Plne", 1001), UiKit.Unit(UiKit.UnitKm, p.Range));
            cy += 4;
            Row(s, kit, x, cy + 33, Std(a, "Plne", 1002), UiKit.Unit(UiKit.UnitKmh, p.Speed));
            Row(s, kit, x, cy + 44, Std(a, "Plne", 1008), UiKit.Unit(UiKit.UnitL, p.Tank));
            Row(s, kit, x, cy + 55, Std(a, "Plne", 1009), UiKit.Unit(UiKit.UnitLh, p.Burn));
            cy += 4;
            if (ownPlane) Row(s, kit, x, cy + 66, Std(a, "Plne", 1020), UiKit.Unit(UiKit.UnitPercent, p.Condition));
            cy += 15;
            string fmt = Std(a, "Plne", 1014);
            long pil = computerOwned ? p.Pilots : p.CrewPilots, att = computerOwned ? p.Attendants : p.CrewAttendants;
            Row(s, kit, x, cy + 66, Std(a, "Plne", 1010), UI.Dialog.DialogText.Format(fmt, new object[] { pil, (long)p.Pilots }));
            Row(s, kit, x, cy + 77, Std(a, "Plne", 1011), UI.Dialog.DialogText.Format(fmt, new object[] { att, (long)p.Attendants }));
            cy += 4;
            Row(s, kit, x, cy + 88, Std(a, "Plne", 1012), UiKit.Money(price ?? p.MarketValue));
            Row(s, kit, x, cy + 99, Std(a, "Plne", 1013), p.BuildYear.ToString());
            if (extraLabel != null) Row(s, kit, x, cy + 110, extraLabel, extraValue);
        }
    }
}
