using System.Collections.Generic;
using OpenAT.Sim.Contracts;
using OpenAT.Sim.Fleet;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using UnityEngine;
using Plane = OpenAT.Sim.Fleet.Plane;

namespace OpenAT.View.Rooms.Planner
{
    /// <summary>
    /// The laptop (rooms-and-ui-5 §3, 5b): globe with all visible airlines' planes and routes, rotating
    /// icons that open movable, z-ordered windows (cities, planes with the flight planner, info), the
    /// globe display toggles, warning lights and the physical buttons. Pushed on the room stack from the
    /// laptop item; leaving returns to the previous room.
    /// </summary>
    [Room(RoomIds.Laptop)]
    [ScreenName("laptop")]
    public sealed class LaptopRoom : PlannerRoom
    {
        const string Lib = "room/globe_b.gli", Icons = "room/globe.gli";
        protected override string Library => Lib;
        protected override string Background => "VR";
        protected override bool IsLaptop => true;
        protected override Vector2Int GlobeOrigin => new Vector2Int(177, 29);
        protected override string EarthFile => App.Session?.Local.LaptopVirus > 0 ? "gli/EarthVir.lbm" : "gli/EarthLap.lbm";
        bool Virus => P != null && P.LaptopVirus > 0;
        /// <summary>Battery empty (§3.2); a laptop of model 0 (entered without owning one) never runs out.</summary>
        bool Dead => P != null && P.LaptopQuality > 0 && P.LaptopBattery <= 0;
        int _lastBattery = int.MaxValue;
        static readonly RectInt Monitor = new RectInt(39, 29, 562, 381);
        static readonly string[] ToggleColour = { "B", "V", "R", "G" };

        bool _windowsHidden;
        // Rotating icons (§3.4): exit, cities, planes, info.
        static readonly (int id, int x, int y, string lib, string prefix, int tool)[] IconDefs =
        {
            (0, 545, 353, "room/icon1.gli", "EXIT_", 3000), (1, 63, 121, "room/icon2.gli", "CITY_", 3001),
            (2, 84, 49, "room/icon3.gli", "PLANE_", 3002), (5, 55, 197, "room/icon6.gli", "INFO_", 3005),
        };
        readonly float[] _rot = new float[4], _speed = new float[4];

        // Desk animations (rooms-and-ui-5 §3): the lamp flickers when a window opens and blinks when one
        // closes; the antivirus clip runs with the security company's laptop protection; in virus mode
        // the icons and toggles are replaced by "touch" clips that do nothing useful.
        SmackerPerson _lamp, _antivir;
        int _lampTrigger = -1;
        SmackerPerson[] _virus;
        readonly int[] _virusTrigger = { -1, -1, -1, -1, -1, -1 };

        void SetupDeskClips()
        {
            _lamp = new SmackerPerson(Assets, App.Audio);
            _lamp.Clip(0, "LapLigV2.smk", 80, 403, Mood.Idle, "A1:0", 1, 1, 30, 30).Decide = () => _lampTrigger;
            _lamp.Clip(1, "LapLigh.smk", 80, 403, Mood.Idle, "A3:0 A1:2", canCancel: false).OnStart = () => _lampTrigger = -1;
            _lamp.Clip(2, "LapLigW1.smk", 80, 403, Mood.Idle, "A1:0", 1, 1, 10, 30, canCancel: false).OnStart = () => _lampTrigger = -1;
            _lamp.Clip(3, "LapLigW1.smk", 80, 403, Mood.Idle, "A1:0", 1, 1, 5, 5, canCancel: false).OnStart = () => _lampTrigger = -1;
            _antivir = new SmackerPerson(Assets, App.Audio);
            _antivir.Clip(0, "Antivir.smk", 522, 57, Mood.Idle, "A1:0");
            if (!Virus) return;
            _virus = new SmackerPerson[6];
            void Btn(int i, string idle, string touch, int x, int y, int wait, string sound = null)
            {
                int k = i;
                var sp = new SmackerPerson(Assets, App.Audio);
                sp.Clip(0, idle, x, y, Mood.Idle, "A1:0", 1, 1, wait, wait).Decide = () => _virusTrigger[k];
                if (touch != null) sp.Clip(1, touch, x, y, Mood.Idle, "A1:0", canCancel: false, sound: sound).OnStart = () => _virusTrigger[k] = -1;
                _virus[i] = sp;
            }
            Btn(0, "TDMStand.smk", "TDM.smk", 67, 128, 30, "tdm.raw");     // cities icon
            Btn(1, "TDMStand.smk", "TDM.smk", 88, 53, 30, "tdm.raw");      // planes icon
            Btn(2, "VirR_GLU.smk", "VirR_TOU.smk", 94, 349, 0);             // route toggles
            Btn(3, "VirF_GLU.smk", "VirF_TOU.smk", 68, 278, 0);             // plane toggles
            Btn(4, "TDMStand.smk", "TDM.smk", 58, 207, 30, "tdm.raw");     // info icon
            Btn(5, "ok.smk", null, 545, 359, 30);                          // exit corner
        }

        protected override IReadOnlyList<Block> Blocks => _windowsHidden || State == null ? (IReadOnlyList<Block>)System.Array.Empty<Block>() : State.Windows;

        protected override void OnRoomEnter()
        {
            base.OnRoomEnter();
            App.Audio.Play("SOUND/laptop.raw");
            SetupDeskClips();
            // Every icon spins up when the laptop opens (rotation and speed start at 1000).
            for (int i = 0; i < _rot.Length; i++) { _rot[i] = 1000; _speed[i] = 1000; }
            W.Execute(new Sim.Items.SetLaptopOpenCommand(P.Index, true));
            if (Virus) { Globe.Alpha = 8000; State.Windows.Clear(); }
            var w = W;
            // Foreign toggles need the informer; bankrupt airlines are always off (§3.4).
            for (int p = 0; p < 4; p++)
            {
                if (p == P.Index) continue;
                if (!P.HasAdvisor(Sim.Staff.AdvisorType.Informer) || w.Players[p].IsOut) { State.DisplayPlanes[p] = false; State.DisplayRoutes[p] = 0; }
            }
            // Plane details whose plane is gone are deleted; city details reset (§1.2).
            State.Windows.RemoveAll(b => b.Left.Type == BlockType.Planes && !b.Left.IsIndex && P.Planes.Find(b.Left.SelectedId) == null);
            foreach (var b in State.Windows)
            {
                if (b.Left.IsDetail(BlockType.Cities)) BackToIndex(b.Left);
                b.AnimStart = -100; b.Closing = false;
            }
        }

        protected override PaneGeom Geom(Block b, bool right)
        {
            if (!right)
                return new PaneGeom
                {
                    Client = new Vector2Int(21, 22), Title = new Vector2Int(41, 7), PageNo = new Vector2Int(104, 195),
                    Prev = PaneGeom.R(0, 23, 19, 44), Next = PaneGeom.R(0, 165, 19, 186), Index = PaneGeom.R(16, 0, 33, 19),
                    InputShift = new Vector2Int(-1, -1),
                };
            return new PaneGeom
            {
                Client = new Vector2Int(206, 22), Title = new Vector2Int(228, 7), PageNo = new Vector2Int(291, 195),
                Prev = PaneGeom.R(375, 23, 395, 44), Next = PaneGeom.R(375, 165, 395, 186), Index = PaneGeom.R(202, 0, 219, 19),
                InputShift = new Vector2Int(1, -1),
            };
        }

        protected override bool ShowPlanesOf(int player) => !Virus && State.DisplayPlanes[player];

        protected override void OnLeaveRoom()
        {
            if (W != null && P != null) W.Execute(new Sim.Items.SetLaptopOpenCommand(P.Index, false));
            base.OnLeaveRoom();
        }

        protected override bool InputBlocked(InputState input)
        {
            if (P == null) return false;
            int b = P.LaptopBattery;
            if (P.LaptopQuality > 0 && b != _lastBattery)
            {
                if (b == 2) App.Audio.Play("SOUND/lapwarn.raw");
                if (b <= 0 && _lastBattery > 0 && _lastBattery != int.MaxValue) App.Audio.Play("SOUND/lapdown.raw");
                _lastBattery = b;
            }
            if (!Dead) return false;
            var c = new Vector2Int(input.Mouse.x - RX, input.Mouse.y);
            if (input.LeftDown && !input.Consumed && In(c, 37, 398, 74, 439)) { input.Consume(); App.Audio.Play("SOUND/laptop1.raw"); Leave(); }
            return true;
        }

        protected override Vector2Int ClampBlock(Block b, Vector2Int pos) =>
            new Vector2Int(Mathf.Clamp(pos.x, 49 - b.Size.x / 2, 600 - b.Size.x / 2), Mathf.Clamp(pos.y, 29, 380));

        protected override void BringToFront(Block b)
        {
            if (State.Windows.Remove(b)) State.Windows.Insert(0, b);
        }

        protected override void BlockRightClick(Block b) => CloseWindow(b);

        void CloseWindow(Block b)
        {
            if (b.Closing) return;
            _lampTrigger = 3;   // the lamp blinks
            b.Closing = true;
            b.AnimStart = Time.unscaledTime;
        }

        // Open/close sweep (rooms-and-ui-5b §1.3): a phase from -7 to 38 at 40 steps per second (two per
        // 20 Hz tick); closing runs it backwards. 0..1 = fraction of that range.
        const float PhaseFrom = -7, PhaseTo = 38, PhasePerSecond = 40;
        const float AnimTime = (PhaseTo - PhaseFrom) / PhasePerSecond;

        float AnimPhase(Block b)
        {
            float t = (Time.unscaledTime - b.AnimStart) / AnimTime;
            return b.Closing ? 1 - Mathf.Clamp01(t) : Mathf.Clamp01(t);
        }

        int SweepPhase(Block b) => Mathf.FloorToInt(PhaseFrom + AnimPhase(b) * (PhaseTo - PhaseFrom));

        protected override bool BlockContentVisible(Block b) => AnimPhase(b) >= 1;

        Block NewWindow(int type, Vector2Int pos)
        {
            if (State.Windows.Count >= 19) return null;
            var b = new Block { Style = 1, WindowType = type, Double = type != BlockType.Info, AnimStart = Time.unscaledTime };
            b.Left.Type = type;
            if (type == BlockType.Planes) b.Right.Type = BlockType.Orders;
            b.Pos = ClampBlock(b, pos);
            UpdatePages(b.Left, b);
            UpdatePages(b.Right, b);
            State.Windows.Insert(0, b);
            _lampTrigger = 1;   // the lamp flickers
            return b;
        }

        protected override void OpenPlaneFromGlobe(Plane pl, Vector2Int canvasPos)
        {
            var b = NewWindow(BlockType.Planes, canvasPos);
            if (b == null) return;
            b.Pos = new Vector2Int(Mathf.Clamp(canvasPos.x, 39, 600 - b.Size.x), Mathf.Clamp(canvasPos.y, 29, 409 - b.Size.y));
            OpenDetail(b, b.Left, pl.Id);
            b.Left.Pages = 4;
        }

        protected override int MainPlaneId
        {
            get
            {
                if (State == null || _windowsHidden) return -1;
                foreach (var b in State.Windows) if (!b.Closing && b.Left.IsDetail(BlockType.Planes)) return b.Left.SelectedId;
                return -1;
            }
        }

        /// <summary>Fleet index pick: the front plane window shows the plane (keeping its page), else a new window opens.</summary>
        protected override void OpenPlaneFromFleet(Plane pl)
        {
            _windowsHidden = false;
            foreach (var b in State.Windows)
            {
                if (b.Closing || !b.Left.IsDetail(BlockType.Planes)) continue;
                int page = b.Left.Page;
                b.Left.SelectedId = pl.Id;
                UpdatePages(b.Left, b);
                b.Left.Page = Mathf.Min(page, b.Left.Pages - 1);
                BringToFront(b);
                return;
            }
            OpenPlaneWindow(pl);
        }

        protected override bool CanOpenPlaneWindow => !Virus;

        protected override bool OpenPlaneWindow(Plane pl)
        {
            if (Virus) return false;
            _windowsHidden = false;
            State.WindowPos.TryGetValue(BlockType.Planes, out var pos);
            var b = NewWindow(BlockType.Planes, pos + new Vector2Int(12 * (State.Windows.Count % 6), 12 * (State.Windows.Count % 6)));
            if (b == null) return false;
            OpenDetail(b, b.Left, pl.Id);
            return true;
        }

        protected override bool CanArrange => !Virus && !_windowsHidden && State.Windows.Count > 1;

        /// <summary>Cascades the open windows over the monitor, the front window last (on top, lowest right).</summary>
        protected override void ArrangeWindows()
        {
            int i = 0;
            for (int k = State.Windows.Count - 1; k >= 0; k--)
            {
                var b = State.Windows[k];
                if (b.Closing) continue;
                int step = i % 9;
                b.Pos = ClampBlock(b, new Vector2Int(44 + 18 * step, 31 + 18 * step));
                i++;
            }
            App.Audio.Play("SOUND/click.raw");
        }

        void OpenFromIcon(int type)
        {
            foreach (var b in State.Windows)
                if (b.WindowType == type && b.Left.IsIndex && !b.Closing) { BringToFront(b); return; }
            _speed[IconIndex(type)] += 1000;   // the spin boost only when a new window opens
            State.WindowPos.TryGetValue(type, out var pos);
            NewWindow(type, pos);
        }

        static int IconIndex(int type) => type == BlockType.Cities ? 1 : type == BlockType.Planes ? 2 : 3;

        public override void Animate(float dt)
        {
            base.Animate(dt);
            if (State == null) return;
            // Finished close animations: remove and remember the position (§1.3).
            for (int i = State.Windows.Count - 1; i >= 0; i--)
            {
                var b = State.Windows[i];
                if (b.Closing && AnimPhase(b) <= 0)
                {
                    State.WindowPos[b.WindowType] = b.Pos;
                    State.Windows.RemoveAt(i);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                _rot[i] += _speed[i] * dt * 20;
                _speed[i] = Mathf.Max(0, _speed[i] - 20 * dt * 20);
            }
            UpdateScrollDrag();
            _lamp?.Update(dt);
            if (P != null && (P.SecurityFlags & 2) != 0) _antivir?.Update(dt);
            if (_virus != null) foreach (var v in _virus) v.Update(dt);
        }

        // ------------------------------------------------------------------ chrome

        bool In(Vector2Int l, int x1, int y1, int x2, int y2) => l.x >= x1 && l.x <= x2 && l.y >= y1 && l.y <= y2;

        bool OverClose(Block b, Vector2Int l) => b.Double ? In(l, 359, 0, 380, 20) : In(l, 173, 3, 191, 19);
        bool RightIsList(Block b) => b.Double && b.Left.Type == BlockType.Planes && b.Right.IsIndex;
        int SwitchAt(Block b, Vector2Int l)
        {
            if (!RightIsList(b)) return -1;
            // The pane type already shown is not a button (original: no hot cursor, no click).
            for (int k = 0; k < 3; k++) if (In(l, 217 + 20 * k, 5, 235 + 20 * k, 20) && b.Right.Type != SwitchTypes[k]) return k;
            return -1;
        }
        static readonly int[] SwitchTypes = { BlockType.Orders, BlockType.Freight, BlockType.Routes };

        Block _scrollBlock;
        bool _scrollRight;
        int _scrollGrab;

        /// <summary>Thumb drag: the page follows the thumb's top, (y − 43) · (pages − 1) / 95, clamped.</summary>
        void UpdateScrollDrag()
        {
            if (_scrollBlock == null) return;
            var input = App.InputNow;
            if (!input.LeftHeld || _scrollBlock.Closing) { _scrollBlock = null; return; }
            var pane = _scrollBlock.PaneOf(_scrollRight);
            if (pane.Pages <= 1) { _scrollBlock = null; return; }
            int ly = input.Mouse.y - _scrollBlock.Pos.y - _scrollGrab;
            pane.Page = Mathf.Clamp((ly - 43) * (pane.Pages - 1) / 95, 0, pane.Pages - 1);
        }

        /// <summary>Previous/next element of the pane's list, wrapping around (Laptop quick-jump).</summary>
        void Jump(Block b, Pane pane, int dir)
        {
            // OpenAT: planes follow the fleet index's filtered order.
            if (Enhanced && pane.Type == BlockType.Planes) { JumpPlane(b, pane, dir); return; }
            var ids = new List<int>();
            switch (pane.Type)
            {
                case BlockType.Planes: foreach (var p in PlaneList(b)) ids.Add(p.Id); break;
                case BlockType.Cities: foreach (var c in CityList()) ids.Add(c.Id); break;
                case BlockType.Orders: foreach (var o in OrderList()) ids.Add(o.Id); break;
                case BlockType.Freight: foreach (var f in FreightList()) ids.Add(f.Id); break;
                case BlockType.Routes: ids.AddRange(RouteList()); break;
            }
            int i = ids.IndexOf(pane.SelectedId);
            if (i < 0 || ids.Count == 0) return;
            int page = pane.Page;
            pane.SelectedId = ids[(i + dir + ids.Count) % ids.Count];
            UpdatePages(pane, b);
            // Quirk (verified on the running original): after a jump a plane window has 3 + photos pages, one fewer.
            if (pane.Type == BlockType.Planes && pane.Pages > 1) pane.Pages--;
            pane.Page = Mathf.Min(page, pane.Pages - 1);   // stays on the same page where possible
            if (pane.Type == BlockType.Cities) TurnToCity(pane.SelectedId);
        }

        protected override bool ChromeClick(Block b, Vector2Int l, InputState input)
        {
            if (AnimPhase(b) < 1) return true;
            if (OverClose(b, l)) { CloseWindow(b); return true; }
            int sw = SwitchAt(b, l);
            if (sw >= 0)
            {
                b.SwitchType(b.Right, SwitchTypes[sw]);
                b.Right.IsIndex = true;
                UpdatePages(b.Right, b);
                return true;
            }
            // Quick-jump to the previous/next element of a detail page (not on Info windows).
            for (int side = 0; side < 2; side++)
            {
                if (side == 1 && !b.Double) continue;
                var pane = b.PaneOf(side == 1);
                if (pane.IsIndex || pane.Type == BlockType.Info) continue;
                int jx = side == 0 ? 167 : 331;
                if (In(l, jx, 4, jx + 10, 19)) { Jump(b, pane, -1); return true; }
                if (In(l, jx + 11, 4, jx + 21, 19)) { Jump(b, pane, +1); return true; }
            }
            // Scrollbars: only the 29 px thumb can be dragged; it keeps the grab offset (§2).
            for (int side = 0; side < 2; side++)
            {
                if (side == 1 && !b.Double) continue;
                var pane = b.PaneOf(side == 1);
                if (pane.Pages <= 1) continue;
                int x0 = side == 0 ? 5 : 376, ty = 43 + 95 * pane.Page / (pane.Pages - 1);
                if (In(l, x0, ty, x0 + 15, ty + 29)) { _scrollBlock = b; _scrollRight = side == 1; _scrollGrab = l.y - ty; return true; }
            }
            if (In(l, 34, 2, 165, 19) || (b.Double && In(l, 223, 2, 356, 19))) { BeginMove(b, l); return true; }
            return false;
        }

        protected override void ChromeHover(Block b, Vector2Int l)
        {
            if (OverClose(b, l)) App.Ui.Cursor = CursorLook.Hot;
            int sw = SwitchAt(b, l);
            if (sw >= 0) App.Ui.Cursor = CursorLook.Hot;   // no tooltip on the laptop switches
            for (int side = 0; side < 2; side++)
            {
                if (side == 1 && !b.Double) continue;
                var pane = b.PaneOf(side == 1);
                int jx = side == 0 ? 167 : 331;
                if (!pane.IsIndex && pane.Type != BlockType.Info && In(l, jx, 4, jx + 21, 19)) App.Ui.Cursor = CursorLook.Hot;
                if (pane.Pages > 1)
                {
                    int x0 = side == 0 ? 5 : 376, ty = 43 + 95 * pane.Page / (pane.Pages - 1);
                    if (In(l, x0, ty, x0 + 15, ty + 29)) App.Ui.Cursor = CursorLook.Hot;
                }
            }
        }

        protected override void DrawBlockChrome(Screen2D s, Block b, int x, int y, Vector2Int l)
        {
            var win = Assets.Texture(Lib, b.Double ? "BIGWIN" : "WINDOW");
            if (AnimPhase(b) >= 1)
            {
                s.Draw(win, x, y);
                // Part of the window, under the texts (Block.cpp order): the index arrows of detail pages and,
                // for a cities window or a plane window with a detail on the right, a blank title row.
                if (!b.Left.IsIndex) s.Draw(Assets.Texture(Lib, "INHALT0"), x + 16, y);
                bool rightDetail = b.Double && b.Left.Type == BlockType.Planes && b.Right.Type != BlockType.None && !b.Right.IsIndex;
                if (b.Left.Type == BlockType.Cities || rightDetail)
                    for (int k = 0; k < 4; k++) s.Draw(Assets.Texture(Lib, "SWITCH7"), x + 217 + 20 * k, y + 5);
                if (rightDetail) s.Draw(Assets.Texture(Lib, "INHALT0"), x + 203, y);
                return;
            }
            DrawSweep(s, b, win, x, y, SweepPhase(b));
        }

        /// <summary>
        /// The laptop window build-up (rooms-and-ui-5b §1.3): two corner pieces slide apart along the title
        /// bar revealing it (phase 0..9), slide down the sides (10..19), along the bottom (20..29); from
        /// phase 22 the whole window fades in, at 38 it is complete. Before phase 0 the corners sit together.
        /// </summary>
        void DrawSweep(Screen2D s, Block b, Texture win, int x, int y, int phase)
        {
            var e0 = Assets.Texture(Lib, "LEFT1"); var e1 = Assets.Texture(Lib, "RIGHT1");
            var e2 = Assets.Texture(Lib, "LEFT2"); var e3 = Assets.Texture(Lib, "RIGHT2");
            int W = win.width, H = win.height;
            void Part(int x1, int y1, int x2, int y2)
            {
                if (x2 <= x1 || y2 <= y1) return;
                s.Draw(win, x + x1, y + y1, new RectInt(x1, y1, x2 - x1, y2 - y1), Color.white);
            }
            bool dbl = b.Double;
            int mid = dbl ? 198 : 100;
            if (phase < 0)
            {
                int cx = dbl ? 178 : 89;
                s.Draw(e0, x + cx, y); s.Draw(e1, x + cx + 5, y);
                return;
            }
            int p = Mathf.Min(phase, 9);
            Part(mid - p * mid / 9, 0, mid + p * (dbl ? 198 : 99) / 9, 15);
            if (phase >= 10)
            {
                int q = Mathf.Min(phase, 19) - 10, h = q * (H - (dbl ? 19 : 13)) / 9;
                Part(0, 0, 13, h);
                if (dbl) Part(383, 0, 396, h); else Part(196, 0, 199, h);
            }
            if (phase >= 20)
            {
                int q = Mathf.Min(phase, 29) - 20;
                if (dbl) { Part(0, 197, q * 198 / 18, 210); Part(198 - q * 198 / 18, 197, 198 + q * 198 / 18, 210); }
                else { Part(0, 197, q * 99 / 9, 210); Part(199 - q * 100 / 9, 197, 199, 210); }
            }
            if (phase < 10)
            {
                if (dbl) { s.Draw(e0, x + 178 - phase * 178 / 9, y); s.Draw(e1, x + 188 + phase * 188 / 9, y); }
                else { s.Draw(e0, x + 80 - phase * 80 / 9, y); s.Draw(e1, x + 90 + phase * 89 / 9, y); }
            }
            else if (phase < 20)
            {
                int dy = (phase - 10) * (H - (dbl ? 16 : 19)) / 9;
                s.Draw(e2, x, y + dy); s.Draw(e3, x + W - (dbl ? 18 : 20), y + dy);
            }
            else if (phase < 30)
            {
                int by = y + H - e2.height - (dbl ? 2 : 0);
                if (dbl)
                {
                    s.Draw(e2, x + (phase - 20) * 82 / 18, by); s.Draw(e3, x + 377 - (phase - 20) * 83 / 18, by);
                    s.Draw(e3, x + 178 - (phase - 20) * 73 / 18, by); s.Draw(e2, x + 188 + (phase - 20) * 81 / 18, by);
                }
                else { s.Draw(e2, x + (phase - 20) * 90 / 9, by); s.Draw(e3, x + 179 - (phase - 20) * 89 / 9, by); }
            }
            if (phase >= 22) s.Draw(win, x, y, new Color32(255, 255, 255, (byte)Mathf.Clamp((phase - 22) * 255 / 16, 0, 255)));
        }

        protected override void DrawBlockOverlay(Screen2D s, Block b, int x, int y, Vector2Int l)
        {
            // Buttons (rooms-and-ui-5b §2, laptop): frame 1 while hovered, frame 2 while pressed.
            bool held = App.InputNow.LeftHeld;
            void Hov(string n, int hx, int hy, bool cond) { if (cond) s.Draw(Assets.Texture(Lib, n + (held ? "2" : "1")), x + hx, y + hy); }
            for (int side = 0; side < 2; side++)
            {
                if (side == 1 && !b.Double) continue;
                var pane = b.PaneOf(side == 1);
                var g = Geom(b, side == 1);
                Hov(side == 0 ? "UPLEFT" : "UPRIGHT", side == 0 ? 0 : 376, 22, pane.Page > 0 && PaneGeom.In(g.Prev, l));
                Hov(side == 0 ? "DWNLEFT" : "DWNRIGT", side == 0 ? 0 : 376, side == 0 ? 168 : 169, pane.Page < pane.Pages - 1 && PaneGeom.In(g.Next, l));
                if (!pane.IsIndex && PaneGeom.In(g.Index, l))
                    s.Draw(Assets.Texture(Lib, held ? "INHALT2" : "INHALT1"), x + (side == 0 ? 16 : 203), y);
                // Quick-jump arrows on detail pages: JUMP0 normally, JUMP1/2 (back) or JUMP3/4 (forward) hovered/pressed.
                if (!pane.IsIndex && pane.Type != BlockType.Info)
                {
                    int jx = side == 0 ? 167 : 331;
                    string jn = In(l, jx, 4, jx + 10, 19) ? "JUMP" + (held ? 2 : 1) : In(l, jx + 11, 4, jx + 21, 19) ? "JUMP" + (held ? 4 : 3) : "JUMP0";
                    if (Assets.Has(Lib, jn)) s.Draw(Assets.Texture(Lib, jn), x + jx, y + 4);
                }
                if (pane.Pages > 1)
                {
                    int ty = 43 + 95 * pane.Page / (pane.Pages - 1);
                    s.Draw(Assets.Texture(Lib, side == 0 ? "SCROLLL" : "SCROLLR"), x + (side == 0 ? 5 : 376), y + ty);
                }
                // Route lock: on the route list, only with the security company's route protection (flag 16).
                if (side == 1 && pane.IsIndex && pane.Type == BlockType.Routes && (P.SecurityFlags & 16) != 0) s.Draw(Assets.Texture(Lib, "LOCK"), x + 341, y + 6);
            }
            if (RightIsList(b))
            {
                // The active type is drawn as SWITCH1..3, a hovered other one as SWITCH4..6.
                int hov = SwitchAt(b, l);
                for (int k = 0; k < 3; k++)
                {
                    bool active = b.Right.Type == SwitchTypes[k];
                    if (active) s.Draw(Assets.Texture(Lib, "SWITCH" + (k + 1)), x + 217 + 20 * k, y + 5);
                    else if (hov == k) s.Draw(Assets.Texture(Lib, "SWITCH" + (k + 4)), x + 217 + 20 * k, y + 5);
                }
            }
            if (OverClose(b, l)) s.Draw(Assets.Texture(Lib, held ? "CLOSE2" : "CLOSE1"), x + (b.Double ? 360 : 174), y);
        }

        // ------------------------------------------------------------------ desk (outside windows)

        bool OverIcon(Vector2Int c, int i) => c.x >= IconDefs[i].x && c.x < IconDefs[i].x + 49 && c.y >= IconDefs[i].y && c.y < IconDefs[i].y + 49;

        int ToggleAt(Vector2Int c, int bx, int by)
        {
            if (c.x < bx || c.y < by || c.x >= bx + 46 || c.y >= by + 46) return -1;
            int col = (c.x - bx) / 23, row = (c.y - by) / 23;
            return col * 2 + row;
        }

        bool ToggleAllowed(int p) => p == P.Index || (P.HasAdvisor(Sim.Staff.AdvisorType.Informer) && !W.Players[p].IsOut);

        protected override bool WorldLeftDown(Vector2Int c, InputState input)
        {
            if (!Monitor.Contains(c) && !In(c, 37, 398, 111, 439) && !In(c, 608, 312, 640, 394)) return false;
            if (In(c, 37, 398, 74, 439)) { App.Audio.Play("SOUND/laptop1.raw"); Leave(); return true; }
            if (In(c, 74, 398, 111, 439)) { App.Audio.Play("SOUND/laptop2.raw"); return true; }
            if (In(c, 608, 313, 640, 350)) { App.Audio.Play("SOUND/laptop3.raw"); _windowsHidden = !_windowsHidden; return true; }
            if (In(c, 608, 358, 640, 394)) { App.Audio.Play("SOUND/laptop4.raw"); return true; }
            for (int i = 0; i < IconDefs.Length; i++)
            {
                if (!OverIcon(c, i)) continue;
                if (i == 0) { Leave(); return true; }
                if (_virus != null) { _virusTrigger[i == 1 ? 0 : i == 2 ? 1 : 4] = 1; return true; }   // virus: the icon only twitches
                OpenFromIcon(i == 1 ? BlockType.Cities : i == 2 ? BlockType.Planes : BlockType.Info);
                return true;
            }
            if (_virus != null)
            {
                if (ToggleAt(c, 66, 277) >= 0) { _virusTrigger[3] = 1; return true; }
                if (ToggleAt(c, 92, 348) >= 0) { _virusTrigger[2] = 1; return true; }
            }
            int t = ToggleAt(c, 66, 277);
            if (t >= 0) { if (ToggleAllowed(t)) State.DisplayPlanes[t] = !State.DisplayPlanes[t]; return true; }
            t = ToggleAt(c, 92, 348);
            if (t >= 0) { if (ToggleAllowed(t)) State.DisplayRoutes[t] = (State.DisplayRoutes[t] + 1) % 3; return true; }
            if (In(c, 136, 225, 170, 264) || In(c, 555, 225, 589, 264)) return true;
            return base.WorldLeftDown(c, input);
        }

        protected override void WorldHover(Vector2Int c, InputState input)
        {
            base.WorldHover(c, input);
            for (int i = 0; i < IconDefs.Length; i++)
                if (OverIcon(c, i))
                {
                    App.Ui.Cursor = CursorLook.Hot;
                    App.Ui.Tooltip.Set(IconDefs[i].tool);
                    _speed[i] = Mathf.Min(1600, _speed[i] + Time.unscaledDeltaTime * 2000);   // hovering alone speeds the icon up
                }
            int t = ToggleAt(c, 66, 277);
            if (t >= 0 && ToggleAllowed(t)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(UI.Dialog.DialogText.Format(Std("Tool", 3006), new object[] { W.Players[t].Airline })); }
            t = ToggleAt(c, 92, 348);
            if (t >= 0 && ToggleAllowed(t)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(UI.Dialog.DialogText.Format(Std("Tool", 3007), new object[] { W.Players[t].Airline })); }
            if (In(c, 136, 225, 170, 264)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(3010); if (input.LeftHeld) TurnDir = -1; }
            if (In(c, 555, 225, 589, 264)) { App.Ui.Cursor = CursorLook.Hot; App.Ui.Tooltip.Set(3011); if (input.LeftHeld) TurnDir = 1; }
            if (In(c, 37, 398, 74, 439) || In(c, 608, 312, 638, 350)) App.Ui.Cursor = CursorLook.Hot;
        }

        // ------------------------------------------------------------------ drawing

        public override void Draw(Screen2D s)
        {
            // The monitor area of the background is transparent: black screen behind it.
            s.FillRect(new RectInt(CanvasX + Monitor.x, Monitor.y, Monitor.width, Monitor.height), new Color32(0, 0, 0, 255));   // black screen, like the original
            base.Draw(s);
        }

        protected override void DrawRoom(Screen2D s)
        {
            if (W == null || State == null) return;
            s.PushClip(new RectInt(RX + Monitor.x, Monitor.y, Monitor.width, Monitor.height));
            var sel = new List<Plane>();
            if (!_windowsHidden)
                foreach (var b in State.Windows) if (SelectedPlane(b) is Plane pl && !sel.Contains(pl)) sel.Add(pl);
            PrepareGlobe(sel, true);
            DrawGlobeWithPlanes(s, Lib);
            // City markers.
            if (!_windowsHidden)
                foreach (var b in State.Windows)
                    if (b.Left.IsDetail(BlockType.Cities) && W.City(b.Left.SelectedId) is Sim.Data.City city && Globe.Project(city.GlobeX, city.GlobeY, out var o))
                        s.Draw(Assets.Texture(Std2, "CITYCUR"), RX + GlobeOrigin.x + GlobeView.CentreOffset.x + (int)o.x - 18, GlobeOrigin.y + GlobeView.CentreOffset.y + (int)o.y - 18);

            var m = App.InputNow.Mouse;
            var c = new Vector2Int(m.x - RX, m.y);
            bool overBlock = HitBlock(m) != null;
            // Icons (virus mode: the virus clips instead of icons and toggles).
            if (_virus != null) foreach (var v in _virus) v.Draw(s, RX);
            for (int i = 0; i < IconDefs.Length && _virus == null; i++)
            {
                var d = IconDefs[i];
                int frame = ((int)_rot[i] >> 10) % 51;
                string digits = d.prefix.Length >= 6 ? frame.ToString("00") : frame.ToString("000");
                string name = d.prefix + digits;
                var tex = Assets.Has(d.lib, name) ? Assets.Texture(d.lib, name) : Assets.Texture(Icons, d.prefix + (d.prefix.Length >= 6 ? "00" : "000"));
                s.Draw(tex, RX + d.x, d.y);
                if (!overBlock && OverIcon(c, i)) s.Draw(Assets.Texture(Icons, "HELL"), RX + d.x, d.y);
            }
            s.Draw(Assets.Texture(Std2, !overBlock && In(c, 136, 225, 170, 264) ? "IB_01" : "IB_00"), RX + 136, 225);
            s.Draw(Assets.Texture(Std2, !overBlock && In(c, 555, 225, 589, 264) ? "IC_01" : "IC_00"), RX + 555, 225);
            // Globe display toggles.
            if (_virus == null) s.Draw(Assets.Texture(Lib, "PLANE"), RX + 66, 277);
            if (_virus == null) s.Draw(Assets.Texture(Lib, "ROUTE"), RX + 92, 348);
            for (int p = 0; p < 4 && _virus == null; p++)
            {
                int cx = (p / 2) * 23, cy = (p % 2) * 23;
                if (State.DisplayPlanes[p]) s.Draw(Assets.Texture(Lib, "PLANE_" + ToggleColour[p] + "1"), RX + 66 + cx, 277 + cy);
                if (State.DisplayRoutes[p] > 0) s.Draw(Assets.Texture(Lib, "ROUTE_" + ToggleColour[p] + State.DisplayRoutes[p]), RX + 92 + (p / 2) * 24, 348 + cy);   // route column 2 at x 116
            }
            // Warning lights (§3.4).
            bool blink = ((int)(Time.unscaledTime * 1000) & 1023) < 512;
            int today = W.Clock.Date;
            bool open = false, due = false, fopen = false, fdue = false, routes = P.LicensedRouteCount > 0;
            foreach (var o2 in P.Contracts.Items) if (o2.InPlan == PlanState.NotPlanned) { open = true; if (o2.LastDate <= today) due = true; }
            foreach (var o2 in P.Freights.Items) if (o2.TonsOpen > 0 && o2.InPlan != -1) { fopen = true; if (o2.LastDate <= today) fdue = true; }
            if (open && (!due || blink)) s.Draw(Assets.Texture(Lib, "WARNING1"), RX + 59, 40);
            if (fopen && (!fdue || blink)) s.Draw(Assets.Texture(Lib, "WARNING2"), RX + 48, 61);
            if (routes) s.Draw(Assets.Texture(Lib, "WARNING3"), RX + 60, 82);

            DrawBlocks(s);
            s.PopClip();
            // Monitor corners over the windows (rounded screen edges).
            if (Assets.Has(Icons, "VR_EDGE1"))
            {
                var c1 = Assets.Texture(Icons, "VR_EDGE1"); var c2 = Assets.Texture(Icons, "VR_EDGE2");
                var c3 = Assets.Texture(Icons, "VR_EDGE3"); var c4 = Assets.Texture(Icons, "VR_EDGE4");
                s.Draw(c1, RX + 39, 29); s.Draw(c2, RX + 601 - c2.width, 29);
                s.Draw(c3, RX + 39, 410 - c3.height); s.Draw(c4, RX + 601 - c4.width, 410 - c4.height);
            }
            if (!Dead) _lamp?.Draw(s, RX);
            if (P != null && (P.SecurityFlags & 2) != 0) _antivir?.Draw(s, RX);
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (Dead) { s.Draw(Assets.Texture("room/globe_c.gli", "VR2"), RX, 0); return; }
            DrawCarry(s);
        }

        protected override void Hotspots(InputState input) { }
    }
}
