using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Eigene Assets: Straßen- und Parkausstattung aus <c>Models/Props</c> (Bank, Mülleimer, Hydrant, Briefkasten,
    /// Fahrradständer, Poller, Pflanzkübel, Litfaßsäule; im Park zusätzlich Picknicktisch und Kiosk). Auf den Gehwegen der
    /// Stadtstraßen in festen Abständen und fest aus Segment-Id + Platz gewürfelt – nicht vor Zufahrten von Gebäuden, nicht
    /// an Kreuzungen, nicht an Laternen und Haltestellen, nicht auf Brücken/in Tunneln. In Parks (G4) entlang der Ränder.
    /// Welche Modelle wohin dürfen, sagt <see cref="OwnModelCatalog.Entry.Placement"/> („sidewalk“, „park“, „both“ –
    /// Ordnerregel oder <c>Name.json</c>), neue Requisiten erscheinen also ohne Code. GPU-Instancing mit Entfernungs-LOD (450 m).
    /// </summary>
    public sealed class StreetFurnitureView : MonoBehaviour
    {
        /// <summary>Raster der möglichen Plätze entlang des Gehwegs (m) und Anteil belegter Plätze.</summary>
        const float SlotSpacing = 11f, SlotShare = 0.45f;
        /// <summary>Abstände (m): zur Kreuzung, zur Zufahrt eines Gebäudes, zur Haltestelle, zur Laterne.</summary>
        const float JunctionClearance = 6f, EntranceClearance = 5f, StopClearance = 12f, LampClearance = 2.5f;
        /// <summary>Raster der Laternen im <see cref="ScenicPropsView"/> (gleiche Formel, damit nichts im Mast steht).</summary>
        const float LampSpacing = 32f;

        SimulationRunner runner;
        PartsRenderer props;
        Camera cam;
        int roadsVersion = -1, cityVersion = -1, serviceVersion = -1, stopCount = -1;
        float lastRebuild = -10f;
        readonly Dictionary<int, List<SimVector2>> entrances = new Dictionary<int, List<SimVector2>>();
        readonly List<OwnModelCatalog.Entry> sidewalk = new List<OwnModelCatalog.Entry>(), park = new List<OwnModelCatalog.Entry>();

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => roadsVersion = cityVersion = serviceVersion = stopCount = -1; // Speichern & Laden
        }

        static uint Hash(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093 ^ b * 19349663 ^ salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return h;
            }
        }

        static float H01(int a, int b, int salt) => (Hash(a, b, salt) & 0xFFFFFF) / (float)0xFFFFFF;

        /// <summary>Gewicht je Requisit (häufig: Mülleimer; selten: Litfaßsäule). Unbekannte neue Modelle: 1.</summary>
        static float Weight(string id)
        {
            switch (id)
            {
                case "TrashBin": return 3f;
                case "Bench": return 2f;
                case "Planter": return 2f;
                case "Hydrant": return 1.5f;
                case "AdPillar": return 0.6f;
                case "Kiosk": return 0.4f;
                default: return 1f;
            }
        }

        static OwnModelCatalog.Entry PickWeighted(List<OwnModelCatalog.Entry> list, float roll)
        {
            float sum = 0f;
            foreach (var e in list) sum += Weight(e.Id);
            float x = roll * sum;
            foreach (var e in list)
            {
                x -= Weight(e.Id);
                if (x <= 0f) return e;
            }
            return list.Count > 0 ? list[list.Count - 1] : null;
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null) return;
            var catalog = OwnModelCatalog.Instance;
            if (catalog == null || catalog.Palette == null || catalog.WithRole(OwnModelRoles.Prop).Count == 0) return;
            if (props == null) props = new PartsRenderer(catalog.Palette, 192f, 450f) { Shadows = UnityEngine.Rendering.ShadowCastingMode.Off };
            if (cam == null) cam = Camera.main;
            var w = runner.World;
            int stops = w.Transit != null ? w.Transit.Stops.Count : 0;
            bool changed = w.Roads.Version != roadsVersion || w.City.Version != cityVersion || w.Services.StructureVersion != serviceVersion || stops != stopCount;
            if (changed && Time.unscaledTime - lastRebuild > 0.5f)
            {
                lastRebuild = Time.unscaledTime;
                roadsVersion = w.Roads.Version;
                cityVersion = w.City.Version;
                serviceVersion = w.Services.StructureVersion;
                stopCount = stops;
                Rebuild(catalog);
            }
            props.Render(cam);
        }

        void Rebuild(OwnModelCatalog catalog)
        {
            var w = runner.World;
            props.Clear();
            sidewalk.Clear();
            park.Clear();
            foreach (var e in catalog.WithRole(OwnModelRoles.Prop))
            {
                string p = string.IsNullOrEmpty(e.Placement) ? "sidewalk" : e.Placement.ToLowerInvariant();
                if (p == "sidewalk" || p == "both") sidewalk.Add(e);
                if (p == "park" || p == "both") park.Add(e);
            }

            entrances.Clear();
            foreach (var b in w.City.Buildings)
            {
                if (!entrances.TryGetValue(b.FrontSegmentId, out var l)) entrances[b.FrontSegmentId] = l = new List<SimVector2>();
                l.Add(b.Entrance);
            }
            var stopsBySegment = new Dictionary<int, List<SimVector2>>();
            if (w.Transit != null)
                foreach (var s in w.Transit.Stops)
                {
                    if (!stopsBySegment.TryGetValue(s.SegmentId, out var l)) stopsBySegment[s.SegmentId] = l = new List<SimVector2>();
                    l.Add(s.Position);
                }

            var roads = w.Roads;
            if (sidewalk.Count > 0)
                foreach (var s in roads.Segments)
                {
                    if (s.Type.Category != RoadCategory.City || s.Length < 24f) continue;
                    float trim0 = RoadMeshBuilder.JunctionTrim(roads, s.Start, s), trim1 = RoadMeshBuilder.JunctionTrim(roads, s.End, s);
                    entrances.TryGetValue(s.Id, out var doors);
                    stopsBySegment.TryGetValue(s.Id, out var halts);
                    int laneIndex = 0;
                    foreach (var lane in s.Type.Lanes)
                    {
                        laneIndex++;
                        if (lane.Kind != LaneKind.Sidewalk) continue;
                        bool rightSide = lane.Offset > 0f;
                        float lampStart = trim0 + 6f + (rightSide ? 0f : LampSpacing * 0.5f);
                        int slot = 0;
                        for (float d = trim0 + JunctionClearance; d < s.Length - trim1 - JunctionClearance; d += SlotSpacing, slot++)
                        {
                            if (H01(s.Id, slot * 8 + laneIndex, 1) > SlotShare) continue;
                            float lampPhase = Mathf.Repeat(d - lampStart, LampSpacing);
                            if (lampPhase < LampClearance || lampPhase > LampSpacing - LampClearance) continue;
                            float t = d / s.Length;
                            if (s.IsCoveredAt(t) || Mathf.Abs(s.ElevationAt(t)) > 0.6f) continue;
                            var model = PickWeighted(sidewalk, H01(s.Id, slot * 8 + laneIndex, 2));
                            if (model == null) continue;
                            var parts = ModelParts.Of(model.Model);
                            float depth = Mathf.Max(0.3f, parts.Size.z);
                            if (depth + 0.4f > lane.Width) continue; // Kiosk o. Ä. passt nicht auf schmale Gehwege
                            float offset = rightSide ? lane.Right - depth * 0.5f - 0.3f : lane.Left + depth * 0.5f + 0.3f;
                            var p = s.Evaluate(t) + s.Right(t) * offset;
                            if (Near(doors, p, EntranceClearance) || Near(halts, p, StopClearance)) continue;
                            if (w.Services.FacilityAt(p, 0.5f) != null) continue;
                            var toRoad = rightSide ? -s.Right(t) : s.Right(t);
                            var pos = SimConvert.ToWorld(p, s.HeightAt(t) + RoadMeshBuilder.RoadHeight + 0.15f);
                            props.Add(parts, Matrix4x4.TRS(pos, Quaternion.LookRotation(new Vector3(toRoad.X, 0f, toRoad.Y)), Vector3.one));
                        }
                    }
                }

            // Parks: Bänke, Mülleimer, Picknicktische entlang der Ränder; großer Park mit Kiosk
            if (park.Count > 0)
                foreach (var f in w.Services.Facilities)
                {
                    if (f.Kind != ServiceKinds.Park) continue;
                    int count = Mathf.Clamp(Mathf.RoundToInt(f.Type.Width * f.Type.Depth / 220f), 2, 14);
                    for (int i = 0; i < count; i++)
                    {
                        var model = PickWeighted(park, H01(f.Id, i, 21));
                        if (model == null) continue;
                        if (model.Id == "Kiosk" && f.Type.Width < 30f) continue;
                        // auf dem inneren Ring (Weg um die Mitte), Blick zur Parkmitte
                        float a = (i + H01(f.Id, i, 22) * 0.6f) / count * Mathf.PI * 2f;
                        var dir = new SimVector2(Mathf.Cos(a), Mathf.Sin(a));
                        var local = new SimVector2(dir.X * f.Type.Width * 0.3f, dir.Y * f.Type.Depth * 0.3f);
                        var p = f.Position + f.Side * local.X + f.Front * local.Y;
                        var toCenter = f.Position - p;
                        var look = toCenter.LengthSquared() > 1e-4f ? new Vector3(toCenter.X, 0f, toCenter.Y) : Vector3.forward;
                        props.Add(ModelParts.Of(model.Model), Matrix4x4.TRS(SimConvert.ToWorld(p, f.Height + 0.32f), Quaternion.LookRotation(look), Vector3.one));
                    }
                }
        }

        static bool Near(List<SimVector2> points, SimVector2 p, float r)
        {
            if (points == null) return false;
            foreach (var q in points) if (SimVector2.DistanceSquared(p, q) < r * r) return true;
            return false;
        }
    }
}
