using System.Linq;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Sim commands used by the planner UI (rooms-and-ui-5b §6.5).</summary>
    public class Rooms2PlannerTests
    {
        static Route RouteBetween(World w, int a, int b) => w.RouteTable.First(r => r.From == OpsWorld.C(a) && r.To == OpsWorld.C(b));

        static (World w, Player p, Plane pl, Route ab) Setup()
        {
            var w = OpsWorld.NewWorld();
            var p = w.Players[0];
            OpsWorld.UsePlaneCrew(w);
            var pl = OpsWorld.AddPlane(w, p);
            var ab = RouteBetween(w, 1, 2);
            RouteOps.Rent(w, p, ab.From, ab.To, ab.Rent);
            return (w, p, pl, ab);
        }

        static int IndexOf(Plane pl, int kind) => System.Array.FindIndex(pl.Plan.Entries, e => e.Kind == kind);

        [Fact]
        public void Move_ShiftsFlightAndKeepsDuration()
        {
            var (w, p, pl, ab) = Setup();
            Assert.True(w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 1, 10)).Ok);
            int i = IndexOf(pl, FlightKind.Route);
            var before = pl.Plan.Entries[i];
            int dur = before.LandAbs - before.StartAbs;
            Assert.True(w.Execute(new MoveFlightCommand(0, pl.Id, i, 2, 15)).Ok);
            var e = pl.Plan.Entries[IndexOf(pl, FlightKind.Route)];
            Assert.Equal((2, 15), (e.StartDate, e.StartHour));
            Assert.Equal(dur, e.LandAbs - e.StartAbs);
        }

        [Fact]
        public void Move_ClampsToPlannerWeek()
        {
            var (w, p, pl, ab) = Setup();
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 2, 10));
            // today (day 0, 9:00) → at least 11:00
            var cmd = new MoveFlightCommand(0, pl.Id, IndexOf(pl, FlightKind.Route), 0, 5);
            Assert.True(w.Execute(cmd).Ok);
            Assert.Equal((0, 11), (cmd.NewDate, cmd.NewHour));
            // beyond the last day → day 6, 23:00
            int d = 9, h = 3;
            MoveFlightCommand.Clamp(w, ref d, ref h);
            Assert.Equal((6, 23), (d, h));
            // hour 24 rolls to the next day
            d = 3; h = 24;
            MoveFlightCommand.Clamp(w, ref d, ref h);
            Assert.Equal((4, 0), (d, h));
        }

        [Fact]
        public void Move_ChainKeepsOffsetsOfLaterFlights()
        {
            var (w, p, pl, ab) = Setup();
            var ba = RouteBetween(w, 2, 1);
            RouteOps.Rent(w, p, ba.From, ba.To, ba.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ab.Id, 1, 8));
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ba.Id, 1, 14));
            var first = pl.Plan.Entries.First(e => e.ObjectId == ab.Id);
            var second = pl.Plan.Entries.First(e => e.ObjectId == ba.Id);
            int gap = second.StartAbs - first.StartAbs;
            int idx = System.Array.FindIndex(pl.Plan.Entries, e => e.ObjectId == ab.Id && e.Kind == FlightKind.Route);
            Assert.True(w.Execute(new MoveFlightCommand(0, pl.Id, idx, 2, 8, chain: true)).Ok);
            first = pl.Plan.Entries.First(e => e.ObjectId == ab.Id);
            second = pl.Plan.Entries.First(e => e.ObjectId == ba.Id);
            Assert.Equal((2, 8), (first.StartDate, first.StartHour));
            Assert.Equal(gap, second.StartAbs - first.StartAbs);
        }

        [Fact]
        public void Move_RefusesLockedAndAutoFlights()
        {
            var (w, p, pl, ab) = Setup();
            // plane starts in Alpha; a Bravo → Alpha route forces a ferry flight first
            var ba = RouteBetween(w, 2, 1);
            RouteOps.Rent(w, p, ba.From, ba.To, ba.Rent);
            w.Execute(new PlaceFlightCommand(0, pl.Id, FlightKind.Route, ba.Id, 1, 12));
            int auto = IndexOf(pl, FlightKind.Auto);
            Assert.True(auto >= 0);
            Assert.Equal("bad-entry", w.Execute(new MoveFlightCommand(0, pl.Id, auto, 2, 10)).Reason);
            Assert.Equal("bad-entry", w.Execute(new MoveFlightCommand(0, pl.Id, 59, 2, 10)).Reason);
        }
    }
}
