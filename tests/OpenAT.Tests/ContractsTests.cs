using System.Linq;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using Xunit;

namespace OpenAT.Tests
{
    public class ContractsTests
    {
        [Fact]
        public void BasePrice_SpecExample()
        {
            // contracts-and-routes.md §2.2: ~1000 km at K = 500 → 71 barrels, fuel 35 500, Base(120) = 42 600
            var w = OpsWorld.NewWorld();
            Assert.Equal(500, w.Kerosene);
            Assert.Equal(71, w.FuelCost.FuelUnits(w.Distance(OpsWorld.C(1), OpsWorld.C(2)), 8000, 700));
            Assert.Equal(35500, w.FuelCost.Estimate(w, null, OpsWorld.C(1), OpsWorld.C(2), 8000, 700));
            Assert.Equal(42600, w.ContractBasePrice(OpsWorld.C(1), OpsWorld.C(2), 120));
            // minimum fuel cost 1000 for very short trips
            Assert.Equal(1000, w.FuelCost.Estimate(w, null, OpsWorld.C(1), OpsWorld.C(1), 8000, 700));
        }

        [Fact]
        public void Generators_DeterministicWithSeedAndWithinTemplate()
        {
            var w = OpsWorld.NewWorld();
            w.CalcPlayerMaximums(true);
            for (int seed = 0; seed < 200; seed++)
            {
                var a = ContractGenerator.Abroad(w, 2, w.HomeCity, new TeakRand(seed));
                var b = ContractGenerator.Abroad(w, 2, w.HomeCity, new TeakRand(seed));
                Assert.Equal(a.ToString(), b.ToString());
                Assert.True(a.From == w.HomeCity || a.To == w.HomeCity);
                Assert.NotEqual(a.From, a.To);
                Assert.Contains(a.Persons, new[] { 2, 90, 180, 280, 340, 430 });
                Assert.True(a.Date <= a.LastDate);
                Assert.True(a.Premium > 0);

                var lm = ContractGenerator.LastMinute(w, seed % 3, new TeakRand(seed));
                Assert.Contains(lm.Persons, new[] { 1, 90, 180, 280, 340, 430 });
                Assert.InRange(lm.Date, 0, 1);
                Assert.Equal(lm.Date, lm.LastDate);

                var f = ContractGenerator.FreightOffice(w, seed % 3, new TeakRand(seed));
                Assert.Contains(f.Tons, new[] { 1, 15, 30, 40, 60, 100 });
                Assert.Equal(f.Tons, f.TonsLeft);
            }
        }

        [Fact]
        public void BeginContract_FromHomeWithin5000Km()
        {
            var w = OpsWorld.NewWorld();
            OpsWorld.AddPlane(w, w.Players[0]);
            w.CalcPlayerMaximums(true);
            Assert.Equal(168, w.PlayerMaxPassengers);       // quirk: type seats stored
            Assert.Equal(3870 * 1000, w.PlayerMaxLength);
            for (int seed = 0; seed < 50; seed++)
            {
                var c = ContractGenerator.Begin(w, 0, new TeakRand(seed));
                Assert.Equal(w.HomeCity, c.From);
                Assert.Equal(90, c.Persons);
                Assert.True(w.Distance(c.From, c.To) <= 5000000);
            }
        }

        [Fact]
        public void TrickleRefill_OneSlotPerTick()
        {
            var w = OpsWorld.NewWorld();
            ContractBoards.FillAll(w);
            Assert.All(w.TravelAgency.Offers, o => Assert.True(o.Premium > 0));
            var p = w.Players[0];
            w.StartWorkingDay();
            Assert.True(w.Execute(new AcceptContractCommand(0, OfferBoard.TravelAgency, 1)).Ok);
            Assert.True(w.Execute(new AcceptContractCommand(0, OfferBoard.TravelAgency, 4)).Ok);
            Assert.Equal(2, p.Contracts.Count);
            Assert.Equal("empty-slot", w.Execute(new AcceptContractCommand(0, OfferBoard.TravelAgency, 1)).Reason);
            w.TravelAgency.RefillTicks = 1;
            w.Execute(new VisitOfferBoardCommand(0, OfferBoard.TravelAgency));
            Assert.True(w.TravelAgency.Offers[1].Premium > 0);
            Assert.Equal(0, w.TravelAgency.Offers[4].Premium);
            Assert.Equal(0, w.TravelAgency.RefillTicks);
            // QUIRK: the "minimum" mode only runs while tick budget remains
            w.Execute(new VisitOfferBoardCommand(0, OfferBoard.TravelAgency, -1, 6));
            Assert.Equal(0, w.TravelAgency.Offers[4].Premium);
        }

        [Fact]
        public void Counters_OpeningHours()
        {
            var w = OpsWorld.NewWorld();
            w.Clock.Weekday = 5;
            Assert.False(CounterHours.LastMinuteOpen(w));
            w.Clock.Weekday = 1;
            w.Clock.Time = GameClock.At(16);
            Assert.False(CounterHours.LastMinuteOpen(w));
            Assert.True(CounterHours.TravelAgencyOpen(w));
        }

        [Fact]
        public void Penalty_HumansPayTheMorningAfter_ComputersNever()
        {
            var w = OpsWorld.NewWorld();
            var h = w.Players[0];
            var c = w.Players[1];
            c.Owner = Owner.Computer;
            foreach (var p in new[] { h, c })
                p.Contracts.Add(new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 90, Date = 0, LastDate = 0, Premium = 40000, Penalty = 20000 });
            w.Clock.Date = 1;
            long hm = h.Money, cm = c.Money;
            ContractUsage.DailyUpdate(w, h);
            ContractUsage.DailyUpdate(w, c);
            Assert.Equal(hm - 20000, h.Money);
            Assert.Equal(-20000, h.Profit);
            Assert.Equal(cm, c.Money);
            w.Clock.Date = 2;
            ContractUsage.DailyUpdate(w, h);
            Assert.Equal(0, h.Contracts.Count);
        }

        [Fact]
        public void Usage_WrongDayAndTooSmall()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);
            int id = p.Contracts.Add(new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 90, Date = 2, LastDate = 3, Premium = 1, Penalty = 1 });
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Contract, id, 1, 12)).Ok);
            var c = p.Contracts.Find(id);
            var e = pl.Plan.Entries.First(x => x.Kind == FlightKind.Contract);
            Assert.Equal(EntryOkay.WrongDay, e.Okay);
            Assert.Equal(0, c.Okay);
            Assert.Equal(PlanState.Planned, c.InPlan);
            Assert.Equal("contract-planned", w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Contract, id, 2, 12)).Reason);

            int big = p.Contracts.Add(new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 430, Date = 0, LastDate = 5, Premium = 1, Penalty = 1 });
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Contract, big, 4, 12));
            Assert.Equal(EntryOkay.TooSmall, pl.Plan.Entries.First(x => x.ObjectId == big && x.Kind == FlightKind.Contract).Okay);
        }

        [Fact]
        public void FreightTonnage_AllocatedOverPlannedFlights()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var pl = OpsWorld.AddPlane(w, p);   // 168 seats → 16 t per flight
            int id = p.Freights.Add(new FreightContract { From = OpsWorld.C(1), To = OpsWorld.C(2), Tons = 40, TonsLeft = 40, TonsOpen = 40, Date = 0, LastDate = 6, Premium = 90000, Penalty = 10 });
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Freight, id, 1, 8));
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Freight, id, 2, 8));
            var f = p.Freights.Find(id);
            Assert.Equal(8, f.TonsOpen);
            Assert.Equal(PlanState.NotPlanned, f.InPlan);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Freight, id, 3, 8));
            Assert.Equal(0, f.TonsOpen);
            Assert.Equal(PlanState.Planned, f.InPlan);
            var tons = pl.Plan.Entries.Where(x => x.Kind == FlightKind.Freight).Select(x => x.Passengers).ToArray();
            Assert.Equal(new[] { 16, 16, 8 }, tons);

            // premium only on the flight that brings TonsLeft to 0 (§5.3)
            long incomeSum = 0;
            w.Events.Subscribe<FlightBooked>(b => incomeSum += b.Income);
            for (int i = 0; i < FlightPlan.Size; i++)
                if (pl.Plan.Entries[i].Kind == FlightKind.Freight) FlightOps.BookFlight(w, p, pl, i);
            Assert.Equal(90000, incomeSum);
            Assert.Equal(0, f.TonsLeft);
            Assert.True(pl.SeatsRemoved);
        }

        [Fact]
        public void Advisor_RatingThresholds()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var c = new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 90, Date = 0, LastDate = 0, Premium = 100000, Penalty = 1 };
            Assert.Equal(7010, ContractAdvisor.Rate(w, p, c));   // no plane at all
            OpsWorld.AddPlane(w, p);
            Assert.Equal(7001, ContractAdvisor.Rate(w, p, c));   // cost 35 500 ≤ 0.5 · 100 000
            c.Premium = 50000;
            Assert.Equal(7002, ContractAdvisor.Rate(w, p, c));
            c.Penalty = 0;
            Assert.Equal(7000, ContractAdvisor.Rate(w, p, c));
        }
    }
}
