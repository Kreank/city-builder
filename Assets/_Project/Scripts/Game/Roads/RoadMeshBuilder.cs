using System.Collections.Generic;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Game.Roads
{
    /// <summary>Erzeugt Platzhalter-Meshes für Straßen: ein flaches Band entlang der Bézierkurve.</summary>
    public static class RoadMeshBuilder
    {
        const float MetersPerSample = 4f;

        public static Mesh BuildSegment(SimVector2 start, SimVector2 control, SimVector2 end, float width, float height, Mesh mesh = null)
        {
            float length = Bezier.ApproximateLength(start, control, end);
            int samples = Mathf.Clamp(Mathf.CeilToInt(length / MetersPerSample), 1, 256) + 1;

            var vertices = new List<Vector3>(samples * 2);
            var uvs = new List<Vector2>(samples * 2);
            var triangles = new List<int>((samples - 1) * 6);
            float half = width * 0.5f;
            float travelled = 0f;
            SimVector2 prev = start;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                SimVector2 p = Bezier.Evaluate(start, control, end, t);
                SimVector2 d = Bezier.Derivative(start, control, end, t);
                if (d.LengthSquared() < 1e-6f) d = end - start;
                d = SimVector2.Normalize(d);
                var right = new SimVector2(d.Y, -d.X);

                travelled += SimVector2.Distance(prev, p);
                prev = p;

                vertices.Add(SimConvert.ToWorld(p - right * half, height));
                vertices.Add(SimConvert.ToWorld(p + right * half, height));
                uvs.Add(new Vector2(0f, travelled / width));
                uvs.Add(new Vector2(1f, travelled / width));

                if (i > 0)
                {
                    int b = (i - 1) * 2;
                    // Im Uhrzeigersinn von oben gesehen → Normale zeigt nach oben.
                    triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                    triangles.Add(b + 1); triangles.Add(b + 2); triangles.Add(b + 3);
                }
            }

            return Finish(mesh, vertices, uvs, triangles);
        }

        /// <summary>Runde Scheibe für Knoten/Kreuzungen, damit Straßenenden sauber ineinander übergehen.</summary>
        public static Mesh BuildDisc(float radius, float height, int segments = 24)
        {
            var vertices = new List<Vector3> { new Vector3(0f, height, 0f) };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                if (i > 0) { triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); }
            }
            return Finish(null, vertices, uvs, triangles);
        }

        static Mesh Finish(Mesh mesh, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            if (mesh == null) mesh = new Mesh();
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
