using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Tiles;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Tiles
{
    /// <summary>
    /// Kacheln (Begleiter am RoadTool): Kachelgrenzen als geländefolgende Linien – gekauftes Land weiß umrandet, kaufbare Kacheln
    /// goldgelb, gesperrte grau. Solange das Kachel-Fenster offen ist, kräftiger und mit Name + Preis je Kachel; in den
    /// Bauwerkzeugen über nicht gekauftem Land der Hinweis „Kachel nicht gekauft“. Liest nur die Simulation.
    /// Auf der 2-km-Karte (eine Kachel) zeichnet die Ansicht nichts.
    /// </summary>
    public sealed class TileView : MonoBehaviour
    {
        static readonly Color OwnedColor = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color PurchasableColor = new Color(1f, 0.78f, 0.25f, 0.75f);
        static readonly Color LockedColor = new Color(0.55f, 0.55f, 0.6f, 0.35f);
        const float SampleStep = 24f;
        const float Inset = 6f;

        RoadTool tool;
        Material baseMaterial;
        SimulationWorld builtFor;
        int builtVersion = -1;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly List<Material> materials = new List<Material>();

        /// <summary>Vom Kachel-Fenster gesetzt: Grenzen kräftig + Beschriftung mit Preis.</summary>
        public static bool Highlight { get; set; }
        /// <summary>Vom Kachel-Fenster gewählte Kachel (−1 = keine) – wird besonders hervorgehoben.</summary>
        public static Vector2Int Selected { get; set; } = new Vector2Int(-1, -1);

        public void Init(RoadTool roadTool, Material material)
        {
            tool = roadTool;
            baseMaterial = material;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => { builtFor = null; };
        }

        SimulationWorld World => tool != null && tool.Runner != null ? tool.Runner.World : null;

        void OnDestroy()
        {
            foreach (var m in materials) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            var world = World;
            if (world?.Tiles == null) return;
            var tiles = world.Tiles;
            int version = tiles.Version * 1000 + Selected.x * 31 + Selected.y + (Highlight ? 500 : 0);
            if (builtFor != world || builtVersion != version) Rebuild(world, version);
            UpdateWidth();
            UpdateLabels(world);
        }

        // ------------------------------------------------------------ Linien

        void Rebuild(SimulationWorld world, int version)
        {
            builtFor = world;
            builtVersion = version;
            foreach (var l in lines) if (l != null) Destroy(l.gameObject);
            lines.Clear();
            var tiles = world.Tiles;
            if (!tiles.Restricted) return;
            for (int y = 0; y < tiles.TilesPerSide; y++)
            for (int x = 0; x < tiles.TilesPerSide; x++)
            {
                var state = tiles.StateOf(x, y);
                bool selected = Selected.x == x && Selected.y == y;
                // gekaufte Kacheln: nur der Außenrand des gekauften Gebiets (innere Grenzen zwischen zwei gekauften Kacheln entfallen)
                if (state == TileState.Owned)
                {
                    tiles.TileBounds(x, y, out var min, out var max);
                    if (!tiles.IsOwned(x, y - 1)) AddLine(world, min, new SimVector2(max.X, min.Y), OwnedColor, 0f);
                    if (!tiles.IsOwned(x + 1, y)) AddLine(world, new SimVector2(max.X, min.Y), max, OwnedColor, 0f);
                    if (!tiles.IsOwned(x, y + 1)) AddLine(world, max, new SimVector2(min.X, max.Y), OwnedColor, 0f);
                    if (!tiles.IsOwned(x - 1, y)) AddLine(world, new SimVector2(min.X, max.Y), min, OwnedColor, 0f);
                    continue;
                }
                if (state == TileState.Locked && !Highlight) continue; // gesperrte nur im Kauf-Fenster
                var color = selected ? new Color(0.35f, 0.9f, 1f, 0.95f) : state == TileState.Purchasable ? PurchasableColor : LockedColor;
                if (!Highlight && !selected) color.a *= 0.6f;
                tiles.TileBounds(x, y, out var a, out var b);
                var i0 = a + new SimVector2(Inset);
                var i1 = b - new SimVector2(Inset);
                AddLine(world, i0, new SimVector2(i1.X, i0.Y), color, 0.5f);
                AddLine(world, new SimVector2(i1.X, i0.Y), i1, color, 0.5f);
                AddLine(world, i1, new SimVector2(i0.X, i1.Y), color, 0.5f);
                AddLine(world, new SimVector2(i0.X, i1.Y), i0, color, 0.5f);
            }
        }

        void AddLine(SimulationWorld world, SimVector2 from, SimVector2 to, Color color, float lift)
        {
            float len = SimVector2.Distance(from, to);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / SampleStep) + 1);
            var go = new GameObject("Kachelgrenze");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = n;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
            var mat = MaterialFor(color);
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = color;
            var terrain = world.Terrain;
            for (int i = 0; i < n; i++)
            {
                var p = SimVector2.Lerp(from, to, i / (float)(n - 1));
                float h = Mathf.Max(terrain.HeightAt(p), terrain.WaterLevel) + 1.5f + lift;
                lr.SetPosition(i, SimConvert.ToWorld(p, h));
            }
            lines.Add(lr);
        }

        Material MaterialFor(Color color)
        {
            foreach (var m in materials)
                if (m != null && m.color == color) return m;
            Material mat;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) mat = new Material(shader);
            else if (baseMaterial != null) mat = new Material(baseMaterial);
            else return null;
            mat.color = color;
            mat.name = "TileBorder";
            materials.Add(mat);
            return mat;
        }

        /// <summary>Linienbreite wächst mit dem Kameraabstand (bleibt auf dem Bildschirm etwa gleich dick).</summary>
        void UpdateWidth()
        {
            if (lines.Count == 0 || tool.Camera == null) return;
            float d = tool.Camera.transform.position.y;
            float w = Mathf.Clamp(d * (Highlight ? 0.006f : 0.0035f), 1.2f, 22f);
            foreach (var l in lines) if (l != null) l.startWidth = l.endWidth = w;
        }

        // ------------------------------------------------------------ Beschriftung

        void UpdateLabels(SimulationWorld world)
        {
            if (!WorldLabels.Enabled) return;
            WorldLabels.Begin(this);
            var tiles = world.Tiles;
            if (!tiles.Restricted) return;
            if (Highlight)
                for (int y = 0; y < tiles.TilesPerSide; y++)
                for (int x = 0; x < tiles.TilesPerSide; x++)
                {
                    var c = tiles.TileCenter(x, y);
                    var state = tiles.StateOf(x, y);
                    string text = state == TileState.Owned ? $"{tiles.NameOf(x, y)} · gekauft"
                        : state == TileState.Purchasable ? $"{tiles.NameOf(x, y)} · kaufbar {UI.Hud.BuildLabels.Money((float)tiles.NextPrice)}"
                        : $"{tiles.NameOf(x, y)} · erst Nachbarkachel kaufen";
                    float h = Mathf.Max(world.Terrain.HeightAt(c), world.Terrain.WaterLevel) + 40f;
                    WorldLabels.Add(this, SimConvert.ToWorld(c, h), text, state == TileState.Locked ? WorldLabelStyle.Error : WorldLabelStyle.Normal, 0f, true);
                }
            // Bauwerkzeuge: Hinweis über nicht gekauftem Land
            if (tool.IsSelectMode || tool.CurrentMode == RoadTool.Mode.Traffic) return;
            var cursor = tool.Cursor;
            if (tiles.OwnedAt(cursor) || !world.Contains(cursor)) return;
            float ch = Mathf.Max(world.Terrain.HeightAt(cursor), world.Terrain.WaterLevel) + 6f;
            WorldLabels.Add(this, SimConvert.ToWorld(cursor, ch), "Kachel nicht gekauft · Kacheln (Knopf oben)", WorldLabelStyle.Error, -28f, true);
        }
    }
}
