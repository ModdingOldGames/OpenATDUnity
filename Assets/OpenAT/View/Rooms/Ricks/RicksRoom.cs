using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;

namespace OpenAT.View.Rooms.Ricks
{
    /// <summary>
    /// Rick's café (rooms-and-ui-8 §1): Rick behind the bar (rumours and tips, <see cref="RickDialog"/>), the
    /// neon sign and the drinker at the bar (horseshoe chain, strike ender, <see cref="DrinkerDialog"/>).
    /// </summary>
    [Room(RoomIds.Ricks)]
    [ScreenName("ricks")]
    public sealed class RicksRoom : RoomScreen
    {
        protected override string Library => "room/rick.gli";
        protected override string Background => "RICK";
        protected override int DefaultDialogPartner => Talkers.Rick;
        const int ClickRick = 10, ClickDrinker = 11;

        SmackerPerson _rick, _neon, _mouth, _eyes;

        protected override void OnCreate()
        {
            int x = 157, y = 130;
            _rick = new SmackerPerson(Assets, App.Audio);
            _rick.Clip(0, "Bar_Wait.smk", x, y, Mood.Idle, "A9:0 A1:1 E1:2");
            _rick.Clip(1, "Bar_Aug.smk", x, y, Mood.Idle, "A9:0 A1:1 E1:2", canCancel: false);
            _rick.Clip(2, "Bar_Rede.smk", x, y, Mood.Talking, "A9:2 A2:3 A2:4 E1:5 E1:0");
            _rick.Clip(3, "Bar_Fing.smk", x, y, Mood.Talking, "A9:2 A2:4 E1:5 E1:0");
            _rick.Clip(4, "Bar_Arm.smk", x, y, Mood.Talking, "A9:2 A2:3 E1:5 E1:0");
            _rick.Clip(5, "Bar_Wait.smk", x, y, Mood.Listening, "A9:5 A2:6 E1:0 E1:2");
            _rick.Clip(6, "Bar_Aug.smk", x, y, Mood.Listening, "A9:5 A2:6 E1:0 E1:2", canCancel: false);

            _neon = new SmackerPerson(Assets, App.Audio);
            _neon.Clip(0, "Neon.smk", 514, 0, Mood.Idle, "A9:0", 99, 99);

            _eyes = new SmackerPerson(Assets, App.Audio);
            _eyes.Clip(0, "AugW.smk", 479, 199, Mood.Idle, "A9:0 A9:1", 1, 1, 10, 90);
            _eyes.Clip(1, "Aug.smk", 479, 199, Mood.Idle, "A9:0 A9:1");

            // The drinker's mouth; the drinking chain 1→2→3→4 is unreachable from the idle hub (open question 2).
            int mx = 483, my = 230;
            _mouth = new SmackerPerson(Assets, App.Audio);
            _mouth.Clip(0, "Mund_W.smk", mx, my, Mood.Idle, "A9:0 E1:5 E1:6", 1, 1, 10, 90);
            _mouth.Clip(1, "Mund.smk", mx, my, Mood.Idle, "A9:2 E1:5 E1:6");
            _mouth.Clip(2, "MundA.smk", mx, my, Mood.Idle, "A9:3", 1, 1, 0, 30);
            _mouth.Clip(3, "MundAW.smk", mx, my, Mood.Idle, "A9:4");
            _mouth.Clip(4, "MundZ.smk", mx, my, Mood.Idle, "A9:0", 1, 1, 10, 90);
            _mouth.Clip(5, "Mund.smk", mx, my, Mood.Talking, "A9:5 E1:6 E1:0");
            _mouth.Clip(6, "Mund_W.smk", mx, my, Mood.Listening, "A9:6 E1:5 E1:0", 1, 1, 10, 90);
        }

        protected override void OnRoomEnter()
        {
            SetBackgroundFx(0, "Kaffee.raw", 40000);
            SetBackgroundFx(2, "toilet.raw", 120000, 0, 40);
        }

        protected override void OnLeaveRoom() { _rick.Dispose(); _neon.Dispose(); _mouth.Dispose(); _eyes.Dispose(); }

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) =>
            talker == Talkers.Rick ? _rick : talker == Talkers.Trinker ? _mouth : null;

        public override void Animate(float dt) { _neon.Update(dt); _rick.Update(dt); _mouth.Update(dt); _eyes.Update(dt); }

        protected override void Hotspots(InputState input)
        {
            Hot(466, 141, 639, 439, ClickDrinker);
            Hot(159, 131, 302, 346, ClickRick);
            Exit(570, 0, 640, 225);
            Exit(0, 0, 70, 439);
        }

        protected override void OnClick(int id)
        {
            if (id == ClickRick) StartDialog(Talkers.Rick, DialogMedium.Air, 0);
            else if (id == ClickDrinker) StartDialog(Talkers.Trinker, DialogMedium.Air, 0);
        }

        protected override void DrawRoom(Screen2D s)
        {
            _neon.Draw(s, RX);
            _rick.Draw(s, RX);
            _mouth.Draw(s, RX);
            _eyes.Draw(s, RX);
        }
    }

    /// <summary>Rick, the barkeeper (rooms-and-ui-3b §13a): introduction once, then one tip per day.</summary>
    [DialogScript(Talkers.Rick)]
    public sealed class RickDialog : DialogTree
    {
        const string Met = "openat.rickMet", Today = "openat.rickDay";

        public RickDialog() : base("Rick")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                bool met = p.Extensions.ContainsKey(Met);
                bool today = p.Extensions.TryGetValue(Today, out var v) && v is int day && day == d.World.Clock.Date;
                if (met && today) d.Say(3000);
                else if (met) d.PlayerSays(2000);
                else d.Say(1000);
                p.Extensions[Met] = true;
                p.Extensions[Today] = d.World.Clock.Date;
            });
            On(1000, d => d.PlayerSays(1100));
            On(1100, d => d.Say(1200, d.Player.Airline));
            On(1200, d => d.PlayerSays(1300));
            On(1300, d => d.Say(1400));
            On(1400, d => d.Say(1401));
            On(2000, Tip);
        }

        static bool OneIn5() => UnityEngine.Random.Range(0, 5) == 0;

        static readonly Sim.Staff.AdvisorType[] AdvisorOrder =
        {
            Sim.Staff.AdvisorType.Informer, Sim.Staff.AdvisorType.Fitness, Sim.Staff.AdvisorType.Security, Sim.Staff.AdvisorType.Order,
            Sim.Staff.AdvisorType.Finance, Sim.Staff.AdvisorType.Personnel, Sim.Staff.AdvisorType.Route,
        };

        /// <summary>Today's applicants include this advisor type (the original's "HasBeraterApplied").</summary>
        static bool Applied(World w, Sim.Staff.AdvisorType t)
        {
            foreach (var wk in w.Workers.Workers)
                if (wk.Type == (int)t && wk.Employer == Sim.Staff.WorkerType.Jobless) return true;
            return false;
        }

        /// <summary>First plane type (album order) of this mission, or any in the free game, that becomes available on <paramref name="day"/>.</summary>
        static Sim.Data.PlaneType Debut(World w, int day)
        {
            Sim.Data.PlaneType best = null;
            foreach (var t in w.Data.PlaneTypes.All)
                if ((t.FirstMission == w.Difficulty || w.Difficulty == Mission.FreeGame) && t.FirstDay == day && (best == null || t.Index < best.Index)) best = t;
            return best;
        }

        /// <summary>The first matching rumour (§13a list; most steps fire on a 1-in-5 roll).</summary>
        static void Tip(DialogSession d)
        {
            var w = d.World;
            var p = d.Player;
            int date = w.Clock.Date;
            if (date == 1 && w.Difficulty == Mission.Rocket) { d.Say(2100); return; }
            if (date == 1 && w.Difficulty == Mission.ConnectWorld) { d.Say(2101); return; }
            // A plane type of this mission (or any in the free game) debuts today: text 2102 with type and maker.
            var debut = Debut(w, date);
            if (debut != null) { d.SayText(9999, DialogText.Get(d.Assets, "Rick", 2102, new object[] { debut.Name, debut.Manufacturer }) ?? ""); return; }
            if (date > 3 && w.MoneyInBankBin && OneIn5()) { d.Say(2104); return; }
            if (date > 15 && p.MechTrust == 0 && OneIn5()) { d.Say(2106); return; }
            // An advisor type has applied today but is not hired (info, fitness, security, orders, money, personnel, route).
            for (int k = 0; k < AdvisorOrder.Length; k++)
                if (!p.HasAdvisor(AdvisorOrder[k]) && Applied(w, AdvisorOrder[k]) && OneIn5()) { d.Say(2107 + k); return; }
            debut = Debut(w, date + 3);
            if (debut != null) { d.SayText(9999, DialogText.Get(d.Assets, "Rick", 2103, new object[] { debut.Name, debut.Manufacturer }) ?? ""); return; }
            var sym = w.Rules.Has<IRivalSympathy>() ? w.Rules.Get<IRivalSympathy>() : null;
            bool coopCandidate = false;   // any rival likes us but does not cooperate; then one 1-in-5 roll
            for (int k = 0; k < Player.Count && sym != null; k++)
                if (k != p.Index && !w.Players[k].IsOut && sym.Get(w, k, p.Index) > 0 && !w.Players[k].Cooperates[p.Index]) coopCandidate = true;
            if (coopCandidate && OneIn5()) { d.Say(2114); return; }
            foreach (var pl in p.Planes.ToList())
            {
                if (pl.TargetCondition - pl.WorstCondition > 20 && OneIn5()) { d.Say(2115); return; }
                if (pl.TargetCondition < 60 && OneIn5()) { d.Say(2116); return; }
                if (pl.Condition <= pl.WorstCondition + 2 && pl.WorstCondition <= 80 && OneIn5()) { d.Say(2121); return; }
            }
            if (p.SharesOwned[p.Index] < p.SharesIssued / 2 && OneIn5()) { d.Say(2117); return; }
            if (p.IssuableShares >= 10000 && OneIn5()) { d.Say(2118); return; }
            if (p.Dividend < 5 && OneIn5()) { d.Say(2119); return; }
            if (p.Money > 30000000 && OneIn5()) { d.Say(2120); return; }
            d.Say(2199);
        }
    }

    /// <summary>The drinker at the bar (rooms-and-ui-3b §13b): horseshoe thanks, talks the staff out of a strike.</summary>
    [DialogScript(Talkers.Trinker)]
    public sealed class DrinkerDialog : DialogTree
    {
        public DrinkerDialog() : base("Rick")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 == 800 || d.Par1 >= 10000) { d.Say(d.Par1); return; }
                if (p.TrinkerTrust && p.StrikeRunning)
                {
                    if (p.StrikeEndType == 3 && p.StrikeEndCountdown > 0) { d.Say(6001); return; }
                    if (d.World.Execute(new DrinkerEndsStrikeCommand(p.Index)).Ok) { d.Say(6000); return; }
                }
                d.Say(5000 + UnityEngine.Random.Range(0, 9));
            });
            On(6000, d => { d.World.Execute(new DrinkerHurriesStrikeEndCommand(d.Player.Index)); d.End(); });
        }
    }
}
