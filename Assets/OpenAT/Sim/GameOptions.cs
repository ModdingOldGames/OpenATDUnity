namespace OpenAT.Sim
{
    /// <summary>
    /// Game rules options. Defaults reproduce the original game's behaviour without its bugs: every
    /// fix of an original bug is a <c>Fix*</c> switch that defaults to on, so turning it off restores the
    /// original (documented in docs/QUIRKS.md and docs/spec/findings/*.md). Additions that are not in the
    /// original default to off. Extensions can add their own switches.
    /// </summary>
    public sealed class GameOptions
    {
        // ---- original options (options screen)
        public bool RandomStartDay = true;
        public bool RealCityCodes = false;
        public bool ShortBriefing = true;
        public bool Autosave = true;

        // ---- fixes for original bugs (on by default; all off = original behaviour)
        /// <summary>Charge loan interest on the loan, not on cash (economy.md quirk 1).</summary>
        public bool FixLoanInterest = true;
        /// <summary>Use the first-class ticket price for first-class demand (planes-and-flights.md; the SDL port's fix).</summary>
        public bool FixFirstClassDemand = true;
        /// <summary>Computer airlines do not get a larger hidden bonus in early missions (sim-core.md §3.3).</summary>
        public bool FixComputerBonusAbs = true;
        /// <summary>Credit limit uses 200 000 − Credit instead of 200 000 − limit (economy.md §5.1, Q-1).</summary>
        public bool FixCreditLimit = true;
        /// <summary>Negative cash is charged at the loan rate instead of the deposit rate (economy.md §5.3, quirk 2).</summary>
        public bool FixOverdraftInterest = true;
        /// <summary>Loan repayment is limited to what cash allows down to the overdraft limit (economy.md §5.1, quirk 4).</summary>
        public bool FixLoanRepayCash = true;
        /// <summary>Share sales subtract the flat 100 fee instead of adding it (economy.md §6.5, quirk 5).</summary>
        public bool FixShareSaleFee = true;
        /// <summary>Dividend statistics record the daily amount, not the yearly one (economy.md §6.7, quirk 6).</summary>
        public bool FixDividendStatistics = true;
        /// <summary>Share-issue dilution compensation is paid only for shares held by players (economy.md §6.6, quirk 7).</summary>
        public bool FixDilutionFreeFloat = true;
        /// <summary>A share issue books the offered count (pct % of the issuable room rounded to 100), not the unrounded one (finance-shares-valuation.md §5.2).</summary>
        public bool FixShareIssueRounding = true;
        /// <summary>Mission-progress statistic of the profit mission uses profit ÷ target (economy.md §16.3, quirk 9).</summary>
        public bool FixProfitMissionProgress = true;
        /// <summary>Finance-advisor rate perk also applies to the free game / add-on / ATFS rates (economy.md §5.1, Q-2).</summary>
        public bool FixFinanceAdvisorPerk = true;
        /// <summary>Crew mapping: a spare attendant fills one luxury slot (not every free one), and luxury
        /// attendants move to planes short of base crew (the original's second pass never runs; economy.md §9.4).</summary>
        public bool FixCrewMapping = true;

        // ---- operations fixes (planes, flights, contracts, routes)
        /// <summary>Freight planning counts only flights that have not started yet (planes-and-flights.md §19.2, the SDL port's fix).</summary>
        public bool FixFreightTonnageCount = true;
        /// <summary>Count gate-less home departures in OutOfGates so the AI's gate bidding can trigger (never incremented in the original).</summary>
        public bool FixOutOfGatesCounter = true;
        /// <summary>"Paste" in the equipment room also applies the cabin items (planes-and-flights.md §6.6).</summary>
        public bool FixPasteCabinEquipment = true;
        /// <summary>Nightly repair cost multiplies improvement × list price in 64 bits (the original's 32-bit product
        /// wraps negative for very expensive planes and pays the player; planes-and-flights.md §7.3).</summary>
        public bool FixRepairCostOverflow = true;
        /// <summary>The hidden computer bonus pool is reduced by the amount paid out (the original recomputes the
        /// deduction from the already increased flight result; sim-core.md §3.3).</summary>
        public bool FixHiddenBonusDeduction = true;
        /// <summary>Automatic ferry flights inserted at "now + 2" late in the day, and plan extension copies, wrap a
        /// start/landing hour of 24 into the next day (the original keeps hour 24/25; planes-and-flights.md §10.2, §10.4).</summary>
        public bool FixFlightHourWrap = true;
        /// <summary>Route mission: a branch city found on the second attempt's tries 6–10 is posted (the original
        /// discards it by testing the 5-try bound).</summary>
        public bool FixMissionBranchTries = true;
        /// <summary>Route "average days between flights" is a nightly moving average of the days since the last
        /// flight (the original sets it to 30 at licensing and never updates it; contracts-and-routes.md §11).</summary>
        public bool FixRouteAvgFlown = true;

        // ---- statistics and hall fixes
        /// <summary>The overnight statistics shift starts additive series at 0, so they hold daily values and their
        /// month sums are real sums (the original never clears today's value: running totals; economy.md §16.1).</summary>
        public bool FixStatisticsDayShift = true;
        /// <summary>Westbound customers keeping the outer lane stay on the westbound outer lane (the original sends
        /// them to the eastbound one's y; airport-and-people.md).</summary>
        public bool FixHallWestboundLane = true;
        /// <summary>Waiting customers get the crossing / check-in-exit patience boosts (the original tests the
        /// direction after overwriting it, so they never apply; airport-and-people.md).</summary>
        public bool FixHallWaitPatience = true;
        /// <summary>Monitor watchers pick the spot they checked (the original checks spot (d+n)%6 but walks to
        /// (d+n)&amp;3; airport-and-people.md).</summary>
        public bool FixHallMonitorSpot = true;

        // ---- items / pranks / security fixes (items-security-special.md; on by default)
        /// <summary>The flat-tyre prank's delay statistic is booked to the victim, not the saboteur (§1.3, Q-8).</summary>
        public bool FixSabotageDelayStatistic = true;
        /// <summary>A plane prank whose plane has no next flight waits silently instead of counting accident/sabotage statistics every hour (§1.3, Q-2).</summary>
        public bool FixSabotageRetrigger = true;
        /// <summary>The impound prank also delays computer airlines' departures (§1.3: the delay check skips robots).</summary>
        public bool FixImpoundComputerPlanes = true;
        /// <summary>Route theft hands the stolen route to the saboteur as intended (§1.3: byte compared with −1).</summary>
        public bool FixRouteTheftTransfer = true;
        /// <summary>Special pranks 5 and 6 check the debt limit like every other prank (the original charges them unchecked).</summary>
        public bool FixSabotageSpecialAffordability = true;
        /// <summary>Special pranks 5 and 6 raise saboteur trust to k + 1 like the others, so buying the impound
        /// unlocks route theft (the original's formula yields a negative value: no gain, route theft unreachable; §1.2, Q-1).</summary>
        public bool FixSpecialPrankTrust = true;
        /// <summary>A human's special prank 5/6 order also sets the victim's "security needed" bit, as robots' orders
        /// do (the original forgets it, so robots never learn to protect against them; §2.4).</summary>
        public bool FixSpecialPrankNeeded = true;
        /// <summary>Robots pay the human price for rocket parts 6–8 (the original indexes the menu-ordered table by
        /// bit: 20 M / 25 M / 50 M instead of 25 M / 50 M / 20 M; §4.1, Q-7).</summary>
        public bool FixRobotRocketPrices = true;
        /// <summary>A first laptop is refused when all six inventory slots are full (the original charges it and
        /// then cannot hand it over; §3.1).</summary>
        public bool FixLaptopFullInventory = true;

        // ---- additions (not in the original)
        /// <summary>Route theft (special prank 6) is offered already from saboteur trust 5 on. Not needed for
        /// reachability once <see cref="FixSpecialPrankTrust"/> is on (the impound then gives trust 6); §1.2, Q-1.</summary>
        public bool RouteTheftReachable = false;
        public bool Widescreen = true;
        /// <summary>Route market share weights use each licensee's own ticket price (contracts-and-routes.md §11.2).</summary>
        public bool FairRouteCompetition = false;
        /// <summary>A computer airline's figure that the human phones stops in the hall and shows the phone pose
        /// (the original shows it only for the local player's own figure, airport-and-people.md §6.2).</summary>
        public bool ComputerPhonePose = false;

        // ---- OpenAT improvements (on by default; off = the original's interface)
        /// <summary>Flight planner improvements: fleet search and filters, previous/next plane, docked schedules in the
        /// extra width of wide screens, flight detail cards, precise time setting and refusal notices.</summary>
        public bool PlannerEnhancements = true;
        /// <summary>The globe room's filofax at the window's real resolution: smoothed artwork and smaller,
        /// sharp text, so the lists hold more rows (needs <see cref="PlannerEnhancements"/>).</summary>
        public bool PlannerHiResFilofax = true;
        /// <summary>The laptop's planner windows, the docked schedules and the order counters' planner panel at the
        /// window's real resolution, like <see cref="PlannerHiResFilofax"/> (needs <see cref="PlannerEnhancements"/>).</summary>
        public bool PlannerHiResLaptop = true;
        /// <summary>Gamepad play (Steam Deck, Legion Go, Xbox and PlayStation pads): a stick cursor that snaps to hotspots,
        /// d-pad and shoulder-button navigation between hotspots, keyboard-free flight scheduling in the planner, and the
        /// hall figure walking with the left stick at a pace set by how far the stick is pushed. Mouse and keyboard
        /// play stay as they are.</summary>
        public bool GamepadSupport = true;
        /// <summary>The first morning's walk of all figures to the boss office can be skipped (Esc, Space, right click,
        /// or the pad's B, Cross or Start): the briefing opens at once.</summary>
        public bool SkipStartWalk = true;

        public GameOptions Clone() => (GameOptions)MemberwiseClone();
    }
}
