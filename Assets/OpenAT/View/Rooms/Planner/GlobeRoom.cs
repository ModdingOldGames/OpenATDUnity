using System.Collections.Generic;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// The globe room with the filofax (rooms-and-ui-5 §2, 5b): the own planes on the turnable globe,
    /// one double-page block with tabs (planes / cities / info left, orders / freight / routes right),
    /// the week grid with drag-and-drop planning. Entered from the office globe.
    /// </summary>
    [Room(RoomIds.Globe)]
    [ScreenName("globe")]
    public sealed class GlobeRoom : PlannerRoom
    {
        const string Kit0 = "room/globe_a.gli";
        int Me => App.Session?.World.LocalPlayer ?? 0;
        protected override string Library => "room/globe_a" + (char)('a' + Me) + ".gli";
        protected override string Background => "GLOBE";
        protected override bool IsLaptop => false;
        protected override Vector2Int GlobeOrigin => new Vector2Int(141, 16);

        readonly List<Block> _blocks = new List<Block>();
        protected override IReadOnlyList<Block> Blocks
        {
            get
            {
                _blocks.Clear();
                if (State != null && State.FilofaxVisible) _blocks.Add(State.EnsureFilofax());
                return _blocks;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            bool fresh = State.Filofax == null;
            var f = State.EnsureFilofax();
            // Tutorial: the window is always open, its left side starts empty and the right shows routes (5 §2.4).
            if (Tutorial.TutorialView.InitialFilofax(out int left, out int right))
            {
                State.FilofaxVisible = true;
                if (fresh) { f.Left.Type = left; f.Right.Type = right; }
            }
        }

        protected override Vector2Int? TutorialBlockOrigin =>
            State != null && State.FilofaxVisible && State.Filofax != null ? State.Filofax.Pos : (Vector2Int?)null;

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            // City details always reset to the index on entry (5b §1.2).
            var f = State.Filofax;
            if (f.Left.IsDetail(BlockType.Cities)) BackToIndex(f.Left);
        }

        protected override PaneGeom Geom(Block b, bool right)
        {
            int dx = right ? 232 : 0;
            return new PaneGeom
            {
                Client = new Vector2Int(48 + dx, 72), Title = new Vector2Int(48 + dx, 57), PageNo = new Vector2Int(137 + dx, 242),
                Prev = PaneGeom.R(40 + dx, 237, 60 + dx, 256), Next = PaneGeom.R(208 + dx, 233, 231 + dx, 255), Index = PaneGeom.R(dx, 78, 36 + dx, 181),
            };
        }

        protected override bool ShowPlanesOf(int player) => player == Me;

        protected override Vector2Int ClampBlock(Block b, Vector2Int pos) =>
            new Vector2Int(Mathf.Clamp(pos.x, -b.Size.x / 2, 640 - b.Size.x / 2), Mathf.Clamp(pos.y, -20, 400));

        protected override void BringToFront(Block b) { }

        protected override void BlockRightClick(Block b)
        {
            State.FilofaxVisible = false;
            Carry = Carry.None;
            App.Audio.Play("SOUND/paper.raw");
        }

        protected override void OpenPlaneFromGlobe(Plane pl, Vector2Int canvasPos)
        {
            State.FilofaxVisible = true;
            var f = State.EnsureFilofax();
            f.SwitchType(f.Left, BlockType.Planes);
            if (f.Right.Type == BlockType.None) f.Right.Type = BlockType.Orders;
            OpenDetail(f, f.Left, pl.Id);
            f.Left.Pages = 4; // QUIRK (5b §4.2): a globe click gives 3 + photos pages
        }

        // ------------------------------------------------------------------ tabs and move strip

        int LeftTabAt(Vector2Int l)
        {
            int t = BlockType.None;
            if (l.y >= 260 && l.y <= 290)
            {
                if (l.x >= 28 && l.x <= 108) t = BlockType.Planes;
                if (l.x >= 108 && l.x <= 188) t = BlockType.Cities;
                if (l.x >= 169 && l.x <= 268) t = BlockType.Info;
            }
            return t;
        }

        int RightTabAt(Block b, Vector2Int l)
        {
            if (b.Left.Type != BlockType.Planes || l.y < 258 || l.y > 289) return BlockType.None;
            int t = BlockType.None;
            if (l.x >= 272 && l.x <= 343) t = BlockType.Orders;
            if (l.x >= 343 && l.x <= 414) t = BlockType.Freight;
            if (l.x >= 414 && l.x <= 486) t = BlockType.Routes;
            return t;
        }

        protected override bool ChromeClick(Block b, Vector2Int l, InputState input)
        {
            int lt = LeftTabAt(l), rt = RightTabAt(b, l);
            if (rt != BlockType.None)
            {
                // Tutorial: only the orders tab, and only from step 1504 (5b §3).
                if (rt != b.Right.Type && !Tutorial.TutorialView.RightTabAllowed(rt)) return true;
                b.SwitchType(b.Right, rt);
                UpdatePages(b.Right, b);
                PaperSound();
                Tutorial.TutorialView.RightTabOpened(rt);
                return true;
            }
            if (lt != BlockType.None)
            {
                if (lt != b.Left.Type && !Tutorial.TutorialView.LeftTabAllowed) return true;   // tutorial: only at step 1502
                b.SwitchType(b.Left, lt);
                if (lt != BlockType.Planes) b.Right.Type = BlockType.None;
                else if (b.Right.Type == BlockType.None) b.SwitchType(b.Right, BlockType.Orders);
                UpdatePages(b.Left, b);
                UpdatePages(b.Right, b);
                PaperSound();
                if (Tutorial.TutorialView.At(Sim.Tutorial.TutorialStep.OpenPlanes)) Tutorial.TutorialView.LeftTabOpened(lt);
                return true;
            }
            if (l.x >= 20 && l.x <= 481 && l.y >= 0 && l.y <= 49) { BeginMove(b, l); return true; }
            return false;
        }

        protected override void ChromeHover(Block b, Vector2Int l)
        {
            int lt = LeftTabAt(l), rt = RightTabAt(b, l);
            if (rt != BlockType.None) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(rt == BlockType.Orders ? 3003 : rt == BlockType.Freight ? 3008 : 3004); }
            else if (lt != BlockType.None) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(lt == BlockType.Planes ? 3002 : lt == BlockType.Cities ? 3001 : 3005); }
        }

        static int LeftTabIndex(int t) => t == BlockType.Planes ? 1 : t == BlockType.Cities ? 2 : t == BlockType.Info ? 3 : 0;
        static int RightTabIndex(int t) => t == BlockType.Orders ? 1 : t == BlockType.Freight ? 2 : t == BlockType.Routes ? 3 : 0;

        protected override void DrawBlockChrome(Screen2D s, Block b, int x, int y, Vector2Int l)
        {
            s.Draw(Assets.Texture(Kit0, "FILOFAX"), x, y);
            s.Draw(Assets.Texture(Kit0, "INDEX" + LeftTabIndex(b.Left.Type)), x + 36, y + 256);
            int ri = RightTabIndex(b.Right.Type);
            if (b.Left.Type == BlockType.Planes && ri > 0) s.Draw(Assets.Texture(Kit0, "INDEXR_" + ri), x + 268, y + 256);
        }

        protected override void DrawBlockOverlay(Screen2D s, Block b, int x, int y, Vector2Int l)
        {
            for (int side = 0; side < 2; side++)
            {
                var pane = b.PaneOf(side == 1);
                if (pane.Type == BlockType.None) continue;
                int dx = side * 232;
                if (!pane.IsIndex) s.Draw(Assets.Texture(Kit0, "INHALT"), x + dx, y + 78);
                if (pane.Page > 0) s.Draw(Assets.Texture(Kit0, "BACK"), x + 40 + dx, y + 237);
                if (pane.Page < pane.Pages - 1) s.Draw(Assets.Texture(Kit0, "NEXT"), x + 208 + dx, y + 233);
                if (side == 1 && pane.IsDetail(BlockType.Routes)) s.Draw(Assets.Texture(Kit0, "LOCK"), x + 441, y + 55);
            }
        }

        // ------------------------------------------------------------------ outside the filofax

        bool OverCorner(Vector2Int c)
        {
            var t = Assets.Texture(Kit0, "FILOECKE");
            return !State.FilofaxVisible && c.x >= 0 && c.x < t.width && c.y > 440 - t.height && c.y < 440;
        }

        protected override bool WorldLeftDown(Vector2Int c, InputState input)
        {
            if (OverCorner(c))
            {
                State.FilofaxVisible = true;
                App.Audio.Play("SOUND/paper.raw");
                return true;
            }
            if (InRect(c, 353, 397, 393, 417) || InRect(c, 247, 396, 287, 416)) return true;
            return base.WorldLeftDown(c, input);
        }

        static bool InRect(Vector2Int c, int x1, int y1, int x2, int y2) => c.x >= x1 && c.x <= x2 && c.y >= y1 && c.y <= y2;

        protected override void WorldHover(Vector2Int c, InputState input)
        {
            base.WorldHover(c, input);
            if (OverCorner(c)) App.Ui.Cursor = CursorLook.Hot;
            bool held = input.LeftHeld;
            if (InRect(c, 353, 397, 393, 417)) { App.Ui.Cursor = CursorLook.Hot; if (held) TurnDir = -1; }
            else if (InRect(c, 247, 396, 287, 416)) { App.Ui.Cursor = CursorLook.Hot; if (held) TurnDir = 1; }
        }

        protected override void Hotspots(InputState input)
        {
            if (!Carry.Empty || HitBlock(input.Mouse) != null) return;
            Exit(495, 241, 640, 440);
            Exit(540, 0, 640, 320);
        }

        public override void Animate(float dt)
        {
            base.Animate(dt);
            if (TurnDir != 0 && !_squeak) { _squeak = true; if (P != null && !P.GlobeOiled) App.Audio.Play("SOUND/Quietsch.raw"); }
            if (TurnDir == 0) _squeak = false;
        }
        bool _squeak;

        // ------------------------------------------------------------------ drawing

        protected override void DrawRoom(Screen2D s)
        {
            if (W == null || State == null) return;
            var f = State.Filofax;
            var sel = new List<Plane>();
            if (State.FilofaxVisible && f != null && SelectedPlane(f) is Plane pl) sel.Add(pl);
            PrepareGlobe(sel, false);
            DrawGlobeWithPlanes(s, Kit0);
            // City marker of an open city detail.
            if (State.FilofaxVisible && f != null && f.Left.IsDetail(BlockType.Cities))
            {
                var city = W.City(f.Left.SelectedId);
                if (city != null && Globe.Project(city.GlobeX, city.GlobeY, out var o))
                    s.Draw(Assets.Texture(Std2, "CITYCUR"), RX + GlobeOrigin.x + GlobeView.CentreOffset.x + (int)o.x - 18, GlobeOrigin.y + GlobeView.CentreOffset.y + (int)o.y - 18);
            }
            var m = App.InputNow.Mouse;
            var c = new Vector2Int(m.x - RX, m.y);
            bool overBlock = HitBlock(m) != null;
            if (!overBlock && InRect(c, 353, 397, 393, 417)) s.Draw(Assets.Texture(Std2, "ID_01"), RX + 353, 397);
            if (!overBlock && InRect(c, 247, 396, 287, 416)) s.Draw(Assets.Texture(Std2, "IE_01"), RX + 247, 396);
            if (State.FilofaxVisible) DrawBlocks(s);
            else
            {
                var t = Assets.Texture(Kit0, "FILOECKE");
                s.Draw(t, RX, 440 - t.height);
            }
        }

        protected override void DrawOverlay(Screen2D s)
        {
            // Power cut in the office (§1.6): darken the whole view.
            if (P != null && P.OfficeState == 3) s.FillRect(new RectInt(0, 0, s.Width, 440), new Color32(0, 0, 20, 150));
            DrawCarry(s);
        }
    }
}
