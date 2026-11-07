using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Kiosk
{
    /// <summary>
    /// The newspaper kiosk (rooms-and-ui-7 §8): three papers composed from <see cref="World.Newspapers"/>
    /// that zoom up while hovered ("reading"); the vendor reads when today's papers are interesting
    /// (player headlines) and sleeps otherwise; the stink bomb on the counter.
    /// </summary>
    [Room(RoomIds.Kiosk)]
    [ScreenName("kiosk")]
    public sealed class KioskRoom : RoomScreen
    {
        const string Lib = "room/kiosk.gli";
        protected override string Library => Lib;
        protected override string Background => "KIOSK";
        protected override int DefaultDialogPartner => Awake ? Talkers.Kiosk : -1;

        const int ClickMan = 10, ClickStink = 20;

        static readonly RectInt[] PaperHot = { R(419, 239, 512, 403), R(494, 169, 555, 325), R(426, 98, 516, 247) };
        static readonly Vector2Int[] ZoomCentre = { new Vector2Int(183, 236), new Vector2Int(349, 246), new Vector2Int(140, 185) };
        static RectInt R(int x1, int y1, int x2, int y2) => new RectInt(x1, y1, x2 - x1, y2 - y1);

        readonly float[] _zoom = new float[3];
        SmackerPerson _man;

        bool Awake => App.Session?.World.Newspapers.IsInteresting ?? false;

        protected override void OnCreate()
        {
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "K_LeseR.smk", 120, 205, Mood.Idle, "A1:1", waitMin: 40, waitMax: 80);
            _man.Clip(1, "K_Seite.smk", 120, 205, Mood.Idle, "A1:2", canCancel: false);
            _man.Clip(2, "K_LeseL.smk", 120, 205, Mood.Idle, "A1:3", waitMin: 40, waitMax: 80);
            _man.Clip(3, "K_Turn.smk", 120, 205, Mood.Idle, "A3:0 A1:4", canCancel: false, sound: "k_turn.raw");
            _man.Clip(4, "K_Seite.smk", 120, 205, Mood.Idle, "A1:3", canCancel: false);
            _man.SetDesired(Mood.None);
        }

        protected override void OnRoomEnter()
        {
            if (!Awake) PlayAmbientLoop("Schnarch.raw");   // the sleeping man snores continuously (original loop)
        }

        public override void Animate(float dt)
        {
            if (Awake) _man.Update(dt);
            // Reading: every paper loses 10 per frame, the hovered one gains 20 (net +10), at 20 fps.
            var m = App.InputNow.Mouse;
            int x = m.x - RX, y = m.y;
            float step = dt * 20f * 10f;
            // Only one paper reads at a time, checked in the original's order (middle, left, top); nothing
            // zooms over the stink bomb or while a dialog or menu is open.
            int hovered = -1;
            bool overStink = !HasStinkBomb && x >= 158 && x <= 209 && y >= 397 && y <= 439;
            if (!IsDialogOpen && !Menus.IsOpen && !overStink)
                foreach (int i in HoverOrder)
                    if (x >= PaperHot[i].xMin && x <= PaperHot[i].xMax && y >= PaperHot[i].yMin && y <= PaperHot[i].yMax) { hovered = i; break; }
            for (int i = 0; i < 3; i++)
                _zoom[i] = Mathf.Clamp(_zoom[i] + (i == hovered ? step : -step), 0, 100);
        }

        bool HasStinkBomb => App.Session?.Local.HasItem(ItemId.StinkBomb) ?? false;

        protected override void Hotspots(InputState input)
        {
            Exit(560, 0, 639, 439);
            if (Awake) Hot(167, 179, 339, 429, ClickMan);
            if (!HasStinkBomb) Hot(158, 397, 209, 439, ClickStink);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMan) StartDialog(Talkers.Kiosk);
            else if (id == ClickStink) StartDialog(Talkers.Kiosk, DialogMedium.Air, 1000);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            if (!Awake) s.Draw(Assets.Texture(Lib, "SLEEPER"), ox + 180, 170);
            else _man.Draw(s, ox);
            if (!HasStinkBomb) s.Draw(Assets.Texture(Lib, "STINK"), ox + 158, 397);
        }

        static readonly int[] HoverOrder = { 1, 0, 2 };

        protected override void DrawOverlay(Screen2D s)
        {
            var w = App.Session?.World;
            if (w == null || IsDialogOpen) return;   // the papers are not drawn during a dialog
            for (int i = 0; i < 3; i++)
            {
                if (_zoom[i] <= 0) continue;
                var paper = Paper(i, w);
                float k = _zoom[i] / 100f;
                float pw = paper.width * k, ph = paper.height * k;
                // QUIRK OQ-8 reproduced (verified on the running original): the top uses half the width, so
                // the papers sit lower than centred.
                var c = ZoomCentre[i];
                float x = RX + c.x - pw / 2, y = c.y - pw / 2;
                // The whole composed page scales while zooming (the original blits the finished paper).
                if (_zoom[i] < 100) { s.SetTransform(x, y, k); NewspaperPage.Draw(s, App, w, i, 0, 0); s.ResetTransform(); }
                else NewspaperPage.Draw(s, App, w, i, Mathf.RoundToInt(x), Mathf.RoundToInt(y));
            }
        }

        Texture Paper(int i, World w) =>
            Assets.Texture(Lib, i == 0 ? (NewspaperPage.HasPicture(w, 0) ? "PAPER1" : "PAPER1A") : i == 1 ? "PAPER2" : "PAPER3");
    }

    /// <summary>Composes one newspaper page (rooms-and-ui-7 §8 "Newspapers", Kiosk.cpp RepaintTip).</summary>
    public static class NewspaperPage
    {
        const string Lib = "room/kiosk.gli";
        static readonly Vector2Int[] TextStart = { new Vector2Int(8, 55), new Vector2Int(3, 34), new Vector2Int(14, 44) };
        static readonly Vector2Int[] TextMax = { new Vector2Int(165, 242), new Vector2Int(194, 290), new Vector2Int(170, 240) };
        static readonly string[] Inline = { "FEUER", "REIFEN", "SALZ", "SKELETT", "SUPERMAN" };
        static readonly string[] Located = { "PASSAG", "PILOT", "STEWARD", "TRASHP" };
        static readonly string[] PlayerPics = { "1", "2", "3", "4", "LIEBSTE", "PLANES", "GELD", "ROUTEN" };

        public static bool HasPicture(World w, int paper)
        {
            for (int d = 0; d < Newspapers.Slots; d++)
            {
                var h = w.Newspapers.Get(paper, d);
                if (!h.IsEmpty && !string.IsNullOrEmpty(h.Picture)) return true;
            }
            return false;
        }

        public static string HeadlineText(Boot.GameApp app, Headline h)
        {
            if (h.TextId != 0) return DialogText.Format(app.Assets.StdText.Get("Misc", h.TextId) ?? "", h.Args);
            return h.Text ?? "";
        }

        public static void Draw(Screen2D s, Boot.GameApp app, World w, int paper, int x, int y)
        {
            // Composition order (rooms-and-ui-7 §8, verified against the running original): template; a
            // located photo (passengers, pilot, stewardess, rubbish) is placed first as the page background;
            // then every headline, each followed - for the first picture only - by its inline picture;
            // finally the decorations that fill the rest of the page. Offsets are relative to the text
            // start of the paper, the photo is aligned to the template's bottom right.
            var a = app.Assets;
            var f = app.Ui.Kit.SmallBlack;
            bool pictures = HasPicture(w, paper);
            var tpl = a.Texture(Lib, paper == 0 ? (pictures ? "PAPER1" : "PAPER1A") : paper == 1 ? "PAPER2" : "PAPER3");
            s.Draw(tpl, x, y);
            int px = TextStart[paper].x, py = TextStart[paper].y + (paper == 0 && !pictures ? 85 : 0);
            int maxX = TextMax[paper].x, maxY = TextMax[paper].y;
            bool picDone = false;

            for (int d = 0; d < Newspapers.Slots && !picDone; d++)
            {
                var h = w.Newspapers.Get(paper, d);
                if (h.IsEmpty || string.IsNullOrEmpty(h.Picture)) continue;
                string located = LocatedName(h);
                if (located == null) continue;
                picDone = true;
                s.Draw(a.Texture(Lib, "PAPER3A"), x, y);
                var photo = a.Texture("gli/kioskloc.gli", located);
                if (photo != null) s.Draw(photo, x + tpl.width - photo.width - 3, y + tpl.height - photo.height);
            }

            for (int d = 0; d < Newspapers.Slots; d++)
            {
                var h = w.Newspapers.Get(paper, d);
                if (h.IsEmpty) continue;
                string text = HeadlineText(app, h);
                if (!string.IsNullOrEmpty(text))
                    py += f.DrawBlock(s, new RectInt(x + px, y + py, maxX - px, Mathf.Max(0, maxY - py)), text, Color.white) + 5;
                if (picDone || string.IsNullOrEmpty(h.Picture)) continue;
                picDone = true;
                py = DrawInlinePicture(s, a, h, paper, x, y, px, py);
            }

            if (picDone && paper == 0)
            {
                var deco = a.Texture(Lib, "PAPER1D"); var end = a.Texture(Lib, "PAPER1E");
                if (py < tpl.height - deco.height - end.height - 10) { s.Draw(deco, x, y + py); py += deco.height + 5; }
                if (py - 5 < tpl.height - end.height) s.Draw(end, x, y + tpl.height - end.height);
            }
            if (paper == 1)
            {
                foreach (var n in new[] { "PAPER2A", "PAPER2D", "PAPER2C", "PAPER2B" })
                {
                    var t = a.Texture(Lib, n);
                    if (py < tpl.height - t.height - 10) { s.Draw(t, x, y + py); py += t.height + 5; }
                }
                s.Draw(a.Texture(Lib, "WET0" + (w.Clock.Date % 6 + 1)), x + 134, y + 4);
            }
        }

        /// <summary>The photo name for a located picture, or null if it is not one.</summary>
        static string LocatedName(Headline h)
        {
            foreach (var l in Located)
                if (h.Picture.StartsWith(l)) return l == "TRASHP" ? "TRASHP" + ((h.PictureOffset >> 48) & 3) : h.Picture;
            return null;
        }

        /// <summary>The inline picture after its headline; returns the new text y (paper-relative).</summary>
        static int DrawInlinePicture(Screen2D s, Boot.GameAssets a, Headline h, int paper, int x, int y, int px, int py)
        {
            string pic = h.Picture;
            if (LocatedName(h) != null) return py;   // already the page background
            foreach (var n in Inline)
                if (pic == n)
                {
                    var t = a.Texture("gli/kiosks.gli", n);
                    if (paper == 0)
                    {
                        s.Draw(t, x + px + 37, y + py);
                        var b = a.Texture(Lib, "PAPER1B");
                        s.Draw(b, x, y + py);
                        s.Draw(a.Texture(Lib, "PAPER1C"), x, y + py + b.height + 3);
                    }
                    else s.Draw(t, x + px + 10, y + py);
                    return py + t.height + 5;
                }
            if (pic == "INTRVIEW" || pic == "STREIK")
            {
                var t = a.Texture(Lib, pic);
                s.Draw(t, x + px + 10, y + py);
                return py + t.height + 5;
            }
            foreach (var n in PlayerPics)
                if (pic == n)
                {
                    int player = (int)(h.PictureOffset / 100) & 3;
                    s.Draw(a.Texture(Lib, "PAPER2A"), x + px - 3, y + py);
                    var t = a.Texture("gli/kioskp" + (player + 1) + ".gli", n);
                    s.Draw(t, x + px + 47, y + py);
                    return py + t.height + 5;
                }
            return py;
        }
    }

    /// <summary>The kiosk vendor (rooms-and-ui-3b §14): small talk and the stink-bomb puzzle.</summary>
    [DialogScript(Talkers.Kiosk)]
    public sealed class KioskDialog : DialogTree
    {
        public KioskDialog() : base("Kios")
        {
            Open(d =>
            {
                if (d.Par1 == 1000) d.Say(d.Player.KioskTrust ? 1021 : 1020);
                else if (d.Par1 == 1010) d.Say(1010);
                else if (d.Par1 >= 10000) d.Say(d.Par1);   // a line chosen by the caller (as the original)
                else d.Say(1000);
            });
            // 1021: the stink bomb (the reward clears KioskTrust); the energy drink (opening 1010) is the items domain's.
            On(1021, d => { d.World.Execute(new ReceiveRewardCommand(d.Player.Index, ItemId.StinkBomb)); d.End(); });
        }
    }
}
