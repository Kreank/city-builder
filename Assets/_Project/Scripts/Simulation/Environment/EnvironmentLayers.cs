using System;
using System.Numerics;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Alle Umwelt-Raster einer Welt. Weitere (Wasser, Boden, Klima ...) kommen hier dazu.</summary>
    public sealed partial class EnvironmentLayers
    {
        /// <summary>Lärm 0..1 – wird jeden Durchlauf neu aus den Quellen berechnet.</summary>
        public GridLayer Noise { get; }

        /// <summary>Luftverschmutzung – sammelt sich an, breitet sich aus, baut sich langsam ab.</summary>
        public GridLayer AirPollution { get; }

        public EnvironmentLayers(Vector2 origin, float sizeMeters, float cellSize)
        {
            int cellsPerSide = Math.Max(1, (int)MathF.Ceiling(sizeMeters / cellSize));
            Noise = new GridLayer("Lärm", origin, cellsPerSide, cellsPerSide, cellSize);
            AirPollution = new GridLayer("Abgas", origin, cellsPerSide, cellsPerSide, cellSize);
        }
    }
}
