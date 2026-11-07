using System;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Customer, luggage and waypoint-walker movement (airport-and-people.md §6.5, §7). Customers move
    /// on the lower floor only, in 4 directions (0 N, 1 E, 2 S, 3 W; 4 = stop), WalkSpeed px per step,
    /// and decide at tile centres. Tile = ((x+4400)/44−100, (y+2200)/22−100), the grid row is +5.
    /// </summary>
    public sealed partial class HallState
    {
        static int CustCol(int x) => (x + 4400) / 44 - 100;
        static int CustRow(int y) => (y + 2200) / 22 - 100;

        int FrameCount(ClanDef k, int slot) => Math.Max(1, k.FrameCount(slot));

        void AdvancePhase(Person p, ClanDef k) => p.Phase = (p.Phase + 1) % (FrameCount(k, Math.Min(p.LookDir, 13)) * k.Faktor);

        internal void CustomerStep(Person p)
        {
            var k = Clan(p.ClanId);
            if (k == null) { p.State = PState.Leaving; return; }
            int step = k.WalkSpeed;
            int stepX = step, stepY = step;

            // The old lady starts at 10:00; the postman walks from 13:00 on weekdays only (§6.6).
            if (k.Group == 70 && Clock.Hour < 10) return;
            if (k.Group == 90 && (Clock.Hour < 13 || Clock.Weekday == 5 || Clock.Weekday == 6)) return;
            if ((p.State & PState.WaitFlag) != 0 && (p.State & ~PState.WaitFlag) == PState.Boarding && p.WaitCount <= 250) return;

            if (p.LookDir == 8) { p.Dir = p.Phase; p.LookDir = p.Phase; p.Phase = 0; }
            else if (p.LookDir == 4)
            {
                p.Phase++;
                if (p.Phase < FrameCount(k, ClanDef.Gimmick) * k.Faktor) return;
                if (k.Group == 70) { p.Phase = 18 * k.Faktor; return; } // the old lady freezes on frame 18
                p.Dir = p.LookDir = k.GimmickArt1;
                p.Phase = 0;
            }
            else if (p.LookDir == 9)
            {
                p.Phase++;
                if (p.Phase < FrameCount(k, ClanDef.WaitGimmick) * k.Faktor) return;
                p.Dir = p.LookDir = k.GimmickArt2;
                p.Phase = 0;
            }

            p.State &= ~PState.WaitFlag;

            // Arrived at the current target: next checkpoint.
            if (Math.Abs(p.Position.X - p.Target.X) <= stepX && Math.Abs(p.Position.Y - p.Target.Y) <= stepY && p.BaseState != PState.Waiting)
            {
                int bs = p.BaseState;
                if (bs == PState.ExShop || bs == PState.ToExit) ReachedTarget(p);
                else if (bs != PState.CheckingIn)
                {
                    ReachedTarget(p);
                    if (p.LookDir == 4)
                    {
                        int ax = CustCol(p.Position.X), ay = CustRow(p.Position.Y);
                        POr(ax, ay + 5, Plate.Occupied);
                        SetPD(ax, ay + 5, p.LookDir);
                        return;
                    }
                    if (p.WaitCount != 0)
                    {
                        if (p.Dir != 8) AdvancePhase(p, k);
                        return;
                    }
                }
            }

            int col = CustCol(p.Position.X), row = CustRow(p.Position.Y);

            // Stench (bit 1 on the lower floor).
            if (row >= 0 && row <= 9 && p.Mood != Mood.Sick && (P(col, row + 5) & Plate.Special) != 0)
            {
                if (p.Mood == Mood.Bone || p.Mood == Mood.StenchDog) SetMood(p, Mood.StenchDog, 7);
                else if (p.Reason == Reason.Shopping || p.Reason == Reason.Waypoint) SetMood(p, Mood.Stench, 7);
                else if (p.Reason == Reason.Flying || p.Reason == Reason.Leaving)
                {
                    p.Mood = Mood.Sick;
                    p.MoodCountdown = Mood.CountStart + CosmeticRand().Rand(15);
                    if (p.FlightAirline >= 0 && p.FlightAirline <= 3 && (p.ClanId & 3) == 0)
                    {
                        World.Players[p.FlightAirline].ChangeImage(-1);
                        World.Events.Publish(new HallImagePenalty(p.FlightAirline, 1));
                    }
                }
            }

            if (p.BaseState != PState.ToCheckInExit && row >= 0 && row <= 9)
            {
                PAnd(col, row + 5, ~Plate.Occupied);
                SetPD(col, row + 5, 255);
            }

            Pt np = Step4(p.Position, p.Dir, stepX, stepY);

            // Crossing a tile centre line: time to decide.
            if ((np.X + 4422) / 44 != (p.Position.X + 4422) / 44 || (np.Y + 2211) / 22 != (p.Position.Y + 2211) / 22 || p.Dir == 4)
            {
                if (row < 2) TryDoor(new Pt(col, row + 5), false, 0);

                int poss = row >= 0 && row <= 9 ? (P(col, row + 5) & 240) & ~(128 >> ((p.Dir + 2) & 3)) : 128 + 32;
                int count = Bits(poss);

                // In a waiting room, look for a free seat.
                if (p.BaseState == PState.Waiting)
                {
                    if (row == 0) { poss &= ~128; count = Bits(poss); }
                    if (row > 0 && row < 7)
                    {
                        int south = 1 << ((row - 1) * 2 + 1), north = 1 << ((row + 1) * 2);
                        if ((Seat(col) & south) == 0)
                        {
                            SeatOr(col, south);
                            SitDown(p, -3, 7);
                            return;
                        }
                        if ((Seat(col) & north) == 0)
                        {
                            SeatOr(col, north);
                            SitDown(p, +3, 6);
                            return;
                        }
                    }
                }

                if (p.BaseState == PState.Waypointing)
                    ChooseByState(p, k, ref stepX, stepY, col, row, ref poss);
                else if (p.BaseState == PState.CheckingIn && Math.Abs(p.Position.Y - p.Target.Y) <= stepY && Math.Abs(p.Position.X - p.Target.X) <= stepX)
                {
                    p.Dir = 4;
                    if (p.PersonalRandWalk.Rand(100) == 0) ReachedTarget(p);
                }
                else if (count == 1)
                {
                    p.Dir = poss == 16 ? 3 : poss == 32 ? 2 : poss == 64 ? 1 : 0;
                }
                else if (count == 0)
                {
                    // Dead end: turn until an exit exists; give up after 10 tries.
                    int death = 0;
                    do { p.Dir = (p.Dir + 1) & 3; death++; }
                    while (death < 10 && (P(col, row + 5) & (128 >> p.Dir)) == 0);
                    if (death >= 10) p.State = PState.Leaving;
                }
                else ChooseByState(p, k, ref stepX, stepY, col, row, ref poss);
            }

            MoveOrWait(p, k, stepX, stepY, col, row);

            p.ScreenPos = new Pt(p.Position.X - p.Position.Y / 2 + 90, p.Position.Y + 220);
        }

        static int Bits(int poss) => ((poss & 16) != 0 ? 1 : 0) + ((poss & 32) != 0 ? 1 : 0) + ((poss & 64) != 0 ? 1 : 0) + ((poss & 128) != 0 ? 1 : 0);

        static Pt Step4(Pt pos, int dir, int sx, int sy)
        {
            switch (dir)
            {
                case 0: return new Pt(pos.X, pos.Y - sy);
                case 1: return new Pt(pos.X + sx, pos.Y);
                case 2: return new Pt(pos.X, pos.Y + sy);
                case 3: return new Pt(pos.X - sx, pos.Y);
            }
            return pos;
        }

        void SitDown(Person p, int dy, int look)
        {
            p.State = PState.SitWaiting | PState.WaitFlag;
            p.Position = new Pt((p.Position.X + 4444) / 44 * 44 - 4444 + 22, (p.Position.Y + 2222) / 22 * 22 - 2222 + 11 + dy);
            p.Dir = p.LookDir = look;
            p.Phase = 0;
        }

        void SetMood(Person p, int mood, int randRange)
        {
            p.Mood = mood;
            if (p.MoodCountdown != 0) p.MoodCountdown = Math.Max(p.MoodCountdown, Mood.CountStart - 16);
            else p.MoodCountdown = Mood.CountStart + CosmeticRand().Rand(randRange);
        }

        /// <summary>Walks one axis toward the target; snaps when within a step and continues on the other axis.</summary>
        static void ApproachY(Person p, int ty, int sy)
        {
            if (p.Position.Y < ty) p.Dir = 2; else if (p.Position.Y > ty) p.Dir = 0;
        }

        void ChooseByState(Person p, ClanDef k, ref int stepX, int stepY, int col, int row, ref int poss)
        {
            switch (p.BaseState)
            {
                case PState.Browsing:
                    if (poss == 0) break;
                    do p.Dir = p.PersonalRandWalk.Rand(4); while ((poss & (128 >> p.Dir)) == 0);
                    break;

                case PState.Waiting:
                    if (row == 4 && (poss & 64) != 0 && (Seat(col + 3) & (1 << ((row - 1) * 2 + 1))) == 0) p.Dir = 1;
                    else if (row == 2 && (poss & 64) != 0 && ((Seat(col + 3) & (1 << ((row + 1) * 2))) == 0 || (Seat(col + 2) & (1 << ((row + 1) * 2))) == 0)) p.Dir = 1;
                    else
                    {
                        if (poss == 0) break;
                        for (int guard = 0; guard < 1000; guard++)
                        {
                            do p.Dir = p.PersonalRandWalk.Rand(4); while ((poss & (128 >> p.Dir)) == 0);
                            if (row == 4 && p.Dir == 2) continue;
                            if (row == 0 && p.Dir == 0) continue;
                            break;
                        }
                    }
                    break;

                case PState.Shopping:
                    // Inside a shop: x first when on the target row and near, else y.
                    if (p.Position.Y == p.Target.Y && Math.Abs(p.Position.X - p.Target.X) <= 44 * 3)
                    {
                        if (p.Position.X < p.Target.X) p.Dir = 1; else if (p.Position.X > p.Target.X) p.Dir = 3;
                        if (Math.Abs(p.Position.X - p.Target.X) <= stepX) { p.Position.X = p.Target.X; p.Dir = 4; }
                    }
                    else
                    {
                        ApproachY(p, p.Target.Y, stepY);
                        if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
                    }
                    break;

                case PState.ExShop:
                    if (p.Position.X == p.Target.X)
                    {
                        ApproachY(p, p.Target.Y, stepY);
                        if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
                    }
                    else
                    {
                        if (p.Position.X < p.Target.X) p.Dir = 1; else if (p.Position.X > p.Target.X) p.Dir = 3;
                        if (Math.Abs(p.Position.X - p.Target.X) <= stepX) { p.Position.X = p.Target.X; p.Dir = 4; }
                    }
                    break;

                case PState.Waypointing:
                    if (p.FlightPlaneId > 1)
                    {
                        // Pause countdown at a WAYPOINT_WAIT rune.
                        p.Phase = 0; p.Dir = 4;
                        p.FlightPlaneId--;
                        if (p.FlightPlaneId == 1) p.FlightPlaneId = 0;
                    }
                    else ManhattanXFirst(p, stepX, stepY);
                    break;

                case PState.ToExit:
                case PState.ToCheckIn:
                case PState.ToWaitRoom:
                case PState.ToShop:
                    GoalSeek(p, stepX, stepY, col, row);
                    poss = row >= 0 && row <= 9 ? (P(col, row + 5) & 240) & ~(128 >> ((p.Dir + 2) & 3)) : 128 + 32;
                    if (p.Dir == 2 && (poss & 48) == 16) p.Dir = 3;
                    if (p.Dir == 3 && (poss & 48) == 32) p.Dir = 2;
                    break;

                case PState.ToCheckInExit:
                    p.Dir = 1;
                    break;

                case PState.EnteringPlane:
                    if (p.Position.X == p.Target.X) p.Dir = 0;
                    else if (p.Position.X > p.Target.X)
                    {
                        p.Dir = 3;
                        if (Math.Abs(p.Position.X - p.Target.X) <= stepX) { p.Position.X = p.Target.X; p.Dir = 4; }
                    }
                    break;

                case PState.LeavingPlane:
                    LeavingPlane(p, k, stepX);
                    break;

                case PState.ToScanner:
                case PState.Boarding:
                    if (p.Position.X == p.Target.X || (poss & 128) != 0) p.Dir = (poss & 128) != 0 ? 0 : 1;
                    else if (p.Position.X < p.Target.X)
                    {
                        p.Dir = (poss & 64) != 0 ? 1 : 0;
                        if (Math.Abs(p.Position.X - p.Target.X) <= stepX) { p.Position.X = p.Target.X; p.Dir = 4; }
                    }
                    else
                    {
                        p.Dir = (poss & 16) != 0 ? 3 : 0;
                        if (Math.Abs(p.Position.X - p.Target.X) <= stepX && (poss & 128) != 0) { p.Position.X = p.Target.X; p.Dir = 4; }
                    }
                    break;
            }
        }

        /// <summary>x first, then y; snaps at the target (waypoint walkers).</summary>
        static void ManhattanXFirst(Person p, int stepX, int stepY)
        {
            if (p.Position.X == p.Target.X)
            {
                ApproachY(p, p.Target.Y, stepY);
                if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
            }
            else
            {
                if (p.Position.X < p.Target.X) p.Dir = 1; else if (p.Position.X > p.Target.X) p.Dir = 3;
                if (Math.Abs(p.Position.X - p.Target.X) <= stepX)
                {
                    p.Position.X = p.Target.X;
                    ApproachY(p, p.Target.Y, stepY);
                    if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
                }
            }
        }

        /// <summary>Goal seeking with lanes (§6.5 "Lanes"): pick a lane row y, walk to it, then along x, then to the target.</summary>
        void GoalSeek(Person p, int stepX, int stepY, int col, int row)
        {
            if (p.Position.X == p.Target.X)
            {
                ApproachY(p, p.Target.Y, stepY);
                if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY)
                {
                    p.Position.Y = p.Target.Y;
                    ApproachY(p, p.Target.Y, stepY);
                    if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
                }
                return;
            }

            int ty = p.Position.Y;
            bool fast = stepY > 4;
            if (p.Position.X < p.Target.X)
            {
                // Eastbound: normal lane row 8 (y 187), outer row 9 (y 209), shared middle row 7 (y 165).
                bool outer = (P(col, 14) & 64) != 0 && (P(col + 1, 14) & 64) != 0 && (P(col - 1, 14) & 64) != 0;
                if (fast && outer && (row + 5 == 14 || ((P(col + 1, 13) & 2) != 0 && p.Target.X - p.Position.X > 88))) ty = 209;
                else if (PD(col, 13) == 8 || PD(col + 1, 13) == 8 || (fast && (P(col + 1, 13) & 2) != 0 && p.Target.X - p.Position.X > 88)
                         || (stepX > 1 && (PD(col, 13) == 254 || PD(col + 1, 13) == 254)))
                {
                    if (outer) ty = 209;
                    else ty = MiddleLaneFree(col, +1, true) ? 165 : 187;
                }
                else if (row + 5 == 14 && p.Dir == 1 && outer && (P(col + 1, 14) & 2) == 0) ty = 209;
                else if (row + 5 == 12 && (p.Dir == 1 || p.Dir == 0)) ty = MiddleLaneFree(col, +1, false) && (fast || p.Dir == 0) ? 165 : 187;
                else ty = 187;
            }
            else if (p.Position.X > p.Target.X)
            {
                // Westbound: normal lane row 6 (y 143), outer row 5 (y 121).
                bool outer = (P(col, 10) & 64) != 0 && (P(col - 1, 10) & 64) != 0 && (P(col + 1, 10) & 64) != 0;
                if (fast && outer && (row + 5 == 10 || ((P(col - 1, 11) & 2) != 0 && p.Position.X - p.Target.X > 88))) ty = 121;
                else if (PD(col, 11) == 8 || PD(col - 1, 11) == 8 || (fast && (P(col - 1, 11) & 2) != 0 && p.Position.X - p.Target.X > 88)
                         || (stepX > 1 && (PD(col, 11) == 254 || PD(col - 1, 11) == 254)))
                {
                    if (outer) ty = 121;
                    else ty = MiddleLaneFree(col, -1, true) ? 165 : 143;
                }
                // Original bug: the westbound "keep the outer lane" branch returns the eastbound outer y (209);
                // FixHallWestboundLane keeps the westbound outer lane (121).
                else if (row + 5 == 10 && p.Dir == 3 && outer && (P(col - 1, 10) & 2) == 0) ty = World.Options.FixHallWestboundLane ? 121 : 209;
                else if (row + 5 == 12 && (p.Dir == 3 || p.Dir == 2)) ty = MiddleLaneFree(col, -1, false) && (fast || p.Dir == 2) ? 165 : 143;
                else ty = 143;
            }

            if (Math.Abs(p.Position.X - p.Target.X) <= stepX)
            {
                p.Position.X = p.Target.X;
                ApproachY(p, p.Target.Y, stepY);
                if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
            }
            if (p.Dir != 4)
            {
                ApproachY(p, ty, stepY);
                if (Math.Abs(p.Position.Y - ty) <= stepY)
                {
                    p.Position.Y = ty;
                    if (p.Position.X < p.Target.X) p.Dir = 1; else if (p.Position.X > p.Target.X) p.Dir = 3;
                    if (Math.Abs(p.Position.X - p.Target.X) <= stepX)
                    {
                        p.Position.X = p.Target.X;
                        ApproachY(p, p.Target.Y, stepY);
                        if (Math.Abs(p.Position.Y - p.Target.Y) <= stepY) { p.Position.Y = p.Target.Y; p.Dir = 4; }
                    }
                }
            }
        }

        /// <summary>The shared middle lane (grid row 12) is usable when the next 5 tiles hold no oncoming traffic.</summary>
        bool MiddleLaneFree(int col, int sign, bool countSlow)
        {
            int oncoming = sign > 0 ? 3 : 1, exitBit = sign > 0 ? 64 : 16;
            for (int c = 0; c < 5; c++)
            {
                int x = col + sign * c;
                int d = PD(x, 12);
                bool occupied = (P(x, 12) & 2) != 0;
                if (((d == oncoming || (countSlow && d == 254)) && occupied) || (P(x, 12) & exitBit) == 0) return false;
            }
            return true;
        }

        void LeavingPlane(Person p, ClanDef k, int stepX)
        {
            if (p.Position.X < p.Target.X)
            {
                p.Dir = 1;
                if (Math.Abs(p.Position.X - p.Target.X) <= stepX) { p.Position.X = p.Target.X; p.Dir = 4; }
            }
            if (p.Position.X < p.Target.X) return;
            p.Dir = 2;
            if (p.Mood == Mood.Sick && (k.Type == ClanType.Male || k.Type == ClanType.Female))
            {
                int wc = k.Type == ClanType.Male ? RoomIds.WcM : RoomIds.WcF;
                p.Target = RuneOr(RuneId.ToShop, wc, p.PersonalRandWalk, p.Position);
                p.StatePar = wc;
                p.State = PState.ToShop;
                return;
            }
            p.State = PState.ToExit;
            p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Position);
            // A "-n" twin whose "+n" partner is the previous slot fetches its luggage at a belt spot.
            var prev = Clan(p.ClanId - 1);
            if (k.HasSuitcase < 0 && prev != null && prev.HasSuitcase == -k.HasSuitcase && Layout.NumBeltSpots > 0)
            {
                int n = p.PersonalRand.Rand(Layout.NumBeltSpots);
                if (RoomBusy[RoomIds.BeltX1 + n] <= 1)
                {
                    foreach (var r in Layout.Runes)
                    {
                        if (r.Id != RuneId.ToShop || r.Par != RoomIds.BeltX1 + n) continue;
                        p.State = PState.ToShop;
                        p.Target = r.Pos;
                        p.StatePar = r.Par;
                        RoomBusy[RoomIds.BeltX1 + n]++;
                        int bag = SuitcaseId(-k.HasSuitcase);
                        var at = RandomRune(RuneId.CreateSuitcase, 0, p.PersonalRandWalk);
                        if (bag >= 0 && at != null) AddPersonToQueue(bag, at.Value, Reason.SuitcaseExit, 0, 0, 0);
                        break;
                    }
                }
            }
        }

        /// <summary>Applies the chosen direction: occupancy, sidestepping, waiting (§6.5 "Collision").</summary>
        void MoveOrWait(Person p, ClanDef k, int stepX, int stepY, int col, int row)
        {
            if (p.Dir != 4)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var np = Step4(p.Position, p.Dir, stepX, stepY);
                    int ncol = CustCol(np.X), nrow = CustRow(np.Y);
                    col = CustCol(p.Position.X); row = CustRow(p.Position.Y);
                    if (nrow < 0 || nrow > 9 || (p.State & PState.BrowseFlag) != 0)
                    {
                        p.Position = np;
                        AdvancePhase(p, k);
                        p.LookDir = p.Dir;
                        break;
                    }
                    int occ = P(ncol, nrow + 5), od = PD(ncol, nrow + 5);
                    int bs = p.BaseState;
                    if ((occ & 2) == 0 || p.WaitCount >= 200 || bs == PState.ToCheckInExit || (p.Dir == 1 && od == 3) || (p.Dir == 3 && od == 1))
                    {
                        p.Position = np;
                        if (bs != PState.Boarding && bs != PState.ToCheckInExit && bs != PState.Waiting && (occ & 8) == 8)
                            POr(ncol, nrow + 5, Plate.Occupied);
                        p.LookDir = p.Dir;
                        AdvancePhase(p, k);
                        break;
                    }
                    // Somebody is there: sidestep W or E when walking S and far from the target (+4 px boost).
                    if (row >= 3 && row < 9 && p.Dir == 2 && p.Position.X > p.Target.X && (P(col - 1, row + 5) & 2) == 0 && (P(col, row + 5) & 16) != 0 && Math.Abs(p.Position.X - p.Target.X) > stepX * 4)
                    {
                        p.Dir = 3; stepX += 4; continue;
                    }
                    if (row > 2 && row < 9 && p.Dir == 2 && p.Position.X < p.Target.X && (P(col + 1, row + 5) & 2) == 0 && (P(col, row + 5) & 64) != 0 && Math.Abs(p.Position.X - p.Target.X) > stepX * 4)
                    {
                        p.Dir = 1; stepX += 4; continue;
                    }
                    // Head-on meeting: walk through. Otherwise wait.
                    if (p.Dir < 0 || p.Dir > 3 || od != (p.Dir + 2) % 4)
                    {
                        p.State |= PState.WaitFlag;
                        if ((P(col, row + 5) & 8) == 8 && bs != PState.ToCheckInExit) POr(col, row + 5, Plate.Occupied);
                        p.LookDir = p.Dir;
                        p.Phase = p.LookDir;
                        p.Dir = p.LookDir = 8;
                        // The original's patience boost tests Dir after it was set to 8, so it never applies (bug);
                        // FixHallWaitPatience tests the direction the customer was walking (kept in Phase).
                        int was = World.Options.FixHallWaitPatience ? p.Phase : p.Dir;
                        if (((row >= 6 && row <= 8 && (was == 0 || was == 2)) || (row == 5 && was == 2))) p.WaitCount = Math.Max(180, p.WaitCount);
                        if (bs == PState.ToCheckInExit && row == 3 && was == 1) p.WaitCount = Math.Max(198, p.WaitCount);
                    }
                    break;
                }
            }
            else
            {
                int bs = p.BaseState;
                if (row >= 0 && row <= 9 && (p.State & PState.BrowseFlag) == 0 && bs != PState.Boarding && bs != PState.ToCheckInExit && bs != PState.Waiting)
                    if ((P(col, row + 5) & 8) == 8) POr(col, row + 5, Plate.Occupied);
                if (k.Type != ClanType.Suitcase) { p.Phase = p.LookDir; p.Dir = p.LookDir = 8; }
            }

            // The direction marker goes on the tile the step started from (the original's ArrayPos).
            if (row >= 0 && row <= 9 && (P(col, row + 5) & 2) != 0)
                SetPD(col, row + 5, stepX == 1 ? 254 : p.Dir);
        }

        // ------------------------------------------------------------------ reached target (§7)

        void ReachedTarget(Person p)
        {
            var k = Clan(p.ClanId);
            switch (p.Reason)
            {
                case Reason.Shopping:
                    switch (p.BaseState)
                    {
                        case PState.ToShop:
                            p.State = PState.Shopping | PState.BrowseFlag;
                            p.Target = RuneOr(RuneId.Shop, p.StatePar, p.PersonalRand, p.Target);
                            break;
                        case PState.Shopping:
                            p.Dir = p.LookDir = 0;
                            if (k.Type != ClanType.Suitcase) { p.Phase = 0; p.Dir = p.LookDir = 8; }
                            p.WaitCount = 150 + (p.Target.Y + p.Target.X + p.ClanId * 33) % 100;
                            p.State = PState.ExShop | PState.BrowseFlag | PState.WaitFlag;
                            p.Target = RuneOr(RuneId.ToShop, p.StatePar, p.PersonalRand, p.Target);
                            break;
                        case PState.ExShop:
                            if (RoomIds.IsPassengerMarker(p.StatePar) && RoomBusy[p.StatePar] > 0) RoomBusy[p.StatePar]--;
                            if (p.PersonalRand.Rand(4) == 0)
                            {
                                p.MoodCountdown = 0;
                                p.State = PState.ToExit;
                                p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                                p.Mood = Mood.Home;
                            }
                            else
                            {
                                p.State = PState.ToShop;
                                p.MoodCountdown = 0;
                                int sp = p.StatePar, m = p.Mood;
                                p.Target = RandomShop(ref sp, k.Type, ref m, p.PersonalRand);
                                p.StatePar = sp; p.Mood = m;
                            }
                            break;
                        case PState.ToExit:
                            p.State = PState.Leaving;
                            break;
                    }
                    break;

                case Reason.SuitcaseExit:
                    StartTriggerNear(p.ScreenPos, new Pt(180, 80), 471);
                    p.State = PState.Leaving;
                    break;

                case Reason.Leaving:
                    switch (p.BaseState)
                    {
                        case PState.ToShop:
                            p.State = PState.Shopping | PState.BrowseFlag;
                            p.Target = RuneOr(RuneId.Shop, p.StatePar, p.PersonalRand, p.Target);
                            break;
                        case PState.Shopping:
                            p.Phase = 0;
                            p.Dir = p.LookDir = 0;
                            p.WaitCount = 150 + (p.Target.Y + p.Target.X + p.ClanId * 33) % 100;
                            p.State |= PState.WaitFlag;
                            if (k.Type != ClanType.Suitcase) { p.Phase = p.LookDir; p.Dir = p.LookDir = 8; }
                            TryPickUpLuggage(p, k);
                            break;
                        case PState.ExShop:
                            if (RoomIds.IsPassengerMarker(p.StatePar) && RoomBusy[p.StatePar] > 0) RoomBusy[p.StatePar]--;
                            p.MoodCountdown = 0;
                            p.State = PState.ToExit;
                            p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                            p.Mood = Mood.Home;
                            break;
                        case PState.ToExit:
                            p.State = PState.Leaving;
                            break;
                    }
                    break;

                case Reason.Flying:
                    FlyingReached(p, k);
                    break;

                case Reason.Waypoint:
                    if (p.FlightPlaneId == 1)
                    {
                        p.State &= ~PState.WaitFlag;
                        p.LookDir = 4;
                        p.Phase = 0;
                    }
                    {
                        int old = p.FlightPlaneIndex, cur = p.FlightPlaneIndex, gim = p.FlightPlaneId;
                        p.Target = NextWaypoint(p.FlightAirline, ref cur, ref gim);
                        p.FlightPlaneIndex = cur; p.FlightPlaneId = gim;
                        if (old > cur && k.Group == 90) p.State = PState.Leaving;
                    }
                    break;
            }
        }

        void TryPickUpLuggage(Person p, ClanDef k)
        {
            int col = CustCol(p.Position.X), row = CustRow(p.Position.Y);
            if ((P(col, row + 5 - 1) & 2) == 0) return;
            for (int c = Persons.Count - 1; c >= 0; c--)
            {
                var s = Persons[c];
                var sk = Clan(s.ClanId);
                if (!s.Alive || sk == null || sk.Type != ClanType.Suitcase) continue;
                if (Math.Abs(s.Position.X - p.Position.X) >= 16 || Math.Abs(s.Position.Y - (p.Position.Y - 22)) >= 8) continue;
                if (sk.HasSuitcase != -k.HasSuitcase) continue;
                s.State = PState.Leaving;
                p.WaitCount = 110;
                p.State = PState.ToExit | PState.BrowseFlag | PState.WaitFlag;
                p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                p.Phase = 0; p.Dir = 2; p.LookDir = 2;
                if (p.StatePar >= RoomIds.BeltX1 && p.StatePar <= RoomIds.BeltX10 && RoomBusy[p.StatePar] > 0) RoomBusy[p.StatePar]--;
                for (int d = 0; d < HallData.MaxClans; d++)
                    if (Data.Clans[d] is ClanDef t && t.HasSuitcase == -k.HasSuitcase && t.Type != ClanType.Suitcase) { p.ClanId = d; break; }
                break;
            }
        }

        void FlyingReached(Person p, ClanDef k)
        {
            int gate = -1, startHour = -1;
            bool hasFlight = Flights != null && Flights.TryGetFlight(p.FlightAirline, p.FlightPlaneId, p.FlightPlaneIndex, out gate, out startHour);
            if (!hasFlight) gate = -1;
            switch (p.BaseState)
            {
                case PState.ToCheckIn:
                    p.State = PState.CheckingIn;
                    if (gate == -1) p.State = PState.Leaving; else p.Target = RuneOr(RuneId.CheckIn, gate, p.PersonalRand, p.Target);
                    OpenAT.Sim.Tutorial.TutorialEngine.PassengerAtCheckIn(World, p.FlightAirline);
                    break;
                case PState.CheckingIn:
                    p.Mood = p.FirstClass ? Mood.FlyingFC : Mood.Flying;
                    p.Dir = 1;
                    if (k.HasSuitcase > 0)
                    {
                        p.State = PState.DroppingSuitcase;
                        if (gate == -1) p.State = PState.Leaving; else p.Target = RuneOr(RuneId.DropSuitcase, gate, p.PersonalRand, p.Target);
                    }
                    else
                    {
                        PAnd(CustCol(p.Position.X), CustRow(p.Position.Y) + 5, ~2);
                        p.State = PState.ToCheckInExit;
                        p.Target = RuneOr(RuneId.ToCheckInExit, 0, p.PersonalRand, p.Target);
                    }
                    break;
                case PState.DroppingSuitcase:
                {
                    p.State = PState.ToCheckInExit | PState.WaitFlag;
                    p.WaitCount = 200;
                    int bag = SuitcaseId(k.HasSuitcase);
                    if (bag >= 0) AddPersonToQueue(bag, p.Position - new Pt(0, 22), Reason.SuitcaseExit, 0, 0, 0);
                    if (k.HasSuitcase != 0)
                        for (int d = 0; d < HallData.MaxClans; d++)
                            if (Data.Clans[d] is ClanDef t && t.HasSuitcase == -k.HasSuitcase) { p.ClanId = d; break; }
                    PAnd(CustCol(p.Position.X), CustRow(p.Position.Y) + 5, ~2);
                    p.Target = RuneOr(RuneId.ToCheckInExit, 0, p.PersonalRand, p.Target);
                    p.LookDir = 0; p.Phase = 0; p.Dir = 1;
                    break;
                }
                case PState.ToCheckInExit:
                    p.State = PState.ToWaitRoom | PState.WaitFlag;
                    if (gate == -1) p.State = PState.Leaving; else p.Target = RuneOr(RuneId.ToWait, gate, p.PersonalRand, p.Target);
                    break;
                case PState.ToWaitRoom:
                    OpenAT.Sim.Tutorial.TutorialEngine.PassengerInWaitingZone(World, p.FlightAirline);
                    if (hasFlight && Flights.PlaneAtGate(p.FlightAirline, p.FlightPlaneId))
                    {
                        p.State = PState.ToScanner;
                        if (gate == -1) p.State = PState.Leaving; else p.Target = RuneOr(RuneId.Scanner, gate, p.PersonalRand, p.Target);
                    }
                    else if (!hasFlight || (startHour == Clock.Hour && Clock.Minute >= 55) || startHour < Clock.Hour)
                    {
                        p.Mood = p.FirstClass ? Mood.MissedFC : Mood.Missed;
                        p.State = PState.ToExit;
                        p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                    }
                    else p.State = PState.Waiting | PState.BrowseFlag;
                    break;
                case PState.ToScanner:
                {
                    p.State = PState.Boarding;
                    p.Target = RuneOr(RuneId.Wait, gate, p.PersonalRand, p.Target);
                    int sec = Flights?.SecurityFlags(p.FlightAirline) ?? 0;
                    if ((sec & (1 << 8)) != 0)
                    {
                        if ((sec & (1 << 11)) != 0 || (sec & (1 << 10)) != 0)
                        {
                            p.State |= PState.WaitFlag;
                            p.WaitCount = 240;
                            World.Events.Publish(new HallGateVideo(gate, (sec & (1 << 11)) != 0 ? HallGateVideo.Show : HallGateVideo.Hand));
                        }
                        else if (p.PersonalRand.Rand(30) == 0)
                        {
                            p.State = PState.Leaving;
                            World.Events.Publish(new HallGateVideo(gate, HallGateVideo.Trap));
                        }
                    }
                    break;
                }
                case PState.Boarding:
                    if (hasFlight && Flights.PlaneAtGate(p.FlightAirline, p.FlightPlaneId))
                    {
                        p.State = PState.EnteringPlane;
                        if (gate == -1) p.State = PState.Leaving; else p.Target = RuneOr(RuneId.WaitPlane, gate, p.PersonalRand, p.Target);
                    }
                    else
                    {
                        p.Mood = p.FirstClass ? Mood.MissedFC : Mood.Missed;
                        p.State = PState.ToExit;
                        p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                    }
                    break;
                case PState.EnteringPlane:
                    p.State = PState.Leaving;
                    break;
                case PState.ToShop:
                    if (k.Type != ClanType.Suitcase) { p.Phase = 0; p.Dir = p.LookDir = 8; }
                    p.State = PState.Shopping | PState.BrowseFlag;
                    p.Target = RuneOr(RuneId.Shop, p.StatePar, p.PersonalRand, p.Target);
                    break;
                case PState.Shopping:
                    p.Dir = p.LookDir = 0;
                    p.WaitCount = 150 + (p.Target.Y + p.Target.X + p.ClanId * 33) % 100;
                    p.State = PState.ExShop | PState.BrowseFlag | PState.WaitFlag;
                    p.Target = RuneOr(RuneId.ToShop, p.StatePar, p.PersonalRand, p.Target);
                    break;
                case PState.ExShop:
                    p.State = PState.ToExit;
                    p.Target = RuneOr(RuneId.Destruction, 0, p.PersonalRand, p.Target);
                    p.Mood = Mood.Home;
                    break;
                case PState.ToExit:
                    p.State = PState.Leaving;
                    break;
            }
        }
    }

    /// <summary>A passenger walked through stench: the airline lost <see cref="Points"/> image (§6.5, already applied).</summary>
    public sealed class HallImagePenalty : Core.ISimEvent
    {
        public readonly int Airline, Points;
        public HallImagePenalty(int airline, int points) { Airline = airline; Points = points; }
    }

    /// <summary>A gate security video should play (§7.1 step 7, §8.4).</summary>
    public sealed class HallGateVideo : Core.ISimEvent
    {
        public const int Show = 0, Hand = 1, Trap = 2;
        public readonly int Gate, Kind;
        public HallGateVideo(int gate, int kind) { Gate = gate; Kind = kind; }
    }
}
