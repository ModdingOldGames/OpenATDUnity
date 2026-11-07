using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Items;
using OpenAT.Sim.Airport;
using OpenAT.Sim.Routes;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Route advisor, partner branches, order analyst, manual cancel, robot ads, default home (docs/mechanics).</summary>
    public class RoutesOrdersFixesTests
    {
        static int RouteIdx(World w, int fromCsv, int toCsv)
        {
            for (int i = 0; i < w.RouteTable.Count; i++)
                if (w.RouteTable[i].From == OpsWorld.C(fromCsv) && w.RouteTable[i].To == OpsWorld.C(toCsv)) return i;
            return -1;
        }

        [Fact]
        public void RouteAdvisor_BlockedOnlyInCooldown_CitySizeAndLevelRemark()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            int ri = RouteIdx(w, 1, 2);            // Alpha (2 000 000, not above) - Bravo, level 1, factor 1.0
            Assert.Equal((8002, OpsWorld.C(2), 0, -1), RouteOps.Advice(w, p, ri));

            // Cooldown after a revocation: unheld and DaysLow < 7 → only the blocked line.
            p.RentRoutes[ri].DaysLow = 3;
            Assert.Equal((8020, 0, 0, -1), RouteOps.Advice(w, p, ri));
            // Held routes never show it; a merely ineligible route (DaysLow 99) neither.
            p.RentRoutes[ri].Rank = 1;
            Assert.Equal(8002, RouteOps.Advice(w, p, ri).Text);
            p.RentRoutes[ri].Rank = 0; p.RentRoutes[ri].DaysLow = 99;
            int far = RouteIdx(w, 3, 4);
            Assert.False(RouteOps.IsRentable(w, p, far));
            Assert.NotEqual(8020, RouteOps.Advice(w, p, far).Text);

            // City size: one big endpoint names it, both big name both.
            w.City(OpsWorld.C(2)).Population = 2500000;
            Assert.Equal((8001, OpsWorld.C(2), 0, -1), RouteOps.Advice(w, p, ri));
            w.City(OpsWorld.C(1)).Population = 2000001;
            Assert.Equal((8000, OpsWorld.C(1), OpsWorld.C(2), -1), RouteOps.Advice(w, p, ri));
            w.City(OpsWorld.C(2)).Population = 1000000;
            Assert.Equal((8001, OpsWorld.C(1), 0, -1), RouteOps.Advice(w, p, ri));

            // Demand remark from the factor against the route level (not the seat load).
            var r = w.RouteTable[ri];
            p.RentRoutes[ri].Load = 90;
            r.Factor = 1.6; Assert.Equal(8010, RouteOps.Advice(w, p, ri).Remark);
            r.Factor = 1.5; Assert.Equal(-1, RouteOps.Advice(w, p, ri).Remark);
            r.Factor = 0.6; Assert.Equal(8011, RouteOps.Advice(w, p, ri).Remark);
            r.Level = 2; r.Factor = 3.5; Assert.Equal(8010, RouteOps.Advice(w, p, ri).Remark);
            r.Factor = 2.0; Assert.Equal(-1, RouteOps.Advice(w, p, ri).Remark);
        }

        [Fact]
        public void PartnerBranch_HumanCanTakeOrdersThroughCooperatingAirline()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var partner = w.Players[1];
            int city = w.CityIndex(OpsWorld.C(3));
            w.ForeignContracts[city].Offers[0] = new Contract { From = OpsWorld.C(3), To = OpsWorld.C(2), Persons = 90, Premium = 50000, Penalty = 1000 };
            w.ForeignFreight[city].Offers[0] = new FreightContract { From = OpsWorld.C(3), To = OpsWorld.C(2), Tons = 15, Premium = 40000, Penalty = 1000 };

            Assert.Equal("no-branch", w.Execute(new AcceptContractCommand(p.Index, OfferBoard.Branch, 0, city)).Reason);
            partner.RentCities[city].Rank = 1;
            Assert.Equal("no-branch", w.Execute(new AcceptContractCommand(p.Index, OfferBoard.Branch, 0, city)).Reason);
            p.Cooperates[partner.Index] = true;
            Assert.True(w.Execute(new AcceptContractCommand(p.Index, OfferBoard.Branch, 0, city)).Ok);
            Assert.True(w.Execute(new AcceptFreightCommand(p.Index, OfferBoard.BranchFreight, 0, city)).Ok);
            Assert.Single(p.Contracts.Items);
            Assert.Single(p.Freights.Items);

            // A partner that went out no longer lends its branches.
            partner.IsOut = true;
            Assert.False(CounterHours.HasBranch(w, p, city));
        }

        [Fact]
        public void Analyst_FreightChecksFerryAndRange_PassengerUsesConfiguredSeats()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            var f = new FreightContract { From = OpsWorld.C(1), To = OpsWorld.C(2), Tons = 15, Date = 1, LastDate = 2, Premium = 100000, Penalty = 1 };
            Assert.Equal(7110, ContractAdvisor.RateFreight(w, p, f));      // no plane has the range
            var pl = OpsWorld.AddPlane(w, p);
            Assert.Equal(7101, ContractAdvisor.RateFreight(w, p, f));
            f.Premium = 0;
            Assert.Equal(7105, ContractAdvisor.RateFreight(w, p, f));

            // A future ferry flight on the pair within the window can carry it.
            pl.Plan.Entries[0] = new FlightEntry { Kind = FlightKind.Auto, From = OpsWorld.C(1), To = OpsWorld.C(2), StartDate = 2, StartHour = 10 };
            Assert.Equal(7113, ContractAdvisor.RateFreight(w, p, f, out var ferry));
            Assert.Same(pl, ferry);

            // Passenger orders: the ferry test compares with economy + first-class seats as configured.
            var c = new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 140, Date = 1, LastDate = 2, Premium = 100000, Penalty = 1 };
            pl.MaxPassengers = 120; pl.MaxPassengersFC = 21;
            Assert.Equal(7013, ContractAdvisor.Rate(w, p, c));
            pl.MaxPassengersFC = 10;                                        // 130 < 140 although the type has 168 seats
            Assert.Equal(7011, ContractAdvisor.Rate(w, p, c));
        }

        [Fact]
        public void Cancel_BooksOwnReason_PenaltyStatistic_AndProfit()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            long money = p.Money;
            int id = p.Contracts.Add(new Contract { From = OpsWorld.C(1), To = OpsWorld.C(2), Persons = 90, Date = 1, LastDate = 1, Premium = 50000, Penalty = 7000 });
            int fid = p.Freights.Add(new FreightContract { From = OpsWorld.C(1), To = OpsWorld.C(2), Tons = 15, Date = 1, LastDate = 1, Premium = 40000, Penalty = 3000 });
            var reasons = new System.Collections.Generic.List<int>();
            w.Events.Subscribe<OpenAT.Sim.MoneyChanged>(e => reasons.Add(e.Reason));
            long profit = p.Profit;

            Assert.True(w.Execute(new CancelContractCommand(p.Index, id)).Ok);
            Assert.True(w.Execute(new CancelContractCommand(p.Index, fid, freight: true)).Ok);

            Assert.Equal(money - 10000, p.Money);
            Assert.Equal(-10000, p.Statistics.Today(StatId.ExpensePenalties));
            Assert.Equal(profit - 10000, p.Profit);
            Assert.Equal(new[] { OpenAT.Sim.Finance.MoneyReason.ContractPenalty, OpenAT.Sim.Finance.MoneyReason.FreightPenalty }, reasons.ToArray());
        }

        [Fact]
        public void RobotCampaignSize_FleetBumpAlsoOnShortCash()
        {
            // date 1: not a third day → +1 with more than five planes, also over the joke size.
            Assert.Equal(1, RobotExecutor.CampaignSize(false, 1000000, 0, 6, 1));
            Assert.Equal(0, RobotExecutor.CampaignSize(false, 1000000, 0, 5, 1));
            Assert.Equal(0, RobotExecutor.CampaignSize(false, 1000000, 0, 6, 3));   // third day: joke size, no bump
            Assert.Equal(2, RobotExecutor.CampaignSize(false, 2000000, 0, 6, 1));
            Assert.Equal(4, RobotExecutor.CampaignSize(false, 2000000, -100, 6, 1)); // capped at 4
            Assert.Equal(5, RobotExecutor.CampaignSize(true, 5000000, 0, 6, 1));
            Assert.Equal(0, RobotExecutor.CampaignSize(true, 5000000, 0, 6, 4));
        }

        [Theory]
        [InlineData(OpenAT.Formats.TextResource.Language.German, "Berlin")]
        [InlineData(OpenAT.Formats.TextResource.Language.English, "London")]
        [InlineData(OpenAT.Formats.TextResource.Language.French, "Paris")]
        [InlineData(OpenAT.Formats.TextResource.Language.Italian, "Roma")]
        [InlineData(OpenAT.Formats.TextResource.Language.Spanish, "Madrid")]
        [InlineData(OpenAT.Formats.TextResource.Language.Portuguese, "Madrid")]
        [InlineData(OpenAT.Formats.TextResource.Language.Polish, "Berlin")]
        public void DefaultHome_ByLanguage_FallsBackToBerlin(OpenAT.Formats.TextResource.Language lang, string home)
        {
            var d = OpsWorld.Data();
            string[] names = { "Berlin", "London", "Paris", "Roma", "Madrid" };
            for (int i = 0; i < names.Length; i++) d.Cities.All[i + 1].Name = names[i];
            d.StdText = new OpenAT.Formats.TextResource { Lang = lang };
            var w = World.NewGame(d, new NewGameSettings { Seed = 1 });
            Assert.Equal(home, w.City(w.HomeCity).Name);
        }

        [Fact]
        public void Sabotage1_RouteImageTimes099()
        {
            var w = ItemsWorld.Make();
            var s = w.Players[0];
            var v = w.Players[1];
            int ri = RouteIdx(w, 1, 2);
            v.RentRoutes[ri].Rank = 1;
            v.RentRoutes[ri].Image = 50;
            var pl = OpsWorld.AddPlane(w, v);
            pl.Location = OpsWorld.C(1);
            pl.Plan.Entries[0] = new FlightEntry
            {
                Kind = FlightKind.Route, ObjectId = w.RouteTable[ri].Id, From = OpsWorld.C(1), To = OpsWorld.C(2), Gate = GateCode.None,
                StartDate = w.Clock.Date, StartHour = 12, LandDate = w.Clock.Date, LandHour = 15,
            };
            pl.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            s.Sabotage.Trust = 1;
            Assert.True(ItemsWorld.Order(w, 0, 1, PrankFamily.Plane, 1, pl.Id).Ok);
            SabotageEngine.HourlyPlanePranks(w);
            pl.Location = PlaneLocation.Airborne;
            SabotageEngine.HourlyPlanePranks(w);
            Assert.Equal(49, v.RentRoutes[ri].Image);                     // 50 × 0.99, truncated
        }
    }
}
