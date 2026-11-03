using System;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>
    /// Bauart eines vom Spieler gesetzten Wohnhauses (Anno-Stil): Zonenart (bestimmt Wohnungen je Zelle, Modelle und
    /// Bedürfnis-Bequemlichkeit), feste Grundstücksgröße in Zonenzellen (8 m) und Baukosten.
    /// </summary>
    public sealed class HouseType
    {
        /// <summary>Speicher-/Katalogschlüssel.</summary>
        public string Id { get; }
        public string DisplayName { get; }
        public ZoneType Zone { get; }
        public int WidthCells { get; }
        public int DepthCells { get; }
        /// <summary>Baukosten in € (Stadtkasse, Kategorie <see cref="Economy.BudgetCategory.HouseConstruction"/>).</summary>
        public float Cost { get; }

        public float Width => WidthCells * ZoneGrid.CellSize;
        public float Depth => DepthCells * ZoneGrid.CellSize;
        public int Cells => WidthCells * DepthCells;
        /// <summary>Wohnungen auf Stufe 1 (wie ein gewachsenes Gebäude gleicher Größe, s. <see cref="LandValue.LandValueRules.HouseholdsFor"/>).</summary>
        public int HouseholdsAtLevel(int level) => LandValue.LandValueRules.HouseholdsFor(Zone, Cells, level);

        public HouseType(string id, string displayName, ZoneType zone, int widthCells, int depthCells, float cost)
        {
            Id = id;
            DisplayName = displayName;
            Zone = zone;
            WidthCells = widthCells;
            DepthCells = depthCells;
            Cost = cost;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Bauarten für einzeln gesetzte Wohnhäuser. Bewusst nur Wohnen (gering/mittel/dicht): Läden, Büros und Industrie sind
    /// Firmen mit Lager, Gewinn und Pleite (G5) – die wählt weiter die Simulation (Entscheidung „Produktion lenken statt bauen“).
    /// Mischgebiet wäre halb Firma; der Laden in Fußweite ist dagegen eine <i>Anforderung</i> der Häuser (<see cref="HouseRules"/>).
    /// Je Zonenart genau eine Bauart → Bauart = (Herkunft Spieler, Zone), im Spielstand muss nichts weiter stehen.
    /// </summary>
    public static class HouseCatalog
    {
        public static readonly HouseType Low = new HouseType("house_low", "Einfamilienhaus", ZoneType.ResidentialLow, 2, 2, 4000f);
        public static readonly HouseType Medium = new HouseType("house_medium", "Mehrfamilienhaus", ZoneType.ResidentialMedium, 3, 3, 16000f);
        public static readonly HouseType High = new HouseType("house_high", "Wohnblock", ZoneType.ResidentialHigh, 4, 4, 45000f);

        public static readonly HouseType[] All = { Low, Medium, High };

        public static HouseType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }

        public static HouseType ForZone(ZoneType zone)
        {
            foreach (var t in All) if (t.Zone == zone) return t;
            return null;
        }

        /// <summary>Bauart eines Gebäudes (null = aus Zone gewachsen bzw. unbekannt).</summary>
        public static HouseType Of(Building b) => b != null && b.IsPlayerPlaced ? ForZone(b.Zone) : null;

        /// <summary>Anzeigename: Bauart bei Spieler-Häusern, sonst Zone.</summary>
        public static string NameOf(Building b) => Of(b)?.DisplayName ?? (b == null ? "" : Zones.DisplayName(b.Zone));
    }
}
