using CityBuilder.Game.Tiles;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Tiles;
using UnityEngine;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Kacheln (rechtes Panel, Knopf oben in der Leiste): Übersichtskarte der N × N Kacheln (gekauft / kaufbar / gesperrt), Klick wählt
    /// eine Kachel; darunter Gelände, Wasser, Wald, Vorkommen und Verkehrswege der Kachel, Preis und „Kaufen“. Solange das Fenster offen
    /// ist, zeigt die <see cref="TileView"/> die Grenzen kräftig mit Name und Preis. Kaufen über <c>world.Tiles.Purchase</c>.
    /// </summary>
    public sealed class TilesWindow : GameWindow
    {
        const int PixelsPerTile = 48;
        const float MapSize = 300f;

        bool open;
        Vector2Int selected = new Vector2Int(-1, -1);
        Texture2D texture;
        SimulationWorld textureWorld;
        int textureVersion = -1;
        Vector2Int textureSelected;
        TileInfo info;
        SimulationWorld infoWorld;
        Vector2Int infoTile = new Vector2Int(-1, -1);
        string lastMessage;

        public TilesWindow() : base(WindowSlot.Right) { }

        public override float OpenScore => open && World?.Tiles != null ? 55f : 0f;

        public void Toggle()
        {
            open = !open;
            Dismissed = false;
            if (open) EnsureSelection();
        }

        public bool IsOpen => open && !Dismissed;

        public override void Close()
        {
            open = false;
            base.Close();
        }

        public override void Poll()
        {
            // Darstellung der Grenzen folgt dem Fenster (auch wenn ein anderes rechtes Panel es gerade verdeckt: nur wenn sichtbar)
            bool show = IsOpen && World?.Tiles != null && World.Tiles.Restricted;
            TileView.Highlight = show;
            TileView.Selected = show ? selected : new Vector2Int(-1, -1);
        }

        void EnsureSelection()
        {
            var tiles = World?.Tiles;
            if (tiles == null) return;
            if (tiles.InMap(selected.x, selected.y)) return;
            // erste kaufbare Kachel, sonst die Mitte
            for (int y = 0; y < tiles.TilesPerSide; y++)
            for (int x = 0; x < tiles.TilesPerSide; x++)
                if (tiles.StateOf(x, y) == TileState.Purchasable) { selected = new Vector2Int(x, y); return; }
            selected = new Vector2Int(tiles.TilesPerSide / 2, tiles.TilesPerSide / 2);
        }

        public override void DrawHead(Ui ui)
        {
            var tiles = World.Tiles;
            string sub = tiles.Restricted
                ? $"{tiles.OwnedCount} von {tiles.Count} gekauft · Karte {tiles.SizeMeters / 1000f:0.#} × {tiles.SizeMeters / 1000f:0.#} km · nächste Kachel {BuildLabels.Money(tiles.NextPrice)}"
                : $"Karte {tiles.SizeMeters / 1000f:0.#} × {tiles.SizeMeters / 1000f:0.#} km · eine Kachel";
            Head(ui, "Kacheln", Tone.Info, "Bauland kaufen", sub);
        }

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var tiles = world.Tiles;
            if (!tiles.Restricted)
            {
                ui.Note("Diese Karte besteht aus einer einzigen Kachel – die ganze Karte ist Bauland. Neue Spiele starten auf einer größeren Karte aus Kacheln (6 × 6 km), auf der man Nachbarkacheln dazukauft.");
                return;
            }
            if (!tiles.InMap(selected.x, selected.y) || textureWorld != world) { selected = new Vector2Int(-1, -1); EnsureSelection(); }

            ui.Section("Übersicht", "Klick wählt eine Kachel · Norden oben");
            ui.Image(MapTexture(world), MapSize, MapSize, p =>
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(p.x * tiles.TilesPerSide), 0, tiles.TilesPerSide - 1);
                int y = Mathf.Clamp(tiles.TilesPerSide - 1 - Mathf.FloorToInt(p.y * tiles.TilesPerSide), 0, tiles.TilesPerSide - 1);
                selected = new Vector2Int(x, y);
                lastMessage = null;
                ContentVersion++;
            });
            using (ui.Chips())
            {
                ui.Chip("gekauft", Tone.Good);
                ui.Chip("kaufbar", Tone.Warn);
                ui.Chip("gesperrt (erst Nachbar kaufen)", Tone.Muted);
            }

            int sx = selected.x, sy = selected.y;
            if (!tiles.InMap(sx, sy)) return;
            var state = tiles.StateOf(sx, sy);
            var tileInfo = InfoFor(world, sx, sy);
            ui.Section($"Kachel {tiles.NameOf(sx, sy)} · {tiles.DirectionOf(sx, sy)}");
            using (ui.Fields())
            {
                ui.Field("Zustand", state == TileState.Owned ? "gekauft – Bauland" : state == TileState.Purchasable ? "kaufbar" : "gesperrt – erst eine Nachbarkachel kaufen",
                    state == TileState.Owned ? Tone.Good : state == TileState.Purchasable ? Tone.Warn : Tone.Muted);
                if (state != TileState.Owned) ui.Field("Preis", BuildLabels.Money(tiles.NextPrice), Tone.Accent);
                ui.Field("Gelände", $"{tileInfo.MinHeight:0}–{tileInfo.MaxHeight:0} m · {tileInfo.SteepShare * 100f:0} % steil");
                ui.Field("Wasser", $"{tileInfo.WaterShare * 100f:0} % der Fläche");
                ui.Field("Wald", $"{tileInfo.ForestShare * 100f:0} % des Landes");
                string deposits = tileInfo.Deposits.Count == 0 ? "keine nennenswerten" : "";
                for (int i = 0; i < tileInfo.Deposits.Count && i < 3; i++)
                    deposits += (i > 0 ? " · " : "") + $"{tileInfo.Deposits[i].Name} {tileInfo.Deposits[i].Quality * 100f:0} %";
                ui.Field("Vorkommen", deposits);
                string ways = (tileInfo.Highway ? "Autobahn" : "") + (tileInfo.Railway ? (tileInfo.Highway ? " · " : "") + "Fernbahn" : "");
                ui.Field("Verkehrswege", ways.Length > 0 ? ways : "keine");
            }

            var plan = tiles.PlanPurchase(sx, sy);
            using (ui.Actions())
            {
                if (state != TileState.Owned)
                    ui.ActionButton($"Kaufen für {BuildLabels.Money(plan.Cost)}", () => Buy(sx, sy), ActionStyle.Primary, false, plan.IsValid,
                        plan.IsValid ? "Kachel kaufen (Stadtkasse, Kategorie „Kauf von Kacheln“)" : TileSystem.ErrorText(plan.Error));
                ui.ActionButton("Ansehen", () => Look(world, sx, sy), ActionStyle.Secondary, false, true, "Kamera auf die Kachel");
            }
            if (state != TileState.Owned && !plan.IsValid) ui.Text(TileSystem.ErrorText(plan.Error), Tone.Warn);
            if (lastMessage != null) ui.Text(lastMessage, Tone.Good);

            ui.Section("Kaufbar", "jede weitere Kachel wird teurer");
            for (int y = tiles.TilesPerSide - 1; y >= 0; y--)
            for (int x = 0; x < tiles.TilesPerSide; x++)
            {
                if (tiles.StateOf(x, y) != TileState.Purchasable) continue;
                int cx = x, cy = y;
                ui.ListItem(tiles.NameOf(x, y), new Color(0.75f, 0.55f, 0.15f), $"Kachel {tiles.NameOf(x, y)}", tiles.DirectionOf(x, y),
                    BuildLabels.Money(tiles.NextPrice), Tone.Warn, () => { selected = new Vector2Int(cx, cy); lastMessage = null; ContentVersion++; });
            }
            if (tiles.TotalSpent > 0) ui.Note($"Bisher für {tiles.Purchases} Kachel(n) ausgegeben: {BuildLabels.Money(tiles.TotalSpent)}");
        }

        void Buy(int x, int y)
        {
            var tiles = World.Tiles;
            double cost = tiles.NextPrice;
            if (tiles.Purchase(x, y))
            {
                lastMessage = $"Kachel {tiles.NameOf(x, y)} gekauft ({BuildLabels.Money(cost)}) – hier darf jetzt gebaut werden.";
                HudMessages.Info("Kachel gekauft", $"{tiles.NameOf(x, y)} ({tiles.DirectionOf(x, y)}) für {BuildLabels.Money(cost)}", HudIconKind.Tiles, "tile-bought");
                textureVersion = -1;
            }
        }

        void Look(SimulationWorld world, int x, int y)
        {
            var cam = Object.FindAnyObjectByType<CameraController>();
            if (cam == null) return;
            var c = world.Tiles.TileCenter(x, y);
            cam.FocusOn(SimConvert.ToWorld(c, world.Terrain.HeightAt(c)));
        }

        TileInfo InfoFor(SimulationWorld world, int x, int y)
        {
            if (info == null || infoWorld != world || infoTile.x != x || infoTile.y != y)
            {
                info = world.Tiles.Describe(world, x, y);
                infoWorld = world;
                infoTile = new Vector2Int(x, y);
            }
            return info;
        }

        // ------------------------------------------------------------ Übersichtskarte

        Texture2D MapTexture(SimulationWorld world)
        {
            var tiles = world.Tiles;
            int n = tiles.TilesPerSide;
            int size = n * PixelsPerTile;
            if (texture != null && textureWorld == world && textureVersion == tiles.Version && textureSelected == selected && texture.width == size) return texture;
            if (texture == null || texture.width != size)
            {
                if (texture != null) Object.Destroy(texture);
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Kacheln" };
            }
            textureWorld = world;
            textureVersion = tiles.Version;
            textureSelected = selected;
            var px = new Color32[size * size];
            var terrain = world.Terrain;
            float step = tiles.SizeMeters / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texturzeile 0 = unten (Süden) – passt zu Ui.Image (Bild steht aufrecht, Norden oben)
                var p = tiles.Origin + new System.Numerics.Vector2((x + 0.5f) * step, (y + 0.5f) * step);
                float h = terrain.HeightAt(p);
                Color c = terrain.IsWater(p) ? new Color(0.2f, 0.42f, 0.65f) : Color.Lerp(new Color(0.36f, 0.52f, 0.28f), new Color(0.62f, 0.56f, 0.45f), Mathf.InverseLerp(5f, 80f, h));
                int tx = x / PixelsPerTile, ty = y / PixelsPerTile;
                var state = tiles.StateOf(tx, ty);
                if (state == TileState.Purchasable) c = Color.Lerp(c, new Color(0.95f, 0.72f, 0.2f), 0.35f) * 0.85f;
                else if (state == TileState.Locked) { float g = c.grayscale; c = Color.Lerp(c, new Color(g, g, g), 0.6f) * 0.55f; }
                int lx = x % PixelsPerTile, ly = y % PixelsPerTile;
                bool edge = lx == 0 || ly == 0 || lx == PixelsPerTile - 1 || ly == PixelsPerTile - 1;
                bool sel = tx == selected.x && ty == selected.y && (lx <= 1 || ly <= 1 || lx >= PixelsPerTile - 2 || ly >= PixelsPerTile - 2);
                if (sel) c = new Color(0.35f, 0.9f, 1f);
                else if (edge) c = Color.Lerp(c, Color.black, 0.5f);
                c.a = 1f;
                px[y * size + x] = c;
            }
            texture.SetPixels32(px);
            texture.Apply(false);
            return texture;
        }
    }
}
