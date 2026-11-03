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
            BindWorld();
            runner.WorldReplaced += OnWorldReplaced; // Speichern & Laden
        }

        CityBuilder.Simulation.Environment.TerrainMap terrain;

        void BindWorld()
        {
            network = runner.World.Roads;
            network.SegmentAdded += OnSegmentAdded;
            network.SegmentRemoved += OnSegmentRemoved;
            network.NodeRemoved += OnNodeRemoved;

            foreach (var s in network.Segments) OnSegmentAdded(s);
            terrain = runner.World.Terrain;
            terrain.Changed += HandleTerrainChanged;
        }

        // --- Speichern & Laden: neue Welt → alte Abos lösen, alle Meshes verwerfen, neu aufbauen
        void OnWorldReplaced()
        {
            UnbindWorld();
            foreach (var mf in segmentObjects.Values) DestroyMeshObject(mf);
            foreach (var mf in nodeObjects.Values) DestroyMeshObject(mf);
            segmentObjects.Clear();
            nodeObjects.Clear();
            dirtySegments.Clear();
            dirtyNodes.Clear();
            BindWorld();
        }

        void UnbindWorld()
        {
            if (terrain != null) terrain.Changed -= HandleTerrainChanged;
            if (network != null)
            {
                network.SegmentAdded -= OnSegmentAdded;
                network.SegmentRemoved -= OnSegmentRemoved;
                network.NodeRemoved -= OnNodeRemoved;
            }
        }
        // --- Ende Speichern & Laden

        /// <summary>Gelände geändert → Brücken/Tröge in dem Bereich neu bauen (Pfeilerlänge, Trogwände).</summary>
        void HandleTerrainChanged(System.Numerics.Vector2 min, System.Numerics.Vector2 max)
        {
            foreach (var s in network.Segments)
            {
                CityBuilder.Simulation.Roads.Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var smin, out var smax);
                if (smax.X < min.X || smax.Y < min.Y || smin.X > max.X || smin.Y > max.Y) continue;
                dirtySegments.Add(s.Id);
                // Eckstücke an Knicken tragen ebenfalls Brücken-/Trogteile
                if (ShapeOf(s.Start) == NodeShape.Bend) dirtyNodes.Add(s.Start.Id);
                if (ShapeOf(s.End) == NodeShape.Bend) dirtyNodes.Add(s.End.Id);
            }
        }

        void OnDestroy()
        {
            if (runner != null) runner.WorldReplaced -= OnWorldReplaced;
            UnbindWorld();
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

        /// <summary>
        /// Kreuzung (ab 3 Straßen) oder Typwechsel: eigene Knotenfläche, Segmente enden davor.
        /// Knick gleichen Typs: Eckstück mit vollem Querschnitt, Segmente enden davor. Sonst nichts.
        /// </summary>
        NodeShape ShapeOf(RoadNode node) => JunctionGeometry.Classify(network, node);

        void RebuildSegment(RoadSegment seg)
        {
            float trimStart = RoadMeshBuilder.JunctionTrim(network, seg.Start, seg);
            float trimEnd = RoadMeshBuilder.JunctionTrim(network, seg.End, seg);
            // An Knicken bringt das Eckstück Brückenplatte/Trogwände selbst mit; an Kreuzungen laufen sie darunter durch
            float structStart = ShapeOf(seg.Start) == NodeShape.Bend ? trimStart : 0f;
            float structEnd = ShapeOf(seg.End) == NodeShape.Bend ? trimEnd : 0f;

            if (!segmentObjects.TryGetValue(seg.Id, out var mf))
            {
                mf = CreateMeshObject($"Road {seg.Id} ({seg.Type.DisplayName})", materials.All);
                segmentObjects[seg.Id] = mf;
            }
            mf.sharedMesh = RoadMeshBuilder.BuildSegment(seg, trimStart, trimEnd, mf.sharedMesh, network.Ground, structStart, structEnd);
            // --- Wasserstraße: Klappe zeichnet die ShippingView (Modell mit beweglichen Klappen statt fester Fahrbahn)
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = !seg.IsDrawbridge;
        }

        void RebuildNode(RoadNode node)
        {
            var shape = ShapeOf(node);
            if (shape == NodeShape.None)
            {
                // Straßenende, Sackgasse oder gerade Fortsetzung: keine Knotenfläche (Segmente stoßen bündig aneinander)
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

            if (shape == NodeShape.Junction)
            {
                // Kreuzung: Fahrbahnfläche mit gerundeten Bordsteinecken + Gehweg-Ecken
                mr.sharedMaterials = new[] { materials[RoadSurface.Asphalt], materials[RoadSurface.Sidewalk] };
                mf.sharedMesh = RoadMeshBuilder.BuildJunction(network, node, mf.sharedMesh);
                return;
            }

            // Knick: Eckstück mit vollem Querschnitt (alle Oberflächen wie ein Segment)
            mr.sharedMaterials = materials.All;
            mf.sharedMesh = RoadMeshBuilder.BuildBend(JunctionGeometry.Bend(network, node), mf.sharedMesh, network.Ground);
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
