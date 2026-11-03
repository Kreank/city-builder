using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Wasserstraße &amp; Hafen (Stufe 3 der Außenanbindungen, <c>world.Shipping</c>): <b>Fahrrinnen</b> auf Fluss/Küste aus den
    /// Wasser-Gateways (Navigationsraster aus dem natürlichen Gelände: Tiefe, Breite), Schiffsklassen mit Tiefgang und
    /// <b>Masthöhe</b>, Straßen-/Gleisbrücken mit lichter Höhe gegen die Fahrrinne (zu niedrig → Rinne gesperrt),
    /// <b>Klappbrücken</b> (öffnen für Schiffe, Schranke rot, Verkehr staut), <b>Durchgangsschiffe</b> auf dem Fluss (auch ohne
    /// Hafen), <b>Güterhafen</b> am Ufer (Kai an tiefem Wasser, Straßenfront) mit Liegeplätzen, Anläufen und Umschlag, Seehandel
    /// und Import/Export der Firmen über den Hafen (billige Fracht). Deterministisch im Verkehrsschritt-Takt, speicherbar.
    /// Ohne <see cref="WorldSettings.PrebuiltConnections"/> (Tests/Standardwelt) ist alles aus.
    /// </summary>
    public sealed partial class ShippingSystem : ISimulationSystem, ISubstepSystem, IUpkeepSource
    {
        public int TickInterval => 1;

        readonly SimulationWorld world;
        readonly RoadNetwork roads;
        readonly TerrainMap terrain;

        /// <summary>Navigationsraster (null = keine Wasserstraßen: ohne Außenanbindung oder ohne Wasser).</summary>
        public NavGrid Grid { get; }
        public bool Active => Grid != null;

        readonly List<Harbor> harbors = new List<Harbor>();
        readonly List<Ship> ships = new List<Ship>();
        readonly List<Ship> removedShips = new List<Ship>();
        int nextHarborId = 1, nextShipId = 1;
        double pendingFees;

        /// <summary>Wird bei jeder Änderung an Häfen, Fahrrinnen, Klappbrücken oder Schiffen erhöht (Darstellung).</summary>
        public int Version { get; private set; }

        public IReadOnlyList<Harbor> Harbors => harbors;
        public IReadOnlyList<Ship> Ships => ships;

        // --- Statistik
        public int ShipsSpawned { get; private set; }
        public int ShipsCompleted { get; private set; }
        public int ShipsCancelled { get; private set; }
        public int ThroughToday { get; private set; }
        public int ThroughYesterday { get; private set; }
        /// <summary>Schiffe, die heute nicht kommen konnten, weil eine feste Brücke zu niedrig ist (je Klasse-Id).</summary>
        readonly Dictionary<string, int> blockedToday = new Dictionary<string, int>();
        readonly Dictionary<string, int> blockedYesterday = new Dictionary<string, int>();
        public double FeesTotal { get; private set; }

        public ShippingSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            roads = world.Roads;
            terrain = world.Terrain;
            if (!world.Settings.PrebuiltConnections) return;
            // Raster + Durchgangsrinnen aus dem frisch erzeugten Gelände (vor jedem Bau – beim Laden identisch)
            var grid = new NavGrid(terrain, world.Min, world.Settings.SizeMeters, ShippingRules.GridCell);
            bool any = false;
            for (int i = 0; i < grid.Resolution * grid.Resolution && !any; i++) any = grid.IsWater(i);
            if (!any) return;
            Grid = grid;
            BuildTrunks();
        }

        // ------------------------------------------------------------ Verdrahtung

        /// <summary>
        /// Hooks setzen (in <c>CreateDefault</c> nach der Bahn, auch beim Laden): Mindesthöhe über der Fahrrinne für die
        /// Trassierung, Prüfung der Klappbrücken, Schranken im Verkehr, Sperren (Gelände, Grundstücke, Bauwerke), Handel, Kasse.
        /// </summary>
        internal void Connect()
        {
            if (!Active) return;
            terrain.NavigationClearance = NavigationFloor;
            roads.SegmentAdded += _ => crossingsDirty = true;
            roads.SegmentRemoved += _ => crossingsDirty = true;
            roads.SegmentReplaced += (_, __) => crossingsDirty = true;
            roads.Reloaded += () => { crossingsDirty = true; accessDirty = true; };
            var rail = world.Rail;
            if (rail != null)
            {
                rail.Tracks.SegmentAdded += _ => crossingsDirty = true;
                rail.Tracks.SegmentRemoved += _ => crossingsDirty = true;
                rail.Tracks.Reloaded += () => crossingsDirty = true;
            }

            // Straßenplan: Klappbrücke muss die Fahrrinne queren (verkettet hinter der Bahn-Prüfung)
            var previousCheck = roads.ExternalCheck;
            roads.ExternalCheck = plan =>
            {
                var e = previousCheck?.Invoke(plan) ?? RoadBuildError.None;
                return e != RoadBuildError.None ? e : CheckRoadPlan(plan);
            };

            // Gelände: Fahrrinne und Hafengelände nicht terraformen
            var previousLocks = terrain.LockQuery;
            terrain.LockQuery = (lo, hi) =>
            {
                var inner = previousLocks?.Invoke(lo, hi);
                var near = new List<Harbor>();
                foreach (var h in harbors)
                    if (h.Contains(Vector2.Clamp(h.Position, lo, hi), TerrainLocks.Band + terrain.CellSize + MathF.Max(h.Type.Width, h.Type.Depth))) near.Add(h);
                return p =>
                {
                    foreach (var h in near) if (h.Contains(p, terrain.CellSize)) return 1f;
                    if (InChannel(p)) return 1f;
                    return inner?.Invoke(p) ?? 0f;
                };
            };

            // Bauwerks-Sperre: Straßen und Gleise nicht durch den Hafen
            roads.RegisterStructures(HarborBlocks);
            rail?.Tracks.RegisterStructures(HarborBlocks);
            if (world.City != null)
            {
                var previous = world.City.LotBlocker;
                world.City.LotBlocker = p => (previous != null && previous(p)) || HarborAt(p, ZoneGrid.CellSize * 0.5f) != null;
            }
            if (world.Utilities != null)
            {
                var prevOverlaps = world.Utilities.OtherStructureOverlaps;
                world.Utilities.OtherStructureOverlaps = (c, f, w, d) => (prevOverlaps != null && prevOverlaps(c, f, w, d)) || HarborOverlaps(c, f, w, d, 0);
                var prevAt = world.Utilities.OtherStructureAt;
                world.Utilities.OtherStructureAt = (p, margin) => (prevAt != null && prevAt(p, margin)) || HarborAt(p, margin) != null;
            }
            if (world.Services != null)
            {
                var prev = world.Services.OtherStructureOverlaps;
                world.Services.OtherStructureOverlaps = (c, f, w, d) => (prev != null && prev(c, f, w, d)) || HarborOverlaps(c, f, w, d, 0);
            }

            // Verkehr: geschlossene Klappbrücke wirkt wie eine Haltelinie (verkettet mit den Bahnübergängen)
            if (world.Traffic != null)
            {
                var previousBarrier = world.Traffic.BarrierAhead;
                world.Traffic.BarrierAhead = (lane, s) =>
                {
                    float a = previousBarrier != null ? previousBarrier(lane, s) : -1f;
                    float b = BarrierAhead(lane, s);
                    if (a < 0f) return b;
                    if (b < 0f) return a;
                    return MathF.Min(a, b);
                };
            }
            ConnectGoods();
            ConnectGateways();
            if (world.Economy != null) world.Economy.RegisterUpkeep(this);
        }

        void MarkDirty()
        {
            crossingsDirty = true;
            accessDirty = true;
            Version++;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            if (!Active) return;
            long now = w.Clock.Tick;
            int tod = (int)(now % SimulationClock.TicksPerDay);
            if (tod == 0) Daily();
            EnsureLinks();
            EnsureCrossings();
            EnsureAccess();
            if (tod % TimeModel.TicksPerHour == 0) Schedule(now, tod / TimeModel.TicksPerHour);
            ExtraCalls(now);
            CountFreight();
            if (now % 5 == 0 && pendingFees > 0)
            {
                world.Economy?.Earn(BudgetCategory.HarborFees, pendingFees);
                FeesTotal += pendingFees;
                pendingFees = 0;
            }
            UpdateGatewayStatus();
        }

        /// <summary>Verkehrsschritt: Schiffe einsetzen und fahren lassen, Klappbrücken schalten.</summary>
        public void Substep(SimulationWorld w)
        {
            if (!Active) return;
            if (ships.Count == 0 && drawbridges.Count == 0 && !crossingsDirty) return;
            EnsureCrossings();
            double now = Now;
            float dt = TimeModel.TrafficStepSeconds;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s.State == ShipState.Waiting) TrySpawn(s, now);
                else StepShip(s, now, dt);
            }
            if (removedShips.Count > 0)
            {
                foreach (var s in removedShips) ships.Remove(s);
                removedShips.Clear();
                Version++;
            }
            UpdateDrawbridges(now, dt);
        }

        double Now => world.Traffic?.Time ?? 0.0;

        void Daily()
        {
            ThroughYesterday = ThroughToday;
            ThroughToday = 0;
            blockedYesterday.Clear();
            foreach (var kv in blockedToday) blockedYesterday[kv.Key] = kv.Value;
            blockedToday.Clear();
            foreach (var h in harbors)
            {
                h.CallsYesterday = h.CallsToday; h.CallsToday = 0;
                h.LoadsYesterday = h.LoadsToday; h.LoadsToday = 0;
            }
            foreach (var d in drawbridges)
            {
                d.OpeningsYesterday = d.OpeningsToday; d.OpeningsToday = 0;
                d.ClosedSecondsToday = 0;
            }
        }

        /// <summary>Schiffe einer Klasse, die gestern wegen einer zu niedrigen festen Brücke nicht kamen.</summary>
        public int BlockedYesterday(ShipClass c) => c != null && blockedYesterday.TryGetValue(c.Id, out int n) ? n : 0;
        public int BlockedToday(ShipClass c) => c != null && blockedToday.TryGetValue(c.Id, out int n) ? n : 0;

        void CountBlocked(ShipClass c)
        {
            blockedToday.TryGetValue(c.Id, out int n);
            blockedToday[c.Id] = n + 1;
        }

        // ------------------------------------------------------------ Kasse

        public string Name => "Hafen & Klappbrücken";

        public double WeeklyUpkeep()
        {
            double s = 0;
            foreach (var h in harbors) s += h.Type.UpkeepPerWeek;
            s += drawbridges.Count * ShippingRules.DrawbridgeUpkeep;
            return s;
        }

        IConstructionBudget Budget => world.Economy;
        bool Affordable(double cost) => Budget == null || Budget.CanAfford(cost);

        // ------------------------------------------------------------ Abfragen

        public Harbor GetHarbor(int id) { foreach (var h in harbors) if (h.Id == id) return h; return null; }
        public Ship GetShip(int id) { foreach (var s in ships) if (s.Id == id) return s; return null; }

        public Harbor HarborAt(Vector2 p, float margin = 0f)
        {
            foreach (var h in harbors) if (h.Contains(p, margin)) return h;
            return null;
        }

        bool HarborOverlaps(Vector2 c, Vector2 f, float w, float d, int exceptId)
        {
            foreach (var h in harbors)
            {
                if (h.Id == exceptId) continue;
                if (Services.ServiceGeometry.RectsOverlap(c, f, w, d, h.Position, h.Front, h.Type.Width, h.Type.Depth)) return true;
            }
            return false;
        }

        const string HarborBlockKind = "harbor";

        IEnumerable<StructureBlock> HarborBlocks()
        {
            foreach (var h in harbors) yield return new StructureBlock(HarborBlockKind, h.Id, h.Type.DisplayName, h.Footprint, h.Height);
        }

        /// <summary>Hinweis für HUD/Fenster: Durchgangs-Fahrrinne gesperrt (feste Brücke zu niedrig), null = frei.</summary>
        public string FairwayWarning
        {
            get
            {
                if (!Active) return null;
                foreach (var w in waterways)
                {
                    if (w.Kind != WaterwayKind.Through || !w.DeepEnough(ShipCatalog.Barge)) continue;
                    var x = w.FirstBlocking(ShipCatalog.Barge);
                    if (x != null) return $"Fahrrinne gesperrt: {CrossingName(x)} hat nur {x.Clearance:0.0} m lichte Höhe (Binnenschiffe brauchen {ShipCatalog.Barge.RequiredClearance:0.0} m) – höher bauen oder Klappbrücke.";
                }
                foreach (var h in harbors)
                    if (h.HasAccess && !h.Reachable && h.BlockedReason != null) return $"{h.Name}: {h.BlockedReason}";
                return null;
            }
        }

        public string CrossingName(WaterwayCrossing x)
        {
            if (x == null) return "";
            if (x.Rail) return "Eisenbahnbrücke";
            var seg = roads.GetSegment(x.SegmentId);
            if (seg == null) return "Brücke";
            if (x.IsDrawbridge) return "Klappbrücke";
            return seg.IsLocked ? $"Brücke ({seg.Type.DisplayName}, Welt)" : $"Straßenbrücke ({seg.Type.DisplayName})";
        }
    }
}
