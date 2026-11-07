using System.IO;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Items;
using OpenAT.Sim.Testing;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Test scenarios (Sim/Testing/Scenario.cs, tools/scenarios/*.json).</summary>
    public class ScenarioTests
    {
        static GameData _data;
        static HallData _hall;

        static bool Load()
        {
            if (_data != null) return true;
            var install = GameInstall.TryLocate();
            if (install == null) return false;
            _hall = HallData.Load(install);
            _data = GameData.Load(install);
            HallContent.Attach(_data, _hall);
            return true;
        }

        static World Start(Scenario s)
        {
            var w = World.NewGame(_data, s.Settings(_data));
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            s.Apply(w);
            return w;
        }

        static string RepoFile(string rel, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
        {
            // The tests run from an artifacts folder outside the repo: start from this source file.
            var dir = new DirectoryInfo(Path.GetDirectoryName(here));
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "scenarios"))) dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, rel);
        }

        [Fact]
        public void Parse_ReadsAllKeys()
        {
            var s = Scenario.Parse(@"{ ""mission"": 2, ""time"": ""10:30"", ""money"": 5, ""items"": [""laptop"", ""Red Bull"", 1],
                ""laptop"": { ""quality"": 2, ""battery"": 7, ""virus"": 1 }, ""security"": [""laptop""], ""planes"": [ { ""type"": ""100"", ""count"": 3 } ],
                ""routes"": 2, ""orders"": 3, ""freight"": 1, ""planFlights"": true, ""room"": ""laptop"", ""skipBriefing"": true, ""advanceDays"": 2 }");
            Assert.Equal(2, s.Mission);
            Assert.Equal(GameClock.At(10, 30), s.TimeOfDay);
            Assert.Equal(5, s.Money);
            Assert.Equal(new[] { ItemId.Laptop, ItemId.RedBull, ItemId.Phone }, s.Items.ToArray());
            Assert.Equal(2, s.LaptopQuality); Assert.Equal(7, s.LaptopBattery); Assert.Equal(1, s.LaptopVirus);
            Assert.Equal(new[] { SecurityBits.Laptop }, s.Security.ToArray());
            Assert.Equal(("100", 3), s.Planes[0]);
            Assert.Equal(2, s.Routes); Assert.Equal(3, s.Orders); Assert.Equal(1, s.Freight);
            Assert.True(s.PlanFlights && s.SkipBriefing);
            Assert.Equal(2, s.AdvanceDays);
            Assert.Equal("laptop", s.Room);
            Assert.Empty(s.Warnings);
            Assert.Contains("unknown item", Scenario.Parse(@"{ ""items"": [""spaceship""] }").Warnings[0]);
        }

        [Fact]
        public void Apply_SetsItemsLaptopMoneyAndTime()
        {
            if (!Load()) return;
            var s = Scenario.Parse(@"{ ""skipBriefing"": true, ""time"": ""10:00"", ""money"": 3000000, ""items"": [""laptop"", ""phone""],
                ""laptop"": { ""quality"": 3, ""battery"": 25, ""virus"": 1 }, ""security"": [""laptop""] }");
            var w = Start(s);
            var p = w.Local;
            Assert.Equal(3000000, p.Money);
            Assert.True(p.HasItem(ItemId.Laptop) && p.HasItem(ItemId.Phone));
            Assert.Equal(3, p.LaptopQuality);
            Assert.Equal(25, p.LaptopBattery);
            Assert.Equal(1, p.LaptopVirus);
            Assert.True(SecurityBits.Has(p.SecurityFlags, SecurityBits.Laptop));
            Assert.Equal(DayState.Playing, w.Clock.State);
            Assert.InRange(w.Clock.Time, GameClock.At(10), GameClock.At(10, 5));
            Assert.Equal(0, w.Hall.Players[w.LocalPlayer].Room);   // still in the hall, not walking into the office
        }

        [Fact]
        public void Apply_IsDeterministic()
        {
            if (!Load()) return;
            const string json = @"{ ""seed"": 9, ""advanceDays"": 1, ""routes"": 2, ""orders"": 3, ""planes"": [ { ""type"": ""100"" } ], ""planFlights"": true }";
            var a = Start(Scenario.Parse(json));
            var b = Start(Scenario.Parse(json));
            Assert.Equal(a.Local.Money, b.Local.Money);
            Assert.Equal(a.Local.Contracts.Count, b.Local.Contracts.Count);
            Assert.Equal(a.Clock.Date, b.Clock.Date);
        }

        [Fact]
        public void LaptopDay3_FillsEveryLaptopPage()
        {
            if (!Load()) return;
            var path = RepoFile("tools/scenarios/laptop-day3.json");
            Assert.NotNull(path);
            var s = Scenario.Parse(File.ReadAllText(path));
            var w = Start(s);
            var p = w.Local;
            Assert.True(s.Warnings.Count == 0, string.Join("; ", s.Warnings));
            Assert.Equal(3, w.Clock.Date);
            Assert.True(p.HasItem(ItemId.Laptop));
            Assert.Equal(4, p.LaptopQuality);
            Assert.True(p.Planes.Count >= 4);              // two starting planes + two bought
            Assert.True(p.LicensedRouteCount >= 4);        // both directions of 2+ routes
            Assert.True(p.Contracts.Count >= 3);
            Assert.True(p.Freights.Count >= 1);
            bool planned = false;
            foreach (var pl in p.Planes.Items) foreach (var e in pl.Plan.Entries) if (e.Kind == OpenAT.Sim.Fleet.FlightKind.Contract) planned = true;
            Assert.True(planned);
            Assert.Equal("laptop", s.Room);
        }

        [Fact]
        public void ShippedScenarios_ApplyWithoutWarnings()
        {
            if (!Load()) return;
            foreach (var name in new[] { "laptop-day0", "laptop-virus", "laptop-low-battery" })
            {
                var s = Scenario.Parse(File.ReadAllText(RepoFile("tools/scenarios/" + name + ".json")));
                Start(s);
                Assert.True(s.Warnings.Count == 0, name + ": " + string.Join("; ", s.Warnings));
            }
        }
    }
}
