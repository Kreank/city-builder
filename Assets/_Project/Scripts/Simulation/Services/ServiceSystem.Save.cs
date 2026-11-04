using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// Spielstand der Dienste: Dienstgebäude (Typ, Lage, Anschluss, Auslastung, Lager der Müllhalde, Abholmenge),
    /// Zustand je Gebäude (Abdeckung je Kanal, liegender Müll, Brandrisiko), Zuordnung Gebäude ↔ Dienst wie beim letzten
    /// Neuaufbau (wird im Spiel nur bei Änderungen neu gerechnet), Versionen. Die Reichweite (Dijkstra) wird aus dem
    /// gespeicherten Anschluss neu berechnet.
    /// </summary>
    public sealed partial class ServiceSystem : ISaveable
    {
        public string SaveId => "services";
        // v2: Frei setzen – Ticks ohne Zufahrt je Dienstgebäude (v1 → 0)
        public int SaveVersion => 2;

        public void Write(SaveWriter w)
        {
            w.Int(nextId);
            w.Int(builtRoadVersion); w.Int(builtStructureVersion); w.Int(reachVersion); w.Int(linkedReachVersion); w.Int(linkedCityVersion);
            w.Bool(pollutionActive);
            w.Int(StructureVersion); w.Int(CoverageVersion);
            facilities.WriteLayout(w, (id, f) =>
            {
                w.Int(f.Id);
                w.String(f.Type.Id);
                w.Vector2(f.Position);
                w.Vector2(f.Front);
                w.Float(f.Height);
                w.Int(f.AccessSegmentId);
                w.Float(f.AccessT);
                w.Float(f.Load);
                w.Int(f.ServedBuildings);
                w.Float(f.Stored);
                w.Float(f.CollectedPerDay);
                w.Bool(f.Reach != null);
                w.Int(f.NoRoadTicks); // v2
            });
            states.WriteLayout(w, (id, s) =>
            {
                w.Int(id);
                w.KeyedFloats(Channels, s.Coverage); // je Kanal über den Namen (Kanäle kommen dazu)
                w.Float(s.Garbage);
                w.Float(s.FireRisk);
            });
            w.Int(sortedFacilities.Count);
            foreach (var f in sortedFacilities) w.Int(f.Id);
            w.Int(sortedBuildings.Count);
            foreach (var b in sortedBuildings) w.Int(b.Id);
            w.Int(links.Length);
            for (int c = 0; c < links.Length; c++)
            {
                w.String(Channels[c]);
                w.Int(links[c].Count);
                foreach (var kv in links[c])
                {
                    w.Int(kv.Key);
                    w.Int(kv.Value.Building.Id);
                    w.Int(kv.Value.Assigned?.Id ?? 0);
                    w.Int(kv.Value.Links.Count);
                    foreach (var l in kv.Value.Links) { w.Int(l.Facility.Id); w.Float(l.Factor); }
                }
            }
        }

        public void Read(SaveReader r, int version)
        {
            nextId = r.Int();
            builtRoadVersion = r.Int(); builtStructureVersion = r.Int(); reachVersion = r.Int(); linkedReachVersion = r.Int(); linkedCityVersion = r.Int();
            pollutionActive = r.Bool();
            StructureVersion = r.Int(); CoverageVersion = r.Int();
            facilities.ReadLayout(r, () =>
            {
                int id = r.Int();
                string typeId = r.String();
                var type = ServiceCatalog.Get(typeId) ?? throw new SaveFormatException($"Unbekannter Dienst „{typeId}“.");
                var f = new ServiceFacility
                {
                    Id = id, Type = type, Position = r.Vector2(), Front = r.Vector2(), Height = r.Float(),
                    AccessSegmentId = r.Int(), AccessT = r.Float(), Load = r.Float(), ServedBuildings = r.Int(),
                    Stored = r.Float(), CollectedPerDay = r.Float(),
                };
                bool hasReach = r.Bool();
                if (version >= 2) f.NoRoadTicks = r.Int();
                // Reichweite aus dem gespeicherten Anschluss neu (reine Funktion des Straßennetzes)
                if (hasReach) f.Reach = ComputeReach(f.AccessSegmentId, f.AccessT, f.Type.RangeSeconds);
                return new KeyValuePair<int, ServiceFacility>(id, f);
            });
            states.ReadLayout(r, () =>
            {
                int id = r.Int();
                var s = new BuildingState();
                r.KeyedFloatsInto(Channels, s.Coverage);
                s.Garbage = r.Float();
                s.FireRisk = r.Float();
                return new KeyValuePair<int, BuildingState>(id, s);
            });
            sortedFacilities.Clear();
            int fc = r.Count(4);
            for (int i = 0; i < fc; i++)
            {
                var f = GetFacility(r.Int());
                if (f != null) sortedFacilities.Add(f); // seit dem letzten Neuaufbau abgerissen → wird ohnehin neu gebaut
            }
            sortedBuildings.Clear();
            int bc = r.Count(4);
            for (int i = 0; i < bc; i++)
            {
                int id = r.Int();
                sortedBuildings.Add(city.GetBuilding(id) ?? new Building { Id = id });
            }
            foreach (var l in links) l.Clear();
            int channels = r.Count(5);
            for (int ci = 0; ci < channels; ci++)
            {
                int c = Array.IndexOf(Channels, r.String());
                var target = c >= 0 ? links[c] : new Dictionary<int, BuildingLinks>(); // unbekannter Kanal → verwerfen
                int n = r.Count(16);
                for (int i = 0; i < n; i++)
                {
                    int key = r.Int();
                    int bid = r.Int();
                    var bl = new BuildingLinks { Building = city.GetBuilding(bid) ?? new Building { Id = bid } };
                    bl.Assigned = GetFacility(r.Int());
                    int m = r.Count(8);
                    for (int k = 0; k < m; k++)
                    {
                        var f = GetFacility(r.Int());
                        float factor = r.Float();
                        if (f != null) bl.Links.Add(new Link { Facility = f, Factor = factor });
                    }
                    target[key] = bl;
                }
            }
        }
    }
}
