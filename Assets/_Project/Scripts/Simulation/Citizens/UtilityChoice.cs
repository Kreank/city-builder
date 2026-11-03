using System;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Gewichteter Zufall für Utility-AI-Entscheidungen: Softmax über Nutzwerte mit Temperatur.
    /// Temperatur → 0 wählt immer die beste Option, größere Temperatur streut. Zufall nur über <see cref="SimRandom"/>.
    /// Arbeitet auf vorhandenen Arrays (keine Allokation).
    /// </summary>
    public static class UtilityChoice
    {
        /// <summary>Unter dieser Temperatur wird nicht gewürfelt (beste Option).</summary>
        public const float MinTemperature = 0.001f;

        /// <summary>Wählt einen Index aus <paramref name="scores"/>[0..count).</summary>
        public static int Choose(float[] scores, int count, float temperature, SimRandom rng)
        {
            if (count <= 0) return -1;
            int best = 0;
            for (int i = 1; i < count; i++) if (scores[i] > scores[best]) best = i;
            if (count == 1 || temperature < MinTemperature) return best;

            float max = scores[best];
            double total = 0;
            for (int i = 0; i < count; i++) total += Math.Exp((scores[i] - max) / temperature);
            double roll = rng.NextDouble() * total;
            for (int i = 0; i < count; i++)
            {
                roll -= Math.Exp((scores[i] - max) / temperature);
                if (roll <= 0) return i;
            }
            return count - 1;
        }

        /// <summary>Wahrscheinlichkeit der Option <paramref name="index"/> unter Softmax (für Tests/Anzeige).</summary>
        public static float Probability(float[] scores, int count, float temperature, int index)
        {
            if (count <= 0 || index < 0 || index >= count) return 0f;
            if (temperature < MinTemperature)
            {
                int best = 0;
                for (int i = 1; i < count; i++) if (scores[i] > scores[best]) best = i;
                return best == index ? 1f : 0f;
            }
            float max = scores[0];
            for (int i = 1; i < count; i++) max = MathF.Max(max, scores[i]);
            double total = 0;
            for (int i = 0; i < count; i++) total += Math.Exp((scores[i] - max) / temperature);
            return (float)(Math.Exp((scores[index] - max) / temperature) / total);
        }

        /// <summary>Zwei Optionen: Wahrscheinlichkeit für a (logistisch).</summary>
        public static float Probability(float a, float b, float temperature)
        {
            if (temperature < MinTemperature) return a > b ? 1f : a < b ? 0f : 0.5f;
            return (float)(1.0 / (1.0 + Math.Exp((b - a) / temperature)));
        }
    }
}
