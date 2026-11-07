using OpenAT.Sim.Core;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Finance
{
    /// <summary>Campaign types of the advertising agency (economy.md §10).</summary>
    public enum AdType { Company = 0, Route = 1, Everything = 2 }

    /// <summary>The joke "worst ad" (tier 0): every human airline gets a letter (<c>Lett 9900/9910/9920</c>).</summary>
    public sealed class WorstAdLetter : ISimEvent
    {
        public readonly Player Recipient, Advertiser;
        public readonly AdType Type;
        public readonly int RouteId;
        public WorstAdLetter(Player to, Player from, AdType t, int routeId) { Recipient = to; Advertiser = from; Type = t; RouteId = routeId; }
        public int LetterBase => 9900 + 10 * (int)Type;
    }

    /// <summary>
    /// Advertising prices and effects (economy.md §10, gWerbePrice). Effects are instant and permanent.
    /// </summary>
    public static class Advertising
    {
        /// <summary>Price per type × tier (3 × 6).</summary>
        public static readonly long[] Price =
        {
            1000, 10000, 50000, 100000, 250000, 2500000,
            2000, 8000, 22000, 56000, 120000, 1400000,
            5000, 93000, 290000, 870000, 4925000, 60000000,
        };

        public static long PriceOf(AdType t, int tier) => Price[(int)t * 6 + tier];

        /// <summary>Company image gain: price/10000·(tier+6)/55 (company) or price/15000·(tier+6)/55 (everything).</summary>
        public static int CompanyImageGain(AdType t, int tier)
        {
            long p = PriceOf(t, tier);
            if (t == AdType.Company) return (int)(p / 10000 * (tier + 6) / 55);
            if (t == AdType.Everything) return (int)(p / 15000 * (tier + 6) / 55);
            return 0;
        }

        /// <summary>Route image gain as the byte the original adds: route ad price/30000, everything price·(tier+6)/6/120000 (byte-wrapped).</summary>
        public static int RouteImageGain(AdType t, int tier)
        {
            long p = PriceOf(t, tier);
            if (t == AdType.Route) return (byte)(p / 30000);
            if (t == AdType.Everything) return (byte)(p * (tier + 6) / 6 / 120000);
            return 0;
        }

        /// <summary>Byte addition then clamp 0..100, like the original's UBYTE route image.</summary>
        static int AddRouteImage(int image, int gain)
        {
            int v = (byte)(image + gain);
            return v > 100 ? 100 : v;
        }

        /// <summary>
        /// Runs a campaign (economy.md §10): image effects, worst-ad letters, cost (3120 + tier,
        /// stat other expenses), bodyguard rebate. <paramref name="routeId"/> only for route ads
        /// (the reverse direction gets the same gain). Caller validated money (DEBT_LIMIT).
        /// </summary>
        public static void Run(World w, Player p, AdType type, int tier, int routeId = 0)
        {
            var bridge = w.OperationsBridge;
            long price = PriceOf(type, tier);
            switch (type)
            {
                case AdType.Company:
                    p.ChangeImage(CompanyImageGain(type, tier));
                    break;
                case AdType.Route:
                {
                    int gain = RouteImageGain(type, tier);
                    bridge.SetRouteImage(w, p, routeId, AddRouteImage(bridge.GetRouteImage(w, p, routeId), gain));
                    int rev = ReverseRoute(w, routeId);
                    if (rev != 0) bridge.SetRouteImage(w, p, rev, AddRouteImage(bridge.GetRouteImage(w, p, rev), gain));
                    break;
                }
                case AdType.Everything:
                {
                    p.ChangeImage(CompanyImageGain(type, tier));
                    int gain = RouteImageGain(type, tier);
                    foreach (int id in bridge.RentedRouteIds(w, p))
                        bridge.SetRouteImage(w, p, id, AddRouteImage(bridge.GetRouteImage(w, p, id), gain));
                    break;
                }
            }
            if (tier == 0)
                foreach (var o in w.Players)
                    if (o.Owner == Owner.Human && !o.IsOut) w.Events.Publish(new WorstAdLetter(o, p, type, routeId));

            p.Statistics.Add(StatId.ExpenseOther, -price);
            p.Book(-price, MoneyReason.AdCampaignBase + tier);
            p.BodyguardRebate(price);
        }

        /// <summary>Id of the route in the opposite direction (from the route table), 0 if none.</summary>
        public static int ReverseRoute(World w, int routeId)
        {
            Data.RouteDef me = null;
            foreach (var r in w.Data.Routes) if (r.Id == routeId) { me = r; break; }
            if (me == null) return 0;
            for (int i = w.Data.Routes.Count - 1; i >= 0; i--)
            {
                var r = w.Data.Routes[i];
                if (r.FromCity == me.ToCity && r.ToCity == me.FromCity) return r.Id;
            }
            return 0;
        }

        /// <summary>
        /// The ad agency's image report text (economy.md §8.5): negative → <c>Werb 2503 + Image/251</c>,
        /// positive → <c>2504 + Image/167</c>, zero → 2520.
        /// </summary>
        public static int ImageReportText(int image) => image < 0 ? 2503 + image / 251 : image > 0 ? 2504 + image / 167 : 2520;
    }
}
