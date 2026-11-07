using OpenAT.Sim.Contracts;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Counters
{
    /// <summary>
    /// Freight counter (rooms-and-ui-6 §7): six cargo orders as crates hanging from the crane, the freight
    /// master (dialog, buzzer that drops a taken crate), the forklift. A taken crate falls with the stamp
    /// and lands with a crash. The glue pot on the floor is the end of the paper clip → glue chain: the freight master
    /// refuses it until he got the paper clip (the item used here), then it can be taken once per day.
    /// </summary>
    [Room(RoomIds.Fracht)]
    [ScreenName("freight")]
    public sealed class FreightRoom : OrderCounterRoom
    {
        const string Lib = "room/fracht.gli";
        const int ClickMaster = 11, ClickGlue = 12;
        protected override string Library => Lib;
        protected override string Background => "FRACHT";
        protected override OfferBoard Board => OfferBoard.FreightOffice;
        protected override bool FreightBoard => true;
        protected override int DefaultDialogPartner => Talkers.Fracht;
        static readonly Vector2Int[] Pos = { V(103, 235), V(286, 12), V(268, 192), V(30, -61), V(26, 131), V(181, 57) };
        static readonly Vector2Int[] TextOff = { V(-7, 99), V(-7, 90), V(0, 84), V(-11, 87), V(-4, 69), V(2, 59) };
        static readonly int[] RopeDx = { -10, -6, -4, -4, -4, -4 };
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        protected override Vector2Int[] SlotPos => Pos;
        protected override Vector2Int TipPos => new Vector2Int(400, 125);
        protected override float FlySeconds => 1f;

        SmackerPerson _master, _fork1, _fork2;
        readonly bool[] _waitingDrop = new bool[6];

        Texture2D Crate(int slot) => Assets.Texture(Lib, "KISTE0" + (slot + 1));

        protected override RectInt SlotRect(int slot)
        {
            var t = Crate(slot);
            var p = Pos[slot];
            return new RectInt(p.x, p.y + 30, t.width, Mathf.Max(1, t.height - 30));
        }

        protected override void OnCreate()
        {
            _master = new SmackerPerson(Assets, App.Audio);
            int x = 485, y = 50;
            _master.Clip(0, "FM1a.smk", x, y, Mood.Idle, "A5:0 A3:1 E1:3", 1, 1, 0, 20);
            _master.Clip(1, "FM3.smk", x, y, Mood.Idle, "A5:0 A3:1 E1:3", 1, 1, 0, 20);
            _master.Clip(2, "FM2.smk", x, y, Mood.Idle, "A1:0", canCancel: false).OnFrame = f => { if (f == 5) DropTaken(); };
            _master.Clip(3, "FM4.smk", x, y, Mood.Idle, "E1:4 E1:7 A1:10", canCancel: false);
            string L = "A9:4 A2:5 A2:6 E1:7 E1:10";
            _master.Clip(4, "FM4SPaS1.smk", x, y, Mood.Listening, L);
            _master.Clip(5, "FM4SPaS2.smk", x, y, Mood.Listening, "A1:4 E1:7 E1:10");
            _master.Clip(6, "FM4SPaS3.smk", x, y, Mood.Listening, "A1:4 E1:7 E1:10");
            string T = "A5:7 A3:8 A2:9 A2:11 E1:4 E1:10";
            _master.Clip(7, "FMSPa1.smk", x, y, Mood.Talking, T);
            _master.Clip(8, "FMSPa2.smk", x, y, Mood.Talking, T);
            _master.Clip(9, "FMSPa3.smk", x, y, Mood.Talking, T);
            _master.Clip(10, "FM4back.smk", x, y, Mood.Idle, "A1:0", canCancel: false);
            _master.Clip(11, "FMSpa4.smk", x, y, Mood.Talking, T);

            _fork1 = new SmackerPerson(Assets, App.Audio);
            _fork1.Clip(0, "Fa1.smk", 60, 240, Mood.Idle, "A1:1");
            _fork1.Clip(1, "", 60, 240, Mood.Idle, "A1:0", 1, 1, 10, 10);
            _fork2 = new SmackerPerson(Assets, App.Audio);
            _fork2.Clip(0, "", 0, 190, Mood.Idle, "A1:1", 1, 1, 10, 10);
            _fork2.Clip(1, "Fa2.smk", 0, 190, Mood.Idle, "A1:0");
        }

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            SetBackgroundFx(0, "Pap3.raw", 25000);
        }

        protected override void OnLeaveRoom()
        {
            for (int i = 0; i < 6; i++) { _waitingDrop[i] = false; Fly[i] = -1; }
            base.OnLeaveRoom();
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Fracht ? _master : null;

        void DropTaken()
        {
            for (int i = 0; i < 6; i++)
                if (_waitingDrop[i]) { _waitingDrop[i] = false; Fly[i] = 0; App.Audio.Play("SOUND/Warning.raw"); }
        }

        protected override void OnFlyDone(int slot) => App.Audio.Play(Random.value < 0.5f ? "SOUND/Kiste1.raw" : "SOUND/Kiste2.raw");

        public override void Animate(float dt)
        {
            // Crates waiting for the buzzer do not move yet.
            for (int i = 0; i < 6; i++) if (_waitingDrop[i]) Fly[i] = -1;
            base.Animate(dt);
            _master.Update(dt);
            _fork1.Update(dt);
            _fork2.Update(dt);
        }

        /// <summary>The glue pot is still there (not taken today, the player does not carry the glue).</summary>
        bool GlueThere => W != null && W.Items.GlueState != 2 && !(App.Session?.Local.HasItem(ItemId.Glue) ?? true);

        bool Busy(int i) => _waitingDrop[i] || Fly[i] >= 0;
        protected override bool SlotBusy(int i) => Busy(i);

        protected override void Hotspots(InputState input)
        {
            Hot(465, 0, 639, 309, ClickMaster);
            Exit(620, 0, 639, 439);
            Exit(440, 357, 639, 430);
            CardHotspots();
            // The glue pot is tested first in the original, so it wins over the exit around it.
            if (GlueThere) Hot(567, 386, 630, 429, ClickGlue);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMaster) { StartDialog(Talkers.Fracht, DialogMedium.Air, 1000); return; }
            if (id == ClickGlue)
            {
                var s = App.Session;
                if (s.World.Items.GlueState == 0) StartDialog(Talkers.Fracht, DialogMedium.Air, 1010);
                else s.World.Execute(new PickUpItemCommand(s.Local.Index, ItemId.Glue));
                return;
            }
            if (id < 0 || id >= 6) return;
            if (!Take(id)) return;
            // The crate waits for the buzzer (frame 5 of the master's clip 2).
            Fly[id] = -1;
            _waitingDrop[id] = true;
            _master.Decision = 2;
        }

        protected override void DrawRoom(Screen2D s)
        {
            _fork2.Draw(s, RX);
            _fork1.Draw(s, RX);
            if (!GlueThere) s.Draw(Assets.Texture(Lib, "NOGLUE"), RX + 577, 396);
            if (W != null)
                for (int i = 5; i >= 0; i--)
                {
                    FreightContract f = Busy(i) ? FlyFreight[i] : SlotFilled(i) ? W.FreightOffice.Offers[i] : null;
                    if (f == null) continue;
                    var t = Crate(i);
                    var p = Pos[i];
                    float fall = Fly[i] >= 0 ? Fly[i] * Fly[i] * 600 : 0;
                    int y = p.y + (int)fall;
                    if (fall <= 0)
                    {
                        // Two rope segments above the crate.
                        var rope = Assets.Texture(Lib, "SEIL0" + (i / 2 + 1));
                        int rx = RX + p.x + t.width / 2 + RopeDx[i];
                        for (int ry = p.y - rope.height; ry > -rope.height; ry -= rope.height) s.Draw(rope, rx, ry);
                    }
                    s.Draw(t, RX + p.x, y);
                    DrawCardText(s, RX + p.x + TextOff[i].x, y + TextOff[i].y - 10, t.width, f.From, f.To);
                    if (Fly[i] >= 0 || _waitingDrop[i]) s.Draw(Assets.Texture(Lib, "STEMPEL"), RX + p.x + t.width / 2 - 13, y + TextOff[i].y + 10);
                }
            s.Draw(Assets.Texture(Lib, "MASK"), RX + 333, 0);
            _master.Draw(s, RX);
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (Hover < 0 || IsDialogOpen || Menus.IsOpen || !SlotFilled(Hover) || Busy(Hover)) return;
            var o = W.FreightOffice.Offers[Hover];
            DrawOrderTip(s, o.From, o.To, 0, o.Tons, o.Date, o.LastDate, o.Premium, o.Penalty, true);
        }
    }

    /// <summary>Freight master (rooms-and-ui-3b §15): only partner lines; any click ends the dialog.</summary>
    [DialogScript(Talkers.Fracht)]
    public sealed class FreightDialog : DialogTree
    {
        public FreightDialog() : base("Frac")
        {
            Open(d => d.Say(d.Par1 >= 1000 ? d.Par1 : 1000));
        }
    }
}
