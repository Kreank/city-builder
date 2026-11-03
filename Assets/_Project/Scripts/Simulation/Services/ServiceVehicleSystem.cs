using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// <b>Dienstfahrzeuge (Grundstufe, 2026-11-03):</b> Dienste schicken echte Fahrzeuge über das <see cref="TrafficSystem"/>
    /// (eigene Fahrt-Id-Spanne <see cref="TripIdRanges"/>, höchstens <see cref="ServiceVehicleRules.MaxTrafficShare"/> der Fahrzeuge).
    /// <list type="bullet">
    /// <item><b>Müllwagen</b> (Deponie, Verbrennungsanlage): stündlich Touren zu den Gebäuden mit dem meisten Müll im
    /// Einzugsgebiet (bis 6 Halte, 10 t), laden am Halt (<see cref="ServiceSystem.CollectGarbage"/>), entladen an der Deponie.
    /// Solange Müllwagen fahren, holt die pauschale Abfuhr nur noch <see cref="ServiceVehicleRules.GarbageBaseShareWithTrucks"/> –
    /// der Rest hängt an der Ankunft der Wagen (Stau → Müll bleibt liegen).</item>
    /// <item><b>Streifenwagen</b> (Polizeiwache): Runden zu zufälligen Gebäuden im Einzugsgebiet (sichtbare Präsenz; ein
    /// Kriminalitätsmodell gibt es noch nicht, die Streife ändert keine Abdeckung).</item>
    /// <item><b>Löschfahrzeug</b> (Feuerwache): <b>Platzhalter-Einsätze</b> – es gibt noch keine Brände; selten (Brandrisiko ×
    /// <see cref="ServiceVehicleRules.FireCallsPerRiskDay"/>) rückt ein Wagen zu einem Gebäude aus, ohne Schaden.</item>
    /// <item><b>Rettungswagen</b> (Klinik, Krankenhaus): echtes Ereignis – erkrankt ein Bürger schwer (ab 65 immer, sonst 30 %),
    /// fährt ein Wagen der zuständigen Klinik zu seiner Wohnung und zurück.</item>
    /// </list>
    /// Abfahrt aus der Zufahrt des Dienstgebäudes, Ziel = Zufahrt des Gebäudes; am Halt steht das Fahrzeug (die Darstellung
    /// zeichnet es geparkt, <see cref="ParkedUnits"/>), danach fährt es zum nächsten Halt bzw. zurück. Abgebrochene Fahrten
    /// (Stau, Straße weg) beenden die Tour. Standard aus (<see cref="Enabled"/>): Tests und alte Welten bleiben bitgleich;
    /// das Spiel schaltet es ein (<c>SimulationRunner</c>). Alles in Id-Reihenfolge, Zufall nur über <see cref="SimRandom"/>.
    /// </summary>
    public sealed partial class ServiceVehicleSystem : ISimulationSystem, ISubstepSystem, Economy.IUpkeepSource
    {
        public int TickInterval => 1;

        readonly ServiceSystem services;
        readonly CitySystem city;
        readonly TrafficSystem traffic;
        readonly SimulationClock clock;
        readonly List<ServiceUnit> units = new List<ServiceUnit>();
        readonly Dictionary<int, ServiceUnit> byTrip = new Dictionary<int, ServiceUnit>();
        readonly List<(int citizen, int home)> rescueQueue = new List<(int, int)>();
        readonly SimRandom rng;
        int nextUnitId = 1, nextTripId = TripIdRanges.ServiceFirst;
        int statsDay = -1;

        /// <summary>Dienstfahrzeuge aktiv (Spiel: an; Tests/Standard: aus → keine Fahrten, Müllabfuhr wie bisher).</summary>
        public bool Enabled { get; set; }
        public ServiceVehicleStats Today { get; } = new ServiceVehicleStats();
        public ServiceVehicleStats Yesterday { get; } = new ServiceVehicleStats();
        /// <summary>Alle Fahrzeuge (nach Id).</summary>
        public IReadOnlyList<ServiceUnit> Units => units;
        /// <summary>Fahrzeuge, die gerade im Verkehr fahren.</summary>
        public int ActiveTrips => byTrip.Count;

        // --- Sicherheit (Brände, Kriminalität; gesetzt vom Safety.SafetySystem, null/true = wie bisher)
        /// <summary>Platzhalter-Brandeinsätze aus dem Brandrisiko (aus, sobald es echte Brände gibt).</summary>
        public bool PlaceholderFireCalls { get; set; } = true;
        /// <summary>Fahrzeug bleibt am Halt, solange true (Löschfahrzeug am brennenden Gebäude). null = feste Standzeit.</summary>
        public Func<ServiceUnit, bool> HoldAtStop { get; set; }
        /// <summary>Gewicht je Gebäude für die Streifen-Auswahl (Kriminalität); null = gleichverteilt wie bisher.</summary>
        public Func<Building, float> PatrolWeight { get; set; }
        /// <summary>Fahrzeug ist am Halt angekommen (läuft im Verkehrsschritt: nur Zustand ändern, keine Fahrt starten).</summary>
        public event Action<ServiceUnit> ArrivedAtStop;
        /// <summary>Halt erledigt (vor der Weiterfahrt; läuft im Verkehrsschritt).</summary>
        public event Action<ServiceUnit> StopFinished;
        // --- Ende Sicherheit

        public ServiceVehicleSystem(ServiceSystem services, CitySystem city, TrafficSystem traffic, SimulationClock clock, CitizenSystem citizens, int seed)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.city = city ?? throw new ArgumentNullException(nameof(city));
            this.traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            rng = new SimRandom(unchecked(seed * 53 + 911));
            traffic.TripEnded += OnTripEnded;
            if (citizens != null) citizens.BecameIll += OnBecameIll;
        }

        // ------------------------------------------------------------ Abfragen (Darstellung, Inspektor)

        /// <summary>Dienstfahrzeug zu einer Fahrt (<see cref="Vehicle.TripId"/>), null = keins.</summary>
        public ServiceUnit UnitForTrip(int tripId) => TripIdRanges.IsService(tripId) && byTrip.TryGetValue(tripId, out var u) ? u : null;

        /// <summary>Fahrzeuge, die gerade an einem Halt stehen (kein Fahrzeug im Verkehr) – die Darstellung zeichnet sie geparkt.</summary>
        public IEnumerable<ServiceUnit> ParkedUnits
        {
            get { foreach (var u in units) if (u.HasPark && u.TripId == 0 && u.State != ServiceUnitState.Idle) yield return u; }
        }

        /// <summary>Fahrzeuge eines Dienstgebäudes (nach Id).</summary>
        public IEnumerable<ServiceUnit> UnitsOf(int depotId)
        {
            foreach (var u in units) if (u.DepotId == depotId) yield return u;
        }

        // ------------------------------------------------------------ Stadtkasse

        public string Name => "Dienstfahrzeuge";
        public double WeeklyUpkeep() => Enabled ? units.Count * (double)ServiceVehicleRules.UpkeepPerVehicleWeek : 0.0;

        // ------------------------------------------------------------ Takt

        public void Tick(SimulationWorld world)
        {
            long now = clock.Tick;
            if (clock.Day != statsDay)
            {
                if (statsDay >= 0) { Yesterday.CopyFrom(Today); Today.Clear(); }
                statsDay = clock.Day;
            }
            bool garbageFleet = false;
            if (Enabled)
                foreach (var u in units) if (u.Kind == ServiceVehicleKind.GarbageTruck) { garbageFleet = true; break; }
            services.GarbageBaseShare = garbageFleet ? ServiceVehicleRules.GarbageBaseShareWithTrucks : 1f;
            if (!Enabled) { rescueQueue.Clear(); DropIdle(); return; }

            if (now % TimeModel.TicksPerHour == 0)
            {
                SyncFleet(now);
                garbageFleet = false;
                foreach (var u in units) if (u.Kind == ServiceVehicleKind.GarbageTruck) { garbageFleet = true; break; }
                services.GarbageBaseShare = garbageFleet ? ServiceVehicleRules.GarbageBaseShareWithTrucks : 1f;
                var assigned = AssignBuildings();
                DispatchGarbage(now, assigned);
                DispatchPatrols(now, assigned);
                FireCalls(now);
            }
            DispatchRescues(now);
            foreach (var u in units)
            {
                if (!u.IsBusy) continue;
                if (now - u.MissionStartTick > ServiceVehicleRules.MissionTimeoutTicks) { Abort(u, now); continue; }
                // Startversuch wiederholen (kein Platz an der Zufahrt, Fahrzeuglimit)
                if (u.TripId == 0 && (u.State == ServiceUnitState.Driving || u.State == ServiceUnitState.Returning)) TryStart(u, now);
            }
        }

        /// <summary>Verkehrsschritt: Fahrzeuge, deren Standzeit am Halt abgelaufen ist, erledigen den Halt und fahren weiter.</summary>
        public void Substep(SimulationWorld world)
        {
            if (units.Count == 0) return;
            double t = traffic.Time;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.State == ServiceUnitState.Working && u.WorkUntil <= t && (HoldAtStop == null || !HoldAtStop(u))) FinishStop(u);
            }
        }

        // ------------------------------------------------------------ Flotte

        /// <summary>Fahrzeuge je Dienstgebäude anlegen bzw. überzählige (in der Wache) entfernen.</summary>
        void SyncFleet(long now)
        {
            var facilities = new List<ServiceFacility>(services.Facilities);
            facilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            var count = new Dictionary<int, int>();
            foreach (var u in units) { count.TryGetValue(u.DepotId, out int c); count[u.DepotId] = c + 1; }
            var wanted = new Dictionary<int, int>();
            foreach (var f in facilities)
            {
                int n = ServiceVehicleRules.FleetSize(f.Type.Id, out var kind);
                if (n <= 0 || !f.HasAccess) continue;
                wanted[f.Id] = n;
                count.TryGetValue(f.Id, out int have);
                for (int i = have; i < n; i++)
                    units.Add(new ServiceUnit { Id = nextUnitId++, Kind = kind, DepotId = f.Id, State = ServiceUnitState.Idle, ReadyTick = now });
            }
            // Fahrzeuge ohne (erreichbares) Dienstgebäude bzw. zu viele: nur wenn sie in der Wache stehen
            var keep = new Dictionary<int, int>();
            units.RemoveAll(u =>
            {
                wanted.TryGetValue(u.DepotId, out int n);
                keep.TryGetValue(u.DepotId, out int k);
                if (u.IsBusy || k < n) { keep[u.DepotId] = k + 1; return false; }
                return true;
            });
        }

        void DropIdle() => units.RemoveAll(u => !u.IsBusy);

        ServiceUnit FreeUnit(int depotId, long now)
        {
            foreach (var u in units)
                if (u.DepotId == depotId && !u.IsBusy && u.ReadyTick <= now) return u;
            return null;
        }

        bool Busy(int buildingId)
        {
            foreach (var u in units)
            {
                if (!u.IsBusy) continue;
                if (u.TargetBuildingId == buildingId || u.Stops.Contains(buildingId)) return true;
            }
            return false;
        }

        /// <summary>Gebäude je zuständigem Müll- bzw. Polizeidienst (ein Durchlauf, nach Gebäude-Id).</summary>
        Dictionary<int, List<Building>> AssignBuildings()
        {
            var map = new Dictionary<int, List<Building>>();
            var list = new List<Building>(city.Buildings);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var b in list)
            {
                if (!b.IsConstructed) continue;
                foreach (var channel in new[] { ServiceKinds.Garbage, ServiceKinds.Police })
                {
                    var f = services.AssignedFacility(channel, b);
                    if (f == null) continue;
                    if (!map.TryGetValue(f.Id, out var l)) map[f.Id] = l = new List<Building>();
                    l.Add(b);
                }
            }
            return map;
        }

        // ------------------------------------------------------------ Aufträge

        void DispatchGarbage(long now, Dictionary<int, List<Building>> assigned)
        {
            var depots = new List<ServiceFacility>();
            foreach (var f in services.Facilities)
                if (f.Kind == ServiceKinds.Garbage && f.HasAccess && f.EffectiveCapacity > 0f) depots.Add(f);
            depots.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var depot in depots)
            {
                if (!assigned.TryGetValue(depot.Id, out var buildings)) continue;
                var pool = new List<(Building b, float g)>();
                foreach (var b in buildings)
                {
                    float g = services.GarbageAt(b);
                    if (g >= ServiceVehicleRules.GarbagePickupThreshold && !Busy(b.Id)) pool.Add((b, g));
                }
                if (pool.Count == 0) continue;
                pool.Sort((x, y) => x.g != y.g ? y.g.CompareTo(x.g) : x.b.Id.CompareTo(y.b.Id));
                ServiceUnit u;
                while (pool.Count > 0 && (u = FreeUnit(depot.Id, now)) != null)
                {
                    // Tour: schwerster Halt zuerst, dann jeweils der nächste der schwersten 12 bis 6 Halte bzw. volle Ladung
                    var stops = new List<int>();
                    float load = 0f;
                    var at = depot.Position;
                    while (stops.Count < ServiceVehicleRules.GarbageMaxStops && pool.Count > 0 && load < ServiceVehicleRules.GarbageTruckCapacity)
                    {
                        int best = 0;
                        if (stops.Count > 0)
                        {
                            float bestD = float.MaxValue;
                            for (int i = 0; i < Math.Min(12, pool.Count); i++)
                            {
                                float d = Vector2.Distance(at, pool[i].b.Position);
                                if (d < bestD) { bestD = d; best = i; }
                            }
                        }
                        stops.Add(pool[best].b.Id);
                        load += pool[best].g;
                        at = pool[best].b.Position;
                        pool.RemoveAt(best);
                    }
                    Begin(u, ServiceMission.Collection, stops, now);
                }
            }
        }

        void DispatchPatrols(long now, Dictionary<int, List<Building>> assigned)
        {
            // Sicherheit: Reserve je Wache (Standard 0 = wie bisher)
            Dictionary<int, int> idle = null;
            if (PatrolReserve > 0)
            {
                idle = new Dictionary<int, int>();
                foreach (var x in units)
                    if (x.Kind == ServiceVehicleKind.PoliceCar && !x.IsBusy) { idle.TryGetValue(x.DepotId, out int n); idle[x.DepotId] = n + 1; }
            }
            foreach (var u in units)
            {
                if (u.Kind != ServiceVehicleKind.PoliceCar || u.IsBusy || u.ReadyTick > now) continue;
                if (idle != null)
                {
                    idle.TryGetValue(u.DepotId, out int free);
                    if (free <= PatrolReserve) continue;
                    idle[u.DepotId] = free - 1;
                }
                if (!assigned.TryGetValue(u.DepotId, out var buildings) || buildings.Count == 0) continue;
                var own = buildings; // Gebäude, die diese Wache betreut (Schlüssel = Dienst-Id)
                var stops = new List<int>();
                var weight = PatrolWeight; // Sicherheit: Streifen fahren bevorzugt dorthin, wo viel passiert
                float total = 0f;
                if (weight != null) foreach (var b in own) total += 0.25f + Math.Max(0f, weight(b));
                for (int i = 0; i < ServiceVehicleRules.PatrolStops * 3 && stops.Count < ServiceVehicleRules.PatrolStops; i++)
                {
                    Building b;
                    if (weight == null) b = own[rng.Next(own.Count)];
                    else
                    {
                        double r = rng.NextDouble() * total;
                        b = own[own.Count - 1];
                        foreach (var x in own)
                        {
                            r -= 0.25f + Math.Max(0f, weight(x));
                            if (r < 0) { b = x; break; }
                        }
                    }
                    if (!stops.Contains(b.Id)) stops.Add(b.Id);
                }
                Begin(u, ServiceMission.Patrol, stops, now);
            }
        }

        /// <summary>Platzhalter-Brandeinsätze: je Stunde und Gebäude Chance Brandrisiko × Rate / 24 (stabiler Hash, kein Zufallsverbrauch).</summary>
        void FireCalls(long now)
        {
            if (!PlaceholderFireCalls) return; // Sicherheit: echte Brände ersetzen die Platzhalter
            bool any = false;
            foreach (var u in units) if (u.Kind == ServiceVehicleKind.FireTruck) { any = true; break; }
            if (!any) return;
            int hour = (int)(now / TimeModel.TicksPerHour);
            var list = new List<Building>(city.Buildings);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var b in list)
            {
                if (!b.IsConstructed) continue;
                float p = services.FireRisk(b) * ServiceVehicleRules.FireCallsPerRiskDay / 24f;
                if (p <= 0f || SimRandom.Hash(b.Id, hour * 31 + 7) / (double)uint.MaxValue >= p) continue;
                Call(ServiceMission.FireCall, b, now);
            }
        }

        /// <summary>
        /// Einsatz zu einem Gebäude (Feuerwehr bzw. Rettung): freies Fahrzeug des zuständigen Dienstes (Zuordnung der Abdeckung),
        /// sonst verpasst. Liefert das ausrückende Fahrzeug (Tests, Szenarien) oder null.
        /// </summary>
        internal ServiceUnit Call(ServiceMission mission, Building b, long now)
        {
            if (b == null) return null;
            bool fire = mission == ServiceMission.FireCall;
            var station = services.AssignedFacility(fire ? ServiceKinds.Fire : ServiceKinds.Health, b);
            var u = station != null ? FreeUnit(station.Id, now) : null;
            if (u == null || u.Kind != (fire ? ServiceVehicleKind.FireTruck : ServiceVehicleKind.Ambulance)) { Today.MissedCalls++; return null; }
            Begin(u, mission, new List<int> { b.Id }, now);
            return u;
        }

        // --- Sicherheit: Einsatz mit Rückfall auf andere Wachen
        /// <summary>
        /// Einsatz zu einem Gebäude (Brand, Tatort, Rettung): zuerst die zuständige Wache, sonst die schnellste andere Wache
        /// der Art mit freiem Fahrzeug, deren Reichweite das Gebäude erreicht. Liefert das ausrückende Fahrzeug oder null
        /// (kein Fahrzeug frei → zählt als verpasst).
        /// </summary>
        public ServiceUnit Dispatch(ServiceMission mission, Building b, long now)
        {
            if (b == null || !Enabled) return null;
            string channel; ServiceVehicleKind kind;
            switch (mission)
            {
                case ServiceMission.FireCall: channel = ServiceKinds.Fire; kind = ServiceVehicleKind.FireTruck; break;
                case ServiceMission.CrimeCall: channel = ServiceKinds.Police; kind = ServiceVehicleKind.PoliceCar; break;
                case ServiceMission.Rescue: channel = ServiceKinds.Health; kind = ServiceVehicleKind.Ambulance; break;
                default: return null;
            }
            var station = services.AssignedFacility(channel, b);
            var u = station != null ? EmergencyUnit(station.Id, kind) : null;
            if (u == null)
            {
                float best = float.PositiveInfinity;
                foreach (var f in services.Facilities)
                {
                    if (f.Kind != channel || f.Reach == null) continue;
                    var cand = EmergencyUnit(f.Id, kind);
                    if (cand == null) continue;
                    float secs = services.ResponseSeconds(f, b);
                    if (secs > f.Reach.RangeSeconds * 1.5f) continue;
                    if (secs < best || (secs == best && u != null && cand.Id < u.Id)) { best = secs; u = cand; }
                }
            }
            if (u == null) { Today.MissedCalls++; return null; }
            if (u.State == ServiceUnitState.Working && u.Mission == ServiceMission.Patrol)
            {
                // Streife unterbrechen: der Wagen fährt vom aktuellen Halt direkt zum Einsatz
                int at = u.TargetBuildingId;
                Begin(u, mission, new List<int> { b.Id }, now, startTrip: false);
                u.FromBuildingId = at;
                TryStart(u, now);
            }
            else Begin(u, mission, new List<int> { b.Id }, now);
            return u;
        }

        /// <summary>
        /// Fahrzeug für einen Notfall: zuerst eins in der Wache (auch in der Pause nach einer Streife), sonst ein Streifenwagen,
        /// der gerade an einem Streifen-Halt steht (wird umgeleitet). Fahrende Streifen werden nicht umgeleitet.
        /// </summary>
        ServiceUnit EmergencyUnit(int depotId, ServiceVehicleKind kind)
        {
            foreach (var u in units)
                if (u.DepotId == depotId && u.Kind == kind && !u.IsBusy) return u;
            if (kind == ServiceVehicleKind.PoliceCar)
                foreach (var u in units)
                    if (u.DepotId == depotId && u.Kind == kind && u.Mission == ServiceMission.Patrol && u.State == ServiceUnitState.Working && u.TripId == 0)
                        return u;
            return null;
        }

        /// <summary>Sicherheit: so viele Streifenwagen je Wache bleiben für Einsätze in der Wache (0 = alle fahren Streife, wie bisher).</summary>
        public int PatrolReserve { get; set; }

        /// <summary>Fahrzeuge, die gerade zu einem Gebäude unterwegs sind bzw. dort arbeiten (Auftrag, Ziel), nach Id.</summary>
        public IEnumerable<ServiceUnit> UnitsAt(int buildingId, ServiceMission mission)
        {
            foreach (var u in units)
                if (u.Mission == mission && u.TargetBuildingId == buildingId &&
                    (u.State == ServiceUnitState.Driving || u.State == ServiceUnitState.Working)) yield return u;
        }
        // --- Ende Sicherheit

        internal void OnBecameIll(Citizen c)
        {
            if (!Enabled || c == null || c.Household == null || c.Household.HomeId == 0) return;
            bool severe = c.Age >= 65 || SimRandom.Hash(c.Id, clock.Day * 7 + 3) / (double)uint.MaxValue < ServiceVehicleRules.RescueShareUnder65;
            if (severe) rescueQueue.Add((c.Id, c.Household.HomeId));
        }

        void DispatchRescues(long now)
        {
            if (rescueQueue.Count == 0) return;
            foreach (var (_, home) in rescueQueue)
            {
                var b = city.GetBuilding(home);
                if (b != null) Call(ServiceMission.Rescue, b, now);
            }
            rescueQueue.Clear();
        }

        // ------------------------------------------------------------ Fahrten

        void Begin(ServiceUnit u, ServiceMission mission, List<int> stops, long now, bool startTrip = true)
        {
            if (stops.Count == 0) return;
            u.Mission = mission;
            u.Stops.Clear();
            for (int i = 1; i < stops.Count; i++) u.Stops.Add(stops[i]);
            u.TargetBuildingId = stops[0];
            u.FromBuildingId = 0;
            u.State = ServiceUnitState.Driving;
            u.Visits = 0;
            u.Load = 0f;
            u.HasPark = false;
            u.Retries = 0;
            u.MissionStartTick = now;
            if (startTrip) TryStart(u, now);
        }

        int NextTripId()
        {
            int id = nextTripId--;
            if (nextTripId < TripIdRanges.ServiceLast) nextTripId = TripIdRanges.ServiceFirst;
            return id;
        }

        bool TryEnd(int buildingId, int depotId, out TripEnd end)
        {
            end = default;
            if (buildingId != 0)
            {
                var b = city.GetBuilding(buildingId);
                if (b == null || b.FrontSegmentId < 0) return false;
                end = TripEnd.AtSegment(b.FrontSegmentId, b.FrontT);
                return true;
            }
            var f = services.GetFacility(depotId);
            if (f == null || !f.HasAccess) return false;
            end = TripEnd.AtSegment(f.AccessSegmentId, f.AccessT);
            return true;
        }

        /// <summary>Nächste Fahrt starten (vom letzten Halt bzw. Depot zum Ziel). Scheitert → später erneut, nach zu vielen Versuchen Abbruch.</summary>
        void TryStart(ServiceUnit u, long now)
        {
            if (u.TripId != 0) return;
            bool toDepot = u.State == ServiceUnitState.Returning;
            if (!toDepot && city.GetBuilding(u.TargetBuildingId) == null) { NextStop(u, now); return; } // Ziel abgerissen
            Vehicle v = null;
            int id = 0;
            if (byTrip.Count < ServiceVehicleRules.MaxTrafficShare * traffic.MaxVehicles &&
                TryEnd(u.FromBuildingId, u.DepotId, out var from) && TryEnd(toDepot ? 0 : u.TargetBuildingId, u.DepotId, out var to))
            {
                id = NextTripId();
                v = traffic.RequestTrip(from, to, ServiceVehicleRules.ClassOf(u.Kind), id);
            }
            if (v == null)
            {
                Today.FailedStarts++;
                if (++u.Retries > ServiceVehicleRules.MaxStartRetries) Abort(u, now);
                return;
            }
            u.Retries = 0;
            u.TripId = id;
            byTrip[id] = u;
            Today.Trips++;
        }

        /// <summary>Verkehr: Fahrt beendet (angekommen oder abgebrochen). Läuft im Verkehrsschritt – nur Zustand ändern, keine Fahrt starten.</summary>
        void OnTripEnded(Vehicle v, bool arrived)
        {
            if (!TripIdRanges.IsService(v.TripId) || !byTrip.TryGetValue(v.TripId, out var u)) return;
            byTrip.Remove(v.TripId);
            u.TripId = 0;
            if (!arrived) { Abort(u, clock.Tick); return; }
            u.ParkPosition = v.Position;
            u.ParkDirection = v.Direction;
            u.ParkHeight = v.Height;
            if (u.State == ServiceUnitState.Returning) { ArriveAtDepot(u, clock.Tick); return; }
            u.HasPark = true;
            u.State = ServiceUnitState.Working;
            u.WorkUntil = traffic.Time + StopSeconds(u.Mission);
            ArrivedAtStop?.Invoke(u); // Sicherheit
        }

        static float StopSeconds(ServiceMission m)
        {
            switch (m)
            {
                case ServiceMission.Collection: return ServiceVehicleRules.GarbageStopSeconds;
                case ServiceMission.Patrol: return ServiceVehicleRules.PatrolStopSeconds;
                case ServiceMission.FireCall: return ServiceVehicleRules.FireCallSeconds;
                case ServiceMission.CrimeCall: return ServiceVehicleRules.CrimeCallSeconds; // Sicherheit
                default: return ServiceVehicleRules.RescueSeconds;
            }
        }

        /// <summary>Halt erledigt: Müll laden bzw. Einsatz zählen, dann weiter zum nächsten Halt oder zurück.</summary>
        void FinishStop(ServiceUnit u)
        {
            var b = city.GetBuilding(u.TargetBuildingId);
            if (b != null)
            {
                Today.Stops++;
                u.Visits++;
                switch (u.Mission)
                {
                    case ServiceMission.Collection:
                        float got = services.CollectGarbage(b, ServiceVehicleRules.GarbageTruckCapacity - u.Load);
                        u.Load += got;
                        Today.GarbageCollected += got;
                        break;
                    case ServiceMission.Patrol: Today.PatrolStops++; break;
                    case ServiceMission.FireCall: Today.FireCalls++; break;
                    case ServiceMission.Rescue: Today.Rescues++; break;
                }
                StopFinished?.Invoke(u); // Sicherheit
            }
            u.FromBuildingId = u.TargetBuildingId;
            NextStop(u, clock.Tick);
        }

        void NextStop(ServiceUnit u, long now)
        {
            bool full = u.Mission == ServiceMission.Collection && u.Load >= ServiceVehicleRules.GarbageTruckCapacity - 0.01f;
            if (u.Stops.Count > 0 && !full)
            {
                u.TargetBuildingId = u.Stops[0];
                u.Stops.RemoveAt(0);
                u.State = ServiceUnitState.Driving;
            }
            else
            {
                u.Stops.Clear();
                u.TargetBuildingId = 0;
                u.State = ServiceUnitState.Returning;
            }
            u.Retries = 0;
            TryStart(u, now);
        }

        void ArriveAtDepot(ServiceUnit u, long now)
        {
            if (u.Load > 0f) services.UnloadGarbage(services.GetFacility(u.DepotId), u.Load);
            u.Load = 0f;
            u.State = ServiceUnitState.Idle;
            u.ReadyTick = now + (u.Mission == ServiceMission.Patrol ? ServiceVehicleRules.PatrolPauseTicks : 1);
            u.Mission = ServiceMission.None;
            u.HasPark = false;
            u.Stops.Clear();
            u.TargetBuildingId = 0;
            u.FromBuildingId = 0;
        }

        /// <summary>Tour abbrechen (Stau, Straße weg, kein Platz): Fahrzeug gilt als zurück in der Wache, Ladung wird entladen.</summary>
        void Abort(ServiceUnit u, long now)
        {
            if (u.TripId != 0) { byTrip.Remove(u.TripId); u.TripId = 0; } // Fahrzeug fährt ggf. noch zu Ende, zählt nicht mehr
            ArriveAtDepot(u, now);
            u.ReadyTick = now + 3;
        }
    }
}
