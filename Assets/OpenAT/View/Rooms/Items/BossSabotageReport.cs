using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Items
{
    /// <summary>
    /// The boss's sabotage report in the morning briefing (rooms-and-ui-3b §3 "2004 / 2080", items-security-special.md
    /// §1.4), attached to the boss's dialog through <see cref="DialogActions"/>: opening 2004 when yesterday's pranks
    /// are listed in <c>w.Items.Report</c>, then per act the victim (2050..2053 / 2055), the act (2060 + plane mode or
    /// the act code), the boss's verdict (2070..2072, or 2073 naming the culprit and 2074 with the fine), and 2081
    /// back into the normal briefing. The fines themselves were booked by the items domain at 9:00.
    /// </summary>
    static class BossSabotageReport
    {
        static bool _active;
        static int _index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            DialogActions.OnOpen(Talkers.Boss, d =>
            {
                _active = false;
                var w = d.World;
                if (w == null || d.Par1 != 1 || d.Medium == DialogMedium.Phone || w.Clock.Date == 0) return false;
                if (BriefingLog.Of(w)?.Takeover != null) return false;
                var rep = w.Items.Report;
                if (rep == null || rep.Lines.Count == 0 || rep.Date != w.Clock.Date) return false;
                _active = true;
                _index = 0;
                d.Say(2004);
                return true;
            });
            DialogActions.On(Talkers.Boss, 2004, d => Guard(d, Step));
            foreach (int id in new[] { 2050, 2051, 2052, 2053, 2055 })
                DialogActions.On(Talkers.Boss, id, d => Guard(d, s =>
                {
                    int code = Line(s).Act.ActCode;
                    s.Say(code < 100 ? 2060 + code : code);
                }));
            for (int id = 2061; id <= 2065; id++) DialogActions.On(Talkers.Boss, id, d => Guard(d, Verdict));
            for (int id = 2075; id <= 2078; id++) DialogActions.On(Talkers.Boss, id, d => Guard(d, Verdict));
            for (int id = 2091; id <= 2096; id++) DialogActions.On(Talkers.Boss, id, d => Guard(d, Verdict));
            foreach (int id in new[] { 2070, 2071, 2072, 2074 })
                DialogActions.On(Talkers.Boss, id, d => Guard(d, s => { _index++; Step(s); }));
            DialogActions.On(Talkers.Boss, 2073, d => Guard(d, s =>
            {
                var line = Line(s);
                s.Say(2074, Airline(s, line.Act.Victim));
                s.ShowNumbers(9992074, UiKit.Thousands(line.Fine));
            }));
        }

        static bool Guard(DialogSession d, System.Action<DialogSession> a)
        {
            var rep = d.World?.Items.Report;
            if (!_active || rep == null || _index >= rep.Lines.Count) return false;
            a(d);
            return true;
        }

        static SabotageReportLine Line(DialogSession d) => d.World.Items.Report.Lines[_index];

        static string Airline(DialogSession d, int player) =>
            player >= 0 && player < Player.Count ? d.World.Players[player].Airline : "";

        /// <summary>Next act (victim line), or back to the normal briefing (2081).</summary>
        static void Step(DialogSession d)
        {
            var rep = d.World.Items.Report;
            if (_index >= rep.Lines.Count) { _active = false; d.Say(2081); return; }
            var line = rep.Lines[_index];
            d.Say(line.IntroId, Airline(d, line.Act.Victim));
        }

        static void Verdict(DialogSession d)
        {
            var line = Line(d);
            if (line.CommentId == 2073) d.Say(2073, Airline(d, line.Act.Perpetrator));
            else d.Say(line.CommentId);
        }
    }
}
