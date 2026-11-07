using UnityEditor;

namespace OpenAT.EditorTools
{
    /// <summary>
    /// Editor menu switch for the virtual DualShock 4 and its overlay (docs/VIRTUALPAD.md): when ticked, the next
    /// Play session adds the device and listens on 127.0.0.1:47800 (OpenAT.View.Boot.VirtualPad).
    /// </summary>
    static class VirtualPadMenu
    {
        const string Pref = "OpenAT.VirtualPad", Item = "OpenAT/Virtual DualShock 4";

        [MenuItem(Item)]
        static void Toggle() => EditorPrefs.SetBool(Pref, !EditorPrefs.GetBool(Pref, false));

        [MenuItem(Item, true)]
        static bool Check()
        {
            Menu.SetChecked(Item, EditorPrefs.GetBool(Pref, false));
            return true;
        }
    }
}
