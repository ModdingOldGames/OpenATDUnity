using System;
using OpenAT.Sim.Modding;
using OpenAT.View.Screens;

namespace OpenAT.View.Modding
{
    /// <summary>
    /// The mod list (OpenAT addition, reached from the options' "other" page or <c>-openat-screen mods</c>):
    /// one line per installed mod, click switches it on/off for the next start (mods load once per run,
    /// so a change needs a restart). Status marks: blank = active, "-" = off, "!" = broken (see the log),
    /// "*" = changed (restart). Texts in our own words; the original has no such page.
    /// </summary>
    [ScreenName("mods")]
    public sealed class ModsScreen : KlackerMenuScreen
    {
        const int FirstRow = 2, MaxRows = 11;
        int _scroll;

        protected override void Fill()
        {
            var all = ModBoot.Mods.All;
            Board.PrintAt(0, 0, "MODS (RESTART TO APPLY)");
            Board.PrintAt(0, 1, new string('=', 24));
            var disabled = ModBoot.DisabledIds();
            if (all.Count == 0) Board.PrintAt(0, FirstRow, "NO MODS INSTALLED");
            int shown = Math.Min(MaxRows, all.Count - _scroll);
            for (int i = 0; i < shown; i++)
            {
                var m = all[_scroll + i];
                string id = m.Id ?? "?";
                bool wantOn = !disabled.Contains(id) && (m.Manifest == null || m.Manifest.Enabled);
                bool isOn = m.Status == ModStatus.Loaded;
                char mark = m.Status == ModStatus.Failed || m.Status == ModStatus.Duplicate ? '!' : wantOn != isOn ? '*' : isOn ? ' ' : '-';
                string name = (m.Manifest?.DisplayName ?? id).ToUpperInvariant();
                string line = mark + (wantOn ? "ON  " : "OFF ") + name;
                if (line.Length > 24) line = line.Substring(0, 24);
                Item(FirstRow + i, 0, line, () => { ModBoot.SetEnabled(id, !wantOn); Warp(); });
            }
            if (all.Count > MaxRows)
                Item(14, 0, "MORE...", () => { _scroll = _scroll + MaxRows >= all.Count ? 0 : _scroll + MaxRows; Warp(); });
            Item(15, 0, "# BACK", OnBack);
        }

        protected override void OnBack()
        {
            if (App.Screens.All.Count > 1) App.Screens.Remove(this);
            else App.Screens.Set(new MainMenuScreen());
        }
    }
}
