using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>
    /// Liest einen Abschnitt des Spielstands (Gegenstück zu <see cref="SaveWriter"/>). Jede Länge wird gegen die
    /// verbleibenden Bytes geprüft – beschädigte Daten führen zu <see cref="SaveFormatException"/> statt zu riesigen
    /// Speicheranforderungen oder Abstürzen.
    /// </summary>
    public sealed class SaveReader
    {
        readonly BinaryReader r;
        readonly Stream stream;

        public SaveReader(Stream stream)
        {
            this.stream = stream;
            r = new BinaryReader(stream, new UTF8Encoding(false), leaveOpen: true);
        }

        /// <summary>Noch nicht gelesene Bytes des Abschnitts.</summary>
        public long Remaining => stream.Length - stream.Position;

        void Need(long bytes)
        {
            if (bytes < 0 || bytes > Remaining) throw new SaveFormatException("Abschnitt ist kürzer als erwartet (abgeschnitten oder beschädigt).");
        }

        public bool Bool() { Need(1); byte b = r.ReadByte(); if (b > 1) throw new SaveFormatException("Ungültiger Wahrheitswert."); return b == 1; }
        public byte Byte() { Need(1); return r.ReadByte(); }
        public int Int() { Need(4); return r.ReadInt32(); }
        public uint UInt() { Need(4); return r.ReadUInt32(); }
        public long Long() { Need(8); return r.ReadInt64(); }
        public ulong ULong() { Need(8); return r.ReadUInt64(); }
        public float Float() { Need(4); return r.ReadSingle(); }
        public double Double() { Need(8); return r.ReadDouble(); }

        public string String()
        {
            if (!Bool()) return null;
            try { return r.ReadString(); }
            catch (EndOfStreamException) { throw new SaveFormatException("Text abgeschnitten."); }
            catch (FormatException) { throw new SaveFormatException("Text beschädigt."); }
        }

        public Vector2 Vector2() { float x = Float(); return new Vector2(x, Float()); }

        public Vector2? NullableVector2() => Bool() ? Vector2() : (Vector2?)null;

        /// <summary>Aufzählung über den Namen (Gegenstück zu <see cref="SaveWriter.Name{T}"/>); unbekannter Name → <paramref name="fallback"/>.</summary>
        public T Name<T>(T fallback = default) where T : struct, System.Enum =>
            TryName<T>(out var v) ? v : fallback;

        /// <summary>Aufzählung über den Namen; false, wenn der Name (z. B. aus einer neueren Version) unbekannt ist.</summary>
        public bool TryName<T>(out T value) where T : struct, System.Enum
        {
            return EnumNames<T>.TryParse(String0(), out value);
        }

        string String0()
        {
            try { return r.ReadString(); }
            catch (EndOfStreamException) { throw new SaveFormatException("Text abgeschnitten."); }
        }

        /// <summary>Gegenstück zu <see cref="SaveWriter.NamedFloats{T}"/>: unbekannte Namen werden übersprungen, fehlende bleiben.</summary>
        public void NamedFloatsInto<T>(float[] target) where T : struct, System.Enum
        {
            int n = Count(5);
            for (int i = 0; i < n; i++)
            {
                bool ok = TryName<T>(out var k);
                float v = Float();
                int idx = ok ? Convert.ToInt32(k) : -1;
                if (idx >= 0 && idx < target.Length) target[idx] = v;
            }
        }

        public void NamedDoublesInto<T>(double[] target) where T : struct, System.Enum
        {
            int n = Count(9);
            for (int i = 0; i < n; i++)
            {
                bool ok = TryName<T>(out var k);
                double v = Double();
                int idx = ok ? Convert.ToInt32(k) : -1;
                if (idx >= 0 && idx < target.Length) target[idx] = v;
            }
        }

        public void KeyedFloatsInto(IReadOnlyList<string> keys, float[] target)
        {
            int n = Count(5);
            for (int i = 0; i < n; i++)
            {
                string key = String0();
                float v = Float();
                for (int k = 0; k < keys.Count && k < target.Length; k++) if (keys[k] == key) { target[k] = v; break; }
            }
        }

        /// <summary>
        /// Anzahl von Elementen mit mindestens <paramref name="minBytesPerItem"/> Bytes je Element – prüft gegen die
        /// Restlänge (−1 ist nicht erlaubt; für „null“-fähige Listen <see cref="CountOrNull"/>).
        /// </summary>
        public int Count(int minBytesPerItem)
        {
            int n = Int();
            if (n < 0) throw new SaveFormatException("Negative Anzahl.");
            Need((long)n * Math.Max(0, minBytesPerItem));
            return n;
        }

        /// <summary>Wie <see cref="Count"/>, −1 = null.</summary>
        public int CountOrNull(int minBytesPerItem)
        {
            int n = Int();
            if (n < -1) throw new SaveFormatException("Negative Anzahl.");
            if (n > 0) Need((long)n * Math.Max(0, minBytesPerItem));
            return n;
        }

        // ------------------------------------------------------------ Felder

        public float[] Floats()
        {
            int n = CountOrNull(4);
            if (n < 0) return null;
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadSingle();
            return a;
        }

        /// <summary>Liest ein Feld fester Länge in ein vorhandenes Feld (z. B. Raster), Länge muss passen.</summary>
        public void FloatsInto(float[] target)
        {
            int n = Count(4);
            if (n != target.Length) throw new SaveFormatException($"Rastergröße passt nicht ({n} statt {target.Length}).");
            for (int i = 0; i < n; i++) target[i] = r.ReadSingle();
        }

        public double[] Doubles()
        {
            int n = CountOrNull(8);
            if (n < 0) return null;
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadDouble();
            return a;
        }

        public void DoublesInto(double[] target)
        {
            int n = Count(8);
            if (n != target.Length) throw new SaveFormatException($"Feldgröße passt nicht ({n} statt {target.Length}).");
            for (int i = 0; i < n; i++) target[i] = r.ReadDouble();
        }

        public int[] Ints()
        {
            int n = CountOrNull(4);
            if (n < 0) return null;
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }

        public void IntsInto(int[] target)
        {
            int n = Count(4);
            if (n != target.Length) throw new SaveFormatException($"Feldgröße passt nicht ({n} statt {target.Length}).");
            for (int i = 0; i < n; i++) target[i] = r.ReadInt32();
        }

        public byte[] Bytes()
        {
            int n = CountOrNull(1);
            if (n < 0) return null;
            return r.ReadBytes(n);
        }

        public void BytesInto(byte[] target)
        {
            int n = Count(1);
            if (n != target.Length) throw new SaveFormatException($"Feldgröße passt nicht ({n} statt {target.Length}).");
            int read = r.Read(target, 0, n);
            if (read != n) throw new SaveFormatException("Abschnitt abgeschnitten.");
        }

        // ------------------------------------------------------------ Sammlungen

        /// <summary>Liste (null bei −1). <paramref name="minBytesPerItem"/> dient der Plausibilitätsprüfung.</summary>
        public List<T> List<T>(Func<T> read, int minBytesPerItem = 1)
        {
            int n = CountOrNull(minBytesPerItem);
            if (n < 0) return null;
            var list = new List<T>(n);
            for (int i = 0; i < n; i++) list.Add(read());
            return list;
        }

        /// <summary>Liste in eine vorhandene Liste lesen (vorher geleert).</summary>
        public void ListInto<T>(List<T> target, Func<T> read, int minBytesPerItem = 1)
        {
            target.Clear();
            int n = CountOrNull(minBytesPerItem);
            for (int i = 0; i < n; i++) target.Add(read());
        }

        public List<int> IntList()
        {
            int n = CountOrNull(4);
            if (n < 0) return null;
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadInt32());
            return list;
        }

        public void DictionaryInto<TKey, TValue>(IDictionary<TKey, TValue> target, Func<TKey> readKey, Func<TValue> readValue, int minBytesPerItem = 1)
        {
            target.Clear();
            int n = CountOrNull(minBytesPerItem);
            for (int i = 0; i < n; i++)
            {
                var k = readKey();
                var v = readValue();
                if (target.ContainsKey(k)) throw new SaveFormatException("Doppelter Schlüssel: " + k);
                target.Add(k, v);
            }
        }

        public void IntDictionaryInto<TValue>(Dictionary<int, TValue> target, Func<TValue> readValue, int minBytesPerItem = 1) =>
            DictionaryInto(target, Int, readValue, 4 + minBytesPerItem);
    }
}
