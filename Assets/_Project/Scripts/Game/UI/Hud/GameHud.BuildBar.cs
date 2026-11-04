using System;
using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Services;
using CityBuilder.Simulation.Services;
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
        enum Category { Roads, Highway, Zones, Utilities, Services, Terrain, Traffic, Bulldoze, Transit /* ÖPNV */, Houses /* Bauen (Setzen statt Zonen) */, Focus /* Produktion lenken */, Rail /* Bahn */ }

        static readonly Color RoadSwatch = BuildLabels.Hex(0x8D8F96);
        static readonly Color HighwaySwatch = BuildLabels.Hex(0x6E7486);
        static readonly Color TemplateSwatch = BuildLabels.Hex(0x5B6478);
        static readonly Color PowerSwatch = BuildLabels.Hex(0xF2A93B);
        static readonly Color WaterSwatch = BuildLabels.Hex(0x4A90D9);
        static readonly Color TerrainSwatch = BuildLabels.Hex(0xA07850);
        static readonly Color TrafficSwatch = BuildLabels.Hex(0x2BB3A3);
        static readonly Color DangerSwatch = BuildLabels.Hex(0xF08A4B);
        static readonly Color ServiceSwatch = BuildLabels.Hex(0x7A6FD6); // G4
        static readonly Color SewageSwatch = BuildLabels.Hex(0x8A6A3E); // G3 erweitert: Abwasser

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
            // UX: „Auswählen“ ganz links – Standard, nichts in der Maus; aktiv im Auswählen-Modus, Kategorien klappen zu
            var select = Btn(null, tool.ReturnToSelect, () => tool.IsSelectMode,
                "Auswählen: nichts bauen, Linksklick wählt Gebäude, Fahrzeug, Bürger, Dienst oder Versorger · Esc/Rechtsklick aus jedem Werkzeug",
                null, "cb-cat");
            select.Add(Icon(HudIconKind.Cursor, "cb-icon--l"));
            select.Add(Text("Auswählen", "cb-cat__label"));
            cats.Add(select);
            CategoryButton(cats, Category.Roads, HudIconKind.Road, "Straßen", "Stadtstraßen: Gerade/Kurve (R), Kreisverkehr (K), Ausbauen (U), Richtung (I) · Tab Typ wechseln");
            CategoryButton(cats, Category.Highway, HudIconKind.Highway, "Autobahn", "Autobahnen, Auf-/Abfahrten und Knoten-Vorlagen · H Doppelbau · , . drehen");
            // --- Setzen statt Zonen: „Bauen“ (6) mit Reitern Wohnen/Gewerbe/Industrie; „Lenken“ (Z) = Schwerpunkt-Pinsel;
            // „Zonen“ nur, solange ein alter Spielstand Zonen hat
            CategoryButton(cats, Category.Houses, HudIconKind.LandValue, "Bauen", "Bauen (6, Anno-Stil): Wohnhäuser, Läden, Büros und Hallen setzen – LMB halten und entlang der Straße ziehen setzt eine Reihe · Betriebe wählt die Simulation");
            CategoryButton(cats, Category.Focus, HudIconKind.Resource, "Lenken", "Produktion lenken (Z): Schwerpunkt-Gebiete malen – welche Industrie in Hallen einzieht, entscheidet die Simulation, du lenkst sie");
            var zonesButton = CategoryButton(cats, Category.Zones, HudIconKind.Zones, "Zonen", "Zonen (alter Spielstand): malen/entzonen · [ ] Pinselgröße · neue Städte setzen Gebäude (Bauen, 6)");
            fastRefresh.Add(() => zonesButton.EnableInClassList("cb-hidden", !HasOldZones()));
            // --- Ende Setzen statt Zonen
            CategoryButton(cats, Category.Utilities, HudIconKind.Power, "Versorgung", "Versorgung (J): Kraftwerke, Umspannwerke, Pumpen · Hochspannung, Ortsnetz (Freileitung/Erdkabel), Rohre, Kanäle selbst verlegen");
            CategoryButton(cats, Category.Services, HudIconKind.Service, "Dienste", "Dienste (4): Polizei, Feuerwehr, Gesundheit, Schulen, Bibliothek, Parks, Müll, Medien, Freizeit, Wahrzeichen, Lärmschutzwand · ⇧4 Abdeckungs-Ansicht");
            CategoryButton(cats, Category.Transit, HudIconKind.Bus, "ÖPNV", "ÖPNV (5): Haltestellen, Buslinien zeichnen, Busdepot · F6 Linienübersicht"); // ÖPNV
            CategoryButton(cats, Category.Rail, HudIconKind.Train, "Bahn", "Bahn (7): Gleise, Weichen, Personen- und Güterbahnhof an der Hauptstrecke · F7 Bahnübersicht"); // Bahn
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
                case RoadTool.Mode.Zone: return tool.SelectedFocus.HasValue ? Category.Focus : Category.Zones; // Produktion lenken
                case RoadTool.Mode.Terraform: return Category.Terrain;
                case RoadTool.Mode.Utility:
                    return utilityTool != null && utilityTool.Current == UtilityTool.UtilityAction.Remove ? Category.Bulldoze : Category.Utilities;
                case RoadTool.Mode.Service:
                    return serviceTool != null && serviceTool.Current == ServiceTool.ServiceAction.Remove ? Category.Bulldoze : Category.Services;
                case RoadTool.Mode.Transit: return TransitCategory(); // ÖPNV
                case RoadTool.Mode.House: return HouseCategory(); // Wohnhäuser
                case RoadTool.Mode.Rail: return RailCategory(); // Bahn
                case RoadTool.Mode.Bulldoze: return Category.Bulldoze;
                case RoadTool.Mode.Inspect: return null;
                default: return tool.SelectedType.Category == RoadCategory.City ? Category.Roads : Category.Highway;
            }
        }

        void SelectCategory(Category c)
        {
            // UX: aktive Kategorie erneut anklicken = zurück zum Auswählen
            if (currentCategory == c) { tool.ReturnToSelect(); return; }
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
                case Category.Services: // G4
                    if (serviceTool != null) serviceTool.Select(ServiceTool.ServiceAction.Place);
                    else tool.SetMode(RoadTool.Mode.Service);
                    break;
                case Category.Terrain:
                    if (terraform != null) terraform.Activate(terraform.Mode);
                    else tool.SetMode(RoadTool.Mode.Terraform);
                    break;
                case Category.Transit: SelectTransitCategory(); break; // ÖPNV
                case Category.Houses: SelectHouseCategory(); break; // Bauen
                case Category.Focus: tool.OpenFocusBrush(); break;   // Produktion lenken
                case Category.Rail: SelectRailCategory(); break; // Bahn
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
            else if (HouseHint(out var houseText, out var houseError)) { text = houseText; error = houseError; } // Wohnhäuser
            else if (tool.CurrentMode == RoadTool.Mode.Terraform && terraform != null && terraform.HitLocked)
            {
                text = "Unter Straßen und Gebäuden ist das Gelände gesperrt";
                error = true;
            }
            // UX: Rückweg zum Auswählen immer nennen; im Auswählen-Modus kurz erklären (Verschieben läuft → dessen Hinweis)
            var mover = tool.GetComponent<CityBuilder.Game.Inspect.FacilityMover>();
            string back = mover != null && mover.Moving ? "Verschieben: Klick setzt · , . drehen · Esc/Rechtsklick bricht ab"
                : tool.IsSelectMode ? null : tool.ReturnHint;
            if (back != null && !error) text = text == null ? back : text + "  ·  " + back;
            else if (back != null && text == null) text = back;

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
                case Category.Services: BuildServicePanel(); break;
                case Category.Terrain: BuildTerrainPanel(); break;
                case Category.Traffic: BuildTrafficPanel(); break;
                case Category.Bulldoze: BuildBulldozePanel(); break;
                case Category.Transit: BuildTransitPanel(); break; // ÖPNV
                case Category.Houses: BuildHousePanel(); break; // Bauen
                case Category.Focus: BuildFocusPanel(); break;  // Produktion lenken
                case Category.Rail: BuildRailPanel(); break; // Bahn
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
                    () => tool.CurrentMode == RoadTool.Mode.Zone && tool.SelectedZone == z && !tool.SelectedFocus.HasValue, // Produktion lenken
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
                () => tool.CurrentMode == RoadTool.Mode.Zone && tool.SelectedZone == ZoneType.None && !tool.SelectedFocus.HasValue,
                "Zonen entfernen (auch RMB in jeder Zone)"));
            panelBody.Add(tiles);
            panelBody.Add(Info(() => "Alter Spielstand: Zonen wachsen weiter · neue Gebäude besser setzen (Bauen, 6) · Schwerpunkte: Lenken (Z)"));
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
            Header("Versorgung", ", und . drehen · Leitungen in Kette, RMB beendet · Netze selbst verlegen: Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz → Haus");
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
                tools.Add(PBtn("Abwasser-Ansicht", () => utilityOverlay.Toggle(UtilityOverlayView.Overlay.Sewage),
                    () => utilityOverlay.Current == UtilityOverlayView.Overlay.Sewage, "Info-Ansicht Abwasser (O)")); // G3 erweitert
            }
            if (envOverlay != null) // G3 erweitert: Wasserverschmutzung
                tools.Add(PBtn("Wasserqualität", () => envOverlay.Toggle(CityBuilder.Game.Environment.EnvironmentOverlayView.Overlay.WaterQuality),
                    () => envOverlay.Current == CityBuilder.Game.Environment.EnvironmentOverlayView.Overlay.WaterQuality, "Verschmutzung der Gewässer (braun = verschmutzt)"));
            tools.Add(PBtn("Unterirdisch", tool.ToggleUndergroundView, () => tool.UndergroundView, "Unterirdisch-Ansicht (X) – Rohre, Kanäle und Erdkabel liegen unter dem Gelände"));
            panelBody.Add(tools);

            // --- Netze selbst verlegt: Zeile „Entlang Straßen verlegen“ (nur sichtbar, wenn das Werkzeug aktiv ist)
            var along = Container("cb-tool-row");
            along.Add(PLabel("Entlang Straßen"));
            foreach (var type in UtilityTool.AlongTypes)
            {
                var t = type;
                along.Add(PBtn(ShortLineName(t), () => utilityTool.SelectAlong(t), () => utilityTool.AlongType == t,
                    $"{t.DisplayName} entlang der Straßen verlegen ({t.CostPerMeter:0} €/m)"));
            }
            along.Add(PBtn("Alle Stadtstraßen", utilityTool.ToggleAlongAll, () => utilityTool.AlongAll,
                "Vorschau für alle Stadtstraßen mit Gesamtkosten – Klick in die Karte oder „Jetzt verlegen“ baut (Esc bricht ab)"));
            along.Add(PBtn("Jetzt verlegen", () => utilityTool.BuildAlongNow(), null, "Verlegt die aktuelle Vorschau zu den normalen Kosten"));
            along.Add(Container("cb-spacer"));
            along.Add(PValue(() => UtilityTool.AlongText(utilityTool.AlongPreview, utilityTool.AlongAll), "cb-muted"));
            panelSlowRefresh.Add(() => along.style.display =
                utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.AlongRoads ? DisplayStyle.Flex : DisplayStyle.None);
            panelBody.Add(along);
            // --- Ende

            var tiles = Container("cb-tiles");
            foreach (var type in UtilityCatalog.All)
            {
                var t = type;
                string unit = UtilityCatalog.Unit(t.Kind);
                string power = t.IsSubstation ? $"{BuildLabels.Number(t.Capacity / 1000f)} MW" : $"{BuildLabels.Number(t.Capacity)} {unit}";
                tiles.Add(Tile(BuildLabels.FacilityName(t), power,
                    t.Kind == UtilityKind.Power ? PowerSwatch : t.Kind == UtilityKind.Sewage ? SewageSwatch : WaterSwatch,
                    t.Kind == UtilityKind.Power ? HudIconKind.Power : t.Kind == UtilityKind.Sewage ? HudIconKind.Pipe : HudIconKind.Water,
                    () => utilityTool.SelectFacility(t),
                    () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.Facility && utilityTool.SelectedType == t,
                    $"{t.DisplayName} · {(t.IsSubstation ? "Umspannkapazität" : "Leistung")} {power} · Fläche {t.Width:0}×{t.Depth:0} m · " +
                    $"Baukosten {BuildLabels.Money(t.Cost)}, Unterhalt {BuildLabels.Money(t.UpkeepPerDay)}/Tag (Stadtkasse)" +
                    (t.NeedsWater ? " · muss am Wasser stehen" : "") +
                    // --- Netze selbst verlegt: Rolle im Stromnetz
                    (t.IsSubstation ? " · verbindet Hochspannung und Ortsnetz, begrenzt die Last des Ortsnetzes · Zufahrt zur Straße" : "") +
                    (t.Kind == UtilityKind.Power && !t.IsSubstation
                        ? (t.FeedsLocalGrid ? " · klein: speist direkt ins Ortsnetz (oder in die Hochspannung)" : " · speist nur in die Hochspannung → Umspannwerk")
                        : "") +
                    // --- G3 erweitert: Atom/Abwasser
                    (t.ConcernRadius > 0f ? $" · kein Abgas, aber Anwohner im Umkreis {t.ConcernRadius:0} m sorgen sich · seltener Störfall" : "") +
                    (t.Untreated ? " · billig, leitet ungeklärt ins Gewässer → verschmutzt flussabwärts (Pumpen, Ufer)" : "") +
                    (t.Kind == UtilityKind.Sewage && !t.Untreated ? " · klärt Abwasser vollständig" : "") +
                    // --- Energie erweitert
                    (t.Output == OutputModel.Hydro ? " · nur im Fluss (Wehr quer zum Fluss) · Leistung je Standort aus Abfluss und Fallhöhe (Uferhöhe), schwankt mit dem Niederschlag · Fluss bleibt durchgängig" : "") +
                    (t.Site == FacilitySite.Sea ? " · nur im offenen Meer · Wind schwankt stündlich (Ø ≈ 50 %) · Anschluss per Hochspannungs-Seekabel" : "") +
                    (t.Output == OutputModel.Biogas ? $" · braucht Zufahrt (Anlieferung) · Rohstoff aus Bauernhöfen im Umkreis {UtilitySystem.BiogasFeedRadius / 1000f:0} km, sonst {UtilitySystem.BiogasBaseShare * 100f:0} % Grundlast" : "") +
                    (t.IsStorage ? $" · speichert {t.StorageKwh / 1000f:0} MWh · lädt bei Überschuss, liefert bei Unterdeckung (nach den Kraftwerken)" : "")));
            }
            tiles.Add(Container("cb-tile-sep"));
            float reach = ConnectionRules.BuildingReach;
            LineTile(tiles, UtilityTool.UtilityAction.HighVoltageLine, PowerSwatch, HudIconKind.Power,
                $"Hochspannung vom Kraftwerk zum Umspannwerk · große Masten (Spannweite bis {UtilityCatalog.HighVoltageLine.MaxSpan:0} m) · keine Hausanschlüsse");
            LineTile(tiles, UtilityTool.UtilityAction.SubseaCable, PowerSwatch, HudIconKind.Underground, // Energie erweitert
                "Hochspannung ohne Masten: Seekabel vom Offshore-Windpark zum Umspannwerk an Land oder Erdkabel unter Straßen · dreimal so teuer wie die Freileitung");
            LineTile(tiles, UtilityTool.UtilityAction.PowerLine, PowerSwatch, HudIconKind.Power,
                $"Ortsnetz als Freileitung vom Umspannwerk entlang der Straße · billig · kleine Masten neben der Straße brauchen Platz " +
                $"(Abstand ≤ {UtilitySystem.MaxPoleSpan:0} m, kein Abriss) · Gebäude ≤ {reach:0} m angeschlossen, Seil zum Haus");
            LineTile(tiles, UtilityTool.UtilityAction.PowerCable, PowerSwatch, HudIconKind.Underground,
                $"Ortsnetz als Erdkabel unter dem Gehweg · teurer, braucht keinen Platz · Gebäude ≤ {reach:0} m angeschlossen");
            LineTile(tiles, UtilityTool.UtilityAction.WaterPipe, WaterSwatch, HudIconKind.Pipe,
                $"Wasserrohr selbst verlegen (Straßen führen kein Wasser mit) · unter dem Gehweg einrasten · Gebäude ≤ {reach:0} m angeschlossen");
            // --- G3 erweitert: Abwasserkanal
            LineTile(tiles, UtilityTool.UtilityAction.SewagePipe, SewageSwatch, HudIconKind.Pipe,
                $"Abwasserkanal (unterirdisch, selbst verlegen) · Gebäude mit Zufahrt ≤ {reach:0} m vom Kanal sind angeschlossen · am besten entlang der Straßen, Kette bis zur Kläranlage/zum Auslass");
            // --- Netze selbst verlegt: Hilfe für bestehende Städte und normales Bauen
            tiles.Add(Tile("Entlang Straßen", "Vorschau + Kosten", PowerSwatch, HudIconKind.Road,
                () => utilityTool.SelectAlong(utilityTool.AlongType),
                () => utilityTool.Active && utilityTool.Current == UtilityTool.UtilityAction.AlongRoads,
                "Netz entlang Straßen verlegen: Erdkabel, Freileitung, Wasserrohr oder Kanal automatisch entlang der Straße unter dem Mauszeiger " +
                "oder aller Stadtstraßen – zu den normalen Kosten, Vorschau mit Kosten und neu angeschlossenen Gebäuden"));
            // --- Ende
            panelBody.Add(tiles);

            var info = Container("cb-info-row");
            info.Add(BalanceLabel(u, UtilityKind.Power));
            info.Add(BalanceLabel(u, UtilityKind.Water));
            info.Add(BalanceLabel(u, UtilityKind.Sewage)); // G3 erweitert
            var upkeep = Text("", "cb-muted");
            panelSlowRefresh.Add(() => upkeep.text = $"Unterhalt {BuildLabels.Money(u.TotalUpkeepPerDay)}/Tag (nur Anzeige)");
            info.Add(upkeep);
            panelBody.Add(info);
        }

        /// <summary>Kachel eines Leitungs-Werkzeugs (Name, Preis je Meter aus der Leitungsart).</summary>
        void LineTile(VisualElement tiles, UtilityTool.UtilityAction action, Color swatch, HudIconKind icon, string tip)
        {
            var t = UtilityTool.LineTypeOf(action);
            tiles.Add(Tile(ShortLineName(t), $"{t.CostPerMeter:0} €/m", swatch, icon,
                () => utilityTool.Select(action),
                () => utilityTool.Active && utilityTool.Current == action, tip));
        }

        static string ShortLineName(UtilityLineType t)
        {
            if (t == UtilityCatalog.HighVoltageLine) return "Hochspannung";
            if (t == UtilityCatalog.OverheadLine) return "Freileitung";
            if (t == UtilityCatalog.UndergroundCable) return "Erdkabel";
            if (t == UtilityCatalog.SubseaCable) return "Seekabel"; // Energie erweitert
            return t.DisplayName;
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

        // ------------------------------------------------------------ Dienste (G4)

        void BuildServicePanel()
        {
            Header("Dienste", ", und . drehen · Reichweite über das Straßennetz (Vorschau färbt erreichte Straßen) · Parks über Fußweg");
            if (serviceTool == null) { panelBody.Add(Info(() => "Dienste-Werkzeug fehlt.")); return; }
            var s = runner.World.Services;

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Drehung"));
            tools.Add(PBtn("−15°", () => serviceTool.Rotate(-15f), null, "Gebäude drehen (,)"));
            tools.Add(PValue(() => $"{serviceTool.Rotation:0}°"));
            tools.Add(PBtn("+15°", () => serviceTool.Rotate(15f), null, "Gebäude drehen (.)"));
            tools.Add(Container("cb-spacer"));
            if (serviceOverlay != null)
            {
                tools.Add(PLabel("Ansicht"));
                tools.Add(PValue(() => serviceOverlay.Selected != null ? ServiceKinds.DisplayName(serviceOverlay.Selected) : "aus"));
                tools.Add(PBtn("Durchschalten", serviceOverlay.Cycle, () => serviceOverlay.Selected != null,
                    "Abdeckungs-Ansicht: Polizei → Feuerwehr → … → Müll → aus (⇧4)"));
            }
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            string lastKind = null;
            foreach (var type in ServiceCatalog.All)
            {
                var t = type;
                if (lastKind != null && t.Kind != lastKind) tiles.Add(Container("cb-tile-sep"));
                lastKind = t.Kind;
                string range = t.Broadcast ? $"Funkradius {t.Range:0} m" : t.Walking ? $"Fußweg {t.Range:0} m" : $"Reichweite {t.Range:0.#} min Fahrzeit (≈ {t.RangeGameMinutes:0} Spielmin.)"; // G4 erw.: Funk
                tiles.Add(Tile(ServiceTool.ShortName(t), $"{BuildLabels.Number(t.Capacity)} {t.CapacityUnit}", ServiceSwatch, HudIconKind.Service,
                    () => serviceTool.SelectType(t),
                    () => serviceTool.Active && serviceTool.Current == ServiceTool.ServiceAction.Place && serviceTool.SelectedType == t,
                    $"{t.DisplayName} · {ServiceKinds.DisplayName(t.Kind)} · Kapazität {BuildLabels.Number(t.Capacity)} {t.CapacityUnit}/Tag · {range} · " +
                    $"Fläche {t.Width:0}×{t.Depth:0} m · Baukosten {BuildLabels.Money(t.Cost)}, Unterhalt {BuildLabels.Money(t.UpkeepPerWeek)}/Woche"));
            }
            // --- Eigene Assets: Lärmschutzwand (Linienbauwerk wie Leitungen) im Dienste-Werkzeug
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(Tile("Lärmschutzwand", $"{CityBuilder.Simulation.Environment.NoiseBarrierSystem.CostPerMeter:0} €/m", ServiceSwatch, HudIconKind.Road,
                () => serviceTool.Select(ServiceTool.ServiceAction.NoiseBarrier),
                () => serviceTool.Active && serviceTool.Current == ServiceTool.ServiceAction.NoiseBarrier,
                "Lärmschutzwand entlang von Straßen ziehen (rastet neben dem Gehweg ein, Strg = frei) · dämpft den Straßenlärm dahinter · " +
                "Klick = Punkt, RMB/Esc = fertig"));
            // --- Ende Eigene Assets
            // --- Umwelt (Etappe 6): Bäume pflanzen (Pinsel)
            tiles.Add(Tile("Bäume pflanzen", $"{CityBuilder.Simulation.Environment.EcologyRules.CostPerTree:0} €/Baum", ServiceSwatch, HudIconKind.Tree,
                () => serviceTool.Select(ServiceTool.ServiceAction.PlantTrees),
                () => serviceTool.Active && serviceTool.Current == ServiceTool.ServiceAction.PlantTrees,
                "Aufforsten auf freier Fläche: Setzlinge wachsen in ~2 Monaten zum Wald, filtern Abgas, Boden erholt sich schneller · " +
                "LMB halten = pflanzen, [ ] = Pinselgröße"));
            // --- Ende Umwelt
            panelBody.Add(tiles);

            var info = Container("cb-info-row");
            var summary = Text("", "cb-muted");
            panelSlowRefresh.Add(() => summary.text =
                $"{s.FacilityCount} Dienste · Unterhalt {BuildLabels.Money((float)s.WeeklyUpkeep())}/Woche · liegender Müll {s.TotalGarbage:0.#} t" +
                $" · Attraktivität {s.Attractiveness:0}{(s.HasTownHall ? " · Rathaus ✓" : "")}"); // Eigene Assets
            info.Add(summary);
            panelBody.Add(info);
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
            if (serviceTool != null) // G4
                tiles.Add(Tile("Dienstgebäude", "Dienste", DangerSwatch, HudIconKind.Service,
                    () => serviceTool.Select(ServiceTool.ServiceAction.Remove),
                    () => serviceTool.Active && serviceTool.Current == ServiceTool.ServiceAction.Remove,
                    "Polizei, Schule, Park, Wahrzeichen, Lärmschutzwand … anklicken – 25 % der Baukosten zurück"));
            AddTransitBulldozeTile(tiles); // ÖPNV
            AddHouseBulldozeTile(tiles); // Wohnhäuser
            AddRailBulldozeTile(tiles); // Bahn
            panelBody.Add(tiles);
            panelBody.Add(Info(() => "Gesetzte Gebäude nur per Abriss (25 % zurück) · Zonengebäude alter Stände verschwinden über Entzonen oder wenn ihre Straße wegfällt."));
        }
    }
}
