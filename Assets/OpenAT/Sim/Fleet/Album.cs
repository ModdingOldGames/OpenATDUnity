using System;
using System.Collections.Generic;

namespace OpenAT.Sim.Fleet
{
    /// <summary>An entry of an <see cref="Album{T}"/>; the album assigns the id.</summary>
    public interface IAlbumItem
    {
        int Id { get; set; }
    }

    /// <summary>
    /// The original's slot container (data-tables.md §0.6): fixed slots, generated ids from 0x1000000
    /// upwards, insertion into the first free slot from index 0. Slot order matters for the original's
    /// iteration order (e.g. maintenance parity uses the slot index), so it is reproduced.
    /// </summary>
    public sealed class Album<T> where T : class, IAlbumItem
    {
        readonly List<T> _slots = new List<T>();
        int _lastId = 0xFFFFFF;
        readonly int _grow;
        readonly int _minFree;

        /// <param name="capacity">Initial slots.</param>
        /// <param name="grow">Slots added when the album runs full.</param>
        /// <param name="minFree">Grow before adding when fewer free slots than this remain (contracts: 3).</param>
        public Album(int capacity = 0, int grow = 10, int minFree = 1)
        {
            for (int i = 0; i < capacity; i++) _slots.Add(null);
            _grow = Math.Max(1, grow);
            _minFree = Math.Max(1, minFree);
        }

        public int Capacity => _slots.Count;
        public int LastId { get => _lastId; set => _lastId = value; }

        public int Count
        {
            get { int n = 0; foreach (var s in _slots) if (s != null) n++; return n; }
        }

        public int NumFree => Capacity - Count;

        /// <summary>Item in slot <paramref name="slot"/> or null.</summary>
        public T this[int slot] => slot >= 0 && slot < _slots.Count ? _slots[slot] : null;

        /// <summary>Used items in slot order.</summary>
        public IEnumerable<T> Items
        {
            get { for (int i = 0; i < _slots.Count; i++) if (_slots[i] != null) yield return _slots[i]; }
        }

        /// <summary>Snapshot of the used items in slot order (safe to modify the album while iterating).</summary>
        public List<T> ToList()
        {
            var l = new List<T>();
            foreach (var s in _slots) if (s != null) l.Add(s);
            return l;
        }

        /// <summary>Adds into the first free slot (growing first if needed) and assigns a new id.</summary>
        public int Add(T item)
        {
            if (NumFree < _minFree) for (int i = 0; i < _grow; i++) _slots.Add(null);
            item.Id = ++_lastId;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i] == null) { _slots[i] = item; return item.Id; }
            _slots.Add(item);
            return item.Id;
        }

        /// <summary>Adds keeping the item's existing id (save games, transfers).</summary>
        public void AddWithId(T item)
        {
            if (NumFree < 1) for (int i = 0; i < _grow; i++) _slots.Add(null);
            if (item.Id > _lastId) _lastId = item.Id;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i] == null) { _slots[i] = item; return; }
            _slots.Add(item);
        }

        public int SlotOf(int id)
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i] != null && _slots[i].Id == id) return i;
            return -1;
        }

        public T Find(int id)
        {
            foreach (var s in _slots) if (s != null && s.Id == id) return s;
            return null;
        }

        public bool Contains(int id) => SlotOf(id) >= 0;

        public bool Remove(int id)
        {
            int i = SlotOf(id);
            if (i < 0) return false;
            _slots[i] = null;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Count; i++) _slots[i] = null;
        }

        /// <summary>Stable sort of the used items to the front of the album (empty slots last).</summary>
        public void Sort(Comparison<T> cmp)
        {
            var used = new List<(T item, int i)>();
            for (int i = 0; i < _slots.Count; i++) if (_slots[i] != null) used.Add((_slots[i], i));
            used.Sort((a, b) => { int c = cmp(a.item, b.item); return c != 0 ? c : a.i.CompareTo(b.i); });
            for (int i = 0; i < _slots.Count; i++) _slots[i] = i < used.Count ? used[i].item : null;
        }
    }
}
