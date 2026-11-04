using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Flughafen (Stufe 4 der Außenanbindungen, <c>world.Aviation</c>): Regionalflugplatz oder Verkehrsflughafen als Anlage aus
    /// Start-/Landebahn, Streifen, Parallelrollweg, Vorfeld mit Abstellpositionen, Terminal (Landseite zur Straße), Tower und
    /// Hangar; Platzregeln (eben/einebnen mit Kosten, nichts auf der Bahn) und <b>Hindernisfreiheit</b> der Einflugschneisen
    /// (vereinfachte ICAO-Anflugflächen + innere Horizontalfläche: bestehende Hindernisse senken die Kapazität oder sperren ein
    /// Bahnende, neue Hochbauten dort sind ungültig). In Betrieb wird der Luft-Gateway befahrbar (Lage am Flughafen, Ziele =
    /// Hauptstädte weltweit mit Großkreis-Entfernung und Flugdauer). Flugplan je Stunde nach Kapazität und Nachfrage, sichtbare
    /// Flugzeuge mit Anflug, Landung, Rollen, Abfertigung, Start (deterministisch im Verkehrsschritt, gespeichert), Fluggäste
    /// (Touristen als Besucher mit Ausgaben, Geschäftsreisende → Büro-Nachfrage, seltener Zuzug), Luftfracht für hochwertige
    /// Güter, Fluglärm entlang der Schneisen (Lärm-Raster), Nachtflugverbot, Gebühren.
    /// </summary>
    public sealed partial class AviationSystem : ISimulationSystem, ISubstepSystem, IUpkeepSource
    {
        public int TickInterval => 1;

        readonly SimulationWorld world;
        readonly RoadNetwork roads;
        readonly TerrainMap terrain;

        readonly List<Airport> airports = new List<Airport>();
        readonly List<Aircraft> aircraft = new List<Aircraft>();
        readonly List<Aircraft> removedAircraft = new List<Aircraft>();
        readonly List<Rotation> rotations = new List<Rotation>();
        int nextAirportId = 1, nextAircraftId = 1, nextRotationId = 1;
        double pendingFees, pendingTourism;

        /// <summary>Nachtflugverbot 22–6 Uhr (Spieler-Schalter): weniger Lärm, weniger Kapazität.</summary>
        public bool NightBan { get; private set; }

        /// <summary>Änderungen an Flughäfen (Darstellung baut neu) bzw. an Flugzeugen/Flugplan.</summary>
        public int LayoutVersion { get; private set; }
        public int Version { get; private set; }

        public IReadOnlyList<Airport> Airports => airports;
        public IReadOnlyList<Aircraft> AllAircraft => aircraft;
        public IReadOnlyList<Rotation> Rotations => rotations;

        // --- Statistik (gespeichert)
        public double FeesTotal { get; private set; }
        public double TourismTotal { get; private set; }
        public double VisitorShopSpendYesterday { get; private set; }
        public double VisitorLeisureSpendYesterday { get; private set; }
        public int ComplaintsYesterday { get; private set; }
        public int AffectedResidentsYesterday { get; private set; }

        /// <summary>Hauptwindrichtung (aus der Breite der Startregion, eigene Annahme – s. <see cref="AviationRules.PrevailingWindFrom"/>).</summary>
        public Vector2 PrevailingWind { get; }
        public float Latitude { get; }

        public AviationSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            roads = world.Roads;
            terrain = world.Terrain;
            Latitude = world.StartRegion?.Latitude ?? world.Climate?.Parameters.Latitude ?? 50f;
            PrevailingWind = world.Climate != null
                ? AviationRules.PrevailingFromClimate(world.Climate)   // Windmodell aus Umwelt (Etappe 6)
                : AviationRules.PrevailingWindFrom(Latitude, world.Settings.TerrainSeed + (world.StartRegion?.Id ?? 0) * 131);
            var env = world.Environment.Noise;
            NoiseLayer = new GridLayer("aircraft_noise", env.Origin, env.Width, env.Height, env.CellSize);
            BuildDestinations();
        }

        // ------------------------------------------------------------ Verdrahtung

        /// <summary>
        /// Hooks setzen (in <c>CreateDefault</c> nach Hafen/Bahn, auch beim Laden): Bauwerks-Sperre (Straßen, Gleise), Grundstücke,
        /// Gelände, Dienste/Versorger/Häuser in der Einflugschneise, Zuzug, Handel/Luftfracht, Nachfrage, Lärm-Erinnerung, Kasse.
        /// </summary>
        internal void Connect()
        {
            roads.RegisterStructures(AirportBlocks);
            world.Rail?.Tracks.RegisterStructures(AirportBlocks);

            var previousLocks = terrain.LockQuery;
            terrain.LockQuery = (lo, hi) =>
            {
                var inner = previousLocks?.Invoke(lo, hi);
                if (airports.Count == 0) return inner;
                return p =>
                {
                    foreach (var a in airports) if (a.Contains(p, terrain.CellSize)) return 1f;
                    return inner?.Invoke(p) ?? 0f;
                };
            };

            var city = world.City;
            if (city != null)
            {
                var previous = city.LotBlocker;
                city.LotBlocker = p => (previous != null && previous(p)) || BlocksLot(p);
                city.PlacementCheck = HousePlacementNote;          // gesetzte Gebäude in der Einflugschneise
                var previousDemand = city.DemandModifier;
                city.DemandModifier = z => (previousDemand?.Invoke(z) ?? 1f) * DemandBoost(z);
            }
            if (world.Utilities != null)
            {
                var prevOverlaps = world.Utilities.OtherStructureOverlaps;
                world.Utilities.OtherStructureOverlaps = (c, f, w, d) => (prevOverlaps != null && prevOverlaps(c, f, w, d)) || AirportOverlaps(c, f, w, d, 0);
                var prevAt = world.Utilities.OtherStructureAt;
                world.Utilities.OtherStructureAt = (p, margin) => (prevAt != null && prevAt(p, margin)) || AirportAt(p, margin) != null;
                world.Utilities.ObstacleCheck = (id, p, half) => ObstacleNote(p, half, ObstacleHeights.Utility(id), UtilityName(id));
            }
            if (world.Services != null)
            {
                var prev = world.Services.OtherStructureOverlaps;
                world.Services.OtherStructureOverlaps = (c, f, w, d) => (prev != null && prev(c, f, w, d)) || AirportOverlaps(c, f, w, d, 0);
                world.Services.ObstacleCheck = (id, p, half) => ObstacleNote(p, half, ServiceHeight(id), ServiceName(id));
            }
            if (world.Citizens != null) world.Citizens.AircraftNoiseAt = AircraftNoiseAt;
            ConnectMoveIn();
            ConnectGoods();
            ConnectGateways();
            if (world.Economy != null) world.Economy.RegisterUpkeep(this);
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            long now = w.Clock.Tick;
            int tod = (int)(now % SimulationClock.TicksPerDay);
            if (airports.Count == 0)
            {
                if (tod == 0) Daily(w.Clock.Day);
                UpdateGatewayStatus();
                return;
            }
            if (tod == 0) Daily(w.Clock.Day);
            EnsureAccess();
            EnsureObstacles(false);
            if (tod % TimeModel.TicksPerHour == 0) Hourly(now, tod / TimeModel.TicksPerHour);
            ProcessStatistical(Now);
            CountFreight();
            if (now % 5 == 0)
            {
                if (pendingFees > 0) { world.Economy?.Earn(BudgetCategory.AirportFees, pendingFees); FeesTotal += pendingFees; pendingFees = 0; }
                if (pendingTourism > 0) { world.Economy?.Earn(BudgetCategory.TourismIncome, pendingTourism); TourismTotal += pendingTourism; pendingTourism = 0; }
            }
            UpdateGatewayStatus();
        }

        /// <summary>Verkehrsschritt: sichtbare Flugzeuge fliegen, rollen, warten, starten.</summary>
        public void Substep(SimulationWorld w)
        {
            if (aircraft.Count == 0) return;
            double now = Now;
            float dt = TimeModel.TrafficStepSeconds;
            for (int i = 0; i < aircraft.Count; i++) StepAircraft(aircraft[i], now, dt);
            if (removedAircraft.Count > 0)
            {
                foreach (var a in removedAircraft) aircraft.Remove(a);
                removedAircraft.Clear();
                Version++;
            }
        }

        double Now => world.Traffic?.Time ?? world.Clock.Tick * (double)TimeModel.TrafficSecondsPerTick;

        void Daily(int day)
        {
            foreach (var a in airports)
            {
                a.ArrivalsYesterday = a.ArrivalsToday; a.ArrivalsToday = 0;
                a.DeparturesYesterday = a.DeparturesToday; a.DeparturesToday = 0;
                a.PaxInYesterday = a.PaxInToday; a.PaxInToday = 0;
                a.PaxOutYesterday = a.PaxOutToday; a.PaxOutToday = 0;
                a.TouristsYesterday = a.TouristsToday; a.TouristsToday = 0;
                a.BusinessYesterday = a.BusinessToday; a.BusinessToday = 0;
                a.LoadsYesterday = a.LoadsToday; a.LoadsToday = 0;
                ChooseDirections(a, day);
            }
            VisitorsDaily();
            CountComplaints();
            Version++;
        }

        // ------------------------------------------------------------ Spieler-Aktionen

        /// <summary>Nachtflugverbot ein/aus (wirkt ab der nächsten Stunde: keine Bewegungen 22–6 Uhr).</summary>
        public void SetNightBan(bool on)
        {
            if (NightBan == on) return;
            NightBan = on;
            Version++;
        }

        // ------------------------------------------------------------ Kasse

        public string Name => "Flughafen";

        public double WeeklyUpkeep()
        {
            double s = 0;
            foreach (var a in airports) s += a.Type.UpkeepPerWeek + (a.Level - 1) * a.Type.UpkeepPerLevel;
            return s;
        }

        IConstructionBudget Budget => world.Economy;
        bool Affordable(double cost) => Budget == null || Budget.CanAfford(cost);

        // ------------------------------------------------------------ Abfragen

        public Airport GetAirport(int id) { foreach (var a in airports) if (a.Id == id) return a; return null; }
        public Aircraft GetAircraft(int id) { foreach (var a in aircraft) if (a.Id == id) return a; return null; }
        public Rotation GetRotation(int id) { foreach (var r in rotations) if (r.Id == id) return r; return null; }

        public Airport AirportAt(Vector2 p, float margin = 0f)
        {
            foreach (var a in airports) if (a.Contains(p, margin)) return a;
            return null;
        }

        bool AirportOverlaps(Vector2 c, Vector2 f, float w, float d, int exceptId)
        {
            foreach (var a in airports)
            {
                if (a.Id == exceptId) continue;
                foreach (var s in a.Layout.Areas)
                    if (Services.ServiceGeometry.RectsOverlap(c, f, w, d, s.Center, s.Front, s.Width, s.Depth)) return true;
            }
            return false;
        }

        const string AirportBlockKind = "airport";

        IEnumerable<StructureBlock> AirportBlocks()
        {
            foreach (var a in airports)
                foreach (var area in a.Layout.Areas) yield return new StructureBlock(AirportBlockKind, a.Id, a.Name, area, a.Height);
        }

        /// <summary>Hinweis für HUD/Fenster (null = alles gut): Flughafen nicht in Betrieb oder Einflugschneise gesperrt.</summary>
        public string Warning
        {
            get
            {
                foreach (var a in airports)
                {
                    if (!a.HasAccess) return $"{a.Name}: keine Straße vor dem Terminal (Landseite) – außer Betrieb";
                    if (a.Blocked) return $"{a.Name}: beide Einflugschneisen durch Hindernisse gesperrt – kein Flugbetrieb";
                    if (a.CorridorA == CorridorState.Closed || a.CorridorB == CorridorState.Closed)
                        return $"{a.Name}: eine Einflugschneise gesperrt ({FirstCoreObstacle(a)}) – nur Gegenrichtungsbetrieb, Kapazität {MovementCapacity(a) / a.Type.MovementsPerHour * 100f:0} %";
                }
                return null;
            }
        }

        string FirstCoreObstacle(Airport a)
        {
            foreach (var o in a.Obstacles) if (o.Core) return o.Name;
            return "Hindernis";
        }

        /// <summary>Kapazität: Flugbewegungen je Stunde (Bahn × Hindernisse × Gegenrichtung).</summary>
        public float MovementCapacity(Airport a)
        {
            if (a == null || !a.InService) return 0f;
            float f = a.Type.MovementsPerHour * a.ObstacleFactor;
            if (a.CounterFlow) f *= AviationRules.CounterFlowFactor;
            return f;
        }

        public static bool IsNightHour(int hour) => hour >= AviationRules.NightStartHour || hour < AviationRules.NightEndHour;
    }
}
