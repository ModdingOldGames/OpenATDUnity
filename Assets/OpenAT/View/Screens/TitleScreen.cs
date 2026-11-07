using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// Title picture. In the original it is only the loading backdrop: the main menu follows on its own
    /// once loading is done (rooms-and-ui-4 §1.2/§1.3), so it continues after a short hold or on any click or key.
    /// </summary>
    [ScreenName("title")]
    public sealed class TitleScreen : GameScreen
    {
        const string Lib = "room/titel.gli";

        public override void OnEnter() => App.Music?.Play("SOUND/title.mid", true);

        const float HoldSeconds = 2.5f;
        float _shown;

        public override void Update(InputState input, float dt)
        {
            _shown += Mathf.Min(dt, 0.1f);   // a long first frame (loading) must not skip the picture
            if (_shown >= HoldSeconds || input.LeftDown || input.RightDown || input.Enter || input.Escape || input.Space)
                App.Screens.Set(new MainMenuScreen());
        }

        public override void Draw(Screen2D s)
        {
            WideFill.DrawCentered(s, Assets.Texture(Lib, "TITEL"));
        }

        public override void OnExit() => Assets.Unload(Lib);
    }
}
