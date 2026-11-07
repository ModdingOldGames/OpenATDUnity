using System;
using System.Collections.Generic;
using System.Reflection;
using OpenAT.View.UI;

namespace OpenAT.View.Screens
{
    /// <summary>Gives a screen a name for the command line (<c>-openat-screen NAME</c>) and debug menus.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ScreenNameAttribute : Attribute
    {
        public readonly string Name;
        public ScreenNameAttribute(string name) => Name = name;
    }

    /// <summary>
    /// Named screen constructors. Every <see cref="GameScreen"/> with a <see cref="ScreenNameAttribute"/>
    /// and a parameterless constructor is found automatically; extensions may also call
    /// <see cref="Register"/>.
    /// </summary>
    public static class ScreenFactory
    {
        static Dictionary<string, Func<GameScreen>> _factories;

        static Dictionary<string, Func<GameScreen>> Factories
        {
            get
            {
                if (_factories != null) return _factories;
                _factories = new Dictionary<string, Func<GameScreen>>(StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || !typeof(GameScreen).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        foreach (ScreenNameAttribute a in t.GetCustomAttributes(typeof(ScreenNameAttribute), false))
                        {
                            var type = t;
                            _factories[a.Name] = () => (GameScreen)Activator.CreateInstance(type);
                        }
                    }
                }
                return _factories;
            }
        }

        public static void Register(string name, Func<GameScreen> create) => Factories[name] = create;

        public static IEnumerable<string> Names => Factories.Keys;

        public static GameScreen Create(string name) =>
            name != null && Factories.TryGetValue(name, out var f) ? f() : null;
    }
}
