using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Transit
{
    /// <summary>
    /// ÖPNV Stufe 1 – Buslinien. Haltestellen am Fahrbahnrand von Stadtstraßen (je Straßenseite oder als Paar),
    /// Linien (Rund-/Pendellinie, Route per <see cref="Pathfinder"/> zwischen den Haltestellen), Busdepots (Bauwerk wie
    /// die Dienste G4, Fahrzeugpool), Fahrzeuge je Linie (→ Takt), Fahrgeld und Unterhalt über die Stadtkasse.
    /// <para>
    /// Busse fahren als echte Fahrzeuge im <see cref="TrafficSystem"/> (stehen im Stau wie alle), halten an jeder
    /// Haltestelle (Haltezeit je ein-/aussteigendem Fahrgast) und befördern echte Bürger mit begrenzter Kapazität.
    /// Fahrgäste kommen vom <see cref="Citizens.CitizenSystem"/> (<see cref="PlanRide"/> → <see cref="StartRide"/>)
    /// und werden über <see cref="RideEnded"/> zurückgemeldet.
    /// </para>
    /// Zeiten in Sekunden Verkehrszeit (<see cref="TrafficSystem.Time"/>). Deterministisch: Listen nach Id, kein Zufall.
    /// </summary>
    public sealed partial class TransitSystem : ISimulationSystem, Economy.IUpkeepSource
    {
        public int TickInterval => 1;

        readonly RoadNetwork roads;
        readonly TerrainMap terrain;
        readonly CitySystem city;
        readonly TrafficSystem traffic;
        readonly SimulationClock clock;
        readonly UtilitySystem utilities;
        readonly ServiceSystem services;
        readonly Vector2 min, max;

        readonly List<BusStop> stops = new List<BusStop>();
        readonly Dictionary<int, BusStop> stopById = new Dictionary<int, BusStop>();
        readonly List<TransitLine> lines = new List<TransitLine>();
        readonly List<TransitDepot> depots = new List<TransitDepot>();
        readonly List<Bus> buses = new List<Bus>();
        readonly Dictionary<int, Bus> busById = new Dictionary<int, Bus>();
        readonly Dictionary<int, List<TransitLine>> linesAtStop = new Dictionary<int, List<TransitLine>>();
        static readonly List<TransitLine> NoLines = new List<TransitLine>();

        int nextStopId = 1, nextLineId = 1, nextDepotId = 1, nextBusId = 1, nextPassengerId = 1;
        int routeRoadVersion = -1;
        bool routesDirty = true, orphansPending;
        double pendingFares;

        /// <summary>Plätze je Bus (Standard <see cref="TransitRules.BusCapacity"/>; Tests/Balancing).</summary>
        public int BusCapacity = TransitRules.BusCapacity;
        /// <summary>Fahrgeld je Einstieg.</summary>
        public double Fare = TransitRules.Fare;

        /// <summary>Wird bei jeder Änderung an Haltestellen, Linien oder Depots erhöht (Darstellung).</summary>
        public int Version { get; private set; }

        public IReadOnlyList<BusStop> Stops => stops;
        public IReadOnlyList<TransitLine> Lines => lines;
        public IReadOnlyList<TransitDepot> Depots => depots;
        public IReadOnlyList<Bus> Buses => buses;

        /// <summary>Fahrt eines Fahrgasts beendet (Ziel-Halt erreicht, aufgegeben, weggefallen). Läuft ggf. im Verkehrs-Tick.</summary>
        public event Action<Passenger, RideOutcome> RideEnded;

        // Statistik (kumuliert)
        public int TotalBoardings { get; private set; }
        public int TotalLeftBehind { get; private set; }
        public int TotalGaveUp { get; private set; }
        public int BusesLost { get; private set; }
        public double FaresTotal { get; private set; }

        public TransitSystem(RoadNetwork roads, TerrainMap terrain, CitySystem city, TrafficSystem traffic, SimulationClock clock,
            UtilitySystem utilities, ServiceSystem services, Vector2 min, Vector2 max)
        {
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.city = city;
            this.traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.utilities = utilities;
            this.services = services;
            this.min = min;
            this.max = max;

            traffic.ScheduledStopReached += OnStopReached;
            traffic.ScheduledVehicleRemoved += OnVehicleRemoved;
            roads.SegmentRemoved += OnSegmentRemoved;
            roads.SegmentReplaced += OnSegmentReplaced;
            roads.SegmentAdded += OnSegmentAdded;

            // Auf Depotflächen wächst nichts; Terraforming gesperrt; Versorger/Dienste nicht auf Depots (Hooks verketten)
            if (city != null)
            {
                var previousBlocker = city.LotBlocker;
                city.LotBlocker = p => (previousBlocker != null && previousBlocker(p)) || DepotAt(p, ZoneGrid.CellSize * 0.5f) != null;
            }
            var previousLocks = terrain.LockQuery;
            terrain.LockQuery = (lo, hi) =>
            {
                var inner = previousLocks?.Invoke(lo, hi);
                var near = new List<TransitDepot>();
                foreach (var d in depots)
                    if (d.Contains(Vector2.Clamp(d.Position, lo, hi), TerrainLocks.Band + terrain.CellSize + MathF.Max(d.Type.Width, d.Type.Depth)))
                        near.Add(d);
                if (near.Count == 0) return inner;
                return p =>
                {
                    foreach (var d in near) if (d.Contains(p, terrain.CellSize)) return 1f;
                    return inner?.Invoke(p) ?? 0f;
                };
            };
            if (utilities != null)
            {
                var prevOverlaps = utilities.OtherStructureOverlaps;
                utilities.OtherStructureOverlaps = (c, f, w, d) => (prevOverlaps != null && prevOverlaps(c, f, w, d)) || DepotOverlaps(c, f, w, d);
                var prevAt = utilities.OtherStructureAt;
                utilities.OtherStructureAt = (p, margin) => (prevAt != null && prevAt(p, margin)) || DepotAt(p, margin) != null;
            }
            if (services != null)
            {
                var prev = services.OtherStructureOverlaps;
                services.OtherStructureOverlaps = (c, f, w, d) => (prev != null && prev(c, f, w, d)) || DepotOverlaps(c, f, w, d);
            }
        }

        // ------------------------------------------------------------ Abfragen

        public BusStop GetStop(int id) => stopById.TryGetValue(id, out var s) ? s : null;
        public TransitLine GetLine(int id) { foreach (var l in lines) if (l.Id == id) return l; return null; }
        public TransitDepot GetDepot(int id) { foreach (var d in depots) if (d.Id == id) return d; return null; }
        public Bus GetBus(int id) => busById.TryGetValue(id, out var b) ? b : null;
        /// <summary>Bus zu einem Fahrzeug im Verkehr (null = kein Linienbus, z. B. Durchgangsverkehr).</summary>
        public Bus BusOf(Vehicle v) => v != null && v.IsScheduled ? GetBus(v.TransitId) : null;
        /// <summary>Linien, die an der Haltestelle halten.</summary>
        public IReadOnlyList<TransitLine> LinesAt(int stopId) { EnsureRoutes(); return linesAtStop.TryGetValue(stopId, out var l) ? l : NoLines; }
        public IEnumerable<Bus> BusesOf(int lineId) { foreach (var b in buses) if (b.LineId == lineId) yield return b; }
        /// <summary>Wartende an einer Haltestelle (alle Linien bzw. je Linie: <see cref="BusStop.WaitingFor"/>).</summary>
        public int WaitingAt(int stopId) => GetStop(stopId)?.WaitingCount ?? 0;
        /// <summary>Fahrgäste, die gerade unterwegs sind (wartend + im Bus).</summary>
        public int ActivePassengers { get { int n = 0; foreach (var s in stops) n += s.Waiting.Count; foreach (var b in buses) n += b.Riders.Count; return n; } }
        /// <summary>Gibt es mindestens eine Linie mit Bussen im Einsatz?</summary>
        public bool HasService { get { foreach (var l in lines) if (l.ActiveVehicles > 0 && l.RouteValid) return true; return false; } }
        public int VehicleCapacityTotal { get { int n = 0; foreach (var d in depots) n += d.Type.VehicleCapacity; return n; } }

        public TransitDepot DepotAt(Vector2 p, float margin = 0f)
        {
            foreach (var d in depots) if (d.Contains(p, margin)) return d;
            return null;
        }

        bool DepotOverlaps(Vector2 c, Vector2 f, float w, float d)
        {
            foreach (var dp in depots)
                if (ServiceGeometry.RectsOverlap(c, f, w, d, dp.Position, dp.Front, dp.Type.Width, dp.Type.Depth)) return true;
            return false;
        }

        /// <summary>Halteposition am Fahrbahnrand (rechte Seite der Fahrtrichtung <paramref name="along"/>).</summary>
        public Vector2 PlatformPosition(BusStop s, bool along)
        {
            var seg = roads.GetSegment(s.SegmentId);
            if (seg == null) return s.Position;
            float off = seg.Type.CarriagewayHalfWidth > 0f ? seg.Type.CarriagewayHalfWidth : seg.Type.HalfWidth * 0.6f;
            return seg.Evaluate(s.T) + seg.Right(s.T) * (along ? off : -off);
        }

        /// <summary>Nächste Haltestelle im Umkreis (für Werkzeug/Inspektor).</summary>
        public BusStop StopNear(Vector2 p, float radius)
        {
            BusStop best = null;
            float bestD = radius;
            foreach (var s in stops)
            {
                float d = Vector2.Distance(s.Position, p);
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------ Kosten (Stadtkasse)

        /// <summary>Stadtkasse für Baukosten (null = kostenlos). Gesetzt von <see cref="SimulationWorld.CreateDefault"/>.</summary>
        public Economy.IConstructionBudget Budget { get; set; }
        /// <summary>Bucht Fahrgeld (gesetzt von <see cref="SimulationWorld.CreateDefault"/> → <c>Economy.Earn(TransitFares)</c>).</summary>
        public Action<double> FareSink { get; set; }
        bool Affordable(double cost) => Budget == null || Budget.CanAfford(cost);

        public string Name => "ÖPNV";

        /// <summary>Unterhalt je Woche: Depots + Busse im Einsatz + Haltestellen.</summary>
        public double WeeklyUpkeep()
        {
            double s = 0;
            foreach (var d in depots) s += d.Type.UpkeepPerWeek;
            s += buses.Count * TransitRules.BusUpkeepPerWeek;
            s += stops.Count * TransitRules.StopUpkeepPerWeek;
            return s;
        }

        // ------------------------------------------------------------ Haltestellen

        /// <summary>
        /// Haltestelle planen: rastet an der nächsten Stadtstraße ein (Rand ≤ <see cref="TransitRules.SnapDistance"/>).
        /// Seite = Straßenseite des Klicks (Rechtsverkehr: rechts in Segmentrichtung = <see cref="StopSide.Along"/>),
        /// mit <paramref name="bothSides"/> ein Haltestellenpaar. Einbahnstraßen: nur die befahrene Seite.
        /// </summary>
        public StopPlan PlanStop(Vector2 point, bool bothSides = false)
        {
            var plan = new StopPlan { Cost = TransitRules.StopCost, Position = point };
            var seg = roads.FindNearestSegment(point, TransitRules.SnapDistance);
            if (seg == null) { plan.Error = TransitBuildError.NoRoad; return plan; }
            plan.SegmentId = seg.Id;
            float t = seg.ClosestT(point);
            plan.T = t;
            plan.Position = seg.Evaluate(t);
            if (seg.Type.Category != RoadCategory.City) { plan.Error = TransitBuildError.NotCityRoad; return plan; }
            if (MathF.Abs(seg.ElevationAt(t)) > 3f) { plan.Error = TransitBuildError.Elevated; return plan; }

            bool fwd = HasBusLane(seg, true), bwd = HasBusLane(seg, false);
            if (!fwd && !bwd) { plan.Error = TransitBuildError.NoLaneOnSide; return plan; }
            StopSide side;
            if (fwd && bwd) side = bothSides ? StopSide.Both : Vector2.Dot(point - plan.Position, seg.Right(t)) >= 0f ? StopSide.Along : StopSide.Against;
            else side = fwd ? StopSide.Along : StopSide.Against;
            plan.Side = side;

            float sAlong = t * seg.Length;
            if ((seg.Start.SegmentIds.Count >= 3 && sAlong < TransitRules.MinDistanceToJunction) ||
                (seg.End.SegmentIds.Count >= 3 && seg.Length - sAlong < TransitRules.MinDistanceToJunction) ||
                sAlong < 3f || seg.Length - sAlong < 3f)
            { plan.Error = TransitBuildError.TooCloseToJunction; return plan; }

            foreach (var s in stops)
            {
                if (Vector2.Distance(s.Position, plan.Position) >= TransitRules.MinStopSpacing) continue;
                if (s.Side == StopSide.Both || side == StopSide.Both || s.Side == side || s.SegmentId != seg.Id)
                { plan.Error = TransitBuildError.TooCloseToStop; return plan; }
            }
            if (!Affordable(plan.Cost)) plan.Error = TransitBuildError.InsufficientFunds;
            return plan;
        }

        bool HasBusLane(RoadSegment seg, bool along)
        {
            foreach (var l in traffic.Lanes.GetLanes(seg.Id))
                if (l.Along == along && (l.Allowed & VehicleClass.Bus) != 0) return true;
            // Spurgraph noch nicht aktualisiert (gerade gebaut): aus dem Querschnitt
            if (traffic.Lanes.GetLanes(seg.Id).Length == 0)
                return along ? seg.Type.CarLanesForward > 0 : seg.Type.CarLanesBackward > 0;
            return false;
        }

        public BusStop BuildStop(StopPlan plan)
        {
            if (plan == null || !plan.IsValid || roads.GetSegment(plan.SegmentId) == null) return null;
            if (!Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.TransitConstruction, plan.Cost);
            var s = new BusStop { Id = nextStopId++, SegmentId = plan.SegmentId, T = plan.T, Side = plan.Side, Position = plan.Position };
            string street = PlaceNames.Street(plan.SegmentId);
            int same = 0;
            foreach (var o in stops) if (o.Name == street || o.Name.StartsWith(street + " ")) same++;
            s.Name = same == 0 ? street : street + " " + (same + 1);
            stops.Add(s);
            stopById[s.Id] = s;
            Version++;
            return s;
        }

        /// <summary>Kurzform für Werkzeuge/Tests.</summary>
        public BusStop TryBuildStop(Vector2 point, bool bothSides = false) => BuildStop(PlanStop(point, bothSides));

        /// <summary>Spieler-Abriss einer Haltestelle (25 % zurück); Linien verlieren den Halt.</summary>
        public bool DemolishStop(int id)
        {
            if (!RemoveStop(id)) return false;
            Budget?.Refund(TransitRules.StopCost * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        bool RemoveStop(int id)
        {
            if (!stopById.TryGetValue(id, out var s)) return false;
            foreach (var p in s.Waiting.ToArray()) Finish(p, RideOutcome.Stranded, s.Position);
            stops.Remove(s);
            stopById.Remove(id);
            // Fahrgäste, die dort aussteigen wollten, steigen sofort aus
            foreach (var b in buses)
                foreach (var p in b.Riders.ToArray())
                    if (p.AlightStopId == id || (p.HasTransfer && p.Plan.Alight2 == id))
                    {
                        b.Riders.Remove(p);
                        Finish(p, RideOutcome.Stranded, b.Vehicle?.Position ?? s.Position);
                    }
            foreach (var l in lines)
                if (l.StopList.RemoveAll(x => x == id) > 0) routesDirty = true;
            // Wartende anderer Haltestellen, deren Ziel weg ist
            foreach (var o in stops)
                foreach (var p in o.Waiting.ToArray())
                    if (p.AlightStopId == id || (p.HasTransfer && p.Plan.Alight2 == id)) Finish(p, RideOutcome.Stranded, o.Position);
            routesDirty = true;
            Version++;
            return true;
        }

        // ------------------------------------------------------------ Linien

        /// <summary>
        /// Neue Linie aus Haltestellen (in Reihenfolge). Rundlinie: letzter Halt fährt zum ersten zurück;
        /// Pendel: hin und zurück. null = weniger als 2 Haltestellen, unbekannte Haltestelle oder Abschnitt unbefahrbar.
        /// </summary>
        public TransitLine CreateLine(IReadOnlyList<int> stopIds, bool loop, int vehicles = 2, string name = null)
        {
            var plan = PreviewLine(stopIds, loop);
            if (!plan.IsValid) return null;
            var l = new TransitLine
            {
                Id = nextLineId++,
                IsLoop = loop,
                TargetVehicles = Math.Max(0, vehicles),
            };
            l.Name = name ?? "Linie " + l.Id;
            l.ColorIndex = (l.Id - 1) % 10;
            l.StopList.AddRange(plan.Stops);
            ApplyPlan(l, plan);
            lines.Add(l);
            RebuildLinesAtStops();
            Version++;
            return l;
        }

        /// <summary>Linie löschen: Busse fahren ins Depot, Wartende gehen zu Fuß.</summary>
        public bool RemoveLine(int lineId)
        {
            var l = GetLine(lineId);
            if (l == null) return false;
            lines.Remove(l);
            foreach (var s in stops)
                foreach (var p in s.Waiting.ToArray())
                    if (p.CurrentLineId == lineId || (p.Leg == 0 && p.Plan.Line2 == lineId)) Finish(p, RideOutcome.Stranded, s.Position);
            foreach (var b in buses.ToArray())
            {
                if (b.LineId != lineId) continue;
                foreach (var p in b.Riders.ToArray()) { b.Riders.Remove(p); Finish(p, RideOutcome.Stranded, b.Vehicle?.Position ?? Vector2.Zero); }
                SendToDepot(b);
            }
            RebuildLinesAtStops();
            Version++;
            return true;
        }

        /// <summary>Zahl der Busse (Takt) einer Linie setzen (0..Depotkapazität).</summary>
        public void SetLineVehicles(int lineId, int vehicles)
        {
            var l = GetLine(lineId);
            if (l == null) return;
            int v = Math.Clamp(vehicles, 0, 99);
            if (v == l.TargetVehicles) return;
            l.TargetVehicles = v;
            Version++;
        }

        public void SetLineLoop(int lineId, bool loop)
        {
            var l = GetLine(lineId);
            if (l == null || l.IsLoop == loop) return;
            l.IsLoop = loop;
            routesDirty = true;
            Version++;
        }

        public void RenameLine(int lineId, string name)
        {
            var l = GetLine(lineId);
            if (l == null || string.IsNullOrWhiteSpace(name)) return;
            l.Name = name.Trim();
            Version++;
        }

        /// <summary>Haltestelle an einer Stelle der Linie einfügen (index &lt; 0 = hinten).</summary>
        public bool AddStopToLine(int lineId, int stopId, int index = -1)
        {
            var l = GetLine(lineId);
            if (l == null || GetStop(stopId) == null) return false;
            if (index < 0 || index > l.StopList.Count) l.StopList.Add(stopId); else l.StopList.Insert(index, stopId);
            routesDirty = true;
            Version++;
            return true;
        }

        public bool RemoveStopFromLine(int lineId, int index)
        {
            var l = GetLine(lineId);
            if (l == null || index < 0 || index >= l.StopList.Count) return false;
            l.StopList.RemoveAt(index);
            routesDirty = true;
            Version++;
            return true;
        }

        void RebuildLinesAtStops()
        {
            linesAtStop.Clear();
            foreach (var l in lines)
                foreach (int s in l.StopList)
                {
                    if (!linesAtStop.TryGetValue(s, out var list)) linesAtStop[s] = list = new List<TransitLine>();
                    if (!list.Contains(l)) list.Add(l);
                }
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            long now = clock.Tick;
            if (orphansPending) ResolveOrphans();
            EnsureRoutes();
            CountVehicles();
            SpawnBuses();
            if (now % 10 == 0) CheckWaiting();
            if (now % 20 == 0) SampleLoad();
            if (now % 5 == 0 && pendingFares > 0)
            {
                FareSink?.Invoke(pendingFares);
                FaresTotal += pendingFares;
                pendingFares = 0;
            }
            if (now % SimulationClock.TicksPerDay == 0) Daily();
        }

        /// <summary>Routen neu berechnen, wenn sich Straßen oder Linien geändert haben.</summary>
        public void EnsureRoutes()
        {
            if (!routesDirty && routeRoadVersion == roads.Version) return;
            routesDirty = false;
            routeRoadVersion = roads.Version;
            traffic.Lanes.Update();
            foreach (var l in lines)
            {
                l.StopList.RemoveAll(id => !stopById.ContainsKey(id));
                ApplyPlan(l, PreviewLine(l.StopList, l.IsLoop));
            }
            foreach (var d in depots)
            {
                if (FindAccess(d.Position, d.Front, d.Type.Width, d.Type.Depth, out int seg, out float t)) { d.AccessSegmentId = seg; d.AccessT = t; }
                else d.AccessSegmentId = -1;
            }
            RebuildLinesAtStops();
        }

        static void ApplyPlan(TransitLine l, LinePlan plan)
        {
            l.Visits = plan.Visits;
            l.LegRoutes = plan.Legs;
            l.LegSeconds = plan.LegSeconds;
            l.LegMeters = plan.LegMeters;
            l.CycleSeconds = plan.CycleSeconds;
            l.LengthMeters = plan.LengthMeters;
            l.BrokenLeg = plan.BrokenLeg;
            l.RouteValid = plan.IsValid;
        }

        void CountVehicles()
        {
            foreach (var l in lines) l.ActiveVehicles = 0;
            foreach (var d in depots) d.VehiclesOut = 0;
            foreach (var b in buses)
            {
                if (b.State != BusState.ToDepot) { var l = GetLine(b.LineId); if (l != null) l.ActiveVehicles++; }
                var d = GetDepot(b.DepotId);
                if (d != null) d.VehiclesOut++;
            }
        }

        void Daily()
        {
            foreach (var l in lines)
            {
                l.PassengersYesterday = l.PassengersToday;
                l.PassengersToday = 0;
                l.LeftBehindToday = 0;
                if (l.ArrivalsToday > 0) l.Punctuality += (l.OnTimeToday / (float)l.ArrivalsToday - l.Punctuality) * 0.5f;
                l.ArrivalsToday = l.OnTimeToday = 0;
                if (l.LoadSamples > 0) l.AverageLoad += (l.LoadSum / l.LoadSamples - l.AverageLoad) * 0.5f;
                l.LoadSum = 0f;
                l.LoadSamples = 0;
            }
            foreach (var s in stops) { s.BoardedYesterday = s.BoardedToday; s.BoardedToday = 0; }
        }

        void SampleLoad()
        {
            foreach (var b in buses)
            {
                if (b.State != BusState.InService) continue;
                var l = GetLine(b.LineId);
                if (l == null) continue;
                l.LoadSum += b.Load;
                l.LoadSamples++;
            }
        }

        // ------------------------------------------------------------ Straßen

        void OnSegmentRemoved(RoadSegment s)
        {
            foreach (var st in stops)
                if (st.SegmentId == s.Id) { st.Orphaned = true; orphansPending = true; }
            routesDirty = true;
        }

        void OnSegmentReplaced(RoadSegment old, IReadOnlyList<RoadSegment> parts)
        {
            foreach (var st in stops)
            {
                if (st.SegmentId != old.Id) continue;
                var p = old.Evaluate(st.T);
                var dirOld = old.Tangent(st.T);
                RoadSegment best = null;
                float bestD = float.MaxValue;
                foreach (var part in parts)
                {
                    float d = part.DistanceTo(p);
                    if (d < bestD) { bestD = d; best = part; }
                }
                if (best == null) continue;
                st.SegmentId = best.Id;
                st.T = best.ClosestT(p);
                st.Position = best.Evaluate(st.T);
                st.Orphaned = false;
                // Richtung umgedreht (Reverse): Seite tauschen
                if (st.Side != StopSide.Both && Vector2.Dot(dirOld, best.Tangent(st.T)) < 0f)
                    st.Side = st.Side == StopSide.Along ? StopSide.Against : StopSide.Along;
            }
            routesDirty = true;
        }

        void ResolveOrphans()
        {
            orphansPending = false;
            var gone = new List<int>();
            foreach (var st in stops) if (st.Orphaned && roads.GetSegment(st.SegmentId) == null) gone.Add(st.Id);
            foreach (int id in gone) RemoveStop(id);
        }

        /// <summary>
        /// Neue Straße: Depots werden NICHT abgerissen (Spielerregel: der Straßenplan muss vorher ungültig sein, s.
        /// <see cref="BlocksRoad"/>). Hier nur Zufahrten neu bestimmen.
        /// </summary>
        void OnSegmentAdded(RoadSegment s) => routesDirty = true;

        // --- Abfragen für den Straßenblocker (Bauregel „Straße überschneidet Depot“)
        /// <summary>Schneidet die ebenerdige Fahrbahn des Segments eine Depot-Grundfläche? Liefert das Depot (sonst null).</summary>
        public TransitDepot BlocksRoad(RoadSegment s, float margin = -0.5f)
        {
            if (s == null) return null;
            foreach (var d in depots)
                if (ServiceGeometry.RoadCrossesRect(s, d.Position, d.Front, d.Type.Width, d.Type.Depth, margin)) return d;
            return null;
        }

        /// <summary>Liegt der Punkt (± Rand) auf einer Depot-Grundfläche? (Haltestellen zählen nicht: sie liegen auf der Straße.)</summary>
        public bool BlocksPoint(Vector2 p, float margin = 0f) => DepotAt(p, margin) != null;
        // --- Ende Abfragen

        bool InWorld(Vector2 p) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;
    }
}
