using OpenAT.Sim;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>The fonts a planner block is drawn with.</summary>
    public sealed class PlannerFonts
    {
        public IFont Black, Red, Grey, Dark, Title;
    }

    /// <summary>
    /// OpenAT high-resolution planner (<see cref="GameOptions.PlannerHiResFilofax"/> for the filofax,
    /// <see cref="GameOptions.PlannerHiResLaptop"/> for laptop and docked windows): a block keeps its size and
    /// place, but is drawn with the window's real pixels. Its artwork is a smoothed upscale and its text the
    /// original's fonts slightly smaller, so the lists hold more rows per page. Layout and input otherwise stay
    /// in the original's coordinates.
    /// </summary>
    public abstract partial class PlannerRoom
    {
        const int FineRow = 18, FineCityRow = 9, FinePage = 9, FineCityPage = 18;

        /// <summary>Size of the high-resolution text against the original fonts (8 of the 9 pixel lines).</summary>
        public const float FineScale = 8f / 9f;

        PlannerFonts _std, _fine, _fineLap, _fonts;

        /// <summary>The block is drawn in high resolution.</summary>
        protected bool Fine(Block b)
        {
            if (b == null || !Enhanced) return false;
            return b.Style == Block.StyleFilofax ? !IsLaptop && W.Options.PlannerHiResFilofax : W.Options.PlannerHiResLaptop;
        }

        /// <summary>Fonts of the block being drawn (the original bitmap fonts outside a high-resolution block).</summary>
        protected PlannerFonts Fs => _fonts ?? StdFonts;

        PlannerFonts StdFonts => _std ?? (_std = new PlannerFonts
        {
            Black = Kit.SmallBlack, Red = Kit.SmallRed, Grey = Kit.SmallGrey,
            Dark = (IFont)Kit.NormalGrey ?? Kit.SmallGrey, Title = Kit.SmallBlack,
        });

        /// <summary>The original's fonts, slightly smaller and drawn with the window's real pixels.</summary>
        public static PlannerFonts FineFonts(UiKit kit, bool laptop)
        {
            IFont Sc(BitmapFont f, float k) => f == null ? null : new ScaledBitmapFont(f, k);
            return new PlannerFonts
            {
                Black = Sc(kit.SmallBlack, FineScale), Red = Sc(kit.SmallRed, FineScale), Grey = Sc(kit.SmallGrey, FineScale),
                Dark = Sc(kit.NormalGrey ?? kit.SmallGrey, FineScale),
                Title = Sc(laptop ? kit.NormalGreen ?? kit.SmallBlack : kit.SmallBlack, 1f),
            };
        }

        /// <summary>Starts drawing a block: high-resolution fonts and smoothed artwork for a fine block.</summary>
        protected void BeginBlockLook(Screen2D s, Block b)
        {
            bool fine = Fine(b);
            _fonts = !fine ? StdFonts : Lap(b) ? _fineLap ?? (_fineLap = FineFonts(Kit, true)) : _fine ?? (_fine = FineFonts(Kit, false));
            s.SmoothArt = fine;
        }

        protected void EndBlockLook(Screen2D s)
        {
            _fonts = null;
            s.SmoothArt = false;
        }

        /// <summary>The pane belongs to a high-resolution block.</summary>
        bool FinePane(Pane pane)
        {
            var st = State;
            if (st == null) return false;
            bool Of(Block b) => b != null && (b.Left == pane || b.Right == pane);
            if (Of(st.Filofax)) return Fine(st.Filofax);
            for (int i = 0; i < st.Windows.Count; i++) if (Of(st.Windows[i])) return Fine(st.Windows[i]);
            for (int i = 0; i < st.Docked.Count; i++) if (Of(st.Docked[i])) return Fine(st.Docked[i]);
            return false;
        }

        /// <summary>Rows per list page of a pane (more in a high-resolution block).</summary>
        protected int PageSize(Pane pane)
        {
            if (!FinePane(pane)) return BlockType.ListPageSize(pane.Type);
            return pane.Type == BlockType.Cities || pane.Type == BlockType.Info ? FineCityPage : FinePage;
        }

        /// <summary>Row height of a pane's list.</summary>
        protected int RowH(Pane pane)
        {
            if (!FinePane(pane)) return BlockType.RowHeight(pane.Type);
            return pane.Type == BlockType.Cities || pane.Type == BlockType.Info ? FineCityRow : FineRow;
        }

        /// <summary>Draws an icon at <paramref name="scale"/> of its size (high-resolution rows use smaller icons).</summary>
        protected static void DrawIcon(Screen2D s, Texture2D t, float x, float y, float scale)
        {
            if (scale == 1f) { s.Draw(t, (int)x, (int)y); return; }
            s.DrawScaled(t, new Rect(x, y, t.width * scale, t.height * scale), new Rect(0, 0, t.width, t.height), Color.white);
        }
    }
}
