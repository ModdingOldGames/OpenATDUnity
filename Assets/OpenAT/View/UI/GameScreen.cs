using System;
using System.Collections.Generic;
using OpenAT.View.Boot;
using OpenAT.View.Render;
using UnityEngine;

namespace OpenAT.View.UI
{
    /// <summary>
    /// One full screen or overlay (title, menu, airport hall, a room, a popup). Screens are stacked;
    /// the top screen gets input, every screen from the topmost opaque one upwards is drawn.
    /// </summary>
    public abstract class GameScreen
    {
        public GameApp App { get; internal set; }
        protected GameAssets Assets => App.Assets;
        protected Screen2D Screen => App.Screen;

        /// <summary>Opaque screens hide everything below them.</summary>
        public virtual bool Opaque => true;

        /// <summary>X offset for 640-wide original layouts (centred on wide screens).</summary>
        protected int OX => App.Screen.CenterX;

        public virtual void OnEnter() { }
        public virtual void OnExit() { }
        public virtual void Update(InputState input, float dt) { }
        public abstract void Draw(Screen2D s);

        /// <summary>A 640-wide original rect moved into the centred layout.</summary>
        protected RectInt R(int x, int y, int w, int h) => new RectInt(OX + x, y, w, h);
    }

    /// <summary>Stack of screens. Changes requested during update are applied after it.</summary>
    public sealed class ScreenStack
    {
        readonly List<GameScreen> _stack = new List<GameScreen>();
        readonly List<Action> _pending = new List<Action>();
        readonly GameApp _app;

        public ScreenStack(GameApp app) => _app = app;

        public GameScreen Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public IReadOnlyList<GameScreen> All => _stack;

        public void Push(GameScreen s) => _pending.Add(() =>
        {
            s.App = _app;
            _stack.Add(s);
            s.OnEnter();
        });

        public void Pop() => _pending.Add(() =>
        {
            if (_stack.Count == 0) return;
            var s = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            s.OnExit();
        });

        /// <summary>Replaces the whole stack with one screen.</summary>
        public void Set(GameScreen s) => _pending.Add(() =>
        {
            for (int i = _stack.Count - 1; i >= 0; i--) _stack[i].OnExit();
            _stack.Clear();
            s.App = _app;
            _stack.Add(s);
            s.OnEnter();
        });

        public void Remove(GameScreen s) => _pending.Add(() =>
        {
            if (_stack.Remove(s)) s.OnExit();
        });

        public void ApplyPending()
        {
            // Applying may enqueue more changes (OnEnter pushing a popup); loop until stable.
            for (int guard = 0; guard < 16 && _pending.Count > 0; guard++)
            {
                var list = _pending.ToArray();
                _pending.Clear();
                foreach (var a in list) a();
            }
        }

        public void Update(InputState input, float dt)
        {
            var top = Top;
            top?.Update(input, dt);
        }

        public void Draw(Screen2D s)
        {
            int first = 0;
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i].Opaque) { first = i; break; }
            for (int i = first; i < _stack.Count; i++) _stack[i].Draw(s);
        }
    }
}
