using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// Dienste (G4): Polizei, Feuerwehr, Gesundheit, Schulen, Bibliothek, Parks, Müll – einzeln gebaut, mit Einflussradius.
    /// <para>
    /// <b>Reichweite über das Straßennetz:</b> Dijkstra vom Anschlusspunkt des Dienstes (nächste Stadtstraße, Rand
    /// ≤ <see cref="RoadReach"/>) über Fahrzeit (Tempolimit × <see cref="SpeedFactor"/>, + <see cref="JunctionPenaltySeconds"/>
    /// je Kreuzung, Einbahnstraßen nur in Fahrtrichtung). Ein Gebäude wird an seiner Zufahrt
    /// (<see cref="Building.FrontSegmentId"/>/<see cref="Building.FrontT"/>) erreicht. Parks wirken über Fußweg-Radius
    /// (Luftlinie): Spaziergänger nehmen Abkürzungen über Wege/Grundstücke, die das Straßennetz nicht kennt.
    /// </para>
    /// <para>
    /// <b>Abdeckung</b> je Gebäude und Kanal = max über alle Dienste (Entfernungswirkung × Überlastungsfaktor).
    /// Entfernungswirkung 1 bis <see cref="FullEffectShare"/> der Reichweite, dann linear auf 0. Jedes Gebäude belastet
    /// den Dienst mit der besten Entfernungswirkung (Bedarf × Wirkung); Überlastungsfaktor = min(1, Kapazität/Belastung).
    /// </para>
    /// <para>
    /// Neu berechnet wird nur bei Änderungen: Reichweiten bei Straßen-/Bauwerks-Version, Zuordnung Gebäude↔Dienst
    /// zusätzlich bei Stadt-Version; Belastung/Abdeckung (billig) jeden Durchlauf, weil die Belegung wechselt.
    /// </para>
    /// Direkte Folgen (keine Bürgerwirkungen – die baut das Bedürfnissystem über <see cref="Coverage"/>):
    /// Müll stapelt sich am Gebäude ohne Abfuhr (<see cref="GarbageAt"/>), Brandrisiko-Wert je Gebäude (<see cref="FireRisk"/>).
    /// </summary>
    public sealed class ServiceSystem : ISimulationSystem, Economy.IUpkeepSource
    {
        /// <summary>Alle 5 Ticks = alle 30 Spielminuten (wie Stadt und Versorgung).</summary>
        public int TickInterval => 5;

        /// <summary>Ein Dienst ist an eine Stadtstraße angeschlossen, wenn deren Rand höchstens so weit von der Grundfläche entfernt ist.</summary>
        public const float RoadReach = 8f;
        /// <summary>Durchschnittstempo im Einsatz als Anteil des Tempolimits.</summary>
        public const float SpeedFactor = 0.8f;
        /// <summary>Zuschlag je durchfahrener Kreuzung (ab 3 Straßen) in Sekunden.</summary>
        public const float JunctionPenaltySeconds = 5f;
        /// <summary>Bis zu diesem Anteil der Reichweite wirkt ein Dienst voll, danach linear abnehmend.</summary>
        public const float FullEffectShare = 0.4f;
        /// <summary>Größter Höhenunterschied auf der Grundfläche (wird eingeebnet).</summary>
        public const float MaxSlope = 8f;
        /// <summary>Für <see cref="CoverageAt"/>: so weit darf eine Stadtstraße vom Punkt entfernt sein.</summary>
        public const float PointRoadReach = 40f;
        /// <summary>Müllfahrzeuge holen je Durchlauf höchstens das Vielfache der laufenden Menge (Rückstau baut sich langsam ab).</summary>
        public const float GarbageCatchUp = 4f;
        /// <summary><see cref="GarbageLevel"/> = 1 nach so vielen Tagen ohne Abfuhr.</summary>
        public const float GarbageDaysToFull = 7f;

        static readonly string[] Channels =
        {
            ServiceKinds.Police, ServiceKinds.Fire, ServiceKinds.Health, ServiceKinds.EducationPrimary, ServiceKinds.EducationSecondary,
            ServiceKinds.Library, ServiceKinds.Park, ServiceKinds.Garbage,
            ServiceKinds.Media, // G4 erweitert: Zeitung/Radio/TV
        };
        static readonly int GarbageChannel = Array.IndexOf(Channels, ServiceKinds.Garbage);
        static readonly int FireChannel = Array.IndexOf(Channels, ServiceKinds.Fire);

        sealed class BuildingState
        {
            public readonly float[] Coverage = new float[Channels.Length];
            public float Garbage;
            public float FireRisk;
        }

        struct Link
        {
            public ServiceFacility Facility;
            public float Factor;
        }

        sealed class BuildingLinks
        {
            public Building Building;
            public readonly List<Link> Links = new List<Link>();
            public ServiceFacility Assigned;
        }

        readonly RoadNetwork roads;
        readonly TerrainMap terrain;
        readonly CitySystem city;
        readonly UtilitySystem utilities;
        readonly EnvironmentLayers environment;
        readonly Vector2 min, max;

        readonly Dictionary<int, ServiceFacility> facilities = new Dictionary<int, ServiceFacility>();
        readonly Dictionary<int, BuildingState> states = new Dictionary<int, BuildingState>();
        /// <summary>Je Kanal: Gebäude-Id → erreichende Dienste (in Id-Reihenfolge aufgebaut).</summary>
        readonly Dictionary<int, BuildingLinks>[] links = new Dictionary<int, BuildingLinks>[Channels.Length];
        readonly List<Building> sortedBuildings = new List<Building>();
        readonly List<ServiceFacility> sortedFacilities = new List<ServiceFacility>();
        int nextId = 1;
        int builtRoadVersion = -1, builtStructureVersion = -1, reachVersion, linkedReachVersion = -1, linkedCityVersion = -1;
        bool pollutionActive;

        /// <summary>Wird bei jeder Änderung an Dienstgebäuden erhöht (für die Darstellung).</summary>
        public int StructureVersion { get; private set; }
        /// <summary>Wird erhöht, wenn sich Abdeckung, Müll oder Brandrisiko merklich geändert haben.</summary>
        public int CoverageVersion { get; private set; }

        /// <summary>
        /// Optional: echter Bedarf je Gebäude und Kanal (z. B. Schülerzahl aus dem Bürgersystem). Rückgabe &lt; 0 = Standard-Schätzung.
        /// </summary>
        public Func<Building, string, float> DemandProvider { get; set; }

        public IEnumerable<ServiceFacility> Facilities => facilities.Values;
        public int FacilityCount => facilities.Count;

        public event Action<ServiceFacility> FacilityAdded;
        public event Action<ServiceFacility> FacilityRemoved;

        public ServiceSystem(RoadNetwork roads, TerrainMap terrain, CitySystem city, UtilitySystem utilities, EnvironmentLayers environment,
            Vector2 min, Vector2 max)
        {
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.city = city ?? throw new ArgumentNullException(nameof(city));
            this.utilities = utilities;
            this.environment = environment;
            this.min = min;
            this.max = max;
            for (int i = 0; i < links.Length; i++) links[i] = new Dictionary<int, BuildingLinks>();

            roads.SegmentAdded += OnSegmentAdded;
            city.BuildingRemoved += b => states.Remove(b.Id);

            // Auf Dienstflächen wächst nichts (vorhandenen Blocker, z. B. Versorgung, weiter beachten)
            var previousBlocker = city.LotBlocker;
            city.LotBlocker = p => (previousBlocker != null && previousBlocker(p)) || FacilityAt(p, ZoneGrid.CellSize * 0.5f) != null;

            // Terraforming: Gelände unter Diensten ist gesperrt
            var previousLocks = terrain.LockQuery;
            terrain.LockQuery = (lo, hi) =>
            {
                var inner = previousLocks?.Invoke(lo, hi);
                var near = new List<ServiceFacility>();
                foreach (var f in facilities.Values)
                    if (f.Contains(Vector2.Clamp(f.Position, lo, hi), TerrainLocks.Band + terrain.CellSize + MathF.Max(f.Type.Width, f.Type.Depth)))
                        near.Add(f);
                if (near.Count == 0) return inner;
                return p =>
                {
                    foreach (var f in near) if (f.Contains(p, terrain.CellSize)) return 1f;
                    return inner?.Invoke(p) ?? 0f;
                };
            };

            // Versorger dürfen nicht auf Dienstflächen (Bauwerk bzw. Mast)
            if (utilities != null)
            {
                utilities.OtherStructureOverlaps = (c, f, w, d) =>
                {
                    foreach (var s in facilities.Values)
                        if (ServiceGeometry.RectsOverlap(c, f, w, d, s.Position, s.Front, s.Type.Width, s.Type.Depth)) return true;
                    return false;
                };
                utilities.OtherStructureAt = (p, margin) => FacilityAt(p, margin) != null;
            }
        }

        // --- ÖPNV (Hook): überlappt ein fremdes Bauwerk (Mitte, Front, Breite, Tiefe)? Gesetzt vom TransitSystem.
        public Func<Vector2, Vector2, float, float, bool> OtherStructureOverlaps { get; set; }
        // --- Ende ÖPNV

        public ServiceFacility GetFacility(int id) => facilities.TryGetValue(id, out var f) ? f : null;

        public ServiceFacility FacilityAt(Vector2 p, float margin = 0f)
        {
            foreach (var f in facilities.Values)
                if (f.Contains(p, margin)) return f;
            return null;
        }

        // ------------------------------------------------------------ Abfrage-API (verbindlich)

        /// <summary>
        /// Abdeckung des Gebäudes mit der Dienstart (0..1, <see cref="ServiceKinds"/>). Sinkt mit der Fahrzeit und bei
        /// Überlastung. <see cref="ServiceKinds.Education"/> = Mittel aus Grund- und Oberschule. Unbekannte Art → 0.
        /// </summary>
        public float Coverage(string serviceKind, Building b)
        {
            if (b == null || !states.TryGetValue(b.Id, out var s)) return 0f;
            if (serviceKind == ServiceKinds.Education)
                return 0.5f * (s.Coverage[Array.IndexOf(Channels, ServiceKinds.EducationPrimary)] +
                               s.Coverage[Array.IndexOf(Channels, ServiceKinds.EducationSecondary)]);
            int c = Array.IndexOf(Channels, serviceKind);
            return c >= 0 ? s.Coverage[c] : 0f;
        }

        /// <summary>
        /// Abdeckung an einem beliebigen Punkt (z. B. Bauplatz, Inspektor): Straßendienste über die nächste Stadtstraße
        /// (≤ <see cref="PointRoadReach"/>, sonst 0), Parks über die Luftlinie. Mit dem aktuellen Überlastungsfaktor.
        /// </summary>
        public float CoverageAt(string serviceKind, Vector2 p)
        {
            if (serviceKind == ServiceKinds.Education)
                return 0.5f * (CoverageAt(ServiceKinds.EducationPrimary, p) + CoverageAt(ServiceKinds.EducationSecondary, p));
            if (Array.IndexOf(Channels, serviceKind) < 0) return 0f;
            RoadSegment seg = null;
            bool searched = false;
            float best = 0f;
            foreach (var f in facilities.Values)
            {
                if (f.Type.Channel != serviceKind) continue;
                float factor;
                if (f.Type.Walking) factor = DistanceFactor(f.DistanceTo(p), f.Type.Range);
                else
                {
                    if (f.Reach == null) continue;
                    if (!searched) { seg = NearestCityRoad(p, PointRoadReach); searched = true; }
                    if (seg == null) continue;
                    factor = f.Reach.Factor(seg, seg.ClosestT(p));
                }
                best = MathF.Max(best, factor * f.LoadFactor);
            }
            return best;
        }

        /// <summary>Gesammelter (nicht abgeholter) Müll am Gebäude in t.</summary>
        public float GarbageAt(Building b) => b != null && states.TryGetValue(b.Id, out var s) ? s.Garbage : 0f;

        /// <summary>Müll am Gebäude 0..1 (1 = <see cref="GarbageDaysToFull"/> Tage Produktion liegen herum).</summary>
        public float GarbageLevel(Building b)
        {
            if (b == null || !states.TryGetValue(b.Id, out var s)) return 0f;
            float perDay = MathF.Max(0.02f, DemandOf(b, ServiceKinds.Garbage));
            return Math.Clamp(s.Garbage / (perDay * GarbageDaysToFull), 0f, 1f);
        }

        /// <summary>Brandrisiko 0..1 (Grundrisiko je Zone, gesenkt durch die Feuerwehr, erhöht durch liegenden Müll). Noch ohne Brände.</summary>
        public float FireRisk(Building b) => b != null && states.TryGetValue(b.Id, out var s) ? s.FireRisk : 0f;

        /// <summary>Mittlere Abdeckung aller fertigen, bewohnten/belegten Gebäude (für Anzeigen).</summary>
        public float AverageCoverage(string serviceKind)
        {
            float sum = 0f;
            int n = 0;
            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) continue;
                sum += Coverage(serviceKind, b);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        public float TotalGarbage { get { float s = 0f; foreach (var st in states.Values) s += st.Garbage; return s; } }

        // ------------------------------------------------------------ Kosten (Stadtkasse bucht – Agent „budget“)

        /// <summary>Unterhalt aller Dienste in € je Spielwoche (die Kasse bucht täglich 1/7, Kategorie „Dienste“).</summary>
        public double WeeklyUpkeep()
        {
            double s = 0;
            foreach (var f in facilities.Values) s += f.Type.UpkeepPerWeek;
            return s;
        }

        public string UpkeepName => "Dienste";
        /// <summary><see cref="Economy.IUpkeepSource"/>: Name in der Bilanz.</summary>
        public string Name => UpkeepName;

        /// <summary>Stadtkasse für Baukosten (null = kostenlos, z. B. reine Tests). Gesetzt von <see cref="SimulationWorld.CreateDefault"/>.</summary>
        public Economy.IConstructionBudget Budget { get; set; }
        bool Affordable(float cost) => Budget == null || Budget.CanAfford(cost);

        // ------------------------------------------------------------ Bedarf

        /// <summary>
        /// Bedarf eines Gebäudes je Kanal und Tag: Personen (Polizei/Feuerwehr: Einwohner + Beschäftigte; Gesundheit:
        /// Einwohner + ¼ Beschäftigte; Bibliothek/Park: Einwohner), Schüler (geschätzt 8 % bzw. 7 % der Einwohner) oder
        /// Müll in t/Tag (Einwohner 10 kg, Arbeitsplätze 5 kg, Industrie 20 kg – Spielmaßstab, nicht real).
        /// </summary>
        public float DemandOf(Building b, string channel)
        {
            if (b == null || !b.IsConstructed) return 0f;
            if (DemandProvider != null)
            {
                float v = DemandProvider(b, channel);
                if (v >= 0f) return v;
            }
            float residents = b.Residents, workers = b.Workers;
            switch (channel)
            {
                case ServiceKinds.Police:
                case ServiceKinds.Fire: return residents + workers;
                case ServiceKinds.Health: return residents + workers * 0.25f;
                case ServiceKinds.EducationPrimary: return residents * 0.08f;
                case ServiceKinds.EducationSecondary: return residents * 0.07f;
                case ServiceKinds.Library:
                case ServiceKinds.Park:
                case ServiceKinds.Media: return residents; // Medien: Leser/Hörer/Zuschauer = Einwohner
                case ServiceKinds.Garbage: return residents * 0.01f + workers * (b.Zone == ZoneType.Industry ? 0.02f : 0.005f);
                default: return 0f;
            }
        }

        // ------------------------------------------------------------ Planen/Bauen/Abreißen

        public ServicePlan Plan(ServiceType type, Vector2 position, float rotationDegrees = 0f)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            float a = rotationDegrees * MathF.PI / 180f;
            var plan = new ServicePlan { Type = type, Position = position, Front = new Vector2(MathF.Sin(a), MathF.Cos(a)), Cost = type.Cost };
            plan.Height = MathF.Max(terrain.HeightAt(position), terrain.WaterLevel + 0.5f);
            plan.Error = Check(plan);
            // Geld zuletzt prüfen: Geometriefehler haben Vorrang (wie Straßen/Versorgung)
            if (plan.Error == ServiceBuildError.None && !Affordable(plan.Cost)) plan.Error = ServiceBuildError.InsufficientFunds;
            if (plan.Error == ServiceBuildError.None)
            {
                var sorted = new List<Building>(city.Buildings);
                sorted.Sort((x, y) => x.Id.CompareTo(y.Id));
                foreach (var b in sorted)
                    if (ServiceGeometry.RectsOverlap(position, plan.Front, type.Width, type.Depth, b.Position, b.Front, b.Width, b.Depth))
                        plan.Displaced.Add(b);
            }
            return plan;
        }

        ServiceBuildError Check(ServicePlan plan)
        {
            var type = plan.Type;
            var corners = ServiceGeometry.Corners(plan.Position, plan.Front, type.Width, type.Depth);
            foreach (var c in corners) if (!InWorld(c)) return ServiceBuildError.OutOfWorld;
            if (terrain.IsWater(plan.Position)) return ServiceBuildError.OnWater;
            float lo = terrain.HeightAt(plan.Position), hi = lo;
            foreach (var c in corners)
            {
                if (terrain.IsWater(c)) return ServiceBuildError.OnWater;
                float h = terrain.HeightAt(c);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            if (hi - lo > MaxSlope) return ServiceBuildError.TooSteep;

            foreach (var s in roads.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, 0f)) return ServiceBuildError.OverlapsRoad;
            foreach (var f in facilities.Values)
                if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, f.Position, f.Front, f.Type.Width, f.Type.Depth))
                    return ServiceBuildError.OverlapsFacility;
            // --- ÖPNV (Hook): andere Bauwerke (Depots) im Weg
            if (OtherStructureOverlaps != null && OtherStructureOverlaps(plan.Position, plan.Front, type.Width, type.Depth))
                return ServiceBuildError.OverlapsFacility;
            // --- Ende ÖPNV
            if (utilities != null)
            {
                foreach (var f in utilities.Facilities)
                    if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, f.Position, f.Front, f.Type.Width, f.Type.Depth))
                        return ServiceBuildError.OverlapsFacility;
                foreach (var l in utilities.Lines)
                    foreach (var p in l.Poles)
                        if (ServiceGeometry.InRect(p, plan.Position, plan.Front, type.Width, type.Depth, UtilitySystem.PoleFootprintHalf))
                            return ServiceBuildError.OverlapsPole;
            }

            if (FindAccess(plan.Position, plan.Front, type.Width, type.Depth, out int seg, out float t))
            {
                plan.AccessSegmentId = seg;
                plan.AccessT = t;
            }
            else if (!type.Walking) return ServiceBuildError.NoRoadAccess;
            return ServiceBuildError.None;
        }

        public ServiceFacility Build(ServicePlan plan)
        {
            if (plan == null || !plan.IsValid) return null;
            if (!Affordable(plan.Cost)) return null; // Kasse kann sich seit der Vorschau geändert haben
            Budget?.Spend(Economy.BudgetCategory.ServiceConstruction, plan.Cost);
            foreach (var b in plan.Displaced) city.RemoveBuilding(b);
            var f = new ServiceFacility
            {
                Id = nextId++,
                Type = plan.Type,
                Position = plan.Position,
                Front = plan.Front,
                Height = plan.Height,
                AccessSegmentId = plan.AccessSegmentId,
                AccessT = plan.AccessT,
            };
            terrain.FlattenArea(f.Position, f.Front, f.Type.Width, f.Type.Depth, f.Height);
            facilities[f.Id] = f;
            StructureVersion++;
            FacilityAdded?.Invoke(f);
            return f;
        }

        /// <summary>Kurzform für Werkzeuge/Tests: planen und bauen, wenn gültig.</summary>
        public ServiceFacility TryBuild(ServiceType type, Vector2 position, float rotationDegrees = 0f) => Build(Plan(type, position, rotationDegrees));

        /// <summary>Spieler-Abriss: entfernt das Gebäude und schreibt 25 % der Baukosten gut (<see cref="Economy.EconomyRules.DemolitionRefundShare"/>).</summary>
        public bool Demolish(int id)
        {
            if (!facilities.TryGetValue(id, out var f)) return false;
            Remove(id);
            Budget?.Refund(f.Type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        /// <summary>Internes Entfernen ohne Erlös (z. B. Straße durchs Gebäude).</summary>
        public bool Remove(int id)
        {
            if (!facilities.TryGetValue(id, out var f)) return false;
            facilities.Remove(id);
            StructureVersion++;
            FacilityRemoved?.Invoke(f);
            return true;
        }

        /// <summary>Reichweite eines geplanten Dienstes (Vorschau: erreichte Straßen einfärben). null bei Parks/ohne Anschluss.</summary>
        public ServiceReach PreviewReach(ServicePlan plan)
        {
            if (plan == null || plan.Type.Walking || plan.AccessSegmentId < 0) return null;
            return ComputeReach(plan.AccessSegmentId, plan.AccessT, plan.Type.RangeSeconds);
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            if (roads.Version != builtRoadVersion || StructureVersion != builtStructureVersion)
            {
                RecomputeReach();
                builtRoadVersion = roads.Version;
                builtStructureVersion = StructureVersion;
                reachVersion++;
            }
            if (reachVersion != linkedReachVersion || city.Version != linkedCityVersion)
            {
                RebuildLinks();
                linkedReachVersion = reachVersion;
                linkedCityVersion = city.Version;
            }
            bool changed = UpdateCoverage();
            changed |= UpdateGarbageAndRisk();
            if (changed) CoverageVersion++;
            Emit();
        }

        /// <summary>Für Tests/Werkzeuge: sofort neu rechnen (ohne Uhr).</summary>
        public void ForceTick() => Tick(null);

        void RecomputeReach()
        {
            sortedFacilities.Clear();
            sortedFacilities.AddRange(facilities.Values);
            sortedFacilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var f in sortedFacilities)
            {
                // Anschluss neu suchen (Straße kann zerteilt, umgebaut oder entfernt worden sein)
                if (FindAccess(f.Position, f.Front, f.Type.Width, f.Type.Depth, out int seg, out float t)) { f.AccessSegmentId = seg; f.AccessT = t; }
                else f.AccessSegmentId = -1;
                f.Reach = f.Type.Walking || !f.HasAccess ? null : ComputeReach(f.AccessSegmentId, f.AccessT, f.Type.RangeSeconds);
            }
        }

        void RebuildLinks()
        {
            sortedBuildings.Clear();
            sortedBuildings.AddRange(city.Buildings);
            sortedBuildings.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (sortedFacilities.Count != facilities.Count)
            {
                sortedFacilities.Clear();
                sortedFacilities.AddRange(facilities.Values);
                sortedFacilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            }
            foreach (var l in links) l.Clear();
            foreach (var b in sortedBuildings)
            {
                if (!states.ContainsKey(b.Id)) states[b.Id] = new BuildingState();
                var seg = roads.GetSegment(b.FrontSegmentId);
                foreach (var f in sortedFacilities)
                {
                    float factor = f.Type.Walking
                        ? DistanceFactor(f.DistanceTo(b.Position), f.Type.Range)
                        : f.Reach != null ? f.Reach.Factor(seg, b.FrontT) : 0f;
                    if (factor <= 0f) continue;
                    int c = Array.IndexOf(Channels, f.Type.Channel);
                    if (!links[c].TryGetValue(b.Id, out var bl)) links[c][b.Id] = bl = new BuildingLinks { Building = b };
                    bl.Links.Add(new Link { Facility = f, Factor = factor });
                }
            }
        }

        /// <summary>Belastung je Dienst und Abdeckung je Gebäude. Liefert true bei merklicher Änderung.</summary>
        bool UpdateCoverage()
        {
            foreach (var f in sortedFacilities) { f.Load = 0f; f.ServedBuildings = 0; }
            for (int c = 0; c < Channels.Length; c++)
                foreach (var bl in links[c].Values)
                {
                    // Bester Dienst mit freier Kapazität (bei Gleichstand der ältere)
                    Link best = default;
                    foreach (var l in bl.Links)
                        if (l.Facility.EffectiveCapacity > 0f && l.Factor > best.Factor) best = l;
                    bl.Assigned = best.Facility;
                    if (best.Facility == null) continue;
                    best.Facility.Load += DemandOf(bl.Building, Channels[c]) * best.Factor;
                    best.Facility.ServedBuildings++;
                }

            bool changed = false;
            foreach (var b in sortedBuildings)
            {
                if (!states.TryGetValue(b.Id, out var s)) continue;
                for (int c = 0; c < Channels.Length; c++)
                {
                    float cov = 0f;
                    if (links[c].TryGetValue(b.Id, out var bl))
                        foreach (var l in bl.Links) cov = MathF.Max(cov, l.Factor * l.Facility.LoadFactor);
                    if (MathF.Abs(cov - s.Coverage[c]) > 1e-3f) changed = true;
                    s.Coverage[c] = cov;
                }
            }
            return changed;
        }

        bool UpdateGarbageAndRisk()
        {
            float dt = TickInterval / (float)SimulationClock.TicksPerDay;
            var collected = new Dictionary<int, float>();
            bool changed = false;
            foreach (var b in sortedBuildings)
            {
                if (!states.TryGetValue(b.Id, out var s)) continue;
                float perDay = DemandOf(b, ServiceKinds.Garbage);
                float before = s.Garbage;
                s.Garbage += perDay * dt;
                float cov = s.Coverage[GarbageChannel];
                if (cov > 0f && links[GarbageChannel].TryGetValue(b.Id, out var bl) && bl.Assigned != null)
                {
                    float take = cov * MathF.Min(s.Garbage, MathF.Max(perDay * dt * GarbageCatchUp, 0.02f));
                    s.Garbage -= take;
                    collected.TryGetValue(bl.Assigned.Id, out float sum);
                    collected[bl.Assigned.Id] = sum + take;
                }
                if (s.Garbage < 1e-6f) s.Garbage = 0f;

                float risk = BaseFireRisk(b.Zone) * (1f - 0.8f * s.Coverage[FireChannel]) + 0.2f * GarbageLevel(b);
                risk = b.IsConstructed ? Math.Clamp(risk, 0f, 1f) : 0f;
                if (MathF.Abs(risk - s.FireRisk) > 1e-3f || MathF.Abs(before - s.Garbage) > 0.01f) changed = true;
                s.FireRisk = risk;
            }
            foreach (var f in sortedFacilities)
            {
                if (f.Kind != ServiceKinds.Garbage) continue;
                collected.TryGetValue(f.Id, out float got);
                if (f.Type.Storage > 0f) f.Stored = MathF.Min(f.Type.Storage, f.Stored + got);
                f.CollectedPerDay += (got / dt - f.CollectedPerDay) * 0.2f; // gleitend
            }
            return changed;
        }

        static float BaseFireRisk(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.Industry: return 0.5f;
                case ZoneType.Retail:
                case ZoneType.Office:
                case ZoneType.Mixed: return 0.3f;
                default: return 0.25f;
            }
        }

        void Emit()
        {
            if (environment == null) return;
            bool polluting = false;
            foreach (var f in facilities.Values)
            {
                if (f.Type.NoiseLevel > 0f) environment.Noise.StampRadialMax(f.Position, f.Type.NoiseRadius, f.Type.NoiseLevel);
                if (f.Type.Pollution > 0f) { environment.AirPollution.AddRadial(f.Position, f.Type.PollutionRadius, f.Type.Pollution); polluting = true; }
            }
            // Abklingen macht bisher das UtilitySystem, aber nur, wenn eigene Kraftwerke Abgas machen – sonst hier
            // (gleiche Werte; später ein gemeinsames Umweltsystem)
            bool utilityPolluting = false;
            if (utilities != null) foreach (var f in utilities.Facilities) if (f.Type.Pollution > 0f) { utilityPolluting = true; break; }
            if (!utilityPolluting && (polluting || pollutionActive))
            {
                environment.AirPollution.Diffuse(0.1f);
                environment.AirPollution.Decay(0.08f);
                pollutionActive = polluting || environment.AirPollution.MaxValue() > 0.001f;
            }
        }

        // ------------------------------------------------------------ Straßen

        /// <summary>Neue Straße durch ein Dienstgebäude → es wird abgerissen (wie Gebäude und Versorger).</summary>
        void OnSegmentAdded(RoadSegment s)
        {
            List<int> hit = null;
            foreach (var f in facilities.Values)
                if (ServiceGeometry.RoadCrossesRect(s, f.Position, f.Front, f.Type.Width, f.Type.Depth, -0.5f)) (hit ?? (hit = new List<int>())).Add(f.Id);
            if (hit == null) return;
            hit.Sort();
            foreach (int id in hit) Remove(id);
        }

        /// <summary>Fahrzeit über ein Segment in Sekunden.</summary>
        public static float SegmentSeconds(RoadSegment s) => s.Length / (MathF.Max(10f, s.Type.SpeedLimitKmh) / 3.6f * SpeedFactor);

        public static bool CanDriveForward(RoadSegment s) => s.Type.CarLanesForward > 0 || s.Type.CarLanesBackward == 0;
        public static bool CanDriveBackward(RoadSegment s) => s.Type.CarLanesBackward > 0 || s.Type.CarLanesForward == 0;

        /// <summary>Entfernungswirkung 0..1: voll bis <see cref="FullEffectShare"/> × Reichweite, dann linear auf 0.</summary>
        public static float DistanceFactor(float distance, float range)
        {
            if (float.IsInfinity(distance) || float.IsNaN(distance) || range <= 0f) return 0f;
            float full = range * FullEffectShare;
            if (distance <= full) return 1f;
            return Math.Clamp((range - distance) / (range - full), 0f, 1f);
        }

        /// <summary>Dijkstra über Fahrzeit vom Punkt t auf dem Startsegment (Einbahnstraßen nur vorwärts).</summary>
        ServiceReach ComputeReach(int startSegmentId, float startT, float rangeSeconds)
        {
            var dist = new Dictionary<int, float>();
            var s0 = roads.GetSegment(startSegmentId);
            if (s0 == null) return new ServiceReach(dist, -1, 0f, rangeSeconds);
            var heap = new MinHeap();
            void Relax(int node, float cost)
            {
                if (dist.TryGetValue(node, out float old) && old <= cost) return;
                dist[node] = cost;
                if (cost <= rangeSeconds) heap.Push(cost, node);
            }
            float secs0 = SegmentSeconds(s0);
            if (CanDriveForward(s0)) Relax(s0.End.Id, (1f - startT) * secs0);
            if (CanDriveBackward(s0)) Relax(s0.Start.Id, startT * secs0);

            while (heap.Count > 0)
            {
                var (cost, id) = heap.Pop();
                if (dist[id] < cost) continue; // veralteter Eintrag
                var node = roads.GetNode(id);
                if (node == null) continue;
                float penalty = node.SegmentIds.Count >= 3 ? JunctionPenaltySeconds : 0f;
                foreach (int sid in node.SegmentIds)
                {
                    var s = roads.GetSegment(sid);
                    if (s == null) continue;
                    float secs = SegmentSeconds(s);
                    if (s.Start == node && CanDriveForward(s)) Relax(s.End.Id, cost + penalty + secs);
                    if (s.End == node && CanDriveBackward(s)) Relax(s.Start.Id, cost + penalty + secs);
                }
            }
            return new ServiceReach(dist, startSegmentId, startT, rangeSeconds);
        }

        /// <summary>
        /// Anschlusspunkt: nächste ebenerdige Stadtstraße, deren Rand höchstens <see cref="RoadReach"/> von der Grundfläche
        /// entfernt ist (bei Gleichstand kleinste Segment-Id).
        /// </summary>
        bool FindAccess(Vector2 center, Vector2 front, float width, float depth, out int segmentId, out float t)
        {
            segmentId = -1;
            t = 0f;
            float best = RoadReach + 1e-3f;
            float reach = MathF.Sqrt(width * width + depth * depth) * 0.5f + RoadReach;
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
                    if (MathF.Abs(s.ElevationAt(u)) > 3f) continue; // Brücke/Tunnel: keine Zufahrt
                    float d = ServiceGeometry.RectDistance(s.Evaluate(u), center, front, width, depth) - s.Type.HalfWidth;
                    if (d < best - 1e-4f || (MathF.Abs(d - best) <= 1e-4f && segmentId >= 0 && s.Id < segmentId))
                    {
                        best = d;
                        segmentId = s.Id;
                        t = u;
                    }
                }
            }
            return segmentId >= 0;
        }

        RoadSegment NearestCityRoad(Vector2 p, float maxDistance)
        {
            RoadSegment best = null;
            float bestD = maxDistance;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float reach = s.Type.HalfWidth + maxDistance;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) continue;
                float d = s.DistanceTo(p) - s.Type.HalfWidth;
                if (d < bestD || (d == bestD && best != null && s.Id < best.Id)) { bestD = d; best = s; }
            }
            return best;
        }

        bool InWorld(Vector2 p) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;
    }
}
