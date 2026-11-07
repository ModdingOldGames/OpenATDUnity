using H = OpenAT.Sim.Hall.RoomIds;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// Original room numbers (rooms-and-ui-9 §2). New rooms added by extensions should use ids ≥ 1000
    /// so they never collide with original ids (which are also save-game values).
    /// </summary>
    public static class RoomIds
    {
        // Ids the airport hall also knows come from the simulation's table (one source of truth).
        public const int Airport = H.Airport, Options = 2, Globe = 6, Laptop = 7, Abend = 8;
        public const int OfficeA = H.OfficeA, OfficeB = 20, OfficeC = 30, OfficeD = H.OfficeD;
        public const int PersonalA = H.PersonnelA, PersonalB = 21, PersonalC = 31, PersonalD = H.PersonnelD;
        public const int DutyFree = H.Shop1, Ricks = H.Ricks, RouteBox = H.RouteBox;
        public const int Bank = H.Bank, Kiosk = H.Kiosk, Museum = H.Museum, LastMinute = H.LastMinute, ArabAir = H.ArabAir, Makler = H.Makler;
        public const int Aufsicht = H.Aufsicht, Tafel = 108, Werbung = H.Werbung, Werkstatt = H.Werkstatt, Reisebuero = H.Reisebuero, Sabotage = H.Sabotage;
        public const int WcF = H.WcF, WcM = H.WcM, Nasa = H.Nasa, Insel = H.Insel, World = 117, Rushmore = H.Rushmore, Statistics = 119;
        public const int PlaneProps = 120, Fracht = H.Fracht, WeltAll = H.Weltall, Security = H.Security, Editor = 124, Designer = H.Designer;
        public const int AirportExit = H.AirportExit, Wall = H.Wall;
        /// <summary>First id free for extensions.</summary>
        public const int FirstCustom = 1000;

        public static int OfficeOf(int player) => OfficeA + 10 * player;
        public static int PersonalOf(int player) => PersonalA + 10 * player;
        public static bool IsOffice(int id) => id == OfficeA || id == OfficeB || id == OfficeC || id == OfficeD;

        /// <summary>First-visit description number (rooms-and-ui-1 §5.4), 0 = none.</summary>
        public static int DescriptionIndex(int room)
        {
            if (IsOffice(room)) return 1;
            switch (room)
            {
                case PersonalA: case PersonalB: case PersonalC: case PersonalD: return 2;
                case DutyFree: return 3;
                case Bank: return 4;
                case Kiosk: return 5;
                case Museum: return 6;
                case LastMinute: return 7;
                case ArabAir: return 8;
                case Makler: return 9;
                case Aufsicht: return 10;
                case Tafel: return 11;
                case Werbung: return 12;
                case Werkstatt: return 13;
                case Reisebuero: return 14;
                case Sabotage: return 15;
                case Nasa: return 16;
                case Insel: return 17;
                case Rushmore: return 18;
                case Statistics: return 19;
                case PlaneProps: return 20;
                case Ricks: return 21;
                case RouteBox: return 22;
                case Fracht: return 23;
                case WeltAll: return 24;
                case Editor: return 25;
                case Security: return 26;
                case Designer: return 27;
            }
            return 0;
        }
    }
}
