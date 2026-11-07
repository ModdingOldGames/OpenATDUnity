using System.Collections.Generic;
using OpenAT.Sim.Core;
using Xunit;

namespace OpenAT.Tests
{
    public class CoreTests
    {
        [Fact]
        public void TeakRand_MatchesLcg()
        {
            var r = new TeakRand(1);
            // v = 1*214013 + 2531011 = 2745024 -> >>16 = 41 (16-bit output, unlike MSVC rand() which masks to 15 bits)
            Assert.Equal(41, r.Rand());
            Assert.Equal(51235, r.Rand());
            Assert.Equal(6334, r.Rand());
            r.Reset();
            Assert.Equal(41, r.Rand());
        }

        [Fact]
        public void TeakRand_RangeIsInclusive()
        {
            var r = new TeakRand(7);
            bool sawMin = false, sawMax = false;
            for (int i = 0; i < 1000; i++)
            {
                int v = r.Rand(3, 5);
                Assert.InRange(v, 3, 5);
                sawMin |= v == 3; sawMax |= v == 5;
            }
            Assert.True(sawMin && sawMax);
        }

        sealed class Item : IContent { public int Id { get; set; } public string Name; }

        [Fact]
        public void Registry_OverrideKeepsOrder()
        {
            var reg = new Registry<Item>();
            reg.Register(new Item { Id = 5, Name = "a" });
            reg.Register(new Item { Id = 2, Name = "b" });
            reg.Register(new Item { Id = 5, Name = "c" });
            Assert.Equal(2, reg.Count);
            Assert.Equal("c", reg[5].Name);
            Assert.Equal(5, reg.All[0].Id);
        }

        interface IFee : IRule { int Fee(int amount); }
        sealed class Flat : IFee { public int Fee(int a) => 10; }
        sealed class Double : IFee { readonly IFee _i; public Double(IFee i) => _i = i; public int Fee(int a) => _i.Fee(a) * 2; }

        [Fact]
        public void RuleSet_ReplaceAndDecorate()
        {
            var rules = new RuleSet();
            rules.Set<IFee>(new Flat());
            rules.Decorate<IFee>(inner => new Double(inner));
            Assert.Equal(20, rules.Get<IFee>().Fee(0));
        }

        sealed class Ping : ISimEvent { public int N; }

        [Fact]
        public void EventBus_DeliversAndUnsubscribes()
        {
            var bus = new EventBus();
            int sum = 0;
            var sub = bus.Subscribe<Ping>(p => sum += p.N);
            bus.Publish(new Ping { N = 2 });
            sub.Dispose();
            bus.Publish(new Ping { N = 5 });
            Assert.Equal(2, sum);
        }
    }
}
