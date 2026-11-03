using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class CitizenTests
    {
        readonly ITestOutputHelper output;
        public CitizenTests(ITestOutputHelper output) => this.output = output;

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

        void Print(SimulationWorld w)
        {
            var s = w.Citizens.Stats;
            var t = w.Traffic.Stats;
            output.WriteLine($"Einwohner {s.Population} in {s.Households} Haushalten · erwerbsfähig {s.WorkingAge} · beschäftigt {s.Employed} " +
                             $"(hier {s.WorkingLocal}, Auspendler {s.CommutersOut}) · arbeitslos {s.Unemployed} · Einpendler {s.CommutersIn}");
            output.WriteLine($"Fahrten: Auto {s.CarTrips} (angekommen {s.CarArrivals}, abgebrochen {s.CarTripsFailed}), sonstige {s.OtherTrips} · " +
                             $"Verkehr: {t.Active} aktiv, davon Bürger {t.CitizenVehicles}, Bürgerfahrten {t.CitizenTripsStarted}/{t.CitizenTripsArrived}/{t.CitizenTripsFailed}");
            output.WriteLine($"Alter: {string.Join(", ", CitizenStats.AgeGroupNames.Select((n, i) => $"{n}: {s.AgeGroups[i]}"))}");
        }

        [Fact]
        public void MoveIn_FillsFreeHomes_WithHouseholdsOfCitizens()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(0, 20), 150f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 2);
            Print(w);

            var s = w.Citizens.Stats;
            Assert.True(s.Population > 20, $"Einwohner {s.Population}");
            Assert.True(s.Households > 5);
            Assert.Equal(s.Population, w.City.Stats.Population);
            Assert.Equal(s.Population, w.Citizens.Citizens.Count(c => !c.IsCommuterIn));
            Assert.True(s.Children + s.Students > 0, "Familien mit Kindern erwartet");

            foreach (var b in w.City.Buildings.Where(b => b.Households > 0))
            {
                var hs = w.Citizens.HouseholdsIn(b.Id);
                Assert.True(hs.Count <= b.Households, $"{b}: {hs.Count}/{b.Households}");
                Assert.Equal(hs.Count, b.OccupiedHouseholds);
                Assert.Equal(hs.Sum(h => h.Members.Count), b.Residents);
                Assert.All(hs, h => Assert.All(h.Members, m => Assert.Equal(b.Id, m.Home.BuildingId)));
            }
        }

        [Fact]
        public void Jobs_AreAssigned_WithinCapacity_AndCommuteHappens()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 4);
            Print(w);

            var s = w.Citizens.Stats;
            Assert.True(s.WorkingLocal > 10, $"beschäftigt hier: {s.WorkingLocal}");
            foreach (var b in w.City.Buildings)
            {
                var workers = w.Citizens.WorkersAt(b.Id);
                Assert.True(workers.Count <= b.Jobs, $"{b}: {workers.Count}/{b.Jobs}");
                Assert.Equal(workers.Count, b.Workers);
                Assert.All(workers, c => Assert.Equal(b.Id, c.WorkplaceId));
            }
            // Jeder Beschäftigte hat einen existierenden, fertigen Arbeitsplatz im Erwerbsalter
            foreach (var c in w.Citizens.Citizens.Where(c => c.WorkplaceId != 0))
            {
                var job = w.City.GetBuilding(c.WorkplaceId);
                Assert.NotNull(job);
                Assert.True(job.IsConstructed);
                Assert.True(c.IsWorkingAge);
            }
            // Tagesablauf: es wurde gependelt und eingekauft
            Assert.True(s.CarTrips + s.OtherTrips > 20);
            Assert.True(s.CarArrivals > 0, "mindestens eine Autofahrt muss ankommen");
        }

        [Fact]
        public void Trip_FromBuilding_ArrivesAtOtherBuilding_AndReportsBack()
        {
            var w = SimulationWorld.CreateDefault();
            w.Citizens.CarShare = 0f; // nur die Fahrt dieses Tests
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            Road(w, new Vector2(0, 0), new Vector2(0, 300));
            w.City.Paint(new Vector2(-200, 20), 40f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(20, 250), 40f, ZoneType.Industry);
            Run(w, 200);
            var home = w.City.Buildings.First(b => b.Zone == ZoneType.ResidentialLow);
            var shop = w.City.Buildings.First(b => b.Zone == ZoneType.Industry);

            Assert.True(w.Citizens.TryTripEnd(Place.Building(home.Id), out var from));
            Assert.True(w.Citizens.TryTripEnd(Place.Building(shop.Id), out var to));
            Assert.False(from.IsNode);

            int arrivedTrip = 0;
            bool arrivedFlag = false;
            Vector2 lastPosition = default;
            w.Traffic.TripEnded += (v, ok) => { if (v.TripId == 4242) { arrivedTrip = v.TripId; arrivedFlag = ok; lastPosition = v.Position; } };

            var vehicle = w.Traffic.RequestTrip(from, to, VehicleClass.Car, 4242);
            Assert.NotNull(vehicle);
            // Startet an der Zufahrt, nicht an einem Knoten
            Assert.True(Vector2.Distance(vehicle.Position, home.Entrance) < 12f, $"Start {vehicle.Position} vs. Zufahrt {home.Entrance}");

            for (int i = 0; i < 2000 && arrivedTrip == 0; i++) w.Step();
            Assert.Equal(4242, arrivedTrip);
            Assert.True(arrivedFlag);
            Assert.True(Vector2.Distance(lastPosition, shop.Entrance) < 15f, $"Ankunft {lastPosition} vs. Zufahrt {shop.Entrance}");
        }

        [Fact]
        public void Trip_OnSameSegment_BehindStart_TurnsAndArrives()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var seg = w.Roads.Segments.Single();
            int done = 0;
            w.Traffic.TripEnded += (v, ok) => { if (ok) done = v.TripId; };
            // Ziel liegt „hinter“ der Startrichtung – beide Richtungen sind möglich, das Auto muss den kürzeren Weg nehmen
            var v1 = w.Traffic.RequestTrip(TripEnd.AtSegment(seg.Id, 0.7f), TripEnd.AtSegment(seg.Id, 0.3f), VehicleClass.Car, 7);
            Assert.NotNull(v1);
            for (int i = 0; i < 2000 && done == 0; i++) w.Step();
            Assert.Equal(7, done);
        }

        [Fact]
        public void ExternalConnection_BringsCommutersIn_WhenNoLocalWorkers()
        {
            var w = SimulationWorld.CreateDefault();
            // Straße bis an den Kartenrand (Welt ±1024 m) = Außenanschluss
            Road(w, new Vector2(-200, 0), new Vector2(1020, 0));
            Assert.Equal(1, w.Traffic.ExternalConnections.Count);
            w.City.Paint(new Vector2(0, 20), 150f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 3);
            Print(w);

            var s = w.Citizens.Stats;
            Assert.Equal(0, s.Population);
            Assert.True(s.CommutersIn > 5, $"Einpendler {s.CommutersIn}");
            Assert.All(w.Citizens.Citizens, c => Assert.True(c.IsCommuterIn && c.WorkplaceId != 0));
            Assert.True(w.City.Stats.Workers > 5);
            // Einpendler kommen über den Außenanschluss: Fahrten müssen dort starten und ankommen
            Assert.True(s.CarArrivals > 0, "Einpendler-Autos müssen ankommen");
        }

        [Fact]
        public void Residents_WorkOutside_WhenNoLocalJobs_AndPreferLocalJobsLater()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(1020, 0));
            w.City.Paint(new Vector2(-100, 0), 90f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 3);
            Print(w);
            var s = w.Citizens.Stats;
            Assert.True(s.CommutersOut > 3, $"Auspendler {s.CommutersOut}");
            Assert.All(w.Citizens.Citizens.Where(c => c.WorksOutside), c => Assert.Equal(w.Traffic.ExternalConnections[0].Id, c.OutsideWorkConnectionId));
            int outBefore = s.CommutersOut;

            // Jetzt entstehen Jobs in der Stadt → Auspendler wechseln dorthin
            w.City.Paint(new Vector2(200, 0), 90f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 3);
            Print(w);
            Assert.True(w.Citizens.Stats.Population > 20, "Wohngebiet darf nicht überschrieben sein");
            Assert.True(w.Citizens.Stats.WorkingLocal > 3);
            Assert.True(w.Citizens.Stats.CommutersOut < outBefore, $"{w.Citizens.Stats.CommutersOut} >= {outBefore}");
        }

        [Fact]
        public void Locals_ReplaceCommutersIn()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(1020, 0));
            w.City.Paint(new Vector2(200, 0), 90f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 2);
            int commutersBefore = w.Citizens.Stats.CommutersIn;
            Assert.True(commutersBefore > 5);

            w.City.Paint(new Vector2(-100, 0), 90f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 4);
            Print(w);
            Assert.True(w.Citizens.Stats.WorkingLocal > 5);
            Assert.True(w.Citizens.Stats.CommutersIn < commutersBefore + w.Citizens.Stats.WorkingLocal,
                "Einwohner müssen Jobs von Einpendlern übernehmen statt nur zusätzliche Jobs zu bekommen");
        }

        [Fact]
        public void Demolition_RelocatesOrRemovesResidents_AndFiresWorkers()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 3);

            var home = w.City.Buildings.First(b => w.Citizens.HouseholdsIn(b.Id).Count > 0);
            var residents = w.Citizens.HouseholdsIn(home.Id).SelectMany(h => h.Members).ToList();
            var job = w.City.Buildings.First(b => w.Citizens.WorkersAt(b.Id).Count > 0);
            var workers = w.Citizens.WorkersAt(job.Id).ToList();

            w.City.RemoveBuilding(home);
            w.City.RemoveBuilding(job);
            Assert.Empty(w.Citizens.HouseholdsIn(home.Id));
            Assert.Empty(w.Citizens.WorkersAt(job.Id));
            foreach (var c in residents)
                Assert.True(w.Citizens.Get(c.Id) == null || c.Home.BuildingId != home.Id, "Bewohner muss umziehen oder wegziehen");
            foreach (var c in workers) Assert.NotEqual(job.Id, c.WorkplaceId);

            // Simulation läuft weiter, ohne dass jemand im abgerissenen Gebäude hängt
            Run(w, SimulationClock.TicksPerDay);
            Assert.DoesNotContain(w.Citizens.Citizens, c => c.Activity != Activity.Travelling && (c.Location.BuildingId == home.Id || c.Location.BuildingId == job.Id));
        }

        [Fact]
        public void Demand_ReactsToUnemployment()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(0, 20), 280f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 3);
            Print(w);
            // Viele Arbeitslose, keine Jobs: Zuzug lässt nach, Arbeitsplatz-Nachfrage hoch
            Assert.True(w.Citizens.Stats.Unemployed > 10);
            Assert.True(w.City.MoveInPull < 45f, $"Zuzug {w.City.MoveInPull}");
            Assert.True(w.City.Demand[ZoneType.Industry] > 50f);
        }
    }
}
