using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Gruppe der Nachfrage-Anzeige (obere Leiste, Kacheln der Kategorie „Bauen“).</summary>
    public enum DemandKind
    {
        Residential,
        Shop,
        Office,
        Industry,
    }

    /// <summary>Nachfrage einer Gruppe mit Begründung (für Balken + Tooltip).</summary>
    public sealed class DemandEntry
    {
        public DemandKind Kind;
        public string Name;
        /// <summary>0..100 (wie <see cref="CitySystem.Demand"/>).</summary>
        public float Value;
        /// <summary>„warum“: Zeilen wie „Arbeitslose: 42“, „Grundbedarf gestern zu 70 % gedeckt“.</summary>
        public readonly List<string> Reasons = new List<string>();
        public string Tooltip => $"Nachfrage {Name}: {Value:0} %" + (Reasons.Count > 0 ? "\n· " + string.Join("\n· ", Reasons) : "");
    }

    /// <summary>
    /// Setzen statt Zonen: Nachfrage-Anzeige aus <see cref="CitySystem.Demand"/> (gleiche Formel wie das Zonenwachstum) und
    /// den Zuschlägen der Warenketten (G5, <c>GoodsSystem.DemandOffset</c>), mit den Gründen dahinter. Nur lesend, rechnet
    /// keinen Spielzustand → keine Wirkung auf den Determinismus.
    /// </summary>
    public static class DemandReport
    {
        public static readonly DemandKind[] All = { DemandKind.Residential, DemandKind.Shop, DemandKind.Office, DemandKind.Industry };

        public static string NameOf(DemandKind k) =>
            k == DemandKind.Residential ? "Wohnen" : k == DemandKind.Shop ? "Laden" : k == DemandKind.Office ? "Büro" : "Industrie";

        public static string ShortName(DemandKind k) =>
            k == DemandKind.Residential ? "W" : k == DemandKind.Shop ? "L" : k == DemandKind.Office ? "B" : "I";

        /// <summary>Gruppe einer Zonenart (Mischgebiet zählt zum Laden).</summary>
        public static DemandKind KindOf(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.Retail:
                case ZoneType.Mixed: return DemandKind.Shop;
                case ZoneType.Office: return DemandKind.Office;
                case ZoneType.Industry: return DemandKind.Industry;
                default: return DemandKind.Residential;
            }
        }

        /// <summary>Wert 0..100: Wohnen = höchste der drei Wohndichten, sonst die Zonenart.</summary>
        public static float ValueOf(CitySystem city, DemandKind k)
        {
            var d = city.Demand;
            switch (k)
            {
                case DemandKind.Residential:
                    return MathF.Max(d[ZoneType.ResidentialLow], MathF.Max(d[ZoneType.ResidentialMedium], d[ZoneType.ResidentialHigh]));
                case DemandKind.Shop: return d[ZoneType.Retail];
                case DemandKind.Office: return d[ZoneType.Office];
                default: return d[ZoneType.Industry];
            }
        }

        public static DemandEntry Explain(SimulationWorld w, DemandKind k)
        {
            var e = new DemandEntry { Kind = k, Name = NameOf(k) };
            var city = w?.City;
            if (city == null) return e;
            e.Value = ValueOf(city, k);
            var pop = city.Population;
            var goods = w.Goods;

            int retailJobs = 0, officeJobs = 0, industryJobs = 0;
            foreach (var b in city.Buildings)
            {
                if (b.Zone == ZoneType.Retail || b.Zone == ZoneType.Mixed) retailJobs += b.Jobs;
                else if (b.Zone == ZoneType.Office) officeJobs += b.Jobs;
                else if (b.Zone == ZoneType.Industry) industryJobs += b.Jobs;
            }

            switch (k)
            {
                case DemandKind.Residential:
                {
                    e.Reasons.Add($"Zuzugsdruck {city.MoveInPull:0} von 100");
                    if (pop.VacantJobs > 0) e.Reasons.Add($"freie Arbeitsplätze: {pop.VacantJobs}");
                    if (pop.CommutersIn > 0) e.Reasons.Add($"von Einpendlern besetzt: {pop.CommutersIn} (Einwohner würden sie übernehmen)");
                    if (pop.Unemployed > 0) e.Reasons.Add($"Arbeitslose: {pop.Unemployed} (dämpft)");
                    if (pop.CommutersOut > 0) e.Reasons.Add($"Auspendler: {pop.CommutersOut} (dämpft)");
                    var s = city.Stats;
                    if (s.Households > 0)
                    {
                        float vacancy = 1f - s.OccupiedHouseholds / (float)s.Households;
                        e.Reasons.Add($"Leerstand {vacancy * 100f:0} % ({s.Households - s.OccupiedHouseholds} freie Wohnungen){(vacancy > 0.15f ? " – dämpft" : "")}");
                    }
                    if (pop.AverageSatisfaction >= 0f) e.Reasons.Add($"Zufriedenheit Ø {pop.AverageSatisfaction * 100f:0} % (ab 60 % zieht die Stadt an)");
                    break;
                }
                case DemandKind.Shop:
                {
                    e.Reasons.Add($"Ladenjobs {retailJobs} von etwa {pop.Residents * 0.09f:0} für {pop.Residents} Einwohner");
                    if (pop.UnservedShoppers > 0) e.Reasons.Add($"Einkäufer ohne erreichbaren Laden: {pop.UnservedShoppers}");
                    if (goods != null)
                    {
                        float met = goods.BasicShoppingMetYesterday();
                        if (met >= 0f) e.Reasons.Add($"Grundbedarf gestern zu {met * 100f:0} % gedeckt{(met < 0.95f ? " – unerfüllte Einkäufe" : "")}");
                        AddMargin(e, goods.MarginDemand(ZoneType.Retail), "Läden");
                    }
                    break;
                }
                case DemandKind.Office:
                {
                    e.Reasons.Add($"Bürojobs {officeJobs} von etwa {MathF.Max(0f, pop.Residents * 0.07f - 10f):0}");
                    if (pop.UnemployedEducated > 0) e.Reasons.Add($"arbeitslose Fachkräfte (mittlerer Abschluss und mehr): {pop.UnemployedEducated}");
                    if (pop.CommutersOut > 0) e.Reasons.Add($"Auspendler: {pop.CommutersOut}");
                    if (goods != null) AddMargin(e, goods.MarginDemand(ZoneType.Office), "Büros");
                    break;
                }
                default:
                {
                    e.Reasons.Add($"Industriejobs {industryJobs} von etwa {pop.Residents * 0.14f + 20f:0}");
                    int simple = Math.Max(0, pop.Unemployed - pop.UnemployedEducated);
                    if (simple > 0) e.Reasons.Add($"Arbeitslose ohne Fachabschluss: {simple}");
                    if (pop.CommutersOut > 0) e.Reasons.Add($"Auspendler: {pop.CommutersOut}");
                    if (goods != null)
                    {
                        float imp = goods.ImportShareYesterday();
                        if (imp > 0.005f) e.Reasons.Add($"Importanteil gestern {imp * 100f:0} % – heimische Betriebe gefragt");
                        AddMargin(e, goods.MarginDemand(ZoneType.Industry), "Betriebe");
                    }
                    break;
                }
            }

            // Steuern (Stadtkasse): Faktor der Zonenart
            if (city.DemandModifier != null)
            {
                var zone = k == DemandKind.Residential ? ZoneType.ResidentialLow : k == DemandKind.Shop ? ZoneType.Retail
                    : k == DemandKind.Office ? ZoneType.Office : ZoneType.Industry;
                float f = city.DemandModifier(zone);
                if (MathF.Abs(f - 1f) > 0.005f) e.Reasons.Add($"Steuersatz: Nachfrage × {f:0.00}");
            }
            return e;
        }

        static void AddMargin(DemandEntry e, float offset, string who)
        {
            if (MathF.Abs(offset) < 0.5f) return;
            e.Reasons.Add(offset > 0f ? $"{who} verdienen gut (+{offset:0})" : $"{who} machen Verlust ({offset:0})");
        }
    }
}
