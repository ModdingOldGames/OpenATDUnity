using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenAT.Formats;

namespace OpenAT.Sim.Data
{
    /// <summary>One row of data/piloten.csv (data-tables.md §1.6).</summary>
    public sealed class WorkerRow
    {
        public string Name;
        /// <summary>0 female, 1 male.</summary>
        public int Sex;
        /// <summary>1..9 advisor kinds, 10 pilot, 11 flight attendant (economy.md §9.1).</summary>
        public int Type;
        /// <summary>Monthly salary.</summary>
        public int Salary;
        public int Talent;
        public int Age;
        /// <summary>CV text (original text, read at run time).</summary>
        public string Comment;
    }

    /// <summary>
    /// Staff pool template and random-name lists (data-tables.md §1.6–1.7): piloten.csv rows in file
    /// order and the female / male / last name lists of Names.csv (capped at 40/40/80).
    /// </summary>
    public sealed class WorkerTables
    {
        public const int MaxWorkers = 350;
        public readonly List<WorkerRow> Rows = new List<WorkerRow>();
        public readonly List<string> FemaleNames = new List<string>();
        public readonly List<string> MaleNames = new List<string>();
        public readonly List<string> LastNames = new List<string>();

        /// <summary>piloten.csv: name, sex, type, salary, talent, age, comment (rest of line, one pair of quotes stripped).</summary>
        public void LoadWorkers(CsvTable t)
        {
            Rows.Clear();
            for (int i = 0; i < t.Rows.Count && Rows.Count < MaxWorkers; i++)
            {
                var r = t.Rows[i];
                if (r.Length < 6) continue;
                string comment = CsvTable.RestOfLine(t.Lines[i], 6);
                if (comment.Length > 0 && comment[0] == '"') comment = comment.Substring(1);
                if (comment.Length > 0 && comment[comment.Length - 1] == '"') comment = comment.Substring(0, comment.Length - 1);
                Rows.Add(new WorkerRow
                {
                    Name = r[0],
                    Sex = CsvTable.Atoi(r[1]),
                    Type = CsvTable.Atoi(r[2]),
                    Salary = CsvTable.Atoi(r[3]),
                    Talent = CsvTable.Atoi(r[4]),
                    Age = CsvTable.Atoi(r[5]),
                    Comment = comment,
                });
            }
        }

        /// <summary>Names.csv: Art;Name with Art 0 female first, 1 male first, 2 last name; other rows ignored.</summary>
        public void LoadNames(CsvTable t)
        {
            FemaleNames.Clear(); MaleNames.Clear(); LastNames.Clear();
            foreach (var r in t.Rows)
            {
                if (r.Length < 2) continue;
                int kind = CsvTable.Atoi(r[0]);
                if (kind == 0 && FemaleNames.Count < 40) FemaleNames.Add(r[1]);
                else if (kind == 1 && MaleNames.Count < 40) MaleNames.Add(r[1]);
                else if (kind == 2 && LastNames.Count < 80) LastNames.Add(r[1]);
            }
        }
    }

    /// <summary>
    /// Finance-domain tables attached to a <see cref="GameData"/> (GameData is not partial, so the
    /// tables are kept in a weak side table). Call <see cref="Load"/> after <see cref="GameData.Load"/>.
    /// </summary>
    public static class FinanceData
    {
        sealed class Tables
        {
            public WorkerTables Workers = new WorkerTables();
            public HeadlineTable Headlines = new HeadlineTable();
        }

        static readonly ConditionalWeakTable<GameData, Tables> _tables = new ConditionalWeakTable<GameData, Tables>();

        static Tables Of(GameData d) => _tables.GetValue(d, _ => new Tables());

        /// <summary>Staff pool template and names (empty until loaded).</summary>
        public static WorkerTables Workers(this GameData d) => Of(d).Workers;

        /// <summary>Newspaper headline table (empty until loaded).</summary>
        public static HeadlineTable Headlines(this GameData d) => Of(d).Headlines;

        /// <summary>Loads piloten.csv, Names.csv and stdpaper.csv from the install (missing files are skipped).</summary>
        public static void Load(GameData d, GameInstall install)
        {
            var t = Of(d);
            if (install.Exists("data/piloten.csv")) t.Workers.LoadWorkers(CsvTable.Parse(install.ReadAllBytes("data/piloten.csv")));
            if (install.Exists("data/names.csv")) t.Workers.LoadNames(CsvTable.Parse(install.ReadAllBytes("data/names.csv")));
            if (install.Exists("data/stdpaper.csv"))
            {
                var raw = install.ReadAllBytes("data/stdpaper.csv");
                t.Headlines.Load(CsvTable.Parse(raw));
            }
        }
    }
}
