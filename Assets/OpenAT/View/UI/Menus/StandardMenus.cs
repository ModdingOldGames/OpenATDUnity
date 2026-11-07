using System;
using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.View.Render;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.UI.Menus
{
    /// <summary>
    /// MENU_REQUEST (rooms-and-ui-2 §4.5): yes/no question or OK notice on request.gli REQUEST.
    /// Text = std "Misc" 3010 + sub-type, formatted with the parameters. Enter/J = yes, N/Esc = no.
    /// The effects of each sub-type belong to their domains; callers pass them as callbacks.
    /// </summary>
    public sealed class RequestMenu : PopupMenu
    {
        public const int CallItADay = 1, ThrownOut = 2, KillCity = 3, KillRoute = 4, WrongRoom = 5, KillItem = 6,
            KillAuftrag = 7, KillAuftrag0 = 8, KillPlan = 9, WrongRoom2 = 10, ThrownOut2 = 11, RentRoute = 12,
            NoRentRoute1 = 13, NoRentRoute4 = 16, Forbidden = 17, Destroyed = 18, Items = 19, NoGlue = 30, NoStench = 31,
            KillFAuftrag = 27, KillFAuftrag0 = 28, KillPlane = 38;

        public int Sub;
        public object[] Args = Array.Empty<object>();
        public Action Yes, No;

        public static RequestMenu Create(int sub, object[] args, Action yes, Action no = null) =>
            new RequestMenu { Sub = sub, Args = args ?? Array.Empty<object>(), Yes = yes, No = no };

        /// <summary>OK-only notices (§4.5 "plain set").</summary>
        public bool Plain => (Sub >= NoRentRoute1 && Sub <= NoRentRoute4) || Sub == Forbidden || Sub == Destroyed || Sub == Items
            || Sub == NoGlue || Sub == NoStench || (Sub >= 20 && Sub <= 26) || (Sub >= 32 && Sub <= 37);

        public override int Type => MenuIds.Request;
        public override Vector2Int Size => new Vector2Int(290, 200);
        public override Texture Background => Assets.Texture("gli/request.gli", "REQUEST");

        void Answer(bool yes)
        {
            Close();
            if (yes) Yes?.Invoke(); else No?.Invoke();
        }

        public override void Update(InputState input, Vector2Int l)
        {
            if (Plain)
            {
                if (Hot(l, 100, 160, 190, 200) && input.LeftDown) Answer(true);
                return;
            }
            bool y = Hot(l, 0, 160, 100, 200), n = Hot(l, 190, 160, 290, 200);
            if (input.LeftDown && (y || n)) Answer(y);
        }

        public override bool OnKey(InputState input)
        {
            foreach (char c in input.Typed)
            {
                char u = char.ToUpperInvariant(c);
                if (u == 'J' || u == 'Y') { Answer(true); return true; }
                if (u == 'N') { Answer(false); return true; }
            }
            if (input.Enter) { Answer(true); return true; }
            if (input.Escape) { Answer(false); return true; }
            return false;
        }

        public override void OnRightClick() => Answer(false);

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.BigGrey;
            string text = DialogText.Format(Std("Misc", 3010 + Sub), Args);
            f.DrawBlock(s, new RectInt(x + 10, y + 10, 270, 150), text, Color.white);
            // The buttons have no hover effect; hovering only switches to the hot cursor (rooms-and-ui-2 §2.5).
            if (Plain) f.Draw(s, x + 145, y + 165, Std("Misc", 3002), TextAlign.Center);
            else
            {
                f.Draw(s, x + 16, y + 165, Std("Misc", 3000));
                f.Draw(s, x + 274, y + 165, Std("Misc", 3001), TextAlign.Right);
            }
        }
    }

    /// <summary>MENU_CLOSED (rooms-and-ui-2 §4.6): opening-hours notice, std "Misc" 3200 + kind.</summary>
    public sealed class ClosedMenu : PopupMenu
    {
        public int Kind;
        public ClosedMenu(int kind) => Kind = kind;
        public override int Type => MenuIds.Closed;
        public override Vector2Int Size => new Vector2Int(290, 200);
        public override Texture Background => Assets.Texture("gli/request.gli", "REQUEST");

        public override void Update(InputState input, Vector2Int l)
        {
            if (Hot(l, 100, 160, 190, 200) && input.LeftDown) Close();
        }

        public override bool OnKey(InputState input)
        {
            if (input.Enter || input.Escape) { Close(); return true; }
            return false;
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.BigGrey;
            var lines = Std("Misc", 3200 + Kind).Split(TextEncoding.LineBreak);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int ly = y + 20 + 21 * i;
                int colon = line.IndexOf(':');
                if (colon > 0 && colon < line.Length - 1)
                {
                    f.Draw(s, x + 20, ly, line.Substring(0, colon + 1));
                    f.Draw(s, x + 270, ly, line.Substring(colon + 1).Trim(), TextAlign.Right);
                }
                else f.Draw(s, x + 20, ly, line);
            }
            f.Draw(s, x + 145, y + 165, Std("Misc", 3002), TextAlign.Center);   // no hover effect, hot cursor only
        }
    }

    /// <summary>
    /// MENU_KONTOAUSZUG (rooms-and-ui-2 §4.17, economy §4): bank statement, 10 pages of 10 lines,
    /// opens on the newest page. Data: <see cref="Player.Statement"/> (oldest first, max 100).
    /// </summary>
    public sealed class KontoauszugMenu : PopupMenu
    {
        const string Lib = "gli/konto.gli";
        int _page = 9;

        public override int Type => MenuIds.Kontoauszug;
        public override Vector2Int Size => new Vector2Int(400, 214);
        public override Texture Background => Assets.Texture(Lib, "TIPMONEY");

        Player P => App.Session?.Local;

        /// <summary>The 100-slot history with empty slots in front (the original's fixed array).</summary>
        StatementLine?[] Slots()
        {
            var a = new StatementLine?[Player.StatementLines];
            var st = P?.Statement;
            if (st == null) return a;
            int off = a.Length - st.Count;
            for (int i = 0; i < st.Count; i++) if (off + i >= 0) a[off + i] = st[i];
            return a;
        }

        bool PageHasLines(StatementLine?[] slots, int page)
        {
            for (int i = page * 10; i < page * 10 + 10; i++)
                if (slots[i].HasValue && !slots[i].Value.Separator) return true;
            return false;
        }

        public override void Update(InputState input, Vector2Int l)
        {
            var slots = Slots();
            bool older = _page > 0 && PageHasLines(slots, _page - 1);
            bool r = _page < 9 && Hot(l, 374, 186, 400, 214, 0, CursorLook.Right);
            bool lft = older && Hot(l, 0, 189, 30, 214, 0, CursorLook.Left);
            if (!input.LeftDown) return;
            if (r) { _page++; Paper(); }
            else if (lft) { _page--; Paper(); }
            else Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var p = P;
            if (p == null) return;
            var f = Kit.BankBlack ?? Kit.SmallBlack;
            var red = Kit.BankRed ?? Kit.SmallRed;
            var clock = App.Session.World.Clock;
            var date = clock.CalendarDate;
            string wd = Std("Schd", 3010 + clock.DisplayWeekday);
            f.Draw(s, x + 28, y + 11, DialogText.Format(Std("Mony", 1000), new object[] { wd, date.Day, date.Month }));
            f.Draw(s, x + 390, y + 11, (_page + 1) + "/10", TextAlign.Right);
            f.Draw(s, x + 36, y + 34, Std("Mony", 1003));
            Amount(s, f, red, x + 378, y + 34, -p.Credit);

            var slots = Slots();
            long opening = p.HistoricMoney;
            for (int i = 0; i < _page * 10; i++) if (slots[i].HasValue) opening += slots[i].Value.Amount;
            f.Draw(s, x + 36, y + 54, Std("Mony", 1004));
            Amount(s, f, red, x + 378, y + 54, opening);
            long bal = opening;
            int row = 0;
            for (int i = _page * 10; i < _page * 10 + 10; i++)
            {
                if (!slots[i].HasValue) continue;
                var line = slots[i].Value;
                bal += line.Amount;
                if (line.Separator) continue;
                string text = Describe(line);
                f.Draw(s, x + 36, y + 72 + 11 * row, Clip(f, text, 270));
                Amount(s, f, red, x + 378, y + 72 + 11 * row, line.Amount);
                row++;
            }
            f.Draw(s, x + 36, y + 193, Std("Mony", 1005));
            Amount(s, f, red, x + 378, y + 193, bal);

            if (_page < 9) s.Draw(Assets.Texture(Lib, "RIGHT"), x + 374, y + 196);
            if (_page > 0 && PageHasLines(slots, _page - 1)) s.Draw(Assets.Texture(Lib, "LEFT"), x, y + 196);
        }

        string Describe(StatementLine line)
        {
            string t = Assets.StdText.Get("Mony", line.Reason) ?? Assets.StdText.Get("Mone", line.Reason) ?? ("#" + line.Reason);
            if (line.Detail != null) t = t.Contains("%") ? DialogText.Format(t, new object[] { line.Detail }) : t + " " + line.Detail;
            return t;
        }

        static string Clip(BitmapFont f, string t, int w)
        {
            if (f.Measure(t) <= w) return t;
            while (t.Length > 1 && f.Measure(t + "...") > w) t = t.Substring(0, t.Length - 1);
            return t + "...";
        }

        static void Amount(Screen2D s, BitmapFont f, BitmapFont red, int right, int y, long v) =>
            (v < 0 ? red : f).Draw(s, right, y, UiKit.Money(v), TextAlign.Right);
    }

    /// <summary>A letter or fax for <see cref="LettersMenu"/>.</summary>
    public sealed class LetterData
    {
        public string Subject, Body, Sender;
        public bool IsFax;
        /// <summary>Fax picture (SALZ, SKELETT, REIFEN, FEUER, SUPERMAN, KAPUTT, STREIK) or null.</summary>
        public string Picture;
    }

    /// <summary>
    /// MENU_LETTERS (rooms-and-ui-2 §4.7). The mailbox model belongs to another domain; letters are
    /// read through <see cref="Source"/> (default: <c>Player.Extensions["letters"]</c> as a
    /// List&lt;LetterData&gt;, newest first).
    /// </summary>
    public sealed class LettersMenu : PopupMenu
    {
        const string Lib = "gli/letter.gli";
        public static Func<Player, IList<LetterData>> Source = p =>
            p != null && p.Extensions.TryGetValue("letters", out var v) && v is IList<LetterData> l ? l : (IList<LetterData>)Array.Empty<LetterData>();

        int _page;
        public override int Type => MenuIds.Letters;
        public override Vector2Int Size => new Vector2Int(300, 390);
        public override Vector2Int? FixedPos => new Vector2Int(170, 25);
        public override Texture Background => Assets.Texture(Lib, "LETTER");
        IList<LetterData> Letters => Source(App.Session?.Local);

        public override void OnOpen() => App.Audio.Play("SOUND/pap3.raw");

        public override void Update(InputState input, Vector2Int l)
        {
            var ls = Letters;
            bool prev = _page > 0 && Hot(l, 6, 332, 56, 389, 4000, CursorLook.Left);
            bool next = _page < ls.Count - 1 && Hot(l, 244, 335, 298, 389, 4002, CursorLook.Right);
            bool ok = Hot(l, 131, 358, 150, 376, 4004);
            bool trash = ls.Count > 0 && Hot(l, 162, 359, 182, 376, 4001);
            if (!input.LeftDown) return;
            if (prev) { _page--; Paper(); }
            else if (next) { _page++; Paper(); }
            else if (ok) Close();
            else if (trash && !ls.IsReadOnly)
            {
                App.Audio.Play("SOUND/pap2.raw");
                ls.RemoveAt(_page);
                if (ls.Count == 0) Close();
                else _page = Mathf.Min(_page, ls.Count - 1);
            }
        }

        public override void OnRightClick() { App.Audio.Play("SOUND/pap3.raw"); Close(); }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            var ls = Letters;
            var letter = ls.Count > 0 ? ls[Mathf.Clamp(_page, 0, ls.Count - 1)] : null;
            var f = Kit.SmallBlack;
            if (letter != null && letter.IsFax)
            {
                s.Draw(Assets.Texture(Lib, "FAX"), x, y);
                f.DrawBlock(s, new RectInt(x + 30, y + 70, 220, 220), letter.Subject + "\n" + letter.Body, Color.white);
                if (letter.Picture != null) s.Draw(Assets.Texture(Lib, letter.Picture), x + 30, y + 180);
                if (_page > 0) s.Draw(Assets.Texture(Lib, "RETURN_F"), x + 16, y + 331);
                if (_page < ls.Count - 1) s.Draw(Assets.Texture(Lib, "NEXT_F"), x + 236, y + 328);
            }
            else
            {
                s.Draw(Background, x, y);
                if (letter != null)
                {
                    var p = App.Session?.Local;
                    f.Draw(s, x + 19, y + 55, Std("Misc", 3120));
                    if (p != null) { f.Draw(s, x + 57, y + 55, p.Name); f.Draw(s, x + 57, y + 67, p.Airline); }
                    f.DrawBlock(s, new RectInt(x + 19, y + 110, 265, 41), letter.Subject ?? "", Color.white);
                    f.DrawBlock(s, new RectInt(x + 19, y + 151, 265, 140), letter.Body ?? "", Color.white);
                    f.Draw(s, x + 19, y + 292, Std("Misc", 3121));
                    f.Draw(s, x + 19, y + 322, letter.Sender ?? "");
                }
                if (_page > 0) s.Draw(Assets.Texture(Lib, "RETURN"), x, y + 332);
                if (_page < ls.Count - 1) s.Draw(Assets.Texture(Lib, "NEXT"), x + 245, y + 335);
            }
            if (l.x >= 131 && l.x <= 150 && l.y >= 358 && l.y <= 376) s.Draw(Assets.Texture(Lib, "OK"), x + 131, y + 358);
            if (l.x >= 162 && l.x <= 182 && l.y >= 359 && l.y <= 376) s.Draw(Assets.Texture(Lib, "LOECHEN"), x + 162, y + 358);
        }
    }

    /// <summary>
    /// Text-entry menus (rooms-and-ui-2 §4.29): rename plane / designed plane, host address, broadcast.
    /// Prompt from std "Misc" (3099 rename, 3097 IP, 3098 broadcast); OK/Cancel; max 20 characters.
    /// </summary>
    public sealed class TextEntryMenu : PopupMenu
    {
        readonly int _type, _prompt, _maxLen;
        readonly object[] _args;
        readonly bool _plainOk;
        readonly Action<string> _ok;
        public string Text;

        public TextEntryMenu(int type, int promptId, string initial, Action<string> ok, object[] args = null, int maxLen = 20, bool plainOk = false)
        {
            _type = type; _prompt = promptId; Text = initial ?? ""; _ok = ok; _args = args ?? Array.Empty<object>(); _maxLen = maxLen; _plainOk = plainOk;
        }

        public override int Type => _type;
        public override Vector2Int Size => new Vector2Int(290, 200);
        public override Texture Background => Assets.Texture(_type == MenuIds.Broadcast ? "gli/broad.gli" : "gli/rename.gli", "MENU");

        static bool Allowed(char c) =>
            char.IsLetterOrDigit(c) || " :,;!?'-+().".IndexOf(c) >= 0 || "ÄÖÜäöüß".IndexOf(c) >= 0;

        void Commit()
        {
            if (Text.Length == 0 && !_plainOk) return;
            Close();
            _ok?.Invoke(Text);
        }

        public override bool OnKey(InputState input)
        {
            bool any = false;
            foreach (char c in input.Typed)
                if (Allowed(c) && Text.Length < _maxLen) { Text += c; any = true; }
            if (input.Backspace && Text.Length > 0) { Text = Text.Substring(0, Text.Length - 1); any = true; }
            if (input.Enter) { Commit(); return true; }
            if (input.Escape) { Close(); return true; }
            return any;
        }

        public override void Update(InputState input, Vector2Int l)
        {
            if (_plainOk)
            {
                if (Hot(l, 100, 160, 190, 200) && input.LeftDown) Commit();
                return;
            }
            bool ok = Text.Length > 0 && Hot(l, 0, 160, 100, 200);
            bool cancel = Hot(l, 190, 160, 290, 200);
            if (!input.LeftDown) return;
            if (ok) Commit();
            else if (cancel) Close();
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.BigGrey;
            f.DrawBlock(s, new RectInt(x + 10, y + 10, 270, 120), DialogText.Format(Std("Misc", _prompt), _args), Color.white);
            bool blink = (int)(Time.unscaledTime * 2) % 2 == 0;
            Kit.SmallBlack.Draw(s, x + 26, y + 141, Text + (blink ? "_" : ""));
            if (_plainOk) f.Draw(s, x + 145, y + 165, Std("Misc", 3002), TextAlign.Center);
            else
            {
                f.Draw(s, x + 16, y + 165, Std("Misc", 3002), Text.Length > 0 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 110));
                f.Draw(s, x + 274, y + 165, Std("Misc", 3003), TextAlign.Right);
            }
        }
    }

    /// <summary>
    /// Paged "notepad" list (rooms-and-ui-2 §4.1) on list.gli BLOCK: 13 rows per page on the right half,
    /// value column right-aligned at x=386, the left half is a tip page the owner draws into.
    /// Used by SELLPLANE, BUYPLANE, PLANECOSTS, PLANEJOB, SABOTAGEPLANE/ROUTE, ADROUTE, BUYXPLANE.
    /// </summary>
    public class NotepadListMenu : PopupMenu
    {
        public sealed class Row { public string Name, Value; public bool Red; public object Tag; }

        readonly int _type;
        readonly string _lib;
        public string Title0, Title1;
        public readonly List<Row> Rows = new List<Row>();
        public Action<int, Row> Picked;
        /// <summary>Owner draws details of the hovered row into the left page (x, y = menu top-left).</summary>
        public Action<Screen2D, int, int, int> DrawTip;
        int _page, _hover = -1;

        public NotepadListMenu(int type, string lib = "gli/list.gli") { _type = type; _lib = lib; }

        public override int Type => _type;
        public override Vector2Int Size => new Vector2Int(388, 213);
        public override Texture Background => Assets.Texture(_lib, "BLOCK");

        public override void Update(InputState input, Vector2Int l)
        {
            _hover = -1;
            for (int k = 0; k < 13 && _page + k < Rows.Count; k++) PadTarget(216, 25 + 13 * k, 387, 37 + 13 * k);   // gamepad (OpenAT)
            bool prev = _page > 0 && Hot(l, 196, 192, 212, 208, 0, CursorLook.Left);
            bool next = _page + 13 < Rows.Count && Hot(l, 374, 199, 390, 215, 0, CursorLook.Right);
            if (!prev && !next && l.x >= 216 && l.x <= 387 && l.y >= 25 && l.y <= 212)
            {
                int r = (l.y - 25) / 13;
                if (r < 13 && _page + r < Rows.Count) { _hover = _page + r; App.Ui.Cursor = CursorLook.Hot; }
            }
            if (!input.LeftDown) return;
            if (prev) { _page -= 13; Paper(); }
            else if (next) { _page += 13; Paper(); }
            else if (_hover >= 0)
            {
                int i = _hover;
                Close();
                Picked?.Invoke(i, Rows[i]);
            }
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            if (Title0 != null) f.Draw(s, x + 216, y + 12, Title0);
            if (Title1 != null) f.Draw(s, x + 386, y + 12, Title1, TextAlign.Right);
            for (int k = 0; k < 13 && _page + k < Rows.Count; k++)
            {
                var row = Rows[_page + k];
                int ry = y + 25 + 13 * k;
                if (_page + k == _hover) s.FillRect(new RectInt(x + 216, y + 23 + 13 * k, 171, 14), row.Red ? new Color32(255, 80, 80, 70) : new Color32(32, 255, 100, 70));
                var font = row.Red ? Kit.SmallRed : f;
                int vw = row.Value != null ? font.Measure(row.Value) : 0;
                int room = 386 - 216 - vw - 4;
                string name = row.Name ?? "";
                if (font.Measure(name) > room)
                {
                    while (name.Length > 1 && font.Measure(name + "...") > room) name = name.Substring(0, name.Length - 1);
                    name += "...";
                }
                font.Draw(s, x + 216, ry, name);
                // Only the name turns red; the value column is always black (original notepad lists).
                if (row.Value != null) f.Draw(s, x + 386, ry, row.Value, TextAlign.Right);
            }
            if (_page > 0) s.Draw(Assets.Texture(_lib, "LEFT"), x + 196, y + 191);
            if (_page + 13 < Rows.Count) s.Draw(Assets.Texture(_lib, "RIGHT"), x + 374, y + 199);
            if (_hover >= 0) DrawTip?.Invoke(s, x, y, _hover);
        }
    }

    /// <summary>
    /// MENU_FILOFAX shell (rooms-and-ui-2 §4.9): room directory (mode 1, adressen.gli, 18 entries "Filo"
    /// 1000+i) and phone book (mode 2, telefons.gli, 14 entries "Filo" 2000+i). Only visited rooms are
    /// listed. Directory click: go to the room (walking is the hall's job; see <see cref="Walk"/>).
    /// Phone click: <see cref="Call"/>. Competitor/branch sub-lists: TODO (part 2 §4.9).
    /// </summary>
    public sealed class FilofaxMenu : PopupMenu
    {
        public readonly int Mode;
        public FilofaxMenu(int mode) => Mode = mode;

        /// <summary>Room ids of the directory entries (own office/personnel resolved per player).</summary>
        public static readonly int[] DirectoryRooms = { 111, 104, 112, 100, 10, 80, 107, 121, 105, 101, 103, 102, 115, 11, 84, 85, 109, 110 };
        /// <summary>Phone book entries: room id and talker (-1 = list slot / empty).</summary>
        public static readonly (int room, int talker)[] PhoneEntries =
        {
            (104, 11), (100, 9), (100, 8), (107, 12), (105, 13), (102, 10), (115, 18), (11, 0), (11, 1), (109, 15), (110, 14),
            (-1, -1), (254, -1), (117, -1),
        };

        /// <summary>Walk to a room (hook for the airport hall); default enters it directly.</summary>
        public static Action<OpenAT.View.Boot.GameApp, int> Walk;
        /// <summary>Place a phone call; default asks the room host.</summary>
        public static Action<OpenAT.View.Boot.GameApp, int, int> Call;
        /// <summary>Opens the competitor (254) or branch (117) sub-list (set by the phone-book lists).</summary>
        public static Action<OpenAT.View.Boot.GameApp, MenuHost, int> OpenList;

        string Lib => Mode == 1 ? "gli/adressen.gli" : "gli/telefons.gli";
        public override int Type => MenuIds.Filofax;
        public override Vector2Int Size => Mode == 1 ? new Vector2Int(204, 239) : new Vector2Int(204, 210);
        public override Vector2Int? FixedPos => Mode == 1 ? new Vector2Int(218, 100) : new Vector2Int(218, 115);
        public override Texture Background => Assets.Texture(Lib, "BLOCK");
        int Count => Mode == 1 ? DirectoryRooms.Length : PhoneEntries.Length;

        int RoomOf(int i)
        {
            int r = Mode == 1 ? DirectoryRooms[i] : PhoneEntries[i].room;
            int p = App.Session?.World.LocalPlayer ?? 0;
            if (r == 10 || r == 11) r += 10 * p;
            return r;
        }

        bool Listed(int i)
        {
            int r = RoomOf(i);
            if (r < 0) return false;
            if (r == 254 || r == 117) return true;
            return App.Rooms.WasVisited(r);
        }

        public override void Update(InputState input, Vector2Int l)
        {
            for (int k = 0; k < Count; k++) if (Listed(k)) PadTarget(32, 4 + 13 * k, 204, 16 + 13 * k);   // gamepad (OpenAT)
            if (l.x < 32 || l.x > 204 || l.y < 4 || l.y > Size.y - 1) return;
            int i = (l.y - 4) / 13;
            if (i >= Count || !Listed(i)) return;
            App.Ui.Cursor = CursorLook.Hot;
            if (!input.LeftDown) return;
            int room = RoomOf(i);
            if (Mode == 1)
            {
                Close();
                if (Walk != null) Walk(App, room); else App.Rooms.WalkTo(room);
            }
            else if (room != 254 && room != 117)
            {
                Close();
                if (Call != null) Call(App, room, PhoneEntries[i].talker); else App.Rooms.PhoneCall(PhoneEntries[i].talker);
            }
            else if (OpenList != null)
            {
                // Competitor list (MENU_PLAYERLIST) and branch list (MENU_BRANCHLIST), rooms-and-ui-2 §4.9.
                var host = Host;
                Close();
                OpenList(App, host, room);
            }
        }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            s.PushClip(new RectInt(x, y, 204, Size.y));
            for (int i = 0; i < Count; i++)
            {
                if (!Listed(i)) continue;
                if (l.x >= 32 && l.y >= 4 + 13 * i && l.y < 17 + 13 * i) HoverBand(s, new RectInt(x + 32, y + 4 + 13 * i, 172, 15));
                f.Draw(s, x + 34, y + 7 + 13 * i, Std("Filo", (Mode == 1 ? 1000 : 2000) + i));
            }
            s.PopClip();
        }
    }

    /// <summary>MENU_QUITMESSAGE (rooms-and-ui-2 §4.30): demo exit screen; any click quits.</summary>
    public sealed class QuitMessageMenu : PopupMenu
    {
        public override int Type => MenuIds.QuitMessage;
        public override Vector2Int Size => new Vector2Int(640, 480);
        public override Vector2Int? FixedPos => Vector2Int.zero;
        public override Texture Background => Assets.Texture("gli/exitmenu.gli", "EXITMENU");
        public override void Update(InputState input, Vector2Int l) { if (input.LeftDown) Quit(); }
        public override void OnRightClick() => Quit();
        static void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        public override void Draw(Screen2D s, int x, int y, Vector2Int l) => s.Draw(Background, x, y);
    }

    /// <summary>
    /// Placeholder for menus whose domain is not written yet. Shows the menu background (if any) and a
    /// note; any click closes. Each use names the spec section to implement.
    /// </summary>
    public sealed class StubMenu : PopupMenu
    {
        readonly int _type;
        readonly string _lib, _bmp, _spec;
        readonly Vector2Int _size;
        public StubMenu(int type, string lib, string bmp, Vector2Int size, string specSection)
        { _type = type; _lib = lib; _bmp = bmp; _size = size; _spec = specSection; }
        public override int Type => _type;
        public override Vector2Int Size => _size;
        public override Texture Background => _lib != null ? Assets.Texture(_lib, _bmp) : null;
        public override void Update(InputState input, Vector2Int l) { if (input.LeftDown) Close(); }
        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            if (Background != null) s.Draw(Background, x, y);
            else s.FillRect(new RectInt(x, y, _size.x, _size.y), new Color32(230, 230, 220, 230));
            Kit.SmallBlack.DrawBlock(s, new RectInt(x + 20, y + 20, _size.x - 40, _size.y - 40), "(not implemented yet: " + _spec + ")", Color.white);
        }
    }
}
