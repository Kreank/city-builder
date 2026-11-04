using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Realm
{
    /// <summary>
    /// Eine eigene Stadt des Spielers (Etappe 7). Genau eine Stadt läuft <b>detailliert</b> (die betrachtete
    /// <see cref="SimulationWorld"/>), alle anderen <b>aggregiert</b> über ihre <see cref="CityEconomy"/>. Wer schon einmal detailliert
    /// gespielt wurde, trägt seinen eingefrorenen Zustand als <see cref="Snapshot"/> (die lokalen Abschnitte eines Spielstands, ohne
    /// Welt/Reich/Planet/Handel) – beim Wechsel wird er wieder geladen; sonst entsteht die Stadtkarte neu aus der Region.
    /// </summary>
    public sealed class RealmCity
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>Weltregion der Stadtkarte.</summary>
        public int RegionId { get; internal set; }
        /// <summary>Weltzelle (Marker auf der Weltkarte, Entfernungen).</summary>
        public int Cell { get; internal set; }
        /// <summary>Geländeseed der Stadtkarte (zusammen mit der Region → Gelände) und Seed der Systeme.</summary>
        public int TerrainSeed { get; internal set; }
        public int Seed { get; internal set; }
        public int FoundedDay { get; internal set; }
        /// <summary>Letzter Tag, an dem die Stadt detailliert lief (−1 = nie).</summary>
        public int LastDetailedDay { get; internal set; } = -1;
        /// <summary>Lokale Abschnitte des eingefrorenen Detailzustands (null = nie detailliert bzw. gerade aktiv).</summary>
        internal List<SaveSection> Snapshot { get; set; }
        public bool HasSnapshot => Snapshot != null;
        /// <summary>Größe des eingefrorenen Zustands in Bytes (Info).</summary>
        public long SnapshotBytes
        {
            get { long n = 0; if (Snapshot != null) foreach (var s in Snapshot) n += s.Data?.Length ?? 0; return n; }
        }
        public CityEconomy Economy { get; } = new CityEconomy();

        public override string ToString() => Name;
    }

    /// <summary>
    /// Aggregierte Stadt-Volkswirtschaft (LOD „fern“, ähnlich <see cref="NationEconomy"/>, aber für eine Stadt): Bevölkerung mit
    /// Wachstum nach geschätzter Zufriedenheit und Wohnraum, Produktion und Bedarf je Güterkategorie (je Kopf, gemessen in der
    /// detaillierten Stadt bzw. aus den Vorkommen der Region), Lager, Weltmarkt-Ein-/Ausfuhr, Stadtkasse mit Steuern und Unterhalt.
    /// Die aktive Stadt füllt dieselben Felder täglich aus der Detailsimulation (<see cref="CaptureFrom"/>) – so zeigt das
    /// Reichsfenster für alle Städte dieselben Kennzahlen.
    /// </summary>
    public sealed class CityEconomy
    {
        const int K = GoodsCategories.Count;

        public double Population { get; internal set; }
        /// <summary>Wohnraum (Einwohner bei voller Belegung) – aggregiert wird nicht gebaut, Wachstum endet hier.</summary>
        public double HousingCapacity { get; internal set; }
        public int Jobs { get; internal set; }
        /// <summary>Zufriedenheit (Schätzung, 0..1) und ihr Ausgangswert aus der Detailsimulation.</summary>
        public float Satisfaction { get; internal set; } = 0.6f;
        public float BaseSatisfaction { get; internal set; } = 0.6f;
        /// <summary>Stadtkasse (€).</summary>
        public double Treasury { get; internal set; }
        /// <summary>Steuern je Einwohner und Tag, Unterhalt je Tag (gemessen in der Detailsimulation, Wochenmittel).</summary>
        public double TaxPerCapitaDay { get; internal set; }
        public double UpkeepPerDay { get; internal set; }
        /// <summary>Letzter Tag: Steuern, Unterhalt, Handelssaldo (€).</summary>
        public double TaxesToday { get; internal set; }
        public double UpkeepToday { get; internal set; }
        public double TradeNetToday { get; internal set; }

        public readonly double[] ProductionPerCapita = new double[K];
        public readonly double[] DemandPerCapita = new double[K];
        /// <summary>Kategorie-Einheiten: Lager, heute erzeugt/benötigt/eingeführt/ausgeführt.</summary>
        public readonly double[] Stock = new double[K];
        public readonly double[] Production = new double[K];
        public readonly double[] Demand = new double[K];
        public readonly double[] Imported = new double[K];
        public readonly double[] Exported = new double[K];

        // --- Kennzeichen (aus der Detailsimulation; Voraussetzungen und Transportwege)
        public bool HasTownHall { get; internal set; }
        /// <summary>Wohngebiete an die Autobahn angeschlossen (bzw. Außenanschluss).</summary>
        public bool RoadConnected { get; internal set; }
        public bool HasFreightRail { get; internal set; }
        public bool HasHarbor { get; internal set; }
        /// <summary>Lief schon einmal detailliert (sonst: neu gegründet, Karte noch leer).</summary>
        public bool Measured { get; internal set; }
        public int LastDay { get; internal set; } = -1;

        public double Balance(GoodsCategory c) => Production[(int)c] - Demand[(int)c];

        /// <summary>Neu gegründete Stadt: leere Karte, Startkasse, Vorkommen der Region als Produktionspotenzial (noch ohne Einwohner).</summary>
        internal void InitNew(Planet p, Region r, double funds)
        {
            Population = 0; HousingCapacity = 0; Jobs = 0;
            Satisfaction = BaseSatisfaction = 0.6f;
            Treasury = funds;
            TaxPerCapitaDay = 0.9; UpkeepPerDay = 0;
            Array.Clear(Stock, 0, K);
            for (int c = 0; c < K; c++) DemandPerCapita[c] = GoodsCategories.PerCapitaDemand[c] * 0.25; // städtischer Haushaltsbedarf (grob)
            if (r != null)
            {
                ProductionPerCapita[(int)GoodsCategory.Food] = 0.3 * r.Fertility;
                ProductionPerCapita[(int)GoodsCategory.Wood] = 0.04 * r.Forest;
                ProductionPerCapita[(int)GoodsCategory.Minerals] = 0.05 * (r.Ore + r.Sand);
                ProductionPerCapita[(int)GoodsCategory.Energy] = 0.1 * (r.Coal + r.Oil);
            }
            Measured = false;
        }

        // ------------------------------------------------------------ Detail → Aggregat

        /// <summary>
        /// Kennzahlen der detailliert laufenden Stadt übernehmen: Einwohner, Wohnraum, Zufriedenheit, Kasse, Steuern/Unterhalt
        /// (Wochenmittel, sonst Vortag), Produktion/Bedarf/Lager je Kategorie aus den G5-Märkten (Vortag, in Kategorie-Einheiten),
        /// Rathaus, Anschluss, Güterbahnhof, Hafen. Liest nur.
        /// </summary>
        public void CaptureFrom(SimulationWorld w)
        {
            var stats = w.Citizens?.Stats;
            Population = stats?.Population ?? 0;
            var cs = w.City?.Stats;
            Jobs = cs?.Jobs ?? 0;
            if (cs != null && cs.Households > 0)
                HousingCapacity = Math.Max(Population, Population * cs.Households / (double)Math.Max(1, cs.OccupiedHouseholds));
            else HousingCapacity = Population;
            BaseSatisfaction = Satisfaction = Population > 0 && stats != null ? stats.AverageSatisfaction : 0.6f;

            var e = w.Economy;
            if (e != null)
            {
                Treasury = e.Balance;
                var period = e.LastWeek ?? e.LastDay;
                double days = period != null ? Math.Max(1, period.Days) : 1;
                double tax = 0, upkeep = 0;
                if (period != null)
                {
                    tax = TaxIncome(period);
                    foreach (var c in UpkeepCategories) upkeep += period.Amount(c);
                }
                TaxPerCapitaDay = Population > 0 ? tax / days / Population : 0.9;
                UpkeepPerDay = upkeep / days;
                if (e.LastDay != null) { TaxesToday = TaxIncome(e.LastDay); double u = 0; foreach (var c in UpkeepCategories) u += e.LastDay.Amount(c); UpkeepToday = u; }
            }

            Array.Clear(Production, 0, K); Array.Clear(Demand, 0, K); Array.Clear(Stock, 0, K);
            Array.Clear(Imported, 0, K); Array.Clear(Exported, 0, K);
            double trade = 0;
            if (w.Goods != null)
                foreach (var g in GoodsCatalog.All)
                {
                    var m = w.Goods.Market(g);
                    int c = (int)TradeGoods.CategoryOf(g);
                    double u = TradeGoods.CategoryUnitsPerGood(g);
                    Production[c] += m.Yesterday.Produced * u;
                    Demand[c] += (m.Yesterday.Consumed + m.Yesterday.HouseholdBought) * u;
                    Imported[c] += m.Yesterday.Imported * u;
                    Exported[c] += m.Yesterday.Exported * u;
                    Stock[c] += m.Stock * u;
                    trade += m.Yesterday.ExportValue - m.Yesterday.ImportValue;
                }
            TradeNetToday = trade;
            for (int c = 0; c < K; c++)
            {
                ProductionPerCapita[c] = Population > 0 ? Production[c] / Population : 0;
                DemandPerCapita[c] = Population > 0 ? Demand[c] / Population : DemandPerCapita[c];
            }

            HasTownHall = w.Services != null && w.Services.HasTownHall;
            LinksOf(w, out bool road, out bool rail, out bool harbor);
            RoadConnected = road;
            HasFreightRail = rail;
            HasHarbor = harbor;
            Measured = Measured || Population > 0;
            LastDay = w.Clock.Day - 1;
        }

        /// <summary>Anschlüsse der detaillierten Stadt (live, liest nur): Wohngebiete an der Autobahn, Güterbahnhof, Hafen in Betrieb.</summary>
        public static void LinksOf(SimulationWorld w, out bool roadConnected, out bool freightRail, out bool harbor)
        {
            var gw = w.Gateways;
            roadConnected = !w.Settings.RequireGatewayForMoveIn || (gw != null && gw.HousingConnected > 0);
            freightRail = w.Rail != null && w.Rail.HasFreightService;
            harbor = false;
            if (w.Shipping != null) foreach (var h in w.Shipping.Harbors) if (h.InService) { harbor = true; break; }
        }

        static readonly BudgetCategory[] TaxCategories =
        {
            BudgetCategory.ResidentialTax, BudgetCategory.CommercialTax, BudgetCategory.OfficeTax, BudgetCategory.IndustryTax,
            BudgetCategory.TransitFares, BudgetCategory.RailFares, BudgetCategory.HarborFees, BudgetCategory.Tariffs,
        };

        static readonly BudgetCategory[] UpkeepCategories =
        {
            BudgetCategory.RoadUpkeep, BudgetCategory.UtilityUpkeep, BudgetCategory.LineUpkeep, BudgetCategory.Services, BudgetCategory.Interest,
        };

        /// <summary>Steuern und Entgelte eines Zeitraums (Grundlage der Landesabgabe).</summary>
        public static double TaxIncome(BudgetPeriod p)
        {
            if (p == null) return 0;
            double s = 0;
            foreach (var c in TaxCategories) s += p.Amount(c);
            return s;
        }

        // ------------------------------------------------------------ Aggregierter Tag

        /// <summary>
        /// Ein Spieltag der aggregierten Stadt. Produktion und Bedarf je Kopf × Einwohner, Lager deckt den Bedarf, Fehlmengen kommen
        /// vom Weltmarkt (× 1,1 Weltpreis), Überschuss über 30 Tage Lager geht hinaus (× 0,85). Kasse: Steuern je Kopf + 4 % des
        /// Außenhandelssaldos − Unterhalt. Zufriedenheit läuft träge zum Ausgangswert (− Schulden, − Lücken der Grundversorgung),
        /// die Bevölkerung wächst/schrumpft danach, Wachstum höchstens bis zum Wohnraum. <paramref name="reserved"/> = Kategorie-Einheiten,
        /// die für ausgehende interne Lieferungen über dem Höchstlager liegen bleiben dürfen.
        /// </summary>
        internal void StepDay(int day, Func<GoodsCategory, double> worldPrice, double[] reserved = null)
        {
            double p = Population;
            double tradeNet = 0;
            double basic = 0, basicNeed = 0;
            for (int c = 0; c < K; c++)
            {
                double prod = p * ProductionPerCapita[c];
                double need = p * DemandPerCapita[c];
                Production[c] = prod;
                Demand[c] = need;
                Stock[c] += prod;
                double used = Math.Min(need, Stock[c]);
                Stock[c] -= used;
                double gap = need - used;
                Imported[c] = gap;
                // Höchstlager; Ware für interne Lieferungen (zwei Wochen) bleibt liegen
                double max = Math.Max(1.0, need) * RealmRules.MaxStockDays + (reserved != null ? reserved[c] : 0.0);
                double over = Stock[c] > max ? Stock[c] - max : 0;
                Stock[c] -= over;
                Exported[c] = over;
                Stock[c] *= c == (int)GoodsCategory.Food ? 0.995 : 0.999;
                double price = worldPrice((GoodsCategory)c);
                tradeNet += over * price * RealmRules.ExportPriceShare - gap * price * RealmRules.ImportPriceFactor;
                if (c == (int)GoodsCategory.Food || c == (int)GoodsCategory.Consumer) { basic += used; basicNeed += need; }
            }
            TradeNetToday = tradeNet;
            TaxesToday = p * TaxPerCapitaDay + RealmRules.TradeTaxShare * tradeNet;
            UpkeepToday = UpkeepPerDay;
            Treasury += TaxesToday - UpkeepToday;

            float target = BaseSatisfaction;
            if (Treasury < 0) target -= RealmRules.DebtSatisfactionPenalty;
            if (basicNeed > 1e-9) target -= RealmRules.ShortageSatisfactionPenalty * (float)(1.0 - basic / basicNeed);
            Satisfaction += (Math.Clamp(target, 0f, 1f) - Satisfaction) * RealmRules.SatisfactionRate;

            double rate = RealmRules.MaxGrowthPerDay * Math.Clamp((Satisfaction - RealmRules.NeutralSatisfaction) / RealmRules.SatisfactionSpan, -1.0, 1.0);
            if (rate > 0) Population = Math.Min(Math.Max(p, HousingCapacity), p + p * rate);
            else Population = p * (1.0 + rate);
            LastDay = day;
        }

        // ------------------------------------------------------------ Spielstand

        internal void Write(SaveWriter w)
        {
            w.Double(Population); w.Double(HousingCapacity); w.Int(Jobs);
            w.Float(Satisfaction); w.Float(BaseSatisfaction);
            w.Double(Treasury); w.Double(TaxPerCapitaDay); w.Double(UpkeepPerDay);
            w.Double(TaxesToday); w.Double(UpkeepToday); w.Double(TradeNetToday);
            w.NamedDoubles<GoodsCategory>(ProductionPerCapita);
            w.NamedDoubles<GoodsCategory>(DemandPerCapita);
            w.NamedDoubles<GoodsCategory>(Stock);
            w.NamedDoubles<GoodsCategory>(Production);
            w.NamedDoubles<GoodsCategory>(Demand);
            w.NamedDoubles<GoodsCategory>(Imported);
            w.NamedDoubles<GoodsCategory>(Exported);
            w.Bool(HasTownHall); w.Bool(RoadConnected); w.Bool(HasFreightRail); w.Bool(HasHarbor); w.Bool(Measured);
            w.Int(LastDay);
        }

        internal void Read(SaveReader r)
        {
            Population = r.Double(); HousingCapacity = r.Double(); Jobs = r.Int();
            Satisfaction = r.Float(); BaseSatisfaction = r.Float();
            Treasury = r.Double(); TaxPerCapitaDay = r.Double(); UpkeepPerDay = r.Double();
            TaxesToday = r.Double(); UpkeepToday = r.Double(); TradeNetToday = r.Double();
            r.NamedDoublesInto<GoodsCategory>(ProductionPerCapita);
            r.NamedDoublesInto<GoodsCategory>(DemandPerCapita);
            r.NamedDoublesInto<GoodsCategory>(Stock);
            r.NamedDoublesInto<GoodsCategory>(Production);
            r.NamedDoublesInto<GoodsCategory>(Demand);
            r.NamedDoublesInto<GoodsCategory>(Imported);
            r.NamedDoublesInto<GoodsCategory>(Exported);
            HasTownHall = r.Bool(); RoadConnected = r.Bool(); HasFreightRail = r.Bool(); HasHarbor = r.Bool(); Measured = r.Bool();
            LastDay = r.Int();
            if (!(Population >= 0) || double.IsInfinity(Population)) throw new SaveFormatException("Stadt: ungültige Einwohnerzahl.");
        }
    }
}
