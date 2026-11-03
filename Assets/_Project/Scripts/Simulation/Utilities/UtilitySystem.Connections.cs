using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>Hausanschlüsse der selbst verlegten Netze (<see cref="ConnectionRules"/>: Strom-Ortsnetz, Wasser, Abwasser).</summary>
    public sealed partial class UtilitySystem
    {
        /// <summary>
        /// Leitung, an der das Gebäude hängt (nächste Leitung der Art, die Gebäude anschließt, in
        /// <see cref="ConnectionRules.BuildingReach"/> seiner Zufahrt), bei gleicher Entfernung die kleinere Id.
        /// null = kein Anschluss. Für die Darstellung des Hausanschlusses und den Inspektor.
        /// </summary>
        public UtilityLine ConnectedLine(Building b, UtilityKind kind)
        {
            if (b == null || ConnectionRules.CarriedByRoads(kind)) return null;
            UtilityLine best = null;
            float bestD = float.MaxValue;
            foreach (var l in lines.Values)
            {
                if (l.Kind != kind || !l.Type.ConnectsBuildings) continue;
                float d = l.DistanceTo(b.Entrance);
                if (d > ConnectionRules.BuildingReach) continue;
                if (d < bestD || (d == bestD && best != null && l.Id < best.Id)) { bestD = d; best = l; }
            }
            return best;
        }
    }
}
