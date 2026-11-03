using System;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Eigener, geseedeter Zufallsgenerator (xoshiro128** mit SplitMix-Initialisierung).
    /// Gleicher Seed → gleiche Folge auf jeder Plattform (Unity/Mono, .NET, Server) – Grundlage für
    /// Multiplayer, Replays und Tests. Keine Uhrzeit, kein globaler Zustand.
    /// </summary>
    public sealed class SimRandom
    {
        uint s0, s1, s2, s3;

        public SimRandom(int seed)
        {
            ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            s0 = (uint)SplitMix(ref x);
            s1 = (uint)SplitMix(ref x);
            s2 = (uint)SplitMix(ref x);
            s3 = (uint)SplitMix(ref x);
            if ((s0 | s1 | s2 | s3) == 0) s0 = 1;
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static uint Rotl(uint v, int k) => (v << k) | (v >> (32 - k));

        /// <summary>Nächste 32-Bit-Zahl.</summary>
        public uint NextUInt()
        {
            uint result = Rotl(s1 * 5, 7) * 9;
            uint t = s1 << 9;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = Rotl(s3, 11);
            return result;
        }

        /// <summary>0 ≤ x &lt; 1.</summary>
        public double NextDouble() => (NextUInt() >> 8) * (1.0 / 16777216.0);

        /// <summary>0 ≤ x &lt; 1 (float).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>0 ≤ x &lt; max.</summary>
        public int Next(int max)
        {
            if (max <= 0) return 0;
            return (int)((NextUInt() * (ulong)max) >> 32);
        }

        /// <summary>min ≤ x &lt; max (wie <see cref="System.Random.Next(int,int)"/>).</summary>
        public int Next(int min, int max) => max <= min ? min : min + Next(max - min);

        /// <summary>Gleichverteilt in [-1, 1].</summary>
        public float NextSigned() => NextFloat() * 2f - 1f;

        /// <summary>true mit Wahrscheinlichkeit p.</summary>
        public bool Chance(double p) => p > 0 && NextDouble() < p;

        /// <summary>Stabiler Hash (für Namen/Texte aus Ids, ohne den Zufallsstrom zu verbrauchen).</summary>
        public static uint Hash(int a, int b = 0)
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }
}
