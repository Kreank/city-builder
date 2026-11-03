using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Traffic
{
    /// <summary>
    /// Platzhalter für Verkehrszeichen und Ampeln an den Zufahrten sowie Absperrungen/Fahrverbots-Schilder.
    /// Farben: Gelb (Raute) = Vorfahrtstraße, Weiß = Vorfahrt gewähren, Rot (rund) = Stopp,
    /// Mast mit Lampe = Ampel, rot-weiße Schranke = gesperrt, blaues Schild = Fahrverbot/Tempolimit.
    /// </summary>
    public sealed class TrafficSignsView : MonoBehaviour
    {
        SimulationRunner runner;
        Material baseMaterial;
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly List<GameObject> props = new List<GameObject>();
        readonly List<(MeshRenderer lamp, RoadNode node, int approach)> lamps = new List<(MeshRenderer, RoadNode, int)>();
        int laneVersion = -1, rulesVersion = -1;
        Transform root;

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
        }

        void Start()
        {
            root = new GameObject("Traffic Signs").transform;
            root.SetParent(transform, false);
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) if (m != null) Destroy(m);
        }

        Material Mat(string key, Color color)
        {
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(baseMaterial) { name = "Sign_" + key };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); else m.color = color;
            if (key.StartsWith("lamp") && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2f);
            }
            materials[key] = m;
            return m;
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null || root == null) return;
            var traffic = runner.World.Traffic;
            if (traffic.Lanes.Version != laneVersion || traffic.Rules.Version != rulesVersion)
            {
                laneVersion = traffic.Lanes.Version;
                rulesVersion = traffic.Rules.Version;
                Rebuild();
            }

            foreach (var (lamp, node, approach) in lamps)
            {
                var state = traffic.LightAt(node, approach);
                lamp.sharedMaterial = state == LightState.Green ? Mat("lampG", new Color(0.1f, 1f, 0.2f))
                    : state == LightState.Yellow ? Mat("lampY", new Color(1f, 0.8f, 0.1f))
                    : Mat("lampR", new Color(1f, 0.1f, 0.1f));
            }
        }

        void Rebuild()
        {
            foreach (var p in props) Destroy(p);
            props.Clear();
            lamps.Clear();

            var world = runner.World;
            var traffic = world.Traffic;
            foreach (var node in world.Roads.Nodes)
            {
                if (node.SegmentIds.Count < 3) continue;
                var info = traffic.Junctions.Get(node);
                if (!info.IsJunction) continue;
                bool anyHighway = node.SegmentIds.Any(id => world.Roads.GetSegment(id).Type.IsHighway);
                bool roundabout = node.SegmentIds.Any(id => world.Roads.GetSegment(id).IsRoundabout);
                bool show = info.Control == JunctionControl.Signs || info.Control == JunctionControl.TrafficLights ||
                            (info.Control == JunctionControl.Auto && !anyHighway && !roundabout && info.Approaches.Any(a => info.RankOf(a) != 1));
                if (!show) continue;

                foreach (int approach in info.Approaches)
                {
                    var lane = info.IncomingLanes[approach].OrderByDescending(l => l.Rank).First();
                    lane.Sample(Mathf.Max(0f, lane.Length - 3f), 0f, out var p, out float h, out var dir);
                    var right = new SimVector2(dir.Y, -dir.X);
                    float edge = lane.Along ? lane.Segment.Type.HalfWidth - lane.Offset : lane.Segment.Type.HalfWidth + lane.Offset;
                    var pos = SimConvert.ToWorld(p + right * (edge - 0.4f), h);
                    var facing = Quaternion.LookRotation(new Vector3(-dir.X, 0f, -dir.Y));

                    if (info.Control == JunctionControl.TrafficLights) AddLight(pos, facing, node, approach);
                    else AddSign(pos, facing, info.SignOf(approach));
                }
            }

            // Regeln auf Straßen: Sperrung, Fahrverbote, Tempolimit
            foreach (var seg in world.Roads.Segments)
            {
                var r = traffic.Rules.GetSegment(seg.Id);
                if (r == null || r.IsEmpty) continue;
                if (r.Closed)
                {
                    AddBarrier(seg, 0.1f);
                    AddBarrier(seg, 0.9f);
                }
                if (r.Banned != VehicleClass.None || r.SpeedLimitKmh > 0f)
                {
                    var p = seg.Evaluate(0.5f);
                    var right = seg.Right(0.5f);
                    var pos = SimConvert.ToWorld(p + right * (seg.Type.HalfWidth - 0.4f), seg.HeightAt(0.5f));
                    var t = seg.Tangent(0.5f);
                    AddPlate(pos, Quaternion.LookRotation(new Vector3(-t.X, 0f, -t.Y)), Mat("ban", new Color(0.15f, 0.3f, 0.8f)), 2.6f, false);
                }
            }
        }

        void AddSign(Vector3 pos, Quaternion facing, ApproachSign sign)
        {
            if (sign == ApproachSign.None) return;
            Material m = sign == ApproachSign.Priority ? Mat("prio", new Color(1f, 0.85f, 0.1f))
                : sign == ApproachSign.Stop ? Mat("stop", new Color(0.85f, 0.1f, 0.1f))
                : Mat("yield", new Color(0.95f, 0.95f, 0.95f));
            AddPlate(pos, facing, m, 2.6f, sign == ApproachSign.Priority);
        }

        void AddPlate(Vector3 pos, Quaternion facing, Material plateMat, float height, bool diamond)
        {
            var pole = Prim(PrimitiveType.Cylinder, Mat("pole", new Color(0.5f, 0.5f, 0.52f)));
            pole.transform.position = pos + Vector3.up * height * 0.5f;
            pole.transform.localScale = new Vector3(0.1f, height * 0.5f, 0.1f);

            var plate = Prim(PrimitiveType.Cube, plateMat);
            plate.transform.position = pos + Vector3.up * height;
            plate.transform.rotation = facing * Quaternion.Euler(0f, 0f, diamond ? 45f : 0f);
            plate.transform.localScale = new Vector3(0.8f, 0.8f, 0.06f);
        }

        void AddLight(Vector3 pos, Quaternion facing, RoadNode node, int approach)
        {
            // Echtes Ampelmodell aus dem Katalog, Signallampe davor
            var model = AssetCatalog.Pick(AssetCatalog.Instance != null ? AssetCatalog.Instance.TrafficLights : null, 0);
            if (model != null)
            {
                var go = new GameObject("Traffic Light");
                go.transform.SetParent(root, true);
                go.transform.SetPositionAndRotation(pos, facing);
                go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = model.Materials;
                props.Add(go);

                var signal = Prim(PrimitiveType.Sphere, Mat("lampR", Color.red));
                signal.transform.position = pos + Vector3.up * (model.Size.y * 0.85f) + facing * Vector3.forward * (model.Size.z * 0.5f + 0.15f);
                signal.transform.localScale = Vector3.one * 0.35f;
                lamps.Add((signal.GetComponent<MeshRenderer>(), node, approach));
                return;
            }

            var pole = Prim(PrimitiveType.Cylinder, Mat("pole", new Color(0.5f, 0.5f, 0.52f)));
            pole.transform.position = pos + Vector3.up * 2f;
            pole.transform.localScale = new Vector3(0.14f, 2f, 0.14f);

            var housing = Prim(PrimitiveType.Cube, Mat("housing", new Color(0.08f, 0.08f, 0.08f)));
            housing.transform.position = pos + Vector3.up * 4.4f;
            housing.transform.rotation = facing;
            housing.transform.localScale = new Vector3(0.45f, 1.1f, 0.35f);

            var lamp = Prim(PrimitiveType.Sphere, Mat("lampR", Color.red));
            lamp.transform.position = pos + Vector3.up * 4.4f + facing * Vector3.forward * 0.2f;
            lamp.transform.localScale = Vector3.one * 0.45f;
            lamps.Add((lamp.GetComponent<MeshRenderer>(), node, approach));
        }

        void AddBarrier(RoadSegment seg, float t)
        {
            var p = seg.Evaluate(t);
            var tan = seg.Tangent(t);
            var bar = Prim(PrimitiveType.Cube, Mat("barrier", new Color(0.9f, 0.15f, 0.12f)));
            bar.transform.position = SimConvert.ToWorld(p, seg.HeightAt(t) + 0.9f);
            bar.transform.rotation = Quaternion.LookRotation(new Vector3(tan.X, 0f, tan.Y));
            bar.transform.localScale = new Vector3(seg.Type.Width * 0.9f, 0.35f, 0.3f);
            var stripe = Prim(PrimitiveType.Cube, Mat("barrierW", Color.white));
            stripe.transform.position = bar.transform.position + Vector3.up * 0.3f;
            stripe.transform.rotation = bar.transform.rotation;
            stripe.transform.localScale = new Vector3(seg.Type.Width * 0.9f, 0.2f, 0.32f);
        }

        GameObject Prim(PrimitiveType type, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, true);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            props.Add(go);
            return go;
        }
    }
}
