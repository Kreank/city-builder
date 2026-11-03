namespace CityBuilder.Simulation.Zoning
{
    public enum ZoneType : byte
    {
        None = 0,
        ResidentialLow,
        ResidentialMedium,
        ResidentialHigh,
        /// <summary>Mischgebiet: Läden im Erdgeschoss, Wohnungen darüber.</summary>
        Mixed,
        Retail,
        Office,
        Industry,
    }

    public static class Zones
    {
        public static readonly ZoneType[] All =
        {
            ZoneType.ResidentialLow, ZoneType.ResidentialMedium, ZoneType.ResidentialHigh,
            ZoneType.Mixed, ZoneType.Retail, ZoneType.Office, ZoneType.Industry,
        };

        public static bool IsResidential(ZoneType z) =>
            z == ZoneType.ResidentialLow || z == ZoneType.ResidentialMedium || z == ZoneType.ResidentialHigh || z == ZoneType.Mixed;

        public static string DisplayName(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return "Wohnen (gering)";
                case ZoneType.ResidentialMedium: return "Wohnen (mittel)";
                case ZoneType.ResidentialHigh: return "Wohnen (hoch)";
                case ZoneType.Mixed: return "Mischgebiet";
                case ZoneType.Retail: return "Einzelhandel";
                case ZoneType.Office: return "Büro";
                case ZoneType.Industry: return "Industrie";
                default: return "keine";
            }
        }

        /// <summary>Mögliche Grundstücksgrößen (Breite × Tiefe in Zellen), bevorzugte zuerst.</summary>
        public static (int w, int d)[] LotSizes(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return new[] { (2, 2), (1, 2), (1, 1) };
                case ZoneType.ResidentialMedium: return new[] { (3, 3), (2, 3), (2, 2) };
                case ZoneType.ResidentialHigh: return new[] { (4, 4), (3, 3), (2, 3) };
                case ZoneType.Mixed: return new[] { (3, 3), (2, 3), (2, 2) };
                case ZoneType.Retail: return new[] { (2, 3), (2, 2), (1, 2), (1, 1) };
                case ZoneType.Office: return new[] { (4, 4), (3, 3), (2, 2) };
                case ZoneType.Industry: return new[] { (4, 4), (4, 3), (3, 3), (2, 2) };
                default: return new (int, int)[0];
            }
        }

        /// <summary>Haushalte je Zelle (Wohnzonen).</summary>
        public static float HouseholdsPerCell(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return 0.5f;
                case ZoneType.ResidentialMedium: return 3f;
                case ZoneType.ResidentialHigh: return 8f;
                case ZoneType.Mixed: return 2f;
                default: return 0f;
            }
        }

        /// <summary>Arbeitsplätze je Zelle.</summary>
        public static float JobsPerCell(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.Mixed: return 1f;
                case ZoneType.Retail: return 2f;
                case ZoneType.Office: return 6f;
                case ZoneType.Industry: return 3f;
                default: return 0f;
            }
        }
    }
}
