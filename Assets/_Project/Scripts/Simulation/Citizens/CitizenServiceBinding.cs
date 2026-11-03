using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Verbindet das Bedürfnismodell mit den Diensten (G4), aufgerufen in <see cref="SimulationWorld.CreateDefault"/>:
    /// <list type="bullet">
    /// <item><see cref="CitizenSystem.ServiceCoverage"/> → <see cref="ServiceSystem.Coverage"/>. Solange es von einer
    /// Dienstart noch kein einziges Gebäude gibt, gilt sie als „noch nicht verlangt“ und liefert neutral 0,5
    /// (wie Strom/Wasser in der Startphase) – sonst würde eine neue Stadt sofort unzufrieden.</item>
    /// <item><see cref="CitizenSystem.GarbageLevel"/> → <see cref="ServiceSystem.GarbageLevel"/>, erst sobald es Müllabfuhr gibt.</item>
    /// <item><see cref="CitizenSystem.ParkFinder"/> → bester Park in Fußweg-Reichweite (Entfernungswirkung × Auslastung).</item>
    /// <item><see cref="ServiceSystem.DemandProvider"/> ← echte Schülerzahlen (6–11 Grundschule, 12–17 Oberschule).</item>
    /// </list>
    /// Die Abdeckung wird im Bürgersystem je Bewertungsrunde und Gebäude zwischengespeichert (<c>HomeContext</c>).
    /// </summary>
    public static class CitizenServiceBinding
    {
        public static void Connect(CitizenSystem citizens, ServiceSystem services)
        {
            if (citizens == null || services == null) return;
            var present = new HashSet<string>();
            int version = -1;

            void Refresh()
            {
                if (version == services.StructureVersion) return;
                version = services.StructureVersion;
                present.Clear();
                foreach (var f in services.Facilities)
                {
                    present.Add(f.Type.Channel);
                    if (f.Type.ExtraChannel != null) present.Add(f.Type.ExtraChannel); // Eigene Assets: Zweitkanal
                }
            }

            float Channel(string id, Zoning.Building b) =>
                present.Contains(id) ? services.Coverage(id, b) : 0.5f;

            citizens.ServiceCoverage = (id, b) =>
            {
                Refresh();
                if (id == ServiceKinds.Education)
                    return 0.5f * (Channel(ServiceKinds.EducationPrimary, b) + Channel(ServiceKinds.EducationSecondary, b));
                return Channel(id, b);
            };

            citizens.GarbageLevel = b =>
            {
                Refresh();
                return present.Contains(ServiceKinds.Garbage) ? services.GarbageLevel(b) : 0f;
            };

            citizens.ParkFinder = b =>
            {
                ServiceFacility best = null;
                float bestQ = 0f;
                foreach (var f in services.Facilities)
                {
                    if (f.Type.Channel != ServiceKinds.Park) continue;
                    float q = ServiceSystem.DistanceFactor(f.DistanceTo(b.Entrance), f.Type.Range) * f.LoadFactor;
                    if (q > bestQ || (q == bestQ && q > 0f && best != null && f.Id < best.Id)) { bestQ = q; best = f; }
                }
                return best == null ? default : new ParkSpot(best.Id, best.Position, bestQ);
            };

            // --- Eigene Assets: Freizeitanlagen, Sport, Wahrzeichen über dieselbe Abdeckungs-API. Ohne solche Bauwerke 0
            // (nicht 0,5 neutral wie die Grunddienste): sie sind ein Bonus, kein Grundbedarf.
            float Bonus(string id, Zoning.Building b)
            {
                Refresh();
                return present.Contains(id) ? services.Coverage(id, b) : 0f;
            }
            citizens.LeisureCoverage = b => Bonus(ServiceKinds.Leisure, b);
            citizens.SportCoverage = b => Bonus(ServiceKinds.Sport, b);
            citizens.LandmarkPride = b => Bonus(ServiceKinds.Landmark, b);
            // --- Ende Eigene Assets

            services.DemandProvider = (b, channel) =>
            {
                if (channel == ServiceKinds.EducationPrimary) return citizens.CountResidents(b.Id, 6, 11);
                if (channel == ServiceKinds.EducationSecondary) return citizens.CountResidents(b.Id, 12, 17);
                return -1f;
            };
        }
    }
}
