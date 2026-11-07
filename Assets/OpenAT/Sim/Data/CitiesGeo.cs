using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Data
{
    /// <summary>
    /// City geometry and random city choice (contracts-and-routes.md §1.2–1.4, planes-and-flights.md §12.1).
    /// Pure functions; the per-world cache lives in <see cref="CityDistanceCache"/>.
    /// </summary>
    public static class CityGeo
    {
        /// <summary>Equatorial circumference in metres used by the original.</summary>
        public const int EarthCircumference = 40040174;
        /// <summary>Distances are capped at this value (the longest pair of the shipped data sits exactly at it).</summary>
        public const int MaxDistance = 16440000;
        /// <summary>The original's approximation of π.</summary>
        public const double Pi = 3.14159;

        /// <summary>
        /// Great-circle distance in metres between two cities (contracts-and-routes.md §1.2), computed with
        /// the original's mix of single- and double-precision steps, truncated, capped at
        /// <see cref="MaxDistance"/>. Same city = 0.
        /// </summary>
        public static int Distance(City a, City b)
        {
            if (a == null || b == null) return 0;
            if (a.Id == b.Id) return 0;
            Vector(a, out float x1, out float y1, out float z1);
            Vector(b, out float x2, out float y2, out float z2);
            float dot = x1 * x2 + y1 * y2 + z1 * z2;
            double abs1 = Math.Sqrt(x1 * x1 + y1 * y1 + z1 * z1);
            double abs2 = Math.Sqrt(x2 * x2 + y2 * y2 + z2 * z2);
            double cos = dot / abs1 / abs2;
            if (cos > 1) cos = 1;
            if (cos < -1) cos = -1;
            float alpha = (float)(Math.Acos(cos) * 180.0 / Pi);
            int d = (int)(Math.Abs(alpha) * (double)EarthCircumference / 360.0);
            return Math.Min(d, MaxDistance);
        }

        static void Vector(City c, out float x, out float y, out float z)
        {
            x = (float)Math.Cos(c.GlobeX * Pi / 180.0);
            z = (float)Math.Sin(c.GlobeX * Pi / 180.0);
            y = (float)Math.Sin(c.GlobeY * Pi / 180.0);
            float s = (float)Math.Sqrt(1 - y * y);
            x *= s;
            z *= s;
        }

        /// <summary>
        /// Flight duration in whole hours (contracts-and-routes.md §1.3): ceil(distance/speed) airborne
        /// hours (integer steps of the original) plus one hour of handling, minimum 2.
        /// </summary>
        public static int FlightHours(int distanceM, int speedKmh)
        {
            if (speedKmh <= 0) return 99;
            int d = (distanceM / speedKmh + 999) / 1000 + 1;
            return d < 2 ? 2 : d;
        }

        /// <summary>
        /// Picks a city uniformly (contracts-and-routes.md §1.4): k = Rand(n)+1, then the k-th matching
        /// city counted from the highest table index down. <paramref name="areaCode"/> 0 = any region.
        /// Returns the city id.
        /// </summary>
        public static int RandomCity(Registry<City> cities, TeakRand rand, int areaCode = 0)
        {
            var all = cities.All;
            int n = 0;
            for (int c = all.Count - 1; c >= 0; c--)
                if (areaCode == 0 || all[c].AreaCode == areaCode) n++;
            if (n == 0) return all.Count > 0 ? all[0].Id : 0;
            n = rand.Rand(n) + 1;
            for (int c = all.Count - 1; c >= 0; c--)
                if (areaCode == 0 || all[c].AreaCode == areaCode)
                {
                    n--;
                    if (n == 0) return all[c].Id;
                }
            return all[0].Id;
        }
    }

    /// <summary>Per-world distance cache indexed by city table index (the original caches per pair).</summary>
    public sealed class CityDistanceCache
    {
        readonly Registry<City> _cities;
        readonly int[] _cache;
        readonly int _n;

        public CityDistanceCache(Registry<City> cities)
        {
            _cities = cities;
            _n = cities.Count;
            _cache = new int[_n * _n];
            for (int i = 0; i < _cache.Length; i++) _cache[i] = -1;
        }

        /// <summary>Distance in metres between two city ids (contracts-and-routes.md §1.2).</summary>
        public int Get(int cityIdA, int cityIdB)
        {
            int a = _cities.IndexOf(cityIdA), b = _cities.IndexOf(cityIdB);
            if (a < 0 || b < 0) return 0;
            if (a >= _n || b >= _n) return CityGeo.Distance(_cities[cityIdA], _cities[cityIdB]);
            int k = a + b * _n;
            int v = _cache[k];
            if (v < 0) _cache[k] = v = CityGeo.Distance(_cities.All[a], _cities.All[b]);
            return v;
        }
    }
}
