using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Missions
{
    /// <summary>
    /// The boss's morning briefing, simulation part (sim-core.md §2.8 step 3, §3.4; economy.md §17):
    /// statistics refresh, then from day 1: pending takeover, mission report (win / day limit →
    /// game end), then every active airline's review with the image (&lt; −990) and bankruptcy
    /// (cash &lt; −5 000 000) checks. Order 300 (= step 3); the routes domain recomputes ConnectFlags
    /// before it (step 2).
    /// </summary>
    public sealed class MorningReviewSystem : SimSystem
    {
        public const string SystemName = "missions.morning-review";
        public override string Name => SystemName;
        public override int Order => 300;

        public override void OnMorning(World w)
        {
            if (w.GameEnded) return;
            w.UpdateStatistics();
            if (w.Clock.Date == 0) return; // day 0: mission introduction only

            w.ExecuteTakeover();
            if (w.GameEnded) return;

            if (w.Difficulty != Mission.FreeGame && MissionReport(w)) return;

            foreach (var p in w.Players)
            {
                if (p.IsOut) continue;
                Review(w, p);
                if (w.GameEnded) return;
            }
        }

        /// <summary>Mission report; returns true when the mission (and the game) ended.</summary>
        public static bool MissionReport(World w)
        {
            var rule = w.Rules.Get<IMissionRule>();
            int d = w.Difficulty;
            var ratings = new long[Player.Count];
            for (int c = 0; c < Player.Count; c++) ratings[c] = rule.Rating(w, w.Players[c]);

            // Leader: the original starts the scan with airline 0 (even when it is out).
            int leader = 0;
            bool lower = MissionTargets.LowerIsBetter(d);
            for (int c = 0; c < Player.Count; c++)
                if (!w.Players[c].IsOut && (lower ? ratings[c] < ratings[leader] : ratings[c] > ratings[leader])) leader = c;

            var winners = new List<Player>();
            foreach (var p in w.Players) if (!p.IsOut && rule.HasWon(w, p)) winners.Add(p);

            int limit = MissionTargets.DayLimit(d);
            bool ended;
            if (limit > 0) ended = w.Clock.Date >= limit;
            else if (MissionTargets.AnyoneWins(d)) ended = winners.Count > 0;
            else ended = rule.HasWon(w, w.Players[leader]);

            w.Events.Publish(new MissionReport(ratings, leader, ended, winners));
            if (!ended) return false;

            foreach (var p in winners) w.Events.Publish(new MissionWon(p));
            w.GameEnded = true;
            var local = w.Players[w.LocalPlayer];
            if (!winners.Contains(local)) w.Events.Publish(new GameOver(local, null));
            return true;
        }

        /// <summary>One airline's review: image comment, plane/crew averages, game-over checks.</summary>
        public static void Review(World w, Player p)
        {
            int comment;
            if (p.ImageGotWorse) { comment = 0; p.ImageGotWorse = false; }
            else if (p.Image < -100) comment = 2021;
            else if (p.Image > 500) comment = 2026;
            else if (p.Image > 100) comment = 2025;
            else comment = 2024;

            var planes = w.OperationsBridge.Planes(w, p);
            int cond = 0, crew = 0;
            foreach (var pl in planes) { cond += pl.Condition; crew += p.CrewQuality(pl.Id); }
            if (planes.Count > 0) { cond /= planes.Count; crew /= planes.Count; }
            w.Events.Publish(new AirlineReviewed(p, comment, cond, crew, planes.Count));

            if (p.Image < -990) w.RemovePlayer(p, OutReason.Image);
            else if (p.Money < Player.DebtGameOver) w.RemovePlayer(p, OutReason.Bankrupt);
        }
    }
}
