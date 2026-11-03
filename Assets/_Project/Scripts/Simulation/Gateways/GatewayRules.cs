using System;
using System.Numerics;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>Alle Zahlen der Außenanbindungen an einer Stelle (Platzhalter fürs Balancing).</summary>
    public static class GatewayRules
    {
        /// <summary>Enden der vorgebauten Autobahn liegen so weit innerhalb des Kartenrands (Außenanschluss = ≤ 40 m).</summary>
        public const float EdgeInset = 6f;
        /// <summary>Teilstücke der Autobahn zwischen Anschlussstelle und Kartenrand: Ziel-/Höchstlänge (m).</summary>
        public const float ChunkLength = 260f, MaxChunkLength = 900f;
        /// <summary>Gradiente der Trasse (Höhenlinie für die Stückgrenzen) – etwas flacher als die erlaubte Steigung 6 %.</summary>
        public const float GradeSlope = 0.045f;
        /// <summary>Glättung des Geländes für die Gradiente (Halbbreite in m).</summary>
        public const float GradeSmoothing = 120f;
        /// <summary>Bevorzugter Abstand der Anschlussstelle von der Kartenmitte (seitlich der Autobahn, Mitte bleibt für die Stadt).</summary>
        public const float PreferredInterchangeOffset = 300f;

        /// <summary>Kapazität einer Autobahn-Richtungsfahrbahn je Spur (Fz/h).</summary>
        public const float LaneCapacityPerHour = 1800f;

        /// <summary>Bahnkorridor: halbe Breite (zweigleisig + Böschung), in der nichts wächst.</summary>
        public const float RailCorridorHalfWidth = 16f;
        /// <summary>Mindestabstand des Bahnkorridors von der Anschlussstelle (m).</summary>
        public const float RailClearanceFromInterchange = 480f;

        /// <summary>Schiffbar ab dieser Breite/Tiefe (m).</summary>
        public const float NavigableWidth = 40f, NavigableDepth = 2.5f;

        /// <summary>
        /// Durchgangsverkehr: Gewicht einer Autobahn-Einfahrt = Grundwert + Faktor × Wichtigkeit der Ziele (begrenzt).
        /// Gewicht 1 = Spielerstraße am Kartenrand ≈ 0,02 Fahrzeuge je Verkehrstick ≈ 290 Fz/h bei Verkehr 1.
        /// </summary>
        public const float ThroughBase = 1f, ThroughPerImportance = 0.4f, ThroughMax = 5f;

        /// <summary>
        /// Verkehrszählung am Anschluss: alle so viele Verkehrsschritte (2 s Verkehrszeit; ein Autobahn-Fahrzeug braucht für das
        /// erste/letzte Stück ≥ 5 s, wird also sicher gesehen).
        /// </summary>
        public const int CountEverySteps = (int)(2f / Core.TimeModel.TrafficStepSeconds);

        /// <summary>Himmelsrichtung eines Vektors (x = Ost, y = Nord).</summary>
        public static string Compass(Vector2 d)
        {
            if (d.LengthSquared() < 1e-8f) return "–";
            float deg = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI; // 0 = Ost, 90 = Nord
            string[] names = { "Ost", "Nordost", "Nord", "Nordwest", "West", "Südwest", "Süd", "Südost" };
            int i = (int)MathF.Round(((deg % 360f) + 360f) % 360f / 45f) % 8;
            return names[i];
        }

        /// <summary>Stabiler Hash (Namen, Nummern) ohne Zufallsstrom.</summary>
        public static uint Hash(int a, int b = 0) => Citizens.SimRandom.Hash(a, b);
    }
}
