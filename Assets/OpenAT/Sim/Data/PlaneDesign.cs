using System;
using System.Collections.Generic;
using System.Text;
using OpenAT.Formats;

namespace OpenAT.Sim.Data
{
    /// <summary>A designer part (planes-and-flights.md §5.2). Category = first letter: B body, C cockpit, H tail, R/L wing, M engine.</summary>
    public sealed class PlanePart
    {
        public int Index;
        public int Id;
        public string ShortName;
        public int Cost, Weight, Power, Noise, Maintenance, Seats, Burn;
        public char Category => string.IsNullOrEmpty(ShortName) ? '?' : ShortName[0];
        /// <summary>Editor draw-order base of the category (rooms-and-ui-7 §6 "z-base").</summary>
        public int ZBase => ZBaseOf(Category);
        public override string ToString() => ShortName;

        public static int ZBaseOf(char category)
        {
            switch (category)
            {
                case 'B': return 10000;
                case 'C': return 10010;
                case 'H': return 9000;
                case 'R': return 12000;
                case 'M': return 7500;
                case 'L': return 8000;
            }
            return 0;
        }
    }

    /// <summary>
    /// An attachment relation of the designer graph (planes-and-flights.md §5.3): child part
    /// <see cref="To"/> attached to <see cref="From"/> (−1 = desktop) using <see cref="Slot"/>,
    /// blocking <see cref="BlockedSlots"/> (concatenated 2-character codes, case-sensitive).
    /// </summary>
    public sealed class PartRelation
    {
        public int Index;
        public int Id;
        public int From = -1, To;
        public int Note1 = DesignNote.Standard, Note2 = DesignNote.Standard, Note3 = DesignNote.Standard;
        public int ZAdd;
        public int Noise;
        public string Slot = "";
        public string BlockedSlots = "";
        /// <summary>Visual offsets from relation.csv (2-D airport view, 3-D editor view).</summary>
        public int Offset2dX, Offset2dY, Offset3dX, Offset3dY;

        public IEnumerable<int> Notes { get { yield return Note1; yield return Note2; yield return Note3; } }

        public bool Blocks(string slot)
        {
            for (int i = 0; i + 1 < BlockedSlots.Length; i += 2)
                if (BlockedSlots[i] == slot[0] && BlockedSlots[i + 1] == slot[1]) return true;
            return false;
        }
    }

    /// <summary>Designer note codes (planes-and-flights.md §5.3).</summary>
    public static class DesignNote
    {
        public const int Standard = 100;
        /// <summary>"Breaks often" and "high consumption" share the code (QUIRK).</summary>
        public const int Breaks = 101;
        public const int BreaksXL = 102;
        public const int Speed300 = 103, Speed400 = 104, Speed500 = 105, Speed600 = 106, Speed700 = 107, Speed800 = 108;
        public const int Pilot1 = 109, Pilot2 = 110, Pilot3 = 111, Pilot4 = 112;
        public const int Attendants4 = 113, Attendants6 = 114, Attendants8 = 115, Attendants10 = 116;

        public static int SpeedCap(int note) => note >= Speed300 && note <= Speed800 ? 300 + (note - Speed300) * 100 : 0;
        public static int Pilots(int note) => note >= Pilot1 && note <= Pilot4 ? note - Pilot1 + 1 : 0;
        public static int Attendants(int note)
        {
            switch (note) { case Attendants4: return 4; case Attendants6: return 6; case Attendants8: return 8; case Attendants10: return 10; }
            return 0;
        }
    }

    /// <summary>
    /// The designer's part catalogue and attachment graph (planes-and-flights.md §5.2–5.3,
    /// data-tables.md §1.10). Built from <see cref="PlaneDesignData"/>; the install's CSV files
    /// override the numeric values by row position.
    /// </summary>
    public sealed class PlaneDesignCatalog
    {
        public readonly List<PlanePart> Parts = new List<PlanePart>();
        public readonly List<PartRelation> Relations = new List<PartRelation>();

        public static PlaneDesignCatalog CreateDefault()
        {
            var c = new PlaneDesignCatalog();
            foreach (var line in PlaneDesignData.Parts.Split('\n'))
            {
                var f = line.Trim().Split(';');
                if (f.Length < 9) continue;
                c.Parts.Add(new PlanePart
                {
                    Index = c.Parts.Count, Id = CsvTable.Atoi(f[0]), ShortName = f[1],
                    Cost = CsvTable.Atoi(f[2]), Weight = CsvTable.Atoi(f[3]), Power = CsvTable.Atoi(f[4]),
                    Noise = CsvTable.Atoi(f[5]), Maintenance = CsvTable.Atoi(f[6]), Seats = CsvTable.Atoi(f[7]), Burn = CsvTable.Atoi(f[8]),
                });
            }
            foreach (var line in PlaneDesignData.Relations.Split('\n'))
            {
                var f = line.Trim().Split(';');
                if (f.Length < 10) continue;
                c.Relations.Add(new PartRelation
                {
                    Index = c.Relations.Count, Id = CsvTable.Atoi(f[0]),
                    From = f[1] == "-" ? -1 : c.IndexOfPart(f[1]), To = c.IndexOfPart(f[2]),
                    Note1 = CsvTable.Atoi(f[3]), Note2 = CsvTable.Atoi(f[4]), Note3 = CsvTable.Atoi(f[5]),
                    ZAdd = CsvTable.Atoi(f[6]), Noise = CsvTable.Atoi(f[7]), Slot = f[8], BlockedSlots = f[9],
                });
            }
            return c;
        }

        /// <summary>Default catalogue with the install's builds.csv / relation.csv applied (if present).</summary>
        public static PlaneDesignCatalog Load(GameInstall install)
        {
            var c = CreateDefault();
            if (install == null) return c;
            CsvTable b = install.Exists("data/builds.csv") ? CsvTable.Parse(install.ReadAllBytes("data/builds.csv"), false) : null;
            CsvTable r = install.Exists("data/relation.csv") ? CsvTable.Parse(install.ReadAllBytes("data/relation.csv"), false) : null;
            c.ApplyOverrides(b, r);
            return c;
        }

        /// <summary>
        /// Applies the CSV balance values by row position (data-tables.md §1.10: ids are informational,
        /// a mismatching id is ignored and the values are still applied).
        /// </summary>
        public void ApplyOverrides(CsvTable builds, CsvTable relations)
        {
            if (builds != null)
                for (int i = 0; i < builds.Rows.Count && i < Parts.Count; i++)
                {
                    var f = builds.Rows[i];
                    if (f.Length < 9) continue;
                    var p = Parts[i];
                    p.Cost = CsvTable.Atoi(f[2]); p.Weight = CsvTable.Atoi(f[3]); p.Power = CsvTable.Atoi(f[4]);
                    p.Noise = CsvTable.Atoi(f[5]); p.Maintenance = CsvTable.Atoi(f[6]); p.Seats = CsvTable.Atoi(f[7]); p.Burn = CsvTable.Atoi(f[8]);
                }
            if (relations != null)
                for (int i = 0; i < relations.Rows.Count && i < Relations.Count; i++)
                {
                    var f = relations.Rows[i];
                    if (f.Length < 11) continue;
                    var r = Relations[i];
                    r.Offset2dX = CsvTable.Atoi(f[3]); r.Offset2dY = CsvTable.Atoi(f[4]);
                    r.Offset3dX = CsvTable.Atoi(f[5]); r.Offset3dY = CsvTable.Atoi(f[6]);
                    r.Note1 = CsvTable.Atoi(f[7]); r.Note2 = CsvTable.Atoi(f[8]); r.Note3 = CsvTable.Atoi(f[9]);
                    r.Noise = CsvTable.Atoi(f[10]);
                }
        }

        public int IndexOfPart(string shortName)
        {
            for (int i = 0; i < Parts.Count; i++) if (Parts[i].ShortName == shortName) return i;
            return -1;
        }

        public PlanePart Part(string shortName)
        {
            int i = IndexOfPart(shortName);
            return i < 0 ? null : Parts[i];
        }

        /// <summary>
        /// Placement rule (planes-and-flights.md §5.3): the parent part must be placed (or the
        /// relation starts at the desktop) and the relation's slot must not be blocked by the relation
        /// of any placed part.
        /// </summary>
        public bool CanAttach(PlaneDesign design, int relationIndex)
        {
            if (relationIndex < 0 || relationIndex >= Relations.Count) return false;
            var rel = Relations[relationIndex];
            if (rel.From >= 0)
            {
                bool parent = false;
                foreach (var p in design.Parts)
                    if (p.ShortName == Parts[rel.From].ShortName) { parent = true; break; }
                if (!parent) return false;
            }
            foreach (var p in design.Parts)
            {
                if (p.RelationIndex < 0 || p.RelationIndex >= Relations.Count) continue;
                if (rel.Slot.Length >= 2 && Relations[p.RelationIndex].Blocks(rel.Slot)) return false;
            }
            return true;
        }
    }

    /// <summary>A part placed in a design: the relation (table index) it was attached with.</summary>
    public sealed class DesignPart
    {
        public string ShortName;
        public string ParentShortName;
        /// <summary>Index into <see cref="PlaneDesignCatalog.Relations"/> (the original stores the index).</summary>
        public int RelationIndex;
        public int Pos2dX, Pos2dY, Pos3dX, Pos3dY;
    }

    /// <summary>Derived statistics of a design (planes-and-flights.md §5.4).</summary>
    public struct DesignStats
    {
        public long Cost;
        public int Seats, Weight, Power, Speed, Burn, Tank, Range, Pilots, Attendants, Noise, Maintenance;
    }

    /// <summary>
    /// A designer plane blueprint (planes-and-flights.md §5, "CXPlane"). Designs are shared files, not
    /// game state; a plane bought from a design keeps a copy.
    /// </summary>
    public sealed class PlaneDesign
    {
        public string Name = "";
        public readonly List<DesignPart> Parts = new List<DesignPart>();

        public PlaneDesign Clone()
        {
            var d = new PlaneDesign { Name = Name };
            foreach (var p in Parts)
                d.Parts.Add(new DesignPart { ShortName = p.ShortName, ParentShortName = p.ParentShortName, RelationIndex = p.RelationIndex, Pos2dX = p.Pos2dX, Pos2dY = p.Pos2dY, Pos3dX = p.Pos3dX, Pos3dY = p.Pos3dY });
            return d;
        }

        /// <summary>Adds a part attached through the relation with table index <paramref name="relationIndex"/>.</summary>
        public DesignPart Add(PlaneDesignCatalog cat, int relationIndex)
        {
            var rel = cat.Relations[relationIndex];
            var p = new DesignPart
            {
                ShortName = cat.Parts[rel.To].ShortName,
                ParentShortName = rel.From >= 0 ? cat.Parts[rel.From].ShortName : "",
                RelationIndex = relationIndex,
            };
            Parts.Add(p);
            return p;
        }

        /// <summary>Adds a part by relation id (first relation in table order with that id).</summary>
        public DesignPart AddById(PlaneDesignCatalog cat, int relationId)
        {
            foreach (var r in cat.Relations) if (r.Id == relationId) return Add(cat, r.Index);
            throw new ArgumentException("unknown relation id " + relationId);
        }

        IEnumerable<int> AllNotes(PlaneDesignCatalog cat)
        {
            foreach (var p in Parts)
            {
                if (p.RelationIndex < 0 || p.RelationIndex >= cat.Relations.Count) continue;
                foreach (var n in cat.Relations[p.RelationIndex].Notes) yield return n;
            }
        }

        PlanePart PartOf(PlaneDesignCatalog cat, DesignPart p) => cat.Part(p.ShortName) ?? new PlanePart { ShortName = p.ShortName };

        /// <summary>Computes the derived statistics (planes-and-flights.md §5.4), integer arithmetic of the original.</summary>
        public DesignStats Stats(PlaneDesignCatalog cat)
        {
            var s = new DesignStats();
            int wingWeight = 0;
            foreach (var dp in Parts)
            {
                var p = PartOf(cat, dp);
                s.Cost += p.Cost;
                s.Seats += p.Seats;
                s.Weight += p.Weight;
                s.Power += p.Power;
                s.Burn += p.Burn;
                s.Noise += p.Noise;
                s.Maintenance += p.Maintenance;
                if (p.Category == 'L' || p.Category == 'R') wingWeight += p.Weight;
                if (dp.RelationIndex >= 0 && dp.RelationIndex < cat.Relations.Count) s.Noise += cat.Relations[dp.RelationIndex].Noise;
            }
            int pilots = 0, attendants = 0;
            foreach (var n in AllNotes(cat))
            {
                if (n == DesignNote.Breaks) { s.Burn += 2500; s.Maintenance += 10; }
                if (n == DesignNote.BreaksXL) { s.Burn += 5000; s.Maintenance += 20; }
                pilots += DesignNote.Pilots(n);
                attendants += DesignNote.Attendants(n);
            }
            s.Pilots = Math.Max(1, pilots);
            s.Attendants = Math.Max(1, attendants);

            s.Speed = s.Power == 0 ? 0 : (s.Power - 6000) * 730 / 44000 + 270;
            foreach (var n in AllNotes(cat))
            {
                int cap = DesignNote.SpeedCap(n);
                if (cap > 0 && s.Speed > cap) s.Speed = (s.Speed * 2 + cap) / 3;
            }

            s.Tank = wingWeight * 7;
            if (s.Burn > 0 && s.Tank > 0 && s.Tank / s.Burn > 22) s.Tank = 22 * s.Burn;
            s.Range = s.Burn > 0 ? s.Tank / s.Burn * s.Speed : 0;
            return s;
        }

        bool HasCategory(char c)
        {
            foreach (var p in Parts) if (!string.IsNullOrEmpty(p.ShortName) && p.ShortName[0] == c) return true;
            return false;
        }

        /// <summary>
        /// Build error (planes-and-flights.md §5.4): 0 = none, 8300 = asymmetric engines (std text Misc
        /// 8300), 8301 = engines too weak (Misc 8301). Incomplete designs report 0 (they are simply not
        /// buildable).
        /// </summary>
        public int Error(PlaneDesignCatalog cat)
        {
            if (!IsComplete) return 0;
            foreach (var a in Parts)
            {
                if (a.ShortName[0] != 'M') continue;
                var ra = Rel(cat, a);
                if (ra == null || ra.From < 0) continue;
                char side = cat.Parts[ra.From].Category;
                if (side != 'R' && side != 'L') continue;
                int want = side == 'R' ? ra.Id + 10 : ra.Id - 10;
                char other = side == 'R' ? 'L' : 'R';
                bool found = false;
                foreach (var b in Parts)
                {
                    if (b.ShortName[0] != 'M') continue;
                    var rb = Rel(cat, b);
                    if (rb == null || rb.From < 0 || cat.Parts[rb.From].Category != other) continue;
                    if (rb.Id == want) { found = true; break; }
                }
                if (!found) return 8300;
            }
            var st = Stats(cat);
            if ((long)st.Power * 4 < st.Weight) return 8301;
            return 0;
        }

        static PartRelation Rel(PlaneDesignCatalog cat, DesignPart p) =>
            p.RelationIndex >= 0 && p.RelationIndex < cat.Relations.Count ? cat.Relations[p.RelationIndex] : null;

        /// <summary>At least one body, cockpit, tail, right wing and engine.</summary>
        public bool IsComplete => HasCategory('B') && HasCategory('C') && HasCategory('H') && HasCategory('R') && HasCategory('M');

        public bool IsBuildable(PlaneDesignCatalog cat) => IsComplete && Error(cat) == 0;

        // ------------------------------------------------------------------ .pln files

        /// <summary>
        /// Reads a designer plane file (data-tables.md §2.4): preview header (skipped by its size field),
        /// name, cost (ignored, garbage in shipped files) and the parts album; unused album slots
        /// (id 0) are dropped.
        /// </summary>
        public static PlaneDesign ReadPln(byte[] data)
        {
            int pos = 0;
            int I32() { int v = BitConverter.ToInt32(data, pos); pos += 4; return v; }
            string Str()
            {
                int len = I32();
                if (len <= 0) return "";
                int n = len;
                while (n > 0 && data[pos + n - 1] == 0) n--;
                var s = TextEncoding.Decode(data, pos, n);
                pos += len;
                return s;
            }

            uint size = (uint)I32();
            for (int i = 0; i < 6; i++) I32();
            pos += (int)size - (4 + 4 * 5) + 1;
            var d = new PlaneDesign { Name = Str() };
            I32(); // cost (not used)
            int count = I32();
            I32(); // deleted offset
            var parts = new List<DesignPart>();
            for (int i = 0; i < count; i++)
            {
                var p = new DesignPart { Pos2dX = I32(), Pos2dY = I32(), Pos3dX = I32(), Pos3dY = I32() };
                p.ShortName = Str();
                p.ParentShortName = Str();
                p.RelationIndex = I32();
                parts.Add(p);
            }
            I32(); // album last id
            int idCount = I32();
            I32();
            for (int i = 0; i < idCount && i < parts.Count; i++)
                if (I32() != 0 && !string.IsNullOrEmpty(parts[i].ShortName)) d.Parts.Add(parts[i]);
            return d;
        }
            /// <summary>
        /// Writes a designer plane file in the original layout (rooms-and-ui-7 §6 ".plane file format",
        /// data-tables.md §2.4): preview header (size, cost, weight, burn, noise, burn again (original
        /// quirk), speed, range, name + NUL), then name, cost and the parts album. The album is padded to
        /// at least 100 slots like the editor's part buffer, unused slots carry id 0.
        /// </summary>
        public byte[] WritePln(PlaneDesignCatalog cat)
        {
            var ms = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(ms);
            byte[] Bytes(string t)
            {
                t = t ?? "";
                var b = new byte[t.Length];
                for (int i = 0; i < t.Length; i++) b[i] = (byte)TextEncoding.ToByte(t[i]);
                return b;
            }
            void Str(string t)
            {
                var b = Bytes(t);
                w.Write(b.Length + 1);
                w.Write(b);
                w.Write((byte)0);
            }
            var st = Stats(cat);
            var name = Bytes(Name);
            w.Write((uint)(4 + 6 * 4 + name.Length));
            w.Write((int)st.Cost); w.Write(st.Weight); w.Write(st.Burn); w.Write(st.Noise); w.Write(st.Burn); w.Write(st.Speed); w.Write(st.Range);
            w.Write(name); w.Write((byte)0);
            Str(Name);
            w.Write((int)st.Cost);
            int slots = Math.Max(100, Parts.Count);
            w.Write(slots);
            w.Write(0);
            for (int i = 0; i < slots; i++)
            {
                var p = i < Parts.Count ? Parts[i] : null;
                w.Write(p?.Pos2dX ?? 0); w.Write(p?.Pos2dY ?? 0); w.Write(p?.Pos3dX ?? 0); w.Write(p?.Pos3dY ?? 0);
                Str(p?.ShortName ?? "");
                Str(p?.ParentShortName ?? "");
                w.Write(p?.RelationIndex ?? 0);
            }
            w.Write(0x01000000 + Parts.Count);
            w.Write(slots);
            w.Write(0);
            for (int i = 0; i < slots; i++) w.Write(i < Parts.Count ? 0x01000001 + i : 0);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Draw order of the editor (rooms-and-ui-7 §6 "Sort the parts"): ascending z-base of the
        /// category + editor y + the relation's zAdd; stable like the original's bubble sort.
        /// </summary>
        public void SortParts(PlaneDesignCatalog cat)
        {
            int Key(DesignPart p) => PlanePart.ZBaseOf(p.ShortName.Length > 0 ? p.ShortName[0] : '?') + p.Pos3dY
                + (p.RelationIndex >= 0 && p.RelationIndex < cat.Relations.Count ? cat.Relations[p.RelationIndex].ZAdd : 0);
            for (int i = 1; i < Parts.Count; i++)
            {
                var x = Parts[i];
                int k = Key(x), j = i - 1;
                while (j >= 0 && Key(Parts[j]) > k) { Parts[j + 1] = Parts[j]; j--; }
                Parts[j + 1] = x;
            }
        }

        public bool Has(string shortName)
        {
            foreach (var p in Parts) if (string.Equals(p.ShortName, shortName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool HasCategoryPart(char c) => HasCategory(c);
    }
}
