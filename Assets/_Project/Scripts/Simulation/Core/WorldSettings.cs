namespace CityBuilder.Simulation.Core
{
    public sealed class WorldSettings
    {
        /// <summary>Kantenlänge der (quadratischen) Spielwelt in Metern. Die Welt ist um 0/0 zentriert.</summary>
        public float SizeMeters = 2048f;

        /// <summary>Zellgröße der Umwelt-Raster (Lärm, Abgas ...) in Metern.</summary>
        public float EnvironmentCellSize = 16f;

        /// <summary>Hügel/Fluss erzeugen (sonst flacher Boden auf 0 – z. B. für Tests).</summary>
        public bool GenerateTerrain;
        public int TerrainSeed = 4711;
        /// <summary>Abstand der Gitterpunkte der Höhenkarte in Metern.</summary>
        public float TerrainCellSize = 4f;

        // --- Stadtkasse
        /// <summary>
        /// Sandbox-Kasse: Kosten werden gebucht, aber nie blockiert (keine Zinsen, kein Baustopp).
        /// Standard an, damit Tests und Werkzeuge ohne Geld-Denken bauen können; das Spiel
        /// (<c>SimulationRunner</c>) schaltet es aus.
        /// </summary>
        public bool UnlimitedFunds = true;
        /// <summary>Startkapital der Stadtkasse in €.</summary>
        public double StartingFunds = 100000.0;
        // --- Ende Stadtkasse
    }
}
