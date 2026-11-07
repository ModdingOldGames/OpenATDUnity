using System;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Screens
{
    public enum OptionsPage { Start = 1, Graphics = 2, Sound = 3, Other = 4, Load = 5, Save = 6, LeaveGame = 8 }

    /// <summary>
    /// Options on the split-flap board (rooms-and-ui-4 §4): start page, graphics, sound (sliders), other,
    /// load/save (slots on disk, <see cref="Session.SaveSlots"/>; name entry on the save page) and "leave game". In game it is an overlay that
    /// stops the clock; from the main menu it replaces the menu. Options are stored in PlayerPrefs
    /// under "OpenAT.Opt.*"; the widescreen switch is an OpenAT addition.
    /// </summary>
    [ScreenName("options")]
    public sealed class OptionsScreen : KlackerMenuScreen
    {
        public const string Prefix = "OpenAT.Opt.";
        readonly bool _inGame;
        OptionsPage _page;
        AudioSource _ambienceLoop;
        int _dragLine = -1, _dragCol = -1;
        int _ambientBefore = RoomAmbience.Hall;

        protected override int? TextCursorY => _editSlot >= 0 ? BoardY + 22 * (_editSlot + 2) + 11 : (int?)null;

        public OptionsScreen() : this(false) { }
        public OptionsScreen(bool inGame, OptionsPage page = OptionsPage.Start) { _inGame = inGame; _page = page; }

        public static bool Get(string key, bool def = true) => PlayerPrefs.GetInt(Prefix + key, def ? 1 : 0) != 0;
        static void Set(string key, bool v) { PlayerPrefs.SetInt(Prefix + key, v ? 1 : 0); PlayerPrefs.Save(); }

        public override void OnEnter()
        {
            if (_inGame && App.Session != null) App.Session.HoldTime = true;
            // The options room mutes the airport ambience; on leaving, the location below sets its own again.
            if (_inGame) { _ambientBefore = App.Audio.GlobalAmbientVolume; App.Audio.SetGlobalAmbientVolume(RoomAmbience.Options); }
            if (_inGame) KlackerPlanes.Reset();   // in a running game the options start without screensaver planes
            base.OnEnter();
        }

        public override void OnExit()
        {
            if (App.Session != null) App.Session.HoldTime = false;
            if (_inGame) App.Audio.SetGlobalAmbientVolume(_ambientBefore);
            App.Audio.StopLoop(_ambienceLoop);
            App.Audio.SaveVolumes();
            PlayerPrefs.Save();
        }

        void Go(OptionsPage p)
        {
            if (_page == OptionsPage.Sound && p != OptionsPage.Sound) { App.Audio.StopLoop(_ambienceLoop); _ambienceLoop = null; }
            _page = p;
            _editSlot = -1;
            if (p == OptionsPage.Load || p == OptionsPage.Save) _headers = null; // slot names are re-read
            Refresh();
        }

        void CloseOptions()
        {
            if (_inGame) App.Screens.Remove(this);
            else App.Screens.Set(new MainMenuScreen());
        }

        protected override void OnBack()
        {
            if (_page == OptionsPage.Start) CloseOptions();
            else if (_page == OptionsPage.Load || _page == OptionsPage.Save) CloseOptions(); // original: back from load/save closes (§4.3 quirk)
            else Go(OptionsPage.Start);
        }

        protected override void Fill()
        {
            Board.PrintAt(0, 1, new string('=', 24));
            switch (_page)
            {
                case OptionsPage.Start: FillStart(); break;
                case OptionsPage.Graphics: FillGraphics(); break;
                case OptionsPage.Sound: FillSound(); break;
                case OptionsPage.Other: FillOther(); break;
                case OptionsPage.Load: case OptionsPage.Save: FillSlots(); break;
                case OptionsPage.LeaveGame: FillLeave(); break;
            }
        }

        void FillStart()
        {
            Board.PrintAt(0, 0, Misc(4000));
            Item(2, 0, Misc(4001), () => Go(OptionsPage.Graphics));
            Item(3, 0, Misc(4002), () => Go(OptionsPage.Sound));
            Item(4, 0, Misc(4003), () => Go(OptionsPage.Other));
            if (_inGame)
            {
                Item(6, 0, Misc(4004), () => Go(OptionsPage.Load));
                Item(7, 0, Misc(4005), () => Go(OptionsPage.Save));
                Item(9, 0, Misc(4006), () => Go(OptionsPage.LeaveGame));
                Item(11, 0, Misc(4008), CloseOptions);
            }
            else Item(6, 0, Misc(4007), CloseOptions);
        }

        void Toggle(int row, string key, int textBase, bool def = true, Action<bool> apply = null)
        {
            bool v = Get(key, def);
            Item(row, 0, Misc(textBase + (v ? 1 : 0)), () => { Set(key, !v); apply?.Invoke(!v); Warp(); });
        }

        void FillGraphics()
        {
            Board.PrintAt(0, 0, Misc(4001).TrimStart('#', ' ').TrimEnd('.') + ":");
            Toggle(2, "PlanesOnApron", 4011);
            Toggle(3, "Passengers", 4013);
            Toggle(4, "Fade", 4015, true, v => { PlayerPrefs.SetInt("OpenAT.Fade", v ? 1 : 0); App.Ui.Fade.Enabled = v; });
            Toggle(5, "ThoughtBubbles", 4020);
            Toggle(6, "Scrolling", 4022);
            Toggle(7, "Transparency", 4024);
            Toggle(8, "Shadows", 4026);
            // OpenAT addition: widescreen framebuffer (text in our own words, the original has none).
            bool ws = App.Screen.Widescreen;
            Item(10, 0, "# WIDESCREEN     : " + (ws ? "ON" : "OFF"), () =>
            {
                App.Screen.Widescreen = !ws;
                PlayerPrefs.SetInt("OpenAT.Widescreen", !ws ? 1 : 0);
                Warp();
            });
            // OpenAT addition: accented letters missing from the small fonts drawn as their base letter
            // (McfFont.BaseLetterFallback; off = the original's blank cell).
            bool fb = Assets.BaseLetterFallback;
            Item(11, 0, "# ACCENT LETTERS : " + (fb ? "BASE" : "OFF"), () =>
            {
                Assets.BaseLetterFallback = !fb;
                PlayerPrefs.SetInt(Boot.GameAssets.PrefBaseLetterFallback, !fb ? 1 : 0);
                PlayerPrefs.Save();
                Warp();
            });
            Item(12, 0, Misc(4099), () => Go(OptionsPage.Start));
        }

        // Sound: (row, text id, getter, setter) sliders with 8 cells at columns 15..22, value 0..7.
        void FillSound()
        {
            Board.PrintAt(0, 0, Misc(4120));
            bool midi = Get("Midi");
            Item(2, 0, Misc(4130 + (midi ? 1 : 0)), () =>
            {
                Set("Midi", !midi);
                if (!midi) App.Music?.Play("SOUND/title.mid", true); else App.Music?.Stop();
                Warp();
            });
            bool digital = Get("DigitalSound");
            Item(7, 0, Misc(4132 + (digital ? 1 : 0)), () => { Set("DigitalSound", !digital); AudioListener.volume = !digital ? 1 : 0; Warp(); });
            Slider(9, 4122, () => App.Audio.AmbienceVolume, v => App.Audio.AmbienceVolume = v, "SOUND/raunen.raw", true);
            Slider(10, 4123, () => PlayerPrefs.GetFloat(Prefix + "Announcements", 1), v => PlayerPrefs.SetFloat(Prefix + "Announcements", v), "SOUND/opt_hinw.raw");
            Slider(11, 4124, () => App.Audio.VoiceVolume, v => App.Audio.VoiceVolume = v, "SOUND/opt_say.raw");
            Slider(12, 4125, () => App.Audio.EffectsVolume, v => App.Audio.EffectsVolume = v, "SOUND/Kaffee.raw");
            Slider(13, 4126, () => PlayerPrefs.GetFloat(Prefix + "Planes", 1), v => PlayerPrefs.SetFloat(Prefix + "Planes", v), "SOUND/prop.raw");
            Item(15, 0, Misc(4099), () => Go(OptionsPage.Start));
        }

        void Slider(int row, int textId, Func<float> get, Action<float> set, string sample, bool loop = false)
        {
            Board.PrintAt(0, row, Misc(textId));
            int v = Mathf.Clamp(Mathf.RoundToInt(get() * 7), 0, 7);
            var cells = new char[8];
            for (int i = 0; i < 8; i++) cells[i] = i == v ? '|' : '-';
            Board.PrintAt(15, row, new string(cells));
            for (int i = 0; i < 8; i++)
            {
                int value = i;
                Hotspot(row, 15 + i, 15 + i, () =>
                {
                    set(value / 7f);
                    if (loop)
                    {
                        if (_ambienceLoop == null) _ambienceLoop = App.Audio.PlayLoop(sample, value / 8f);
                        else _ambienceLoop.volume = value / 8f;
                    }
                    else App.Audio.Play(sample, value / 8f / Mathf.Max(0.01f, App.Audio.EffectsVolume));
                    Warp();
                });
            }
        }

        void FillOther()
        {
            Board.PrintAt(0, 0, Misc(4030));
            Toggle(2, "Assistant", 4031);
            Toggle(3, "Advisor", 4033);
            Toggle(4, "Autosave", 4038);
            Toggle(5, "Fax", 4040);
            Toggle(6, "RealCodes", 4042, true, on => Assets.GameData.UseRealCodes(on));
            bool bubbles = PlayerPrefs.GetInt(DialogSession.PrefBubbles, 1) != 0;
            Item(7, 0, Misc(4044 + (bubbles ? 1 : 0)), () => { PlayerPrefs.SetInt(DialogSession.PrefBubbles, bubbles ? 0 : 1); Warp(); });
            Toggle(8, "ShortBriefing", 4048);
            Toggle(9, "RandomStartDay", 4050);
            // OpenAT addition: the mod list (View/Modding/ModsScreen).
            Item(10, 0, "# MODS...", () => App.Screens.Push(new Modding.ModsScreen()));
            Item(11, 0, Misc(4099), () => Go(OptionsPage.Start));
        }

        // ------------------------------------------------------------------ load / save (§4.3)

        // Save-page name editing: slot being edited (-1 none), its 20 characters, cursor column 0..19.
        int _editSlot = -1, _editCursor;
        char[] _editName;
        bool _editTouched;
        Sim.Save.SaveHeader[] _headers;

        void ReadSlots()
        {
            _headers = new Sim.Save.SaveHeader[Session.SaveSlots.Count];
            for (int i = 0; i < _headers.Length; i++) _headers[i] = Session.SaveSlots.Header(i);
        }

        string SlotName(int slot)
        {
            var h = _headers?[slot];
            return h != null && !string.IsNullOrEmpty(h.Name) ? h.Name.ToUpperInvariant() : h != null ? "" : Misc(4073);
        }

        void FillSlots()
        {
            bool load = _page == OptionsPage.Load;
            if (_headers == null) ReadSlots();
            Board.PrintAt(0, 0, Misc(load ? 4070 : 4071));
            // Load: slots 1..12 (12 = autosave); save: 1..11. Empty slots are dark on the load page.
            int n = load ? Session.SaveSlots.Count : Session.SaveSlots.Count - 1;
            for (int i = 0; i < n; i++)
            {
                int slot = i;
                string name = _editSlot == slot ? new string(_editName) : SlotName(slot);
                if (name.Length > Session.SaveSlots.MaxNameLength) name = name.Substring(0, Session.SaveSlots.MaxNameLength);
                string label = (i + 1).ToString("00") + ":" + name;
                if (load) Item(2 + i, 1, label, () => LoadSlot(slot), enabled: _headers[slot] != null);
                else Item(2 + i, 1, label, () => { if (_editSlot < 0) BeginEdit(slot); });
            }
            if (_editSlot >= 0)
            {
                Item(15, 0, Misc(4097), CancelEdit, c0: 0, c1: 9);
                Item(15, 22, Misc(4098), CommitEdit, c0: 22, c1: 23);
            }
            else Item(15, 0, Misc(4096), OnBack, c0: 0, c1: 9);
        }

        void LoadSlot(int slot)
        {
            if (_headers?[slot] == null) return;
            if (!Session.SaveSlots.Load(App, slot)) { ReadSlots(); Warp(); }
        }

        void BeginEdit(int slot)
        {
            _editSlot = slot;
            _editCursor = 0;
            _editTouched = false;
            string name = SlotName(slot).PadRight(Session.SaveSlots.MaxNameLength);
            _editName = name.Substring(0, Session.SaveSlots.MaxNameLength).ToCharArray();
            if (App.Session != null) App.Session.HoldTime = true;
            Warp();
        }

        void CancelEdit()
        {
            _editSlot = -1;
            ReadSlots();   // names are re-read from disk
            Warp();
        }

        void CommitEdit()
        {
            if (_editSlot < 0) return;
            int slot = _editSlot;
            string name = new string(_editName).TrimEnd();
            if (!_editTouched && _headers?[slot] == null) name = "";
            _editSlot = -1;
            if (App.Session == null || !Session.SaveSlots.Save(App, slot, name)) { ReadSlots(); Warp(); return; }
            CloseOptions();
        }

        /// <summary>Typing on the save page (§4.3): A-Z (upper-cased), Ä Ö Ü, digits, space, - + .; backspace blanks and moves left.</summary>
        bool UpdateEditing(InputState input)
        {
            if (_editSlot < 0) return false;
            bool changed = false;
            foreach (char ch in input.Typed)
            {
                char u = char.ToUpperInvariant(ch);
                if (!Session.SaveSlots.IsAllowedNameChar(u)) continue;
                if (!_editTouched && _headers?[_editSlot] == null)
                    for (int i = 0; i < _editName.Length; i++) _editName[i] = ' ';
                _editTouched = true;
                _editName[_editCursor] = u;
                _editCursor = Mathf.Min(Session.SaveSlots.MaxNameLength - 1, _editCursor + 1);
                changed = true;
            }
            if (input.Backspace)
            {
                _editTouched = true;
                _editName[_editCursor] = ' ';
                _editCursor = Mathf.Max(0, _editCursor - 1);
                changed = true;
            }
            if (input.LeftPressed) _editCursor = Mathf.Max(0, _editCursor - 1);
            if (input.RightPressed) _editCursor = Mathf.Min(Session.SaveSlots.MaxNameLength - 1, _editCursor + 1);
            if (input.Enter) { App.Audio.Play("SOUND/change.raw"); CommitEdit(); return true; }
            if (input.Escape || input.RightDown) { CancelEdit(); input.Escape = false; input.RightDown = false; return true; }
            if (changed) Warp();
            // While editing, clicks outside the save page's own lines (slot rows, row 15) are ignored.
            if (input.LeftDown)
            {
                int line = HoverLine(input);
                if (line != 15) { input.LeftDown = false; return true; }
            }
            return false;
        }

        /// <summary>Hovering a filled slot on the load page shows its info line as a tooltip.</summary>
        void SlotTooltip(InputState input)
        {
            if (_page != OptionsPage.Load || _headers == null) return;
            int line = HoverLine(input), slot = line - 2;
            if (slot < 0 || slot >= _headers.Length || _headers[slot] == null) return;
            var text = Session.SaveSlots.InfoText(Assets, _headers[slot]);
            if (!string.IsNullOrEmpty(text)) App.Ui.Tooltip.Set(text);
        }

        void FillLeave()
        {
            Board.PrintAt(0, 0, Misc(4065));
            Board.PrintAt(0, 2, Misc(4061));
            Item(4, 0, Misc(4062), () =>
            {
                App.Session = null;
                App.Rooms.Reset();
                App.Screens.Set(new MainMenuScreen());
            });
            Item(5, 0, Misc(4063), () => Go(OptionsPage.Start));
        }

        public override void Update(InputState input, float dt)
        {
            PumpPlanes(input, dt);
            // Dragging a slider with the button held (§4.2 sound page).
            if (_page == OptionsPage.Sound && input.LeftHeld && !input.LeftDown)
            {
                int line = HoverLine(input), col = HoverColumn(input);
                if (line >= 9 && line <= 13 && col >= 15 && col <= 22 && (line != _dragLine || col != _dragCol))
                {
                    _dragLine = line; _dragCol = col;
                    input.LeftDown = true; base.Update(input, dt); input.LeftDown = false; return;
                }
            }
            if (input.LeftDown) { _dragLine = HoverLine(input); _dragCol = HoverColumn(input); }
            if (UpdateEditing(input)) { Board.Update(dt); return; }
            base.Update(input, dt);
            SlotTooltip(input);
        }

        public override void Draw(Screen2D s)
        {
            if (_inGame) s.FillRect(new RectInt(0, 0, s.Width, s.Height), Color.black);
            base.Draw(s);
            // Save-page cursor under the edited character: x = 129 + 16 * (col + 4), row of the slot (§4.3).
            if (_editSlot >= 0 && (int)(Time.unscaledTime * 3) % 2 == 0)
                s.FillRect(new RectInt(OX + 129 + 16 * (_editCursor + 4), BoardY + 22 * (_editSlot + 2) + 19, 15, 3), new Color32(255, 255, 255, 255));
        }
    }
}
