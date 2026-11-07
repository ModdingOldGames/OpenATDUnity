using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI.Dialog
{
    /// <summary>
    /// Speech-bubble frame painter (rooms-and-ui-1 §4.10) from glstd.gli XBLASE01..14: tall bubbles
    /// (≥ 68 px) use the big corner pieces, small ones the small pieces; the left/right pieces carry the
    /// white interior; the tail is chosen by the edge the entry point lies on and centred on it.
    /// </summary>
    public static class SpeechBubble
    {
        static Texture2D P(UiKit kit, int i) => kit.WhiteTrans(UiKit.StdLib, "XBLASE" + i.ToString("00"));

        /// <summary>Draws the frame for rect (x1,y1)-(x2,y2) (screen) with a tail at <paramref name="entry"/>.</summary>
        public static void Draw(Screen2D s, UiKit kit, int x1, int y1, int x2, int y2, Vector2Int? entry)
        {
            int w = x2 - x1, h = y2 - y1;
            if (w <= 0 || h <= 0) return;
            bool tall = h >= 68;
            var tl = P(kit, tall ? 1 : 7); var tr = P(kit, tall ? 2 : 8);
            var bl = P(kit, tall ? 5 : 9); var br = P(kit, tall ? 6 : 10);
            var el = P(kit, 3); var er = P(kit, 4);
            int capT = tl.height, capB = bl.height;
            if (capT + capB > h) { capT = h / 2; capB = h - capT; }

            int lw = Mathf.Min(tl.width, w / 2);
            int rw = Mathf.Min(tr.width, w - lw);
            int gap = w - lw - rw; // only for very wide bubbles
            void Row(Texture2D l, Texture2D r, int y, int srcY, int rows)
            {
                if (rows <= 0) return;
                s.Draw(l, x1, y, new RectInt(0, srcY, lw, rows), Color.white);
                if (gap > 0) s.DrawScaled(l, new Rect(x1 + lw, y, gap, rows), new Rect(l.width - 40, srcY, 30, rows), Color.white);
                s.Draw(r, x2 - rw, y, new RectInt(r.width - rw, srcY, rw, rows), Color.white);
            }
            Row(tl, tr, y1, 0, capT);
            int y = y1 + capT, end = y2 - capB;
            // A zero-height middle piece (missing bitmap) would never advance: fill the rest in one band then.
            int band = el.height > 0 ? el.height : end - y;
            while (y < end)
            {
                int rows = Mathf.Min(band, end - y);
                Row(el, er, y, 0, rows);
                y += rows;
            }
            Row(bl, br, y2 - capB, bl.height - capB, capB);

            if (!entry.HasValue) return;
            var e = entry.Value;
            if (e.y >= y2 - 1) { var t = P(kit, 11); s.Draw(t, e.x - t.width / 2, y2 - 2); }
            else if (e.y <= y1 + 1) { var t = P(kit, 12); s.Draw(t, e.x - t.width / 2, y1 - t.height + 2); }
            else if (e.x <= x1) { var t = P(kit, 13); s.Draw(t, x1 - t.width + 2, e.y - t.height / 2); }
            else if (e.x >= x2) { var t = P(kit, 14); s.Draw(t, x2 - 2, e.y - t.height / 2); }
        }
    }

    /// <summary>
    /// The number card (rooms-and-ui-1 §4.9): glnumber.gli NUMBERS stretched to fit the text, centred on
    /// x=320 of the room canvas with its bottom at y=439.
    /// </summary>
    public sealed class NumberWindow
    {
        public string Text;
        int _w, _h;

        public void Set(UiKit kit, string text)
        {
            Text = string.IsNullOrEmpty(text) ? null : text.Replace(' ', ' ');
            if (Text == null) return;
            var f = kit.DialogPartner;
            string measure = text;
            int w = 60, h = 40;
            for (; w <= 630; w += 10)
            {
                h = w * 2 / 3;
                if (f.DrawBlock(null, new RectInt(0, 0, w, 2000), measure, Color.white, TextAlign.Left, true) <= h) break;
            }
            int need = f.DrawBlock(null, new RectInt(0, 0, w, 2000), measure, Color.white, TextAlign.Left, true);
            while (h - 10 >= need) h -= 10;
            _w = w + 20; _h = h + 20;
        }

        public void Draw(Screen2D s, UiKit kit, int canvasX)
        {
            if (Text == null) return;
            var tpl = kit.Assets.Texture("gli/glnumber.gli", "NUMBERS");
            int x = canvasX + 320 - _w / 2, y = 439 - _h;
            s.DrawScaled(tpl, new Rect(x, y, _w, _h), new Rect(0, 0, tpl.width, tpl.height), Color.white);
            var f = kit.DialogPartner;
            string t = Text.Replace(' ', ' ');
            int th = f.DrawBlock(null, new RectInt(0, 0, _w - 20, 2000), t, Color.white, TextAlign.Left, true);
            f.DrawBlock(s, new RectInt(x + 10, y + (_h - th) / 2, _w - 20, th + 2), t, Color.white);
        }
    }
}
