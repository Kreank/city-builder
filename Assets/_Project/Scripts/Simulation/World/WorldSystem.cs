using System;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;

namespace CityBuilder.Simulation.World
{
    /// <summary>Übersetzt eine Weltregion in Parameter der Stadtkarte (Gelände, Klima, Ressourcen).</summary>
    public static class RegionMapping
    {
        /// <summary>
        /// Relief aus der Zerklüftung (Flachland ~0,5, Mittelgebirge ~1, Hochgebirge bis 2,2), Fluss wenn die Region
        /// einen Fluss hat (Breite nach Abfluss), Küste in Richtung Meer.
        /// </summary>
        public static TerrainParams TerrainFor(Region r, Planet p)
        {
            float relief = Math.Clamp(0.4f + r.Ruggedness / 600f, 0.35f, 2.2f);
            float width = 1f;
            if (r.HasRiver && p != null && p.RiverThreshold > 0f)
                width = Math.Clamp(0.75f + MathF.Log10(MathF.Max(1f, r.MaxFlow / p.RiverThreshold)) * 0.5f, 0.75f, 1.6f);
            return new TerrainParams
            {
                Relief = relief,
                River = r.HasRiver,
                RiverWidth = width,
                CoastDirection = r.CoastDirection,
                CoastShare = r.IsCoastal ? Math.Clamp(0.12f + r.CoastCells / (float)Math.Max(1, r.Cells.Count) * 0.3f, 0.12f, 0.3f) : 0.22f,
            };
        }

        public static ClimateParams ClimateFor(Region r) => ClimateParams.FromRegion(r);
        public static RegionResources ResourcesFor(Region r) => RegionResources.FromRegion(r);
    }

    /// <summary>
    /// Welt-Tagesschritt (Etappe 5): erneuert die Ressourcen der Stadtkarte (Grundwasser nach dem Niederschlag des
    /// Tages) und rechnet die aggregierten Länder-Volkswirtschaften einen Tag weiter.
    /// </summary>
    public sealed class WorldSystem : ISimulationSystem
    {
        public int TickInterval => SimulationClock.TicksPerDay;

        public void Tick(SimulationWorld world)
        {
            int day = world.Clock.Day - 1; // abgelaufener Tag
            if (world.Resources != null)
            {
                float recharge = 1f;
                if (world.Climate != null && world.Climate.MeanDailyPrecipitation > 0f)
                    recharge = Math.Clamp(world.Climate.PrecipitationOn(day) / world.Climate.MeanDailyPrecipitation, 0f, 4f) * 0.5f + 0.5f;
                world.Resources.RegenerateDay(recharge);
            }
            world.Planet?.Economy?.TickDay(day);
        }
    }
}
