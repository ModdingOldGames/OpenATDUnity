using System;
using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>What a screen that shows the status bar offers to it (a room, the airport hall, ...).</summary>
    public interface IStatusBarHost
    {
        GameApp App { get; }
        bool IsDialogOpen { get; }
        MenuHost Menus { get; }
        /// <summary>Left click on a non-empty inventory slot (rooms-and-ui-1 §9.4).</summary>
        void UseItem(int slot, int item);
        /// <summary>Screen x of the 640-wide room canvas (menus zoom from canvas coordinates).</summary>
        int CanvasX { get; }
    }

    /// <summary>
    /// Inventory access. The item model belongs to the items domain (Sim, not yet written); until then
    /// the status bar reads a public <c>Items</c> field/property (byte[]/int[]) on <see cref="Player"/>
    /// if one exists, else <c>Player.Extensions["items"]</c>. 0xFF / -1 = empty slot.
    /// </summary>
    public static class Inventory
    {
        public const int Slots = 6;
        public const int Empty = 0xFF;

        /// <summary>Replaceable accessor (set by the items domain once it exists).</summary>
        public static Func<Player, IList<int>> Get = DefaultGet;

        static IList<int> DefaultGet(Player p)
        {
            if (p == null) return Array.Empty<int>();
            object v = null;
            var t = p.GetType();
            var f = t.GetField("Items");
            if (f != null) v = f.GetValue(p);
            else
            {
                var pr = t.GetProperty("Items");
                if (pr != null) v = pr.GetValue(p);
            }
            if (v == null) p.Extensions.TryGetValue("items", out v);
            switch (v)
            {
                case byte[] b: return Array.ConvertAll(b, x => (int)x);
                case IList<int> l: return l;
                case IList<byte> lb: { var r = new int[lb.Count]; for (int i = 0; i < r.Length; i++) r[i] = lb[i]; return r; }
            }
            return Array.Empty<int>();
        }

        public static int At(Player p, int slot)
        {
            var l = Get(p);
            if (slot < 0 || slot >= l.Count) return Empty;
            int v = l[slot];
            return v < 0 || v >= 0xFF ? Empty : v;
        }
    }

    /// <summary>
    /// The bottom status bar (rooms-and-ui-1 §9): airline logo, clock (game speed), weekday + time,
    /// money, six inventory slots, "repeat last message" and options icons. Full screen width: the
    /// left group is anchored to the left edge, the two icons to the right edge, the extra width in
    /// between is filled with the empty-slot panel.
    /// </summary>
    public sealed class StatusBar
    {
        public const int Height = 40;
        int _pressedIcon; // 1 repeat, 2 options, 3 tutorial
        float _pressedUntil;

        /// <summary>Called when the options icon is clicked (in-game options).</summary>
        public static Action<GameApp> OpenOptions = app => app.OpenOptions();
        /// <summary>Called when the airline logo is clicked (statistics room).</summary>
        public static Action<GameApp> OpenStatistics = app => app.Rooms.ToggleRoom(Rooms.Framework.RoomIds.Statistics);

        public int Top(Screen2D s) => s.Height - Height;

        static bool Before9(GameApp app) => app.Session != null && app.Session.World.Clock.Time < Sim.Core.GameClock.DayStart && app.Session.World.Clock.Date > 0;

        /// <summary>Hover/clicks. Returns true if the bar handled the click.</summary>
        public bool Update(IStatusBarHost host, InputState input)
        {
            var app = host.App;
            var session = app.Session;
            if (session == null) return false;
            var s = app.Screen;
            int top = Top(s), W = s.Width;
            var m = input.Mouse;
            if (m.y < top) return false;
            var ui = app.Ui;
            var player = session.Local;
            bool busy = host.IsDialogOpen || host.Menus.IsOpen;
            int ry = m.y - top;

            // Hotspots (§5.3 table), status bar coordinates relative to its top-left / right edge.
            if (m.x >= W - 30)
            {
                if (ry < 20) { if (app.Advisors.HasLast) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(2100); } }
                else { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(1000); }
            }
            // In the statistics room the logo is that room's exit button (EXIT cursor, no tooltip).
            else if (m.x < 80 && host is Rooms.Framework.RoomScreen rs && rs.RoomId == Rooms.Framework.RoomIds.Statistics) ui.Cursor = CursorLook.Exit;
            else if (m.x < 80) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(2102); }
            else if (m.x <= 102 && ry < 20) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(1003); }
            else if (m.x <= 242 && ry < 20) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(1001); }
            else if (m.x <= 242) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(1002); }
            else if (m.x >= 248 && m.x < 248 + 60 * Inventory.Slots)
            {
                int slot = (m.x - 248) / 60, item = Inventory.At(player, slot);
                if (item != Inventory.Empty) { ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(2000 + item); }
            }

            // Holding the button on the time display accelerates time (rooms-and-ui-9 §7).
            session.Driver.ClockClickFactor = 1;
            if (m.x >= 102 && m.x <= 255 && ry < 20 && !busy)
                session.Driver.ClockClickFactor = input.LeftHeld ? 5 : input.RightHeld ? 20 : 1;

            // Tutorial panel left of the icons (rooms-and-ui-1 §9.5): click cancels the tutorial.
            bool tutorial = Tutorial.TutorialView.Active;
            if (tutorial && !busy && Tutorial.TutorialView.OverButton(W, m, top))
            {
                ui.Cursor = CursorLook.Hot; ui.Tooltip.Set(2101);
                if (input.LeftDown && !input.Consumed)
                {
                    Press(3);
                    Tutorial.TutorialView.Cancel(app);
                    input.Consume(); return true;
                }
            }

            if (input.Consumed) return false;
            if (input.LeftDown)
            {
                if (m.x >= 78 && m.x <= 101 && ry < 20)
                {
                    player.GameSpeed = (player.GameSpeed + 1) % 4;
                    input.Consume(); return true;
                }
                if (m.x >= 80 && m.x <= 242 && ry >= 20)
                {
                    if (!host.Menus.IsOpen)
                        host.Menus.Open(new KontoauszugMenu(), new Vector2Int(100, 470), 0.17f, screenOrigin: true);
                    input.Consume(); return true;
                }
                if (m.x >= W - 30)
                {
                    if (ry >= 20)
                    {
                        if (!busy && !Before9(app)) { Press(2); OpenOptions(app); }
                    }
                    else if (app.Advisors.HasLast) { Press(1); app.Advisors.RepeatLast(); }
                    input.Consume(); return true;
                }
                if (m.x >= 248 && m.x < 248 + 60 * Inventory.Slots)
                {
                    int slot = (m.x - 248) / 60, item = Inventory.At(player, slot);
                    if (item != Inventory.Empty && !host.Menus.IsOpen) host.UseItem(slot, item);
                    input.Consume(); return true;
                }
                if (m.x < 78)
                {
                    if (!busy && !Before9(app)) OpenStatistics(app);
                    input.Consume(); return true;
                }
            }
            if (input.RightDown)
            {
                if (m.x >= 78 && m.x <= 101 && ry < 20)
                {
                    player.GameSpeed = (player.GameSpeed + 3) % 4;
                    input.Consume(); return true;
                }
                if (m.x >= 248 && m.x < 248 + 60 * Inventory.Slots && !busy)
                {
                    int slot = (m.x - 248) / 60, item = Inventory.At(player, slot);
                    if (item != Inventory.Empty)
                    {
                        var name = app.Assets.StdText.Get("Tool", 2000 + item) ?? "";
                        host.Menus.Open(RequestMenu.Create(RequestMenu.KillItem, new object[] { name },
                            () => DiscardItem(player, slot)), new Vector2Int(250 + slot * 60, 446), 0.17f, screenOrigin: true);
                    }
                    input.Consume(); return true;
                }
            }
            return false;
        }

        static void DiscardItem(Player p, int slot)
        {
            var l = Inventory.Get(p);
            if (l.IsReadOnly) { Rooms.Items.ItemUseView.Drop(Boot.GameApp.Instance, p, slot); return; }
            if (l is int[]) return;
            if (slot < l.Count) l[slot] = Inventory.Empty;
        }

        void Press(int icon) { _pressedIcon = icon; _pressedUntil = Time.unscaledTime + 0.4f; }

        public void Draw(Screen2D s, IStatusBarHost host, InputState input)
        {
            var app = host.App;
            var session = app.Session;
            var kit = app.Ui.Kit;
            int top = Top(s), W = s.Width;
            s.FillRect(new RectInt(0, top, W, Height), Color.black);
            s.Draw(kit.Std("STATLIN0"), 0, top);
            // Extra width: stretched middle of the empty slot panel.
            var slotTex = kit.Std("STATLIN5");
            int extraX = 248 + 60 * Inventory.Slots, extraW = W - 32 - extraX;
            if (extraW > 0)
                s.DrawScaled(slotTex, new Rect(extraX, top, extraW, Height), new Rect(8, 0, slotTex.width - 16, slotTex.height), new Color32(200, 200, 200, 255));
            var right = kit.Std("STATLIN6");
            s.Draw(right, W - right.width, top);
            if (session == null) return;
            var w = session.World;
            var player = session.Local;
            s.Draw(kit.Std("LOGO" + Mathf.Clamp(player.Index, 0, 6)), 7, top + 3);
            s.Draw(kit.Std("CLOCK" + Mathf.Clamp(player.GameSpeed, 0, 3)), 79, top);

            bool busy = host.IsDialogOpen || host.Menus.IsOpen;
            var m = input.Mouse;
            if (!busy && m.x >= 78 && m.x <= 241 && m.y >= top && m.y < top + 20) s.Draw(kit.Std("TIMEHIGH"), 78, top);
            if (!busy && m.x >= 78 && m.x <= 241 && m.y >= top + 20 && m.y < top + 40) s.Draw(kit.Std("GELDHIGH"), 78, top + 21);

            var font = kit.Cash;
            string day = app.Assets.StdText.Get("Schd", 3000 + w.Clock.DisplayWeekday) ?? "";
            font.Draw(s, 110, top + 2, day + "  " + w.Clock.ClockText.Trim());
            font.Draw(s, 110, top + 21, MoneyText(app, player.Money));

            for (int i = 0; i < Inventory.Slots; i++)
            {
                int item = Inventory.At(player, i);
                var tex = item != Inventory.Empty && kit.HasStd("ITEM" + item.ToString("00")) ? kit.Std("ITEM" + item.ToString("00")) : slotTex;
                s.Draw(tex, 248 + 60 * i, top);
            }

            int ix = W - right.width + 1;
            bool pressed = Time.unscaledTime < _pressedUntil;
            if (Tutorial.TutorialView.Active && kit.HasStd("TUT01"))
            {
                var tut = kit.Std("TUT01");
                int tx = ix - tut.width;
                s.Draw(tut, tx, top);
                if (pressed && _pressedIcon == 3) s.Draw(kit.Std("TUT03"), tx + 8, top + 11);
                else if (!busy && Tutorial.TutorialView.OverButton(W, m, top)) s.Draw(kit.Std("TUT02"), tx + 8, top + 11);
            }
            if (app.Advisors.HasLast)
            {
                bool hov = m.x >= W - 30 && m.y >= top && m.y < top + 20;
                s.Draw(kit.Std(pressed && _pressedIcon == 1 ? "REPEAT03" : hov ? "REPEAT02" : "REPEAT01"), ix, top + 1);
            }
            {
                bool hov = !busy && m.x >= W - 30 && m.y >= top + 20;
                s.Draw(kit.Std(pressed && _pressedIcon == 2 ? "STAT03" : hov ? "STAT02" : "STAT01"), ix, top + 21);
            }
        }

        /// <summary>Money with thousands separators; from 1e9 on in millions with the unit suffix (§9.2).</summary>
        public static string MoneyText(GameApp app, long money)
        {
            if (Math.Abs(money) >= 1000000000L)
            {
                var unit = app.Assets.Texts("ein_ger.res").Get(1000, 1000 + 1) ?? "Mio";
                return UiKit.Thousands(money / 1000000) + " " + unit;
            }
            return UiKit.Thousands(money);
        }
    }
}
