using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Spielstand der Lärmschutzwände: Stücke (Id, Enden) in Speicher-Reihenfolge, nächste Id, Version.</summary>
    public sealed partial class NoiseBarrierSystem : ISaveable
    {
        public string SaveId => "noisebarriers";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextId);
            w.Int(Version);
            barriers.WriteLayout(w, (id, b) =>
            {
                w.Int(b.Id);
                w.Vector2(b.A);
                w.Vector2(b.B);
            });
        }

        public void Read(SaveReader r, int version)
        {
            nextId = r.Int();
            Version = r.Int();
            barriers.ReadLayout(r, () =>
            {
                var b = new NoiseBarrier { Id = r.Int() };
                b.A = r.Vector2();
                b.B = r.Vector2();
                return new KeyValuePair<int, NoiseBarrier>(b.Id, b);
            });
        }
    }
}
