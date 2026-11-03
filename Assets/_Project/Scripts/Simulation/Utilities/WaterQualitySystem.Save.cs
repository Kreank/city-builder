using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Spielstand der Wasserqualität: Verschmutzungsraster (fließt, klingt ab – nicht rekonstruierbar), Einleitungen,
    /// Pumpen-/Netzqualität des letzten Durchlaufs, Summen, Version. Die Wassermaske entsteht aus dem Gelände neu.
    /// </summary>
    public sealed partial class WaterQualitySystem : ISaveable
    {
        public string SaveId => "waterquality";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Float(PollutionPerCubicMeter);
            w.Bool(active);
            w.Float(TotalTreated); w.Float(TotalUntreated);
            w.Int(Version);
            Pollution.WriteState(w);
            w.IntDictionary(treated, w.Float);
            w.IntDictionary(untreated, w.Float);
            w.IntDictionary(pumpQuality, w.Float);
            w.IntDictionary(networkQuality, w.Float);
        }

        public void Read(SaveReader r, int version)
        {
            PollutionPerCubicMeter = r.Float();
            active = r.Bool();
            TotalTreated = r.Float(); TotalUntreated = r.Float();
            Version = r.Int();
            Pollution.ReadState(r);
            r.IntDictionaryInto(treated, r.Float, 4);
            r.IntDictionaryInto(untreated, r.Float, 4);
            r.IntDictionaryInto(pumpQuality, r.Float, 4);
            r.IntDictionaryInto(networkQuality, r.Float, 4);
            water = null;
            waterVersion = -1; // Wassermaske neu aus dem Gelände
        }
    }
}
