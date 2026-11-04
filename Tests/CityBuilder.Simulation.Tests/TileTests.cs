using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Aviation;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Tiles;
using CityBuilder.Simulation.Transit;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Kartengröße &amp; Kacheln: Standard (2 km) = eine Kachel, alles wie bisher; große Karte 6 × 6 km = 3 × 3 Kacheln mit der Mitte
    /// gekauft, Bauen nur auf gekauftem Land (zentrale Prüfung), Kauf nur angrenzend und über die Stadtkasse (steigender Preis),
    /// Welt-Infrastruktur über die ganze Karte, Bodenwert/Lärm nur auf gekauftem Land, Speichern/Laden bitgleich, Migration.
    /// </summary>
    public class TileTests
    {
        readonly ITestOutputHelper output;
        public TileTests(ITestOutputHelper output) => this.output = output;

        const float Big = WorldSettings.GameSizeMeters; // 6 144 m
        const float Tile = 2048f;

        static SimulationWorld BigFlat(bool realFunds = false)
        {
            var s = new WorldSettings { SizeMeters = Big };
            if (realFunds) { s.UnlimitedFunds = false; s.StartingFunds = 1_000_000; }
            return SimulationWorld.CreateDefault(s);
        }

        static RoadPlan RoadPlan(SimulationWorld w, Vector2 a, Vector2 b) =>
            RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, w.Terrain.HeightAt(a), w.Terrain.HeightAt(b));

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadPlan(w, a, b);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        // ------------------------------------------------------------ Standard bleibt wie bisher

        [Fact]
        public void Default_2km_IsOneTile_NothingRestricted()
        {
            var w = SimulationWorld.CreateDefault();
            Assert.Equal(1, w.Tiles.TilesPerSide);
            Assert.False(w.Tiles.Restricted);
            Assert.Null(w.Roads.BuildArea);
            Assert.True(w.Tiles.IsBuildable(new Vector2(1000f, -1000f)));
            Assert.Equal(TileState.Owned, w.Tiles.StateOf(0, 0));
            Assert.Equal(1, TileRules.TilesPerSide(1024f, Tile));  // kleine Testkarten: eine Kachel
            Assert.Equal(2, TileRules.TilesPerSide(4096f, Tile));
            Assert.Equal(3, TileRules.TilesPerSide(Big, Tile));
        }

        // ------------------------------------------------------------ Große Karte: Startzustand und Kauf

        [Fact]
        public void BigMap_StartsWithCenterTile_PurchaseOnlyAdjacent_PriceRises()
        {
            var w = BigFlat(realFunds: true);
            var t = w.Tiles;
            Assert.Equal(3, t.TilesPerSide);
            Assert.Equal(Tile, t.TileSize);
            Assert.True(t.Restricted);
            Assert.Same(t, w.Roads.BuildArea);
            Assert.Same(t, w.Rail.Tracks.BuildArea);
            Assert.Equal(1, t.OwnedCount);
            Assert.True(t.IsOwned(1, 1));
            Assert.Equal("B2", t.NameOf(1, 1));
            Assert.Equal("Mitte", t.DirectionOf(1, 1));
            Assert.Equal("Nordosten", t.DirectionOf(2, 2));
            Assert.Equal(TileState.Purchasable, t.StateOf(2, 1));
            Assert.Equal(TileState.Locked, t.StateOf(2, 2));

            Assert.Equal(TilePurchaseError.NotAdjacent, t.PlanPurchase(2, 2).Error);
            Assert.Equal(TilePurchaseError.AlreadyOwned, t.PlanPurchase(1, 1).Error);
            Assert.Equal(TilePurchaseError.OutOfMap, t.PlanPurchase(3, 1).Error);

            double before = w.Economy.Balance;
            var first = t.PlanPurchase(2, 1);
            Assert.True(first.IsValid);
            Assert.Equal(TileRules.BasePrice, first.Cost);
            Assert.True(t.Purchase(first));
            Assert.Equal(before - TileRules.BasePrice, w.Economy.Balance, 3);
            Assert.Equal(TileRules.BasePrice, w.Economy.CurrentWeek.Amount(BudgetCategory.TilePurchase), 3);
            Assert.Equal(TileState.Purchasable, t.StateOf(2, 2)); // jetzt Nachbar
            var second = t.PlanPurchase(2, 2);
            Assert.Equal(TileRules.BasePrice + TileRules.PriceStep, second.Cost);
            Assert.True(t.Purchase(second));
            Assert.Equal(3, t.OwnedCount);
            Assert.Equal(2, t.Purchases);

            // Kasse leer → nicht kaufbar
            var poor = SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = Big, UnlimitedFunds = false, StartingFunds = 10_000 });
            Assert.Equal(TilePurchaseError.InsufficientFunds, poor.Tiles.PlanPurchase(1, 2).Error);
            Assert.False(poor.Tiles.Purchase(1, 2));
            Assert.Equal(1, poor.Tiles.OwnedCount);
        }

        // ------------------------------------------------------------ Bauen nur auf gekauftem Land

        [Fact]
        public void BigMap_BuildingOnlyOnOwnedTiles_AllTools()
        {
            var w = BigFlat();
            var east = new Vector2(Tile * 0.5f + 300f, 0f); // Kachel C2 (nicht gekauft)
            var home = new Vector2(-200f, 0f);              // Mitte (gekauft)

            // Straßen und Gleise
            Assert.Equal(RoadBuildError.TileNotOwned, RoadPlan(w, new Vector2(800f, 0f), new Vector2(1300f, 0f)).Error);
            Assert.True(RoadPlan(w, new Vector2(-600f, 0f), new Vector2(600f, 0f)).IsValid);
            Assert.Equal(RoadBuildError.TileNotOwned, w.Rail.PlanTrack(new Vector2(900f, 200f), null, new Vector2(1500f, 200f)).Error);
            Road(w, new Vector2(-600f, 0f), new Vector2(600f, 0f));

            // Gesetzte Gebäude, Dienste, Versorger, Leitungen, Depots, Lärmschutzwand
            Assert.Equal(HouseBuildError.TileNotOwned, w.City.PlanHouseAt(HouseCatalog.Low, east, Vector2.UnitY).Error);
            Assert.NotEqual(HouseBuildError.TileNotOwned, w.City.PlanHouseAt(HouseCatalog.Low, home + new Vector2(0f, 20f), -Vector2.UnitY).Error);
            Assert.Equal(ServiceBuildError.TileNotOwned, w.Services.Plan(ServiceCatalog.PoliceStation, east, Vector2.UnitY).Error);
            Assert.Equal(UtilityBuildError.TileNotOwned, w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, east, Vector2.UnitY).Error);
            Assert.True(w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, new Vector2(300f, 400f), Vector2.UnitY).IsValid);
            Assert.Equal(UtilityBuildError.TileNotOwned, w.Utilities.PlanLine(UtilityCatalog.WaterPipe, new Vector2(900f, 50f), new Vector2(1200f, 50f)).Error);
            Assert.True(w.Utilities.PlanLine(UtilityCatalog.WaterPipe, new Vector2(-300f, 50f), new Vector2(300f, 50f)).IsValid);
            Assert.Equal(TransitBuildError.TileNotOwned, w.Transit.PlanDepot(TransitCatalog.BusDepot, east).Error);
            Assert.Equal(NoiseBarrierError.TileNotOwned, w.NoiseBarriers.Plan(new Vector2(900f, 40f), new Vector2(1100f, 40f)).Error);

            // Flughafen: die 2 800-m-Bahn passt nicht in eine Kachel
            var port = w.Aviation.PlanAirport(AirportCatalog.Airport, new Vector2(0f, 500f), Vector2.UnitX, 1);
            Assert.Equal(2800f, port.RunwayLength);
            Assert.Equal(AirportBuildError.TileNotOwned, port.Error);

            // Terraforming und Bäume: nicht gekauftes Land ist gesperrt
            float h0 = w.Terrain.HeightAt(east);
            var r = w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, east, 30f, 2f, 0f));
            Assert.False(r.Changed);
            Assert.True(r.LockedPoints > 0);
            Assert.Equal(h0, w.Terrain.HeightAt(east));
            Assert.True(w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, home + new Vector2(0f, -300f), 20f, 1f, 0f)).Changed);
            Assert.Empty(w.Ecology.PlanTrees(east, 40f).Cells);

            // nach dem Kauf geht es
            Assert.True(w.Tiles.Purchase(2, 1));
            Assert.True(RoadPlan(w, new Vector2(800f, 0f), new Vector2(1300f, 0f)).IsValid);
            Assert.NotEqual(ServiceBuildError.TileNotOwned, w.Services.Plan(ServiceCatalog.PoliceStation, east, Vector2.UnitY).Error);
            Assert.True(w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, east, 30f, 2f, 0f)).Changed);
            var both = w.Aviation.PlanAirport(AirportCatalog.Airport, new Vector2(1024f, 500f), Vector2.UnitX, 1);
            Assert.NotEqual(AirportBuildError.TileNotOwned, both.Error); // Mitte + Osten: Bahn passt
            output.WriteLine($"Flughafen über zwei Kacheln: {AviationSystem.ErrorText(both) ?? "gültig"}");
        }

        [Fact]
        public void BigMap_ZonesDoNotGrowOnUnownedLand()
        {
            var w = BigFlat();
            using (w.Tiles.Unrestricted()) Road(w, new Vector2(900f, -300f), new Vector2(1300f, -300f)); // Straße der „Welt“ über die Grenze
            w.City.Paint(new Vector2(1150f, -280f), 40f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(950f, -280f), 40f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400; i++) w.Step();
            Assert.Contains(w.City.Buildings, b => b.Position.X < Tile * 0.5f);
            Assert.DoesNotContain(w.City.Buildings, b => b.Position.X > Tile * 0.5f + 8f);
        }

        // ------------------------------------------------------------ Welt-Infrastruktur über die ganze Karte

        [Fact]
        public void BigMap_PrebuiltHighwayAndRail_SpanWholeMap_FeederInCenterTile()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings
            {
                SizeMeters = Big, GenerateTerrain = true, PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true,
            });
            float half = Big * 0.5f;
            var locked = w.Roads.Segments.Where(s => s.IsLocked).ToList();
            Assert.NotEmpty(locked);
            int edgeEnds = locked.SelectMany(s => new[] { s.Start.Position, s.End.Position })
                .Count(p => MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)) > half - 40f);
            Assert.True(edgeEnds >= 2, $"Autobahn reicht nicht an den Kartenrand ({edgeEnds})");
            Assert.True(w.Rail.Tracks.SegmentCount > 0);
            Assert.True(w.Rail.Tracks.Segments.Any(s => MathF.Max(MathF.Abs(s.Start.Position.X), MathF.Abs(s.Start.Position.Y)) > half - 60f
                                                       || MathF.Max(MathF.Abs(s.End.Position.X), MathF.Abs(s.End.Position.Y)) > half - 60f));
            var feeders = w.Gateways.FeederEnds;
            Assert.NotEmpty(feeders);
            foreach (var f in feeders) Assert.True(w.Tiles.OwnedAt(f), $"Zubringer {f} liegt nicht auf der Startkachel");
            Assert.All(w.Gateways.All.Where(g => g.HasLocation), g => Assert.True(MathF.Max(MathF.Abs(g.Position.X), MathF.Abs(g.Position.Y)) > half - 300f));

            // Spieler schließt am Zubringer an
            var feeder = feeders[0];
            var dir = Vector2.Normalize(feeder - w.Gateways.Interchange);
            var far = feeder + dir * 200f;
            var plan = RoadBuilder.Plan(w.Roads, feeder, 8f, null, far, 8f, RoadCatalog.CityTwoLane, 0f, 0f, RoadSegmentFlags.None, followTerrain: true);
            Assert.True(plan.IsValid, plan.Error.ToString());
        }

        // ------------------------------------------------------------ Leistung: nur gekauftes Land rechnen

        [Fact]
        public void BigMap_LandValueOnlyOnOwnedTiles()
        {
            var w = BigFlat();
            Road(w, new Vector2(-600f, 0f), new Vector2(600f, 0f));
            using (w.Tiles.Unrestricted()) Road(w, new Vector2(1300f, 0f), new Vector2(1900f, 0f));
            w.LandValue.ForceRecalculate();
            Assert.True(w.LandValue.ValueAt(new Vector2(0f, 10f)) > 0.1f);
            Assert.Equal(-1f, w.LandValue.ValueAt(new Vector2(1600f, 10f))); // nie berechnet
            w.Tiles.Purchase(2, 1);
            w.LandValue.ForceRecalculate();
            Assert.True(w.LandValue.ValueAt(new Vector2(1600f, 10f)) > 0.1f);
        }

        [Fact]
        public void Info_Performance_BigMap_vs_2km()
        {
            var lines = new List<string>();
            foreach (float size in new[] { 2048f, Big })
            {
                var sw = Stopwatch.StartNew();
                var w = SimulationWorld.CreateDefault(new WorldSettings
                {
                    SizeMeters = size, GenerateWorld = true, WorldSeed = 2024, GenerateTerrain = true,
                    PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true,
                });
                long create = sw.ElapsedMilliseconds;
                w.Ecology.Enabled = true; w.Safety.Enabled = true; w.ServiceVehicles.Enabled = true;
                for (int t = 0; t < 240; t++) w.Step(); // einschwingen (erste Runde füllt Caches)
                var tick = new Dictionary<string, double>();
                var subs = w.Systems.OfType<ISubstepSystem>().ToList();
                double subTotal = 0;
                for (int t = 0; t < 240; t++)
                {
                    sw.Restart();
                    for (int k = 0; k < TimeModel.TrafficStepsPerTick; k++) foreach (var s in subs) s.Substep(w);
                    subTotal += sw.Elapsed.TotalMilliseconds;
                    w.Clock.Advance();
                    foreach (var sy in w.Systems)
                        if (w.Clock.Tick % sy.TickInterval == 0)
                        {
                            sw.Restart();
                            sy.Tick(w);
                            string n = sy.GetType().Name;
                            tick[n] = (tick.TryGetValue(n, out var v) ? v : 0) + sw.Elapsed.TotalMilliseconds;
                        }
                }
                lines.Add($"{size / 1000f:0.#} km ({w.Tiles.TilesPerSide}×{w.Tiles.TilesPerSide}): Welt erzeugen {create} ms, Fahrzeuge {w.Traffic.Vehicles.Count}, " +
                          $"Verkehrsschritte {subTotal:0} ms/Tag, Kalender {tick.Values.Sum():0} ms/Tag – " +
                          string.Join(", ", tick.OrderByDescending(k => k.Value).Take(4).Select(k => $"{k.Key} {k.Value:0}")));
            }
            foreach (var l in lines) output.WriteLine(l);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void BigMap_SaveLoad_Bitgleich_WithPurchase()
        {
            var a = BigFlat(realFunds: true);
            Road(a, new Vector2(-600f, 0f), new Vector2(600f, 0f));
            Assert.True(a.Tiles.Purchase(2, 1));
            Road(a, new Vector2(600f, 0f), new Vector2(1500f, 0f));
            a.City.Paint(new Vector2(1200f, 20f), 50f, ZoneType.ResidentialLow);
            for (int i = 0; i < 60; i++) a.Step();
            for (int i = 0; i < 37; i++) a.Substep();

            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.Equal(Big, b.Settings.SizeMeters);
            Assert.Equal(3, b.Tiles.TilesPerSide);
            Assert.True(b.Tiles.IsOwned(2, 1));
            Assert.False(b.Tiles.IsOwned(0, 1));
            Assert.Equal(1, b.Tiles.Purchases);
            Assert.Equal(a.Tiles.NextPrice, b.Tiles.NextPrice);
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));

            // gleiche Aktionen danach → gleich
            Assert.True(a.Tiles.Purchase(1, 2));
            Assert.True(b.Tiles.Purchase(1, 2));
            for (int i = 0; i < 80; i++) { a.Step(); b.Step(); }
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b).ToDictionary(k => k.Key, k => k.Value);
            foreach (var kv in ha) Assert.True(kv.Value == hb[kv.Key], $"Abschnitt {kv.Key} weicht ab");
            Assert.Equal(TileState.Owned, b.Tiles.StateOf(1, 2));
        }

        [Fact]
        public void Migration_WorldV4_WithoutTiles_IsOneTile_AllOwned()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300f, 0f), new Vector2(300f, 0f));
            var c = SaveGame.Capture(w);
            var ws = c.Find(SaveGame.WorldSectionId);
            c.Set(ws.Id, 4, ws.Data.Take(ws.Data.Length - 4).ToArray()); // v4: ohne Kachelgröße
            c.Remove("tiles");
            var old = SaveGame.Restore(SaveContainer.FromBytes(c.ToBytes()));
            Assert.Equal(2048f, old.Settings.TileSize);
            Assert.Equal(1, old.Tiles.TilesPerSide);
            Assert.True(old.Tiles.IsBuildable(new Vector2(1000f, 1000f)));
            Assert.Equal(w.Roads.SegmentCount, old.Roads.SegmentCount);
        }

        [Fact]
        public void TilesSection_IsLocalPerCity()
        {
            Assert.False(Realm.RealmSwitch.IsGlobal("tiles")); // jede Stadt des Reichs hat ihre eigenen Kacheln
        }
    }
}
