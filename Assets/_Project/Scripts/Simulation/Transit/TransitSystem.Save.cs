using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Transit
{
    /// <summary>
    /// Spielstand des ÖPNV: Haltestellen (mit Wartenden in Reihenfolge), Linien (inkl. berechneter Route – wird im Spiel nur
    /// bei Straßen-/Linienänderungen neu gerechnet), Depots, Busse (Verweis auf ihr Fahrzeug im Verkehr, Fahrgäste an Bord),
    /// alle Fahrgäste, Fahrgeld-Puffer, Statistik und Zähler. <c>linesAtStop</c> wird neu aufgebaut. Gelesen nach dem
    /// Verkehr (Fahrzeuge) und den Bürgern (die ihre Fahrgäste erst in <c>AfterLoad</c> zuordnen).
    /// </summary>
    public sealed partial class TransitSystem : ISaveable
    {
        public string SaveId => "transit";
        public int SaveVersion => 2; // v2: Zeitmodell – Verkehrszeiten double, Fußweg zur Haltestelle (ReadyTime)

        // Während des Lesens/bis zum Zuordnen der Bürger: Fahrgast-Id → Objekt
        readonly Dictionary<int, Passenger> passengersById = new Dictionary<int, Passenger>();

        /// <summary>Fahrgast nach Id (nur Wartende und Fahrgäste an Bord) – für das Bürgersystem beim Laden.</summary>
        public Passenger FindPassenger(int id)
        {
            if (passengersById.Count == 0)
                foreach (var p in AllPassengers()) passengersById[p.Id] = p;
            return passengersById.TryGetValue(id, out var x) ? x : null;
        }

        /// <summary>Platzhalter für einen Fahrgast, den der ÖPNV nicht mehr führt (beendete Fahrt, noch nicht abgemeldet).</summary>
        public static Passenger DetachedPassenger(int id, int citizenId) => new Passenger { Id = id, CitizenId = citizenId, IsActive = false };

        List<Passenger> AllPassengers()
        {
            var list = new List<Passenger>();
            var seen = new HashSet<Passenger>();
            foreach (var s in stops) foreach (var p in s.Waiting) if (seen.Add(p)) list.Add(p);
            foreach (var b in buses) foreach (var p in b.Riders) if (seen.Add(p)) list.Add(p);
            return list;
        }

        public void Write(SaveWriter w)
        {
            w.Int(nextStopId); w.Int(nextLineId); w.Int(nextDepotId); w.Int(nextBusId); w.Int(nextPassengerId);
            w.Int(routeRoadVersion); w.Bool(routesDirty); w.Bool(orphansPending); w.Double(pendingFares);
            w.Int(BusCapacity); w.Double(Fare); w.Int(Version);
            w.Int(TotalBoardings); w.Int(TotalLeftBehind); w.Int(TotalGaveUp); w.Int(BusesLost); w.Double(FaresTotal);
            w.Int(StatisticalBoardings);

            var passengers = AllPassengers();
            w.Int(passengers.Count);
            foreach (var p in passengers)
            {
                w.Int(p.Id); w.Int(p.CitizenId);
                WritePlan(w, p.Plan);
                w.Int(p.Leg); w.Int(p.WaitingAtStop); w.Int(p.Bus?.Id ?? 0); w.Bool(p.IsActive);
                w.Vector2(p.Destination); w.Vector2(p.EndPosition); w.Name(p.Outcome);
                w.Double(p.WaitSince); w.Double(p.StartTime); w.Double(p.BoardTime); w.Float(p.PlannedRide); w.Double(p.ReadyTime);
                w.Float(p.WaitedSeconds); w.Int(p.FullBusesSeen); w.Bool(p.WasLate);
            }

            w.Int(stops.Count);
            foreach (var s in stops)
            {
                w.Int(s.Id); w.String(s.Name); w.Int(s.SegmentId); w.Float(s.T); w.Name(s.Side); w.Vector2(s.Position);
                w.Bool(s.Orphaned); w.Int(s.BoardedToday); w.Int(s.BoardedYesterday);
                w.Int(s.Waiting.Count);
                foreach (var p in s.Waiting) w.Int(p.Id);
            }

            w.Int(lines.Count);
            foreach (var l in lines)
            {
                w.Int(l.Id); w.String(l.Name); w.Int(l.ColorIndex); w.Bool(l.IsLoop); w.IntList(l.StopList); w.Int(l.TargetVehicles);
                w.Ints(l.Visits); w.Floats(l.LegSeconds); w.Floats(l.LegMeters);
                w.Int(l.LegRoutes.Length);
                foreach (var route in l.LegRoutes)
                {
                    w.Int(route?.Count ?? -1);
                    if (route != null) foreach (var e in route) { w.Int(e.SegmentId); w.Bool(e.Along); }
                }
                w.Float(l.CycleSeconds); w.Float(l.LengthMeters); w.Bool(l.RouteValid); w.Int(l.BrokenLeg);
                w.Int(l.ActiveVehicles); w.Double(l.NextSpawnTime);
                w.Int(l.PassengersToday); w.Int(l.PassengersYesterday); w.Int(l.PassengersTotal);
                w.Float(l.Punctuality); w.Float(l.AverageLoad); w.Int(l.LeftBehindToday);
                w.Int(l.ArrivalsToday); w.Int(l.OnTimeToday); w.Int(l.LoadSamples); w.Float(l.LoadSum);
            }

            w.Int(depots.Count);
            foreach (var d in depots)
            {
                w.Int(d.Id); w.String(d.Type.Id); w.Vector2(d.Position); w.Vector2(d.Front); w.Float(d.Height);
                w.Int(d.AccessSegmentId); w.Float(d.AccessT); w.Int(d.VehiclesOut);
            }

            w.Int(buses.Count);
            foreach (var b in buses)
            {
                w.Int(b.Id); w.Int(b.LineId); w.Int(b.DepotId); w.Name(b.State); w.Int(b.Vehicle?.Id ?? 0);
                w.Int(b.Capacity); w.Int(b.VisitIndex); w.Int(b.TargetStopId);
                w.Int(b.Riders.Count);
                foreach (var p in b.Riders) w.Int(p.Id);
                w.Double(b.LegStartTime); w.Bool(b.MeasureLeg); w.Bool(b.LastLegOnTime); w.Float(b.LastDelaySeconds); w.Int(b.StopsServed);
            }
        }

        static void WritePlan(SaveWriter w, TransitOption o)
        {
            w.Bool(o.Valid); w.Int(o.Line1); w.Int(o.Board); w.Int(o.Alight1); w.Int(o.Line2); w.Int(o.Alight2);
            w.Float(o.AccessMeters); w.Float(o.EgressMeters); w.Float(o.WaitSeconds); w.Float(o.RideSeconds);
            w.Int(o.Transfers); w.Float(o.PerceivedSeconds); w.Float(o.TotalSeconds);
        }

        static TransitOption ReadPlan(SaveReader r) => new TransitOption
        {
            Valid = r.Bool(), Line1 = r.Int(), Board = r.Int(), Alight1 = r.Int(), Line2 = r.Int(), Alight2 = r.Int(),
            AccessMeters = r.Float(), EgressMeters = r.Float(), WaitSeconds = r.Float(), RideSeconds = r.Float(),
            Transfers = r.Int(), PerceivedSeconds = r.Float(), TotalSeconds = r.Float(),
        };

        public void Read(SaveReader r, int version)
        {
            stops.Clear(); stopById.Clear(); lines.Clear(); depots.Clear(); buses.Clear(); busById.Clear(); passengersById.Clear();

            nextStopId = r.Int(); nextLineId = r.Int(); nextDepotId = r.Int(); nextBusId = r.Int(); nextPassengerId = r.Int();
            routeRoadVersion = r.Int(); routesDirty = r.Bool(); orphansPending = r.Bool(); pendingFares = r.Double();
            BusCapacity = r.Int(); Fare = r.Double(); Version = r.Int();
            TotalBoardings = r.Int(); TotalLeftBehind = r.Int(); TotalGaveUp = r.Int(); BusesLost = r.Int(); FaresTotal = r.Double();
            StatisticalBoardings = r.Int();

            var busOf = new Dictionary<Passenger, int>();
            int pc = r.Count(40);
            for (int i = 0; i < pc; i++)
            {
                var p = new Passenger { Id = r.Int(), CitizenId = r.Int() };
                p.Plan = ReadPlan(r);
                p.Leg = r.Int(); p.WaitingAtStop = r.Int();
                int bus = r.Int();
                p.IsActive = r.Bool();
                p.Destination = r.Vector2(); p.EndPosition = r.Vector2(); p.Outcome = r.Name<RideOutcome>();
                if (version >= 2)
                {
                    p.WaitSince = r.Double(); p.StartTime = r.Double(); p.BoardTime = r.Double(); p.PlannedRide = r.Float(); p.ReadyTime = r.Double();
                }
                else
                {
                    // v1: Fahrgast wartete sofort an der Haltestelle (kein Fußweg) → schon angekommen
                    p.WaitSince = r.Float(); p.StartTime = r.Float(); p.BoardTime = r.Float(); p.PlannedRide = r.Float(); p.ReadyTime = p.StartTime;
                }
                p.WaitedSeconds = r.Float(); p.FullBusesSeen = r.Int(); p.WasLate = r.Bool();
                if (passengersById.ContainsKey(p.Id)) throw new SaveFormatException($"Fahrgast {p.Id} doppelt.");
                passengersById[p.Id] = p;
                if (bus != 0) busOf[p] = bus;
            }
            Passenger FindP(int id) => passengersById.TryGetValue(id, out var p) ? p : throw new SaveFormatException($"Fahrgast {id} fehlt.");

            int sc = r.Count(30);
            for (int i = 0; i < sc; i++)
            {
                var s = new BusStop { Id = r.Int(), Name = r.String(), SegmentId = r.Int(), T = r.Float(), Side = r.Name<StopSide>(), Position = r.Vector2() };
                s.Orphaned = r.Bool(); s.BoardedToday = r.Int(); s.BoardedYesterday = r.Int();
                int wc = r.Count(4);
                for (int k = 0; k < wc; k++) s.Waiting.Add(FindP(r.Int()));
                stops.Add(s);
                stopById[s.Id] = s;
            }

            int lc = r.Count(40);
            for (int i = 0; i < lc; i++)
            {
                var l = new TransitLine { Id = r.Int(), Name = r.String(), ColorIndex = r.Int(), IsLoop = r.Bool() };
                l.StopList.AddRange(r.IntList() ?? new List<int>());
                l.TargetVehicles = r.Int();
                l.Visits = r.Ints() ?? new int[0];
                l.LegSeconds = r.Floats() ?? new float[0];
                l.LegMeters = r.Floats() ?? new float[0];
                int legs = r.Count(4);
                l.LegRoutes = new List<EdgeKey>[legs];
                for (int k = 0; k < legs; k++)
                {
                    int n = r.CountOrNull(5);
                    if (n < 0) continue;
                    var route = new List<EdgeKey>(n);
                    for (int e = 0; e < n; e++) route.Add(new EdgeKey(r.Int(), r.Bool()));
                    l.LegRoutes[k] = route;
                }
                l.CycleSeconds = r.Float(); l.LengthMeters = r.Float(); l.RouteValid = r.Bool(); l.BrokenLeg = r.Int();
                l.ActiveVehicles = r.Int(); l.NextSpawnTime = version >= 2 ? r.Double() : r.Float();
                l.PassengersToday = r.Int(); l.PassengersYesterday = r.Int(); l.PassengersTotal = r.Int();
                l.Punctuality = r.Float(); l.AverageLoad = r.Float(); l.LeftBehindToday = r.Int();
                l.ArrivalsToday = r.Int(); l.OnTimeToday = r.Int(); l.LoadSamples = r.Int(); l.LoadSum = r.Float();
                lines.Add(l);
            }

            int dc = r.Count(30);
            for (int i = 0; i < dc; i++)
            {
                int id = r.Int();
                string typeId = r.String();
                DepotType type = null;
                foreach (var t in TransitCatalog.Depots) if (t.Id == typeId) type = t;
                if (type == null) throw new SaveFormatException($"Unbekanntes Depot „{typeId}“.");
                depots.Add(new TransitDepot
                {
                    Id = id, Type = type, Position = r.Vector2(), Front = r.Vector2(), Height = r.Float(),
                    AccessSegmentId = r.Int(), AccessT = r.Float(), VehiclesOut = r.Int(),
                });
            }

            var vehicles = new Dictionary<int, Vehicle>();
            foreach (var v in traffic.Vehicles) vehicles[v.Id] = v;
            int bc = r.Count(30);
            for (int i = 0; i < bc; i++)
            {
                var b = new Bus { Id = r.Int(), LineId = r.Int(), DepotId = r.Int(), State = r.Name<BusState>() };
                int vid = r.Int();
                b.Vehicle = vid == 0 ? null : vehicles.TryGetValue(vid, out var v) ? v : throw new SaveFormatException($"Bus {b.Id}: Fahrzeug {vid} fehlt.");
                b.Capacity = r.Int(); b.VisitIndex = r.Int(); b.TargetStopId = r.Int();
                int rc = r.Count(4);
                for (int k = 0; k < rc; k++) b.Riders.Add(FindP(r.Int()));
                b.LegStartTime = version >= 2 ? r.Double() : r.Float(); b.MeasureLeg = r.Bool(); b.LastLegOnTime = r.Bool(); b.LastDelaySeconds = r.Float(); b.StopsServed = r.Int();
                buses.Add(b);
                busById[b.Id] = b;
            }
            foreach (var kv in busOf) kv.Key.Bus = GetBus(kv.Value);
            RebuildLinesAtStops();
        }
    }
}
