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

## Entscheidung: Bürgerverhalten (umgesetzt – s. „Bürgerverhalten (Bedürfnisse)“)
- **Spiel-KI im Kern, keine Sprach-KI zur Laufzeit** (bewusst entschieden: keine LLM-„Hauptfiguren“, kein LLM-Erzähler).
- **Utility AI** mit Bedürfnissen (Wohnen, Arbeit/Einkommen, Versorgung, Gesundheit, Sicherheit, Bildung, Freizeit,
  Umwelt): Bürger bewerten Optionen und wählen, was ihre Bedürfnisse am meisten verbessert.
- **Abwechslung statt Uhrwerk:** Persönlichkeit je Bürger (eigene Gewichte), gewichteter Zufall statt immer der besten
  Option, Gedächtnis/Gewohnheiten (z. B. Stau → Bus), Beziehungen (Freunde, Partner, Kinder) und Lebensereignisse.
- **KI nur in der Entwicklung:** Ein Sprachmodell erzeugt Inhalte (Persönlichkeiten, Tagesabläufe, Lebensereignisse,
  Vorlieben, Sprüche), die als Daten mitgeliefert werden – im Spiel keine KI-Kosten, offline lauffähig.
- Simulation bleibt deterministisch (Multiplayer, Replays, Tests).

## Konzept: Wirtschaft, Welt & Handel (Leitlinie für G5, G6, Etappen 5–9)
- **Güter statt Geldströme:** Wirtschaft = physische Güter mit Lagerbestand. Rohstoffe (Getreide, Vieh, Holz, Erz,
  Kohle, Erdöl, Sand/Kies) → Zwischenprodukte (Mehl, Bretter, Stahl, Kunststoff, Glas, Zement) → Endprodukte
  (Lebensmittel, Kleidung, Möbel, Elektronik, Baustoffe, Fahrzeuge). Produktion über **Rezepte** (Eingänge → Ausgänge
  je Tag, Arbeitskräfte je Bildungsstufe, Strom/Wasser). Güterkatalog datengetrieben, erweiterbar.
- **Standort zählt:** Rohstoffgewinnung braucht Vorkommen aus der Welt-/Bodenkarte (Fruchtbarkeit, Erz, Öl, Wald);
  Vorkommen sind endlich bzw. regenerieren (Wald, Boden). Klima bestimmt Ernte und Bedarf (Heizen, Kühlen).
- **Logistik ist Spiel:** Güter liegen in Gebäudelagern und werden per Fahrzeug transportiert (Lkw über das
  `TrafficSystem`, später Zug/Schiff/Flugzeug). Transportkosten = Zeit × Kostensatz. Stau kostet also Geld.
- **Märkte:** je Gut ein **lokaler Preis** aus Angebot/Nachfrage (Lagerbestand gegen Zielbestand, träge angepasst)
  und ein **Weltmarktpreis** an den Außenanschlüssen. Import, wenn lokal teurer als Welt + Transport; Export umgekehrt.
  Firmen (Industrie-/Gewerbegebäude) rechnen mit Erlös − Einkauf − Löhnen → Gewinn steuert Wachstum/Verfall und
  Nachfrage der Zonen (ersetzt pauschale Nachfrage schrittweise).
- **Haushalte:** Einkommen aus Lohn/Rente; Konsum nach **Bedarfsstufen** (wie Anno: Grundbedarf → Komfort → Luxus),
  höhere Stufen erst mit Einkommen + Zufriedenheit; Kaufkraft treibt den Einzelhandel; unerfüllter Konsum senkt das
  Bedürfnis „Einkauf“.
- **Staat:** Steuern (Grund/Einkommen/Gewerbe), später Zölle, Subventionen, Verordnungen; Bodenwert (G6) als
  Spiegel der Standortqualität und Steuerbasis.
- **Welt (Etappe 5):** fiktive Erde mit Regionen (Klima, Relief, Böden, Bodenschätze) → **komparative Vorteile**:
  Länder spezialisieren sich (Tourismus, Rohstoffe, Technik). Jede Stadtkarte ist ein Ausschnitt einer Region.
- **Detailstufen (LOD-Simulation):** die eigene, betrachtete Stadt läuft voll (einzelne Bürger, Fahrzeuge, Lager);
  andere Städte und Länder laufen **aggregiert** (Volkswirtschaft je Stadt: Bevölkerung, Produktion je Gut,
  Bedarf, Lager, Preise) – so bleibt eine ganze Welt rechenbar. Wechselt man in eine Stadt, wird sie detailliert.
- **Handel & Politik (7–9):** Spieler und NPC-Länder nutzen **dieselben Schnittstellen** (Angebote, Aufträge,
  Verträge mit Laufzeit, Liefermenge, Preis). Asynchron: Aufträge haben Lieferzeiten über echte Transportwege.
  NPC-Länder = Spiel-KI (Utility AI wie bei Bürgern, keine Sprach-KI zur Laufzeit).
- Alles deterministisch (geseedeter RNG), Commands für Spieleraktionen (Multiplayer/Replays).

## Entscheidung: Netze werden selbst verlegt (2026-11-03, Spieler)
- **Straßen führen keine Leitungen/Rohre mehr automatisch mit** (löst die bisherige G3-Regel „Stadtstraßen tragen mit“
  ab – Umbau steht an). Der Spieler verlegt **Wasserrohre, Abwasserkanäle** (unterirdisch, Einrasten entlang von Straßen)
  und **Strom** (Hochspannung vom Kraftwerk → Umspannwerk → Ortsnetz entlang der Straßen) selbst.
- Ein Gebäude ist angeschlossen, wenn eine passende Leitung/ein Rohr in Reichweite seiner Vorderseite liegt; es
  bekommt einen **sichtbaren Hausanschluss**. Anschlusslogik als gemeinsamer Baustein für alle Netzarten.

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
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Traffic, Environment, Zoning, Citizens, Utilities, Services, Economy, LandValue, World, Goods, People)
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
- **Bauhilfe Parallelabstand** (`Simulation/Roads/ParallelGuides`, getestet in `ParallelGuideTests`; Anzeige
  `RoadGuidesView`, Einrasten `RoadTool`): Das Zonenraster beginnt an der **Gehweg-Außenkante** (`RoadType.HalfWidth`),
  Reihen à 8 m. Bei nahen Straßen gewinnt die **ältere** (kleinere Segment-Id, volle Reihen), die neuere bekommt nur
  Reihen, deren Zellmitten ≥ `ZoneGrid.CellSpacing` (6,8 m) von fremden Zellen und > `ZoneGrid.RoadClearance` (3,6 m) vom
  Straßenrand liegen (bis 1,2 m Überlappung wird geduldet, darüber fällt die Reihe weg – Rest bleibt ungenutzt).
  **Optimaler Achsabstand** = halbe Breite A + halbe Breite B + (4 + n) × 8 m, n = Reihen der neuen Straße (F9: 4 → 3 →
  2 → 1 → aus; Standard 4 = „Rücken an Rücken“ 64 m). Jedes Vielfache von 8 m ist lückenlos, Stufen unter 4 + 1 sind
  sinnlos, weil die ältere Straße immer zuerst 4 Reihen nimmt (eine „1 Reihe je Seite“-Bebauung gibt das Raster nicht her).
  Beispiele (4 + 4): city_2↔city_2 77 m, city_4↔city_4 84 m, city_4↔city_2 80,5 m, Einbahn 1-spurig↔selbst 74 m,
  Tram↔selbst 83,4 m. `Evaluate` bildet die Raster-Regeln nach (Reihen je Straße, ungenutzt, Überlappung), `Describe`
  liefert „Abstand 77,0 m · optimal (4 + 4 Reihen)“ bzw. „zu eng: X m Überlappung“/„X m Lücke“.
  Werkzeug (gerade/Kurve, Stadtstraßen): gestrichelte Parallelen beidseits der bis zu 3 nächsten ebenerdigen Stadtstraßen
  (gerade auch 80 m über die Enden hinaus), deren Zonentiefe als zarte Fläche mit Reihenlinien, die Vorschau zeigt ihre
  eigene Zonentiefe beidseitig (grün). Mit Einrasten (G, Strg hebt auf) rasten Start, Kontrollpunkt und Ende im Umkreis
  6 m auf die Linien ein; gerade Straßen von einer geraden Hilfslinie aus werden bis 8° Abweichung exakt parallel
  (Länge 8-m-Raster), Kurven zwischen zwei Punkten derselben gekrümmten Hilfslinie bekommen einen ausgeglichenen
  Kontrollpunkt (`FitParallelControl`, 90°-Bogen mit 77 m Abstand < 1 m Abweichung). Abstand + Bewertung stehen in
  `RoadTool.Guide.Info` (Hinweiszeile + Beschriftung). **Blockraster (F10):** Länge gerader Straßen rastet auf
  `BlockLength` = Zellen × 8 m + 2 × (halbe Breite + 1 m) ein (Kreuzungsfläche wird vom Raster ausgespart), Querlinien
  zeigen die Stellen; gilt, wenn der Start eine Kreuzung ist/wird. An Blockecken nimmt die ältere Straße die Eckzellen.

## Gelände
- `TerrainMap` (Simulation, 4-m-Raster): erzeugt (Hügel bis ~90 m, Fluss, Wasserspiegel 0) oder flach (Tests).
  Mit Weltkarte bekommt `Generate` `TerrainParams` der Startregion (Relief-Faktor, Fluss/Breite, Küste; s. „Welt (Etappe 5)“).
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
  Rückgabe `TerraformResult`: geänderte/gesperrte Punkte, bewegte Erdmenge (m³) und Kosten (Stadtkasse), Dirty-Rechteck;
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
  Wegzug/Umzug/Jobwechsel entscheidet seit dem Bedürfnismodell die Utility AI (s. u.); bei Abriss Umzug in freie Wohnung oder Wegzug.
- Tagesablauf: Arbeitsbeginn je Bürger 6:00–9:30, 8 h Arbeit; danach (bzw. tagsüber ohne Job) einkaufen in
  Einzelhandel/Mischgebiet 10–20 Uhr, wenn das Bedürfnis ≥ 1 ist (ein Einkauf versorgt den Haushalt); dann heim.
  Kinder/Schüler bleiben vorerst zu Hause (ab 12 Jahren Freizeit im Viertel, s. Bedürfnismodell).
- Fahrten: Anteil `CarShare` (35 %, Einpendler 60 %) × `TrafficSystem.Demand` fährt als echtes Auto
  (`TrafficSystem.RequestTrip` von Zufahrt/Außenanschluss zu Zufahrt/Außenanschluss), höchstens 70 % von `MaxVehicles`.
  Rest = Platzhalter (zu Fuß/Rad, Fahrzeit aus Luftlinie, 15 km/h) bzw. seit ÖPNV Stufe 1 Bus (s. „ÖPNV“). Ankunft meldet `TrafficSystem.TripEnded`
  (auch bei Abbruch → Bürger kommt trotzdem an; Sicherheitsnetz 3 Tage). Handler dürfen dort keine Fahrten starten.
  **Zeitverzerrung:** Verkehr läuft mit 0,25 s je Tick, die Uhr mit 6 min je Tick – eine Autofahrt dauert daher
  oft Spielstunden bis über einen Tag (wie Cities: Skylines). Der Tagesablauf ist darauf robust, Autofahrer sind länger „unterwegs“.
- Darstellung: Inspektor (Taste C, s. u.) mit Stadtübersicht (Statistik, Altersbalken, Bildung). HUD zeigt Arbeitslose/Pendler.
  Fußgänger (Platzhalter-Fahrten, Park, Laden, Haustür) zeichnet `PeopleView` (s. „Fußgänger“).
- Haushaltsart `Household.Kind` (Single/Paar/Familie/Alleinerziehend/Senioren/sonstige) wird aus dem Alter der
  Mitglieder abgeleitet, nicht gespeichert. Familienstand je Bürger: `Citizen.Relationship`/`PartnerId` (Bedürfnismodell).
- Lesende Abfragen: `CitizenSystem.CitizenForTrip(tripId)` / `VehicleOf(citizen)`, `Citizen.TripStartTick`,
  `PlannedArrivalTick` (Platzhalter-Fahrt), `HasCarTrip`, `BusyUntilTick` (Ende der Tätigkeit); `Vehicle.StartTime` (Verkehrszeit beim Start).

## Bürgerverhalten (Bedürfnisse)
- Dateien in `Simulation/Citizens`: `Needs.cs` (Bedürfnisse, Gewichte je Lebensphase, `ServiceIds`), `Personality.cs`,
  `Memory.cs`, `UtilityChoice.cs` (Softmax), `SimRandom.cs` (eigener geseedeter RNG, ersetzt `System.Random` im
  Bürgersystem), `CitizenSystem.Needs.cs` / `.Decisions.cs` / `.Life.cs` (partial), Inhalte in `Citizens/Content/`.
- **Bedürfnisse** (`NeedKind`, Erfüllung 0..1, 1 = gut): Wohnen (Platz: Personen vs. bequem je Bauart 5/4/3,5; Zustand
  0,55 + 0,12 × Stufe, −0,25 ohne Versorgung), Arbeit (Job 0,8 ± Passung Bildung/Ehrgeiz, Auspendler 0,65, arbeitslos
  0,45 − 0,04/Tag), Versorgung (`Utilities.IsSatisfied` → 1 / 0,05), Einkaufen (1 − ShoppingNeed/1,5), Gesundheit
  (0,65 × `Citizen.Health` + 0,35 × Dienst „health“), Sicherheit (0,15 + 0,85 × (0,6 police + 0,4 fire)), Bildung (Kinder:
  education; Erwachsene: library/education), Freizeit (sinkt ~0,35/Tag, steigt durch Freizeit), Ruhe/Umwelt
  (1 − (1,1 × Lärm + 1,6 × Abgas) ± Müll), Mobilität (Pendel-Luftlinie 0,5–4 km, Stau-/Pendel-Erinnerungen).
  Werte laufen exponentiell zum Zielwert (Rate 0,25–0,6 je Bewertung).
- **Gestaffelt:** je Tick 1/`NeedsSpreadTicks` (24) der Bürger → jeder 10× am Tag. Umfeld je Wohngebäude
  (`HomeContext`: Lärm, Abgas, Versorgung, 7 Dienste) einmal je Runde berechnet und von allen Bewohnern geteilt.
  Leistung (Testprojekt, Info): 20 000 Bürger ≈ 2,5–3 ms/Tick für alle Systeme, Bedürfnis-Staffel ≈ 0,25 ms/Tick.
- **Dienste (G4) über Delegaten**, verbunden in `CitizenServiceBinding.Connect` (aufgerufen in `CreateDefault`):
  `ServiceCoverage(id, building)` → `world.Services.Coverage` (Ids „police“, „fire“, „health“, „education“, „library“,
  „park“, „garbage“, zusätzlich „education_primary/secondary“: Kinder 6–11 bzw. 12–17 nutzen ihre Schulart). Gibt es von
  einer Art noch kein Dienstgebäude, liefert sie neutral 0,5 (Startphase, wie Strom/Wasser). `GarbageLevel` (liegender
  Müll, erst mit Müllabfuhr in der Stadt) senkt Ruhe/Umwelt um bis zu 0,3. `ParkFinder` = bester Park nach
  Fußweg-Wirkung × Auslastung. Rückrichtung: `Services.DemandProvider` ← echte Schülerzahlen (`CountResidents`).
  Abdeckung wird je Bewertungsrunde und Gebäude im `HomeContext` zwischengespeichert. Ohne Bindung (reine Tests): 0,5.
- **Persönlichkeit** (`Personality`, 8 Züge 0..1: gesellig, sparsam, ehrgeizig, ängstlich, naturverbunden,
  lärmempfindlich, familienorientiert, abenteuerlustig): Zuzügler aus einem von 16 Archetypen (± 0,15), Kinder = Mischung
  der Eltern (± 0,18). `NeedWeight(k)` multipliziert die Lebensphasen-Gewichte (bei 0,5 überall ≈ 1).
  `Temperature` = 0,06 + 0,22 × abenteuerlustig + 0,06 × (1 − ängstlich).
- **Zufriedenheit** = Σ (Phase × Persönlichkeit × Erfüllung) / Σ Gewichte. **Gemüt** = 0,5 + 0,5·tanh(0,8·Σ Valenz×Stärke
  der Erinnerungen) + 0,2·(Zufriedenheit − 0,6) − 0,1 krank, geglättet. Je Gebäude: `ResidentSatisfaction(id)`,
  `WorkerSatisfaction(id)` (0,6 Arbeit + 0,4 Mobilität der Beschäftigten), je Haushalt `Household.Satisfaction`.
- **Utility AI** (`UtilityChoice.Choose`, Softmax, Temperatur aus der Persönlichkeit, Zufall nur aus `SimRandom`):
  Freizeit 15–21 Uhr (zu Hause / Park = `Activity.Leisure`: echter G4-Park in Reichweite (`Citizen.LeisureParkId`, Fußweg
  hin/zurück verlängert die Dauer, Qualität = Parkwirkung), sonst Spaziergang im Viertel / Bummeln = Ladenfahrt),
  Verkehrsmittel (`CarProbability`: Grundanteil × Demand × 2 × logistisch(Nutzen Auto) – neutral = alter Anteil; sparsam,
  naturverbunden, Stau-Erinnerung senken), Umzug (alle 5 Tage je Haushalt bei Leidensdruck: bleiben mit Trägheit und
  Abzug für Lärm-/Ausfall-/Unsicher-Erinnerungen vs. bis 6 zufällige freie Wohnungen, `HomeScore`, halbe Temperatur),
  Wegzug (`LeaveProbability` = 0,25 × logistisch(Wegzug − Bleiben); Bleiben aus Zufriedenheit, Gemüt, Jahren in der
  Stadt; Wegzug aus Abenteuerlust, Ausfällen, +0,7 wenn alle Erwerbsfähigen > 10 Tage arbeitslos – ersetzt die alte
  8-%-Regel), Jobwechsel (alle 10 Tage bei schlechter Arbeit/Mobilität, `JobUtility` = Weg + Passung), Jobsuche
  Arbeitsloser = bester `JobUtility` (ohne Würfeln).
- **Gedächtnis** (`MemoryList`, max. 6, gleiche Art + Bezug wird aufgefrischt, schwächste ersetzt; tägliches Abklingen
  je Art, Ängstliche vergessen Schlechtes langsamer): Stau (Autofahrt > 2,2 × freie Fahrzeit oder abgebrochen),
  Strom-/Wasserausfall, Lärm, Abgas, Unsicherheit, schöner Park, Bummel, kein Laden, Lebensereignisse u. a.
- **Lebensereignisse** (Vorlagen in `Content/LifeEvents.cs`: Tageschance, Alter, Zug-Einfluss, Erinnerungen, Meldungen):
  Partnersuche (Singles werden täglich gepaart, kleinerer Haushalt zieht um), Hochzeit, Geburt (Paar mit Frau 20–44,
  ≤ 4 Kinder, Platz zählt), Auszug erwachsener Kinder (18–35, eigene Wohnung per `HomeScore`), Trennung (häufiger bei
  Unzufriedenheit; einer zieht aus oder weg), Krankheit/Genesung (Alter, Abgas, Dienst „health“; krank ab 70 kann tödlich
  sein), Jobverlust, Rente, Tod (Partner verwitwet). `LifeEventRate` skaliert alle Chancen. Ticker: `RecentEvents`.
- **Inhalte als Daten** (`Content/`, keine KI zur Laufzeit): Vornamen w/m je 90, 130 Nachnamen (deutsch gemischt),
  16 Archetypen, 25 Erinnerungsarten × 4–5 Gedankensätze mit Platzhaltern {Name} {Partner} {Kind} {Straße} {Firma}
  {Park}, 13 Ereignisvorlagen × 3 Meldungen, Straßen-/Park-/Firmennamen aus stabilen Hashes (Straßen haben noch keine
  echten Namen). Vorlage wird beim Entstehen deterministisch gewählt; nicht auflösbare Platzhalter → nächste Vorlage.
- **Stadt-Hook:** `PopulationInfo.AverageSatisfaction` → `CitySystem.MoveInPull` ± (Zufriedenheit − 0,6) × 40 (wirkt auch
  auf die Wohn-Nachfrage); Zuzügler meiden Häuser mit Bewohner-Zufriedenheit < 0,45. `Household.Income` (Lohn/Rente je
  Tag, ohne Steuern) für die Wirtschaft.
- **Abfragen für den Inspektor** (im Inspektor verdrahtet): `c.Need(k)` + `Needs.DisplayName/Describe`,
  `c.Personality` (`Describe(gender)`, Indexer je `Trait`), `MemoryTexts(c, list)` / `MemoryText(c, m)`, `c.Satisfaction`
  + `SatisfactionText`, `Concerns(c)`, `c.Mood` + `MoodText`, `c.Health`/`IsIll` + `HealthText`, `FamilyStatusText(c)`,
  `PartnerOf(c)`, `c.FullName`, `ResidentSatisfaction/WorkerSatisfaction(buildingId)`, `Stats.AverageNeeds` u. a.
  Dazu `BuildingCondition(b)`, `SurroundingsScore(b)`, `LeisurePlaceText(c)`, `CountResidents(id, min, max)`.
- **HUD:** `Game/Citizens/CitizenAlerts` (Begleiter am RoadTool) meldet einmal je Spieltag Geburten/Hochzeiten des
  Vortags mit einem Beispielsatz (`HudMessages.Info`, Schlüssel „citizens-daily“).
- Später: Kinder gehen wirklich zur Schule (Tagesablauf), Freundschaften/Nachbarn, Tagesabläufe je Archetyp, Bodenwert/Ausbaustufen
  aus der Zufriedenheit (G6), Einkommen in Wohnwahl/Konsum (G5), ÖPNV statt Platzhalter ✅ (s. „ÖPNV“: Stau → eher Bus). Gleitkomma (`MathF.Tanh`, `Math.Exp`) ist nicht bitgenau über Laufzeiten garantiert –
  für Lockstep-Multiplayer ggf. später Festkomma/Tabellen.

## Fußgänger (sichtbare Bürger)
- **Simulation** `Simulation/People` (rein, getestet in `PedestrianTests`): `SidewalkGraph` = ungerichtetes Gehwegnetz aus dem
  Straßennetz (nur Typen mit Gehweg beidseits; neu gebaut bei `RoadNetwork.Version`). Knoten = Gehweg-Enden je Arm (links/
  rechts vom Knoten weg), Kanten = Gehweg entlang des Segments (`SampledCurve.OffsetAt` mit dem `Offset` der Gehweg-Spur =
  Fahrbahnrand + halbe Gehwegbreite, zwischen den Trimm-Abständen), Gehwegecke (Mitte zwischen Bordstein und Außenkante aus
  `JunctionGeometry.Layout`, im Knick entlang des versetzten Eckstücks), **Querung** (Zebrastreifen am Armende quer über die
  Fahrbahn, Bordstein → Fahrbahnhöhe) nur an Kreuzungsflächen (`NodeShape.Junction`) und Sackgassen-Enden, +6 m Zuschlag.
  Wegfindung A* (Luftlinie). Zugang: `TryAccessAtT` (Seite aus der Lage des Gebäudes), `TryNearestAccess`, `TryAccessAtNode`.
  `PedestrianRoute` = Linienzug mit Höhe (Gehweg +0,2 m, Fahrbahn +0,05 m) und Belag je Punkt.
- `PedestrianPlanner` (je Welt): Endpunkte (Haustür = Zufahrt − 1 m, Park = Tor an der Straßenseite + Luftlinie zur Parkmitte,
  Außenanschluss, freier Punkt), Routen-Cache je Start/Ziel (geleert bei Netzänderung, max. 30 000), `RouteBudget` (neue
  Routen je Frame). `Pose(citizen, time, activitySince)` → `PedestrianPose` (Hidden/Walking/Standing/Waiting, Ort, Höhe,
  Richtung, m/Tick): Platzhalter-Fahrt (kein Auto) linear über die Route zwischen `TripStartTick` und `PlannedArrivalTick`
  (Tempo = Routenlänge / Fahrzeit → Ankunft passt immer zur Simulation); Park: Hinweg (wenn der Beginn beobachtet wurde),
  Platz je Bürger auf der Parkfläche mit kleiner Runde, Rückweg in den letzten `ParkWalkTicks` vor `Citizen.BusyUntilTick`
  (gleiche Formel wie das Bürgersystem); Spaziergang im Viertel = 40 m auf dem eigenen Gehweg hin und her; Einkauf = die
  letzten 3 Ticks vor dem Laden; Ankunft zu Hause = 2 Ticks vor der Haustür. Neu lesend: `Citizen.BusyUntilTick`.
- **ÖPNV-Erweiterungsstelle:** `PedestrianPlanner.Transit` bzw. in Unity `PeopleView.TransitHook`
  (`Func<Citizen, double, TransitPose>`): `TransitPose.WaitingAt(stop, h, facing)` (steht am Haltestellenpunkt),
  `InVehicle` (unsichtbar), `Walk(from, fromPoint, to, toPoint, start, end)` (Fußweg über die Gehwege, Place.None = Punkt).
  **Verbunden** über `TransitPedestrians` (Simulation/People, liest nur `CitizenSystem.RideOf`/`TransitSystem`; `PeopleView`
  nimmt sie, wenn `TransitHook` null ist): Fahrgast wartet → zuerst Fußweg Tür → Haltestelle in **Verkehrszeit** ab
  `Passenger.StartTime` (Gehweglänge ÷ `TransitRules.WalkSpeed` 1,3 m/s, 1 Tick = `TrafficSystem.Dt` 0,25 s → echtes
  Gehtempo), dann Stehen auf dem Gehweg der Halteseite (`WaitPoint`: 0,6 m zum Bordstein, ±2,5 m gestreut, Blick zur Straße;
  `StopSide.Both` → Seite nach Id). Im Bus → unsichtbar. Nach dem Aussteigen Fußweg vom Gehweg des Ziel-Halts zum Ziel in den
  letzten Ticks vor `PlannedArrivalTick` (Formel wie `CitizenSystem.OnRideEnded`: Luftlinie ÷ 1 500 m/Tick, + 3 außerhalb).
  Die Fahrt entfernt das Bürgersystem beim Aussteigen – `TransitPedestrians.Observe` merkt sie sich je Frame. Statistische
  Fahrgäste (`TransitRealShare`) und unbeobachtete Fahrten: unsichtbar.
  **Zeitbasis im Bürgersystem** (nur dokumentiert, nicht umgebaut): `TransitSystem.StartRide` lässt sofort warten (Fußweg zur
  Haltestelle ohne Dauer; die Planung `TransitOption.AccessMeters`/`PerceivedSeconds` rechnet ihn aber in Verkehrszeit) – die
  Darstellung zeigt ihn trotzdem in Verkehrszeit; wer einsteigt, bevor er sichtbar ankommt, verschwindet unterwegs. Dauer in
  Spielticks (Zeitraffer) haben: Platzhalter-Fußwege (`CitizenSystem.StartTrip`: `dist / OtherMetersPerTick`), Fußweg vom
  Ziel-Halt (`OnRideEnded`: `rest / OtherMetersPerTick`) und Parkwege (`TryLeisure`: `walk`). Für echtes Gehtempo müssten diese
  drei Stellen in Verkehrszeit rechnen (z. B. Ankunft über `traffic.Time` + Strecke ÷ 1,3 m/s statt `ArriveTick`), bzw.
  `StartRide` erst nach dem Zugangsweg in die Warteschlange stellen.
- **Darstellung** `Game/People/PeopleView` (Begleiter am RoadTool): je Frame alle Bürger mit billiger Vorprüfung (Start/Ziel
  in Reichweite), dann Pose, Sichtkegel, bis `FarDistance` 520 m (Kamera höher → nichts), höchstens `MaxVisible` 3 000 (nächste
  zuerst); bis `NearDistance` 170 m volle Figur (Beine/Arme schwingen um Hüfte/Schulter, Wippen, Senioren gebeugt, Schatten),
  darüber 2 Quader ohne Schatten. Neue Routen höchstens 24 je Frame. `Graphics.RenderMeshInstanced`, ein Material je Farbe.
  Seitliche Streuung ±0,85 m (auf Zebrastreifen ±0,5 m), Schrittfrequenz in Echtzeit. Freizeitbeginn beobachtet die View
  (Wechsel in `Activity.Leisure`), die Simulation speichert ihn nicht.
- **Figuren** `PeopleFigures`: Platzhalter aus Quadern, 1,75 m (Kleinkind 0,55×, Kind 0,74×, Jugendliche 0,93×, Kopf relativ
  größer), Farben nach Bürger-Id (Haut 5, Haare 5 bzw. grau/weiß bei Senioren, Oberteil 12 / Kinder kräftig / Senioren gedeckt,
  Hose 6). Eigene Modelle (`OwnModelCatalog.People`, s. „Eigene Assets“) ersetzen die Platzhalter: je Gruppe zufällig nach Id,
  fehlt die Gruppe, Erwachsene skaliert.
- **Inspektor:** Fußgänger anklickbar (`InspectorPicking`: Kugel um die zuletzt gezeichnete Körpermitte, Vorrang wie Fahrzeuge)
  → Bürger; Ring um die Person und Verfolgen (`PeopleView.TryGetPosition`).
- **Zeitverzerrung:** 1 Tick = 6 Spielminuten = 0,1 s Echtzeit bei Tempo 1. Platzhalter-Fußwege dauern meist 1–2 Ticks →
  Fußgänger laufen sichtbar im Zeitraffer (z. B. 600 m in 0,2 s). Deutlich länger sichtbar sind Park, Spaziergang, Laden, Tür.
  Echt wirkende Gehgeschwindigkeit bräuchte Fußwege in Verkehrszeit (wie Autos) im Bürgersystem – offen.
- Leistung (Testprojekt, Info): Raster 13×13 Straßen (312 Segmente) → Gehwegnetz 17 ms, 500 Routen 75 ms (inkl. JIT).
  Unity noch nicht gestartet (Optik, Frame-Zeiten ungeprüft).

## Spiel-UI (UI Toolkit, Phase 1)
- `Game/UI/Hud/GameHud` (+ `GameHud.BuildBar.cs`), Begleiter am `RoadTool` (zuletzt angehängt). Erzeugt zur Laufzeit
  `PanelSettings` (ScaleWithScreenSize, Referenz 1920×1080, Match 0,5) + `UIDocument`; lädt
  `Resources/UI/CityTheme.tss` (importiert nur `unity-theme://default` → Standardschrift) und `Resources/UI/CityHud.uss`
  (Design-Token als `--cb-…`-Variablen, Klassen `cb-…`). Aufbau in C#, weil die Inhalte aus Katalogen kommen.
  Fehlen Theme/USS, hängt das RoadTool die alte IMGUI-`RoadToolbar` als Rückfall an.
- Inhalt: obere Leiste (Stadtname-Platzhalter `GameHud.CityName`, Datum mit Anzeige-Kalender 12×30 Tage, Tempo,
  Einwohner, Arbeitslose, Strom-/Wasser-Warnchip nur bei unterdecktem Netz, Info-Modus), linke Ansichtsleiste
  (Stau, Lärm, Abgas, Strom, Wasser, Dienste-Abdeckung, Abwasser, Wasserqualität, Medien, Unterirdisch), Bauleiste (Kategorien → Werkzeugzeile + Kacheln + Infozeile),
  Hinweiszeile aus `RoadTool.Guide` (Fehler/Info/Länge/Kosten), Tooltips, Toasts.
- Knöpfe rufen dieselben öffentlichen Methoden wie die Tasten (RoadTool, Werkzeuge, Views, `SimulationRunner.TogglePause`);
  Zustände werden jedes Frame aus den Quellen gelesen → Tasten und Knöpfe bleiben synchron. Die Kategorie folgt dem Modus.
- Icons: `HudIcon` zeichnet per Painter2D (24er-Raster, Farbe = `color`), keine Bilddateien.
- Mausblockade: `UiBlocker.RegisterToolkitRoot` → `IsPointerOverUi` prüft zusätzlich `panel.Pick`. Reine Layout-Container
  haben `PickingMode.Ignore`, nur echte Flächen blockieren. IMGUI-Fenster richten sich nach `HudLayout` (Top/Bottom/LeftInset).
- Meldungen: `HudMessages.Info/Warning(title, text, icon, key)` – gleicher `key` ersetzt die alte Meldung.
  Quellen bisher: `Game/Utilities/UtilityAlerts` (Netz unterdeckt/wieder ausreichend, Gebäude verlassen, Leitung durch Bau abgerissen).
- Schrift: Manrope/Inter geplant, noch nicht im Projekt – Tausch über `-unity-font-definition` in `.cb-root`/`.cb-heading`.
- Noch IMGUI: nur die Rückfall-`RoadToolbar` und die Rückfall-Fenster (s. Phase 2); neue IMGUI-Fenster anderer Agenten
  (Wirtschaft F5, Weltkarte F4) sind auf den Baukasten umgezogen, ihr IMGUI bleibt Rückfall.

## Spiel-UI Phase 2 (Fenster)
- **Baukasten** `Game/UI/Panels`: `Ui` (Sofort-Modus: Fenster beschreiben ihren Inhalt bei jeder Aktualisierung neu,
  Elemente werden nach Stelle + Art **wiederverwendet** → Hover/Klick/Scroll bleiben erhalten), Widgets in `Ui.Widgets.cs`:
  Behälter `Card/Fields/Grid(2|3)/Chips/Actions/Row/Col` (für `using`), `Section`, `Text`, `Note`, `Title`, `Field`,
  `FieldLink` (anklickbar), `TotalRow`, `Columns`, `Stat` (Kennzahl-Kachel), `Progress`, `Meter`, `Need` (Bedürfnisbalken),
  `ValueBar`, `WeekBars` (Wochen-Saldo), `Chip`, `ListItem` (Avatar + Titel + Badge, anklickbar), `Quote`, `Message`,
  `Tabs`, `ActionButton`, `IconButton`, `Stepper` (−/+), `Slider`, `Pager`; Farbtöne über `Tone`.
  `GameWindow` (Basis: `OpenScore`, `Close`, `HandleEscape`, `DrawHead`/`DrawBody`, `Tick`/`Poll`, Kopf-Helfer `Head(...)`),
  `WindowHost` (in `GameHud.Windows`; höchstens ein rechtes Panel + ein mittiges Fenster, höchster `OpenScore` gewinnt,
  mittig verdrängt das neue das alte; Esc: mittig schließen, sonst `HandleEscape` des rechten Panels; 5×/s + nach Klick neu),
  `WorldLabels` (Welt-Beschriftungen per `Begin/Add` je Frame, Ebene in der HUD), `UiPanels` (Schalter).
  **Neues Fenster:** Klasse von `GameWindow` ableiten (Beispiel im Doc-Kommentar), `hud.Windows.Add(new …())`. USS-Abschnitt
  „Fenster (Phase 2)“ in `CityHud.uss`.
- **Fenster** (`Game/UI/Windows`, Inspektor in `Game/Inspect/InspectorWindow*.cs`): Inspektor (rechts, Info-Modus),
  Verkehrsregeln (rechts, Modus T, Zufahrt-Buchstaben als Welt-Beschriftung), Versorgungsnetze (rechts, Werkzeug J/Ansicht O),
  Dienste (rechts, Werkzeug 4/Ansicht ⇧4), Stadtkasse (mittig, M bzw. Klick auf den Kontostand), Hilfe & Tasten (mittig, F1 bzw.
  „?“ oben) inkl. der Entwickler-Statistik. Welt-Beschriftungen von RoadGuidesView/UtilityTool/ServiceTool gehen bei aktiver
  neuer UI über `WorldLabels`. Mausrad über Fenstern zoomt die Kamera nicht mehr.
- **Rückfall:** Startet die neue UI nicht (Theme/USS fehlt) oder `UiPanels.ForceImgui`, zeichnen BudgetPanel, TrafficPanel,
  UtilityPanel, ServicePanel und DebugHud wie früher per IMGUI (Aktionen wie Steuern/Verkehrsregeln bleiben so bedienbar).
  Die IMGUI-Darstellung des Inspektors ist entfernt (rein lesend, sonst doppelte Pflege); seine Logik bleibt in `InspectorPanel`.
- G6 im HUD: Inspektor zeigt Bodenwert, Stufe + Fortschritt + „was fehlt“, Zustand (Übersicht) und alle Bodenwert-Faktoren mit
  Anforderungen der nächsten Stufe (Umfeld); linke Leiste Knopf „Bodenwert“ (F2); `UI/Hud/LandValueAlerts` meldet Aufstiege
  und Verfall gesammelt höchstens 1× je Spieltag.
- Welt im HUD: `UI/Windows/WorldMapUiWindow` (mittig, breit, F4 oder Globus-Knopf oben; Textur über
  `WorldMapWindow.TextureFor(mode)`, Umschalter Biome/Relief/Länder/Temperatur/Niederschlag/Rohstoffe (+ 7 Vorkommen),
  Hover = Zelleninfo, Klick wählt ein Land: Hauptstadt, Primär-/Sekundärstärke, Bevölkerung, BIP, Saldo je Güterkategorie;
  das IMGUI-Fenster bleibt Rückfall). Linke Leiste „Rohstoffe“ (F3) mit Untermenü der 7 Raster (`ShowResource`), Name in der
  Ansicht-Anzeige. Obere Leiste: Jahreszeit, Tagestemperatur (+ Niederschlag) aus `world.Climate.Today` hinter dem Datum.
  Inspektor: „Vorkommen“ (`DepositAt`) im Gebäude-Reiter Umfeld, beim Versorger und in der Stadtübersicht unter dem Mauszeiger.
  Baukasten: `Ui.Image(texture, w, h, onClick, onHover)`, `GameWindow.FrameClass` (z. B. `cb-window--wide`).
- Optik ungetestet (Unity nicht gestartet); unsicher: Scroller-Klassen, Prozent-`translate` der Welt-Beschriftungen.

## Inspektor (Info-Modus, Taste C)
- `Game/Inspect/InspectorPanel` (Begleiter am `RoadTool`, ersetzt das frühere `CitizenPanel`), `InspectorPicking`.
  Liest nur. Klick = Strahl gegen Gebäude (Grundstück × `VisualHeight`), Versorgungsbauwerke (Fläche × grobe
  Platzhalterhöhe), Fahrzeuge und Fußgänger (Kugel um die interpolierte Position, kleiner Vorrang vor Gebäuden); Rückfall:
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
  **Dienstgebäude** (G4, `SelectionKind.Service`): Dienstart, Kapazität, Auslastung/Überlastungsfaktor, versorgte Gebäude,
  Reichweite/Zufahrt, Lager, Kosten/Unterhalt, Lärm. Gebäude-Reiter Umfeld: **Dienste in Reichweite** = Abdeckung je
  Dienstart (Bildung mit Grund-/Oberschule), Müll am Gebäude, Brandrisiko.
- **Bedürfnismodell im Inspektor:** Bürger mit Namen, Familienstand + Partner (anklickbar), Persönlichkeit/Charakterzüge;
  „Befinden“: Gesundheit/krank, Zufriedenheit, Gemüt, Sorgen, Bedürfnisbalken (Erfüllung rot→grün), Erinnerungen als
  Sätze, Rohwerte + Haushaltseinkommen; Freizeit „im Park“/„Spaziergang im Viertel“. Gebäude: Zustand, Zufriedenheit
  Bewohner/Mitarbeiter/Umfeld, Bodenwert/Stufe/Zustand (G6). Darstellung seit Phase 2: `InspectorWindow` (rechtes Panel).
  Noch offen: Zustand von Bauwerken (Versorger/Dienste).

## Strom & Wasser (G3)
- `Simulation/Utilities`: `UtilityCatalog` (Kohle-/Gaskraftwerk, Windrad, Solarpark, Wasserpumpe am Ufer, Brunnen+Wasserturm;
  Leistung Strom kW / Wasser m³/h, Baukosten/Unterhalt bucht die Stadtkasse ab, s. u.), `UtilitySystem` (alle 30 min).
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
- Später (Abwasser/Atom: s. „G3 erweitert“): Pumpe braucht Strom, Wind/Sonne schwankend (Tageszeit/Wetter), Brennstoff aus Warenketten,
  Versorgung in Nachfrage/Zufriedenheit (G2/G6), Masten/Rohre folgen späteren
  Geländeänderungen (Terraforming zu Wasser unter einem Mast wird noch nicht geprüft), Hochspannung vs. Ortsnetz,
  (Wasserqualität: erledigt, s. „G3 erweitert“). **Mittelinsel:** `RoadCatalog` hat noch keinen Typ mit Mittelstreifen
  (`LaneKind.Median` existiert im Builder, wird aber nicht benutzt) → Straßen mit Mittelstreifen und Masten darauf
  (dann ohne Platzverbrauch neben der Straße) später. Ebenso: Masten unter hohen Brücken erlauben, Masten mehrerer
  Leitungen am selben Punkt zusammenlegen.

## Abwasser, Wasserqualität & Atomkraftwerk (G3 erweitert)
- **Abwasser** = dritte Netzart `UtilityKind.Sewage` im `UtilitySystem` (Status `HasSewage`/`TicksWithoutSewage`). Gebäude
  erzeugen 0,9 × Wasserbedarf (`UtilityCatalog.SewageShareOfWater`); Abnahme durch **Kläranlage** (500 m³/h, klärt voll) und
  **Abwasser-Auslass** (1500 m³/h, billig, `Untreated`, muss ans Ufer). Startphase wie Strom/Wasser: erst verlangt, wenn es eine
  Kläranlage/einen Auslass gibt; dann fließt es in `IsSatisfied` (Versorgung, Zustand, Stufen) ein; verlassen nach 4 Tagen
  (`SewageAbandonAfterTicks`). **Kanäle werden selbst verlegt** (Entscheidung des Spielers): Straßen führen sie nicht mit.
- **`ConnectionRules`** (wiederverwendbarer Baustein, bisher nur Abwasser – Wasser/Strom folgen): `CarriedByRoads(kind)`,
  Gebäude angeschlossen, wenn ein Kanal ≤ `BuildingReach` 12 m von der Zufahrt (`Building.Entrance`) verläuft (auch mittendrin);
  Leitungen/Bauwerke verbinden sich weiter über Enden (3 m). `UtilitySystem.ConnectedLine(b, kind)`, `HouseConnection(...)` für
  den sichtbaren Hausanschluss (`UtilityView`, unterirdisch, braun, 1,5 m unter den Wasserrohren).
- **`WaterQualitySystem`** (`world.WaterQuality`, nach dem `UtilitySystem`): je Kanalnetz geklärt (Kläranlagen zuerst, anteilig)
  bzw. ungeklärt über Auslässe. `Pollution` (`GridLayer`, Umwelt-Gitter, 0 = sauber): Auslass gibt `PollutionPerCubicMeter`
  (0,015) × m³/h auf Wasserzellen ≤ 40 m; Transport **nur flussabwärts** (`TerrainMap.FlowDirection`, Festlegung −y bzw. zum Meer,
  weil der Fluss kein Gefälle hat): 60 % zur nächsten Wasserzelle stromab (sonst schräg), 4 % je Seite quer, 1 % Selbstreinigung.
  `PumpQuality(f)` = 1 − Verschmutzung im Ansaugbereich, je Wassernetz nach Leistung gemischt (Brunnen = 1) →
  `DrinkingWaterQuality(b)`. Bodenwert: Wasserlage × (1 − 1,5 × Verschmutzung) (`LandValueSystem.WaterPollutionNear`).
- **Atomkraftwerk** (`UtilityCatalog.NuclearPlant`): 200 MW, 400 000 € / 4 500 €/Tag, Kühlwasser (am Ufer, aber trockene ebene
  Grundfläche: `DryFootprint`), kein Abgas, `ConcernRadius` 1200 m → `UtilitySystem.ConcernAt(p)` (voll bis 30 %).
  **Störfall** (Entscheidung: an, aber harmlos): `IncidentChancePerDay` 0,2 % je Tag (stabiler Hash aus Id + Tag, kein
  gemeinsamer Zufall), 3 Tage Abschaltung (Leistung 0), Event `NuclearIncident`, Medienmeldung; keine Sperrzone.
  `IncidentsEnabled` schaltet ab, `ForceIncident` für Tests.
- **Bürger** (Hooks in `CitizenSystem.Needs`, verbunden in `CitizenUtilityBinding.Connect`, alle null = neutral):
  `DrinkingWaterQuality` → Gesundheit −0,4 × (1 − Qualität), Erinnerung `BadTapWater` unter 0,8; `NuclearConcern` → Ruhe −0,3 ×
  Sorge × ängstlich, Erinnerung `NuclearFear` ab Ängstlichkeit 0,4; fehlendes Abwasser → `SewageBackup`. Ohne Zufall (RNG unverändert).
- Darstellung: Werkzeug „Abwasserkanal“ (`UtilityTool.UtilityAction.SewagePipe`, unterirdisch), Kacheln Atom/Kläranlage/Auslass,
  O schaltet Strom → Wasser → **Abwasser** → aus, linke Leiste Knöpfe „Abwasser“ und „Wasserqualität“ (`EnvironmentOverlayView.
  Overlay.WaterQuality`, nur Verschmutzung sichtbar), Kanal-Chip oben, Inspektor: Abwasser/Trinkwasser/Atom-Sorge am Gebäude,
  geklärt/eingeleitet, Pumpen-Qualität, Akzeptanz/Betrieb am Bauwerk. Keine neuen Tasten.
- Später: Kläranlage braucht Strom, Klärschlamm, Regenwasser/Überlauf, echtes Gefälle/Fließrichtung aus dem Gelände,
  Fischsterben/Tourismus, Grundwasser-Verschmutzung, Wasser/Strom auf `ConnectionRules` umstellen (Hochspannung → Umspannwerk → Ortsnetz).

## Medien (G4 erweitert)
- Dienstart `ServiceKinds.Media` („media“, auch in `ServiceKinds.All`/Overlay): **Zeitungsverlag** (Zustellung übers Straßennetz,
  3 min, braucht Zufahrt), **Radiosender** (Funkradius 900 m) und **TV-Sender (Turm)** (1600 m) – `ServiceType.Broadcast` (technisch
  `Walking` = Luftlinie, keine Zufahrt). Kapazität = Leser/Hörer/Zuschauer, Bedarf = Einwohner, Kosten wie alle Dienste (Kasse).
- Bürger: `MediaReach` (ohne Medienhaus 0, nicht 0,5) → Freizeit +0,004 je Bewertung, Bildung Erwachsene +0,08;
  `NewsTone` → Gemüt ± 0,05 × Reichweite × Stimmung (vorsichtig dosiert).
- **`MediaSystem`** (`world.Media`, nach den Diensten): erkennt Ereignisse aus echten Daten – Stau (≥ 6 stehende Fahrzeuge je
  Segment), Strom-/Wasser-/Abwasserausfall (≥ 3 Gebäude), Steuersatz rauf/runter, Eröffnung Schule/Gesundheit/Park/Polizei/
  Feuerwehr/Medium, Atomkraftwerk gebaut/Störfall, Trinkwasser < 80 %, Zufriedenheit ± 4 Punkte zum Vortag, Einwohnermarken,
  Arbeitslosigkeit ≥ 12 %. Vorlagen in `Services/Content/Headlines.cs` (Platzhalter {Straße} {Anzahl} {Steuer} {Satz} {Name}
  {Einwohner} {Prozent} {Medium}, Ton je Thema, Medienhäuser „Stadt-Kurier“, „Radio Welle 98“, „Stadt-TV 7“).
  **Ohne Medien (bzw. Reichweite < 1 %) keine Schlagzeilen.** Sperrfrist 2 Tage je Thema/Bezug, max. 2 je Durchlauf,
  `Recent` (max. 40, neueste zuerst), `Published`, `CurrentTone` (2 Tage gewichtet). HUD: `Game/Services/MediaAlerts` →
  `HudMessages` (schlechte Nachrichten als Warnung). Linke Leiste Knopf „Medien-Reichweite“.
- Später: Nachrichtenfenster/Ticker in der HUD, Kampagnen/Werbung, Medien beeinflussen Wahlen/Politik, Klatsch aus Lebensereignissen.

## Dienste (G4)
- `Simulation/Services`: `ServiceCatalog` (Polizeiwache, Feuerwache, Klinik, Krankenhaus, Grundschule, Gymnasium/Oberschule,
  Bibliothek, Park klein/groß, Müllhalde, Verbrennungsanlage – Grundfläche, Kapazität je Tag (Personen/Schüler/t Müll),
  Reichweite, Baukosten, Unterhalt **je Woche**, Lärm, Abgas, Lager), `ServiceSystem` (alle 30 min, `world.Services`).
- **Verbindliche Abfrage-API** (andere Systeme docken nur so an): `world.Services.Coverage(string kind, Building b)` und
  `CoverageAt(string kind, Vector2 p)` → 0..1, Arten als `ServiceKinds.Police/Fire/Health/Education/Library/Park/Garbage`.
  `Education` = Mittel aus `EducationPrimary`/`EducationSecondary` (auch einzeln abfragbar). Unbekannte Art → 0.
  Weitere: `GarbageAt/GarbageLevel(b)`, `FireRisk(b)`, `AverageCoverage(kind)`, `DemandOf(b, channel)`;
  `DemandProvider` (Func) erlaubt echten Bedarf (z. B. Schülerzahlen aus dem Bürgersystem) statt Schätzung.
- **Reichweite über das Straßennetz**: Dijkstra über Fahrzeit vom Anschlusspunkt (nächste ebenerdige Stadtstraße, Rand
  ≤ 8 m von der Grundfläche – sonst `NoRoadAccess`); Tempolimit × 0,8, +5 s je Kreuzung (≥ 3 Straßen), Einbahnstraßen nur
  in Fahrtrichtung, Autobahnen/Rampen befahrbar. Gebäude werden an ihrer Zufahrt (`FrontSegmentId`/`FrontT`) erreicht.
  Wirkung 1 bis 40 % der Reichweite, dann linear auf 0. Ohne Straßenverbindung (z. B. Fluss ohne Brücke) keine Wirkung.
  **Parks** wirken über Fußweg-Radius (Luftlinie zur Parkfläche): Spaziergänger kürzen über Wege/Grundstücke ab, die das
  Straßennetz nicht kennt; Parks brauchen keine Zufahrt.
- **Abdeckung** = max über alle Dienste des Kanals (Entfernungswirkung × min(1, Kapazität/Belastung)). Jedes Gebäude
  belastet den Dienst mit der besten Wirkung (Bedarf × Wirkung). Bedarf aus der Belegung (`Residents`, `Workers`);
  Schüler geschätzt 8 %/7 % der Einwohner. Neu gerechnet: Reichweiten nur bei Straßen-/Dienst-Version, Zuordnung zusätzlich
  bei Stadt-Version, Last/Abdeckung je Durchlauf (billig); `CoverageVersion` für die Darstellung.
- **Direkte Folgen** (keine Bürgerwirkungen – Gesundheit/Kriminalität/Bildung baut das Bedürfnissystem über die API):
  Müll (Einwohner 10 kg/Tag, Arbeitsplätze 5 kg, Industrie 20 kg – Spielmaßstab) stapelt sich am Gebäude, abgeholt wird
  Abdeckung × höchstens 4× die laufende Menge (Rückstau baut sich ab); Müllhalde läuft voll (25 000 t → wirkt nicht mehr).
  `FireRisk` = Grundrisiko je Zone × (1 − 0,8 × Feuerwehr) + 0,2 × Müllstand – noch ohne Brände.
- Platzregeln wie G3: nicht auf Straße/Wasser/anderen Bauwerken/Strommasten, max. 8 m Gefälle (wird eingeebnet), Gebäude
  darunter werden abgerissen, Straße durchs Dienstgebäude reißt es ab. Grundstücke (`CitySystem.LotBlocker`, verkettet mit
  G3) und Terraforming (`TerrainMap.LockQuery`, verkettet) sind auf Dienstflächen gesperrt. Versorger/Masten dürfen nicht
  auf Dienstflächen (Hooks `UtilitySystem.OtherStructureOverlaps/-At`).
- **Kosten (Stadtkasse)**: `ServiceSystem` ist `IUpkeepSource` (`Name` „Dienste“, `WeeklyUpkeep()` = Summe der
  Katalogwerte je Woche) und wird in `CreateDefault` per `world.Economy.RegisterUpkeep` angemeldet. Baukosten über
  `Budget` (`IConstructionBudget`): `Plan` → `ServiceBuildError.InsufficientFunds` (nach den Geometrieregeln), `Build`
  bucht unter `BudgetCategory.ServiceConstruction` („Bau Dienste“). Spieler-Abriss `Demolish` = 25 % zurück
  (`EconomyRules.DemolitionRefundShare`); internes `Remove` (Straße durchs Gebäude) bringt nichts.
- Darstellung (`Game/Services`, Begleiter des `RoadTool`, Modus `RoadTool.Mode.Service`): `ServiceTool` (**Taste 4** –
  einziger freier Buchstabe wäre Y, das Input System liest aber US-Lage, auf QWERTZ also die Taste „Z“; Bauleiste
  „Dienste (4)“ über `DrawToolbarRows`, `,`/`.` drehen, Vorschau färbt erreichte Straßen nach Wirkung grün→rot, Parks mit
  Ring), `ServiceView` (Platzhalter aus Grundkörpern, Müllsäcke an Gebäuden mit Müllstau), `ServiceOverlayView`
  (**⇧4** schaltet Polizei → … → Müll → aus; Abdeckung grün→rot über Gebäuden; im Werkzeug automatisch die gewählte Art),
  `ServicePanel` (IMGUI, Abdeckung Ø je Art, Auslastung je Gebäude). **Neue HUD**: Kategorie „Dienste“ (Taste 4) in
  `GameHud.BuildBar` mit Kacheln aller `ServiceCatalog`-Gebäude, Werkzeugzeile (Drehen, Ansicht durchschalten ⇧4) und
  Abriss-Kachel „Dienstgebäude“; linke Ansichtsleiste Knopf „Dienste-Abdeckung“ (`ServiceOverlayView.Cycle`),
  Icon `HudIconKind.Service`. Die IMGUI-Zeilen `ServiceTool.DrawToolbarRows` bleiben für die Rückfall-`RoadToolbar`.
- **Eigene Modelle**: FBX unter `Assets/_Project/Models/Services/<Name>.fbx` werden automatisch übernommen
  (`OwnModelCatalogBuilder.ServiceFiles` → `OwnModelCatalog.Services`): PoliceStation, FireStation, Clinic, Hospital,
  PrimarySchool, HighSchool, Library, ParkSmall, ParkLarge, Landfill, Incinerator (Konvention wie bei den Versorgern).
- Später: Brände/Kriminalität als Ereignisse, Dienstfahrzeuge im Verkehr (statt Dijkstra-Schätzung), Sperrungen/
  Abbiegeverbote in der Reichweite, Müllhalde leeren/Recycling, (Medien: s. „Medien (G4 erweitert)“),
  Budgetregler je Dienst, Ausbaustufen.

## Bodenwert & Ausbaustufen (G6)
- `Simulation/LandValue`: `LandValueRules` (alle Formeln/Zahlen, Platzhalter fürs Balancing), `LandValueSystem`
  (`world.LandValue`, alle 30 min, nach Diensten und vor der Kasse) + `LandValueSystem.Levels.cs` (Stufen, Zustand).
- **Bodenwert** 0..1 je Zelle im Raster `LandValue.Layer` (`GridLayer`, gleiches 16-m-Gitter wie Lärm/Abgas):
  Grundwert 0,10 + Straßenanbindung 0,18 (voll bis 24 m, 0 ab 96 m) + Arbeitsplätze in 800 m 0,14 (Sättigung
  1 − e^(−Jobs/250), „Nähe Zentrum/Jobs“) + Außenanschluss 0,04 (Luftlinie, 0 ab 2,5 km) + Dienste 0,25 (Polizei 0,06,
  Feuerwehr 0,05, Gesundheit 0,05, Bildung 0,06, Bibliothek 0,03; Gebäude auf der Zelle → `Services.Coverage`, sonst
  `CoverageAt` nur mit Straße ≤ 96 m) + Parks 0,12 + Wasserlage 0,08 (Ufer ≤ 72 m) + Aussicht 0,05 (Höhe über dem
  Mittel im 150-m-Ring) + Dichte 0,06 + Nachbarschaft ±0,10 (Zufriedenheit der Bewohner/Beschäftigten in 160 m, 0,6
  neutral) − Lärm 0,25 × − Abgas 0,35 × − Müll 0,20 × Müllstand (`Citizens.GarbageLevel`, also erst mit Müllabfuhr);
  Wasserzellen 0. Ergebnis begrenzt auf 0..1.
- **Gestaffelt & träge:** je Durchlauf 1/48 der Zellen → jede Zelle einmal am Spieltag; Wert läuft um 35 % zum Ziel
  (erste Berechnung direkt). Hilfsraster: Straßenabstand (fein, bei `Roads.Version`), Autobahnnähe sowie Jobs, Dichte,
  Zufriedenheit, Müll (grob 64 m, je Runde aus den Gebäuden gestempelt); Gelände (Wasser/Ufer/Aussicht) je Zelle
  zwischengespeichert, verworfen über `TerrainMap.Changed`. Kosten: alle 16 384 Zellen ≈ 8 ms (Debug), ≈ 0,2 ms je Durchlauf.
  `ForceRecalculate()` rechnet alles sofort und ungeglättet (Tests, Laden).
- **Ausbaustufen** 1–3 (`Building.Level`, täglich): Aufstieg, wenn Bodenwert, Zufriedenheit (Bewohner bzw. Beschäftigte),
  Versorgung (`Utilities.IsSatisfied`), Bildung/Fachkräfte (Wohnen: Erwachsene ≥ mittlerer Abschluss; Büro: Beschäftigte
  mit Hochschule; Handel/Industrie: ≥ mittlerer Abschluss), Belegung ≥ 75 % und Zustand ≥ 0,5 erfüllt sind –
  netto 5 Tage (Stufe 2) bzw. 8 Tage (Stufe 3); erfüllt +1, sonst −2 Tage. Anforderungen je Zone in
  `LandValueRules.RequirementFor` (Bodenwert Wohnen 0,40/0,60, Büro 0,45/0,62, Handel 0,40/0,58, Industrie nur 0,25/0,40).
  Aufstieg setzt den Zustand auf 1 (Modernisierung). Kapazität × 1,0/1,4/1,8 (`HouseholdsFor/JobsFor`, Stufe 1 = alte
  Formel) → mehr Wohnungen/Jobs und damit mehr Strom-/Wasserbedarf und Müll. Abstieg nach netto 10 schlechten Tagen
  (Bodenwert 0,12 unter der eigenen Stufe, Zufriedenheit < 0,4 oder keine Versorgung). Überbelegung nach Abstieg baut sich
  über Umzug/Wegzug ab (es zieht niemand nach). Änderung → `CitySystem.NotifyBuildingChanged` (Version++, `BuildingChanged`).
- **Zustand** `Building.Condition` 0..1 (täglich): −0,002 Alterung, −0,04 ohne Versorgung, −0,03 × Müllstand, −0,01 bei
  Leerstand > 10 Tage, −0,01 bei Zufriedenheit < 0,35; +0,012 Instandhaltung, wenn versorgt, kaum Müll, belegt, zufrieden
  (≥ 0,55) und Bodenwert ≥ 0,25. Zustand 0 → `BuildingDecayed`, Abriss (`DemolishDecayed`). Das Bürgersystem nutzt ihn:
  `CitizenSystem.BuildingCondition(b)` = Wohnqualität (0,55 + 0,12 × Stufe) × (0,5 + 0,5 × Zustand) − 0,25 ohne Versorgung –
  dieselbe Formel im Wohnbedürfnis (bei Zustand 1 wie vor G6).
- **Grundsteuer nach Bodenwert:** `EconomySystem.PropertyTaxFactor` (Hook) = `EconomyRules.PropertyTaxFactor(Bodenwert)`
  = Bodenwert / 0,30, begrenzt 0,25..2,5 (unbekannt → 1); Grundsteuer = bewohnte Haushalte × 40 € × Faktor × Satz Wohnen.
  0,30 ≈ typisches Wohngebäude an der Straße ohne Dienste → Durchschnittsstadt bringt etwa die alte Pauschale.
- **Standortwahl (Hook)** `CitySystem.LotPreference` = `LandValue.GrowthPreference`: Wohnen/Handel/Büro bevorzugen hohen
  Bodenwert, Industrie günstiges Land + Autobahnnähe (0,55 × (1 − Wert) + 0,45 × Autobahn, 0 ab 640 m); Gewicht
  0,05 + Vorliebe² beim Würfeln der Startzelle (gleich viel Zufall wie vorher).
- **API (Inspektor/HUD):** `ValueAt(p)`, `ValueOf(b)` (−1 = unbekannt), `Explain(p|b)` → `LandValueFactors` (Beiträge je
  Faktor, `Contributions()` als (Text, Wert), `Target`, `Current`), `LevelInfo(b)` → `BuildingLevelInfo` (Stufe, Fortschritt
  0..1, Tage, `Missing` + `MissingText`, nächste Anforderungen, Abstiegs-Tage, Zustand + `ConditionText`, `Facts`),
  `FactsOf(b)`, `HighwayAccessAt(p)`, `LandValueRules.ValueText/ConditionText/LevelGapText`. Events `LevelChanged`,
  `BuildingDecayed`; Zähler `Upgrades/Downgrades/DecayedBuildings`. Test-Hook `FactsProvider`.
- Darstellung: `EnvironmentOverlayView` Overlay „Bodenwert“ (**F2**, rot → gelb → grün); `BuildingView` sortiert die
  Katalogmodelle einer Zone nach Höhe und wählt je Stufe aus einem Drittel (bei nur einem Modell dieses), Platzhalter
  +30 % Höhe je Stufe. Knopf in der HUD-Ansichtsleiste/Inspektor-Anzeige baut `ui2` an diese API.
- Später: Stufenaufstieg auch nach Wohlstand/Einkommen (G5), Brände/Kriminalität senken den Wert, Bodenwert als
  Preis beim Kauf von Grundstücken, Denkmalschutz/Stilrichtungen, Abriss-Meldung im HUD.

## Stadtkasse (Teil von G5, vorgezogen)
- `Simulation/Economy`: `EconomySystem` (`world.Economy`, läuft 1× je Spieltag, als letztes System), `EconomyRules`
  (alle Zahlen an einem Ort, Platzhalter fürs Balancing), `BudgetCategory`/`TaxKind`, `BudgetPeriod` (Buchungen eines
  Tages/einer Woche je Kategorie, Beträge ≥ 0, Einnahme/Ausgabe aus der Kategorie).
- **Tagesabrechnung**: Grundsteuer = bewohnte Haushalte × 40 € × Bodenwert-Faktor (G6) × Satz Wohnen; Gewerbe-/Einkommensteuer = besetzte
  Jobs × Basis (Handel/Mischgebiet 45, Büro 60, Industrie 40 €) × Satz der Zonenart. Unterhalt: Straßen 0,5 % des
  Neubauwerts je Tag (`RoadBuilder.SegmentValue` = Länge × €/m × Brücken-/Tunnelfaktor → 1 km Stadtstraße 60 €/Tag),
  freie Leitungen/Rohre 0,5 % je Tag, Versorgungsbauwerke `UpkeepPerDay` aus dem `UtilityCatalog`, weitere
  Kostenträger über `IUpkeepSource` (`Name`, `WeeklyUpkeep()`, täglich 1/7, Kategorie „Dienste“ mit Aufschlüsselung je
  Name) – **Dienste (G4) melden sich nur mit `world.Economy.RegisterUpkeep(src)` an**. Nach 7 Tagen wird die Woche
  abgeschlossen (`History`, max. 52 Wochen, `LastWeek`, `CurrentWeek` inkl. Baukosten, `LastDay`, `EstimateDay()` als
  Prognose ohne Buchung). Identität: `Net` = `ClosingBalance − OpeningBalance` je Woche.
- **Steuersätze** je `TaxKind` (Wohnen/Handel/Büro/Industrie): Standard 10 %, 0–30 %, ganze Prozent (`SetTaxRate`,
  `ChangeTaxRate`). Wirkung über den Hook `CitySystem.DemandModifier` (Faktor auf `Demand` je Zone): beim Standardsatz
  exakt 1, darunter +2 % je Prozentpunkt, darüber −4 % je Prozentpunkt (min. 0,1); Mischgebiet = Mittel Wohnen/Handel.
  `MoveInPull` (Zuzug) bleibt bisher unberührt.
- **Baukosten** über die Schnittstelle `IConstructionBudget` (`CanAfford`/`Spend`/`Refund`), gesetzt als `Budget` an
  `RoadNetwork`, `TerrainMap` und `UtilitySystem` (null = kostenlos, z. B. reine Netz-Tests). Straßen: `RoadBuilder.Plan`
  prüft nach den Geometrieregeln (die haben Vorrang) → `RoadBuildError.InsufficientFunds`; `Build`, Doppelbau,
  Kreisverkehr und Vorlagen buchen den **Planpreis genau einmal** (`RoadBuilder.Charged` hängt die Kasse während des
  Verbundbaus ab, Teilstücke werden nicht einzeln geprüft/gebucht). Ausbau = `ReplaceCost` (`CheckReplace` prüft Geld).
  Versorgung: `PlanFacility`/`PlanLine` → `UtilityBuildError.InsufficientFunds`, `Build…` bucht. Terraforming:
  `TerrainMap.Terraform` prüft vorher gegen die größtmögliche Erdmenge (π(r+Zelle)² × Stärke) und bucht danach
  Volumen × 0,5 €/m³ (`TerraformResult.Cost`, `.InsufficientFunds`). **Abriss durch den Spieler** (`RoadBuilder.Demolish`,
  `UtilitySystem.DemolishFacility/-Line`) bringt 25 % zurück; internes Entfernen (`RemoveSegment`, Kreisverkehr räumt
  innen, Straße durch Bauwerk, Leitung nicht umtrassierbar) bringt nichts.
- **Kredit = Überziehungsrahmen** 50 000 € (bewusst statt Kredit mit Laufzeit: kein Verwaltungsaufwand, überbrückt die
  Startphase; echte Kredite später): Bauen erlaubt, solange der Kontostand danach ≥ −50 000 € ist. Zinsen 0,2 % je Tag
  auf das Minus. **Konsequenz:** 28 Tage am Stück im Minus → `ConstructionFrozen` (Baustopp: keine Neubauten), bis der
  Kontostand wieder ≥ 0 ist. Wachstum der Zonen (private Bauherren) läuft weiter.
- **Sandbox** `WorldSettings.UnlimitedFunds` (Standard **an**, Startkapital `StartingFunds` 100 000 €): alles wird
  gebucht, aber nie blockiert, keine Zinsen, kein Baustopp. Entscheidung: Die vielen bestehenden Tests/der Rauchtest bauen
  ohne an Geld zu denken und bleiben so unverändert; das Spiel (`SimulationRunner`, Inspector „Stadtkasse“) schaltet die
  echte Kasse ein. Wirtschafts-Tests setzen `UnlimitedFunds = false`.
- Darstellung: `Game/Economy/BudgetPanel` (IMGUI, Taste **M**, verschiebbar; Begleiter am `RoadTool`): Kontostand,
  Verfügbar/Überzogen/Baustopp, Tagesprognose, letzte und laufende Woche je Kategorie, Dienste je Name, Steuersätze ±
  mit Nachfragefaktor, Saldo-Balken der letzten 12 Wochen. Fehlertexte „Zu wenig Geld in der Stadtkasse (M)“ in
  Straßen-/Versorgungswerkzeug, Terraforming zeigt Kosten je Strich.
- **Seit G5:** Gewerbesteuer aus dem **Gewinn** der Firma (Hook `EconomySystem.TaxableProfit`, s. „Warenketten (G5)“) statt
  besetzte Jobs × Basis; die Job-Pauschale bleibt der Rückfall für Gebäude ohne Firma bzw. ohne Bindung.
- Später: Gebühren (ÖPNV, Maut), Einnahmen aus Handel/Export (G5), Steuern nach Einkommen/Bildung/Bodenwert (G6),
  Steuersatz wirkt auf Zuzug/Zufriedenheit, Kredite mit Laufzeit/Tilgung, Budgetregler je Dienst, Bauwerks-Abriss
  anteilig nach Alter, Speicherstand.

## Warenketten (G5)
- `Simulation/Goods`: `GoodsCatalog` (19 Güter – Rohstoffe Getreide, Vieh, Holz, Erz, Kohle, Erdöl, Sand/Kies →
  Zwischenprodukte Mehl, Bretter, Stahl, Kunststoff, Glas, Zement → Endprodukte Lebensmittel, Kleidung, Möbel, Elektronik,
  Fahrzeuge, Baustoffe; je Gut Weltmarkt-Grundpreis €/Einheit, m³/t (Lkw-Ladung 25 m³/20 t), Verderb/Tag, Bedarfsstufe,
  Verbrauch je Haushalt, Absatz je Verkäufer), `RecipeCatalog` (18 Industrie-Rezepte + Einzelhandel + Büro: Eingänge →
  Ausgänge **je Arbeitsplatz und Tag**, `Workforce` = Anteil Jobs einfach/mittlerer Abschluss/Studium, Strom kW/Wasser je Job
  (Industriebedarf im Netz, s. u.), Vorkommen + Mindestgüte; Faustregel Wertschöpfung ≈ 175 €/Job zu Grundpreisen),
  `GoodsRules` (alle Zahlen), `Firm`, `GoodMarket`, `Shipment`, `GoodsSystem` (partial: Kern, `.Trade`, `.Households`,
  `.Siting`), `GoodsBinding.Connect` (in `CreateDefault`). Läuft nach den Diensten, vor der Stadtkasse.
- **Takt:** jeden Tick Lkw-Abfahrten, jede Spielstunde (10 Ticks) Lieferungen → Produktivität → Produktion → Bestellungen →
  Export; zum Tageswechsel Löhne, Büro-Erlös, Verderb, Gewinnrechnung, Märkte, Pleiten. Firmen in Gebäude-Id-Reihenfolge,
  Zufall nur über `SimRandom` → deterministisch.
- **Firmen** in Industrie, Einzelhandel, Mischgebiet (Rolle Einzelhandel) und Büro – angelegt bei `BuildingAdded`. Lager:
  Eingang 3 Tage Verbrauch, Ausgang 3 Tage Produktion (bei voller Belegschaft), Erstausstattung 50 % Eingang aus dem
  Startkapital (400 €/Job). Produktivität = Belegschaft × (1 − 0,5 × fehlende Fachkräfte) × (0,2 ohne Strom/Wasser) ×
  Vorkommen (0,5 + Güte) × Ernte (nur Landwirtschaft); Produktion je Stunde begrenzt durch Eingänge und freien Ausgang (Status `MissingInput`/`OutputFull`).
  Gewinn = Erlös − Wareneinsatz (verbrauchte/verkaufte Eingänge zum Ø-Einkaufspreis) − Löhne (echte Löhne der Beschäftigten,
  `CitizenSystem.WageBillOf`) − Transport. Büro: Erlös 185 €/Beschäftigtem × Dienstleistungsnachfrage (0,75–1,3).
- **Standortwahl der Industrie** (eine Unterart je Grundstück, beim Entstehen): Score = Bedarf × (0,6 + Güte) + 0,2 × Güte +
  stabile Streuung (Hash). Bedarf je Gut = 0,25 + 1,5 × Lücke (geplanter Bedarf aller Firmen + geschätzter Haushaltskonsum
  gegen geplante Kapazität) + 0,5 × (Preisindex − 1) + 0,3 × Weltmarkt; Überkapazität kostet bis −0,3. So entstehen Ketten
  rückwärts (Lebensmittelfabrik → Mühle → Bauernhof). Gewinnung nur ab Mindestgüte des Vorkommens.
- **Markt je Gut:** Bestand = Ausgangs- + Eingangslager aller Firmen; Zielbestand = 3 Tage Bedarf (Eingänge der Belegschaft +
  Haushaltswünsche, geglättet 0,3). Zielpreis = Grund × clamp(1 + 0,6 × (1 − (Bestand+1)/(Ziel+1)), 0,5, 1,8); lokaler Preis
  läuft je Tag 25 % dorthin und bleibt im **Paritätsband** Welt × [0,9 … 1,07] (Export- bzw. Importparität). Weltmarkt =
  Grund × (1 ± 10 % geglättetes Hash-Rauschen, Periode 15 Tage).
- **Handel:** Abnehmer bestellen bei Eingang + unterwegs < 50 % (bis 3 Ladungen/Stunde): günstigster Anbieter im selben
  Netzteil (lokaler Preis + Lkw-Kosten/Menge) gegen Import (Welt × 1,08 Fracht + Lkw ab nächstem Außenanschluss). Ohne jeden
  Außenanschluss: **Umland** (Fracht 15 %, Platzhalter, 1,5 km) – wie beim Zuzug ist der Start ohne Anschluss spielbar. Export
  ab 50 % Ausgangslager, wenn Welt − Fracht ≥ 90 % des lokalen Preises, ab 90 % Füllung immer (bis 30 % herunter). Firmen
  dürfen bis 10 Tage Lohn überziehen, darüber kaufen sie nichts.
- **Lieferverkehr:** eine Lieferung = eine Ladung. Anteil 0,35 × `TrafficSystem.Demand` als echter Lkw (`VehicleClass.Truck`,
  **negative Fahrt-Ids** → keine Kollision mit Bürgerfahrten), höchstens 15 % von `MaxVehicles`; Abfahrten über die Stunde
  verteilt, bei fehlendem Platz an der Einfahrt bis 2 h erneut versucht, sonst Platzhalter (3 km/Tick, +3 Ticks außerhalb).
  Ankunft über `TripEnded` bucht ins Ziellager (auch bei Abbruch; Sicherheitsnetz 3 Tage). Transport = 12 € + 0,12 €/Verkehrs-
  sekunde (geschätzt beim Abschicken, beim Lkw mit echter Fahrzeit nachberechnet → **Stau kostet Geld**). `VehicleView` zeigt
  Lkw mit den Katalogmodellen `AssetCatalog.Trucks`. Wegen der Zeitverzerrung (1 Stunde = 2,5 s Verkehr) scheitern in dichten
  Städten viele Lkw-Starts am Platz → Platzhalter (Statistik `TruckFailedYesterday`).
- **Haushalte** (Hook `CitizenSystem.Purchase`, markiert in `Arrive`): Einkauf deckt 3 Tage × (0,5 + 0,5 × Personen);
  Grundbedarf (Lebensmittel) immer, Komfort (Kleidung, Möbel) ab 110 €/Tag Einkommen + Zufriedenheit 0,45, Luxus
  (Elektronik, Fahrzeuge) ab 200 € + 0,55; Budget Grundbedarf 90 %, Rest 50 % des Einkommens; Ladenpreis = lokal × 1,45.
  Erfüllung 0..1 (Gewichte 0,6/0,25/0,15, nur Leistbares zählt) → `ShoppingNeed = min(0,99; Bedarf) × (1 − Erfüllung)`:
  leerer Laden lässt das Bedürfnis „Einkauf“ niedrig, neuer Versuch am nächsten Tag, Erinnerung „kein Laden“.
  `CitizenSystem.ShopHasStock`: Bürger meiden Läden ohne Grundbedarf.
- **Nachfrage der Zonen** (Hook `CitySystem.BusinessDemand`, additiv vor dem Steuerfaktor): ±25 × tanh(Ø Marge/0,15) der
  etablierten Firmen (ab 3 Tagen) je Zonenart; Industrie + bis 12 bei hohem Importanteil, Einzelhandel + bis 20 bei
  unerfüllten Einkäufen; Mischgebiet = halber Handel. **Verfall:** 21 Tage geglätteter Verlust und Konto unter dem Kreditrahmen
  → Pleite, Gebäude wird verlassen (`AbandonBankruptBuildings`).
- **Stadtkasse:** Gewerbesteuer = max(0, Tagesgewinn) × 2,5 × Satz (`EconomyRules.ProfitTaxMultiplier`; deckt vorerst auch
  den Lohnsteueranteil der alten Pauschale). Firmen haben eigene Kassen; Handel läuft nicht über die Stadtkasse.
- **Vorkommen & Ernte aus der Welt** (`GoodsBinding.ConnectWorld` in `CreateDefault`): `DepositAt` = `world.Resources.DepositAt`,
  Güte einer Firma = Mittel über das 3×3-Entnahmefenster (`DepositSampleSpacing` = Zellgröße, `DepositQualityAt`), täglich neu
  → erschöpfte Vorkommen senken die Produktivität, unter der Mindestgüte `NoDeposit`. `Extract` rechnet Wareneinheiten in
  Karteneinheiten um (`GoodsBinding.MapUnitsPerGood`): Erz/Kohle t = Gewicht der Ware (1,0/0,9), Erdöl/Sand m³ = Volumen
  (1,1/0,6); Holz 0,01 m³ und Boden 0,001 Punkte je Einheit **im Spielmaßstab** (die Karte entnimmt nur im ~48×48-m-Fenster,
  ein Forst/Hof bewirtschaftet viel mehr Fläche – physisch wäre der Wald in Stunden weg). Erneuerung macht `WorldSystem`.
  **Ernte** (`HarvestFactor`, Landwirtschaft): (0,6 Grundertrag + 0,4 × `Climate.HarvestFactor(Tag)`/Jahresmittel) ×
  clamp(Jahresmittel/0,5, 0,4, 1,2) – Jahreszeit schwankt um 1 (Winter ≈ 0,6, Sommer ≈ 1,4), das Klima der Region hebt/senkt
  das Mittel; ohne Grundertrag stünden Höfe im Winter still und gingen pleite. Ohne Bindung (Tests): Standard „fertility“ 0,6,
  „wood“ 0,4, sonst 0, Entnahme unbegrenzt. `ResourceIds` = Verweis auf `World.ResourceIds`.
- **Strom/Wasser der Industrie** (Hook `UtilitySystem.DemandProvider` + `DemandOf(b, kind)`, markiert): Industriefirmen
  verbrauchen Rezept × Arbeitsplätze (`GoodsSystem.UtilityDemand`), alle anderen Gebäude weiter `UtilityCatalog.Demand`;
  der Inspektor zeigt denselben Wert.
- **Verkehrsstatistik:** `TrafficStats.CitizenVehicles`/`CitizenTrips*` zählen nur positive Fahrt-Ids, Lkw stehen in
  `DeliveryVehicles`.
- **Abfrage-API:** `FirmAt(buildingId)` (Rolle, `Recipe`, `Form`, `Status` + `Firm.StatusText`, `InputOf/OutputOf/…CapacityOf/
  IncomingOf`, `Cash`, `Yesterday`/`Today` (Erlös, Wareneinsatz, Löhne, Transport, Gewinn), `ProfitEma`, `Margin`, `Workers/Jobs`,
  `Productivity`, `SkillCoverage`, `DepositQuality`), `Firms`, `Market(good)`/`Markets` (lokal/Welt, Bestand/Ziel, Bedarf,
  `Yesterday` Produktion/Verbrauch/Haushalte/Import/Export), `TradeYesterday`/`TradeTotal`, `Shipments`, `ShipmentsOf(id, list)`,
  `ShipmentForTrip(tripId)` (Lkw im Inspektor), `Stats`, `DemandOffset(zone)`, `RetailPrice`, `HighestTier`, `Restructure`.
- **Darstellung:** Fenster „Wirtschaft“ (**Taste F5** – F3 ist die Rohstoff-Ansicht; Koffer-Knopf in der oberen Leiste):
  `UI/Windows/GoodsWindow` (mittig, Baukasten) mit Reitern Güter (lokal/Welt, Import/Export, Bestand/Ziel,
  erzeugt/verbraucht, Haushalte), Firmen (Übersicht, Nachfrage-Zuschläge, Erntefaktor, Liste mit Gewinn/Kasse, blätterbar),
  Lieferungen (Lkw/Platzhalter/fehlgeschlagen/verloren, Liste). Zustand (offen, Reiter, Taste) im `Game/Goods/GoodsPanel`
  (Begleiter am `RoadTool`), das nur noch als IMGUI-Rückfall zeichnet. **Inspektor:** Gebäude mit Firma haben den Reiter
  „Firma“ (`InspectorWindow.Goods.cs`: Gewinn gestern/heute, Marge, Produktivität + Zustand, Betrieb/Rolle, Vorkommen,
  Strom/Wasser, Kasse, Gewinnrechnung, Eingangslager/Regale und Ausgangslager als Balken, laufende Lieferungen mit
  anklickbarem Lkw); Lkw zeigen „Ladung“ (Gut, Menge, Art, Von/Nach anklickbar, Wert, Transport).
- **Leistung** (Testprojekt, Info): ~190 Firmen, ~15 000 Bürger ≈ 4 ms/Tick für alle Systeme; Warensystem allein ≈ 2 ms je Spieltag.
- Später: Baustoffe für den Bau, Brennstoffe
  für Kraftwerke, Lagerhallen/Güterbahnhof/Hafen, Verträge statt Spotmarkt, Zölle/Subventionen, Löhne nach Gewinn, Ladenwahl
  nach Preis/Sortiment, Abfahrt-Puffer für Lkw, Spieler-Spezialisierung über `Restructure`.

## Welt (Etappe 5)
- `Simulation/World` (Namespace `CityBuilder.Simulation.World`): `PlanetGenerator` (+ `.Political`), `Planet` (+ `Region`,
  `Country`, `CountryProfile`), `ClimateModel`, `ResourceMap`, `WorldEconomy`/`NationEconomy`, `WorldSystem`, `RegionMapping`,
  `NameGenerator`, `WorldNoise` (3D-Wertrauschen auf der Kugel, `MinHeap`). Geländeparameter: `Environment/TerrainParams`.
- **Einschalten:** `WorldSettings.GenerateWorld` (Standard **aus** → Tests/Standardwelt exakt wie bisher, Prüfsummen-Test),
  `WorldSeed`, `StartRegion` (−1 = `Planet.SuggestStartRegion`: gemäßigt, fruchtbar, Fluss/Küste), `Planet` (`PlanetSettings`).
  Das Spiel (`SimulationRunner`) schaltet die Welt ein (Seed 2024).
- **Generator** (geseedet, ~0,45 s für 360 × 180 Zellen à ~111 km): Voronoi-Platten auf der Kugel (verzerrt), ~42 %
  Kontinentalplatten, Drift → Kollision (Gebirge), Ozean-Kontinent (Gebirge + Vulkane, Graben), Inselbögen, Grabenbruch/
  Rücken; alte abgetragene Gebirgsgürtel; Meeresspiegel als flächengewichtetes Quantil (30 % Land); Schelf auf
  Kontinentalkruste. Temperatur = 27 − 55·(|Breite|/90)^1,6 − 6,5 K/km + Küstenmilderung; Jahresschwankung nach Breite ×
  Kontinentalität. Niederschlag: Grundwert je Breite (ITCZ, Rossbreiten, Westwind), Feuchte zieht mit dem Wind (Passat
  Ost, Westwind, polarer Ost) je Breitenzeile, füllt sich über dem Meer, regnet über Land aus, Steigungsregen beim
  Aufstieg → **Regenschatten** dahinter (`ComputePrecipitation` ist rein/testbar). Abfluss per Priority-Flood → Flüsse
  (abflussreichste ~7 %). Biome (Whittaker vereinfacht, `Biomes`). Geologie: **Erz** an Gebirgsgürteln (geglättete
  Orogenese) in Häufungen, **Kohle** in Sedimentbecken (Tiefland, Senken, Vorland), **Öl** in tiefen Becken und auf dem
  **Schelf** (Tiefsee 0), **Sand/Kies** an Flüssen/Küsten/Wüsten/Gletschervorland, **Grundwasser** aus Niederschlag ×
  Becken (fossile Wüsten-Aquifere, Permafrost wenig), **Boden** aus Biom, Hang, Auen (Nil-Effekt), Vulkanen; **Wald** aus Biom.
- **Regionen** (~360): Saatpunkte mit Mindestabstand, Mehrquellen-Dijkstra über Land (Höhenunterschied teuer → Grenzen auf
  Kämmen), Inseln ab 6 Zellen eigene Region; Mittelwerte (Klima, Relief, Vorkommen inkl. halbem Schelf-Öl), Fluss, Küste +
  Richtung zum Meer, Nachbarn. **Länder** (~36): Hauptstadt-Regionen mit Mindestabstand, Dijkstra über den Regionsgraph
  (Land, Gebirge teurer; Seewege zu den 3 nächsten Regionen ×2,5). Namen aus dem Silbengenerator (eindeutig), Hauptstadt =
  beste Zelle (Boden, Fluss, Küste, Klima). **Profil** (`CountryProfile`): `Raw` 0..1 aus Natur/Lage für Tourismus
  (Küste, Wärme, Berge, Vielfalt), Landwirtschaft (Boden × Wachstum × Wasser), Rohstoffe (Erz/Kohle/Öl/Wald), Industrie
  (Kohle+Erz, Wasser, Flüsse, Ebene, Küste), Technik (gemäßigt, Küste, Flüsse, „Kultur“-Zufall); `Rank` = Rang unter allen
  Ländern; `Comparative` = Rang relativ zu den eigenen Stärken → `Primary`/`Secondary` (Spezialisierung).
- **LOD-Volkswirtschaft** (`NationEconomy`, je Land, täglich über `WorldSystem`): Bevölkerung (aus Boden/Klima/Küste/Flüssen),
  8 Güter-Kategorien (`GoodsCategory`: Lebensmittel, Holz, Erze/Baustoffe, Energie, Industrie, Konsum, Technik, Tourismus).
  Kapazität je Kopf aus Profil/Vorkommen, Ernte und Heizbedarf aus dem Klima der Hauptstadt, Vorleistungen (Erz+Energie+Holz →
  Industrie → Konsum/Technik; Tourismus braucht Lebensmittel/Energie) begrenzen die Produktion, Haushaltsbedarf je Kopf,
  Lager (Ziel 20, max. 120 Tage, Überschuss → `Overflow` = Exportpotenzial), Preis träge (±3 %/Tag je Lücke, 0,25–4 × Grundpreis;
  Startpreis spiegelt Knappheit). `Balance(cat)` = Überschuss/Defizit je Tag für die spätere Handels-KI, `WorldPrice` =
  produktionsgewichtetes Mittel. Spieler-Land = Land der Startregion (`Planet.PlayerCountry`, `Country.IsPlayer`) – läuft
  vorerst ebenfalls aggregiert (der Rest des Landes neben der Stadt).
- **Stadtkarte als Ausschnitt** (`SimulationWorld`-Konstruktor): `RegionMapping.TerrainFor` → `TerrainParams` (Relief
  0,35–2,2 aus der Zerklüftung, Fluss ja/nein + Breite nach Abfluss, Küste in Meeresrichtung mit 12–30 % Meer, Mitte bleibt
  frei), Geländeseed = `TerrainSeed + Region.Id·7919`. `TerrainParams.Default` = bitgleich zum alten Gelände.
- **Klima der Stadt** `world.Climate` (`ClimateModel`, immer gesetzt; ohne Welt 9,5 °C ± 9 K, 750 mm, 50° N): Tag 0 = 1. Januar,
  Jahreszeit (Südhalbkugel vertauscht), `TemperatureOn(day)` (Jahresgang + Wetterlagen über ~4 Tage), `PrecipitationOn(day)`
  (nasse/trockene Tage, Tropen mit Regenzeit), `HarvestFactor` 0..1, `HeatingFactor`/`CoolingFactor` 0..1,5, `Today`,
  `AnnualHarvestFactor`. Zustandslos, deterministisch aus Seed + Tag.
- **Ressourcen der Stadt** `world.Resources` (`ResourceMap`, immer gesetzt, Zellen = Umwelt-Raster 16 m): Ids in `ResourceIds`
  („fertility“, „wood“, „ore“, „coal“, „oil“, „sand“, „groundwater“), je Id ein `GridLayer` mit verbleibender Ergiebigkeit 0..1.
  **Verbindliche API:** `float DepositAt(string id, Vector2 p)` (0..1) und `float Extract(string id, Vector2 p, float amount)`
  (Menge in `ResourceKind.Unit` je `AmountPerHectare`: Boden 100 Punkte, Holz 300 m³, Erz 50 000 t, Kohle 80 000 t,
  Öl 20 000 m³, Sand 30 000 m³, Grundwasser 2 000 m³ je ha; entnimmt aus der Zelle, sonst aus den 8 Nachbarn, liefert die
  entnommene Menge). Dazu `AmountAt`, `TotalAmount`, `NaturalAt`, `Layer(id)`, `Kind(id)`, `Version`. Erneuerung täglich
  (`WorldSystem`): Wald logistisch + Anflug, Boden 1 %/Tag zum Ausgangswert, Grundwasser 2 %/Tag × Niederschlag des Tages;
  Erz/Kohle/Öl/Sand endlich. Ebenerdige Straßen roden Wald und versiegeln Boden dauerhaft (`ClearUnderRoad`, Tunnel/Brücken
  nicht). Lokale Verteilung aus den Regionswerten (`RegionResources.FromRegion`, √-angehoben) + Gelände: Boden in Ebenen/Auen,
  Wald in Flecken (Startgebiet 180 m frei), Erz unter Hügeln, Kohle/Öl in großen Lagerstätten (Öl auch unter Wasser),
  Sand am Ufer, Grundwasser in Senken.
- **Darstellung:** Ressourcen-Ansicht im `EnvironmentOverlayView` (`Overlay.Resource`, **F3** schaltet Boden → … →
  Grundwasser → aus, Legende oben mit Gesamtmenge; `ShowResource(id)` für die HUD). `ScenicPropsView`: Bäume = Waldraster
  × 0,85 (gerodete Zellen lichten aus, Nachwuchs kommt zurück; Rückfall ohne Raster: alte Perlin-Verteilung).
  `Game/WorldMap/WorldMapWindow` (IMGUI nur noch Rückfall – neue UI: `WorldMapUiWindow`; **F4**, Begleiter am `RoadTool`): Textur 360 × 180 (×2), Modi Biome/Relief/Länder/
  Temperatur/Niederschlag/Boden/Wald/Erz/Kohle/Öl/Sand/Grundwasser, Startregion weiß umrandet, Hauptstädte (Spieler gelb),
  Zelleninfo unter der Karte, rechts Land unter der Maus bzw. Spieler-Land mit Stärken und Wirtschaftstabelle.
  (Im Editor noch nicht visuell abgenommen.)
- Später: Startregion im Menü wählen (Weltkarte anklicken), Klima auf Gelände-Farben/Bewuchs (trocken → gelbes Gras, Schnee),
  Wetter in Strom (Wind/Sonne)/Verkehr, Meeresströmungen, Flüsse auf der Weltkarte als Linien, Handel zwischen Ländern
  (Etappe 8 über `Balance`/`Price`), Stadt-Wirtschaft (G5) speist ins Spieler-Land, Wechsel in andere Städte (LOD detailliert).

## Eigene Assets (im Git)
- **Alles Eigene liegt unter `Assets/_Project/`** (z. B. Blender-Modelle in `Assets/_Project/Models/`, Blender-Skripte
  in `blender/` im Projektordner). Die `.gitignore` versioniert unter `Assets/` nur `_Project`, `Scenes`, `Settings`
  und `InputSystem_Actions` – alles andere (Store-Pakete direkt nach dem Import) wird nie committet.
- **Eigene Modelle kommen nach Ordner automatisch ins Spiel** (Neubau beider Kataloge, sobald sich ein FBX unter
  `Models/` ändert): `Utilities/<Name>.fbx` und `Services/<Name>.fbx` über die Namenslisten in `OwnModelCatalogBuilder`
  (Versorger/Dienste; `School` gilt für Grund- und Oberschule, `Park` für beide Parks); `Buildings/<Zone>/*.fbx`
  (Ordnernamen = `AssetCatalog`-Felder: ResidentialLow/-Medium/-High, Mixed, Retail, Office, Industry),
  `Nature/Tree_*.fbx`, `Props/StreetLamp*.fbx` über `AssetCatalogBuilder.Own(…)` in Originalgröße mit `CB_Palette.mat`.
  **Personen** (Fußgänger): alle `People/*.fbx` (Ordner anlegen, wenn das erste Modell kommt) → `OwnModelCatalog.People`,
  Id = Dateiname; Gruppe nach Präfix `Toddler*`, `Child*`/`Kid*`, `Teen*`, `Senior*`, sonst Erwachsene. Konvention: Ursprung =
  Mitte zwischen den Füßen, Front +Z, 1 Einheit = 1 m (~1,75 m groß; grobe Einheitenfehler werden korrigiert), Paletten-Material;
  optionale Kindobjekte `LegL`, `LegR`, `ArmL`, `ArmR` schwingen beim Gehen um ihren eigenen Ursprung (= Hüft-/Schultergelenk,
  Drehachse X). Gezeichnet per Instancing (ein Teil je MeshFilter), keine Farbvariation (mehrere Modelle je Gruppe verwenden).
  Versorger zusätzlich NuclearPlant (Utilities) und SewagePlant (aus Models/Services, Schlüssel „Services/SewagePlant“), Dienste
  zusätzlich Newspaper, RadioStation, TVTower (Medien). Noch ohne Verwendung:
  Transport/*, Nature/Bush_*/Rock_*, übrige Props.
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

## ÖPNV (Stufe 1: Buslinien)
- `Simulation/Transit`: `TransitTypes.cs` (`TransitRules` alle Zahlen, `TransitCatalog.BusDepot`, `BusStop`, `TransitLine`, `Bus`,
  `Passenger`, `TransitOption`, Pläne/Fehler), `TransitSystem` (+ `.Operations` Route/Depot/Busbetrieb, `.Riders` Fahrgäste),
  `world.Transit` (jeden Tick, nach den Diensten). Verkehr: `TrafficSystem.Scheduled.cs` (Linienfahrzeuge), `Pathfinder.FindPathToEdge`.
- **Haltestelle** (`PlanStop`/`BuildStop`, 600 €, 15 €/Woche): rastet an der nächsten ebenerdigen Stadtstraße ein (Rand ≤ 12 m),
  Seite des Klicks (rechts in Segmentrichtung = `StopSide.Along`) oder Paar (`Both`); Einbahn nur die befahrene Seite; ≥ 15 m
  von Kreuzungen, ≥ 30 m zur nächsten Haltestelle derselben Seite. Bus hält auf der Fahrspur am Rand (keine Bucht → blockiert
  die Spur, wie im echten Leben). **Straßenumbau:** Haltestellen wandern beim Zerteilen/Umbau mit (Seite tauscht bei
  „Richtung umdrehen“), fällt die Straße weg, entfällt die Haltestelle (sie liegt auf der Straße, blockiert sie also nicht).
- **Linie** (`CreateLine(stops, loop, busse)`, `PreviewLine` für die Werkzeug-Vorschau): Rundlinie A→B→C→A oder Pendel A→B→C→B→A
  (`VisitStops`). Je Abschnitt A* (Bus) ab der Kante, auf der der Bus am vorigen Halt steht; Ziel auf einer Seite →
  `FindPathToEdge` (liegt es zurück auf derselben Kante: Runde bis zur Einbiegekante). Fahrplan je Abschnitt = Σ Länge /
  (Tempolimit × 0,75) + 4 s je Kreuzung + 8 s Halt; Umlaufzeit/Busse = Takt. Neu berechnet bei Straßen-/Linienänderung.
- **Busdepot** (Bauwerk wie G4: 40 × 30 m, 45 000 €, 1 500 €/Woche, 16 Busse; `PlanDepot`/`BuildDepot`/`DemolishDepot` 25 %
  zurück; Sperren für Grundstücke/Terraforming/Versorger/Dienste über verkettete Hooks, neu `ServiceSystem.OtherStructureOverlaps`).
  `SnapDepot` richtet die Vorderseite zur nächsten Stadtstraße aus und rastet am Rand ein (Spielerregel). **Straßenbau reißt
  Depots nicht ab** – für den Straßenblocker gibt es `BlocksRoad(segment)` / `BlocksPoint(p)` (noch nicht angehängt).
- **Busbetrieb:** fehlen einer Linie Busse, setzt das nächste Depot mit freiem Fahrzeug alle 4 s einen ein, der i-te beginnt am
  Halt i·n/Ziel (Takt durch Verteilung). Busse sind echte Fahrzeuge (`VehicleClass.Bus`, `Vehicle.IsScheduled`/`TransitId`):
  bremsen bis zur Haltestelle, stehen (`HoldAtStopFor`, Haltezeit 4 s + 1,2 s je Einsteiger + 0,8 s je Aussteiger, zählt nicht als
  „hängend“), bekommen das nächste Ziel (`Redirect`, unerreichbare Halte werden übersprungen), ordnen sich vor dem Halt rechts ein.
  Stau, Ampeln, 90-s-Abbruch wie alle; abgebrochener Bus → Fahrgäste stranden, Depot setzt neu ein. Weniger Busse gewünscht →
  leer werdende Busse fahren ins Depot. Pünktlich = Abschnitt ≤ 1,35 × Plan + 20 s.
- **Fahrgäste** (`CitizenSystem.Transit.cs`): `StartTrip` → `PlanRide(von, nach)` (Halte ≤ 600 m, je Ende 4 nächste, höchstens
  1 Umstieg, nur Linien mit Bussen) → Wahl in zwei Stufen: Auto (`CarProbability` + 0,4 × Erinnerung Bus voll/verspätet,
  − 0,3 × Vorteil einer schnelleren Busverbindung) sonst Bus gegen Fuß/Rad: `BusProbability` = logistisch(((t_Fuß/Rad − t_Bus,gefühlt)/600 s
  + 0,2 gesellig − 0,15 sparsam − 0,3 naturverbunden (je Abweichung von 0,5) + 0,5 Stau − 0,7 Bus voll − 0,5 verspätet + 0,3 gute
  Fahrt) / (Temperatur + 0,15)); t_Fuß/Rad = 1,25 × Luftlinie / 2,2 m/s; t_Bus,gefühlt = 1,3 × Fußwege/1,3 m/s + 1,5 × halber Takt
  + Fahrzeit + 120 s je Umstieg. Ohne Linien/Erinnerungen ist alles exakt wie vorher. **Skalierung wie beim Auto:**
  `TransitRealShare` 35 % × Demand fahren als einzelne Fahrgäste (warten an der Haltestelle, steigen ein – Kapazität 60 –, fahren,
  steigen aus, Rest zu Fuß), der Rest statistisch (Fahrplanzeit auf der Spieluhr, zählt als Fahrgast und zahlt, belegt keinen
  Platz). Grund ist die Zeitverzerrung (ein Spieltag = 60 s Verkehrszeit), nicht die Rechenzeit. Volle Busse lassen stehen
  (nach 2 vollen gibt man auf), Geduld max(150 s, 2,5 × Takt); Ergebnis → Erinnerungen `BusFull`/`BusLate`/`BusRide` →
  Mobilitäts-Bedürfnis (−0,15/−0,1/+0,1) und nächste Wahl. Sicherheitsnetz 5 Tage.
- **Kasse:** `BudgetCategory.TransitFares` („Fahrgeld ÖPNV“, 1,50 € je Einstieg, über `EconomySystem.Earn` alle 5 Ticks),
  `TransitConstruction` („Bau ÖPNV“); Unterhalt als `IUpkeepSource` „ÖPNV“ (Depots + 420 € je Bus im Einsatz + Haltestellen).
- **Abfrage-API:** Linie `Stops/VisitStops/Legs`, `BusesOf`, `PassengersToday/Yesterday/Total`, `AverageLoad`, `Punctuality`,
  `HeadwaySeconds`, `LeftBehindToday`; Haltestelle `WaitingCount`/`WaitingFor(line)`, `BoardedToday`, `LinesAt(stop)`; Bus `BusOf(vehicle)`,
  `LineId`, `Passengers`, `TargetStopId`, `LastLegOnTime`; Bürger `RideOf(c)`, `BusOf(c)`.
- **Unity** (`Game/Transit`): `TransitTool` (**Taste 5**, Modus `RoadTool.Mode.Transit`: Haltestelle ⇧ = beide Seiten, Linie zeichnen
  mit Routen-Vorschau, Klick auf freie Straße setzt gleich einen Halt, Klick auf den ersten Halt = Rundlinie, Enter fertig, RMB
  letzter Halt zurück, Esc verwerfen; Depot rastet ein, Strg frei; Abriss), `TransitView` (Haltestellen-Modell „bus_stop“ =
  `Models/Props/BusStop.fbx`, Depot „bus_depot“ = `Models/Transport/BusDepot.fbx` über `OwnModelCatalog.Transport`, sonst
  Platzhalter; Linienverläufe in 10 Linienfarben; Farbplatte auf dem Dach der Linienbusse – Busmodelle selbst aus „Buses“ über
  die `VehicleView`). HUD: Kategorie „ÖPNV“ (`GameHud.Transit.cs`), Icon `HudIconKind.Bus`, `TransitWindow` (rechts, **F6** bzw.
  im Werkzeug: Linien, Busse ±, Rund/Pendel, Halte mit Wartenden, Löschen). Inspektor: Haltestellen/Depots anklickbar
  (`SelectionKind.TransitStop/-Depot`), Bus zeigt Linie/Fahrgäste, Bürger „wartet auf Linie …/im Bus“. IMGUI-Rückfallleiste hat
  keinen ÖPNV-Eintrag. Im Editor noch nicht visuell abgenommen.
- **Medien:** Schlagzeilen-Themen `NewBusLine`, `BusOverloaded` (≥ 20 stehen gelassen am Tag), `BusLate` (Pünktlichkeit < 60 %),
  erkannt in `MediaSystem.DetectTransit` (liest `world.Transit`; ohne Medien gebaute Linien sind keine Neuigkeit).
- Leistung (Testprojekt, Info): 10 Linien, 40 Busse, ~10 500 Bürger ≈ 4 ms/Tick für alle Systeme.
- Später: Busspuren bevorzugen/Buchten, Tram/U-/S-Bahn, Taxi, Fahrpreis/Abo einstellbar, Linien bearbeiten im Werkzeug
  (Halt einfügen per Klick), Fahrplan mit Abfahrtszeiten statt Verteilung, Depotzuordnung je Linie (sichtbare Wartende: ✅ über `TransitPedestrians`, s. „Fußgänger“).

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
   - ✅ Kosten fürs Terraforming (Erdmenge × Preis, Stadtkasse)
   - Später: Bewuchs wächst auf bearbeitetem Boden nach,
     Rückgängig, Gelände unter Brücken/über Tunneln freigeben (mit Durchfahrts-/Überdeckungsgrenze)
4. Gebäude & Wirtschaft (wie Cities: Skylines, aber tiefer – Warenketten als Vorstufe zum Handel). Entscheidungen:
   **einzelne Bürger** als Agenten (Alter, Bildung, Wohnung, Job, Bedürfnisse, echte Fahrten),
   **Strom-/Wassernetze werden gebaut** (Leitungen, Rohre unterirdisch), Zonen in G1: Wohnen (3 Dichten),
   Mischgebiet, Einzelhandel, Büro, Industrie; Dienste/Versorger einzeln baubar, Reichweite übers Straßennetz.
   - ✅ G1 Zonenraster entlang Straßen + Malwerkzeug, Grundstücke, wachsende Gebäude (Store-Modelle), Nachfrage
   - ✅ G2 Bürger, Haushalte, Jobs, Pendeln/Einkaufen (Verkehr aus Gebäuden statt Sackgassen), Außenanbindungen.
     ✅ Bedürfnismodell (Utility AI, Persönlichkeit, Gedächtnis, Lebensereignisse inkl. Geburten/Auszug, Zufriedenheit).
     Später: Schulen (Bildung über G4), ÖPNV statt Platzhalter-Fahrten,
     Zuzug nur über Außenanschlüsse, Lieferverkehr (G5), Zufriedenheit (G6)
     ✅ Sichtbare Fußgänger: Gehwegnetz + Routen (Querung an Kreuzungen, Parkweg), Interpolation über die Fahrzeit,
     Park/Spaziergang/Laden/Haustür, Instancing mit LOD, Platzhalter-Figuren + eigene Modelle (Models/People), anklickbar
     (im Editor noch nicht visuell abgenommen). Später: Fußwege in Verkehrszeit (echtes Gehtempo), Abkürzungen durch Parks,
     freie Fußwege/Plätze, Ampelphasen an Zebrastreifen, inkrementeller Gehweg-Neubau (✅ ÖPNV verbunden: Wartende, Fußwege zur/von der Haltestelle)
   - ✅ G3 Strom & Wasser: Kraftwerke/Pumpe/Wasserturm, Netze (Straßen tragen mit + freie Leitungen/Rohre), Bilanz je Netz,
     Versorgungsstatus, Verlassen bei langem Ausfall, Info-Ansicht, Kosten/Unterhalt über die Stadtkasse.
     ✅ Abwasser (selbst verlegte Kanäle, Kläranlage, Auslass), Wasserqualität flussabwärts → Trinkwasser/Gesundheit/Ufer,
     Atomkraftwerk mit Akzeptanz und Störfall. Später: schwankende Erzeugung, Wasser/Strom auf ConnectionRules
   - ✅ G4 Dienste (einzeln baubar, Einflussradius übers Straßennetz): Polizei, Feuerwehr, Gesundheit, Schulen,
     Bibliothek, Parks, Müll (Müllhalde, Verbrennungsanlage); Abdeckungs-API, Müllstau, Brandrisiko, Werkzeug/Ansicht
     (im Editor noch nicht visuell abgenommen). ✅ Medien (Zeitung, Radio, TV) mit Schlagzeilen aus echten Daten. Später: Brände/Kriminalität
     als Ereignisse, Dienstfahrzeuge
   - ✅ G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
     (Stadtkasse: Einnahmen/Ausgaben, Bilanz halten = Kern der Herausforderung); Industrie-/Wohnformen erweitern
     - ✅ Güterkatalog + Rezepte, Firmen mit Lager/Kasse/Gewinn, Industrie-Unterarten nach Vorkommen/Nachfrage, Märkte mit
       Preisen und Weltmarkt, Import/Export, Lkw-Lieferverkehr, Konsum nach Bedarfsstufen, Gewinn → Nachfrage/Pleite,
       Gewerbesteuer aus Gewinn, Vorkommen/Ernte aus der Welt, Industrie-Strom/Wasser nach Rezept, Fenster „Wirtschaft“
       (F5, Baukasten) und Inspektor-Reiter „Firma“/Lkw-Ladung (im Editor noch nicht visuell abgenommen).
       Offen: Baustoffe/Brennstoffe nutzen, Verträge
     - ✅ Stadtkasse vorgezogen: Kontostand, Steuern je Zonenart mit Nachfragewirkung, Unterhalt, Baukosten,
       Abrisserlös, Überziehungsrahmen mit Zinsen + Baustopp, Wochenbilanz/Historie, `IUpkeepSource` für Dienste,
       Fenster (M). Offen: Gebühren, Handelseinnahmen, Kredite mit Laufzeit, Wirkung auf Zuzug/Zufriedenheit
   - ÖPNV & Fernverkehr: Taxi-, Bus-, Zug-Depot, Buslinien, Tram, U-Bahn, S-Bahn, Fernbahn, Flughafen
     - ✅ Stufe 1 Buslinien: Haltestellen, Rund-/Pendellinien, Busdepot, echte Busse im Verkehr, Fahrgäste mit Verkehrsmittelwahl
       und Kapazität, Fahrgeld/Unterhalt, Linienfenster (F6), Werkzeug (5) (im Editor noch nicht visuell abgenommen).
       Später: Tram, U-/S-Bahn, Taxi, Fernbahn, Flughafen
   - Energie erweitern: ✅ Atomkraftwerk; weitere reale Kraftwerkstypen
   - Schifffahrt (mit Häfen/Handel): schiffbare Gewässer mit Durchfahrtshöhe; feste Brücken hoch genug oder
     **Klapp-/Zugbrücken** (niedrig, öffnen für Schiffe → Rot/Stau, Umwege) oder Tunnel
   - ✅ G6 Bodenwert (Raster aus Lage, Diensten, Umwelt, Nachbarschaft), Ausbaustufen 1–3, Gebäudezustand/Verfall,
     Grundsteuer nach Bodenwert, Standortwahl der Bauherren, Overlay (F2), Anzeige in Inspektor/HUD und Meldungen
     bei Aufstieg/Verfall (Spiel-UI Phase 2). Offen: Stufen nach Einkommen (G5)
5. ✅ Welt: fiktive Erde, Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator), Länder mit eigenen Stärken
   (Regionen, Länder mit Profil/komparativen Vorteilen, LOD-Volkswirtschaft je Land, Stadtkarte als Ausschnitt der
   Startregion, `world.Climate`, `world.Resources` mit `DepositAt`/`Extract`, Weltkarte F4, Ressourcen-Ansicht F3;
   im Editor noch nicht visuell abgenommen). Später: Startregion wählen, Klima auf Gelände-Optik, Handel (8)
6. Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte
7. Reich: mehrere Städte (eigene oder NPC), Transport dazwischen, interner Handel
8. Handel & Politik: Bot-Nationen, Marktpreise, Verträge, Regierungen/Bündnisse, externer Handel, Tourismus
9. Multiplayer (Freunde einladen, eigene Städte im eigenen oder fremden Land, asynchron), Online-Handel, Mobile-Port
