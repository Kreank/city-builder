using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Spielstand-Hilfen für Raster (Werte bitgenau + Version).</summary>
    public sealed partial class GridLayer
    {
        public void WriteState(SaveWriter w)
        {
            w.Int(Width);
            w.Int(Height);
            w.Int(Version);
            w.Floats(cells);
        }

        public void ReadState(SaveReader r)
        {
            int width = r.Int(), height = r.Int();
            if (width != Width || height != Height) throw new SaveFormatException($"Raster „{Name}“: Größe {width}×{height} passt nicht ({Width}×{Height}).");
            Version = r.Int();
            r.FloatsInto(cells);
        }
    }

    /// <summary>Spielstand: Lärm- und Abgasraster (Lärm wird stündlich neu gezeichnet, liegt aber dazwischen als Zustand vor).</summary>
    public sealed partial class EnvironmentLayers : ISaveable
    {
        public string SaveId => "environment";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            Noise.WriteState(w);
            AirPollution.WriteState(w);
        }

        public void Read(SaveReader r, int version)
        {
            Noise.ReadState(r);
            AirPollution.ReadState(r);
        }
    }
}
