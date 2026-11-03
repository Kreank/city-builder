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
            if (utilities != null) goods.UtilitiesSatisfied = utilities.IsSatisfied;
        }
    }
}
