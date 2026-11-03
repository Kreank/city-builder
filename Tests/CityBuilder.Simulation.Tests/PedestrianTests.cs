using System;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.People;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Sichtbare Fußgänger: Gehwegnetz, Routen (Gehweg, Querung, Parkweg) und Interpolation über die Fahrzeit.</summary>
    public class PedestrianTests
    {
        readonly ITestOutputHelper output;
        public PedestrianTests(ITestOutputHelper output) => this.output = output;

        static readonly RoadType City = RoadCatalog.CityTwoLane;

        static void Road(RoadNetwork net, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(net, a, 8f, null, b, 8f, t ?? City);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(net, plan);
        }

        static RoadSegment SegmentNear(RoadNetwork net, Vector2 p) =>
            net.Segments.OrderBy(s => s.DistanceTo(p)).First();

        static SidewalkAccess Access(SidewalkGraph g, RoadNetwork net, Vector2 onRoad, Vector2 side)
        {
            var s = SegmentNear(net, onRoad);
            Assert.True(g.TryAccessAtT(s.Id, s.ClosestT(onRoad), side, out var a));
            return a;
        }

        static PedestrianRoute Route(SidewalkGraph g, in SidewalkAccess a, in SidewalkAccess b)
        {
            var rb = new PedestrianRouteBuilder();
            Assert.True(g.AppendPath(a, b, rb), "kein Weg gefunden");
            return rb.Build();
        }

        /// <summary>Alle Punkte (auch dazwischen, alle 0,5 m) außerhalb von Querungen liegen neben der Fahrbahn.</summary>
        static void AssertOnSidewalks(RoadNetwork net, PedestrianRoute r)
        {
            for (int i = 0; i < r.Count - 1; i++)
            {
                if (r.Surface(i) == PedestrianRoute.SurfaceCrossing || r.Surface(i + 1) == PedestrianRoute.SurfaceCrossing) continue;
                var a = r.Point(i);
                var b = r.Point(i + 1);
                int steps = Math.Max(1, (int)(Vector2.Distance(a, b) / 0.5f));
                for (int k = 0; k <= steps; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)steps);
                    foreach (var s in net.Segments)
                    {
                        float d = s.DistanceTo(p);
                        Assert.True(d >= s.Type.CarriagewayHalfWidth - 0.05f,
                            $"Punkt {p} liegt {d:0.00} m von der Mitte von {s} (Fahrbahnhälfte {s.Type.CarriagewayHalfWidth})");
                    }
                }
            }
        }

        // ------------------------------------------------------------ Gehwegnetz

        [Fact]
        public void Graph_HasSidewalkOffsetsAndCrossingsOnlyAtJunctionsAndDeadEnds()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(-200, 0), new Vector2(200, 0));
            Road(net, new Vector2(0, -200), new Vector2(0, 200)); // Kreuzung bei (0,0), vier Sackgassen-Enden
            var g = new SidewalkGraph(net);
            // 4 Arme × 2 Gehweg-Enden an der Kreuzung + 4 Enden × 2
            Assert.Equal(16, g.VertexCount);
            // Querungen: 4 Arme an der Kreuzung + 4 Sackgassen
            Assert.Equal(8, g.CrossingCount);

            var s = SegmentNear(net, new Vector2(-100, 0));
            Assert.True(g.TryAccessAtT(s.Id, s.ClosestT(new Vector2(-100, 0)), new Vector2(-100, 30), out var north));
            Assert.True(g.TryAccessAtT(s.Id, s.ClosestT(new Vector2(-100, 0)), new Vector2(-100, -30), out var south));
            float expected = City.CarriagewayHalfWidth + (City.HalfWidth - City.CarriagewayHalfWidth) * 0.5f; // 3,5 + 1,5
            Assert.Equal(expected, MathF.Abs(north.Point.Y), 2);
            Assert.Equal(expected, MathF.Abs(south.Point.Y), 2);
            Assert.True(north.Point.Y > 0f && south.Point.Y < 0f);
            Assert.NotEqual(north.Side, south.Side);
            Assert.Equal(SidewalkGraph.SidewalkLift, north.Height, 3);
        }

        [Fact]
        public void Route_StaysOnSidewalks_AroundJunctionCorner()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(-200, 0), new Vector2(200, 0));
            Road(net, new Vector2(0, -200), new Vector2(0, 200));
            var g = new SidewalkGraph(net);
            // Nordseite der West-Straße → Westseite der Nord-Straße: um die Ecke, ohne Querung
            var a = Access(g, net, new Vector2(-80, 0), new Vector2(-80, 20));
            var b = Access(g, net, new Vector2(0, 80), new Vector2(-20, 80));
            var r = Route(g, a, b);
            output.WriteLine($"Länge {r.Length:0.0} m, {r.Count} Punkte, Querungen {r.Crossings}");
            Assert.Equal(0, r.Crossings);
            Assert.True(Vector2.Distance(r.Start, a.Point) < 0.01f && Vector2.Distance(r.End, b.Point) < 0.01f);
            Assert.InRange(r.Length, 140f, 152f); // ≈ 75 + 75 m, die gerundete Ecke kürzt etwas ab
            AssertOnSidewalks(net, r);
        }

        [Fact]
        public void Route_CurvedArmsAtJunction_StayOnSidewalks()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(-150, 0), new Vector2(150, 0));
            // Zwei stark gekrümmte Arme an derselben Kreuzung (Abgangsrichtung ≠ Richtung am gekürzten Ende)
            var p1 = RoadBuilder.Plan(net, new Vector2(0, 0), 8f, new Vector2(0, 60), new Vector2(-90, 70), 8f, City);
            Assert.True(p1.IsValid, p1.Error.ToString());
            RoadBuilder.Build(net, p1);
            var p2 = RoadBuilder.Plan(net, new Vector2(0, 0), 8f, new Vector2(0, -60), new Vector2(90, -70), 8f, City);
            Assert.True(p2.IsValid, p2.Error.ToString());
            RoadBuilder.Build(net, p2);
            var g = new SidewalkGraph(net);
            foreach (var (from, side, to, toSide) in new[]
            {
                (new Vector2(-100, 0), new Vector2(-100, 20), new Vector2(-80, 70), new Vector2(-80, 50)),
                (new Vector2(100, 0), new Vector2(100, -20), new Vector2(80, -70), new Vector2(80, -50)),
                (new Vector2(-100, 0), new Vector2(-100, -20), new Vector2(100, 0), new Vector2(100, -20)),
            })
            {
                var a = Access(g, net, from, side);
                var b = Access(g, net, to, toSide);
                var r = Route(g, a, b);
                output.WriteLine($"{from} → {to}: {r.Length:0.0} m, Querungen {r.Crossings}");
                AssertOnSidewalks(net, r);
            }
        }

        [Fact]
        public void Route_CrossesStreetAtJunction_NotMidBlock()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(-300, 0), new Vector2(100, 0));
            Road(net, new Vector2(0, -150), new Vector2(0, 150));
            var g = new SidewalkGraph(net);
            // Gegenüberliegende Straßenseite 30 m vor der Kreuzung: Querung am Zebrastreifen der Kreuzung (nicht am 270 m entfernten Ende)
            var a = Access(g, net, new Vector2(-30, 0), new Vector2(-30, 20));
            var b = Access(g, net, new Vector2(-30, 0), new Vector2(-30, -20));
            var r = Route(g, a, b);
            output.WriteLine($"Länge {r.Length:0.0} m, Querungen {r.Crossings}");
            Assert.Equal(1, r.Crossings);
            for (int i = 0; i < r.Count; i++)
                if (r.Surface(i) == PedestrianRoute.SurfaceCrossing)
                {
                    Assert.True(Vector2.Distance(r.Point(i), Vector2.Zero) < 25f, $"Querung bei {r.Point(i)} nicht an der Kreuzung");
                    Assert.Equal(SidewalkGraph.RoadLift, r.Height(i), 3);
                }
            Assert.InRange(r.Length, 50f, 90f);
            AssertOnSidewalks(net, r);
        }

        [Fact]
        public void Route_DeadEnd_TurnsAroundAtTheEnd()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(0, 0), new Vector2(120, 0));
            var g = new SidewalkGraph(net);
            var a = Access(g, net, new Vector2(100, 0), new Vector2(100, 20));
            var b = Access(g, net, new Vector2(100, 0), new Vector2(100, -20));
            var r = Route(g, a, b);
            Assert.Equal(1, r.Crossings);
            Assert.InRange(r.Length, 45f, 55f); // 20 m hin, 10 m quer, 20 m zurück
        }

        [Fact]
        public void Route_FindsWayOverSeveralSegmentsAndBends()
        {
            var net = new RoadNetwork();
            // Zickzack aus vier Segmenten (Knicke mit Eckstück), gleiche Richtung
            Road(net, new Vector2(0, 0), new Vector2(100, 0));
            Road(net, new Vector2(100, 0), new Vector2(180, 60));
            Road(net, new Vector2(180, 60), new Vector2(180, 160));
            Road(net, new Vector2(180, 160), new Vector2(300, 160));
            Assert.Equal(4, net.SegmentCount);
            var g = new SidewalkGraph(net);
            var a = Access(g, net, new Vector2(20, 0), new Vector2(20, -20));     // rechte Seite in Bauichtung
            var b = Access(g, net, new Vector2(280, 160), new Vector2(280, 140));
            Assert.Equal(a.Side, b.Side);
            var r = Route(g, a, b);
            output.WriteLine($"Länge {r.Length:0.0} m, {r.Count} Punkte");
            Assert.Equal(0, r.Crossings);
            float centerline = 80f + 100f + 100f + 100f; // grob: Mittellinie von x=20 bis x=280
            Assert.InRange(r.Length, centerline * 0.85f, centerline * 1.15f);
            AssertOnSidewalks(net, r);
            // Alle Segmente werden berührt
            foreach (var s in net.Segments)
                Assert.True(Enumerable.Range(0, r.Count).Any(i => s.DistanceTo(r.Point(i)) < 6f), $"{s} nicht auf dem Weg");
        }

        [Fact]
        public void Route_NotConnected_ReturnsFalse()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(0, 0), new Vector2(100, 0));
            Road(net, new Vector2(0, 300), new Vector2(100, 300));
            var g = new SidewalkGraph(net);
            var a = Access(g, net, new Vector2(50, 0), new Vector2(50, 20));
            var b = Access(g, net, new Vector2(50, 300), new Vector2(50, 320));
            Assert.False(g.AppendPath(a, b, new PedestrianRouteBuilder()));
        }

        [Fact]
        public void Highways_HaveNoSidewalks()
        {
            var net = new RoadNetwork();
            Road(net, new Vector2(0, 0), new Vector2(300, 0), RoadCatalog.Highway2);
            var g = new SidewalkGraph(net);
            Assert.Equal(0, g.VertexCount);
            Assert.False(g.TryAccessAtT(net.Segments.First().Id, 0.5f, new Vector2(150, 20), out _));
        }

        // ------------------------------------------------------------ Planer, Park, Interpolation

        static Building FakeBuilding(int id, RoadNetwork net, Vector2 onRoad, Vector2 front)
        {
            var s = SegmentNear(net, onRoad);
            float half = City.HalfWidth;
            var entrance = onRoad - front * half;
            return new Building
            {
                Id = id, WidthCells = 2, DepthCells = 2, Front = front, Position = entrance - front * 8f,
                FrontSegmentId = s.Id, FrontT = s.ClosestT(onRoad), Construction = 1f,
            };
        }

        [Fact]
        public void Planner_RebuildsGraphAndCachesRoutes()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(200, 0));
            var planner = new PedestrianPlanner(w);
            var b1 = FakeBuilding(1, w.Roads, new Vector2(40, 0), new Vector2(0, -1));
            var b2 = FakeBuilding(2, w.Roads, new Vector2(160, 0), new Vector2(0, -1));
            Assert.True(planner.TryBuildingEndpoint(b1, out var e1));
            Assert.True(planner.TryBuildingEndpoint(b2, out var e2));
            var r = planner.Route(e1, e2);
            Assert.NotNull(r);
            Assert.Same(r, planner.Route(e1, e2));
            Assert.Equal(1, planner.RoutesComputed);
            // Tür → Gehweg → Tür
            Assert.True(Vector2.Distance(r.Start, e1.Door) < 0.01f && Vector2.Distance(r.End, e2.Door) < 0.01f);
            Assert.Equal(PedestrianRoute.SurfaceGround, r.Surface(0));
            Assert.Equal(PedestrianRoute.SurfaceSidewalk, r.Surface(1));

            var g = planner.Graph;
            Road(w.Roads, new Vector2(100, 0), new Vector2(100, 150)); // Netz ändert sich → neuer Graph, Cache leer
            Assert.NotSame(g, planner.Graph);
            Assert.Equal(0, planner.CachedRoutes);
        }

        [Fact]
        public void Planner_RouteBudget_LimitsNewRoutes()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(200, 0));
            var planner = new PedestrianPlanner(w) { RouteBudget = 0 };
            var b1 = FakeBuilding(1, w.Roads, new Vector2(40, 0), new Vector2(0, -1));
            var b2 = FakeBuilding(2, w.Roads, new Vector2(160, 0), new Vector2(0, -1));
            planner.TryBuildingEndpoint(b1, out var e1);
            planner.TryBuildingEndpoint(b2, out var e2);
            Assert.Null(planner.Route(e1, e2));
            planner.RouteBudget = 1;
            Assert.NotNull(planner.Route(e1, e2));
        }

        [Fact]
        public void ParkRoute_EndsInsideThePark()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(300, 0));
            var park = w.Services.TryBuild(ServiceCatalog.ParkSmall, new Vector2(200, 20));
            Assert.NotNull(park);
            var planner = new PedestrianPlanner(w);
            var home = FakeBuilding(1, w.Roads, new Vector2(50, 0), new Vector2(0, -1));
            Assert.True(planner.TryBuildingEndpoint(home, out var he));
            Assert.True(planner.TryParkEndpoint(park, out var pe));
            var r = planner.Route(he, pe);
            Assert.NotNull(r);
            // Letztes Stück: Tor → Parkmitte (Luftlinie auf der Parkfläche)
            Assert.True(Vector2.Distance(r.End, park.Position) < 0.01f);
            Assert.Equal(PedestrianRoute.SurfacePark, r.Surface(r.Count - 1));
            var gate = r.Point(r.Count - 2);
            Assert.True(park.Contains(gate, 0.5f), $"Tor {gate} nicht am Park");
            for (int k = 0; k <= 10; k++) Assert.True(park.Contains(Vector2.Lerp(gate, r.End, k / 10f), 0.5f));
            // Die Straße wird nicht gequert (Park auf derselben Seite wie das Haus)
            Assert.Equal(0, r.Crossings);
            AssertOnSidewalks(w.Roads, r);
        }

        [Fact]
        public void Walk_HitsStartAndEndAtTheRightTime()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(200, 0));
            var planner = new PedestrianPlanner(w);
            var b1 = FakeBuilding(1, w.Roads, new Vector2(40, 0), new Vector2(0, -1));
            var b2 = FakeBuilding(2, w.Roads, new Vector2(160, 0), new Vector2(0, 1)); // gegenüber
            planner.TryBuildingEndpoint(b1, out var e1);
            planner.TryBuildingEndpoint(b2, out var e2);
            var r = planner.Route(e1, e2);
            Assert.NotNull(r);
            Assert.Equal(1, r.Crossings);

            var p0 = PedestrianPlanner.Walk(r, 100, 103, 100.0, PedestrianSpot.Trip);
            var pm = PedestrianPlanner.Walk(r, 100, 103, 101.5, PedestrianSpot.Trip);
            var p1 = PedestrianPlanner.Walk(r, 100, 103, 103.0, PedestrianSpot.Trip);
            var late = PedestrianPlanner.Walk(r, 100, 103, 104.0, PedestrianSpot.Trip);
            Assert.Equal(PedestrianActivity.Walking, pm.Kind);
            Assert.True(Vector2.Distance(p0.Position, e1.Door) < 0.01f);
            Assert.True(Vector2.Distance(p1.Position, e2.Door) < 0.01f);
            Assert.True(Vector2.Distance(late.Position, e2.Door) < 0.01f);
            Assert.Equal(0.5f, pm.Progress, 3);
            // Geschwindigkeit = sichtbare Weglänge / Fahrzeit (länger als die Luftlinie)
            Assert.Equal(r.Length / 3f, pm.MetersPerTick, 2);
            Assert.True(r.Length > Vector2.Distance(e1.Door, e2.Door));
        }

        [Fact]
        public void Pose_FollowsPlaceholderTripOfCitizen()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(100, 20), 90f, ZoneType.ResidentialLow);
            for (int i = 0; i < 3000 && w.City.Buildings.Count(b => b.IsConstructed) < 3; i++) w.Step();
            var built = w.City.Buildings.Where(b => b.IsConstructed).OrderBy(b => b.Entrance.X).ToArray();
            Assert.True(built.Length >= 2, $"nur {built.Length} Gebäude");
            var from = built[0];
            var to = built[built.Length - 1];

            var c = new Citizen
            {
                Id = 4711, Age = 30, Activity = Activity.Travelling, Mode = TravelMode.Other, Purpose = TripPurpose.ToShop,
                Location = Place.Building(from.Id), Destination = Place.Building(to.Id), TripStartTick = 500, ArriveTick = 502,
            };
            var planner = new PedestrianPlanner(w);
            var start = planner.Pose(c, 500.0);
            var mid = planner.Pose(c, 501.0);
            var end = planner.Pose(c, 502.0);
            Assert.Equal(PedestrianActivity.Walking, mid.Kind);
            Assert.True(planner.TryBuildingEndpoint(from, out var ef));
            Assert.True(planner.TryBuildingEndpoint(to, out var et));
            Assert.True(Vector2.Distance(start.Position, ef.Door) < 0.01f);
            Assert.True(Vector2.Distance(end.Position, et.Door) < 0.01f);
            Assert.InRange(mid.Progress, 0.49f, 0.51f);

            // Autofahrer sind kein Fußgänger
            c.Mode = TravelMode.Car;
            Assert.False(planner.Pose(c, 501.0).IsVisible);
            c.Mode = TravelMode.Other;

            // Gerade angekommen: kurz vor der Haustür (BusyUntilTick = Ankunft + 2)
            c.Activity = Activity.Home;
            c.Location = Place.Building(to.Id);
            c.BusyUntil = 504;
            Assert.Equal(504, c.BusyUntilTick);
            var door = planner.Pose(c, 502.5);
            Assert.Equal(PedestrianSpot.Door, door.Spot);
            Assert.True(Vector2.Distance(door.Position, et.Door) < 2f);
            Assert.False(planner.Pose(c, 504.5).IsVisible);
            c.BusyUntil = 600; // lange zu Hause → drinnen
            Assert.False(planner.Pose(c, 502.5).IsVisible);
        }

        [Fact]
        public void Pose_ParkVisit_WalksThereStaysAndWalksBack()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(400, 0));
            var park = w.Services.TryBuild(ServiceCatalog.ParkSmall, new Vector2(300, 20));
            Assert.NotNull(park);
            w.City.Paint(new Vector2(60, 20), 50f, ZoneType.ResidentialLow);
            for (int i = 0; i < 3000 && !w.City.Buildings.Any(b => b.IsConstructed); i++) w.Step();
            var home = w.City.Buildings.First(b => b.IsConstructed);
            var hh = new Household { Id = 1, HomeId = home.Id };
            var c = new Citizen { Id = 7, Age = 40, Household = hh, Activity = Activity.Leisure, Location = Place.Building(home.Id) };
            c.LeisureParkId = park.Id;
            int walk = PedestrianPlanner.ParkWalkTicks(home.Entrance, park.Position);
            long since = 1000;
            c.BusyUntil = since + 12 + 2 * walk;
            var planner = new PedestrianPlanner(w);

            var going = planner.Pose(c, since + walk * 0.5, since);
            var staying = planner.Pose(c, since + walk + 5, since);
            var back = planner.Pose(c, c.BusyUntil - walk * 0.5, since);
            var arrived = planner.Pose(c, c.BusyUntil, since);
            Assert.Equal(PedestrianActivity.Walking, going.Kind);
            Assert.Equal(PedestrianActivity.Standing, staying.Kind);
            Assert.True(park.Contains(staying.Position), "Aufenthalt nicht auf der Parkfläche");
            Assert.Equal(PedestrianActivity.Walking, back.Kind);
            Assert.True(planner.TryBuildingEndpoint(home, out var he));
            Assert.True(Vector2.Distance(arrived.Position, he.Door) < 0.01f);
            // Unbekannter Beginn: gilt als schon im Park
            Assert.Equal(PedestrianActivity.Standing, planner.Pose(c, since + walk * 0.5).Kind);
        }

        [Fact]
        public void Pose_TransitHook_OverridesState()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(0, 0), new Vector2(200, 0));
            var planner = new PedestrianPlanner(w);
            var c = new Citizen { Id = 3, Activity = Activity.Travelling, Mode = TravelMode.Other };
            var stop = new Vector2(100, -6);
            planner.Transit = (citizen, t) => t < 10 ? TransitPose.WaitingAt(stop, 0.2f, new Vector2(0, 1))
                : t < 20 ? TransitPose.InVehicle
                : TransitPose.Walk(Place.None, new Vector2(100, -6), Place.None, new Vector2(180, -6), 20, 22);
            var waiting = planner.Pose(c, 5);
            Assert.Equal(PedestrianActivity.Waiting, waiting.Kind);
            Assert.Equal(PedestrianSpot.Stop, waiting.Spot);
            Assert.Equal(stop, waiting.Position);
            Assert.False(planner.Pose(c, 15).IsVisible);
            var walking = planner.Pose(c, 21);
            Assert.Equal(PedestrianActivity.Walking, walking.Kind);
            Assert.InRange(walking.Position.X, 120f, 160f);
        }

        [Fact]
        public void RunningCity_PlaceholderWalkersGetRoutes()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w.Roads, new Vector2(-150, 0), new Vector2(150, 0));
            Road(w.Roads, new Vector2(0, -150), new Vector2(0, 150));
            w.City.Paint(new Vector2(-70, 25), 60f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(70, -25), 60f, ZoneType.Retail);
            w.City.Paint(new Vector2(70, 25), 60f, ZoneType.Office);
            var planner = new PedestrianPlanner(w);
            int walking = 0, visible = 0, routed = 0;
            for (int i = 0; i < 2400; i++)
            {
                w.Step();
                if (i < 1200 || i % 4 != 0) continue;
                foreach (var c in w.Citizens.Citizens)
                {
                    if (c.Activity == Activity.Travelling && !c.HasCarTrip && c.Mode != TravelMode.Car && c.Location.IsBuilding && c.Destination.IsBuilding)
                    {
                        walking++;
                        if (planner.Route(c.Location, c.Destination) != null) routed++;
                    }
                    if (planner.Pose(c, w.Clock.Tick + 0.5).IsVisible) visible++;
                }
            }
            output.WriteLine($"Fußwege {walking}, davon mit Route {routed}; sichtbare Posen {visible}; Routen berechnet {planner.RoutesComputed}");
            Assert.True(walking > 0, "keine Platzhalter-Fahrten");
            Assert.True(routed >= walking * 0.9f, $"{routed}/{walking} Fußwege mit Route");
            Assert.True(visible > 0);
        }
    }
}
