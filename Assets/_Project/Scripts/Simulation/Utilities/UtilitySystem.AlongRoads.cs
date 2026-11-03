using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Vorschau für „Netz entlang Straßen verlegen“: geplante Leitungsstücke, Kosten, was nicht geht und wie viele Gebäude
    /// neu angeschlossen würden. Erst <see cref="UtilitySystem.BuildAlongRoads"/> baut.
    /// </summary>
    public sealed class AlongRoadsPlan
    {
        public UtilityLineType Type;
        /// <summary>Geplante Stücke (gültige und ungültige – ungültige werden nicht gebaut).</summary>
        public readonly List<LinePlan> Pieces = new List<LinePlan>();
        /// <summary>Berücksichtigte Stadtstraßen-Segmente (nach Id).</summary>
        public readonly List<int> SegmentIds = new List<int>();
        /// <summary>Übersprungen: dort liegt schon eine Leitung dieser Art und Ebene.</summary>
        public int Existing;
        /// <summary>Stücke, die nicht gehen (z. B. Freileitung: Gebäude auf dem Maststandort) – erster Grund in <see cref="FirstInvalid"/>.</summary>
        public int Invalid;
        public UtilityBuildError FirstInvalid;
        /// <summary>Kosten und Länge der gültigen Stücke.</summary>
        public float Cost;
        public float Length;
        /// <summary>Gebäude, die heute keinen Anschluss dieser Art haben und danach einen hätten.</summary>
        public int BuildingsReached;
        /// <summary>None, TooShort (nichts zu verlegen) oder InsufficientFunds (Stadtkasse reicht nicht für alles).</summary>
        public UtilityBuildError Error;

        public int ValidCount { get { int n = 0; foreach (var p in Pieces) if (p.IsValid) n++; return n; } }
        public bool IsValid => Error == UtilityBuildError.None && ValidCount > 0;
    }

    /// <summary>
    /// „Netz entlang Straßen verlegen“ (Netze selbst verlegt): verlegt die gewählte Leitungsart automatisch entlang
    /// ausgewählter oder aller Stadtstraßen – zu den normalen Kosten je Meter. Gedacht für bestehende Städte (nach dem Umbau
    /// wären sonst alle Häuser unversorgt) und fürs normale Bauen („entlang dieser Straße“).
    /// <list type="bullet">
    /// <item>Unterirdisch (Rohr, Kanal, Erdkabel): unter dem Gehweg (<see cref="ConnectionRules.SidewalkInset"/>), auf der rechten
    /// Seite in Segmentrichtung; reicht ein Strang nicht bis zur Zufahrt gegenüber (breite Straßen), auf beiden Seiten.
    /// An den Knoten führt ein kurzes Stück zur Straßenmitte – dort treffen sich alle Stränge (verbunden).</item>
    /// <item>Oberirdisch (Freileitung, Hochspannung): auf der Straßenmitte, Masten rücken neben die Straße und brauchen Platz;
    /// stehen dort schon Gebäude, ist das Stück ungültig (kein Abriss) → Erdkabel nehmen.</item>
    /// <item>Gekrümmte Straßen werden in gerade Stücke zerlegt (Sehnenfehler ≤ <see cref="AlongRoadsChordError"/>).</item>
    /// <item>Stücke, an denen schon eine Leitung derselben Art und Ebene liegt, werden übersprungen (zweimal klicken kostet nichts).</item>
    /// </list>
    /// </summary>
    public sealed partial class UtilitySystem
    {
        /// <summary>Größter Abstand zwischen Straßenkurve und geradem Leitungsstück.</summary>
        public const float AlongRoadsChordError = 1.5f;
        /// <summary>Längstes Stück (unterirdisch; Freileitungen dürfen länger, damit sich an den Stößen keine Doppelmasten häufen).</summary>
        const float AlongPieceUnderground = 40f, AlongPieceOverhead = 120f;
        /// <summary>Ein Stück gilt als vorhanden, wenn Anfang, Mitte und Ende so nah an einer bestehenden Leitung liegen.</summary>
        const float AlongExistingTolerance = 1.5f;

        /// <summary>Vorschau: Leitungsart entlang einer Stadtstraße.</summary>
        public AlongRoadsPlan PlanAlongRoad(UtilityLineType type, int segmentId) => PlanAlongRoads(type, new[] { segmentId });

        /// <summary>Vorschau: Leitungsart entlang der Segmente (null = alle Stadtstraßen; Autobahnen/Rampen nie).</summary>
        public AlongRoadsPlan PlanAlongRoads(UtilityLineType type, IEnumerable<int> segmentIds = null)
        {
            var plan = new AlongRoadsPlan { Type = type };
            if (type == null) { plan.Error = UtilityBuildError.TooShort; return plan; }
            var segs = new List<RoadSegment>();
            if (segmentIds == null)
            {
                foreach (var s in roads.Segments) if (CarriesUtilities(s)) segs.Add(s);
            }
            else
            {
                var seen = new HashSet<int>();
                foreach (int id in segmentIds)
                {
                    var s = roads.GetSegment(id);
                    if (s != null && CarriesUtilities(s) && seen.Add(id)) segs.Add(s);
                }
            }
            segs.Sort((a, b) => a.Id.CompareTo(b.Id));

            foreach (var s in segs)
            {
                plan.SegmentIds.Add(s.Id);
                foreach (float offset in AlongOffsets(type, s))
                    foreach (var (a, b) in AlongPath(s, offset, type.Underground ? AlongPieceUnderground : AlongPieceOverhead))
                    {
                        if (AlreadyLaid(type, a, b)) { plan.Existing++; continue; }
                        var piece = PlanLineCore(type, a, b, checkFunds: false);
                        plan.Pieces.Add(piece);
                        if (piece.IsValid) { plan.Cost += piece.Cost; plan.Length += piece.Length; }
                        else
                        {
                            if (plan.Invalid == 0) plan.FirstInvalid = piece.Error;
                            plan.Invalid++;
                        }
                    }
            }

            if (type.ConnectsBuildings && plan.Pieces.Count > 0)
                foreach (var b in city.Buildings)
                {
                    if (ConnectedLine(b, type.Kind) != null) continue;
                    foreach (var p in plan.Pieces)
                        if (p.IsValid && UtilityGeometry.DistanceToSegment(b.Entrance, p.A, p.B) <= ConnectionRules.BuildingReach)
                        { plan.BuildingsReached++; break; }
                }

            if (plan.ValidCount == 0) plan.Error = UtilityBuildError.TooShort;
            else if (!Affordable(plan.Cost)) plan.Error = UtilityBuildError.InsufficientFunds;
            return plan;
        }

        /// <summary>
        /// Baut alle gültigen Stücke der Vorschau (jedes wird noch einmal geprüft; was inzwischen nicht mehr geht, entfällt).
        /// Bucht die Kosten wie einzeln verlegte Leitungen. Liefert die Zahl gebauter Leitungen (0 = nichts/zu wenig Geld).
        /// </summary>
        public int BuildAlongRoads(AlongRoadsPlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return 0;
            int built = 0;
            foreach (var p in plan.Pieces)
            {
                if (!p.IsValid || AlreadyLaid(plan.Type, p.A, p.B)) continue;
                if (BuildLine(PlanLine(plan.Type, p.A, p.B)) != null) built++;
            }
            return built;
        }

        /// <summary>Seitenabstände der Stränge: oberirdisch die Mitte, unterirdisch rechts (bzw. beide Seiten bei breiten Straßen).</summary>
        static float[] AlongOffsets(UtilityLineType type, RoadSegment s)
        {
            if (!type.Underground) return new[] { 0f };
            float offset = SidewalkOffset(s);
            float far = s.Type.HalfWidth + offset; // Zufahrt gegenüber liegt am anderen Straßenrand
            return far <= ConnectionRules.BuildingReach - 0.5f ? new[] { offset } : new[] { offset, -offset };
        }

        /// <summary>
        /// Gerade Stücke entlang eines Segments. offset 0: auf der Mitte von Knoten zu Knoten. Sonst: Knoten → seitlich unter dem
        /// Gehweg → … → Knoten (kurze Anschlussstücke an den Enden, damit sich alle Stränge in der Knotenmitte treffen).
        /// </summary>
        static List<(Vector2, Vector2)> AlongPath(RoadSegment s, float offset, float maxPiece)
        {
            var result = new List<(Vector2, Vector2)>();
            var start = s.Evaluate(0f);
            var end = s.Evaluate(1f);
            float length = s.Length;
            Vector2 Center(float u) => s.Evaluate(u);
            Vector2 Side(float u) => s.Evaluate(u) + RoadNormal(s, u) * offset;

            var points = new List<Vector2>();
            float lead = MathF.Max(s.Type.HalfWidth, 6f);
            if (MathF.Abs(offset) < 1e-3f || length < 2f * lead + MinLineLength)
                Subdivide(Center, 0f, 1f, maxPiece, points);
            else
            {
                points.Add(start);
                float u0 = lead / length, u1 = 1f - lead / length;
                Subdivide(Side, u0, u1, maxPiece, points);
                points.Add(end);
            }
            for (int i = 1; i < points.Count; i++)
                if (Vector2.Distance(points[i - 1], points[i]) >= MinLineLength) result.Add((points[i - 1], points[i]));
            return result;
        }

        /// <summary>Kurve zwischen u0 und u1 in gleich lange Sehnen zerlegen (Anzahl so, dass der Sehnenfehler klein bleibt).</summary>
        static void Subdivide(Func<float, Vector2> curve, float u0, float u1, float maxPiece, List<Vector2> points)
        {
            float length = 0f;
            var prev = curve(u0);
            for (int i = 1; i <= 16; i++)
            {
                var q = curve(u0 + (u1 - u0) * i / 16f);
                length += Vector2.Distance(prev, q);
                prev = q;
            }
            int n = Math.Max(1, (int)MathF.Ceiling(length / maxPiece));
            for (; n < 32; n++)
            {
                bool ok = true;
                for (int i = 0; i < n && ok; i++)
                {
                    float a = u0 + (u1 - u0) * i / n, b = u0 + (u1 - u0) * (i + 1) / n;
                    var mid = (curve(a) + curve(b)) * 0.5f;
                    if (Vector2.Distance(mid, curve((a + b) * 0.5f)) > AlongRoadsChordError) ok = false;
                }
                if (ok) break;
            }
            for (int i = 0; i <= n; i++) points.Add(curve(u0 + (u1 - u0) * i / n));
        }

        /// <summary>Liegt dort schon eine Leitung derselben Art und Ebene (Anfang, Mitte, Ende nah dran)?</summary>
        bool AlreadyLaid(UtilityLineType type, Vector2 a, Vector2 b)
        {
            var mid = (a + b) * 0.5f;
            foreach (var l in lines.Values)
            {
                if (l.Kind != type.Kind || l.Level != type.Level) continue;
                if (l.DistanceTo(a) <= AlongExistingTolerance && l.DistanceTo(b) <= AlongExistingTolerance &&
                    l.DistanceTo(mid) <= AlongExistingTolerance) return true;
            }
            return false;
        }
    }
}
