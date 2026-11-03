using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Transit;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class TransitTests
    {
        readonly ITestOutputHelper output;
        public TransitTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>Quadrat 400 × 300 m, Depot unten in der Mitte, 4 Haltestellenpaare (eine je Seite).</summary>
        static SimulationWorld Square(out BusStop[] stops, out TransitDepot depot)
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, -150), new Vector2(200, -150));
            Road(w, new Vector2(200, -150), new Vector2(200, 150));
            Road(w, new Vector2(200, 150), new Vector2(-200, 150));
            Road(w, new Vector2(-200, 150), new Vector2(-200, -150));
            var t = w.Transit;
            depot = t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(0, -175));
            Assert.NotNull(depot);
            stops = new[]
            {
                t.TryBuildStop(new Vector2(-50, -150), bothSides: true),
                t.TryBuildStop(new Vector2(200, 0), bothSides: true),
                t.TryBuildStop(new Vector2(50, 150), bothSides: true),
                t.TryBuildStop(new Vector2(-200, 0), bothSides: true),
            };
            Assert.All(stops, s => Assert.NotNull(s));
            return w;
        }

        static Citizen TestCitizen(int id) => new Citizen { Id = id, Age = 35, Personality = Personality.Neutral };

        [Fact]
        public void Stop_SnapsToRoadSide_AndRejectsBadPlaces()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var t = w.Transit;
            // Segment läuft nach +x → rechte Seite (Süden, y < 0) ist „in Segmentrichtung“
            var south = t.PlanStop(new Vector2(-50, -5));
            var north = t.PlanStop(new Vector2(50, 5));
            Assert.True(south.IsValid, south.Error.ToString());
            Assert.Equal(StopSide.Along, south.Side);
            Assert.Equal(StopSide.Against, north.Side);
            Assert.True(MathF.Abs(south.Position.Y) < 0.5f, "rastet auf der Straße ein");
            Assert.Equal(TransitBuildError.NoRoad, t.PlanStop(new Vector2(0, 80)).Error);
            Assert.NotNull(t.BuildStop(south));
            Assert.Equal(TransitBuildError.TooCloseToStop, t.PlanStop(new Vector2(-40, -5)).Error);
            Assert.True(t.PlanStop(new Vector2(-40, 5)).IsValid, "gegenüberliegende Seite ist frei");
        }

        [Fact]
        public void Line_RoutesOverRoads_Loop()
        {
            var w = Square(out var stops, out _);
            var line = w.Transit.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 2);
            Assert.NotNull(line);
            Assert.True(line.RouteValid);
            Assert.Equal(4, line.VisitStops.Count);
            var lanes = w.Traffic.Lanes;
            foreach (var leg in line.Legs)
            {
                Assert.NotNull(leg);
                for (int i = 0; i + 1 < leg.Count; i++)
                {
                    Assert.NotNull(w.Roads.GetSegment(leg[i].SegmentId));
                    Assert.Equal(lanes.EdgeTo(leg[i]), lanes.EdgeFrom(leg[i + 1])); // zusammenhängend
                }
            }
            output.WriteLine($"Länge {line.LengthMeters:0} m, Umlauf {line.CycleSeconds:0} s, Takt {line.CycleSeconds / 2:0} s");
            Assert.InRange(line.LengthMeters, 1200f, 1700f); // Umfang 1400 m
            Assert.True(line.RideSeconds(stops[0].Id, stops[2].Id) < line.CycleSeconds);

            // Pendel: hin und zurück, Haltestellen in Umlaufreihenfolge
            var pendel = w.Transit.PreviewLine(new[] { stops[0].Id, stops[1].Id, stops[2].Id }, loop: false);
            Assert.True(pendel.IsValid);
            Assert.Equal(new[] { stops[0].Id, stops[1].Id, stops[2].Id, stops[1].Id }, pendel.Visits);
        }

        [Fact]
        public void Bus_StopsAtStops_AndCompletesRound()
        {
            var w = Square(out var stops, out var depot);
            var line = w.Transit.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 2);
            bool sawAtStop = false;
            Bus first = null;
            for (int i = 0; i < 6000; i++)
            {
                w.Step();
                first ??= w.Transit.Buses.FirstOrDefault();
                if (first?.Vehicle != null && first.Vehicle.IsAtStop) sawAtStop = true;
                if (first != null && first.StopsServed >= 6) break;
            }
            Assert.NotNull(first);
            Assert.Equal(VehicleClass.Bus, first.Vehicle.Class);
            Assert.True(first.Vehicle.IsScheduled);
            Assert.True(sawAtStop, "Bus muss an Haltestellen stehen");
            Assert.True(first.StopsServed >= 6, $"Halte: {first.StopsServed}");
            Assert.Equal(2, w.Transit.Buses.Count);
            Assert.Equal(2, depot.VehiclesOut);
            Assert.True(line.ArrivalsToday + line.PassengersToday >= 0);
            output.WriteLine($"Halte {first.StopsServed}, Verkehrszeit {w.Traffic.Time:0} s, pünktlich zuletzt {first.LastLegOnTime} (+{first.LastDelaySeconds:0} s)");

            // Weniger Busse: einer fährt zurück ins Depot
            w.Transit.SetLineVehicles(line.Id, 1);
            for (int i = 0; i < 4000 && w.Transit.Buses.Count > 1; i++) w.Step();
            Assert.Single(w.Transit.Buses);
        }

        [Fact]
        public void Capacity_LimitsBoarding_FullBusLeavesPassengers()
        {
            var w = Square(out var stops, out _);
            w.Transit.BusCapacity = 3;
            var line = w.Transit.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 1);
            Run(w, 5); // Bus einsetzen
            var plan = new TransitOption { Valid = true, Line1 = line.Id, Board = stops[0].Id, Alight1 = stops[2].Id, WaitSeconds = 60, RideSeconds = 60 };
            var riders = Enumerable.Range(0, 10).Select(i => w.Transit.StartRide(900000 + i, plan, stops[2].Position)).ToList();
            Assert.Equal(10, stops[0].WaitingCount);
            int maxLoad = 0;
            for (int i = 0; i < 3000; i++)
            {
                w.Step();
                foreach (var b in w.Transit.Buses) maxLoad = System.Math.Max(maxLoad, b.Passengers);
                if (riders.Count(r => r.Outcome == RideOutcome.Arrived && !r.IsActive) >= 3) break;
            }
            Assert.True(maxLoad <= 3, $"höchstens 3 im Bus, war {maxLoad}");
            Assert.Equal(3, riders.Count(r => !r.IsActive && r.Outcome == RideOutcome.Arrived));
            Assert.True(w.Transit.TotalLeftBehind > 0, "volle Busse lassen Fahrgäste stehen");
            Assert.True(riders.Any(r => r.FullBusesSeen > 0));
        }

        [Fact]
        public void Choice_BusPreferred_WhenCheaper_AndJamMemoryRaisesBusUse()
        {
            var w = Square(out var stops, out _);
            var line = w.Transit.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 4);
            Run(w, 400); // Busse im Einsatz
            Assert.True(w.Transit.HasService);
            var from = stops[0].Position + new Vector2(0, -30);
            var to = stops[2].Position + new Vector2(0, 30);
            var o = w.Transit.PlanRide(from, to);
            Assert.True(o.Valid);
            Assert.Equal(line.Id, o.Line1);
            Assert.Equal(0, o.Transfers);
            float dist = Vector2.Distance(from, to);
            var c = TestCitizen(1);
            float pBus = w.Citizens.BusProbability(c, dist, o);
            output.WriteLine($"Weg {dist:0} m: Bus gefühlt {o.PerceivedSeconds:0} s gegen Fuß/Rad {CitizenSystem.OtherSeconds(dist):0} s → p(Bus) {pBus:0.00}");

            // Weiter Weg mit guter Verbindung → eher Bus; sehr kurzer Weg → eher zu Fuß; ohne Verbindung nie
            Assert.True(w.Citizens.BusProbability(c, 2500f, o) > 0.5f);
            var near = w.Transit.PlanRide(stops[0].Position, stops[0].Position + new Vector2(60, 0));
            float pShort = near.Valid ? w.Citizens.BusProbability(c, 60f, near) : 0f;
            Assert.True(pShort < 0.5f, $"kurz: {pShort}");
            Assert.Equal(0f, w.Citizens.BusProbability(c, dist, default));
            Assert.False(w.Transit.PlanRide(new Vector2(800, 800), to).Valid);

            // Stau-Erinnerung → mehr Bus, weniger Auto; volle Busse → mehr Auto
            var jam = TestCitizen(2);
            w.Citizens.Remember(jam, MemoryKind.TrafficJam, 0.8f);
            Assert.True(w.Citizens.BusProbability(jam, dist, o) > pBus);
            Assert.True(w.Citizens.CarProbability(jam, dist, o) < w.Citizens.CarProbability(c, dist, o));
            var full = TestCitizen(3);
            w.Citizens.Remember(full, MemoryKind.BusFull, 0.8f);
            Assert.True(w.Citizens.BusProbability(full, dist, o) < pBus);
            Assert.True(w.Citizens.CarProbability(full, dist, o) > w.Citizens.CarProbability(c, dist, o));
            // Ohne ÖPNV-Bezug unverändert gegenüber vorher
            Assert.Equal(w.Citizens.CarProbability(c, dist), w.Citizens.CarProbability(c, dist, default));
        }

        [Fact]
        public void Transfer_PlansAtMostOneChange()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-950, 0), new Vector2(950, 0));
            Road(w, new Vector2(0, -950), new Vector2(0, 950));
            var t = w.Transit;
            Assert.NotNull(t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(-300, -25)));
            var a = t.TryBuildStop(new Vector2(-900, 0), true);
            var x = t.TryBuildStop(new Vector2(-60, 0), true);
            var b = t.TryBuildStop(new Vector2(0, 900), true);
            var y = t.TryBuildStop(new Vector2(0, -60), true);
            var l1 = t.CreateLine(new[] { a.Id, x.Id }, loop: false, vehicles: 2);
            var l2 = t.CreateLine(new[] { y.Id, x.Id, b.Id }, loop: false, vehicles: 2);
            Assert.NotNull(l1);
            Assert.NotNull(l2);
            Run(w, 600);
            var o = t.PlanRide(a.Position, b.Position);
            Assert.True(o.Valid);
            Assert.Equal(1, o.Transfers);
            Assert.Equal(l1.Id, o.Line1);
            Assert.Equal(x.Id, o.Alight1);
            Assert.Equal(l2.Id, o.Line2);
            Assert.Equal(b.Id, o.Alight2);
        }

        [Fact]
        public void Depot_SnapsToRoad_AndBlocksRoadInsteadOfBeingDemolished()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var t = w.Transit;
            Assert.True(t.SnapDepot(TransitCatalog.BusDepot, new Vector2(10, 50), out var pos, out float rot));
            var d = t.TryBuildDepot(TransitCatalog.BusDepot, pos, rot);
            Assert.NotNull(d);
            Assert.True(d.Front.Y < -0.99f, "Vorderseite zeigt zur Straße");
            Assert.True(d.HasAccess);
            var plan = RoadBuilder.Plan(w.Roads, new Vector2(0, -100), 8f, null, new Vector2(0, 200), 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid);
            RoadBuilder.Build(w.Roads, plan);
            Assert.Contains(d, t.Depots); // Straßenbau reißt nicht ab – der Straßenblocker fragt BlocksRoad
            Assert.Contains(w.Roads.Segments, s => t.BlocksRoad(s) == d);
            Assert.True(t.BlocksPoint(d.Position));
        }

        // ------------------------------------------------------------ Mit Bürgern

        /// <summary>Lange Straße: Wohnen im Westen, Industrie/Handel im Osten, Pendellinie mit 3 Halten.</summary>
        static SimulationWorld Commuters(int days, out TransitLine line)
        {
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(-800, 0), new Vector2(800, 0));
            w.City.Paint(new Vector2(-600, 20), 160f, ZoneType.ResidentialMedium);
            w.City.Paint(new Vector2(600, -20), 160f, ZoneType.Industry);
            w.City.Paint(new Vector2(600, 20), 120f, ZoneType.Retail);
            var t = w.Transit;
            Assert.NotNull(t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(0, -28)));
            var s = new[]
            {
                t.TryBuildStop(new Vector2(-650, 0), true),
                t.TryBuildStop(new Vector2(-480, 0), true),
                t.TryBuildStop(new Vector2(0, 5), true),
                t.TryBuildStop(new Vector2(500, 0), true),
                t.TryBuildStop(new Vector2(680, 0), true),
            };
            Assert.All(s, x => Assert.NotNull(x));
            line = t.CreateLine(s.Select(x => x.Id).ToList(), loop: false, vehicles: 4);
            Assert.NotNull(line);
            Run(w, SimulationClock.TicksPerDay * days);
            return w;
        }

        [Fact]
        public void Citizens_RideRealBuses_FaresAndUpkeepInBudget()
        {
            var w = Commuters(10, out var line);
            var s = w.Citizens.Stats;
            var t = w.Transit;
            output.WriteLine($"Einwohner {s.Population}, Autofahrten {s.CarTrips}, Busfahrten {s.TransitTrips} (angekommen {s.TransitArrivals}, " +
                             $"aufgegeben {s.TransitGaveUp}), sonstige {s.OtherTrips}; Einstiege {t.TotalBoardings}, stehen gelassen {t.TotalLeftBehind}, " +
                             $"Fahrgeld {t.FaresTotal:0} €, Linie gestern {line.PassengersYesterday}, Auslastung {line.AverageLoad:P0}, pünktlich {line.Punctuality:P0}");
            Assert.True(s.TransitTrips > 0, "Bürger wählen den Bus");
            Assert.True(t.TotalBoardings > 0, "Bürger steigen ein");
            Assert.True(s.TransitArrivals > 0, "Bürger kommen mit dem Bus an");

            // Stadtkasse: Fahrgeld als Einnahme, Bau und Unterhalt als Ausgabe
            var e = w.Economy;
            double fares = e.History.Sum(h => h.Amount(BudgetCategory.TransitFares)) + e.CurrentWeek.Amount(BudgetCategory.TransitFares);
            Assert.Equal(t.FaresTotal, fares, 3);
            Assert.Equal((t.TotalBoardings + t.StatisticalBoardings) * TransitRules.Fare, t.FaresTotal, 3);
            Assert.True(e.LastDay.ServiceUpkeep.Any(kv => kv.Key == "ÖPNV" && kv.Value >= TransitCatalog.BusDepot.UpkeepPerWeek / 7.0));
            double build = e.History.Sum(h => h.Amount(BudgetCategory.TransitConstruction)) + e.CurrentWeek.Amount(BudgetCategory.TransitConstruction);
            Assert.Equal(TransitCatalog.BusDepot.Cost + 5 * TransitRules.StopCost, build, 2);
        }

        [Fact]
        public void Determinism_SameRunSameResult()
        {
            var a = Commuters(2, out var la);
            var b = Commuters(2, out var lb);
            Assert.Equal(a.Transit.TotalBoardings, b.Transit.TotalBoardings);
            Assert.Equal(a.Citizens.Stats.TransitTrips, b.Citizens.Stats.TransitTrips);
            Assert.Equal(a.Citizens.Stats.CarTrips, b.Citizens.Stats.CarTrips);
            Assert.Equal(la.PassengersTotal, lb.PassengersTotal);
            Assert.Equal(a.Economy.Balance, b.Economy.Balance);
            Assert.Equal(a.Traffic.Time, b.Traffic.Time);
            Assert.Equal(a.Transit.Buses.Select(x => x.Vehicle.Position).ToArray(), b.Transit.Buses.Select(x => x.Vehicle.Position).ToArray());
        }

        [Fact]
        public void Removing_StopOrLine_StrandsPassengersSafely()
        {
            var w = Square(out var stops, out _);
            var line = w.Transit.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 2);
            Run(w, 200);
            var plan = new TransitOption { Valid = true, Line1 = line.Id, Board = stops[1].Id, Alight1 = stops[3].Id };
            var p = w.Transit.StartRide(999, plan, stops[3].Position);
            Assert.True(w.Transit.DemolishStop(stops[3].Id));
            Assert.False(p.IsActive);
            Assert.Equal(RideOutcome.Stranded, p.Outcome);
            Assert.True(line.RouteValid);
            Assert.Equal(3, line.Stops.Count);
            Assert.True(w.Transit.RemoveLine(line.Id));
            for (int i = 0; i < 3000 && w.Transit.Buses.Count > 0; i++) w.Step();
            Assert.Empty(w.Transit.Buses);
            Assert.DoesNotContain(w.Traffic.Vehicles, v => v.IsScheduled);
        }

        // ------------------------------------------------------------ Leistung (nur Information)

        [Fact]
        public void Performance_10Lines_40Buses_10000Citizens()
        {
            var w = SimulationWorld.CreateDefault();
            for (int i = 0; i < 5; i++) Road(w, new Vector2(-600, -400 + i * 200), new Vector2(600, -400 + i * 200));
            Road(w, new Vector2(-600, -400), new Vector2(-600, 400));
            Road(w, new Vector2(600, -400), new Vector2(600, 400));
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-300, -380 + i * 200), 250f, ZoneType.ResidentialMedium);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(350, -380 + i * 200), 200f, ZoneType.Industry);
            w.City.Paint(new Vector2(350, -220), 80f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 2);
            var t = w.Transit;
            for (int i = 0; i < 3; i++) Assert.NotNull(t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(-625, -300 + i * 250), 90f) ??
                                                       t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(625, -300 + i * 250), 90f));
            int lines = 0;
            for (int r = 0; r < 5; r++)
                for (int half = 0; half < 2; half++)
                {
                    float y = -400 + r * 200;
                    var ids = Enumerable.Range(0, 5).Select(k => t.TryBuildStop(new Vector2((half == 0 ? -550 : 30) + k * 120, y - 4f), half == 1))
                        .Where(s => s != null).Select(s => s.Id).ToList();
                    if (t.CreateLine(ids, loop: false, vehicles: 4) != null) lines++;
                }
            var housing = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).ToList();
            Assert.NotEmpty(housing);
            int k2 = 0;
            while (w.Citizens.Citizens.Count < 10000) w.Citizens.CreateHousehold(housing[k2++ % housing.Count]);
            w.Citizens.ForceManage();
            Run(w, SimulationClock.TicksPerDay * 2); // Busse nach Takt einsetzen

            var sw = Stopwatch.StartNew();
            Run(w, SimulationClock.TicksPerDay);
            sw.Stop();
            var s = w.Citizens.Stats;
            output.WriteLine($"{lines} Linien, {t.Buses.Count} Busse, {t.Stops.Count} Haltestellen, {w.Citizens.Citizens.Count} Bürger, Fahrzeuge {w.Traffic.Vehicles.Count}");
            output.WriteLine($"Ein Spieltag (240 Ticks, alle Systeme): {sw.Elapsed.TotalMilliseconds:0} ms = {sw.Elapsed.TotalMilliseconds / 240:0.00} ms/Tick");
            output.WriteLine($"Busfahrten {s.TransitTrips} (an {s.TransitArrivals}, aufgegeben {s.TransitGaveUp}), Einstiege {t.TotalBoardings}, " +
                             $"unterwegs {t.ActivePassengers}, Autofahrten {s.CarTrips}");
            foreach (var d in t.Depots) output.WriteLine($"{d}: Zufahrt {d.HasAccess}, im Einsatz {d.VehiclesOut}/{d.Type.VehicleCapacity}");
            foreach (var l in t.Lines) output.WriteLine($"{l.Name}: Umlauf {l.CycleSeconds:0} s, Busse {l.ActiveVehicles}/{l.TargetVehicles}, gültig {l.RouteValid}");
            Assert.True(lines >= 8, $"Linien: {lines}");
        }
    }
}
