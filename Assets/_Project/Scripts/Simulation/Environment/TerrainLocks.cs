using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Sperrmaske fürs Terraforming. Gesperrt (1) sind:
    ///   • alle Straßen (auch Brücken/Tröge/Tunnel) samt dem Streifen, den <see cref="TerrainMap.RoadMargin"/>
    ///     an die Fahrbahn bindet,
    ///   • alle Grundstücke von Gebäuden (die <c>CitySystem</c> beim Bau eingeebnet hat) plus eine Rasterzelle.
    /// Danach fällt die Sperre über <see cref="Band"/> Meter weich auf 0 ab – so entstehen am Rand Böschungen statt Stufen.
    /// Die Maske ist zustandslos und wird aus Straßennetz und Gebäudeliste gelesen, bleibt also bei Bau/Abriss
    /// automatisch aktuell.
    /// </summary>
    public static class TerrainLocks
    {
        /// <summary>Breite der weichen Überblendung außerhalb der Sperrfläche (Meter).</summary>
        public const float Band = 8f;

        public static Func<Vector2, float> Query(RoadNetwork roads, IEnumerable<Building> buildings, TerrainMap terrain, Vector2 min, Vector2 max)
        {
            float margin = terrain.RoadMargin;
            var segs = new List<RoadSegment>();
            if (roads != null)
                foreach (var s in roads.Segments)
                {
                    float reach = s.Type.HalfWidth + margin + Band;
                    Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var smin, out var smax);
                    if (smax.X + reach < min.X || smax.Y + reach < min.Y || smin.X - reach > max.X || smin.Y - reach > max.Y) continue;
                    segs.Add(s);
                }

            var lots = new List<Building>();
            float lotMargin = terrain.CellSize;
            if (buildings != null)
                foreach (var b in buildings)
                {
                    float reach = MathF.Max(b.Width, b.Depth) * 0.75f + lotMargin + Band;
                    var p = b.Position;
                    if (p.X + reach < min.X || p.Y + reach < min.Y || p.X - reach > max.X || p.Y - reach > max.Y) continue;
                    lots.Add(b);
                }

            if (segs.Count == 0 && lots.Count == 0) return null;

            return p =>
            {
                float result = 0f;
                foreach (var s in segs)
                {
                    float hw = s.Type.HalfWidth + margin;
                    float d = Vector2.Distance(p, s.Evaluate(s.ClosestT(p)));
                    if (d > hw + Band) continue;
                    if (d <= hw) return 1f;
                    result = MathF.Max(result, 1f - TerrainMap.SmoothStep(hw, hw + Band, d));
                }
                foreach (var b in lots)
                {
                    var d = p - b.Position;
                    float ox = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, b.Side)) - b.Width * 0.5f);
                    float oy = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, b.Front)) - b.Depth * 0.5f);
                    float outside = MathF.Sqrt(ox * ox + oy * oy);
                    if (outside > lotMargin + Band) continue;
                    if (outside <= lotMargin) return 1f;
                    result = MathF.Max(result, 1f - TerrainMap.SmoothStep(lotMargin, lotMargin + Band, outside));
                }
                return result;
            };
        }
    }
}
