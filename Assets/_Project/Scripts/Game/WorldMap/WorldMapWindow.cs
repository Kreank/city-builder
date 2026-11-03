using System;
using System.Text;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.WorldMap
{
    /// <summary>
    /// Weltkarte (Etappe 5, IMGUI-Fenster, Taste F4, verschiebbar): Textur aus dem Weltmodell, umschaltbar zwischen
    /// Biomen, Relief, Ländern, Klima und Bodenschätzen. Startregion weiß umrandet, Hauptstädte als Punkte (Spieler-Land
    /// gelb). Maus über der Karte zeigt die Zelle (Klima, Region, Land, Vorkommen); rechts das Spieler-Land mit Stärken
    /// und aggregierter Volkswirtschaft. Liest nur. Begleiter des <see cref="RoadTool"/>.
    /// </summary>
    public sealed class WorldMapWindow : MonoBehaviour
    {
        public enum Mode { Biomes, Relief, Countries, Temperature, Precipitation, Fertility, Forest, Ore, Coal, Oil, Sand, Groundwater }

        public static readonly string[] ModeNames =
            { "Biome", "Relief", "Länder", "Temperatur", "Niederschlag", "Boden", "Wald", "Erz", "Kohle", "Öl", "Sand/Kies", "Grundwasser" };

        const int WindowId = 0x3E7D;
        const float Scale = 2f;

        RoadTool tool;
        Rect window = new Rect(0f, 0f, 1060f, 560f);
        bool visible, placed;
        Mode mode = Mode.Biomes;
        Texture2D texture;
        Mode shownMode = (Mode)(-1);
        int shownCountryVersion = -1;
        Vector2 scroll;
        GUIStyle small, header;

        public bool Visible { get => visible; set => visible = value; }

        public void Init(RoadTool roadTool)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => shownCountryVersion = -1; // Speichern & Laden: anderer Planet
        }

        public Planet Planet => tool != null && tool.Runner != null && tool.Runner.World != null ? tool.Runner.World.Planet : null;

        void Update()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f4Key.wasPressedThisFrame) visible = !visible;
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }

        void OnGUI()
        {
            if (!visible) return;
            if (CityBuilder.Game.UI.Panels.UiPanels.ToolkitActive) return; // neue UI: UI/Windows/WorldMapUiWindow (IMGUI nur Rückfall)
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
            }
            if (!placed)
            {
                window.width = Mathf.Min(window.width, Screen.width - 20f);
                window.x = Mathf.Max(10f, Screen.width * 0.5f - window.width * 0.5f);
                window.y = HudLayout.TopInset + 10f;
                placed = true;
            }
            UiBlocker.Register(window);
            window = GUI.Window(WindowId, window, Draw, "Weltkarte (F4)");
        }

        void Draw(int id)
        {
            var p = Planet;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f))) visible = false;
            GUILayout.EndHorizontal();
            if (p == null)
            {
                GUILayout.Label("Keine Weltkarte – im SimulationRunner „Generate World“ einschalten.", small);
                GUI.DragWindow();
                return;
            }

            // Modus-Leiste
            GUILayout.BeginHorizontal();
            for (int m = 0; m < ModeNames.Length; m++)
            {
                bool on = GUILayout.Toggle(mode == (Mode)m, ModeNames[m], GUI.skin.button, GUILayout.Height(22f));
                if (on && mode != (Mode)m) mode = (Mode)m;
            }
            GUILayout.EndHorizontal();

            if (texture == null || shownMode != mode || shownCountryVersion != p.StartRegionId) Rebuild(p);

            GUILayout.BeginHorizontal();
            // Karte
            GUILayout.BeginVertical(GUILayout.Width(p.Width * Scale));
            var mapRect = GUILayoutUtility.GetRect(p.Width * Scale, p.Height * Scale, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(mapRect, texture, ScaleMode.StretchToFill, false);
            int hover = -1;
            var mouse = Event.current.mousePosition;
            if (mapRect.Contains(mouse))
            {
                int x = Mathf.Clamp((int)((mouse.x - mapRect.x) / Scale), 0, p.Width - 1);
                int y = Mathf.Clamp((int)((mouse.y - mapRect.y) / Scale), 0, p.Height - 1);
                hover = p.Index(x, y);
            }
            GUILayout.Label(hover >= 0 ? CellText(p, hover) : "<i>Maus über die Karte: Zelleninfo</i>", small, GUILayout.Height(56f));
            GUILayout.EndVertical();

            // Spieler-Land
            GUILayout.BeginVertical();
            scroll = GUILayout.BeginScrollView(scroll);
            CountryPanel(p, hover >= 0 && p.CountryOfCell(hover) != null ? p.CountryOfCell(hover) : p.PlayerCountry);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        /// <summary>Kartentextur für einen Modus (für die UI-Toolkit-Weltkarte; wird bei Moduswechsel neu gefärbt).</summary>
        public Texture2D TextureFor(Mode m)
        {
            var p = Planet;
            if (p == null) return null;
            mode = m;
            if (texture == null || shownMode != mode || shownCountryVersion != p.StartRegionId) Rebuild(p);
            return texture;
        }

        // ------------------------------------------------------------ Texte

        public static string CellText(Planet p, int i)
        {
            var sb = new StringBuilder();
            float lat = p.LatitudeOf(i), lon = p.Longitude(p.XOf(i));
            sb.Append($"{Mathf.Abs(lat):0}° {(lat >= 0 ? "N" : "S")}, {Mathf.Abs(lon):0}° {(lon >= 0 ? "O" : "W")} · <b>{Biomes.DisplayName(p.BiomeOf[i])}</b> · ");
            sb.Append(p.IsLand(i) ? $"Höhe {p.Elevation[i]:0} m" : $"Tiefe {-p.Elevation[i]:0} m");
            sb.Append($" · {p.Temperature[i]:0.0} °C (±{p.Amplitude[i]:0}) · {p.Precipitation[i]:0} mm/Jahr");
            if (p.IsRiver(i)) sb.Append(" · Fluss");
            var c = p.CountryOfCell(i);
            if (c != null) sb.Append($"\nRegion <b>{p.Regions[p.RegionOf[i]].Name}</b>, Land <b>{c.Name}</b>{(c.IsPlayer ? " (Spieler)" : "")}");
            else if (p.RegionOf[i] >= 0) sb.Append($"\nRegion <b>{p.Regions[p.RegionOf[i]].Name}</b> · freies Land (unbesiedelt)");
            sb.Append($"\nBoden {p.Fertility[i]:0.00} · Wald {p.Forest[i]:0.00} · Erz {p.Ore[i]:0.00} · Kohle {p.Coal[i]:0.00} · Öl {p.Oil[i]:0.00} · " +
                      $"Sand {p.Sand[i]:0.00} · Grundwasser {p.Groundwater[i]:0.00}");
            return sb.ToString();
        }

        void CountryPanel(Planet p, Country c)
        {
            if (c == null) return;
            GUILayout.Label($"{c.Name}{(c.IsPlayer ? " <color=#ffd84a>(dein Land)</color>" : "")}", header);
            GUILayout.Label($"Hauptstadt {c.CapitalName} · {c.Regions.Count} Regionen · {c.AreaKm2 / 1e6f:0.00} Mio. km² · Ø {c.MeanTemperature:0.0} °C, {c.Precipitation:0} mm", small);
            if (c.IsPlayer && p.StartRegion != null)
            {
                var r = p.StartRegion;
                GUILayout.Label($"Startregion <b>{r.Name}</b>: {Biomes.DisplayName(r.DominantBiome)}, Relief {r.Ruggedness:0} m, " +
                                $"{(r.HasRiver ? "Fluss" : "kein Fluss")}, {(r.IsCoastal ? "Küste" : "Binnenland")}", small);
            }
            GUILayout.Space(4f);
            GUILayout.Label("<b>Stärken</b> (Rang unter allen Ländern)", small);
            for (int k = 0; k < CountryProfile.Count; k++)
            {
                var s = (Strength)k;
                string mark = s == c.Profile.Primary ? " ★" : s == c.Profile.Secondary ? " ☆" : "";
                GUILayout.Label($"{CountryProfile.DisplayName(s),-14} {Bar(c.Profile[s])} {c.Profile[s] * 100f:0}{mark}", small);
            }
            var e = c.Economy;
            if (e == null) return;
            GUILayout.Space(4f);
            GUILayout.Label($"<b>Volkswirtschaft</b> (aggregiert) · {e.Population / 1e6:0.0} Mio. Einwohner · BIP {e.Gdp / 1e6:0.0} Mio. €/Tag" +
                            (e.LastDay >= 0 ? $" · Tag {e.LastDay + 1}" : " · noch kein Tag gerechnet"), small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Gut</b>", small, GUILayout.Width(130f));
            GUILayout.Label("<b>Saldo/Tag</b>", small, GUILayout.Width(80f));
            GUILayout.Label("<b>Lager</b>", small, GUILayout.Width(55f));
            GUILayout.Label("<b>Preis</b>", small, GUILayout.Width(55f));
            GUILayout.EndHorizontal();
            for (int k = 0; k < GoodsCategories.Count; k++)
            {
                var g = (GoodsCategory)k;
                double bal = e.Balance(g);
                double days = e.Demand[k] > 0 ? e.Stock[k] / e.Demand[k] : 0;
                GUILayout.BeginHorizontal();
                GUILayout.Label(GoodsCategories.DisplayName(g), small, GUILayout.Width(130f));
                GUILayout.Label($"<color={(bal >= 0 ? "#8fe08f" : "#ff8a70")}>{(bal >= 0 ? "+" : "−")}{Math.Abs(bal) / 1e6:0.00} M</color>", small, GUILayout.Width(80f));
                GUILayout.Label($"{days:0} T.", small, GUILayout.Width(55f));
                GUILayout.Label($"{e.Price[k]:0.00} €", small, GUILayout.Width(55f));
                GUILayout.EndHorizontal();
            }
        }

        static string Bar(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 10f), 0, 10);
            return new string('█', n) + new string('░', 10 - n);
        }

        // ------------------------------------------------------------ Textur

        void Rebuild(Planet p)
        {
            if (texture == null)
                texture = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "WorldMap" };
            var px = new Color32[p.Width * p.Height];
            for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < p.Width; x++)
            {
                int i = p.Index(x, y);
                px[(p.Height - 1 - y) * p.Width + x] = CellColor(p, i);   // Textur: Zeile 0 unten
            }
            // Startregion umranden, Hauptstädte markieren
            int start = p.StartRegionId;
            for (int i = 0; i < p.CellCount; i++)
            {
                if (start < 0 || p.RegionOf[i] != start) continue;
                bool edge = false;
                for (int k = 0; k < 4 && !edge; k++)
                {
                    int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dy = k == 2 ? 1 : k == 3 ? -1 : 0;
                    int j = p.Neighbor(i, dx, dy);
                    edge = j < 0 || p.RegionOf[j] != start;
                }
                if (edge) px[(p.Height - 1 - p.YOf(i)) * p.Width + p.XOf(i)] = new Color32(255, 255, 255, 255);
            }
            foreach (var c in p.Countries)
            {
                var col = c.IsPlayer ? new Color32(255, 216, 74, 255) : new Color32(20, 20, 20, 255);
                px[(p.Height - 1 - p.YOf(c.CapitalCell)) * p.Width + p.XOf(c.CapitalCell)] = col;
            }
            texture.SetPixels32(px);
            texture.Apply(false);
            shownMode = mode;
            shownCountryVersion = start;
        }

        static readonly Color32[] BiomeColors =
        {
            new Color32(28, 58, 112, 255),   // Ozean
            new Color32(58, 106, 164, 255),  // Schelf
            new Color32(238, 243, 248, 255), // Eis
            new Color32(166, 170, 146, 255), // Tundra
            new Color32(58, 98, 70, 255),    // Taiga
            new Color32(72, 138, 60, 255),   // Laub-/Mischwald
            new Color32(38, 118, 92, 255),   // Gemäßigter Regenwald
            new Color32(172, 188, 102, 255), // Steppe
            new Color32(226, 206, 142, 255), // Wüste
            new Color32(196, 184, 92, 255),  // Savanne
            new Color32(26, 108, 42, 255),   // Tropischer Regenwald
            new Color32(134, 124, 114, 255), // Hochgebirge
        };

        Color32 CellColor(Planet p, int i)
        {
            bool land = p.IsLand(i);
            var ocean = new Color32(24, 44, 84, 255);
            switch (mode)
            {
                case Mode.Biomes:
                {
                    var c = BiomeColors[Mathf.Clamp((int)p.BiomeOf[i], 0, BiomeColors.Length - 1)];
                    if (p.IsRiver(i)) c = Color32.Lerp(c, new Color32(60, 120, 200, 255), 0.55f);
                    return Shade(p, i, c);
                }
                case Mode.Relief:
                {
                    if (!land) return Color32.Lerp(new Color32(70, 120, 180, 255), new Color32(10, 25, 60, 255), Mathf.Clamp01(-p.Elevation[i] / 5000f));
                    float h = Mathf.Clamp01(p.Elevation[i] / 4500f);
                    var lo = new Color32(70, 140, 70, 255); var mid = new Color32(200, 180, 110, 255); var hi = new Color32(240, 240, 240, 255);
                    return Shade(p, i, h < 0.4f ? Color32.Lerp(lo, mid, h / 0.4f) : Color32.Lerp(mid, hi, (h - 0.4f) / 0.6f));
                }
                case Mode.Countries:
                {
                    if (!land) return ocean;
                    var country = p.CountryOfCell(i);
                    uint rgb = country != null ? country.Color : FreeLandColor; // Spielstart: freies Land hell/neutral
                    var c = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                    // Grenzen dunkler
                    for (int k = 0; k < 4; k++)
                    {
                        int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dy = k == 2 ? 1 : k == 3 ? -1 : 0;
                        int j = p.Neighbor(i, dx, dy);
                        if (j >= 0 && p.IsLand(j) && p.CountryOfCell(j) != country) return Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.45f);
                    }
                    if (p.RegionOf[i] >= 0)
                        for (int k = 0; k < 2; k++)
                        {
                            int j = p.Neighbor(i, k, 1 - k);
                            if (j >= 0 && p.IsLand(j) && p.RegionOf[j] != p.RegionOf[i]) return Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.15f);
                        }
                    return c;
                }
                case Mode.Temperature:
                {
                    float t = Mathf.InverseLerp(-30f, 30f, p.Temperature[i]);
                    var c = t < 0.5f ? Color32.Lerp(new Color32(40, 60, 200, 255), new Color32(240, 240, 220, 255), t * 2f)
                                     : Color32.Lerp(new Color32(240, 240, 220, 255), new Color32(200, 40, 30, 255), (t - 0.5f) * 2f);
                    return land ? c : Color32.Lerp(c, ocean, 0.6f);
                }
                case Mode.Precipitation:
                {
                    float v = Mathf.Clamp01(Mathf.Log(1f + p.Precipitation[i] / 100f) / Mathf.Log(30f));
                    var c = Color32.Lerp(new Color32(225, 200, 140, 255), new Color32(20, 70, 190, 255), v);
                    return land ? c : Color32.Lerp(c, ocean, 0.6f);
                }
                default:
                {
                    float[] field = mode == Mode.Fertility ? p.Fertility : mode == Mode.Forest ? p.Forest : mode == Mode.Ore ? p.Ore
                        : mode == Mode.Coal ? p.Coal : mode == Mode.Oil ? p.Oil : mode == Mode.Sand ? p.Sand : p.Groundwater;
                    float v = Mathf.Clamp01(field[i]);
                    if (!land) return v > 0.01f ? Color32.Lerp(ocean, new Color32(200, 120, 230, 255), v) : ocean;
                    return Color32.Lerp(new Color32(70, 70, 66, 255), ResourceColor(mode), v);
                }
            }
        }

        /// <summary>Spielstart: unbesiedeltes Land ohne Staat (Länder-Ansicht).</summary>
        public const uint FreeLandColor = 0xD8D2C0;

        static Color32 ResourceColor(Mode m)
        {
            switch (m)
            {
                case Mode.Fertility: return new Color32(120, 220, 60, 255);
                case Mode.Forest: return new Color32(30, 160, 60, 255);
                case Mode.Ore: return new Color32(230, 90, 60, 255);
                case Mode.Coal: return new Color32(15, 15, 15, 255);
                case Mode.Oil: return new Color32(200, 120, 230, 255);
                case Mode.Sand: return new Color32(250, 220, 120, 255);
                default: return new Color32(60, 160, 255, 255);
            }
        }

        /// <summary>Einfache Schummerung von Nordwesten.</summary>
        static Color32 Shade(Planet p, int i, Color32 c)
        {
            if (!p.IsLand(i)) return c;
            int j = p.Neighbor(i, -1, -1);
            if (j < 0) return c;
            float d = (p.Elevation[i] - Mathf.Max(0f, p.Elevation[j])) / 1500f;
            float f = Mathf.Clamp(1f + d * 0.35f, 0.7f, 1.2f);
            return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);
        }
    }
}
