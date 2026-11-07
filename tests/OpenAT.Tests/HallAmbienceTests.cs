using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Missions;
using OpenAT.Formats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Half-hourly airport notices (rooms-and-ui-3 §5.6, airport-and-people.md §10) and the phone-call state of the hall
    /// figures (airport-and-people.md §6.2, the <see cref="GameOptions.ComputerPhonePose"/> addition).
    /// </summary>
    public class HallAmbienceTests
    {
        static GameClock Clock(int hour, int minute, int date = 3, long start = 1000)
        {
            var c = new GameClock { Date = date, StartTime = start };
            c.Time = GameClock.At(hour, minute);
            return c;
        }

        [Theory]
        [InlineData(10, 30, true)]
        [InlineData(12, 30, true)]
        [InlineData(14, 30, true)]
        [InlineData(16, 30, true)]
        [InlineData(9, 30, false)]
        [InlineData(11, 30, false)]
        [InlineData(10, 29, false)]
        [InlineData(10, 31, false)]
        [InlineData(8, 30, false)]
        [InlineData(18, 30, false)]
        public void NoticeIsDueAtHalfPastEvenHours(int hour, int minute, bool due) =>
            Assert.Equal(due, HallAnnouncements.NoticeDue(Clock(hour, minute)));

        [Fact]
        public void NoticeSampleFollowsTheLocalGenerator()
        {
            int vehicle = 0;
            for (int date = 0; date < 200; date++)
                foreach (int hour in new[] { 10, 12, 14, 16 })
                {
                    var c = Clock(hour, 30, date, 77);
                    string s = HallAnnouncements.NoticeSample(c);
                    bool rolledVehicle = new TeakRand((int)(date + 77 + hour)).Rand(10) == 0;
                    string expected = rolledVehicle ? HallAnnouncements.Vehicle : ((hour / 2) & 1) == 0 ? HallAnnouncements.Zone : HallAnnouncements.Notice;
                    Assert.Equal(expected, s);
                    // Same answer for the whole minute.
                    var later = Clock(hour, 30, date, 77);
                    later.Time += 900;
                    Assert.Equal(s, HallAnnouncements.NoticeSample(later));
                    if (rolledVehicle) vehicle++;
                }
            // Roughly one in ten.
            Assert.InRange(vehicle, 40, 130);
        }

        static GameData _data;
        static HallData _hall;

        static bool Load()
        {
            if (_data != null) return true;
            var install = GameInstall.TryLocate();
            if (install == null) return false;
            _hall = HallData.Load(install);
            _data = GameData.Load(install);
            HallModule.Register();
            BriefingRecorder.Register();
            HallContent.Attach(_data, _hall);
            return true;
        }

        static World NewWorld(bool phonePose)
        {
            var w = World.NewGame(_data, new NewGameSettings { Seed = 4242, Difficulty = Mission.FreeGame }, new GameOptions { ComputerPhonePose = phonePose });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            Rooms1HallWalkTests.StayInHall(w);
            for (int i = 0; i < 400; i++) w.Tick();
            return w;
        }

        [Fact]
        public void PhoneCallRecordsBothPartnersAndHangsUp()
        {
            if (!Load()) return;
            var w = NewWorld(false);
            Assert.True(w.Execute(new PhoneCallCommand(0, 2)).Ok);
            Assert.Equal(3, w.Hall.Players[0].PhonePartner);
            Assert.Equal(1, w.Hall.Players[2].PhonePartner);
            Assert.True(w.Execute(new PhoneCallCommand(0, -1)).Ok);
            Assert.Equal(0, w.Hall.Players[0].PhonePartner);
            Assert.Equal(0, w.Hall.Players[2].PhonePartner);
            Assert.False(w.Execute(new PhoneCallCommand(0, 0)).Ok);
        }

        /// <summary>With the addition on, a computer figure on the phone with the human does not move.</summary>
        [Fact]
        public void ComputerFigureHoldsStillOnThePhoneOnlyWithTheAddition()
        {
            if (!Load()) return;
            foreach (bool option in new[] { false, true })
            {
                var w = NewWorld(option);
                int moved = 0;
                for (int c = 1; c < Player.Count; c++)
                {
                    Assert.True(w.Execute(new PhoneCallCommand(0, c)).Ok);
                    var fig = w.Hall.PlayerPerson(c);
                    var before = fig.Position;
                    for (int i = 0; i < 300; i++) w.Tick();
                    if (w.Hall.PlayerPerson(c).Position != before) moved++;
                    Assert.True(w.Execute(new PhoneCallCommand(0, -1)).Ok);
                }
                if (option) Assert.Equal(0, moved);
                else Assert.True(moved > 0, "computer figures walk when the addition is off");
            }
        }
    }
}
