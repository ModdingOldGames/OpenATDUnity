using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Stats;
using Xunit;

namespace OpenAT.Tests
{
    public class Rooms1StatisticsTests
    {
        [Fact]
        public void Groups_FitTheSixteenLabelRows()
        {
            foreach (var g in StatisticsScreen.Groups) Assert.True(g.Length <= StatisticsScreen.MaxRows);
            Assert.Equal(StatRowKind.SumDiff, StatisticsScreen.Groups[0][15].Kind);
        }

        [Fact]
        public void Table_SumsOnlyTickedRowsAndDifference()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.Statistics.Clear();
            p.Statistics.Set(StatId.IncomeRoutes, 1000);
            p.Statistics.Set(StatId.IncomeOrders, 500);
            p.Statistics.Set(StatId.IncomeOther, 7);
            p.Statistics.Set(StatId.ExpenseRents, -300);
            p.Statistics.Set(StatId.Salaries, -200);
            var st = new StatisticsScreenState();
            var t = st.Ticked[0];
            t[3] = false; // other income not counted
            var v = StatisticsScreen.TableValues(w, p, 0, t);
            Assert.Null(v[0]);
            Assert.Equal(1000, v[1]);
            Assert.Equal(7, v[3]);           // shown although unticked
            Assert.Equal(1500, v[4]);        // income sum
            Assert.Equal(-500, v[13]);       // expense sum
            Assert.Equal(1000, v[15]);       // balance
        }

        [Fact]
        public void Table_PercentOfRowAboveAndShareValue()
        {
            var w = FinanceFixture.Make();
            var p = w.Players[0];
            p.Statistics.Clear();
            p.Statistics.Set(StatId.Passengers, 200);
            p.Statistics.Set(StatId.SatisfiedPassengers, 50);
            var st = new StatisticsScreenState();
            var v = StatisticsScreen.TableValues(w, p, 2, st.Ticked[2]);
            Assert.Equal(25, v[13]);
            Assert.Equal(0, v[15]); // no employees: 0 %

            p.Statistics.Set(StatId.SharesHeldBase + 1, 100);
            w.Players[1].Statistics.Set(StatId.SharePrice, 12);   // the value uses each airline's recorded share price of the day
            var s1 = StatisticsScreen.TableValues(w, p, 1, st.Ticked[1]);
            Assert.Equal(1200, s1[7]);
            st.Ticked[1][2] = false; // share price row unticked: no share value
            Assert.Equal(0, StatisticsScreen.TableValues(w, p, 1, st.Ticked[1])[7]);
        }

        [Fact]
        public void Labels_ToggleHeadingAndSolo()
        {
            var st = new StatisticsScreenState();
            st.Click(0, false); // income heading toggles rows 1..5
            Assert.False(st.Ticked[0][1]);
            Assert.False(st.Ticked[0][3]);
            Assert.True(st.Ticked[0][7]);
            st.Click(8, true); // solo salaries within the expense block
            Assert.True(st.Ticked[0][8]);
            Assert.False(st.Ticked[0][7]);
            Assert.False(st.Ticked[0][12]);
            Assert.False(st.Ticked[0][1]); // other block untouched
        }

        [Fact]
        public void Periods_Step()
        {
            Assert.Equal(14, StatisticsScreen.StepPeriod(7, 1));
            Assert.Equal(7, StatisticsScreen.StepPeriod(7, -1));
            Assert.Equal(360, StatisticsScreen.StepPeriod(360, 1));
        }
    }
}
