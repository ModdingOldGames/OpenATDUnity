using UnityEngine;

namespace OpenAT.View.Render
{
    /// <summary>
    /// Fills the side areas left and right of a centred 640-wide original picture on wide screens.
    /// Default style stretches the picture's outermost columns and darkens them, so the picture seems
    /// to continue softly instead of ending in black bars.
    /// </summary>
    public static class WideFill
    {
        public enum Style { Black, StretchedEdge }

        public static Style Current = Style.StretchedEdge;

        /// <summary>Draws <paramref name="tex"/> centred at y and fills the sides.</summary>
        public static void DrawCentered(Screen2D s, Texture tex, int y = 0)
        {
            int x = (s.Width - tex.width) / 2;
            Sides(s, tex, x, y);
            s.Draw(tex, x, y);
        }

        public static void Sides(Screen2D s, Texture tex, int x, int y)
        {
            if (x <= 0 || Current == Style.Black) return;
            // Mirror the outer parts of the picture into the side areas, darkened.
            var tint = new Color32(85, 85, 85, 255);
            int left = x, right = s.Width - x - tex.width;
            int lw = Mathf.Min(left, tex.width), rw = Mathf.Min(right, tex.width);
            s.DrawScaled(tex, new Rect(x - lw, y, lw, tex.height), new Rect(0, 0, lw, tex.height), tint, flipX: true);
            s.DrawScaled(tex, new Rect(x + tex.width, y, rw, tex.height), new Rect(tex.width - rw, 0, rw, tex.height), tint, flipX: true);
            // Soft shadow at the picture edges.
            for (int i = 0; i < 8; i++)
            {
                byte a = (byte)(120 - i * 15);
                s.FillRect(new RectInt(x - 1 - i, y, 1, tex.height), new Color32(0, 0, 0, a));
                s.FillRect(new RectInt(x + tex.width + i, y, 1, tex.height), new Color32(0, 0, 0, a));
            }
        }
    }
}
