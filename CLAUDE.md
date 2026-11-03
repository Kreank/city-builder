# City Builder (Arbeitstitel)

Länder-/Reichs-Aufbausimulation in Unity 6 (URP). Inspiration: Cities: Skylines (freie Straßen,
Umwelt-Overlays), Anno (Produktionsketten, Bedürfnisstufen). Ziel: Start bei 0 → Stadt → Reich aus
mehreren Städten → Handel mit anderen Reichen (Bots, später echte Spieler, asynchron).
Plattform: PC zuerst, Mobile später (deshalb URP mit PC- und Mobile-Pipeline-Asset).

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
    TrafficOverlayView, TrafficPanel, CitizenPanel …) – neue Views dort ergänzen, dann muss die Szene nicht neu erzeugt werden.
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

## Gelände
- `TerrainMap` (Simulation, 4-m-Raster): erzeugt (Hügel bis ~90 m, Fluss, Wasserspiegel 0) oder flach (Tests).
  `RoadNetwork.Ground` = Gelände. **Straßenhöhen sind absolut**, Brücke/Damm/Trog/Tunnel ergibt sich aus der
  Elevation (Höhe über Gelände). Bauhöhe im Werkzeug = Elevation. Ebenerdige Abschnitte (±4 m) formen das Gelände
  (`ConformTo`), Wasser nur per Brücke (≥ 2,5 m über Wasserspiegel).
- `GroundView` zeichnet das Gelände in Chunks inkl. Trog-Löchern und Wasserfläche.
- **Automatische Trassierung** (Bauhöhe 0, `followTerrain`): Segment bekommt ein Höhenprofil (`Profiles`):
  geglättet dem Gelände folgend, Berg > 14 m über der direkten Linie → Tunnel, Tal/Wasser > 10 m darunter → Brücke,
  Steigung begrenzt; Gelände wird bis 6 m Damm/Einschnitt eingeebnet. Profile werden beim Zerteilen/Umbau mitgenommen.
- Knoten mit zwei unterschiedlichen Straßentypen bekommen ein Übergangsstück (`RoadMeshBuilder.NeedsJunctionMesh`).
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
- Darstellung: `CitizenPanel` (Taste C = `RoadTool.Mode.Inspect`): Statistik, Altersbalken, Bildung; Klick auf
  Gebäude zeigt Haushalte/Beschäftigte. HUD zeigt Arbeitslose/Pendler.

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
  (Platzhalter aus Grundkörpern, Masten alle ≤ 45 m mit Seilen, Rohre 2,5 m unter Gelände nur unterirdisch sichtbar),
  `UtilityOverlayView` (O: Strom → Wasser → aus; Marker grün/rot über Gebäuden, wachsen mit der Ausfalldauer),
  `UtilityPanel` (Erzeugung/Bedarf je Netz).
- Später: Abwasser/Kläranlage, Pumpe braucht Strom, Wind/Sonne schwankend (Tageszeit/Wetter), Brennstoff aus Warenketten,
  Stadtkasse (Kosten/Unterhalt abbuchen), Versorgung in Nachfrage/Zufriedenheit (G2/G6), Masten/Rohre folgen späteren
  Geländeänderungen, Hochspannung vs. Ortsnetz, Wasserqualität/Verschmutzung flussabwärts.

## Store-Assets (nicht im Git, `Assets/ThirdParty/`)
Import über Package Manager → My Assets (oder Batch `-importPackage`), danach Menü
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
   - G4 Dienste: Polizei, Feuerwehr, Gesundheit, Bildung, Parks, Müll
   - G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
   - Schifffahrt (mit Häfen/Handel): schiffbare Gewässer mit Durchfahrtshöhe; feste Brücken hoch genug oder
     **Klapp-/Zugbrücken** (niedrig, öffnen für Schiffe → Rot/Stau, Umwege) oder Tunnel
   - G6 Bodenwert, Zufriedenheit, Ausbaustufen
5. Welt: Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator)
6. Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte
7. Reich: mehrere Städte, Transport dazwischen
8. Handel: Bot-Nationen, Marktpreise, Verträge
9. Online-Handel, Mobile-Port
