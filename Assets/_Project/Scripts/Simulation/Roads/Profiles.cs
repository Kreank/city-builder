using System;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Höhenprofile für geländefolgende Straßen: abtasten, glätten, Steigung begrenzen, zerteilen.
    /// Ein Profil sind Höhen an gleichmäßig über t (0..1) verteilten Stellen.
    /// </summary>
    public static class Profiles
    {
        /// <summary>Abstand der Profilpunkte in Metern.</summary>
        public const float Spacing = 4f;

        public static float Sample(float[] profile, float t)
        {
            float f = Math.Clamp(t, 0f, 1f) * (profile.Length - 1);
            int i = Math.Min((int)f, profile.Length - 2);
            float u = f - i;
            return profile[i] + (profile[i + 1] - profile[i]) * u;
        }

        public static float MaxSlope(float[] profile, float length)
        {
            float ds = length / (profile.Length - 1);
            if (ds <= 1e-4f) return 0f;
            float max = 0f;
            for (int i = 0; i < profile.Length - 1; i++) max = MathF.Max(max, MathF.Abs(profile[i + 1] - profile[i]) / ds);
            return max;
        }

        /// <summary>Teilprofil für den Bereich [a, b] eines Höhenverlaufs.</summary>
        public static float[] Resample(Func<float, float> heightAt, float a, float b, float length)
        {
            int n = Math.Clamp((int)MathF.Ceiling(length * (b - a) / Spacing) + 1, 2, 257);
            var p = new float[n];
            for (int k = 0; k < n; k++) p[k] = heightAt(a + (b - a) * k / (n - 1));
            return p;
        }

        /// <summary>Ragt das Gelände so weit über die direkte Linie, wird untertunnelt.</summary>
        public const float TunnelThreshold = 14f;
        /// <summary>Liegt das Gelände so weit unter der direkten Linie (Tal), wird überbrückt.</summary>
        public const float BridgeThreshold = 10f;
        /// <summary>Über Wasser so hoch bleiben (lichte Höhe für Boote/Hochwasser).</summary>
        public const float WaterClearance = 3f;

        /// <summary>
        /// Automatische Trassierung für ebenerdig gebaute Straßen (wie im echten Straßenbau):
        /// geringe Höhenunterschiede → Straße folgt geglättet dem Gelände (wird eingeebnet),
        /// Berg deutlich über der direkten Linie → Tunnel, Tal/Wasser deutlich darunter → Brücke.
        /// Enden auf den Anschlusshöhen, Steigung auf <paramref name="maxSlope"/> begrenzt.
        /// Liefert null, wenn die Enden mit dieser Steigung nicht verbindbar sind.
        /// </summary>
        public static float[] FollowTerrain(IGround ground, Vector2 p0, Vector2 c, Vector2 p2, float length,
            float startHeight, float endHeight, float maxSlope)
        {
            int n = Math.Clamp((int)MathF.Ceiling(length / Spacing) + 1, 3, 257);
            float ds = length / (n - 1);
            var terrain = new float[n];
            var raw = new float[n];
            var water = new bool[n];
            for (int k = 0; k < n; k++)
            {
                var p = Bezier.Evaluate(p0, c, p2, k / (float)(n - 1));
                terrain[k] = ground.HeightAt(p);
                water[k] = ground.IsWater(p);
                raw[k] = MathF.Max(terrain[k], ground.WaterLevel + WaterClearance);
            }

            // Geländefolgende Linie: glätten (gleitender Mittelwert über ~24 m, zweimal)
            var smooth = Smooth(Smooth(raw, 3), 3);

            // Enden weich an die Anschlusshöhen führen
            int m = Math.Min(n / 2, 5);
            for (int k = 0; k < n; k++)
            {
                if (k < m) smooth[k] = Lerp(startHeight, smooth[k], k / (float)m);
                int j = n - 1 - k;
                if (k < m) smooth[j] = Lerp(endHeight, smooth[j], k / (float)m);
            }
            smooth[0] = startHeight;
            smooth[n - 1] = endHeight;

            // Berge untertunneln, Täler/Wasser überbrücken: dort bleibt die Straße auf der direkten Linie
            float g = maxSlope * ds * 0.999f;
            bool directFeasible = MathF.Abs(endHeight - startHeight) <= g * (n - 1);
            if (directFeasible)
            {
                var weight = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float line = Lerp(startHeight, endHeight, k / (float)(n - 1));
                    bool mountain = terrain[k] - line > TunnelThreshold;
                    bool valley = line - terrain[k] > BridgeThreshold || (water[k] && line >= ground.WaterLevel + WaterClearance);
                    if (mountain || valley) weight[k] = 1f;
                }
                // Bereiche etwas aufweiten und weich ausblenden (Rampen zu Portal/Widerlager)
                weight = Smooth(Dilate(weight, 6), 4);
                for (int k = 1; k < n - 1; k++)
                {
                    float line = Lerp(startHeight, endHeight, k / (float)(n - 1));
                    float w = Math.Clamp(weight[k] * 1.5f, 0f, 1f);
                    smooth[k] = Lerp(smooth[k], line, w);
                }
            }

            // Mindesthöhe über Wasser, steigungsgerecht in beide Richtungen ausgebreitet (Brückenrampen)
            var floor = new float[n];
            for (int k = 0; k < n; k++) floor[k] = water[k] ? ground.WaterLevel + WaterClearance : float.MinValue;
            for (int k = 1; k < n; k++) floor[k] = MathF.Max(floor[k], floor[k - 1] - g);
            for (int k = n - 2; k >= 0; k--) floor[k] = MathF.Max(floor[k], floor[k + 1] - g);
            if (startHeight < floor[0] - 0.01f || endHeight < floor[n - 1] - 0.01f) return null; // Ufer zu niedrig für die Rampe
            for (int k = 0; k < n; k++) smooth[k] = MathF.Max(smooth[k], floor[k]);

            // Steigung begrenzen: vorwärts und rückwärts klemmen (hält die Mindesthöhe ein, da sie selbst steigungsgerecht ist)
            for (int k = 1; k < n; k++) smooth[k] = Math.Clamp(smooth[k], smooth[k - 1] - g, smooth[k - 1] + g);
            smooth[n - 1] = endHeight;
            for (int k = n - 2; k >= 0; k--) smooth[k] = Math.Clamp(smooth[k], smooth[k + 1] - g, smooth[k + 1] + g);
            if (MathF.Abs(smooth[0] - startHeight) > 0.01f) return null; // Enden mit dieser Steigung nicht verbindbar
            smooth[0] = startHeight;
            return smooth;
        }

        static float[] Dilate(float[] src, int radius)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
                for (int k = Math.Max(0, i - radius); k <= Math.Min(src.Length - 1, i + radius); k++)
                    dst[i] = MathF.Max(dst[i], src[k]);
            return dst;
        }

        static float[] Smooth(float[] src, int radius)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = i - radius; k <= i + radius; k++)
                {
                    if (k < 0 || k >= src.Length) continue;
                    sum += src[k];
                    count++;
                }
                dst[i] = sum / count;
            }
            return dst;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
