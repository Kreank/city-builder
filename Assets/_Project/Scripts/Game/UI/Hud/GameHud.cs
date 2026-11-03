using System;
using System.Collections.Generic;
using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Terrain;
using CityBuilder.Game.Traffic;
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
        /// <summary>Platzhalter: Die Simulation kennt noch keinen Stadtnamen.</summary>
        public const string CityName = "Neustadt";

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

        /// <summary>Wahr, solange die neue UI läuft (sonst greift das RoadTool auf die alte IMGUI-Leiste zurück).</summary>
        public bool Ready { get; private set; }

        /// <summary>Baut die UI auf. false = Theme/USS nicht gefunden → Aufrufer nimmt die alte Leiste.</summary>
        public bool Init(RoadTool roadTool)
        {
            tool = roadTool;
            runner = roadTool.Runner;
            envOverlay = FindAnyObjectByType<EnvironmentOverlayView>();
            trafficOverlay = GetComponent<TrafficOverlayView>();
            utilityOverlay = GetComponent<UtilityOverlayView>();
            vehicles = GetComponent<VehicleView>();
            terraform = GetComponent<TerraformTool>();
            utilityTool = GetComponent<UtilityTool>();

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
            root.Add(BuildBuildBar());
            toastStack = Container("cb-toasts");
            root.Add(toastStack);
            tooltip = Container("cb-tooltip", "cb-hidden");
            tooltipLabel = Text("");
            tooltip.Add(tooltipLabel);
            root.Add(tooltip);

            UiBlocker.RegisterToolkitRoot(root);
            HudMessages.Posted += OnMessage;
            Ready = true;
            RefreshAll();
            return true;
        }

        void OnDestroy()
        {
            HudMessages.Posted -= OnMessage;
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
            UpdateToasts();
            PlaceTooltip();
            UpdateLayoutInsets();
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

            // Toasts weichen IMGUI-Fenstern am rechten Rand aus (Verkehrs-/Versorgungsfenster, Inspektor)
            float right = UiBlocker.RightEdgeImguiWidth();
            toastStack.style.right = 16f + (right > 0f ? right / scale + 8f : 0f);
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
            names.Add(Text(CityName, "cb-city-name", "cb-heading"));
            var date = Text("", "cb-date");
            names.Add(date);
            brand.Add(names);
            topBar.Add(brand);
            slowRefresh.Add(() => date.text = DateText());

            // Spieltempo (Leertaste/1/2/3 bleiben im SimulationRunner)
            var speed = Container("cb-segment");
            var icons = new[] { HudIconKind.Pause, HudIconKind.Play, HudIconKind.Fast, HudIconKind.Faster };
            for (int i = 0; i < SimulationRunner.SpeedCount && i < icons.Length; i++)
            {
                int index = i;
                string tip = i == 0 ? "Pause (Leertaste)" : $"Tempo ×{SimulationRunner.MultiplierOf(i)} (Taste {i})";
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

            // Versorgungswarnung – nur sichtbar, wenn ein Netz unterdeckt ist
            topBar.Add(SupplyChip(UtilityKind.Power, HudIconKind.Power, UtilityOverlayView.Overlay.Power));
            topBar.Add(SupplyChip(UtilityKind.Water, HudIconKind.Water, UtilityOverlayView.Overlay.Water));

            // Info-Modus (C)
            var info = Btn(null, () => tool.SetMode(tool.CurrentMode == RoadTool.Mode.Inspect ? RoadTool.Mode.Straight : RoadTool.Mode.Inspect),
                () => tool.CurrentMode == RoadTool.Mode.Inspect,
                "Info-Modus (C): Gebäude, Bürger, Fahrzeuge und Versorger anklicken · Rechtsklick hebt auf", null, "cb-icon-btn");
            info.Add(Icon(HudIconKind.Info));
            topBar.Add(info);
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
            RailButton(HudIconKind.Power, "Strom (O schaltet Strom, Wasser, aus)", () => utilityOverlay?.Toggle(UtilityOverlayView.Overlay.Power),
                () => utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Power);
            RailButton(HudIconKind.Water, "Wasser (O schaltet Strom, Wasser, aus)", () => utilityOverlay?.Toggle(UtilityOverlayView.Overlay.Water),
                () => utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Water);
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
                Add(utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Power, "Strom");
                Add(utilityOverlay != null && utilityOverlay.Current == UtilityOverlayView.Overlay.Water, "Wasser");
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
