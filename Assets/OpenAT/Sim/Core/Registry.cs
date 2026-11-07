using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Core
{
    /// <summary>Content with a stable id (plane type, city, item, ...).</summary>
    public interface IContent
    {
        int Id { get; }
    }

    /// <summary>
    /// Ordered, id-keyed content table. Original data is registered first; extensions may add new
    /// entries or override existing ones by id. Iteration order is registration order (deterministic).
    /// </summary>
    public sealed class Registry<T> where T : class, IContent
    {
        readonly List<T> _items = new List<T>();
        readonly Dictionary<int, int> _index = new Dictionary<int, int>();

        public int Count => _items.Count;
        public IReadOnlyList<T> All => _items;

        public T this[int id] => _index.TryGetValue(id, out var i) ? _items[i] : throw new KeyNotFoundException(typeof(T).Name + " " + id);

        public bool TryGet(int id, out T item)
        {
            if (_index.TryGetValue(id, out var i)) { item = _items[i]; return true; }
            item = null;
            return false;
        }

        public T Find(int id) => TryGet(id, out var t) ? t : null;

        public bool Contains(int id) => _index.ContainsKey(id);

        /// <summary>Adds an entry, or replaces the entry with the same id (keeping its position).</summary>
        public void Register(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_index.TryGetValue(item.Id, out var i)) _items[i] = item;
            else
            {
                _index[item.Id] = _items.Count;
                _items.Add(item);
            }
        }

        public int IndexOf(int id) => _index.TryGetValue(id, out var i) ? i : -1;
    }
}
