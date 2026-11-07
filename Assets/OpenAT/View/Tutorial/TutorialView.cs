using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Tutorial;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;
using RoomIds = OpenAT.View.Rooms.Framework.RoomIds;

namespace OpenAT.View.Tutorial
{
    /// <summary>
    /// The view's half of the mission-0 tutorial (docs/spec/findings/tutorial.md, sim-core.md §2.2). The state
    /// (running, step, monologue) lives in the sim (<see cref="World.Tutorial"/>, saved); this class
    /// <list type="bullet">
    /// <item>runs the script steps that wait for the assistant to fall silent or for the player to be in a
    /// certain place (<see cref="TutorialDirector"/>), through <see cref="TutorialStepCommand"/>;</item>
    /// <item>turns <see cref="TutorialAdvanced"/> into assistant lines (<c>Tuto</c> group);</item>
    /// <item>answers the rooms' questions: which clicks are not allowed yet, where the glow markers are
    /// (<c>gli/glglow.gli</c> <c>TUTMARK1..5</c>), whether right click / leaving is blocked.</item>
    /// </list>
    /// </summary>
    public static class TutorialView
    {
        static World W => GameApp.Instance?.Session?.World;

        /// <summary>The tutorial runs (mission 0, not cancelled, first day).</summary>
        public static bool Active => W != null && TutorialEngine.Running(W);

        public static int Step => W?.Tutorial.Step ?? 0;

        public static bool At(int step) => Active && Step == step;

        static void Say(int id, params object[] args)
        {
            var app = GameApp.Instance;
            var raw = app?.Assets?.StdText.Get("Tuto", id);
            if (raw != null) app.Advisors.Queue(AdvisorKind.Assistant, DialogText.Format(raw, args));
        }

        // ------------------------------------------------------------------ room gates

        /// <summary>
        /// A click on a room hotspot (<paramref name="id"/>; 999 = exit). Returns true when the tutorial
        /// swallows it (rooms-and-ui-5 §1: the office before step 1401; the planner exit up to 1506).
        /// </summary>
        public static bool BlockClick(RoomScreen room, int id)
        {
            if (!Active || room == null || room.IsPhonePane) return false;
            int s = Step;
            if (RoomIds.IsOffice(room.RoomId))
            {
                if (id == 10 && s < TutorialStep.ClickGlobe) return true;
                if (id != 10 && id != 0 && s <= TutorialStep.ClickGlobe)
                {
                    Say(id == RoomScreen.ExitId ? 1520 : id == 13 ? 1521 : 1522);
                    return true;
                }
            }
            if (room.RoomId == RoomIds.Globe && id == RoomScreen.ExitId && s <= TutorialStep.PlaceOrder) return true;
            return false;
        }

        /// <summary>Right click (leave / cancel) is not allowed: office at 1401, planner up to 1506, boss up to 1310.</summary>
        public static bool BlockRightClick(RoomScreen room)
        {
            if (!Active || room == null || room.IsPhonePane) return false;
            int s = Step;
            if (RoomIds.IsOffice(room.RoomId) && s == TutorialStep.ClickGlobe) return true;
            if (room.RoomId == RoomIds.Globe && s <= TutorialStep.PlaceOrder) return true;
            if (room.RoomId == RoomIds.Aufsicht && s <= TutorialStep.GoToOffice) return true;
            return false;
        }

        // ------------------------------------------------------------------ planner gates (rooms-and-ui-5b §4)

        /// <summary>The left tab may change only at step 1502.</summary>
        public static bool LeftTabAllowed => !Active || Step == TutorialStep.OpenPlanes;

        /// <summary>The right tab: only orders, and only from step 1504.</summary>
        public static bool RightTabAllowed(int type) => !Active || (type == 3 && Step >= TutorialStep.OpenOrders);

        /// <summary>A plane's details open only from step 1503.</summary>
        public static bool PlaneDetailAllowed => !Active || Step >= TutorialStep.OpenPlane;

        /// <summary>Drag-and-drop placement and the double-click append are off in the tutorial.</summary>
        public static bool ShortcutsAllowed => !Active;

        /// <summary>Left tab switched (1502 → 1503 when the planes list opens).</summary>
        public static void LeftTabOpened(int type)
        {
            if (type == 2) Execute(new TutorialStepCommand(TutorialStep.OpenPlanes, TutorialStep.OpenPlane, new[] { 1503 }, new object[] { W.Local.Airline }, skipCurrent: true));
        }

        /// <summary>Right tab switched (1504 → 1505 when the orders list opens).</summary>
        public static void RightTabOpened(int type)
        {
            if (type == 3) Execute(new TutorialStepCommand(TutorialStep.OpenOrders, TutorialStep.TakeOrder, new[] { 1505 }, skipCurrent: true));
        }

        /// <summary>A plane's details were opened (1503 → 1504).</summary>
        public static void PlaneOpened(string name) =>
            Execute(new TutorialStepCommand(TutorialStep.OpenPlane, TutorialStep.OpenOrders, new[] { 1504 }, new object[] { name ?? "" }, skipCurrent: true));

        /// <summary>The "i" of an order row at step 1505: not now (the assistant says so instead).</summary>
        public static bool BlockOrderInfo()
        {
            if (!At(TutorialStep.TakeOrder)) return false;
            GameApp.Instance.Advisors.SkipCurrent();
            Say(1510);
            return true;
        }

        /// <summary>An order was picked up (1505 → 1506).</summary>
        public static void OrderTaken() =>
            Execute(new TutorialStepCommand(TutorialStep.TakeOrder, TutorialStep.PlaceOrder, new[] { 1506 }, skipCurrent: true));

        /// <summary>
        /// Placing the first order (step 1506): the flight goes to today, two hours from now, whatever cell was
        /// clicked (rooms-and-ui-5b §6.3 step 3). Returns the forced (date, hour) or null.
        /// </summary>
        public static (int date, int hour)? ForcedPlacement()
        {
            if (!At(TutorialStep.PlaceOrder)) return null;
            return (W.Clock.Date, W.Clock.Hour + 2);
        }

        /// <summary>The first order is in the plan (1506 → 1507).</summary>
        public static void OrderPlaced() =>
            Execute(new TutorialStepCommand(TutorialStep.PlaceOrder, TutorialStep.BackToHall, new[] { 1507 }, skipCurrent: true));

        /// <summary>In the tutorial the planner opens with an empty left side and routes on the right (rooms-and-ui-5 §2.4).</summary>
        public static bool InitialFilofax(out int left, out int right)
        {
            left = 0; right = 4;
            return Active;
        }

        static void Execute(TutorialStepCommand cmd)
        {
            var w = W;
            if (w == null) return;
            var r = w.Execute(cmd);
            if (!r.Ok && Debug.isDebugBuild) Debug.Log("OpenAT tutorial: step " + cmd.From + " → " + cmd.To + " refused: " + r);
        }

        // ------------------------------------------------------------------ glow markers

        /// <summary>
        /// Glow markers for the current step and room (canvas coordinates, marker index 0..4). In the planner the
        /// positions are relative to the filofax (<paramref name="blockOrigin"/>; null = hidden).
        /// </summary>
        public static IEnumerable<(Vector2Int pos, int mark)> Glows(int room, Vector2Int? blockOrigin)
        {
            if (!Active) yield break;
            int s = Step;
            if (RoomIds.IsOffice(room) && s == TutorialStep.ClickGlobe) yield return (new Vector2Int(311, 165), 0);
            if (room != RoomIds.Globe || blockOrigin == null) yield break;
            var o = blockOrigin.Value;
            switch (s)
            {
                case TutorialStep.OpenPlanes: yield return (o + new Vector2Int(40, 259), 1); break;
                case TutorialStep.OpenPlane: yield return (o + new Vector2Int(36, 57), 3); break;
                case TutorialStep.OpenOrders: yield return (o + new Vector2Int(273, 259), 2); break;
                case TutorialStep.TakeOrder: yield return (o + new Vector2Int(268, 57), 3); break;
                case TutorialStep.PlaceOrder: yield return (o + new Vector2Int(46, 57), 4); break;
            }
        }

        static readonly Dictionary<int, Texture2D> _glow = new Dictionary<int, Texture2D>();
        static GameAssets _glowAssets;

        /// <summary>
        /// White mask of a marker: the marker's raw pixel value v (0..8) is how far the spot stays dark, so the
        /// white share is (8 − v) / 8; the pulse scales it by (8 − S)/16 with S = 4 + 4·sin(t / 150 ms)
        /// (rooms-and-ui-1 §5.3 "glow").
        /// </summary>
        static Texture2D GlowTexture(GameAssets a, int mark)
        {
            if (_glowAssets != a) { _glow.Clear(); _glowAssets = a; }
            if (_glow.TryGetValue(mark, out var t)) return t;
            var bmp = a.Bitmap("gli/glglow.gli", "TUTMARK" + (mark + 1));
            if (bmp == null) { _glow[mark] = null; return null; }
            var px = new Color32[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    int v = Mathf.Min(8, bmp.Pixels[y * bmp.Width + x]);
                    px[y * bmp.Width + x] = new Color32(255, 255, 255, (byte)((8 - v) * 255 / 8));
                }
            t = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "tutmark" + mark };
            t.SetPixels32(px);
            t.Apply(false, false);
            _glow[mark] = t;
            return t;
        }

        /// <summary>Draws the markers of a room (call after the room canvas, before dialogs and menus).</summary>
        public static void DrawGlows(Screen2D s, int canvasX, int room, Vector2Int? blockOrigin)
        {
            if (!Active) return;
            var app = GameApp.Instance;
            float strength = Mathf.Sin(Time.unscaledTime * 1000f / 150f) * 4f + 4f;
            byte alpha = (byte)Mathf.Clamp((8f - strength) / 16f * 255f, 0, 255);
            s.PushClip(new RectInt(canvasX, 0, 640, 440));
            foreach (var (pos, mark) in Glows(room, blockOrigin))
            {
                var t = GlowTexture(app.Assets, mark);
                if (t != null) s.Draw(t, canvasX + pos.x, pos.y, new Color32(255, 255, 255, alpha));
            }
            s.PopClip();
        }

        // ------------------------------------------------------------------ status bar button

        /// <summary>Status-bar tutorial panel (rooms-and-ui-1 §9.5): x range relative to the right edge.</summary>
        public static bool OverButton(int w, Vector2Int m, int top) => m.x >= w - 71 && m.x <= w - 33 && m.y >= top && m.y < top + 40;

        /// <summary>Cancels the tutorial (button or Space): the assistant stops, markers go, the day may start.</summary>
        public static void Cancel(GameApp app)
        {
            var w = app?.Session?.World;
            if (w == null || !w.Tutorial.Active) return;
            app.Advisors.Monolog = false;
            app.Advisors.ClearQueue();
            var r = w.Execute(new CancelTutorialCommand());
            if (r.Ok && w.Clock.State == Sim.Core.DayState.Playing && app.Rooms.CurrentRoom == RoomIds.Aufsicht) app.Rooms.LeaveAll();
        }
    }

    /// <summary>
    /// The per-frame tutorial script of the view (the original runs it once per main-loop iteration): the steps
    /// that wait for the assistant to be silent or for the player to be somewhere. Steps the simulation can
    /// see itself (passengers, the plane at the gate) are in <see cref="TutorialEngine"/>.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        World _world;
        IDisposable _sub;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.Tutorial");
            DontDestroyOnLoad(go);
            go.AddComponent<TutorialDirector>();
        }

        void Bind(World w)
        {
            _sub?.Dispose();
            _sub = null;
            _world = w;
            if (w != null) _sub = w.Events.Subscribe<TutorialAdvanced>(OnAdvanced);
        }

        void OnAdvanced(TutorialAdvanced e)
        {
            var app = GameApp.Instance;
            if (app == null || app.Assets == null) return;
            if (e.SkipCurrent) app.Advisors.SkipCurrent();
            foreach (int id in e.Texts)
            {
                var raw = app.Assets.StdText.Get("Tuto", id);
                if (raw != null) app.Advisors.Queue(AdvisorKind.Assistant, DialogText.Format(raw, e.Args));
            }
        }

        void Update()
        {
            var app = GameApp.Instance;
            if (app == null) return;
            var w = app.Session?.World;
            if (w != _world) Bind(w);
            if (w == null || app.Assets == null) { if (app.Advisors != null) app.Advisors.Monolog = false; return; }
            app.Advisors.Monolog = w.Tutorial.Active && w.Tutorial.Monolog;
            if (!TutorialEngine.Running(w)) return;
            foreach (var sc in app.Screens.All) if (sc is OptionsScreen) return;
            Script(app, w);
        }

        static bool HasHall(GameApp app)
        {
            foreach (var sc in app.Screens.All) if (sc is IStatusBarHost && !(sc is RoomScreen)) return true;
            return false;
        }

        void Script(GameApp app, World w)
        {
            var t = w.Tutorial;
            bool silent = app.Advisors.Silent;
            int local = w.LocalPlayer;
            var hp = w.Hall?.Players[local];
            bool inHall = app.Rooms.Locations.Count == 0;
            switch (t.Step)
            {
                case TutorialStep.NotStarted:
                    if (w.Clock.Date == 0 && HasHall(app)) Step(w, TutorialStep.NotStarted, TutorialStep.Start, null);
                    break;
                case TutorialStep.Start:
                    Step(w, TutorialStep.Start, TutorialStep.Welcome, new[] { 1000, 1001 }, monolog: true);
                    break;
                case TutorialStep.Welcome:
                    if (!silent) break;
                    if (Step(w, TutorialStep.Welcome, TutorialStep.GoToBoss, new[] { 1100, 1101 }, monolog: false) && w.Hall != null)
                        w.Execute(new WalkToRoomCommand(local, RoomIds.Aufsicht));
                    break;
                case TutorialStep.GoToBoss:
                    if (silent && (hp?.Room == RoomIds.Aufsicht || app.Rooms.CurrentRoom == RoomIds.Aufsicht))
                        Step(w, TutorialStep.GoToBoss, TutorialStep.AtBoss, new[] { 1200 });
                    break;
                case TutorialStep.AtBoss:
                    if (silent && app.Rooms.Current is Rooms.Aufsicht.AufsichtRoom boss && !boss.IsDialogOpen)
                    {
                        if (Step(w, TutorialStep.AtBoss, TutorialStep.Briefing, null)) boss.StartBriefing(true);
                    }
                    break;
                case TutorialStep.Briefing:
                    if (inHall && HasHall(app))
                    {
                        if (w.Clock.State == Sim.Core.DayState.Morning) w.Execute(new Sim.Missions.StartWorkingDayCommand());
                        if (Step(w, TutorialStep.Briefing, TutorialStep.GoToOffice, new[] { 1300 }, skip: true) && w.Hall != null)
                            w.Execute(new WalkToRoomCommand(local, RoomIds.OfficeOf(local)));
                    }
                    break;
                case TutorialStep.GoToOffice:
                    if (!inHall) Step(w, TutorialStep.GoToOffice, TutorialStep.InOffice, new[] { 1400 }, new object[] { w.Local.Airline }, skip: true);
                    break;
                case TutorialStep.InOffice:
                    if (silent) Step(w, TutorialStep.InOffice, TutorialStep.ClickGlobe, new[] { 1401 });
                    break;
                case TutorialStep.ClickGlobe:
                    if (app.Rooms.CurrentRoom == RoomIds.Globe) Step(w, TutorialStep.ClickGlobe, TutorialStep.InPlanner, new[] { 1500 }, skip: true);
                    break;
                case TutorialStep.InPlanner:
                    Step(w, TutorialStep.InPlanner, TutorialStep.PlannerIntro, new[] { 1501 });
                    break;
                case TutorialStep.PlannerIntro:
                    if (silent) Step(w, TutorialStep.PlannerIntro, TutorialStep.OpenPlanes, new[] { 1502 });
                    break;
                case TutorialStep.BackToHall:
                    if (inHall) Step(w, TutorialStep.BackToHall, TutorialStep.InHall, new[] { 1600, 1601 }, skip: true);
                    break;
                case TutorialStep.PlaneLeft:
                    if (silent) Step(w, TutorialStep.PlaneLeft, TutorialStep.Farewell, new[] { 2002, 2003 });
                    break;
                case TutorialStep.Farewell:
                    if (silent) Step(w, TutorialStep.Farewell, TutorialStep.Farewell, new[] { 2004, 2005, 2006 }, monolog: false, end: true);
                    break;
            }
        }

        static bool Step(World w, int from, int to, int[] texts, object[] args = null, bool skip = false, bool? monolog = null, bool end = false)
        {
            var r = w.Execute(new TutorialStepCommand(from, to, texts, args, skip, monolog, end));
            if (r.Ok) Debug.Log("OpenAT tutorial: step " + from + " → " + to);
            return r.Ok;
        }
    }
}
