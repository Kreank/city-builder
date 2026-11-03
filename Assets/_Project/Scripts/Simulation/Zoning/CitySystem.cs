using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Nachfrage je Zonenart (0..100), wie die RCI-Balken in Cities: Skylines.</summary>
    public sealed class ZoneDemand
    {
        readonly float[] values = new float[8];
        public float this[ZoneType z] { get => values[(int)z]; internal set => values[(int)z] = Math.Clamp(value, 0f, 100f); }
    }

    public sealed class CityStats
    {
        public int Buildings;
        public int UnderConstruction;
        public int Households;
        public int OccupiedHouseholds;
        public int Population;
        public int Jobs;
        public int Workers;
        public int ZonedCells;
        public int FreeZonedCells;
    }

    /// <summary>
    /// Arbeitsmarkt und Wohnungen aus den einzelnen Bürgern (G2). Wird vom Bürgersystem gefüllt,
    /// die Stadt leitet daraus die Nachfrage ab – so kennt die Stadt keine Bürger-Typen.
    /// </summary>
    public sealed class PopulationInfo
    {
        /// <summary>Einwohner (ohne Einpendler).</summary>
        public int Residents;
        /// <summary>Einwohner im Erwerbsalter (18–64).</summary>
        public int WorkingAge;
        public int Unemployed;
        /// <summary>Davon mit mittlerem/höherem Abschluss (für Büro-Nachfrage).</summary>
        public int UnemployedEducated;
        /// <summary>Einwohner, die außerhalb arbeiten (würden lieber hier arbeiten).</summary>
        public int CommutersOut;
        /// <summary>Arbeitskräfte von außerhalb auf hiesigen Jobs (könnten durch Einwohner ersetzt werden).</summary>
        public int CommutersIn;
        public int VacantJobs;
        public int FreeHomes;
        /// <summary>Einwohner, die einkaufen wollen, aber kein Geschäft erreichen.</summary>
        public int UnservedShoppers;
    }

    /// <summary>
    /// Stadt: Zonen malen, Nachfrage, wachsende Gebäude (Grundstückswahl, Einebnen, Bau)
    /// und Abriss, wenn ein Gebäude die Straßenanbindung verliert oder eine Straße hindurchgebaut wird.
    /// Einzug, Jobs und Pendeln übernimmt seit G2 das Bürgersystem (<c>Citizens.CitizenSystem</c>);
    /// die Nachfrage kommt aus dessen <see cref="Population"/>.
    /// </summary>
    public sealed class CitySystem : ISimulationSystem
    {
        /// <summary>Alle 5 Ticks = alle 30 Spielminuten.</summary>
        public int TickInterval => 5;
        const float BuildingBucket = 32f;

        readonly RoadNetwork network;
        readonly TerrainMap terrain;
        readonly Random rng;
        readonly Dictionary<int, Building> buildings = new Dictionary<int, Building>();
        readonly Dictionary<long, List<Building>> buildingBuckets = new Dictionary<long, List<Building>>();
        int nextId = 1;

        public ZoneGrid Grid { get; }
        public ZoneMap ZoneMap { get; }
        public ZoneDemand Demand { get; } = new ZoneDemand();
        public CityStats Stats { get; } = new CityStats();
        /// <summary>Bürgerdaten für die Nachfrage (gefüllt vom Bürgersystem).</summary>
        public PopulationInfo Population { get; } = new PopulationInfo();
        /// <summary>Wie sehr die Stadt Zuzug anzieht (0..100, aus Jobs vs. Arbeitssuchenden) – steuert den Einzug.</summary>
        public float MoveInPull => moveInPull;
        public IEnumerable<Building> Buildings => buildings.Values;
        /// <summary>Wird bei jeder Änderung an Gebäuden/Zonen erhöht (für die Darstellung).</summary>
        public int Version { get; private set; }

        public event Action<Building> BuildingAdded;
        public event Action<Building> BuildingRemoved;

        // --- G3 Strom & Wasser (Hook): Punkte, auf denen nichts wachsen darf (Versorger-Bauwerke). Gesetzt vom UtilitySystem.
        public Func<Vector2, bool> LotBlocker { get; set; }

        // --- Stadtkasse (Hook): Faktor je Zonenart auf die Nachfrage (Steuersätze). null = 1. Gesetzt von SimulationWorld.
        public Func<ZoneType, float> DemandModifier { get; set; }
        // --- Ende Stadtkasse

        public CitySystem(RoadNetwork network, TerrainMap terrain, Vector2 origin, float sizeMeters, int seed = 1)
        {
            this.network = network;
            this.terrain = terrain;
            rng = new Random(seed);
            Grid = new ZoneGrid(network);
            ZoneMap = new ZoneMap(origin, sizeMeters);
            network.SegmentAdded += OnSegmentAdded;
        }

        public Building GetBuilding(int id) => buildings.TryGetValue(id, out var b) ? b : null;

        // ------------------------------------------------------------ Spieleraktionen

        /// <summary>Malt (oder löscht mit <see cref="ZoneType.None"/>) alle Zellen im Umkreis. Liefert die Anzahl Zellen.</summary>
        public int Paint(Vector2 center, float radius, ZoneType zone)
        {
            Grid.Update();
            int n = 0;
            foreach (var (block, c, r) in Grid.Near(center, radius))
            {
                ref var cell = ref block[c, r];
                if (ZoneMap.Get(cell.Center) == zone) continue;
                ZoneMap.SetCell(cell.Center, zone);
                n++;
                // Umgewidmete Zelle: Gebäude anderer Art darauf wird abgerissen
                var b = BuildingAt(cell.Center);
                if (b != null && b.Zone != zone) RemoveBuilding(b);
            }
            if (n > 0) Version++;
            return n;
        }

        public ZoneType ZoneAt(Vector2 p) => ZoneMap.Get(p);

        public Building BuildingAt(Vector2 p)
        {
            if (!buildingBuckets.TryGetValue(BucketKey(p), out var list)) return null;
            foreach (var b in list) if (b.Contains(p)) return b;
            return null;
        }

        public bool RemoveBuilding(Building b)
        {
            if (!buildings.Remove(b.Id)) return false;
            foreach (var key in BucketKeys(b))
                if (buildingBuckets.TryGetValue(key, out var list)) list.Remove(b);
            Version++;
            BuildingRemoved?.Invoke(b);
            return true;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            if (Grid.Update()) ValidateBuildings();
            UpdateBuildings();
            UpdateDemand();
            Grow();
        }

        /// <summary>Für Tests/Werkzeuge: Wachstum sofort anstoßen.</summary>
        public void ForceTick() => Tick(null);

        void UpdateBuildings()
        {
            // Nur Baufortschritt – Einzug und Arbeitsplätze besetzt das Bürgersystem
            foreach (var b in buildings.Values)
            {
                if (b.IsConstructed) continue;
                b.Construction = MathF.Min(1f, b.Construction + 0.1f);
                if (b.IsConstructed) Version++;
            }
        }

        // ------------------------------------------------------------ Nachfrage

        void UpdateDemand()
        {
            int households = 0, occupied = 0, jobs = 0, workers = 0, built = 0, constructing = 0;
            int retailJobs = 0, officeJobs = 0, industryJobs = 0;
            var freeByZone = new int[8];
            foreach (var b in buildings.Values)
            {
                if (b.IsConstructed) built++; else constructing++;
                households += b.Households;
                occupied += b.OccupiedHouseholds;
                jobs += b.Jobs;
                workers += b.Workers;
                if (b.Zone == ZoneType.Retail || b.Zone == ZoneType.Mixed) retailJobs += b.Jobs;
                else if (b.Zone == ZoneType.Office) officeJobs += b.Jobs;
                else if (b.Zone == ZoneType.Industry) industryJobs += b.Jobs;
            }
            int zoned = 0, free = 0;
            foreach (var block in Grid.Blocks)
            foreach (var cell in block.Cells)
            {
                if (!cell.Valid) continue;
                var z = ZoneMap.Get(cell.Center);
                if (z == ZoneType.None) continue;
                zoned++;
                if (BuildingAt(cell.Center) == null) { free++; freeByZone[(int)z]++; }
            }

            // G2: Bevölkerung und Arbeitsmarkt aus den einzelnen Bürgern
            var pop = Population;
            float population = pop.Residents;
            float workforce = pop.WorkingAge;

            // Wohnen: hoch, wenn es freie Jobs (oder von Einpendlern besetzte) gibt und kaum Arbeitssuchende;
            // am Anfang Grundnachfrage zum Starten
            float openJobs = pop.VacantJobs + pop.CommutersIn * 0.6f;
            float seekers = pop.Unemployed + pop.CommutersOut * 0.6f;
            float jobPull = Math.Clamp((openJobs - seekers) / MathF.Max(40f, workforce), -1f, 1f);
            float vacancy = households > 0 ? 1f - occupied / (float)households : 0f;
            moveInPull = 45f + 55f * jobPull;
            // Gebaut wird weniger, wenn schon viele Wohnungen leer stehen
            float res = moveInPull - MathF.Max(0f, vacancy - 0.15f) * 80f;
            Demand[ZoneType.ResidentialLow] = res + 10f;
            Demand[ZoneType.ResidentialMedium] = res * (population > 200 ? 1f : 0.6f);
            Demand[ZoneType.ResidentialHigh] = res * (population > 1000 ? 1f : 0.3f);

            // Arbeit: Grundbedarf aus der Bevölkerung, dazu Arbeitssuchende/Auspendler (Bildung → Büro, sonst Industrie).
            // Industrie braucht wenig Einwohner zum Start.
            float Need(float wanted, float have) => wanted <= 0f ? 0f : 100f * Math.Clamp((wanted - have) / MathF.Max(8f, wanted), 0f, 1f);
            float jobless = MathF.Max(0f, seekers - pop.VacantJobs);
            float educatedShare = pop.Unemployed > 0 ? pop.UnemployedEducated / (float)pop.Unemployed : 0.3f;
            float joblessPull = 60f * Math.Clamp(jobless / MathF.Max(20f, workforce * 0.2f), 0f, 1f);
            Demand[ZoneType.Retail] = Need(population * 0.09f, retailJobs) + MathF.Min(40f, pop.UnservedShoppers * 0.5f);
            Demand[ZoneType.Office] = Need(population * 0.07f - 10f, officeJobs) + joblessPull * educatedShare;
            Demand[ZoneType.Industry] = Need(population * 0.14f + 20f, industryJobs) + joblessPull * (1f - educatedShare);
            Demand[ZoneType.Mixed] = (res + Demand[ZoneType.Retail]) * 0.5f;
            // --- Stadtkasse: hohe Steuern dämpfen die Nachfrage (Standardsatz = Faktor 1)
            if (DemandModifier != null)
                foreach (var z in Zones.All) Demand[z] = Demand[z] * DemandModifier(z);
            // --- Ende Stadtkasse

            Stats.Buildings = built;
            Stats.UnderConstruction = constructing;
            Stats.Households = households;
            Stats.OccupiedHouseholds = occupied;
            Stats.Population = pop.Residents;
            Stats.Jobs = jobs;
            Stats.Workers = workers;
            Stats.ZonedCells = zoned;
            Stats.FreeZonedCells = free;
            freeCells = freeByZone;
        }

        int[] freeCells = new int[8];
        float moveInPull = 45f;

        // ------------------------------------------------------------ Wachstum

        void Grow()
        {
            float total = 0f;
            foreach (var z in Zones.All) if (freeCells[(int)z] > 0) total += Demand[z];
            if (total <= 1f) return;

            int attempts = 1 + (int)(total / 60f);
            for (int i = 0; i < attempts; i++)
            {
                // Zonenart nach Nachfrage gewichtet wählen
                double roll = rng.NextDouble() * total;
                ZoneType zone = ZoneType.None;
                foreach (var z in Zones.All)
                {
                    if (freeCells[(int)z] == 0) continue;
                    roll -= Demand[z];
                    if (roll <= 0) { zone = z; break; }
                }
                if (zone == ZoneType.None) continue;
                // Bei geringer Nachfrage wird seltener gebaut
                if (rng.NextDouble() * 100.0 > Demand[zone]) continue;
                TryGrow(zone);
            }
        }

        bool TryGrow(ZoneType zone)
        {
            // Zufällige freie Zelle der Zone in der vordersten Reihe als Ausgangspunkt
            var candidates = new List<(ZoneBlock block, int column)>();
            foreach (var block in Grid.Blocks)
                for (int c = 0; c < block.Columns; c++)
                {
                    var cell = block[c, 0];
                    if (cell.Valid && ZoneMap.Get(cell.Center) == zone && BuildingAt(cell.Center) == null) candidates.Add((block, c));
                }
            if (candidates.Count == 0) return false;

            var (b, col) = candidates[rng.Next(candidates.Count)];
            foreach (var (w, d) in Zones.LotSizes(zone))
            {
                // Grundstück darf links oder rechts der Startzelle beginnen
                for (int shift = 0; shift < w; shift++)
                {
                    int c0 = col - shift;
                    if (c0 < 0 || c0 + w > b.Columns) continue;
                    if (!LotFits(b, c0, w, d, zone)) continue;
                    Place(b, c0, w, d, zone);
                    return true;
                }
            }
            return false;
        }

        bool LotFits(ZoneBlock b, int c0, int w, int d, ZoneType zone)
        {
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int c = c0; c < c0 + w; c++)
            for (int r = 0; r < d; r++)
            {
                var cell = b[c, r];
                if (!cell.Valid || ZoneMap.Get(cell.Center) != zone || BuildingAt(cell.Center) != null) return false;
                if (LotBlocker != null && LotBlocker(cell.Center)) return false; // G3-Hook
                minH = MathF.Min(minH, cell.Height);
                maxH = MathF.Max(maxH, cell.Height);
            }
            return maxH - minH < 3f; // zu steil für ein Grundstück
        }

        void Place(ZoneBlock block, int c0, int w, int d, ZoneType zone)
        {
            Vector2 center = Vector2.Zero, outward = Vector2.Zero;
            for (int c = c0; c < c0 + w; c++)
            for (int r = 0; r < d; r++)
            {
                center += block[c, r].Center;
                outward += block[c, r].Out;
            }
            center /= w * d;
            var front = -Vector2.Normalize(outward);
            // Bodenhöhe: an der Straße (Zufahrt eben), Gelände wird angeglichen
            float roadHeight = block.Segment.HeightAt(block.Segment.ClosestT(center));
            float height = MathF.Max(roadHeight, terrain.WaterLevel + 0.5f);

            int cells = w * d;
            var building = new Building
            {
                Id = nextId++,
                Zone = zone,
                WidthCells = w,
                DepthCells = d,
                Position = center,
                Front = front,
                Height = height,
                FrontSegmentId = block.Segment.Id,
                FrontT = block.Segment.ClosestT(center + front * (d * ZoneGrid.CellSize * 0.5f)),
                Variant = rng.Next(1 << 20),
                Households = Zones.HouseholdsPerCell(zone) > 0f ? Math.Max(1, (int)MathF.Round(Zones.HouseholdsPerCell(zone) * cells)) : 0,
                Jobs = (int)MathF.Round(Zones.JobsPerCell(zone) * cells),
            };
            terrain.FlattenArea(center, front, building.Width, building.Depth, height);
            buildings[building.Id] = building;
            foreach (var key in BucketKeys(building))
            {
                if (!buildingBuckets.TryGetValue(key, out var list)) buildingBuckets[key] = list = new List<Building>();
                list.Add(building);
            }
            Version++;
            BuildingAdded?.Invoke(building);
        }

        // ------------------------------------------------------------ Straßenänderungen

        /// <summary>Neue Straße durch ein Grundstück → Gebäude wird abgerissen.</summary>
        void OnSegmentAdded(RoadSegment s)
        {
            var hit = new List<Building>();
            int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 4f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                if (MathF.Abs(s.ElevationAt(t)) > 3f) continue; // Brücke/Tunnel geht drüber/drunter weg
                var p = s.Evaluate(t);
                foreach (var key in NearKeys(p, s.Type.HalfWidth + BuildingBucket))
                {
                    if (!buildingBuckets.TryGetValue(key, out var list)) continue;
                    foreach (var b in list)
                        if (!hit.Contains(b) && b.Contains(p, s.Type.HalfWidth - 0.5f)) hit.Add(b);
                }
            }
            foreach (var b in hit) RemoveBuilding(b);
        }

        /// <summary>Nach Netzänderungen: Gebäude ohne Straße an der Vorderseite werden abgerissen.</summary>
        void ValidateBuildings()
        {
            var remove = new List<Building>();
            foreach (var b in buildings.Values)
            {
                var seg = network.FindNearestSegment(b.Entrance, 3f);
                if (seg == null || seg.Type.Category != RoadCategory.City) remove.Add(b);
                else
                {
                    b.FrontSegmentId = seg.Id;
                    b.FrontT = seg.ClosestT(b.Entrance);
                }
            }
            foreach (var b in remove) RemoveBuilding(b);
        }

        // ------------------------------------------------------------ Räumliche Suche

        static long BucketKey(Vector2 p) =>
            ((long)MathF.Floor(p.X / BuildingBucket) << 32) ^ (uint)(int)MathF.Floor(p.Y / BuildingBucket);

        static IEnumerable<long> BucketKeys(Building b)
        {
            float r = MathF.Sqrt(b.Width * b.Width + b.Depth * b.Depth) * 0.5f;
            return NearKeys(b.Position, r);
        }

        static IEnumerable<long> NearKeys(Vector2 p, float r)
        {
            int x0 = (int)MathF.Floor((p.X - r) / BuildingBucket), x1 = (int)MathF.Floor((p.X + r) / BuildingBucket);
            int y0 = (int)MathF.Floor((p.Y - r) / BuildingBucket), y1 = (int)MathF.Floor((p.Y + r) / BuildingBucket);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return ((long)x << 32) ^ (uint)y;
        }
    }
}
