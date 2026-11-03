using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEditor;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Schneller Rauchtest ohne Play Mode: baut alle Straßentypen, Brücken, Kreisverkehr und Vorlagen,
    /// lässt den Verkehr laufen und erzeugt für jedes Segment das echte Mesh.
    ///   Menü: City Builder → Smoke Test
    ///   Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod CityBuilder.Editor.SmokeTest.Run
    /// </summary>
    public static class SmokeTest
    {
        [MenuItem("City Builder/Smoke Test")]
        public static void Run()
        {
            var world = SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = 4096f });
            var roads = world.Roads;
            int errors = 0;

            void Road(SimVector2 a, SimVector2 b, RoadType t, float h0 = 0f, float h1 = 0f, SimVector2? c = null)
            {
                var plan = RoadBuilder.Plan(roads, a, 8f, c, b, 8f, t, h0, h1);
                if (!plan.IsValid) { Debug.LogError($"[Smoke] Plan ungültig {t.Id}: {plan.Error}"); errors++; return; }
                RoadBuilder.Build(roads, plan);
            }

            // Alle Stadttypen als Raster mit Kreuzungen
            var cityTypes = RoadCatalog.All.Where(t => t.Category == RoadCategory.City).ToArray();
            for (int i = 0; i < cityTypes.Length; i++)
            {
                float y = -1500f + i * 120f;
                Road(new SimVector2(-1800, y), new SimVector2(-900, y), cityTypes[i]);
            }
            Road(new SimVector2(-1300, -1600), new SimVector2(-1300, -500), RoadCatalog.CityTwoLane);
            Road(new SimVector2(-1100, -1600), new SimVector2(-1100, -500), RoadCatalog.CityFourLane, 0f, 0f, new SimVector2(-1000, -1050));

            // Brücke über eine Straße
            Road(new SimVector2(-600, -1000), new SimVector2(-600, -900), RoadCatalog.CityTwoLane, 0f, 8f);
            Road(new SimVector2(-600, -900), new SimVector2(-600, -600), RoadCatalog.CityTwoLane, 8f, 8f);
            Road(new SimVector2(-800, -750), new SimVector2(-400, -750), RoadCatalog.CityTwoLane);

            // Tunnel unter einer Straße (Trog → Tunnel → Trog)
            Road(new SimVector2(400, -1300), new SimVector2(400, -1200), RoadCatalog.CityTwoLane, 0f, -8f);
            Road(new SimVector2(400, -1200), new SimVector2(400, -800), RoadCatalog.CityTwoLane, -8f, -8f);
            Road(new SimVector2(400, -800), new SimVector2(400, -700), RoadCatalog.CityTwoLane, -8f, 0f);
            Road(new SimVector2(250, -1000), new SimVector2(550, -1000), RoadCatalog.CityFourLane);

            // Kreisverkehr
            Road(new SimVector2(-200, -1000), new SimVector2(200, -1000), RoadCatalog.CityTwoLane);
            Road(new SimVector2(0, -1200), new SimVector2(0, -800), RoadCatalog.CityTwoLane);
            var rb = RoadBuilder.PlanRoundabout(roads, new SimVector2(0, -1000), 30f, RoadCatalog.DefaultRoundabout);
            if (rb.IsValid) RoadBuilder.BuildRoundabout(roads, rb); else { Debug.LogError($"[Smoke] Kreisverkehr: {rb.Error}"); errors++; }

            // Vorlagen
            int k = 0;
            foreach (RoadTemplateKind kind in System.Enum.GetValues(typeof(RoadTemplateKind)))
            {
                var tp = RoadTemplates.Plan(roads, kind, new SimVector2(-1200 + k * 1100, 900), 0f);
                if (tp.IsValid) RoadTemplates.Build(roads, tp); else { Debug.LogError($"[Smoke] Vorlage {kind}: {tp.Error}"); errors++; }
                k++;
            }

            // Ampel + Schilder setzen, Verkehr laufen lassen
            foreach (var n in roads.Nodes.Where(n => n.SegmentIds.Count == 4).Take(3))
                world.Traffic.Rules.SetControl(n.Id, JunctionControl.TrafficLights);
            for (int i = 0; i < 3000; i++) world.Step();

            // Meshes bauen
            int meshes = 0, vertices = 0;
            foreach (var s in roads.Segments)
            {
                var mesh = RoadMeshBuilder.BuildSegment(s, 5f, 5f);
                vertices += mesh.vertexCount;
                meshes++;
                Object.DestroyImmediate(mesh);
            }
            foreach (var n in roads.Nodes.Where(n => n.SegmentIds.Count >= 3))
            {
                var mesh = RoadMeshBuilder.BuildJunction(roads, n);
                vertices += mesh.vertexCount;
                meshes++;
                Object.DestroyImmediate(mesh);
            }

            // Gelände: erzeugte Hügel/Fluss, Straße auf Gelände, Brücke über den Fluss, Meshes mit Geländebezug
            var hills = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            var hr = hills.Roads;
            var terrain = hills.Terrain;
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int y = 0; y < terrain.Resolution; y += 8)
            for (int x = 0; x < terrain.Resolution; x += 8) { minH = Mathf.Min(minH, terrain[x, y]); maxH = Mathf.Max(maxH, terrain[x, y]); }
            int terrainRoads = 0, terrainErrors = 0;
            for (int i = -2; i <= 2; i++)
            {
                var a = new SimVector2(-120 + i * 10, i * 60);
                var b = new SimVector2(120 + i * 10, i * 60);
                var plan = RoadBuilder.Plan(hr, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, terrain.HeightAt(a), terrain.HeightAt(b));
                if (plan.IsValid) { RoadBuilder.Build(hr, plan); terrainRoads++; }
                else { terrainErrors++; Debug.LogWarning($"[Smoke] Geländestraße {i}: {plan.Error}"); }
            }
            foreach (var s in hr.Segments)
            {
                var mesh = RoadMeshBuilder.BuildSegment(s, 0f, 0f, null, hr.Ground);
                vertices += mesh.vertexCount;
                meshes++;
                Object.DestroyImmediate(mesh);
            }
            Debug.Log($"[Smoke] Gelände {minH:0}…{maxH:0} m, Wasserspiegel {terrain.WaterLevel}, Straßen gebaut {terrainRoads}, abgelehnt {terrainErrors}");

            var st = world.Traffic.Stats;
            Debug.Log($"[Smoke] Segmente {roads.SegmentCount}, Knoten {roads.NodeCount}, Meshes {meshes} ({vertices} Vertices), " +
                      $"Verkehr: erzeugt {st.Spawned}, angekommen {st.Arrived}, hängen geblieben {st.Despawned}, aktiv {st.Active}, Fehler {errors}");
            if (Application.isBatchMode && errors > 0) EditorApplication.Exit(1);
        }
    }
}
