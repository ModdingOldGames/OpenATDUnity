using System.Collections.Generic;
using System.IO;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Save;

namespace OpenAT.Sim.Contracts
{
    /// <summary>One requested placement of a trial: plane and departure (date, hour).</summary>
    public struct TrialLeg
    {
        public int PlaneId, Date, Hour;
        public int RequestedAbs => Date * 24 + Hour;
    }

    /// <summary>What one placement did in the isolated copy.</summary>
    public sealed class TrialLegResult
    {
        public TrialLeg Leg;
        public bool Ok;
        /// <summary>Command refusal reason when not <see cref="Ok"/>.</summary>
        public string Reason;
        /// <summary>Resulting departure of the placed flight (absolute hour), −1 when refused.</summary>
        public int StartAbs = -1, LandAbs = -1;
        /// <summary>Entry validity of the placed flight (<see cref="EntryOkay"/>: wrong day, too small, ...).</summary>
        public int Okay;
        /// <summary>The flight's departure has no free gate at home.</summary>
        public bool GateWarning;
        public int FerriesAdded, FerriesRemoved, OtherFlightsMoved;
        /// <summary>Kind and object of an existing flight the placement took off the plan (the planner's swap), 0 = none.</summary>
        public int PickedUpKind, PickedUpId;
        /// <summary>Tons this placement carries (freight).</summary>
        public int Tons;
    }

    /// <summary>The result of evaluating a trial against a copy of the live world.</summary>
    public sealed class TrialResult
    {
        /// <summary>The isolated copy after acceptance and placements (render plans from here; never merge back).</summary>
        public World Sandbox;
        /// <summary>The offer could not be taken in the copy (closed counter, slot gone, ...).</summary>
        public string AcceptFailure;
        /// <summary>Id of the contract / freight order in the copy.</summary>
        public int ObjectId = -1;
        public readonly List<TrialLegResult> Legs = new List<TrialLegResult>();
        /// <summary>Freight: total tons and tons still neither flown nor planned in the copy.</summary>
        public int Tons, TonsOpen;
        /// <summary>Gate warnings on future flights of all own planes: live and in the copy.</summary>
        public int GateWarningsBefore, GateWarningsAfter;

        public bool AllOk { get { if (AcceptFailure != null) return false; foreach (var l in Legs) if (!l.Ok) return false; return true; } }
    }

    /// <summary>
    /// "Try in planner" at the order counters (OpenAT planner improvements). It evaluates taking an offer and
    /// placing it in one or more planes against an isolated copy of the world, made by an in-memory save/load
    /// round trip. The live world is never changed: no acceptance, no plan or gate change, no events, money,
    /// clock or random state. The copy runs the same commands the game uses, so its result is the
    /// live result as long as the live state has not changed since. <see cref="Apply"/> checks that first.
    /// </summary>
    public sealed class OfferTrial
    {
        public readonly int Player;
        public readonly OfferBoard Board;
        public readonly int Slot;
        public readonly OfferFingerprint Offer;
        public readonly List<TrialLeg> Legs = new List<TrialLeg>();

        public bool Freight => Offer.Freight;
        int Kind => Freight ? FlightKind.Freight : FlightKind.Contract;

        public OfferTrial(World w, int player, OfferBoard board, int slot)
        {
            Player = player; Board = board; Slot = slot;
            Offer = OfferFingerprint.Of(w, board, slot);
        }

        /// <summary>True while the board still shows this offer in its slot.</summary>
        public bool OfferStillThere(World live) => Offer != null && Offer.SameAs(OfferFingerprint.Of(live, Board, Slot));

        /// <summary>A copy of the world by save/load in memory (shares only the read-only game data).</summary>
        public static World Isolate(World live)
        {
            using (var ms = new MemoryStream())
            {
                SaveGame.Write(live, ms, "trial");
                ms.Position = 0;
                return SaveGame.Read(ms, live.Data);
            }
        }

        static int GateWarnings(World w, Player p)
        {
            int n = 0, now = w.Clock.Date * 24 + w.Clock.Hour;
            foreach (var pl in p.Planes.Items)
                foreach (var e in pl.Plan.Entries)
                    if (e.Kind != 0 && e.StartAbs > now && e.GateWarning) n++;
            return n;
        }

        /// <summary>
        /// Takes the offer in a fresh copy of <paramref name="live"/> (or uses the already accepted object
        /// <paramref name="acceptedId"/>) and places every leg in order with <see cref="PlaceFlightCommand"/>.
        /// </summary>
        public TrialResult Evaluate(World live, int acceptedId = -1)
        {
            var r = new TrialResult { Sandbox = Isolate(live) };
            var w = r.Sandbox;
            var p = w.Players[Player];
            r.GateWarningsBefore = GateWarnings(live, live.Players[Player]);
            if (acceptedId >= 0) r.ObjectId = acceptedId;
            else if (Offer == null || !Offer.SameAs(OfferFingerprint.Of(w, Board, Slot))) r.AcceptFailure = "offer-gone";
            else if (Freight)
            {
                var acc = new AcceptFreightCommand(Player, Board, Slot);
                var ok = w.Execute(acc);
                if (ok.Ok) r.ObjectId = acc.FreightId; else r.AcceptFailure = ok.Reason;
            }
            else
            {
                var acc = new AcceptContractCommand(Player, Board, Slot);
                var ok = w.Execute(acc);
                if (ok.Ok) r.ObjectId = acc.ContractId; else r.AcceptFailure = ok.Reason;
            }
            if (r.AcceptFailure == null)
                foreach (var leg in Legs) r.Legs.Add(PlaceLeg(w, p, leg, r.ObjectId));
            if (Freight && p.Freights.Find(r.ObjectId) is FreightContract f) { r.Tons = f.Tons; r.TonsOpen = f.TonsOpen; }
            r.GateWarningsAfter = GateWarnings(w, p);
            return r;
        }

        TrialLegResult PlaceLeg(World w, Player p, TrialLeg leg, int objectId)
        {
            var res = new TrialLegResult { Leg = leg };
            var pl = p.Planes.Find(leg.PlaneId);
            var before = pl != null ? (FlightEntry[])pl.Plan.Entries.Clone() : null;
            int tonsBefore = Freight ? p.Freights.Find(objectId)?.TonsOpen ?? 0 : 0;
            var cmd = new PlaceFlightCommand(Player, leg.PlaneId, Kind, objectId, leg.Date, leg.Hour) { ClickDate = leg.Date, ClickHour = leg.Hour };
            var ok = w.Execute(cmd);
            res.Ok = ok.Ok;
            res.Reason = ok.Reason;
            if (!ok.Ok) return res;
            res.PickedUpKind = cmd.PickedUpKind == FlightKind.Auto ? 0 : cmd.PickedUpKind;
            res.PickedUpId = res.PickedUpKind != 0 ? cmd.PickedUpId : 0;
            var after = pl.Plan.Entries;
            // The placed flight is this object's entry that was not in the plan before.
            foreach (var e in after)
            {
                if (e.Kind != Kind || e.ObjectId != objectId) continue;
                bool old = false;
                foreach (var o in before) if (o.Kind == Kind && o.ObjectId == objectId && o.StartAbs == e.StartAbs) { old = true; break; }
                if (old) continue;
                res.StartAbs = e.StartAbs; res.LandAbs = e.LandAbs; res.Okay = e.Okay; res.GateWarning = e.GateWarning;
                if (Freight) res.Tons = e.Passengers;
                break;
            }
            int autoBefore = 0, autoAfter = 0;
            foreach (var e in before) if (e.Kind == FlightKind.Auto) autoBefore++;
            foreach (var e in after) if (e.Kind == FlightKind.Auto) autoAfter++;
            if (autoAfter > autoBefore) res.FerriesAdded = autoAfter - autoBefore; else res.FerriesRemoved = autoBefore - autoAfter;
            foreach (var e in before)
            {
                if (e.Kind == 0 || e.Kind == FlightKind.Auto || (e.Kind == Kind && e.ObjectId == objectId)) continue;
                bool same = false, exists = false;
                foreach (var a in after)
                {
                    if (a.Kind != e.Kind || a.ObjectId != e.ObjectId) continue;
                    exists = true;
                    if (a.StartAbs == e.StartAbs) { same = true; break; }
                }
                if (exists && !same) res.OtherFlightsMoved++;
            }
            if (Freight && res.Tons == 0) res.Tons = tonsBefore - (p.Freights.Find(objectId)?.TonsOpen ?? 0);
            return res;
        }

        /// <summary>Why a trial cannot be applied to the live plans, or null when it can.</summary>
        public static string ApplyBlocker(TrialResult check)
        {
            if (check.AcceptFailure != null) return check.AcceptFailure;
            if (check.Legs.Count == 0) return "no-legs";
            foreach (var l in check.Legs)
            {
                if (!l.Ok) return l.Reason ?? "refused";
                if (l.PickedUpKind != 0) return "replaces-flight";
            }
            return null;
        }

        /// <summary>
        /// Puts the trial's legs into the live plans for the already accepted object <paramref name="acceptedId"/>.
        /// All or nothing: the legs are first checked on a fresh copy of the current live state. They are
        /// applied only when every leg succeeds there without taking another flight off a plan, and when
        /// <paramref name="expected"/> (the result the player saw) still matches: same departures, ferries
        /// and moved flights. Otherwise nothing changes and the reason is returned. Returns null on success.
        /// </summary>
        public string Apply(World live, int acceptedId, TrialResult expected)
        {
            var check = Evaluate(live, acceptedId);
            string blocker = ApplyBlocker(check);
            if (blocker != null) return blocker;
            if (expected != null)
            {
                if (expected.Legs.Count != check.Legs.Count) return "changed";
                for (int i = 0; i < check.Legs.Count; i++)
                {
                    var a = expected.Legs[i]; var b = check.Legs[i];
                    if (a.StartAbs != b.StartAbs || a.FerriesAdded != b.FerriesAdded || a.FerriesRemoved != b.FerriesRemoved || a.OtherFlightsMoved != b.OtherFlightsMoved || a.Okay != b.Okay)
                        return "changed";
                }
            }
            // The copy and the live world run the same deterministic commands from the same state, so every
            // live placement succeeds as it did in the copy. A refusal here (should never happen) takes back the
            // legs already placed, so the plan is never left half applied.
            var placed = new List<(int plane, int start)>();
            foreach (var leg in Legs)
            {
                var cmd = new PlaceFlightCommand(Player, leg.PlaneId, Kind, acceptedId, leg.Date, leg.Hour) { ClickDate = leg.Date, ClickHour = leg.Hour };
                var before = (FlightEntry[])live.Players[Player].Planes.Find(leg.PlaneId).Plan.Entries.Clone();
                if (!live.Execute(cmd).Ok || (cmd.PickedUpKind != 0 && cmd.PickedUpKind != FlightKind.Auto))
                {
                    Undo(live, placed, acceptedId);
                    return "refused";
                }
                var pl = live.Players[Player].Planes.Find(leg.PlaneId);
                foreach (var e in pl.Plan.Entries)
                {
                    if (e.Kind != Kind || e.ObjectId != acceptedId) continue;
                    bool old = false;
                    foreach (var o in before) if (o.Kind == Kind && o.ObjectId == acceptedId && o.StartAbs == e.StartAbs) { old = true; break; }
                    if (!old) { placed.Add((leg.PlaneId, e.StartAbs)); break; }
                }
            }
            return null;
        }

        void Undo(World live, List<(int plane, int start)> placed, int objectId)
        {
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                var pl = live.Players[Player].Planes.Find(placed[i].plane);
                if (pl == null) continue;
                for (int k = 0; k < FlightPlan.Size; k++)
                    if (pl.Plan.Entries[k].Kind == Kind && pl.Plan.Entries[k].ObjectId == objectId && pl.Plan.Entries[k].StartAbs == placed[i].start)
                    {
                        live.Execute(new RemoveFlightCommand(Player, pl.Id, k));
                        break;
                    }
            }
        }
    }
}
