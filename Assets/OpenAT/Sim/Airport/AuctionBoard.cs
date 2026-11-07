using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Airport
{
    /// <summary>A slip of the auction board ("CTafelZettel").</summary>
    public sealed class AuctionSlip
    {
        /// <summary>City table index, gate number or route id; −1 (cities/gates) or 0 (routes) = empty.</summary>
        public int ObjectId = -1;
        /// <summary>Leading bidder, −1 = none.</summary>
        public int Player = -1;
        public int Price;
        /// <summary>Branch rank the winner gets.</summary>
        public int Rank;
        /// <summary>The local player bid on it (summary letter).</summary>
        public bool WasInterested;

        public AuctionSlip Clone() => (AuctionSlip)MemberwiseClone();
    }

    /// <summary>
    /// The manager's notice board ("CTafelData", contracts-and-routes.md §6): 7 slips each for routes,
    /// branches and gates. Deluxe never posts routes (the code paths exist).
    /// </summary>
    public sealed class AuctionBoard
    {
        public const int Slots = 7;
        public readonly AuctionSlip[] Routes = new AuctionSlip[Slots];
        public readonly AuctionSlip[] Cities = new AuctionSlip[Slots];
        public readonly AuctionSlip[] Gates = new AuctionSlip[Slots];

        public AuctionBoard()
        {
            for (int i = 0; i < Slots; i++)
            {
                Routes[i] = new AuctionSlip { ObjectId = 0 };
                Cities[i] = new AuctionSlip();
                Gates[i] = new AuctionSlip();
            }
        }

        /// <summary>
        /// New postings "Randomize(n)" (§6.1): generator Date + StartTime; bidders cleared. Branches (travel
        /// holding only): per draw (≤ 10) a 30 % chance while fewer than 3 are posted, up to 5 city tries
        /// rejecting cities held 3× or already posted (unbid slips of earlier days stay on the board
        /// and count as posted). Route mission: a second attempt with 10 tries per draw while fewer than 4
        /// are posted, also skipping home and mission cities (bug: a success on tries 6–10 is discarded unless
        /// <see cref="GameOptions.FixMissionBranchTries"/>).
        /// Gates: unrented numbers (4 on day 0), up to 7, price 3000 − 100·number.
        /// </summary>
        public void Randomize(World w, int day)
        {
            for (int c = 0; c < Slots; c++)
            {
                Routes[c].WasInterested = Cities[c].WasInterested = Gates[c].WasInterested = false;
                Routes[c].Player = Cities[c].Player = Gates[c].Player = -1;
                Gates[c].ObjectId = -1;
            }
            int numCities = 0, numGates = 0;
            var r = new TeakRand(w.Clock.Date + (int)w.Clock.StartTime);
            var cities = w.Data.Cities;

            if (w.TravelHolding && cities.Count > 0)
            {
                for (int c = 0; c < System.Math.Min(day, 10); c++)
                {
                    if (r.Rand(100) < 30 && numCities < 3)
                    {
                        int e, obj = -1, used = 0;
                        for (e = 0; e < 5; e++)
                        {
                            obj = cities.IndexOf(CityGeo.RandomCity(cities, r));
                            used = Holders(w, obj);
                            if (used >= 3) continue;
                            if (Posted(obj)) continue;
                            break;
                        }
                        if (e < 5) Post(w, ref numCities, obj, used);
                    }
                    if (w.Difficulty == Mission.ConnectWorld && numCities < 4)
                    {
                        int e, obj = -1, used = 0;
                        for (e = 0; e < 10; e++)
                        {
                            obj = cities.IndexOf(CityGeo.RandomCity(cities, r));
                            used = Holders(w, obj);
                            if (used >= 3) continue;
                            if (Posted(obj)) continue;
                            bool special = obj == w.CityIndex(w.HomeCity);
                            foreach (var m in w.MissionCityIds) if (m != 0 && w.CityIndex(m) == obj) special = true;
                            if (special) continue;
                            break;
                        }
                        // The original tests "e < 5" after this 10-try loop, discarding hits on tries 6–10.
                        if (e < (w.Options.FixMissionBranchTries ? 10 : 5) && numCities < Slots) Post(w, ref numCities, obj, used);
                    }
                }
            }

            int anz = w.Clock.Date == 0 ? 4 : w.Rules.Get<IAirportGateCount>().GateCount(w);
            for (int c = 0, f = 0; c < anz && f < Slots; c++)
            {
                bool found = false;
                foreach (var p in w.Players)
                    if (!p.IsOut && p.Gates.HasGate(c)) found = true;
                if (found) continue;
                Gates[numGates].ObjectId = c;
                Gates[numGates].Player = -1;
                Gates[numGates].Price = 3000 - c * 100;
                Gates[numGates].Rank = 0;
                f++;
                numGates++;
            }
        }

        void Post(World w, ref int numCities, int obj, int used)
        {
            if (numCities >= Slots) return;
            var s = Cities[numCities];
            s.ObjectId = obj;
            s.Player = -1;
            s.Price = w.Data.Cities.All[obj].OfficeRent;
            s.Rank = used + 1;
            numCities++;
        }

        bool Posted(int obj)
        {
            foreach (var s in Cities) if (s.ObjectId == obj) return true;
            return false;
        }

        static int Holders(World w, int cityIndex)
        {
            int n = 0;
            foreach (var p in w.Players) if (cityIndex >= 0 && cityIndex < p.RentCities.Length && p.RentCities[cityIndex].Rank != 0) n++;
            return n;
        }

        /// <summary>
        /// A bid (§6.2): +10 % (integer) and the bidder leads. Within one visit a slip whose leader changed
        /// since entering is first restored to its state on entry (pass <paramref name="onEntry"/>), so a
        /// visit raises a price at most once.
        /// </summary>
        public static bool Bid(World w, Player p, AuctionSlip slip, AuctionSlip onEntry = null)
        {
            if (slip.ObjectId < 0 || slip.Player == p.Index) return false;
            if (onEntry != null && slip.Player != onEntry.Player)
            {
                slip.Player = onEntry.Player;
                slip.Price = onEntry.Price;
            }
            if (slip.Player == p.Index) return false;
            int previous = slip.Player;
            slip.Price += slip.Price / 10;
            slip.Player = p.Index;
            if (p.Index == w.LocalPlayer) slip.WasInterested = true;
            // Outbid someone on a gate or branch: the informer may tell him (staff domain, Advi 9001/9002).
            if (previous >= 0 && previous < Player.Count && w.Auctions != null)
            {
                bool gate = System.Array.IndexOf(w.Auctions.Gates, slip) >= 0;
                if (gate || System.Array.IndexOf(w.Auctions.Cities, slip) >= 0)
                    w.Events.Publish(new Staff.RivalMove { Rival = p, Kind = gate ? Staff.RivalMoveKind.OutbidGate : Staff.RivalMoveKind.OutbidBranch, Previous = w.Players[previous], ObjectId = slip.ObjectId });
            }
            return true;
        }

        /// <summary>
        /// Nightly resolution (sim-core.md §2.6 step 24): every slip with a leader goes to it; branch:
        /// rank as posted, image 0, rent = bid; route: rented at the bid; gate: first free record, rent =
        /// bid; the winner pays 3 × the bid once (2040/2041/2042). Won slips are cleared.
        /// </summary>
        public void Resolve(World w)
        {
            var won = new List<string>();
            var lost = new List<string>();
            for (int c = 0; c < Slots; c++)
            {
                var s = Cities[c];
                if (s.ObjectId != -1 && s.Player != -1)
                {
                    var p = w.Players[s.Player];
                    if (s.WasInterested) (s.Player == w.LocalPlayer ? won : lost).Add("city:" + s.ObjectId);
                    p.RentCities[s.ObjectId].Rank = s.Rank;
                    p.RentCities[s.ObjectId].Image = 0;
                    p.RentCities[s.ObjectId].Rent = s.Price;
                    p.Book(-(long)s.Price * 3, MoneyReason.AuctionBranch, w.Data.Cities.All[s.ObjectId].Name);
                    w.Events.Publish(new AuctionWon { Player = p, Kind = "city", ObjectId = s.ObjectId, Price = s.Price });
                    s.ObjectId = -1;
                    s.Player = -1;
                }

                var rt = Routes[c];
                var route = rt.ObjectId != 0 ? w.RouteById(rt.ObjectId) : null;
                if (route != null && rt.Player != -1)
                {
                    var p = w.Players[rt.Player];
                    if (rt.WasInterested) (rt.Player == w.LocalPlayer ? won : lost).Add("route:" + rt.ObjectId);
                    RouteOps.Rent(w, p, route.From, route.To, rt.Price);
                    p.Book(-(long)rt.Price * 3, MoneyReason.AuctionRoute, Contracts.ContractUsage.CityPair(w, route.From, route.To));
                    w.Events.Publish(new AuctionWon { Player = p, Kind = "route", ObjectId = rt.ObjectId, Price = rt.Price });
                    rt.ObjectId = 0;
                    rt.Player = -1;
                }

                var g = Gates[c];
                if (g.ObjectId != -1 && g.Player != -1)
                {
                    var p = w.Players[g.Player];
                    if (g.WasInterested) (g.Player == w.LocalPlayer ? won : lost).Add("gate:" + (g.ObjectId + 1));
                    p.Gates.Rent(g.ObjectId, g.Price);
                    p.Book(-(long)g.Price * 3, MoneyReason.AuctionGate);
                    w.Events.Publish(new AuctionWon { Player = p, Kind = "gate", ObjectId = g.ObjectId, Price = g.Price });
                    g.ObjectId = -1;
                    g.Player = -1;
                }
            }
            if (won.Count > 0 || lost.Count > 0)
                w.Events.Publish(new LetterSent(w.Local, "Misc", 2600, string.Join(";", won), string.Join(";", lost)));
        }
    }
}
