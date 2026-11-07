using OpenAT.Sim.Finance;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim
{
    /// <summary>Fuel quality bought at Petrol Air (economy.md §12.2).</summary>
    public enum KeroseneKind { Premium = 0, Normal = 1, Cheap = 2 }

    /// <summary>Result of taking fuel from the tank for a flight (economy.md §12.3).</summary>
    public struct TankDraw
    {
        /// <summary>Barrels taken from the tank.</summary>
        public int FromTank;
        /// <summary>Imputed cost of the tank part (barrels × average tank price), already added to the balance sheet and stat 35.</summary>
        public long ImputedCost;
    }

    /// <summary>Kerosene: fuel kind, storage tank, prices for flights (economy.md §12).</summary>
    public sealed partial class Player
    {
        /// <summary>Tank capacity units added and price per unit of the four tank models (×1/2/3/5/10 multiplier in the dialog).</summary>
        public static readonly int[] TankCapacity = { 10, 30, 50, 100 };
        public static readonly int[] TankPricePerUnit = { 10000, 20000, 40000, 60000 };

        public KeroseneKind KeroseneKind = KeroseneKind.Normal;
        /// <summary>Tank capacity and contents in barrels.</summary>
        public int Tank = 100, TankContents;
        /// <summary>Planes refuel from the tank.</summary>
        public bool TankOpen;
        /// <summary>Average purchase price per barrel of the tank contents.</summary>
        public double TankPrice;
        /// <summary>Barrels of cheap fuel in the tank.</summary>
        public int BadKerosene;

        /// <summary>Price per barrel of a fuel kind at today's market price: 2× / 1× / ½×.</summary>
        public static int PricePerBarrel(int market, KeroseneKind kind) =>
            kind == KeroseneKind.Premium ? market * 2 : kind == KeroseneKind.Cheap ? market / 2 : market;

        /// <summary>Today's pump price per barrel of the chosen kind.</summary>
        public int FuelPricePerBarrel() => PricePerBarrel(World?.Kerosene ?? 500, KeroseneKind);

        // Fuel per flight, its three cost variants and the cheap-fuel wear factor are the operations
        // rule IFlightFuelCost (Fleet/OperationsRules.cs, World.FuelCost), which reads the tank through IFuelAccount.

        /// <summary>
        /// The fuel grade picked in the Petrol Air dialog (economy.md §12.2): it takes effect at once (pump
        /// fuel price and cheap-fuel wear), whether or not any fuel is bought afterwards.
        /// </summary>
        public void ChooseKerosene(KeroseneKind kind) => KeroseneKind = kind;

        /// <summary>
        /// The kerosene advisor on entering Petrol Air (staff-and-advisors.md §9.2): price &lt; 400, the tank
        /// test <c>contents·2 &lt; capacity·4</c> (false only without a tank) and cash &gt; 20 000 → "buy now"
        /// (<c>Advi 3000</c>); else price &gt; 600 → "don't buy" (3001); 0 = nothing (or no advisor).
        /// </summary>
        public int KeroseneEntryRemark()
        {
            if (!HasAdvisor(AdvisorType.Kerosene)) return 0;
            int k = World?.Kerosene ?? 500;
            if (k < 400 && (long)TankContents * 2 < (long)Tank * 4 && Money > 20000) return 3000;
            return k > 600 ? 3001 : 0;
        }

        /// <summary>
        /// Takes the tank part of a flight's fuel when the flight is booked (economy.md §12.3):
        /// contents −t, cheap fuel −t (floor 0), imputed t·TankPrice to balance-sheet kerosene and
        /// stat 35. Kerosene advisor warns when the tank runs dry (<c>Advi 3030</c>).
        /// </summary>
        public TankDraw DrawTankFuel(int barrels, string planeName = null)
        {
            var r = new TankDraw();
            if (!TankOpen) return r;
            int t = System.Math.Min(TankContents, barrels);
            TankContents -= t;
            BadKerosene -= t;
            if (BadKerosene < 0) BadKerosene = 0;
            if (TankContents == 0 && t > 0 && HasAdvisor(AdvisorType.Kerosene))
                World?.Events.Publish(new AdvisorMessage(this, (int)AdvisorType.Kerosene, 3030, planeName));
            r.FromTank = t;
            r.ImputedCost = (long)(t * TankPrice);
            Today.Kerosene += (int)(t * TankPrice);
            Statistics.Add(StatId.ExpenseKerosene, -(long)(t * TankPrice));
            return r;
        }

        /// <summary>Cost of buying <paramref name="barrels"/> into the tank: price·qty minus 5 % (≥ 10 000) or 10 % (≥ 50 000).</summary>
        public static long TankPurchaseCost(int pricePerBarrel, long barrels)
        {
            long cost = pricePerBarrel * barrels;
            long rebate = barrels >= 50000 ? cost / 10 : barrels >= 10000 ? cost / 20 : 0;
            return cost - rebate;
        }

        /// <summary>
        /// The dialog clamp (economy.md §12.2): at most the free tank space, and at most what cash
        /// allows down to −1 000 000 (then the rebate is recomputed).
        /// </summary>
        public long MaxKeroseneBarrels(KeroseneKind kind)
        {
            int price = PricePerBarrel(World?.Kerosene ?? 500, kind);
            long space = System.Math.Max(0, Tank - TankContents);
            long n = space;
            if (price > 0 && TankPurchaseCost(price, n) > Money - DebtLimit)
                n = System.Math.Max(0, (Money - DebtLimit) / price);
            return n;
        }

        /// <summary>
        /// Buys fuel into the tank and sets the kind (economy.md §12.2): contents, cheap-fuel count,
        /// average price, booking 2090, bodyguard rebate. Caller validated.
        /// </summary>
        public void BuyKerosene(KeroseneKind kind, int barrels)
        {
            KeroseneKind = kind;
            if (barrels <= 0) return;
            long cost = TankPurchaseCost(PricePerBarrel(World?.Kerosene ?? 500, kind), barrels);
            int old = TankContents;
            TankContents += barrels;
            if (kind == KeroseneKind.Cheap) BadKerosene += barrels;
            if (kind == KeroseneKind.Premium) BadKerosene = System.Math.Max(0, BadKerosene - barrels);
            TankPrice = (old * TankPrice + cost) / (old + barrels);
            Book(-cost, MoneyReason.KeroseneBought);
            BodyguardRebate(cost);
        }

        /// <summary>Buys storage tanks (economy.md §12.2): capacity += units·mult, price·mult (2091) + rebate.</summary>
        public void BuyTank(int model, int multiplier)
        {
            long price = (long)TankPricePerUnit[model] * multiplier;
            Tank += TankCapacity[model] * multiplier;
            Book(-price, MoneyReason.TankBought, TankCapacity[model] * 1000 + " x" + multiplier);
            BodyguardRebate(price);
        }
    }
}
