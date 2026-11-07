using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Sim.Data;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Designer
{
    /// <summary>
    /// Where the player's designer planes live (planes-and-flights.md §5.1: shared files, not game
    /// state): <c>persistentDataPath/myplanes/*.plane</c> in the original file format, so designs can be
    /// exchanged with the original game. <c>OPENAT_DESIGN_DIR</c> overrides the folder (tests, screenshots).
    /// </summary>
    public static class DesignFiles
    {
        public const string Folder = "myplanes";

        public static string Directory
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("OPENAT_DESIGN_DIR");
                return !string.IsNullOrEmpty(env) ? env : Path.Combine(Application.persistentDataPath, Folder);
            }
        }

        public static PlaneDesignCatalog Catalog(GameApp app) =>
            app.Session?.World.DesignCatalog ?? app.Assets.GameData.DesignCatalog ?? PlaneDesignCatalog.CreateDefault();

        public static PlaneDesignLibrary Library(GameApp app) => new PlaneDesignLibrary(Directory, Catalog(app));
    }

    /// <summary>
    /// "DrawXPlaneTipContents": a design's card on the left page of the designer's list (headline at
    /// (6,6), body from (6,28)+15; labels std "Plne", values 70 px to the right).
    /// </summary>
    public static class DesignTips
    {
        static string Std(GameAssets a, string g, int id) => a.StdText.Get(g, id) ?? "";

        public static void Draw(Screen2D s, GameAssets a, UiKit kit, int x, int y, PlaneDesign d, PlaneDesignCatalog cat)
        {
            if (d == null) return;
            var f = kit.SmallBlack;
            var st = d.Stats(cat);
            f.Draw(s, x + 6, y + 6, d.Name);
            int cx = x + 6, cy = y + 28 + 15;
            void Row(int dy, int label, string value)
            {
                f.Draw(s, cx, cy + dy, Std(a, "Plne", label));
                f.Draw(s, cx + 70, cy + dy, value);
            }
            Row(0, 1000, st.Seats.ToString());
            Row(11, 1017, UiKit.Unit(UiKit.UnitT, st.Seats / 10));
            Row(22, 1001, UiKit.Unit(UiKit.UnitKm, st.Range));
            cy += 4;
            Row(33, 1002, UiKit.Unit(UiKit.UnitKmh, st.Speed));
            cy += 11;
            Row(44, 1008, UiKit.Unit(UiKit.UnitL, st.Tank));
            Row(55, 1009, UiKit.Unit(UiKit.UnitLh, st.Burn));
            cy += 4 + 15;
            Row(66, 1010, st.Pilots.ToString());
            Row(77, 1011, st.Attendants.ToString());
            cy += 4;
            Row(88, 1012, UiKit.Money(st.Cost));
        }
    }

    /// <summary>MENU_BUYXPLANE (rooms-and-ui-2 §4.4): the buildable designs; hovering shows the design card.</summary>
    public sealed class BuyXPlaneMenu : NotepadListMenu
    {
        public readonly List<(string file, PlaneDesign design)> Designs;

        public BuyXPlaneMenu(GameApp app) : base(MenuIds.BuyXPlane)
        {
            var lib = DesignFiles.Library(app);
            Designs = lib.Buildable();
            Title0 = app.Assets.StdText.Get("Schd", 1601);
            Title1 = " ";
            foreach (var d in Designs) Rows.Add(new Row { Name = d.design.Name, Value = " ", Tag = d.file });
            DrawTip = (s, x, y, i) => DesignTips.Draw(s, App.Assets, Kit, x, y, Designs[i].design, lib.Catalog);
        }
    }

    /// <summary>
    /// Designed planes drawn from their parts' side-view bitmaps (<c>P2D_&lt;part&gt;</c> in glstd.gli) at the
    /// stored 2-D positions ("CXPlane::BlitPlaneAt", behind-glass size): used by the airport windows.
    /// </summary>
    public static class DesignedPlaneArt
    {
        public static string SideBitmap(string shortName) => "P2D_" + shortName;
    }
}
