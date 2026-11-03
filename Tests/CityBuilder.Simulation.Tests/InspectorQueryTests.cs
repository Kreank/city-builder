using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Lesende Abfragen für den Inspektor (Unity-Seite): Haushaltsart, Fahrt ↔ Bürger, Fahrtbeginn.</summary>
    public class InspectorQueryTests
    {
        static List<Citizen> Members(params int[] ages) => ages.Select((a, i) => new Citizen { Id = i + 1, Age = a }).ToList();

        [Theory]
        [InlineData(HouseholdKind.Single, 30)]
        [InlineData(HouseholdKind.Couple, 30, 32)]
        [InlineData(HouseholdKind.Family, 40, 38, 10, 4)]
        [InlineData(HouseholdKind.SingleParent, 35, 8)]
        [InlineData(HouseholdKind.Seniors, 70)]
        [InlineData(HouseholdKind.Seniors, 70, 72)]
        [InlineData(HouseholdKind.Couple, 64, 67)]
        [InlineData(HouseholdKind.Other, 25, 26, 27)]
        [InlineData(HouseholdKind.Other, 12)]
        public void HouseholdKind_IsDerivedFromMemberAges(HouseholdKind expected, params int[] ages)
        {
            Assert.Equal(expected, HouseholdKinds.Classify(Members(ages)));
        }

        [Fact]
        public void HouseholdKind_OfEmptyHousehold_IsOther()
        {
            Assert.Equal(HouseholdKind.Other, new Household().Kind);
        }

        [Fact]
        public void CarTrips_MapToCitizen_AndBack()
        {
            var w = SimulationWorld.CreateDefault();
            w.Citizens.CarShare = 1f;
            var plan = RoadBuilder.Plan(w.Roads, new Vector2(-300, 0), 8f, null, new Vector2(300, 0), 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid);
            RoadBuilder.Build(w.Roads, plan);
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);

            int checkedVehicles = 0;
            for (int i = 0; i < SimulationClock.TicksPerDay * 4 && checkedVehicles < 5; i++)
            {
                w.Step();
                foreach (var v in w.Traffic.ActiveVehicles.Where(v => v.TripId != 0))
                {
                    var c = w.Citizens.CitizenForTrip(v.TripId);
                    if (c == null) continue; // Fahrt im selben Tick beendet
                    Assert.True(c.HasCarTrip);
                    Assert.Equal(TravelMode.Car, c.Mode);
                    Assert.Equal(Activity.Travelling, c.Activity);
                    Assert.Same(v, w.Citizens.VehicleOf(c));
                    Assert.True(c.TripStartTick <= w.Clock.Tick);
                    Assert.True(v.StartTime <= w.Traffic.Time);
                    checkedVehicles++;
                }
            }
            Assert.True(checkedVehicles > 0, "mindestens eine Bürgerfahrt im Auto erwartet");

            // Durchgangsverkehr/unbekannte Fahrt → kein Bürger; Bürger ohne Autofahrt → kein Fahrzeug
            Assert.Null(w.Citizens.CitizenForTrip(0));
            Assert.Null(w.Citizens.CitizenForTrip(int.MaxValue - 7));
            var walker = w.Citizens.Citizens.FirstOrDefault(c => !c.HasCarTrip);
            Assert.NotNull(walker);
            Assert.Null(w.Citizens.VehicleOf(walker));
        }
    }
}
