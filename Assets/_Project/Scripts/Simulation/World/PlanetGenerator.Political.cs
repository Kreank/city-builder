using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.World
{
    // Regionen (Voronoi über Land mit Gebirgskosten), Länder (Gruppierung der Regionen) und Stärken-Profile.
    public static partial class PlanetGenerator
    {
        static void Political(Planet p, Vector3[] unit, PlanetSettings s, int seed)
        {
            var names = new NameGenerator(seed ^ 0x4E414D45);
            var centers = BuildRegions(p, unit, s, seed);
            AggregateRegions(p, unit, centers);
            foreach (var r in p.Regions) r.Name = names.Region();
            BuildCountries(p, unit, s, seed, names);
            Profiles(p, seed);
        }

        static float LandFraction(Planet p)
        {
            double land = 0, total = 0;
            for (int i = 0; i < p.CellCount; i++)
            {
                double a = p.CellAreaKm2(p.YOf(i));
                total += a;
                if (p.IsLand(i)) land += a;
            }
            return total > 0 ? (float)(land / total) : 0f;
        }

        // ------------------------------------------------------------ Regionen

        static List<Vector3> BuildRegions(Planet p, Vector3[] unit, PlanetSettings s, int seed)
        {
            int n = p.CellCount;
            var rng = new SimRandom(seed ^ 0x5E61);
            var land = new List<int>();
            for (int i = 0; i < n; i++) { p.RegionOf[i] = -1; if (p.IsLand(i)) land.Add(i); }
            for (int k = land.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); (land[k], land[j]) = (land[j], land[k]); }

            float dmin = MathF.Sqrt(4f * MathF.PI * MathF.Max(0.01f, LandFraction(p)) / Math.Max(1, s.TargetRegions)) * 0.8f;
            float cosMin = MathF.Cos(dmin);
            var centers = new List<Vector3>();
            var seeds = new List<int>();
            foreach (int c in land)
            {
                // Pole meiden (Eis) – dort trotzdem Regionen über die Inselsuche unten
                bool ok = true;
                foreach (var v in centers) if (Vector3.Dot(v, unit[c]) > cosMin) { ok = false; break; }
                if (!ok) continue;
                centers.Add(unit[c]);
                seeds.Add(c);
            }

            // Mehrquellen-Dijkstra über Land, Gebirge sind teuer → Grenzen folgen Kämmen
            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.MaxValue;
            var heap = new MinHeap();
            for (int k = 0; k < seeds.Count; k++) { dist[seeds[k]] = 0f; p.RegionOf[seeds[k]] = k; heap.Push(0f, seeds[k]); }
            while (heap.Count > 0)
            {
                var (d, i) = heap.Pop();
                if (d > dist[i]) continue;
                float cosLat = MathF.Max(0.05f, MathF.Cos(p.LatitudeOf(i) * MathF.PI / 180f));
                for (int k = 0; k < 8; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0 || !p.IsLand(j)) continue;
                    float dx = Planet.Dx8[k] * cosLat, dy = Planet.Dy8[k];
                    float step = MathF.Sqrt(dx * dx + dy * dy) * (1f + MathF.Abs(p.Elevation[j] - p.Elevation[i]) / 400f);
                    float nd = d + step;
                    if (nd >= dist[j]) continue;
                    dist[j] = nd;
                    p.RegionOf[j] = p.RegionOf[i];
                    heap.Push(nd, j);
                }
            }

            // Inseln ohne Saatpunkt: ab 6 Zellen eigene Region, kleinere zur nächsten Region
            var queue = new Queue<int>();
            var comp = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!p.IsLand(i) || p.RegionOf[i] >= 0) continue;
                comp.Clear();
                p.RegionOf[i] = -2;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    comp.Add(c);
                    for (int k = 0; k < 8; k++)
                    {
                        int j = p.Neighbor(c, Planet.Dx8[k], Planet.Dy8[k]);
                        if (j < 0 || !p.IsLand(j) || p.RegionOf[j] != -1) continue;
                        p.RegionOf[j] = -2;
                        queue.Enqueue(j);
                    }
                }
                int id;
                if (comp.Count >= 6 || centers.Count == 0)
                {
                    id = centers.Count;
                    centers.Add(unit[comp[0]]);
                }
                else
                {
                    id = 0;
                    float best = -2f;
                    for (int k = 0; k < centers.Count; k++)
                    {
                        float dd = Vector3.Dot(centers[k], unit[comp[0]]);
                        if (dd > best) { best = dd; id = k; }
                    }
                }
                foreach (int c in comp) p.RegionOf[c] = id;
            }

            for (int k = 0; k < centers.Count; k++) p.Regions.Add(new Region { Id = k });
            for (int i = 0; i < n; i++) if (p.RegionOf[i] >= 0) p.Regions[p.RegionOf[i]].Cells.Add(i);
            return centers;
        }

        static void AggregateRegions(Planet p, Vector3[] unit, List<Vector3> centers)
        {
            // Meer nahe der Küste der nächsten Region zuordnen (Schelf-Öl, Fischerei später)
            int n = p.CellCount;
            for (int i = 0; i < n; i++) p.MaritimeRegion[i] = -1;
            var queue = new Queue<int>();
            var depth = new int[n];
            for (int i = 0; i < n; i++) if (p.IsLand(i) && p.CoastDistance[i] == 1) queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int label = p.IsLand(i) ? p.RegionOf[i] : p.MaritimeRegion[i];
                for (int k = 0; k < 4; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0 || p.IsLand(j) || p.MaritimeRegion[j] >= 0) continue;
                    int dj = (p.IsLand(i) ? 0 : depth[i]) + 1;
                    if (dj > Planet.MaritimeReach) continue;
                    depth[j] = dj;
                    p.MaritimeRegion[j] = label;
                    queue.Enqueue(j);
                }
            }

            var biomeArea = new float[Biomes.Count];
            var maritimeOil = new float[p.Regions.Count];
            for (int i = 0; i < n; i++)
                if (!p.IsLand(i) && p.MaritimeRegion[i] >= 0)
                    maritimeOil[p.MaritimeRegion[i]] += p.Oil[i] * p.CellAreaKm2(p.YOf(i));

            foreach (var r in p.Regions)
            {
                Array.Clear(biomeArea, 0, biomeArea.Length);
                float area = 0f, elev = 0f, maxE = 0f, rug = 0f, temp = 0f, amp = 0f, prec = 0f;
                float fert = 0f, forest = 0f, ore = 0f, coal = 0f, oil = 0f, sand = 0f, gw = 0f, maxFlow = 0f;
                var mean = Vector3.Zero;
                var coastDir = Vector2.Zero;
                int coast = 0;
                var neighbors = new HashSet<int>();
                foreach (int i in r.Cells)
                {
                    float a = p.CellAreaKm2(p.YOf(i));
                    area += a;
                    elev += p.Elevation[i] * a; maxE = MathF.Max(maxE, p.Elevation[i]);
                    rug += p.Ruggedness(i) * a;
                    temp += p.Temperature[i] * a; amp += p.Amplitude[i] * a; prec += p.Precipitation[i] * a;
                    fert += p.Fertility[i] * a; forest += p.Forest[i] * a; ore += p.Ore[i] * a; coal += p.Coal[i] * a;
                    oil += p.Oil[i] * a; sand += p.Sand[i] * a; gw += p.Groundwater[i] * a;
                    maxFlow = MathF.Max(maxFlow, p.Flow[i]);
                    biomeArea[(int)p.BiomeOf[i]] += a;
                    mean += unit[i] * a;
                    bool isCoast = false;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                        if (j < 0) continue;
                        if (!p.IsLand(j))
                        {
                            if (k < 4) { isCoast = true; coastDir += new Vector2(Planet.Dx8[k], -Planet.Dy8[k]); }
                        }
                        else if (p.RegionOf[j] != r.Id && p.RegionOf[j] >= 0) neighbors.Add(p.RegionOf[j]);
                    }
                    if (isCoast) coast++;
                }
                float inv = area > 0f ? 1f / area : 0f;
                r.AreaKm2 = area;
                r.MeanElevation = elev * inv; r.MaxElevation = maxE; r.Ruggedness = rug * inv;
                r.MeanTemperature = temp * inv; r.SeasonalAmplitude = amp * inv; r.Precipitation = prec * inv;
                r.Fertility = fert * inv; r.Forest = forest * inv; r.Ore = ore * inv; r.Coal = coal * inv;
                r.Oil = WorldNoise.Clamp01((oil + maritimeOil[r.Id] * 0.5f) * inv);
                r.Sand = sand * inv; r.Groundwater = gw * inv;
                r.MaxFlow = maxFlow;
                r.HasRiver = maxFlow >= p.RiverThreshold;
                r.CoastCells = coast;
                r.CoastDirection = coast > 0 && coastDir.LengthSquared() > 1e-4f ? Vector2.Normalize(coastDir) : (Vector2?)null;
                int bestBiome = 0;
                for (int b = 1; b < biomeArea.Length; b++) if (biomeArea[b] > biomeArea[bestBiome]) bestBiome = b;
                r.DominantBiome = (Biome)bestBiome;
                var list = new List<int>(neighbors);
                list.Sort();
                r.Neighbors.AddRange(list);
                // Mittelpunkt: Zelle am nächsten zum Flächenschwerpunkt
                var m = mean.LengthSquared() > 1e-8f ? Vector3.Normalize(mean) : centers[r.Id];
                int center = r.Cells[0];
                float best = -2f;
                foreach (int i in r.Cells)
                {
                    float d = Vector3.Dot(unit[i], m);
                    if (d > best) { best = d; center = i; }
                }
                r.CenterCell = center;
                r.Latitude = p.LatitudeOf(center);
                r.Longitude = p.Longitude(p.XOf(center));
            }
        }

        // ------------------------------------------------------------ Länder

        static void BuildCountries(Planet p, Vector3[] unit, PlanetSettings s, int seed, NameGenerator names)
        {
            int rc = p.Regions.Count;
            if (rc == 0) return;
            var rng = new SimRandom(seed ^ 0xC0C0);
            var c = new Vector3[rc];
            for (int r = 0; r < rc; r++) c[r] = unit[p.Regions[r].CenterCell];

            int target = Math.Max(1, Math.Min(s.TargetCountries, rc));
            var order = new int[rc];
            for (int r = 0; r < rc; r++) order[r] = r;
            for (int k = rc - 1; k > 0; k--) { int j = rng.Next(k + 1); (order[k], order[j]) = (order[j], order[k]); }
            float dmin = MathF.Sqrt(4f * MathF.PI * MathF.Max(0.01f, LandFraction(p)) / target) * 0.85f;
            var seeds = new List<int>();
            for (int attempt = 0; attempt < 4 && seeds.Count < target; attempt++, dmin *= 0.8f)
            {
                float cosMin = MathF.Cos(dmin);
                foreach (int r in order)
                {
                    if (seeds.Count >= target) break;
                    if (seeds.Contains(r) || p.Regions[r].Cells.Count < 3) continue;
                    bool ok = true;
                    foreach (int q in seeds) if (Vector3.Dot(c[q], c[r]) > cosMin) { ok = false; break; }
                    if (ok) seeds.Add(r);
                }
            }
            if (seeds.Count == 0) seeds.Add(0);

            // Regionsgraph: Landnachbarn (Gebirge teurer) + Seewege zu den 3 nächsten Regionen
            var edges = new List<(int to, float cost)>[rc];
            for (int r = 0; r < rc; r++) edges[r] = new List<(int, float)>();
            float Angle(int a, int b) => MathF.Acos(Math.Clamp(Vector3.Dot(c[a], c[b]), -1f, 1f));
            for (int r = 0; r < rc; r++)
            {
                var reg = p.Regions[r];
                foreach (int q in reg.Neighbors)
                    edges[r].Add((q, Angle(r, q) * (1f + MathF.Max(reg.Ruggedness, p.Regions[q].Ruggedness) / 1500f)));
                var near = new List<(float d, int q)>();
                for (int q = 0; q < rc; q++)
                    if (q != r && !reg.Neighbors.Contains(q)) near.Add((-Vector3.Dot(c[r], c[q]), q));
                near.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.q.CompareTo(y.q));
                for (int k = 0; k < Math.Min(3, near.Count); k++)
                {
                    int q = near[k].q;
                    float cost = Angle(r, q) * 2.5f;
                    edges[r].Add((q, cost));
                    edges[q].Add((r, cost));
                }
            }
            var dist = new float[rc];
            var owner = new int[rc];
            for (int r = 0; r < rc; r++) { dist[r] = float.MaxValue; owner[r] = -1; }
            var heap = new MinHeap();
            for (int k = 0; k < seeds.Count; k++) { dist[seeds[k]] = 0f; owner[seeds[k]] = k; heap.Push(0f, seeds[k]); }
            while (heap.Count > 0)
            {
                var (d, r) = heap.Pop();
                if (d > dist[r]) continue;
                foreach (var (to, cost) in edges[r])
                {
                    float nd = d + cost;
                    if (nd >= dist[to]) continue;
                    dist[to] = nd; owner[to] = owner[r];
                    heap.Push(nd, to);
                }
            }
            for (int r = 0; r < rc; r++)
            {
                if (owner[r] >= 0) continue;
                float best = -2f;
                for (int k = 0; k < seeds.Count; k++)
                {
                    float d = Vector3.Dot(c[r], c[seeds[k]]);
                    if (d > best) { best = d; owner[r] = k; }
                }
            }
            // --- Spielstart: ein Teil des Landes bleibt unbesiedelt (zwischen den Ländern, Randlagen)
            if (s.FreeLandShare > 0f) FreeLand(p, s, seed, seeds, owner, dist, edges);
            // --- Ende Spielstart

            for (int k = 0; k < seeds.Count; k++)
            {
                var country = new Country { Id = k, Name = names.Country(), CapitalRegionId = seeds[k], CapitalName = names.City(), Color = CountryColor(k) };
                p.Countries.Add(country);
            }
            for (int r = 0; r < rc; r++)
            {
                p.Regions[r].CountryId = owner[r];
                if (owner[r] < 0) continue; // frei
                p.Regions[r].Country = p.Countries[owner[r]];
                p.Countries[owner[r]].Regions.Add(r);
            }
            foreach (var country in p.Countries)
                country.CapitalCell = BestCapitalCell(p, p.Regions[country.CapitalRegionId]);
        }

        // --- Spielstart & Startregion: freies Land
        /// <summary>
        /// Macht etwa <see cref="PlanetSettings.FreeLandShare"/> der Regionen zu freiem Land. Bevorzugt Regionen, die weit von
        /// der eigenen Hauptstadt liegen (relativ zum Land und weltweit, Dijkstra-Abstand), an Grenzen zu anderen Ländern und
        /// Inseln; ein Hash-Rauschen verteilt das freie Land über alle Klimazonen und Kontinente. Hauptstädte bleiben. Danach
        /// wird alles frei, was nicht mehr über eigene Regionen (Land- oder Seeweg) mit der Hauptstadt verbunden ist.
        /// </summary>
        static void FreeLand(Planet p, PlanetSettings s, int seed, List<int> seeds, int[] owner, float[] dist, List<(int to, float cost)>[] edges)
        {
            int rc = p.Regions.Count;
            var capital = new bool[rc];
            foreach (int r in seeds) capital[r] = true;
            var maxDist = new float[seeds.Count];
            float globalMax = 0f;
            for (int r = 0; r < rc; r++)
            {
                if (owner[r] < 0 || dist[r] == float.MaxValue) continue;
                maxDist[owner[r]] = MathF.Max(maxDist[owner[r]], dist[r]);
                globalMax = MathF.Max(globalMax, dist[r]);
            }
            var key = new float[rc];
            var order = new List<int>();
            for (int r = 0; r < rc; r++)
            {
                if (capital[r] || owner[r] < 0) continue;
                bool unreachable = dist[r] == float.MaxValue;
                float rel = unreachable ? 1f : maxDist[owner[r]] > 0f ? dist[r] / maxDist[owner[r]] : 0f;
                float abs = unreachable ? 1f : globalMax > 0f ? dist[r] / globalMax : 0f;
                var reg = p.Regions[r];
                bool border = false;
                foreach (int q in reg.Neighbors) if (owner[q] != owner[r]) { border = true; break; }
                bool island = reg.Neighbors.Count == 0;
                float noise = (SimRandom.Hash(seed ^ 0x46524545, r) & 0xFFFF) / 65535f;
                key[r] = 0.5f * rel + 0.25f * abs + (border ? 0.2f : 0f) + (island ? 0.1f : 0f) + 0.35f * noise;
                order.Add(r);
            }
            order.Sort((a, b) => key[a] != key[b] ? key[b].CompareTo(key[a]) : a.CompareTo(b));
            int target = Math.Min(order.Count, (int)MathF.Round(Math.Clamp(s.FreeLandShare, 0f, 0.9f) * rc));
            for (int i = 0; i < target; i++) owner[order[i]] = -1;

            // Zusammenhang: nur, was über eigene Regionen von der Hauptstadt aus erreichbar ist, bleibt beim Land
            var reached = new bool[rc];
            var queue = new Queue<int>();
            foreach (int r in seeds) { reached[r] = true; queue.Enqueue(r); }
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach (var (to, _) in edges[r])
                {
                    if (reached[to] || owner[to] != owner[r]) continue;
                    reached[to] = true;
                    queue.Enqueue(to);
                }
            }
            for (int r = 0; r < rc; r++) if (owner[r] >= 0 && !reached[r]) owner[r] = -1;
        }

        /// <summary>
        /// Stadtstaat des Spielers in einer freien Region anlegen (<paramref name="cityState"/> null) bzw. dorthin verlegen:
        /// Region zuordnen, Hauptstadtzelle, Kennzahlen, Stärken-Profil (Rang gegen die NPC-Länder, deren Ränge bleiben)
        /// und eine kleine eigene Volkswirtschaft (Umland der Stadt) – Handel und LOD-Wirtschaft behandeln ihn wie ein Land.
        /// </summary>
        internal static void PlaceCityState(Planet p, Country cityState, Region region)
        {
            if (cityState == null)
            {
                cityState = new Country
                {
                    Id = p.Countries.Count, Name = PlayerRealm.CityStateName(null), CapitalName = PlayerRealm.DefaultCityName,
                    Color = PlayerColor, IsPlayer = true, IsCityState = true,
                };
                p.Countries.Add(cityState);
            }
            foreach (int old in cityState.Regions.ToArray()) p.AssignRegion(p.Regions[old], null);
            p.AssignRegion(region, cityState);
            cityState.CapitalRegionId = region.Id;
            cityState.CapitalCell = BestCapitalCell(p, region);

            var st = new ProfileStats();
            foreach (int i in region.Cells) st.AddCell(p, i);
            for (int i = 0; i < p.CellCount; i++)
                if (!p.IsLand(i) && p.MaritimeRegion[i] == region.Id) st.AddShelfOil(p, i);
            float culture = (SimRandom.Hash(p.Seed ^ 0x5C17, region.Id) & 0xFFFF) / 65535f;
            FillRaw(cityState, st, culture);
            // Rang gegen die NPC-Länder (deren Ränge ändern sich nicht)
            int others = 0;
            var below = new float[CountryProfile.Count];
            foreach (var o in p.Countries)
            {
                if (o == cityState) continue;
                others++;
                for (int k = 0; k < CountryProfile.Count; k++)
                {
                    float v = cityState.Profile.Raw[k], w = o.Profile.Raw[k];
                    if (w < v) below[k] += 1f; else if (w == v) below[k] += 0.5f;
                }
            }
            for (int k = 0; k < CountryProfile.Count; k++) cityState.Profile.Rank[k] = others > 0 ? below[k] / others : 0.5f;
            Comparative(cityState.Profile);
            p.Economy?.PlaceCityState(p, cityState);
        }

        /// <summary>Kartenfarbe des Spielers (Gold).</summary>
        public const uint PlayerColor = 0xE8C547;

        static int BestCapitalCell(Planet p, Region cap)
        {
            int bestCell = cap.CenterCell;
            float bestScore = float.MinValue;
            foreach (int i in cap.Cells)
            {
                float sc = p.Fertility[i] + (p.IsRiver(i) ? 0.5f : 0f) + (p.CoastDistance[i] == 1 ? 0.4f : 0f)
                           - MathF.Abs(p.Temperature[i] - 14f) / 25f - p.Ruggedness(i) / 2000f;
                if (sc > bestScore) { bestScore = sc; bestCell = i; }
            }
            return bestCell;
        }
        // --- Ende Spielstart

        static uint CountryColor(int id)
        {
            float h = (id * 0.618034f) % 1f, sat = 0.45f + 0.15f * (id % 3), val = 0.92f - 0.08f * (id % 2);
            float r = 0, g = 0, b = 0;
            float f = h * 6f;
            int sector = (int)MathF.Floor(f) % 6;
            float frac = f - MathF.Floor(f);
            float pp = val * (1 - sat), q = val * (1 - sat * frac), t = val * (1 - sat * (1 - frac));
            switch (sector)
            {
                case 0: r = val; g = t; b = pp; break;
                case 1: r = q; g = val; b = pp; break;
                case 2: r = pp; g = val; b = t; break;
                case 3: r = pp; g = q; b = val; break;
                case 4: r = t; g = pp; b = val; break;
                default: r = val; g = pp; b = q; break;
            }
            return ((uint)(r * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(b * 255);
        }

        // ------------------------------------------------------------ Stärken-Profile (komparative Vorteile)

        /// <summary>Flächengewichtete Summen über die Zellen eines Landes (Grundlage von <see cref="FillRaw"/>).</summary>
        sealed class ProfileStats
        {
            public float area, temp, prec, fert, forest, ore, coal, oil, gw, mountain, flat, river;
            public int coastCells, landCells;
            public readonly float[] biome = new float[Biomes.Count];

            public void AddCell(Planet p, int i)
            {
                float a = p.CellAreaKm2(p.YOf(i));
                area += a; temp += p.Temperature[i] * a; prec += p.Precipitation[i] * a;
                fert += p.Fertility[i] * a; forest += p.Forest[i] * a; ore += p.Ore[i] * a;
                coal += p.Coal[i] * a; oil += p.Oil[i] * a; gw += p.Groundwater[i] * a;
                if (p.Elevation[i] > 1500f) mountain += a;
                if (p.Ruggedness(i) < 300f) flat += a;
                if (p.IsRiver(i)) river += a;
                biome[(int)p.BiomeOf[i]] += a;
                landCells++;
                if (p.CoastDistance[i] == 1) coastCells++;
            }

            /// <summary>Schelf-Öl gehört dem Küstenland (halb gewichtet).</summary>
            public void AddShelfOil(Planet p, int i) => oil += p.Oil[i] * p.CellAreaKm2(p.YOf(i)) * 0.5f;
        }

        static void Profiles(Planet p, int seed)
        {
            int cc = p.Countries.Count;
            if (cc == 0) return;
            var stats = new ProfileStats[cc];
            for (int c = 0; c < cc; c++) stats[c] = new ProfileStats();
            for (int i = 0; i < p.CellCount; i++)
            {
                if (!p.IsLand(i)) continue;
                int c = p.Regions[p.RegionOf[i]].CountryId;
                if (c < 0) continue; // freies Land
                stats[c].AddCell(p, i);
            }
            // Schelf-Öl gehört dem Küstenland
            for (int i = 0; i < p.CellCount; i++)
                if (!p.IsLand(i) && p.MaritimeRegion[i] >= 0)
                {
                    int c = p.Regions[p.MaritimeRegion[i]].CountryId;
                    if (c >= 0) stats[c].AddShelfOil(p, i);
                }

            var rng = new SimRandom(seed ^ 0x7EC4);
            for (int c = 0; c < cc; c++) FillRaw(p.Countries[c], stats[c], rng.NextFloat());
            // Rang unter allen Ländern + komparativer Vorteil gegenüber den eigenen anderen Stärken
            for (int k = 0; k < CountryProfile.Count; k++)
                for (int c = 0; c < cc; c++)
                {
                    float v = p.Countries[c].Profile.Raw[k];
                    float below = 0f;
                    for (int o = 0; o < cc; o++)
                    {
                        if (o == c) continue;
                        float w = p.Countries[o].Profile.Raw[k];
                        if (w < v) below += 1f; else if (w == v) below += 0.5f;
                    }
                    p.Countries[c].Profile.Rank[k] = cc > 1 ? below / (cc - 1) : 0.5f;
                }
            foreach (var country in p.Countries) Comparative(country.Profile);
        }

        /// <summary>Absolute Eignung (<see cref="CountryProfile.Raw"/>) und Kennzahlen eines Landes aus seinen Zellsummen.</summary>
        static void FillRaw(Country country, ProfileStats s, float culture)
        {
            float inv = s.area > 0f ? 1f / s.area : 0f;
            float t = s.temp * inv, pr = s.prec * inv;
            country.AreaKm2 = s.area;
            country.MeanTemperature = t;
            country.Precipitation = pr;
            country.LandCells = s.landCells;
            country.CoastCells = s.coastCells;
            float coastRatio = s.landCells > 0 ? MathF.Min(1f, s.coastCells / (float)s.landCells * 1.6f) : 0f;
            float diversity = 0f;
            for (int b = 0; b < Biomes.Count; b++) if (s.biome[b] * inv > 0.05f) diversity += 1f;
            diversity = MathF.Min(1f, diversity / 5f);
            float growing = WorldNoise.Clamp01(1f - MathF.Abs(t - 17f) / 20f);
            float water = WorldNoise.SmoothStep(250f, 800f, pr);

            var raw = country.Profile.Raw;
            raw[(int)Strength.Agriculture] = WorldNoise.Clamp01(s.fert * inv * 1.3f * growing * (0.4f + 0.6f * water));
            raw[(int)Strength.RawMaterials] = WorldNoise.Clamp01((0.32f * s.ore + 0.25f * s.coal + 0.3f * s.oil + 0.13f * s.forest) * inv * 2.2f);
            raw[(int)Strength.Industry] = WorldNoise.Clamp01(0.3f * (s.coal + s.ore) * inv * 1.5f + 0.2f * s.gw * inv
                                                             + 0.2f * MathF.Min(1f, s.river * inv * 6f) + 0.15f * s.flat * inv + 0.15f * coastRatio);
            float warmSunny = WorldNoise.Clamp01(1f - MathF.Abs(t - 24f) / 12f) * (1f - WorldNoise.SmoothStep(1200f, 2600f, pr));
            raw[(int)Strength.Tourism] = WorldNoise.Clamp01(0.32f * coastRatio + 0.3f * warmSunny + 0.23f * MathF.Min(1f, s.mountain * inv * 4f) + 0.15f * diversity);
            float temperate = WorldNoise.Clamp01(1f - MathF.Abs(t - 12f) / 15f);
            raw[(int)Strength.Technology] = WorldNoise.Clamp01(0.35f * temperate + 0.25f * coastRatio + 0.15f * MathF.Min(1f, s.river * inv * 6f) + 0.25f * culture);
        }

        /// <summary>Komparativer Vorteil gegenüber den eigenen anderen Stärken → Primär-/Sekundärstärke.</summary>
        static void Comparative(CountryProfile pr)
        {
            float mean = 0f;
            for (int k = 0; k < CountryProfile.Count; k++) mean += pr.Rank[k] + 0.05f;
            mean /= CountryProfile.Count;
            int first = 0, second = -1;
            for (int k = 0; k < CountryProfile.Count; k++)
            {
                pr.Comparative[k] = (pr.Rank[k] + 0.05f) / mean;
                if (pr.Comparative[k] > pr.Comparative[first]) first = k;
            }
            for (int k = 0; k < CountryProfile.Count; k++)
                if (k != first && (second < 0 || pr.Comparative[k] > pr.Comparative[second])) second = k;
            pr.Primary = (Strength)first;
            pr.Secondary = (Strength)second;
        }
    }
}
