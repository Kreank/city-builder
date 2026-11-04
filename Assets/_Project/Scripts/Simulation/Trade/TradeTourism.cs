using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel 2: Tourismus-Handel ↔ Spielerstadt. Das Besucher-Potenzial der Stadt kommt aus der vorhandenen Flughafen-Grundstufe
    /// (<c>AviationSystem.BaseTouristPotential</c>: Tourismus-Stärke, Attraktivität der Freizeitanlagen/Wahrzeichen, Stadtgröße)
    /// × Umwelt (<c>EcologySystem.TourismFactor</c>, wenn das Umweltmodell läuft). Der Handel verteilt es auf die Herkunftsländer
    /// und Verkehrsmittel; Flugtouristen werden Nachfrage des Flughafens, die übrigen kommen direkt in die Besucherliste
    /// (Ausgaben, Kurtaxe, Ladenumsatz wie bisher). Zugang aus den Gateways (Straße angeschlossen, Bahn/Wasser/Luft in Betrieb).
    /// </summary>
    public static class TradeTourism
    {
        public static void Connect(SimulationWorld world)
        {
            var trade = world?.Trade;
            var aviation = world?.Aviation;
            if (trade == null || aviation == null) return;
            trade.PlayerTourismPotential = () =>
            {
                double p = aviation.BaseTouristPotential();
                if (world.Ecology != null && world.Ecology.Enabled) p *= world.Ecology.TourismFactor;
                return p;
            };
            trade.CityTourismAccess = () => Access(world);
            trade.CityGroundVisitors = k => aviation.AddGroundVisitors(k, trade.Day);
            aviation.TouristDemandSource = () => (float)trade.CityAirTourists;
        }

        static TourismAccess Access(SimulationWorld world)
        {
            var a = new TourismAccess();
            var gw = world.Gateways;
            a.Road = gw == null || !world.Settings.RequireGatewayForMoveIn || gw.HousingConnected > 0;
            if (gw != null)
                foreach (var g in gw.All)
                {
                    if (g.Status != GatewayStatus.Operational) continue;
                    if (g.Kind == GatewayKind.Rail) a.Rail = true;
                    else if (g.Kind == GatewayKind.Water) a.Sea = true;
                    else if (g.Kind == GatewayKind.Air) a.Air = true;
                }
            if (world.Aviation != null)
                foreach (var ap in world.Aviation.Airports) if (ap.InService) a.Air = true;
            return a;
        }
    }
}
