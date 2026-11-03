using System;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Wasserstraße (Stufe 3): Mindesthöhe von Brücken über der Fahrrinne (lichte Durchfahrtshöhe + Tragwerk über dem
    /// Wasserspiegel). Gesetzt vom <c>ShippingSystem</c>; null = keine Fahrrinne (Tests/Standardwelt, alles wie bisher).
    /// </summary>
    public sealed partial class TerrainMap : INavigableGround
    {
        /// <summary>(Punkt) → verlangte Höhe über dem Wasserspiegel (m), 0 = keine Fahrrinne.</summary>
        public Func<Vector2, float> NavigationClearance { get; set; }

        public float NavigationFloor(Vector2 position) => NavigationClearance?.Invoke(position) ?? 0f;

        /// <summary>
        /// Hafen: Rechteck auf Kaihöhe bringen – anders als <see cref="FlattenArea"/> auch im Wasser (Aufschüttung bis zur
        /// Kaimauer). Ändert nur das aktuelle Gelände; das natürliche (Fahrrinnen, Wassertiefe vor dem Kai) bleibt.
        /// </summary>
        public void FillArea(Vector2 center, Vector2 front, float width, float depth, float height, float band = 6f)
        {
            var side = new Vector2(-front.Y, front.X);
            float reach = MathF.Max(width, depth) * 0.75f + band;
            int x0 = Math.Max(0, (int)MathF.Floor((center.X - reach - Origin.X) / CellSize));
            int x1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((center.X + reach - Origin.X) / CellSize));
            int y0 = Math.Max(0, (int)MathF.Floor((center.Y - reach - Origin.Y) / CellSize));
            int y1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((center.Y + reach - Origin.Y) / CellSize));
            bool changed = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var d = PointAt(x, y) - center;
                    float along = Vector2.Dot(d, front);
                    float ox = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, side)) - width * 0.5f);
                    float oy = MathF.Max(0f, MathF.Abs(along) - depth * 0.5f);
                    float outside = MathF.Sqrt(ox * ox + oy * oy);
                    if (outside > band) continue;
                    int idx = y * Resolution + x;
                    // Kaimauer: zur Wasserseite (hinten) senkrecht, keine Böschung ins Fahrwasser
                    if (outside > 0f && along < -depth * 0.5f) continue;
                    if (outside > 0f && current[idx] < WaterLevel) continue;
                    float w = outside <= 0f ? 1f : 1f - SmoothStep(0f, band, outside);
                    float target = ApplyRoadRule(idx, Lerp(current[idx], height, w));
                    if (MathF.Abs(target - current[idx]) < 0.01f) continue;
                    current[idx] = target;
                    changed = true;
                }
            if (!changed) return;
            Version++;
            Changed?.Invoke(center - new Vector2(reach), center + new Vector2(reach));
        }
    }
}
