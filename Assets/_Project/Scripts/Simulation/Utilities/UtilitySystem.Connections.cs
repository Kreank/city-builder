using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>Hausanschlüsse selbst verlegter Netze (<see cref="ConnectionRules"/>, bisher nur Abwasser).</summary>
    public sealed partial class UtilitySystem
    {
        /// <summary>
        /// Leitung, an der das Gebäude hängt (nächste in <see cref="ConnectionRules.BuildingReach"/> seiner Zufahrt),
        /// bei kleinster Entfernung die kleinere Id. null = kein Anschluss bzw. Netzart wird von Straßen mitgeführt.
        /// Für die Darstellung des Hausanschlusses und den Inspektor.
        /// </summary>
        public UtilityLine ConnectedLine(Building b, UtilityKind kind)
        {
            if (b == null || ConnectionRules.CarriedByRoads(kind)) return null;
            UtilityLine best = null;
            float bestD = ConnectionRules.BuildingReach;
            foreach (var l in lines.Values)
            {
                if (l.Kind != kind) continue;
                float d = l.DistanceTo(b.Entrance);
                if (d < bestD || (d == bestD && best != null && l.Id < best.Id)) { bestD = d; best = l; }
            }
            return best;
        }
    }
}
