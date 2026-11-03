using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Spielstand: Knoten (Position, Höhe, Segmentreihenfolge), Segmente (Typ, Form, Richtung, Flags, Höhenprofil), Id-Zähler.</summary>
    public sealed partial class RoadNetwork : ISaveable
    {
        /// <summary>
        /// Das Netz wurde komplett ersetzt (Laden) – ohne einzelne Added/Removed-Ereignisse (die würden Gelände formen,
        /// Gebäude abreißen …). Abgeleitete Daten (Spurgraph usw.) bauen sich hier neu auf.
        /// </summary>
        public event Action Reloaded;

        public string SaveId => "roads";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextNodeId);
            w.Int(nextSegmentId);
            w.Int(Version);
            nodes.WriteLayout(w, (id, n) =>
            {
                w.Int(id);
                w.Vector2(n.Position);
                w.Float(n.Height);
                w.IntList(n.segmentIds);
            });
            segments.WriteLayout(w, (id, s) =>
            {
                w.Int(id);
                w.Int(s.Start.Id);
                w.Int(s.End.Id);
                w.Vector2(s.Control);
                w.String(s.Type.Id);
                w.Int((int)s.Flags);
                w.Floats(s.Profile);
            });
        }

        public void Read(SaveReader r, int version)
        {
            nextNodeId = r.Int();
            nextSegmentId = r.Int();
            int savedVersion = r.Int();
            nodes.ReadLayout(r, () =>
            {
                int id = r.Int();
                var n = new RoadNode(id, r.Vector2(), r.Float());
                foreach (int s in r.IntList()) n.segmentIds.Add(s);
                return new KeyValuePair<int, RoadNode>(id, n);
            });
            segments.ReadLayout(r, () =>
            {
                int id = r.Int();
                var start = GetNode(r.Int()) ?? throw new SaveFormatException($"Segment {id}: Startknoten fehlt.");
                var end = GetNode(r.Int()) ?? throw new SaveFormatException($"Segment {id}: Endknoten fehlt.");
                var control = r.Vector2();
                string typeId = r.String();
                var type = RoadCatalog.Get(typeId ?? "") ?? throw new SaveFormatException($"Unbekannter Straßentyp „{typeId}“.");
                var flags = (RoadSegmentFlags)r.Int();
                var profile = r.Floats();
                return new KeyValuePair<int, RoadSegment>(id, new RoadSegment(id, start, end, control, type, flags, Ground, profile));
            });
            foreach (var n in nodes.Values)
                foreach (int s in n.segmentIds)
                    if (!segments.ContainsKey(s)) throw new SaveFormatException($"Knoten {n.Id} verweist auf fehlendes Segment {s}.");
            Version = savedVersion;
            Reloaded?.Invoke();
        }
    }
}
