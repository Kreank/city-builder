using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Eigene Assets: liest alle FBX unter <c>Assets/_Project/Models</c> samt optionaler <b>Beschreibungsdatei</b>
    /// <c>Name.json</c> daneben und ordnet jedes Modell einer Rolle zu (JSON vor Ordnerregeln). Grundlage für
    /// <see cref="OwnModelCatalogBuilder"/> (eigener Katalog) und <see cref="AssetCatalogBuilder"/> (Gebäude/Bäume/Laternen)
    /// sowie den Bericht <c>Models/_Unused.txt</c> („wartet auf Funktion …“). Konvention: <c>docs/Modell-Konvention.md</c>.
    /// </summary>
    public static class OwnModelDescriptions
    {
        public const string ModelsRoot = "Assets/_Project/Models";
        public const string UnusedReport = ModelsRoot + "/_Unused.txt";

        /// <summary>Felder der Beschreibungsdatei (JsonUtility, fehlende Felder bleiben leer/0).</summary>
        [Serializable]
        public sealed class Json
        {
            public string role, category, use, zone, placement, biome, service, facility, transport, waitsFor, note;
            public int level;
            public string[] tags;
            public float[] footprint;
        }

        /// <summary>Ein Modell mit Zuordnung.</summary>
        public sealed class Desc
        {
            public string Path, Name, Folder;
            public Json Json;
            public string Role, Use, Placement, Biome, Zone, Target, WaitsFor;
            public int Level;
            public string[] Tags = new string[0];
            /// <summary>Wo im Spiel verwendet (für den Bericht) – null = ungenutzt.</summary>
            public string UsedBy;
            public string JsonError;
        }

        // ------------------------------------------------------------ Ordnerregeln

        /// <summary>Utilities/&lt;Name&gt; (bzw. Schlüssel mit Ordner) → Id des Versorgungsbauwerks.</summary>
        public static readonly Dictionary<string, string> FacilityFiles = new Dictionary<string, string>
        {
            { "CoalPlant", "power_coal" }, { "GasPlant", "power_gas" }, { "WindTurbine", "power_wind" },
            { "SolarPark", "power_solar" }, { "WaterPump", "water_pump" }, { "WaterTower", "water_tower" },
            { "NuclearPlant", "power_nuclear" }, { "Services/SewagePlant", "sewage_plant" }, { "Substation", "power_substation" },
            // Eigene Assets 2: Energie erweitert (Models/Energy)
            { "Energy/BiogasPlant", "power_biogas" }, { "Energy/HydroDam", "power_hydro" }, { "Energy/OffshoreWind", "power_offshore_wind" },
            { "Energy/BatteryStorage", "power_battery" },
        };

        /// <summary>Dienst-Id → Dateinamen (Services, Leisure, Landmarks; erster vorhandener gilt).</summary>
        public static readonly Dictionary<string, string[]> ServiceFiles = new Dictionary<string, string[]>
        {
            { "police_station", new[] { "Services/PoliceStation" } }, { "fire_station", new[] { "Services/FireStation" } },
            { "clinic", new[] { "Services/Clinic" } }, { "hospital", new[] { "Services/Hospital" } },
            { "school_primary", new[] { "Services/PrimarySchool", "Services/School" } }, { "school_high", new[] { "Services/HighSchool", "Services/School" } },
            { "library", new[] { "Services/Library" } },
            { "park_small", new[] { "Services/ParkSmall", "Services/Park" } }, { "park_large", new[] { "Services/ParkLarge", "Services/Park" } },
            { "landfill", new[] { "Services/Landfill" } }, { "incinerator", new[] { "Services/Incinerator" } },
            { "media_newspaper", new[] { "Services/Newspaper" } }, { "media_radio", new[] { "Services/RadioStation" } }, { "media_tv", new[] { "Services/TVTower" } },
            // Freizeit
            { "leisure_playground", new[] { "Leisure/Playground" } }, { "leisure_sports_field", new[] { "Leisure/SportsField" } },
            { "leisure_tennis", new[] { "Leisure/TennisCourts" } }, { "leisure_pool", new[] { "Leisure/OutdoorPool" } },
            { "leisure_stadium", new[] { "Leisure/Stadium" } }, { "leisure_ferris_wheel", new[] { "Leisure/FerrisWheel" } },
            // Wahrzeichen
            { "landmark_town_hall", new[] { "Landmarks/TownHall" } }, { "landmark_church", new[] { "Landmarks/Church" } },
            { "landmark_museum", new[] { "Landmarks/Museum" } }, { "landmark_castle", new[] { "Landmarks/Castle" } },
            { "landmark_ruin", new[] { "Landmarks/Ruin" } }, { "landmark_statue", new[] { "Landmarks/Statue" } },
            { "landmark_fountain", new[] { "Landmarks/Fountain" } }, { "landmark_obelisk", new[] { "Landmarks/Obelisk" } },
            { "landmark_bandstand", new[] { "Landmarks/Bandstand" } },
        };

        /// <summary>ÖPNV-Id → Datei (mit Ordner).</summary>
        public static readonly Dictionary<string, string[]> TransportFiles = new Dictionary<string, string[]>
        {
            { "bus_depot", new[] { "Transport/BusDepot" } },
            { "bus_stop", new[] { "Props/BusStop" } },
        };

        /// <summary>Ordner/Datei ohne Funktion im Spiel → worauf sie wartet (für den Bericht).</summary>
        static readonly (string prefix, string waitsFor)[] Waiting =
        {
            ("Airport/", "Flughafen (Roadmap: ÖPNV & Fernverkehr)"),
            ("Coast/", "Hafen/Schifffahrt/Küste (Roadmap: Schifffahrt mit Häfen)"),
            ("Transport/TaxiDepot", "Taxi (Roadmap: ÖPNV)"),
            ("Transport/MetroEntrance", "U-Bahn (Roadmap: ÖPNV)"),
            ("Transport/TrainStation", "Bahn (Roadmap: S-Bahn/Fernbahn)"),
            ("Infrastructure/Drawbridge", "Klapp-/Zugbrücken (Roadmap: Schifffahrt)"),
            // --- Eigene Assets 2: Taxi und Gelände-Kacheln (Entscheidungen s. docs/Modell-Konvention.md)
            ("Vehicles/Taxi", "Taxi (Taxi-Depot + Taxi als Verkehrsmittel der Bürger, Roadmap: ÖPNV)"),
            ("Terrain/Cliff_Corner", "Gelände-Kachelraster: Klippen-Ecke passt nur an rechtwinklige Kanten – das Gelände ist eine stetige Höhenkarte (Cliff_Straight steht an steilen Hängen)"),
            ("Terrain/Cliff_Waterfall", "Flüsse mit Gefälle/Stufen: der erzeugte Fluss hat kein Gefälle (Bett überall gleich tief)"),
            ("Terrain/Hill", "keine Verwendung geplant – passt nicht zum prozeduralen Gelände: Hügel entstehen aus der Höhenkarte (TerrainMap/Terraforming); ein Hügel-Modell würde Straßen/Gebäude/Gelände durchdringen"),
            ("Terrain/Pond", "keine Verwendung geplant – passt nicht zum prozeduralen Gelände: Wasser ist jede Stelle unter dem Wasserspiegel (Terraforming „Absenken“ macht Teiche mit Simulation); ein Modell hätte Wasser ohne Wirkung"),
            ("Terrain/River_", "keine Verwendung geplant – passt nicht zum prozeduralen Gelände: der Fluss ist Teil der Höhenkarte (Breite/Lauf aus der Startregion, Wasserqualität, Brücken)"),
        };

        /// <summary>Requisiten nach Name → Aufstellort („sidewalk“, „park“, „both“).</summary>
        static string PropPlacement(string name)
        {
            switch (name)
            {
                case "Kiosk":
                case "PicnicTable": return "park";
                case "Bench":
                case "TrashBin":
                case "Planter": return "both";
                default: return "sidewalk";
            }
        }

        /// <summary>Landwirtschaft nach Name → Verwendung + Schlagwörter.</summary>
        static void AgricultureRule(Desc d)
        {
            string n = d.Name;
            if (n.StartsWith("Field_"))
            {
                d.Use = "field";
                d.Tags = n.Substring(6).ToLowerInvariant().Split('_');
                return;
            }
            switch (n)
            {
                case "Farm": d.Use = "farmstead"; break;
                case "Pasture": d.Use = "pasture"; break;
                case "Orchard": d.Use = "orchard"; break;
                case "Vineyard": d.Use = "vineyard"; break;
                case "Greenhouse": d.Use = "greenhouse"; break;
                case "Cow": d.Use = "animal"; break;
                case "Tractor": d.Use = "vehicle"; break;
                case "HayBales": d.Use = "deco"; break;
                case "Windmill": d.Use = "windmill"; break;
                default: d.Use = n.ToLowerInvariant(); break;
            }
        }

        static string InfrastructureUse(string name)
        {
            switch (name)
            {
                case "BridgePillar": return "bridge_pillar";
                case "TunnelPortal": return "tunnel_portal";
                case "PowerPylon": return "power_pylon";
                case "SignGantry": return "sign_gantry";
                case "Roadworks": return "roadworks";
                case "NoiseBarrier": return "noise_barrier";
                default: return name.ToLowerInvariant();
            }
        }

        // --- Eigene Assets 2: Fahrzeuge (Models/Vehicles) → AssetCatalog (Busse, Lieferwagen, Dienstfahrzeuge) und Felsklippen
        /// <summary>Fahrzeug nach Name → Verwendung.</summary>
        public static string VehicleUse(string name)
        {
            switch (name)
            {
                case "Bus": return "bus";
                case "DeliveryVan": return "delivery_van";
                case "GarbageTruck": return "garbage_truck";
                case "PoliceCar": return "police_car";
                case "FireTruck": return "fire_truck";
                case "Ambulance": return "ambulance";
                case "Taxi": return "taxi";
                default: return name.ToLowerInvariant();
            }
        }

        /// <summary>Fahrzeug-Verwendungen mit Funktion: Linienbus, kleiner Lieferwagen (Einzelhandel), Dienstfahrzeuge.</summary>
        public static readonly HashSet<string> VehicleUses = new HashSet<string>
            { "bus", "delivery_van", "garbage_truck", "police_car", "fire_truck", "ambulance" };
        /// <summary>Dienstfahrzeuge (Name im AssetCatalog = Modellname der Simulation, <c>ServiceVehicleRules.ModelName</c>).</summary>
        public static readonly HashSet<string> ServiceVehicleUses = new HashSet<string> { "garbage_truck", "police_car", "fire_truck", "ambulance" };
        /// <summary>Felsen-Verwendung „cliff“: Felsklippe an steilen Hängen (NatureDetailView), sonst normaler Felsen.</summary>
        public const string CliffUse = "cliff";
        // --- Ende Eigene Assets 2

        /// <summary>Verwendungen, die eine View im Spiel zeichnet (Rest = „wartet auf Funktion“).</summary>
        public static readonly HashSet<string> AgricultureUses = new HashSet<string>
            { "farmstead", "field", "pasture", "orchard", "vineyard", "greenhouse", "animal", "vehicle", "deco", "windmill" };
        public static readonly HashSet<string> InfrastructureUses = new HashSet<string>
            { "bridge_pillar", "tunnel_portal", "power_pylon", "sign_gantry", "roadworks", "noise_barrier" };

        static readonly string[] Zones = { "ResidentialLow", "ResidentialMedium", "ResidentialHigh", "Mixed", "Retail", "Office", "Industry" };

        // ------------------------------------------------------------ Lesen

        /// <summary>Alle Modelle mit Zuordnung (nach Pfad sortiert, deterministisch).</summary>
        public static List<Desc> Scan()
        {
            var list = new List<Desc>();
            if (!AssetDatabase.IsValidFolder(ModelsRoot)) return list;
            var paths = AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var d = new Desc
                {
                    Path = path,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/').Substring(ModelsRoot.Length).TrimStart('/'),
                };
                ReadJson(d);
                Classify(d);
                list.Add(d);
            }
            return list;
        }

        /// <summary>Relativer Schlüssel „Ordner/Name“ (z. B. „Leisure/Stadium“).</summary>
        public static string Key(Desc d) => string.IsNullOrEmpty(d.Folder) ? d.Name : d.Folder + "/" + d.Name;

        static void ReadJson(Desc d)
        {
            string json = System.IO.Path.ChangeExtension(d.Path, ".json");
            if (!File.Exists(json)) return;
            try
            {
                d.Json = JsonUtility.FromJson<Json>(File.ReadAllText(json));
            }
            catch (Exception e)
            {
                d.JsonError = e.Message;
                Debug.LogWarning($"[OwnModels] {json}: JSON nicht lesbar ({e.Message}) – Ordnerregeln gelten.");
            }
        }

        static void Classify(Desc d)
        {
            var j = d.Json;
            if (j != null && !string.IsNullOrEmpty(j.role))
            {
                d.Role = j.role.Trim().ToLowerInvariant();
                d.Use = j.use;
                d.Placement = j.placement;
                d.Biome = j.biome;
                d.Zone = j.zone;
                d.Level = j.level;
                d.WaitsFor = j.waitsFor;
                d.Target = j.service ?? j.facility ?? j.transport;
                if (j.tags != null) d.Tags = j.tags;
                // Teilangaben ergänzen: Landwirtschaft/Infrastruktur ohne „use“ → aus dem Namen
                if (d.Role == OwnModelRoles.Agriculture && string.IsNullOrEmpty(d.Use)) AgricultureRule(d);
                if (d.Role == OwnModelRoles.Infrastructure && string.IsNullOrEmpty(d.Use)) d.Use = InfrastructureUse(d.Name);
                if (d.Role == OwnModelRoles.Prop && string.IsNullOrEmpty(d.Placement)) d.Placement = PropPlacement(d.Name);
                if (d.Role == OwnModelRoles.Vehicle && string.IsNullOrEmpty(d.Use)) d.Use = VehicleUse(d.Name); // Eigene Assets 2
                if (d.Role == OwnModelRoles.Building && string.IsNullOrEmpty(d.Zone) && Zones.Contains(d.Folder.Replace("Buildings/", "")))
                    d.Zone = d.Folder.Replace("Buildings/", "");
                return;
            }
            FolderRule(d);
        }

        static void FolderRule(Desc d)
        {
            string key = Key(d);
            foreach (var (prefix, waits) in Waiting)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) { d.Role = OwnModelRoles.Waiting; d.WaitsFor = waits; return; }
            foreach (var kv in FacilityFiles)
                if (key == (kv.Key.Contains("/") ? kv.Key : "Utilities/" + kv.Key)) { d.Role = OwnModelRoles.Facility; d.Target = kv.Value; return; }
            foreach (var kv in ServiceFiles)
                if (kv.Value.Contains(key)) { d.Role = OwnModelRoles.Service; d.Target = kv.Key; return; }
            foreach (var kv in TransportFiles)
                if (kv.Value.Contains(key)) { d.Role = OwnModelRoles.Transport; d.Target = kv.Key; return; }

            string top = d.Folder.Split('/')[0];
            switch (top)
            {
                case "Buildings":
                    d.Role = OwnModelRoles.Building;
                    d.Zone = d.Folder.Length > 10 ? d.Folder.Substring(10) : null;
                    return;
                case "Nature":
                    if (d.Name.StartsWith("Tree_")) d.Role = OwnModelRoles.Tree;
                    else if (d.Name.StartsWith("Bush_")) d.Role = OwnModelRoles.Bush;
                    else if (d.Name.StartsWith("Rock_")) d.Role = OwnModelRoles.Rock;
                    break;
                case "Props":
                    if (d.Name.StartsWith("StreetLamp")) d.Role = OwnModelRoles.StreetLamp;
                    else { d.Role = OwnModelRoles.Prop; d.Placement = PropPlacement(d.Name); }
                    return;
                case "People": d.Role = OwnModelRoles.Person; return;
                case "Agriculture": d.Role = OwnModelRoles.Agriculture; AgricultureRule(d); return;
                case "Infrastructure": d.Role = OwnModelRoles.Infrastructure; d.Use = InfrastructureUse(d.Name); return;
                // --- Eigene Assets 2: Fahrzeuge und Gelände-Deko (Taxi/Gelände-Kacheln stehen oben in Waiting)
                case "Vehicles": d.Role = OwnModelRoles.Vehicle; d.Use = VehicleUse(d.Name); return;
                case "Terrain":
                    if (d.Name.StartsWith("Cliff_Straight")) { d.Role = OwnModelRoles.Rock; d.Use = CliffUse; return; }
                    if (d.Name.StartsWith("Rock")) { d.Role = OwnModelRoles.Rock; return; }
                    break;
                // --- Ende Eigene Assets 2
            }
            if (d.Role == null)
            {
                d.Role = OwnModelRoles.Waiting;
                d.WaitsFor = "keine Regel für Ordner/Name – Name.json mit \"role\" anlegen (docs/Modell-Konvention.md)";
            }
        }

        // ------------------------------------------------------------ Abfragen für die Kataloge

        /// <summary>Gebäude einer Zone (Ordner Buildings/&lt;Zone&gt; oder JSON role „building“ mit zone).</summary>
        public static IEnumerable<Desc> Buildings(List<Desc> all, string zone) =>
            all.Where(d => d.Role == OwnModelRoles.Building && string.Equals(d.Zone, zone, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<Desc> WithRole(List<Desc> all, string role) => all.Where(d => d.Role == role);

        /// <summary>Bekannte Dienst-Id oder Dienstart (z. B. „park“ → alle Parks).</summary>
        public static IEnumerable<string> ServiceIdsFor(string target)
        {
            if (string.IsNullOrEmpty(target)) yield break;
            if (ServiceCatalog.Get(target) != null) { yield return target; yield break; }
            foreach (var t in ServiceCatalog.All) if (t.Kind == target || t.Channel == target) yield return t.Id;
        }

        public static bool IsFacilityId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var t in UtilityCatalog.All) if (t.Id == id) return true;
            return FacilityFiles.ContainsValue(id);
        }

        // ------------------------------------------------------------ Bericht

        /// <summary>
        /// Schreibt <c>Models/_Unused.txt</c>: ungenutzte Modelle mit dem Grund („wartet auf Funktion …“), danach die
        /// verwendeten mit Ort im Spiel. Nur bei Änderung neu geschrieben (kein Import-Kreislauf: .txt löst keinen Neubau aus).
        /// </summary>
        public static void WriteReport(List<Desc> all)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Eigene Modelle ohne Verwendung im Spiel (automatisch erzeugt vom OwnModelCatalogBuilder – nicht bearbeiten)");
            sb.AppendLine("# Konvention/JSON-Felder: docs/Modell-Konvention.md");
            sb.AppendLine();
            var unused = all.Where(d => d.UsedBy == null).ToList();
            if (unused.Count == 0) sb.AppendLine("(alle Modelle sind im Spiel)");
            foreach (var g in unused.GroupBy(d => d.WaitsFor ?? "unbekannt").OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"wartet auf: {g.Key}");
                foreach (var d in g) sb.AppendLine($"  {Key(d)}.fbx{(d.JsonError != null ? "  (JSON-Fehler: " + d.JsonError + ")" : "")}");
            }
            sb.AppendLine();
            sb.AppendLine($"# Verwendet ({all.Count - unused.Count} von {all.Count})");
            foreach (var d in all.Where(d => d.UsedBy != null))
                sb.AppendLine($"  {Key(d)}.fbx → {d.UsedBy}{(d.Json != null ? " [json]" : "")}");
            string text = sb.ToString().Replace("\r\n", "\n");
            string full = System.IO.Path.GetFullPath(UnusedReport);
            if (File.Exists(full) && File.ReadAllText(full) == text) return;
            File.WriteAllText(full, text, new UTF8Encoding(false));
            Debug.Log($"[OwnModels] {unused.Count} Modelle ohne Verwendung → {UnusedReport}");
        }
    }
}
