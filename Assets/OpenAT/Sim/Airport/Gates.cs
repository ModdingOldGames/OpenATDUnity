namespace OpenAT.Sim.Airport
{
    /// <summary>A gate record of an airline ("CGate"): rented gate number, or free when <see cref="Rent"/> is −1.</summary>
    public struct GateRecord
    {
        public int Number;
        /// <summary>Monthly rent; −1 = record unused.</summary>
        public int Rent;
        public bool Used => Rent != -1;
    }

    /// <summary>
    /// An airline's gates at the home airport ("CGates", contracts-and-routes.md §7): 30 records and the
    /// 7-day × 24-hour occupancy table used by gate planning (planes-and-flights.md §10.8).
    /// </summary>
    public sealed class PlayerGates
    {
        public const int Records = 30;
        public const int Hours = 24 * 7;

        public readonly GateRecord[] Gates = new GateRecord[Records];
        public int NumRented;
        /// <summary>Occupancy per hour from today 00:00 (a byte per hour in the original).</summary>
        public readonly int[] Occupancy = new int[Hours];

        public PlayerGates() { Clear(); }

        public void Clear()
        {
            for (int i = 0; i < Records; i++) Gates[i] = new GateRecord { Number = 0, Rent = -1 };
            NumRented = 0;
            for (int i = 0; i < Hours; i++) Occupancy[i] = 0;
        }

        /// <summary>Rents gate <paramref name="number"/> into the first free record ("RentGate").</summary>
        public bool Rent(int number, int monthlyRent)
        {
            for (int i = 0; i < Records; i++)
                if (Gates[i].Rent == -1)
                {
                    NumRented++;
                    Gates[i].Number = number;
                    Gates[i].Rent = monthlyRent;
                    return true;
                }
            return false;
        }

        public bool HasGate(int number)
        {
            for (int i = 0; i < Records; i++) if (Gates[i].Used && Gates[i].Number == number) return true;
            return false;
        }

        /// <summary>Sum of the daily rents (Miete/30 per gate, integer per gate).</summary>
        public long DailyRent
        {
            get { long s = 0; for (int i = 0; i < Records; i++) if (Gates[i].Used) s += Gates[i].Rent / 30; return s; }
        }
    }

    /// <summary>An airline's branch office record per city ("CRentCity", contracts-and-routes.md §8).</summary>
    public struct RentCity
    {
        /// <summary>0 = no branch, 1–3 = rank.</summary>
        public int Rank;
        public int Rent;
        /// <summary>Saved but never read by the passenger formula (§8).</summary>
        public int Image;
        public bool Owned => Rank != 0;
    }
}
