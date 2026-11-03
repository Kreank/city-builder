using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Gruppe der setzbaren Gebäudearten (Reiter der Bauleisten-Kategorie „Bauen“).</summary>
    public enum PlaceableGroup
    {
        /// <summary>Wohnhäuser (gering/mittel/dicht).</summary>
        Residential,
        /// <summary>Laden, Wohn- und Geschäftshaus, Bürohaus.</summary>
        Commercial,
        /// <summary>Gewerbe-/Industriehallen.</summary>
        Industrial,
    }

    /// <summary>
    /// Setzbare Gebäudeart (Anno-Stil, „Setzen statt Zonen“): Zonenart (bestimmt Wohnungen/Jobs je Zelle, Modelle, Steuern und
    /// ob eine Firma einzieht), feste Grundstücksgröße in Zonenzellen (8 m) und Baukosten. Der Name „House…“ ist historisch
    /// (zuerst gab es nur Wohnhäuser) – Läden, Büros und Hallen nutzen dieselben Typen, Pläne und Regeln.
    /// Der Spieler setzt nur die <b>Hülle</b>: welcher Betrieb in Laden/Büro/Halle einzieht, wählt die Simulation (G5-Standortwahl).
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
        /// <summary>Reiter in der Bauleiste.</summary>
        public PlaceableGroup Group { get; }
        /// <summary>Kurzbeschreibung für den Kachel-Tooltip.</summary>
        public string Description { get; }

        public float Width => WidthCells * ZoneGrid.CellSize;
        public float Depth => DepthCells * ZoneGrid.CellSize;
        public int Cells => WidthCells * DepthCells;
        /// <summary>Wohnungen auf einer Stufe (wie ein gewachsenes Gebäude gleicher Größe, s. <see cref="LandValue.LandValueRules.HouseholdsFor"/>).</summary>
        public int HouseholdsAtLevel(int level) => LandValue.LandValueRules.HouseholdsFor(Zone, Cells, level);
        /// <summary>Arbeitsplätze auf einer Stufe (wie ein gewachsenes Gebäude gleicher Größe, s. <see cref="LandValue.LandValueRules.JobsFor"/>).</summary>
        public int JobsAtLevel(int level) => LandValue.LandValueRules.JobsFor(Zone, Cells, level);
        /// <summary>Zieht eine Firma ein (Laden, Mischgebiet, Büro, Industrie – G5)?</summary>
        public bool HasFirm => Zone == ZoneType.Retail || Zone == ZoneType.Mixed || Zone == ZoneType.Office || Zone == ZoneType.Industry;
        /// <summary>Hat Wohnungen (Wohnen, Mischgebiet)?</summary>
        public bool HasHomes => Zones.HouseholdsPerCell(Zone) > 0f;

        public HouseType(string id, string displayName, ZoneType zone, int widthCells, int depthCells, float cost,
            PlaceableGroup group = PlaceableGroup.Residential, string description = null)
        {
            Id = id;
            DisplayName = displayName;
            Zone = zone;
            WidthCells = widthCells;
            DepthCells = depthCells;
            Cost = cost;
            Group = group;
            Description = description ?? displayName;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Setzbare Gebäudearten. Wohnen (gering/mittel/dicht) seit „Wohnhäuser setzen“; seit „Setzen statt Zonen“ auch Laden,
    /// Wohn- und Geschäftshaus (Laden unten, Wohnungen oben), Bürohaus sowie Gewerbe- und Industriehalle. Läden, Büros und
    /// Hallen bekommen ihren Betrieb von der Simulation (<c>GoodsSystem</c>: Einzelhandel, Büro bzw. Industrie-Standortwahl
    /// inkl. Schwerpunkt/Förderung). Bauart = (Herkunft Spieler, Zone, Größe) → im Spielstand muss nichts weiter stehen
    /// (gesetzte Gebäude alter Stände sind 2×2/3×3/4×4-Wohnhäuser und passen weiter).
    /// </summary>
    public static class HouseCatalog
    {
        public static readonly HouseType Low = new HouseType("house_low", "Einfamilienhaus", ZoneType.ResidentialLow, 2, 2, 4000f,
            PlaceableGroup.Residential, "Wohnen gering: wenige Wohnungen");
        public static readonly HouseType Medium = new HouseType("house_medium", "Mehrfamilienhaus", ZoneType.ResidentialMedium, 3, 3, 16000f,
            PlaceableGroup.Residential, "Wohnen mittel");
        public static readonly HouseType High = new HouseType("house_high", "Wohnblock", ZoneType.ResidentialHigh, 4, 4, 45000f,
            PlaceableGroup.Residential, "Wohnen dicht: viele Wohnungen");

        // --- Setzen statt Zonen: Gewerbe und Industrie (Hülle setzen, Betrieb wählt die Simulation)
        public static readonly HouseType Shop = new HouseType("shop", "Laden", ZoneType.Retail, 2, 2, 6000f,
            PlaceableGroup.Commercial, "Einzelhandel: ein Händler zieht ein und verkauft an Haushalte in Fußweite, Ware über die Warenketten");
        public static readonly HouseType MixedUse = new HouseType("mixed", "Wohn- und Geschäftshaus", ZoneType.Mixed, 3, 3, 20000f,
            PlaceableGroup.Commercial, "Mischgebiet: Laden im Erdgeschoss, Wohnungen darüber");
        public static readonly HouseType OfficeBuilding = new HouseType("office", "Bürohaus", ZoneType.Office, 3, 3, 28000f,
            PlaceableGroup.Commercial, "Büro: Dienstleister, braucht gut ausgebildete Fachkräfte");
        public static readonly HouseType SmallHall = new HouseType("hall_small", "Gewerbehalle", ZoneType.Industry, 3, 3, 15000f,
            PlaceableGroup.Industrial, "Kleine Halle: welcher Betrieb einzieht, entscheidet die Simulation (Vorkommen, Nachfrage, Schwerpunkt)");
        public static readonly HouseType LargeHall = new HouseType("hall_large", "Industriehalle", ZoneType.Industry, 4, 4, 30000f,
            PlaceableGroup.Industrial, "Große Halle: mehr Arbeitsplätze; Betrieb nach Vorkommen, Nachfrage und Schwerpunkt");
        // --- Ende Setzen statt Zonen

        public static readonly HouseType[] All = { Low, Medium, High, Shop, MixedUse, OfficeBuilding, SmallHall, LargeHall };

        public static HouseType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }

        /// <summary>Erste Bauart einer Zonenart (Industrie: Gewerbehalle).</summary>
        public static HouseType ForZone(ZoneType zone)
        {
            foreach (var t in All) if (t.Zone == zone) return t;
            return null;
        }

        /// <summary>Bauart zu Zone + Größe; ohne exakten Treffer die erste der Zone.</summary>
        public static HouseType ForZone(ZoneType zone, int widthCells, int depthCells)
        {
            foreach (var t in All) if (t.Zone == zone && t.WidthCells == widthCells && t.DepthCells == depthCells) return t;
            return ForZone(zone);
        }

        /// <summary>Bauart eines Gebäudes (null = aus Zone gewachsen bzw. unbekannt).</summary>
        public static HouseType Of(Building b) => b != null && b.IsPlayerPlaced ? ForZone(b.Zone, b.WidthCells, b.DepthCells) : null;

        /// <summary>Anzeigename: Bauart bei gesetzten Gebäuden, sonst Zone.</summary>
        public static string NameOf(Building b) => Of(b)?.DisplayName ?? (b == null ? "" : Zones.DisplayName(b.Zone));

        /// <summary>Gebäudearten eines Reiters (feste Katalog-Reihenfolge).</summary>
        public static List<HouseType> InGroup(PlaceableGroup g)
        {
            var list = new List<HouseType>();
            foreach (var t in All) if (t.Group == g) list.Add(t);
            return list;
        }

        public static string GroupName(PlaceableGroup g) =>
            g == PlaceableGroup.Residential ? "Wohnen" : g == PlaceableGroup.Commercial ? "Gewerbe" : "Industrie";
    }
}
