using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Zoning
{
    /// <summary>
    /// Zeichnet die Gebäude der Stadt: Store-Modell passend zur Zonenart, ans Grundstück angepasst
    /// und zur Straße gedreht; ohne Katalog Platzhalter-Quader in neutralen, leicht variierenden Fassadenfarben.
    /// Im Bau wachsen sie aus dem Boden.
    /// <para>
    /// Zonenanzeige: dezent über eine farbige <b>Grundstücksplatte</b> (Bordkante um das Gebäude). Im Zonen-Werkzeug
    /// und im Info-Modus (<see cref="HighlightZones"/>) wird sie kräftig, und Platzhalter-Quader werden zusätzlich
    /// in Zonenfarbe getönt. Store-Modelle werden nie getönt.
    /// </para>
    /// </summary>
    public sealed class BuildingView : MonoBehaviour
    {
        /// <summary>Fassadenfarben der Platzhalter (hell, neutral) – Auswahl über <see cref="Building.Variant"/>.</summary>
        static readonly Color[] FacadeColors =
        {
            new Color(0.90f, 0.88f, 0.84f), new Color(0.84f, 0.84f, 0.82f), new Color(0.93f, 0.90f, 0.82f),
            new Color(0.80f, 0.78f, 0.74f), new Color(0.88f, 0.86f, 0.88f), new Color(0.86f, 0.82f, 0.76f),
        };
        /// <summary>Oberkante der Grundstücksplatte über der Bodenplatte (m) und Gesamtdicke (reicht etwas ins Gelände).</summary>
        const float PlateTop = 0.22f;
        const float PlateDepth = 0.4f;

        SimulationRunner runner;
        RoadTool tool;
        Material baseMaterial;
        Mesh cube;
        readonly InstancedModels models = new InstancedModels();
        readonly Dictionary<int, Material> facadeMaterials = new Dictionary<int, Material>();
        readonly Dictionary<ZoneType, Material> tintMaterials = new Dictionary<ZoneType, Material>();
        readonly Dictionary<(ZoneType, bool), Material> plateMaterials = new Dictionary<(ZoneType, bool), Material>();
        readonly Dictionary<(ZoneType zone, int facade), List<Matrix4x4>> placeholders = new Dictionary<(ZoneType, int), List<Matrix4x4>>();
        readonly Dictionary<ZoneType, List<Matrix4x4>> plates = new Dictionary<ZoneType, List<Matrix4x4>>();
        readonly Dictionary<int, float> visualHeights = new Dictionary<int, float>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int builtVersion = -1;
        bool anyUnderConstruction;
        FarmView farms; // Eigene Assets

        /// <summary>
        /// Deutliche Zonenfarben (kräftige Platte, getönte Platzhalter) – automatisch an, solange das
        /// Zonen-Werkzeug oder der Info-Modus des <see cref="RoadTool"/> aktiv ist.
        /// </summary>
        public bool HighlightZones { get; private set; }

        /// <summary>Zonen-Grundstücksplatten anzeigen (dezente Zonenanzeige).</summary>
        public bool ShowLotPlates = true;

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
            tool = GetComponent<RoadTool>();
            r.WorldReplaced += () => { builtVersion = -1; visualHeights.Clear(); }; // Speichern & Laden: neu aufbauen
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        void OnDestroy()
        {
            foreach (var m in facadeMaterials.Values) if (m != null) Destroy(m);
            foreach (var m in tintMaterials.Values) if (m != null) Destroy(m);
            foreach (var m in plateMaterials.Values) if (m != null) Destroy(m);
        }

        /// <summary>Sichtbare Höhe des Gebäudes (m über der Bodenplatte) – für Auswahl/Hervorhebung im Inspektor.</summary>
        public float VisualHeight(Building b)
        {
            if (b == null) return 0f;
            return visualHeights.TryGetValue(b.Id, out float h) ? h : PlaceholderHeight(b) * Mathf.Clamp(b.Construction, 0.08f, 1f);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null || cube == null) return;
            // UX: Auswählen ist jetzt der Standard – kräftige Zonen nur im Zonen-Werkzeug und in der Stadtübersicht
            var inspector = tool != null ? tool.GetComponent<CityBuilder.Game.Inspect.InspectorPanel>() : null;
            HighlightZones = tool != null && ((tool.CurrentMode == RoadTool.Mode.Zone && !tool.SelectedFocus.HasValue) || // nicht beim Schwerpunkt-Pinsel
                                              (tool.CurrentMode == RoadTool.Mode.Inspect && inspector != null && inspector.ShowOverview));

            var city = runner.World.City;
            // Neu befüllen, wenn sich Gebäude geändert haben (im Bau: laufend, damit sie wachsen)
            if (city.Version != builtVersion || anyUnderConstruction)
            {
                builtVersion = city.Version;
                Rebuild(city);
            }
            models.Render();
            foreach (var kv in placeholders)
            {
                var mat = HighlightZones ? TintMaterial(kv.Key.zone) : FacadeMaterial(kv.Key.facade);
                Draw(kv.Value, mat, UnityEngine.Rendering.ShadowCastingMode.On);
            }
            if (ShowLotPlates)
                foreach (var kv in plates)
                    Draw(kv.Value, PlateMaterial(kv.Key, HighlightZones), UnityEngine.Rendering.ShadowCastingMode.Off);
        }

        void Draw(List<Matrix4x4> list, Material mat, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            if (list.Count == 0) return;
            var rp = new RenderParams(mat) { shadowCastingMode = shadows, receiveShadows = true };
            for (int start = 0; start < list.Count; start += buffer.Length)
            {
                int n = Mathf.Min(buffer.Length, list.Count - start);
                list.CopyTo(start, buffer, 0, n);
                Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
            }
        }

        void Rebuild(CitySystem city)
        {
            models.Clear();
            foreach (var l in placeholders.Values) l.Clear();
            foreach (var l in plates.Values) l.Clear();
            visualHeights.Clear();
            anyUnderConstruction = false;
            var catalog = AssetCatalog.Instance;

            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) anyUnderConstruction = true;
                float grow = Mathf.Clamp(b.Construction, 0.08f, 1f);
                var front = new Vector3(b.Front.X, 0f, b.Front.Y);
                var look = Quaternion.LookRotation(front);
                var basePos = SimConvert.ToWorld(b.Position, b.Height);
                var model = catalog != null ? PickForLevel(catalog.Buildings(b.Zone), b.Variant, b.Level) : null; // G6

                // Grundstücksplatte in Zonenfarbe (Bordkante um das Gebäude) – Setzen statt Zonen: nur noch für Zonengebäude
                // alter Spielstände; gesetzte Gebäude stehen ohne Platte auf dem Gelände
                if (!b.IsPlayerPlaced)
                {
                    if (!plates.TryGetValue(b.Zone, out var plateList)) plates[b.Zone] = plateList = new List<Matrix4x4>();
                    plateList.Add(Matrix4x4.TRS(basePos + Vector3.up * (PlateTop - PlateDepth * 0.5f), look,
                        new Vector3(b.Width * 0.96f, PlateDepth, b.Depth * 0.96f)));
                }

                // --- Eigene Assets: Bauernhöfe (Landwirtschafts-Firma) zeichnet die FarmView als Hof/Felder je Jahreszeit
                if (farms == null) farms = GetComponent<FarmView>();
                if (farms != null && farms.Handles(b)) { visualHeights[b.Id] = farms.VisualHeight(b); continue; }
                // --- Ende Eigene Assets

                if (model != null)
                {
                    // Modell ans Grundstück anpassen: um 0° oder 90° gedreht, je nachdem was besser passt
                    float w = b.Width * 0.92f, d = b.Depth * 0.92f;
                    float s0 = Mathf.Min(w / model.Size.x, d / model.Size.z);
                    float s90 = Mathf.Min(w / model.Size.z, d / model.Size.x);
                    bool turn = s90 > s0 * 1.1f;
                    float s = Mathf.Min(turn ? s90 : s0, 1.6f);
                    var rot = look * (turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
                    models.Add(model, Matrix4x4.TRS(basePos, rot, new Vector3(s, s * grow, s)));
                    visualHeights[b.Id] = model.Size.y * s * grow;
                }
                else
                {
                    float h = PlaceholderHeight(b) * grow;
                    var size = new Vector3(b.Width * 0.85f, h, b.Depth * 0.85f);
                    var pos = basePos + Vector3.up * (h * 0.5f);
                    var key = (b.Zone, FacadeIndex(b));
                    if (!placeholders.TryGetValue(key, out var list)) placeholders[key] = list = new List<Matrix4x4>();
                    list.Add(Matrix4x4.TRS(pos, look, size));
                    visualHeights[b.Id] = h;
                }
            }
        }

        // --- G6 Ausbaustufen: Modelle der Zonen-Kategorie nach Höhe sortiert und in MaxLevel Bänder geteilt –
        // Stufe 1 wählt aus den niedrigsten, die höchste Stufe aus den höchsten (innerhalb des Bands per Variant).
        // Mit nur einem Modell bleibt es bei diesem (dann zeigt nur der Platzhalter die Stufe über die Höhe).
        static readonly Dictionary<ModelEntry[], int[]> byHeight = new Dictionary<ModelEntry[], int[]>();

        /// <summary>Modell je Stufe (auch für die Vorschau des Haus-Werkzeugs).</summary>
        internal static ModelEntry PickForLevel(ModelEntry[] list, int variant, int level)
        {
            if (!AssetCatalog.Has(list) || list.Length < 2) return AssetCatalog.Pick(list, variant);
            // --- Eigene Assets: Modelle mit fester Stufe aus Name.json („level“) gehen vor
            int fixedCount = 0;
            foreach (var e in list) if (e != null && e.Mesh != null && e.Level == level) fixedCount++;
            if (fixedCount > 0)
            {
                int k = (variant & 0x7fffffff) % fixedCount;
                foreach (var e in list)
                    if (e != null && e.Mesh != null && e.Level == level && k-- == 0) return e;
            }
            // --- Ende Eigene Assets
            if (!byHeight.TryGetValue(list, out var order))
            {
                order = new int[list.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                System.Array.Sort(order, (a, c) =>
                {
                    float ha = list[a] != null ? list[a].Size.y : 0f, hc = list[c] != null ? list[c].Size.y : 0f;
                    return ha != hc ? ha.CompareTo(hc) : a.CompareTo(c);
                });
                byHeight[list] = order;
            }
            int max = CityBuilder.Simulation.LandValue.LandValueRules.MaxLevel;
            int lv = Mathf.Clamp(level, 1, max), n = order.Length;
            int start = n * (lv - 1) / max, end = Mathf.Max(start + 1, n * lv / max);
            int pick = order[start + (variant & 0x7fffffff) % (end - start)];
            return list[pick] != null && list[pick].Mesh != null ? list[pick] : AssetCatalog.Pick(list, variant);
        }
        // --- Ende G6

        static int FacadeIndex(Building b) => (int)((uint)(b.Variant * 2654435761u) >> 8) % FacadeColors.Length;

        static float PlaceholderHeight(Building b)
        {
            float jitter = (0.8f + (b.Variant % 100) / 250f) * (1f + 0.3f * (b.Level - 1)); // G6: höhere Stufe = höher
            switch (b.Zone)
            {
                case ZoneType.ResidentialLow: return 6f * jitter;
                case ZoneType.ResidentialMedium: return 14f * jitter;
                case ZoneType.ResidentialHigh: return 40f * jitter;
                case ZoneType.Mixed: return 16f * jitter;
                case ZoneType.Retail: return 7f * jitter;
                case ZoneType.Office: return 32f * jitter;
                default: return 10f * jitter;
            }
        }

        Material NewMaterial(string name, Color c)
        {
            var m = new Material(baseMaterial) { name = name, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            return m;
        }

        Material FacadeMaterial(int index)
        {
            if (facadeMaterials.TryGetValue(index, out var m)) return m;
            return facadeMaterials[index] = NewMaterial("Building_Facade" + index, FacadeColors[index]);
        }

        /// <summary>Getönter Platzhalter für den Zonen-/Info-Modus (frühere Volltönung).</summary>
        Material TintMaterial(ZoneType z)
        {
            if (tintMaterials.TryGetValue(z, out var m)) return m;
            return tintMaterials[z] = NewMaterial("Building_" + z, Color.Lerp(ZoneView.ZoneColor(z), Color.white, 0.35f));
        }

        Material PlateMaterial(ZoneType z, bool strong)
        {
            if (plateMaterials.TryGetValue((z, strong), out var m)) return m;
            var zc = ZoneView.ZoneColor(z);
            // Normal: gedämpft (Richtung Pflastergrau), damit es nicht schreit; im Zonen-/Info-Modus kräftig
            var c = strong ? zc : Color.Lerp(zc, new Color(0.62f, 0.62f, 0.6f), 0.45f);
            return plateMaterials[(z, strong)] = NewMaterial($"LotPlate_{z}{(strong ? "_Strong" : "")}", c);
        }
    }
}
