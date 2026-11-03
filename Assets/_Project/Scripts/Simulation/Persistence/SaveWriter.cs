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

        /// <summary>
        /// Aufzählung <b>über ihren Namen</b> (robust gegen umsortierte/ergänzte Werte; Flags als „A, B“). Standard für alle
        /// Enums im Spielstand.
        /// </summary>
        public void Name<T>(T v) where T : struct, System.Enum => w.Write(EnumNames<T>.Of(v));

        /// <summary>Feld, das über eine Aufzählung indiziert ist (Index i = (T)i), als Paare Name → Wert.</summary>
        public void NamedFloats<T>(float[] values) where T : struct, System.Enum
        {
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++) { w.Write(EnumNames<T>.OfIndex(i)); w.Write(values[i]); }
        }

        public void NamedDoubles<T>(double[] values) where T : struct, System.Enum
        {
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++) { w.Write(EnumNames<T>.OfIndex(i)); w.Write(values[i]); }
        }

        /// <summary>Feld über Text-Schlüssel (z. B. Dienst-Kanäle) als Paare Schlüssel → Wert.</summary>
        public void KeyedFloats(IReadOnlyList<string> keys, float[] values)
        {
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++) { w.Write(keys[i]); w.Write(values[i]); }
        }

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

    /// <summary>Zwischenspeicher Name ↔ Wert je Aufzählung (schnell bei vielen Bürgern; Flags-Kombinationen über ToString/TryParse).</summary>
    static class EnumNames<T> where T : struct, System.Enum
    {
        static readonly Dictionary<T, string> names = new Dictionary<T, string>();
        static readonly Dictionary<string, T> values = new Dictionary<string, T>();

        static EnumNames()
        {
            foreach (T v in System.Enum.GetValues(typeof(T)))
            {
                string n = v.ToString();
                if (!names.ContainsKey(v)) names[v] = n;
                values[n] = v;
            }
        }

        public static string Of(T v) => names.TryGetValue(v, out var n) ? n : v.ToString();

        public static string OfIndex(int i) => Of((T)System.Enum.ToObject(typeof(T), i));

        public static bool TryParse(string s, out T v)
        {
            if (s != null && values.TryGetValue(s, out v)) return true;
            return System.Enum.TryParse(s, false, out v) && !int.TryParse(s, out _);
        }
    }
}
