using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Spielstand „rail“: Gleisnetz (gleicher Code wie die Straßen), Hauptstrecken-Enden, Bahnhöfe mit Statistik, Züge
    /// vollständig (Fahrweg, Position, Tempo, Halte, Reservierungen), Fahrgäste (wartend/draußen/im Zug), Schrankenzustand,
    /// Zähler und offene Entgelte. Abgeleitet (nach dem Laden neu): Fahrweg-Graph, Bahnsteige, Zufahrten, Bahnübergänge
    /// (Zustand wird über die Lage übernommen), Gateway-Status.
    /// </summary>
    public sealed partial class RailSystem : ISaveable, IAfterLoad
    {
        public string SaveId => "rail";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            Tracks.Write(w);
            w.Int(nextStationId); w.Int(nextTrainId); w.Int(nextPassengerId);
            w.Int(Track1StartNode); w.Int(Track1EndNode); w.Int(Track2StartNode); w.Int(Track2EndNode);
            w.Int(GatewayA); w.Int(GatewayB);
            w.Double(pendingFares); w.Double(FaresTotal);
            w.Int(TrainsSpawned); w.Int(TrainsCompleted); w.Int(TrainsCancelled);
            w.Int(PassengersToday); w.Int(PassengersYesterday); w.Int(PassengersTotal); w.Int(MoveInsByRail); w.Int(GaveUpToday);
            w.Int(lastShipmentId);

            // Fahrgäste (alle aktiven, nach Id)
            var all = new List<RailPassenger>(rideByCitizen.Values);
            all.Sort((a, b) => a.Id.CompareTo(b.Id));
            w.List(all, p =>
            {
                w.Int(p.Id); w.Int(p.CitizenId); w.Bool(p.Outbound); w.Int(p.StationId); w.Name(p.State); w.Int(p.TrainId);
                w.Vector2(p.Destination); w.Double(p.StartTime); w.Double(p.ReadyTime); w.Double(p.WaitSince); w.Float(p.ExpectedSeconds);
            });
            w.List(outsideWaiting, p => w.Int(p.Id));

            w.List(stations, s =>
            {
                w.Int(s.Id); w.String(s.Type.Id); w.String(s.Name); w.Vector2(s.Position); w.Vector2(s.Front); w.Float(s.Height);
                w.Int(s.BoardedToday); w.Int(s.AlightedToday); w.Int(s.BoardedYesterday); w.Int(s.AlightedYesterday);
                w.Int(s.StopsToday); w.Int(s.StopsYesterday); w.Int(s.LoadsToday); w.Int(s.LoadsYesterday);
                w.Int(s.TotalPassengers); w.Int(s.PendingLoads);
                w.List(s.Waiting, p => w.Int(p.Id));
            });

            w.List(trains, t =>
            {
                w.Int(t.Id); w.String(t.Type.Id); w.Name(t.State); w.Int(t.FromGatewayId); w.Int(t.ToGatewayId);
                w.List(t.Route, e => { w.Int(e.SegmentId); w.Bool(e.Along); });
                w.Float(t.HeadS); w.Float(t.PrevHeadS); w.Float(t.Speed);
                w.Int(t.ReservedUntil); w.Int(t.ReleasedBefore); w.Int(t.HeadIndex);
                w.List(t.Stops, x => { w.Int(x.station); w.Float(x.s); });
                w.Int(t.NextStop); w.Double(t.DwellUntil); w.Double(t.SpawnTime); w.Double(t.WaitSince); w.Int(t.BlockedBy);
                w.Int(t.ThroughPassengers); w.Int(t.Loads); w.Bool(t.AtSignal); w.Double(t.DelaySeconds);
                w.List(t.Riders, p => w.Int(p.Id));
            });

            WriteOwners(w, segmentOwner);
            WriteOwners(w, nodeOwner);
            w.List(crossings, c => { w.Vector2(c.Position); w.Bool(c.Closed); w.Double(c.OpenAt); w.Int(c.ClosuresToday); });
        }

        static void WriteOwners(SaveWriter w, Dictionary<int, int> map)
        {
            var keys = new List<int>(map.Keys);
            keys.Sort();
            w.Int(keys.Count);
            foreach (int k in keys) { w.Int(k); w.Int(map[k]); }
        }

        static void ReadOwners(SaveReader r, Dictionary<int, int> map)
        {
            map.Clear();
            int n = r.Count(8);
            for (int i = 0; i < n; i++) { int k = r.Int(); map[k] = r.Int(); }
        }

        public void Read(SaveReader r, int version)
        {
            Tracks.Read(r, 1);
            nextStationId = r.Int(); nextTrainId = r.Int(); nextPassengerId = r.Int();
            Track1StartNode = r.Int(); Track1EndNode = r.Int(); Track2StartNode = r.Int(); Track2EndNode = r.Int();
            GatewayA = r.Int(); GatewayB = r.Int();
            pendingFares = r.Double(); FaresTotal = r.Double();
            TrainsSpawned = r.Int(); TrainsCompleted = r.Int(); TrainsCancelled = r.Int();
            PassengersToday = r.Int(); PassengersYesterday = r.Int(); PassengersTotal = r.Int(); MoveInsByRail = r.Int(); GaveUpToday = r.Int();
            lastShipmentId = r.Int();

            var byId = new Dictionary<int, RailPassenger>();
            rideByCitizen.Clear();
            foreach (var p in r.List(() => new RailPassenger
            {
                Id = r.Int(), CitizenId = r.Int(), Outbound = r.Bool(), StationId = r.Int(), State = r.Name<RailRideState>(), TrainId = r.Int(),
                Destination = r.Vector2(), StartTime = r.Double(), ReadyTime = r.Double(), WaitSince = r.Double(), ExpectedSeconds = r.Float(),
            }, 16))
            {
                byId[p.Id] = p;
                rideByCitizen[p.CitizenId] = p;
            }
            RailPassenger P(int id) => byId.TryGetValue(id, out var p) ? p : null;
            outsideWaiting.Clear();
            foreach (int id in r.List(r.Int, 4)) { var p = P(id); if (p != null) outsideWaiting.Add(p); }

            stations.Clear();
            stations.AddRange(r.List(() =>
            {
                var s = new Station { Id = r.Int() };
                string type = r.String();
                s.Type = StationCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekannter Bahnhofstyp „{type}“.");
                s.Name = r.String() ?? ""; s.Position = r.Vector2(); s.Front = r.Vector2(); s.Height = r.Float();
                s.BoardedToday = r.Int(); s.AlightedToday = r.Int(); s.BoardedYesterday = r.Int(); s.AlightedYesterday = r.Int();
                s.StopsToday = r.Int(); s.StopsYesterday = r.Int(); s.LoadsToday = r.Int(); s.LoadsYesterday = r.Int();
                s.TotalPassengers = r.Int(); s.PendingLoads = r.Int();
                foreach (int id in r.List(r.Int, 4)) { var p = P(id); if (p != null) s.Waiting.Add(p); }
                return s;
            }, 16));

            trains.Clear();
            trains.AddRange(r.List(() =>
            {
                var t = new Train { Id = r.Int() };
                string type = r.String();
                t.Type = TrainCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekannte Zuggattung „{type}“.");
                t.State = r.Name<TrainState>(); t.FromGatewayId = r.Int(); t.ToGatewayId = r.Int();
                var route = r.List(() => new EdgeKey(r.Int(), r.Bool()), 5);
                foreach (var e in route) if (Tracks.GetSegment(e.SegmentId) == null) throw new SaveFormatException($"Zug {t.Id}: Gleisstück {e.SegmentId} fehlt.");
                SetRoute(t, route);
                t.HeadS = r.Float(); t.PrevHeadS = r.Float(); t.Speed = r.Float();
                t.ReservedUntil = r.Int(); t.ReleasedBefore = r.Int(); t.HeadIndex = r.Int();
                foreach (var x in r.List(() => (r.Int(), r.Float()), 8)) t.Stops.Add(x);
                t.NextStop = r.Int(); t.DwellUntil = r.Double(); t.SpawnTime = r.Double(); t.WaitSince = r.Double(); t.BlockedBy = r.Int();
                t.ThroughPassengers = r.Int(); t.Loads = r.Int(); t.AtSignal = r.Bool(); t.DelaySeconds = r.Double();
                foreach (int id in r.List(r.Int, 4)) { var p = P(id); if (p != null) t.Riders.Add(p); }
                return t;
            }, 16));

            ReadOwners(r, segmentOwner);
            ReadOwners(r, nodeOwner);
            crossings.Clear();
            crossingsByRoad.Clear();
            foreach (var c in r.List(() => new LevelCrossing { Position = r.Vector2(), Closed = r.Bool(), OpenAt = r.Double(), ClosuresToday = r.Int() }, 21))
                crossings.Add(c); // Zustand; die Kreuzungen selbst entstehen in AfterLoad neu (Lage → Zustand)
            removedTrains.Clear();
            MarkDirty();
        }

        public void AfterLoad(SimulationWorld w)
        {
            graphDirty = true;
            EnsureGraph();
            platformsDirty = true;
            EnsurePlatforms();
            crossingsDirty = true;
            EnsureCrossings();
            UpdateGatewayStatus();
        }
    }
}
