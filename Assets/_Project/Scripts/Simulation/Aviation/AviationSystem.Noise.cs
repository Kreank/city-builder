using System;
using System.Numerics;
using CityBuilder.Simulation.Environment;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Fluglärm: stündlich aus den Flugbewegungen der letzten Stunde (gewichtet nach Muster, Nachtflüge ×2,5) ein eigenes Raster
    /// <see cref="NoiseLayer"/> – stark über der Bahn und entlang der An-/Abflugschneisen (3,2 km, wird breiter und leiser) –,
    /// das in das Lärm-Raster der Stadt einfließt (Maximum). Darüber wirkt es auf Bodenwert und Zufriedenheit wie jeder Lärm;
    /// Bürger erinnern sich an „Fluglärm“. Mit Nachtflugverbot ist es nachts still.
    /// </summary>
    public sealed partial class AviationSystem
    {
        /// <summary>Fluglärm 0..1 je Zelle (gleiches Raster wie der Stadtlärm, gespeichert).</summary>
        public GridLayer NoiseLayer { get; }

        public float AircraftNoiseAt(Vector2 p) => airports.Count == 0 ? 0f : NoiseLayer.Sample(p);

        /// <summary>Pegel aus gewichteten Bewegungen der Stunde.</summary>
        public static float LevelFor(float weightedMovements) =>
            weightedMovements <= 0f ? 0f : MathF.Min(AviationRules.NoiseMax, AviationRules.NoiseBase + AviationRules.NoisePerMovement * weightedMovements);

        /// <summary>Radius des Lärmstempels in Abstand d vom Bahnende.</summary>
        public static float NoiseRadiusAt(float d) =>
            AviationRules.NoiseRadiusNear + (AviationRules.NoiseRadiusFar - AviationRules.NoiseRadiusNear) * Math.Clamp(d / AviationRules.NoiseLength, 0f, 1f);

        /// <summary>Anteil des Pegels in Abstand d (Flugzeug steigt bzw. ist noch hoch → leiser).</summary>
        public static float NoiseFalloffAt(float d)
        {
            float f = 1f - Math.Clamp(d / AviationRules.NoiseLength, 0f, 1f);
            return 0.3f + 0.7f * f * f;
        }

        /// <summary>
        /// Vom <c>NoiseSystem</c> stündlich nach Straßen und Gleisen aufgerufen: Fluglärm-Raster neu zeichnen (Bewegungen seit dem
        /// letzten Stundenwechsel) und als Maximum ins Stadt-Raster übernehmen.
        /// </summary>
        public void StampNoise(GridLayer noise)
        {
            if (airports.Count == 0) return;
            NoiseLayer.Clear();
            foreach (var a in airports)
            {
                float la = LevelFor(a.NoiseHourA), lb = LevelFor(a.NoiseHourB);
                if (la <= 0f && lb <= 0f) continue;
                var l = a.Layout;
                // Bahn (Startlauf, Umkehrschub)
                float lr = MathF.Max(la, lb) * 0.9f;
                for (float u = -l.HalfLength; u <= l.HalfLength; u += 48f)
                    NoiseLayer.StampRadialMax(l.At(u, 0f), AviationRules.NoiseRadiusNear * 1.3f, lr);
                StampCorridor(l.EndA, -l.Dir, la);
                StampCorridor(l.EndB, l.Dir, lb);
            }
            var src = NoiseLayer.RawValues;
            var dst = noise.RawValues;
            int n = Math.Min(src.Length, dst.Length);
            for (int i = 0; i < n; i++) if (src[i] > dst[i]) dst[i] = src[i];
            noise.MarkChanged();
        }

        void StampCorridor(Vector2 end, Vector2 outward, float level)
        {
            if (level <= 0f) return;
            for (float d = 0f; d <= AviationRules.NoiseLength; d += 48f)
            {
                var p = end + outward * d;
                float r = NoiseRadiusAt(d);
                if (p.X < world.Min.X - r || p.Y < world.Min.Y - r || p.X > world.Max.X + r || p.Y > world.Max.Y + r) break;
                NoiseLayer.StampRadialMax(p, r, level * NoiseFalloffAt(d));
            }
        }
    }
}
