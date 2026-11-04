using System;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Berechnet die Lärmkarte aus allen Lärmquellen. Aktuell nur Straßen,
    /// später auch Industrie, Flughäfen usw. (und Verkehrsaufkommen statt fixer Werte).
    /// Lärmschutzwände (<see cref="NoiseBarrierSystem"/>) dämpfen den Straßenlärm hinter sich.
    /// </summary>
    public sealed class NoiseSystem : ISimulationSystem
    {
        /// <summary>Alle 10 Ticks = 1x pro Spielstunde.</summary>
        public int TickInterval => 10;

        public void Tick(SimulationWorld world)
        {
            var noise = world.Environment.Noise;
            noise.Clear();
            var barriers = world.NoiseBarriers; // Eigene Assets: Lärmschutzwände (null/leer = wie bisher)
            bool shielded = barriers != null && barriers.Count > 0;

            float step = noise.CellSize * 0.5f;
            foreach (var segment in world.Roads.Segments)
            {
                var type = segment.Type;
                int samples = Math.Max(2, (int)MathF.Ceiling(segment.Length / step) + 1);
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);
                    // Tunnel sind still, im offenen Trog dämpfen die Wände
                    float h = segment.HeightAt(t);
                    float level = segment.IsCoveredAt(t) ? 0f : h < -0.3f ? type.NoiseLevel * 0.5f : type.NoiseLevel;
                    var p = segment.Evaluate(t);
                    if (shielded && level > 0f && barriers.AnyNear(p, type.NoiseRadius)) StampShielded(noise, p, type.NoiseRadius, level, barriers);
                    else noise.StampRadialMax(p, type.NoiseRadius, level);
                }
            }
            world.Rail?.StampNoise(noise); // --- Bahn: Gleise (Züge) machen Lärm, Tunnel nicht
            world.Aviation?.StampNoise(noise); // --- Flughafen: Fluglärm entlang Bahn und Einflugschneisen
        }

        // --- Eigene Assets: wie GridLayer.StampRadialMax (gleicher Abfall), aber je Zelle mit der Dämpfung durch Wände
        static void StampShielded(GridLayer layer, Vector2 center, float radius, float strength, NoiseBarrierSystem barriers)
        {
            if (radius <= 0f || strength <= 0f) return;
            var cells = layer.RawValues;
            var origin = layer.Origin;
            float size = layer.CellSize;
            int minX = Math.Max(0, (int)MathF.Floor((center.X - radius - origin.X) / size));
            int maxX = Math.Min(layer.Width - 1, (int)MathF.Floor((center.X + radius - origin.X) / size));
            int minY = Math.Max(0, (int)MathF.Floor((center.Y - radius - origin.Y) / size));
            int maxY = Math.Min(layer.Height - 1, (int)MathF.Floor((center.Y + radius - origin.Y) / size));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var c = layer.CellCenter(x, y);
                float dist = Vector2.Distance(c, center);
                if (dist > radius) continue;
                float v = strength * (1f - dist / radius) * barriers.Attenuation(center, c);
                int index = y * layer.Width + x;
                if (v > cells[index]) cells[index] = v;
            }
            layer.MarkChanged();
        }
        // --- Ende Eigene Assets
    }
}
