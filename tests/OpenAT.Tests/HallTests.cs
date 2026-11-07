using System.Collections.Generic;
using System.Text;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Airport hall (airport-and-people.md): layout, walk grid, walking, spawning, determinism.</summary>
    public class HallTests
    {
        static GameInstall _install;
        static HallData _hall;
        static GameData _data;

        static bool Load()
        {
            if (_hall != null) return true;
            _install = GameInstall.TryLocate();
            if (_install == null) return false;
            _hall = HallData.Load(_install);
            _data = GameData.Load(_install);
            HallModule.Register();
            HallContent.Attach(_data, _hall);
            return true;
        }

        static World NewWorld(int seed = 12345)
        {
            var w = World.NewGame(_data, new NewGameSettings { StartTime = 0, Seed = seed });
            w.Systems.Remove(OpenAT.Sim.AI.AiSystems.PumpSystem.SystemName); // these tests assume idle computer bosses
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            return w;
        }

        [Fact]
        public void Pt_ValueSemantics()
        {
            Assert.Equal(new Pt(3, 4), new Pt(1, 1) + new Pt(2, 3));
            Assert.True(new Pt(1, 2) != new Pt(2, 1));
        }

        [Fact]
        public void Layout_MatchesSpecExtents()
        {
            if (!Load()) return;
            // §2.3 "Resulting extents" (difficulty 3, level 1).
            var l = new HallLayout(_hall, 3, HallLayout.DefaultLevels());
            Assert.Equal(176, l.LeftEnd);
            Assert.Equal(5852, l.RightEnd);
            Assert.Equal(-297, l.MinX);
            Assert.Equal(4, l.GateCount); // 3 + level
            Assert.True(l.Doors.Count > 10);
            Assert.True(l.Areas.Count > 30);
            Assert.True(l.ClipMarkers.Count > 2);
            // Brick table: 333 rows; clan table: 89 clans in slots 1..89.
            Assert.Equal(333, _hall.Bricks.Count);
            Assert.Null(_hall.Clans[0]);
            Assert.NotNull(_hall.Clans[1]);
            Assert.Equal(ClanType.Player1, _hall.Clans[31].Type);
            // Builds are merged in depth order (layer, bottom + floor offset, x). The segment files are
            // stored almost sorted by the original editor; the k-way merge keeps their order.
            int unordered = 0;
            for (int i = 1; i < l.Builds.Count; i++)
            {
                var a = l.Builds[i - 1]; var b = l.Builds[i];
                if (!(a.Brick.Layer < b.Brick.Layer || (a.Brick.Layer == b.Brick.Layer && a.SortKey <= b.SortKey))) unordered++;
            }
            Assert.True(unordered * 50 < l.Builds.Count, "unordered pairs: " + unordered);
        }

        [Fact]
        public void Layout_FreeGameUsesMap7AndHasAllServiceRooms()
        {
            if (!Load()) return;
            var l = new HallLayout(_hall, Mission.FreeGame, HallLayout.DefaultLevels());
            Assert.True(l.LeftEnd < l.RightEnd);
            foreach (int room in new[] { RoomIds.OfficeA, RoomIds.PersonnelA, RoomIds.Bank, RoomIds.Museum, RoomIds.Aufsicht, RoomIds.Werkstatt, RoomIds.Ricks, RoomIds.Shop1, RoomIds.AirportExit })
            {
                Assert.True(l.HasRune(RuneId.ToShop, room), "approach rune of room " + room);
                Assert.True(l.HasRune(RuneId.Shop, room) || room == RoomIds.AirportExit, "threshold rune of room " + room);
            }
            for (int p = 1; p <= 4; p++) Assert.True(l.HasRune(RuneId.PCreation, p));
        }

        [Fact]
        public void Grid_IsSymmetricAndRunesAreWalkable()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            int asym = 0;
            for (int x = 0; x < h.Layout.Columns - 1; x++)
                for (int y = 5; y < 15; y++)
                {
                    // A wall between two lower tiles removes the exit on both sides (§4.3), except one-way runes.
                    bool e = (h.P(x, y) & Plate.ExitE) != 0, back = (h.P(x + 1, y) & Plate.ExitW) != 0;
                    if (e != back) asym++;
                }
            // One-way tiles (belts, arrows) are the only asymmetric edges; they are a small minority.
            Assert.True(asym < 200, "asymmetric edges: " + asym);
            // Every room's approach rune lies on a tile players may stand on.
            foreach (var r in h.Layout.Runes)
            {
                if (r.Id != RuneId.ToShop || RoomIds.IsPassengerMarker(r.Par) || r.Par >= RoomIds.BeltX1) continue;
                var t = HallState.TileOfPosition(r.Pos);
                if (t.Y < 0 || t.Y > 15) continue;
                Assert.True((h.P(t.X, t.Y) & (Plate.Players | Plate.Customers)) != 0, "rune of room " + r.Par + " at " + t);
            }
        }

        [Fact]
        public void NewGame_PlacesFiguresAndWalkers()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            for (int p = 0; p < 4; p++) Assert.NotNull(h.PlayerPerson(p));
            Assert.Contains(h.Persons, x => x.Reason == Reason.Waypoint);
            h.PlaceAfterBriefing();
            for (int p = 0; p < 4; p++) Assert.True(h.PlayerPerson(p).Position.Y >= 5000, "upstairs after the briefing");
        }

        static void RunUntil(World w, System.Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks && !done(); i++) w.Tick();
        }

        [Fact]
        public void Walk_ClickReachesTheTargetTile()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            h.PlaceAfterBriefing();
            w.StartWorkingDay();
            RunUntil(w, () => false, 20);
            var me = h.PlayerPerson(0);
            // Walk to a lower-floor tile three columns west of the bank's approach rune.
            var bank = h.RandomRune(RuneId.ToShop, RoomIds.Bank, new TeakRand(1)).Value;
            var target = new Pt(bank.X / 44 - 3, 12);
            Assert.True(w.Execute(new WalkToTileCommand(0, target)).Ok);
            RunUntil(w, () => !h.Players[0].WalkActive && me.Dir == 8, 4000);
            Assert.Equal(target, HallState.TileOfPosition(me.Position));
        }

        [Fact]
        public void Walk_ToRoomRaisesRoomRequested()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            h.PlaceAfterBriefing();
            w.StartWorkingDay();
            var got = new List<RoomRequested>();
            w.Events.Subscribe<RoomRequested>(e => got.Add(e));
            RunUntil(w, () => false, 5);
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.Bank)).Ok);
            RunUntil(w, () => got.Count > 0, 6000);
            Assert.Single(got);
            Assert.Equal(0, got[0].Player);
            Assert.Equal(RoomIds.Bank, got[0].Room);
            Assert.False(h.Players[0].InHall);
            // Leaving the room: the figure walks back out to the approach point.
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            RunUntil(w, () => false, 200);
            Assert.True(h.Players[0].InHall);
            Assert.Equal(0, h.PlayerPerson(0).StatePar);
        }

        [Fact]
        public void Walk_UpstairsUsesTheStairs()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            h.PlaceAfterBriefing();
            w.StartWorkingDay();
            var got = new List<RoomRequested>();
            w.Events.Subscribe<RoomRequested>(e => got.Add(e));
            RunUntil(w, () => false, 5);
            // Down to the bank, back from it, then up to the own office.
            w.Execute(new WalkToRoomCommand(0, RoomIds.Bank));
            RunUntil(w, () => got.Count > 0, 6000);
            w.Execute(new ReturnToHallCommand(0));
            RunUntil(w, () => h.PlayerPerson(0).StatePar == 0 && h.PlayerPerson(0).Dir == 8, 600);
            Assert.True(h.PlayerPerson(0).Position.Y < 5000);
            got.Clear();
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.OfficeA)).Ok);
            RunUntil(w, () => got.Count > 0, 8000);
            Assert.Single(got);
            Assert.Equal(RoomIds.OfficeA, got[0].Room);
            Assert.True(h.PlayerPerson(0).Position.Y >= 5000);
        }

        [Fact]
        public void Spawning_ShoppersArriveAndLeave()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            h.PlaceAfterBriefing();
            w.StartWorkingDay();
            // 20 game minutes = 40 000 units = 1334 ticks: one shopper per 64 ticks after 09:00.
            RunUntil(w, () => false, 1334);
            int shoppers = h.NumShoppers();
            Assert.InRange(shoppers, 10, 70);
            // Customers stay on the lower floor and inside the grid's columns.
            foreach (var p in h.Persons)
                if (p.Reason == Reason.Shopping) Assert.InRange(p.Position.X, h.Layout.MinX - 100, h.Layout.MaxX + 400);
            // By late evening the shop flow has emptied again (no new shoppers after 16:00).
            RunUntil(w, () => w.Clock.Hour >= 17 && w.Clock.Minute >= 30, 200000);
            Assert.True(h.NumShoppers() < shoppers);
        }

        static string Snapshot(World w)
        {
            var sb = new StringBuilder();
            foreach (var p in w.Hall.Persons) sb.Append(p.ClanId).Append(':').Append(p.Position).Append(p.State).Append(p.Dir).Append(';');
            foreach (var b in w.Hall.Plates) sb.Append((char)('a' + (b & 15)));
            return sb.ToString();
        }

        [Fact]
        public void Determinism_SameInputsSameHall()
        {
            if (!Load()) return;
            var a = NewWorld(); var b = NewWorld();
            foreach (var w in new[] { a, b }) { w.Hall.PlaceAfterBriefing(); w.StartWorkingDay(); }
            for (int i = 0; i < 2500; i++)
            {
                if (i == 100) { a.Execute(new WalkToRoomCommand(0, RoomIds.Museum)); b.Execute(new WalkToRoomCommand(0, RoomIds.Museum)); }
                a.Tick(); b.Tick();
            }
            Assert.Equal(Snapshot(a), Snapshot(b));
            Assert.True(a.Hall.Persons.Count > 5);
        }

        [Fact]
        public void NewDay_RespawnsAndRotatesClans()
        {
            if (!Load()) return;
            var w = NewWorld();
            var h = w.Hall;
            h.PlaceAfterBriefing();
            w.StartWorkingDay();
            var before = (int[])h.TodayInGame.Clone();
            RunUntil(w, () => w.Clock.Date == 1 && w.Clock.State == DayState.Morning, 200000);
            Assert.Equal(1, w.Clock.Date);
            // Only players and waypoint walkers exist after the night.
            foreach (var p in h.Persons) Assert.True(p.Reason == Reason.Player || p.Reason == Reason.Waypoint);
            int changed = 0;
            for (int i = 0; i < before.Length; i++) if (before[i] != h.TodayInGame[i]) changed++;
            Assert.InRange(changed, 1, 20);
        }

        [Fact]
        public void Commands_RejectClicksBeforeNine()
        {
            if (!Load()) return;
            var w = NewWorld();
            Assert.False(w.Execute(new WalkToClickCommand(0, new Pt(1000, 300))).Ok); // frozen morning, 09:00
        }

        [Fact]
        public void Mood_BitmapNumbersSkipTheMissingFiveToNine()
        {
            Assert.Equal(0, Mood.BitmapNumber(0));
            Assert.Equal(4, Mood.BitmapNumber(4));
            Assert.Equal(10, Mood.BitmapNumber(Mood.Repair));
            Assert.Equal(14, Mood.BitmapNumber(Mood.Missed));
            Assert.Equal(19, Mood.BitmapNumber(Mood.Bank));
            Assert.Equal(42, Mood.BitmapNumber(Mood.Stars3FC));
            Assert.True(Mood.ShowsAirlineLogo(Mood.Missed));
            Assert.True(Mood.ShowsAirlineLogo(Mood.TicketFC));
            Assert.False(Mood.ShowsAirlineLogo(Mood.Bank));
            Assert.False(Mood.ShowsAirlineLogo(4));
            Assert.False(Mood.ShowsAirlineLogo(Mood.Empty));
        }

        [Fact]
        public void Install_EveryMoodHasABubbleAndEveryAirlineASmallLogo()
        {
            var gi = GameInstall.TryLocate();
            string path = gi?.Find(GameInstall.Folders.Gli, "glstd.gli");
            if (path == null) return;
            var gli = GliLibrary.Load(path);
            for (int m = 0; m <= Mood.Stars3FC; m++) Assert.True(gli.Contains("MOOD" + Mood.BitmapNumber(m).ToString("00")), "mood " + m);
            for (int p = 0; p < Player.Count; p++) Assert.True(gli.Contains("SLOGO" + p), "logo " + p);
        }
    }
}
