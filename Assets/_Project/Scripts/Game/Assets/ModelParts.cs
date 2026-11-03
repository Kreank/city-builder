using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBuilder.Game.Assets
{
    /// <summary>
    /// Eigene Assets: ein Blender-Modell (Prefab aus dem <see cref="OwnModelCatalog"/>) zerlegt in instanzierbare Teile
    /// (je MeshFilter und Submesh mit Matrix relativ zur Wurzel) – ohne GameObjects und ohne lesbare Meshes, damit viele
    /// Kopien per <see cref="PartsRenderer"/> gezeichnet werden können. Benannte bewegliche Kindobjekte (z. B. „Sails“)
    /// lassen sich ausschließen bzw. getrennt als <see cref="Moving"/> holen.
    /// </summary>
    public sealed class ModelParts
    {
        public struct Part
        {
            public Mesh Mesh;
            public int Submesh;
            public Matrix4x4 Local;
        }

        public readonly List<Part> Parts = new List<Part>();
        /// <summary>Begrenzung im Modellraum (Ursprung = Mitte der Grundfläche).</summary>
        public Bounds Bounds;
        public Vector3 Size => Bounds.size;
        /// <summary>Drehpunkt des beweglichen Teils (nur bei <see cref="Moving"/>-Teilen gesetzt).</summary>
        public Vector3 Pivot;
        /// <summary>Drehung des beweglichen Teils gegenüber der Wurzel.</summary>
        public Quaternion PivotRotation = Quaternion.identity;

        /// <summary>Matrix des beweglichen Teils: Wurzel-Matrix, Drehung <paramref name="spin"/> (im Wurzelraum) um den Drehpunkt.</summary>
        public Matrix4x4 PoseAt(Matrix4x4 root, Quaternion spin) =>
            root * Matrix4x4.TRS(Pivot, spin * PivotRotation, Vector3.one);

        static readonly Dictionary<(GameObject, string, bool), ModelParts> cache = new Dictionary<(GameObject, string, bool), ModelParts>();

        /// <summary>Teile ohne die Kindobjekte namens <paramref name="exclude"/> (null = alles).</summary>
        public static ModelParts Of(GameObject prefab, string exclude = null) => Get(prefab, exclude, false);

        /// <summary>Nur das Kindobjekt <paramref name="child"/> (Matrizen relativ zu dessen Drehpunkt) oder null, wenn es fehlt.</summary>
        public static ModelParts Moving(GameObject prefab, string child)
        {
            var p = Get(prefab, child, true);
            return p != null && p.Parts.Count > 0 ? p : null;
        }

        static ModelParts Get(GameObject prefab, string name, bool only)
        {
            if (prefab == null) return null;
            var key = (prefab, name, only);
            if (cache.TryGetValue(key, out var parts)) return parts;
            parts = new ModelParts();
            var root = prefab.transform;
            var toRoot = root.worldToLocalMatrix;
            Transform pivot = only && name != null ? FindChild(root, name) : null;
            if (only && pivot == null) { cache[key] = parts; return parts; }
            if (pivot != null)
            {
                parts.Pivot = toRoot.MultiplyPoint3x4(pivot.position);
                parts.PivotRotation = Quaternion.Inverse(root.rotation) * pivot.rotation;
            }
            bool any = false;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                bool inChild = name != null && IsUnder(mf.transform, name, root);
                if (only != inChild) continue;
                // Bewegliche Teile ohne Skalierung des Drehpunkts (TRS in PoseAt), daher relativ zu dessen Lage/Drehung
                var local = (only ? Matrix4x4.TRS(pivot.position, pivot.rotation, Vector3.one).inverse : toRoot) * mf.transform.localToWorldMatrix;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    parts.Parts.Add(new Part { Mesh = mf.sharedMesh, Submesh = sub, Local = local });
                // Begrenzung immer im Modellraum (auch für bewegliche Teile)
                var m = toRoot * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var pt = m.MultiplyPoint3x4(c);
                    if (!any) { parts.Bounds = new Bounds(pt, Vector3.zero); any = true; } else parts.Bounds.Encapsulate(pt);
                }
            }
            cache[key] = parts;
            return parts;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        static bool IsUnder(Transform t, string name, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
                if (string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Nach einem Neubau des Katalogs (Editor) bzw. Weltwechsel: Zerlegungen verwerfen.</summary>
        public static void ClearCache() => cache.Clear();
    }

    /// <summary>
    /// Zeichnet viele Kopien von <see cref="ModelParts"/> per GPU-Instancing mit einem Material (Palette). Mit
    /// <see cref="ChunkSize"/> &gt; 0 werden die Kopien in Kacheln einsortiert und nur bis <see cref="MaxDistance"/>
    /// von der Kamera gezeichnet (Entfernungs-LOD).
    /// </summary>
    public sealed class PartsRenderer
    {
        const int Batch = 1023;
        readonly Dictionary<long, Dictionary<(Mesh, int), List<Matrix4x4>>> chunks = new Dictionary<long, Dictionary<(Mesh, int), List<Matrix4x4>>>();
        readonly Matrix4x4[] buffer = new Matrix4x4[Batch];
        Material material;

        public ShadowCastingMode Shadows = ShadowCastingMode.On;
        /// <summary>Kachelgröße in m für die Entfernungs-LOD (0 = eine Kachel, immer zeichnen).</summary>
        public float ChunkSize;
        /// <summary>Nur Kacheln bis zu dieser Entfernung von der Kamera zeichnen (∞ = alle).</summary>
        public float MaxDistance = float.PositiveInfinity;
        public int Count { get; private set; }

        public PartsRenderer(Material palette, float chunkSize = 0f, float maxDistance = float.PositiveInfinity)
        {
            SetMaterial(palette);
            ChunkSize = chunkSize;
            MaxDistance = maxDistance;
        }

        public void SetMaterial(Material palette)
        {
            if (palette != null && !palette.enableInstancing)
                palette = new Material(palette) { enableInstancing = true, name = palette.name + " (Instanced)" };
            material = palette;
        }

        public void Clear()
        {
            foreach (var c in chunks.Values) foreach (var l in c.Values) l.Clear();
            Count = 0;
        }

        long ChunkKey(Vector3 p)
        {
            if (ChunkSize <= 0f) return 0;
            int x = Mathf.FloorToInt(p.x / ChunkSize), z = Mathf.FloorToInt(p.z / ChunkSize);
            return ((long)x << 32) ^ (uint)z;
        }

        public void Add(ModelParts model, Matrix4x4 matrix)
        {
            if (model == null || model.Parts.Count == 0) return;
            long key = ChunkKey(matrix.GetColumn(3));
            if (!chunks.TryGetValue(key, out var chunk)) chunks[key] = chunk = new Dictionary<(Mesh, int), List<Matrix4x4>>();
            foreach (var part in model.Parts)
            {
                var k = (part.Mesh, part.Submesh);
                if (!chunk.TryGetValue(k, out var list)) chunk[k] = list = new List<Matrix4x4>();
                list.Add(matrix * part.Local);
            }
            Count++;
        }

        public void Render(Camera camera = null)
        {
            if (material == null || Count == 0) return;
            Vector3 cam = camera != null ? camera.transform.position : Vector3.zero;
            bool cull = camera != null && ChunkSize > 0f && !float.IsInfinity(MaxDistance);
            float reach = MaxDistance + ChunkSize * 0.75f;
            var rp = new RenderParams(material) { shadowCastingMode = Shadows, receiveShadows = true };
            foreach (var kv in chunks)
            {
                if (cull)
                {
                    float cx = ((int)(kv.Key >> 32) + 0.5f) * ChunkSize, cz = ((int)(uint)kv.Key + 0.5f) * ChunkSize;
                    float dx = cx - cam.x, dz = cz - cam.z;
                    if (dx * dx + dz * dz > reach * reach) continue;
                }
                foreach (var part in kv.Value)
                {
                    var list = part.Value;
                    if (list.Count == 0) continue;
                    for (int start = 0; start < list.Count; start += Batch)
                    {
                        int n = Mathf.Min(Batch, list.Count - start);
                        list.CopyTo(start, buffer, 0, n);
                        Graphics.RenderMeshInstanced(rp, part.Key.Item1, part.Key.Item2, buffer, n);
                    }
                }
            }
        }
    }
}
