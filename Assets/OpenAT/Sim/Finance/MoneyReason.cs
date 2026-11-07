namespace OpenAT.Sim.Finance
{
    /// <summary>
    /// Reason codes of money bookings (economy.md §3.1). The numeric values are the original's and
    /// double as text ids (std_ger.res group "Mone") for the account statement.
    /// </summary>
    public static class MoneyReason
    {
        public const int LoanInterest = 2000;
        public const int DepositInterest = 2001;
        public const int OverdraftInterest = 2002;
        public const int LoanTaken = 2003;
        public const int LoanRepaid = 2004;
        public const int BankBinMoney = 2006;
        public const int PlaneBought = 2010;
        public const int PlaneSold = 2011;
        public const int AuctionBranch = 2040;
        public const int AuctionRoute = 2041;
        public const int AuctionGate = 2042;
        public const int GateRent = 2050;
        public const int BranchRent = 2051;
        public const int RouteRent = 2052;
        public const int ContractPenalty = 2060;
        public const int FreightPenalty = 2065;
        public const int Salaries = 2070;
        public const int SabotageJob = 2080;
        public const int KeroseneBought = 2090;
        public const int TankBought = 2091;
        /// <summary>Flight result: 2099 + object type (1 route, 2 order, 3 empty/ferry, 4 freight).</summary>
        public const int FlightResultBase = 2099;
        public const int FlightRoute = 2100;
        public const int FlightOrder = 2101;
        public const int FlightEmpty = 2102;
        public const int FlightFreight = 2103;
        public const int PlaneRefit = 2110;
        public const int FreightSwitch = 2111;
        public const int SabotageFine = 2200;
        public const int SabotageCompensation = 2201;
        public const int Maintenance = 3110;
        /// <summary>Ad campaign tier 0..5: 3120 + tier.</summary>
        public const int AdCampaignBase = 3120;
        public const int BodyguardRebate = 3130;
        public const int DividendsPaid = 3140;
        public const int DividendsReceived = 3141;
        public const int SharesBought = 3150;
        public const int SharesSold = 3151;
        public const int ShareIssueFee = 3160;
        public const int ShareIssueDilution = 3161;
        public const int ShareIssueProceeds = 3162;
        public const int AirportExpansion = 3170;
        public const int TakeoverAbsorbed = 3180;
        public const int LiquidationPayout = 3181;
        /// <summary>Phone call: 3200 + (mobile bit + 2 × long-distance level).</summary>
        public const int PhoneBase = 3200;
        public const int SpecialRoomEntry = 3300;
        public const int RocketPart = 3400;
        public const int SabotageDamage = 3500;
        public const int SeasonIncident = 3501;
        public const int SabotageMoneyTransfer = 3502;
        public const int SecurityFees = 3503;
        public const int WealthTax = 3600;
        public const int GiftReceived = 3700;
        public const int GiftSent = 3701;
        public const int ShopPurchase = 9999;
    }
}
