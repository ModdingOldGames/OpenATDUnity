using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Core
{
    /// <summary>Marker for replaceable game rules (formulas). See docs/ARCHITECTURE.md "Extensibility".</summary>
    public interface IRule { }

    /// <summary>
    /// Holds one implementation per rule interface. Defaults reproduce the original game; extensions
    /// call <see cref="Replace{T}"/> or <see cref="Decorate{T}"/> to change behaviour without touching
    /// the default code.
    /// </summary>
    public sealed class RuleSet
    {
        readonly Dictionary<Type, IRule> _rules = new Dictionary<Type, IRule>();

        public T Get<T>() where T : class, IRule
        {
            if (_rules.TryGetValue(typeof(T), out var r)) return (T)r;
            throw new InvalidOperationException("No rule registered for " + typeof(T).Name);
        }

        public bool Has<T>() where T : class, IRule => _rules.ContainsKey(typeof(T));

        public void Set<T>(T rule) where T : class, IRule => _rules[typeof(T)] = rule ?? throw new ArgumentNullException(nameof(rule));

        /// <summary>Replaces a rule, returning the previous implementation.</summary>
        public T Replace<T>(T rule) where T : class, IRule
        {
            _rules.TryGetValue(typeof(T), out var old);
            Set(rule);
            return old as T;
        }

        /// <summary>Wraps the current rule: <c>rules.Decorate&lt;IFoo&gt;(inner =&gt; new LoggingFoo(inner))</c>.</summary>
        public void Decorate<T>(Func<T, T> wrap) where T : class, IRule => Set(wrap(Get<T>()));
    }
}
