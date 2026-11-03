using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Zeigt das Straßennetz der Simulation an. Hört auf Events des RoadNetwork und baut
    /// betroffene Meshes gesammelt einmal pro Frame neu (ein Bauvorgang kann viele Segmente zerteilen).
    /// Die View verändert die Simulation nie selbst.
    /// </summary>
    public sealed class RoadNetworkView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [Tooltip("Basis-Material (URP Lit). Die Oberflächenfarben werden davon abgeleitet.")]
        [SerializeField] Material roadMaterial;

        readonly Dictionary<int, MeshFilter> segmentObjects = new Dictionary<int, MeshFilter>();
        readonly Dictionary<int, MeshFilter> nodeObjects = new Dictionary<int, MeshFilter>();
        readonly HashSet<int> dirtySegments = new HashSet<int>();
        readonly HashSet<int> dirtyNodes = new HashSet<int>();
        RoadNetwork network;
        RoadMaterials materials;

        void Start()
        {
            materials = new RoadMaterials(roadMaterial);
            network = runner.World.Roads;
            network.SegmentAdded += OnSegmentAdded;
            network.SegmentRemoved += OnSegmentRemoved;
            network.NodeRemoved += OnNodeRemoved;

            foreach (var s in network.Segments) OnSegmentAdded(s);
        }

        void OnDestroy()
        {
            if (network != null)
            {
                network.SegmentAdded -= OnSegmentAdded;
                network.SegmentRemoved -= OnSegmentRemoved;
                network.NodeRemoved -= OnNodeRemoved;
            }
            materials?.Destroy();
        }

        void OnSegmentAdded(RoadSegment segment)
        {
            dirtySegments.Add(segment.Id);
            dirtyNodes.Add(segment.Start.Id);
            dirtyNodes.Add(segment.End.Id);
        }

        void OnSegmentRemoved(RoadSegment segment)
        {
            if (segmentObjects.TryGetValue(segment.Id, out var mf))
            {
                DestroyMeshObject(mf);
                segmentObjects.Remove(segment.Id);
            }
            dirtyNodes.Add(segment.Start.Id);
            dirtyNodes.Add(segment.End.Id);
        }

        void OnNodeRemoved(RoadNode node)
        {
            if (!nodeObjects.TryGetValue(node.Id, out var mf)) return;
            DestroyMeshObject(mf);
            nodeObjects.Remove(node.Id);
        }

        void LateUpdate()
        {
            if (dirtyNodes.Count == 0 && dirtySegments.Count == 0) return;

            // Knoten zuerst: ändert sich der Grad eines Knotens, müssen alle anliegenden Segmente neu gekürzt werden.
            foreach (int nodeId in dirtyNodes)
            {
                var node = network.GetNode(nodeId);
                if (node == null) continue;
                RebuildNode(node);
                foreach (int segId in node.SegmentIds) dirtySegments.Add(segId);
            }
            dirtyNodes.Clear();

            foreach (int segId in dirtySegments)
            {
                var seg = network.GetSegment(segId);
                if (seg != null) RebuildSegment(seg);
            }
            dirtySegments.Clear();
        }

        /// <summary>Kreuzung = ab 3 Straßen. Dort wird die Fläche frei gehalten (keine Markierungen).</summary>
        static bool IsJunction(RoadNode node) => node.SegmentIds.Count >= 3;

        float JunctionRadius(RoadNode node)
        {
            float r = 0f;
            foreach (int id in node.SegmentIds) r = Mathf.Max(r, network.GetSegment(id).Type.HalfWidth);
            return r;
        }

        void RebuildSegment(RoadSegment seg)
        {
            float trimStart = IsJunction(seg.Start) ? JunctionRadius(seg.Start) : 0f;
            float trimEnd = IsJunction(seg.End) ? JunctionRadius(seg.End) : 0f;

            if (!segmentObjects.TryGetValue(seg.Id, out var mf))
            {
                mf = CreateMeshObject($"Road {seg.Id} ({seg.Type.DisplayName})", materials.All);
                segmentObjects[seg.Id] = mf;
            }
            mf.sharedMesh = RoadMeshBuilder.BuildSegment(seg, trimStart, trimEnd, mf.sharedMesh);
        }

        void RebuildNode(RoadNode node)
        {
            int degree = node.SegmentIds.Count;
            if (degree <= 1)
            {
                // Sackgasse/Straßenende: keine Knotenfläche
                if (nodeObjects.TryGetValue(node.Id, out var old))
                {
                    DestroyMeshObject(old);
                    nodeObjects.Remove(node.Id);
                }
                return;
            }

            // Grad 2: kleine Scheibe schließt Lücken in Knicken. Kreuzung: volle Fläche.
            float radius = 0f;
            foreach (int id in node.SegmentIds)
            {
                var t = network.GetSegment(id).Type;
                radius = Mathf.Max(radius, IsJunction(node) ? t.HalfWidth : t.CarriagewayHalfWidth);
            }

            if (!nodeObjects.TryGetValue(node.Id, out var mf))
            {
                mf = CreateMeshObject($"Node {node.Id}", new[] { materials[RoadSurface.Asphalt] });
                mf.transform.position = SimConvert.ToWorld(node.Position);
                nodeObjects[node.Id] = mf;
            }
            // Leicht unter der Fahrbahn, damit Markierungen der Segmente sichtbar bleiben.
            mf.sharedMesh = RoadMeshBuilder.BuildDisc(radius, RoadMeshBuilder.RoadHeight - 0.002f, mf.sharedMesh);
        }

        MeshFilter CreateMeshObject(string name, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mf;
        }

        static void DestroyMeshObject(MeshFilter mf)
        {
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(mf.gameObject);
        }
    }
}
