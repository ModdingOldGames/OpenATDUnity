using System.Globalization;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.Tutorial
{
    /// <summary>
    /// Auto-runner commands for scripted checks (<see cref="AutoRunner.Extra"/>): <c>save N</c> (write slot N),
    /// <c>tutorial</c> (log the tutorial state), <c>walk ROOM</c> (the figure walks to a room door),
    /// <c>skipmsg</c> (the assistant's current line ends), <c>planesel</c> (log the selected plane and the step).
    /// </summary>
    static class TutorialTestHooks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var previous = AutoRunner.Extra;
            AutoRunner.Extra = (app, parts) => Command(app, parts) ?? (previous != null ? previous(app, parts) : 0);
        }

        static int? Command(GameApp app, string[] parts)
        {
            var w = app.Session?.World;
            switch (parts[0])
            {
                case "save":
                    if (w == null) return 1;
                    Debug.Log("OpenAT test: save slot " + parts[1] + ": " + Session.SaveSlots.Save(app, int.Parse(parts[1], CultureInfo.InvariantCulture), "test " + parts[1]));
                    return 2;
                case "tutorial":
                    if (w != null) Debug.Log($"OpenAT test: tutorial active {w.Tutorial.Active} step {w.Tutorial.Step} monolog {w.Tutorial.Monolog} silent {app.Advisors.Silent} {w.Clock.ClockText} rooms {string.Join(",", app.Rooms.Locations)}");
                    return 1;
                case "walk":
                    if (w?.Hall != null) Debug.Log("OpenAT test: walk " + parts[1] + ": " + w.Execute(new Sim.Hall.WalkToRoomCommand(w.LocalPlayer, int.Parse(parts[1], CultureInfo.InvariantCulture))));
                    return 2;
                case "skipmsg":
                    app.Advisors.SkipCurrent();
                    return 1;
                case "talkto":
                    // Walk up to another airline's figure for a face-to-face talk.
                    if (w?.Hall != null) Debug.Log("OpenAT test: talk to " + parts[1] + ": " + w.Execute(new Sim.Hall.WalkToPlayerCommand(w.LocalPlayer, int.Parse(parts[1], CultureInfo.InvariantCulture))));
                    return 2;
                case "phone":
                    // "phone P [mobile]": call airline P from the phone book.
                    Rooms.WorldPhone.PlayerListMenu.CallCompetitor(app, int.Parse(parts[1], CultureInfo.InvariantCulture), parts.Length > 2);
                    return 2;
                case "call":
                    // "call T": phone dialog partner T (talker id, e.g. 8/9 the bank clerks) like the phone book does.
                    Debug.Log("OpenAT test: call " + parts[1] + ": " + app.Rooms.PhoneCall(int.Parse(parts[1], CultureInfo.InvariantCulture)));
                    return 2;
                case "bored":
                    if (w != null) w.Execute(new Sim.Rivals.RivalBoredCommand(int.Parse(parts[1], CultureInfo.InvariantCulture)));
                    return 1;
                case "strikeend":
                    // Pretend the local strike just ended by waiting (end type 3).
                    if (w != null) { w.Local.StrikeHours = 0; w.Local.StrikeNotified = false; w.Local.StrikeEndType = 3; }
                    return 1;
                case "fluege":
                    // Open the departures monitor on the hall's menu slot.
                    foreach (var sc in app.Screens.All)
                        if (sc is Airport.AirportScreen h && !h.Menus.IsOpen) h.Menus.Open(new Airport.FluegeMenu(), new Vector2Int(320, 220), 0.175f);
                    return 2;
                case "camto":
                    // "camto P": centre the hall camera on airline P's figure (phone-pose shots).
                    if (w?.Hall != null)
                        foreach (var sc in app.Screens.All)
                            if (sc is Airport.AirportScreen h && w.Hall.PlayerPerson(int.Parse(parts[1], CultureInfo.InvariantCulture)) is Sim.Hall.Person fig)
                                h.RestoreCamera(fig.ScreenPos.X - h.ViewWidth / 2, 0);
                    return 1;
                case "stop":
                    // The local figure stops where it is (a click on its own tile).
                    if (w?.Hall != null && w.Hall.PlayerPerson(w.LocalPlayer) is Sim.Hall.Person me)
                        w.Execute(new Sim.Hall.WalkToTileCommand(w.LocalPlayer, Sim.Hall.HallState.TileOfPosition(me.Position)));
                    return 1;
                case "hallcam":
                    foreach (var sc in app.Screens.All)
                        if (sc is Airport.AirportScreen h) h.RestoreCamera(int.Parse(parts[1], CultureInfo.InvariantCulture), 0);
                    return 1;
                case "worstad":
                    if (w != null) w.Execute(new Sim.Finance.AdCampaignCommand(int.Parse(parts[1], CultureInfo.InvariantCulture), Sim.Finance.AdType.Company, 0));
                    return 2;
                case "expand":
                    // Make the airport expandable now: all gates rented by us, day ≥ 8 is faked by the last expansion only.
                    if (w?.Hall != null)
                    {
                        for (int g = 0; g < 8; g++) if (!w.Local.Gates.HasGate(g)) w.Local.Gates.Rent(g, 1000);
                        Debug.Log("OpenAT test: expansion decision " + Sim.Hall.AirportExpansion.Decide(w));
                    }
                    return 1;
            }
            return null;
        }
    }
}
