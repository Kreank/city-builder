using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>Stufe in der Warenkette.</summary>
    public enum GoodTier : byte
    {
        /// <summary>Rohstoff (aus Vorkommen gewonnen).</summary>
        Raw,
        /// <summary>Zwischenprodukt (verarbeitet).</summary>
        Intermediate,
        /// <summary>Endprodukt (Fertigung).</summary>
        Final,
    }

    /// <summary>Bedarfsstufe der Haushalte (wie Anno): Grundbedarf → Komfort → Luxus.</summary>
    public enum ConsumerTier : byte
    {
        /// <summary>Kein Konsumgut (nur Firmen/Export).</summary>
        None,
        Basic,
        Comfort,
        Luxury,
    }

    /// <summary>Ressourcen der Welt-/Bodenkarte (Ids als Strings, wie sie der Agent „world“ liefert).</summary>
    public static class ResourceIds
    {
        public const string Fertility = "fertility";
        public const string Wood = "wood";
        public const string Ore = "ore";
        public const string Coal = "coal";
        public const string Oil = "oil";
        public const string Sand = "sand";
        public const string Groundwater = "groundwater";

        public static readonly string[] All = { Fertility, Wood, Ore, Coal, Oil, Sand, Groundwater };

        public static string DisplayName(string id)
        {
            switch (id)
            {
                case Fertility: return "Fruchtbarkeit";
                case Wood: return "Wald";
                case Ore: return "Erz";
                case Coal: return "Kohle";
                case Oil: return "Erdöl";
                case Sand: return "Sand/Kies";
                case Groundwater: return "Grundwasser";
                default: return id ?? "–";
            }
        }
    }

    /// <summary>
    /// Ein handelbares Gut. Mengen in „Einheiten“ (≈ 1 t bzw. eine Palette), Preise in € je Einheit.
    /// Alle Werte sind Platzhalter fürs Balancing (s. <see cref="GoodsCatalog"/>).
    /// </summary>
    public sealed class GoodType
    {
        /// <summary>Laufende Nummer im Katalog (Index für Lager-Arrays).</summary>
        public int Index { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public GoodTier Tier { get; internal set; }
        /// <summary>Weltmarkt-Grundpreis in € je Einheit.</summary>
        public float BasePrice { get; internal set; }
        /// <summary>Volumen je Einheit in m³ (Ladung eines Lkw).</summary>
        public float Volume { get; internal set; }
        /// <summary>Gewicht je Einheit in t (Ladung eines Lkw).</summary>
        public float Weight { get; internal set; }
        /// <summary>Anteil des Bestands, der je Spieltag verdirbt (0 = haltbar).</summary>
        public float SpoilPerDay { get; internal set; }
        /// <summary>Bedarfsstufe der Haushalte (None = kein Konsumgut).</summary>
        public ConsumerTier Consumer { get; internal set; }
        /// <summary>Verbrauch eines Haushalts (1 Person) je Spieltag, wenn er sich die Stufe leisten kann.</summary>
        public float HouseholdPerDay { get; internal set; }
        /// <summary>Verkaufsmenge je Verkäufer (Einzelhandels-Job) und Tag – bemisst das Ladenregal.</summary>
        public float RetailPerJobDay { get; internal set; }

        public bool IsConsumerGood => Consumer != ConsumerTier.None;

        /// <summary>Einheiten je Lkw-Ladung (Volumen- oder Gewichtsgrenze).</summary>
        public float LoadUnits => MathF.Min(GoodsRules.TruckVolume / Volume, GoodsRules.TruckWeight / Weight);

        public override string ToString() => Name;
    }

    /// <summary>
    /// Güterkatalog (datengetrieben, erweiterbar): Rohstoffe → Zwischenprodukte → Endprodukte nach dem Konzept
    /// „Wirtschaft, Welt &amp; Handel“. Neue Güter hier ergänzen; die Rezepte stehen in <see cref="RecipeCatalog"/>.
    /// </summary>
    public static class GoodsCatalog
    {
        // --- Rohstoffe
        public static readonly GoodType Grain;
        public static readonly GoodType Livestock;
        public static readonly GoodType Wood;
        public static readonly GoodType Ore;
        public static readonly GoodType Coal;
        public static readonly GoodType Oil;
        public static readonly GoodType Sand;
        // --- Zwischenprodukte
        public static readonly GoodType Flour;
        public static readonly GoodType Planks;
        public static readonly GoodType Steel;
        public static readonly GoodType Plastic;
        public static readonly GoodType Glass;
        public static readonly GoodType Cement;
        // --- Endprodukte
        public static readonly GoodType Food;
        public static readonly GoodType Clothing;
        public static readonly GoodType Furniture;
        public static readonly GoodType Electronics;
        public static readonly GoodType Vehicles;
        public static readonly GoodType BuildingMaterials;

        static readonly List<GoodType> all = new List<GoodType>();
        static readonly Dictionary<string, GoodType> byId = new Dictionary<string, GoodType>();

        /// <summary>Alle Güter in Katalogreihenfolge (Index = Position).</summary>
        public static IReadOnlyList<GoodType> All => all;
        public static int Count => all.Count;
        /// <summary>Konsumgüter (für den Einzelhandel), nach Stufe sortiert.</summary>
        public static readonly GoodType[] ConsumerGoods;

        static GoodsCatalog()
        {
            //                 Id            Name            Stufe                 €/E    m³    t     verdirbt
            Grain = Add("grain", "Getreide", GoodTier.Raw, 40f, 1.3f, 0.75f);
            Livestock = Add("livestock", "Vieh", GoodTier.Raw, 70f, 2.0f, 0.5f, 0.02f);
            Wood = Add("wood", "Holz", GoodTier.Raw, 35f, 1.5f, 0.7f);
            Ore = Add("ore", "Erz", GoodTier.Raw, 45f, 0.4f, 1.0f);
            Coal = Add("coal", "Kohle", GoodTier.Raw, 40f, 0.8f, 0.9f);
            Oil = Add("oil", "Erdöl", GoodTier.Raw, 60f, 1.1f, 0.9f);
            Sand = Add("sand", "Sand/Kies", GoodTier.Raw, 15f, 0.6f, 1.0f);

            Flour = Add("flour", "Mehl", GoodTier.Intermediate, 90f, 1.4f, 0.6f, 0.005f);
            Planks = Add("planks", "Bretter", GoodTier.Intermediate, 85f, 1.6f, 0.6f);
            Steel = Add("steel", "Stahl", GoodTier.Intermediate, 220f, 0.3f, 1.0f);
            Plastic = Add("plastic", "Kunststoff", GoodTier.Intermediate, 180f, 1.2f, 0.5f);
            Glass = Add("glass", "Glas", GoodTier.Intermediate, 140f, 0.8f, 0.6f);
            Cement = Add("cement", "Zement", GoodTier.Intermediate, 70f, 0.8f, 1.0f);

            //                                                                                            Stufe              je HH/Tag  je Verkäufer/Tag
            Food = Add("food", "Lebensmittel", GoodTier.Final, 120f, 1.2f, 0.5f, 0.03f, ConsumerTier.Basic, 0.2f, 1.8f);
            Clothing = Add("clothing", "Kleidung", GoodTier.Final, 160f, 1.5f, 0.2f, 0f, ConsumerTier.Comfort, 0.05f, 0.35f);
            Furniture = Add("furniture", "Möbel", GoodTier.Final, 260f, 3.0f, 0.4f, 0f, ConsumerTier.Comfort, 0.03f, 0.2f);
            Electronics = Add("electronics", "Elektronik", GoodTier.Final, 600f, 0.8f, 0.15f, 0f, ConsumerTier.Luxury, 0.03f, 0.15f);
            Vehicles = Add("vehicles", "Fahrzeuge", GoodTier.Final, 1500f, 10f, 1.4f, 0f, ConsumerTier.Luxury, 0.004f, 0.03f);
            BuildingMaterials = Add("buildingmaterials", "Baustoffe", GoodTier.Final, 150f, 1.0f, 1.0f);

            var consumer = new List<GoodType>();
            for (int tier = (int)ConsumerTier.Basic; tier <= (int)ConsumerTier.Luxury; tier++)
                foreach (var g in all) if ((int)g.Consumer == tier) consumer.Add(g);
            ConsumerGoods = consumer.ToArray();
        }

        static GoodType Add(string id, string name, GoodTier tier, float price, float volume, float weight, float spoil = 0f,
                            ConsumerTier consumer = ConsumerTier.None, float perHousehold = 0f, float perRetailJob = 0f)
        {
            var g = new GoodType
            {
                Index = all.Count, Id = id, Name = name, Tier = tier, BasePrice = price, Volume = volume, Weight = weight,
                SpoilPerDay = spoil, Consumer = consumer, HouseholdPerDay = perHousehold, RetailPerJobDay = perRetailJob,
            };
            all.Add(g);
            byId[id] = g;
            return g;
        }

        /// <summary>Gut nach Id (null = unbekannt).</summary>
        public static GoodType Get(string id) => id != null && byId.TryGetValue(id, out var g) ? g : null;

        public static string DisplayName(GoodTier t)
        {
            switch (t)
            {
                case GoodTier.Raw: return "Rohstoff";
                case GoodTier.Intermediate: return "Zwischenprodukt";
                default: return "Endprodukt";
            }
        }

        public static string DisplayName(ConsumerTier t)
        {
            switch (t)
            {
                case ConsumerTier.Basic: return "Grundbedarf";
                case ConsumerTier.Comfort: return "Komfort";
                case ConsumerTier.Luxury: return "Luxus";
                default: return "kein Konsumgut";
            }
        }
    }
}
