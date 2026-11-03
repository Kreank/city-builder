using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Kleine 2D-Geometrie-Helfer für Straßenflächen: Schnitte, Flächen, Triangulierung.</summary>
    public static class Geometry2D
    {
        public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Linke Normale (gegen den Uhrzeigersinn gedreht).</summary>
        public static Vector2 Left(Vector2 dir) => new Vector2(-dir.Y, dir.X);

        /// <summary>Rechte Normale (im Uhrzeigersinn gedreht) – wie <see cref="RoadSegment.Right"/>.</summary>
        public static Vector2 Right(Vector2 dir) => new Vector2(dir.Y, -dir.X);

        /// <summary>Schnitt zweier Geraden a + s·da und b + t·db (ohne Bereichsprüfung).</summary>
        public static bool LineIntersection(Vector2 a, Vector2 da, Vector2 b, Vector2 db, out float s, out float t)
        {
            s = t = 0f;
            float denom = Cross(da, db);
            float scale = da.Length() * db.Length();
            if (scale < 1e-12f || MathF.Abs(denom) <= 1e-6f * scale) return false;
            var ab = b - a;
            s = Cross(ab, db) / denom;
            t = Cross(ab, da) / denom;
            return true;
        }

        /// <summary>Echter Schnitt zweier Strecken (Berührung an den Enden zählt nicht).</summary>
        public static bool SegmentsCross(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out Vector2 hit)
        {
            hit = default;
            if (!LineIntersection(a0, a1 - a0, b0, b1 - b0, out float s, out float t)) return false;
            const float e = 1e-4f;
            if (s <= e || s >= 1f - e || t <= e || t >= 1f - e) return false;
            hit = a0 + (a1 - a0) * s;
            return true;
        }

        /// <summary>Hat der Linienzug Selbstschnitte (zwischen nicht benachbarten Stücken)?</summary>
        public static bool HasSelfIntersection(IReadOnlyList<Vector2> pts)
        {
            for (int i = 0; i < pts.Count - 1; i++)
                for (int j = i + 2; j < pts.Count - 1; j++)
                    if (SegmentsCross(pts[i], pts[i + 1], pts[j], pts[j + 1], out _)) return true;
            return false;
        }

        /// <summary>
        /// Entfernt Schleifen aus einem Linienzug (z. B. Innenkante einer engen Kurve): Schneiden sich zwei
        /// Stücke, werden alle Punkte dazwischen auf den Schnittpunkt gezogen. Die Punktzahl bleibt gleich,
        /// damit die Zuordnung zu den Stützstellen der Kurve erhalten bleibt.
        /// </summary>
        public static bool RemoveLoops(Vector2[] pts)
        {
            bool changed = false;
            int n = pts.Length;
            for (int i = 0; i < n - 1; i++)
            {
                // vom Ende her suchen → größte Schleife zuerst
                for (int j = n - 2; j >= i + 2; j--)
                {
                    if (!SegmentsCross(pts[i], pts[i + 1], pts[j], pts[j + 1], out var hit)) continue;
                    for (int k = i + 1; k <= j; k++) pts[k] = hit;
                    changed = true;
                    i = j - 1; // weiter ab dem Schnittpunkt
                    break;
                }
            }
            return changed;
        }

        /// <summary>Vorzeichenbehaftete Fläche (positiv = gegen den Uhrzeigersinn).</summary>
        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
                a += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return a * 0.5f;
        }

        public static float TriangleArea(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a) * 0.5f;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            const float e = 1e-6f;
            return Cross(b - a, p - a) > e && Cross(c - b, p - b) > e && Cross(a - c, p - c) > e;
        }

        /// <summary>
        /// Ohren-Triangulierung eines einfachen Polygons. Liefert Index-Tripel gegen den Uhrzeigersinn.
        /// Doppelte/kollineare Punkte werden übersprungen (ergeben keine Dreiecke).
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> poly)
        {
            var result = new List<int>();
            int n = poly.Count;
            if (n < 3) return result;
            var idx = new List<int>(n);
            bool ccw = SignedArea(poly) >= 0f;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * n * n)
            {
                bool clipped = false;
                int m = idx.Count;
                for (int i = 0; i < m; i++)
                {
                    int ia = idx[(i + m - 1) % m], ib = idx[i], ic = idx[(i + 1) % m];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    float cross = Cross(b - a, c - b);
                    float scale = (b - a).Length() * (c - b).Length();
                    if (MathF.Abs(cross) <= 1e-6f * scale + 1e-9f)
                    {
                        // doppelt oder kollinear → Punkt fällt weg
                        idx.RemoveAt(i);
                        clipped = true;
                        break;
                    }
                    if (cross < 0f) continue; // überstumpfe Ecke
                    bool empty = true;
                    for (int k = 0; k < m && empty; k++)
                    {
                        int ik = idx[k];
                        if (ik == ia || ik == ib || ik == ic) continue;
                        var p = poly[ik];
                        if (p == a || p == b || p == c) continue;
                        if (InTriangle(p, a, b, c)) empty = false;
                    }
                    if (!empty) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Polygon nicht einfach (sollte nicht vorkommen) → Fächer als Notlösung
                    for (int i = 1; i < idx.Count - 1; i++) { result.Add(idx[0]); result.Add(idx[i]); result.Add(idx[i + 1]); }
                    return result;
                }
            }
            if (idx.Count == 3 && TriangleArea(poly[idx[0]], poly[idx[1]], poly[idx[2]]) > 1e-7f)
            {
                result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]);
            }
            return result;
        }

        /// <summary>
        /// Überlappen sich zwei Dreiecke flächig (mehr als <paramref name="tolerance"/> Meter tief)?
        /// Gemeinsame Kanten/Ecken zählen nicht. Trennende-Achsen-Test.
        /// </summary>
        public static bool TrianglesOverlap(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2, float tolerance = 0.01f)
        {
            var ta = new[] { a0, a1, a2 };
            var tb = new[] { b0, b1, b2 };
            for (int pass = 0; pass < 2; pass++)
            {
                var t = pass == 0 ? ta : tb;
                for (int e = 0; e < 3; e++)
                {
                    var edge = t[(e + 1) % 3] - t[e];
                    float len = edge.Length();
                    if (len < 1e-6f) continue;
                    var axis = new Vector2(-edge.Y, edge.X) / len;
                    float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                    for (int k = 0; k < 3; k++)
                    {
                        float pa = Vector2.Dot(ta[k], axis), pb = Vector2.Dot(tb[k], axis);
                        minA = MathF.Min(minA, pa); maxA = MathF.Max(maxA, pa);
                        minB = MathF.Min(minB, pb); maxB = MathF.Max(maxB, pb);
                    }
                    if (maxA - minB <= tolerance || maxB - minA <= tolerance) return false;
                }
            }
            return true;
        }
    }
}
