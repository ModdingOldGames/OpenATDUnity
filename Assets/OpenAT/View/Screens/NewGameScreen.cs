using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// The new-game wizard on the split-flap board (rooms-and-ui-4 §3): campaigns (150), mission pages
    /// (1 / 12 / 122), player selection (2 / 14) with name entry, home airport (5) and the loading page
    /// (99) that starts the game. Network pages are not implemented.
    /// </summary>
    [ScreenName("newgame")]
    public sealed class NewGameScreen : KlackerMenuScreen
    {
        public const int MissionsPage = 1, PlayersPage = 2, AirportPage = 5, AddOnPage = 12, AddOnPlayersPage = 14, AtfsPage = 122, CampaignPage = 150, LoadingPage = 99;
        const string PrefPrefix = "OpenAT.NewGame.";

        int _page;
        int _difficulty = Mission.FreeGame;
        int _local;
        readonly string[] _names = new string[4], _airlines = new string[4], _abbrevs = new string[4];
        int _cursorPlayer = -1, _cursorCol;
        int _cityOffset;
        int _homeCity;
        float _loadingWait = -1;

        public NewGameScreen() : this(PlayersPage) { }
        public NewGameScreen(int page) { _page = page; }

        public override void OnEnter()
        {
            _local = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "LastPlayer", 0), 0, 3);
            for (int c = 0; c < 4; c++)
            {
                _names[c] = Load("Name" + c, 1000 + 10 * c, 21);
                _airlines[c] = Load("Airline" + c, 1001 + 10 * c, 21);
                _abbrevs[c] = Load("Abbrev" + c, 1002 + 10 * c, 2);
            }
            _homeCity = PlayerPrefs.GetInt(PrefPrefix + "HomeCity", 0);
            if (_homeCity == 0 || !Assets.GameData.Cities.Contains(_homeCity)) _homeCity = DefaultHome();
            base.OnEnter();
        }

        string Load(string key, int textId, int len)
        {
            string v = PlayerPrefs.GetString(PrefPrefix + key, "");
            if (string.IsNullOrEmpty(v)) v = T(textId);
            v = v.ToUpperInvariant();
            return v.Length > len ? v.Substring(0, len) : v.PadRight(len);
        }

        void SaveNames()
        {
            for (int c = 0; c < 4; c++)
            {
                PlayerPrefs.SetString(PrefPrefix + "Name" + c, _names[c].TrimEnd());
                PlayerPrefs.SetString(PrefPrefix + "Airline" + c, _airlines[c].TrimEnd());
                PlayerPrefs.SetString(PrefPrefix + "Abbrev" + c, _abbrevs[c].TrimEnd());
            }
            PlayerPrefs.SetInt(PrefPrefix + "LastPlayer", _local);
            PlayerPrefs.Save();
        }

        int DefaultHome()
        {
            // First-use default by language (sim-core §3.2): German Berlin, English London, French Paris.
            var c = Assets.GameData.CityByName(Assets.Language == Formats.TextResource.Language.German ? "Berlin" : Assets.Language == Formats.TextResource.Language.French ? "Paris" : "London");
            return c != null ? c.Id : Assets.GameData.Cities.All[0].Id;
        }

        void Go(int page)
        {
            _page = page;
            _cursorPlayer = -1;
            if (page == AirportPage)
            {
                var all = Assets.GameData.Cities.All;
                int idx = Mathf.Max(0, Assets.GameData.Cities.IndexOf(_homeCity));
                _cityOffset = idx / 12 * 12;
                if (_cityOffset >= all.Count) _cityOffset = 0;
            }
            Refresh();
        }

        bool NamesOk
        {
            get
            {
                var n = new HashSet<string>(); var a = new HashSet<string>(); var b = new HashSet<string>();
                for (int c = 0; c < 4; c++)
                {
                    if (!n.Add(_names[c].Trim()) || !a.Add(_airlines[c].Trim()) || !b.Add(_abbrevs[c].Trim())) return false;
                }
                return true;
            }
        }

        protected override void Fill()
        {
            if (_page != 0 && _page != LoadingPage) Board.PrintAt(0, 1, new string('=', 24));
            switch (_page)
            {
                case CampaignPage: FillCampaigns(); break;
                case MissionsPage: case AddOnPage: case AtfsPage: FillMissions(); break;
                case PlayersPage: case AddOnPlayersPage: FillPlayers(); break;
                case AirportPage: FillAirport(); break;
                case LoadingPage:
                    string t = T(530);
                    Board.PrintAt(12 - (t.Length - 3) / 2, 8, t);
                    break;
            }
        }

        void FillCampaigns()
        {
            string title = T(515);
            Board.PrintAt(0, 0, title.Length > 2 ? title.Substring(2) : title);
            Item(2, 1, T(502), () => { _difficulty = Mission.Tutorial; Go(MissionsPage); }, c0: 1, c1: 20);
            Item(3, 1, T(512), () => { _difficulty = Mission.AddOnFirst; Go(AddOnPage); }, c0: 1, c1: 20);
            Item(4, 1, T(513), () => { _difficulty = Mission.AtfsFirst; Go(AtfsPage); }, c0: 1, c1: 20);
            Item(6, 0, T(4001), () => App.Screens.Set(new MainMenuScreen()), c0: 1, c1: 20);
        }

        void FillMissions()
        {
            Board.PrintAt(0, 0, T(1999));
            int first, last;
            if (_page == MissionsPage) { first = 0; last = 5; }
            else if (_page == AddOnPage) { first = Mission.AddOnFirst; last = Mission.AddOnLast; }
            else { first = Mission.AtfsFirst; last = Mission.AtfsLast; }
            // Locked missions (above the campaign's unlocked maximum, Session.MissionProgressStore) are dark
            // and have no hotspot; a click selects the row's mission clamped to the maximum.
            int max = Session.MissionProgressStore.Max(Sim.Missions.MissionProgress.CampaignOf(first));
            for (int d = first; d <= last; d++)
            {
                int row = 2 + d - first;
                int id = _page == MissionsPage ? 2000 + 100 * d : 3000 + d;
                string name = Assets.StdText.Get("NewG", id);
                if (name == null) continue;
                int dd = Mathf.Min(d, max);
                Item(row, 1, (d == _difficulty ? "==>" : "   ") + name, () => { _difficulty = dd; Warp(); }, enabled: d <= max, c0: 1, c1: 23);
            }
            Item(15, 0, T(4001), () => Go(CampaignPage), c0: 0, c1: 10);
            string next = T(4002);
            Item(15, 24 - next.Length, next, () => Go(_page == MissionsPage ? PlayersPage : AddOnPlayersPage), c0: 14, c1: 23);
        }

        void FillPlayers()
        {
            Board.PrintAt(0, 0, T(999));
            for (int c = 0; c < 4; c++)
            {
                Board.PrintAt(6, 2 * c + 2, _names[c].Length > 18 ? _names[c].Substring(0, 18) : _names[c]);
                Board.PrintAt(6, 2 * c + 3, _airlines[c].Length > 18 ? _airlines[c].Substring(0, 18) : _airlines[c]);
                Board.PrintAt(3, 2 * c + 3, _abbrevs[c]);
                Board.LineDisabled[2 * c + 2] = Board.LineDisabled[2 * c + 3] = c != _local;
            }
            Item(15, 0, T(4001), () =>
            {
                SaveNames();
                if (_page == AddOnPlayersPage) Go(Mission.IsAtfs(_difficulty) ? AtfsPage : AddOnPage);
                else if (_difficulty == Mission.FreeGame) App.Screens.Set(new MainMenuScreen());
                else Go(MissionsPage);
            }, c0: 0, c1: 10);
            if (NamesOk)
            {
                string start = T(4000);
                Item(15, 24 - start.Length, start, () => { SaveNames(); Go(LoadingPage); _loadingWait = 0; }, c0: 14, c1: 23);
            }
        }

        void FillAirport()
        {
            Board.PrintAt(0, 0, T(600));
            var all = Assets.GameData.Cities.All;
            for (int i = 0; i < 12 && _cityOffset + i < all.Count; i++)
            {
                var city = all[_cityOffset + i];
                int id = city.Id;
                Item(2 + i, 3, city.Name, () =>
                {
                    _homeCity = id;
                    PlayerPrefs.SetInt(PrefPrefix + "HomeCity", id);
                    Warp();
                }, c0: 0, c1: 23);
                if (id == _homeCity) Board.PrintAt(0, 2 + i, "==>");
            }
            Item(15, 22, T(4003), () => App.Screens.Set(new MainMenuScreen()), c0: 22, c1: 23);
            if (_cityOffset > 0) Item(15, 10, "<<", () => { _cityOffset = Mathf.Max(0, _cityOffset - 12); Warp(); }, c0: 10, c1: 11);
            if (_cityOffset + 12 < all.Count) Item(15, 13, ">>", () => { _cityOffset += 12; Warp(); }, c0: 13, c1: 14);
        }

        protected override void OnBack()
        {
            switch (_page)
            {
                case AirportPage: App.Screens.Set(new MainMenuScreen()); break;
                case CampaignPage: App.Screens.Set(new MainMenuScreen()); break;
                case MissionsPage: case AddOnPage: case AtfsPage: Go(CampaignPage); break;
                case PlayersPage: case AddOnPlayersPage:
                    if (_cursorPlayer >= 0) { _cursorPlayer = -1; return; }
                    SaveNames();
                    if (_page == AddOnPlayersPage) Go(Mission.IsAtfs(_difficulty) ? AtfsPage : AddOnPage);
                    else if (_difficulty == Mission.FreeGame) App.Screens.Set(new MainMenuScreen());
                    else Go(MissionsPage);
                    break;
            }
        }

        // ------------------------------------------------------------------ player page input

        protected override int? TextCursorY => _cursorPlayer >= 0 && (_page == PlayersPage || _page == AddOnPlayersPage) ? 129 + 66 * _cursorPlayer + 11 : (int?)null;

        public override void Update(InputState input, float dt)
        {
            PumpPlanes(input, dt);
            if (_page == LoadingPage)
            {
                Board.Update(dt);
                if (Board.Settled && (_loadingWait += dt) > 0.25f) StartGame();
                return;
            }
            if (_page == PlayersPage || _page == AddOnPlayersPage)
            {
                if (UpdatePlayers(input)) return;
            }
            base.Update(input, dt);
        }

        bool UpdatePlayers(InputState input)
        {
            var m = input.Mouse;
            int x = m.x - OX, y = m.y;
            for (int c = 0; c < 4; c++)
            {
                int top = 129 + 66 * c;
                if (x < 128 || x > 512 || y < top || y > top + 44) continue;
                bool nameRow = y < top + 22;
                if (x >= 223 && nameRow) App.Ui.Cursor = CursorLook.Hot;
                // Only a click is handled here; hovering over a row (the mouse usually stays on the name
                // just clicked) must still let the typing below reach the name.
                if (!input.LeftDown) break;
                App.Audio.Play("SOUND/change.raw");
                if (c != _local) { _local = c; PlayerPrefs.SetInt(PrefPrefix + "LastPlayer", c); }
                if (x >= 223 && nameRow) { _cursorPlayer = c; _cursorCol = Mathf.Clamp((x - 223) / 16, 0, 17); }
                else if (!nameRow) _cursorPlayer = -1;
                Warp();
                input.Consume();
                return true;
            }
            if (_cursorPlayer < 0) return false;
            bool changed = false;
            foreach (char ch in input.Typed)
            {
                char u = char.ToUpperInvariant(ch);
                if ((u >= 'A' && u <= 'Z') || u == 'Ä' || u == 'Ö' || u == 'Ü' || u == ' ' || u == '-' || u == '.')
                {
                    var arr = _names[_cursorPlayer].ToCharArray();
                    arr[_cursorCol] = u;
                    _names[_cursorPlayer] = new string(arr);
                    _cursorCol = Mathf.Min(17, _cursorCol + 1);
                    changed = true;
                }
            }
            if (input.Backspace && _cursorCol > 0) { DeleteAt(_cursorCol - 1); _cursorCol--; changed = true; }
            if (input.Delete) { DeleteAt(_cursorCol); changed = true; }
            if (input.LeftPressed) _cursorCol = Mathf.Max(0, _cursorCol - 1);
            if (input.RightPressed) _cursorCol = Mathf.Min(17, _cursorCol + 1);
            if (input.UpPressed && _cursorPlayer > 0) _cursorPlayer--;
            if (input.DownPressed && _cursorPlayer < 3) _cursorPlayer++;
            if (input.Enter) { if (_cursorPlayer < 3) { _cursorPlayer++; _cursorCol = 0; } }
            if (changed) Warp();
            if (input.Escape) { _cursorPlayer = -1; input.Escape = false; return true; }
            return changed;
        }

        void DeleteAt(int col)
        {
            var s = _names[_cursorPlayer];
            if (col < 0 || col >= s.Length) return;
            _names[_cursorPlayer] = (s.Remove(col, 1) + " ").Substring(0, s.Length);
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(Screen2D s)
        {
            if (_page != PlayersPage && _page != AddOnPlayersPage) { base.Draw(s); return; }
            WideFill.DrawCentered(s, Assets.Texture(Lib, "HAUPTMNU"));
            int ox = OX;
            // Logical rows → screen y with the extra 22 px gaps of the player page (§3.6).
            DrawRow(s, 0, 63); DrawRow(s, 1, 85);
            for (int c = 0; c < 4; c++) { DrawRow(s, 2 * c + 2, 129 + 66 * c, 6); DrawRow(s, 2 * c + 3, 151 + 66 * c, 3); }
            DrawRow(s, 15, 393);
            for (int c = 0; c < 4; c++)
            {
                var portrait = Assets.Texture(Lib, "PLAYER" + (c == _local ? c : c + 4));
                s.Draw(portrait, ox + 128, 174 + 66 * c - portrait.height);
                s.Draw(Assets.Texture(UiKit.StdLib, "SLOGO" + c), ox + 183, 132 + 66 * c);
            }
            if (_cursorPlayer >= 0 && (int)(Time.unscaledTime * 3) % 2 == 0)
                s.FillRect(new RectInt(ox + 128 + 16 * (_cursorCol + 6), 129 + 66 * _cursorPlayer + 19, 15, 3), new Color32(255, 255, 255, 255));
            DrawVersion(s);
            DrawPlanes(s);
        }

        protected override bool ShowVersion => true;

        void DrawRow(Screen2D s, int row, int y, int firstCol = 0)
        {
            int x0 = OX + BoardX + firstCol * KlackerBoard.CellWidth;
            s.PushClip(new RectInt(x0, y, (KlackerBoard.Columns - firstCol) * KlackerBoard.CellWidth, KlackerBoard.CellHeight));
            Board.Draw(s, OX + BoardX, y - row * KlackerBoard.CellHeight);
            s.PopClip();
        }

        /// <summary>Hit rows on the player page use the gapped layout, so map the mouse back for the base.</summary>
        void StartGame()
        {
            var settings = new NewGameSettings
            {
                Difficulty = _difficulty,
                HomeCity = _homeCity,
                LocalPlayer = _local,
                StartTime = PlayerPrefs.GetInt("OpenAT.Opt.RandomStartDay", 1) != 0 ? (long?)null : (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds,
                Seed = Environment.TickCount,
            };
            for (int c = 0; c < 4; c++)
            {
                settings.PlayerNames[c] = _names[c].Trim();
                settings.AirlineNames[c] = _airlines[c].Trim();
                settings.Abbreviations[c] = _abbrevs[c].Trim();
            }
            App.StartNewGame(settings);
        }
    }
}
