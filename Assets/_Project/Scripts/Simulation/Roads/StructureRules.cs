using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Grundfläche eines frei platzierten Bauwerks: gedrehtes Rechteck um <see cref="Center"/>.
    /// <see cref="Front"/> (Einheitsvektor) zeigt in Richtung der Tiefe zur <b>Vorderseite</b> (Zufahrt).
    /// Gemeinsamer Baustein für Straßensperre (<see cref="StructureBlock"/>) und Straßenausrichtung (<see cref="RoadFront"/>).
    /// </summary>
    public readonly struct Footprint
    {
        public readonly Vector2 Center;
        public readonly Vector2 Front;
        public readonly float Width;
        public readonly float Depth;

        public Footprint(Vector2 center, Vector2 front, float width, float depth)
        {
            Center = center;
            Front = front;
            Width = width;
            Depth = depth;
        }

        public Vector2 Side => new Vector2(-Front.Y, Front.X);
        /// <summary>Mitte der Vorderkante (Anschlusspunkt der Zufahrt).</summary>
        public Vector2 FrontMid => Center + Front * (Depth * 0.5f);
        public float Radius => MathF.Sqrt(Width * Width + Depth * Depth) * 0.5f;

        public bool Contains(Vector2 p, float margin = 0f)
        {
            var d = p - Center;
            return MathF.Abs(Vector2.Dot(d, Side)) <= Width * 0.5f + margin && MathF.Abs(Vector2.Dot(d, Front)) <= Depth * 0.5f + margin;
        }

        public Vector2[] Corners()
        {
            var s = Side * (Width * 0.5f);
            var f = Front * (Depth * 0.5f);
            return new[] { Center - s - f, Center + s - f, Center + s + f, Center - s + f };
        }

        /// <summary>Vorderseite aus einem Winkel (0° = +Y, im Uhrzeigersinn wie <c>PlanFacility</c>/<c>Plan</c>).</summary>
        public static Vector2 FrontFromDegrees(float degrees)
        {
            float a = degrees * MathF.PI / 180f;
            return new Vector2(MathF.Sin(a), MathF.Cos(a));
        }

        /// <summary>Winkel (Grad 0..360) einer Vorderseite – Umkehrung von <see cref="FrontFromDegrees"/>.</summary>
        public static float DegreesFromFront(Vector2 front)
        {
            float d = MathF.Atan2(front.X, front.Y) * 180f / MathF.PI;
            return d < 0f ? d + 360f : d;
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = -degrees * MathF.PI / 180f; // im Uhrzeigersinn wie FrontFromDegrees
            float c = MathF.Cos(a), s = MathF.Sin(a);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
    }

    /// <summary>
    /// Ein Bauwerk, das Straßen <b>nicht</b> überbauen dürfen (Dienste, Versorger, später Depots, Kläranlage …).
    /// Bauwerksarten melden sich mit einer Zeile an: <c>roads.RegisterStructures(() =&gt; meineBlocks)</c>.
    /// Zonen-Gebäude gehören nicht dazu – dort entfernt eine Straße Zone und Gebäude wie bisher.
    /// </summary>
    public sealed class StructureBlock
    {
        /// <summary>Art des Besitzers (z. B. „service“, „utility“) – mit <see cref="Id"/> eindeutig.</summary>
        public string Kind { get; }
        public int Id { get; }
        /// <summary>Anzeigename für die Fehlermeldung „Straße überschneidet …“.</summary>
        public string Name { get; }
        public Footprint Footprint { get; }
        /// <summary>Bodenhöhe (für die Hervorhebung in der Vorschau).</summary>
        public float Height { get; }

        public StructureBlock(string kind, int id, string name, Footprint footprint, float height)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Footprint = footprint;
            Height = height;
        }

        public override string ToString() => $"{Name} ({Kind} #{Id})";
    }

    /// <summary>
    /// Regeln für Bauwerke an der Straße: Zufahrt an der Vorderseite und automatische Ausrichtung zur Straße.
    /// Reine Geometrie über dem <see cref="RoadNetwork"/>, zustandslos – Dienste, Versorger (und künftig Depots) nutzen sie.
    /// </summary>
    public static class RoadFront
    {
        /// <summary>Abstand Vorderkante ↔ Gehweg-Außenkante beim Einrasten (klein, damit die Fahrbahnprüfung nicht anschlägt).</summary>
        public const float SnapGap = 0.5f;
        /// <summary>Die Straße muss „vor“ der Vorderseite liegen: Kegel von ±60° um die Vorderseite.</summary>
        public const float FrontConeCos = 0.5f;
        /// <summary>Elevation, ab der eine Straße Brücke/Tunnel ist (keine Zufahrt, kein Einrasten, keine Sperre).</summary>
        public const float AtGradeElevation = 3f;

        /// <summary>
        /// Zufahrt an der Vorderseite: nächste ebenerdige Stadtstraße, deren Rand höchstens <paramref name="reach"/> von der
        /// Mitte der Vorderkante entfernt ist und die vor der Vorderseite liegt (Kegel ±60°). Gleichstand → kleinste Segment-Id.
        /// </summary>
        public static bool FindAccess(RoadNetwork roads, Footprint fp, float reach, out int segmentId, out float t)
        {
            segmentId = -1;
            t = 0f;
            var p = fp.FrontMid;
            float best = reach + 1e-3f;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                float r = reach + s.Type.HalfWidth + 1f;
                if (p.X < lo.X - r || p.Y < lo.Y - r || p.X > hi.X + r || p.Y > hi.Y + r) continue;
                int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 1f));
                for (int i = 0; i <= samples; i++)
                {
                    float u = i / (float)samples;
                    if (MathF.Abs(s.ElevationAt(u)) > AtGradeElevation) continue;
                    var q = s.Evaluate(u);
                    var dq = q - p;
                    float len = dq.Length();
                    if (len > 1e-3f && Vector2.Dot(dq, fp.Front) < FrontConeCos * len) continue; // liegt nicht vor der Vorderseite
                    float d = MathF.Max(0f, len - s.Type.HalfWidth);
                    if (d < best - 1e-4f || (MathF.Abs(d - best) <= 1e-4f && segmentId >= 0 && s.Id < segmentId))
                    {
                        best = d;
                        segmentId = s.Id;
                        t = u;
                    }
                }
            }
            return segmentId >= 0;
        }

        /// <summary>
        /// Richtet ein Bauwerk zur nächsten ebenerdigen Stadtstraße aus: Vorderseite zeigt zur Straße (Normale der Tangente,
        /// auch in Kurven), die Vorderkante rastet am Gehweg ein (<see cref="SnapGap"/>), verschiebbar entlang der Straße.
        /// <paramref name="rotationOffset"/> dreht zusätzlich (Grad); das Bauwerk rückt dann so weit ab, dass es den Gehweg
        /// nicht berührt. Liefert false, wenn keine Stadtstraße im Umkreis <paramref name="searchRadius"/> (Rand) liegt.
        /// </summary>
        public static bool TryAlign(RoadNetwork roads, Vector2 cursor, float width, float depth, float rotationOffset, float searchRadius,
            out Vector2 center, out Vector2 front)
        {
            center = cursor;
            front = Vector2.UnitY;
            RoadSegment best = null;
            float bestD = searchRadius, bestT = 0f;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float margin = s.Type.HalfWidth + searchRadius;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (cursor.X < lo.X - margin || cursor.Y < lo.Y - margin || cursor.X > hi.X + margin || cursor.Y > hi.Y + margin) continue;
                float u = s.ClosestT(cursor);
                if (MathF.Abs(s.ElevationAt(u)) > AtGradeElevation) continue;
                float d = Vector2.Distance(s.Evaluate(u), cursor) - s.Type.HalfWidth;
                if (d < bestD - 1e-4f || (MathF.Abs(d - bestD) <= 1e-4f && best != null && s.Id < best.Id))
                {
                    bestD = d;
                    best = s;
                    bestT = u;
                }
            }
            if (best == null) return false;

            var q = best.Evaluate(bestT);
            var tan = best.Tangent(bestT);
            var normal = new Vector2(-tan.Y, tan.X);                     // links der Fahrtrichtung
            if (Vector2.Dot(cursor - q, normal) < 0f) normal = -normal;  // zur Seite des Cursors
            else if (Vector2.Dot(cursor - q, normal) == 0f) normal = new Vector2(tan.Y, -tan.X); // auf der Mittellinie: rechts
            front = Vector2.Normalize(Footprint.Rotate(-normal, rotationOffset));
            var side = new Vector2(-front.Y, front.X);
            // Abstand Mitte ↔ Straßenachse: wie die Fahrbahnprüfung der Bauwerke (Rechteck je Achse um die halbe Breite
            // erweitert) – bei gedrehten Bauwerken etwas mehr Luft an der Ecke, ungedreht = halbe Breite + Abstand + halbe Tiefe
            float hw = best.Type.HalfWidth;
            float extent = (depth * 0.5f + hw) * MathF.Abs(Vector2.Dot(front, normal)) + (width * 0.5f + hw) * MathF.Abs(Vector2.Dot(side, normal));
            center = q + normal * (SnapGap + extent);
            return true;
        }

        /// <summary>
        /// Erstes Bauwerk (entlang der Straße), dessen Grundfläche die ebenerdige Fahrbahn inkl. Gehweg (halbe Breite
        /// <paramref name="halfWidth"/>) berührt. Brücken/Tunnel (|Elevation| &gt; 3 m) gehen darüber/darunter hinweg.
        /// </summary>
        public static StructureBlock FindBlocking(RoadNetwork network, Vector2 p0, Vector2 control, Vector2 p2, float halfWidth,
            Func<float, float> heightAt)
        {
            if (!network.HasStructures) return null;
            Bezier.Bounds(p0, control, p2, out var lo, out var hi);
            List<StructureBlock> near = null;
            foreach (var b in network.Structures)
            {
                var c = b.Footprint.Center;
                float r = b.Footprint.Radius + halfWidth + 1f;
                if (c.X < lo.X - r || c.Y < lo.Y - r || c.X > hi.X + r || c.Y > hi.Y + r) continue;
                (near ?? (near = new List<StructureBlock>())).Add(b);
            }
            if (near == null) return null;
            // Deterministisch: bei Treffern an derselben Stelle gewinnt Art + kleinste Id
            near.Sort((a, b) => a.Kind != b.Kind ? string.CompareOrdinal(a.Kind, b.Kind) : a.Id.CompareTo(b.Id));
            float length = Bezier.ApproximateLength(p0, control, p2);
            int samples = Math.Max(2, (int)MathF.Ceiling(length / 1.5f));
            var ground = network.Ground;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                var p = Bezier.Evaluate(p0, control, p2, t);
                if (heightAt != null && MathF.Abs(heightAt(t) - ground.HeightAt(p)) > AtGradeElevation) continue;
                foreach (var b in near)
                    if (b.Footprint.Contains(p, halfWidth - BlockTolerance)) return b;
            }
            return null;
        }

        /// <summary>So viel darf eine Straße den Rand eines Bauwerks streifen, ohne dass es als Überschneidung zählt.</summary>
        public const float BlockTolerance = 0.25f;
    }
}
