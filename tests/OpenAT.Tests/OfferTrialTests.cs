using System.IO;
using System.Linq;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Save;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>"Try in planner" at the counters (docs/IMPROVEMENTS.md): isolation, live equivalence, staleness, atomic apply.</summary>
    public class OfferTrialTests
    {
        static (World w, Player p, Plane pl) Setup()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            w.StartWorkingDay();
            // Bravo → Alpha: the plane stands in Alpha (home), so a ferry flight is needed first.
            w.TravelAgency.Offers[2] = new Contract { From = OpsWorld.C(2), To = OpsWorld.C(1), Persons = 90, Date = 1, LastDate = 3, Premium = 50000, Penalty = 10000 };
            w.FreightOffice.Offers[4] = new FreightContract { From = OpsWorld.C(1), To = OpsWorld.C(3), Tons = 30, Date = 1, LastDate = 4, Premium = 70000, Penalty = 5000 };
            return (w, p, pl);
        }

        static byte[] Bytes(World w)
        {
            using (var ms = new MemoryStream()) { SaveGame.Write(w, ms, "X"); return ms.ToArray(); }
        }

        [Fact]
        public void Trial_LeavesTheLiveWorldUntouched()
        {
            var (w, p, pl) = Setup();
            var before = Bytes(w);
            int events = 0;
            w.Events.Subscribe<ContractAccepted>(_ => events++);
            w.Events.Subscribe<CommandExecuted>(_ => events++);
            var t = new OfferTrial(w, 0, OfferBoard.TravelAgency, 2);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 10 });
            var r = t.Evaluate(w);
            Assert.True(r.AllOk);
            Assert.Equal(0, events);
            Assert.Equal(before, Bytes(w));
            Assert.Equal(0, p.Contracts.Count);
            Assert.False(w.TravelAgency.IsEmpty(2));
            Assert.NotSame(w, r.Sandbox);
        }

        [Fact]
        public void Trial_MatchesTheLiveCommands()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.TravelAgency, 2);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 10 });
            var r = t.Evaluate(w);
            var leg = r.Legs.Single();
            Assert.True(leg.Ok);
            Assert.Equal(1, leg.FerriesAdded);   // Alpha → Bravo first

            var acc = new AcceptContractCommand(0, OfferBoard.TravelAgency, 2);
            Assert.True(w.Execute(acc).Ok);
            Assert.Equal(r.ObjectId, acc.ContractId);   // the trial predicts the accepted id
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Contract, acc.ContractId, 1, 10) { ClickDate = 1, ClickHour = 10 }).Ok);
            var sandboxPlan = r.Sandbox.Players[0].Planes.Find(pl.Id).Plan.Entries;
            Assert.Equal(sandboxPlan.Select(e => e.ToString()), pl.Plan.Entries.Select(e => e.ToString()));
            var live = pl.Plan.Entries.First(e => e.Kind == FlightKind.Contract);
            Assert.Equal(live.StartAbs, leg.StartAbs);
        }

        [Fact]
        public void Trial_GoesStaleWhenTheSlotChanges()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.TravelAgency, 2);
            Assert.True(t.OfferStillThere(w));
            // The board refills the slot with another offer: same slot number, different content.
            w.TravelAgency.Offers[2] = new Contract { From = OpsWorld.C(3), To = OpsWorld.C(1), Persons = 50, Date = 1, LastDate = 2, Premium = 30000, Penalty = 0 };
            Assert.False(t.OfferStillThere(w));
            Assert.Equal("offer-gone", t.Evaluate(w).AcceptFailure);
        }

        [Fact]
        public void Trial_UsesTheRealRules_ClosedCounterAndOutsideWeek()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.TravelAgency, 2);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 9, Hour = 10 });
            var r = t.Evaluate(w);
            Assert.False(r.Legs[0].Ok);
            Assert.Equal("outside-week", r.Legs[0].Reason);
            w.Clock.Time = GameClock.At(17, 30);
            Assert.Equal("closed", t.Evaluate(w).AcceptFailure);
        }

        [Fact]
        public void Freight_SeveralLegsAssignTonnage()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.FreightOffice, 4);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 8 });
            var r1 = t.Evaluate(w);
            Assert.True(r1.AllOk);
            Assert.Equal(30, r1.Tons);
            Assert.True(r1.TonsOpen < 30);
            Assert.Equal(30 - r1.TonsOpen, r1.Legs[0].Tons);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 2, Hour = 8 });
            var r2 = t.Evaluate(w);
            Assert.True(r2.AllOk);
            Assert.True(r2.TonsOpen < r1.TonsOpen);
            Assert.Equal(0, p.Freights.Count);   // nothing accepted live
        }

        [Fact]
        public void Apply_PlacesAllLegsAfterAcceptance()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.FreightOffice, 4);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 8 });
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 2, Hour = 8 });
            var seen = t.Evaluate(w);
            var acc = new AcceptFreightCommand(0, OfferBoard.FreightOffice, 4);
            Assert.True(w.Execute(acc).Ok);
            Assert.Null(t.Apply(w, acc.FreightId, seen));
            Assert.Equal(2, pl.Plan.Entries.Count(e => e.Kind == FlightKind.Freight && e.ObjectId == acc.FreightId));
            Assert.Equal(seen.TonsOpen, p.Freights.Find(acc.FreightId).TonsOpen);
        }

        [Fact]
        public void Apply_IsAllOrNothing()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.FreightOffice, 4);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 8 });
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 12, Hour = 8 });   // outside the planner week
            var seen = t.Evaluate(w);
            var acc = new AcceptFreightCommand(0, OfferBoard.FreightOffice, 4);
            Assert.True(w.Execute(acc).Ok);
            var before = pl.Plan.Entries.Select(e => e.ToString()).ToArray();
            Assert.Equal("outside-week", t.Apply(w, acc.FreightId, seen));
            Assert.Equal(before, pl.Plan.Entries.Select(e => e.ToString()).ToArray());
            Assert.Equal(30, p.Freights.Find(acc.FreightId).TonsOpen);   // accepted, unscheduled
        }

        [Fact]
        public void Apply_RefusesWhenTheLivePlanChanged()
        {
            var (w, p, pl) = Setup();
            var t = new OfferTrial(w, 0, OfferBoard.TravelAgency, 2);
            t.Legs.Add(new TrialLeg { PlaneId = pl.Id, Date = 1, Hour = 10 });
            var seen = t.Evaluate(w);
            // Meanwhile the player plans the plane elsewhere: the same request now has another outcome.
            var ab = w.RouteTable.First(r => r.From == OpsWorld.C(1) && r.To == OpsWorld.C(3));
            Sim.Routes.RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 1, 4)).Ok);
            var acc = new AcceptContractCommand(0, OfferBoard.TravelAgency, 2);
            Assert.True(w.Execute(acc).Ok);
            var before = pl.Plan.Entries.Select(e => e.ToString()).ToArray();
            Assert.Equal("changed", t.Apply(w, acc.ContractId, seen));
            Assert.Equal(before, pl.Plan.Entries.Select(e => e.ToString()).ToArray());
        }
    }
}
