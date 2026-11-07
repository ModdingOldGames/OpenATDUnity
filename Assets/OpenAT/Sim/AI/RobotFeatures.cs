using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.AI
{
    /// <summary>
    /// Per-mission switches of the computer players (ai-robots.md §8, sim-core.md §3.7, the original's
    /// "RobotUse" lookup). The original names are given in the comments.
    /// </summary>
    public enum RobotFeature
    {
        /// <summary>SABOTAGE: may use the saboteur and kerosene rooms.</summary>
        Sabotage,
        /// <summary>FRACHT: takes cargo jobs.</summary>
        Freight,
        /// <summary>WERBUNG: uses the advertising agency.</summary>
        Advertising,
        /// <summary>AUFSICHT: late-day board visits.</summary>
        LateBoard,
        /// <summary>NASA: visits NASA, rocket logic.</summary>
        Nasa,
        /// <summary>NEVERGIVEROUTEUP: never drops a losing route.</summary>
        NeverGiveRouteUp,
        /// <summary>ROUTES: unused, always off.</summary>
        Routes,
        /// <summary>SUGGESTROUTES: the route-mode switch test passes at once.</summary>
        SuggestRoutes,
        /// <summary>FORCEROUTES: forced route mode.</summary>
        ForceRoutes,
        /// <summary>HARDWERBUNG: money-based campaign tiers.</summary>
        HardAdvertising,
        /// <summary>SELLSHARES: slot 2 may fire-sell shares early.</summary>
        SellShares,
        /// <summary>BUYOWNSHARES: buy back own shares.</summary>
        BuyOwnShares,
        /// <summary>REBUYSHARES: slot 3 rebuys half of a share issue.</summary>
        RebuyShares,
        /// <summary>TUTORIALLIMIT: daily order cap.</summary>
        TutorialLimit,
        /// <summary>ROUTEMISSION: prefer mission routes at the route box.</summary>
        RouteMission,
        /// <summary>MUCHWERBUNG: image race campaigns.</summary>
        MuchAdvertising,
        /// <summary>BONUS_X2 / X4 / X8: bonus refill multipliers (applied by the finance domain).</summary>
        BonusX2, BonusX4, BonusX8,
        /// <summary>ROUTEBOX: the route box exists.</summary>
        RouteBox,
        /// <summary>ABROAD: phones the branch offices for orders.</summary>
        Abroad,
        /// <summary>MUCH_SABOTAGE: may sabotage humans.</summary>
        MuchSabotage,
        /// <summary>PAYBACK_CREDIT: repays the starting credit.</summary>
        PaybackCredit,
        /// <summary>MUCH_FRACHT: cargo first.</summary>
        MuchFreight,
        /// <summary>FREE_FRACHT: takes free relief cargo.</summary>
        FreeFreight,
        /// <summary>RUN_FRACHT: runs to the cargo office first thing in the morning.</summary>
        RunFreight,
        /// <summary>LUXERY: buys cabin upgrades for a flat price.</summary>
        Luxury,
        /// <summary>TRAVELHOLDING: travel agency and last-minute counter exist.</summary>
        TravelHolding,
        /// <summary>IMAGEBONUS: daily free image (applied by the finance domain).</summary>
        ImageBonus,
        /// <summary>GOODPLANES: always repairs, morning mechanic visits.</summary>
        GoodPlanes,
        /// <summary>REPAIRPLANES: careful repair mission.</summary>
        RepairPlanes,
        /// <summary>HIGHSHAREPRICE: dividend fixed at 25.</summary>
        HighSharePrice,
        /// <summary>WORKQUICK: half the room time.</summary>
        WorkQuick,
        /// <summary>GROSSESKONTO: hoards money (no big-plane museum visits, no ads, plane cap 3).</summary>
        HoardMoney,
        /// <summary>EMITMUCHSHARES: issues shares whenever possible.</summary>
        EmitMuchShares,
        /// <summary>WORKVERYQUICK: a quarter of the room time.</summary>
        WorkVeryQuick,
        /// <summary>ALLRUN: always runs.</summary>
        AllRun,
        /// <summary>DONTBUYANYSHARES: never buys shares.</summary>
        DontBuyAnyShares,
        /// <summary>MAXKREDIT: keeps the credit topped up.</summary>
        MaxCredit,
        /// <summary>MAX4PLANES / MAX5PLANES / MAX10PLANES: plane caps.</summary>
        Max4Planes, Max5Planes, Max10Planes,
        /// <summary>NOCHITCHAT: no kiosk, Rick's or telescope.</summary>
        NoChitChat,
        /// <summary>OFTENMECH: mechanic instead of chit-chat.</summary>
        OftenMechanic,
        /// <summary>SHORTFLIGHTS: short flights only with a 4-plane fleet.</summary>
        ShortFlights,
        /// <summary>EXTREME_SABOTAGE: heavy sabotage.</summary>
        ExtremeSabotage,
        /// <summary>SECURTY_OFFICE: uses the security office.</summary>
        SecurityOffice,
        /// <summary>MAKLER: uses the plane broker.</summary>
        Broker,
        /// <summary>PETROLAIR: uses the kerosene dealer.</summary>
        PetrolAir,
        /// <summary>MAX20PERCENT: keeps its own stake at or below 20 %.</summary>
        Max20Percent,
        /// <summary>TANKS: hoards kerosene.</summary>
        Tanks,
        /// <summary>DESIGNER: visits the designer.</summary>
        Designer,
        /// <summary>DESIGNER_BUY: buys the mission's designer planes.</summary>
        DesignerBuy,
    }

    /// <summary>Lookup of the mission feature matrix. Replace to change the robots' behaviour per mission.</summary>
    public interface IRobotFeatures : IRule
    {
        bool Has(World w, RobotFeature f);
    }

    /// <summary>
    /// The mission feature matrix as data (ai-robots.md §8 / sim-core.md §3.7). One row per feature:
    /// "base 0-5 | free game | add-on 1-10 | ATFS 1-10", X = on. Modders can build their own table from
    /// rows (<see cref="FromRows"/>) or switch single cells (<see cref="Set"/>).
    /// </summary>
    public sealed class RobotFeatureTable : IRobotFeatures
    {
        /// <summary>Levels: base 0..5, free game 6, add-on 7..16, ATFS 17..26.</summary>
        public const int Levels = 27;

        public static readonly IReadOnlyDictionary<RobotFeature, string> OriginalRows = new Dictionary<RobotFeature, string>
        {
            [RobotFeature.Sabotage]         = "-XXXXX|X|XXXXXXXXXX|-XXXXXXXXX",
            [RobotFeature.Freight]          = "------|X|-XXXXXXXXX|XXXXXXXXXX",
            [RobotFeature.Advertising]      = "---XXX|X|XXXXXXXXXX|-XX-X-XXXX",
            [RobotFeature.LateBoard]        = "----XX|X|----------|----------",
            [RobotFeature.Nasa]             = "-----X|-|---------X|----------",
            [RobotFeature.NeverGiveRouteUp] = "----X-|-|----------|----------",
            [RobotFeature.Routes]           = "------|-|----------|----------",
            [RobotFeature.SuggestRoutes]    = "---X--|-|----------|----------",
            [RobotFeature.ForceRoutes]      = "------|-|-----X----|----------",
            [RobotFeature.HardAdvertising]  = "----XX|-|------XXXX|----------",
            [RobotFeature.SellShares]       = "XXXX--|-|XXX-------|X---------",
            [RobotFeature.BuyOwnShares]     = "----XX|-|-------XXX|----------",
            [RobotFeature.RebuyShares]      = "-----X|-|--------XX|----------",
            [RobotFeature.TutorialLimit]    = "X-----|-|----------|----------",
            [RobotFeature.RouteMission]     = "---X--|-|----------|----------",
            [RobotFeature.MuchAdvertising]  = "----X-|-|----------|----------",
            [RobotFeature.BonusX2]          = "--XXX-|-|XXX-------|----------",
            [RobotFeature.BonusX4]          = "-----X|-|---X--X---|----------",
            [RobotFeature.BonusX8]          = "------|X|----XX-XXX|----------",
            [RobotFeature.RouteBox]         = "-XXXXX|X|XXXXXXXXXX|XXX-X-XXXX",
            [RobotFeature.Abroad]           = "--XXXX|X|XXXXXXXXXX|XXXXXXXXXX",
            [RobotFeature.MuchSabotage]     = "---XXX|X|XXXXXXXXXX|XXXXXXXXXX",
            [RobotFeature.PaybackCredit]    = "------|-|X---------|----------",
            [RobotFeature.MuchFreight]      = "------|-|-X--------|----------",
            [RobotFeature.FreeFreight]      = "------|-|--X-------|----------",
            [RobotFeature.RunFreight]       = "------|-|--X-------|----------",
            [RobotFeature.Luxury]           = "------|-|----X-----|-X--------",
            [RobotFeature.TravelHolding]    = "XXXXXX|X|XXXXX-XXXX|XXXXXXXXXX",
            [RobotFeature.ImageBonus]       = "------|-|-----X----|----------",
            [RobotFeature.GoodPlanes]       = "------|-|-----X----|-X--------",
            [RobotFeature.RepairPlanes]     = "------|-|------X---|----------",
            [RobotFeature.HighSharePrice]   = "------|-|-------X--|XXXXXXXXXX",
            [RobotFeature.WorkQuick]        = "------|-|---XXXXXXX|XXXXXXXXXX",
            [RobotFeature.HoardMoney]       = "------|-|----------|X---------",
            [RobotFeature.EmitMuchShares]   = "------|-|----------|X---X--X--",
            [RobotFeature.WorkVeryQuick]    = "------|-|----------|-----XXXXX",
            [RobotFeature.AllRun]           = "------|-|----------|---------X",
            [RobotFeature.DontBuyAnyShares] = "------|-|----------|X---------",
            [RobotFeature.MaxCredit]        = "------|-|----------|XXX-----XX",
            [RobotFeature.Max4Planes]       = "------|-|----------|--X-X--X--",
            [RobotFeature.Max5Planes]       = "------|-|----------|---X-X----",
            [RobotFeature.Max10Planes]      = "------|-|----------|-X--------",
            [RobotFeature.NoChitChat]       = "------|-|----------|--X----XXX",
            [RobotFeature.OftenMechanic]    = "------|-|----------|-X--X--XXX",
            [RobotFeature.ShortFlights]     = "------|-|----------|--X-------",
            [RobotFeature.ExtremeSabotage]  = "------|-|----------|---X-X----",
            [RobotFeature.SecurityOffice]   = "------|X|----------|---X-X-X-X",
            [RobotFeature.Broker]           = "XXXXXX|X|XXXXXXXXXX|XXX-X-XXXX",
            [RobotFeature.PetrolAir]        = "XXXXXX|X|XXXXXXXXXX|XXXXX-XX--",
            [RobotFeature.Max20Percent]     = "XXXXXX|X|XXXXXXXXXX|XXXXX-XXXX",
            [RobotFeature.Tanks]            = "------|-|----------|--------XX",
            [RobotFeature.Designer]         = "------|X|----------|---XX-XX--",
            [RobotFeature.DesignerBuy]      = "------|-|----------|----X--X--",
        };

        static readonly int FeatureCount = Enum.GetValues(typeof(RobotFeature)).Length;
        readonly bool[] _on = new bool[FeatureCount * Levels];

        /// <summary>The original matrix.</summary>
        public static RobotFeatureTable CreateOriginal() => FromRows(OriginalRows);

        /// <summary>Builds a table from rows in the "base|free|add-on|ATFS" format (missing features are off).</summary>
        public static RobotFeatureTable FromRows(IReadOnlyDictionary<RobotFeature, string> rows)
        {
            var t = new RobotFeatureTable();
            foreach (var kv in rows)
            {
                string cells = kv.Value.Replace("|", "");
                if (cells.Length != Levels) throw new ArgumentException("Feature row " + kv.Key + " needs " + Levels + " cells");
                for (int l = 0; l < Levels; l++) t.Set(kv.Key, l, cells[l] == 'X');
            }
            return t;
        }

        /// <summary>Level index of a mission value (ai-robots.md §8): base 0..5, free game 6, add-on 7..16, ATFS 17..26; −1 unknown.</summary>
        public static int LevelOf(int difficulty)
        {
            if (difficulty == Mission.FreeGame) return 6;
            if (Mission.IsBase(difficulty)) return difficulty;
            if (Mission.IsAddOn(difficulty)) return 7 + difficulty - Mission.AddOnFirst;
            if (Mission.IsAtfs(difficulty)) return 17 + difficulty - Mission.AtfsFirst;
            return -1;
        }

        public bool Get(RobotFeature f, int level) => level >= 0 && level < Levels && _on[(int)f * Levels + level];

        public void Set(RobotFeature f, int level, bool on)
        {
            if (level < 0 || level >= Levels) throw new ArgumentOutOfRangeException(nameof(level));
            _on[(int)f * Levels + level] = on;
        }

        public bool Has(World w, RobotFeature f) => Get(f, LevelOf(w.Difficulty));
    }
}
