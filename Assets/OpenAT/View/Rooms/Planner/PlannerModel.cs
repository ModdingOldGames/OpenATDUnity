using System.Collections.Generic;
using OpenAT.Sim;
using UnityEngine;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>Block (window) content types (rooms-and-ui-5b §1.1).</summary>
    public static class BlockType
    {
        public const int None = 0, Cities = 1, Planes = 2, Orders = 3, Routes = 4, Info = 5, Freight = 6;

        /// <summary>Rows per list page: 6 rows of 26 px or 13 rows of 13 px.</summary>
        public static int ListPageSize(int t) => t == Cities || t == Info ? 13 : 6;
        public static int RowHeight(int t) => t == Cities || t == Info ? 13 : 26;
    }

    /// <summary>One side of a block: content type, index (list) or detail mode, selection and page.</summary>
    public sealed class Pane
    {
        public int Type;
        public bool IsIndex = true;
        public int SelectedId = -1;
        /// <summary>Page number (0-based) of the list or detail.</summary>
        public int Page;
        public int Pages = 1;

        public void CopyFrom(Pane o) { Type = o.Type; IsIndex = o.IsIndex; SelectedId = o.SelectedId; Page = o.Page; Pages = o.Pages; }
        public bool IsDetail(int type) => Type == type && !IsIndex;
    }

    /// <summary>
    /// A planner window ("BLOCK", rooms-and-ui-5b §1): style 0 = the globe-room filofax (double page,
    /// tabs), style 1 = a laptop window (single for Info, double otherwise). Saved with the player.
    /// </summary>
    public sealed class Block
    {
        public int Style;
        public Vector2Int Pos;
        public bool Double = true;
        public readonly Pane Left = new Pane(), Right = new Pane();
        /// <summary>Per-type memory used when the panes switch type (filofax tabs).</summary>
        public readonly Dictionary<int, Pane> Memory = new Dictionary<int, Pane>();
        /// <summary>Laptop open/close animation: start time (unscaled seconds), closing flag.</summary>
        public float AnimStart = -100;
        public bool Closing;
        /// <summary>Type of the window as opened by its laptop icon.</summary>
        public int WindowType;

        public Vector2Int Size => Style == 0 ? new Vector2Int(482, 311) : Double ? new Vector2Int(396, 210) : new Vector2Int(199, 210);

        public Pane PaneOf(bool right) => right ? Right : Left;

        /// <summary>Saves the pane's view into its type slot and restores the slot of <paramref name="newType"/> (§3).</summary>
        public void SwitchType(Pane pane, int newType)
        {
            if (pane.Type == newType) return;
            if (pane.Type != BlockType.None)
            {
                var mem = new Pane(); mem.CopyFrom(pane);
                Memory[pane.Type * 2 + (pane == Right ? 1 : 0)] = mem;
            }
            if (Memory.TryGetValue(newType * 2 + (pane == Right ? 1 : 0), out var m)) pane.CopyFrom(m);
            else { pane.Type = newType; pane.IsIndex = true; pane.SelectedId = -1; pane.Page = 0; pane.Pages = 1; }
            pane.Type = newType;
        }
    }

    /// <summary>Per-player planner state that survives leaving the room (rooms-and-ui-5 §2.4, §3.5, §4.3).</summary>
    public sealed class PlannerState
    {
        const string Key = "openat.planner";

        /// <summary>Globe rotation shared by the globe room and the laptop ("EarthAlpha").</summary>
        public int EarthAlpha = 8000;
        /// <summary>False until the globe was first turned towards the home airport.</summary>
        public bool AlphaInitialised;
        public bool FilofaxVisible = true;
        public Block Filofax;
        /// <summary>Laptop windows, front first (slots 1..19 of the original).</summary>
        public readonly List<Block> Windows = new List<Block>();
        /// <summary>Remembered laptop window positions per type ("GlobeWindows").</summary>
        public readonly Dictionary<int, Vector2Int> WindowPos = new Dictionary<int, Vector2Int>
        {
            { BlockType.Cities, new Vector2Int(123, 88) }, { BlockType.Planes, new Vector2Int(144, 16) }, { BlockType.Info, new Vector2Int(115, 164) },
        };
        public readonly bool[] DisplayPlanes = { true, false, false, false };
        public readonly int[] DisplayRoutes = new int[4];

        public static PlannerState Of(Player p)
        {
            if (p == null) return new PlannerState();
            if (p.Extensions.TryGetValue(Key, out var v) && v is PlannerState s) return s;
            s = new PlannerState();
            p.Extensions[Key] = s;
            return s;
        }

        public Block EnsureFilofax()
        {
            if (Filofax != null) return Filofax;
            Filofax = new Block { Style = 0, Pos = new Vector2Int(80, 100), Double = true };
            Filofax.Left.Type = BlockType.Planes;
            Filofax.Right.Type = BlockType.Orders;
            return Filofax;
        }
    }

    /// <summary>The object on the cursor while planning (rooms-and-ui-5b §6): route 1, order 2, freight 4.</summary>
    public struct Carry
    {
        public int Kind, Id;
        public bool Empty => Kind == 0;
        public static Carry None => new Carry();
    }
}
