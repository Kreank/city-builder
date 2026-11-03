using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class TrafficTests
    {
        readonly ITestOutputHelper output;
        public TrafficTests(ITestOutputHelper output) => this.output = output;

        static List<RoadSegment> Road(RoadNetwork net, Vector2 a, Vector2 b, RoadType type = null, Vector2? control = null)
        {
            var plan = RoadBuilder.Plan(net, a, 8f, control, b, 8f, type ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            return RoadBuilder.Build(net, plan);
        }

        static SimulationWorld CrossroadWorld()
        {
            var world = SimulationWorld.CreateDefault();
            // Seit G2 entstehen Fahrten nur noch an Außenanschlüssen (Kartenrand) bzw. aus Gebäuden.
            // Diese Tests prüfen das Fahrverhalten, deshalb gelten die Sackgassen hier als Außenanschluss.
            world.Traffic.DeadEndsAreExternal = true;
            Road(world.Roads, new Vector2(-300, 0), new Vector2(300, 0));
            Road(world.Roads, new Vector2(0, -300), new Vector2(0, 300));
            world.Traffic.Lanes.Update();
            return world;
        }

        [Fact]
        public void LaneGraph_FourWayCrossing_HasLeftStraightRightPerApproach()
        {
            var world = CrossroadWorld();
            var center = world.Roads.FindNearestNode(Vector2.Zero, 1f);
            var conns = world.Traffic.Lanes.GetConnections(center.Id);
            Assert.Equal(12, conns.Count);
            Assert.Equal(4, conns.Count(c => c.Turn == TurnType.Left));
            Assert.Equal(4, conns.Count(c => c.Turn == TurnType.Right));
            Assert.Equal(4, conns.Count(c => c.Turn == TurnType.Straight));
        }

        [Fact]
        public void LaneGraph_FourLaneRoad_LeftLaneTurnsLeft_RightLaneTurnsRight()
        {
            var world = SimulationWorld.CreateDefault();
            Road(world.Roads, new Vector2(-300, 0), new Vector2(300, 0), RoadCatalog.CityFourLane);
            Road(world.Roads, new Vector2(0, -300), new Vector2(0, 300));
            world.Traffic.Lanes.Update();
            var center = world.Roads.FindNearestNode(Vector2.Zero, 1f);
            var fromWest = world.Traffic.Lanes.GetConnections(center.Id)
                .Where(c => c.From.Segment.Type == RoadCatalog.CityFourLane && c.From.Along && c.From.To == center).ToList();

            var leftLane = fromWest.Select(c => c.From).OrderBy(l => l.Rank).First();
            var rightLane = fromWest.Select(c => c.From).OrderBy(l => l.Rank).Last();
            Assert.Contains(fromWest, c => c.From == leftLane && c.Turn == TurnType.Left);
            Assert.DoesNotContain(fromWest, c => c.From == leftLane && c.Turn == TurnType.Right);
            Assert.Contains(fromWest, c => c.From == rightLane && c.Turn == TurnType.Right);
            Assert.Contains(fromWest, c => c.From == rightLane && c.Turn == TurnType.Straight);
        }

        [Fact]
        public void LaneGraph_OneWayHasNoBackwardLanes_DeadEndAllowsUTurn()
        {
            var world = SimulationWorld.CreateDefault();
            var oneWay = Road(world.Roads, new Vector2(0, 0), new Vector2(100, 0), RoadCatalog.CityOneWay2)[0];
            var twoWay = Road(world.Roads, new Vector2(0, 200), new Vector2(100, 200))[0];
            world.Traffic.Lanes.Update();

            Assert.All(world.Traffic.Lanes.GetLanes(oneWay.Id), l => Assert.True(l.Along));
            var uturns = world.Traffic.Lanes.GetConnections(twoWay.End.Id);
            Assert.Contains(uturns, c => c.Turn == TurnType.UTurn);
        }

        [Fact]
        public void Pathfinder_RespectsTurnBanAndClosure()
        {
            var world = CrossroadWorld();
            var t = world.Traffic;
            var net = world.Roads;
            var center = net.FindNearestNode(Vector2.Zero, 1f);
            var west = net.Segments.Single(s => s.Touches(center) && s.Other(center).Position.X < -100);
            var north = net.Segments.Single(s => s.Touches(center) && s.Other(center).Position.Y > 100);
            var start = new EdgeKey(west.Id, west.End == center);
            var dest = north.Other(center);

            var path = t.Paths.FindPath(start, dest, VehicleClass.Car);
            Assert.NotNull(path);
            Assert.Equal(2, path.Count);

            t.Rules.SetTurnBan(center.Id, west.Id, north.Id, VehicleClass.Car);
            var detour = t.Paths.FindPath(start, dest, VehicleClass.Car);
            Assert.NotNull(detour);                      // Umweg: geradeaus, in der Sackgasse wenden, rechts ab
            Assert.True(detour.Count > 2);
            Assert.Equal(2, t.Paths.FindPath(start, dest, VehicleClass.Bus).Count); // Verbot gilt nur für Pkw

            t.Rules.SetTurnBan(center.Id, west.Id, north.Id, VehicleClass.None);
            t.Rules.SetClosed(north.Id, true);
            Assert.Null(t.Paths.FindPath(start, dest, VehicleClass.Car));
        }

        [Fact]
        public void Rules_SurviveSplit()
        {
            var world = SimulationWorld.CreateDefault();
            var seg = Road(world.Roads, new Vector2(0, 0), new Vector2(200, 0))[0];
            world.Traffic.Rules.SetBanned(seg.Id, VehicleClass.Truck);
            world.Roads.SplitSegment(seg, 0.5f);
            Assert.All(world.Roads.Segments, s => Assert.False(world.Traffic.Rules.IsSegmentAllowed(s.Id, VehicleClass.Truck)));
        }

        /// <summary>Zeitmodell: Fahrverhalten in Verkehrsschritten (<see cref="TrafficSystem.Dt"/> = 0,25 s), nicht in Kalender-Ticks.</summary>
        static void Run(SimulationWorld world, int trafficSteps)
        {
            for (int i = 0; i < trafficSteps; i++) world.Substep();
        }

        [Fact]
        public void Traffic_FlowsThroughUncontrolledCrossroad()
        {
            var world = CrossroadWorld();
            Run(world, 4000); // ~17 Minuten Fahrzeit
            var s = world.Traffic.Stats;
            output.WriteLine($"spawned {s.Spawned} arrived {s.Arrived} despawned {s.Despawned} active {s.Active}");
            Assert.True(s.Spawned > 50);
            Assert.True(s.Arrived > s.Spawned * 0.6f, $"zu wenige angekommen: {s.Arrived}/{s.Spawned}");
            Assert.True(s.Despawned <= s.Spawned * 0.05f, $"zu viele hängen geblieben: {s.Despawned}");
        }

        [Fact]
        public void Traffic_FlowsWithTrafficLightsAndSigns()
        {
            foreach (var control in new[] { JunctionControl.TrafficLights, JunctionControl.Signs, JunctionControl.RightBeforeLeft })
            {
                var world = CrossroadWorld();
                var center = world.Roads.FindNearestNode(Vector2.Zero, 1f);
                world.Traffic.Rules.SetControl(center.Id, control);
                if (control == JunctionControl.Signs)
                    foreach (int id in center.SegmentIds)
                    {
                        var seg = world.Roads.GetSegment(id);
                        bool eastWest = System.MathF.Abs(seg.Other(center).Position.Y) < 1f;
                        world.Traffic.Rules.SetSign(center.Id, id, eastWest ? ApproachSign.Priority : ApproachSign.Stop);
                    }
                Run(world, 4000);
                var s = world.Traffic.Stats;
                output.WriteLine($"{control}: spawned {s.Spawned} arrived {s.Arrived} despawned {s.Despawned}");
                Assert.True(s.Arrived > s.Spawned * 0.5f, $"{control}: {s.Arrived}/{s.Spawned}");
                Assert.True(s.Despawned <= s.Spawned * 0.08f, $"{control}: despawned {s.Despawned}");
            }
        }

        [Fact]
        public void Traffic_UsesRoundabout()
        {
            var world = CrossroadWorld();
            var plan = RoadBuilder.PlanRoundabout(world.Roads, Vector2.Zero, 30f, RoadCatalog.DefaultRoundabout);
            RoadBuilder.BuildRoundabout(world.Roads, plan);
            Run(world, 4000);
            var s = world.Traffic.Stats;
            output.WriteLine($"roundabout: spawned {s.Spawned} arrived {s.Arrived} despawned {s.Despawned}");
            Assert.True(s.Arrived > s.Spawned * 0.5f);
            Assert.True(s.Despawned <= s.Spawned * 0.08f);
        }

        [Fact]
        public void Cloverleaf_ConnectsEveryDirection()
        {
            var world = SimulationWorld.CreateDefault();
            var plan = RoadTemplates.Plan(world.Roads, RoadTemplateKind.Cloverleaf, Vector2.Zero, 0f);
            RoadTemplates.Build(world.Roads, plan);
            world.Traffic.Lanes.Update();
            var t = world.Traffic;

            // Einfahrende Enden: Knoten vom Grad 1 mit ausgehender Spur; ausfahrende: mit eingehender Spur
            var ends = world.Roads.Nodes.Where(n => n.SegmentIds.Count == 1).ToList();
            Assert.Equal(8, ends.Count);
            var sources = ends.Where(n => t.Lanes.OutgoingLanes(n).Any()).ToList();
            var sinks = ends.Where(n => t.Lanes.IncomingLanes(n).Any()).ToList();
            Assert.Equal(4, sources.Count);
            Assert.Equal(4, sinks.Count);

            foreach (var src in sources)
            {
                var edge = t.Lanes.OutgoingLanes(src).First().Edge;
                int reachable = sinks.Count(dst => t.Paths.FindPath(edge, dst, VehicleClass.Car) != null);
                Assert.Equal(4, reachable); // 3 Richtungen + Wenden über Schleife/Rampe zählt mit
            }
        }

        [Fact]
        public void Triangle_And_Diamond_AreConnected()
        {
            foreach (var kind in new[] { RoadTemplateKind.Triangle, RoadTemplateKind.DiamondInterchange })
            {
                var world = SimulationWorld.CreateDefault();
                RoadTemplates.Build(world.Roads, RoadTemplates.Plan(world.Roads, kind, Vector2.Zero, 0f));
                world.Traffic.Lanes.Update();
                var t = world.Traffic;
                var ends = world.Roads.Nodes.Where(n => n.SegmentIds.Count == 1).ToList();
                var sources = ends.Where(n => t.Lanes.OutgoingLanes(n).Any()).ToList();
                var sinks = ends.Where(n => t.Lanes.IncomingLanes(n).Any()).ToList();
                foreach (var src in sources)
                {
                    var edge = t.Lanes.OutgoingLanes(src).First().Edge;
                    int reachable = sinks.Count(dst => t.Paths.FindPath(edge, dst, VehicleClass.Car) != null);
                    output.WriteLine($"{kind}: {src} erreicht {reachable}/{sinks.Count}");
                    Assert.True(reachable >= sinks.Count - 1, $"{kind}: {src} erreicht nur {reachable}/{sinks.Count}");
                }
            }
        }

        [Fact]
        public void Traffic_FlowsOverCloverleaf()
        {
            var world = SimulationWorld.CreateDefault();
            world.Traffic.DeadEndsAreExternal = true; // offene Enden der Vorlage = Außenanschlüsse (s. CrossroadWorld)
            RoadTemplates.Build(world.Roads, RoadTemplates.Plan(world.Roads, RoadTemplateKind.Cloverleaf, Vector2.Zero, 0f));
            Run(world, 6000);
            var s = world.Traffic.Stats;
            output.WriteLine($"cloverleaf: spawned {s.Spawned} arrived {s.Arrived} despawned {s.Despawned}");
            Assert.True(s.Arrived > s.Spawned * 0.5f);
            Assert.True(s.Despawned <= s.Spawned * 0.08f);
        }
    }
}
