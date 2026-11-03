using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.World
{
    /// <summary>Einstellungen des Weltgenerators (Standardwerte = ~111-km-Zellen, ~30 % Land).</summary>
    public sealed class PlanetSettings
    {
        public int Width = 360;
        public int Height = 180;
        /// <summary>Lithosphärenplatten (Kontinente/Ozeane, Gebirge an den Grenzen).</summary>
        public int Plates = 14;
        /// <summary>Flächenanteil Land (Meeresspiegel wird danach gewählt).</summary>
        public float LandFraction = 0.30f;
        public int TargetRegions = 300;
        public int TargetCountries = 36;
    }

    /// <summary>
    /// Prozeduraler Generator der fiktiven Erde (geseedet, deterministisch). Ablauf:
    /// Platten (Voronoi auf der Kugel, verzerrt) → Relief (Kontinentalkruste, Gebirge an konvergenten Grenzen,
    /// Rifts/Rücken an divergenten, alte Gebirgsgürtel, Schelf) → Küstenabstand → Temperatur (Breite, Höhe,
    /// Kontinentalität) → Niederschlag (Windgürtel, Feuchtetransport, Steigungsregen, Regenschatten) → Abfluss/Flüsse
    /// (Priority-Flood) → Biome → Böden und Bodenschätze (Geologie) → Regionen → Länder → Profile → Volkswirtschaften.
    /// </summary>
    public static partial class PlanetGenerator
    {
        public static Planet Generate(int seed, PlanetSettings settings = null)
        {
            var s = settings ?? new PlanetSettings();
            if (s.Width < 16 || s.Height < 8) throw new ArgumentOutOfRangeException(nameof(settings), "Welt zu klein.");
            var sw = Stopwatch.StartNew();
            var p = new Planet(seed, s.Width, s.Height);
            var unit = new Vector3[p.CellCount];
            for (int i = 0; i < unit.Length; i++) unit[i] = p.UnitVector(i);

            var tect = Relief(p, unit, s, seed);
            CoastDistances(p);
            Temperatures(p, unit, seed);
            var precip = ComputePrecipitation(p.Width, p.Height, p.Elevation, p.Temperature);
            for (int i = 0; i < precip.Length; i++)
            {
                var u = unit[i];
                float n = WorldNoise.Fbm3(u.X * 3f + 11f, u.Y * 3f, u.Z * 3f - 5f, 3, seed + 404);
                p.Precipitation[i] = MathF.Max(20f, precip[i] * (0.75f + 0.5f * n));
            }
            var filled = Drainage(p);
            Classify(p);
            Geology(p, unit, tect, filled, seed);
            Political(p, unit, s, seed);
            p.Economy = new WorldEconomy(p);
            p.GenerationMilliseconds = sw.Elapsed.TotalMilliseconds;
            return p;
        }

        // ------------------------------------------------------------ Platten und Relief

        sealed class Tectonics
        {
            public float[] Volcanic;
        }

        static Vector3 RandomUnit(SimRandom rng)
        {
            float z = rng.NextFloat() * 2f - 1f, a = rng.NextFloat() * MathF.PI * 2f;
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            return new Vector3(r * MathF.Cos(a), z, r * MathF.Sin(a));
        }

        static Vector3 Tangent(Vector3 v, Vector3 at) => v - Vector3.Dot(v, at) * at;

        static Tectonics Relief(Planet p, Vector3[] unit, PlanetSettings s, int seed)
        {
            var rng = new SimRandom(seed ^ 0x1A2B3C);
            int plates = Math.Max(4, s.Plates);
            var center = new Vector3[plates];
            var drift = new Vector3[plates];
            var continental = new bool[plates];
            var baseHeight = new float[plates];
            int continentalCount = Math.Max(3, (int)MathF.Round(plates * 0.42f));
            for (int k = 0; k < plates; k++)
            {
                center[k] = RandomUnit(rng);
                var t = Tangent(RandomUnit(rng), center[k]);
                drift[k] = (t.LengthSquared() > 1e-6f ? Vector3.Normalize(t) : Vector3.UnitY) * (0.4f + 0.6f * rng.NextFloat());
            }
            // zufällige Auswahl der Kontinentalplatten
            var order = new int[plates];
            for (int k = 0; k < plates; k++) order[k] = k;
            for (int k = plates - 1; k > 0; k--) { int j = rng.Next(k + 1); (order[k], order[j]) = (order[j], order[k]); }
            for (int k = 0; k < plates; k++)
            {
                continental[order[k]] = k < continentalCount;
                baseHeight[order[k]] = k < continentalCount ? 0.42f + 0.12f * rng.NextFloat() : -0.42f + 0.1f * rng.NextFloat();
            }

            int n = p.CellCount;
            var raw = new float[n];
            var orogeny = new float[n];
            var volcanic = new float[n];
            var onContinent = new bool[n];
            const float W = 0.075f; // Breite der Grenzzone (rad, ~480 km)

            for (int i = 0; i < n; i++)
            {
                var u = unit[i];
                // Verzerrung, damit Plattengrenzen nicht wie Großkreise aussehen
                var warp = new Vector3(
                    WorldNoise.Fbm3(u.X * 1.8f + 3.1f, u.Y * 1.8f, u.Z * 1.8f, 3, seed + 11) - 0.5f,
                    WorldNoise.Fbm3(u.X * 1.8f, u.Y * 1.8f + 7.7f, u.Z * 1.8f, 3, seed + 12) - 0.5f,
                    WorldNoise.Fbm3(u.X * 1.8f, u.Y * 1.8f, u.Z * 1.8f + 5.3f, 3, seed + 13) - 0.5f);
                var q = Vector3.Normalize(u + warp * 0.55f);

                int a = 0, b = 1;
                float da = -2f, db = -2f;
                for (int k = 0; k < plates; k++)
                {
                    float d = Vector3.Dot(q, center[k]);
                    if (d > da) { db = da; b = a; da = d; a = k; }
                    else if (d > db) { db = d; b = k; }
                }
                float angA = MathF.Acos(Math.Clamp(da, -1f, 1f)), angB = MathF.Acos(Math.Clamp(db, -1f, 1f));
                float dist = (angB - angA) * 0.5f; // Abstand zur Grenze (rad)
                var normal = Tangent(center[b] - center[a], q);
                normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitX;
                float conv = Vector3.Dot(Tangent(drift[a], q) - Tangent(drift[b], q), normal);
                float fall = MathF.Exp(-(dist / W) * (dist / W));

                bool ca = continental[a], cb = continental[b];
                float uplift = 0f, oro = 0f, volc = 0f;
                if (conv > 0f)
                {
                    if (ca && cb) { uplift = 1.15f * conv * fall; oro = conv * fall; }                 // Kollision (Himalaya)
                    else if (ca)
                    {
                        float off = (dist - 0.03f) / W;
                        float f2 = MathF.Exp(-off * off);
                        uplift = 0.9f * conv * f2; oro = conv * f2; volc = conv * f2 * 0.8f;              // Anden
                    }
                    else if (cb) { uplift = -0.3f * conv * fall; volc = conv * fall * 0.3f; }           // Tiefseegraben
                    else
                    {
                        float off = (dist - 0.02f) / (W * 0.5f);
                        float f2 = MathF.Exp(-off * off);
                        uplift = 0.5f * conv * f2; oro = 0.6f * conv * f2; volc = conv * f2;               // Inselbogen
                    }
                }
                else
                {
                    float div = -conv;
                    if (ca && cb) uplift = -0.28f * div * fall;                                          // Grabenbruch
                    else uplift = 0.16f * div * fall;                                                     // Mittelozeanischer Rücken
                }

                // alte, abgetragene Gebirgsgürtel auf Kontinenten (Erz, flaches Mittelgebirge)
                float ridge = 1f - MathF.Abs(WorldNoise.Fbm3(q.X * 2.6f + 9f, q.Y * 2.6f, q.Z * 2.6f, 4, seed + 21) * 2f - 1f);
                float ancient = ca ? WorldNoise.SmoothStep(0.8f, 0.96f, ridge) : 0f;
                uplift += ancient * 0.16f;
                oro = MathF.Max(oro, ancient * 0.55f);

                float t = WorldNoise.SmoothStep(0f, W * 1.2f, dist);
                float baseH = baseHeight[a] + (1f - t) * ((baseHeight[a] + baseHeight[b]) * 0.5f - baseHeight[a]);
                float detail = WorldNoise.Fbm3(q.X * 2.4f, q.Y * 2.4f, q.Z * 2.4f, 6, seed + 31) - 0.5f;
                float large = WorldNoise.Fbm3(q.X * 1.1f - 4f, q.Y * 1.1f, q.Z * 1.1f, 3, seed + 32) - 0.5f;
                raw[i] = baseH + detail * 0.55f + large * 0.35f + uplift;
                orogeny[i] = WorldNoise.Clamp01(oro * 1.4f);
                volcanic[i] = WorldNoise.Clamp01(volc * 1.4f);
                onContinent[i] = ca;
                p.Plate[i] = a;
            }

            // Meeresspiegel: flächengewichtetes Quantil → fester Landanteil
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, (x, y) => raw[x] != raw[y] ? raw[x].CompareTo(raw[y]) : x.CompareTo(y));
            double total = 0;
            for (int y = 0; y < p.Height; y++) total += p.CellAreaKm2(y) * p.Width;
            double acc = 0, oceanArea = total * (1.0 - s.LandFraction);
            float sea = raw[idx[n - 1]];
            for (int k = 0; k < n; k++)
            {
                acc += p.CellAreaKm2(p.YOf(idx[k]));
                if (acc >= oceanArea) { sea = raw[idx[k]]; break; }
            }
            float maxR = raw[idx[n - 1]], minR = raw[idx[0]];
            for (int i = 0; i < n; i++)
            {
                float r = raw[i];
                if (r > sea)
                {
                    float h = (r - sea) / MathF.Max(1e-4f, maxR - sea);
                    p.Elevation[i] = 8f + 5600f * MathF.Pow(h, 1.55f);
                }
                else
                {
                    float d = (sea - r);
                    if (onContinent[i] && d < 0.12f) p.Elevation[i] = -15f - 175f * (d / 0.12f);   // Schelf
                    else p.Elevation[i] = -200f - 5200f * MathF.Sqrt(WorldNoise.Clamp01(d / MathF.Max(1e-4f, sea - minR)));
                }
                p.Orogeny[i] = orogeny[i];
            }
            return new Tectonics { Volcanic = volcanic };
        }

        // ------------------------------------------------------------ Küstenabstand

        static void CoastDistances(Planet p)
        {
            int n = p.CellCount;
            var dist = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                dist[i] = int.MaxValue;
                bool land = p.IsLand(i);
                for (int k = 0; k < 4; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j >= 0 && p.IsLand(j) != land) { dist[i] = 1; queue.Enqueue(i); break; }
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                bool land = p.IsLand(i);
                for (int k = 0; k < 4; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0 || p.IsLand(j) != land || dist[j] <= dist[i] + 1) continue;
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
            for (int i = 0; i < n; i++) p.CoastDistance[i] = (byte)Math.Min(255, dist[i]);
        }

        // ------------------------------------------------------------ Temperatur

        /// <summary>Jahresmittel auf Meereshöhe nach Breite (°C): Äquator ~27, 45° ~9, Pol ~−28.</summary>
        internal static float SeaLevelTemperature(float latitude) => 27f - 55f * MathF.Pow(MathF.Abs(latitude) / 90f, 1.6f);

        /// <summary>Temperaturabnahme mit der Höhe (K je m).</summary>
        public const float LapseRate = 0.0065f;

        static void Temperatures(Planet p, Vector3[] unit, int seed)
        {
            for (int i = 0; i < p.CellCount; i++)
            {
                float lat = p.LatitudeOf(i);
                float sinLat = MathF.Sin(MathF.Abs(lat) * MathF.PI / 180f);
                var u = unit[i];
                float noise = (WorldNoise.Fbm3(u.X * 4f, u.Y * 4f + 2f, u.Z * 4f, 3, seed + 51) - 0.5f) * 3f;
                float t = SeaLevelTemperature(lat) + noise;
                if (p.IsLand(i))
                {
                    float cont = WorldNoise.Clamp01(p.CoastDistance[i] / 12f);
                    t -= LapseRate * p.Elevation[i];
                    t += (1f - cont) * 2f * sinLat;              // Meer mildert hohe Breiten
                    p.Amplitude[i] = (1.5f + 22f * sinLat) * (0.3f + 0.7f * cont);
                }
                else p.Amplitude[i] = 1f + 6f * sinLat;
                p.Temperature[i] = t;
            }
        }

        // ------------------------------------------------------------ Niederschlag

        /// <summary>Grundniederschlag nach Breite (mm/Jahr): ITCZ nass, Rossbreiten trocken, Westwindzone mäßig, Pole trocken.</summary>
        internal static float LatitudeRain(float latitude)
        {
            float a = MathF.Abs(latitude);
            return 230f + 1900f * MathF.Exp(-(a / 12f) * (a / 12f)) + 1000f * MathF.Exp(-((a - 48f) / 16f) * ((a - 48f) / 16f));
        }

        /// <summary>Windrichtung (x-Schritt je Zelle) im Breitengürtel: Passat aus Ost (−1), Westwind (+1), polarer Ostwind (−1).</summary>
        internal static int WindDirection(float latitude)
        {
            float a = MathF.Abs(latitude);
            return a < 30f ? -1 : a < 60f ? 1 : -1;
        }

        /// <summary>
        /// Niederschlag (mm/Jahr) je Zelle aus Höhe und Temperatur: Feuchte wird je Breitenzeile mit dem Wind
        /// transportiert, über dem Meer aufgefüllt, über Land ausgeregnet – verstärkt beim Aufstieg am Gebirge
        /// (Steigungsregen), dahinter trocken (Regenschatten). Tropen: Feuchte-Recycling über dem Wald.
        /// Anschließend leicht geglättet. Rein (ohne Rauschen), damit testbar.
        /// </summary>
        internal static float[] ComputePrecipitation(int width, int height, float[] elevation, float[] temperature)
        {
            var rain = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                float lat = 90f - (y + 0.5f) * 180f / height;
                int dir = WindDirection(lat);
                float baseRain = LatitudeRain(lat);
                float loss0 = MathF.Abs(lat) < 18f ? 0.012f : 0.022f;
                // Start über dem Meer (Luv-Seite), sonst irgendwo mit halber Feuchte
                int start = -1;
                for (int x = 0; x < width; x++) if (elevation[y * width + x] < 0f) { start = x; break; }
                bool fromSea = start >= 0;
                float m = fromSea ? 1f : 0.5f;
                if (!fromSea) start = 0;
                float prevH = 0f;
                int laps = fromSea ? 1 : 2;
                for (int step = 0; step < width * laps; step++)
                {
                    int x = ((start + dir * step) % width + width) % width;
                    int i = y * width + x;
                    float e = elevation[i];
                    float coldFactor = Math.Clamp((temperature[i] + 25f) / 35f, 0.25f, 1f);
                    float p;
                    if (e < 0f)
                    {
                        m += (1f - m) * 0.35f;
                        p = baseRain * (0.8f + 0.2f * m);
                        prevH = 0f;
                    }
                    else
                    {
                        float lift = MathF.Max(0f, e - prevH) / 1000f;
                        p = baseRain * (0.2f + 0.9f * m) + 1400f * m * lift;
                        m -= m * MathF.Min(0.95f, loss0 + 0.9f * lift);
                        m = MathF.Max(m, 0.04f);
                        prevH = e;
                    }
                    if (laps == 1 || step >= width) rain[i] = p * coldFactor;
                }
            }
            // leicht glätten (3×3, Länge wiederholt sich)
            var smooth = new float[rain.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = 0f, w = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = (x + dx + width) % width;
                        float k = dx == 0 && dy == 0 ? 2f : 1f;
                        sum += rain[yy * width + xx] * k;
                        w += k;
                    }
                }
                smooth[y * width + x] = sum / w;
            }
            return smooth;
        }

        // ------------------------------------------------------------ Abfluss (Priority-Flood)

        static float[] Drainage(Planet p)
        {
            int n = p.CellCount;
            var filled = new float[n];
            var receiver = new int[n];
            var visited = new bool[n];
            var order = new List<int>(n);
            var heap = new MinHeap();
            for (int i = 0; i < n; i++)
            {
                receiver[i] = -1;
                filled[i] = p.Elevation[i];
                if (p.IsLand(i)) continue;
                visited[i] = true;
                // Meereszellen an der Küste sind die Mündungen
                for (int k = 0; k < 8; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j >= 0 && p.IsLand(j)) { heap.Push(0f, i); break; }
                }
            }
            while (heap.Count > 0)
            {
                var (h, i) = heap.Pop();
                if (p.IsLand(i)) order.Add(i);
                for (int k = 0; k < 8; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0 || visited[j]) continue;
                    visited[j] = true;
                    filled[j] = MathF.Max(p.Elevation[j], h + 0.5f);
                    receiver[j] = i;
                    heap.Push(filled[j], j);
                }
            }
            // Abfluss: von oben nach unten aufsummieren (Niederschlag in m × relative Fläche)
            for (int k = order.Count - 1; k >= 0; k--)
            {
                int i = order[k];
                p.Flow[i] += p.Precipitation[i] / 1000f * MathF.Cos(p.LatitudeOf(i) * MathF.PI / 180f);
                int r = receiver[i];
                if (r >= 0 && p.IsLand(r)) p.Flow[r] += p.Flow[i];
            }
            // Flüsse = die abflussreichsten ~7 % der Landzellen
            var flows = new List<float>();
            for (int i = 0; i < n; i++) if (p.IsLand(i)) flows.Add(p.Flow[i]);
            flows.Sort();
            p.RiverThreshold = flows.Count > 0 ? MathF.Max(1.5f, flows[(int)(flows.Count * 0.93f)]) : float.MaxValue;
            return filled;
        }

        // ------------------------------------------------------------ Biome

        internal static Biome ClassifyCell(float elevation, float temperature, float precipitation)
        {
            if (elevation < 0f) return elevation > -200f ? Biome.Shelf : Biome.Ocean;
            if (elevation > 3000f || (elevation > 2200f && temperature < 2f)) return Biome.Mountain;
            if (temperature < -10f) return Biome.Ice;
            if (temperature < -3f) return Biome.Tundra;
            if (temperature < 4f) return precipitation > 300f ? Biome.Taiga : Biome.Tundra;
            if (temperature < 18f)
            {
                if (precipitation < 280f) return Biome.Desert;
                if (precipitation < 620f) return Biome.Grassland;
                if (precipitation > 2000f) return Biome.TemperateRainforest;
                return Biome.TemperateForest;
            }
            if (precipitation < 320f) return Biome.Desert;
            if (precipitation < 1300f) return Biome.Savanna;
            return Biome.TropicalRainforest;
        }

        static void Classify(Planet p)
        {
            for (int i = 0; i < p.CellCount; i++)
                p.BiomeOf[i] = ClassifyCell(p.Elevation[i], p.Temperature[i], p.Precipitation[i]);
        }

        // ------------------------------------------------------------ Böden und Bodenschätze

        static float[] Blur(Planet p, float[] src, int radius, int passes)
        {
            var a = (float[])src.Clone();
            var b = new float[a.Length];
            int w = p.Width, h = p.Height;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int dx = -radius; dx <= radius; dx++) sum += a[y * w + (x + dx + w) % w];
                    b[y * w + x] = sum / (2 * radius + 1);
                }
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f; int cnt = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        sum += b[yy * w + x]; cnt++;
                    }
                    a[y * w + x] = sum / cnt;
                }
            }
            return a;
        }

        /// <summary>Lagerstätten-Häufung: fleckiges Rauschen → 0..1 (die meisten Flächen 0).</summary>
        static float Cluster(Vector3 u, float freq, int seed, float lo = 0.5f, float hi = 0.72f) =>
            WorldNoise.SmoothStep(lo, hi, WorldNoise.Fbm3(u.X * freq + 1.7f, u.Y * freq - 2.3f, u.Z * freq + 0.9f, 4, seed));

        static void Geology(Planet p, Vector3[] unit, Tectonics tect, float[] filled, int seed)
        {
            int n = p.CellCount;
            var landH = new float[n];
            for (int i = 0; i < n; i++) landH[i] = MathF.Max(0f, p.Elevation[i]);
            var blurH = Blur(p, landH, 3, 2);
            var blurOro = Blur(p, p.Orogeny, 2, 2);
            var blurVolc = Blur(p, tect.Volcanic, 1, 2);
            float thr = p.RiverThreshold;

            for (int i = 0; i < n; i++)
            {
                var u = unit[i];
                float e = p.Elevation[i];
                var biome = p.BiomeOf[i];
                if (e < 0f)
                {
                    // Meer: nur Schelf hat Sedimentbecken mit Öl/Gas und Sand
                    if (biome == Biome.Shelf)
                    {
                        float basinSea = 0.55f + 0.45f * WorldNoise.Fbm3(u.X * 5f, u.Y * 5f, u.Z * 5f, 3, seed + 61);
                        p.Basin[i] = basinSea;
                        p.Oil[i] = WorldNoise.Clamp01(basinSea * Cluster(u, 5f, seed + 70, 0.5f, 0.7f) * 1.25f);
                        p.Sand[i] = 0.4f * WorldNoise.Clamp01(1f - (p.CoastDistance[i] - 1) / 3f);
                    }
                    continue;
                }

                float rugged = p.Ruggedness(i);
                float lowland = 1f - WorldNoise.SmoothStep(200f, 900f, e);
                float depression = WorldNoise.Clamp01((blurH[i] - e) / 400f);
                float lake = WorldNoise.Clamp01((filled[i] - e) / 150f);
                float foreland = WorldNoise.Clamp01(blurOro[i] * 1.6f) * lowland * (1f - p.Orogeny[i]);
                float basin = WorldNoise.Clamp01(0.5f * lowland + 0.6f * depression + 0.35f * lake + 0.45f * foreland) * (1f - 0.7f * p.Orogeny[i]);
                p.Basin[i] = basin;

                float riverness = p.Flow[i] >= thr * 0.5f ? WorldNoise.Clamp01(MathF.Log(p.Flow[i] / (thr * 0.5f)) / 2.5f + 0.2f) : 0f;
                bool desert = biome == Biome.Desert;
                bool mountain = biome == Biome.Mountain || e > 2000f;
                float coastal = p.CoastDistance[i] <= 1 ? 1f : 0f;

                // Erz: Gebirgsgürtel (heute und alt) + Schilde, in Häufungen
                float oreBelt = 0.75f * blurOro[i] + 0.25f * WorldNoise.SmoothStep(300f, 2500f, rugged);
                p.Ore[i] = WorldNoise.Clamp01(oreBelt * (0.2f + 1.1f * Cluster(u, 6f, seed + 71, 0.45f, 0.7f)) + 0.12f * Cluster(u, 11f, seed + 72, 0.6f, 0.78f));
                // Kohle: Sedimentbecken (Vorland, Senken), unabhängig vom heutigen Klima
                p.Coal[i] = WorldNoise.Clamp01(basin * (0.1f + 1.0f * Cluster(u, 5f, seed + 73, 0.45f, 0.68f)));
                // Öl: tiefe Becken, seltener als Kohle
                p.Oil[i] = WorldNoise.Clamp01(basin * basin * 1.4f * Cluster(u, 4f, seed + 74, 0.52f, 0.72f));
                // Sand/Kies: Flüsse, Küsten, Wüsten, Gletschervorland
                float glacial = p.Temperature[i] < 1f ? lowland * 0.5f : 0f;
                p.Sand[i] = WorldNoise.Clamp01((0.12f + 0.45f * riverness + 0.35f * coastal + (desert ? 0.45f : 0f) + glacial) * (mountain ? 0.35f : 1f));
                // Grundwasser: Niederschlag + poröse Becken + Flussnähe; Permafrost/Fels wenig; fossile Wüsten-Aquifere
                float gw = WorldNoise.Clamp01(p.Precipitation[i] / 1800f) * (0.45f + 0.55f * MathF.Max(basin, lowland * 0.6f)) + 0.35f * riverness;
                if (desert) gw += 0.25f * basin * Cluster(u, 6f, seed + 75, 0.45f, 0.65f);
                if (p.Temperature[i] < -5f) gw *= 0.3f;
                if (mountain) gw *= 0.6f;
                p.Groundwater[i] = WorldNoise.Clamp01(gw);
                // Wald
                float forestNoise = WorldNoise.Fbm3(u.X * 7f, u.Y * 7f, u.Z * 7f, 3, seed + 76);
                p.Forest[i] = WorldNoise.Clamp01(Biomes.ForestCover(biome) * (0.75f + 0.5f * forestNoise));
                // Boden: Biom, Hangneigung, Auen (Nil-Effekt in Wüsten), vulkanische Böden
                float fert = Biomes.BaseFertility(biome) * (1f - 0.6f * WorldNoise.SmoothStep(200f, 1500f, rugged))
                             + 0.22f * riverness + (desert ? 0.45f * riverness : 0f) + 0.15f * blurVolc[i] * (mountain ? 0.3f : 1f);
                if (p.Temperature[i] < -3f) fert *= 0.4f;
                p.Fertility[i] = WorldNoise.Clamp01(fert);
            }
        }
    }
}
