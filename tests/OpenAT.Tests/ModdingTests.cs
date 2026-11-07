using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Modding;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Save;
using Xunit;
using Xunit.Abstractions;

namespace OpenAT.Tests
{
    // ---------------------------------------------------------------------- a code mod compiled into the tests

    /// <summary>Saved state of <see cref="TestCounterMod"/>.</summary>
    public sealed class TestCounterState
    {
        public int Days;
        public int Landings;
        public List<int> Dates = new List<int>();
    }

    /// <summary>Per-airline state of <see cref="TestCounterMod"/>.</summary>
    public sealed class TestCounterPlayerState { public int Mornings; }

    /// <summary>Uses most of the ModContext API: tunable, system, rule decorator, event, command, player state.</summary>
    public sealed class TestCounterMod : IOpenATMod
    {
        public static int Loads;

        public void OnLoad(ModContext ctx)
        {
            Loads++;
            ctx.DefineTunable("test.counter.step", 1, integer: true);
            ctx.SetTunable(TunableKeys.PlaneWearPercent, 50);
            ctx.AddSystem(() => new CounterSystem(ctx));
            ctx.DecorateRule<IKerosenePriceRule>(inner => new FixedKerosene(inner));
            ctx.Subscribe<DayChanged>((w, e) => ctx.State<TestCounterState>(w).Dates.Add(e.NewDate));
            ctx.RegisterCommand("test.add", (w, args) => new AddDaysCommand(ctx.StateKey, int.Parse(args[0])));
            ctx.EditNewGameOptions(o => o.FixLoanInterest = true);
            ctx.RegisterRoom(1000, () => new object(), "Test room");
        }

        sealed class CounterSystem : SimSystem
        {
            readonly ModContext _ctx;
            public CounterSystem(ModContext ctx) => _ctx = ctx;
            public override string Name => "test.counter";
            public override int Order => 2500;
            public override void OnNewDay(World w) => _ctx.State<TestCounterState>(w).Days += w.Tunables.Int("test.counter.step");
            public override void OnPlayerNewDay(World w, Player p) => _ctx.PlayerState<TestCounterPlayerState>(p).Mornings++;
        }

        /// <summary>Decorator: the market price never goes below 400.</summary>
        public sealed class FixedKerosene : IKerosenePriceRule
        {
            readonly IKerosenePriceRule _inner;
            public FixedKerosene(IKerosenePriceRule inner) => _inner = inner;
            public int NextPrice(World w, int price) => Math.Max(400, _inner.NextPrice(w, price));
        }

        sealed class AddDaysCommand : ICommand<World>
        {
            readonly string _key; readonly int _n;
            public AddDaysCommand(string key, int n) { _key = key; _n = n; }
            public CommandResult Validate(World w) => _n > 0 ? CommandResult.Success : CommandResult.Fail("n");
            public void Apply(World w) => ModState.Get<TestCounterState>(w, _key).Days += _n;
        }
    }

    public sealed class ThrowingOnLoadMod : IOpenATMod
    {
        public void OnLoad(ModContext ctx)
        {
            ctx.SetTunable(TunableKeys.KeroseneMax, 999);   // must be discarded: OnLoad fails afterwards
            throw new InvalidOperationException("broken on purpose");
        }
    }

    public sealed class ThrowingSystemMod : IOpenATMod
    {
        public static int Calls;
        public void OnLoad(ModContext ctx) => ctx.AddSystem(() => new Boom());
        sealed class Boom : SimSystem
        {
            public override string Name => "test.boom";
            public override int Order => 100;
            public override void OnHour(World w) { Calls++; throw new Exception("boom"); }
        }
    }

    public sealed class ThrowingWorldHookMod : IOpenATMod
    {
        public void OnLoad(ModContext ctx) => ctx.OnWorldCreated(w => throw new Exception("hook fails"));
    }

    /// <summary>The modding system (docs/MODDING.md): loader, data mods, code mods, saves, isolation, determinism.</summary>
    public class ModdingTests
    {
        readonly ITestOutputHelper _out;
        public ModdingTests(ITestOutputHelper output) => _out = output;

        static string Repo([CallerFilePath] string here = null) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", ".."));
        static string Examples => Path.Combine(Repo(), "Mods.Examples");

        static string TempDir(string name)
        {
            var d = Path.Combine(Path.GetTempPath(), "openat-modtests", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static string WriteMod(string root, string folder, string manifest, params (string file, string text)[] files)
        {
            var dir = Path.Combine(root, folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
            foreach (var (file, text) in files)
            {
                var p = Path.Combine(dir, file);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, text);
            }
            return dir;
        }

        static ModManifest Manifest(string id) => new ModManifest { Id = id, Version = "1.0", Directory = "." };

        /// <summary>A synthetic world (no install) built from <paramref name="d"/> (mods applied to it first).</summary>
        static World SynthWorld(GameData d)
        {
            var w = new World(d, new GameOptions { RandomStartDay = false });
            w.HomeCity = OpsWorld.C(1);
            w.Clock.InitCalendar(0);
            w.Clock.Time = GameClock.DayStart;
            w.EnsureOperationsArrays();
            RouteOps.UpdateDemand(w);
            return w;
        }

        // ------------------------------------------------------------------ JSON

        [Fact]
        public void Json_ParsesCommentsTrailingCommasEscapes()
        {
            var o = Json.ParseObject("{ // c\n \"a\": 1.5, \"b\": [1, 2, ], /* x */ \"c\": \"q\\\"\\u0041\", \"d\": true, \"e\": null, }");
            Assert.Equal(1.5, o.Num("a"));
            Assert.Equal(2, o.Arr("b").Count);
            Assert.Equal("q\"A", o.Str("c"));
            Assert.True(o.Bool("d"));
            Assert.True(o.Has("e"));
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, o.Entries.Select(e => e.Key).ToArray());
            var ex = Assert.Throws<FormatException>(() => Json.Parse("{\n \"a\": [1 2] }"));
            Assert.Contains("line 2", ex.Message);
        }

        // ------------------------------------------------------------------ discovery and order

        [Fact]
        public void Loader_OrdersByDependenciesThenLoadOrderThenId_AndIsolatesBrokenMods()
        {
            var root = TempDir("order");
            WriteMod(root, "Zeta", "{ \"id\": \"zeta\", \"loadOrder\": -5 }");
            WriteMod(root, "Alpha", "{ \"id\": \"alpha\", \"dependencies\": [\"zeta\", \"beta>=2.0\"] }");
            WriteMod(root, "Beta", "{ \"id\": \"beta\", \"version\": \"2.1\" }");
            WriteMod(root, "Gamma", "{ \"id\": \"gamma\", \"dependencies\": [\"nothere\"] }");
            WriteMod(root, "Delta", "{ \"id\": \"delta\", \"dependencies\": [\"gamma\"] }");           // fails through gamma
            WriteMod(root, "Eps", "{ \"id\": \"eps\", \"dependencies\": [{ \"id\": \"nothere\", \"optional\": true }] }");
            WriteMod(root, "Off", "{ \"id\": \"off\", \"enabled\": false }");
            WriteMod(root, "Bad", "{ \"id\": \"bad\", ");                                              // broken JSON
            WriteMod(root, "Dup", "{ \"id\": \"beta\" }");
            WriteMod(root, "Cyc1", "{ \"id\": \"cyc1\", \"dependencies\": [\"cyc2\"] }");
            WriteMod(root, "Cyc2", "{ \"id\": \"cyc2\", \"dependencies\": [\"cyc1\"] }");
            WriteMod(root, "Old", "{ \"id\": \"old\", \"dependencies\": [\"beta>=3\"] }");
            WriteMod(root, "BadData", "{ \"id\": \"baddata\" }", ("data/x.json", "{ \"planeTypes\": [ { \"id\": 100, \"noSuchField\": 1 } ] }"));
            WriteMod(root, "BadDll", "{ \"id\": \"baddll\" }", ("NotAnAssembly.dll", "this is not a PE file"));

            var opts = new ModLoadOptions { Log = _out.WriteLine };
            opts.Roots.Add(root);
            opts.Disabled.Add("eps-not-really");
            var set = ModLoader.Load(opts);

            Assert.Equal(new[] { "zeta", "beta", "alpha", "eps" }, set.Active.Select(m => m.Id).ToArray());
            ModStatus S(string folderId) => set.All.First(m => m.Manifest.Directory.EndsWith(folderId)).Status;
            Assert.Equal(ModStatus.Failed, S("Gamma"));
            Assert.Equal(ModStatus.Failed, S("Delta"));
            Assert.Equal(ModStatus.Disabled, S("Off"));
            Assert.Equal(ModStatus.Failed, S("Bad"));
            Assert.Equal(ModStatus.Duplicate, S("Dup"));
            Assert.Equal(ModStatus.Failed, S("Cyc1"));
            Assert.Equal(ModStatus.Failed, S("Old"));
            Assert.Equal(ModStatus.Failed, S("BadData"));
            Assert.Equal(ModStatus.Failed, S("BadDll"));
            Assert.Contains(set.Messages, m => m.Contains("noSuchField"));

            // Switched off by the player.
            var opts2 = new ModLoadOptions();
            opts2.Roots.Add(root);
            opts2.Disabled.Add("zeta");
            var set2 = ModLoader.Load(opts2);
            Assert.DoesNotContain(set2.Active, m => m.Id == "zeta" || m.Id == "alpha");
        }

        // ------------------------------------------------------------------ data mods

        [Fact]
        public void DataMod_LowFuelPrices_ChangesTheKeroseneMarket()
        {
            var set = ModLoader.Load(Path.Combine(Examples, "LowFuelPrices"), _out.WriteLine);
            Assert.Equal("lowfuelprices", Assert.Single(set.Active).Id);
            var d = set.ApplyData(OpsWorld.Data());
            Assert.Equal(350, d.Tunables.Int(TunableKeys.KeroseneMax));

            var w = SynthWorld(d);
            var plain = SynthWorld(OpsWorld.Data());
            w.InitKerosene();
            plain.InitKerosene();
            Assert.Equal(250, w.Kerosene);
            Assert.Equal(500, plain.Kerosene);
            for (int day = 1; day <= 120; day++)
            {
                w.Clock.Date = plain.Clock.Date = day;
                w.UpdateKerosenePrice();
                plain.UpdateKerosenePrice();
                Assert.InRange(w.Kerosene, 150, 350);
                Assert.InRange(plain.Kerosene, 300, 700);
            }
            // Fuel cost follows the market price.
            Assert.True(w.Players[0].FuelPricePerBarrel() < plain.Players[0].FuelPricePerBarrel() || w.Kerosene < plain.Kerosene);
        }

        [Fact]
        public void DataMod_ContentTablesOptionsAndRobots()
        {
            var root = TempDir("content");
            WriteMod(root, "Content", "{ \"id\": \"content\", \"version\": \"1.2.3\" }", ("data/content.json", @"{
                ""tunables"": { ""route.potentialPercent"": 200, ""fleet.wearPercent"": 0, ""my.own"": 7 },
                ""options"": { ""FixLoanInterest"": true, ""RandomStartDay"": false },
                ""planeTypes"": [
                    { ""id"": 100, ""price"": 12345678, ""seats"": 200 },
                    { ""id"": 140, ""basedOn"": 105, ""name"": ""757 Stretch"", ""seats"": 300 }
                ],
                ""cities"": [ { ""name"": ""Bravo"", ""population"": 4000000 }, { ""id"": 3, ""officeRent"": 1 } ],
                ""routes"": [
                    { ""from"": ""Alpha"", ""to"": ""Bravo"", ""factor"": 2.0 },
                    { ""from"": ""Alpha"", ""to"": ""Echo"", ""level"": 2, ""rent"": 90000 }
                ],
                ""robotFeatures"": { ""Sabotage"": { ""free"": false }, ""Freight"": ""XXXXXX|X|XXXXXXXXXX|XXXXXXXXXX"" },
                ""robotTuning"": { ""workTicks"": { ""Bank"": 42 }, ""standStillLimit"": 3, ""defaultPicks"": [""Bank"", ""Office""] }
            }"));
            var set = ModLoader.Load(root, _out.WriteLine);
            Assert.Single(set.Active);
            var d = set.ApplyData(OpsWorld.Data());

            Assert.Equal(12345678, d.PlaneTypeByCsvId(100).Price);
            Assert.Equal(200, d.PlaneTypeByCsvId(100).Seats);
            var stretch = d.PlaneTypeByCsvId(140);
            Assert.Equal("757 Stretch", stretch.Name);
            Assert.Equal(300, stretch.Seats);
            Assert.Equal(d.PlaneTypeByCsvId(105).Range, stretch.Range);                       // copied from basedOn
            Assert.DoesNotContain(d.PlaneTypes.All, t => t != stretch && t.Index == stretch.Index);
            Assert.Equal(4000000, d.CityByName("Bravo").Population);
            Assert.Equal(1, d.CityByName("Charlie").OfficeRent);
            var ab = d.Routes.Where(r => r.FromCity == OpsWorld.C(1) && r.ToCity == OpsWorld.C(2)).ToList();
            Assert.Single(ab);
            Assert.Equal(2.0, ab[0].Factor);
            Assert.Equal(2.0, d.Routes.Single(r => r.FromCity == OpsWorld.C(2) && r.ToCity == OpsWorld.C(1)).Factor);   // both ways
            Assert.Equal(90000, d.Routes.Single(r => r.FromCity == OpsWorld.C(5) && r.ToCity == OpsWorld.C(1)).Rent);  // new, both ways
            Assert.Equal(7, d.Tunables.Int("my.own"));

            var o = set.CreateOptions();
            Assert.True(o.FixLoanInterest);
            Assert.False(o.RandomStartDay);

            var w = SynthWorld(d);
            Assert.Equal(d.Routes.Count, w.RouteTable.Count);
            var t = (RobotFeatureTable)w.Rules.Get<IRobotFeatures>();
            Assert.False(t.Get(RobotFeature.Sabotage, 6));
            Assert.True(t.Get(RobotFeature.Sabotage, 7));
            Assert.True(t.Get(RobotFeature.Freight, 0));
            Assert.Equal(42, w.AI.Tuning.Work(RobotAction.Bank));
            Assert.Equal(3, w.AI.Tuning.StandStillLimit);
            Assert.Equal(new[] { RobotAction.Bank, RobotAction.Office }, w.AI.Tuning.DefaultPicks);

            // Potential doubled by the tunable.
            var plain = SynthWorld(OpsWorld.Data());
            var demand = w.Rules.Get<IRouteDemandRule>();
            var r12 = w.RouteTable.First(r => r.From == OpsWorld.C(3) && r.To == OpsWorld.C(4));     // cities untouched by the mod
            var p12 = plain.RouteTable.First(r => r.From == OpsWorld.C(3) && r.To == OpsWorld.C(4));
            Assert.Equal(2 * plain.Rules.Get<IRouteDemandRule>().Potential(plain, p12), demand.Potential(w, r12));
            // Wear 0 %.
            var pl = OpsWorld.AddPlane(w, w.Players[0]);
            Assert.Equal(pl.Condition, w.Rules.Get<IPlaneWearRule>().ApplyWear(w, w.Players[0], pl, 5000000, 1));

            // Other worlds and fresh data are untouched.
            Assert.Equal(500, OpsWorld.Data().Tunables.Int(TunableKeys.KeroseneStart));
            Assert.Equal(100, plain.Tunables.Int(TunableKeys.RoutePotentialPercent));
        }

        [Fact]
        public void Tunables_DefaultsAreTheOriginalAndValuesAreClamped()
        {
            var t = Tunables.CreateDefault();
            Assert.Equal(500, t.Int(TunableKeys.KeroseneStart));
            Assert.Equal(300, t.Int(TunableKeys.KeroseneMin));
            Assert.Equal(700, t.Int(TunableKeys.KeroseneMax));
            Assert.Equal(4.27, t.Get(TunableKeys.RouteDemandFactor));
            Assert.All(t.Defs, d => Assert.True(t.IsDefault(d.Key)));
            Assert.Empty(t.Changes());
            Assert.NotNull(t.Set(TunableKeys.KeroseneMin, -5));
            Assert.Equal(1, t.Int(TunableKeys.KeroseneMin));
            Assert.NotNull(t.Set(TunableKeys.KeroseneMax, 400.6));
            Assert.Equal(401, t.Int(TunableKeys.KeroseneMax));
            var c = t.Clone();
            c.Set(TunableKeys.KeroseneMax, 450);
            Assert.Equal(401, t.Int(TunableKeys.KeroseneMax));
        }

        // ------------------------------------------------------------------ code mods

        [Fact]
        public void CodeMod_RegistrationsApplyToEveryWorld()
        {
            var set = new ModSet(_out.WriteLine);
            var m = set.AddCodeMod(Manifest("testcounter"), new TestCounterMod());
            Assert.True(m.IsActive);
            var d = set.ApplyData(OpsWorld.Data());
            Assert.Equal(50, d.Tunables.Int(TunableKeys.PlaneWearPercent));
            Assert.Single(set.Rooms);
            Assert.True(set.CreateOptions().FixLoanInterest);

            var w = SynthWorld(d);
            Assert.IsType<GuardedSystem>(w.Systems.Find("test.counter"));
            Assert.IsType<TestCounterMod.FixedKerosene>(w.Rules.Get<IKerosenePriceRule>());
            OpsWorld.RunUntil(w, 3, 12);
            var s = ModState.Get<TestCounterState>(w, "mod.testcounter");
            Assert.Equal(3, s.Days);
            Assert.Equal(new[] { 1, 2, 3 }, s.Dates);
            Assert.True(w.Kerosene >= 400);
            Assert.Equal(3, ModState.Get<TestCounterPlayerState>(w.Players[0], "mod.testcounter").Mornings);

            Assert.True(w.Execute(set.CreateCommand("test.add", w, "10")).Ok);
            Assert.Equal(13, s.Days);
            Assert.Null(set.CreateCommand("nothing", w));
        }

        [Fact]
        public void CodeMod_BrokenModsAreIsolated()
        {
            var set = new ModSet(_out.WriteLine);
            var bad = set.AddCodeMod(Manifest("throwsonload"), new ThrowingOnLoadMod());
            var boom = set.AddCodeMod(Manifest("throwsinsystem"), new ThrowingSystemMod());
            var hook = set.AddCodeMod(Manifest("throwsinhook"), new ThrowingWorldHookMod());
            var good = set.AddCodeMod(Manifest("testcounter"), new TestCounterMod());
            Assert.Equal(ModStatus.Failed, bad.Status);
            Assert.Contains("broken on purpose", bad.Error);
            Assert.True(boom.IsActive && hook.IsActive && good.IsActive);

            var d = set.ApplyData(OpsWorld.Data());
            Assert.Equal(700, d.Tunables.Int(TunableKeys.KeroseneMax));   // the failed mod's registration was discarded
            ThrowingSystemMod.Calls = 0;
            var w = SynthWorld(d);                                         // the throwing world hook does not stop the world
            OpsWorld.RunUntil(w, 1, 12);
            Assert.Equal(1, ThrowingSystemMod.Calls);                      // switched off after the first failure
            Assert.True(((GuardedSystem)w.Systems.Find("test.boom")).Failed);
            Assert.Equal(1, ModState.Get<TestCounterState>(w, "mod.testcounter").Days);   // other mods keep working
            Assert.Contains(set.Messages, x => x.Contains("hook fails"));
            Assert.Contains(set.Messages, x => x.Contains("switched off"));
        }

        // ------------------------------------------------------------------ the example DLL (built from Mods.Examples)

        static readonly object BuildLock = new object();
        static string _builtRouteBonus;

        /// <summary>Builds Mods.Examples/RouteBonusEvent with the .NET SDK into a temp mod folder (null when dotnet is missing).</summary>
        static string RouteBonusMod(ITestOutputHelper log)
        {
            lock (BuildLock)
            {
                if (_builtRouteBonus != null) return _builtRouteBonus;
                var deploy = Path.Combine(TempDir("built"), "RouteBonusEvent");
                var proj = Path.Combine(Examples, "RouteBonusEvent", "RouteBonusEvent.csproj");
                var psi = new ProcessStartInfo("dotnet", $"build \"{proj}\" -nologo -v q -nodeReuse:false -p:UseSharedCompilation=false -p:ModDeployDir=\"{deploy}\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                // The test host's MSBuild variables would redirect the nested build.
                foreach (var k in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildLoadMicrosoftTargetsReadOnly" }) psi.Environment.Remove(k);
                // No lingering MSBuild nodes / compiler server: they inherit the redirected pipes and keep
                // ReadToEnd waiting until their idle timeout (~15 minutes).
                psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
                Process p;
                try { p = Process.Start(psi); }
                catch (Exception e) { log.WriteLine("dotnet not available: " + e.Message); return null; }
                string stdout = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0, "building the example mod failed:\n" + stdout);
                Assert.True(File.Exists(Path.Combine(deploy, "RouteBonusEvent.dll")), stdout);
                Assert.False(File.Exists(Path.Combine(deploy, "OpenAT.Sim.dll")), "the mod must not ship the Sim");
                return _builtRouteBonus = deploy;
            }
        }

        static object StateOf(World w, string key) => w.Extensions.TryGetValue(key, out var v) ? v : null;
        static int Field(object o, string name) => (int)o.GetType().GetField(name).GetValue(o);

        [Fact]
        public void CodeMod_ExampleDllLoadsBindsToTheHostSimAndRuns()
        {
            var dir = RouteBonusMod(_out);
            if (dir == null) return;
            var set = ModLoader.Load(dir, _out.WriteLine);
            var m = Assert.Single(set.Active);
            Assert.Equal("routebonusevent", m.Id);
            Assert.Single(m.Instances);
            // The mod's IOpenATMod is this process's IOpenATMod (bound to the host Sim, not a copy).
            Assert.Same(typeof(IOpenATMod).Assembly, m.Instances[0].GetType().GetInterfaces().First(i => i.Name == "IOpenATMod").Assembly);

            var d = set.ApplyData(OpsWorld.Data());
            Assert.Equal(150, d.Tunables.Int("routeBonus.percent"));
            var w = SynthWorld(d);
            var news = new List<ModNewsEvent>();
            w.Events.Subscribe<ModNewsEvent>(news.Add);
            OpsWorld.RunUntil(w, 1, 12);
            var state = StateOf(w, "mod.routebonusevent");
            Assert.NotNull(state);
            int routeId = Field(state, "RouteId");
            Assert.NotEqual(0, routeId);
            Assert.Equal(8, Field(state, "UntilDate"));
            Assert.Single(news);
            _out.WriteLine(news[0].ToString());

            // The boosted route sits at its boosted target; the same route of a world without the mod does not.
            var plain = SynthWorld(OpsWorld.Data());
            OpsWorld.RunUntil(plain, 1, 12);
            var r = w.RouteById(routeId);
            var rp = plain.RouteById(routeId);
            Assert.True(r.Demand > rp.Demand, $"boosted {r.Demand} vs {rp.Demand}");

            // The named command works through World.Execute.
            Assert.True(w.Execute(set.CreateCommand("routebonus.start", w, "0")).Ok);
            Assert.Equal(2, Field(state, "Count"));
        }

        // ------------------------------------------------------------------ saves and determinism (need the install)

        static GameInstall _install;
        static GameInstall Install => _install ?? (_install = GameInstall.TryLocate());

        static GameData InstallData(ModSet mods)
        {
            if (Install == null) return null;
            var d = GameData.Load(Install);
            FinanceData.Load(d, Install);
            return mods != null ? mods.ApplyData(d) : d;
        }

        ModSet ExampleMods(bool withDll)
        {
            var o = new ModLoadOptions { Log = _out.WriteLine };
            o.Roots.Add(Path.Combine(Examples, "LowFuelPrices"));
            if (withDll)
            {
                var dir = RouteBonusMod(_out);
                if (dir == null) return null;
                o.Roots.Add(dir);
            }
            var set = ModLoader.Load(o);
            set.AddCodeMod(Manifest("testcounter"), new TestCounterMod());
            return set;
        }

        static World RoundTrip(World w, GameData data, out List<string> warnings)
        {
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "MODS");
            ms.Position = 0;
            return SaveGame.Read(ms, data, out warnings);
        }

        static string Hash(World w, int restoreCount)
        {
            int keep = w.RestoreCount;
            w.RestoreCount = restoreCount;
            try { return SaveGame.StateHash(w); }
            finally { w.RestoreCount = keep; }
        }

        [Fact]
        public void Save_ModStateSurvivesSaveLoad_AndLoadsWithoutTheMods()
        {
            var mods = ExampleMods(withDll: true);
            if (mods == null) return;
            var d = InstallData(mods);
            if (d == null) return;
            var a = HeadlessRunner.NewGame(d, Mission.FreeGame, 4711);
            Assert.Equal(250, a.World.Kerosene);
            Assert.True(a.World.Options.FixLoanInterest);                  // the test mod's new-game option
            a.RunDays(9);
            var counter = ModState.Get<TestCounterState>(a.World, "mod.testcounter");
            Assert.Equal(9, counter.Days);
            var bonus = StateOf(a.World, "mod.routebonusevent");
            Assert.Equal(2, Field(bonus, "Count"));
            Assert.Empty(SaveGame.Audit(a.World));

            var b = RoundTrip(a.World, d, out var warnings);
            Assert.Empty(warnings);
            Assert.Empty(ModSet.CheckSave(b));
            Assert.Equal(9, ModState.Get<TestCounterState>(b, "mod.testcounter").Days);
            Assert.Equal(counter.Dates, ModState.Get<TestCounterState>(b, "mod.testcounter").Dates);
            Assert.Equal(9, ModState.Get<TestCounterPlayerState>(b.Players[2], "mod.testcounter").Mornings);
            var bonus2 = StateOf(b, "mod.routebonusevent");
            Assert.Equal(bonus.GetType(), bonus2.GetType());
            Assert.Equal(Field(bonus, "RouteId"), Field(bonus2, "RouteId"));
            Assert.IsType<GuardedSystem>(b.Systems.Find("mod.routebonus"));   // the loaded world has the mods' machinery again

            // Both continue identically.
            var rb = new HeadlessRunner(b);
            for (int day = 0; day < 6; day++)
            {
                a.RunDay();
                rb.RunDay();
                Assert.Equal(Hash(a.World, b.RestoreCount), Hash(b, b.RestoreCount));
            }

            // Without the mods: loads and plays on, the compatibility check names the mods. (In this process the
            // mod's DLL stays loaded, so its state type still resolves; in a fresh process the loader reports
            // "unknown type ... (skipped)" and drops it, see docs/MODDING.md.)
            var vanilla = InstallData(null);
            var c = RoundTrip(a.World, vanilla, out var w2);
            foreach (var x in w2) _out.WriteLine("warning: " + x);
            Assert.Null(c.Systems.Find("mod.routebonus"));
            Assert.Equal(700, c.Tunables.Int(TunableKeys.KeroseneMax));
            var issues = ModSet.CheckSave(c);
            Assert.Contains(issues, x => x.Contains("routebonusevent"));
            Assert.Contains(issues, x => x.Contains("lowfuelprices"));
            new HeadlessRunner(c).RunDays(2);                               // and it plays on

            // A vanilla save loaded with mods: the mods start with fresh state.
            var v = HeadlessRunner.NewGame(vanilla, Mission.FreeGame, 4711);
            v.RunDays(2);
            var e = RoundTrip(v.World, d, out _);
            Assert.Contains(ModSet.CheckSave(e), x => x.Contains("was not active"));
            new HeadlessRunner(e).RunDays(2);
            Assert.Equal(2, ModState.Get<TestCounterState>(e, "mod.testcounter").Days);
        }

        [Fact]
        public void Headless_RunWithModsIsDeterministic()
        {
            if (Install == null) return;
            string Run(bool mods, out int kerosene)
            {
                var set = mods ? ExampleMods(withDll: true) : null;
                var d = InstallData(set);
                var r = HeadlessRunner.NewGame(d, Mission.FreeGame, 999);
                r.RunDays(12);
                kerosene = r.World.KerosenePast.Max();
                return SaveGame.StateHash(r.World);
            }
            if (RouteBonusMod(_out) == null) return;
            string a = Run(true, out int ka), b = Run(true, out _), v = Run(false, out int kv);
            Assert.Equal(a, b);
            Assert.NotEqual(a, v);
            Assert.InRange(ka, 1, 400);    // low fuel prices mod (history before day 0 may be up to start + noise)
            Assert.True(kv > 300);
        }
    }
}
