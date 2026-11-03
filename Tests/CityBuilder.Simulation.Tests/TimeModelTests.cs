using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.People;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Transit;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Zeitmodell (<see cref="TimeModel"/>): Konstanten, Verkehrsschritte je Tick, Fußwege in Verkehrszeit und der
    /// Balancing-Nachweis mit Zahlen (große Stadt: Autofahrt in Spielminuten, Lkw/Bus regulär, Gehtempo, ms je Tick).
    /// </summary>
    public class TimeModelTests
    {
        readonly ITestOutputHelper output;
        public TimeModelTests(ITestOutputHelper output) => this.output = output;

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

        // ------------------------------------------------------------ Konstanten und Takt

        [Fact]
        public void Constants_AreConsistent()
        {
            Assert.Equal(240, SimulationClock.TicksPerDay);
            Assert.Equal(10, SimulationClock.TicksPerHour);
            Assert.Equal(45f, TimeModel.TrafficSecondsPerTick);
            Assert.Equal(8f, TimeModel.Dilation);
            Assert.Equal(TimeModel.TrafficStepSeconds, TrafficSystem.Dt);
            Assert.Equal(10800f, TimeModel.TrafficSecondsPerDay);
            Assert.Equal(1, TimeModel.TicksFor(0));
            Assert.Equal(1, TimeModel.TicksFor(45));
            Assert.Equal(2, TimeModel.TicksFor(45.5));
            Assert.Equal(24.0, TimeModel.GameMinutes(180), 6); // 3 min Verkehrszeit = 24 Spielminuten
            // Tempo 1/2/3: 6 / 3 / 1,5 Minuten je Spieltag
            Assert.Equal(360f, TimeModel.RealSecondsPerDay(1), 3);
            Assert.Equal(180f, TimeModel.RealSecondsPerDay(2), 3);
            Assert.Equal(90f, TimeModel.RealSecondsPerDay(3), 3);
        }

        [Fact]
        public void Step_RunsAllTrafficSubsteps_ThenTheCalendarTick()
        {
            var w = SimulationWorld.CreateDefault();
            Assert.True(w.AtTickBoundary);
            for (int i = 0; i < TimeModel.TrafficStepsPerTick - 1; i++) w.Substep();
            Assert.Equal(0, w.Clock.Tick);
            Assert.Equal((TimeModel.TrafficStepsPerTick - 1) * (double)TimeModel.TrafficStepSeconds, w.Traffic.Time, 9);
            Assert.False(w.AtTickBoundary);
            Assert.Equal((TimeModel.TrafficStepsPerTick - 1) / (double)TimeModel.TrafficStepsPerTick, w.TickTime, 9);
            w.Substep();
            Assert.Equal(1, w.Clock.Tick);
            Assert.True(w.AtTickBoundary);
            w.Step();
            Assert.Equal(2, w.Clock.Tick);
            Assert.Equal(2 * (double)TimeModel.TrafficSecondsPerTick, w.Traffic.Time, 9);
            // Mitten im Tick: Step beendet nur den laufenden Tick
            for (int i = 0; i < 7; i++) w.Substep();
            w.Step();
            Assert.Equal(3, w.Clock.Tick);
            Assert.True(w.AtTickBoundary);
            // Verkehrszeit bleibt über Jahre exakt (double aus der Schrittzahl)
            Assert.Equal(w.Clock.Tick * (double)TimeModel.TrafficSecondsPerTick, w.Traffic.Time, 9);
        }

        [Fact]
        public void Walks_UseRealWalkingPace_InTrafficTime()
        {
            // 600 m Luftlinie zu Fuß: 600 × 1,25 / 1,3 = 577 s Verkehrszeit = 13 Ticks (78 Spielminuten), früher 1 Tick
            Assert.Equal(577f, TimeModel.WalkSeconds(600f), 0);
            Assert.Equal(13, TimeModel.WalkTicks(600f));
            Assert.Equal(TimeModel.WalkSeconds(500f), TimeModel.OtherTripSeconds(500f));
            Assert.True(TimeModel.OtherTripSeconds(3000f) < TimeModel.WalkSeconds(3000f) / 3f, "lange Wege: Rad/Sonstiges");

            // Pose: echtes Gehtempo, auch wenn die geplante Zeit (aufgerundete Ticks) länger ist
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            var planner = new PedestrianPlanner(w);
            planner.RouteBudget = 10;
            Assert.True(planner.TryPointEndpoint(new Vector2(-200, 8), 0f, out var a));
            Assert.True(planner.TryPointEndpoint(new Vector2(200, 8), 0f, out var b));
            var route = planner.Route(a, b);
            Assert.NotNull(route);
            double start = 100, end = start + TimeModel.TicksFor(route.Length / TimeModel.WalkSpeed) + 3;
            var mid = PedestrianPlanner.WalkAtPace(route, start, end, start + 2, PedestrianSpot.Trip);
            Assert.Equal(PedestrianActivity.Walking, mid.Kind);
            Assert.Equal(TimeModel.WalkSpeed, mid.MetersPerSecond, 2);
            var after = PedestrianPlanner.WalkAtPace(route, start, end, end - 0.5, PedestrianSpot.Trip);
            Assert.Equal(1f, after.Progress, 3); // vor der geplanten Ankunft schon da, wartet an der Tür
            // Zu wenig Zeit: schneller, Ankunft genau am Ende
            var hurry = PedestrianPlanner.WalkAtPace(route, start, start + 1, start + 0.5, PedestrianSpot.Trip);
            Assert.True(hurry.MetersPerSecond > TimeModel.WalkSpeed);
            output.WriteLine($"Route {route.Length:0} m: {route.Length / TimeModel.WalkSpeed:0} s Verkehrszeit = " +
                             $"{TimeModel.GameMinutes(route.Length / TimeModel.WalkSpeed):0} Spielminuten, sichtbar {mid.MetersPerSecond:0.00} m/s Verkehrszeit");
        }

        [Fact]
        public void TransitPassenger_WalksToTheStopFirst_ThenBoards()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, -150), new Vector2(200, -150));
            Road(w, new Vector2(200, -150), new Vector2(200, 150));
            Road(w, new Vector2(200, 150), new Vector2(-200, 150));
            Road(w, new Vector2(-200, 150), new Vector2(-200, -150));
            var t = w.Transit;
            Assert.NotNull(t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(0, -175)));
            var stops = new[]
            {
                t.TryBuildStop(new Vector2(-50, -150), bothSides: true), t.TryBuildStop(new Vector2(200, 0), bothSides: true),
                t.TryBuildStop(new Vector2(50, 150), bothSides: true), t.TryBuildStop(new Vector2(-200, 0), bothSides: true),
            };
            var line = t.CreateLine(stops.Select(s => s.Id).ToList(), loop: true, vehicles: 2);
            Run(w, 20);
            var plan = new TransitOption { Valid = true, Line1 = line.Id, Board = stops[0].Id, Alight1 = stops[2].Id, AccessMeters = 260f };
            var p = t.StartRide(4711, plan, stops[2].Position);
            Assert.NotNull(p);
            double ready = w.Traffic.Time + TimeModel.WalkSeconds(260f);
            Assert.False(p.IsAtStop(w.Traffic.Time));
            // Solange der Fußweg läuft, steigt der Fahrgast in keinen Bus
            while (w.Traffic.Time < ready - 1.0) { w.Substep(); Assert.Null(p.Bus); }
            for (int i = 0; i < 40 * TimeModel.TrafficStepsPerTick && p.Bus == null && p.IsActive; i++) w.Substep();
            Assert.True(p.Bus != null, $"kein Einstieg: aktiv {p.IsActive}, Ergebnis {p.Outcome}, Busse {t.Buses.Count}, Zeit {w.Traffic.Time - ready:0} s nach Ankunft");
            Assert.True(w.Traffic.Time >= ready, "eingestiegen erst nach dem Fußweg");
        }

        [Fact]
        public void SaveLoad_MidTick_AndOldWorldSectionWithoutSubstep()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(0, 20), 60f, ZoneType.ResidentialLow);
            Run(w, 30);
            for (int i = 0; i < 55; i++) w.Substep();
            var loaded = Persistence.SaveGame.Load(Persistence.SaveGame.SaveToBytes(w));
            Assert.Equal(55, loaded.SubstepInTick);
            Assert.Equal(w.Traffic.Time, loaded.Traffic.Time);
            Assert.Equal(Persistence.SaveGame.StateHash(w), Persistence.SaveGame.StateHash(loaded));
            w.Step(); loaded.Step();
            Assert.Equal(Persistence.SaveGame.StateHash(w), Persistence.SaveGame.StateHash(loaded));

            // Migration: Weltabschnitt v1 (vor dem Zeitmodell) hat keinen Verkehrsschritt → Tick-Grenze
            var c = Persistence.SaveGame.Capture(w);
            var ws = c.Find(Persistence.SaveGame.WorldSectionId);
            Assert.Equal(3, ws.Version); // v3 (Spielstart): + freies Land (Float) + Stadtname (String, null = 1 Byte)
            c.Set(ws.Id, 1, ws.Data.Take(ws.Data.Length - 4 - 5).ToArray());
            var old = Persistence.SaveGame.Restore(Persistence.SaveContainer.FromBytes(c.ToBytes()));
            Assert.True(old.AtTickBoundary);
            Assert.Equal(w.Clock.Tick, old.Clock.Tick);
        }

        // ------------------------------------------------------------ Balancing-Nachweis (große Stadt, Zahlen)

        /// <summary>
        /// Große Stadt (~15 000 Bürger, ~190 Firmen, 10 Buslinien): misst ms je Verkehrsschritt und je Kalender-Tick,
        /// rechnet auf Tempo 1/2/3 hoch und wertet einen Spieltag aus (Autofahrt in Spielminuten, Lkw/Busse regulär,
        /// Gehtempo der Fußgänger).
        /// </summary>
        [Fact]
        public void Balancing_LargeCity_TripTimes_Deliveries_Buses_Performance()
        {
            var w = SimulationWorld.CreateDefault();
            for (int i = 0; i < 5; i++) Road(w, new Vector2(-900, -400 + i * 200), new Vector2(1020, -400 + i * 200));
            Road(w, new Vector2(-900, -400), new Vector2(-900, 400));
            Road(w, new Vector2(-150, -400), new Vector2(-150, 400));
            Road(w, new Vector2(500, -400), new Vector2(500, 400));
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-600, -400 + i * 200), 290f, ZoneType.ResidentialMedium);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-200, -400 + i * 200), 75f, ZoneType.Office);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(250, -400 + i * 200), 320f, ZoneType.Industry);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(700, -400 + i * 200), 110f, ZoneType.Retail);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(920, -400 + i * 200), 85f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 2);
            var t = w.Transit;
            for (int i = 0; i < 3; i++)
                Assert.NotNull(t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(-925, -300 + i * 250), 90f) ??
                               t.TryBuildDepot(TransitCatalog.BusDepot, new Vector2(-875, -300 + i * 250), 270f));
            int lines = 0;
            for (int r = 0; r < 5; r++)
                for (int half = 0; half < 2; half++)
                {
                    float y = -400 + r * 200;
                    var ids = Enumerable.Range(0, 5).Select(k => t.TryBuildStop(new Vector2((half == 0 ? -850 : 50) + k * 170, y - 4f), half == 1))
                        .Where(s => s != null).Select(s => s.Id).ToList();
                    if (t.CreateLine(ids, loop: false, vehicles: 4) != null) lines++;
                }
            var housing = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).ToList();
            Assert.NotEmpty(housing);
            int k2 = 0;
            while (w.Citizens.Citizens.Count < 10000) w.Citizens.CreateHousehold(housing[k2++ % housing.Count]);
            w.Citizens.ForceManage();
            Run(w, SimulationClock.TicksPerDay * 3); // Firmen wachsen, Busse nach Takt

            // Beobachtung über einen Spieltag
            var carMinutes = new List<double>();
            int carEnded = 0, carArrived = 0;
            w.Traffic.TripEnded += (v, arrived) =>
            {
                if (v.TripId <= 0) return;
                carEnded++;
                if (!arrived) return;
                carArrived++;
                carMinutes.Add(TimeModel.GameMinutes(w.Traffic.Time - v.StartTime));
            };
            var s0 = Snapshot(w);
            int shipments = 0, trucks = 0, truckFailed = 0, placeholders = 0;
            double substepMs = 0, tickMs = 0, worstSubstep = 0, worstTick = 0;
            int substeps = 0;
            var walkSpeeds = new List<float>();
            var planner = new PedestrianPlanner(w);
            var sw = new Stopwatch();
            for (int tick = 0; tick < SimulationClock.TicksPerDay; tick++)
            {
                for (int s = 0; s < TimeModel.TrafficStepsPerTick; s++)
                {
                    bool last = s == TimeModel.TrafficStepsPerTick - 1;
                    sw.Restart();
                    w.Substep();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (last) { tickMs += ms; worstTick = Math.Max(worstTick, ms); }
                    else { substepMs += ms; substeps++; worstSubstep = Math.Max(worstSubstep, ms); }
                }
                if (w.Clock.Tick % SimulationClock.TicksPerDay == 1)
                {
                    var g = w.Goods.Stats;
                    shipments += g.ShipmentsYesterday; trucks += g.TrucksYesterday; truckFailed += g.TruckFailedYesterday; placeholders += g.PlaceholderYesterday;
                }
                if (tick % 20 == 10)
                {
                    planner.RouteBudget = 200;
                    foreach (var c in w.Citizens.Citizens)
                    {
                        if (c.Activity != CityBuilder.Simulation.Citizens.Activity.Travelling || c.HasCarTrip || c.Mode == TravelMode.Car) continue;
                        var pose = planner.Pose(c, w.TickTime);
                        if (pose.Kind == PedestrianActivity.Walking && pose.Spot == PedestrianSpot.Trip && pose.Progress < 1f) walkSpeeds.Add(pose.MetersPerSecond);
                        if (walkSpeeds.Count > 400) break;
                    }
                }
            }
            var s1 = Snapshot(w);
            var cs = w.Citizens.Stats;
            int realBus = (s1.TransitTrips - s1.Statistical) - (s0.TransitTrips - s0.Statistical);
            int busArrived = s1.TransitArrivals - s0.TransitArrivals, busGaveUp = s1.TransitGaveUp - s0.TransitGaveUp;
            carMinutes.Sort();
            double avgCar = carMinutes.Count > 0 ? carMinutes.Average() : 0;
            double medCar = carMinutes.Count > 0 ? carMinutes[carMinutes.Count / 2] : 0;
            double p90Car = carMinutes.Count > 0 ? carMinutes[(int)(carMinutes.Count * 0.9)] : 0;
            walkSpeeds.Sort();
            double medWalk = walkSpeeds.Count > 0 ? walkSpeeds[walkSpeeds.Count / 2] : 0;

            double perSub = substepMs / Math.Max(1, substeps);
            double perTickAll = (substepMs + tickMs) / SimulationClock.TicksPerDay;
            output.WriteLine($"Stadt: {cs.Population} Einwohner, {w.Citizens.Citizens.Count} Bürger, {w.Goods.Firms.Count} Firmen, {lines} Linien, " +
                             $"{t.Buses.Count} Busse, Fahrzeuge jetzt {w.Traffic.Vehicles.Count}");
            output.WriteLine($"Leistung: Verkehrsschritt Ø {perSub:0.000} ms (max {worstSubstep:0.00}), Schritt mit Kalender-Tick Ø {tickMs / SimulationClock.TicksPerDay:0.00} ms " +
                             $"(max {worstTick:0.0}); je Kalender-Tick gesamt {perTickAll:0.00} ms; Spieltag {(substepMs + tickMs) / 1000.0:0.00} s Rechenzeit");
            for (int speed = 1; speed <= 3; speed++)
            {
                double ticksPerSecond = TimeModel.TicksPerRealSecond * TimeModel.SpeedMultipliers[speed];
                double msPerSecond = ticksPerSecond * perTickAll;
                output.WriteLine($"  Tempo {speed}: 1 Spieltag = {TimeModel.RealSecondsPerDay(speed) / 60f:0.00} min Echtzeit, {ticksPerSecond * TimeModel.TrafficStepsPerTick:0} Verkehrsschritte/s, " +
                                 $"≈ {msPerSecond:0} ms Rechenzeit je Echtzeitsekunde = {msPerSecond / 60.0:0.00} ms je Frame bei 60 fps");
            }
            output.WriteLine($"Autofahrten: {carEnded} beendet, {carArrived} regulär; Spielminuten Ø {avgCar:0.0}, Median {medCar:0.0}, 90 % {p90Car:0.0}");
            output.WriteLine($"Lieferungen (Vortag): {shipments}, davon echte Lkw {trucks} ({(shipments > 0 ? 100.0 * trucks / shipments : 0):0} %), " +
                             $"ohne Weg/Platz {truckFailed}, Platzhalter (Lkw-Limit/Anteil) {placeholders}");
            output.WriteLine($"Busfahrten (echte Fahrgäste, 1 Tag): {realBus} gestartet, {busArrived} regulär angekommen ({(realBus > 0 ? 100.0 * busArrived / realBus : 0):0} %), " +
                             $"{busGaveUp} aufgegeben; Einstiege gesamt {t.TotalBoardings}");
            output.WriteLine($"Fußgänger: {walkSpeeds.Count} Stichproben, Gehtempo Median {medWalk:0.00} m/s Verkehrszeit");

            Assert.True(carArrived > 0, "keine Autofahrt angekommen");
            Assert.InRange(medCar, 5.0, 60.0); // Ziel ≈ 10–40 Spielminuten
            Assert.InRange(medWalk, 1.2, 1.6);
            Assert.True(trucks > 0, "keine echten Lkw");
        }

        struct Snap { public int TransitTrips, Statistical, TransitArrivals, TransitGaveUp; }

        static Snap Snapshot(SimulationWorld w)
        {
            var s = w.Citizens.Stats;
            return new Snap { TransitTrips = s.TransitTrips, Statistical = s.TransitStatistical, TransitArrivals = s.TransitArrivals, TransitGaveUp = s.TransitGaveUp };
        }
    }
}
