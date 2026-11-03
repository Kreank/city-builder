using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Utilities;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Bausteine für Tests der selbst verlegten Netze (Straßen führen nichts mehr mit): Leitungen legen,
    /// Stromkette Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz.
    /// </summary>
    static class GridKit
    {
        public static UtilityLine Lay(SimulationWorld w, UtilityLineType type, Vector2 a, Vector2 b)
        {
            var plan = w.Utilities.PlanLine(type, a, b);
            Assert.True(plan.IsValid, $"{type} {a}→{b}: {plan.Error}");
            var line = w.Utilities.BuildLine(plan);
            Assert.NotNull(line);
            return line;
        }

        /// <summary>
        /// Stromkette für das Straßenpaar der UtilityTests (Weststraße (0,0)→(200,0)): Kohlekraftwerk westlich am
        /// Straßenanfang, Hochspannung nach Süden zum Umspannwerk, Erdkabel vom Umspannwerk zum Straßenanfang (0,0).
        /// Ortsnetz entlang der Straße legt der Test selbst.
        /// </summary>
        public static (UtilityFacility plant, UtilityFacility substation, UtilityLine hv) PowerChainWest(SimulationWorld w,
            UtilityFacilityType plantType = null, UtilityFacilityType substationType = null)
        {
            var u = w.Utilities;
            var plant = u.TryBuildFacility(plantType ?? UtilityCatalog.CoalPlant, new Vector2(-26, 0), 90f); // Vorderseite zur Straße
            Assert.NotNull(plant);
            var sub = u.TryBuildFacility(substationType ?? UtilityCatalog.Substation, new Vector2(-20, -120)); // Fläche x −32..−8, y −130..−110
            Assert.NotNull(sub);
            var hv = Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-26, -24), new Vector2(-32, -120)); // Kraftwerk-Südrand → Umspannwerk-Westrand
            Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-20, -110), new Vector2(0, 0)); // Umspannwerk-Nordrand → Straßenanfang
            return (plant, sub, hv);
        }
    }
}
