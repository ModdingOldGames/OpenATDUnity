namespace OpenAT.Sim.Hall
{
    /// <summary>A point in hall space (pixels or logical walk units; see airport-and-people.md §3).</summary>
    public struct Pt : System.IEquatable<Pt>
    {
        public int X, Y;
        public Pt(int x, int y) { X = x; Y = y; }
        public static Pt operator +(Pt a, Pt b) => new Pt(a.X + b.X, a.Y + b.Y);
        public static Pt operator -(Pt a, Pt b) => new Pt(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Pt a, Pt b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Pt a, Pt b) => !(a == b);
        public bool Equals(Pt o) => this == o;
        public override bool Equals(object o) => o is Pt p && this == p;
        public override int GetHashCode() => X * 7919 + Y;
        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>Invisible marker bricks ("runes", airport-and-people.md §4.4). Values are brick.csv ids.</summary>
    public static class RuneId
    {
        public const int Creation = 2000, PCreation = 2001, Destruction = 2002, PCreation2 = 2003;
        public const int Up = 2010, Right = 2011, South = 2012, Left = 2013;
        public const int BeltRight = 2020, BeltLeft = 2021, WestEast = 2022, NorthSouth = 2023;
        public const int NoPlayer = 2025, NoClient = 2026;
        public const int CheckIn = 2030, ToCheckIn = 2031, Shop = 2032, ToShop = 2033, Wait = 2034, ToWait = 2035;
        public const int UpRight = 2036, RightSouth = 2037, SouthLeft = 2038, LeftUp = 2039;
        public const int WaitPlane = 2040, DropSuitcase = 2041, ExitSuitcase = 2042, CreateSuitcase = 2043;
        public const int ToCheckInExit = 2044, Scanner = 2045;
        public const int LeftEnd = 2050, RightEnd = 2051;
        public const int WaypointStart = 2054, Waypoint = 2055, WaypointGimmick = 2056, WaypointWait = 2057;
        public const int NoLeft = 2060, NoDown = 2061, NoWay = 2062, AreaTopLeft = 2063, AreaBottomRight = 2064;
        /// <summary>Clip markers: the code's meaning (FULL = 2065, MIDDLE = 2066) differs from the csv text (§4.4).</summary>
        public const int ClipFull = 2065, ClipMiddle = 2066, ClipNone = 2067;
        public const int CondBlock = 2070;

        /// <summary>Runes kept in the rune list for queries (the original's selection in CalcCoordinates).</summary>
        public static bool IsQueryRune(int id) =>
            (id >= Creation && id <= PCreation2) || (id >= CheckIn && id <= ToWait) || id == AreaTopLeft || id == AreaBottomRight
            || id == WaitPlane || id == DropSuitcase || id == ExitSuitcase || id == CreateSuitcase || id == ToCheckInExit
            || id == WaypointStart || id == Waypoint || id == WaypointGimmick || id == WaypointWait || id == CondBlock || id == Scanner;
    }

    /// <summary>Room ids used by the hall (rooms-and-ui-9-room-hosting.md §2).</summary>
    public static class RoomIds
    {
        public const int Airport = 1;
        public const int OfficeA = 10, PersonnelA = 11, OfficeD = 40, PersonnelD = 41;
        public const int Stairs1Up = 50, Stairs1Down = 51, StairsFirst = 50, StairsLast = 55;
        public const int Shop1 = 80, Shop4 = 83, Ricks = 84, RouteBox = 85;
        public const int Bank = 100, Kiosk = 101, Museum = 102, LastMinute = 103, ArabAir = 104, Makler = 105;
        public const int Aufsicht = 107, Werbung = 109, Werkstatt = 110, Reisebuero = 111, Sabotage = 112;
        public const int WcF = 113, WcM = 114, Nasa = 115, Insel = 116, Rushmore = 118, Fracht = 121, Weltall = 122;
        public const int Security = 123, Designer = 125;
        public const int FlightDisplay = 140;
        public const int ReiseX1 = 200, LastMinX1 = 210, BeltX1 = 220, BeltX10 = 229;
        public const int Monitor1 = 230, Monitor6 = 235, Monitor7 = 236, Monitor9 = 238;
        public const int Phone1 = 240, Phone4 = 243, Electro = 244, AirportExit = 250, Wall = 254, AntiMarker = 255;

        public static bool IsStairs(int r) => r >= StairsFirst && r <= StairsLast;
        public static bool IsPassengerMarker(int r) => r >= ReiseX1 && r <= Monitor6;
        /// <summary>Offices and personnel rooms 10..41 (owner = room / 10 - 1).</summary>
        public static bool IsOfficeOrPersonnel(int r) => r >= OfficeA && r <= PersonnelD;
    }

    /// <summary>brick.csv obstacle types (§4.3).</summary>
    public static class Obstacle
    {
        public const int None = 0, Point = 1, Wide = 2, SitPoint = 3, ShortFence = 4, Horizontal = 10, Vertical = 11,
            BigVertical = 12, HorizontalShifted = 14, HorizontalExit = 20, ShopFront = 21, StairFront = 22, BuroFrontR = 23,
            NoEntry1 = 24, NoEntry2 = 25, Museum = 26, BuroFrontL = 27, Up = 40, Right = 41, Down = 42, Left = 43, Rune = 99;
    }

    /// <summary>Walk-grid tile bits (§3.4).</summary>
    public static class Plate
    {
        public const int ExitN = 128, ExitE = 64, ExitS = 32, ExitW = 16, Exits = 240;
        public const int Customers = 8, Players = 4, Occupied = 2, Special = 1;
        public const byte Initial = 0xFC;
        public const int Rows = 16;
        /// <summary>Spare columns in front of column 0 (columns may go negative).</summary>
        public const int SpareColumns = 20;
        public const int TileW = 44, TileH = 22;
        public const int OffsetX = 42;
        /// <summary>Direction bit for a 4-way direction 0 N, 1 E, 2 S, 3 W.</summary>
        public static int Bit(int dir4) => 128 >> dir4;
    }

    /// <summary>Person state machine values (airport-and-people.md §6.5, §7).</summary>
    public static class PState
    {
        public const int Browsing = 0, ToCheckIn = 1, CheckingIn = 2, ToWaitRoom = 3, Waiting = 4, Boarding = 5, SitWaiting = 6,
            EnteringPlane = 7, LeavingPlane = 8, ToShop = 10, Shopping = 11, ExShop = 12, DroppingSuitcase = 13,
            ToCheckInExit = 14, Waypointing = 15, ToScanner = 16, ToExit = 30, Leaving = 31, Talking = 32;
        public const int BrowseFlag = 64, WaitFlag = 128;
        public static int Base(int s) => s & ~(BrowseFlag | WaitFlag);
    }

    /// <summary>Why a person is in the airport.</summary>
    public static class Reason
    {
        public const int Player = 0, Shopping = 50, Leaving = 51, Flying = 52, SuitcaseExit = 53, Waypoint = 54;
    }

    /// <summary>Clan types from clan.csv.</summary>
    public static class ClanType
    {
        public const int Female = 10, Male = 11, BrownFemale = 18, BrownMale = 19;
        public const int Player1 = 21, Player4 = 24, Suitcase = 30, Animation = 40;
        public static bool IsPlayer(int t) => t >= Player1 && t <= Player4;
        public static bool IsCustomer(int t) => t == Female || t == Male || t == BrownFemale || t == BrownMale || t == Suitcase;
    }

    /// <summary>Thought-bubble ids (§6.7).</summary>
    public static class Mood
    {
        public const int Base2 = 5, Base3 = 13, Repair = 5, Sick = 6, Smile = 7, Flying = 8, Missed = 9, Ticket = 10, Expensive = 11,
            Stars2 = 12, Stars3 = 13, Bank = 14, BankRobber = 15, Beverage = 16, Chocolate = 17, None = 18, Travel = 19,
            Home = 20, Screen = 21, Museum = 22, Toilet = 23, Bone = 24, Stench = 25, Stuck = 26, StenchDog = 27, Empty = 28,
            RepairFC = 29, SickFC = 30, SmileFC = 31, FlyingFC = 32, MissedFC = 33, TicketFC = 34, ExpensiveFC = 35,
            Stars2FC = 36, Stars3FC = 37;
        public const int CountStart = 60;

        /// <summary>
        /// Number in the bubble bitmap's name (<c>MOOD&lt;nn&gt;</c> in glstd.gli, §6.7): ids 0-4 use 00-04, the
        /// library has no 05-09, so ids 5-37 use 10-42 (id + 5).
        /// </summary>
        public static int BitmapNumber(int mood) => mood < 5 ? mood : mood + 5;

        /// <summary>Passenger opinions (5-13) and their first-class variants (29-37) show the flight's airline logo (§6.7).</summary>
        public static bool ShowsAirlineLogo(int mood) => (mood >= Base2 && mood <= Base3) || (mood >= RepairFC && mood <= Stars3FC);
    }

    /// <summary>Opening times of airport services in clock units (rooms-and-ui-9 §5.2).</summary>
    public static class OpeningHours
    {
        public const int DutyOpen = 10 * 60000, DutyClose = 16 * 60000, ArabOpen = 11 * 60000, LastClose = 16 * 60000,
            MuseOpen = 11 * 60000, ReisClose = 17 * 60000, MaklClose = 16 * 60000, WerbOpen = 12 * 60000;
    }
}
