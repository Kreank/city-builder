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
        /// <summary>
        /// v2 (Netze selbst verlegt): Leitungsart je Leitung, Umspannwerk-Oberseite/Last, Netzebene + Umspannwerte je Netz,
        /// Übergangsfrist. v1 (Straßen trugen Strom/Wasser): Leitungen = bisherige Art (Strom → Freileitung im Ortsnetz),
        /// Netze werden beim ersten Durchlauf nach den neuen Regeln neu berechnet, und es gilt eine Übergangsfrist
        /// (<see cref="MigrationGraceTicks"/>), damit der Spieler mit „Entlang Straßen verlegen“ nachrüsten kann.
        /// </summary>
        // v3 (Energie erweitert): Nennleistung/Standort/Ladestand je Bauwerk, Speicher je Netz, Abflusslage (v2 → Katalogwerte, leere Speicher).
        public int SaveVersion => 3;

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
            w.Int(MigrationGraceTicks); // v2
            w.Float(riverFlowIndex); w.Int(flowIndexDay); w.Long(energyTick); // v3 Energie erweitert

            facilities.WriteLayout(w, (id, f) =>
            {
                w.Int(f.Id);
                w.String(f.Type.Id);
                w.Vector2(f.Position);
                w.Vector2(f.Front);
                w.Float(f.Height);
                w.Float(f.Output);
                w.Int(f.NetworkId);
                w.Int(f.UpstreamNetworkId); // v2
                w.Float(f.Load);            // v2
                w.Float(f.Rated); w.Float(f.StoredKwh); w.Float(f.Charging); w.Float(f.SiteFlow); w.Float(f.SiteHead); // v3
                w.Float(f.Availability); w.Float(f.FeedWorkers); // v3
            });
            lines.WriteLayout(w, (id, l) =>
            {
                w.Int(l.Id);
                w.Name(l.Kind);
                w.String(l.Type.Id); // v2
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
                    // v2: Ebene und Umspannwerte
                    w.Name(n.Level); w.Float(n.Generation); w.Float(n.Imported); w.Float(n.TransformerCapacity); w.Int(n.Substations);
                    w.Float(n.StorageDischarge); w.Float(n.StorageCharge); w.Int(n.Storages); // v3
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
            MigrationGraceTicks = version >= 2 ? r.Int() : MigrationGraceAfterV1;
            if (version >= 3) { riverFlowIndex = r.Float(); flowIndexDay = r.Int(); energyTick = r.Long(); }
            else { riverFlowIndex = 1f; flowIndexDay = -1; energyTick = 0; }

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
                if (version >= 2) { f.UpstreamNetworkId = r.Int(); f.Load = r.Float(); }
                if (version >= 3)
                {
                    f.Rated = r.Float(); f.StoredKwh = r.Float(); f.Charging = r.Float(); f.SiteFlow = r.Float(); f.SiteHead = r.Float();
                    f.Availability = r.Float(); f.FeedWorkers = r.Float();
                }
                else f.Rated = type.Capacity; // v2: alle Bauwerke mit Katalogleistung (Wasserkraft/Speicher gab es nicht)
                return new KeyValuePair<int, UtilityFacility>(id, f);
            });
            lines.ReadLayout(r, () =>
            {
                int lineId = r.Int();
                var lineKind = r.Name<UtilityKind>();
                // v1: noch keine Leitungsarten → bisherige Art (Strom = Freileitung im Ortsnetz, 45-m-Masten wie bisher)
                var lineType = version >= 2 ? UtilityCatalog.LineTypeById(r.String()) : UtilityCatalog.DefaultLineType(lineKind);
                if (lineType == null || lineType.Kind != lineKind) throw new SaveFormatException("Unbekannte Leitungsart.");
                var l = new UtilityLine { Id = lineId, Type = lineType, A = r.Vector2(), B = r.Vector2(), NetworkId = r.Int(), Revision = r.Int() };
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
                    if (version >= 2)
                    {
                        n.Level = r.Name<GridLevel>(); n.Generation = r.Float(); n.Imported = r.Float();
                        n.TransformerCapacity = r.Float(); n.Substations = r.Int();
                        if (version >= 3) { n.StorageDischarge = r.Float(); n.StorageCharge = r.Float(); n.Storages = r.Int(); }
                    }
                    else n.Generation = n.Production;
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
            // v1: Netze stammen noch aus „Straßen tragen mit“ → beim nächsten Durchlauf nach den neuen Regeln neu aufbauen
            if (version < 2) builtStructureVersion = -1;
        }
    }
}
