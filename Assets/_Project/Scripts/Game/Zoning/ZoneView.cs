using System.Collections.Generic;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Zoning
{
    /// <summary>
    /// Zeigt das Zonenraster: gemalte Zellen in der Zonenfarbe, freie gültige Zellen als helles Raster.
    /// Sichtbar, solange das Zonen-Werkzeug aktiv ist (oder dauerhaft eingeschaltet).
    /// </summary>
    public sealed class ZoneView : MonoBehaviour
    {
        SimulationRunner runner;
        Material material;
        Mesh mesh;
        MeshRenderer meshRenderer;
        int gridVersion = -1, zoneVersion = -1, cityVersion = -1;
        float nextRebuild;

        public bool Visible { get; set; }

        public static Color ZoneColor(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return new Color(0.45f, 0.85f, 0.35f);
                case ZoneType.ResidentialMedium: return new Color(0.2f, 0.7f, 0.25f);
                case ZoneType.ResidentialHigh: return new Color(0.05f, 0.5f, 0.2f);
                case ZoneType.Mixed: return new Color(0.6f, 0.75f, 0.2f);
                case ZoneType.Retail: return new Color(0.25f, 0.55f, 0.95f);
                case ZoneType.Office: return new Color(0.2f, 0.85f, 0.9f);
                case ZoneType.Industry: return new Color(0.95f, 0.8f, 0.15f);
                default: return new Color(1f, 1f, 1f);
            }
        }

        public void Init(SimulationRunner r, Material transparent)
        {
            runner = r;
            material = new Material(transparent) { color = Color.white, name = "Zones" };
        }

        void Start()
        {
            var go = new GameObject("Zones");
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Zones", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        void LateUpdate()
        {
            if (meshRenderer == null || runner == null || runner.World == null) return;
            meshRenderer.enabled = Visible;
            if (!Visible) return;

            var city = runner.World.City;
            city.Grid.Update();
            bool changed = city.Grid.Version != gridVersion || city.ZoneMap.Version != zoneVersion || city.Version != cityVersion;
            if (!changed || Time.unscaledTime < nextRebuild) return;
            nextRebuild = Time.unscaledTime + 0.1f;
            gridVersion = city.Grid.Version;
            zoneVersion = city.ZoneMap.Version;
            cityVersion = city.Version;
            Rebuild(city);
        }

        void Rebuild(CitySystem city)
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            const float inset = 0.6f;
            float half = ZoneGrid.CellSize * 0.5f - inset;

            foreach (var block in city.Grid.Blocks)
            for (int r = 0; r < ZoneGrid.Depth; r++)
            for (int c = 0; c < block.Columns; c++)
            {
                var cell = block[c, r];
                if (!cell.Valid) continue;
                var zone = city.ZoneMap.Get(cell.Center);
                bool built = city.BuildingAt(cell.Center) != null;
                var color = ZoneColor(zone);
                color.a = zone == ZoneType.None ? 0.18f : built ? 0.25f : 0.55f;

                var a = cell.Along * half;
                var o = cell.Out * half;
                float y = cell.Height + 0.25f;
                int b = verts.Count;
                verts.Add(SimConvert.ToWorld(cell.Center - a - o, y));
                verts.Add(SimConvert.ToWorld(cell.Center + a - o, y));
                verts.Add(SimConvert.ToWorld(cell.Center - a + o, y));
                verts.Add(SimConvert.ToWorld(cell.Center + a + o, y));
                for (int k = 0; k < 4; k++) colors.Add(color);
                // beidseitig, damit die Wicklungsrichtung (links/rechts der Straße) egal ist
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
