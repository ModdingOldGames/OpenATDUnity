using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// A boss's six carried items (items-security-special.md §3.1). Slots hold an <see cref="ItemId"/> or
    /// <see cref="ItemId.None"/> (0xFF). After every change the slots are packed to the front in
    /// ascending item order (the original's "ReformIcons"), so the status bar shows a stable order.
    ///
    /// Read-only to the outside (<see cref="IList{T}.IsReadOnly"/> = true): the status bar reads it
    /// through <c>Player.Items</c>; changes go through the item commands.
    /// </summary>
    public sealed class Inventory : IList<int>, IReadOnlyList<int>
    {
        public const int Slots = 6;
        readonly int[] _slots = { ItemId.None, ItemId.None, ItemId.None, ItemId.None, ItemId.None, ItemId.None };

        public int this[int index]
        {
            get => _slots[index];
            set => throw new NotSupportedException("Inventory is changed through item commands.");
        }

        public int Count => Slots;
        public bool IsReadOnly => true;

        /// <summary>Number of occupied slots.</summary>
        public int Used { get { int n = 0; foreach (var s in _slots) if (s != ItemId.None) n++; return n; } }
        public bool HasSpace => Used < Slots;
        public bool IsEmpty => Used == 0;

        public bool Has(int item) { foreach (var s in _slots) if (s == item) return true; return false; }

        /// <summary>Adds an item when a slot is free; fails silently otherwise (the original's BuyItem).</summary>
        public bool Add(int item)
        {
            if (!ItemId.IsValid(item)) return false;
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == ItemId.None) { _slots[i] = item; Pack(); return true; }
            return false;
        }

        /// <summary>Removes one copy of an item.</summary>
        public bool Remove(int item)
        {
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == item) { _slots[i] = ItemId.None; Pack(); return true; }
            return false;
        }

        /// <summary>Empties a slot.</summary>
        public int RemoveAt(int slot)
        {
            if (slot < 0 || slot >= Slots) return ItemId.None;
            int v = _slots[slot];
            _slots[slot] = ItemId.None;
            Pack();
            return v;
        }

        /// <summary>Replaces one copy of <paramref name="from"/> with <paramref name="to"/>.</summary>
        public bool Replace(int from, int to)
        {
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == from) { _slots[i] = to; Pack(); return true; }
            return false;
        }

        public void Clear() { for (int i = 0; i < Slots; i++) _slots[i] = ItemId.None; }

        /// <summary>Packs the items to the front, sorted by id (ReformIcons).</summary>
        void Pack() => Array.Sort(_slots);

        public int[] ToArray() => (int[])_slots.Clone();

        // ---- IList<int>
        public int IndexOf(int item) => Array.IndexOf(_slots, item);
        public bool Contains(int item) => Has(item);
        public void CopyTo(int[] array, int arrayIndex) => _slots.CopyTo(array, arrayIndex);
        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)_slots).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        void ICollection<int>.Add(int item) => throw new NotSupportedException();
        bool ICollection<int>.Remove(int item) => throw new NotSupportedException();
        void ICollection<int>.Clear() => throw new NotSupportedException();
        void IList<int>.Insert(int index, int item) => throw new NotSupportedException();
        void IList<int>.RemoveAt(int index) => throw new NotSupportedException();

        public override string ToString() => string.Join(",", _slots);
    }
}
