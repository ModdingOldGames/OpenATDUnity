using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>
    /// Split-flap ("klacker") board used by the main menu, options and new-game screens:
    /// 24 x 16 character cells. Each cell flips towards its target glyph through the glyph
    /// sequence; flipping speeds up for long distances. Behaviour: docs/spec/findings/rooms-and-ui.md.
    /// </summary>
    public sealed class KlackerBoard
    {
        public const int Columns = 24, Rows = 16;
        public const int CellWidth = 16, CellHeight = 22;
        public const float TickSeconds = 0.05f;
        const string Lib = "room/klacker.gli";

        /// <summary>Glyph order (original single-byte codes). The last two are the human/computer
        /// player symbols, which are never passed while flipping.</summary>
        public static readonly string Glyphs =
            " ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÜ0123456789.!:;,?()-#=*|><+" +
            "ÁÈÏÉÌÍÒÓØ\u008A\u008DÚÙÝ\u008E" +
            "¥ÆÊ£Ñ\u008C\u008F¯" + "¼ÀÅ" + "¦ÃÇ" + "²³";

        static readonly string[] BitmapNames = (
            "KL_SP KL_A KL_B KL_C KL_D KL_E KL_F KL_G KL_H KL_I KL_J KL_K KL_L KL_M KL_N KL_O KL_P KL_Q KL_R KL_S KL_T KL_U KL_V KL_W KL_X KL_Y KL_Z KL_AE KL_OE KL_UE " +
            "KL_0 KL_1 KL_2 KL_3 KL_4 KL_5 KL_6 KL_7 KL_8 KL_9 KL_PKT KL_AUSR KL_DPKT KL_SEMI KL_KOMMA KL_FRAGE KL_KLA KL_KLZ KL_MINUS KL_BULL KL_GLCH KL_MAL KL_KREUZ KL_MORE KL_LESS KL_PL " +
            "KL_193 KL_200 KL_207 KL_201 KL_204 KL_205 KL_210 KL_211 KL_216 KL_138 KL_141 KL_218 KL_217 KL_221 KL_142 KL_165 KL_198 KL_202 KL_163 KL_209 KL_140 KL_143 KL_175 " +
            "KL_188 KL_192 KL_197 KL_165B KL_195 KL_128 KL_HUMAN KL_COMPI").Split(' ');

        /// <summary>Index offset of the dimmed ("disabled") glyph set.</summary>
        public const int DimOffset = 87;

        public readonly byte[] Target = new byte[Columns * Rows];
        public readonly byte[] Shown = new byte[Columns * Rows];
        public readonly bool[] LineDisabled = new bool[Rows];

        Texture2D[] _glyphs, _dimGlyphs;
        float _acc;

        /// <summary>Sound hook: number of flipping cells this tick.</summary>
        public System.Action<int> OnKlack;

        public void Load(GameAssets assets)
        {
            _glyphs = new Texture2D[BitmapNames.Length];
            _dimGlyphs = new Texture2D[BitmapNames.Length];
            for (int i = 0; i < BitmapNames.Length; i++)
            {
                // Installs without the accent glyphs (English, German) still roll through their indices;
                // a missing glyph shows nothing, as in the original.
                _glyphs[i] = assets.Has(Lib, BitmapNames[i]) ? assets.Texture(Lib, BitmapNames[i]) : null;
                string dim = BitmapNames[i].Replace("KL_", "KD_");
                _dimGlyphs[i] = assets.Has(Lib, dim) ? assets.Texture(Lib, dim) : _glyphs[i];
            }
        }

        public void Clear()
        {
            System.Array.Clear(Target, 0, Target.Length);
            System.Array.Clear(LineDisabled, 0, LineDisabled.Length);
        }

        public static byte GlyphIndex(char ch)
        {
            ch = ToUpper(ch);
            int i = Glyphs.IndexOf(ch);
            return i < 0 ? (byte)0 : (byte)i;
        }

        static char ToUpper(char c)
        {
            if (c >= 'a' && c <= 'z') return (char)(c - 32);
            switch (c)
            {
                case 'ä': return 'Ä';
                case 'ö': return 'Ö';
                case 'ü': return 'Ü';
            }
            // Accented letters without a glyph fall back to their base letter.
            if ("èéêëÈÉÊË".IndexOf(c) >= 0) return 'E';
            if ("àáâãÀÁÂÃ".IndexOf(c) >= 0) return 'A';
            if ("ìíîïÌÍÎÏ".IndexOf(c) >= 0) return 'I';
            if ("ùúûÙÚÛ".IndexOf(c) >= 0) return 'U';
            if ("òóôõÒÓÔÕ".IndexOf(c) >= 0) return 'O';
            if (c == 'ç' || c == 'Ç') return 'C';
            if (c == 'ß') return 'S';
            return c;
        }

        public void PrintAt(int x, int y, string text)
        {
            if (text == null || y < 0 || y >= Rows) return;
            for (int c = 0; c + x < Columns && c < text.Length; c++)
                if (c + x >= 0) Target[c + x + y * Columns] = GlyphIndex(text[c]);
        }

        /// <summary>Right-aligned text ending at column 23.</summary>
        public void PrintRight(int y, string text) => PrintAt(Columns - text.Length, y, text);

        public bool Settled
        {
            get
            {
                for (int i = 0; i < Target.Length; i++) if (Target[i] != Shown[i]) return false;
                return true;
            }
        }

        public void SettleNow() => System.Array.Copy(Target, Shown, Target.Length);

        public void Update(float dt)
        {
            _acc += dt;
            while (_acc >= TickSeconds)
            {
                _acc -= TickSeconds;
                Step();
            }
        }

        /// <summary>One flip step: every differing cell advances 1..9 glyphs depending on distance.</summary>
        public void Step()
        {
            int l = Glyphs.Length;
            int flipping = 0;
            for (int c = 0; c < Target.Length; c++)
            {
                if (Target[c] == Shown[c]) continue;
                flipping++;
                int dist = (Target[c] + l - Shown[c]) % l;
                int steps = dist > 20 ? 9 : dist > 10 ? 7 : dist > 6 ? 4 : dist > 3 ? 2 : 1;
                int h = Shown[c];
                for (int d = 0; d < steps; d++)
                {
                    h++;
                    if (h >= l) h = 0;
                    // Skip from the end of letters/umlauts/digits back to blank when the target is below.
                    if (h == 27 && Target[c] < 27) h = 0;
                    else if (h == 30 && Target[c] < 30) h = 0;
                    else if (h == 40 && Target[c] < 40) h = 0;
                    if (h >= l - 2) h = 0;
                }
                if (dist < (Target[c] + l - h) % l) h = Target[c];
                Shown[c] = (byte)h;
            }
            if (flipping > 0) OnKlack?.Invoke(flipping);
        }

        public static int CellX(int originX, int col) => originX + col * CellWidth;
        public static int CellY(int originY, int row) => originY + row * CellHeight;

        public void Draw(Screen2D s, int originX, int originY)
        {
            if (_glyphs == null) return;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                {
                    int g = Shown[x + y * Columns];
                    if (g <= 0 || g >= _glyphs.Length) continue;
                    var tex = LineDisabled[y] ? _dimGlyphs[g] : _glyphs[g];
                    if (tex != null) s.Draw(tex, originX + x * CellWidth, originY + y * CellHeight);
                }
        }
    }
}
