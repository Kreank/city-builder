using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Grobe Güter-Kategorien der aggregierten Länder-Volkswirtschaften (LOD). Der feine Güterkatalog der
    /// Stadt (G5) wird später auf diese Kategorien abgebildet.
    /// </summary>
    public enum GoodsCategory { Food, Wood, Minerals, Energy, Industrial, Consumer, Technology, Tourism }

    public static class GoodsCategories
    {
        public const int Count = 8;

        public static string DisplayName(GoodsCategory c)
        {
            switch (c)
            {
                case GoodsCategory.Food: return "Lebensmittel/Agrar";
                case GoodsCategory.Wood: return "Holz";
                case GoodsCategory.Minerals: return "Erze/Baustoffe";
                case GoodsCategory.Energy: return "Energie (Kohle/Öl)";
                case GoodsCategory.Industrial: return "Industriegüter";
                case GoodsCategory.Consumer: return "Konsumgüter";
                case GoodsCategory.Technology: return "Technik";
                default: return "Tourismus";
            }
        }

        /// <summary>Grundpreis € je Einheit (Platzhalter fürs Balancing).</summary>
        public static readonly double[] BasePrice = { 2.0, 1.5, 1.2, 3.0, 5.0, 8.0, 20.0, 10.0 };
        /// <summary>Haushaltsbedarf je Einwohner und Tag (Einheiten, ohne Heizen/Kühlen).</summary>
        public static readonly double[] PerCapitaDemand = { 1.0, 0.05, 0.02, 0.5, 0.1, 0.5, 0.08, 0.04 };
    }

    /// <summary>
    /// Aggregierte Volkswirtschaft eines Landes (Detailstufe „fern“): Bevölkerung, Produktion/Bedarf/Lager/Preis
    /// je Güter-Kategorie, täglicher Schritt. Produktivität aus dem Stärken-Profil und den Bodenschätzen,
    /// Ernte und Heizbedarf aus dem Klima der Hauptstadt. Vorleistungen (Erz+Energie → Industrie → Konsum/Technik)
    /// begrenzen die Produktion. Preis passt sich träge an (Lager gegen Zielbestand). Grundlage der späteren Handels-KI:
    /// <see cref="Balance"/> = Überschuss (+) bzw. Defizit (−) je Tag.
    /// </summary>
    public sealed partial class NationEconomy
    {
        public const int TargetStockDays = 20;
        public const int MaxStockDays = 120;
        const double PriceAdjust = 0.03;

        /// <summary>Vorleistungen je Einheit Ausgabe: (Ausgabe, Eingang, Menge).</summary>
        static readonly (GoodsCategory output, GoodsCategory input, double amount)[] Inputs =
        {
            (GoodsCategory.Industrial, GoodsCategory.Minerals, 1.2), (GoodsCategory.Industrial, GoodsCategory.Energy, 0.6),
            (GoodsCategory.Industrial, GoodsCategory.Wood, 0.3),
            (GoodsCategory.Consumer, GoodsCategory.Industrial, 0.5), (GoodsCategory.Consumer, GoodsCategory.Energy, 0.2),
            (GoodsCategory.Technology, GoodsCategory.Industrial, 0.4), (GoodsCategory.Technology, GoodsCategory.Energy, 0.3),
            (GoodsCategory.Tourism, GoodsCategory.Food, 0.2), (GoodsCategory.Tourism, GoodsCategory.Energy, 0.1),
        };

        /// <summary>Vorleistungen (Ausgabe, Eingang, Menge je Einheit) – für die Welt-Kalibrierung.</summary>
        internal static IReadOnlyList<(GoodsCategory output, GoodsCategory input, double amount)> InputTable => Inputs;

        static readonly GoodsCategory[] ProductionOrder =
        {
            GoodsCategory.Food, GoodsCategory.Wood, GoodsCategory.Minerals, GoodsCategory.Energy,
            GoodsCategory.Industrial, GoodsCategory.Consumer, GoodsCategory.Technology, GoodsCategory.Tourism,
        };

        public Country Country { get; }
        public ClimateModel Climate { get; }
        public double Population { get; private set; }
        /// <summary>Höchstleistung je Einwohner und Tag (ohne Klima/Vorleistungen).</summary>
        public readonly double[] CapacityPerCapita = new double[GoodsCategories.Count];
        public readonly double[] Production = new double[GoodsCategories.Count];
        /// <summary>Gesamtbedarf (Haushalte + Vorleistungen) je Tag.</summary>
        public readonly double[] Demand = new double[GoodsCategories.Count];
        public readonly double[] Consumption = new double[GoodsCategories.Count];
        public readonly double[] Stock = new double[GoodsCategories.Count];
        public readonly double[] Price = new double[GoodsCategories.Count];
        /// <summary>Was über das Höchstlager hinaus anfiel (Exportpotenzial) – Summe seit Beginn.</summary>
        public readonly double[] Overflow = new double[GoodsCategories.Count];

        /// <summary>Anteil des gedeckten Haushaltsbedarfs je Kategorie (0..1).</summary>
        public readonly double[] Satisfaction = new double[GoodsCategories.Count];

        public double Gdp { get; private set; }
        public int LastDay { get; private set; } = -1;

        public double Balance(GoodsCategory c) => Production[(int)c] - Demand[(int)c];

        internal NationEconomy(Country country, ClimateModel climate, double population, float wood, float minerals, float energy)
        {
            Country = country;
            Climate = climate;
            Population = population;
            var pr = country.Profile.Raw;
            double agri = pr[(int)Strength.Agriculture], ind = pr[(int)Strength.Industry], tech = pr[(int)Strength.Technology];
            double tour = pr[(int)Strength.Tourism];
            CapacityPerCapita[(int)GoodsCategory.Food] = 1.15 * (0.45 + 1.2 * agri) / Math.Max(0.25, climate.AnnualHarvestFactor);
            CapacityPerCapita[(int)GoodsCategory.Wood] = 0.08 * (0.2 + 1.6 * wood);
            CapacityPerCapita[(int)GoodsCategory.Minerals] = 0.25 * (0.2 + 1.6 * minerals);
            CapacityPerCapita[(int)GoodsCategory.Energy] = 0.75 * (0.25 + 1.5 * energy);
            CapacityPerCapita[(int)GoodsCategory.Industrial] = 0.22 * (0.4 + 1.2 * ind);
            CapacityPerCapita[(int)GoodsCategory.Consumer] = 0.55 * (0.4 + 0.6 * ind + 0.6 * tech);
            CapacityPerCapita[(int)GoodsCategory.Technology] = 0.09 * (0.3 + 1.4 * tech);
            CapacityPerCapita[(int)GoodsCategory.Tourism] = 0.05 * (0.3 + 1.4 * tour);
            // Start: Lager für 20 Tage, Preis spiegelt Knappheit (komparative Vorteile sofort sichtbar)
            for (int c = 0; c < GoodsCategories.Count; c++)
            {
                double demand = population * GoodsCategories.PerCapitaDemand[c];
                double cap = population * CapacityPerCapita[c];
                Stock[c] = demand * TargetStockDays;
                Price[c] = GoodsCategories.BasePrice[c] * Math.Clamp(Math.Sqrt(Math.Max(1e-6, demand) / Math.Max(1e-6, cap)), 0.4, 2.5);
                Satisfaction[c] = 1.0;
            }
        }

        internal void CalibrateCapacity(int c, double scale) => CapacityPerCapita[c] *= scale;

        /// <summary>Startpreise nach der Kalibrierung neu aus Bedarf/Kapazität (wie im Konstruktor).</summary>
        internal void ResetStartPrices()
        {
            for (int c = 0; c < GoodsCategories.Count; c++)
            {
                double demand = Population * GoodsCategories.PerCapitaDemand[c];
                double cap = Population * CapacityPerCapita[c];
                Price[c] = GoodsCategories.BasePrice[c] * Math.Clamp(Math.Sqrt(Math.Max(1e-6, demand) / Math.Max(1e-6, cap)), 0.4, 2.5);
            }
        }

        /// <summary>Ein Spieltag.</summary>
        public void TickDay(int day)
        {
            var w = Climate.On(day);
            Array.Clear(Demand, 0, Demand.Length);
            Array.Clear(Production, 0, Production.Length);

            // Haushaltsbedarf (Energie mit Heizen/Kühlen)
            var household = new double[GoodsCategories.Count];
            for (int c = 0; c < GoodsCategories.Count; c++) household[c] = Population * GoodsCategories.PerCapitaDemand[c];
            household[(int)GoodsCategory.Energy] += Population * (0.8 * w.Heating + 0.3 * w.Cooling);

            // Produktion in Kettenreihenfolge, Vorleistungen aus dem Lager.
            // Etappe 8 (Kalibrierung): Teilen sich mehrere Abnehmer eine Vorleistung (Industriegüter → Konsum und Technik),
            // bekommt jeder seinen Anteil am Lager (Lager / noch offener Gesamtbedarf) – vorher nahm der erste in der
            // Reihenfolge alles und Technik stand weltweit still.
            var potentials = new double[GoodsCategories.Count];
            var openNeed = new double[GoodsCategories.Count];
            foreach (var cat in ProductionOrder)
            {
                int c = (int)cat;
                double potential = Population * CapacityPerCapita[c];
                if (cat == GoodsCategory.Food) potential *= w.Harvest;
                if (cat == GoodsCategory.Tourism) potential *= 0.6 + 0.6 * (w.Season == Season.Summer ? 1.0 : 0.4);
                potentials[c] = potential;
                foreach (var (o, input, amount) in Inputs)
                    if (o == cat) openNeed[(int)input] += potential * amount;
            }
            foreach (var cat in ProductionOrder)
            {
                int c = (int)cat;
                double potential = potentials[c];
                double factor = 1.0;
                foreach (var (o, input, amount) in Inputs)
                {
                    if (o != cat) continue;
                    double need = potential * amount;
                    Demand[(int)input] += need;
                    if (need > 0) factor = Math.Min(factor, Stock[(int)input] / Math.Max(need, openNeed[(int)input]));
                    openNeed[(int)input] -= need;
                }
                factor = Math.Clamp(factor, 0.0, 1.0);
                double produced = potential * factor;
                foreach (var (o, input, amount) in Inputs)
                    if (o == cat) Stock[(int)input] = Math.Max(0.0, Stock[(int)input] - produced * amount);
                Production[c] = produced;
                Stock[c] += produced;
            }

            Gdp = 0;
            for (int c = 0; c < GoodsCategories.Count; c++)
            {
                Demand[c] += household[c];
                double used = Math.Min(household[c], Stock[c]);
                Stock[c] -= used;
                Consumption[c] = used;
                Satisfaction[c] = household[c] > 0 ? used / household[c] : 1.0;
                // Verderb/Schwund, Höchstlager
                Stock[c] *= c == (int)GoodsCategory.Food ? 0.995 : 0.999;
                double max = Math.Max(1.0, Demand[c]) * MaxStockDays;
                if (Stock[c] > max) { Overflow[c] += Stock[c] - max; Stock[c] = max; }
                // Preis: träge gegen den Zielbestand
                double target = Math.Max(1.0, Demand[c]) * TargetStockDays;
                double gap = Math.Clamp((target - Stock[c]) / target, -1.0, 1.0);
                double basePrice = GoodsCategories.BasePrice[c];
                Price[c] = Math.Clamp(Price[c] * Math.Exp(PriceAdjust * gap), basePrice * 0.25, basePrice * 4.0);
                Gdp += Production[c] * Price[c];
            }

            // Bevölkerung: wächst bei gedeckter Ernährung, schrumpft bei Hunger (sehr langsam)
            double food = Satisfaction[(int)GoodsCategory.Food];
            double rate = Math.Clamp((food - 0.85) / 0.15, -1.0, 1.0) * 0.00004;
            Population *= 1.0 + rate;
            LastDay = day;
        }
    }

    /// <summary>Alle Länder-Volkswirtschaften der Welt (LOD „aggregiert“), täglicher Schritt über <see cref="WorldSystem"/>.</summary>
    public sealed partial class WorldEconomy
    {
        public IReadOnlyList<NationEconomy> Nations => nations;
        readonly List<NationEconomy> nations = new List<NationEconomy>();

        public int DaysSimulated { get; private set; }

        internal WorldEconomy(Planet p)
        {
            int cc = p.Countries.Count;
            var pop = new double[cc];
            var wood = new float[cc]; var minerals = new float[cc]; var energy = new float[cc]; var area = new float[cc];
            for (int i = 0; i < p.CellCount; i++)
            {
                if (!p.IsLand(i)) continue;
                int c = p.Regions[p.RegionOf[i]].CountryId;
                float a = p.CellAreaKm2(p.YOf(i));
                float t = p.Temperature[i];
                float habitable = WorldNoise.Clamp01(1f - MathF.Abs(t - 16f) / 26f) * (p.BiomeOf[i] == Biome.Ice ? 0f : 1f);
                // Einwohner je km²: Grundlast + fruchtbare Böden + Küste + Flüsse
                double density = habitable * (2.0 + 110.0 * p.Fertility[i] + (p.CoastDistance[i] == 1 ? 25.0 : 0.0) + (p.IsRiver(i) ? 35.0 : 0.0));
                pop[c] += density * a;
                area[c] += a;
                wood[c] += p.Forest[i] * a;
                minerals[c] += 0.7f * p.Ore[i] * a + 0.3f * p.Sand[i] * a;
                energy[c] += 0.5f * (p.Coal[i] + p.Oil[i]) * a;
            }
            for (int c = 0; c < cc; c++)
            {
                var country = p.Countries[c];
                float inv = area[c] > 0f ? 1f / area[c] : 0f;
                var climate = new ClimateModel(ClimateParams.FromCell(p, country.CapitalCell), p.Seed * 31 + c);
                var e = new NationEconomy(country, climate, Math.Max(10000.0, pop[c]),
                    WorldNoise.Clamp01(wood[c] * inv * 1.3f), WorldNoise.Clamp01(minerals[c] * inv * 2.5f), WorldNoise.Clamp01(energy[c] * inv * 3f));
                country.Economy = e;
                nations.Add(e);
            }
            Calibrate();
        }

        /// <summary>
        /// Etappe 8: Kapazität je Kategorie weltweit skalieren, sodass die Welt im Mittel ausgeglichen ist (Σ Kapazität ≈ Σ Bedarf
        /// × Zuschlag für Verderb/Schwund). Länderunterschiede (Stärken, Vorkommen, Klima) bleiben relativ erhalten → einzelne Länder
        /// haben Überschuss bzw. Defizit, das der Handel ausgleicht. Reihenfolge rückwärts durch die Kette: erst Endprodukte gegen
        /// den Haushaltsbedarf, dann Industriegüter gegen Haushalte + Vorleistungen, dann Rohstoffe. Heizen/Kühlen und Jahreszeiten
        /// (Ernte, Tourismus-Saison) gehen mit ihrem Jahresmittel ein.
        /// </summary>
        void Calibrate()
        {
            int k = GoodsCategories.Count;
            var household = new double[k];
            var meanFactor = new double[nations.Count, k];
            for (int i = 0; i < nations.Count; i++)
            {
                var n = nations[i];
                double heat = 0, cool = 0, tour = 0, harvest = 0;
                const int samples = 52;
                for (int s = 0; s < samples; s++)
                {
                    var w = n.Climate.On(s * 7);
                    heat += w.Heating; cool += w.Cooling; harvest += w.Harvest;
                    tour += 0.6 + 0.6 * (w.Season == Season.Summer ? 1.0 : 0.4);
                }
                heat /= samples; cool /= samples; tour /= samples; harvest /= samples;
                for (int c = 0; c < k; c++)
                {
                    household[c] += n.Population * GoodsCategories.PerCapitaDemand[c];
                    meanFactor[i, c] = 1.0;
                }
                household[(int)GoodsCategory.Energy] += n.Population * (0.8 * heat + 0.3 * cool);
                meanFactor[i, (int)GoodsCategory.Food] = harvest;
                meanFactor[i, (int)GoodsCategory.Tourism] = tour;
            }
            // rückwärts durch die Kette (Ausgaben vor ihren Eingängen)
            var order = new[]
            {
                GoodsCategory.Tourism, GoodsCategory.Technology, GoodsCategory.Consumer, GoodsCategory.Industrial,
                GoodsCategory.Energy, GoodsCategory.Minerals, GoodsCategory.Wood, GoodsCategory.Food,
            };
            var demand = (double[])household.Clone();
            foreach (var cat in order)
            {
                int c = (int)cat;
                double capacity = 0;
                for (int i = 0; i < nations.Count; i++) capacity += nations[i].Population * nations[i].CapacityPerCapita[c] * meanFactor[i, c];
                double target = demand[c] * (cat == GoodsCategory.Food ? CalibrationFood : CalibrationMargin);
                double scale = capacity > 0 ? target / capacity : 1.0;
                for (int i = 0; i < nations.Count; i++) nations[i].CalibrateCapacity(c, scale);
                // Vorleistungen dieser (jetzt kalibrierten) Produktion erhöhen den Bedarf der Eingänge
                foreach (var (o, input, amount) in NationEconomy.InputTable)
                    if (o == cat) demand[(int)input] += target * amount;
            }
            for (int i = 0; i < nations.Count; i++) nations[i].ResetStartPrices();
        }

        /// <summary>Zielverhältnis Welt-Kapazität / Welt-Bedarf (Lebensmittel verderben 0,5 %/Tag im Lager, alles andere 0,1 %).</summary>
        public const double CalibrationMargin = 1.04, CalibrationFood = 1.12;

        public void TickDay(int day)
        {
            foreach (var n in nations) n.TickDay(day);
            DaysSimulated++;
        }

        /// <summary>Weltmarktpreis-Schätzung je Kategorie: produktionsgewichtetes Mittel der Länderpreise.</summary>
        public double WorldPrice(GoodsCategory c)
        {
            double sum = 0, w = 0;
            foreach (var n in nations)
            {
                double prod = Math.Max(1.0, n.Production[(int)c]);
                sum += n.Price[(int)c] * prod;
                w += prod;
            }
            return w > 0 ? sum / w : GoodsCategories.BasePrice[(int)c];
        }

        public double TotalPopulation
        {
            get { double s = 0; foreach (var n in nations) s += n.Population; return s; }
        }
    }
}
