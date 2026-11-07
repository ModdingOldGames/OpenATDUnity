using OpenAT.Sim.Core;

namespace OpenAT.Sim.Fleet
{
    /// <summary>
    /// Planner edge drag (rooms-and-ui-5b §6.5): moves a placed, unlocked route/contract/freight flight
    /// to a new start. The start is clamped to the planner week (today .. today+6, today ≥ hour + 2;
    /// hour 24 rolls to the next day, or 23 on the last day). With <see cref="Chain"/> every later flight
    /// keeps its offset (Shift in the original). Passengers are recomputed, then the plan is validated.
    /// </summary>
    public sealed class MoveFlightCommand : PlayerCommand
    {
        public int PlaneId, EntryIndex, StartDate, StartHour;
        public bool Chain;
        /// <summary>Clamped start actually used (after Apply).</summary>
        public int NewDate, NewHour;

        public MoveFlightCommand(int player, int planeId, int entryIndex, int startDate, int startHour, bool chain = false) : base(player)
        { PlaneId = planeId; EntryIndex = entryIndex; StartDate = startDate; StartHour = startHour; Chain = chain; }

        /// <summary>The planner's clamp of a start (date, hour) to the editable week.</summary>
        public static void Clamp(World w, ref int date, ref int hour)
        {
            int today = w.Clock.Date, now = w.Clock.Hour;
            while (hour < 0) { hour += 24; date--; }
            while (hour > 23) { hour -= 24; date++; }
            if (date < today) { date = today; hour = 0; }
            if (date == today && hour < now + 2)
            {
                hour = now + 2;
                if (hour > 23) { hour -= 24; date++; }
            }
            if (date > today + 6) { date = today + 6; hour = 23; }
        }

        protected override CommandResult Check(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            if (pl == null) return CommandResult.Fail("no-plane");
            if (EntryIndex < 0 || EntryIndex >= FlightPlan.Size) return CommandResult.Fail("bad-entry");
            var e = pl.Plan.Entries[EntryIndex];
            if (e.Kind != FlightKind.Route && e.Kind != FlightKind.Contract && e.Kind != FlightKind.Freight) return CommandResult.Fail("bad-entry");
            if (!e.IsFuture(w.Clock.Date, w.Clock.Hour)) return CommandResult.Fail("too-late");
            return CommandResult.Success;
        }

        protected override void Do(World w, Player p)
        {
            var pl = p.Planes.Find(PlaneId);
            var f = pl.Plan.Entries;
            int date = StartDate, hour = StartHour;
            Clamp(w, ref date, ref hour);
            NewDate = date; NewHour = hour;
            int oldAbs = f[EntryIndex].StartAbs;
            int delta = date * 24 + hour - oldAbs;
            if (delta == 0) return;
            for (int i = 0; i < FlightPlan.Size; i++)
            {
                bool move = i == EntryIndex || (Chain && f[i].Kind != FlightKind.None && f[i].Kind != FlightKind.Auto
                    && f[i].StartAbs > oldAbs && f[i].IsFuture(w.Clock.Date, w.Clock.Hour));
                if (!move) continue;
                ref var e = ref f[i];
                int abs = e.StartAbs + delta;
                e.StartDate = abs / 24;
                e.StartHour = abs % 24;
                int land = e.LandAbs + delta;
                e.LandDate = land / 24;
                e.LandHour = land % 24;
                e.HoursBefore = ((e.StartDate - w.Clock.Date) * 24 + (e.StartHour - w.Clock.Hour)) & 0xFF;
                if (e.Kind == FlightKind.Freight) e.Okay = EntryOkay.Ok;
                w.Rules.Get<IRoutePassengerRule>().Calculate(w, p, pl, ref e);
            }
            FlightPlanner.AfterEdit(w, p, pl);
        }
    }
}
