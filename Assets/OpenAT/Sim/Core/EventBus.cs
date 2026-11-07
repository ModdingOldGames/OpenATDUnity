using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Core
{
    /// <summary>Marker for simulation events. Events are immutable facts about what happened.</summary>
    public interface ISimEvent { }

    /// <summary>
    /// Typed publish/subscribe. Handlers run synchronously in subscription order (deterministic).
    /// The view subscribes to animate/sound things; extensions subscribe to add behaviour.
    /// </summary>
    public sealed class EventBus
    {
        readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
        readonly List<Action<ISimEvent>> _any = new List<Action<ISimEvent>>();

        public IDisposable Subscribe<T>(Action<T> handler) where T : ISimEvent
        {
            if (!_handlers.TryGetValue(typeof(T), out var list)) _handlers[typeof(T)] = list = new List<Delegate>();
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        /// <summary>Receives every event (logging, replays, network sync).</summary>
        public IDisposable SubscribeAll(Action<ISimEvent> handler)
        {
            _any.Add(handler);
            return new Subscription(() => _any.Remove(handler));
        }

        public void Publish<T>(T e) where T : ISimEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                // Copy so handlers may unsubscribe while being called.
                var snapshot = list.ToArray();
                foreach (var d in snapshot) ((Action<T>)d)(e);
            }
            if (_any.Count > 0)
                foreach (var h in _any.ToArray()) h(e);
        }

        sealed class Subscription : IDisposable
        {
            Action _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() { _dispose?.Invoke(); _dispose = null; }
        }
    }
}
