using System;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Umwelt: Wirkungen auf andere Systeme – alles über deren Hooks, verbunden in <see cref="Connect"/> (aufgerufen am Ende von
    /// <c>SimulationWorld.CreateDefault</c>, auch beim Laden). Solange <see cref="Enabled"/> aus ist, liefern alle Hooks
    /// genau die alten Werte (Fruchtbarkeit/Ernte unverändert, keine Belastung, keine Kosten).
    /// </summary>
    public sealed partial class EcologySystem
    {
        public void Connect()
        {
            // Warenketten: Fruchtbarkeit × Boden/Luft (Standortwahl, Güte der Höfe), Ernte × Hitze/Dürre, Kosten der Auflagen
            var goods = world.Goods;
            if (goods != null)
            {
                var baseDeposit = goods.DepositAt;
                if (baseDeposit != null)
                    goods.DepositAt = (id, p) =>
                    {
                        float v = baseDeposit(id, p);
                        return Enabled && id == ResourceIds.Fertility ? v * FertilityFactorAt(p) : v;
                    };
                var baseHarvest = goods.HarvestFactor;
                if (baseHarvest != null)
                    goods.HarvestFactor = day => Enabled ? baseHarvest(day) * HarvestStress(day) : baseHarvest(day);
                goods.EnvironmentCostFor = FirmCost;
            }

            // Bürger: chronische Luftbelastung an der Wohnung → Gesundheit/Krankheit (Alter verstärkt)
            if (world.Citizens != null)
                world.Citizens.ChronicAirExposure = b => Enabled && b != null ? Exposure.Sample(b.Position) : 0f;

            // Bodenwert: Altlasten
            if (world.LandValue != null)
                world.LandValue.ContaminationAt = p => Enabled ? Soil.Sample(p) : 0f;

            // Trinkwasser: Brunnen saugen Grundwasser an
            if (world.WaterQuality != null)
                world.WaterQuality.WellQuality = f => Enabled && f != null ? WellQualityAt(f.Position) : 1f;

            // Stadtkasse: Bäume (Bau) und Unterhalt (Filter, Umweltzone, Pflege)
            if (world.Economy != null)
            {
                Budget = world.Economy;
                world.Economy.RegisterUpkeep(this);
            }
        }
    }
}
