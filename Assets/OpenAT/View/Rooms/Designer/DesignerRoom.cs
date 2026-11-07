using System.Collections.Generic;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Designer
{
    /// <summary>
    /// Designer office (rooms-and-ui-7 §3): Stan the designer (dialog: explanations, buying planes built
    /// from the player's designs via MENU_BUYXPLANE into <see cref="BuyDesignedPlaneCommand"/>), the bat,
    /// the worker with the swallow, the blender and the leg. The drawing board leads into the aircraft
    /// constructor (<see cref="EditorRoom"/>, room 124).
    /// </summary>
    [Room(RoomIds.Designer)]
    [ScreenName("designer")]
    public sealed class DesignerRoom : RoomScreen
    {
        const string Lib = "room/designer.gli";
        protected override string Library => Lib;
        protected override string Background => "DESIGNER";
        protected override int DefaultDialogPartner => Talkers.Designer;
        const int ClickStan = 10, ClickBoard = 12;

        SmackerPerson _stan, _bat, _worker, _blender, _leg;
        AudioSource _loop;

        protected override void OnCreate()
        {
            int x = 198, y = 18;
            _stan = new SmackerPerson(Assets, App.Audio);
            _stan.Clip(0, "DeManW.smk", x, y, Mood.Idle, "A9:0 E1:1 E1:2 A1:3 A1:4 A1:5", 1, 1, 10, 10);
            _stan.Clip(1, "DeManW.smk", x, y, Mood.Listening, "A9:1 E5:2 E5:0", 1, 1, 50, 50);
            _stan.Clip(2, "DeManS.smk", x, y, Mood.Talking, "A9:2 E1:1 E1:0");
            _stan.Clip(3, "DeManSh.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _stan.Clip(4, "DeManFl.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _stan.Clip(5, "DeManCo.smk", x, y, Mood.Idle, "A9:0", canCancel: false);

            _bat = new SmackerPerson(Assets, App.Audio);
            _bat.Clip(0, "DeBatW.smk", 0, 13, Mood.Idle, "A9:0 A1:1", 1, 1, 50, 190);
            _bat.Clip(1, "DeBat.smk", 0, 13, Mood.Idle, "A9:0");

            _worker = new SmackerPerson(Assets, App.Audio);
            _worker.Clip(0, "DeWorker.smk", 330, 262, Mood.Idle, "A9:0 A1:1");
            _worker.Clip(1, "DePlane.smk", 330, 262, Mood.Idle, "A9:0");

            // OQ-2: the original registers the blender clip under id 0; it is reachable as successor 1 here.
            _blender = new SmackerPerson(Assets, App.Audio);
            _blender.Clip(0, "DeBatW.smk", 337, 92, Mood.Idle, "A9:0 A1:1", 1, 1, 10, 90);
            _blender.Clip(1, "DeBlnder.smk", 337, 92, Mood.Idle, "A9:0", 1, 1, 10, 90);

            _leg = new SmackerPerson(Assets, App.Audio);
            _leg.Clip(0, "DeLeg.smk", 435, 222, Mood.Idle, "A9:1", 1, 1, 10, 10);
            _leg.Clip(1, "DeLegW.smk", 435, 222, Mood.Idle, "A9:1 A2:0", 1, 1, 10, 90);
        }

        protected override void OnRoomEnter()
        {
            if (PlayerPrefs.GetFloat(GameAudio.PrefAmbience, 1f) > 0) _loop = App.Audio.PlayLoop("SOUND/design.raw", 0.6f);
        }

        protected override void OnLeaveRoom()
        {
            if (_loop != null) { App.Audio.StopLoop(_loop); _loop = null; }
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Designer ? _stan : null;

        public override void Animate(float dt)
        {
            _stan.Update(dt);
            _bat.Update(dt);
            _worker.Update(dt);
            _blender.Update(dt);
            _leg.Update(dt);
        }

        protected override void Hotspots(InputState input)
        {
            // Priority order of the original (first match wins): exit, Stan, drawing board; Hot(): last wins.
            Hot(43, 112, 250, 307, ClickBoard);
            Hot(200, 26, 354, 341, ClickStan);
            Exit(296, 240, 361, 337);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickStan) StartDialog(Talkers.Designer, DialogMedium.Air, 1000);
            else if (id == ClickBoard) App.Rooms.Enter(RoomIds.Editor);
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            _bat.Draw(s, ox);
            _worker.Draw(s, ox);
            _leg.Draw(s, ox);
            _blender.Draw(s, ox);
            _stan.Draw(s, ox);
        }
    }

    /// <summary>Designer (rooms-and-ui-3b §17, group "Desi").</summary>
    [DialogScript(Talkers.Designer)]
    public sealed class DesignerDialog : DialogTree
    {
        static readonly int[] Quantities = PurchaseQuantities.Allowed;

        public DesignerDialog() : base("Desi")
        {
            Open(d =>
            {
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 == 6000) { Offer(d); return; }
                d.Say(1000);
            });
            On(1000, d => d.Choices(2000, 2002));
            On(3040, d => d.Choices(2001, 2002));
            On(2000, d => d.Say(3000));
            On(3000, d => d.PlayerSays(3010));
            On(3010, d => d.Say(3020));
            On(3020, d => d.PlayerSays(3030));
            On(3030, d => d.Say(3040));
            On(2001, d => d.Say(4000));
            On(4000, d => d.Say(DesignFiles.Library(d.Stage.App).Buildable().Count == 0 ? 4002 : 4001));
            On(4001, d =>
            {
                var menu = new BuyXPlaneMenu(d.Stage.App);
                menu.Picked = (i, row) => d.Stage.App.Rooms.Current?.StartDialog(Talkers.Designer, DialogMedium.Air, 6000, i);
                d.OpenMenu(menu, -1);
            });
            On(2002, d => d.Say(5000));
            On(6011, 6015, (d, id) =>
            {
                var design = Picked(d);
                int q = Quantities[id - 6011];
                if (design == null || !d.World.Execute(new BuyDesignedPlaneCommand(d.Player.Index, design, q)).Ok) { d.Say(6020); return; }
                d.Say(6030);
            });
        }

        /// <summary>Re-reads the buildable list like the original and takes the picked row (Par2).</summary>
        static OpenAT.Sim.Data.PlaneDesign Picked(DialogSession d)
        {
            var l = DesignFiles.Library(d.Stage.App).Buildable();
            return d.Par2 >= 0 && d.Par2 < l.Count ? l[d.Par2].design : null;
        }

        /// <summary>6000: quantity choice 1/2/3/5/10 limited by the debt limit, or 6020 when not even one is affordable.</summary>
        static void Offer(DialogSession d)
        {
            var design = Picked(d);
            if (design == null) { d.End(); return; }
            long cost = design.Stats(d.World.DesignCatalog).Cost;
            if (!d.Player.CanAfford(cost)) { d.Say(6020); return; }
            var args = new List<object> { "" };
            int n = 0;
            foreach (var q in Quantities)
            {
                if (!d.Player.CanAfford(cost * q)) break;
                n++;
            }
            foreach (var q in Quantities) args.Add(UiKit.Thousands(cost * q));
            d.Choices(6010, 6010 + n, ChoiceArgs.OnePerOption, args.ToArray());
        }
    }
}
