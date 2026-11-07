using System;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Finance;

namespace OpenAT.Sim.Fleet
{
    /// <summary>Common base for operations commands issued by one airline.</summary>
    public abstract class PlayerCommand : ICommand<World>
    {
        public int PlayerIndex;
        protected PlayerCommand(int player) { PlayerIndex = player; }
        protected Player P(World w) => w.Players[PlayerIndex];
        public CommandResult Validate(World w)
        {
            if (PlayerIndex < 0 || PlayerIndex >= Player.Count) return CommandResult.Fail("no-player");
            if (w.Players[PlayerIndex].IsOut) return CommandResult.Fail("player-out");
            return Check(w, w.Players[PlayerIndex]);
        }
        public void Apply(World w) => Do(w, w.Players[PlayerIndex]);
        protected abstract CommandResult Check(World w, Player p);
        protected abstract void Do(World w, Player p);
    }

    /// <summary>Quantities the broker and designer offer (planes-and-flights.md §4.1).</summary>
    public static class PurchaseQuantities
    {
        public static readonly int[] Allowed = { 1, 2, 3, 5, 10 };
        public static bool IsAllowed(int n) => Array.IndexOf(Allowed, n) >= 0;
    }

    /// <summary>Buy new planes at the broker (planes-and-flights.md §4.1). Names share one generator seeded with Date.</summary>
    public sealed class BuyPlaneCommand : PlayerCommand
    {
        /// <summary>Plane type csv id (100..126) or runtime id.</summary>
        public int TypeId;
        public int Quantity = 1;
        public BuyPlaneCommand(int player, int typeId, int quantity = 1) : base(player) { TypeId = typeId; Quantity = quantity; }

        PlaneType Type(World w) => w.Data.PlaneTypes.Find(TypeId) ?? w.Data.PlaneTypeByCsvId(TypeId);

        protected override CommandResult Check(World w, Player p)
        {
            var t = Type(w);
            if (t == null) return CommandResult.Fail("unknown-type");
            if (!PlaneMarket.IsAvailable(w, t)) return CommandResult.Fail("not-available");
            if (!PurchaseQuantities.IsAllowed(Quantity)) return CommandResult.Fail("bad-quantity");
            if (!p.CanAfford((long)t.Price * Quantity)) return CommandResult.Fail("too-expensive");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var t = Type(w);
            var names = new TeakRand(w.Clock.Date);
            for (int i = 0; i < Quantity; i++)
            {
                var pl = PlaneMarket.BuyNew(w, p, t, names);
                w.Events.Publish(new PlaneTraded { Player = p, Plane = pl, Price = t.Price, Source = "broker" });
            }
            w.Rules.Get<IPurchaseRebate>().Apply(w, p, (long)t.Price * Quantity);
            w.Rules.Get<ICrewMapping>().MapCrew(w, p);
        }
    }

    /// <summary>Buy planes built from a designer blueprint (planes-and-flights.md §5.1, §5.5).</summary>
    public sealed class BuyDesignedPlaneCommand : PlayerCommand
    {
        public PlaneDesign Design;
        public int Quantity = 1;
        public BuyDesignedPlaneCommand(int player, PlaneDesign design, int quantity = 1) : base(player) { Design = design; Quantity = quantity; }

        protected override CommandResult Check(World w, Player p)
        {
            if (Design == null || !Design.IsBuildable(w.DesignCatalog)) return CommandResult.Fail("not-buildable");
            if (!PurchaseQuantities.IsAllowed(Quantity)) return CommandResult.Fail("bad-quantity");
            if (!p.CanAfford(Design.Stats(w.DesignCatalog).Cost * Quantity)) return CommandResult.Fail("too-expensive");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var names = new TeakRand(w.Clock.Date);
            long cost = Design.Stats(w.DesignCatalog).Cost;
            for (int i = 0; i < Quantity; i++)
            {
                var pl = PlaneMarket.BuyDesigned(w, p, Design, names);
                w.Events.Publish(new PlaneTraded { Player = p, Plane = pl, Price = cost, Source = "designer" });
            }
            w.Rules.Get<IPurchaseRebate>().Apply(w, p, cost * Quantity);
            w.Rules.Get<ICrewMapping>().MapCrew(w, p);
        }
    }

    /// <summary>Museum: refill emptied offers when entering (planes-and-flights.md §4.3).</summary>
    public sealed class EnterMuseumCommand : PlayerCommand
    {
        public EnterMuseumCommand(int player) : base(player) { }
        protected override CommandResult Check(World w, Player p) => CommandResult.Success;
        protected override void Do(World w, Player p)
        {
            PlaneMarket.RefillUsedPlanes(w);
            p.VisitedMuseumToday = true;
        }
    }

    /// <summary>Opening rule of the museum: closed on weekdays 5 and 6 (planes-and-flights.md §4.3).</summary>
    public static class MuseumHours
    {
        public static bool IsOpen(World w) => w.Clock.Weekday != 5 && w.Clock.Weekday != 6;
    }

    /// <summary>Buy a used plane at the museum (planes-and-flights.md §4.3): price = market value; worst condition = condition − 20.</summary>
    public sealed class BuyUsedPlaneCommand : PlayerCommand
    {
        public int Slot;
        public BuyUsedPlaneCommand(int player, int slot) : base(player) { Slot = slot; }

        protected override CommandResult Check(World w, Player p)
        {
            if (!MuseumHours.IsOpen(w)) return CommandResult.Fail("closed");
            if (Slot < 0 || Slot >= w.UsedPlanes.Length || w.UsedPlanes[Slot] == null) return CommandResult.Fail("no-offer");
            if (!p.CanAfford(w.UsedPlanes[Slot].MarketValue)) return CommandResult.Fail("too-expensive");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var offer = w.UsedPlanes[Slot];
            int price = offer.MarketValue;
            var pl = offer.Clone();
            pl.WorstCondition = pl.Condition - 20;
            p.Planes.Add(pl);
            w.UsedPlanes[Slot] = null;
            w.MuseumRefillTicks = 0;
            p.Book(-price, MoneyReason.PlaneBought, pl.Name);
            PlaneMarket.SortFleet(p);
            w.Rules.Get<IPurchaseRebate>().Apply(w, p, price);
            w.Rules.Get<ICrewMapping>().MapCrew(w, p);
            w.Events.Publish(new PlaneTraded { Player = p, Plane = pl, Price = price, Source = "museum" });
        }
    }

    /// <summary>Sell a plane at the museum (planes-and-flights.md §4.4): market value × 9/10, blocked while flights are booked.</summary>
    public sealed class SellPlaneCommand : PlayerCommand
    {
        public int PlaneId;
        public bool IgnoreOpeningHours;
        public SellPlaneCommand(int player, int planeId) : base(player) { PlaneId = planeId; }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (!IgnoreOpeningHours && !MuseumHours.IsOpen(w)) return CommandResult.Fail("closed");
            if (!FlightPlanner.CanBeSold(w, pl)) return CommandResult.Fail("flights-planned");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            int price = PlaneMarket.SalePrice(pl);
            p.Book(price, MoneyReason.PlaneSold, pl.Name);
            p.Planes.Remove(PlaneId);
            w.Rules.Get<ICrewMapping>().MapCrew(w, p);
            ContractUsage.UpdateContracts(w, p);
            ContractUsage.UpdateFreight(w, p);
            Airport.GatePlanner.PlanGates(w, p);
            w.Events.Publish(new PlaneTraded { Player = p, Plane = pl, Price = price, Sold = true, Source = "museum" });
        }
    }

    /// <summary>Rename a plane (at most 20 characters, planes-and-flights.md §3.1).</summary>
    public sealed class RenamePlaneCommand : PlayerCommand
    {
        public int PlaneId;
        public string Name;
        public RenamePlaneCommand(int player, int planeId, string name) : base(player) { PlaneId = planeId; Name = name; }

        protected override CommandResult Check(World w, Player p)
        {
            if (p.Planes.Find(PlaneId) == null) return CommandResult.Fail("no-plane");
            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 20) return CommandResult.Fail("bad-name");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p) => p.Planes.Find(PlaneId).Name = Name;
    }

    /// <summary>
    /// Choose an equipment level in the equipment room (planes-and-flights.md §6.2): refused (std text
    /// Sched 1810) if the pending changes of the whole fleet would exceed the money. Food has no cost.
    /// </summary>
    public sealed class SetEquipmentCommand : PlayerCommand
    {
        public int PlaneId, Item, Level;
        public SetEquipmentCommand(int player, int planeId, int item, int level) : base(player) { PlaneId = planeId; Item = item; Level = level; }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (Item < 0 || Item >= Equip.Count || Level < 0 || Level > 2) return CommandResult.Fail("bad-level");
            if (Item == Equip.Food) return CommandResult.Success;
            int back = pl.EquipmentTarget[Item];
            pl.EquipmentTarget[Item] = Level;
            long sum = FlightOps.PendingRefitCost(p);
            pl.EquipmentTarget[Item] = back;
            return sum > p.Money ? CommandResult.Fail("too-expensive") : CommandResult.Success;
        }

        protected override void Do(World w, Player p) => p.Planes.Find(PlaneId).EquipmentTarget[Item] = Level;
    }

    /// <summary>
    /// First-class split ±10 % (planes-and-flights.md §6.4): refused if a count would be negative or the
    /// seats would no longer hold an open contract or freight flight. No cost.
    /// </summary>
    public sealed class ChangeFirstClassCommand : PlayerCommand
    {
        public int PlaneId;
        public bool More;
        public ChangeFirstClassCommand(int player, int planeId, bool more) : base(player) { PlaneId = planeId; More = more; }

        public static bool Compute(Plane pl, bool more, out int eco, out int fc)
        {
            long total = pl.MaxPassengers + pl.MaxPassengersFC * 2;
            eco = pl.MaxPassengers; fc = pl.MaxPassengersFC;
            if (total <= 0) return false;
            long pct = pl.MaxPassengersFC * 2 * 100 / total;
            pct = (pct + 5) / 10 * 10;
            pct += more ? 10 : -10;
            long nfc = total * pct / 2 / 100;
            long neco = total - nfc * 2;
            if (neco == pl.MaxPassengers)
            {
                if (more) { nfc++; neco -= 2; } else { nfc--; neco += 2; }
            }
            eco = (int)neco; fc = (int)nfc;
            return nfc >= 0 && neco >= 0;
        }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (!Compute(pl, More, out int eco, out int fc)) return CommandResult.Fail("out-of-range");
            if (eco + fc < FlightPlanner.MaxPassengerOpenFlight(w, p, pl)) return CommandResult.Fail("open-flight-too-big");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            Compute(pl, More, out int eco, out int fc);
            pl.MaxPassengers = eco;
            pl.MaxPassengersFC = fc;
        }
    }

    /// <summary>Desired attendants +/−1 between the type value and twice it (planes-and-flights.md §6.5).</summary>
    public sealed class ChangeAttendantsCommand : PlayerCommand
    {
        public int PlaneId, Delta;
        public ChangeAttendantsCommand(int player, int planeId, int delta) : base(player) { PlaneId = planeId; Delta = delta; }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            int n = pl.MaxAttendants + Math.Sign(Delta);
            if (n < pl.Attendants || n > pl.Attendants * 2) return CommandResult.Fail("out-of-range");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            p.Planes.Find(PlaneId).MaxAttendants += Math.Sign(Delta);
            w.Rules.Get<ICrewMapping>().MapCrew(w, p);
        }
    }

    /// <summary>
    /// Copy / paste of the equipment configuration (planes-and-flights.md §6.6). Paste applies only the
    /// four technical items (QUIRK) unless <see cref="GameOptions.FixPasteCabinEquipment"/> is set.
    /// </summary>
    public sealed class CopyPasteEquipmentCommand : PlayerCommand
    {
        public int PlaneId;
        public bool Paste;
        public CopyPasteEquipmentCommand(int player, int planeId, bool paste) : base(player) { PlaneId = planeId; Paste = paste; }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (!Paste) return CommandResult.Success;
            if (p.EquipmentClipboard == null) return CommandResult.Fail("nothing-copied");
            var back = (int[])pl.EquipmentTarget.Clone();
            ApplyPaste(w, p, pl);
            long sum = FlightOps.PendingRefitCost(p);
            Array.Copy(back, pl.EquipmentTarget, Equip.Count);
            return sum > p.Money ? CommandResult.Fail("too-expensive") : CommandResult.Success;
        }

        static void ApplyPaste(World w, Player p, Plane pl)
        {
            int first = w.Options.FixPasteCabinEquipment ? 0 : Equip.Engines;
            for (int i = first; i < Equip.Count; i++) pl.EquipmentTarget[i] = p.EquipmentClipboard[i];
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (!Paste)
            {
                p.EquipmentClipboard = (int[])pl.EquipmentTarget.Clone();
                p.AttendantRatioClipboard = pl.Attendants > 0 ? (double)pl.MaxAttendants / pl.Attendants : 1;
                return;
            }
            ApplyPaste(w, p, pl);
        }
    }

    /// <summary>Mechanic: repair target 0..100 in steps of 5 for one plane, or all (<see cref="PlaneId"/> 0) (planes-and-flights.md §7.2).</summary>
    public sealed class SetRepairTargetCommand : PlayerCommand
    {
        public int PlaneId, Target;
        public SetRepairTargetCommand(int player, int planeId, int target) : base(player) { PlaneId = planeId; Target = target; }

        protected override CommandResult Check(World w, Player p)
        {
            if (Target < 0 || Target > 100 || Target % 5 != 0) return CommandResult.Fail("bad-target");
            if (PlaneId != 0 && p.Planes.Find(PlaneId) == null) return CommandResult.Fail("no-plane");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            foreach (var pl in p.Planes.Items)
                if (PlaneId == 0 || pl.Id == PlaneId) pl.TargetCondition = Target;
        }
    }

    // ------------------------------------------------------------------ flight planning

    /// <summary>
    /// Place a route, contract or freight post-it in a plane's plan (planes-and-flights.md §10.1).
    /// Refusals: too far / ≥ 24 h (Advi 2310), contract done (2311) or already planned (2312). Start
    /// today is forced to ≥ hour + 2. A future non-automatic flight covering the click time is replaced
    /// and picked up (<see cref="PickedUpKind"/>/<see cref="PickedUpId"/> after Apply); otherwise the last
    /// slot is used (overwritten when the plan is full).
    /// </summary>
    public sealed class PlaceFlightCommand : PlayerCommand
    {
        public int PlaneId, Kind, ObjectId, StartDate, StartHour;
        /// <summary>Click position (defaults to the start); the covered flight there is replaced.</summary>
        public int? ClickDate, ClickHour;
        public int PickedUpKind, PickedUpId;

        public PlaceFlightCommand(int player, int planeId, int kind, int objectId, int startDate, int startHour) : base(player)
        { PlaneId = planeId; Kind = kind; ObjectId = objectId; StartDate = startDate; StartHour = startHour; }

        bool Cities(World w, Player p, out int from, out int to)
        {
            from = to = 0;
            switch (Kind)
            {
                case FlightKind.Route:
                    var r = w.RouteById(ObjectId);
                    if (r == null) return false;
                    from = r.From; to = r.To; return true;
                case FlightKind.Contract:
                    var c = p.Contracts.Find(ObjectId);
                    if (c == null) return false;
                    from = c.From; to = c.To; return true;
                case FlightKind.Freight:
                    var f = p.Freights.Find(ObjectId);
                    if (f == null) return false;
                    from = f.From; to = f.To; return true;
            }
            return false;
        }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (!Cities(w, p, out int from, out int to)) return CommandResult.Fail("no-object");
            if (Kind == FlightKind.Route)
            {
                int ri = w.RouteIndexOf(ObjectId);
                if (!p.RentRoutes[ri].Licensed) return CommandResult.Fail("route-not-rented");
            }
            if (w.Distance(from, to) > pl.Range * 1000) return CommandResult.Fail("too-far");
            if (FlightPlanner.Duration(w, pl, from, to) >= 24) return CommandResult.Fail("too-far");
            if (Kind == FlightKind.Contract)
            {
                var c = p.Contracts.Find(ObjectId);
                if (c.InPlan == PlanState.Done) return CommandResult.Fail("contract-done");
                if (c.InPlan == PlanState.Planned) return CommandResult.Fail("contract-planned");
            }
            int date = StartDate, hour = StartHour;
            Normalise(ref date, ref hour);
            if (date < w.Clock.Date || date >= w.Clock.Date + 7) return CommandResult.Fail("outside-week");
            return CommandResult.Success;
        }

        static void Normalise(ref int date, ref int hour)
        {
            while (hour < 0) { hour += 24; date--; }
            while (hour > 23) { hour -= 24; date++; }
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            int date = StartDate, hour = StartHour;
            Normalise(ref date, ref hour);
            if (date == w.Clock.Date && hour <= w.Clock.Hour + 1) hour = w.Clock.Hour + 2;
            int cd = ClickDate ?? StartDate, ch = ClickHour ?? StartHour;
            Normalise(ref cd, ref ch);

            var f = pl.Plan.Entries;
            int c;
            for (c = FlightPlan.Size - 1; c >= 0; c--)
            {
                if (f[c].Kind == 0) continue;
                if ((f[c].StartDate < cd || (f[c].StartDate == cd && f[c].StartHour <= ch)) &&
                    (f[c].LandDate > cd || (f[c].LandDate == cd && f[c].LandHour >= ch)) && f[c].Kind != FlightKind.Auto)
                    break;
            }
            PickedUpKind = 0; PickedUpId = 0;
            if (c >= 0 && f[c].IsFuture(w.Clock.Date, w.Clock.Hour))
            {
                PickedUpKind = f[c].Kind;
                PickedUpId = f[c].ObjectId;
            }
            else c = FlightPlan.Size - 1;

            ref var e = ref f[c];
            e.Okay = EntryOkay.Ok;
            e.StartDate = date;
            e.StartHour = hour;
            e.Kind = Kind;
            e.ObjectId = ObjectId;
            if (Kind == FlightKind.Route)
            {
                var rr = p.RentRoutes[w.RouteIndexOf(ObjectId)];
                e.TicketPrice = rr.TicketPrice;
                e.TicketPriceFC = rr.TicketPriceFC;
            }
            FlightPlanner.RefreshCities(w, p, ref e);
            e.HoursBefore = ((e.StartDate - w.Clock.Date) * 24 + (e.StartHour - w.Clock.Hour)) & 0xFF;
            w.Rules.Get<IRoutePassengerRule>().Calculate(w, p, pl, ref e);
            e.PArrived = 0;
            FlightPlanner.AfterEdit(w, p, pl);
        }
    }

    /// <summary>Pick up (remove) a future, non-automatic flight from a plan (planes-and-flights.md §10.1).</summary>
    public sealed class RemoveFlightCommand : PlayerCommand
    {
        public int PlaneId, EntryIndex;
        public RemoveFlightCommand(int player, int planeId, int entryIndex) : base(player) { PlaneId = planeId; EntryIndex = entryIndex; }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (EntryIndex < 0 || EntryIndex >= FlightPlan.Size) return CommandResult.Fail("bad-entry");
            var e = pl.Plan.Entries[EntryIndex];
            if (e.Kind == 0 || e.Kind == FlightKind.Auto) return CommandResult.Fail("bad-entry");
            if (!e.IsFuture(w.Clock.Date, w.Clock.Hour)) return CommandResult.Fail("too-late");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            pl.Plan.Entries[EntryIndex].Kind = FlightKind.None;
            FlightPlanner.AfterEdit(w, p, pl);
        }
    }

    /// <summary>"Clear plan" (planes-and-flights.md §10.1): deletes every future entry, validates without sorting.</summary>
    public sealed class ClearPlanCommand : PlayerCommand
    {
        public int PlaneId;
        public ClearPlanCommand(int player, int planeId) : base(player) { PlaneId = planeId; }
        protected override CommandResult Check(World w, Player p) => p.Planes.Find(PlaneId) == null ? CommandResult.Fail("no-plane") : CommandResult.Success;

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            var f = pl.Plan.Entries;
            for (int c = FlightPlan.Size - 1; c >= 0; c--)
                if (f[c].Kind != 0 && f[c].IsFuture(w.Clock.Date, w.Clock.Hour)) f[c].Kind = FlightKind.None;
            pl.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            ContractUsage.UpdateContracts(w, p);
            ContractUsage.UpdateFreight(w, p);
            FlightPlanner.Check(w, p, pl, false);
        }
    }

    /// <summary>"Extend plan" button (planes-and-flights.md §10.4).</summary>
    public sealed class ExtendPlanCommand : PlayerCommand
    {
        public int PlaneId;
        public ExtendPlanCommand(int player, int planeId) : base(player) { PlaneId = planeId; }
        protected override CommandResult Check(World w, Player p) => p.Planes.Find(PlaneId) == null ? CommandResult.Fail("no-plane") : CommandResult.Success;

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            pl.Plan.UpdatePointers(w.Clock.Date, w.Clock.Hour);
            FlightPlanner.Extend(w, p, pl);
            ContractUsage.UpdateContracts(w, p);
            ContractUsage.UpdateFreight(w, p);
            FlightPlanner.Check(w, p, pl, false);
        }
    }
}
