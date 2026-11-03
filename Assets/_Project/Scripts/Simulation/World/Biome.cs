namespace CityBuilder.Simulation.World
{
    /// <summary>Großlebensraum einer Weltzelle (vereinfachtes Whittaker-Schema aus Jahresmitteltemperatur und Niederschlag).</summary>
    public enum Biome : byte
    {
        Ocean,
        /// <summary>Schelfmeer (&lt; 200 m tief, auf Kontinentalkruste) – Öl/Gas möglich.</summary>
        Shelf,
        Ice,
        Tundra,
        Taiga,
        TemperateForest,
        TemperateRainforest,
        Grassland,
        Desert,
        Savanna,
        TropicalRainforest,
        /// <summary>Hochgebirge (alpin, über der Baumgrenze).</summary>
        Mountain,
    }

    public static class Biomes
    {
        public const int Count = 12;

        public static bool IsWater(Biome b) => b == Biome.Ocean || b == Biome.Shelf;

        public static string DisplayName(Biome b)
        {
            switch (b)
            {
                case Biome.Ocean: return "Ozean";
                case Biome.Shelf: return "Schelfmeer";
                case Biome.Ice: return "Eis";
                case Biome.Tundra: return "Tundra";
                case Biome.Taiga: return "Nadelwald (Taiga)";
                case Biome.TemperateForest: return "Laub-/Mischwald";
                case Biome.TemperateRainforest: return "Gemäßigter Regenwald";
                case Biome.Grassland: return "Steppe/Grasland";
                case Biome.Desert: return "Wüste";
                case Biome.Savanna: return "Savanne";
                case Biome.TropicalRainforest: return "Tropischer Regenwald";
                case Biome.Mountain: return "Hochgebirge";
                default: return b.ToString();
            }
        }

        /// <summary>Natürliche Waldbedeckung 0..1.</summary>
        public static float ForestCover(Biome b)
        {
            switch (b)
            {
                case Biome.TropicalRainforest: return 0.95f;
                case Biome.TemperateRainforest: return 0.92f;
                case Biome.TemperateForest: return 0.75f;
                case Biome.Taiga: return 0.7f;
                case Biome.Savanna: return 0.22f;
                case Biome.Grassland: return 0.1f;
                case Biome.Mountain: return 0.12f;
                case Biome.Tundra: return 0.04f;
                default: return 0f;
            }
        }

        /// <summary>Grundfruchtbarkeit des Bodens 0..1 (Schwarzerde der Steppe hoch, ausgewaschene Tropenböden mittel).</summary>
        public static float BaseFertility(Biome b)
        {
            switch (b)
            {
                case Biome.Grassland: return 0.85f;
                case Biome.TemperateForest: return 0.7f;
                case Biome.TemperateRainforest: return 0.55f;
                case Biome.Savanna: return 0.5f;
                case Biome.TropicalRainforest: return 0.42f;
                case Biome.Taiga: return 0.3f;
                case Biome.Mountain: return 0.12f;
                case Biome.Tundra: return 0.08f;
                case Biome.Desert: return 0.06f;
                default: return 0f;
            }
        }
    }
}
