using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Speichern &amp; Laden: Roundtrip je System, <b>Determinismus nach dem Laden</b> (Welt A speichert, B lädt, beide
    /// laufen weiter und bleiben bitgleich), unbekannte/fehlende Abschnitte, Versionsmigration, beschädigte Dateien,
    /// Leistung. Neue Systeme erscheinen automatisch (alle Abschnitte der <see cref="SaveRegistry"/> werden verglichen).
    /// </summary>
    public class SaveLoadTests
    {
        readonly ITestOutputHelper output;
        public SaveLoadTests(ITestOutputHelper output) => this.output = output;

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static bool TryRoad(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 0f, null, b, 0f, t ?? RoadCatalog.CityTwoLane);
            if (!plan.IsValid) return false;
            RoadBuilder.Build(w.Roads, plan);
            return true;
        }

        /// <summary>
        /// Stadt mit allem, was gespeichert werden muss: Straßenraster mit Außenanschluss, Zonen aller Art, Verkehrsregeln,
        /// Kraftwerk/Wasser, Dienste (inkl. Müll), Terraforming, geänderte Steuern, echte Kasse. Bürger, Firmen, Lkw und
        /// Fahrzeuge entstehen beim Laufen.
        /// </summary>
        static SimulationWorld BuildCity(int days, bool generated = false, int seed = 777)
        {
            var settings = new WorldSettings { UnlimitedFunds = false, StartingFunds = 2_000_000 };
            if (generated)
            {
                settings.GenerateTerrain = true;
                settings.GenerateWorld = true;
                settings.WorldSeed = 99;
                settings.Planet = new World.PlanetSettings { Width = 96, Height = 48, TargetRegions = 60, TargetCountries = 8 };
            }
            var w = SimulationWorld.CreateDefault(settings, seed);
            // Hauptachse bis zum Kartenrand (Außenanschluss) + Querstraßen
            TryRoad(w, new Vector2(-500, 0), new Vector2(1020, 0), RoadCatalog.CityFourLane);
            TryRoad(w, new Vector2(-500, 160), new Vector2(500, 160));
            for (int x = -400; x <= 400; x += 200) TryRoad(w, new Vector2(x, 0), new Vector2(x, 160));
            w.City.Paint(new Vector2(-300, 80), 140f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(-300, 200), 120f, ZoneType.ResidentialMedium);
            w.City.Paint(new Vector2(100, 80), 60f, ZoneType.Retail);
            w.City.Paint(new Vector2(300, 80), 60f, ZoneType.Office);
            w.City.Paint(new Vector2(250, -40), 200f, ZoneType.Industry);
            w.City.Paint(new Vector2(0, 200), 50f, ZoneType.Mixed);

            // Verkehrsregeln: Ampel, Tempolimit, Lkw-Verbot
            var junction = w.Roads.Nodes.FirstOrDefault(n => n.SegmentIds.Count >= 3);
            if (junction != null)
            {
                w.Traffic.Rules.SetControl(junction.Id, JunctionControl.TrafficLights);
                w.Traffic.Rules.SetGreenSeconds(junction.Id, 25f);
            }
            var side = w.Roads.Segments.First(s => s.Type == RoadCatalog.CityTwoLane);
            w.Traffic.Rules.SetSpeedLimit(side.Id, 30f);
            w.Traffic.Rules.SetBanned(side.Id, VehicleClass.Bus);

            Run(w, SimulationClock.TicksPerDay); // erste Gebäude
            w.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(700, -120));
            w.Utilities.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(-600, 60));
            w.Utilities.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-600, -80));
            var line = w.Utilities.PlanLine(UtilityKind.Power, new Vector2(-600, -60), new Vector2(-500, -10));
            if (line.IsValid) w.Utilities.BuildLine(line);
            // Netze selbst verlegt: Umspannwerk, Hochspannung, Erdkabel entlang aller Stadtstraßen (Zustände im Determinismus-Test)
            var sub = w.Utilities.TryBuildFacility(UtilityCatalog.Substation, new Vector2(700, -230));
            if (sub != null)
            {
                var hv = w.Utilities.PlanLine(UtilityCatalog.HighVoltageLine, new Vector2(700, -138), new Vector2(700, -220));
                if (hv.IsValid) w.Utilities.BuildLine(hv);
            }
            w.Utilities.BuildAlongRoads(w.Utilities.PlanAlongRoads(UtilityCatalog.UndergroundCable));
            w.Services.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-100, -40));
            w.Services.TryBuild(ServiceCatalog.Clinic, new Vector2(100, -40));
            w.Services.TryBuild(ServiceCatalog.PrimarySchool, new Vector2(-500, 220));
            w.Services.TryBuild(ServiceCatalog.ParkSmall, new Vector2(-150, 260));
            w.Services.TryBuild(ServiceCatalog.Landfill, new Vector2(600, 160));
            w.Services.TryBuild(ServiceCatalog.Newspaper, new Vector2(300, -40));          // Medien
            w.Utilities.TryBuildFacility(UtilityCatalog.SewagePlant, new Vector2(-700, -160)); // Abwasser
            // ÖPNV: Depot, Haltestellen, Ringlinie mit 2 Bussen
            if (w.Transit != null)
            {
                w.Transit.TryBuildDepot(Simulation.Transit.TransitCatalog.BusDepot, new Vector2(-300, -40));
                var stops = new[] { new Vector2(-300, 0), new Vector2(200, 0), new Vector2(200, 160), new Vector2(-300, 160) }
                    .Select(p => w.Transit.TryBuildStop(p, bothSides: true)).Where(s => s != null).Select(s => s.Id).ToList();
                if (stops.Count >= 2) w.Transit.CreateLine(stops, loop: true, vehicles: 2);
            }
            w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, new Vector2(-800, -600), 40f, 2f));
            w.Economy.SetTaxRate(TaxKind.Residential, 0.12f);
            w.Economy.SetTaxRate(TaxKind.Industry, 0.08f);
            // Produktion lenken: Schwerpunkt + Förderung (Betriebswechsel, Förderbuchung im Determinismus-Test)
            w.Policy.Paint(new Vector2(250, -40), 120f, Simulation.Goods.IndustryFocus.Processing);
            w.Policy.SetSubsidy(Simulation.Goods.IndustryFocus.Processing, 2);
            w.Policy.SetSubsidy(Simulation.Goods.IndustryFocus.ConsumerGoods, 1);
            Run(w, SimulationClock.TicksPerDay * (days - 1));
            return w;
        }

        /// <summary>Vergleicht alle Abschnitte und meldet, welche Systeme abweichen.</summary>
        static void AssertSameState(SimulationWorld a, SimulationWorld b, string when)
        {
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            Assert.Equal(ha.Select(x => x.Key), hb.Select(x => x.Key));
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, $"{when}: abweichende Abschnitte: {string.Join(", ", diff)}");
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));
        }

        /// <summary>Zusätzliche Beobachtungen außerhalb des Speicherformats (falls ein Feld beim Schreiben fehlte, wirkt es hier).</summary>
        static string Observe(SimulationWorld w)
        {
            var parts = new List<string>
            {
                $"tick {w.Clock.Tick}",
                $"pop {w.Citizens.Stats.Population} hh {w.Citizens.Stats.Households} in {w.Citizens.Stats.CommutersIn} sat {w.Citizens.Stats.AverageSatisfaction:R}",
                $"veh {w.Traffic.Vehicles.Count} time {w.Traffic.Time:R} spawned {w.Traffic.Stats.Spawned}",
                $"bld {w.City.Buildings.Count()} demand {w.City.Demand[ZoneType.ResidentialLow]:R}",
                $"cash {w.Economy.Balance:R} firms {w.Goods.Firms.Count} ship {w.Goods.Shipments.Count}",
                $"lv {w.LandValue.Layer.RawValues.Sum():R} noise {w.Environment.Noise.RawValues.Sum():R} air {w.Environment.AirPollution.RawValues.Sum():R}",
            };
            foreach (var v in w.Traffic.Vehicles) parts.Add($"v{v.Id}:{v.Position.X:R},{v.Position.Y:R},{v.Speed:R}");
            foreach (var c in w.Citizens.Citizens) parts.Add($"c{c.Id}:{c.Activity}:{c.Location.BuildingId}:{c.Satisfaction:R}:{c.Mood:R}");
            return string.Join("|", parts);
        }

        // ------------------------------------------------------------ Determinismus (Kernanforderung)

        [Fact]
        public void Determinism_AfterLoad_WorldsStayIdentical()
        {
            var a = BuildCity(days: 6);
            // Zeitmodell: um Mitternacht fährt kaum jemand (Fahrten dauern realistisch kurz) → bis zum Morgenverkehr weiter und
            // zusätzlich mitten in einen Tick hinein (Speichern zwischen zwei Verkehrsschritten)
            for (int i = 0; i < SimulationClock.TicksPerDay && a.Traffic.Vehicles.Count(v => v.TripId != 0) < 3; i++) a.Step();
            for (int i = 0; i < 37; i++) a.Substep();
            Assert.False(a.AtTickBoundary);
            Assert.True(a.Citizens.Citizens.Count > 50, $"nur {a.Citizens.Citizens.Count} Bürger");
            Assert.True(a.Traffic.Vehicles.Count > 0, "Fahrzeuge erwartet");
            Assert.True(a.Goods.Firms.Count > 0, "Firmen erwartet");

            var bytes = SaveGame.SaveToBytes(a, "Test");
            var b = SaveGame.Load(bytes);
            AssertSameState(a, b, "direkt nach dem Laden");
            Assert.Equal(Observe(a), Observe(b));

            // Beide weiterlaufen lassen, unterwegs dieselben Spieleraktionen
            for (int day = 1; day <= 4; day++)
            {
                Run(a, SimulationClock.TicksPerDay);
                Run(b, SimulationClock.TicksPerDay);
                if (day == 2)
                    foreach (var w in new[] { a, b })
                    {
                        TryRoad(w, new Vector2(-500, 160), new Vector2(-500, 320));
                        w.City.Paint(new Vector2(-560, 260), 40f, ZoneType.ResidentialHigh);
                        w.Services.TryBuild(ServiceCatalog.FireStation, new Vector2(500, -40));
                        w.Economy.SetTaxRate(TaxKind.Commercial, 0.14f);
                    }
                Assert.Equal(Observe(a), Observe(b));
            }
            AssertSameState(a, b, "nach 4 weiteren Tagen");
            output.WriteLine($"Bürger {a.Citizens.Citizens.Count}, Fahrzeuge {a.Traffic.Vehicles.Count}, Firmen {a.Goods.Firms.Count}, " +
                             $"Lieferungen {a.Goods.Shipments.Count}, Spielstand {bytes.Length / 1024} KB");
        }

        [Fact]
        public void Determinism_GeneratedWorld_PlanetAndResources()
        {
            var a = BuildCity(days: 3, generated: true, seed: 4242);
            Assert.NotNull(a.Planet);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.NotNull(b.Planet);
            Assert.Equal(a.Planet.StartRegionId, b.Planet.StartRegionId);
            AssertSameState(a, b, "direkt nach dem Laden");
            Assert.NotNull(a.Trade);
            // Handel: ein offener Command in der Warteschlange muss mitwandern
            int npc = a.Trade.Nations.First(n => !n.IsPlayer).Id;
            foreach (var x in new[] { a, b })
                x.Trade.Submit(new Simulation.Trade.SetTariffCommand(x.Trade.PlayerId, npc, 0.15));
            AssertSameState(a, b, "mit Command in der Warteschlange");
            var c = SaveGame.Load(SaveGame.SaveToBytes(a));
            Run(a, SimulationClock.TicksPerDay * 3);
            Run(b, SimulationClock.TicksPerDay * 3);
            Run(c, SimulationClock.TicksPerDay * 3);
            AssertSameState(a, b, "nach 3 weiteren Tagen");
            AssertSameState(a, c, "Command-Warteschlange gespeichert");
            Assert.Equal(0.15, a.Trade.TariffRate(a.Trade.PlayerId, npc), 6);
            Assert.Equal(a.Planet.Economy.Nations[0].Stock, b.Planet.Economy.Nations[0].Stock);
        }

        [Fact]
        public void Determinism_SaveAtManyMoments_AlsoRightAfterBuilding()
        {
            var a = BuildCity(days: 3, seed: 31337);
            int[] gaps = { 1, 17, 59, 113, 7, 91 };
            for (int i = 0; i < gaps.Length; i++)
            {
                Run(a, gaps[i]);   // mitten im Tag, mitten in der Stunde, mitten im Bedürfnis-Durchlauf …
                if (i == 3)
                {
                    // Spieleraktion ohne Tick danach (wie im Pausenmodus): Spurgraph ist noch nicht aktualisiert
                    TryRoad(a, new Vector2(-400, 0), new Vector2(-400, -150));
                    a.City.Paint(new Vector2(-450, -80), 30f, ZoneType.ResidentialLow);
                }
                var b = SaveGame.Load(SaveGame.SaveToBytes(a));
                AssertSameState(a, b, $"Moment {i}: direkt nach dem Laden");
                var probe = SaveGame.Load(SaveGame.SaveToBytes(a)); // a läuft unverändert weiter, Vergleich mit Kopie
                Run(b, 150);
                Run(probe, 150);
                AssertSameState(b, probe, $"Moment {i}: nach 150 Ticks");
                Run(a, 150);
                AssertSameState(a, b, $"Moment {i}: Original gegen Geladenes nach 150 Ticks");
            }
        }

        [Fact]
        public void SaveDoesNotChangeTheWorld()
        {
            var a = BuildCity(days: 3);
            var c = BuildCity(days: 3); // gleiche Eingaben → gleiche Welt
            AssertSameState(a, c, "Ausgang");
            for (int i = 0; i < 5; i++) SaveGame.SaveToBytes(a);
            Run(a, SimulationClock.TicksPerDay);
            Run(c, SimulationClock.TicksPerDay);
            AssertSameState(a, c, "Speichern darf den Verlauf nicht ändern");
        }

        [Fact]
        public void LoadedWorld_CanBeSavedAndLoadedAgain()
        {
            var a = BuildCity(days: 3);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Run(b, 37);
            var c = SaveGame.Load(SaveGame.SaveToBytes(b));
            Run(a, 37);
            AssertSameState(a, c, "zweimal geladen");
        }

        // ------------------------------------------------------------ Roundtrip je System

        [Fact]
        public void Roundtrip_EverySystem()
        {
            var a = BuildCity(days: 4);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));

            // Jeder Abschnitt bitgleich (alle registrierten Systeme)
            var ids = SaveRegistry.Collect(a).Select(s => s.SaveId).ToList();
            foreach (var id in new[] { "terrain", "environment", "resources", "roads", "rules", "city", "traffic", "citizens", "utilities",
                                       "waterquality", "services", "media", "transit", "landvalue", "goods", "policy", "economy" })
                Assert.Contains(id, ids);
            var sb = SaveRegistry.Collect(b).ToDictionary(s => s.SaveId);
            foreach (var s in SaveRegistry.Collect(a))
                Assert.True(SaveGame.StateHash(s) == SaveGame.StateHash(sb[s.SaveId]), $"Abschnitt {s.SaveId} weicht ab");

            // Uhr, Einstellungen, Seed
            Assert.Equal(a.Clock.Tick, b.Clock.Tick);
            Assert.Equal(a.Seed, b.Seed);
            Assert.False(b.Settings.UnlimitedFunds);
            // Gelände inkl. Terraforming und natürlicher Höhe
            Assert.Equal(a.Terrain.HeightAt(new Vector2(-800, -600)), b.Terrain.HeightAt(new Vector2(-800, -600)));
            Assert.Equal(a.Terrain.NaturalHeightAt(new Vector2(-800, -600)), b.Terrain.NaturalHeightAt(new Vector2(-800, -600)));
            Assert.True(a.Terrain.HeightAt(new Vector2(-800, -600)) > 0.5f, "Terraforming erwartet");
            // Straßen (Ids, Typen, Profile) und abgeleiteter Spurgraph
            Assert.Equal(a.Roads.Segments.Select(s => (s.Id, s.Type.Id, s.Start.Id, s.End.Id)), b.Roads.Segments.Select(s => (s.Id, s.Type.Id, s.Start.Id, s.End.Id)));
            Assert.Equal(a.Traffic.Lanes.AllLanes.Count(), b.Traffic.Lanes.AllLanes.Count());
            // Verkehrsregeln
            var light = a.Roads.Nodes.First(n => a.Traffic.Rules.ControlAt(n.Id) == JunctionControl.TrafficLights);
            Assert.Equal(JunctionControl.TrafficLights, b.Traffic.Rules.ControlAt(light.Id));
            Assert.Equal(25f, b.Traffic.Rules.GetNode(light.Id).GreenSeconds);
            // Stadt
            Assert.Equal(a.City.Buildings.Select(x => (x.Id, x.Level, x.Condition, x.Construction)), b.City.Buildings.Select(x => (x.Id, x.Level, x.Condition, x.Construction)));
            Assert.Equal(a.City.ZoneAt(new Vector2(-300, 80)), b.City.ZoneAt(new Vector2(-300, 80)));
            Assert.Equal(a.City.Grid.ValidCellCount, b.City.Grid.ValidCellCount);
            // Bürger: niemand verloren, Autofahrer haben ihr Fahrzeug
            Assert.Equal(a.Citizens.Citizens.Count, b.Citizens.Citizens.Count);
            foreach (var c in b.Citizens.Citizens.Where(c => c.HasCarTrip))
                Assert.NotNull(b.Citizens.VehicleOf(c));
            var ca = a.Citizens.Citizens.First(c => c.Household != null);
            var cb = b.Citizens.Get(ca.Id);
            Assert.Equal(ca.FullName, cb.FullName);
            Assert.Equal(ca.Personality.Adventurous, cb.Personality.Adventurous);
            Assert.Equal(ca.Household.Id, cb.Household.Id);
            Assert.Equal(ca.Memories.Count, cb.Memories.Count);
            // Verkehr
            Assert.Equal(a.Traffic.Vehicles.Select(v => (v.Id, v.S, v.TripId)), b.Traffic.Vehicles.Select(v => (v.Id, v.S, v.TripId)));
            // Versorgung, Dienste, Bodenwert
            Assert.Equal(a.Utilities.FacilityCount, b.Utilities.FacilityCount);
            Assert.Equal(a.Utilities.Networks(UtilityKind.Power).Count, b.Utilities.Networks(UtilityKind.Power).Count);
            Assert.Equal(a.Services.FacilityCount, b.Services.FacilityCount);
            var home = a.City.Buildings.First(x => x.IsConstructed && x.Households > 0);
            Assert.Equal(a.Services.Coverage(ServiceKinds.Police, home), b.Services.Coverage(ServiceKinds.Police, b.City.GetBuilding(home.Id)));
            Assert.Equal(a.Services.GarbageLevel(home), b.Services.GarbageLevel(b.City.GetBuilding(home.Id)));
            Assert.Equal(a.LandValue.ValueOf(home), b.LandValue.ValueOf(b.City.GetBuilding(home.Id)));
            // Warenketten, Kasse
            Assert.Equal(a.Goods.Firms.Select(f => (f.BuildingId, f.Recipe.Id, f.Cash)), b.Goods.Firms.Select(f => (f.BuildingId, f.Recipe.Id, f.Cash)));
            Assert.Equal(a.Goods.Markets.Select(m => m.LocalPrice), b.Goods.Markets.Select(m => m.LocalPrice));
            Assert.Equal(a.Economy.Balance, b.Economy.Balance);
            Assert.Equal(a.Economy.TaxRate(TaxKind.Residential), b.Economy.TaxRate(TaxKind.Residential));
            Assert.Equal(a.Economy.History.Count, b.Economy.History.Count);
            // ÖPNV: Busse fahren als Fahrzeuge, Fahrgäste bleiben ihren Bürgern zugeordnet
            Assert.Equal(a.Transit.Buses.Count, b.Transit.Buses.Count);
            Assert.True(a.Transit.Lines.Count > 0, "Linie erwartet");
            foreach (var bus in b.Transit.Buses) if (bus.Vehicle != null) Assert.Contains(bus.Vehicle, b.Traffic.Vehicles);
            Assert.Equal(a.Citizens.ActiveTransitRides, b.Citizens.ActiveTransitRides);
            Assert.Equal(a.Media.Recent.Count, b.Media.Recent.Count);
        }

        [Fact]
        public void Info_HeaderReadableWithoutLoading()
        {
            var a = BuildCity(days: 2);
            var bytes = SaveGame.SaveToBytes(a, "Neustadt");
            using (var ms = new MemoryStream(bytes))
            {
                var info = SaveContainer.ReadInfo(ms);
                Assert.Equal("Neustadt", info.Name);
                Assert.Equal(a.Clock.Tick, info.Tick);
                Assert.Equal(a.Citizens.Stats.Population, info.Population);
                Assert.Equal(a.Economy.Balance, info.Balance);
                Assert.Equal(SaveGame.GameVersion, info.GameVersion);
                Assert.Equal(SaveContainer.FormatVersion, info.FormatVersion);
            }
        }

        [Fact]
        public void File_SaveAndLoadThroughDisk()
        {
            var a = BuildCity(days: 2);
            string dir = Path.Combine(Path.GetTempPath(), "cb-save-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(dir, "stand.cbsave");
                SaveGame.SaveToFile(a, path, "Datei");
                SaveGame.SaveToFile(a, path, "Datei"); // überschreiben
                Assert.Equal("Datei", SaveGame.ReadInfo(path).Name);
                var b = SaveGame.LoadFromFile(path);
                AssertSameState(a, b, "über die Datei");
                Assert.False(File.Exists(path + ".tmp"));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ------------------------------------------------------------ Abschnitte: unbekannt, fehlend, Migration

        [Fact]
        public void UnknownSection_IsSkipped()
        {
            var a = BuildCity(days: 2);
            var c = SaveGame.Capture(a);
            c.Set("zukunft_transit", 7, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            c.Sections.Insert(2, new SaveSection { Id = "noch_ein_neues_system", Version = 1, Data = new byte[1000] });
            var b = SaveGame.Load(c.ToBytes());
            AssertSameState(a, b, "mit unbekannten Abschnitten");
        }

        [Fact]
        public void MissingSection_UsesDefaultState()
        {
            var a = BuildCity(days: 3);
            var c = SaveGame.Capture(a);
            Assert.True(c.Remove("goods"));
            Assert.True(c.Remove("landvalue"));
            var b = SaveGame.Load(c.ToBytes());
            Assert.Empty(b.Goods.Firms);                      // Standardzustand: keine Firmen …
            Assert.Equal(0, b.LandValue.Cycles);
            Assert.Equal(a.City.Buildings.Count(), b.City.Buildings.Count()); // … der Rest ist da
            Run(b, SimulationClock.TicksPerDay);               // und läuft weiter: Firmen entstehen für vorhandene Gebäude neu
            Assert.NotEmpty(b.Goods.Firms);
            Assert.True(b.LandValue.Cycles >= 1);
        }

        /// <summary>
        /// Netze selbst verlegt: alter Abschnitt „utilities“ v1 (ohne Leitungsarten, Straßen trugen Strom/Wasser) →
        /// Leitung gilt als bisherige Art (Freileitung im Ortsnetz), Übergangsfrist, Netze nach neuen Regeln, Stadt über
        /// „Entlang Straßen verlegen“ wieder versorgbar.
        /// </summary>
        [Fact]
        public void Migration_UtilitiesV1_LinesKeepOldKind_GraceAndAlongRoads()
        {
            var a = BuildCity(days: 1);
            byte[] v1;
            using (var ms = new MemoryStream())
            {
                var w = new SaveWriter(ms);
                w.Int(1); w.Int(2);                       // nächste Bauwerk-/Leitungs-Id
                w.Int(-1); w.Int(-1); w.Int(-1);          // Neuaufbau-Versionen
                w.Int(1); w.Int(0);                       // StructureVersion, StatusVersion
                w.Bool(true); w.Int(0); w.Bool(false);    // Verlassen an, 0 verlassen, kein Abgas
                w.Int(0); w.Int(0);                       // neu trassiert / abgerissen
                w.Bool(true); w.Float(0.002f); w.Int(0); w.Int(-1); // Störfälle
                w.IntDictionary(new Dictionary<int, int>(), w.Int);
                w.Int(0); w.Int(0);                       // keine Bauwerke (StableMap: 0 Plätze, 0 frei)
                w.Int(1); w.Bool(true);                   // 1 Leitung (v1: ohne Leitungsart)
                w.Int(1); w.Name(UtilityKind.Power); w.Vector2(new Vector2(-900, 900)); w.Vector2(new Vector2(-800, 900));
                w.Int(-1); w.Int(0); w.Int(0);            // Netz, Revision, keine Masten
                w.Int(0);                                 // freie Plätze
                w.Int(0);                                 // kein Versorgungsstatus
                w.Int(0);                                 // keine Netze
                w.Flush();
                v1 = ms.ToArray();
            }
            var c = SaveGame.Capture(a);
            c.Set("utilities", 1, v1);
            var b = SaveGame.Load(c.ToBytes());
            var u = b.Utilities;
            Assert.Same(UtilityCatalog.OverheadLine, u.GetLine(1).Type);
            Assert.Equal(UtilitySystem.MigrationGraceAfterV1, u.MigrationGraceTicks);
            Run(b, 20);
            Assert.True(u.MigrationGraceTicks < UtilitySystem.MigrationGraceAfterV1);
            var plan = u.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.True(plan.BuildingsReached > 0);
            Assert.True(u.BuildAlongRoads(plan) > 0);
        }

        [Fact]
        public void Migration_OldRulesSectionWithoutGreenTime()
        {
            var a = BuildCity(days: 1);
            var light = a.Roads.Nodes.First(n => a.Traffic.Rules.ControlAt(n.Id) == JunctionControl.TrafficLights);
            var seg = a.Roads.Segments.First(s => a.Traffic.Rules.GetSegment(s.Id) != null);

            // Künstlicher Abschnitt „rules“ in Version 1 (vor der Grünzeit je Ampel)
            byte[] v1;
            using (var ms = new MemoryStream())
            {
                var w = new SaveWriter(ms);
                w.Int(5);                                 // Version der Regeln
                w.Int(1);                                 // 1 Knoten
                w.Int(light.Id); w.Name(JunctionControl.TrafficLights);
                w.Int(0);                                 // keine Schilder
                w.Int(1); w.Int(seg.Id); w.Int(seg.Id); w.Name(VehicleClass.Truck); // ein Abbiegeverbot (Wenden für Lkw)
                w.Int(1);                                 // 1 Segment
                w.Int(seg.Id); w.Bool(true); w.Name(VehicleClass.None); w.Float(50f);
                w.Flush();
                v1 = ms.ToArray();
            }
            var c = SaveGame.Capture(a);
            c.Set("rules", 1, v1);
            var b = SaveGame.Load(c.ToBytes());
            var rules = b.Traffic.Rules;
            Assert.Equal(JunctionControl.TrafficLights, rules.ControlAt(light.Id));
            Assert.Equal(20f, rules.GetNode(light.Id).GreenSeconds);  // Standard aus der Migration
            Assert.Equal(VehicleClass.Truck, rules.GetTurnBan(light.Id, seg.Id, seg.Id));
            Assert.True(rules.GetSegment(seg.Id).Closed);
            Assert.Equal(50f, rules.GetSegment(seg.Id).SpeedLimitKmh);
            Assert.Equal(5, rules.Version);
            Run(b, 20); // läuft
        }

        [Fact]
        public void NewerSectionVersion_IsRejectedWithMessage()
        {
            var a = BuildCity(days: 1);
            var c = SaveGame.Capture(a);
            c.Find("city").Version = 99;
            var e = Assert.Throws<SaveLoadException>(() => SaveGame.Load(c.ToBytes()));
            Assert.Contains("city", e.Message);
            Assert.Contains("neueren", e.Message);
        }

        // ------------------------------------------------------------ Beschädigte Dateien

        [Fact]
        public void CorruptFiles_GiveCleanErrors()
        {
            var a = BuildCity(days: 1);
            var good = SaveGame.SaveToBytes(a);

            void Expect(byte[] data, string what)
            {
                var e = Assert.Throws<SaveLoadException>(() => SaveGame.Load(data));
                Assert.False(string.IsNullOrWhiteSpace(e.Message), what);
                output.WriteLine($"{what}: {e.Message}");
            }

            Expect(new byte[0], "leer");
            Expect(new byte[] { 1, 2, 3 }, "zu kurz");
            Expect(System.Text.Encoding.UTF8.GetBytes("Hallo, ich bin kein Spielstand. ".PadRight(400, 'x')), "Text");
            var magic = (byte[])good.Clone(); magic[0] = (byte)'X';
            Expect(magic, "falsche Kennung");
            Expect(good.Take(good.Length / 2).ToArray(), "abgeschnitten");
            Expect(good.Take(good.Length - 1).ToArray(), "letztes Byte fehlt");
            var flipped = (byte[])good.Clone(); flipped[good.Length - 100] ^= 0x5A;
            Expect(flipped, "Byte im komprimierten Teil verändert");
            var header = (byte[])good.Clone(); header[5] = 0xFF; header[4] = 0xFF;
            Expect(header, "Formatversion unsinnig");
            var rnd = new Random(5);
            for (int i = 0; i < 20; i++)
            {
                var noise = (byte[])good.Clone();
                for (int k = 0; k < 8; k++) noise[rnd.Next(good.Length)] = (byte)rnd.Next(256);
                Assert.Throws<SaveLoadException>(() => SaveGame.Load(noise));
            }

            // Gültige Prüfsumme, aber unsinniger Abschnittsinhalt → Meldung nennt den Abschnitt
            foreach (var id in new[] { "roads", "city", "citizens", "traffic", "goods", "terrain" })
            {
                var c = SaveGame.Capture(a);
                var data = c.Find(id).Data;
                var broken = new byte[data.Length];
                for (int k = 0; k < broken.Length; k++) broken[k] = (byte)(data[k] ^ (k * 37 + 11));
                c.Set(id, c.Find(id).Version, broken);
                var e = Assert.Throws<SaveLoadException>(() => SaveGame.Load(c.ToBytes()));
                Assert.Contains(id, e.Message);
                var cut = SaveGame.Capture(a);
                cut.Set(id, cut.Find(id).Version, data.Take(data.Length / 3).ToArray());
                e = Assert.Throws<SaveLoadException>(() => SaveGame.Load(cut.ToBytes()));
                Assert.Contains(id, e.Message);
            }
            var noWorld = SaveGame.Capture(a);
            noWorld.Remove(SaveGame.WorldSectionId);
            Assert.Throws<SaveLoadException>(() => SaveGame.Load(noWorld.ToBytes()));
        }

        // ------------------------------------------------------------ Bausteine

        [Fact]
        public void CompatRandom_MatchesSystemRandom()
        {
            foreach (int seed in new[] { 0, 1, 12345, -7, int.MaxValue, int.MinValue, 4711 })
            {
                var a = new Random(seed);
                var b = new CompatRandom(seed);
                for (int i = 0; i < 2000; i++)
                {
                    switch (i % 5)
                    {
                        case 0: Assert.Equal(a.NextDouble(), b.NextDouble()); break;
                        case 1: Assert.Equal(a.Next(1000), b.Next(1000)); break;
                        case 2: Assert.Equal(a.Next(-50, 70), b.Next(-50, 70)); break;
                        case 3: Assert.Equal(a.Next(), b.Next()); break;
                        default: Assert.Equal(a.Next(int.MinValue, int.MaxValue), b.Next(int.MinValue, int.MaxValue)); break;
                    }
                }
            }
            // Zustand speichern und fortsetzen
            var r = new CompatRandom(99);
            for (int i = 0; i < 100; i++) r.Next();
            var ms = new MemoryStream();
            r.Write(new SaveWriter(ms));
            var copy = new CompatRandom(1);
            ms.Position = 0;
            copy.Read(new SaveReader(ms));
            for (int i = 0; i < 100; i++) Assert.Equal(r.NextDouble(), copy.NextDouble());
        }

        [Fact]
        public void StableMap_IteratesLikeDictionary_AndRestoresLayout()
        {
            var rnd = new Random(3);
            var dict = new Dictionary<int, int>();
            var map = new StableMap<int, int>();
            int next = 1;
            for (int step = 0; step < 5000; step++)
            {
                if (dict.Count > 0 && rnd.NextDouble() < 0.45)
                {
                    int key = dict.Keys.ElementAt(rnd.Next(dict.Count));
                    Assert.Equal(dict.Remove(key), map.Remove(key));
                }
                else
                {
                    int v = rnd.Next();
                    dict[next] = v;
                    map[next] = v;
                    next++;
                }
                if (step % 97 == 0) Assert.Equal(dict.ToList(), map.ToList());
            }
            Assert.Equal(dict.ToList(), map.ToList());

            // Belegung speichern/laden: gleiche Reihenfolge auch für künftige Einfügungen
            var ms = new MemoryStream();
            var w = new SaveWriter(ms);
            map.WriteLayout(w, (k, v) => { w.Int(k); w.Int(v); });
            w.Flush();
            ms.Position = 0;
            var r = new SaveReader(ms);
            var copy = new StableMap<int, int>();
            copy.ReadLayout(r, () => new KeyValuePair<int, int>(r.Int(), r.Int()));
            for (int i = 0; i < 50; i++) { map[next] = i; copy[next] = i; dict[next] = i; next++; }
            Assert.Equal(dict.ToList(), copy.ToList());
            Assert.Equal(map.ToList(), copy.ToList());
        }

        // ------------------------------------------------------------ Leistung (nur Information)

        [Fact]
        public void Performance_LargeCity_SaveAndLoad()
        {
            var w = SimulationWorld.CreateDefault();
            for (int i = 0; i < 5; i++) TryRoad(w, new Vector2(-900, -400 + i * 200), new Vector2(1020, -400 + i * 200));
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-600, -400 + i * 200), 290f, ZoneType.ResidentialMedium);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-200, -400 + i * 200), 75f, ZoneType.Office);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(250, -400 + i * 200), 320f, ZoneType.Industry);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(700, -400 + i * 200), 110f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 2);
            var housing = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).ToList();
            Assert.NotEmpty(housing);
            int k = 0;
            while (w.Citizens.Citizens.Count < 15000) w.Citizens.CreateHousehold(housing[k++ % housing.Count]);
            w.Citizens.ForceManage();
            Run(w, SimulationClock.TicksPerDay);

            SaveGame.SaveToBytes(w); // Aufwärmen (JIT)
            var sw = Stopwatch.StartNew();
            var container = SaveGame.Capture(w, "Großstadt");
            long capture = sw.ElapsedMilliseconds;
            var bytes = container.ToBytes();
            long save = sw.ElapsedMilliseconds;
            sw.Restart();
            var loaded = SaveGame.Load(bytes);
            long load = sw.ElapsedMilliseconds;

            output.WriteLine($"{w.Citizens.Citizens.Count} Bürger, {w.City.Buildings.Count()} Gebäude, {w.Goods.Firms.Count} Firmen, " +
                             $"{w.Traffic.Vehicles.Count} Fahrzeuge, {w.Roads.SegmentCount} Segmente");
            output.WriteLine($"Speichern: {save} ms (davon Abschnitte schreiben {capture} ms), Laden: {load} ms (inkl. Welt neu erzeugen)");
            output.WriteLine($"Datei: {bytes.Length / 1024.0:0} KB komprimiert, {container.Sections.Sum(s => s.Data.Length) / 1024.0:0} KB roh");
            foreach (var s in container.Sections.OrderByDescending(s => s.Data.Length))
                output.WriteLine($"  {s.Id,-12} {s.Data.Length / 1024.0,8:0.0} KB");
            Assert.Equal(w.Citizens.Citizens.Count, loaded.Citizens.Citizens.Count);
            Assert.True(save < 20000 && load < 20000, "grobe Obergrenze, damit Ausreißer auffallen");
        }
    }
}
