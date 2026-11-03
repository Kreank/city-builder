using System;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Höhenkarte der Welt (Gitterpunkte im Abstand <see cref="CellSize"/>). Hält die natürliche Höhe
    /// und die aktuelle (von Straßen angepasste) Höhe. Später auch Grundlage für Klima, Fruchtbarkeit, Rohstoffe.
    /// </summary>
    public sealed class TerrainMap : IGround
    {
        readonly float[] natural;
        readonly float[] current;

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
            WaterLevel = -1000f;
        }

        public float this[int x, int y] => current[y * Resolution + x];
        public float NaturalAt(int x, int y) => natural[y * Resolution + x];
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
        public static TerrainMap Generate(Vector2 origin, float sizeMeters, float cellSize, int seed)
        {
            var t = new TerrainMap(origin, sizeMeters, cellSize) { WaterLevel = 0f };
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
                float h = 6f + hills * 18f + edge * (ridge * ridge * 55f + hills * 20f);

                // Fluss: geschwungene Linie in y-Richtung
                float riverX = riverOffset + MathF.Sin(p.Y / 420f + riverPhase) * 160f + MathF.Sin(p.Y / 170f) * 40f;
                float d = MathF.Abs(p.X - riverX);
                float bed = SmoothStep(30f, 110f, d);                               // 0 im Fluss, 1 außerhalb
                h = Lerp(-4f, h, bed);
                t.natural[y * t.Resolution + x] = t.current[y * t.Resolution + x] = h;
            }
            return t;
        }

        // ------------------------------------------------------------ Abfragen

        public float HeightAt(Vector2 p) => Sample(current, p);
        public float NaturalHeightAt(Vector2 p) => Sample(natural, p);
        public bool IsWater(Vector2 p) => HeightAt(p) < WaterLevel - 0.1f;

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

        // ------------------------------------------------------------ Rauschen

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float SmoothStep(float e0, float e1, float x)
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
