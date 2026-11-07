using System;
using OpenAT.Sim;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Staff;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// Shared order-card counter (rooms-and-ui-6 §0.2): six offer slots of a global board shown as cards,
    /// hover = hot cursor + order tip + one-time order-advisor comment, click = take at once
    /// (<see cref="AcceptContractCommand"/> / <see cref="AcceptFreightCommand"/>), the card then flies away.
    /// Entering and leaving refills the board (<see cref="VisitOfferBoardCommand"/>).
    /// </summary>
    public abstract partial class OrderCounterRoom : RoomScreen
    {
        protected World W => App.Session?.World;
        protected Player P => App.Session?.Local;

        protected abstract OfferBoard Board { get; }
        protected abstract Vector2Int[] SlotPos { get; }
        /// <summary>Card hit size (passenger cards) or per-slot crate rects.</summary>
        protected virtual RectInt SlotRect(int slot) { var p = SlotPos[slot]; return new RectInt(p.x, p.y, 70, 65); }
        protected abstract Vector2Int TipPos { get; }
        protected virtual bool FreightBoard => false;

        protected int Hover = -1;
        int _commented = -1;
        // Leave animation per slot: progress 0..1, -1 = none.
        protected readonly float[] Fly = { -1, -1, -1, -1, -1, -1 };
        protected Contract[] FlyCard = new Contract[6];
        protected FreightContract[] FlyFreight = new FreightContract[6];

        protected override void OnRoomEnter()
        {
            if (W != null) W.Execute(new VisitOfferBoardCommand(P.Index, Board));
        }

        protected override void OnLeaveRoom()
        {
            if (W != null && App.Session != null) W.Execute(new VisitOfferBoardCommand(P.Index, Board));
        }

        protected bool SlotFilled(int slot)
        {
            if (W == null) return false;
            if (FreightBoard) return W.FreightOffice.Offers[slot] != null && W.FreightOffice.Offers[slot].Premium >= 0;
            var b = Board == OfferBoard.LastMinute ? W.LastMinute : W.TravelAgency;
            return !b.IsEmpty(slot);
        }

        protected Contract Offer(int slot) => (Board == OfferBoard.LastMinute ? W.LastMinute : W.TravelAgency).Offers[slot];

        /// <summary>Closing time: a player still inside is sent out (§0.2).</summary>
        protected virtual bool Open => true;

        public override void Update(InputState input, float dt)
        {
            ApplyPanelLayout();
            if (Menus != null && !Menus.IsOpen && !IsDialogOpen && input.Mouse.y < Screen.Height - StatusBar.Height) PanelInput(input);
            else _panelMouse = false;
            base.Update(input, dt);
            if (W != null && !Open && !IsDialogOpen && !Menus.IsOpen && App.Rooms.Current == this) Leave();
        }

        public override void Animate(float dt)
        {
            for (int i = 0; i < 6; i++)
                if (Fly[i] >= 0)
                {
                    Fly[i] += dt / FlySeconds;
                    if (Fly[i] >= 1) { Fly[i] = -1; OnFlyDone(i); }
                }
        }

        protected virtual bool SlotBusy(int i) => Fly[i] >= 0;

        /// <summary>The hovered offer's tip; the planner panel (OpenAT) is drawn over it.</summary>
        protected virtual void DrawOfferTip(Screen2D s) { }

        protected sealed override void DrawOverlay(Screen2D s)
        {
            DrawOfferTip(s);
            DrawPanel(s);
        }
        protected virtual float FlySeconds => 0.333f;
        protected virtual void OnFlyDone(int slot) { }

        /// <summary>Hover over the cards; registers hotspots (card ids 0..5).</summary>
        protected void CardHotspots()
        {
            Hover = -1;
            for (int i = 5; i >= 0; i--)
            {
                if (!SlotFilled(i) || SlotBusy(i)) continue;
                var r = SlotRect(i);
                if (Hot(r.xMin, r.yMin, r.xMax - 1, r.yMax - 1, i)) { Hover = i; break; }
            }
            if (Hover >= 0 && Hover != _commented)
            {
                _commented = Hover;
                if (P.HasAdvisor(AdvisorType.Order))
                {
                    Sim.Fleet.Plane ferry;
                    int id = FreightBoard ? ContractAdvisor.RateFreight(W, P, W.FreightOffice.Offers[Hover], out ferry) : ContractAdvisor.Rate(W, P, Offer(Hover), out ferry);
                    var raw = Assets.StdText.Get("Advi", id);
                    if (raw != null && ferry != null) raw = DialogText.Format(raw, new object[] { ferry.Name });
                    if (raw != null) App.Advisors.Queue(AdvisorKind.Contract, raw, AdvisorUrgency.Comment);
                }
            }
            if (Hover < 0 && _commented >= 0) { _commented = -1; App.Advisors.NoComments(); }
        }

        /// <summary>Takes the card in <paramref name="slot"/>; true on success.</summary>
        protected bool Take(int slot)
        {
            bool tried = IsTried(slot);
            int id;
            if (FreightBoard)
            {
                var copy = W.FreightOffice.Offers[slot].Clone();
                var cmd = new AcceptFreightCommand(P.Index, Board, slot);
                if (!W.Execute(cmd).Ok) return false;
                FlyFreight[slot] = copy;
                id = cmd.FreightId;
            }
            else
            {
                var copy = Offer(slot).Clone();
                var cmd = new AcceptContractCommand(P.Index, Board, slot);
                if (!W.Execute(cmd).Ok) return false;
                FlyCard[slot] = copy;
                id = cmd.ContractId;
            }
            if (tried) OnTriedOfferAccepted(id);
            App.Audio.Play("SOUND/paptake.raw");
            Fly[slot] = 0;
            App.Advisors.NoComments();
            return true;
        }

        // ------------------------------------------------------------------ card art

        static string Short(string city)
        {
            // Names with exactly two spaces are cut to their first word (reise.cpp ShortenLongCities).
            if (city == null) return "";
            int n = 0; foreach (var c in city) if (c == ' ') n++;
            return n == 2 ? city.Substring(0, city.IndexOf(' ')) : city;
        }

        /// <summary>The four card lines (§0.2): codes, origin, dash, destination.</summary>
        protected void DrawCardText(Screen2D s, int x, int y, int w, int from, int to)
        {
            var f = App.Ui.Kit.SmallBlack;
            var a = W.City(from); var b = W.City(to);
            int cx = x + w / 2;
            f.Draw(s, cx, y + 14, (a?.ShortCode ?? "?") + "-" + (b?.ShortCode ?? "?"), TextAlign.Center);
            f.Draw(s, cx, y + 31, Clip(f, Short(a?.Name), w - 4), TextAlign.Center);
            f.Draw(s, cx, y + 41, "-", TextAlign.Center);
            f.Draw(s, cx, y + 52, Clip(f, Short(b?.Name), w - 4), TextAlign.Center);
        }

        protected static string Clip(BitmapFont f, string t, int w)
        {
            if (t == null) return "";
            while (t.Length > 1 && f.Measure(t) > w) t = t.Substring(0, t.Length - 1);
            return t;
        }

        /// <summary>Card with its 4 lines; scaled while flying to <paramref name="target"/>.</summary>
        protected void DrawCard(Screen2D s, int slot, int from, int to, Vector2Int target)
        {
            var tex = App.Ui.Kit.Std("ZETTEL0" + (slot % 3 + 1));
            var p = SlotPos[slot];
            if (Fly[slot] >= 0)
            {
                float t = Fly[slot], sc = Mathf.Lerp(1f, 0.29f, t);
                float x = Mathf.Lerp(p.x, target.x, t), y = Mathf.Lerp(p.y, target.y, t);
                s.DrawScaled(tex, new Rect(RX + x, y, tex.width * sc, tex.height * sc), new Rect(0, 0, tex.width, tex.height), Color.white);
                return;
            }
            s.Draw(tex, RX + p.x, p.y);
            DrawCardText(s, RX + p.x, p.y, tex.width, from, to);
        }

        // ------------------------------------------------------------------ tip panel (TIP_BUYAUFTRAG / FRACHT)

        protected void DrawOrderTip(Screen2D s, int from, int to, int persons, int tons, int date, int last, long premium, long penalty, bool freight)
        {
            var bg = Assets.Texture("gli/tipau.gli", "BLOC1");
            int x = RX + TipPos.x, y = TipPos.y;
            s.Draw(bg, x, y);
            var f = App.Ui.Kit.SmallBlack;
            string Std(string g, int id) => Assets.StdText.Get(g, id) ?? "";
            int lx = x + 28, rx = x + 222, ly = y + 12;
            var a = W.City(from); var b = W.City(to);
            if (!freight)
            {
                // Passenger order card (DrawAuftragTipContents, verified on the running original): headline at
                // (32,3), contents from (32,20): "From - To" (falling back to codes), then date, distance,
                // passengers, penalty and, after a gap, the premium; values left-aligned 85 px in.
                int cx = x + 32, cy = y + 20;
                f.Draw(s, cx, y + 3, Std("Auft", 900));
                string pair = (a?.Name ?? "") + " - " + (b?.Name ?? "");
                if (f.Measure(pair) > 170) pair = (a?.Name ?? "") + " - " + (b?.ShortCode ?? "");
                if (f.Measure(pair) > 170) pair = (a?.ShortCode ?? "") + " - " + (b?.ShortCode ?? "");
                f.Draw(s, cx, cy, pair);
                void Line(int dy, string label, string value) { f.Draw(s, cx, cy + dy, label); f.Draw(s, cx + 85, cy + dy, value); }
                string wd(int d) => Std("Schd", 3010 + Planner.PlannerRoom.Weekday(W, d));
                string when = W.Clock.Date == last ? Std("Auft", 2002) : date == last ? wd(date) : Std("Schd", 3009) + " " + wd(last);
                Line(22, Std("Auft", 1003), when);
                Line(44, Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(from, to) / 1000));
                Line(55, Std("Auft", 1000), persons.ToString());
                Line(66, Std("Auft", 1002), UiKit.Money(penalty));
                Line(88, Std("Auft", 1001), UiKit.Money(premium));
                DrawRouteOnMap(s, x, y, a, b);
                return;
            }
            f.Draw(s, lx, ly, Std("Auft", freight ? 902 : 900)); ly += 14;
            f.Draw(s, lx, ly, Clip(f, a?.Name, 190)); ly += 11;
            f.Draw(s, lx, ly, Clip(f, "- " + b?.Name, 190)); ly += 16;
            void Row(string label, string value) { f.Draw(s, lx, ly, label); f.Draw(s, rx, ly, value, TextAlign.Right); ly += 12; }
            if (freight) Row(Std("Auft", 1008), UiKit.Unit(UiKit.UnitT, tons));
            else Row(Std("Auft", 1000), persons.ToString());
            Row(Std("Auft", 1003), DateRange(date, last));
            Row(Std("Auft", 1001), UiKit.Money(premium));
            Row(Std("Auft", 1002), UiKit.Money(penalty));
            Row(Std("Auft", 1007), UiKit.Unit(UiKit.UnitKm, W.Distance(from, to) / 1000));
            // Plane icons: which own planes could fly it (black = none).
            int px = lx, py = ly + 4;
            int shown = 0;
            foreach (var pl in P.Planes.Items)
            {
                bool ok = W.Distance(from, to) <= pl.Range * 1000 && W.FlightHours(from, to, pl.Speed) < 24 && (freight || pl.MaxPassengers + pl.MaxPassengersFC >= persons);
                if (!ok) continue;
                if (shown++ >= 9) break;
                s.Draw(Assets.Texture("gli/tipau.gli", "PL_" + new[] { "B", "V", "R", "J" }[P.Index] + "00"), px, py);
                px += 20;
            }
            if (shown == 0) s.Draw(Assets.Texture("gli/tipau.gli", "PL_SW00"), px, py);
            DrawRouteOnMap(s, x, y, a, b);
        }

        /// <summary>Route on the small world map of the card (approximate equirectangular placement).</summary>
        void DrawRouteOnMap(Screen2D s, int x, int y, Sim.Data.City a, Sim.Data.City b)
        {
            Vector2 M(Sim.Data.City c) => new Vector2(x + 42 + (c.GlobeX + 170) * 178f / 345f, y + 188 + c.GlobeY * 0.62f);
            if (a != null && b != null)
            {
                var pa = M(a); var pb = M(b);
                var red = new Color32(200, 0, 0, 255);
                int n = Mathf.Max(1, Mathf.CeilToInt((pb - pa).magnitude));
                for (int i = 0; i <= n; i++) { var q = Vector2.Lerp(pa, pb, i / (float)n); s.FillRect(new RectInt((int)q.x, (int)q.y, 1, 1), red); }
                s.FillRect(new RectInt((int)pa.x - 1, (int)pa.y - 1, 3, 3), red);
                s.FillRect(new RectInt((int)pb.x - 1, (int)pb.y - 1, 3, 3), red);
            }
        }

        string DateRange(int date, int last)
        {
            var w = W;
            int today = w.Clock.Date;
            string D(int d)
            {
                int k = d - today;
                if (k == 0) return Assets.StdText.Get("Auft", 2002);
                if (k == 1) return Assets.StdText.Get("Auft", 2001);
                return DialogText.Format(Assets.StdText.Get("Auft", 2000) ?? "%li", new object[] { (long)k });
            }
            if (date == last) return D(date);
            if (date <= today) return DialogText.Format(Assets.StdText.Get("Auft", 2010) ?? "%li", new object[] { (long)(last - today) });
            return DialogText.Format(Assets.StdText.Get("Auft", 2011) ?? "%li %li", new object[] { (long)(date - today), (long)(last - today) });
        }
    }
}
