using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Contracts
{
    /// <summary>
    /// Plan validation of held contracts and freight (planes-and-flights.md §10.5–10.6), nightly
    /// penalties and clean-up (§12.5, contracts-and-routes.md §2.9).
    /// </summary>
    public static class ContractUsage
    {
        /// <summary>
        /// "UpdateAuftragsUsage" (§10.5): reset every contract to Okay 1 / InPlan 0 (unless done), then
        /// flag the contract entries of human airlines; non-contract, non-freight entries become Okay 0.
        /// </summary>
        public static void UpdateContracts(World w, Player p)
        {
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var list = p.Contracts.ToList();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].Okay = 1;
                if (list[i].InPlan != PlanState.Done) list[i].InPlan = PlanState.NotPlanned;
            }
            foreach (var plane in p.Planes.Items)
            {
                var f = plane.Plan.Entries;
                for (int d = FlightPlan.Size - 1; d >= 0; d--)
                {
                    ref var e = ref f[d];
                    if (e.Kind == FlightKind.Contract && p.IsHuman)
                    {
                        var c = p.Contracts.Find(e.ObjectId);
                        if (c == null) continue;
                        if ((plane.Seats >= c.Persons && e.StartDate <= c.LastDate) || e.StartDate > date || (e.StartDate == date && e.StartHour > hour))
                        {
                            if (c.InPlan == PlanState.NotPlanned) { c.InPlan = PlanState.Planned; e.Okay = EntryOkay.Ok; }
                        }
                        if (plane.Seats < c.Persons) { c.Okay = 0; e.Okay = EntryOkay.TooSmall; }
                        if (e.StartDate < c.Date || e.StartDate > c.LastDate) { c.Okay = 0; e.Okay = EntryOkay.WrongDay; }
                    }
                    else if (e.Kind != FlightKind.Freight)
                        e.Okay = EntryOkay.Ok;
                }
            }
        }

        /// <summary>
        /// "UpdateFrachtauftragsUsage" (§10.6): humans only; re-allocates the open tonnage over the planned
        /// freight flights in plan order. Which flights count toward the open tonnage follows the GOG
        /// original unless <see cref="GameOptions.FixFreightTonnageCount"/> (SDL port) is set.
        /// </summary>
        public static void UpdateFreight(World w, Player p)
        {
            if (p.IsComputer) return;
            int date = w.Clock.Date, hour = w.Clock.Hour;
            var list = p.Freights.ToList();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var fr = list[i];
                fr.Okay = 1;
                if (fr.InPlan != PlanState.Done && fr.TonsLeft > 0) { fr.InPlan = PlanState.NotPlanned; fr.TonsOpen = fr.TonsLeft; }
            }
            foreach (var plane in p.Planes.Items)
            {
                var f = plane.Plan.Entries;
                for (int d = 0; d < FlightPlan.Size; d++)
                {
                    ref var e = ref f[d];
                    if (e.Kind != FlightKind.Freight) continue;
                    var fr = p.Freights.Find(e.ObjectId);
                    if (fr == null) continue;
                    if (e.StartDate < fr.Date || e.StartDate > fr.LastDate)
                    {
                        fr.Okay = 0;
                        fr.InPlan = PlanState.Planned;
                        e.Okay = EntryOkay.WrongDay;
                    }
                    else if (fr.TonsLeft != 0)
                    {
                        int cap = plane.Seats / 10;
                        e.Passengers = cap;
                        bool counts;
                        if (w.Options.FixFreightTonnageCount)
                            counts = !(e.StartDate < date || (e.StartDate == date && e.StartHour < hour));
                        else
                            counts = e.StartDate > date || e.StartHour >= hour; // the original's hour test ignores the date
                        if (counts) fr.TonsOpen -= cap;
                        if (fr.TonsOpen <= 0)
                        {
                            e.Passengers = (e.Passengers - (-fr.TonsOpen)) & 0xFFFF;
                            fr.TonsOpen = 0;
                            fr.InPlan = PlanState.Planned;
                            e.Okay = EntryOkay.Ok;
                        }
                        if (e.Passengers == 0) e.GateWarning = true;
                    }
                }
            }
        }

        /// <summary>
        /// "UpdateAuftraege" (§12.5, contracts-and-routes.md §2.9), per-airline new-day step 17: the
        /// morning after the last day human airlines pay the penalty (or get a no-penalty letter) unless
        /// done or planned-and-valid; two days after, the contract and its not-yet-started entries are
        /// removed (kept while an entry cannot be removed).
        /// </summary>
        public static void DailyUpdate(World w, Player p)
        {
            int date = w.Clock.Date;
            foreach (var c in p.Contracts.ToList())
            {
                if (c.LastDate == date - 1)
                {
                    if (c.InPlan != PlanState.Done && p.IsHuman && !(c.InPlan == PlanState.Planned && c.Okay == 1))
                    {
                        if (c.Penalty > 0)
                        {
                            p.Book(-c.Penalty, MoneyReason.ContractPenalty, CityPair(w, c.From, c.To));
                            p.Profit -= c.Penalty;
                            if (p.Owner == Owner.Human && !p.IsOut) w.Events.Publish(new LetterSent(p, "Lett", 1000, w.City(c.From).Name, w.City(c.To).Name, c.Penalty));
                        }
                        else if (p.Owner == Owner.Human && !p.IsOut)
                            w.Events.Publish(new LetterSent(p, "Lett", 1010, w.City(c.From).Name, w.City(c.To).Name));
                        w.Events.Publish(new ContractExpired { Player = p, Contract = c, Penalty = c.Penalty > 0 ? c.Penalty : 0 });
                    }
                }
                else if (c.LastDate == date - 2)
                {
                    bool cantDelete = false;
                    foreach (var plane in p.Planes.ToList())
                    {
                        var f = plane.Plan.Entries;
                        for (int e = FlightPlan.Size - 1; e >= 0; e--)
                        {
                            if (f[e].Kind != FlightKind.Contract || f[e].ObjectId != c.Id) continue;
                            if (f[e].StartDate > date || f[e].StartHour > 2)
                            {
                                if (e == 0) plane.Plan.StartCity = f[e].To;
                                f[e].Kind = FlightKind.None;
                                plane.Plan.UpdatePointers(date, w.Clock.Hour);
                                FlightPlanner.Check(w, p, plane);
                                UpdateContracts(w, p);
                            }
                            else cantDelete = true;
                        }
                    }
                    if (!cantDelete) p.Contracts.Remove(c.Id);
                }
            }

            foreach (var fr in p.Freights.ToList())
            {
                if (fr.LastDate == date - 1)
                {
                    if (fr.InPlan != PlanState.Done && p.IsHuman && !(fr.InPlan == PlanState.Planned && fr.Okay == 1))
                    {
                        if (fr.Penalty > 0)
                        {
                            p.Book(-fr.Penalty, MoneyReason.FreightPenalty, CityPair(w, fr.From, fr.To));
                            p.Profit -= fr.Penalty;
                            if (p.Owner == Owner.Human && !p.IsOut) w.Events.Publish(new LetterSent(p, "Lett", 1005, w.City(fr.From).Name, w.City(fr.To).Name, fr.Penalty));
                        }
                        else if (p.Owner == Owner.Human && !p.IsOut)
                            w.Events.Publish(new LetterSent(p, "Lett", 1015, w.City(fr.From).Name, w.City(fr.To).Name));
                        w.Events.Publish(new ContractExpired { Player = p, Freight = fr, Penalty = fr.Penalty > 0 ? fr.Penalty : 0 });
                    }
                }
                else if (fr.LastDate <= date - 2)
                {
                    bool cantDelete = false;
                    foreach (var plane in p.Planes.ToList())
                    {
                        var f = plane.Plan.Entries;
                        bool again = true;
                        while (again)
                        {
                            again = false;
                            for (int e = FlightPlan.Size - 1; e >= 0; e--)
                            {
                                if (f[e].Kind != FlightKind.Freight || f[e].ObjectId != fr.Id) continue;
                                if (f[e].StartDate > date || f[e].StartHour > 2)
                                {
                                    if (e == 0) plane.Plan.StartCity = f[e].To;
                                    f[e].Kind = FlightKind.None;
                                    plane.Plan.UpdatePointers(date, w.Clock.Hour);
                                    FlightPlanner.Check(w, p, plane);
                                    UpdateFreight(w, p);
                                    again = true;
                                    break;
                                }
                                cantDelete = true;
                            }
                        }
                    }
                    if (!cantDelete) p.Freights.Remove(fr.Id);
                }
            }
        }

        public static string CityPair(World w, int from, int to)
        {
            var a = w.Data.Cities.Find(from);
            var b = w.Data.Cities.Find(to);
            return (a?.ShortCode ?? "?") + "-" + (b?.ShortCode ?? "?");
        }
    }
}
