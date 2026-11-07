using System;
using System.Runtime.CompilerServices;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>
    /// Registers the hall with the world machinery. Call <see cref="Register"/> once per process before
    /// worlds are created (the Unity boot does it from View/Boot/HallBoot.cs; tests call it directly).
    /// It appends factories to <see cref="World.SystemFactories"/> and <see cref="World.SetupStepFactories"/>;
    /// calling it again does nothing.
    ///
    /// Hall content (layout files, brick/clan tables) comes from the install, which the Sim does not
    /// know. Attach it to the world's <see cref="Data.GameData"/> with <see cref="HallContent.Attach"/>, or
    /// set <see cref="HallContent.Resolver"/>; worlds without content simply have no hall.
    /// </summary>
    public static class HallModule
    {
        static bool _registered;
        static readonly object Lock = new object();

        public static void Register()
        {
            lock (Lock)
            {
                if (_registered) return;
                _registered = true;
                World.SystemFactories.Add(() => new HallSystem());
                World.SystemFactories.Add(() => new HallNightSystem());
                World.SystemFactories.Add(() => new HallNewDaySystem());
                World.SystemFactories.Add(() => new AirportExpansionSystem());
                World.SetupStepFactories.Add(() => new HallSetup());
                // Hall <-> operations (HallFlightLink.cs, HallPlanes.cs): plane animation, flight displays, gate count.
                World.SystemFactories.Add(() => new HallPlaneSystem());
                World.SystemFactories.Add(() => new HallFlightDisplaySystem());
                World.RuleFactories.Add(HallFlights.RegisterRules);
            }
        }

        public static bool IsRegistered => _registered;

        /// <summary>Creates the hall of a world now (e.g. for a world created before the content was attached).
        /// Also adds the hall systems if the world was created before <see cref="Register"/>.</summary>
        public static HallState EnsureHall(World w, HallData data = null)
        {
            if (w.Hall != null) return w.Hall;
            data = data ?? HallContent.For(w.Data);
            if (data == null) return null;
            if (w.Systems.Find(HallSystem.SystemName) == null)
            {
                w.Systems.Add(new HallSystem());
                w.Systems.Add(new HallNightSystem());
                w.Systems.Add(new HallNewDaySystem());
                w.Systems.Add(new AirportExpansionSystem());
            }
            HallFlights.AddSystems(w);
            HallFlights.RegisterRules(w);
            HallSetup.Create(w, data, firstDay: true);
            return w.Hall;
        }
    }

    /// <summary>Static hall content per <see cref="Data.GameData"/> (content, not game state).</summary>
    public static class HallContent
    {
        static readonly ConditionalWeakTable<Data.GameData, HallData> Table = new ConditionalWeakTable<Data.GameData, HallData>();

        /// <summary>Optional fallback that loads content for game data that has none attached (set by the Unity boot).</summary>
        public static Func<Data.GameData, HallData> Resolver;

        public static void Attach(Data.GameData data, HallData hall)
        {
            Table.Remove(data);
            Table.Add(data, hall);
        }

        public static HallData For(Data.GameData data)
        {
            if (data == null) return null;
            if (Table.TryGetValue(data, out var h)) return h;
            var r = Resolver?.Invoke(data);
            if (r != null) Attach(data, r);
            return r;
        }
    }

    /// <summary>
    /// New-game step: pedestrian streams seeded with the date (sim-core §4.2), the first-day clan
    /// rotation, the level-1 layout (§2.2 "Levels"), and the hall's new day (players at PCREATION).
    /// </summary>
    public sealed class HallSetup : ISetupStep
    {
        public string Name => "hall";
        public int Order => 900;

        public void Apply(World w, NewGameSettings s)
        {
            var data = HallContent.For(w.Data);
            if (data == null) return;
            Create(w, data, firstDay: true);
        }

        internal static void Create(World w, HallData data, bool firstDay)
        {
            var layout = new HallLayout(data, w.Difficulty, HallLayout.DefaultLevels());
            var h = new HallState(w, layout);
            h.Flights = HallFlights.Factory?.Invoke(w);
            w.Hall = h;
            w.PersonRandCreate.SetSeed(w.Clock.Date);
            w.PersonRandMisc.SetSeed(w.Clock.Date);
            h.UpdateClansInGame(firstDay);
            h.NewDay();
        }
    }

    // ---------------------------------------------------------------------- commands

    /// <summary>Click in the hall at hall pixel coordinates (screen + camera): room or floor (§6.4).</summary>
    public sealed class WalkToClickCommand : ICommand<World>
    {
        public readonly int Player;
        public readonly Pt Click;
        /// <summary>A double click makes the figure run.</summary>
        public readonly bool Run;
        public WalkToClickCommand(int player, Pt click, bool run = false) { Player = player; Click = click; Run = run; }

        public CommandResult Validate(World w)
        {
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= global::OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("hall.player");
            // The original only ignores clicks while the time stands still (bNoTime: the morning briefing);
            // 09:00 itself, right after the briefing has been left, is already a valid click time.
            if (w.Clock.Time < GameClock.DayStart) return CommandResult.Fail("hall.before-nine");
            if (w.Clock.Frozen) return CommandResult.Fail("hall.no-time");
            if (OpenAT.Sim.Tutorial.TutorialEngine.HallClicksBlocked(w)) return CommandResult.Fail("hall.tutorial");
            var hp = w.Hall.Players[Player];
            if (!hp.InHall) return CommandResult.Fail("hall.in-room");
            if (hp.IsStuck > 0 || hp.RunningToToilet) return CommandResult.Fail("hall.busy");
            var me = w.Hall.PlayerPerson(Player);
            if (me == null) return CommandResult.Fail("hall.no-figure");
            if (RoomIds.IsStairs(me.StatePar)) return CommandResult.Fail("hall.on-stairs");
            if (me.StatePar != 0) return CommandResult.Fail("hall.auto-walking");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var h = w.Hall;
            var me = h.PlayerPerson(Player);
            if (h.Players[Player].TalkPartner != 0) new EndTalkCommand(Player).Apply(w);   // walking away ends a pending talk
            me.Running = Run;
            if (me.LookDir == 9)
            {
                me.Dir = 8;
                me.LookDir = h.Clan(me.ClanId)?.GimmickArt2 ?? 2;
                me.Phase = 0;
            }
            h.WalkToClick(Player, Click);
        }
    }

    /// <summary>Walk to a room's door/counter (hotkeys, filofax, AI).</summary>
    public sealed class WalkToRoomCommand : ICommand<World>
    {
        public readonly int Player, Room;
        public readonly bool Run;
        public WalkToRoomCommand(int player, int room, bool run = false) { Player = player; Room = room; Run = run; }

        public CommandResult Validate(World w)
        {
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= global::OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("hall.player");
            if (!w.Hall.Layout.HasRune(RuneId.ToShop, Room)) return CommandResult.Fail("hall.no-such-room");
            if (w.Hall.ClosedMenu(Room) >= 0) return CommandResult.Fail("hall.closed");
            return CommandResult.Success;
        }

        public void Apply(World w)
        {
            var me = w.Hall.PlayerPerson(Player);
            if (me != null && Run) me.Running = true;
            w.Hall.WalkToRoom(Player, Room);
        }
    }

    /// <summary>Walk to a grid tile (col, row 0..15).</summary>
    public sealed class WalkToTileCommand : ICommand<World>
    {
        public readonly int Player;
        public readonly Pt Tile;
        public WalkToTileCommand(int player, Pt tile) { Player = player; Tile = tile; }

        public CommandResult Validate(World w)
        {
            if (w.Hall == null) return CommandResult.Fail("hall.none");
            if (Player < 0 || Player >= global::OpenAT.Sim.Player.Count || w.Players[Player].IsOut) return CommandResult.Fail("hall.player");
            if (Tile.Y < 0 || Tile.Y > 15) return CommandResult.Fail("hall.tile");
            return CommandResult.Success;
        }

        public void Apply(World w) => w.Hall.WalkToTile(Player, Tile);
    }

    /// <summary>The room framework reports that a player left its room and is back in the hall.</summary>
    public sealed class ReturnToHallCommand : ICommand<World>
    {
        public readonly int Player;
        public ReturnToHallCommand(int player) => Player = player;
        public CommandResult Validate(World w) => w.Hall == null ? CommandResult.Fail("hall.none") : CommandResult.Success;
        public void Apply(World w) => w.Hall.ReturnToHall(Player);
    }
}
