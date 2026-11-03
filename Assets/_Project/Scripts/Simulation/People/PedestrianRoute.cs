using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.People
{
    /// <summary>
    /// Fertiger Fußweg als Linienzug (Haustür → Gehweg → … → Tür/Park) mit Bogenlänge, Höhe (inkl. Gehweg-/Fahrbahnhöhe)
    /// und Belag je Punkt. Unveränderlich; wird vom <see cref="PedestrianPlanner"/> je Start/Ziel zwischengespeichert.
    /// </summary>
    public sealed class PedestrianRoute
    {
        public const byte SurfaceGround = 0, SurfaceSidewalk = 1, SurfaceCrossing = 2, SurfacePark = 3;

        readonly Vector2[] points;
        readonly float[] heights;
        readonly float[] cum;
        readonly byte[] surface;

        public int Count => points.Length;
        public float Length => cum[cum.Length - 1];
        public Vector2 Point(int i) => points[i];
        public float Height(int i) => heights[i];
        public float Distance(int i) => cum[i];
        /// <summary>Belag des Punkts (<see cref="SurfaceSidewalk"/>, <see cref="SurfaceCrossing"/> …).</summary>
        public byte Surface(int i) => surface[i];
        public Vector2 Start => points[0];
        public Vector2 End => points[points.Length - 1];
        /// <summary>Anzahl Straßenquerungen (zusammenhängende Fahrbahn-Abschnitte).</summary>
        public int Crossings { get; }

        internal PedestrianRoute(List<Vector2> p, List<float> h, List<byte> s)
        {
            points = p.ToArray();
            heights = h.ToArray();
            surface = s.ToArray();
            cum = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            for (int i = 1; i < surface.Length; i++)
                if (surface[i] == SurfaceCrossing && surface[i - 1] != SurfaceCrossing) Crossings++;
        }

        /// <summary>Position, Höhe und Laufrichtung bei Weglänge d (auf 0..Length begrenzt).</summary>
        public void Sample(float d, out Vector2 position, out float height, out Vector2 direction)
        {
            int last = points.Length - 1;
            if (last == 0) { position = points[0]; height = heights[0]; direction = Vector2.UnitY; return; }
            d = Math.Clamp(d, 0f, cum[last]);
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cum[mid] <= d) lo = mid; else hi = mid;
            }
            float seg = cum[hi] - cum[lo];
            float u = seg > 1e-6f ? (d - cum[lo]) / seg : 0f;
            position = Vector2.Lerp(points[lo], points[hi], u);
            height = heights[lo] + (heights[hi] - heights[lo]) * u;
            var dir = points[hi] - points[lo];
            // Nullstücke (doppelte Punkte) überspringen
            for (int k = hi; dir.LengthSquared() < 1e-8f && k < last; k++) dir = points[k + 1] - points[lo];
            direction = dir.LengthSquared() > 1e-8f ? Vector2.Normalize(dir) : Vector2.UnitY;
        }

        /// <summary>Kleinster Abstand eines Punkts zum Linienzug (Tests, Auswahl).</summary>
        public float DistanceTo(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var ab = points[i + 1] - a;
                float l2 = ab.LengthSquared();
                float u = l2 > 1e-8f ? Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : 0f;
                best = MathF.Min(best, Vector2.Distance(a + ab * u, p));
            }
            return points.Length == 1 ? Vector2.Distance(points[0], p) : best;
        }
    }

    /// <summary>Sammelt Punkte eines Fußwegs (doppelte Punkte werden zusammengefasst).</summary>
    public sealed class PedestrianRouteBuilder
    {
        readonly List<Vector2> points = new List<Vector2>();
        readonly List<float> heights = new List<float>();
        readonly List<byte> surface = new List<byte>();

        public int Count => points.Count;

        public void Clear()
        {
            points.Clear();
            heights.Clear();
            surface.Clear();
        }

        public void Add(Vector2 p, float height, byte surf)
        {
            int n = points.Count;
            if (n > 0 && Vector2.DistanceSquared(points[n - 1], p) < 1e-4f)
            {
                // gleicher Ort: Querungs-/Höhenangabe des neuen Punkts gewinnt nur, wenn er tiefer liegt (Bordstein)
                if (surf == PedestrianRoute.SurfaceCrossing) { surface[n - 1] = surf; heights[n - 1] = height; }
                return;
            }
            points.Add(p);
            heights.Add(height);
            surface.Add(surf);
        }

        public PedestrianRoute Build() => points.Count == 0 ? null : new PedestrianRoute(points, heights, surface);
    }
}
