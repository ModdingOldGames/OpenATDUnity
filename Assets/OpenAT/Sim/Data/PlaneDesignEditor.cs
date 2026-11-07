using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Data
{
    /// <summary>Where a carried part would snap to (rooms-and-ui-7 §6 "Interaction state machine", step 2).</summary>
    public struct DesignSnap
    {
        /// <summary>Relation table index (−1 = no snap, the part just hangs at the mouse).</summary>
        public int Relation;
        /// <summary>Index into <see cref="PlaneDesign.Parts"/> of the parent (−1 = the desk).</summary>
        public int ParentPart;
        public int Pos3dX, Pos3dY, Pos2dX, Pos2dY;
        /// <summary>Mirrored twin (left wing / second engine): relation −1 = none.</summary>
        public int TwinRelation;
        public int TwinPos3dX, TwinPos3dY, TwinPos2dX, TwinPos2dY;
        public bool Snapped => Relation >= 0;
    }

    /// <summary>
    /// Editing rules of the aircraft constructor (rooms-and-ui-7 §6), independent of the screen:
    /// enabled strip slots, wing skipping, snap search, placing (with the mirrored twin) and the
    /// cascading removal. Positions are editor canvas pixels; bitmap sizes come from the caller.
    /// </summary>
    public sealed class PlaneDesignEditor
    {
        /// <summary>Strip slots: body, cockpit, tail, wings, engines.</summary>
        public const int SlotBody = 0, SlotCockpit = 1, SlotTail = 2, SlotWing = 3, SlotMotor = 4;
        public static readonly char[] SlotCategory = { 'B', 'C', 'H', 'R', 'M' };
        public static readonly int[] SlotVariants = { 5, 5, 7, 6, 8 };

        public readonly PlaneDesignCatalog Catalog;
        public PlaneDesign Design;
        /// <summary>Bitmap size of a part (editor bitmap), by short name.</summary>
        public readonly Func<string, (int w, int h)> Size;

        public PlaneDesignEditor(PlaneDesignCatalog catalog, PlaneDesign design, Func<string, (int w, int h)> size)
        {
            Catalog = catalog; Design = design ?? new PlaneDesign(); Size = size;
        }

        // ------------------------------------------------------------------ strip state

        /// <summary>Enabled strip slots ("UpdateButtonState").</summary>
        public bool[] EnabledSlots()
        {
            bool b = true, c = false, h = false, wg = false, m = false;
            foreach (var p in Design.Parts)
            {
                char k = Cat(p);
                if (k == 'B') c = h = wg = true;
                if (k == 'L' || k == 'H') m = true;
            }
            foreach (var p in Design.Parts)
            {
                char k = Cat(p);
                if (k == 'B') b = false;
                if (k == 'C') c = false;
                if (k == 'H') h = false;
                if (k == 'L') wg = false;
            }
            return new[] { b, c, h, wg, m };
        }

        static char Cat(DesignPart p) => string.IsNullOrEmpty(p.ShortName) ? '?' : p.ShortName[0];

        /// <summary>Short name of the selected variant of a strip slot (wings show the right wing).</summary>
        public static string VariantName(int slot, int variant) => SlotCategory[slot].ToString() + (variant + 1);

        /// <summary>
        /// "CheckUnusablePart": while wings are enabled, skips wing variants that do not fit the present
        /// body, moving in <paramref name="direction"/> (+1 / −1).
        /// </summary>
        public int SkipUnusableWing(int sel, int direction, bool wingsEnabled)
        {
            if (!wingsEnabled) return sel;
            for (int guard = 0; guard < 12; guard++)
            {
                bool bad = (Design.Has("B1") && (sel == 0 || sel == 1 || sel == 2 || sel == 5))
                    || (Design.Has("B2") && sel == 4)
                    || (Design.Has("B3") && (sel == 2 || sel == 5))
                    || (Design.Has("B4") && (sel == 0 || sel == 2 || sel == 5))
                    || (Design.Has("B5") && sel == 5);
                if (!bad) return sel;
                sel = (sel + direction + 6) % 6;
            }
            return sel;
        }

        /// <summary>The mirrored partner carried along: a right wing brings its left wing, an engine its twin.</summary>
        public static string TwinOf(string carried)
        {
            if (string.IsNullOrEmpty(carried)) return null;
            if (carried[0] == 'R') return "L" + carried.Substring(1);
            if (carried[0] == 'M') return carried;
            return null;
        }

        // ------------------------------------------------------------------ snapping

        bool SlotFree(string slot)
        {
            if (string.IsNullOrEmpty(slot) || slot.Length < 2) return true;
            foreach (var p in Design.Parts)
            {
                if (p.RelationIndex < 0 || p.RelationIndex >= Catalog.Relations.Count) continue;
                if (Catalog.Relations[p.RelationIndex].Blocks(slot)) return false;
            }
            return true;
        }

        /// <summary>
        /// Snap search for the carried part at mouse (mx, my) (§6 step 2): best relation within 100 px
        /// (desk relations compare the mouse with the target's top-left and accept up to twice the best
        /// distance, as the original does). Without a snap the part is centred on the mouse.
        /// </summary>
        public DesignSnap FindSnap(string carried, int mx, int my)
        {
            var snap = new DesignSnap { Relation = -1, ParentPart = -1, TwinRelation = -1 };
            if (string.IsNullOrEmpty(carried)) return snap;
            var (w, h) = Size(carried);
            snap.Pos3dX = mx - w / 2; snap.Pos3dY = my - h / 2;
            string twin = TwinOf(carried);
            if (twin != null)
            {
                var (_, th) = Size(twin);
                snap.TwinPos3dX = snap.Pos3dX + w * 3 / 4; snap.TwinPos3dY = snap.Pos3dY - th;
            }
            int to = Catalog.IndexOfPart(carried);
            double best = 100;
            var rels = Catalog.Relations;
            for (int c = 0; c < rels.Count; c++)
            {
                var r = rels[c];
                if (r.To != to || !SlotFree(r.Slot)) continue;
                if (r.From < 0)
                {
                    int tx = r.Offset3dX - w / 2, ty = r.Offset3dY - h / 2;
                    double dist = Dist(mx - tx, my - ty);
                    if (dist < best * 2)
                    {
                        best = dist;
                        snap.Relation = c; snap.ParentPart = -1;
                        snap.Pos3dX = tx; snap.Pos3dY = ty; snap.Pos2dX = r.Offset2dX; snap.Pos2dY = r.Offset2dY;
                    }
                    continue;
                }
                string from = Catalog.Parts[r.From].ShortName;
                for (int d = 0; d < Design.Parts.Count; d++)
                {
                    var pd = Design.Parts[d];
                    if (!string.Equals(pd.ShortName, from, StringComparison.OrdinalIgnoreCase)) continue;
                    int tx = pd.Pos3dX + r.Offset3dX, ty = pd.Pos3dY + r.Offset3dY;
                    double dist = Dist(mx - tx - w / 2, my - ty - h / 2);
                    if (dist >= best) continue;
                    best = dist;
                    snap.Relation = c; snap.ParentPart = d;
                    snap.Pos3dX = tx; snap.Pos3dY = ty; snap.Pos2dX = pd.Pos2dX + r.Offset2dX; snap.Pos2dY = pd.Pos2dY + r.Offset2dY;
                    snap.TwinRelation = -1;
                    if (twin == null) continue;
                    // Twin parent: the other wing when the parent is a wing, else the same part.
                    int e = d;
                    char pc = Cat(pd);
                    if (pc == 'L' || pc == 'R')
                        for (int k = 0; k < Design.Parts.Count; k++)
                        {
                            char kc = Cat(Design.Parts[k]);
                            if (k != d && (kc == 'L' || kc == 'R')) { e = k; break; }
                        }
                    int add = pc == 'L' || (pc == 'H' && r.Slot.Length > 1 && r.Slot[1] == 'L') ? -1 : 1;
                    int tr = c + add;
                    if (tr < 0 || tr >= rels.Count) continue;   // the original reads past the table here
                    var pe = Design.Parts[e];
                    snap.TwinRelation = tr;
                    snap.TwinPos3dX = pe.Pos3dX + rels[tr].Offset3dX; snap.TwinPos3dY = pe.Pos3dY + rels[tr].Offset3dY;
                    snap.TwinPos2dX = pe.Pos2dX + rels[tr].Offset2dX; snap.TwinPos2dY = pe.Pos2dY + rels[tr].Offset2dY;
                }
            }
            return snap;
        }

        static double Dist(int dx, int dy) => Math.Sqrt((double)dx * dx + (double)dy * dy);

        /// <summary>
        /// Mouse release with a snap (§6 step 3): adds the part (and the twin), re-sorts. Returns the part
        /// that stays on the cursor: engines stay for repeated placement unless <paramref name="fromStripDrag"/>.
        /// </summary>
        public string Place(string carried, DesignSnap snap, bool fromStripDrag)
        {
            if (!snap.Snapped || string.IsNullOrEmpty(carried)) return carried;
            string parent = snap.ParentPart >= 0 ? Design.Parts[snap.ParentPart].ShortName : "";
            Design.Parts.Add(new DesignPart
            {
                ShortName = carried, ParentShortName = parent, RelationIndex = snap.Relation,
                Pos2dX = snap.Pos2dX, Pos2dY = snap.Pos2dY, Pos3dX = snap.Pos3dX, Pos3dY = snap.Pos3dY,
            });
            string twin = TwinOf(carried);
            if (twin != null && snap.TwinRelation >= 0)
                Design.Parts.Add(new DesignPart
                {
                    ShortName = twin, ParentShortName = parent, RelationIndex = snap.TwinRelation,
                    Pos2dX = snap.TwinPos2dX, Pos2dY = snap.TwinPos2dY, Pos3dX = snap.TwinPos3dX, Pos3dY = snap.TwinPos3dY,
                });
            Design.SortParts(Catalog);
            if (fromStripDrag) return null;
            return carried[0] == 'M' ? carried : null;
        }

        /// <summary>True when removing this part needs the "clear the whole design" confirmation (a body while others exist).</summary>
        public bool NeedsClearConfirm(int index) =>
            index >= 0 && index < Design.Parts.Count && Cat(Design.Parts[index]) == 'B' && Design.Parts.Count > 1;

        /// <summary>
        /// Removes a placed part with the cascade (§6 step 5): orphans (parent no longer present), the
        /// mirror wing (relation id ±200) and, for an engine on a wing, its mirror engine (id ±10 at the
        /// neighbouring table index). Returns the short name to carry (left wings become right wings).
        /// </summary>
        public string Remove(int index)
        {
            if (index < 0 || index >= Design.Parts.Count) return null;
            var part = Design.Parts[index];
            int relnr = part.RelationIndex;
            int rel = relnr >= 0 && relnr < Catalog.Relations.Count ? Catalog.Relations[relnr].Id : -1;
            string carry = part.ShortName;
            if (carry.Length > 0 && carry[0] == 'L') carry = "R" + carry.Substring(1);
            Design.Parts.RemoveAt(index);
            bool again = true;
            while (again)
            {
                again = false;
                for (int c = 0; c < Design.Parts.Count; c++)
                {
                    var p = Design.Parts[c];
                    if (string.IsNullOrEmpty(p.ParentShortName)) continue;
                    int pid = p.RelationIndex >= 0 && p.RelationIndex < Catalog.Relations.Count ? Catalog.Relations[p.RelationIndex].Id : -1;
                    bool orphan = !Design.Has(p.ParentShortName);
                    bool mirrorWing = (carry[0] == 'R' && rel >= 400 && rel < 600 && pid == rel + 200) || (rel >= 600 && rel < 800 && pid == rel - 200);
                    bool mirrorMotor = carry[0] == 'M' && rel >= 700 && rel <= 1400 && Math.Abs(pid - rel) == 10 && Math.Abs(relnr - p.RelationIndex) == 1;
                    if (orphan || mirrorWing || mirrorMotor) { Design.Parts.RemoveAt(c); again = true; break; }
                }
            }
            return carry;
        }

        /// <summary>Right click on a strip slot (§6 step 6): removes every part of that category (both wings) and the orphans.</summary>
        public void RemoveCategory(int slot)
        {
            char k = SlotCategory[slot];
            bool again = true;
            while (again)
            {
                again = false;
                for (int c = 0; c < Design.Parts.Count; c++)
                {
                    var p = Design.Parts[c];
                    char pc = Cat(p);
                    bool match = pc == k || (k == 'R' && pc == 'L');
                    bool orphan = !string.IsNullOrEmpty(p.ParentShortName) && !Design.Has(p.ParentShortName);
                    if (match || orphan) { Design.Parts.RemoveAt(c); again = true; break; }
                }
            }
        }

        // ------------------------------------------------------------------ labels

        /// <summary>Noise label id (Std Misc, §6 "Noise label").</summary>
        public static int NoiseLabel(int noise) =>
            noise > 100 ? 8406 : noise > 80 ? 8405 : noise > 60 ? 8404 : noise > 40 ? 8403 : noise > 20 ? 8402 : noise > 0 ? 8401 : 8400;

        /// <summary>Maintenance label id (Std Misc, §6 "Maintenance label").</summary>
        public static int MaintenanceLabel(int m) =>
            m > 110 ? 8500 : m > 80 ? 8501 : m > 50 ? 8502 : m > 20 ? 8503 : m >= 0 ? 8504 : m > -10 ? 8505 : m > -20 ? 8506 : m > -30 ? 8507 : 8508;
    }
}
