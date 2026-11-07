using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Staff;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    public class FinanceStaffTests
    {
        static Worker Add(World w, int type, int talent, int salary, int employer)
        {
            var wk = new Worker { Name = "W" + w.Workers.Count, Type = type, Talent = talent, Salary = salary, OriginalSalary = salary, Employer = employer };
            w.Workers.Workers.Add(wk);
            return wk;
        }

        [Fact]
        public void MapWorkers_SmallestShortfallFirstThenLuxury()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, RequiredPilots = 2, RequiredAttendants = 3, MaxAttendants = 4 });
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 2, RequiredPilots = 1, RequiredAttendants = 2, MaxAttendants = 2 });
            var w = FinanceFixture.Make(bridge: bridge);
            var p = w.Players[0];
            var pil = new[] { Add(w, 10, 50, 5000, 0), Add(w, 10, 60, 5000, 0), Add(w, 10, 70, 5000, 0) };
            var att = new List<Worker>();
            for (int i = 0; i < 6; i++) att.Add(Add(w, 11, 10 * (i + 1), 4000, 0));
            p.MapWorkers(false);
            Assert.Equal(2, pil[0].PlaneId);
            Assert.Equal(1, pil[1].PlaneId);
            Assert.Equal(1, pil[2].PlaneId);
            Assert.Equal(2, att[0].PlaneId);
            Assert.Equal(2, att[1].PlaneId);
            Assert.Equal(1, att[5].PlaneId); // luxury slot
            Assert.Equal(new CrewInfo { Pilots = 2, Attendants = 4, Quality = (60 + 70 + 30 + 40 + 50 + 60) / 6 }, p.Crew(1));
            Assert.Equal(new CrewInfo { Pilots = 1, Attendants = 2, Quality = (50 + 10 + 20) / 3 }, p.Crew(2));
            Assert.Equal(0, p.PilotSurplus);
            Assert.Equal(0, p.AttendantSurplus);
            Assert.Equal(3, p.PilotCount);
            Assert.Equal(6, p.AttendantCount);
            Assert.Equal(60, p.CrewQuality(99)); // unknown plane
        }

        [Fact]
        public void MapWorkers_LuxuryQuirkCountsOnEveryPlane()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, RequiredPilots = 0, RequiredAttendants = 1, MaxAttendants = 2 });
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 2, RequiredPilots = 0, RequiredAttendants = 1, MaxAttendants = 2 });
            var w = FinanceFixture.Make(options: new GameOptions { FixCrewMapping = false }, bridge: bridge);
            var p = w.Players[0];
            Add(w, 11, 50, 4000, 0); Add(w, 11, 50, 4000, 0);
            var extra = Add(w, 11, 50, 4000, 0);
            p.MapWorkers(false);
            Assert.Equal(2, extra.PlaneId);      // the last plane with a free slot keeps it
            Assert.Equal(2, p.Crew(1).Attendants); // ...but plane 1 counted it as well (original)
            Assert.Equal(2, p.Crew(2).Attendants);
        }

        [Fact]
        public void MapWorkers_Fixed_OneLuxurySlotAndSecondPass()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, RequiredPilots = 0, RequiredAttendants = 1, MaxAttendants = 2 });
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 2, RequiredPilots = 0, RequiredAttendants = 1, MaxAttendants = 2 });
            var w = FinanceFixture.Make(bridge: bridge); // FixCrewMapping on by default
            var p = w.Players[0];
            var first = Add(w, 11, 50, 4000, 0);
            Add(w, 11, 50, 4000, 0);
            var extra = Add(w, 11, 50, 4000, 0);
            p.MapWorkers(false);
            Assert.Equal(1, first.PlaneId);
            Assert.Equal(1, extra.PlaneId);        // first free luxury slot only
            Assert.Equal(2, p.Crew(1).Attendants);
            Assert.Equal(1, p.Crew(2).Attendants);
            Assert.Equal(-1, p.AttendantSurplus);  // desired 4, have 3

            // A third plane short of base crew: one of plane 1's two attendants (the first in pool order)
            // is freed from the luxury slot and moves there (second pass).
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 3, RequiredPilots = 0, RequiredAttendants = 1, MaxAttendants = 1 });
            p.MapWorkers(false);
            Assert.Equal(3, first.PlaneId);
            Assert.Equal(1, extra.PlaneId);
            Assert.Equal(1, p.Crew(1).Attendants);
            Assert.Equal(1, p.Crew(2).Attendants);
            Assert.Equal(1, p.Crew(3).Attendants);
            Assert.Equal(-2, p.AttendantSurplus);  // desired 5, have 3

            // Original: the second pass never runs, plane 3 stays without crew.
            var o = FinanceFixture.Make(options: new GameOptions { FixCrewMapping = false }, bridge: bridge);
            var q = o.Players[0];
            Add(o, 11, 50, 4000, 0).PlaneId = 1;
            Add(o, 11, 50, 4000, 0).PlaneId = 2;
            var lux = Add(o, 11, 50, 4000, 0);
            lux.PlaneId = 1;
            q.MapWorkers(false);
            Assert.Equal(1, lux.PlaneId);
            Assert.Equal(0, q.Crew(3).Attendants);
        }

        [Fact]
        public void Salaries_RaiseCutAndBooking()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            var a = Add(w, 10, 50, 3000, 0);
            Add(w, 11, 50, 4500, 0);
            long m = p.Money;
            p.BookSalaries();
            Assert.Equal(m - 7500 / 30, p.Money);
            w.Players[1].BookSalaries(); // computers pay nothing
            p.ChangeSalary(a, true);
            Assert.Equal(3300, a.Salary);
            Assert.Equal(120, a.Happiness);
            p.ChangeSalary(a, false);
            Assert.Equal(3300 - 330, a.Salary);
            Assert.Equal(95, a.Happiness);
            a.Happiness = -80;
            p.ChangeSalary(a, false); // −25 → below −100: quits
            Assert.Equal(WorkerType.Reserve, a.Employer);
            Assert.Equal(3000, a.Salary);
            Assert.Equal(100, a.Happiness);
        }

        [Fact]
        public void AdvisorQualityAndFitness()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            Assert.Equal(0, p.AdvisorQuality(AdvisorType.Finance));
            Add(w, 5, 60, 9000, 0);
            Add(w, 5, 80, 9000, 0);
            Assert.Equal(80, p.AdvisorQuality(AdvisorType.Finance));
            Add(w, 8, 90, 9000, 0);
            Assert.Equal(5, p.FitnessWalkSpeed());
            // Finance perk: Normal mission base rate 6 → (6 + 4)/2.
            w.Difficulty = Mission.ConnectWorld;
            p.LoanRate = 6; p.SavingsRate = 4;
            Assert.True(p.ApplyFinanceAdvisorPerk());
            Assert.Equal(5, p.LoanRate);
            Assert.False(p.ApplyFinanceAdvisorPerk());
        }

        [Fact]
        public void Bodyguard_Rebate()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            Add(w, 9, 55, 9000, 0);
            long m = p.Money;
            Assert.Equal(1000, p.BodyguardRebate(20000)); // 20000/100·5
            Assert.Equal(m + 1000, p.Money);
        }

        static World PoolWorld(int size)
        {
            var w = FinanceFixture.Make();
            w.Workers.Workers.Clear();
            for (int i = 0; i < size; i++)
                w.Workers.Workers.Add(new Worker { Name = "N" + i, Type = i % 3 == 0 ? 10 : i % 3 == 1 ? 11 : 1 + i % 9, Talent = 70, Salary = 5000, OriginalSalary = 5000 });
            w.Workers.LastNames.Add("X");
            return w;
        }

        [Fact]
        public void LabourMarket_ApplicantsAreDeterministic()
        {
            var w1 = PoolWorld(150);
            var w2 = PoolWorld(150);
            w1.Clock.Date = w2.Clock.Date = 4;
            w1.Workers.NewDay(w1);
            w2.Workers.NewDay(w2);
            int jobless = 0, attendants = 0;
            for (int i = 0; i < w1.Workers.Count; i++)
            {
                Assert.Equal(w1.Workers[i].Employer, w2.Workers[i].Employer);
                if (w1.Workers[i].Employer == WorkerType.Jobless) { jobless++; if (w1.Workers[i].IsAttendant) attendants++; }
            }
            var r = new TeakRand(4);
            int expectedAny = 10 + r.Rand(10);
            Assert.InRange(jobless, expectedAny + 5, expectedAny + 6);
            Assert.True(attendants >= 5);
            // ≥ 40 talented pilots and attendants free → no top-up
            Assert.Equal(150, w1.Workers.Count);
        }

        [Fact]
        public void LabourMarket_ShortageTopUpAndUnhappiness()
        {
            var w = PoolWorld(10);
            var p = w.Players[0];
            p.Image = -600; // 3 steps
            var wk = w.Workers[0];
            wk.Employer = 0;
            wk.Salary = 4000; // below original → −20 per step
            wk.Happiness = 0;
            w.Workers.NewDay(w);
            Assert.Equal(10 + 20, w.Workers.Count); // +10 pilots +10 attendants
            Assert.Equal(-63, wk.Happiness);
            var gen = w.Workers[10];
            Assert.Equal(WorkerType.Pilot, gen.Type);
            Assert.InRange(gen.Salary, 3000, 9900);
            Assert.Equal(0, gen.Salary % 100);
            Assert.InRange(gen.Talent - gen.Salary / 200, 20, 49);
        }

        [Fact]
        public void Strike_PlannedAndStartedAtTen()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, StandingAtCity = true, NextLandingHour = -1 });
            var w = FinanceFixture.Make(bridge: bridge);
            var p = w.Players[0];
            w.Workers.Workers.Add(new Worker { Type = 10, Employer = 0, Happiness = 10, Salary = 1, OriginalSalary = 1 });
            p.DaysWithoutStrike = 8;
            w.Clock.Date = 6;
            w.Clock.Time = GameClock.At(10);
            new StrikeHourSystem().OnMinute(w);
            Assert.False(p.StrikePlanned);
            Assert.Equal(4 + new TeakRand(6 + 10).Rand(20), p.StrikeHours);
            Assert.Equal(0, p.DaysWithoutStrike);
            Assert.Equal(2090, w.Newspapers.Overrides[10].TextId);
            int h = p.StrikeHours;
            w.Clock.Time = GameClock.At(11);
            new StrikeHourSystem().OnMinute(w);
            Assert.Equal(h - 1, p.StrikeHours);
            // Threat: −20 happiness, ends after 4 game minutes.
            Assert.True(w.Execute(new EndStrikeCommand(0, StrikeEndOption.Threaten)).Ok);
            Assert.Equal(-10, w.Workers[w.Workers.Count - 1].Happiness);
            for (int i = 0; i < 4; i++) new StrikeCountdownSystem().OnMinute(w);
            Assert.False(p.StrikeRunning);
            Assert.Equal(1, p.StrikeEndType);
        }

        [Fact]
        public void Strike_LongWhenVeryUnhappy()
        {
            var bridge = new ListOperationsBridge();
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, NextLandingHour = 9, NextLandingDate = 2 });
            var w = FinanceFixture.Make(bridge: bridge);
            var p = w.Players[0];
            w.Workers.Workers.Add(new Worker { Type = 10, Employer = 0, Happiness = -50, Salary = 1, OriginalSalary = 1 });
            p.StrikePlanned = true;
            p.DaysWithoutStrike = 5;
            w.Clock.Date = 2;
            w.Clock.Time = GameClock.At(7);
            new StrikeHourSystem().OnMinute(w);
            Assert.Equal(72, p.StrikeHours); // lands at 9 = hour + 2
        }

        [Fact]
        public void Pool_LoadsFromInstall()
        {
            var install = GameInstall.TryLocate();
            if (install == null) return;
            var d = new GameData();
            FinanceData.Load(d, install);
            var t = d.Workers();
            Assert.True(t.Rows.Count > 300);
            Assert.True(t.LastNames.Count > 50);
            Assert.Contains(t.Rows, r => r.Type == WorkerType.Pilot);
            Assert.True(d.Headlines().Rows.Count > 300);
            Assert.True(d.Headlines().FillerCount[0] > 0);
        }
    }

    public class FinanceMissionTests
    {
        [Fact]
        public void HasWon_BasicTargets()
        {
            var w = FinanceFixture.Make(Mission.Tutorial);
            var rule = w.Rules.Get<IMissionRule>();
            var p = w.Players[0];
            p.Counters.Orders = 9;
            Assert.False(rule.HasWon(w, p));
            p.Counters.Orders = 10;
            Assert.True(rule.HasWon(w, p));

            w.Difficulty = Mission.Passengers;
            p.Counters.Passengers = 2500;
            Assert.True(rule.HasWon(w, p));

            w.Difficulty = Mission.Image;
            p.Image = 749;
            Assert.False(rule.HasWon(w, p));
            p.Image = 750;
            Assert.True(rule.HasWon(w, p));
            Assert.Equal(75, rule.Rating(w, p));

            w.Difficulty = 11; // debt: credit + overdraft = 0
            p.Money = 100; p.Credit = 0;
            Assert.True(rule.HasWon(w, p));
            p.Money = -1;
            Assert.False(rule.HasWon(w, p));
            Assert.Equal(1, rule.Rating(w, p));

            w.Difficulty = 18;
            p.SharePrice = 220;
            Assert.True(rule.HasWon(w, p));
        }

        [Fact]
        public void HasWon_HighestAtDayAndFleetGoals()
        {
            var bridge = new ListOperationsBridge();
            var w = FinanceFixture.Make(16, bridge: bridge);
            var rule = w.Rules.Get<IMissionRule>();
            w.Players[0].Statistics.Set(StatId.CompanyValue, 5000);
            w.Players[1].Statistics.Set(StatId.CompanyValue, 5000);
            w.Players[2].Statistics.Set(StatId.CompanyValue, 100);
            Assert.True(rule.HasWon(w, w.Players[0])); // ties all win
            Assert.True(rule.HasWon(w, w.Players[1]));
            Assert.False(rule.HasWon(w, w.Players[2]));

            w.Difficulty = 17;
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 1, Condition = 95 });
            Assert.False(rule.HasWon(w, w.Players[0])); // needs 2 planes
            bridge.PlaneLists[0].Add(new PlaneSnapshot { Id = 2, Condition = 89 });
            Assert.False(rule.HasWon(w, w.Players[0]));
            bridge.PlaneLists[0][1] = new PlaneSnapshot { Id = 2, Condition = 90 };
            Assert.True(rule.HasWon(w, w.Players[0]));
            Assert.Equal(92, rule.Rating(w, w.Players[0]));
        }

        [Fact]
        public void MorningReview_GameOverThresholds()
        {
            var w = FinanceFixture.Make();
            var events = new List<ISimEvent>();
            w.Events.SubscribeAll(events.Add);
            w.Clock.Date = 3;
            w.Players[1].Money = -5000000; // not below
            w.Players[2].Money = -5000001;
            w.Players[3].Image = -990;     // not below
            new MorningReviewSystem().OnMorning(w);
            Assert.False(w.Players[1].IsOut);
            Assert.True(w.Players[2].IsOut);
            Assert.False(w.Players[3].IsOut);
            Assert.Contains(events, e => e is PlayerOut o && o.Player.Index == 2 && o.Reason == OutReason.Bankrupt);
            Assert.False(w.GameEnded);

            w.Players[0].Image = -991;
            w.Players[0].Money = -9000000; // image is checked first
            new MorningReviewSystem().OnMorning(w);
            Assert.True(w.GameEnded);
            Assert.Contains(events, e => e is GameOver g && g.Player.Index == 0 && g.Reason == OutReason.Image);
        }

        [Fact]
        public void MorningReview_OutAirlineHoldingsZeroed()
        {
            var w = FinanceFixture.Make();
            w.Clock.Date = 1;
            w.Players[0].SharesOwned[1] = 500;
            w.Players[1].SharesOwned[0] = 300;
            w.Players[1].Money = -6000000;
            new MorningReviewSystem().OnMorning(w);
            Assert.True(w.Players[1].IsOut);
            Assert.Equal(0, w.Players[0].SharesOwned[1]);
            Assert.Equal(0, w.Players[1].SharesOwned[0]);
        }

        [Fact]
        public void MissionReport_LeaderWinEndsTheGame()
        {
            var w = FinanceFixture.Make(Mission.Passengers);
            var events = new List<ISimEvent>();
            w.Events.SubscribeAll(events.Add);
            w.Clock.Date = 5;
            w.Players[2].Counters.Passengers = 3000;
            w.Players[0].Counters.Passengers = 2600;
            new MorningReviewSystem().OnMorning(w);
            Assert.True(w.GameEnded);
            var report = (MissionReport)events.Find(e => e is MissionReport);
            Assert.Equal(2, report.Leader);
            Assert.Equal(2, report.Winners.Count);
            Assert.Contains(events, e => e is MissionWon m && m.Player.Index == 0);
            Assert.DoesNotContain(events, e => e is GameOver);
        }

        [Fact]
        public void MissionReport_DayLimitedAndFreeGame()
        {
            var w = FinanceFixture.Make(13);
            w.Clock.Date = 20;
            new MorningReviewSystem().OnMorning(w);
            Assert.False(w.GameEnded);
            w.Clock.Date = 21;
            w.Players[1].Counters.ReliefFreight = 10;
            var events = new List<ISimEvent>();
            w.Events.SubscribeAll(events.Add);
            new MorningReviewSystem().OnMorning(w);
            Assert.True(w.GameEnded);
            Assert.Contains(events, e => e is GameOver g && g.Player.Index == 0 && g.Reason == null);

            var free = FinanceFixture.Make();
            free.Clock.Date = 50;
            free.Players[0].Counters.Passengers = 1000000;
            new MorningReviewSystem().OnMorning(free);
            Assert.False(free.GameEnded);
        }

        [Fact]
        public void AiBonus_TopUpWhenHumanLeads()
        {
            var w = FinanceFixture.Make(Mission.Passengers);
            var ai = w.Players[1];
            ai.Bonus = 0;
            w.Players[0].Counters.Passengers = 1000;
            ai.Counters.Passengers = 400; // 1000 > 400, 666 > 400, 500 > 400
            new AiBonusSystem().OnPlayerNewDay(w, ai);
            Assert.Equal(300000, ai.Bonus); // mission 1: no multiplier
            var w2 = FinanceFixture.Make(Mission.Profit);
            w2.Players[1].Bonus = 0;
            w2.Players[0].Profit = 100;
            new AiBonusSystem().OnPlayerNewDay(w2, w2.Players[1]);
            Assert.Equal(600000, w2.Players[1].Bonus); // ×2 in mission 2
        }
    }

    public class FinanceNewspaperTests
    {
        static HeadlineTable Table()
        {
            var t = new HeadlineTable();
            t.Rows.Add(new HeadlineRow { Paper = 1, Chain = 0, Text = "Filler A in {city}" });
            t.Rows.Add(new HeadlineRow { Paper = 1, Chain = 0, Text = "Filler B {rand(1,9)} {{rand}}" });
            t.Rows.Add(new HeadlineRow { Paper = 2, Chain = 0, Text = "Two {2}" });
            t.Rows.Add(new HeadlineRow { Paper = 3, Chain = 0, Text = "Three" });
            t.FillerCount[0] = 2; t.FillerCount[1] = 1; t.FillerCount[2] = 1;
            return t;
        }

        [Fact]
        public void Reload_PicksFillersAndSubstitutesTokens()
        {
            var w = FinanceFixture.Make();
            w.Data.Cities.Register(new City { Id = City.IdBase + 1, Name = "Metropolis" });
            var n = w.Newspapers;
            n.AddOverride(1, 2090, "STREIK", 0, 25, "Air0");
            n.Reload(w, Table());
            n.Interpolate(w);
            n.SortByPriority();
            Assert.Equal("Two Air1", n.Get(1, 1).Text);
            Assert.Equal("Three", n.Get(2, 0).Text);
            string p1 = n.Get(0, 0).Text;
            Assert.True(p1 == "Filler A in Metropolis" || p1.StartsWith("Filler B "));
            Assert.True(n.IsInteresting);
            // The override (priority 25) is sorted before the filler (priority 0).
            Assert.Equal(2090, n.Get(1, 0).TextId);
        }

        [Fact]
        public void Comparison_RichestOnDayTen()
        {
            var w = FinanceFixture.Make();
            for (int d = 9; d < 40; d++)
            {
                if (d % 4 != 2) continue;
                if (new TeakRand(d).Rand(8) != 3) continue;
                w.Clock.Date = d;
                w.Players[0].Money = 1; w.Players[1].Money = 2; w.Players[2].Money = 30000000; // no tie (the scan stops at a tie)
                w.Newspapers.Comparison(w);
                Assert.Equal(2053, w.Newspapers.Overrides[10].TextId);
                Assert.Equal(200, w.Newspapers.Overrides[10].PictureOffset);
                return;
            }
        }
    }
}
