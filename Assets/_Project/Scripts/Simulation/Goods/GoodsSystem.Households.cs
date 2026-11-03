using System;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Haushaltskonsum: Haushalte kaufen Endprodukte im Einzelhandel nach Bedarfsstufen (Grundbedarf → Komfort → Luxus),
    /// abhängig von Einkommen und Zufriedenheit. Hook des Bürgersystems (<c>CitizenSystem.Purchase</c>).
    /// </summary>
    public sealed partial class GoodsSystem
    {
        int purchasesToday, successfulToday;
        readonly float[] tierWantedToday = new float[4];
        readonly float[] tierBoughtToday = new float[4];

        /// <summary>Höchste Bedarfsstufe, die sich ein Haushalt mit diesem Einkommen (€/Tag) und dieser Zufriedenheit leistet.</summary>
        public static ConsumerTier HighestTier(float income, float satisfaction)
        {
            if (income >= GoodsRules.LuxuryIncome && satisfaction >= GoodsRules.LuxurySatisfaction) return ConsumerTier.Luxury;
            if (income >= GoodsRules.ComfortIncome && satisfaction >= GoodsRules.ComfortSatisfaction) return ConsumerTier.Comfort;
            return ConsumerTier.Basic;
        }

        /// <summary>Hat der Laden Grundbedarf im Regal? (Ladenwahl der Bürger; Gebäude ohne Firma → ja.)</summary>
        public bool ShopHasStock(int shopBuildingId)
        {
            var f = FirmAt(shopBuildingId);
            if (f == null || f.Role != FirmRole.Retail) return true;
            return f.InputStock[GoodsCatalog.ConsumerGoods[0].Index] >= 0.5f;
        }

        /// <summary>Endpreis im Laden (lokaler Großhandelspreis × Aufschlag).</summary>
        public float RetailPrice(GoodType g) => markets[g.Index].LocalPrice * GoodsRules.RetailMarkup;

        /// <summary>
        /// Einkauf eines Haushalts im Laden (Gebäude-Id). Liefert die Erfüllung 0..1 (gewichtet nach Stufe; nur was der
        /// Haushalt sich leisten kann, zählt als gewünscht). Erfolgreich ist ein Einkauf, wenn der Laden den Grundbedarf
        /// vorrätig hat. Laden ohne Firma → 1 (alter Ablauf).
        /// </summary>
        public float Purchase(Household h, int shopBuildingId)
        {
            var f = FirmAt(shopBuildingId);
            if (h == null || f == null || f.Role != FirmRole.Retail) return 1f;
            purchasesToday++;
            int persons = Math.Max(1, h.Members.Count);
            float size = 0.5f + 0.5f * persons;
            float days = GoodsRules.DaysPerShopping;
            var top = HighestTier(h.Income, h.Satisfaction);
            // Grundbedarf geht vor (bis 90 % des Einkommens), der Rest nur aus dem frei verfügbaren Anteil
            double basicBudget = Math.Max(0f, h.Income) * days * 0.9;
            double freeBudget = Math.Max(0f, h.Income) * days * GoodsRules.SpendShare;
            double spent = 0;
            float weighted = 0f, weights = 0f, basicFraction = 1f;

            for (var tier = ConsumerTier.Basic; tier <= top; tier++)
            {
                double wantValue = 0, gotValue = 0;
                foreach (var g in GoodsCatalog.ConsumerGoods)
                {
                    if (g.Consumer != tier) continue;
                    int k = g.Index;
                    float price = RetailPrice(g);
                    float want = g.HouseholdPerDay * days * size;
                    double budget = tier == ConsumerTier.Basic ? basicBudget - spent : freeBudget - spent;
                    if (price > 0f) want = (float)Math.Min(want, Math.Max(0, budget) / price);
                    if (want <= 1e-4f) continue;
                    float take = MathF.Min(want, f.InputStock[k]);
                    if (take > 0f)
                    {
                        double cost = f.AverageInputCost(g) * take;
                        double revenue = take * (double)price;
                        f.InputStock[k] -= take;
                        f.InputValue[k] = Math.Max(0, f.InputValue[k] - cost);
                        if (f.InputStock[k] < 1e-5f) { f.InputStock[k] = 0f; f.InputValue[k] = 0; }
                        f.Today.InputCost += cost;
                        f.Today.Revenue += revenue;
                        f.Today.Sold += take;
                        f.Cash += revenue;
                        spent += revenue;
                    }
                    var m = markets[k];
                    m.Today.HouseholdWanted += want;
                    m.Today.HouseholdBought += take;
                    tierWantedToday[(int)tier] += want;
                    tierBoughtToday[(int)tier] += take;
                    wantValue += want * (double)price;
                    gotValue += take * (double)price;
                }
                if (wantValue <= 0) continue;
                float fraction = (float)(gotValue / wantValue);
                if (tier == ConsumerTier.Basic) basicFraction = fraction;
                float w = tier == ConsumerTier.Basic ? GoodsRules.BasicWeight : tier == ConsumerTier.Comfort ? GoodsRules.ComfortWeight : GoodsRules.LuxuryWeight;
                weighted += w * fraction;
                weights += w;
            }
            if (basicFraction >= 0.5f) successfulToday++;
            return weights > 0f ? weighted / weights : 1f;
        }
    }
}
