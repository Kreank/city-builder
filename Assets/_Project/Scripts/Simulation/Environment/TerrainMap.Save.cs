using System;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Spielstand: aktuelle Höhe (Straßen, Bauplätze, Terraforming), natürliche Höhe (Terraforming ändert auch sie),
    /// bearbeiteter Boden (<see cref="WorkedAt"/>), Wasserspiegel. Nicht aus dem Seed neu erzeugbar, daher vollständig.
    /// </summary>
    public sealed partial class TerrainMap : ISaveable
    {
        public string SaveId => "terrain";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(Resolution);
            w.Float(WaterLevel);
            w.Int(Version);
            w.Floats(current);
            w.Floats(natural);
            w.Floats(worked);
        }

        public void Read(SaveReader r, int version)
        {
            int res = r.Int();
            if (res != Resolution) throw new SaveFormatException($"Geländeauflösung {res} passt nicht zur Welt ({Resolution}).");
            WaterLevel = r.Float();
            Version = r.Int();
            r.FloatsInto(current);
            r.FloatsInto(natural);
            r.FloatsInto(worked);
            // Darstellung/Caches (Bodenwert-Gelände, GroundView) neu: ganze Karte geändert
            Changed?.Invoke(Origin, Origin + new System.Numerics.Vector2((Resolution - 1) * CellSize));
        }
    }
}
