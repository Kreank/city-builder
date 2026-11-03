using System.Collections.Generic;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Traffic
{
    /// <summary>
    /// Stau-Ansicht (V): färbt jede Straße nach dem Verhältnis Ist-/Sollgeschwindigkeit
    /// (grün = frei, gelb = zäh, rot = Stau).
    /// </summary>
    public sealed class TrafficOverlayView : MonoBehaviour
    {
        SimulationRunner runner;
        Material lineMaterial;
        readonly Dictionary<int, LineRenderer> lines = new Dictionary<int, LineRenderer>();
        Transform root;
        int networkVersion = -1;
        float nextColorUpdate;

        public bool Visible { get; private set; }

        public void Init(SimulationRunner r, Material transparent)
        {
            runner = r;
            lineMaterial = new Material(transparent) { color = Color.white, name = "TrafficOverlay" };
            r.WorldReplaced += () =>
            {
                // Speichern & Laden: Linien gehören zu Segment-Ids der alten Welt
                foreach (var l in lines.Values) if (l != null) Destroy(l.gameObject);
                lines.Clear();
                networkVersion = -1;
            };
        }

        void Start()
        {
            root = new GameObject("Traffic Overlay").transform;
            root.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        public void Toggle()
        {
            Visible = !Visible;
            root.gameObject.SetActive(Visible);
            networkVersion = -1;
        }

        void Update()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.vKey.wasPressedThisFrame) Toggle();
            if (!Visible || runner == null || runner.World == null) return;

            var roads = runner.World.Roads;
            if (roads.Version != networkVersion)
            {
                networkVersion = roads.Version;
                RebuildLines(roads);
                nextColorUpdate = 0f;
            }
            if (Time.unscaledTime >= nextColorUpdate)
            {
                nextColorUpdate = Time.unscaledTime + 0.5f;
                UpdateColors(roads);
            }
        }

        void RebuildLines(RoadNetwork roads)
        {
            var alive = new HashSet<int>();
            foreach (var s in roads.Segments)
            {
                alive.Add(s.Id);
                if (lines.ContainsKey(s.Id)) continue;
                var go = new GameObject($"Overlay {s.Id}");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMaterial;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.alignment = LineAlignment.TransformZ;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                lr.widthMultiplier = Mathf.Max(3f, s.Type.Width * 0.6f);
                const int n = 16;
                lr.positionCount = n + 1;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    lr.SetPosition(i, SimConvert.ToWorld(s.Evaluate(t), s.HeightAt(t) + 0.6f));
                }
                lines[s.Id] = lr;
            }
            var dead = new List<int>();
            foreach (var kv in lines) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                Destroy(lines[id].gameObject);
                lines.Remove(id);
            }
        }

        void UpdateColors(RoadNetwork roads)
        {
            var traffic = runner.World.Traffic;
            foreach (var kv in lines)
            {
                float ratio = 1f;
                foreach (var lane in traffic.Lanes.GetLanes(kv.Key)) ratio = Mathf.Min(ratio, lane.SpeedRatio);
                var c = ratio > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.15f, 0.85f, 0.25f), (ratio - 0.5f) * 2f)
                                     : Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(1f, 0.85f, 0.1f), ratio * 2f);
                c.a = 0.75f;
                kv.Value.startColor = kv.Value.endColor = c;
            }
        }
    }
}
