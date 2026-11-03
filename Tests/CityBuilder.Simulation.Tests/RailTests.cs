using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Rail;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Bahn (Stufe 2): Hauptstrecke auf dem Korridor (zweigleisig, gesperrt, Brücke über die Autobahn), Gleisregeln strenger
    /// als Straßen, Kreuzung Straße ↔ Gleis (Bahnübergang/lichte Höhe/längs verboten), Bahnhöfe (Einrasten, Bahnsteige,
    /// Straßenfront → Betrieb, Rail-Gateway befahrbar), Züge (Fahrplan, Halt, Blockabschnitte), Schranke + wartender Verkehr,
    /// Fahrgäste/Zuzug per Bahn, Güterbahnhof im Handel, Kasse, Speichern/Laden bitgleich.
    /// </summary>
    public class RailTests
    {
        readonly ITestOutputHelper output;
        public RailTests(ITestOutputHelper output) => this.output = output;

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

        [Theory]
        [InlineData(2024)] // Spiel-Standard: volle Erde, Seed 2024
        [InlineData(74)]
        [InlineData(333)]
        public void GameWorlds_MainlineAndAStationSpot_TrainsRunFast(int seed)
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain: true, world: true, worldSeed: seed, fullPlanet: true));
            var rail = w.Rail;
            output.WriteLine(rail.MainlineReport);
            Assert.True(rail.HasMainline, rail.MainlineReport);
            // Irgendwo entlang der Hauptstrecke lässt sich ein Personenbahnhof setzen (spielbar auch im Hügelland)
            int valid = 0, tried = 0;
            for (float f = 0.1f; f <= 0.9f; f += 0.05f)
                foreach (float side in new[] { 1f, -1f })
                {
                    var p = Axis(w, f, out _, out var normal);
                    var plan = rail.PlanStation(StationCatalog.Passenger, p + normal * side * 30f);
                    tried++;
                    if (plan.IsValid) valid++;
                }
            output.WriteLine($"Bahnhofsplätze: {valid} von {tried} gültig");
            Assert.True(valid > 0, "kein Platz für einen Bahnhof an der Hauptstrecke");
            // Leistung: Fahrplan einen halben Tag (Info)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int maxTrains = 0;
            for (int i = 0; i < SimulationClock.TicksPerDay / 2; i++) { w.Step(); maxTrains = Math.Max(maxTrains, rail.Trains.Count); }
            sw.Stop();
            output.WriteLine($"½ Tag: {rail.TrainsCompleted} Züge durch, max. {maxTrains} gleichzeitig, ausgefallen {rail.TrainsCancelled}, {sw.ElapsedMilliseconds} ms gesamt");
            Assert.Equal(0, rail.TrainsCancelled);
            Assert.True(rail.TrainsCompleted > 10);
        }

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        /// <summary>Punkt auf der Korridor-Achse bei Anteil f (0 = A, 1 = B) und Achse/Normale.</summary>
        static Vector2 Axis(SimulationWorld w, float f, out Vector2 dir, out Vector2 normal)
        {
            var a = w.Gateways.RailStart; var b = w.Gateways.RailEnd;
            dir = Vector2.Normalize(b - a);
            normal = new Vector2(-dir.Y, dir.X);
            return a + (b - a) * f;
        }

        /// <summary>Anteil der Achse weit weg von der Autobahn (Rampe der Eisenbahnbrücke meiden).</summary>
        static float FarFraction(SimulationWorld w)
        {
            var p1 = Axis(w, 0.2f, out _, out _); var p2 = Axis(w, 0.8f, out _, out _);
            return Vector2.Distance(p1, w.Gateways.Interchange) > Vector2.Distance(p2, w.Gateways.Interchange) ? 0.2f : 0.8f;
        }

        static void Substeps(SimulationWorld w, int n, Action each = null)
        {
            for (int i = 0; i < n; i++) { w.Substep(); each?.Invoke(); }
        }

        /// <summary>Personenbahnhof an der Hauptstrecke (Seite +normal) mit Straße vor dem Gebäude, an den Zubringer angeschlossen.</summary>
        static Station BuildConnectedStation(SimulationWorld w, StationType type, float f, bool connectToHighway = true)
        {
            var p = Axis(w, f, out var dir, out var normal);
            var plan = w.Rail.PlanStation(type, p + normal * 30f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var st = w.Rail.BuildStation(plan);
            Assert.NotNull(st);
            // Straße parallel vor der Vorderseite
            var front = st.FrontMid + st.Front * 7.5f;
            var side = new Vector2(-st.Front.Y, st.Front.X);
            Road(w, front - side * 120f, front + side * 120f);
            if (connectToHighway)
            {
                var feeder = w.Gateways.FeederEnds.OrderBy(x => Vector2.Distance(x, front)).First();
                var node = w.Roads.FindNearestNode(front + side * 120f, 2f);
                Road(w, node.Position, feeder);
            }
            return st;
        }

        // ------------------------------------------------------------ Hauptstrecke

        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(true, false, 0)]
        [InlineData(true, true, 99)]
        [InlineData(true, true, 7)]
        public void Mainline_IsBuiltOnCorridor_DoubleTrack_Locked_ClearOfHighway(bool terrain, bool world, int seed)
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain, world, seed));
            var rail = w.Rail;
            output.WriteLine(rail.MainlineReport);
            Assert.True(rail.HasMainline, rail.MainlineReport);
            Assert.All(rail.Tracks.Segments, s => { Assert.True(s.IsLocked); Assert.Equal(RoadCatalog.RailMain, s.Type); });
            Assert.True(rail.Tracks.Segments.Max(s => s.MaxSlope) <= RailRules.MainlineGrade + 0.002f, "Steigung der Hauptstrecke");
            // durchgehend in beiden Richtungen (Fahrweg-Graph)
            var t1 = rail.QueueTrain(TrainCatalog.Freight, fromA: true);
            var t2 = rail.QueueTrain(TrainCatalog.Freight, fromA: false);
            Assert.NotNull(t1); Assert.NotNull(t2);
            Assert.True(t1.RouteLength > 1000f && t2.RouteLength > 1000f);
            Assert.True(t1.RouteEdges.Select(e => e.SegmentId).Intersect(t2.RouteEdges.Select(e => e.SegmentId)).Count() == 0, "zwei getrennte Gleise");
            // Kosten trägt die Welt, kein Unterhalt
            Assert.Equal(w.Settings.StartingFunds, w.Economy.Balance, 3);
            Assert.Equal(0.0, rail.WeeklyUpkeep(), 3);
            // Rail-Gateways: Hauptstrecke liegt, aber ohne Bahnhof noch nicht befahrbar
            var rg = w.Gateways.All.Where(g => g.Kind == GatewayKind.Rail).ToList();
            Assert.Equal(2, rg.Count);
            w.Step();
            Assert.All(rg, g => Assert.Equal(GatewayStatus.Planned, g.Status));
            Assert.All(rg, g => Assert.Contains("Bahnhof", g.StatusText));
            // Autobahn wird kreuzungsfrei gequert (lichte Höhe ≥ 5 m), Wasser auf einer Brücke
            foreach (var hw in w.Roads.Segments)
                foreach (var tr in rail.Tracks.Segments)
                    for (int i = 0; i <= 64; i++)
                    {
                        var p = hw.Evaluate(i / 64f);
                        float tt = tr.ClosestT(p);
                        if (Vector2.Distance(tr.Evaluate(tt), p) > 1.5f) continue;
                        float dh = tr.HeightAt(tt) - hw.HeightAt(i / 64f);
                        output.WriteLine($"Querung {hw.Type.Id} bei {p}: Gleis {dh:0.0} m darüber");
                        Assert.True(dh >= RailRules.RailOverRoadClearance - 0.01f, $"lichte Höhe {dh:0.00} m");
                    }
            foreach (var tr in rail.Tracks.Segments)
                for (int i = 0; i <= 32; i++)
                    if (w.Terrain.IsWater(tr.Evaluate(i / 32f))) Assert.True(tr.HeightAt(i / 32f) >= w.Terrain.WaterLevel + 2.5f);
        }

        // ------------------------------------------------------------ Gleise bauen

        [Fact]
        public void PlayerTracks_StricterThanRoads_SwitchesTangential()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            float f = FarFraction(w);
            var p = Axis(w, f, out var dir, out var normal);
            float sgn = f < 0.5f ? 1f : -1f; // Richtung weg vom Kartenrand
            var d = dir * sgn;
            var main = rail.Tracks.FindNearestSegment(p + normal * 2.25f, 1f);
            Assert.NotNull(main);

            // Zu enger Bogen (Radius < 150 m)
            var tight = rail.PlanTrack(p + normal * 80f, p + normal * 80f + d * 60f, p + normal * 140f + d * 60f);
            Assert.Equal(RoadBuildError.TrackGeometry, tight.Error);
            // Zu steil (3,5 % für Gleise, Straßen dürften 10 %)
            var steep = rail.PlanTrack(p + normal * 80f, null, p + normal * 80f + d * 100f, 6f, 0f, 6f, followTerrain: false);
            Assert.Equal(RoadBuildError.TooSteep, steep.Error);
            var road = RoadBuilder.Plan(w.Roads, p + normal * 80f, 0f, null, p + normal * 80f + d * 100f, 0f, RoadCatalog.CityTwoLane, 0f, 6f);
            Assert.True(road.IsValid, "dieselbe Steigung ist für Straßen erlaubt");
            // Abzweig quer von der Hauptstrecke (30°) – keine Weiche
            var start = main.Evaluate(0.5f);
            var steepBranch = rail.PlanTrack(start, null, start + (d * MathF.Cos(0.52f) + normal * MathF.Sin(0.52f)) * 200f);
            Assert.Equal(RoadBuildError.TrackGeometry, steepBranch.Error);
            // Weiche: flach abzweigen (6°), dann parallel – gültig, zerteilt das Hauptgleis, Sperre bleibt
            int before = rail.Tracks.SegmentCount;
            var a0 = start;
            var a2 = start + d * 300f + normal * 30f;
            var ctrl = start + d * 150f + normal * 0f;
            var branch = rail.PlanTrack(a0, ctrl, a2);
            output.WriteLine($"Weiche: {branch.Error}, Radius {RailSystem.MinRadius(a0, ctrl, a2):0} m, Kosten {branch.Cost:0}");
            Assert.True(branch.IsValid, branch.Error.ToString());
            var built = rail.BuildTrack(branch);
            Assert.NotEmpty(built);
            Assert.True(rail.Tracks.SegmentCount > before + 1);
            Assert.All(rail.Tracks.Segments.Where(s => s.Type == RoadCatalog.RailMain), s => Assert.True(s.IsLocked));
            Assert.False(rail.DemolishTrack(rail.Tracks.Segments.First(s => s.IsLocked).Id));
            Assert.NotNull(rail.DemolishBlockReason(rail.Tracks.Segments.First(s => s.IsLocked).Id));
            // Fahrweg-Graph: vom Hauptgleis führt eine Verbindung ins neue Gleis
            var newSeg = built[0];
            var nodeAtSwitch = rail.Tracks.FindNearestNode(start, 1f);
            Assert.NotNull(nodeAtSwitch);
            Assert.Equal(3, nodeAtSwitch.SegmentIds.Count);
            // Kosten: „Bau Bahn“, Unterhalt
            var cw = w.Economy.CurrentWeek;
            Assert.True(cw.Amount(Economy.BudgetCategory.RailConstruction) > 0);
            Assert.True(rail.WeeklyUpkeep() > 0);
            Assert.True(rail.DemolishTrack(newSeg.Id));
        }

        // ------------------------------------------------------------ Straße ↔ Gleis

        [Fact]
        public void RoadAcrossTrack_LevelCrossingOrGradeSeparated_NeverAlong()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            var p = Axis(w, FarFraction(w), out var dir, out var normal);

            // Stadtstraße quer: Bahnübergang (je Gleis einer)
            var plan = RoadBuilder.Plan(w.Roads, p - normal * 150f, 0f, null, p + normal * 150f, 0f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
            Assert.Equal(2, rail.Crossings.Count);
            Assert.All(rail.Crossings, c => Assert.True(c.Angle > 80f));

            // Autobahn-Typ ebenerdig → nicht erlaubt
            var q = p + dir * 300f;
            var hw = RoadBuilder.Plan(w.Roads, q - normal * 150f, 0f, null, q + normal * 150f, 0f, RoadCatalog.Highway2);
            Assert.Equal(RoadBuildError.RailCrossing, hw.Error);
            // Brücke 3 m über dem Gleis: zu wenig lichte Höhe; 7 m: gut (keine neue Schranke)
            var low = RoadBuilder.Plan(w.Roads, q - normal * 150f, 0f, null, q + normal * 150f, 0f, RoadCatalog.CityTwoLane, 3f, 3f);
            Assert.Equal(RoadBuildError.InsufficientClearance, low.Error);
            var high = RoadBuilder.Plan(w.Roads, q - normal * 150f, 0f, null, q + normal * 150f, 0f, RoadCatalog.CityTwoLane, 7f, 7f);
            Assert.True(high.IsValid, high.Error.ToString());
            RoadBuilder.Build(w.Roads, high);
            Assert.Equal(2, rail.Crossings.Count);
            // Spitzer Winkel (20°) → kein Bahnübergang
            var r = p - dir * 300f;
            var skew = Vector2.Normalize(dir * MathF.Cos(0.35f) + normal * MathF.Sin(0.35f));
            var acute = RoadBuilder.Plan(w.Roads, r - skew * 150f, 0f, null, r + skew * 150f, 0f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.RailCrossing, acute.Error);
            // Längs auf dem Gleis → verboten
            var along = RoadBuilder.Plan(w.Roads, r + normal * 2f, 0f, null, r + normal * 2f + dir * 120f, 0f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.OverlapsRail, along.Error);
            // Bahnübergang zu nah an einer Straßenkreuzung (Straßenende 10 m vor dem Gleis)
            var near = RoadBuilder.Plan(w.Roads, r - normal * 150f, 0f, null, r + normal * 12f, 0f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.RailCrossing, near.Error);

            // Gleis quer über eine vorhandene Straße: Bahnübergang entsteht
            var s0 = p + normal * 120f - dir * 400f;
            Road(w, s0, s0 + dir * 800f); // Straße parallel zur Hauptstrecke
            var t0 = s0 - normal * 0f;
            int crossingsBefore = rail.Crossings.Count;
            var track = rail.PlanTrack(p + normal * 60f + dir * 100f, null, p + normal * 220f + dir * 100f);
            output.WriteLine($"Gleis quer über Straße: {track.Error}");
            Assert.True(track.IsValid, track.Error.ToString());
            rail.BuildTrack(track);
            Assert.Equal(crossingsBefore + 1, rail.Crossings.Count);
        }

        // ------------------------------------------------------------ Bahnhof

        [Fact]
        public void Station_SnapsToMainline_TwoPlatforms_NeedsRoadFront_OpensRailGateway()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            var p = Axis(w, FarFraction(w), out var dir, out var normal);
            var plan = rail.PlanStation(StationCatalog.Passenger, p + normal * 25f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(2, plan.PlatformTracks);
            Assert.False(plan.HasRoadAccess);
            Assert.True(Vector2.Dot(plan.Front, normal) > 0.99f, "Vorderseite zeigt vom Gleis weg");
            Assert.Equal(RailRules.PlatformGap + StationCatalog.Passenger.Depth * 0.5f + RailRules.TrackSpacing * 0.5f,
                Vector2.Dot(plan.Position - p, normal), 1);
            // ohne Gleis in der Nähe kein Bahnhof
            Assert.Equal(StationBuildError.NoTrack, rail.PlanStation(StationCatalog.Passenger, p + normal * 300f).Error);

            var st = rail.BuildStation(plan);
            Assert.NotNull(st);
            Assert.Equal(2, st.Platforms.Count);
            Assert.True(st.OnMainline);
            Assert.False(st.InService, "ohne Straße vor dem Bahnhof außer Betrieb");
            w.Step();
            Assert.All(w.Gateways.All.Where(g => g.Kind == GatewayKind.Rail), g => Assert.Equal(GatewayStatus.Planned, g.Status));
            // Straße vor dem Bahnhof → in Betrieb, Rail-Gateways befahrbar
            var front = st.FrontMid + st.Front * 7.5f;
            var side = new Vector2(-st.Front.Y, st.Front.X);
            Road(w, front - side * 100f, front + side * 100f);
            w.Step();
            Assert.True(st.HasAccess);
            Assert.True(st.InService);
            Assert.All(w.Gateways.All.Where(g => g.Kind == GatewayKind.Rail), g => Assert.True(g.IsOperational));
            // Straßen dürfen weder Gebäude noch Bahnsteig überbauen
            var through = RoadBuilder.Plan(w.Roads, st.Position - st.Front * 60f + side * 10f, 0f, null, st.Position + st.Front * 60f + side * 10f, 0f, RoadCatalog.CityTwoLane);
            Assert.False(through.IsValid);
            // Abriss: 25 % zurück
            double bal = w.Economy.Balance;
            Assert.True(rail.DemolishStation(st.Id));
            Assert.Equal(bal + StationCatalog.Passenger.Cost * Economy.EconomyRules.DemolitionRefundShare, w.Economy.Balance, 2);
        }

        // ------------------------------------------------------------ Züge

        [Fact]
        public void Intercity_StopsAtStation_ThenLeaves_FreightPassesThrough()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            var st = BuildConnectedStation(w, StationCatalog.Passenger, FarFraction(w), connectToHighway: false);
            w.Step();
            Assert.True(st.InService);
            var ic = rail.QueueTrain(TrainCatalog.Intercity, fromA: true);
            var fr = rail.QueueTrain(TrainCatalog.Freight, fromA: false);
            Assert.Single(ic.Stops(rail));
            Assert.Empty(fr.Stops(rail));
            bool dwelled = false, maxOk = true;
            float vmax = 0f;
            Substeps(w, 4000, () =>
            {
                if (ic.State == TrainState.Dwelling) dwelled = true;
                vmax = MathF.Max(vmax, ic.Speed);
                if (ic.Speed > TrainCatalog.Intercity.MaxSpeed + 0.01f) maxOk = false;
            });
            output.WriteLine($"Fernzug: Halt {dwelled}, vmax {vmax * 3.6f:0} km/h, fertig {rail.TrainsCompleted}, Halte am Bahnhof {st.StopsToday}");
            Assert.True(dwelled, "Fernzug hält am Bahnhof");
            Assert.True(maxOk);
            Assert.True(vmax > 100f / 3.6f);
            Assert.DoesNotContain(ic, rail.Trains);
            Assert.DoesNotContain(fr, rail.Trains);
            Assert.True(rail.TrainsCompleted >= 2);
            Assert.Equal(0, rail.TrainsCancelled);
            Assert.True(st.StopsToday + st.StopsYesterday >= 1);
            // Stationsentgelt in der Kasse (alle 5 Ticks gebucht)
            w.Step();
            Assert.True(rail.FaresTotal >= RailRules.IntercityStopFee);
        }

        [Fact]
        public void BlockSignals_FollowingTrainsNeverShareABlock()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            BuildConnectedStation(w, StationCatalog.Passenger, FarFraction(w), connectToHighway: false);
            w.Step();
            // Drei Züge kurz hintereinander auf demselben Gleis (der erste hält am Bahnhof)
            var list = new List<Train> { rail.QueueTrain(TrainCatalog.Intercity, true), rail.QueueTrain(TrainCatalog.Freight, true), rail.QueueTrain(TrainCatalog.Intercity, true) };
            int signalStops = 0, minGapViolations = 0;
            float minGap = float.MaxValue;
            Substeps(w, 6000, () =>
            {
                var on = rail.Trains.Where(t => t.State != TrainState.Waiting).ToList();
                foreach (var t in on) if (t.AtSignal) signalStops++;
                // gleicher Fahrweg (gleiche Einfahrt, gleiches Gleis): Abstand Spitze ↔ Ende des Vordermanns
                for (int i = 0; i < on.Count; i++)
                    for (int j = 0; j < on.Count; j++)
                    {
                        if (i == j || on[i].Id >= on[j].Id) continue; // i fuhr zuerst ein
                        var lead = on[i]; var follow = on[j];
                        if (lead.RouteEdges[0].SegmentId != follow.RouteEdges[0].SegmentId) continue;
                        // gemeinsames Stück: Ende des Vordermanns auf dem Fahrweg des Folgezugs
                        float tail = RailTestsHelper.TailOnFollowerRoute(rail, lead, follow);
                        if (float.IsNaN(tail)) continue;
                        float gap = tail - follow.HeadS;
                        minGap = MathF.Min(minGap, gap);
                        if (gap < 0f) minGapViolations++;
                    }
            });
            output.WriteLine($"Signalhalte (Schritte) {signalStops}, kleinster Abstand {minGap:0} m, Verstöße {minGapViolations}, fertig {rail.TrainsCompleted}, ausgefallen {rail.TrainsCancelled}");
            Assert.Equal(0, minGapViolations);
            Assert.True(signalStops > 0, "Folgezug wartet am Signal");
            Assert.All(list, t => Assert.DoesNotContain(t, rail.Trains)); // alle drei durch (Fahrplanzüge kommen dazu)
            Assert.Equal(0, rail.TrainsCancelled);
        }

        [Fact]
        public void StationOnPassingLoop_IntercityTakesTheLoop_WaitingTrainReplans()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            float f = FarFraction(w);
            var mid = Axis(w, f, out var dir, out var normal);
            Vector2 T1(float s) => mid + dir * s - normal * (RailRules.TrackSpacing * 0.5f); // Gleis 1 (A → B) rechts der Achse
            // Wartender Zug (Fahrweg geplant), bevor die Weichen ins Hauptgleis kommen
            var waiting = rail.QueueTrain(TrainCatalog.Freight, fromA: true);
            Assert.Equal(TrainState.Waiting, waiting.State);
            // Überholgleis 12 m neben Gleis 1: Weiche – Bogen hinaus – Gerade – Bogen zurück – Weiche
            var a = T1(-180f);
            var b = a + dir * 120f - normal * 12f;
            var c = b + dir * 120f;
            var d = c + dir * 120f + normal * 12f;
            foreach (var (s0, ctrl, s1) in new[] { (a, (Vector2?)(a + dir * 60f), b), (b, (Vector2?)null, c), (c, (Vector2?)(c + dir * 60f), d) })
            {
                var plan = rail.PlanTrack(s0, ctrl, s1);
                output.WriteLine($"Stück {s0} → {s1}: {plan.Error}");
                Assert.True(plan.IsValid, plan.Error.ToString());
                Assert.NotEmpty(rail.BuildTrack(plan));
            }
            // Bahnhof am Überholgleis (Hauptgleis 18 m entfernt → kein Bahnsteig dort)
            var stPlan = rail.PlanStation(StationCatalog.Passenger, (b + c) * 0.5f - normal * 30f);
            Assert.True(stPlan.IsValid, stPlan.Error.ToString());
            var st = rail.BuildStation(stPlan);
            Assert.Single(st.Platforms);
            var front = st.FrontMid + st.Front * 7.5f;
            var side = new Vector2(-st.Front.Y, st.Front.X);
            Road(w, front - side * 100f, front + side * 100f);
            w.Step();
            Assert.True(st.InService, $"Zufahrt {st.HasAccess}, Hauptstrecke {st.OnMainline}");
            Assert.Contains(waiting, rail.Trains); // neu geplant statt ausgefallen
            var loopSegs = rail.Tracks.Segments.Where(s => !s.IsLocked).Select(s => s.Id).ToHashSet();
            var ic = rail.QueueTrain(TrainCatalog.Intercity, fromA: true);
            Assert.Contains(ic.RouteEdges, e => loopSegs.Contains(e.SegmentId));
            Assert.Single(ic.Stops(rail));
            bool dwelled = false;
            Substeps(w, 4000, () => { if (ic.State == TrainState.Dwelling) dwelled = true; });
            output.WriteLine($"Fernzug über das Überholgleis: Halt {dwelled}, fertig {rail.TrainsCompleted}, ausgefallen {rail.TrainsCancelled}");
            Assert.True(dwelled);
            Assert.DoesNotContain(ic, rail.Trains);
            Assert.DoesNotContain(waiting, rail.Trains);
            Assert.Equal(0, rail.TrainsCancelled);
        }

        // ------------------------------------------------------------ Bahnübergang

        [Fact]
        public void LevelCrossing_ClosesForTrain_CarsWait_NoCarOnTrackWhileTrainPasses()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            var p = Axis(w, FarFraction(w), out var dir, out var normal);
            var a = p - normal * 300f; var b = p + normal * 300f;
            Road(w, a, b);
            Assert.Equal(2, rail.Crossings.Count);
            w.Traffic.Lanes.Update();
            var na = w.Roads.FindNearestNode(a, 1f); var nb = w.Roads.FindNearestNode(b, 1f);
            var train = rail.QueueTrain(TrainCatalog.Freight, fromA: true);
            int tripId = 1, closedSteps = 0, waitingAtBarrier = 0, carsOnTrackWhileTrain = 0;
            Substeps(w, 3000, () =>
            {
                if (w.Traffic.Vehicles.Count < 30 && w.SubstepInTick % 8 == 0)
                {
                    w.Traffic.RequestTrip(TripEnd.AtNode(na), TripEnd.AtNode(nb), VehicleClass.Car, tripId++);
                    w.Traffic.RequestTrip(TripEnd.AtNode(nb), TripEnd.AtNode(na), VehicleClass.Car, tripId++);
                }
                bool closed = rail.Crossings.Any(c => c.Closed);
                if (closed) closedSteps++;
                bool trainOnCrossing = rail.Trains.Any(t => t.IsOnMap && rail.Crossings.Any(c => RailTestsHelper.Covers(rail, t, c)));
                foreach (var v in w.Traffic.ActiveVehicles)
                {
                    if (v.Lane == null) continue;
                    foreach (var c in rail.CrossingsOnRoad(v.Lane.Segment.Id))
                    {
                        float sc = v.Lane.Along ? c.RoadT * v.Lane.Length : (1f - c.RoadT) * v.Lane.Length;
                        float d = sc - v.S;
                        if (c.Closed && v.Speed < 0.3f && d > 0f && d < c.HalfSpan + 15f) waitingAtBarrier++;
                        if (trainOnCrossing && MathF.Abs(d) < 3f && c.Closed) carsOnTrackWhileTrain++;
                    }
                }
            });
            output.WriteLine($"geschlossen {closedSteps} Schritte, wartende Fahrzeug-Schritte {waitingAtBarrier}, Fahrzeuge auf dem Gleis bei Zug {carsOnTrackWhileTrain}");
            Assert.True(closedSteps > 40, "Schranke war geschlossen");
            Assert.True(waitingAtBarrier > 0, "Autos warten vor der Schranke");
            Assert.Equal(0, carsOnTrackWhileTrain);
            Assert.All(rail.Crossings, c => Assert.False(c.Closed)); // nach dem Zug wieder offen
            Assert.True(rail.Crossings.Sum(c => c.ClosuresToday) >= 2);
        }

        // ------------------------------------------------------------ Fahrgäste, Zuzug

        [Fact]
        public void MoveIn_ByRailOnly_PassengersRideTrains_FaresBooked()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            w.Economy.UnlimitedFunds = true;
            var rail = w.Rail;
            // Bahnhof mit Straße – NICHT an die Autobahn angeschlossen: Zuzug nur per Bahn
            var st = BuildConnectedStation(w, StationCatalog.Passenger, FarFraction(w), connectToHighway: false);
            var side = new Vector2(-st.Front.Y, st.Front.X);
            var front = st.FrontMid + st.Front * 7.5f;
            Road(w, front, front + st.Front * 200f);
            w.City.Paint(front + st.Front * 100f + side * 25f, 80f, ZoneType.ResidentialLow);
            w.City.Paint(front + st.Front * 100f - side * 25f, 80f, ZoneType.ResidentialLow);
            for (int day = 0; day < 3; day++) for (int i = 0; i < SimulationClock.TicksPerDay; i++) w.Step();
            var rg = w.Gateways.All.Where(g => g.Kind == GatewayKind.Rail).ToList();
            output.WriteLine($"Einwohner {w.Citizens.Stats.Population}, Zuzug über Bahn {rg.Sum(g => g.TotalMoveIns)}, Fahrgäste gesamt {rail.PassengersTotal}, " +
                             $"Bahnhof aus/ein {st.AlightedYesterday}/{st.BoardedYesterday}, Züge {rail.TrainsCompleted}, Entgelte {rail.FaresTotal:0} €, aufgegeben heute {rail.GaveUpToday}");
            Assert.True(w.Citizens.Stats.Population > 10, $"Einwohner {w.Citizens.Stats.Population}");
            Assert.True(rg.Sum(g => g.TotalMoveIns) > 3, "Zuzug über die Bahn");
            Assert.True(rail.PassengersTotal > 3, "Fahrgäste steigen am Bahnhof aus");
            Assert.True(rail.FaresTotal > RailRules.IntercityStopFee);
            Assert.All(w.Highways(), g => Assert.Equal(0, g.TotalMoveIns));
            // Ohne Jobs in der Stadt pendeln Einwohner per Bahn hinaus (Bahnhof = einziger Anschluss)
            output.WriteLine($"Auspendler {w.Citizens.Stats.CommutersOut}, eingestiegen gesamt {st.TotalPassengers}");
            Assert.True(w.Citizens.Stats.CommutersOut > 0, "Auspendler über den Bahnhof");
            Assert.True(rail.Stations[0].BoardedYesterday + rail.Stations[0].BoardedToday > 0, "Pendler steigen in den Fernzug");
        }

        // ------------------------------------------------------------ Güter

        [Fact]
        public void FreightStation_TradeRunsViaStation_CheaperFreight()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var rail = w.Rail;
            var st = BuildConnectedStation(w, StationCatalog.Freight, FarFraction(w), connectToHighway: false);
            w.Step();
            Assert.True(st.InService, $"Zufahrt {st.HasAccess}, Hauptstrecke {st.OnMainline}");
            int comp = w.Citizens.Components.OfNode(st.AccessNodeId);
            var node = w.Goods.PreferredGateway(Goods.GoodsCatalog.All[0], true, false, comp);
            Assert.NotNull(node);
            Assert.Equal(st.AccessNodeId, node.Id);
            Assert.Equal(RailRules.RailFreightShare, w.Goods.FreightShareFor(node), 6);
            Assert.True(RailRules.RailFreightShare < Goods.GoodsRules.WorldFreightShare);
            // Güterzug hält am Güterbahnhof (Umschlag), Fernzug nicht
            var fr = rail.QueueTrain(TrainCatalog.Freight, fromA: true);
            var ic = rail.QueueTrain(TrainCatalog.Intercity, fromA: false);
            Assert.Single(fr.Stops(rail));
            Assert.Empty(ic.Stops(rail));
            Assert.Same(rail.GatewayOf(true), w.Gateways.OfNode(st.AccessNodeId));
        }

        [Fact]
        public void FreightStation_FirmImportsByRail_CheaperThanTruck_LoadsCounted()
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain: false, world: true, worldSeed: 99));
            Assert.NotNull(w.Trade);
            var rail = w.Rail;
            var st = BuildConnectedStation(w, StationCatalog.Freight, FarFraction(w), connectToHighway: false);
            var side = new Vector2(-st.Front.Y, st.Front.X);
            var front = st.FrontMid + st.Front * 7.5f;
            Road(w, front, front + st.Front * 260f);
            w.City.Paint(front + st.Front * 130f + side * 25f, 60f, ZoneType.Industry);
            w.City.Population.Residents = 3000;
            w.City.Population.WorkingAge = 1800;
            for (int i = 0; i < 60; i++) w.City.ForceTick();
            w.Step();
            Assert.True(st.InService);
            var firms = w.Goods.Firms.Where(f => f.Zone == ZoneType.Industry).ToList();
            Assert.NotEmpty(firms);
            foreach (var b in w.City.Buildings) b.Workers = b.Jobs;
            Assert.True(w.Goods.Restructure(firms[0], Goods.RecipeCatalog.Get("mill")));
            var grain = Goods.GoodsCatalog.Grain;
            var mill = firms[0];
            mill.InputStock[grain.Index] = 0f;
            mill.Incoming[grain.Index] = 0f;
            w.Goods.Market(grain).LocalPrice = w.Goods.Market(grain).WorldPrice * 1.6f; // Import lohnt
            int before = w.Goods.Shipments.Count;
            w.Goods.ForceStep();
            var imports = w.Goods.Shipments.Skip(before).Where(s => s.Kind == Goods.ShipmentKind.Import && s.Good == grain && s.ToBuildingId == mill.BuildingId).ToList();
            Assert.NotEmpty(imports);
            Assert.All(imports, s => Assert.Equal(st.AccessNodeId, s.ConnectionNodeId));
            foreach (var s in imports)
            {
                double road = s.Value * Goods.GoodsRules.WorldFreightShare + s.TruckEstimate;
                output.WriteLine($"Import {s.Amount:0.0} {s.Good.Name}: Wert {s.Value:0} €, Fracht Bahn {s.TransportCost:0.00} € (per Lkw über die Autobahn wären es ≥ {road:0.00} €)");
                Assert.True(s.TransportCost < road, "Bahnfracht billiger");
            }
            w.Step();
            Assert.True(st.LoadsToday >= imports.Count, $"Umschlag {st.LoadsToday}");
            Assert.True(st.PendingLoads > 0);
            Assert.True(rail.GatewayOf(true).Today.Imports + rail.GatewayOf(false).Today.Imports >= 0);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_MidTrainRun_ContinuesBitIdentical()
        {
            var w = SimulationWorld.CreateDefault(Settings());
            var st = BuildConnectedStation(w, StationCatalog.Passenger, FarFraction(w), connectToHighway: false);
            var p = Axis(w, FarFraction(w), out var dir, out var normal);
            Road(w, p - normal * 300f - dir * 120f, p + normal * 300f - dir * 120f); // Bahnübergang
            w.Rail.QueueTrain(TrainCatalog.Intercity, true);
            w.Rail.QueueTrain(TrainCatalog.Freight, false);
            for (int i = 0; i < 700; i++) w.Substep();
            Assert.Contains(w.Rail.Trains, t => t.IsOnMap);
            var bytes = SaveGame.SaveToBytes(w);
            var b = SaveGame.Load(bytes);
            Assert.Equal(SaveGame.StateHash(w), SaveGame.StateHash(b));
            for (int i = 0; i < 2500; i++) { w.Substep(); b.Substep(); }
            var ha = SaveGame.SectionHashes(w);
            var hb = SaveGame.SectionHashes(b).ToDictionary(x => x.Key, x => x.Value);
            foreach (var kv in ha) Assert.True(hb.TryGetValue(kv.Key, out var v) && v == kv.Value, $"Abschnitt {kv.Key} weicht ab");
            Assert.Equal(w.Rail.TrainsCompleted, b.Rail.TrainsCompleted);
            Assert.True(w.Rail.TrainsCompleted >= 1);
            Assert.Equal(w.Rail.Crossings.Count, b.Rail.Crossings.Count);
            Assert.Equal(st.Name, b.Rail.Stations[0].Name);
        }

        [Fact]
        public void WithoutPrebuiltConnections_NoRail_NothingChanges()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings());
            Assert.False(w.Rail.HasMainline);
            Assert.Equal(0, w.Rail.Tracks.SegmentCount);
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            Assert.Empty(w.Rail.Crossings);
            for (int i = 0; i < 50; i++) w.Step();
            Assert.Empty(w.Rail.Trains);
        }
    }

    static class RailTestsHelper
    {
        public static IEnumerable<(int station, float s)> Stops(this Train t, RailSystem rail) => rail.StopsOf(t);

        public static IEnumerable<Gateway> Highways(this SimulationWorld w) => w.Gateways.All.Where(g => g.Kind == GatewayKind.Highway);

        /// <summary>Zugende des Vordermanns auf dem Fahrweg des Folgezugs (NaN, wenn nicht auf demselben Stück).</summary>
        public static float TailOnFollowerRoute(RailSystem rail, Train lead, Train follow)
        {
            float tail = lead.HeadS - lead.Length;
            if (tail < 0f) return float.NaN;
            rail.Sample(lead, lead.HeadS, lead.Length, out var pos, out _, out _);
            // Lage auf dem Fahrweg des Folgezugs über das gemeinsame Gleisstück
            for (int i = 0; i < follow.RouteEdges.Count; i++)
            {
                var e = follow.RouteEdges[i];
                var seg = rail.Tracks.GetSegment(e.SegmentId);
                if (seg == null) continue;
                float t = seg.ClosestT(pos);
                if (Vector2.Distance(seg.Evaluate(t), pos) > 0.5f) continue;
                return rail.RouteStartOf(follow, i) + (e.Along ? t : 1f - t) * seg.Length;
            }
            return float.NaN;
        }

        public static bool Covers(RailSystem rail, Train t, LevelCrossing c)
        {
            for (int i = 0; i < t.RouteEdges.Count; i++)
            {
                if (t.RouteEdges[i].SegmentId != c.RailSegmentId) continue;
                float s = rail.RouteStartOf(t, i) + (t.RouteEdges[i].Along ? c.RailT : 1f - c.RailT) * rail.Tracks.GetSegment(c.RailSegmentId).Length;
                return s <= t.HeadS && s >= t.HeadS - t.Length;
            }
            return false;
        }
    }
}
