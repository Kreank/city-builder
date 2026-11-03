namespace CityBuilder.Simulation.Core
{
    public sealed class WorldSettings
    {
        /// <summary>Kantenlänge der (quadratischen) Spielwelt in Metern. Die Welt ist um 0/0 zentriert.</summary>
        public float SizeMeters = 2048f;

        /// <summary>Zellgröße der Umwelt-Raster (Lärm, Abgas ...) in Metern.</summary>
        public float EnvironmentCellSize = 16f;
    }
}
