using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// Spielstand des Verkehrs. <b>Entscheidung: laufende Fahrzeuge werden vollständig gespeichert</b> (Spur bzw.
    /// Kreuzungsverbindung, Position, Tempo, Route, Ziel, Warte-/Spurwechselzustand, Reihenfolge auf jeder Spur),
    /// dazu Stau-Index je Spur, Verkehrszeit, Zufall und Zähler. Grund: Nur so laufen geladene Welten bitgleich weiter
    /// (Determinismus-Test, später Multiplayer/Replays); Bürger und Lkw-Ladungen bleiben ihrem Fahrzeug zugeordnet und
    /// kommen ganz normal an – niemand geht verloren, kein Sonderweg „Fahrten neu starten“.
    /// Spurgraph, Kreuzungsregeln-Cache und Außenanschlüsse sind abgeleitet und werden neu aufgebaut
    /// (<see cref="LaneGraph.RebuildAll"/> beim Laden der Straßen).
    /// </summary>
    public sealed partial class TrafficSystem : ISaveable, IBeforeSave
    {
        public string SaveId => "traffic";
        public int SaveVersion => 2; // v2: Zeitmodell – Verkehrszeit aus den Schritten (double), Fahrzeugzeiten double

        /// <summary>
        /// Ausstehende Spurgraph-Änderungen (Straßenbau ohne Tick dazwischen, z. B. im Pausenmodus) vor dem Speichern
        /// abschließen – genau das täte der nächste Tick. Danach entsprechen alle Verbindungen dem Netz, Fahrzeugverweise
        /// lassen sich eindeutig speichern.
        /// </summary>
        public void BeforeSave() => Lanes.Update();

        public void Write(SaveWriter w)
        {
            rng.Write(w);
            w.Long(tick);
            w.Int(nextId);
            w.Float(Demand);
            w.Float(ThroughTrafficRate);
            w.Int(MaxVehicles);
            w.Int(ScheduledVehicleReserve);
            w.Bool(deadEndsAreExternal);
            w.Int(Lanes.Version);
            w.Int(Paths.Sequence);
            var s = Stats;
            w.Int(s.Active); w.Int(s.Spawned); w.Int(s.Arrived); w.Int(s.Despawned); w.Float(s.AverageSpeedKmh); w.Int(s.Waiting);
            w.Int(s.CitizenVehicles); w.Int(s.DeliveryVehicles); w.Int(s.CitizenTripsStarted); w.Int(s.CitizenTripsArrived);
            w.Int(s.CitizenTripsFailed);

            // Stau-Index je Spur (alle Spuren, Netz-Reihenfolge)
            w.Int(network.SegmentCount);
            foreach (var seg in network.Segments)
            {
                var lanes = Lanes.GetLanes(seg.Id);
                w.Int(seg.Id);
                w.Int(lanes.Length);
                foreach (var l in lanes) w.Float(l.SpeedRatio);
            }

            // Fahrzeuge (Listenreihenfolge = Schrittreihenfolge)
            w.Int(Vehicles.Count);
            foreach (var v in Vehicles)
            {
                w.Int(v.Id);
                w.Bool(removed.Contains(v));
                w.Name(v.Class);
                w.Float(v.Length);
                w.Float(v.MaxSpeed);
                w.Float(v.Acceleration);
                w.Int(v.Variant);
                WriteLane(w, v.Lane);
                WriteConnection(w, v.Connection);
                w.Float(v.S);
                w.Float(v.Speed);
                w.Int(v.Route.Count);
                foreach (var e in v.Route) { w.Int(e.SegmentId); w.Bool(e.Along); }
                w.Int(v.RouteIndex);
                w.Int(v.Destination?.Id ?? 0);
                if (v.Destination != null) { w.Vector2(v.Destination.Position); w.Float(v.Destination.Height); }
                WriteConnection(w, v.Planned);
                w.Int(v.TripId);
                w.Int(v.DestinationSegmentId);
                w.Float(v.DestinationT);
                w.Double(v.StartTime);
                w.Float(v.WaitTime);
                w.Float(v.StopLineWait);
                w.Float(v.LaneChangeCooldown);
                w.Float(v.Lateral);
                w.Long(v.LastTick);
                w.Int(v.Reroutes);
                w.Vector2(v.Position);
                w.Float(v.Height);
                w.Vector2(v.Direction);
                w.Vector2(v.PrevPosition);
                w.Float(v.PrevHeight);
                w.Vector2(v.PrevDirection);
                // ÖPNV: Linienfahrzeuge
                w.Bool(v.IsScheduled);
                w.Int(v.TransitId);
                w.Bool(v.AtStop);
                w.Double(v.HoldUntil);
                w.Byte(v.DestinationDirection);
            }

            // Reihenfolge auf Spuren und Kreuzungsverbindungen (vorderstes zuerst)
            var occupied = new List<Lane>();
            foreach (var seg in network.Segments)
                foreach (var l in Lanes.GetLanes(seg.Id))
                    if (l.Vehicles.Count > 0) occupied.Add(l);
            w.Int(occupied.Count);
            foreach (var l in occupied)
            {
                WriteLane(w, l);
                w.Int(l.Vehicles.Count);
                foreach (var v in l.Vehicles) w.Int(v.Id);
            }
            var busy = new List<LaneConnection>();
            foreach (var n in network.Nodes)
                foreach (var c in Lanes.GetConnections(n.Id))
                    if (c.Vehicles.Count > 0) busy.Add(c);
            w.Int(busy.Count);
            foreach (var c in busy)
            {
                WriteConnection(w, c);
                w.Int(c.Vehicles.Count);
                foreach (var v in c.Vehicles) w.Int(v.Id);
            }
        }

        void WriteLane(SaveWriter w, Lane l)
        {
            if (l == null) { w.Int(0); return; }
            w.Int(l.Segment.Id);
            w.Int(Array.IndexOf(Lanes.GetLanes(l.Segment.Id), l));
        }

        void WriteConnection(SaveWriter w, LaneConnection c)
        {
            if (c == null) { w.Int(0); return; }
            var list = Lanes.GetConnections(c.Node.Id);
            int index = -1;
            for (int i = 0; i < list.Count; i++) if (list[i] == c) { index = i; break; }
            if (index < 0) { w.Int(0); return; } // nicht mehr im Graphen (kann nach BeforeSave nicht vorkommen)
            w.Int(c.Node.Id);
            w.Int(index);
        }

        Lane ReadLane(SaveReader r)
        {
            int seg = r.Int();
            if (seg == 0) return null;
            int i = r.Int();
            var lanes = Lanes.GetLanes(seg);
            if (i < 0 || i >= lanes.Length) throw new SaveFormatException($"Spur {seg}/{i} gibt es nicht.");
            return lanes[i];
        }

        LaneConnection ReadConnection(SaveReader r)
        {
            int node = r.Int();
            if (node == 0) return null;
            int i = r.Int();
            var list = Lanes.GetConnections(node);
            if (i < 0 || i >= list.Count) throw new SaveFormatException($"Kreuzungsverbindung {node}/{i} gibt es nicht.");
            return list[i];
        }

        public void Read(SaveReader r, int version)
        {
            // Spurgraph ist schon aus dem Straßennetz neu aufgebaut (RoadNetwork.Reloaded); Fahrzeuge der frischen Welt verwerfen
            foreach (var l in Lanes.AllLanes) l.Vehicles.Clear();
            foreach (var node in network.Nodes) foreach (var c in Lanes.GetConnections(node.Id)) c.Vehicles.Clear();
            Vehicles.Clear();
            removed.Clear();

            rng.Read(r);
            tick = r.Long();
            if (version < 2) r.Float(); // v1: Verkehrszeit als float (= tick × Dt)
            nextId = r.Int();
            Demand = r.Float();
            ThroughTrafficRate = r.Float();
            MaxVehicles = r.Int();
            ScheduledVehicleReserve = r.Int();
            deadEndsAreExternal = r.Bool();
            endpointsVersion = -1;
            Lanes.RestoreVersion(r.Int());
            Paths.Sequence = r.Int();
            var s = Stats;
            s.Active = r.Int(); s.Spawned = r.Int(); s.Arrived = r.Int(); s.Despawned = r.Int(); s.AverageSpeedKmh = r.Float(); s.Waiting = r.Int();
            s.CitizenVehicles = r.Int(); s.DeliveryVehicles = r.Int(); s.CitizenTripsStarted = r.Int(); s.CitizenTripsArrived = r.Int();
            s.CitizenTripsFailed = r.Int();

            int segs = r.Count(8);
            for (int i = 0; i < segs; i++)
            {
                int id = r.Int();
                int count = r.Count(4);
                var lanes = Lanes.GetLanes(id);
                for (int k = 0; k < count; k++)
                {
                    float ratio = r.Float();
                    if (k < lanes.Length) lanes[k].SpeedRatio = ratio;
                }
            }

            var byId = new Dictionary<int, Vehicle>();
            int n = r.Count(100);
            for (int i = 0; i < n; i++)
            {
                var v = new Vehicle { Id = r.Int() };
                bool isRemoved = r.Bool();
                v.Class = r.Name(VehicleClass.Car);
                v.Length = r.Float();
                v.MaxSpeed = r.Float();
                v.Acceleration = r.Float();
                v.Variant = r.Int();
                v.Lane = ReadLane(r);
                v.Connection = ReadConnection(r);
                v.S = r.Float();
                v.Speed = r.Float();
                int route = r.Count(5);
                v.Route = new List<EdgeKey>(route);
                for (int k = 0; k < route; k++) v.Route.Add(new EdgeKey(r.Int(), r.Bool()));
                v.RouteIndex = r.Int();
                int dest = r.Int();
                if (dest != 0)
                {
                    var p = r.Vector2();
                    float h = r.Float();
                    // inzwischen abgerissener Zielknoten → losgelöster Platzhalter mit derselben Id (wie im laufenden Spiel)
                    v.Destination = network.GetNode(dest) ?? new RoadNode(dest, p, h);
                }
                v.Planned = ReadConnection(r);
                v.TripId = r.Int();
                v.DestinationSegmentId = r.Int();
                v.DestinationT = r.Float();
                v.StartTime = version >= 2 ? r.Double() : r.Float();
                v.WaitTime = r.Float();
                v.StopLineWait = r.Float();
                v.LaneChangeCooldown = r.Float();
                v.Lateral = r.Float();
                v.LastTick = r.Long();
                v.Reroutes = r.Int();
                v.Position = r.Vector2();
                v.Height = r.Float();
                v.Direction = r.Vector2();
                v.PrevPosition = r.Vector2();
                v.PrevHeight = r.Float();
                v.PrevDirection = r.Vector2();
                v.IsScheduled = r.Bool();
                v.TransitId = r.Int();
                v.AtStop = r.Bool();
                v.HoldUntil = version >= 2 ? r.Double() : r.Float();
                v.DestinationDirection = r.Byte();
                if (byId.ContainsKey(v.Id)) throw new SaveFormatException($"Fahrzeug {v.Id} doppelt.");
                byId[v.Id] = v;
                Vehicles.Add(v);
                if (isRemoved) removed.Add(v);
            }

            Vehicle Find(int id) => byId.TryGetValue(id, out var v) ? v : throw new SaveFormatException($"Fahrzeug {id} fehlt.");
            int lanesWithCars = r.Count(12);
            for (int i = 0; i < lanesWithCars; i++)
            {
                var lane = ReadLane(r) ?? throw new SaveFormatException("Spur fehlt.");
                int count = r.Count(4);
                for (int k = 0; k < count; k++) lane.Vehicles.Add(Find(r.Int()));
            }
            int busy = r.Count(12);
            for (int i = 0; i < busy; i++)
            {
                var c = ReadConnection(r) ?? throw new SaveFormatException("Kreuzungsverbindung fehlt.");
                int count = r.Count(4);
                for (int k = 0; k < count; k++) c.Vehicles.Add(Find(r.Int()));
            }
        }
    }

    public sealed partial class LaneGraph
    {
        /// <summary>Spurgraph komplett aus dem aktuellen Netz neu aufbauen (nach dem Laden der Straßen).</summary>
        internal void RebuildAll()
        {
            segmentLanes.Clear();
            nodeConnections.Clear();
            dirtyNodes.Clear();
            foreach (var s in network.Segments) OnSegmentAdded(s);
            Update();
        }

        internal void RestoreVersion(int version) => Version = version;
    }

    public sealed partial class Pathfinder
    {
        /// <summary>Laufende Nummer der offenen Liste (Gleichstand-Reihenfolge der Suche) – Teil des Spielstands.</summary>
        internal int Sequence { get => seq; set => seq = value; }
    }
}
