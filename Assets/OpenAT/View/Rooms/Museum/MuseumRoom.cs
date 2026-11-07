using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Staff;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Makler;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Museum
{
    /// <summary>
    /// Museum, used planes (rooms-and-ui-7 §2): three used offers as miniatures in the hall (hover shows
    /// the plane card and the plane advisor's comment, click asks the keeper), the keeper and the flying
    /// skeleton. Buying and selling go through the dialog (<see cref="BuyUsedPlaneCommand"/>,
    /// <see cref="SellPlaneCommand"/> via MENU_SELLPLANE).
    /// </summary>
    [Room(RoomIds.Museum)]
    [ScreenName("museum")]
    public sealed class MuseumRoom : RoomScreen
    {
        const string Lib = "room/museum.gli";
        protected override string Library => Lib;
        protected override string Background => "MUSEUM";
        protected override int DefaultDialogPartner => Talkers.Museum;
        const int ClickPlane = 10, ClickMan = 11;
        static readonly Vector2Int[] MiniPos = { new Vector2Int(140, 281), new Vector2Int(206, 293), new Vector2Int(291, 276) };

        SmackerPerson _man, _skeleton;
        int _trigSkeleton = -1, _trigMan = -1, _trigEmergency = -1;
        FrameAnimation _rotor;
        int _hoverSlot = -1, _clickSlot = -1, _commented = -1;
        World W => App.Session?.World;

        protected override void OnCreate()
        {
            int x = 420, y = 250;
            _man = new SmackerPerson(Assets, App.Audio);
            // Keeper and the flying skeleton play a little scene through three shared trigger values
            // (rooms-and-ui-7 §2): while the keeper listens he may look up (7/8) or catch the skeleton
            // (10..13, MU_Hit at frame 3 makes it say "ouch"); the skeleton flies by (2), gets caught (6/7)
            // or grins (3). Turning to the visitor resets the emergency value, turning back hides it (0).
            _man.Clip(0, "MU_putzA.smk", x, y, Mood.Idle, "A9:0 A9:1 E1:2", canCancel: false, sound: "mu_putza.raw");
            _man.Clip(1, "MU_putzB.smk", x, y, Mood.Idle, "A9:0 A9:1 E1:2", canCancel: false, sound: "mu_putzb.raw");
            _man.Clip(2, "MU_turn.smk", x, y, Mood.Idle, "A1:5 E1:3 E1:5 E1:6", canCancel: false).OnStart = () => _trigEmergency = -1;
            _man.Clip(3, "MU_redeA.smk", x, y, Mood.Talking, "A8:3 A8:4 E1:5 E1:6");
            _man.Clip(4, "MU_redeB.smk", x, y, Mood.Talking, "A8:3 A8:4 E1:5 E1:6");
            _man.Clip(5, "MU_RedeW.smk", x, y, Mood.Listening, "A9:5 A1:9 E1:3 E1:5 E1:6", 1, 1, 5, 5).OnEnd = () => _trigMan = -1;
            _man.Clip(6, "MU_turnZ.smk", x, y, Mood.Idle, "A9:0", canCancel: false).OnStart = () => _trigEmergency = 0;
            var c7 = _man.Clip(7, "MU_Guck.smk", x, y, Mood.Listening, "A1:8", canCancel: false); c7.OnStart = () => _trigMan = -1;
            _man.Clip(8, "MU_GuckZ.smk", x, y, Mood.Listening, "A1:5", canCancel: false).OnEnd = () => _trigSkeleton = 3;
            var c9 = _man.Clip(9, "MU_RedeW.smk", x, y, Mood.Listening, "A1:5 E1:3 E1:5 E1:6", 1, 1, 370, 370);
            c9.OnStart = () => _trigSkeleton = 1; c9.Decide = () => _trigMan;
            var c10 = _man.Clip(10, "", x, y, Mood.Listening, "A1:11"); c10.OnEnd = () => _trigMan = -1;
            var c11 = _man.Clip(11, "MU_Guck.smk", x, y, Mood.Listening, "A1:12", 1, 1, 10, 10, canCancel: false); c11.OnStart = () => _trigMan = -1;
            _man.Clip(12, "MU_GuckZ.smk", x, y, Mood.Listening, "A1:13", canCancel: false);
            var c13 = _man.Clip(13, "MU_Hit.smk", x, y, Mood.Listening, "A1:5"); c13.OnFrame = f => { if (f == 3) _trigSkeleton = 4; }; c13.Decide = () => _trigMan;

            _skeleton = new SmackerPerson(Assets, App.Audio);
            _skeleton.Clip(0, "flyingmw.smk", 420, 180, Mood.Idle, "A9:0", 1, 1, 60, 60).Decide = () => _trigSkeleton;
            _skeleton.Clip(1, "", 420, 180, Mood.Idle, "A9:2 A9:6", canCancel: false).OnStart = () => _trigSkeleton = -1;
            var s2 = _skeleton.Clip(2, "FlyingM.smk", 420, 180, Mood.Idle, "A9:0", 1, 3, canCancel: false, sound: "flyingm.raw");
            s2.OnEnd = () => _trigMan = 7; s2.Decide = () => _trigEmergency;
            var s3 = _skeleton.Clip(3, "flygrinz.smk", 420, 180, Mood.Idle, "A9:0", canCancel: false, sound: "flygrinz.raw");
            s3.OnStart = () => _trigSkeleton = -1; s3.Decide = () => _trigEmergency;
            _skeleton.Clip(4, "", 420, 180, Mood.Idle, "A9:5", canCancel: false).OnStart = () => _trigSkeleton = -1;
            var s5 = _skeleton.Clip(5, "Fly_auah.smk", 420, 180, Mood.Idle, "A9:0", canCancel: false);
            s5.OnStart = () => _trigSkeleton = -1; s5.Decide = () => _trigEmergency;
            var s6 = _skeleton.Clip(6, "FlyingM.smk", 420, 180, Mood.Idle, "A9:7", 1, 2, canCancel: false, sound: "flyingm.raw");
            s6.OnEnd = () => _trigMan = 10; s6.Decide = () => _trigEmergency;
            _skeleton.Clip(7, "FlyingM.smk", 420, 180, Mood.Idle, "A9:0", 2, 2, canCancel: false, sound: "flyingm.raw").Decide = () => _trigEmergency;
            _rotor = new FrameAnimation(Lib, "ROTOR", 2, AnimMode.Repeat, 0, 1);
        }

        protected override void OnRoomEnter()
        {
            var s = App.Session;
            if (s != null) s.World.Execute(new EnterMuseumCommand(s.World.LocalPlayer));
            PlayAmbientLoop("museum.raw");   // looped continuously, like the original
        }

        protected override void OnLeaveRoom()
        {
            var p = App.Session?.Local;
            if (p != null && p.Extensions.ContainsKey("openat.museum.met")) p.Extensions["openat.museum.met2"] = true;
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Museum ? _man : null;

        public override void Animate(float dt)
        {
            _man.Update(dt);
            _skeleton.Update(dt);
            _rotor.Update(dt, App.Audio);
        }

        protected override void Hotspots(InputState input)
        {
            _hoverSlot = -1;
            Hot(408, 193, 590, 430, ClickMan);
            int mx = input.Mouse.x - RX, my = input.Mouse.y;
            if (W != null && mx >= 125 && mx <= 364 && my >= 226 && my <= 330)
            {
                int slot = Mathf.Clamp((mx - 125) * 3 / 240, 0, 2);
                if (W.UsedPlanes[slot] != null && Hot(125, 226, 374, 330, ClickPlane)) { _hoverSlot = slot; _clickSlot = slot; }
            }
            Exit(0, 0, 233, 227);
            Exit(533, 312, 605, 352);
            Comment();
        }

        void Comment()
        {
            if (_hoverSlot == _commented) return;
            _commented = _hoverSlot;
            if (_hoverSlot < 0) { App.Advisors.NoComments(); return; }
            var p = App.Session.Local;
            if (!p.HasAdvisor(AdvisorType.PlaneAnalyst)) return;
            var pl = W.UsedPlanes[_hoverSlot];
            int id = pl.BuildYear < 1960 ? 4001 : pl.Condition < 20 ? 4000 : pl.Condition < 40 ? 4002 : pl.Condition < 60 ? 4003 : pl.Condition < 80 ? 4004 : 4005;
            var raw = Assets.StdText.Get("Advi", id);
            if (raw != null) App.Advisors.Queue(AdvisorKind.Plane, raw, AdvisorUrgency.Comment);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMan) StartDialog(Talkers.Museum);
            else if (id == ClickPlane && _clickSlot >= 0 && W.UsedPlanes[_clickSlot] != null)
            {
                App.Advisors.NoComments();
                StartDialog(Talkers.Museum, DialogMedium.Air, _clickSlot);
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            _rotor.Draw(s, Assets, ox + 82, 304);
            if (W != null)
                for (int i = 0; i < 3; i++)
                {
                    var pl = W.UsedPlanes[i];
                    if (pl == null) continue;
                    int size = Mathf.Clamp((pl.Seats - 100) * 4 / 400, 0, 3);
                    s.Draw(Assets.Texture(Lib, "MINI0" + (i + 1) + "0" + (size + 1)), ox + MiniPos[i].x, MiniPos[i].y);
                }
            _skeleton.Draw(s, ox);
            _man.Draw(s, ox);
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (_hoverSlot < 0 || IsDialogOpen || Menus.IsOpen || W == null) return;
            var pl = W.UsedPlanes[_hoverSlot];
            if (pl == null) return;
            var bg = Assets.Texture("gli/tippl.gli", "BLOC1");
            int x = RX + 245 + (_hoverSlot - 1) * 100 - bg.width / 2, y = 50;
            s.Draw(bg, x, y);
            PlaneTips.DrawPlane(s, Assets, App.Ui.Kit, x + 32, y + 3, pl, pl.MarketValue, 170, contentsDy: 17);
        }
    }

    /// <summary>
    /// MENU_SELLPLANE (rooms-and-ui-2 §4.2): own planes as in the original's plane table: name and condition
    /// columns (Schd 1001/1002); a plane short of crew or with a problem is red; red rows first, then by name.
    /// The hover card shows the plane's value.
    /// </summary>
    public sealed class SellPlaneMenu : NotepadListMenu
    {
        public SellPlaneMenu(World w, Player p, UI.UiKit kit, Boot.GameAssets assets) : base(MenuIds.SellPlane)
        {
            Title0 = assets.StdText.Get("Schd", 1001);
            Title1 = assets.StdText.Get("Schd", 1002);
            var list = new System.Collections.Generic.List<Plane>(p.Planes.Items);
            bool RedOf(Plane pl) => pl.CrewPilots < pl.Pilots || pl.CrewAttendants < pl.Attendants || pl.Problem != 0;
            list.Sort((a, b) => RedOf(a) != RedOf(b) ? (RedOf(a) ? -1 : 1) : string.CompareOrdinal(a.Name ?? "", b.Name ?? ""));
            foreach (var pl in list)
                Rows.Add(new Row { Name = pl.Name, Value = UiKit.Unit(UiKit.UnitPercent, pl.Condition), Red = RedOf(pl), Tag = pl });
            DrawTip = (s, x, y, i) => PlaneTips.DrawPlane(s, App.Assets, Kit, x + 8, y + 6, (Plane)Rows[i].Tag, null, 166);
        }

        public override void Update(InputState input, Vector2Int l)
        {
            // Picking a row continues the dialog itself: no re-entry line in between.
            if (input.LeftDown && l.x >= 216 && l.x <= 387 && l.y >= 25 && l.y <= 212) ReEntry = -1;
            base.Update(input, l);
        }
    }

    /// <summary>Museum keeper (rooms-and-ui-3b §7).</summary>
    [DialogScript(Talkers.Museum)]
    public sealed class MuseumDialog : DialogTree
    {
        public MuseumDialog() : base("Muse")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 >= 0 && d.Par1 <= 2)
                {
                    d.Par3 = d.Par1;
                    var used = d.World.UsedPlanes[d.Par1];
                    d.Say(500, UiKit.Thousands(used?.MarketValue ?? 0));   // the offer line names the price
                    return;
                }
                if (d.Par1 >= 100 && d.Par1 <= 103)
                {
                    var pl = p.Planes.Find(d.Par2);
                    if (pl == null) { d.End(); return; }
                    d.Say(710 + d.Par1 - 100);
                    d.ShowNumbers(9990710 + d.Par1 - 100, UiKit.Thousands(PlaneMarket.SalePrice(pl)));
                    return;
                }
                if (d.Medium == DialogMedium.Phone) d.Say(101);
                else if (p.Extensions.ContainsKey("openat.museum.met")) d.Say(102);
                else d.Say(100);
                p.Extensions["openat.museum.met"] = true;
            });
            On(new[] { 100, 101, 102 }, d =>
            {
                if (d.Player.Extensions.ContainsKey("openat.museum.met2")) d.Choices(211, 213); else d.Choices(201, 203);
            });
            On(new[] { 201, 211 }, d => d.Say((d.Medium == DialogMedium.Phone ? 401 : 400) + (d.Player.Extensions.ContainsKey("openat.museum.met2") ? 2 : 0)));
            On(new[] { 202, 212 }, d => d.Say(d.Player.Extensions.ContainsKey("openat.museum.met2") ? 701 : 700));
            On(new[] { 700, 701 }, d =>
            {
                if (d.Medium == DialogMedium.Phone)
                {
                    if (d.Player.Extensions.ContainsKey("openat.museum.met2")) d.Choices(211, 213); else d.Choices(201, 203);
                    return;
                }
                var menu = new SellPlaneMenu(d.World, d.Player, d.Stage.App.Ui.Kit, d.Assets);
                menu.Picked = (i, row) =>
                {
                    var pl = (Plane)row.Tag;
                    int code = 100 + (pl.Sponsored ? 2 : 0) + (FlightPlanner.CanBeSold(d.World, pl) ? 0 : 1);
                    d.Stage.App.Rooms.Current?.StartDialog(Talkers.Museum, DialogMedium.Air, code, pl.Id);
                };
                d.OpenMenu(menu, 699, new Vector2Int(320, 220), 0.1f);
            });
            On(500, d => d.Choices(600, 601));
            On(600, d =>
            {
                if (!d.World.Execute(new BuyUsedPlaneCommand(d.Player.Index, d.Par3)).Ok) { d.Say(6000); return; }
                d.End();
            });
            On(new[] { 710, 712 }, d => d.Choices(720, 721));
            On(720, d =>
            {
                d.World.Execute(new SellPlaneCommand(d.Player.Index, d.Par2));
                d.End();
            });
        }
    }
}
