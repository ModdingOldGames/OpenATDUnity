using System;
using System.Collections.Generic;
using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Missions;
using OpenAT.Sim.Save;
using OpenAT.View.Boot;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Session
{
    /// <summary>
    /// Save-game slots on disk (rooms-and-ui-4 §4.3, sim-core.md §5): <c>Game0.sav</c> .. <c>Game11.sav</c> in
    /// <c>Application.persistentDataPath/saves</c> (or the folder given with <c>-openat-savedir DIR</c>; scripted <c>-openat-auto</c> runs default to <c>saves-auto</c>); slot
    /// 11 is the autosave. Saving records the room stack first; loading builds a fresh session from the file,
    /// shows the airport and re-enters the saved top room (§5.3). The hall link (UiBusy / IsRoomBusy, walk
    /// handler), the day loop, the advisors and this module's own subscriptions re-bind by themselves when
    /// <see cref="GameApp.Session"/> changes.
    /// </summary>
    public static class SaveSlots
    {
        public const int Count = SaveGame.SlotCount, Autosave = SaveGame.AutosaveSlot;
        /// <summary>Slot names: max 20 characters (A-Z, Ä Ö Ü, digits, space, '-', '+', '.').</summary>
        public const int MaxNameLength = 20;

        static string _folder;

        public static string Folder
        {
            get
            {
                if (_folder != null) return _folder;
                string arg = null;
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], "-openat-savedir", StringComparison.OrdinalIgnoreCase)) arg = a[i + 1];
                // Scripted runs (-openat-auto) never touch the player's own saves unless a folder is given.
                bool scripted = Array.Exists(a, s => string.Equals(s, "-openat-auto", StringComparison.OrdinalIgnoreCase));
                _folder = arg ?? Path.Combine(Application.persistentDataPath, scripted ? "saves-auto" : "saves");
                return _folder;
            }
        }

        public static string PathOf(int slot) => Path.Combine(Folder, SaveGame.SlotFileName(slot));

        /// <summary>Header of a slot, or null when the slot is empty / not readable.</summary>
        public static SaveHeader Header(int slot) => SaveGame.TryReadHeaderFile(PathOf(slot));

        public static bool IsAllowedNameChar(char c) =>
            (c >= 'A' && c <= 'Z') || c == 'Ä' || c == 'Ö' || c == 'Ü' || (c >= '0' && c <= '9') || c == ' ' || c == '-' || c == '+' || c == '.';

        /// <summary>
        /// The info line of the load page tooltip (rooms-and-ui-4 §4.3): <c>Misc</c> 1150 for missions (mission
        /// number, weekday, day, month, airline) or 1151 for a free game (weekday, day, month, airline).
        /// </summary>
        public static string InfoText(GameAssets assets, SaveHeader h)
        {
            if (h == null || assets == null) return null;
            string weekday = assets.StdText.GetOr("Schd", 3010 + h.Weekday);
            if (h.Mission == Mission.FreeGame)
                return DialogText.Format(assets.StdText.GetOr("Misc", 1151), new object[] { weekday, h.Day, h.Month, h.Airline });
            return DialogText.Format(assets.StdText.GetOr("Misc", 1150), new object[] { MissionProgress.NumberInCampaign(h.Mission), weekday, h.Day, h.Month, h.Airline });
        }

        // ------------------------------------------------------------------ save

        /// <summary>Saves the running game into a slot. Returns false (and logs) on failure.</summary>
        public static bool Save(GameApp app, int slot, string name)
        {
            var w = app?.Session?.World;
            if (w == null || slot < 0 || slot >= Count) return false;
            try
            {
                var stack = new List<int>();
                foreach (int r in app.Rooms.Locations) if (r != RoomIds.Airport && r != RoomIds.Options && stack.Count < RecordRoomStackCommand.MaxDepth) stack.Add(r);
                int? camX = null, camY = null;
                foreach (var s in app.Screens.All) if (s is Airport.AirportScreen hall) { camX = hall.ViewX; camY = hall.ViewY; }
                w.Execute(new RecordRoomStackCommand(w.LocalPlayer, stack.ToArray(), camX, camY));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SaveGame.WriteFile(w, PathOf(slot), (name ?? "").Trim());
                Debug.Log("OpenAT save: slot " + slot + " '" + name + "' day " + w.Clock.Date + " " + w.Clock.ClockText + " rooms [" + string.Join(",", stack) + "] camera " + camX + "," + camY + " in " + sw.ElapsedMilliseconds + " ms -> " + PathOf(slot));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("OpenAT save: slot " + slot + " failed: " + e);
                return false;
            }
        }

        // ------------------------------------------------------------------ load

        /// <summary>
        /// Loads a slot and resumes it: new <see cref="GameSession"/> from <see cref="SaveGame.Read(Stream, Sim.Data.GameData, out List{string})"/>,
        /// the airport as the base screen, the saved top room re-entered. Works from the main menu and in game.
        /// </summary>
        public static bool Load(GameApp app, int slot)
        {
            if (app?.Assets == null) return false;
            string path = PathOf(slot);
            World w;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var f = File.OpenRead(path)) w = SaveGame.Read(f, app.Assets.GameData, out var warnings);
                Debug.Log("OpenAT load: slot " + slot + " day " + w.Clock.Date + " " + w.Clock.ClockText + " in " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                Debug.LogError("OpenAT load: slot " + slot + " (" + path + ") failed: " + e);
                return false;
            }
            Resume(app, w);
            return true;
        }

        /// <summary>Airport camera of the loaded game, applied by <see cref="SessionServices"/> once the hall screen is up.</summary>
        public static Vector2Int? PendingCamera;
        /// <summary>The hall screen of the game that was running before the load (not the one to adjust).</summary>
        public static GameScreen ReplacedHall;

        /// <summary>Makes <paramref name="w"/> the running game (after a load).</summary>
        public static void Resume(GameApp app, World w)
        {
            ReplacedHall = null;
            foreach (var s in app.Screens.All) if (s is Airport.AirportScreen) ReplacedHall = s;
            app.Rooms.Reset();
            var session = new GameSession(w);
            app.Session = session;
            app.Music?.Stop();
            var hall = ScreenFactory.Create("hall");
            app.Screens.Set(hall ?? new LobbyScreen());
            var stack = new List<int>(w.Local.RoomStack ?? Array.Empty<int>());
            // A call-it-a-day autosave was written with the evening screen open; loading before 18:00 clears the
            // call (sim-core.md §5.3), so the player continues the day at the airport instead.
            if (!w.CallItADay) stack.Remove(RoomIds.Abend);
            app.Rooms.Restore(stack);
            PendingCamera = w.Local.CameraX.HasValue ? new Vector2Int(w.Local.CameraX.Value, w.Local.CameraY ?? 0) : (Vector2Int?)null;
            Debug.Log("OpenAT load: resumed, rooms [" + string.Join(",", stack) + "]");
        }
    }
}
