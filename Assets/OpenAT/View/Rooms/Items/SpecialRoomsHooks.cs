using System.Globalization;
using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Boot;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.WorldPhone;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Items
{
    /// <summary>
    /// Wires the special rooms into shared UI hooks: the phone book's competitor / branch sub-lists
    /// (<see cref="FilofaxMenu.OpenList"/>) and test helpers for scripted runs (<see cref="AutoRunner.Extra"/>):
    /// <c>item N</c> (give an item), <c>trust N</c> (saboteur trust), <c>parts MASK</c> (rocket/station parts),
    /// <c>speed N</c> (game speed), <c>callday</c> (call it a day), <c>planes</c> (log the rivals' planes), <c>fax MODE</c>
    /// (prank letter), <c>wc ROOM</c> (toilet visit), <c>branch CITYINDEX</c> (own branch), <c>log</c>.
    /// </summary>
    static class SpecialRoomsHooks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FilofaxMenu.OpenList == null) FilofaxMenu.OpenList = (app, host, room) => PhoneBookLists.Open(app, host, room);
            var previous = AutoRunner.Extra;
            AutoRunner.Extra = (app, parts) => Command(app, parts) ?? (previous != null ? previous(app, parts) : 0);
        }

        static int? Command(GameApp app, string[] parts)
        {
            var s = app.EnsureSession();
            if (s == null) return null;
            var w = s.World;
            var p = s.Local;
            int Arg(int i) => int.Parse(parts[i], CultureInfo.InvariantCulture);
            switch (parts[0])
            {
                case "item": ItemEngine.Give(w, p, Arg(1), "test"); return 1;
                case "trust": p.Sabotage.Trust = Arg(1); return 1;
                case "parts": p.Counters.RocketFlags = Arg(1); return 1;
                case "speed": p.GameSpeed = Arg(1); return 1;
                case "callday": GameFlow.CallItADay(app); return 2;
                case "fax":
                    // A plane prank letter to the local player (shows the fax popup or the advisor line).
                    w.Events.Publish(new OpenAT.Sim.Fleet.LetterSent(p, "Lett", 500 + Arg(1), p.Planes.Count > 0 ? p.Planes.ToList()[0].Name : "?"));
                    return 2;
                case "wc":
                    // The toilet pause is held and timed by the hall (HallPlayer.OnToilet); the view only blocks input.
                    if (w.Hall != null) { w.Hall.Players[p.Index].OnToilet = Arg(1); w.Hall.Players[p.Index].OnToiletTicks = 0; }
                    w.Events.Publish(new OpenAT.Sim.Hall.HallRequest(p.Index, OpenAT.Sim.Hall.HallRequest.Toilet, Arg(1)));
                    return 2;
                case "branch":
                    if (Arg(1) < p.RentCities.Length) p.RentCities[Arg(1)].Rank = 1;
                    return 1;
                case "planes":
                    for (int c = 0; c < Player.Count; c++)
                        foreach (var pl in w.Players[c].Planes.ToList())
                            Debug.Log($"OpenAT test: p{c} plane {pl.Id} {pl.Name} at {pl.Location}");
                    return 1;
                case "log":
                    Debug.Log($"OpenAT test: day {w.Clock.Date} {w.Clock.ClockText} money {p.Money} trust {p.Sabotage.Trust} pending {p.Sabotage.PlaneMode}/{p.Sabotage.PersonMode}/{p.Sabotage.SpecialMode} hints {p.Sabotage.Hints} security {p.SecurityFlags:X} parts {p.Counters.RocketFlags:X} acts {w.Items.Acts.Count} report {w.Items.Report.Lines.Count} rooms {string.Join(",", app.Rooms.Locations)}");
                    for (int c = 0; c < Player.Count; c++)
                        Debug.Log($"OpenAT test: p{c} {w.Players[c].Airline} money {w.Players[c].Money} sick {w.Players[c].Sick} phones {w.Players[c].PhonesDown} office {w.Players[c].OfficeState}");
                    return 1;
            }
            return null;
        }
    }
}
