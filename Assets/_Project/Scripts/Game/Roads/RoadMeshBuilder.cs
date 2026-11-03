using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Erzeugt Platzhalter-Meshes für Straßen aus dem Spurquerschnitt des Straßentyps:
    /// Fahrbahn, Gehwege mit Bordstein, Busspuren, Gleise, Markierungen, Richtungspfeile
    /// sowie bei Brücken Fahrbahnplatte, Seitenwände und Pfeiler.
    /// </summary>
    public static class RoadMeshBuilder
    {
        public const float RoadHeight = 0.05f;
        const float MarkingHeight = 0.07f;
        const float SidewalkHeight = 0.2f;
        const float MedianHeight = 0.15f;
        const float RailHeight = 0.09f;
        const float LineWidth = 0.15f;
        const float TrackGauge = 1.435f;
        public const float DeckThickness = 0.8f;
        /// <summary>Abstand der Brückenpfeiler (Bogenlänge), erster nach der halben Spannweite.</summary>
        public const float BridgePillarSpacing = PillarSpacing;

        // --- Eigene Assets: Brückenpfeiler als Blender-Modell (Models/Infrastructure/BridgePillar.fbx) statt Mesh-Quader
        static int modelPillars = -1;
        /// <summary>Zeichnet die <c>InfrastructureDecoView</c> die Pfeiler als Modell (dann baut das Segment-Mesh keine)?</summary>
        public static bool ModelPillars
        {
            get
            {
                if (modelPillars < 0)
                {
                    var c = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
                    modelPillars = c != null && c.Palette != null && c.Find(CityBuilder.Game.Assets.OwnModelRoles.Infrastructure, "bridge_pillar") != null ? 1 : 0;
                }
                return modelPillars == 1;
            }
        }
        /// <summary>Stelle eines Bauwerks-Details auf einem Segment (Pfeiler, Tunnelportal) für die Modell-Darstellung.</summary>
        public struct StructureSpot
        {
            public SimVector2 Position;
            /// <summary>Quer zur Straße (rechts) bzw. beim Portal: Richtung aus dem Tunnel heraus.</summary>
            public SimVector2 Right, Out;
            public float RoadHeight, GroundHeight;
        }

        /// <summary>
        /// Pfeiler- und Portalstellen eines Segments – dieselben Regeln wie im Segment-Mesh (Pfeiler je
        /// <see cref="BridgePillarSpacing"/> ab der halben Spannweite, nur ≥ 2 m über Gelände; Portal am Übergang Trog → Tunnel).
        /// </summary>
        public static void StructureSpots(RoadSegment segment, float structureTrimStart, float structureTrimEnd, IGround ground,
            List<StructureSpot> pillars, List<StructureSpot> portals)
        {
            var curve = new Curve(segment.Start.Position, segment.Control, segment.End.Position, segment.HeightAt, ground);
            float s0 = Mathf.Clamp(structureTrimStart, 0f, curve.Length * 0.5f);
            float s1 = curve.Length - Mathf.Clamp(structureTrimEnd, 0f, curve.Length * 0.5f);
            if (pillars != null && curve.MaxElevation > 0.6f)
                for (float d = s0 + PillarSpacing * 0.5f; d < s1; d += PillarSpacing)
                {
                    curve.At(d, out var p, out var r, out float h, out float g);
                    if (h - g < 2f) continue;
                    pillars.Add(new StructureSpot { Position = p, Right = r, RoadHeight = h, GroundHeight = g });
                }
            if (portals != null && curve.MinElevation < -0.3f)
                foreach (float d in curve.Crossings(-RoadBuilder.TunnelDepth))
                {
                    if (d < s0 || d > s1) continue;
                    curve.At(d, out var p, out var r, out float h, out float g);
                    // Richtung zur offenen Seite (Trog): dort liegt die Straße weniger tief unter dem Gelände
                    curve.At(Mathf.Max(0f, d - 2f), out var pa, out _, out float ha, out float ga);
                    curve.At(Mathf.Min(curve.Length, d + 2f), out var pb, out _, out float hb, out float gb);
                    var along = pb - pa;
                    along = along.LengthSquared() > 1e-6f ? SimVector2.Normalize(along) : new SimVector2(-r.Y, r.X);
                    var dir = (ha - ga) > (hb - gb) ? -along : along;
                    portals.Add(new StructureSpot { Position = p, Right = r, Out = dir, RoadHeight = h, GroundHeight = g });
                }
        }
        // --- Ende Eigene Assets
        const float PillarSpacing = 36f;

        // ------------------------------------------------------------ Kurvenabtastung

        /// <summary>
        /// Abgetastete Kurve mit Bogenlänge, Höhe und Geländehöhe – erlaubt Streifen "von Meter x bis Meter y".
        /// Geometrie (inkl. Innenkanten enger Kurven ohne Selbstschnitt) kommt aus <see cref="SampledCurve"/>.
        /// </summary>
        sealed class Curve
        {
            readonly SampledCurve sc;
            readonly float[] grounds;
            public float Length => sc.Length;

            public Curve(SimVector2 p0, SimVector2 p1, SimVector2 p2, float h0 = 0f, float h1 = 0f, IGround ground = null)
                : this(p0, p1, p2, t => Mathf.Lerp(h0, h1, t), ground) { }

            /// <summary>Kurve mit beliebigem Höhenverlauf (z. B. geländefolgendes Profil).</summary>
            public Curve(SimVector2 p0, SimVector2 p1, SimVector2 p2, System.Func<float, float> heightAt, IGround ground = null)
            {
                sc = new SampledCurve(p0, p1, p2, heightAt);
                grounds = new float[sc.Count];
                for (int i = 0; i < sc.Count; i++) grounds[i] = ground != null ? ground.HeightAt(sc.Point(i)) : 0f;
            }

            /// <summary>Wie <see cref="At(float, out SimVector2, out SimVector2, out float)"/>, zusätzlich Geländehöhe.</summary>
            public void At(float d, out SimVector2 pos, out SimVector2 right, out float baseHeight, out float groundHeight)
            {
                sc.At(d, out pos, out right, out baseHeight);
                int i = sc.Locate(d, out float u);
                groundHeight = Mathf.Lerp(grounds[i], grounds[i + 1], u);
            }

            public void At(float d, out SimVector2 pos, out SimVector2 right, out float baseHeight) =>
                sc.At(d, out pos, out right, out baseHeight);

            /// <summary>Punkt bei Meter d, seitlich um offset versetzt (positiv = rechts) – in engen Kurven ohne Selbstschnitt.</summary>
            public SimVector2 Offset(float d, float offset) => sc.OffsetAt(d, offset);

            /// <summary>Größte Höhe über Gelände.</summary>
            public float MaxElevation
            {
                get { float m = float.MinValue; for (int i = 0; i < sc.Count; i++) m = Mathf.Max(m, sc.Height(i) - grounds[i]); return m; }
            }

            /// <summary>Kleinste Höhe über Gelände (negativ = unter der Erde).</summary>
            public float MinElevation
            {
                get { float m = float.MaxValue; for (int i = 0; i < sc.Count; i++) m = Mathf.Min(m, sc.Height(i) - grounds[i]); return m; }
            }

            /// <summary>Stellen (Bogenlänge), an denen die Höhe über Gelände den Wert <paramref name="level"/> durchläuft.</summary>
            public List<float> Crossings(float level)
            {
                var result = new List<float>();
                for (int i = 0; i < sc.Count - 1; i++)
                {
                    float a = sc.Height(i) - grounds[i] - level, b = sc.Height(i + 1) - grounds[i + 1] - level;
                    if ((a > 0f) == (b > 0f) || Mathf.Approximately(a, b)) continue;
                    float u = a / (a - b);
                    result.Add(Mathf.Lerp(sc.Distance(i), sc.Distance(i + 1), u));
                }
                return result;
            }

            /// <summary>Stützstellen zwischen d0 und d1 (inkl. Ränder).</summary>
            public void Stations(float d0, float d1, List<float> result) => sc.Stations(d0, d1, result);
        }

        // ------------------------------------------------------------ Mesh-Daten

        sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Triangles;
            readonly List<float> stations = new List<float>();

            public MeshData(int submeshes)
            {
                Triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) Triangles[i] = new List<int>();
            }

            /// <summary>Flacher Streifen zwischen den seitlichen Abständen left..right (positiv = rechts), Höhe relativ zur Fahrbahn.</summary>
            public void Strip(Curve c, float d0, float d1, float left, float right, float height, int surface, bool facingDown = false)
            {
                if (d1 - d0 < 0.01f || right - left < 0.001f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                foreach (float d in stations)
                {
                    c.At(d, out _, out _, out float h);
                    Vertices.Add(SimConvert.ToWorld(c.Offset(d, left), h + height));
                    Vertices.Add(SimConvert.ToWorld(c.Offset(d, right), h + height));
                    Uvs.Add(new Vector2(0f, d / 4f));
                    Uvs.Add(new Vector2(1f, d / 4f));
                }
                var tris = Triangles[surface];
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    int b = baseIndex + i * 2;
                    if (!facingDown)
                    {
                        // Im Uhrzeigersinn von oben gesehen → Normale nach oben
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                        tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    }
                    else
                    {
                        tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                        tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                    }
                }
            }

            /// <summary>Senkrechte Wand bei seitlichem Abstand offset (Höhen relativ zur Fahrbahn), beidseitig sichtbar.</summary>
            public void Wall(Curve c, float d0, float d1, float offset, float bottom, float top, int surface)
            {
                if (d1 - d0 < 0.01f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                foreach (float d in stations)
                {
                    c.At(d, out _, out _, out float h);
                    var q = c.Offset(d, offset);
                    Vertices.Add(SimConvert.ToWorld(q, h + bottom));
                    Vertices.Add(SimConvert.ToWorld(q, h + top));
                    Uvs.Add(new Vector2(0f, d / 4f));
                    Uvs.Add(new Vector2(1f, d / 4f));
                }
                var tris = Triangles[surface];
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    int b = baseIndex + i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            /// <summary>
            /// Streifen nur dort, wo die Höhe über Gelände in [minElev, maxElev] liegt.
            /// onGround = true: <paramref name="height"/> ist relativ zum Gelände statt zur Fahrbahn.
            /// </summary>
            public void StripWhere(Curve c, float d0, float d1, float left, float right, float height, int surface,
                float minElev, float maxElev, bool onGround = false, bool facingDown = false)
            {
                if (d1 - d0 < 0.01f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                var ok = new bool[stations.Count];
                for (int i = 0; i < stations.Count; i++)
                {
                    c.At(stations[i], out _, out _, out float h, out float g);
                    ok[i] = h - g >= minElev && h - g <= maxElev;
                    float y = onGround ? g + height : h + height;
                    Vertices.Add(SimConvert.ToWorld(c.Offset(stations[i], left), y));
                    Vertices.Add(SimConvert.ToWorld(c.Offset(stations[i], right), y));
                    Uvs.Add(new Vector2(0f, stations[i] / 4f));
                    Uvs.Add(new Vector2(1f, stations[i] / 4f));
                }
                var tris = Triangles[surface];
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    if (!ok[i] || !ok[i + 1]) continue;
                    int b = baseIndex + i * 2;
                    if (!facingDown)
                    {
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                        tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    }
                    else
                    {
                        tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                        tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                    }
                }
            }

            /// <summary>
            /// Senkrechte Wand; Unter-/Oberkante als Funktion von (Fahrbahnhöhe, Geländehöhe).
            /// Nur dort, wo die Höhe über Gelände in [minElev, maxElev] liegt.
            /// </summary>
            public void WallWhere(Curve c, float d0, float d1, float offset, System.Func<float, float, float> bottom,
                System.Func<float, float, float> top, int surface, float minElev, float maxElev)
            {
                if (d1 - d0 < 0.01f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                var ok = new bool[stations.Count];
                for (int i = 0; i < stations.Count; i++)
                {
                    c.At(stations[i], out _, out _, out float h, out float g);
                    ok[i] = h - g >= minElev && h - g <= maxElev;
                    var q = c.Offset(stations[i], offset);
                    Vertices.Add(SimConvert.ToWorld(q, bottom(h, g)));
                    Vertices.Add(SimConvert.ToWorld(q, top(h, g)));
                    Uvs.Add(new Vector2(0f, stations[i] / 4f));
                    Uvs.Add(new Vector2(1f, stations[i] / 4f));
                }
                var tris = Triangles[surface];
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    if (!ok[i] || !ok[i + 1]) continue;
                    int b = baseIndex + i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            /// <summary>Senkrechte Fläche quer zur Straße (Tunnelportal), beidseitig sichtbar.</summary>
            public void CrossWall(Curve c, float d, float left, float right, float y0, float y1, int surface)
            {
                var pl = c.Offset(d, left);
                var pr = c.Offset(d, right);
                int b = Vertices.Count;
                Vertices.Add(SimConvert.ToWorld(pl, y0));
                Vertices.Add(SimConvert.ToWorld(pr, y0));
                Vertices.Add(SimConvert.ToWorld(pl, y1));
                Vertices.Add(SimConvert.ToWorld(pr, y1));
                for (int k = 0; k < 4; k++) Uvs.Add(Vector2.zero);
                var tris = Triangles[surface];
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
            }

            public void Dashed(Curve c, float d0, float d1, float offset, float width, float dash, float gap, int surface)
            {
                float start = d0 + gap * 0.5f;
                for (float d = start; d < d1; d += dash + gap)
                    Strip(c, d, Mathf.Min(d + dash, d1), offset - width * 0.5f, offset + width * 0.5f, MarkingHeight, surface);
            }

            /// <summary>Richtungspfeil auf einer Spur (Spitze in Kurvenrichtung).</summary>
            public void Arrow(Curve c, float d, float offset, int surface)
            {
                Strip(c, d, d + 2.5f, offset - 0.15f, offset + 0.15f, MarkingHeight, surface);
                c.At(d + 2.5f, out var p, out var r, out float h);
                c.At(d + 4.5f, out var tip, out var r2, out float h2);
                int b = Vertices.Count;
                Vertices.Add(SimConvert.ToWorld(p + r * (offset - 0.6f), h + MarkingHeight));
                Vertices.Add(SimConvert.ToWorld(tip + r2 * offset, h2 + MarkingHeight));
                Vertices.Add(SimConvert.ToWorld(p + r * (offset + 0.6f), h + MarkingHeight));
                Uvs.Add(Vector2.zero); Uvs.Add(Vector2.up); Uvs.Add(Vector2.right);
                Triangles[surface].Add(b); Triangles[surface].Add(b + 1); Triangles[surface].Add(b + 2);
            }

            /// <summary>Senkrechter Quader (Pfeiler) von y0 bis y1.</summary>
            public void Pillar(SimVector2 center, SimVector2 right, float halfAcross, float halfAlong, float y0, float y1, int surface)
            {
                var fwd = new SimVector2(-right.Y, right.X);
                var corners = new[]
                {
                    center - right * halfAcross - fwd * halfAlong,
                    center + right * halfAcross - fwd * halfAlong,
                    center + right * halfAcross + fwd * halfAlong,
                    center - right * halfAcross + fwd * halfAlong,
                };
                var tris = Triangles[surface];
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i];
                    var bq = corners[(i + 1) % 4];
                    int b = Vertices.Count;
                    Vertices.Add(SimConvert.ToWorld(a, y0));
                    Vertices.Add(SimConvert.ToWorld(bq, y0));
                    Vertices.Add(SimConvert.ToWorld(a, y1));
                    Vertices.Add(SimConvert.ToWorld(bq, y1));
                    for (int k = 0; k < 4; k++) Uvs.Add(Vector2.zero);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            public Mesh ToMesh(Mesh mesh)
            {
                if (mesh == null) mesh = new Mesh();
                mesh.Clear();
                if (Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, Uvs);
                mesh.subMeshCount = Triangles.Length;
                for (int i = 0; i < Triangles.Length; i++) mesh.SetTriangles(Triangles[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ------------------------------------------------------------ Straße

        /// <summary>
        /// Baut das Mesh eines Segments. trimStart/trimEnd kürzen das Segment an Kreuzungen und Knicken
        /// (<see cref="JunctionTrim"/>), damit Kreuzungsfläche bzw. Eckstück anschließen, ohne zu überlappen.
        /// structureTrimStart/-End kürzen zusätzlich Brückenplatte, Brüstung und Trogwände (an Knicken, wo das
        /// Eckstück sie selbst mitbringt); an Kreuzungen laufen sie unter der Kreuzungsfläche durch (0).
        /// </summary>
        public static Mesh BuildSegment(RoadSegment segment, float trimStart, float trimEnd, Mesh mesh = null, IGround ground = null,
            float structureTrimStart = 0f, float structureTrimEnd = 0f)
        {
            var curve = new Curve(segment.Start.Position, segment.Control, segment.End.Position, segment.HeightAt, ground);
            float d0 = Mathf.Clamp(trimStart, 0f, curve.Length * 0.5f);
            float d1 = curve.Length - Mathf.Clamp(trimEnd, 0f, curve.Length * 0.5f);
            float s0 = Mathf.Clamp(structureTrimStart, 0f, curve.Length * 0.5f);
            float s1 = curve.Length - Mathf.Clamp(structureTrimEnd, 0f, curve.Length * 0.5f);
            var data = new MeshData(RoadMaterials.SurfaceCount);
            BuildRoad(data, curve, segment.Type, d0, d1, s0, s1);
            return data.ToMesh(mesh);
        }

        /// <summary>
        /// Eckstück an einem Knick zweier Segmente gleichen Typs: voller Querschnitt (Fahrbahn, Gehwege,
        /// Bordstein, Markierungen, ggf. Brücke/Trog) entlang einer Bézierkurve zwischen den gekürzten Enden –
        /// innen gerundet, außen Bogen. Weltkoordinaten.
        /// </summary>
        public static Mesh BuildBend(BendLayout bend, Mesh mesh = null, IGround ground = null)
        {
            var curve = new Curve(bend.P0, bend.Control, bend.P2, bend.H0, bend.H2, ground);
            var data = new MeshData(RoadMaterials.SurfaceCount);
            BuildRoad(data, curve, bend.Type, 0f, curve.Length, 0f, curve.Length);
            return data.ToMesh(mesh);
        }

        /// <summary>Querschnitt eines Straßentyps entlang der Kurve: Fahrbahn d0..d1, Bauwerke s0..s1.</summary>
        static void BuildRoad(MeshData data, Curve curve, RoadType type, float d0, float d1, float s0, float s1)
        {
            var lanes = type.Lanes;
            bool highway = type.Category != RoadCategory.City;
            float dash = highway ? 6f : 3f, gap = highway ? 12f : 6f;
            int concrete = (int)RoadSurface.Sidewalk;

            // Spurflächen
            foreach (var lane in lanes)
            {
                switch (lane.Kind)
                {
                    case LaneKind.Sidewalk:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, SidewalkHeight, concrete);
                        float curb = lane.Offset < 0f ? lane.Right : lane.Left;
                        data.Wall(curve, d0, d1, curb, RoadHeight, SidewalkHeight, concrete);
                        break;
                    case LaneKind.Median:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, MedianHeight, (int)RoadSurface.Median);
                        break;
                    case LaneKind.Bus:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, RoadHeight, (int)RoadSurface.BusLane);
                        break;
                    case LaneKind.Tram:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, RoadHeight, (int)RoadSurface.TramBed);
                        float g = TrackGauge * 0.5f;
                        data.Strip(curve, d0, d1, lane.Offset - g - 0.06f, lane.Offset - g + 0.06f, RailHeight, (int)RoadSurface.Rail);
                        data.Strip(curve, d0, d1, lane.Offset + g - 0.06f, lane.Offset + g + 0.06f, RailHeight, (int)RoadSurface.Rail);
                        break;
                    default:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, RoadHeight, (int)RoadSurface.Asphalt);
                        break;
                }
            }

            // Markierungen zwischen benachbarten Spuren
            int marking = (int)RoadSurface.Marking;
            for (int i = 0; i < lanes.Count - 1; i++)
            {
                var a = lanes[i];
                var b = lanes[i + 1];
                float x = a.Right;
                if (!a.IsVehicleLane || !b.IsVehicleLane)
                {
                    if (highway && (a.IsVehicleLane || b.IsVehicleLane))
                        data.Strip(curve, d0, d1, x - LineWidth, x + LineWidth, MarkingHeight, marking);
                    continue;
                }

                bool carCar = a.Kind == LaneKind.Car && b.Kind == LaneKind.Car;
                if (a.Kind == LaneKind.Tram && b.Kind == LaneKind.Tram) continue;

                if (a.Direction != b.Direction)
                {
                    bool narrowRoad = type.CarLanesForward <= 1 && type.CarLanesBackward <= 1;
                    if (carCar && narrowRoad) data.Dashed(curve, d0, d1, x, LineWidth, dash, gap, marking);
                    else
                    {
                        data.Strip(curve, d0, d1, x - 0.3f, x - 0.3f + LineWidth, MarkingHeight, marking);
                        data.Strip(curve, d0, d1, x + 0.3f - LineWidth, x + 0.3f, MarkingHeight, marking);
                    }
                }
                else if (carCar) data.Dashed(curve, d0, d1, x, LineWidth, dash, gap, marking);
                else data.Strip(curve, d0, d1, x - 0.125f, x + 0.125f, MarkingHeight, marking);
            }

            // Richtungspfeile auf Einbahnstraßen, Autobahnen und Rampen
            if (type.IsOneWay)
            {
                float spacing = highway ? 80f : 40f;
                foreach (var lane in lanes)
                {
                    if (lane.Kind != LaneKind.Car && lane.Kind != LaneKind.Bus) continue;
                    for (float d = d0 + 8f; d + 5f < d1 - 4f; d += spacing)
                        data.Arrow(curve, d, lane.Offset, marking);
                }
            }

            float hw = type.HalfWidth;

            // Brücke: Fahrbahnplatte, Brüstung und Pfeiler – nur wo die Straße deutlich über dem Gelände liegt
            if (curve.MaxElevation > 0.6f)
            {
                data.StripWhere(curve, s0, s1, -hw, hw, -DeckThickness, concrete, 0.6f, float.MaxValue, facingDown: true);
                System.Func<float, float, float> deckBottom = (h, g) => h - DeckThickness;
                System.Func<float, float, float> parapet = (h, g) => h + SidewalkHeight + 0.6f;
                data.WallWhere(curve, s0, s1, -hw, deckBottom, parapet, concrete, 0.6f, float.MaxValue);
                data.WallWhere(curve, s0, s1, hw, deckBottom, parapet, concrete, 0.6f, float.MaxValue);

                // Eigene Assets: mit Modell „BridgePillar“ zeichnet die InfrastructureDecoView die Pfeiler (gleiche Abstände)
                if (!ModelPillars)
                for (float d = s0 + PillarSpacing * 0.5f; d < s1; d += PillarSpacing)
                {
                    curve.At(d, out var p, out var r, out float h, out float g);
                    if (h - g < 2f) continue;
                    data.Pillar(p, r, Mathf.Min(hw * 0.5f, 4f), 0.8f, g - 1f, h - DeckThickness, concrete);
                }
            }

            // Unter der Erde: offener Trog bis TunnelDepth, darunter Tunnel mit Portal
            if (curve.MinElevation < -0.3f)
            {
                float td = RoadBuilder.TunnelDepth;
                float wall = hw + 0.3f;
                const float tunnelClear = 5.5f;
                System.Func<float, float, float> bottom = (h, g) => h - 0.2f;
                System.Func<float, float, float> top = (h, g) => h - g > -td ? Mathf.Max(g, h + SidewalkHeight + 0.6f) : h + tunnelClear;
                data.WallWhere(curve, s0, s1, -wall, bottom, top, concrete, float.MinValue, 0.2f);
                data.WallWhere(curve, s0, s1, wall, bottom, top, concrete, float.MinValue, 0.2f);

                // Geländekante neben dem Trog – überdeckt die Kachelränder des Bodenlochs
                int groundSurface = (int)RoadSurface.Ground;
                data.StripWhere(curve, s0, s1, -wall - 8f, -wall, 0.02f, groundSurface, -td, -0.3f, onGround: true);
                data.StripWhere(curve, s0, s1, wall, wall + 8f, 0.02f, groundSurface, -td, -0.3f, onGround: true);
                // Mauerkrone
                data.StripWhere(curve, s0, s1, -wall - 0.4f, -wall, 0.04f, concrete, -td, -0.3f, onGround: true);
                data.StripWhere(curve, s0, s1, wall, wall + 0.4f, 0.04f, concrete, -td, -0.3f, onGround: true);

                // Tunnelportal: Stirnwand über der Einfahrt
                foreach (float d in curve.Crossings(-td))
                {
                    if (d < s0 || d > s1) continue;
                    curve.At(d, out _, out _, out float h, out float g);
                    data.CrossWall(curve, d, -wall - 0.4f, wall + 0.4f, h + tunnelClear, g + 0.04f, concrete);
                }
            }
        }

        // ------------------------------------------------------------ Kreuzungen

        /// <summary>
        /// Wie weit endet eine Straße vor dem Knoten (Bogenlänge)? An Kreuzungen so weit, dass der gerundete
        /// Bordstein und die Gehweg-Außenkanten der Nachbarn davor liegen; an Knicken bis zum Eckstück.
        /// Wird von Segment-, Kreuzungs- und Eckstück-Mesh gleich benutzt (<see cref="JunctionGeometry.Trim"/>).
        /// </summary>
        public static float JunctionTrim(RoadNetwork network, RoadNode node, RoadSegment segment) =>
            JunctionGeometry.Trim(network, node, segment);

        /// <summary>
        /// Braucht der Knoten eine eigene Fläche? Kreuzungen (ab 3 Straßen) und Übergänge zwischen
        /// unterschiedlichen Straßentypen (z. B. 4-spurig → 2-spurig). Knicke gleichen Typs bekommen
        /// stattdessen ein Eckstück (<see cref="NodeShape.Bend"/>, <see cref="BuildBend"/>).
        /// </summary>
        public static bool NeedsJunctionMesh(RoadNetwork network, RoadNode node) =>
            JunctionGeometry.Classify(network, node) == NodeShape.Junction;

        /// <summary>
        /// Kreuzungsfläche: Fahrbahn aus den Fahrbahnkanten aller Arme mit gerundeten Bordsteinecken
        /// (trianguliert, kein Gehweg darüber), dazu Gehweg-Ecken mit Bordstein zwischen benachbarten
        /// Stadtstraßen. Geometrie aus <see cref="JunctionGeometry.Layout"/>. Weltkoordinaten.
        /// Submesh 0 = Asphalt, 1 = Gehweg.
        /// </summary>
        public static Mesh BuildJunction(RoadNetwork network, RoadNode node, Mesh mesh = null)
        {
            var layout = JunctionGeometry.Layout(network, node);
            var data = new MeshData(2);

            // Fahrbahn: Umriss gegen den Uhrzeigersinn, Ohren-Triangulierung (auch bei nicht sternförmigen Umrissen)
            var ring = layout.Ring;
            int baseRing = data.Vertices.Count;
            for (int i = 0; i < ring.Count; i++)
            {
                data.Vertices.Add(SimConvert.ToWorld(ring[i], layout.RingHeights[i] + RoadHeight));
                data.Uvs.Add(new Vector2(ring[i].X / 4f, ring[i].Y / 4f));
            }
            var tris = Geometry2D.Triangulate(ring);
            for (int k = 0; k < tris.Count; k += 3)
            {
                // gegen den Uhrzeigersinn (Simulation) → im Uhrzeigersinn von oben (Unity) = Normale nach oben
                data.Triangles[0].Add(baseRing + tris[k]);
                data.Triangles[0].Add(baseRing + tris[k + 2]);
                data.Triangles[0].Add(baseRing + tris[k + 1]);
            }

            // Gehweg-Ecken
            foreach (var corner in layout.Corners)
            {
                if (!corner.HasSidewalk) continue;
                float ha = layout.Arms[corner.ArmA].Height, hb = layout.Arms[corner.ArmB].Height;
                var inner = corner.Curb;
                var outer = corner.Outer;
                int steps = inner.Length - 1;

                int baseIndex = data.Vertices.Count;
                for (int k = 0; k <= steps; k++)
                {
                    float h = Mathf.Lerp(ha, hb, k / (float)steps);
                    // innen (Bordstein, zum Knoten) = links, außen = rechts in Laufrichtung → Normale nach oben
                    data.Vertices.Add(SimConvert.ToWorld(inner[k], h + SidewalkHeight));
                    data.Vertices.Add(SimConvert.ToWorld(outer[k], h + SidewalkHeight));
                    data.Uvs.Add(Vector2.zero);
                    data.Uvs.Add(Vector2.right);
                }
                for (int k = 0; k < steps; k++)
                {
                    int bi = baseIndex + k * 2;
                    data.Triangles[1].Add(bi); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 1);
                    data.Triangles[1].Add(bi + 1); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 3);
                }

                // Bordstein entlang der Innenkante
                int curbBase = data.Vertices.Count;
                for (int k = 0; k <= steps; k++)
                {
                    float h = Mathf.Lerp(ha, hb, k / (float)steps);
                    data.Vertices.Add(SimConvert.ToWorld(inner[k], h + RoadHeight));
                    data.Vertices.Add(SimConvert.ToWorld(inner[k], h + SidewalkHeight));
                    data.Uvs.Add(Vector2.zero);
                    data.Uvs.Add(Vector2.up);
                }
                for (int k = 0; k < steps; k++)
                {
                    int bi = curbBase + k * 2;
                    data.Triangles[1].Add(bi); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 1);
                    data.Triangles[1].Add(bi + 1); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 3);
                    data.Triangles[1].Add(bi); data.Triangles[1].Add(bi + 1); data.Triangles[1].Add(bi + 2);
                    data.Triangles[1].Add(bi + 1); data.Triangles[1].Add(bi + 3); data.Triangles[1].Add(bi + 2);
                }
            }

            return data.ToMesh(mesh);
        }

        // ------------------------------------------------------------ Vorschau

        public struct PreviewCurve
        {
            public SimVector2 P0, Control, P2;
            public float H0, H2;
            public float Width;
            /// <summary>Optionales Höhenprofil (geländefolgende Straße).</summary>
            public float[] Profile;

            public PreviewCurve(SimVector2 p0, SimVector2 c, SimVector2 p2, float width, float h0 = 0f, float h2 = 0f, float[] profile = null)
            {
                P0 = p0; Control = c; P2 = p2; Width = width; H0 = h0; H2 = h2; Profile = profile;
            }
        }

        /// <summary>Einfache Bänder (Vorschau beim Bauen). Mehrere Kurven in einem Mesh (Kreisverkehr, Vorlagen).</summary>
        public static Mesh BuildRibbons(IReadOnlyList<PreviewCurve> curves, float height, Mesh mesh = null)
        {
            var data = new MeshData(1);
            foreach (var pc in curves)
            {
                var profile = pc.Profile;
                var curve = profile != null
                    ? new Curve(pc.P0, pc.Control, pc.P2, t => Profiles.Sample(profile, t))
                    : new Curve(pc.P0, pc.Control, pc.P2, pc.H0, pc.H2);
                data.Strip(curve, 0f, curve.Length, -pc.Width * 0.5f, pc.Width * 0.5f, height, 0);
            }
            return data.ToMesh(mesh);
        }
    }
}
