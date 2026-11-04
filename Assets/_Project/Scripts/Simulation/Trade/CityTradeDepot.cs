using System;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Grenzlager der Spielerstadt am Außenanschluss (Etappe 8) – Vertragspartei auf Spielerseite ist die <b>Stadtkasse</b>:
    /// <list type="bullet">
    /// <item><b>Import:</b> Lieferung kommt nach der Lieferzeit an der Grenze an → Grenzlager; die Kasse zahlt dem Lieferland den
    /// Vertragspreis. Firmen beziehen die Ware wie einen Import (Lkw ab Außenanschluss) und zahlen Vertragspreis + Zoll gegen
    /// das Lieferland an die Kasse („Handel: Verkauf“ bzw. „Zölle“).</item>
    /// <item><b>Export:</b> Firmen liefern in offene Exportverträge (Lkw zum Außenanschluss) und bekommen von der Kasse den
    /// Vertragspreis; am Versandtag geht die Ware aus dem Grenzlager an das Käuferland (Fernfracht zahlt die Kasse),
    /// das bei Ankunft zahlt. Fehlt Ware → Vertragsstrafe aus der Kasse.</item>
    /// <item><b>Zoll</b> des Spielers (Grundzoll) verteuert Weltmarkt-Importe der Firmen und geht an die Kasse.</item>
    /// <item><b>Überhang:</b> Importware, die 14 Tage nach der letzten Ankunft noch liegt, bzw. Exportware ohne offenen Vertrag wird
    /// zu 85 % des Weltpreises verkauft (Kasse). Verderbliche Importware verdirbt wie im Firmenlager.</item>
    /// </list>
    /// Zustand in klaren Feldern je Gut (für Speichern/Laden).
    /// </summary>
    public sealed partial class CityTradeDepot : ITradeGateway
    {
        readonly TradeSystem trade;
        readonly GoodsSystem goods;
        readonly EconomySystem treasury;

        /// <summary>Vertragsware im Grenzlager je Gut (Einheiten) und ihr Wert: Vertragspreis bzw. Zollanteil.</summary>
        public readonly double[] ImportStock = new double[GoodsCatalog.Count];
        public readonly double[] ImportCost = new double[GoodsCatalog.Count];
        public readonly double[] ImportTariff = new double[GoodsCatalog.Count];
        /// <summary>Exportware im Grenzlager bzw. auf dem Weg dorthin (Lkw der Firmen).</summary>
        public readonly double[] ExportStock = new double[GoodsCatalog.Count];
        public readonly double[] ExportEnRoute = new double[GoodsCatalog.Count];
        /// <summary>Handelstag der letzten Import-Ankunft je Gut.</summary>
        public readonly int[] LastImportDay = new int[GoodsCatalog.Count];
        /// <summary>Unverkaufte Importware geht so viele Tage nach der letzten Ankunft an den Weltmarkt.</summary>
        public const int DumpAfterDays = 14;

        /// <summary>Summen seit Beginn (Info): Zölle, an Firmen verkauft, Firmen bezahlt, Restverkauf.</summary>
        public double TariffsTotal { get; private set; }
        public double SalesToFirms { get; private set; }
        public double PaidToFirms { get; private set; }
        public double DumpedValue { get; private set; }
        public double FreightPaid { get; private set; }

        internal CityTradeDepot(TradeSystem trade, GoodsSystem goods, EconomySystem treasury)
        {
            this.trade = trade;
            this.goods = goods;
            this.treasury = treasury;
        }

        public GoodsSystem Goods => goods;
        int Player => trade.PlayerId;

        internal void Post(BudgetCategory c, double amount) => treasury?.Post(c, amount);

        internal void PayFreight(double amount)
        {
            if (!(amount > 0)) return;
            FreightPaid += amount;
            Post(BudgetCategory.TradePurchases, amount);
        }

        // ------------------------------------------------------------ Seite Handel

        internal void ReceiveImport(GoodType g, double units, double unitPrice, double tariffRate)
        {
            int k = g.Index;
            ImportStock[k] += units;
            LastImportDay[k] = trade.Day;
            ImportCost[k] += units * unitPrice;
            ImportTariff[k] += units * unitPrice * tariffRate;
        }

        internal double TakeExport(GoodType g, double units)
        {
            int k = g.Index;
            double s = Math.Min(units, ExportStock[k]);
            ExportStock[k] -= s;
            if (ExportStock[k] < 1e-6) ExportStock[k] = 0;
            return s;
        }

        // ------------------------------------------------------------ ITradeGateway (Seite Warensystem)

        public float ImportAvailable(GoodType g) => (float)ImportStock[g.Index];

        public double ImportUnitPrice(GoodType g)
        {
            int k = g.Index;
            return ImportStock[k] > 1e-9 ? (ImportCost[k] + ImportTariff[k]) / ImportStock[k] : 0.0;
        }

        public double TakeImport(GoodType g, float amount)
        {
            int k = g.Index;
            double stock = ImportStock[k];
            if (stock <= 1e-9 || amount <= 0f) return 0.0;
            double a = Math.Min(amount, stock);
            double share = a / stock;
            double cost = ImportCost[k] * share, tariff = ImportTariff[k] * share;
            ImportStock[k] -= a;
            ImportCost[k] -= cost;
            ImportTariff[k] -= tariff;
            if (ImportStock[k] < 1e-6) { ImportStock[k] = 0; ImportCost[k] = 0; ImportTariff[k] = 0; }
            SalesToFirms += cost;
            TariffsTotal += tariff;
            Post(BudgetCategory.TradeSales, cost);
            Post(BudgetCategory.Tariffs, tariff);
            return cost + tariff;
        }

        public float ExportWanted(GoodType g)
        {
            double due = trade.PlayerExportDue(g, TradeRules.DaysPerWeek);
            return (float)Math.Max(0.0, due - ExportStock[g.Index] - ExportEnRoute[g.Index]);
        }

        public double ExportUnitPrice(GoodType g) => trade.PlayerExportPrice(g);

        public double ExportDispatched(GoodType g, float amount)
        {
            int k = g.Index;
            ExportEnRoute[k] += amount;
            double value = amount * ExportUnitPrice(g);
            PaidToFirms += value;
            Post(BudgetCategory.TradePurchases, value);
            return value;
        }

        public void ExportArrived(GoodType g, float amount)
        {
            int k = g.Index;
            ExportEnRoute[k] = Math.Max(0.0, ExportEnRoute[k] - amount);
            ExportStock[k] += amount;
        }

        /// <summary>Grundzoll des Spielers auf Weltmarkt-Importe (Handel 2: in einer Zollunion der gemeinsame Außenzoll).</summary>
        public double SpotTariffRate(GoodType g) => Player >= 0 ? trade.GeneralTariff(Player) : 0.0;

        /// <summary>Handel 2: reicht die Stadtkasse (Geschenke)? Ohne Kasse immer.</summary>
        internal bool CanAfford(double amount) => treasury == null || treasury.CanAfford(amount);

        public void BookTariff(double amount)
        {
            if (!(amount > 0)) return;
            TariffsTotal += amount;
            Post(BudgetCategory.Tariffs, amount);
        }

        // ------------------------------------------------------------ Lagebild der Stadt (für die NPC-KI)

        /// <summary>Lokaler Großhandelspreis (ohne Warensystem: Weltpreis).</summary>
        public double LocalPrice(GoodType g) => goods != null ? goods.Market(g).LocalPrice : trade.WorldPrice(g);

        /// <summary>Einfuhrbedarf je Tag: Importe gestern bzw. geplanter Bedarf über der Kapazität (Einheiten).</summary>
        public double ImportNeedPerDay(GoodType g)
        {
            if (goods == null) return 0.0;
            var m = goods.Market(g);
            return Math.Max(m.Yesterday.Imported, m.PlannedDemandPerDay - m.CapacityPerDay);
        }

        /// <summary>Ausfuhrpotenzial je Tag: Exporte gestern bzw. Kapazität über dem geplanten Bedarf (Einheiten).</summary>
        public double ExportPotentialPerDay(GoodType g)
        {
            if (goods == null) return 0.0;
            var m = goods.Market(g);
            return Math.Max(m.Yesterday.Exported, m.CapacityPerDay - m.PlannedDemandPerDay);
        }

        /// <summary>Gut der Kategorie mit dem größten Einfuhrbedarf der Stadt (null = kein Bedarf ≥ 1 E/Tag).</summary>
        public GoodType BestImportGood(GoodsCategory c)
        {
            GoodType best = null;
            double bestV = 1.0;
            foreach (var g in TradeGoods.GoodsOf(c))
            {
                double v = ImportNeedPerDay(g);
                if (v > bestV) { bestV = v; best = g; }
            }
            return best;
        }

        /// <summary>Gut der Kategorie mit dem größten Ausfuhrpotenzial der Stadt (null = keins ≥ 1 E/Tag).</summary>
        public GoodType BestExportGood(GoodsCategory c)
        {
            GoodType best = null;
            double bestV = 1.0;
            foreach (var g in TradeGoods.GoodsOf(c))
            {
                double v = ExportPotentialPerDay(g);
                if (v > bestV) { bestV = v; best = g; }
            }
            return best;
        }

        // ------------------------------------------------------------ Tageswechsel

        internal void DayStep()
        {
            foreach (var g in GoodsCatalog.All)
            {
                int k = g.Index;
                // Verderb
                if (g.SpoilPerDay > 0f)
                {
                    double keep = 1.0 - g.SpoilPerDay;
                    ImportStock[k] *= keep; ImportCost[k] *= keep; ImportTariff[k] *= keep;
                    // Exportware liegt nur bis zum nächsten Versandtag (≤ 1 Woche) – kein Verderb, sonst fehlte systematisch Menge
                }
                // Überhang: Importware, die 14 Tage nach der letzten Ankunft noch liegt → Weltmarkt
                if (ImportStock[k] > 1e-6 && trade.Day - LastImportDay[k] >= DumpAfterDays)
                {
                    Dump(g, ImportStock[k]);
                    ImportStock[k] = ImportCost[k] = ImportTariff[k] = 0;
                }
                // Exportware ohne offenen Vertrag → Weltmarkt
                if (ExportStock[k] > 1e-6 && trade.PlayerExportDue(g, 10 * TradeRules.DaysPerWeek) <= 0)
                {
                    Dump(g, ExportStock[k]);
                    ExportStock[k] = 0;
                }
            }
        }

        void Dump(GoodType g, double units)
        {
            double value = units * trade.WorldPrice(g) * TradeRules.DepotDumpPriceShare;
            DumpedValue += value;
            Post(BudgetCategory.TradeSales, value);
        }
    }
}
