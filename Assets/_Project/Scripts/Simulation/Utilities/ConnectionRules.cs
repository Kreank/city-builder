using System;
using System.Numerics;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Anschlussregeln der Versorgungsnetze – wiederverwendbarer Baustein (Entscheidung des Spielers: Netze werden selbst
    /// verlegt, Straßen führen nichts mehr mit). <b>Stand:</b> gilt bisher nur für Abwasser; Wasser und Strom werden
    /// später auf dasselbe Prinzip umgestellt (dann <see cref="CarriedByRoads"/> anpassen, Strom mit Hochspannung →
    /// Umspannwerk → Ortsnetz).
    /// <list type="bullet">
    /// <item>Ein Gebäude ist angeschlossen, wenn eine Leitung seiner Art höchstens <see cref="BuildingReach"/> von seiner
    /// Zufahrt (<see cref="Building.Entrance"/>, Mitte der Vorderseite) entfernt verläuft – egal, ob mittendrin oder am Ende.</item>
    /// <item>Hausanschluss (Darstellung): gerade Linie von der Zufahrt zum nächsten Punkt der Leitung
    /// (<see cref="HouseConnection"/>).</item>
    /// <item>Bauwerke und Leitungen verbinden sich wie bisher über Leitungsenden (<see cref="UtilitySystem.ConnectRadius"/>).</item>
    /// </list>
    /// </summary>
    public static class ConnectionRules
    {
        /// <summary>Reichweite des Hausanschlusses (Zufahrt ↔ Leitung) in m.</summary>
        public const float BuildingReach = 12f;

        /// <summary>Führen Stadtstraßen diese Netzart automatisch mit? (Abwasser: nein – Kanäle verlegt der Spieler.)</summary>
        public static bool CarriedByRoads(UtilityKind kind) => kind != UtilityKind.Sewage;

        /// <summary>Nächster Punkt einer geraden Leitung zu p.</summary>
        public static Vector2 ClosestPoint(UtilityLine line, Vector2 p)
        {
            var ab = line.B - line.A;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - line.A, ab) / len2, 0f, 1f) : 0f;
            return line.A + ab * t;
        }

        /// <summary>Liegt die Leitung in Anschlussreichweite der Zufahrt des Gebäudes?</summary>
        public static bool Reaches(UtilityLine line, Building b) =>
            line != null && b != null && line.DistanceTo(b.Entrance) <= BuildingReach;

        /// <summary>Hausanschluss für die Darstellung: von der Zufahrt zum nächsten Punkt der Leitung (false = außer Reichweite).</summary>
        public static bool HouseConnection(UtilityLine line, Building b, out Vector2 from, out Vector2 to)
        {
            from = b?.Entrance ?? default;
            to = from;
            if (!Reaches(line, b)) return false;
            to = ClosestPoint(line, from);
            return true;
        }
    }
}
