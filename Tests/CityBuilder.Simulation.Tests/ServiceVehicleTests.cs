using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Dienstfahrzeuge (Grundstufe): Müllwagen-Touren mit Abholung bei Ankunft, Streife im Einzugsgebiet, Feuerwehr-/Rettungs-
    /// Einsätze, eigene Fahrt-Id-Spanne, Anteil begrenzt, Standard aus, Spielstand deterministisch.
    /// </summary>
    public class ServiceVehicleTests
    {
        readonly ITestOutputHelper output;
        public ServiceVehicleTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>
        /// Wohnstraße (0..400) mit Querstraße nach Süden (Stichstraße 200,0 → 200,−160) für die Dienste; bewohnt, Müll gestaut.
        /// <paramref name="enabled"/> schaltet die Dienstfahrzeuge vor dem Bau der Dienste ein.
        /// </summary>
        static SimulationWorld Town(bool enabled, bool garbage = true, int seed = 4711)
        {
            var w = SimulationWorld.CreateDefault(null, seed);
            w.Utilities.AbandonmentEnabled = false;
            w.ServiceVehicles.Enabled = enabled;
            Road(w, new Vector2(0, 0), new Vector2(400, 0));
            Road(w, new Vector2(200, 0), new Vector2(200, -160));
            w.City.Paint(new Vector2(100, 30), 60f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(300, 30), 60f, ZoneType.ResidentialLow);
            for (int i = 0; i < 2500; i++)
            {
                w.Step();
                if (i % 50 == 0 && w.City.Buildings.Count(b => b.IsConstructed && b.Residents > 0) >= 10) break;
            }
            Run(w, 2 * SimulationClock.TicksPerDay); // Müll stapelt sich ohne Abfuhr
            if (garbage) Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Landfill, new Vector2(240, -100))));
            return w;
        }

        [Fact]
        public void Default_Off_NoVehicles_GarbageAsBefore()
        {
            var w = Town(enabled: false);
            Run(w, SimulationClock.TicksPerDay);
            Assert.False(w.ServiceVehicles.Enabled);
            Assert.Empty(w.ServiceVehicles.Units);
            Assert.Equal(1f, w.Services.GarbageBaseShare);
            Assert.Equal(0, w.Traffic.Stats.ServiceVehicles);
            Assert.Equal(0.0, w.ServiceVehicles.WeeklyUpkeep());
        }

        [Fact]
        public void GarbageTrucks_TourBuildingsWithBacklog_CollectOnArrival()
        {
            var w = Town(enabled: true);
            var fleet = w.ServiceVehicles;
            var dump = w.Services.Facilities.Single(f => f.Kind == ServiceKinds.Garbage);
            float storedBefore = dump.Stored;
            int seen = 0, maxActive = 0;
            bool idsOk = true;
            for (int tick = 0; tick < SimulationClock.TicksPerDay; tick++)
            {
                for (int s = 0; s < TimeModel.TrafficStepsPerTick; s += 20)
                {
                    for (int k = 0; k < 20; k++) w.Substep();
                    foreach (var v in w.Traffic.Vehicles)
                    {
                        if (!TripIdRanges.IsService(v.TripId)) continue;
                        seen++;
                        var u = fleet.UnitForTrip(v.TripId);
                        idsOk &= u != null && w.Citizens.CitizenForTrip(v.TripId) == null && w.Goods.ShipmentForTrip(v.TripId) == null;
                        idsOk &= v.Class == VehicleClass.Truck && u.Kind == ServiceVehicleKind.GarbageTruck;
                    }
                    maxActive = System.Math.Max(maxActive, fleet.ActiveTrips);
                }
            }
            var st = fleet.Today.Trips > 0 ? fleet.Today : fleet.Yesterday;
            output.WriteLine($"Müllwagen: {fleet.Units.Count} Wagen, Fahrten heute {fleet.Today.Trips}/gestern {fleet.Yesterday.Trips}, Halte {fleet.Today.Stops + fleet.Yesterday.Stops}, " +
                             $"geladen {fleet.Today.GarbageCollected + fleet.Yesterday.GarbageCollected:0.00} t, Halde {storedBefore:0.00} → {dump.Stored:0.00} t, " +
                             $"gleichzeitig ≤ {maxActive}, beobachtet {seen}, Fehlstarts {fleet.Today.FailedStarts + fleet.Yesterday.FailedStarts}");
            Assert.Equal(ServiceVehicleRules.FleetSize("landfill", out _), fleet.Units.Count);
            Assert.Equal(ServiceVehicleRules.GarbageBaseShareWithTrucks, w.Services.GarbageBaseShare);
            Assert.True(seen > 0, "Müllwagen fahren im Verkehr");
            Assert.True(idsOk, "Fahrt-Ids der Dienstfahrzeuge kollidieren nicht mit Bürgern/Lkw");
            Assert.True(fleet.Today.Stops + fleet.Yesterday.Stops > 0);
            Assert.True(fleet.Today.GarbageCollected + fleet.Yesterday.GarbageCollected > 0f);
            Assert.True(dump.Stored > storedBefore, "Halde füllt sich (Entladen bei Rückkehr + Pauschale)");
            Assert.True(maxActive <= ServiceVehicleRules.MaxTrafficShare * w.Traffic.MaxVehicles);
            Assert.True(w.ServiceVehicles.WeeklyUpkeep() > 0.0);
        }

        [Fact]
        public void GarbageBacklog_DependsOnTruckArrival()
        {
            // gleiche Stadt: einmal fahren die Müllwagen, einmal ist die Stichstraße zur Halde für Lkw gesperrt (kein Start möglich)
            var a = Town(enabled: true);
            var b = Town(enabled: true);
            var stub = b.Roads.Segments.Single(s => System.Math.Abs(s.Evaluate(0.5f).X - 200f) < 1f && s.Evaluate(0.5f).Y < -40f);

            b.Traffic.Rules.SetBanned(stub.Id, VehicleClass.Truck);
            Run(a, 2 * SimulationClock.TicksPerDay);
            Run(b, 2 * SimulationClock.TicksPerDay);
            float ga = a.Services.TotalGarbage, gb = b.Services.TotalGarbage;
            output.WriteLine($"Müll nach 2 Tagen: Wagen fahren {ga:0.00} t, Wagen gesperrt {gb:0.00} t (Fahrten {a.ServiceVehicles.Yesterday.Trips} / {b.ServiceVehicles.Yesterday.Trips})");
            Assert.True(gb > ga * 1.2f, "ohne Müllwagen-Ankunft bleibt mehr Müll liegen");
            Assert.True(b.ServiceVehicles.Yesterday.GarbageCollected == 0f);
        }

        [Fact]
        public void Police_Patrols_Fire_And_Rescue_Calls()
        {
            var w = Town(enabled: true, garbage: false);
            var s = w.Services;
            Assert.NotNull(s.Build(s.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(240, -60))));
            Assert.NotNull(s.Build(s.PlanAligned(ServiceCatalog.FireStation, new Vector2(160, -60))));
            Assert.NotNull(s.Build(s.PlanAligned(ServiceCatalog.Clinic, new Vector2(185, -125))));
            Run(w, 12); // Zuordnung + erste volle Stunde
            var fleet = w.ServiceVehicles;
            Assert.Equal(3 + 2 + 1, fleet.Units.Count);

            var home = w.City.Buildings.Where(b => b.IsConstructed && b.Residents > 0).OrderBy(b => b.Id).First();
            var fire = fleet.Call(ServiceMission.FireCall, home, w.Clock.Tick);
            Assert.NotNull(fire);
            Assert.Equal(ServiceVehicleKind.FireTruck, fire.Kind);
            // Rettung: schwer erkrankter Senior → Rettungswagen der Klinik
            var patient = w.Citizens.Citizens.First(c => c.Household != null && c.Household.HomeId == home.Id);
            patient.Age = 70;
            fleet.OnBecameIll(patient);
            Run(w, 1);
            var ambulance = fleet.Units.Single(u => u.Kind == ServiceVehicleKind.Ambulance);
            Assert.Equal(ServiceMission.Rescue, ambulance.Mission);

            Run(w, SimulationClock.TicksPerDay / 2);
            var t = fleet.Today;
            output.WriteLine($"Streife {t.PatrolStops} Halte, Brand {t.FireCalls}, Rettung {t.Rescues}, verpasst {t.MissedCalls}, Fahrten {t.Trips}");
            Assert.True(t.PatrolStops > 0, "Streifenwagen fahren Runden");
            Assert.True(t.FireCalls >= 1);
            Assert.True(t.Rescues >= 1);
            Assert.All(fleet.Units.Where(u => u.Kind == ServiceVehicleKind.PoliceCar),
                u => Assert.Equal(VehicleClass.Car, ServiceVehicleRules.ClassOf(u.Kind)));
        }

        [Fact]
        public void SaveLoad_MidTour_Deterministic()
        {
            var a = Town(enabled: true);
            Assert.NotNull(a.Services.Build(a.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(160, -60))));
            Run(a, 30);
            for (int i = 0; i < 77; i++) a.Substep(); // mitten im Tick, Fahrzeuge unterwegs
            Assert.True(a.ServiceVehicles.Units.Any(u => u.IsBusy));
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.True(b.ServiceVehicles.Enabled);
            Assert.Equal(a.ServiceVehicles.Units.Count, b.ServiceVehicles.Units.Count);
            Assert.Equal(a.ServiceVehicles.ActiveTrips, b.ServiceVehicles.ActiveTrips);
            Assert.Equal(a.Services.GarbageBaseShare, b.Services.GarbageBaseShare);
            Same(a, b, "direkt nach dem Laden");
            for (int i = 0; i < SimulationClock.TicksPerDay / 2; i++) { a.Step(); b.Step(); }
            Same(a, b, "nach einem halben Tag");
            Assert.Equal(a.ServiceVehicles.Today.Trips, b.ServiceVehicles.Today.Trips);
        }

        static void Same(SimulationWorld a, SimulationWorld b, string when)
        {
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            Assert.Contains("servicevehicles", ha.Select(x => x.Key));
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, $"{when}: abweichende Abschnitte: {string.Join(", ", diff)}");
        }
    }
}
