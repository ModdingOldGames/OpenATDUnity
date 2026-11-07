using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Anyone moving through the hall: player figures, shoppers, passengers, luggage, waypoint walkers,
    /// sabotage effects (airport-and-people.md §6). Field meanings follow the spec; for players
    /// <see cref="State"/> is the player index.
    /// </summary>
    public sealed class Person
    {
        /// <summary>Clan album slot (= pool number).</summary>
        public int ClanId;
        /// <summary>Movement direction. Customers: 0 N, 1 E, 2 S, 3 W, 4 stop, 8 waiting. Players: 0..7 compass, 8 stop.</summary>
        public int Dir;
        /// <summary>Sprite slot: 0-3 walk, 4 gimmick, 5 wave/stuck, 6/7 sit, 8 stand (Phase = facing), 9 waiting gimmick.</summary>
        public int LookDir;
        public int Phase;
        public int WaitCount;
        public int Mood;
        public int MoodCountdown;
        public int Reason;
        public int FlightAirline;
        public int FlightPlaneId;
        /// <summary>Flight index; waypoint walkers: current waypoint; also the 0..15 sprite jitter seed.</summary>
        public int FlightPlaneIndex;
        public bool FirstClass;
        /// <summary>Logical position (§3.2): lower floor y ≥ 0, upper floor y ≥ 5000.</summary>
        public Pt Position;
        /// <summary>Foot point in hall pixels.</summary>
        public Pt ScreenPos;
        public Pt Target;
        public int State;
        public int StatePar;
        public bool Running;
        public int FloorOffset;
        public readonly TeakRand PersonalRand = new TeakRand();
        public readonly TeakRand PersonalRandWalk = new TeakRand();

        /// <summary>False once removed (the slot is compacted at the next depth sort).</summary>
        public bool Alive = true;
        /// <summary>Stable id for view-side caches and camera focus.</summary>
        public int Uid;

        public int BaseState => PState.Base(State);
        public bool IsUpstairs => Position.Y >= 4000;

        /// <summary>Depth sort key (§3.5): upper-floor persons after lower-floor ones, then by foot y.</summary>
        internal static int Compare(Person a, Person b)
        {
            int ya = a.ScreenPos.Y + a.FloorOffset, yb = b.ScreenPos.Y + b.FloorOffset;
            bool ua = ya < 170, ub = yb < 170;
            if (ua == ub) return ya.CompareTo(yb);
            return ua ? 1 : -1;
        }

        public override string ToString() => $"P#{Uid} clan {ClanId} pos {Position} st {State}/{StatePar} dir {Dir} look {LookDir}";
    }

    /// <summary>Per-airline walking state kept by the hall (the original keeps it in PLAYER).</summary>
    public sealed class HallPlayer
    {
        public readonly int Index;
        public HallPlayer(int i) => Index = i;

        public Pt PrimaryTarget, SecondaryTarget, TertiaryTarget;
        public int NewDir = 8;
        public bool WalkActive;
        /// <summary>Room the player is walking to (0 none).</summary>
        public int DirectToRoom;
        /// <summary>Room just left (or −1 while auto-walking into one).</summary>
        public int ExRoom;
        public int WaitForRoom;
        public int ThrownOutOfRoom;
        /// <summary>Room barred after the maximum stay (0 = none) and since when (<see cref="HallState.UpdateStayLimit"/>).</summary>
        public int LocationForbidden, LocationForbiddenTime;
        /// <summary>Clock time the player entered one of the max-stay rooms (−1 = not in one).</summary>
        public int LocationTime = -1;
        /// <summary>1..5 (3 + fitness advisors, §6.4). The staff domain may change it.</summary>
        public int WalkSpeed = 3;
        public int IsStuck;
        public int StandCount;
        public int IsWalkingToPlayer = -1;
        public bool RunningToToilet;
        /// <summary>Toilet room (113/114) while the local figure is held in the toilet pause (MENU_WC_F/M), else 0.</summary>
        public int OnToilet;
        /// <summary>Ticks spent in the toilet pause so far.</summary>
        public int OnToiletTicks;
        /// <summary>Face-to-face talk partner + 1 (0 = none); a computer figure stands still meanwhile.</summary>
        public int TalkPartner;
        /// <summary>Phone partner + 1 while this airline is on the phone with another airline (0 = none, <see cref="PhoneCallCommand"/>).</summary>
        public int PhonePartner;
        public int Koffein;
        /// <summary>Room the player is in (0 / <see cref="RoomIds.Airport"/> = hall). Set when a room is
        /// requested; cleared with <see cref="ReturnToHallCommand"/>.</summary>
        public int Room;
        /// <summary>Ticks since the local player last moved (camera re-centring, §5.3).</summary>
        public int DidntMove;

        public bool InHall => Room == 0 || Room == RoomIds.Airport;
    }
}
