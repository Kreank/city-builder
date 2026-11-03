using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Zeigt das Straßennetz der Simulation an. Hört auf Events des RoadNetwork –
    /// die View verändert die Simulation nie selbst.
    /// </summary>
    public sealed class RoadNetworkView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] Material roadMaterial;
        [SerializeField] float roadHeight = 0.05f;

        readonly Dictionary<int, GameObject> segmentObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> nodeObjects = new Dictionary<int, GameObject>();
        RoadNetwork network;

        void Start()
        {
            network = runner.World.Roads;
            network.SegmentAdded += OnSegmentAdded;
            network.SegmentRemoved += OnSegmentRemoved;
            network.NodeRemoved += OnNodeRemoved;

            foreach (var s in network.Segments) OnSegmentAdded(s);
        }

        void OnDestroy()
        {
            if (network == null) return;
            network.SegmentAdded -= OnSegmentAdded;
            network.SegmentRemoved -= OnSegmentRemoved;
            network.NodeRemoved -= OnNodeRemoved;
        }

        void OnSegmentAdded(RoadSegment segment)
        {
            var go = CreateMeshObject($"Road {segment.Id} ({segment.Type.DisplayName})",
                RoadMeshBuilder.BuildSegment(segment.Start.Position, segment.Control, segment.End.Position, segment.Type.Width, roadHeight));
            segmentObjects[segment.Id] = go;

            RefreshNode(segment.Start);
            RefreshNode(segment.End);
        }

        void OnSegmentRemoved(RoadSegment segment)
        {
            if (segmentObjects.TryGetValue(segment.Id, out var go))
            {
                DestroyMeshObject(go);
                segmentObjects.Remove(segment.Id);
            }
            RefreshNode(segment.Start);
            RefreshNode(segment.End);
        }

        void OnNodeRemoved(RoadNode node)
        {
            if (!nodeObjects.TryGetValue(node.Id, out var go)) return;
            DestroyMeshObject(go);
            nodeObjects.Remove(node.Id);
        }

        /// <summary>Knotenscheibe so groß wie die breiteste angeschlossene Straße.</summary>
        void RefreshNode(RoadNode node)
        {
            if (nodeObjects.TryGetValue(node.Id, out var old))
            {
                DestroyMeshObject(old);
                nodeObjects.Remove(node.Id);
            }
            if (network.GetNode(node.Id) == null || node.SegmentIds.Count == 0) return;

            float width = 0f;
            foreach (int id in node.SegmentIds)
                width = Mathf.Max(width, network.GetSegment(id).Type.Width);

            var go = CreateMeshObject($"Node {node.Id}", RoadMeshBuilder.BuildDisc(width * 0.5f, roadHeight));
            go.transform.position = SimConvert.ToWorld(node.Position);
            nodeObjects[node.Id] = go;
        }

        GameObject CreateMeshObject(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = roadMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void DestroyMeshObject(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(go);
        }
    }
}
