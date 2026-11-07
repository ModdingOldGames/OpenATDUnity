using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Nasa
{
    /// <summary>
    /// The NASA shop of the space missions (rooms-and-ui-8 §5): the salesman (button press after every sale,
    /// <see cref="TipNow"/>), the chick in the display case that sometimes turns into an alien, radio chatter.
    /// The dialog starts by itself on entry; the first attempt to leave shows the farewell (Nasa 9999,
    /// <see cref="NasaFarewellCommand"/>). Parts are bought with <see cref="BuySpacePartCommand"/>.
    /// </summary>
    [Room(RoomIds.Nasa)]
    [ScreenName("nasa")]
    public sealed class NasaRoom : RoomScreen
    {
        protected override string Library => "room/nasa.gli";
        protected override string Background => "NASA";
        protected override int DefaultDialogPartner => Talkers.Nasa;
        const int ClickMan = 10;

        SmackerPerson _man, _case;
        /// <summary>"Tip now": 12 = play the button-press clip next (set by a sale).</summary>
        internal int TipNow = -1;
        int _alien = -1;
        AudioSource _loop;

        protected override void OnCreate()
        {
            int x = 210, y = 140;
            _man = new SmackerPerson(Assets, App.Audio);
            System.Func<int> tip = () => TipNow;
            _man.Clip(0, "LE_RedeW.smk", x, y, Mood.Talking, "A1:7", 1, 1, 2, 2, canCancel: false);
            _man.Clip(1, "LE_WaitK.smk", x, y, Mood.Idle, "A1:14");
            _man.Clip(2, "LE_lock.smk", x, y, Mood.Idle, "A1:14", canCancel: false);
            _man.Clip(3, "LE_strck.smk", x, y, Mood.Idle, "A1:4", canCancel: false);
            _man.Clip(4, "LE_strkW.smk", x, y, Mood.Idle, "A1:5", 1, 1, 20, 90);
            _man.Clip(5, "LE_strkZ.smk", x, y, Mood.Idle, "A1:14", canCancel: false);
            _man.Clip(6, "LE_turn.smk", x, y, Mood.Idle, "A9:7 E1:7 E1:9 E1:13", canCancel: false);
            _man.Clip(7, "LE_RedeW.smk", x, y, Mood.Listening, "A9:7 A2:8 E1:9 E1:10 E1:11 E1:13 A1:15", 1, 1, 10, 10).Decide = tip;
            _man.Clip(8, "LE_RedeK.smk", x, y, Mood.Listening, "A9:7 E1:9 E1:13").Decide = tip;
            for (int c = 9; c <= 11; c++)
                _man.Clip(c, "LE_Rede" + (char)('A' + c - 9) + ".smk", x, y, Mood.Talking, "A4:9 A4:10 A4:11 E1:13 E1:7").Decide = tip;
            _man.Clip(12, "LE_taste.smk", x, y, Mood.Listening, "A9:7 E1:9 E1:13", canCancel: false).OnStart = () => TipNow = -1;
            _man.Clip(13, "LE_TurnZ.smk", x, y, Mood.Idle, "A9:14", canCancel: false);
            _man.Clip(14, "LE_Wait.smk", x, y, Mood.Idle, "A9:14 A2:1 A1:2 A1:3 E1:6", 1, 1, 10, 10);
            var press = _man.Clip(15, "LE_taste.smk", x, y, Mood.Listening, "A9:7 E1:9 E1:13", canCancel: false);
            press.OnFrame = f => { if (f == 6) _alien = 1; };

            _case = new SmackerPerson(Assets, App.Audio);
            _case.Clip(0, "Chick_.smk", 445, 175, Mood.Idle, "A1:0").Decide = () => _alien;
            _case.Clip(1, "Alien_.smk", 445, 175, Mood.Idle, "A9:0").OnStart = () => _alien = -1;
        }

        protected override void OnRoomEnter()
        {
            _loop = App.Audio.PlayLoop("SOUND/funk.raw", 0.4f);
            StartDialog(Talkers.Nasa, DialogMedium.Air, 0);
        }

        protected override void OnLeaveRoom()
        {
            if (_loop != null) App.Audio.StopLoop(_loop);
            _loop = null;
            _man.Dispose(); _case.Dispose();
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Nasa ? _man : null;

        public override void Animate(float dt) { _man.Update(dt); _case.Update(dt); }

        protected override void Hotspots(InputState input)
        {
            Hot(197, 140, 398, 377, ClickMan);
            Exit(195, 373, 424, 440);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMan) StartDialog(Talkers.Nasa, DialogMedium.Air, 1);
        }

        /// <summary>The first attempt to leave (exit or right click) shows the farewell instead (§5).</summary>
        protected override void Leave()
        {
            var s = App.Session;
            if (s != null && !s.Local.TalkedToNasa && s.World.Execute(new NasaFarewellCommand(s.Local.Index)).Ok)
            {
                StartDialog(Talkers.Nasa, DialogMedium.Air, 2);
                App.Advisors.HidePlayer = true;
                return;
            }
            base.Leave();
        }

        internal void LeaveNow() => base.Leave();

        protected override void DrawRoom(Screen2D s)
        {
            _case.Draw(s, RX);
            _man.Draw(s, RX);
        }
    }

    /// <summary>
    /// The NASA salesman (rooms-and-ui-3b §12, items-security-special.md §4.1): rocket parts in the Final
    /// mission, space-station modules in add-on 10; by phone the option ids are +100.
    /// </summary>
    [DialogScript(Talkers.Nasa)]
    public sealed class NasaDialog : DialogTree
    {
        static bool Rocket(DialogSession d) => SpaceParts.IsRocketMission(d.World.Difficulty);
        static int Phone(DialogSession d) => d.Medium == DialogMedium.Phone ? 100 : 0;
        static int B(DialogSession d) => (Rocket(d) ? 2000 : 2200) + Phone(d);
        static int Flags(DialogSession d) => d.Player.Counters.RocketFlags;

        static void RocketShop(DialogSession d)
        {
            if ((Flags(d) & SpaceParts.RocketStage1) != SpaceParts.RocketStage1) d.Choices(5000, 5003);
            else d.Choices(5100, 5107);
        }

        static void StationShop(DialogSession d)
        {
            int f = Flags(d);
            if ((f & SpaceParts.StationStage2) == SpaceParts.StationStage2) d.Choices(8200, 8202);
            else if ((f & SpaceParts.StationStage1) == SpaceParts.StationStage1) d.Choices(8100, 8105);
            else d.Choices(8000, 8003);
        }

        static void Buy(DialogSession d, int bit, int success)
        {
            var r = d.World.Execute(new BuySpacePartCommand(d.Player.Index, bit));
            if (r.Ok)
            {
                d.Stage.App.Audio.Play("SOUND/taeterae.raw");
                if (d.Stage is NasaRoom room) room.TipNow = 12;
                d.Say(success);
                return;
            }
            if (r.Reason != null && r.Reason.StartsWith("nasa-") && int.TryParse(r.Reason.Substring(5), out int line)) d.Say(line);
            else if (Rocket(d)) RocketShop(d); else StationShop(d);
        }

        static int StationBit(int id) => id >= 8200 ? 8 + id - 8200 : id >= 8100 ? 3 + id - 8100 : id - 8000;

        public NasaDialog() : base("Nasa")
        {
            Open(d =>
            {
                var p = d.Player;
                switch (d.Par1)
                {
                    case 0: d.Say(500); return;
                    case 2: d.Say(9999); return;
                }
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                const string met = "openat.nasaMet";
                if (d.Medium == DialogMedium.Phone) d.Say(1001);
                else if (d.TalkedToA) d.Say(1003);
                else if (p.Extensions.ContainsKey(met)) d.Say(1002);
                else d.Say(1000);
                d.TalkedToA = true;
                p.Extensions[met] = true;
            });
            On(1000, 1003, (d, _) => d.Choices(B(d), B(d) + 3));
            On(new[] { 2000, 2100, 2200, 2300 }, d => d.Say(3000));
            On(3000, d => d.PlayerSays(3001));
            On(3001, d => d.Say(3002));
            On(3002, d => d.Choices(B(d) + 1, B(d) + 3));
            On(new[] { 2001, 2101 }, d => d.Say(4000));
            On(4000, d => d.Say(4001));
            On(4001, d => d.Say(4002));
            On(4002, d => d.Say(4003));
            On(new[] { 4003, 5003, 5107 }, d => d.Choices(2002 + Phone(d), 2003 + Phone(d)));
            On(new[] { 2201, 2301 }, d => d.Say(4100));
            On(4100, d => d.Say(4101));
            On(4101, d => d.Say(4102));
            On(4102, d => d.Say(4103));
            On(new[] { 4103, 8003, 8105, 8202 }, d => d.Choices(B(d) + 2, B(d) + 3));
            On(new[] { 2002, 2102, 7000, 5096, 5097, 5098, 5099, 5196, 5197 }, RocketShop);
            for (int id = 5000; id <= 5106; id++)
            {
                int bit = SpaceParts.RocketBitOfMenu(id);
                if (bit >= 0) On(id, d => Buy(d, bit, 5099));
            }
            On(new[] { 2202, 2302, 8300, 8301, 8302 }, StationShop);
            On(8000, 8002, (d, id) => Buy(d, StationBit(id), 8302));
            On(8100, 8104, (d, id) => Buy(d, StationBit(id), 8302));
            On(8200, 8201, (d, id) => Buy(d, StationBit(id), 8302));
            On(2003, d => d.Say(6000));
            On(9999, d =>
            {
                d.End();
                d.Stage.App.Advisors.HidePlayer = false;
                if (d.Stage is NasaRoom room) room.LeaveNow();
            });
        }
    }
}
