using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Mail;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Rivals;
using OpenAT.Sim.Save;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// Sim parts of the save/load UI work: the in-tray (Sim/Mail), the recorded room stack, campaign
    /// progress (sim-core.md §3.4, rooms-and-ui-3b §3 unlock table, rooms-and-ui-4 §6.2 outros) and the
    /// rival talk effects (rooms-and-ui-3b §19).
    /// </summary>
    public class SaveUiTests
    {
        static World NewWorld()
        {
            var w = new World(new GameData());
            for (int c = 0; c < Player.Count; c++) { w.Players[c].Owner = c == 0 ? Owner.Human : Owner.Computer; w.Players[c].Airline = "AIR" + c; }
            w.LocalPlayer = 0;
            return w;
        }

        static World RoundTrip(World w)
        {
            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "TEST");
            ms.Position = 0;
            var w2 = SaveGame.Read(ms, w.Data, out var warnings);
            Assert.Empty(warnings);
            return w2;
        }

        // ------------------------------------------------------------------ mail

        [Fact]
        public void LettersAreCollectedFromTheStartForHumansOnly()
        {
            var w = NewWorld();
            w.Events.Publish(new LetterSent(w.Players[0], "Lett", 1000, "Berlin", "Paris", 25000L));
            w.Events.Publish(new LetterSent(w.Players[1], "Lett", 1000, "Rome", "Oslo", 1L));
            Assert.Equal(1, w.Players[0].Mail.Count);
            Assert.Equal(0, w.Players[1].Mail.Count);
            var l = w.Players[0].Mail.Letters[0];
            Assert.Equal("Lett", l.Group);
            Assert.Equal(1000, l.TextId);
            Assert.Equal(new object[] { "Berlin", "Paris", 25000L }, l.Args);
        }

        [Fact]
        public void MailboxIsNewestFirstAndHolds25()
        {
            var w = NewWorld();
            for (int i = 0; i < 30; i++) w.Events.Publish(new LetterSent(w.Players[0], "Lett", 2000 + i));
            var m = w.Players[0].Mail;
            Assert.Equal(Mailbox.Capacity, m.Count);
            Assert.Equal(2029, m.Letters[0].TextId);
            Assert.Equal(2005, m.Letters[m.Count - 1].TextId);
        }

        [Fact]
        public void DeleteLetterCommandRemovesOne()
        {
            var w = NewWorld();
            w.Events.Publish(new LetterSent(w.Players[0], "Lett", 1));
            w.Events.Publish(new LetterSent(w.Players[0], "Lett", 2));
            Assert.False(w.Execute(new DeleteLetterCommand(0, 5)).Ok);
            Assert.True(w.Execute(new DeleteLetterCommand(0, 0)).Ok);
            Assert.Single(w.Players[0].Mail.Letters);
            Assert.Equal(1, w.Players[0].Mail.Letters[0].TextId);
        }

        [Fact]
        public void LettersAndRoomStackSurviveASave()
        {
            var w = NewWorld();
            w.Events.Publish(new LetterSent(w.Players[0], "Lett", 3000, "Berlin", "Paris", 7, 10));
            Assert.True(w.Execute(new RecordRoomStackCommand(0, new[] { 10, 11 }, 950, -10)).Ok);
            Assert.False(w.Execute(new RecordRoomStackCommand(0, new int[11])).Ok);

            var w2 = RoundTrip(w);
            var l = Assert.Single(w2.Players[0].Mail.Letters);
            Assert.Equal(3000, l.TextId);
            Assert.Equal(new object[] { "Berlin", "Paris", 7, 10 }, l.Args);
            Assert.Equal(new[] { 10, 11 }, w2.Players[0].RoomStack);
            Assert.Equal(950, w2.Players[0].CameraX);
            Assert.Equal(-10, w2.Players[0].CameraY);
            // The loaded world keeps collecting letters (the subscription belongs to the fresh world).
            w2.Events.Publish(new LetterSent(w2.Players[0], "Lett", 3100));
            Assert.Equal(2, w2.Players[0].Mail.Count);
            Assert.Equal(3100, w2.Players[0].Mail.Letters[0].TextId);
        }

        // ------------------------------------------------------------------ campaigns

        [Theory]
        [InlineData(0, 1)]
        [InlineData(4, 5)]
        [InlineData(5, Mission.FreeGame)]     // rocket: last base mission
        [InlineData(11, 12)]
        [InlineData(19, 20)]
        [InlineData(20, Mission.FreeGame)]    // space station: last add-on mission
        [InlineData(41, 42)]
        [InlineData(49, 50)]
        [InlineData(50, Mission.FreeGame)]
        [InlineData(Mission.FreeGame, Mission.FreeGame)]
        public void WinUnlocksTheNextMissionOfTheCampaign(int won, int unlocked) => Assert.Equal(unlocked, MissionProgress.UnlockedByWin(won));

        [Fact]
        public void StoredMaximumNeverDrops()
        {
            Assert.Equal(3, MissionProgress.MaxAfterWin(1, 2));
            Assert.Equal(4, MissionProgress.MaxAfterWin(4, 1));
            Assert.Equal(5, MissionProgress.MaxAfterWin(5, 5));
            Assert.Equal(Mission.AddOnFirst, MissionProgress.DefaultMax(Campaign.AddOn));
            Assert.True(MissionProgress.IsLocked(13, 12));
            Assert.False(MissionProgress.IsLocked(12, 12));
            Assert.False(MissionProgress.IsLocked(Mission.FreeGame, 0));
        }

        [Fact]
        public void OutrosForTheSpaceFinals()
        {
            Assert.Equal("intro/outro.smk", MissionProgress.OutroAfterWin(Mission.Rocket));
            Assert.Equal("intro/outro2.smk", MissionProgress.OutroAfterWin(Mission.AddOnLast));
            Assert.Null(MissionProgress.OutroAfterWin(Mission.AddOnLast, rightClick: true));   // quirk
            Assert.Equal("intro/outro.smk", MissionProgress.OutroAfterWin(Mission.Rocket, rightClick: true));
            Assert.Null(MissionProgress.OutroAfterWin(Mission.AtfsLast));
            Assert.Null(MissionProgress.OutroAfterWin(4));
            Assert.Equal(1, MissionProgress.NumberInCampaign(0));
            Assert.Equal(10, MissionProgress.NumberInCampaign(20));
            Assert.Equal(3, MissionProgress.NumberInCampaign(43));
        }

        // ------------------------------------------------------------------ rival talk

        [Fact]
        public void RivalTalkChangesSympathyAndCooperation()
        {
            var w = NewWorld();
            Assert.True(w.Execute(new RivalTalkCommand(1, 0, 30, true)).Ok);
            Assert.Equal(80, w.Players[1].Sympathy[0]);
            Assert.True(w.Players[1].Cooperates[0]);
            Assert.True(w.Players[0].Cooperates[1]);
            Assert.True(w.Execute(new RivalTalkCommand(1, 0, -5000, false)).Ok);
            Assert.Equal(-RivalTalkCommand.SympathyLimit, w.Players[1].Sympathy[0]);
            Assert.False(w.Players[1].Cooperates[0]);
            Assert.False(w.Players[0].Cooperates[1]);
            Assert.False(w.Execute(new RivalTalkCommand(0, 0, 1)).Ok);
        }
    }
}
