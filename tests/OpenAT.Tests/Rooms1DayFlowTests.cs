using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Missions;
using Xunit;

namespace OpenAT.Tests
{
    public class Rooms1DayFlowTests
    {
        static World Make(int difficulty = Mission.FreeGame)
        {
            var w = FinanceFixture.Make(difficulty);
            BriefingRecorder.Ensure(w);
            w.Clock.State = DayState.Morning;
            w.Clock.Frozen = true;
            return w;
        }

        static void RunUntilMorning(World w)
        {
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
        }

        [Fact]
        public void StartWorkingDay_OnlyInTheMorning()
        {
            var w = Make();
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.Equal(DayState.Playing, w.Clock.State);
            Assert.False(w.Clock.Frozen);
            Assert.False(w.Execute(new StartWorkingDayCommand()).Ok);
        }

        [Fact]
        public void CallItADay_EndsTheDayWhenAllHumansCalled()
        {
            var w = Make();
            Assert.False(w.Execute(new CallItADayCommand(0)).Ok); // briefing still open
            w.Execute(new StartWorkingDayCommand());
            w.Players[1].Owner = Owner.Human;
            for (int i = 0; i < 10; i++) w.Tick();
            Assert.True(w.Execute(new CallItADayCommand(0)).Ok);
            Assert.False(w.CallItADay);
            w.Execute(new CallItADayCommand(1));
            Assert.True(w.CallItADay);
            w.Tick();
            Assert.Equal(DayState.Night, w.Clock.State);
        }

        [Fact]
        public void FullDayLoop_EveningNightNextBriefing()
        {
            var w = Make();
            w.Execute(new StartWorkingDayCommand());
            int evenings = 0, mornings = 0;
            w.Events.Subscribe<Sim.Systems.EveningStarted>(_ => evenings++);
            w.Events.Subscribe<Sim.Systems.MorningStarted>(_ => mornings++);
            RunUntilMorning(w);
            Assert.Equal(1, evenings);
            Assert.Equal(1, mornings);
            Assert.Equal(1, w.Clock.Date);
            Assert.Equal(GameClock.DayStart, w.Clock.Time);
            var log = BriefingLog.Of(w);
            Assert.Equal(1, log.Date);
            // Free game: no mission report, one review per active airline.
            Assert.Null(log.Report);
            Assert.Equal(4, log.Reviews.Count);
            Assert.False(log.HasNews);
        }

        [Fact]
        public void BriefingLog_RecordsBankruptcyAndGameOver()
        {
            var w = Make();
            w.Execute(new StartWorkingDayCommand());
            w.Players[0].Money = Player.DebtGameOver - 1;
            RunUntilMorning(w);
            var log = BriefingLog.Of(w);
            Assert.Equal(OutReason.Bankrupt, log.OutReasonOf(0));
            Assert.NotNull(log.GameOver);
            Assert.True(log.HasNews);
            Assert.True(log.HasNewsFor(0));
            Assert.False(log.HasNewsFor(1));
            Assert.NotNull(log.ReviewOf(0));
        }

        [Fact]
        public void BriefingLog_MissionReportEndsTutorialWithWinner()
        {
            var w = Make(Mission.Tutorial);
            w.Execute(new StartWorkingDayCommand());
            w.Players[0].Counters.Orders = MissionTargets.TutorialOrders;
            RunUntilMorning(w);
            var log = BriefingLog.Of(w);
            Assert.NotNull(log.Report);
            Assert.True(log.Report.Ended);
            Assert.Equal(0, log.Report.Leader);
            Assert.True(log.Won(0));
            Assert.Null(log.GameOver);
            Assert.True(w.GameEnded);
        }
    }
}
