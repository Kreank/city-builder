using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Bahn (Stufe 2 der Außenanbindungen, <c>world.Rail</c>): eigenes <b>Gleisnetz</b> (<see cref="Tracks"/> – ein zweites
    /// <see cref="RoadNetwork"/> mit Gleistypen, nutzt Bézier, Höhen, Profile/Trassierung, Brücken/Tunnel, Zerteilen und den
    /// Spielstand-Code der Straßen), vorgebaute zweigleisige <b>Fernbahn-Hauptstrecke</b> auf dem Bahnkorridor (gehört der
    /// Welt), vom Spieler gebaute Gleise mit strengerer Trassierung (Steigung, Radius, tangentiale Weichen),
    /// <b>Bahnübergänge</b> (Schranke, Straßenverkehr wartet) bzw. kreuzungsfreie Querungen, <b>Personen- und Güterbahnhöfe</b>
    /// (Straßenfront + Bahnsteige am Gleis), <b>Züge</b> (Lok + Wagen) mit Blockabschnitten und Signalen, Fernzüge mit Halt am
    /// eigenen Bahnhof, Güterzüge mit Umschlag, Fahrgäste aus der Verkehrsmittelwahl der Bürger, Zuzug per Bahn und Handel
    /// über den Güterbahnhof. Deterministisch im Verkehrsschritt-Takt (<see cref="ISubstepSystem"/>), speicherbar.
    /// </summary>
    public sealed partial class RailSystem : ISimulationSystem, ISubstepSystem, IUpkeepSource
    {
        public int TickInterval => 1;

        readonly SimulationWorld world;
        readonly RoadNetwork roads;
        readonly TerrainMap terrain;

        /// <summary>Gleisnetz (Knoten + Gleisstücke). Gleisstück = Blockabschnitt; Knoten = Signal/Weiche/Kreuzung.</summary>
        public RoadNetwork Tracks { get; } = new RoadNetwork();

        readonly List<Station> stations = new List<Station>();
        readonly List<Train> trains = new List<Train>();
        int nextStationId = 1, nextTrainId = 1, nextPassengerId = 1;
        double pendingFares;

        /// <summary>Wird bei jeder Änderung an Gleisen, Bahnhöfen oder Zügen erhöht (Darstellung).</summary>
        public int Version { get; private set; }

        public IReadOnlyList<Station> Stations => stations;
        public IReadOnlyList<Train> Trains => trains;

        // --- Statistik (kumuliert / je Tag)
        public int TrainsSpawned { get; private set; }
        public int TrainsCompleted { get; private set; }
        public int TrainsCancelled { get; private set; }
        public int PassengersToday { get; private set; }
        public int PassengersYesterday { get; private set; }
        public int PassengersTotal { get; private set; }
        public int MoveInsByRail { get; internal set; }
        public int GaveUpToday { get; private set; }
        public double FaresTotal { get; private set; }

        public RailSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            roads = world.Roads;
            terrain = world.Terrain;
            Tracks.Ground = terrain;
        }

        // ------------------------------------------------------------ Verdrahtung

        /// <summary>
        /// Hooks setzen (in <c>CreateDefault</c>, auch beim Laden): Gelände unter Gleisen, Grundstücke/Terraforming gesperrt,
        /// Bauwerks-Sperre in beide Richtungen, Prüfung Straße ↔ Gleis, Schranken im Verkehr, Fahrgäste, Zuzug, Handel, Kasse.
        /// </summary>
        internal void Connect()
        {
            Tracks.Budget = world.Economy != null ? new RailBudget(world.Economy) : null;
            Tracks.SegmentAdded += terrain.ConformTo;
            Tracks.SegmentAdded += OnTrackAdded;
            Tracks.SegmentRemoved += _ => MarkDirty();
            Tracks.SegmentReplaced += (_, __) => MarkDirty();
            Tracks.Reloaded += MarkDirty;
            roads.SegmentAdded += _ => crossingsDirty = true;
            roads.SegmentRemoved += _ => crossingsDirty = true;
            roads.Reloaded += () => crossingsDirty = true;

            // Gelände: Gleise haben Vorrang wie Straßen; Terraforming unter Gleisen und Bahnhöfen gesperrt
            var previousRule = terrain.RoadRule;
            terrain.RoadRule = (p, proposed) =>
            {
                float result = TrackRule(p, previousRule != null ? previousRule(p, proposed) : proposed, out bool underTrack);
                // Unter einem Gleis setzt die Gleisregel die Höhe – am Bahnübergang darf sie trotzdem nicht über die Straße
                // ragen: die Straßenregel noch einmal anwenden (sie nimmt dort die niedrigere Fläche).
                if (underTrack && previousRule != null) result = MathF.Min(result, previousRule(p, result));
                return result;
            };
            var previousLocks = terrain.LockQuery;
            terrain.LockQuery = (lo, hi) =>
            {
                var inner = previousLocks?.Invoke(lo, hi);
                var tracks = TerrainLocks.Query(Tracks, null, terrain, lo, hi);
                var near = new List<Station>();
                foreach (var s in stations)
                    if (s.Contains(Vector2.Clamp(s.Position, lo, hi), TerrainLocks.Band + terrain.CellSize + MathF.Max(s.Type.Width, s.Type.Depth))) near.Add(s);
                if (tracks == null && near.Count == 0) return inner;
                return p =>
                {
                    foreach (var s in near) if (s.Contains(p, terrain.CellSize)) return 1f;
                    float a = tracks?.Invoke(p) ?? 0f;
                    float b = inner?.Invoke(p) ?? 0f;
                    return MathF.Max(a, b);
                };
            };

            // Bauwerks-Sperre: Straßen dürfen Bahnhöfe (Gebäude + Bahnsteige) nicht überbauen; Gleise nicht durch Bauwerke
            roads.RegisterStructures(StationBlocksForRoads);
            Tracks.RegisterStructures(() => ForeignStructures());
            Tracks.RegisterStructures(StationBuildingBlocks);
            roads.ExternalCheck = CheckRoadPlan;
            Tracks.ExternalCheck = CheckTrackPlan;

            // Grundstücke: auf Gleisen und Bahnhöfen wächst nichts
            if (world.City != null)
            {
                var previous = world.City.LotBlocker;
                world.City.LotBlocker = p => (previous != null && previous(p)) || TrackAt(p, ZoneGrid.CellSize * 0.5f) || StationAt(p, ZoneGrid.CellSize * 0.5f) != null;
            }
            // Versorger/Dienste/Depots nicht auf Bahnhöfe
            if (world.Utilities != null)
            {
                var prevOverlaps = world.Utilities.OtherStructureOverlaps;
                world.Utilities.OtherStructureOverlaps = (c, f, w, d) => (prevOverlaps != null && prevOverlaps(c, f, w, d)) || StationOverlaps(c, f, w, d, 0);
                var prevAt = world.Utilities.OtherStructureAt;
                world.Utilities.OtherStructureAt = (p, margin) => (prevAt != null && prevAt(p, margin)) || StationAt(p, margin) != null;
            }
            if (world.Services != null)
            {
                var prev = world.Services.OtherStructureOverlaps;
                world.Services.OtherStructureOverlaps = (c, f, w, d) => (prev != null && prev(c, f, w, d)) || StationOverlaps(c, f, w, d, 0);
            }

            // Verkehr: geschlossene Schranke wirkt wie eine Haltelinie
            if (world.Traffic != null) world.Traffic.BarrierAhead = BarrierAhead;
            ConnectCitizens();
            ConnectGateways();
            ConnectGoods();
            if (world.Economy != null) world.Economy.RegisterUpkeep(this);
        }

        void MarkDirty()
        {
            graphDirty = true;
            crossingsDirty = true;
            platformsDirty = true;
            Version++;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            long now = w.Clock.Tick;
            int tod = (int)(now % SimulationClock.TicksPerDay);
            if (tod == 0) Daily();
            EnsurePlatforms();
            ScheduleTrains(tod);
            CheckPassengers();
            if (HasFreightService) CountFreight();
            if (now % 5 == 0 && pendingFares > 0)
            {
                world.Economy?.Earn(BudgetCategory.RailFares, pendingFares);
                FaresTotal += pendingFares;
                pendingFares = 0;
            }
            UpdateGatewayStatus();
        }

        /// <summary>Verkehrsschritt: Züge einsetzen, fahren lassen (Blöcke/Signale, Halte), Schranken schalten.</summary>
        public void Substep(SimulationWorld w)
        {
            if (trains.Count == 0 && crossings.Count == 0 && !crossingsDirty) return;
            EnsureGraph();
            EnsureCrossings();
            double now = Now;
            for (int i = 0; i < trains.Count; i++)
            {
                var t = trains[i];
                if (t.State == TrainState.Waiting) TrySpawn(t, now);
                else StepTrain(t, now);
            }
            if (removedTrains.Count > 0)
            {
                foreach (var t in removedTrains) trains.Remove(t);
                removedTrains.Clear();
                Version++;
            }
            UpdateCrossings(now);
        }

        double Now => world.Traffic?.Time ?? 0.0;

        void Daily()
        {
            PassengersYesterday = PassengersToday;
            PassengersToday = 0;
            GaveUpToday = 0;
            foreach (var s in stations)
            {
                s.BoardedYesterday = s.BoardedToday; s.BoardedToday = 0;
                s.AlightedYesterday = s.AlightedToday; s.AlightedToday = 0;
                s.StopsYesterday = s.StopsToday; s.StopsToday = 0;
                s.LoadsYesterday = s.LoadsToday; s.LoadsToday = 0;
            }
            foreach (var c in crossings) c.ClosuresToday = 0;
        }

        // ------------------------------------------------------------ Kasse

        public string Name => "Bahn";

        /// <summary>Unterhalt je Woche: Bahnhöfe + Spielergleise (die Hauptstrecke trägt die Welt).</summary>
        public double WeeklyUpkeep()
        {
            double s = 0;
            foreach (var st in stations) s += st.Type.UpkeepPerWeek;
            foreach (var seg in Tracks.Segments)
                if (!seg.IsLocked) s += seg.Length * RailRules.TrackUpkeepPerMeterWeek;
            return s;
        }

        /// <summary>Gleisbau bucht unter „Bau Bahn“ statt „Straßenbau“ (RoadBuilder kennt nur die Schnittstelle).</summary>
        sealed class RailBudget : IConstructionBudget
        {
            readonly IConstructionBudget inner;
            public RailBudget(IConstructionBudget inner) { this.inner = inner; }
            public bool CanAfford(double cost) => inner.CanAfford(cost);
            public void Spend(BudgetCategory category, double amount) => inner.Spend(BudgetCategory.RailConstruction, amount);
            public void Refund(double amount) => inner.Refund(amount);
        }

        IConstructionBudget Budget => world.Economy;
        bool Affordable(double cost) => Budget == null || Budget.CanAfford(cost);
    }
}
