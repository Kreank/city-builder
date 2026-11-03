using System;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Verbindet das Warensystem mit Bürgern, Stadt und Versorgung (aufgerufen in <c>SimulationWorld.CreateDefault</c>).
    /// Die Bodenkarte (Agent „world“) wird beim Mergen über <see cref="GoodsSystem.DepositAt"/> und
    /// <see cref="GoodsSystem.Extract"/> angeschlossen; die Stadtkasse über <c>EconomySystem.TaxableProfit</c>.
    /// </summary>
    public static class GoodsBinding
    {
        public static void Connect(GoodsSystem goods, CitySystem city, CitizenSystem citizens, UtilitySystem utilities)
        {
            if (citizens != null)
            {
                citizens.Purchase = goods.Purchase;           // Einkauf hängt am Ladenbestand
                citizens.ShopHasStock = goods.ShopHasStock;   // leere Läden meiden
                goods.WageBill = citizens.WageBillOf;         // Löhne aus den echten Beschäftigten
                goods.Workforce = citizens.WorkforceOf;       // Fachkräfte je Bildungsstufe
            }
            if (city != null) city.BusinessDemand = goods.DemandOffset; // Gewinn → Nachfrage der Zonen
            if (utilities != null)
            {
                goods.UtilitiesSatisfied = utilities.IsSatisfied;
                utilities.DemandProvider = goods.UtilityDemand; // Strom/Wasser der Industrie aus dem Rezept
            }
        }

        /// <summary>
        /// Bodenkarte und Klima (Welt, Etappe 5): Vorkommen 0..1 direkt aus <c>ResourceMap.DepositAt</c>; die Entnahme
        /// rechnet Wareneinheiten in Karteneinheiten um (<see cref="MapUnitsPerGood"/>) und zurück. Ernte der
        /// Landwirtschaft = (0,6 + 0,4 × <c>ClimateModel.HarvestFactor(Tag)</c> / Jahresmittel) × clamp(Jahresmittel / Bezug,
        /// 0,4, 1,2): die Jahreszeit schwankt um 1, das Klima der Region hebt/senkt das Jahresmittel.
        /// </summary>
        public static void ConnectWorld(GoodsSystem goods, World.ResourceMap resources, World.ClimateModel climate)
        {
            if (resources != null)
            {
                goods.DepositAt = resources.DepositAt;
                goods.DepositSampleSpacing = resources.CellSize; // Güte = Mittel über das 3×3-Entnahmefenster
                goods.Extract = (id, p, units) =>
                {
                    float k = MapUnitsPerGood(id);
                    if (k <= 0f) return units;
                    return resources.Extract(id, p, units * k) / k;
                };
            }
            if (climate != null)
            {
                float annual = MathF.Max(0.05f, climate.AnnualHarvestFactor);
                float scale = Math.Clamp(annual / GoodsRules.HarvestReference, 0.4f, 1.2f);
                float floor = GoodsRules.HarvestFloor;
                goods.HarvestFactor = day => (floor + (1f - floor) * climate.HarvestFactor(day) / annual) * scale;
            }
        }

        /// <summary>Karteneinheiten (<c>ResourceKind.Unit</c>) je geförderter Wareneinheit; 0 = nicht entnehmen.</summary>
        public static float MapUnitsPerGood(string resourceId)
        {
            switch (resourceId)
            {
                case ResourceIds.Ore: return GoodsCatalog.Ore.Weight;      // t
                case ResourceIds.Coal: return GoodsCatalog.Coal.Weight;    // t
                case ResourceIds.Oil: return GoodsCatalog.Oil.Volume;      // m³
                case ResourceIds.Sand: return GoodsCatalog.Sand.Volume;    // m³
                case ResourceIds.Wood: return GoodsRules.WoodPerUnit;      // m³ (Spielmaßstab)
                case ResourceIds.Fertility: return GoodsRules.FertilityPerUnit; // Bodenpunkte
                default: return 0f;
            }
        }
    }
}
