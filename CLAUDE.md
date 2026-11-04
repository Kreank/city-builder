# City Builder (Arbeitstitel)

Länder-/Reichs-Aufbausimulation in Unity 6 (URP). Inspiration: Cities: Skylines (freie Straßen,
Umwelt-Overlays), Anno (Produktionsketten, Bedürfnisstufen). Ziel: Start bei 0 → Stadt → Reich aus
mehreren Städten → Handel mit anderen Reichen (Bots, später echte Spieler, asynchron).
Plattform: PC zuerst, Mobile später (deshalb URP mit PC- und Mobile-Pipeline-Asset).

## Vision (vom Spieler formuliert, Leitlinie für alle Etappen)
- **Fiktive Erde:** Man erschafft sein eigenes Land, baut eine Stadt, später weitere Städte (selbst oder durch NPCs).
  Jedes Land hat andere natürliche Vorkommen und Stärken (Tourismus, Rohstoffe, Technik …).
- **Spielstart (2026-11-03, Spieler):** Statt einer Karte (Cities: Skylines) sucht man sich aus, **wo** man beginnt – nach
  Vorlieben (Klima, Küste/Fluss, Gelände, Schwerpunkt wie Agrar/Erze/Tourismus …), wobei eine Wahl andere unmöglich machen
  kann (Erze meist in Bergen, selten am Meer). Keine echten Ländernamen. Der Spieler gründet seine Stadt in **freiem,
  unbesiedeltem Land** als **freie Stadt (Stadtstaat)**; mit weiteren Städten wird daraus später ein eigenes Land mit selbst
  gewähltem Namen (Etappe 7). Umgesetzt: s. „Spielstart & Startregion“ und „Reich (Etappe 7)“.
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
  Ein knappes Drittel des Landes ist **frei** (zwischen den Ländern, Randlagen); dort startet der Spieler als Stadtstaat und
  wächst durch weitere Städte zum Land.
- **Detailstufen (LOD-Simulation):** die eigene, betrachtete Stadt läuft voll (einzelne Bürger, Fahrzeuge, Lager);
  andere Städte und Länder laufen **aggregiert** (Volkswirtschaft je Stadt: Bevölkerung, Produktion je Gut,
  Bedarf, Lager, Preise) – so bleibt eine ganze Welt rechenbar. Wechselt man in eine Stadt, wird sie detailliert.
- **Handel & Politik (7–9):** Spieler und NPC-Länder nutzen **dieselben Schnittstellen** (Angebote, Aufträge,
  Verträge mit Laufzeit, Liefermenge, Preis). Asynchron: Aufträge haben Lieferzeiten über echte Transportwege.
  NPC-Länder = Spiel-KI (Utility AI wie bei Bürgern, keine Sprach-KI zur Laufzeit).
- Alles deterministisch (geseedeter RNG), Commands für Spieleraktionen (Multiplayer/Replays).

## Entscheidung: Netze werden selbst verlegt (2026-11-03, Spieler – umgesetzt, s. „Strom & Wasser (G3)“)
- **Straßen führen keine Leitungen/Rohre mehr automatisch mit** (hat die alte G3-Regel „Stadtstraßen tragen mit“
  abgelöst). Der Spieler verlegt **Wasserrohre, Abwasserkanäle** (unterirdisch, Einrasten unter dem Gehweg) und **Strom**
  (Hochspannung vom Kraftwerk → Umspannwerk → Ortsnetz als Freileitung oder Erdkabel entlang der Straßen) selbst.
- Ein Gebäude ist angeschlossen, wenn eine passende Leitung/ein Rohr ≤ 12 m von seiner Zufahrt verläuft; es
  bekommt einen **sichtbaren Hausanschluss**. Anschlusslogik als gemeinsamer Baustein `ConnectionRules` für alle Netzarten.
- Hilfe „Netz entlang Straßen verlegen“ (einzelne oder alle Stadtstraßen, normale Kosten, Vorschau) – v. a. für
  bestehende Städte/alte Spielstände (Übergangsfrist 5 Tage nach dem Laden eines Stands v1).

## Entscheidung: Bauart (2026-11-03, Spieler – umgesetzt, s. „Wohnhäuser setzen (Anno-Stil)“ und „Setzen statt Zonen“)
- **Freie Straßen bleiben** (kein Anno-Raster).
- **Wohnhäuser auch einzeln setzen (Anno-Stil), Zonen bleiben zusätzlich:** Der Spieler stellt ein Wohnhaus hin, es bevölkert
  sich und **steigt auf, wenn Bedürfnisse erfüllt sind** (Einrichtungen in Reichweite: Laden/Markt, Schule, Polizei, Gesundheit …).
- **Nachtrag (2026-11-03, Spieler): Setzen statt Zonen.** Alle Gebäude (Wohnen, Laden, Büro, Industriehalle) werden gesetzt,
  per gehaltener linker Maustaste als **Reihe entlang der Straße** (wie Anno). Welcher Betrieb in Laden/Büro/Halle einzieht,
  entscheidet die Simulation (Vorkommen, Nachfrage, Schwerpunkt). Zonen verschwinden aus der Bauleiste (Code bleibt für alte
  Stände), Zonenraster/Grundstücksplatten entfallen; Nachfrage als eigene Anzeige; Straßen-Abstandshilfe nach Haustiefen.
  **Umgesetzt** (s. „Setzen statt Zonen“ unter „Wohnhäuser setzen“).
- **Produktion wählt weiter die Simulation**, der Spieler kann sie aber **lenken** (z. B. „mehr Öl fördern, wo Öl liegt“):
  Schwerpunkte/Förderung statt selbst gebauter Produktionsketten. Umgesetzt: s. „Produktion lenken“ (unter G5).

## Entscheidung: Kartengröße (2026-11-04, Spieler – noch nicht umgesetzt)
- Die 2-km-Stadtkarte ist zu klein (Verkehrsflughafen findet in echten Spielwelten keinen Platz, Metropolen eng).
  Gewählt: **größere Karte aus 2-km-Kacheln** (z. B. 6 × 6 km), Start auf der mittleren Kachel, **Nachbarkacheln mit Geld
  kaufen** (wie Cities: Skylines); volle Rechenlast nur, wo gebaut ist. Nächster großer Umbau (Gelände, Raster, Speichern).

## Architektur-Regeln
- **Simulation ≠ Darstellung.** `Assets/_Project/Scripts/Simulation` ist reines C# (`noEngineReferences: true`),
  kein `UnityEngine`. Vektoren: `System.Numerics.Vector2` (x, y) ↔ Unity (x, 0, z) über `SimConvert`.
- Simulation läuft in festen Ticks (`SimulationWorld.Step`, 1 Tick = 6 Spielminuten = 180 Verkehrsschritte à 0,25 s,
  s. „Zeitmodell“), nie mit `Time.deltaTime`. Alle Zeitkonstanten stehen in `Core/TimeModel`.
  Teilsysteme implementieren `ISimulationSystem`.
- Views (`Scripts/Game`) lesen die Simulation und hören auf Events; sie ändern keinen Zustand an der Simulation vorbei.
  Spieleraktionen gehen über Methoden der Simulation (später: Commands, wichtig für Multiplayer/Replays).
- Umwelt (Lärm, Abgas, später Klima, Böden, Rohstoffe) = `GridLayer`-Raster, obwohl Bauen frei ist.
- C#-Stand von Unity: max. C# 9. Das Testprojekt erzwingt `LangVersion 9`.
- Bot-Reiche und Spieler sollen dieselben Schnittstellen nutzen (Handel, Verträge).

## Zeitmodell (2026-11-03)
- **Zwei verzahnte Zeitachsen**, alles in `Simulation/Core/TimeModel.cs` (einziger Ort für Zeitkonstanten):
  **Kalender** 1 Tick = 6 Spielminuten (`TicksPerDay` 240, `TicksPerHour` 10) – Tagesabläufe, Produktion, Bedürfnisse,
  Abrechnungen, Ausfalldauern rechnen in Ticks. **Verkehrszeit** (`TrafficSystem.Time`, double aus der Schrittzahl, über Jahre
  exakt): je Tick `TrafficStepsPerTick` 180 Verkehrsschritte à `TrafficStepSeconds` 0,25 s = 45 s. **Zeitdehnung**
  `Dilation` = 8 Spielsekunden je Verkehrssekunde (3 min Fahrt = 24 Spielminuten); ein Spieltag = 3 h Verkehrszeit.
- **Ablauf** `SimulationWorld.Substep()`: alle `ISubstepSystem` (Verkehr; ÖPNV setzt Busse ein) einen Verkehrsschritt, nach dem
  180. Schritt der Kalender-Tick (Uhr + 1, alle Systeme nach `TickInterval`). `Step()` = einen ganzen Tick (bzw. den laufenden zu
  Ende); Tests/headless wie bisher. `SubstepInTick`, `AtTickBoundary`, `TickTime` (Ticks inkl. Bruchteil, synchron zur
  Verkehrszeit). Ereignisse aus dem Verkehr (Ankunft, Halt) kommen mitten im Tick (Uhr noch beim alten Tick).
- **Wege in Verkehrszeit:** Auto/Lkw/Bus fahren ohnehin dort. Platzhalter ohne Auto/Bus: bis `WalkMaxMeters` 900 m Luftlinie zu
  Fuß (`WalkSeconds` = Luftlinie × 1,25 ÷ `WalkSpeed` 1,3 m/s), darüber Rad/Sonstiges (`BikeSpeed` 4,5 m/s), außerhalb + 3 Ticks
  (`OutsideTrafficSeconds`); Umrechnung `TicksFor(sekunden)` (aufgerundet). Gilt für `CitizenSystem.StartTrip`, Fußweg vom
  Ziel-Halt (`CitizenSystem.EgressTicks`), Parkwege (`TimeModel.WalkTicks`, gleiche Formel in `PedestrianPlanner.ParkWalkTicks`),
  statistische Busfahrten, Lkw-Platzhalter (`GoodsRules.PlaceholderTicks`: 1,3 × Luftlinie ÷ 9 m/s). ÖPNV-Fahrgast geht erst zur
  Haltestelle (`Passenger.ReadyTime` = Start + Zugangsweg) und kann erst dann einsteigen.
- **Echtzeit** (nur Unity, `SimulationRunner`): 4 Tempostufen = `TicksPerRealSecond` (0,022) × `SpeedMultipliers` 1/5/30/90 →
  1 Spieltag = **Echtzeit 3 h** / 36 / 6 / 2 Minuten, Verkehr sichtbar 1× / 5× / 30× / 90× (4 / 20 / 120 / 360 Verkehrsschritte je
  Sekunde; Entscheidung des Spielers 2026-11-03). Tasten 1/2/3, erneut 3 = Stufe 4 (Taste 4 ist Dienste); HUD hat 4 Knöpfe. `ticksPerSecond` ist **nicht** mehr
  serialisiert (die Szene hatte einen alten Wert 10 gespeichert, der das Zeitmodell überschrieb). Der Runner verteilt die Verkehrsschritte über
  die Frames (`Substep`), Budget `maxSimMillisecondsPerFrame` 8 ms: Rückstand wird verworfen (Simulation langsamer, Frame
  friert nicht ein; `DroppedStepsLastFrame`, `LastFrameSimMilliseconds` in Debug-HUD/Hilfe). `TickAlpha` = Bruchteil zum nächsten
  **Verkehrsschritt** (Fahrzeug-Interpolation), `GameTickTime` = Kalenderzeit für Fußgänger, `AnimationTimeScale` = Verkehrszeit je
  Echtzeit, begrenzt auf 3 (Schrittfrequenz der Fußgänger, Rotoren – sonst Flimmern). Sichtbar: Autos fahren bei Tempo 1 mit
  30-facher Echtzeit (Verkehrszeit), Fußgänger 1,3 m/s Verkehrszeit.
- **Entscheidung** (Analyse im Bericht): Mischform aus „Kalender bleibt 6 min/Tick“ + „Verkehr in 180 Teilschritten“ statt
  (a) 1-Minuten-Ticks (alle Tick-Konstanten aller Systeme umrechnen, 6× Rechenzeit der Kalendersysteme) oder (b) frei getrennter
  Takte (Verhältnis zur Laufzeit wäre nicht deterministisch speicherbar). Vorbild Cities: Skylines: Bewegung läuft im Verhältnis
  zum Kalender langsam, Tagesabläufe sind tolerant. Zielwerte erreicht (Info-Test `TimeModelTests.Balancing_…`, große Stadt
  ~12 700 Einwohner, 10 Buslinien): Autofahrt Ø 25, Median 21, 90 % 49 Spielminuten; Lkw 76 % der Lieferungen echt (vorher ~10 %),
  27 ohne Platz; Busfahrgäste 87 % regulär angekommen (vorher 80 von 3 862); Gehtempo Median 1,30 m/s; Verkehrsschritt Ø 0,1 ms,
  Kalender-Tick-Schritt Ø 4,4 ms (Tageswechsel bis ~21 ms, einzelner Frame), gesamt ≈ 23 ms je Kalender-Tick → Tempo 3 ≈ 1 ms je
  Frame bei 60 fps.
- **Neue Zeitkonstanten** immer in `TimeModel` (oder dort ableiten), Fahr-/Gehzeiten in Verkehrssekunden, Tagesablauf in Ticks.
  Tests mit Fahrverhalten laufen in `Substep()`-Schritten (z. B. `TrafficTests.Run`), Kalender-Logik in `Step()`.
- **Speichern:** Weltabschnitt v2 speichert `SubstepInTick` (Speichern mitten im Tick erlaubt; v4: Gateway-Schalter); Verkehr v2 / ÖPNV v2 / Waren v2:
  Verkehrszeiten als double, `Passenger.ReadyTime`. Alte Stände: Uhr unverändert (Tick-Länge gleich), Verkehrszeit = Schritte ×
  0,25 s (wie vorher), alte Fahrgäste gelten als schon an der Haltestelle, laufende Platzhalter-Fahrten behalten ihre alte
  (kürzere) Ankunft.
- Offen: Abfahrten der Bürger/Lkw innerhalb eines Ticks verteilen (heute alle an der Tick-Grenze), Rad als eigene Darstellung,
  `CarShare` neu abstimmen (Rechenzeit ließe deutlich mehr echte Autos zu), Lockstep-Multiplayer: Spieleraktionen müssten auf
  Verkehrsschritte (nicht nur Ticks) datiert werden.

## Struktur
- `Assets/_Project/Scripts/Simulation` – Kern (Core, Roads, Traffic, Environment, Zoning, Citizens, Utilities, Services, Economy, LandValue, World, Goods, Trade, Transit, People, Gateways, Rail, Safety, Shipping, Aviation, Realm)
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
- **Bauwerks-Sperre (UX-Runde)** `Simulation/Roads/StructureRules.cs`: Straßen reißen Dienste, Versorger und Busdepots
  **nicht** mehr ab. Bauwerksarten melden sich mit einer Zeile an: `roads.RegisterStructures(() => blocks)` (je Bauwerk ein
  `StructureBlock` = Art, Id, Name, `Footprint`, Höhe; angemeldet: `ServiceSystem` „service“, `UtilitySystem` „utility“,
  `TransitSystem` „depot“ – Haltestellen nicht). `RoadBuilder.Plan` (damit auch Kurve, Doppelbau, Kreisverkehr, Vorlagen)
  prüft am Ende von `Validate` per `RoadFront.FindBlocking` (Fahrbahn inkl. Gehweg, ebenerdig; Brücken/Tunnel > 3 m
  Elevation gehen drüber/drunter, 0,25 m Toleranz) → `RoadBuildError.OverlapsStructure` + `RoadPlan.BlockedBy`
  (ebenso `RoundaboutPlan`/`TemplatePlan`/`DualCarriagewayPlan.BlockedBy`). Ausbau auf breiteren Typ: `CheckReplace` →
  `ReplaceBlocker`. Werkzeug: Vorschau rot, Bauwerk rot hervorgehoben, Hinweis „Straße überschneidet <Name> – anders führen
  oder Gebäude verschieben“. **Zonen bleiben wie bisher** (Straße entfernt Zone + Gebäude), Strommasten werden weiter
  umgelegt, Wasserrohre unter Straßen unverändert.
- **Front-Ausrichtung** (`RoadFront`): `FindAccess(roads, footprint, reach)` = Zufahrt an der **Mitte der Vorderseite**
  (ebenerdige Stadtstraße vor ihr – Kegel ±60° um `Front` –, Rand ≤ reach); `TryAlign(roads, cursor, w, d, drehung, radius)`
  dreht die Vorderseite zur nächsten Stadtstraße (Normale der Tangente, auch in Kurven), Vorderkante 0,5 m vor dem Gehweg
  (`SnapGap`), Mitte folgt dem Cursor entlang der Straße; Zusatzdrehung rückt das Bauwerk so weit ab, dass die Ecken frei
  bleiben. Neue Bauwerksarten nutzen beides mit einer Zeile.
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
  `RoadGuidesView`, Einrasten `RoadTool`) – **seit „Setzen statt Zonen“ nach Gebäudetiefen** (vorher Zonenraster 4 + n Reihen
  à 8 m, 77 m für city_2↔city_2): Gesetzte Gebäude rasten mit der Vorderkante `RoadFront.SnapGap` 0,5 m vor der
  Gehweg-Außenkante (`RoadType.HalfWidth`) ein. **Optimaler Achsabstand** = halbe Breite A + halbe Breite B + 2 × (0,5 m +
  Tiefe) = zwei Reihen der gewählten Bauart **Rücken an Rücken**. Tiefe = `RoadTool.ParallelDepth` (F9: 16 → 24 → 32 m → aus,
  Standard 24 m; folgt der im Bau-Werkzeug gewählten Bauart, `SetParallelDepth`). Beispiele city_2↔city_2: 16 m
  (Einfamilienhaus, Laden) 46 m, 24 m (Mehrfamilienhaus, Wohn-/Geschäftshaus, Bürohaus, Gewerbehalle) 62 m, 32 m (Wohnblock,
  Industriehalle) 78 m; city_4↔city_2 mit 24 m 65,5 m. `Evaluate(a, b, Abstand, Tiefe)` → `Rows` (0–2 Reihen passen), `Unused`,
  `Overlap`, `Fit` (optimal bis 0,25 m Lücke, **enger als 0,01 m ist schon „zu eng“** – sonst überlappen die Gebäude), `Describe`
  „Abstand 62,0 m · optimal (Rücken an Rücken, 2 × 24 m tief)“ / „X m Lücke zwischen den Rückseiten“ / „zu eng: X m Überlappung
  (…, nur eine Reihe passt)“. **Kurvenzuschlag** (`CurveAllowance`, `GuideHit.Allowance`): auf Bögen stehen die Gebäude als
  Sehnen, die hinteren Ecken beider Reihen ragen um ≈ b²/(4R) über (b = Breite = Tiefe der quadratischen Bauarten, R =
  Krümmungsradius `CurveRadius` an der Stelle) + 0,75 m Puffer für die Bézier-Näherung; ab R > 3 km 0. Die Hinweiszeile bewertet
  den Abstand ohne Zuschlag. Tests prüfen gegen echtes Setzen (`PlanHouseRow`): alle 25 Typpaare × 16/32 m Rücken an Rücken
  ohne Lücke, Reihen-Modell über 33 Abstände = tatsächliches Setzen, 90°-Bogen (R ≈ 300 m) innen 9 von 9 Gebäuden passend
  (ohne Zuschlag 0 von 9).
  Werkzeug (gerade/Kurve, Stadtstraßen): gestrichelte Parallelen beidseits der bis zu 3 nächsten ebenerdigen Stadtstraßen
  (gerade auch 80 m über die Enden hinaus), dazu **nur eine zarte Linie** dort, wo die Rückseite einer Reihe der gewählten Tiefe
  liegt (die früheren Zonentiefen-Flächen sind entfernt); die Vorschau zeigt diese Linie beidseits der eigenen Straße (grün).
  Mit Einrasten (G, Strg hebt auf) rasten Start, Kontrollpunkt und Ende im Umkreis
  6 m auf die Linien ein; gerade Straßen von einer geraden Hilfslinie aus werden bis 8° Abweichung exakt parallel
  (Länge 8-m-Raster), Kurven zwischen zwei Punkten derselben gekrümmten Hilfslinie bekommen einen ausgeglichenen
  Kontrollpunkt (`FitParallelControl`, 90°-Bogen < 1 m Abweichung). Abstand + Bewertung stehen in
  `RoadTool.Guide.Info` (Hinweiszeile + Beschriftung). **Blockraster (F10):** Länge gerader Straßen rastet auf
  `BlockLength` = Zellen × 8 m + 2 × (halbe Breite + 1 m) ein (8 m = Gebäudebreiten-Raster; Kreuzungsfläche ausgespart), Querlinien
  zeigen die Stellen; gilt, wenn der Start eine Kreuzung ist/wird. An Blockecken nimmt die ältere Straße die Eckzellen.

## Gelände
- `TerrainMap` (Simulation, 4-m-Raster): erzeugt (Hügel bis ~90 m, Fluss, Wasserspiegel 0) oder flach (Tests).
  Mit Weltkarte bekommt `Generate` `TerrainParams` der Startregion (Relief-Faktor, Fluss/Breite, Küste; s. „Welt (Etappe 5)“).
  `RoadNetwork.Ground` = Gelände. **Straßenhöhen sind absolut**, Brücke/Damm/Trog/Tunnel ergibt sich aus der
  Elevation (Höhe über Gelände). Bauhöhe im Werkzeug = Elevation. Ebenerdige Abschnitte (±4 m) formen das Gelände
  (`ConformTo`), Wasser nur per Brücke (≥ 2,5 m über Wasserspiegel) oder **Tunnel unter dem Gewässergrund** (≥ 7 m darunter, seit
  Wasserstraße Stufe 3). Über einer **Fahrrinne** verlangt die automatische Trassierung die lichte Durchfahrtshöhe + Tragwerk
  (`INavigableGround`/`Profiles.WaterFloor`, s. „Wasserstraße & Hafen“).
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
  auf das alte Material, wenn er fehlt). **Seit Umwelt (Etappe 6) nach Klima/Jahreszeit:** trockenes gelbes Gras
  (`ClimateModel.GrassDrynessOn`: Dürre, Sommerhitze, trockenes Grundklima) und Schneedecke (`SnowCoverOn`, Grad-Tag-Modell, in
  Höhenlagen dichter, nicht auf Fels/Wasser); einmal je Spieltag, bei merklicher Änderung werden alle Kacheln nach und nach (3 je
  Bild) neu eingefärbt (nur Vertex-Farben). Optisch ungeprüft.

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
- **Seit „Setzen statt Zonen“ (Spielerentscheidung):** Zonen sind ein **Altbestand**. Neue Städte setzen alle Gebäude
  (s. „Setzen statt Zonen“); die Zonen-Kategorie erscheint in der Bauleiste nur, solange der Spielstand gemalte Zonen hat
  (`CityStats.ZonedCells > 0`), Taste Z gehört dem Schwerpunkt-Pinsel. Der Code bleibt: alte Stände mit Zonen wachsen,
  zonen und reißen ab wie bisher (Test `OldSaveWithZones_KeepsGrowing_AfterLoad_Deterministic`). Ohne gemalte Zonen wächst
  nichts (`Grow` findet keine freie Zonenzelle; `NewGame_WithoutZones_NothingGrows_…`); geprüft: Nachfrage, Bodenwert-
  Standortwahl (`LotPreference` wirkt nur beim Wachsen), Zuzug, Gateways, Firmen und Tests hängen nicht an Zonen.
  `ZoneView` nur beim Zonen-Pinsel (nicht beim Schwerpunkt-Pinsel), Grundstücksplatten nur an Zonengebäuden.

## Bürger (G2)
- `Simulation/Citizens`: `Citizen` (Alter, Bildung, Haushalt/Wohnung, Job, Bedürfnis Einkaufen, Tätigkeit, Fahrt),
  `Household`, `CitizenSystem` (jeden Tick Tagesablauf, alle 5 Ticks Zuzug/Jobsuche/Statistik, täglich Altern),
  `RoadComponents` (zusammenhängende Netzteile = Vorprüfung „erreichbar“).
- Zuzug: wie G1 ein Haushalt je Gebäude und Durchlauf, solange `MoveInPull > 5` (Single/Paar/Familie/Alleinerziehend/
  Senioren). **Im Spiel nur über einen Anschluss** (`WorldSettings.RequireGatewayForMoveIn`, ersetzt die alte Regel „kein
  Außenanschluss nötig“; s. „Außenanbindungen (Gateways)“): Wohnung muss im selben Netzteil wie die Einfahrt eines befahrbaren
  Gateways liegen, ein Erwachsener fährt vom Gateway zur neuen Wohnung (normale Verkehrsmittelwahl). Tests ohne Schalter wie
  früher (sofort in der Wohnung). Jobsuche: nächster freier passender Job im selben Netzteil (Büro braucht
  mittleren Abschluss, Gebildete bevorzugen Büro). Ohne Job hier → Auspendler über den nächsten Außenanschluss;
  Auspendler wechseln zu freien Jobs hier. Freie Jobs ohne einheimische Bewerber → Einpendler (Bürger ohne Haushalt);
  Einwohner verdrängen Einpendler. Altern am Geburtstag, 18 = Abschluss, 65 = Rente, ab 80 Sterbewahrscheinlichkeit,
  Wegzug/Umzug/Jobwechsel entscheidet seit dem Bedürfnismodell die Utility AI (s. u.); bei Abriss Umzug in freie Wohnung oder Wegzug.
- Tagesablauf: Arbeitsbeginn je Bürger 6:00–9:30, 8 h Arbeit; danach (bzw. tagsüber ohne Job) einkaufen in
  Einzelhandel/Mischgebiet 10–20 Uhr, wenn das Bedürfnis ≥ 1 ist (ein Einkauf versorgt den Haushalt); dann heim.
  Kinder/Schüler bleiben vorerst zu Hause (ab 12 Jahren Freizeit im Viertel, s. Bedürfnismodell).
- Fahrten: Anteil `CarShare` (35 %, Einpendler 60 %) × `TrafficSystem.Demand` fährt als echtes Auto
  (`TrafficSystem.RequestTrip` von Zufahrt/Außenanschluss zu Zufahrt/Außenanschluss), höchstens 70 % von `MaxVehicles`.
  Rest = Platzhalter (bis 900 m zu Fuß mit 1,3 m/s, sonst Rad 4,5 m/s – in Verkehrszeit, s. „Zeitmodell“) bzw. seit ÖPNV Stufe 1 Bus (s. „ÖPNV“). Ankunft meldet `TrafficSystem.TripEnded`
  (auch bei Abbruch → Bürger kommt trotzdem an; Sicherheitsnetz 3 Tage). Handler dürfen dort keine Fahrten starten.
  **Zeitmodell** (früher „Zeitverzerrung“: 0,25 s Verkehr je 6-min-Tick, Fahrten dauerten Spielstunden bis Tage): 45 s Verkehr je
  Tick, eine Stadtfahrt dauert jetzt typisch 10–50 Spielminuten (Ø 25). Ankunft wird im nächsten Kalender-Tick verarbeitet.
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
  Strom-/Wasserausfall, Lärm, Abgas, Unsicherheit, schöner Park, Bummel, kein Laden, Lebensereignisse u. a.; seit „Sicherheit“ auch
  Brand (`Fire`) und Einbruch (`Burglary`); seit „Flughafen“ Fluglärm (`AircraftNoise`).
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
  `Passenger.StartTime` bis `Passenger.ReadyTime` (dann steht er wirklich an der Haltestelle, s. „Zeitmodell“; Verkehrszeit →
  Ticks über `SimulationWorld.TickTime`), dann Stehen auf dem Gehweg der Halteseite (`WaitPoint`: 0,6 m zum Bordstein, ±2,5 m gestreut, Blick zur Straße;
  `StopSide.Both` → Seite nach Id). Im Bus → unsichtbar. Nach dem Aussteigen Fußweg vom Gehweg des Ziel-Halts zum Ziel in den
  letzten Ticks vor `PlannedArrivalTick` (gleiche Formel `CitizenSystem.EgressTicks`: Gehtempo in Verkehrszeit, + 3 Ticks außerhalb).
  Die Fahrt entfernt das Bürgersystem beim Aussteigen – `TransitPedestrians.Observe` merkt sie sich je Frame. Statistische
  Fahrgäste (`TransitRealShare`) und unbeobachtete Fahrten: unsichtbar.
  **Zeitbasis (umgebaut, s. „Zeitmodell“):** `StartRide` setzt `ReadyTime` = Start + Zugangsweg (Luftlinie × 1,25 ÷ 1,3 m/s),
  vorher steigt der Fahrgast nicht ein; Platzhalter-Fußwege, Fußweg vom Ziel-Halt und Parkwege rechnen mit Gehtempo in
  Verkehrszeit.
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
- **Gehtempo (Zeitmodell, früher „Zeitverzerrung“ mit 600 m in 0,2 s):** `PedestrianPlanner.WalkAtPace` geht mit 1,3 m/s
  Verkehrszeit ab dem Start (nur schneller, wenn die Route länger ist als die geplante Zeit; danach Warten am Ziel) – Info-Test
  misst Median 1,30 m/s. Rad/Sonstiges (> 900 m Luftlinie) ist unsichtbar (noch kein Rad). Spaziergang ≈ 1,1 m/s. In Echtzeit bei
  Tempo 1 ≈ 30-fach (Zeitraffer der Verkehrszeit); Schrittfrequenz über `SimulationRunner.AnimationTimeScale` (max. 3×).
- Leistung (Testprojekt, Info): Raster 13×13 Straßen (312 Segmente) → Gehwegnetz 17 ms, 500 Routen 75 ms (inkl. JIT).
  Unity noch nicht gestartet (Optik, Frame-Zeiten ungeprüft).

## Spiel-UI (UI Toolkit, Phase 1)
- `Game/UI/Hud/GameHud` (+ `GameHud.BuildBar.cs`), Begleiter am `RoadTool` (zuletzt angehängt). Erzeugt zur Laufzeit
  `PanelSettings` (ScaleWithScreenSize, Referenz 1920×1080, Match 0,5) + `UIDocument`; lädt
  `Resources/UI/CityTheme.tss` (importiert nur `unity-theme://default` → Standardschrift) und `Resources/UI/CityHud.uss`
  (Design-Token als `--cb-…`-Variablen, Klassen `cb-…`). Aufbau in C#, weil die Inhalte aus Katalogen kommen.
  Fehlen Theme/USS, hängt das RoadTool die alte IMGUI-`RoadToolbar` als Rückfall an.
- Inhalt: obere Leiste (Stadtname `GameHud.CityName` = `World.Realm.CityName`, Datum mit Anzeige-Kalender 12×30 Tage, Tempo,
  Einwohner, Arbeitslose, **Nachfrage W/L/B/I** mit Tooltip „warum“ (Setzen statt Zonen, `GameHud.Demand.cs`), Strom-/Wasser-Warnchip nur bei unterdecktem Netz, Info-Knopf = Stadtübersicht), linke Ansichtsleiste
  (Stau, Lärm, Luftqualität, Bodenbelastung, Grundwasser, Umwelt-Fenster, Bodenwert, Rohstoffe, Strom, Wasser, Dienste-Abdeckung, Abwasser, Wasserqualität, Medien, Brandgefahr, Kriminalität, Unterirdisch), Bauleiste („Auswählen“ ganz links, Kategorien – seit „Setzen statt Zonen“ Straßen, Autobahn, **Bauen** (6, Reiter Wohnen/Gewerbe/Industrie), **Lenken** (Z), Zonen nur bei alten Ständen, Versorgung … – → Werkzeugzeile + Kacheln + Infozeile;
  aktive Kategorie erneut = Auswählen, s. „Auswählen-Modus & Verschieben“),
  Hinweiszeile aus `RoadTool.Guide` (Fehler/Info/Länge/Kosten) + `RoadTool.ReturnHint`, Tooltips, Toasts.
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

## Auswählen-Modus & Verschieben (UX-Runde)
- **Standard = Auswählen** (`RoadTool.SelectMode` = der vorhandene `Mode.Inspect`; bewusst kein neuer Enum-Wert, damit
  Inspektor/Picking/Views und Werkzeuge anderer Agenten unverändert passen). Start in diesem Modus, keine Bauvorschau,
  Linksklick wählt Gebäude, Fahrzeug, Bürger, Dienst, Versorger, Haltestelle/Depot.
- **Rückkehr:** Esc bzw. Rechtsklick (ohne laufenden Vorgang) → Auswählen; Werkzeugtaste erneut (K B U I T Z J L 4 5 6 7) →
  Auswählen (`RoadTool.ToggleMode`); R bleibt Gerade ↔ Kurve. Aktive Kategorie in der Bauleiste erneut anklicken → Auswählen.
  In Zonen/Gelände hat Rechtsklick eigene Bedeutung, dort nur Esc. Laufende Vorgänge melden Begleiter mit
  `RoadTool.RegisterOperation(busy, esc, rechtsklick)` (Leitungskette, Buslinie zeichnen, Verschieben): dann bricht Esc/RMB
  nur diesen Vorgang ab. Esc, das ein Fenster verbraucht (`WindowHost.EscapeConsumedFrame`), wirkt nicht zusätzlich.
  Hinweiszeile: `RoadTool.ReturnHint` („Esc/Rechtsklick: zurück zum Auswählen“).
- **Bauleiste:** Knopf „Auswählen“ (Icon `HudIconKind.Cursor`) ganz links, aktiv im Auswählen-Modus; dort ist keine
  Kategorie offen. **Inspektor-Panel** öffnet sich erst mit einer Auswahl (`InspectorPanel.WantsPanel`); Stadtübersicht
  nur über Info-Knopf oben bzw. C (`RoadTool.ToggleCityInfo` → `InspectorPanel.ShowOverview`); × schließt, Modus bleibt.
  Zonen kräftig: Zonen-Werkzeug oder Stadtübersicht.
- **Verschieben** `Game/Inspect/FacilityMover` (Begleiter): Dienst/Versorger/Busdepot/gesetztes Wohnhaus auswählen → „Verschieben (Y)“ im
  Inspektor oder Taste **Y** (einziger freier Buchstabe, gedrucktes Zeichen der Belegung) → Geist mit Vorderseiten-Pfeil folgt der
  Maus mit denselben Platzregeln inkl. Ausrichtung, `,`/`.` drehen, Klick setzt (Stadtkasse 10 %), Esc/RMB bricht ab.
  Depots: `TransitSystem.SnapDepot` + `PlanMoveDepot`/`MoveDepot`.
- **Vorschau:** `Game/UI/PlacementGhost` (Quader + Streifen an der Vorderkante + Pfeil zur Zufahrt) in Dienst-, Versorgungs- und
  Verschieben-Werkzeug; auch als rote Hervorhebung des Bauwerks, das eine geplante Straße blockiert.

## Inspektor (Auswählen-Modus, Übersicht Taste C)
- `Game/Inspect/InspectorPanel` (Begleiter am `RoadTool`, ersetzt das frühere `CitizenPanel`), `InspectorPicking`.
  Liest nur. Klick = Strahl gegen Gebäude (Grundstück × `VisualHeight`), Versorgungsbauwerke (Fläche × grobe
  Platzhalterhöhe), Fahrzeuge und Fußgänger (Kugel um die interpolierte Position, kleiner Vorrang vor Gebäuden); Rückfall:
  Geländepunkt unter der Maus. Rechtsklick/Klick ins Leere hebt auf (Panel schließt, außer die Stadtübersicht ist offen),
  „← Zurück“ geht den Verlauf zurück.
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
- `Simulation/Utilities`: `UtilityCatalog` (Kohle-/Gaskraftwerk, Windrad, Solarpark, Wasserpumpe am Ufer, Brunnen+Wasserturm; Biogas, Wasserkraft, Offshore, Batteriespeicher s. „Energie erweitert & Dienstfahrzeuge“;
  Leistung Strom kW / Wasser m³/h, Baukosten/Unterhalt bucht die Stadtkasse ab, s. u.), `UtilitySystem` (alle 30 min).
- Bauwerke frei platziert und gedreht (`PlanFacility` → `BuildFacility`): nicht auf Straßen/Wasser/anderen Bauwerken,
  Pumpe braucht Wasser in Reichweite (Gelände `IsWater`), Fläche wird eingeebnet, Gebäude darunter werden abgerissen.
  **Straßen dürfen Bauwerke nicht überbauen** (Bauwerks-Sperre, s. Straßen). Auf Bauwerksflächen wächst nichts (`CitySystem.LotBlocker`-Hook).
  **Straßenfront (UX):** `UtilityFacilityType.NeedsRoadFront` für Kohle-/Gaskraftwerk, Wasserturm, Kläranlage, Umspannwerk
  (Brennstoff/Personal/Schlamm/Wartung). Steht so ein Bauwerk an einer Stadtstraße (Rand ≤ 8 m),
  muss die Vorderseite zu ihr zeigen, sonst `UtilityBuildError.NoRoadAccess`; Überland ohne Straße in der Nähe (Anschluss per
  Leitung) bleibt erlaubt, weil Brennstoff/Personal noch nicht simuliert sind. **Ausnahmen** ohne Front: Windrad, Solarpark
  (niemand fährt im Alltag hin), Pumpe, Atomkraftwerk, Abwasser-Auslass (richten sich nach dem Wasser). Der Netzanschluss
  läuft nur über Leitungsenden am Rand (Straßen tragen nichts mehr). `PlanFacilityAligned(type, cursor, drehung)`
  richtet aus; `PlanMove(id, pos, front)`/`MoveFacility(plan)` verschiebt (Id/Leistung bleiben, 10 % Umzugspreis
  `MoveCostShare`, Masten auf der neuen Fläche werden umgelegt, Event `FacilityMoved`).
- **Netze werden selbst verlegt** (Entscheidung des Spielers, ersetzt die alte Regel „Stadtstraßen tragen mit“):
  `ConnectionRules.CarriedByRoads(kind)` = false für alle Arten. **Leitungsarten** `UtilityLineType` im `UtilityCatalog`
  (`Id` = Speicher-Schlüssel, `LineTypeById`): `WaterPipe` 10 €/m, `SewerPipe` 12 €/m (unterirdisch), `HighVoltageLine`
  30 €/m (Ebene `GridLevel.HighVoltage`, Masten 4×4 m, Spannweite 90 m, **keine Hausanschlüsse**), `OverheadLine`
  15 €/m (Ortsnetz, Masten 2×2 m / 45 m wie bisher, brauchen Platz neben der Straße), `UndergroundCable` 40 €/m (Ortsnetz,
  unterirdisch, kein Platzbedarf). `PlanLine(type, a, b)` → `BuildLine`; `PlanLine(kind, …)` nimmt `DefaultLineType`
  (Strom = Freileitung). `UtilityLine.Type` (Kind/Level abgeleitet). Enden verbinden sich im Umkreis 3 m mit Leitungen
  **derselben Art und Ebene** (auch mittendrin) und mit Bauwerken, die die Art annehmen (`UtilitySystem.Accepts`).
  **Gebäude** hängen an jeder Leitung mit `ConnectsBuildings` ≤ `ConnectionRules.BuildingReach` 12 m von der Zufahrt
  (bzw. Leitungsende im Gebäude); `ConnectedLine(b, kind)` = Hausanschluss. **Einrasten** (`ResolveLinePoint(type, …)`):
  Rohre/Kanäle/Erdkabel unter dem Gehweg (`SidewalkInset` 2,5 m innerhalb des Straßenrands, Seite des Mauszeigers),
  Freileitungen/Hochspannung auf der Straßenmitte (Masten rücken neben die Straße; ein Strang versorgt beide Seiten).
  Zusammenhang per Union-Find, neu berechnet nur bei Änderung (Straßen-/Stadt-/Bauwerks-Version), alles nach Id sortiert.
- **Strom mit Spannungsebenen** (`UtilitySystem.PowerGrid.cs`): Kraftwerk → **Hochspannung** → **Umspannwerk**
  (`UtilityCatalog.Substation`, `FacilityRole.Substation`, 20 MW, 24×20 m, 18 000 €, Zufahrt; Modell später über
  `OwnModelCatalog` „Substation“ → `power_substation`, bis dahin Platzhalter) → **Ortsnetz** → Gebäude. Das Umspannwerk hat
  zwei Knoten (Oberseite Hochspannung, Unterseite Ortsnetz; `NetworkId` = Ortsnetz, `UpstreamNetworkId`, `Load`). Große
  Kraftwerke speisen nur in die Hochspannung; **Wind/Solar** (`FeedsLocalGrid`) speisen ins Ortsnetz, wenn ein Ortsnetz-Ende
  sie berührt, sonst in die Hochspannung (Entscheidung: kleine Erzeuger brauchen kein Umspannwerk, wie Bürgersolar).
  Bilanz: Fehlmenge jedes Ortsnetzes (Bedarf − eigene Erzeugung) verteilt sich nach Kapazität auf seine Umspannwerke,
  je Umspannwerk ≤ Kapazität (**begrenzt die Last**); reicht die Hochspannung nicht, bekommt jedes Umspannwerk denselben
  Anteil; im Ortsnetz dann wie bisher älteste Gebäude zuerst. Ortsnetz ohne Umspannwerk/Erzeuger = unversorgt. Keine
  Rückspeisung ins Hochspannungsnetz. `UtilityNetwork`: `Level`, `Generation`, `Imported`, `TransformerCapacity`,
  `Substations`, `HasSource`; `TotalProduction` = Erzeugung (ohne Doppelzählung), `TotalDemand` = Gebäude (ohne Hochspannung).
  `IsRequired(Power)` zählt Umspannwerke nicht.
- **„Netz entlang Straßen verlegen“** (`UtilitySystem.AlongRoads.cs`): `PlanAlongRoads(type, segmentIds = null)` /
  `PlanAlongRoad(type, segId)` → `AlongRoadsPlan` (Stücke, Kosten/Länge der gültigen, `Existing` übersprungen, `Invalid` mit
  `FirstInvalid`, `BuildingsReached`, Fehler `InsufficientFunds`/`TooShort`) → `BuildAlongRoads(plan)` zu den normalen
  Meterpreisen. Unterirdisch: unter dem rechten Gehweg, bei breiten Straßen (Gegenseite > 11,5 m) beidseits, an den Knoten
  kurze Stücke zur Mitte (alle Stränge treffen sich dort); oberirdisch auf der Mitte; Kurven in Sehnen ≤ 1,5 m Fehler.
  Freileitung an bebauter Straße: Stücke ohne Maststandort sind ungültig (kein Abriss) → Erdkabel.
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
  Unterirdische Leitungen (Rohre, Kanäle, Erdkabel) haben keine Masten und dürfen unter Straßen liegen. Grundfläche und
  Spannweite je Leitungsart (`UtilityLineType.PoleSize/MaxSpan/PoleFootprintHalf`, `PoleBlocks` für Platzprüfungen).
- **Bilanz** je Netz: Erzeugung gegen Bedarf (Bedarf aus Haushalten/Jobs-Kapazität, Industrie mehr). Unterdeckung:
  älteste Gebäude (kleinste Id) zuerst, kleinere können noch nachrutschen; der Rest ist unversorgt.
- **Abfrage für die Stadt**: `world.Utilities.IsSupplied(b)`, `IsSupplied(b, kind)`, `GetStatus(b)` (ja/nein + Ticks ohne),
  `IsSatisfied(b)` (versorgt oder Art noch nicht verlangt), `NetworkOf(b, kind)`.
- **Folge**: > 2 Spieltage ohne Strom oder Wasser → Gebäude wird verlassen (abgerissen, `BuildingAbandoned`). Erst wenn
  es mind. einen Versorger dieser Art gibt (Startphase folgenlos, Dauer zählt erst ab dann). `AbandonmentEnabled` schaltet ab.
- Kraftwerke stempeln Lärm (nach dem `NoiseSystem`). **Abgas rechnet seit Etappe 6 das `EcologySystem`** (s. „Umwelt (Etappe 6)“;
  ohne volles Modell exakt die alte Rechnung, die Merker `pollutionActive` bleiben im Spielstand von Versorgung/Diensten).
- Darstellung (`Game/Utilities`, Begleiter des `RoadTool`, Modus `RoadTool.Mode.Utility`): `UtilityTool` (J; Bauleiste
  „Versorgung“, , . drehen, Leitungen in Kette, RMB beendet; Rohre erzwingen die Unterirdisch-Ansicht), `UtilityView`
  (Platzhalter aus Grundkörpern, Masten an den Simulations-Standorten mit Fundament und Seilen, Anschlussseil zum
  Leitungsende, Rohre 2,5 m unter Gelände nur unterirdisch sichtbar; Vorschau im Werkzeug zeigt die Masten, ungültige rot),
  `UtilityOverlayView` (O: Strom → Wasser → aus; Marker grün/rot über Gebäuden, wachsen mit der Ausfalldauer),
  `UtilityPanel` (Erzeugung/Bedarf je Netz).
  **Netze selbst verlegt:** Werkzeuge `UtilityAction.HighVoltageLine`, `PowerLine` (= Freileitung Ortsnetz), `PowerCable`,
  `WaterPipe`, `SewagePipe`, `AlongRoads` (Art wählbar: Erdkabel/Freileitung/Wasserrohr/Kanal; Straße unter dem Mauszeiger oder
  „Alle Stadtstraßen“, Vorschau-Striche mit Kosten, Klick bzw. „Jetzt verlegen“ baut; Esc bricht „alle“ ab, über
  `RoadTool.RegisterOperation`); unterirdische Arten schalten die Unterirdisch-Ansicht ein. Bauleiste „Versorgung“: Kacheln
  Umspannwerk (MW), Hochspannung, Freileitung, Erdkabel, Wasserrohr, Abwasserkanal, „Entlang Straßen“ + Zeile „Entlang Straßen“.
  `UtilityView`: Hochspannung als Stahlmast 26 m mit zwei Traversen, Freileitung als Holzmast 9 m, Erdkabel orange 1,2 m tief,
  Hausanschlüsse unterirdisch (blau/braun/orange) bzw. Seil vom nächsten Mast zum Haus. Fenster/Inspektor: „Hochspannung n“ /
  „Ortsnetz n“ mit Erzeugung, über Umspannwerke bezogen, Kapazität; Gebäude: Anschluss (Art, Abstand) oder „kein Anschluss“;
  Umspannwerk: Last/Kapazität in MW, Hochspannungs- und Ortsnetz. Unterdeckungs-Meldung nur für Netze mit Quelle.
  **Speichern:** Abschnitt `utilities` v2 (Leitungsart, Umspannwerk-Oberseite/Last, Netzebene/Umspannwerte, Übergangsfrist);
  v1 → Leitungen = bisherige Art (Strom = Freileitung), Netze werden neu aufgebaut, `MigrationGraceTicks` 5 Tage ohne Ausfallzählung.
- Später (Abwasser/Atom: s. „G3 erweitert“): Pumpe braucht Strom, Wind/Sonne schwankend (Tageszeit/Wetter), Brennstoff aus Warenketten,
  Versorgung in Nachfrage/Zufriedenheit (G2/G6), Masten/Rohre folgen späteren
  Geländeänderungen (Terraforming zu Wasser unter einem Mast wird noch nicht geprüft), **Rohrdurchmesser/Kapazität**
  (Haupt- vs. Verteilleitung – bewusst später: ohne Druck-/Durchflussmodell wäre es nur ein zweiter Preis; sinnvoll erst mit
  Leitungskapazität je Strang und Engpass-Anzeige), Rückspeisung Ortsnetz → Hochspannung, Umspannwerk-Größen, Spannungsfall
  über lange Ortsnetze, Masten an Kreuzungen zusammenlegen („Entlang Straßen“ setzt je Stück eigene Endmasten),
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
- **`ConnectionRules`** (gemeinsamer Baustein für Strom-Ortsnetz, Wasser, Abwasser, s. G3): `CarriedByRoads(kind)` = false,
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
  Fischsterben/Tourismus, Grundwasser-Verschmutzung. (Wasser/Strom auf `ConnectionRules`: erledigt, s. G3.)

## Umwelt (Etappe 6, agent/environment)
- **Simulation** `Simulation/Environment/EcologySystem` (+ `.Ground.cs` Boden/Grundwasser/Bäume, `.Effects.cs` Hooks, `.Save.cs`;
  Zahlen in `EcologyRules`; `world.Ecology`, direkt nach Versorgung und Diensten, `TickInterval` 1: Verkehr zählen je Tick, Luft alle
  5 Ticks, Boden täglich). Wetter in `World/ClimateModel.Weather.cs` (Klima jetzt `partial`, zustandslos): `WindOn(day)` → `WindState`
  (Richtung, in die die Luft zieht; Herkunft in Grad; Stärke 0..1 ≈ 0–12 m/s, Ø 0,46; Hauptwind nach Breite – Passat aus Ost, 30–60°
  Westwind, polar Ost – Wetterlagen drehen ±100° über ~3 Tage), `InversionOn` (Winterhalbjahr, < 3 °C, windstill, trocken),
  `DroughtOn` (Regen 20 Tage vs. Jahreszeit-Mittel × Wärme), `HeatStressOn` (Tageshöchstwert ≈ Mittel + 6 K, ab 30 °C),
  `SnowCoverOn` (Grad-Tag-Modell 40 Tage), `GrassDrynessOn`.
- **Standard aus** (`Enabled`): dann rechnet es die Luft **exakt wie früher** UtilitySystem/ServiceSystem (nur Kraftwerke und
  Verbrennung, gleiche Reihenfolge und Werte; Test `Legacy_CoalPlantOnly_SameAsBefore`, alle 479 alten Tests unverändert grün).
  `SimulationRunner.environmentModel` schaltet ein (auch nach dem Laden). Alle Hooks liefern ohne Modell die alten Werte.
- **Luft** (weiter `world.Environment.AirPollution`, gleiche Einheit; Smog ab `SmogThreshold` 0,3) je Durchlauf (30 min):
  **Verkehr** = echte Fahrzeuge je 16-m-Zelle, jeden Tick gezählt und gemittelt (Pkw 1, Lkw 3,5, Bus 2,2, Tram 0; unter 2 m/s × 1,6;
  0,06 je Fahrzeug-Mittel), **Schiffe** (Wasserstraße & Hafen: Frachter 0,25 × Länge, Passagier 0,15 × Länge, am Kai × 0,4),
  **Kraftwerke** = Katalogwert × Auslastung (`PlantLoad`: geliefert/Erzeugung des Netzes × Verfügbarkeit, Bereitschaft 15 %) × Filter,
  **Industrie** = `AirPerJob(Rezept)` × Jobs × Produktivität (Eingänge fehlen/Lager voll × 0,3) × Filter, Radius 64 m + halbe Größe
  (Stahlwerk 0,001 je Job ≈ Kohlekraftwerk bei 60 Jobs; Raffinerie, Zement, Glas, Zechen …; Büro/Laden 0), **Heizen** = bewohnte
  Wohnungen × 0,0007 × Heizbedarf des Klimas × Bauart (EFH 1,3, MFH 1, Block 0,8, Mischgebiet 0,9; Stufe 2/3 × 0,9/0,8) + Beschäftigte
  × 0,00015, **Müllverbrennung** = Katalogwert × (0,3 + 0,7 Auslastung) × Filter. Danach **Wind** (Upwind-Verschiebung bis 0,6 Zellen je
  Durchlauf × Stärke, über den Rand verlässt die Luft die Karte), Diffusion 0,1, Abbau (0,05 + 0,05 × Wind + 0,05 × Regen/6 mm) ×
  Inversion (Abbau × 0,4, Diffusion × 0,5, Wind × 0,3) + **Wald** 0,06 × Waldraster + **Parks** 0,04 auf der Fläche (+16 m).
  Statistik: Ausstoß je Quelle (`EmissionOf`, `EmissionYesterday`, `ShareYesterday`), Luft an Wohnungen (`HomeAirAverage/Max`,
  `SmogBuildings`), `SmogAlert` (≥ 5 Wohngebäude und ≥ 10 % im Smog, Hysterese) + Event `SmogAlertChanged`.
- **Boden & Grundwasser** (eigene Raster `Soil`, `Groundwater`, täglich): Ablagerung 0,003 × Tagesmittel Luft; Industrie nach Rezept
  (`GroundPerJob`: Raffinerie, Ölförderung, Stahl, Zechen/Erz – saure Grubenwässer –, Färberei, Elektronik, Dünger) × Filter;
  **Müllhalde** Boden 0,006 und Sickerwasser 0,008/Tag (Radius + 60 m) × (0,3 + 0,7 Füllstand); Verbrennung Asche; Abwasser-Auslass
  Uferboden. Auswaschung Boden → Grundwasser 0,1 %/Tag, Grundwasser breitet sich 3 %/Tag aus; Erholung Boden 0,4 %/Tag (mit Wald ×1,5),
  Grundwasser 0,3 %/Tag. **Chronische Belastung** `Exposure` = Tagesmittel der Luft, geglättet (≈ 10 Tage).
- **Wirkungen** (`Connect`, Hooks): Fruchtbarkeit = Vorkommen × (1 − 0,75 × Boden) × (1 − 0,3 × Luft) über `GoodsSystem.DepositAt`
  (Standortwahl und Güte der Höfe; Mindestgüte 0,25 → belastete Äcker können ausfallen); **Ernte** × (1 − 0,5 × Hitze) × (1 − 0,45 ×
  Dürre) über `GoodsSystem.HarvestFactor`; **Brunnen** = 1 − Grundwasser (schlechteste Zelle 3×3) über `WaterQualitySystem.WellQuality`
  → Trinkwasser/Gesundheit; **Gesundheit** (`CitizenSystem.ChronicAirExposure`): Zielwert − 0,4 × Belastung × Empfindlichkeit (unter 6 J.
  1,6, Kinder 1,25, ab 60 1,4, ab 75 1,8), Krankheitschance × (1 + 2 × …), Erinnerung „Smog“ ab 0,25; Ruhe/Zufriedenheit wie bisher über
  die Luft an der Wohnung; **Bodenwert** Faktor „Altlasten (Boden)“ −0,12 × Belastung (`LandValueSystem.ContaminationAt`);
  **Tourismus** `TourismFactor` (vorgemerkt); **Schlagzeilen** neue Themen `Smog`, `FishKill` (Wasserverschmutzung ≥ 0,6),
  `EnvironmentRules` (Auflagen verschärft; hinten angefügt).
- **Gegenmaßnahmen mit Kosten:** **Umweltauflagen** `SetRegulation(0–3)` – Emission von Kraftwerken, Industrie, Verbrennung × 1 / 0,65 /
  0,4 / 0,22; Firmen zahlen je Job und Tag 0 / 5 / 12 / 22 € × Gewicht des Rezepts (Stahlwerk 1, Büro 0) → neue Ledger-Zeile
  `FirmLedger.Environment` (mindert Gewinn und Gewerbesteuer, Hook `GoodsSystem.EnvironmentCostFor`), städtische Kraftwerke/Verbrennung
  + 20 / 45 / 80 % Unterhalt (`IUpkeepSource` „Umweltschutz“). **Umweltzone** `SetLowEmissionZone(an, Mitte, Radius 200–1200 m)`:
  Lkw × 0,4, Pkw × 0,85 in der Zone; Firmen in der Zone 1,5 €/Job/Tag, Stadt 400 €/Woche. **Bäume pflanzen** `PlanTrees(Mitte, Radius
  ≤ 80 m)` → `TreePlan` (freie Zellen: nicht Wasser, Sperre `TerrainMap.LockQuery` = Straßen, Gebäude, Bauwerke, Gleise; noch nicht
  dicht bewaldet; 6 Bäume je Feld × 30 €) → `PlantTrees` bucht „Umweltschutz (Bäume)“ (`BudgetCategory.Environment`, hinten angefügt),
  Setzlinge 0,05 im Waldraster wachsen 0,012/Tag bis 0,8 (≈ 2 Monate) – die Landschaftsbäume (`ScenicPropsView`) zeigen sie, Forstbetriebe
  können sie ernten; Straße/Gebäude darüber beendet die Aufforstung dort. Pflege 1 €/Feld/Woche. Lärmschutzwand gab es schon.
- **Darstellung:** Ansicht „Luftqualität“ (P, gestreckt × 2,5, Legende mit Wind/Inversion), neue Ansichten **Bodenbelastung** und
  **Grundwasser** (`EnvironmentOverlayView.Overlay.Soil/Groundwater`, Knöpfe in der Ansichtsleiste, keine neuen Tasten), **Umweltfenster**
  (`UI/Windows/EnvironmentWindow`, rechts, Baum-Knopf links: Luft an Wohnungen, Wetter, Quellen gestern als Balken, Boden/Wasser,
  Ernte-Stress, Tourismus, Auflagen-Stepper mit Kosten, Umweltzone einrichten/aufheben/Radius, Pflanz-Pinsel). **Bäume-Pinsel** im
  Dienste-Werkzeug (`ServiceTool.ServiceAction.PlantTrees`, `ServiceTool.Trees.cs`, Kachel „Bäume pflanzen“, LMB halten = 4×/s, `[`/`]`
  Radius, Ring + Hinweis). `EnvironmentAlerts` (Smog-Alarm/-Ende, Fischsterben → `HudMessages`), `SmogHazeView` (dezenter bräunlicher
  Dunst über `RenderSettings.fog` ab Luft 0,15 um den Blickpunkt, stellt die alten Nebelwerte wieder her), Gelände-Farben nach Jahreszeit
  (s. „Gelände“), Inspektor (`InspectorWindow.Ecology.cs`: Umfeld chronische Luft/Boden/Grundwasser/Umweltzone, Firma Ausstoß und
  Umweltkosten + Ledger-Zeile, Kraftwerk Auslastung/Filter, Brunnenwasser), Icons `Tree`, `Groundwater`, Hilfe (F1).
- **Spielstand** `ecology` v1 (Schalter, Auflagen, Umweltzone, Verkehrs-/Schiffszählung des laufenden Durchlaufs, Tagessumme der Luft,
  Raster Boden/Grundwasser/Aufforstung/Belastung, Zähler, Statistik); `goods` v4 (Umweltkosten, v3 → 0). `SaveLoadTests.BuildCity` hat
  Umwelt an, Auflagen 1 und eine Aufforstung.
- **Tests** `EcologyTests` (20): aus = Verkehr/Heizen ohne Abgas, alte Rechnung bitgleich, Wind verschiebt die Fahne ins Lee (11 von 11
  windigen Tagen), Wald (Abgas gesamt 56 %) und Park (12 % weniger an der Quelle) bauen ab, Klima (Wind Ø 0,46, Ostanteil 0,67, 4
  Inversionstage/Jahr gemäßigt; Schnee kalt Januar 1,0 / Juli 0, gemäßigt Januar 0,3; Gras trocken arid 0,95 vs. 0,04), Filter (Industrie
  397 → 159, Kraftwerk 86 → 34 je Tag; Stahlwerk 1 045 €/Tag Umweltkosten, Stadt +2 835 €/Woche), Umweltzone, Bäume (28 Felder 5 040 €,
  Waldraster 0,05 → 0,41 nach 30 → 0,80 nach 80 Tagen, Straße beendet), Boden → Fruchtbarkeit 0,40 → 0,22 und Ernte 21 → 0, Hitze/Dürre
  (gemäßigt schlechtester Tag × 0,80, heiß/trocken × 0,47), Deponie → Grundwasser am Brunnen 0,02/Brunnen 96 % nach 40 Tagen, direkt 0,5 →
  50 %, Smog (Luft 0,29) → Gesundheit −0,20 Erwachsene, −0,25 Kinder, −0,37 Ältere, Zufriedenheit sinkt, Altlasten → Bodenwert,
  Schlagzeilen Smog-Alarm/Fischsterben, Schiffe stoßen aus, Speichern mitten im Durchlauf bitgleich, Migration ohne Abschnitt,
  Determinismus. **Info Industrie- vs. grüne Stadt** (≈ 1 340 Einwohner, 14 Tage Januar): Luft an Wohnungen 0,103 vs. 0,048, Quellen
  Industrie/Kraftwerk 44/42 % vs. Heizen/Verkehr 57/43 %, Gesundheit 0,911 vs. 0,957, Boden max 0,024 vs. 0,001, Tourismus 0,88 vs. 0,96;
  Rechenzeit ≈ 0,6 ms je Luft-Durchlauf inkl. Tagesteil (128 × 128 Zellen).
- **Entscheidungen:** (1) Standard aus statt still neuer Werte – alte Tests/Welten bleiben bitgleich, das Spiel schaltet ein (wie
  Sicherheit). (2) Luft bleibt im Raster `AirPollution` (alle Leser – Bürger, Bodenwert, Inspektor – wirken weiter ohne Änderung).
  (3) Verkehr über echte Fahrzeugpositionen je Tick statt Segmentlisten (gleicher Effekt, ein Durchlauf; Platzhalterfahrten stoßen nicht
  aus). (4) Chronische Belastung als Raster statt Feld am Bürger (kein Umbau des Bürger-Spielstands). (5) Wald = vorhandenes Waldraster
  (Bäume pflanzen schreibt dort, die Landschaft zeigt es automatisch). (6) Kraftwerke stoßen nach Auslastung aus (kleine Stadt mit
  großem Kohlekraftwerk raucht nicht mehr voll – realistischer, im alten Modus unverändert).
- **Optisch ungeprüft** (Unity nicht gestartet): Ansichten/Legenden, Umweltfenster, Pflanz-Ring, Smog-Dunst (Nebel in URP), Schnee-/
  Trockenfarben, Icons.
- **Offen:** Lärm nach echter Verkehrsmenge/Industrie, Ozon im Sommer, Hochwasser/Starkregen, Klimawandel/CO₂ als Weltwert, Fernwärme/
  Wärmepumpen (Heizen ohne Abgas), Landstrom im Hafen, Tourismus als Nachfrage, Umweltzone frei malen (heute Kreis), Bodensanierung als
  Maßnahme, Grundwasser fließt nach Gelände (heute nur Ausbreitung), Bäume im Winter kahl, Firmen meiden strenge Auflagen bei der Standortwahl.

## Medien (G4 erweitert)
- Dienstart `ServiceKinds.Media` („media“, auch in `ServiceKinds.All`/Overlay): **Zeitungsverlag** (Zustellung übers Straßennetz,
  3 min, braucht Zufahrt), **Radiosender** (Funkradius 900 m) und **TV-Sender (Turm)** (1600 m) – `ServiceType.Broadcast` (technisch
  `Walking` = Luftlinie, keine Zufahrt). Kapazität = Leser/Hörer/Zuschauer, Bedarf = Einwohner, Kosten wie alle Dienste (Kasse).
- Bürger: `MediaReach` (ohne Medienhaus 0, nicht 0,5) → Freizeit +0,004 je Bewertung, Bildung Erwachsene +0,08;
  `NewsTone` → Gemüt ± 0,05 × Reichweite × Stimmung (vorsichtig dosiert).
- **`MediaSystem`** (`world.Media`, nach den Diensten): erkennt Ereignisse aus echten Daten – Stau (≥ 6 stehende Fahrzeuge je
  Segment), Strom-/Wasser-/Abwasserausfall (≥ 3 Gebäude), Steuersatz rauf/runter, Eröffnung Schule/Gesundheit/Park/Polizei/
  Feuerwehr/Medium, Atomkraftwerk gebaut/Störfall, Trinkwasser < 80 %, Zufriedenheit ± 4 Punkte zum Vortag, Einwohnermarken,
  Arbeitslosigkeit ≥ 12 %, Handel (Vertrag, Embargo, Zollerhöhung über `Report`, s. Etappe 8). Vorlagen in `Services/Content/Headlines.cs` (Platzhalter {Straße} {Anzahl} {Steuer} {Satz} {Name}
  {Einwohner} {Prozent} {Medium} {Land}, Ton je Thema, Medienhäuser „Stadt-Kurier“, „Radio Welle 98“, „Stadt-TV 7“).
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
- **Reichweite über das Straßennetz**: Dijkstra über Fahrzeit vom Anschlusspunkt (**Mitte der Vorderseite**, ebenerdige
  Stadtstraße vor ihr, Rand ≤ 8 m – sonst `NoRoadAccess` „Keine Zufahrt an der Vorderseite“; früher zählte jeder Rand); Tempolimit × 0,8, +5 s je Kreuzung (≥ 3 Straßen), Einbahnstraßen nur
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
  `FireRisk` = Grundrisiko je Zone × (1 − 0,8 × Feuerwehr) + 0,2 × Müllstand – alter Kennwert (Inspektor nur bei ausgeschalteter
  Sicherheit); echte Brandgefahr/Brände/Kriminalität: s. „Sicherheit: Brände & Kriminalität“.
- Platzregeln wie G3: nicht auf Straße/Wasser/anderen Bauwerken/Strommasten, max. 8 m Gefälle (wird eingeebnet), Gebäude
  darunter werden abgerissen. **Straßen durchs Dienstgebäude sind gesperrt** (Bauwerks-Sperre, `OnSegmentAdded` reißt nichts
  mehr ab). **Ausrichtung (UX):** `ServiceType.NeedsRoadFront` (= nicht `Walking`; Parks nicht) – `PlanAligned(type, cursor,
  drehung, movingId)` dreht die Vorderseite zur Straße (Cursor ≤ Tiefe + 16 m vom Straßenrand), `,`/`.` drehen zusätzlich, solange
  die Vorderseite Zufahrt hat. **Verschieben:** `PlanMove(id, pos, front)` / `MoveFacility(plan)` – gleiche Platzregeln (eigene
  alte Fläche zählt nicht), 10 % der Baukosten (`MoveCostShare`, Kategorie „Bau Dienste“), Id/Lager/Last/Statistik bleiben,
  Anschluss + Reichweite neu, Event `FacilityMoved`. Grundstücke (`CitySystem.LotBlocker`, verkettet mit
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
  Freizeit/Wahrzeichen aus `Models/Leisure/` bzw. `Models/Landmarks/` (s. „Freizeit, Wahrzeichen & Lärmschutzwand“); Zuordnung
  zentral in `Editor/OwnModelDescriptions.cs`, `Name.json` geht vor (s. „Eigene Assets“).
- Später: ~~Brände/Kriminalität als Ereignisse~~ ✅ (s. „Sicherheit: Brände & Kriminalität“), Dienstfahrzeuge im Verkehr (✅ Grundstufe: Müllwagen/Streife/Feuerwehr/Rettung, s. „Energie erweitert & Dienstfahrzeuge“; Reichweite weiter per Dijkstra-Schätzung), Sperrungen/
  Abbiegeverbote in der Reichweite, Müllhalde leeren/Recycling, (Medien: s. „Medien (G4 erweitert)“),
  Budgetregler je Dienst, Ausbaustufen.

## Freizeit, Wahrzeichen & Lärmschutzwand (Eigene Assets, 2026-11-03)
- **Als Dienstarten im `ServiceCatalog`** (gleiche Platzregeln, Front-Ausrichtung, Bauwerks-Sperre, Kosten/Unterhalt, Verschieben,
  Speichern, Kacheln in der Kategorie „Dienste“ – getrennt nach Art): neue `ServiceKinds.Leisure` („leisure“), `Sport` („sport“,
  Teilkanal) und `Landmark` („landmark“), beide Hauptarten in `ServiceKinds.All` (Overlay, Inspektor). Neue `ServiceType`-Felder:
  `ExtraChannel` (zweiter Kanal, Last zählt nur im Hauptkanal), `RoadFrontRequired` (Radius-Wirkung, aber Zufahrt nötig),
  `Unique`, `Strength` (Faktor auf die Entfernungswirkung, Standard 1 = bitgleich wie vorher), `Attractiveness`.
- **Freizeit** (Modelle Models/Leisure): Spielplatz 16×16 (Fußweg 250 m), Sportplatz 48×32 (600 m, + Sport), Tennisplätze 32×24
  (500 m, + Sport, Stärke 0,8), Freibad 40×32 (3 min Fahrt, Zufahrt, + Sport), Stadion 80×64 (6 min, Zufahrt, Lärm 0,6 / 260 m,
  Attraktivität 30), Riesenrad 24×24 (5 min, Zufahrt, Attraktivität 15). Bedarf = Einwohner (wie Parks), Sport/Wahrzeichen
  belasten nicht (`DemandOf` 0).
- **Wahrzeichen** (Models/Landmarks, Wirkung über Radius × Stärke): Rathaus 36×28 (**einmalig**, Zufahrt, 700 m, Flag
  `ServiceSystem.HasTownHall` als Voraussetzung für Etappe 7 „Land gründen“; zweites → `ServiceBuildError.AlreadyBuilt`),
  Kirche 24×36 (Zufahrt), Museum 34×30 (Zufahrt, Attraktivität 25), Burg 44×44 (900 m, 40), Burgruine, Denkmal, Brunnen, Obelisk,
  Musikpavillon (+ Freizeit). `ServiceSystem.Attractiveness` = Summe (Zahl für späteren Tourismus).
- **Bürger** (kleiner Block in `CitizenServiceBinding`, Hooks `CitizenSystem.LeisureCoverage/SportCoverage/LandmarkPride`, ohne
  solche Bauwerke 0 – kein 0,5-Neutralwert, weil Bonus): Freizeit + 0,012 × Abdeckung je Bewertung (≈ +0,12/Tag), Gesundheits-Ziel
  + 0,1 × Sport, Zufriedenheit + 0,04 × Stolz und Gemüt-Ziel + 0,03 × Stolz. Umfeld-Cache speichert die drei Werte (citizens v2).
- **Bodenwert:** Faktor „Wahrzeichen“ 0,10 × `CoverageAt(landmark)` (`LandValueRules.LandmarkWeight`, Beitrag im Inspektor).
- **Lärmschutzwand** `Simulation/Environment/NoiseBarrierSystem` (`world.NoiseBarriers`, Abschnitt „noisebarriers“): gerade Stücke
  4–400 m (`Plan(a, b)` → `Build`, Kette im Werkzeug), 60 €/m („Straßenbau“), Unterhalt 0,35 €/m/Woche („Lärmschutz“), Abriss 25 %;
  nicht auf Fahrbahn/Gehweg (ebenerdig), Bauwerken oder Gebäuden (kein Abriss), Straßen dürfen sie nicht durchqueren
  (Bauwerks-Sperre Art „barrier“), Grundstücke auf der Wand wachsen nicht (`LotBlocker`). `SnapToRoad(p)` rastet 1 m hinter der
  Gehweg-Außenkante auf der Seite des Mauszeigers ein. **Wirkung:** `NoiseSystem` stempelt Straßenlärm nahe einer Wand zellweise mit
  `Attenuation(quelle, zelle)` – kreuzt die Sichtlinie eine Wand, kommen 30 % direkt dahinter bis 70 % nach 160 m an; vor der Wand
  und ohne Wände bitgleich wie vorher (Dienst-/Versorger-Lärm wird nicht gedämpft). Werkzeug: Dienste-Werkzeug,
  `ServiceAction.NoiseBarrier` (Kachel „Lärmschutzwand“, Strg = frei, RMB/Esc beendet die Kette), Abriss über „Dienstgebäude“.
- Tests `OwnAssetsTests` (Freizeit → Freizeit/Gesundheit, Stadion braucht Zufahrt/macht Lärm, Wahrzeichen → Bodenwert/Stolz/
  Attraktivität, Rathaus einmalig, Wand dämpft dahinter und nicht davor, Sperren/Kosten, Speichern/Laden + Determinismus).
- Später: Freizeitanlagen als echte Freizeitziele im Tagesablauf (wie Parks), Stadion-Veranstaltungen mit Verkehr, Tourismus aus
  der Attraktivität, Wand-Lärmschutz auch für Dienst-/Industrielärm, Wände an Kurven entlang (heute gerade Stücke), Dienste dürfen
  noch auf Wänden platziert werden (Sperre fehlt dort).

## Energie erweitert & Dienstfahrzeuge (Eigene Assets 2, 2026-11-03, agent/models2)
- **Neue Versorger** im `UtilityCatalog` (hinten in `All`, Kacheln/Inspektor/Kasse/Verschieben automatisch; Modelle `Models/Energy`):
  **Biogasanlage** `power_biogas` (2 MW, 40×32 m, 16 000 € / 180 €/Tag, `RequiresRoadAccess` = Zufahrt Pflicht auch Überland,
  klein → Ortsnetz oder Hochspannung), **Laufwasserkraftwerk** `power_hydro` (66×56 m, 90 000 € / 450 €/Tag, `FacilitySite.River`,
  nur Hochspannung), **Offshore-Windpark** `power_offshore_wind` (4 × 5 MW, 180×180 m, 150 000 € / 1 100 €/Tag, `FacilitySite.Sea`,
  `Units` 4, nur Hochspannung), **Batteriespeicher** `power_battery` (`FacilityRole.Storage`, 10 MW / 20 MWh, 32×24 m, 30 000 € /
  120 €/Tag, Zufahrt, Ortsnetz oder Hochspannung). Neue Leitungsart **Hochspannungs-Seekabel/Erdkabel** `power_hv_cable` (90 €/m,
  Ebene Hochspannung, unterirdisch ohne Masten → kreuzt Meer/Straßen; Werkzeug `UtilityAction.SubseaCable`, Kachel „Seekabel“).
  Neue Fehler `NeedsRiver`, `NeedsSea`, `NoHydroSite` (hinten angefügt). Dateien: `UtilityCatalog` (Enums `FacilitySite`,
  `OutputModel`), `UtilitySystem.Energy.cs` (alles Neue), kleine markierte Stellen in `UtilitySystem.cs`/`.Save.cs`.
- **Nennleistung je Bauwerk** `UtilityFacility.Rated` (Katalogwert, Wasserkraft je Standort) × `Availability` (0..1) × Störfall →
  `Output`. `IsRequired` zählt nur Erzeuger (`IsGenerator`, nicht Umspannwerk/Speicher). Onshore-Windrad/Solar bleiben bewusst
  **konstant** (bestehende Balance/Tests) – „schwankende Erzeugung“ gilt nur für die neuen Typen.
- **Wasserkraft – Entscheidung Laufwasserkraftwerk mit Wehr statt Talsperre:** Der erzeugte Fluss hat kein Gefälle; eine Talsperre
  müsste ein Tal fluten und den Fluss unterbrechen. Das Wehr staut nur bis unter das niedrigere Ufer: **Fallhöhe** H = niedrigere
  Uferhöhe (höchster Punkt bis 60 m hinter der Uferlinie) − Wasserspiegel − 1 m Freibord, 2…15 m; **Abfluss** Q = Wasserbreite quer
  zum Wehr × mittlere Tiefe × 0,9 m/s × √(Jahresniederschlag/750) (0,5…1,5); P = 9,81 × min(Q, 150 m³/s Ausbau) × H × 0,88 kW
  (Testfluss 46 m, Q 152, H 5 m → 6,5 MW). Abfluss schwankt mit der Niederschlagslage (`RiverFlowIndex` = gleitendes Mittel des
  Klima-Niederschlags, Faktor 0,45…1,5; große Flüsse liegen meist über dem Ausbaudurchfluss). **Der Fluss bleibt durchgängig:**
  Gelände wird nicht eingeebnet, Wasser bleibt Wasser, Verschmutzung wandert weiter, Wasser-Gateways bleiben schiffbar (Schleuse/
  Fischtreppe gehören zum Bauwerk). Werkzeug richtet das Wehr aus (`TryAlignHydro`: schmalster Querschnitt in 15°-Schritten,
  Mitte des Flusses, Vorderseite flussabwärts; `,`/`.` drehen zusätzlich), Hinweis zeigt Breite/Abfluss/Fallhöhe/MW.
- **Meer vs. Fluss** (`IsSea`): Wasser + natürlicher Grund ≤ −5 m (Flussbett −4, Meer −7) + ≥ 75 % Wasser im Umkreis 150 m.
  Offshore: Mitte offenes Meer, jede Ecke ≥ 3 m tief. Wind `WindFactor(tick)` = zwei Lagen geglättetes Hash-Rauschen (6 h/1,5 h),
  0,05…1, Ø ≈ 0,46 über 30 Tage (alle Parks ein Wetter, deterministisch).
- **Biogas – Rohstoff als Bedarfs-Anbindung** (Hook `UtilitySystem.FarmSources` ← `Goods/FarmFeedstock.Sources`: Bauernhöfe =
  Firmen mit Rezept „farm“ + Beschäftigte): Anlagen nach Id teilen sich die Höfe im Umkreis 3 km (nächste zuerst, kein Hof doppelt),
  Volllast ab 40 Beschäftigten, sonst Grundlast 25 % (Bioabfall) + Rest anteilig. **Offen:** echtes Gut „Biomasse/Silage“ mit
  Lieferungen per Traktor/Lkw und Erlös für die Höfe (Mais/Gülle sind noch keine Güter).
- **Batteriespeicher im Netzmodell** (`OfferStorage`/`SettleStorage`, je Durchlauf `StepHours` 0,5 h, Wirkungsgrad 90 % je Richtung):
  Hochspannungs-Speicher bieten vor der Umspannung an (zählen für die Lieferquote), Ortsnetz-Speicher nach dem Bezug aus der
  Hochspannung → **Speicher liefern erst, wenn die Kraftwerke nicht reichen**, nur so viel wie fehlt. Laden aus dem Überschuss:
  zuerst im eigenen Netz, Ortsnetz-Speicher zusätzlich über freie Umspannkapazität aus dem Überschuss der Hochspannung (Last des
  Umspannwerks steigt). Speicher, die entladen, laden nicht. Netz: `StorageDischarge`/`StorageCharge`/`Storages`, Bauwerk
  `StoredKwh`, `Charging`, `StateOfCharge`. Keine Rückspeisung Ortsnetz → Hochspannung (wie bisher).
- **Dienstfahrzeuge (Grundstufe)** `Simulation/Services/ServiceVehicleSystem` (+ `.Save.cs`, Typen/Regeln `ServiceVehicles.cs`,
  `world.ServiceVehicles`, `ISubstepSystem` + Kalender-Tick, `IUpkeepSource` „Dienstfahrzeuge“ 150 €/Woche je Fahrzeug):
  **Standard aus** (`Enabled`; Tests/alte Welten bitgleich), `SimulationRunner` schaltet ein (`serviceVehicles`, auch nach dem Laden).
  Flotte je Dienstgebäude mit Zufahrt: Polizei 3 Streifenwagen, Feuerwache 2 Löschfahrzeuge, Krankenhaus 3/Klinik 1 Rettungswagen,
  Deponie 6/Verbrennung 5 Müllwagen. Echte Fahrten über `TrafficSystem.RequestTrip` mit **eigener Fahrt-Id-Spanne**
  `Traffic/TripIdRanges` (−1,5 … −1,9 Mrd., Lkw des Warensystems zählen ab −1 abwärts), Anteil ≤ 5 % von `MaxVehicles`,
  Statistik `TrafficStats.ServiceVehicles`. Ablauf: Depot → Halt (Zufahrt des Gebäudes) → steht dort (`Working`, die Darstellung
  zeichnet es geparkt: `ParkedUnits`) → nächster Halt bzw. zurück; Startversuch je Tick, nach 20 Fehlversuchen bzw. 1 Tag oder bei
  abgebrochener Fahrt Abbruch (gilt als zurück, Ladung entladen). Alles nach Id, Zufall `SimRandom`, Hash für Einsätze.
  - **Müllwagen:** stündlich Touren (bis 6 Halte, 10 t) zu den Gebäuden mit dem meisten Müll ≥ 50 kg im Einzugsgebiet der Deponie
    (`ServiceSystem.AssignedFacility`), laden bei Ankunft (`CollectGarbage`), entladen an der Deponie (`UnloadGarbage`). **Entscheidung
    „teilweise“:** solange Müllwagen fahren, holt die Pauschale nur noch 50 % der laufenden Menge **ohne Nachholen**
    (`ServiceSystem.GarbageBaseShare`, Standard 1 = bitgleich wie vorher), Rückstau wartet auf den Wagen (Test: Stichstraße für Lkw
    gesperrt → 4,7× so viel Müll). Nicht jede Tonne als Fahrt (große Städte bräuchten Tausende Halte am Tag).
  - **Streife:** Runden über 3 zufällige Gebäude im Einzugsgebiet der Wache, 20 s Halt, 1 h Pause. **Seit „Sicherheit“** (an):
    Halte erzeugen Streifenpräsenz (senkt die Kriminalität), Ziele gewichtet nach Kriminalitätsrate (`PatrolWeight`), 1 Wagen je Wache
    bleibt in Reserve (`PatrolReserve`), Notrufe unterbrechen eine am Halt stehende Streife.
  - **Feuerwehr: Platzhalter-Einsätze** nur noch bei ausgeschalteter Sicherheit (`PlaceholderFireCalls`): je Stunde und Gebäude Chance
    Brandrisiko × 1 %/24 (stabiler Hash), 5 min vor Ort, kein Schaden. Mit Sicherheit: echte Brände, Fahrzeug bleibt bis zum Ende
    am Brand (`HoldAtStop`).
  - **Rettungswagen: echtes Ereignis** – neues Event `CitizenSystem.BecameIll` (eine Zeile in `.Life.cs`): ab 65 immer, sonst 30 %
    schwer → Wagen der zuständigen Klinik/des Krankenhauses fährt zur Wohnung (3 min) und zurück; noch ohne Wirkung auf die Genesung.
  - **Taxi:** wartet auf Funktion (bräuchte Taxi-Depot + Verkehrsmittel „Taxi“ in der Wahl der Bürger – zu großer Umbau für diese Runde).
- **Fahrzeug-Modelle** (`Models/Vehicles`, Rolle `vehicle`, `AssetCatalog.Buses/Vans/ServiceVehicles`): **Linienbus** = eigenes
  Bus-Modell **statt** der Store-Busse, solange es da ist (Entscheidung: einheitliche Flotte eines Verkehrsbetriebs, Paletten-Stil wie
  die eigenen Gebäude, Store-Busse 9–12 m uneinheitlich; Store-Busse bleiben Rückfall). **Lieferwagen** (DeliveryVan): Lkw-Fahrten des
  Warensystems zu Einzelhandel mit ≤ 35 % einer Lkw-Ladung zeigt die `VehicleView` als Transporter (nur Darstellung – in der
  Simulation bleibt es ein 12-m-Lkw; offen: eigene Fahrzeugklasse/-länge und billigere Fracht). Dienstfahrzeuge nach Art
  (`ServiceVehicleRules.ModelName`), ohne Modell farbige Quader. Inspektor: Fahrzeug zeigt Art/Auftrag/Dienst/Ziel/Tour/Ladung.
- **Gelände-Modelle** (`Models/Terrain`): `Cliff_Straight` = Felsklippe (`rock`, `use: cliff`) in `NatureDetailView` auf einem
  16-m-Raster, wo der Hang ≥ 0,8 (≈ 40°) fällt (55 % der Zellen), Wand talwärts, Unterkante am Hangfuß, Höhe auf den Hang gestaucht
  (0,3…1,4), nicht auf Gebäuden/Bauwerken/am Wasser; `Rocks` = weiterer Felsen. **In `_Unused.txt` mit Begründung:** `Cliff_Corner`
  (Kachelraster-Ecke), `Cliff_Waterfall` (Fluss ohne Gefälle/Stufen), `Hill`, `Pond`, `River_*` (passt nicht zum prozeduralen
  Gelände: Hügel/Teiche/Flüsse kommen aus Höhenkarte bzw. Terraforming).
- **Darstellung:** `UtilityView` setzt beim Offshore-Park 4 Anlagen im Raster (`Units`, je Rotor dreht), Platzhalter für alle neuen
  Typen, Seekabel dunkelrot (unterirdisch-Ansicht), keine Betonplatte im Meer. Bauleiste/Tooltips/Werkzeug-Hinweise, Inspektor
  (Ladestand/Betrieb, Standort Abfluss/Fallhöhe, Wind, Rohstoff).
- **Spielstand:** `utilities` **v3** (Nennleistung, Ladestand, Laden, Standort, Verfügbarkeit, Biogas-Rohstoff je Bauwerk; Speicher je
  Netz; Abflusslage) – v2 → Katalogleistung, leere Speicher, Abfluss normal. Neuer Abschnitt `servicevehicles` v1 (Schalter, Fahrzeuge
  mit Auftrag/Halten/Fahrt-Id, Zufall, Zähler, `GarbageBaseShare`) – fehlt er, keine Fahrzeuge. `SaveLoadTests.BuildCity` hat Speicher +
  Dienstfahrzeuge.
- **Tests** `EnergyTests` (8: Wasserkraft nur im Fluss/Leistung aus Q und H/Fluss bleibt Wasser/Trockenzeit, niedrige Ufer, Offshore
  nur im Meer + Seekabel + Wind-Statistik, Biogas Zufahrt + Höfe teilen, Speicher Hochspannung laden/überbrücken/leer, Speicher
  Ortsnetz lädt übers Umspannwerk, Kasse, Spielstand bitgleich) und `ServiceVehicleTests` (5: aus = wie bisher, Müllwagen im Verkehr
  mit eigener Id-Spanne/Halde füllt sich, Rückstau hängt an der Ankunft, Streife/Brand/Rettung, Speichern mitten in der Tour bitgleich).
- **Optisch ungeprüft** (Unity nicht gestartet): Lage/Größe der Modelle (Wehr 66 m auf breiteren Flüssen, Höhe 0,4 m über dem Wasser),
  Offshore-Raster, Klippen an Hängen, geparkte Dienstfahrzeuge am Gehwegrand, Transporter statt Lkw, Bus-Dachplatte auf dem neuen
  Bus-Modell. Offen: Wehrbreite an die Flussbreite anpassen, Talsperre mit Stausee, Seekabel-Darstellung über dem Meeresgrund,
  Brände/Kriminalität als echte Ereignisse (dann Feuerwehr/Streife mit Wirkung), Müllwagen-Halte entlang der Route bündeln, Taxi.

## Sicherheit: Brände & Kriminalität (agent/safety, 2026-11-04)
- **Simulation** `Simulation/Safety` (`SafetySystem` + `.Fire.cs`/`.Crime.cs`/`.Save.cs`, Zahlen in `SafetyRules`, Typen `SafetyTypes`;
  `world.Safety`, jeden Tick nach den Dienstfahrzeugen). **Standard aus** (`Enabled`; Tests/alte Welten bitgleich, Platzhalter-
  Brandeinsätze bleiben), `SimulationRunner.safetyEvents` schaltet ein (auch nach dem Laden). Zufall nur über stabile Hashes
  (`SafetyRules.Roll(id, tick/stunde, salt)`), keine gemeinsame Zufallsfolge. `IgnitionScale`/`CrimeScale` (gespeichert) für Tests/Balancing.
- **Brandgefahr** `FireRiskOf(b)` → `FireRiskInfo` = Bauart (EFH 1, MFH 1,15, Block 1,3, Laden 1,1, Mischgebiet 1,2, Büro 0,8, Industrie 1,8)
  × Rezept (Raffinerie 2,2, Ölförderung 1,8, Sägewerk 1,6, Zeche 1,5, Möbel/Textil 1,4, Stahl/Glas 1,3, Mühle/Lebensmittel 1,2) × Zustand
  (1 + 1,2 × (1 − Condition)) × Müll (1 + 0,8 × Müllstand) × Stromausfall 1,25 × Größe (0,6 + 0,1 × Zellen) × Brandschutz (1 − 0,35 ×
  Feuerwehr-Abdeckung). Entstehung stündlich: Faktor × `IgnitionsPerDay` 0,00015 / 24 (≈ 5 % je Gebäude und Spieljahr bei Faktor 1).
- **Verlauf** (je Tick = 0,1 h): Stärke 0,1 → `dI = (1,5 × Brandlast × I + 0,03)(1 − I)` (Vollbrand ohne Feuerwehr ≈ 3 h, Totalschaden
  ≈ 4,6 h); Schaden 0,28 × I je Stunde, Zustand sinkt mit (min. 0,05). Notruf nach 1 Tick (Menschen im Haus) bzw. 3 Ticks. Entstehungsbrand
  (< 0,16) mit Anwesenden: 6 %/Tick selbst gelöscht. **Löschen erst bei Ankunft**: je Löschfahrzeug vor Ort −0,9 × Wasser je Stunde;
  Wasser = 1 mit Wassernetz am Gebäude (Hydrant, `GetStatus(b).HasWater`), sonst Tankwasser 0,35. Alarmierung je Stärke 1/2/3 Fahrzeuge
  (`ServiceVehicleSystem.Dispatch`: zuständige Wache, sonst schnellste andere im 1,5-fachen der Reichweite). Ohne Dienstfahrzeuge (Tests):
  geschätzte Ankunft aus der Dijkstra-Fahrzeit (`ServiceSystem.ResponseSeconds`). Antwortzeit in Verkehrszeit (`ReportTime`/
  `FirstArrivalTime`) → Spielminuten. **Langsamer Verkehr verzögert** (Test: Tempo 10 km/h auf allen Straßen → Ankunft 6 → 23 min,
  Schaden 1 % → 5 %). **Übergreifen** ab Stärke 0,45 auf Nachbarn ≤ 12 m (Kante zu Kante): 3 %/Tick × Stärke × Nähe × Brandlast,
  Feuerwehr vor Ort × 0,3. Wind: nicht modelliert.
- **Folgen:** Verletzte (je Anwesendem 0,3 % + 6 % × Stärke, beim Notruf und bei Stärke 0,5, max. 4; Gesundheit −0,25…−0,45, keine Toten)
  → Rettungswagen; Erinnerung `MemoryKind.Fire` (Bewohner/Anwesende stark, Nachbarn in 80 m schwach); Schlagzeilen `Fire` (ab Stärke 0,4)
  und `FireRuin`; Lager der Firma brennt anteilig mit; Bodenwert-Abzug (s. G6). **Schwer (Schaden ≥ 0,4):** Sanierung 1 + 6 × Schaden Tage,
  `Building.OutOfService` (Baufortschritt ruht, Construction 0), Haushalte ziehen **vorübergehend** in freie Wohnungen
  (`CitizenSystem.Vacate`, ohne freie Wohnung Wegzug) und **kehren danach zurück** (`ReturnHouseholds`), Beschäftigte verlieren den Job.
  **Totalschaden:** Zonengebäude → Abriss (Zone wird neu bebaut, wie Verfall). **Entscheidung gesetzte Gebäude: Brandruine statt still
  verschwinden** – der Spieler hat sie bezahlt und platziert; sie bleiben als Ruine stehen (Bewohner ausgezogen), **Wiederaufbau** im
  Inspektor für 60 % der Baukosten (Rest Versicherung, Kasse „Bau Gebäude (gesetzt)“, 3 Tage) oder Abriss wie gewohnt. So bleibt die
  Stadtgestalt erhalten und ein Brand kostet spürbar Geld, ohne eine Reihe still zu zerreißen.
- **Kriminalität** stündlich je Gebäude (`CrimeFactorsOf(b)` → `CrimeFactors`): Viertel = 3×3 Zellen à 96 m (Mitte voll, Rand ½).
  Druck = 0,06 + 0,30 Arbeitslosigkeit (Quote/25 %) + 0,22 Unzufriedenheit ((0,7 − Ø)/0,5) + 0,12 wenig Bildung (Anteil ohne/mit
  Grundschulabschluss 20 → 70 %) + 0,10 Dichte (Wohnungen + Jobs / 600) + 0,12 niedriger Bodenwert (1 − Wert/0,6) + 0,05 Laden;
  Rate = Druck × (1 − 0,6 × Polizeiabdeckung) × (1 − 0,35 × Streifenpräsenz) (`SafetyRules.CrimeRate`). **Streifenpräsenz** aus echten
  Streifen-Halten (+1 in der Zelle, Nachbarn +0,4, stündlich × 0,8, Präsenz = 1 − e^(−x/2)). Straftaten stündlich: Rate × 0,03 × Größe
  (0,4 + (Wohnungen + Jobs)/15, max. 3) × Tageszeit (0–5 Uhr 1,8 … tagsüber 0,7) / 24. Art nach Bauart (Wohnen: Einbruch/Vandalismus,
  Laden: Ladendiebstahl/Einbruch/Vandalismus, Büro/Industrie: Vandalismus/Einbruch); Schaden 80–4 000 € (Firmenkasse, Ladenlager,
  Vandalismus senkt den Zustand; Haushalte haben noch kein Vermögen → Schaden nur als Zahl), Erinnerung `Burglary` (Opfer-Haushalt) bzw.
  `FeelsUnsafe` (Mitbewohner/Beschäftigte). **Anzeige** (Einbruch 85 %, Vandalismus 60 %, Laden 50 %) → Streifenwagen
  (`ServiceMission.CrimeCall`, 120 s vor Ort) → **Aufklärung** bei Ankunft: Grundquote (Einbruch 25 %, Laden 45 %, Vandalismus 15 %) ×
  (0,5 + 0,5 × Abdeckung) + bis 25 % für schnelle Ankunft (0 ab 45 min); kein Wagen frei → spätere Ermittlung (halbe Quote).
  **Festnahme** → Zelle der Wache (20 je Wache, Platzhalter bis zum Gefängnis; voll → wieder frei; täglich 30 % Abgang). Schlagzeilen
  `CrimeWave` (≥ 6 Taten am Tag rund um eine Straße, auch HUD) und `Arrest`.
- **Bürger:** Hook `CitizenSystem.CrimeLevel` → Sicherheit −0,45 × Rate, −0,25 × (Einbruch + ½ Brand-Erinnerung); Umzug zählt `Burglary`
  + ½ `Fire` zu den Unsicherheits-Erinnerungen. Neue Erinnerungen `Fire`, `Burglary` (+ Gedankensätze in `Thoughts`).
- **Dienstfahrzeuge (kleine markierte Erweiterungen):** `Dispatch(mission, b, now)`, `UnitsAt`, Events `ArrivedAtStop`/`StopFinished`,
  Hooks `HoldAtStop`, `PatrolWeight`, `PatrolReserve`, `PlaceholderFireCalls`; neuer Auftrag `ServiceMission.CrimeCall`.
- **Darstellung** (`Game/Services/SafetyView`, nur lesend): flackernde Flammen-Quader auf dem Dach (Anzahl/Größe nach Stärke), Rauchsäule,
  verkohlte Mauerreste + Glut an Ruinen, grauer Rohbau mit Gerüst während der Sanierung (`BuildingView` zeichnet `OutOfService`-Gebäude
  nicht), Blaulicht (wechselnde blaue Leuchte) auf Löschfahrzeug/Streife/Rettung im Einsatz. Linke Ansichtsleiste **„Brandgefahr“**
  (Flamme) und **„Kriminalität“** (Schild) über `ServiceOverlayView` (`SafetyOverlays.FireRisk/Crime`, grün → rot, brennende Gebäude bzw.
  Tatorte der letzten 6 h groß rot, Wachen blau; ohne neue Tasten). Inspektor (`InspectorWindow.Safety.cs`): Brand (Stärke, Schaden,
  Löschfahrzeuge vor Ort/unterwegs, geschätzte Ankunft, Wasser, Verletzte), Ruine/Sanierung mit Knopf **Wiederaufbauen**, Sicherheit
  (Kriminalität, Brandgefahr, letzte 3 Vorfälle), Reiter Umfeld mit allen Faktoren; Stadtübersicht „Sicherheit“ (Brände, Anfahrt,
  Ruinen, Straftaten, Aufklärungsquote, Zellen, Schaden). HUD: `SafetyAlerts` → `HudMessages` (Feuer/übergegriffen/gelöscht/ausgebrannt/
  saniert, Einbruchserie; ein Schlüssel je Brand).
- **Spielstand** `safety` v1 (Schalter, Faktoren, Zähler, laufende Brände inkl. Verkehrszeiten, Verlauf, Sanierungen/Ruinen – setzt
  `Building.OutOfService`, das der Stadt-Abschnitt nicht kennt –, Straftaten, offene Einsätze, Raten der letzten Stunde, Streifenpräsenz,
  Zellen, HUD-Ereignisse). Fehlt → aus. `SaveLoadTests.BuildCity` hat Sicherheit an (Entstehung × 20, Feuerwache).
- **Tests** `SafetyTests` (15): aus = neutral, Brandgefahr-Faktoren, wächst ohne Feuerwehr → Ruine → Wiederaufbau kostet, Zonengebäude
  wird abgerissen, Übergreifen in der Reihe (nicht weit), echtes Löschfahrzeug löscht bei Ankunft + langsamer Verkehr verzögert, ohne
  Wasser mehr Schaden/länger, schwerer Schaden → vorübergehender Auszug + Rückkehr, Verletzte/Rettung/Schlagzeile, Kriminalität monoton
  in den Faktoren und sinkt mit Polizei + Streife (Ø 0,505 → 0,125), Einbruch → Erinnerung/Sicherheit sinkt/Streifenwagen am Tatort,
  Speichern mitten im Brand mit Ruine bitgleich, Migration ohne Abschnitt, **Balance-Info** (175 Gebäude, ≈ 2 750 Einwohner, Feuerwache +
  Polizei, 20 Tage, Entstehung × 30): 16 Brände, 14 gelöscht, 1 selbst gelöscht, 0 schwer, 0 Totalschaden, Ø Anfahrt ≈ 6 min,
  hochgerechnet ≈ 18 Brände je Spieljahr; Kriminalität Ø 0,19, ≈ 1,3 Taten/Tag, Aufklärung ≈ 24 %; ≈ 2,3 ms je Tick (alle Systeme).
- **Optisch ungeprüft** (Unity nicht gestartet): Flammen/Rauch/Ruinen/Gerüst/Blaulicht (Größen, Emission im URP-Lit), Icons Flamme/Schild,
  Inspektor-Abschnitte, Ansichten.
- **Offen:** Gefängnis (Zellen sind Platzhalter), echter Stau-Test (heute Tempo-Begrenzung als Ersatz), Wind/Wetter beim Übergreifen,
  Straßenbeleuchtung als Kriminalitätsfaktor, Haushaltsvermögen (Einbruchsschaden trifft heute nur Firmen), Brände an Dienst-/
  Versorgungsbauwerken, Firma während der Sanierung ruhen lassen (läuft heute ohne Beschäftigte weiter, ggf. Pleite), Brandschutz-
  Verordnung/Rauchmelder-Förderung, Budgetregler Polizei/Feuerwehr, Martinshorn-Vorfahrt im Verkehr.

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
- **Sicherheit** (Hook `SafetyPenaltyAt`, Faktor „Brände/Kriminalität“ −0,15 × Wert, ohne Sicherheit 0 = bitgleich): frische Brände in
  120 m (klingen in ≈ 6 Tagen ab) + Kriminalitätsrate des Gebäudes auf der Zelle.
- Später: Stufenaufstieg auch nach Wohlstand/Einkommen (G5), ~~Brände/Kriminalität senken den Wert~~ ✅, Bodenwert als
  Preis beim Kauf von Grundstücken, Denkmalschutz/Stilrichtungen, Abriss-Meldung im HUD.

## Wohnhäuser setzen (Anno-Stil)
- **Entscheidung des Spielers** (s. „Entscheidung: Bauart“): Wohnhäuser einzeln hinstellen, sie bevölkern sich und steigen auf,
  wenn Einrichtungen in Reichweite sind. **Zonen bleiben zusätzlich**, freie Straßen bleiben.
- **Herkunft** `Building.Origin` (`BuildingOrigin.Zone` | `Player`, `IsPlayerPlaced`). **Bauarten** `Zoning/HouseCatalog`:
  Einfamilienhaus (Wohnen gering, 2×2 Zellen = 16×16 m, 4 000 €), Mehrfamilienhaus (mittel, 3×3, 16 000 €), Wohnblock (dicht,
  4×4, 45 000 €); Wohnungen wie gewachsene Gebäude gleicher Größe (`HouseholdsAtLevel`, Kapazität × 1,0/1,4/1,8 je Stufe wie G6).
  ~~Je Zonenart genau eine Bauart~~ → seit „Setzen statt Zonen“: Bauart = (Herkunft, Zone, Größe) (`HouseCatalog.Of`), im Spielstand
  steht weiter nur die Herkunft. ~~Bewusst ohne Laden/Mischgebiet~~ → abgelöst durch „Setzen statt Zonen“ (unten): der Spieler
  setzt die Hülle, den Betrieb wählt weiter die Simulation.
- **Simulation** `CitySystem.Houses.cs`: `PlanHouse(type, cursor, drehung, movingId)` richtet die Vorderseite über
  `RoadFront.TryAlign` zur Straße aus (Cursor ≤ Tiefe + 16 m vom Rand), `PlanHouseAt(type, mitte, front)` genau →
  `HousePlan` (Fehler `HouseBuildError`: OutOfWorld, OnWater, TooSteep > 4 m, OverlapsRoad, OverlapsStructure mit
  `BlockerName` – Dienste/Versorger/Depots exakt über `roads.Structures`, Strommasten über `LotBlocker`-Prüfpunkte –,
  **OverlapsBuilding** (Zonengebäude darunter: lieber ungültig mit Hinweis als still abreißen), NoRoadAccess über
  `RoadFront.FindAccess` ≤ 8 m, InsufficientFunds) → `BuildHouse(plan)` (Baufortschritt wie Zonen, Bodenplatte auf Höhe der
  Zufahrt, Gelände eingeebnet). Kasse: `CitySystem.Budget`, Kategorie **`BudgetCategory.HouseConstruction`** („Bau Wohnhäuser“,
  hinten angefügt). `DemolishHouse(id)` = 25 % zurück. **Verschieben** `PlanMoveHouse(id, cursor, drehung)`/`MoveHouse(plan)`
  (10 %, Id/Stufe/Bewohner bleiben, Event `BuildingMoved` + `BuildingChanged`).
- **Schutz:** gesetzte Häuser werden **nicht** von Zonenlogik abgerissen (Umwidmung/Entzonen in `Paint`, Straße durchs Grundstück
  in `OnSegmentAdded`, fehlende Straße in `ValidateBuildings` – dort bleibt das Haus stehen, Zufahrt = −1, `HasRoadAccess`),
  nicht verlassen bei Ausfall (`UtilitySystem.Abandon` überspringt sie – sie steigen stattdessen ab) und nicht wegen Verfall
  abgerissen. **Bauwerks-Sperre** wie Dienste: `roads.RegisterStructures` Art „house“ (Name = Bauart). Dienste, Versorger und
  Depots über einem gesetzten Haus → Plan ungültig (`OverlapsFacility`, kleine markierte Zeile in den drei Systemen).
  Zonen wachsen nicht auf/an gesetzten Häusern (`LotFits` prüft auch schräg stehende).
- **Frei setzen (2026-11-04, Spieler):** gesetzte Gebäude dürfen **ohne Straße** stehen (erst Gebäude, dann Straße).
  `CheckHouse` liefert dann `None` mit `AccessSegmentId = −1` (`HousePlan.HasRoadAccess` = false; `NoRoadAccess` wird nicht mehr
  vergeben). Ohne Zufahrt ruht der Bau und der Zustand sinkt in `CitySystem.HouseNoRoadDays` (3) Spieltagen von 1 auf 0
  (`DecayWithoutRoad` in `UpdateBuildings`), dann Abriss ohne Erstattung + Event `HouseDecayedWithoutRoad` (HUD-Meldung in
  `LandValueAlerts`). Bekommt eine Baustelle ihre Straße (`RevalidateHouseAccess`), startet sie wieder neuwertig. Gilt auch für
  fertige Häuser, deren Straße abgerissen wird. Inspektor: „Ohne Straße – verfällt in …“ (`DaysUntilNoRoadDecay`). Kein neues
  Spielstandfeld (läuft über `Condition`). Dienste/Versorger/Depots verlangen weiter eine Zufahrt.
- **Bevölkern:** kein Sonderweg – gesetzte Häuser sind normale Wohnungen (Zuzug, Umzug, `HomeScore`, Wohnkomfort je Zone).
- **Aufstieg nach Bedürfnissen** (`Zoning/HouseRules`, datengetrieben; Auswertung `LandValue/LandValueSystem.Houses.cs`, täglich
  in `EvaluateBuildings` statt der G6-Regeln): je Stufe eine `HouseLevelRule` (neue harte `HouseNeed`s, weiche Zufriedenheit/
  Belegung, Tage); Anforderungen gelten kumuliert. **Stufe 2 „gehoben“** (3 Tage): Strom, Wasser (`Utilities.IsSupplied`), Laden
  mit Ware in Fußweite (Einzelhandel/Mischgebiet ≤ `ShopWalkRadius` 500 m Luftlinie, `Goods.ShopHasStock`), Grundschule;
  Zufriedenheit ≥ 0,55, Belegung ≥ 75 %. **Stufe 3 „wohlhabend“** (5 Tage): + Polizei, Gesundheit, Abwasser, Park, Oberschule
  oder Bibliothek; Zufriedenheit ≥ 0,62. Dienste über `world.Services.Coverage(kind, b)` ≥ `ServiceThreshold` 0,25 (bei
  mehreren Arten zählt die beste). Fortschritt: alles erfüllt +1, Hartes fehlt −2, nur Weiches fehlt → Pause. Fehlt eine
  Anforderung der **eigenen** Stufe: schlechte Tage (+1/−2), nach `DeclineDays` 6 → Abstieg. Mehr Stufen = Regel an
  `HouseRules.Levels` anhängen (Kapazitätsfaktor ist bei G6 noch auf 3 Stufen begrenzt). Zonengebäude: G6 unverändert.
- **API:** `LevelInfo(b)` für gesetzte Häuser: `PlayerHouse`, `Needs` (✓/✗ + `Detail`, z. B. „Abdeckung 45 % (nötig 25 %)“,
  „Laden in 320 m“), `MissingText` „Es fehlt: Grundschule in Reichweite, …“ (inkl. weicher Punkte), `Missing` mit
  `LevelGap.Needs`, `OwnNeedsMissing`, Fortschritt/Abstiegstage. `EvaluateNeed(s)`, `PreviewHouse(plan)` → `HousePreview`
  (alle Anforderungen am Bauplatz – Versorgung = Leitung in Anschlussweite, Dienste über `CoverageAt` –, `ReachableLevel`).
  Test-Hook `HouseNeedOverride`.
- **Werkzeug** (`Game/Zoning/HouseTool`, Modus `RoadTool.Mode.House`, **Taste 6** – alle Buchstaben belegt, 6 war frei und passt
  zu 4 Dienste/5 ÖPNV): Bauleisten-Kategorie „Wohnhäuser“ (`GameHud.Houses.cs`: 3 Bauarten, Drehen, Abriss-Kachel, auch unter
  Abriss), Vorschau mit dem **Stufe-1-Modell der Zone** (`AssetCatalog` über `BuildingView.PickForLevel`, wie `BuildingView`
  skaliert, über `PlacementGhost.ShowModel`; ohne Katalog Quader) grün/rot + Vorderseiten-Pfeil, `,`/`.` drehen, Kosten/Fehler/
  „Es fehlt“ in der Hinweiszeile (`GameHud.UpdateHint` → `HouseTool.HintText`), Welt-Beschriftung mit ✓/✗ je Bedürfnis und
  erreichbarer Stufe. **Haus-Fenster** (`UI/Windows/HouseWindow`, rechts): je Stufe die Anforderungen am Mauszeiger mit ✓/✗ +
  Detail; Klick auf eine Einrichtung zeigt deren **Reichweite** (Abdeckungs-Ansicht `ServiceOverlayView.ToolKind`; Laden: Ring
  500 m), ohne Auswahl die erste fehlende. Esc/Rechtsklick → Auswählen. Inspektor: Herkunft, Stufe + Name, Anforderungen ✓/✗,
  weiche Bedingungen, Fortschritt, Abstieg droht; Knöpfe Verschieben (Y, `FacilityMover` kann jetzt auch gesetzte Häuser) und
  Abreißen. IMGUI-Rückfallleiste ohne Eintrag. Im Editor noch nicht visuell abgenommen.
- **Speichern:** Abschnitt `city` v2 (Herkunft je Gebäude); v1 → alle Gebäude „aus Zone“ (`HouseTests.Migration_CityV1_…`).
- Später: Häuser-Meldungen im HUD (Aufstieg/Abstieg gesammelt wie `LandValueAlerts`), Bedarfsstufen der Haushalte (G5) an die
  Hausstufe koppeln, Marktplatz als eigener Dienst, Reichweiten aller Einrichtungen gleichzeitig, eigenes Aussehen gesetzter Häuser.

### Setzen statt Zonen (Entscheidung des Spielers, umgesetzt)
- **Was der Spieler tut:** Taste 6 / Kategorie **„Bauen“** mit Reitern **Wohnen** (Einfamilienhaus, Mehrfamilienhaus, Wohnblock),
  **Gewerbe** (Laden 2×2 6 000 €, Wohn- und Geschäftshaus 3×3 20 000 €, Bürohaus 3×3 28 000 €), **Industrie** (Gewerbehalle 3×3
  15 000 €, Industriehalle 4×4 30 000 €). Klick = ein Gebäude (wie bisher, drehbar). **LMB halten und entlang der Straße ziehen**
  = Reihe gleichartiger Gebäude: Vorschau mit echten Modellen (`PlacementGhost` je Platz, gültig grün, ungültig rot),
  Hinweiszeile „Reihe: 7 × Einfamilienhaus · Kosten 28 000 € · 1 ausgelassen (rot: Polizeiwache im Weg)“, Loslassen baut alle
  gültigen, RMB/Esc bricht nur die Reihe ab (`RoadTool.RegisterOperation`). Welcher Betrieb in Laden/Büro/Halle einzieht,
  entscheidet die Simulation (G5-Standortwahl inkl. Schwerpunkt/Förderung). Keine neue Taste (6 bleibt, Z = Lenken).
- **Modell** `HouseCatalog` (8 Bauarten, `PlaceableGroup` = Reiter, `Description`, `JobsAtLevel`, `HasFirm`, `HasHomes`):
  Bauart = Zone + Größe (`ForZone(zone, w, d)`), Kapazitäten wie gewachsene Gebäude gleicher Größe (`LandValueRules.HouseholdsFor/
  JobsFor`), `BuildHouse` setzt jetzt auch `Jobs`. Firmen entstehen wie bei Zonengebäuden über `GoodsSystem.OnBuildingAdded`
  (Laden/Mischgebiet → Einzelhandel, Büro → Büro, Halle → `ChooseIndustry` mit `SiteModifier` = Schwerpunkt/Förderung).
  **Pleite in einer gesetzten Hülle** → kein Abriss, `GoodsSystem.NewTenant`: neuer Betreiber (Industrie: neue Standortwahl ohne
  den alten Betrieb; Läden/Büros gleiche Rolle), Lager leer, Startkapital (`Bankruptcies` zählt weiter).
- **Reihe** `Zoning/CitySystem.Rows.cs`: `PlanHouseRow(type, start, end, max = 40)` → `HouseRowPlan` (`Items` gültig/ungültig,
  `TotalCost` nur gültige, `ValidCount`, `SegmentIds`, `Length`). Nächste ebenerdige Stadtstraße am Start (wie `TryAlign`), Seite =
  Seite des Startpunkts, Richtung = Zugrichtung entlang der Tangente; `TraceRowPath` tastet die Vorderkanten-Linie (Gehweg + 0,5 m)
  im 1-m-Schritt ab und läuft über Knoten mit **genau zwei Segmenten gleichen Typs** weiter (Knick/Bogen), stoppt an Kreuzungen,
  Typwechseln, Brücken/Tunneln. Abstand: kleinste Bogenlänge ohne Überlappung mit dem Vorgänger (Bisektion, Rechtecke +
  `RowGap` 0,05 m) → gerade genau Breite + 0,05 m, Kurve außen stoßen die Vorderecken, innen die hinteren Ecken an; auf der
  Innenseite enger Kurven rückt ein Gebäude in 0,5-m-Schritten ab, bis die Vorderecken den Gehweg nicht berühren. Plätze bis zur
  Maus (Mitte ≤ Mausprojektion + 0,1 Breite). Jeder Platz läuft durch `PlanHouseAt` (alle Platzregeln), Geld kumuliert
  (`InsufficientFunds` ab dem ersten, der nicht mehr reicht). `BuildHouseRow(row)` prüft jeden Platz erneut, bucht die Summe
  **genau einmal** (Kategorie `HouseConstruction`, Anzeige jetzt „Bau Gebäude (gesetzt)“) und legt die Gebäude an
  (`PlaceBuilding`); reicht das Geld nicht für alle, wird nichts gebaut. Rein, deterministisch, ohne Zufall.
- **Aufstieg je Bauart** (`HouseRules.LevelsFor(zone)`, datengetrieben wie die Wohnhäuser; Overloads mit `ZoneType` für
  `RuleFor/NeedsFor/AllNeeds/MaxLevelFor/LevelName/ReachableLevel`, die alten Overloads = Wohnen): Wohnen und Mischgebiet wie
  bisher. **Laden** Stufe 2 „belebt“ Strom, Wasser, **Kundschaft** 150 Einwohner in 600 m (`HouseNeedKind.Customers`,
  `CustomerRadius`, Luftlinie Zufahrt ↔ Zufahrt) · Stufe 3 „gefragt“ Abwasser, Polizei, 500 Einwohner. **Büro** Stufe 2 „modern“
  Strom, Wasser, **Fachkräfte** 30 % der Beschäftigten mit Studium (`Skilled`, aus `BuildingFacts.Skill`; Vorschau am Bauplatz =
  Anteil der Erwachsenen der Stadt) · Stufe 3 „repräsentativ“ Abwasser, Polizei, Bibliothek/Oberschule, 55 %. **Halle** Stufe 2
  „ausgebaut“ Strom, Wasser, **Güteranschluss** (`Freight`: `LandValue.HighwayAccessAt` ≥ 0,2 ≈ Autobahn ≤ 510 m) · Stufe 3
  „Großbetrieb“ Abwasser, Feuerwehr, 40 % Facharbeiter (mittlerer Abschluss). Weich: Zufriedenheit/Besetzung der Beschäftigten.
  Vorschau/Inspektor/Haus-Fenster zeigen die Regeln der Bauart (Reichweiten-Ring auch für Kundschaft).
- **Nachfrage-Anzeige** `Zoning/DemandReport` (nur lesend): `ValueOf(city, kind)` (Wohnen = höchste der drei Dichten, Laden =
  Einzelhandel, Büro, Industrie aus `CitySystem.Demand`, das die G5-Zuschläge schon enthält), `Explain(world, kind)` → Gründe
  (Zuzugsdruck, freie Jobs, Einpendler, Arbeitslose, Auspendler, Leerstand, Zufriedenheit · Ladenjobs je Einwohner, Einkäufer
  ohne Laden, Grundbedarf gestern gedeckt, Marge · arbeitslose Fachkräfte · Importanteil gestern, Steuerfaktor). Neue lesende
  Helfer in `GoodsSystem`: `ImportShareYesterday`, `BasicShoppingMetYesterday`, `MarginDemand`.
- **Spiel:** `HouseTool` (Reihe ziehen, Geister-Pool, Hinweis), `GameHud.Houses.cs` (Reiter, Kacheln mit Nachfrage-Balken ihrer
  Art, Unterzeile Wohnungen/Jobs), `GameHud.Demand.cs` (obere Leiste: „Nachfrage“ W L B I als senkrechte Balken, Tooltip =
  `Explain`, Klick öffnet Bauen mit passendem Reiter; USS-Block `.cb-demand…`), `GameHud.Focus.cs` (eigene Kategorie „Lenken“),
  `HouseWindow` (Regeln der Bauart, Hinweis „Betrieb wählt die Simulation“), `BuildingView` (Grundstücksplatte nur noch an
  Zonengebäuden), `RoadGuidesView` (keine Zonenflächen mehr, s. Bauhilfe Parallelabstand). Wahl einer Bauart stellt die
  Abstandshilfe auf ihre Tiefe.
- **Speichern:** keine neue Abschnittsversion nötig – Bauart ergibt sich aus Zone + Größe, gesetzte Wohnhäuser alter Stände
  (2×2/3×3/4×4) passen unverändert; Pleite-Nachfolger nutzt bestehende Firmendaten. Determinismus-Test (`SaveLoadTests.BuildCity`)
  setzt jetzt eine Laden- und eine Hallenreihe. Tests `PlacingTests` (13): Reihe gerade/links/Einzelklick/ohne Straße, Kurve +
  Knick beidseits ohne Überlappung, ungültige ausgelassen + eine Buchung, Kasse kumuliert, Jobs/Firmen in Laden/Mischgebiet/Büro/
  Hallen, Standortwahl in Hallen mit Schwerpunkt Öl/Gas (10 Hallen: 1 → 4 Öl/Gas-Betriebe), Pleite → neuer Betreiber, Aufstieg
  Laden nach Kundschaft, Güteranschluss/Fachkräfte, kein Wachstum ohne Zonen, alter Stand mit Zonen wächst nach dem Laden
  bitgleich weiter, Rundreise gesetzter Gewerbe-Reihen, Nachfrage-Erklärung.
- **Optik ungetestet** (Unity nicht gestartet): Reihen-Geister (viele `PlacementGhost`), Reiter/Kacheln, senkrechte Nachfrage-Balken
  und mehrzeilige Tooltips, Gebäude ohne Platte auf eingeebnetem Gelände, Katalogmodelle für Laden/Büro/Halle.
- Offen: Reihe über Kreuzungen hinweg fortsetzen, Ecken/Blockinnenhöfe füllen, eigenes Aussehen gesetzter Läden/Hallen,
  Schwerpunkt auch für Gewerbe/Büro, Nachfrage-Verlauf, Abriss ganzer Reihen per Ziehen.

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
  Grund × (1 ± 10 % geglättetes Hash-Rauschen, Periode 15 Tage); mit Weltkarte stattdessen der Weltpreis der Länder (Etappe 8).
- **Handel:** Abnehmer bestellen bei Eingang + unterwegs < 50 % (bis 3 Ladungen/Stunde): günstigster Anbieter im selben
  Netzteil (lokaler Preis + Lkw-Kosten/Menge) gegen Import (Welt × 1,08 Fracht + Lkw ab Außenanschluss – mit Gateways der
  Anschluss Richtung Partnerland, s. „Außenanbindungen (Gateways)“). Ohne jeden Außenanschluss: **Umland** (Fracht 15 %,
  Platzhalter, 1,5 km) – nur noch in Tests; im Spiel entfällt es (`WorldSettings.RequireGatewayForTrade`). Export
  ab 50 % Ausgangslager, wenn Welt − Fracht ≥ 90 % des lokalen Preises, ab 90 % Füllung immer (bis 30 % herunter). Firmen
  dürfen bis 10 Tage Lohn überziehen, darüber kaufen sie nichts.
- **Lieferverkehr:** eine Lieferung = eine Ladung. Anteil 0,8 × `TrafficSystem.Demand` als echter Lkw (seit dem Zeitmodell; vorher 0,35) (`VehicleClass.Truck`,
  **negative Fahrt-Ids** → keine Kollision mit Bürgerfahrten), höchstens 15 % von `MaxVehicles`; Abfahrten über die Stunde
  verteilt, bei fehlendem Platz an der Einfahrt bis 2 h (20 Versuche, 15 min Verkehrszeit) erneut versucht, sonst Platzhalter
  (`GoodsRules.PlaceholderTicks`: 1,3 × Luftlinie ÷ 9 m/s Verkehrszeit, +3 Ticks außerhalb).
  Ankunft über `TripEnded` bucht ins Ziellager (auch bei Abbruch; Sicherheitsnetz 3 Tage). Transport = 12 € + 0,12 €/Verkehrs-
  sekunde (geschätzt beim Abschicken, beim Lkw mit echter Fahrzeit nachberechnet → **Stau kostet Geld**). `VehicleView` zeigt
  Lkw mit den Katalogmodellen `AssetCatalog.Trucks`. Früher (Zeitverzerrung, 1 Stunde = 2,5 s Verkehr) scheiterten viele
  Lkw-Starts am Platz; seit dem Zeitmodell (1 Stunde = 7,5 min Verkehr) ~4 % (Statistik `TruckFailedYesterday`).
- **Haushalte** (Hook `CitizenSystem.Purchase`, markiert in `Arrive`): Einkauf deckt 3 Tage × (0,5 + 0,5 × Personen);
  Grundbedarf (Lebensmittel) immer, Komfort (Kleidung, Möbel) ab 110 €/Tag Einkommen + Zufriedenheit 0,45, Luxus
  (Elektronik, Fahrzeuge) ab 200 € + 0,55; Budget Grundbedarf 90 %, Rest 50 % des Einkommens; Ladenpreis = lokal × 1,45.
  Erfüllung 0..1 (Gewichte 0,6/0,25/0,15, nur Leistbares zählt) → `ShoppingNeed = min(0,99; Bedarf) × (1 − Erfüllung)`:
  leerer Laden lässt das Bedürfnis „Einkauf“ niedrig, neuer Versuch am nächsten Tag, Erinnerung „kein Laden“.
  `CitizenSystem.ShopHasStock`: Bürger meiden Läden ohne Grundbedarf.
- **Nachfrage der Zonen** (Hook `CitySystem.BusinessDemand`, additiv vor dem Steuerfaktor): ±25 × tanh(Ø Marge/0,15) der
  etablierten Firmen (ab 3 Tagen) je Zonenart; Industrie + bis 12 bei hohem Importanteil, Einzelhandel + bis 20 bei
  unerfüllten Einkäufen; Mischgebiet = halber Handel. **Verfall:** 21 Tage geglätteter Verlust und Konto unter dem Kreditrahmen
  → Pleite, Gebäude wird verlassen (`AbandonBankruptBuildings`); **vom Spieler gesetzte Hüllen** bleiben stehen, ein neuer Betreiber
  zieht ein (`NewTenant`, s. „Setzen statt Zonen“). Firmen entstehen in Zonen- und gesetzten Gebäuden gleich (`OnBuildingAdded`).
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
- **Leistung** (Testprojekt, Info): ~190 Firmen, ~15 000 Bürger ≈ 4 ms/Tick für alle Systeme (vor dem Zeitmodell; seitdem
  ≈ 10 ms je Kalender-Tick inkl. 180 Verkehrsschritten); Warensystem allein ≈ 2 ms je Spieltag.
- Später: Baustoffe für den Bau, Brennstoffe
  für Kraftwerke, Lagerhallen/Güterbahnhof/Hafen, Verträge statt Spotmarkt, Zölle/Subventionen, Löhne nach Gewinn, Ladenwahl
  nach Preis/Sortiment, Abfahrt-Puffer für Lkw. (~~Spieler-Spezialisierung über `Restructure`~~ ✅ s. „Produktion lenken“.)

## Produktion lenken (Teil von G5, Entscheidung Bauart)
- **Modell:** Die Simulation wählt die Industrie weiter selbst (Standortwahl), der Spieler lenkt mit zwei Hebeln. Kein Bau
  einzelner Betriebe, kein Verbot. Dateien: `Simulation/Goods/IndustryFocus.cs` (Enum `IndustryFocus`, `FocusFit`, Datentabelle
  `IndustryBranches`), `ProductionPolicy.cs` (+ `.Save.cs`, `PolicyRules` = alle Zahlen), `world.Policy` (täglich nach dem
  Warensystem, vor der Stadtkasse).
- **Branchen/Schwerpunkte** (= auch Förder-Branchen): automatisch (Standard), Landwirtschaft & Viehzucht (Bauernhof, Mühle),
  Forst (Forstbetrieb, Sägewerk), Erz, Kohle, Öl/Gas, Baustoffe (Kiesgrube, Zementwerk, Baustoffwerk), Verarbeitung (Stahl,
  Raffinerie/Kunststoff, Glas), Konsumgüter (Lebensmittel, Kleidung, Möbel), Technik (Elektronik, Fahrzeuge). Jedes der 18
  Industrie-Rezepte hat genau eine Heimat-Branche (`HomeOf`); je Schwerpunkt **Kern** (`Primary`), **verwandt** (`Related`,
  z. B. Stahlwerk im Erz-/Kohle-Gebiet, Raffinerie bei Öl/Gas – bleibt, kein Bonus) und **fremd**. **Entscheidung Viehzucht:**
  kein eigener Schwerpunkt, weil der Rezeptkatalog nur den Bauernhof (Getreide **und** Vieh) kennt – getrennt hätte er
  dieselbe Wirkung; kommt ein Weide-/Viehbetrieb, wird er in der Tabelle ergänzt.
- **Schwerpunkt-Gebiete:** eigenes Raster im Umwelt-Zellmaß (16 m, `Paint(mitte, radius, focus)`, `FocusAt`, `Hectares`),
  unabhängig von Straßen und Zonen → überlebt Umbau/Entzonen. Standortwahl: Hook `GoodsSystem.SiteModifier` →
  `SiteAdjust`: Kern **+1,2**, fremd **−0,6** auf den Score (typisch 0,3–2,5); sehr dringender Bedarf kann einen fremden Betrieb
  also trotzdem gewinnen lassen. **Vorkommen unter der Mindestgüte bleibt −∞** (keine Ölförderung ohne Öl). Bestehende fremde
  Betriebe im Gebiet stellen **träge** um (`Steer`, täglich, Firmen in Id-Reihenfolge, `SimRandom`): frühestens 14 Tage nach
  Gründung bzw. letztem Wechsel, Tageschance 6 % (rentabel, Schwerpunkt neu) bzw. 25 % (geglätteter Verlust), höchstens 3
  Wechsel je Tag; Ziel = beste Standortwahl ohne den alten Betrieb (`GoodsSystem.SuggestIndustry`) und nur, wenn er Kern oder
  verwandt ist (`GoodsSystem.Restructure`: neuer Betreiber, Lager leer, Startkapital). Sonst bleibt er (`BlockedYesterday`).
  Automatik-Gebiete werden nie umgestellt.
- **Förderung je Branche** (Stufe 0–3 keine/gering/mittel/stark, `SetSubsidy`/`ChangeSubsidy`): **5/12/25 € je Beschäftigtem und
  Tag** an jede Industriefirma der Branche (überall, nicht nur im Gebiet; Hook `GoodsSystem.SubsidyFor` → `FirmLedger.Subsidy`,
  zählt zum Gewinn → weniger Pleiten, mehr Nachfrage, ~25 % kommen über die Gewerbesteuer zurück) + Standort-Zuschlag
  0,15/0,3/0,5 → mehr Betriebe dieser Art. Die Stadtkasse bucht die gezahlte Summe des Tageswechsels (`GoodsStats.SubsidiesYesterday`,
  Hook `EconomySystem.DailySubsidies`) als neue Ausgaben-Kategorie **„Wirtschaftsförderung“** (`BudgetCategory.IndustrySubsidies`,
  hinten angefügt). Bei Baustopp (`CanPay`: Kasse zu lange überzogen) ruht die Förderung samt Zuschlag.
- **Rückmeldung:** `DepositHint(focus, p)` („Erdöl-Vorkommen hier: gut (62 %)“ / „schwach“ / „keins (5 %, mind. 15 %) – hier kann
  kein Betrieb „Ölförderung“ entstehen“; ab 35 % „gut“), `RateDeposit`, `Report(branch)` (Betriebe, Beschäftigte, Förderung/Tag,
  Produktion/Import/Export gestern der Branchen-Güter, Fläche), `Recommendations()` (bei Bedarf, je Tag/Stand zwischengespeichert,
  kein Spielzustand): ungenutzte ergiebige Vorkommen mit Himmelsrichtung („Hohes Ölvorkommen im Osten ungenutzt (~12 ha)“; ≥ 8
  Zellen ohne passenden Betrieb in 200 m, Boden erst ab 0,75, Wald ab 0,6), Importanteil ≥ 60 % bei Rohstoffen/Zwischenprodukten
  („Stahl wird zu 80 % importiert – Schwerpunkt oder Förderung „Verarbeitung““), Förderung ohne Betrieb, Schwerpunkt ohne Vorkommen.
- **Spiel:** seit „Setzen statt Zonen“ eigene Bauleisten-Kategorie **„Lenken“** (Taste Z öffnet den zuletzt gewählten Schwerpunkt,
  `RoadTool.OpenFocusBrush`; technisch weiter `Mode.Zone` mit `SelectedFocus`, ohne Zonenraster). Früher: Zonen-Werkzeug (Taste Z) mit „Schwerpunkt Industrie“ unter den Zonenkacheln. Kachel je
  Schwerpunkt (Farbe, Fläche/Förderstufe) + „Automatisch“ (entfernen), Pinsel-Vorgaben 48/96/160 m (Schwerpunkt-Pinsel bis 160 m,
  Zonen-Pinsel weiter max. 60 m). `RoadTool.SelectedFocus`/`SelectFocus` (null = Zonen-Pinsel; `SelectZone` setzt zurück);
  LMB malt, RMB setzt automatisch, Esc zurück zum Auswählen (wie Zonen). Hinweiszeile + Beschriftung am Pinsel = `DepositHint`.
  `Game/Goods/IndustryFocusView` (Begleiter): Gebiete als farbige 16-m-Flächen im Zonen-Werkzeug (kräftig beim Schwerpunkt-Pinsel),
  schaltet beim Wählen eines Rohstoff-Schwerpunkts die passende Rohstoff-Ansicht (F3-Raster) ein und beim Verlassen wieder aus.
  Fenster Wirtschaft (F5) Reiter **„Lenkung“** (`GoodsWindow.Policy.cs`): Förderung je Woche/gestern, Betriebswechsel, Empfehlungen,
  je Branche −/+ Förderstufe, Betriebe/Jobs, €/Woche, Produktion/Import/Export gestern, Fläche („malen“ öffnet den Pinsel).
  Inspektor-Reiter „Firma“ (`InspectorWindow.Policy.cs`): Schwerpunkt des Standorts + Passung („fremd – stellt mit der Zeit um“),
  Vorkommen dafür, Branche + Förderstufe + €/Tag; Gewinnrechnung mit Zeile „Förderung“. Hilfe (F1) ergänzt.
  **Optik ungetestet** (Unity nicht gestartet): Farben/Deckkraft der Gebietsflächen über der Rohstoff-Ansicht, Kachel-Unterzeilen,
  Breite des Stepper-Zusatztexts.
- **Spielstand:** Abschnitt „policy“ v1 (Raster mit Namenstabelle der Enum-Werte, Förderstufen über den Namen, letzter Wechsel je
  Gebäude, Zufall, Zähler); fehlt er (alte Stände) → überall automatisch, keine Förderung. Waren **v3**: `FirmLedger.Subsidy` und
  `SubsidiesYesterday` (v2 → 0). Determinismus-Test (`SaveLoadTests.BuildCity`) malt Schwerpunkt Verarbeitung + Förderung.
- **Tests** (`ProductionPolicyTests`, 10): Score-Wirkung Kern/verwandt/fremd/Förderung/Baustopp exakt; Standortwahl mit/ohne
  Schwerpunkt deterministisch (Labor, Öl 0,6 überall: ohne 1 von 7 Betrieben Ölförderung, mit Schwerpunkt Öl 6 von 7);
  Vorkommen-Grenze (Öl 5 % → keine Ölförderung, Hinweis „keins“, Empfehlung); träger Wechsel (7 fremde Betriebe: nach 1/5/10/30/60
  Tagen 1/2/4/5/7 umgestellt, Verlustbetriebe 7 in 5 Tagen, Automatik 0); ohne Vorkommen kein Wechsel (33 blockiert); Förderung
  = Firmen-Ledger = Kassenbuchung; Baustopp → 0; Spielstand Rundreise + Weiterlaufen bitgleich; fehlender Abschnitt.
  Info: Ölfeld im Osten, Stadt 12 Tage – ohne Schwerpunkt 1 von 4 Industriebetrieben über dem Feld fördert/verarbeitet Öl,
  mit Schwerpunkt Öl/Gas + Förderung gering 3 von 4 (kleine Stichprobe).
- Offen: eigener Viehbetrieb (dann Schwerpunkt Viehzucht), Förderung je Gut statt je Branche, Bodenwert/Lärm-Wirkung von
  Schwerpunkten, Schwerpunkt auch für Gewerbe/Büro (Tourismus, Technik-Büros), NPC-Städte mit Schwerpunkten (Etappe 7),
  Balancing der Fördersätze gegen echte Steuereinnahmen großer Städte.

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
  (Land, Gebirge teurer; Seewege zu den 3 nächsten Regionen ×2,5); danach bleibt **freies Land** übrig (`PlanetSettings.FreeLandShare`
  32 %, `Region.IsFree`/`Country == null`, s. „Spielstart & Startregion“). Namen aus dem Silbengenerator (eindeutig), Hauptstadt =
  beste Zelle (Boden, Fluss, Küste, Klima). **Profil** (`CountryProfile`): `Raw` 0..1 aus Natur/Lage für Tourismus
  (Küste, Wärme, Berge, Vielfalt), Landwirtschaft (Boden × Wachstum × Wasser), Rohstoffe (Erz/Kohle/Öl/Wald), Industrie
  (Kohle+Erz, Wasser, Flüsse, Ebene, Küste), Technik (gemäßigt, Küste, Flüsse, „Kultur“-Zufall); `Rank` = Rang unter allen
  Ländern; `Comparative` = Rang relativ zu den eigenen Stärken → `Primary`/`Secondary` (Spezialisierung).
- **LOD-Volkswirtschaft** (`NationEconomy`, je Land, täglich über `WorldSystem`): Bevölkerung (aus Boden/Klima/Küste/Flüssen),
  8 Güter-Kategorien (`GoodsCategory`: Lebensmittel, Holz, Erze/Baustoffe, Energie, Industrie, Konsum, Technik, Tourismus).
  Kapazität je Kopf aus Profil/Vorkommen, Ernte und Heizbedarf aus dem Klima der Hauptstadt, Vorleistungen (Erz+Energie+Holz →
  Industrie → Konsum/Technik; Tourismus braucht Lebensmittel/Energie) begrenzen die Produktion, Haushaltsbedarf je Kopf,
  Lager (Ziel 20, max. 120 Tage, Überschuss → `Overflow` = Exportpotenzial), Preis träge (±3 %/Tag je Lücke, 0,25–4 × Grundpreis;
  Startpreis spiegelt Knappheit; seit Etappe 8 weltweit kalibriert, s. „Handel (Etappe 8)“). `Balance(cat)` = Überschuss/Defizit je Tag für die Handels-KI, `WorldPrice` =
  produktionsgewichtetes Mittel. Spieler-Land = Land der Startregion (`Planet.PlayerCountry`, `Country.IsPlayer`): seit
  „Spielstart“ der **Stadtstaat** in einer freien Region (eigene kleine Volkswirtschaft, 5 000 Einwohner Umland); in alten
  Spielständen das NPC-Land der Startregion (läuft aggregiert als „Rest des Landes neben der Stadt“).
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
- Später: ~~Startregion im Menü wählen~~ (✅ s. „Spielstart & Startregion“), ~~Klima auf Gelände-Farben (trocken → gelbes Gras, Schnee)~~ ✅
  (s. „Umwelt (Etappe 6)“), Bewuchs (Bäume im Winter kahl),
  Wetter in Strom (Wind/Sonne)/Verkehr, Meeresströmungen, Flüsse auf der Weltkarte als Linien, Stadt-Wirtschaft (G5) speist ins
  Spieler-Land, Wechsel in andere Städte (LOD detailliert). Handel zwischen Ländern: s. „Handel (Etappe 8)“.

## Spielstart & Startregion
- **Politisches Modell** (`PlanetGenerator.Political`, `FreeLand`): Nach der Länderverteilung bleiben ~32 % der Regionen
  **frei** (`PlanetSettings.FreeLandShare`, `Region.IsFree`, `Region.CountryId == -1`, `Region.Country == null`): Schlüssel =
  0,5 × Abstand zur eigenen Hauptstadt (relativ) + 0,25 × Abstand weltweit + 0,2 Grenzregion + 0,1 Insel + 0,35 Hash-Rauschen →
  freies Land liegt zwischen den Ländern und in Randlagen, in allen Klimazonen und auf mehreren Kontinenten; Hauptstädte bleiben,
  was danach nicht mehr über eigene Regionen (Land/See) mit der Hauptstadt verbunden ist, wird ebenfalls frei. NPC-Länder behalten
  Namen/Ids/Reihenfolge; Profile und Volkswirtschaften rechnen nur über eigenes Land. **`FreeLandShare = 0` = altes Modell
  bitgleich** (Prüfsumme in `StartFinderTests`, zusätzlich 60 Tage Handel gegen den alten Code verglichen).
- **Spieler als Stadtstaat:** `Planet.SetStartRegion(freie Region)` legt ein `Country` mit `IsPlayer` + `IsCityState` **hinten**
  an `Countries` an (Id = Anzahl NPC-Länder), Hauptstadtzelle wie bei Ländern, Profil aus der Region (Rang gegen die NPC-Länder,
  deren Ränge bleiben), Volkswirtschaft `WorldEconomy.PlaceCityState` (5 000 Einwohner Umland, dieselben Kalibrierfaktoren, keine
  Neukalibrierung). Erneuter Aufruf mit einer freien Region verlegt ihn; Wechsel in ein NPC-Land → Ausnahme. Startregion in einem
  NPC-Land (alte Stände, Tests) = altes Verhalten (dieses Land wird Spieler-Land). `SuggestStartRegion` wählt nur freie Regionen,
  wenn es welche gibt. Dazu `Planet.IsStartable(r)`, `FreeRegionCount`, `PlayerCityState`, `AssignRegion` (hält Id, Objekt und
  Listen gleich). **API kompatibel:** `Region.CountryId`/`Country.Regions`/`PlayerCountry` bleiben; neu sind nur Felder
  (`Region.Country`, `IsFree`, `Country.IsCityState`). `CountryOfCell` liefert für freies Land **null** – Leser müssen das abfangen.
  **Handel/LOD:** Der Stadtstaat ist eine normale Handelspartei (`TradeSystem.PlayerId` = seine Id, Handelsort = Mitte der
  Startregion, Grundzoll 0); die Stadt-Seite läuft wie bisher über das Grenzlager. Am Handel musste nichts geändert werden
  (geprüft: Lieferungen/Bezahlung laufen für `IsPlayer` über `City`, Lagebild/Weltpreis rechnen mit seiner kleinen
  Volkswirtschaft, Beziehungen/Nachbarschaft über `CountryId >= 0`).
- **`PlayerRealm`** (`world.Realm`, immer gesetzt, Abschnitt „realm“ v1, seit Etappe 7 v2): `CityName` (= Name der **aktiven**
  Stadt, Start aus `WorldSettings.CityName`, Standard „Neustadt“, `RenameCity`), `DisplayName` = `NationName` ?? „Freie Stadt
  {Hauptstadt}“ (Name des Stadtstaats auf Weltkarte und im Handel), `NationName`, `FlagColor`, `CityRegions`, `MinCitiesForNation` 3,
  `CanFoundNation` – **umgesetzt in Etappe 7**, s. „Reich (Etappe 7)“. Speichert zusätzlich den **Besitz aller Regionen** (Land-Id je
  Region) und stellt Abweichungen beim Laden wieder her.
- **Startsuche** `Simulation/World/StartFinder` (rein, getestet in `StartFinderTests`) mit Katalog `StartCriteria` (Regeln +
  Erklärtexte als Daten): `RegionFacts` je freier Region (≥ 4 Zellen) aus den echten Planet-Daten – Klimazone
  (`ClimateOf`: polar < −6 °C/Eis, trocken = Wüste oder < 250 mm, subpolar = Tundra/Taiga/< 4 °C, gemäßigt < 14 °C,
  subtropisch < 21 °C, sonst tropisch), Relief-Index max(Zerklüftung/150 m, Höhe/600 m) (< 1 flach, 1–3 hügelig, ≥ 3 gebirgig),
  Küstenanteil, Flussgröße (Abfluss/Flussschwelle), Landwirtschaft (Boden × Wasser × Wachstum), Tourismus (warme trockene Küste
  oder Berge), Lebensfreundlichkeit, Entfernung zum nächsten NPC-Land (Dijkstra über alle Zellen, km).
  **Gruppen:** Klima (eine), Lage (mehrere: Meeresküste, großer Fluss, Binnenland – Seen gibt es im Weltmodell nicht),
  Gelände (eine), Schwerpunkt (mehrere: Landwirtschaft, Forst, Erze, Kohle, Öl/Gas, Baustoffe, Tourismus, Hafen & Handel),
  Schwierigkeit (leicht/normal/schwer), Nachbarn (nahe ≤ ~320 km / abgelegen ≥ ~500 km). Score 0..1 je Option, passt ab 0,5;
  Schwellen so, dass eine Vorliebe etwa das obere Fünftel bis Drittel trifft (Seeds 2024/7/99).
  **Ausschluss aus der Welt:** `Evaluate(auswahl)` zählt je Option die freien Regionen mit Auswahl + Option (exklusive Gruppe:
  statt der bisherigen); 0 → `Unavailable` (Tooltip: „keine passende Region zu deiner übrigen Auswahl – {Erklärtext}“), ≤ 3 →
  `Rare`. Beispiele Seed 2024: Küste → Binnenland gesperrt; Küste + Erze → subpolar gesperrt, gemäßigt selten (2);
  Polar + großer Fluss = 0. `Suggest` = beste 5 (Summe der Scores + 0,6 × Lebensfreundlichkeit, bei „schwer“ umgekehrt) mit
  Begründung, z. B. „gemäßigt, hügelig, Küste, großer Fluss, fruchtbar, Kohle im Flachland im Westen, nächstes Land Boumavien
  250 km“ (Lage der Vorkommen = Schwerpunkt der ergiebigen Zellen + Relief dort) und 8 Balkenwerten. `Describe(regionId)` für die
  freie Wahl auf der Karte.
  **Erz und Küste:** Auf Zellebene liegt an Küsten halb so viel Erz wie im Bergland (Test); auf Regionsebene (~700 km) enthalten
  Küstenregionen oft auch Gebirge (wie die Anden), daher ist „Küste + Erze“ nicht automatisch leer – selten ist Erz an **flacher**
  Küste (5–24 % der Erzregionen), Erzregionen sind überwiegend hügelig/gebirgig (55–95 %).
- **Neues-Spiel-Fenster** `Game/Persistence/NewGameWindow` (Baukasten, mittig breit, 4 Schritte): (1) Welt-Seed / „Neu würfeln“,
  Kartenvorschau (Planet + StartFinder im Hintergrund-Task; freies Land hell, Länder getönt), Zahlen; (2) Vorlieben als
  `Ui.ChoiceChip` (Treffer als Zahl, „selten · n“ mit Akzentrand, gesperrt ausgegraut mit Erklärung, Meldung „n freie Regionen
  passen“); (3) Vorschläge als Liste mit Begründung, Goldpunkte auf der Karte, Klick wählt, Klick auf freies Land wählt frei,
  Balkenwerte der Auswahl; (4) Stadtname (`Ui.TextInput`) → „Stadt gründen“ = `SaveManager.StartNewGame` →
  `SimulationRunner.NewGame(WorldSettings)` (Seed, `StartRegion`, `CityName`). Gleicher Seed + Einstellungen → gleiche Regions-Ids.
  Esc geht einen Schritt zurück. **Erster Programmstart ohne Spielstand** (`SaveManager.HasAnySave`): Runner erzeugt nur eine
  schnelle Platzhalterwelt ohne Weltkarte (angehalten, `AwaitingNewGame`), das Fenster öffnet sich und lässt sich erst nach dem
  Gründen schließen („Schnellstart“ = beste Region ohne Vorlieben). Mit Spielständen startet das Spiel wie bisher mit der
  Standardwelt; Spielmenü (Esc) → „Neues Spiel einrichten …“. Optik ungetestet (Unity nicht gestartet).
- **Spielstand:** Weltabschnitt **v3** = v2 (Zeitmodell: Verkehrsschritt) + `FreeLandShare` + Stadtname der Einstellungen; Abschnitt „realm“ (s. o.).
  **Migration (Entscheidung): alte Stände behalten ihr Spieler-Land.** v1/v2 laden mit `FreeLandShare = 0` → exakt die alte Welt
  (alle Regionen NPC, Spieler = Land der Startregion), weil Länder-Volkswirtschaften („planet“) und Handel („trade“) nach
  Länder-Ids gespeichert sind – eine neu verteilte Welt würde nicht mehr zu Verträgen, Beziehungen und Lagern passen.
  Fehlt „realm“, heißt die Stadt „Neustadt“ (wie der alte Platzhalter).

## Reich (Etappe 7)
Vision des Spielers: „Man startet als freie Stadt und kann zu einem Land werden. Später weitere Städte (selbst oder durch NPCs). Handel
intern und extern.“ Keine echten Ländernamen; der Spieler benennt sein Land.
- `Simulation/Realm` (Namespace `CityBuilder.Simulation.Realm`): `RealmRules` (alle Zahlen), `RealmCity` + `CityEconomy` (aggregierte
  Stadt), `RealmDelivery`/`RealmShipment`/`RealmRoute`/`RealmNews` + Commands, `RealmSystem` (+ `.Nation.cs`, Logik, `ISimulationSystem`,
  läuft **zuletzt** in `CreateDefault`, erreichbar über `world.Realm.System`), `RealmSwitch` (Stadtwechsel), `PlayerRealm.Save.cs`
  (Abschnitt „realm“ v2, `partial` zu `World/PlayerRealm`). Der Zustand liegt komplett in `PlayerRealm` (`world.Realm`).
- **Eine Stadt = eine `SimulationWorld`, genau eine läuft detailliert** (die betrachtete, `Realm.ActiveCity`), alle anderen
  **aggregiert** (`RealmCity.Economy`). **Entscheidung Spielstand/Wechsel:** je anderer Stadt ein **eingebetteter Unter-Spielstand**
  (`RealmCity.Snapshot` = ihre lokalen Abschnitte als Rohbytes, unkomprimiert im Abschnitt „realm“ → Prüfsummen hängen nicht vom GZip ab)
  + **reichsweite Abschnitte** `RealmSwitch.GlobalSections` = „world“ (Uhr), „realm“, „planet“ (Länder-Volkswirtschaften), „trade“
  (Verträge, Beziehungen, Zölle, Grenzlager). Alles andere (Straßen, Bürger, Kasse, Firmen, Gateways, Bahn, Hafen, Flughafen …) ist lokal.
  Begründung: Der Wechsel nutzt genau den erprobten Weg `SaveGame.Restore` (Welt aus Einstellungen + Seed neu, Abschnitte lesen) – kein
  System musste „mehrstädtefähig“ werden, Determinismus bleibt (Test: zwei gleiche Läufe mit Wechseln bitgleich, Speichern/Laden eines
  Reichs mit Unter-Spielständen → bitgleich weiter, auch nach erneutem Wechsel). Ein Spielstand enthält alle Städte.
- **Wechsel** (`RealmSwitch.Switch(world, cityId)` → neue Welt; Unity ersetzt sie wie beim Laden, `WorldReplaced`): laufenden Tick zu Ende
  rechnen → Kennzahlen der aktiven Stadt ins Aggregat (`CityEconomy.CaptureFrom`) → lokale Abschnitte als Schnappschuss in ihre `RealmCity`
  → Ziel aktiv → Container: Weltabschnitt mit Region (`StartRegion`), Geländeseed und Name der Zielstadt + aktuelle Uhr
  (`SaveGame.WorldSection`/`ReadWorldSettings` – kopiert alle gespeicherten Einstellungen), lokale Abschnitte der Zielstadt (nie
  detailliert: frisch erzeugte Stadtkarte aus ihrer Region inkl. vorgebauter Autobahn), reichsweite Abschnitte → `Restore`. Danach: Kasse =
  aggregierte Kasse (Differenz als „Reich: Einnahmen/Ausgaben“), Exportware auf dem Weg ins Grenzlager gilt als angekommen. Fehler → alte
  Welt bleibt gültig (Reichszustand wird zurückgesetzt). Bewusst **kein Command** (ersetzt die Welt wie ein Laden). Uhr läuft durch:
  eine zurückgeholte Stadt springt in der Zeit (Bürger/Systeme laufen ohne Nachholschleifen weiter; geprüft: keine Tages-Schleifen im Code).
  Schnappschuss einer kleinen Teststadt ≈ 1,3 MB, Wechsel im Test < 1 s (zwei Welterzeugungen bei neuer Stadt).
- **Stadt gründen** (`FoundCityCommand(region, name, zelle)`, Prüfung `CheckFoundCity` mit allen Bedingungen ✓/✗ für die UI): Region frei
  (oder eigenes Gebiet – dann freie Weltzelle mit ≥ 2 Zellen Abstand), höchstens 8 Städte, **Verbindung** über echte Wege (Nachbarregion
  einer eigenen Stadt + Mutterstadt an der Autobahn angeschlossen, oder Küste ↔ Küste + Hafen in Betrieb), Mutterstadt (die detaillierte)
  **≥ 1 500 Einwohner**, **Rathaus** (`HasTownHall`), **250 000 €** aus der Stadtkasse („Reich: Ausgaben“). Wirkung: Region gehört dem
  Spieler (`Planet.AssignRegion`), neue `RealmCity` (Geländeseed/Seed aus der Startstadt + Id, Kasse 100 000 €, leere Karte),
  Nachbarländer der Region −2 Beziehung. Gründungen in der eigenen Region = andere Weltzelle, anderer Geländeseed.
- **Aggregierte Stadt** (`CityEconomy.StepDay`, täglich für alle nicht aktiven Städte): Produktion/Bedarf je Güterkategorie = Einwohner ×
  je Kopf (gemessen in der Detailsimulation: G5-Märkte des Vortags in Kategorie-Einheiten; neu gegründet: Vorkommen der Region),
  Lager deckt Bedarf, Fehlmengen vom Weltmarkt (× 1,1), Überschuss über 30 Tage Lager hinaus (× 0,85; Ware für eigene Lieferungen bleibt
  liegen), Kasse = Steuern je Kopf (Wochenmittel) + 4 % Außenhandelssaldo − Unterhalt; Zufriedenheit läuft zum gemessenen Wert
  (− Schulden, − Lücken Lebensmittel/Konsum), Wachstum ± 0,4 %/Tag nach Zufriedenheit (neutral 0,55), höchstens bis zum Wohnraum
  (aggregiert wird nicht gebaut). Die aktive Stadt füllt dieselben Felder täglich aus der Detailsimulation → Reichsfenster zeigt für alle
  Städte dieselben Kennzahlen. Rückkehr: der Detailzustand ist die Wahrheit (Bevölkerung wie verlassen), nur die Kasse wird verrechnet.
- **Interne Lieferungen** (`ProposeDeliveryCommand(von, nach, ContractTerms)`, `CancelDeliveryCommand`; **dieselben `ContractTerms` und
  dieselbe Submit/Execute/Log-Schnittstelle wie Handelsverträge**, Ergebnis `TradeResult`, Partner = eigene Stadt, ohne Zoll und Strafe):
  wöchentlicher Versand über `RealmRoute` (Lkw über die Autobahn auf derselben Landmasse 1,35 × Großkreis × 0,03 €/t·km; **Bahn** wenn
  beide Städte einen Güterbahnhof in Betrieb haben × 0,56 und schneller; **Schiff** Küste ↔ Küste, 60 km Vorlauf je Stadt ohne eigenen
  Hafen; billigster Weg, Lieferzeit wie Etappe 8). Sender detailliert: Hook `TradeSystem.ExtraExportDue/ExtraExportPrice` (Firmen liefern
  per Lkw ins Grenzlager wie bei Exportverträgen, Verrechnungspreis = Welt × Faktor), Versand aus dem Grenzlager; aggregiert: aus dem
  Kategorie-Lager. Empfänger detailliert: Grenzlager (`ReceiveImport`, Zoll 0 – Firmen kaufen wie Vertragsware), aggregiert: Lager.
  Fracht zahlt der Sender, Verrechnung bei Ankunft Empfänger → Sender (Kategorien „Reich: Einnahmen/Ausgaben“, hinten angefügt).
- **Land gründen** (`FoundNationCommand(name, farbe, hauptstadt)`, Prüfung `CheckNation` mit **Fortschritt**): ≥ 3 Städte, ≥ 2 Rathäuser,
  ≥ 6 000 Einwohner gesamt, Hauptstadt mit Rathaus, Name frei (kein NPC-Land, nicht „Freie Stadt …“); nur im neuen politischen Modell
  (alte Stände ohne freies Land: Spieler ist schon ein Land). Wirkung: `NationName`/`FlagColor`/Hauptstadt → Name, Farbe, Hauptstadtzelle
  des Spieler-Landes (`Country.IsCityState` bleibt als Kennzeichen „vom Spieler gegründet“; Weltkarte färbt über `Planet.PoliticalVersion`
  um). **NPC-Reaktion:** Beziehung +3 Höflichkeit, −6 bei gemeinsamer Grenze, −4 × Protektionismus; **Anerkennung** bei Neigung ≥ 0
  (Beziehung + 15 × (1 − Protektionismus) − 10 Nachbar), sonst Neuprüfung alle 30 Tage (Zufall nach Neigung), Anerkennung +2 Beziehung
  (Test: 10 von 16 Ländern sofort). **Landeskasse:** 10 % der täglichen Steuern jeder Stadt, `TransferFundsCommand` (Zuschuss an eine Stadt).
  **Länder-Volkswirtschaft des Spielers = Summe seiner Städte** (ab 2 Städten: 5 000 Umland je Stadt-Region + Einwohner aller Städte,
  `NationEconomy.SetPopulation`). Zölle/Verträge gehören ohnehin dem Spieler-Land (Länder-Id) → gelten landesweit; abgewickelt über das
  Grenzlager der jeweils detaillierten Stadt (das Grenzlager wandert beim Wechsel mit).
- **NPC-Expansion** (`Realm.NpcExpansion`, Standard aus, das Spiel schaltet ein – `SimulationRunner.npcExpansion`): wöchentlich je NPC-Land
  Chance 1,5 % × (0,5 + Risiko) × (1 + Hunger), dann 120 Tage Pause; Ziel = freie Nachbarregion per Softmax über den Nutzen (Boden, Küste,
  Fluss, Bodenschätze, Wald; Spielergrenze meiden vorsichtige Länder); höchstens 45 % des anfangs freien Landes. Konflikte nur als
  Beziehung: Anspruch am Spielergebiet −6 („Grenzstreit“, Meldung), andere Nachbarn −2. Kein Krieg. Besitz steht schon im Abschnitt „realm“.
- **Spielstand** „realm“ **v2** (v1 → eine Stadt, Seeds der frisch erzeugten Welt): aktive Stadt, Hauptstadt, Städte (Aggregat +
  Schnappschuss), Lieferungen + Ware unterwegs, Landeskasse, Anerkennung, Expansion (Zufall `SimRandom`, Abkühlzeiten), Meldungen,
  Command-Warteschlange und -Log.
- **Unity:** `Game/UI/Windows/RealmWindow` (mittig, **Pos1** – F12 gehört dem Flughafen –, Fahnen-Knopf oben `HudIconKind.Flag`): Reiter
  Städte (Kennzahlen, Status detailliert/aggregiert/neu, Weg, „Zur Stadt wechseln“, Umbenennen, „Neue Stadt gründen …“ → Weltkarte),
  Lieferungen (Liste, Unterwegs, neue Vereinbarung mit Weg/Wert/Fracht, Beenden), Land (Fortschritt + Bedingungen, Name, Flagge aus 8
  Grundfarben, Hauptstadt; danach Landeskasse, Zuschüsse, Anerkennung je Land), Meldungen. `WorldMapUiWindow.Realm.cs`: „Deine Städte“
  (Klick = wechseln), Klick auf eine Region zeigt die Gründungsbedingungen + Namensfeld + „Stadt gründen“. `WorldMapWindow`: Stadtmarker
  (weiß = aktiv, gold = weitere), Neufärben bei Besitz-/Farb-/Städtewechsel. Obere Leiste: Stadtname + „ · {Land}“ bzw. „ · n Städte“.
  `Game/Realm/RealmActions` (Commands ausführen, Wechsel mit Meldung). **Optik ungeprüft** (Unity nicht gestartet): Layout des Fensters,
  Marker-Kreuze auf der Karte, Fahnen-Icon, Dauer/Ruckler beim Wechsel im Editor.
- **Tests** `RealmTests` (14): Startzustand/API, Gründungsbedingungen (Einwohner, Rathaus, NPC-Land, Erreichbarkeit), Gründen (Besitz,
  Kosten, Abstand, Weg), Wechsel hin/zurück/wieder hin (Straßen bitgleich, Bevölkerung, Uhr), Determinismus + Speichern/Laden mit
  Unter-Spielständen, aggregiertes Wachstum/Schrumpfen, aggregierter Tageslauf + Kassenausgleich, Lieferungen aggregiert → Grenzlager und
  Grenzlager → aggregiert (Hook), Lieferung über Wechsel + Laden, Land gründen (Bedingungen, Wirkungen, Anerkennung, Landesabgabe,
  Zuschuss, Laden), NPC-Expansion (langsam, Obergrenze, deterministisch, nur angrenzend), Migration realm v1.
- **Nächste Teilstufe (offen):** Pendler/Zuzug zwischen Nachbarstädten (heute keine), NPC-Städte als eigene Siedlungen (NPC-Länder
  wachsen heute nur über Regionen, ihre Volkswirtschaft bekommt keine Einwohner dazu), Gebietsstreit mit Folgen (Verhandlung, Abtretung),
  Grenzlager je Stadt statt eines wandernden Reichs-Grenzlagers, Landesgesetze (einheitliche Steuersätze/Zölle je Stadt), Hauptstadt-Bonus,
  Medien-Schlagzeilen zu Gründung/Anerkennung/Grenzstreit, Profil (Stärken) des Spieler-Landes über alle Regionen statt der aktiven Region,
  interne Lieferungen mit sichtbaren Lkw/Zügen zwischen den Karten, Kassenausgleich auch für Lager/Bevölkerung beim Zurückkehren.

## Handel (Etappe 8)
- `Simulation/Trade` (Namespace `CityBuilder.Simulation.Trade`): `TradeSystem` (+ `.Ai.cs`), `TradeGoods`, `TradeGeography`,
  `TradeContract` (Bedingungen, Angebot, Vertrag, Lieferung, Gedächtnis), `TradeCommands`, `NationTrade` (Persönlichkeit, Zölle,
  Vertrauen), `CityTradeDepot` (Grenzlager), `TradeRules` (alle Zahlen). Nur mit Weltkarte: `CreateDefault` legt `world.Trade` an,
  sobald `world.Planet.Economy` existiert (läuft zuletzt; Tests ohne Welt bleiben unverändert). G5 kennt nur `Goods/ITradeGateway`.
- **Güter ↔ Kategorien:** Agrar = Getreide, Vieh, Mehl, Lebensmittel; Holz = Holz, Bretter; Erze/Baustoffe = Erz, Sand, Zement,
  Baustoffe; Energie = Kohle, Erdöl; Industrie = Stahl, Kunststoff, Glas; Konsum = Kleidung, Möbel; Technik = Elektronik, Fahrzeuge;
  Tourismus ohne Gut. 1 Wareneinheit = Grundpreis Gut / Grundpreis Kategorie Kategorie-Einheiten (wertgleich).
- **Preise:** Länderpreis je Gut = Grundpreis × (Kategoriepreis/Kategorie-Grundpreis des Landes) × (1 ± 6 % Hash je Land/Gut).
  **Weltpreis** = Σ Länderpreis × w / Σ w, w = geglättetes Handelsvolumen (Kategorie-Einheiten/Tag, EMA 0,1) + 2 % der Produktion.
  Die Stadt bekommt ihn über `GoodsSystem.WorldPriceSource` (begrenzt 0,5–2 × G5-Grundpreis) statt des Rauschens → Paritätsband,
  Import/Export und Standortwahl reagieren auf Knappheit der Länder.
- **Transport** (`TradeGeography`, einmal beim Start): Handelsort = Hauptstadt (Spieler: Mitte der Startregion); Landweg nur auf
  derselben Landmasse (Großkreis × 1,35 × 0,03 €/t·km), Seeweg über den nächsten Küstenpunkt (Breitensuche über Land: Vor-/Nachlauf
  × 0,03 + Großkreis Hafen–Hafen × 1,25 × 0,004 €/t·km + 2 × 4 €/t Umschlag) – der billigere gewinnt. Frachttonnen = max(Gewicht,
  Volumen × 0,4). Lieferzeit = ⌈1 + Land/600 km + See/500 km (+2 Hafentage)⌉ Tage. Der Verkäufer trägt die Fernfracht (frei Grenze).
- **Schnittstelle (Spieler = NPC):** `ProposeContractCommand` (Angebot „verkaufe“ / Nachfrage „kaufe“ an ein Land oder alle; auch
  Handelsabkommen), `AcceptOfferCommand`, `RejectOfferCommand`, `WithdrawOfferCommand`, `CancelContractCommand`, `SetTariffCommand`.
  `Submit` reiht ein (nächster Tick), `Execute` sofort (UI, KI); `CommandLog` für Replays. `ContractTerms`: Gut, Menge/Woche, Wochen
  (1–52), Festpreis oder Weltpreis × Faktor, Strafanteil. Angebote gelten 7 Tage und werden **in der nächsten Nacht** beantwortet
  (während des Handelstags gestellte erst in der übernächsten).
- **Verträge:** Start am Folgetag (Spieler als Verkäufer: +6 Tage Vorlauf zum Sammeln), wöchentlicher Versand, Ankunft nach der
  Lieferzeit, Bezahlung bei Ankunft (Preis am Versandtag). Fehlmenge → Strafe = Fehlmenge × Preis × Strafanteil an den Käufer,
  Vertrauen des Käufers − (0,05 + 0,3 × Fehlquote), Beziehung − (4 + 12 × Fehlquote), Erinnerung „Vertragsbruch“; volle Lieferung:
  Vertrauen +0,01, Beziehung +0,4. Kündigung: Gebühr = min(4, Restwochen) × Wochenwert × Strafanteil × 0,5, Vertrauen −0,1,
  Beziehung −6; Ware unterwegs kommt noch an. Handelsabkommen: beide Zölle 0 für die Laufzeit (26 Wochen), Beziehung +0,5/Woche.
- **NPC-Seite:** Lager in Kategorie-Einheiten (`NationEconomy.Stock`); Versand nur über einer Reserve von 3 + 12 × (1 − Verlässlichkeit)
  Tagen Bedarf, unter 10 Tagen Reichweite bricht ein Land mit Wahrscheinlichkeit 0,5 × (1 − Verlässlichkeit) bewusst.
- **Stadt (`CityTradeDepot`, Vertragspartei = Stadtkasse):** Importware landet im Grenzlager (Kasse zahlt „Handel: Einkauf“), Firmen
  beziehen sie wie einen Import per Lkw ab Außenanschluss zu Vertragspreis + Zoll gegen das Lieferland („Handel: Verkauf“ + „Zölle“),
  wenn das höchstens 5 % teurer ist als lokal/Weltmarkt. Exportverträge: Firmen liefern vor dem Weltmarkt-Export bis 10 % Restfüllung,
  wenn Vertragspreis − Lkw ≥ 90 % lokal (volle Lager immer), Kasse zahlt den Vertragspreis; am Versandtag geht die Ware aus dem
  Grenzlager, Fernfracht zahlt die Kasse, das Käuferland zahlt bei Ankunft. Importware 14 Tage nach der letzten Ankunft bzw.
  Exportware ohne Vertrag geht zu 85 % des Weltpreises weg. **Grundzoll** des Spielers verteuert Weltmarkt-Importe der Firmen (Teil
  des Einkaufswerts) und geht an die Kasse. Neue Kassen-Kategorien (hinten angefügt): Zölle, Handel: Verkauf, Vertragsstrafen
  (erhalten) | Handel: Einkauf (inkl. Fracht), Vertragsstrafen (gezahlt); `EconomySystem.Post(cat, betrag)`.
- **Beziehungen/Politik:** Beziehung je Länderpaar −100..100, Start 10 ± 25 (Hash), Nachbarn −8; Drift 2 %/Woche zum Startwert;
  Zoll von A gegen B über 10 % kostet je Woche (Satz − 0,1) × 10 Punkte; unter −50 Embargo (keine Annahme, Verträge werden gekündigt).
  Zölle: `TariffSchedule` je Land (Grundzoll + Satz je Lieferland), NPC-Grundzoll = Protektionismus × 20 %; Vergeltung: liegt der Zoll
  eines Landes gegen n mehr als 5 Punkte über dem Satz von n, hebt ein protektionistisches n (≥ 0,3) wöchentlich um 2 Punkte nach,
  ohne Grund zurück zum Grundzoll. Vertrauen je gerichtetem Paar 0..1 (Start 0,6, Drift 1 %/Woche), Gedächtnis 12 Einträge je Land.
- **NPC = Utility AI** (`TradeSystem.Ai.cs`, keine Sprach-KI): Persönlichkeit (Risiko, Protektionismus, Verlässlichkeit,
  Geschäftssinn; Hash + Profil). Lagebild täglich: Reichweite = Lager/Bedarf, Netto = Saldo ± Verträge; Defizit = 0,6 × (20 −
  Reichweite)/20 + 0,8 × (−Netto/Bedarf); Überschuss = (0,6 × (Reichweite − 35)/60 + 0,8 × Netto/Bedarf) × (1 + 0,3 Primär-/0,15
  Sekundärstärke). **Bewertung** eines Angebots = Summe von Bausteinen (Preis gegen Inlandspreis inkl. Zoll/Fracht × Geschäftssinn,
  Bedarf, Menge gegen Bedarf/Lieferfähigkeit, Schutz eigener Stärken, eigener Bedarf beim Verkauf, Überschuss, Strafe, Beziehung,
  Vertrauen, Festpreis-Laufzeit) + 0,1 → Annahme mit 1/(1 + e^(−U/T)), T = 0,08 + 0,15 × Risiko; **Grund** = stärkster Gegen- bzw.
  Fürgrund („zu teuer: höchstens …“, „Preis zu niedrig: mindestens …“, „Menge zu groß“, „Bedarf gedeckt“, „kein Vertrauen nach
  Vertragsbruch“ …). **Wochenentscheidung** (je Land jeden 7. Tag, gestaffelt): Softmax (T = 0,05 + 0,2 × Risiko) über Nichtstun
  (0,15), Kaufen je Defizit-Kategorie, Verkaufen je Überschuss-Kategorie, Handelsabkommen (Partner mit Beziehung ≥ 25 und laufendem
  Vertrag); Partner per Softmax (Überschuss/Defizit, Beziehung, Vertrauen, Preisvorteil, Lieferzeit), Menge aus Bedarf und
  Lieferfähigkeit (beim Spieler aus Importen/Kapazität der Stadt), Laufzeit 4 + 8 × (1 − Risiko) ± 2 Wochen, Preis zwischen
  Untergrenze Verkäufer × 1,04 und Obergrenze Käufer nach Geschäftssinn, Weltpreis-Kopplung mit Wahrscheinlichkeit 0,7 × Risiko.
  Höchstens 2 Angebote je Entscheidung, 3 offene je Land, 6 offene an den Spieler (1 je Land).
- **UI:** `Game/UI/Windows/TradeWindow` (mittig, **F11**, Münz-Knopf in der oberen Leiste, „Handel“ beim Land in der Weltkarte):
  Länder (Beziehung, Stärken, Persönlichkeit, Vertrauen in uns, Handelsweg, Zölle, Lage je Kategorie, Gedächtnis, Abkommen anbieten),
  Weltpreise mit Wochentrend + lokalem Preis + Grenzlager, Angebote (annehmen/ablehnen, eigene mit Antwort/Begründung, neues Angebot
  mit Vorschau der NPC-Einschätzung über `PreviewOffer` + `Evaluate`), Verträge (Lieferstatus, unterwegs, Grenzlager, Kündigen),
  Zölle (Grundzoll, Satz je Land, Handelsbuchungen der Kasse). Aktionen per `Execute` (wirkt auch bei Pause). Optik ungetestet.
- **Leistung** (Testprojekt): 36 Länder, 1 Jahr Länder + Handel ≈ 120 ms (≈ 0,33 ms/Tag, seit der Kalibrierung mehr Verträge),
  Handel allein ≈ 60 ms/Jahr, Wege 8 ms.
- **Kalibrierung der Länder-Volkswirtschaften** (`WorldEconomy.Calibrate`, `NationEconomy.TickDay`): Vorher war die Welt global knapp
  (Produktion/Bedarf Industrie 0,43, Technik 0,00). Ursachen: (1) Vorleistungen nach Reihenfolge – Konsum nahm alle Industriegüter,
  Technik bekam nichts; jetzt bekommt jeder Abnehmer seinen Anteil (Lager / noch offener Bedarf aller späteren Abnehmer).
  (2) Kapazitäten je Kopf passten nicht zum Bedarf inkl. Vorleistungen (überschlagen: Industrie ~0,19 statt ~0,35 je Kopf, Energie ~0,5 statt
  ~1,1). Jetzt wird beim Erzeugen je Kategorie weltweit skaliert, rückwärts durch die Kette (Endprodukte gegen Haushalte, Industrie
  gegen Haushalte + Vorleistungen, dann Rohstoffe; Heizen/Kühlen, Ernte und Tourismus-Saison im Jahresmittel): Σ Kapazität =
  Σ Bedarf × 1,04 (Lebensmittel 1,12 wegen 0,5 %/Tag Verderb). Länderunterschiede bleiben relativ. Ergebnis (Seeds 2024/7):
  Welt-Produktion/Bedarf je Kategorie 0,90–1,09; Länder mit Primärstärke haben 1,16–1,64 × Kapazität ihres Bedarfs (übrige 0,9–1,6).
  Ein Jahr ohne Handel: Lagerlücke (Wert unter 20 Tagen Reichweite) wächst von 544 auf 607 G€; mit Handel Tag 365 269 G€,
  Summe Tag 60–365 36 % kleiner.
- **Medien** (`TradeMedia.Connect` in `CreateDefault`, Event `TradeSystem.News`): Schlagzeilen-Themen `TradeDeal` („Handelsvertrag
  mit {Land}“ bei jedem Vertrag/Abkommen des Spielers), `Embargo` (Beziehung zum Spieler fällt unter −50), `TariffRaise` (ein Land
  erhöht seinen Zoll gegen uns); neuer Platzhalter {Land}. Nur mit Medienhaus sichtbar, Sperrfristen wie üblich.
- **Später (Stufe 2+):** Regierungen/Bündnisse (Zollunion, gemeinsame Außenzölle, Sanktionen, Abstimmungen), Diplomatie-Aktionen
  (Geschenke, Drohungen), Kredite zwischen Ländern, echte Häfen/Güterbahnhöfe der Stadt statt Grenzlager an der Straße (Güterbahnhof ✅ Bahn Stufe 2, Hafen ✅ Wasserstraße Stufe 3 – das
  Grenzlager selbst bleibt als Buchungsort), Tourismus als
  Dienstleistungshandel, NPC-Länder kündigen unrentable Verträge, Preis-/Mengenverhandlung (Gegenangebote), Handel zwischen eigenen
  Städten (✅ Etappe 7: interne Lieferungen, s. „Reich“), Multiplayer über das Command-Protokoll, Speichern (alle Zustände in `TradeSystem`/`NationTrade`/`CityTradeDepot`).

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
  zusätzlich Newspaper, RadioStation, TVTower (Medien).
- **Automatik für neue Modelle (Eigene Assets, 2026-11-03):** Beschreibungsdatei **`Name.json`** neben dem FBX (Felder `role`,
  `use`, `zone`, `level`, `placement`, `biome`, `service`, `facility`, `transport`, `tags`, `waitsFor`) hat Vorrang vor den
  Ordnerregeln; beides steht in `Editor/OwnModelDescriptions.cs` (eine Stelle für alle Zuordnungen), Anleitung für die Blender-KI:
  **`docs/Modell-Konvention.md`**. `OwnModelCatalogBuilder` baut daraus `Facilities`/`Services` (inkl. Leisure/Landmarks)/
  `Transport`/`People` und neu **`Extras`** (Rollen agriculture, infrastructure, prop, bush, rock; je Eintrag `Role`, `Use`,
  `Placement`, `Tags`, `Size`; Abfrage `Extra(name)`, `WithRole(role)`, `Find(role, use, tag)`), der `AssetCatalogBuilder` nimmt
  Gebäude (Zone, feste Stufe `level` → `ModelEntry.Level`), Bäume und Laternen aus derselben Zuordnung. Neubau bei jeder FBX-/
  PNG-/**JSON**-Änderung und beim Editorstart, wenn der Quellen-Stempel (`OwnModelCatalog.SourceStamp`) abweicht. Ungenutzte
  Modelle → **`Models/_Unused.txt`** (erzeugt, im `.gitignore`; „wartet auf: Flughafen/Hafen/Taxi/U-Bahn/Bahn/Klappbrücke …“,
  darunter alle verwendeten mit Ort) und Editor-Log.
- **Darstellung der neuen Modelle** (Begleiter am RoadTool, Block „Eigene Assets“; fehlt ein Modell, bleibt alles wie vorher):
  `Game/Assets/ModelParts` (Prefab → instanzierbare Teile je MeshFilter, bewegliche Kindobjekte getrennt, `PoseAt`) +
  `PartsRenderer` (Instancing mit Palette, Kacheln für Entfernungs-LOD), `ModelSpinner` (dreht „Wheel“ des Riesenrads).
  `Zoning/FarmView`: Industriegrundstücke mit Landwirtschafts-Firma (Rezept „farm“) zeigen fest aus der Gebäude-Id Hof, Windmühle
  (Flügel drehen), Weide, Grünland mit Kühen, Obstgarten, Weinberg, Gewächshaus oder Acker (Weizen/Mais/Raps/Kartoffel/Gemüse) –
  Äcker je Monat (Klima, Südhalbkugel versetzt): gepflügt → Aufwuchs (Meadow) → reif → abgeerntet mit Heuballen, Traktoren März–
  Oktober; `BuildingView` lässt diese Grundstücke aus (`FarmView.Handles`). Das Rezept kennt nur Getreide + Vieh, Obst/Wein/Gemüse
  sind optische Spielarten. `Roads/InfrastructureDecoView`: Brückenpfeiler-Modell an den Mesh-Pfeilerstellen
  (`RoadMeshBuilder.StructureSpots`, Mesh-Pfeiler entfallen über `RoadMeshBuilder.ModelPillars`), Tunnelportale am Übergang Trog →
  Tunnel (Öffnung nach außen), Schilderbrücken 160 m vor jeder Autobahn-Abfahrt (Knoten mit abgehender Rampe), Baustellen-Set an
  Gebäuden im Bau. `UtilityView`: Hochspannungsmast = `PowerPylon` (6 Seile an den Isolatoren). `Environment/NatureDetailView`:
  Büsche am Waldrand (Waldraster 0,3 ± 0,3), selten auf Wiesen, entlang der Parkränder; Felsen nach Steigung (ab ~14°) und Höhe
  (> 40 m), Straßen räumen weg, nicht auf Gebäuden/Bauwerken/Wasser (LOD 900/1600 m). `Environment/StreetFurnitureView`: Requisiten
  auf den Gehwegen der Stadtstraßen (11-m-Raster, 45 % belegt, gewichtet: Mülleimer > Bank/Pflanzkübel > Hydrant > …; nicht ≤ 6 m
  an Kreuzungen, ≤ 5 m vor Zufahrten, ≤ 12 m an Haltestellen, ≤ 2,5 m an Laternen, nicht auf Brücken/in Tunneln, nur wenn der
  Gehweg breit genug ist) und in Parks (innerer Ring, Kiosk nur im großen Park), LOD 450 m. `Environment/NoiseBarrierView`:
  Lärmschutzwände als 16-m-Kacheln. BusStop: wie bisher über `OwnModelCatalog.Transport` („bus_stop“).
  Noch ohne Verwendung: Coast/Beach/BeachChair/LifeguardTower/Lighthouse/Breakwater/Pier/FishingBoat/Tugboat, Transport/TaxiDepot/MetroEntrance,
  Rail/Tram (vorgemerkt), Vehicles/Taxi, (Coast/CargoShip/Ferry/Sailboat/Quay/HarborCrane/ContainerCrane/HarborWarehouse/Buoy_* und
  Infrastructure/Drawbridge: s. „Wasserstraße & Hafen“, Ids ship_*, harbor_*, buoy_*, drawbridge; Airport/*: s. „Flughafen (Stufe 4)“, Ids air_*),
  Terrain/Cliff_Corner/Cliff_Waterfall/Hill/Pond/River_* (Begründung im Bericht). Energy/*, Vehicles/*, Terrain/Cliff_Straight+Rocks:
  s. „Energie erweitert & Dienstfahrzeuge“. Transport/TrainStation + Rail/Locomotive/PassengerCoach/ContainerWagon/TankWagon/
  LevelCrossing/Track_Straight: s. „Bahn (Stufe 2)“ (Ids train_station, rail_*; `Track_Straight` zugeordnet, Gleise zeichnet bisher das Mesh).
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
- `TrafficSystem` (`ISubstepSystem`: 1 Verkehrsschritt = 0,25 s Fahrzeit, 180 je Kalender-Tick, s. „Zeitmodell“): `LaneGraph` (Spuren + Abbiegebeziehungen,
  links abbiegen von linken Spuren …), `Pathfinder` (A* über gerichtete Segmente), IDM-Fahrzeugfolge, Spurwechsel,
  `JunctionController` (Auto-Vorfahrt, rechts vor links, Schilder, Ampelphasen), `TrafficRules` (Spieler-Regeln).
- Fahrten: Bürgerfahrten aus Gebäuden (s. Bürger G2) und Durchgangsverkehr zwischen **Außenanschlüssen**
  (Straßenenden am Kartenrand, `ExternalConnections`). Sackgassen in der Stadt erzeugen keinen Verkehr mehr – außer mit
  `DeadEndsAreExternal` (Testszenarien). Ziele mitten auf Segmenten (`FindPathToSegment`) wandern beim Zerteilen mit.
  Hängende Fahrzeuge verschwinden nach 90 s. Hook `ThroughTrafficWeight` (Gateways): Gewicht je Start/Ziel statt Gleichverteilung (null = wie bisher).
  **Fahrt-Id-Spannen** (`Traffic/TripIdRanges`): Bürger > 0, Lkw des Warensystems ab −1 abwärts, Dienstfahrzeuge −1,5 … −1,9 Mrd.
  (`IsService`, Statistik `ServiceVehicles`); neue Fahrzeugarten bekommen dort eine eigene Spanne.

## Außenanbindungen (Gateways)
Wunsch des Spielers: Fernstraße, Bahn, Wasserstraße sind wie in Cities: Skylines **schon da**; man muss die Stadt an die Autobahn
anschließen, damit Leute kommen und gehandelt wird. (Echte Städte anderer Spieler als Ziel: bewusst später.)
- `Simulation/Gateways` (Namespace `CityBuilder.Simulation.Gateways`): `Gateway` (Art `GatewayKind` Highway/Road/Rail/Water/Air,
  `GatewayStatus` Operational/Planned, Lage am Kartenrand + Richtung nach außen, **Ziel** `TargetKind/TargetName/TargetCountryId/
  TargetRegionId` + `Importance`, `Capacity`, Einfahrt/Ausfahrt `EntryNodeId`/`ExitNodeId`, `PartnerId` = anderes Ende, Wasser:
  Breite/Tiefe/`Navigable`, Zähler `Today`/`Yesterday`/`TotalMoveIns`), `GatewayRules` (alle Zahlen), `GatewayGenerator`,
  `GatewaySystem` (`world.Gateways`, läuft zuletzt, ISaveable „gateways“, + `.Trade.cs`, `.Save.cs`).
- **Schalter** in `WorldSettings` (Standard aus → Tests/Standardwelt wie bisher; `SimulationRunner` schaltet alle ein):
  `PrebuiltConnections` (Kartenstart baut die Anbindung), `RequireGatewayForMoveIn`, `RequireGatewayForTrade`. Weltabschnitt des
  Spielstands v4 (v1–v3 = alles aus; Platzhalterwelt des Erststarts ohne Gateways). `CreateDefault(settings, seed, newGame)`: erzeugt nur bei `newGame` (Laden: false – Straßen und
  Gateways kommen aus dem Stand, nichts wird doppelt gebaut).
- **Ziele** (`GatewayGenerator.Targets`): Nachbarregionen der Startregion (fremdes Land +1,5, Hauptstadt-Region +1,5, Bevölkerung),
  die 6 wichtigsten fremden Hauptstädte (Bevölkerung × Nähe), die eigene Hauptstadt; Peilung Ost/Nord aus Länge/Breite. Ohne Welt:
  vier „Umland“-Richtungen. Freie Nachbarregion (kein Land) → Ziel „{Region} (freies Land)“ (Art Umland); Spieler = `PlayerCountryId` (auch Stadtstaat). Gateway-Ziel = bestes Ziel in seiner Richtung (Gewicht × cos, ab cos 0,35), Wichtigkeit = Σ w·cos².
- **Autobahn** (`BuildHighway`, deterministisch): Achse = Winkel (15°-Schritte) mit dem besten Bezug zu den Zielen; Anschlussstelle
  (Vorlage Raute, 400 m) bevorzugt ±300 m seitlich der Mitte (Mitte bleibt für die Stadt), sonst weitere Lagen; Richtungsfahrbahnen
  `Highway2` von den Vorlagen-Enden bis 6 m vor den Kartenrand in Stücken à ~260 m (≤ 900 m, Grenzen ≥ 150 m vom Wasser), Höhen der
  Stückgrenzen auf einer geglätteten, auf 4,5 % begrenzten Gradiente, je Stück automatische Trassierung (Tunnel/Brücke/Damm, Brücke
  über den Fluss). Alles wird erst komplett geplant (Kandidat verworfen bei ungültigem Stück, Ende im Meer, zu wenig lichter Höhe der
  Rauten-Brücke am Hang), dann mit abgehängter Stadtkasse gebaut (**Kosten trägt die Welt**). Autobahn, Rampen und Brücke bekommen
  `RoadSegmentFlags.Locked`; die beiden äußeren Querstraßen-Stücke der Raute sind der **Zubringer** (Stadtstraße, frei, dort schließt
  der Spieler an; `FeederEnds`). Name „A {Nr} Richtung {Ziel}“ (Nummer aus Hash von Region/Geländeseed).
- **Gesperrte Straßen** (`RoadSegment.IsLocked`): `RoadBuilder.Demolish` → false, `CheckReplace` → `RoadBuildError.Locked`,
  `RoadBuilder.Reverse` → null, `LockedReason(seg)` für den Hinweis; Kreisverkehr entfernt sie nicht; kein Unterhalt
  (`EconomySystem`); Flag bleibt beim Zerteilen (Rampen an die Autobahn anschließen geht weiter).
- **Bahn**: gerader Korridor quer durch die Karte (≥ 45° zur Autobahn, ≥ 480 m von der Anschlussstelle, Enden auf Land,
  ≤ 25 % Wasser), halbe Breite 16 m, zwei Rail-Gateways. Freigehalten über den verketteten `CitySystem.LotBlocker` (`InRailCorridor`).
  **Seit Stufe 2 liegt darauf die zweigleisige Fernbahn-Hauptstrecke** (s. „Bahn (Stufe 2)“); die Rail-Gateways sind „noch nicht
  befahrbar (Bahnhof an der Hauptstrecke fehlt)“ (`Gateway.RailBuilt`), bis ein Bahnhof in Betrieb ist → `Operational`.
- **Wasser** (seit Stufe 3 befahren, s. „Wasserstraße & Hafen“): Wasserläufe an jeder Kartenkante (Fluss: breitester Lauf je Kante, ≥ 12 m; Meer: Lauf ≥ 300 m, der längste)
  → Water-Gateways mit Breite, Tiefe, schiffbar ab 40 m/2,5 m (Meer immer), Name „Fluss stromab-/stromaufwärts Richtung …“ bzw.
  „Seeweg · Küste“. Befahrbar (`Operational`), sobald ein Hafen in Betrieb ist, dessen Fahrrinne frei zu diesem Gateway führt
  (`Gateway.ShippingNote` nennt sonst den Grund: „Hafen fehlt · Durchgangsschiffe fahren“, „Fahrrinne gesperrt (Brücke zu niedrig)“).
  **Luft**: ein Gateway, zunächst ohne Lage („Flughafen folgt“); seit Stufe 4 mit Lage am Terminal und befahrbar, sobald ein Flughafen in Betrieb ist (s. „Flughafen (Stufe 4)“).
- **Zuzug** (Hook `CitizenSystem.MoveInGateway`, nur mit `RequireGatewayForMoveIn`): `MoveInEntry(home, Netzteil)` = Einfahrt eines
  befahrbaren Straßen-Gateways im Netzteil (mehrere: wer gemessen an der Wichtigkeit am wenigsten Zuzügler brachte); gibt es gar
  keine Gateways, zählt jeder befahrbare Außenanschluss (Testwelten). Sonst null → kein Zuzug (`BlockedMoveInsToday`). Zuzug startet
  die erste Fahrt (`StartMoveInTrip`: ein Erwachsener von `Place.Outside(Einfahrt)` heim, Rest ist schon da); `MovedInVia` zählt je
  Gateway. `ConnectionHint` („Stadt an die A 7 (Zubringer an der Anschlussstelle) anschließen, damit Leute kommen.“) solange ein
  Wohngebäude ohne Anschluss ist (Stundenprüfung). **Bahn:** ein Personenbahnhof in Betrieb im Netzteil zählt als Anschluss
  (`RailEntryFor` → Zugangsknoten des Bahnhofs, abwechselnd nach Wichtigkeit mit der Autobahn; `RailGatewayOfNode` ordnet
  Bahnhofsknoten dem Rail-Gateway zu, `OfNode` fällt darauf zurück).
- **Handel** (Hook `GoodsSystem.PreferredGateway`, `RequireGatewayForTrade`): `TradeNode(gut, import, vertrag, netzteil)` – Vertrag →
  Partner des laufenden Vertrags (`ContractPartner`), Weltmarkt → günstigstes Lieferland (Preis + Fracht) bzw. bester Abnehmer
  (`SpotPartner`, täglich); `GatewayToward(land)`: Landweg → Gateway Richtung Handelsort des Landes, Seeweg → Gateway Richtung Hafen
  des Spielerlandes (`TradeRouteText`: „Seeweg: Lkw über … zum Hafen außerhalb (bis Wasserstraße/Hafen fertig)“; **mit eigenem Hafen in
  Betrieb** „Seeweg über Hafen … (Fracht x %)“ über den Hook `HarborRouteText`, die Firmen laden am Kai, s. „Wasserstraße & Hafen“). Import = Einfahrt,
  Export = Ausfahrt; Lkw fahren über die Autobahn. Ohne Welt/Treffer: nächster Außenanschluss wie bisher.
- **Durchgangsverkehr** (Hook `TrafficSystem.ThroughTrafficWeight`, mit `PrebuiltConnections`): Autobahn-Einfahrt Gewicht
  1 + 0,4 × Wichtigkeit (≤ 5; Spielerstraßen am Rand 1), Ausfahrten sind kein Start, keine Wende am selben Gateway – läuft auch
  ohne angeschlossene Stadt.
- **Zählung**: Fahrzeuge rein/raus je Straßen-Gateway (Fahrzeug-Ids auf dem ersten/letzten Stück, alle 8 Verkehrsschritte = 2 s Verkehrszeit als `ISubstepSystem`, gespeichert),
  Zuzüge, Import/Export-Ladungen + Wert (neue `Shipment`s mit `ConnectionNodeId`).
- **Darstellung**: `Game/Gateways/GatewayView` (Begleiter am RoadTool): Schilder „A 7 · Richtung …“ (90 m innerhalb des Rands,
  `WorldLabels`), Bahntrasse als gestrichelte Doppellinie mit „Bahntrasse · Züge folgen“ (nur ohne Hauptstrecke; mit Gleisen
  „Fernbahn · Bahnhof an die Gleise setzen“, bis ein Bahnhof in Betrieb ist), Wasserwege beschriftet, „Zubringer · hier
  anschließen“ + Meldung (Schlüssel „gateway-connect“) solange `ConnectionHint`; HUD-Warnchip „Nicht angeschlossen“ (Klick →
  `CameraController.FocusOn` Zubringer). Inspektor: Gateways anklickbar (`SelectionKind.Gateway`, Art, Ziel inkl. Handelsweg, Zustand,
  Verkehr/Zuzug/Handel heute/gestern), Liste in der Stadtübersicht, Lkw/Orte außerhalb zeigen den Gateway. Weltkarte: Strahlen je
  Anschluss ab der Startregion (rot Autobahn, braun Bahn, hellblau Wasser) + Liste beim Spielerland. RoadTool: gesperrte Segmente
  zeigen den Hinweis statt Abriss/Umdrehen/Ausbau. Hilfe (F1) Abschnitt „Start“. Im Editor noch nicht visuell abgenommen.
- Tests: `GatewayTests` (Autobahn durchgehend in beide Richtungen, Raute + Zubringer, Brücke über den Fluss, gesperrt/Zubringer
  abreißbar, Unterhalt, Zuzug erst nach Anschluss + Zuzügler-Auto auf der Einfahrt, Durchgangsverkehr ohne Anschluss/ohne Wende,
  Bahnkorridor frei, Wasser-Gateways, Import über den Gateway Richtung Partnerland + Vertrag, kein Umland, Speichern/Laden ohne
  Neubau + bitgleich, Erzeugung deterministisch; volle Erde Seeds 2024/74/333/592).
- Später: (~~Stufe 2 Bahn~~ ✅ s. „Bahn (Stufe 2)“), (~~Stufe 3 Wasserstraße/Hafen~~ ✅ s. „Wasserstraße & Hafen (Stufe 3)“), (~~Stufe 4 Flughafen~~ ✅ s. „Flughafen (Stufe 4)“); weitere Landstraßen-Gateways (zweite Fernstraße, Spielerstraßen am Rand als
  Gateway führen), Bahnkorridor auch fürs Gelände sperren/Bäume roden, Gateways nach Wichtigkeit „wachsen“ (Ausbau auf 3 Spuren),
  andere echte Städte als Ziel.

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
  Depots nicht ab** – Depots hängen an der Bauwerks-Sperre (`RoadNetwork.RegisterStructures`, Art „depot“; Plan ungültig);
  `BlocksRoad(segment)` / `BlocksPoint(p)` bleiben als Abfragen. Verschieben: `PlanMoveDepot`/`MoveDepot` (10 %).
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
  `TransitRealShare` (seit dem Zeitmodell 100 %, vorher 35 %) × Demand fahren als einzelne Fahrgäste (gehen zur Haltestelle,
  warten, steigen ein – Kapazität 60 –, fahren, steigen aus, Rest zu Fuß), der Rest statistisch (Fahrplanzeit in Ticks, zählt als
  Fahrgast und zahlt, belegt keinen Platz). Früher Notlösung gegen die Zeitverzerrung (ein Spieltag = 60 s Verkehrszeit), jetzt
  3 h Verkehrszeit je Tag → nur noch Stellschraube. Wartende werden jeden Tick geprüft, Busse auch zwischen den Ticks eingesetzt
  (`TransitSystem.Substep`). Volle Busse lassen stehen
  (nach 2 vollen gibt man auf), Geduld max(150 s, 2,5 × Takt); Ergebnis → Erinnerungen `BusFull`/`BusLate`/`BusRide` →
  Mobilitäts-Bedürfnis (−0,15/−0,1/+0,1) und nächste Wahl. Sicherheitsnetz 1 Tag (vorher 5). Takt/Umlauf im Fenster in Spielminuten.
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
- Leistung (Testprojekt, Info): 10 Linien, 40 Busse, ~10 500 Bürger ≈ 4 ms/Tick für alle Systeme (vor dem Zeitmodell; seitdem
  ≈ 11,6 ms je Kalender-Tick inkl. 180 Verkehrsschritten).
- Später: Busspuren bevorzugen/Buchten, Tram/U-/S-Bahn, Taxi, Fahrpreis/Abo einstellbar, Linien bearbeiten im Werkzeug
  (Halt einfügen per Klick), Fahrplan mit Abfahrtszeiten statt Verteilung, Depotzuordnung je Linie (sichtbare Wartende: ✅ über `TransitPedestrians`, s. „Fußgänger“).

## Bahn (Stufe 2)
- `Simulation/Rail` (Namespace `CityBuilder.Simulation.Rail`): `RailTypes.cs` (`RailRules` = alle Zahlen, `StationCatalog`,
  `TrainCatalog`, `RailCars`, `Station`/`Platform`, `Train`, `LevelCrossing`, `RailPassenger`, `RailOffer`), `RailSystem` (`world.Rail`,
  `ISimulationSystem` + `ISubstepSystem` + `IUpkeepSource` „Bahn“, partial: `.Tracks` Gleise/Prüfungen/Fahrweg-Graph, `.Mainline`,
  `.Stations`, `.Trains` Fahrplan/Blöcke/Fahren, `.Crossings`, `.Passengers`, `.Freight`, `.Save`). Erzeugt in `CreateDefault` nach den
  Gateways (`Connect()` verdrahtet alle Hooks, auch beim Laden), `BuildMainline()` direkt nach `Gateways.Generate()` (nur neue Welt).
- **Entscheidung Gleisnetz:** eigenes **zweites `RoadNetwork`** (`Rail.Tracks`) mit Gleistypen (`RoadCategory.Rail`, `RoadCatalog.RailMain`
  160 km/h / `RailTrack` 120 km/h, nicht in `RoadCatalog.All`) statt Gleise im Straßennetz: Bézier, Knoten/Zerteilen, Höhen, Profile/
  automatische Trassierung (Tunnel/Brücke), `RoadBuilder.Plan/Build/Demolish`, Budget-Hook und der Spielstand-Code werden 1:1
  wiederverwendet, aber kein Straßenverbraucher (Spurgraph, Wegfindung, Gehwege, Zonenraster, Netzteile, Dienst-Reichweiten,
  Bodenwert, Masten …) muss Gleise herausfiltern – und `TrafficSystem`/`VehicleView` (parallel bei agent/models2) bleiben fast
  unberührt. Kleine, markierte Änderungen im Straßencode: `RoadType.MaxSlope` für Rail **3,5 %** (Stadt 10 %, Autobahn 6 %),
  `RoadType.MinJunctionAngle` (Gleise 0°, Weiche zweigt flach ab), `RoadBuilder.InSwitchZone` (45 m Weichenbereich ist keine Kreuzung),
  Hook `RoadNetwork.ExternalCheck` am Ende von `Validate`, neue Fehler `RailCrossing`, `OverlapsRail`, `TrackGeometry`, `TrackOccupied`.
- **Gleisregeln** (`CheckTrackPlan`): Bogenhalbmesser ≥ `MinRadius` 150 m (`MinRadius(p0, c, p2)` aus der Bézier-Krümmung), Anschluss
  **tangential**: am Gleisende Fortsetzung, mitten auf dem Gleis Weiche ≤ `MaxSwitchAngle` 12°, höchstens 3 Gleise je Knoten,
  ebenerdige Gleiskreuzung ≥ 25°, belegte/reservierte Gleise werden weder zerteilt noch angeschlossen noch abgerissen. Fahrweg-Graph:
  Übergang an einem Knoten nur bei Ablenkung ≤ `MaxDeflection` 20° (Weiche/Kreuzung von selbst richtig). Zonen-Gebäude unter neuen
  Gleisen werden abgerissen, Bauwerke (Dienste, Versorger, Depots, gesetzte Häuser, Bahnhofsgebäude) sperren. Gelände: `ConformTo` wie
  Straßen, verkettete `RoadRule`/`LockQuery`/`LotBlocker`; Kasse „Bau Bahn“ (`RailBudget` biegt die RoadBuilder-Buchung um),
  Unterhalt 0,9 €/m/Woche, Abriss 25 %; Lärm (Gleistyp × 0,75 „Schienenbonus“, Tunnel still) über `NoiseSystem` → `StampNoise`.
- **Straße ↔ Gleis** (eine Regel `CrossCheck` für beide Richtungen: Straßenplan gegen Gleise, Gleisplan gegen Straßen): gleiche Ebene
  (±1,5 m) = **Bahnübergang** nur mit Stadtstraße, Winkel ≥ 35°, ≥ 20 m von Straßenknoten (Planenden + Straßenkreuzungen) und ≥ 25 m
  von Weichen, nicht an Bahnsteigen; sonst **lichte Höhe** Gleis über Straße 5 m, Straße über Gleis 6,5 m (Oberleitung), dazwischen
  `InsufficientClearance`; längs übereinander `OverlapsRail`. Autobahn/Rampe ebenerdig → `RailCrossing`.
- **Hauptstrecke** (`BuildMainline`): zwei Gleise ±2,25 m um die Korridorachse (Rechtsverkehr: Gleis 1 A→B, Gleis 2 B→A), Gradiente =
  geglättetes Gelände (160 m) mit 2 % (`MainlineGrade`), Boden über Wasser (Brücke), über jeder Straße, die den Korridor kreuzt
  (Autobahn), Gleis ≥ Straße + 5 m + 1,2 m Tragwerk – steigungsgerecht ausgebreitet; Stücke ~260 m = Blockabschnitte (nicht über
  Wasser/Kreuzungen geschnitten), eigenes Höhenprofil je Stück (`AddSegment(..., profile)`, Böschung bis 6 m). Gesperrt (`Locked`),
  Kosten/Unterhalt trägt die Welt. Test (flach, Gelände, Weltseeds 99/7): Brücke 6,0–18,5 m über der Autobahn.
- **Bahnhöfe** (`PlanStation(type, cursor)` → `BuildStation`, `DemolishStation` 25 %): rastet am nächsten ebenerdigen Gleis ≤ 40 m ein –
  Rückseite parallel, 6 m hinter der Gleismitte (Bahnsteig dazwischen), Vorderseite zeigt vom Gleis weg zur Straße. **Personenbahnhof**
  44×20 m, Bahnsteig 200 m, 120 000 € / 2 400 €/Woche; **Güterbahnhof** 60×30 m, Ladegleis 260 m, 150 000 € / 3 000 €/Woche. Bahnsteige
  (`Platform`, abgeleitet): Gleise mit Mitte 3–16 m hinter der Rückseite, parallel (≤ 10°), mittig, über ≥ 60 % der Länge, max. 3 (an der
  Hauptstrecke: beide Gleise). **In Betrieb** = Stadtstraße vor der Vorderseite (`RoadFront.FindAccess` ≤ 8 m; bauen geht auch ohne –
  dann außer Betrieb mit Hinweis) **und** ein Bahnsteiggleis auf einem Fahrweg der Hauptstrecke (`MainlineEdges`: vorwärts von den
  Einfahrten und rückwärts von den Ausfahrten erreichbar). Sperren: Straßen weder durch Gebäude noch Bahnsteigfläche (Bauwerks-Art
  „station“), Gleise nur nicht durchs Gebäude; Grundstücke/Terraforming/Versorger/Dienste gesperrt (verkettete Hooks).
  `AccessNodeId` = Straßenknoten an der Zufahrt = **Ankunftsort** für Zuzug/Pendler per Bahn. Namen „{Stadt} Hbf“, weitere mit Himmelsrichtung.
- **Züge** (`TrainCatalog`): **Fernzug** Lok + 6 Reisezugwagen (≈ 180 m, 160 km/h, 0,45/0,7 m/s², 432 Plätze), **Güterzug** Lok + 10 Wagen
  (6 Container-, 4 Kesselwagen, ≈ 209 m, 100 km/h, 0,25/0,45 m/s²). **Fahrplan** (Kalender-Ticks): Fernzug je Richtung stündlich 6–22 Uhr (A zur
  vollen, B zur halben Stunde), Güterzug alle 2 h rund um die Uhr. Fahrweg (`PlanRouteFrom`, Dijkstra über Fahrzeit, deterministisch)
  Einfahrt → Ausfahrt, über das Bahnsteiggleis eines passenden Bahnhofs mit dem kürzesten Umweg (Überholgleis/Bahnhofsschleife wird
  genommen); weitere Bahnhöfe auf dem Fahrweg bekommen auch einen Halt (Zugmitte an der Bahnsteigmitte). **Blocksystem:** Gleisstück =
  Blockabschnitt mit Signal am Anfang, Knoten = Fahrstraßenelement; ein Zug fährt nur in Abschnitte, die er für sich reserviert hat
  (Gleisstück frei + Einfahrknoten frei), reserviert voraus bis Bremsweg + 200 m und gibt hinter dem Zugende frei → Folgezüge halten am
  Signal (10 m davor), Gegenzüge auf eingleisigen Stücken warten; warten zwei Züge > 240 s aufeinander, fällt der später eingesetzte aus.
  Fahrt nach Bremskurve (v ≤ √(2·b·Abstand) bis Signal/Halteplatz, Bremskurven vor langsameren Abschnitten), Haltezeit 45 s + 0,3 s je
  Ein-/Aussteiger bzw. 240 s Umschlag. Einfahrt am Kartenrand, sobald der erste Block frei ist (sonst ≤ 900 s warten, dann Ausfall),
  Ausfahrt über den Rand hinaus. Umbau vor einem fahrenden Zug: Fahrweg ab dem letzten reservierten Stück neu (`Replan`).
- **Bahnübergänge** (`LevelCrossing`, abgeleitet aus beiden Netzen, Zustand gespeichert): Schranke schließt, wenn ein Zug in 28 s + 40 m da
  wäre oder den Übergang belegt, öffnet 3 s nach dem Zugende. Verkehr: Hook `TrafficSystem.BarrierAhead` (`TrafficSystem.Rail.cs`, eine
  markierte Zeile in `StepOnLane`) = Haltelinie 3,5 m vor dem Gleisrand (schräg verlängert); Wartezeit vor der Schranke zählt nicht bis zum
  90-s-Abbruch. Test: kein Auto auf dem Gleis, solange ein Zug drüberfährt.
- **Fahrgäste** (Verkehrsmittelwahl bleibt im Bürgersystem, „Bus-Logik erweitert“, `CitizenSystem.Rail.cs`): Fahrten mit einem Ende
  außerhalb (Pendler, Heim-/Arbeitswege, Zuzügler) bekommen ein Angebot `RailOffer` vom nächsten Personenbahnhof in Betrieb ≤ 2,2 km
  (Zugang zu Fuß/Rad, Wartezeit bis zum nächsten Fernzug + 30 s, Fahrt bis zum Kartenrand, außerhalb 0,6 × Autozeit) →
  `RailProbability` = logistisch((t_Auto − t_Bahn,gefühlt)/600 s + gesellig/naturverbunden/sparsam + Stau-Erinnerung − „Zug verspätet“ +
  „gute Zugfahrt“). Echte Fahrgäste: abfahrend gehen sie zum Bahnhof, warten (≤ 1 100 s) und steigen in den nächsten haltenden Fernzug
  (Plätze), fahren mit ihm aus der Karte; ankommend warten sie „draußen“ (≤ 1 400 s) und werden vom nächsten einfahrenden Fernzug mit Halt
  an ihrem Bahnhof mitgenommen, steigen aus und gehen zum Ziel. Ergebnis → Erinnerungen `TrainRide`/`TrainLate` (neue `MemoryKind`s, je 4
  Gedanken). Ohne Bahnhof wird kein Zufall gezogen (alles wie vorher). **Zuzug per Bahn:** `MoveInEntry` liefert den Ankunftsknoten des
  Bahnhofs → Zuzügler fahren immer Zug (Ort „außerhalb“ = Bahnhofsknoten); **Pendler ohne Straßenanschluss** (`NearestExternal` fällt auf
  den Bahnhof zurück) ebenso. Rail-Gateways zählen Zuzug (`TotalMoveIns`).
- **Güter** (Hook-Kette um `GoodsSystem.PreferredGateway` + neu `GoodsSystem.FreightShareFor`): Güterbahnhof in Betrieb im Netzteil der
  Firma → Import/Export über seinen Zugangsknoten (Lkw nur Firma ↔ Güterbahnhof), Fernfracht **4,5 %** statt 8 % des Warenwerts;
  Kapazität 480 Ladungen/Tag (24 Güterzughalte × 10 Wagen × 2), darüber wieder per Lkw über die Autobahn. Ladungen werden gezählt
  (`LoadsToday`, `PendingLoads` → Umschlag beim nächsten Güterzughalt).
- **Kasse:** neue Kategorien (hinten angefügt) `RailConstruction` „Bau Bahn“ (Gleise, Bahnhöfe) und `RailFares` „Bahn: Entgelte &
  Fahrgeld“ (Einnahme: 60 € je Fernzughalt, 90 € je Güterzughalt, 2,50 € je Ein-/Aussteiger, 6 € je umgeschlagener Ladung, gebucht alle 5
  Ticks); Unterhalt Bahnhöfe + Spielergleise als `IUpkeepSource` „Bahn“ (Kategorie Dienste). Fernzüge/Hauptstrecke zahlt die Welt.
- **Spielstand** „rail“ v1 (automatisch hinten in der `SaveRegistry`, `IAfterLoad`): Gleisnetz (`RoadNetwork.Write/Read`), Hauptstrecken-
  Enden, Bahnhöfe + Statistik, Züge vollständig (Fahrweg inkl. Längen, Spitze, Tempo, Reservierungen, Halte, Fahrgäste), Fahrgäste
  (wartend/draußen/im Zug), Schrankenzustand (über die Lage), Zähler. Abgeleitet: Fahrweg-Graph, Bahnsteige/Zufahrt, Bahnübergänge,
  Gateway-Status. Test: Speichern mitten in der Zugfahrt (Bahnübergang, Halt) → beide Welten 2 500 Verkehrsschritte bitgleich.
- **Unity** (`Game/Rail`, Begleiter am RoadTool): `RailView` (Gleis-Mesh je Stück: Schotter, Schwellen, Schienen, Fahrdraht + Masten,
  Brücken mit Tragwerk/Pfeilern; Blocksignale rot/grün per Instancing, `ShowSignals`; Bahnhof = Modell „train_station“
  (`Models/Transport/TrainStation`) bzw. Platzhalter, Bahnsteige + Dach, Güterbahnhof-Platzhalter mit Halle/Containern/Kran;
  Bahnübergang = Modell „rail_level_crossing“ (`Models/Rail/LevelCrossing`, Schranken `BarrierA/B` drehen um die Gleisachse) bzw.
  Platzhalter; Züge aus `Models/Rail` – Lok, Reisezugwagen, Container-/Kesselwagen, an zwei Drehgestellpunkten ausgerichtet, zwischen
  zwei Verkehrsschritten interpoliert, sonst Quader; `PickTrain`/`TryGetTrainPosition` für den Inspektor). `RailTool` (**Taste 7** – 7 war
  frei; Modus `RoadTool.Mode.Rail`): Gleis (Start/Ende, Kette; ab einem Gleis automatisch tangential = Weiche), Gleisbogen (3 Klicks),
  Personen-/Güterbahnhof (Geist am Gleis eingerastet, Hinweis zu Bahnsteigen/Straßenfront), Abriss (Gleis/Bahnhof); Bild↑/↓ Bauhöhe
  (0 = automatische Trassierung). Bauleiste: eigene Kategorie **„Bahn“** (`GameHud.Rail.cs`, Icon `HudIconKind.Train`, Abriss-Kachel
  „Gleis/Bahnhof“). `RailWindow` (rechts, **F7** bzw. im Werkzeug): Hauptstrecke/Rail-Gateways, Fahrplan, Bahnhöfe, Züge, Bahnübergänge.
  Inspektor: `SelectionKind.Train` (anklickbar, Folgen, Halte, Fahrgäste aus der Stadt) und `SelectionKind.Station` (Betrieb, Gleise
  belegt/frei, Statistik, Entgelte), Bürger „im Zug/wartet am Bahnsteig“. `GatewayView`: gestrichelte Trasse nur ohne Hauptstrecke.
  Modellzuordnung über `Name.json` (role transport) + `OwnModelDescriptions.TransportFiles`; `Rail/Tram` bleibt vorgemerkt.
  **Optik ungeprüft** (Unity nicht gestartet): Gleis-Mesh/Normalen, Ausrichtung der Rail-Modelle und Schrankendrehsinn, Bahnsteighöhen,
  Signale, Fahrdrahthöhe, Platzhalter.
- **Tests** `RailTests` (19): Hauptstrecke zweigleisig/gesperrt/2 %/Brücke über Autobahn und Wasser (flach + 3 Gelände-Seeds), Spielwelten
  2024/74/333 (Bahnhofsplatz vorhanden, ½ Tag Fahrplan ohne Ausfall), Gleisregeln (Radius, 3,5 % statt 10 %, Weiche flach ja/30° nein,
  Sperre, Kosten), Straße ↔ Gleis (Bahnübergang, Autobahn nein, 3 m nein/7 m ja, 20° nein, längs nein, zu nah an Kreuzung, Gleis über
  vorhandene Straße), Bahnhof (Einrasten, 2 Bahnsteige, Straßenfront → Betrieb → Gateway befahrbar, Sperre, Abriss), Fernzug hält/Güterzug
  fährt durch, Blocksignale (3 Züge, nie Abstand < 0, Signalhalt), Überholgleis mit Bahnhof (Fernzug nimmt es, wartender Zug plant neu),
  Bahnübergang (Schranke zu, Autos warten, keins auf dem Gleis), Zuzug + Auspendler nur per Bahn, Güterbahnhof (Hook, Import per Bahn
  billiger, Umschlag), Speichern/Laden bitgleich, ohne Gateways keine Bahn.
- **Nächste Teilstufe (offen):** eigene Regional-/S-Bahn-Linien des Spielers (Zugdepot, Linien zwischen eigenen Bahnhöfen, Takt, Fahrgeld
  wie Bus, Umstieg Bus ↔ Bahn in `PlanRide`), Wenden in Kopfbahnhöfen (heute nur Durchgangsfahrwege), Güterbahnhof an einem Stumpfgleis,
  echte Ladungen im Güterzug (Wagen je Gut statt Zähler, Lieferzeit per Zug), Signale als eigene Objekte (Vorsignal, Blockteilung
  langer Stücke, Fahrstraßen über ganze eingleisige Abschnitte statt Ausfall bei Gegenseitigkeit), Rückstau über den Bahnübergang
  verhindern (heute dürfen Autos auf dem Übergang stehen bleiben, der Zug „fährt durch“), Fußgänger an Schranken, Bahnhof verschieben (Y),
  Bahnhof als Ziel im Bus-Netz (Zubringerlinien), Schlagzeilen (neuer Bahnhof, Verspätungen), Tram (`Models/Rail/Tram`, `RoadCatalog.CityTram`),
  Lärm nach echten Zugzahlen je Gleis, Bodenwert-Bonus Bahnhofsnähe, Oberleitung/Masten als Modell (`Track_Straight` liegt bereit).

## Wasserstraße & Hafen (Stufe 3)
Vision des Spielers: „Schifffahrt: schiffbare Gewässer mit Durchfahrtshöhe; feste Brücken hoch genug oder Klapp-/Zugbrücken (niedrig,
öffnen für Schiffe → Rot/Stau, Umwege) oder Tunnel.“
- `Simulation/Shipping` (Namespace `CityBuilder.Simulation.Shipping`): `ShippingTypes.cs` (`ShippingRules` = alle Zahlen, `ShipClass`/
  `ShipCatalog`, `HarborType`/`HarborCatalog`, `Harbor`, `Waterway` + `WaterwayCrossing`, `Drawbridge`, `Ship`, `BridgeCheck`,
  `DrawbridgePlan`), `NavGrid` (Navigationsraster), `ShippingSystem` (`world.Shipping`, `ISimulationSystem` + `ISubstepSystem` +
  `IUpkeepSource` „Hafen & Klappbrücken“, partial: `.Waterways`, `.Drawbridges`, `.Harbors`, `.Ships`, `.Trade`, `.Save`). Erzeugt in
  `CreateDefault` **nach der Bahn und vor `Gateways.Generate()`** (Fahrrinnen stehen fest, bevor die Autobahn trassiert wird). Ohne
  `PrebuiltConnections` oder ohne Wasser ist alles aus (`Active` false, Tests/Standardwelt unverändert).
- **Fahrrinne** (abgeleitet, nicht gespeichert): `NavGrid` (8-m-Zellen) aus dem **natürlichen** Gelände beim Erzeugen der Welt – Tiefe
  (flachster Punkt der Zelle) und Chamfer-Abstand zum Ufer; beim Laden entsteht dasselbe Raster. Durchgangsrinnen = Dijkstra (eigener
  Heap, deterministisch, 8 Nachbarn, Tiefe ≥ Binnenschiff-Tiefgang + 0,5 m, ≥ 10 m vom Ufer, Ufernähe < 40 m kostet bis 4×) zwischen allen
  schiffbaren Wasserläufen am Kartenrand (`GatewayGenerator.WaterAtEdges`: Fluss ↔ Fluss, Fluss ↔ Meer), geglättet (±4 Zellen), alle
  24 m neu abgetastet, 80 m über den Rand hinaus verlängert; Gateways werden über die Lage zugeordnet (`EnsureLinks`). Je Hafen eine Rinne
  vom Wasser-Gateway zum Anfahrpunkt vor dem Kai. Je Punkt Tiefe und Breite; halbe Kanalbreite = 35 % der Gewässerbreite (10–45 m).
  Bemessungshöhe: Binnenwasserstraße **7 m**, Seeweg **25 m** lichte Höhe. Standardgelände: Fluss 4 m tief, ~60 m Kernbreite, 2 300 m lang.
- **Schiffsklassen** (`ShipCatalog`, Tiefgang/Masthöhe/Tempo/Laderaum): Binnenschiff 85 × 9,5 m, 2,8 m / 6,5 m (Steuerhaus abgesenkt),
  14 km/h, 60 Ladungen (Platzhalter-Rumpf); Küstenfrachter (`Coast/CargoShip`, 80 × 13 m) 5,5 m / 24 m, 22 km/h, 160 Ladungen – nur Seeweg;
  Fahrgastschiff (`Coast/Ferry`) 1,6 m / 11 m; Segelyacht (`Coast/Sailboat`) 1,6 m / 13 m. Braucht Wasser ≥ Tiefgang + 0,5 m und unter
  festen Brücken ≥ Masthöhe + 0,5 m.
- **Brücken über die Rinne** (`EnsureCrossings`, bei jeder Straßen-/Gleisänderung): Schnitt jeder Straße und jedes Gleises mit dem
  Rinnen-Linienzug → `WaterwayCrossing` (Lage, Strecke, lichte Höhe = tiefster Fahrbahnpunkt über dem Kanal − Tragwerk 1,5 m Straße /
  1,2 m Gleis; Tunnel unter dem Grund zählen nicht). Fest + zu niedrig für eine Klasse → Rinne für sie **gesperrt** (`Waterway.Passable`,
  `FirstBlocking`). **Trassierung**: `TerrainMap` implementiert `Roads.INavigableGround` (Hook `NavigationClearance` = Raster der Rinnen
  mit Bemessungshöhe + 1,5 m); `Profiles.WaterFloor` hebt automatisch trassierte Straßen/Gleise, die vorgebaute Autobahn
  (`GatewayGenerator`) und die Hauptstrecke (`RailSystem.Mainline`) darüber (Standardwelt: Autobahnbrücke 13,4/14,2 m lichte Höhe).
  Mit fester Bauhöhe (Bild↑/↓) darf man niedriger bauen – **Hinweis im Werkzeug** (`CheckBridge`, RoadTool/RailTool-Zeile): „Zu niedrig für
  Schiffe: lichte Höhe 3,5 m < 7,0 m – höher bauen (Bild↑) oder Klappbrücke (Hafen-Werkzeug 8)“, bzw. „✓ · zu niedrig für Fahrgastschiff/
  Segelyacht – die fahren dann nicht durch“, „Tunnel unter der Fahrrinne – Schiffe fahren frei darüber“. **Tunnel unter Wasser** sind neu
  erlaubt (`RoadBuilder.Validate`: unter Wasser ok, wenn ≥ 7 m unter dem Gewässergrund). Gesperrte Rinne → `FairwayWarning` (HUD-Chip
  „Fahrrinne gesperrt“, Fenster), Durchgangsschiffe der Klasse kommen nicht (je Klasse gezählt, `BlockedToday/Yesterday`), Schiffe, die
  schon unterwegs sind, halten davor und kehren nach 120 s um; Hafen dahinter unerreichbar (`Harbor.BlockedReason`). Terraforming in der
  Rinne ist gesperrt (verkettete `LockQuery`).
- **Klappbrücke** (`RoadSegmentFlags.Drawbridge`, Bit 4, bleibt beim Zerteilen; `RoadSegment.IsDrawbridge`): `PlanDrawbridge(typ, a, b)` =
  gerade Stadtstraße quer über die Rinne (genau eine Querung, ≥ 50° steil, sonst `RoadBuildError.DrawbridgeNoFairway`/`AngleTooSharp`;
  Zufahrten ≥ 20 m) → drei Stücke: Zufahrt (geländefolgend), **Klappe** (flach, 4 m über Wasser bzw. so hoch, dass die Zufahrten mit
  85 % der Höchststeigung herunterkommen; Länge = 2 × min(Kanal, 28 m)/sin + 16 m, 36–90 m), Zufahrt; die Fahrrinne verlangt dabei keine
  Mindesthöhe (`suppressFloor`). `BuildDrawbridge` baut nacheinander (Einrasten an den gebauten Enden), Mechanik 180 000 € („Bau Hafen &
  Klappbrücken“) + Fahrbahn (Straßenbau), 900 €/Woche. `DemolishDrawbridge` reißt die Klappe ab (Zufahrten bleiben). Prüfung des Flags
  über den verketteten `RoadNetwork.ExternalCheck` (hinter der Bahn-Prüfung). `Drawbridge` (abgeleitet, Zustand über die Lage gespeichert):
  RoadOpen → (Schiff angemeldet ≤ 450 m und Straße ≥ 75 s frei) **Warning** 10 s (Ampel rot, Schranken, Klappe wird geräumt – wartet bis
  keine Fahrzeuge auf der Klappe, höchstens 45 s) → Raising 15 s → **Open** (bis kein Schiff mehr angemeldet ist bzw. nach 150 s, nie
  solange ein Schiff unter/kurz vor der Klappe ist) → Lowering 15 + 4 s → RoadOpen. Schiffe halten 30 m vor dem Kanal, bis die Klappe offen
  ist; Boote, die geschlossen durchpassen, lassen nicht öffnen. Verkehr: verketteter Hook `TrafficSystem.BarrierAhead` (hinter den
  Bahnübergängen) = Haltelinie 3 m vor dem Ende jeder Zufahrt, Spuren Richtung Klappe; **neu** in `TrafficSystem.Rail.cs`: auch die Schlange
  bis 160 m hinter der Haltelinie zählt als „wartet an der Schranke“ (kein Abbruch nach 90 s, gilt auch für Bahnübergänge). Statistik:
  Öffnungen heute/gestern/gesamt, Sperrzeit, angemeldete/wartende Schiffe.
- **Durchgangsschiffe** (Fahrplan in Spielstunden, je Durchgangsrinne mit beiden Gateways): Binnenschiff A→B zu geraden, B→A zu ungeraden
  Stunden (rund um die Uhr, 24/Tag); Fahrgastschiff April–Oktober 10–18 Uhr stündlich abwechselnd; Segelyacht Mai–September 9–18 Uhr
  (2 von 3 Stunden). Öffnen Klappbrücken **auch ohne Hafen**. Fahren (`StepShip`, Verkehrsschritt 0,25 s): Bug-Strecke `S` auf der gerichteten
  Fahrt, Beschleunigung je Klasse, Bremskurve v ≤ √(2 · 0,08 m/s² · Abstand) bis zum nächsten Halt (Klappbrücke zu, feste Brücke zu niedrig,
  Liegeplatz/Reede, Heck des Vordermanns + 60 m auf derselben Rinne und Richtung); Gegenverkehr weicht nach Steuerbord aus (bis 18 m,
  zu den Enden zur Mitte zurück). Einfahrt, sobald der erste Abschnitt frei ist (≤ 900 s, sonst Ausfall). Kollisionen zwischen
  verschiedenen Rinnen (Durchgang ↔ Hafenrinne) werden nicht geprüft.
- **Güterhafen** (`HarborCatalog.Cargo` 112 × 56 m, 2 Liegeplätze, 260 000 € / 4 200 €/Woche): `PlanHarbor` = Richtung zum Wasser aus dem
  Umkreis (90 m), Uferlinie, **Kaikante** so weit hinaus, bis vor dem Liegeplatz das Wasser tief genug ist – erst für Küstenfrachter (bis
  80 m, → Seehafen), sonst für Binnenschiffe (bis 60 m), solange die Vorderkante an Land bleibt –, Hafengelände dahinter (Vorderkante an Land, dazwischen **Aufschüttung**:
  `TerrainMap.FillArea` ändert nur das aktuelle Gelände, das natürliche bleibt für Rinne/Tiefen), Kaihöhe 2,5 m über Wasser, bis 20 m
  Höhenunterschied wird abgetragen; Sperren wie Bahnhöfe (Straßen/Gleise nicht durch, Grundstücke/Terraforming/Versorger/Dienste über die
  verketteten Hooks). Rinnen je Wasserlauf (Seehafen nur über See). **In Betrieb** = Stadtstraße vor der Landseite (`RoadFront.FindAccess`
  ≤ 8 m) **und** eine Rinne für die Hafenklasse frei → Wasser-Gateway `Operational`. Anläufe alle 3 h je Hafen (versetzt nach Id) über die
  freien Rinnen im Wechsel, zusätzlich, wenn mehr Fracht wartet als ein Schiff fasst; höchstens Liegeplätze + 1 Schiffe gleichzeitig. Ohne
  freien Liegeplatz wartet das Schiff auf Reede (160 m vor dem Anfahrpunkt). Am Kai: Umschlag = min(wartende Ladungen, Laderaum), Liegezeit
  900 s + 4 s je Ladung (≈ 2–3 Spielstunden), Hafengeld 220 € (Binnenschiff) / 600 € (Küstenfrachter) + 4 € je Ladung („Hafen: Hafengeld &
  Umschlag“, alle 5 Ticks gebucht); dann legt es ab (Fahrt vom Liegeplatz zurück zum Gateway). `DemolishHarbor` 25 % zurück, Schiffe drehen um.
- **Güter/Handel** (Hook-Kette um `GoodsSystem.PreferredGateway`/`FreightShareFor`, vor Bahn und Autobahn): Hafen in Betrieb im Netzteil der
  Firma → Import/Export über seinen Zufahrtsknoten, wenn der Handelspartner (Vertrag bzw. Weltmarkt-Partner) über den **Seeweg** erreicht wird
  (Landweg bleibt bei Güterbahnhof/Autobahn; ohne Weltkarte nimmt der Hafen alles). Fernfracht **2,5 %** (Seehafen) bzw. **3,5 %**
  (Binnenhafen) des Warenwerts statt 8 % per Lkw bzw. 4,5 % per Bahn (Seeweg 0,004 €/t·km gegen 0,03 €/t·km über Land, Etappe 8);
  Kapazität = 8 Anläufe × Laderaum je Tag, darüber wieder Bahn/Lkw. Ladungen werden gezählt (`LoadsToday`, `PendingLoads` → Umschlag beim
  nächsten Schiff). `GatewaySystem.OfNode` kennt die Hafenzufahrt (Hook `WaterGatewayOfNode` → Zählung am Wasser-Gateway),
  `TradeRouteText` „Seeweg über Hafen … (Küstenfrachter, Fracht 2,5 % des Warenwerts)“ (Hook `HarborRouteText`). Test Seed 2024: alle 19
  Weltmarkt-Güter kommen über See → über den Hafen.
- **Kasse:** neue Kategorien (hinten angefügt) `HarborConstruction` „Bau Hafen & Klappbrücken“, `HarborFees` „Hafen: Hafengeld & Umschlag“
  (Einnahme). Unterhalt Häfen + Klappbrücken als `IUpkeepSource`.
- **Spielstand** „shipping“ v1 (automatisch hinten in der `SaveRegistry`, `IAfterLoad`): Häfen + Statistik, Schiffe vollständig (Rinne über
  Art/Hafen/Gateways, Richtung, Bug, Tempo, Zustand, Liegeplatz, Liegezeit, Ladung, Wartezeit), Zustand der Klappbrücken (über die Lage),
  Zähler, gesperrte Schiffe je Klasse, offene Gebühren. Abgeleitet: Raster, Rinnen, Querungen, Klappbrücken, Zufahrten, Gateway-Status.
  Test: Speichern bei offener Klappe mit Schiffen unterwegs → 3 000 Verkehrsschritte bitgleich. Grenze: Schiffe einer abgerissenen
  Hafenrinne fallen beim Laden weg (im laufenden Spiel fahren sie noch hinaus).
- **Unity** (`Game/Shipping`, Begleiter am RoadTool): `ShippingTool` (**Taste 8** – 8 war frei; Modus `RoadTool.Mode.Harbor`): Güterhafen
  (Geist rastet am Ufer ein, Hinweis Klasse/Tiefe/Straße), Klappbrücke (zwei Klicks, Vorschau: Linie + gelbe Klappe, Kosten/Fehler), Abriss
  (Hafen/Klappe). `ShippingView`: Fahrrinne als Overlay (sichtbar beim Straßen-/Gleis-/Hafenbau und Terraforming oder per Knopf „Fahrrinne“;
  Querungen rot = zu niedrig, gelb = Klappbrücke, grün = hoch genug), Tonnen rot/grün am Kanalrand alle 160 m (`Coast/Buoy_*`), Häfen (Pflaster,
  Kai-Kacheln `Coast/Quay`, Kräne `HarborCrane` bzw. `ContainerCrane` am Seehafen, Speicher `HarborWarehouse`, Container-Stapel), Klappbrücke
  (`Infrastructure/Drawbridge`, Klappen `LeafA/LeafB` drehen um die Querachse nach dem `Lift` der Simulation; feste Deckstücke bis zu den
  Enden der Klappe, Ampeln/Schranken rot bei gesperrter Straße; Platzhalter ohne Modell), Schiffe (Modelle bzw. Platzhalter-Rumpf mit
  Luken und Steuerhaus, interpoliert zwischen zwei Verkehrsschritten, am Kai parallel zur Kaikante), `PickShip`/`TryGetShipPosition`.
  `RoadNetworkView` blendet die Fahrbahn der Klappe aus (Modell statt Mesh), `InfrastructureDecoView` setzt dort keine Pfeiler.
  Bauleiste: Kategorie **„Hafen“** (`GameHud.Harbor.cs`, neues Icon `HudIconKind.Ship`, Abriss-Kachel „Hafen/Klappe“), HUD-Warnchip „Fahrrinne
  gesperrt“. `HarborWindow` (rechts, **F8** bzw. im Werkzeug): Wasser-Gateways, Rinnen mit allen Brücken (lichte Höhe), gesperrte Klassen,
  Häfen, Klappbrücken, Schiffe. Inspektor: `SelectionKind.Ship` (Folgen), `Harbor`, `Drawbridge`. Hilfe (F1): „8 · F8“ und Abschnitt Start.
  Modellzuordnung `OwnModelDescriptions.TransportFiles` (ship_cargo/ferry/sailboat, harbor_quay/crane/container_crane/warehouse, buoy_red/
  green, drawbridge). **Optik ungeprüft** (Unity nicht gestartet): Ausrichtung der Coast-Modelle (Wasserseite +Z) und der Klappbrücke
  (Straßenachse, Drehsinn der Klappen, Höhe 3 m Deck im Modell), Schiffshöhe auf dem Wasser, Lage der Kai-Kacheln/Kräne, Overlay-Material,
  Tonnen, Platzhalter.
- **Tests** `ShippingTests` (12): Rinne zwischen den Fluss-Gateways (tief/breit, im Wasser, deterministisch, Küstenfrachter zu tief),
  vorgebaute Autobahnbrücke hoch genug + 1 Tag Durchgangsschiffe (22 durch, 0 Ausfall), zu niedrige feste Brücke (Hinweis, Rinne gesperrt,
  Warnung, Schiffe bleiben weg, fahrendes Schiff kehrt um), automatische Trassierung hoch genug + Tunnel frei, Klappbrücke (öffnet, Schiff
  durch, Autos warten an der Schranke, keins auf der offenen Klappe, wieder zu), Segelyacht vs. Autobahnbrücke, Binnenhafen (Einrasten,
  Liegeplätze tief, Aufschüttung, Straßenfront → Betrieb → Wasser-Gateway befahrbar, Straße nicht durch den Hafen, Anlauf/Liegeplatz/Umschlag/
  Abfahrt/Gebühren), Handel über den Hafen (Hook, Fracht, Gateway-Zählung, Kapazität), niedrige Brücken schneiden den Hafen ab, Seehafen auf
  Seed 2024 (Küstenfrachter, Seehandel über den Hafen, Routentext), Speichern/Laden bitgleich, ohne Außenanbindung aus.
- **Nächste Teilstufe (offen):** Umwege (Wegfindung meidet oft offene Klappbrücken, Hinweis Stau), Gleis-Klappbrücken/Hubbrücken, Schleusen und
  Kanäle (Terraforming-Wasser als Rinne), Fährterminal + Fährlinien (Coast/Pier, Ferry), Fischereihafen (FishingBoat), Schlepper beim Anlegen
  (Tugboat), Mole/Leuchtturm am Seehafen, echte Ladung je Schiff (Lieferzeit per Schiff statt Zähler, Umschlag Bahn ↔ Schiff am Kai mit
  Hafenbahn), Kollisionen/Begegnungsverbot zwischen Rinnen, Liegeplätze nach Schiffslänge, Hafengröße/Ausbau, Schlagzeilen (neuer Hafen,
  Klappbrücke stört), Lärm der Häfen, Bodenwert am Wasser, Kreuzfahrt/Tourismus.

## Flughafen (Stufe 4)
- `Simulation/Aviation` (Namespace `CityBuilder.Simulation.Aviation`): `AviationTypes.cs` (`AviationRules` = alle Zahlen, `AirportType`/
  `AirportCatalog`, `AircraftType`/`AircraftCatalog`, `AirportLayout` = Geometrie, `Airport`, `Aircraft`, `Rotation` = Umlauf, `Destination`,
  `Obstacle`, `AirportPlan`), `AviationSystem` (`world.Aviation`, `ISimulationSystem` + `ISubstepSystem` + `IUpkeepSource` „Flughafen“, partial:
  `.Airports` Planen/Bauen/Ausbau/Abriss/Betriebsrichtung, `.Obstacles` Hindernisfreiheit + Bauhöhen, `.Flights` Flugplan + Flugzeuge,
  `.Passengers` Nachfrage/Ziele/Besucher/Zuzug/Luftfracht/Luft-Gateway, `.Noise` Fluglärm, `.Save`). Erzeugt in `CreateDefault` **nach dem Hafen**
  (verkettet dessen Hooks). Läuft immer (auch ohne Gateways); ohne Flughafen ändert sich nichts (alle Hooks liefern dann exakt den alten Wert).
- **Bauarten** (`AirportCatalog`): **Regionalflugplatz** Bahn 1 200 × 30 m, Streifen ±40 m, Sichtanflug (Anflugfläche 4 %, 2,5 km, innen ±40 m),
  10 Bewegungen/h, Terminal 1–2 Module (3/5 Positionen, 160/320 Fluggäste/h), 650 000 € / 6 500 €/Woche; **Verkehrsflughafen** Bahn 2 800 m echt –
  `RunwayLengthFor(Kartengröße)` = min(echt, 90 % der Karte auf 100 m, ≥ 1 700 m) → **1 800 m auf der 2-km-Standardkarte** –, 45 m breit, Streifen
  ±75 m, Präzisionsanflug (2 %, 3 km, innen ±150 m), 28 Bewegungen/h, Terminal 2–4 Module (4/6/8 Positionen, 600/1 200/2 000 Fluggäste/h),
  Luftfracht 60 Ladungen/Tag, 2,4 Mio. € / 18 000 €/Woche, Ausbau je Stufe 900 000 € (+6 000 €/Woche).
- **Anlage** (`AirportLayout`, Bahn-Koordinaten u entlang A → B, v zur Terminal-Seite): Bahn, Streifen, Parallelrollweg (v = 75/120 m) mit fünf
  Abrollbahnen, Vorfeld mit Abstellpositionen (52/45 m Abstand), Terminal-Module 64 × 32 m (Landseite = von der Bahn weg, **Straße davor** ≤ 12 m →
  in Betrieb), Tower, Hangar, Windsack. Gelände **T-förmig** (`Areas`): Bahn-Bereich (Streifen bis Rollweg, Bahnlänge + 2 × 60 m) + Terminal-Bereich
  (Vorfeld + Terminal, mittig) – ein einziges Rechteck über die ganze Länge fand in den Spielwelten keinen Platz.
- **Platzregeln** (`PlanAirport(type, Mitte, Richtung, Seite)` → `AirportPlan`): in der Karte (10 m Rand), kein Wasser, Höhenunterschied ≤ 30 m –
  das Gelände wird auf die mittlere Höhe **eingeebnet** (`FlattenArea`, Erdmenge × 0,5 €/m³ wird mitgebucht, „Bau Flughafen“), keine Straße/Gleis
  (Autobahn/Hauptstrecke der Welt: eigener Fehler), kein Bauwerk/gesetztes Gebäude, keine Freileitung (Masten); Zonengebäude weichen. Beide
  Einflugschneisen gesperrt → ungültig (`ApproachBlocked`). Bauwerks-Sperre (Art „airport“, beide Flächen) für Straßen und Gleise, verkettete
  Sperren für Grundstücke/Terraforming/Versorger/Dienste.
- **Hindernisfreiheit** (vereinfachte ICAO-Hindernisbegrenzungsflächen, `AllowedAt`): je Bahnende **Anflugfläche** (Beginn 60 m hinter dem Ende,
  Aufweitung 15 % je Seite, Neigung 4 %/2 %), seitlich **Übergangsfläche** 1 : 7 ab dem Streifenrand, **innere Horizontalfläche** 45 m (Radius 2/4 km).
  Hindernisse = Gebäude (Höhe geschätzt nach Zone/Größe/Stufe, `ObstacleHeights`), Dienste (TV-Turm 180 m, Radio 90 m, Kirche 45 m …), Versorger
  (Windrad 140 m, AKW 150 m, Kohle 110 m, Wasserturm 32 m …), Strommasten (Hochspannung 45 m), **Gelände** (Anhöhen in der Anflugfläche, je 300 m der
  höchste Punkt). In der **Kernzone** (erste 1 200 m) sperrt ein Hindernis dieses Bahnende → Landung und Start nur über das freie Ende
  (**Gegenrichtungsbetrieb**, Kapazität × 0,55); weiter draußen × 0,85 je Hindernis (max. 3 je Seite), Übergangs-/Horizontalfläche × 0,97 (≥ 0,85);
  geprüft stündlich bei Änderung (Gebäude, Bauwerke, Gelände). **Neue Hochbauten** in Anflug-/Übergangsfläche sind ungültig mit Hinweis
  („Einflugschneise Flughafen …: hier höchstens 3 m hoch – Wohnblock ~39 m“): Hooks `CitySystem.PlacementCheck` (gesetzte Gebäude),
  `ServiceSystem.ObstacleCheck`/`UtilitySystem.ObstacleCheck` (neuer Fehler `ObstacleClearance` + `ObstacleNote`, hinten angefügt). Die innere
  Horizontalfläche sperrt keine Neubauten (sonst wären auf der 2-km-Karte alle Windräder/Türme verboten), sie senkt nur die Kapazität. Zonen (Altbestand)
  wachsen weiter und werden dann zum Hindernis. `HeightLimitAt(p)` für Vorschau/Fenster.
- **Wind** (seit dem Merge von agent/environment): Wind des Tages = `world.Climate.WindOn(day)` (Umwelt, Etappe 6), Hauptwindrichtung = stärkegewichtetes
  Jahresmittel (`PrevailingFromClimate`); vorher eigene Annahme nach Breite (`PrevailingWindFrom`/`WindOfDay`, bleibt Rückfall). Betriebsrichtung
  täglich gegen den Wind (`ChooseDirections`), mit gesperrtem Ende erzwungen. Werkzeug richtet die Bahn anfangs nach der Hauptwindrichtung aus.
- **Luft-Gateway**: in Betrieb → `Operational`, `HasLocation` am Terminal (Landseite), Name „Luftweg · Flughafen …“, Ziel „weltweit · n Ziele“,
  Kapazität = Bewegungen/h; sonst `Gateway.AviationNote` („Straße vor dem Terminal fehlt“, „gesperrt (Hindernisse …)“). **Ziele** = bis 14 Hauptstädte
  (Gewicht √Bevölkerung / (1 + km/2 500)), Großkreis-Entfernung ab der Startregion, Flugdauer 0,5 h + km × 1,05 / 800 km/h; ohne Welt 4 fiktive Ziele.
- **Flugplan** (stündlich, `ScheduleHour`): Nachfrage je Richtung und Tag = Einwohner × 0,004 + Büro-Jobs × 0,012 + übrige Jobs × 0,0015 + Touristen
  (60 × Tourismus-Stärke des eigenen Landes `Profile.Raw[Tourism]` × (1 + Attraktivität/60) × Stadtgröße) – Anteil je Flughafen – + Umland (24/260),
  verteilt mit Morgen-/Abendspitze; Grundversorgung 0,34/0,5 Landungen je Tagesstunde; Übertrag als Bruchteil (deterministisch). Begrenzt durch
  Bahn (Hälfte der Bewegungen) und Terminal. **Umlauf** (`Rotation`): Landung → Bodenzeit (Kleinflugzeug 20, Turboprop 25, Jet 45, Fracht 60 Spielminuten)
  → Start; Muster nach Bauart (Flugplatz: Kleinflugzeug/Turboprop, Flughafen: 65 % Jet, 25 % Turboprop, 10 % klein); Frachtflug um 6/13/20 Uhr, wenn
  ≥ 8 Ladungen warten. **Nachtflugverbot** (Spieler-Schalter, 22–6 Uhr): keine Bewegungen, 70 % der Nachtnachfrage weicht auf den Tag aus, späte Abflüge
  parken bis 6 Uhr; ohne Verbot Nachtflüge mit 20 % Nachfrage.
- **Sichtbare Flugzeuge** (Verkehrszeit, `StepAircraft` im Verkehrsschritt): höchstens 3 Landungen je Stunde und Flughafen sichtbar (Rest statistisch, wie
  der Anteil echter Autos – in Verkehrszeit ist eine Spielstunde 450 s, eine Bahn schafft physisch keine 28 Bewegungen darin). Anflug auf dem 3°-Gleitpfad
  vom Kartenrand + 700 m (erscheint pünktlich zur Landezeit), Aufsetzen 300 m hinter der Schwelle, Ausrollen bis zur passenden Abrollbahn, Rollen über
  den Parallelrollweg zur Position, Abfertigung, Push-back, Rollen zum Haltepunkt, **Startfreigabe** nur bei freier Bahn (kein Ausrollen/Start, nächste
  Landung > 70 s, wer zuerst wartet, startet zuerst; > 900 s → läuft statistisch zu Ende), Startlauf bis zur Abhebegeschwindigkeit, Steigflug aus der
  Karte. Wege sind aus Lage/Richtung/Position abgeleitet (`TryPose` für Darstellung und Inspektor).
- **Wirkungen**: ankommende Fluggäste → **Besucher** (Touristen-Anteil) bleiben 2–4 Tage, kaufen täglich für 55 € in den Läden (`GoodsSystem.VisitorPurchase`:
  Ware aus dem Regal, Umsatz bei der Firma) und zahlen 30 € Eintritt (× Attraktivität/20, ≤ 1) + 3 € Kurtaxe („Tourismus: Kurtaxe & Eintritt“); Nachfrage
  Läden × (1 + ≤ 0,3 nach Besuchern je Einwohner), Büros × (1 + ≤ 0,25 nach **Geschäftsreisenden** je Büro-Job) über den verketteten
  `CitySystem.DemandModifier`. **Zuzug** per Flugzeug selten (0,12 Haushalte je Passagierlandung, Hook `MoveInGateway` verkettet, Zählung über
  `GatewaySystem.AirGatewayOfNode`). **Luftfracht**: Güter ab 300 € Grundpreis (Elektronik) über einen Verkehrsflughafen im Netzteil (Hooks
  `PreferredGateway`/`FreightShareFor` verkettet vor Hafen/Bahn), Fernfracht **6 %** des Warenwerts (teurer als Schiff 2,5 %/Bahn 4,5 %, aber schnell),
  ≤ 60 Ladungen/Tag, Umschlag beim nächsten Fracht- bzw. Jetflug (Beiladung 4).
- **Fluglärm** (`.Noise`, eigenes Raster `NoiseLayer`, gespeichert): stündlich aus den gewichteten Bewegungen der letzten Stunde (Jet 1, Turboprop 0,55,
  Kleinflugzeug 0,25, Fracht 1,15; Nachtflüge × 2,5), Pegel 0,35 + 0,1 je Bewegung (≤ 1,1) über der Bahn und entlang beider Schneisen (3,2 km, Radius
  140 → 420 m, leiser mit der Entfernung); als Maximum ins Stadt-Lärmraster (`NoiseSystem`, eine markierte Zeile) → Bodenwert/Zufriedenheit wie jeder Lärm.
  Neue Erinnerung **`MemoryKind.AircraftNoise` „Fluglärm“** (5 Gedanken), wenn der Lärm am Wohnort überwiegend vom Flughafen kommt (Hook
  `CitizenSystem.AircraftNoiseAt`), zählt beim Umzug wie Lärm. Übersicht: betroffene Einwohner (≥ 30 % Fluglärm, 18 Uhr), Beschwerden (Erinnerung ≥ 0,2).
- **Kasse** (hinten angefügt, nach Umwelt): `AirportConstruction` „Bau Flughafen“ (inkl. Einebnen), `AirportFees` „Flughafen: Lande- & Passagierentgelte“
  (Landung 35/420/1 400/1 800 €, 9 € je abfliegendem Fluggast, 25 € je Frachtladung; alle 5 Ticks gebucht), `TourismIncome` „Tourismus: Kurtaxe & Eintritt“;
  Unterhalt als `IUpkeepSource` „Flughafen“; Abriss 25 %.
- **Spielstand** „aviation“ v1 (automatisch hinten in der `SaveRegistry`, `IAfterLoad`): Flughäfen (Lage, Richtung, Seite, Bahnlänge, Höhe, Stufe,
  Betriebsrichtung, Wind, Planungs-Überträge, Statistik, Lärmzähler, Zustand der Schneisen + Kapazitätsfaktor + Signatur), Umläufe, Flugzeuge
  vollständig, Nachtflugverbot, Besucher, Tagesnachfrage, Überträge, Fluglärm-Raster. Abgeleitet: Zufahrt, Hindernisliste (Anzeige), Ziele, Gateway.
  Fehlt der Abschnitt → kein Flughafen. Test: Speichern mitten im Tick mit Flugzeugen unterwegs → 6 000 Verkehrsschritte bitgleich.
- **Unity** (`Game/Aviation`, Begleiter am RoadTool): `AirportTool` (**Taste 9** – 9 war frei; Modus `RoadTool.Mode.Airport`): Regionalflugplatz/
  Verkehrsflughafen (Mauszeiger = Bahnmitte, `,`/`.` drehen 5°, ⇧ 1°, Enter Terminal-Seite), Terminal ausbauen, Abriss. Vorschau: Bahn, beide Flächen,
  Terminal, **Einflugschneisen als ansteigende Trapeze** (grün frei, gelb eingeschränkt, rot gesperrt), **Lärmkorridor** (breiter werdendes Band),
  Hindernisse als Säulen, Hinweis mit Kosten/Erdarbeiten/Wind/Schneisen/Straße. `AirportView`: Bahn aus 64-m-Kacheln (`Airport/Runway`,
  `Runway_End`), Rollwege (`Taxiway`), Vorfeld + Positionsmarken, Terminal-Module (`Terminal`), Tower (`ControlTower`), Hangar (Tor zum Vorfeld),
  Windsack (dreht nach dem Tageswind), Flugzeuge (`Airliner`, `Airliner_Red` = Fracht, `SmallPlane`, Turboprop = Airliner × 0,72) mit Neigung im
  Anflug/Steigflug, Gepäckschlepper (`BaggageTrain`) an parkenden Flugzeugen, Overlay Schneisen + Hindernisse; sonst Platzhalter. Modell-Ids `air_*` in
  `OwnModelDescriptions.TransportFiles`. Bauleiste: Kategorie **„Flughafen“** (`GameHud.Airport.cs`, neues Icon `HudIconKind.Plane`, Werkzeugzeile
  Nach Wind/Terminal-Seite/Nachtflugverbot/Einflugschneisen, Abriss-Kachel), HUD-Warnchip „Flughafen“. `AirportWindow` (rechts, **F12** bzw. im Werkzeug;
  Reiter Betrieb · Flugplan · Ziele · Wirkung). Inspektor: `SelectionKind.Aircraft` (Folgen) und `Airport` (Ausbau-Knopf). Hilfe (F1): „9 · F12“ und Start.
  Dienst-/Versorger-Werkzeug zeigen den Hindernis-Hinweis. **Optik ungeprüft** (Unity nicht gestartet): Ausrichtung/Größe der Airport-Modelle (Bahn-Kacheln
  entlang lokal X, `Runway_End` an Ende A um 180° gedreht, Windsack-Richtung, Hangar-Tor), Flugzeughöhe/-neigung, Gepäckschlepper, Platzhalter, Overlay.
- **Tests** `AviationTests` (13): Bahnlänge nach Kartengröße, Bahnkennung, Wind nach Breite, Großkreis; Platz eben gültig + Einebnen + Buchung + kein
  Geld; Fehler Straße/Rand/Hügel + Erdarbeiten; TV-Turm in der Kernzone sperrt eine Schneise, zwei sperren alles; neue Hochbauten (TV-Turm, Windrad,
  Wohnblock) ungültig, niedriges Haus weit draußen erlaubt, nachträgliche Anhöhe → Gegenrichtungsbetrieb + Kapazität < 60 %; Luft-Gateway befahrbar mit
  Lage; ein Spieltag Flugbetrieb (alle Zustände von Anflug bis Start, Landungen/Fluggäste/Gebühren/Besucher); Nachtflugverbot (0 Nachtbewegungen, nachts
  still); Lärm in der Schneise ≫ seitlich und im Stadt-Lärm; Speichern/Laden bitgleich; ohne Flughafen nichts; Spielwelten 2024/74 (Info, s. u.).
- **Grenze 2-km-Karte (ehrlich):** In den echten Spielwelten (Seeds 2024/74, Autobahn + Bahnkorridor + Fluss queren die Karte) findet das Suchraster für den
  **Verkehrsflughafen keinen Platz** (1 920 m Bahn-Bereich kreuzt fast immer Autobahn oder Hauptstrecke), den **Regionalflugplatz** nur in Seed 2024
  (mit 340 000 € Erdarbeiten, eine Schneise durch Gelände gesperrt). Auf flachen Testkarten passen beide. Außerdem liegen die Einflugschneisen bei
  Ost-West-Lage großteils außerhalb der Karte (dort gibt es keine Hindernisse) – diagonal oder nahe einem Rand wirkt die Hindernisfreiheit voll.
- **Nächste Teilstufe (offen):** größere Karten bzw. Verlegen der Welt-Autobahn/-Bahn (oder Tunnel unter der Bahn) für einen echten Verkehrsflughafen,
  zweite (parallele) Bahn, Hotels/Übernachtungsbetriebe und Tourismus als Dienstleistungshandel (Etappe 8), Pendler per Flugzeug, echte Fluggäste als
  Bürger (Weg zum Terminal, Bus-/Bahnanschluss am Terminal, Parkhaus), Lieferzeit der Luftfracht in Verträgen (schnell statt billig), Flughafen-Jobs,
  Lärmschutz-Programm (Schallschutzfenster), Lärmkontingente/Bonus für leise Muster, Fluglärm-Overlay (eigene Ansicht), Bodenwert-Bonus Flughafennähe
  (Gewerbe), Schlagzeilen (neuer Flughafen, Fluglärm-Proteste), Go-around/Warteschleifen, Hubschrauber, Zonen-Wachstum in der Schneise begrenzen.

## Speichern & Laden
- **Regel: Jedes neue System implementiert `ISaveable` und erscheint im Determinismus-Test.** Am besten als
  `partial class XyzSystem` in `XyzSystem.Save.cs` (konfliktarm), Anmeldung in `Persistence/SaveRegistry.Collect`
  (eine Zeile, Reihenfolge = Lesereihenfolge) – oder einfach per `AddSystem`: registrierte `ISaveable`-Systeme werden
  automatisch hinten angehängt. `SaveLoadTests` vergleicht **alle** Abschnitte → neue Systeme sind sofort im Test.
- Format (`Simulation/Persistence`): Kopf unkomprimiert (Magic „CBSV“, Formatversion, Spielversion, Name, Tick der
  Simulationsuhr – keine Systemzeit –, Einwohner, Kontostand, Länge, FNV-1a-64-Prüfsumme), danach GZip mit Abschnitten
  (Id als Text, Version, Länge, Bytes). Unbekannter Abschnitt → übersprungen, fehlender → Standardzustand der frischen
  Welt, neuere Abschnittsversion → `SaveLoadException`. Beschädigt/abgeschnitten → `SaveLoadException` mit Meldung
  (Abschnitt genannt), nie ein Absturz; die alte Welt bleibt dann unberührt.
- `ISaveable` (`SaveId`, `SaveVersion`, `Write(SaveWriter)`, `Read(SaveReader, version)`), optional `IAfterLoad`
  (nach allen Abschnitten, z. B. Querverweise wie Bürger ↔ Fahrgast) und `IBeforeSave` (nur ohnehin fällige Nacharbeit,
  z. B. Spurgraph nach Bau im Pausenmodus). `Write` darf nichts verändern. **Enums immer über den Namen**
  (`w.Name(e)`/`r.Name<T>()`, Felder je Enum `NamedFloats`/`NamedDoubles`, Güter/Kanäle über ihre Id) – unbekannte
  Werte werden übersprungen. Nachschlage-Wörterbücher kanonisch (`w.IntDictionary`, nach Schlüssel sortiert); Wörterbücher,
  die **durchlaufen** werden, sind `Core.StableMap` (gleiche Reihenfolge wie `Dictionary`, Platzbelegung speicherbar).
  `System.Random` gibt es in der Simulation nicht mehr → `Core.CompatRandom` (gleiche Zahlenfolge, speicherbar) bzw. `SimRandom`.
- **Migrationen:** `SaveVersion` erhöhen und in `Read` alte Versionen weiter lesen (Beispiel `rules` v1 ohne Grünzeit → 20 s;
  `citizens` v2 = Umfeld-Cache mit Freizeit/Sport/Stolz, v1 → 0; neuer Abschnitt `noisebarriers` fehlt in alten Ständen → keine Wände;
  `utilities` v3 = Energie erweitert, v2 → Katalogleistung/leere Speicher; `servicevehicles` v1 fehlt → keine Dienstfahrzeuge;
  `safety` v1 fehlt → Sicherheit aus, keine Brände/Ruinen – das Spiel schaltet nach dem Laden ein; `shipping` v1 fehlt → keine Häfen,
  Schiffe/Klappbrücken – Fahrrinnen und Durchgangsschiffe entstehen aus dem Gelände neu; `ecology` v1 fehlt → Umwelt aus (alte
  Luftrechnung), Boden/Grundwasser sauber, keine Aufforstung – das Spiel schaltet ein; `goods` v4 = Umweltkosten im Firmen-Ledger, v3 → 0;
  `aviation` v1 fehlt → kein Flughafen; `realm` v2 = Etappe 7 Reich, v1 → eine Stadt, keine Lieferungen/Landeskasse, NPC-Expansion aus –
  das Spiel schaltet sie ein).
- Laden = `SimulationWorld.CreateDefault(gespeicherte WorldSettings, Seed)` (Planet/Gelände entstehen deterministisch neu,
  alle Hooks verdrahtet; Seed steht in `SimulationWorld.Seed`), dann alle Abschnitte. Abgeleitet und neu aufgebaut:
  Spurgraph (`RoadNetwork.Reloaded`), Kreuzungs-Cache, Außenanschlüsse, Netzteile, Gebäude-/Zonen-Index, Dienst-Reichweiten,
  `linesAtStop`, Wassermaske, Gelände-Caches des Bodenwerts, Handelswege, Handelspartner der Gateways. Weltabschnitt v4 trägt die
  Gateway-Schalter; Laden ruft `CreateDefault(…, newGame: false)` (keine vorgebaute Autobahn, sie steht im Abschnitt „roads“ mit
  `Locked`-Flag, die Gateways in „gateways“). **Gespeichert, obwohl „abgeleitet“**, weil sie
  bewusst nur bei Änderungen neu gerechnet werden und dazwischen eine Momentaufnahme sind: Zonenraster (Geländehöhen beim
  Aufbau), Versorgungsnetze, Dienst-Zuordnung, Grobraster des Bodenwerts, Umfeld-Cache und Gebäudelisten der Bürger,
  ÖPNV-Routen, KI-Lagebild des Handels.
- **Verkehr: laufende Fahrzeuge werden vollständig gespeichert** (Spur/Verbindung, Position, Route, Wartezustand,
  Reihenfolge je Spur, Stau-Index je Spur) – nur so laufen geladene Welten bitgleich weiter; Bürger, Lkw-Ladungen und
  Busse behalten ihr Fahrzeug, niemand geht verloren.
- Determinismus-Test: Welt A spielt, speichert, B lädt; A und B laufen weiter (auch mit gleichen Spieleraktionen,
  Speichern mitten im Tag und mitten im Tick zwischen zwei Verkehrsschritten, nach Bau ohne Tick, Handels-Command in der Warteschlange) und müssen je Abschnitt bitgleich sein
  (`SaveGame.SectionHashes`/`StateHash` = FNV über die Abschnittsbytes) plus unabhängige Beobachtungen.
- Leistung (Testprojekt, Info): 15 000 Bürger speichern ≈ 0,1 s, laden ≈ 0,17 s (inkl. Welt erzeugen), Datei ≈ 2,8 MB.
- Unity: `SimulationRunner.ReplaceWorld`/`NewGame` + Event `WorldReplaced` (Views lösen alte Abos, verwerfen Caches/Objekte,
  bauen neu auf – neue Views bitte ebenso). `Game/Persistence/SaveManager` (Begleiter am RoadTool): Ablage
  `Application.persistentDataPath/Saves/*.cbsave`, Schreiben im Hintergrund (atomar), Autosave alle N Spielminuten
  (PlayerPrefs, Ringpuffer 3), **Strg+S** Schnellspeichern, **Strg+L** Schnellladen. `SaveMenuWindow` (mittig, **Esc**
  wenn Esc sonst nichts tat; pausiert): Speichern/Speichern unter/Laden (Liste mit Spielzeit, Einwohner, Kontostand,
  Größe)/Neues Spiel (öffnet das Neues-Spiel-Fenster, s. „Spielstart & Startregion“)/Autosave. Tastenkürzel lesen `GameKeyboard.Current` (null während `Ui.TextInput`).
  Im Editor noch nicht visuell abgenommen.

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
     ✅ Zuzug nur über Außenanschlüsse (s. Gateways), Lieferverkehr (G5), Zufriedenheit (G6)
     ✅ Sichtbare Fußgänger: Gehwegnetz + Routen (Querung an Kreuzungen, Parkweg), Interpolation über die Fahrzeit,
     Park/Spaziergang/Laden/Haustür, Instancing mit LOD, Platzhalter-Figuren + eigene Modelle (Models/People), anklickbar
     (im Editor noch nicht visuell abgenommen). ✅ Fußwege in Verkehrszeit (echtes Gehtempo, s. „Zeitmodell“). Später: Abkürzungen durch Parks,
     freie Fußwege/Plätze, Ampelphasen an Zebrastreifen, inkrementeller Gehweg-Neubau (✅ ÖPNV verbunden: Wartende, Fußwege zur/von der Haltestelle)
   - ✅ G3 Strom & Wasser: Kraftwerke/Pumpe/Wasserturm, Netze (selbst verlegt), Bilanz je Netz,
     Versorgungsstatus, Verlassen bei langem Ausfall, Info-Ansicht, Kosten/Unterhalt über die Stadtkasse.
     ✅ Abwasser (selbst verlegte Kanäle, Kläranlage, Auslass), Wasserqualität flussabwärts → Trinkwasser/Gesundheit/Ufer,
     Atomkraftwerk mit Akzeptanz und Störfall.
     ✅ Netze selbst verlegt: Wasserrohre, Strom mit Hochspannung → Umspannwerk → Ortsnetz (Freileitung/Erdkabel),
     Hausanschluss in 12 m, „Entlang Straßen verlegen“, Spielstand v2 mit Migration (im Editor noch nicht visuell
     abgenommen). Später: schwankende Erzeugung, Rohrdurchmesser/Leitungskapazität, Rückspeisung, Umspannwerk-Modell
   - ✅ G4 Dienste (einzeln baubar, Einflussradius übers Straßennetz): Polizei, Feuerwehr, Gesundheit, Schulen,
     Bibliothek, Parks, Müll (Müllhalde, Verbrennungsanlage); Abdeckungs-API, Müllstau, Brandrisiko, Werkzeug/Ansicht
     (im Editor noch nicht visuell abgenommen). ✅ Medien (Zeitung, Radio, TV) mit Schlagzeilen aus echten Daten. ✅ Dienstfahrzeuge
     (Grundstufe: Müllwagen-Touren mit Abholung bei Ankunft, Streife, Platzhalter-Brandeinsätze, Rettung bei Krankheit).
     ✅ Sicherheit: Brände (Entstehung, Wachstum, Übergreifen, echte Löschfahrzeuge, Wasser, Sanierung/Ruine/Wiederaufbau) und
     Kriminalität (Faktoren, Streifenpräsenz, Einsätze, Aufklärung, Zellen), Ansichten/Flammen/Blaulicht/Inspektor/HUD, Spielstand
     safety v1 (im Editor noch nicht visuell abgenommen). Später: Gefängnis, Brandschutz-Verordnungen, Wind, Taxi
   - ✅ G5 Warenketten: Rohstoffe → Industrie → Handel → Haushalte, Lieferverkehr, Preise, Import/Export, Haushalt/Steuern
     (Stadtkasse: Einnahmen/Ausgaben, Bilanz halten = Kern der Herausforderung); Industrie-/Wohnformen erweitern
     - ✅ Güterkatalog + Rezepte, Firmen mit Lager/Kasse/Gewinn, Industrie-Unterarten nach Vorkommen/Nachfrage, Märkte mit
       Preisen und Weltmarkt, Import/Export, Lkw-Lieferverkehr, Konsum nach Bedarfsstufen, Gewinn → Nachfrage/Pleite,
       Gewerbesteuer aus Gewinn, Vorkommen/Ernte aus der Welt, Industrie-Strom/Wasser nach Rezept, Fenster „Wirtschaft“
       (F5, Baukasten) und Inspektor-Reiter „Firma“/Lkw-Ladung (im Editor noch nicht visuell abgenommen).
       Offen: Baustoffe/Brennstoffe nutzen, Verträge
     - ✅ Produktion lenken: Schwerpunkt-Gebiete (Zonen-Werkzeug, eigenes Raster, Bonus/Abschlag in der Standortwahl, träger
       Betriebswechsel, Vorkommen bleibt Pflicht), Förderung je Branche (Stufe 0–3, Kasse „Wirtschaftsförderung“), Hinweis
       zum Vorkommen + Rohstoff-Ansicht, Reiter „Lenkung“ (F5) mit Empfehlungen, Inspektor, Spielstand „policy“ v1/Waren v3
       (im Editor noch nicht visuell abgenommen). Offen: Viehbetrieb, Förderung je Gut, Gewerbe-/Büro-Schwerpunkte
     - ✅ Stadtkasse vorgezogen: Kontostand, Steuern je Zonenart mit Nachfragewirkung, Unterhalt, Baukosten,
       Abrisserlös, Überziehungsrahmen mit Zinsen + Baustopp, Wochenbilanz/Historie, `IUpkeepSource` für Dienste,
       Fenster (M). Offen: Gebühren, Handelseinnahmen, Kredite mit Laufzeit, Wirkung auf Zuzug/Zufriedenheit
   - Außenanbindungen (Gateways, s. dort):
     - ✅ Stufe 1: vorhandene Autobahn beim Kartenstart (Raute + Zubringer, gehört der Welt, Brücke über den Fluss), Ziele aus der
       Weltkarte, Zuzug nur über Anschlüsse (Zuzügler fahren vom Gateway), Handel über den Gateway Richtung Partnerland (Seeweg
       vorerst per Lkw zum Hafen außerhalb), Durchgangsverkehr nach Wichtigkeit, Bahnkorridor + Wasserstraßen vorbereitet,
       Schilder/Inspektor/HUD-Hinweis/Weltkarte (im Editor noch nicht visuell abgenommen)
     - ✅ Stufe 2 Bahn (s. „Bahn (Stufe 2)“): zweigleisige Fernbahn-Hauptstrecke auf dem Korridor (gesperrt, Brücke über die
       Autobahn), eigenes Gleisnetz mit strengeren Regeln (3,5 %, Radius 150 m, tangentiale Weichen), Bahnübergang mit Schranke
       (Verkehr wartet) bzw. Brücke/Tunnel, Personen- und Güterbahnhof (Straßenfront, Bahnsteige), Fernzüge (Halt am eigenen Bahnhof)
       und Güterzüge mit Blockabschnitten/Signalen, Fahrgäste aus der Verkehrsmittelwahl, Zuzug/Pendler per Bahn (Rail-Gateway
       befahrbar), Import/Export über den Güterbahnhof billiger, Kasse, Spielstand, Werkzeug 7/F7/Inspektor (im Editor noch nicht
       visuell abgenommen). Nächste Teilstufe: eigene Regional-/S-Bahn-Linien mit Zugdepot, Kopfbahnhöfe/Wenden, echte Ladung je Wagen,
       Fahrstraßen über eingleisige Abschnitte, Rückstau am Bahnübergang, Tram
     - ✅ Stufe 3 Wasserstraße/Hafen (s. „Wasserstraße & Hafen (Stufe 3)“): Fahrrinnen aus den Wasser-Gateways (Tiefe/Breite aus dem
       natürlichen Gelände), Schiffsklassen mit Tiefgang und Masthöhe, lichte Höhe von Straßen-/Gleisbrücken (Trassierung hoch genug,
       Hinweis „zu niedrig für Schiffe“, gesperrte Rinne + Warnung), Tunnel unter dem Fluss, Klappbrücke (Schranke/Ampel, Verkehr staut),
       Durchgangsschiffe (Binnenschiff, Fahrgastschiff, Segelyacht), Güterhafen (Binnen-/Seehafen, Liegeplätze, Reede, Umschlag,
       Wasser-Gateway befahrbar), Seehandel und Import/Export per Schiff billiger, Kasse, Spielstand, Werkzeug 8/F8/Inspektor/Warnchip
       (im Editor noch nicht visuell abgenommen). Nächste Teilstufe s. dort
     - ✅ Stufe 4 Flughafen (s. „Flughafen (Stufe 4)“): Regionalflugplatz/Verkehrsflughafen als Anlage (Bahn nach Kartengröße, Rollwege, Vorfeld,
       Terminal mit Ausbaustufen, Tower, Hangar), Einebnen mit Kosten, Hindernisfreiheit (Anflug-/Übergangsflächen: bestehende Hindernisse sperren
       ein Bahnende bzw. senken die Kapazität, neue Hochbauten ungültig), Wind aus dem Klima, Luft-Gateway mit Lage und Zielen weltweit, Flugplan nach
       Nachfrage/Kapazität, sichtbare Flugzeuge (Anflug → Rollen → Parken → Start), Nachtflugverbot, Fluglärm (Raster, Erinnerung, Beschwerden),
       Besucher/Tourismus-Grundstufe, Geschäftsreisen → Büro-Nachfrage, seltener Zuzug, Luftfracht, Kasse, Spielstand, Werkzeug 9/F12/Inspektor
       (im Editor noch nicht visuell abgenommen). Grenze: Verkehrsflughafen findet auf der 2-km-Spielkarte kaum Platz. Nächste Teilstufe s. dort
   - ÖPNV & Fernverkehr: Taxi-, Bus-, Zug-Depot, Buslinien ✅, Fernbahn ✅ (Stufe 2), Tram, U-Bahn, S-Bahn, Flughafen ✅ (Stufe 4)
     - ✅ Stufe 1 Buslinien: Haltestellen, Rund-/Pendellinien, Busdepot, echte Busse im Verkehr, Fahrgäste mit Verkehrsmittelwahl
       und Kapazität, Fahrgeld/Unterhalt, Linienfenster (F6), Werkzeug (5) (im Editor noch nicht visuell abgenommen).
       Später: Tram, U-/S-Bahn, Taxi, Fernbahn, Flughafen
   - Energie erweitern: ✅ Atomkraftwerk; ✅ Biogas (Rohstoff aus Bauernhöfen), Laufwasserkraftwerk (Abfluss/Fallhöhe), Offshore-Windpark
     (Seekabel), Batteriespeicher (puffert im Netz), Spielstand utilities v3 (im Editor noch nicht visuell abgenommen). Offen: Biomasse als Gut,
     Talsperre, schwankende Onshore-Wind/Solar
   - ✅ Schifffahrt (mit Häfen/Handel): schiffbare Gewässer mit Durchfahrtshöhe; feste Brücken hoch genug oder
     **Klapp-/Zugbrücken** (niedrig, öffnen für Schiffe → Rot/Stau) oder Tunnel (Stufe 3, s. „Wasserstraße & Hafen“). Offen: Umwege
     (Wegfindung meidet häufig offene Klappbrücken), Gleis-Klappbrücken, Hubbrücken, Schleusen
   - ✅ G6 Bodenwert (Raster aus Lage, Diensten, Umwelt, Nachbarschaft), Ausbaustufen 1–3, Gebäudezustand/Verfall,
     Grundsteuer nach Bodenwert, Standortwahl der Bauherren, Overlay (F2), Anzeige in Inspektor/HUD und Meldungen
     bei Aufstieg/Verfall (Spiel-UI Phase 2). Offen: Stufen nach Einkommen (G5)
   - ✅ Wohnhäuser einzeln setzen (Anno-Stil, Taste 6): 3 Bauarten, Platzregeln/Straßensperre wie Dienste, bevölkern über das
     Bürgersystem, Stufen nach Einrichtungen in Reichweite (datengetrieben, Abstieg), Vorschau mit ✓/✗ und Reichweiten,
     Inspektor, Verschieben, Spielstand city v2 (im Editor noch nicht visuell abgenommen). Zonen bleiben zusätzlich.
   - ✅ Setzen statt Zonen: Reihe ziehen (LMB halten, folgt Straße/Kurve/Knick, eine Buchung), Laden, Wohn- und Geschäftshaus,
     Bürohaus, Gewerbe-/Industriehalle (Betrieb wählt die Simulation inkl. Schwerpunkt, Pleite → neuer Betreiber), Aufstieg je Bauart
     (Kundschaft, Fachkräfte, Güteranschluss), Kategorie Bauen mit Reitern + Lenken (Z), Zonen nur noch in alten Ständen, keine
     Grundstücksplatten/Zonenflächen, Nachfrage-Anzeige mit „warum“, Parallelabstand nach Gebäudetiefen + Kurvenzuschlag
     (im Editor noch nicht visuell abgenommen). Später: Reihen über Kreuzungen, Ecken füllen, eigenes Aussehen
   - ✅ Zeitmodell: Kalender 6 min/Tick + 180 Verkehrsschritte (Zeitdehnung 8), Fußwege/Platzhalter in Verkehrszeit,
     Fahrgäste gehen erst zur Haltestelle, Tempo 1/2/3 = 6/3/1,5 min je Spieltag mit Rechenzeit-Budget, Speichern mitten im Tick
     (im Editor noch nicht visuell abgenommen). Später: Abfahrten innerhalb des Ticks verteilen, CarShare neu abstimmen, Rad-Darstellung
   - ✅ Speichern & Laden: versioniertes Binärformat mit Abschnitten/Migrationen, alle Systeme `ISaveable`, Determinismus
     nach dem Laden, Spielmenü (Esc) mit Autosave/Schnellspeichern. Später: Cloud/Multiplayer-Stände, Vorschaubild, Replays aus Commands
   - ✅ Eigene Assets eingebaut: Freizeitanlagen + Wahrzeichen (Rathaus einmalig, Bodenwert/Stolz/Attraktivität), Lärmschutzwand
     (dämpft Lärm), Bauernhöfe je Jahreszeit, Brückenpfeiler/Tunnelportale/Schilderbrücken/Baustellen/Hochspannungsmast, Büsche/
     Felsen, Straßen-/Parkausstattung; Beschreibungsdatei `Name.json` + Bericht `_Unused.txt` (im Editor noch nicht visuell
     abgenommen). Wartet auf Funktion: Strand/Leuchtturm/Mole/Fähranleger/Fischerei, Taxi, U-Bahn
     (Bahn ✅ Stufe 2; Hafen, Schiffe, Tonnen, Klappbrücke ✅ Stufe 3; Flughafen ✅ Stufe 4)
5. ✅ Welt: fiktive Erde, Klimazonen, Fruchtbarkeit, Bodenschätze (Kartengenerator), Länder mit eigenen Stärken
   (Regionen, Länder mit Profil/komparativen Vorteilen, LOD-Volkswirtschaft je Land, Stadtkarte als Ausschnitt der
   Startregion, `world.Climate`, `world.Resources` mit `DepositAt`/`Extract`, Weltkarte F4, Ressourcen-Ansicht F3;
   im Editor noch nicht visuell abgenommen). ✅ Startregion nach Vorlieben wählen (Spielstart, freies Land, Stadtstaat).
   Später: ~~Klima auf Gelände-Optik~~ ✅ (Etappe 6), Handel (8)
6. ✅ Umwelt: Abgas, Wasser, Auswirkungen auf Gesundheit/Zufriedenheit/Ernte (s. „Umwelt (Etappe 6)“): eigenes `EcologySystem` –
   Luft aus Verkehr (echte Fahrzeuge, Lkw stärker), Kraftwerken nach Auslastung, Industrie nach Rezept, Heizen nach Klima/Bauart,
   Müllverbrennung, Schiffen; Wind je Tag, Inversion, Regen, Abbau durch Wald/Parks; Boden & Grundwasser (Industrie, Deponie,
   Auslass, Ablagerung) → Fruchtbarkeit/Ernte, Brunnen; Hitze/Dürre → Ernte; chronische Belastung → Gesundheit (Kinder/Ältere
   stärker), Altlasten → Bodenwert, Tourismus-Faktor vorgemerkt; Smog-Alarm/Fischsterben/Auflagen als Schlagzeilen + HUD;
   Gegenmaßnahmen Umweltauflagen 0–3 (Filter, Firmen- und Stadtkosten), Umweltzone, Bäume pflanzen; Ansichten Luft/Boden/
   Grundwasser, Umweltfenster, Smog-Dunst, Gelände-Farben nach Jahreszeit; Spielstand ecology v1/goods v4 (im Editor noch nicht
   visuell abgenommen). Später: Lärm aus Industrie/Verkehrsmenge, Hochwasser, Klimawandel/CO₂, Recycling, Fernwärme/Wärmepumpen
7. ✅ Reich: mehrere Städte, Transport dazwischen, interner Handel, Land gründen (s. „Reich (Etappe 7)“): Städte gründen auf der
   Weltkarte (freie Nachbarregion bzw. freier Platz in der eigenen Region; Einwohner, Rathaus, Anschluss Autobahn/Hafen, Kosten), eine Stadt
   detailliert + übrige aggregiert (Stadt-Volkswirtschaft mit Wachstum, Produktion/Bedarf je Kategorie, Lager, Kasse), Wechseln mit
   eingebetteten Unter-Spielständen (deterministisch, ein Spielstand enthält alle Städte), interne Lieferungen (gleiche Vertragsbedingungen/
   Commands wie der Handel, Lkw/Bahn/Schiff, ohne Zoll), Land gründen (Name, Flagge, Hauptstadt, Weltkarte färbt um, Anerkennung/Beziehung
   der NPC-Länder, Landeskasse, Länder-Volkswirtschaft = Summe der Städte), NPC-Expansion in freies Land (Utility AI, langsam, gedeckelt,
   Grenzstreit nur als Beziehung), Reichsfenster (Pos1), Stadtname + Land oben (im Editor noch nicht visuell abgenommen).
   Nächste Teilstufe: Pendler/Zuzug zwischen Nachbarstädten, NPC-Städte, Gebietsstreit mit Folgen, Grenzlager je Stadt, Landesgesetze.
8. Handel & Politik: Bot-Nationen, Marktpreise, Verträge, Regierungen/Bündnisse, externer Handel, Tourismus
   - ✅ Stufe 1: Weltmarkt aus den Ländern (Weltpreis speist G5), Handelswege Land/See mit Kosten und Lieferzeit, Angebote/Verträge/
     Lieferungen als Commands (gleich für Spieler und NPC), Grenzlager mit G5-Import/Export, NPC-Länder als Utility AI mit
     Persönlichkeit/Gedächtnis, Beziehungen, Vertrauen, Zölle, Handelsabkommen, Stadtkasse (Zölle, Strafen, Vertragshandel),
     Handelsfenster F11 (im Editor noch nicht visuell abgenommen), Weltwirtschaft kalibriert (im Mittel ausgeglichen),
     Schlagzeilen zum Handel. Später: Regierungen/Bündnisse, Tourismus, Gegenangebote
9. Multiplayer (Freunde einladen, eigene Städte im eigenen oder fremden Land, asynchron), Online-Handel, Mobile-Port
