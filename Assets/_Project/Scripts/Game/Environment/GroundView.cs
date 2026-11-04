using System.Collections.Generic;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Gelände aus der Höhenkarte der Simulation, in Kacheln (Chunks) aufgeteilt. Ersetzt die flache
    /// Platzhalter-Plane. Schneidet Löcher für Tröge/Tunneleinfahrten, zeigt Wasser und hat eine
    /// Unterirdisch-Ansicht (Gelände durchsichtig).
    /// Gelände-„Texturen“ ohne Texturdateien: Vertex-Farben nach Höhe, Steigung, Wassernähe und bearbeitetem
    /// Boden (<see cref="GroundColor"/>), gezeichnet mit dem eigenen URP-Shader <c>Resources/Shaders/GroundVertexColor</c>
    /// (URP/Lit zeigt keine Vertex-Farben). Fehlt der Shader, bleibt das ursprüngliche einfarbige Material.
    /// Neu gebaut werden nur die Chunks im geänderten Rechteck (<see cref="TerrainMap.Changed"/>).
    /// </summary>
    public sealed class GroundView : MonoBehaviour
    {
        const int ChunkCells = 64;

        SimulationRunner runner;
        Renderer original;
        Material groundMaterial;
        Material transparentMaterial;
        Material waterMaterial;
        Material vertexColorMaterial;

        TerrainMap terrain;
        RoadNetwork roads;
        int cells, chunksPerSide;
        bool[] holes;
        MeshFilter[] chunkFilters;
        MeshRenderer[] chunkRenderers;
        GameObject water;
        readonly HashSet<int> dirtyChunks = new HashSet<int>();
        readonly List<(SimVector2 min, SimVector2 max)> dirtyHoleAreas = new List<(SimVector2, SimVector2)>();
        bool appliedUnderground;

        /// <summary>Vom Spieler umgeschaltet (X).</summary>
        public bool UndergroundToggled { get; private set; }
        /// <summary>Vom Werkzeug gesetzt, solange unter dem Gelände gebaut wird.</summary>
        public bool UndergroundForced { get; set; }
        public bool Underground => UndergroundToggled || UndergroundForced;

        public void Init(SimulationRunner r, Renderer originalGround, Material transparentBase)
        {
            runner = r;
            original = originalGround;
            groundMaterial = originalGround.sharedMaterial;
            Color c = groundMaterial.HasProperty("_BaseColor") ? groundMaterial.GetColor("_BaseColor") : groundMaterial.color;
            var shader = Resources.Load<Shader>("Shaders/GroundVertexColor");
            if (shader != null && shader.isSupported)
            {
                vertexColorMaterial = new Material(shader) { name = "GroundVertexColor" };
                groundMaterial = vertexColorMaterial;
                c = Color.white; // Sprites/Default multipliziert die Vertex-Farben ebenfalls
            }
            c.a = 0.22f;
            transparentMaterial = new Material(transparentBase) { color = c, name = "GroundUnderground" };
            waterMaterial = new Material(transparentBase) { color = new Color(0.16f, 0.38f, 0.62f, 0.78f), name = "Water" };
        }

        public void ToggleUnderground() => UndergroundToggled = !UndergroundToggled;

        void Start()
        {
            if (runner == null || original == null) { enabled = false; return; }
            runner.WorldReplaced += OnWorldReplaced; // Speichern & Laden
            BindWorld();
        }

        // --- Speichern & Laden: neue Welt → Abos lösen, Kacheln/Wasser verwerfen, alles neu aufbauen
        void OnWorldReplaced()
        {
            UnbindWorld();
            BindWorld();
        }

        void UnbindWorld()
        {
            if (terrain != null) terrain.Changed -= HandleTerrainChanged;
            if (roads != null) { roads.SegmentAdded -= MarkHoles; roads.SegmentRemoved -= MarkHoles; }
            if (tiles != null) { tiles.Purchased -= HandleTilePurchased; tiles = null; } // Kacheln
            if (chunkFilters != null)
                foreach (var f in chunkFilters)
                    if (f != null)
                    {
                        if (f.sharedMesh != null) Destroy(f.sharedMesh);
                        Destroy(f.gameObject);
                    }
            if (water != null) Destroy(water);
            water = null;
            chunkFilters = null;
            chunkRenderers = null;
            holes = null;
            dirtyChunks.Clear();
            dirtyHoleAreas.Clear();
            appliedUnderground = false;
            tintDay = int.MinValue; recolorQueue.Clear(); // Umwelt (Etappe 6): Jahreszeit-Färbung neu bestimmen
        }
        // --- Ende Speichern & Laden

        void BindWorld()
        {
            terrain = runner.World.Terrain;
            roads = runner.World.Roads;
            cells = terrain.Resolution - 1;
            chunksPerSide = Mathf.CeilToInt(cells / (float)ChunkCells);
            holes = new bool[cells * cells];

            chunkFilters = new MeshFilter[chunksPerSide * chunksPerSide];
            chunkRenderers = new MeshRenderer[chunkFilters.Length];
            for (int i = 0; i < chunkFilters.Length; i++)
            {
                var go = new GameObject($"Terrain Chunk {i}");
                go.transform.SetParent(transform, false);
                chunkFilters[i] = go.AddComponent<MeshFilter>();
                chunkRenderers[i] = go.AddComponent<MeshRenderer>();
                chunkRenderers[i].sharedMaterial = groundMaterial;
                chunkFilters[i].sharedMesh = new Mesh { name = go.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                dirtyChunks.Add(i);
            }
            original.enabled = false;

            if (terrain.WaterLevel > -100f)
            {
                water = GameObject.CreatePrimitive(PrimitiveType.Quad);
                water.name = "Water";
                Destroy(water.GetComponent<Collider>());
                water.transform.SetParent(transform, false);
                float size = cells * terrain.CellSize;
                water.transform.position = new Vector3(terrain.Origin.X + size * 0.5f, terrain.WaterLevel, terrain.Origin.Y + size * 0.5f);
                water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                water.transform.localScale = new Vector3(size, size, 1f);
                var mr = water.GetComponent<MeshRenderer>();
                mr.sharedMaterial = waterMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            roads.SegmentAdded += MarkHoles;
            roads.SegmentRemoved += MarkHoles;
            terrain.Changed += HandleTerrainChanged;
            foreach (var s in roads.Segments) MarkHoles(s);
            // --- Kacheln: nach einem Kauf die Kacheln dieser Kachel neu einfärben
            tiles = runner.World.Tiles;
            if (tiles != null) tiles.Purchased += HandleTilePurchased;
            // --- Ende Kacheln
        }

        // --- Kacheln
        CityBuilder.Simulation.Tiles.TileSystem tiles;
        readonly List<int> pendingChunks = new List<int>();

        float ChunkDistance(int index, Vector3 camPos)
        {
            int cx = index % chunksPerSide, cy = index / chunksPerSide;
            var c = terrain.PointAt(Mathf.Min(cells, cx * ChunkCells + ChunkCells / 2), Mathf.Min(cells, cy * ChunkCells + ChunkCells / 2));
            float dx = c.X - camPos.x, dz = c.Y - camPos.z;
            return dx * dx + dz * dz;
        }

        void HandleTilePurchased(int tx, int ty)
        {
            if (tiles == null) return;
            tiles.TileBounds(tx, ty, out var min, out var max);
            CellRange(min, max, out int x0, out int y0, out int x1, out int y1);
            for (int cy = y0 / ChunkCells; cy <= y1 / ChunkCells; cy++)
            for (int cx = x0 / ChunkCells; cx <= x1 / ChunkCells; cx++)
                recolorQueue.Enqueue(cy * chunksPerSide + cx);
        }
        // --- Ende Kacheln

        void OnDestroy()
        {
            if (runner != null) runner.WorldReplaced -= OnWorldReplaced;
            if (terrain != null) terrain.Changed -= HandleTerrainChanged;
            foreach (var m in new[] { transparentMaterial, waterMaterial, vertexColorMaterial }) if (m != null) Destroy(m);
            if (chunkFilters == null) return;
            foreach (var f in chunkFilters) if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
        }

        void HandleTerrainChanged(SimVector2 min, SimVector2 max)
        {
            CellRange(min, max, out int x0, out int y0, out int x1, out int y1);
            MarkChunks(x0 - 1, y0 - 1, x1 + 1, y1 + 1);
            dirtyHoleAreas.Add((min, max));
        }

        void MarkHoles(RoadSegment s)
        {
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
            float m = s.Type.HalfWidth + terrain.CellSize * 2f;
            dirtyHoleAreas.Add((min - new SimVector2(m), max + new SimVector2(m)));
        }

        void LateUpdate()
        {
            if (holes == null) return;
            if (dirtyHoleAreas.Count > 0) RecomputeHoles();
            UpdateSeasonTint(); // Umwelt (Etappe 6)
            // Kacheln: auf der 6-km-Karte sind es 576 Kacheln – höchstens ~30 ms je Bild bauen (nächste zur Kamera zuerst),
            // damit der Start/das Laden nicht sekundenlang einfriert
            if (dirtyChunks.Count > 0)
            {
                pendingChunks.Clear();
                pendingChunks.AddRange(dirtyChunks);
                var cam = Camera.main;
                if (pendingChunks.Count > 16 && cam != null)
                {
                    var cp = cam.transform.position;
                    pendingChunks.Sort((a, b) => ChunkDistance(a, cp).CompareTo(ChunkDistance(b, cp)));
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (int i in pendingChunks)
                {
                    BuildChunk(i);
                    dirtyChunks.Remove(i);
                    if (watch.ElapsedMilliseconds > 30) break;
                }
            }

            if (Underground != appliedUnderground)
            {
                appliedUnderground = Underground;
                foreach (var r in chunkRenderers)
                {
                    r.sharedMaterial = appliedUnderground ? transparentMaterial : groundMaterial;
                    r.shadowCastingMode = appliedUnderground ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                }
                if (water != null) water.SetActive(!appliedUnderground);
            }
        }

        // ------------------------------------------------------------ Löcher (Tröge)

        void RecomputeHoles()
        {
            foreach (var (min, max) in dirtyHoleAreas)
            {
                CellRange(min, max, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    holes[y * cells + x] = false;

                foreach (var s in roads.Segments)
                {
                    Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var smin, out var smax);
                    if (smax.X < min.X || smax.Y < min.Y || smin.X > max.X || smin.Y > max.Y) continue;
                    if (!s.IsUnderground) continue;
                    Stamp(s, x0, y0, x1, y1);
                }
                MarkChunks(x0, y0, x1, y1);
            }
            dirtyHoleAreas.Clear();
        }

        /// <summary>Löcher dort, wo die Straße im offenen Trog liegt (nicht im überdeckten Tunnel).</summary>
        void Stamp(RoadSegment s, int x0, int y0, int x1, int y1)
        {
            float cell = terrain.CellSize;
            float radius = s.Type.HalfWidth + cell * 0.75f;
            int samples = Mathf.Max(2, Mathf.CeilToInt(s.Length / 2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float e = s.ElevationAt(t);
                if (e >= -0.3f || e <= -RoadBuilder.TunnelDepth) continue;
                var c = s.Evaluate(t);
                CellRange(c - new SimVector2(radius), c + new SimVector2(radius), out int cx0, out int cy0, out int cx1, out int cy1);
                cx0 = Mathf.Max(cx0, x0); cy0 = Mathf.Max(cy0, y0);
                cx1 = Mathf.Min(cx1, x1); cy1 = Mathf.Min(cy1, y1);
                for (int y = cy0; y <= cy1; y++)
                for (int x = cx0; x <= cx1; x++)
                {
                    var center = terrain.PointAt(x, y) + new SimVector2(cell * 0.5f);
                    if (SimVector2.Distance(center, c) <= radius) holes[y * cells + x] = true;
                }
            }
        }

        void CellRange(SimVector2 min, SimVector2 max, out int x0, out int y0, out int x1, out int y1)
        {
            float cell = terrain.CellSize;
            x0 = Mathf.Clamp(Mathf.FloorToInt((min.X - terrain.Origin.X) / cell), 0, cells - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((min.Y - terrain.Origin.Y) / cell), 0, cells - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((max.X - terrain.Origin.X) / cell), 0, cells - 1);
            y1 = Mathf.Clamp(Mathf.FloorToInt((max.Y - terrain.Origin.Y) / cell), 0, cells - 1);
        }

        void MarkChunks(int x0, int y0, int x1, int y1)
        {
            x0 = Mathf.Clamp(x0, 0, cells - 1); y0 = Mathf.Clamp(y0, 0, cells - 1);
            x1 = Mathf.Clamp(x1, 0, cells - 1); y1 = Mathf.Clamp(y1, 0, cells - 1);
            for (int cy = y0 / ChunkCells; cy <= y1 / ChunkCells; cy++)
            for (int cx = x0 / ChunkCells; cx <= x1 / ChunkCells; cx++)
                dirtyChunks.Add(cy * chunksPerSide + cx);
        }

        // ------------------------------------------------------------ Mesh

        void BuildChunk(int index)
        {
            int cx = index % chunksPerSide, cy = index / chunksPerSide;
            int gx0 = cx * ChunkCells, gy0 = cy * ChunkCells;
            int gx1 = Mathf.Min(cells, gx0 + ChunkCells), gy1 = Mathf.Min(cells, gy0 + ChunkCells);
            int w = gx1 - gx0 + 1, h = gy1 - gy0 + 1;

            var verts = new Vector3[w * h];
            var uvs = new Vector2[w * h];
            var colors = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = terrain.PointAt(gx0 + x, gy0 + y);
                verts[y * w + x] = new Vector3(p.X, terrain[gx0 + x, gy0 + y], p.Y);
                uvs[y * w + x] = new Vector2((gx0 + x) * 0.25f, (gy0 + y) * 0.25f);
                colors[y * w + x] = GroundColor(gx0 + x, gy0 + y);
            }

            var tris = new List<int>((w - 1) * (h - 1) * 6);
            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                if (holes[(gy0 + y) * cells + gx0 + x]) continue;
                int b = y * w + x;
                // Normale nach oben (im Uhrzeigersinn von oben gesehen)
                tris.Add(b); tris.Add(b + w); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + w); tris.Add(b + w + 1);
            }

            var mesh = chunkFilters[index].sharedMesh;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------ Gelände-Farben

        static readonly Color Grass = new Color(0.36f, 0.52f, 0.28f);
        static readonly Color Meadow = new Color(0.27f, 0.42f, 0.21f);
        static readonly Color DryGrass = new Color(0.47f, 0.55f, 0.30f);
        static readonly Color Sand = new Color(0.78f, 0.72f, 0.53f);
        static readonly Color WetSand = new Color(0.47f, 0.43f, 0.33f);
        static readonly Color Rock = new Color(0.48f, 0.46f, 0.43f);
        static readonly Color Earth = new Color(0.45f, 0.35f, 0.25f);
        static readonly Color Gravel = new Color(0.55f, 0.52f, 0.47f);
        // --- Umwelt (Etappe 6): Gelände-Farben nach Klima/Jahreszeit (aus world.Climate, ohne Spielzustand)
        static readonly Color Straw = new Color(0.66f, 0.6f, 0.34f);
        static readonly Color Snow = new Color(0.9f, 0.92f, 0.95f);
        float tintDryness, tintSnow;
        int tintDay = int.MinValue;
        readonly Queue<int> recolorQueue = new Queue<int>();
        /// <summary>Kacheln, deren Farben je Bild neu berechnet werden (verteilt, damit der Tageswechsel nicht ruckelt).</summary>
        const int RecolorPerFrame = 3;

        /// <summary>
        /// Einmal je Spieltag: Grastrockenheit und Schneedecke aus dem Klima; ändert sich die Färbung merklich (≥ 0,04), werden alle
        /// Kacheln nach und nach neu eingefärbt (nur Vertex-Farben).
        /// </summary>
        void UpdateSeasonTint()
        {
            var world = runner.World;
            var climate = world?.Climate;
            if (climate != null && world.Clock.Day != tintDay)
            {
                tintDay = world.Clock.Day;
                float dry = Mathf.Round(climate.GrassDrynessOn(tintDay) * 20f) / 20f;
                float snow = Mathf.Round(climate.SnowCoverOn(tintDay) * 20f) / 20f;
                if (Mathf.Abs(dry - tintDryness) >= 0.04f || Mathf.Abs(snow - tintSnow) >= 0.04f)
                {
                    tintDryness = dry;
                    tintSnow = snow;
                    recolorQueue.Clear();
                    for (int i = 0; i < chunkFilters.Length; i++) recolorQueue.Enqueue(i);
                }
            }
            for (int n = 0; n < RecolorPerFrame && recolorQueue.Count > 0; n++)
            {
                int i = recolorQueue.Dequeue();
                if (!dirtyChunks.Contains(i)) RecolorChunk(i);
            }
        }

        void RecolorChunk(int index)
        {
            var mesh = chunkFilters[index].sharedMesh;
            int cx = index % chunksPerSide, cy = index / chunksPerSide;
            int gx0 = cx * ChunkCells, gy0 = cy * ChunkCells;
            int gx1 = Mathf.Min(cells, gx0 + ChunkCells), gy1 = Mathf.Min(cells, gy0 + ChunkCells);
            int w = gx1 - gx0 + 1, h = gy1 - gy0 + 1;
            if (mesh == null || mesh.vertexCount != w * h) { dirtyChunks.Add(index); return; }
            var colors = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                colors[y * w + x] = GroundColor(gx0 + x, gy0 + y);
            mesh.SetColors(colors);
        }
        // --- Ende Umwelt

        /// <summary>
        /// Farbe eines Gitterpunkts: Gras mit dunkleren Wiesenflecken und trockenen Stellen (Rauschen),
        /// Sand am Ufer bzw. nasser Sand unter Wasser, Fels an steilen Hängen (ab ~30°), Erde/Kies auf
        /// bearbeitetem Boden (Straßenböschungen, Terraforming – <see cref="TerrainMap.WorkedAt"/>).
        /// </summary>
        Color32 GroundColor(int gx, int gy)
        {
            float h = terrain[gx, gy];
            int res = terrain.Resolution;
            float cell = terrain.CellSize;
            float dx = (terrain[Mathf.Min(gx + 1, res - 1), gy] - terrain[Mathf.Max(gx - 1, 0), gy]) / (2f * cell);
            float dy = (terrain[gx, Mathf.Min(gy + 1, res - 1)] - terrain[gx, Mathf.Max(gy - 1, 0)]) / (2f * cell);
            float slope = Mathf.Sqrt(dx * dx + dy * dy); // tan(Neigung)

            var p = terrain.PointAt(gx, gy);
            float patches = Mathf.PerlinNoise(p.X * 0.011f + 31.7f, p.Y * 0.011f + 11.3f);
            float detail = Mathf.PerlinNoise(p.X * 0.07f + 5.1f, p.Y * 0.07f + 17.9f);

            var c = Color.Lerp(Grass, Meadow, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.7f, patches)));
            c = Color.Lerp(c, DryGrass, Mathf.InverseLerp(0.62f, 0.9f, detail) * 0.6f);
            // Höhenlagen etwas karger
            c = Color.Lerp(c, Rock, Mathf.InverseLerp(55f, 95f, h) * 0.35f);
            // Umwelt (Etappe 6): trockenes, gelbes Gras bei Dürre/Sommerhitze/trockenem Klima (fleckig nach dem Rauschen)
            if (tintDryness > 0.01f) c = Color.Lerp(c, Straw, tintDryness * (0.45f + 0.55f * detail));

            if (terrain.WaterLevel > -100f)
            {
                float above = h - terrain.WaterLevel;
                float shore = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 2.4f + detail, above));
                c = Color.Lerp(c, Sand, shore);
                if (above < 0f) c = Color.Lerp(Sand, WetSand, Mathf.InverseLerp(0f, 2f, -above));
            }

            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, slope + (detail - 0.5f) * 0.1f));
            c = Color.Lerp(c, Rock, rock);

            float worked = terrain.WorkedAt(gx, gy);
            if (worked > 0.01f)
            {
                var soil = Color.Lerp(Earth, Gravel, detail);
                c = Color.Lerp(c, soil, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.6f, worked)) * 0.85f);
            }
            // Umwelt (Etappe 6): Schneedecke im Winter kalter Regionen – nicht auf steilem Fels und nicht unter Wasser, in Höhenlagen dichter
            if (tintSnow > 0.01f && (terrain.WaterLevel <= -100f || h > terrain.WaterLevel + 0.2f))
            {
                float cover = Mathf.Clamp01(tintSnow * (1f + Mathf.InverseLerp(40f, 90f, h)) - rock * 0.7f) * (0.8f + 0.2f * patches);
                c = Color.Lerp(c, Snow, cover);
            }
            // --- Kacheln: nicht gekauftes Land etwas entsättigt und dunkler (gekauftes Land sticht hervor)
            if (tiles != null && tiles.Restricted && !tiles.OwnedAt(p))
            {
                float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                c = Color.Lerp(c, new Color(g, g, g), 0.35f) * 0.82f;
                c.a = 1f;
            }
            // --- Ende Kacheln
            return c;
        }
    }
}
