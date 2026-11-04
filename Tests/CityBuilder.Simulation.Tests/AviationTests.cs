using System;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Aviation;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Flughafen (Stufe 4): Bahnlänge nach Kartengröße, Platzregeln (Straße/Wasser/Gelände/Karte, Einebnen kostet), Hindernisfreiheit
    /// (bestehende Hindernisse sperren/beschränken, neue Hochbauten ungültig), Luft-Gateway, Flugbetrieb (sichtbare Flugzeuge mit
    /// Anflug → Rollen → Parken → Start, statistische Umläufe), Nachtflugverbot, Fluglärm, Tourismus/Gebühren, Speichern bitgleich.
    /// </summary>
    public class AviationTests
    {
        readonly ITestOutputHelper output;
        public AviationTests(ITestOutputHelper output) => this.output = output;

        static SimulationWorld Flat()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings());
            w.Step();
            return w;
        }

        /// <summary>Stadtstraße 3 m vor der Landseite des Terminals, parallel zur Bahn.</summary>
        static void TerminalRoad(SimulationWorld w, Airport a)
        {
            var l = a.Layout;
            var t = l.Terminal;
            float off = l.TerminalFront + RoadCatalog.CityTwoLane.HalfWidth + 3f;
            var p1 = l.At(-t.Width * 0.5f - 60f, off);
            var p2 = l.At(t.Width * 0.5f + 60f, off);
            var plan = RoadBuilder.Plan(w.Roads, p1, 8f, null, p2, 8f, RoadCatalog.CityTwoLane, w.Terrain.HeightAt(p1), w.Terrain.HeightAt(p2), RoadSegmentFlags.None, false);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static Airport BuildAirport(SimulationWorld w, AirportType type, Vector2 center, Vector2 dir, int side = 1)
        {
            var plan = w.Aviation.PlanAirport(type, center, dir, side);
            Assert.True(plan.IsValid, AviationSystem.ErrorText(plan));
            var a = w.Aviation.BuildAirport(plan);
            Assert.NotNull(a);
            TerminalRoad(w, a);
            w.Step();
            return a;
        }

        // ------------------------------------------------------------ Grundregeln

        [Fact]
        public void RunwayLength_FollowsMapSize_AndCatalog()
        {
            Assert.Equal(1200f, AirportCatalog.Airfield.RunwayLengthFor(2048f));
            Assert.Equal(1800f, AirportCatalog.Airport.RunwayLengthFor(2048f));
            Assert.Equal(2800f, AirportCatalog.Airport.RunwayLengthFor(4096f));
            Assert.Equal("09/27", AviationRules.RunwayDesignator(Vector2.UnitX));
            Assert.Equal("18/36", AviationRules.RunwayDesignator(Vector2.UnitY));
            // Westwinde in mittleren Breiten, Passat aus Osten in den Tropen
            Assert.True(AviationRules.PrevailingWindFrom(50f, 1).X < -0.5f);
            Assert.True(AviationRules.PrevailingWindFrom(12f, 1).X > 0.5f);
            Assert.InRange(AviationRules.GreatCircleKm(52.5f, 13.4f, 48.1f, 11.6f), 480f, 520f); // Berlin–München ≈ 504 km
        }

        [Fact]
        public void Plan_FlatSite_Valid_BuildFlattensAndBooks()
        {
            var w = Flat();
            // echte Kasse: Startkapital reicht nicht
            w.Economy.UnlimitedFunds = false;
            Assert.Equal(AirportBuildError.InsufficientFunds, w.Aviation.PlanAirport(AirportCatalog.Airfield, Vector2.Zero, Vector2.UnitX, 1).Error);
            w.Economy.UnlimitedFunds = true; // Sandbox: wird gebucht, nie blockiert
            double before = w.Economy.Balance;
            var plan = w.Aviation.PlanAirport(AirportCatalog.Airfield, Vector2.Zero, Vector2.UnitX, 1);
            Assert.True(plan.IsValid, AviationSystem.ErrorText(plan));
            Assert.Equal(1200f, plan.RunwayLength);
            Assert.Equal(CorridorState.Clear, plan.CorridorA);
            Assert.Equal(1f, plan.CapacityFactor);
            Assert.False(plan.HasRoadAccess);
            var a = w.Aviation.BuildAirport(plan);
            Assert.NotNull(a);
            Assert.Equal(before - plan.TotalCost, w.Economy.Balance, 1);
            Assert.True(MathF.Abs(w.Terrain.HeightAt(a.Center) - a.Height) < 0.2f);
            Assert.False(a.InService); // keine Straße vor dem Terminal
            TerminalRoad(w, a);
            w.Step();
            Assert.True(a.HasAccess);
            Assert.True(a.InService);
            // Straßen dürfen nicht über die Bahn
            var p1 = a.Center + new Vector2(0, -150);
            var p2 = a.Center + new Vector2(0, 150);
            var rp = RoadBuilder.Plan(w.Roads, p1, 8f, null, p2, 8f, RoadCatalog.CityTwoLane, 1.5f, 1.5f, RoadSegmentFlags.None, false);
            Assert.False(rp.IsValid);
            Assert.Equal(RoadBuildError.OverlapsStructure, rp.Error);
        }

        [Fact]
        public void Plan_Errors_Road_OutOfWorld_Hilly_Water()
        {
            var w = Flat();
            // Straße quer über das Gelände
            var r = RoadBuilder.Plan(w.Roads, new Vector2(0, -300), 8f, null, new Vector2(0, 300), 8f, RoadCatalog.CityTwoLane, 0f, 0f, RoadSegmentFlags.None, false);
            RoadBuilder.Build(w.Roads, r);
            var plan = w.Aviation.PlanAirport(AirportCatalog.Airfield, Vector2.Zero, Vector2.UnitX, 1);
            Assert.Equal(AirportBuildError.OverlapsRoad, plan.Error);
            // zu weit am Rand
            plan = w.Aviation.PlanAirport(AirportCatalog.Airport, new Vector2(500, 0), Vector2.UnitX, 1);
            Assert.Equal(AirportBuildError.OutOfWorld, plan.Error);
            // Hügel (Terraforming 40 m) → zu uneben, Erdmenge
            var w2 = Flat();
            for (int i = 0; i < 40; i++)
                w2.Terrain.Terraform(new Simulation.Environment.TerraformBrush(Simulation.Environment.TerraformMode.Raise, new Vector2(300, 60), 60f, 2f, 0.3f));
            plan = w2.Aviation.PlanAirport(AirportCatalog.Airfield, new Vector2(0, 0), Vector2.UnitX, 1);
            Assert.Equal(AirportBuildError.TooHilly, plan.Error);
            // kleinere Anhöhe: gültig, aber Erdarbeiten kosten
            var w3 = Flat();
            for (int i = 0; i < 5; i++)
                w3.Terrain.Terraform(new Simulation.Environment.TerraformBrush(Simulation.Environment.TerraformMode.Raise, new Vector2(300, 60), 60f, 2f, 0.3f));
            plan = w3.Aviation.PlanAirport(AirportCatalog.Airfield, new Vector2(0, 0), Vector2.UnitX, 1);
            Assert.True(plan.IsValid, AviationSystem.ErrorText(plan));
            Assert.True(plan.EarthworkCost > 1000, $"Erdarbeiten {plan.EarthworkCost:0} €");
            output.WriteLine($"Erdmenge {plan.EarthVolume:0} m³ → {plan.EarthworkCost:0} €");
        }

        // ------------------------------------------------------------ Hindernisfreiheit

        [Fact]
        public void Obstacles_TvTowerInCore_ClosesCorridor_BothBlocks()
        {
            var w = Flat();
            var d = Vector2.Normalize(new Vector2(1f, 1f));
            var l = new AirportLayout(AirportCatalog.Airport, Vector2.Zero, d, 1, 1800f, 1);
            // TV-Turm 300 m vor Bahnende A (Diagonale: die Schneisen liegen in der 2-km-Karte) (Kernzone der Anflugfläche A)
            var tvA = w.Services.Plan(ServiceCatalog.TvTower, l.EndA - d * 300f, Vector2.UnitY);
            Assert.True(tvA.IsValid, tvA.Error.ToString());
            w.Services.Build(tvA);
            var plan = w.Aviation.PlanAirport(AirportCatalog.Airport, Vector2.Zero, d, 1);
            Assert.True(plan.IsValid, AviationSystem.ErrorText(plan));
            Assert.Equal(CorridorState.Closed, plan.CorridorA);
            Assert.Equal(CorridorState.Clear, plan.CorridorB);
            Assert.Contains(plan.Obstacles, o => o.Core && o.Kind == ObstacleKind.Service);
            // zweiter Turm in der Kernzone B: beide Schneisen gesperrt → kein Betrieb möglich
            var tvB = w.Services.Plan(ServiceCatalog.TvTower, l.EndB + d * 300f, Vector2.UnitY);
            Assert.True(tvB.IsValid, tvB.Error.ToString());
            w.Services.Build(tvB);
            plan = w.Aviation.PlanAirport(AirportCatalog.Airport, Vector2.Zero, d, 1);
            Assert.Equal(AirportBuildError.ApproachBlocked, plan.Error);
        }

        [Fact]
        public void Obstacles_ExistingLimitsCapacity_CounterFlow_AndNewTallBuildingsInvalid()
        {
            var w = Flat();
            var d = Vector2.Normalize(new Vector2(1f, 1f));
            var a = BuildAirport(w, AirportCatalog.Airport, Vector2.Zero, d);
            var l = a.Layout;
            Assert.Equal(CorridorState.Clear, a.CorridorA);
            float cap0 = w.Aviation.MovementCapacity(a);
            // neue Hochbauten in der Einflugschneise: ungültig mit Hinweis
            var tv = w.Services.Plan(ServiceCatalog.TvTower, l.EndA - d * 400f, Vector2.UnitY);
            Assert.Equal(ServiceBuildError.ObstacleClearance, tv.Error);
            Assert.Contains("Einflugschneise", tv.ObstacleNote);
            var wind = w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, l.EndB + d * 300f, Vector2.UnitY);
            Assert.Equal(UtilityBuildError.ObstacleClearance, wind.Error);
            var house = w.City.PlanHouseAt(HouseCatalog.High, l.EndA - d * 200f, Vector2.UnitY);
            Assert.Equal(HouseBuildError.OverlapsStructure, house.Error);
            Assert.Contains("Einflugschneise", house.BlockerName);
            output.WriteLine(house.BlockerName);
            // niedriges Gebäude weit draußen ist erlaubt (Prüfung Höhe gegen Fläche)
            Assert.Null(w.Aviation.ObstacleNote(l.EndA - d * 900f, 8f, 9f, "Einfamilienhaus"));
            Assert.NotNull(w.Aviation.ObstacleNote(l.EndA - d * 100f, 8f, 9f, "Einfamilienhaus"));
            // bestehendes Hindernis (Gelände nachträglich angehoben) in der Kernzone A → Gegenrichtungsbetrieb, Kapazität sinkt
            for (int i = 0; i < 12; i++)
                w.Terrain.Terraform(new Simulation.Environment.TerraformBrush(Simulation.Environment.TerraformMode.Raise, l.EndA - d * 220f, 30f, 2f, 0.3f));
            for (int i = 0; i < TimeModel.TicksPerHour; i++) w.Step();
            Assert.Equal(CorridorState.Closed, a.CorridorA);
            Assert.True(a.InService);
            Assert.Equal(-1, a.ArrivalDirection);  // Landung über Ende B (Richtung B → A)
            Assert.Equal(1, a.DepartureDirection); // Start Richtung B
            Assert.True(a.CounterFlow);
            Assert.True(w.Aviation.MovementCapacity(a) < cap0 * 0.6f);
            Assert.NotNull(w.Aviation.Warning);
            output.WriteLine($"Kapazität {cap0:0.0} → {w.Aviation.MovementCapacity(a):0.0} · {w.Aviation.Warning}");
        }

        // ------------------------------------------------------------ Gateway + Flugbetrieb

        [Fact]
        public void Gateway_BecomesOperational_WithLocation()
        {
            var settings = new WorldSettings { PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true };
            var w = SimulationWorld.CreateDefault(settings);
            w.Step();
            var air = w.Gateways.All.Single(g => g.Kind == GatewayKind.Air);
            Assert.False(air.IsOperational);
            Assert.False(air.HasLocation);
            // freien Platz suchen (Autobahn und Bahnkorridor laufen durch die Karte)
            AirportPlan found = null;
            for (int ang = 0; ang < 180 && found == null; ang += 15)
                for (int x = -300; x <= 300 && found == null; x += 100)
                    for (int y = -700; y <= 700 && found == null; y += 100)
                    {
                        float r = ang * MathF.PI / 180f;
                        var p = w.Aviation.PlanAirport(AirportCatalog.Airfield, new Vector2(x, y), new Vector2(MathF.Cos(r), MathF.Sin(r)), 1);
                        if (p.IsValid) found = p;
                    }
            Assert.NotNull(found);
            var a = w.Aviation.BuildAirport(found);
            TerminalRoad(w, a);
            w.Step();
            Assert.True(a.InService);
            Assert.True(air.IsOperational, air.StatusText);
            Assert.True(air.HasLocation);
            Assert.True(Vector2.Distance(air.Position, a.Layout.Terminal.FrontMid) < 1f);
            Assert.NotEmpty(w.Aviation.Destinations);
            output.WriteLine($"{air.Name} · {air.TargetName} · {air.StatusText}");
        }

        [Fact]
        public void Flights_VisibleAircraft_LandTaxiParkDepart_FeesAndVisitors()
        {
            var w = Flat();
            var a = BuildAirport(w, AirportCatalog.Airport, Vector2.Zero, Vector2.UnitX);
            var seen = new System.Collections.Generic.HashSet<AircraftState>();
            int maxAircraft = 0;
            for (int i = 0; i < TimeModel.TicksPerDay * TimeModel.TrafficStepsPerTick; i++)
            {
                w.Substep();
                foreach (var x in w.Aviation.AllAircraft)
                {
                    seen.Add(x.State);
                    Assert.True(w.Aviation.TryPose(x, x.S, out var pos, out float alt, out _) || x.State == AircraftState.Approach);
                    if (x.State == AircraftState.Parked) Assert.True(a.Layout.Site.Contains(pos), "Flugzeug parkt auf dem Vorfeld");
                    Assert.True(alt >= 0f);
                }
                maxAircraft = Math.Max(maxAircraft, w.Aviation.AllAircraft.Count);
            }
            foreach (var s in new[] { AircraftState.Approach, AircraftState.Rollout, AircraftState.TaxiIn, AircraftState.Parked, AircraftState.TaxiOut, AircraftState.Takeoff })
                Assert.Contains(s, seen);
            Assert.True(a.ArrivalsYesterday + a.ArrivalsToday > 5, $"Landungen {a.ArrivalsYesterday}/{a.ArrivalsToday}");
            Assert.True(a.DeparturesYesterday + a.DeparturesToday > 3);
            Assert.True(a.PaxInYesterday + a.PaxInToday > 100);
            Assert.True(w.Aviation.FeesTotal > 0);
            Assert.True(w.Economy.CurrentWeek.Amount(BudgetCategory.AirportFees) + (w.Economy.LastWeek?.Amount(BudgetCategory.AirportFees) ?? 0) > 0);
            Assert.True(w.Aviation.VisitorsPresent > 0 || a.TouristsYesterday > 0);
            output.WriteLine($"Landungen {a.ArrivalsYesterday}, Starts {a.DeparturesYesterday}, Pax {a.PaxInYesterday}/{a.PaxOutYesterday}, " +
                             $"Touristen {a.TouristsYesterday}, Besucher {w.Aviation.VisitorsPresent:0}, Gebühren {w.Aviation.FeesTotal:0} €, max. {maxAircraft} Flugzeuge");
        }

        [Fact]
        public void NightBan_NoMovementsAtNight_LessNoise()
        {
            var w = Flat();
            var a = BuildAirport(w, AirportCatalog.Airport, Vector2.Zero, Vector2.UnitX);
            w.Aviation.SetNightBan(true);
            int nightMovements = 0, dayMovements = 0;
            float nightNoise = 0f, dayNoise = 0f;
            var probe = a.Layout.At(-a.Layout.HalfLength + 60f, 0f); // Bahnende (die Schneisen liegen bei Ost-West-Lage außerhalb der Karte)
            for (int t = 0; t < TimeModel.TicksPerDay * 2; t++)
            {
                int before = a.TotalMovements;
                w.Step();
                int hour = w.Clock.Hour;
                int moved = a.TotalMovements - before;
                if (AviationSystem.IsNightHour(hour) && hour != AviationRules.NightEndHour) nightMovements += moved;
                else dayMovements += moved;
                float n = w.Aviation.AircraftNoiseAt(probe);
                if (hour == 3) nightNoise = MathF.Max(nightNoise, n);
                if (hour == 18) dayNoise = MathF.Max(dayNoise, n);
            }
            Assert.Equal(0, nightMovements);
            Assert.True(dayMovements > 5);
            Assert.True(dayNoise > 0.2f, $"Fluglärm am Tag {dayNoise:0.00}");
            Assert.True(nightNoise < 0.05f, $"Fluglärm nachts {nightNoise:0.00}");
            // Fluglärm fließt in das Lärm-Raster der Stadt
            output.WriteLine($"Bewegungen Tag {dayMovements}, Nacht {nightMovements}; Lärm Tag {dayNoise:0.00}, Nacht {nightNoise:0.00}");
        }

        [Fact]
        public void Noise_AlongApproach_StrongerThanSideways_InCityNoise()
        {
            var w = Flat();
            var a = BuildAirport(w, AirportCatalog.Airport, Vector2.Zero, Vector2.UnitX);
            for (int t = 0; t < TimeModel.TicksPerDay / 2 + 30; t++) w.Step(); // bis in den Nachmittag
            var l = a.Layout;
            float inCorridor = w.Aviation.AircraftNoiseAt(l.EndA - Vector2.UnitX * 100f) + w.Aviation.AircraftNoiseAt(l.EndB + Vector2.UnitX * 100f);
            float sideways = w.Aviation.AircraftNoiseAt(l.At(0f, -600f));
            Assert.True(inCorridor > 0.3f, $"Schneise {inCorridor:0.00}");
            Assert.True(sideways < inCorridor * 0.3f, $"seitlich {sideways:0.00}");
            Assert.True(w.Environment.Noise.Sample(l.EndA - Vector2.UnitX * 100f) >= w.Aviation.AircraftNoiseAt(l.EndA - Vector2.UnitX * 100f) - 1e-4f);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_AircraftUnderway_ContinuesBitIdentical()
        {
            var w = Flat();
            var a = BuildAirport(w, AirportCatalog.Airport, Vector2.Zero, Vector2.UnitX);
            w.Aviation.SetNightBan(true);
            // bis Flugzeuge unterwegs sind (mitten im Tick)
            for (int i = 0; i < TimeModel.TicksPerDay * TimeModel.TrafficStepsPerTick && !(w.Aviation.AllAircraft.Count >= 2 && w.Aviation.AllAircraft.Any(x => x.State != AircraftState.Approach)); i++) w.Substep();
            for (int i = 0; i < 37; i++) w.Substep();
            Assert.NotEmpty(w.Aviation.AllAircraft);
            var bytes = SaveGame.SaveToBytes(w);
            var c = SaveGame.Load(bytes);
            Assert.Equal(SaveGame.StateHash(w), SaveGame.StateHash(c));
            Assert.Equal(w.Aviation.AllAircraft.Count, c.Aviation.AllAircraft.Count);
            Assert.True(c.Aviation.NightBan);
            for (int i = 0; i < 6000; i++) { w.Substep(); c.Substep(); }
            var ha = SaveGame.SectionHashes(w);
            var hb = SaveGame.SectionHashes(c).ToDictionary(x => x.Key, x => x.Value);
            foreach (var kv in ha) Assert.True(hb.TryGetValue(kv.Key, out var v) && v == kv.Value, $"Abschnitt {kv.Key} weicht ab");
            Assert.Equal(a.TotalMovements, c.Aviation.Airports[0].TotalMovements);
            output.WriteLine($"Bewegungen {a.TotalMovements}, Flugzeuge {w.Aviation.AllAircraft.Count}");
        }

        [Fact]
        public void WithoutAirport_NothingChanges()
        {
            var w = Flat();
            for (int i = 0; i < 30; i++) w.Step();
            Assert.Empty(w.Aviation.Airports);
            Assert.Empty(w.Aviation.AllAircraft);
            Assert.Equal(0f, w.Aviation.AircraftNoiseAt(Vector2.Zero));
            Assert.Null(w.Aviation.Warning);
        }
    }
}
