using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Wie gut passt ein Parallelabstand zum Zonenraster?</summary>
    public enum ParallelFit
    {
        /// <summary>Zonenstreifen treffen sich lückenlos und ohne Überlappung (± <see cref="ParallelGuides.Tolerance"/>).</summary>
        Optimal,
        /// <summary>Weiter als nötig: ein Streifen bleibt ungenutzt.</summary>
        Gap,
        /// <summary>Zu eng: Zellen überlappen bzw. die neue Straße verliert Reihen.</summary>
        Overlap,
    }

    /// <summary>Auswertung eines Abstands zwischen einer bestehenden und einer geplanten parallelen Straße.</summary>
    public struct ParallelGapInfo
    {
        /// <summary>Abstand der Straßenachsen (m).</summary>
        public float AxisDistance;
        /// <summary>Abstand der Gehweg-Außenkanten (m) – dort beginnt das Zonenraster.</summary>
        public float EdgeGap;
        /// <summary>Reihen, die das Zonenraster der bestehenden (älteren) Straße auf dieser Seite gibt.</summary>
        public int ExistingRows;
        /// <summary>Reihen, die für die neue Straße übrig bleiben.</summary>
        public int PlannedRows;
        /// <summary>Ungenutzter Streifen zwischen den Zonen (m).</summary>
        public float Unused;
        /// <summary>Überlappung der Zellen (m; das Raster duldet bis 1,2 m, darüber fällt eine Reihe weg).</summary>
        public float Overlap;
        /// <summary>Randabstand minus Zielabstand der gewählten Stufe (positiv = zu weit).</summary>
        public float Delta;
        /// <summary>Gewünschte Reihen der neuen Straße (Stufe).</summary>
        public int TargetRows;
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
    }

    /// <summary>
    /// Bauhilfe „optimaler Straßenabstand“: wie weit zwei parallele Stadtstraßen auseinander liegen müssen, damit
    /// sich ihre Zonenstreifen (<see cref="ZoneGrid"/>) lückenlos und ohne Überlappung treffen, dazu Hilfslinien
    /// und Einrasten. Reine Geometrie, liest nur das Netz.
    ///
    /// Zonenraster: beginnt an der Gehweg-Außenkante (<see cref="RoadType.HalfWidth"/>, volle Breite inkl. Gehweg),
    /// je Reihe <see cref="ZoneGrid.CellSize"/> tief. Liegen zwei Straßen nah beieinander, gewinnt die ältere
    /// (kleinere Segment-Id) – sie bekommt ihre Reihen zuerst, die neuere nur, was dahinter frei bleibt
    /// (Zellmitten ≥ <see cref="ZoneGrid.CellSpacing"/> auseinander, ≥ <see cref="ZoneGrid.RoadClearance"/> vom Straßenrand).
    /// Optimaler Randabstand = (Reihen der bestehenden + Reihen der neuen Straße) × 8 m; Standard 4 + 4 = 64 m
    /// („Rücken an Rücken“), Achsabstand = halbe Breite A + halbe Breite B + 64 m.
    /// </summary>
    public static class ParallelGuides
    {
        /// <summary>Bis zu dieser Abweichung gilt ein Abstand als optimal (m).</summary>
        public const float Tolerance = 0.25f;
        /// <summary>Gerade Hilfslinien reichen so weit über die Straßenenden hinaus (m).</summary>
        public const float Extension = 80f;
        /// <summary>Kleinste sinnvolle Stufe: die neue Straße bekommt mindestens eine Reihe.</summary>
        public const int MinRows = 1;

        /// <summary>Tiefe des Zonenstreifens bei <paramref name="rows"/> Reihen (m).</summary>
        public static float ZoneDepth(int rows = ZoneGrid.Depth) => Math.Clamp(rows, 0, ZoneGrid.Depth) * ZoneGrid.CellSize;

        /// <summary>Randabstand (Gehwegkante zu Gehwegkante), bei dem die bestehende Straße volle Tiefe und die neue
        /// genau <paramref name="plannedRows"/> Reihen bekommt.</summary>
        public static float OptimalEdgeGap(int plannedRows = ZoneGrid.Depth) => ZoneDepth(ZoneGrid.Depth) + ZoneDepth(plannedRows);

        /// <summary>Optimaler Achsabstand zweier paralleler Straßen.</summary>
        public static float OptimalAxisDistance(RoadType existing, RoadType planned, int plannedRows = ZoneGrid.Depth) =>
            existing.HalfWidth + planned.HalfWidth + OptimalEdgeGap(plannedRows);

        /// <summary>
        /// Wie verteilt das Zonenraster einen Randabstand auf zwei parallele Straßen (Spalten fluchtend = ungünstigster
        /// Fall)? Bildet die Regeln aus <see cref="ZoneGrid"/> nach: ältere Straße zuerst, Abstand zum Straßenrand,
        /// Mindestabstand der Zellmitten.
        /// </summary>
        public static void RowsBetween(float edgeGap, out int existingRows, out int plannedRows)
        {
            const float cell = ZoneGrid.CellSize, half = cell * 0.5f;
            existingRows = 0;
            for (int j = 0; j < ZoneGrid.Depth; j++)
            {
                if (edgeGap - (half + j * cell) <= ZoneGrid.RoadClearance) break;
                existingRows++;
            }
            plannedRows = 0;
            for (int k = 0; k < ZoneGrid.Depth; k++)
            {
                float x = edgeGap - half - k * cell; // Zellmitte, gemessen vom Rand der bestehenden Straße
                if (x <= ZoneGrid.RoadClearance) break;
                bool free = true;
                for (int j = 0; j < existingRows && free; j++)
                    if (MathF.Abs(x - (half + j * cell)) < ZoneGrid.CellSpacing) free = false;
                if (!free) break;
                plannedRows++;
            }
        }

        /// <summary>Bewertet einen Achsabstand für die Stufe <paramref name="targetRows"/>.</summary>
        public static ParallelGapInfo Evaluate(RoadType existing, RoadType planned, float axisDistance, int targetRows = ZoneGrid.Depth)
        {
            var info = new ParallelGapInfo
            {
                AxisDistance = axisDistance,
                EdgeGap = axisDistance - existing.HalfWidth - planned.HalfWidth,
                TargetRows = Math.Clamp(targetRows, MinRows, ZoneGrid.Depth),
            };
            RowsBetween(info.EdgeGap, out info.ExistingRows, out info.PlannedRows);
            float used = (info.ExistingRows + info.PlannedRows) * ZoneGrid.CellSize;
            info.Unused = MathF.Max(0f, info.EdgeGap - used);
            info.Overlap = MathF.Max(0f, used - info.EdgeGap);
            info.Delta = info.EdgeGap - OptimalEdgeGap(info.TargetRows);
            info.Fit = MathF.Abs(info.Delta) <= Tolerance ? ParallelFit.Optimal
                : info.Delta > 0f ? ParallelFit.Gap : ParallelFit.Overlap;
            return info;
        }

        /// <summary>Beschriftung, z. B. „Abstand 77,0 m · optimal (4 + 4 Reihen)“.</summary>
        public static string Describe(ParallelGapInfo info)
        {
            string rows = $"{info.ExistingRows} + {info.PlannedRows} Reihen";
            string unused = info.Unused > Tolerance ? $", {Meters(info.Unused)} ungenutzt" : "";
            switch (info.Fit)
            {
                case ParallelFit.Optimal:
                    return $"Abstand {Meters(info.AxisDistance)} · optimal ({rows})";
                case ParallelFit.Gap:
                    return $"Abstand {Meters(info.AxisDistance)} · {Meters(info.Delta)} Lücke ({rows}{unused})";
                default:
                    string what = info.Overlap > Tolerance ? $"{Meters(-info.Delta)} Überlappung" : $"{Meters(-info.Delta)} zu nah";
                    return $"Abstand {Meters(info.AxisDistance)} · zu eng: {what} ({rows}{unused})";
            }
        }

        static string Meters(float m) => m.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " m";

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

        /// <summary>Hat diese Straße ein Zonenraster (ebenerdige Stadtstraße)?</summary>
        public static bool HasZones(RoadSegment s) =>
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
        public static List<GuideHit> FindGuides(RoadNetwork network, Vector2 p, RoadType planned, int plannedRows, float radius,
            List<GuideHit> result = null)
        {
            result ??= new List<GuideHit>();
            result.Clear();
            if (planned == null || planned.Category != RoadCategory.City) return result;
            foreach (var s in network.Segments)
            {
                if (!HasZones(s)) continue;
                float offset = OptimalAxisDistance(s.Type, planned, plannedRows);
                float reach = offset + radius + Extension;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
                if (p.X < min.X - reach || p.Y < min.Y - reach || p.X > max.X + reach || p.Y > max.Y + reach) continue;
                if (TryEvaluate(s, p, offset, out var hit) && hit.Miss <= radius) result.Add(hit);
            }
            result.Sort((a, b) => a.Miss.CompareTo(b.Miss));
            return result;
        }

        /// <summary>Rastet auf die nächste Hilfslinie im Umkreis <paramref name="snapRadius"/> ein.</summary>
        public static bool TrySnap(RoadNetwork network, Vector2 p, RoadType planned, int plannedRows, float snapRadius, out GuideHit hit)
        {
            var list = FindGuides(network, p, planned, plannedRows, snapRadius);
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
