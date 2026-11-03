using System.Numerics;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Parameter für <see cref="TerrainMap.Generate(Vector2, float, float, int, TerrainParams)"/> aus der Region der Weltkarte
    /// (Etappe 5). <see cref="Default"/> erzeugt exakt das bisherige Gelände (Hügel bis ~90 m, Fluss, keine Küste).
    /// </summary>
    public sealed class TerrainParams
    {
        /// <summary>Faktor auf die Hügel-/Berghöhen (Flachland ~0,4, Mittelgebirge 1, Gebirge bis 2,2).</summary>
        public float Relief = 1f;
        /// <summary>Fluss quer über die Karte.</summary>
        public bool River = true;
        /// <summary>Faktor auf die Flussbreite (großer Strom &gt; 1).</summary>
        public float RiverWidth = 1f;
        /// <summary>Richtung zum Meer (x = Ost, y = Nord); null = keine Küste.</summary>
        public Vector2? CoastDirection;
        /// <summary>Anteil der Karte (in Küstenrichtung), der Meer ist (0..0,3).</summary>
        public float CoastShare = 0.22f;

        public static TerrainParams Default => new TerrainParams();

        public override string ToString() =>
            $"Relief {Relief:0.00}, Fluss {(River ? $"ja ({RiverWidth:0.0}×)" : "nein")}, Küste {(CoastDirection.HasValue ? $"{CoastDirection.Value.X:0.0}/{CoastDirection.Value.Y:0.0}" : "nein")}";
    }
}
