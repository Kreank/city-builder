using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>
    /// Schreibt einen Abschnitt des Spielstands (Little Endian, Gleitkommazahlen bitgenau).
    /// Hilfen für Vektoren, Listen, Wörterbücher und Id-Referenzen (0 = keine).
    /// Gegenstück: <see cref="SaveReader"/> – Reihenfolge der Aufrufe muss exakt übereinstimmen.
    /// </summary>
    public sealed class SaveWriter
    {
        readonly BinaryWriter w;

        public SaveWriter(Stream stream) => w = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);

        public void Bool(bool v) => w.Write(v);
        public void Byte(byte v) => w.Write(v);
        public void Int(int v) => w.Write(v);
        public void UInt(uint v) => w.Write(v);
        public void Long(long v) => w.Write(v);
        public void ULong(ulong v) => w.Write(v);
        public void Float(float v) => w.Write(v);
        public void Double(double v) => w.Write(v);

        /// <summary>Text (null erlaubt).</summary>
        public void String(string v)
        {
            w.Write(v != null);
            if (v != null) w.Write(v);
        }

        public void Vector2(Vector2 v) { w.Write(v.X); w.Write(v.Y); }

        public void NullableVector2(Vector2? v)
        {
            w.Write(v.HasValue);
            if (v.HasValue) Vector2(v.Value);
        }

        /// <summary>Aufzählung als Ganzzahl.</summary>
        public void Enum<T>(T v) where T : struct, System.Enum => w.Write(Convert.ToInt32(v));

        /// <summary>Verweis über die Id (0 = keiner).</summary>
        public void Ref<T>(T obj, Func<T, int> id) where T : class => w.Write(obj == null ? 0 : id(obj));

        // ------------------------------------------------------------ Felder

        public void Floats(float[] a)
        {
            w.Write(a == null ? -1 : a.Length);
            if (a == null) return;
            foreach (var v in a) w.Write(v);
        }

        public void Doubles(double[] a)
        {
            w.Write(a == null ? -1 : a.Length);
            if (a == null) return;
            foreach (var v in a) w.Write(v);
        }

        public void Ints(int[] a)
        {
            w.Write(a == null ? -1 : a.Length);
            if (a == null) return;
            foreach (var v in a) w.Write(v);
        }

        public void Bytes(byte[] a)
        {
            w.Write(a == null ? -1 : a.Length);
            if (a != null) w.Write(a);
        }

        // ------------------------------------------------------------ Sammlungen

        public void List<T>(IReadOnlyCollection<T> items, Action<T> write)
        {
            w.Write(items == null ? -1 : items.Count);
            if (items == null) return;
            foreach (var x in items) write(x);
        }

        public void IntList(IReadOnlyCollection<int> items)
        {
            w.Write(items == null ? -1 : items.Count);
            if (items == null) return;
            foreach (var x in items) w.Write(x);
        }

        /// <summary>Wörterbuch in Durchlauf-Reihenfolge (Lesen fügt in derselben Reihenfolge ein).</summary>
        public void Dictionary<TKey, TValue>(IReadOnlyCollection<KeyValuePair<TKey, TValue>> items, Action<TKey> writeKey, Action<TValue> writeValue)
        {
            w.Write(items == null ? -1 : items.Count);
            if (items == null) return;
            foreach (var kv in items) { writeKey(kv.Key); writeValue(kv.Value); }
        }

        /// <summary>
        /// Nachschlage-Wörterbuch mit Ganzzahl-Schlüssel, <b>nach Schlüssel sortiert</b> (kanonisch: gleicher Inhalt →
        /// gleiche Bytes, egal in welcher Reihenfolge eingefügt/entfernt wurde). Nur für Wörterbücher, die nie durchlaufen
        /// werden – sonst <see cref="Core.StableMap{TKey,TValue}"/> verwenden.
        /// </summary>
        public void IntDictionary<TValue>(Dictionary<int, TValue> items, Action<TValue> writeValue)
        {
            w.Write(items.Count);
            foreach (int key in SortedKeys(items)) { w.Write(key); writeValue(items[key]); }
        }

        /// <summary>Schlüssel aufsteigend (für kanonisches Schreiben von Nachschlage-Tabellen).</summary>
        public static List<TKey> SortedKeys<TKey, TValue>(Dictionary<TKey, TValue> items)
        {
            var keys = new List<TKey>(items.Keys);
            keys.Sort();
            return keys;
        }

        public void Flush() => w.Flush();
    }
}
