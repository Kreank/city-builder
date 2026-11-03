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
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Environment)
- `Assets/_Project/Scripts/Game` – Unity-Seite (Runner, Kamera, Straßen-Tool/-View, Overlays, HUD)
- `Assets/_Project/Scripts/Editor/ProjectSetup.cs` – erzeugt `Scenes/Main.unity` + Materialien (Menü „City Builder“)
- `Tests/CityBuilder.Simulation.Tests` – xUnit-Tests des Kerns ohne Unity

## Befehle
- Tests: `dotnet test Tests/CityBuilder.Simulation.Tests`
- Szene neu erzeugen (Editor geschlossen):
  `"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod CityBuilder.Editor.ProjectSetup.CreateMainScene -logFile setup.log`

## Straßen
- Rechtsverkehr. Querschnitt pro Typ in `RoadCatalog` (Spuren links→rechts in Segmentrichtung Start→Ende).
  Segmentrichtung ist bedeutsam (Einbahn, Autobahn, Rampe) – beim Zerteilen bleibt sie erhalten.
- Bauen immer über `RoadBuilder.Plan…()` → Vorschau → `RoadBuilder.Build…()`. Kreuzungen entstehen automatisch
  durch Zerteilen; Autobahnen kreuzen nie ebenerdig und verbinden sich nur mit Autobahn/Rampe.
- Kreisverkehr = Ring aus Einbahn-Bögen gegen den Uhrzeigersinn; Inneres wird entfernt.

## Roadmap
1. ✅ Fundament: Tick-Simulation, Kamera, freie Straßen (gerade/Kurve), Lärm-Raster + Overlay, Bauhilfen
2. Straßen
   - ✅ A: Spurmodell + Katalog, Kreuzungen durch Zerteilen, Kreisverkehr, Markierungen, Bauleiste
   - B: Spurgraph/Abbiegebeziehungen, Straße aufwerten/ersetzen, Richtung umdrehen, Parallelbau Autobahn
   - C: Verkehrssimulation (Fahrzeuge, Wegfindung, Stau)
   - D: Verkehrsregeln (Ampeln, Vorfahrt, Stopp, Abbiegeverbote, Fahrverbote, ÖPNV-Vorrang)
   - E: Höhen – Brücken, Tunnel, Rampen → Autobahnkreuze/-dreiecke/Anschlussstellen
3. Terrain mit Höhen
4. Erste Stadt: Zonen entlang Straßen, Wohnhäuser, Bevölkerung, eine Produktionskette, Lager, Geld
5. Welt: Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator)
6. Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte
7. Reich: mehrere Städte, Transport dazwischen
8. Handel: Bot-Nationen, Marktpreise, Verträge
9. Online-Handel, Mobile-Port
