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
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Traffic, Environment, Zoning, Citizens, Utilities, Services, Economy, LandValue, World)
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
  Rest = Platzhalter (zu Fuß/ÖPNV, Fahrzeit aus Luftlinie, 15 km/h). Ankunft meldet `TrafficSystem.TripEnded`
  (auch bei Abbruch → Bürger kommt trotzdem an; Sicherheitsnetz 3 Tage). Handler dürfen dort keine Fahrten starten.
  **Zeitverzerrung:** Verkehr läuft mit 0,25 s je Tick, die Uhr mit 6 min je Tick – eine Autofahrt dauert daher
  oft Spielstunden bis über einen Tag (wie Cities: Skylines). Der Tagesablauf ist darauf robust, Autofahrer sind länger „unterwegs“.
- Darstellung: Inspektor (Taste C, s. u.) mit Stadtübersicht (Statistik, Altersbalken, Bildung). HUD zeigt Arbeitslose/Pendler.
- Haushaltsart `Household.Kind` (Single/Paar/Familie/Alleinerziehend/Senioren/sonstige) wird aus dem Alter der
  Mitglieder abgeleitet, nicht gespeichert. Familienstand je Bürger: `Citizen.Relationship`/`PartnerId` (Bedürfnismodell).
- Lesende Abfragen: `CitizenSystem.CitizenForTrip(tripId)` / `VehicleOf(citizen)`, `Citizen.TripStartTick`,
  `PlannedArrivalTick` (Platzhalter-Fahrt), `HasCarTrip`; `Vehicle.StartTime` (Verkehrszeit beim Start).

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
  aus der Zufriedenheit (G6), Einkommen in Wohnwahl/Konsum (G5), ÖPNV statt Platzhalter (Stau → „eher ÖPNV“ wirkt bisher
  auf den Platzhalter „zu Fuß/ÖPNV“). Gleitkomma (`MathF.Tanh`, `Math.Exp`) ist nicht bitgenau über Laufzeiten garantiert –
  für Lockstep-Multiplayer ggf. später Festkomma/Tabellen.

## Spiel-UI (UI Toolkit, Phase 1)
- `Game/UI/Hud/GameHud` (+ `GameHud.BuildBar.cs`), Begleiter am `RoadTool` (zuletzt angehängt). Erzeugt zur Laufzeit
  `PanelSettings` (ScaleWithScreenSize, Referenz 1920×1080, Match 0,5) + `UIDocument`; lädt
  `Resources/UI/CityTheme.tss` (importiert nur `unity-theme://default` → Standardschrift) und `Resources/UI/CityHud.uss`
  (Design-Token als `--cb-…`-Variablen, Klassen `cb-…`). Aufbau in C#, weil die Inhalte aus Katalogen kommen.
  Fehlen Theme/USS, hängt das RoadTool die alte IMGUI-`RoadToolbar` als Rückfall an.
- Inhalt: obere Leiste (Stadtname-Platzhalter `GameHud.CityName`, Datum mit Anzeige-Kalender 12×30 Tage, Tempo,
  Einwohner, Arbeitslose, Strom-/Wasser-Warnchip nur bei unterdecktem Netz, Info-Modus), linke Ansichtsleiste
  (Stau, Lärm, Abgas, Strom, Wasser, Dienste-Abdeckung, Unterirdisch), Bauleiste (Kategorien → Werkzeugzeile + Kacheln + Infozeile),
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
  (Wirtschaft, Weltkarte) sollen auf den Baukasten umziehen.

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
- Optik ungetestet (Unity nicht gestartet); unsicher: Scroller-Klassen, Prozent-`translate` der Welt-Beschriftungen.

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
- Später: Abwasser/Kläranlage, Pumpe braucht Strom, Wind/Sonne schwankend (Tageszeit/Wetter), Brennstoff aus Warenketten,
  Versorgung in Nachfrage/Zufriedenheit (G2/G6), Masten/Rohre folgen späteren
  Geländeänderungen (Terraforming zu Wasser unter einem Mast wird noch nicht geprüft), Hochspannung vs. Ortsnetz,
  Wasserqualität/Verschmutzung flussabwärts. **Mittelinsel:** `RoadCatalog` hat noch keinen Typ mit Mittelstreifen
  (`LaneKind.Median` existiert im Builder, wird aber nicht benutzt) → Straßen mit Mittelstreifen und Masten darauf
  (dann ohne Platzverbrauch neben der Straße) später. Ebenso: Masten unter hohen Brücken erlauben, Masten mehrerer
  Leitungen am selben Punkt zusammenlegen.

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
  Abbiegeverbote in der Reichweite, Müllhalde leeren/Recycling, Medien (Zeitung/Radio/TV), Abwasser/Kläranlage,
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
- Später: Gebühren (ÖPNV, Maut), Einnahmen aus Handel/Export (G5), Steuern nach Einkommen/Bildung/Bodenwert (G6),
  Steuersatz wirkt auf Zuzug/Zufriedenheit, Kredite mit Laufzeit/Tilgung, Budgetregler je Dienst, Bauwerks-Abriss
  anteilig nach Alter, Speicherstand.

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
  `Game/WorldMap/WorldMapWindow` (IMGUI, **F4**, Begleiter am `RoadTool`): Textur 360 × 180 (×2), Modi Biome/Relief/Länder/
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
  Noch ohne Verwendung (Gebäude gibt es in der Simulation noch nicht): NuclearPlant, Newspaper, RadioStation,
  TVTower, SewagePlant, Transport/*, Nature/Bush_*/Rock_*, übrige Props.
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
     Zuzug nur über Außenanschlüsse, sichtbare Fußgänger, Lieferverkehr (G5), Zufriedenheit (G6)
   - ✅ G3 Strom & Wasser: Kraftwerke/Pumpe/Wasserturm, Netze (Straßen tragen mit + freie Leitungen/Rohre), Bilanz je Netz,
     Versorgungsstatus, Verlassen bei langem Ausfall, Info-Ansicht, Kosten/Unterhalt über die Stadtkasse. Später: Abwasser, schwankende Erzeugung
   - ✅ G4 Dienste (einzeln baubar, Einflussradius übers Straßennetz): Polizei, Feuerwehr, Gesundheit, Schulen,
     Bibliothek, Parks, Müll (Müllhalde, Verbrennungsanlage); Abdeckungs-API, Müllstau, Brandrisiko, Werkzeug/Ansicht
     (im Editor noch nicht visuell abgenommen). Später: Medien (Zeitung, Radio, TV), Abwasser/Kläranlage, Brände/Kriminalität
     als Ereignisse, Dienstfahrzeuge
   - G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
     (Stadtkasse: Einnahmen/Ausgaben, Bilanz halten = Kern der Herausforderung); Industrie-/Wohnformen erweitern
     - ✅ Stadtkasse vorgezogen: Kontostand, Steuern je Zonenart mit Nachfragewirkung, Unterhalt, Baukosten,
       Abrisserlös, Überziehungsrahmen mit Zinsen + Baustopp, Wochenbilanz/Historie, `IUpkeepSource` für Dienste,
       Fenster (M). Offen: Gebühren, Handelseinnahmen, Kredite mit Laufzeit, Wirkung auf Zuzug/Zufriedenheit
   - ÖPNV & Fernverkehr: Taxi-, Bus-, Zug-Depot, Buslinien, Tram, U-Bahn, S-Bahn, Fernbahn, Flughafen
   - Energie erweitern: Atomkraftwerk (und weitere reale Kraftwerkstypen)
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
