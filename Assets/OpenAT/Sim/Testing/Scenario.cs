using System;
using System.Collections.Generic;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Modding;
using OpenAT.Sim.Routes;

namespace OpenAT.Sim.Testing
{
    /// <summary>
    /// A test state applied right after a new game (docs/ROOMS.md "Test scenarios"): a JSON file such as
    /// <c>tools/scenarios/laptop-day3.json</c>. Everything goes through the normal commands where one exists,
    /// so the result is a state the game could reach; it is deterministic (same file + seed = same world)
    /// and runs headless. The view only adds "open this room" (<see cref="Room"/>).
    /// <para>Keys (all optional): <c>mission</c>, <c>seed</c>, <c>homeCity</c> (name), <c>advanceDays</c>
    /// (play N days, robots included, and stop at the next morning), <c>skipBriefing</c> (start the working
    /// day at once, the figure stays in the hall), <c>time</c> ("HH:MM", run the clock to it), <c>money</c>,
    /// <c>items</c> (names, see <see cref="ItemNames"/>), <c>laptop</c> {quality 1..4, battery (game minutes),
    /// virus 0..3}, <c>security</c> (names, see <see cref="SecurityNames"/>), <c>planes</c>
    /// [{type: name or csv id, count}], <c>routes</c> (rent N routes from the home airport),
    /// <c>orders</c> / <c>freight</c> (accept N travel-agency / cargo-office offers), <c>planFlights</c>
    /// (put the accepted orders into the plans), <c>advisors</c> {type name (see <see cref="Staff.AdvisorType"/>):
    /// talent} (employ a free advisor of that type), <c>room</c> (room id or screen name for the view).</para>
    /// </summary>
    public sealed class Scenario
    {
        public int Mission = OpenAT.Sim.Mission.FreeGame;
        public int Seed = 12345;
        public string HomeCity;
        public int AdvanceDays;
        public bool SkipBriefing;
        public int? TimeOfDay;
        public long? Money;
        public readonly List<int> Items = new List<int>();
        public int? LaptopQuality, LaptopBattery, LaptopVirus;
        public readonly List<int> Security = new List<int>();
        public readonly List<(string type, int count)> Planes = new List<(string, int)>();
        public int Routes, Orders, Freight;
        public bool PlanFlights;
        public string Room;
        public readonly List<(int type, int talent)> Advisors = new List<(int, int)>();
        /// <summary>What went wrong while applying (unknown names, refused commands); empty when all worked.</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Own names for the inventory items (ItemId order).</summary>
        public static readonly string[] ItemNames =
        {
            "laptop", "phone", "filofax", "machinegun", "beer", "cigar", "oil", "postcard", "pills", "spider", "dart",
            "diskette", "bra", "horseshoe", "chocolates", "spikedchocolates", "paperclip", "glue", "glove", "redbull",
            "stinkbomb", "glowingcoal", "coal", "pliers", "perfume", "stinkperfume",
        };

        /// <summary>Own names for the security company's protections (SecurityBits order).</summary>
        public static readonly string[] SecurityNames = { "office", "laptop", "personnel", "bank", "routeboard", "administration", "planeinterior", "planeexterior", "gate" };

        public static Scenario Parse(string json)
        {
            var o = Json.ParseObject(json);
            var s = new Scenario
            {
                Mission = o.Int("mission", OpenAT.Sim.Mission.FreeGame),
                Seed = o.Int("seed", 12345),
                HomeCity = o.Str("homeCity"),
                AdvanceDays = Math.Max(0, o.Int("advanceDays")),
                SkipBriefing = o.Bool("skipBriefing"),
                Routes = Math.Max(0, o.Int("routes")),
                Orders = Math.Max(0, o.Int("orders")),
                Freight = Math.Max(0, o.Int("freight")),
                PlanFlights = o.Bool("planFlights"),
                Room = o.Str("room"),
            };
            if (o.Has("money")) s.Money = (long)o.Num("money");
            var time = o.Str("time");
            if (!string.IsNullOrEmpty(time))
            {
                var parts = time.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)) s.TimeOfDay = GameClock.At(h, m);
                else s.Warnings.Add("bad time " + time);
            }
            foreach (var v in o.Arr("items") ?? new List<object>())
            {
                int id = Lookup(ItemNames, v);
                if (id >= 0) s.Items.Add(id); else s.Warnings.Add("unknown item " + v);
            }
            var lap = o.Obj("laptop");
            if (lap != null)
            {
                if (lap.Has("quality")) s.LaptopQuality = lap.Int("quality");
                if (lap.Has("battery")) s.LaptopBattery = lap.Int("battery");
                if (lap.Has("virus")) s.LaptopVirus = lap.Int("virus");
            }
            foreach (var v in o.Arr("security") ?? new List<object>())
            {
                int id = Lookup(SecurityNames, v);
                if (id >= 0) s.Security.Add(id); else s.Warnings.Add("unknown security option " + v);
            }
            var adv = o.Obj("advisors");
            if (adv != null)
                foreach (Staff.AdvisorType t in Enum.GetValues(typeof(Staff.AdvisorType)))
                    if (adv.Has(t.ToString().ToLowerInvariant())) s.Advisors.Add(((int)t, adv.Int(t.ToString().ToLowerInvariant())));
            foreach (var v in o.Arr("planes") ?? new List<object>())
                if (v is JsonObject p) s.Planes.Add((p.Str("type"), Math.Max(1, p.Int("count", 1))));
            return s;
        }

        static int Lookup(string[] names, object v)
        {
            if (Json.TryNumber(v, out double d) && !(v is string)) return (int)d >= 0 && (int)d < names.Length ? (int)d : -1;
            var s = (v as string ?? "").Trim().Replace(" ", "").Replace("-", "").ToLowerInvariant();
            return Array.IndexOf(names, s);
        }

        /// <summary>The new-game settings this scenario starts from.</summary>
        public NewGameSettings Settings(GameData data)
        {
            var s = new NewGameSettings { Difficulty = Mission, Seed = Seed, LocalPlayer = 0, StartTime = 0 };
            if (!string.IsNullOrEmpty(HomeCity))
            {
                var c = data.CityByName(HomeCity);
                if (c != null) s.HomeCity = c.Id; else Warnings.Add("unknown city " + HomeCity);
            }
            return s;
        }

        /// <summary>Applies the scenario to a freshly started world (local player).</summary>
        public void Apply(World w)
        {
            var p = w.Local;
            for (int d = 0; d < AdvanceDays; d++)
            {
                RunToMorning(w);
                Run(w, new StartWorkingDayCommand(), "start day");
                StayInHall(w);
                for (int i = 0; i < 100; i++) w.Tick();
                Run(w, new CallItADayCommand(w.LocalPlayer), "call it a day");
            }
            if (AdvanceDays > 0) RunToMorning(w);

            if (Money.HasValue) p.Money = Money.Value;
            foreach (int item in Items)
            {
                if (item == ItemId.Laptop) { ItemEngine.BuyLaptop(w, p, free: true); continue; }
                if (!p.Items.Add(item)) Warnings.Add("inventory full for item " + ItemNames[item]);
            }
            if (LaptopQuality.HasValue)
            {
                if (!p.HasItem(ItemId.Laptop) && !p.Items.Add(ItemId.Laptop)) Warnings.Add("inventory full for the laptop");
                p.LaptopQuality = Math.Max(1, Math.Min(4, LaptopQuality.Value));
                p.LaptopBattery = ItemCatalog.LaptopBattery[p.LaptopQuality];
            }
            if (LaptopBattery.HasValue) p.LaptopBattery = LaptopBattery.Value;
            if (LaptopVirus.HasValue) p.LaptopVirus = LaptopVirus.Value;
            if (Security.Count > 0)
            {
                int flags = p.SecurityFlags;
                foreach (int b in Security) flags |= 1 << b;
                Run(w, new SetSecurityFlagsCommand(p.Index, flags), "security");
            }

            foreach (var (type, talent) in Advisors)
            {
                Staff.Worker free = null;
                foreach (var wk in w.Workers.Workers) if (wk.Type == type && !wk.IsEmployed) { free = wk; break; }
                if (free == null) { Warnings.Add("no free advisor of type " + type); continue; }
                free.Employer = p.Index;
                if (talent > 0) free.Talent = Math.Min(100, talent);
            }

            foreach (var (type, count) in Planes)
            {
                var t = FindType(w, type);
                if (t == null) { Warnings.Add("unknown plane type " + type); continue; }
                for (int i = 0; i < count; i++) Run(w, new BuyPlaneCommand(p.Index, t.Id, 1), "buy " + type);
            }
            int rented = 0;
            for (int r = 0; r < w.RouteTable.Count && rented < Routes; r++)
            {
                var route = w.RouteTable[r];
                if (route.From != w.HomeCity) continue;
                if (w.Execute(new RentRouteCommand(p.Index, r)).Ok) rented++;
            }
            if (rented < Routes) Warnings.Add("rented " + rented + " of " + Routes + " routes");
            if (Orders > 0)
            {
                Run(w, new VisitOfferBoardCommand(p.Index, OfferBoard.TravelAgency, minimum: Orders), "travel agency");
                int taken = 0;
                for (int slot = 0; slot < ContractBoard.Slots && taken < Orders; slot++)
                {
                    if (w.TravelAgency.IsEmpty(slot)) continue;
                    if (w.Execute(new AcceptContractCommand(p.Index, OfferBoard.TravelAgency, slot) { IgnoreOpeningHours = true }).Ok) taken++;
                }
                if (taken < Orders) Warnings.Add("accepted " + taken + " of " + Orders + " orders");
            }
            if (Freight > 0)
            {
                Run(w, new VisitOfferBoardCommand(p.Index, OfferBoard.FreightOffice, minimum: Freight), "cargo office");
                int taken = 0;
                for (int slot = 0; slot < FreightBoard.Slots && taken < Freight; slot++)
                    if (w.Execute(new AcceptFreightCommand(p.Index, OfferBoard.FreightOffice, slot)).Ok) taken++;
                if (taken < Freight) Warnings.Add("accepted " + taken + " of " + Freight + " freight orders");
            }
            if (PlanFlights) PlanOrders(w, p);

            if (SkipBriefing && w.Clock.State == DayState.Morning)
            {
                Run(w, new StartWorkingDayCommand(), "start day");
                StayInHall(w);
            }
            if (TimeOfDay.HasValue)
            {
                if (w.Clock.State == DayState.Morning) { Run(w, new StartWorkingDayCommand(), "start day"); StayInHall(w); }
                int date = w.Clock.Date;
                for (int i = 0; i < 2_000_000 && w.Clock.Date == date && w.Clock.Time < TimeOfDay.Value && w.Clock.State == DayState.Playing; i++) w.Tick();
            }
        }

        static PlaneType FindType(World w, string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            if (int.TryParse(type, out int id)) return w.Data.PlaneTypes.Find(id) ?? w.Data.PlaneTypeByCsvId(id);
            foreach (var t in w.Data.PlaneTypes.All)
                if (string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase) || string.Equals(t.FullName, type, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>Each open order goes into the first plane that can take it, on the first free day at 8:00.</summary>
        void PlanOrders(World w, Player p)
        {
            foreach (var c in p.Contracts.Items)
            {
                bool done = false;
                foreach (var pl in p.Planes.Items)
                {
                    for (int date = Math.Max(c.Date, w.Clock.Date); date <= c.LastDate && !done; date++)
                        done = w.Execute(new PlaceFlightCommand(p.Index, pl.Id, FlightKind.Contract, c.Id, date, 8)).Ok;
                    if (done) break;
                }
                if (!done) Warnings.Add("could not plan an order");
            }
        }

        void Run(World w, ICommand<World> cmd, string what)
        {
            var r = w.Execute(cmd);
            if (!r.Ok) Warnings.Add(what + ": " + r);
        }

        static void RunToMorning(World w)
        {
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
        }

        /// <summary>Starting the day walks the figure to its office; a scenario keeps it where it stands.</summary>
        static void StayInHall(World w)
        {
            var me = w.Hall?.PlayerPerson(w.LocalPlayer);
            if (me != null) w.Execute(new Hall.WalkToTileCommand(w.LocalPlayer, Hall.HallState.TileOfPosition(me.Position)));
        }
    }
}
