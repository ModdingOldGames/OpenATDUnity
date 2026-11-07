using OpenAT.Sim.Core;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Contracts
{
    /// <summary>
    /// Contract and freight generators (contracts-and-routes.md §2.3–2.4, §5.2). Each mirrors the
    /// original's draw order so that the same seeds produce the same offers. "Area types": 0 = both
    /// cities in one random region, 1 = home region with the home city, 2 = anywhere, 4 = like 1 but
    /// at most 10 000 km (early game).
    /// </summary>
    public static class ContractGenerator
    {
        public const int MaxEarlyDistance = 10000000;

        /// <summary>City pair picking shared by Last Minute, manager orders and the cargo office ("RandomCities").</summary>
        public static void RandomCities(World w, int areaType, int homeCity, TeakRand r, out int from, out int to)
        {
            var cities = w.Data.Cities;
            int homeArea = cities.Find(w.HomeCity)?.AreaCode ?? 1;
            int timeOut = 0;
            while (true)
            {
                from = to = 0;
                switch (areaType)
                {
                    case 0:
                        {
                            int area = 1 + r.Rand(4);
                            from = CityGeo.RandomCity(cities, r, area);
                            to = CityGeo.RandomCity(cities, r, area);
                        }
                        break;
                    case 1:
                    case 4:
                        from = CityGeo.RandomCity(cities, r, homeArea);
                        to = CityGeo.RandomCity(cities, r, homeArea);
                        if (r.Rand(2) == 0) from = homeCity; else to = homeCity;
                        break;
                    default:
                        from = CityGeo.RandomCity(cities, r);
                        to = CityGeo.RandomCity(cities, r);
                        break;
                }
                timeOut++;
                if (timeOut > 300 && from != to) break;
                if (from != to && !(areaType == 4 && w.Distance(from, to) > MaxEarlyDistance)) break;
                if (cities.Count < 2 || timeOut > 10000) break; // safety for modded tables with empty regions
            }
        }

        /// <summary>
        /// Size roll of the passenger template (§2.3 step 5): returns persons and scales the premium.
        /// <paramref name="tiny"/> = persons for s == 0 (2, or 1 at Last Minute).
        /// </summary>
        static int SizeRoll(World w, int s, int tiny, ref int premium, ref int penalty)
        {
            int date = w.Clock.Date;
            bool tut = w.Difficulty == Mission.Tutorial;
            if (s == 0) { premium *= 4; penalty = premium * 4; return tiny; }
            if (s < 15 || (date < 4 && s < 30) || (date < 8 && s < 20) || (tut && s < 70)) { premium = premium * 3 / 4; return 90; }
            if (s < 40 || tut) return 180;
            if (s < 70) { premium = premium * 5 / 4; return 280; }
            if (s < 90) { premium = premium * 6 / 4; return 340; }
            premium = premium * 7 / 4;
            return 430;
        }

        static int FreightSizeRoll(World w, int s, ref int premium, ref int penalty)
        {
            int date = w.Clock.Date;
            bool tut = w.Difficulty == Mission.Tutorial;
            if (s == 0) { premium *= 4; penalty = premium * 4; return 1; }
            if (s < 15 || (date < 4 && s < 30) || (date < 8 && s < 20) || (tut && s < 70)) { premium = premium * 3 / 4; return 15; }
            if (s < 40 || tut) return 30;
            if (s < 70) { premium = premium * 5 / 4; return 40; }
            if (s < 90) { premium = premium * 6 / 4; return 60; }
            premium = premium * 7 / 4;
            return 100;
        }

        static void AreaMultiplier(int areaType, ref int premium)
        {
            if (areaType == 1) premium = premium * 3 / 2;
            if (areaType == 2) premium = premium * 8 / 5;
        }

        /// <summary>Last Minute counter offer ("RefillForLastMinute", §2.4): f 130 then ×5/4, departure today.</summary>
        public static Contract LastMinute(World w, int areaType, TeakRand r)
        {
            int timeOut = 0;
            int today = w.Clock.Date;
            var c = new Contract();
            while (true)
            {
                RandomCities(w, areaType, w.HomeCity, r, out c.From, out c.To);
                c.Date = c.LastDate = today;
                c.InPlan = 0;
                c.Premium = w.ContractBasePrice(c.From, c.To, 130);
                c.Penalty = c.Premium / 2 * 100 / 100;
                c.Premium = c.Premium * 5 / 4;

                int t = r.Rand(100);
                if (t < 50) { }
                else if (t < 60) { c.Date++; c.LastDate++; }
                else if (t < 80) { c.Premium *= 2; c.Penalty = c.Premium * 4; }
                else if (t < 95) c.Premium /= 2;
                else { c.Premium *= 2; c.Penalty = 0; }

                c.Persons = SizeRoll(w, r.Rand(100), 1, ref c.Premium, ref c.Penalty);
                AreaMultiplier(areaType, ref c.Premium);

                if ((c.Persons > w.PlayerMaxPassengers || w.Distance(c.From, c.To) > w.PlayerMaxLength) && timeOut++ < 100) continue;
                return c;
            }
        }

        /// <summary>
        /// Travel agency / branch offer ("RefillForAusland", §2.4): f 120; two random cities, one replaced
        /// by <paramref name="cityId"/>; all draws from a private generator seeded by one draw of the list RNG.
        /// </summary>
        public static Contract Abroad(World w, int areaType, int cityId, TeakRand listRandom)
        {
            int timeOut = 0;
            int today = w.Clock.Date;
            var local = new TeakRand(listRandom.Rand());
            var cities = w.Data.Cities;
            var c = new Contract();
            while (true)
            {
                do
                {
                    c.From = CityGeo.RandomCity(cities, local);
                    c.To = CityGeo.RandomCity(cities, local);
                    if (local.Rand(2) == 0) c.From = cityId; else c.To = cityId;
                }
                while (cities.Count > 1 && (c.From == c.To || (areaType == 4 && w.Distance(c.From, c.To) > MaxEarlyDistance)));

                c.Persons = 180;
                c.Date = today + 1 + local.Rand(3);
                c.LastDate = c.Date;
                c.InPlan = 0;
                c.Premium = w.ContractBasePrice(c.From, c.To, 120);
                c.Penalty = c.Premium / 2 * 100 / 100;
                if (local.Rand(5) == 4) c.LastDate += local.Rand(5);

                int t = local.Rand(100);
                if (t < 50) { }
                else if (t < 65) { c.Date = today; c.LastDate = today + 4 + local.Rand(3); }
                else if (t < 80) { c.Premium *= 2; c.Penalty = c.Premium * 2; c.LastDate = c.Date = today + 1; }
                else if (t < 95) c.Premium /= 2;
                else { c.Premium *= 2; c.Penalty = 0; }

                c.Persons = SizeRoll(w, local.Rand(100), 2, ref c.Premium, ref c.Penalty);
                AreaMultiplier(areaType, ref c.Premium);
                if (c.Date != c.LastDate) { c.Date = today; c.Premium = c.Premium * 4 / 5; }

                if ((c.Persons > w.PlayerMaxPassengers || w.Distance(c.From, c.To) > w.PlayerMaxLength) && timeOut++ < 100) continue;
                return c;
            }
        }

        /// <summary>
        /// Start-of-game contract ("RefillForBegin", §2.4): from home, ≤ 5000 km, 90 persons, f 100.
        /// Retries against the largest (80 tries) and smallest (60 tries) human plane; infeasible
        /// contracts become penalty-free with doubled premium and a 6-day window.
        /// </summary>
        public static Contract Begin(World w, int areaType, TeakRand r)
        {
            int timeOut = 0;
            int today = w.Clock.Date;
            var c = new Contract();
            while (true)
            {
                do
                {
                    RandomCities(w, areaType, w.HomeCity, r, out c.From, out c.To);
                    c.From = w.HomeCity;
                }
                while (w.Data.Cities.Count > 1 && (c.From == c.To || w.Distance(c.From, c.To) > 5000000));

                c.Date = c.LastDate = today;
                c.Persons = 90;
                c.InPlan = 0;
                c.Premium = w.ContractBasePrice(c.From, c.To, 100);
                c.Penalty = c.Premium / 2 * 100 / 100;

                int t = r.Rand(100);
                if (t < 50) { }
                else if (t < 60) c.LastDate = today + 6;
                else if (t >= 70 && t < 95) { c.Premium *= 2; c.Penalty = c.Premium * 2; c.LastDate = today + 1; }
                else if (t >= 95) { c.Premium *= 2; c.Penalty = 0; }

                AreaMultiplier(areaType, ref c.Premium);
                if (c.Date != c.LastDate) c.Premium = c.Premium * 4 / 5;

                timeOut++;
                int dist = w.Distance(c.From, c.To);
                if ((c.Persons > w.PlayerMaxPassengers || dist > w.PlayerMaxLength) && timeOut < 80) continue;
                if ((c.Persons > w.PlayerMinPassengers || dist > w.PlayerMinLength) && timeOut < 60) continue;
                if (c.Persons > w.PlayerMaxPassengers || dist > w.PlayerMaxLength)
                {
                    c.Penalty = 0;
                    c.Premium *= 2;
                    c.Date = today;
                    c.LastDate = c.Date + 5;
                }
                return c;
            }
        }

        /// <summary>Airport manager order of add-on mission 9 ("RefillForUhrig", §2.4): f 115, penalty = premium.</summary>
        public static Contract ManagerOrder(World w, int areaType, TeakRand r)
        {
            int timeOut = 0;
            int today = w.Clock.Date;
            var c = new Contract();
            while (true)
            {
                RandomCities(w, areaType, w.HomeCity, r, out c.From, out c.To);
                c.Persons = 180;
                c.Date = today + 1 + r.Rand(3);
                c.LastDate = c.Date;
                c.InPlan = 0;
                c.Premium = w.ContractBasePrice(c.From, c.To, 115);
                c.Penalty = c.Premium / 2 * 100 / 100 * 2;
                if (r.Rand(5) == 4) c.LastDate += r.Rand(5);

                int t = r.Rand(100);
                if (t < 50) { }
                else if (t < 60) { c.Date = today; c.LastDate = today + 4 + r.Rand(3); }
                else if (t < 70) { }
                else if (t < 80) { c.Premium *= 2; c.Penalty = c.Premium * 2; c.LastDate = c.Date = today + 1; }
                else if (t < 95) c.Premium /= 2;
                else { c.Premium *= 2; c.Penalty = 0; }

                c.Persons = SizeRoll(w, r.Rand(100), 2, ref c.Premium, ref c.Penalty);
                AreaMultiplier(areaType, ref c.Premium);
                if (c.Date != c.LastDate) { c.Date = today; c.Premium = c.Premium * 4 / 5; }

                if ((c.Persons > w.PlayerMaxPassengers || w.Distance(c.From, c.To) > w.PlayerMaxLength) && timeOut++ < 100) continue;
                return c;
            }
        }

        /// <summary>Common freight template steps after the city choice (§5.2).</summary>
        static void FreightBody(World w, FreightContract f, int areaType, TeakRand r)
        {
            int today = w.Clock.Date;
            f.Tons = 30;
            f.Date = today + 1 + r.Rand(3);
            f.LastDate = f.Date;
            f.InPlan = 0;
            f.Premium = w.ContractBasePrice(f.From, f.To, 105);
            f.Penalty = f.Premium / 2 * 100 / 100;
            if (r.Rand(5) == 4) f.LastDate += r.Rand(5);

            int t = r.Rand(100);
            if (t < 50) { }
            else if (t < 60) { f.Date = today; f.LastDate = today + 4 + r.Rand(3); }
            else if (t < 70) { }
            else if (t < 80) { f.Premium *= 2; f.Penalty = f.Premium * 2; f.LastDate = f.Date = today + 1; }
            else if (t < 95) f.Premium /= 2;
            else { f.Premium *= 2; f.Penalty = 0; }

            f.Tons = FreightSizeRoll(w, r.Rand(100), ref f.Premium, ref f.Penalty);
            AreaMultiplier(areaType, ref f.Premium);
            if (f.Date != f.LastDate) { f.Date = today; f.Premium = f.Premium * 4 / 5; }

            if (f.To == w.CrisisCityId && w.Difficulty == 13) { f.Premium = 0; f.Penalty /= 2; }

            if (f.Tons > 80) f.Premium *= 5;
            else if (f.Tons > 60) f.Premium *= 4;
            else if (f.Tons > 30) f.Premium *= 3;
        }

        /// <summary>Cargo office offer ("CFracht::Refill", §5.2), with the add-on 3 relief destination.</summary>
        public static FreightContract FreightOffice(World w, int areaType, TeakRand r)
        {
            int timeOut = 0;
            var f = new FreightContract { InPlan = 0, Okay = 1 };
            var cities = w.Data.Cities;
            while (true)
            {
                if (w.Difficulty == 13 && r.Rand(100) > 25 && w.CrisisCityId != 0)
                {
                    f.To = w.CrisisCityId;
                    do f.From = CityGeo.RandomCity(cities, r);
                    while (cities.Count > 1 && f.From == w.CrisisCityId);
                }
                else RandomCities(w, areaType, w.HomeCity, r, out f.From, out f.To);

                FreightBody(w, f, areaType, r);
                if (w.Distance(f.From, f.To) > w.PlayerMaxLength && timeOut++ < 100) continue;
                f.TonsOpen = f.TonsLeft = f.Tons;
                return f;
            }
        }

        /// <summary>Branch freight offer ("CFracht::RefillForAusland", §5.2): draws directly from the list RNG.</summary>
        public static FreightContract FreightAbroad(World w, int areaType, int cityId, TeakRand r)
        {
            int timeOut = 0;
            var f = new FreightContract { InPlan = 0, Okay = 1 };
            var cities = w.Data.Cities;
            while (true)
            {
                do
                {
                    f.From = CityGeo.RandomCity(cities, r);
                    f.To = CityGeo.RandomCity(cities, r);
                    if (r.Rand(2) == 0) f.From = cityId; else f.To = cityId;
                }
                while (cities.Count > 1 && (f.From == f.To || (areaType == 4 && w.Distance(f.From, f.To) > MaxEarlyDistance)));

                FreightBody(w, f, areaType, r);
                if (w.Distance(f.From, f.To) > w.PlayerMaxLength && timeOut++ < 100) continue;
                f.TonsOpen = f.TonsLeft = f.Tons;
                return f;
            }
        }
    }
}
