using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Hall
{
    /// <summary>A placed brick (airport-and-people.md §2.2). X,Y = top-left of the bitmap in hall pixels.</summary>
    public struct Build
    {
        public BrickDef Brick;
        public int X, Y;
        /// <summary>Placement parameter (room, gate, door/trigger index after loading).</summary>
        public int Par;
        /// <summary>Index of the segment (0..9) the build came from.</summary>
        public int Segment;
        public int BrickId => Brick.Id;
        /// <summary>Depth key = Layer·65536 + bottom + FloorOffset (§3.5).</summary>
        public int SortKey => (Brick.Layer << 16) + Y + Brick.Height + Brick.FloorOffset;
    }

    /// <summary>A rune in the query list: position converted to logical walk space (§3.3) except cond blocks.</summary>
    public struct RuneInfo
    {
        public int Id;
        public int Par;
        /// <summary>Logical position (lower floor y 0.., upper floor y 5000..). Raw build position for cond blocks.</summary>
        public Pt Pos;
        /// <summary>Raw build position (screen space).</summary>
        public Pt Raw;
        public BrickDef Brick;
    }

    public struct AreaMarker { public int Par; public Pt P1, P2; }
    public struct ClipMarker { public int Type; public int X; }

    public sealed class DoorInfo
    {
        /// <summary>Tile of the door (col, row 0..15).</summary>
        public Pt Tile;
        /// <summary>0 normal, 1 sabotage rotating wall, 2 cargo door, 3 security door.</summary>
        public int Kind;
        public int BrickId;
    }

    /// <summary>
    /// The static airport: merged, depth-sorted builds of the ten segments, the initial walk grid and
    /// the door/trigger/rune/area tables (airport-and-people.md §2.3, §4). Immutable after construction;
    /// the dynamic copy of the grid lives in <see cref="HallState"/>.
    /// </summary>
    public sealed class HallLayout
    {
        public readonly HallData Data;
        public readonly int Difficulty;
        public readonly int[] Levels;
        /// <summary>All builds in draw order (runes included; the view skips ids ≥ 2000).</summary>
        public readonly List<Build> Builds = new List<Build>();
        public readonly int[] Dilatation = new int[10];
        public int LeftEnd = -99999, RightEnd = 99999;
        /// <summary>Columns in the grid (excluding the 20 spare columns in front).</summary>
        public int Columns;
        public byte[] InitialPlates;
        /// <summary>Seat masks per column (bit set = no free seat), §4.1.</summary>
        public ushort[] InitialSeats;
        public readonly List<DoorInfo> Doors = new List<DoorInfo>();
        /// <summary>One-shot trigger bricks (luggage scanner, cola machine): raw positions.</summary>
        public readonly List<Pt> Triggers = new List<Pt>();
        public readonly List<ClipMarker> ClipMarkers = new List<ClipMarker>();
        public readonly List<AreaMarker> Areas = new List<AreaMarker>();
        public readonly List<RuneInfo> Runes = new List<RuneInfo>();
        public int NumBeltSpots;
        public int MinX, MaxX;

        public HallLayout(HallData data, int difficulty, int[] levels)
        {
            Data = data;
            Difficulty = difficulty;
            Levels = (int[])levels.Clone();
            Assemble();
            CalcPlates();
            CalcCoordinates();
        }

        /// <summary>Standard layout: all segments at level 1 (§2.2 "Levels").</summary>
        public static int[] DefaultLevels() => new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

        // ------------------------------------------------------------------ assembly (§2.3)

        void Assemble()
        {
            var lists = new List<Build>[10];
            for (int c = 0; c < 10; c++)
            {
                lists[c] = new List<Build>();
                var seg = Data.Segment(c, Difficulty, Levels[c]);
                if (seg == null) continue;
                foreach (var r in seg.Records)
                {
                    if (!r.Used) continue;
                    var b = Data.Brick(r.BrickId);
                    if (b == null) continue;
                    lists[c].Add(new Build { Brick = b, X = r.X, Y = r.Y, Par = r.Par, Segment = c });
                }
            }
            // Each segment is shifted so its leftmost floor tile (brick 100) joins the previous segment's rightmost one.
            for (int c = 1; c < 10; c++)
            {
                int prevRight = int.MinValue, left = int.MaxValue;
                foreach (var b in lists[c - 1]) if (b.BrickId == 100 && b.X > prevRight) prevRight = b.X;
                foreach (var b in lists[c]) if (b.BrickId == 100 && b.X < left) left = b.X;
                if (prevRight == int.MinValue || left == int.MaxValue) { Dilatation[c] = Dilatation[c - 1]; continue; }
                // The previous segment's builds are already shifted by Dilatation[c-1].
                Dilatation[c] = Dilatation[c - 1] + (prevRight + 88) - left;
            }
            // k-way merge by the brick comparator (layer, bottom+floor offset, x); ties go to the lower segment.
            var idx = new int[10];
            for (int c = 0; c < 10; c++)
                for (int i = 0; i < lists[c].Count; i++) { var b = lists[c][i]; b.X += Dilatation[c]; lists[c][i] = b; }
            while (true)
            {
                int best = -1;
                for (int c = 0; c < 10; c++)
                {
                    if (idx[c] >= lists[c].Count) continue;
                    if (best < 0 || Less(lists[c][idx[c]], lists[best][idx[best]])) best = c;
                }
                if (best < 0) break;
                Builds.Add(lists[best][idx[best]++]);
            }
            MinX = int.MaxValue; MaxX = int.MinValue;
            foreach (var b in Builds) { if (b.X < MinX) MinX = b.X; if (b.X > MaxX) MaxX = b.X; }
            if (Builds.Count == 0) MinX = MaxX = 0;
        }

        static bool Less(in Build a, in Build b)
        {
            if (a.Brick.Layer != b.Brick.Layer) return a.Brick.Layer < b.Brick.Layer;
            int ya = a.Y + a.Brick.Height + a.Brick.FloorOffset, yb = b.Y + b.Brick.Height + b.Brick.FloorOffset;
            if (ya != yb) return ya < yb;
            return a.X < b.X;
        }

        // ------------------------------------------------------------------ build → tile (§3.3)

        /// <summary>Column of a build: alignment −1 left, 0 centre, +1 right; small x offset. 0 when outside.</summary>
        public int TileX(in Build b, int dx, int align)
        {
            int w = b.Brick.Width, h = b.Brick.Height;
            int ax = align < 0 ? 0 : align == 0 ? w / 2 : w;
            int floorY = h - 2 + b.Y < 210 ? 93 : 220;
            int rc = (ax + b.X - Plate.OffsetX + 11 + dx + (h + b.Y - 2 - floorY) / 2) / 44 - 1;
            if (rc < 0 || rc >= Columns) return 0;
            return rc;
        }

        /// <summary>Row of a build (0..4 upper, 5..15 lower; may be outside).</summary>
        public int TileY(in Build b, int dy)
        {
            int h = b.Brick.Height;
            if (h - 2 + b.Y < 210) return (h - 2 + b.Y - 93 + dy + 2200) / 22 - 100;
            return 5 + (h - 2 + b.Y - 220 + dy + 2200) / 22 - 100;
        }

        // ------------------------------------------------------------------ walk grid (§4)

        void CalcPlates()
        {
            int maxX = 0;
            bool any = false;
            foreach (var b in Builds) { if (!any || b.X > maxX) maxX = b.X; any = true; }
            if (!any) { Columns = 1; InitialPlates = new byte[(Columns + Plate.SpareColumns) * Plate.Rows]; InitialSeats = new ushort[Columns + Plate.SpareColumns]; return; }
            Columns = maxX / 44 + 40;
            var g = new Grid(new byte[(Columns + Plate.SpareColumns) * Plate.Rows]);
            for (int i = 0; i < g.Data.Length; i++) g.Data[i] = Plate.Initial;
            InitialSeats = CalcSeats();

            for (int c = 0; c < Builds.Count; c++)
            {
                var b = Builds[c];
                int x, y, x2;
                switch (b.Brick.ObstacleType)
                {
                    case Obstacle.None:
                        if (b.BrickId >= 718 && b.BrickId <= 729)
                        {
                            x = TileX(b, 0, 0);
                            y = TileY(b, b.Brick.FloorOffset + (b.BrickId == 729 ? 30 : 0));
                            b.Par = Doors.Count;
                            Doors.Add(new DoorInfo
                            {
                                Tile = new Pt(x, y), BrickId = b.BrickId,
                                Kind = b.BrickId == 727 ? 1 : b.BrickId == 718 || b.BrickId == 719 ? 3 : b.BrickId == 729 ? 2 : 0,
                            });
                            Builds[c] = b;
                        }
                        else if (b.BrickId == 471 || b.BrickId == 303)
                        {
                            b.Par = Triggers.Count;
                            Triggers.Add(new Pt(b.X, b.Y));
                            Builds[c] = b;
                        }
                        break;

                    case Obstacle.Point:
                    case Obstacle.SitPoint:
                        x = TileX(b, 0, 0);
                        y = TileY(b, b.Brick.ObstacleType == Obstacle.SitPoint ? -10 : 0);
                        if (y >= 0 && y <= 15 && x >= 0)
                        {
                            g.Set(x, y, 0);
                            g.Clear(x, y + 1, 128); g.Clear(x, y - 1, 32); g.Clear(x + 1, y, 16); g.Clear(x - 1, y, 64);
                        }
                        break;

                    case Obstacle.Wide:
                        x = TileX(b, 10, -1); x2 = TileX(b, -10, 1); y = TileY(b, 0);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d <= x2; d++) g.Clear(d, y, 12);
                        break;

                    case Obstacle.ShortFence:
                    case Obstacle.Horizontal:
                    case Obstacle.HorizontalShifted:
                        x = TileX(b, 10, -1);
                        x2 = b.Brick.ObstacleType == Obstacle.ShortFence ? x : TileX(b, -10, 1) - (b.Brick.ObstacleType == Obstacle.HorizontalShifted ? 1 : 0);
                        y = TileY(b, -8) - (b.Brick.ObstacleType == Obstacle.HorizontalShifted ? 1 : 0);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d <= x2; d++) g.Clear(d, y, 32);
                        if (y >= -1 && y <= 14 && x >= 0) for (int d = x; d <= x2; d++) g.Clear(d, y + 1, 128);
                        break;

                    case Obstacle.BigVertical:
                        x = TileX(b, -6, -1); y = TileY(b, -8) - 1;
                        if (y >= 0 && y <= 15 && x >= 0) { g.Clear(x, y, 64); g.Clear(x + 1, y, 16); }
                        goto case Obstacle.Vertical;
                    case Obstacle.Vertical:
                        x = TileX(b, -6, -1); y = TileY(b, -8);
                        if (y >= 0 && y <= 15 && x >= 0) { g.Clear(x, y, 64); g.Clear(x + 1, y, 16); }
                        break;

                    case Obstacle.HorizontalExit:
                        x = TileX(b, 10, -1); x2 = TileX(b, -10, 1);
                        y = TileY(b, -8);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d <= x2; d++) if (d != x2 - 2) g.Clear(d, y, 32);
                        y = TileY(b, 8);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d <= x2; d++) if (d != x2 - 2) g.Clear(d, y, 128);
                        break;

                    case Obstacle.ShopFront:
                    case Obstacle.Museum:
                        x = TileX(b, 10, -1); x2 = TileX(b, -10, 1); y = TileY(b, -8);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d < x2; d++) g.Clear(d, y, 32);
                        if (y >= -1 && y <= 14 && x >= 0) for (int d = x; d < x2; d++) g.Clear(d, y + 1, 128);
                        for (y--; y >= 0 && y <= 14 && y != 4; y--)
                        {
                            if (b.Brick.ObstacleType == Obstacle.ShopFront && x >= 0) { g.Clear(x - 1, y, 64); g.Clear(x, y, 16); }
                            if (x2 >= 0) { g.Clear(x2 + 1, y, 16); g.Clear(x2, y, 64); }
                        }
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d <= x2; d++) g.Clear(d, y, 128);
                        break;

                    case Obstacle.StairFront:
                        x = TileX(b, 10, -1); x2 = TileX(b, -10, 1); y = TileY(b, -8);
                        if (y >= 0 && y <= 15 && x >= 0) for (int d = x; d < x2; d++) g.Clear(d, y, 32);
                        if (y >= -1 && y <= 14 && x >= 0) for (int d = x; d < x2; d++) g.Clear(d, y + 1, 128);
                        SideWalls(g, x, x2, y);
                        break;

                    case Obstacle.BuroFrontL:
                    case Obstacle.BuroFrontR:
                        x = TileX(b, 10, -1); x2 = TileX(b, -10, 1); y = TileY(b, -8);
                        {
                            int gap = b.Brick.ObstacleType == Obstacle.BuroFrontL ? x + 1 : x2 - 1;
                            if (y >= 0 && y <= 14 && x >= 0)
                                for (int d = x; d <= x2; d++) if (d != gap) { g.Clear(d, y, 32); g.Clear(d, y + 1, 128); }
                        }
                        SideWalls(g, x, x2, y);
                        break;

                    case Obstacle.NoEntry1:
                    case Obstacle.NoEntry2:
                        x = TileX(b, 10, -1);
                        x2 = TileX(b, b.Brick.ObstacleType == Obstacle.NoEntry2 ? -60 : -10, 1);
                        y = TileY(b, -8);
                        if (y >= 0 && y <= (b.Brick.ObstacleType == Obstacle.NoEntry2 ? 14 : 15) && x >= 0)
                            for (int d = x; d <= x2; d++) { g.Clear(d, y, 32); g.Clear(d, y + 1, 128); }
                        SideWalls(g, x, x2, y);
                        break;

                    case Obstacle.Up: x = TileX(b, 0, 0); y = TileY(b, 0); g.Clear(x, y, 112); break;
                    case Obstacle.Right: x = TileX(b, 0, 0); y = TileY(b, 0); g.Clear(x, y, 176); break;
                    case Obstacle.Down: x = TileX(b, 0, 0); y = TileY(b, 0); g.Clear(x, y, 208); break;
                    case Obstacle.Left: x = TileX(b, 0, 0); y = TileY(b, 0); g.Clear(x, y, 224); break;

                    case Obstacle.Rune:
                        x = TileX(b, 0, 0); y = TileY(b, 0);
                        if (y < 0 || y > 15 || x < 0) break;
                        switch (b.BrickId)
                        {
                            case RuneId.Up: g.Clear(x, y, 112); break;
                            case RuneId.Right: g.Clear(x, y, 176); break;
                            case RuneId.South: g.Clear(x, y, 208); break;
                            case RuneId.Left: g.Clear(x, y, 224); break;
                            case RuneId.UpRight: g.Clear(x, y, 48); break;
                            case RuneId.RightSouth: g.Clear(x, y, 144); break;
                            case RuneId.SouthLeft: g.Clear(x, y, 192); break;
                            case RuneId.LeftUp: g.Clear(x, y, 96); break;
                            case RuneId.BeltRight: g.Set(x, y, (byte)((g.Get(x, y) & ~176) | 1)); break;
                            case RuneId.BeltLeft: g.Set(x, y, (byte)((g.Get(x, y) & ~224) | 1)); break;
                            case RuneId.WestEast: g.Clear(x, y, 160); break;
                            case RuneId.NorthSouth: g.Clear(x, y, 80); break;
                            case RuneId.NoPlayer: g.Clear(x, y, 4); break;
                            case RuneId.NoClient: g.Clear(x, y, 8); break;
                            case RuneId.LeftEnd: LeftEnd = x * 44; break;
                            case RuneId.RightEnd: RightEnd = x * 44; break;
                            case RuneId.NoLeft: g.Clear(x, y, 16); g.Clear(x - 1, y, 64); break;
                            case RuneId.NoDown: g.Clear(x, y, 32); g.Clear(x, y + 1, 128); break;
                            case RuneId.NoWay:
                                g.Clear(x, y, 32); g.Clear(x, y + 1, 128);
                                g.Clear(x, y - 1, 32); g.Clear(x, y, 128);
                                g.Clear(x, y, 16); g.Clear(x - 1, y, 64);
                                g.Clear(x + 1, y, 16); g.Clear(x, y, 64);
                                break;
                            case RuneId.ClipFull:
                            case RuneId.ClipMiddle:
                            case RuneId.ClipNone:
                                ClipMarkers.Add(new ClipMarker { Type = b.BrickId, X = b.X });
                                break;
                        }
                        break;
                }
            }
            // Clip markers sorted by x (stable).
            StableSortClips();
            InitialPlates = g.Data;
        }

        void StableSortClips()
        {
            // Insertion sort: equal positions keep file order (the original's sort is stable too).
            for (int i = 1; i < ClipMarkers.Count; i++)
            {
                var v = ClipMarkers[i]; int j = i - 1;
                while (j >= 0 && ClipMarkers[j].X > v.X) { ClipMarkers[j + 1] = ClipMarkers[j]; j--; }
                ClipMarkers[j + 1] = v;
            }
        }

        /// <summary>Walls on both sides going up from row y until row 4 (the original stops at 4 or below 0).</summary>
        static void SideWalls(Grid g, int x, int x2, int y)
        {
            for (; y >= 0 && y <= 15 && y != 4; y--)
            {
                if (x >= 0) { g.Clear(x - 1, y, 64); g.Clear(x, y, 16); }
                if (x2 >= 0) { g.Clear(x2 + 1, y, 16); g.Clear(x2, y, 64); }
            }
        }

        ushort[] CalcSeats()
        {
            var seats = new ushort[Columns + Plate.SpareColumns];
            for (int i = 0; i < seats.Length; i++) seats[i] = 0xFFFF;
            for (int c = Builds.Count - 1; c >= 0; c--)
            {
                var b = Builds[c];
                if (b.BrickId < 370 || b.BrickId > 372) continue;
                int x = TileX(b, 0, 0), y = TileY(b, -10) - 5;
                if (y < 0 || y > 7) continue;
                int i = x + Plate.SpareColumns;
                seats[i] = (ushort)(seats[i] & ~(1 << (y * 2)) & ~(1 << (y * 2 + 1)));
            }
            return seats;
        }

        // ------------------------------------------------------------------ runes and areas (§4.4, §4.5)

        void CalcCoordinates()
        {
            // Area runes with par 0 take the room of a nearby 2SHOP (or SHOP) rune.
            for (int c = 0; c < Builds.Count; c++)
            {
                var b = Builds[c];
                if (b.Par != 0 || (b.BrickId != RuneId.AreaTopLeft && b.BrickId != RuneId.AreaBottomRight)) continue;
                bool lo = b.BrickId == RuneId.AreaTopLeft;
                foreach (int target in new[] { RuneId.ToShop, RuneId.Shop })
                {
                    if (b.Par != 0) break;
                    foreach (var d in Builds)
                    {
                        if (d.BrickId != target) continue;
                        bool hit = lo
                            ? d.X >= b.X && d.Y >= b.Y && d.X - b.X <= 176 && d.Y - b.Y <= 132
                            : b.X >= d.X && b.Y >= d.Y && b.X - d.X < 176 && b.Y - d.Y < 88;
                        if (hit) b.Par = d.Par;
                    }
                }
                Builds[c] = b;
            }

            foreach (var lo in Builds)
            {
                if (lo.BrickId != RuneId.AreaTopLeft) continue;
                int best = int.MaxValue; Pt p2 = default; bool found = false;
                foreach (var ru in Builds)
                {
                    if (ru.BrickId != RuneId.AreaBottomRight || ru.Par != lo.Par) continue;
                    int dist = Math.Abs(ru.X - lo.X);
                    if (dist < best && ru.X > lo.X && ru.Y > lo.Y) { best = dist; p2 = new Pt(ru.X, ru.Y); found = true; }
                }
                if (!found) p2 = new Pt(lo.X, lo.Y);
                Areas.Add(new AreaMarker { Par = lo.Par, P1 = new Pt(lo.X, lo.Y), P2 = p2 });
            }

            foreach (var b in Builds)
            {
                if (b.BrickId == RuneId.Shop && b.Par >= RoomIds.BeltX1 && b.Par <= RoomIds.BeltX10)
                    NumBeltSpots = Math.Max(NumBeltSpots, b.Par - RoomIds.BeltX1);
            }

            foreach (var b in Builds)
            {
                if (b.Brick.ObstacleType != Obstacle.Rune || !RuneId.IsQueryRune(b.BrickId)) continue;
                var r = new RuneInfo { Id = b.BrickId, Par = b.Par, Raw = new Pt(b.X, b.Y), Pos = new Pt(b.X, b.Y), Brick = b.Brick };
                if (b.BrickId != RuneId.CondBlock)
                {
                    int w = b.Brick.Width, h = b.Brick.Height;
                    if (h - 2 + b.Y < 170)
                    {
                        int x = (w / 2 + b.X - Plate.OffsetX + 11 + (h + b.Y - 2 - 93) / 2) / 44 - 1;
                        int y = (h - 2 + b.Y - 93 + 2200) / 22 - 100;
                        r.Pos = new Pt(x * 44 + 22, y * 22 + 11 + 5000);
                    }
                    else
                    {
                        int x = (w / 2 + b.X - Plate.OffsetX + 11 + (h + b.Y - 2 - 220) / 2) / 44 - 1;
                        int y = (h - 2 + b.Y - 220 + 2200) / 22 - 100;
                        r.Pos = new Pt(x * 44 + 22, y * 22 + 11);
                    }
                }
                Runes.Add(r);
            }
        }

        /// <summary>Number of rentable gates = number of RUNE_2WAIT runes (§2.2).</summary>
        public int GateCount { get { int n = 0; foreach (var r in Runes) if (r.Id == RuneId.ToWait) n++; return n; } }

        public bool HasRune(int id, int par) { foreach (var r in Runes) if (r.Id == id && r.Par == par) return true; return false; }

        /// <summary>Byte grid with 20 spare columns in front (index = row + 16·column).</summary>
        internal readonly struct Grid
        {
            public readonly byte[] Data;
            public Grid(byte[] d) => Data = d;
            int I(int x, int y) => y + ((x + Plate.SpareColumns) << 4);
            public byte Get(int x, int y) { int i = I(x, y); return i >= 0 && i < Data.Length ? Data[i] : (byte)0; }
            public void Set(int x, int y, byte v) { int i = I(x, y); if (i >= 0 && i < Data.Length) Data[i] = v; }
            public void Clear(int x, int y, int bits) { int i = I(x, y); if (i >= 0 && i < Data.Length) Data[i] = (byte)(Data[i] & ~bits); }
        }
    }
}
