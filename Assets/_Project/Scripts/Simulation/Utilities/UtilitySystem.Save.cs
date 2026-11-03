using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Spielstand der Versorgung: Bauwerke (Typ, Lage, Leistung), freie Leitungen/Rohre mit Masten, Versorgungsstatus
    /// je Gebäude (inkl. Ausfalldauer), Netze mit Bilanz und Zuordnung (exakt wie beim letzten Neuaufbau – neu gerechnet
    /// wird wie im Spiel erst, wenn sich Straßen/Gebäude/Bauwerke ändern), Zähler und Versionen.
    /// </summary>
    public sealed partial class UtilitySystem : ISaveable
    {
        public string SaveId => "utilities";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextFacilityId); w.Int(nextLineId);
            w.Int(builtRoadVersion); w.Int(builtCityVersion); w.Int(builtStructureVersion);
            w.Int(StructureVersion); w.Int(StatusVersion);
            w.Bool(AbandonmentEnabled); w.Int(AbandonedBuildings); w.Bool(pollutionActive);
            w.Int(ReroutedLines); w.Int(LinesRemovedByConstruction);
            // Atom (G3 erw.): Störfälle, Abschaltungen
            w.Bool(IncidentsEnabled); w.Float(IncidentChancePerDay); w.Int(Incidents); w.Int(incidentCheckedDay);
            w.IntDictionary(offlineUntilDay, w.Int);

            facilities.WriteLayout(w, (id, f) =>
            {
                w.Int(f.Id);
                w.String(f.Type.Id);
                w.Vector2(f.Position);
                w.Vector2(f.Front);
                w.Float(f.Height);
                w.Float(f.Output);
                w.Int(f.NetworkId);
            });
            lines.WriteLayout(w, (id, l) =>
            {
                w.Int(l.Id);
                w.Name(l.Kind);
                w.Vector2(l.A);
                w.Vector2(l.B);
                w.Int(l.NetworkId);
                w.Int(l.Revision);
                w.Int(l.Poles.Count);
                foreach (var p in l.Poles) w.Vector2(p);
            });
            w.Int(status.Count);
            foreach (int key in SaveWriter.SortedKeys(status))
            {
                w.Int(key);
                var s = status[key];
                w.Bool(s.HasPower); w.Bool(s.HasWater);
                w.Int(s.TicksWithoutPower); w.Int(s.TicksWithoutWater);
                w.Int(s.PowerNetwork); w.Int(s.WaterNetwork);
                w.Bool(s.HasSewage); w.Int(s.TicksWithoutSewage); w.Int(s.SewageNetwork);
            }
            w.Int(networks.Length);
            for (int k = 0; k < networks.Length; k++)
            {
                w.Name((UtilityKind)k);
                w.Int(networks[k].Count);
                for (int i = 0; i < networks[k].Count; i++)
                {
                    var n = networks[k][i];
                    w.Int(n.Id);
                    w.Float(n.Production); w.Float(n.Demand); w.Float(n.Delivered);
                    w.Int(n.Facilities); w.Int(n.Buildings); w.Int(n.SuppliedBuildings); w.Int(n.Lines);
                    var members = networkBuildings[k][i];
                    w.Int(members.Count);
                    foreach (var b in members) w.Int(b.Id);
                }
                w.IntDictionary(buildingNetwork[k], w.Int);
            }
        }

        public void Read(SaveReader r, int version)
        {
            nextFacilityId = r.Int(); nextLineId = r.Int();
            builtRoadVersion = r.Int(); builtCityVersion = r.Int(); builtStructureVersion = r.Int();
            StructureVersion = r.Int(); StatusVersion = r.Int();
            AbandonmentEnabled = r.Bool(); AbandonedBuildings = r.Int(); pollutionActive = r.Bool();
            ReroutedLines = r.Int(); LinesRemovedByConstruction = r.Int();
            IncidentsEnabled = r.Bool(); IncidentChancePerDay = r.Float(); Incidents = r.Int(); incidentCheckedDay = r.Int();
            r.IntDictionaryInto(offlineUntilDay, r.Int, 4);

            facilities.ReadLayout(r, () =>
            {
                int id = r.Int();
                string typeId = r.String();
                UtilityFacilityType type = null;
                foreach (var t in UtilityCatalog.All) if (t.Id == typeId) type = t;
                if (type == null) throw new SaveFormatException($"Unbekannter Versorger „{typeId}“.");
                var f = new UtilityFacility
                {
                    Id = id, Type = type, Position = r.Vector2(), Front = r.Vector2(), Height = r.Float(), Output = r.Float(), NetworkId = r.Int(),
                };
                return new KeyValuePair<int, UtilityFacility>(id, f);
            });
            lines.ReadLayout(r, () =>
            {
                var l = new UtilityLine { Id = r.Int(), Kind = r.Name<UtilityKind>(), A = r.Vector2(), B = r.Vector2(), NetworkId = r.Int(), Revision = r.Int() };
                int poles = r.Count(8);
                var list = new List<System.Numerics.Vector2>(poles);
                for (int i = 0; i < poles; i++) list.Add(r.Vector2());
                l.Poles = list.ToArray();
                return new KeyValuePair<int, UtilityLine>(l.Id, l);
            });
            status.Clear();
            int sc = r.Count(22);
            for (int i = 0; i < sc; i++)
            {
                int id = r.Int();
                status[id] = new UtilityStatus
                {
                    HasPower = r.Bool(), HasWater = r.Bool(), TicksWithoutPower = r.Int(), TicksWithoutWater = r.Int(),
                    PowerNetwork = r.Int(), WaterNetwork = r.Int(),
                    HasSewage = r.Bool(), TicksWithoutSewage = r.Int(), SewageNetwork = r.Int(),
                };
            }
            for (int k = 0; k < networks.Length; k++) { networks[k].Clear(); networkBuildings[k].Clear(); buildingNetwork[k].Clear(); }
            int kinds = r.Count(5);
            for (int kk = 0; kk < kinds; kk++)
            {
                bool knownKind = r.TryName<UtilityKind>(out var kind);
                int k = knownKind && (int)kind < networks.Length ? (int)kind : -1;
                var netList = new List<UtilityNetwork>();
                var memberList = new List<List<Building>>();
                var map = new Dictionary<int, int>();
                int nc = r.Count(36);
                for (int i = 0; i < nc; i++)
                {
                    var n = new UtilityNetwork
                    {
                        Id = r.Int(), Kind = knownKind ? kind : UtilityKind.Power, Production = r.Float(), Demand = r.Float(), Delivered = r.Float(),
                        Facilities = r.Int(), Buildings = r.Int(), SuppliedBuildings = r.Int(), Lines = r.Int(),
                    };
                    int m = r.Count(4);
                    var members = new List<Building>(m);
                    for (int j = 0; j < m; j++)
                    {
                        int id = r.Int();
                        members.Add(city.GetBuilding(id) ?? new Building { Id = id }); // seit dem letzten Neuaufbau abgerissen
                    }
                    netList.Add(n);
                    memberList.Add(members);
                }
                r.IntDictionaryInto(map, r.Int, 4);
                if (k < 0) continue; // unbekannte (künftige) Versorgungsart
                networks[k].AddRange(netList);
                networkBuildings[k].AddRange(memberList);
                foreach (var kv in map) buildingNetwork[k][kv.Key] = kv.Value;
            }
        }
    }
}
