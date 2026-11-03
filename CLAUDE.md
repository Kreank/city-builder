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
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Traffic, Environment)
- `Assets/_Project/Scripts/Game` – Unity-Seite (Runner, Kamera, Straßen-Tool/-View, Verkehr-Views, Overlays, HUD)
  - `RoadTool` hängt beim Start seine Begleiter an (Guides, Toolbar, VehicleView, TrafficSignsView,
    TrafficOverlayView, TrafficPanel) – neue Views dort ergänzen, dann muss die Szene nicht neu erzeugt werden.
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
- Fahrten entstehen vorerst zwischen Sackgassen/Kartenrand (bis es Gebäude gibt). Hängende Fahrzeuge verschwinden nach 90 s.

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
   (Fahrzeuge, Laternen, EU-Ampel, Bäume). Später: Terraforming-Werkzeug, Gelände-Texturen
4. Gebäude & Wirtschaft (wie Cities: Skylines, aber tiefer – Warenketten als Vorstufe zum Handel). Entscheidungen:
   **einzelne Bürger** als Agenten (Alter, Bildung, Wohnung, Job, Bedürfnisse, echte Fahrten),
   **Strom-/Wassernetze werden gebaut** (Leitungen, Rohre unterirdisch), Zonen in G1: Wohnen (3 Dichten),
   Mischgebiet, Einzelhandel, Büro, Industrie; Dienste/Versorger einzeln baubar, Reichweite übers Straßennetz.
   - G1 Zonenraster entlang Straßen + Malwerkzeug, Grundstücke, wachsende Gebäude (Store-Modelle), Nachfrage
   - G2 Bürger, Haushalte, Jobs, Pendeln/Einkaufen (Verkehr aus Gebäuden statt Sackgassen), Außenanbindungen
   - G3 Strom & Wasser (Erzeugung, Netze, Verbrauch, Folgen bei Ausfall)
   - G4 Dienste: Polizei, Feuerwehr, Gesundheit, Bildung, Parks, Müll
   - G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
   - G6 Bodenwert, Zufriedenheit, Ausbaustufen
5. Welt: Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator)
6. Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte
7. Reich: mehrere Städte, Transport dazwischen
8. Handel: Bot-Nationen, Marktpreise, Verträge
9. Online-Handel, Mobile-Port
