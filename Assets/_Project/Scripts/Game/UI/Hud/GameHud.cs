using System;
using System.Collections.Generic;
using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Services;
using CityBuilder.Game.Terrain;
using CityBuilder.Game.Traffic;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.UI.Windows;
using CityBuilder.Game.Utilities;
using CityBuilder.Simulation.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Spiel-UI mit UI Toolkit (Begleiter am <see cref="RoadTool"/>): obere Leiste (Stadt, Datum, Tempo, Einwohner,
    /// Arbeitslose, Versorgungswarnung, Info-Modus), linke Ansichtsleiste (Overlays), Bauleiste unten
    /// (<c>GameHud.BuildBar.cs</c>), Werkzeug-Hinweis, Meldungen oben rechts und Tooltips.
    /// Aufbau in C# (Inhalte kommen aus Katalogen), Aussehen in <c>Resources/UI/CityHud.uss</c>.
    /// Liest nur; Aktionen gehen über die öffentlichen Methoden von RoadTool, Werkzeugen, Views und Runner –
    /// dieselben Wege wie die Tastenkürzel, deshalb bleiben Tasten und Knöpfe synchron.
    /// </summary>
    public sealed partial class GameHud : MonoBehaviour
    {
        /// <summary>Spielstart: Name der Spielerstadt aus der Simulation (<c>World.Realm</c>, im Spielstand; früher Platzhalter).</summary>
        public static string CityName => activeRunner != null && activeRunner.World != null
            ? activeRunner.World.Realm.CityName : CityBuilder.Simulation.World.PlayerRealm.DefaultCityName;
        static SimulationRunner activeRunner;

        const string ThemeResource = "UI/CityTheme";
        const string StyleResource = "UI/CityHud";
        const float SlowRefreshInterval = 0.2f;
        const int MaxToasts = 4;

        // Anzeige-Kalender: Die Simulation hat 360 Tage je Jahr → 12 Monate à 30 Tage (nur Darstellung).
        static readonly string[] Months =
        {
            "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
        };

        RoadTool tool;
        SimulationRunner runner;
        EnvironmentOverlayView envOverlay;
        TrafficOverlayView trafficOverlay;
        UtilityOverlayView utilityOverlay;
        VehicleView vehicles;
        TerraformTool terraform;
        UtilityTool utilityTool;
        ServiceTool serviceTool;          // G4
        ServiceOverlayView serviceOverlay; // G4
        CityBuilder.Game.Transit.TransitTool transitTool; // ÖPNV

        PanelSettings panelSettings;
        UIDocument document;
        VisualElement root, topBar, rail, overlayName, toastStack, tooltip;
        Label overlayNameValue, tooltipLabel;
        VisualElement hoveredTip;

        /// <summary>Jedes Frame: Zustände (ausgewählt, sichtbar).</summary>
        readonly List<Action> fastRefresh = new List<Action>();
        /// <summary>5× je Sekunde: Texte und Zahlen.</summary>
        readonly List<Action> slowRefresh = new List<Action>();
        float nextSlowRefresh;
        bool refreshNow;

        sealed class Toast
        {
            public VisualElement Element;
            public float Until;
            public string Key;
            public bool Leaving;
        }

        readonly List<Toast> toasts = new List<Toast>();

        /// <summary>Fensterverwaltung (Phase 2): rechtes Panel + mittiges Fenster. Neue Fenster: <c>Windows.Add(new …Window())</c>.</summary>
        public WindowHost Windows { get; private set; }
        WorldLabelLayer worldLabels;
        HelpWindow help;
        WorldMapUiWindow worldMap;
        GoodsWindow goodsWindow; // G5 Wirtschaft (F5)
        TradeWindow tradeWindow; // Etappe 8 Handel (F11)
        VisualElement resourceFlyout;

        /// <summary>Wahr, solange die neue UI läuft (sonst greift das RoadTool auf die alte IMGUI-Leiste zurück).</summary>
        public bool Ready { get; private set; }

        /// <summary>Baut die UI auf. false = Theme/USS nicht gefunden → Aufrufer nimmt die alte Leiste.</summary>
        public bool Init(RoadTool roadTool)
        {
            tool = roadTool;
            runner = roadTool.Runner;
            activeRunner = runner; // Spielstart: Stadtname
            envOverlay = FindAnyObjectByType<EnvironmentOverlayView>();
            trafficOverlay = GetComponent<TrafficOverlayView>();
            utilityOverlay = GetComponent<UtilityOverlayView>();
            vehicles = GetComponent<VehicleView>();
            terraform = GetComponent<TerraformTool>();
            utilityTool = GetComponent<UtilityTool>();
            serviceTool = GetComponent<ServiceTool>();
            serviceOverlay = GetComponent<ServiceOverlayView>();
            transitTool = GetComponent<CityBuilder.Game.Transit.TransitTool>(); // ÖPNV

            var theme = Resources.Load<ThemeStyleSheet>(ThemeResource);
            var style = Resources.Load<StyleSheet>(StyleResource);
            if (theme == null || style == null)
            {
                Debug.LogWarning($"[CityBuilder] Spiel-UI: Resources/{ThemeResource}.tss oder {StyleResource}.uss fehlt – alte IMGUI-Leiste wird benutzt.");
                enabled = false;
                return false;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "City HUD Panel Settings";
            panelSettings.themeStyleSheet = theme;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 10f;

            var go = new GameObject("City HUD (UI Toolkit)");
            go.SetActive(false); // erst konfigurieren, dann aktivieren (UIDocument hängt sich in OnEnable ans Panel)
            go.transform.SetParent(transform, false);
            document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            go.SetActive(true);

            root = document.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[CityBuilder] Spiel-UI: UIDocument ohne Wurzel – alte IMGUI-Leiste wird benutzt.");
                Destroy(go);
                enabled = false;
                return false;
            }
            root.styleSheets.Add(style);
            root.AddToClassList("cb-root");
            root.pickingMode = PickingMode.Ignore;

            root.Add(BuildTopBar());
            root.Add(BuildRail());
            root.Add(BuildOverlayName());
            root.Add(BuildResourceFlyout());
            root.Add(BuildBuildBar());
            toastStack = Container("cb-toasts");
            root.Add(toastStack);
            tooltip = Container("cb-tooltip", "cb-hidden");
            tooltipLabel = Text("");
            tooltip.Add(tooltipLabel);
            root.Add(tooltip);

            UiBlocker.RegisterToolkitRoot(root);
            HudMessages.Posted += OnMessage;
            Ui.TooltipHook = (e, text) => Tip(e, text);
            if (!UiPanels.ForceImgui) BuildWindows();
            // G6: Aufstiege/Verfall gesammelt als Meldung (höchstens 1× je Spieltag)
            var lvAlerts = GetComponent<LandValueAlerts>() ?? gameObject.AddComponent<LandValueAlerts>();
            lvAlerts.Init(runner);
            Ready = true;
            RefreshAll();
            return true;
        }

        void OnDestroy()
        {
            HudMessages.Posted -= OnMessage;
            if (Windows != null) UiPanels.ToolkitActive = false;
            worldLabels?.Dispose();
            Ui.TooltipHook = null;
            if (root != null) UiBlocker.UnregisterToolkitRoot(root);
            if (panelSettings != null) Destroy(panelSettings);
        }

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (!Ready || runner == null || runner.World == null) return;

            UpdateBuildBar();
            foreach (var a in fastRefresh) a();
            foreach (var a in panelFastRefresh) a();
            if (refreshNow || Time.unscaledTime >= nextSlowRefresh)
            {
                refreshNow = false;
                nextSlowRefresh = Time.unscaledTime + SlowRefreshInterval;
                foreach (var a in slowRefresh) a();
                foreach (var a in panelSlowRefresh) a();
            }
            Windows?.Update();
            UpdateToasts();
            PlaceTooltip();
            UpdateLayoutInsets();
        }

        void LateUpdate()
        {
            if (Ready) worldLabels?.Update();
        }

        /// <summary>Fenster der Phase 2 anmelden; ab dann zeichnen die alten IMGUI-Fenster nicht mehr (s. <see cref="UiPanels"/>).</summary>
        void BuildWindows()
        {
            Windows = new WindowHost(tool, root);
            var inspector = tool.GetComponent<CityBuilder.Game.Inspect.InspectorPanel>();
            if (inspector != null) Windows.Add(new CityBuilder.Game.Inspect.InspectorWindow(inspector));
            Windows.Add(new TrafficWindow());
            Windows.Add(new UtilityWindow());
            Windows.Add(new ServiceWindow());
            Windows.Add(new HouseWindow()); // Wohnhäuser setzen: Anforderungen am Mauszeiger (Werkzeug 6)
            Windows.Add(new TransitWindow()); // ÖPNV: Linienübersicht (F6, offen im ÖPNV-Werkzeug)
            var budget = tool.GetComponent<CityBuilder.Game.Economy.BudgetPanel>();
            if (budget != null) Windows.Add(new BudgetWindow(budget));
            help = Windows.Add(new HelpWindow());
            var mapComp = tool.GetComponent<CityBuilder.Game.WorldMap.WorldMapWindow>();
            if (mapComp != null) worldMap = Windows.Add(new WorldMapUiWindow(mapComp));
            // --- G5 Warenketten: Fenster „Wirtschaft“ (F5); Zustand im GoodsPanel
            var goodsComp = tool.GetComponent<CityBuilder.Game.Goods.GoodsPanel>();
            if (goodsComp != null) goodsWindow = Windows.Add(new GoodsWindow(goodsComp));
            // --- Ende G5
            tradeWindow = Windows.Add(new TradeWindow()); // Etappe 8 Handel (F11), nur mit Weltkarte sichtbar
            worldLabels = new WorldLabelLayer(root, tool.Camera);
            UiPanels.ToolkitActive = true;
        }

        void RefreshAll()
        {
            foreach (var a in fastRefresh) a();
            foreach (var a in panelFastRefresh) a();
            foreach (var a in slowRefresh) a();
            foreach (var a in panelSlowRefresh) a();
        }

        /// <summary>Meldet den belegten Platz an die IMGUI-Fenster (Bildschirmpixel).</summary>
        void UpdateLayoutInsets()
        {
            float w = root.layout.width;
            if (float.IsNaN(w) || w < 1f) return;
            float scale = Screen.width / w;
            HudLayout.TopInset = topBar.worldBound.yMax * scale + 8f;
            HudLayout.LeftInset = rail.worldBound.xMax * scale + 8f;
            float top = buildBar.worldBound.yMin;
            if (!float.IsNaN(top)) HudLayout.BottomInset = Mathf.Max(0f, root.layout.height - top) * scale + 8f;

            // Toasts weichen dem rechten Panel bzw. IMGUI-Fenstern am rechten Rand aus
            float right = UiBlocker.RightEdgeImguiWidth() / scale;
            float panelLeft = Windows != null ? Windows.RightPanelLeft : float.NaN;
            if (!float.IsNaN(panelLeft)) right = Mathf.Max(right, w - panelLeft);
            toastStack.style.right = 16f + (right > 0f ? right + 8f : 0f);
            HudLayout.RightInset = right > 0f ? right * scale + 8f : 0f;

            // Rechtes Panel: über die Bauleiste rücken, wenn sie bei schmalen Bildschirmen darunter reicht
            if (Windows != null && !float.IsNaN(top))
            {
                // buildBar selbst ist volle Breite – maßgeblich sind die sichtbaren Leisten darin
                float barRight = 0f;
                foreach (var child in buildBar.Children())
                    if (child.resolvedStyle.display != DisplayStyle.None && !float.IsNaN(child.worldBound.xMax))
                        barRight = Mathf.Max(barRight, child.worldBound.xMax);
                bool overlaps = barRight > w - 16f - 392f;
                Windows.SetRightBottom(overlaps ? Mathf.Max(16f, root.layout.height - top + 8f) : 16f);
            }
        }

        // ------------------------------------------------------------ Bausteine

        static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        /// <summary>Reiner Layout-Container ohne eigene Fläche – lässt Klicks zur Welt durch.</summary>
        static VisualElement Container(params string[] classes)
        {
            var e = El(classes);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        static Label Text(string text, params string[] classes)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        static HudIcon Icon(HudIconKind kind, params string[] classes)
        {
            var i = new HudIcon(kind);
            foreach (var c in classes) i.AddToClassList(c);
            return i;
        }

        /// <summary>Klickbares Element (eigener Knopf statt <c>Button</c>, damit keine Theme-Stile dazwischenfunken).</summary>
        VisualElement OnClick(VisualElement e, Action onClick)
        {
            e.AddManipulator(new Clickable(() =>
            {
                onClick();
                refreshNow = true;
            }));
            return e;
        }

        VisualElement Btn(string label, Action onClick, Func<bool> isOn = null, string tip = null, HudIconKind? icon = null,
            string cls = "cb-btn", List<Action> bind = null)
        {
            var b = El(cls);
            if (icon.HasValue) b.Add(Icon(icon.Value, "cb-icon--s"));
            if (!string.IsNullOrEmpty(label)) b.Add(Text(label));
            OnClick(b, onClick);
            if (isOn != null) (bind ?? fastRefresh).Add(() => b.EnableInClassList("cb-on", isOn()));
            Tip(b, tip);
            return b;
        }

        // ------------------------------------------------------------ Tooltip

        void Tip(VisualElement e, string text) => Tip(e, () => text);

        void Tip(VisualElement e, Func<string> text)
        {
            if (text == null) return;
            e.RegisterCallback<PointerEnterEvent>(_ =>
            {
                string t = text();
                if (string.IsNullOrEmpty(t)) return;
                hoveredTip = e;
                tooltipLabel.text = t;
                tooltip.RemoveFromClassList("cb-hidden");
                tooltip.style.visibility = Visibility.Hidden; // erst nach dem Layout platzieren
            });
            e.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (hoveredTip == e) HideTooltip();
            });
        }

        void HideTooltip()
        {
            hoveredTip = null;
            tooltip.AddToClassList("cb-hidden");
        }

        void PlaceTooltip()
        {
            if (hoveredTip == null) return;
            if (hoveredTip.panel == null || hoveredTip.resolvedStyle.display == DisplayStyle.None) { HideTooltip(); return; }
            var size = tooltip.layout.size;
            if (float.IsNaN(size.x) || size.x < 1f) return;
            var b = root.WorldToLocal(hoveredTip.worldBound);
            float w = root.layout.width, h = root.layout.height;
            float x, y;
            if (b.xMax < 120f)
            {
                // linke Leiste: rechts daneben
                x = b.xMax + 10f;
                y = b.center.y - size.y * 0.5f;
            }
            else
            {
                bool below = b.center.y < h * 0.5f;
                x = b.center.x - size.x * 0.5f;
                y = below ? b.yMax + 8f : b.yMin - size.y - 8f;
            }
            tooltip.style.left = Mathf.Clamp(x, 8f, Mathf.Max(8f, w - size.x - 8f));
            tooltip.style.top = Mathf.Clamp(y, 8f, Mathf.Max(8f, h - size.y - 8f));
            tooltip.style.visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------ Obere Leiste

        VisualElement BuildTopBar()
        {
            topBar = El("cb-panel", "cb-topbar");

            var brand = Container("cb-brand");
            var logo = Container("cb-logo");
            logo.Add(Icon(HudIconKind.Logo, "cb-icon--l"));
            brand.Add(logo);
            var names = Container("cb-col");
            var cityLabel = Text(CityName, "cb-city-name", "cb-heading");
            names.Add(cityLabel);
            var date = Text("", "cb-date");
            names.Add(date);
            brand.Add(names);
            topBar.Add(brand);
            slowRefresh.Add(() => date.text = DateText() + ClimateText());
            slowRefresh.Add(() => { string n = CityName; if (cityLabel.text != n) cityLabel.text = n; }); // Spielstart: Stadtname

            // Spieltempo (Leertaste/1/2/3 bleiben im SimulationRunner)
            var speed = Container("cb-segment");
            var icons = new[] { HudIconKind.Pause, HudIconKind.Play, HudIconKind.Fast, HudIconKind.Faster, HudIconKind.Faster };
            for (int i = 0; i < SimulationRunner.SpeedCount && i < icons.Length; i++)
            {
                int index = i;
                float dayMin = Simulation.Core.TimeModel.RealSecondsPerDay(i) / 60f;
                string key = i <= 3 ? $"Taste {i}" : "Taste 3 nochmal";
                string tip = i == 0 ? "Pause (Leertaste)"
                    : i == 1 ? $"Echtzeit: Fahrzeuge in echter Geschwindigkeit, 1 Spieltag ≈ {dayMin / 60f:0.#} h ({key})"
                    : $"Tempo {i}: 1 Spieltag ≈ {dayMin:0.#} min ({key})"; // Zeitmodell
                var b = Btn(null, () => { if (index == 0) runner.TogglePause(); else runner.SetSpeed(index); },
                    () => runner.SpeedIndex == index, tip, null, "cb-seg-btn");
                b.AddToClassList("cb-seg-btn--icon");
                b.Add(Icon(icons[i]));
                speed.Add(b);
            }
            topBar.Add(speed);
            topBar.Add(Container("cb-spacer"));

            // Einwohner
            var pop = Stat(HudIconKind.People, "cb-icon--action", "Einwohner", out var popValue, out _);
            slowRefresh.Add(() => popValue.text = BuildLabels.Number(runner.World.Citizens.Stats.Population));
            Tip(pop, () =>
            {
                var s = runner.World.Citizens.Stats;
                return $"Einwohner ohne Einpendler · {BuildLabels.Number(s.Households)} Haushalte · " +
                       $"{s.Children} Kinder, {s.Students} Schüler, {s.Adults} Erwachsene, {s.Seniors} Senioren";
            });
            topBar.Add(pop);

            // Arbeitslose
            var jobs = Stat(HudIconKind.Briefcase, "cb-icon--accent", "Arbeitslos", out var jobValue, out var jobExtra);
            slowRefresh.Add(() =>
            {
                var s = runner.World.Citizens.Stats;
                jobValue.text = BuildLabels.Number(s.Unemployed);
                jobExtra.text = s.WorkingAge > 0 ? $"{s.UnemploymentRate * 100f:0} %" : "";
            });
            Tip(jobs, () =>
            {
                var s = runner.World.Citizens.Stats;
                return $"Arbeitslose im Erwerbsalter ({BuildLabels.Number(s.WorkingAge)}) · " +
                       $"Auspendler {BuildLabels.Number(s.CommutersOut)} · Einpendler {BuildLabels.Number(s.CommutersIn)}";
            });
            topBar.Add(jobs);
            topBar.Add(DemandChip()); // Setzen statt Zonen: Nachfrage W/L/B/I mit „warum“ (GameHud.Demand.cs)

            // Stadtkasse: Kontostand + Saldo der letzten Woche, Klick öffnet das Fenster (M)
            var cash = Stat(HudIconKind.Coins, "cb-icon--accent", "Stadtkasse", out var cashValue, out var cashExtra);
            slowRefresh.Add(() =>
            {
                var e = runner.World.Economy;
                if (e == null) { cashValue.text = "–"; cashExtra.text = ""; return; }
                cashValue.text = BuildLabels.Money(e.Balance);
                var last = e.LastWeek;
                cashExtra.text = last == null ? "" : (last.Net >= 0 ? "+" : "−") + BuildLabels.Money(Math.Abs(last.Net)) + "/Wo";
                cashValue.EnableInClassList("cb-stat__value--warn", e.IsOverdrawn || e.ConstructionFrozen);
            });
            Tip(cash, () =>
            {
                var e = runner.World.Economy;
                if (e == null) return "Stadtkasse";
                string state = e.ConstructionFrozen ? "Baustopp – zu lange im Minus" : e.IsOverdrawn ? "Konto überzogen" :
                    e.UnlimitedFunds ? "Sandbox: unbegrenztes Geld" : "Klick: Stadtkasse (M)";
                return $"{state} · Prognose heute {(e.EstimateDay().Net >= 0 ? "+" : "−")}{BuildLabels.Money(Math.Abs(e.EstimateDay().Net))}";
            });
            cash.RegisterCallback<ClickEvent>(_ => { var bp = tool.GetComponent<CityBuilder.Game.Economy.BudgetPanel>(); if (bp != null) bp.Visible = !bp.Visible; });
            topBar.Add(cash);

            // Versorgungswarnung – nur sichtbar, wenn ein Netz unterdeckt ist
            topBar.Add(SupplyChip(UtilityKind.Power, HudIconKind.Power, UtilityOverlayView.Overlay.Power));
            topBar.Add(SupplyChip(UtilityKind.Water, HudIconKind.Water, UtilityOverlayView.Overlay.Water));
            topBar.Add(SupplyChip(UtilityKind.Sewage, HudIconKind.Pipe, UtilityOverlayView.Overlay.Sewage)); // G3 erweitert
            topBar.Add(GatewayChip()); // Außenanbindung: „Stadt an die Autobahn anschließen“

            // Info-Modus (C)
            // UX: Info = Stadtübersicht ein/aus (wechselt ins Auswählen); Auswahl selbst geht immer per Linksklick
            var inspectorPanel = tool.GetComponent<CityBuilder.Game.Inspect.InspectorPanel>();
            var info = Btn(null, tool.ToggleCityInfo,
                () => tool.IsSelectMode && inspectorPanel != null && inspectorPanel.ShowOverview,
                "Stadtübersicht (C) · Gebäude, Bürger, Fahrzeuge, Dienste und Versorger wählt man im Auswählen-Modus per Linksklick", null, "cb-icon-btn");
            info.Add(Icon(HudIconKind.Info));
            topBar.Add(info);

            // Hilfe & Tasten (F1) – nur mit den neuen Fenstern
            // Weltkarte (F4) – nur mit den neuen Fenstern
            var mapBtn = Btn(null, () => worldMap?.Toggle(), () => worldMap != null && worldMap.IsOpen, "Weltkarte (F4): Länder, Klima, Rohstoffe", null, "cb-icon-btn");
            mapBtn.Add(Icon(HudIconKind.Globe));
            topBar.Add(mapBtn);

            // --- G5 Wirtschaft (F5) – nur mit den neuen Fenstern
            var goodsBtn = Btn(null, () => goodsWindow?.Toggle(), () => goodsWindow != null && goodsWindow.IsOpen,
                "Wirtschaft (F5): Güter, Preise, Import/Export, Firmen, Lieferungen", null, "cb-icon-btn");
            goodsBtn.Add(Icon(HudIconKind.Briefcase));
            topBar.Add(goodsBtn);
            // --- Ende G5

            // --- Etappe 8 Handel (F11) – nur mit den neuen Fenstern und Weltkarte
            var tradeBtn = Btn(null, () => tradeWindow?.Toggle(), () => tradeWindow != null && tradeWindow.IsOpen,
                "Handel (F11): Länder, Weltpreise, Angebote, Verträge, Zölle", null, "cb-icon-btn");
            tradeBtn.Add(Icon(HudIconKind.Coins));
            topBar.Add(tradeBtn);
            // --- Ende Etappe 8

            var helpBtn = Btn(null, () => help?.Toggle(), () => help != null && help.IsOpen, "Hilfe & Tasten (F1)", null, "cb-icon-btn");
            helpBtn.Add(Icon(HudIconKind.Help));
            topBar.Add(helpBtn);
            return topBar;
        }

        VisualElement Stat(HudIconKind icon, string iconClass, string label, out Label value, out Label extra)
        {
            var chip = El("cb-stat");
            chip.Add(Icon(icon, iconClass));
            var col = Container("cb-col");
            col.Add(Text(label, "cb-stat__label"));
            var row = Container("cb-row");
            value = Text("0", "cb-stat__value");
            extra = Text("", "cb-stat__extra");
            row.Add(value);
            row.Add(extra);
            col.Add(row);
            chip.Add(col);
            return chip;
        }

        VisualElement SupplyChip(UtilityKind kind, HudIconKind icon, UtilityOverlayView.Overlay overlay)
        {
            var chip = El("cb-chip-warn", "cb-hidden");
            chip.Add(Icon(icon));
            var text = Text("", "cb-chip-warn__text");
            chip.Add(text);
            int unsuppliedShown = 0;
            OnClick(chip, () =>
            {
                if (utilityOverlay != null && utilityOverlay.Current != overlay) utilityOverlay.Toggle(overlay);
            });
            slowRefresh.Add(() =>
            {
                bool shortage = UtilityAlerts.HasShortage(runner.World.Utilities, kind, out float coverage, out int unsupplied);
                chip.EnableInClassList("cb-hidden", !shortage);
                text.text = $"{UtilityCatalog.KindName(kind)} {coverage * 100f:0} %";
                unsuppliedShown = unsupplied;
            });
            Tip(chip, () => $"{UtilityCatalog.KindName(kind)}netz unterdeckt · {unsuppliedShown} Gebäude ohne {UtilityCatalog.KindName(kind)} · " +
                            $"Erzeugung {BuildLabels.Number(runner.World.Utilities.TotalProduction(kind))} / Bedarf " +
                            $"{BuildLabels.Number(runner.World.Utilities.TotalDemand(kind))} {UtilityCatalog.Unit(kind)} · Klick: Info-Ansicht");
            return chip;
        }

        // --- Außenanbindungen (Gateways): Warnchip, solange Wohngebiete an keinem Anschluss hängen; Klick fährt zum Zubringer
        VisualElement GatewayChip()
        {
            var chip = El("cb-chip-warn", "cb-hidden");
            chip.Add(Icon(HudIconKind.Highway));
            var text = Text("", "cb-chip-warn__text");
            chip.Add(text);
            string hint = null;
            OnClick(chip, () =>
            {
                var gw = runner.World.Gateways;
                var cam = tool.Camera != null ? tool.Camera.GetComponent<CameraController>() : null;
                if (gw == null || cam == null || gw.FeederEnds.Count == 0) return;
                // nächstes Zubringer-Ende zur Kartenmitte anfahren
                var best = gw.FeederEnds[0];
                foreach (var p in gw.FeederEnds) if (p.Length() < best.Length()) best = p;
                cam.FocusOn(SimConvert.ToWorld(best, runner.World.Terrain.HeightAt(best)));
            });
            slowRefresh.Add(() =>
            {
                hint = runner.World.Gateways?.ConnectionHint;
                chip.EnableInClassList("cb-hidden", hint == null);
                text.text = "Nicht angeschlossen";
            });
            Tip(chip, () => (hint ?? "") + " · Klick: zum Zubringer");
            return chip;
        }
        // --- Ende Gateways

        static readonly string[] SeasonNames = { "Frühling", "Sommer", "Herbst", "Winter" };

        /// <summary>„ · Frühling, 12 °C“ aus <c>world.Climate</c> (Tageswerte).</summary>
        string ClimateText()
        {
            var climate = runner.World.Climate;
            if (climate == null) return "";
            var d = climate.Today;
            return $" · {SeasonNames[Mathf.Clamp((int)d.Season, 0, 3)]}, {d.Temperature:0} °C" + (d.Precipitation > 0.5f ? $", {d.Precipitation:0} mm" : "");
        }

        string ResourceName(string id)
        {
            var kind = runner.World.Resources?.Kind(id);
            return kind != null ? kind.DisplayName : id;
        }

        /// <summary>Untermenü der Rohstoff-Ansicht: alle 7 Raster direkt wählbar, sichtbar solange die Ansicht an ist.</summary>
        VisualElement BuildResourceFlyout()
        {
            resourceFlyout = El("cb-panel", "cb-flyout", "cb-hidden");
            resourceFlyout.Add(Text("Rohstoffe", "cb-overlay-name__label"));
            var seg = Container("cb-segment");
            foreach (var id in CityBuilder.Simulation.World.ResourceIds.All)
            {
                string rid = id;
                var b = Btn(ResourceShort(rid), () => envOverlay?.ShowResource(rid), () => envOverlay != null && envOverlay.CurrentResourceId == rid,
                    null, null, "cb-seg-btn");
                Tip(b, () => ResourceTip(rid));
                seg.Add(b);
            }
            resourceFlyout.Add(seg);
            resourceFlyout.Add(Btn("aus", () => envOverlay?.ShowResource(null), null, "Rohstoff-Ansicht aus", null, "cb-btn"));
            fastRefresh.Add(() => resourceFlyout.EnableInClassList("cb-hidden", envOverlay == null || envOverlay.Current != EnvironmentOverlayView.Overlay.Resource));
            return resourceFlyout;
        }

        static string ResourceShort(string id)
        {
            switch (id)
            {
                case "fertility": return "Boden";
                case "wood": return "Wald";
                case "ore": return "Erz";
                case "coal": return "Kohle";
                case "oil": return "Öl";
                case "sand": return "Sand";
                case "groundwater": return "Wasser";
                default: return id;
            }
        }

        string ResourceTip(string id)
        {
            var r = runner.World.Resources;
            var kind = r?.Kind(id);
            if (kind == null) return id;
            return $"{kind.DisplayName} · gesamt {BuildLabels.Number((float)r.TotalAmount(id))} {kind.Unit}" + (kind.Renewable ? " · erneuert sich" : " · endlich");
        }

        string DateText()
        {
            var c = runner.World.Clock;
            int day = c.DayOfYear % 30 + 1;
            int month = Mathf.Clamp(c.DayOfYear / 30, 0, Months.Length - 1);
            return $"{day}. {Months[month]}, Jahr {c.Year + 1} · {c.Hour:00}:{c.Minute:00}";
        }

        // ------------------------------------------------------------ Linke Ansichtsleiste

        VisualElement BuildRail()
        {
            rail = El("cb-panel", "cb-rail");
            RailButton(HudIconKind.Car, "Verkehr: Stau-Ansicht (V)", () => trafficOverlay?.Toggle(), () => trafficOverlay != null && trafficOverlay.Visible);
            RailButton(HudIconKind.Noise, "Lärm (N)", () => envOverlay?.Toggle(EnvironmentOverlayView.Overlay.Noise),
                () => envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.Noise);
            RailButton(HudIconKind.Air, "Abgas (P)", () => envOverlay?.Toggle(EnvironmentOverlayView.Overlay.AirPollution),
                () => envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.AirPollution);
            RailButton(HudIconKind.LandValue, "Bodenwert (F2) – rot niedrig, gelb mittel, grün hoch", () => envOverlay?.Toggle(EnvironmentOverlayView.Overlay.LandValue),
                () => envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.LandValue); // G6
            RailButton(HudIconKind.Resource, "Rohstoffe (F3 schaltet Boden → Wald → Erz → Kohle → Öl → Sand → Grundwasser → aus)",
                () => envOverlay?.CycleResource(), () => envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.Resource); // Welt
            RailButton(HudIconKind.Power, "Strom (O schaltet Strom, Wasser, aus)", () => utilityOverlay?.Toggle(UtilityOverlayView.Overlay.Power),
                () => utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Power);
            RailButton(HudIconKind.Water, "Wasser (O schaltet Strom, Wasser, aus)", () => utilityOverlay?.Toggle(UtilityOverlayView.Overlay.Water),
                () => utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Water);
            RailButton(HudIconKind.Service, "Dienste-Abdeckung (⇧4 schaltet Polizei → Feuerwehr → … → Müll → aus)", () => serviceOverlay?.Cycle(),
                () => serviceOverlay != null && serviceOverlay.Selected != null); // G4
            // --- G3/G4 erweitert: Abwasser (auch per O), Wasserqualität, Medien (auch per ⇧4) – bewusst ohne neue Tasten
            RailButton(HudIconKind.Pipe, "Abwasser (O schaltet Strom, Wasser, Abwasser, aus)", () => utilityOverlay?.Toggle(UtilityOverlayView.Overlay.Sewage),
                () => utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Sewage);
            RailButton(HudIconKind.Water, "Wasserqualität – braun = durch Abwasser verschmutzt (fließt flussabwärts)",
                () => envOverlay?.Toggle(EnvironmentOverlayView.Overlay.WaterQuality),
                () => envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.WaterQuality);
            RailButton(HudIconKind.Info, "Medien-Reichweite (Zeitung, Radio, TV)",
                () => serviceOverlay?.Toggle(CityBuilder.Simulation.Services.ServiceKinds.Media),
                () => serviceOverlay != null && serviceOverlay.Selected == CityBuilder.Simulation.Services.ServiceKinds.Media);
            // --- Ende
            rail.Add(Container("cb-rail-sep"));
            RailButton(HudIconKind.Underground, "Unterirdisch (X) – Boden durchsichtig, Tunnel und Rohre sichtbar",
                () => tool.ToggleUndergroundView(), () => tool.UndergroundView);
            return rail;
        }

        void RailButton(HudIconKind icon, string tip, Action onClick, Func<bool> isOn)
        {
            var b = Btn(null, onClick, isOn, tip, null, "cb-rail-btn");
            b.Add(Icon(icon, "cb-icon--l"));
            rail.Add(b);
        }

        VisualElement BuildOverlayName()
        {
            overlayName = El("cb-overlay-name", "cb-hidden");
            overlayName.Add(Text("Ansicht", "cb-overlay-name__label"));
            overlayNameValue = Text("", "cb-overlay-name__value", "cb-heading");
            overlayName.Add(overlayNameValue);
            fastRefresh.Add(() =>
            {
                string s = null;
                void Add(bool on, string name) { if (on) s = s == null ? name : s + " + " + name; }
                Add(trafficOverlay != null && trafficOverlay.Visible, "Verkehr");
                Add(envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.Noise, "Lärm");
                Add(envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.AirPollution, "Abgas");
                Add(envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.LandValue, "Bodenwert");
                if (envOverlay != null && envOverlay.CurrentResourceId != null) Add(true, ResourceName(envOverlay.CurrentResourceId));
                Add(utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Power, "Strom");
                Add(utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Water, "Wasser");
                Add(utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Sewage, "Abwasser"); // G3 erw.
                Add(envOverlay != null && envOverlay.Current == EnvironmentOverlayView.Overlay.WaterQuality, "Wasserqualität"); // G3 erw.
                Add(serviceOverlay != null && serviceOverlay.Current != null,
                    serviceOverlay != null && serviceOverlay.Current != null ? "Abdeckung " + CityBuilder.Simulation.Services.ServiceKinds.DisplayName(serviceOverlay.Current) : null); // G4
                Add(tool.UndergroundView, "Unterirdisch");
                overlayName.EnableInClassList("cb-hidden", s == null);
                if (s != null) overlayNameValue.text = s;
            });
            return overlayName;
        }

        // ------------------------------------------------------------ Meldungen

        void OnMessage(HudMessage m)
        {
            if (!Ready) return;
            if (!string.IsNullOrEmpty(m.Key))
                foreach (var t in toasts.ToArray())
                    if (t.Key == m.Key) RemoveToast(t, immediate: true);

            var el = El("cb-toast", "cb-toast--enter");
            if (m.Kind == HudMessageKind.Warning) el.AddToClassList("cb-toast--warning");
            var badge = Container("cb-toast__badge");
            badge.Add(Icon(m.Icon));
            el.Add(badge);
            var body = Container("cb-toast__body");
            body.Add(Text(m.Title, "cb-toast__title", "cb-heading"));
            if (!string.IsNullOrEmpty(m.Text)) body.Add(Text(m.Text, "cb-toast__text"));
            el.Add(body);

            var toast = new Toast
            {
                Element = el,
                Key = m.Key,
                Until = Time.unscaledTime + (m.Kind == HudMessageKind.Warning ? 10f : 6f),
            };
            OnClick(el, () => RemoveToast(toast, immediate: false));
            Tip(el, "Klicken zum Schließen");
            toastStack.Insert(0, el);
            toasts.Add(toast);
            el.schedule.Execute(() => el.RemoveFromClassList("cb-toast--enter")).StartingIn(30);

            int alive = 0;
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (!toasts[i].Leaving && ++alive > MaxToasts) RemoveToast(toasts[i], immediate: false);
        }

        void UpdateToasts()
        {
            foreach (var t in toasts.ToArray())
                if (!t.Leaving && Time.unscaledTime >= t.Until) RemoveToast(t, immediate: false);
        }

        void RemoveToast(Toast t, bool immediate)
        {
            if (immediate)
            {
                t.Element.RemoveFromHierarchy();
                toasts.Remove(t);
                return;
            }
            if (t.Leaving) return;
            t.Leaving = true;
            t.Element.AddToClassList("cb-toast--leave");
            t.Element.schedule.Execute(() =>
            {
                t.Element.RemoveFromHierarchy();
                toasts.Remove(t);
            }).StartingIn(260);
        }
    }
}
