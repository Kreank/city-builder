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

        /// <summary>
        /// Blossom f(a, b) der Kurve. Der Kontrollpunkt des Teilstücks [a, b] ist f(a, b),
        /// Start/Ende sind f(a, a) bzw. f(b, b). Damit lassen sich Kurven exakt zerteilen.
        /// </summary>
        public static Vector2 Blossom(Vector2 p0, Vector2 p1, Vector2 p2, float a, float b) =>
            (1f - a) * (1f - b) * p0 + ((1f - a) * b + a * (1f - b)) * p1 + a * b * p2;

        /// <summary>Zerteilt die Kurve bei t (de Casteljau) – liefert die Kontrollpunkte beider Hälften.</summary>
        public static void Split(Vector2 p0, Vector2 p1, Vector2 p2, float t,
            out Vector2 leftControl, out Vector2 point, out Vector2 rightControl)
        {
            leftControl = Vector2.Lerp(p0, p1, t);
            rightControl = Vector2.Lerp(p1, p2, t);
            point = Vector2.Lerp(leftControl, rightControl, t);
        }

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

        /// <summary>Parameter t des Kurvenpunkts, der <paramref name="point"/> am nächsten liegt.</summary>
        public static float ClosestT(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 point, int samples = 32)
        {
            float bestT = 0f, best = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float d = Vector2.DistanceSquared(Evaluate(p0, p1, p2, t), point);
                if (d < best) { best = d; bestT = t; }
            }

            // Verfeinern per Goldenem Schnitt im Nachbarintervall
            float lo = System.Math.Max(0f, bestT - 1f / samples);
            float hi = System.Math.Min(1f, bestT + 1f / samples);
            const float g = 0.618034f;
            for (int i = 0; i < 20; i++)
            {
                float m1 = hi - (hi - lo) * g;
                float m2 = lo + (hi - lo) * g;
                if (Vector2.DistanceSquared(Evaluate(p0, p1, p2, m1), point) <
                    Vector2.DistanceSquared(Evaluate(p0, p1, p2, m2), point))
                    hi = m2;
                else
                    lo = m1;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>Kürzester Abstand eines Punkts zur Kurve.</summary>
        public static float DistanceToPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 point) =>
            Vector2.Distance(Evaluate(p0, p1, p2, ClosestT(p0, p1, p2, point)), point);

        public static float DistanceToLineSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-6f) return Vector2.Distance(a, point);
            float t = Vector2.Dot(point - a, ab) / lenSq;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return Vector2.Distance(a + ab * t, point);
        }

        /// <summary>Schnitt zweier Strecken. Liefert die Parameter u (auf a0→a1) und v (auf b0→b1).</summary>
        public static bool LineSegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float u, out float v)
        {
            u = v = 0f;
            Vector2 r = a1 - a0, s = b1 - b0;
            float denom = r.X * s.Y - r.Y * s.X;
            // Relativ prüfen: (fast) parallele/kollineare Stücke liefern sonst numerischen Unsinn
            if (System.Math.Abs(denom) < 1e-4f * r.Length() * s.Length()) return false;
            Vector2 qp = b0 - a0;
            u = (qp.X * s.Y - qp.Y * s.X) / denom;
            v = (qp.X * r.Y - qp.Y * r.X) / denom;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            // Gegenprobe: beide Parameter müssen auf denselben Punkt zeigen
            return Vector2.DistanceSquared(a0 + r * u, b0 + s * v) < 0.01f;
        }

        /// <summary>Achsenparalleler Hüllquader (Kurve liegt immer in der konvexen Hülle der Kontrollpunkte).</summary>
        public static void Bounds(Vector2 p0, Vector2 p1, Vector2 p2, out Vector2 min, out Vector2 max)
        {
            min = Vector2.Min(Vector2.Min(p0, p1), p2);
            max = Vector2.Max(Vector2.Max(p0, p1), p2);
        }
    }
}
