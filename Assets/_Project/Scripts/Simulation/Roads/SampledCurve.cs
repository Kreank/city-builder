using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Abgetastete Bézierkurve mit Bogenlänge, Höhe und Krümmung. Grundlage für Straßen-Meshes:
    /// Punkte "bei Meter d, seitlich um offset versetzt" liefert <see cref="OffsetAt"/> – in engen Kurven
    /// ohne Selbstüberschneidung der Innenkante (Versatz wird auf den Krümmungsradius begrenzt, Schleifen
    /// werden zusammengezogen).
    /// </summary>
    public sealed class SampledCurve
    {
        /// <summary>Innenkante höchstens bis zu diesem Anteil des Krümmungsradius (1 = bis zum Krümmungsmittelpunkt).</summary>
        public const float MaxInnerOffsetRatio = 0.95f;

        readonly Vector2[] points;
        readonly Vector2[] rights;
        readonly float[] dist;
        readonly float[] heights;
        /// <summary>Vorzeichenbehaftete Krümmung (1/m), positiv = Linkskurve.</summary>
        readonly float[] curvature;
        readonly Dictionary<float, Vector2[]> offsetCache = new Dictionary<float, Vector2[]>();

        public int Count => points.Length;
        public float Length => dist[dist.Length - 1];
        public Vector2 Point(int i) => points[i];
        public Vector2 RightAt(int i) => rights[i];
        public float Distance(int i) => dist[i];
        public float Height(int i) => heights[i];
        public float Curvature(int i) => curvature[i];

        public SampledCurve(Vector2 p0, Vector2 p1, Vector2 p2, Func<float, float> heightAt)
        {
            float approx = Bezier.ApproximateLength(p0, p1, p2);
            // Abstand ~2 m, in Kurven zusätzlich höchstens ~4° Richtungsänderung je Stück
            float turn = 0f;
            var l0 = p1 - p0;
            var l1 = p2 - p1;
            if (l0.LengthSquared() > 1e-8f && l1.LengthSquared() > 1e-8f) turn = RoadGeometry.Angle(l0, l1);
            int n = Math.Clamp(Math.Max((int)MathF.Ceiling(approx / 2f), (int)MathF.Ceiling(turn / 4f)), 2, 512);

            points = new Vector2[n + 1];
            rights = new Vector2[n + 1];
            dist = new float[n + 1];
            heights = new float[n + 1];
            curvature = new float[n + 1];
            var second = 2f * (p0 - 2f * p1 + p2);
            float d = 0f;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Bezier.Evaluate(p0, p1, p2, t);
                var der = Bezier.Derivative(p0, p1, p2, t);
                float speed = der.Length();
                var tan = speed > 1e-4f ? der / speed : SafeDirection(p0, p1, p2);
                if (i > 0) d += Vector2.Distance(points[i - 1], p);
                points[i] = p;
                rights[i] = Geometry2D.Right(tan);
                dist[i] = d;
                heights[i] = heightAt(t);
                curvature[i] = speed > 1e-3f ? Geometry2D.Cross(der, second) / (speed * speed * speed) : 0f;
            }
        }

        static Vector2 SafeDirection(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            var d = p2 - p0;
            return d.LengthSquared() > 1e-10f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        /// <summary>Stützstelle unterhalb von d und Anteil u bis zur nächsten.</summary>
        public int Locate(float d, out float u)
        {
            int last = dist.Length - 1;
            if (d <= 0f) { u = 0f; return 0; }
            if (d >= dist[last]) { u = 1f; return last - 1; }
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (dist[mid] <= d) lo = mid; else hi = mid;
            }
            u = (d - dist[lo]) / MathF.Max(1e-6f, dist[hi] - dist[lo]);
            return lo;
        }

        public void At(float d, out Vector2 pos, out Vector2 right, out float height)
        {
            int i = Locate(d, out float u);
            pos = Vector2.Lerp(points[i], points[i + 1], u);
            var r = Vector2.Lerp(rights[i], rights[i + 1], u);
            right = r.LengthSquared() > 1e-10f ? Vector2.Normalize(r) : rights[i];
            height = heights[i] + (heights[i + 1] - heights[i]) * u;
        }

        /// <summary>Punkt bei Bogenlänge d, seitlich um offset versetzt (positiv = rechts), ohne Selbstschnitt.</summary>
        public Vector2 OffsetAt(float d, float offset)
        {
            var line = OffsetLine(offset);
            int i = Locate(d, out float u);
            return Vector2.Lerp(line[i], line[i + 1], u);
        }

        /// <summary>
        /// Versetzte Kante je Stützstelle (zwischengespeichert). Innen in engen Kurven (Versatz größer als
        /// <see cref="MaxInnerOffsetRatio"/> × Krümmungsradius) liefe die Kante rückwärts (Schwalbenschwanz):
        /// Diese Zone wird auf einen Punkt (begrenzter Versatz an der stärksten Krümmung) zusammengezogen,
        /// verbleibende Schleifen auf ihren Schnittpunkt. Grenze: Haarnadeln, deren Schenkel kürzer als der
        /// Versatz sind, überdecken sich physisch – das kann eine Kante allein nicht lösen.
        /// </summary>
        public Vector2[] OffsetLine(float offset)
        {
            if (offsetCache.TryGetValue(offset, out var cached)) return cached;
            int n = points.Length;
            var line = new Vector2[n];
            bool tight = false;
            for (int i = 0; i < n; i++)
            {
                line[i] = points[i] + rights[i] * offset;
                // Versatz zur Kurveninnenseite: o·κ < 0 (links = negativ bei Linkskurve κ > 0)
                if (offset * curvature[i] < -0.5f) tight = true;
            }
            if (!tight) { offsetCache[offset] = line; return line; }

            // Enge Zonen (Versatz ≥ MaxInnerOffsetRatio × Radius) auf einen Punkt zusammenziehen: den begrenzten
            // Versatz an der stärksten Krümmung. Die Streifen werden dort zum Fächer statt sich zu überschlagen.
            for (int a = 0; a < n; a++)
            {
                if (offset * curvature[a] >= -MaxInnerOffsetRatio) continue;
                int b = a;
                while (b + 1 < n && offset * curvature[b + 1] < -MaxInnerOffsetRatio) b++;
                int m = a;
                for (int k = a; k <= b; k++) if (MathF.Abs(curvature[k]) > MathF.Abs(curvature[m])) m = k;
                var x = points[m] + rights[m] * (-MaxInnerOffsetRatio / curvature[m]);
                // Nachbarn, die schon "hinter" dem Fächerpunkt liegen, ebenfalls einziehen
                while (a > 0 && Vector2.Dot(x - line[a - 1], Tangent(a - 1)) < 0f) a--;
                while (b + 1 < n && Vector2.Dot(line[b + 1] - x, Tangent(b + 1)) < 0f) b++;
                for (int k = a; k <= b; k++) line[k] = x;
                a = b;
            }
            Geometry2D.RemoveLoops(line);
            offsetCache[offset] = line;
            return line;
        }

        Vector2 Tangent(int i) => new Vector2(-rights[i].Y, rights[i].X);

        /// <summary>Stützstellen zwischen d0 und d1 (inkl. Ränder).</summary>
        public void Stations(float d0, float d1, List<float> result)
        {
            result.Clear();
            result.Add(d0);
            for (int i = 0; i < dist.Length; i++)
                if (dist[i] > d0 + 0.01f && dist[i] < d1 - 0.01f) result.Add(dist[i]);
            result.Add(d1);
        }

        /// <summary>Kleinster Krümmungsradius (m) – unendlich bei Geraden.</summary>
        public float MinRadius
        {
            get
            {
                float k = 0f;
                foreach (float c in curvature) k = MathF.Max(k, MathF.Abs(c));
                return k > 1e-6f ? 1f / k : float.PositiveInfinity;
            }
        }
    }
}
