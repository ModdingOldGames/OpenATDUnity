using OpenAT.Sim;
using OpenAT.Sim.Airport;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Tafel
{
    /// <summary>Slip positions of the notice board (rooms-and-ui-6 §4.3, "ZettelPos"): 7 route, 7 city, 7 gate slots.</summary>
    public static class TafelLayout
    {
        static readonly Vector2Int[] Pos =
        {
            V(41, 15), V(245, 278), V(180, 32), V(375, 281), V(322, 12), V(0, 0), V(0, 0),
            V(318, 138), V(470, 61), V(18, 156), V(155, 170), V(0, 0), V(0, 0), V(0, 0),
            V(10, 289), V(132, 258), V(466, 182), V(18, 156), V(375, 281), V(318, 138), V(245, 278),
        };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        public static Vector2Int RouteSlot(int i) => Pos[i];
        public static Vector2Int CitySlot(int i) => Pos[7 + i];
        public static Vector2Int GateSlot(int i) => Pos[14 + i];
    }

    /// <summary>
    /// The notice board (rooms-and-ui-6 §4): branch-office and gate auctions on paper slips. A click on a
    /// slip bids +10 % through <see cref="BidCommand"/>, against the board's state when the room was
    /// entered (one raise per visit); the fly takes off when the mouse comes near. Reached from the boss
    /// office only. Route slips stay unused (§4.6).
    /// </summary>
    [Room(RoomIds.Tafel)]
    [ScreenName("tafel")]
    public sealed class TafelRoom : RoomScreen
    {
        const string Lib = "room/tafel.gli";
        protected override string Library => Lib;
        protected override string Background => "TAFEL";

        const int ClickCity = 100, ClickGate = 200, ClickPostcard = 800;

        SmackerPerson _fly;
        int _flyStart = -1;
        readonly AuctionSlip[] _entryCities = new AuctionSlip[AuctionBoard.Slots];
        readonly AuctionSlip[] _entryGates = new AuctionSlip[AuctionBoard.Slots];

        protected override void OnCreate()
        {
            _fly = new SmackerPerson(Assets, App.Audio);
            _fly.Clip(0, "FlyW01.smk", 554, 253, Mood.Idle, "A9:0 A9:1", 5, 9);
            _fly.Clip(1, "FlyW02.smk", 554, 253, Mood.Idle, "A9:0 A9:1", 6, 9);
            _fly.Clip(2, "Fly.smk", 554, 253, Mood.Idle, "A9:3", canCancel: false, sound: "Fly.raw");
            _fly.Clip(3, "FlyOut.smk", 554, 253, Mood.Idle, "A9:4", 9, 9, 200, 600, canCancel: false);
            _fly.Clip(4, "FlyB.smk", 554, 253, Mood.Idle, "A9:0", canCancel: false, sound: "FlyB.raw").OnStart = () => _flyStart = -1;
        }

        protected override void OnRoomEnter()
        {
            // Snapshot for the "one raise per visit" rule (§4.4).
            var a = App.Session?.World.Auctions;
            if (a == null) return;
            for (int i = 0; i < AuctionBoard.Slots; i++) { _entryCities[i] = a.Cities[i].Clone(); _entryGates[i] = a.Gates[i].Clone(); }
        }

        public override void Animate(float dt) => _fly.Update(dt);

        World W => App.Session?.World;

        bool InSlip(Vector2Int pos, int slot, out RectInt r)
        {
            var t = Assets.Texture(Lib, "ZETTEL0" + (4 + slot % 3));
            r = new RectInt(pos.x, pos.y, t.width, t.height);
            var m = App.InputNow.Mouse;
            int x = m.x - RX, y = m.y;
            return x >= r.xMin && x <= r.xMax && y >= r.yMin && y <= r.yMax;
        }

        protected override void Hotspots(InputState input)
        {
            var m = input.Mouse;
            int mx = m.x - RX;
            if (mx >= 500 && mx <= 640 && m.y >= 280 && m.y <= 424 && _flyStart < 0 && _fly.ActiveClip <= 1) { _flyStart = 2; _fly.Decision = 2; }
            var w = W;
            if (w != null)
            {
                var me = w.Local;
                // Like the original (rooms-and-ui-6 §4.4): slips are tested in posting order, cities first,
                // then gates; the first slip under the mouse takes the click even if it cannot be bid on
                // (own bid, or a city slip hidden because the airline already has a branch there).
                bool done = false;
                for (int c = 0; c < AuctionBoard.Slots && !done; c++)
                {
                    var s = w.Auctions.Cities[c];
                    if (s.ObjectId < 0 || !InSlip(TafelLayout.CitySlot(c), c, out var r)) continue;
                    done = true;
                    if (s.Player != me.Index && me.RentCities[s.ObjectId].Rank == 0) Hot(r.xMin, r.yMin, r.xMax, r.yMax, ClickCity + c);
                }
                for (int c = 0; c < AuctionBoard.Slots && !done; c++)
                {
                    var g = w.Auctions.Gates[c];
                    if (g.ObjectId == -1 || !InSlip(TafelLayout.GateSlot(c), c, out var r)) continue;
                    done = true;
                    if (g.Player != me.Index) Hot(r.xMin, r.yMin, r.xMax, r.yMax, ClickGate + c);
                }
            }
            if (PostcardThere)
            {
                Hot(25, 317, 188, 410, ClickPostcard);
                Hot(116, 299, 182, 334, ClickPostcard);
                Hot(37, 385, 116, 425, ClickPostcard);
            }
            Exit(580, 369, 639, 423);
        }

        protected override void OnClick(int id)
        {
            var w = W;
            if (w == null) return;
            if (id == ClickPostcard)
            {
                w.Execute(new Sim.Items.PickUpItemCommand(w.LocalPlayer, Sim.Items.ItemId.Postcard));   // silent, like the original
                return;
            }
            bool city = id < ClickGate;
            int slot = id - (city ? ClickCity : ClickGate);
            w.Execute(new BidCommand(w.LocalPlayer, city ? AuctionKind.City : AuctionKind.Gate, slot, city ? _entryCities[slot] : _entryGates[slot]));   // no sound in the original
        }

        /// <summary>The postcard pinned to the board (rooms-and-ui-6 §4.4): back every day, gone once taken.</summary>
        bool PostcardThere
        {
            get
            {
                var w = W;
                return w != null && w.Items.Postcard && !w.Local.SeligTrust && w.Difficulty != Mission.Tutorial;
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            var w = W;
            if (!PostcardThere) s.Draw(Assets.Texture(Lib, "NOCARD"), ox + 16, 290);
            if (w != null)
            {
                for (int c = 0; c < AuctionBoard.Slots; c++)
                {
                    var cs = w.Auctions.Cities[c];
                    // A city slip is only shown to airlines without a branch there.
                    if (cs.ObjectId >= 0 && w.Local.RentCities[cs.ObjectId].Rank == 0) DrawSlip(s, ox, TafelLayout.CitySlot(c), c, cs, true);
                }
                for (int c = 0; c < AuctionBoard.Slots; c++)
                {
                    var g = w.Auctions.Gates[c];
                    if (g.ObjectId != -1) DrawSlip(s, ox, TafelLayout.GateSlot(c), c, g, false);
                }
            }
            _fly.Draw(s, ox);
        }

        void DrawSlip(Screen2D s, int ox, Vector2Int pos, int slot, AuctionSlip a, bool city)
        {
            var paper = Assets.Texture(Lib, "ZETTEL0" + (4 + slot % 3));
            int x = ox + pos.x, y = pos.y;
            s.Draw(paper, x, y);
            var f = App.Ui.Kit.SmallBlack;
            var w = W;
            string bidder = a.Player >= 0 && a.Player < Player.Count ? w.Players[a.Player].Airline ?? "" : "";
            string price = UiKit.Money(a.Price);
            if (city)
            {
                f.Draw(s, x + paper.width / 2, y + 36, Std("Misc", 2501), TextAlign.Center);
                string name = a.ObjectId < w.Data.Cities.Count ? w.Data.Cities.All[a.ObjectId].Name : "";
                f.Draw(s, x + paper.width / 2, y + 58, name, TextAlign.Center);
                f.Draw(s, x + 13, y + 85, bidder);
                f.Draw(s, x + 13, y + 100, price);
            }
            else
            {
                f.Draw(s, x + paper.width / 2, y + 33, DialogText.Format(Std("Misc", 2500), new object[] { (long)(a.ObjectId + 1) }), TextAlign.Center);
                f.Draw(s, x + 13, y + 75, bidder);
                f.Draw(s, x + 13, y + 90, price);
            }
        }

        string Std(string group, int id) => Assets.StdText.Get(group, id) ?? "";
    }
}
