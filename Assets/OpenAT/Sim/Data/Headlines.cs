using System.Collections.Generic;
using OpenAT.Formats;

namespace OpenAT.Sim.Data
{
    /// <summary>One row of data/stdpaper.csv (data-tables.md §1.9).</summary>
    public sealed class HeadlineRow
    {
        /// <summary>Newspaper 1..3 (0 = invalid row; it still counts for the chain-head test).</summary>
        public int Paper;
        /// <summary>0 = standalone filler, else a story-chain step number.</summary>
        public int Chain;
        /// <summary>Headline text with tokens (original text, read at run time); null if the row has none.</summary>
        public string Text;
    }

    /// <summary>The headline table in file order plus the number of fillers per paper.</summary>
    public sealed class HeadlineTable
    {
        public readonly List<HeadlineRow> Rows = new List<HeadlineRow>();
        /// <summary>Number of filler rows (chain 0) per paper 0..2 (original <c>NumRand</c>).</summary>
        public readonly int[] FillerCount = new int[3];

        public void Load(CsvTable t)
        {
            Rows.Clear();
            for (int i = 0; i < 3; i++) FillerCount[i] = 0;
            foreach (var r in t.Rows)
            {
                var row = new HeadlineRow
                {
                    Paper = r.Length > 0 ? CsvTable.Atoi(r[0]) : 0,
                    Chain = r.Length > 1 ? CsvTable.Atoi(r[1]) : 0,
                    Text = r.Length > 2 ? r[2] : null,
                };
                if (row.Paper < 0 || row.Paper > 3) row.Paper = 0;
                Rows.Add(row);
                if (row.Paper != 0 && row.Chain == 0) FillerCount[row.Paper - 1]++;
            }
        }
    }
}
