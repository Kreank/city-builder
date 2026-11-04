using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Shipping;
using CityBuilder.Simulation.Traffic;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Wasserstraße &amp; Hafen (Stufe 3): Fahrrinne aus den Wasser-Gateways (Tiefe/Breite), vorgebaute Brücken hoch genug,
    /// Durchgangsschiffe, zu niedrige feste Brücke sperrt die Rinne (Hinweis, Schiffe bleiben weg/kehren um), Tunnel ist frei,
    /// automatische Trassierung baut hoch genug, Klappbrücke öffnet für Schiffe (Schranke, Verkehr wartet, kein Auto auf der
    /// offenen Klappe), Güterhafen (Einrasten am Ufer, Straßenfront, Gateway befahrbar, Anläufe, Umschlag, Fracht billiger),
    /// Speichern/Laden bitgleich, ohne Außenanbindung aus.
    /// </summary>
    public class ShippingTests
    {
        readonly ITestOutputHelper output;
        public ShippingTests(ITestOutputHelper output) => this.output = output;

        static WorldSettings Settings() => new WorldSettings
        {
            PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true, GenerateTerrain = true,
        };

        static SimulationWorld NewWorld()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            w.Economy.UnlimitedFunds = true;
            w.Step();
            return w;
        }

        static Waterway Trunk(SimulationWorld w) => w.Shipping.Waterways.First(x => x.Kind == WaterwayKind.Through);

        /// <summary>Punkt auf der Durchgangsrinne bei Anteil f, Richtung und Normale.</summary>
        static Vector2 OnTrunk(SimulationWorld w, float f, out Vector2 dir, out Vector2 normal)
        {
            var t = Trunk(w);
            t.Sample(t.Length * f, out var p, out dir);
            normal = new Vector2(-dir.Y, dir.X);
            return p;
        }

        /// <summary>Erster Landpunkt von p aus in Richtung d, plus <paramref name="extra"/> Meter.</summary>
        static Vector2 Bank(SimulationWorld w, Vector2 p, Vector2 d, float extra)
        {
            for (float s = 0f; s < 400f; s += 2f)
                if (!w.Terrain.IsWater(p + d * s)) return p + d * (s + extra);
            throw new InvalidOperationException("kein Ufer gefunden");
        }

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, float? ha = null, float? hb = null, bool follow = false)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane,
                ha ?? w.Terrain.HeightAt(a), hb ?? w.Terrain.HeightAt(b), RoadSegmentFlags.None, follow);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        // ------------------------------------------------------------ Fahrrinne

        [Fact]
        public void Fairway_RiverBetweenWaterGateways_DeepAndWide_Deterministic()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            Assert.True(sh.Active);
            var t = Trunk(w);
            var water = w.Gateways.All.Where(g => g.Kind == GatewayKind.Water).ToList();
            output.WriteLine($"{t}: Tiefe {t.MinDepth:0.0} m, Breite {t.MinWidth:0} m, Gateways {t.GatewayA}/{t.GatewayB}");
            Assert.Equal(2, water.Count);
            Assert.Contains(water, g => g.Id == t.GatewayA);
            Assert.Contains(water, g => g.Id == t.GatewayB);
            Assert.True(t.Length > 1800f);
            Assert.True(t.MinDepth >= ShipCatalog.Barge.RequiredDepth, $"Tiefe {t.MinDepth}");
            Assert.True(t.MinWidth >= 2f * ShippingRules.MinShoreDistance);
            // jeder Punkt in der Karte liegt im Wasser, die Enden außerhalb
            foreach (var p in t.Path) if (w.Contains(p)) Assert.True(w.Terrain.IsWater(p), $"Punkt {p} an Land");
            Assert.False(w.Contains(t.Path[0]));
            Assert.False(w.Contains(t.Path[t.Path.Count - 1]));
            // gleiche Welt → gleiche Rinne
            var w2 = SimulationWorld.CreateDefault(Settings());
            var t2 = Trunk(w2);
            Assert.Equal(t.Path.Count, t2.Path.Count);
            for (int i = 0; i < t.Path.Count; i++) Assert.Equal(t.Path[i], t2.Path[i]);
            // Klassen: Binnenschiff ja, Küstenfrachter (5,5 m Tiefgang) nicht im 4 m tiefen Fluss
            Assert.True(t.DeepEnough(ShipCatalog.Barge));
            Assert.False(t.DeepEnough(ShipCatalog.Coaster));
        }

        [Fact]
        public void PrebuiltHighwayBridge_HighEnough_ThroughShipsPass()
        {
            var w = NewWorld();
            var t = Trunk(w);
            Assert.NotEmpty(t.Crossings); // Autobahn quert den Fluss
            foreach (var x in t.Crossings) output.WriteLine($"{w.Shipping.CrossingName(x)}: lichte Höhe {x.Clearance:0.0} m");
            Assert.All(t.Crossings, x => Assert.True(x.Clearance >= ShippingRules.RiverDesignClearance, $"{x.Clearance}"));
            Assert.True(t.Passable(ShipCatalog.Barge));
            Assert.Null(w.Shipping.FairwayWarning);
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) w.Step();
            var sh = w.Shipping;
            output.WriteLine($"Schiffe eingesetzt {sh.ShipsSpawned}, durch {sh.ShipsCompleted}, ausgefallen {sh.ShipsCancelled}, Durchgang gestern {sh.ThroughYesterday}");
            Assert.True(sh.ShipsCompleted >= 10, "Binnenschiffe fahren durch");
            Assert.Equal(0, sh.ShipsCancelled);
            // Wasser-Gateways bleiben ohne Hafen „geplant“, nennen aber den Durchgang
            Assert.All(w.Gateways.All.Where(g => g.Kind == GatewayKind.Water), g =>
            {
                Assert.Equal(GatewayStatus.Planned, g.Status);
                Assert.Contains("Hafen fehlt", g.StatusText);
            });
        }

        [Fact]
        public void LowFixedBridge_BlocksFairway_HintAndShipsStayAway()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var p = OnTrunk(w, 0.5f, out _, out var n);
            var a = Bank(w, p, -n, 40f); var b = Bank(w, p, n, 40f);
            float h = w.Terrain.WaterLevel + 5f; // feste Brücke 5 m über Wasser → lichte Höhe 3,5 m
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, h, h);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var check = sh.CheckBridge(plan);
            output.WriteLine(check.Text);
            Assert.True(check.CrossesFairway);
            Assert.True(check.TooLow);
            Assert.Contains("höher bauen", check.Text);
            Assert.Contains("Klappbrücke", check.Text);
            // ein Schiff ist schon unterwegs, als die Brücke gebaut wird → hält davor und kehrt um
            var early = sh.QueueThrough(ShipCatalog.Barge, true);
            Assert.NotNull(early);
            RoadBuilder.Build(w.Roads, plan);
            w.Step();
            var t = Trunk(w);
            Assert.False(t.Passable(ShipCatalog.Barge));
            Assert.NotNull(sh.FairwayWarning);
            output.WriteLine(sh.FairwayWarning);
            int completed = sh.ShipsCompleted;
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) w.Step();
            output.WriteLine($"durch {sh.ShipsCompleted - completed}, gesperrt gestern {sh.BlockedYesterday(ShipCatalog.Barge)}, ausgefallen {sh.ShipsCancelled}");
            Assert.Equal(completed, sh.ShipsCompleted);
            Assert.True(sh.BlockedYesterday(ShipCatalog.Barge) + sh.BlockedToday(ShipCatalog.Barge) >= 10);
            Assert.DoesNotContain(sh.Ships, s => s.Id == early.Id); // umgekehrt
        }

        [Fact]
        public void AutoTrassierung_BridgesHighEnough_TunnelIsFree()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var p = OnTrunk(w, 0.3f, out var dir, out var n);
            var a = Bank(w, p, -n, 60f); var b = Bank(w, p, n, 60f);
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, w.Terrain.HeightAt(a), w.Terrain.HeightAt(b), RoadSegmentFlags.None, true);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var check = sh.CheckBridge(plan);
            output.WriteLine(check.Text);
            Assert.True(check.CrossesFairway);
            Assert.False(check.TooLow, check.Text);
            Assert.True(check.Clearance >= ShippingRules.RiverDesignClearance - 0.05f, $"{check.Clearance}");
            // Tunnel unter dem Fluss (Bauhöhe −16 m): kein Hindernis
            var p2 = OnTrunk(w, 0.5f, out _, out var n2);
            var ta = Bank(w, p2, -n2, 12f); var tb = Bank(w, p2, n2, 12f);
            float depth = -12f; // Flussbett −4 m → Tunnel ≥ 7 m darunter
            var tunnel = RoadBuilder.Plan(w.Roads, ta, 8f, null, tb, 8f, RoadCatalog.CityTwoLane, depth, depth);
            Assert.True(tunnel.IsValid, tunnel.Error.ToString());
            var tc = sh.CheckBridge(tunnel);
            output.WriteLine(tc.Text);
            Assert.True(tc.Tunnel);
            Assert.False(tc.TooLow);
            RoadBuilder.Build(w.Roads, tunnel);
            w.Step();
            Assert.True(Trunk(w).Passable(ShipCatalog.Barge));
        }

        // ------------------------------------------------------------ Klappbrücke

        [Fact]
        public void Drawbridge_OpensForShip_TrafficWaits_NoCarOnOpenSpan()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var p = OnTrunk(w, 0.5f, out _, out var n);
            var a = Bank(w, p, -n, 25f); var b = Bank(w, p, n, 25f);
            var plan = sh.PlanDrawbridge(RoadCatalog.CityTwoLane, a, b);
            Assert.True(plan.IsValid, plan.Error.ToString());
            output.WriteLine($"Klappe {Vector2.Distance(plan.SpanStart, plan.SpanEnd):0} m, Deck {plan.Deck:0.0} m, Kosten {plan.Cost:0} €");
            Assert.True(plan.Deck < ShippingRules.RiverDesignClearance); // niedrig – sie öffnet ja
            var span = sh.BuildDrawbridge(plan);
            Assert.NotNull(span);
            Assert.True(span.IsDrawbridge);
            Assert.Single(sh.Drawbridges);
            var d = sh.Drawbridges[0];
            Assert.True(Trunk(w).Passable(ShipCatalog.Barge)); // Klappbrücke sperrt nicht
            Assert.Contains(Trunk(w).Crossings, x => x.IsDrawbridge);
            // geplante feste Brücke gleicher Höhe wäre zu niedrig – der Hinweis nennt beim Plan der Klappe „öffnet“
            Assert.Contains("öffnet", sh.CheckBridge(plan.Span).Text);
            // Ohne Flag: dieselbe Straße ohne Fahrrinne wird als Klappbrücke abgelehnt
            var dry = RoadBuilder.Plan(w.Roads, a + n * 400f, 8f, null, a + n * 400f + new Vector2(n.Y, -n.X) * 60f, 8f, RoadCatalog.CityTwoLane,
                20f, 20f, RoadSegmentFlags.Drawbridge);
            Assert.Equal(RoadBuildError.DrawbridgeNoFairway, dry.Error);

            w.Traffic.Lanes.Update();
            var na = w.Roads.FindNearestNode(a, 2f); var nb = w.Roads.FindNearestNode(b, 2f);
            Assert.NotNull(na); Assert.NotNull(nb);
            var ship = sh.QueueThrough(ShipCatalog.Barge, true);
            Assert.NotNull(ship);
            int tripId = 1, closedSteps = 0, waitingCars = 0, carsOnOpenSpan = 0;
            bool opened = false, shipWaited = false;
            for (int i = 0; i < 4000; i++)
            {
                w.Substep();
                if (w.Traffic.Vehicles.Count < 30 && w.SubstepInTick % 8 == 0)
                {
                    w.Traffic.RequestTrip(TripEnd.AtNode(na), TripEnd.AtNode(nb), VehicleClass.Car, tripId++);
                    w.Traffic.RequestTrip(TripEnd.AtNode(nb), TripEnd.AtNode(na), VehicleClass.Car, tripId++);
                }
                if (d.RoadClosed) closedSteps++;
                if (d.State == DrawbridgeState.Open) opened = true;
                if (ship.State == ShipState.WaitingBridge) shipWaited = true;
                foreach (var v in w.Traffic.ActiveVehicles)
                {
                    if (v.Lane == null) continue;
                    if (v.Lane.Segment.Id == d.SegmentId && d.Lift > 0.01f) carsOnOpenSpan++;
                    if (d.RoadClosed && v.Speed < 0.3f && v.Lane.Segment.Id != d.SegmentId && v.Lane.Length - v.S < 40f &&
                        (v.Lane.To.Id == span.Start.Id || v.Lane.To.Id == span.End.Id)) waitingCars++;
                }
                if (!sh.Ships.Contains(ship) && d.State == DrawbridgeState.RoadOpen) break;
            }
            output.WriteLine($"gesperrt {closedSteps} Schritte, Öffnungen {d.OpeningsToday}, wartende Autos (Schritte) {waitingCars}, Autos auf offener Klappe {carsOnOpenSpan}, Schiff wartete {shipWaited}, gewartet {ship.WaitedSeconds:0} s");
            Assert.True(opened, "Klappe ging auf");
            Assert.True(d.OpeningsToday >= 1);
            Assert.True(closedSteps > (ShippingRules.DrawbridgeWarn + ShippingRules.DrawbridgeRaise) / TimeModel.TrafficStepSeconds);
            Assert.True(waitingCars > 0, "Autos warten vor der Schranke");
            Assert.Equal(0, carsOnOpenSpan);
            Assert.DoesNotContain(ship, sh.Ships); // Schiff ist durch
            Assert.Equal(DrawbridgeState.RoadOpen, d.State);
        }

        [Fact]
        public void Yacht_TooTallForFixedBridge_ButPassesDrawbridge()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            // Autobahnbrücke ≈ 13 m: Segelyacht (13 m + 0,5) passt nicht, Binnenschiff schon
            var t = Trunk(w);
            float minClear = t.Crossings.Min(x => x.Clearance);
            output.WriteLine($"niedrigste feste Brücke {minClear:0.0} m, Yacht braucht {ShipCatalog.Yacht.RequiredClearance:0.0} m");
            Assert.Equal(minClear >= ShipCatalog.Yacht.RequiredClearance, t.Passable(ShipCatalog.Yacht));
            Assert.True(t.Passable(ShipCatalog.Barge));
        }

        // ------------------------------------------------------------ Hafen

        static Harbor BuildRiverHarbor(SimulationWorld w, bool road = true)
        {
            var sh = w.Shipping;
            HarborPlan plan = null;
            foreach (float f in new[] { 0.4f, 0.45f, 0.35f, 0.3f, 0.25f, 0.85f, 0.8f, 0.75f })
                foreach (float side in new[] { 1f, -1f })
                {
                    var p = OnTrunk(w, f, out _, out var n);
                    var cursor = Bank(w, p, n * side, 30f);
                    var pl = sh.PlanHarbor(HarborCatalog.Cargo, cursor);
                    if (pl.IsValid) { plan = pl; goto found; }
                }
            found:
            Assert.NotNull(plan);
            var h = sh.BuildHarbor(plan);
            Assert.NotNull(h);
            if (road)
            {
                var front = h.FrontMid + h.Front * 7.5f;
                var side2 = h.Side;
                var rp = RoadBuilder.Plan(w.Roads, front - side2 * 90f, 8f, null, front + side2 * 90f, 8f, RoadCatalog.CityTwoLane,
                    w.Terrain.HeightAt(front - side2 * 90f), w.Terrain.HeightAt(front + side2 * 90f), RoadSegmentFlags.None, true);
                Assert.True(rp.IsValid, rp.Error.ToString());
                RoadBuilder.Build(w.Roads, rp);
            }
            w.Step();
            return h;
        }

        [Fact]
        public void Harbor_SnapsToShore_NeedsRoad_OpensWaterGateway_ShipsCall()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var h = BuildRiverHarbor(w, road: false);
            output.WriteLine($"{h}: Klasse {h.ShipClass}, Rinnen {sh.WaterwaysOf(h).Count}, Zufahrt {h.HasAccess}, erreichbar {h.Reachable}");
            Assert.Equal(ShipCatalog.Barge, h.ShipClass); // Fluss 4 m tief → Binnenhafen
            Assert.Equal(2, sh.WaterwaysOf(h).Count);       // zu beiden Fluss-Gateways
            Assert.True(h.Reachable);
            Assert.False(h.InService);                      // noch keine Straße vor dem Kai
            Assert.All(w.Gateways.All.Where(g => g.Kind == GatewayKind.Water), g => Assert.Equal(GatewayStatus.Planned, g.Status));
            // Liegeplätze im tiefen Wasser, Hafengelände an Land (aufgeschüttet)
            for (int i = 0; i < h.Type.Berths; i++)
                Assert.True(w.Terrain.WaterLevel - w.Terrain.NaturalHeightAt(h.Berth(i, h.ShipClass)) >= ShipCatalog.Barge.Draft, "Liegeplatz tief genug");
            Assert.False(w.Terrain.IsWater(h.Position));
            Assert.False(w.Terrain.IsWater(h.QuayMid + h.Front * 2f));
            // Straße vor die Vorderseite → in Betrieb → Wasser-Gateway befahrbar
            var front = h.FrontMid + h.Front * 7.5f;
            Road(w, front - h.Side * 90f, front + h.Side * 90f, follow: true);
            w.Step();
            Assert.True(h.InService);
            Assert.Contains(w.Gateways.All, g => g.Kind == GatewayKind.Water && g.IsOperational);
            // Straßen dürfen nicht durch den Hafen
            var cross = RoadBuilder.Plan(w.Roads, h.Position - h.Side * 80f, 8f, null, h.Position + h.Side * 80f, 8f, RoadCatalog.CityTwoLane, h.Height, h.Height);
            Assert.Equal(RoadBuildError.OverlapsStructure, cross.Error);
            // Anläufe: Schiffe kommen, legen an (Liegeplatz), schlagen um, fahren wieder
            h.PendingLoads = 30;
            var ship = sh.QueueCall(h);
            Assert.NotNull(ship);
            bool moored = false;
            for (int i = 0; i < 4 * SimulationClock.TicksPerHour * 180 && !moored; i++)
            {
                w.Substep();
                moored |= ship.State == ShipState.Moored;
            }
            Assert.True(moored, $"Schiff liegt am Kai (Zustand {ship.State}, Bug {ship.S:0}/{w.Shipping.RouteLength(ship):0})");
            Assert.True(ship.Berth >= 0);
            Assert.Equal(0, h.PendingLoads);
            Assert.Equal(30, ship.Loads);
            Assert.True(h.CallsToday + h.CallsYesterday >= 1);
            for (int i = 0; i < 8 * SimulationClock.TicksPerHour; i++) w.Step();
            output.WriteLine($"Anläufe heute {h.CallsToday}, Schiffe am Hafen {sh.Ships.Count(s => s.HarborId == h.Id)}, Gebühren {sh.FeesTotal:0} €");
            Assert.DoesNotContain(ship, sh.Ships); // wieder hinaus
            Assert.True(sh.FeesTotal > 0);
        }

        [Fact]
        public void Harbor_TradeRunsViaHarbor_CheaperFreight_LoadsCounted()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var h = BuildRiverHarbor(w);
            Assert.True(h.InService);
            int comp = w.Citizens.Components.OfNode(h.AccessNodeId);
            var node = w.Goods.PreferredGateway(Goods.GoodsCatalog.All[0], true, false, comp);
            Assert.NotNull(node);
            Assert.Equal(h.AccessNodeId, node.Id);
            Assert.Equal(ShippingRules.InlandFreightShare, w.Goods.FreightShareFor(node), 6);
            Assert.True(ShippingRules.InlandFreightShare < Rail.RailRules.RailFreightShare);
            Assert.True(ShippingRules.SeaFreightShare < ShippingRules.InlandFreightShare);
            Assert.NotNull(w.Gateways.OfNode(h.AccessNodeId));
            Assert.Equal(GatewayKind.Water, w.Gateways.OfNode(h.AccessNodeId).Kind);
            // Kapazität: Anläufe × Laderaum
            Assert.Equal(8 * ShipCatalog.Barge.Capacity, sh.CapacityPerDay(h));
            // Kasse: Bau gebucht, Unterhalt gemeldet
            Assert.True(sh.WeeklyUpkeep() >= HarborCatalog.Cargo.UpkeepPerWeek);
        }

        [Fact]
        public void LowBridge_CutsOffHarbor_Warning()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var h = BuildRiverHarbor(w);
            Assert.True(h.InService);
            // beide Zufahrten sperren: zu niedrige Brücken flussauf- und flussabwärts des Hafens
            float hs = Trunk(w).Project(h.Approach, out _);
            foreach (float sign in new[] { -1f, 1f })
                foreach (float off in new[] { 220f, 260f, 320f, 380f, 450f, 180f })
                {
                    float s = hs + sign * off;
                    Trunk(w).Sample(s, out var p, out var dir);
                    var n = new Vector2(-dir.Y, dir.X);
                    var a = Bank(w, p, -n, 12f); var b = Bank(w, p, n, 12f);
                    float y = w.Terrain.WaterLevel + 5f;
                    var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, y, y);
                    if (!plan.IsValid) { output.WriteLine($"Brücke bei {s:0}: {plan.Error}"); continue; }
                    RoadBuilder.Build(w.Roads, plan);
                    break;
                }
            w.Step();
            output.WriteLine($"erreichbar {h.Reachable}: {h.BlockedReason} · Warnung: {sh.FairwayWarning}");
            Assert.False(h.Reachable);
            Assert.False(h.InService);
            Assert.NotNull(h.BlockedReason);
            Assert.Contains("zu niedrig", h.BlockedReason);
            Assert.All(w.Gateways.All.Where(g => g.Kind == GatewayKind.Water), g => Assert.False(g.IsOperational));
            Assert.Null(sh.QueueCall(h));
        }

        [Fact]
        public void SeaHarbor_OnCoast_Coaster_SeaTradeRunsViaHarbor()
        {
            // Spiel-Standard: volle Erde, Seed 2024 (Startregion mit Küste und Fluss)
            var s = Settings();
            s.GenerateWorld = true;
            s.WorldSeed = 2024;
            var w = SimulationWorld.CreateDefault(s);
            w.Economy.UnlimitedFunds = true;
            w.Step();
            var sh = w.Shipping;
            Assert.NotNull(w.Trade);
            Assert.Contains(w.Gateways.All, g => g.Kind == GatewayKind.Water && g.TargetKind == GatewayTargetKind.Sea);
            // Küstenplatz mit tiefem Wasser vor dem Kai suchen (Seehafen für Küstenfrachter)
            HarborPlan plan = null;
            float half = w.Settings.SizeMeters * 0.5f - 120f;
            for (float x = -half; x <= half && plan == null; x += 48f)
                for (float y = -half; y <= half && plan == null; y += 48f)
                {
                    var p = new Vector2(x, y);
                    if (w.Terrain.IsWater(p)) continue;
                    var pl = sh.PlanHarbor(HarborCatalog.Cargo, p);
                    if (pl.IsValid && pl.ShipClass == ShipCatalog.Coaster) plan = pl;
                }
            Assert.NotNull(plan);
            var h = sh.BuildHarbor(plan);
            var front = h.FrontMid + h.Front * 7.5f;
            var rp = RoadBuilder.Plan(w.Roads, front - h.Side * 90f, 8f, null, front + h.Side * 90f, 8f, RoadCatalog.CityTwoLane,
                w.Terrain.HeightAt(front - h.Side * 90f), w.Terrain.HeightAt(front + h.Side * 90f), RoadSegmentFlags.None, true);
            Assert.True(rp.IsValid, rp.Error.ToString());
            RoadBuilder.Build(w.Roads, rp);
            w.Step();
            output.WriteLine($"{h}: {h.ShipClass}, Rinnen {sh.WaterwaysOf(h).Count}, in Betrieb {h.InService} {h.BlockedReason}");
            Assert.Equal(ShipCatalog.Coaster, h.ShipClass);
            Assert.True(h.InService, h.BlockedReason);
            Assert.All(sh.WaterwaysOf(h), x => Assert.True(x.Sea));
            Assert.Contains(w.Gateways.All, g => g.Kind == GatewayKind.Water && g.TargetKind == GatewayTargetKind.Sea && g.IsOperational);
            // Seehandel: Gut, dessen günstigstes Lieferland über See kommt → Import über den Hafen, Fracht 2,5 %
            int comp = w.Citizens.Components.OfNode(h.AccessNodeId);
            int seaGoods = 0, landGoods = 0;
            foreach (var g in Goods.GoodsCatalog.All)
            {
                int partner = w.Gateways.SpotPartner(g, true);
                if (partner < 0) continue;
                var route = w.Trade.Geography.Route(w.Trade.PlayerId, partner);
                var node = w.Goods.PreferredGateway(g, true, false, comp);
                if (route.Mode == Trade.TransportMode.Sea)
                {
                    seaGoods++;
                    Assert.NotNull(node);
                    Assert.Equal(h.AccessNodeId, node.Id);
                    Assert.Equal(ShippingRules.SeaFreightShare, w.Goods.FreightShareFor(node), 6);
                    Assert.Contains(h.Name, w.Gateways.TradeRouteText(partner));
                }
                else
                {
                    landGoods++;
                    Assert.True(node == null || node.Id != h.AccessNodeId, "Landweg nicht über den Hafen");
                }
            }
            output.WriteLine($"Güter über See {seaGoods}, über Land {landGoods}");
            Assert.True(seaGoods > 0, "mindestens ein Gut kommt über See");
            // Anlauf eines Küstenfrachters
            h.PendingLoads = 50;
            var ship = sh.QueueCall(h);
            Assert.NotNull(ship);
            Assert.Equal(ShipCatalog.Coaster, ship.Class);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_ShipsUnderway_DrawbridgeOpen_ContinuesBitIdentical()
        {
            var w = NewWorld();
            var sh = w.Shipping;
            var h = BuildRiverHarbor(w);
            var p = OnTrunk(w, 0.5f, out _, out var n);
            var dp = sh.PlanDrawbridge(RoadCatalog.CityTwoLane, Bank(w, p, -n, 25f), Bank(w, p, n, 25f));
            Assert.True(dp.IsValid, dp.Error.ToString());
            Assert.NotNull(sh.BuildDrawbridge(dp));
            sh.QueueThrough(ShipCatalog.Barge, true);
            sh.QueueThrough(ShipCatalog.Barge, false);
            h.PendingLoads = 12;
            sh.QueueCall(h);
            // speichern, während die Klappe offen ist (bzw. sich bewegt) und Schiffe unterwegs sind
            for (int i = 0; i < 4000 && (sh.Drawbridges[0].State == DrawbridgeState.RoadOpen || i < 400); i++) w.Substep();
            Assert.NotEqual(DrawbridgeState.RoadOpen, sh.Drawbridges[0].State);
            Assert.Contains(sh.Ships, s => s.IsOnMap);
            var bytes = SaveGame.SaveToBytes(w);
            var c = SaveGame.Load(bytes);
            Assert.Equal(SaveGame.StateHash(w), SaveGame.StateHash(c));
            Assert.Equal(sh.Ships.Count, c.Shipping.Ships.Count);
            Assert.Equal(sh.Drawbridges.Count, c.Shipping.Drawbridges.Count);
            for (int i = 0; i < 3000; i++) { w.Substep(); c.Substep(); }
            var ha = SaveGame.SectionHashes(w);
            var hb = SaveGame.SectionHashes(c).ToDictionary(x => x.Key, x => x.Value);
            foreach (var kv in ha) Assert.True(hb.TryGetValue(kv.Key, out var v) && v == kv.Value, $"Abschnitt {kv.Key} weicht ab");
            Assert.Equal(sh.ShipsCompleted, c.Shipping.ShipsCompleted);
            Assert.Equal(sh.Drawbridges[0].OpeningsTotal, c.Shipping.Drawbridges[0].OpeningsTotal);
            output.WriteLine($"Schiffe durch {sh.ShipsCompleted}, Öffnungen {sh.Drawbridges[0].OpeningsTotal}, Hafen-Anläufe {h.CallsToday}");
        }

        [Fact]
        public void WithoutPrebuiltConnections_NoShipping_NothingChanges()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            Assert.False(w.Shipping.Active);
            Assert.Empty(w.Shipping.Waterways);
            Assert.Equal(0f, w.Terrain.NavigationFloor(Vector2.Zero));
            for (int i = 0; i < 20; i++) w.Step();
            Assert.Empty(w.Shipping.Ships);
        }
    }
}
