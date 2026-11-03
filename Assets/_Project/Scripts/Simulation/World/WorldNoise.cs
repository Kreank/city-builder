using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Deterministisches Gitterrauschen (2D/3D, Wert-Rauschen + fBm) für den Weltgenerator und die
    /// Ressourcen-Raster. Nur Ganzzahl-Hashes und einfache Gleitkomma-Arithmetik – gleicher Seed ergibt
    /// auf derselben Laufzeit dieselbe Welt. 3D-Rauschen auf der Einheitskugel vermeidet Nähte am
    /// Datumsgrenz-Meridian und Verzerrung an den Polen.
    /// </summary>
    public static class WorldNoise
    {
        public static uint Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u; h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE3Du; h = (h << 17) | (h >> 15);
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>0..1 aus Hash.</summary>
        public static float Hash01(int x, int y, int z, int seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / (float)0xFFFFFF;

        static float Smooth(float t) => t * t * (3f - 2f * t);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Value3(float x, float y, float z, int seed)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
            float u = Smooth(x - xi), v = Smooth(y - yi), w = Smooth(z - zi);
            float c000 = Hash01(xi, yi, zi, seed), c100 = Hash01(xi + 1, yi, zi, seed);
            float c010 = Hash01(xi, yi + 1, zi, seed), c110 = Hash01(xi + 1, yi + 1, zi, seed);
            float c001 = Hash01(xi, yi, zi + 1, seed), c101 = Hash01(xi + 1, yi, zi + 1, seed);
            float c011 = Hash01(xi, yi + 1, zi + 1, seed), c111 = Hash01(xi + 1, yi + 1, zi + 1, seed);
            float a = Lerp(Lerp(c000, c100, u), Lerp(c010, c110, u), v);
            float b = Lerp(Lerp(c001, c101, u), Lerp(c011, c111, u), v);
            return Lerp(a, b, w);
        }

        /// <summary>fBm 3D, Ergebnis ~0..1 (Mittel 0,5).</summary>
        public static float Fbm3(float x, float y, float z, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value3(x, y, z, seed + i * 1013) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; z *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static float Value2(float x, float y, int seed)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float u = Smooth(x - xi), v = Smooth(y - yi);
            float a = Hash01(xi, yi, 0, seed), b = Hash01(xi + 1, yi, 0, seed);
            float c = Hash01(xi, yi + 1, 0, seed), d = Hash01(xi + 1, yi + 1, 0, seed);
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        }

        /// <summary>fBm 2D, Ergebnis ~0..1 (Mittel 0,5).</summary>
        public static float Fbm2(float x, float y, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value2(x, y, seed + i * 1013) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>1D-Wertrauschen (z. B. Wetter über die Tage), 0..1.</summary>
        public static float Value1(float x, int seed)
        {
            int xi = (int)MathF.Floor(x);
            return Lerp(Hash01(xi, 0, 0, seed), Hash01(xi + 1, 0, 0, seed), Smooth(x - xi));
        }

        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>Binärer Min-Heap (Unity/.NET Standard 2.1 hat keine PriorityQueue). Gleiche Priorität → kleinere Id zuerst (deterministisch).</summary>
    internal sealed class MinHeap
    {
        readonly List<(float key, int id)> items = new List<(float, int)>();
        public int Count => items.Count;

        static bool Less((float key, int id) a, (float key, int id) b) => a.key < b.key || (a.key == b.key && a.id < b.id);

        public void Push(float key, int id)
        {
            items.Add((key, id));
            int i = items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (!Less(items[i], items[p])) break;
                (items[i], items[p]) = (items[p], items[i]);
                i = p;
            }
        }

        public (float key, int id) Pop()
        {
            var top = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < items.Count && Less(items[l], items[m])) m = l;
                if (r < items.Count && Less(items[r], items[m])) m = r;
                if (m == i) break;
                (items[i], items[m]) = (items[m], items[i]);
                i = m;
            }
            return top;
        }
    }
}
