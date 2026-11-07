using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// Shown when no Airline Tycoon Deluxe install was found. Original fonts are not available yet,
    /// so this one screen uses Unity's IMGUI.
    /// </summary>
    public sealed class InstallPromptScreen : GameScreen
    {
        string _path = @"C:\Program Files (x86)\GOG Galaxy\Games\Airline Tycoon Deluxe";
        string _error;
        GameObject _host;

        public override void OnEnter()
        {
            _host = new GameObject("InstallPrompt");
            _host.AddComponent<Gui>().Owner = this;
        }

        public override void OnExit()
        {
            if (_host != null) Object.Destroy(_host);
        }

        public override void Draw(Screen2D s) { }

        sealed class Gui : MonoBehaviour
        {
            public InstallPromptScreen Owner;

            void OnGUI()
            {
                float scale = Mathf.Max(1f, UnityEngine.Screen.height / 540f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                var area = new Rect(40, 40, UnityEngine.Screen.width / scale - 80, 300);
                GUILayout.BeginArea(area);
                GUILayout.Label("OpenAT needs the files of your own copy of Airline Tycoon Deluxe.");
                GUILayout.Label("Enter the folder that contains At.exe, gli/ and room/:");
                Owner._path = GUILayout.TextField(Owner._path);
                if (GUILayout.Button("Use this folder", GUILayout.Width(200)))
                {
                    if (!Owner.App.TryUseInstall(Owner._path.Trim().Trim('"')))
                        Owner._error = "That folder does not look like an Airline Tycoon Deluxe installation.";
                }
                if (Owner._error != null) GUILayout.Label(Owner._error);
                GUILayout.EndArea();
            }
        }
    }
}
