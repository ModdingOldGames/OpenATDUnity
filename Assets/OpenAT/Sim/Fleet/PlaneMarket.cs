using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Buying and selling planes (planes-and-flights.md §4): availability, unused names, broker
    /// purchases, designer purchases, the museum's three daily used-plane offers, sale value.
    /// </summary>
    public static class PlaneMarket
    {
        /// <summary>Hard-coded fallback name when every name is in use (our own wording).</summary>
        public const string FallbackName = "Unnamed";

        /// <summary>Availability rule (§2.2): FirstMission &lt; mission, or (same mission or free game) and FirstDay ≤ Date.</summary>
        public static bool IsAvailable(World w, PlaneType t)
        {
            int d = w.Difficulty;
            return t.FirstMission < d || ((t.FirstMission == d || d == Mission.FreeGame) && t.FirstDay <= w.Clock.Date);
        }

        /// <summary>Types in album index order (index 0 upwards; the first csv row is slot 29).</summary>
        public static List<PlaneType> TypesByIndex(World w)
        {
            var l = new List<PlaneType>(w.Data.PlaneTypes.All);
            l.Sort((a, b) => a.Index.CompareTo(b.Index));
            return l;
        }

        /// <summary>Broker catalogue: available types in album order.</summary>
        public static List<PlaneType> BrokerList(World w)
        {
            var l = new List<PlaneType>();
            foreach (var t in TypesByIndex(w)) if (IsAvailable(w, t)) l.Add(t);
            return l;
        }

        /// <summary>"GetRandomExistingType": k = Rand(n)+1, k-th available type counted from index 0.</summary>
        public static PlaneType RandomExistingType(World w, TeakRand r)
        {
            var l = BrokerList(w);
            if (l.Count == 0) return null;
            return l[r.Rand(l.Count)];
        }

        public static bool IsNameInUse(World w, string name)
        {
            foreach (var p in w.Players)
                foreach (var pl in p.Planes.Items)
                    if (string.Equals(pl.Name, name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// "GetUnused" (planes-and-flights.md §4.8, data-tables.md §1.8): random tier-1 picks (entry 0
        /// never), sequential tier 1, random tier 2, sequential tier 2, fallback. Unique across all fleets.
        /// </summary>
        public static string UnusedName(World w, TeakRand r)
        {
            var n1 = w.Data.PlaneNamesPreferred;
            var n2 = w.Data.PlaneNamesFallback;
            if (n1.Count > 1)
            {
                for (int c = 0; c < n1.Count; c++)
                {
                    var s = n1[1 + r.Rand(n1.Count - 1)];
                    if (!IsNameInUse(w, s)) return s;
                }
                for (int c = 1; c < n1.Count; c++) if (!IsNameInUse(w, n1[c])) return n1[c];
            }
            if (n2.Count > 1)
            {
                for (int c = 0; c < n2.Count; c++)
                {
                    var s = n2[1 + r.Rand(n2.Count - 1)];
                    if (!IsNameInUse(w, s)) return s;
                }
                for (int c = 1; c < n2.Count; c++) if (!IsNameInUse(w, n2[c])) return n2[c];
            }
            return FallbackName;
        }

        /// <summary>Build year of new planes: 2002 + Date/365.</summary>
        public static int NewBuildYear(World w) => 2002 + w.Clock.Date / 365;

        /// <summary>Fleet attendant ratio Σ MaxAttendants / Σ min(crew, type attendants), 1 if no crew (§4.1).</summary>
        public static double AverageAttendantRatio(World w, Player p)
        {
            long anz = 0, max = 0;
            var crew = w.Rules.Get<ICrewMapping>();
            foreach (var pl in p.Planes.Items) { anz += Math.Min(crew.Crew(w, p, pl).Attendants, pl.Attendants); max += pl.MaxAttendants; }
            return anz != 0 ? (double)max / anz : 1;
        }

        /// <summary>
        /// "BuyPlane(type)" (§4.1): condition 100, build year 2002+Date/365, unused name from
        /// <paramref name="names"/>, delivered at once at home; attendants scaled by the fleet average;
        /// price booked (reason 2010) unless <paramref name="free"/>; album sorted by name.
        /// </summary>
        public static Plane BuyNew(World w, Player p, PlaneType t, TeakRand names, bool free = false)
        {
            var pl = Plane.FromType(t, UnusedName(w, names), 100, NewBuildYear(w), w.HomeCity);
            SetGlobe(w, pl);
            pl.MaxAttendants = (int)(t.Attendants * AverageAttendantRatio(w, p));
            if (pl.MaxAttendants < 0) pl.MaxAttendants = 0;
            if (pl.MaxAttendants > pl.Attendants * 2) pl.MaxAttendants = pl.Attendants * 2;
            p.Planes.Add(pl);
            if (!free) p.Book(-t.Price, MoneyReason.PlaneBought, pl.Name);
            SortFleet(p);
            return pl;
        }

        /// <summary>"BuyPlane(design)" (§5.5).</summary>
        public static Plane BuyDesigned(World w, Player p, PlaneDesign d, TeakRand names)
        {
            var pl = Plane.FromDesign(d, w.DesignCatalog, UnusedName(w, names), NewBuildYear(w), w.HomeCity);
            SetGlobe(w, pl);
            pl.MaxAttendants = (int)(pl.Attendants * AverageAttendantRatio(w, p));
            if (pl.MaxAttendants < 0) pl.MaxAttendants = 0;
            if (pl.MaxAttendants > pl.Attendants * 2) pl.MaxAttendants = pl.Attendants * 2;
            p.Planes.Add(pl);
            p.Book(-pl.Price, MoneyReason.PlaneBought, pl.Name);
            SortFleet(p);
            return pl;
        }

        public static void SortFleet(Player p) => p.Planes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        static void SetGlobe(World w, Plane pl)
        {
            var c = w.Data.Cities.Find(pl.Location);
            if (c != null) { pl.GlobeX = c.GlobeX; pl.GlobeY = c.GlobeY; }
        }

        // ------------------------------------------------------------------ museum

        /// <summary>
        /// "CreateRandomUsedPlane" (§4.3, sim-core.md §6.9): generator Date + slot. The constructor
        /// arguments are drawn right to left (MSVC order, open question 2): throw-away condition, type,
        /// name. Build year 1990 − Rand(1990 − first year) for older types, else 1996 − Rand(1996 − first
        /// year); condition (year − 1950) + 25 + Rand(40) − 20 as a byte, &lt; 20 or &gt; 200 → 20, cap 100.
        /// </summary>
        public static Plane CreateUsedPlane(World w, int slot)
        {
            var r = new TeakRand(w.Clock.Date + slot);
            int throwAway = r.Rand(80) + 11;
            var t = RandomExistingType(w, r);
            if (t == null) return null;
            string name = UnusedName(w, r);
            var pl = Plane.FromType(t, name, throwAway, 1900, w.HomeCity);
            SetGlobe(w, pl);
            pl.BuildYear = pl.FirstYear < 1990 ? 1990 - r.Rand(1990 - pl.FirstYear) : 1996 - CRem(r, 1996 - pl.FirstYear);
            int z = ((pl.BuildYear - 1950) + 25 + r.Rand(40) - 20) & 0xFF;
            if (z < 20 || z > 200) z = 20;
            if (z > 100) z = 100;
            pl.Condition = z;
            pl.TargetCondition = z;
            return pl;
        }

        /// <summary>Rand(n) with C remainder semantics for negative n (first year 1998 → n = −2).</summary>
        static int CRem(TeakRand r, int n) => n == 0 ? 0 : r.Rand() % n;

        /// <summary>Three fresh offers (sim-core.md §2.6 step 20); none in the tycoon mission on days 40–50.</summary>
        public static void CreateUsedPlanes(World w)
        {
            for (int c = 0; c < w.UsedPlanes.Length; c++) w.UsedPlanes[c] = CreateUsedPlane(w, c);
            if (w.Difficulty == Mission.AtfsLast && w.Clock.Date >= 40 && w.Clock.Date <= 50)
                for (int c = 0; c < w.UsedPlanes.Length; c++) w.UsedPlanes[c] = null;
        }

        /// <summary>
        /// "UpdateUsedPlanes" when entering the museum: regenerate up to min(3, ticks/20) empty slots
        /// with their morning seeds (the same planes reappear).
        /// </summary>
        public static void RefillUsedPlanes(World w)
        {
            int anz = Math.Min(w.UsedPlanes.Length, w.MuseumRefillTicks / 20);
            for (int c = 0; c < w.UsedPlanes.Length && anz > 0; c++)
                if (w.UsedPlanes[c] == null)
                {
                    w.UsedPlanes[c] = CreateUsedPlane(w, c);
                    anz--;
                    w.MuseumRefillTicks = 0;
                }
            if (w.Difficulty == Mission.AtfsLast && w.Clock.Date >= 40 && w.Clock.Date <= 50)
                for (int c = 0; c < w.UsedPlanes.Length; c++) w.UsedPlanes[c] = null;
        }

        /// <summary>Sale price at the museum: market value × 9 / 10 (§4.4).</summary>
        public static int SalePrice(Plane pl) => pl.MarketValue * 9 / 10;
    }
}
