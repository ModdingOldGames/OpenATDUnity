using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Data;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Items;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Makler
{
    /// <summary>
    /// Plane broker (rooms-and-ui-7 §1): the broker at his desk (turns the light knob, presses the
    /// fountain button), fish tank, neon lights, fountain, glass door. Buying goes through the dialog and
    /// MENU_BUYPLANE (<see cref="BuyPlaneMenu"/>) into <see cref="BuyPlaneCommand"/>. Closes at 16:00.
    /// </summary>
    [Room(RoomIds.Makler)]
    [ScreenName("makler")]
    public sealed class MaklerRoom : RoomScreen
    {
        const string Lib = "room/makler.gli";
        protected override string Library => Lib;
        protected override string Background => "MAKLER";
        protected override int DefaultDialogPartner => Talkers.Makler;
        const int ClickBroker = 10, ClickFlap = 20;

        SmackerPerson _man;
        FrameAnimation _bubbles, _fish, _lightL, _lightR, _flap;
        bool _light = true, _water;
        int _fountain;
        float _fountainT;
        AudioSource _spring;

        protected override void OnCreate()
        {
            int x = 470, y = 218;
            _man = new SmackerPerson(Assets, App.Audio);
            _man.Clip(0, "M_Wait.smk", x, y, Mood.Idle, "A9:0 A2:1 A2:2 E1:3", 1, 1, 15, 15);
            _man.Clip(1, "M_Dreh.smk", x, y, Mood.Idle, "A9:0 E1:3", canCancel: false, sound: "m_dreh.raw").OnFrame = f => { if (f == 6) _light = !_light; };
            _man.Clip(2, "M_Druc.smk", x, y, Mood.Idle, "A9:0 E1:3", canCancel: false, sound: "m_druc.raw").OnFrame = f => { if (f == 4) _water = !_water; };
            _man.Clip(3, "M_Turn.smk", x, y, Mood.Idle, "A1:4 E1:5", canCancel: false);
            _man.Clip(4, "M_Rede.smk", x, y, Mood.Talking, "A9:4 A5:7 E1:5 E1:6", 2, 2);
            _man.Clip(5, "M_List.smk", x, y, Mood.Listening, "A9:5 A9:8 E1:4 E1:6", 1, 1, 10, 20);
            _man.Clip(6, "M_Back.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _man.Clip(7, "M_RedeB.smk", x, y, Mood.Talking, "A9:4 E1:5 E1:6");
            _man.Clip(8, "M_ListK.smk", x, y, Mood.Listening, "A9:5", 1, 1, 1, 1, canCancel: false);

            _bubbles = new FrameAnimation(Lib, "BUL", 7, AnimMode.Repeat, 0, 2, first: 0);
            _fish = new FrameAnimation(Lib, "FISH", 35, AnimMode.Repeat, 400, 2, 300, first: 0, showFirst: false);
            _lightL = new FrameAnimation(Lib, "LICHTL", 5, AnimMode.Repeat, 0, 5, first: 0);
            _lightR = new FrameAnimation(Lib, "LICHTR", 5, AnimMode.Repeat, 0, 5, first: 0);
            // The wall flap opens on half of the visits; the bra hangs behind it while the chain is still open (§1).
            if (Random.value < 0.5f) _flap = new FrameAnimation(Lib, BraBehindFlap ? "KLAPPE" : "KLAPOH", 4, AnimMode.Once, 500, 5, sound: "Klappe.raw", first: 0);
        }

        protected override void OnRoomEnter() { }

        /// <summary>The bra can be taken: not carried, no horseshoe, the drinker's trust not yet earned, not the tutorial.</summary>
        bool BraBehindFlap
        {
            get
            {
                var s = App.Session;
                if (s == null) return false;
                var p = s.Local;
                return !p.HasItem(ItemId.Bra) && !p.HasItem(ItemId.Horseshoe) && !p.TrinkerTrust && s.World.Difficulty != Mission.Tutorial;
            }
        }

        protected override void OnLeaveRoom()
        {
            if (_spring != null) App.Audio.StopLoop(_spring);
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Makler ? _man : null;

        public override void Animate(float dt)
        {
            _man.Update(dt);
            _bubbles.Update(dt, App.Audio);
            _fish.Update(dt, App.Audio);
            if (_light) { _lightL.Update(dt, App.Audio); _lightR.Update(dt, App.Audio); }
            _flap?.Update(dt, App.Audio);
            // Fountain: one step per 100 ms (§1).
            _fountainT += dt;
            int guard = 0;
            while (_fountainT >= 0.1f && guard++ < 20)
            {
                _fountainT -= 0.1f;
                if (_water) _fountain = _fountain < 2 ? _fountain + 1 : _fountain >= 5 ? 3 : _fountain + 1;
                else if (_fountain > 0) _fountain = _fountain >= 3 && _fountain <= 5 ? 6 : _fountain >= 8 ? 0 : _fountain + 1;
            }
            if (_water && _spring == null && !IsPhonePane) _spring = App.Audio.PlayLoop("SOUND/Spring.raw", 0.5f);
            if (!_water && _spring != null) { App.Audio.StopLoop(_spring); _spring = null; }
            // Closing time 16:00: the player is sent out.
            var c = Clock;
            if (!IsPhonePane && c != null && c.Hour >= 16 && !IsDialogOpen && !Menus.IsOpen && App.Rooms.Current == this) Leave();
        }

        protected override void Hotspots(InputState input)
        {
            if (_flap != null && _flap.Frame == 3 && BraBehindFlap) Hot(0, 15, 38, 79, ClickFlap);
            Hot(335, 199, 639, 439, ClickBroker);
            Exit(55, 106, 196, 405);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickBroker) StartDialog(Talkers.Makler);
            else if (id == ClickFlap)
            {
                var s = App.Session;
                // The flap shows the empty variant from now on, without restarting its animation.
                if (s.World.Execute(new PickUpItemCommand(s.Local.Index, ItemId.Bra)).Ok) _flap.Prefix = "KLAPOH";
            }
        }

        static readonly string[] FountainFrames = { "SPRITZW0", "SPRITZA0", "SPRITZA1", "SPRITZ00", "SPRITZ01", "SPRITZ02", "SPRITZE0", "SPRITZE1", "SPRITZE2" };

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            _flap?.Draw(s, Assets, ox, 0);
            _bubbles.Draw(s, Assets, ox + 394, 400);
            _fish.Draw(s, Assets, ox + 394, 400);
            if (_light) { _lightL.Draw(s, Assets, ox + 88, 400); _lightR.Draw(s, Assets, ox + 193, 375); }
            s.Draw(Assets.Texture(Lib, FountainFrames[_fountain]), ox + 226, 253);
            s.Draw(Assets.Texture(Lib, "OPAQUE"), ox, 149);
            var glass = new Color32(255, 255, 255, 110);
            s.Draw(Assets.Texture(Lib, "TRANS1"), ox + 325, 200, glass);
            s.Draw(Assets.Texture(Lib, "TRANS2"), ox + 225, 184, glass);
            s.Draw(Assets.Texture(Lib, "TRANS3"), ox + 102, 172, glass);
            s.Draw(Assets.Texture(Lib, "TRANS4"), ox, 157, glass);
            _man.Draw(s, ox);
        }
    }

    /// <summary>MENU_BUYPLANE (rooms-and-ui-2 §4.3): the catalogue; hovering shows the type card on the left page.</summary>
    public sealed class BuyPlaneMenu : NotepadListMenu
    {
        public readonly List<PlaneType> Types;
        public BuyPlaneMenu(World w, GameAssets assets) : base(MenuIds.BuyPlane)
        {
            Types = PlaneMarket.BrokerList(w);
            // The original's type table: one titled column (Schd 1601) with the type name only; the second
            // column is blank (prices are on the hover card). Verified on the running original.
            Title0 = assets.StdText.Get("Schd", 1601);
            Title1 = " ";
            foreach (var t in Types) Rows.Add(new Row { Name = t.Name, Value = "", Tag = t });
            DrawTip = (s, x, y, i) => PlaneTips.DrawType(s, App.Assets, Kit, x + 8, y + 6, Types[i], 166);
        }
    }

    /// <summary>Plane broker (rooms-and-ui-3b §6).</summary>
    [DialogScript(Talkers.Makler)]
    public sealed class MaklerDialog : DialogTree
    {
        public static readonly int[] Quantities = PurchaseQuantities.Allowed;

        static bool Met(Player p) => p.Extensions.ContainsKey("openat.makler.met");

        public MaklerDialog() : base("Makl")
        {
            Open(d =>
            {
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 >= 130 && d.Par1 <= 133)
                {
                    var t = TypeOf(d);
                    if (t == null || !d.Player.CanAfford(t.Price)) d.Say(6000); else d.Say(d.Par1);
                    return;
                }
                var p = d.Player;
                if (d.Medium == DialogMedium.Phone) d.Say(82);
                else if (d.TalkedToA) d.Say(83);
                else if (Met(p)) d.Say(81);
                else d.Say(80);
                d.TalkedToA = true;
                p.Extensions["openat.makler.met"] = true;
            });
            On(new[] { 80, 81, 83, 110, 150, 170 }, d => { if (d.Medium == DialogMedium.Phone) d.End(); else d.Choices(100, 103); });
            On(82, d => d.Choices(1100, 1103));
            On(100, d => d.Say(d.Medium == DialogMedium.Phone ? 121 : 120));
            On(120, d =>
            {
                var menu = new BuyPlaneMenu(d.World, d.Assets);
                menu.Picked = (i, row) =>
                {
                    d.Stage.App.Rooms.Current?.StartDialog(Talkers.Makler, DialogMedium.Air, 130 + Random.Range(0, 4), i);
                };
                d.OpenMenu(menu, -1);
            });
            On(1100, d => d.Say(1200));
            On(new[] { 101, 1101 }, d => d.Say(110));
            On(new[] { 102, 1102 }, d => d.Say(170));
            On(130, 133, (d, id) =>
            {
                var t = TypeOf(d);
                if (t == null || !d.Player.CanAfford(t.Price)) { d.Say(6000); return; }
                var args = new List<object> { "" };
                int n = 0;
                foreach (var q in Quantities)
                {
                    if (!d.Player.CanAfford((long)t.Price * q)) break;
                    args.Add(UiKit.Thousands((long)t.Price * q));
                    n++;
                }
                d.Choices(140, 140 + n, ChoiceArgs.OnePerOption, args.ToArray());
            });
            On(140, d => d.Say(160));
            On(141, 145, (d, id) =>
            {
                var t = TypeOf(d);
                int q = Quantities[id - 141];
                if (t == null || !d.World.Execute(new BuyPlaneCommand(d.Player.Index, t.Id, q)).Ok) { d.Say(6000); return; }
                d.Say(150);
            });
        }

        static PlaneType TypeOf(DialogSession d)
        {
            var l = PlaneMarket.BrokerList(d.World);
            return d.Par2 >= 0 && d.Par2 < l.Count ? l[d.Par2] : null;
        }

    }
}
