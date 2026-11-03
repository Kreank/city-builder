using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>
    /// Geplante Reihe gleichartiger Gebäude entlang einer Straße (Anno-Stil, LMB halten und ziehen). Ungültige Plätze stehen
    /// mit Fehler in <see cref="Items"/> (Vorschau rot) und werden beim Bauen ausgelassen.
    /// </summary>
    public sealed class HouseRowPlan
    {
        public HouseType Type;
        /// <summary>Alle Plätze der Reihe in Zugrichtung (gültige und ungültige).</summary>
        public readonly List<HousePlan> Items = new List<HousePlan>();
        /// <summary>Summe der Baukosten aller gültigen Plätze (wird beim Bauen genau einmal gebucht).</summary>
        public float TotalCost;
        public int ValidCount;
        /// <summary>An einer Straße ausgerichtet (sonst nur ein ungültiger Platz am Cursor).</summary>
        public bool Aligned;
        /// <summary>Segmente, denen die Reihe folgt (Start zuerst).</summary>
        public readonly List<int> SegmentIds = new List<int>();
        /// <summary>Weg entlang der Vorderkante (m) vom ersten zum letzten Gebäudemittelpunkt.</summary>
        public float Length;
        public bool IsValid => ValidCount > 0;

        public IEnumerable<HousePlan> ValidItems
        {
            get { foreach (var p in Items) if (p.IsValid) yield return p; }
        }
    }

    /// <summary>
    /// Setzen statt Zonen: Reihe ziehen. <see cref="PlanHouseRow"/> folgt der Straße am Startpunkt (auch Kurven und
    /// Fortsetzungen über Knoten mit genau zwei Straßen gleichen Typs), Vorderseite zur Straße, Seite = Seite des Startpunkts,
    /// Abstand so klein wie möglich ohne Überlappung (gerade: genau eine Gebäudebreite + <see cref="RowGap"/>; Kurve außen:
    /// Vorderecken stoßen aneinander, innen: hintere Ecken). Jeder Platz durchläuft die normalen Platzregeln
    /// (<see cref="PlanHouseAt"/>); die Kasse wird kumuliert geprüft. <see cref="BuildHouseRow"/> bucht die Summe einmal.
    /// Rein und deterministisch (keine Zufallszahlen beim Planen).
    /// </summary>
    public sealed partial class CitySystem
    {
        /// <summary>Höchstens so viele Gebäude je Reihe.</summary>
        public const int MaxRowLength = 40;
        /// <summary>Luft zwischen zwei Nachbarn der Reihe (m) – so meldet die Rechteckprüfung keine Berührung als Überlappung.</summary>
        public const float RowGap = 0.05f;

        struct RowSample
        {
            public float S;          // Bogenlänge entlang der Vorderkanten-Linie
            public Vector2 Front;    // Punkt auf der Vorderkanten-Linie (Gehweg + SnapGap)
            public Vector2 Out;      // Normale von der Straße weg (zur Gebäudeseite)
        }

        /// <summary>
        /// Reihe von <paramref name="start"/> (Mausdruck) bis <paramref name="end"/> (aktuelle Maus) planen. Ohne Stadtstraße
        /// in der Nähe: ein ungültiger Platz am Start (wie <see cref="PlanHouse"/>).
        /// </summary>
        public HouseRowPlan PlanHouseRow(HouseType type, Vector2 start, Vector2 end, int maxCount = MaxRowLength)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            maxCount = Math.Clamp(maxCount, 1, MaxRowLength);
            var row = new HouseRowPlan { Type = type };
            if (!NearestCityRoad(start, type.Depth + HouseAlignReach, out var seg, out float t0))
            {
                Add(row, PlanHouse(type, start));
                return row;
            }
            row.Aligned = true;

            var tan = seg.Tangent(t0);
            var left = new Vector2(-tan.Y, tan.X);
            var q = seg.Evaluate(t0);
            // Seite wie RoadFront.TryAlign: Seite des Cursors, auf der Mittellinie rechts
            int side = Vector2.Dot(start - q, left) > 0f ? 1 : -1;
            var drag = end - start;
            int dir = Vector2.Dot(drag, tan) >= 0f ? 1 : -1;
            bool dragged = drag.LengthSquared() > 1f;

            float maxLength = maxCount * type.Width * 1.6f + type.Width;
            var path = TraceRowPath(seg, t0, dir, side * dir, maxLength, row.SegmentIds);
            if (path.Count < 2) { Add(row, PlanHouse(type, start)); return row; }

            // Wie weit die Maus entlang der Reihe gezogen hat (nächster Punkt der Vorderkanten-Linie)
            float sEnd = 0f;
            if (dragged)
            {
                float best = float.MaxValue;
                foreach (var p in path)
                {
                    float d = Vector2.DistanceSquared(p.Front, end);
                    if (d < best) { best = d; sEnd = p.S; }
                }
            }

            var pathSegments = new List<RoadSegment>();
            foreach (int id in row.SegmentIds) { var s = network.GetSegment(id); if (s != null) pathSegments.Add(s); }

            float sCur = 0f;
            var (c0, f0) = RowPose(type, path, sCur, pathSegments);
            AddChecked(row, PlanHouseAt(type, c0, f0));
            var prevC = c0;
            var prevF = f0;
            float pathEnd = path[path.Count - 1].S;
            while (row.Items.Count < maxCount)
            {
                // kleinstes s hinter dem Vorgänger ohne Überlappung (Rechtecke um RowGap vergrößert)
                float lo = sCur + type.Width * 0.3f, hi = sCur + type.Width * 3f;
                if (lo > pathEnd) break;
                hi = MathF.Min(hi, pathEnd);
                if (Overlaps(type, path, hi, pathSegments, prevC, prevF)) break; // zu enge Kurve
                for (int i = 0; i < 22; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Overlaps(type, path, mid, pathSegments, prevC, prevF)) lo = mid; else hi = mid;
                }
                float sNext = hi;
                if (sNext > sEnd + type.Width * 0.1f) break; // Maus noch nicht so weit gezogen
                var (c, f) = RowPose(type, path, sNext, pathSegments);
                AddChecked(row, PlanHouseAt(type, c, f));
                sCur = sNext;
                prevC = c;
                prevF = f;
            }
            row.Length = sCur;
            return row;
        }

        /// <summary>Platz zur Reihe hinzufügen und die Kasse kumuliert prüfen.</summary>
        void AddChecked(HouseRowPlan row, HousePlan plan)
        {
            if (plan.IsValid && Budget != null && !Budget.CanAfford(row.TotalCost + plan.Cost)) plan.Error = HouseBuildError.InsufficientFunds;
            Add(row, plan);
        }

        static void Add(HouseRowPlan row, HousePlan plan)
        {
            row.Items.Add(plan);
            if (!plan.IsValid) return;
            row.ValidCount++;
            row.TotalCost += plan.Cost;
        }

        /// <summary>
        /// Baut alle gültigen Plätze der Reihe (vorher erneut geprüft) und bucht die Summe <b>genau einmal</b>
        /// (Kategorie <see cref="Economy.BudgetCategory.HouseConstruction"/>). Reicht das Geld nicht für alle, wird nichts gebaut.
        /// </summary>
        public List<Building> BuildHouseRow(HouseRowPlan row)
        {
            var built = new List<Building>();
            if (row == null) return built;
            var plans = new List<HousePlan>();
            float total = 0f;
            foreach (var p in row.Items)
            {
                if (!p.IsValid) continue;
                var fresh = PlanHouseAt(p.Type, p.Position, p.Front);
                if (fresh.Error != HouseBuildError.None && fresh.Error != HouseBuildError.InsufficientFunds) continue;
                fresh.Error = HouseBuildError.None; // Geld wird unten für die ganze Reihe geprüft
                plans.Add(fresh);
                total += fresh.Cost;
            }
            if (plans.Count == 0) return built;
            if (Budget != null && !Budget.CanAfford(total)) return built;
            Budget?.Spend(Economy.BudgetCategory.HouseConstruction, total);
            foreach (var p in plans) built.Add(PlaceBuilding(p));
            return built;
        }

        bool Overlaps(HouseType type, List<RowSample> path, float s, List<RoadSegment> roads, Vector2 prevC, Vector2 prevF)
        {
            var (c, f) = RowPose(type, path, s, roads);
            float pad = RowGap + 0.01f; // RectsOverlap duldet 0,01 m Berührung → so ist der Abstand genau Breite + RowGap
            return ServiceGeometry.RectsOverlap(prevC, prevF, type.Width + pad, type.Depth + pad, c, f, type.Width + pad, type.Depth + pad);
        }

        /// <summary>
        /// Lage eines Gebäudes bei Bogenlänge <paramref name="s"/>: Vorderkante auf der Linie, Vorderseite zur Straße. Auf der
        /// Innenseite enger Kurven ragen die Vorderecken in den Gehweg → so weit (0,5-m-Schritte, max. 3 m) abrücken, bis frei.
        /// </summary>
        (Vector2 center, Vector2 front) RowPose(HouseType type, List<RowSample> path, float s, List<RoadSegment> roads)
        {
            int i = 1;
            while (i < path.Count - 1 && path[i].S < s) i++;
            var a = path[i - 1];
            var b = path[i];
            float span = b.S - a.S;
            float u = span > 1e-5f ? Math.Clamp((s - a.S) / span, 0f, 1f) : 0f;
            var p = Vector2.Lerp(a.Front, b.Front, u);
            var n = Vector2.Lerp(a.Out, b.Out, u);
            n = n.LengthSquared() > 1e-8f ? Vector2.Normalize(n) : a.Out;
            var front = -n;
            var c0 = p + n * (type.Depth * 0.5f);
            for (int k = 0; k <= 6; k++)
            {
                var c = c0 + n * (0.5f * k);
                bool hit = false;
                foreach (var r in roads)
                    if (ServiceGeometry.RoadCrossesRect(r, c, front, type.Width, type.Depth, 0f)) { hit = true; break; }
                if (!hit) return (c, front);
            }
            return (c0, front);
        }

        /// <summary>
        /// Linie der Vorderkanten entlang der Straße: ab <paramref name="t0"/> in Richtung <paramref name="dir"/>, Seite
        /// <paramref name="walkSide"/> (+1 = links der Laufrichtung). Läuft über Knoten mit genau zwei Segmenten gleichen Typs
        /// (Knick/Bogen) weiter, endet an Kreuzungen, Typwechseln, Brücken/Tunneln oder nach <paramref name="maxLength"/>.
        /// </summary>
        List<RowSample> TraceRowPath(RoadSegment seg, float t0, int dir, int walkSide, float maxLength, List<int> segmentIds)
        {
            var path = new List<RowSample>();
            var visited = new HashSet<int>();
            float s = 0f;
            Vector2? last = null;
            float t = t0;
            var type = seg.Type;
            while (seg != null && visited.Add(seg.Id))
            {
                segmentIds.Add(seg.Id);
                float hw = seg.Type.HalfWidth + RoadFront.SnapGap;
                float step = 1f / MathF.Max(1f, seg.Length); // ~1 m entlang der Achse
                while (true)
                {
                    if (MathF.Abs(seg.ElevationAt(t)) > RoadFront.AtGradeElevation) return path;
                    var walk = seg.Tangent(t) * dir;
                    var outward = new Vector2(-walk.Y, walk.X) * walkSide;
                    var f = seg.Evaluate(t) + outward * hw;
                    if (last.HasValue) s += Vector2.Distance(last.Value, f);
                    path.Add(new RowSample { S = s, Front = f, Out = outward });
                    last = f;
                    if (s >= maxLength) return path;
                    if (dir > 0 ? t >= 1f : t <= 0f) break;
                    t = Math.Clamp(t + step * dir, 0f, 1f);
                }
                // Fortsetzung über einen Knoten mit genau zwei Straßen gleichen Typs
                var node = dir > 0 ? seg.End : seg.Start;
                if (node.SegmentIds.Count != 2) break;
                int nextId = node.SegmentIds[0] == seg.Id ? node.SegmentIds[1] : node.SegmentIds[0];
                var next = network.GetSegment(nextId);
                if (next == null || next.Type != type) break;
                dir = next.Start == node ? 1 : -1;
                t = dir > 0 ? 0f : 1f;
                // Laufrichtung und Seite relativ zur Laufrichtung bleiben → walkSide unverändert. Der erste Punkt des neuen
                // Segments liegt auf dem Knoten (schon in der Linie) → überspringen.
                seg = next;
                t = Math.Clamp(t + dir / MathF.Max(1f, next.Length), 0f, 1f);
            }
            return path;
        }

        /// <summary>Nächste ebenerdige Stadtstraße (Rand ≤ <paramref name="searchRadius"/>) wie <see cref="RoadFront.TryAlign"/>.</summary>
        bool NearestCityRoad(Vector2 cursor, float searchRadius, out RoadSegment best, out float bestT)
        {
            best = null;
            bestT = 0f;
            float bestD = searchRadius;
            foreach (var s in network.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float margin = s.Type.HalfWidth + searchRadius;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (cursor.X < lo.X - margin || cursor.Y < lo.Y - margin || cursor.X > hi.X + margin || cursor.Y > hi.Y + margin) continue;
                float u = s.ClosestT(cursor);
                if (MathF.Abs(s.ElevationAt(u)) > RoadFront.AtGradeElevation) continue;
                float d = Vector2.Distance(s.Evaluate(u), cursor) - s.Type.HalfWidth;
                if (d < bestD - 1e-4f || (MathF.Abs(d - bestD) <= 1e-4f && best != null && s.Id < best.Id))
                {
                    bestD = d;
                    best = s;
                    bestT = u;
                }
            }
            return best != null;
        }
    }
}
