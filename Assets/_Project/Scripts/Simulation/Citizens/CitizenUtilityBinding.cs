using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// G3/G4 erweitert: verbindet das Bedürfnismodell mit Wasserqualität, Atomkraftwerk und Medien (aufgerufen in
    /// <see cref="Core.SimulationWorld.CreateDefault"/>). Alles nur lesend:
    /// <list type="bullet">
    /// <item><see cref="CitizenSystem.DrinkingWaterQuality"/> ← <see cref="WaterQualitySystem.DrinkingWaterQuality"/> (Gesundheit).</item>
    /// <item><see cref="CitizenSystem.NuclearConcern"/> ← <see cref="UtilitySystem.ConcernAt"/> an der Wohnung (Ruhe/Umwelt, Erinnerung bei Ängstlichen).</item>
    /// <item><see cref="CitizenSystem.MediaReach"/> ← <see cref="ServiceSystem.Coverage"/> „media“ – ohne Medienhaus 0 (nicht 0,5 wie andere Dienste:
    /// Medien sind ein Bonus, ihr Fehlen kein Mangel).</item>
    /// <item><see cref="CitizenSystem.NewsTone"/> ← <see cref="MediaSystem.CurrentTone"/>.</item>
    /// </list>
    /// </summary>
    public static class CitizenUtilityBinding
    {
        public static void Connect(CitizenSystem citizens, UtilitySystem utilities, WaterQualitySystem water, ServiceSystem services, MediaSystem media)
        {
            if (citizens == null) return;
            if (water != null) citizens.DrinkingWaterQuality = water.DrinkingWaterQuality;
            if (utilities != null) citizens.NuclearConcern = b => utilities.ConcernAt(b.Position);
            if (services != null && media != null) citizens.MediaReach = b => media.HasMedia ? services.Coverage(ServiceKinds.Media, b) : 0f;
            if (media != null) citizens.NewsTone = () => media.CurrentTone;
        }
    }
}
