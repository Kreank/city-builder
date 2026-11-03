using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>
    /// Außenanbindungen der Stadtkarte (<c>world.Gateways</c>): vorhandene Fernstraße, Bahn-, Wasser- und Luftwege wie in
    /// Cities: Skylines. Beim Kartenstart (neue Welt mit <see cref="WorldSettings.PrebuiltConnections"/>) erzeugt
    /// <see cref="GatewayGenerator"/> die Lage; dieses System hält die Gateways, beantwortet die Hooks der anderen Systeme
    /// (Zuzug über Anschlüsse, Handel Richtung Partnerland, Durchgangsverkehr, freigehaltener Bahnkorridor) und zählt
    /// Verkehr/Handel je Anschluss.
    /// </summary>
    public sealed partial class GatewaySystem : ISimulationSystem, ISubstepSystem
    {
        public int TickInterval => 1;

        readonly SimulationWorld world;
        readonly List<Gateway> gateways = new List<Gateway>();
        int nextId = 1;

        /// <summary>Alle Gateways (nach Id).</summary>
        public IReadOnlyList<Gateway> All => gateways;
        public Gateway Get(int id)
        {
            foreach (var g in gateways) if (g.Id == id) return g;
            return null;
        }

        // --- Autobahn / Anschlussstelle
        /// <summary>Nummer der Autobahn („A 7“), 0 = keine.</summary>
        public int HighwayNumber { get; private set; }
        public bool HasHighway => HighwayNumber > 0;
        /// <summary>Achse der Autobahn (Grad, 0 = Ost) und Mitte der Anschlussstelle.</summary>
        public float HighwayAngle { get; private set; }
        public Vector2 Interchange { get; private set; }
        /// <summary>Enden des Zubringers (offene Stadtstraßen der Anschlussstelle), an die der Spieler anschließt.</summary>
        public IReadOnlyList<Vector2> FeederEnds => feederEnds;
        readonly List<Vector2> feederEnds = new List<Vector2>();

        // --- Bahnkorridor
        public bool HasRailCorridor { get; private set; }
        public Vector2 RailStart { get; private set; }
        public Vector2 RailEnd { get; private set; }
        public float RailHalfWidth => GatewayRules.RailCorridorHalfWidth;

        /// <summary>Bericht der Erzeugung (nur in der laufenden Sitzung, nicht gespeichert).</summary>
        public GatewayLayoutReport LastReport { get; private set; }

        // --- Zuzug
        /// <summary>Haushalte, die heute/gestern nicht zuziehen konnten, weil ihre Wohnung an keinem Anschluss hängt.</summary>
        public int BlockedMoveInsToday { get; private set; }
        public int BlockedMoveInsYesterday { get; private set; }
        /// <summary>Wohngebäude mit Anschluss / ohne Anschluss (Stand der letzten Stundenprüfung).</summary>
        public int HousingConnected { get; private set; }
        public int HousingUnconnected { get; private set; }

        public GatewaySystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        // ------------------------------------------------------------ Erzeugen (Kartenstart)

        /// <summary>
        /// Erzeugt die Außenanbindung aus der Region (nur neue Welt): Autobahn mit Anschlussstelle + Zubringer, Bahnkorridor,
        /// Wasserstraßen, Luftweg. Kosten trägt die Welt.
        /// </summary>
        public GatewayLayoutReport Generate()
        {
            var targets = GatewayGenerator.Targets(world.Planet);
            int number = 1 + (int)(GatewayRules.Hash(world.StartRegion?.Id ?? 0, world.Settings.TerrainSeed) % 98u);
            HighwayNumber = number; // für die Namen; bleibt 0, wenn die Autobahn nicht gebaut werden kann
            var report = GatewayGenerator.BuildHighway(world, this, targets);
            if (!report.HighwayBuilt) HighwayNumber = 0;

            // Bahnkorridor quer zur Autobahn (ohne Autobahn quer durch die Mitte)
            float hwAngle = report.HighwayBuilt ? report.HighwayAngle : 0f;
            var center = report.HighwayBuilt ? report.Interchange : Vector2.Zero;
            if (GatewayGenerator.PlanRail(world, targets, hwAngle, center, out var ra, out var rb, out _))
            {
                HasRailCorridor = true;
                RailStart = ra;
                RailEnd = rb;
                AddEnd(GatewayKind.Rail, GatewayStatus.Planned, ra, Vector2.Normalize(ra - rb), targets, "Bahn", 0f, 0, 0);
                AddEnd(GatewayKind.Rail, GatewayStatus.Planned, rb, Vector2.Normalize(rb - ra), targets, "Bahn", 0f, 0, 0);
                LinkPartners(gateways.Count - 2, gateways.Count - 1);
            }

            // Wasserstraßen (Fluss/Küste am Kartenrand)
            foreach (var run in GatewayGenerator.WaterAtEdges(world))
            {
                bool navigable = run.Sea || (run.Width >= GatewayRules.NavigableWidth && run.Depth >= GatewayRules.NavigableDepth);
                var g = AddEnd(GatewayKind.Water, GatewayStatus.Planned, run.Center, run.Outward, targets,
                    run.Sea ? "Seeweg" : "Fluss", run.Sea ? 40f : navigable ? 20f : 0f, 0, 0);
                g.WaterWidth = run.Width;
                g.WaterDepth = run.Sea ? 20f : run.Depth;
                g.Navigable = navigable;
                if (run.Sea)
                {
                    g.TargetKind = GatewayTargetKind.Sea;
                    g.TargetName = "Meer (Seeweg)";
                    g.Name = "Seeweg · Küste";
                }
                else
                {
                    bool downstream = Vector2.Dot(run.Outward, world.Terrain.FlowDirection) > 0f;
                    g.Name = $"Fluss {(downstream ? "stromabwärts" : "stromaufwärts")} Richtung {g.TargetName}";
                }
            }

            // Luftweg (ohne Lage): wichtigstes fernes Ziel
            var air = new Gateway
            {
                Id = nextId++, Kind = GatewayKind.Air, Status = GatewayStatus.Planned, HasLocation = false,
                Route = "Luftweg", Name = "Luftweg (Flughafen folgt)", TargetKind = GatewayTargetKind.Hinterland, TargetName = "weltweit",
            };
            gateways.Add(air);

            LastReport = report;
            return report;
        }

        /// <summary>Wird von <see cref="GatewayGenerator.BuildHighway"/> nach dem Bau aufgerufen: zwei Autobahn-Gateways.</summary>
        internal void AddHighway(float angle, Vector2 interchange, Vector2 entryA, Vector2 exitA, Vector2 entryB, Vector2 exitB,
            Vector2 stubA, Vector2 stubB, List<GatewayTarget> targets)
        {
            HighwayAngle = angle;
            Interchange = interchange;
            feederEnds.Clear();
            feederEnds.Add(stubA);
            feederEnds.Add(stubB);
            var u = GatewayGenerator.Dir(angle);
            float capacity = 2 * GatewayRules.LaneCapacityPerHour;
            var roads = world.Roads;
            int entryAId = roads.FindNearestNode(entryA, 1f)?.Id ?? 0, exitAId = roads.FindNearestNode(exitA, 1f)?.Id ?? 0;
            int entryBId = roads.FindNearestNode(entryB, 1f)?.Id ?? 0, exitBId = roads.FindNearestNode(exitB, 1f)?.Id ?? 0;
            string route = $"A {HighwayNumber}";
            AddEnd(GatewayKind.Highway, GatewayStatus.Operational, (entryA + exitA) * 0.5f, u, targets, route, capacity, entryAId, exitAId);
            AddEnd(GatewayKind.Highway, GatewayStatus.Operational, (entryB + exitB) * 0.5f, -u, targets, route, capacity, entryBId, exitBId);
            LinkPartners(gateways.Count - 2, gateways.Count - 1);
        }

        Gateway AddEnd(GatewayKind kind, GatewayStatus status, Vector2 position, Vector2 dir, List<GatewayTarget> targets, string route,
            float capacity, int entry, int exit)
        {
            var target = GatewayGenerator.BestTarget(targets, dir, out float importance);
            var g = new Gateway
            {
                Id = nextId++, Kind = kind, Status = status, Position = position, Direction = dir, Route = route,
                Capacity = capacity, EntryNodeId = entry, ExitNodeId = exit,
                Importance = MathF.Max(0.2f, importance),
            };
            if (target != null)
            {
                g.TargetKind = target.Kind;
                g.TargetName = target.Name;
                g.TargetCountryId = target.CountryId;
                g.TargetRegionId = target.RegionId;
            }
            else
            {
                g.TargetKind = GatewayTargetKind.Hinterland;
                g.TargetName = $"Umland ({GatewayRules.Compass(dir)})";
            }
            g.Name = $"{route} Richtung {g.TargetName}";
            gateways.Add(g);
            return g;
        }

        void LinkPartners(int i, int j)
        {
            gateways[i].PartnerId = gateways[j].Id;
            gateways[j].PartnerId = gateways[i].Id;
        }

        // ------------------------------------------------------------ Abfragen

        /// <summary>Gateway, zu dem ein Straßenknoten gehört (Einfahrt oder Ausfahrt), sonst null.</summary>
        public Gateway OfNode(int nodeId)
        {
            if (nodeId == 0) return null;
            foreach (var g in gateways)
                if (g.EntryNodeId == nodeId || g.ExitNodeId == nodeId) return g;
            return null;
        }

        /// <summary>Befahrbare Straßen-Gateways (Autobahn-Enden).</summary>
        public IEnumerable<Gateway> RoadGateways()
        {
            foreach (var g in gateways) if (g.IsRoad && g.IsOperational) yield return g;
        }

        /// <summary>Ist der Netzteil mit einem befahrbaren Anschluss verbunden (Einfahrt erreichbar)?</summary>
        public bool IsConnected(int component)
        {
            if (component < 0) return false;
            return EntryFor(component, Vector2.Zero) != null;
        }

        /// <summary>Hängt das Gebäude an einem Anschluss (über das Straßennetz)?</summary>
        public bool IsConnected(Building b) => b != null && IsConnected(ComponentOfBuilding(b));

        int ComponentOfBuilding(Building b)
        {
            var comps = world.Citizens?.Components;
            if (comps == null || b == null) return -1;
            int c = comps.OfSegment(b.FrontSegmentId);
            if (c >= 0) return c;
            var seg = world.Roads.FindNearestSegment(b.Entrance, 4f);
            return seg == null ? -1 : comps.OfNode(seg.Start.Id);
        }

        /// <summary>Gibt es überhaupt einen befahrbaren Anschluss (Gateway oder – ohne Gateways – einen Außenanschluss)?</summary>
        public bool HasAnyConnection
        {
            get
            {
                foreach (var _ in RoadGateways()) return true;
                return world.Traffic != null && world.Traffic.ExternalConnections.Count > 0;
            }
        }

        /// <summary>
        /// Hinweis für die HUD, solange die Stadt nicht angeschlossen ist (null = alles gut): Zuzug braucht eine Verbindung
        /// der Wohngebiete mit der Autobahn.
        /// </summary>
        public string ConnectionHint
        {
            get
            {
                if (!world.Settings.RequireGatewayForMoveIn) return null;
                if (!HasAnyConnection) return "Keine Außenanbindung: Straße an den Kartenrand bauen, damit Leute kommen.";
                if (HousingConnected > 0 && HousingUnconnected == 0) return null;
                string where = HasHighway ? $"an die A {HighwayNumber} (Zubringer an der Anschlussstelle)" : "an einen Außenanschluss";
                if (HousingConnected == 0) return $"Stadt {where} anschließen, damit Leute kommen.";
                return $"{HousingUnconnected} Wohngebäude ohne Anschluss – Straßen {where} verbinden.";
            }
        }

        // ------------------------------------------------------------ Hook: Zuzug

        /// <summary>
        /// Hook für <c>CitizenSystem.MoveInGateway</c>: Einfahrt eines befahrbaren Anschlusses im selben Netzteil wie die
        /// Wohnung (bei mehreren nach Wichtigkeit abwechselnd), sonst null (Zuzug blockiert, wird gezählt).
        /// </summary>
        public RoadNode MoveInEntry(Building home, int component)
        {
            var node = EntryFor(component, home?.Entrance ?? Vector2.Zero);
            if (node == null) BlockedMoveInsToday++;
            return node;
        }

        RoadNode EntryFor(int component, Vector2 near)
        {
            if (component < 0) return null;
            var traffic = world.Traffic;
            var comps = world.Citizens?.Components;
            if (traffic == null || comps == null) return null;
            Gateway best = null;
            float bestScore = float.MaxValue;
            bool anyRoad = false;
            foreach (var g in gateways)
            {
                if (!g.IsRoad || !g.IsOperational) continue;
                anyRoad = true;
                var node = world.Roads.GetNode(g.EntryNodeId);
                if (node == null || comps.OfNode(node.Id) != component || !traffic.IsExternalUsable(node, true)) continue;
                // gewichtet abwechselnd: wer gemessen an seiner Wichtigkeit am wenigsten Zuzügler gebracht hat
                float score = (g.TotalMoveIns + 1f) / MathF.Max(0.1f, g.Importance);
                if (score < bestScore - 1e-6f) { bestScore = score; best = g; }
            }
            if (best != null) return world.Roads.GetNode(best.EntryNodeId);
            if (anyRoad) return null;
            // Ohne Gateways (Testwelten): nächster befahrbarer Außenanschluss im selben Netzteil
            RoadNode nearest = null;
            float bestD = float.MaxValue;
            foreach (var n in traffic.ExternalConnections)
            {
                if (comps.OfNode(n.Id) != component || !traffic.IsExternalUsable(n, true)) continue;
                float d = Vector2.Distance(near, n.Position);
                if (d < bestD) { bestD = d; nearest = n; }
            }
            return nearest;
        }

        void OnMovedIn(Citizens.Household h, RoadNode via)
        {
            var g = OfNode(via?.Id ?? 0);
            if (g == null) return;
            g.Today.MoveIns++;
            g.TotalMoveIns++;
        }

        // ------------------------------------------------------------ Hook: Durchgangsverkehr

        /// <summary>
        /// Hook für <c>TrafficSystem.ThroughTrafficWeight</c>: Autobahn-Einfahrten nach Wichtigkeit der Ziele, Ausfahrten
        /// sind kein Startpunkt, keine Wende am selben Gateway; andere Außenanschlüsse (Spielerstraßen) wie bisher 1.
        /// </summary>
        public float ThroughWeight(RoadNode from, RoadNode to)
        {
            if (to == null)
            {
                var g = OfNode(from?.Id ?? 0);
                if (g == null) return 1f;
                if (g.EntryNodeId != from.Id) return 0f; // Ausfahrt: hier fährt niemand los
                return EndWeight(g);
            }
            var gf = OfNode(from?.Id ?? 0);
            var gt = OfNode(to.Id);
            if (gt == null) return 1f;
            if (gt.ExitNodeId != to.Id) return 0f;          // Einfahrt ist kein Ziel
            if (gf != null && gf.Id == gt.Id) return 0f;   // keine Wende am selben Anschluss
            return EndWeight(gt);
        }

        static float EndWeight(Gateway g) =>
            Math.Clamp(GatewayRules.ThroughBase + GatewayRules.ThroughPerImportance * g.Importance, GatewayRules.ThroughBase, GatewayRules.ThroughMax);

        // ------------------------------------------------------------ Hook: Bahnkorridor freihalten

        /// <summary>Liegt der Punkt im freigehaltenen Bahnkorridor (+ halbe Zone)?</summary>
        public bool InRailCorridor(Vector2 p, float margin = 0f)
        {
            if (!HasRailCorridor) return false;
            var ab = RailEnd - RailStart;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return false;
            float t = Math.Clamp(Vector2.Dot(p - RailStart, ab) / len2, 0f, 1f);
            return Vector2.Distance(p, RailStart + ab * t) <= RailHalfWidth + margin;
        }

        // ------------------------------------------------------------ Verdrahtung

        /// <summary>Hooks der anderen Systeme setzen (in <c>CreateDefault</c>, auch beim Laden).</summary>
        internal void Connect()
        {
            var s = world.Settings;
            if (world.Citizens != null)
            {
                if (s.RequireGatewayForMoveIn) world.Citizens.MoveInGateway = MoveInEntry;
                world.Citizens.MovedInVia += OnMovedIn;
            }
            if (world.Traffic != null && s.PrebuiltConnections) world.Traffic.ThroughTrafficWeight = ThroughWeight;
            if (world.Goods != null)
            {
                world.Goods.RequireGatewayForTrade = s.RequireGatewayForTrade;
                if (s.PrebuiltConnections) world.Goods.PreferredGateway = TradeNode;
            }
            if (world.City != null && s.PrebuiltConnections)
            {
                var previous = world.City.LotBlocker;
                world.City.LotBlocker = p => (previous != null && previous(p)) || InRailCorridor(p, ZoneGrid.CellSize * 0.5f);
            }
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            long now = w.Clock.Tick;
            if (now % SimulationClock.TicksPerDay == 0) RollDay();
            CountShipments();
            if (now % TimeModel.TicksPerHour == 0) CountHousing();
        }

        /// <summary>Verkehrsschritt: Fahrzeuge an den Anschlüssen zählen (alle <see cref="GatewayRules.CountEverySteps"/> Schritte).</summary>
        public void Substep(SimulationWorld w)
        {
            if (w.SubstepInTick % GatewayRules.CountEverySteps == 0) CountVehicles();
        }

        void RollDay()
        {
            foreach (var g in gateways) { g.Yesterday.CopyFrom(g.Today); g.Today.Clear(); }
            BlockedMoveInsYesterday = BlockedMoveInsToday;
            BlockedMoveInsToday = 0;
        }

        // Je Gateway: Fahrzeug-Ids, die beim letzten Zählen auf dem Einfahrts-/Ausfahrtsstück standen (gespeichert)
        readonly Dictionary<int, HashSet<int>> seenIn = new Dictionary<int, HashSet<int>>();
        readonly Dictionary<int, HashSet<int>> seenOut = new Dictionary<int, HashSet<int>>();
        readonly HashSet<int> scratch = new HashSet<int>();

        void CountVehicles()
        {
            var traffic = world.Traffic;
            if (traffic == null) return;
            foreach (var g in gateways)
            {
                if (!g.IsRoad || !g.IsOperational) continue;
                g.Today.VehiclesIn += CountOn(g.EntryNodeId, g.Id, seenIn);
                g.Today.VehiclesOut += CountOn(g.ExitNodeId, g.Id, seenOut);
            }
        }

        int CountOn(int nodeId, int gatewayId, Dictionary<int, HashSet<int>> seen)
        {
            var node = world.Roads.GetNode(nodeId);
            if (!seen.TryGetValue(gatewayId, out var set)) seen[gatewayId] = set = new HashSet<int>();
            if (node == null) { set.Clear(); return 0; }
            scratch.Clear();
            int added = 0;
            foreach (int segId in node.SegmentIds)
                foreach (var lane in world.Traffic.Lanes.GetLanes(segId))
                    foreach (var v in lane.Vehicles)
                    {
                        if (!scratch.Add(v.Id)) continue;
                        if (!set.Contains(v.Id)) added++;
                    }
            set.Clear();
            foreach (int id in scratch) set.Add(id);
            return added;
        }

        int lastShipmentId;

        void CountShipments()
        {
            var goods = world.Goods;
            if (goods == null) return;
            var list = goods.Shipments;
            int i = list.Count - 1;
            while (i >= 0 && list[i].Id > lastShipmentId) i--;
            for (int k = i + 1; k < list.Count; k++)
            {
                var s = list[k];
                lastShipmentId = Math.Max(lastShipmentId, s.Id);
                var g = OfNode(s.ConnectionNodeId);
                if (g == null) continue;
                if (s.Kind == ShipmentKind.Import) { g.Today.Imports++; g.Today.ImportValue += s.Value; }
                else if (s.Kind == ShipmentKind.Export) { g.Today.Exports++; g.Today.ExportValue += s.Value; }
            }
        }

        void CountHousing()
        {
            if (!world.Settings.RequireGatewayForMoveIn || world.City == null) { HousingConnected = HousingUnconnected = 0; return; }
            int yes = 0, no = 0;
            var cache = new Dictionary<int, bool>();
            foreach (var b in world.City.Buildings)
            {
                if (b.Households <= 0) continue;
                int c = ComponentOfBuilding(b);
                if (!cache.TryGetValue(c, out bool ok)) cache[c] = ok = IsConnected(c);
                if (ok) yes++; else no++;
            }
            HousingConnected = yes;
            HousingUnconnected = no;
        }
    }
}
