using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Schwerpunkt eines Gebiets bzw. Branche der Wirtschaftsförderung („Produktion lenken“). <see cref="Auto"/> = die
    /// Simulation wählt frei (Standard). Reihenfolge = Anzeige; gespeichert wird über den Namen.
    /// </summary>
    public enum IndustryFocus : byte
    {
        Auto = 0,
        /// <summary>Landwirtschaft &amp; Viehzucht (Bauernhof liefert Getreide und Vieh, Mühle).</summary>
        Agriculture,
        /// <summary>Forst (Forstbetrieb, Sägewerk).</summary>
        Forestry,
        /// <summary>Erz (Erzbergwerk).</summary>
        Ore,
        /// <summary>Kohle (Kohlezeche).</summary>
        Coal,
        /// <summary>Öl/Gas (Ölförderung).</summary>
        OilGas,
        /// <summary>Baustoffe (Kiesgrube, Zementwerk, Baustoffwerk).</summary>
        BuildingMaterials,
        /// <summary>Verarbeitung (Stahlwerk, Raffinerie/Kunststoff, Glashütte).</summary>
        Processing,
        /// <summary>Konsumgüter (Lebensmittel, Kleidung, Möbel).</summary>
        ConsumerGoods,
        /// <summary>Technik (Elektronik, Fahrzeuge).</summary>
        Technology,
    }

    /// <summary>Wie ein Rezept zum Schwerpunkt eines Gebiets passt.</summary>
    public enum FocusFit : byte
    {
        /// <summary>Gebiet ohne Schwerpunkt (automatisch).</summary>
        Neutral,
        /// <summary>Kernbetrieb des Schwerpunkts – starker Bonus bei der Standortwahl.</summary>
        Primary,
        /// <summary>Verwandter Betrieb (z. B. Stahlwerk im Erz-Gebiet) – kein Bonus, aber auch kein Abschlag, wechselt nicht.</summary>
        Related,
        /// <summary>Fremder Betrieb – Abschlag bei der Standortwahl, wechselt mit der Zeit den Betrieb.</summary>
        Foreign,
    }

    /// <summary>
    /// Datentabelle der Branchen: welche Rezepte Kern bzw. verwandt sind, welches Vorkommen ein Rohstoff-Schwerpunkt braucht,
    /// Namen für die Anzeige. Jedes Industrie-Rezept hat genau eine Heimat-Branche (<see cref="HomeOf"/>) – dort wird es gefördert.
    /// <para>
    /// Entscheidung „Viehzucht“: Der Rezeptkatalog kennt nur den Bauernhof, der Getreide <b>und</b> Vieh liefert. Ein eigener
    /// Schwerpunkt Viehzucht hätte genau dieselbe Wirkung wie Landwirtschaft – deshalb ein gemeinsamer Schwerpunkt
    /// „Landwirtschaft &amp; Viehzucht“. Kommt später ein eigener Viehbetrieb (Weide, Fruchtbarkeit niedriger), wird er hier ergänzt.
    /// </para>
    /// </summary>
    public static class IndustryBranches
    {
        public const int Count = (int)IndustryFocus.Technology + 1;

        /// <summary>Alle Schwerpunkte ohne <see cref="IndustryFocus.Auto"/> (Anzeige-Reihenfolge).</summary>
        public static readonly IndustryFocus[] Branches =
        {
            IndustryFocus.Agriculture, IndustryFocus.Forestry, IndustryFocus.Ore, IndustryFocus.Coal, IndustryFocus.OilGas,
            IndustryFocus.BuildingMaterials, IndustryFocus.Processing, IndustryFocus.ConsumerGoods, IndustryFocus.Technology,
        };

        static readonly string[][] primaryIds =
        {
            new string[0],
            new[] { "farm", "mill" },
            new[] { "forestry", "sawmill" },
            new[] { "ore_mine" },
            new[] { "coal_mine" },
            new[] { "oil_well" },
            new[] { "quarry", "cementworks", "building_materials" },
            new[] { "steelworks", "refinery", "glassworks" },
            new[] { "food_factory", "textile", "furniture" },
            new[] { "electronics", "vehicle_plant" },
        };

        static readonly string[][] relatedIds =
        {
            new string[0],
            new[] { "food_factory" },
            new[] { "furniture" },
            new[] { "steelworks" },
            new[] { "steelworks", "glassworks", "cementworks" },
            new[] { "refinery" },
            new[] { "glassworks" },
            new[] { "mill", "sawmill", "cementworks" },
            new[] { "mill", "sawmill" },
            new[] { "steelworks", "refinery", "glassworks" },
        };

        static readonly FocusFit[,] fit;      // [Schwerpunkt, Rezept-Index]
        static readonly IndustryFocus[] home; // je Rezept-Index
        static readonly Recipe[][] primary;

        static IndustryBranches()
        {
            int n = RecipeCatalog.All.Count;
            fit = new FocusFit[Count, n];
            home = new IndustryFocus[n];
            primary = new Recipe[Count][];
            for (int f = 1; f < Count; f++)
            {
                for (int r = 0; r < n; r++) fit[f, r] = RecipeCatalog.All[r].IsIndustry ? FocusFit.Foreign : FocusFit.Neutral;
                foreach (var id in relatedIds[f]) fit[f, Need(id).Index] = FocusFit.Related;
                var list = new List<Recipe>();
                foreach (var id in primaryIds[f])
                {
                    var r = Need(id);
                    fit[f, r.Index] = FocusFit.Primary;
                    if (home[r.Index] == IndustryFocus.Auto) home[r.Index] = (IndustryFocus)f;
                    list.Add(r);
                }
                primary[f] = list.ToArray();
            }
            primary[0] = new Recipe[0];
            foreach (var r in RecipeCatalog.Industry)
                if (home[r.Index] == IndustryFocus.Auto) throw new InvalidOperationException($"Rezept {r.Id} hat keine Branche.");
        }

        static Recipe Need(string id) => RecipeCatalog.Get(id) ?? throw new InvalidOperationException($"Rezept {id} fehlt.");

        /// <summary>Passung eines Rezepts zum Schwerpunkt (Auto → <see cref="FocusFit.Neutral"/>; Läden/Büros immer neutral).</summary>
        public static FocusFit Fit(IndustryFocus focus, Recipe r)
        {
            if (focus == IndustryFocus.Auto || r == null || (int)focus >= Count) return FocusFit.Neutral;
            return fit[(int)focus, r.Index];
        }

        /// <summary>Branche, in der ein Industrie-Rezept gefördert wird (Läden/Büros: <see cref="IndustryFocus.Auto"/>).</summary>
        public static IndustryFocus HomeOf(Recipe r) => r == null || r.Index >= home.Length ? IndustryFocus.Auto : home[r.Index];

        /// <summary>Kernbetriebe eines Schwerpunkts.</summary>
        public static IReadOnlyList<Recipe> Primary(IndustryFocus f) => primary[(int)f];

        /// <summary>Vorkommen, das ein Rohstoff-Schwerpunkt braucht (null = Verarbeitung/Fertigung, kein Vorkommen nötig).</summary>
        public static string Deposit(IndustryFocus f)
        {
            switch (f)
            {
                case IndustryFocus.Agriculture: return ResourceIds.Fertility;
                case IndustryFocus.Forestry: return ResourceIds.Wood;
                case IndustryFocus.Ore: return ResourceIds.Ore;
                case IndustryFocus.Coal: return ResourceIds.Coal;
                case IndustryFocus.OilGas: return ResourceIds.Oil;
                case IndustryFocus.BuildingMaterials: return ResourceIds.Sand;
                default: return null;
            }
        }

        /// <summary>Gewinnungs-Rezept eines Rohstoff-Schwerpunkts (null ohne Vorkommen).</summary>
        public static Recipe ExtractionOf(IndustryFocus f)
        {
            foreach (var r in primary[(int)f]) if (r.Deposit != null) return r;
            return null;
        }

        public static string DisplayName(IndustryFocus f)
        {
            switch (f)
            {
                case IndustryFocus.Agriculture: return "Landwirtschaft & Viehzucht";
                case IndustryFocus.Forestry: return "Forst";
                case IndustryFocus.Ore: return "Erz";
                case IndustryFocus.Coal: return "Kohle";
                case IndustryFocus.OilGas: return "Öl/Gas";
                case IndustryFocus.BuildingMaterials: return "Baustoffe";
                case IndustryFocus.Processing: return "Verarbeitung";
                case IndustryFocus.ConsumerGoods: return "Konsumgüter";
                case IndustryFocus.Technology: return "Technik";
                default: return "automatisch";
            }
        }

        /// <summary>Kurzer Name für Kacheln.</summary>
        public static string ShortName(IndustryFocus f) => f == IndustryFocus.Agriculture ? "Landwirtschaft" : f == IndustryFocus.Auto ? "Automatisch" : DisplayName(f);

        /// <summary>Was der Schwerpunkt umfasst (Tooltip).</summary>
        public static string Describe(IndustryFocus f)
        {
            if (f == IndustryFocus.Auto) return "Die Simulation wählt den Betrieb nach Vorkommen und Nachfrage (Standard).";
            var parts = new List<string>();
            foreach (var r in primary[(int)f]) parts.Add(r.Name);
            string s = string.Join(", ", parts);
            var related = new List<string>();
            foreach (var id in relatedIds[(int)f]) related.Add(Need(id).Name);
            if (related.Count > 0) s += " · bleiben auch: " + string.Join(", ", related);
            string dep = Deposit(f);
            if (dep != null) s += $" · braucht {ResourceIds.DisplayName(dep)} im Boden";
            return s;
        }

        /// <summary>Förderstufe als Wort.</summary>
        public static string LevelName(int level)
        {
            switch (level)
            {
                case 1: return "gering";
                case 2: return "mittel";
                case 3: return "stark";
                default: return "keine";
            }
        }
    }
}
