using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Security
{
    /// <summary>
    /// Safe &amp; Secure Ltd. (rooms-and-ui-8 §4): the security officer at his desk, the peephole spy, the
    /// door, safe and gas mask, and four surveillance monitors wandering between camera views. The officer's
    /// dialog opens MENU_SECURITY (<see cref="SecurityMenu"/>, rows toggled with <see cref="ToggleSecurityCommand"/>).
    /// </summary>
    [Room(RoomIds.Security)]
    [ScreenName("security")]
    public sealed class SecurityRoom : RoomScreen
    {
        protected override string Library => "room/security.gli";
        protected override string Background => "SECURITY";
        protected override int DefaultDialogPartner => Talkers.Security;
        const int ClickOfficer = 10;

        SmackerPerson _officer, _spy, _door, _safe, _mask;
        readonly SmackerPerson[] _monitors = new SmackerPerson[4];
        /// <summary>The officer's "comm" variable: 1 makes the spy look through the peephole (−1 = free choice).</summary>
        int _comm = -1;
        AudioSource _loop;

        protected override void OnCreate()
        {
            _officer = new SmackerPerson(Assets, App.Audio);
            int x = 240, y = 79;
            _officer.Clip(0, "SO_normal.smk", x, y, Mood.Idle, "A1:0 A9:14 A1:7 A1:10 E1:1", 2, 3);
            _officer.Clip(1, "SO_zum_reden.smk", x, y, Mood.Idle, "E1:2 E1:4 E1:6", canCancel: false);
            _officer.Clip(2, "SO_reden_mund_zu.smk", x, y, Mood.Listening, "A9:2 E5:4 E5:5", 1, 1, 50, 50);
            _officer.Clip(3, "ArabP.smk", x, y, Mood.Talking, "A9:4", 1, 1, 20, 20);
            _officer.Clip(4, "SO_reden.smk", x, y, Mood.Talking, "A9:4 E1:2 E1:5");
            _officer.Clip(5, "SO_reden_zurueck.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _officer.Clip(6, "SO_erschrecken.smk", x, y, Mood.Idle, "A9:0", canCancel: false);
            _officer.Clip(7, "SO_hindrehen.smk", x, y, Mood.Idle, "A2:8 A1:9 E1:9", canCancel: false);
            _officer.Clip(8, "SO_druecken.smk", 256, 63, Mood.Idle, "A9:8 A9:9 E1:9", canCancel: false);
            _officer.Clip(9, "SO_herdrehen.smk", x, y, Mood.Idle, "A1:0", canCancel: false);
            _officer.Clip(10, "SO_tasse_nehmen.smk", x, y, Mood.Idle, "A1:11 A1:12 E1:13", canCancel: false);
            _officer.Clip(11, "SO_tasse_trinken.smk", x, y, Mood.Idle, "A1:12 A1:13 E1:13", canCancel: false);
            _officer.Clip(12, "SO_tasse_zittern.smk", x, y, Mood.Idle, "A1:11 A1:12 A1:13 E1:13", canCancel: false);
            _officer.Clip(13, "SO_tasse_hinsetzen.smk", x, y, Mood.Idle, "A1:0", canCancel: false);
            _officer.Clip(14, "SO_normal.smk", x, y, Mood.Idle, "A1:6 E1:1", 1, 1, 120, 120).OnStart = () => _comm = 1;

            _spy = new SmackerPerson(Assets, App.Audio);
            _spy.Clip(0, "SO_spion_pause.smk", 85, 327, Mood.Idle, "A9:0 A0:1").Decide = () => _comm;
            _spy.Clip(1, "SO_spion.smk", 85, 327, Mood.Idle, "A9:0").OnStart = () => _comm = 0;

            _door = new SmackerPerson(Assets, App.Audio);
            _door.Clip(0, "SO_tuer.smk", 38, 145, Mood.Idle, "A9:0");
            _safe = new SmackerPerson(Assets, App.Audio);
            _safe.Clip(0, "SO_tresor_pause.smk", 528, 198, Mood.Idle, "A9:0 A2:1", 1, 1, 20, 20);
            _safe.Clip(1, "SO_tresor.smk", 528, 198, Mood.Idle, "A9:1 A3:0", 1, 3);
            _mask = new SmackerPerson(Assets, App.Audio);
            _mask.Clip(0, "SO_gasmask_pause.smk", 468, 89, Mood.Idle, "A9:0 A4:1", 1, 1, 80, 80);
            _mask.Clip(1, "SO_gasmask.smk", 468, 89, Mood.Idle, "A9:0");

            // Monitor 2 (KamB) is built but never drawn in the original; it is left out.
            _monitors[0] = Monitor('A', 7, 125, 85);
            _monitors[1] = Monitor('C', 3, 362, 83);
            _monitors[2] = Monitor('D', 3, 348, 277);
            _monitors[3] = Monitor('E', 3, 509, 47);
        }

        /// <summary>
        /// A ring of camera views: pan clip "Kam&lt;m&gt;&lt;i&gt;&lt;i+1&gt;" then view clip "Kam&lt;m&gt;&lt;i+1&gt;", which either
        /// stays or pans on (both weight 9, hold 0..20).
        /// </summary>
        SmackerPerson Monitor(char m, int views, int x, int y)
        {
            var p = new SmackerPerson(Assets, App.Audio);
            int n = 2 * views;
            for (int i = 1; i <= views; i++)
            {
                int to = i % views + 1;
                int pan = 2 * (i - 1), view = pan + 1;
                p.Clip(pan, "Kam" + m + i + to + ".smk", x, y, Mood.Idle, "A9:" + view, 1, 1, 0, 20);
                p.Clip(view, "Kam" + m + to + ".smk", x, y, Mood.Idle, "A9:" + view + " A9:" + ((view + 1) % n), 1, 1, 0, 20);
            }
            return p;
        }

        protected override void OnRoomEnter() => _loop = App.Audio.PlayLoop("SOUND/secure.raw", 0.5f);

        protected override void OnLeaveRoom()
        {
            if (_loop != null) App.Audio.StopLoop(_loop);
            _loop = null;
            foreach (var p in All()) p.Dispose();
        }

        System.Collections.Generic.IEnumerable<SmackerPerson> All()
        {
            yield return _door; yield return _mask;
            foreach (var m in _monitors) yield return m;
            yield return _spy; yield return _safe; yield return _officer;
        }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.Security ? _officer : null;

        public override void Animate(float dt) { foreach (var p in All()) p.Update(dt); }

        protected override void Hotspots(InputState input)
        {
            Hot(288, 144, 401, 267, ClickOfficer);
            Exit(4, 70, 112, 412);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickOfficer) StartDialog(Talkers.Security, DialogMedium.Air, 1000);
        }

        // Draw order: door, gas mask, monitors 1,3,4,5, spy, safe, officer (§4).
        protected override void DrawRoom(Screen2D s) { foreach (var p in All()) p.Draw(s, RX); }
    }

    /// <summary>Mr Fightworthy (rooms-and-ui-3b §16).</summary>
    [DialogScript(Talkers.Security)]
    public sealed class SecurityDialog : DialogTree
    {
        public SecurityDialog() : base("Secu")
        {
            Open(d => d.Say(d.Par1 >= 10000 ? d.Par1 : 1000));
            On(1000, d => d.Choices(2000, 2002));
            On(2000, d => d.Say(3000));
            On(3000, d => d.Choices(2001, 2002));
            On(2001, d => d.OpenMenu(new SecurityMenu(), -1, new Vector2Int(320, 220), 0.17f));
            On(2002, d => d.Say(4000));
        }
    }

    /// <summary>
    /// MENU_SECURITY (rooms-and-ui-2 §4.24, items-security-special.md §2) on wartung.gli WARTUNG: nine services
    /// (std "Misc" 8000..8008, gate row levels 8008/8010/8011), "1 x" / "N x" (planes) prefix when active,
    /// the laptop row grey without a laptop, the daily costs (fixed, per plane, total) and "close".
    /// A click toggles a row (<see cref="ToggleSecurityCommand"/>; the gate row cycles its levels).
    /// </summary>
    public sealed class SecurityMenu : PopupMenu
    {
        const string Lib = "gli/wartung.gli";
        public override int Type => MenuIds.Security;
        public override Vector2Int Size => new Vector2Int(368, 249);
        public override Vector2Int? FixedPos => new Vector2Int(136, 95);
        public override Texture Background => Assets.Texture(Lib, "WARTUNG");

        Player P => App.Session.Local;

        public override void OnOpen() => Paper();

        public override void Update(InputState input, Vector2Int l)
        {
            var p = P;
            int row = -1;
            for (int i = 0; i < SecurityBits.Rows; i++)
            {
                if (i == SecurityBits.Laptop && !p.HasItem(ItemId.Laptop)) continue;
                if (Hot(l, 63, 53 + 12 * i, 346, 66 + 12 * i - 1)) row = i;
            }
            bool close = Hot(l, 55, 211, 120, 226);
            if (!input.LeftDown) return;
            if (row >= 0)
            {
                var r = App.Session.World.Execute(new ToggleSecurityCommand(p.Index, row));
                if (r.Ok) App.Audio.Play("SOUND/change.raw");
            }
            else if (close) Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var p = P;
            var f = Kit.SmallBlack;
            var grey = Kit.SmallGrey;
            f.Draw(s, x + 63, y + 40, Std("Misc", 8020));
            int planes = p.Planes.Count;
            int flags = p.SecurityFlags;
            for (int i = 0; i < SecurityBits.Rows; i++)
            {
                int ry = y + 55 + 12 * i;
                bool on = i == SecurityBits.Gate ? SecurityBits.GateLevel(flags) > 0 : SecurityBits.Has(flags, i);
                int text = 8000 + i;
                if (i == SecurityBits.Gate)
                {
                    int lvl = SecurityBits.GateLevel(flags);
                    text = lvl == 2 ? 8010 : lvl == 3 ? 8011 : 8008;
                }
                bool disabled = i == SecurityBits.Laptop && !p.HasItem(ItemId.Laptop);
                if (!disabled && l.x >= 63 && l.x <= 346 && l.y >= 53 + 12 * i && l.y < 65 + 12 * i)
                    HoverBand(s, new RectInt(x + 63, y + 53 + 12 * i, 283, 13));
                string label = Std("Misc", text);
                if (on) label = (i < 6 ? "1" : planes.ToString()) + " x " + label;
                (disabled ? grey : f).Draw(s, x + 63, ry, label);
            }
            // Daily costs: fixed services, the per-plane services for one plane, total (economy.md §14).
            long total = p.SecurityCosts(planes);
            long fix = p.SecurityCosts(0);
            long perPlane = p.SecurityCosts(1) - fix;
            f.Draw(s, x + 155, y + 185, Std("Misc", 8021)); f.Draw(s, x + 330, y + 185, UiKit.Money(fix), TextAlign.Right);
            f.Draw(s, x + 155, y + 197, Std("Misc", 8022)); f.Draw(s, x + 330, y + 197, UiKit.Money(perPlane), TextAlign.Right);
            f.Draw(s, x + 155, y + 213, Std("Misc", 8023)); f.Draw(s, x + 330, y + 213, UiKit.Money(total), TextAlign.Right);
            if (l.x >= 55 && l.x <= 120 && l.y >= 211 && l.y <= 226) HoverBand(s, new RectInt(x + 55, y + 211, 66, 15));
            f.Draw(s, x + 63, y + 213, Std("Schd", 500));
        }
    }
}
