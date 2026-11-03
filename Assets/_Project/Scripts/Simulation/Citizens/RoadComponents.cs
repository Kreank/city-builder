using System.Collections.Generic;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Zusammenhängende Teile des Straßennetzes (ohne Fahrtrichtung). Günstige Vorprüfung „erreichbar?“
    /// für Job- und Ladensuche, damit nicht für jeden Kandidaten eine Wegsuche nötig ist.
    /// Wird neu berechnet, wenn sich das Netz ändert.
    /// </summary>
    public sealed class RoadComponents
    {
        readonly RoadNetwork network;
        readonly Dictionary<int, int> parent = new Dictionary<int, int>();
        int version = -1;

        public RoadComponents(RoadNetwork network) => this.network = network;

        /// <summary>Komponente eines Knotens (-1 = unbekannt).</summary>
        public int OfNode(int nodeId)
        {
            Refresh();
            return parent.ContainsKey(nodeId) ? Find(nodeId) : -1;
        }

        public int OfSegment(int segmentId)
        {
            var s = network.GetSegment(segmentId);
            return s == null ? -1 : OfNode(s.Start.Id);
        }

        void Refresh()
        {
            if (version == network.Version) return;
            version = network.Version;
            parent.Clear();
            foreach (var n in network.Nodes) parent[n.Id] = n.Id;
            foreach (var s in network.Segments)
            {
                int a = Find(s.Start.Id), b = Find(s.End.Id);
                if (a != b) parent[a] = b;
            }
        }

        int Find(int x)
        {
            while (true)
            {
                int p = parent[x];
                if (p == x) return x;
                int gp = parent[p];
                parent[x] = gp; // Pfadhalbierung
                x = p;
            }
        }
    }
}
