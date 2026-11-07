using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.Sim.Staff;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Personal
{
    /// <summary>Hiring clerk ("a" talker of every airline): job market for advisors, pilots and attendants (rooms-and-ui-3b §4a).</summary>
    [DialogScript(Talkers.Personal1a, Talkers.Personal1a + 2, Talkers.Personal1a + 4, Talkers.Personal1a + 6)]
    public sealed class HiringClerkDialog : DialogTree
    {
        static int Jobless(World w, int type)
        {
            int n = 0;
            foreach (var wk in w.Workers.Workers)
                if (wk.Employer == WorkerType.Jobless && (type < WorkerType.Pilot ? wk.IsAdvisor : wk.Type == type)) n++;
            return n;
        }

        static readonly int[] MenuTypes = { 1, WorkerType.Pilot, WorkerType.Attendant };

        public HiringClerkDialog() : base("Jobs")
        {
            Open(d =>
            {
                var w = d.World;
                if (d.Par1 == 20) { d.Say(d.Player.SeligTrust ? 301 : 302); return; }
                if (d.Par1 == 300) { d.Say(300); return; }
                if (d.Par1 >= 10000) { d.End(); return; }
                if (d.Medium == DialogMedium.Phone)
                {
                    d.Say(101);
                    d.ShowNumbers(9990101, Jobless(w, 1), Jobless(w, WorkerType.Pilot), Jobless(w, WorkerType.Attendant));
                    return;
                }
                if (!d.TalkedToA) { d.TalkedToA = true; d.PlayerSays(99); }
                else d.Say(102);
            });
            On(99, d => d.Say(100));
            On(new[] { 100, 102, 250 }, d => d.Choices(200, 203));
            On(200, 202, (d, id) => d.OpenMenu(new PersonalMenu(MenuTypes[id - 200], true), -1, new Vector2Int(291, 279), 0.1f));
            // 300: the postcard was given (the items domain sets SeligTrust); 301: the pills.
            On(300, d => d.End());
            On(301, d => { d.World.Execute(new ReceiveRewardCommand(d.Player.Index, ItemId.Pills)); d.End(); });
        }
    }

    /// <summary>Staff clerk ("b" talker): salaries, own staff, strikes (rooms-and-ui-3b §4b).</summary>
    [DialogScript(Talkers.Personal1b, Talkers.Personal1b + 2, Talkers.Personal1b + 4, Talkers.Personal1b + 6)]
    public sealed class StaffClerkDialog : DialogTree
    {
        static readonly int[] MenuTypes = { 1, WorkerType.Pilot, WorkerType.Attendant };

        public StaffClerkDialog() : base("Jobs")
        {
            Open(d =>
            {
                var p = d.Player;
                if (d.Par1 >= 1000 && d.Par1 <= 1010) { d.Say(d.Par1); return; }
                if (d.Par1 >= 10000) { d.End(); return; }
                if (p.StrikeHours > 0 && p.StrikeEndCountdown == 0) { d.Say(950); return; }
                if (d.Medium == DialogMedium.Phone) { d.Say(501); return; }
                if (d.TalkedToB) d.Say(503);
                else { d.TalkedToB = true; d.PlayerSays(499); }
            });
            On(499, d => d.Say(500));
            On(new[] { 500, 501, 502, 503, 820, 962 }, d => d.Choices(600, 605));
            On(600, d => { d.World.Execute(new ChangeAllSalariesCommand(d.Player.Index, true)); d.Say(700); });
            On(700, d => d.Say(820));
            On(601, d => d.Say(800));
            On(800, d => d.Choices(810, 811));
            On(810, d => { d.World.Execute(new ChangeAllSalariesCommand(d.Player.Index, false)); d.Say(820); });
            On(811, d => d.Say(820));
            On(602, 604, (d, id) =>
            {
                if (d.Medium == DialogMedium.Phone) d.Say(900);
                else d.OpenMenu(new PersonalMenu(MenuTypes[id - 602], false), -1, new Vector2Int(405, 261), 0.1f);
            });
            On(950, d => d.Choices(960, 962));
            On(960, d => { d.World.Execute(new EndStrikeCommand(d.Player.Index, StrikeEndOption.RaiseSalaries)); d.Say(970); });
            On(961, d => { d.World.Execute(new EndStrikeCommand(d.Player.Index, StrikeEndOption.Threaten)); d.Say(971); });
            On(new[] { 970, 971 }, d => d.Say(502));
        }
    }
}
