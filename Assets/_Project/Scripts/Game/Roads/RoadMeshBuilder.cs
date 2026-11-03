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
        const float DeckThickness = 0.8f;
        const float PillarSpacing = 36f;

        // ------------------------------------------------------------ Kurvenabtastung

        /// <summary>Abgetastete Kurve mit Bogenlänge und Höhe – erlaubt Streifen "von Meter x bis Meter y".</summary>
        sealed class Curve
        {
            readonly List<SimVector2> points = new List<SimVector2>();
            readonly List<SimVector2> rights = new List<SimVector2>();
            readonly List<float> dist = new List<float>();
            readonly List<float> heights = new List<float>();
            readonly List<float> grounds = new List<float>();
            public float Length => dist[dist.Count - 1];

            public Curve(SimVector2 p0, SimVector2 p1, SimVector2 p2, float h0 = 0f, float h1 = 0f, IGround ground = null)
                : this(p0, p1, p2, t => Mathf.Lerp(h0, h1, t), ground) { }

            /// <summary>Kurve mit beliebigem Höhenverlauf (z. B. geländefolgendes Profil).</summary>
            public Curve(SimVector2 p0, SimVector2 p1, SimVector2 p2, System.Func<float, float> heightAt, IGround ground = null)
            {
                float approx = Bezier.ApproximateLength(p0, p1, p2);
                int n = Mathf.Clamp(Mathf.CeilToInt(approx / 2f), 2, 512);
                float d = 0f;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    var p = Bezier.Evaluate(p0, p1, p2, t);
                    var tan = Bezier.Derivative(p0, p1, p2, t);
                    if (tan.LengthSquared() < 1e-6f) tan = p2 - p0;
                    tan = SimVector2.Normalize(tan);
                    if (i > 0) d += SimVector2.Distance(points[i - 1], p);
                    points.Add(p);
                    rights.Add(new SimVector2(tan.Y, -tan.X));
                    dist.Add(d);
                    heights.Add(heightAt(t));
                    grounds.Add(ground != null ? ground.HeightAt(p) : 0f);
                }
            }

            /// <summary>Wie <see cref="At(float, out SimVector2, out SimVector2, out float)"/>, zusätzlich Geländehöhe.</summary>
            public void At(float d, out SimVector2 pos, out SimVector2 right, out float baseHeight, out float groundHeight)
            {
                At(d, out pos, out right, out baseHeight);
                int last = dist.Count - 1;
                if (d <= 0f) { groundHeight = grounds[0]; return; }
                if (d >= dist[last]) { groundHeight = grounds[last]; return; }
                int lo = 0, hi = last;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (dist[mid] <= d) lo = mid; else hi = mid;
                }
                float u = (d - dist[lo]) / Mathf.Max(1e-6f, dist[hi] - dist[lo]);
                groundHeight = Mathf.Lerp(grounds[lo], grounds[hi], u);
            }

            public void At(float d, out SimVector2 pos, out SimVector2 right, out float baseHeight)
            {
                int last = dist.Count - 1;
                if (d <= 0f) { pos = points[0]; right = rights[0]; baseHeight = heights[0]; return; }
                if (d >= dist[last]) { pos = points[last]; right = rights[last]; baseHeight = heights[last]; return; }
                int lo = 0, hi = last;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (dist[mid] <= d) lo = mid; else hi = mid;
                }
                float u = (d - dist[lo]) / Mathf.Max(1e-6f, dist[hi] - dist[lo]);
                pos = SimVector2.Lerp(points[lo], points[hi], u);
                right = SimVector2.Normalize(SimVector2.Lerp(rights[lo], rights[hi], u));
                baseHeight = Mathf.Lerp(heights[lo], heights[hi], u);
            }

            /// <summary>Größte Höhe über Gelände.</summary>
            public float MaxElevation
            {
                get { float m = float.MinValue; for (int i = 0; i < heights.Count; i++) m = Mathf.Max(m, heights[i] - grounds[i]); return m; }
            }

            /// <summary>Kleinste Höhe über Gelände (negativ = unter der Erde).</summary>
            public float MinElevation
            {
                get { float m = float.MaxValue; for (int i = 0; i < heights.Count; i++) m = Mathf.Min(m, heights[i] - grounds[i]); return m; }
            }

            /// <summary>Stellen (Bogenlänge), an denen die Höhe über Gelände den Wert <paramref name="level"/> durchläuft.</summary>
            public List<float> Crossings(float level)
            {
                var result = new List<float>();
                for (int i = 0; i < heights.Count - 1; i++)
                {
                    float a = heights[i] - grounds[i] - level, b = heights[i + 1] - grounds[i + 1] - level;
                    if ((a > 0f) == (b > 0f) || Mathf.Approximately(a, b)) continue;
                    float u = a / (a - b);
                    result.Add(Mathf.Lerp(dist[i], dist[i + 1], u));
                }
                return result;
            }

            /// <summary>Stützstellen zwischen d0 und d1 (inkl. Ränder).</summary>
            public void Stations(float d0, float d1, List<float> result)
            {
                result.Clear();
                result.Add(d0);
                for (int i = 0; i < dist.Count; i++)
                    if (dist[i] > d0 + 0.01f && dist[i] < d1 - 0.01f) result.Add(dist[i]);
                result.Add(d1);
            }
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
                    c.At(d, out var p, out var r, out float h);
                    Vertices.Add(SimConvert.ToWorld(p + r * left, h + height));
                    Vertices.Add(SimConvert.ToWorld(p + r * right, h + height));
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
                    c.At(d, out var p, out var r, out float h);
                    var q = p + r * offset;
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
                    c.At(stations[i], out var p, out var r, out float h, out float g);
                    ok[i] = h - g >= minElev && h - g <= maxElev;
                    float y = onGround ? g + height : h + height;
                    Vertices.Add(SimConvert.ToWorld(p + r * left, y));
                    Vertices.Add(SimConvert.ToWorld(p + r * right, y));
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
                    c.At(stations[i], out var p, out var r, out float h, out float g);
                    ok[i] = h - g >= minElev && h - g <= maxElev;
                    var q = p + r * offset;
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
                c.At(d, out var p, out var r, out _);
                int b = Vertices.Count;
                Vertices.Add(SimConvert.ToWorld(p + r * left, y0));
                Vertices.Add(SimConvert.ToWorld(p + r * right, y0));
                Vertices.Add(SimConvert.ToWorld(p + r * left, y1));
                Vertices.Add(SimConvert.ToWorld(p + r * right, y1));
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
        /// Baut das Mesh eines Segments. trimStart/trimEnd kürzen das Segment an Kreuzungen,
        /// damit die Kreuzungsfläche frei von Markierungen bleibt.
        /// </summary>
        public static Mesh BuildSegment(RoadSegment segment, float trimStart, float trimEnd, Mesh mesh = null, IGround ground = null)
        {
            var type = segment.Type;
            var curve = new Curve(segment.Start.Position, segment.Control, segment.End.Position, segment.HeightAt, ground);
            float d0 = Mathf.Clamp(trimStart, 0f, curve.Length * 0.45f);
            float d1 = curve.Length - Mathf.Clamp(trimEnd, 0f, curve.Length * 0.45f);

            var data = new MeshData(RoadMaterials.SurfaceCount);
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
                data.StripWhere(curve, 0f, curve.Length, -hw, hw, -DeckThickness, concrete, 0.6f, float.MaxValue, facingDown: true);
                System.Func<float, float, float> deckBottom = (h, g) => h - DeckThickness;
                System.Func<float, float, float> parapet = (h, g) => h + SidewalkHeight + 0.6f;
                data.WallWhere(curve, 0f, curve.Length, -hw, deckBottom, parapet, concrete, 0.6f, float.MaxValue);
                data.WallWhere(curve, 0f, curve.Length, hw, deckBottom, parapet, concrete, 0.6f, float.MaxValue);

                for (float d = PillarSpacing * 0.5f; d < curve.Length; d += PillarSpacing)
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
                data.WallWhere(curve, 0f, curve.Length, -wall, bottom, top, concrete, float.MinValue, 0.2f);
                data.WallWhere(curve, 0f, curve.Length, wall, bottom, top, concrete, float.MinValue, 0.2f);

                // Geländekante neben dem Trog – überdeckt die Kachelränder des Bodenlochs
                int groundSurface = (int)RoadSurface.Ground;
                data.StripWhere(curve, 0f, curve.Length, -wall - 8f, -wall, 0.02f, groundSurface, -td, -0.3f, onGround: true);
                data.StripWhere(curve, 0f, curve.Length, wall, wall + 8f, 0.02f, groundSurface, -td, -0.3f, onGround: true);
                // Mauerkrone
                data.StripWhere(curve, 0f, curve.Length, -wall - 0.4f, -wall, 0.04f, concrete, -td, -0.3f, onGround: true);
                data.StripWhere(curve, 0f, curve.Length, wall, wall + 0.4f, 0.04f, concrete, -td, -0.3f, onGround: true);

                // Tunnelportal: Stirnwand über der Einfahrt
                foreach (float d in curve.Crossings(-td))
                {
                    curve.At(d, out _, out _, out float h, out float g);
                    data.CrossWall(curve, d, -wall - 0.4f, wall + 0.4f, h + tunnelClear, g + 0.04f, concrete);
                }
            }

            return data.ToMesh(mesh);
        }

        /// <summary>Asphaltscheibe (Weltkoordinaten) – schließt Lücken an Knicken zwischen zwei Straßen.</summary>
        public static Mesh BuildDisc(SimVector2 center, float radius, float height, Mesh mesh = null, int segments = 32)
        {
            var data = new MeshData(1);
            data.Vertices.Add(SimConvert.ToWorld(center, height));
            data.Uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                data.Vertices.Add(SimConvert.ToWorld(center + new SimVector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, height));
                data.Uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                if (i > 0) { data.Triangles[0].Add(0); data.Triangles[0].Add(i + 1); data.Triangles[0].Add(i); }
            }
            return data.ToMesh(mesh);
        }

        // ------------------------------------------------------------ Kreuzungen

        /// <summary>Eine an der Kreuzung angeschlossene Straße, gesehen vom Knoten weg.</summary>
        struct Arm
        {
            public RoadSegment Segment;
            public SimVector2 Dir;      // vom Knoten weg
            public float Angle;         // Richtungswinkel für die Sortierung
            public float Trim;          // so weit endet die Straße vor dem Knoten
            public SimVector2 Point;    // Mittellinie am Straßenende
            public SimVector2 Right;    // rechts, vom Knoten weg gesehen
            public float Height;
            public float Carriageway;   // halbe Fahrbahnbreite
            public float Full;          // halbe Gesamtbreite
            public bool HasSidewalk;
        }

        static SimVector2 DirectionAway(RoadSegment s, RoadNode node, float t)
        {
            var tan = s.Tangent(t);
            return node == s.Start ? tan : -tan;
        }

        static List<Arm> SortedArms(RoadNetwork network, RoadNode node)
        {
            var arms = new List<Arm>();
            foreach (int id in node.SegmentIds)
            {
                var s = network.GetSegment(id);
                var dir = DirectionAway(s, node, node == s.Start ? 0f : 1f);
                arms.Add(new Arm { Segment = s, Dir = dir, Angle = Mathf.Atan2(dir.Y, dir.X) });
            }
            arms.Sort((a, b) => a.Angle.CompareTo(b.Angle)); // gegen den Uhrzeigersinn
            return arms;
        }

        /// <summary>
        /// Wie weit endet eine Straße vor dem Kreuzungsknoten? So weit, dass sie sich mit den
        /// Nachbarstraßen (nach Winkel) nicht mehr überlappt. Wird von Segment- und Kreuzungsmesh gleich benutzt.
        /// </summary>
        public static float JunctionTrim(RoadNetwork network, RoadNode node, RoadSegment segment)
        {
            if (!NeedsJunctionMesh(network, node)) return 0f;
            var arms = SortedArms(network, node);
            int i = arms.FindIndex(a => a.Segment == segment);
            if (i < 0) return 0f;
            return Trim(arms, i);
        }

        /// <summary>
        /// Braucht der Knoten eine eigene Fläche? Kreuzungen (ab 3 Straßen) und Übergänge zwischen
        /// unterschiedlichen Straßentypen (z. B. 4-spurig → 2-spurig).
        /// </summary>
        public static bool NeedsJunctionMesh(RoadNetwork network, RoadNode node)
        {
            int n = node.SegmentIds.Count;
            if (n >= 3) return true;
            if (n != 2) return false;
            return network.GetSegment(node.SegmentIds[0]).Type != network.GetSegment(node.SegmentIds[1]).Type;
        }

        static float Trim(List<Arm> arms, int i)
        {
            var me = arms[i];
            if (arms.Count == 2)
            {
                // Übergang zweier Straßentypen: Länge nach Breitenunterschied (ca. 1:4 Verjüngung)
                var other = arms[1 - i];
                float dw = Mathf.Abs(me.Segment.Type.HalfWidth - other.Segment.Type.HalfWidth);
                return Mathf.Min(Mathf.Max(6f, dw * 4f + 4f), me.Segment.Length * 0.45f, 60f);
            }
            float needed = 0f;
            foreach (int k in new[] { (i + arms.Count - 1) % arms.Count, (i + 1) % arms.Count })
            {
                if (k == i) continue;
                var other = arms[k];
                float angle = Mathf.Max(20f, RoadGeometry.Angle(me.Dir, other.Dir));
                float hw = Mathf.Max(me.Segment.Type.HalfWidth, other.Segment.Type.HalfWidth);
                needed = Mathf.Max(needed, hw / Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad));
            }
            needed = Mathf.Max(needed, me.Segment.Type.HalfWidth * 0.5f) + 1f;
            return Mathf.Min(needed, me.Segment.Length * 0.45f, 60f);
        }

        /// <summary>
        /// Kreuzungsfläche: Fahrbahn aus den Fahrbahnkanten aller Arme mit gerundeten Ecken,
        /// dazu Gehweg-Ecken mit Bordstein zwischen benachbarten Stadtstraßen. Weltkoordinaten.
        /// Submesh 0 = Asphalt, 1 = Gehweg.
        /// </summary>
        public static Mesh BuildJunction(RoadNetwork network, RoadNode node, Mesh mesh = null)
        {
            var arms = SortedArms(network, node);
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                var s = a.Segment;
                a.Trim = Trim(arms, i);
                float t = Mathf.Clamp01(a.Trim / Mathf.Max(0.01f, s.Length));
                if (node == s.End) t = 1f - t;
                a.Point = s.Evaluate(t);
                a.Dir = DirectionAway(s, node, t);
                a.Right = new SimVector2(a.Dir.Y, -a.Dir.X);
                a.Height = s.HeightAt(t);
                a.Carriageway = s.Type.CarriagewayHalfWidth;
                a.Full = s.Type.HalfWidth;
                a.HasSidewalk = s.Type.Lanes.Count > 0 && s.Type.Lanes[0].Kind == LaneKind.Sidewalk;
                arms[i] = a;
            }

            var data = new MeshData(2);

            // Fahrbahn: Fächer um den Knoten, Punkte gegen den Uhrzeigersinn
            var ring = new List<(SimVector2 p, float h)>();
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                var b = arms[(i + 1) % arms.Count];
                ring.Add((a.Point + a.Right * a.Carriageway, a.Height));
                // Ecke von der linken Fahrbahnkante von a zur rechten von b (enthält beide Endpunkte)
                var corner = CornerPath(node.Position, a, b, a.Carriageway, b.Carriageway, arms.Count == 2);
                for (int k = 0; k < corner.Length - 1; k++)
                    ring.Add((corner[k], Mathf.Lerp(a.Height, b.Height, k / (float)(corner.Length - 1))));
            }
            int center = data.Vertices.Count;
            data.Vertices.Add(SimConvert.ToWorld(node.Position, node.Height + RoadHeight));
            data.Uvs.Add(new Vector2(0.5f, 0.5f));
            foreach (var (p, h) in ring)
            {
                data.Vertices.Add(SimConvert.ToWorld(p, h + RoadHeight));
                data.Uvs.Add(new Vector2(p.X / 4f, p.Y / 4f));
            }
            for (int k = 0; k < ring.Count; k++)
            {
                int v0 = center + 1 + k;
                int v1 = center + 1 + (k + 1) % ring.Count;
                data.Triangles[0].Add(center); data.Triangles[0].Add(v1); data.Triangles[0].Add(v0);
            }

            // Gehweg-Ecken
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                var b = arms[(i + 1) % arms.Count];
                if (!a.HasSidewalk || !b.HasSidewalk) continue;

                var inner = CornerPath(node.Position, a, b, a.Carriageway, b.Carriageway, arms.Count == 2);
                var outer = CornerPath(node.Position, a, b, a.Full, b.Full, arms.Count == 2);
                int cornerSteps = inner.Length - 1;

                int baseIndex = data.Vertices.Count;
                for (int k = 0; k <= cornerSteps; k++)
                {
                    float u = k / (float)cornerSteps;
                    float h = Mathf.Lerp(a.Height, b.Height, u);
                    var pi = inner[k];
                    var po = outer[k];
                    // innen (zum Knoten) = links, außen = rechts in Laufrichtung → Normale nach oben
                    data.Vertices.Add(SimConvert.ToWorld(pi, h + SidewalkHeight));
                    data.Vertices.Add(SimConvert.ToWorld(po, h + SidewalkHeight));
                    data.Uvs.Add(Vector2.zero);
                    data.Uvs.Add(Vector2.right);
                }
                for (int k = 0; k < cornerSteps; k++)
                {
                    int bi = baseIndex + k * 2;
                    data.Triangles[1].Add(bi); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 1);
                    data.Triangles[1].Add(bi + 1); data.Triangles[1].Add(bi + 2); data.Triangles[1].Add(bi + 3);
                }

                // Bordstein entlang der Innenkante
                int curbBase = data.Vertices.Count;
                for (int k = 0; k <= cornerSteps; k++)
                {
                    float u = k / (float)cornerSteps;
                    float h = Mathf.Lerp(a.Height, b.Height, u);
                    var pi = inner[k];
                    data.Vertices.Add(SimConvert.ToWorld(pi, h + RoadHeight));
                    data.Vertices.Add(SimConvert.ToWorld(pi, h + SidewalkHeight));
                    data.Uvs.Add(Vector2.zero);
                    data.Uvs.Add(Vector2.up);
                }
                for (int k = 0; k < cornerSteps; k++)
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

        const int CornerSteps = 8;

        /// <summary>
        /// Verlauf einer Kreuzungsecke von der linken Kante von Arm a (Breite wa) zur rechten Kante von Arm b (wb),
        /// gegen den Uhrzeigersinn, immer CornerSteps + 1 Punkte (innen und außen passen so zusammen).
        ///   Spitze Ecke (&lt; 180°): Bogen, Kontrollpunkt = Schnitt der Kanten.
        ///   Gerade Durchfahrt (≈ 180°): gerade Linie.
        ///   Überstumpf (&gt; 180°): Bogen hinter dem Knoten herum, sonst würde sich die Fläche überschlagen.
        /// </summary>
        static SimVector2[] CornerPath(SimVector2 node, Arm a, Arm b, float wa, float wb, bool transition = false)
        {
            var p0 = a.Point - a.Right * wa;
            var p2 = b.Point + b.Right * wb;
            var pts = new SimVector2[CornerSteps + 1];

            float angA = Mathf.Atan2(a.Dir.Y, a.Dir.X) * Mathf.Rad2Deg;
            float angB = Mathf.Atan2(b.Dir.Y, b.Dir.X) * Mathf.Rad2Deg;
            float span = Mathf.Repeat(angB - angA, 360f);
            if (span < 1e-3f) span = 360f; // nur bei einem Arm denkbar

            if (span < 175f)
            {
                var hit = RoadBuilder.RayIntersection(p0, -a.Dir, p2, -b.Dir);
                var mid = (p0 + p2) * 0.5f;
                var c = hit.HasValue && SimVector2.Distance(hit.Value, mid) < SimVector2.Distance(p0, p2) * 1.5f + 1f ? hit.Value : mid;
                for (int k = 0; k <= CornerSteps; k++) pts[k] = Bezier.Evaluate(p0, c, p2, k / (float)CornerSteps);
                return pts;
            }

            // Übergang zweier Straßen: stumpfe Seite gerade verbinden (sonst Delle hinter dem Knoten)
            if (span <= 185f || transition)
            {
                for (int k = 0; k <= CornerSteps; k++) pts[k] = SimVector2.Lerp(p0, p2, k / (float)CornerSteps);
                return pts;
            }

            // Überstumpf: p0 → Bogen um den Knoten (von links von a bis rechts von b) → p2
            float from = (angA + 90f) * Mathf.Deg2Rad;
            float sweep = (span - 180f) * Mathf.Deg2Rad;
            float radius = Mathf.Max(wa, wb);
            pts[0] = p0;
            pts[CornerSteps] = p2;
            for (int k = 1; k < CornerSteps; k++)
            {
                float u = (k - 1) / (float)(CornerSteps - 2);
                float ang = from + sweep * u;
                pts[k] = node + new SimVector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            }
            return pts;
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
