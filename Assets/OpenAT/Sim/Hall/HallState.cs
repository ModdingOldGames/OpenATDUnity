using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Links the hall to the flight domain (planes, flight plans, gates). The default (null) means no
    /// flights: departing passengers are never spawned and stray ones leave. Implemented by the
    /// planes/gates domain.
    /// </summary>
    public interface IHallFlightLink
    {
        /// <summary>Gate (−1 none) and departure hour of a flight plan entry; false when it does not exist.</summary>
        bool TryGetFlight(int airline, int planeId, int flightIndex, out int gate, out int startHour);
        /// <summary>True when the plane stands at its gate behind the windows (Ort −3 at its target x).</summary>
        bool PlaneAtGate(int airline, int planeId);
        /// <summary>Security flags of an airline (bit 8 enhanced gate, 10/11 gate videos).</summary>
        int SecurityFlags(int airline);
        /// <summary>Called every 32 ticks (offset 16) to add departing passengers (§6.6) through
        /// <see cref="HallState.AddPersonToQueue"/>.</summary>
        void AddNewPassengers(World w, HallState hall);
        /// <summary>Owner of gate <paramref name="gate"/> or −1 (logo bars, counters).</summary>
        int GateOwner(int gate);
        /// <summary>Text of flight display <paramref name="index"/> (0..29 check-in, 30..59 gates), "" when blank.</summary>
        string FlightDisplayText(int index);
    }

    public sealed class DoorState { public int State, Dir, Winkel; }

    /// <summary>
    /// Dynamic hall state of one world: the live walk grid (occupancy, stench, glue, condition blocks),
    /// seats, doors, triggers, persons and the person queue. Advanced by <see cref="HallSystem"/>.
    /// </summary>
    public sealed partial class HallState
    {
        public readonly World World;
        /// <summary>Static layout (derived from the hall content); rebuilt on load (HallState.Save.cs).</summary>
        [field: Save.NotSaved]
        public HallLayout Layout { get; private set; }
        public HallData Data => Layout.Data;

        /// <summary>Walk grid incl. 20 spare columns (index = row + 16·(col + 20)).</summary>
        public byte[] Plates;
        public byte[] PlateDir;
        public ushort[] Seats;
        public DoorState[] Doors;
        /// <summary>Per trigger: incremented when the one-shot animation starts (the view times it).</summary>
        public int[] TriggerSerial;

        /// <summary>Persons in depth order after each step (§3.5).</summary>
        public readonly List<Person> Persons = new List<Person>();
        public readonly PersonQueue Queue = new PersonQueue();
        /// <summary>Passenger marker occupancy (spots 200..238, belts) and tentative room counts (§5.4).</summary>
        public readonly int[] RoomBusy = new int[256];
        public readonly HallPlayer[] Players = new HallPlayer[Player.Count];
        /// <summary>Clan in game today (bit 0), "flipped today" (bit 7) during the daily rotation.</summary>
        public readonly int[] TodayInGame = new int[HallData.MaxClans];
        public bool AnyPersonsInAirport;
        public bool IgnoreNextDoor;
        public IHallFlightLink Flights;
        /// <summary>Who occupies a room (default: any hall player whose <see cref="HallPlayer.Room"/> is it).
        /// The room framework may replace it with its own location stacks.</summary>
        public Func<int, int, bool> IsRoomBusy;

        int _nextUid = 1;
        int _cosmetic;

        /// <summary>Deterministic stand-in for the original's C rand() in cosmetic places (moods).</summary>
        internal TeakRand CosmeticRand() => new TeakRand(Clock.Date * 7919 + Clock.Time + 31 * ++_cosmetic);

        public HallState(World world, HallLayout layout)
        {
            World = world;
            for (int i = 0; i < Players.Length; i++) Players[i] = new HallPlayer(i);
            IsRoomBusy = DefaultIsRoomBusy;
            ResetLayout(layout);
            for (int s = 0; s < HallData.MaxClans; s++)
            {
                var c = Data.Clans[s];
                TodayInGame[s] = c != null && (c.Type > 11 || c.Group > 0) ? 1 : 0;
            }
        }

        /// <summary>Installs a (new) layout and resets the dynamic grid from it (airport expansion rebuilds).</summary>
        public void ResetLayout(HallLayout layout)
        {
            Layout = layout;
            Plates = (byte[])layout.InitialPlates.Clone();
            PlateDir = new byte[Plates.Length];
            for (int i = 0; i < PlateDir.Length; i++) PlateDir[i] = 255;
            Seats = (ushort[])layout.InitialSeats.Clone();
            Doors = new DoorState[layout.Doors.Count];
            for (int i = 0; i < Doors.Length; i++) Doors[i] = new DoorState();
            TriggerSerial = new int[layout.Triggers.Count];
        }

        /// <summary>Default <see cref="IsRoomBusy"/>: any active hall player other than <paramref name="except"/> is in the room.</summary>
        bool DefaultIsRoomBusy(int room, int except)
        {
            foreach (var p in Players) if (p.Index != except && !World.Players[p.Index].IsOut && p.Room == room) return true;
            return false;
        }

        public int LeftEnd => Layout.LeftEnd;
        public int RightEnd => Layout.RightEnd;
        GameClock Clock => World.Clock;

        // ------------------------------------------------------------------ grid access

        /// <summary>Index into <see cref="Plates"/> for (column, row); the original's row + (col &lt;&lt; 4).</summary>
        public static int PI(int x, int y) => y + ((x + Plate.SpareColumns) << 4);

        public int P(int x, int y) { int i = PI(x, y); return (uint)i < (uint)Plates.Length ? Plates[i] : 0; }
        public void POr(int x, int y, int bits) { int i = PI(x, y); if ((uint)i < (uint)Plates.Length) Plates[i] = (byte)(Plates[i] | bits); }
        public void PAnd(int x, int y, int mask) { int i = PI(x, y); if ((uint)i < (uint)Plates.Length) Plates[i] = (byte)(Plates[i] & mask); }
        public int PD(int x, int y) { int i = PI(x, y); return (uint)i < (uint)PlateDir.Length ? PlateDir[i] : 255; }
        public void SetPD(int x, int y, int v) { int i = PI(x, y); if ((uint)i < (uint)PlateDir.Length) PlateDir[i] = (byte)v; }
        int Seat(int x) { int i = x + Plate.SpareColumns; return (uint)i < (uint)Seats.Length ? Seats[i] : 0xFFFF; }
        void SeatOr(int x, int bits) { int i = x + Plate.SpareColumns; if ((uint)i < (uint)Seats.Length) Seats[i] = (ushort)(Seats[i] | bits); }

        // ------------------------------------------------------------------ clans

        public ClanDef Clan(int slot) => (uint)slot < (uint)Data.Clans.Length ? Data.Clans[slot] : null;

        /// <summary>Weighted random customer clan (§6.1 GetCustomerId). <paramref name="koffer"/> 99 = without luggage.</summary>
        public int CustomerId(int browned, int koffer, TeakRand rand)
        {
            int num = 0;
            for (int c = 0; c < HallData.MaxClans; c++) if (CustomerMatch(c, browned, koffer)) num += Data.Clans[c].Wkeit;
            int rnd = rand.Rand(num);
            num = 0;
            for (int c = 0; c < HallData.MaxClans; c++)
                if (CustomerMatch(c, browned, koffer))
                {
                    num += Data.Clans[c].Wkeit;
                    if (num > rnd) return c;
                }
            return -1;
        }

        bool CustomerMatch(int c, int browned, int koffer)
        {
            var k = Data.Clans[c];
            if (k == null || (TodayInGame[c] & 127) == 0) return false;
            bool kofferOk = koffer == Math.Sign(k.HasSuitcase) || (koffer == 99 && k.HasSuitcase <= 0);
            bool white = (k.Type == ClanType.Female || k.Type == ClanType.Male) && browned < 2;
            bool brown = (k.Type == ClanType.BrownFemale || k.Type == ClanType.BrownMale) && browned > 0;
            return kofferOk && (white || brown);
        }

        public int CustomerIdByGroup(int group)
        {
            int num = 0;
            for (int c = 0; c < HallData.MaxClans; c++) if (Data.Clans[c] != null && (TodayInGame[c] & 127) != 0 && Data.Clans[c].Group == group) num++;
            int rnd = World.PersonRandCreate.Rand(num);
            num = 0;
            for (int c = 0; c < HallData.MaxClans; c++)
                if (Data.Clans[c] != null && (TodayInGame[c] & 127) != 0 && Data.Clans[c].Group == group)
                    if (++num > rnd) return c;
            return -1;
        }

        public int SuitcaseId(int koffer)
        {
            for (int c = 0; c < HallData.MaxClans; c++) if (Data.Clans[c] is ClanDef k && k.Type == ClanType.Suitcase && k.HasSuitcase == koffer) return c;
            return -1;
        }

        public int PlayerClanId(int player)
        {
            for (int c = 0; c < HallData.MaxClans; c++) if (Data.Clans[c] is ClanDef k && k.Type == ClanType.Player1 + player) return c;
            return -1;
        }

        static bool Rotates(ClanDef k) => k != null && k.Type <= 11 && k.Group <= 0 && k.HasSuitcase >= 0;

        /// <summary>Daily rotation of the customer clans (§6.1 "Daily rotation"), using PersonRandCreate.</summary>
        public void UpdateClansInGame(bool firstDay)
        {
            var rand = World.PersonRandCreate;
            int anz = 0;
            for (int c = 0; c < HallData.MaxClans; c++) if (Rotates(Data.Clans[c])) anz++;
            if (anz == 0) return;
            int added = 0, removed = 0;
            for (int n = 0; n < 10; n++)
            {
            again:
                int r = rand.Rand(anz);
                for (int c = 0; c < HallData.MaxClans; c++)
                {
                    var k = Data.Clans[c];
                    if (!Rotates(k) || r-- != 0) continue;
                    if (k.Wkeit == 1 && rand.Rand(20) != 0) goto again;
                    if (TodayInGame[c] >= 128) { n--; break; }
                    if (!firstDay && ((TodayInGame[c] != 0 && removed >= 5) || (TodayInGame[c] == 0 && added >= 5))) { n--; break; }
                    int v = TodayInGame[c] != 0 ? 0 : 1;
                    if (v == 0) removed++; else added++;
                    TodayInGame[c] = v + 128;
                    if (k.HasSuitcase != 0)
                        for (int d = 0; d < HallData.MaxClans; d++)
                            if (Data.Clans[d] is ClanDef t && t.Type <= 11 && t.Group <= 0 && k.HasSuitcase == -t.HasSuitcase) TodayInGame[d] = v + 128;
                    break;
                }
            }
            for (int c = 0; c < HallData.MaxClans; c++) TodayInGame[c] &= 127;
        }

        // ------------------------------------------------------------------ rune queries (§4.4)

        /// <summary>Random rune of a type/par (iterates from the end like the original). Null when none.</summary>
        public Pt? RandomRune(int id, int par, TeakRand rand)
        {
            var buf = new List<int>(20);
            var runes = Layout.Runes;
            for (int c = runes.Count - 1; c >= 0; c--)
                if (runes[c].Id == id && runes[c].Par == par) { buf.Add(c); if (buf.Count == 20) break; }
            if (buf.Count == 0) return null;
            if (buf.Count == 1) return runes[buf[0]].Pos;
            return runes[buf[(rand ?? CosmeticRand()).Rand(buf.Count)]].Pos;
        }

        public Pt RuneOr(int id, int par, TeakRand rand, Pt fallback) => RandomRune(id, par, rand) ?? fallback;

        public int RuneParNear(Pt pos, Pt maxDist, int runeId)
        {
            var runes = Layout.Runes;
            for (int c = runes.Count - 1; c >= 0; c--)
                if (runes[c].Id == runeId && Math.Abs(runes[c].Pos.X - pos.X) <= maxDist.X && Math.Abs(runes[c].Pos.Y - pos.Y) <= maxDist.Y)
                    return runes[c].Par;
            return 0;
        }

        /// <summary>Best staircase approach rune (par, par+2, par+4) for a walk between two x positions (§6.4).</summary>
        public Pt BestStairs(int par, int x1, int x2)
        {
            int best = int.MaxValue; Pt res = default;
            foreach (var r in Layout.Runes)
                if (r.Id == RuneId.ToShop && (r.Par == par || r.Par == par + 2 || r.Par == par + 4))
                {
                    int d = Math.Abs(r.Pos.X - x1) + Math.Abs(r.Pos.X - x2);
                    if (d < best) { best = d; res = r.Pos; }
                }
            return res;
        }

        /// <summary>Next waypoint of a walker route (§6.6); <paramref name="gimmick"/> carries pause/gimmick state.</summary>
        public Pt NextWaypoint(int start, ref int current, ref int gimmick)
        {
            var runes = Layout.Runes;
            for (int c = runes.Count - 1; c >= 0; c--)
                if (runes[c].Id == RuneId.WaypointWait && runes[c].Par == current) gimmick = 100;
            for (int pass = 0; pass < 2; pass++)
            {
                int want = pass == 0 ? current + 1 : start;
                for (int c = runes.Count - 1; c >= 0; c--)
                {
                    var r = runes[c];
                    if ((r.Id == RuneId.Waypoint || r.Id == RuneId.WaypointWait || r.Id == RuneId.WaypointGimmick) && r.Par == (want & 0xFF))
                    {
                        current = want & 0xFF;
                        if (gimmick <= 1) gimmick = r.Id == RuneId.WaypointGimmick ? 1 : 0;
                        return r.Pos;
                    }
                }
            }
            return new Pt(0, 0);
        }

        /// <summary>Room id of the marked area under a hall pixel position (§4.5), 0 if none.</summary>
        public int MarkedArea(Pt pos)
        {
            var areas = Layout.Areas;
            int best = int.MaxValue, room = 0; bool any = false;
            for (int c = areas.Count - 1; c >= 0; c--)
            {
                var a = areas[c];
                if (pos.X < a.P1.X || pos.Y < a.P1.Y || pos.X > a.P2.X || pos.Y > a.P2.Y) continue;
                int size = a.P2.X - a.P1.X;
                any = true;
                if (size > 0 && size < best) { best = size; room = a.Par; }
            }
            if (!any) return 0;
            if (room >= RoomIds.FlightDisplay && room < RoomIds.FlightDisplay + 60)
            {
                var text = Flights?.FlightDisplayText(room - RoomIds.FlightDisplay);
                if (string.IsNullOrEmpty(text)) return 0;
            }
            if (room == RoomIds.AntiMarker) return 0;
            return room;
        }

        /// <summary>Index of the build of a brick near a hall pixel position (searched from the end), −1 if none.</summary>
        public int BuildNear(Pt pos, Pt maxDist, int brickId)
        {
            var b = Layout.Builds;
            for (int c = b.Count - 1; c >= 0; c--)
                if (b[c].BrickId == brickId && Math.Abs(b[c].X - pos.X) <= maxDist.X && Math.Abs(b[c].Y - pos.Y) <= maxDist.Y) return c;
            return -1;
        }

        void StartTriggerNear(Pt screenPos, Pt maxDist, int brickId)
        {
            int i = BuildNear(screenPos, maxDist, brickId);
            if (i >= 0)
            {
                int par = Layout.Builds[i].Par;
                if ((uint)par < (uint)TriggerSerial.Length) TriggerSerial[par]++;
            }
        }

        // ------------------------------------------------------------------ shops (§7.3)

        static bool InRange(int v, int a, int b) => v >= a && v <= b;

        /// <summary>Picks a shop for a shopper (§7.3 GetRandomShop). Updates <paramref name="statePar"/> and mood.</summary>
        public Pt RandomShop(ref int statePar, int clanType, ref int mood, TeakRand rand, bool setMood = true)
        {
            if (InRange(statePar, RoomIds.LastMinX1, RoomIds.LastMinX1 + 3)) statePar = RoomIds.LastMinute;
            if (InRange(statePar, RoomIds.ReiseX1, RoomIds.ReiseX1 + 3)) statePar = RoomIds.Reisebuero;
            if (InRange(statePar, RoomIds.Monitor1, RoomIds.Monitor9)) statePar = RoomIds.Monitor1;
            var runes = Layout.Runes;
            var buf = new List<int>(20);
            for (int c = 0; c < runes.Count && buf.Count < 20; c++)
            {
                var r = runes[c];
                if (r.Id != RuneId.ToShop) continue;
                int p = r.Par;
                if (InRange(p, RoomIds.Shop1, RoomIds.Shop4) || p == RoomIds.Monitor1 || p == RoomIds.Museum || p == RoomIds.Bank
                    || p == RoomIds.Reisebuero || p == RoomIds.LastMinute || (p == RoomIds.WcF && clanType == ClanType.Female) || (p == RoomIds.WcM && clanType == ClanType.Male))
                    buf.Add(c);
            }
            if (buf.Count == 0) return new Pt(0, 0);
            int t = Clock.Time, wd = Clock.Weekday;
            for (int guard = 0; guard < 2000; guard++)
            {
                int c;
                // Another shop than the current one; bounded in case every candidate is the current shop.
                int spin = 0;
                do c = buf[rand.Rand(buf.Count)]; while (statePar == runes[c].Par && buf.Count != 1 && ++spin < 1000);
                int par = runes[c].Par;
                if (par == RoomIds.Shop1 && (t < OpeningHours.DutyOpen || ((wd == 5 || wd == 6) && t > OpeningHours.DutyClose - 60000))) continue;
                if (par == RoomIds.LastMinute && (t > OpeningHours.LastClose - 60000 || wd == 5)) continue;
                if (par == RoomIds.Museum && (t < OpeningHours.MuseOpen || wd == 5 || wd == 6)) continue;
                if (par == RoomIds.Reisebuero && (t > OpeningHours.ReisClose - 60000 || wd == 6)) continue;

                if (par == RoomIds.Reisebuero || par == RoomIds.LastMinute || par == RoomIds.Monitor1)
                {
                    int baseId = par == RoomIds.Reisebuero ? RoomIds.ReiseX1 : par == RoomIds.LastMinute ? RoomIds.LastMinX1 : RoomIds.Monitor1;
                    int n = rand.Rand(256);
                    for (int d = 0; d < 4; d++)
                    {
                        // The original's monitor branch checks spot (d+n)%6 but looks the rune up with (d+n)&3 (bug: the
                        // last two monitors are never used and a busy spot can be doubled); FixHallMonitorSpot uses %6 for both.
                        int busyIdx = par == RoomIds.Monitor1 ? baseId + (d + n) % 6 : baseId + ((d + n) & 3);
                        int runeIdx = par == RoomIds.Monitor1 && World.Options.FixHallMonitorSpot ? busyIdx : baseId + ((d + n) & 3);
                        if (RoomBusy[busyIdx] != 0) continue;
                        foreach (var r in runes)
                            if (r.Id == RuneId.ToShop && r.Par == runeIdx)
                            {
                                statePar = r.Par;
                                RoomBusy[busyIdx]++;
                                if (setMood) mood = par == RoomIds.Monitor1 ? Mood.Screen : Mood.Travel;
                                return r.Pos;
                            }
                    }
                    continue; // no free spot: pick again
                }

                statePar = par;
                if (setMood)
                {
                    mood = Mood.None;
                    // The original uses the C runtime rand() here; a throw-away generator keeps it deterministic
                    // without disturbing the shared pedestrian streams.
                    var mr = CosmeticRand();
                    if (par == RoomIds.Shop1) mood = mr.Rand(10) < 5 ? Mood.Beverage : Mood.Chocolate;
                    else if (par == RoomIds.Bank) mood = mr.Rand(90) == 0 ? Mood.BankRobber : Mood.Bank;
                    else if (par == RoomIds.Museum) mood = Mood.Museum;
                    else if (par == RoomIds.WcF || par == RoomIds.WcM) mood = Mood.Toilet;
                }
                return runes[c].Pos;
            }
            return runes[buf[0]].Pos;
        }

        // ------------------------------------------------------------------ doors (§8.1)

        /// <summary>Advances all doors one tick.</summary>
        public void PumpDoors()
        {
            for (int c = Doors.Length - 1; c >= 0; c--)
            {
                var d = Doors[c];
                var kind = Layout.Doors[c].Kind;
                if (kind == 1)
                {
                    if (d.Dir == 1)
                    {
                        if (d.State >= 40)
                        {
                            d.Dir = 0;
                            if (!IsRoomBusy(RoomIds.Sabotage, -1)) { d.State = 18; d.Dir = -1; }
                        }
                        else d.State += 2;
                        d.Winkel = Math.Min(18, d.State + 1) / 2;
                    }
                    else if (d.Dir == -1)
                    {
                        d.State--;
                        if (d.State == 0) { d.Dir = 0; if (IsRoomBusy(RoomIds.Sabotage, -1)) d.Dir = 1; }
                        d.Winkel = Math.Min(18, d.State + 1) / 2;
                    }
                }
                else
                {
                    int max = kind == 3 ? 22 : 18, step = kind == 3 ? 4 : 2, frames = kind == 3 ? 6 : 4;
                    if (d.Dir == 1)
                    {
                        if (d.State >= max) d.Dir = -1; else d.State += step;
                        d.Winkel = Math.Min((frames - 1) * 2, d.State + 1) / 2;
                    }
                    else if (d.Dir == -1)
                    {
                        d.State--;
                        if (d.State == 0)
                        {
                            d.Dir = 0;
                            if (kind != 3) World.Events.Publish(new HallSound(HallSound.DoorClose, Layout.Doors[c].Tile.X * 44));
                        }
                        d.Winkel = Math.Min((frames - 1) * 2, d.State + 1) / 2;
                    }
                }
            }
        }

        /// <summary>Opens doors next to a person crossing a tile centre (§8.1 TryDoor).</summary>
        public void TryDoor(Pt tile, bool player, int playerNum)
        {
            if (World.CallItADay) return;
            for (int c = Doors.Length - 1; c >= 0; c--)
            {
                var info = Layout.Doors[c];
                var d = Doors[c];
                if (info.Kind == 2)
                {
                    if (!player) continue;
                    var hp = Players[playerNum];
                    if (hp.ExRoom != RoomIds.Fracht && hp.DirectToRoom != RoomIds.Fracht) continue;
                }
                if (info.Kind == 1)
                {
                    if (info.Tile.X == tile.X && Math.Abs(info.Tile.Y - tile.Y) <= 1)
                    {
                        if (d.Dir == 0 && d.State == 0) { d.Dir = 1; World.Events.Publish(new HallSound(HallSound.SabotageDoor, info.Tile.X * 44)); }
                        else if (d.Dir == 0 && d.State >= 40 && tile.Y != 9) { d.State = 18; d.Dir = -1; World.Events.Publish(new HallSound(HallSound.SabotageDoor, info.Tile.X * 44)); }
                    }
                    continue;
                }
                bool hit = (info.Tile.X == tile.X && ((info.Tile.Y == tile.Y - 1 && !player) || info.Tile.Y == tile.Y))
                    || (info.Tile.X == tile.X && info.Tile.Y == tile.Y + 4 && info.Kind == 3)
                    || (info.Tile.X == tile.X + 1 && info.Tile.Y == tile.Y)
                    || (info.Tile.X == tile.X - 1 && info.Tile.Y == tile.Y);
                if (!hit) continue;
                // (Mined offices, §8.1, belong to the sabotage domain; not handled here.)
                if (d.Dir != 1 && d.State == 0)
                {
                    if (!IgnoreNextDoor && info.Kind != 3) World.Events.Publish(new HallSound(HallSound.DoorOpen, info.Tile.X * 44));
                    IgnoreNextDoor = false;
                }
                d.Dir = 1;
            }
        }

        /// <summary>Toggles a condition block (closed shop, destroyed office; §4.4 rune 2070).</summary>
        public void SetConditionBlock(int id, bool blocking)
        {
            foreach (var r in Layout.Runes)
            {
                if (r.Id != RuneId.CondBlock || r.Par != id) continue;
                var b = new Build { Brick = r.Brick, X = r.Raw.X, Y = r.Raw.Y };
                int x = Layout.TileX(b, 0, 0), y = Layout.TileY(b, 0);
                if (blocking) { PAnd(x, y - 1, ~32); PAnd(x, y, ~128); }
                else { POr(x, y - 1, 32); POr(x, y, 128); }
            }
        }

        // ------------------------------------------------------------------ persons

        /// <summary>Creates a person with the original's start state per reason (§6.6 PERSON ctor).</summary>
        public Person CreatePerson(int clanId, Pt position, int reason, int flightAirline, int flightPlaneId, int flightPlaneIndex, int mood = 0, bool firstClass = false)
        {
            var k = Clan(clanId);
            if ((k == null || k.Type <= ClanType.Player1 || k.Type >= ClanType.Player4) && mood == 0) mood = Mood.None;
            var p = new Person
            {
                Uid = _nextUid++,
                ClanId = clanId, Dir = 2, LookDir = 0, Phase = 0, WaitCount = 0, Mood = mood, MoodCountdown = 0,
                Reason = reason, FlightAirline = flightAirline, FlightPlaneId = flightPlaneId, FlightPlaneIndex = flightPlaneIndex,
                Position = position, State = 0, StatePar = 0, Running = false, FirstClass = firstClass, FloorOffset = k?.FloorOffset ?? 0,
            };
            int seed = Clock.Hour + Clock.Minute / 5 + clanId;
            p.PersonalRand.SetSeed(seed);
            p.PersonalRandWalk.SetSeed(seed);
            p.ScreenPos = new Pt(position.X - position.Y / 2 + 90, position.Y + 220);
            var rc = World.PersonRandCreate;
            switch (reason)
            {
                case Reason.Shopping:
                {
                    p.State = PState.ToShop;
                    p.Target = position + new Pt(0, -22);
                    int sp = p.StatePar, m = p.Mood;
                    p.Target = RandomShop(ref sp, k?.Type ?? 0, ref m, rc);
                    p.StatePar = sp; p.Mood = m;
                    break;
                }
                case Reason.Leaving:
                    p.State = PState.LeavingPlane;
                    p.Target = position + new Pt(88, 0);
                    break;
                case Reason.Flying:
                {
                    int gate = -2, hour = 0;
                    bool ok = Flights != null && Flights.TryGetFlight(flightAirline, flightPlaneId, flightPlaneIndex, out gate, out hour);
                    if (!ok || gate == -2)
                    {
                        p.State = PState.Leaving;
                        p.Target = position;
                    }
                    else
                    {
                        p.State = PState.ToCheckIn;
                        p.Target = RuneOr(RuneId.ToCheckIn, gate, rc, position);
                    }
                    break;
                }
                case Reason.SuitcaseExit:
                    p.Dir = 0;
                    p.State = PState.Browsing;
                    p.Target = RuneOr(RuneId.ExitSuitcase, 0, rc, position);
                    StartTriggerNear(p.ScreenPos, new Pt(180, 80), 471);
                    break;
                case Reason.Waypoint:
                    p.State = PState.Waypointing;
                    p.Target = RuneOr(RuneId.Waypoint, flightAirline, rc, position);
                    break;
            }
            if (k != null && k.IsPlayer)
            {
                p.State = k.Type - ClanType.Player1;
                Players[p.State].WalkActive = false;
            }
            return p;
        }

        /// <summary>Adds a person directly (no queue). The new person sorts in at the next step.</summary>
        public Person AddPerson(Person p) { Persons.Add(p); return p; }

        /// <summary>The figure of a player, or null.</summary>
        public Person PlayerPerson(int player)
        {
            for (int c = Persons.Count - 1; c >= 0; c--)
            {
                var p = Persons[c];
                if (p.Alive && Clan(p.ClanId) is ClanDef k && k.Type == ClanType.Player1 + player) return p;
            }
            return null;
        }

        public int NumShoppers()
        {
            int n = 0;
            foreach (var p in Persons) if (p.Alive && Clan(p.ClanId) is ClanDef k && k.Type < ClanType.Player1 && p.Reason == Reason.Shopping) n++;
            return n;
        }

        public int NumUsed { get { int n = 0; foreach (var p in Persons) if (p.Alive) n++; return n; } }

        /// <summary>Queues a person (only 08:00-18:00, §6.6). Used by the flight domain for passengers.</summary>
        public void AddPersonToQueue(int clanId, Pt position, int reason, int flightAirline, int flightPlaneId, int flightPlaneIndex, int mood = 0, bool firstClass = false)
        {
            if (World.CallItADay || Clock.Time > 18 * 60000 || Clock.Time < 8 * 60000) return;
            if (clanId < 0 || Clan(clanId) == null) return;
            AnyPersonsInAirport = true;
            Queue.Add(clanId, position, reason, flightAirline, flightPlaneId, flightPlaneIndex, mood, firstClass,
                Clan(clanId)?.Type == ClanType.Suitcase, Clock.TimeSlice);
        }

        // ------------------------------------------------------------------ day start

        /// <summary>
        /// Start of a day in the hall (§6.6 "New day"): all persons removed, player figures at their
        /// PCREATION runes, one walker per WAYPOINT_START rune.
        /// </summary>
        public void NewDay()
        {
            Persons.Clear();
            for (int c = 0; c < Player.Count; c++)
            {
                if (World.Players[c].IsOut) continue;
                int clan = PlayerClanId(c);
                if (clan < 0) continue;
                var pos = RuneOr(RuneId.PCreation, c + 1, World.PersonRandCreate, new Pt(LeftEnd + 400 + 44 * c, 121));
                Persons.Add(CreatePerson(clan, pos, Reason.Player, 99, 0, 0));
            }
            foreach (var b in Layout.Builds)
            {
                if (b.BrickId != RuneId.WaypointStart) continue;
                int clan = CustomerIdByGroup(b.Par);
                if (clan < 0) continue;
                var start = RuneOr(RuneId.WaypointStart, b.Par, World.PersonRandCreate, new Pt(0, 0));
                if (b.Par == 240)
                {
                    var p = CreatePerson(clan, start, Reason.Waypoint, b.Par, b.Par, 0, Mood.Bone);
                    p.Position = p.Target;
                    p.WaitCount = 170;
                    Persons.Add(p);
                }
                else Persons.Add(CreatePerson(clan, start, Reason.Waypoint, b.Par, b.Par, 0));
            }
            // The figures are created anew, so a toilet run that never reached the door does not carry over.
            foreach (var hp in Players) { hp.WalkActive = false; hp.NewDir = 8; hp.DirectToRoom = 0; hp.IsStuck = 0; hp.IsWalkingToPlayer = -1; hp.RunningToToilet = false; hp.OnToilet = 0; hp.OnToiletTicks = 0; }
            DepthSort();
        }

        /// <summary>
        /// Places every figure in front of its office after the morning briefing (Aufsicht.cpp; §4.4
        /// PCREATION2: par 10·(p+1), or 10·(p+5) on day 0). Called by the room framework when the
        /// briefing is left; also used by the hall test boot.
        /// </summary>
        public void PlaceAfterBriefing()
        {
            for (int c = 0; c < Player.Count; c++)
            {
                if (World.Players[c].IsOut) continue;
                var p = PlayerPerson(c);
                if (p == null) continue;
                int par = Clock.Date != 0 ? (c + 1) * 10 : (c + 5) * 10;
                var pos = RandomRune(RuneId.PCreation2, par, World.PersonRandCreate) ?? RandomRune(RuneId.PCreation2, (c + 1) * 10, World.PersonRandCreate);
                if (pos == null) continue;
                p.Position = pos.Value;
                p.MoodCountdown = 0;
                p.ScreenPos = new Pt(p.Position.X - (p.Position.Y - 5000) / 2 + 86, p.Position.Y - 5000 + 93);
                p.LookDir = c == 0 || c == 1 ? 3 : 1;
                p.Dir = p.LookDir * 2;
                p.StatePar = 0;
                p.Target = p.Position;
                var hp = Players[c];
                hp.WalkActive = false; hp.NewDir = 8; hp.DirectToRoom = 0;
                hp.PrimaryTarget = hp.SecondaryTarget = hp.TertiaryTarget = TileOfPosition(p.Position);
            }
            DepthSort();
        }

        /// <summary>Tile (col, row 0..15) of a logical position (player rules: truncating division).</summary>
        public static Pt TileOfPosition(Pt p) => p.Y >= 4000 ? new Pt(p.X / 44, (p.Y - 5000) / 22) : new Pt(p.X / 44, p.Y / 22 + 5);
    }

    /// <summary>The pending-spawn queue (§6.6 CPersonQueue).</summary>
    public sealed class PersonQueue
    {
        public struct Entry
        {
            public int TimeSlice;
            public int ClanId, Reason, FlightAirline, FlightPlaneId, FlightPlaneIndex, Mood;
            public bool FirstClass;
            public Pt Position;
        }
        struct Spot { public Pt Position; public int TimeSlice; }

        readonly List<Entry> _entries = new List<Entry>();
        readonly List<Spot> _spots = new List<Spot>();

        public IReadOnlyList<Entry> Entries => _entries;

        public void Clear() { _entries.Clear(); _spots.Clear(); }

        /// <summary>First spawn at a spot 10 ticks later (luggage at once), then 20 ticks apart.</summary>
        internal void Add(int clan, Pt pos, int reason, int fa, int fp, int fi, int mood, bool fc, bool suitcase, int timeSlice)
        {
            int s = -1;
            for (int c = 0; c < _spots.Count; c++) if (_spots[c].Position == pos) { s = c; break; }
            if (s < 0) { _spots.Add(new Spot { Position = pos, TimeSlice = -1 }); s = _spots.Count - 1; }
            var spot = _spots[s];
            int when;
            if (spot.TimeSlice == -1 || spot.TimeSlice < timeSlice) when = spot.TimeSlice = suitcase ? timeSlice : timeSlice + 10;
            else { spot.TimeSlice += 20; when = spot.TimeSlice; }
            _spots[s] = spot;
            var e = new Entry { TimeSlice = when, ClanId = clan, Position = pos, Reason = reason, FlightAirline = fa, FlightPlaneId = fp, FlightPlaneIndex = fi, Mood = mood, FirstClass = fc };
            // The original fills the first free slot; Pump scans from the end.
            int free = _entries.FindIndex(x => x.TimeSlice == -1);
            if (free >= 0) _entries[free] = e; else _entries.Add(e);
        }

        /// <summary>Forces the spot time (arriving passengers, network sync).</summary>
        public void SetSpotTime(Pt pos, int timeSlice)
        {
            for (int c = 0; c < _spots.Count; c++)
                if (_spots[c].Position == pos) { var s = _spots[c]; s.TimeSlice = timeSlice; _spots[c] = s; return; }
            _spots.Add(new Spot { Position = pos, TimeSlice = timeSlice });
        }

        public void ResetSpotTimes()
        {
            for (int c = 0; c < _spots.Count; c++) { var s = _spots[c]; s.TimeSlice = -1; _spots[c] = s; }
        }

        /// <summary>Takes at most one due entry (scanning from the end, like the original).</summary>
        internal bool TakeDue(int timeSlice, out Entry e)
        {
            for (int c = _entries.Count - 1; c >= 0; c--)
            {
                if (_entries[c].TimeSlice != -1 && _entries[c].TimeSlice <= timeSlice)
                {
                    e = _entries[c];
                    var x = _entries[c]; x.TimeSlice = -1; _entries[c] = x;
                    return true;
                }
            }
            e = default;
            return false;
        }
    }

    /// <summary>A hall sound cue (§10) at a hall x position; the view decides on distance/volume.</summary>
    public sealed class HallSound : ISimEvent
    {
        public const string DoorOpen = "Tuerauf.raw", DoorClose = "Tuerzu.raw", SabotageDoor = "sabotage.raw";
        public readonly string File;
        public readonly int X;
        public HallSound(string file, int x) { File = file; X = x; }
    }
}
