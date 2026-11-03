using System;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Berechnet die Lärmkarte aus allen Lärmquellen. Aktuell nur Straßen,
    /// später auch Industrie, Flughäfen usw. (und Verkehrsaufkommen statt fixer Werte).
    /// </summary>
    public sealed class NoiseSystem : ISimulationSystem
    {
        /// <summary>Alle 10 Ticks = 1x pro Spielstunde.</summary>
        public int TickInterval => 10;

        public void Tick(SimulationWorld world)
        {
            var noise = world.Environment.Noise;
            noise.Clear();

            float step = noise.CellSize * 0.5f;
            foreach (var segment in world.Roads.Segments)
            {
                var type = segment.Type;
                int samples = Math.Max(2, (int)MathF.Ceiling(segment.Length / step) + 1);
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);
                    noise.StampRadialMax(segment.Evaluate(t), type.NoiseRadius, type.NoiseLevel);
                }
            }
        }
    }
}
