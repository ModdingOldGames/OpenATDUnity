using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim.Data;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Designer
{
    /// <summary>
    /// Aircraft constructor (rooms-and-ui-7 §6, room 124): a part strip (body, cockpit, tail, wings,
    /// engines) at the bottom, the blueprint board where parts snap onto the attachment slots of the
    /// relation graph (<see cref="PlaneDesignEditor"/>), live statistics on the right, the design name
    /// at the top (click to rename), prev/next design, new, delete, OK (save + leave) and cancel.
    /// Designs are <c>.plane</c> files in <see cref="DesignFiles.Directory"/>. Right click never leaves.
    /// </summary>
    [Room(RoomIds.Editor)]
    [ScreenName("editor")]
    public sealed class EditorRoom : RoomScreen
    {
        const string Lib = "room/editor.gli";
        protected override string Library => Lib;
        protected override string Background => "EDITOR";

        const int IdRename = 10, IdPrev = 800, IdNext = 801, IdDelete = 900, IdNew = 901, IdCancel = 998, IdOk = ExitId, IdPart = 10000;

        /// <summary>Editor bitmaps in part catalogue order (B1..B5, C1..C5, H1..H7, R1..R6, M1..M8, L1..L6).</summary>
        static readonly string[] PartBitmaps = BuildNames(new[] { ("BODY_A", 5), ("CPIT_A", 5), ("HECK_A", 7), ("RWNG_A", 6), ("MOT_A", 8), ("LWNG_A", 6) }, "00");
        /// <summary>Strip icons: per slot the first index into <see cref="StripBitmaps"/>.</summary>
        static readonly string[] StripBitmaps = BuildNames(new[] { ("S_B_", 5), ("S_C_", 5), ("S_H_", 7), ("S_W_", 6), ("S_M_", 8) }, "00");
        static readonly int[] StripFirst = { 0, 5, 10, 17, 23 };

        static string[] BuildNames((string prefix, int n)[] groups, string fmt)
        {
            var l = new List<string>();
            foreach (var (p, n) in groups) for (int i = 1; i <= n; i++) l.Add(p + i.ToString(fmt));
            return l.ToArray();
        }

        PlaneDesignLibrary _lib;
        PlaneDesignEditor _ed;
        PlaneDesignCatalog _cat;
        string _file;
        readonly int[] _sel = new int[5];
        bool[] _enabled = { true, false, false, false, false };
        string _carry;
        /// <summary>DragDropMode of the original: 0 = no, 1 = picked from the strip this press, −1 = picked up from the board.</summary>
        int _drag;
        DesignSnap _snap;
        int _hover = -1;
        AudioSource _loop;
        readonly Dictionary<string, Texture2D> _outlines = new Dictionary<string, Texture2D>();

        UiKit Kit => App.Ui.Kit;
        string DefaultName => Assets.StdText.Get("Misc", 8210) ?? "noname";

        protected override void OnCreate()
        {
            _cat = DesignFiles.Catalog(App);
            _lib = DesignFiles.Library(App);
            _ed = new PlaneDesignEditor(_cat, new PlaneDesign { Name = DefaultName }, SizeOf);
            if (IsPhonePane) return;
            _lib.EnsureDirectory();
            _file = PlaneDesignLibrary.WorkFile;
            if (_lib.Exists(_file)) LoadDesign(_file);
            UpdateSlots();
            _sel[PlaneDesignEditor.SlotWing] = _ed.SkipUnusableWing(_sel[PlaneDesignEditor.SlotWing], 1, _enabled[PlaneDesignEditor.SlotWing]);
        }

        protected override void OnRoomEnter()
        {
            if (PlayerPrefs.GetFloat(GameAudio.PrefAmbience, 1f) > 0) _loop = App.Audio.PlayLoop("SOUND/secure.raw", 0.6f);
        }

        protected override void OnLeaveRoom()
        {
            if (_loop != null) { App.Audio.StopLoop(_loop); _loop = null; }
            foreach (var t in _outlines.Values) if (t != null) Object.Destroy(t);
            _outlines.Clear();
        }

        // ------------------------------------------------------------------ files

        void LoadDesign(string file)
        {
            var d = _lib.Load(file) ?? new PlaneDesign();
            if (string.IsNullOrEmpty(d.Name)) d.Name = DefaultName;
            _ed.Design = d;
        }

        void SaveDesign() { if (_file != null) _lib.Save(_file, _ed.Design); }

        void NewDesign()
        {
            SaveDesign();
            _file = _lib.NewFileName() ?? PlaneDesignLibrary.WorkFile;
            _ed.Design = new PlaneDesign { Name = DefaultName + PlaneDesignLibrary.NumberPart(_file) };
        }

        void Step(int add)
        {
            SaveDesign();
            _ed.Design = new PlaneDesign { Name = DefaultName };
            _file = _lib.Next(_file, add) ?? _file;
            LoadDesign(_file);
        }

        void DeleteCurrent()
        {
            _lib.Delete(_file);
            _ed.Design = new PlaneDesign { Name = DefaultName };
            var next = _lib.Next(_file, -1);
            if (next != null) { _file = next; LoadDesign(next); }
        }

        void ClearBoard() { _ed.Design.Parts.Clear(); UpdateSlots(); }

        void AskClear(bool deleteFile)
        {
            Menus.Open(RequestMenu.Create(RequestMenu.KillPlane, null, () =>
            {
                if (deleteFile) DeleteCurrent(); else ClearBoard();
                UpdateSlots();
            }), new Vector2Int(320, 220), 0.17f);
        }

        /// <summary>OK and the exit id save the design before leaving; cancel leaves without saving.</summary>
        protected override void Leave()
        {
            SaveDesign();
            base.Leave();
        }

        // ------------------------------------------------------------------ bitmaps

        string PartBitmap(string shortName)
        {
            int i = _cat.IndexOfPart(shortName);
            return i >= 0 && i < PartBitmaps.Length ? PartBitmaps[i] : null;
        }

        (int, int) SizeOf(string shortName)
        {
            var b = PartBitmap(shortName) is string n ? Assets.Bitmap(Lib, n) : null;
            return b != null ? (b.Width, b.Height) : (40, 40);
        }

        Texture2D PartTex(string shortName) => PartBitmap(shortName) is string n ? Assets.Texture(Lib, n) : null;

        /// <summary>White one-pixel outline around the opaque pixels ("ColorFX.BlitOutline"), drawn at (x−1, y−1).</summary>
        Texture2D Outline(string shortName)
        {
            if (_outlines.TryGetValue(shortName, out var t)) return t;
            var name = PartBitmap(shortName);
            var b = name != null ? Assets.Bitmap(Lib, name) : null;
            if (b == null) { _outlines[shortName] = null; return null; }
            int w = b.Width + 2, h = b.Height + 2;
            var px = new Color32[w * h];
            bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < b.Width && y < b.Height && b[x, y] != Bitmap16.ColorKey;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = x - 1, sy = y - 1;
                    if (Opaque(sx, sy)) continue;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        for (int dx = -1; dx <= 1 && !edge; dx++)
                            if ((dx != 0 || dy != 0) && Opaque(sx + dx, sy + dy)) edge = true;
                    // Same row order as GameAssets.ToTexture (first row = top of the bitmap).
                    if (edge) px[y * w + x] = new Color32(255, 255, 255, 255);
                }
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "outline " + shortName };
            t.SetPixels32(px);
            t.Apply(false, false);
            _outlines[shortName] = t;
            return t;
        }

        bool HitsPixel(DesignPart p, int mx, int my)
        {
            var name = PartBitmap(p.ShortName);
            var b = name != null ? Assets.Bitmap(Lib, name) : null;
            if (b == null) return false;
            int x = mx - p.Pos3dX, y = my - p.Pos3dY;
            return x >= 0 && y >= 0 && x < b.Width && y < b.Height && b[x, y] != Bitmap16.ColorKey;
        }

        // ------------------------------------------------------------------ input

        void UpdateSlots() => _enabled = _ed.EnabledSlots();

        Vector2Int MouseCanvas(InputState input) => new Vector2Int(input.Mouse.x - RX, input.Mouse.y);

        public override void Update(InputState input, float dt)
        {
            bool free = Menus != null && !Menus.IsOpen && !IsDialogOpen && input.Mouse.y < Screen.Height - StatusBar.Height;
            var m = MouseCanvas(input);
            _snap = _carry != null ? _ed.FindSnap(_carry, m.x, m.y) : default;
            if (free && input.RightDown && !input.Consumed)
            {
                input.Consume();
                RightClick(m);
            }
            if (free && input.LeftUp && !input.Consumed) LeftUp();
            base.Update(input, dt);   // hotspots (Hotspots), clicks (OnClick), menus, keys, status bar
        }

        void LeftUp()
        {
            if (_carry != null && _snap.Snapped && _drag != -1)
            {
                _carry = _ed.Place(_carry, _snap, _drag == 1);
                UpdateSlots();
            }
            _drag = 0;
            SkipWing(1);
        }

        void SkipWing(int dir)
        {
            int w = PlaneDesignEditor.SlotWing;
            _sel[w] = _ed.SkipUnusableWing(_sel[w], dir, _enabled[w]);
        }

        void RightClick(Vector2Int m)
        {
            if (_carry != null) { _carry = null; return; }
            if (_hover >= IdPart)
            {
                int i = _hover - IdPart;
                if (_ed.NeedsClearConfirm(i)) { AskClear(false); return; }
                _ed.Remove(i);
                UpdateSlots();
                return;
            }
            for (int c = 0; c < 5; c++)
            {
                if (m.x < 27 + c * 127 || m.x > 101 + c * 127 || m.y < 363 || m.y > 436) continue;
                if (c == PlaneDesignEditor.SlotBody && _ed.Design.Parts.Count > 1) { AskClear(false); return; }
                _ed.RemoveCategory(c);
                UpdateSlots();
                return;
            }
        }

        protected override void Hotspots(InputState input)
        {
            var m = MouseCanvas(input);
            _hover = -1;
            // Registration order follows the original's SetMouseLook calls (the last match wins).
            if (Hot(602, 192, 640, 224, IdCancel, 0, CursorLook.Exit)) _hover = IdCancel;
            if (Hot(602, 158, 640, 188, IdOk, 0, CursorLook.Exit)) _hover = IdOk;
            if (Hot(0, 192, 38, 224, IdDelete)) _hover = IdDelete;
            if (Hot(0, 158, 38, 188, IdNew)) _hover = IdNew;
            if (Hot(185, 0, 212, 23, IdPrev)) _hover = IdPrev;
            if (Hot(438, 0, 465, 23, IdNext)) _hover = IdNext;
            if (Hot(212, 0, 438, 15, IdRename)) _hover = IdRename;
            for (int c = 0; c < 5; c++)
            {
                if (!_enabled[c]) continue;
                if (Hot(4 + c * 127, 370, 27 + c * 127, 426, c * 100 + 100)) _hover = c * 100 + 100;
                if (Hot(101 + c * 127, 370, 124 + c * 127, 426, c * 100 + 101)) _hover = c * 100 + 101;
                if (Hot(27 + c * 127, 363, 101 + c * 127, 436, c * 100 + 105)) _hover = c * 100 + 105;
            }
            if (_carry != null) return;
            // Placed parts: engines first, then the others, topmost first; pixel-exact.
            var parts = _ed.Design.Parts;
            for (int pass = 1; pass <= 2; pass++)
                for (int d = parts.Count - 1; d >= 0; d--)
                {
                    bool motor = parts[d].ShortName.Length > 0 && parts[d].ShortName[0] == 'M';
                    if (motor != (pass == 1) || !HitsPixel(parts[d], m.x, m.y)) continue;
                    Hot(m.x, m.y, m.x, m.y, IdPart + d);
                    _hover = IdPart + d;
                    return;
                }
        }

        protected override void OnClick(int id)
        {
            switch (id)
            {
                case IdCancel: App.Rooms.Leave(); return;
                case IdRename:
                    Menus.Open(new TextEntryMenu(MenuIds.RenameEditPlane, 3099, _ed.Design.Name, n => _ed.Design.Name = n, new object[] { _ed.Design.Name }),
                        new Vector2Int(320, 10), 0.17f);
                    return;
                case IdDelete:
                    if (_ed.Design.Parts.Count > 1) AskClear(true); else { DeleteCurrent(); UpdateSlots(); }
                    return;
                case IdNew: NewDesign(); UpdateSlots(); return;
                case IdPrev: Step(-1); UpdateSlots(); return;
                case IdNext: Step(1); UpdateSlots(); return;
            }
            if (id >= IdPart)
            {
                int i = id - IdPart;
                if (_ed.NeedsClearConfirm(i)) { AskClear(false); return; }
                _carry = _ed.Remove(i);
                _drag = -1;
                UpdateSlots();
                return;
            }
            if (id >= 100 && id < 600)
            {
                int c = id / 100 - 1, k = id % 100, n = PlaneDesignEditor.SlotVariants[c];
                if (k == 0) _sel[c] = (_sel[c] - 1 + n) % n;
                else if (k == 1) _sel[c] = (_sel[c] + 1) % n;
                else if (k == 5) { _carry = PlaneDesignEditor.VariantName(c, _sel[c]); _drag = 1; }
                if (c == PlaneDesignEditor.SlotWing) SkipWing(k == 0 ? -1 : 1);
                // Cycling the strip also swaps a carried part of that category.
                if (k <= 1 && _carry != null && _carry[0] == PlaneDesignEditor.SlotCategory[c])
                    _carry = PlaneDesignEditor.VariantName(c, _sel[c]);
            }
        }

        // ------------------------------------------------------------------ drawing

        void Blit(Screen2D s, Texture2D t, int x, int y) { if (t != null) s.Draw(t, RX + x, RY + y); }
        const int RY = 0;

        void Button(Screen2D s, string prefix, int id, int x, int y)
        {
            bool hot = _hover == id && !Menus.IsOpen && !IsDialogOpen;
            int state = hot ? (App.InputNow.LeftHeld ? 2 : 1) : 0;
            Blit(s, Assets.Texture(Lib, prefix + state), x, y);
        }

        protected override void DrawRoom(Screen2D s)
        {
            var parts = _ed.Design.Parts;
            bool idle = !Menus.IsOpen && !IsDialogOpen;
            int carryZ = 0;
            if (_carry != null)
                carryZ = PlanePart.ZBaseOf(_carry[0]) + _snap.Pos3dY + (_snap.Relation >= 0 ? _cat.Relations[_snap.Relation].ZAdd : 0);
            bool carried = false;
            for (int d = 0; d < parts.Count; d++)
            {
                var p = parts[d];
                if (_carry != null && !carried && carryZ <= PlanePart.ZBaseOf(p.ShortName[0]) + p.Pos3dY)
                {
                    Blit(s, PartTex(_carry), _snap.Pos3dX, _snap.Pos3dY);
                    carried = true;
                }
                Blit(s, PartTex(p.ShortName), p.Pos3dX, p.Pos3dY);
            }
            if (parts.Count > 0) DrawStats(s);
            if (_carry != null && !carried) Blit(s, PartTex(_carry), _snap.Pos3dX, _snap.Pos3dY);
            if (_carry != null && _snap.Snapped && idle)
            {
                Blit(s, Outline(_carry), _snap.Pos3dX - 1, _snap.Pos3dY - 1);
                var twin = PlaneDesignEditor.TwinOf(_carry);
                if (twin != null) Blit(s, Outline(twin), _snap.TwinPos3dX - 1, _snap.TwinPos3dY - 1);
            }
            Blit(s, Assets.Texture(Lib, "MASKE_O"), 0, 0);
            Blit(s, Assets.Texture(Lib, "MASKE_U"), 0, 343);

            // Design name, centred in (193,7)-(471,28).
            Kit.NormalGreen.Draw(s, RX + 332, 7, _ed.Design.Name ?? "", TextAlign.Center);

            // Strip: the selected variant of each slot, translucent when the slot is disabled.
            for (int c = 0; c < 5; c++)
            {
                var icon = Assets.Texture(Lib, StripBitmaps[StripFirst[c] + _sel[c]]);
                if (icon == null) continue;
                int cx = 66 + 127 * c;
                int x = cx - icon.width / 2, y = 399 - icon.height / 2;
                if (_enabled[c]) Blit(s, icon, x, y);
                else s.Draw(icon, RX + x, y, new Color32(255, 255, 255, 110));
                if (!_enabled[c]) continue;
                Button(s, "BUT_TL", c * 100 + 100, 4 + c * 127, 370);
                Button(s, "BUT_TR", c * 100 + 101, 101 + c * 127, 370);
            }
            Button(s, "CANCEL", IdCancel, 602, 192);
            Button(s, "DELETE", IdDelete, 0, 192);
            Button(s, "NEW", IdNew, 0, 158);
            Button(s, "OK", IdOk, 602, 158);
            Button(s, "BUT_DL", IdPrev, 185, 0);
            Button(s, "BUT_DR", IdNext, 438, 0);

            if (idle && _carry == null && _hover >= IdPart && _hover - IdPart < parts.Count)
            {
                var p = parts[_hover - IdPart];
                Blit(s, Outline(p.ShortName), p.Pos3dX - 1, p.Pos3dY - 1);
            }
        }

        string Std(string g, int id) => Assets.StdText.Get(g, id) ?? "";

        void DrawStats(Screen2D s)
        {
            var st = _ed.Design.Stats(_cat);
            long cost = st.Cost;
            int weight = st.Weight, seats = st.Seats, burn = st.Burn, noise = st.Noise, maint = st.Maintenance, speed = st.Speed, range = st.Range;
            // Display tank: 7 × wing weight without the 22-hour cap ("CalcTank(true)").
            int tank = 0;
            foreach (var p in _ed.Design.Parts)
                if (p.ShortName.Length > 0 && (p.ShortName[0] == 'L' || p.ShortName[0] == 'R')) tank += (_cat.Part(p.ShortName)?.Weight ?? 0) * 7;
            // Hovering an enabled strip slot shows that single part.
            if (!Menus.IsOpen && !IsDialogOpen)
            {
                var m = MouseCanvas(App.InputNow);
                for (int c = 0; c < 5; c++)
                {
                    if (!_enabled[c] || m.x < 4 + c * 127 || m.x > 124 + c * 127 || m.y < 363 || m.y > 436) continue;
                    var q = _cat.Part(PlaneDesignEditor.VariantName(c, _sel[c]));
                    if (q == null) break;
                    weight = q.Weight; seats = q.Seats; burn = 0; noise = q.Noise; maint = q.Maintenance; cost = q.Cost; speed = 0; tank = 0; range = 0;
                    break;
                }
            }
            var lines = new List<(int y, string text)>();
            lines.Add((20, DialogText.Format(Std("Misc", 8200), new object[] { weight })));
            if (seats > 0) lines.Add((35, DialogText.Format(Std("Misc", 8201), new object[] { seats, seats / 10 })));
            if (burn > 0) lines.Add((50, DialogText.Format(Std("Misc", 8202), new object[] { burn })));
            if (noise != 0) lines.Add((65, DialogText.Format(Std("Misc", 8204), new object[] { Std("Misc", PlaneDesignEditor.NoiseLabel(noise)), System.Math.Abs(noise) })));
            lines.Add((80, DialogText.Format(Std("Misc", 8206), new object[] { Std("Misc", PlaneDesignEditor.MaintenanceLabel(maint)) })));
            if (speed > 0) lines.Add((95, DialogText.Format(Std("Misc", 8207), new object[] { UiKit.Unit(UiKit.UnitKmh, speed) })));
            if (tank > 0) lines.Add((110, Std("Plne", 1008) + ": " + UiKit.Unit(UiKit.UnitL, tank)));
            if (range > 0) lines.Add((125, Std("Plne", 1001) + ": " + UiKit.Unit(UiKit.UnitKm, range)));
            lines.Add((150, DialogText.Format(Std("Misc", 8203), new object[] { UiKit.Money(cost) })));
            var black = Kit.SmallBlack;
            var yellow = Kit.Stat(4);
            foreach (var (y, text) in lines)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        black.Draw(s, RX + 480 + dx, y + dy, text);
                yellow.Draw(s, RX + 480, y, text);
            }
            int err = _ed.Design.Error(_cat);
            if (err != 0) Kit.Stat(3).DrawBlock(s, new RectInt(RX + 480, 180, 100, 120), Std("Misc", err), Color.white);
        }
    }
}
