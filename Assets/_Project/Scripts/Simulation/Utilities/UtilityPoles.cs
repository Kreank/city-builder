using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Strommasten als Simulationsobjekte: Standorte werden hier berechnet (nicht erst in der Darstellung), damit
    /// Regeln gelten. Ein Mast (Grundfläche <see cref="PoleSize"/>² plus <see cref="PoleMargin"/> Rand) steht nie
    /// auf Fahrbahn/Gehweg (<see cref="RoadType.HalfWidth"/>, außer über überdeckten Tunneln), Wasser, Bauwerken
    /// oder Gebäuden. Leitungen dürfen Straßen und schmale Gewässer überspannen (Spannweite ≤ <see cref="MaxPoleSpan"/>).
    /// <para>
    /// Verfahren: Endmasten so nah wie möglich an den Leitungsenden (Enden selbst dürfen auf Straße/Bauwerk liegen –
    /// dort ist der Anschluss), dazwischen gleichmäßig verteilte Masten; ein blockierter Standort wird entlang der
    /// Leitung verschoben (±1 m-Schritte). Läuft die Leitung längs einer Straße, wird der Mast seitlich neben den
    /// Straßenrand gesetzt (<see cref="PoleRoadGap"/>) – er verbraucht dort Platz (Grundstücke über
    /// <c>CitySystem.LotBlocker</c> gesperrt). Findet sich kein Standort, ist der Plan ungültig.
    /// </para>
    /// <para>
    /// Mittelstreifen: Es gibt im <see cref="RoadCatalog"/> noch keinen Straßentyp mit Mittelstreifen
    /// (<c>LaneKind.Median</c> wird nirgends benutzt) – Masten auf breiten Mittelinseln sind für später vorgesehen.
    /// </para>
    /// </summary>
    public sealed partial class UtilitySystem
    {
        /// <summary>Größte Spannweite zwischen zwei Masten (und vom Leitungsende zum ersten Mast).</summary>
        public const float MaxPoleSpan = 45f;
        /// <summary>Kantenlänge der quadratischen Mast-Grundfläche.</summary>
        public const float PoleSize = 2f;
        /// <summary>Freier Rand um die Grundfläche.</summary>
        public const float PoleMargin = 0.5f;
        /// <summary>Halbe Grundfläche + Rand: so weit muss alles Verbotene vom Mastmittelpunkt weg sein.</summary>
        public const float PoleFootprintHalf = PoleSize * 0.5f + PoleMargin;
        /// <summary>Abstand Mastfuß ↔ Straßenrand, wenn der Mast neben eine Straße gesetzt wird.</summary>
        public const float PoleRoadGap = 1f;
        const float PoleStep = 1f;
        const float MinPoleGap = 4f;
        /// <summary>Ab diesem |cos| zwischen Leitung und Straße gilt die Leitung als „längs der Straße“ (≈ 37°).</summary>
        const float AlongRoadCos = 0.8f;

        /// <summary>Wie oft Leitungen wegen Straßen-/Bauwerksbau neu trassiert wurden.</summary>
        public int ReroutedLines { get; private set; }
        /// <summary>Leitungen, die beim Straßen-/Bauwerksbau abgerissen wurden, weil keine Trasse mehr möglich war.</summary>
        public int LinesRemovedByConstruction { get; private set; }

        public event Action<UtilityLine> LineRemoved;

        /// <summary>Ein Mast (beliebiger Leitung) im Umkreis, sonst null.</summary>
        public Vector2? PoleNear(Vector2 p, float radius)
        {
            float r2 = radius * radius;
            foreach (var l in lines.Values)
                foreach (var q in l.Poles)
                    if (Vector2.DistanceSquared(p, q) <= r2) return q;
            return null;
        }

        /// <summary>Darf an dieser Stelle ein Mast stehen? (für Vorschau/Tests; None = ja)</summary>
        public UtilityBuildError CheckPole(Vector2 p) => CheckPole(p, out _, out _);

        UtilityBuildError CheckPole(Vector2 p, out RoadSegment road, out float roadT)
        {
            road = null;
            roadT = 0f;
            if (!InWorld(p)) return UtilityBuildError.OutOfWorld;

            float best = float.MaxValue;
            foreach (var s in roads.Segments)
            {
                float reach = s.Type.HalfWidth + PoleFootprintHalf;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) continue;
                float t = s.ClosestT(p);
                float d = Vector2.Distance(s.Evaluate(t), p) - s.Type.HalfWidth;
                if (d >= PoleFootprintHalf - 1e-3f || s.IsCoveredAt(t)) continue; // Tunnel liegt unter dem Mast
                if (d < best) { best = d; road = s; roadT = t; }
            }
            if (road != null) return UtilityBuildError.PoleOnRoad;

            float h = PoleSize * 0.5f;
            if (terrain.IsWater(p) || terrain.IsWater(p + new Vector2(h, h)) || terrain.IsWater(p + new Vector2(-h, h)) ||
                terrain.IsWater(p + new Vector2(h, -h)) || terrain.IsWater(p + new Vector2(-h, -h)))
                return UtilityBuildError.PoleOnWater;
            if (FacilityAt(p, PoleFootprintHalf) != null) return UtilityBuildError.PoleOnFacility;
            float m = PoleFootprintHalf;
            if (city.BuildingAt(p) != null || city.BuildingAt(p + new Vector2(m, m)) != null || city.BuildingAt(p + new Vector2(-m, m)) != null ||
                city.BuildingAt(p + new Vector2(m, -m)) != null || city.BuildingAt(p + new Vector2(-m, -m)) != null)
                return UtilityBuildError.PoleOnBuilding;
            return UtilityBuildError.None;
        }

        /// <summary>Berührt diese Straße die Mast-Grundfläche bei <paramref name="p"/>?</summary>
        static bool RoadBlocksPole(RoadSegment s, Vector2 p)
        {
            float reach = s.Type.HalfWidth + PoleFootprintHalf;
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
            if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) return false;
            float t = s.ClosestT(p);
            return Vector2.Distance(s.Evaluate(t), p) - s.Type.HalfWidth < PoleFootprintHalf - 1e-3f && !s.IsCoveredAt(t);
        }

        /// <summary>
        /// Berechnet die Masten einer Stromleitung von <paramref name="a"/> nach <paramref name="b"/>.
        /// Liefert den ersten Fehler (None = alle Masten gültig); ungültige Masten stehen mit Fehler in der Liste.
        /// </summary>
        UtilityBuildError PlanPoles(Vector2 a, Vector2 b, List<PolePlan> poles)
        {
            poles.Clear();
            float length = Vector2.Distance(a, b);
            if (length < 1e-3f) return UtilityBuildError.TooShort;
            var dir = (b - a) / length;
            var right = new Vector2(dir.Y, -dir.X);

            // Standort bei Bogenlänge s – direkt auf der Leitung oder (längs einer Straße) seitlich daneben
            bool TryAt(float s, out Vector2 pos, out UtilityBuildError why)
            {
                pos = a + dir * s;
                why = CheckPole(pos, out var road, out float t);
                if (why == UtilityBuildError.None) return true;
                if (why != UtilityBuildError.PoleOnRoad || road == null) return false;
                var tangent = road.Tangent(t);
                if (MathF.Abs(Vector2.Dot(tangent, dir)) < AlongRoadCos) return false; // quert die Straße → verschieben
                var center = road.Evaluate(t);
                var normal = new Vector2(tangent.Y, -tangent.X);
                float offset = Vector2.Dot(pos - center, normal);
                // Seite: auf der die Leitung liegt; mittig auf der Straße → rechts in Leitungsrichtung (gleichbleibend)
                float preferred = MathF.Abs(offset) > 0.5f ? MathF.Sign(offset) : (Vector2.Dot(normal, right) >= 0f ? 1f : -1f);
                float distance = road.Type.HalfWidth + PoleRoadGap + PoleSize * 0.5f;
                foreach (float side in new[] { preferred, -preferred })
                {
                    var q = center + normal * (side * distance);
                    if (CheckPole(q) == UtilityBuildError.None) { pos = q; why = UtilityBuildError.None; return true; }
                }
                return false;
            }

            float maxSlide = MathF.Min(length, MaxPoleSpan);

            // Erster Mast: vom Anfang zur Mitte suchen
            Vector2 first = default;
            float firstS = -1f;
            var startWhy = UtilityBuildError.None;
            for (int i = 0; i * PoleStep <= maxSlide + 1e-3f; i++)
            {
                float s = MathF.Min(i * PoleStep, length);
                if (TryAt(s, out var pos, out var why)) { first = pos; firstS = s; break; }
                if (i == 0) startWhy = why;
            }
            if (firstS < 0f)
            {
                poles.Add(new PolePlan { Position = a, Error = startWhy });
                return startWhy;
            }
            poles.Add(new PolePlan { Position = first });

            // Letzter Mast: vom Ende rückwärts suchen
            Vector2 last = default;
            float lastS = -1f;
            float lowest = MathF.Max(firstS + MinPoleGap, length - MaxPoleSpan);
            for (int i = 0; length - i * PoleStep >= lowest - 1e-3f; i++)
            {
                float s = length - i * PoleStep;
                if (TryAt(s, out var pos, out _)) { last = pos; lastS = s; break; }
            }
            var target = lastS >= 0f ? last : b;
            float targetS = lastS >= 0f ? lastS : length;

            // Dazwischen: gleichmäßig verteilen, blockierte Standorte entlang der Leitung verschieben
            var error = UtilityBuildError.None;
            var prev = first;
            float prevS = firstS;
            int maxShift = (int)(MaxPoleSpan * 0.5f / PoleStep);
            for (int guard = 0; Vector2.Distance(prev, target) > MaxPoleSpan + 1e-3f && guard < 1000; guard++)
            {
                float remaining = targetS - prevS;
                int spans = Math.Max(2, (int)MathF.Ceiling(remaining / (MaxPoleSpan - 2f)));
                float ideal = prevS + remaining / spans;
                bool found = false;
                for (int k = 0; k <= maxShift && !found; k++)
                {
                    for (int sign = -1; sign <= 1 && !found; sign += 2)
                    {
                        if (k == 0 && sign > 0) continue;
                        float s = ideal + sign * k * PoleStep;
                        if (s <= prevS + PoleStep * 0.5f || s >= targetS - PoleStep * 0.5f) continue;
                        if (TryAt(s, out var pos, out _) && Vector2.Distance(prev, pos) <= MaxPoleSpan + 1e-3f)
                        {
                            poles.Add(new PolePlan { Position = pos });
                            prev = pos;
                            prevS = s;
                            found = true;
                        }
                    }
                }
                if (found) continue;
                // Kein Standort in Reichweite: Mast an der Idealstelle als ungültig zeigen und weiterplanen (Vorschau)
                var at = a + dir * ideal;
                var why = CheckPole(at);
                if (why == UtilityBuildError.None) why = UtilityBuildError.PoleOnRoad; // nur seitlich frei, aber zu weit
                poles.Add(new PolePlan { Position = at, Error = why });
                if (error == UtilityBuildError.None) error = why;
                prev = at;
                prevS = ideal;
            }
            if (lastS >= 0f) poles.Add(new PolePlan { Position = last });
            return error;
        }

        static Vector2[] PolePositions(List<PolePlan> plans)
        {
            var result = new Vector2[plans.Count];
            for (int i = 0; i < plans.Count; i++) result[i] = plans[i].Position;
            return result;
        }

        /// <summary>
        /// Leitungen mit einem Mast, auf den <paramref name="blocked"/> zutrifft, werden neu trassiert (Masten
        /// verschoben); ist keine gültige Trasse mehr möglich, wird die Leitung abgerissen.
        /// </summary>
        void RerouteLines(Func<Vector2, bool> blocked)
        {
            List<UtilityLine> hit = null;
            foreach (var l in lines.Values)
            {
                if (l.Kind != UtilityKind.Power) continue;
                foreach (var p in l.Poles)
                    if (blocked(p)) { (hit ?? (hit = new List<UtilityLine>())).Add(l); break; }
            }
            if (hit == null) return;
            var plans = new List<PolePlan>();
            foreach (var l in hit)
            {
                if (PlanPoles(l.A, l.B, plans) == UtilityBuildError.None)
                {
                    l.Poles = PolePositions(plans);
                    l.Revision++;
                    ReroutedLines++;
                }
                else
                {
                    lines.Remove(l.Id);
                    LinesRemovedByConstruction++;
                    LineRemoved?.Invoke(l);
                }
            }
            StructureVersion++;
        }
    }
}
