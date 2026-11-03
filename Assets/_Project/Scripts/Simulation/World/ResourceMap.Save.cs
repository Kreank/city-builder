using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Spielstand: verbleibende Vorkommen je Ressource (Entnahmen, Erneuerung) und der natürliche Ausgangszustand
    /// (Straßen roden/versiegeln ihn dauerhaft) – beides nicht aus dem Seed rekonstruierbar.
    /// </summary>
    public sealed partial class ResourceMap : ISaveable
    {
        public string SaveId => "resources";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(layers.Length);
            for (int k = 0; k < layers.Length; k++)
            {
                w.String(ResourceKind.Catalog[k].Id);
                layers[k].WriteState(w);
                w.Floats(natural[k]);
            }
        }

        public void Read(SaveReader r, int version)
        {
            int n = r.Count(8);
            for (int i = 0; i < n; i++)
            {
                string id = r.String();
                if (id == null || !index.TryGetValue(id, out int k))
                {
                    // Unbekannte (künftige) Ressource überspringen
                    var skip = new Environment.GridLayer("skip", Origin, Width, Height, CellSize);
                    skip.ReadState(r);
                    r.Floats();
                    continue;
                }
                layers[k].ReadState(r);
                r.FloatsInto(natural[k]);
            }
        }
    }
}
