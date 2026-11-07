using System.Collections.Generic;
using System.Linq;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Routes;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Hall ↔ operations (HallFlightLink, HallPlanes): flight lookups, gate owners, flight displays,
    /// gate count, departing and arriving passengers and the plane animation (airport-and-people.md §6.6,
    /// §7, §9.2–9.3; planes-and-flights.md §11; sim-core.md §2.3–2.4).
    /// </summary>
    public class HallFlightsTests
    {
        // ------------------------------------------------------------------ without the install

        /// <summary>Synthetic world with one route flight from home (gate 0) at 11:00, departing day 0.</summary>
        static (World w, Plane plane, int ri) OpsWithFlight()
        {
            var w = OpsWorld.NewWorld();
            OpsWorld.UsePlaneCrew(w); // crew on the plane, so the flight is not delayed
            var p = w.Players[0];
            p.Owner = Owner.Human;
            for (int c = 0; c < Player.Count; c++) w.Players[c].Gates.Rent(c, 1000);
            var plane = OpsWorld.AddPlane(w, p);
            int ri = w.RouteIndex(OpsWorld.C(1), OpsWorld.C(2));
            RouteOps.RentAtOffice(w, p, ri);
            w.StartWorkingDay();
            var r = w.Execute(new PlaceFlightCommand(0, plane.Id, FlightKind.Route, w.RouteTable[ri].Id, 0, 11));
            Assert.True(r.Ok, r.ToString());
            return (w, plane, ri);
        }

        static int EntryAt(Plane plane, int date, int hour)
        {
            for (int i = 0; i < FlightPlan.Size; i++)
                if (plane.Plan.Entries[i].Kind != 0 && plane.Plan.Entries[i].StartDate == date && plane.Plan.Entries[i].StartHour == hour) return i;
            return -1;
        }

        [Fact]
        public void Link_TryGetFlight_ReturnsGateAndStartHour()
        {
            var (w, plane, _) = OpsWithFlight();
            var link = new HallFlightLink(w);
            int e = EntryAt(plane, 0, 11);
            Assert.True(e >= 0);
            Assert.True(link.TryGetFlight(0, plane.Id, e, out int gate, out int start));
            Assert.Equal(0, gate);
            Assert.Equal(11, start);
            Assert.False(link.TryGetFlight(0, plane.Id + 999, e, out _, out _));
            Assert.False(link.TryGetFlight(0, plane.Id, -1, out _, out _));
            Assert.False(link.TryGetFlight(7, plane.Id, e, out _, out _));
        }

        [Fact]
        public void Link_GateOwner_FromRentedGates()
        {
            var (w, _, _) = OpsWithFlight();
            var link = new HallFlightLink(w);
            for (int c = 0; c < Player.Count; c++) Assert.Equal(c, link.GateOwner(c));
            Assert.Equal(-1, link.GateOwner(9));
            w.Players[2].IsOut = true;
            Assert.Equal(-1, link.GateOwner(2));
        }

        [Fact]
        public void Link_FlightDisplays_ShowNextHourDeparturesAndArrivals()
        {
            var (w, plane, _) = OpsWithFlight();
            var link = new HallFlightLink(w);
            // 09:xx: departure 11:00 is not yet this or next hour.
            Assert.Equal("", link.FlightDisplayText(0));
            OpsWorld.RunUntil(w, 0, 10, 5);
            // Check-in (0) and gate (30) displays of gate 0: "FROM-TO hh:00" with hh = start + 1 (§9.3).
            Assert.Equal("ALP-BRA 12:00", link.FlightDisplayText(0));
            Assert.Equal("ALP-BRA 12:00", link.FlightDisplayText(30));
            Assert.Equal("Alpha-Bravo 12:00", link.FlightDisplayTooltip(30));
            Assert.Equal("", link.FlightDisplayText(1));
            Assert.Equal("", link.FlightDisplayText(-1));
            Assert.Equal("", link.FlightDisplayText(60));

            // Arrival at home: the gate display shows the landing hour during the landing hour.
            int e = EntryAt(plane, 0, 11);
            ref var f = ref plane.Plan.Entries[e];
            int back = w.RouteIndex(OpsWorld.C(2), OpsWorld.C(1));
            RouteOps.RentAtOffice(w, w.Players[0], back);
            int startBack = f.LandHour + 1;
            Assert.True(w.Execute(new PlaceFlightCommand(0, plane.Id, FlightKind.Route, w.RouteTable[back].Id, 0, startBack)).Ok);
            int eb = EntryAt(plane, 0, startBack);
            var fb = plane.Plan.Entries[eb];
            Assert.True(fb.Gate >= 0);
            OpsWorld.RunUntil(w, 0, fb.LandHour, 1);
            Assert.Equal("BRA-ALP " + fb.LandHour + ":00", link.FlightDisplayText(30 + fb.Gate));
        }

        [Fact]
        public void GateCount_FallsBackToFourWithoutHall()
        {
            var w = OpsWorld.NewWorld();
            Assert.Equal(4, new HallGateCount().GateCount(w));
        }

        [Fact]
        public void Link_SpawnsNothingWithoutGate()
        {
            var (w, plane, _) = OpsWithFlight();
            int e = EntryAt(plane, 0, 11);
            plane.Plan.Entries[e].Gate = GateCode.None;
            var link = new HallFlightLink(w);
            OpsWorld.RunUntil(w, 0, 10, 30);
            Assert.Equal("", link.FlightDisplayText(0));
            Assert.Equal(0, plane.Plan.Entries[e].PArrived);
        }

        // ------------------------------------------------------------------ with the install (hall layout)

        static GameInstall _install;
        static HallData _hall;
        static GameData _data;
        static readonly object Lock = new object();

        static bool Load()
        {
            lock (Lock)
            {
                if (_hall != null) return true;
                _install = GameInstall.TryLocate();
                if (_install == null) return false;
                _hall = HallData.Load(_install);
                _data = GameData.Load(_install);
                HallModule.Register();
                HallContent.Attach(_data, _hall);
                return true;
            }
        }

        /// <summary>Free game with a hall; airline 0 (human) flies its first plane out at 11:00 and back.</summary>
        static (World w, Plane plane, int outEntry, int backEntry) HallWorldWithFlights(int seed = 4711)
        {
            var w = World.NewGame(_data, new NewGameSettings { StartTime = 0, Seed = seed }, new GameOptions { RandomStartDay = false });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            var p = w.Players[0];
            var plane = p.Planes.Items.First();
            int ri = -1;
            var cand = Enumerable.Range(0, w.RouteTable.Count)
                .Where(i => w.RouteTable[i].From == w.HomeCity && w.Distance(w.RouteTable[i].From, w.RouteTable[i].To) <= plane.Range * 1000)
                .OrderBy(i => w.Distance(w.RouteTable[i].From, w.RouteTable[i].To)).ThenBy(i => i);
            foreach (int i in cand)
                if (w.Execute(new RentRouteCommand(0, i)).Ok) { ri = i; break; }
            Assert.True(ri >= 0);
            w.Hall.PlaceAfterBriefing();
            w.StartWorkingDay();
            var route = w.RouteTable[ri];
            Assert.True(w.Execute(new PlaceFlightCommand(0, plane.Id, FlightKind.Route, route.Id, 0, 11)).Ok);
            int dur = FlightPlanner.Duration(w, plane, route.From, route.To);
            int back = w.ReverseRouteIndex(ri);
            Assert.True(w.Execute(new PlaceFlightCommand(0, plane.Id, FlightKind.Route, w.RouteTable[back].Id, 0, 11 + dur + 1)).Ok);
            return (w, plane, EntryAt(plane, 0, 11), EntryAt(plane, 0, 11 + dur + 1));
        }

        [Fact]
        public void Hall_HasLinkAndLayoutGateCount()
        {
            if (!Load()) return;
            var (w, _, _, _) = HallWorldWithFlights();
            Assert.IsType<HallFlightLink>(w.Hall.Flights);
            Assert.Equal(w.Hall.Layout.GateCount, w.Rules.Get<IAirportGateCount>().GateCount(w));
            Assert.True(w.Hall.Layout.GateCount >= 4);
            Assert.Equal(0, w.Hall.Flights.GateOwner(0));
        }

        [Fact]
        public void Hall_DepartingPassengersWalkToTheGateAndBoard()
        {
            if (!Load()) return;
            var (w, plane, e, _) = HallWorldWithFlights();
            var f0 = plane.Plan.Entries[e];
            Assert.Equal(0, f0.Gate);
            Assert.True(f0.Passengers + f0.PassengersFC > 0, "no passengers booked: " + f0);
            var h = w.Hall;
            OpsWorld.RunUntil(w, 0, 10, 40);
            // Passengers for the 11:00 departure since 10:00, with the airline and plane of the flight.
            var pax = h.Persons.Where(x => x.Reason == Reason.Flying && x.FlightAirline == 0 && x.FlightPlaneId == plane.Id).ToList();
            Assert.NotEmpty(pax);
            Assert.True(plane.Plan.Entries[e].PArrived > 0);
            Assert.Contains(pax, x => x.BaseState != PState.ToCheckIn); // some already past the queue start

            // At :30 the plane comes to the gate behind the windows and stops at RUNE_2WAIT x + 200.
            OpsWorld.RunUntil(w, 0, 11, 31);
            var hp = h.FindPlane(0, plane.Id);
            Assert.NotNull(hp);
            Assert.Equal(PlaneLocation.AtGate, hp.Ort);
            Assert.True(hp.Pos.X > hp.TargetX);
            bool atGate = false;
            int boardingSeen = 0;
            while (w.Clock.Time < GameClock.At(11, 55))
            {
                w.Tick();
                if (h.PlaneAtGate(0, plane.Id)) atGate = true;
                if (atGate) boardingSeen = System.Math.Max(boardingSeen, h.Persons.Count(x => x.Reason == Reason.Flying && x.FlightPlaneId == plane.Id
                    && (x.BaseState == PState.ToScanner || x.BaseState == PState.Boarding || x.BaseState == PState.EnteringPlane)));
            }
            Assert.True(atGate, "plane never stood at its gate: " + hp);
            Assert.True(boardingSeen > 0, "nobody boarded");

            // From :55 it taxis out, takes off on the runway and the flight is booked.
            int booked = 0;
            w.Events.Subscribe<FlightBooked>(x => { if (x.Plane == plane) booked++; });
            OpsWorld.RunUntil(w, 0, 12, 10);
            Assert.False(h.PlaneAtGate(0, plane.Id));
            Assert.Equal(1, booked);
            Assert.Equal(PlaneLocation.Airborne, plane.Location);
            Assert.True(hp.Ort == PlaneLocation.AtGate && hp.Pos.X < hp.TargetX || hp.Ort == PlaneLocation.TakingOff || hp.Ort == 0, hp.ToString());
        }

        [Fact]
        public void Hall_ArrivingPlaneUnloadsPassengersAtTheGate()
        {
            if (!Load()) return;
            var (w, plane, _, eb) = HallWorldWithFlights();
            var fb = plane.Plan.Entries[eb];
            var h = w.Hall;
            OpsWorld.RunUntil(w, 0, fb.LandHour, 16);
            var hp = h.FindPlane(0, plane.Id);
            Assert.NotNull(hp);
            Assert.Equal(PlaneLocation.Landing, hp.Ort);
            int y0 = hp.Pos.Y, x0 = hp.Pos.X;
            w.Tick();
            Assert.Equal(x0 + 6, hp.Pos.X); // a step every tick while animating
            Assert.Equal(System.Math.Min(40, y0 + 1), hp.Pos.Y);

            int leaving = 0;
            while (w.Clock.Time < GameClock.At(fb.LandHour, 50))
            {
                w.Tick();
                leaving = System.Math.Max(leaving, h.Persons.Count(x => x.Reason == Reason.Leaving && x.FlightAirline == 0)
                                                   + h.Queue.Entries.Count(x => x.TimeSlice != -1 && x.Reason == Reason.Leaving));
            }
            Assert.True(leaving > 0, "no arriving passengers: " + hp);
            Assert.Equal(w.HomeCity, plane.Location);
        }

        [Fact]
        public void Hall_PlanesStepEveryEleventhTickWhenIdle()
        {
            if (!Load()) return;
            var w = World.NewGame(_data, new NewGameSettings { StartTime = 0, Seed = 3 }, new GameOptions { RandomStartDay = false });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            var plane = w.Players[0].Planes.Items.First();
            w.StartWorkingDay();
            for (int i = 0; i < 12; i++) w.Tick();
            // A take-off seen while idle starts at the next step (≤ 11 ticks later), then moves every tick.
            plane.Location = PlaneLocation.TakingOff;
            plane.PhaseStartMinute = w.AbsoluteMinute;
            var h = w.Hall;
            int ticks = 0;
            while ((h.FindPlane(0, plane.Id)?.Ort ?? 0) != PlaneLocation.TakingOff && ticks < 20) { w.Tick(); ticks++; }
            Assert.InRange(ticks, 1, 11);
            var hp = h.FindPlane(0, plane.Id);
            int x = hp.Pos.X;
            w.Tick();
            Assert.Equal(x + 7, hp.Pos.X);
            plane.Location = w.HomeCity;
        }

        [Fact]
        public void Hall_FlightsAreDeterministic()
        {
            if (!Load()) return;
            string Run()
            {
                var (w, plane, _, _) = HallWorldWithFlights(99);
                var sb = new System.Text.StringBuilder();
                for (int t = 0; t < 14; t++)
                {
                    OpsWorld.RunUntil(w, 0, 10 + t / 2, (t % 2) * 30 + 5);
                    var hp = w.Hall.FindPlane(0, plane.Id);
                    sb.Append(w.Hall.Persons.Count).Append('/').Append(hp?.ToString() ?? "-").Append(';');
                    foreach (var p in w.Hall.Persons) sb.Append(p.Position.X).Append(',').Append(p.Position.Y).Append(' ');
                }
                return sb.ToString();
            }
            Assert.Equal(Run(), Run());
        }
    }
}
