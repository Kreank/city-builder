# City Builder (Arbeitstitel)

Länder-/Reichs-Aufbausimulation in Unity 6 (URP). Inspiration: Cities: Skylines (freie Straßen,
Umwelt-Overlays), Anno (Produktionsketten, Bedürfnisstufen). Ziel: Start bei 0 → Stadt → Reich aus
mehreren Städten → Handel mit anderen Reichen (Bots, später echte Spieler, asynchron).
Plattform: PC zuerst, Mobile später (deshalb URP mit PC- und Mobile-Pipeline-Asset).

## Vision (vom Spieler formuliert, Leitlinie für alle Etappen)
- **Fiktive Erde:** Man erschafft sein eigenes Land, baut eine Stadt, später weitere Städte (selbst oder durch NPCs).
  Jedes Land hat andere natürliche Vorkommen und Stärken (Tourismus, Rohstoffe, Technik …).
- **Handel intern und extern:** zwischen eigenen Städten und zwischen Ländern; Regierungen können gebildet werden.
  **Freunde einladen** (Multiplayer, asynchron): eigene Städte im eigenen Land oder als fremdes Land, Handel untereinander.
- **Zwei Bauarten:** (1) **Gebiete** markieren, Leute kommen und bebauen selbst – Wohnen (gering/mittel/dicht),
  Industrie (mehrere Formen), Büro + Läden. (2) **Einzeln baubare Gebäude mit Einflussradius:** Polizei, Feuerwehr,
  Schulen, Bibliothek, Parks, Strom, Wasser und Abwasser, Medien (Zeitung, Radio, TV-Sender), Taxi-/Bus-/Zug-Depot,
  Müllhalde, Verbrennungsanlage, Flughafen, Hafen, S-Bahn, Fernbahn, U-Bahn.
- **Bevölkerung & Wirtschaft mit ausgeklügelter Logik:** Besiedlung folgt Angebot und Nachfrage; die Bilanz muss gehalten,
  optimal Einnahmen erzielt werden. Das Spiel soll eine echte **Herausforderung** sein (ggf. KI für Besiedlung/NPCs).
- **Alles, was es wirklich gibt:** Schiffe, Flugzeuge, Züge, Autobahnen, Straßen, Tunnel, Brücken, Zugbrücken,
  Solarparks, Kohle- und Atomkraftwerke … – Schritt für Schritt. **Tiefgang ist sehr wichtig** (lieber tief als breit).

## Entscheidung: Bürgerverhalten (Konzept, noch nicht umgesetzt)
- **Spiel-KI im Kern, keine Sprach-KI zur Laufzeit** (bewusst entschieden: keine LLM-„Hauptfiguren“, kein LLM-Erzähler).
- **Utility AI** mit Bedürfnissen (Wohnen, Arbeit/Einkommen, Versorgung, Gesundheit, Sicherheit, Bildung, Freizeit,
  Umwelt): Bürger bewerten Optionen und wählen, was ihre Bedürfnisse am meisten verbessert.
- **Abwechslung statt Uhrwerk:** Persönlichkeit je Bürger (eigene Gewichte), gewichteter Zufall statt immer der besten
  Option, Gedächtnis/Gewohnheiten (z. B. Stau → Bus), Beziehungen (Freunde, Partner, Kinder) und Lebensereignisse.
- **KI nur in der Entwicklung:** Ein Sprachmodell erzeugt Inhalte (Persönlichkeiten, Tagesabläufe, Lebensereignisse,
  Vorlieben, Sprüche), die als Daten mitgeliefert werden – im Spiel keine KI-Kosten, offline lauffähig.
- Simulation bleibt deterministisch (Multiplayer, Replays, Tests).

## Architektur-Regeln
- **Simulation ≠ Darstellung.** `Assets/_Project/Scripts/Simulation` ist reines C# (`noEngineReferences: true`),
  kein `UnityEngine`. Vektoren: `System.Numerics.Vector2` (x, y) ↔ Unity (x, 0, z) über `SimConvert`.
- Simulation läuft in festen Ticks (`SimulationWorld.Step`, 1 Tick = 6 Spielminuten), nie mit `Time.deltaTime`.
  Teilsysteme implementieren `ISimulationSystem`.
- Views (`Scripts/Game`) lesen die Simulation und hören auf Events; sie ändern keinen Zustand an der Simulation vorbei.
  Spieleraktionen gehen über Methoden der Simulation (später: Commands, wichtig für Multiplayer/Replays).
- Umwelt (Lärm, Abgas, später Klima, Böden, Rohstoffe) = `GridLayer`-Raster, obwohl Bauen frei ist.
- C#-Stand von Unity: max. C# 9. Das Testprojekt erzwingt `LangVersion 9`.
- Bot-Reiche und Spieler sollen dieselben Schnittstellen nutzen (Handel, Verträge).

## Struktur
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Traffic, Environment, Zoning, Citizens, Utilities)
- `Assets/_Project/Scripts/Game` – Unity-Seite (Runner, Kamera, Straßen-Tool/-View, Verkehr-Views, Overlays, HUD)
  - `RoadTool` hängt beim Start seine Begleiter an (Guides, Toolbar, VehicleView, TrafficSignsView,
    TrafficOverlayView, TrafficPanel, InspectorPanel …) – neue Views dort ergänzen, dann muss die Szene nicht neu erzeugt werden.
- `Assets/_Project/Scripts/Editor/ProjectSetup.cs` – erzeugt `Scenes/Main.unity` + Materialien (Menü „City Builder“)
- `Assets/_Project/Scripts/Editor/SmokeTest.cs` – baut alles einmal inkl. Unity-Meshes (Menü „City Builder → Smoke Test“)
- `Tests/CityBuilder.Simulation.Tests` – xUnit-Tests des Kerns ohne Unity (inkl. Verkehrsläufe)

## Befehle
- Tests: `dotnet test Tests/CityBuilder.Simulation.Tests`
- Szene neu erzeugen (Editor geschlossen):
  `"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod CityBuilder.Editor.ProjectSetup.CreateMainScene -logFile setup.log`
- Rauchtest (Editor geschlossen): wie oben mit `-executeMethod CityBuilder.Editor.SmokeTest.Run`
- Dateien nie mit PowerShell `Get-Content`/`Set-Content` umschreiben (PS 5.1 zerstört UTF-8) – Edit-Werkzeug nutzen.

## Straßen
- Rechtsverkehr. Querschnitt pro Typ in `RoadCatalog` (Spuren links→rechts in Segmentrichtung Start→Ende).
  Segmentrichtung ist bedeutsam (Einbahn, Autobahn, Rampe) – beim Zerteilen bleibt sie erhalten.
- Bauen immer über `RoadBuilder.Plan…()` → Vorschau → `RoadBuilder.Build…()`. Kreuzungen entstehen automatisch
  durch Zerteilen; Autobahnen kreuzen nie ebenerdig und verbinden sich nur mit Autobahn/Rampe.
- Kreisverkehr = Ring aus Einbahn-Bögen gegen den Uhrzeigersinn; Inneres wird entfernt.
- Höhen: Knoten haben `Height`, Segmente interpolieren linear. Gleiche Ebene = ±1,5 m (Kreuzung),
  ab 4,5 m Unterschied kreuzungsfrei, dazwischen Fehler. Einrasten nur auf gleicher Ebene. Max. Steigung je Kategorie.
  Höhe −30…+40 m: 0 bis −7 m offener Trog (Boden bekommt Löcher, `GroundView`), ab −7 m Tunnel (überdeckt, Portal, kein Lärm).
  Unterirdisch-Ansicht (X, automatisch beim Bauen unter 0 m) macht den Boden durchsichtig.
- `RoadTemplates`: Raute, Kleeblatt, Dreieck – Quadrant 4 wird beschrieben und um 90° gedreht (A↔B tauschen).
- `RoadNetwork.SegmentReplaced` (Zerteilen/Umbau/Umdrehen) → Verkehrsregeln wandern mit.
- **Knoten-Geometrie** rein in der Simulation (`JunctionGeometry`, getestet in `JunctionGeometryTests`):
  `Classify` → `Junction` (ab 3 Straßen oder Typwechsel: Kreuzungsfläche), `Bend` (Grad 2, gleicher Typ, Knick
  > 0,5°: Eckstück mit vollem Querschnitt als Bézier, Innenradius ≥ halbe Breite + 1 m), `None` (gerade Fortsetzung,
  keine Knotenfläche). `Trim` = Bogenlänge, um die ein Segment vor dem Knoten endet: so weit, dass der gerundete
  Bordstein (Radius 4 m, ohne Gehweg 1,5 m) und die Gehweg-Außenkanten der Nachbarn davor liegen (max. 45 % der
  Länge / 60 m). `Layout` liefert Fahrbahn-Umriss (ohne Gehweg, Ohren-Triangulierung) und Gehwegecken
  (Bordstein- und Außenkante, gleiche Punktzahl). Segment-, Kreuzungs- und Eckstück-Mesh benutzen dieselbe
  Abtastung `SampledCurve` (Endpunkte nach Bogenlänge, nicht nach t). `SampledCurve.OffsetAt` hält Innenkanten
  enger Kurven frei von Selbstschnitt (enge Zone → Fächerpunkt). Grenze: Haarnadeln mit Schenkeln kürzer als die
  Straßenbreite überdecken sich physisch.

## Gelände
- `TerrainMap` (Simulation, 4-m-Raster): erzeugt (Hügel bis ~90 m, Fluss, Wasserspiegel 0) oder flach (Tests).
  `RoadNetwork.Ground` = Gelände. **Straßenhöhen sind absolut**, Brücke/Damm/Trog/Tunnel ergibt sich aus der
  Elevation (Höhe über Gelände). Bauhöhe im Werkzeug = Elevation. Ebenerdige Abschnitte (±4 m) formen das Gelände
  (`ConformTo`), Wasser nur per Brücke (≥ 2,5 m über Wasserspiegel).
- `GroundView` zeichnet das Gelände in Chunks inkl. Trog-Löchern und Wasserfläche.
- **Automatische Trassierung** (Bauhöhe 0, `followTerrain`): Segment bekommt ein Höhenprofil (`Profiles`):
  geglättet dem Gelände folgend, Berg > 14 m über der direkten Linie → Tunnel, Tal/Wasser > 10 m darunter → Brücke,
  Steigung begrenzt; Gelände wird bis 6 m Damm/Einschnitt eingeebnet. Profile werden beim Zerteilen/Umbau mitgenommen.
- Knoten mit zwei unterschiedlichen Straßentypen bekommen ein Übergangsstück (`RoadMeshBuilder.NeedsJunctionMesh`),
  Knicke gleichen Typs ein Eckstück (`RoadMeshBuilder.BuildBend`, s. Straßen).
- **Terraforming** (`TerrainMap.Terraform(TerraformBrush)`): Anheben, Absenken, Glätten (3×3-Mittel), Einebnen auf
  Zielhöhe; Radius, Stärke (max. Δh je Anwendung), weiche Kante. Ändert aktuelle **und** natürliche Höhe (gewollte
  Änderung des Grundgeländes → später gebaute Straßen gelten dort als ebenerdig). Grenzen −12…+150 m.
  Rückgabe `TerraformResult`: geänderte/gesperrte Punkte, bewegte Erdmenge (m³, für spätere Kosten), Dirty-Rechteck;
  dasselbe Rechteck kommt über `TerrainMap.Changed` (GroundView/RoadNetworkView bauen nur dort neu).
- **Sperrmaske** `TerrainLocks` (gesetzt von `SimulationWorld` als `TerrainMap.LockQuery`): gesperrt sind alle Straßen
  (auch Brücken/Tröge/Tunnel) bis Fahrbahn + `RoadMargin` und alle Gebäude-Grundstücke + 1 Rasterzelle; danach
  8 m weiche Überblendung (Böschung statt Stufe). Zustandslos aus Netz und Gebäudeliste gelesen. Zusätzlich gilt
  weiter `RoadRule`. Wasser: Wasserspiegel bleibt 0, Absenken unter 0 ergibt Wasser (GroundView zeichnet eine
  Wasserfläche auf 0 über die ganze Karte; Bäume in neuem Wasser werden ausgeblendet).
- `TerrainMap.WorkedAt`: 0..1 bearbeiteter Boden (Straßenböschung via `ConformTo`, Terraforming), wächst nicht zu.
- Werkzeug `Game/Terrain/TerraformTool` (Begleiter am `RoadTool`, Modus `RoadTool.Mode.Terraform`, Taste L,
  Leiste „Gelände (L)“): LMB halten = 20 Anwendungen/s (Echtzeit), RMB = Anheben/Absenken umgekehrt,
  Einebnen nimmt die Höhe beim Klick auf, `[`/`]` Radius; Pinsel-Vorschau als geländefolgende Ringe.
- **Gelände-Farben** ohne Texturen: `GroundView` setzt Vertex-Farben (Gras/Wiese per Rauschen, Sand am Ufer, nasser
  Sand unter Wasser, Fels ab ~30° Steigung, Erde/Kies auf bearbeitetem Boden); Shader
  `Resources/Shaders/GroundVertexColor.shader` (eigener URP-Shader, da URP/Lit keine Vertex-Farben zeigt; Fallback
  auf das alte Material, wenn er fehlt).

## Zonen & Gebäude (G1)
- `ZoneGrid`: 8-m-Zellen, 4 Reihen tief je Seite ebenerdiger Stadtstraßen, neu berechnet bei Netzänderung.
  Gemalte Zonen in `ZoneMap` (4-m-Raster) → überleben Zerteilen/Umbau.
- `CitySystem` (alle 30 Spielminuten): Nachfrage je Zonenart, Wachstum (Grundstück 1×1–4×4, Gelände einebnen),
  Bau; Abriss bei Straße durchs Grundstück, fehlender Anbindung oder Umwidmung. Gebäude behalten Kapazitäten
  (`Households`, `Jobs`); die Belegung (`OccupiedHouseholds`, `Residents`, `Workers`) setzt das Bürgersystem.
  Nachfrage kommt aus `CitySystem.Population` (vom Bürgersystem gefüllt: Arbeitslose, Pendler, freie Jobs/Wohnungen,
  unversorgte Käufer); `MoveInPull` steuert den Zuzug.
- Anschlusspunkt: `Building.FrontSegmentId` + `FrontT` (Zufahrt mitten an der Straßenseite).
- Darstellung: `ZoneView` (nur im Zonen-Werkzeug), `BuildingView` (Katalogmodelle je Zone, sonst Quader).
  **Zonenanzeige ohne Volltönung:** Platzhalter-Quader sind neutral (6 helle Fassadenfarben, Auswahl über `Variant`);
  die Zone zeigt eine farbige **Grundstücksplatte** (Bordkante ums Gebäude, 96 % des Grundstücks), normal gedämpft.
  Im Zonen-Werkzeug und Info-Modus (`BuildingView.HighlightZones`, aus `RoadTool.CurrentMode`) wird die Platte kräftig
  und Platzhalter werden in Zonenfarbe getönt. Store-Modelle werden nie getönt. `BuildingView.VisualHeight(b)` liefert
  die sichtbare Höhe (Auswahl/Hervorhebung).

## Bürger (G2)
- `Simulation/Citizens`: `Citizen` (Alter, Bildung, Haushalt/Wohnung, Job, Bedürfnis Einkaufen, Tätigkeit, Fahrt),
  `Household`, `CitizenSystem` (jeden Tick Tagesablauf, alle 5 Ticks Zuzug/Jobsuche/Statistik, täglich Altern),
  `RoadComponents` (zusammenhängende Netzteile = Vorprüfung „erreichbar“).
- Zuzug: wie G1 ein Haushalt je Gebäude und Durchlauf, solange `MoveInPull > 5` (Single/Paar/Familie/Alleinerziehend/
  Senioren). Kein Außenanschluss nötig (Start). Jobsuche: nächster freier passender Job im selben Netzteil (Büro braucht
  mittleren Abschluss, Gebildete bevorzugen Büro). Ohne Job hier → Auspendler über den nächsten Außenanschluss;
  Auspendler wechseln zu freien Jobs hier. Freie Jobs ohne einheimische Bewerber → Einpendler (Bürger ohne Haushalt);
  Einwohner verdrängen Einpendler. Altern am Geburtstag, 18 = Abschluss, 65 = Rente, ab 80 Sterbewahrscheinlichkeit,
  Wegzug bei langer Arbeitslosigkeit des ganzen Haushalts, bei Abriss Umzug in freie Wohnung oder Wegzug.
- Tagesablauf: Arbeitsbeginn je Bürger 6:00–9:30, 8 h Arbeit; danach (bzw. tagsüber ohne Job) einkaufen in
  Einzelhandel/Mischgebiet 10–20 Uhr, wenn das Bedürfnis ≥ 1 ist (ein Einkauf versorgt den Haushalt); dann heim.
  Kinder/Schüler bleiben vorerst zu Hause.
- Fahrten: Anteil `CarShare` (35 %, Einpendler 60 %) × `TrafficSystem.Demand` fährt als echtes Auto
  (`TrafficSystem.RequestTrip` von Zufahrt/Außenanschluss zu Zufahrt/Außenanschluss), höchstens 70 % von `MaxVehicles`.
  Rest = Platzhalter (zu Fuß/ÖPNV, Fahrzeit aus Luftlinie, 15 km/h). Ankunft meldet `TrafficSystem.TripEnded`
  (auch bei Abbruch → Bürger kommt trotzdem an; Sicherheitsnetz 3 Tage). Handler dürfen dort keine Fahrten starten.
  **Zeitverzerrung:** Verkehr läuft mit 0,25 s je Tick, die Uhr mit 6 min je Tick – eine Autofahrt dauert daher
  oft Spielstunden bis über einen Tag (wie Cities: Skylines). Der Tagesablauf ist darauf robust, Autofahrer sind länger „unterwegs“.
- Darstellung: Inspektor (Taste C, s. u.) mit Stadtübersicht (Statistik, Altersbalken, Bildung). HUD zeigt Arbeitslose/Pendler.
- Haushaltsart `Household.Kind` (Single/Paar/Familie/Alleinerziehend/Senioren/sonstige) wird aus dem Alter der
  Mitglieder abgeleitet, nicht gespeichert. Einen Familienstand je Bürger gibt es noch nicht.
- Lesende Abfragen: `CitizenSystem.CitizenForTrip(tripId)` / `VehicleOf(citizen)`, `Citizen.TripStartTick`,
  `PlannedArrivalTick` (Platzhalter-Fahrt), `HasCarTrip`; `Vehicle.StartTime` (Verkehrszeit beim Start).

## Spiel-UI (UI Toolkit, Phase 1)
- `Game/UI/Hud/GameHud` (+ `GameHud.BuildBar.cs`), Begleiter am `RoadTool` (zuletzt angehängt). Erzeugt zur Laufzeit
  `PanelSettings` (ScaleWithScreenSize, Referenz 1920×1080, Match 0,5) + `UIDocument`; lädt
  `Resources/UI/CityTheme.tss` (importiert nur `unity-theme://default` → Standardschrift) und `Resources/UI/CityHud.uss`
  (Design-Token als `--cb-…`-Variablen, Klassen `cb-…`). Aufbau in C#, weil die Inhalte aus Katalogen kommen.
  Fehlen Theme/USS, hängt das RoadTool die alte IMGUI-`RoadToolbar` als Rückfall an.
- Inhalt: obere Leiste (Stadtname-Platzhalter `GameHud.CityName`, Datum mit Anzeige-Kalender 12×30 Tage, Tempo,
  Einwohner, Arbeitslose, Strom-/Wasser-Warnchip nur bei unterdecktem Netz, Info-Modus), linke Ansichtsleiste
  (Stau, Lärm, Abgas, Strom, Wasser, Unterirdisch), Bauleiste (Kategorien → Werkzeugzeile + Kacheln + Infozeile),
  Hinweiszeile aus `RoadTool.Guide` (Fehler/Info/Länge/Kosten), Tooltips, Toasts.
- Knöpfe rufen dieselben öffentlichen Methoden wie die Tasten (RoadTool, Werkzeuge, Views, `SimulationRunner.TogglePause`);
  Zustände werden jedes Frame aus den Quellen gelesen → Tasten und Knöpfe bleiben synchron. Die Kategorie folgt dem Modus.
- Icons: `HudIcon` zeichnet per Painter2D (24er-Raster, Farbe = `color`), keine Bilddateien.
- Mausblockade: `UiBlocker.RegisterToolkitRoot` → `IsPointerOverUi` prüft zusätzlich `panel.Pick`. Reine Layout-Container
  haben `PickingMode.Ignore`, nur echte Flächen blockieren. IMGUI-Fenster richten sich nach `HudLayout` (Top/Bottom/LeftInset).
- Meldungen: `HudMessages.Info/Warning(title, text, icon, key)` – gleicher `key` ersetzt die alte Meldung.
  Quellen bisher: `Game/Utilities/UtilityAlerts` (Netz unterdeckt/wieder ausreichend, Gebäude verlassen, Leitung durch Bau abgerissen).
- Schrift: Manrope/Inter geplant, noch nicht im Projekt – Tausch über `-unity-font-definition` in `.cb-root`/`.cb-heading`.
- Noch IMGUI: Inspektor, TrafficPanel, UtilityPanel, DebugHud (F1), Welt-Beschriftungen (RoadGuidesView, UtilityTool).
  Phase 2: Inspektor als rechtes Panel im neuen Stil (Entwurf `Panels.dc.html`).

## Inspektor (Info-Modus, Taste C)
- `Game/Inspect/InspectorPanel` (Begleiter am `RoadTool`, ersetzt das frühere `CitizenPanel`), `InspectorPicking`.
  Liest nur. Klick = Strahl gegen Gebäude (Grundstück × `VisualHeight`), Versorgungsbauwerke (Fläche × grobe
  Platzhalterhöhe) und Fahrzeuge (Kugel um die interpolierte Position, kleiner Vorrang vor Gebäuden); Rückfall:
  Geländepunkt unter der Maus. Rechtsklick/„Übersicht“ hebt auf, „← Zurück“ geht den Verlauf zurück.
- Auswahl = Art + Id (`Selection`), jedes Frame neu nachgeschlagen; verschwundenes Fahrzeug → Auswahl springt zum
  Fahrer (Bürger). Hervorhebung: gelber Umriss (Boden + Dach) bzw. Ring ums Fahrzeug; bei Bürgern zusätzlich
  Wohnung (grün) und Arbeitsplatz/Ziel (blau). **Verfolgen** (F oder Häkchen) für Fahrzeug/Bürger über
  `CameraController.FollowTarget`; Verschieben mit WASD beendet es.
- Reiter: **Gebäude** Übersicht (Zone/Nutzung, Grundstück, Höhe, Stufe, Bau, Wohnungen/Bewohner, Jobs mit
  Einheimischen/Einpendlern, anwesend, Zufahrt, Strom/Wasser mit Ausfalldauer und Restzeit bis Verlassen) ·
  Bewohner (Haushalte mit Art, Mitglieder anklickbar, blätterbar) · Arbeit (Beschäftigte, Bildung, anklickbar) ·
  Umfeld (Lärm, Abgas, Bodenplatte vs. Ursprungsgelände). **Bürger** Person (Alter, Geburtstag, Bildung,
  Haushaltsart, Wohnung, Mitglieder) · Alltag (Arbeit/Pendeln/arbeitslos, Arbeitszeit, Tätigkeit, Ort, Fahrt mit
  Zweck/Ziel/Dauer, Auto anklickbar bzw. Platzhalter-Ankunft, Einkaufsbedürfnis) · Befinden. **Fahrzeug**: Klasse,
  Tempo, Fahrtdauer (Verkehrszeit), Ort, Ziel, Fahrer. **Versorgungsbauwerk**: Leistung, Netzbilanz, Kosten, Emissionen.
- **Platzhalter fürs Bedürfnismodell** (zeigen „— (kommt mit Bedürfnismodell)“, keine Zahlen): Bürger Gesundheit,
  Zufriedenheit, Gemüt, Familienstand; Gebäude Zustand, Zufriedenheit Bewohner/Mitarbeiter/Umfeld; Bodenwert (G6),
  Dienste in Reichweite (G4), Zustand von Bauwerken. Unter „Befinden“ stehen die vorhandenen Rohwerte (Lärm/Abgas/
  Versorgung an der Wohnung, Arbeitslosigkeit, Einkaufsbedürfnis), die das Modell später bewerten kann.
  Neue Werte: dort statt `Pending` einsetzen.

## Strom & Wasser (G3)
- `Simulation/Utilities`: `UtilityCatalog` (Kohle-/Gaskraftwerk, Windrad, Solarpark, Wasserpumpe am Ufer, Brunnen+Wasserturm;
  Leistung Strom kW / Wasser m³/h, Kosten/Unterhalt nur Anzeige – es gibt noch keine Stadtkasse), `UtilitySystem` (alle 30 min).
- Bauwerke frei platziert und gedreht (`PlanFacility` → `BuildFacility`): nicht auf Straßen/Wasser/anderen Bauwerken,
  Pumpe braucht Wasser in Reichweite (Gelände `IsWater`), Fläche wird eingeebnet, Gebäude darunter werden abgerissen,
  Straße durchs Bauwerk reißt es ab. Auf Bauwerksflächen wächst nichts (`CitySystem.LotBlocker`-Hook).
- **Netze**: Stadtstraßen tragen Leitung und Rohr automatisch mit (wie Cities: Skylines – spart Kleinarbeit, Straßen
  liegen ohnehin an jedem Gebäude); Autobahnen/Rampen nicht. Lücken schließen frei verlegte gerade Leitungen/Rohre
  (`PlanLine` → `BuildLine`); Enden verbinden sich im Umkreis 3 m mit Leitungen (auch mittendrin), Bauwerken, Stadtstraßen,
  Gebäuden. Bauwerke hängen an Stadtstraßen, deren Rand ≤ 8 m entfernt ist; Gebäude an ihrer Vorderstraße.
  Zusammenhang per Union-Find, neu berechnet nur bei Änderung (Straßen-/Stadt-/Bauwerks-Version).
- **Strommasten** sind Simulationsobjekte (`UtilityLine.Poles`, berechnet in `UtilityPoles.cs` beim `PlanLine`;
  Vorschau `LinePlan.Poles` mit Fehler je Mast). Regeln: Grundfläche 2×2 m + 0,5 m Rand nie auf Fahrbahn/Gehweg
  (`RoadType.HalfWidth`, nur über überdeckten Tunneln erlaubt), Wasser, Bauwerken oder Gebäuden; Spannweite
  ≤ `MaxPoleSpan` 45 m (auch Leitungsende → Endmast). Leitungsenden selbst dürfen auf Straße/Bauwerk liegen
  (Anschluss), der Endmast rückt dann auf der Leitung nach innen. Blockierte Standorte werden in 1-m-Schritten
  entlang der Leitung verschoben (Straßen werden so überspannt, Masten beidseits); läuft die Leitung längs einer
  Straße (|cos| ≥ 0,8), steht der Mast seitlich 1 m neben dem Straßenrand (Seite der Leitung, mittig → rechts in
  Leitungsrichtung). Kein Standort → Plan ungültig (`PoleOnRoad/-Water/-Facility/-Building`).
  **Platzverbrauch:** Masten sperren Grundstücke über `CitySystem.LotBlocker`. Steht schon ein Gebäude im Weg, ist
  der Plan **ungültig mit Hinweis** (kein Abriss – eine Leitung soll nicht unbemerkt Häuser räumen; Bauwerke reißen
  dagegen ab, weil sie bewusst groß platziert werden).
  **Straße/Bauwerk später durch einen Mast:** Leitung wird mit denselben Enden neu trassiert (`Revision++`,
  `ReroutedLines`); geht das nicht, wird sie abgerissen (`LineRemoved`, `LinesRemovedByConstruction`) – Straßenbau
  hat Vorrang, die Leitung soll aber nach Möglichkeit erhalten bleiben.
  Wasserrohre haben keine Masten und dürfen weiter unter Straßen liegen.
- **Bilanz** je Netz: Erzeugung gegen Bedarf (Bedarf aus Haushalten/Jobs-Kapazität, Industrie mehr). Unterdeckung:
  älteste Gebäude (kleinste Id) zuerst, kleinere können noch nachrutschen; der Rest ist unversorgt.
- **Abfrage für die Stadt**: `world.Utilities.IsSupplied(b)`, `IsSupplied(b, kind)`, `GetStatus(b)` (ja/nein + Ticks ohne),
  `IsSatisfied(b)` (versorgt oder Art noch nicht verlangt), `NetworkOf(b, kind)`.
- **Folge**: > 2 Spieltage ohne Strom oder Wasser → Gebäude wird verlassen (abgerissen, `BuildingAbandoned`). Erst wenn
  es mind. einen Versorger dieser Art gibt (Startphase folgenlos, Dauer zählt erst ab dann). `AbandonmentEnabled` schaltet ab.
- Kraftwerke stempeln Lärm (nach dem `NoiseSystem`) und addieren Abgas; das `UtilitySystem` lässt Abgas vorerst auch
  abklingen/sich verteilen (bisher einzige Quelle → später in ein eigenes Umweltsystem verschieben).
- Darstellung (`Game/Utilities`, Begleiter des `RoadTool`, Modus `RoadTool.Mode.Utility`): `UtilityTool` (J; Bauleiste
  „Versorgung“, , . drehen, Leitungen in Kette, RMB beendet; Rohre erzwingen die Unterirdisch-Ansicht), `UtilityView`
  (Platzhalter aus Grundkörpern, Masten an den Simulations-Standorten mit Fundament und Seilen, Anschlussseil zum
  Leitungsende, Rohre 2,5 m unter Gelände nur unterirdisch sichtbar; Vorschau im Werkzeug zeigt die Masten, ungültige rot),
  `UtilityOverlayView` (O: Strom → Wasser → aus; Marker grün/rot über Gebäuden, wachsen mit der Ausfalldauer),
  `UtilityPanel` (Erzeugung/Bedarf je Netz).
- Später: Abwasser/Kläranlage, Pumpe braucht Strom, Wind/Sonne schwankend (Tageszeit/Wetter), Brennstoff aus Warenketten,
  Stadtkasse (Kosten/Unterhalt abbuchen), Versorgung in Nachfrage/Zufriedenheit (G2/G6), Masten/Rohre folgen späteren
  Geländeänderungen (Terraforming zu Wasser unter einem Mast wird noch nicht geprüft), Hochspannung vs. Ortsnetz,
  Wasserqualität/Verschmutzung flussabwärts. **Mittelinsel:** `RoadCatalog` hat noch keinen Typ mit Mittelstreifen
  (`LaneKind.Median` existiert im Builder, wird aber nicht benutzt) → Straßen mit Mittelstreifen und Masten darauf
  (dann ohne Platzverbrauch neben der Straße) später. Ebenso: Masten unter hohen Brücken erlauben, Masten mehrerer
  Leitungen am selben Punkt zusammenlegen.

## Eigene Assets (im Git)
- **Alles Eigene liegt unter `Assets/_Project/`** (z. B. Blender-Modelle in `Assets/_Project/Models/`, Blender-Skripte
  in `blender/` im Projektordner). Die `.gitignore` versioniert unter `Assets/` nur `_Project`, `Scenes`, `Settings`
  und `InputSystem_Actions` – alles andere (Store-Pakete direkt nach dem Import) wird nie committet.
- Organize verschiebt nur bekannte Paketordner (`ThirdPartySetup.PackageRoots`) und Ordner, die ein Paketimport neu
  angelegt hat (`ThirdParty/_Catalog/PackageRoots.txt`, mitgeschrieben vom `StorePackageImporter`), nie `_Project`.

## Store-Assets (nicht im Git, `Assets/ThirdParty/`)
Einfachster Weg: Pakete im Package Manager → My Assets nur **herunterladen**, dann Fenster
„City Builder → Third Party → Store-Pakete …“ (`StorePackageImporter`): listet den Cache `%APPDATA%\Unity\Asset Store-5.x`
(gilt für **alle** Unity-Projekte!), angehakte Pakete gehören zu diesem Projekt (`Assets/_Project/Config/StorePackages.txt`,
im Git); „Importieren und einsortieren“ importiert die noch fehlenden ohne Dialog (Stand je Rechner in
`ThirdParty/_Catalog/ImportedPackages.txt`), verschiebt jeden neuen Ordner unter `Assets/` nach ThirdParty, wandelt
Materialien, baut den Katalog und schreibt `ThirdParty/_Catalog/Inventory.txt` (alle Prefabs mit Maßen).
**Neue Pakete einsortieren:** Der Katalog ist handgepflegt (`AssetCatalogBuilder`: Prefab-Name + Zielgröße je Kategorie) –
anhand der Inventurliste Einträge ergänzen, dann „Build Asset Catalog“.
Manuell: Import über Package Manager → My Assets (oder Batch `-importPackage`), danach Menü
„City Builder → Third Party → Organize And Convert“ (verschiebt nach ThirdParty, Built-in → URP) und
„… → Build Asset Catalog“ (erzeugt `ThirdParty/_Catalog/Resources/AssetCatalog.asset`). Ohne Katalog: Platzhalter.
Pakete: SimplePoly City, Pandazole City Town, Colorful City (Triangularity), Modular Lowpoly Streets,
Low Poly City Objects (Guddies), Road props for games, Nature Pack (Downtown Game Studio), Nebula Free low poly car pack,
Street Vehicles Pack Autobus Tram (Vladislav Simakov). Colorful City: Editor-DLL wird entfernt (Unity-6-inkompatibel).

## Verkehr
- `TrafficSystem` (1 Tick = 0,25 s Fahrzeit, entkoppelt von der Kalenderzeit): `LaneGraph` (Spuren + Abbiegebeziehungen,
  links abbiegen von linken Spuren …), `Pathfinder` (A* über gerichtete Segmente), IDM-Fahrzeugfolge, Spurwechsel,
  `JunctionController` (Auto-Vorfahrt, rechts vor links, Schilder, Ampelphasen), `TrafficRules` (Spieler-Regeln).
- Fahrten: Bürgerfahrten aus Gebäuden (s. Bürger G2) und Durchgangsverkehr zwischen **Außenanschlüssen**
  (Straßenenden am Kartenrand, `ExternalConnections`). Sackgassen in der Stadt erzeugen keinen Verkehr mehr – außer mit
  `DeadEndsAreExternal` (Testszenarien). Ziele mitten auf Segmenten (`FindPathToSegment`) wandern beim Zerteilen mit.
  Hängende Fahrzeuge verschwinden nach 90 s.

## Roadmap
1. ✅ Fundament: Tick-Simulation, Kamera, freie Straßen (gerade/Kurve), Lärm-Raster + Overlay, Bauhilfen
2. ✅ Straßen
   - ✅ A: Spurmodell + Katalog, Kreuzungen durch Zerteilen, Kreisverkehr, Markierungen, Bauleiste
   - ✅ B: Spurgraph/Abbiegebeziehungen, Ausbauen, Richtung umdrehen, Doppelbau Autobahn
   - ✅ C: Verkehrssimulation (Fahrzeuge, Wegfindung, Spurwechsel, Stau-Ansicht)
   - ✅ D: Verkehrsregeln (Ampeln, Vorfahrt/Stopp, rechts vor links, Abbiegeverbote, Sperrung, Fahrverbote, Tempo)
   - ✅ E: Höhen – Brücken, Trog, Tunnel, Unterirdisch-Ansicht, Autobahn-Vorlagen (Anschlussstelle, Kleeblatt, Dreieck)
   - Später: Spurpfeile frei setzen, Buslinien/Haltestellen, Tramlinien, Abbiegespuren automatisch, bessere Kreuzungsflächen
3. ✅ Terrain: Höhenkarte, Fluss, Straßen formen Gelände, Brücken über Täler/Wasser, Tunnel durch Berge; Store-Modelle
   (Fahrzeuge, Laternen, EU-Ampel, Bäume)
   - ✅ Terraforming (Simulation + Tests, Sperre unter Straßen/Gebäuden) und Werkzeug (Leiste „Gelände“)
   - ✅ Gelände-Farben per Vertex-Farben + eigenem URP-Shader (im Editor noch nicht visuell abgenommen)
   - Später: Kosten fürs Terraforming (Erdmenge ist vorhanden), Bewuchs wächst auf bearbeitetem Boden nach,
     Rückgängig, Gelände unter Brücken/über Tunneln freigeben (mit Durchfahrts-/Überdeckungsgrenze)
4. Gebäude & Wirtschaft (wie Cities: Skylines, aber tiefer – Warenketten als Vorstufe zum Handel). Entscheidungen:
   **einzelne Bürger** als Agenten (Alter, Bildung, Wohnung, Job, Bedürfnisse, echte Fahrten),
   **Strom-/Wassernetze werden gebaut** (Leitungen, Rohre unterirdisch), Zonen in G1: Wohnen (3 Dichten),
   Mischgebiet, Einzelhandel, Büro, Industrie; Dienste/Versorger einzeln baubar, Reichweite übers Straßennetz.
   - ✅ G1 Zonenraster entlang Straßen + Malwerkzeug, Grundstücke, wachsende Gebäude (Store-Modelle), Nachfrage
   - ✅ G2 Bürger, Haushalte, Jobs, Pendeln/Einkaufen (Verkehr aus Gebäuden statt Sackgassen), Außenanbindungen.
     Später: Geburten/Auszug erwachsener Kinder, Schulen (Bildung über G4), ÖPNV statt Platzhalter-Fahrten,
     Zuzug nur über Außenanschlüsse, sichtbare Fußgänger, Lieferverkehr (G5), Zufriedenheit (G6)
   - ✅ G3 Strom & Wasser: Kraftwerke/Pumpe/Wasserturm, Netze (Straßen tragen mit + freie Leitungen/Rohre), Bilanz je Netz,
     Versorgungsstatus, Verlassen bei langem Ausfall, Info-Ansicht. Später: Abwasser, schwankende Erzeugung, Kosten abbuchen
   - G4 Dienste (einzeln baubar, Einflussradius übers Straßennetz): Polizei, Feuerwehr, Gesundheit, Schulen,
     Bibliothek, Parks, Medien (Zeitung, Radio, TV), Abwasser/Kläranlage, Müll (Müllhalde, Verbrennungsanlage)
   - G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
     (Stadtkasse: Einnahmen/Ausgaben, Bilanz halten = Kern der Herausforderung); Industrie-/Wohnformen erweitern
   - ÖPNV & Fernverkehr: Taxi-, Bus-, Zug-Depot, Buslinien, Tram, U-Bahn, S-Bahn, Fernbahn, Flughafen
   - Energie erweitern: Atomkraftwerk (und weitere reale Kraftwerkstypen)
   - Schifffahrt (mit Häfen/Handel): schiffbare Gewässer mit Durchfahrtshöhe; feste Brücken hoch genug oder
     **Klapp-/Zugbrücken** (niedrig, öffnen für Schiffe → Rot/Stau, Umwege) oder Tunnel
   - G6 Bodenwert, Zufriedenheit, Ausbaustufen
5. Welt: fiktive Erde, Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator), Länder mit eigenen Stärken
6. Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte
7. Reich: mehrere Städte (eigene oder NPC), Transport dazwischen, interner Handel
8. Handel & Politik: Bot-Nationen, Marktpreise, Verträge, Regierungen/Bündnisse, externer Handel, Tourismus
9. Multiplayer (Freunde einladen, eigene Städte im eigenen oder fremden Land, asynchron), Online-Handel, Mobile-Port
