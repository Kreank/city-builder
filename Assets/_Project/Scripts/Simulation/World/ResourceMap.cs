using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.World
{
    /// <summary>Ids der Ressourcen-Raster (verbindlich – andere Systeme, z. B. Warenketten G5, fragen damit an).</summary>
    public static class ResourceIds
    {
        public const string Fertility = "fertility";
        public const string Wood = "wood";
        public const string Ore = "ore";
        public const string Coal = "coal";
        public const string Oil = "oil";
        public const string Sand = "sand";
        public const string Groundwater = "groundwater";

        public static readonly string[] All = { Fertility, Wood, Ore, Coal, Oil, Sand, Groundwater };
    }

    /// <summary>Art eines Vorkommens: Einheit, Menge bei voller Ergiebigkeit, Erneuerung.</summary>
    public sealed class ResourceKind
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Unit { get; }
        /// <summary>Menge je Hektar bei Ergiebigkeit 1 (Einheit <see cref="Unit"/>).</summary>
        public float AmountPerHectare { get; }
        /// <summary>Erneuert sich (Wald wächst nach, Boden erholt sich, Grundwasser füllt sich auf).</summary>
        public bool Renewable { get; }
        /// <summary>Erneuerung je Tag (Anteil, Bedeutung je Art s. <see cref="ResourceMap.RegenerateDay"/>).</summary>
        public float RegenPerDay { get; }

        public ResourceKind(string id, string name, string unit, float perHectare, bool renewable, float regen)
        {
            Id = id; DisplayName = name; Unit = unit; AmountPerHectare = perHectare; Renewable = renewable; RegenPerDay = regen;
        }

        /// <summary>Katalog (Spielmaßstab, Platzhalter fürs Balancing).</summary>
        public static readonly ResourceKind[] Catalog =
        {
            new ResourceKind(ResourceIds.Fertility, "Bodenfruchtbarkeit", "Bodenpunkte", 100f, true, 0.01f),
            new ResourceKind(ResourceIds.Wood, "Wald/Holz", "m³", 300f, true, 0.004f),
            new ResourceKind(ResourceIds.Ore, "Erz", "t", 50000f, false, 0f),
            new ResourceKind(ResourceIds.Coal, "Kohle", "t", 80000f, false, 0f),
            new ResourceKind(ResourceIds.Oil, "Erdöl", "m³", 20000f, false, 0f),
            new ResourceKind(ResourceIds.Sand, "Sand/Kies", "m³", 30000f, false, 0f),
            new ResourceKind(ResourceIds.Groundwater, "Grundwasser", "m³", 2000f, true, 0.02f),
        };
    }

    /// <summary>Ergiebigkeit 0..1 einer Region je Ressource – Vorgabe für die lokalen Raster der Stadtkarte.</summary>
    public sealed class RegionResources
    {
        public float Fertility = 0.6f, Forest = 0.5f, Ore = 0.35f, Coal = 0.35f, Oil = 0.2f, Sand = 0.5f, Groundwater = 0.6f;

        /// <summary>Standard-Welt ohne Weltkarte: gemäßigte Mischung.</summary>
        public static RegionResources Default => new RegionResources();

        /// <summary>
        /// Aus einer Weltregion: Mittelwerte der Weltzellen, leicht angehoben (die Stadt sitzt an einem günstigen Fleck
        /// der Region, und eine Weltzelle von ~100 km mittelt kleine Lagerstätten weg).
        /// </summary>
        public static RegionResources FromRegion(Region r) => new RegionResources
        {
            Fertility = Lift(r.Fertility), Forest = Lift(r.Forest), Ore = Lift(r.Ore), Coal = Lift(r.Coal),
            Oil = Lift(r.Oil), Sand = Lift(r.Sand), Groundwater = Lift(r.Groundwater),
        };

        static float Lift(float v) => WorldNoise.Clamp01(MathF.Sqrt(WorldNoise.Clamp01(v)) * 1.05f);

        public float this[string id]
        {
            get
            {
                switch (id)
                {
                    case ResourceIds.Fertility: return Fertility;
                    case ResourceIds.Wood: return Forest;
                    case ResourceIds.Ore: return Ore;
                    case ResourceIds.Coal: return Coal;
                    case ResourceIds.Oil: return Oil;
                    case ResourceIds.Sand: return Sand;
                    case ResourceIds.Groundwater: return Groundwater;
                    default: return 0f;
                }
            }
        }
    }

    /// <summary>
    /// Ressourcen-Raster der Stadtkarte (je Id ein <see cref="GridLayer"/> mit der verbleibenden Ergiebigkeit 0..1,
    /// gleiche Zellen wie die Umwelt-Raster). Vorkommen sind endlich (Erz, Kohle, Öl, Sand) bzw. erneuern sich
    /// (Wald, Boden, Grundwasser) – täglich über <see cref="WorldSystem"/>.
    /// <para><b>Verbindliche API</b> (Warenketten docken per Delegat an): <see cref="DepositAt"/> und <see cref="Extract"/>.</para>
    /// </summary>
    public sealed class ResourceMap
    {
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        readonly GridLayer[] layers;
        /// <summary>Natürlicher Ausgangszustand (Zielwert der Erneuerung).</summary>
        readonly float[][] natural;

        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Hektar je Zelle.</summary>
        public float CellHectares => CellSize * CellSize / 10000f;
        public RegionResources Source { get; }

        public IReadOnlyList<ResourceKind> Kinds => ResourceKind.Catalog;

        ResourceMap(Vector2 origin, int cells, float cellSize, RegionResources source)
        {
            Origin = origin; Width = Height = cells; CellSize = cellSize; Source = source;
            layers = new GridLayer[ResourceKind.Catalog.Length];
            natural = new float[layers.Length][];
            for (int k = 0; k < layers.Length; k++)
            {
                index[ResourceKind.Catalog[k].Id] = k;
                layers[k] = new GridLayer(ResourceKind.Catalog[k].DisplayName, origin, cells, cells, cellSize);
                natural[k] = new float[cells * cells];
            }
        }

        public bool Has(string id) => id != null && index.ContainsKey(id);
        public ResourceKind Kind(string id) => Has(id) ? ResourceKind.Catalog[index[id]] : null;
        /// <summary>Raster einer Ressource (für Overlays), null bei unbekannter Id.</summary>
        public GridLayer Layer(string id) => Has(id) ? layers[index[id]] : null;

        /// <summary>Summe der Raster-Versionen (ändert sich bei jeder Entnahme/Erneuerung).</summary>
        public int Version
        {
            get { int v = 0; foreach (var l in layers) v += l.Version; return v; }
        }

        // ------------------------------------------------------------ Verbindliche API

        /// <summary>Verbleibende Ergiebigkeit 0..1 an der Stelle (Zelle); unbekannte Id oder außerhalb → 0.</summary>
        public float DepositAt(string id, Vector2 p)
        {
            if (!Has(id)) return 0f;
            return layers[index[id]].Sample(p);
        }

        /// <summary>
        /// Entnimmt bis zu <paramref name="amount"/> Einheiten (<see cref="ResourceKind.Unit"/>, z. B. t Erz, m³ Holz)
        /// an der Stelle: zuerst aus der Zelle bei <paramref name="p"/>, reicht das nicht, aus den 8 Nachbarzellen.
        /// Liefert die tatsächlich entnommene Menge (0 bei erschöpft/unbekannt/außerhalb) und senkt das Vorkommen.
        /// </summary>
        public float Extract(string id, Vector2 p, float amount)
        {
            if (!Has(id) || !(amount > 0f)) return 0f;
            int k = index[id];
            var layer = layers[k];
            if (!layer.TryWorldToCell(p, out int cx, out int cy)) return 0f;
            float capacity = ResourceKind.Catalog[k].AmountPerHectare * CellHectares;
            var values = layer.RawValues;
            float remaining = amount;
            for (int n = 0; n < 9 && remaining > 0f; n++)
            {
                int x = cx + (n == 0 ? 0 : NX[n - 1]), y = cy + (n == 0 ? 0 : NY[n - 1]);
                if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                int i = y * Width + x;
                float available = values[i] * capacity;
                if (available <= 0f) continue;
                float take = MathF.Min(available, remaining);
                values[i] = MathF.Max(0f, (available - take) / capacity);
                remaining -= take;
            }
            if (remaining < amount) layer.MarkChanged();
            return amount - remaining;
        }

        static readonly int[] NX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] NY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // ------------------------------------------------------------ Weitere Abfragen

        /// <summary>Verbleibende Menge in der Zelle an der Stelle (Einheiten).</summary>
        public float AmountAt(string id, Vector2 p) =>
            Has(id) ? DepositAt(id, p) * ResourceKind.Catalog[index[id]].AmountPerHectare * CellHectares : 0f;

        /// <summary>Gesamte verbleibende Menge auf der Karte (Einheiten).</summary>
        public double TotalAmount(string id)
        {
            if (!Has(id)) return 0;
            int k = index[id];
            double sum = 0;
            foreach (var v in layers[k].RawValues) sum += v;
            return sum * ResourceKind.Catalog[k].AmountPerHectare * CellHectares;
        }

        /// <summary>Natürlicher Ausgangswert 0..1 (Ziel der Erneuerung).</summary>
        public float NaturalAt(string id, Vector2 p)
        {
            if (!Has(id)) return 0f;
            int k = index[id];
            return layers[k].TryWorldToCell(p, out int x, out int y) ? natural[k][y * Width + x] : 0f;
        }

        /// <summary>
        /// Ein Tag Erneuerung: Wald wächst logistisch (+ Anflug, damit Kahlschlag wieder zuwächst), Boden erholt sich
        /// exponentiell zum Ausgangswert, Grundwasser füllt sich je nach Niederschlag (<paramref name="recharge"/> ≈ 1 im Mittel).
        /// </summary>
        public void RegenerateDay(float recharge = 1f)
        {
            for (int k = 0; k < layers.Length; k++)
            {
                var kind = ResourceKind.Catalog[k];
                if (!kind.Renewable) continue;
                var v = layers[k].RawValues;
                var nat = natural[k];
                bool changed = false;
                float r = kind.RegenPerDay;
                for (int i = 0; i < v.Length; i++)
                {
                    float target = nat[i];
                    if (v[i] >= target) continue;
                    float next;
                    if (kind.Id == ResourceIds.Wood)
                        next = v[i] + r * v[i] * (1f - v[i] / target) + 0.0004f * target;
                    else if (kind.Id == ResourceIds.Groundwater)
                        next = v[i] + r * MathF.Max(0f, recharge) * (target - v[i]);
                    else
                        next = v[i] + r * (target - v[i]);
                    v[i] = MathF.Min(target, next);
                    changed = true;
                }
                if (changed) layers[k].MarkChanged();
            }
        }

        /// <summary>Ebenerdige Straßen roden den Wald und versiegeln den Boden darunter (Tunnel/Brücken nicht).</summary>
        public void ClearUnderRoad(RoadSegment s)
        {
            float r = s.Type.HalfWidth + 2f;
            int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / (CellSize * 0.5f)));
            int wood = index[ResourceIds.Wood], fert = index[ResourceIds.Fertility];
            bool changed = false;
            for (int n = 0; n <= samples; n++)
            {
                float t = n / (float)samples;
                if (s.IsCoveredAt(t) || MathF.Abs(s.ElevationAt(t)) > 4f) continue;
                var c = s.Evaluate(t);
                int x0 = Math.Max(0, (int)MathF.Floor((c.X - r - Origin.X) / CellSize)), x1 = Math.Min(Width - 1, (int)MathF.Floor((c.X + r - Origin.X) / CellSize));
                int y0 = Math.Max(0, (int)MathF.Floor((c.Y - r - Origin.Y) / CellSize)), y1 = Math.Min(Height - 1, (int)MathF.Floor((c.Y + r - Origin.Y) / CellSize));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var center = Origin + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
                    if (Vector2.Distance(center, c) > r + CellSize * 0.5f) continue;
                    int i = y * Width + x;
                    foreach (int k in new[] { wood, fert })
                    {
                        if (layers[k].RawValues[i] == 0f && natural[k][i] == 0f) continue;
                        layers[k].RawValues[i] = 0f;
                        natural[k][i] = 0f;
                        changed = true;
                    }
                }
            }
            if (changed) { layers[wood].MarkChanged(); layers[fert].MarkChanged(); }
        }

        // ------------------------------------------------------------ Erzeugung

        /// <summary>Radius um die Kartenmitte ohne Wald (Startgebiet, wie bisher bei den Bäumen).</summary>
        public const float ClearStartRadius = 180f;

        /// <summary>
        /// Lokale Raster aus den Regionswerten und dem Gelände: Boden fruchtbar in Ebenen und Auen, Wald in Flecken
        /// (Dichte nach Region), Erz in Häufungen unter Hügeln, Kohle/Öl in großen Becken (Öl auch unter Wasser),
        /// Sand/Kies an Ufern, Grundwasser in Senken. Wasser hat keinen Boden/Wald.
        /// </summary>
        public static ResourceMap Generate(Vector2 origin, float sizeMeters, float cellSize, RegionResources source, TerrainMap terrain, int seed)
        {
            source ??= RegionResources.Default;
            int cells = Math.Max(1, (int)MathF.Ceiling(sizeMeters / cellSize));
            var map = new ResourceMap(origin, cells, cellSize, source);
            float water = terrain != null ? terrain.WaterLevel : -1000f;
            // Höhenbereich für „Hügel“-Maß
            float minH = float.MaxValue, maxH = float.MinValue;
            if (terrain != null)
                for (int y = 0; y < terrain.Resolution; y += 4)
                for (int x = 0; x < terrain.Resolution; x += 4)
                {
                    float h = terrain.NaturalAt(x, y);
                    minH = MathF.Min(minH, h); maxH = MathF.Max(maxH, h);
                }
            float span = MathF.Max(1f, maxH - MathF.Max(minH, water));

            float[] Raw(string id) => map.layers[map.index[id]].RawValues;
            var fert = Raw(ResourceIds.Fertility); var wood = Raw(ResourceIds.Wood); var ore = Raw(ResourceIds.Ore);
            var coal = Raw(ResourceIds.Coal); var oil = Raw(ResourceIds.Oil); var sand = Raw(ResourceIds.Sand); var gw = Raw(ResourceIds.Groundwater);
            float woodThreshold = 0.78f - 0.5f * source.Forest;

            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                int i = y * cells + x;
                var p = origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
                float h = terrain != null ? terrain.NaturalHeightAt(p) : 0f;
                bool wet = h < water - 0.1f;
                float slope = 0f;
                if (terrain != null)
                {
                    float hx = terrain.NaturalHeightAt(p + new Vector2(cellSize, 0f)) - terrain.NaturalHeightAt(p - new Vector2(cellSize, 0f));
                    float hy = terrain.NaturalHeightAt(p + new Vector2(0f, cellSize)) - terrain.NaturalHeightAt(p - new Vector2(0f, cellSize));
                    slope = MathF.Sqrt(hx * hx + hy * hy) / (2f * cellSize);
                }
                float hill = terrain != null ? WorldNoise.Clamp01((h - MathF.Max(minH, water)) / span) : 0.3f;
                float nearWater = terrain != null && water > -999f ? 1f - WorldNoise.SmoothStep(0.5f, 6f, h - water) : 0f;
                float n1 = WorldNoise.Fbm2(p.X / 260f + 11f, p.Y / 260f - 7f, 4, seed + 101);

                // Boden
                fert[i] = wet ? 0f : WorldNoise.Clamp01(source.Fertility * (0.7f + 0.6f * n1) * (1f - WorldNoise.SmoothStep(0.08f, 0.45f, slope))
                                                         * (0.85f + 0.3f * (1f - hill)) + 0.15f * nearWater * source.Fertility);
                // Wald in Flecken
                float patch = WorldNoise.Fbm2(p.X / 330f + 3.3f, p.Y / 330f + 9.1f, 4, seed + 102);
                float w = wet ? 0f : WorldNoise.Clamp01((patch - woodThreshold) * 3.2f) * (slope > 0.6f ? 0.5f : 1f);
                float r = p.Length();
                if (r < ClearStartRadius + 60f) w *= WorldNoise.SmoothStep(ClearStartRadius, ClearStartRadius + 60f, r);
                wood[i] = w;
                // Erz: Häufungen, bevorzugt unter Hügeln
                float oreBlob = WorldNoise.SmoothStep(0.52f, 0.72f, WorldNoise.Fbm2(p.X / 240f - 5f, p.Y / 240f + 2f, 4, seed + 103));
                ore[i] = WorldNoise.Clamp01(source.Ore * 1.3f * oreBlob * (0.35f + 0.65f * hill));
                // Kohle: große Flöze
                float coalBlob = WorldNoise.SmoothStep(0.5f, 0.68f, WorldNoise.Fbm2(p.X / 480f + 8f, p.Y / 480f - 4f, 3, seed + 104));
                coal[i] = WorldNoise.Clamp01(source.Coal * 1.25f * coalBlob * (1f - 0.4f * hill));
                // Öl: seltene, große Lagerstätten (auch unter Wasser)
                float oilBlob = WorldNoise.SmoothStep(0.56f, 0.72f, WorldNoise.Fbm2(p.X / 650f - 13f, p.Y / 650f + 6f, 3, seed + 105));
                oil[i] = WorldNoise.Clamp01(source.Oil * 1.3f * oilBlob);
                // Sand/Kies: Ufer und Flussbett
                float n2 = WorldNoise.Fbm2(p.X / 180f, p.Y / 180f, 3, seed + 106);
                sand[i] = WorldNoise.Clamp01(source.Sand * (0.25f + 0.9f * nearWater + (wet ? 0.6f : 0f)) * (0.7f + 0.6f * n2));
                // Grundwasser: tiefe Lagen voller
                float n3 = WorldNoise.Fbm2(p.X / 600f + 21f, p.Y / 600f, 3, seed + 107);
                gw[i] = wet ? source.Groundwater : WorldNoise.Clamp01(source.Groundwater * (0.55f + 0.5f * (1f - hill) + 0.25f * nearWater) * (0.75f + 0.5f * n3));
            }
            for (int k = 0; k < map.layers.Length; k++)
            {
                Array.Copy(map.layers[k].RawValues, map.natural[k], map.natural[k].Length);
                map.layers[k].MarkChanged();
            }
            return map;
        }
    }
}
