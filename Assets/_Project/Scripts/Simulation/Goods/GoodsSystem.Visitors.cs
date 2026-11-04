using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Flughafen (Tourismus-Grundstufe): Besucher kaufen in den Läden der Stadt ein – Waren gehen aus dem Regal, der Umsatz
    /// zählt bei der Firma (wie ein Haushaltseinkauf). Ohne Hotels gibt es noch keine Übernachtungsbetriebe.
    /// </summary>
    public sealed partial class GoodsSystem
    {
        /// <summary>
        /// Einkauf der Besucher eines Tages für höchstens <paramref name="budget"/> € (Komfort/Luxus vor Grundbedarf), gleichmäßig
        /// auf die Läden mit Ware verteilt (nach Firmen-Reihenfolge, deterministisch). Liefert den ausgegebenen Betrag.
        /// </summary>
        public double VisitorPurchase(double budget)
        {
            if (budget <= 0) return 0;
            var shops = new List<Firm>();
            foreach (var f in firms) if (f.Role == FirmRole.Retail && f.Status != FirmStatus.UnderConstruction) shops.Add(f);
            if (shops.Count == 0) return 0;
            double share = budget / shops.Count, spentTotal = 0;
            foreach (var f in shops)
            {
                double left = share;
                for (var tier = ConsumerTier.Luxury; tier >= ConsumerTier.Basic && left > 0.01; tier--)
                    foreach (var g in GoodsCatalog.ConsumerGoods)
                    {
                        if (g.Consumer != tier || left <= 0.01) continue;
                        int k = g.Index;
                        float price = RetailPrice(g);
                        if (price <= 0f || f.InputStock[k] <= 1e-4f) continue;
                        float take = MathF.Min(f.InputStock[k], (float)(left / price));
                        if (take <= 1e-4f) continue;
                        double cost = f.AverageInputCost(g) * take;
                        double revenue = take * (double)price;
                        f.InputStock[k] -= take;
                        f.InputValue[k] = Math.Max(0, f.InputValue[k] - cost);
                        if (f.InputStock[k] < 1e-5f) { f.InputStock[k] = 0f; f.InputValue[k] = 0; }
                        f.Today.InputCost += cost;
                        f.Today.Revenue += revenue;
                        f.Today.Sold += take;
                        f.Cash += revenue;
                        markets[k].Today.HouseholdBought += take;
                        left -= revenue;
                        spentTotal += revenue;
                    }
            }
            return spentTotal;
        }
    }
}
