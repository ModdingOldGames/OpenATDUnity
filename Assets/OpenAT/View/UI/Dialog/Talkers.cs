using System.Collections.Generic;
using UnityEngine;

namespace OpenAT.View.UI.Dialog
{
    /// <summary>Bubble placement relative to its anchor (rooms-and-ui-3 §2.1).</summary>
    public enum BubbleStyle { Below = 0, Left = 1, Above = 2, Right = 3 }

    public enum DialogMedium { Air = 0, Phone = 1 }

    /// <summary>One dialog partner (rooms-and-ui-3 §2, rooms-and-ui-1 §4.6).</summary>
    public sealed class TalkerInfo
    {
        public int Id;
        public string Name;
        /// <summary>4-character text group in dlg_ger.res.</summary>
        public string Group;
        /// <summary>Voice directory under VOICE/.</summary>
        public string VoiceDir;
        /// <summary>Room the character sits in (phone pane source), -1 none.</summary>
        public int Room = -1;
        public Vector2Int BubblePos;
        public BubbleStyle Style = BubbleStyle.Above;
        public Vector2Int? PhoneBubblePos;
        public BubbleStyle PhoneStyle = BubbleStyle.Above;
        /// <summary>X of the 340 px slice of the room picture shown in the phone pane.</summary>
        public int HandyOffset = 160;
    }

    /// <summary>
    /// The talker table. Original partners use ids 0..26 (TALKER_*); extensions register new ids
    /// (use values ≥ 1000) with <see cref="Register"/>.
    /// </summary>
    public static class Talkers
    {
        public const int Personal1a = 0, Personal1b = 1, Banker1 = 8, Banker2 = 9, Museum = 10, Arab = 11, Boss = 12,
            Makler = 13, Mechaniker = 14, Werbung = 15, DutyFree = 16, Sabotage = 17, Nasa = 18, World = 19,
            Competitor = 20, Kiosk = 21, Rick = 22, Trinker = 23, Fracht = 24, Security = 25, Designer = 26,
            Passenger = 998, Player = 999;

        static readonly Dictionary<int, TalkerInfo> _all = new Dictionary<int, TalkerInfo>();

        static Talkers()
        {
            for (int p = 0; p < 4; p++)
            {
                Add(new TalkerInfo { Id = 2 * p, Name = "personnel-a", Group = "Jobs", VoiceDir = "PE", Room = 11 + 10 * p, BubblePos = V(443, 208), Style = BubbleStyle.Left, PhoneBubblePos = V(535, 207), PhoneStyle = BubbleStyle.Left, HandyOffset = 280 });
                Add(new TalkerInfo { Id = 2 * p + 1, Name = "personnel-b", Group = "Jobs", VoiceDir = "PE", Room = 11 + 10 * p, BubblePos = V(280, 144), Style = BubbleStyle.Right, PhoneBubblePos = V(435, 124), PhoneStyle = BubbleStyle.Left, HandyOffset = 120 });
            }
            Add(new TalkerInfo { Id = Banker1, Name = "banker-1", Group = "Bank", VoiceDir = "BA", Room = 100, BubblePos = V(477, 157), Style = BubbleStyle.Above, PhoneBubblePos = V(479, 157), PhoneStyle = BubbleStyle.Above, HandyOffset = 367 });
            Add(new TalkerInfo { Id = Banker2, Name = "banker-2", Group = "Bank", VoiceDir = "B2", Room = 100, BubblePos = V(240, 147), Style = BubbleStyle.Above, PhoneBubblePos = V(467, 202), PhoneStyle = BubbleStyle.Left, HandyOffset = 153 });
            Add(new TalkerInfo { Id = Museum, Name = "museum", Group = "Muse", VoiceDir = "MU", Room = 102, BubblePos = V(440, 234), PhoneBubblePos = V(440, 234), HandyOffset = 367 });
            Add(new TalkerInfo { Id = Arab, Name = "arab", Group = "Arab", VoiceDir = "AA", Room = 104, BubblePos = V(486, 183), Style = BubbleStyle.Left, PhoneBubblePos = V(526, 183), PhoneStyle = BubbleStyle.Left, HandyOffset = 160 });
            Add(new TalkerInfo { Id = Boss, Name = "boss", Group = "Boss", VoiceDir = "BO", Room = 107, BubblePos = V(400, 102), PhoneBubblePos = V(470, 158), PhoneStyle = BubbleStyle.Left, HandyOffset = 160 });
            Add(new TalkerInfo { Id = Makler, Name = "makler", Group = "Makl", VoiceDir = "MA", Room = 105, BubblePos = V(540, 200), PhoneBubblePos = V(540, 200), HandyOffset = 378 });
            Add(new TalkerInfo { Id = Mechaniker, Name = "mechanic", Group = "Mech", VoiceDir = "ME", Room = 110, BubblePos = V(200, 203), PhoneBubblePos = V(445, 199), HandyOffset = 131 });
            Add(new TalkerInfo { Id = Werbung, Name = "advertising", Group = "Werb", VoiceDir = "WE", Room = 109, BubblePos = V(400, 250), PhoneBubblePos = V(526, 300), PhoneStyle = BubbleStyle.Left, HandyOffset = 160 });
            Add(new TalkerInfo { Id = DutyFree, Name = "dutyfree", Group = "Duty", VoiceDir = "DU", Room = 80, BubblePos = V(396, 128), Style = BubbleStyle.Left, PhoneBubblePos = V(396, 198), PhoneStyle = BubbleStyle.Left, HandyOffset = 160 });
            Add(new TalkerInfo { Id = Sabotage, Name = "saboteur", Group = "Sabo", VoiceDir = "SA", Room = 112, BubblePos = V(396, 198), Style = BubbleStyle.Left, PhoneBubblePos = V(400, 188), PhoneStyle = BubbleStyle.Left, HandyOffset = 367 });
            Add(new TalkerInfo { Id = Nasa, Name = "nasa", Group = "Nasa", VoiceDir = "NA", Room = 115, BubblePos = V(394, 114), PhoneBubblePos = V(350, 292), PhoneStyle = BubbleStyle.Left, HandyOffset = 160 });
            Add(new TalkerInfo { Id = World, Name = "world", Group = "Welt", VoiceDir = "WO", Room = 117, HandyOffset = 170 });
            Add(new TalkerInfo { Id = Competitor, Name = "competitor", Group = "Play", VoiceDir = "PL", BubblePos = V(560, 380), Style = BubbleStyle.Left });
            Add(new TalkerInfo { Id = Kiosk, Name = "kiosk", Group = "Kios", VoiceDir = "KI", Room = 101, BubblePos = V(288, 192) });
            Add(new TalkerInfo { Id = Rick, Name = "rick", Group = "Rick", VoiceDir = "RI", Room = 84, BubblePos = V(220, 105) });
            Add(new TalkerInfo { Id = Trinker, Name = "drinker", Group = "Rick", VoiceDir = "TR", Room = 84, BubblePos = V(480, 113) });
            Add(new TalkerInfo { Id = Fracht, Name = "freight", Group = "Frac", VoiceDir = "FR", Room = 121, BubblePos = V(556, 126), Style = BubbleStyle.Left });
            Add(new TalkerInfo { Id = Security, Name = "security", Group = "Secu", VoiceDir = "SE", Room = 123, BubblePos = V(328, 100) });
            Add(new TalkerInfo { Id = Designer, Name = "designer", Group = "Desi", VoiceDir = "DE", Room = 125, BubblePos = V(268, 126), Style = BubbleStyle.Below });
            Add(new TalkerInfo { Id = Passenger, Name = "passenger", Group = "Pass", VoiceDir = "" });
        }

        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        static void Add(TalkerInfo t) => _all[t.Id] = t;

        public static void Register(TalkerInfo t) => _all[t.Id] = t;
        public static TalkerInfo Get(int id) => _all.TryGetValue(id, out var t) ? t : null;
    }
}
