using System.Collections.Generic;
using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Save;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Highscore table (sim-core.md §5.4, rooms-and-ui-4 §3.8).</summary>
    public class HighscoreTests
    {
        static long[] Scores(HighscoreTable t)
        {
            var r = new long[HighscoreTable.Size];
            for (int i = 0; i < r.Length; i++) r[i] = t[i].Score;
            return r;
        }

        [Fact]
        public void NewSessions_InsertedByRank_LastRowFallsOut()
        {
            var t = new HighscoreTable();
            Assert.True(t.Offer("A", 1, 500));
            Assert.True(t.Offer("B", 2, 900));
            Assert.True(t.Offer("C", 3, 700));
            Assert.Equal(new long[] { 900, 700, 500, 0, 0, 0 }, Scores(t));
            Assert.Equal("---", t[3].Name);
            for (uint s = 4; s <= 6; s++) t.Offer("X", s, 100 * s);
            Assert.True(t.Offer("D", 7, 650));
            Assert.Equal(new long[] { 900, 700, 650, 600, 500, 500 }, Scores(t));
            Assert.False(t.Offer("E", 8, 400));          // must beat a row, a tie is not enough
            Assert.False(t.Offer("F", 9, 0));
            Assert.False(t.Offer("G", 10, -5000));
        }

        [Fact]
        public void SameSession_OnlyRaisesItsOwnRow()
        {
            var t = new HighscoreTable();
            t.Offer("A", 1, 500);
            t.Offer("B", 2, 900);
            Assert.False(t.Offer("A", 1, 300));          // lower: kept
            Assert.Equal(500, t[1].Score);
            Assert.True(t.Offer("A NEW", 1, 1200));      // higher: raised, renamed, moved up
            Assert.Equal(new long[] { 1200, 900, 0, 0, 0, 0 }, Scores(t));
            Assert.Equal("A NEW", t[0].Name);
            Assert.Equal(1u, t[0].Session);
        }

        [Fact]
        public void TextRoundTrip_AndBadLines()
        {
            var t = new HighscoreTable();
            t.Offer("Ann;Lee", 77, 4595000);
            t.Offer("Bo", 12, 123);
            var back = HighscoreTable.Parse(t.Serialize());
            Assert.Equal(Scores(t), Scores(back));
            Assert.Equal("Ann Lee", back[0].Name);
            Assert.Equal(77u, back[0].Session);
            var junk = HighscoreTable.Parse("garbage\nX;1;50\nY;2;900\n");
            Assert.Equal(new long[] { 900, 50, 0, 0, 0, 0 }, Scores(junk));
        }

        [Fact]
        public void Statistics_OfferHumansOnly_NotWhenCheated_SessionKeptInSave()
        {
            var w = FinanceFixture.Make();
            var offers = new List<HighscoreOffered>();
            w.Events.Subscribe<HighscoreOffered>(offers.Add);
            w.UpdateStatistics();
            Assert.Single(offers);
            Assert.Same(w.Players[0], offers[0].Player);
            Assert.Equal(w.Players[0].Statistics.Today(StatId.CompanyValue), offers[0].Score);
            Assert.NotEqual(0u, offers[0].Session);
            Assert.Equal(w.SessionId, offers[0].Session);

            var ms = new MemoryStream();
            SaveGame.Write(w, ms, "T");
            ms.Position = 0;
            var w2 = SaveGame.Read(ms, w.Data, out _);
            Assert.Equal(w.SessionId, w2.SessionId);

            w.CheatedSession = true;
            offers.Clear();
            w.UpdateStatistics();
            Assert.Empty(offers);
        }
    }
}
