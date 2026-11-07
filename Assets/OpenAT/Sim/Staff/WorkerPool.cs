using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;

namespace OpenAT.Sim.Staff
{
    /// <summary>A worker quit (economy.md §9.5/§9.6). Secretary message <c>Advi 2000 + type + sex·100</c>.</summary>
    public sealed class WorkerQuit : ISimEvent
    {
        public readonly Player Player;
        public readonly Worker Worker;
        public WorkerQuit(Player p, Worker w) { Player = p; Worker = w; }
    }

    /// <summary>
    /// The world's staff pool (economy.md §9, sim-core.md §6.10, original <c>CWorkers</c>): every
    /// person in file order, the generated extra pilots/attendants appended, and the name lists.
    /// </summary>
    public sealed class WorkerPool
    {
        public readonly List<Worker> Workers = new List<Worker>();
        public readonly List<string> FemaleNames = new List<string>();
        public readonly List<string> MaleNames = new List<string>();
        public readonly List<string> LastNames = new List<string>();

        /// <summary>
        /// Stand-in for the original's unseeded C <c>rand()</c> (random names, attendant sex in the
        /// shortage top-up). Deterministic here; reseeded with Date + StartTime + 7919 per top-up.
        /// </summary>
        public readonly TeakRand NameRandom = new TeakRand();

        public int Count => Workers.Count;
        public Worker this[int i] => Workers[i];

        /// <summary>Loads the pool from the template table (original <c>ReInit</c>): all in reserve, happiness 100.</summary>
        public void Init(WorkerTables t)
        {
            Workers.Clear();
            FemaleNames.Clear(); MaleNames.Clear(); LastNames.Clear();
            if (t == null) return;
            foreach (var r in t.Rows)
                Workers.Add(new Worker
                {
                    Name = r.Name, Sex = r.Sex, Type = r.Type, Salary = r.Salary, OriginalSalary = r.Salary,
                    Talent = r.Talent, Age = r.Age, Comment = r.Comment,
                    Employer = WorkerType.Reserve, PlaneId = -1, Happiness = 100,
                });
            FemaleNames.AddRange(t.FemaleNames);
            MaleNames.AddRange(t.MaleNames);
            LastNames.AddRange(t.LastNames);
        }

        static int Seed(World w) => (int)(w.Clock.Date + w.Clock.StartTime);

        /// <summary>
        /// Daily labour market (economy.md §9.2): shortage top-up, unhappiness of human airlines'
        /// staff, jobless back to reserve, 10..19 random applicants plus 5..6 attendants.
        /// All draws seeded with Date + StartTime.
        /// </summary>
        public void NewDay(World w)
        {
            CheckShortage(w);
            var rand = new TeakRand(Seed(w));

            for (int c = 0; c < Workers.Count; c++)
            {
                var wk = Workers[c];
                wk.WarnedToday = false;
                if (!wk.IsEmployed) continue;
                var emp = w.Players[wk.Employer];
                int steps = 0;
                if (emp.Owner == Owner.Human && emp.Image < 500) steps = 1;
                if (steps > 0 && emp.Image < 0) steps++;
                if (steps > 0 && emp.Image < -500) steps++;
                for (; steps > 0; steps--)
                {
                    wk.Happiness--;
                    if (wk.Salary < wk.OriginalSalary) wk.Happiness -= 20;
                    if (wk.Happiness < -100)
                    {
                        // Quits: back to the reserve (salary and happiness are not reset here, as coded).
                        if (emp.Owner == Owner.Human) Quit(w, emp, wk);
                        wk.Employer = WorkerType.Reserve;
                        emp.MapWorkers(false);
                        break;
                    }
                }
            }

            foreach (var wk in Workers)
                if (wk.Employer == WorkerType.Jobless) wk.Employer = WorkerType.Reserve;

            PickApplicants(rand, 10 + rand.Rand(10), anyType: true);
            PickApplicants(rand, 5 + rand.Rand(2), anyType: false);
        }

        void PickApplicants(TeakRand rand, int wanted, bool anyType)
        {
            int n = Workers.Count;
            if (n == 0) return;
            for (int c = 0, got = 0; c < 100 && got < wanted; c++)
            {
                int m = rand.Rand(n);
                for (int d = 0; d < 100; d++)
                {
                    var cand = Workers[(m + d) % n];
                    if (cand.Employer == WorkerType.Reserve && (anyType || cand.Type == WorkerType.Attendant))
                    {
                        m = (m + d) % n;
                        break;
                    }
                }
                var wk = Workers[m];
                if (wk.Employer == WorkerType.Reserve && (anyType || wk.Type == WorkerType.Attendant))
                {
                    wk.Employer = WorkerType.Jobless;
                    wk.Salary = wk.OriginalSalary;
                    got++;
                }
            }
        }

        internal static void Quit(World w, Player emp, Worker wk)
        {
            w.Events.Publish(new AdvisorMessage(emp, AdvisorMessage.Secretary, 2000 + wk.Type + wk.Sex * 100, wk.Name));
            w.Events.Publish(new WorkerQuit(emp, wk));
        }

        /// <summary>
        /// Shortage top-up (economy.md §9.2 step 1): fewer than 40 unemployed pilots (resp. attendants)
        /// with talent &gt; 60 → 10 new ones are appended.
        /// </summary>
        public void CheckShortage(World w)
        {
            var rand = new TeakRand(Seed(w));
            NameRandom.SetSeed(Seed(w) + 7919);

            if (CountFreeTalented(WorkerType.Pilot) < 40)
                for (int i = 0; i < 10; i++)
                {
                    var wk = new Worker { Type = WorkerType.Pilot };
                    wk.Sex = rand.Rand(100) > 20 ? 1 : 0;
                    wk.Name = RandomName(wk.Sex == 1);
                    wk.Salary = (30 + rand.Rand(70)) * 100;
                    wk.Talent = wk.Salary / 200 + rand.Rand(30) + 20;
                    wk.Age = 19 + rand.Rand(50);
                    Finish(wk);
                }

            if (CountFreeTalented(WorkerType.Attendant) < 40)
                for (int i = 0; i < 10; i++)
                {
                    var wk = new Worker { Type = WorkerType.Attendant };
                    wk.Sex = NameRandom.Rand(100) > 80 ? 1 : 0;
                    wk.Name = RandomName(wk.Sex == 1);
                    wk.Salary = (30 + rand.Rand(50)) * 100;
                    wk.Talent = wk.Salary * 100 / 80 / 200 + rand.Rand(30) + 20;
                    wk.Age = 19 + rand.Rand(40);
                    Finish(wk);
                }
        }

        void Finish(Worker wk)
        {
            wk.Comment = "";
            wk.Employer = WorkerType.Reserve;
            wk.Happiness = 100;
            wk.WarnedToday = false;
            wk.OriginalSalary = wk.Salary;
            Workers.Add(wk);
        }

        int CountFreeTalented(int type)
        {
            int n = 0;
            foreach (var wk in Workers)
                if ((wk.Employer == WorkerType.Reserve || wk.Employer == WorkerType.Jobless) && wk.Type == type && wk.Talent > 60) n++;
            return n;
        }

        /// <summary>Random "first last" name (original <c>GetRandomName</c>, here from <see cref="NameRandom"/>).</summary>
        public string RandomName(bool male)
        {
            var first = male ? MaleNames : FemaleNames;
            string a = first.Count > 0 ? first[NameRandom.Rand(first.Count)] : "";
            string b = LastNames.Count > 0 ? LastNames[NameRandom.Rand(LastNames.Count)] : "";
            return (a + " " + b).Trim();
        }

        /// <summary>
        /// Guarantees an applicant of the given advisor type (economy.md §9.2 step 5, game start):
        /// if none is jobless, up to 1000 random draws (seed Date + StartTime) for a reserve one.
        /// </summary>
        public void EnsureAdvisor(World w, int type)
        {
            foreach (var wk in Workers) if (wk.Employer == WorkerType.Jobless && wk.Type == type) return;
            if (Workers.Count == 0) return;
            var rand = new TeakRand(Seed(w));
            for (int c = 0; c < 1000; c++)
            {
                var wk = Workers[rand.Rand(Workers.Count)];
                if (wk.Employer == WorkerType.Reserve && wk.Type == type)
                {
                    wk.Employer = WorkerType.Jobless;
                    wk.Salary = wk.OriginalSalary;
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ happiness helpers

        /// <summary>Average happiness of an airline's staff (integer); 100 if it has none.</summary>
        public int AverageHappiness(int player)
        {
            int n = 0, sum = 0;
            foreach (var wk in Workers) if (wk.Employer == player) { n++; sum += wk.Happiness; }
            return n > 0 ? sum / n : 100;
        }

        /// <summary>Lowest happiness; 100 if no staff.</summary>
        public int MinHappiness(int player)
        {
            int min = 999;
            foreach (var wk in Workers) if (wk.Employer == player && wk.Happiness < min) min = wk.Happiness;
            return min != 999 ? min : 100;
        }

        /// <summary>Highest happiness; 100 if no staff.</summary>
        public int MaxHappiness(int player)
        {
            int max = -1;
            foreach (var wk in Workers) if (wk.Employer == player && wk.Happiness > max) max = wk.Happiness;
            return max != -1 ? max : 100;
        }

        /// <summary>Strike mood: average minus 10 if anyone is below 0 (economy.md §9.7).</summary>
        public int Mood(int player) => AverageHappiness(player) - (MinHappiness(player) < 0 ? 10 : 0);

        /// <summary>Adds to every employee's happiness, clamped −100..100 (original <c>AddHappiness</c>).</summary>
        public void AddHappiness(int player, int value)
        {
            foreach (var wk in Workers)
                if (wk.Employer == player)
                {
                    wk.Happiness += value;
                    if (wk.Happiness < -100) wk.Happiness = -100;
                    if (wk.Happiness > 100) wk.Happiness = 100;
                }
        }

        /// <summary>
        /// Personnel-advisor value ratio (economy.md §9.3): over all workers of the same type with
        /// |talent difference| &lt; 15 (including the candidate): +1 if they earn more, else −1.
        /// </summary>
        public int QualityRatio(int index)
        {
            var me = Workers[index];
            int r = 0;
            foreach (var wk in Workers)
                if (wk.Type == me.Type && System.Math.Abs(wk.Talent - me.Talent) < 15)
                    r += wk.Salary > me.Salary ? 1 : -1;
            return r;
        }

        public int IndexOf(Worker wk) => Workers.IndexOf(wk);
    }
}
