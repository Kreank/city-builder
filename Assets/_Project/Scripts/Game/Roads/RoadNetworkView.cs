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

        /// <summary>Beleuchtetes Basis-Material (URP Lit) – auch für Fahrzeuge und Schilder.</summary>
        public Material BaseMaterial => roadMaterial;

        void Start()
        {
            materials = new RoadMaterials(roadMaterial);
            network = runner.World.Roads;
            network.SegmentAdded += OnSegmentAdded;
            network.SegmentRemoved += OnSegmentRemoved;
            network.NodeRemoved += OnNodeRemoved;

            foreach (var s in network.Segments) OnSegmentAdded(s);
            runner.World.Terrain.Changed += HandleTerrainChanged;
        }

        /// <summary>Gelände geändert → Brücken/Tröge in dem Bereich neu bauen (Pfeilerlänge, Trogwände).</summary>
        void HandleTerrainChanged(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
        {
            foreach (var s in network.Segments)
            {
                CityBuilder.Simulation.Roads.Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var smin, out var smax);
                if (smax.X < min.X || smax.Y < min.Y || smin.X > max.X || smin.Y > max.Y) continue;
                dirtySegments.Add(s.Id);
            }
        }

        void OnDestroy()
        {
            if (runner != null && runner.World != null) runner.World.Terrain.Changed -= HandleTerrainChanged;
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

        /// <summary>Kreuzung (ab 3 Straßen) oder Typwechsel: eigene Knotenfläche, Segmente enden davor.</summary>
        bool IsJunction(RoadNode node) => RoadMeshBuilder.NeedsJunctionMesh(network, node);

        void RebuildSegment(RoadSegment seg)
        {
            float trimStart = IsJunction(seg.Start) ? RoadMeshBuilder.JunctionTrim(network, seg.Start, seg) : 0f;
            float trimEnd = IsJunction(seg.End) ? RoadMeshBuilder.JunctionTrim(network, seg.End, seg) : 0f;

            if (!segmentObjects.TryGetValue(seg.Id, out var mf))
            {
                mf = CreateMeshObject($"Road {seg.Id} ({seg.Type.DisplayName})", materials.All);
                segmentObjects[seg.Id] = mf;
            }
            mf.sharedMesh = RoadMeshBuilder.BuildSegment(seg, trimStart, trimEnd, mf.sharedMesh, network.Ground);
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

            if (!nodeObjects.TryGetValue(node.Id, out var mf))
            {
                mf = CreateMeshObject($"Node {node.Id}", new[] { materials[RoadSurface.Asphalt] });
                nodeObjects[node.Id] = mf;
            }
            var mr = mf.GetComponent<MeshRenderer>();

            if (IsJunction(node))
            {
                // Kreuzung: Fläche aus den Fahrbahnkanten mit gerundeten Ecken + Gehweg-Ecken
                mr.sharedMaterials = new[] { materials[RoadSurface.Asphalt], materials[RoadSurface.Sidewalk] };
                mf.sharedMesh = RoadMeshBuilder.BuildJunction(network, node, mf.sharedMesh);
                return;
            }

            // Grad 2: kleine Scheibe schließt Lücken in Knicken (leicht unter der Fahrbahn, Markierungen bleiben sichtbar)
            float radius = 0f;
            foreach (int id in node.SegmentIds) radius = Mathf.Max(radius, network.GetSegment(id).Type.CarriagewayHalfWidth);
            mr.sharedMaterials = new[] { materials[RoadSurface.Asphalt] };
            mf.sharedMesh = RoadMeshBuilder.BuildDisc(node.Position, radius, node.Height + RoadMeshBuilder.RoadHeight - 0.002f, mf.sharedMesh);
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
