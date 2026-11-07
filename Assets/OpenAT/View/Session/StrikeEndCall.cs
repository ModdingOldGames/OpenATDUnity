using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Staff;
using OpenAT.View.Boot;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Rooms.Rivals;
using OpenAT.View.Screens;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Session
{
    /// <summary>
    /// "The strike is over" (sim-core.md §6.1, rooms-and-ui-3 §6.3, rooms-and-ui-3b §4b): once the local airline's
    /// strike has ended (09:00–18:00) and the player can be reached — a mobile phone, or in the own office or
    /// personnel office, no dialog or menu open — the staff clerk calls: in the personnel office face to face,
    /// elsewhere the phone rings and the personnel office slides in as the phone pane. The dialog is
    /// <c>Jobs</c> 1000 + end type; the game speed drops to 0 and the sim marks the end as told
    /// (<see cref="StrikeEndNotifiedCommand"/>).
    /// </summary>
    public sealed class StrikeEndCall : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.StrikeEndCall");
            DontDestroyOnLoad(go);
            go.AddComponent<StrikeEndCall>();
        }

        void Update()
        {
            var app = GameApp.Instance;
            var w = app?.Session?.World;
            if (w == null || app.Assets == null) return;
            var p = w.Local;
            if (!StrikeEndNotifiedCommand.Due(w, p)) return;
            if (w.Clock.State != DayState.Playing || w.CallItADay || p.CalledItADay) return;
            foreach (var sc in app.Screens.All) if (sc is OptionsScreen) return;
            if (GameFlow.UiBusy(app) || app.Rooms.Phone != null || RivalCallScreen.IsOpen(app)) return;
            int me = w.LocalPlayer;
            int room = app.Rooms.CurrentRoom;
            int personnel = RoomIds.PersonalOf(me);
            bool inPersonnel = room == personnel;
            if (!p.HasItem(Sim.Items.ItemId.Phone) && room != RoomIds.OfficeOf(me) && !inPersonnel) return;

            int talker = Talkers.Personal1b + 2 * me;
            int par1 = 1000 + p.StrikeEndType;
            if (!w.Execute(new StrikeEndNotifiedCommand(me)).Ok) return;
            p.GameSpeed = 0;
            if (app.Session != null) app.Session.Driver.Warp = false;
            Debug.Log("OpenAT strike: the staff clerk calls about the end of the strike (" + par1 + ")");
            if (inPersonnel && app.Rooms.Current != null)
            {
                app.Rooms.Current.StartDialog(talker, DialogMedium.Air, par1);
                return;
            }
            var pane = RoomRegistry.Create(personnel);
            if (pane == null) return;
            app.Audio.Play("SOUND/Phone.raw");
            var screen = new PhoneCallScreen(pane, talker, par1);
            app.Rooms.Phone = screen;
            app.Screens.Push(screen);
        }
    }
}
