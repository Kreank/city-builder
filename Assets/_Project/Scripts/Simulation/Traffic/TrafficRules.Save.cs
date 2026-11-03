using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// Spielstand: alle Spieler-Regeln je Knoten (Steuerung, Schilder, Abbiegeverbote, Grünzeit) und je Segment
    /// (Sperrung, Fahrverbote, Tempo).
    /// <para>Migration: Version 1 kannte noch keine Grünzeit je Ampel (vor Etappe 2D) – fehlt → Standard 20 s.</para>
    /// </summary>
    public sealed partial class TrafficRules : ISaveable
    {
        public string SaveId => "rules";
        public int SaveVersion => 2;

        public void Write(SaveWriter w)
        {
            w.Int(Version);
            // Nachschlage-Tabellen → kanonisch nach Schlüssel
            w.IntDictionary(nodes, n =>
            {
                w.Name(n.Control);
                w.IntDictionary(n.Signs, s => w.Name(s));
                var bans = new List<(int from, int to)>(n.BannedTurns.Keys);
                bans.Sort();
                w.Int(bans.Count);
                foreach (var b in bans) { w.Int(b.from); w.Int(b.to); w.Name(n.BannedTurns[b]); }
                w.Float(n.GreenSeconds); // seit Version 2
            });
            w.IntDictionary(segments, s =>
            {
                w.Bool(s.Closed);
                w.Name(s.Banned);
                w.Float(s.SpeedLimitKmh);
            });
        }

        public void Read(SaveReader r, int version)
        {
            nodes.Clear();
            segments.Clear();
            Version = r.Int();
            int n = r.Count(9);
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                var rules = new NodeRules { Control = r.Name<JunctionControl>() };
                int signs = r.Count(5);
                for (int k = 0; k < signs; k++) { int seg = r.Int(); rules.Signs[seg] = r.Name<ApproachSign>(); }
                int bans = r.Count(9);
                for (int k = 0; k < bans; k++)
                {
                    int from = r.Int(), to = r.Int();
                    rules.BannedTurns[(from, to)] = r.Name<VehicleClass>();
                }
                rules.GreenSeconds = version >= 2 ? r.Float() : 20f; // Migration v1 → v2
                nodes[id] = rules;
            }
            int s = r.Count(10);
            for (int i = 0; i < s; i++)
            {
                int id = r.Int();
                segments[id] = new SegmentRules { Closed = r.Bool(), Banned = r.Name<VehicleClass>(), SpeedLimitKmh = r.Float() };
            }
        }
    }
}
