using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Makler;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Werkstatt
{
    /// <summary>
    /// Workshop (rooms-and-ui-7 §4): mechanic Jack (smokes), the plane in the hangar door, welding,
    /// flames, light, the oil can (given once the mechanic trusts the player), the slime on the floor (the stink
    /// perfume needs it) with its bubbles and the cigarette gag. Maintenance level, repair targets, costs and breakdown lists go through
    /// the mechanic's dialog (<see cref="SetMechModeCommand"/>, <see cref="SetRepairTargetCommand"/>).
    /// </summary>
    [Room(RoomIds.Werkstatt)]
    [ScreenName("werkstatt")]
    public sealed class WerkstattRoom : RoomScreen
    {
        const string Lib = "room/werkstat.gli";
        protected override string Library => Lib;
        protected override string Background => "WERKSTAT";
        protected override int DefaultDialogPartner => Talkers.Mechaniker;
        const int ClickMech = 10, ClickOil = 20;

        SmackerPerson _man, _plane, _slime, _bomb;
        readonly SmackerPerson[] _bubbles = new SmackerPerson[5];
        static readonly Vector2Int[] BubblePos = { new Vector2Int(93, 441), new Vector2Int(111, 418), new Vector2Int(137, 430), new Vector2Int(54, 427), new Vector2Int(14, 427) };
        FrameAnimation _saw, _flame, _light;
        /// <summary>The mechanic lit a cigarette (frame 18 of his smoking clip): the bomb gag goes off, but only on a slimed floor.</summary>
        bool _lit;
        bool Guarded => App.Session != null && (App.Session.Local.SecurityFlags & (1 << 7)) != 0;
        bool Slimed => App.Session != null && App.Session.World.Items.Slimed != -1;
        bool HasOil => App.Session?.Local.HasItem(ItemId.Oil) ?? true;

        protected override void OnCreate()
        {
            _man = new SmackerPerson(Assets, App.Audio);
            int x = 190, y = 209;
            _man.Clip(0, "WK_Wait.smk", x, y, Mood.Idle, "A9:0 A1:4 E1:1", 1, 1, 10, 10);
            _man.Clip(1, "WK_Turn.smk", x, y, Mood.Idle, "E1:2 E1:3", canCancel: false);
            _man.Clip(2, "WK_Rede.smk", x, y, Mood.Talking, "A9:2 E1:3 E1:7");
            _man.Clip(3, "WK_RedeW.smk", x, y, Mood.Listening, "A9:3 E1:2 E1:7 A3:8", 1, 1, 10, 10);
            _man.Clip(4, "WK_Tuch.smk", x, y, Mood.Idle, "A9:5", canCancel: false);
            _man.Clip(5, "WK_Putz.smk", x, y, Mood.Idle, "A9:6", 1, 3, canCancel: false);
            _man.Clip(6, "WK_TuchZ.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _man.Clip(7, "WK_TurnZ.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _man.Clip(8, "WK_Rauch.smk", 173, y, Mood.Listening, "A9:3 E1:2 E1:7").OnFrame = f => { if (f == 18) _lit = true; };

            _plane = new SmackerPerson(Assets, App.Audio);
            if (Guarded) _plane.Clip(0, "spike.smk", 535, 72, Mood.Idle, "A1:0");
            else
            {
                _plane.Clip(0, "WerStand.smk", 535, 72, Mood.Idle, "A1:1", 1, 1, 100, 210);
                _plane.Clip(1, "WerL.smk", 535, 72, Mood.Idle, "A1:2");
                _plane.Clip(2, "WerStand.smk", 535, 72, Mood.Idle, "A1:3", 1, 1, 100, 210);
                _plane.Clip(3, "WerR.smk", 535, 72, Mood.Idle, "A1:0");
            }
            // Slime (rooms-and-ui-7 §4): clean floor → on 1 visit in 10 the spill can happen (it then stays until the
            // next 6th day); already slimed → the puddle loops. The spill's end marks the floor slimed in the sim.
            _slime = new SmackerPerson(Assets, App.Audio);
            if (!Slimed)
            {
                if (Random.Range(0, 10) == 0) _slime.Clip(0, "SLIMEW.smk", 0, 275, Mood.Idle, "A8:0 A1:1", 1, 1, 60, 60);
                else _slime.Clip(0, "SLIMEW.smk", 0, 275, Mood.Idle, "A4:0", 1, 1, 60, 60);
            }
            else _slime.Clip(0, "SLIME02.smk", 0, 275, Mood.Idle, "A9:2", 99, 99, canCancel: false);
            _slime.Clip(1, "SLIME01.smk", 0, 275, Mood.Idle, "A9:2", canCancel: false, sound: "SLIME01.raw").OnEnd = () =>
            {
                var w = App.Session?.World;
                if (w != null && !IsPhonePane) w.Execute(new SetWorkshopSlimedCommand());
            };
            _slime.Clip(2, "SLIME02.smk", 0, 275, Mood.Idle, "A9:2", 99, 99, canCancel: false);
            for (int c = 0; c < 5; c++)
            {
                var b = _bubbles[c] = new SmackerPerson(Assets, App.Audio);
                var bp = BubblePos[c];
                b.Clip(0, "BLASEW.smk", bp.x, bp.y, Mood.Idle, "A9:0").Decide = () => Slimed ? 2 : -1;
                b.Clip(1, "BLASE0" + (c % 3 + 1) + ".smk", bp.x, bp.y, Mood.Idle, "A9:0", canCancel: false);
                b.Clip(2, "BLASEW.smk", bp.x, bp.y, Mood.Idle, "A9:1", 1, 1, 60 + Random.Range(0, 160), 260 + Random.Range(0, 160), canCancel: false);
            }
            _bomb = new SmackerPerson(Assets, App.Audio);
            _bomb.Clip(0, "BombeW.smk", 180, 386, Mood.Idle, "A9:0", 1, 1, 200, 420).Decide = () => _lit ? 1 : -1;
            _bomb.Clip(1, "Bombe.smk", 180, 386, Mood.Idle, "A9:0", canCancel: false, sound: "Bombe.raw").OnStart = () => _lit = false;
            if (Random.Range(0, 6) == 0) _saw = new FrameAnimation(Lib, "SOUD", 21, AnimMode.Repeat, 150, 2, 100, "saw.raw", first: 0, digits: 4);
            _flame = new FrameAnimation(Lib, "FLAME", 3, AnimMode.Repeat, 0, 1);
            _light = new FrameAnimation(Lib, "LIGHT", 2, AnimMode.Repeat, 400, 2, 300);
        }

        protected override void OnRoomEnter()
        {
            SetBackgroundFx(0, "crash.raw", 100000, -10000);
            SetBackgroundFx(1, "crash2.raw", 90000, 40000);
            SetBackgroundFx(2, "crash3.raw", 120000, 75000);
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Mechaniker ? _man : null;

        public override void Animate(float dt)
        {
            _man.Update(dt);
            _plane.Update(dt);
            _slime.Update(dt);
            if (Slimed) foreach (var b in _bubbles) b.Update(dt);
            if (!Slimed) _lit = false;
            _bomb.Update(dt);
            _saw?.Update(dt, App.Audio);
            _flame.Update(dt, App.Audio);
            _light.Update(dt, App.Audio);
        }

        protected override void Hotspots(InputState input)
        {
            if (!HasOil) Hot(72, 294, 101, 315, ClickOil);
            Hot(195, 212, 263, 437, ClickMech);
            Exit(0, 0, 50, 439);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickMech) StartDialog(Talkers.Mechaniker, DialogMedium.Air, 1);
            else if (id == ClickOil)
            {
                // Without the mechanic's trust (the beer) he refuses; with it he hands the oil can over (if there is room).
                var s = App.Session;
                if (s.Local.MechTrust == 0) StartDialog(Talkers.Mechaniker, DialogMedium.Air, 20);
                else if (s.Local.Items.HasSpace)
                {
                    StartDialog(Talkers.Mechaniker, DialogMedium.Air, 21);
                    s.World.Execute(new PickUpItemCommand(s.Local.Index, ItemId.Oil));
                }
            }
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            _plane.Draw(s, ox);
            _slime.Draw(s, ox);
            if (Slimed) foreach (var b in _bubbles) b.Draw(s, ox);
            if (!Guarded) s.Draw(Assets.Texture(Lib, "DOOR"), ox + 534, 73);
            if (!HasOil) s.Draw(Assets.Texture(Lib, "OIL"), ox + 72, 294);
            _man.Draw(s, ox);
            _bomb.Draw(s, ox);
            _saw?.Draw(s, Assets, ox + 470, 290);
            _flame.Draw(s, Assets, ox + 104, 314);
            s.PushClip(new RectInt(ox, 0, 640, 440));
            _light.Draw(s, Assets, ox + 244, 89);
            s.PopClip();
        }
    }

    /// <summary>
    /// MENU_PLANEREPAIRS (rooms-and-ui-2 §4.22): own planes with condition, repair target and extra cost;
    /// clicking a row enters a new target with the calculator; "all planes" applies the last value.
    /// </summary>
    public sealed class PlaneRepairsMenu : PopupMenu
    {
        const string Lib = "gli/wartung.gli";
        int _page, _all = 100, _sel = -1;
        public override int Type => MenuIds.PlaneRepairs;
        public override Vector2Int Size => new Vector2Int(368, 249);
        public override Texture Background => Assets.Texture(Lib, "WARTUNG");
        Player P => App.Session.Local;
        List<Plane> Planes => P.Planes.ToList();

        static long Cost(Plane p) => System.Math.Max(0, p.TargetCondition - (p.WorstCondition + 20)) * (long)p.Price / 110;

        static int Round5(long v) => (int)System.Math.Max(0, System.Math.Min(100, (v + 2) / 5 * 5));

        public override void Update(InputState input, Vector2Int l)
        {
            var list = Planes;
            int pages = System.Math.Max(1, (list.Count + 12) / 13);
            int row = -1;
            if (Hot(l, 55, 51, 348, 212)) { row = (l.y - 51) / 12; if (_page * 13 + row >= list.Count || row >= 13) row = -1; }
            bool close = Hot(l, 55, 211, 120, 226);
            bool all = Hot(l, 214, 211, 346, 226);
            bool prev = pages > 1 && Hot(l, 32, 208, 62, 238, 0, CursorLook.Left);
            bool next = pages > 1 && Hot(l, 328, 208, 358, 238, 0, CursorLook.Right);
            if (row >= 0)
                foreach (char c in input.Typed)
                {
                    var pl = list[_page * 13 + row];
                    if (c == '+' || c == '-') App.Session.World.Execute(new SetRepairTargetCommand(P.Index, pl.Id, Round5(pl.TargetCondition + (c == '+' ? 5 : -5))));
                }
            if (!input.LeftDown) return;
            if (prev) { _page = (_page + pages - 1) % pages; Paper(); }
            else if (next) { _page = (_page + 1) % pages; Paper(); }
            else if (close) Close();
            else if (all) App.Session.World.Execute(new SetRepairTargetCommand(P.Index, 0, Round5(_all)));
            else if (row >= 0)
            {
                var pl = list[_page * 13 + row];
                _sel = row;
                Host.Calculator.Open(ScreenPos + new Vector2Int(350, 30 + 12 * row), v =>
                {
                    _all = Round5(v);
                    App.Session.World.Execute(new SetRepairTargetCommand(P.Index, pl.Id, _all));
                    _sel = -1;
                }, () => _sel = -1);
            }
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            f.Draw(s, x + 63, y + 40, Std("Schd", 1000));
            f.Draw(s, x + 175, y + 40, Std("Schd", 1002));
            f.Draw(s, x + 220, y + 40, Std("Schd", 1005));
            f.Draw(s, x + 285, y + 40, Std("Schd", 1006), TextAlign.Right);
            var list = Planes;
            for (int k = 0; k < 13 && _page * 13 + k < list.Count; k++)
            {
                var pl = list[_page * 13 + k];
                int ry = y + 53 + 12 * k;
                if (l.x >= 55 && l.x <= 348 && l.y >= 51 + 12 * k && l.y < 63 + 12 * k) HoverBand(s, new RectInt(x + 55, ry - 2, 291, 13));
                if (k == _sel) s.FillRect(new RectInt(x + 216, ry - 1, 36, 12), new Color32(32, 255, 100, 110));
                string name = pl.Name;
                while (name.Length > 1 && f.Measure(name) > 105) name = name.Substring(0, name.Length - 1);
                f.Draw(s, x + 63, ry, name);
                f.Draw(s, x + 185, ry, pl.Condition + "%");
                f.Draw(s, x + 220, ry, pl.TargetCondition + "%");
                f.Draw(s, x + 340, ry, UiKit.Money(Cost(pl)), TextAlign.Right);
            }
            f.Draw(s, x + 63, y + 213, Std("Schd", 500));
            f.Draw(s, x + 216, y + 213, DialogText.Format(Std("Schd", 1008), new object[] { (long)_all }));
            if (list.Count > 13) { s.Draw(Assets.Texture(Lib, "LEFT"), x + 42, y + 218); s.Draw(Assets.Texture(Lib, "RIGHT"), x + 338, y + 218); }
        }
    }

    /// <summary>MENU_PANNEN (rooms-and-ui-2 §4.23): breakdown counts per plane; a row shows its incident list.</summary>
    public sealed class BreakdownMenu : PopupMenu
    {
        const string Lib = "gli/wartung.gli";
        int _page, _detail = -1;
        public override int Type => MenuIds.Pannen;
        public override Vector2Int Size => new Vector2Int(368, 249);
        public override Texture Background => Assets.Texture(Lib, "WARTUNG");
        List<Plane> Planes => App.Session.Local.Planes.ToList();

        public override void Update(InputState input, Vector2Int l)
        {
            var list = Planes;
            int pages = System.Math.Max(1, (list.Count + 12) / 13);
            int row = _detail < 0 && Hot(l, 55, 51, 348, 212) ? (l.y - 51) / 12 : -1;
            if (row >= 0 && (row >= 13 || _page * 13 + row >= list.Count)) row = -1;
            bool close = Hot(l, 55, 211, 120, 226);
            bool prev = Hot(l, 32, 208, 62, 238, 0, CursorLook.Left);
            bool next = Hot(l, 328, 208, 358, 238, 0, CursorLook.Right);
            if (!input.LeftDown) return;
            if (close) { if (_detail >= 0) _detail = -1; else Close(); }
            else if (prev) { if (_detail >= 0) _detail = (_detail + list.Count - 1) % list.Count; else _page = (_page + pages - 1) % pages; Paper(); }
            else if (next) { if (_detail >= 0) _detail = (_detail + 1) % list.Count; else _page = (_page + 1) % pages; Paper(); }
            else if (row >= 0) _detail = _page * 13 + row;
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            var list = Planes;
            if (_detail >= 0 && _detail < list.Count)
            {
                var pl = list[_detail];
                f.Draw(s, x + 63, y + 40, pl.Name);
                f.Draw(s, x + 63, y + 55, pl.NumIncidents + " " + Std("Schd", 1009));
                int k = 0;
                foreach (var inc in pl.Incidents)
                {
                    int ry = y + 73 + 12 * k++;
                    if (k > 11) break;
                    var date = App.Session.World.Clock.CalendarDate.AddDays(inc.Date - App.Session.World.Clock.Date);
                    int mins = inc.Time / Sim.Core.GameClock.UnitsPerMinute; f.Draw(s, x + 63, ry, mins / 60 + ":" + (mins % 60).ToString("00"));
                    f.Draw(s, x + 108, ry, date.Day + "." + date.Month + ".");
                    f.Draw(s, x + 153, ry, Std("Schd", 2360 + inc.Code));
                }
                f.Draw(s, x + 63, y + 213, Std("Schd", 1010));
                return;
            }
            f.Draw(s, x + 63, y + 40, Std("Schd", 1000));
            f.Draw(s, x + 265, y + 40, Std("Schd", 1009));
            for (int k = 0; k < 13 && _page * 13 + k < list.Count; k++)
            {
                var pl = list[_page * 13 + k];
                int ry = y + 53 + 12 * k;
                if (l.x >= 55 && l.x <= 348 && l.y >= 51 + 12 * k && l.y < 63 + 12 * k) HoverBand(s, new RectInt(x + 55, ry - 2, 291, 13));
                f.Draw(s, x + 63, ry, pl.Name);
                f.Draw(s, x + 265, ry, pl.NumIncidents.ToString());
            }
            f.Draw(s, x + 63, y + 213, Std("Schd", 500));
        }
    }

    /// <summary>Mechanic (rooms-and-ui-3b §5).</summary>
    [DialogScript(Talkers.Mechaniker)]
    public sealed class MechanicDialog : DialogTree
    {
        static int Angry(Player p) => p.MechAngry;
        static void SetAngry(Player p, int v) => p.MechAngry = v;

        static void MainList(DialogSession d)
        {
            if (d.Medium == DialogMedium.Phone) d.Choices(2100, 2102); else d.Choices(2000, 2006);
        }

        public MechanicDialog() : base("Mech")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (d.Par1 == 2) { d.Say(10000); return; }   // the beer was handed over (mechanic trust ≥ 1)
                if (d.Par1 == 20) { d.Say(1020); return; }
                if (d.Par1 == 21) { d.Say(1021); return; }
                if (Angry(p) == 2) { d.Say(d.Medium == DialogMedium.Phone ? 5003 : 5002); return; }
                if (d.Medium == DialogMedium.Phone) d.Say(1001);
                else if (d.TalkedToA) d.Say(1002);
                else d.Say(1000);
                d.TalkedToA = true;
            });
            On(new[] { 1000, 1001, 1002, 3005, 2500 }, MainList);
            On(new[] { 2000, 2100 }, d => d.Say(3000));
            On(3000, d => d.Say(3001));
            On(3001, d => d.Say(3002));
            On(3002, d => d.Say(3003));
            On(3003, d => d.Say(d.Player.MechTrust == 2 ? 3004 : 3005));
            On(3004, d => d.Say(3005));
            On(new[] { 2001, 2101 }, d => d.Say(4000 + Mathf.Clamp(d.Player.MechMode, 0, 3)));
            On(new[] { 4000, 4001, 4002, 4003, 4020, 4021 }, d =>
            {
                if (d.Player.MechTrust == 2) d.Choices(4010, 4013); else d.Choices(4011, 4014);
            });
            On(4010, 4013, (d, id) =>
            {
                int mode = id - 4010;
                d.World.Execute(new SetMechModeCommand(d.Player.Index, mode));
                d.Say(4060);
                d.ShowNumbers(9994060, (long)Maintenance.RepairPrice[mode] * d.Player.Planes.Count / 30);
            });
            On(4014, d =>
            {
                if (d.Player.MechTrust == 1) d.Player.MechTrust = 2;
                d.Say(d.Player.MechTrust == 2 ? 4021 : 4020);
            });
            On(2002, d => d.Say(d.World.Clock.Date == 0 ? 2500 : 2501));
            On(2501, d =>
            {
                var menu = new NotepadListMenu(MenuIds.PlaneCosts)
                {
                    Title0 = d.Assets.StdText.Get("Schd", 1000),
                    Title1 = d.Assets.StdText.Get("Schd", 1003),
                };
                foreach (var pl in d.Player.Planes.Items) menu.Rows.Add(new NotepadListMenu.Row { Name = pl.Name, Value = UiKit.Thousands(pl.MaintenanceCosts), Tag = pl });
                var kit = d.Stage.App.Ui.Kit;
                menu.DrawTip = (s, x, y, i) => PlaneTips.DrawPlane(s, d.Assets, kit, x + 8, y + 6, (Plane)menu.Rows[i].Tag, null, 166,
                    d.Assets.StdText.Get("Schd", 1003), UiKit.Money(((Plane)menu.Rows[i].Tag).MaintenanceCosts), ownPlane: true);
                menu.Picked = (i, r) => { };
                d.OpenMenu(menu, 2502, new Vector2Int(200, 300), 0.1f);
            });
            On(2003, d => d.OpenMenu(new PlaneRepairsMenu(), 2502, new Vector2Int(200, 300), 0.1f));
            On(2004, d => d.OpenMenu(new BreakdownMenu(), 2502, new Vector2Int(200, 300), 0.1f));
            On(2005, d => d.Say(Angry(d.Player) == 0 ? 5000 : 5001));
            On(new[] { 5000, 2502 }, d => { SetAngry(d.Player, 1); MainList(d); });
            On(new[] { 5001, 5002, 5003 }, d =>
            {
                SetAngry(d.Player, 2);
                d.End();
                if (d.Medium == DialogMedium.Air && d.Stage.App.Rooms.CurrentRoom == Framework.RoomIds.Werkstatt) d.Stage.App.Rooms.Leave();
            });
        }
    }
}
