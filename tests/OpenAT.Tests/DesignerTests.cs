using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Data;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Aircraft constructor and designer (rooms-and-ui-7 §3, §6; planes-and-flights.md §5).</summary>
    public class DesignerTests
    {
        // Editor bitmap sizes of the parts used below (room/editor.gli).
        static readonly Dictionary<string, (int, int)> Sizes = new Dictionary<string, (int, int)>
        {
            ["B1"] = (146, 107), ["B2"] = (292, 183), ["C1"] = (82, 82), ["H1"] = (173, 198),
            ["R4"] = (231, 139), ["L4"] = (200, 153), ["M1"] = (87, 63),
        };

        static (int, int) Size(string n) => Sizes.TryGetValue(n, out var s) ? s : (50, 50);

        static PlaneDesignCatalog Catalog()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            var inst = GameInstall.TryLocate();
            return inst != null ? PlaneDesignCatalog.Load(inst) : cat;
        }

        static PlaneDesign Small(PlaneDesignCatalog cat)
        {
            var d = new PlaneDesign { Name = "Weiße Möwe" };
            foreach (var id in new[] { 100, 200, 300, 400, 600, 1000, 1010 }) d.AddById(cat, id);
            return d;
        }

        [Fact]
        public void PlaneFile_RoundTripKeepsPartsAndCp1252Name()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            var d = Small(cat);
            for (int i = 0; i < d.Parts.Count; i++) { d.Parts[i].Pos3dX = 10 * i; d.Parts[i].Pos2dY = -i; }
            var bytes = d.WritePln(cat);
            // Preview header: size = 4 + 6·4 + name length, then cost.
            Assert.Equal(4 + 24 + d.Name.Length, System.BitConverter.ToInt32(bytes, 0));
            Assert.Equal((int)d.Stats(cat).Cost, System.BitConverter.ToInt32(bytes, 4));
            Assert.Equal(0xDF, bytes[4 + 7 * 4 + 3]);   // 'ß' stored as its single CP1252 byte
            var back = PlaneDesign.ReadPln(bytes);
            Assert.Equal(d.Name, back.Name);
            Assert.Equal(d.Parts.Count, back.Parts.Count);
            for (int i = 0; i < d.Parts.Count; i++)
            {
                Assert.Equal(d.Parts[i].ShortName, back.Parts[i].ShortName);
                Assert.Equal(d.Parts[i].ParentShortName, back.Parts[i].ParentShortName);
                Assert.Equal(d.Parts[i].RelationIndex, back.Parts[i].RelationIndex);
                Assert.Equal(d.Parts[i].Pos3dX, back.Parts[i].Pos3dX);
                Assert.Equal(d.Parts[i].Pos2dY, back.Parts[i].Pos2dY);
            }
            Assert.True(back.IsBuildable(cat));
        }

        [Fact]
        public void PlaneFile_ShippedAiDesignsLoadAndRewrite()
        {
            var inst = GameInstall.TryLocate();
            if (inst == null) return;
            var data = GameData.Load(inst);
            Assert.True(data.MissionDesigns.ContainsKey(45));
            Assert.True(data.MissionDesigns.ContainsKey(48));
            var cat = data.DesignCatalog;
            var d = data.MissionDesigns[45];
            Assert.Equal(60100000, d.Stats(cat).Cost);   // data-tables.md §2.4 example
            Assert.True(d.IsBuildable(cat));
            var again = PlaneDesign.ReadPln(d.WritePln(cat));
            Assert.Equal(d.Parts.Select(p => p.ShortName + p.RelationIndex), again.Parts.Select(p => p.ShortName + p.RelationIndex));
        }

        [Fact]
        public void NewGame_DesignerMissionGivesTheAiItsDesign()
        {
            var inst = GameInstall.TryLocate();
            if (inst == null) return;
            var data = GameData.Load(inst);
            var w = World.NewGame(data, new NewGameSettings { Difficulty = 45 });
            Assert.NotNull(w.AI.MissionDesign);
            Assert.Same(data.DesignCatalog, w.DesignCatalog);
            var w2 = World.NewGame(data, new NewGameSettings { Difficulty = 1 });
            Assert.Null(w2.AI.MissionDesign);
        }

        [Fact]
        public void Editor_StripSlotsFollowThePlacedParts()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            var ed = new PlaneDesignEditor(cat, new PlaneDesign(), Size);
            Assert.Equal(new[] { true, false, false, false, false }, ed.EnabledSlots());
            ed.Design.AddById(cat, 100);
            Assert.Equal(new[] { false, true, true, true, false }, ed.EnabledSlots());
            ed.Design.AddById(cat, 300);
            Assert.Equal(new[] { false, true, false, true, true }, ed.EnabledSlots());   // a tail enables engines
            ed.Design.AddById(cat, 600);
            Assert.False(ed.EnabledSlots()[PlaneDesignEditor.SlotWing]);
            // B1 skips wings R1, R2, R3, R6 (§6 table): from R1 forward the first usable one is R4.
            var ed2 = new PlaneDesignEditor(cat, new PlaneDesign(), Size);
            ed2.Design.AddById(cat, 100);
            Assert.Equal(3, ed2.SkipUnusableWing(0, 1, true));
            Assert.Equal(3, ed2.SkipUnusableWing(5, 1, true));     // R6, R1, R2, R3 unusable → R4
            Assert.Equal(4, ed2.SkipUnusableWing(4, -1, true));
            Assert.Equal(0, ed2.SkipUnusableWing(0, 1, false));
        }

        [Fact]
        public void Editor_SnapPlaceTwinAndCascade()
        {
            var cat = Catalog();
            var ed = new PlaneDesignEditor(cat, new PlaneDesign(), Size);
            // Body: desk relation 100 at offset3d (320,190) minus half the bitmap (top-left compared with the mouse).
            int bx = cat.Relations[0].Offset3dX - 73, by = cat.Relations[0].Offset3dY - 53;
            var s = ed.FindSnap("B1", bx + 5, by + 5);
            Assert.Equal(0, s.Relation);
            Assert.Equal((bx, by), (s.Pos3dX, s.Pos3dY));
            Assert.Null(ed.Place("B1", s, false));
            // Far away from every target: no snap, the part hangs centred on the mouse.
            var none = ed.FindSnap("C1", 5, 430);
            Assert.False(none.Snapped);
            Assert.Equal((5 - 41, 430 - 41), (none.Pos3dX, none.Pos3dY));

            // Right wing R4 at its relation 400: the left twin L4 comes along (relation index + 1).
            var r400 = cat.Relations.First(r => r.Id == 400);
            var w = ed.FindSnap("R4", bx + r400.Offset3dX + 115, by + r400.Offset3dY + 69);
            Assert.Equal(r400.Index, w.Relation);
            Assert.Equal(r400.Index + 1, w.TwinRelation);
            ed.Place("R4", w, false);
            Assert.Equal(3, ed.Design.Parts.Count);
            Assert.True(ed.Design.Has("L4"));

            // Engine M1 on R4 (relation 1000) with its twin on L4 (1010); engines stay on the cursor.
            var r1000 = cat.Relations.First(r => r.Id == 1000);
            var rw = ed.Design.Parts.First(p => p.ShortName == "R4");
            var m = ed.FindSnap("M1", rw.Pos3dX + r1000.Offset3dX + 43, rw.Pos3dY + r1000.Offset3dY + 31);
            Assert.Equal(r1000.Index, m.Relation);
            Assert.Equal(1010, cat.Relations[m.TwinRelation].Id);
            Assert.Equal("M1", ed.Place("M1", m, false));
            Assert.NotEqual(r1000.Index, ed.FindSnap("M1", rw.Pos3dX + r1000.Offset3dX + 43, rw.Pos3dY + r1000.Offset3dY + 31).Relation);   // slot taken
            Assert.Equal(5, ed.Design.Parts.Count);

            // Draw order: ascending z-base + y + zAdd.
            var keys = ed.Design.Parts.Select(p => PlanePart.ZBaseOf(p.ShortName[0]) + p.Pos3dY + cat.Relations[p.RelationIndex].ZAdd).ToList();
            Assert.Equal(keys.OrderBy(k => k), keys);

            // Picking up the left wing removes the right wing too (id −200) and both engines (orphans).
            int li = ed.Design.Parts.FindIndex(p => p.ShortName == "L4");
            Assert.False(ed.NeedsClearConfirm(li));
            Assert.Equal("R4", ed.Remove(li));
            Assert.Equal(new[] { "B1" }, ed.Design.Parts.Select(p => p.ShortName));
            Assert.False(ed.NeedsClearConfirm(0));   // body alone: no confirmation
        }

        [Fact]
        public void Editor_RemovingOneEngineTakesItsMirror()
        {
            var cat = PlaneDesignCatalog.CreateDefault();
            var d = new PlaneDesign();
            foreach (var id in new[] { 100, 300, 400, 600, 1000, 1010 }) d.AddById(cat, id);
            var ed = new PlaneDesignEditor(cat, d, Size);
            int i = d.Parts.FindIndex(p => p.ShortName == "M1");
            Assert.Equal("M1", ed.Remove(i));
            Assert.DoesNotContain(d.Parts, p => p.ShortName == "M1");
            Assert.Equal(4, d.Parts.Count);
            ed.RemoveCategory(PlaneDesignEditor.SlotWing);
            Assert.Equal(new[] { "B1", "H1" }, d.Parts.Select(p => p.ShortName));
            Assert.True(ed.NeedsClearConfirm(0));
        }

        [Fact]
        public void Editor_Labels()
        {
            Assert.Equal(8400, PlaneDesignEditor.NoiseLabel(0));
            Assert.Equal(8401, PlaneDesignEditor.NoiseLabel(1));
            Assert.Equal(8406, PlaneDesignEditor.NoiseLabel(101));
            Assert.Equal(8508, PlaneDesignEditor.MaintenanceLabel(-30));
            Assert.Equal(8507, PlaneDesignEditor.MaintenanceLabel(-29));
            Assert.Equal(8504, PlaneDesignEditor.MaintenanceLabel(0));
            Assert.Equal(8500, PlaneDesignEditor.MaintenanceLabel(111));
        }

        [Fact]
        public void Library_FilesOrderNumberingAndBuildableList()
        {
            var dir = Path.Combine(Path.GetTempPath(), "openat-designs-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var cat = PlaneDesignCatalog.CreateDefault();
                var lib = new PlaneDesignLibrary(dir, cat);
                Assert.Empty(lib.Files());
                Assert.Null(lib.Next("data.plane", 1));
                Assert.Equal("data.plane", lib.NewFileName());
                Assert.True(lib.Save("data.plane", Small(cat)));
                Assert.Equal("data1.plane", lib.NewFileName());
                Assert.Equal("1", PlaneDesignLibrary.NumberPart("data1.plane"));
                var half = new PlaneDesign { Name = "half" };
                half.AddById(cat, 100);
                lib.Save("data1.plane", half);
                lib.Save("Alpha.plane", Small(cat));
                Assert.Equal(new[] { "Alpha.plane", "data.plane", "data1.plane" }, lib.Files());
                Assert.Equal("Alpha.plane", lib.Next("data1.plane", 1));     // wraps
                Assert.Equal("data1.plane", lib.Next("Alpha.plane", -1));
                Assert.Equal("Alpha.plane", lib.Next("gone.plane", 1));      // unknown → first
                Assert.Equal(new[] { "Alpha.plane", "data.plane" }, lib.Buildable().Select(b => b.file));
                Assert.Equal("Weiße Möwe", lib.Load("data.plane").Name);
                File.WriteAllBytes(lib.PathOf("empty.plane"), new byte[0]);
                Assert.Empty(lib.Load("empty.plane").Parts);
                lib.Delete("data.plane");
                Assert.False(lib.Exists("data.plane"));
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }
}
