using System;
using System.Collections;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Wörterbuch mit <b>speicherbarer Reihenfolge</b>: verhält sich beim Durchlaufen exakt wie
    /// <see cref="Dictionary{TKey,TValue}"/> (Einträge in Platz-Reihenfolge; ein entfernter Platz wird vom nächsten
    /// Einfügen wiederverwendet, zuletzt frei gewordener zuerst), legt diese Plätze aber offen. So lässt sich nach dem
    /// Laden dieselbe Durchlauf-Reihenfolge wiederherstellen (<see cref="WriteLayout"/>/<see cref="ReadLayout"/>) –
    /// wichtig für den Determinismus, weil viele Systeme Summen und Zufallsentscheidungen in dieser Reihenfolge bilden.
    /// <para>Für Zustand, der durchlaufen wird (Straßen, Gebäude, Bauwerke …). Reine Nachschlage-Tabellen dürfen
    /// <see cref="Dictionary{TKey,TValue}"/> bleiben.</para>
    /// </summary>
    public sealed class StableMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        struct Slot
        {
            public TKey Key;
            public TValue Value;
            public bool Used;
        }

        readonly Dictionary<TKey, int> index = new Dictionary<TKey, int>();
        Slot[] slots = new Slot[8];
        int slotCount;
        readonly List<int> free = new List<int>();
        ValueCollection values;
        KeyCollection keys;

        public int Count => index.Count;

        public TValue this[TKey key]
        {
            get => index.TryGetValue(key, out int i) ? slots[i].Value : throw new KeyNotFoundException(key?.ToString());
            set
            {
                if (index.TryGetValue(key, out int i)) { slots[i].Value = value; return; }
                Insert(key, value);
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (index.ContainsKey(key)) throw new ArgumentException("Schlüssel existiert bereits: " + key);
            Insert(key, value);
        }

        void Insert(TKey key, TValue value)
        {
            int i;
            if (free.Count > 0)
            {
                i = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
            }
            else
            {
                if (slotCount == slots.Length) Array.Resize(ref slots, slots.Length * 2);
                i = slotCount++;
            }
            slots[i] = new Slot { Key = key, Value = value, Used = true };
            index.Add(key, i);
        }

        public bool Remove(TKey key)
        {
            if (!index.TryGetValue(key, out int i)) return false;
            index.Remove(key);
            slots[i] = default;
            free.Add(i);
            return true;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (index.TryGetValue(key, out int i)) { value = slots[i].Value; return true; }
            value = default;
            return false;
        }

        public bool ContainsKey(TKey key) => index.ContainsKey(key);

        public void Clear()
        {
            index.Clear();
            Array.Clear(slots, 0, slotCount);
            slotCount = 0;
            free.Clear();
        }

        public ValueCollection Values => values ??= new ValueCollection(this);
        public KeyCollection Keys => keys ??= new KeyCollection(this);

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            readonly StableMap<TKey, TValue> map;
            int i;
            KeyValuePair<TKey, TValue> current;

            internal Enumerator(StableMap<TKey, TValue> map) { this.map = map; i = -1; current = default; }

            public bool MoveNext()
            {
                while (++i < map.slotCount)
                {
                    if (!map.slots[i].Used) continue;
                    current = new KeyValuePair<TKey, TValue>(map.slots[i].Key, map.slots[i].Value);
                    return true;
                }
                current = default;
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => current;
            object IEnumerator.Current => current;
            public void Reset() { i = -1; current = default; }
            public void Dispose() { }
        }

        public sealed class ValueCollection : IReadOnlyCollection<TValue>
        {
            readonly StableMap<TKey, TValue> map;
            internal ValueCollection(StableMap<TKey, TValue> map) => this.map = map;
            public int Count => map.Count;
            public ValueEnumerator GetEnumerator() => new ValueEnumerator(map);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct ValueEnumerator : IEnumerator<TValue>
        {
            Enumerator inner;
            internal ValueEnumerator(StableMap<TKey, TValue> map) => inner = new Enumerator(map);
            public bool MoveNext() => inner.MoveNext();
            public TValue Current => inner.Current.Value;
            object IEnumerator.Current => Current;
            public void Reset() => inner.Reset();
            public void Dispose() { }
        }

        public sealed class KeyCollection : IReadOnlyCollection<TKey>
        {
            readonly StableMap<TKey, TValue> map;
            internal KeyCollection(StableMap<TKey, TValue> map) => this.map = map;
            public int Count => map.Count;
            public IEnumerator<TKey> GetEnumerator() { foreach (var kv in map) yield return kv.Key; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // ------------------------------------------------------------ Spielstand

        /// <summary>
        /// Schreibt die Platzbelegung (inkl. Lücken und Reihenfolge der freien Plätze) und je belegtem Platz den Eintrag
        /// über <paramref name="writeEntry"/>.
        /// </summary>
        public void WriteLayout(Persistence.SaveWriter w, Action<TKey, TValue> writeEntry)
        {
            w.Int(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                w.Bool(slots[i].Used);
                if (slots[i].Used) writeEntry(slots[i].Key, slots[i].Value);
            }
            w.Int(free.Count);
            foreach (int f in free) w.Int(f);
        }

        /// <summary>Stellt eine mit <see cref="WriteLayout"/> geschriebene Belegung wieder her (vorheriger Inhalt wird verworfen).</summary>
        public void ReadLayout(Persistence.SaveReader r, Func<KeyValuePair<TKey, TValue>> readEntry)
        {
            Clear();
            int n = r.Count(1);
            if (slots.Length < n) slots = new Slot[Math.Max(8, n)];
            var holes = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!r.Bool()) { holes.Add(i); continue; }
                var kv = readEntry();
                if (index.ContainsKey(kv.Key)) throw new Persistence.SaveFormatException("Doppelter Schlüssel: " + kv.Key);
                slots[i] = new Slot { Key = kv.Key, Value = kv.Value, Used = true };
                index.Add(kv.Key, i);
            }
            slotCount = n;
            int f = r.Count(4);
            if (f != holes.Count) throw new Persistence.SaveFormatException("Freiliste passt nicht zur Belegung.");
            var seen = new bool[n];
            for (int k = 0; k < f; k++)
            {
                int s = r.Int();
                if (s < 0 || s >= n || slots[s].Used || seen[s]) throw new Persistence.SaveFormatException("Freiliste ungültig.");
                seen[s] = true;
                free.Add(s);
            }
        }

        /// <summary>Einträge in Durchlauf-Reihenfolge einfügen (ohne Lücken) – für Abschnitte, die keine Belegung speichern.</summary>
        public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> entries)
        {
            foreach (var kv in entries) Add(kv.Key, kv.Value);
        }
    }
}
