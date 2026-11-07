using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;

namespace OpenAT.Sim.Missions
{
    /// <summary>
    /// Mission counters of an airline (sim-core.md §3.4, §3.6). Written by the domains that own the
    /// events (flights, contracts, freight, NASA shop); read by the mission rules.
    /// <see cref="Player.Profit"/> (Gewinn) stays in the finance state.
    /// </summary>
    public sealed class MissionCounters
    {
        /// <summary>Completed contracts ("NumAuftraege", tutorial).</summary>
        public long Orders;
        /// <summary>Passengers carried ("NumPassengers").</summary>
        public long Passengers;
        /// <summary>Freight tons carried ("NumFracht") and relief freight ("NumFrachtFree").</summary>
        public long Freight, ReliefFreight;
        /// <summary>Flown miles ("NumMiles").</summary>
        public long Miles;
        /// <summary>Service points (recomputed by the add-on 5 rating).</summary>
        public long ServicePoints;
        /// <summary>Manager order flights done ("NumOrderFlights").</summary>
        public long OrderFlights;
        /// <summary>Number of the 12 connections of mission 3 (recomputed at 9:00 by the routes domain).</summary>
        public int ConnectFlags;
        /// <summary>Rocket / station part bits (10 bits) and the value at the last headline check.</summary>
        public int RocketFlags, LastRocketFlags;
        /// <summary>Days without being sabotaged (+1 each night; reset by the sabotage domain).</summary>
        public int DaysWithoutSabotage;

        public void Clear()
        {
            Orders = Passengers = Freight = ReliefFreight = Miles = ServicePoints = OrderFlights = 0;
            ConnectFlags = RocketFlags = LastRocketFlags = DaysWithoutSabotage = 0;
        }
    }

    /// <summary>Mission targets (sim-core.md §4.1).</summary>
    public static class MissionTargets
    {
        public const int TutorialOrders = 10;
        public const int Passengers = 2500;
        public const long Profit = 5000000;
        public const int Flags = 10;
        public const int Image = 750;
        public const int Freight = 1000;
        public const int Days = 21;
        public const int MilesDays = 30;
        public const int Service = 150;
        public const int ValueDays = 21;
        public const int Shares = 220;
        public const int OrderFlights = 200;
        public const long AtfsMoney = 15000000;
        public const int AtfsCondition = 90;
        public const int AtfsPassengersAvg = 500;
        public const int AtfsDaysWithoutSabotage = 15;
        public const int AtfsPlaneSize = 600;
        public const int AtfsSharePrice = 200;
        public const int AtfsOwnSharePercent = 20;
        public const int AtfsFuelUse = 500;
        public const int Atfs9Days = 45;
        public const int Atfs10Days = 60;

        /// <summary>Day limit of the "best at day N" missions, 0 for open missions.</summary>
        public static int DayLimit(int d)
        {
            switch (d)
            {
                case 13: return Days;
                case 14: return MilesDays;
                case 16: return ValueDays;
                case 49: return Atfs9Days;
                case 50: return Atfs10Days;
                default: return 0;
            }
        }

        /// <summary>Lower rating is better (add-on 1, debt).</summary>
        public static bool LowerIsBetter(int d) => d == 11;

        /// <summary>ATFS missions that end as soon as anybody meets the goal.</summary>
        public static bool AnyoneWins(int d) => d == 42 || d == 43 || d == 44 || d == 48;

        /// <summary>Mission flags BONUS_X2/X4/X8 (sim-core.md §3.7) as one multiplier.</summary>
        public static int BonusMultiplier(int d)
        {
            int m = 1;
            if (d == 2 || d == 3 || d == 4 || d == 11 || d == 12 || d == 13) m *= 2;
            if (d == 5 || d == 14 || d == 17) m *= 4;
            if (d == Mission.FreeGame || d == 15 || d == 16 || d == 18 || d == 19 || d == 20) m *= 8;
            return m;
        }

        /// <summary>Mission flag IMAGEBONUS (computers' daily image bonus; add-on 6).</summary>
        public static bool ImageBonus(int d) => d == 16;
    }

    /// <summary>Mission rating and goal test (sim-core.md §3.4). Replaceable rule.</summary>
    public interface IMissionRule : IRule
    {
        /// <summary>Mission metric of an airline (original <c>GetMissionRating</c>); −1 when out.</summary>
        long Rating(World w, Player p, bool other = false);
        /// <summary>Has the airline met the goal (original <c>HasWon</c>)?</summary>
        bool HasWon(World w, Player p);
    }

    /// <summary>The original mission rules (sim-core.md §3.4, economy.md §17).</summary>
    public sealed class MissionRuleOriginal : IMissionRule
    {
        static int Bits(int flags) { int n = 0; for (int c = 0; c < 32; c++) if ((flags & (1 << c)) != 0) n++; return n; }
        static long Clamp32(long v) => v > int.MaxValue ? int.MaxValue : v;

        public long Rating(World w, Player p, bool other = false)
        {
            if (p.IsOut) return -1;
            var k = p.Counters;
            var planes = w.OperationsBridge.Planes(w, p);
            switch (w.Difficulty)
            {
                case Mission.Tutorial: return k.Orders;
                case Mission.FreeGame:
                case Mission.Passengers: return k.Passengers;
                case Mission.Profit: return Clamp32(p.Profit);
                case Mission.ConnectWorld: return k.ConnectFlags;
                case Mission.Image: return p.Image / 10;
                case Mission.Rocket: return Bits(k.RocketFlags);
                case 11: return Clamp32(p.Money > 0 ? p.Credit : p.Credit - p.Money);
                case 12: return k.Freight;
                case 13: return k.ReliefFreight;
                case 14: return k.Miles;
                case 15:
                {
                    long sp = 0;
                    foreach (var pl in planes)
                        sp += pl.SeatLevel + pl.TrayLevel + pl.DecorLevel + pl.EngineLevel + pl.TyreLevel + pl.ElectronicsLevel + pl.SafetyLevel;
                    if (p.IsComputer) sp += 2L * planes.Count; // simulated crew: (80 − 50)/15 per plane
                    else
                        foreach (var wk in w.Workers.Workers)
                            if (wk.Employer == p.Index && wk.PlaneId != -1 && wk.Talent > 50) sp += (wk.Talent - 60) / 15;
                    k.ServicePoints = sp;
                    return sp;
                }
                case 16: case 49: case 50: return Clamp32(p.Statistics.Today(StatId.CompanyValue));
                case 17:
                {
                    long sum = 0;
                    foreach (var pl in planes) sum += pl.Condition;
                    return planes.Count > 0 ? sum / planes.Count : 0;
                }
                case 18: return (long)p.SharePrice;
                case 19: return k.OrderFlights;
                case 20: return Bits(k.RocketFlags);
                case 41: return p.Money;
                case 42:
                {
                    int n = 0;
                    foreach (var pl in planes)
                        if (pl.Condition >= MissionTargets.AtfsCondition && pl.TyreLevel == 2 && pl.EngineLevel == 2 && pl.SafetyLevel == 2 && pl.ElectronicsLevel == 2) n++;
                    return n;
                }
                case 43:
                {
                    var s = p.Statistics;
                    // Passengers of the last 5 days: a difference of running totals in the original, a sum of
                    // daily values with the statistics day shift fixed (same result).
                    long pax = 0;
                    if (w.Options.FixStatisticsDayShift) for (int c = 1; c <= 5; c++) pax += s.PastDay(StatId.Passengers, c);
                    else pax = s.PastDay(StatId.Passengers, 1) - s.PastDay(StatId.Passengers, 6);
                    long f = 0;
                    for (int c = 0; c < System.Math.Min(w.Clock.Date, 5); c++) f += s.PastDay(StatId.Planes, c + 1);
                    return f != 0 ? pax / f : 0;
                }
                case 44: case 46: return k.DaysWithoutSabotage;
                case 45:
                {
                    int n = 0;
                    foreach (var pl in planes) if (pl.Seats >= MissionTargets.AtfsPlaneSize && pl.IsDesigned) n++;
                    return n;
                }
                case 47:
                {
                    var s = p.Statistics;
                    long sum = 0;
                    if (!other)
                    {
                        long n = 0;
                        for (int c = 1; c <= System.Math.Min(29, w.Clock.Date); c++) { sum += (int)s.PastDay(StatId.SharePrice, c); n++; }
                        return n > 0 ? sum / n : 0;
                    }
                    for (int c = 0; c <= 29; c++)
                    {
                        long issued = s.PastDay(StatId.SharesIssued, c);
                        if (issued > 0 && s.PastDay(StatId.SharesHeldBase + p.Index, c) * 100 / issued <= MissionTargets.AtfsOwnSharePercent) sum++;
                    }
                    return sum;
                }
                case 48:
                {
                    int n = 0;
                    foreach (var pl in planes)
                        if (pl.IsDesigned && pl.Seats > 0 && pl.Speed > 0 && pl.FuelUse * 100 / pl.Speed <= MissionTargets.AtfsFuelUse) n++;
                    return n;
                }
            }
            return 0;
        }

        static bool NobodyHigher(World w, Player p, System.Func<Player, long> value)
        {
            long mine = value(p);
            foreach (var o in w.Players) if (!o.IsOut && value(o) > mine) return false;
            return true;
        }

        public bool HasWon(World w, Player p)
        {
            var k = p.Counters;
            int planes = w.OperationsBridge.Planes(w, p).Count;
            switch (w.Difficulty)
            {
                case Mission.Tutorial: return k.Orders >= MissionTargets.TutorialOrders;
                case Mission.Passengers: return k.Passengers >= MissionTargets.Passengers;
                case Mission.Profit: return p.Profit >= MissionTargets.Profit;
                case Mission.ConnectWorld: return k.ConnectFlags >= MissionTargets.Flags;
                case Mission.Image: return p.Image >= MissionTargets.Image;
                case Mission.Rocket: return Bits(k.RocketFlags) >= 10;
                case 11: return Rating(w, p) == 0;
                case 12: return k.Freight >= MissionTargets.Freight;
                case 13: return NobodyHigher(w, p, o => o.Counters.ReliefFreight);
                case 14: return NobodyHigher(w, p, o => o.Counters.Miles);
                case 15: return k.ServicePoints > MissionTargets.Service;
                case 16: case 49: case 50: return NobodyHigher(w, p, o => o.Statistics.Today(StatId.CompanyValue));
                case 17:
                    if (planes < 2) return false;
                    foreach (var pl in w.OperationsBridge.Planes(w, p)) if (pl.Condition < 90) return false;
                    return true;
                case 18: return p.SharePrice >= MissionTargets.Shares;
                case 19: return k.OrderFlights >= MissionTargets.OrderFlights;
                case 20: return Bits(k.RocketFlags) >= 10;
                case 41: return p.Money >= MissionTargets.AtfsMoney;
                case 42: return planes >= 5 && Rating(w, p) >= planes;
                case 43: return planes >= 4 && Rating(w, p) >= MissionTargets.AtfsPassengersAvg;
                case 44: case 46: return planes >= 5 && k.DaysWithoutSabotage >= MissionTargets.AtfsDaysWithoutSabotage;
                case 45: return Rating(w, p) >= 3;
                case 47:
                    if (Rating(w, p) < MissionTargets.AtfsSharePrice) return false;
                    for (int c = 0; c <= 29; c++)
                    {
                        long issued = p.Statistics.PastDay(StatId.SharesIssued, c);
                        // The original divides without a zero check; a day without a snapshot fails the test here.
                        if (issued <= 0) return false;
                        if (p.Statistics.PastDay(StatId.SharesHeldBase + p.Index, c) * 100 / issued > MissionTargets.AtfsOwnSharePercent) return false;
                    }
                    return true;
                case 48: return Rating(w, p) >= 5;
            }
            return false;
        }
    }

    // ---------------------------------------------------------------------- events

    /// <summary>Reason an airline left the game.</summary>
    public enum OutReason { Image = 1, Bankrupt = 2, Absorbed = 3, Liquidated = 4 }

    /// <summary>An airline was removed from the game (economy.md §17.2).</summary>
    public sealed class PlayerOut : ISimEvent
    {
        public readonly Player Player;
        public readonly OutReason Reason;
        public PlayerOut(Player p, OutReason r) { Player = p; Reason = r; }
    }

    /// <summary>The game is over for the local player (lost; or the mission ended without a win).</summary>
    public sealed class GameOver : ISimEvent
    {
        public readonly Player Player;
        /// <summary>Null when the mission ended normally (the local player did not meet the goal).</summary>
        public readonly OutReason? Reason;
        public GameOver(Player p, OutReason? r) { Player = p; Reason = r; }
    }

    /// <summary>An airline met the mission goal at the morning report.</summary>
    public sealed class MissionWon : ISimEvent
    {
        public readonly Player Player;
        public MissionWon(Player p) => Player = p;
    }

    /// <summary>The daily mission report (Boss 6000 + 100·mission …): metric per airline and the leader.</summary>
    public sealed class MissionReport : ISimEvent
    {
        public readonly long[] Ratings;
        public readonly int Leader;
        /// <summary>The mission is over (goal met, or the day limit reached).</summary>
        public readonly bool Ended;
        public readonly IReadOnlyList<Player> Winners;
        public MissionReport(long[] ratings, int leader, bool ended, IReadOnlyList<Player> winners)
        { Ratings = ratings; Leader = leader; Ended = ended; Winners = winners; }
    }

    /// <summary>The boss's per-airline review (economy.md §17.1): image comment tier and plane/crew averages.</summary>
    public sealed class AirlineReviewed : ISimEvent
    {
        public readonly Player Player;
        /// <summary>Text id base of the image comment (2021x / 2024x / 2025x / 2026x), or 0 for the "image got worse" remark.</summary>
        public readonly int ImageCommentBase;
        public readonly int AverageCondition, AverageCrewQuality, Planes;
        public AirlineReviewed(Player p, int imageComment, int cond, int crew, int planes)
        { Player = p; ImageCommentBase = imageComment; AverageCondition = cond; AverageCrewQuality = crew; Planes = planes; }
    }
}
