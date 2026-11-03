using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Environment;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Navigationsraster der Wasserstraßen (8-m-Zellen): Wassertiefe und Abstand zum Ufer je Zelle, einmal beim Erzeugen
    /// der Welt aus dem <b>natürlichen</b> Gelände gerechnet (Laden erzeugt dasselbe Raster – deterministisch). Darauf sucht
    /// <see cref="FindPath"/> Fahrrinnen: Dijkstra über 8 Nachbarn, nur Zellen tief/breit genug, Ufernähe kostet mehr (die Rinne
    /// läuft in der Gewässermitte). Danach geglättet und gleichmäßig neu abgetastet.
    /// </summary>
    public sealed class NavGrid
    {
        public int Resolution { get; }
        public float Cell { get; }
        public Vector2 Origin { get; }
        readonly float[] depth;
        readonly float[] shore;
        readonly float waterLevel;

        public NavGrid(TerrainMap terrain, Vector2 origin, float size, float cell)
        {
            Cell = cell;
            Origin = origin;
            Resolution = Math.Max(2, (int)MathF.Round(size / cell));
            waterLevel = terrain.WaterLevel;
            int n = Resolution * Resolution;
            depth = new float[n];
            shore = new float[n];
            for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    var p = Center(x, y);
                    // tiefster/flachster Wert der Zelle: an den Ecken und der Mitte (flachster zählt)
                    float h = terrain.NaturalHeightAt(p);
                    float half = cell * 0.45f;
                    h = MathF.Max(h, terrain.NaturalHeightAt(p + new Vector2(half, half)));
                    h = MathF.Max(h, terrain.NaturalHeightAt(p + new Vector2(-half, half)));
                    h = MathF.Max(h, terrain.NaturalHeightAt(p + new Vector2(half, -half)));
                    h = MathF.Max(h, terrain.NaturalHeightAt(p + new Vector2(-half, -half)));
                    depth[y * Resolution + x] = waterLevel - h;
                }
            DistanceToShore();
        }

        public Vector2 Center(int x, int y) => Origin + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Resolution && y < Resolution;

        public void CellOf(Vector2 p, out int x, out int y)
        {
            x = Math.Clamp((int)MathF.Floor((p.X - Origin.X) / Cell), 0, Resolution - 1);
            y = Math.Clamp((int)MathF.Floor((p.Y - Origin.Y) / Cell), 0, Resolution - 1);
        }

        public bool IsWater(int i) => depth[i] > 0.1f;
        public float DepthAt(Vector2 p) { CellOf(p, out int x, out int y); return depth[y * Resolution + x]; }
        public float ShoreAt(Vector2 p) { CellOf(p, out int x, out int y); return shore[y * Resolution + x]; }

        /// <summary>Chamfer-Abstand (m) jeder Wasserzelle zur nächsten Landzelle (zwei Durchläufe, 8 Nachbarn).</summary>
        void DistanceToShore()
        {
            int r = Resolution;
            const float big = 1e9f;
            float d1 = Cell, d2 = Cell * 1.41421356f;
            for (int i = 0; i < depth.Length; i++) shore[i] = IsWater(i) ? big : 0f;
            for (int y = 0; y < r; y++)
                for (int x = 0; x < r; x++)
                {
                    int i = y * r + x;
                    if (shore[i] == 0f) continue;
                    float v = shore[i];
                    if (x > 0) v = MathF.Min(v, shore[i - 1] + d1);
                    if (y > 0)
                    {
                        v = MathF.Min(v, shore[i - r] + d1);
                        if (x > 0) v = MathF.Min(v, shore[i - r - 1] + d2);
                        if (x < r - 1) v = MathF.Min(v, shore[i - r + 1] + d2);
                    }
                    shore[i] = v;
                }
            for (int y = r - 1; y >= 0; y--)
                for (int x = r - 1; x >= 0; x--)
                {
                    int i = y * r + x;
                    if (shore[i] == 0f) continue;
                    float v = shore[i];
                    if (x < r - 1) v = MathF.Min(v, shore[i + 1] + d1);
                    if (y < r - 1)
                    {
                        v = MathF.Min(v, shore[i + r] + d1);
                        if (x < r - 1) v = MathF.Min(v, shore[i + r + 1] + d2);
                        if (x > 0) v = MathF.Min(v, shore[i + r - 1] + d2);
                    }
                    // Kartenrand ist kein Ufer (Wasser läuft draußen weiter): Abstand dort nicht begrenzen
                    shore[i] = v;
                }
            // Zellen am Kartenrand ohne Land in der Nähe: großer Wert bleibt; begrenzen für die Kosten
            for (int i = 0; i < shore.Length; i++) if (shore[i] >= big * 0.5f) shore[i] = 10000f;
        }

        bool Navigable(int i, float minDepth) => depth[i] >= minDepth && shore[i] >= ShippingRules.MinShoreDistance;

        /// <summary>Nächste befahrbare Zelle (Tiefe ≥ minDepth) zu einem Punkt im Umkreis; −1 = keine.</summary>
        public int NearestNavigable(Vector2 p, float minDepth, float radius)
        {
            CellOf(p, out int cx, out int cy);
            int rc = (int)MathF.Ceiling(radius / Cell);
            int best = -1;
            float bestD = float.MaxValue;
            for (int y = cy - rc; y <= cy + rc; y++)
                for (int x = cx - rc; x <= cx + rc; x++)
                {
                    if (!InBounds(x, y)) continue;
                    int i = y * Resolution + x;
                    if (!Navigable(i, minDepth)) continue;
                    float d = Vector2.DistanceSquared(Center(x, y), p);
                    if (d < bestD - 1e-3f) { bestD = d; best = i; }
                }
            return best >= 0 && bestD <= radius * radius ? best : -1;
        }

        /// <summary>
        /// Einquellen-Dijkstra ab <paramref name="source"/> über befahrbare Zellen (Tiefe ≥ minDepth); Rückgabe: Vorgänger je
        /// Zelle (−1 = nicht erreicht, Quelle = sich selbst). Kosten = Weglänge × (1 + Ufernähe).
        /// </summary>
        public int[] Search(int source, float minDepth)
        {
            int n = depth.Length;
            var prev = new int[n];
            var dist = new float[n];
            for (int i = 0; i < n; i++) { prev[i] = -1; dist[i] = float.MaxValue; }
            if (source < 0 || !Navigable(source, minDepth)) return prev;
            var heap = new MinHeap(1024);
            dist[source] = 0f;
            prev[source] = source;
            heap.Push(0f, source);
            int r = Resolution;
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int i);
                if (d > dist[i]) continue;
                int x = i % r, y = i / r;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + DX[k], ny = y + DY[k];
                    if (!InBounds(nx, ny)) continue;
                    int j = ny * r + nx;
                    if (!Navigable(j, minDepth)) continue;
                    float step = (k < 4 ? 1f : 1.41421356f) * Cell;
                    float near = MathF.Max(0f, ShippingRules.PreferredShoreDistance - shore[j]) / ShippingRules.PreferredShoreDistance;
                    float nd = d + step * (1f + 3f * near * near);
                    if (nd < dist[j] - 1e-4f) { dist[j] = nd; prev[j] = i; heap.Push(nd, j); }
                }
            }
            return prev;
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Zellen-Weg von <paramref name="target"/> zurück zur Quelle (aus <see cref="Search"/>), als Mittelpunkte Quelle → Ziel.</summary>
        public List<Vector2> Trace(int[] prev, int target)
        {
            if (target < 0 || prev[target] < 0) return null;
            var cells = new List<Vector2>();
            int cur = target;
            int guard = depth.Length;
            while (guard-- > 0)
            {
                cells.Add(Center(cur % Resolution, cur / Resolution));
                if (prev[cur] == cur) break;
                cur = prev[cur];
            }
            cells.Reverse();
            return cells;
        }

        /// <summary>Kleinste Tiefe und Breite (2 × Uferabstand) entlang eines Linienzugs.</summary>
        public void Measure(IReadOnlyList<Vector2> pts, out float[] depths, out float[] widths)
        {
            depths = new float[pts.Count];
            widths = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                depths[i] = DepthAt(pts[i]);
                widths[i] = MathF.Min(2000f, 2f * ShoreAt(pts[i]));
            }
        }

        /// <summary>Glätten (gleitender Mittelwert, Enden fest) und gleichmäßig neu abtasten.</summary>
        public static List<Vector2> Smooth(List<Vector2> pts, int radius, float spacing)
        {
            if (pts == null || pts.Count < 3) return pts;
            var sm = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == 0 || i == pts.Count - 1) { sm.Add(pts[i]); continue; }
                int r = Math.Min(radius, Math.Min(i, pts.Count - 1 - i));
                var sum = Vector2.Zero;
                for (int k = i - r; k <= i + r; k++) sum += pts[k];
                sm.Add(sum / (2 * r + 1));
            }
            // gleichmäßig neu abtasten
            var cum = new float[sm.Count];
            for (int i = 1; i < sm.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(sm[i - 1], sm[i]);
            float total = cum[sm.Count - 1];
            int count = Math.Max(2, (int)MathF.Ceiling(total / spacing) + 1);
            var result = new List<Vector2>(count);
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float s = total * k / (count - 1);
                while (seg < sm.Count - 2 && cum[seg + 1] < s) seg++;
                float len = cum[seg + 1] - cum[seg];
                float u = len > 1e-4f ? (s - cum[seg]) / len : 0f;
                result.Add(Vector2.Lerp(sm[seg], sm[seg + 1], Math.Clamp(u, 0f, 1f)));
            }
            return result;
        }

        /// <summary>Kleiner binärer Heap (Kosten, Zelle) – deterministisch, ohne <c>PriorityQueue</c> (fehlt in Unitys .NET).</summary>
        sealed class MinHeap
        {
            float[] keys;
            int[] vals;
            public int Count { get; private set; }
            public MinHeap(int capacity) { keys = new float[capacity]; vals = new int[capacity]; }

            public void Push(float k, int v)
            {
                if (Count == keys.Length) { Array.Resize(ref keys, Count * 2); Array.Resize(ref vals, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (Less(keys[p], vals[p], k, v)) break;
                    keys[i] = keys[p]; vals[i] = vals[p];
                    i = p;
                }
                keys[i] = k; vals[i] = v;
            }

            public void Pop(out float k, out int v)
            {
                k = keys[0]; v = vals[0];
                Count--;
                if (Count == 0) return;
                float lk = keys[Count]; int lv = vals[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count && Less(keys[c + 1], vals[c + 1], keys[c], vals[c])) c++;
                    if (Less(lk, lv, keys[c], vals[c])) break;
                    keys[i] = keys[c]; vals[i] = vals[c];
                    i = c;
                }
                keys[i] = lk; vals[i] = lv;
            }

            static bool Less(float ka, int va, float kb, int vb) => ka < kb || (ka == kb && va < vb);
        }
    }
}
