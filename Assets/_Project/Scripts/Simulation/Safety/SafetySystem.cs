using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>
    /// <b>Sicherheit: Brände und Kriminalität als echte Ereignisse</b> (<c>world.Safety</c>, läuft jeden Tick nach den
    /// Dienstfahrzeugen).
    /// <list type="bullet">
    /// <item><b>Brände</b> entstehen je Gebäude aus der Brandgefahr (Bauart, Rezept, Zustand, Müll, Stromausfall, Größe,
    /// Brandschutz), wachsen logistisch, greifen auf Nachbarn über, werden von echten Löschfahrzeugen
    /// (<see cref="ServiceVehicleSystem"/>) gelöscht – Löschen beginnt erst bei Ankunft (Stau verzögert), ohne Wasser am
    /// Gebäude nur mit Tankwasser. Folgen: Zustand sinkt, Verletzte (Rettungswagen), Erinnerungen, Schlagzeilen, Bodenwert;
    /// schwerer Schaden → Sanierung (Bewohner ziehen vorübergehend aus), Totalschaden → Abriss (Zonengebäude) bzw.
    /// Brandruine (gesetzte Gebäude, Wiederaufbau gegen Geld).</item>
    /// <item><b>Kriminalität</b> je Gebäude aus Arbeitslosigkeit, Unzufriedenheit, Bildung, Dichte, Bodenwert (Viertel),
    /// gesenkt durch Polizeiabdeckung und Streifenpräsenz (echte Fahrten). Straftaten (Einbruch, Ladendiebstahl,
    /// Vandalismus) nachts häufiger, mit Schaden, Erinnerungen, Anzeige, Einsatz mit echtem Streifenwagen, Aufklärung,
    /// Festnahme → Zelle der Wache (Platzhalter bis zum Gefängnis).</item>
    /// </list>
    /// <b>Standard aus</b> (<see cref="Enabled"/>): Tests und alte Welten bleiben bitgleich; das Spiel schaltet ein.
    /// Zufall nur über stabile Hashes (<see cref="SafetyRules.Roll"/>) – keine gemeinsame Zufallsfolge wird gestört.
    /// </summary>
    public sealed partial class SafetySystem : ISimulationSystem
    {
        public int TickInterval => 1;

        readonly SimulationWorld world;
        readonly CitySystem city;
        readonly ServiceSystem services;
        readonly ServiceVehicleSystem vehicles;
        readonly CitizenSystem citizens;
        readonly SimulationClock clock;

        readonly List<Fire> fires = new List<Fire>();
        readonly Dictionary<int, Fire> fireByBuilding = new Dictionary<int, Fire>();
        readonly List<FireRecord> fireHistory = new List<FireRecord>();
        readonly SortedDictionary<int, FireRepair> repairs = new SortedDictionary<int, FireRepair>();
        readonly List<CrimeIncident> crimes = new List<CrimeIncident>();      // Verlauf (neueste zuerst)
        readonly List<CrimeIncident> openCrimes = new List<CrimeIncident>();  // warten auf Polizei
        readonly SortedDictionary<int, CrimeFactors> crimeRates = new SortedDictionary<int, CrimeFactors>();
        readonly SortedDictionary<int, int> custody = new SortedDictionary<int, int>();
        readonly List<SafetyEvent> events = new List<SafetyEvent>();
        readonly float[] patrol;
        readonly int gridW;
        readonly Vector2 gridMin;
        int nextFireId = 1, nextCrimeId = 1, nextEventSerial = 1, statsDay = -1;
        bool enabled;

        public const int MaxHistory = 40, MaxCrimes = 60, MaxEvents = 40;

        /// <summary>Tests/Balancing (später Schwierigkeitsgrad): Faktor auf die Brand-Entstehung bzw. die Straftaten (gespeichert).</summary>
        public float IgnitionScale = 1f, CrimeScale = 1f;

        public SafetyStats Today { get; } = new SafetyStats();
        public SafetyStats Yesterday { get; } = new SafetyStats();
        public SafetyStats Total { get; } = new SafetyStats();
        /// <summary>Wird bei jeder sichtbaren Änderung erhöht (Darstellung: Flammen, Ruinen, Ansichten).</summary>
        public int Version { get; private set; }

        public SafetySystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            city = world.City;
            services = world.Services;
            vehicles = world.ServiceVehicles;
            citizens = world.Citizens;
            clock = world.Clock;
            gridMin = world.Min;
            gridW = Math.Max(1, (int)MathF.Ceiling(world.Settings.SizeMeters / SafetyRules.CrimeCell));
            patrol = new float[gridW * gridW];

            city.BuildingRemoved += OnBuildingRemoved;
            if (vehicles != null)
            {
                vehicles.ArrivedAtStop += OnUnitArrived;
                vehicles.StopFinished += OnStopFinished;
            }
            if (citizens != null) citizens.CrimeLevel = b => enabled ? CrimeRate(b) : 0f;
            if (world.LandValue != null) world.LandValue.SafetyPenaltyAt = p => enabled ? LandValuePenalty(p) : 0f;
            ApplyHooks();
        }

        /// <summary>Brände und Kriminalität aktiv (Spiel: an; Tests/Standard: aus → wie vorher, Platzhalter-Brandeinsätze bleiben).</summary>
        public bool Enabled
        {
            get => enabled;
            set { enabled = value; ApplyHooks(); }
        }

        void ApplyHooks()
        {
            if (vehicles == null) return;
            vehicles.PlaceholderFireCalls = !enabled;
            vehicles.HoldAtStop = enabled ? HoldTruck : (Func<ServiceUnit, bool>)null;
            vehicles.PatrolWeight = enabled ? PatrolWeight : (Func<Building, float>)null;
            vehicles.PatrolReserve = enabled ? 1 : 0; // ein Streifenwagen je Wache bleibt für Einsätze
        }

        // ------------------------------------------------------------ Abfragen (Inspektor, Ansichten, HUD)

        public IReadOnlyList<Fire> ActiveFires => fires;
        public Fire FireAt(int buildingId) => fireByBuilding.TryGetValue(buildingId, out var f) ? f : null;
        /// <summary>Abgeschlossene Brände, neueste zuerst.</summary>
        public IReadOnlyList<FireRecord> FireHistory => fireHistory;
        /// <summary>Sanierung/Ruine des Gebäudes, null = keine.</summary>
        public FireRepair RepairOf(int buildingId) => repairs.TryGetValue(buildingId, out var r) ? r : null;
        public IEnumerable<FireRepair> Repairs => repairs.Values;
        /// <summary>Letzte Straftaten (neueste zuerst).</summary>
        public IReadOnlyList<CrimeIncident> RecentCrimes => crimes;
        public IReadOnlyList<SafetyEvent> Events => events;
        /// <summary>Festgenommene in den Zellen der Wachen (Platzhalter bis zum Gefängnis).</summary>
        public int InCustody { get { int n = 0; foreach (var v in custody.Values) n += v; return n; } }
        public int CustodyAt(int stationId) => custody.TryGetValue(stationId, out var n) ? n : 0;
        public int CustodyCapacity { get { int n = 0; foreach (var f in services.Facilities) if (f.Kind == ServiceKinds.Police) n += SafetyRules.HoldingCellsPerStation; return n; } }

        /// <summary>Kriminalitätsrate 0..1 (Stand der letzten Stunde, 0 = unbekannt/keine).</summary>
        public float CrimeRate(Building b) => b != null && crimeRates.TryGetValue(b.Id, out var f) ? f.Rate : 0f;
        public CrimeFactors CrimeFactorsOf(Building b) => b != null && crimeRates.TryGetValue(b.Id, out var f) ? f : default;

        /// <summary>Letzte Straftaten an einem Gebäude (neueste zuerst).</summary>
        public IEnumerable<CrimeIncident> CrimesAt(int buildingId)
        {
            foreach (var c in crimes) if (c.BuildingId == buildingId) yield return c;
        }

        /// <summary>Löschfahrzeuge zum Brand unterwegs mit geschätzter Ankunft (Verkehrsminuten-Schätzung, Spielminuten).</summary>
        public float EstimatedArrivalMinutes(Fire f)
        {
            if (f == null) return -1f;
            var b = city.GetBuilding(f.BuildingId);
            if (b == null) return -1f;
            if (vehicles == null || !vehicles.Enabled)
                return f.FallbackArrivalTick < 0 ? -1f : MathF.Max(0f, (f.FallbackArrivalTick - clock.Tick) * TimeModel.GameMinutesPerTick);
            float best = -1f;
            foreach (var u in vehicles.UnitsAt(b.Id, ServiceMission.FireCall))
            {
                if (u.State != ServiceUnitState.Driving) continue;
                var station = services.GetFacility(u.DepotId);
                float secs = services.ResponseSeconds(station, b);
                if (float.IsInfinity(secs)) continue;
                // Schätzung: Fahrzeit nach Tempolimit minus schon gefahrene Zeit (Verkehrssekunden → Spielminuten)
                var v = FindVehicle(u.TripId);
                double driven = v != null ? world.Traffic.Time - v.StartTime : 0.0;
                float left = (float)TimeModel.GameMinutes(Math.Max(0.0, secs - driven));
                if (best < 0f || left < best) best = left;
            }
            return best;
        }

        Traffic.Vehicle FindVehicle(int tripId)
        {
            if (tripId == 0) return null;
            foreach (var v in world.Traffic.Vehicles) if (v.TripId == tripId) return v;
            return null;
        }

        // ------------------------------------------------------------ Takt

        public void Tick(SimulationWorld w)
        {
            long now = clock.Tick;
            if (clock.Day != statsDay)
            {
                if (statsDay >= 0) DailyRollover();
                statsDay = clock.Day;
            }
            if (!enabled) return;
            if (now % TimeModel.TicksPerHour == 0)
            {
                DecayPatrol();
                RecomputeCrime();
                RollIgnitions(now);
                RollCrimes(now);
            }
            UpdateFires(now);
            UpdateCrimeCalls(now);
            UpdateRepairs(now);
        }

        void DailyRollover()
        {
            Today.AddTo(Total);
            Yesterday.CopyFrom(Today);
            Today.Clear();
            // Zellen der Wachen: täglicher Abgang (Gericht/Überstellung) – Platzhalter bis zum Gefängnis
            var ids = new List<int>(custody.Keys);
            foreach (int id in ids)
            {
                int n = custody[id];
                int leave = (int)MathF.Ceiling(n * SafetyRules.CustodyReleasePerDay);
                n -= leave;
                if (n <= 0 || services.GetFacility(id) == null) custody.Remove(id); else custody[id] = n;
            }
            if (enabled) CrimeWaveHeadline();
        }

        // ------------------------------------------------------------ Ereignisse

        void Post(bool warning, string title, string text, int buildingId, string key)
        {
            events.Insert(0, new SafetyEvent
            {
                Serial = nextEventSerial++, Tick = clock.Tick, Warning = warning, Title = title, Text = text, BuildingId = buildingId, Key = key,
            });
            if (events.Count > MaxEvents) events.RemoveAt(events.Count - 1);
        }

        string StreetName(Building b) => b != null && b.FrontSegmentId > 0 ? Citizens.Content.PlaceNames.Street(b.FrontSegmentId) : "einer Seitenstraße";

        static string BuildingName(Building b) => b == null ? "Gebäude" : HouseCatalog.NameOf(b);

        void Headline(HeadlineTopic topic, string key, params (string key, string value)[] values) =>
            world.Media?.Report(topic, key, values);

        void OnBuildingRemoved(Building b)
        {
            if (fireByBuilding.TryGetValue(b.Id, out var f)) EndFire(f, b, FireOutcome.Removed, notify: false);
            if (repairs.Remove(b.Id)) Version++;
            crimeRates.Remove(b.Id);
        }

        void OnUnitArrived(ServiceUnit u)
        {
            if (!enabled) return;
            if (u.Mission == ServiceMission.FireCall && fireByBuilding.TryGetValue(u.TargetBuildingId, out var f) && f.FirstArrivalTick < 0)
            {
                f.FirstArrivalTick = clock.Tick;
                f.FirstArrivalTime = world.Traffic.Time;
            }
            if (u.Mission == ServiceMission.CrimeCall)
                foreach (var c in openCrimes)
                    if (c.UnitId == u.Id && c.ArrivalTick < 0) { c.ArrivalTick = clock.Tick; c.ArrivalTime = world.Traffic.Time; }
        }

        void OnStopFinished(ServiceUnit u)
        {
            if (!enabled) return;
            var b = city.GetBuilding(u.TargetBuildingId);
            if (b == null) return;
            if (u.Mission == ServiceMission.Patrol) AddPatrol(b.Position, 1f);
            else if (u.Mission == ServiceMission.CrimeCall)
            {
                AddPatrol(b.Position, 0.5f);
                foreach (var c in openCrimes)
                    if (c.UnitId == u.Id && !c.Closed)
                    {
                        if (c.ArrivalTick < 0) { c.ArrivalTick = clock.Tick; c.ArrivalTime = world.Traffic.Time; }
                        Resolve(c, b, onScene: true);
                    }
                openCrimes.RemoveAll(c => c.Closed);
            }
        }

        // ------------------------------------------------------------ Bodenwert

        /// <summary>Abzug am Punkt 0..1: frische Brände in der Nähe (klingen in Tagen ab) + Kriminalität der Nachbarschaft.</summary>
        public float LandValuePenalty(Vector2 p)
        {
            float scar = 0f;
            long now = clock.Tick;
            foreach (var r in fireHistory)
            {
                float days = (now - r.EndTick) / (float)TimeModel.TicksPerDay;
                if (days > SafetyRules.ScarMaxAgeDays || r.Outcome == FireOutcome.SelfExtinguished) continue;
                float d = Vector2.Distance(p, r.Position);
                if (d >= SafetyRules.ScarRadius) continue;
                scar += (1f - d / SafetyRules.ScarRadius) * MathF.Exp(-days / SafetyRules.ScarDays) * (0.4f + r.Damage);
            }
            foreach (var f in fires)
            {
                float d = Vector2.Distance(p, f.Position);
                if (d < SafetyRules.ScarRadius) scar += (1f - d / SafetyRules.ScarRadius) * (0.4f + f.Damage);
            }
            return Math.Clamp(scar + CrimeNear(p), 0f, 1f);
        }

        float CrimeNear(Vector2 p)
        {
            var b = city.BuildingAt(p);
            if (b != null) return CrimeRate(b);
            return 0f;
        }
    }
}
