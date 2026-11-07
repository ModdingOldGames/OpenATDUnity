using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Core
{
    /// <summary>
    /// A unit of simulation behaviour (flights, finance, AI, ...). Hooks run in ascending
    /// <see cref="Order"/>; ties keep registration order. The default orders follow the step numbers of
    /// the original sequences (sim-core.md §2.3–2.8) × 100, so extensions can slot in between, and any
    /// system can be replaced by name.
    /// </summary>
    public interface ISimSystem
    {
        /// <summary>Stable name used to replace or remove the system.</summary>
        string Name { get; }
        int Order { get; }

        /// <summary>Every simulation tick, before the clock advances.</summary>
        void OnTick(World w);
        /// <summary>When the minute value changed during the tick.</summary>
        void OnMinute(World w);
        /// <summary>When the hour changed (minute wrapped to 0).</summary>
        void OnHour(World w);
        /// <summary>World-level midnight sequence (sim-core.md §2.6).</summary>
        void OnNewDay(World w);
        /// <summary>Per airline new-day sequence (sim-core.md §2.7), called for each active airline.</summary>
        void OnPlayerNewDay(World w, Player p);
        /// <summary>09:00 morning sequence (sim-core.md §2.8).</summary>
        void OnMorning(World w);
        /// <summary>Evening screen opens (end of the working day).</summary>
        void OnEvening(World w);
    }

    /// <summary>Base class with empty hooks.</summary>
    public abstract class SimSystem : ISimSystem
    {
        public abstract string Name { get; }
        public abstract int Order { get; }
        public virtual void OnTick(World w) { }
        public virtual void OnMinute(World w) { }
        public virtual void OnHour(World w) { }
        public virtual void OnNewDay(World w) { }
        public virtual void OnPlayerNewDay(World w, Player p) { }
        public virtual void OnMorning(World w) { }
        public virtual void OnEvening(World w) { }
    }

    public sealed class SimPipeline
    {
        readonly List<ISimSystem> _systems = new List<ISimSystem>();
        ISimSystem[] _sorted;

        public IReadOnlyList<ISimSystem> Systems => Sorted();

        public void Add(ISimSystem system)
        {
            if (Find(system.Name) != null) throw new InvalidOperationException("System already registered: " + system.Name);
            _systems.Add(system);
            _sorted = null;
        }

        /// <summary>Replaces the system with the same name.</summary>
        public void Replace(ISimSystem system)
        {
            int i = _systems.FindIndex(s => s.Name == system.Name);
            if (i < 0) throw new InvalidOperationException("No system named " + system.Name);
            _systems[i] = system;
            _sorted = null;
        }

        public bool Remove(string name)
        {
            int i = _systems.FindIndex(s => s.Name == name);
            if (i < 0) return false;
            _systems.RemoveAt(i);
            _sorted = null;
            return true;
        }

        public ISimSystem Find(string name) => _systems.Find(s => s.Name == name);
        public T Get<T>() where T : class, ISimSystem { foreach (var s in _systems) if (s is T t) return t; return null; }

        ISimSystem[] Sorted()
        {
            if (_sorted != null) return _sorted;
            var list = new List<(ISimSystem s, int i)>();
            for (int i = 0; i < _systems.Count; i++) list.Add((_systems[i], i));
            list.Sort((a, b) => a.s.Order != b.s.Order ? a.s.Order.CompareTo(b.s.Order) : a.i.CompareTo(b.i));
            _sorted = list.ConvertAll(x => x.s).ToArray();
            return _sorted;
        }

        public void RunTick(World w) { foreach (var s in Sorted()) s.OnTick(w); }
        public void RunMinute(World w) { foreach (var s in Sorted()) s.OnMinute(w); }
        public void RunHour(World w) { foreach (var s in Sorted()) s.OnHour(w); }
        public void RunNewDay(World w) { foreach (var s in Sorted()) s.OnNewDay(w); }
        public void RunPlayerNewDay(World w, Player p) { foreach (var s in Sorted()) s.OnPlayerNewDay(w, p); }
        public void RunMorning(World w) { foreach (var s in Sorted()) s.OnMorning(w); }
        public void RunEvening(World w) { foreach (var s in Sorted()) s.OnEvening(w); }
    }
}
