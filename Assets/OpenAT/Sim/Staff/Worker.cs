namespace OpenAT.Sim.Staff
{
    /// <summary>Advisor kinds (economy.md §9.1, original <c>BERATERTYP_*</c>). Values double as worker types 1..9.</summary>
    public enum AdvisorType
    {
        Secretary = 0,
        Personnel = 1,
        Kerosene = 2,
        Route = 3,
        Order = 4,
        Finance = 5,
        Informer = 6,
        PlaneAnalyst = 7,
        Fitness = 8,
        Security = 9,
    }

    /// <summary>Worker type and employer codes (economy.md §9.1).</summary>
    public static class WorkerType
    {
        /// <summary>Types 1..9 are advisors (<see cref="AdvisorType"/>).</summary>
        public const int Pilot = 10;
        public const int Attendant = 11;

        /// <summary>Hidden pool.</summary>
        public const int Reserve = 100;
        /// <summary>Today's applicant.</summary>
        public const int Jobless = 101;

        public static bool IsAdvisor(int type) => type < Pilot;
    }

    /// <summary>One person of the staff pool (economy.md §9.1, original <c>CWorker</c>).</summary>
    public sealed class Worker
    {
        public string Name;
        /// <summary>0 female, 1 male.</summary>
        public int Sex;
        public int Type;
        /// <summary>Current monthly salary and the salary from the table / generation.</summary>
        public int Salary, OriginalSalary;
        /// <summary>Quality 0..100 (generated workers may exceed 100).</summary>
        public int Talent;
        public int Age;
        public string Comment;
        /// <summary>Airline 0..3, <see cref="WorkerType.Reserve"/> or <see cref="WorkerType.Jobless"/>.</summary>
        public int Employer = WorkerType.Reserve;
        /// <summary>Assigned plane id, −1 = none.</summary>
        public int PlaneId = -1;
        /// <summary>Happiness, nominally −100..100 (not clamped by every change).</summary>
        public int Happiness = 100;
        public bool WarnedToday;

        public bool IsPilot => Type == WorkerType.Pilot;
        public bool IsAttendant => Type == WorkerType.Attendant;
        public bool IsAdvisor => WorkerType.IsAdvisor(Type);
        public bool IsEmployed => Employer >= 0 && Employer < Player.Count;

        public override string ToString() => $"{Name} t{Type} talent {Talent} salary {Salary} emp {Employer}";
    }
}
