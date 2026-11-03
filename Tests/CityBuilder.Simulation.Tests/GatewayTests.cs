using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Außenanbindungen (Gateways): vorgebaute Autobahn mit Anschlussstelle + Zubringer (nicht abreißbar), Zuzug nur über
    /// Anschlüsse (Zuzügler fahren vom Gateway), Handel über den Gateway Richtung Partnerland, Durchgangsverkehr ohne
    /// Anschluss, freigehaltener Bahnkorridor, Wasserstraßen, Speichern/Laden + Determinismus.
    /// </summary>
    public class GatewayTests
    {
        readonly ITestOutputHelper output;
        public GatewayTests(ITestOutputHelper output) => this.output = output;

        static WorldSettings Settings(bool terrain = false, bool world = false, int worldSeed = 99, bool fullPlanet = false)
        {
            var s = new WorldSettings
            {
                PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true,
                GenerateTerrain = terrain,
            };
            if (world)
            {
                s.GenerateWorld = true;
                s.WorldSeed = worldSeed;
                if (!fullPlanet) s.Planet = new World.PlanetSettings { Width = 96, Height = 48, TargetRegions = 60, TargetCountries = 8 };
            }
            return s;
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static List<Gateway> Highways(SimulationWorld w) => w.Gateways.All.Where(g => g.Kind == GatewayKind.Highway).ToList();

        static List<EdgeKey> PathFrom(SimulationWorld w, RoadNode from, RoadNode to)
        {
            w.Traffic.Lanes.Update();
            foreach (var lane in w.Traffic.Lanes.OutgoingLanes(from))
            {
                var p = w.Traffic.Paths.FindPath(lane.Edge, to, VehicleClass.Car);
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>Flache Welt: Autobahn Ost–West bei y = −300, Zubringer-Enden bei (0, 100) und (0, −700).</summary>
        static RoadNode FeederEndNear(SimulationWorld w, Vector2 toward)
        {
            var end = w.Gateways.FeederEnds.OrderBy(p => Vector2.Distance(p, toward)).First();
            var node = w.Roads.FindNearestNode(end, 2f);
            Assert.NotNull(node);
            return node;
        }

        // ------------------------------------------------------------ Autobahn

        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 0)]
        [InlineData(true, true, 99)]
        [InlineData(true, true, 7)]
        [InlineData(true, true, 2024)]
        [InlineData(true, true, 2024, true)]  // Spiel-Standard: volle Erde, Seed 2024
        [InlineData(true, true, 74, true)]
        [InlineData(true, true, 333, true)]   // Hang: Brücke der Anschlussstelle braucht lichte Höhe
        [InlineData(true, true, 592, true)]
        public void Highway_IsBuilt_ContinuousBothWays_WithInterchangeAndFeeder(bool terrain, bool world, int seed, bool fullPlanet = false)
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain, world, seed, fullPlanet));
            var report = w.Gateways.LastReport;
            output.WriteLine(report.ToString());
            foreach (var g in w.Gateways.All) output.WriteLine($"  {g} @ {g.Position}");
            Assert.True(report.HighwayBuilt, report.Failure);
            Assert.Null(report.Failure);

            var hw = Highways(w);
            Assert.Equal(2, hw.Count);
            Assert.All(hw, g => Assert.True(g.IsOperational));
            Assert.Equal(hw[0].PartnerId, hw[1].Id);
            Assert.StartsWith($"A {w.Gateways.HighwayNumber} Richtung ", hw[0].Name);
            // Einfahrt/Ausfahrt sind Außenanschlüsse am Kartenrand
            foreach (var g in hw)
            {
                var entry = w.Roads.GetNode(g.EntryNodeId);
                var exit = w.Roads.GetNode(g.ExitNodeId);
                Assert.NotNull(entry);
                Assert.NotNull(exit);
                Assert.Contains(entry, w.Traffic.ExternalConnections);
                Assert.Contains(exit, w.Traffic.ExternalConnections);
                Assert.True(w.Traffic.IsExternalUsable(entry, true));
                Assert.True(w.Traffic.IsExternalUsable(exit, false));
            }
            // durchgehend befahrbar in beide Richtungen
            Assert.NotNull(PathFrom(w, w.Roads.GetNode(hw[0].EntryNodeId), w.Roads.GetNode(hw[1].ExitNodeId)));
            Assert.NotNull(PathFrom(w, w.Roads.GetNode(hw[1].EntryNodeId), w.Roads.GetNode(hw[0].ExitNodeId)));

            // Anschlussstelle: 4 Rampen, Brücke (Stadtstraße, gesperrt), zwei Zubringer-Stummel (Stadtstraße, frei)
            Assert.Equal(4, w.Roads.Segments.Count(s => s.Type == RoadCatalog.Ramp));
            Assert.All(w.Roads.Segments.Where(s => s.Type.Category != RoadCategory.City), s => Assert.True(s.IsLocked, s.ToString()));
            var city = w.Roads.Segments.Where(s => s.Type.Category == RoadCategory.City).ToList();
            Assert.Contains(city, s => s.IsLocked);   // Brücke über die Autobahn
            Assert.Contains(city, s => !s.IsLocked);  // Zubringer
            Assert.Equal(2, w.Gateways.FeederEnds.Count);
            foreach (var end in w.Gateways.FeederEnds)
            {
                var n = w.Roads.FindNearestNode(end, 2f);
                Assert.NotNull(n);
                Assert.Single(n.SegmentIds);
                var seg = w.Roads.GetSegment(n.SegmentIds[0]);
                Assert.Equal(RoadCategory.City, seg.Type.Category);
                Assert.False(seg.IsLocked);
                // vom Zubringer kommt man auf die Autobahn
                Assert.NotNull(PathFrom(w, n, w.Roads.GetNode(hw[0].ExitNodeId)));
            }
            // Kosten trägt die Welt
            Assert.Equal(w.Settings.StartingFunds, w.Economy.Balance, 3);
        }

        [Fact]
        public void Highway_BridgesTheRiver()
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain: true));
            Assert.True(w.Gateways.LastReport.HighwayBuilt);
            bool bridge = false;
            foreach (var s in w.Roads.Segments.Where(s => s.Type.IsHighway))
                for (int i = 0; i <= 32; i++)
                {
                    float t = i / 32f;
                    var p = s.Evaluate(t);
                    if (w.Terrain.IsWater(p))
                    {
                        Assert.True(s.HeightAt(t) >= w.Terrain.WaterLevel + 2.5f, $"{s} über Wasser zu tief");
                        bridge = true;
                    }
                }
            Assert.True(bridge, "Autobahn quert den Fluss auf einer Brücke");
        }

        [Fact]
        public void WithoutPrebuiltConnections_NothingIsBuilt()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            Assert.Equal(0, w.Roads.SegmentCount);
            Assert.Empty(w.Gateways.All);
            Assert.Null(w.Gateways.ConnectionHint);
        }

        [Fact]
        public void LockedHighway_CannotBeDemolishedOrRebuilt_FeederCan()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            int before = w.Roads.SegmentCount;
            var hwSeg = w.Roads.Segments.First(s => s.Type.IsHighway);
            Assert.False(RoadBuilder.Demolish(w.Roads, hwSeg));
            Assert.True(w.Roads.Contains(hwSeg));
            Assert.Equal(RoadBuildError.Locked, RoadBuilder.CheckReplace(w.Roads, hwSeg, RoadCatalog.Highway3));
            Assert.Null(RoadBuilder.Replace(w.Roads, hwSeg, RoadCatalog.Highway3));
            Assert.Null(RoadBuilder.Reverse(w.Roads, hwSeg));
            Assert.NotNull(RoadBuilder.LockedReason(hwSeg));
            var bridge = w.Roads.Segments.First(s => s.Type.Category == RoadCategory.City && s.IsLocked);
            Assert.False(RoadBuilder.Demolish(w.Roads, bridge));
            Assert.Equal(before, w.Roads.SegmentCount);

            // Neue Rampe an die Autobahn anschließen zerteilt sie – die Teile bleiben gesperrt
            var stub = w.Roads.Segments.First(s => s.Type.Category == RoadCategory.City && !s.IsLocked);
            Assert.Null(RoadBuilder.LockedReason(stub));
            Assert.True(RoadBuilder.Demolish(w.Roads, stub));
            Assert.False(w.Roads.Contains(stub));
        }

        [Fact]
        public void SplittingLockedSegment_KeepsLock()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var seg = w.Roads.Segments.First(s => s.Type.IsHighway && s.Length > 100f);
            w.Roads.SplitSegment(seg, 0.5f);
            Assert.All(w.Roads.Segments.Where(s => s.Type.IsHighway), s => Assert.True(s.IsLocked));
        }

        [Fact]
        public void HighwayUpkeep_IsPaidByTheWorld()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            Run(w, SimulationClock.TicksPerDay + 2);
            var periods = w.Economy.History.Concat(new[] { w.Economy.CurrentWeek }).ToList();
            double upkeep = Math.Abs(periods.Sum(p => p.Amount(Economy.BudgetCategory.RoadUpkeep)));
            // nur der Zubringer (Stadtstraße des Spielers) kostet Unterhalt, nicht die Autobahn der Welt
            double feeder = w.Roads.Segments.Where(s => !s.IsLocked).Sum(s => RoadBuilder.SegmentValue(s) * Economy.EconomyRules.RoadUpkeepShare);
            double all = w.Roads.Segments.Sum(s => RoadBuilder.SegmentValue(s) * Economy.EconomyRules.RoadUpkeepShare);
            output.WriteLine($"Unterhalt {upkeep:0.00} € (Zubringer/Tag {feeder:0.00} €, alles/Tag {all:0.00} €)");
            Assert.True(upkeep <= feeder * 1.01 + 0.01, $"Unterhalt {upkeep} > Zubringer {feeder}");
            Assert.True(feeder < all * 0.2);
            Assert.Equal(0.0, periods.Sum(p => p.Amount(Economy.BudgetCategory.RoadConstruction)), 3);
        }

        // ------------------------------------------------------------ Zuzug

        [Fact]
        public void MoveIn_OnlyAfterConnection_DriversStartAtGateway()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            Assert.NotNull(w.Gateways.ConnectionHint); // noch nichts angeschlossen

            // Wohngebiet abseits (nicht verbunden)
            Road(w, new Vector2(-300, 300), new Vector2(300, 300));
            w.City.Paint(new Vector2(0, 330), 150f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 2);
            Assert.True(w.City.Buildings.Count(b => b.Households > 0) >= 1, "Häuser stehen (leer)");
            Assert.Equal(0, w.Citizens.Stats.Population);
            Assert.True(w.Gateways.BlockedMoveInsToday + w.Gateways.BlockedMoveInsYesterday > 0);
            Assert.Contains("anschließen", w.Gateways.ConnectionHint);
            output.WriteLine(w.Gateways.ConnectionHint);

            // Anschluss: Zubringer-Ende (0, 100) mit dem Wohngebiet verbinden
            var feeder = FeederEndNear(w, new Vector2(0, 300));
            var moved = new List<(Household h, RoadNode via)>();
            w.Citizens.MovedInVia += (h, via) => moved.Add((h, via));
            w.Citizens.CarShare = 1f;
            Road(w, feeder.Position, new Vector2(0, 300));
            int carStartsAtGateway = 0;
            var entries = Highways(w).Select(g => g.EntryNodeId).ToHashSet();
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++)
            {
                int n = moved.Count;
                w.Step();
                foreach (var (h, via) in moved.Skip(n))
                {
                    Assert.Contains(via.Id, entries);
                    var driver = h.Members.FirstOrDefault(m => m.Age >= Citizen.AdultAge);
                    if (driver == null) continue;
                    Assert.Equal(TripPurpose.ToHome, driver.Purpose);
                    var v = w.Citizens.VehicleOf(driver);
                    if (v == null) continue;
                    // Auto steht auf der Einfahrt der Autobahn (Fahrt beginnt am Gateway)
                    var lane = v.Lane;
                    Assert.NotNull(lane);
                    Assert.Equal(via.Id, lane.From.Id);
                    Assert.True(lane.Segment.Type.IsHighway);
                    carStartsAtGateway++;
                }
            }
            output.WriteLine($"Zuzug {moved.Count} Haushalte, davon {carStartsAtGateway} mit Auto vom Gateway; Einwohner {w.Citizens.Stats.Population}");
            Assert.True(w.Citizens.Stats.Population > 10, $"Einwohner {w.Citizens.Stats.Population}");
            Assert.True(moved.Count > 3);
            Assert.True(carStartsAtGateway > 0, "mindestens ein Zuzügler fährt sichtbar über die Autobahn herein");
            Assert.Equal(moved.Count, Highways(w).Sum(g => g.TotalMoveIns));
            Assert.Null(w.Gateways.ConnectionHint);
        }

        [Fact]
        public void MoveIn_RequireWithoutGateways_UsesExternalConnection()
        {
            // Testwelt ohne Gateways, aber mit Pflicht: ohne Straße zum Kartenrand kein Zuzug, mit ihr schon
            var s = new WorldSettings { RequireGatewayForMoveIn = true };
            var w = SimulationWorld.CreateDefault(s);
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(0, 20), 150f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay);
            Assert.Equal(0, w.Citizens.Stats.Population);
            Road(w, new Vector2(200, 0), new Vector2(1020, 0));
            Run(w, SimulationClock.TicksPerDay);
            Assert.True(w.Citizens.Stats.Population > 10);
        }

        // ------------------------------------------------------------ Durchgangsverkehr

        [Fact]
        public void ThroughTraffic_RunsWithoutConnection_NoUTurns()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var hw = Highways(w);
            Assert.Equal(0f, w.Gateways.ThroughWeight(w.Roads.GetNode(hw[0].ExitNodeId), null));     // Ausfahrt ist kein Start
            Assert.True(w.Gateways.ThroughWeight(w.Roads.GetNode(hw[0].EntryNodeId), null) > 0f);
            Assert.Equal(0f, w.Gateways.ThroughWeight(w.Roads.GetNode(hw[0].EntryNodeId), w.Roads.GetNode(hw[0].ExitNodeId))); // keine Wende
            Assert.True(w.Gateways.ThroughWeight(w.Roads.GetNode(hw[0].EntryNodeId), w.Roads.GetNode(hw[1].ExitNodeId)) > 0f);

            Run(w, 900);
            output.WriteLine(string.Join(" | ", hw.Select(g => $"{g.Name}: rein {g.Today.VehiclesIn}, raus {g.Today.VehiclesOut}")) + $" · gestartet {w.Traffic.Stats.Spawned}, angekommen {w.Traffic.Stats.Arrived}, aktiv {w.Traffic.Stats.Active}");
            Assert.True(w.Traffic.Stats.Spawned > 10, $"Durchgangsverkehr {w.Traffic.Stats.Spawned}");
            Assert.All(hw, g => Assert.True(g.Today.VehiclesIn > 0));
            Assert.All(hw, g => Assert.True(g.Today.VehiclesOut > 0));
            Assert.Equal(0, w.Citizens.Stats.Population);
            // Fahrzeuge bleiben auf der Autobahn (keine Wende über die Anschlussstelle)
            Assert.All(w.Traffic.ActiveVehicles, v => Assert.NotEqual(RoadCategory.City, v.Lane?.Segment.Type.Category ?? RoadCategory.Highway));
        }

        // ------------------------------------------------------------ Bahn, Wasser, Luft

        [Fact]
        public void RailCorridor_IsKeptFree_AndPlannedGatewaysExist()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var gw = w.Gateways;
            Assert.True(gw.HasRailCorridor);
            var rails = gw.All.Where(g => g.Kind == GatewayKind.Rail).ToList();
            Assert.Equal(2, rails.Count);
            Assert.All(rails, g => Assert.Equal(GatewayStatus.Planned, g.Status));
            Assert.All(rails, g => Assert.Contains("noch nicht befahrbar", g.StatusText));
            Assert.Single(gw.All, g => g.Kind == GatewayKind.Air && !g.HasLocation);

            // Straße quer über den Korridor, Wohngebiet beidseits → kein Gebäude im Korridor
            var a = gw.RailStart; var b = gw.RailEnd;
            var dir = Vector2.Normalize(b - a);
            var normal = new Vector2(-dir.Y, dir.X);
            // weg von der Autobahn (Bahn: dort liegt jetzt die Rampe der Eisenbahnbrücke über die Autobahn)
            var p1 = a + (b - a) * 0.2f; var p2 = a + (b - a) * 0.8f;
            var mid = Vector2.Distance(p1, gw.Interchange) > Vector2.Distance(p2, gw.Interchange) ? p1 : p2;
            Road(w, mid - normal * 220f, mid + normal * 220f);
            w.City.Population.Residents = 3000;
            w.City.Population.WorkingAge = 1800;
            w.City.Paint(mid + dir * 20f, 200f, ZoneType.Industry);
            w.City.Paint(mid - dir * 20f, 200f, ZoneType.Industry);
            for (int i = 0; i < 80; i++) w.City.ForceTick();
            Assert.True(w.City.Buildings.Count() > 6, $"Gebäude {w.City.Buildings.Count()}");
            Assert.True(w.City.LotBlocker(mid));
            foreach (var bld in w.City.Buildings)
            {
                // nächster Punkt der Korridor-Mittellinie liegt nicht im Grundstück, Abstand ≥ halbe Breite
                float t = Math.Clamp(Vector2.Dot(bld.Position - a, b - a) / (b - a).LengthSquared(), 0f, 1f);
                var q = a + (b - a) * t;
                Assert.False(bld.Contains(q), $"{bld} steht im Bahnkorridor");
                Assert.False(gw.InRailCorridor(bld.Position));
            }
            // beidseits gebaut (Korridor teilt das Gebiet, sperrt es nicht ganz)
            Assert.Contains(w.City.Buildings, x => Vector2.Dot(x.Position - mid, normal) > 30f || Vector2.Dot(x.Position - mid, normal) < -30f);
        }

        [Fact]
        public void WaterGateways_AtRiverEnds_WithNavigability()
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain: true));
            var water = w.Gateways.All.Where(g => g.Kind == GatewayKind.Water).ToList();
            foreach (var g in water) output.WriteLine($"{g} Breite {g.WaterWidth:0} m Tiefe {g.WaterDepth:0.0} m schiffbar {g.Navigable}");
            Assert.Equal(2, water.Count); // Standardgelände: Fluss quer über die Karte (Nord ↔ Süd)
            Assert.All(water, g => Assert.Equal(GatewayStatus.Planned, g.Status));
            Assert.All(water, g => Assert.True(MathF.Abs(g.Position.Y) > 1000f));
            Assert.All(water, g => Assert.Equal(g.WaterWidth >= GatewayRules.NavigableWidth && g.WaterDepth >= GatewayRules.NavigableDepth, g.Navigable));
            Assert.Contains(water, g => g.Name.Contains("stromabwärts"));
        }

        // ------------------------------------------------------------ Handel

        [Fact]
        public void Import_UsesGatewayTowardPartnerCountry()
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain: false, world: true, worldSeed: 99));
            Assert.NotNull(w.Trade);
            var hw = Highways(w);
            Assert.Equal(2, hw.Count);
            Assert.True(hw.All(g => g.TargetCountryId >= 0 || g.TargetRegionId >= 0), "Ziele aus der Weltkarte");

            // Industrie am Zubringer
            var feeder = FeederEndNear(w, Vector2.Zero);
            var dir = Vector2.Normalize(feeder.Position - w.Gateways.Interchange);
            var far = feeder.Position + dir * 300f;
            Road(w, feeder.Position, far);
            var side = new Vector2(-dir.Y, dir.X);
            w.City.Paint(feeder.Position + dir * 150f + side * 25f, 60f, ZoneType.Industry);
            w.City.Population.Residents = 3000;
            w.City.Population.WorkingAge = 1800;
            for (int i = 0; i < 60; i++) w.City.ForceTick();
            var firms = w.Goods.Firms.Where(f => f.Zone == ZoneType.Industry).ToList();
            Assert.NotEmpty(firms);
            foreach (var b in w.City.Buildings) b.Workers = b.Jobs;
            Assert.True(w.Goods.Restructure(firms[0], RecipeCatalog.Get("mill")));
            var grain = GoodsCatalog.Grain;
            var mill = firms[0];

            // Erwartung: Gateway in Richtung des günstigsten Lieferlands
            int partner = w.Gateways.SpotPartner(grain, true);
            Assert.True(partner >= 0);
            var expected = w.Gateways.GatewayToward(partner, out bool viaPort);
            var toward = w.Gateways.DirectionToward(partner, out _);
            Assert.Equal(hw.OrderByDescending(g => Vector2.Dot(g.Direction, toward)).First().Id, expected.Id);
            output.WriteLine($"Lieferland {w.Planet.Countries[partner].Name}: {w.Gateways.TradeRouteText(partner)}");

            mill.InputStock[grain.Index] = 0f;
            mill.Incoming[grain.Index] = 0f;
            w.Goods.Market(grain).LocalPrice = w.Goods.Market(grain).WorldPrice * 1.6f; // Import lohnt
            int before = w.Goods.Shipments.Count;
            w.Goods.ForceStep();
            var imports = w.Goods.Shipments.Skip(before).Where(s => s.Kind == ShipmentKind.Import && s.Good == grain && s.ToBuildingId == mill.BuildingId).ToList();
            Assert.NotEmpty(imports);
            Assert.All(imports, s => Assert.Equal(expected.EntryNodeId, s.ConnectionNodeId));

            // Vertrag mit einem anderen Land → Gateway Richtung dieses Landes
            var t = w.Trade;
            int other = Enumerable.Range(0, t.Nations.Count).Where(c => c != t.PlayerId)
                .OrderBy(c => Vector2.Dot(w.Gateways.DirectionToward(c, out _), expected.Direction)).First();
            t.AiEnabled = false;
            var r = t.Execute(new ProposeContractCommand(t.PlayerId, other, TradeSide.Buy,
                new ContractTerms { Good = grain, UnitsPerWeek = 40, Weeks = 2, Price = 1.0 }));
            Assert.True(r.Ok, r.Message);
            var acc = t.Execute(new AcceptOfferCommand(other, r.Id));
            Assert.True(acc.Ok, acc.Message);
            Assert.Equal(other, w.Gateways.ContractPartner(grain, true));
            var cnode = w.Gateways.TradeNode(grain, true, true, w.Citizens.Components.OfSegment(mill.Building.FrontSegmentId));
            Assert.NotNull(cnode);
            Assert.Equal(w.Gateways.GatewayToward(other).EntryNodeId, cnode.Id);

            // Zählung je Gateway
            Run(w, 2);
            Assert.True(expected.Today.Imports >= imports.Count);
        }

        [Fact]
        public void WithoutGateway_NoHinterlandTrade()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { RequireGatewayForTrade = true });
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(0, -20), 60f, ZoneType.Industry);
            w.City.Population.Residents = 3000;
            w.City.Population.WorkingAge = 1800;
            for (int i = 0; i < 60; i++) w.City.ForceTick();
            foreach (var b in w.City.Buildings) b.Workers = b.Jobs;
            w.Goods.ForceStep();
            Assert.DoesNotContain(w.Goods.Shipments, s => s.Kind == ShipmentKind.Import || s.Kind == ShipmentKind.Export);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_KeepsGateways_NoRebuild_AndStaysDeterministic()
        {
            var a = SimulationWorld.CreateDefault(Settings());
            Road(a, FeederEndNear(a, new Vector2(0, 300)).Position, new Vector2(0, 300));
            Road(a, new Vector2(-300, 300), new Vector2(300, 300));
            a.City.Paint(new Vector2(0, 330), 150f, ZoneType.ResidentialLow);
            a.City.Paint(new Vector2(200, 270), 60f, ZoneType.Industry);
            Run(a, SimulationClock.TicksPerDay * 2 + 37);
            Assert.True(a.Citizens.Stats.Population > 0);

            var bytes = SaveGame.SaveToBytes(a);
            var b = SaveGame.Load(bytes);
            Assert.True(b.Settings.PrebuiltConnections && b.Settings.RequireGatewayForMoveIn && b.Settings.RequireGatewayForTrade);
            Assert.Equal(a.Roads.SegmentCount, b.Roads.SegmentCount); // nichts doppelt gebaut
            Assert.Equal(a.Roads.Segments.Count(s => s.IsLocked), b.Roads.Segments.Count(s => s.IsLocked));
            Assert.Equal(a.Gateways.All.Count, b.Gateways.All.Count);
            Assert.Equal(a.Gateways.All.Select(g => g.Name), b.Gateways.All.Select(g => g.Name));
            Assert.Equal(SaveGame.StateHash(a.Gateways), SaveGame.StateHash(b.Gateways));
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));

            Run(a, 300);
            Run(b, 300);
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            Assert.Equal(ha.Count, hb.Count);
            for (int i = 0; i < ha.Count; i++) Assert.True(ha[i].Value == hb[i].Value, $"Abschnitt {ha[i].Key} weicht ab");
            Assert.Equal(a.Gateways.All.Sum(g => g.Today.VehiclesIn), b.Gateways.All.Sum(g => g.Today.VehiclesIn));
        }

        [Fact]
        public void OldWorldSection_WithoutGatewaySwitches_LoadsWithSwitchesOff()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var c = SaveGame.Capture(w);
            var ws = c.Find(SaveGame.WorldSectionId);
            Assert.Equal(4, ws.Version);
            c.Set(ws.Id, 3, ws.Data.Take(ws.Data.Length - 3).ToArray()); // v3: ohne die drei Gateway-Schalter
            var old = SaveGame.Restore(SaveContainer.FromBytes(c.ToBytes()));
            Assert.False(old.Settings.PrebuiltConnections);
            Assert.False(old.Settings.RequireGatewayForMoveIn);
            Assert.False(old.Settings.RequireGatewayForTrade);
            Assert.Equal(w.Roads.SegmentCount, old.Roads.SegmentCount); // Straßen kommen aus dem Stand
        }

        [Fact]
        public void Generation_IsDeterministic()
        {
            var a = SimulationWorld.CreateDefault(Settings(terrain: true));
            var b = SimulationWorld.CreateDefault(Settings(terrain: true));
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));
        }
    }
}
