using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Quadratische Bézierkurve: Start, ein Kontrollpunkt, Ende – reicht für Straßenkurven.</summary>
    public static class Bezier
    {
        public static Vector2 Evaluate(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        /// <summary>Ableitung (nicht normalisiert).</summary>
        public static Vector2 Derivative(Vector2 p0, Vector2 p1, Vector2 p2, float t) =>
            2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);

        public static float ApproximateLength(Vector2 p0, Vector2 p1, Vector2 p2, int samples = 32)
        {
            float length = 0f;
            Vector2 prev = p0;
            for (int i = 1; i <= samples; i++)
            {
                Vector2 p = Evaluate(p0, p1, p2, i / (float)samples);
                length += Vector2.Distance(prev, p);
                prev = p;
            }
            return length;
        }

        /// <summary>Kürzester Abstand eines Punkts zur Kurve (Näherung über Abtastung).</summary>
        public static float DistanceToPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 point, int samples = 32)
        {
            float best = float.MaxValue;
            Vector2 prev = p0;
            for (int i = 1; i <= samples; i++)
            {
                Vector2 p = Evaluate(p0, p1, p2, i / (float)samples);
                float d = DistanceToLineSegment(prev, p, point);
                if (d < best) best = d;
                prev = p;
            }
            return best;
        }

        public static float DistanceToLineSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-6f) return Vector2.Distance(a, point);
            float t = Vector2.Dot(point - a, ab) / lenSq;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return Vector2.Distance(a + ab * t, point);
        }
    }
}
