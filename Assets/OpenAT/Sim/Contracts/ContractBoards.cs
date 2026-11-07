using System;

namespace OpenAT.Sim.Contracts
{
    /// <summary>
    /// Filling and trickle-refilling of the shared offer pools (contracts-and-routes.md §2.4–2.5, §5.2).
    /// Full fills run at game start and every midnight with fresh seeds; trickle refills run when a
    /// player enters or leaves a counter (or opens a branch list) and regenerate up to
    /// min(6, ticks) empty slots (one tick per 5 game minutes).
    /// </summary>
    public static class ContractBoards
    {
        /// <summary>Area type of slot <paramref name="c"/> of the travel agency, branch and freight trickle lists.</summary>
        public static int EarlyAreaType(World w, int c)
        {
            int date = w.Clock.Date;
            if (date < 5 && c < 5) return 4;
            if (date < 10 && c < 3) return 4;
            return c / 2;
        }

        static bool Atfs10Contracts(World w) => w.Difficulty == Mission.AtfsLast && w.Clock.Date >= 20 && w.Clock.Date <= 30;
        static bool Atfs10Freight(World w) => w.Difficulty == Mission.AtfsLast && w.Clock.Date >= 25 && w.Clock.Date <= 35;

        // ------------------------------------------------------------------ Last Minute

        public static void FillLastMinute(World w)
        {
            w.CalcPlayerMaximums();
            var b = w.LastMinute;
            for (int c = 0; c < ContractBoard.Slots; c++) b.Offers[c] = ContractGenerator.LastMinute(w, c / 2, b.Random);
            if (Atfs10Contracts(w)) foreach (var o in b.Offers) o.Premium = 0;
        }

        public static void RefillLastMinute(World w, int minimum = 0)
        {
            var b = w.LastMinute;
            RefillContracts(w, b, minimum, c => ContractGenerator.LastMinute(w, c / 2, b.Random));
            if (Atfs10Contracts(w)) foreach (var o in b.Offers) o.Premium = 0;
        }

        // ------------------------------------------------------------------ travel agency

        public static void FillTravelAgency(World w)
        {
            w.CalcPlayerMaximums();
            var b = w.TravelAgency;
            for (int c = 0; c < ContractBoard.Slots; c++) b.Offers[c] = ContractGenerator.Abroad(w, EarlyAreaType(w, c), w.HomeCity, b.Random);
            if (Atfs10Contracts(w)) foreach (var o in b.Offers) o.Premium = 0;
        }

        public static void RefillTravelAgency(World w, int minimum = 0)
        {
            var b = w.TravelAgency;
            RefillContracts(w, b, minimum, c => ContractGenerator.Abroad(w, EarlyAreaType(w, c), w.HomeCity, b.Random));
            if (Atfs10Contracts(w)) foreach (var o in b.Offers) o.Premium = 0;
        }

        // ------------------------------------------------------------------ branch lists

        public static void FillForeign(World w, int cityIndex)
        {
            w.CalcPlayerMaximums();
            var b = w.ForeignContracts[cityIndex];
            int city = w.CityIdAt(cityIndex);
            for (int c = 0; c < ContractBoard.Slots; c++) b.Offers[c] = ContractGenerator.Abroad(w, EarlyAreaType(w, c), city, b.Random);
        }

        public static void RefillForeign(World w, int cityIndex, int minimum = 0)
        {
            var b = w.ForeignContracts[cityIndex];
            int city = w.CityIdAt(cityIndex);
            RefillContracts(w, b, minimum, c => ContractGenerator.Abroad(w, EarlyAreaType(w, c), city, b.Random));
        }

        /// <summary>
        /// The original's trickle refill (§2.5): regenerate up to min(6, ticks) empty slots (Premium == 0)
        /// from slot 0; if budget remains, count filled slots and fill more until <paramref name="minimum"/>
        /// are filled; reset the counter.
        /// </summary>
        static void RefillContracts(World w, ContractBoard b, int minimum, Func<int, Contract> gen)
        {
            int anz = Math.Min(ContractBoard.Slots, b.RefillTicks);
            w.CalcPlayerMaximums();
            for (int c = 0; c < ContractBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium == 0) { b.Offers[c] = gen(c); anz--; }
            for (int c = 0; c < ContractBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium != 0) minimum--;
            for (int c = 0; c < ContractBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium == 0 && minimum > 0) { b.Offers[c] = gen(c); minimum--; }
            b.RefillTicks = 0;
        }

        // ------------------------------------------------------------------ freight

        public static void FillFreightOffice(World w)
        {
            w.CalcPlayerMaximums();
            var b = w.FreightOffice;
            for (int c = 0; c < FreightBoard.Slots; c++) b.Offers[c] = ContractGenerator.FreightOffice(w, c / 2, b.Random);
            if (Atfs10Freight(w)) foreach (var o in b.Offers) o.Premium = -1;
        }

        public static void RefillFreightOffice(World w, int minimum = 0)
        {
            var b = w.FreightOffice;
            int anz = Math.Min(FreightBoard.Slots, b.RefillTicks);
            w.CalcPlayerMaximums();
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium < 0) { b.Offers[c] = ContractGenerator.FreightOffice(w, EarlyAreaType(w, c), b.Random); anz--; }
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium != -1) minimum--;
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium < 0 && minimum > 0) { b.Offers[c] = ContractGenerator.FreightOffice(w, EarlyAreaType(w, c), b.Random); minimum--; }
            b.RefillTicks = 0;
            if (Atfs10Freight(w)) foreach (var o in b.Offers) o.Premium = -1;
        }

        public static void FillForeignFreight(World w, int cityIndex)
        {
            w.CalcPlayerMaximums();
            var b = w.ForeignFreight[cityIndex];
            int city = w.CityIdAt(cityIndex);
            for (int c = 0; c < FreightBoard.Slots; c++) b.Offers[c] = ContractGenerator.FreightAbroad(w, EarlyAreaType(w, c), city, b.Random);
        }

        /// <summary>Branch freight refill: empty test Premium ≤ 0 (QUIRK: a free relief cargo counts as empty).</summary>
        public static void RefillForeignFreight(World w, int cityIndex, int minimum = 0)
        {
            var b = w.ForeignFreight[cityIndex];
            int city = w.CityIdAt(cityIndex);
            int anz = Math.Min(FreightBoard.Slots, b.RefillTicks);
            w.CalcPlayerMaximums();
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium <= 0) { b.Offers[c] = ContractGenerator.FreightAbroad(w, EarlyAreaType(w, c), city, b.Random); anz--; }
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium != 0) minimum--;
            for (int c = 0; c < FreightBoard.Slots && anz > 0; c++)
                if (b.Offers[c].Premium <= 0 && minimum > 0) { b.Offers[c] = ContractGenerator.FreightAbroad(w, EarlyAreaType(w, c), city, b.Random); minimum--; }
            b.RefillTicks = 0;
        }

        // ------------------------------------------------------------------ nightly / setup

        /// <summary>
        /// Full refill of every pool with the midnight seeds (sim-core.md §2.6 step 14): freight Date,
        /// Last Minute Date+1, travel agency Date+2, branch lists Date+cityIndex+3; counters to 6.
        /// <paramref name="mainSeedOverride"/> replaces the three main seeds (the original's single-player
        /// wall-clock seeding at game start: three consecutive clock reads, i.e. one value for all three).
        /// </summary>
        public static void FillAll(World w, int? mainSeedOverride = null, bool setupOrder = false)
        {
            int d = w.Clock.Date;
            w.FreightOffice.Random.SetSeed(mainSeedOverride ?? d);
            w.LastMinute.Random.SetSeed(mainSeedOverride ?? d + 1);
            w.TravelAgency.Random.SetSeed(mainSeedOverride ?? d + 2);
            if (setupOrder)
            {
                // Game start: freight, Last Minute, travel agency (sim-core.md §3.6).
                FillFreightOffice(w);
                FillLastMinute(w);
                FillTravelAgency(w);
            }
            else
            {
                // Midnight: Last Minute, travel agency, freight.
                FillLastMinute(w);
                FillTravelAgency(w);
                FillFreightOffice(w);
            }
            w.TravelAgency.RefillTicks = w.LastMinute.RefillTicks = w.FreightOffice.RefillTicks = 6;
            w.MuseumRefillTicks = 6;
            for (int c = 0; c < w.ForeignContracts.Length; c++)
            {
                w.ForeignContracts[c].Random.SetSeed(d + c + 3);
                FillForeign(w, c);
                w.ForeignContracts[c].RefillTicks = 6;
                w.ForeignFreight[c].Random.SetSeed(d + c + 3);
                FillForeignFreight(w, c);
                w.ForeignFreight[c].RefillTicks = 6;
            }
        }

        /// <summary>Every 5 game minutes: +1 on every refill counter (sim-core.md §2.4 minute step 2).</summary>
        public static void Tick(World w)
        {
            w.TravelAgency.RefillTicks++;
            w.LastMinute.RefillTicks++;
            w.FreightOffice.RefillTicks++;
            w.MuseumRefillTicks++;
            foreach (var b in w.ForeignContracts) b.RefillTicks++;
            foreach (var b in w.ForeignFreight) b.RefillTicks++;
        }
    }
}
