using System;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Anschlussregeln der Versorgungsnetze – gemeinsamer Baustein für Strom, Wasser und Abwasser (Entscheidung des
    /// Spielers: Netze werden selbst verlegt, Straßen führen nichts mehr mit).
    /// <list type="bullet">
    /// <item>Ein Gebäude ist angeschlossen, wenn eine Leitung seiner Art, die Gebäude anschließt
    /// (<see cref="UtilityLineType.ConnectsBuildings"/>: Rohr, Kanal, Ortsnetz – nicht Hochspannung), höchstens
    /// <see cref="BuildingReach"/> von seiner Zufahrt (<see cref="Building.Entrance"/>, Mitte der Vorderseite) entfernt
    /// verläuft – egal, ob mittendrin oder am Ende.</item>
    /// <item>Hausanschluss (Darstellung): gerade Linie von der Zufahrt zum nächsten Punkt der Leitung
    /// (<see cref="HouseConnection"/>); bei der Freileitung ein Seil vom nächsten Mast.</item>
    /// <item>Bauwerke und Leitungen verbinden sich über Leitungsenden (<see cref="UtilitySystem.ConnectRadius"/>), Leitungen
    /// nur mit Leitungen derselben Ebene (<see cref="GridLevel"/>); Hochspannung und Ortsnetz treffen sich im Umspannwerk.</item>
    /// <item>Rohre, Kanäle und Erdkabel rasten unter dem Gehweg ein (<see cref="SidewalkInset"/>), Freileitungen auf der
    /// Straßenmitte (Masten stehen dann neben der Straße, ein Strang versorgt beide Seiten).</item>
    /// </list>
    /// </summary>
    public static class ConnectionRules
    {
        /// <summary>Reichweite des Hausanschlusses (Zufahrt ↔ Leitung) in m.</summary>
        public const float BuildingReach = 12f;

        /// <summary>Unterirdische Leitungen liegen so weit innerhalb des Straßenrands (unter dem Gehweg, nahe der Bordsteinkante).</summary>
        public const float SidewalkInset = 2.5f;

        /// <summary>
        /// Führen Stadtstraßen diese Netzart automatisch mit? Seit „Netze werden selbst verlegt“ für keine Art mehr
        /// (bis dahin: Strom und Wasser ja, Abwasser nein). Bleibt als Schalter für einen späteren Sandbox-Modus.
        /// </summary>
        public static bool CarriedByRoads(UtilityKind kind) => false;

        /// <summary>Nächster Punkt einer geraden Leitung zu p.</summary>
        public static Vector2 ClosestPoint(UtilityLine line, Vector2 p)
        {
            var ab = line.B - line.A;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - line.A, ab) / len2, 0f, 1f) : 0f;
            return line.A + ab * t;
        }

        /// <summary>Liegt die Leitung in Anschlussreichweite der Zufahrt des Gebäudes (und schließt sie Gebäude an)?</summary>
        public static bool Reaches(UtilityLine line, Building b) =>
            line != null && b != null && line.Type.ConnectsBuildings && line.DistanceTo(b.Entrance) <= BuildingReach;

        /// <summary>Hausanschluss für die Darstellung: von der Zufahrt zum nächsten Punkt der Leitung (false = außer Reichweite).</summary>
        public static bool HouseConnection(UtilityLine line, Building b, out Vector2 from, out Vector2 to)
        {
            from = b?.Entrance ?? default;
            to = from;
            if (!Reaches(line, b)) return false;
            to = ClosestPoint(line, from);
            return true;
        }

        /// <summary>
        /// Freileitung: Seil vom nächsten Mast zur Zufahrt (false = keine Freileitung, keine Masten oder außer Reichweite).
        /// </summary>
        public static bool OverheadDrop(UtilityLine line, Building b, out Vector2 pole, out Vector2 house)
        {
            pole = default;
            house = b?.Entrance ?? default;
            if (!Reaches(line, b) || !line.Type.HasPoles || line.Poles.Count == 0) return false;
            float best = float.MaxValue;
            foreach (var q in line.Poles)
            {
                float d = Vector2.DistanceSquared(q, house);
                if (d < best) { best = d; pole = q; }
            }
            return true;
        }
    }
}
