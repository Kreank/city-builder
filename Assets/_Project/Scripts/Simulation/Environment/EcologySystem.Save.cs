using System.Numerics;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Spielstand „ecology“ v1: Schalter, Umweltauflagen, Umweltzone, Verkehrszählung des laufenden Durchlaufs, Tagessumme der
    /// Luft, Raster Boden/Grundwasser/Aufforstung/chronische Belastung, Zähler und Statistik. Die Luft selbst steht weiter im
    /// Abschnitt „environment“. Fehlt der Abschnitt (alte Stände): aus, alles sauber, keine Aufforstung (das Spiel schaltet ein).
    /// Abgeleitet (nicht gespeichert): Vegetations-Abbau (aus Waldraster und Parks neu).
    /// </summary>
    public sealed partial class EcologySystem : ISaveable
    {
        public string SaveId => "ecology";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Bool(Enabled);
            w.Int(Regulation);
            w.Bool(LowEmissionZone);
            w.Float(ZoneCenter.X); w.Float(ZoneCenter.Y); w.Float(ZoneRadius);
            w.Int(trafficSamples);
            w.Floats(trafficAccum);
            w.Int(dayAirPasses);
            w.Floats(dayAirSum);
            Soil.WriteState(w);
            Groundwater.WriteState(w);
            Planted.WriteState(w);
            Exposure.WriteState(w);
            w.Int(PlantedCells); w.Float(TreesPlanted);
            w.Float(HomeAirAverage); w.Float(HomeAirMax); w.Int(SmogBuildings); w.Int(HomeBuildings);
            w.Bool(SmogAlert); w.Int(SmogEpisodes); w.Int(FishKills); w.Bool(fishKillActive);
            w.Int(Version);
            w.Floats(emission);
            w.Floats(emissionToday);
            w.Floats(emissionYesterday);
        }

        public void Read(SaveReader r, int version)
        {
            Enabled = r.Bool();
            Regulation = r.Int();
            LowEmissionZone = r.Bool();
            float zx = r.Float(), zy = r.Float();
            ZoneCenter = new Vector2(zx, zy);
            ZoneRadius = r.Float();
            trafficSamples = r.Int();
            r.FloatsInto(trafficAccum);
            dayAirPasses = r.Int();
            r.FloatsInto(dayAirSum);
            Soil.ReadState(r);
            Groundwater.ReadState(r);
            Planted.ReadState(r);
            Exposure.ReadState(r);
            PlantedCells = r.Int(); TreesPlanted = r.Float();
            HomeAirAverage = r.Float(); HomeAirMax = r.Float(); SmogBuildings = r.Int(); HomeBuildings = r.Int();
            SmogAlert = r.Bool(); SmogEpisodes = r.Int(); FishKills = r.Int(); fishKillActive = r.Bool();
            Version = r.Int();
            r.FloatsInto(emission);
            r.FloatsInto(emissionToday);
            r.FloatsInto(emissionYesterday);
            vegetation = null; // neu aus Wald/Parks
        }
    }
}
