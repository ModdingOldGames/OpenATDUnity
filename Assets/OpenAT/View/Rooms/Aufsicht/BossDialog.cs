using System.Text;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Stats;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;

namespace OpenAT.View.Rooms.Aufsicht
{
    /// <summary>
    /// The airport boss (rooms-and-ui-3b §3): mission introduction on day 0, the daily briefing (takeover,
    /// mission report, per-airline reviews, eliminations, game end) and the daytime "applications" talk.
    /// The decisions were taken by the 09:00 morning review in the sim; the dialogue presents the
    /// <see cref="BriefingLog"/> of the day.
    /// </summary>
    [DialogScript(Talkers.Boss)]
    public sealed class BossDialog : DialogTree
    {
        static readonly System.Random Rng = new System.Random();

        static AufsichtRoom Room(DialogSession d) => d.Stage as AufsichtRoom;
        static BriefingLog Log(DialogSession d) => BriefingLog.Of(d.World);

        /// <summary>Day-0 mission introduction (also node 701).</summary>
        static void MissionIntro(DialogSession d)
        {
            int m = d.World.Difficulty;
            if (Mission.IsAtfs(m)) d.Say(1800 + (m - 40) * 10);
            else if (m == 13 && d.Par1 == 1) d.Say(1600 + (m - 10) * 10, CityName(d.World, d.World.CrisisCityId));   // names the crisis city
            else if (Mission.IsAddOn(m)) d.Say(1600 + (m - 10) * 10);
            else d.Say(1000 + m * 100);
        }

        /// <summary>Text id of the mission report for the difficulty (6000.., 7100.., 8500..), 0 = none.</summary>
        static int ReportBase(int m)
        {
            if (Mission.IsBase(m)) return 6000 + m * 100;
            if (Mission.IsAddOn(m)) return 7100 + (m - 11) * 100;
            if (Mission.IsAtfs(m)) return 8500 + (m - 41) * 100;
            return 0;
        }

        static bool IsReport(int id, int offset) =>
            id % 100 == offset && (id >= 6000 && id <= 6504 || id >= 7100 && id <= 8004 || id >= 8500 && id <= 9404);

        static object[] MissionCities(World w)
        {
            var a = new object[7];
            for (int i = 0; i < 6; i++) a[i] = w.MissionCityIds[i] != 0 ? CityName(w, w.MissionCityIds[i]) : "";
            a[6] = CityName(w, w.HomeCity);
            return a;
        }

        static string CityName(World w, int id)
        {
            foreach (var c in w.Data.Cities.All) if (c.Id == id) return c.Name;
            return "";
        }

        static bool MoneyMetric(int m) => m == Mission.Profit || m == 11 || m == 16 || m == 41 || m == 49 || m == 50;

        public BossDialog() : base("Boss")
        {
            Open(d =>
            {
                var w = d.World;
                if (d.Par1 >= 10000) { d.End(); return; }
                if (d.Par1 == 2) { d.Say(4000); return; }
                if (d.Medium == DialogMedium.Phone) { d.Say(4001); return; }
                if (d.Par1 == 1)
                {
                    if (w.Clock.Date == 0)
                    {
                        if (w.Difficulty == Mission.FreeGame || w.Difficulty == Mission.Tutorial) d.Say(700);
                        else MissionIntro(d);
                    }
                    else if (Log(d).Takeover != null) d.Say(5000);
                    else d.Say(2000 + Rng.Next(4));
                    return;
                }
                d.Say(4000);
            });

            // ---- day-0 introduction chains
            On(700, d => d.Say(701));
            On(701, MissionIntro);
            On(1000, d => d.Say(1001));
            On(1100, d => d.Say(1101));
            On(1200, d => d.Say(1201));
            On(1201, d => d.Say(1202));
            On(1300, d => d.SayText(1301, DialogText.Get(d.Assets, "Boss", 1301, MissionCities(d.World)) ?? ""));
            On(1301, d => d.Say(1302));
            On(1302, d => d.Say(1303));
            On(1400, d => d.Say(1401));
            On(1500, d => d.Say(1501));
            On(1501, d => d.Say(1502));
            for (int id = 1610; id <= 1700; id += 10) Say(id, id + 1);
            for (int id = 1810; id <= 1900; id += 10) Say(id, id + 1);
            On(new[] { 1001, 1101, 1202, 1303, 1401, 1502 }, d => d.Say(1999));
            On(1999, d => d.End()); // the room starts the working day when the dialogue ends

            // ---- takeover (decided at the bank, executed by the morning review)
            On(5000, d =>
            {
                var t = Log(d).Takeover;
                d.Say(5001, t.Acquirer.Airline, t.Target.Airline);
            });
            On(5001, d =>
            {
                var t = Log(d).Takeover;
                if (t.Mode == TakeoverMode.Absorb) d.Say(5010, t.Acquirer.Airline, t.Target.Airline, t.Acquirer.Airline);
                else d.Say(5020, t.Acquirer.Airline, t.Target.Airline);
            });
            On(new[] { 5010, 5020 }, d => d.Say(5030, Log(d).Takeover.Target.Airline));
            On(5030, d =>
            {
                if (Log(d).Takeover.Target.Index == d.World.LocalPlayer) GameOver(d, true);
                else d.Say(5041);
            });

            // ---- daily briefing: mission report, then the reviews
            On(new[] { 2000, 2001, 2002, 2003, 2081, 5041 }, d =>
            {
                d.Par1 = 0; d.Par2 = 0; d.Par3 = 0;
                int r = ReportBase(d.World.Difficulty);
                if (r == 0 || Log(d).Report == null) Review(d);
                else d.Say(r);
            });
        }

        static void GameOver(DialogSession d, bool lost) =>
            d.OpenMenu(new GameOverMenu(lost));

        // ------------------------------------------------------------------ reviews

        /// <summary>Starts (or continues with) the review of airline <c>Par1</c> (index into the day's reviews).</summary>
        internal static void Review(DialogSession d)
        {
            var log = Log(d);
            if (d.Par1 >= log.Reviews.Count)
            {
                Room(d)?.Look(0);
                d.Say(2102);
                return;
            }
            var rv = log.Reviews[d.Par1];
            var p = rv.Player;
            Room(d)?.LookAtPlayer(p.Index);
            int remark;
            switch (rv.ImageCommentBase)
            {
                case 0: remark = 20200 + Rng.Next(3); break;
                case 2021: remark = 20210 + Rng.Next(3); break;
                case 2026: remark = 20260 + Rng.Next(5); break;
                case 2025: remark = 20250 + Rng.Next(8); break;
                default: remark = 20240 + Rng.Next(10); break;
            }
            string a = DialogText.Get(d.Assets, "Boss", 2010 + System.Math.Min(d.Par3, 3), new object[] { p.Airline }) ?? "";
            string b = DialogText.Get(d.Assets, "Boss", remark, new object[] { p.Airline }) ?? "";
            d.SayText(12040, a + " " + b);
        }

        internal static void ReviewVerdict(DialogSession d)
        {
            var log = Log(d);
            var rv = log.Reviews[d.Par1];
            var p = rv.Player;
            var why = log.OutReasonOf(p.Index);
            if (why == OutReason.Image) { d.Say(20230 + Rng.Next(2)); return; }
            if (why == OutReason.Bankrupt) { d.Say(3000, p.Airline); return; }

            int fleet, crew;
            if (rv.Planes == 0) fleet = 2035;
            else if (rv.AverageCondition < 40) fleet = 2030;
            else if (rv.AverageCondition < 60) fleet = 2031;
            else if (rv.AverageCondition < 80) fleet = 2032;
            else if (rv.AverageCondition < 90) fleet = 2033;
            else fleet = new[] { 2034, 2037, 2038 }[Rng.Next(3)];
            if (rv.Planes == 0) crew = p.Index == 3 ? 2040 + Rng.Next(2) : new[] { 2043, 2046, 2047 }[Rng.Next(3)];
            else if (rv.AverageCrewQuality < 40) crew = 2040;
            else if (rv.AverageCrewQuality < 60) crew = 2041;
            else if (rv.AverageCrewQuality < 70) crew = 2042;
            else if (rv.AverageCrewQuality < 80) crew = new[] { 2043, 2046, 2047 }[Rng.Next(3)];
            else crew = 2044;
            var sb = new StringBuilder();
            sb.Append(DialogText.Get(d.Assets, "Boss", fleet, new object[] { p.Airline }) ?? "");
            // "No flights made": flights since the game start (the original's never-cleared day value).
            if (rv.Planes > 0 && p.Statistics.Total(StatId.Flights) == 0) sb.Append(' ').Append(DialogText.Get(d.Assets, "Boss", 2036, new object[] { p.Airline }) ?? "");
            sb.Append(' ').Append(DialogText.Get(d.Assets, "Boss", crew, new object[] { p.Airline }) ?? "");
            d.SayText(2040, sb.ToString());
        }

        internal static void NextReview(DialogSession d)
        {
            d.Par1++;
            d.Par3++;
            Review(d);
        }

        // ------------------------------------------------------------------ mission report

        internal static void Report(DialogSession d, int r)
        {
            var w = d.World;
            var rep = Log(d).Report;
            var line = new StringBuilder("[[BO\\6001]]");
            var card = new StringBuilder();
            bool money = MoneyMetric(w.Difficulty);
            bool first = true;
            for (int c = 0; c < Player.Count; c++)
            {
                if (rep.Ratings[c] < 0) continue;
                string v = money ? UiKit.Money(rep.Ratings[c]) : UiKit.Thousands(rep.Ratings[c]);
                if (!first) { line.Append(", "); card.Append(TextEncoding.LineBreak); }
                first = false;
                line.Append(w.Players[c].Airline).Append(": ").Append(v);
                card.Append(w.Players[c].Airline).Append(": ").Append(v);
            }
            line.Append('.');
            d.SayText(r + 1, line.ToString());
            d.Numbers.Set(d.Stage.App.Ui.Kit, card.ToString());
            d.CanCancelEmpty = true;
        }

        internal static void ReportVerdict(DialogSession d, int r)
        {
            var w = d.World;
            var log = Log(d);
            var rep = log.Report;
            d.Numbers.Set(d.Stage.App.Ui.Kit, null);
            var leader = w.Players[rep.Leader];
            if (!rep.Ended)
            {
                // Connection and rocket missions skip straight to the reviews while the leader has nothing.
                if ((r == 6300 || r == 6500) && rep.Ratings[rep.Leader] <= 0) { Review(d); return; }
                d.Say(r + 2, leader.Airline);
                return;
            }
            if (rep.Winners.Count > 1)
            {
                var sb = new StringBuilder();
                foreach (var p in rep.Winners) { if (sb.Length > 0) sb.Append(", "); sb.Append(p.Airline); }
                d.Say(r + 4, sb.ToString());
            }
            else d.Say(r + 3, rep.Winners.Count == 1 ? rep.Winners[0].Airline : leader.Airline);
        }

        internal static void EndOfGame(DialogSession d)
        {
            var log = Log(d);
            GameOver(d, !log.Won(d.World.LocalPlayer));
        }
    }

    /// <summary>
    /// Nodes of the boss tree whose ids depend on the mission (reports) or form the review loop. Kept
    /// as <see cref="DialogActions"/> so the tree above stays data-like.
    /// </summary>
    static class BossDialogLoop
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            const int t = Talkers.Boss;
            for (int r = 6000; r <= 9400; r += 100)
            {
                if (r > 6500 && r < 7100 || r > 8000 && r < 8500) continue;
                int rr = r;
                DialogActions.On(t, rr, d => { BossDialog.Report(d, rr); return true; });
                DialogActions.On(t, rr + 1, d => { BossDialog.ReportVerdict(d, rr); return true; });
                DialogActions.On(t, rr + 2, d => { BossDialog.Review(d); return true; });
                DialogActions.On(t, rr + 3, d => { BossDialog.EndOfGame(d); return true; });
                DialogActions.On(t, rr + 4, d => { BossDialog.EndOfGame(d); return true; });
            }
            DialogActions.On(t, 12040, d => { BossDialog.ReviewVerdict(d); return true; });
            DialogActions.On(t, 2040, d => { BossDialog.NextReview(d); return true; });
            DialogActions.On(t, 3002, d => { BossDialog.NextReview(d); return true; });
            DialogActions.On(t, 3000, d => { d.Say(3001, BriefingLog.Of(d.World).Reviews[d.Par1].Player.Airline); return true; });
            DialogActions.On(t, 20230, 20231, d => OutVerdict(d));
            DialogActions.On(t, 3001, d =>
            {
                if (BriefingLog.Of(d.World).Reviews[d.Par1].Player.Index == d.World.LocalPlayer) d.OpenMenu(new GameOverMenu(true));
                else d.Say(3002);
                return true;
            });
            DialogActions.On(t, 2102, d => { d.End(); return true; });

            // Daytime talk (par1 2 / phone): applications.
            DialogActions.On(t, 4000, 4001, d =>
            {
                if (d.World.Difficulty == Mission.FreeGame) d.Choices(4011, 4012); else d.Choices(4010, 4012);
                return true;
            });
            DialogActions.On(t, 4010, d => { d.Say(4050); return true; });
            DialogActions.On(t, 4050, d =>
            {
                var w = d.World;
                int m = w.Difficulty;
                if (Mission.IsAtfs(m)) d.Say(4084 + (m - 40));
                else if (Mission.IsAddOn(m))
                {
                    // Add-on 3 names the crisis city; add-on 9 adds a number card (4090 + 9).
                    if (m == 13) d.Say(4070 + (m - 10), Name(w, w.CrisisCityId));
                    else d.Say(4070 + (m - 10));
                    if (m == 19) d.ShowNumbers(4090 + (m - 10));
                }
                else if (m == Mission.ConnectWorld)
                {
                    var a = new object[7];
                    for (int i = 0; i < 6; i++) a[i] = Name(w, w.MissionCityIds[i]);
                    a[6] = Name(w, w.HomeCity);
                    d.SayText(4063, DialogText.Get(d.Assets, "Boss", 4063, a) ?? "");
                }
                else d.Say(4060 + m);
                return true;
            });
            // Airport expansion (rooms-and-ui-6 §3.8): the sim decides 4110/4111/4114/4115/4112/4113.
            DialogActions.On(t, 4011, d => { d.Say(OpenAT.Sim.Hall.AirportExpansion.Decide(d.World)); return true; });
            DialogActions.On(t, 4113, d => { d.Choices(4120, 4121); return true; });
            DialogActions.On(t, 4120, d =>
            {
                var r = d.World.Execute(new OpenAT.Sim.Hall.RequestAirportExpansionCommand(d.World.LocalPlayer));
                if (!r.Ok) UnityEngine.Debug.Log("OpenAT boss: expansion refused: " + r);
                d.Say(4130);
                return true;
            });
            DialogActions.On(t, 4121, d => { d.Say(4131); return true; });
            foreach (int id in new[] { 4012, 4130, 4131, 4110, 4111, 4112, 4114, 4115, 4060, 4061, 4062, 4063, 4064, 4065 })
                DialogActions.On(t, id, d => EndTalk(d));
            for (int id = 4070; id <= 4094; id++) DialogActions.On(t, id, d => EndTalk(d));
            DialogActions.On(t, 4999, d => { d.End(); return true; });
        }

        static string Name(World w, int id)
        {
            foreach (var c in w.Data.Cities.All) if (c.Id == id) return c.Name;
            return "";
        }

        static bool EndTalk(DialogSession d)
        {
            if (d.Medium == DialogMedium.Phone) d.Say(4999); else d.End();
            return true;
        }

        static bool OutVerdict(DialogSession d)
        {
            var p = BriefingLog.Of(d.World).Reviews[d.Par1].Player;
            if (p.Index == d.World.LocalPlayer) d.OpenMenu(new GameOverMenu(true));
            else BossDialog.NextReview(d);
            return true;
        }
    }
}
