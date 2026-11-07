using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Save;
using Xunit;
using Xunit.Abstractions;

namespace OpenAT.Tests
{
    // ---------------------------------------------------------------------- test extension state

    public sealed class SaveTestShape { public int Sides; public string Label; }
    public sealed class SaveTestCircle { public double Radius; }

    /// <summary>An extension's state: polymorphic fields, shared references, a [NotSaved] cache, hooks.</summary>
    public sealed class SaveTestExtension : ISaveHooks
    {
        public const string Key = "tests.save-extension";
        public object Anything;
        public SaveTestShape A, B;
        public readonly List<object> Mixed = new List<object>();
        public readonly Dictionary<int, SaveTestShape> ById = new Dictionary<int, SaveTestShape>();
        public int[,] Grid = new int[2, 3];
        public long? Maybe;
        public DayState State;
        public Plane Plane;
        public City City;
        [NotSaved] public int Cache = 77;
        public Action Callback;
        int _private = 5;
        public int Private { get => _private; set => _private = value; }
        [NotSaved] public int BeforeSaveCalls, AfterLoadCalls;

        void ISaveHooks.OnBeforeSave(World w) => BeforeSaveCalls++;
        void ISaveHooks.OnAfterLoad(World w) { AfterLoadCalls++; Cache = 1000 + A.Sides; }
    }

    /// <summary>Save games (sim-core.md §5, rooms-and-ui-4 §4.3): header, graph round trip, determinism after load.</summary>
    public class SaveGameTests
    {
        readonly ITestOutputHelper _out;
        public SaveGameTests(ITestOutputHelper output) => _out = output;

        static GameInstall _install;
        static GameData _plain, _withHall;
        static readonly object Lock = new object();

        static GameData LoadData(bool withHall)
        {
            lock (Lock)
            {
                _install = _install ?? GameInstall.TryLocate();
                if (_install == null) return null;
                if (!withHall && _plain != null) return _plain;
                if (withHall && _withHall != null) return _withHall;
                var d = GameData.Load(_install);
                FinanceData.Load(d, _install);
                if (withHall)
                {
                    HallModule.Register();
                    HallContent.Attach(d, HallData.Load(_install));
                    _withHall = d;
                }
                else _plain = d;
                return d;
            }
        }

        static HeadlessRunner NewRunner(GameData d, int mission, int seed, bool hall)
        {
            var r = HeadlessRunner.NewGame(d, mission, seed);
            if (hall && r.World.Hall == null) HallModule.EnsureHall(r.World);
            try { r.World.DesignCatalog = PlaneDesignCatalog.Load(_install); } catch { /* default catalogue */ }
            r.World.Extensions[SaveTestExtension.Key] = MakeExtension(r.World);
            return r;
        }

        static SaveTestExtension MakeExtension(World w)
        {
            var shared = new SaveTestShape { Sides = 3, Label = "tri" };
            var x = new SaveTestExtension
            {
                Anything = new SaveTestCircle { Radius = 2.5 }, A = shared, B = shared, Maybe = 42, State = DayState.Evening,
                City = w.Data.Cities.All[3], Callback = () => { }, Private = 9,
            };
            x.Mixed.Add(shared); x.Mixed.Add(17); x.Mixed.Add(17L); x.Mixed.Add("text"); x.Mixed.Add(null); x.Mixed.Add(DayState.Night);
            x.ById[1] = shared;
            x.ById[2] = new SaveTestShape { Sides = 4 };
            x.Grid[1, 2] = 12;
            foreach (var p in w.Players) foreach (var pl in p.Planes.Items) { x.Plane = pl; break; }
            return x;
        }

        static World RoundTrip(World w, out long saveMs, out long loadMs, out int bytes)
        {
            var ms = new MemoryStream();
            var sw = Stopwatch.StartNew();
            SaveGame.Write(w, ms, "TEST");
            saveMs = sw.ElapsedMilliseconds;
            bytes = (int)ms.Length;
            ms.Position = 0;
            sw.Restart();
            var w2 = SaveGame.Read(ms, w.Data, out var warnings);
            loadMs = sw.ElapsedMilliseconds;
            Assert.Empty(warnings);
            return w2;
        }

        /// <summary>The state hash with the restore counter (the one field a load changes) aligned.</summary>
        static string Hash(World w, int restoreCount, bool machinery = false)
        {
            int keep = w.RestoreCount;
            w.RestoreCount = restoreCount;
            try { return SaveGame.StateHash(w, machinery); }
            finally { w.RestoreCount = keep; }
        }

        static void AssertSameRows(int day, AirlineDayRow[] a, AirlineDayRow[] b)
        {
            for (int c = 0; c < Player.Count; c++)
            {
                string where = $"day {day} P{c}";
                Assert.True(a[c].Money == b[c].Money, where + " money " + a[c].Money + " vs " + b[c].Money);
                Assert.True(a[c].Credit == b[c].Credit, where + " credit");
                Assert.True(a[c].Planes == b[c].Planes, where + " planes");
                Assert.True(a[c].Flights == b[c].Flights, where + " flights");
                Assert.True(a[c].Contracts == b[c].Contracts, where + " contracts");
                Assert.True(a[c].Freights == b[c].Freights, where + " freights");
                Assert.True(a[c].RouteLegs == b[c].RouteLegs, where + " routes");
                Assert.True(a[c].Branches == b[c].Branches, where + " branches");
                Assert.True(a[c].Image == b[c].Image, where + " image");
                Assert.True(a[c].SharePrice.Equals(b[c].SharePrice), where + " share price");
                Assert.True(a[c].Actions == b[c].Actions, where + " robot actions");
                Assert.True(a[c].Out == b[c].Out, where + " out");
            }
        }

        /// <summary>Deep state equality (restore counter aligned); on failure shows the first differing lines of the dumps.</summary>
        void AssertSameState(World a, World b, bool machinery = true)
        {
            int rc = b.RestoreCount;
            if (Hash(a, rc, machinery) == Hash(b, rc, machinery)) return;
            int keep = a.RestoreCount;
            a.RestoreCount = rc;
            var da = SaveGame.Dump(a, machinery).Split('\n');
            a.RestoreCount = keep;
            var db = SaveGame.Dump(b, machinery).Split('\n');
            int shown = 0;
            for (int i = 0; i < Math.Max(da.Length, db.Length) && shown < 20; i++)
            {
                string x = i < da.Length ? da[i] : "<end>", y = i < db.Length ? db[i] : "<end>";
                if (x == y) continue;
                _out.WriteLine("- " + x);
                _out.WriteLine("+ " + y);
                shown++;
            }
            Assert.Fail("state differs (see output)");
        }

        /// <summary>Runs the original and the loaded world side by side and compares rows and deep state every day.</summary>
        void Lockstep(HeadlessRunner a, HeadlessRunner b, int days, bool skipFirstRows = false)
        {
            int rc = b.World.RestoreCount;
            for (int d = 0; d < days; d++)
            {
                var ra = a.RunDay();
                var rb = b.RunDay();
                // A runner created mid-day counts today's flights/actions from its creation, not from 9:00.
                if (!(skipFirstRows && d == 0)) AssertSameRows(a.World.Clock.Date, ra, rb);
                Assert.Equal(Hash(a.World, rc), Hash(b.World, rc));
            }
            Assert.Equal(Hash(a.World, rc, true), Hash(b.World, rc, true));
            _out.WriteLine(a.Table());
        }

        // ------------------------------------------------------------------ the oracle: determinism after load

        [Theory]
        [InlineData(false, Mission.FreeGame, 12345)]
        [InlineData(true, Mission.FreeGame, 777)]
        [InlineData(true, 3, 4242)]
        public void SaveLoad_ThenTenDaysInLockstep(bool hall, int mission, int seed)
        {
            var d = LoadData(hall);
            if (d == null) return;
            var a = NewRunner(d, mission, seed, hall);
            Assert.Equal(hall, a.World.Hall != null);
            a.RunDays(5);

            var w2 = RoundTrip(a.World, out long saveMs, out long loadMs, out int bytes);
            _out.WriteLine($"hall {hall} mission {mission}: {bytes} bytes, save {saveMs} ms, load {loadMs} ms");
            Assert.Equal(1, w2.RestoreCount);
            Assert.Equal(hall, w2.Hall != null);
            AssertSameState(a.World, w2);

            // The loaded world has its own (fresh) machinery and data references.
            Assert.Same(d, w2.Data);
            Assert.NotSame(a.World.Rules, w2.Rules);
            Assert.Same(w2, w2.Players[2].World);
            if (hall) Assert.Same(w2, w2.Hall.World);

            var b = new HeadlessRunner(w2);
            Lockstep(a, b, 10);
        }

        [Fact]
        public void AutosaveAt17_MidDaySave_ThenLockstep()
        {
            var d = LoadData(true);
            if (d == null) return;
            var a = NewRunner(d, Mission.FreeGame, 99, true);
            a.RunDays(3);
            var w = a.World;
            var requests = new List<AutosaveRequested>();
            w.Events.Subscribe<AutosaveRequested>(e => requests.Add(e));
            // Play the 4th day up to the 17:00 autosave request; save after the tick (as the view does).
            long guard = 0;
            while (requests.Count == 0 && guard++ < 100000)
            {
                if (w.Clock.State == DayState.Morning && w.Clock.Frozen) w.Execute(new StartWorkingDayCommand());
                w.Tick();
            }
            Assert.Single(requests);
            Assert.Equal(AutosaveReason.FiveOClock, requests[0].Reason);
            Assert.Equal(SaveGame.AutosaveSlot, requests[0].Slot);
            Assert.Equal(17, w.Clock.Hour);
            Assert.Equal(0, w.Clock.Minute);

            var w2 = RoundTrip(w, out _, out _, out _);
            AssertSameState(w, w2);
            var b = new HeadlessRunner(w2);
            Lockstep(a, b, 10, skipFirstRows: true);
        }

        // ------------------------------------------------------------------ header

        [Fact]
        public void Header_IsReadWithoutTheBody()
        {
            var d = LoadData(false);
            if (d == null) return;
            var a = NewRunner(d, 2, 5, false);
            a.RunDays(2);
            var w = a.World;
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "MY SLOT 1");
            long total = ms.Length;

            ms.Position = 0;
            var h = SaveGame.ReadHeader(ms);
            Assert.True(ms.Position < 1000 && ms.Position < total / 10, "header read " + ms.Position + " of " + total + " bytes");
            Assert.Equal("MY SLOT 1", h.Name);
            Assert.Equal(2, h.Mission);
            Assert.Equal(w.Clock.Date, h.Date);
            Assert.Equal(w.Clock.DisplayWeekday, h.Weekday);
            Assert.Equal(w.Clock.CalendarDate.Day, h.Day);
            Assert.Equal(w.Clock.CalendarDate.Month, h.Month);
            Assert.Equal(w.Local.Airline, h.Airline);
            Assert.Equal(w.Players[3].Airline, h.Airlines[3]);
            Assert.Equal(Owner.Computer, h.Owners[1]);
            Assert.Equal(d.Cities.Count, h.CityCount);
            Assert.Equal(SaveGame.FormatVersion, h.FormatVersion);
            Assert.Equal(SaveGame.ProgramVersion, h.ProgramVersion);
            Assert.Equal(" 9:00", h.ClockText);
        }

        [Fact]
        public void Header_MissingOrForeignFile_IsAnEmptySlot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "openat-save-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Null(SaveGame.TryReadHeaderFile(Path.Combine(dir, SaveGame.SlotFileName(3))));
                string junk = Path.Combine(dir, SaveGame.SlotFileName(4));
                File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
                Assert.Null(SaveGame.TryReadHeaderFile(junk));
                Assert.Throws<SaveException>(() => SaveGame.ReadHeader(new MemoryStream(new byte[] { (byte)'O', (byte)'A' })));

                var w = OpsWorld.NewWorld();
                string path = Path.Combine(dir, SaveGame.SlotFileName(SaveGame.AutosaveSlot));
                SaveGame.WriteFile(w, path, "AUTO");
                Assert.Equal("AUTO", SaveGame.TryReadHeaderFile(path).Name);
                Assert.Equal("Game11.sav", SaveGame.SlotFileName(SaveGame.AutosaveSlot));
                var w2 = SaveGame.ReadFile(path, w.Data);
                Assert.Equal(SaveGame.StateHash(w), Hash(w2, 0));
            }
            finally { Directory.Delete(dir, true); }
        }

        // ------------------------------------------------------------------ serializer rules (no install needed)

        [Fact]
        public void Graph_PolymorphismIdentityContentAndSkips()
        {
            var w = OpsWorld.NewWorld();
            var x = MakeExtension(w);
            w.Extensions[SaveTestExtension.Key] = x;
            w.Players[1].Extensions["tests.p1"] = new SaveTestShape { Sides = 8 };
            w.Options.FixLoanInterest = true;

            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "x");
            Assert.Equal(1, x.BeforeSaveCalls);
            ms.Position = 0;
            var w2 = SaveGame.Read(ms, w.Data);

            var y = (SaveTestExtension)w2.Extensions[SaveTestExtension.Key];
            Assert.NotSame(x, y);
            Assert.Equal(2.5, ((SaveTestCircle)y.Anything).Radius);
            Assert.Same(y.A, y.B);
            Assert.Same(y.A, y.Mixed[0]);
            Assert.Same(y.A, y.ById[1]);
            Assert.Equal(4, y.ById[2].Sides);
            Assert.Equal(17, Assert.IsType<int>(y.Mixed[1]));
            Assert.Equal(17L, Assert.IsType<long>(y.Mixed[2]));
            Assert.Equal("text", y.Mixed[3]);
            Assert.Null(y.Mixed[4]);
            Assert.Equal(DayState.Night, y.Mixed[5]);
            Assert.Equal(12, y.Grid[1, 2]);
            Assert.Equal(42L, y.Maybe);
            Assert.Equal(DayState.Evening, y.State);
            Assert.Equal(9, y.Private);
            // Content is referenced by id: the very object of the game data.
            Assert.Same(w.Data.Cities.All[3], y.City);
            // Not saved: delegates and [NotSaved] fields (the hook rebuilt the cache).
            Assert.Null(y.Callback);
            Assert.Equal(1003, y.Cache);
            Assert.Equal(1, y.AfterLoadCalls);
            Assert.Equal(8, ((SaveTestShape)w2.Players[1].Extensions["tests.p1"]).Sides);
            Assert.True(w2.Options.FixLoanInterest);
            // Machinery is fresh.
            Assert.NotNull(w2.Systems.Find(AutosaveSystem.SystemName));
            Assert.NotNull(w2.Distances);
        }

        [Fact]
        public void Graph_SharedPlaneReferencesStayShared()
        {
            var w = OpsWorld.NewWorld();
            var pl = new Plane { Name = "Shared" };
            w.Players[0].Planes.Add(pl);
            w.UsedPlanes[1] = pl;
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "x");
            ms.Position = 0;
            var w2 = SaveGame.Read(ms, w.Data);
            var p2 = w2.Players[0].Planes.Find(pl.Id);
            Assert.NotNull(p2);
            Assert.Same(p2, w2.UsedPlanes[1]);
            Assert.Equal("Shared", p2.Name);
        }

        [Fact]
        public void Load_ClearsCallItADayBeforeSix_AndCountsRestores()
        {
            var w = OpsWorld.NewWorld();
            foreach (var p in w.Players) p.Owner = p.Index == 0 ? Owner.Human : Owner.Computer;
            w.Clock.Time = GameClock.At(15);
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            var got = new List<AutosaveRequested>();
            w.Events.Subscribe<AutosaveRequested>(e => got.Add(e));
            Assert.True(w.Execute(new CallItADayCommand(0)).Ok);
            Assert.True(w.CallItADay);
            Assert.Single(got);
            Assert.Equal(AutosaveReason.CallItADay, got[0].Reason);

            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "x");
            ms.Position = 0;
            var w2 = SaveGame.Read(ms, w.Data);
            Assert.False(w2.CallItADay);
            Assert.False(w2.Players[0].CalledItADay);
            Assert.Equal(1, w2.RestoreCount);
            ms = new MemoryStream();
            SaveGame.Write(w2, ms, "x");
            ms.Position = 0;
            Assert.Equal(1, SaveGame.ReadHeader(ms).RestoreCount);
            ms.Position = 0;
            Assert.Equal(2, SaveGame.Read(ms, w.Data).RestoreCount);
        }

        [Fact]
        public void Autosave_NotRequestedWhenOff()
        {
            var w = OpsWorld.NewWorld();
            w.Options.Autosave = false;
            w.Clock.Time = GameClock.At(16, 59) + 990;
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            int n = 0;
            w.Events.Subscribe<AutosaveRequested>(e => n++);
            w.Tick();
            Assert.Equal(17, w.Clock.Hour);
            Assert.Equal(0, n);
            w.Options.Autosave = true;
            w.Clock.Time = GameClock.At(16, 59) + 990;
            w.Tick();
            Assert.Equal(1, n);
        }

        [Fact]
        public void UnsupportedType_FailsWithItsPath()
        {
            var w = OpsWorld.NewWorld();
            w.Extensions["tests.bad"] = new System.Text.StringBuilder("x");
            var e = Assert.Throws<SaveException>(() => SaveGame.Write(w, new MemoryStream(), "x"));
            Assert.Contains("Extensions.[tests.bad]", e.Message);
            Assert.Contains("StringBuilder", e.Message);
        }

        [Fact]
        public void UnknownSavedType_IsSkippedWithAWarning()
        {
            var w = OpsWorld.NewWorld();
            w.Extensions["tests.gone"] = new SaveTestShape { Sides = 6, Label = "hex" };
            w.Extensions["tests.kept"] = new SaveTestCircle { Radius = 1 };
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "x");
            // Rename the type in the file (same length): as if the extension's code had been removed.
            var bytes = ms.ToArray();
            var from = System.Text.Encoding.UTF8.GetBytes("OpenAT.Tests.SaveTestShape");
            var to = System.Text.Encoding.UTF8.GetBytes("OpenAT.Tests.SaveTestShapX");
            int at = IndexOf(bytes, from);
            Assert.True(at > 0);
            Array.Copy(to, 0, bytes, at, to.Length);
            var w2 = SaveGame.Read(new MemoryStream(bytes), w.Data, out var warnings);
            Assert.Contains(warnings, s => s.Contains("SaveTestShapX"));
            Assert.Null(w2.Extensions["tests.gone"]);
            Assert.Equal(1, ((SaveTestCircle)w2.Extensions["tests.kept"]).Radius);
        }

        [Fact]
        public void RenamedOrNewField_KeepsItsDefault_OtherFieldsLoad()
        {
            var w = OpsWorld.NewWorld();
            w.Extensions["tests.shape"] = new SaveTestShape { Sides = 6, Label = "hex" };
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "x");
            // The file's field "Label" becomes "Labex": an old field the code no longer has, and a new
            // field ("Label") the save does not have.
            var bytes = ms.ToArray();
            int at = IndexOf(bytes, System.Text.Encoding.UTF8.GetBytes("Label"));
            Assert.True(at > 0);
            bytes[at + 4] = (byte)'x';
            var w2 = SaveGame.Read(new MemoryStream(bytes), w.Data, out var warnings);
            var s = (SaveTestShape)w2.Extensions["tests.shape"];
            Assert.Equal(6, s.Sides);
            Assert.Null(s.Label);
            Assert.Empty(warnings);
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        [Fact]
        public void Audit_StateTypesKeepTheirDefaultsOnLoad()
        {
            var d = LoadData(true);
            if (d == null) return;
            var a = NewRunner(d, Mission.FreeGame, 8, true);
            a.RunDays(2);
            var issues = SaveGame.Audit(a.World);
            foreach (var i in issues) _out.WriteLine(i);
            Assert.Empty(issues);
        }

        // ------------------------------------------------------------------ performance

        [Fact]
        public void SaveAndLoad_TypicalGame_Under200ms()
        {
            var d = LoadData(true);
            if (d == null) return;
            var a = NewRunner(d, Mission.FreeGame, 31, true);
            a.RunDays(8);
            RoundTrip(a.World, out _, out _, out _); // warm-up (JIT, reflection caches)
            long best = long.MaxValue;
            int bytes = 0;
            for (int i = 0; i < 3; i++)
            {
                RoundTrip(a.World, out long s, out long l, out bytes);
                best = Math.Min(best, s + l);
                _out.WriteLine($"save {s} ms, load {l} ms, {bytes} bytes");
            }
            var m = SaveGame.Measure(a.World);
            _out.WriteLine($"{m.Objects} objects, {m.Types} types");
            Assert.True(best < 200, "save+load took " + best + " ms");
        }
    }
}
