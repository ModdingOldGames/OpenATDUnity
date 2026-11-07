using System.Collections.Generic;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using OpenAT.View.UI;
using UnityEngine;

namespace OpenAT.View.Screens
{
    /// <summary>
    /// The split-flap menus' screensaver (rooms-and-ui-4 §2.8): when nobody touches mouse or keyboard for 600 ticks,
    /// planes in the four airline liveries fly across the screen in front of the board; they get longer and more
    /// frequent the longer the idle time lasts, and dodge the mouse (or the text cursor) vertically. One shared
    /// state for all board screens (main menu, new game, options), as in the original. Ticks run at a fixed
    /// <see cref="TicksPerSecond"/> (the original counted paint frames). WS: planes enter at the right screen edge.
    /// </summary>
    public static class KlackerPlanes
    {
        /// <summary>Animation rate (the original's frame-counted ticks; rooms-and-ui-4 §6 Q7).</summary>
        public const float TicksPerSecond = 20f;
        const string Lib = "gli/glplanes.gli";

        // The universal plane bitmaps used here, by the original's list index.
        const int EngLeft1 = 4, EngRight2 = 5, EngLeft2 = 6, BaseTailLeft = 29, BaseNoseLeft = 30, BaseBody = 31, EngRight1 = 32;
        static readonly string[] Names =
        {
            "999SB", "999SV", "999SR", "999SG",
            "FLUG07", "FLUG08", "FLUG09", "FLUG10", "FLUG11",
            "FLUGB01", "FLUGB02", "FLUGB03", "FLUGB06", "FLUGB07",
            "FLUGV01", "FLUGV02", "FLUGV03", "FLUGV06", "FLUGV07",
            "FLUGR01", "FLUGR02", "FLUGR03", "FLUGR06", "FLUGR07",
            "FLUGG01", "FLUGG02", "FLUGG03", "FLUGG06", "FLUGG07",
            "FLUGB08", "FLUGB99", "FLUGB98", "FLUG12",
        };

        sealed class Plane { public int Size = -1, Dir, Logo, X, Y; }

        static readonly Plane[] Planes = NewPlanes();
        static readonly System.Random Rng = new System.Random();
        /// <summary>Idle ticks since the last mouse move or key.</summary>
        public static int IdleTicks;
        static float _acc;
        static int _frame = -1;
        static Vector2Int _lastMouse = new Vector2Int(int.MinValue, 0);
        static GameAssets _assets;
        static readonly Dictionary<int, Texture2D> Tex = new Dictionary<int, Texture2D>();

        static Plane[] NewPlanes() { var a = new Plane[10]; for (int i = 0; i < a.Length; i++) a[i] = new Plane(); return a; }

        /// <summary>Removes all planes and restarts the idle count (the in-game options do this on entry).</summary>
        public static void Reset()
        {
            foreach (var p in Planes) p.Size = -1;
            IdleTicks = 0;
        }

        /// <summary>Number of planes in flight (tests, logs).</summary>
        public static int Flying { get { int n = 0; foreach (var p in Planes) if (p.Size != -1) n++; return n; } }

        /// <summary>Once per frame (repeat calls in the same frame are ignored): idle reset and the ticks.</summary>
        public static void Update(InputState input, float dt, int screenWidth, int avoidY)
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            if (input.Mouse != _lastMouse || input.AnyKeyOrClick || input.Backspace || input.Tab || input.F1 || input.F2
                || input.F3 || input.F4 || input.LeftPressed || input.RightPressed || input.UpPressed || input.DownPressed)
                IdleTicks = 0;
            _lastMouse = input.Mouse;
            _acc += dt;
            int steps = 0;
            while (_acc >= 1f / TicksPerSecond && steps++ < 8)
            {
                _acc -= 1f / TicksPerSecond;
                Tick(screenWidth, avoidY);
            }
            if (_acc > 1f) _acc = 0;
        }

        /// <summary>One tick: move, dodge, retire off-screen planes, maybe launch one.</summary>
        public static void Tick(int screenWidth, int avoidY)
        {
            IdleTicks++;
            foreach (var p in Planes)
            {
                if (p.Size == -1) continue;
                p.X += p.Dir;
                if (p.Dir > 0 && p.X > screenWidth) p.Size = -1;
                if (p.Dir < 0 && p.X < -190 - p.Size * 35) p.Size = -1;
                // Dodge: a point between 100 px above and 20 px below the plane's base line pushes it away.
                if (avoidY > p.Y - 100 && avoidY < p.Y + 20)
                {
                    if (avoidY > p.Y - 50) p.Y -= 4;
                    if (avoidY < p.Y - 50) p.Y += 4;
                }
            }
            int t = IdleTicks;
            if (!((t > 600 && t % 50 == 0) || (t > 800 && t % 44 == 0) || (t > 1100 && t % 47 == 0))) return;
            foreach (var p in Planes)
            {
                if (p.Size != -1) continue;
                p.Dir = Rng.Next(2) * 20 - 10;
                p.Logo = Rng.Next(4);
                p.Size = Mathf.Min(7, Rng.Next((t - 580) / 30 + 1));
                p.Y = Rng.Next(400) + 80;
                p.X = p.Dir < 0 ? screenWidth : -190 - p.Size * 35;
                break;
            }
        }

        static Texture2D Bm(GameAssets assets, int index)
        {
            if (_assets != assets) { Tex.Clear(); _assets = assets; }
            if (Tex.TryGetValue(index, out var t)) return t;
            var bmp = assets.Bitmap(Lib, Names[index]);
            t = bmp == null ? null : GameAssets.ToTexture(bmp, "klacker plane " + Names[index], keyed: true);
            Tex[index] = t;
            return t;
        }

        static int W(GameAssets a, int i) => Bm(a, i)?.width ?? 0;
        static int H(GameAssets a, int i) => Bm(a, i)?.height ?? 0;

        static void Blit(Screen2D s, GameAssets a, int index, int x, int y)
        {
            var t = Bm(a, index);
            if (t != null) s.Draw(t, x, y);
        }

        /// <summary>
        /// Draws the planes over the board (screen coordinates; y is the plane's base line). Pieces of livery l use
        /// the bitmaps 9 + 5·l + part (part 0 nose, 1 body, 2 tail of a plane flying left; 3 nose, 4 tail flying right)
        /// over the neutral base pieces; the engines are drawn relative to the middle of the body.
        /// </summary>
        public static void Draw(Screen2D s, GameAssets a)
        {
            if (a == null) return;
            foreach (var p in Planes)
            {
                if (p.Size == -1) continue;
                int x = p.X, y = p.Y, liv = 9 + p.Logo * 5;
                int bodyW = W(a, 10) * p.Size / 2;
                if (p.Dir < 0)
                {
                    Blit(s, a, BaseNoseLeft, x, y - H(a, 9));
                    if (p.Logo > 0) Blit(s, a, liv, x, y - H(a, 9));
                    x += W(a, liv);
                    for (int d = 0; d < p.Size; d++)
                    {
                        Blit(s, a, BaseBody, x, y - H(a, 10));
                        if (p.Logo > 0) Blit(s, a, liv + 1, x, y - H(a, 10));
                        x += W(a, liv + 1);
                    }
                    Blit(s, a, EngLeft1, x - bodyW - W(a, EngLeft1) / 2 + 60, y - 118);
                    Blit(s, a, BaseTailLeft, x, y - H(a, 11) + 19);
                    if (p.Logo > 0) Blit(s, a, liv + 2, x, y - H(a, 11) + 19);
                    Blit(s, a, EngLeft2, x - bodyW - W(a, EngLeft1) / 2 + 40, y - 48);
                }
                else
                {
                    int x0 = x, half = W(a, liv + 1) * p.Size / 2;
                    Blit(s, a, EngRight1, x - 47 + half, y - 119);
                    Blit(s, a, 13, x, y - H(a, 13) - 31);
                    Blit(s, a, liv + 4, x, y - H(a, 13) - 31);
                    x += W(a, liv + 4);
                    for (int d = 0; d < p.Size; d++)
                    {
                        Blit(s, a, BaseBody, x, y - H(a, 10));
                        if (p.Logo > 0) Blit(s, a, liv + 1, x, y - H(a, 10));
                        x += W(a, liv + 1);
                    }
                    Blit(s, a, 12, x, y - H(a, 12));
                    Blit(s, a, liv + 3, x, y - H(a, 12));
                    Blit(s, a, EngRight2, x0 + half - 20, y - 46);
                }
            }
        }
    }
}
