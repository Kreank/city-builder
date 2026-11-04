using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Eigene Assets: Infrastruktur-Modelle aus <c>Models/Infrastructure</c> am Straßennetz – Brückenpfeiler (statt der
    /// Mesh-Quader, gleiche Abstände, Höhe bis zum Gelände), Tunnelportale am Übergang Trog → Tunnel, Schilderbrücken auf
    /// Autobahnen vor jeder Anschlussstelle (Abfahrt), Baustellen-Sets an Gebäuden im Bau. Alles per GPU-Instancing mit
    /// Entfernungs-LOD, liest nur. Fehlt ein Modell, bleibt die bisherige Darstellung.
    /// </summary>
    public sealed class InfrastructureDecoView : MonoBehaviour
    {
        /// <summary>Schilderbrücke so weit vor der Abfahrt (m, entlang der Autobahn).</summary>
        const float GantryBeforeExit = 160f;

        SimulationRunner runner;
        PartsRenderer structures, roadworks;
        int roadsVersion = -1, terrainVersion = -1, cityVersion = -1;
        float nextRoadworks;
        Camera cam;
        readonly List<RoadMeshBuilder.StructureSpot> pillarSpots = new List<RoadMeshBuilder.StructureSpot>();
        readonly List<RoadMeshBuilder.StructureSpot> portalSpots = new List<RoadMeshBuilder.StructureSpot>();

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => roadsVersion = terrainVersion = cityVersion = -1; // Speichern & Laden
        }

        static OwnModelCatalog.Entry Model(string use)
        {
            var c = OwnModelCatalog.Instance;
            return c != null && c.Palette != null ? c.Find(OwnModelRoles.Infrastructure, use) : null;
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null) return;
            var catalog = OwnModelCatalog.Instance;
            if (catalog == null || catalog.Palette == null) return;
            if (structures == null)
            {
                structures = new PartsRenderer(catalog.Palette, 256f, 2600f);
                roadworks = new PartsRenderer(catalog.Palette, 256f, 900f);
            }
            if (cam == null) cam = Camera.main;
            var w = runner.World;
            if (w.Roads.Version != roadsVersion || w.Terrain.Version != terrainVersion)
            {
                roadsVersion = w.Roads.Version;
                terrainVersion = w.Terrain.Version;
                RebuildStructures(w.Roads);
            }
            if (w.City.Version != cityVersion || Time.unscaledTime >= nextRoadworks)
            {
                cityVersion = w.City.Version;
                nextRoadworks = Time.unscaledTime + 1f;
                RebuildRoadworks(w.City);
            }
            structures.Render(cam);
            roadworks.Render(cam);
        }

        static Quaternion Across(SimVector2 right)
        {
            var r = new Vector3(right.X, 0f, right.Y);
            var fwd = Vector3.Cross(r, Vector3.up);
            return fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
        }

        void RebuildStructures(RoadNetwork roads)
        {
            structures.Clear();
            var pillar = Model("bridge_pillar");
            var portal = Model("tunnel_portal");
            var gantry = Model("sign_gantry");
            var pillarParts = pillar != null ? ModelParts.Of(pillar.Model) : null;
            var portalParts = portal != null ? ModelParts.Of(portal.Model) : null;

            if (pillarParts != null || portalParts != null)
                foreach (var s in roads.Segments)
                {
                    if (s.IsDrawbridge) continue; // --- Wasserstraße: Klappbrücke hat eigene Pfeiler (ShippingView)
                    bool elevated = pillarParts != null && s.IsElevated;
                    bool under = portalParts != null && s.IsUnderground;
                    if (!elevated && !under) continue;
                    float trimStart = RoadMeshBuilder.JunctionTrim(roads, s.Start, s), trimEnd = RoadMeshBuilder.JunctionTrim(roads, s.End, s);
                    float structStart = JunctionGeometry.Classify(roads, s.Start) == NodeShape.Bend ? trimStart : 0f;
                    float structEnd = JunctionGeometry.Classify(roads, s.End) == NodeShape.Bend ? trimEnd : 0f;
                    pillarSpots.Clear();
                    portalSpots.Clear();
                    RoadMeshBuilder.StructureSpots(s, structStart, structEnd, roads.Ground, elevated ? pillarSpots : null, under ? portalSpots : null);
                    float hw = s.Type.HalfWidth;
                    foreach (var p in pillarSpots)
                    {
                        // Modell 12,5 m hoch, Kopf 12 m breit: Höhe vom Fundament (1 m im Gelände) bis unter die Platte
                        float bottom = p.GroundHeight - 1f, top = p.RoadHeight - RoadMeshBuilder.DeckThickness;
                        float sy = Mathf.Max(0.2f, (top - bottom) / Mathf.Max(1f, pillarParts.Size.y));
                        float sx = Mathf.Clamp(hw * 1.7f / Mathf.Max(1f, pillarParts.Size.x), 0.5f, 3f);
                        structures.Add(pillarParts, Matrix4x4.TRS(SimConvert.ToWorld(p.Position, bottom), Across(p.Right), new Vector3(sx, sy, 1f)));
                    }
                    foreach (var p in portalSpots)
                    {
                        // Öffnung 11 m für eine 2-spurige Straße – auf die Fahrbahnbreite gezogen, Vorderseite nach draußen
                        float sx = Mathf.Clamp(hw * 2f / 11f, 0.8f, 3.5f);
                        var outDir = new Vector3(p.Out.X, 0f, p.Out.Y);
                        var pos = SimConvert.ToWorld(p.Position, p.RoadHeight) - outDir * 0.5f;
                        structures.Add(portalParts, Matrix4x4.TRS(pos, Quaternion.LookRotation(outDir), new Vector3(sx, 1f, 1f)));
                    }
                }

            // Schilderbrücken: Autobahn-Segment, das in einen Knoten mit abgehender Rampe (Abfahrt) mündet
            if (gantry != null)
            {
                var gantryParts = ModelParts.Of(gantry.Model);
                foreach (var node in roads.Nodes)
                {
                    bool exit = false;
                    foreach (int id in node.SegmentIds)
                    {
                        var x = roads.GetSegment(id);
                        if (x != null && x.Type.Category == RoadCategory.Ramp && x.Start == node) { exit = true; break; }
                    }
                    if (!exit) continue;
                    foreach (int id in node.SegmentIds)
                    {
                        var s = roads.GetSegment(id);
                        if (s == null || s.Type.Category != RoadCategory.Highway || s.End != node || s.Length < 40f) continue;
                        float d = Mathf.Max(s.Length * 0.35f, s.Length - GantryBeforeExit);
                        float t = d / s.Length;
                        if (s.IsCoveredAt(t)) continue;
                        var tan = s.Tangent(t);
                        float sx = Mathf.Clamp(s.Type.HalfWidth * 2f / 16f, 0.7f, 2.5f);
                        // Schilder (Vorderseite +Z) schauen den ankommenden Fahrern entgegen
                        structures.Add(gantryParts, Matrix4x4.TRS(SimConvert.ToWorld(s.Evaluate(t), s.HeightAt(t) + RoadMeshBuilder.RoadHeight),
                            Quaternion.LookRotation(new Vector3(-tan.X, 0f, -tan.Y)), new Vector3(sx, 1f, 1f)));
                    }
                }
            }
        }

        void RebuildRoadworks(CityBuilder.Simulation.Zoning.CitySystem city)
        {
            roadworks.Clear();
            var set = Model("roadworks");
            if (set == null) return;
            var parts = ModelParts.Of(set.Model);
            foreach (var b in city.Buildings)
            {
                if (b.IsConstructed || b.Width < 8f || b.Depth < 8f) continue;
                // Baustellen-Set an der Vorderkante des Grundstücks (auf der Grundstücksplatte)
                var p = b.Entrance - b.Front * 3.2f;
                var rot = Quaternion.LookRotation(new Vector3(b.Front.X, 0f, b.Front.Y));
                float s = Mathf.Min(1f, b.Width * 0.9f / Mathf.Max(1f, parts.Size.x));
                roadworks.Add(parts, Matrix4x4.TRS(SimConvert.ToWorld(p, b.Height + 0.23f), rot, Vector3.one * s));
            }
        }
    }
}
