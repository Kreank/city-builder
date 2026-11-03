using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    public sealed class TrafficStats
    {
        public int Active;
        public int Spawned;
        public int Arrived;
        /// <summary>Wegen Stillstand/Unerreichbarkeit entfernt (wie in Cities: Skylines).</summary>
        public int Despawned;
        public float AverageSpeedKmh;
        public int Waiting;
        /// <summary>Davon Fahrten von Bürgern (aus Gebäuden bzw. Pendler).</summary>
        public int CitizenVehicles;
        /// <summary>G5: Lieferwagen des Warensystems (negative Fahrt-Ids) – zählen nicht als Bürgerfahrten.</summary>
        public int DeliveryVehicles;
        /// <summary>Dienstfahrzeuge (Müllwagen, Streife, Feuerwehr, Rettung; Fahrt-Ids in <see cref="TripIdRanges"/>).</summary>
        public int ServiceVehicles;
        public int CitizenTripsStarted;
        public int CitizenTripsArrived;
        /// <summary>Bürgerfahrten, die abgebrochen wurden (Stau, Straße weg) – der Bürger gilt trotzdem als angekommen.</summary>
        public int CitizenTripsFailed;
    }

    /// <summary>
    /// Verkehrssimulation: Bürgerfahrten zwischen Gebäudezufahrten/Außenanschlüssen (<see cref="RequestTrip"/>)
    /// und Durchgangsverkehr zwischen Außenanschlüssen am Kartenrand; bewegt Fahrzeuge spurgenau
    /// (Fahrzeugfolgemodell IDM), wählt Spuren/Spurwechsel, beachtet Vorfahrt, Schilder und Ampeln.
    /// Zeitmodell (<see cref="TimeModel"/>): ein Verkehrsschritt (<see cref="Substep"/>) = <see cref="Dt"/> Sekunden Fahrzeit,
    /// <see cref="TimeModel.TrafficStepsPerTick"/> Schritte je Kalender-Tick (30 s Verkehrszeit = 6 Spielminuten).
    /// </summary>
    public sealed partial class TrafficSystem : ISimulationSystem, ISubstepSystem
    {
        public const float Dt = TimeModel.TrafficStepSeconds;
        const float MinGap = 2f;
        const float TimeHeadway = 1.2f;
        const float ComfortDecel = 2.5f;

        public int TickInterval => 1;

        readonly RoadNetwork network;
        readonly Vector2 worldMin, worldMax;
        readonly CompatRandom rng; // wie System.Random (gleiche Folge), aber speicherbar

        public LaneGraph Lanes { get; }
        public TrafficRules Rules { get; }
        public JunctionController Junctions { get; }
        public Pathfinder Paths { get; }
        public TrafficStats Stats { get; } = new TrafficStats();

        public readonly List<Vehicle> Vehicles = new List<Vehicle>();
        readonly HashSet<Vehicle> removed = new HashSet<Vehicle>();

        /// <summary>
        /// Verkehrsaufkommen (0 = aus, 1 = normal, 3 = sehr viel): skaliert den Durchgangsverkehr und
        /// (über das Bürgersystem) den Anteil der Bürgerfahrten, die als sichtbares Auto fahren.
        /// </summary>
        public float Demand = 1f;
        /// <summary>Durchgangsverkehr je Außenanschluss und Tick (bei Demand 1).</summary>
        public float ThroughTrafficRate = 0.02f;
        // --- Gateways (Hook, klein): Gewichte des Durchgangsverkehrs. (Start, null) = Gewicht als Startpunkt (Menge
        // skaliert mit der Summe), (Start, Ziel) = Gewicht des Ziels (0 = nicht, z. B. Wende am selben Gateway).
        // null = wie bisher (gleich verteilt, alle Ziele außer dem Start).
        public Func<RoadNode, RoadNode, float> ThroughTrafficWeight;
        // --- Ende Gateways

        /// <summary>Bürgerfahrt beendet: (Fahrzeug, angekommen?). Wird während des Ticks ausgelöst – Empfänger dürfen keine Fahrten starten.</summary>
        public event Action<Vehicle, bool> TripEnded;

        bool deadEndsAreExternal;
        /// <summary>
        /// Sackgassen zählen als Außenanschluss (Testszenarien/Sandkasten ohne Kartenrand).
        /// Normal sind nur Straßenenden am Kartenrand Außenanschlüsse.
        /// </summary>
        public bool DeadEndsAreExternal
        {
            get => deadEndsAreExternal;
            set { deadEndsAreExternal = value; endpointsVersion = -1; }
        }
        public int MaxVehicles = 1500;
        /// <summary>Simulierte Verkehrszeit in Sekunden (= Verkehrsschritte × <see cref="Dt"/>, double: bleibt über Jahre exakt).</summary>
        public double Time => tick * (double)Dt;

        /// <summary>Verkehrsschritte seit Spielbeginn.</summary>
        long tick;
        int nextId = 1;

        sealed class Endpoint
        {
            public RoadNode Node;
            public List<Lane> Out = new List<Lane>();
            public List<Lane> In = new List<Lane>();
            public bool Highway;
        }
        readonly List<Endpoint> endpoints = new List<Endpoint>();
        int endpointsVersion = -1;

        public TrafficSystem(RoadNetwork network, Vector2 worldMin, Vector2 worldMax, int seed = 12345)
        {
            this.network = network;
            this.worldMin = worldMin;
            this.worldMax = worldMax;
            rng = new CompatRandom(seed);
            Lanes = new LaneGraph(network);
            Rules = new TrafficRules(network);
            Junctions = new JunctionController(network, Lanes, Rules);
            Paths = new Pathfinder(network, Lanes, Rules);

            Lanes.LanesRemoved += lanes =>
            {
                foreach (var lane in lanes)
                    foreach (var v in lane.Vehicles.ToList()) Remove(v, despawned: false);
            };
            Lanes.ConnectionsRemoved += conns =>
            {
                var set = new HashSet<LaneConnection>(conns);
                foreach (var c in conns)
                    foreach (var v in c.Vehicles.ToList()) Remove(v, despawned: false);
                foreach (var v in Vehicles)
                    if (v.Planned != null && set.Contains(v.Planned)) v.Planned = null;
            };
            // Zielsegment zerteilt/umgebaut → Ziel auf das passende Teilstück umhängen
            network.SegmentReplaced += (old, parts) =>
            {
                foreach (var v in Vehicles)
                {
                    if (v.DestinationSegmentId != old.Id) continue;
                    var p = old.Evaluate(v.DestinationT);
                    RoadSegment bestSeg = null;
                    float bestD = float.MaxValue;
                    foreach (var part in parts)
                    {
                        float d = part.DistanceTo(p);
                        if (d < bestD) { bestD = d; bestSeg = part; }
                    }
                    if (bestSeg == null) continue;
                    v.DestinationSegmentId = bestSeg.Id;
                    v.DestinationT = bestSeg.ClosestT(p);
                }
            };
        }

        public IEnumerable<Vehicle> ActiveVehicles => Vehicles.Where(v => !removed.Contains(v));

        // ------------------------------------------------------------ Tick

        /// <summary>Kalender-Tick: nichts zu tun – der Verkehr läuft in <see cref="Substep"/>.</summary>
        public void Tick(SimulationWorld world) { }

        /// <summary>Ein Verkehrsschritt (<see cref="Dt"/> s Verkehrszeit).</summary>
        public void Substep(SimulationWorld world) => Step();

        /// <summary>Ein Verkehrsschritt ohne Welt (Tests, Werkzeuge).</summary>
        public void Step()
        {
            tick++;
            Lanes.Update();

            if (Demand > 0f && Vehicles.Count < MaxVehicles) Spawn();

            // Verbindungen zuerst (Fahrzeuge verlassen die Kreuzung), dann Spuren – jeweils vorderstes zuerst.
            var conns = new HashSet<LaneConnection>();
            var lanes = new HashSet<Lane>();
            foreach (var v in Vehicles)
            {
                if (removed.Contains(v)) continue;
                if (v.Connection != null) conns.Add(v.Connection);
                else if (v.Lane != null) lanes.Add(v.Lane);
            }
            foreach (var c in conns)
            {
                var list = c.Vehicles.ToArray();
                for (int i = 0; i < list.Length; i++)
                    if (list[i].LastTick != tick && list[i].Connection == c) StepOnConnection(list[i], c);
            }
            foreach (var l in lanes)
            {
                var list = l.Vehicles.ToArray();
                for (int i = 0; i < list.Length; i++)
                    if (list[i].LastTick != tick && list[i].Lane == l && list[i].Connection == null) StepOnLane(list[i], l);
            }

            UpdatePositionsAndStats();
            if (tick % 20 == 0) RelaxIdleLanes();

            if (removed.Count > 0)
            {
                Vehicles.RemoveAll(v => removed.Contains(v));
                removed.Clear();
            }
        }

        // ------------------------------------------------------------ Fahren

        float LaneSpeed(Lane lane, Vehicle v) => MathF.Min(v.MaxSpeed, Rules.SpeedLimit(lane.Segment));

        float ConnectionSpeed(LaneConnection c, Vehicle v)
        {
            float limit = MathF.Min(Rules.SpeedLimit(c.From.Segment), Rules.SpeedLimit(c.To.Segment));
            switch (c.Turn)
            {
                case TurnType.Straight: limit = c.From.Segment.IsRoundabout || c.To.Segment.IsRoundabout ? MathF.Min(limit, 8f) : limit; break;
                case TurnType.UTurn: limit = 4f; break;
                default: limit = MathF.Min(limit, 7.5f); break;
            }
            return MathF.Min(v.MaxSpeed, limit);
        }

        /// <summary>Intelligent Driver Model: Beschleunigung aus Wunschgeschwindigkeit und Abstand.</summary>
        static float Idm(Vehicle v, float desired, float gap, float leaderSpeed)
        {
            float a = v.Acceleration;
            float speed = v.Speed;
            float dv = speed - leaderSpeed;
            float sStar = MinGap + MathF.Max(0f, speed * TimeHeadway + speed * dv / (2f * MathF.Sqrt(a * ComfortDecel)));
            float free = 1f - MathF.Pow(speed / MathF.Max(0.1f, desired), 4f);
            float interaction = sStar / MathF.Max(0.1f, gap);
            return a * (free - interaction * interaction);
        }

        void StepOnLane(Vehicle v, Lane lane)
        {
            v.LastTick = tick;
            // --- ÖPNV: Linienbus steht an der Haltestelle (Ein-/Aussteigen)
            if (v.IsScheduled && HoldAtStop(v)) return;
            // --- Ende ÖPNV
            if (v.Planned != null && v.Planned.From != lane) v.Planned = null;

            int index = lane.Vehicles.IndexOf(v);
            var leader = index > 0 ? lane.Vehicles[index - 1] : null;
            float length = lane.Length;
            float desired = LaneSpeed(lane, v);
            float gap = 1000f, leaderSpeed = desired;
            if (leader != null)
            {
                gap = leader.S - leader.Length - v.S;
                leaderSpeed = leader.Speed;
            }

            float distToEnd = length - v.S;
            bool mayExit = false;
            LaneConnection exit = null;

            if (!v.IsOnFinalEdge && (leader == null || gap > distToEnd))
            {
                exit = v.Planned ?? (v.Planned = ChooseConnection(v));
                if (removed.Contains(v)) return;
                float endGap = 1000f, endSpeed = 0f;
                if (exit == null)
                {
                    endGap = distToEnd - 0.5f;
                }
                else
                {
                    float lookahead = 12f + v.Speed * 3f;
                    bool permitted = distToEnd > lookahead || CanEnter(v, exit, distToEnd);
                    if (!permitted) endGap = distToEnd - 0.5f;
                    else if (exit.To.FreeSpaceAtStart - JunctionQueue(exit) < v.Length + 1f && distToEnd < lookahead)
                        endGap = distToEnd - 0.5f; // Kreuzung nicht blockieren
                    else
                    {
                        mayExit = distToEnd <= lookahead;
                        var cl = exit.Last;
                        if (cl != null) { endGap = distToEnd + cl.S - cl.Length; endSpeed = cl.Speed; }
                    }
                }
                if (endGap < gap) { gap = endGap; leaderSpeed = endSpeed; }
            }
            // --- ÖPNV: Linienbus bremst bis zur Haltestelle (wie vor einem stehenden Fahrzeug)
            if (v.IsScheduled && v.IsOnFinalEdge)
            {
                float stopGap = ArrivalS(v, lane) + MinGap - v.S;
                if (stopGap < gap) { gap = MathF.Max(0.1f, stopGap); leaderSpeed = 0f; }
            }
            // --- Ende ÖPNV

            Advance(v, desired, gap, leaderSpeed, leader);

            // Stillstand / Haltelinie
            if (v.Speed < 0.3f) v.WaitTime += Dt; else v.WaitTime = 0f;
            if (v.Speed < 0.3f && length - v.S < 4f) v.StopLineWait += Dt;
            else if (v.Speed > 2f) v.StopLineWait = 0f;
            if (v.LaneChangeCooldown > 0f) v.LaneChangeCooldown -= Dt;

            if (v.IsOnFinalEdge && v.S >= ArrivalS(v, lane) - 1f)
            {
                // --- ÖPNV: Linienbus kommt an der Haltestelle an (bleibt stehen, statt zu verschwinden)
                if (v.IsScheduled)
                {
                    if (v.Speed < 1.5f || v.S >= ArrivalS(v, lane)) { ReachStop(v); return; }
                }
                else
                // --- Ende ÖPNV
                {
                    Arrive(v);
                    return;
                }
            }
            if (v.S >= length)
            {
                if (mayExit && exit != null) { EnterConnection(v, exit, v.S - length); return; }
                v.S = length;
                v.Speed = 0f;
            }

            if (!v.IsOnFinalEdge && v.LaneChangeCooldown <= 0f) TryLaneChange(v, lane);

            if (v.WaitTime > 25f && v.Reroutes < 3) { Reroute(v); v.WaitTime = 10f; }
            else if (v.WaitTime > 90f) Remove(v, despawned: true);
        }

        void StepOnConnection(Vehicle v, LaneConnection c)
        {
            v.LastTick = tick;
            int index = c.Vehicles.IndexOf(v);
            var leader = index > 0 ? c.Vehicles[index - 1] : null;
            float desired = ConnectionSpeed(c, v);
            float gap = 1000f, leaderSpeed = desired;
            if (leader != null) { gap = leader.S - leader.Length - v.S; leaderSpeed = leader.Speed; }

            float distToEnd = c.Length - v.S;
            var last = c.To.Last;
            if (last != null)
            {
                float g = distToEnd + last.S - last.Length;
                if (g < gap) { gap = g; leaderSpeed = last.Speed; }
            }

            Advance(v, desired, gap, leaderSpeed, leader);
            if (v.Speed < 0.3f) v.WaitTime += Dt; else v.WaitTime = 0f;

            if (v.S >= c.Length)
            {
                float overshoot = v.S - c.Length;
                var target = c.To;
                if (target.Last != null && target.Last.S - target.Last.Length < overshoot + 0.5f)
                {
                    v.S = c.Length;
                    v.Speed = 0f;
                }
                else
                {
                    c.Vehicles.Remove(v);
                    v.Connection = null;
                    v.Lane = target;
                    v.S = overshoot;
                    target.Insert(v);
                    v.RouteIndex++;
                    v.Planned = null;
                    v.WaitTime = 0f;
                }
            }
            if (v.WaitTime > 90f) Remove(v, despawned: true);
        }

        /// <summary>Strecke auf der letzten Kante, an der das Fahrzeug ankommt (Zufahrt oder Spurende).</summary>
        static float ArrivalS(Vehicle v, Lane lane)
        {
            if (!v.HasSegmentDestination || lane.Segment.Id != v.DestinationSegmentId) return lane.Length;
            float s = lane.Along ? v.DestinationT * lane.Length : (1f - v.DestinationT) * lane.Length;
            return Math.Clamp(s, 1.5f, lane.Length);
        }

        static void Advance(Vehicle v, float desired, float gap, float leaderSpeed, Vehicle leader)
        {
            float acc = Idm(v, desired, gap, leaderSpeed);
            acc = MathF.Max(acc, -9f);
            v.Speed = Math.Clamp(v.Speed + acc * Dt, 0f, desired * 1.05f);
            float move = v.Speed * Dt;
            if (gap < 1000f) move = MathF.Min(move, MathF.Max(0f, gap - 0.3f));
            if (leader != null) move = MathF.Min(move, MathF.Max(0f, leader.S - leader.Length - 0.3f - v.S));
            if (move < v.Speed * Dt * 0.5f && move < 0.05f) v.Speed = MathF.Min(v.Speed, move / Dt);
            v.S += move;
        }

        /// <summary>Wie viel Platz belegen Fahrzeuge, die gerade über die Kreuzung in diese Spur fahren?</summary>
        float JunctionQueue(LaneConnection exit)
        {
            float sum = 0f;
            foreach (var c in Lanes.GetConnections(exit.Node.Id))
            {
                if (c.To != exit.To) continue;
                foreach (var v in c.Vehicles) sum += v.Length + MinGap;
            }
            return sum;
        }

        void EnterConnection(Vehicle v, LaneConnection c, float overshoot)
        {
            v.Lane.Vehicles.Remove(v);
            v.Connection = c;
            v.S = MathF.Min(overshoot, c.Length);
            c.Insert(v);
            v.Planned = null;
            v.StopLineWait = 0f;
            v.WaitTime = 0f;
        }

        // ------------------------------------------------------------ Spurwahl

        LaneConnection ChooseConnection(Vehicle v)
        {
            if (!RouteValid(v))
            {
                Reroute(v);
                if (v.IsOnFinalEdge || !RouteValid(v)) return null;
            }
            var next = v.NextEdge.Value;
            var after = v.EdgeAfterNext;
            var node = v.Lane.To;
            LaneConnection best = null;
            float bestScore = float.MaxValue;
            foreach (var c in v.Lane.Outgoing)
            {
                if (!c.To.Edge.Equals(next) || (c.To.Allowed & v.Class) == 0) continue;
                if (!Rules.IsTurnAllowed(node.Id, v.Lane.Segment.Id, c.To.Segment.Id, v.Class)) continue;
                float score = c.To.Vehicles.Count + c.Vehicles.Count;
                if (after.HasValue && !HasConnection(c.To, after.Value, v.Class)) score += 20f;
                if (v.Class == VehicleClass.Bus && c.To.Definition.Kind == LaneKind.Bus) score -= 5f;
                if (v.IsScheduled && !after.HasValue) score -= c.To.Rank * 3f; // ÖPNV: vor der Haltestelle rechts einordnen
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        bool HasConnection(Lane lane, EdgeKey edge, VehicleClass cls)
        {
            foreach (var o in lane.Outgoing)
                if (o.To.Edge.Equals(edge) && (o.To.Allowed & cls) != 0 &&
                    Rules.IsTurnAllowed(o.Node.Id, lane.Segment.Id, o.To.Segment.Id, cls)) return true;
            return false;
        }

        bool RouteValid(Vehicle v)
        {
            for (int i = v.RouteIndex + 1; i < v.Route.Count; i++)
            {
                var e = v.Route[i];
                if (network.GetSegment(e.SegmentId) == null || !Rules.IsSegmentAllowed(e.SegmentId, v.Class)) return false;
            }
            if (v.HasSegmentDestination) return network.GetSegment(v.DestinationSegmentId) != null;
            return v.Destination != null && network.Contains(v.Destination);
        }

        List<EdgeKey> FindRoute(Vehicle v, EdgeKey from, float s, Lane fromLane) =>
            v.HasSegmentDestination
                ? v.DestinationDirection != 0 // ÖPNV: Haltestelle auf einer Straßenseite
                    ? Paths.FindPathToEdge(from, s, new EdgeKey(v.DestinationSegmentId, v.DestinationDirection == 1), v.DestinationT, v.Class, fromLane)
                    : Paths.FindPathToSegment(from, s, v.DestinationSegmentId, v.DestinationT, v.Class, fromLane)
                : Paths.FindPath(from, v.Destination, v.Class, fromLane);

        /// <summary>Wechselt auf eine Nachbarspur, wenn nur von dort die nächste Abbiegung möglich ist.</summary>
        void TryLaneChange(Vehicle v, Lane lane)
        {
            var next = v.NextEdge;
            if (!next.HasValue || HasConnection(lane, next.Value, v.Class)) return;
            if (lane.Length - v.S < 6f) return;

            // Nächstgelegene passende Spur suchen
            Lane target = null;
            int bestDistance = int.MaxValue;
            foreach (var l in Lanes.LanesOf(lane.Edge))
            {
                if ((l.Allowed & v.Class) == 0 || !HasConnection(l, next.Value, v.Class)) continue;
                int d = Math.Abs(l.Rank - lane.Rank);
                if (d < bestDistance) { bestDistance = d; target = l; }
            }
            if (target == null) return;
            var neighbor = target.Rank < lane.Rank ? lane.LeftNeighbor : lane.RightNeighbor;
            if (neighbor == null || (neighbor.Allowed & v.Class) == 0) return;

            // Lücke prüfen
            Vehicle front = null, back = null;
            foreach (var o in neighbor.Vehicles)
            {
                if (o.S >= v.S) front = o;
                else { back = o; break; }
            }
            if (front != null && front.S - front.Length - v.S < 3f + v.Speed * 0.6f) return;
            if (back != null && v.S - v.Length - back.S < 4f + back.Speed * 0.9f) return;

            lane.Vehicles.Remove(v);
            v.Lateral += lane.Offset - neighbor.Offset;
            v.Lane = neighbor;
            neighbor.Insert(v);
            v.Planned = null;
            v.LaneChangeCooldown = 1.5f;
        }

        void Reroute(Vehicle v)
        {
            if (v.Lane == null || v.InJunction) return;
            v.Reroutes++;
            var path = FindRoute(v, v.Lane.Edge, v.S, v.Lane) ?? FindRoute(v, v.Lane.Edge, v.S, null);
            if (path == null && (v.TripId != 0 || v.IsScheduled)) // ÖPNV: Linienbus meldet sich beim TransitSystem ab
            {
                // Bürgerfahrt: Ziel nicht mehr erreichbar → abbrechen, das Bürgersystem lässt den Bürger trotzdem ankommen
                Remove(v, despawned: true);
                return;
            }
            if (path == null)
            {
                // Ziel nicht mehr erreichbar → neues Ziel
                RefreshEndpoints();
                var dest = PickDestination(v.Class, v.Lane.To);
                if (dest != null) path = Paths.FindPath(v.Lane.Edge, dest.Node, v.Class);
                if (dest == null || path == null) { Remove(v, despawned: true); return; }
                v.Destination = dest.Node;
            }
            v.Route = path;
            v.RouteIndex = 0;
            v.Planned = null;
        }

        // ------------------------------------------------------------ Kreuzungen

        /// <summary>Darf das Fahrzeug jetzt in die Kreuzung einfahren?</summary>
        bool CanEnter(Vehicle v, LaneConnection c, float distToEnd)
        {
            var j = Junctions.Get(c.Node);
            if (!j.IsJunction) return true;
            int mine = c.From.Segment.Id;

            if (j.Control == JunctionControl.TrafficLights)
            {
                var light = Junctions.LightFor(j, mine, Time);
                // Bei Gelb noch fahren, wenn Anhalten nicht mehr möglich ist
                return light == LightState.Green || (light == LightState.Yellow && distToEnd < v.Speed * 1.2f);
            }

            if (j.SignOf(mine) == ApproachSign.Stop && v.StopLineWait < 1.5f) return false;

            int rank = j.RankOf(mine);
            foreach (int other in j.Approaches)
            {
                if (other == mine) continue;
                int r = j.RankOf(other);
                bool yieldTo = r > rank || (r == rank && rank == 1 && j.FromRight(mine, other) && v.WaitTime < 6f);
                if (yieldTo && ApproachBusy(j, other)) return false;
            }

            // Fahrzeuge anderer Zufahrten in der Kreuzung abwarten (außer Vorfahrtströme untereinander)
            foreach (var conn in Lanes.GetConnections(c.Node.Id))
            {
                if (conn.Vehicles.Count == 0) continue;
                int from = conn.From.Segment.Id;
                if (from == mine) continue;
                if (conn.To == c.To) return false;   // fährt gerade in dieselbe Spur ein
                if (rank == 2) continue;             // Vorfahrtstrom: die anderen haben gewartet
                if (j.RankOf(from) >= rank) return false;
            }
            return true;
        }

        /// <summary>Nähert sich auf dieser Zufahrt ein Fahrzeug, das (noch) fahren will?</summary>
        static bool ApproachBusy(JunctionInfo j, int approach)
        {
            foreach (var lane in j.IncomingLanes[approach])
            {
                if (lane.Vehicles.Count == 0) continue;
                var f = lane.Vehicles[0];
                float dist = lane.Length - f.S;
                if (dist > 15f + f.Speed * 3f) continue;
                if (f.Speed < 0.3f && f.WaitTime > 2f) continue; // steht selbst und wartet
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------ Entstehen / Verschwinden

        void RefreshEndpoints()
        {
            if (endpointsVersion == Lanes.Version) return;
            endpointsVersion = Lanes.Version;
            endpoints.Clear();
            externals.Clear();
            foreach (var node in network.Nodes)
            {
                // Außenanschluss: Straßenende (Grad 1) am Kartenrand – optional jede Sackgasse
                bool edge = node.Position.X < worldMin.X + 40f || node.Position.Y < worldMin.Y + 40f ||
                            node.Position.X > worldMax.X - 40f || node.Position.Y > worldMax.Y - 40f;
                if (node.SegmentIds.Count != 1 || !(edge || deadEndsAreExternal)) continue;
                var ep = new Endpoint { Node = node };
                ep.Out.AddRange(Lanes.OutgoingLanes(node));
                ep.In.AddRange(Lanes.IncomingLanes(node));
                foreach (int id in node.SegmentIds) if (network.GetSegment(id).Type.IsHighway) ep.Highway = true;
                if (ep.Out.Count > 0 || ep.In.Count > 0)
                {
                    endpoints.Add(ep);
                    externals.Add(node);
                }
            }
        }

        readonly List<RoadNode> externals = new List<RoadNode>();

        /// <summary>Außenanschlüsse: Straßenenden am Kartenrand (Verbindung zur Außenwelt).</summary>
        public IReadOnlyList<RoadNode> ExternalConnections { get { RefreshEndpoints(); return externals; } }

        /// <summary>Anzahl der Außenanschlüsse.</summary>
        public int EndpointCount { get { RefreshEndpoints(); return endpoints.Count; } }

        /// <summary>Kann von diesem Außenanschluss aus losgefahren (outgoing) bzw. dort angekommen werden?</summary>
        public bool IsExternalUsable(RoadNode node, bool outgoing)
        {
            RefreshEndpoints();
            foreach (var e in endpoints)
                if (e.Node == node) return outgoing ? e.Out.Count > 0 : e.In.Count > 0;
            return false;
        }

        /// <summary>Durchgangsverkehr zwischen Außenanschlüssen.</summary>
        void Spawn()
        {
            RefreshEndpoints();
            if (endpoints.Count < 2) return;
            float weight = endpoints.Count;
            if (ThroughTrafficWeight != null) { weight = 0f; foreach (var e in endpoints) weight += MathF.Max(0f, ThroughTrafficWeight(e.Node, null)); } // Gateways
            float expected = Demand * weight * ThroughTrafficRate;
            int count = (int)expected + (rng.NextDouble() < expected - (int)expected ? 1 : 0);
            for (int i = 0; i < count && Vehicles.Count < MaxVehicles; i++) TrySpawnOne(weight);
        }

        /// <summary>Gateways: Startpunkt nach Gewicht (ein Zufallswert wie bisher).</summary>
        Endpoint PickOrigin(float total)
        {
            if (ThroughTrafficWeight == null || total <= 0f) return endpoints[rng.Next(endpoints.Count)];
            double roll = rng.NextDouble() * total;
            foreach (var e in endpoints)
            {
                roll -= MathF.Max(0f, ThroughTrafficWeight(e.Node, null));
                if (roll < 0) return e;
            }
            return endpoints[endpoints.Count - 1];
        }

        void TrySpawnOne(float total)
        {
            var origin = PickOrigin(total);
            if (origin.Out.Count == 0) return;

            VehicleClass cls;
            bool hasTram = origin.Out.Any(l => l.Allowed == VehicleClass.Tram);
            double roll = rng.NextDouble();
            if (hasTram && roll < 0.12) cls = VehicleClass.Tram;
            else if (origin.Highway) cls = roll < 0.75 ? VehicleClass.Car : VehicleClass.Truck;
            else cls = roll < 0.82 ? VehicleClass.Car : roll < 0.92 ? VehicleClass.Truck : VehicleClass.Bus;

            var lanes = origin.Out.Where(l => (l.Allowed & cls) != 0 && Rules.IsSegmentAllowed(l.Segment.Id, cls)).ToList();
            if (lanes.Count == 0) return;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var dest = PickDestination(cls, origin.Node);
                if (dest == null) return;
                var startEdge = lanes[rng.Next(lanes.Count)].Edge;
                var path = Paths.FindPath(startEdge, dest.Node, cls);
                if (path == null) continue;

                // Spur so wählen, dass die erste Abbiegung ohne Spurwechsel geht
                Lane lane = null;
                foreach (var l in lanes)
                {
                    if (!l.Edge.Equals(startEdge) || l.FreeSpaceAtStart < VehicleClasses.Length(cls) + 4f) continue;
                    if (lane == null || (path.Count > 1 && HasConnection(l, path[1], cls) && !HasConnection(lane, path[1], cls))) lane = l;
                }
                if (lane == null) return;

                var v = new Vehicle
                {
                    Id = nextId++,
                    Class = cls,
                    Length = VehicleClasses.Length(cls),
                    MaxSpeed = VehicleClasses.MaxSpeed(cls) * (0.9f + (float)rng.NextDouble() * 0.15f),
                    Acceleration = VehicleClasses.Acceleration(cls),
                    Variant = rng.Next(8),
                    Lane = lane,
                    Destination = dest.Node,
                    Route = path,
                    StartTime = Time,
                };
                v.S = MathF.Min(v.Length, lane.Length);
                v.Speed = MathF.Min(8f, LaneSpeed(lane, v));
                lane.Insert(v);
                SamplePosition(v);
                v.PrevPosition = v.Position;
                v.PrevHeight = v.Height;
                v.PrevDirection = v.Direction;
                Vehicles.Add(v);
                Stats.Spawned++;
                return;
            }
        }

        Endpoint PickDestination(VehicleClass cls, RoadNode except)
        {
            if (endpoints.Count == 0) return null;
            if (ThroughTrafficWeight != null) // --- Gateways: Ziel nach Gewicht (ein Zufallswert)
            {
                double sum = 0;
                foreach (var e in endpoints)
                    if (e.Node != except && e.In.Any(l => (l.Allowed & cls) != 0)) sum += MathF.Max(0f, ThroughTrafficWeight(except, e.Node));
                if (sum <= 0) return null;
                double roll = rng.NextDouble() * sum;
                foreach (var e in endpoints)
                {
                    if (e.Node == except || !e.In.Any(l => (l.Allowed & cls) != 0)) continue;
                    float w = MathF.Max(0f, ThroughTrafficWeight(except, e.Node));
                    if (w <= 0f) continue;
                    roll -= w;
                    if (roll < 0) return e;
                }
                return null;
            }
            for (int i = 0; i < 6; i++)
            {
                var e = endpoints[rng.Next(endpoints.Count)];
                if (e.Node == except) continue;
                if (e.In.Any(l => (l.Allowed & cls) != 0)) return e;
            }
            return null;
        }

        void Arrive(Vehicle v)
        {
            Stats.Arrived++;
            Remove(v, despawned: false, arrived: true);
        }

        void Remove(Vehicle v, bool despawned, bool arrived = false)
        {
            if (!removed.Add(v)) return;
            v.Lane?.Vehicles.Remove(v);
            v.Connection?.Vehicles.Remove(v);
            v.Lane = null;
            v.Connection = null;
            if (despawned) Stats.Despawned++;
            if (v.IsScheduled) ScheduledVehicleRemoved?.Invoke(v, despawned); // ÖPNV
            if (v.TripId != 0)
            {
                if (v.TripId > 0) { if (arrived) Stats.CitizenTripsArrived++; else Stats.CitizenTripsFailed++; } // G5: Lkw < 0
                TripEnded?.Invoke(v, arrived);
            }
        }

        // ------------------------------------------------------------ Bürgerfahrten

        /// <summary>
        /// Startet eine Fahrt von einer Gebäudezufahrt bzw. einem Außenanschluss zu einem anderen.
        /// Liefert null, wenn kein Weg existiert, das Fahrzeuglimit erreicht ist oder am Start kein Platz ist –
        /// der Aufrufer behandelt die Fahrt dann als „nicht sichtbar“ (zu Fuß/ÖPNV-Platzhalter).
        /// </summary>
        public Vehicle RequestTrip(TripEnd from, TripEnd to, VehicleClass cls, int tripId)
        {
            if (tripId == 0) throw new ArgumentException("tripId muss != 0 sein.", nameof(tripId));
            Lanes.Update();
            if (Vehicles.Count >= MaxVehicles) return null;
            if (!to.IsNode && network.GetSegment(to.SegmentId) == null) return null;
            if (to.IsNode && !network.Contains(to.Node)) return null;

            // Mögliche Startspuren mit Startposition
            var starts = new List<(Lane lane, float s)>();
            if (from.IsNode)
            {
                if (!network.Contains(from.Node)) return null;
                foreach (var l in Lanes.OutgoingLanes(from.Node)) starts.Add((l, 0f));
            }
            else
            {
                var seg = network.GetSegment(from.SegmentId);
                if (seg == null) return null;
                foreach (var l in Lanes.GetLanes(seg.Id)) starts.Add((l, l.Along ? from.T * l.Length : (1f - from.T) * l.Length));
            }
            starts.RemoveAll(x => (x.lane.Allowed & cls) == 0 || !Rules.IsSegmentAllowed(x.lane.Segment.Id, cls));
            if (starts.Count == 0) return null;

            // Günstigste Fahrtrichtung (aus der Einfahrt darf links und rechts abgebogen werden)
            List<EdgeKey> bestPath = null;
            float bestCost = float.MaxValue, bestS = 0f;
            EdgeKey bestEdge = default;
            foreach (var group in starts.GroupBy(x => x.lane.Edge))
            {
                float s = group.First().s;
                var path = to.IsNode
                    ? Paths.FindPath(group.Key, to.Node, cls)
                    : Paths.FindPathToSegment(group.Key, s, to.SegmentId, to.T, cls);
                if (path == null || Paths.LastCost >= bestCost) continue;
                bestCost = Paths.LastCost;
                bestPath = path;
                bestEdge = group.Key;
                bestS = s;
            }
            if (bestPath == null) return null;

            // Spur mit Platz an der Startstelle, bevorzugt eine, von der die erste Abbiegung ohne Spurwechsel geht
            float length = VehicleClasses.Length(cls);
            Lane lane = null;
            float startS = 0f;
            foreach (var (l, _) in starts)
            {
                if (!l.Edge.Equals(bestEdge)) continue;
                float s = Math.Clamp(bestS, MathF.Min(length, l.Length), l.Length);
                if (!HasRoomAt(l, s, length)) continue;
                if (lane == null || (bestPath.Count > 1 && HasConnection(l, bestPath[1], cls) && !HasConnection(lane, bestPath[1], cls)))
                {
                    lane = l;
                    startS = s;
                }
            }
            if (lane == null) return null;

            var v = new Vehicle
            {
                Id = nextId++,
                Class = cls,
                Length = length,
                MaxSpeed = VehicleClasses.MaxSpeed(cls) * (0.9f + (float)rng.NextDouble() * 0.15f),
                Acceleration = VehicleClasses.Acceleration(cls),
                Variant = rng.Next(8),
                Lane = lane,
                Destination = to.IsNode ? to.Node : null,
                DestinationSegmentId = to.IsNode ? -1 : to.SegmentId,
                DestinationT = to.T,
                Route = bestPath,
                TripId = tripId,
                StartTime = Time,
            };
            v.S = startS;
            v.Speed = from.IsNode ? MathF.Min(8f, LaneSpeed(lane, v)) : 0f; // aus der Einfahrt: aus dem Stand
            lane.Insert(v);
            SamplePosition(v);
            v.PrevPosition = v.Position;
            v.PrevHeight = v.Height;
            v.PrevDirection = v.Direction;
            Vehicles.Add(v);
            Stats.Spawned++;
            if (tripId > 0) Stats.CitizenTripsStarted++; // G5: Lieferfahrten (negative Ids) zählen nicht als Bürgerfahrt
            return v;
        }

        /// <summary>Ist auf der Spur an Stelle s (Front) Platz für ein Fahrzeug der Länge <paramref name="length"/>?</summary>
        static bool HasRoomAt(Lane lane, float s, float length)
        {
            foreach (var o in lane.Vehicles)
            {
                // Belegt: [o.S - o.Length, o.S] gegen [s - length, s] mit Sicherheitsabstand
                if (o.S - o.Length < s + 4f && o.S > s - length - 4f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------ Darstellung / Statistik

        void UpdatePositionsAndStats()
        {
            float speedSum = 0f;
            int active = 0, waiting = 0, citizens = 0, deliveries = 0, services = 0; // Dienstfahrzeuge
            foreach (var v in Vehicles)
            {
                if (removed.Contains(v)) continue;
                if (v.TripId > 0) citizens++;
                else if (v.TripId < 0) { if (TripIdRanges.IsService(v.TripId)) services++; else deliveries++; } // G5: Lkw des Warensystems; Dienstfahrzeuge
                v.PrevPosition = v.Position;
                v.PrevHeight = v.Height;
                v.PrevDirection = v.Direction;
                if (MathF.Abs(v.Lateral) > 0.01f)
                    v.Lateral = MathF.Sign(v.Lateral) * MathF.Max(0f, MathF.Abs(v.Lateral) - 2.5f * Dt);
                else v.Lateral = 0f;
                SamplePosition(v);
                speedSum += v.Speed;
                active++;
                if (v.Speed < 0.5f) waiting++;
            }
            Stats.Active = active;
            Stats.Waiting = waiting;
            Stats.CitizenVehicles = citizens;
            Stats.DeliveryVehicles = deliveries;
            Stats.ServiceVehicles = services; // Dienstfahrzeuge
            Stats.AverageSpeedKmh = active > 0 ? speedSum / active * 3.6f : 0f;

            // Stauindex je Spur (gleitender Mittelwert)
            var perLane = new Dictionary<Lane, (float sum, int n)>();
            foreach (var v in Vehicles)
            {
                if (v.Lane == null || removed.Contains(v)) continue;
                float ratio = v.Speed / MathF.Max(1f, LaneSpeed(v.Lane, v));
                perLane.TryGetValue(v.Lane, out var acc);
                perLane[v.Lane] = (acc.sum + ratio, acc.n + 1);
            }
            foreach (var kv in perLane)
                kv.Key.SpeedRatio += (kv.Value.sum / kv.Value.n - kv.Key.SpeedRatio) * 0.05f;
        }

        void RelaxIdleLanes()
        {
            foreach (var lane in Lanes.AllLanes)
                if (lane.Vehicles.Count == 0) lane.SpeedRatio += (1f - lane.SpeedRatio) * 0.3f;
        }

        static void SamplePosition(Vehicle v)
        {
            Vector2 pos, dir;
            float h;
            if (v.Connection != null)
            {
                v.Connection.Sample(MathF.Max(0f, v.S - v.Length * 0.5f), out pos, out h, out dir);
            }
            else if (v.Lane != null)
            {
                v.Lane.Sample(MathF.Max(0f, v.S - v.Length * 0.5f), v.Lateral, out pos, out h, out dir);
            }
            else return;
            v.Position = pos;
            v.Height = h;
            v.Direction = dir;
        }

        /// <summary>Lichtzustand einer Zufahrt (für die Anzeige).</summary>
        public LightState LightAt(RoadNode node, int approachSegment)
        {
            var j = Junctions.Get(node);
            return Junctions.LightFor(j, approachSegment, Time);
        }
    }
}
