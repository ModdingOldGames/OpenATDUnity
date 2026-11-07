using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Player figure walking (airport-and-people.md §6.4, rooms-and-ui-9 §5.3): click → tile targets,
    /// waypoint steering, per-tile movement in 8 directions, stairs as pseudo-rooms, the walk-in
    /// protocol to rooms. Reaching a room's RUNE_SHOP publishes <see cref="RoomRequested"/>.
    /// </summary>
    public sealed partial class HallState
    {
        /// <summary>True while the player's UI has a menu or dialog open (blocks some walk-in steps). Set by the room framework.</summary>
        public Func<int, bool> UiBusy = _ => false;

        // ------------------------------------------------------------------ targets (Player.cpp walking)

        /// <summary>Opening-hours check of a room for a walk request (rooms-and-ui-9 §5.2). Returns the closed-menu index or −1.</summary>
        public int ClosedMenu(int room)
        {
            int t = Clock.Time, wd = Clock.Weekday;
            if (room == RoomIds.Shop1 && (t < OpeningHours.DutyOpen || ((wd == 5 || wd == 6) && t > OpeningHours.DutyClose))) return 0;
            if (room == RoomIds.ArabAir && (t < OpeningHours.ArabOpen || wd == 6)) return 1;
            if (room == RoomIds.LastMinute && (t > OpeningHours.LastClose || wd == 5)) return 2;
            if (room == RoomIds.Museum && (t < OpeningHours.MuseOpen || wd == 5 || wd == 6)) return 3;
            if (room == RoomIds.Reisebuero && t > OpeningHours.ReisClose) return 4;
            if (room == RoomIds.Makler && t > OpeningHours.MaklClose) return 5;
            int diff = World.Difficulty;
            if (room == RoomIds.Werbung && (diff >= 2 || diff == Mission.FreeGame) && (t < OpeningHours.WerbOpen || wd == 5 || wd == 6)) return 6;
            // Security office power outage after the pliers (items-security-special.md §2.5): MENU_CLOSED_NOSECURITY.
            if (room == RoomIds.Security && World.Items != null && World.Items.SecurityOutageDays > 0) return 8;
            return -1;
        }

        /// <summary>Walks a player to a room's approach rune (WalkToRoom). False when closed or unknown.</summary>
        public bool WalkToRoom(int player, int room)
        {
            var hp = Players[player];
            if (hp.RunningToToilet) return true;
            if (World.Players[player].IsOut) return false;
            int closed = ClosedMenu(room);
            if (closed >= 0)
            {
                World.Events.Publish(new HallRequest(player, HallRequest.Closed, room, closed));
                return false;
            }
            // A bombed office (rooms-and-ui-9 §5.6): a human gets the "destroyed" notice and, once per day for the whole
            // airport, the glowing coal; the walk goes on (the condition blocks stop it at the ruin).
            int officeOwner = global::OpenAT.Sim.Items.ItemRooms.OfficeOwner(room);
            if (officeOwner >= 0 && World.Players[officeOwner].OfficeState == 2 && World.Players[player].Owner == Owner.Human && !UiBusy(player))
            {
                global::OpenAT.Sim.Items.ItemEngine.TryPickUp(World, World.Players[player], global::OpenAT.Sim.Items.ItemId.GlowingCoal);
                World.Events.Publish(new HallRequest(player, HallRequest.Destroyed, room));
            }
            var target = RandomRune(RuneId.ToShop, room, World.PersonRandCreate);
            if (target == null) return false;
            var t = target.Value;
            hp.PrimaryTarget = new Pt(t.X / 44, t.Y < 4000 ? t.Y / 22 + 5 : (t.Y - 5000 + 2200) / 22 - 100);
            hp.WaitForRoom = 0;
            hp.DirectToRoom = room;
            UpdateWaypoints(player);
            return true;
        }

        /// <summary>Click in the hall at a hall pixel position (WalkToMouseClick, §6.4 "Click to walk").</summary>
        public void WalkToClick(int player, Pt click)
        {
            var hp = Players[player];
            int room = MarkedArea(click);
            if (hp.RunningToToilet) return;
            if (room == RoomIds.Wall)
            {
                click.Y = click.Y < 150 ? 138 : 221 + 66;
                room = 0;
            }
            if (room != 0 && (room < RoomIds.FlightDisplay || room > RoomIds.FlightDisplay + 59))
            {
                WalkToRoom(player, room);
                return;
            }
            if (click.Y > 170 && click.Y <= 220) click.Y = 221;
            if (click.Y > -20 && click.Y <= 93) click.Y = 94;
            hp.WaitForRoom = 0;
            hp.DirectToRoom = 0;
            var pt = hp.PrimaryTarget;
            if (click.Y > 220 && click.Y < 440)
            {
                int py = click.Y - 220, px = click.X + py / 2 - 86;
                pt = new Pt((px + 4400) / 44 - 100, (py + 2200) / 22 - 100 + 5);
            }
            else if (click.Y > 93 && click.Y < 170)
            {
                int py = click.Y - 93, px = click.X + py / 2 - 88;
                pt = new Pt((px + 4400) / 44 - 100, (py + 2200) / 22 - 100);
            }
            // Blocked target tile: try south of it; rows 3 and 15 mean "no floor here".
            while (pt.Y != 3 && pt.Y != 15)
            {
                int v = P(pt.X, pt.Y);
                if ((v & Plate.Players) != 0 && (v & Plate.Exits) != 0) break;
                pt.Y++;
                if (pt.Y > 15) { hp.WalkActive = false; return; }
            }
            if (pt.Y == 3 || pt.Y == 15) { hp.WalkActive = false; return; }
            if (pt.Y < 3)
            {
                int y = pt.Y;
                while (y < 3 && (P(pt.X, y) & Plate.ExitS) != 0) y++;
                if (y == 3) { hp.WalkActive = false; return; } // target in mid-air
            }
            hp.PrimaryTarget = pt;
            UpdateWaypoints(player);
        }

        /// <summary>Walks to a tile (WalkToPlate).</summary>
        public void WalkToTile(int player, Pt tile)
        {
            var hp = Players[player];
            if (hp.RunningToToilet) return;
            hp.PrimaryTarget = tile;
            UpdateWaypoints(player);
        }

        /// <summary>Secondary (narrow pocket fix) and tertiary (stairs) targets (UpdateWaypoints).</summary>
        public void UpdateWaypoints(int player)
        {
            var hp = Players[player];
            var person = PlayerPerson(player);
            hp.WalkActive = true;
            var pt = hp.PrimaryTarget;
            hp.SecondaryTarget = pt;

            int xs = 0;
            for (int cx = 0; cx < 4; cx++) if ((P(pt.X + cx, pt.Y) & 64) != 0) xs++; else break;
            for (int cx = 0; cx < 4; cx++) if ((P(pt.X - cx, pt.Y) & 16) != 0) xs++; else break;
            if (xs < 4)
            {
                int refy = 0;
                for (; refy < 3; refy++) if ((P(pt.X, pt.Y + refy) & 32) == 0) break;
                if (refy < 3)
                {
                    for (int cx = 1; cx < 3; cx++)
                    {
                        if ((P(pt.X + cx, pt.Y) & 16) == 0) break;
                        int cy = 0;
                        for (; cy < 3; cy++) if ((P(pt.X + cx, pt.Y + cy) & 32) == 0) break;
                        if (cy > refy) { hp.SecondaryTarget.X += cx; refy = cy; }
                    }
                    for (int cx = 1; cx < 3; cx++)
                    {
                        if ((P(pt.X - cx, pt.Y) & 64) == 0) break;
                        int cy = 0;
                        for (; cy < 3; cy++) if ((P(pt.X - cx, pt.Y + cy) & 32) == 0) break;
                        if (cy > refy) { hp.SecondaryTarget.X -= cx; refy = cy; }
                    }
                }
            }

            if (person == null) { hp.TertiaryTarget = hp.SecondaryTarget; return; }
            if (hp.SecondaryTarget.Y < 5 && person.Position.Y < 5000)
            {
                var s = BestStairs(RoomIds.Stairs1Up, pt.X * 44, person.Position.X);
                hp.TertiaryTarget = new Pt(s.X / 44, s.Y / 22 + 5);
            }
            else if (hp.DirectToRoom != RoomIds.Security && hp.SecondaryTarget.Y >= 5 && person.Position.Y >= 5000)
            {
                var s = BestStairs(RoomIds.Stairs1Down, pt.X * 44, person.Position.X);
                hp.TertiaryTarget = new Pt(s.X / 44, (s.Y - 5000) / 22);
            }
            else hp.TertiaryTarget = hp.SecondaryTarget;

            if (!World.CallItADay && person.StatePar == 0 && hp.IsStuck == 0)
                person.Target = TileTarget(hp.TertiaryTarget);
        }

        /// <summary>Logical centre of a tile (rows 0..4 upstairs, 5..15 downstairs).</summary>
        public static Pt TileTarget(Pt t) => new Pt(t.X * 44 + 22, t.Y < 5 ? t.Y * 22 + 5000 + 11 : (t.Y - 5) * 22 + 11);

        /// <summary>Per-tick steering: E/W until the column matches, then N/S (UpdateWaypointWalkingDirection).</summary>
        public void UpdateWalkingDirection(int player)
        {
            if (World.Players[player].IsOut) return;
            var hp = Players[player];
            if (!hp.WalkActive) return;
            var person = PlayerPerson(player);
            if (person == null) return;
            for (int guard = 0; guard < 4; guard++)
            {
                var p = person.Position;
                Pt a = p.Y > 0 && p.Y < 5000 ? new Pt((p.X + 4400) / 44 - 100, (p.Y + 2200) / 22 - 100 + 5) : new Pt((p.X + 4400) / 44 - 100, (p.Y - 5000 + 2200) / 22 - 100);
                var tt = hp.TertiaryTarget;
                if (a.Y >= 15) hp.NewDir = 0;
                else if (a.X > tt.X) hp.NewDir = 6;
                else if (a.X < tt.X) hp.NewDir = 2;
                else if (a.Y > tt.Y) hp.NewDir = 0;
                else if (a.Y < tt.Y) hp.NewDir = 4;
                else if (hp.TertiaryTarget != hp.SecondaryTarget) { hp.TertiaryTarget = hp.SecondaryTarget; continue; }
                else if (hp.PrimaryTarget != hp.SecondaryTarget) { hp.TertiaryTarget = hp.SecondaryTarget = hp.PrimaryTarget; continue; }
                else { hp.NewDir = 8; hp.WalkActive = false; }
                return;
            }
        }

        // ------------------------------------------------------------------ one step (DoOnePlayerStep)

        static Pt Step8(Pt pos, int dir, int sx, int sy)
        {
            switch (dir)
            {
                case 0: return new Pt(pos.X, pos.Y - sy * 2);
                case 1: return new Pt(pos.X + sx, pos.Y - sy);
                case 2: return new Pt(pos.X + sx, pos.Y);
                case 3: return new Pt(pos.X + sx, pos.Y + sy);
                case 4: return new Pt(pos.X, pos.Y + sy * 2);
                case 5: return new Pt(pos.X - sx, pos.Y + sy);
                case 6: return new Pt(pos.X - sx, pos.Y);
                case 7: return new Pt(pos.X - sx, pos.Y - sy);
            }
            return pos;
        }

        bool Ok(int v, int bits) => (v & bits) != 0;

        internal void PlayerStep(Person me)
        {
            var k = Clan(me.ClanId);
            int pn = me.State;
            var hp = Players[pn];
            int sx = hp.WalkSpeed * 2, sy = hp.WalkSpeed;
            int lastDir = me.Dir;

            if (hp.IsStuck > 0)
            {
                hp.IsStuck--;
                me.Running = false;
                me.Phase = (me.Phase + 1) % (FrameCount(k, me.LookDir) * k.Faktor);
                me.LookDir = 5;
                if (hp.IsStuck > 0) return;
                me.LookDir = 2; me.Phase = 0;
                if (World.Players[pn].IsHuman) me.Mood = 0;
            }

            if (me.LookDir == 4) return; // pressed against the railing by an explosion
            if (me.LookDir == 8) { me.LookDir = me.Phase % 4; me.Phase = 0; }
            else if (me.LookDir == 9)
            {
                me.Phase++;
                TryDoor(TileOfPosition(me.Position), true, pn);
                if (me.Position == me.Target && hp.NewDir == 8)
                {
                    if (me.Phase < FrameCount(k, ClanDef.WaitGimmick) * k.Faktor) return;
                    var lr = new TeakRand(Clock.Date + Clock.Time);
                    if (lr.Rand(100) < 90 || World.Players[pn].Owner == Owner.RemoteHuman) { me.Phase = 0; return; }
                    hp.StandCount = -100;
                }
                me.Dir = 8;
                me.LookDir = k.GimmickArt2;
                me.Phase = 0;
            }

            if (me.StatePar == 0) me.Target = TileTarget(hp.TertiaryTarget);

            bool up;
            Pt a;
            if (me.Position.Y < 4000)
            {
                a = new Pt(me.Position.X / 44, me.Position.Y / 22 + 5);
                up = false;
            }
            else
            {
                me.Position.Y -= 5000;
                a = new Pt(me.Position.X / 44, (me.Position.Y + 2200) / 22 - 100);
                up = true;
            }
            int upY = up ? 5000 : 0;

            var np = Step8(me.Position, me.Dir, sx, sy);

            if ((np.X + 4422) / 44 != (me.Position.X + 4422) / 44 || (np.Y + 2211) / 22 != (me.Position.Y + 2211) / 22 || me.Dir == 8)
            {
                // Glue on the upper floor (sabotage): stuck for 600 ticks.
                if (a.Y >= 0 && a.Y <= 4 && (P(a.X, a.Y) & Plate.Special) != 0)
                {
                    RemoveAnimationNear(me.Position + new Pt(0, 5000));
                    SetMood(me, Mood.Stuck, 15);
                    me.State &= ~PState.WaitFlag;
                    me.LookDir = 5; me.Phase = 0; me.Running = false;
                    hp.IsStuck = 20 * 30;
                    me.Position.Y += 5000;
                    World.Events.Publish(new HallPlayerStuck(pn));
                    return;
                }

                TryDoor(a, true, pn);
                if (me.LookDir == 4) { if (up) me.Position.Y += 5000; return; }

                if (me.Dir != 1 && me.Dir != 3 && me.Dir != 5 && me.Dir != 7)
                    me.Position = new Pt(me.Position.X / 44 * 44 + 22, (me.Position.Y + 2200) / 22 * 22 - 2200 + 11);

                int poss = a.Y >= 0 && a.Y <= 14 ? P(a.X, a.Y) & 240 : 128 + 32;
                if (a.Y >= 0 && a.Y < 15)
                {
                    if (a.Y > 0 && (P(a.X, a.Y - 1) & 4) == 0) poss &= ~128;
                    if (a.Y < 14 && (P(a.X, a.Y + 1) & 4) == 0) poss &= ~32;
                    if ((P(a.X - 1, a.Y) & 4) == 0) poss &= ~16;
                    if ((P(a.X + 1, a.Y) & 4) == 0) poss &= ~64;
                }

                if (me.StatePar != 0)
                {
                    if (!RoomIds.IsStairs(me.StatePar)) AutoDirection(me, hp, a, np);
                }
                else
                {
                    me.Dir = hp.NewDir;
                    if (a.Y >= 5 && a.Y < 15) Diagonals(me, a);
                    if (me.Dir == 8 && World.Players[pn].Owner != Owner.Computer) me.Target = me.Position + new Pt(0, upY);
                }

                // Is the direction allowed at all? (The workshop and cargo approaches ignore the grid.)
                bool exempt = me.StatePar == RoomIds.Fracht || me.StatePar == RoomIds.Werkstatt || me.StatePar == RoomIds.Nasa
                              || hp.ExRoom == RoomIds.Werkstatt || hp.ExRoom == RoomIds.Fracht;
                // Dir 8 tests bit 8 here, so a standing figure that still walks actively may be nudged
                // toward its target row (original behaviour).
                if (((128 >> (me.Dir >> 1)) & poss) == 0 && !exempt) Dodge(me, hp, a, poss);

                if (me.StatePar == 0) ApproachRune(me, hp, pn, np, upY, up, sx, sy);
            }
            else if (a.Y == 2 && me.Dir == 0) TryDoor(a, true, pn);

            // (The original's "don't walk manually while leaving a room" guard is always off: its rune test
            // contains "|| ROOM_WELTALL", which is always true and clears the found room. Kept as is.)
            if (me.Dir < 8) me.Position = Step8(me.Position, me.Dir, sx, sy);

            if (me.StatePar != 0)
            {
                if (RoomIds.IsStairs(me.StatePar)) StairsStep(me, hp, a, ref up, sx, sy);
                else AutoWalkStep(me, hp, pn, a, np, upY, up, sx, sy);
                upY = up ? 5000 : 0;
            }

            if (me.StatePar == 0 && hp.InHall) LeavingRoomCheck(me, hp, np, upY, sx, sy);

            if (me.Dir != 8)
            {
                me.LookDir = me.Dir;
                hp.DidntMove = 0;
            }
            else
            {
                me.Phase = me.LookDir;
                me.LookDir = 8;
                if (hp.IsWalkingToPlayer != -1) FaceOtherPlayer(me, hp, upY);
            }

            switch (me.Dir)
            {
                case 0: me.LookDir = 0; break;
                case 1: case 2: case 3: me.LookDir = 1; break;
                case 4: me.LookDir = 2; break;
                case 5: case 6: case 7: me.LookDir = 3; break;
                case 8:
                {
                    var prim = new Pt((me.Target.X - 22) / 44, (me.Target.Y + 22 + upY - 5000 - 11) / 22 + 1);
                    bool atPrim = hp.PrimaryTarget == prim;
                    if (me.Position.X == me.Target.X && me.Position.Y % 5000 == me.Target.Y % 5000 && atPrim)
                    {
                        if (me.StatePar == 0) { if (hp.TertiaryTarget == hp.PrimaryTarget && World.Players[pn].IsHuman) me.Running = false; }
                        else hp.WalkActive = false;
                    }
                    else if (me.Position.X == me.Target.X && me.Position.Y % 5000 > me.Target.Y % 5000 && atPrim)
                    {
                        if (me.StatePar == 0 && (P(a.X, a.Y) & 128) == 0) hp.WalkActive = false;
                    }
                    break;
                }
            }

            if (me.Dir != 8)
            {
                bool walkway = me.Position.Y / 22 + 5 == 13 && (P(me.Position.X / 44, me.Position.Y / 22 + 5) & 240) == 80;
                if (me.Running && me.Dir < 8 && !walkway)
                    me.Phase = (me.Phase + 1) % (FrameCount(k, me.LookDir + 10) * k.Faktor * 2);
                else
                    me.Phase = (me.Phase + 1) % (FrameCount(k, me.LookDir) * k.Faktor);
            }

            if (me.Dir == 8 && lastDir != 8)
            {
                var prim = new Pt((me.Target.X - 22) / 44, (me.Target.Y + 22 + upY - 5000 - 11) / 22 + 1);
                if (hp.PrimaryTarget == prim) { hp.WalkActive = false; hp.NewDir = 8; }
            }

            if (up)
            {
                me.ScreenPos = new Pt(me.Position.X - me.Position.Y / 2 + 86, me.Position.Y + 93);
                me.Position.Y += 5000;
            }
            else me.ScreenPos = new Pt(me.Position.X - me.Position.Y / 2 + 86, me.Position.Y + 220);
        }

        /// <summary>On lower rows an E/W intent becomes a diagonal when the target row differs and the corridor is clear.</summary>
        void Diagonals(Person me, Pt a)
        {
            int tRow = me.Target.Y / 22 + 5;
            int here = P(a.X, a.Y);
            if (me.Dir == 2)
            {
                if (a.Y > tRow && me.Target.X / 44 - a.X <= a.Y - tRow && Ok(here, 128) && Ok(here, 64) && Ok(P(a.X, a.Y - 1), 64)
                    && Ok(P(a.X + 1, a.Y), 128) && Ok(here, 4) && Ok(P(a.X, a.Y - 1), 4) && Ok(P(a.X + 1, a.Y), 4)) me.Dir = 1;
                else if (a.Y < tRow && me.Target.X / 44 - a.X <= tRow - a.Y && Ok(here, 32) && Ok(here, 64) && Ok(P(a.X, a.Y + 1), 64)
                    && Ok(P(a.X + 1, a.Y), 32) && Ok(here, 4) && Ok(P(a.X, a.Y + 1), 4) && Ok(P(a.X + 1, a.Y), 4)) me.Dir = 3;
            }
            else if (me.Dir == 6)
            {
                if (a.Y > tRow && a.X - me.Target.X / 44 <= a.Y - tRow && Ok(here, 128) && Ok(here, 16) && Ok(P(a.X, a.Y - 1), 16)
                    && Ok(P(a.X - 1, a.Y), 128) && Ok(here, 4) && Ok(P(a.X, a.Y - 1), 4) && Ok(P(a.X - 1, a.Y), 4)) me.Dir = 7;
                else if (a.Y < tRow && a.X - me.Target.X / 44 <= tRow - a.Y && Ok(here, 32) && Ok(here, 16) && Ok(P(a.X, a.Y + 1), 16)
                    && Ok(P(a.X - 1, a.Y), 32) && Ok(here, 4) && Ok(P(a.X, a.Y + 1), 4) && Ok(P(a.X - 1, a.Y), 4)) me.Dir = 5;
            }
        }

        /// <summary>Blocked: step N when the target is above and near, else move toward the standard lanes
        /// (row 13 eastbound, 11 westbound) or the target row; upper rows 0-2 have their own dodge.</summary>
        void Dodge(Person me, HallPlayer hp, Pt a, int poss)
        {
            if (!hp.WalkActive) { me.Dir = 8; return; }
            int tRow = me.Target.Y / 22 + 5;
            if (a.Y >= 5 && a.Y <= 14)
            {
                int here = P(a.X, a.Y);
                if (me.Dir == 2 && a.Y > tRow && a.Y > 5 && me.Target.X / 44 - a.X <= a.Y - tRow && Ok(here, 128) && Ok(P(a.X, a.Y - 1), 64) && Ok(here, 4) && Ok(P(a.X, a.Y - 1), 4))
                    me.Dir = 0;
                else if (me.Dir == 6 && a.Y > tRow && a.Y > 5 && a.X - me.Target.X / 44 <= a.Y - tRow && Ok(here, 128) && Ok(P(a.X, a.Y - 1), 16) && Ok(here, 4) && Ok(P(a.X, a.Y - 1), 4))
                    me.Dir = 0;
                else
                {
                    int ty = a.Y;
                    if (me.Dir == 2) ty = 13;
                    if (me.Dir == 6) ty = 11;
                    int bit = 128 >> (me.Dir >> 1);
                    if ((P(a.X, ty) & bit) == 0 || (P(a.X, ty) & 4) == 0) ty = tRow;
                    if (ty < a.Y && (poss & 128) != 0) me.Dir = 0;
                    else if (ty > a.Y && (poss & 32) != 0) me.Dir = 4;
                    else me.Dir = 8;
                }
            }
            else if (a.Y >= 0 && a.Y <= 2 && (me.Dir == 2 || me.Dir == 6))
            {
                int py = (me.Position.Y + 2200) / 22 - 100;
                if (me.Dir == 2 && (poss & 32) != 0 && Ok(P(a.X, a.Y + 1), 64)) me.Dir = 4;
                else if (me.Dir == 2 && (poss & 128) != 0 && Ok(P(a.X, a.Y - 1), 64)) me.Dir = 0;
                else if (me.Dir == 6 && (poss & 32) != 0 && Ok(P(a.X, a.Y + 1), 16)) me.Dir = 4;
                else if (me.Dir == 6 && (poss & 128) != 0 && Ok(P(a.X, a.Y - 1), 16)) me.Dir = 0;
                else if (py > hp.TertiaryTarget.Y && (poss & 128) != 0) me.Dir = 0;
                else if (py < hp.TertiaryTarget.Y && (poss & 32) != 0) { me.Dir = 4; if (me.StatePar == -1) me.StatePar = 0; }
                else if ((poss & 32) != 0 && a.Y == 0) me.Dir = 4;
                else if ((poss & 128) != 0 && a.Y == 2) me.Dir = 0;
                else me.Dir = 8;
            }
            else me.Dir = 8;
        }

        /// <summary>Direction while auto-walking to a room or out of it: north first while below the target row,
        /// then E/W, then south (special E/W handling for the workshop and cargo ends).</summary>
        void AutoDirection(Person me, HallPlayer hp, Pt a, Pt np)
        {
            int px = me.Position.X, py = me.Position.Y, tx = me.Target.X, ty = (me.Target.Y + 1000) % 5000;
            bool farRight = me.StatePar == RoomIds.Werkstatt || (me.StatePar == -1 && px > RightEnd - 600);
            bool farLeft = me.StatePar == RoomIds.Fracht || (me.StatePar == -1 && px < LeftEnd + 600);
            if (px > tx && farRight) me.Dir = 6;
            else if (px > tx && farLeft) me.Dir = 6;
            else if (py + 1000 > ty) me.Dir = 0;
            else if (px > tx) me.Dir = 6;
            else if (px < tx) me.Dir = 2;
            else if (py + 1000 < ty) me.Dir = 4;
            EndDiagonals(me, hp, a, np);
        }

        void EndDiagonals(Person me, HallPlayer hp, Pt a, Pt np)
        {
            int here = P(a.X, a.Y);
            if (me.StatePar == RoomIds.Werkstatt || (me.StatePar == -1 && me.Position.X > RightEnd - 600))
            {
                if (me.Dir == 2 && (here & 64) == 0) me.Dir = 3;
                if (me.Dir == 6 && (here & 16) == 0 && ((here & 64) == 0 || (np.X + 4444) / 44 < 33)) me.Dir = 7;
            }
            else if (me.StatePar == RoomIds.Fracht || (me.StatePar == -1 && me.Position.X < LeftEnd + 600 && hp.ExRoom != RoomIds.Museum))
            {
                if (me.Dir == 2 && (here & 64) == 0) me.Dir = 3;
                if (me.Dir == 6 && (here & 16) == 0) me.Dir = 7;
            }
        }

        static bool OnlyWhenTargeted(int c) =>
            c == RoomIds.Kiosk || c == RoomIds.Insel || c == RoomIds.Security || c == RoomIds.Rushmore || c == RoomIds.Weltall
            || c == RoomIds.Ricks || c == RoomIds.RouteBox;

        /// <summary>Stepping onto a room announcement rune (RUNE_2SHOP): menus, stairs, walk-in gatekeeping (§5.3).</summary>
        void ApproachRune(Person me, HallPlayer hp, int pn, Pt np, int upY, bool up, int sx, int sy)
        {
            int c = RuneParNear(new Pt(np.X, np.Y + upY), new Pt(sx, sy * 2), RuneId.ToShop);
            bool human = World.Players[pn].Owner != Owner.Computer;
            if (c == RoomIds.AirportExit)
            {
                if (me.Dir != 0) World.Events.Publish(new HallRequest(pn, HallRequest.CallItADay, c));
                hp.ExRoom = c;
                me.StatePar = -1;
            }
            if (c >= RoomIds.Phone1 && c <= RoomIds.Phone4)
            {
                if (human && hp.DirectToRoom == c && !UiBusy(pn))
                {
                    World.Events.Publish(new HallRequest(pn, HallRequest.Phone, c));
                    me.LookDir = 0; me.Phase = 0;
                    hp.DirectToRoom = 0;
                }
            }
            else if (c >= RoomIds.Monitor7 && c <= RoomIds.Monitor9)
            {
                if (human && hp.DirectToRoom == c && !UiBusy(pn))
                {
                    World.Events.Publish(new HallRequest(pn, HallRequest.FlightMonitor, c));
                    hp.DirectToRoom = 0;
                    me.LookDir = 0; me.Phase = 0;
                }
            }
            else if (c == RoomIds.Electro)
            {
                if (human && hp.DirectToRoom == c && !UiBusy(pn))
                {
                    // With the glove the machine only hands out a drink; without it the boss gets the shock (skeleton
                    // animation, facing the machine). The view runs UseVendingMachineCommand and plays the sound.
                    bool glove = World.Players[pn].HasItem(global::OpenAT.Sim.Items.ItemId.Glove);
                    World.Events.Publish(new HallRequest(pn, HallRequest.ColaMachine, c));
                    hp.DirectToRoom = 0;
                    if (!glove)
                    {
                        StartTriggerNear(me.ScreenPos, new Pt(180, 160), 303);
                        me.LookDir = 2; me.Phase = 0;
                    }
                }
            }
            else if (c != 0 && !RoomIds.IsPassengerMarker(c) && c != RoomIds.AirportExit && (!OnlyWhenTargeted(c) || c == hp.DirectToRoom))
            {
                hp.ExRoom = -1;
                me.StatePar = c;
                if (RoomIds.IsStairs(c)) return;
                if (c != RoomIds.WcF && c != RoomIds.WcM) RoomBusy[c]++;

                if (RoomIds.IsOfficeOrPersonnel(c) && c / 10 != pn + 1)
                {
                    // A competitor's office: turn around, ask whether to go to the own one.
                    me.Target = new Pt(me.Position.X, me.Position.Y + 22 + upY);
                    hp.NewDir = 4;
                    hp.TertiaryTarget = new Pt((me.Target.X - 22) / 44, (me.Target.Y - 5000 - 11) / 22 + 1);
                    hp.WalkActive = false;
                    if (RoomBusy[c] > 0) RoomBusy[c]--;
                    if (!UiBusy(pn))
                    {
                        hp.ThrownOutOfRoom = c % 10 + pn * 10 + 10;
                        bool out_ = World.Players[c / 10 - 1].IsOut;
                        // Carrying the pliers to a rival's intact office (not the personnel office) cuts its power (§5.3 a).
                        if (!out_ && c % 10 == 0 && World.Players[pn].Owner == Owner.Human)
                            global::OpenAT.Sim.Items.ItemEngine.CutOfficePower(World, World.Players[pn], World.Players[c / 10 - 1]);
                        World.Events.Publish(new HallRequest(pn, out_ ? HallRequest.WrongRoomOut : HallRequest.WrongRoom, c));
                    }
                    me.Dir = 4; me.StatePar = 0; hp.ExRoom = 0;
                }
                else if (UiBusy(pn) || hp.IsWalkingToPlayer != -1)
                {
                    if (c != RoomIds.WcF && c != RoomIds.WcM && RoomBusy[c] > 0) RoomBusy[c]--;
                    if (c == RoomIds.Werkstatt) me.Target = new Pt(me.Position.X - 44, me.Position.Y);
                    else if (c == RoomIds.Fracht) me.Target = new Pt(me.Position.X + 44, me.Position.Y);
                    else me.Target = new Pt(me.Position.X, me.Position.Y + 22 + upY);
                    hp.NewDir = 8;
                    hp.TertiaryTarget = new Pt((me.Target.X - 22) / 44, (me.Target.Y - 5000 - 11) / 22 + 1);
                    hp.WalkActive = false;
                }
                else if ((RoomBusy[c] > 1 || hp.LocationForbidden == c) && !Clock.Frozen && !RoomIds.IsOfficeOrPersonnel(c))
                {
                    if (c != RoomIds.WcF && c != RoomIds.WcM && RoomBusy[c] > 0) RoomBusy[c]--;
                    bool board = c == RoomIds.RouteBox || c == RoomIds.Security;
                    if (c == RoomIds.Nasa || c == RoomIds.Fracht) me.Target = new Pt(me.Position.X + 44, me.Position.Y + 22 + upY);
                    else if (c == RoomIds.Werkstatt) me.Target = new Pt(me.Position.X - 44, me.Position.Y);
                    else if (board)
                    {
                        me.LookDir = 2;
                        hp.DirectToRoom = 0;
                        hp.ThrownOutOfRoom = c;
                        World.Events.Publish(new HallRequest(pn, HallRequest.ThrownOutBoard, c));
                        me.StatePar = 0; hp.ExRoom = 0;
                    }
                    else me.Target = new Pt(me.Position.X, me.Position.Y + 22 + upY);
                    hp.NewDir = 8;
                    hp.TertiaryTarget = new Pt((me.Target.X - 22) / 44, (me.Target.Y - 5000 - 11) / 22 + 1);
                    hp.WalkActive = false;
                    if (!board && !UiBusy(pn))
                    {
                        hp.ThrownOutOfRoom = c;
                        World.Events.Publish(new HallRequest(pn, hp.LocationForbidden != c ? HallRequest.ThrownOut : HallRequest.Forbidden, c));
                    }
                }
                else me.Target = RuneOr(RuneId.Shop, c, null, me.Target);
            }
        }

        /// <summary>Stairs pseudo-rooms 50-55: NE up the stairs then W on the gallery, or E then SW down (§6.4).</summary>
        void StairsStep(Person me, HallPlayer hp, Pt a, ref bool up, int sx, int sy)
        {
            bool goingUp = me.StatePar == 50 || me.StatePar == 52 || me.StatePar == 54;
            if (up)
            {
                if (goingUp)
                {
                    me.Dir = 6;
                    if ((P(a.X, a.Y) & 16) == 0 && Math.Abs((me.Position.X + 4422) % 44) <= sx) { me.Dir = 4; me.StatePar = 0; hp.ExRoom = 0; }
                }
                else
                {
                    me.Dir = 2;
                    if ((P(a.X, a.Y) & 64) == 0)
                    {
                        me.Position.Y -= 22;
                        me.Position = new Pt((me.Position.X + 22) / 44 * 44, (me.Position.Y + 11) / 22 * 22);
                        up = false;
                        me.Dir = 5;
                    }
                }
            }
            else
            {
                if (goingUp)
                {
                    me.Dir = 1;
                    if ((P(a.X, a.Y) & 64) == 0)
                    {
                        me.Position = new Pt((me.Position.X + 22) / 44 * 44 + 22, (me.Position.Y + 11) / 22 * 22 + 11);
                        up = true;
                        me.Dir = 6;
                    }
                }
                else
                {
                    me.Dir = 5;
                    if ((P(a.X, a.Y) & 16) == 0 && Math.Abs((me.Position.Y + 2211) % 22) <= sy) { me.Dir = 4; me.StatePar = 0; hp.ExRoom = 0; }
                }
            }
        }

        /// <summary>Automatic walk into a room: snap near the target, steer, and enter on its RUNE_SHOP.</summary>
        void AutoWalkStep(Person me, HallPlayer hp, int pn, Pt a, Pt np, int upY, bool up, int sx, int sy)
        {
            if (Math.Abs(me.Position.X - me.Target.X) <= sx) me.Position.X = me.Target.X;
            if (Math.Abs(me.Position.Y - me.Target.Y) <= sy * 2) me.Position.Y = me.Target.Y;
            me.Dir = 8;
            int px = me.Position.X, py = me.Position.Y, tx = me.Target.X, ty = (me.Target.Y + 1000) % 5000;
            bool farRight = me.StatePar == RoomIds.Werkstatt || (me.StatePar == -1 && px > RightEnd - 600);
            bool farLeft = me.StatePar == RoomIds.Fracht || (me.StatePar == -1 && px < LeftEnd + 600);
            if (px > tx && farRight) me.Dir = 6;
            else if (px > tx && farLeft) me.Dir = 6;
            else if (py + 1000 > ty) me.Dir = 0;
            else if (px > tx) me.Dir = 6;
            else if (px < tx) me.Dir = 2;
            else if (py + 1000 < ty) me.Dir = 4;
            EndDiagonals(me, hp, a, np);

            if (me.StatePar != -1)
            {
                int c = RuneParNear(new Pt(np.X, np.Y + upY), new Pt(sx, sy * 2), RuneId.Shop);
                if (c == RoomIds.Electro) c = 0;
                if (c == RoomIds.WcF || c == RoomIds.WcM)
                {
                    hp.RunningToToilet = false;
                    // A human's figure waits at the door while the toilet pause runs (the view's MENU_WC_F/M).
                    if (World.Players[pn].Owner == Owner.Human) hp.OnToilet = c;
                    World.Events.Publish(new HallRequest(pn, HallRequest.Toilet, c));
                }
                else if (c != 0 && !RoomIds.IsPassengerMarker(c))
                {
                    if (pn != World.LocalPlayer || !World.CallItADay)
                    {
                        if (World.Players[pn].Owner != Owner.RemoteHuman) EnterRoom(pn, c);
                        hp.WalkActive = false;
                    }
                    me.Dir = hp.NewDir = 8;
                    me.StatePar = 0;
                    hp.ExRoom = 0;
                    if (c == RoomIds.LastMinute) me.LookDir = 1;
                    else if (c == RoomIds.Reisebuero) me.LookDir = 3;
                    else if (c == RoomIds.ArabAir || c == RoomIds.Kiosk || c == RoomIds.Ricks || c == RoomIds.RouteBox) me.LookDir = 0;
                }
            }
            if (me.Position.X == me.Target.X && me.Position.Y + upY == me.Target.Y) { me.StatePar = 0; hp.ExRoom = 0; }
        }

        /// <summary>Back in the hall on a RUNE_SHOP: auto-walk out to the approach point (+offset).</summary>
        void LeavingRoomCheck(Person me, HallPlayer hp, Pt np, int upY, int sx, int sy)
        {
            int c = RuneParNear(new Pt(np.X, np.Y + upY), new Pt(sx, sy * 2), RuneId.Shop);
            if (c == RoomIds.Electro || c == RoomIds.RouteBox || c == RoomIds.Weltall) c = 0;
            if (c == 0 || (c >= RoomIds.ReiseX1 && c <= RoomIds.Monitor9) || c == RoomIds.Ricks || RoomIds.IsStairs(c)) return;
            int r = RuneParNear(new Pt(me.Position.X, me.Position.Y + upY), new Pt(sx, sy * 2), RuneId.Shop);
            if (r == 0) return;
            var approach = RuneOr(RuneId.ToShop, r, null, me.Target);
            if (r == RoomIds.Nasa || r == RoomIds.Fracht) me.Target = approach + new Pt(44, 0);
            else if (r == RoomIds.Weltall || r == RoomIds.Rushmore || r == RoomIds.Insel || r == RoomIds.Security) { me.Target = approach; hp.DirectToRoom = 0; }
            else if (r != RoomIds.Werkstatt) me.Target = approach + new Pt(0, 22);
            else me.Target = approach + new Pt(-44, 0);
            hp.ExRoom = c;
            me.StatePar = -1;
        }

        void FaceOtherPlayer(Person me, HallPlayer hp, int upY)
        {
            var other = PlayerPerson(hp.IsWalkingToPlayer);
            if (other != null)
            {
                var p = other.Position;
                if (me.Position.Y + upY > p.Y + 7) me.Phase = 0;
                else if (me.Position.Y + upY < p.Y - 7) me.Phase = 2;
                else if (me.Position.X > p.X + 7) me.Phase = 3;
                else if (me.Position.X < p.X - 7) me.Phase = 1;
                World.Events.Publish(new HallRequest(me.State, HallRequest.TalkToPlayer, hp.IsWalkingToPlayer));
                Players[hp.IsWalkingToPlayer].IsWalkingToPlayer = -1;
            }
            hp.IsWalkingToPlayer = -1;
        }

        /// <summary>Enters a room: remembers it and tells the room framework.</summary>
        void EnterRoom(int player, int room)
        {
            var hp = Players[player];
            if (hp.Room == room) return;
            var me = PlayerPerson(player);
            if (me != null) me.Running = false;
            // A toilet run that ends in another room (the roll fired on that room's door rune) is over: otherwise
            // every later walk would be swallowed by the "running to the toilet" state for the rest of the day.
            hp.RunningToToilet = false;
            hp.Room = room;
            UpdateRoomUsage();
            World.Events.Publish(new RoomRequested(player, room));
        }

        /// <summary>The player is back in the hall (the room framework calls this through <see cref="ReturnToHallCommand"/>).</summary>
        public void ReturnToHall(int player)
        {
            var hp = Players[player];
            int left = hp.Room;
            if (hp.Room != 0 && hp.Room != RoomIds.Airport && RoomBusy[hp.Room] > 0 && hp.Room != RoomIds.WcF && hp.Room != RoomIds.WcM) RoomBusy[hp.Room]--;
            hp.Room = 0;
            if (World.Players[player].Owner != Owner.Computer)
            {
                // Closing a room window ends the walk to it, so the figure standing on the approach rune of a
                // room that is only entered as the walk target (route box, kiosk, ...) does not walk straight
                // back in (rooms-and-ui-9 §5.3 step 7). The route box window also stops the auto-walk and turns
                // the figure to look direction 2.
                if (left != 0 && hp.DirectToRoom == left && OnlyWhenTargeted(left)) hp.DirectToRoom = 0;
                var me = PlayerPerson(player);
                if (left == RoomIds.RouteBox && me != null) { me.LookDir = 2; me.StatePar = 0; }
            }
            IgnoreNextDoor = true;
            UpdateRoomUsage();
        }

        /// <summary>
        /// "UpdateRoomUsage" (rooms-and-ui-9 §5.4): after every enter/leave the occupancy of the public rooms
        /// (ids 42..139) is recounted from where the airlines that are still in the game actually are, so a
        /// tentative count from the walk-in (or one lost to the anti-deadlock reset) cannot drift. The toilets
        /// are not counted (the original names WC_F twice, which only matters for WC_M; the walk never counts toilets).
        /// </summary>
        public void UpdateRoomUsage()
        {
            for (int r = FirstPublicRoom; r <= LastPublicRoom; r++) RoomBusy[r] = 0;
            for (int p = 0; p < Players.Length; p++)
            {
                if (World.Players[p].IsOut) continue;
                int r = Players[p].Room;
                if (r >= FirstPublicRoom && r <= LastPublicRoom && r != RoomIds.WcF && r != RoomIds.WcM) RoomBusy[r]++;
            }
        }

        const int FirstPublicRoom = 42, LastPublicRoom = 139;

        /// <summary>The shared rooms with a maximum stay (rooms-and-ui-9 §5.5).</summary>
        public static bool HasStayLimit(int room) =>
            room == RoomIds.Reisebuero || room == RoomIds.LastMinute || room == RoomIds.Shop1 || room == RoomIds.Bank
            || room == RoomIds.Museum || room == RoomIds.ArabAir || room == RoomIds.Makler || room == RoomIds.Aufsicht
            || room == RoomIds.RouteBox || room == RoomIds.Ricks || room == RoomIds.Sabotage || room == RoomIds.Nasa
            || room == RoomIds.Rushmore || room == RoomIds.Insel || room == RoomIds.Werkstatt || room == RoomIds.Kiosk
            || room == RoomIds.Fracht;

        /// <summary>Game time a player may stay in such a room, and how long it stays barred afterwards (2 hours each).</summary>
        public const int StayLimit = 2 * GameClock.UnitsPerHour;

        /// <summary>
        /// Maximum stay rule (rooms-and-ui-9 §5.5): a human airline that stays two game hours in one of the shared rooms
        /// is pushed out (<see cref="HallRequest.StayedTooLong"/>; the room framework closes that room and everything
        /// above it, e.g. the laptop) and barred from it for two game hours (the "forbidden" note at the door). The
        /// timer runs while such a room is open and restarts when none is. Computer airlines keep their own room
        /// timing (they never stay that long).
        /// </summary>
        public void UpdateStayLimit()
        {
            int now = Clock.Time;
            for (int p = 0; p < Players.Length; p++)
            {
                var hp = Players[p];
                var pl = World.Players[p];
                if (pl.IsOut || pl.Owner == Owner.Computer) { hp.LocationTime = -1; continue; }
                // A new day restarts the clock below the stored times.
                if (hp.LocationTime > now) hp.LocationTime = -1;
                if (hp.LocationForbidden != 0 && (hp.LocationForbiddenTime > now || now - hp.LocationForbiddenTime >= StayLimit)) hp.LocationForbidden = 0;
                if (!HasStayLimit(hp.Room)) { hp.LocationTime = -1; continue; }
                if (hp.LocationTime == -1) hp.LocationTime = now;
                if (now - hp.LocationTime < StayLimit) continue;
                hp.LocationForbidden = hp.Room;
                hp.LocationForbiddenTime = now;
                hp.LocationTime = -1;
                World.Events.Publish(new HallRequest(p, HallRequest.StayedTooLong, hp.Room));
            }
        }

        void RemoveAnimationNear(Pt pos)
        {
            foreach (var q in Persons)
                if (q.Alive && Math.Abs(q.Position.X - pos.X) < 44 && Math.Abs(q.Position.Y - pos.Y) < 22 && Clan(q.ClanId)?.Type == ClanType.Animation)
                    q.State = PState.Leaving;
        }

        /// <summary>Idle gimmick after 400 ticks of standing in the hall (§6.4 "Idle").</summary>
        void UpdateIdle(int player)
        {
            var hp = Players[player];
            var me = PlayerPerson(player);
            if (me == null || !hp.InHall || UiBusy(player) || me.LookDir != 8 || World.Players[player].Owner != Owner.Human) { hp.StandCount = 0; return; }
            hp.StandCount++;
            bool walkway = me.Position.Y / 22 + 5 == 13 && (P(me.Position.X / 44, me.Position.Y / 22 + 5) & 240) == 80;
            if (hp.IsStuck == 0 && hp.StandCount >= 20 * 20 && !walkway)
            {
                me.State &= ~PState.WaitFlag;
                me.LookDir = 9;
                me.Phase = 0;
                hp.NewDir = 8;
                me.Target = me.Position;
                hp.StandCount = 0;
            }
        }
    }

    /// <summary>A player figure reached a room's threshold: the room framework should open the room.</summary>
    public sealed class RoomRequested : ISimEvent
    {
        public readonly int Player, Room;
        public RoomRequested(int player, int room) { Player = player; Room = room; }
        public override string ToString() => $"RoomRequested(p{Player}, {Room})";
    }

    /// <summary>A hall situation that needs a menu or message box (rooms-and-ui-9 §5.3, §10).</summary>
    public sealed class HallRequest : ISimEvent
    {
        public const int CallItADay = 1, Phone = 2, FlightMonitor = 3, ColaMachine = 4, WrongRoom = 5, WrongRoomOut = 6,
            ThrownOut = 7, Forbidden = 8, ThrownOutBoard = 9, Closed = 10, Toilet = 11, TalkToPlayer = 12,
            /// <summary>Maximum stay reached (<see cref="HallState.UpdateStayLimit"/>): leave <see cref="Room"/> and everything above it.</summary>
            StayedTooLong = 13,
            /// <summary>The walk target is a bombed office (MENU_REQUEST_DESTROYED; the walk goes on).</summary>
            Destroyed = 14;
        public readonly int Player, Kind, Room, Arg;
        public HallRequest(int player, int kind, int room, int arg = 0) { Player = player; Kind = kind; Room = room; Arg = arg; }
        public override string ToString() => $"HallRequest(p{Player}, kind {Kind}, room {Room}, {Arg})";
    }

    public sealed class HallPlayerStuck : ISimEvent { public readonly int Player; public HallPlayerStuck(int p) => Player = p; }
}

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// The toilet pause of a human figure: <see cref="Done"/> false = the flush after 20 ticks (sound; the ladies'
    /// perfume may be picked up), true = the pause is over and the figure walks out (rooms-and-ui-2 §4.19).
    /// </summary>
    public sealed class HallToiletFlush : ISimEvent
    {
        public readonly int Player, Room;
        public readonly bool Done;
        public HallToiletFlush(int player, int room, bool done = false) { Player = player; Room = room; Done = done; }
    }
}
