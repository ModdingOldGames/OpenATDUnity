namespace OpenAT.Sim
{
    /// <summary>
    /// Mission ("Difficulty") values as used by the original (sim-core.md §3.4):
    /// -1 free game, 0..5 base campaign, 11..20 add-on, 41..50 "flight security" campaign.
    /// </summary>
    public static class Mission
    {
        public const int FreeGame = -1;
        public const int Tutorial = 0;
        public const int Passengers = 1;
        public const int Profit = 2;
        public const int ConnectWorld = 3;
        public const int Image = 4;
        public const int Rocket = 5;
        public const int AddOnFirst = 11, AddOnLast = 20;
        public const int AtfsFirst = 41, AtfsLast = 50;

        public static bool IsBase(int d) => d >= 0 && d <= 5;
        public static bool IsAddOn(int d) => d >= AddOnFirst && d <= AddOnLast;
        public static bool IsAtfs(int d) => d >= AtfsFirst && d <= AtfsLast;

        /// <summary>Starting values per mission (sim-core.md §3.3).</summary>
        public readonly struct StartValues
        {
            public readonly long HumanMoney, ComputerMoney, Credit;
            public readonly int LoanRate, SavingsRate;
            public StartValues(long mh, long mc, long credit, int loan, int savings)
            { HumanMoney = mh; ComputerMoney = mc; Credit = credit; LoanRate = loan; SavingsRate = savings; }
        }

        public static StartValues Start(int d)
        {
            switch (d)
            {
                case FreeGame: return new StartValues(1500000, 2000000, 0, 10, 3);
                case 0: return new StartValues(5000000, 200000, 0, 5, 5);
                case 1: return new StartValues(5000000, 500000, 0, 5, 5);
                case 2: return new StartValues(3000000, 1000000, 0, 5, 5);
                case 3: return new StartValues(2000000, 3000000, 0, 6, 4);
                case 4: return new StartValues(1800000, 4000000, 0, 10, 3);
                case 5: return new StartValues(1500000, 6000000, 0, 15, 1);
                case 11: return new StartValues(3000000, 1000000, 10000000, 15, 1);
            }
            if (IsAddOn(d)) return new StartValues(3000000, 1000000, 0, 15, 1);
            if (IsAtfs(d)) return new StartValues(3000000, 3000000, 0, 15, 0);
            return Start(FreeGame);
        }
    }
}
