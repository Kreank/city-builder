using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Transit
{
    /// <summary>Linienrouten (Vorschau/Fahrplan), Depots und der Busbetrieb (Einsetzen, Halten, Ein-/Aussteigen).</summary>
    public sealed partial class TransitSystem
    {
        // ------------------------------------------------------------ Route / Fahrplan

        /// <summary>
        /// Route einer (geplanten) Linie: je Abschnitt Halt k → Halt k+1 ein Weg über das Straßennetz (A*, Bus), beginnend
        /// auf der Kante, auf der der Bus am vorigen Halt steht. Fahrplanzeit je Abschnitt = Σ Teilstrecke /
        /// (Tempolimit × <see cref="TransitRules.ScheduleSpeedFactor"/>) + Kreuzungen + geplante Haltezeit.
        /// </summary>
        public LinePlan PreviewLine(IReadOnlyList<int> stopIds, bool loop)
        {
            var plan = new LinePlan { IsLoop = loop };
            if (stopIds != null)
                foreach (int id in stopIds)
                    if (stopById.ContainsKey(id) && (plan.Stops.Count == 0 || plan.Stops[plan.Stops.Count - 1] != id)) plan.Stops.Add(id);
            if (loop && plan.Stops.Count > 2 && plan.Stops[0] == plan.Stops[plan.Stops.Count - 1]) plan.Stops.RemoveAt(plan.Stops.Count - 1);
            int n = plan.Stops.Count;
            if (n < 2) return plan;
            traffic.Lanes.Update();

            var visits = new List<int>(plan.Stops);
            if (!loop) for (int i = n - 2; i >= 1; i--) visits.Add(plan.Stops[i]);
            int count = visits.Count;
            plan.Visits = visits.ToArray();
            plan.Legs = new List<EdgeKey>[count];
            plan.LegSeconds = new float[count];
            plan.LegMeters = new float[count];

            for (int k = 0; k < count; k++) ComputeLeg(plan, visits, k, k == 0 ? (EdgeKey?)null : LastEdge(plan.Legs[k - 1]));
            // Umlauf: der erste Abschnitt beginnt dort, wo der letzte ankommt
            var end = LastEdge(plan.Legs[count - 1]);
            if (end.HasValue && plan.Legs[0] != null && !plan.Legs[0][0].Equals(end.Value)) ComputeLeg(plan, visits, 0, end);
            plan.BrokenLeg = -1;
            for (int k = 0; k < count; k++)
            {
                if (plan.Legs[k] == null) { if (plan.BrokenLeg < 0) plan.BrokenLeg = k; continue; }
                plan.LengthMeters += plan.LegMeters[k];
                plan.CycleSeconds += plan.LegSeconds[k];
            }
            return plan;
        }

        static EdgeKey? LastEdge(List<EdgeKey> path) => path == null || path.Count == 0 ? (EdgeKey?)null : path[path.Count - 1];

        void ComputeLeg(LinePlan plan, List<int> visits, int k, EdgeKey? from)
        {
            var a = stopById[visits[k]];
            var b = stopById[visits[(k + 1) % visits.Count]];
            var path = LegPath(a, b, from, out float meters, out float seconds);
            // Startkante ungünstig (z. B. Ziel liegt zurück auf derselben Kante): beste Seite von a versuchen
            if (path == null && from.HasValue) path = LegPath(a, b, null, out meters, out seconds);
            plan.Legs[k] = path;
            plan.LegMeters[k] = meters;
            plan.LegSeconds[k] = seconds;
        }

        /// <summary>Weg von Haltestelle a (ab Kante <paramref name="from"/>, sonst beste Seite von a) zu b.</summary>
        List<EdgeKey> LegPath(BusStop a, BusStop b, EdgeKey? from, out float meters, out float seconds)
        {
            meters = 0f;
            seconds = 0f;
            var segA = roads.GetSegment(a.SegmentId);
            if (segA == null || roads.GetSegment(b.SegmentId) == null) return null;
            var candidates = new List<EdgeKey>();
            if (from.HasValue && from.Value.SegmentId == a.SegmentId) candidates.Add(from.Value);
            else
            {
                if (a.ServesAlong && HasBusLane(segA, true)) candidates.Add(new EdgeKey(segA.Id, true));
                if (a.ServesAgainst && HasBusLane(segA, false)) candidates.Add(new EdgeKey(segA.Id, false));
            }
            List<EdgeKey> best = null;
            float bestCost = float.MaxValue;
            foreach (var e in candidates)
            {
                float s = e.Along ? a.T * segA.Length : (1f - a.T) * segA.Length;
                var path = b.Direction == 0
                    ? traffic.Paths.FindPathToSegment(e, s, b.SegmentId, b.T, VehicleClass.Bus)
                    : traffic.Paths.FindPathToEdge(e, s, new EdgeKey(b.SegmentId, b.Direction == 1), b.T, VehicleClass.Bus);
                if (path == null) continue;
                Measure(path, s, b.T, out float m, out float secs);
                if (secs < bestCost) { bestCost = secs; best = path; meters = m; seconds = secs; }
            }
            return best;
        }

        void Measure(List<EdgeKey> path, float startS, float endT, out float meters, out float seconds)
        {
            meters = 0f;
            seconds = TransitRules.PlannedDwellSeconds;
            for (int i = 0; i < path.Count; i++)
            {
                var seg = roads.GetSegment(path[i].SegmentId);
                if (seg == null) continue;
                float from = i == 0 ? startS : 0f;
                float to = i == path.Count - 1 ? (path[i].Along ? endT : 1f - endT) * seg.Length : seg.Length;
                float len = MathF.Max(0f, to - from);
                meters += len;
                float speed = MathF.Min(traffic.Rules.SpeedLimit(seg), VehicleClasses.MaxSpeed(VehicleClass.Bus)) * TransitRules.ScheduleSpeedFactor;
                seconds += len / MathF.Max(2f, speed);
                if (i > 0)
                {
                    var node = traffic.Lanes.EdgeFrom(path[i]);
                    if (node != null && node.SegmentIds.Count >= 3) seconds += TransitRules.ScheduleJunctionSeconds;
                }
            }
        }

        // ------------------------------------------------------------ Depots

        public DepotPlan PlanDepot(DepotType type, Vector2 position, float rotationDegrees = 0f)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            float a = rotationDegrees * MathF.PI / 180f;
            var plan = new DepotPlan { Type = type, Position = position, Front = new Vector2(MathF.Sin(a), MathF.Cos(a)), Cost = type.Cost };
            plan.Height = MathF.Max(terrain.HeightAt(position), terrain.WaterLevel + 0.5f);
            plan.Error = CheckDepot(plan);
            if (plan.Error == TransitBuildError.None && !Affordable(plan.Cost)) plan.Error = TransitBuildError.InsufficientFunds;
            if (plan.Error == TransitBuildError.None && city != null)
            {
                var sorted = new List<Building>(city.Buildings);
                sorted.Sort((x, y) => x.Id.CompareTo(y.Id));
                foreach (var b in sorted)
                    if (ServiceGeometry.RectsOverlap(position, plan.Front, type.Width, type.Depth, b.Position, b.Front, b.Width, b.Depth))
                        plan.Displaced.Add(b);
            }
            return plan;
        }

        TransitBuildError CheckDepot(DepotPlan plan)
        {
            var type = plan.Type;
            var corners = ServiceGeometry.Corners(plan.Position, plan.Front, type.Width, type.Depth);
            foreach (var c in corners) if (!InWorld(c)) return TransitBuildError.OutOfWorld;
            if (terrain.IsWater(plan.Position)) return TransitBuildError.OnWater;
            float lo = terrain.HeightAt(plan.Position), hi = lo;
            foreach (var c in corners)
            {
                if (terrain.IsWater(c)) return TransitBuildError.OnWater;
                float h = terrain.HeightAt(c);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            if (hi - lo > TransitRules.DepotMaxSlope) return TransitBuildError.TooSteep;
            foreach (var s in roads.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, 0f)) return TransitBuildError.OverlapsRoad;
            if (DepotOverlaps(plan.Position, plan.Front, type.Width, type.Depth, plan.MovingId)) return TransitBuildError.OverlapsFacility;
            if (services != null)
                foreach (var f in services.Facilities)
                    if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, f.Position, f.Front, f.Type.Width, f.Type.Depth))
                        return TransitBuildError.OverlapsFacility;
            if (utilities != null)
            {
                foreach (var f in utilities.Facilities)
                    if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, f.Position, f.Front, f.Type.Width, f.Type.Depth))
                        return TransitBuildError.OverlapsFacility;
                foreach (var l in utilities.Lines)
                    foreach (var p in l.Poles)
                        if (ServiceGeometry.InRect(p, plan.Position, plan.Front, type.Width, type.Depth, l.PoleFootprintHalf))
                            return TransitBuildError.OverlapsPole;
            }
            if (!FindAccess(plan.Position, plan.Front, type.Width, type.Depth, out int seg, out float t)) return TransitBuildError.NoRoadAccess;
            plan.AccessSegmentId = seg;
            plan.AccessT = t;
            return TransitBuildError.None;
        }

        public TransitDepot BuildDepot(DepotPlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.TransitConstruction, plan.Cost);
            if (city != null) foreach (var b in plan.Displaced) city.RemoveBuilding(b);
            var d = new TransitDepot
            {
                Id = nextDepotId++,
                Type = plan.Type,
                Position = plan.Position,
                Front = plan.Front,
                Height = plan.Height,
                AccessSegmentId = plan.AccessSegmentId,
                AccessT = plan.AccessT,
            };
            terrain.FlattenArea(d.Position, d.Front, d.Type.Width, d.Type.Depth, d.Height);
            depots.Add(d);
            Version++;
            return d;
        }

        /// <summary>
        /// Spielerregel „Gebäude mit Zufahrt rasten am Straßenrand ein“: Depot mit der Vorderseite zur nächsten ebenerdigen
        /// Stadtstraße (≤ 60 m) ausrichten und an den Rand schieben (Anschluss = Mitte der Vorderseite, 1 m Abstand).
        /// false = keine Straße in der Nähe (Position/Drehung bleiben wie übergeben).
        /// </summary>
        public bool SnapDepot(DepotType type, Vector2 point, out Vector2 position, out float rotationDegrees)
        {
            position = point;
            rotationDegrees = 0f;
            RoadSegment best = null;
            float bestD = 60f, bestT = 0f;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float t = s.ClosestT(point);
                if (MathF.Abs(s.ElevationAt(t)) > 3f) continue;
                float d = Vector2.Distance(s.Evaluate(t), point) - s.Type.HalfWidth;
                if (d < bestD || (d == bestD && best != null && s.Id < best.Id)) { bestD = d; best = s; bestT = t; }
            }
            if (best == null) return false;
            var q = best.Evaluate(bestT);
            var right = best.Right(bestT);
            var n = Vector2.Dot(point - q, right) >= 0f ? right : -right; // Seite des Klicks
            var front = -n;                                                 // Vorderseite zeigt zur Straße
            position = q + n * (best.Type.HalfWidth + 1f + type.Depth * 0.5f);
            rotationDegrees = MathF.Atan2(front.X, front.Y) * 180f / MathF.PI;
            return true;
        }

        public TransitDepot TryBuildDepot(DepotType type, Vector2 position, float rotationDegrees = 0f) => BuildDepot(PlanDepot(type, position, rotationDegrees));

        // --- UX: Verschieben (gleiche Platzregeln, 10 % Umzugspreis, Depot behält Id und Busse)
        /// <summary>Umzugspreis als Anteil der Baukosten.</summary>
        public const double MoveCostShare = 0.1;

        /// <summary>Umzugsplan für ein Depot (Vorderseite als Winkel wie <see cref="PlanDepot"/>).</summary>
        public DepotPlan PlanMoveDepot(int id, Vector2 position, float rotationDegrees)
        {
            var d = GetDepot(id);
            if (d == null) return null;
            float a = rotationDegrees * MathF.PI / 180f;
            var plan = new DepotPlan { Type = d.Type, Position = position, Front = new Vector2(MathF.Sin(a), MathF.Cos(a)), MovingId = id,
                Cost = d.Type.Cost * MoveCostShare };
            plan.Height = MathF.Max(terrain.HeightAt(position), terrain.WaterLevel + 0.5f);
            plan.Error = CheckDepot(plan);
            if (plan.Error == TransitBuildError.None && !Affordable(plan.Cost)) plan.Error = TransitBuildError.InsufficientFunds;
            if (plan.Error == TransitBuildError.None && city != null)
                foreach (var b in city.Buildings)
                    if (ServiceGeometry.RectsOverlap(position, plan.Front, d.Type.Width, d.Type.Depth, b.Position, b.Front, b.Width, b.Depth))
                        plan.Displaced.Add(b);
            return plan;
        }

        /// <summary>Führt einen gültigen Umzugsplan aus (Busse im Einsatz fahren weiter, neue starten am neuen Ort).</summary>
        public bool MoveDepot(DepotPlan plan)
        {
            if (plan == null || !plan.IsValid || plan.MovingId <= 0 || !Affordable(plan.Cost)) return false;
            var d = GetDepot(plan.MovingId);
            if (d == null) return false;
            Budget?.Spend(Economy.BudgetCategory.TransitConstruction, plan.Cost);
            if (city != null) foreach (var b in plan.Displaced) city.RemoveBuilding(b);
            d.Position = plan.Position;
            d.Front = plan.Front;
            d.Height = plan.Height;
            d.AccessSegmentId = plan.AccessSegmentId;
            d.AccessT = plan.AccessT;
            terrain.FlattenArea(d.Position, d.Front, d.Type.Width, d.Type.Depth, d.Height);
            Version++;
            routesDirty = true;
            return true;
        }
        // --- Ende UX

        /// <summary>Spieler-Abriss: 25 % zurück. Busse des Depots werden abgezogen.</summary>
        public bool DemolishDepot(int id)
        {
            var d = GetDepot(id);
            if (d == null) return false;
            RemoveDepot(id);
            Budget?.Refund(d.Type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        /// <summary>Internes Entfernen (Straße durchs Depot) ohne Erlös.</summary>
        public bool RemoveDepot(int id)
        {
            var d = GetDepot(id);
            if (d == null) return false;
            depots.Remove(d);
            foreach (var b in buses.ToArray())
                if (b.DepotId == id) RetireBus(b, RideOutcome.Stranded);
            Version++;
            return true;
        }

        bool FindAccess(Vector2 center, Vector2 front, float width, float depth, out int segmentId, out float t)
        {
            segmentId = -1;
            t = 0f;
            float best = TransitRules.DepotRoadReach + 1e-3f;
            float reach = MathF.Sqrt(width * width + depth * depth) * 0.5f + TransitRules.DepotRoadReach;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                float r = reach + s.Type.HalfWidth;
                if (center.X < lo.X - r || center.Y < lo.Y - r || center.X > hi.X + r || center.Y > hi.Y + r) continue;
                int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 2f));
                for (int i = 0; i <= samples; i++)
                {
                    float u = i / (float)samples;
                    if (MathF.Abs(s.ElevationAt(u)) > 3f) continue;
                    float d = ServiceGeometry.RectDistance(s.Evaluate(u), center, front, width, depth) - s.Type.HalfWidth;
                    if (d < best - 1e-4f || (MathF.Abs(d - best) <= 1e-4f && segmentId >= 0 && s.Id < segmentId))
                    {
                        best = d;
                        segmentId = s.Id;
                        t = Math.Clamp(u, 0.05f, 0.95f);
                    }
                }
            }
            return segmentId >= 0;
        }

        // ------------------------------------------------------------ Busse einsetzen

        void SpawnBuses()
        {
            double now = traffic.Time;
            foreach (var l in lines)
            {
                if (!l.RouteValid || l.ActiveVehicles >= l.TargetVehicles || now < l.NextSpawnTime) continue;
                // Busse über den Umlauf verteilen: der i-te Bus beginnt am Halt i·n/Ziel (statt alle am ersten Halt)
                int startVisit = l.Visits.Length * l.ActiveVehicles / Math.Max(1, l.TargetVehicles) % l.Visits.Length;
                var first = GetStop(l.Visits[startVisit]);
                if (first == null) continue;
                // Nächstes Depot mit freiem Fahrzeug (Luftlinie zum ersten Halt, bei Gleichstand kleinste Id)
                TransitDepot depot = null;
                float bestD = float.MaxValue;
                foreach (var d in depots)
                {
                    if (!d.HasAccess || d.FreeVehicles <= 0 || d.Type.VehicleClass != VehicleClass.Bus) continue;
                    float dist = Vector2.Distance(d.Position, first.Position);
                    if (dist < bestD) { bestD = dist; depot = d; }
                }
                if (depot == null) { l.NextSpawnTime = now + 5f; continue; }
                var bus = new Bus { Id = nextBusId, LineId = l.Id, DepotId = depot.Id, State = BusState.ToLine, Capacity = BusCapacity, VisitIndex = startVisit, TargetStopId = first.Id };
                var v = traffic.RequestScheduledVehicle(TripEnd.AtSegment(depot.AccessSegmentId, depot.AccessT), first.SegmentId, first.T,
                    first.Direction, VehicleClass.Bus, bus.Id);
                if (v == null) { l.NextSpawnTime = now + 3f; continue; } // kein Platz/kein Weg: später erneut
                nextBusId++;
                bus.Vehicle = v;
                buses.Add(bus);
                busById[bus.Id] = bus;
                l.ActiveVehicles++;
                depot.VehiclesOut++;
                // Nächster Bus kurz danach (Startplatz am Depot frei machen); der Takt entsteht über die verteilten Startpunkte
                l.NextSpawnTime = now + 4f;
            }
        }

        // ------------------------------------------------------------ An der Haltestelle

        void OnStopReached(Vehicle v)
        {
            if (!busById.TryGetValue(v.TransitId, out var bus) || bus.Vehicle != v) { traffic.Dismiss(v); return; }
            if (bus.State == BusState.ToDepot) { RemoveBus(bus); traffic.Dismiss(v); return; }
            var line = GetLine(bus.LineId);
            if (line == null || line.Visits.Length < 2) { RetireBus(bus, RideOutcome.Stranded); return; }

            // Welcher Halt ist das? (Linie kann sich seit der Abfahrt geändert haben)
            int visit = bus.VisitIndex < line.Visits.Length && line.Visits[bus.VisitIndex] == bus.TargetStopId
                ? bus.VisitIndex : Array.IndexOf(line.Visits, bus.TargetStopId);
            var stop = GetStop(bus.TargetStopId);
            if (visit < 0 || stop == null)
            {
                // Halt gehört nicht mehr zur Linie: einfach zum nächsten fahren
                bus.VisitIndex = Math.Min(bus.VisitIndex, line.Visits.Length - 1);
                DriveOn(bus, line, bus.VisitIndex, 1f);
                return;
            }
            double now = traffic.Time;

            // Pünktlichkeit des Abschnitts
            if (bus.MeasureLeg && bus.State == BusState.InService)
            {
                int leg = (visit - 1 + line.Visits.Length) % line.Visits.Length;
                float planned = line.LegSeconds[leg];
                float actual = (float)(now - bus.LegStartTime);
                bool onTime = actual <= planned * TransitRules.LateFactor + TransitRules.LateSlackSeconds;
                bus.LastLegOnTime = onTime;
                bus.LastDelaySeconds = MathF.Max(0f, actual - planned);
                line.ArrivalsToday++;
                if (onTime) line.OnTimeToday++;
                if (!onTime) foreach (var p in bus.Riders) p.WasLate = true;
            }
            bus.State = BusState.InService;
            bus.StopsServed++;

            // Aussteigen (Ziel bzw. Umstieg)
            int alighted = 0;
            for (int i = bus.Riders.Count - 1; i >= 0; i--)
            {
                var p = bus.Riders[i];
                if (p.AlightStopId != stop.Id) continue;
                bus.Riders.RemoveAt(i);
                alighted++;
                p.Bus = null;
                if (p.Leg == 0 && p.HasTransfer)
                {
                    p.Leg = 1;
                    p.WaitingAtStop = stop.Id;
                    p.WaitSince = now;
                    p.FullBusesSeen = 0;
                    stop.Waiting.Add(p);
                }
                else Finish(p, RideOutcome.Arrived, stop.Position);
            }

            // Zu viele Busse auf der Linie → leerer Bus fährt ins Depot
            bool excess = line.ActiveVehicles > line.TargetVehicles;
            int boarded = 0;
            if (!excess)
            {
                for (int i = 0; i < stop.Waiting.Count; i++)
                {
                    var p = stop.Waiting[i];
                    if (p.CurrentLineId != line.Id || p.ReadyTime > now) continue; // noch auf dem Fußweg zur Haltestelle
                    // Richtung prüfen (Pendel): fährt dieser Bus in vertretbarer Zeit zum Ziel-Halt?
                    float ride = line.RideSecondsFromVisit(visit, p.AlightStopId);
                    float plannedRide = line.RideSeconds(stop.Id, p.AlightStopId);
                    if (float.IsInfinity(ride) || ride > plannedRide * 1.5f + 30f) continue;
                    if (bus.Riders.Count >= bus.Capacity)
                    {
                        p.FullBusesSeen++;
                        line.LeftBehindToday++;
                        TotalLeftBehind++;
                        continue;
                    }
                    stop.Waiting.RemoveAt(i--);
                    p.WaitingAtStop = 0;
                    p.Bus = bus;
                    p.WaitedSeconds += (float)(now - p.WaitSince);
                    p.BoardTime = now;
                    p.PlannedRide = ride;
                    bus.Riders.Add(p);
                    boarded++;
                    line.PassengersToday++;
                    line.PassengersTotal++;
                    stop.BoardedToday++;
                    TotalBoardings++;
                    pendingFares += Fare;
                }
                // Wer zu oft volle Busse gesehen hat, gibt auf
                for (int i = stop.Waiting.Count - 1; i >= 0; i--)
                {
                    var p = stop.Waiting[i];
                    if (p.CurrentLineId == line.Id && p.FullBusesSeen >= TransitRules.MaxFullBusesBeforeGivingUp)
                        Finish(p, RideOutcome.GaveUpFull, stop.Position);
                }
            }

            float dwell = TransitRules.DwellBaseSeconds + boarded * TransitRules.SecondsPerBoarding + alighted * TransitRules.SecondsPerAlighting;
            traffic.HoldAtStopFor(v, dwell);
            if (excess && bus.Riders.Count == 0) { SendToDepot(bus); return; }
            DriveOn(bus, line, visit, dwell);
        }

        /// <summary>Zum nächsten erreichbaren Halt nach <paramref name="visit"/> fahren (unerreichbare werden übersprungen).</summary>
        void DriveOn(Bus bus, TransitLine line, int visit, float dwell)
        {
            int n = line.Visits.Length;
            for (int i = 1; i <= n; i++)
            {
                int k = (visit + i) % n;
                var next = GetStop(line.Visits[k]);
                if (next == null) continue;
                if (traffic.Redirect(bus.Vehicle, next.SegmentId, next.T, next.Direction))
                {
                    bus.VisitIndex = k;
                    bus.TargetStopId = next.Id;
                    bus.LegStartTime = traffic.Time + dwell;
                    bus.MeasureLeg = i == 1;
                    return;
                }
            }
            RetireBus(bus, RideOutcome.Stranded);
        }

        void SendToDepot(Bus bus)
        {
            bus.State = BusState.ToDepot;
            var depot = GetDepot(bus.DepotId);
            if (depot == null || !depot.HasAccess || !traffic.Redirect(bus.Vehicle, depot.AccessSegmentId, depot.AccessT, 0))
            {
                RemoveBus(bus);
                traffic.Dismiss(bus.Vehicle);
            }
        }

        /// <summary>Bus sofort aus dem Dienst nehmen; Fahrgäste steigen aus und gehen zu Fuß weiter.</summary>
        void RetireBus(Bus bus, RideOutcome outcome)
        {
            var pos = bus.Vehicle?.Position ?? Vector2.Zero;
            foreach (var p in bus.Riders.ToArray()) Finish(p, outcome, pos);
            bus.Riders.Clear();
            RemoveBus(bus);
            if (bus.Vehicle != null) traffic.Dismiss(bus.Vehicle);
        }

        void RemoveBus(Bus bus)
        {
            if (!busById.Remove(bus.Id)) return;
            buses.Remove(bus);
            var l = GetLine(bus.LineId);
            if (l != null && bus.State != BusState.ToDepot && l.ActiveVehicles > 0) l.ActiveVehicles--;
            var d = GetDepot(bus.DepotId);
            if (d != null && d.VehiclesOut > 0) d.VehiclesOut--;
        }

        void OnVehicleRemoved(Vehicle v, bool despawned)
        {
            if (!busById.TryGetValue(v.TransitId, out var bus) || bus.Vehicle != v) return;
            if (despawned) BusesLost++;
            var pos = v.Position;
            foreach (var p in bus.Riders.ToArray()) Finish(p, RideOutcome.Stranded, pos);
            bus.Riders.Clear();
            RemoveBus(bus);
        }
    }
}
