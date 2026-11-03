using System;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// UX-Runde: Straßen sperren an Dienst-/Versorgungsbauwerken (statt sie abzureißen), Ausrichtung zur Straße mit
    /// Zufahrt an der Vorderseite, Verschieben von Bauwerken.
    /// </summary>
    public class StructureRulesTests
    {
        readonly ITestOutputHelper output;
        public StructureRulesTests(ITestOutputHelper output) => this.output = output;

        static RoadPlan PlanRoad(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null, Vector2? control = null) =>
            RoadBuilder.Plan(w.Roads, a, 8f, control, b, 8f, t ?? RoadCatalog.CityTwoLane);

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null, Vector2? control = null)
        {
            var plan = PlanRoad(w, a, b, t, control);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static float HalfWidth => RoadCatalog.CityTwoLane.HalfWidth;

        // ------------------------------------------------------------ Straßensperre

        [Fact]
        public void RoadThroughService_IsInvalid_StraightCurveRoundaboutUpgrade_AndBuildingStays()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var police = w.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(0, 25));
            Assert.True(police.IsValid, police.Error.ToString());
            var f = w.Services.Build(police);
            Assert.NotNull(f);

            // gerade quer durch
            var straight = PlanRoad(w, new Vector2(0, 200), new Vector2(0, 2));
            Assert.Equal(RoadBuildError.OverlapsStructure, straight.Error);
            Assert.Equal("service", straight.BlockedBy.Kind);
            Assert.Equal(f.Id, straight.BlockedBy.Id);
            Assert.Empty(RoadBuilder.Build(w.Roads, straight));

            // Kurve, die das Gebäude streift
            var curve = PlanRoad(w, new Vector2(-60, 120), new Vector2(60, 120), control: new Vector2(0, -60));
            Assert.Equal(RoadBuildError.OverlapsStructure, curve.Error);

            // Kreisverkehr auf dem Gebäude
            var ring = RoadBuilder.PlanRoundabout(w.Roads, f.Position, 20f, RoadCatalog.DefaultRoundabout);
            Assert.Equal(RoadBuildError.OverlapsStructure, ring.Error);
            Assert.NotNull(ring.BlockedBy);

            // Ausbau auf eine breitere Straße, die bis aufs Gebäude reichen würde
            var seg = w.Roads.Segments.First();
            var wide = RoadCatalog.All.OrderByDescending(t => t.Width).First(t => t.Category == RoadCategory.City);
            Assert.True(wide.HalfWidth > HalfWidth + RoadFront.SnapGap);
            Assert.Equal(RoadBuildError.OverlapsStructure, RoadBuilder.CheckReplace(w.Roads, seg, wide));
            Assert.Null(RoadBuilder.Replace(w.Roads, seg, wide));

            // Brücke darüber hinweg ist erlaubt (Elevation > 3 m)
            var bridge = RoadBuilder.Plan(w.Roads, new Vector2(-15, 200), 0f, null, new Vector2(-15, 40), 0f, RoadCatalog.CityTwoLane, 10f, 10f);
            Assert.NotEqual(RoadBuildError.OverlapsStructure, bridge.Error);

            // daneben vorbei geht
            Assert.True(PlanRoad(w, new Vector2(60, 200), new Vector2(60, 2)).IsValid);

            w.Services.ForceTick();
            Assert.NotNull(w.Services.GetFacility(f.Id));
            Assert.Equal(1, w.Services.FacilityCount);
        }

        [Fact]
        public void RoadThroughUtility_IsInvalid_PolesAreStillRerouted()
        {
            var w = SimulationWorld.CreateDefault();
            var u = w.Utilities;
            var wind = u.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(0, 100));
            Assert.NotNull(wind);
            var plan = PlanRoad(w, new Vector2(-100, 100), new Vector2(100, 100));
            Assert.Equal(RoadBuildError.OverlapsStructure, plan.Error);
            Assert.Equal("utility", plan.BlockedBy.Kind);
            Assert.Equal(UtilityCatalog.WindTurbine.DisplayName, plan.BlockedBy.Name);

            // Strommasten bleiben wie bisher: Straße durch einen Mast wird gebaut, die Leitung umgelegt
            var line = u.TryBuildLine(UtilityKind.Power, new Vector2(300, -100), new Vector2(300, 100));
            Assert.NotNull(line);
            var pole = line.Poles[1];
            Road(w, new Vector2(200, pole.Y), new Vector2(400, pole.Y));
            Assert.NotNull(u.GetFacility(wind.Id));
        }

        [Fact]
        public void RoadThroughZone_StillRemovesZoneBuildings()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(0, 20), 80f, ZoneType.ResidentialLow);
            for (int i = 0; i < 3000 && w.City.Buildings.Count(b => b.IsConstructed) < 6; i++) w.Step();
            var target = w.City.Buildings.Where(b => b.IsConstructed && b.Position.Y > 0).OrderBy(b => MathF.Abs(b.Position.X)).First();
            var plan = PlanRoad(w, new Vector2(target.Position.X, 150), new Vector2(target.Position.X, 0));
            Assert.True(plan.IsValid, plan.Error.ToString()); // Zonen-Gebäude sperren nicht
            RoadBuilder.Build(w.Roads, plan);
            for (int i = 0; i < 20; i++) w.Step();
            Assert.Null(w.City.GetBuilding(target.Id));
        }

        // ------------------------------------------------------------ Ausrichtung, Zufahrt an der Vorderseite

        [Fact]
        public void Aligned_FrontFacesRoad_SnapsToSidewalk_AndAccessIsAtFront()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var s = w.Services;
            var type = ServiceCatalog.FireStation;
            foreach (var cursor in new[] { new Vector2(30, 25), new Vector2(-50, -40), new Vector2(10, 3) })
            {
                var plan = s.PlanAligned(type, cursor);
                Assert.True(plan.IsValid, $"{cursor}: {plan.Error}");
                Assert.True(plan.Aligned);
                // Vorderseite zeigt zur Straße (y = 0), Vorderkante am Gehweg
                Assert.Equal(-MathF.Sign(plan.Position.Y), MathF.Sign(plan.Front.Y));
                Assert.Equal(1f, MathF.Abs(plan.Front.Y), 3);
                Assert.Equal(HalfWidth + RoadFront.SnapGap, MathF.Abs(plan.FrontMid.Y), 2);
                // entlang der Straße verschiebbar: Mitte folgt dem Cursor
                Assert.Equal(cursor.X, plan.Position.X, 1);
                // Anschluss an der Vorderseite (Mitte)
                var seg = w.Roads.GetSegment(plan.AccessSegmentId);
                Assert.NotNull(seg);
                Assert.Equal(cursor.X, seg.Evaluate(plan.AccessT).X, 0);
            }
        }

        [Fact]
        public void Aligned_OnCurve_UsesTangent()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-150, 0), new Vector2(150, 0), control: new Vector2(0, 200));
            var seg = w.Roads.Segments.Single();
            var s = w.Services;
            var plan = s.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(-60, 110)); // außen am Bogen
            Assert.True(plan.IsValid, plan.Error.ToString());
            float t = seg.ClosestT(plan.FrontMid);
            var tan = seg.Tangent(t);
            Assert.True(MathF.Abs(Vector2.Dot(plan.Front, tan)) < 0.02f, "Vorderseite senkrecht zur Tangente");
            Assert.True(Vector2.Dot(seg.Evaluate(t) - plan.FrontMid, plan.Front) > 0f, "Straße liegt vor der Vorderseite");
            Assert.Equal(HalfWidth + RoadFront.SnapGap, Vector2.Distance(seg.Evaluate(t), plan.FrontMid), 1);
        }

        [Fact]
        public void Rotating_AwayFromRoad_GivesNoRoadAccess()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var s = w.Services;
            Assert.True(s.PlanAligned(ServiceCatalog.Clinic, new Vector2(0, 20), 15f).IsValid);  // leicht schräg: Zufahrt bleibt
            var side = s.PlanAligned(ServiceCatalog.Clinic, new Vector2(0, 20), 90f);           // Vorderseite längs der Straße
            Assert.Equal(ServiceBuildError.NoRoadAccess, side.Error);
            var back = s.PlanAligned(ServiceCatalog.Clinic, new Vector2(0, 20), 180f);          // Rücken zur Straße
            Assert.Equal(ServiceBuildError.NoRoadAccess, back.Error);
            // Rückseite an der Straße zählt nicht mehr als Zufahrt (früher: irgendein Rand ≤ 8 m)
            Assert.Equal(ServiceBuildError.NoRoadAccess, s.Plan(ServiceCatalog.Clinic, new Vector2(0, -16), 180f).Error);
            Assert.True(s.Plan(ServiceCatalog.Clinic, new Vector2(0, -16), 0f).IsValid);
            // Parks brauchen keine Vorderseite und richten sich nicht aus
            var park = s.PlanAligned(ServiceCatalog.ParkSmall, new Vector2(0, 30), 90f);
            Assert.True(park.IsValid);
            Assert.False(park.Aligned);
        }

        [Fact]
        public void Utilities_AlignOnlyWithRoadFront_WindNeedsNone()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var u = w.Utilities;
            var coal = u.PlanFacilityAligned(UtilityCatalog.CoalPlant, new Vector2(0, -40));
            Assert.True(coal.IsValid, coal.Error.ToString());
            Assert.True(coal.Aligned);
            Assert.Equal(1f, coal.Front.Y, 3); // Vorderseite nach Norden zur Straße
            Assert.Equal(UtilityBuildError.NoRoadAccess, u.PlanFacilityAligned(UtilityCatalog.CoalPlant, new Vector2(0, -40), 180f).Error);
            // Überland ohne Straße in der Nähe: keine Zufahrtspflicht (Anschluss per Leitung)
            Assert.True(u.PlanFacility(UtilityCatalog.CoalPlant, new Vector2(0, 400)).IsValid);
            var wind = u.PlanFacilityAligned(UtilityCatalog.WindTurbine, new Vector2(0, 20), 45f);
            Assert.True(wind.IsValid);
            Assert.False(wind.Aligned);
            Assert.False(UtilityCatalog.SolarPark.NeedsRoadFront);
            Assert.False(UtilityCatalog.WaterPump.NeedsRoadFront);
        }

        // ------------------------------------------------------------ Verschieben

        [Fact]
        public void MoveService_KeepsIdAndState_Costs_AndChecksRules()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false });
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            var s = w.Services;
            var dump = s.Build(s.PlanAligned(ServiceCatalog.Landfill, new Vector2(-100, 40)));
            Assert.NotNull(dump);
            var police = s.Build(s.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(100, 30)));
            Assert.NotNull(police);
            // Zustand: Lager gefüllt
            dump.Stored = 123f; // Testprojekt kompiliert die Quellen mit (internal sichtbar)
            int id = dump.Id;

            // Regeln: nicht auf die Straße, nicht auf ein anderes Bauwerk, nicht mit dem Rücken zur Straße
            Assert.Equal(ServiceBuildError.OverlapsRoad, s.PlanMove(id, new Vector2(0, 5), Vector2.UnitY).Error);
            Assert.Equal(ServiceBuildError.OverlapsFacility, s.PlanMove(id, new Vector2(100, 40), -Vector2.UnitY).Error);
            Assert.Equal(ServiceBuildError.NoRoadAccess, s.PlanMove(id, new Vector2(0, -40), -Vector2.UnitY).Error);
            // Ein Stück entlang der Straße (überlappt die eigene alte Fläche – erlaubt)
            var target = s.PlanAligned(ServiceCatalog.Landfill, new Vector2(-80, 40), 0f, id);
            Assert.True(target.IsValid, target.Error.ToString());
            Assert.Equal(ServiceCatalog.Landfill.Cost * ServiceSystem.MoveCostShare, target.Cost, 2);

            double before = w.Economy.Balance;
            int structureVersion = s.StructureVersion;
            int moved = 0;
            s.FacilityMoved += f => moved++;
            Assert.True(s.MoveFacility(target));
            Assert.Equal(before - target.Cost, w.Economy.Balance, 2);
            Assert.Same(dump, s.GetFacility(id));
            Assert.Equal(123f, dump.Stored);
            Assert.Equal(target.Position, dump.Position);
            Assert.True(dump.HasAccess);
            Assert.Equal(1, moved);
            Assert.True(s.StructureVersion > structureVersion);
            Assert.Equal(2, s.FacilityCount);

            // Straßen-Sperre folgt dem neuen Standort
            Assert.Equal(RoadBuildError.OverlapsStructure, PlanRoad(w, new Vector2(dump.Position.X, 200), new Vector2(dump.Position.X, 2)).Error);
            s.ForceTick();
            Assert.NotNull(dump.Reach);
        }

        [Fact]
        public void MoveUtility_KeepsIdAndOutput_AndChecksRules()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            var u = w.Utilities;
            var gas = u.BuildFacility(u.PlanFacilityAligned(UtilityCatalog.GasPlant, new Vector2(-100, 40)));
            Assert.NotNull(gas);
            var wind = u.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(100, 60));
            Assert.NotNull(wind);
            Assert.Equal(UtilityBuildError.OverlapsFacility, u.PlanMove(gas.Id, wind.Position, Vector2.UnitY).Error);
            Assert.Equal(UtilityBuildError.OverlapsRoad, u.PlanMove(gas.Id, new Vector2(0, 5), Vector2.UnitY).Error);
            var plan = u.PlanFacilityAligned(UtilityCatalog.GasPlant, new Vector2(0, -40), 0f, gas.Id);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(UtilityCatalog.GasPlant.Cost * UtilitySystem.MoveCostShare, plan.Cost, 2);
            Assert.True(u.MoveFacility(plan));
            Assert.Same(gas, u.GetFacility(gas.Id));
            Assert.Equal(plan.Position, gas.Position);
            Assert.Equal(1, gas.Front.Y, 3);
            u.ForceTick();
            Assert.True(gas.NetworkId >= 0);
            Assert.Equal(2, u.FacilityCount);
            Assert.True(PlanRoad(w, new Vector2(-100, 200), new Vector2(-100, 2)).IsValid, "alte Fläche ist frei");
        }
    }
}
