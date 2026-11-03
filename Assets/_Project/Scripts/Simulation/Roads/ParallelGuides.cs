using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Wie gut passt ein Parallelabstand zu zwei Gebäudereihen Rücken an Rücken?</summary>
    public enum ParallelFit
    {
        /// <summary>Beide Reihen stehen Rücken an Rücken, ohne Lücke und ohne Überlappung (± <see cref="ParallelGuides.Tolerance"/>).</summary>
        Optimal,
        /// <summary>Weiter als nötig: zwischen den Rückseiten bleibt ein Streifen ungenutzt.</summary>
        Gap,
        /// <summary>Zu eng: die Gebäude der beiden Reihen würden sich überlappen (eine Reihe muss flacher bauen oder entfällt).</summary>
        Overlap,
    }

    /// <summary>Auswertung eines Abstands zwischen einer bestehenden und einer geplanten parallelen Straße.</summary>
    public struct ParallelGapInfo
    {
        /// <summary>Abstand der Straßenachsen (m).</summary>
        public float AxisDistance;
        /// <summary>Abstand der Gehweg-Außenkanten (m).</summary>
        public float EdgeGap;
        /// <summary>Gebäudetiefe der gewählten Bauart (m), für die bewertet wird.</summary>
        public float Depth;
        /// <summary>Wie viele Reihen dieser Tiefe zwischen die Straßen passen (0–2).</summary>
        public int Rows;
        /// <summary>Ungenutzter Streifen zwischen den Rückseiten (m).</summary>
        public float Unused;
        /// <summary>Überlappung der Rückseiten (m).</summary>
        public float Overlap;
        /// <summary>Randabstand minus optimaler Randabstand (positiv = zu weit).</summary>
        public float Delta;
        public ParallelFit Fit;
    }

    /// <summary>Eine Parallel-Hilfslinie neben einer bestehenden Stadtstraße, am Cursor ausgewertet.</summary>
    public struct GuideHit
    {
        public RoadSegment Segment;
        /// <summary>Parameter auf dem Segment (bei geraden Straßen auch außerhalb 0..1 = Verlängerung).</summary>
        public float T;
        /// <summary>+1 = rechts in Segmentrichtung, −1 = links.</summary>
        public int Side;
        /// <summary>Achsabstand der Hilfslinie (m).</summary>
        public float Offset;
        /// <summary>Punkt auf der Hilfslinie.</summary>
        public Vector2 Point;
        /// <summary>Richtung der Hilfslinie an diesem Punkt (= Segmentrichtung).</summary>
        public Vector2 Tangent;
        /// <summary>Abstand des abgefragten Punkts zur Hilfslinie (m).</summary>
        public float Miss;
        /// <summary>Tatsächlicher Achsabstand des abgefragten Punkts.</summary>
        public float Distance;
        public bool Straight;
        /// <summary>Kurvenzuschlag im <see cref="Offset"/> (m, 0 bei geraden Straßen), s. <see cref="ParallelGuides.CurveAllowance"/>.</summary>
        public float Allowance;
    }

    /// <summary>
    /// Bauhilfe „optimaler Straßenabstand“ (seit „Setzen statt Zonen“ nach <b>Gebäudetiefen</b>): wie weit zwei parallele
    /// Stadtstraßen auseinander liegen müssen, damit zwei Reihen gesetzter Gebäude der gewählten Bauart Rücken an Rücken
    /// passen, dazu Hilfslinien und Einrasten. Reine Geometrie, liest nur das Netz.
    ///
    /// Gesetzte Gebäude rasten mit der Vorderkante <see cref="RoadFront.SnapGap"/> vor der Gehweg-Außenkante
    /// (<see cref="RoadType.HalfWidth"/>) ein und sind <i>Tiefe</i> m tief. Optimaler Randabstand = 2 × (SnapGap + Tiefe),
    /// Achsabstand = halbe Breite A + halbe Breite B + 2 × (0,5 m + Tiefe); z. B. Einfamilienhaus (16 m) zwischen zwei
    /// city_2: 6,5 + 6,5 + 33 = 46 m, Mehrfamilienhaus/Bürohaus (24 m) 62 m, Wohnblock/Industriehalle (32 m) 78 m.
    /// (Früher: Zonenraster mit 4 + 4 Reihen à 8 m = 64 m Randabstand.)
    /// </summary>
    public static class ParallelGuides
    {
        /// <summary>Bis zu dieser Lücke gilt ein Abstand als optimal (m). Enger als <see cref="OverlapTolerance"/> ist schon „zu eng“ – Gebäude überlappen sonst.</summary>
        public const float Tolerance = 0.25f;
        /// <summary>So viel enger als optimal wird noch geduldet (Rundung; die Rechteckprüfung der Gebäude duldet 0,01 m).</summary>
        public const float OverlapTolerance = 0.01f;
        /// <summary>Gerade Hilfslinien reichen so weit über die Straßenenden hinaus (m).</summary>
        public const float Extension = 80f;
        /// <summary>Standard-Tiefe (Mehrfamilienhaus, Wohn- und Geschäftshaus, Bürohaus, Gewerbehalle).</summary>
        public const float DefaultDepth = 24f;

        /// <summary>Von der Gehweg-Außenkante bis zur Rückseite eines gesetzten Gebäudes der Tiefe <paramref name="depth"/>.</summary>
        public static float RowDepth(float depth) => RoadFront.SnapGap + MathF.Max(0f, depth);

        /// <summary>Randabstand (Gehwegkante zu Gehwegkante) für zwei Reihen Rücken an Rücken.</summary>
        public static float OptimalEdgeGap(float depth = DefaultDepth) => 2f * RowDepth(depth);

        /// <summary>Optimaler Achsabstand zweier paralleler Straßen.</summary>
        public static float OptimalAxisDistance(RoadType existing, RoadType planned, float depth = DefaultDepth) =>
            existing.HalfWidth + planned.HalfWidth + OptimalEdgeGap(depth);

        /// <summary>Bewertet einen Achsabstand für Gebäude der Tiefe <paramref name="depth"/>.</summary>
        public static ParallelGapInfo Evaluate(RoadType existing, RoadType planned, float axisDistance, float depth = DefaultDepth)
        {
            var info = new ParallelGapInfo
            {
                AxisDistance = axisDistance,
                EdgeGap = axisDistance - existing.HalfWidth - planned.HalfWidth,
                Depth = depth,
            };
            float row = RowDepth(depth);
            info.Rows = row <= 0f ? 0 : Math.Clamp((int)MathF.Floor((info.EdgeGap + OverlapTolerance) / row), 0, 2);
            info.Delta = info.EdgeGap - OptimalEdgeGap(depth);
            info.Unused = MathF.Max(0f, info.Delta);
            info.Overlap = MathF.Max(0f, -info.Delta);
            info.Fit = info.Delta >= -OverlapTolerance && info.Delta <= Tolerance ? ParallelFit.Optimal
                : info.Delta > 0f ? ParallelFit.Gap : ParallelFit.Overlap;
            return info;
        }

        /// <summary>Beschriftung, z. B. „Abstand 62,0 m · optimal (Rücken an Rücken, 2 × 24 m tief)“.</summary>
        public static string Describe(ParallelGapInfo info)
        {
            string depth = $"{info.Depth:0} m tief";
            switch (info.Fit)
            {
                case ParallelFit.Optimal:
                    return $"Abstand {FormatMeters(info.AxisDistance)} · optimal (Rücken an Rücken, 2 × {depth})";
                case ParallelFit.Gap:
                    return $"Abstand {FormatMeters(info.AxisDistance)} · {FormatMeters(info.Delta)} Lücke zwischen den Rückseiten (2 × {depth})";
                default:
                    string rows = info.Rows >= 2 ? "" : info.Rows == 1 ? ", nur eine Reihe passt" : ", keine Reihe passt";
                    return $"Abstand {FormatMeters(info.AxisDistance)} · zu eng: {FormatMeters(info.Overlap)} Überlappung (2 × {depth}{rows})";
            }
        }

        /// <summary>Genauigkeit der Kurven-Parallele (<see cref="FitParallelControl"/>: 90°-Bogen &lt; 1 m Abweichung) als Puffer.</summary>
        public const float CurveFitMargin = 0.75f;

        /// <summary>
        /// Kurvenzuschlag (m): Auf Bögen stehen die Gebäude als Sehnen, ihre hinteren Ecken ragen je Reihe um ≈ b²/(8R) über
        /// die Linie der Rückseiten (b = Breite = Tiefe der quadratischen Bauarten) – beide Reihen zusammen b²/(4R) – dazu
        /// der Puffer für die Bézier-Näherung der Parallele. Gerade/fast gerade (R &gt; 3 km): 0.
        /// </summary>
        public static float CurveAllowance(float depth, float radius)
        {
            if (float.IsInfinity(radius) || radius > 3000f || depth <= 0f) return 0f;
            return depth * depth / (4f * MathF.Max(radius, depth)) + CurveFitMargin;
        }

        /// <summary>Krümmungsradius des Segments bei <paramref name="t"/> (∞ bei gerader Straße).</summary>
        public static float CurveRadius(RoadSegment s, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            var p0 = s.Start.Position; var c = s.Control; var p2 = s.End.Position;
            var d1 = 2f * (1f - t) * (c - p0) + 2f * t * (p2 - c);
            var d2 = 2f * (p2 - 2f * c + p0);
            float cross = MathF.Abs(d1.X * d2.Y - d1.Y * d2.X);
            float len = d1.Length();
            if (cross < 1e-6f || len < 1e-4f) return float.PositiveInfinity;
            return len * len * len / cross;
        }

        /// <summary>Meterangabe mit einer Nachkommastelle und deutschem Komma.</summary>
        public static string FormatMeters(float m) => m.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " m";

        // ------------------------------------------------------------------ Hilfslinien

        /// <summary>Gilt das Segment als gerade (Kontrollpunkt auf der Verbindungslinie)?</summary>
        public static bool IsStraight(RoadSegment s)
        {
            var a = s.Start.Position;
            var d = s.End.Position - a;
            float len = d.Length();
            if (len < 1e-4f) return true;
            var c = s.Control - a;
            return MathF.Abs(d.X * c.Y - d.Y * c.X) / len < 0.05f;
        }

        /// <summary>Können an dieser Straße Gebäude stehen (ebenerdige Stadtstraße)?</summary>
        public static bool HasFrontage(RoadSegment s) =>
            s.Type.Category == RoadCategory.City && MathF.Abs(s.ElevationAt(0.5f)) < 1.5f;

        /// <summary>
        /// Punkt im (vorzeichenbehafteten, + = rechts) Abstand <paramref name="signedOffset"/> neben der Achse.
        /// Gerade Straßen werden für t außerhalb 0..1 linear verlängert.
        /// </summary>
        public static Vector2 OffsetPoint(RoadSegment s, float t, float signedOffset)
        {
            if (IsStraight(s))
            {
                var a = s.Start.Position;
                var d = s.End.Position - a;
                float len = d.Length();
                var dir = len > 1e-4f ? d / len : Vector2.UnitX;
                return a + d * t + new Vector2(dir.Y, -dir.X) * signedOffset;
            }
            float tc = Math.Clamp(t, 0f, 1f);
            return s.Evaluate(tc) + s.Right(tc) * signedOffset;
        }

        /// <summary>Hilfslinie des Segments am Punkt <paramref name="p"/> auswerten (ohne Radius-Prüfung).</summary>
        public static bool TryEvaluate(RoadSegment s, Vector2 p, float offset, out GuideHit hit)
        {
            hit = default;
            Vector2 q, tangent;
            float t;
            bool straight = IsStraight(s);
            if (straight)
            {
                var a = s.Start.Position;
                var d = s.End.Position - a;
                float len = d.Length();
                if (len < 1e-4f) return false;
                tangent = d / len;
                float u = Vector2.Dot(p - a, tangent);
                if (u < -Extension || u > len + Extension) return false;
                t = u / len;
                q = a + tangent * u;
            }
            else
            {
                t = s.ClosestT(p);
                q = s.Evaluate(t);
                tangent = s.Tangent(t);
                // Hinter den Enden einer Kurve gibt es keine Parallele
                if ((t <= 1e-3f || t >= 1f - 1e-3f) && MathF.Abs(Vector2.Dot(p - q, tangent)) > 1f) return false;
            }
            var right = new Vector2(tangent.Y, -tangent.X);
            float signed = Vector2.Dot(p - q, right);
            int side = signed >= 0f ? 1 : -1;
            hit = new GuideHit
            {
                Segment = s,
                T = t,
                Side = side,
                Offset = offset,
                Point = q + right * side * offset,
                Tangent = tangent,
                Distance = MathF.Abs(signed),
                Miss = MathF.Abs(MathF.Abs(signed) - offset),
                Straight = straight,
            };
            return true;
        }

        /// <summary>
        /// Alle Hilfslinien bestehender Stadtstraßen, deren Linie höchstens <paramref name="radius"/> vom Punkt entfernt
        /// liegt, nach Abstand sortiert (je Segment die nähere Seite).
        /// </summary>
        public static List<GuideHit> FindGuides(RoadNetwork network, Vector2 p, RoadType planned, float depth, float radius,
            List<GuideHit> result = null)
        {
            result ??= new List<GuideHit>();
            result.Clear();
            if (planned == null || planned.Category != RoadCategory.City) return result;
            foreach (var s in network.Segments)
            {
                if (!HasFrontage(s)) continue;
                float offset = OptimalAxisDistance(s.Type, planned, depth);
                float reach = offset + radius + Extension;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
                if (p.X < min.X - reach || p.Y < min.Y - reach || p.X > max.X + reach || p.Y > max.Y + reach) continue;
                if (!TryEvaluate(s, p, offset, out var hit)) continue;
                // Kurve: hintere Ecken beider Reihen ragen über die Bogenlinie → Zuschlag an dieser Stelle
                if (!hit.Straight)
                {
                    float extra = CurveAllowance(depth, CurveRadius(s, hit.T));
                    if (extra > 0f && TryEvaluate(s, p, offset + extra, out var curved)) { hit = curved; hit.Allowance = extra; }
                }
                if (hit.Miss <= radius) result.Add(hit);
            }
            result.Sort((a, b) => a.Miss.CompareTo(b.Miss));
            return result;
        }

        /// <summary>Rastet auf die nächste Hilfslinie im Umkreis <paramref name="snapRadius"/> ein.</summary>
        public static bool TrySnap(RoadNetwork network, Vector2 p, RoadType planned, float depth, float snapRadius, out GuideHit hit)
        {
            var list = FindGuides(network, p, planned, depth, snapRadius);
            hit = list.Count > 0 ? list[0] : default;
            return list.Count > 0;
        }

        /// <summary>
        /// Kontrollpunkt einer Kurve zwischen zwei Punkten derselben Hilfslinie, sodass die neue Straße an beiden Enden
        /// exakt parallel zur bestehenden verläuft (Schnitt der Tangenten). Klassische Näherung der Offset-Kurve.
        /// </summary>
        public static bool TryParallelControl(GuideHit a, GuideHit b, out Vector2 control)
        {
            control = (a.Point + b.Point) * 0.5f;
            float cross = a.Tangent.X * b.Tangent.Y - a.Tangent.Y * b.Tangent.X;
            if (MathF.Abs(cross) < 1e-3f) return false; // (fast) gerade → Mitte
            var d = b.Point - a.Point;
            float s = (d.X * b.Tangent.Y - d.Y * b.Tangent.X) / cross;
            control = a.Point + a.Tangent * s;
            return true;
        }

        /// <summary>
        /// Kontrollpunkt einer Kurve von <paramref name="start"/> nach <paramref name="end"/> (beide auf derselben Hilfslinie
        /// eines gekrümmten Segments), die der Offset-Kurve im Abstand <paramref name="signedOffset"/> möglichst genau folgt
        /// (kleinste Quadrate über 15 Stützstellen, 4× umparametriert; 90°-Bogen mit 77 m Abstand unter 1 m Abweichung).
        /// Genauer als <see cref="TryParallelControl"/> (dort ~4,7 m), die Enden sind dafür nicht exakt tangential.
        /// </summary>
        public static Vector2 FitParallelControl(RoadSegment s, float signedOffset, float t0, float t1)
        {
            var p0 = OffsetPoint(s, t0, signedOffset);
            var p2 = OffsetPoint(s, t1, signedOffset);
            const int n = 16;
            var targets = new Vector2[n - 1];
            var us = new float[n - 1];
            for (int i = 1; i < n; i++)
            {
                targets[i - 1] = OffsetPoint(s, t0 + (t1 - t0) * i / n, signedOffset);
                us[i - 1] = i / (float)n;
            }
            Vector2 control = (p0 + p2) * 0.5f;
            // Ausgleich, danach Stützstellen auf die nächsten Kurvenpunkte umparametrieren und wiederholen
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Vector2 num = Vector2.Zero;
                float den = 0f;
                for (int i = 0; i < targets.Length; i++)
                {
                    float u = us[i];
                    float a = (1f - u) * (1f - u), b = 2f * u * (1f - u), c = u * u;
                    num += b * (targets[i] - a * p0 - c * p2);
                    den += b * b;
                }
                if (den < 1e-6f) break;
                control = num / den;
                for (int i = 0; i < targets.Length; i++) us[i] = Bezier.ClosestT(p0, control, p2, targets[i]);
            }
            return control;
        }

        /// <summary>
        /// „Blöcke“: Achsabstand zweier Querstraßen auf einer Straße vom Typ <paramref name="road"/>, damit dazwischen
        /// genau <paramref name="cells"/> Zellen ohne Rest liegen. Das Raster beginnt an Kreuzungen erst hinter der
        /// Kreuzungsfläche (größte halbe Breite der anderen Straßen am Knoten + 1 m, s. <see cref="ZoneGrid"/>).
        /// </summary>
        public static float BlockLength(RoadType road, RoadType cross, int cells) =>
            cells * ZoneGrid.CellSize + 2f * (MathF.Max(road.HalfWidth, cross.HalfWidth) + 1f);
    }
}
