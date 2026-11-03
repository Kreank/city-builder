using System;
using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Utilities;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Bauleiste unten (ersetzt die IMGUI-<see cref="RoadToolbar"/>): Kategorien, darüber die Bauteile der Kategorie
    /// als Kacheln plus Werkzeugzeile (Modus, Höhe, Einrasten, Drehen …), ganz oben der Werkzeug-Hinweis aus
    /// <see cref="RoadTool.Guide"/>. Die Kategorie folgt dem Werkzeug (Tasten R/K/U/I/B/T/Z/L/J …).
    /// </summary>
    public sealed partial class GameHud
    {
        enum Category { Roads, Highway, Zones, Utilities, Terrain, Traffic, Bulldoze }

        static readonly Color RoadSwatch = BuildLabels.Hex(0x8D8F96);
        static readonly Color HighwaySwatch = BuildLabels.Hex(0x6E7486);
        static readonly Color TemplateSwatch = BuildLabels.Hex(0x5B6478);
        static readonly Color PowerSwatch = BuildLabels.Hex(0xF2A93B);
        static readonly Color WaterSwatch = BuildLabels.Hex(0x4A90D9);
        static readonly Color TerrainSwatch = BuildLabels.Hex(0xA07850);
        static readonly Color TrafficSwatch = BuildLabels.Hex(0x2BB3A3);
        static readonly Color DangerSwatch = BuildLabels.Hex(0xF08A4B);

        VisualElement buildBar, hint, buildPanel, panelBody;
        Label hintText, panelTitle, panelHint;
        HudIcon hintIcon;
        Category? currentCategory;
        Category? shownCategory;

        /// <summary>Aktualisierungen nur für den Inhalt der gezeigten Kategorie (werden beim Wechsel ersetzt).</summary>
        readonly List<Action> panelFastRefresh = new List<Action>();
        readonly List<Action> panelSlowRefresh = new List<Action>();

        VisualElement BuildBuildBar()
        {
            buildBar = Container("cb-buildbar");

            hint = El("cb-hint", "cb-hidden");
            hintIcon = Icon(HudIconKind.Info);
            hint.Add(hintIcon);
            hintText = Text("", "cb-hint__text");
            hint.Add(hintText);
            buildBar.Add(hint);

            buildPanel = El("cb-panel", "cb-build-panel");
            var header = Container("cb-build-panel__header");
            panelTitle = Text("", "cb-build-panel__title", "cb-heading");
            header.Add(panelTitle);
            header.Add(Container("cb-spacer"));
            panelHint = Text("", "cb-build-panel__hint");
            header.Add(panelHint);
            buildPanel.Add(header);
            panelBody = Container("cb-col");
            buildPanel.Add(panelBody);
            buildBar.Add(buildPanel);

            var cats = El("cb-panel", "cb-catbar");
            CategoryButton(cats, Category.Roads, HudIconKind.Road, "Straßen", "Stadtstraßen: Gerade/Kurve (R), Kreisverkehr (K), Ausbauen (U), Richtung (I) · Tab Typ wechseln");
            CategoryButton(cats, Category.Highway, HudIconKind.Highway, "Autobahn", "Autobahnen, Auf-/Abfahrten und Knoten-Vorlagen · H Doppelbau · , . drehen");
            CategoryButton(cats, Category.Zones, HudIconKind.Zones, "Zonen", "Zonen malen (Z) · [ ] Pinselgröße");
            CategoryButton(cats, Category.Utilities, HudIconKind.Power, "Versorgung", "Strom und Wasser (J): Kraftwerke, Pumpen, Leitungen, Rohre");
            CategoryButton(cats, Category.Terrain, HudIconKind.Terrain, "Gelände", "Gelände formen (L) · [ ] Radius");
            CategoryButton(cats, Category.Traffic, HudIconKind.TrafficLight, "Verkehr", "Verkehrsregeln (T), Stau-Ansicht (V), Verkehrsaufkommen");
            var bulldoze = CategoryButton(cats, Category.Bulldoze, HudIconKind.Bulldoze, "Abriss", "Abriss (B): Straßen, Leitungen und Bauwerke");
            bulldoze.AddToClassList("cb-cat--danger");
            buildBar.Add(cats);
            return buildBar;
        }

        VisualElement CategoryButton(VisualElement parent, Category c, HudIconKind icon, string label, string tip)
        {
            var b = Btn(null, () => SelectCategory(c), () => currentCategory == c, tip, null, "cb-cat");
            b.Add(Icon(icon, "cb-icon--l"));
            b.Add(Text(label, "cb-cat__label"));
            parent.Add(b);
            return b;
        }

        // ------------------------------------------------------------ Kategorie ↔ Werkzeug

        Category? CategoryFromMode()
        {
            switch (tool.CurrentMode)
            {
                case RoadTool.Mode.Traffic: return Category.Traffic;
                case RoadTool.Mode.Template: return Category.Highway;
                case RoadTool.Mode.Zone: return Category.Zones;
                case RoadTool.Mode.Terraform: return Category.Terrain;
                case RoadTool.Mode.Utility:
                    return utilityTool != null && utilityTool.Current == UtilityTool.UtilityAction.Remove ? Category.Bulldoze : Category.Utilities;
                case RoadTool.Mode.Bulldoze: return Category.Bulldoze;
                case RoadTool.Mode.Inspect: return null;
                default: return tool.SelectedType.Category == RoadCategory.City ? Category.Roads : Category.Highway;
            }
        }

        void SelectCategory(Category c)
        {
            var mode = tool.CurrentMode;
            switch (c)
            {
                case Category.Roads:
                    EnsureRoadType(highway: false);
                    if (!IsRoadMode(tool.CurrentMode, highway: false)) tool.SetMode(RoadTool.Mode.Straight);
                    break;
                case Category.Highway:
                    EnsureRoadType(highway: true);
                    if (!IsRoadMode(tool.CurrentMode, highway: true)) tool.SetMode(RoadTool.Mode.Straight);
                    break;
                case Category.Zones: tool.SelectZone(tool.SelectedZone); break;
                case Category.Utilities:
                    if (utilityTool != null)
                        utilityTool.Select(utilityTool.Current == UtilityTool.UtilityAction.Remove ? UtilityTool.UtilityAction.Facility : utilityTool.Current);
                    else tool.SetMode(RoadTool.Mode.Utility);
                    break;
                case Category.Terrain:
                    if (terraform != null) terraform.Activate(terraform.Mode);
                    else tool.SetMode(RoadTool.Mode.Terraform);
                    break;
                case Category.Traffic: if (mode != RoadTool.Mode.Traffic) tool.SetMode(RoadTool.Mode.Traffic); break;
                case Category.Bulldoze: if (mode != RoadTool.Mode.Bulldoze) tool.SetMode(RoadTool.Mode.Bulldoze); break;
            }
        }

        static bool IsRoadMode(RoadTool.Mode m, bool highway) =>
            m == RoadTool.Mode.Straight || m == RoadTool.Mode.Curve || m == RoadTool.Mode.Upgrade || m == RoadTool.Mode.Reverse ||
            (!highway && m == RoadTool.Mode.Roundabout) || (highway && m == RoadTool.Mode.Template);

        /// <summary>Gewählten Straßentyp in die Kategorie holen (Stadt bzw. Autobahn/Rampe).</summary>
        void EnsureRoadType(bool highway)
        {
            bool isCity = tool.SelectedType.Category == RoadCategory.City;
            if (isCity != highway) return;
            var first = RoadCatalog.All.FirstOrDefault(t => (t.Category == RoadCategory.City) != highway);
            if (first != null) tool.SelectType(first);
        }

        void UpdateBuildBar()
        {
            currentCategory = CategoryFromMode();
            buildPanel.EnableInClassList("cb-hidden", currentCategory == null);
            if (currentCategory != null && currentCategory != shownCategory) ShowCategory(currentCategory.Value);
            UpdateHint();
        }

        // ------------------------------------------------------------ Hinweiszeile (Kosten, Länge, Fehler)

        void UpdateHint()
        {
            string text = null;
            bool error = false;
            var g = tool.Guide;
            if (g.Active && tool.HasCursor)
            {
                if (!string.IsNullOrEmpty(g.Error)) { text = g.Error; error = true; }
                else
                {
                    var parts = new List<string>(4);
                    if (!string.IsNullOrEmpty(g.Info)) parts.Add(g.Info);
                    if (!g.LabelOnly && g.Length > 0.5f) parts.Add($"Länge {BuildLabels.Number(g.Length)} m");
                    if (g.Cost > 0.5f) parts.Add($"Kosten {BuildLabels.Money(g.Cost)}");
                    if (parts.Count > 0) text = string.Join("  ·  ", parts);
                }
            }
            else if (tool.CurrentMode == RoadTool.Mode.Terraform && terraform != null && terraform.HitLocked)
            {
                text = "Unter Straßen und Gebäuden ist das Gelände gesperrt";
                error = true;
            }

            hint.EnableInClassList("cb-hidden", text == null);
            if (text == null) return;
            hint.EnableInClassList("cb-hint--error", error);
            hintIcon.Kind = error ? HudIconKind.Warning : HudIconKind.Info;
            hintText.text = text;
        }

        // ------------------------------------------------------------ Inhalt je Kategorie

        void ShowCategory(Category c)
        {
            shownCategory = c;
            panelFastRefresh.Clear();
            panelSlowRefresh.Clear();
            panelBody.Clear();
            switch (c)
            {
                case Category.Roads: BuildRoadPanel(highway: false); break;
                case Category.Highway: BuildRoadPanel(highway: true); break;
                case Category.Zones: BuildZonePanel(); break;
                case Category.Utilities: BuildUtilityPanel(); break;
                case Category.Terrain: BuildTerrainPanel(); break;
                case Category.Traffic: BuildTrafficPanel(); break;
                case Category.Bulldoze: BuildBulldozePanel(); break;
            }
            refreshNow = true;
        }

        void Header(string title, string hintLine)
        {
            panelTitle.text = title;
            panelHint.text = hintLine;
        }

        VisualElement PBtn(string label, Action onClick, Func<bool> isOn = null, string tip = null, string cls = "cb-btn") =>
            Btn(label, onClick, isOn, tip, null, cls, panelFastRefresh);

        Label PLabel(string text, string cls = "cb-option-label") => Text(text, cls);

        /// <summary>Wert, der sich laufend ändert (5×/s aktualisiert).</summary>
        Label PValue(Func<string> value, string cls = "cb-option-value")
        {
            var l = Text("", cls);
            panelSlowRefresh.Add(() => l.text = value());
            return l;
        }

        VisualElement Tile(string name, string sub, Color swatch, HudIconKind icon, Action onClick, Func<bool> isOn, string tip)
        {
            var t = El("cb-tile");
            var sw = Container("cb-tile__swatch");
            sw.style.backgroundColor = swatch;
            sw.Add(Icon(icon, "cb-icon--l"));
            t.Add(sw);
            t.Add(Text(name, "cb-tile__name"));
            if (sub != null) t.Add(Text(sub, "cb-tile__sub"));
            OnClick(t, onClick);
            panelFastRefresh.Add(() => t.EnableInClassList("cb-on", isOn()));
            Tip(t, tip);
            return t;
        }

        VisualElement Info(Func<string> text)
        {
            var l = Text("", "cb-info");
            panelSlowRefresh.Add(() => l.text = text());
            return l;
        }

        // ------------------------------------------------------------ Straßen / Autobahn

        void BuildRoadPanel(bool highway)
        {
            Header(highway ? "Autobahn & Knoten" : "Straßen",
                highway ? "Klick Start, dann Ende · H Doppelbau · Vorlage: , und . drehen"
                        : "Klick Start, dann Ende · R Gerade/Kurve · Tab nächster Typ · RMB/Esc abbrechen");

            var tools = Container("cb-tool-row");
            var seg = Container("cb-segment");
            seg.Add(ModeButton(RoadTool.Mode.Straight, "Gerade", "Gerade bauen (R wechselt Gerade/Kurve)", highway));
            seg.Add(ModeButton(RoadTool.Mode.Curve, "Kurve", "Kurve: Start, Kontrollpunkt, Ende (R)", highway));
            if (!highway) seg.Add(ModeButton(RoadTool.Mode.Roundabout, "Kreisverkehr", "Kreisverkehr (K): Mittelpunkt, dann Radius · nutzt eine Einbahn-Stadtstraße", false));
            seg.Add(ModeButton(RoadTool.Mode.Upgrade, "Ausbauen", "Ausbauen (U): Straße anklicken, sie wird zum gewählten Typ", highway));
            seg.Add(ModeButton(RoadTool.Mode.Reverse, "Richtung", "Richtung umdrehen (I): Straße anklicken", highway));
            tools.Add(seg);
            if (highway)
                tools.Add(PBtn("Doppelbau", tool.ToggleDualCarriageway, () => tool.DualCarriageway,
                    "Doppelbau (H): beide Richtungsfahrbahnen auf einmal bauen"));
            tools.Add(Container("cb-spacer"));
            HeightControls(tools);
            tools.Add(PBtn("Einrasten", tool.ToggleSnap, () => tool.SnapEnabled,
                $"Einrasten (G): {RoadTool.AngleSnapStep:0}° / {RoadTool.LengthSnapStep:0} m · Strg halten = frei"));
            tools.Add(PBtn("Unterirdisch", tool.ToggleUndergroundView, () => tool.UndergroundView, "Unterirdisch-Ansicht (X)"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var type in RoadCatalog.All)
            {
                if ((type.Category == RoadCategory.City) == highway) continue;
                var t = type;
                tiles.Add(Tile(BuildLabels.RoadName(t), $"{t.CostPerMeter:0} €/m", highway ? HighwaySwatch : RoadSwatch,
                    highway ? HudIconKind.Highway : HudIconKind.Road,
                    () => tool.SelectType(t),
                    () => tool.SelectedType == t && tool.CurrentMode != RoadTool.Mode.Template,
                    BuildLabels.DescribeRoad(t)));
            }
            if (highway)
            {
                tiles.Add(Container("cb-tile-sep"));
                foreach (RoadTemplateKind kind in Enum.GetValues(typeof(RoadTemplateKind)))
                {
                    var k = kind;
                    tiles.Add(Tile(RoadTemplates.DisplayName(k), "Vorlage", TemplateSwatch, HudIconKind.Highway,
                        () => tool.SelectTemplate(k),
                        () => tool.CurrentMode == RoadTool.Mode.Template && tool.TemplateKind == k,
                        $"{RoadTemplates.DisplayName(k)}: platzieren und an den offenen Enden eigene Autobahnen anschließen · , und . drehen"));
                }
            }
            panelBody.Add(tiles);

            if (highway)
            {
                // Drehung nur im Vorlagen-Modus
                var rot = Container("cb-tool-row");
                rot.Add(PLabel("Drehung der Vorlage"));
                rot.Add(PBtn("−15°", () => tool.RotateTemplate(-15f), null, "Vorlage drehen (,)"));
                rot.Add(PValue(() => $"{tool.TemplateRotation:0}°"));
                rot.Add(PBtn("+15°", () => tool.RotateTemplate(15f), null, "Vorlage drehen (.)"));
                rot.style.marginTop = 6f;
                panelFastRefresh.Add(() => rot.EnableInClassList("cb-hidden", tool.CurrentMode != RoadTool.Mode.Template));
                panelBody.Add(rot);
            }

            panelBody.Add(Info(() =>
            {
                if (tool.CurrentMode == RoadTool.Mode.Template)
                    return $"Drehung {tool.TemplateRotation:0}° · Autobahn-Typ: " +
                           $"{(tool.SelectedType.IsHighway ? tool.SelectedType.DisplayName : RoadCatalog.Highway2.DisplayName)}";
                var t = tool.CurrentMode == RoadTool.Mode.Roundabout ? tool.RoundaboutType : tool.SelectedType;
                return BuildLabels.DescribeRoad(t) + " · Bild auf: Brücke · Bild ab: Trog/Tunnel (ab −7 m) · Höhe 0 = automatisch trassieren";
            }));
        }

        VisualElement ModeButton(RoadTool.Mode mode, string label, string tip, bool highway) =>
            PBtn(label, () =>
            {
                EnsureRoadType(highway);
                tool.SetMode(mode);
            }, () => tool.CurrentMode == mode, tip, "cb-seg-btn");

        void HeightControls(VisualElement row)
        {
            const string tip = "Bauhöhe über Gelände (Bild auf/ab) · 0 = automatisch trassieren (Tunnel/Brücke/einebnen) · " +
                               "unter 0 Trog, ab −7 m Tunnel";
            row.Add(PLabel("Höhe"));
            row.Add(PBtn("−", () => tool.ChangeHeight(-RoadTool.HeightStep), null, tip, "cb-btn"));
            var v = PValue(() => tool.AutoTrace ? "auto" : $"{tool.BuildHeight:+0;−0} m");
            Tip(v, tip);
            v.pickingMode = PickingMode.Position;
            row.Add(v);
            row.Add(PBtn("+", () => tool.ChangeHeight(RoadTool.HeightStep), null, tip, "cb-btn"));
        }

        // ------------------------------------------------------------ Zonen

        void BuildZonePanel()
        {
            Header("Zonen", "LMB malen · RMB löschen · [ ] Pinsel");
            var city = runner.World.City;

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Pinsel"));
            tools.Add(PBtn("−", () => tool.SetBrush(tool.BrushRadius - 4f), null, "Pinsel kleiner ([)"));
            tools.Add(PValue(() => $"{tool.BrushRadius:0} m"));
            tools.Add(PBtn("+", () => tool.SetBrush(tool.BrushRadius + 4f), null, "Pinsel größer (])"));
            var presets = Container("cb-segment");
            foreach (var r in new[] { 8f, 16f, 32f })
            {
                float radius = r;
                presets.Add(PBtn($"{r:0} m", () => tool.SetBrush(radius), () => Mathf.Approximately(tool.BrushRadius, radius), null, "cb-seg-btn"));
            }
            tools.Add(presets);
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var zone in Zones.All)
            {
                var z = zone;
                var tile = Tile(BuildLabels.ZoneName(z), null, BuildLabels.ZoneColor(z), HudIconKind.Zones,
                    () => tool.SelectZone(z),
                    () => tool.CurrentMode == RoadTool.Mode.Zone && tool.SelectedZone == z,
                    () => $"{Zones.DisplayName(z)} · Nachfrage {Mathf.Clamp(city.Demand[z], 0f, 100f):0} %");
                var bar = Container("cb-bar");
                var fill = Container("cb-bar__fill");
                fill.style.backgroundColor = BuildLabels.ZoneColor(z);
                bar.Add(fill);
                tile.Add(bar);
                panelSlowRefresh.Add(() => fill.style.width = Length.Percent(Mathf.Clamp(city.Demand[z], 0f, 100f)));
                tiles.Add(tile);
            }
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(Tile("Entzonen", null, BuildLabels.ZoneColor(ZoneType.None), HudIconKind.Bulldoze,
                () => tool.SelectZone(ZoneType.None),
                () => tool.CurrentMode == RoadTool.Mode.Zone && tool.SelectedZone == ZoneType.None,
                "Zonen entfernen (auch RMB in jeder Zone)"));
            panelBody.Add(tiles);
            panelBody.Add(Info(() => "Balken in den Kacheln = Nachfrage · Zonen entstehen 4 Reihen tief an ebenerdigen Stadtstraßen"));
        }

        VisualElement Tile(string name, string sub, Color swatch, HudIconKind icon, Action onClick, Func<bool> isOn, Func<string> tip)
        {
            var t = Tile(name, sub, swatch, icon, onClick, isOn, (string)null);
            Tip(t, tip);
            return t;
        }

        // ------------------------------------------------------------ Versorgung

        void BuildUtilityPanel()
        {
            Header("Versorgung", ", und . drehen · Leitungen in Kette, RMB beendet · Stadtstraßen leiten Strom und Wasser mit");
            if (utilityTool == null) { panelBody.Add(Info(() => "Versorgungs-Werkzeug fehlt.")); return; }
            var u = runner.World.Utilities;

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Drehung"));
            tools.Add(PBtn("−15°", () => utilityTool.Rotate(-15f), null, "Bauwerk drehen (,)"));
            tools.Add(PValue(() => $"{utilityTool.Rotation:0}°"));
            tools.Add(PBtn("+15°", () => utilityTool.Rotate(15f), null, "Bauwerk drehen (.)"));
            tools.Add(Container("cb-spacer"));
            if (utilityOverlay != null)
            {
                tools.Add(PBtn("Strom-Ansicht", () => utilityOverlay.Toggle(UtilityOverlayView.Overlay.Power),
                    () => utilityOverlay.Current == UtilityOverlayView.Overlay.Power, "Info-Ansicht Strom (O)"));
                tools.Add(PBtn("Wasser-Ansicht", () => utilityOverlay.Toggle(UtilityOverlayView.Overlay.Water),
                    () => utilityOverlay.Current == UtilityOverlayView.Overlay.Water, "Info-Ansicht Wasser (O)"));
            }
            tools.Add(PBtn("Unterirdisch", tool.ToggleUndergroundView, () => tool.UndergroundView, "Unterirdisch-Ansicht (X) – Rohre liegen unter dem Gelände"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var type in UtilityCatalog.All)
            {
                var t = type;
                string unit = UtilityCatalog.Unit(t.Kind);
                tiles.Add(Tile(BuildLabels.FacilityName(t), $"{BuildLabels.Number(t.Capacity)} {unit}",
                    t.Kind == UtilityKind.Power ? PowerSwatch : WaterSwatch,
                    t.Kind == UtilityKind.Power ? HudIconKind.Power : HudIconKind.Water,
                    () => utilityTool.SelectFacility(t),
                    () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.Facility && utilityTool.SelectedType == t,
                    $"{t.DisplayName} · Leistung {BuildLabels.Number(t.Capacity)} {unit} · Fläche {t.Width:0}×{t.Depth:0} m · " +
                    $"Baukosten {BuildLabels.Money(t.Cost)}, Unterhalt {BuildLabels.Money(t.UpkeepPerDay)}/Tag (nur Anzeige, noch keine Stadtkasse)" +
                    (t.NeedsWater ? " · muss am Wasser stehen" : "")));
            }
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(Tile("Stromleitung", $"{UtilityCatalog.LineCostPerMeter(UtilityKind.Power):0} €/m", PowerSwatch, HudIconKind.Power,
                () => utilityTool.Select(UtilityTool.UtilityAction.PowerLine),
                () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.PowerLine,
                $"Freie Stromleitung für Lücken/Überland · Masten neben der Straße, höchstens {UtilitySystem.MaxPoleSpan:0} m Abstand, belegen Platz"));
            tiles.Add(Tile("Wasserrohr", $"{UtilityCatalog.LineCostPerMeter(UtilityKind.Water):0} €/m", WaterSwatch, HudIconKind.Pipe,
                () => utilityTool.Select(UtilityTool.UtilityAction.WaterPipe),
                () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.WaterPipe,
                "Wasserrohr (liegt unterirdisch, schaltet die Unterirdisch-Ansicht ein)"));
            panelBody.Add(tiles);

            var info = Container("cb-info-row");
            info.Add(BalanceLabel(u, UtilityKind.Power));
            info.Add(BalanceLabel(u, UtilityKind.Water));
            var upkeep = Text("", "cb-muted");
            panelSlowRefresh.Add(() => upkeep.text = $"Unterhalt {BuildLabels.Money(u.TotalUpkeepPerDay)}/Tag (nur Anzeige)");
            info.Add(upkeep);
            panelBody.Add(info);
        }

        Label BalanceLabel(UtilitySystem u, UtilityKind kind)
        {
            var l = Text("", "cb-bold");
            panelSlowRefresh.Add(() =>
            {
                float prod = u.TotalProduction(kind), dem = u.TotalDemand(kind);
                bool bad = dem > prod + 0.001f;
                l.text = $"{UtilityCatalog.KindName(kind)} {BuildLabels.Number(prod)} / {BuildLabels.Number(dem)} {UtilityCatalog.Unit(kind)}";
                l.EnableInClassList("cb-text-warn", bad);
                l.EnableInClassList("cb-text-ok", !bad);
            });
            return l;
        }

        // ------------------------------------------------------------ Gelände

        void BuildTerrainPanel()
        {
            Header("Gelände", "LMB halten = anwenden · RMB umgekehrt · [ ] Radius · unter 0 m entsteht Wasser");
            if (terraform == null) { panelBody.Add(Info(() => "Gelände-Werkzeug fehlt.")); return; }

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Radius"));
            tools.Add(PBtn("−", () => terraform.SetRadius(terraform.Radius - 4f), null, "Radius kleiner ([)"));
            tools.Add(PValue(() => $"{terraform.Radius:0} m"));
            tools.Add(PBtn("+", () => terraform.SetRadius(terraform.Radius + 4f), null, "Radius größer (])"));
            var presets = Container("cb-segment");
            foreach (var r in new[] { 8f, 16f, 32f, 64f })
            {
                float radius = r;
                presets.Add(PBtn($"{r:0} m", () => terraform.SetRadius(radius), () => Mathf.Approximately(terraform.Radius, radius), null, "cb-seg-btn"));
            }
            tools.Add(presets);

            tools.Add(PLabel("Stärke"));
            var strength = Container("cb-segment");
            string[] names = { "sanft", "mittel", "stark" };
            for (int i = 0; i < Terrain.TerraformTool.Strengths.Length && i < names.Length; i++)
            {
                float s = Terrain.TerraformTool.Strengths[i];
                strength.Add(PBtn(names[i], () => terraform.SetStrength(s), () => Mathf.Approximately(terraform.Strength, s),
                    $"{s:0.#} m/s in der Pinselmitte", "cb-seg-btn"));
            }
            tools.Add(strength);

            tools.Add(PLabel("Weiche Kante"));
            var slider = new Slider(0f, 1f) { value = terraform.Softness };
            slider.AddToClassList("cb-slider");
            slider.RegisterValueChangedCallback(e => terraform.SetSoftness(e.newValue));
            panelSlowRefresh.Add(() => { if (!Mathf.Approximately(slider.value, terraform.Softness)) slider.SetValueWithoutNotify(terraform.Softness); });
            tools.Add(slider);
            tools.Add(PValue(() => $"{terraform.Softness * 100f:0} %"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            TerrainTile(tiles, TerraformMode.Raise, "Anheben", "Gelände anheben (RMB senkt ab)");
            TerrainTile(tiles, TerraformMode.Lower, "Absenken", "Gelände absenken (RMB hebt an) · unter 0 m entsteht Wasser");
            TerrainTile(tiles, TerraformMode.Smooth, "Glätten", "Gelände glätten (3×3-Mittel)");
            TerrainTile(tiles, TerraformMode.Level, "Einebnen", "Einebnen: Klick nimmt die Zielhöhe auf, dann halten");
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                string info = terraform.Mode == TerraformMode.Level
                    ? (terraform.HasTarget ? $"Zielhöhe {terraform.TargetHeight:0.0} m (Klick nimmt neue Höhe auf)" : "Klick nimmt die Zielhöhe auf")
                    : "LMB halten = anwenden" + (terraform.Mode == TerraformMode.Raise || terraform.Mode == TerraformMode.Lower ? " · RMB = umgekehrt" : "");
                return info + $"  ·  Erdmenge {BuildLabels.Number(terraform.StrokeVolume)} m³" +
                       (terraform.HitLocked ? "  ·  unter Straßen/Gebäuden gesperrt" : "");
            }));
        }

        void TerrainTile(VisualElement parent, TerraformMode mode, string name, string tip)
        {
            parent.Add(Tile(name, null, TerrainSwatch, HudIconKind.Terrain,
                () => terraform.Activate(mode),
                () => terraform.Active && terraform.Mode == mode, tip));
        }

        // ------------------------------------------------------------ Verkehr

        void BuildTrafficPanel()
        {
            Header("Verkehr", "Kreuzung oder Straße anklicken, Regeln im Fenster rechts setzen · RMB hebt auf");
            var traffic = runner.World.Traffic;

            var tools = Container("cb-tool-row");
            if (trafficOverlay != null)
                tools.Add(PBtn("Stau-Ansicht", trafficOverlay.Toggle, () => trafficOverlay.Visible, "Stau-Ansicht (V): grün frei, gelb zäh, rot Stau"));
            if (vehicles != null)
                tools.Add(PBtn("Fahrzeuge", () => vehicles.Visible = !vehicles.Visible, () => vehicles.Visible, "Fahrzeuge zeigen/ausblenden"));
            tools.Add(Container("cb-spacer"));
            tools.Add(PLabel("Verkehrsaufkommen"));
            var demand = Container("cb-segment");
            foreach (var (label, value) in new[] { ("Aus", 0f), ("Wenig", 0.4f), ("Normal", 1f), ("Viel", 2f), ("Extrem", 4f) })
            {
                float v = value;
                demand.Add(PBtn(label, () => traffic.Demand = v, () => Mathf.Approximately(traffic.Demand, v),
                    $"Faktor {v:0.#} auf Fahrten (Bürger und Durchgangsverkehr)", "cb-seg-btn"));
            }
            tools.Add(demand);
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            tiles.Add(Tile("Regeln bearbeiten", "Taste T", TrafficSwatch, HudIconKind.TrafficLight,
                () => tool.SetMode(RoadTool.Mode.Traffic),
                () => tool.CurrentMode == RoadTool.Mode.Traffic,
                "Ampeln, Vorfahrt/Stopp, rechts vor links, Abbiegeverbote, Sperrung, Fahrverbote, Tempo"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
                $"Außenanschlüsse (Kartenrand): {traffic.EndpointCount} · Fahrzeuge: {BuildLabels.Number(traffic.Stats.Active)} " +
                $"(Bürger {BuildLabels.Number(traffic.Stats.CitizenVehicles)}) · Ø {traffic.Stats.AverageSpeedKmh:0} km/h · {traffic.Stats.Waiting} warten"));
        }

        // ------------------------------------------------------------ Abriss

        void BuildBulldozePanel()
        {
            Header("Abriss", "Anklicken reißt ab · RMB/Esc abbrechen");
            var tiles = Container("cb-tiles");
            tiles.Add(Tile("Straßen", "Taste B", DangerSwatch, HudIconKind.Road,
                () => tool.SetMode(RoadTool.Mode.Bulldoze),
                () => tool.CurrentMode == RoadTool.Mode.Bulldoze,
                "Straßenabschnitt anklicken (auch Brücken/Tunnel)"));
            if (utilityTool != null)
                tiles.Add(Tile("Leitungen & Bauwerke", "Versorgung", DangerSwatch, HudIconKind.Power,
                    () => utilityTool.Select(UtilityTool.UtilityAction.Remove),
                    () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.Remove,
                    "Kraftwerk, Pumpe, Stromleitung oder Wasserrohr anklicken"));
            panelBody.Add(tiles);
            panelBody.Add(Info(() => "Gebäude in Zonen verschwinden über Entzonen (Kategorie Zonen) oder wenn ihre Straße wegfällt."));
        }
    }
}
