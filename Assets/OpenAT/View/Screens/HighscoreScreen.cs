using OpenAT.Sim.Stats;
using OpenAT.View.Session;
using OpenAT.View.UI;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// Highscore page of the main menu (rooms-and-ui-4 §3.8): title NewG 1100 on row 0; for rank n = 1..6 "n." at
    /// column 0 and the name at column 3 of row 2n, the company value with thousands separators at column 3 of
    /// row 2n+1; back (NewG 4001) on row 15. Any left click (or right click / Esc) returns to the main menu.
    /// </summary>
    [ScreenName("highscores")]
    public sealed class HighscoreScreen : KlackerMenuScreen
    {
        bool _left;

        protected override void Fill()
        {
            Board.PrintAt(0, 0, T(1100));
            var t = HighscoreStore.Table;
            for (int n = 1; n <= HighscoreTable.Size; n++)
            {
                var e = t[n - 1];
                Board.PrintAt(0, 2 * n, n + ".");
                Board.PrintAt(3, 2 * n, e.Name ?? "");
                Board.PrintAt(3, 2 * n + 1, UiKit.Thousands(e.Score));
            }
            Item(15, 0, T(4001), Back, c0: 0, c1: 6);
        }

        public override void Update(InputState input, float dt)
        {
            base.Update(input, dt);
            if (input.LeftDown) Back();
        }

        protected override void OnBack() => Back();

        void Back()
        {
            if (_left) return;
            _left = true;
            App.Screens.Set(new MainMenuScreen());
        }
    }
}
