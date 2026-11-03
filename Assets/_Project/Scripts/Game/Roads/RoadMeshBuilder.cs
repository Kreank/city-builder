using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Erzeugt Platzhalter-Meshes für Straßen aus dem Spurquerschnitt des Straßentyps:
    /// Fahrbahn, Gehwege mit Bordstein, Busspuren, Gleise, Markierungen und Richtungspfeile.
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

        // ------------------------------------------------------------ Kurvenabtastung

        /// <summary>Abgetastete Kurve mit Bogenlänge – erlaubt Streifen "von Meter x bis Meter y".</summary>
        sealed class Curve
        {
            readonly List<SimVector2> points = new List<SimVector2>();
            readonly List<SimVector2> rights = new List<SimVector2>();
            readonly List<float> dist = new List<float>();
            public float Length => dist[dist.Count - 1];

            public Curve(SimVector2 p0, SimVector2 p1, SimVector2 p2)
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
                }
            }

            public void At(float d, out SimVector2 pos, out SimVector2 right)
            {
                if (d <= 0f) { pos = points[0]; right = rights[0]; return; }
                int last = dist.Count - 1;
                if (d >= dist[last]) { pos = points[last]; right = rights[last]; return; }
                int lo = 0, hi = last;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (dist[mid] <= d) lo = mid; else hi = mid;
                }
                float u = (d - dist[lo]) / Mathf.Max(1e-6f, dist[hi] - dist[lo]);
                pos = SimVector2.Lerp(points[lo], points[hi], u);
                right = SimVector2.Normalize(SimVector2.Lerp(rights[lo], rights[hi], u));
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

            /// <summary>Flacher Streifen zwischen den seitlichen Abständen left..right (positiv = rechts).</summary>
            public void Strip(Curve c, float d0, float d1, float left, float right, float height, int surface)
            {
                if (d1 - d0 < 0.01f || right - left < 0.001f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                foreach (float d in stations)
                {
                    c.At(d, out var p, out var r);
                    Vertices.Add(SimConvert.ToWorld(p + r * left, height));
                    Vertices.Add(SimConvert.ToWorld(p + r * right, height));
                    Uvs.Add(new Vector2(0f, d / 4f));
                    Uvs.Add(new Vector2(1f, d / 4f));
                }
                var tris = Triangles[surface];
                for (int i = 0; i < stations.Count - 1; i++)
                {
                    int b = baseIndex + i * 2;
                    // Im Uhrzeigersinn von oben gesehen → Normale nach oben
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            /// <summary>Senkrechte Wand (Bordstein) bei seitlichem Abstand offset, beidseitig sichtbar.</summary>
            public void Wall(Curve c, float d0, float d1, float offset, float bottom, float top, int surface)
            {
                if (d1 - d0 < 0.01f) return;
                c.Stations(d0, d1, stations);
                int baseIndex = Vertices.Count;
                foreach (float d in stations)
                {
                    c.At(d, out var p, out var r);
                    var q = p + r * offset;
                    Vertices.Add(SimConvert.ToWorld(q, bottom));
                    Vertices.Add(SimConvert.ToWorld(q, top));
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
                c.At(d + 2.5f, out var p, out var r);
                c.At(d + 4.5f, out var tip, out var r2);
                int b = Vertices.Count;
                Vertices.Add(SimConvert.ToWorld(p + r * (offset - 0.6f), MarkingHeight));
                Vertices.Add(SimConvert.ToWorld(tip + r2 * offset, MarkingHeight));
                Vertices.Add(SimConvert.ToWorld(p + r * (offset + 0.6f), MarkingHeight));
                Uvs.Add(Vector2.zero); Uvs.Add(Vector2.up); Uvs.Add(Vector2.right);
                Triangles[surface].Add(b); Triangles[surface].Add(b + 1); Triangles[surface].Add(b + 2);
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
        public static Mesh BuildSegment(RoadSegment segment, float trimStart, float trimEnd, Mesh mesh = null)
        {
            var type = segment.Type;
            var curve = new Curve(segment.Start.Position, segment.Control, segment.End.Position);
            float d0 = Mathf.Clamp(trimStart, 0f, curve.Length * 0.45f);
            float d1 = curve.Length - Mathf.Clamp(trimEnd, 0f, curve.Length * 0.45f);

            var data = new MeshData(RoadMaterials.SurfaceCount);
            var lanes = type.Lanes;
            bool highway = type.Category != RoadCategory.City;
            float dash = highway ? 6f : 3f, gap = highway ? 12f : 6f;

            // Spurflächen
            foreach (var lane in lanes)
            {
                switch (lane.Kind)
                {
                    case LaneKind.Sidewalk:
                        data.Strip(curve, d0, d1, lane.Left, lane.Right, SidewalkHeight, (int)RoadSurface.Sidewalk);
                        // Bordstein zur Fahrbahn hin
                        float curb = lane.Offset < 0f ? lane.Right : lane.Left;
                        data.Wall(curve, d0, d1, curb, RoadHeight, SidewalkHeight, (int)RoadSurface.Sidewalk);
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
                    default: // Car, Shoulder
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
                    // Fahrbahnrand bei Autobahn/Rampe (Standstreifen)
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
                else data.Strip(curve, d0, d1, x - 0.125f, x + 0.125f, MarkingHeight, marking); // Bus-/Tramspur: breite durchgezogene Linie
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

            return data.ToMesh(mesh);
        }

        /// <summary>Asphaltscheibe für Knoten/Kreuzungen.</summary>
        public static Mesh BuildDisc(float radius, float height, Mesh mesh = null, int segments = 32)
        {
            var data = new MeshData(1);
            data.Vertices.Add(new Vector3(0f, height, 0f));
            data.Uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                data.Vertices.Add(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
                data.Uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                if (i > 0) { data.Triangles[0].Add(0); data.Triangles[0].Add(i + 1); data.Triangles[0].Add(i); }
            }
            return data.ToMesh(mesh);
        }

        // ------------------------------------------------------------ Vorschau

        /// <summary>Einfache Bänder (Vorschau beim Bauen). Mehrere Kurven in einem Mesh, z. B. für den Kreisverkehr.</summary>
        public static Mesh BuildRibbons(IReadOnlyList<(SimVector2 p0, SimVector2 c, SimVector2 p2)> curves, float width, float height, Mesh mesh = null)
        {
            var data = new MeshData(1);
            foreach (var (p0, c, p2) in curves)
            {
                var curve = new Curve(p0, c, p2);
                data.Strip(curve, 0f, curve.Length, -width * 0.5f, width * 0.5f, height, 0);
            }
            return data.ToMesh(mesh);
        }
    }
}
