using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>Rolle einer Firma in der Warenkette.</summary>
    public enum FirmRole : byte
    {
        /// <summary>Rohstoff-Gewinnung (braucht Vorkommen).</summary>
        Extraction,
        /// <summary>Verarbeitung Rohstoff → Zwischenprodukt.</summary>
        Processing,
        /// <summary>Fertigung Zwischenprodukt → Endprodukt.</summary>
        Manufacturing,
        /// <summary>Einzelhandel (Einzelhandels-/Mischgebiet): kauft Endprodukte, verkauft an Haushalte.</summary>
        Retail,
        /// <summary>Dienstleistung/Büro: keine Güter, Erlös je Beschäftigtem.</summary>
        Office,
    }

    /// <summary>Unterart der Industriezone (eine je Grundstück).</summary>
    public enum IndustryForm : byte
    {
        None,
        Agriculture,
        Forestry,
        Mining,
        Oil,
        Processing,
        Manufacturing,
    }

    /// <summary>Menge eines Guts je Arbeitsplatz und Spieltag (bei voller Belegschaft und Produktivität 1).</summary>
    public readonly struct GoodAmount
    {
        public readonly GoodType Good;
        public readonly float PerJob;
        public GoodAmount(GoodType good, float perJob) { Good = good; PerJob = perJob; }
        public override string ToString() => $"{PerJob:0.##} {Good?.Name}";
    }

    /// <summary>
    /// Rezept: Eingänge → Ausgänge je Arbeitsplatz und Tag, Arbeitskräfte je Bildungsstufe, Strom-/Wasserbedarf und
    /// (für Gewinnung) das nötige Vorkommen. Einzelhandel nutzt <see cref="Inputs"/> als Sortiment (Absatz je Verkäufer).
    /// </summary>
    public sealed class Recipe
    {
        public int Index { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public FirmRole Role { get; internal set; }
        public IndustryForm Form { get; internal set; }
        public GoodAmount[] Inputs { get; internal set; } = new GoodAmount[0];
        public GoodAmount[] Outputs { get; internal set; } = new GoodAmount[0];
        /// <summary>Ressource der Bodenkarte (<see cref="ResourceIds"/>), null = kein Vorkommen nötig.</summary>
        public string Deposit { get; internal set; }
        /// <summary>Mindestgüte des Vorkommens am Ort (0..1).</summary>
        public float MinDeposit { get; internal set; }
        /// <summary>
        /// Anteil der Arbeitsplätze je Bildungsstufe: [0] einfache Arbeit (ohne/Grundschule),
        /// [1] mindestens mittlerer Abschluss, [2] Studium. Summe 1.
        /// </summary>
        public float[] Workforce { get; internal set; } = { 1f, 0f, 0f };
        /// <summary>Strom (kW) und Wasser (m³/h) je Arbeitsplatz – Daten für die Versorgung (noch ohne Rückwirkung aufs Netz).</summary>
        public float PowerPerJob { get; internal set; }
        public float WaterPerJob { get; internal set; }

        public bool IsIndustry => Role == FirmRole.Extraction || Role == FirmRole.Processing || Role == FirmRole.Manufacturing;

        /// <summary>Wert der Ausgänge minus Eingänge je Arbeitsplatz und Tag zu Grundpreisen.</summary>
        public float ValueAddedPerJob
        {
            get
            {
                float v = 0f;
                foreach (var o in Outputs) v += o.PerJob * o.Good.BasePrice;
                foreach (var i in Inputs) v -= i.PerJob * i.Good.BasePrice;
                return v;
            }
        }

        public bool Produces(GoodType g) { foreach (var o in Outputs) if (o.Good == g) return true; return false; }
        public bool Uses(GoodType g) { foreach (var i in Inputs) if (i.Good == g) return true; return false; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Rezeptkatalog (datengetrieben). Neue Rezepte hier ergänzen. Faustregel fürs Balancing: Wertschöpfung
    /// (Ausgänge − Eingänge zu Grundpreisen) ≈ 175 € je Arbeitsplatz und Tag – deckt Lohn (~100 €) und Fracht
    /// auch dann, wenn alle Eingänge importiert und alle Ausgänge exportiert werden; lokale Ketten verdienen mehr.
    /// </summary>
    public static class RecipeCatalog
    {
        static readonly List<Recipe> all = new List<Recipe>();
        static readonly Dictionary<string, Recipe> byId = new Dictionary<string, Recipe>();

        public static IReadOnlyList<Recipe> All => all;
        /// <summary>Alle Industrie-Rezepte (Unterarten der Industriezone).</summary>
        public static readonly Recipe[] Industry;
        public static readonly Recipe Retail;
        public static readonly Recipe Office;

        static RecipeCatalog()
        {
            float[] simple = { 0.9f, 0.1f, 0f }, mixed = { 0.7f, 0.25f, 0.05f }, skilled = { 0.4f, 0.45f, 0.15f };

            // --- Gewinnung (braucht Vorkommen)
            Add("farm", "Bauernhof", FirmRole.Extraction, IndustryForm.Agriculture, null,
                Out(GoodsCatalog.Grain, 3.4f, GoodsCatalog.Livestock, 0.56f), ResourceIds.Fertility, 0.25f, simple, 2f, 1f);
            Add("forestry", "Forstbetrieb", FirmRole.Extraction, IndustryForm.Forestry, null,
                Out(GoodsCatalog.Wood, 5f), ResourceIds.Wood, 0.2f, simple, 1f, 0.2f);
            Add("ore_mine", "Erzbergwerk", FirmRole.Extraction, IndustryForm.Mining, null,
                Out(GoodsCatalog.Ore, 3.9f), ResourceIds.Ore, 0.15f, mixed, 6f, 1f);
            Add("coal_mine", "Kohlezeche", FirmRole.Extraction, IndustryForm.Mining, null,
                Out(GoodsCatalog.Coal, 4.4f), ResourceIds.Coal, 0.15f, mixed, 6f, 1f);
            Add("quarry", "Kiesgrube", FirmRole.Extraction, IndustryForm.Mining, null,
                Out(GoodsCatalog.Sand, 11.7f), ResourceIds.Sand, 0.15f, simple, 3f, 0.5f);
            Add("oil_well", "Ölförderung", FirmRole.Extraction, IndustryForm.Oil, null,
                Out(GoodsCatalog.Oil, 2.9f), ResourceIds.Oil, 0.15f, new[] { 0.6f, 0.3f, 0.1f }, 5f, 0.5f);

            // --- Verarbeitung
            Add("mill", "Mühle", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Grain, 4f), Out(GoodsCatalog.Flour, 3.7f), null, 0f, simple, 4f, 0.5f);
            Add("sawmill", "Sägewerk", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Wood, 4f), Out(GoodsCatalog.Planks, 3.7f), null, 0f, simple, 4f, 0.3f);
            Add("steelworks", "Stahlwerk", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Ore, 2f, GoodsCatalog.Coal, 1.5f), Out(GoodsCatalog.Steel, 1.48f), null, 0f, mixed, 10f, 2f);
            Add("refinery", "Raffinerie", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Oil, 3f), Out(GoodsCatalog.Plastic, 2f), null, 0f, mixed, 8f, 1.5f);
            Add("glassworks", "Glashütte", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Sand, 4f, GoodsCatalog.Coal, 1f), Out(GoodsCatalog.Glass, 1.95f), null, 0f, mixed, 8f, 1f);
            Add("cementworks", "Zementwerk", FirmRole.Processing, IndustryForm.Processing,
                In(GoodsCatalog.Sand, 5f, GoodsCatalog.Coal, 1f), Out(GoodsCatalog.Cement, 4.15f), null, 0f, simple, 7f, 1f);

            // --- Fertigung
            Add("food_factory", "Lebensmittelfabrik", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Flour, 1.5f, GoodsCatalog.Livestock, 1f), Out(GoodsCatalog.Food, 3.15f), null, 0f, mixed, 4f, 1.5f);
            Add("textile", "Textilfabrik", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Livestock, 1f, GoodsCatalog.Plastic, 0.5f), Out(GoodsCatalog.Clothing, 2.1f), null, 0f, mixed, 3f, 1f);
            Add("furniture", "Möbelfabrik", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Planks, 1.6f, GoodsCatalog.Steel, 0.2f), Out(GoodsCatalog.Furniture, 1.37f), null, 0f, mixed, 3f, 0.3f);
            Add("electronics", "Elektronikwerk", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Plastic, 0.4f, GoodsCatalog.Steel, 0.2f, GoodsCatalog.Glass, 0.3f), Out(GoodsCatalog.Electronics, 0.56f),
                null, 0f, skilled, 5f, 0.5f);
            Add("building_materials", "Baustoffwerk", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Cement, 1.2f, GoodsCatalog.Glass, 0.3f, GoodsCatalog.Steel, 0.15f), Out(GoodsCatalog.BuildingMaterials, 2.2f),
                null, 0f, simple, 4f, 1f);
            Add("vehicle_plant", "Fahrzeugwerk", FirmRole.Manufacturing, IndustryForm.Manufacturing,
                In(GoodsCatalog.Steel, 0.5f, GoodsCatalog.Plastic, 0.3f, GoodsCatalog.Glass, 0.15f), Out(GoodsCatalog.Vehicles, 0.24f),
                null, 0f, skilled, 6f, 0.8f);

            var industry = new List<Recipe>(all);
            Industry = industry.ToArray();

            // --- Einzelhandel: Sortiment = Konsumgüter, Menge = Absatz je Verkäufer und Tag
            var shelf = new List<GoodAmount>();
            foreach (var g in GoodsCatalog.ConsumerGoods) shelf.Add(new GoodAmount(g, g.RetailPerJobDay));
            Retail = Add("retail", "Einzelhandel", FirmRole.Retail, IndustryForm.None, shelf.ToArray(), new GoodAmount[0],
                         null, 0f, new[] { 0.85f, 0.15f, 0f }, 1.5f, 0.1f);
            Office = Add("office", "Büro/Dienstleistung", FirmRole.Office, IndustryForm.None, new GoodAmount[0], new GoodAmount[0],
                         null, 0f, new[] { 0f, 0.7f, 0.3f }, 1f, 0.05f);
        }

        static GoodAmount[] In(params object[] pairs) => Pairs(pairs);
        static GoodAmount[] Out(params object[] pairs) => Pairs(pairs);

        static GoodAmount[] Pairs(object[] pairs)
        {
            var r = new GoodAmount[pairs.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new GoodAmount((GoodType)pairs[2 * i], (float)pairs[2 * i + 1]);
            return r;
        }

        static Recipe Add(string id, string name, FirmRole role, IndustryForm form, GoodAmount[] inputs, GoodAmount[] outputs,
                          string deposit, float minDeposit, float[] workforce, float power, float water)
        {
            var r = new Recipe
            {
                Index = all.Count, Id = id, Name = name, Role = role, Form = form,
                Inputs = inputs ?? new GoodAmount[0], Outputs = outputs ?? new GoodAmount[0],
                Deposit = deposit, MinDeposit = minDeposit, Workforce = workforce, PowerPerJob = power, WaterPerJob = water,
            };
            all.Add(r);
            byId[id] = r;
            return r;
        }

        public static Recipe Get(string id) => id != null && byId.TryGetValue(id, out var r) ? r : null;

        public static string DisplayName(FirmRole r)
        {
            switch (r)
            {
                case FirmRole.Extraction: return "Rohstoff-Gewinnung";
                case FirmRole.Processing: return "Verarbeitung";
                case FirmRole.Manufacturing: return "Fertigung";
                case FirmRole.Retail: return "Einzelhandel";
                default: return "Dienstleistung/Büro";
            }
        }

        public static string DisplayName(IndustryForm f)
        {
            switch (f)
            {
                case IndustryForm.Agriculture: return "Landwirtschaft";
                case IndustryForm.Forestry: return "Forstwirtschaft";
                case IndustryForm.Mining: return "Bergbau";
                case IndustryForm.Oil: return "Ölförderung";
                case IndustryForm.Processing: return "Verarbeitung";
                case IndustryForm.Manufacturing: return "Fertigung";
                default: return "–";
            }
        }
    }
}
