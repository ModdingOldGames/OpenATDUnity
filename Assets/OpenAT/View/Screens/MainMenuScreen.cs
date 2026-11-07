using System;
using System.Collections.Generic;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// Base for the split-flap menu pages (main menu, new game, options): background picture
    /// "HAUPTMNU" with a 24x16 klacker board at (128, 63) in original coordinates. Lines are
    /// clickable (optionally only a column range); hovering a line shows the hot cursor and every
    /// left click plays change.raw (rooms-and-ui-4 §2.7). Subclasses fill the board.
    /// </summary>
    public abstract class KlackerMenuScreen : GameScreen
    {
        protected const string Lib = "room/startup.gli";
        protected const int BoardX = 128, BoardY = 63;
        protected readonly KlackerBoard Board = new KlackerBoard();
        readonly Dictionary<int, List<(int c0, int c1, Action a, CursorLook look)>> _lineActions = new Dictionary<int, List<(int, int, Action, CursorLook)>>();

        public override void OnEnter()
        {
            Board.Load(Assets);
            Board.OnKlack = n =>
            {
                App.Audio.Play("SOUND/Klack0.raw", 0.5f);
                if (n > 20) App.Audio.Play("SOUND/Klack1.raw", 0.5f);
                if (n > 50) App.Audio.Play("SOUND/Klack2.raw", 0.5f);
            };
            Refresh();
        }

        /// <summary>Rebuilds the board's target text and the line actions.</summary>
        protected void Refresh()
        {
            Board.Clear();
            _lineActions.Clear();
            Fill();
        }

        /// <summary>Like <see cref="Refresh"/> but the new text appears at once ("warp").</summary>
        protected void Warp()
        {
            Refresh();
            Board.SettleNow();
        }

        protected abstract void Fill();

        protected string T(int id) => Assets.StdText.GetOr("NewG", id);
        protected string Misc(int id) => Assets.StdText.GetOr("Misc", id);

        /// <summary>Prints a line and makes it clickable (whole row, or columns c0..c1).</summary>
        protected void Item(int row, int col, string text, Action onClick, bool enabled = true, int c0 = 0, int c1 = KlackerBoard.Columns - 1, CursorLook look = CursorLook.Hot)
        {
            Board.PrintAt(col, row, text);
            if (!enabled) Board.LineDisabled[row] = true;
            if (enabled && onClick != null) Hotspot(row, c0, c1, onClick, look);
        }

        protected void Hotspot(int row, int c0, int c1, Action onClick, CursorLook look = CursorLook.Hot)
        {
            if (!_lineActions.TryGetValue(row, out var l)) _lineActions[row] = l = new List<(int, int, Action, CursorLook)>();
            l.Add((c0, c1, onClick, look));
        }

        protected int HoverLine(InputState input)
        {
            int x = input.Mouse.x - OX - BoardX, y = input.Mouse.y - BoardY;
            if (x < 0 || y < 0 || x >= KlackerBoard.Columns * KlackerBoard.CellWidth) return -1;
            int line = y / KlackerBoard.CellHeight;
            return line < KlackerBoard.Rows ? line : -1;
        }

        protected int HoverColumn(InputState input) => (input.Mouse.x - OX - BoardX) / KlackerBoard.CellWidth;

        (Action a, CursorLook look)? Hit(InputState input)
        {
            int line = HoverLine(input), col = HoverColumn(input);
            if (line < 0 || !_lineActions.TryGetValue(line, out var l)) return null;
            foreach (var e in l) if (col >= e.c0 && col <= e.c1) return (e.a, e.look);
            return null;
        }

        /// <summary>Text cursor y while typing on the board (the idle planes dodge it instead of the mouse), else null.</summary>
        protected virtual int? TextCursorY => null;

        /// <summary>The idle screensaver planes (<see cref="KlackerPlanes"/>); once per frame, also from overriding screens.</summary>
        protected void PumpPlanes(InputState input, float dt) =>
            KlackerPlanes.Update(input, dt, Screen.Width, TextCursorY ?? input.Mouse.y);

        /// <summary>Draws the idle planes over the board.</summary>
        protected void DrawPlanes(Screen2D s) => KlackerPlanes.Draw(s, Assets);

        public override void Update(InputState input, float dt)
        {
            PumpPlanes(input, dt);
            Board.Update(dt);
            var hit = Hit(input);
            if (hit.HasValue) App.Ui.Cursor = hit.Value.look;
            if (input.LeftDown)
            {
                App.Audio.Play("SOUND/change.raw");
                hit?.a();
            }
            if (input.Escape || input.RightDown) OnBack();
        }

        protected virtual void OnBack() { }

        public override void Draw(Screen2D s)
        {
            WideFill.DrawCentered(s, Assets.Texture(Lib, "HAUPTMNU"));
            Board.Draw(s, OX + BoardX, BoardY);
            if (ShowVersion) DrawVersion(s);
            DrawPlanes(s);
        }

        /// <summary>The main menu and the new-game pages print the version bottom-right of the board.</summary>
        protected virtual bool ShowVersion => false;

        /// <summary>Version line: small red font, right-aligned at x 519, y 429 (rooms-and-ui-4 §3.1).</summary>
        protected void DrawVersion(Screen2D s) =>
            App.Ui.Kit.SmallRed?.Draw(s, OX + 519, 429, "VERSION " + Application.version, TextAlign.Right);
    }

    /// <summary>Main menu page (texts NewG 500..515 in std_ger.res, rooms-and-ui-4 §3.3).</summary>
    [ScreenName("menu")]
    public sealed class MainMenuScreen : KlackerMenuScreen
    {
        protected override bool ShowVersion => true;

        protected override void Fill()
        {
            Board.PrintAt(0, 0, T(500));
            Board.PrintAt(0, 1, new string('=', 24));
            Item(2, 1, "# " + T(501), () => App.Screens.Set(new NewGameScreen(NewGameScreen.PlayersPage)), c0: 1, c1: 20);
            Item(3, 1, T(515), () => App.Screens.Set(new NewGameScreen(NewGameScreen.CampaignPage)), c0: 1, c1: 20);
            Item(4, 1, T(514), null, enabled: false);       // network game: not implemented
            Item(5, 1, T(506), () => App.Screens.Set(new OptionsScreen(false, OptionsPage.Load)), c0: 1, c1: 20);
            Item(7, 1, T(504), () => App.Screens.Set(new NewGameScreen(NewGameScreen.AirportPage)), c0: 1, c1: 20);
            Item(8, 1, T(507), () => App.Screens.Set(new OptionsScreen(false)), c0: 1, c1: 20);
            Item(10, 1, T(505), () => App.Screens.Set(new VideoScreen("intro/intro.smk", () => new MainMenuScreen())), c0: 1, c1: 20);
            Item(11, 1, T(508), () => App.Screens.Set(new CreditsScreen()), c0: 1, c1: 20);
            Item(12, 1, T(509), () => App.Screens.Set(new HighscoreScreen()), c0: 1, c1: 20);
            Item(14, 1, T(510), Quit, c0: 1, c1: 20, look: CursorLook.Exit);
        }

        static void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
