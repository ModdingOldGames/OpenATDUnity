using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.AI;
using OpenAT.Sim.Core;
using OpenAT.Sim.Hall;
using OpenAT.View.Boot;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Menus;
using UnityEngine;
using HallIds = OpenAT.Sim.Hall.RoomIds;

namespace OpenAT.View.Rooms.Framework
{
    /// <summary>
    /// Walk-to-room hotkeys (rooms-and-ui-1 §7.2, rooms-and-ui-9 §6.2): one letter per location, in the hall and in
    /// every room. A key leaves all rooms and sends the figure walking to the location; the same key twice within
    /// 300 ms makes it run (unless stuck). The letters depend on the language (German/Dutch, Portuguese, all others).
    /// Active only when <see cref="KeysAllowed"/> holds (after 09:00 while the day is played, hours 8–17, not after
    /// call-it-a-day, <b>not while the tutorial runs</b>, no dialog or menu in the window, not on the stairs, not in
    /// the evening screen, not walking up to another player). Esc, Tab, F2, F3 and F4 share the same gate
    /// (<see cref="GlobalKeysAllowed"/>); F2 has a second, looser route through the room window
    /// (<see cref="RoomF2Allowed"/>: no dialog or menu, after 09:00), so it also works in the tutorial and the
    /// evening screen, as in the original.
    /// </summary>
    public static class RoomHotkeys
    {
        /// <summary>Key tables of the original.</summary>
        public enum Layout { GermanDutch, Portuguese, Other }

        /// <summary>Pseudo-targets: the own office / personnel office (resolved per player) and the telescope (per mission).</summary>
        public const int OwnOffice = HallIds.OfficeA, OwnPersonnel = HallIds.PersonnelA, Telescope = HallIds.Rushmore;

        static readonly Dictionary<char, int> German = new Dictionary<char, int>
        {
            ['T'] = HallIds.Reisebuero, ['B'] = OwnOffice, ['C'] = HallIds.Ricks, ['D'] = HallIds.Shop1, ['E'] = HallIds.Werbung,
            ['G'] = RoomIds.Globe, ['U'] = HallIds.Aufsicht, ['S'] = Telescope, ['I'] = HallIds.Kiosk, ['L'] = HallIds.LastMinute,
            ['M'] = HallIds.Museum, ['N'] = HallIds.Nasa, ['P'] = OwnPersonnel, ['R'] = HallIds.RouteBox, ['F'] = HallIds.Makler,
            ['K'] = HallIds.Bank, ['W'] = HallIds.Werkstatt, ['A'] = HallIds.ArabAir, ['H'] = HallIds.Fracht, ['X'] = HallIds.Designer,
            ['Y'] = HallIds.Security,
        };

        static readonly Dictionary<char, int> Portuguese = new Dictionary<char, int>
        {
            ['T'] = HallIds.Reisebuero, ['O'] = OwnOffice, ['C'] = HallIds.Ricks, ['D'] = HallIds.Shop1, ['I'] = HallIds.Werbung,
            ['G'] = RoomIds.Globe, ['U'] = HallIds.Aufsicht, ['S'] = Telescope, ['K'] = HallIds.Kiosk, ['L'] = HallIds.LastMinute,
            ['M'] = HallIds.Museum, ['N'] = HallIds.Nasa, ['P'] = OwnPersonnel, ['R'] = HallIds.RouteBox, ['E'] = HallIds.Makler,
            ['B'] = HallIds.Bank, ['W'] = HallIds.Werkstatt, ['A'] = HallIds.ArabAir, ['H'] = HallIds.Fracht,
        };

        static readonly Dictionary<char, int> Other = new Dictionary<char, int>
        {
            ['T'] = HallIds.Reisebuero, ['B'] = HallIds.Bank, ['C'] = HallIds.Ricks, ['D'] = HallIds.Shop1, ['E'] = HallIds.Makler,
            ['G'] = RoomIds.Globe, ['U'] = HallIds.Aufsicht, ['S'] = Telescope, ['O'] = OwnOffice, ['L'] = HallIds.LastMinute,
            ['M'] = HallIds.Museum, ['N'] = HallIds.Nasa, ['P'] = OwnPersonnel, ['R'] = HallIds.RouteBox, ['I'] = HallIds.Werbung,
            ['K'] = HallIds.Kiosk, ['W'] = HallIds.Werkstatt, ['A'] = HallIds.ArabAir, ['H'] = HallIds.Fracht, ['X'] = HallIds.Designer,
            ['Y'] = HallIds.Security,
        };

        /// <summary>German and Dutch installs share one table, Portuguese has its own, every other language uses the English one.</summary>
        public static Layout LayoutOf(TextResource.Language lang) =>
            lang == TextResource.Language.German || lang == TextResource.Language.Dutch ? Layout.GermanDutch
            : lang == TextResource.Language.Portuguese ? Layout.Portuguese : Layout.Other;

        /// <summary>Location of a key (upper case) in a layout, 0 = none. Office/personnel/telescope are pseudo-targets.</summary>
        public static int Target(char key, Layout layout)
        {
            var t = layout == Layout.GermanDutch ? German : layout == Layout.Portuguese ? Portuguese : Other;
            return t.TryGetValue(char.ToUpperInvariant(key), out int r) ? r : 0;
        }

        /// <summary>The tutorial switches off the global keys (Esc, Tab, F3, F4) and the room hotkeys.</summary>
        public static bool TutorialBlocksGlobalKeys(World w) => w != null && OpenAT.Sim.Tutorial.TutorialEngine.Running(w);

        /// <summary>
        /// Gate of Esc/F2 (options), F3 (load page), F4 (save page) and Tab (call it a day): the same activity gate as
        /// the room hotkeys (rooms-and-ui-1 §7.2). Without a game (a room opened directly) the keys stay free.
        /// </summary>
        public static bool GlobalKeysAllowed(GameApp app, World w, bool dialogOrMenuOpen) =>
            w == null ? !dialogOrMenuOpen : KeysAllowed(app, w, dialogOrMenuOpen);

        /// <summary>The room window's own F2: no dialog or menu open and after 09:00, without the tutorial check (§7.1).</summary>
        public static bool RoomF2Allowed(World w, bool dialogOrMenuOpen) =>
            !dialogOrMenuOpen && (w == null || w.Clock.Time > GameClock.DayStart);

        /// <summary>
        /// Esc/F2/F3/F4 of a room or the hall: opens the options (on the load/save page for F3/F4) when the gates allow it.
        /// True when the options were opened.
        /// </summary>
        public static bool HandleOptionKeys(GameApp app, InputState input, bool dialogOrMenuOpen)
        {
            if (!input.Escape && !input.F2 && !input.F3 && !input.F4) return false;
            var w = app.Session?.World;
            bool global = GlobalKeysAllowed(app, w, dialogOrMenuOpen);
            if (input.F2 && (global || RoomF2Allowed(w, dialogOrMenuOpen))) { app.OpenOptions(); return true; }
            if (!global) return false;
            if (input.Escape) { app.OpenOptions(); return true; }
            if (input.F3) { app.OpenOptions(OptionsPage.Load); return true; }
            if (input.F4) { app.OpenOptions(OptionsPage.Save); return true; }
            return false;
        }

        /// <summary>The activity gate of the room hotkeys (rooms-and-ui-1 §7.2).</summary>
        public static bool KeysAllowed(GameApp app, World w, bool dialogOrMenuOpen)
        {
            if (w == null || dialogOrMenuOpen) return false;
            var c = w.Clock;
            if (c.Time <= GameClock.DayStart || c.State != DayState.Playing || c.Hour < 8 || c.Hour >= 18) return false;
            if (w.CallItADay || w.Local.CalledItADay || TutorialBlocksGlobalKeys(w)) return false;
            if (app.Rooms.CurrentRoom == RoomIds.Abend || app.Rooms.CurrentRoom == RoomIds.Options) return false;
            if (w.Hall != null)
            {
                var hp = w.Hall.Players[w.LocalPlayer];
                var me = w.Hall.PlayerPerson(w.LocalPlayer);
                if (hp.IsWalkingToPlayer != -1) return false;
                if (me != null && HallIds.IsStairs(me.StatePar)) return false;
            }
            return true;
        }

        static char _lastKey;
        static float _lastKeyTime = -10;

        /// <summary>Handles the typed letters of this frame; true when one sent the player walking.</summary>
        public static bool Handle(GameApp app, InputState input, bool dialogOrMenuOpen)
        {
            if (input.Typed.Count == 0) return false;
            var w = app.Session?.World;
            var layout = LayoutOf(app.Assets.Language);
            bool handled = false;
            foreach (char ch in input.Typed)
            {
                char key = char.ToUpperInvariant(ch);
                int target = Target(key, layout);
                if (target == 0) continue;
                if (!KeysAllowed(app, w, dialogOrMenuOpen)) continue;
                // Same key again within 300 ms: run.
                float now = Time.unscaledTime;
                bool run = key == _lastKey && now - _lastKeyTime < 0.3f;
                _lastKey = key;
                _lastKeyTime = now;
                if (target == HallIds.Werbung && input.Shift) continue;
                if (Go(app, w, target, run)) handled = true;
            }
            return handled;
        }

        static bool InQueue(GameApp app, int room)
        {
            foreach (int l in app.Rooms.Locations) if (l == room) return true;
            return false;
        }

        /// <summary>Resolves the pseudo-targets and the mission/feature conditions, then walks (false = nothing happened).</summary>
        static bool Go(GameApp app, World w, int target, bool run)
        {
            int local = w.LocalPlayer, diff = w.Difficulty;
            var hosts = app.Rooms;
            switch (target)
            {
                case HallIds.Reisebuero:
                case HallIds.LastMinute:
                    if (!w.Rules.Get<IRobotFeatures>().Has(w, RobotFeature.TravelHolding)) return false;
                    break;
                case HallIds.Nasa:
                    if (diff != Mission.Rocket && diff != Mission.AddOnLast) return false;
                    break;
                case HallIds.RouteBox:
                case HallIds.ArabAir:
                    if (diff == Mission.Tutorial) return false;
                    break;
                case HallIds.Fracht:
                    if (diff < Mission.AddOnFirst + 1 && diff != Mission.FreeGame) return false;
                    break;
                case HallIds.Designer:
                case HallIds.Security:
                    if (w.Hall != null && !w.Hall.Layout.HasRune(RuneId.ToShop, target)) return false;
                    break;
                case Telescope:
                    if (InQueue(app, HallIds.Rushmore) || InQueue(app, HallIds.Insel) || InQueue(app, HallIds.Weltall)) return false;
                    target = diff == Mission.Rocket ? HallIds.Insel : diff == Mission.AddOnLast ? HallIds.Weltall : HallIds.Rushmore;
                    break;
                case OwnOffice:
                    target = RoomIds.OfficeOf(local);
                    if (InQueue(app, target)) return false;
                    hosts.WalkToGlobe = false;
                    break;
                case OwnPersonnel:
                    target = RoomIds.PersonalOf(local);
                    break;
                case RoomIds.Globe:
                    if (InQueue(app, RoomIds.Globe)) return false;
                    if (hosts.CurrentRoom == RoomIds.OfficeOf(local)) return hosts.Enter(RoomIds.Globe);
                    hosts.WalkToGlobe = true;
                    target = RoomIds.OfficeOf(local);
                    break;
            }
            if (InQueue(app, target)) return false;
            Walk(app, w, target, run);
            return true;
        }

        /// <summary>WalkToRoom + LeaveAllRooms; with the hall the figure walks, without one the room is entered.</summary>
        static void Walk(GameApp app, World w, int room, bool run)
        {
            var h = w.Hall;
            if (h == null) { app.Rooms.WalkTo(room); return; }
            int local = w.LocalPlayer;
            int closed = h.ClosedMenu(room);
            bool stuck = h.Players[local].IsStuck > 0;
            var r = w.Execute(new WalkToRoomCommand(local, room, run && !stuck));
            app.Rooms.LeaveAll();
            Debug.Log("OpenAT hotkey: walk to " + room + (run ? " (run)" : "") + ": " + r);
            // A closed shop answers with its "closed" note in the hall (the walk request's closed menu).
            if (!r.Ok && closed >= 0 && OpenAT.View.Airport.AirportScreen.Shown?.Menus is MenuHost menus && !menus.IsOpen)
                menus.Open(new ClosedMenu(closed), new Vector2Int(320, 220), 0.17f);
        }
    }
}
