using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Eine Zonenzelle (8 × 8 m) neben einer Straße.</summary>
    public struct ZoneCell
    {
        public Vector2 Center;
        /// <summary>Richtung entlang der Straße.</summary>
        public Vector2 Along;
        /// <summary>Richtung von der Straße weg.</summary>
        public Vector2 Out;
        public float Height;
        public bool Valid;
    }

    /// <summary>Zellen auf einer Seite eines Straßensegments: Spalten entlang der Straße, Reihen in die Tiefe.</summary>
    public sealed class ZoneBlock
    {
        public RoadSegment Segment { get; }
        public bool RightSide { get; }
        public int Columns { get; }
        public readonly ZoneCell[] Cells;

        internal ZoneBlock(RoadSegment segment, bool rightSide, int columns)
        {
            Segment = segment;
            RightSide = rightSide;
            Columns = columns;
            Cells = new ZoneCell[columns * ZoneGrid.Depth];
        }

        public ref ZoneCell this[int column, int row] => ref Cells[row * Columns + column];
    }

    /// <summary>
    /// Zonenraster entlang der Straßen (wie in Cities: Skylines): je Seite einer ebenerdigen Stadtstraße
    /// Zellen von 8 × 8 m, bis zu 4 Reihen tief. Wird bei jeder Netzänderung neu berechnet; was gemalt wurde,
    /// steht separat in der <see cref="ZoneMap"/> und bleibt dadurch erhalten.
    /// </summary>
    public sealed class ZoneGrid
    {
        public const float CellSize = 8f;
        public const int Depth = 4;
        /// <summary>Zellmitten näher als das am Straßenrand (Gehweg-Außenkante) sind ungültig.</summary>
        public const float RoadClearance = CellSize * 0.45f;
        /// <summary>Mindestabstand der Zellmitten verschiedener Straßen; die ältere Straße (kleinere Id) gewinnt.</summary>
        public const float CellSpacing = CellSize * 0.85f;
        const float Bucket = 16f;

        readonly RoadNetwork network;
        readonly List<ZoneBlock> blocks = new List<ZoneBlock>();
        readonly Dictionary<long, List<(ZoneBlock block, int column, int row)>> buckets = new Dictionary<long, List<(ZoneBlock, int, int)>>();
        int builtVersion = -1;

        public IReadOnlyList<ZoneBlock> Blocks => blocks;
        /// <summary>Wird bei jedem Neuaufbau erhöht.</summary>
        public int Version { get; private set; }

        public ZoneGrid(RoadNetwork network) => this.network = network;

        /// <summary>Baut das Raster neu auf, falls sich das Straßennetz geändert hat. Liefert true, wenn neu gebaut wurde.</summary>
        public bool Update()
        {
            if (builtVersion == network.Version) return false;
            builtVersion = network.Version;
            Rebuild();
            Version++;
            return true;
        }

        void Rebuild()
        {
            blocks.Clear();
            buckets.Clear();
            var ground = network.Ground;
            var segments = new List<RoadSegment>(network.Segments);
            segments.Sort((a, b) => a.Id.CompareTo(b.Id)); // stabil: ältere Straßen zuerst

            foreach (var s in segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float trim0 = Trim(s.Start, s), trim1 = Trim(s.End, s);
                float usable = s.Length - trim0 - trim1;
                int columns = (int)MathF.Floor(usable / CellSize);
                if (columns < 1) continue;
                float d0 = trim0 + (usable - columns * CellSize) * 0.5f;

                foreach (bool right in new[] { true, false })
                {
                    var block = new ZoneBlock(s, right, columns);
                    bool any = false;
                    for (int c = 0; c < columns; c++)
                    {
                        float t = Math.Clamp((d0 + (c + 0.5f) * CellSize) / s.Length, 0f, 1f);
                        var p = s.Evaluate(t);
                        var tangent = s.Tangent(t);
                        var side = right ? s.Right(t) : -s.Right(t);
                        float roadHeight = s.HeightAt(t);
                        bool atGrade = MathF.Abs(s.ElevationAt(t)) < 1.5f;

                        for (int r = 0; r < Depth; r++)
                        {
                            ref var cell = ref block[c, r];
                            cell.Center = p + side * (s.Type.HalfWidth + (r + 0.5f) * CellSize);
                            cell.Along = right ? tangent : -tangent;
                            cell.Out = side;
                            cell.Height = ground.HeightAt(cell.Center);
                            // Zellen müssen lückenlos an die Straße anschließen
                            bool prevValid = r == 0 || block[c, r - 1].Valid;
                            cell.Valid = prevValid && atGrade && IsBuildable(cell.Center, cell.Height, roadHeight, ground);
                            if (!cell.Valid) continue;
                            AddToBucket(block, c, r, cell.Center);
                            any = true;
                        }
                    }
                    if (any) blocks.Add(block);
                }
            }
        }

        /// <summary>An Kreuzungen beginnt das Raster erst hinter der Kreuzungsfläche.</summary>
        float Trim(RoadNode node, RoadSegment s)
        {
            if (node.SegmentIds.Count <= 1) return 0f;
            float hw = 0f;
            foreach (int id in node.SegmentIds)
                if (id != s.Id) hw = MathF.Max(hw, network.GetSegment(id).Type.HalfWidth);
            return node.SegmentIds.Count == 2 ? 0f : hw + 1f;
        }

        bool IsBuildable(Vector2 center, float height, float roadHeight, IGround ground)
        {
            if (ground.IsWater(center)) return false;
            if (MathF.Abs(height - roadHeight) > 4f) return false;       // zu steil zur Straße
            if (network.FindNearestSegment(center, RoadClearance) != null) return false; // auf/an einer Straße
            // Überlappung mit bereits vergebenen Zellen anderer Straßen
            foreach (var (b, c, r) in Near(center, CellSize * 0.9f))
                if (Vector2.Distance(b[c, r].Center, center) < CellSpacing) return false;
            return true;
        }

        void AddToBucket(ZoneBlock block, int c, int r, Vector2 p)
        {
            long key = Key(p);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(ZoneBlock, int, int)>();
            list.Add((block, c, r));
        }

        static long Key(Vector2 p) => ((long)MathF.Floor(p.X / Bucket) << 32) ^ (uint)(int)MathF.Floor(p.Y / Bucket);

        /// <summary>Alle gültigen Zellen, deren Mittelpunkt im Umkreis liegt.</summary>
        public IEnumerable<(ZoneBlock block, int column, int row)> Near(Vector2 p, float radius)
        {
            int x0 = (int)MathF.Floor((p.X - radius) / Bucket), x1 = (int)MathF.Floor((p.X + radius) / Bucket);
            int y0 = (int)MathF.Floor((p.Y - radius) / Bucket), y1 = (int)MathF.Floor((p.Y + radius) / Bucket);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!buckets.TryGetValue(((long)x << 32) ^ (uint)y, out var list)) continue;
                foreach (var e in list)
                    if (Vector2.Distance(e.Item1[e.Item2, e.Item3].Center, p) <= radius) yield return e;
            }
        }

        public int ValidCellCount
        {
            get { int n = 0; foreach (var b in blocks) foreach (var c in b.Cells) if (c.Valid) n++; return n; }
        }
    }
}
