# Modell-Konvention (für die Blender-KI)

Neue Modelle kommen **ohne Code-Änderung** ins Spiel: FBX nach `Assets/_Project/Models/<Ordner>/<Name>.fbx` exportieren –
der Editor baut die Kataloge automatisch neu (bei jeder FBX-/JSON-Änderung und beim Start, wenn sich etwas geändert hat).
Was keine Funktion hat, steht danach in `Assets/_Project/Models/_Unused.txt` (mit „wartet auf …“) und im Editor-Log
(`[OwnModels] …`). Diese Datei wird erzeugt, nicht bearbeiten (nicht im Git).

## Modell selbst

- **Ursprung** = Mitte der Grundfläche auf Bodenhöhe (Personen: Mitte zwischen den Füßen, Bäume/Props: Stamm-/Fußpunkt).
- **Vorderseite** zeigt in Unity nach **+Z** (Blender −Y) – bei Gebäuden/Diensten die Zufahrt zur Straße, bei Requisiten die
  Seite zur Straße, bei Schilderbrücken die Schildseite (schaut den ankommenden Autos entgegen), bei Tunnelportalen die Öffnung.
- **Maßstab** 1 Einheit = 1 m, Y-up, Modifier angewendet. Grobe Einheitenfehler werden bei Diensten/Versorgern korrigiert,
  sonst gilt die Originalgröße.
- **Material**: nur das gemeinsame `CB_Palette` (Farbfelder der Paletten-Textur), keine eigenen Texturen.
- **Bewegliche Teile** als **benannte Kindobjekte** mit Ursprung im Drehpunkt: `Rotor` (Windrad), `Sails` (Windmühle,
  dreht um die Front-Achse), `Wheel` (Riesenrad, dreht um lokale Z), `LegL/LegR/ArmL/ArmR` (Personen), `LeafA/LeafB`
  (Klappbrücke, noch ohne Funktion).
- **Kachelbare Teile**: Felder 32 × 32 m (Reihen entlang X), Lärmschutzwand 16 m entlang +Z, Straßenteile laufen entlang +Z.
- **Wasserbauwerke** (Eigene Assets 2): `HydroDam` – Wehrachse = Breite (X, quer zum Fluss), Vorderseite +Z flussabwärts, Ursprung auf dem Wasserspiegel (das Spiel setzt ihn 0,4 m darüber; Gelände wird nicht eingeebnet, Grundfläche 66 × 56 m). `OffshoreWind` – Ursprung = Wasserspiegel, Gründung darf darunter reichen, Rotor als Kindobjekt `Rotor` (dreht um lokale Z).
- **Fahrzeuge** (`Models/Vehicles`): Ursprung = Mitte der Unterkante (Räder auf 0), Front +Z, echte Länge (Pkw ~4,6 m, Lieferwagen ~6 m, Müllwagen ~8 m, Bus 12 m) – es gibt keine Front-Erkennung und keine Skalierung wie bei Store-Fahrzeugen.

## Ordner und Namen (Regeln ohne JSON)

| Ordner / Name | Rolle | Wo im Spiel |
|---|---|---|
| `Buildings/<Zone>/*.fbx` (`ResidentialLow/-Medium/-High`, `Mixed`, `Retail`, `Office`, `Industry`) | building | wachsende Gebäude der Zone; Stufe nach Höhe |
| `Utilities/<Name>` (CoalPlant, GasPlant, WindTurbine, SolarPark, WaterPump, WaterTower, NuclearPlant, Substation), `Services/SewagePlant` | facility | Versorgungsbauwerk |
| `Energy/<Name>` (BiogasPlant, HydroDam, OffshoreWind, BatteryStorage) | facility | Biogasanlage (`power_biogas`), Laufwasserkraftwerk im Fluss (`power_hydro`), Offshore-Windpark – das Modell = **eine** Anlage, der Park zeichnet 4 im Raster (`power_offshore_wind`, Kindobjekt `Rotor`), Batteriespeicher (`power_battery`) |
| `Services/<Name>` (PoliceStation, FireStation, Clinic, Hospital, PrimarySchool/HighSchool oder School, Library, ParkSmall/ParkLarge oder Park, Landfill, Incinerator, Newspaper, RadioStation, TVTower) | service | Dienstgebäude |
| `Leisure/<Name>` (Playground, SportsField, TennisCourts, OutdoorPool, Stadium, FerrisWheel) | service | Freizeitanlage (baubar) |
| `Landmarks/<Name>` (TownHall, Church, Museum, Castle, Ruin, Statue, Fountain, Obelisk, Bandstand) | service | Wahrzeichen (baubar) |
| `Transport/BusDepot`, `Props/BusStop` | transport | Busdepot, Haltestelle |
| `Nature/Tree_*` · `Bush_*` · `Rock_*` | tree · bush · rock | Bäume · Büsche (Waldrand, Parks) · Felsen (Hänge, Gebirge) |
| `Terrain/Rocks`, `Terrain/Cliff_Straight` | rock · rock (`use: cliff`) | weiterer Felsen · Felsklippe an sehr steilen Hängen (ab ~40°, Wand +Z talwärts, Höhe auf den Hang gestaucht) |
| `Vehicles/<Name>` (Bus, DeliveryVan, GarbageTruck, PoliceCar, FireTruck, Ambulance) | vehicle | Linienbus (ersetzt die Store-Busse), kleiner Lieferwagen (Einzelhandel ≤ 35 % Ladung), Dienstfahrzeuge (Müllwagen, Streife, Feuerwehr, Rettung) – Ursprung Mitte Unterkante, Front +Z, Originalgröße |
| `Props/StreetLamp*` | streetlamp | Straßenlaternen |
| `Props/<Name>` (sonst) | prop | Gehweg-/Parkausstattung (Kiosk, PicnicTable nur Park; Bench, TrashBin, Planter beides; Rest Gehweg) |
| `People/*.fbx` | person | Fußgänger (Präfix `Toddler`, `Child`/`Kid`, `Teen`, `Senior`, sonst Erwachsene) |
| `Agriculture/Farm`, `Field_<Frucht>[_Harvested]`, `Pasture`, `Orchard`, `Vineyard`, `Greenhouse`, `Cow`, `Tractor`, `HayBales`, `Windmill` | agriculture | Bauernhöfe (Industrie mit Landwirtschafts-Firma), Felder je Jahreszeit |
| `Infrastructure/BridgePillar`, `TunnelPortal`, `PowerPylon`, `SignGantry`, `Roadworks`, `NoiseBarrier` | infrastructure | Brückenpfeiler, Tunnelportal, Hochspannungsmast, Schilderbrücke vor Abfahrten, Baustelle, Lärmschutzwand |
| `Airport/*`, `Coast/*`, `Transport/TaxiDepot`, `MetroEntrance`, `TrainStation`, `Infrastructure/Drawbridge`, `Vehicles/Taxi` | waiting | noch ohne Funktion (Flughafen, Hafen, Taxi, U-Bahn, Bahn, Schifffahrt) |
| `Terrain/Cliff_Corner`, `Cliff_Waterfall`, `Hill`, `Pond`, `River_*` | waiting | **passt nicht zum prozeduralen Gelände** (Höhenkarte statt Kachelraster: Hügel/Teiche/Flüsse entstehen aus der Höhenkarte bzw. dem Terraforming, der Fluss hat kein Gefälle für Wasserfälle) – Gelände-Kacheln bitte nicht mehr bauen |

Feldfrüchte, die die Höfe kennen: `Wheat` (mit `Wheat_Harvested`), `Corn`, `Rapeseed`, `Potato`, `Vegetables`, `Plowed`, `Meadow`.

## Beschreibungsdatei `Name.json` (optional, hat Vorrang)

Neben `Name.fbx` liegt `Name.json` – damit passt jedes Modell in jedem Ordner. Fehlende Felder bleiben leer.

| Feld | Werte | Bedeutung |
|---|---|---|
| `role` | `building`, `tree`, `streetlamp`, `prop`, `bush`, `rock`, `service`, `facility`, `transport`, `person`, `agriculture`, `infrastructure`, `vehicle`, `waiting` | Pflicht – wohin das Modell gehört |
| `zone` | `ResidentialLow` … `Industry` | bei `building` |
| `level` | 1–3 | bei `building`: feste Ausbaustufe (0/leer = nach Höhe) |
| `placement` | `sidewalk`, `park`, `both` | bei `prop` |
| `biome` | z. B. `temperate`, `boreal`, `tropical` | bei `tree` (gespeichert, noch ohne Wirkung) |
| `service` | Dienst-Id (`park_small`, `leisure_stadium`, `landmark_church` …) **oder** Dienstart (`park`, `leisure`, `landmark`) | bei `service`; Dienstart = alle Dienste dieser Art, die noch kein JSON-Modell haben (JSON geht vor den Ordnerregeln) |
| `facility` | Versorger-Id (`power_coal`, `water_tower`, `power_substation`, `power_biogas`, `power_hydro`, `power_offshore_wind`, `power_battery` …) | bei `facility` |
| `transport` | `bus_depot`, `bus_stop` | bei `transport` |
| `use` | Landwirtschaft: `farmstead`, `field`, `pasture`, `orchard`, `vineyard`, `greenhouse`, `animal`, `vehicle`, `deco`, `windmill` · Infrastruktur: `bridge_pillar`, `tunnel_portal`, `power_pylon`, `sign_gantry`, `roadworks`, `noise_barrier` · Fahrzeug: `bus`, `delivery_van`, `garbage_truck`, `police_car`, `fire_truck`, `ambulance` (`taxi` wartet) · Felsen: `cliff` | Verwendung innerhalb der Rolle (leer = aus dem Namen) |
| `tags` | Liste, z. B. `["wheat","harvested"]` | Feldfrucht/Zustand bei `field` |
| `waitsFor` | Text | bei `waiting`: worauf das Modell wartet (steht im Bericht) |
| `footprint` | `[Breite, Tiefe]` in m | nur Information (noch ohne Wirkung) |

### Beispiele

```json
{ "role": "building", "zone": "ResidentialLow", "level": 2 }
```
```json
{ "role": "prop", "placement": "sidewalk" }
```
```json
{ "role": "tree", "biome": "temperate" }
```
```json
{ "role": "service", "service": "park" }
```
```json
{ "role": "service", "service": "landmark_church" }
```
```json
{ "role": "agriculture", "use": "field", "tags": ["corn"] }
```
```json
{ "role": "waiting", "waitsFor": "Flughafen (Rollfeld)" }
```
```json
{ "role": "facility", "facility": "power_hydro" }
```
```json
{ "role": "vehicle", "use": "garbage_truck" }
```
```json
{ "role": "rock", "use": "cliff" }
```

## Ablauf

1. Modell bauen (Skript `blender/scripts/obj_*.py`, Größe im Docstring nennen), rendern, prüfen.
2. FBX nach `Assets/_Project/Models/<Ordner>/<Name>.fbx`; wenn Ordner/Name nicht eindeutig sind, `Name.json` daneben.
3. Unity baut `Resources/OwnModels.asset` und den Asset-Katalog neu. In `Models/_Unused.txt` steht, was noch wartet.
4. Neue Spielfunktion nötig (z. B. Flughafen)? Dann bleibt das Modell als `waiting` im Bericht, bis ein Agent sie baut.
