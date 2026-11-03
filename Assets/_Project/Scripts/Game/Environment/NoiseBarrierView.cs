using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Environment;
using UnityEngine;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Eigene Assets: zeichnet Lärmschutzwände (<see cref="NoiseBarrierSystem"/>) als Kette aus Kacheln des Modells
    /// <c>Models/Infrastructure/NoiseBarrier.fbx</c> (16 m lang, kachelbar entlang +Z, 4 m hoch) – jede Wand in gleich lange
    /// Stücke geteilt und auf ihre Länge gezogen, Höhe aus dem Gelände. Ohne Modell grüne Platzhalter-Quader. Liest nur.
    /// </summary>
    public sealed class NoiseBarrierView : MonoBehaviour
    {
        const float TileLength = 16f;

        SimulationRunner runner;
        Material baseMaterial, placeholder;
        Mesh cube;
        PartsRenderer tiles;
        readonly List<Matrix4x4> boxes = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int builtVersion = -1, terrainVersion = -1;
        Camera cam;

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
            r.WorldReplaced += () => builtVersion = -1; // Speichern & Laden
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            if (baseMaterial != null)
            {
                placeholder = new Material(baseMaterial) { name = "NoiseBarrierPlaceholder", enableInstancing = true };
                var c = new Color(0.25f, 0.42f, 0.3f);
                if (placeholder.HasProperty("_BaseColor")) placeholder.SetColor("_BaseColor", c); else placeholder.color = c;
            }
        }

        void OnDestroy()
        {
            if (placeholder != null) Destroy(placeholder);
        }

        /// <summary>Kachel-Matrizen einer Wand (auch für die Vorschau im Werkzeug).</summary>
        public static void TileMatrices(SimulationRunner runner, System.Numerics.Vector2 a, System.Numerics.Vector2 b, float modelLength, List<Matrix4x4> into)
        {
            float len = System.Numerics.Vector2.Distance(a, b);
            if (len < 0.1f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / TileLength));
            float piece = len / n;
            var dir = new Vector3(b.X - a.X, 0f, b.Y - a.Y).normalized;
            var rot = Quaternion.LookRotation(dir);
            var terrain = runner.World.Terrain;
            for (int i = 0; i < n; i++)
            {
                var p = System.Numerics.Vector2.Lerp(a, b, (i + 0.5f) / n);
                float h = Mathf.Max(terrain.HeightAt(p), terrain.WaterLevel) - 0.1f;
                into.Add(Matrix4x4.TRS(SimConvert.ToWorld(p, h), rot, new Vector3(1f, 1f, piece / Mathf.Max(0.5f, modelLength))));
            }
        }

        void LateUpdate()
        {
            if (runner == null || runner.World?.NoiseBarriers == null) return;
            if (cam == null) cam = Camera.main;
            var nb = runner.World.NoiseBarriers;
            var catalog = OwnModelCatalog.Instance;
            var entry = catalog != null && catalog.Palette != null ? catalog.Find(OwnModelRoles.Infrastructure, "noise_barrier") : null;
            if (entry != null && tiles == null) tiles = new PartsRenderer(catalog.Palette, 256f, 1800f);
            if (nb.Version != builtVersion || runner.World.Terrain.Version != terrainVersion)
            {
                builtVersion = nb.Version;
                terrainVersion = runner.World.Terrain.Version;
                tiles?.Clear();
                boxes.Clear();
                var parts = entry != null ? ModelParts.Of(entry.Model) : null;
                var list = new List<Matrix4x4>();
                foreach (var w in nb.Barriers)
                {
                    list.Clear();
                    if (parts != null)
                    {
                        TileMatrices(runner, w.A, w.B, parts.Size.z, list);
                        foreach (var m in list) tiles.Add(parts, m);
                    }
                    else
                    {
                        TileMatrices(runner, w.A, w.B, 1f, list);
                        foreach (var m in list)
                            boxes.Add(m * Matrix4x4.TRS(new Vector3(0f, NoiseBarrierSystem.WallHeight * 0.5f, 0f), Quaternion.identity,
                                new Vector3(NoiseBarrierSystem.Thickness, NoiseBarrierSystem.WallHeight, 1f)));
                    }
                }
            }
            tiles?.Render(cam);
            if (boxes.Count > 0 && placeholder != null && cube != null)
            {
                var rp = new RenderParams(placeholder) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
                for (int start = 0; start < boxes.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, boxes.Count - start);
                    boxes.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
                }
            }
        }
    }
}
