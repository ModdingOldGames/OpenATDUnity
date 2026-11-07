using System.Linq;
using OpenAT.Sim;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using Xunit;

namespace OpenAT.Tests
{
    public class RoutesTests
    {
        static int Idx(World w, int a, int b) => w.RouteIndex(OpsWorld.C(a), OpsWorld.C(b));

        [Fact]
        public void Demand_PotentialAndNightlyPull()
        {
            var w = OpsWorld.NewWorld();
            var r = w.RouteTable[Idx(w, 1, 2)];
            // sqrt(2 000 000 · 1 000 000) · 1 / 1000 / 2 · 3/4 = 530.3 → 530; day 0: trunc(530 · 4.27)
            Assert.Equal(530, new OriginalRouteDemand().Potential(w, r));
            Assert.Equal(2263, r.Demand);
            w.Clock.Date = 3;  // DayFaktor 900 → 589
            r.Demand = 0;
            RouteOps.UpdateDemand(w);
            Assert.Equal((0 * 6 + (int)(589 * 4.27)) / 7, r.Demand);
        }

        [Fact]
        public void ReferenceAndDefaultTicketPrice()
        {
            var w = OpsWorld.NewWorld();
            // 1001 km at 800 l/h, 800 km/h: 6 barrels · 500 = 3000 → ×3/180×2 = 100
            Assert.Equal(100, w.ReferencePrice(OpsWorld.C(1), OpsWorld.C(2)));
            Assert.Equal(200, RouteOps.DefaultTicket(w, OpsWorld.C(1), OpsWorld.C(2)));
        }

        [Fact]
        public void Passengers_EconomyFormula()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            int ri = Idx(w, 1, 2);
            var route = w.RouteTable[ri];
            RouteOps.Rent(w, p, route.From, route.To, route.Rent);
            var e = new FlightEntry { Kind = FlightKind.Route, ObjectId = route.Id, StartDate = 1, StartHour = 12, HoursBefore = 48, TicketPrice = 200, TicketPriceFC = 400 };
            var rule = new OriginalRoutePassengers();
            rule.Calculate(w, p, pl, ref e);
            // share 2263 → cap 1.5·126 = 189 → image (0·4 + 0 + 200 = 200): 189·600/1100 = 103
            Assert.Equal(103, e.Passengers);
            Assert.Equal(0, e.PassengersFC);   // luxury sum 0 < 6
            // short notice: HoursBefore 0 → ×48/96
            e.HoursBefore = 0;
            rule.Calculate(w, p, pl, ref e);
            Assert.Equal(189 * 48 / 96 * 600 / 1100, e.Passengers);
            // computer rank 2 shares the pool: weights 37 / 37 → half; demand 100 → 50 → ×600/1100 = 27
            var c = w.Players[1];
            c.Owner = Owner.Computer;
            RouteOps.Rent(w, c, route.From, route.To, route.Rent);
            Assert.Equal(2, c.RentRoutes[ri].Rank);
            route.Demand = 100;
            e.HoursBefore = 48;
            rule.Calculate(w, p, pl, ref e);
            Assert.Equal(27, e.Passengers);
            // night penalty ×5/6 for start and landing
            e.StartHour = 23;
            rule.Calculate(w, p, pl, ref e);
            Assert.Equal((int)(50L * 5 / 6 * 5 / 6 * 600 / 1100), e.Passengers);
            // frozen from S−2:00
            int frozen = e.Passengers;
            route.Demand = 5000;
            w.Clock.Date = 1; w.Clock.Time = Sim.Core.GameClock.At(22);
            rule.Calculate(w, p, pl, ref e);
            Assert.Equal(frozen, e.Passengers);
        }

        [Fact]
        public void AvgFlown_MovingAverage_FixedByDefault()
        {
            foreach (bool fix in new[] { true, false })
            {
                var w = OpsWorld.NewWorld();
                w.Options.FixRouteAvgFlown = fix;
                var p = w.Players[0];
                int ab = Idx(w, 1, 2);
                Assert.True(w.Execute(new RentRouteCommand(0, ab)).Ok);
                var rr = p.RentRoutes[ab];
                Assert.Equal(30, rr.AvgFlown);
                rr.LastFlown = 0;   // flown today
                RouteOps.DailyUpdate(w, p);
                // LastFlown 1 after the night: (4·30 + 1)/5 = 24; the original keeps 30 for ever
                Assert.Equal(fix ? 24 : 30, rr.AvgFlown);
            }
        }

        [Fact]
        public void RouteOffice_RanksEligibilityDropAndCooldown()
        {
            var w = OpsWorld.NewWorld();
            int ab = Idx(w, 1, 2), ba = Idx(w, 2, 1), cd = Idx(w, 3, 4);
            for (int i = 0; i < 3; i++) Assert.True(w.Execute(new RentRouteCommand(i, ab)).Ok);
            Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(0, 3).Select(i => w.Players[i].RentRoutes[ba].Rank));
            Assert.Equal("rank-full", w.Execute(new RentRouteCommand(3, ab)).Reason);
            Assert.True(w.Execute(new DropRouteCommand(0, ab)).Ok);
            Assert.Equal(1, w.Players[1].RentRoutes[ab].Rank);
            Assert.Equal(2, w.Players[2].RentRoutes[ab].Rank);
            Assert.Equal(99, w.Players[0].RentRoutes[ab].DaysLow);
            Assert.Equal(99, w.Players[0].RentRoutes[ba].DaysLow);
            Assert.True(new RentRouteCommand(0, ab).Validate(w).Ok);   // a voluntary drop is not blocked
            // Charlie–Delta does not touch home: eligible only via a ≥ 20 % route sharing an endpoint
            var p = w.Players[3];
            Assert.Equal("not-eligible", w.Execute(new RentRouteCommand(3, cd)).Reason);
            p.RentRoutes[Idx(w, 1, 3)].RouteShare = 20;
            Assert.True(w.Execute(new RentRouteCommand(3, cd)).Ok);
            // cooldown after a revocation
            p.RentRoutes[cd].DaysLow = 19;
            p.RentRoutes[cd].RouteShare = 0;
            p.RentRoutes[Idx(w, 4, 3)].DaysLow = 0;
            int letters = 0;
            w.Events.Subscribe<LetterSent>(l => letters++);
            RouteOps.DailyUpdate(w, p);
            Assert.False(p.RentRoutes[cd].Licensed);
            Assert.Equal(0, p.RentRoutes[cd].DaysLow);
            Assert.Equal("cooldown", w.Execute(new RentRouteCommand(3, cd)).Reason);
            Assert.Equal(1, letters);   // one letter per pair (from-id < to-id)
        }

        [Fact]
        public void DropRefusedWhileInUse_AndPricesClamped()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            int ab = Idx(w, 1, 2), ba = Idx(w, 2, 1);
            w.Execute(new RentRouteCommand(0, ab));
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, w.RouteTable[ab].Id, 1, 10));
            Assert.Equal("in-use", w.Execute(new DropRouteCommand(0, ba)).Reason);
            w.Execute(new SetTicketPricesCommand(0, ab, 5000, 9000));
            Assert.Equal(1600, p.RentRoutes[ab].TicketPrice);    // 16 · RefPrice 100
            Assert.Equal(4800, p.RentRoutes[ab].TicketPriceFC);  // 48 · RefPrice
            Assert.Equal(1600, p.RentRoutes[ba].TicketPrice);    // economy copied back
            Assert.Equal(1600, pl.Plan.Entries.First(e => e.Kind == FlightKind.Route).TicketPrice);
            Assert.Equal(50, RouteOps.Preset(w, ab, 0));
        }

        [Fact]
        public void Gates_PlanningAndWarnings()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            p.Gates.Rent(0, 1000);
            var a = OpsWorld.AddPlane(w, p);
            var b = OpsWorld.AddPlane(w, p);
            int ab = Idx(w, 1, 2);
            w.Execute(new RentRouteCommand(0, ab));
            w.Execute(new PlaceFlightCommand(0, a.Id, FlightKind.Route, w.RouteTable[ab].Id, 1, 10));
            w.Execute(new PlaceFlightCommand(0, b.Id, FlightKind.Route, w.RouteTable[ab].Id, 1, 11));
            var ea = a.Plan.Entries.First(e => e.Kind == FlightKind.Route);
            var eb = b.Plan.Entries.First(e => e.Kind == FlightKind.Route);
            // the plane planned first (album order) gets gate 0; the other finds hours 10–11 taken
            Assert.Equal(new[] { GateCode.None, 0 }, new[] { ea.Gate, eb.Gate }.OrderBy(x => x).ToArray());
            Assert.True(eb.GateWarning && ea.GateWarning);
            // a flight not touching home
            var bc = Idx(w, 2, 3);
            w.Players[0].RentRoutes[bc].Rank = 1;
            w.Execute(new PlaceFlightCommand(0, a.Id, FlightKind.Route, w.RouteTable[bc].Id, 2, 10));
            Assert.Equal(GateCode.NotHome, a.Plan.Entries.First(e => e.Kind == FlightKind.Route && e.ObjectId == w.RouteTable[bc].Id).Gate);
        }

        [Fact]
        public void Auction_BidResolveRenumber()
        {
            var w = OpsWorld.NewWorld();
            w.Players[0].Gates.Rent(0, 1000);
            w.Players[1].Gates.Rent(2, 1000);
            w.Clock.Date = 1;
            w.Auctions.Randomize(w, 3);
            var gates = w.Auctions.Gates.Where(s => s.ObjectId >= 0).Select(s => s.ObjectId).ToArray();
            Assert.Equal(new[] { 1, 3 }, gates);                    // 4 gate runes, 0 and 2 rented
            Assert.Equal(2900, w.Auctions.Gates[0].Price);          // 3000 − 100 · 1
            Assert.True(w.Execute(new BidCommand(2, AuctionKind.Gate, 0)).Ok);
            Assert.Equal(3190, w.Auctions.Gates[0].Price);          // +10 %
            Assert.True(w.Execute(new BidCommand(3, AuctionKind.Gate, 0)).Ok);
            Assert.Equal(3509, w.Auctions.Gates[0].Price);
            long m = w.Players[3].Money;
            w.Auctions.Resolve(w);
            Assert.Equal(m - 3 * 3509, w.Players[3].Money);
            Assert.True(w.Players[3].Gates.HasGate(1));
            GatePlanner.Renumber(w);
            Assert.Equal(new[] { 0, 1, 2 }, new[] { w.Players[0].Gates.Gates[0].Number, w.Players[1].Gates.Gates[0].Number, w.Players[3].Gates.Gates[0].Number });
        }
    }
}
