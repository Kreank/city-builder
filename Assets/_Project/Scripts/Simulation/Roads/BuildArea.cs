using System;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Kacheln: Bauland der Stadt (gekaufte Kacheln). Gesetzt als <see cref="RoadNetwork.BuildArea"/> (Straßen- und Gleisnetz);
    /// alle Bau-Prüfungen fragen über <see cref="BuildArea"/> nach. null = überall baubar.
    /// </summary>
    public interface IBuildArea
    {
        /// <summary>Darf der Spieler an diesem Punkt bauen (Kachel gekauft)?</summary>
        bool IsBuildable(Vector2 p);
    }

    /// <summary>Kacheln: gemeinsame Prüfungen gegen <see cref="RoadNetwork.BuildArea"/> (Punkt, Kurve, gedrehtes Rechteck).</summary>
    public static class BuildArea
    {
        /// <summary>Hinweis für alle Werkzeuge, wenn ein Bau auf nicht gekauftes Land reicht.</summary>
        public const string NotOwnedText = "Kachel nicht gekauft – erst kaufen (Kacheln-Fenster, Knopf oben)";

        /// <summary>Höchster Abstand der Prüfpunkte (Kacheln sind ≥ 64 m groß und achsparallel).</summary>
        const float Spacing = 16f;

        public static bool Active(RoadNetwork net) => net?.BuildArea != null;

        public static bool Allows(RoadNetwork net, Vector2 p) => net?.BuildArea == null || net.BuildArea.IsBuildable(p);

        /// <summary>Quadratische Bézier-Kurve (Straße, Gleis) inkl. halber Breite beidseits.</summary>
        public static bool AllowsCurve(RoadNetwork net, Vector2 start, Vector2 control, Vector2 end, float halfWidth)
        {
            var area = net?.BuildArea;
            if (area == null) return true;
            float length = Bezier.ApproximateLength(start, control, end);
            int n = Math.Max(2, (int)MathF.Ceiling(length / Spacing));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Bezier.Evaluate(start, control, end, t);
                if (!area.IsBuildable(p)) return false;
                if (halfWidth <= 0f) continue;
                var d = Bezier.Derivative(start, control, end, t);
                if (d.LengthSquared() < 1e-8f) continue;
                d = Vector2.Normalize(d);
                var side = new Vector2(-d.Y, d.X) * halfWidth;
                if (!area.IsBuildable(p + side) || !area.IsBuildable(p - side)) return false;
            }
            return true;
        }

        /// <summary>Gerades Stück (Leitung, Wand) inkl. halber Breite.</summary>
        public static bool AllowsLine(RoadNetwork net, Vector2 a, Vector2 b, float halfWidth = 0f) =>
            AllowsCurve(net, a, (a + b) * 0.5f, b, halfWidth);

        /// <summary>
        /// Gedrehtes Rechteck: Mitte, Vorderseite (Richtung), Breite quer zur Vorderseite, Tiefe entlang. Prüft ein Raster von
        /// Punkten (≤ 16 m Abstand) inkl. Rand, damit auch ein L-förmiges Bauland richtig erkannt wird.
        /// </summary>
        public static bool AllowsRect(RoadNetwork net, Vector2 center, Vector2 front, float width, float depth)
        {
            var area = net?.BuildArea;
            if (area == null) return true;
            if (front.LengthSquared() < 1e-8f) front = Vector2.UnitY;
            front = Vector2.Normalize(front);
            var right = new Vector2(front.Y, -front.X);
            int nx = Math.Max(1, (int)MathF.Ceiling(width / Spacing)), ny = Math.Max(1, (int)MathF.Ceiling(depth / Spacing));
            for (int iy = 0; iy <= ny; iy++)
            for (int ix = 0; ix <= nx; ix++)
            {
                var p = center + right * ((ix / (float)nx - 0.5f) * width) + front * ((iy / (float)ny - 0.5f) * depth);
                if (!area.IsBuildable(p)) return false;
            }
            return true;
        }

        /// <summary>Kreis (Pinsel, Rundbau).</summary>
        public static bool AllowsCircle(RoadNetwork net, Vector2 center, float radius) =>
            AllowsRect(net, center, Vector2.UnitY, radius * 2f, radius * 2f);
    }
}
