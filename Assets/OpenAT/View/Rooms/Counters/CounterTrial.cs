using System;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Planner;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;
using PT = OpenAT.View.Rooms.Planner.PlannerTexts;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// The placement trial of the counters' planner panel (OpenAT). The tried offer is a hypothetical post-it.
    /// It can be placed on an hour of any own plane's week, picked up again and placed elsewhere; freight can
    /// go on several flights. Each change is evaluated by <see cref="OfferTrial"/> in an isolated copy of the
    /// world, and the panel draws that copy. The live game is never touched until the player takes the
    /// offer (the normal card click, or "Take and plan"). After that, "Apply" puts the trial's flights into the real plans,
    /// all or nothing, but only if a fresh check still gives what the panel showed.
    /// </summary>
    public abstract partial class OrderCounterRoom
    {
        static readonly Color32 TrialFrame = new Color32(255, 255, 255, 255), TrialFrame2 = new Color32(220, 40, 160, 255);
        static readonly Color32 ShiftFrame = new Color32(255, 150, 0, 255), Ghost = new Color32(255, 255, 255, 110);

        OfferTrial _trial;
        TrialResult _result;
        /// <summary>The hypothetical post-it is on the cursor.</summary>
        bool _tryCarry;
        /// <summary>Live id of the tried offer once the player took it, −1 before.</summary>
        int _acceptedId = -1;
        bool _applied;
        string _trialMsg;
        long _stamp;

        World TrialWorld => _result != null && _result.AcceptFailure == null ? _result.Sandbox : null;
        bool TrialAccepted => _acceptedId >= 0;
        int TrialKind => _tryOffer != null && _tryOffer.Freight ? FlightKind.Freight : FlightKind.Contract;

        bool IsTried(int slot) => _panelOpen && _trial != null && slot == _trySlot && !TrialAccepted && OfferThere;

        void OnTrialOpened()
        {
            _trial = new OfferTrial(W, P.Index, Board, _trySlot);
            _result = null; _acceptedId = -1; _applied = false; _trialMsg = null;
            _tryCarry = true;
            Evaluate();
        }

        void OnTrialClosed() { _trial = null; _result = null; _tryCarry = false; _acceptedId = -1; _applied = false; _trialMsg = null; }

        void OnTrialPlaneChanged() { }

        /// <summary>The player took the tried offer with the normal card click: the trial now refers to the live order.</summary>
        void OnTriedOfferAccepted(int id)
        {
            _acceptedId = id;
            _tryCarry = false;
            _trialMsg = null;
            Evaluate();
        }

        // ------------------------------------------------------------------ evaluation

        /// <summary>Everything a trial result depends on: clock hour, own plans, own orders, the offer slot.</summary>
        long LiveStamp()
        {
            unchecked
            {
                long h = W.Clock.Date * 31 + W.Clock.Hour;
                foreach (var pl in P.Planes.Items)
                {
                    h = h * 131 + pl.Id;
                    foreach (var e in pl.Plan.Entries) if (e.Kind != 0) h = h * 31 + e.Kind * 7919 + e.ObjectId * 17 + e.StartAbs;
                }
                h = h * 31 + P.Contracts.Count * 7 + P.Freights.Count;
                var fp = OfferFingerprint.Of(W, Board, _trySlot);
                if (fp != null) h = h * 31 + fp.Premium + fp.From * 3 + fp.To;
                return h;
            }
        }

        void Evaluate()
        {
            _stamp = LiveStamp();
            if (_trial == null || _applied) { _result = null; return; }
            if (!TrialAccepted && !OfferThere) { _result = null; _tryCarry = false; return; }
            try { _result = _trial.Evaluate(W, _acceptedId); }
            catch (Exception e)
            {
                Debug.LogError("OpenAT trial: evaluation failed: " + e);
                _result = null;
                _trialMsg = PT.Get("try.error");
            }
        }

        /// <summary>Re-evaluates when the live state the result depends on has changed.</summary>
        void TrialRefresh()
        {
            if (_trial == null) return;
            if (LiveStamp() != _stamp) Evaluate();
        }

        // ------------------------------------------------------------------ input

        void TrialOutsideInput(InputState input, Vector2Int canvas) { }

        bool TrialRightClick()
        {
            if (!_tryCarry) return false;
            _tryCarry = false;
            return true;
        }

        RectInt ResetBtn { get { var c = CardRect; return new RectInt(c.x + 4, c.yMax - 17, 46, 14); } }
        RectInt ApplyBtn { get { var c = CardRect; return new RectInt(c.x + 54, c.yMax - 17, 88, 14); } }
        bool CanReset => _trial != null && _trial.Legs.Count > 0 && !_applied && !_picking;
        bool CanApply => TrialAccepted && !_applied && _trial != null && _trial.Legs.Count > 0 && _result != null && OfferTrial.ApplyBlocker(_result) == null;
        /// <summary>The offer is still on the board and the trial is complete: one button takes it and applies the plan.</summary>
        bool CanTakeAndApply => !TrialAccepted && !_picking && _trial != null && _trial.Legs.Count > 0 && OfferThere && _result != null
            && OfferTrial.ApplyBlocker(_result) == null;

        /// <summary>Grid cell under a window-relative point: day row and hour, or false.</summary>
        static bool Cell(Vector2Int l, out int row, out int hour)
        {
            var cm = l - new Vector2Int(21, 22);
            row = hour = -1;
            if (cm.x < PlannerRoom.GridX || cm.x >= PlannerRoom.GridX + 24 * PlannerRoom.CellW || cm.y < PlannerRoom.GridY || cm.y >= PlannerRoom.GridY + 7 * PlannerRoom.CellH) return false;
            hour = (cm.x - PlannerRoom.GridX) / PlannerRoom.CellW;
            row = (cm.y - PlannerRoom.GridY) / PlannerRoom.CellH;
            return true;
        }

        int TryCells(Plane pl) => pl == null || _tryOffer == null ? 1 : W.FlightHours(_tryOffer.From, _tryOffer.To, pl.Speed) + 1;

        /// <summary>Snapped start of the hypothetical post-it centred on the cursor, as the planner snaps (§6.1).</summary>
        void Snap(Vector2Int l, Plane pl, out int row, out int col)
        {
            var cm = l - new Vector2Int(21, 22);
            int w = TryCells(pl) * PlannerRoom.CellW;
            col = Mathf.FloorToInt((cm.x - w / 2f + 3 - PlannerRoom.GridX) / PlannerRoom.CellW);
            row = Mathf.FloorToInt((cm.y - PlannerRoom.CellH / 2f + 9 - PlannerRoom.GridY) / PlannerRoom.CellH);
        }

        bool TrialClick(InputState input, Vector2Int canvas, Vector2Int l)
        {
            if (_trial == null) return false;
            if (CanReset && In(ResetBtn, canvas))
            {
                _trial.Legs.Clear();
                _tryCarry = true;
                Evaluate();
                App.Audio.Play("SOUND/click.raw");
                return true;
            }
            if (CanApply && In(ApplyBtn, canvas)) { ApplyTrial(null); return true; }
            if (CanTakeAndApply && In(ApplyBtn, canvas)) { TakeAndApply(); return true; }
            if (_applied || !Cell(l, out _, out _)) return false;
            if (!TrialAccepted && !OfferThere) return true;
            var pl = P.Planes.Find(_panelPlane);
            if (pl == null) return true;
            if (_tryCarry)
            {
                Snap(l, pl, out int row, out int col);
                var leg = new TrialLeg { PlaneId = pl.Id, Date = W.Clock.Date + row, Hour = col };
                // Normalise like the planner: a post-it hanging left of the grid starts on the previous day.
                while (leg.Hour < 0) { leg.Hour += 24; leg.Date--; }
                while (leg.Hour > 23) { leg.Hour -= 24; leg.Date++; }
                var keep = new System.Collections.Generic.List<TrialLeg>(_trial.Legs);
                if (TrialKind == FlightKind.Contract) _trial.Legs.Clear();   // a passenger order flies once
                _trial.Legs.Add(leg);
                Evaluate();
                var last = _result != null && _result.Legs.Count > 0 ? _result.Legs[_result.Legs.Count - 1] : null;
                if (last == null || !last.Ok)
                {
                    // Refused: the plan stays as it was; say why, keep the post-it on the cursor.
                    _trialMsg = last == null ? PT.Get("try.error") : Planner.PlannerRoom.FailTextFor(last.Reason);
                    _trial.Legs.Clear();
                    _trial.Legs.AddRange(keep);
                    Evaluate();
                    return true;
                }
                _trialMsg = null;
                App.Audio.Play("SOUND/pap2.raw");
                _tryCarry = TrialKind == FlightKind.Freight && _result.TonsOpen > 0;
                if (!_tryCarry) PadFollow();   // the pad cursor goes to "Take and plan" / "Apply plan"
                return true;
            }
            // Click on a trial flight: take it back onto the cursor.
            var gw = TrialWorld;
            var gpl = gw?.Players[P.Index].Planes.Find(pl.Id);
            if (gpl == null || !Cell(l, out int r2, out int h2)) return true;
            int abs = (W.Clock.Date + r2) * 24 + h2;
            for (int i = 0; i < _result.Legs.Count && i < _trial.Legs.Count; i++)
            {
                var lr = _result.Legs[i];
                if (!lr.Ok || lr.Leg.PlaneId != pl.Id || abs < lr.StartAbs || abs > lr.LandAbs) continue;
                _trial.Legs.RemoveAt(i);
                _tryCarry = true;
                Evaluate();
                App.Audio.Play("SOUND/paptake.raw");
                return true;
            }
            return true;
        }

        /// <summary>
        /// Takes the tried offer as its card click would (the room's own take, with its animation), then applies
        /// the trial. The plan must still be the one the panel showed before the taking; otherwise the order
        /// stays taken but unscheduled, as with a failed "Apply".
        /// </summary>
        void TakeAndApply()
        {
            var shown = _result;
            OnClick(_trySlot);
            if (!TrialAccepted) { _trialMsg = PT.Get("try.nottaken"); return; }
            if (!CanApply) { _trialMsg = PT.Get("try.notapplied", ReasonText(_result == null ? "changed" : OfferTrial.ApplyBlocker(_result) ?? "changed")); return; }
            ApplyTrial(shown);
        }

        /// <param name="shown">The result the player saw, when it was computed before the taking (null: the current one).</param>
        void ApplyTrial(TrialResult shown)
        {
            string err;
            try { err = _trial.Apply(W, _acceptedId, shown ?? _result); }
            catch (Exception e) { Debug.LogError("OpenAT trial: apply failed: " + e); err = "error"; }
            if (err == null)
            {
                _applied = true;
                _result = null;
                _trialMsg = PT.Get("try.applied");
                App.Audio.Play("SOUND/pap2.raw");
            }
            else
            {
                _trialMsg = PT.Get("try.notapplied", ReasonText(err));
                Evaluate();
            }
        }

        string ReasonText(string reason)
        {
            switch (reason)
            {
                case "changed": return PT.Get("try.changed");
                case "replaces-flight": return PT.Get("try.replaces");
                case "offer-gone": return PT.Get("try.gone");
                case "closed": return PT.Get("try.closed");
                case "no-legs": return PT.Get("try.nolegs");
            }
            return Planner.PlannerRoom.FailTextFor(reason);
        }

        void TrialGridHover(InputState input, Vector2Int l)
        {
            var pl = P.Planes.Find(_panelPlane);
            if (pl == null || !Cell(l, out int row, out int hour)) return;
            if (_tryCarry && (TrialAccepted || OfferThere)) { App.Ui.Cursor = CursorLook.Hot; return; }
            var gw = TrialWorld ?? W;
            var gpl = gw.Players[P.Index].Planes.Find(pl.Id) ?? pl;
            int abs = (W.Clock.Date + row) * 24 + hour;
            for (int i = 0; i < gpl.Plan.Entries.Length; i++)
            {
                var e = gpl.Plan.Entries[i];
                if (e.Kind == 0 || e.StartAbs > abs || e.LandAbs < abs) continue;
                bool mine = TrialWorld != null && e.Kind == TrialKind && e.ObjectId == _result.ObjectId;
                if (mine && !_applied) App.Ui.Cursor = CursorLook.Hot;
                int key = pl.Id * 64 + i;
                if (key != _hoverKey || _hoverEntry.StartAbs != e.StartAbs) { _hoverKey = key; _hoverSince = Time.unscaledTime; }
                _hoverEntry = e; _hoverPlane = pl.Id; _hoverOn = true;
                return;
            }
        }

        // ------------------------------------------------------------------ flight card (hover)

        bool _hoverOn;
        int _hoverKey = -1, _hoverPlane;
        float _hoverSince;
        FlightEntry _hoverEntry;

        /// <summary>The planner's flight card for the hovered flight, with what the trial does to it.</summary>
        void DrawTrialHoverCard(Screen2D s)
        {
            if (!_hoverOn || Time.unscaledTime - _hoverSince < FlightCard.Delay) return;
            var kit = App.Ui.Kit;
            var e = _hoverEntry;
            var gw = TrialWorld ?? W;
            var gp = gw.Players[P.Index];
            var lines = FlightCard.Lines(gw, Assets, kit, gp, gp.Planes.Find(_hoverPlane), e);
            bool trial = TrialWorld != null && !_applied;
            bool mine = trial && e.Kind == TrialKind && e.ObjectId == _result.ObjectId;
            var live = P.Planes.Find(_hoverPlane);
            if (mine) lines.Add((PT.Get("try.card"), kit.SmallRed));
            else if (e.Kind == FlightKind.Auto)
            {
                // A ferry flight that the live plan does not have is one the trial adds.
                bool added = trial && live != null;
                if (added)
                    foreach (var le in live.Plan.Entries)
                        if (le.Kind == FlightKind.Auto && le.From == e.From && le.To == e.To && le.StartAbs == e.StartAbs) { added = false; break; }
                lines.Add(added ? (PT.Get("try.newferry"), kit.SmallRed) : (PT.Get("auto.why"), kit.SmallGrey));
            }
            else if (trial && live != null)
            {
                // A flight that the live plan has at another time is one the trial moves.
                int was = -1;
                foreach (var le in live.Plan.Entries)
                {
                    if (le.Kind != e.Kind || le.ObjectId != e.ObjectId) continue;
                    if (le.StartAbs == e.StartAbs) { was = -1; break; }
                    was = le.StartAbs;
                }
                if (was >= 0) lines.Add((PT.Get("try.movedby", DayHour(was)), kit.SmallRed));
            }
            var m = App.InputNow.Mouse;
            FlightCard.Draw(s, m.x + 14, m.y + 18, FlightCard.Width, lines);
        }

        // ------------------------------------------------------------------ drawing

        void Outline(Screen2D s, RectInt r, Color32 a, Color32 b)
        {
            s.FrameRect(new RectInt(r.x - 1, r.y - 1, r.width + 2, r.height + 1), a);
            s.FrameRect(new RectInt(r.x - 2, r.y - 2, r.width + 4, r.height + 3), b);
        }

        void DrawTrialOverlay(Screen2D s, World gw, Plane gpl, Vector2Int client, Vector2Int cm)
        {
            if (_trial == null) return;
            if (TrialWorld != null && !_applied)
            {
                var live = P.Planes.Find(gpl.Id);
                foreach (var e in gpl.Plan.Entries)
                {
                    if (e.Kind == 0) continue;
                    if (e.Kind == TrialKind && e.ObjectId == _result.ObjectId)
                    {
                        // The trial's flights: a bright double frame so they read as hypothetical.
                        WeekGrid.Segments(gw, e, client, r => Outline(s, r, TrialFrame, TrialFrame2));
                        continue;
                    }
                    // Flights the trial moved (exist live at another time): orange frame.
                    if (e.Kind == FlightKind.Auto || live == null) continue;
                    bool same = false, exists = false;
                    foreach (var le in live.Plan.Entries)
                    {
                        if (le.Kind != e.Kind || le.ObjectId != e.ObjectId) continue;
                        exists = true;
                        if (le.StartAbs == e.StartAbs) { same = true; break; }
                    }
                    if (exists && !same) WeekGrid.Segments(gw, e, client, r => s.FrameRect(r, ShiftFrame));
                }
            }
            // The hypothetical post-it on the cursor, snapped to the grid.
            var pl = P.Planes.Find(_panelPlane);
            if (!_tryCarry || pl == null || (!TrialAccepted && !OfferThere)) return;
            var l = cm + new Vector2Int(21, 22);
            if (!Cell(l, out _, out _)) return;
            Snap(l, pl, out int row, out int col);
            int cells = TryCells(pl);
            int x = client.x + PlannerRoom.GridX + col * PlannerRoom.CellW + 1, y = client.y + PlannerRoom.GridY + row * PlannerRoom.CellH;
            string code(int id) => W.City(id)?.ShortCode ?? "?";
            WeekGrid.DrawBar(s, Assets, App.Ui.Kit, x, y, TrialKind, cells, code(_tryOffer.From), code(_tryOffer.To), 0, false, 0, int.MaxValue, PanelCodes);
            s.FillRect(new RectInt(x, y, cells * PlannerRoom.CellW, PlannerRoom.CellH), Ghost);
            Outline(s, new RectInt(x, y, cells * PlannerRoom.CellW, PlannerRoom.CellH), TrialFrame, TrialFrame2);
        }

        void DrawTrialLines(Action<string, BitmapFont> line)
        {
            var kit = App.Ui.Kit;
            if (_trialMsg != null) line(_trialMsg, _applied ? kit.SmallBlack : kit.SmallRed);
            if (TrialAccepted) line(PT.Get(_applied ? "try.scheduled" : "try.accepted"), kit.SmallBlack);
            if (_applied || _trial == null) return;
            if (_result == null) return;
            if (_result.AcceptFailure != null) { line(PT.Get("try.cannot", ReasonText(_result.AcceptFailure)), kit.SmallRed); return; }
            for (int i = 0; i < _result.Legs.Count; i++)
            {
                var r = _result.Legs[i];
                string plane = P.Planes.Find(r.Leg.PlaneId)?.Name ?? "?";
                if (!r.Ok) { line(plane + ": " + Planner.PlannerRoom.FailTextFor(r.Reason), kit.SmallRed); continue; }
                string when = DayHour(r.StartAbs) + "-" + (r.LandAbs % 24).ToString("00") + ":00";
                if (r.StartAbs != r.Leg.RequestedAbs) when += " " + PT.Get("try.wanted", DayHour(r.Leg.RequestedAbs));
                line(plane + ": " + when + (TrialKind == FlightKind.Freight ? ", " + UiKit.Unit(UiKit.UnitT, r.Tons) : ""), kit.SmallBlack);
                var notes = new System.Collections.Generic.List<string>();
                if (r.FerriesAdded > 0) notes.Add(PT.Get("res.ferry", r.FerriesAdded));
                if (r.FerriesRemoved > 0) notes.Add(PT.Get("res.ferrygone", r.FerriesRemoved));
                if (r.OtherFlightsMoved > 0) notes.Add(PT.Get("res.shift", r.OtherFlightsMoved));
                if (r.GateWarning) notes.Add(PT.Get("gate.none"));
                if (notes.Count > 0) line(string.Join("; ", notes), kit.SmallBlack);
                if (r.Okay != 0) line(Std("Auft", 2320 + r.Okay), kit.SmallRed);
                if (r.PickedUpKind != 0) line(PT.Get("try.replaces"), kit.SmallRed);
            }
            if (TrialKind == FlightKind.Freight && _result.Tons > 0)
                line(PT.Get("try.tons", _result.Tons - _result.TonsOpen, _result.Tons, _result.TonsOpen), kit.SmallBlack);
            if (_result.GateWarningsAfter > _result.GateWarningsBefore) line(PT.Get("try.gates", _result.GateWarningsAfter - _result.GateWarningsBefore), kit.SmallRed);
            if (_trial.Legs.Count > 0) line(PT.Get("try.only"), kit.SmallGrey);
            line(PT.Get(_tryCarry ? "try.place" : _trial.Legs.Count > 0 ? "try.move" : "try.pick"), kit.SmallGrey);
        }

        void DrawTrialButtons(Screen2D s, RectInt card, Vector2Int mouse)
        {
            var f = App.Ui.Kit.SmallBlack;
            void Btn(RectInt b, string text)
            {
                var r = new RectInt(RX + b.x, b.y, b.width, b.height);
                s.FillRect(r, In(r, mouse) ? PaperDark : Paper);
                s.FrameRect(r, Ink);
                f.Draw(s, r.x + r.width / 2, r.y + 1, text, TextAlign.Center);
            }
            if (CanReset) Btn(ResetBtn, PT.Get("clear"));
            if (CanApply) Btn(ApplyBtn, PT.Get("try.apply"));
            else if (CanTakeAndApply) Btn(ApplyBtn, PT.Get("try.takeapply"));
        }
    }
}
