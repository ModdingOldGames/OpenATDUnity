using System;
using OpenAT.Sim.Core;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Fleet
{
    // ------------------------------------------------------------------ interfaces to other domains
    // Defaults reproduce the original where operations alone can do so; the owning domain replaces
    // them (docs/spec/integration.md "Operations needs from Finance").

    /// <summary>
    /// The airline's fuel state (planes-and-flights.md §12.2, §16). Owned by the finance/kerosene
    /// domain; the default is "normal kerosene, tank closed and empty".
    /// </summary>
    public interface IFuelAccount : IRule
    {
        /// <summary>0 premium, 1 normal, 2 cheap ("KerosinKind").</summary>
        int Kind(World w, Player p);
        bool TankOpen(World w, Player p);
        int TankContent(World w, Player p);
        /// <summary>Average purchase price of the tank stock ("TankPreis").</summary>
        double TankPrice(World w, Player p);
        /// <summary>Cheap units in the tank ("BadKerosin").</summary>
        int BadKerosene(World w, Player p);
        /// <summary>
        /// Takes <paramref name="units"/> from an open tank after a booking (bad fuel first) and books the
        /// calculatory cost (units x tank price) to the balance sheet and statistics; returns the units
        /// taken and that cost (for the plane's P&amp;L).
        /// </summary>
        int Drain(World w, Player p, int units, string planeName, out long imputedCost);
    }

    public sealed class NoTankFuelAccount : IFuelAccount
    {
        public int Kind(World w, Player p) => 1;
        public bool TankOpen(World w, Player p) => false;
        public int TankContent(World w, Player p) => 0;
        public double TankPrice(World w, Player p) => 0;
        public int BadKerosene(World w, Player p) => 0;
        public int Drain(World w, Player p, int units, string planeName, out long imputedCost) { imputedCost = 0; return 0; }
    }

    /// <summary>Flight fuel cost formulas (planes-and-flights.md §12.2, contracts-and-routes.md §2.2).</summary>
    public interface IFlightFuelCost : IRule
    {
        /// <summary>Fuel units F = d/1000 × burn / 160 / speed (integer, left to right).</summary>
        int FuelUnits(int distanceM, int burn, int speed);
        /// <summary>Estimate (CalculateFlightCost): tank units free, rest at market × kind; minimum 1000. Null player = F·K.</summary>
        int Estimate(World w, Player p, int from, int to, int burn, int speed);
        /// <summary>Planner display (…Rechnerisch): tank units at the tank price; minimum 1000.</summary>
        int Planner(World w, Player p, int from, int to, int burn, int speed);
        /// <summary>Booking (CalculateRealFlightCost): tank units free, rest at market × kind; no minimum.</summary>
        int Booking(World w, Player p, int from, int to, int burn, int speed);
        /// <summary>Bad-kerosene wear factor 1 or 3 (§16), evaluated before the tank is drained.</summary>
        int BadKeroseneFactor(World w, Player p);
    }

    public sealed class OriginalFlightFuelCost : IFlightFuelCost
    {
        public int FuelUnits(int distanceM, int burn, int speed) => speed <= 0 ? 0 : distanceM / 1000 * burn / 160 / speed;

        static int Market(World w, Player p, int units)
        {
            if (p == null) return units * w.Kerosene;
            switch (w.Rules.Get<IFuelAccount>().Kind(w, p))
            {
                case 0: return units * w.Kerosene * 2;
                case 2: return units * w.Kerosene / 2;
                default: return units * w.Kerosene;
            }
        }

        int Units(World w, int from, int to, int burn, int speed) => FuelUnits(w.Distance(from, to), burn, speed);

        public int Estimate(World w, Player p, int from, int to, int burn, int speed)
        {
            int f = Units(w, from, to, burn, speed);
            if (p != null)
            {
                var fuel = w.Rules.Get<IFuelAccount>();
                if (fuel.TankOpen(w, p)) f -= Math.Min(fuel.TankContent(w, p), f);
            }
            int k = Market(w, p, f);
            return k < 1000 ? 1000 : k;
        }

        public int Planner(World w, Player p, int from, int to, int burn, int speed)
        {
            int f = Units(w, from, to, burn, speed);
            int cost = 0;
            if (p != null)
            {
                var fuel = w.Rules.Get<IFuelAccount>();
                if (fuel.TankOpen(w, p))
                {
                    int t = Math.Min(fuel.TankContent(w, p), f);
                    cost += (int)(fuel.TankPrice(w, p) * t);
                    f -= t;
                }
            }
            cost += Market(w, p, f);
            return cost < 1000 ? 1000 : cost;
        }

        public int Booking(World w, Player p, int from, int to, int burn, int speed)
        {
            int f = Units(w, from, to, burn, speed);
            var fuel = w.Rules.Get<IFuelAccount>();
            if (fuel.TankOpen(w, p)) f -= Math.Min(fuel.TankContent(w, p), f);
            return Market(w, p, f);
        }

        public int BadKeroseneFactor(World w, Player p)
        {
            var fuel = w.Rules.Get<IFuelAccount>();
            bool open = fuel.TankOpen(w, p);
            int content = fuel.TankContent(w, p);
            if (open && content > 0 && fuel.BadKerosene(w, p) * 100 / content > 50) return 3;
            if ((!open || content == 0) && fuel.Kind(w, p) == 2) return 3;
            return 1;
        }
    }

    /// <summary>The airline's mechanic mode 0..3 ("MechMode", planes-and-flights.md §7.2). Owned by finance; default 2.</summary>
    public interface IMechanicLevel : IRule
    {
        int Level(World w, Player p);
    }

    public sealed class DefaultMechanicLevel : IMechanicLevel
    {
        public int Level(World w, Player p) => 2;
    }

    /// <summary>Strike state of an airline (sim-core.md §6.1, staff domain). Default: never striking.</summary>
    public interface IStrikeStatus : IRule
    {
        bool IsStriking(World w, Player p);
    }

    public sealed class NoStrikes : IStrikeStatus
    {
        public bool IsStriking(World w, Player p) => false;
    }

    /// <summary>
    /// Crew assignment ("MapWorkers" + "UpdatePersonalQuality", planes-and-flights.md §8). The staff
    /// domain owns the worker pool and replaces this. The default distributes
    /// <see cref="Player.PilotCount"/>/<see cref="Player.AttendantCount"/> greedily (plane with the
    /// smallest remaining need first, spare attendants up to the desired count) with quality 60.
    /// </summary>
    public interface ICrewMapping : IRule
    {
        void MapCrew(World w, Player p);
        /// <summary>Crew on board of a plane after the last mapping.</summary>
        CrewState Crew(World w, Player p, Plane plane);
    }

    /// <summary>Assigned crew of a plane ("AnzPiloten", "AnzBegleiter", "PersonalQuality").</summary>
    public struct CrewState
    {
        public int Pilots, Attendants, Quality;
    }

    public sealed class CountCrewMapping : ICrewMapping
    {
        public CrewState Crew(World w, Player p, Plane plane) =>
            new CrewState { Pilots = plane.CrewPilots, Attendants = plane.CrewAttendants, Quality = plane.PersonalQuality };

        public void MapCrew(World w, Player p)
        {
            var planes = p.Planes.ToList();
            foreach (var pl in planes) { pl.CrewPilots = 0; pl.CrewAttendants = 0; pl.PersonalQuality = 60; }
            Assign(planes, p.PilotCount, true);
            int spare = Assign(planes, p.AttendantCount, false);
            for (int i = 0; i < planes.Count && spare > 0; i++)
                while (spare > 0 && planes[i].CrewAttendants < planes[i].MaxAttendants) { planes[i].CrewAttendants++; spare--; }
        }

        static int Assign(System.Collections.Generic.List<Plane> planes, int count, bool pilots)
        {
            while (count > 0)
            {
                int best = -1, bestNeed = 999;
                for (int d = 0; d < planes.Count; d++)
                {
                    int need = pilots ? planes[d].Pilots - planes[d].CrewPilots : planes[d].Attendants - planes[d].CrewAttendants;
                    if (need > 0 && need < bestNeed) { bestNeed = need; best = d; }
                }
                if (best < 0) break;
                if (pilots) planes[best].CrewPilots++; else planes[best].CrewAttendants++;
                count--;
            }
            return count;
        }
    }

    /// <summary>Sabotage aftermath and security flags read by flights (items/sabotage domain). Default: none.</summary>
    public interface IFlightSabotageState : IRule
    {
        /// <summary>"Press statement" penalty active: passengers become 1 + n mod 3 (§12.3).</summary>
        bool PressStatement(World w, Player p);
        /// <summary>Airline index named by an active "advertising brochure" sabotage, or −1 (§12.8).</summary>
        int BrochureTarget(World w, Player p);
        /// <summary>Security office flags; bits 10 and 11 count as luxury for first class (§12.3).</summary>
        int SecurityFlags(World w, Player p);
    }

    public sealed class NoFlightSabotage : IFlightSabotageState
    {
        public bool PressStatement(World w, Player p) => false;
        public int BrochureTarget(World w, Player p) => -1;
        public int SecurityFlags(World w, Player p) => 0;
    }

    /// <summary>Security-advisor rebate on purchases (planes-and-flights.md §4.6; staff domain). Default: none.</summary>
    public interface IPurchaseRebate : IRule
    {
        void Apply(World w, Player p, long price);
    }

    public sealed class NoPurchaseRebate : IPurchaseRebate
    {
        public void Apply(World w, Player p, long price) { }
    }

    /// <summary>
    /// Number of rentable gates = gate runes (brick 2035) in the hall layout (contracts-and-routes.md
    /// §6.1). The airport/layout domain replaces this; the default is the level-1 waiting zone (4).
    /// </summary>
    public interface IAirportGateCount : IRule
    {
        int GateCount(World w);
    }

    public sealed class FixedGateCount : IAirportGateCount
    {
        public int Gates = 4;
        public int GateCount(World w) => Gates;
    }

    /// <summary>Books the night's maintenance (planes-and-flights.md §7.3): extra repair cost + fee/30 per plane, reason 3110.</summary>
    public interface IMaintenanceBooking : IRule
    {
        void Book(World w, Player p, long extraRepairCost, int planes, int mode);
    }

    public sealed class DirectMaintenanceBooking : IMaintenanceBooking
    {
        public void Book(World w, Player p, long extra, int planes, int mode)
        {
            long total = extra + (long)Maintenance.RepairPrice[mode] * planes / 30;
            p.Book(-total, Finance.MoneyReason.Maintenance);
        }
    }

    /// <summary>
    /// Economic side of in-flight incidents (sim-core.md §6.3–6.4). Default: image −2 for a quality
    /// incident (share price ×0.7 and trust need the finance domain), −75 000 (3501) for a seasonal one.
    /// </summary>
    public interface IIncidentEconomics : IRule
    {
        void QualityIncident(World w, Player p);
        void SeasonalIncident(World w, Player p);
    }

    public sealed class BasicIncidentEconomics : IIncidentEconomics
    {
        // Amounts are tunables (Sim/Modding); originals 2 and 75 000.
        public void QualityIncident(World w, Player p) => p.ChangeImage(-(w.Tunables?.Int(Modding.TunableKeys.QualityIncidentImageLoss, 2) ?? 2));
        public void SeasonalIncident(World w, Player p) => p.Book(-(w.Tunables?.Long(Modding.TunableKeys.SeasonalIncidentCost) ?? 75000), Finance.MoneyReason.SeasonIncident);
    }

    // ------------------------------------------------------------------ operations formulas

    /// <summary>Passengers of a route flight entry (planes-and-flights.md §12.3, contracts-and-routes.md §3.7).</summary>
    public interface IRoutePassengerRule : IRule
    {
        void Calculate(World w, Player p, Plane plane, ref FlightEntry e);
    }

    /// <summary>Wear per departure (planes-and-flights.md §7.1); returns the new condition.</summary>
    public interface IPlaneWearRule : IRule
    {
        int ApplyWear(World w, Player p, Plane plane, int distanceM, int badKeroseneFactor);
    }

    public sealed class OriginalPlaneWear : IPlaneWearRule
    {
        public int ApplyWear(World w, Player p, Plane plane, int distanceM, int bkf)
        {
            int wear = (1 + distanceM * 10 / 40040174) * (2015 - plane.BuildYear) * bkf / 15;
            int pct = w.Tunables?.Int(Modding.TunableKeys.PlaneWearPercent, 100) ?? 100;   // tunable (Sim/Modding), 100 = original
            if (pct != 100) wear = (int)((long)wear * pct / 100);
            int z = (plane.Condition - wear) & 0xFF;
            return z > 200 ? 0 : z;
        }
    }

    /// <summary>Daily condition change by mechanic mode (planes-and-flights.md §7.2), before the target cap.</summary>
    public interface IMaintenanceRule : IRule
    {
        int DailyCondition(World w, Player p, Plane plane, int slot, int mode, TeakRand rand);
    }

    public sealed class OriginalMaintenance : IMaintenanceRule
    {
        public int DailyCondition(World w, Player p, Plane plane, int slot, int mode, TeakRand rand)
        {
            int z = plane.Condition;
            int date = w.Clock.Date;
            switch (mode)
            {
                case 0:
                    if (((slot + z + date) & 1) == 0) z = (z - 2) & 0xFF;
                    if (z > 200) z = 0;
                    break;
                case 1:
                    if (((slot + z + date) & 7) == 0) z = (z - 2) & 0xFF;
                    else z = Math.Min(z + 5, 100);
                    if (z > 200) z = 0;
                    break;
                case 2:
                    z = z < 60 ? Math.Min(z + rand.Rand(5) + 2, 100) : Math.Min(z + rand.Rand(8) + 2, 100);
                    break;
                case 3:
                    z = Math.Min(z + (z < 60 ? 18 : 15), 100);
                    break;
            }
            return z;
        }
    }

    /// <summary>Route demand (contracts-and-routes.md §3.2).</summary>
    public interface IRouteDemandRule : IRule
    {
        /// <summary>Potential daily passengers ("AnzPassagiere").</summary>
        int Potential(World w, Route r);
        /// <summary>Demand after the nightly update.</summary>
        int NextDemand(World w, Route r);
    }

    public sealed class OriginalRouteDemand : IRouteDemandRule
    {
        public int Potential(World w, Route r)
        {
            int date = w.Clock.Date;
            int dayF = date < 3 ? 1000 : date < 9 ? 900 : date < 29 ? 750 : 650;
            var a = w.Data.Cities.Find(r.From);
            var b = w.Data.Cities.Find(r.To);
            if (a == null || b == null) return 0;
            int pot = (int)(Math.Sqrt(a.Population * (double)b.Population) * r.Factor / dayF / 2 * 3 / 4);
            // Tunable (Sim/Modding): 100 % = original.
            int pct = w.Tunables?.Int(Modding.TunableKeys.RoutePotentialPercent, 100) ?? 100;
            return pct == 100 ? pot : (int)((long)pot * pct / 100);
        }

        public int NextDemand(World w, Route r)
        {
            int target = (int)(Potential(w, r) * (w.Tunables?.Get(Modding.TunableKeys.RouteDemandFactor, 4.27) ?? 4.27));   // tunable, original 4.27
            return w.Clock.Date == 0 ? target : (r.Demand * 6 + target) / 7;
        }
    }
}
