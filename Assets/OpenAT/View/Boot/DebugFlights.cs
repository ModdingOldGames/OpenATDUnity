using System;
using System.Collections.Generic;
using System.Globalization;
using OpenAT.Sim;
using OpenAT.Sim.Fleet;
using OpenAT.Sim.Routes;
using UnityEngine;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Debug switches for looking at flights in the hall without playing to them (screenshots, manual
    /// checks). All are command-line flags and change nothing unless given:
    /// <list type="bullet">
    /// <item><c>-openat-debug-autoplan [human|all]</c> — at new game, rents a short home route for the
    /// human airline (or every airline with <c>all</c>) and plans each starting plane out and back,
    /// the first plane leaving at 11:00, the next ones two hours apart (route flights with a gate, so
    /// passengers spawn from 10:00 and the planes come to the gates at :30).</item>
    /// <item><c>-openat-debug-advance &lt;minutes&gt;</c> — when the hall is first shown, runs the
    /// simulation that many game minutes ahead at once (deterministic ticks, nothing is skipped).</item>
    /// <item><c>-openat-debug-speed &lt;0..3&gt;</c> — sets the local player's game speed.</item>
    /// <item><c>-openat-debug-camera &lt;x&gt;</c> — puts the hall camera's left edge at hall x once
    /// (manual scroll mode, so it stays there; e.g. 1000 for the check-in, 4500 for the gates).</item>
    /// </list>
    /// Example: <c>-openat-screen hall -openat-debug-autoplan all -openat-debug-advance 95</c> shows
    /// passengers checking in for the 11:00 departures at 10:35.
    /// </summary>
    public static class DebugFlights
    {
        const string AutoPlanFlag = "-openat-debug-autoplan", AdvanceFlag = "-openat-debug-advance", SpeedFlag = "-openat-debug-speed",
            CameraFlag = "-openat-debug-camera";

        /// <summary>Hall camera x from <c>-openat-debug-camera</c>, or null.</summary>
        public static int? CameraX =>
            int.TryParse(Value(CameraFlag), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : (int?)null;
        static GameSession _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (!Has(AutoPlanFlag)) return;
            string mode = Value(AutoPlanFlag);
            bool all = string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase);
            World.SetupStepFactories.Add(() => new AutoPlanStep(all));
            Debug.Log("OpenAT debug: auto-planning flights for " + (all ? "every airline" : "the human airlines"));
        }

        /// <summary>Called by the hall view when it draws; applies the advance/speed flags once per session.</summary>
        public static void HallShown(GameApp app)
        {
            var session = app?.Session;
            if (session == null || session == _applied) return;
            _applied = session;
            var w = session.World;
            if (int.TryParse(Value(SpeedFlag), NumberStyles.Integer, CultureInfo.InvariantCulture, out int speed))
                w.Local.GameSpeed = Mathf.Clamp(speed, 0, 3);
            if (int.TryParse(Value(AdvanceFlag), NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) && minutes > 0)
            {
                long target = w.AbsoluteMinute + minutes;
                int guard = 0;
                while (w.AbsoluteMinute < target && guard++ < 200000)
                {
                    if (w.Clock.Frozen) w.StartWorkingDay();
                    w.Tick();
                }
                Debug.Log("OpenAT debug: advanced to day " + w.Clock.Date + " " + w.Clock.ClockText);
            }
        }

        /// <summary>The new-game step behind <c>-openat-debug-autoplan</c> (after the hall, order 950).</summary>
        public sealed class AutoPlanStep : ISetupStep
        {
            readonly bool _all;
            public AutoPlanStep(bool all) { _all = all; }
            public string Name => "debug.autoplan";
            public int Order => 950;

            public void Apply(World w, NewGameSettings s)
            {
                int firstHour = 11;
                foreach (var p in w.Players)
                {
                    if (p.IsOut || (!_all && p.IsComputer)) continue;
                    var planes = p.Planes.ToList();
                    if (planes.Count == 0) continue;
                    int ri = RentShortHomeRoute(w, p, planes[0]);
                    if (ri < 0) { Debug.LogWarning("OpenAT debug: no home route for " + p.Airline); continue; }
                    var route = w.RouteTable[ri];
                    int back = w.ReverseRouteIndex(ri);
                    int hour = firstHour;
                    foreach (var pl in planes)
                    {
                        if (hour > 20) break;
                        var r1 = w.Execute(new PlaceFlightCommand(p.Index, pl.Id, FlightKind.Route, route.Id, w.Clock.Date, hour));
                        int dur = FlightPlanner.Duration(w, pl, route.From, route.To);
                        if (r1.Ok && back >= 0)
                            w.Execute(new PlaceFlightCommand(p.Index, pl.Id, FlightKind.Route, w.RouteTable[back].Id, w.Clock.Date, hour + dur + 1));
                        Debug.Log("OpenAT debug: " + p.Airline + " " + pl.Name + " " + w.City(route.From).Name + "-" + w.City(route.To).Name
                                  + " at " + hour + ":00 (" + (r1.Ok ? "ok" : r1.ToString()) + ")");
                        hour += 2;
                    }
                    firstHour++; // airlines one hour apart, so the boards change during the morning
                }
            }

            /// <summary>The shortest licensed or rentable route from home that the plane can fly (index or −1).</summary>
            static int RentShortHomeRoute(World w, Player p, OpenAT.Sim.Fleet.Plane pl)
            {
                var cand = new List<int>();
                for (int i = 0; i < w.RouteTable.Count; i++)
                    if (w.RouteTable[i].From == w.HomeCity && w.Distance(w.RouteTable[i].From, w.RouteTable[i].To) <= pl.Range * 1000) cand.Add(i);
                cand.Sort((a, b) =>
                {
                    int c = w.Distance(w.RouteTable[a].From, w.RouteTable[a].To).CompareTo(w.Distance(w.RouteTable[b].From, w.RouteTable[b].To));
                    return c != 0 ? c : a.CompareTo(b);
                });
                foreach (int i in cand)
                {
                    if (p.RentRoutes[i].Licensed) return i;
                    if (w.Execute(new RentRouteCommand(p.Index, i)).Ok) return i;
                }
                return -1;
            }
        }

        static bool Has(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Value(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith("-")) return args[i + 1];
            return null;
        }
    }
}
