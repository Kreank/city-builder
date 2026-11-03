using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Abbildung der 19 G5-Güter auf die 8 Güterkategorien der Länder (Etappe 5) – die Brücke zwischen der detaillierten
    /// Stadt und den aggregierten Volkswirtschaften (LOD).
    /// <list type="bullet">
    /// <item>Lebensmittel/Agrar: Getreide, Vieh, Mehl, Lebensmittel</item>
    /// <item>Holz: Holz, Bretter</item>
    /// <item>Erze/Baustoffe: Erz, Sand/Kies, Zement, Baustoffe</item>
    /// <item>Energie: Kohle, Erdöl</item>
    /// <item>Industriegüter: Stahl, Kunststoff, Glas</item>
    /// <item>Konsumgüter: Kleidung, Möbel</item>
    /// <item>Technik: Elektronik, Fahrzeuge</item>
    /// <item>Tourismus: kein Gut (Dienstleistung, nicht handelbar)</item>
    /// </list>
    /// <b>Mengen:</b> eine Wareneinheit entspricht <see cref="CategoryUnitsPerGood"/> = Grundpreis Gut / Grundpreis Kategorie
    /// Kategorie-Einheiten (wertgleich: 1 Getreide à 40 € = 20 Agrar-Einheiten à 2 €). <b>Preise:</b> Preis eines Gutes in einem
    /// Land = Grundpreis Gut × Preisindex der Kategorie dort (Preis/Grundpreis) × (1 ± <see cref="TradeRules.GoodPriceSpread"/>).
    /// </summary>
    public static class TradeGoods
    {
        static readonly GoodsCategory[] category;
        static readonly double[] unitsPerGood;
        static readonly double[] freightTons;
        static readonly List<GoodType>[] byCategory;
        /// <summary>Mittlere Frachttonnen je Kategorie-Einheit (für den Handel der Länder untereinander).</summary>
        static readonly double[] categoryTonsPerUnit;

        /// <summary>Alle handelbaren Güter (alle 19, in Katalogreihenfolge).</summary>
        public static IReadOnlyList<GoodType> Tradeable => GoodsCatalog.All;

        static TradeGoods()
        {
            int n = GoodsCatalog.Count;
            category = new GoodsCategory[n];
            unitsPerGood = new double[n];
            freightTons = new double[n];
            byCategory = new List<GoodType>[GoodsCategories.Count];
            for (int c = 0; c < byCategory.Length; c++) byCategory[c] = new List<GoodType>();
            foreach (var g in GoodsCatalog.All)
            {
                var c = Map(g);
                category[g.Index] = c;
                unitsPerGood[g.Index] = g.BasePrice / GoodsCategories.BasePrice[(int)c];
                freightTons[g.Index] = Math.Max(g.Weight, g.Volume * TradeRules.VolumeTonsPerCubicMeter);
                byCategory[(int)c].Add(g);
            }
            categoryTonsPerUnit = new double[GoodsCategories.Count];
            for (int c = 0; c < byCategory.Length; c++)
            {
                double s = 0;
                foreach (var g in byCategory[c]) s += freightTons[g.Index] / unitsPerGood[g.Index];
                categoryTonsPerUnit[c] = byCategory[c].Count > 0 ? s / byCategory[c].Count : 0;
            }
        }

        static GoodsCategory Map(GoodType g)
        {
            switch (g.Id)
            {
                case "grain": case "livestock": case "flour": case "food": return GoodsCategory.Food;
                case "wood": case "planks": return GoodsCategory.Wood;
                case "ore": case "sand": case "cement": case "buildingmaterials": return GoodsCategory.Minerals;
                case "coal": case "oil": return GoodsCategory.Energy;
                case "steel": case "plastic": case "glass": return GoodsCategory.Industrial;
                case "clothing": case "furniture": return GoodsCategory.Consumer;
                case "electronics": case "vehicles": return GoodsCategory.Technology;
                default:
                    // Neue Güter: nach Stufe einsortieren (Rohstoff → Erze, Zwischenprodukt → Industrie, Endprodukt → Konsum)
                    return g.Tier == GoodTier.Raw ? GoodsCategory.Minerals : g.Tier == GoodTier.Intermediate ? GoodsCategory.Industrial : GoodsCategory.Consumer;
            }
        }

        public static GoodsCategory CategoryOf(GoodType g) => category[g.Index];

        /// <summary>Kategorie-Einheiten je Wareneinheit (wertgleich zu Grundpreisen).</summary>
        public static double CategoryUnitsPerGood(GoodType g) => unitsPerGood[g.Index];

        /// <summary>Frachttonnen je Wareneinheit: max(Gewicht, Volumen × 0,4).</summary>
        public static double FreightTons(GoodType g) => freightTons[g.Index];

        /// <summary>Mittlere Frachttonnen je Kategorie-Einheit.</summary>
        public static double CategoryFreightTons(GoodsCategory c) => categoryTonsPerUnit[(int)c];

        /// <summary>Güter einer Kategorie (Tourismus: leer).</summary>
        public static IReadOnlyList<GoodType> GoodsOf(GoodsCategory c) => byCategory[(int)c];

        /// <summary>Kategorie hat handelbare Güter (alle außer Tourismus).</summary>
        public static bool IsTradeable(GoodsCategory c) => byCategory[(int)c].Count > 0;

        /// <summary>Stabile Preisstreuung eines Gutes in einem Land (± <see cref="TradeRules.GoodPriceSpread"/>).</summary>
        public static double PriceOffset(int seed, int country, GoodType g)
        {
            uint h = SimRandom.Hash(seed * 7919 + country, 1013 + g.Index);
            return ((h & 0xFFFF) / 32767.5 - 1.0) * TradeRules.GoodPriceSpread;
        }

        /// <summary>Welche Kategorien eine Stärke des Länderprofils trägt (Spezialisierung → Exportneigung).</summary>
        public static bool Supports(Strength s, GoodsCategory c)
        {
            switch (s)
            {
                case Strength.Agriculture: return c == GoodsCategory.Food;
                case Strength.RawMaterials: return c == GoodsCategory.Wood || c == GoodsCategory.Minerals || c == GoodsCategory.Energy;
                case Strength.Industry: return c == GoodsCategory.Industrial || c == GoodsCategory.Consumer || c == GoodsCategory.Minerals;
                case Strength.Technology: return c == GoodsCategory.Technology || c == GoodsCategory.Consumer;
                default: return c == GoodsCategory.Tourism;
            }
        }
    }
}
