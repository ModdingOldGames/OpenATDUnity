namespace OpenAT.Sim.Items
{
    /// <summary>
    /// Rocket ("The Final Frontier") and space-station ("Beyond the Final Frontier") parts
    /// (items-security-special.md §4.1, economy.md §15.6). Both missions use one 10-bit mask per
    /// airline, <see cref="Missions.MissionCounters.RocketFlags"/>; all 10 bits = won.
    /// </summary>
    public static class SpaceParts
    {
        public const int Parts = 10;
        public const int AllMask = (1 << Parts) - 1;

        // ---- rocket bits
        public const int Base = 0, Tower = 1, Arm = 2, Airframe = 3, Wings = 4, Capsule = 5, Boosters = 6, MainEngine = 7, Tail = 8, Cockpit = 9;
        public const int RocketStage1 = (1 << Base) | (1 << Tower) | (1 << Arm);

        /// <summary>Human price of each rocket part by bit (the dialog's per-part price).</summary>
        public static readonly long[] RocketPriceByBit = { 200000, 400000, 600000, 5000000, 8000000, 10000000, 25000000, 50000000, 20000000, 85000000 };

        /// <summary>
        /// The original's rocket price table in menu order. Robots index it by bit number, so they pay
        /// 20 M / 25 M / 50 M for bits 6 / 7 / 8 (same total; bug, §4.1 Q-7, kept behind FixRobotRocketPrices).
        /// </summary>
        public static readonly long[] RocketPriceTable = { 200000, 400000, 600000, 5000000, 8000000, 10000000, 20000000, 25000000, 50000000, 85000000 };

        /// <summary>Rocket bit of each menu entry: 5000..5002 (stage 1), 5100..5106 (stage 2).</summary>
        public static int RocketBitOfMenu(int menuId)
        {
            switch (menuId)
            {
                case 5000: return Base; case 5001: return Tower; case 5002: return Arm;
                case 5100: return Airframe; case 5101: return Wings; case 5102: return Capsule; case 5103: return Tail;
                case 5104: return Boosters; case 5105: return MainEngine; case 5106: return Cockpit;
            }
            return -1;
        }

        /// <summary>Prerequisite bits of a rocket part (tower ← base, arm ← tower, stage 2 ← stage 1 + airframe, boosters ← wings).</summary>
        public static int RocketPrerequisites(int bit)
        {
            switch (bit)
            {
                case Base: return 0;
                case Tower: return 1 << Base;
                case Arm: return 1 << Tower;
                case Airframe: return RocketStage1;
                case Boosters: return RocketStage1 | (1 << Airframe) | (1 << Wings);
                default: return RocketStage1 | (1 << Airframe);
            }
        }

        /// <summary>Nasa text for a missing prerequisite (5096 base, 5097 tower, 5196 airframe, 5197 wings).</summary>
        public static int RocketPrerequisiteText(int bit, int flags)
        {
            if (bit == Tower) return 5096;
            if (bit == Arm) return 5097;
            if (bit == Airframe) return 0;
            if ((flags & (1 << Airframe)) == 0) return 5196;
            return 5197;
        }

        // ---- station
        /// <summary>Space-station part prices by bit (rings A–C, oxygen, comms, main, living, office, power plant, solar).</summary>
        public static readonly long[] StationPriceByBit = { 1000000, 2000000, 3000000, 2000000, 10000000, 20000000, 35000000, 50000000, 35000000, 80000000 };
        public const int StationStage1 = 0x007, StationStage2 = 0x0F8, StationStage3 = 0x300;

        /// <summary>Stage 1..3 of a station bit.</summary>
        public static int StationStage(int bit) => bit < 3 ? 1 : bit < 8 ? 2 : 3;

        /// <summary>Station parts offered: a stage is listed once the previous one is complete.</summary>
        public static bool StationOffered(int bit, int flags)
        {
            int st = StationStage(bit);
            if (st == 1) return (flags & StationStage1) != StationStage1;
            if (st == 2) return (flags & StationStage1) == StationStage1 && (flags & StationStage2) != StationStage2;
            return (flags & StationStage1) == StationStage1 && (flags & StationStage2) == StationStage2;
        }

        public static bool IsRocketMission(int difficulty) => difficulty == Mission.Rocket;
        public static bool IsStationMission(int difficulty) => difficulty == StationMission;
        /// <summary>DIFF_ADDON10 "Beyond the Final Frontier".</summary>
        public const int StationMission = 20;
        public static bool HasNasa(int difficulty) => IsRocketMission(difficulty) || IsStationMission(difficulty);

        /// <summary>Price of a part for a human buyer.</summary>
        public static long Price(int difficulty, int bit) =>
            bit < 0 || bit >= Parts ? 0 : IsRocketMission(difficulty) ? RocketPriceByBit[bit] : StationPriceByBit[bit];

        /// <summary>Price a robot pays for a part. The original indexes the rocket menu table by bit number
        /// (<paramref name="menuTableByBit"/>, restored with <see cref="GameOptions.FixRobotRocketPrices"/> off).</summary>
        public static long RobotPrice(int difficulty, int bit, bool menuTableByBit = false) =>
            bit < 0 || bit >= Parts ? 0 : !IsRocketMission(difficulty) ? StationPriceByBit[bit] : menuTableByBit ? RocketPriceTable[bit] : RocketPriceByBit[bit];

        /// <summary>Lowest missing bit, or −1.</summary>
        public static int LowestMissing(int flags)
        {
            for (int b = 0; b < Parts; b++) if ((flags & (1 << b)) == 0) return b;
            return -1;
        }

        public static int Count(int flags) { int n = 0; for (int b = 0; b < Parts; b++) if ((flags & (1 << b)) != 0) n++; return n; }
    }
}
