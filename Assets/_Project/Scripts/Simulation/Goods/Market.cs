using System;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>Mengen und Werte eines Gutes an einem Tag.</summary>
    public sealed class MarketDay
    {
        public float Produced;
        /// <summary>Von Firmen verbraucht (Rezept-Eingänge).</summary>
        public float Consumed;
        /// <summary>Von Haushalten gekauft bzw. gewünscht.</summary>
        public float HouseholdBought;
        public float HouseholdWanted;
        /// <summary>Lokal zwischen Firmen gehandelt (abgeschickt).</summary>
        public float LocalTraded;
        public float Imported;
        public float Exported;
        public double ImportValue;
        public double ExportValue;
        /// <summary>Verdorben.</summary>
        public float Spoiled;

        internal void Clear()
        {
            Produced = Consumed = HouseholdBought = HouseholdWanted = LocalTraded = Imported = Exported = Spoiled = 0f;
            ImportValue = ExportValue = 0;
        }

        internal void CopyFrom(MarketDay o)
        {
            Produced = o.Produced; Consumed = o.Consumed; HouseholdBought = o.HouseholdBought; HouseholdWanted = o.HouseholdWanted;
            LocalTraded = o.LocalTraded; Imported = o.Imported; Exported = o.Exported; Spoiled = o.Spoiled;
            ImportValue = o.ImportValue; ExportValue = o.ExportValue;
        }
    }

    /// <summary>
    /// Markt eines Gutes in der Stadt: lokaler Preis aus Angebot/Nachfrage (Bestand gegen Zielbestand, träge,
    /// begrenzt durch die Import-/Exportparität zum Weltmarkt) und Weltmarktpreis an den Außenanschlüssen (Grundpreis, leicht schwankend über deterministisches Rauschen).
    /// </summary>
    public sealed class GoodMarket
    {
        internal GoodMarket(GoodType good)
        {
            Good = good;
            LocalPrice = good.BasePrice;
            WorldPrice = good.BasePrice;
        }

        public GoodType Good { get; }
        /// <summary>Lokaler Großhandelspreis (€ je Einheit).</summary>
        public float LocalPrice { get; internal set; }
        /// <summary>Weltmarktpreis an den Außenanschlüssen (€ je Einheit, ohne Fracht).</summary>
        public float WorldPrice { get; internal set; }
        /// <summary>Bestand in der Stadt: Ausgangslager der Hersteller + Eingangslager/Regale der Abnehmer.</summary>
        public float Stock { get; internal set; }
        /// <summary>Zielbestand = <see cref="GoodsRules.TargetStockDays"/> × Bedarf je Tag.</summary>
        public float TargetStock { get; internal set; }
        /// <summary>Geglätteter Bedarf je Tag (Rezept-Eingänge bei voller Belegschaft + Haushaltswünsche).</summary>
        public float DemandPerDay { get; internal set; }
        /// <summary>Geglättete Produktion je Tag.</summary>
        public float SupplyPerDay { get; internal set; }
        /// <summary>Geplante Kapazität (Ausgänge bei voller Belegschaft aller Firmen, auch im Bau).</summary>
        public float CapacityPerDay { get; internal set; }
        /// <summary>Geplanter Bedarf (Eingänge bei voller Belegschaft + geschätzter Haushaltskonsum).</summary>
        public float PlannedDemandPerDay { get; internal set; }

        public MarketDay Today { get; } = new MarketDay();
        public MarketDay Yesterday { get; } = new MarketDay();
        /// <summary>Summe seit Spielbeginn.</summary>
        public double TotalImportValue { get; internal set; }
        public double TotalExportValue { get; internal set; }
        public float TotalImported { get; internal set; }
        public float TotalExported { get; internal set; }

        /// <summary>Verhältnis lokal/Grundpreis (1 = normal, &gt; 1 = knapp).</summary>
        public float PriceIndex => LocalPrice / Good.BasePrice;

        /// <summary>
        /// Zielpreis aus Bestand und Zielbestand: Grundpreis × clamp(1 + Empfindlichkeit × (1 − (Bestand+1)/(Ziel+1))).
        /// Ohne Bedarf und ohne Bestand neutral.
        /// </summary>
        public static float TargetPrice(GoodType g, float stock, float target)
        {
            float ratio = (stock + 1f) / (target + 1f);
            float f = 1f + GoodsRules.PriceSensitivity * (1f - ratio);
            return g.BasePrice * Math.Clamp(f, GoodsRules.MinPriceFactor, GoodsRules.MaxPriceFactor);
        }

        /// <summary>Tageswechsel: Glättung, Zielbestand, Preis träge anpassen.</summary>
        internal void CloseDay(float stock, float demandToday, float worldPrice)
        {
            Stock = stock;
            float w = GoodsRules.EmaWeight;
            DemandPerDay = DemandPerDay <= 0f ? demandToday : DemandPerDay + (demandToday - DemandPerDay) * w;
            SupplyPerDay = SupplyPerDay <= 0f ? Today.Produced : SupplyPerDay + (Today.Produced - SupplyPerDay) * w;
            TargetStock = DemandPerDay * GoodsRules.TargetStockDays;
            float target = TargetPrice(Good, Stock, TargetStock);
            LocalPrice += (target - LocalPrice) * GoodsRules.PriceInertia;
            WorldPrice = worldPrice;
            // Weltmarkt-Parität: billiger als Export lohnt nicht zu verkaufen, teurer als Import nicht zu kaufen
            LocalPrice = Math.Clamp(LocalPrice, WorldPrice * (1f - GoodsRules.ParityBelow), WorldPrice * (1f + GoodsRules.ParityAbove));
            TotalImportValue += Today.ImportValue;
            TotalExportValue += Today.ExportValue;
            TotalImported += Today.Imported;
            TotalExported += Today.Exported;
            Yesterday.CopyFrom(Today);
            Today.Clear();
        }

        /// <summary>
        /// Weltmarktpreis am Tag: Grundpreis × (1 + Schwankung × Rauschen). Rauschen = geglättete Zufallswerte je
        /// <see cref="GoodsRules.WorldNoisePeriodDays"/> Tage aus einem stabilen Hash (deterministisch, ohne Zufallsstrom).
        /// </summary>
        public static float WorldPriceOn(GoodType g, int day, int seed)
        {
            int period = GoodsRules.WorldNoisePeriodDays;
            int k = day / period;
            float t = (day - k * period) / (float)period;
            float a = Noise(seed, g.Index, k), b = Noise(seed, g.Index, k + 1);
            float s = t * t * (3f - 2f * t); // weich
            float n = a + (b - a) * s;
            return g.BasePrice * (1f + GoodsRules.WorldVolatility * n);
        }

        static float Noise(int seed, int good, int k) =>
            (SimRandom.Hash(seed * 31 + good, k) & 0xFFFF) / 32767.5f - 1f;
    }

    /// <summary>Import/Export-Bilanz der Stadt (Summe aller Güter).</summary>
    public sealed class TradeBalance
    {
        public double ImportValue;
        public double ExportValue;
        public float ImportedUnits;
        public float ExportedUnits;
        /// <summary>Export − Import (positiv = Überschuss).</summary>
        public double Net => ExportValue - ImportValue;
    }
}
