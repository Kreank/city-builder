using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    public enum UtilityBuildError
    {
        None,
        OutOfWorld,
        OverlapsRoad,
        OverlapsFacility,
        OnWater,
        NeedsWater,
        TooSteep,
        TooShort,
        Duplicate,
        /// <summary>Kein Maststandort: Fahrbahn/Gehweg zu breit zum Überspannen.</summary>
        PoleOnRoad,
        /// <summary>Kein Maststandort: Wasser breiter als die Spannweite.</summary>
        PoleOnWater,
        /// <summary>Kein Maststandort: Versorger-Bauwerk im Weg.</summary>
        PoleOnFacility,
        /// <summary>Kein Maststandort: bestehendes Gebäude im Weg (wird nicht abgerissen).</summary>
        PoleOnBuilding,
        /// <summary>Stadtkasse: zu wenig Geld oder Baustopp.</summary>
        InsufficientFunds,
    }

    /// <summary>Geplantes Bauwerk (Vorschau) – erst <see cref="UtilitySystem.BuildFacility"/> baut es.</summary>
    public sealed class FacilityPlan
    {
        public UtilityFacilityType Type;
        public Vector2 Position;
        public Vector2 Front;
        public float Height;
        public float Cost;
        public UtilityBuildError Error;
        /// <summary>Gebäude, die für das Bauwerk abgerissen würden.</summary>
        public readonly List<Building> Displaced = new List<Building>();
        public bool IsValid => Error == UtilityBuildError.None;
    }

    /// <summary>Geplante Leitung/Rohr (Vorschau).</summary>
    public sealed class LinePlan
    {
        public UtilityKind Kind;
        public Vector2 A, B;
        public float Cost;
        public UtilityBuildError Error;
        /// <summary>Geplante Masten (nur Strom), ungültige mit <see cref="PolePlan.Error"/> (Vorschau rot).</summary>
        public readonly List<PolePlan> Poles = new List<PolePlan>();
        public bool IsValid => Error == UtilityBuildError.None;
        public float Length => Vector2.Distance(A, B);
    }

    /// <summary>Woran ein Leitungsende eingerastet ist.</summary>
    public enum UtilitySnap { None, LineEnd, Line, Facility, Road, Building }

    /// <summary>
    /// Strom &amp; Wasser (G3): Versorger-Bauwerke, frei verlegte Leitungen/Rohre und die Netze.
    /// <para>
    /// Netzzusammenhang per Union-Find über einen Graphen aus Stadtstraßen (tragen Leitung und Rohr automatisch
    /// mit, wie in Cities: Skylines), frei verlegten Leitungen/Rohren, Bauwerken und Gebäuden. Neu berechnet nur,
    /// wenn sich Straßen, Gebäude oder Versorgung geändert haben. Pro Netz: Erzeugung gegen Bedarf; bei Unterdeckung
    /// werden die ältesten Gebäude (kleinste Id) zuerst versorgt, der Rest bleibt unversorgt.
    /// </para>
    /// <para>
    /// Folge: Ein Gebäude, das länger als <see cref="AbandonAfterTicks"/> ohne Strom oder Wasser ist, wird verlassen
    /// und abgerissen – aber erst, wenn die Stadt überhaupt einen Versorger dieser Art hat (Startphase ohne Kraftwerk
    /// bleibt folgenlos). Die Stadt-Simulation kann den Zustand über <see cref="IsSupplied(Building)"/> abfragen.
    /// </para>
    /// </summary>
    public sealed partial class UtilitySystem : ISimulationSystem
    {
        /// <summary>Alle 5 Ticks = alle 30 Spielminuten (wie die Stadt).</summary>
        public int TickInterval => 5;

        /// <summary>Nach 2 Spieltagen ohne Versorgung wird ein Gebäude verlassen.</summary>
        public const int AbandonAfterTicks = 2 * SimulationClock.TicksPerDay;
        /// <summary>Abwasser (G3 erweitert): ohne Entsorgung hält man es länger aus – verlassen nach 4 Spieltagen.</summary>
        public const int SewageAbandonAfterTicks = 4 * SimulationClock.TicksPerDay;
        /// <summary>Leitungsenden verbinden sich mit allem in diesem Abstand.</summary>
        public const float ConnectRadius = 3f;
        /// <summary>Ein Bauwerk ist an eine Straße angeschlossen, wenn deren Rand höchstens so weit entfernt ist.</summary>
        public const float RoadReach = 8f;
        public const float MinLineLength = 4f;
        /// <summary>Größter Höhenunterschied auf der Grundfläche (wird eingeebnet).</summary>
        public const float MaxFacilitySlope = 8f;
        const float PollutionDecay = 0.08f;
        const float PollutionDiffuse = 0.1f;

        readonly RoadNetwork roads;
        readonly TerrainMap terrain;
        readonly CitySystem city;
        readonly EnvironmentLayers environment;
        readonly Vector2 min, max;

        readonly Dictionary<int, UtilityFacility> facilities = new Dictionary<int, UtilityFacility>();
        readonly Dictionary<int, UtilityLine> lines = new Dictionary<int, UtilityLine>();
        readonly Dictionary<int, UtilityStatus> status = new Dictionary<int, UtilityStatus>();
        // Je Art (Strom, Wasser, Abwasser) – Index = (int)UtilityKind
        readonly List<UtilityNetwork>[] networks = { new List<UtilityNetwork>(), new List<UtilityNetwork>(), new List<UtilityNetwork>() };
        /// <summary>Je Art und Netz: Gebäude (nach Id sortiert).</summary>
        readonly List<List<Building>>[] networkBuildings = { new List<List<Building>>(), new List<List<Building>>(), new List<List<Building>>() };
        /// <summary>Je Art: Gebäude-Id → Netz-Id.</summary>
        readonly Dictionary<int, int>[] buildingNetwork = { new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>() };
        int nextFacilityId = 1, nextLineId = 1;
        int builtRoadVersion = -1, builtCityVersion = -1, builtStructureVersion = -1;

        /// <summary>Wird bei jeder Änderung an Bauwerken/Leitungen erhöht (für die Darstellung).</summary>
        public int StructureVersion { get; private set; }
        /// <summary>Wird erhöht, wenn sich Netze oder Versorgungszustände geändert haben.</summary>
        public int StatusVersion { get; private set; }
        /// <summary>Folge „verlassen“ aktiv (abschaltbar, z. B. für Tests oder einen Sandbox-Modus).</summary>
        public bool AbandonmentEnabled { get; set; } = true;
        /// <summary>Wie viele Gebäude bisher wegen fehlender Versorgung verlassen wurden.</summary>
        public int AbandonedBuildings { get; private set; }

        // --- Stadtkasse (Hook): prüft/bucht Bau- und Abrisskosten. null = kostenlos. Gesetzt von SimulationWorld.
        public Economy.IConstructionBudget Budget { get; set; }
        bool Affordable(float cost) => Budget == null || Budget.CanAfford(cost);
        // --- Ende Stadtkasse

        public IEnumerable<UtilityFacility> Facilities => facilities.Values;
        public IEnumerable<UtilityLine> Lines => lines.Values;
        public IReadOnlyList<UtilityNetwork> Networks(UtilityKind kind) => networks[(int)kind];
        public int FacilityCount => facilities.Count;
        public int LineCount => lines.Count;

        public event Action<UtilityFacility> FacilityAdded;
        public event Action<UtilityFacility> FacilityRemoved;
        public event Action<Building> BuildingAbandoned;

        public UtilitySystem(RoadNetwork roads, TerrainMap terrain, CitySystem city, EnvironmentLayers environment, Vector2 min, Vector2 max)
        {
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.city = city ?? throw new ArgumentNullException(nameof(city));
            this.environment = environment;
            this.min = min;
            this.max = max;
            roads.SegmentAdded += OnSegmentAdded;
            city.BuildingRemoved += b => status.Remove(b.Id);
            // Auf Versorger-Flächen und Mast-Grundflächen wachsen keine Gebäude (8-m-Zelle: halbe Zelle Rand)
            city.LotBlocker = p => FacilityAt(p, ZoneGrid.CellSize * 0.5f) != null || PoleNear(p, ZoneGrid.CellSize * 0.5f + PoleFootprintHalf) != null;
        }

        public UtilityFacility GetFacility(int id) => facilities.TryGetValue(id, out var f) ? f : null;
        public UtilityLine GetLine(int id) => lines.TryGetValue(id, out var l) ? l : null;

        // ------------------------------------------------------------ Abfragen für die Stadt

        /// <summary>Versorgungszustand eines Gebäudes (Gebäude im Bau: alles false, Dauer 0).</summary>
        public UtilityStatus GetStatus(Building b) => b != null && status.TryGetValue(b.Id, out var s) ? s : default;

        /// <summary>Hat das Gebäude Strom UND Wasser?</summary>
        public bool IsSupplied(Building b) { var s = GetStatus(b); return s.HasPower && s.HasWater; }

        public bool IsSupplied(Building b, UtilityKind kind) => GetStatus(b).Has(kind);

        /// <summary>
        /// Gibt es in der Stadt mindestens einen Versorger dieser Art? Vorher fehlt die Versorgung „planmäßig“
        /// (Startphase) und hat keine Folgen.
        /// </summary>
        public bool IsRequired(UtilityKind kind)
        {
            foreach (var f in facilities.Values) if (f.Kind == kind) return true;
            return false;
        }

        /// <summary>Für Nachfrage/Zuzug: versorgt – oder die Art wird noch gar nicht verlangt (Startphase).</summary>
        public bool IsSatisfied(Building b)
        {
            var s = GetStatus(b);
            return (s.HasPower || !IsRequired(UtilityKind.Power)) && (s.HasWater || !IsRequired(UtilityKind.Water))
                   && (s.HasSewage || !IsRequired(UtilityKind.Sewage)); // Abwasser: erst mit Kläranlage/Auslass verlangt
        }

        /// <summary>Netz, in dem das Gebäude hängt (null = keins).</summary>
        public UtilityNetwork NetworkOf(Building b, UtilityKind kind)
        {
            if (b == null || !buildingNetwork[(int)kind].TryGetValue(b.Id, out int id)) return null;
            return id >= 0 && id < networks[(int)kind].Count ? networks[(int)kind][id] : null;
        }

        public float TotalProduction(UtilityKind kind) { float s = 0f; foreach (var n in networks[(int)kind]) s += n.Production; return s; }
        // --- G5 Warenketten (Hook): Verbrauch eines Gebäudes aus seiner Firma (Rezept × Arbeitsplätze); NaN/null = Katalogwert.
        // Gesetzt in GoodsBinding.Connect (GoodsSystem.UtilityDemand).
        public Func<Building, UtilityKind, float> DemandProvider { get; set; }

        /// <summary>Verbrauch eines fertigen Gebäudes (kW bzw. m³/h): Firma nach Rezept, sonst <see cref="UtilityCatalog.Demand"/>.</summary>
        public float DemandOf(Building b, UtilityKind kind)
        {
            if (b == null || !b.IsConstructed) return 0f;
            // Abwasser folgt immer dem tatsächlichen Wasserverbrauch (auch Industrie nach Rezept, G5)
            if (kind == UtilityKind.Sewage) return DemandOf(b, UtilityKind.Water) * UtilityCatalog.SewageShareOfWater;
            float v = DemandProvider != null ? DemandProvider(b, kind) : float.NaN;
            return float.IsNaN(v) ? UtilityCatalog.Demand(b, kind) : v;
        }
        // --- Ende G5

        public float TotalDemand(UtilityKind kind) { float s = 0f; foreach (var n in networks[(int)kind]) s += n.Demand; return s; }
        public float TotalUpkeepPerDay { get { float s = 0f; foreach (var f in facilities.Values) s += f.Type.UpkeepPerDay; return s; } }

        // ------------------------------------------------------------ Bauwerke planen/bauen/abreißen

        public FacilityPlan PlanFacility(UtilityFacilityType type, Vector2 position, float rotationDegrees = 0f)
        {
            float a = rotationDegrees * MathF.PI / 180f;
            var plan = new FacilityPlan { Type = type, Position = position, Front = new Vector2(MathF.Sin(a), MathF.Cos(a)), Cost = type.Cost };
            plan.Error = CheckFacility(plan);
            if (plan.Error == UtilityBuildError.None && !Affordable(plan.Cost)) plan.Error = UtilityBuildError.InsufficientFunds;
            plan.Height = MathF.Max(terrain.HeightAt(position), terrain.WaterLevel + 0.5f);
            if (plan.Error == UtilityBuildError.None)
                foreach (var b in city.Buildings)
                    if (RectsOverlap(position, plan.Front, type.Width, type.Depth, b.Position, b.Front, b.Width, b.Depth)) plan.Displaced.Add(b);
            return plan;
        }

        UtilityBuildError CheckFacility(FacilityPlan plan)
        {
            var type = plan.Type;
            var corners = UtilityGeometry.Corners(plan.Position, plan.Front, type.Width, type.Depth);
            foreach (var c in corners) if (!InWorld(c)) return UtilityBuildError.OutOfWorld;

            float lo = terrain.HeightAt(plan.Position), hi = lo;
            foreach (var c in corners) { float h = terrain.HeightAt(c); lo = MathF.Min(lo, h); hi = MathF.Max(hi, h); }

            if (type.NeedsWater)
            {
                // Pumpe steht an Land, Wasser muss in Reichweite sein (Ufer)
                if (terrain.IsWater(plan.Position)) return UtilityBuildError.OnWater;
                // Atom (G3 erweitert): große Bauwerke am Ufer brauchen trotzdem eine trockene, ebene Grundfläche
                if (type.DryFootprint)
                {
                    foreach (var c in corners) if (terrain.IsWater(c)) return UtilityBuildError.OnWater;
                    if (hi - lo > MaxFacilitySlope) return UtilityBuildError.TooSteep;
                }
                if (!WaterNear(plan.Position, MathF.Max(type.Width, type.Depth) * 0.5f + 20f)) return UtilityBuildError.NeedsWater;
            }
            else
            {
                if (terrain.IsWater(plan.Position)) return UtilityBuildError.OnWater;
                foreach (var c in corners) if (terrain.IsWater(c)) return UtilityBuildError.OnWater;
                if (hi - lo > MaxFacilitySlope) return UtilityBuildError.TooSteep;
            }

            foreach (var s in roads.Segments)
                if (RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, 0f)) return UtilityBuildError.OverlapsRoad;
            foreach (var f in facilities.Values)
                if (RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, f.Position, f.Front, f.Type.Width, f.Type.Depth))
                    return UtilityBuildError.OverlapsFacility;
            if (OtherStructureOverlaps != null && OtherStructureOverlaps(plan.Position, plan.Front, type.Width, type.Depth))
                return UtilityBuildError.OverlapsFacility; // G4-Hook: Dienstgebäude
            return UtilityBuildError.None;
        }

        // --- G4 Dienste (Hooks, gesetzt vom ServiceSystem): andere Bauwerke, auf die weder Versorger noch Masten dürfen.
        /// <summary>Überlappt das Rechteck (Mitte, Tiefenrichtung, Breite, Tiefe) ein fremdes Bauwerk?</summary>
        public Func<Vector2, Vector2, float, float, bool> OtherStructureOverlaps { get; set; }
        /// <summary>Liegt ein fremdes Bauwerk am Punkt (mit Rand)?</summary>
        public Func<Vector2, float, bool> OtherStructureAt { get; set; }
        // --- Ende G4

        bool WaterNear(Vector2 p, float radius)
        {
            for (float r = 4f; r <= radius; r += 4f)
            {
                int n = Math.Max(8, (int)(r * 0.8f));
                for (int i = 0; i < n; i++)
                {
                    float a = i * MathF.PI * 2f / n;
                    if (terrain.IsWater(p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r)) return true;
                }
            }
            return false;
        }

        public UtilityFacility BuildFacility(FacilityPlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.UtilityConstruction, plan.Cost);
            foreach (var b in plan.Displaced) city.RemoveBuilding(b);
            var f = new UtilityFacility
            {
                Id = nextFacilityId++,
                Type = plan.Type,
                Position = plan.Position,
                Front = plan.Front,
                Height = plan.Height,
                Output = plan.Type.Capacity,
            };
            if (!plan.Type.NeedsWater || plan.Type.DryFootprint) terrain.FlattenArea(f.Position, f.Front, plan.Type.Width, plan.Type.Depth, f.Height);
            facilities[f.Id] = f;
            StructureVersion++;
            FacilityAdded?.Invoke(f);
            // Masten auf der neuen Fläche: Leitung neu trassieren (oder abreißen, wenn es nicht geht)
            RerouteLines(p => f.Contains(p, PoleFootprintHalf));
            return f;
        }

        /// <summary>Kurzform für Werkzeuge/Tests: planen und bauen, wenn gültig.</summary>
        public UtilityFacility TryBuildFacility(UtilityFacilityType type, Vector2 position, float rotationDegrees = 0f) =>
            BuildFacility(PlanFacility(type, position, rotationDegrees));

        /// <summary>Spieler-Abriss: entfernt das Bauwerk und schreibt den Abrisserlös gut.</summary>
        public bool DemolishFacility(int id)
        {
            if (!facilities.TryGetValue(id, out var f)) return false;
            RemoveFacility(id);
            Budget?.Refund(f.Type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        public bool RemoveFacility(int id)
        {
            if (!facilities.TryGetValue(id, out var f)) return false;
            facilities.Remove(id);
            StructureVersion++;
            FacilityRemoved?.Invoke(f);
            return true;
        }

        public UtilityFacility FacilityAt(Vector2 p, float margin = 0f)
        {
            foreach (var f in facilities.Values)
                if (f.Contains(p, margin)) return f;
            return null;
        }

        // ------------------------------------------------------------ Leitungen/Rohre

        public LinePlan PlanLine(UtilityKind kind, Vector2 a, Vector2 b)
        {
            var plan = new LinePlan { Kind = kind, A = a, B = b };
            plan.Cost = plan.Length * UtilityCatalog.LineCostPerMeter(kind);
            if (!InWorld(a) || !InWorld(b)) plan.Error = UtilityBuildError.OutOfWorld;
            else if (plan.Length < MinLineLength) plan.Error = UtilityBuildError.TooShort;
            else
                foreach (var l in lines.Values)
                    if (l.Kind == kind && ((Vector2.Distance(l.A, a) < 1f && Vector2.Distance(l.B, b) < 1f) ||
                                           (Vector2.Distance(l.A, b) < 1f && Vector2.Distance(l.B, a) < 1f)))
                    { plan.Error = UtilityBuildError.Duplicate; break; }
            // Stromleitungen brauchen Masten (Wasserrohre liegen unterirdisch und dürfen unter Straßen)
            if (kind == UtilityKind.Power && plan.Error == UtilityBuildError.None)
                plan.Error = PlanPoles(a, b, plan.Poles);
            if (plan.Error == UtilityBuildError.None && !Affordable(plan.Cost)) plan.Error = UtilityBuildError.InsufficientFunds;
            return plan;
        }

        public UtilityLine BuildLine(LinePlan plan)
        {
            if (plan == null || !plan.IsValid || !Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.UtilityConstruction, plan.Cost);
            var l = new UtilityLine { Id = nextLineId++, Kind = plan.Kind, A = plan.A, B = plan.B, Poles = PolePositions(plan.Poles) };
            lines[l.Id] = l;
            StructureVersion++;
            return l;
        }

        public UtilityLine TryBuildLine(UtilityKind kind, Vector2 a, Vector2 b) => BuildLine(PlanLine(kind, a, b));

        /// <summary>Spieler-Abriss: entfernt die Leitung und schreibt den Abrisserlös gut.</summary>
        public bool DemolishLine(int id)
        {
            if (!lines.TryGetValue(id, out var l)) return false;
            RemoveLine(id);
            Budget?.Refund(l.Length * UtilityCatalog.LineCostPerMeter(l.Kind) * Economy.EconomyRules.DemolitionRefundShare);
            return true;
        }

        public bool RemoveLine(int id)
        {
            if (!lines.TryGetValue(id, out var l)) return false;
            lines.Remove(id);
            StructureVersion++;
            LineRemoved?.Invoke(l);
            return true;
        }

        /// <summary>Nächste Leitung der Art (oder jeder Art bei null) im Umkreis.</summary>
        public UtilityLine LineNear(Vector2 p, float maxDistance, UtilityKind? kind = null)
        {
            UtilityLine best = null;
            float bestD = maxDistance;
            foreach (var l in lines.Values)
            {
                if (kind.HasValue && l.Kind != kind.Value) continue;
                float d = l.DistanceTo(p);
                if (d <= bestD) { bestD = d; best = l; }
            }
            return best;
        }

        /// <summary>
        /// Rastet einen Punkt für ein Leitungsende ein: Leitungsende → Bauwerk der Art (Rand) → Leitung → Straße → Gebäude.
        /// </summary>
        public Vector2 ResolveLinePoint(UtilityKind kind, Vector2 p, float snap, out UtilitySnap what)
        {
            what = UtilitySnap.None;
            Vector2 best = p;
            float bestD = snap;
            foreach (var l in lines.Values)
            {
                if (l.Kind != kind) continue;
                foreach (var e in new[] { l.A, l.B })
                {
                    float d = Vector2.Distance(e, p);
                    if (d <= bestD) { bestD = d; best = e; what = UtilitySnap.LineEnd; }
                }
            }
            if (what != UtilitySnap.None) return best;

            foreach (var f in facilities.Values)
            {
                if (f.Kind != kind || !f.Contains(p, snap)) continue;
                // Auf den Rand der Grundfläche legen
                var d = p - f.Position;
                float x = Math.Clamp(Vector2.Dot(d, f.Side), -f.Type.Width * 0.5f, f.Type.Width * 0.5f);
                float y = Math.Clamp(Vector2.Dot(d, f.Front), -f.Type.Depth * 0.5f, f.Type.Depth * 0.5f);
                what = UtilitySnap.Facility;
                return f.Position + f.Side * x + f.Front * y;
            }

            var line = LineNear(p, snap, kind);
            if (line != null)
            {
                var ab = line.B - line.A;
                float t = Math.Clamp(Vector2.Dot(p - line.A, ab) / MathF.Max(1e-6f, ab.LengthSquared()), 0f, 1f);
                what = UtilitySnap.Line;
                return line.A + ab * t;
            }

            var seg = NearestCityRoad(p, snap);
            if (seg != null)
            {
                what = UtilitySnap.Road;
                return seg.Evaluate(seg.ClosestT(p));
            }
            if (city.BuildingAt(p) != null) what = UtilitySnap.Building;
            return p;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            if (roads.Version != builtRoadVersion || city.Version != builtCityVersion || StructureVersion != builtStructureVersion)
            {
                Rebuild(UtilityKind.Power);
                Rebuild(UtilityKind.Water);
                Rebuild(UtilityKind.Sewage); // Abwasser (G3 erweitert)
                builtRoadVersion = roads.Version;
                builtCityVersion = city.Version;
                builtStructureVersion = StructureVersion;
                StatusVersion++;
            }
            UpdateIncidents(world); // Atom (G3 erweitert): seltener Störfall, s. UtilitySystem.Nuclear.cs
            foreach (var f in facilities.Values) f.Output = f.Type.Capacity * OutputFactor(f); // später: Wind/Sonne/Brennstoff
            bool changed = Distribute(UtilityKind.Power) | Distribute(UtilityKind.Water) | Distribute(UtilityKind.Sewage);
            if (changed) StatusVersion++;
            UpdateDurations();
            Abandon();
            Emit();
        }

        /// <summary>Für Tests/Werkzeuge: sofort neu rechnen (ohne Uhr).</summary>
        public void ForceTick() => Tick(null);

        void Rebuild(UtilityKind kind)
        {
            int k = (int)kind;
            var uf = new UnionFind();

            // Stadtstraßen tragen Leitung und Rohr: Segmente an gemeinsamen Knoten sind verbunden
            var segIndex = new Dictionary<int, int>();
            foreach (var s in roads.Segments)
                if (ConnectionRules.CarriedByRoads(kind) && CarriesUtilities(s)) segIndex[s.Id] = uf.Add(); // Abwasser: nicht (selbst verlegt)
            foreach (var n in roads.Nodes)
            {
                int first = -1;
                foreach (int id in n.SegmentIds)
                {
                    if (!segIndex.TryGetValue(id, out int i)) continue;
                    if (first < 0) first = i; else uf.Union(first, i);
                }
            }

            // Gebäude hängen an ihrer Straße (Vorderseite)
            var sorted = new List<Building>(city.Buildings);
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            var bIndex = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                var b = sorted[i];
                bIndex[i] = uf.Add();
                int segId = b.FrontSegmentId;
                if (!segIndex.ContainsKey(segId))
                {
                    var near = NearestCityRoad(b.Entrance, ConnectRadius);
                    segId = near != null ? near.Id : -1;
                }
                if (segId >= 0 && segIndex.TryGetValue(segId, out int si)) uf.Union(bIndex[i], si);
            }

            // Bauwerke: an Straßen in Reichweite
            var fList = new List<UtilityFacility>();
            foreach (var f in facilities.Values) if (f.Kind == kind) fList.Add(f);
            fList.Sort((a, b) => a.Id.CompareTo(b.Id));
            var fIndex = new int[fList.Count];
            for (int i = 0; i < fList.Count; i++)
            {
                fIndex[i] = uf.Add();
                var f = fList[i];
                foreach (var s in roads.Segments)
                    if (segIndex.TryGetValue(s.Id, out int si) && RoadCrossesRect(s, f.Position, f.Front, f.Type.Width, f.Type.Depth, RoadReach))
                        uf.Union(fIndex[i], si);
            }

            // Leitungen: Enden verbinden sich mit Leitungen, Bauwerken, Straßen und Gebäuden
            var lList = new List<UtilityLine>();
            foreach (var l in lines.Values) if (l.Kind == kind) lList.Add(l);
            lList.Sort((a, b) => a.Id.CompareTo(b.Id));
            var lIndex = new int[lList.Count];
            for (int i = 0; i < lList.Count; i++) lIndex[i] = uf.Add();
            var buildingPos = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++) buildingPos[sorted[i].Id] = i;
            for (int i = 0; i < lList.Count; i++)
            {
                var l = lList[i];
                foreach (var e in new[] { l.A, l.B })
                {
                    for (int j = 0; j < lList.Count; j++)
                        if (j != i && lList[j].DistanceTo(e) <= ConnectRadius) uf.Union(lIndex[i], lIndex[j]);
                    for (int j = 0; j < fList.Count; j++)
                        if (fList[j].Contains(e, ConnectRadius)) uf.Union(lIndex[i], fIndex[j]);
                    var seg = NearestCityRoad(e, ConnectRadius);
                    if (seg != null && segIndex.TryGetValue(seg.Id, out int segI)) uf.Union(lIndex[i], segI);
                    var b = city.BuildingAt(e);
                    if (b != null && buildingPos.TryGetValue(b.Id, out int bi)) uf.Union(lIndex[i], bIndex[bi]);
                }
            }

            // Selbst verlegte Netze (Abwasser): Gebäude hängen an jeder Leitung in Anschlussreichweite ihrer Zufahrt
            if (!ConnectionRules.CarriedByRoads(kind) && lList.Count > 0)
            {
                float reach = ConnectionRules.BuildingReach;
                for (int j = 0; j < lList.Count; j++)
                {
                    var l = lList[j];
                    float x0 = MathF.Min(l.A.X, l.B.X) - reach, x1 = MathF.Max(l.A.X, l.B.X) + reach;
                    float y0 = MathF.Min(l.A.Y, l.B.Y) - reach, y1 = MathF.Max(l.A.Y, l.B.Y) + reach;
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var e = sorted[i].Entrance;
                        if (e.X < x0 || e.X > x1 || e.Y < y0 || e.Y > y1) continue;
                        if (ConnectionRules.Reaches(l, sorted[i])) uf.Union(bIndex[i], lIndex[j]);
                    }
                }
            }

            // Netze: je Wurzel eins (nur mit Gebäude, Bauwerk oder Leitung), Ids in fester Reihenfolge
            var list = networks[k];
            var members = networkBuildings[k];
            var map = buildingNetwork[k];
            list.Clear();
            members.Clear();
            map.Clear();
            var rootToNet = new Dictionary<int, int>();
            int NetFor(int element)
            {
                int root = uf.Find(element);
                if (rootToNet.TryGetValue(root, out int id)) return id;
                id = list.Count;
                rootToNet[root] = id;
                list.Add(new UtilityNetwork { Id = id, Kind = kind });
                members.Add(new List<Building>());
                return id;
            }
            for (int i = 0; i < fList.Count; i++)
            {
                int id = NetFor(fIndex[i]);
                fList[i].NetworkId = id;
                list[id].Facilities++;
            }
            for (int i = 0; i < lList.Count; i++)
            {
                int id = NetFor(lIndex[i]);
                lList[i].NetworkId = id;
                list[id].Lines++;
            }
            for (int i = 0; i < sorted.Count; i++)
            {
                int id = NetFor(bIndex[i]);
                map[sorted[i].Id] = id;
                members[id].Add(sorted[i]);
                list[id].Buildings++;
            }
        }

        /// <summary>Verteilt die Erzeugung je Netz. Liefert true, wenn sich ein Versorgungszustand geändert hat.</summary>
        bool Distribute(UtilityKind kind)
        {
            int k = (int)kind;
            bool changed = false;
            var list = networks[k];
            foreach (var n in list) { n.Production = 0f; n.Demand = 0f; n.Delivered = 0f; n.SuppliedBuildings = 0; }
            foreach (var f in facilities.Values)
                if (f.Kind == kind && f.NetworkId >= 0 && f.NetworkId < list.Count) list[f.NetworkId].Production += f.Output;

            for (int id = 0; id < list.Count; id++)
            {
                var n = list[id];
                float remaining = n.Production;
                // Älteste Gebäude zuerst; passt ein großes nicht mehr, können kleinere noch versorgt werden
                foreach (var b in networkBuildings[k][id])
                {
                    float demand = DemandOf(b, kind); // G5: Industrie nach Rezept
                    n.Demand += demand;
                    bool supplied = demand > 0f && demand <= remaining + 1e-4f;
                    if (supplied) { remaining -= demand; n.Delivered += demand; n.SuppliedBuildings++; }

                    status.TryGetValue(b.Id, out var s);
                    bool old = s.Has(kind);
                    if (kind == UtilityKind.Power) { s.HasPower = supplied; s.PowerNetwork = id; }
                    else if (kind == UtilityKind.Sewage) { s.HasSewage = supplied; s.SewageNetwork = id; }
                    else { s.HasWater = supplied; s.WaterNetwork = id; }
                    status[b.Id] = s;
                    if (old != supplied) changed = true;
                }
            }
            return changed;
        }

        void UpdateDurations()
        {
            // Dauer zählt erst, wenn die Art verlangt wird – sonst würde das erste Kraftwerk sofort alle Altbauten räumen
            bool power = IsRequired(UtilityKind.Power), water = IsRequired(UtilityKind.Water), sewage = IsRequired(UtilityKind.Sewage);
            foreach (var b in city.Buildings)
            {
                if (!status.TryGetValue(b.Id, out var s)) continue;
                if (!b.IsConstructed) { status[b.Id] = default; continue; }
                s.TicksWithoutPower = s.HasPower || !power ? 0 : s.TicksWithoutPower + TickInterval;
                s.TicksWithoutWater = s.HasWater || !water ? 0 : s.TicksWithoutWater + TickInterval;
                s.TicksWithoutSewage = s.HasSewage || !sewage ? 0 : s.TicksWithoutSewage + TickInterval;
                status[b.Id] = s;
            }
        }

        void Abandon()
        {
            if (!AbandonmentEnabled) return;
            bool power = IsRequired(UtilityKind.Power), water = IsRequired(UtilityKind.Water), sewage = IsRequired(UtilityKind.Sewage);
            if (!power && !water && !sewage) return;
            List<Building> leave = null;
            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed || !status.TryGetValue(b.Id, out var s)) continue;
                if ((power && s.TicksWithoutPower >= AbandonAfterTicks) || (water && s.TicksWithoutWater >= AbandonAfterTicks)
                    || (sewage && s.TicksWithoutSewage >= SewageAbandonAfterTicks))
                    (leave ?? (leave = new List<Building>())).Add(b);
            }
            if (leave == null) return;
            foreach (var b in leave)
            {
                if (!city.RemoveBuilding(b)) continue;
                AbandonedBuildings++;
                BuildingAbandoned?.Invoke(b);
            }
        }

        /// <summary>Lärm und Abgas der Bauwerke in die Umwelt-Raster (läuft nach dem NoiseSystem, das neu zeichnet).</summary>
        void Emit()
        {
            if (environment == null) return;
            bool polluting = false;
            foreach (var f in facilities.Values)
            {
                if (f.Type.NoiseLevel > 0f) environment.Noise.StampRadialMax(f.Position, f.Type.NoiseRadius, f.Type.NoiseLevel);
                if (f.Type.Pollution > 0f)
                {
                    environment.AirPollution.AddRadial(f.Position, f.Type.PollutionRadius, f.Type.Pollution);
                    polluting = true;
                }
            }
            // Abgas baut sich ab und verteilt sich (bisher einzige Quelle – später eigenes Umweltsystem)
            if (polluting || pollutionActive)
            {
                environment.AirPollution.Diffuse(PollutionDiffuse);
                environment.AirPollution.Decay(PollutionDecay);
                pollutionActive = polluting || environment.AirPollution.MaxValue() > 0.001f;
            }
        }

        bool pollutionActive;

        // ------------------------------------------------------------ Straßenänderungen

        /// <summary>
        /// Neue Straße durch ein Bauwerk → Bauwerk wird abgerissen (wie bei Gebäuden).
        /// Neue Straße durch einen Mast → Leitung wird neu trassiert, geht das nicht, wird sie abgerissen.
        /// </summary>
        void OnSegmentAdded(RoadSegment s)
        {
            List<int> hit = null;
            foreach (var f in facilities.Values)
                if (RoadCrossesRect(s, f.Position, f.Front, f.Type.Width, f.Type.Depth, -0.5f)) (hit ?? (hit = new List<int>())).Add(f.Id);
            if (hit != null) foreach (int id in hit) RemoveFacility(id);
            RerouteLines(p => RoadBlocksPole(s, p));
        }

        // ------------------------------------------------------------ Geometrie

        /// <summary>Nur ebenerdige Stadtstraßen (inkl. Brücken/Tunnel der Stadtstraßen) tragen Leitungen – Autobahnen nicht.</summary>
        static bool CarriesUtilities(RoadSegment s) => s.Type.Category == RoadCategory.City;

        RoadSegment NearestCityRoad(Vector2 p, float maxDistance)
        {
            RoadSegment best = null;
            float bestD = maxDistance;
            foreach (var s in roads.Segments)
            {
                if (!CarriesUtilities(s)) continue;
                float reach = s.Type.HalfWidth + maxDistance;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (p.X < lo.X - reach || p.Y < lo.Y - reach || p.X > hi.X + reach || p.Y > hi.Y + reach) continue;
                float d = s.DistanceTo(p) - s.Type.HalfWidth;
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// Berührt die Fahrbahn (ebenerdige Abschnitte; Brücken/Tunnel gehen drüber/drunter weg) das Rechteck,
        /// erweitert um <paramref name="margin"/>?
        /// </summary>
        static bool RoadCrossesRect(RoadSegment s, Vector2 center, Vector2 front, float width, float depth, float margin)
        {
            float reach = MathF.Sqrt(width * width + depth * depth) * 0.5f + s.Type.HalfWidth + margin + 1f;
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
            if (center.X < lo.X - reach || center.Y < lo.Y - reach || center.X > hi.X + reach || center.Y > hi.Y + reach) return false;
            int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                if (MathF.Abs(s.ElevationAt(t)) > 3f) continue;
                if (UtilityGeometry.InRect(s.Evaluate(t), center, front, width, depth, s.Type.HalfWidth + margin)) return true;
            }
            return false;
        }

        /// <summary>Überlappen sich zwei gedrehte Rechtecke? (Trennachsen-Test)</summary>
        static bool RectsOverlap(Vector2 c1, Vector2 f1, float w1, float d1, Vector2 c2, Vector2 f2, float w2, float d2)
        {
            var a = UtilityGeometry.Corners(c1, f1, w1, d1);
            var b = UtilityGeometry.Corners(c2, f2, w2, d2);
            foreach (var axis in new[] { f1, new Vector2(-f1.Y, f1.X), f2, new Vector2(-f2.Y, f2.X) })
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in a) { float v = Vector2.Dot(p, axis); amin = MathF.Min(amin, v); amax = MathF.Max(amax, v); }
                foreach (var p in b) { float v = Vector2.Dot(p, axis); bmin = MathF.Min(bmin, v); bmax = MathF.Max(bmax, v); }
                if (amax <= bmin + 0.01f || bmax <= amin + 0.01f) return false;
            }
            return true;
        }

        bool InWorld(Vector2 p) => p.X >= min.X && p.Y >= min.Y && p.X <= max.X && p.Y <= max.Y;
    }
}
