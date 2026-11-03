using System;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Höhenkarte der Welt (Gitterpunkte im Abstand <see cref="CellSize"/>). Hält die natürliche Höhe
    /// und die aktuelle (von Straßen angepasste) Höhe. Später auch Grundlage für Klima, Fruchtbarkeit, Rohstoffe.
    /// </summary>
    public sealed partial class TerrainMap : IGround
    {
        readonly float[] natural;
        readonly float[] current;
        /// <summary>0..1: wie stark der Boden bearbeitet wurde (Straßenböschung, Terraforming) → Erde/Kies statt Gras.</summary>
        readonly float[] worked;

        /// <summary>Gitterpunkte je Seite.</summary>
        public int Resolution { get; }
        public float CellSize { get; }
        public Vector2 Origin { get; }
        public float WaterLevel { get; set; }
        public int Version { get; private set; }

        /// <summary>Ein Bereich der Höhenkarte hat sich geändert (min, max in Weltkoordinaten).</summary>
        public event Action<Vector2, Vector2> Changed;

        public TerrainMap(Vector2 origin, float sizeMeters, float cellSize)
        {
            Origin = origin;
            CellSize = cellSize;
            Resolution = (int)MathF.Ceiling(sizeMeters / cellSize) + 1;
            natural = new float[Resolution * Resolution];
            current = new float[Resolution * Resolution];
            worked = new float[Resolution * Resolution];
            WaterLevel = -1000f;
        }

        public float this[int x, int y] => current[y * Resolution + x];
        public float NaturalAt(int x, int y) => natural[y * Resolution + x];
        /// <summary>0..1: bearbeiteter Boden (Böschung/Terraforming) – für die Darstellung (Erde/Kies).</summary>
        public float WorkedAt(int x, int y) => worked[y * Resolution + x];
        public Vector2 PointAt(int x, int y) => Origin + new Vector2(x * CellSize, y * CellSize);

        // ------------------------------------------------------------ Erzeugen

        public static TerrainMap Flat(Vector2 origin, float sizeMeters, float cellSize, float height = 0f)
        {
            var t = new TerrainMap(origin, sizeMeters, cellSize);
            for (int i = 0; i < t.current.Length; i++) t.natural[i] = t.current[i] = height;
            return t;
        }

        /// <summary>
        /// Hügellandschaft mit Fluss: flache Mitte (Startgebiet), nach außen Hügel bis ~60 m, ein Fluss quer über die Karte.
        /// </summary>
        public static TerrainMap Generate(Vector2 origin, float sizeMeters, float cellSize, int seed) =>
            Generate(origin, sizeMeters, cellSize, seed, null);

        /// <summary>
        /// Wie oben, mit Parametern aus der Region der Weltkarte (Etappe 5): Relief-Stärke, Fluss ja/nein/Breite,
        /// Küste in einer Richtung. <c>null</c>/<see cref="TerrainParams.Default"/> = exakt das bisherige Gelände.
        /// </summary>
        public static TerrainMap Generate(Vector2 origin, float sizeMeters, float cellSize, int seed, TerrainParams parameters)
        {
            // --- Welt (Etappe 5): Regionsparameter (Standard: Faktoren 1 → bitgleich zum alten Gelände)
            var tp = parameters ?? TerrainParams.Default;
            float relief = tp.Relief, riverWidth = tp.RiverWidth;
            Vector2 coastDir = tp.CoastDirection.HasValue && tp.CoastDirection.Value.LengthSquared() > 1e-6f
                ? Vector2.Normalize(tp.CoastDirection.Value) : Vector2.Zero;
            bool coast = coastDir != Vector2.Zero;
            float coastLine = 1f - 2f * Math.Clamp(tp.CoastShare, 0.05f, 0.3f);
            // --- Ende Welt
            var t = new TerrainMap(origin, sizeMeters, cellSize) { WaterLevel = 0f };
            if (coast && MathF.Abs(coastDir.Y) > 0.3f) t.FlowDirection = new Vector2(0f, MathF.Sign(coastDir.Y)); // Wasserqualität: Fluss läuft zum Meer
            var rng = new Random(seed);
            float ox = (float)rng.NextDouble() * 1000f, oy = (float)rng.NextDouble() * 1000f;
            float riverPhase = (float)rng.NextDouble() * MathF.PI * 2f;
            float riverOffset = ((float)rng.NextDouble() - 0.5f) * sizeMeters * 0.3f;
            float half = sizeMeters * 0.5f;

            for (int y = 0; y < t.Resolution; y++)
            for (int x = 0; x < t.Resolution; x++)
            {
                var p = t.PointAt(x, y);
                float distCenter = p.Length();
                float hills = Fbm((p.X + ox) / 650f, (p.Y + oy) / 650f, 5);           // 0..1
                float ridge = 1f - MathF.Abs(Fbm((p.X - oy) / 900f, (p.Y + ox) / 900f, 3) * 2f - 1f);
                float edge = SmoothStep(250f, half * 0.9f, distCenter);            // Mitte flacher
                float h = 6f + hills * 18f * relief + edge * (ridge * ridge * 55f + hills * 20f) * relief;

                // Fluss: geschwungene Linie in y-Richtung
                if (tp.River)
                {
                    float riverX = riverOffset + MathF.Sin(p.Y / 420f + riverPhase) * 160f + MathF.Sin(p.Y / 170f) * 40f;
                    float d = MathF.Abs(p.X - riverX);
                    float bed = SmoothStep(30f * riverWidth, 110f * riverWidth, d); // 0 im Fluss, 1 außerhalb
                    h = Lerp(-4f, h, bed);
                }
                // Küste (Etappe 5): Meer auf der Seite der Region, die ans Meer grenzt; Ufer leicht geschwungen
                if (coast)
                {
                    float s = Vector2.Dot(p, coastDir) / half;
                    float wobble = (Fbm((p.X + 3000f) / 500f, (p.Y - 2000f) / 500f, 3) - 0.5f) * 0.25f;
                    float sea = SmoothStep(coastLine - 0.04f, coastLine + 0.12f, s + wobble);
                    h = Lerp(h, -7f, sea);
                }
                t.natural[y * t.Resolution + x] = t.current[y * t.Resolution + x] = h;
            }
            return t;
        }

        // ------------------------------------------------------------ Abfragen

        public float HeightAt(Vector2 p) => Sample(current, p);
        public float NaturalHeightAt(Vector2 p) => Sample(natural, p);
        public bool IsWater(Vector2 p) => HeightAt(p) < WaterLevel - 0.1f;

        // --- Abwasser/Wasserqualität (G3 erweitert): Fließrichtung der Gewässer (Einheitsvektor). Der erzeugte Fluss läuft
        // in y-Richtung ohne Gefälle (Bett auf −4 m) – die Richtung ist deshalb eine Festlegung: nach −y („Süden“), mit
        // Küste in Nord/Süd-Richtung zum Meer hin. Gilt für alle Wasserzellen (Ausbreitung von Verschmutzung).
        public Vector2 FlowDirection { get; set; } = new Vector2(0f, -1f);
        // --- Ende Wasserqualität

        float Sample(float[] data, Vector2 p)
        {
            float fx = (p.X - Origin.X) / CellSize, fy = (p.Y - Origin.Y) / CellSize;
            int x0 = Math.Clamp((int)MathF.Floor(fx), 0, Resolution - 2);
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, Resolution - 2);
            float u = Math.Clamp(fx - x0, 0f, 1f), v = Math.Clamp(fy - y0, 0f, 1f);
            float a = data[y0 * Resolution + x0], b = data[y0 * Resolution + x0 + 1];
            float c = data[(y0 + 1) * Resolution + x0], d = data[(y0 + 1) * Resolution + x0 + 1];
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        }

        // ------------------------------------------------------------ Straßen formen das Gelände

        /// <summary>Bis zu diesem Höhenunterschied wird das Gelände an die Straße angepasst (Damm/Einschnitt).</summary>
        public const float ConformRange = 4f;
        /// <summary>Größte Dammhöhe/Einschnitttiefe bei geländefolgenden Straßen.</summary>
        public const float MaxCutFill = 6f;
        const float Band = 10f;

        /// <summary>
        /// Passt das Gelände an eine ebenerdige Straße an: unter der Straße auf Fahrbahnhöhe, daneben
        /// weicher Übergang (Böschung). Brücken-, Trog- und Tunnelabschnitte bleiben unberührt.
        /// </summary>
        /// <summary>So tief liegt das Gelände unter einer ebenerdigen Fahrbahn (damit es nie durchscheint).</summary>
        public const float UnderRoad = 0.12f;

        /// <summary>
        /// Vorrangregel der Straßen: (Punkt, vorgeschlagene Höhe) → erlaubte Höhe. Unter ebenerdigen Straßen
        /// liegt das Gelände exakt knapp unter der Fahrbahn, bis eine Rasterzelle daneben darf es nicht höher sein.
        /// Wird von der Welt gesetzt und bei jeder Geländeänderung (Straßen, Grundstücke) angewandt.
        /// </summary>
        public Func<Vector2, float, float> RoadRule { get; set; }

        /// <summary>Breite des Streifens neben der Fahrbahn, der nicht über die Straße ragen darf.</summary>
        public float RoadMargin => CellSize + 0.5f;

        float ApplyRoadRule(int idx, float proposed)
        {
            if (RoadRule == null) return proposed;
            int x = idx % Resolution, y = idx / Resolution;
            return RoadRule(PointAt(x, y), proposed);
        }

        public void ConformTo(RoadSegment segment)
        {
            // Innen (Fahrbahn + eine Rasterzelle) exakt auf Straßenhöhe, damit kein Rasterdreieck über die Kante ragt
            float hw = segment.Type.HalfWidth + RoadMargin;
            float reach = hw + Band;
            // Geländefolgende Straßen: Damm/Einschnitt bis 6 m (darüber hinaus Brücke bzw. Trog/Tunnel)
            float range = segment.FollowsTerrain ? MaxCutFill : ConformRange;
            int samples = Math.Max(2, (int)MathF.Ceiling(segment.Length / (CellSize * 0.5f)));
            Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
            // Je Gitterpunkt: stärkstes Gewicht und die Fahrbahnhöhe des nächsten Abtastpunkts
            var weights = new System.Collections.Generic.Dictionary<int, (float w, float road)>();

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                var c = segment.Evaluate(t);
                float road = segment.HeightAt(t) - UnderRoad;
                float e = road - HeightAt(c);
                if (MathF.Abs(e) > range || NaturalHeightAt(c) < WaterLevel) continue;

                int x0 = Math.Max(0, (int)MathF.Floor((c.X - reach - Origin.X) / CellSize));
                int x1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((c.X + reach - Origin.X) / CellSize));
                int y0 = Math.Max(0, (int)MathF.Floor((c.Y - reach - Origin.Y) / CellSize));
                int y1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((c.Y + reach - Origin.Y) / CellSize));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dist = Vector2.Distance(PointAt(x, y), c);
                    if (dist > reach) continue;
                    float w = dist <= hw ? 1f : 1f - SmoothStep(hw, reach, dist);
                    int idx = y * Resolution + x;
                    if (!weights.TryGetValue(idx, out var old) || w > old.w) weights[idx] = (w, road);
                }
                min = Vector2.Min(min, c - new Vector2(reach));
                max = Vector2.Max(max, c + new Vector2(reach));
            }

            bool changed = false;
            foreach (var kv in weights)
            {
                float road = kv.Value.road;
                if (kv.Value.w >= 1f)
                {
                    // Unter der Fahrbahn die genaue Höhe am nächsten Punkt der Straße (nicht die des Abtastpunkts)
                    var p = PointAt(kv.Key % Resolution, kv.Key / Resolution);
                    road = segment.HeightAt(segment.ClosestT(p)) - UnderRoad;
                }
                float target = ApplyRoadRule(kv.Key, Lerp(current[kv.Key], road, kv.Value.w));
                if (MathF.Abs(target - current[kv.Key]) < 0.005f) continue;
                worked[kv.Key] = MathF.Max(worked[kv.Key], MathF.Min(1f, MathF.Abs(target - current[kv.Key]) / 1.5f) * kv.Value.w);
                current[kv.Key] = target;
                changed = true;
            }
            if (!changed) return;
            Version++;
            Changed?.Invoke(min, max);
        }

        /// <summary>
        /// Ebnet ein (gedrehtes) Rechteck auf die Höhe <paramref name="height"/> ein, mit weicher Böschung
        /// außen herum (für Grundstücke). <paramref name="front"/> = Richtung der Tiefe.
        /// </summary>
        public void FlattenArea(Vector2 center, Vector2 front, float width, float depth, float height, float band = 6f)
        {
            var side = new Vector2(-front.Y, front.X);
            float reach = MathF.Max(width, depth) * 0.75f + band;
            int x0 = Math.Max(0, (int)MathF.Floor((center.X - reach - Origin.X) / CellSize));
            int x1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((center.X + reach - Origin.X) / CellSize));
            int y0 = Math.Max(0, (int)MathF.Floor((center.Y - reach - Origin.Y) / CellSize));
            int y1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((center.Y + reach - Origin.Y) / CellSize));
            bool changed = false;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var d = PointAt(x, y) - center;
                float ox = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, side)) - width * 0.5f);
                float oy = MathF.Max(0f, MathF.Abs(Vector2.Dot(d, front)) - depth * 0.5f);
                float outside = MathF.Sqrt(ox * ox + oy * oy);
                if (outside > band) continue;
                int idx = y * Resolution + x;
                if (natural[idx] < WaterLevel) continue;
                float w = outside <= 0f ? 1f : 1f - SmoothStep(0f, band, outside);
                // Grundstücke dürfen das Gelände nicht über benachbarte Straßen heben
                float target = ApplyRoadRule(idx, Lerp(current[idx], height, w));
                if (MathF.Abs(target - current[idx]) < 0.01f) continue;
                current[idx] = target;
                changed = true;
            }
            if (!changed) return;
            Version++;
            Changed?.Invoke(center - new Vector2(reach), center + new Vector2(reach));
        }

        // ------------------------------------------------------------ Terraforming (Spieler)

        /// <summary>Grenzen fürs Terraforming (absolute Höhe).</summary>
        public const float MinTerraformHeight = -12f, MaxTerraformHeight = 150f;

        /// <summary>
        /// Sperrmaske fürs Terraforming: (min, max) des Pinsels → Abfrage Punkt → 0 (frei) … 1 (gesperrt).
        /// Die Welt setzt sie (Straßen und Gebäude, weich überblendet, siehe <c>TerrainLocks</c>).
        /// Die Abfrage wird je Pinsel einmal erzeugt, damit sie nur Objekte in der Nähe prüfen muss.
        /// </summary>
        public Func<Vector2, Vector2, Func<Vector2, float>> LockQuery { get; set; }

        // --- Stadtkasse (Hook): Erdarbeiten kosten Volumen × Preis. null = kostenlos (Tests ohne Wirtschaft).
        public Economy.IConstructionBudget Budget { get; set; }
        // --- Ende Stadtkasse

        /// <summary>
        /// Wendet einen Pinsel einmal an (Anheben, Absenken, Glätten, Einebnen). Gesperrte Punkte (unter Straßen
        /// und Gebäuden) bleiben, am Rand der Sperre wird die Wirkung weich ausgeblendet. Ändert aktuelle und
        /// natürliche Höhe gleich (Terraforming ist eine gewollte Änderung des Grundgeländes; Straßen bleiben
        /// dadurch „ebenerdig“). Löst <see cref="Changed"/> mit dem geänderten Rechteck aus.
        /// </summary>
        public TerraformResult Terraform(TerraformBrush brush)
        {
            var result = new TerraformResult();
            if (!(brush.Radius > 0f) || !(brush.Strength > 0f)) return result;

            float r = brush.Radius;
            float inner = r * (1f - Math.Clamp(brush.Softness, 0f, 1f));
            var c = brush.Center;
            int x0 = Math.Max(0, (int)MathF.Floor((c.X - r - Origin.X) / CellSize));
            int x1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((c.X + r - Origin.X) / CellSize));
            int y0 = Math.Max(0, (int)MathF.Floor((c.Y - r - Origin.Y) / CellSize));
            int y1 = Math.Min(Resolution - 1, (int)MathF.Ceiling((c.Y + r - Origin.Y) / CellSize));
            if (x0 > x1 || y0 > y1) return result;

            // Stadtkasse: vorher gegen die größtmögliche Erdmenge dieser Anwendung prüfen, danach die echte buchen
            if (Budget != null)
            {
                float reach = r + CellSize;
                double worst = MathF.PI * reach * reach * brush.Strength * Economy.EconomyRules.TerraformCostPerCubicMeter;
                if (!Budget.CanAfford(worst)) { result.InsufficientFunds = true; return result; }
            }

            // Schnappschuss (mit einem Punkt Rand), damit Glätten nicht von der eigenen Reihenfolge abhängt
            int sx0 = Math.Max(0, x0 - 1), sy0 = Math.Max(0, y0 - 1);
            int sx1 = Math.Min(Resolution - 1, x1 + 1), sy1 = Math.Min(Resolution - 1, y1 + 1);
            int sw = sx1 - sx0 + 1;
            var snap = new float[sw * (sy1 - sy0 + 1)];
            for (int y = sy0; y <= sy1; y++)
                Array.Copy(current, y * Resolution + sx0, snap, (y - sy0) * sw, sw);
            Func<int, int, float> Old = (px, py) => snap[(py - sy0) * sw + (px - sx0)];

            var lockAt = LockQuery?.Invoke(c - new Vector2(r), c + new Vector2(r));
            Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);
            float cellArea = CellSize * CellSize;

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = PointAt(x, y);
                float dist = Vector2.Distance(p, c);
                if (dist > r) continue;
                float w = dist <= inner ? 1f : 1f - SmoothStep(inner, r, dist);
                if (lockAt != null)
                {
                    float l = Math.Clamp(lockAt(p), 0f, 1f);
                    if (l >= 0.999f) { result.LockedPoints++; continue; }
                    w *= 1f - l;
                }
                if (w <= 0.001f) continue;

                float old = Old(x, y);
                float step = brush.Strength * w;
                float target;
                switch (brush.Mode)
                {
                    case TerraformMode.Raise: target = old + step; break;
                    case TerraformMode.Lower: target = old - step; break;
                    case TerraformMode.Smooth: target = MoveTowards(old, Neighbourhood(x, y, sx0, sy0, sx1, sy1, Old), step); break;
                    default: target = MoveTowards(old, brush.TargetHeight, step); break;
                }
                target = Math.Clamp(target, MinTerraformHeight, MaxTerraformHeight);
                int idx = y * Resolution + x;
                // Sicherheitsnetz: nie über eine ebenerdige Straße
                target = ApplyRoadRule(idx, target);
                float delta = target - current[idx];
                if (MathF.Abs(delta) < 0.001f) continue;

                current[idx] = target;
                natural[idx] += delta;
                worked[idx] = MathF.Min(1f, worked[idx] + MathF.Abs(delta) * 0.35f);
                result.ChangedPoints++;
                result.Volume += MathF.Abs(delta) * cellArea;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            if (result.ChangedPoints == 0) return result;
            result.Cost = result.Volume * Economy.EconomyRules.TerraformCostPerCubicMeter;
            Budget?.Spend(Economy.BudgetCategory.Terraforming, result.Cost);
            result.Min = min - new Vector2(CellSize);
            result.Max = max + new Vector2(CellSize);
            Version++;
            Changed?.Invoke(result.Min, result.Max);
            return result;
        }

        /// <summary>Mittel der 3×3-Nachbarschaft (aus dem Schnappschuss).</summary>
        static float Neighbourhood(int x, int y, int sx0, int sy0, int sx1, int sy1, Func<int, int, float> old)
        {
            float sum = 0f;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < sx0 || ny < sy0 || nx > sx1 || ny > sy1) continue;
                sum += old(nx, ny);
                n++;
            }
            return sum / n;
        }

        static float MoveTowards(float from, float to, float maxStep) =>
            MathF.Abs(to - from) <= maxStep ? to : from + MathF.Sign(to - from) * maxStep;

        // ------------------------------------------------------------ Rauschen

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        internal static float SmoothStep(float e0, float e1, float x)
        {
            float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float ValueNoise(float x, float y)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float u = x - xi, v = y - yi;
            u = u * u * (3f - 2f * u);
            v = v * v * (3f - 2f * v);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x, y) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
