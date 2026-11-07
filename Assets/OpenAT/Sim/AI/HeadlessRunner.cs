using System;
using System.Collections.Generic;
using System.Text;
using OpenAT.Sim.Core;
using OpenAT.Sim.Missions;

namespace OpenAT.Sim.AI
{
    /// <summary>One airline's state at the start of a day (after the night's bookings).</summary>
    public struct AirlineDayRow
    {
        public int Player;
        public long Money, Credit;
        public int Planes, Flights, Contracts, Freights, RouteLegs, Branches, Image, Actions;
        public double SharePrice;
        public bool RouteMode, WantsRoutes, Out;

        public string Mode => Out ? "out" : RouteMode ? "routes" : WantsRoutes ? "wants" : "orders";
    }

    /// <summary>
    /// Runs a game without a view (the `openat sim` command and the AI tests): every airline a computer by
    /// default, the clock driven tick by tick, the morning briefing left at once. One row per airline per day.
    /// </summary>
    public sealed class HeadlessRunner
    {
        public readonly World World;
        public readonly List<AirlineDayRow[]> Days = new List<AirlineDayRow[]>();
        readonly int[] _flightsAtMorning = new int[Player.Count];
        readonly int[] _actionsAtMorning = new int[Player.Count];

        /// <summary>Mission winners in order (date, airline); the run goes on after a win (the sim keeps working).</summary>
        public readonly List<(int Date, int Player)> Winners = new List<(int, int)>();
        /// <summary>Airlines that left the game (date, airline, reason).</summary>
        public readonly List<(int Date, int Player, OutReason Reason)> Outs = new List<(int, int, OutReason)>();

        public HeadlessRunner(World w)
        {
            World = w;
            w.Events.Subscribe<MissionWon>(e => Winners.Add((w.Clock.Date, e.Player.Index)));
            w.Events.Subscribe<PlayerOut>(e => Outs.Add((w.Clock.Date, e.Player.Index, e.Reason)));
            Mark();
        }

        /// <summary>A new game with the given owners (null = all computers).</summary>
        public static HeadlessRunner NewGame(Data.GameData data, int difficulty, int seed, Owner[] owners = null, GameOptions options = null)
        {
            var o = owners ?? new[] { Owner.Computer, Owner.Computer, Owner.Computer, Owner.Computer };
            var s = new NewGameSettings { Difficulty = difficulty, Seed = seed, Owners = o, LocalPlayer = 0 };
            return new HeadlessRunner(World.NewGame(data, s, options));
        }

        void Mark()
        {
            for (int c = 0; c < Player.Count; c++)
            {
                _flightsAtMorning[c] = World.AI.FlightsTotal[c];
                _actionsAtMorning[c] = World.Players[c].Robot.ActionsExecuted;
            }
        }

        /// <summary>Plays one working day and the following night up to the next 09:00 briefing; records a row set.</summary>
        public AirlineDayRow[] RunDay()
        {
            var w = World;
            int start = w.Clock.Date;
            long guard = 0;
            while (!(w.Clock.Date > start && w.Clock.State == DayState.Morning) && guard++ < 200000)
            {
                if (w.Clock.State == DayState.Morning && w.Clock.Frozen) w.Execute(new StartWorkingDayCommand());
                w.Tick();
            }
            var rows = Snapshot();
            Days.Add(rows);
            Mark();
            return rows;
        }

        public void RunDays(int n, Action<int, AirlineDayRow[]> onDay = null)
        {
            for (int d = 0; d < n; d++)
            {
                var rows = RunDay();
                onDay?.Invoke(World.Clock.Date - 1, rows);
            }
        }

        public AirlineDayRow[] Snapshot()
        {
            var w = World;
            var rows = new AirlineDayRow[Player.Count];
            for (int c = 0; c < Player.Count; c++)
            {
                var p = w.Players[c];
                int branches = 0;
                foreach (var b in p.RentCities) if (b.Rank != 0) branches++;
                rows[c] = new AirlineDayRow
                {
                    Player = c, Money = p.Money, Credit = p.Credit, Planes = p.Planes.Count,
                    Flights = w.AI.FlightsTotal[c] - _flightsAtMorning[c], Contracts = p.Contracts.Count, Freights = p.Freights.Count,
                    RouteLegs = p.LicensedRouteCount, Branches = branches, Image = p.Image, SharePrice = p.SharePrice,
                    Actions = p.Robot.ActionsExecuted - _actionsAtMorning[c],
                    RouteMode = p.Robot.RouteMode, WantsRoutes = p.Robot.WantsRoutes, Out = p.IsOut,
                };
            }
            return rows;
        }

        public static string Header() =>
            "day | P | money      | credit    | pl | flt | ctr | frt | legs | img  | share  | act | mode";

        public static string Format(int day, AirlineDayRow r) =>
            string.Format("{0,3} | {1} | {2,10} | {3,9} | {4,2} | {5,3} | {6,3} | {7,3} | {8,4} | {9,4} | {10,6:0.0} | {11,3} | {12}",
                day, r.Player, r.Money, r.Credit, r.Planes, r.Flights, r.Contracts, r.Freights, r.RouteLegs, r.Image, r.SharePrice, r.Actions, r.Mode);

        /// <summary>The whole run as a table.</summary>
        public string Table()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header());
            for (int d = 0; d < Days.Count; d++)
                foreach (var r in Days[d]) sb.AppendLine(Format(d, r));
            return sb.ToString();
        }
    }
}
