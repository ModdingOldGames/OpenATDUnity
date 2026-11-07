using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;

namespace OpenAT.View.UI
{
    /// <summary>A gamepad button an action can be bound to (the d-pad and the sticks are fixed).</summary>
    public enum PadButton { None, South, East, West, North, L1, R1, L2, R2, Start, Select, L3, R3, Touchpad }

    /// <summary>The gamepad actions that can be rebound (Options > Other > OpenAT extras > Gamepad buttons).</summary>
    public enum PadAction { Click, Back, Options, FastTravel, EndDay, Tooltip, WalkPlanner, WalkOffice, PrevTarget, NextTarget, ScrollUp, ScrollDown, Home }

    /// <summary>
    /// Gamepad button bindings (OpenAT): one button per action, stored in PlayerPrefs ("OpenAT.Pad.&lt;action&gt;"). Binding a
    /// button that another action uses gives that action the old button (a swap), so no button does two things.
    /// </summary>
    public static class PadBindings
    {
        const string Prefix = "OpenAT.Pad.";

        public static readonly PadAction[] Actions = (PadAction[])System.Enum.GetValues(typeof(PadAction));

        public static PadButton Default(PadAction a)
        {
            switch (a)
            {
                case PadAction.Click: return PadButton.South;
                case PadAction.Back: return PadButton.East;
                case PadAction.Options: return PadButton.Start;
                case PadAction.FastTravel: return PadButton.North;
                case PadAction.EndDay: return PadButton.Select;
                case PadAction.Tooltip: return PadButton.R3;
                case PadAction.WalkPlanner: return PadButton.West;
                case PadAction.WalkOffice: return PadButton.None;
                case PadAction.PrevTarget: return PadButton.L1;
                case PadAction.NextTarget: return PadButton.R1;
                case PadAction.ScrollUp: return PadButton.L2;
                case PadAction.ScrollDown: return PadButton.R2;
                case PadAction.Home: return PadButton.Touchpad;
            }
            return PadButton.None;
        }

        static readonly PadButton[] _cache = new PadButton[Actions.Length];
        static bool _loaded;

        public static PadButton Get(PadAction a)
        {
            if (!_loaded)
            {
                foreach (var x in Actions) _cache[(int)x] = (PadButton)PlayerPrefs.GetInt(Prefix + x, (int)Default(x));
                _loaded = true;
            }
            return _cache[(int)a];
        }

        /// <summary>Binds a button; the action that had it gets this action's old button.</summary>
        public static void Set(PadAction a, PadButton b)
        {
            var old = Get(a);
            if (b != PadButton.None)
                foreach (var x in Actions)
                    if (x != a && Get(x) == b) Store(x, old);
            Store(a, b);
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            foreach (var x in Actions) Store(x, Default(x));
            PlayerPrefs.Save();
        }

        static void Store(PadAction a, PadButton b)
        {
            Get(a);
            _cache[(int)a] = b;
            PlayerPrefs.SetInt(Prefix + a, (int)b);
        }

        public static ButtonControl Control(Gamepad g, PadButton b)
        {
            switch (b)
            {
                case PadButton.South: return g.buttonSouth;
                case PadButton.East: return g.buttonEast;
                case PadButton.West: return g.buttonWest;
                case PadButton.North: return g.buttonNorth;
                case PadButton.L1: return g.leftShoulder;
                case PadButton.R1: return g.rightShoulder;
                case PadButton.L2: return g.leftTrigger;
                case PadButton.R2: return g.rightTrigger;
                case PadButton.Start: return g.startButton;
                case PadButton.Select: return g.selectButton;
                case PadButton.L3: return g.leftStickButton;
                case PadButton.R3: return g.rightStickButton;
                case PadButton.Touchpad: return (g as DualShockGamepad)?.touchpadButton;
            }
            return null;
        }

        public static bool Pressed(Gamepad g, PadAction a) => Control(g, Get(a))?.wasPressedThisFrame ?? false;
        public static bool Held(Gamepad g, PadAction a) => Control(g, Get(a))?.isPressed ?? false;
        public static bool Released(Gamepad g, PadAction a) => Control(g, Get(a))?.wasReleasedThisFrame ?? false;
        /// <summary>Analog value 0..1 (triggers), or 0/1 for a button.</summary>
        public static float Value(Gamepad g, PadAction a) => Control(g, Get(a))?.ReadValue() ?? 0f;

        /// <summary>The first button pressed this frame, or None (for rebinding).</summary>
        public static PadButton FirstPressed(Gamepad g)
        {
            for (var b = PadButton.South; b <= PadButton.Touchpad; b++)
                if (Control(g, b)?.wasPressedThisFrame ?? false) return b;
            return PadButton.None;
        }

        /// <summary>Button names for the split-flap board (Xbox / PlayStation; letters, digits and '-' only).</summary>
        public static string Name(PadButton b)
        {
            switch (b)
            {
                case PadButton.South: return "A-CROSS";
                case PadButton.East: return "B-CIRCLE";
                case PadButton.West: return "X-SQUARE";
                case PadButton.North: return "Y-TRIANGLE";
                case PadButton.L1: return "LB-L1";
                case PadButton.R1: return "RB-R1";
                case PadButton.L2: return "LT-L2";
                case PadButton.R2: return "RT-R2";
                case PadButton.Start: return "MENU-OPT";
                case PadButton.Select: return "VIEW-SHARE";
                case PadButton.L3: return "LS-L3";
                case PadButton.R3: return "RS-R3";
                case PadButton.Touchpad: return "TOUCHPAD";
            }
            return "-";
        }

        public static string Label(PadAction a, bool german)
        {
            switch (a)
            {
                case PadAction.Click: return german ? "KLICK" : "CLICK";
                case PadAction.Back: return german ? "ZURUECK" : "BACK";
                case PadAction.Options: return german ? "OPTIONEN" : "OPTIONS";
                case PadAction.FastTravel: return german ? "SCHNELLREISE" : "FAST TRAVEL";
                case PadAction.EndDay: return german ? "FEIERABEND" : "END THE DAY";
                case PadAction.Tooltip: return german ? "HINWEIS" : "TOOLTIP";
                case PadAction.WalkPlanner: return german ? "ZUM PLANER" : "TO PLANNER";
                case PadAction.WalkOffice: return german ? "ZUM BUERO" : "TO OFFICE";
                case PadAction.PrevTarget: return german ? "ZURUECK-ZIEL" : "PREV TARGET";
                case PadAction.NextTarget: return german ? "NAECHST-ZIEL" : "NEXT TARGET";
                case PadAction.ScrollUp: return german ? "HOCH ROLLEN" : "SCROLL UP";
                case PadAction.ScrollDown: return german ? "RUNTER ROLL." : "SCROLL DOWN";
                case PadAction.Home: return german ? "CURSOR START" : "CURSOR HOME";
            }
            return a.ToString().ToUpperInvariant();
        }
    }
}
