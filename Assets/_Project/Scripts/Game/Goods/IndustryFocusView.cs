using System.Collections.Generic;
using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Goods;
using UnityEngine;

namespace CityBuilder.Game.Goods
{
    /// <summary>
    /// Produktion lenken – Darstellung der Schwerpunkt-Gebiete (Begleiter am <see cref="RoadTool"/>): farbige Rasterflächen
    /// (16 m) über dem Gelände, sichtbar im Zonen-Werkzeug (kräftig beim Schwerpunkt-Pinsel, zart beim Zonen-Pinsel).
    /// Schaltet beim Wählen eines Rohstoff-Schwerpunkts die passende Rohstoff-Ansicht (F3) ein und beim Verlassen wieder aus,
    /// sofern der Spieler sie nicht inzwischen selbst umgeschaltet hat. Liest nur.
    /// </summary>
    public sealed class IndustryFocusView : MonoBehaviour
    {
        RoadTool tool;
        Material material;
        Mesh mesh;
        MeshRenderer meshRenderer;
        int shownVersion = -1;
        bool shownStrong;
        float nextRebuild;
        EnvironmentOverlayView overlay;
        IndustryFocus? lastFocus;
        string autoResource; // von uns eingeschaltete Rohstoff-Ansicht

        public static Color FocusColor(IndustryFocus f)
        {
            switch (f)
            {
                case IndustryFocus.Agriculture: return new Color(0.62f, 0.8f, 0.22f);
                case IndustryFocus.Forestry: return new Color(0.12f, 0.52f, 0.2f);
                case IndustryFocus.Ore: return new Color(0.75f, 0.35f, 0.25f);
                case IndustryFocus.Coal: return new Color(0.25f, 0.25f, 0.28f);
                case IndustryFocus.OilGas: return new Color(0.45f, 0.2f, 0.6f);
                case IndustryFocus.BuildingMaterials: return new Color(0.88f, 0.72f, 0.38f);
                case IndustryFocus.Processing: return new Color(0.3f, 0.5f, 0.75f);
                case IndustryFocus.ConsumerGoods: return new Color(0.92f, 0.45f, 0.6f);
                case IndustryFocus.Technology: return new Color(0.15f, 0.8f, 0.85f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (transparent != null) material = new Material(transparent) { color = Color.white, name = "IndustryFocus" };
            if (tool != null && tool.Runner != null) tool.Runner.WorldReplaced += () => shownVersion = -1; // Speichern & Laden
        }

        void Start()
        {
            var go = new GameObject("Industry Focus");
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = "IndustryFocus", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        void LateUpdate()
        {
            if (tool == null || tool.Runner == null || tool.Runner.World == null || meshRenderer == null) return;
            var policy = tool.Runner.World.Policy;
            bool zoneMode = tool.CurrentMode == RoadTool.Mode.Zone;
            IndustryFocus? focus = zoneMode ? tool.SelectedFocus : null;
            SyncOverlay(focus);

            bool visible = zoneMode && policy != null && material != null;
            meshRenderer.enabled = visible;
            if (!visible) return;
            bool strong = focus.HasValue;
            if ((policy.Version == shownVersion && strong == shownStrong) || Time.unscaledTime < nextRebuild) return;
            nextRebuild = Time.unscaledTime + 0.1f;
            shownVersion = policy.Version;
            shownStrong = strong;
            Rebuild(policy, strong);
        }

        /// <summary>Rohstoff-Ansicht passend zum gewählten Schwerpunkt (nur beim Wechsel, damit F3 frei bleibt).</summary>
        void SyncOverlay(IndustryFocus? focus)
        {
            if (focus == lastFocus) return;
            lastFocus = focus;
            if (overlay == null) overlay = FindAnyObjectByType<EnvironmentOverlayView>();
            if (overlay == null) return;
            string res = focus.HasValue ? IndustryBranches.Deposit(focus.Value) : null;
            if (res != null)
            {
                if (overlay.CurrentResourceId != res) { overlay.ShowResource(res); autoResource = res; }
            }
            else if (autoResource != null)
            {
                if (overlay.CurrentResourceId == autoResource) overlay.ShowResource(null); // nur unsere Ansicht wieder aus
                autoResource = null;
            }
        }

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        void Rebuild(ProductionPolicy policy, bool strong)
        {
            verts.Clear(); colors.Clear(); tris.Clear();
            var terrain = tool.Runner.World.Terrain;
            int n = policy.Resolution;
            float half = policy.CellSize * 0.5f - 0.4f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var f = policy.FocusAtCell(x, y);
                if (f == IndustryFocus.Auto) continue;
                var c = FocusColor(f);
                // Rand zu anderem Schwerpunkt kräftiger, Fläche zart (Rohstoff-Ansicht bleibt darunter lesbar)
                bool edge = policy.FocusAtCell(x - 1, y) != f || policy.FocusAtCell(x + 1, y) != f ||
                            policy.FocusAtCell(x, y - 1) != f || policy.FocusAtCell(x, y + 1) != f;
                c.a = strong ? (edge ? 0.55f : 0.3f) : (edge ? 0.3f : 0.14f);
                var centre = policy.CellCenter(x, y);
                int b = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    var p = centre + new System.Numerics.Vector2(k % 2 == 0 ? -half : half, k < 2 ? -half : half);
                    float h = Mathf.Max(terrain.HeightAt(p), terrain.WaterLevel) + 0.6f;
                    verts.Add(new Vector3(p.X, h, p.Y));
                    colors.Add(c);
                }
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
