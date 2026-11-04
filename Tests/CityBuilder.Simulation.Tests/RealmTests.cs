using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Realm;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Etappe 7 Reich: weitere Städte, aggregierte Simulation, Wechseln mit Speichern, interne Lieferungen, Land gründen, NPC-Expansion.</summary>
    public class RealmTests
    {
        readonly ITestOutputHelper output;
        public RealmTests(ITestOutputHelper output) { this.output = output; }

        static readonly PlanetSettings Small = new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 };
        const int WorldSeed = 31;

        /// <summary>Freie Startregion mit mindestens zwei freien Nachbarregionen (gleiche Ids in jeder Welt mit diesem Seed).</summary>
        static readonly Lazy<(int start, int[] free)> Layout = new Lazy<(int, int[])>(() =>
        {
            var p = PlanetGenerator.Generate(WorldSeed, Small);
            foreach (var r in p.Regions.OrderBy(r => r.Id))
            {
                if (!r.IsFree || r.Cells.Count < 4) continue;
                var free = r.Neighbors.Where(q => p.Regions[q].IsFree && p.Regions[q].Cells.Count >= 2).OrderBy(q => q).ToArray();
                if (free.Length >= 2) return (r.Id, free);
            }
            throw new InvalidOperationException("keine passende Startregion");
        });

        static SimulationWorld NewRealm(string name = "Bergheim", bool town = false)
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = WorldSeed, Planet = Small, StartRegion = Layout.Value.start, CityName = name,
                GenerateTerrain = false, SizeMeters = 1024f,
            });
            if (town)
            {
                w.Utilities.AbandonmentEnabled = false;
                var plan = RoadBuilder.Plan(w.Roads, new Vector2(-300, 0), 0f, null, new Vector2(300, 0), 0f, RoadCatalog.CityTwoLane);
                Assert.True(plan.IsValid, plan.Error.ToString());
                RoadBuilder.Build(w.Roads, plan);
                w.City.Paint(new Vector2(-100, 45), 50f, ZoneType.ResidentialLow);
                w.City.Paint(new Vector2(150, 45), 40f, ZoneType.Retail);
                var hall = w.Services.PlanAligned(ServiceCatalog.TownHall, new Vector2(-150, -30));
                Assert.True(hall.IsValid, hall.Error.ToString());
                Assert.NotNull(w.Services.Build(hall));
                Days(w, 2);
                Assert.True(w.Citizens.Stats.Population > 10);
            }
            return w;
        }

        static void Days(SimulationWorld w, int days)
        {
            for (int i = 0; i < SimulationClock.TicksPerDay * days; i++) w.Step();
        }

        /// <summary>Mutterstadt erfüllt die Gründungsbedingungen (Rathaus steht; Einwohnerzahl für den Test gesetzt).</summary>
        static RealmCity Found(SimulationWorld w, int region, string name, int cell = -1)
        {
            w.Citizens.Stats.Population = Math.Max(w.Citizens.Stats.Population, RealmRules.FoundMinPopulation);
            var r = w.Realm.System.Execute(new FoundCityCommand(region, name, cell));
            Assert.True(r.Ok, r.Message);
            return w.Realm.City(r.Id);
        }

        // ------------------------------------------------------------ Grundzustand

        [Fact]
        public void NewGame_OneActiveCity_ApiUnchanged()
        {
            var w = NewRealm();
            var realm = w.Realm;
            Assert.Single(realm.Cities);
            Assert.Equal(0, realm.ActiveCityId);
            Assert.Equal("Bergheim", realm.CityName);
            Assert.Equal("Freie Stadt Bergheim", realm.DisplayName);
            Assert.Equal(new[] { w.StartRegion.Id }, realm.CityRegions);
            Assert.False(realm.CanFoundNation);
            Assert.NotNull(realm.System);
            Assert.Equal(w.Seed, realm.ActiveCity.Seed);
            Assert.Equal(w.Settings.TerrainSeed, realm.ActiveCity.TerrainSeed);
            Assert.Equal(w.Planet.PlayerCountry.CapitalCell, realm.ActiveCity.Cell);
            Days(w, 1);
            Assert.Equal(0, realm.ActiveCity.Economy.LastDay);
        }

        // ------------------------------------------------------------ Stadt gründen

        [Fact]
        public void FoundCity_ChecksAllConditions()
        {
            var w = NewRealm();
            var sys = w.Realm.System;
            int target = Layout.Value.free[0];
            var check = sys.CheckFoundCity(target, -1, "Talheim");
            foreach (var i in check.Items) output.WriteLine($"{(i.Ok ? "✓" : "✗")} {i.Text} {i.Detail}");
            Assert.False(check.Ok);
            Assert.Contains(check.Items, i => !i.Ok && i.Text.Contains("Einwohner"));
            Assert.Contains(check.Items, i => !i.Ok && i.Text.Contains("Rathaus"));
            Assert.Contains(check.Items, i => i.Ok && i.Text.Contains("Verbindung"));
            Assert.False(sys.Execute(new FoundCityCommand(target, "Talheim")).Ok);
            Assert.Single(w.Realm.Cities);

            // NPC-Land ist tabu, weit entfernte Regionen sind nicht erreichbar
            var p = w.Planet;
            var npc = p.Regions.First(r => r.CountryId >= 0 && !p.Countries[r.CountryId].IsPlayer);
            Assert.Contains(sys.CheckFoundCity(npc.Id).Items, i => !i.Ok && i.Text == "Region frei");
            var far = p.Regions.First(r => r.IsFree && !r.Neighbors.Any(q => w.Realm.CityRegions.Contains(q)) && r.Id != w.StartRegion.Id
                                           && !(r.IsCoastal && w.StartRegion.IsCoastal));
            Assert.Contains(sys.CheckFoundCity(far.Id).Items, i => !i.Ok && i.Text.StartsWith("Verbindung"));
        }

        [Fact]
        public void FoundCity_InFreeNeighborRegion_OwnsRegion_PaysCost()
        {
            var w = NewRealm(town: true);
            var p = w.Planet;
            int target = Layout.Value.free[0];
            double before = w.Economy.Balance;
            int version = p.PoliticalVersion;
            var b = Found(w, target, "Talheim");
            Assert.Equal(2, w.Realm.Cities.Count);
            Assert.Equal("Talheim", b.Name);
            Assert.Equal(target, b.RegionId);
            Assert.Equal(target, p.RegionOf[b.Cell]);
            Assert.Equal(p.PlayerCountryId, p.Regions[target].CountryId);
            Assert.Contains(target, w.Realm.CityRegions);
            Assert.True(p.PoliticalVersion > version);
            Assert.Equal(before - RealmRules.FoundingCost, w.Economy.Balance, 3);
            Assert.Equal(RealmRules.FoundingCost, w.Economy.CurrentWeek.Amount(BudgetCategory.RealmExpenses), 3);
            Assert.Equal(RealmRules.NewCityFunds, b.Economy.Treasury, 3);
            Assert.False(b.HasSnapshot);
            Assert.Equal(0, b.Economy.Population);
            Assert.NotEqual(w.Realm.ActiveCity.TerrainSeed, b.TerrainSeed);
            // gleicher Name geht nicht, zweite Stadt in derselben Weltzelle auch nicht
            Assert.False(w.Realm.System.Execute(new FoundCityCommand(Layout.Value.free[1], "talheim")).Ok);
            Assert.Contains(w.Realm.System.CheckFoundCity(target, b.Cell, "Neudorf").Items, i => !i.Ok && i.Text.StartsWith("Freier Platz"));
            // Weg über die Autobahn
            var route = w.Realm.System.Route(0, b.Id);
            Assert.NotNull(route);
            output.WriteLine(route.ToString());
            Assert.Equal(RealmRouteMode.Road, route.Mode);
            Assert.InRange(route.LeadDays, 1, 6);
        }

        // ------------------------------------------------------------ Wechseln

        [Fact]
        public void Switch_ToNewCity_AndBack_RestoresDetail()
        {
            var a = NewRealm(town: true);
            var b = Found(a, Layout.Value.free[0], "Talheim");
            Days(a, 1);
            int popA = a.Citizens.Stats.Population;
            int segmentsA = a.Roads.Segments.Count();
            ulong roadsHashA = SaveGame.StateHash(a.Roads);
            long tick = a.Clock.Tick;

            var wb = RealmSwitch.Switch(a, b.Id);
            Assert.Equal(b.Id, wb.Realm.ActiveCityId);
            Assert.Equal("Talheim", wb.Realm.CityName);
            Assert.Equal(Layout.Value.free[0], wb.StartRegion.Id);
            Assert.Equal(tick, wb.Clock.Tick);
            Assert.Equal(0, wb.Roads.Segments.Count()); // neue, leere Stadtkarte
            Assert.Equal(0, wb.Citizens.Stats.Population);
            Assert.True(wb.Realm.City(0).HasSnapshot);
            Assert.False(wb.Realm.ActiveCity.HasSnapshot);
            Assert.Equal("Freie Stadt Bergheim", wb.Planet.PlayerCountry.Name); // Hauptstadt bleibt die Startstadt
            Assert.Equal(wb.Planet.PlayerCountryId, wb.Planet.Regions[Layout.Value.start].CountryId); // Besitz kommt mit
            Assert.Equal(popA, (int)wb.Realm.City(0).Economy.Population);
            output.WriteLine($"Schnappschuss Bergheim {wb.Realm.City(0).SnapshotBytes / 1024} KB");

            // in B bauen, ein paar Tage
            var plan = RoadBuilder.Plan(wb.Roads, new Vector2(-200, 100), 0f, null, new Vector2(200, 100), 0f, RoadCatalog.CityTwoLane);
            RoadBuilder.Build(wb.Roads, plan);
            Days(wb, 3);
            int segmentsB = wb.Roads.Segments.Count();

            // zurück nach A: Detailzustand wie verlassen (Straßen bitgleich), Uhr läuft weiter
            var wa = RealmSwitch.Switch(wb, 0);
            Assert.Equal(0, wa.Realm.ActiveCityId);
            Assert.Equal(segmentsA, wa.Roads.Segments.Count());
            Assert.Equal(roadsHashA, SaveGame.StateHash(wa.Roads));
            Assert.Equal(popA, wa.Citizens.Stats.Population);
            Assert.Equal(tick + 3 * SimulationClock.TicksPerDay, wa.Clock.Tick);
            Assert.True(wa.Realm.City(b.Id).HasSnapshot);
            // Talheim wieder: Straße noch da
            var wb2 = RealmSwitch.Switch(wa, b.Id);
            Assert.Equal(segmentsB, wb2.Roads.Segments.Count());
            Days(wb2, 1);
            var back = RealmSwitch.Switch(wb2, 0);
            Days(back, 2); // läuft nach dem Zeitsprung ohne Fehler weiter
            Assert.True(back.Citizens.Stats.Population > 0);
        }

        /// <summary>Kacheln: große Karte (3 × 3) – jede Stadt hat ihre eigenen Kacheln, Kauf überlebt den Wechsel.</summary>
        [Fact]
        public void Switch_BigMap_TilesPerCity()
        {
            var a = SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = WorldSeed, Planet = Small, StartRegion = Layout.Value.start, CityName = "Bergheim",
                GenerateTerrain = false, SizeMeters = WorldSettings.GameSizeMeters,
            });
            Assert.Equal(3, a.Tiles.TilesPerSide);
            Assert.True(a.Tiles.Purchase(2, 1));
            var road = RoadBuilder.Plan(a.Roads, new Vector2(-300, 0), 0f, null, new Vector2(300, 0), 0f, RoadCatalog.CityTwoLane);
            Assert.True(road.IsValid, road.Error.ToString());
            RoadBuilder.Build(a.Roads, road);
            var hall = a.Services.PlanAligned(ServiceCatalog.TownHall, new Vector2(-150, -30));
            Assert.True(hall.IsValid, hall.Error.ToString());
            Assert.NotNull(a.Services.Build(hall));
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var wb = RealmSwitch.Switch(a, b.Id);
            Assert.Equal(WorldSettings.GameSizeMeters, wb.Settings.SizeMeters);
            Assert.Equal(1, wb.Tiles.OwnedCount); // neue Stadt: nur die Mitte
            Assert.False(wb.Tiles.IsOwned(2, 1));
            Assert.True(wb.Tiles.Purchase(1, 2));
            Days(wb, 1);
            var wa = RealmSwitch.Switch(wb, 0);
            Assert.True(wa.Tiles.IsOwned(2, 1));
            Assert.False(wa.Tiles.IsOwned(1, 2));
            var wb2 = RealmSwitch.Switch(wa, b.Id);
            Assert.True(wb2.Tiles.IsOwned(1, 2));
            Assert.False(wb2.Tiles.IsOwned(2, 1));
        }

        [Fact]
        public void Switch_IsDeterministic_AndSurvivesSaveLoad()
        {
            SimulationWorld Play()
            {
                var w = NewRealm(town: true);
                var b = Found(w, Layout.Value.free[0], "Talheim");
                Days(w, 1);
                var wb = RealmSwitch.Switch(w, b.Id);
                Days(wb, 1);
                return RealmSwitch.Switch(wb, 0);
            }
            var x = Play();
            var y = Play();
            Assert.Equal(SaveGame.StateHash(x), SaveGame.StateHash(y));

            // Spielstand enthält alle Städte; geladene Welt läuft bitgleich weiter, auch nach einem weiteren Wechsel
            var data = SaveGame.SaveToBytes(x, "Reich");
            var z = SaveGame.Load(data);
            Assert.Equal(2, z.Realm.Cities.Count);
            Assert.True(z.Realm.City(1).HasSnapshot);
            Assert.Equal(SaveGame.StateHash(x), SaveGame.StateHash(z));
            Days(x, 1); Days(z, 1);
            Assert.Equal(SaveGame.StateHash(x), SaveGame.StateHash(z));
            var xs = RealmSwitch.Switch(x, 1);
            var zs = RealmSwitch.Switch(z, 1);
            Days(xs, 1); Days(zs, 1);
            var hx = SaveGame.SectionHashes(xs);
            var hz = SaveGame.SectionHashes(zs);
            foreach (var (k, v) in hx.Zip(hz)) Assert.True(k.Value == v.Value, $"Abschnitt {k.Key} weicht ab");
        }

        // ------------------------------------------------------------ Aggregierte Stadt

        [Fact]
        public void AggregatedCity_GrowsWithSatisfaction_UpToHousing_AndShrinks()
        {
            var e = new CityEconomy();
            e.Population = 3000; e.HousingCapacity = 3300; e.Treasury = 50000; e.TaxPerCapitaDay = 1.2; e.UpkeepPerDay = 2000;
            e.BaseSatisfaction = e.Satisfaction = 0.8f;
            for (int c = 0; c < GoodsCategories.Count; c++) { e.ProductionPerCapita[c] = 0.2; e.DemandPerCapita[c] = 0.2; }
            for (int d = 0; d < 120; d++) e.StepDay(d, c => GoodsCategories.BasePrice[(int)c]);
            output.WriteLine($"gut: {e.Population:0} Einwohner, Kasse {e.Treasury:0}");
            Assert.Equal(3300, e.Population, 1); // Wachstum endet am Wohnraum
            Assert.True(e.Treasury > 50000);

            var bad = new CityEconomy();
            bad.Population = 3000; bad.HousingCapacity = 4000; bad.Treasury = -1000; bad.TaxPerCapitaDay = 0.5; bad.UpkeepPerDay = 3000;
            bad.BaseSatisfaction = bad.Satisfaction = 0.45f;
            for (int c = 0; c < GoodsCategories.Count; c++) bad.DemandPerCapita[c] = 0.2; // nichts selbst erzeugt → Einfuhr
            for (int d = 0; d < 60; d++) bad.StepDay(d, c => GoodsCategories.BasePrice[(int)c]);
            output.WriteLine($"schlecht: {bad.Population:0} Einwohner, Zufriedenheit {bad.Satisfaction:0.00}, Kasse {bad.Treasury:0}");
            Assert.True(bad.Population < 3000);
            Assert.True(bad.Satisfaction < 0.45f);
            Assert.True(bad.Imported[(int)GoodsCategory.Food] > 0);
        }

        [Fact]
        public void AggregatedCity_RunsDaily_InTheRealm()
        {
            var a = NewRealm(town: true);
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var wb = RealmSwitch.Switch(a, b.Id);
            var aggA = wb.Realm.City(0).Economy;
            double pop = aggA.Population;
            double treasury = aggA.Treasury;
            Days(wb, 3);
            Assert.Equal(wb.Clock.Day - 1, aggA.LastDay);
            Assert.NotEqual(treasury, aggA.Treasury);
            output.WriteLine($"Bergheim aggregiert: {pop:0} → {aggA.Population:0} Einwohner, Kasse {treasury:0} → {aggA.Treasury:0}");
            // Kasse beim Zurückwechseln = aggregierte Kasse (Differenz als Verrechnung gebucht)
            double expected = aggA.Treasury;
            var back = RealmSwitch.Switch(wb, 0);
            Assert.Equal(expected, back.Economy.Balance, 3);
        }

        // ------------------------------------------------------------ Interne Lieferungen

        static ContractTerms Terms(string good, double units, int weeks = 2) => new ContractTerms
        {
            Good = GoodsCatalog.Get(good), UnitsPerWeek = units, Weeks = weeks, PriceMode = PriceMode.WorldIndexed, Price = 1.0,
        };

        [Fact]
        public void Delivery_FromAggregatedCity_ArrivesInTheBorderDepot()
        {
            var a = NewRealm(town: true);
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var sys = a.Realm.System;
            var grain = GoodsCatalog.Get("grain");
            int cat = (int)TradeGoods.CategoryOf(grain);
            b.Economy.Stock[cat] = 500 * TradeGoods.CategoryUnitsPerGood(grain);

            Assert.False(sys.Execute(new ProposeDeliveryCommand(0, 0, Terms("grain", 10))).Ok);
            Assert.False(sys.Execute(new ProposeDeliveryCommand(b.Id, 0, Terms("grain", 0))).Ok);
            var r = sys.Execute(new ProposeDeliveryCommand(b.Id, 0, Terms("grain", 40, weeks: 2)));
            Assert.True(r.Ok, r.Message);
            var d = a.Realm.Delivery(r.Id);
            double treasuryB = b.Economy.Treasury;
            var route = sys.Route(b.Id, 0);

            Days(a, 2 + route.LeadDays);
            output.WriteLine($"{route} · Grenzlager Getreide {a.Trade.City.ImportStock[grain.Index]:0.0} · verschickt {d.Shipped}");
            Assert.Equal(40, d.Shipped, 3);
            Assert.True(a.Trade.City.ImportStock[grain.Index] > 0 || a.Trade.City.SalesToFirms > 0, "Ware ist im Grenzlager angekommen");
            Assert.True(a.Economy.CurrentWeek.Amount(BudgetCategory.RealmExpenses) + (a.Economy.LastWeek?.Amount(BudgetCategory.RealmExpenses) ?? 0) > RealmRules.FoundingCost);
            Assert.True(d.ValueSettled > 0);
            Assert.True(d.FreightPaid > 0);
            // Verrechnung − Fracht; dazu höchstens Steueranteil an der Ausfuhr des Überhangs (keine Einwohner, kein Unterhalt)
            Assert.True(b.Economy.Treasury >= treasuryB + d.ValueSettled - d.FreightPaid - 0.01, $"{b.Economy.Treasury} < {treasuryB + d.ValueSettled - d.FreightPaid}");
            Days(a, 7 + route.LeadDays);
            Assert.Equal(80, d.Shipped, 3);
            Assert.Equal(RealmDeliveryStatus.Completed, d.Status);
        }

        [Fact]
        public void Delivery_FromActiveCity_UsesExportPathOfTheDepot()
        {
            var a = NewRealm(town: true);
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var sys = a.Realm.System;
            var planks = GoodsCatalog.Get("planks");
            Assert.Equal(0, a.Trade.PlayerExportDue(planks, 7), 6);
            var r = sys.Execute(new ProposeDeliveryCommand(0, b.Id, Terms("planks", 25, weeks: 1)));
            Assert.True(r.Ok, r.Message);
            // Firmen sehen den Bedarf wie einen Exportvertrag (Hook im Handel) und den Verrechnungspreis
            Assert.Equal(25, a.Trade.PlayerExportDue(planks, 7), 6);
            Assert.True(a.Trade.PlayerExportPrice(planks) > 0);
            a.Trade.City.ExportStock[planks.Index] = 30; // Lkw der Firmen haben angeliefert
            int cat = (int)TradeGoods.CategoryOf(planks);
            double stockB = b.Economy.Stock[cat];
            var route = sys.Route(0, b.Id);
            Days(a, 2 + route.LeadDays);
            var d = a.Realm.Delivery(r.Id);
            Assert.Equal(25, d.Shipped, 3);
            Assert.True(a.Trade.City.ExportStock[planks.Index] <= 5 + 1e-9); // Rest ohne Vertrag geht wie gehabt an den Weltmarkt
            Assert.True(b.Economy.Stock[cat] > stockB || b.Economy.Exported[cat] > 0);
            Assert.True(d.ValueSettled > 0);
            Assert.True(a.Economy.CurrentWeek.Amount(BudgetCategory.RealmIncome) + (a.Economy.LastWeek?.Amount(BudgetCategory.RealmIncome) ?? 0) > 0);
            Assert.Equal(RealmDeliveryStatus.Completed, d.Status);
        }

        [Fact]
        public void Delivery_SurvivesSwitch_AndSaveLoad()
        {
            var a = NewRealm(town: true);
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var grain = GoodsCatalog.Get("grain");
            b.Economy.Stock[(int)TradeGoods.CategoryOf(grain)] = 1000 * TradeGoods.CategoryUnitsPerGood(grain);
            var r = a.Realm.System.Execute(new ProposeDeliveryCommand(b.Id, 0, Terms("grain", 30, weeks: 6)));
            Days(a, 2);
            var wb = RealmSwitch.Switch(a, b.Id); // Sender ist jetzt detailliert, Empfänger aggregiert
            var d = wb.Realm.Delivery(r.Id);
            Assert.NotNull(d);
            Assert.Equal(30, wb.Trade.PlayerExportDue(grain, 7), 6);
            var z = SaveGame.Load(SaveGame.SaveToBytes(wb));
            Days(wb, 8); Days(z, 8);
            Assert.Equal(SaveGame.StateHash(wb), SaveGame.StateHash(z));
            Assert.True(z.Realm.Delivery(r.Id).WeeksDispatched >= 2);
        }

        // ------------------------------------------------------------ Land gründen

        [Fact]
        public void FoundNation_Conditions_Effects_Recognition_Levy()
        {
            var a = NewRealm(town: true);
            var sys = a.Realm.System;
            var p = a.Planet;
            var b = Found(a, Layout.Value.free[0], "Talheim");
            var check = sys.CheckNation("Aurelien", -1);
            Assert.False(check.Ok);
            float progress = check.Progress;
            var c = Found(a, Layout.Value.free[1], "Seedorf");
            Assert.True(sys.CheckNation("Aurelien", -1).Progress > progress);
            Assert.Contains(sys.CheckNation("Aurelien", -1).Items, i => !i.Ok && i.Text.Contains("Rathäuser"));
            b.Economy.HasTownHall = true;
            b.Economy.Population = 3000;
            c.Economy.Population = 2000;
            a.Citizens.Stats.Population = 1500;
            Assert.False(sys.CheckNation(p.Countries.First(x => !x.IsPlayer).Name, -1).Ok); // Name eines NPC-Landes
            Assert.False(sys.Execute(new FoundNationCommand("Aurelien", 0x3366CC, c.Id)).Ok); // Hauptstadt ohne Rathaus
            Assert.True(a.Realm.CanFoundNation);

            var t = a.Trade;
            int me = p.PlayerCountryId;
            var relBefore = t.Nations.Where(n => !n.IsPlayer).Select(n => t.Relation(n.Id, me)).ToArray();
            int version = p.PoliticalVersion;
            var res = sys.Execute(new FoundNationCommand("Aurelien", 0x3366CC, b.Id));
            Assert.True(res.Ok, res.Message);
            Assert.True(a.Realm.IsNation);
            Assert.Equal("Aurelien", p.PlayerCountry.Name);
            Assert.Equal("Talheim", p.PlayerCountry.CapitalName);
            Assert.Equal(b.Cell, p.PlayerCountry.CapitalCell);
            Assert.Equal(0x3366CCu, p.PlayerCountry.Color);
            Assert.True(p.PoliticalVersion > version);
            Assert.False(a.Realm.CanFoundNation);
            var relAfter = t.Nations.Where(n => !n.IsPlayer).Select(n => t.Relation(n.Id, me)).ToArray();
            Assert.NotEqual(relBefore, relAfter);
            output.WriteLine($"{sys.RecognitionCount} von {t.Nations.Count - 1} Ländern erkennen an");
            Assert.InRange(sys.RecognitionCount, 1, t.Nations.Count - 1);

            // Landesabgabe: 10 % der Steuern jeder Stadt in die Landeskasse; Länder-Volkswirtschaft = Summe der Städte
            Days(a, 2);
            Assert.True(a.Realm.NationTreasury > 0, "Landeskasse bekommt die Abgabe");
            double sum = RealmRules.UmlandPerRegion * a.Realm.CityRegions.Count + a.Realm.TotalPopulation;
            Assert.Equal(sum, p.PlayerCountry.Economy.Population, 0);
            // Zuschuss an eine Stadt
            double nt = a.Realm.NationTreasury;
            Assert.False(sys.Execute(new TransferFundsCommand(b.Id, nt + 1)).Ok);
            Assert.True(sys.Execute(new TransferFundsCommand(b.Id, nt / 2)).Ok);
            Assert.Equal(nt / 2, a.Realm.NationTreasury, 3);

            // Spielstand: Land bleibt Land
            var z = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.Equal("Aurelien", z.Planet.PlayerCountry.Name);
            Assert.Equal(b.Id, z.Realm.CapitalCityId);
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(z));
        }

        // ------------------------------------------------------------ NPC-Expansion

        [Fact]
        public void NpcExpansion_ClaimsFreeLandSlowly_Bounded_Deterministic()
        {
            int[] Owners(SimulationWorld w) => w.Planet.Regions.Select(r => r.CountryId).ToArray();
            SimulationWorld Run()
            {
                var w = NewRealm();
                w.Realm.NpcExpansion = true;
                for (int week = 0; week < 400; week++) w.Realm.System.ExpansionWeek(week * 7 + 3);
                return w;
            }
            var x = Run();
            var y = Run();
            Assert.Equal(Owners(x), Owners(y));
            var fresh = NewRealm();
            int initial = fresh.Planet.FreeRegionCount;
            int now = x.Planet.FreeRegionCount;
            output.WriteLine($"frei: {initial} → {now}, beansprucht {x.Realm.RegionsClaimedByNpc}");
            Assert.True(now < initial, "NPC-Länder nehmen freies Land");
            Assert.True(now >= Math.Ceiling(x.Realm.InitialFreeRegions * (1 - RealmRules.ExpansionMaxShare)), "Obergrenze");
            // nur angrenzend an das eigene Land, nie Spielerregionen
            var before = fresh.Planet;
            foreach (var r in x.Planet.Regions)
            {
                if (before.Regions[r.Id].CountryId == r.CountryId) continue;
                Assert.True(before.Regions[r.Id].IsFree);
                Assert.False(x.Planet.Countries[r.CountryId].IsPlayer);
            }
            Assert.Equal(x.Planet.PlayerCountryId, x.Planet.Regions[Layout.Value.start].CountryId);
            Assert.NotEmpty(x.Realm.News);
            // Spielstand stellt den Besitz wieder her
            var z = SaveGame.Load(SaveGame.SaveToBytes(x));
            Assert.Equal(Owners(x), Owners(z));
        }

        [Fact]
        public void NpcExpansion_OffByDefault()
        {
            var w = NewRealm();
            int free = w.Planet.FreeRegionCount;
            for (int week = 0; week < 50; week++) w.Realm.System.ExpansionWeek(week * 7 + 3);
            Assert.True(w.Planet.FreeRegionCount <= free); // direkt aufgerufen läuft sie; im Tageslauf nur mit Schalter
            var v = NewRealm();
            Assert.False(v.Realm.NpcExpansion);
        }

        // ------------------------------------------------------------ Spielstand-Migration

        [Fact]
        public void SaveLoad_RealmV1_MigratesToOneCity()
        {
            var a = NewRealm("Altstadt");
            Days(a, 1);
            var c = SaveGame.Capture(a);
            // Abschnitt „realm“ im Format v1 (Spielstart): Stadtname, Landesname, Flagge, Regionen, Besitz
            var p = a.Planet;
            var bytes = Bytes(w =>
            {
                w.String("Altstadt"); w.String(null); w.UInt(PlanetGenerator.PlayerColor); w.IntList(new List<int> { a.StartRegion.Id });
                w.Int(p.Regions.Count);
                foreach (var r in p.Regions) w.Int(r.CountryId);
            });
            c.Set("realm", 1, bytes);
            var z = SaveGame.Restore(c);
            Assert.Single(z.Realm.Cities);
            Assert.Equal("Altstadt", z.Realm.CityName);
            Assert.Equal(a.StartRegion.Id, z.Realm.ActiveCity.RegionId);
            Assert.Equal(z.Seed, z.Realm.ActiveCity.Seed);
            Days(z, 1);
            Assert.Equal(2, SaveGame.Capture(z).Find("realm").Version);
        }

        static byte[] Bytes(Action<SaveWriter> write)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                var w = new SaveWriter(ms);
                write(w);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
