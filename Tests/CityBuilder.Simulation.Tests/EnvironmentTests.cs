using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class EnvironmentTests
    {
        static GridLayer NewLayer() => new GridLayer("test", new Vector2(-50, -50), 10, 10, 10f);

        [Fact]
        public void WorldToCell_MapsCornersCorrectly()
        {
            var layer = NewLayer();
            Assert.True(layer.TryWorldToCell(new Vector2(-50, -50), out int x0, out int y0));
            Assert.Equal((0, 0), (x0, y0));
            Assert.True(layer.TryWorldToCell(new Vector2(49.9f, 49.9f), out int x1, out int y1));
            Assert.Equal((9, 9), (x1, y1));
            Assert.False(layer.TryWorldToCell(new Vector2(50.1f, 0), out _, out _));
        }

        [Fact]
        public void StampRadialMax_DoesNotAccumulate()
        {
            var layer = NewLayer();
            layer.StampRadialMax(new Vector2(5, 5), 30f, 0.8f);
            layer.StampRadialMax(new Vector2(5, 5), 30f, 0.8f);
            Assert.InRange(layer.Sample(new Vector2(5, 5)), 0.79f, 0.8f);
            Assert.Equal(0f, layer.Sample(new Vector2(-45, -45)));
        }

        [Fact]
        public void Diffuse_SpreadsToNeighboursAndConservesInterior()
        {
            var layer = NewLayer();
            layer[5, 5] = 1f;
            layer.Diffuse(0.4f);
            Assert.Equal(0.6f, layer[5, 5], 4);
            Assert.Equal(0.1f, layer[4, 5], 4);
            Assert.Equal(0.1f, layer[5, 6], 4);

            float sum = 0f;
            foreach (var v in layer.RawValues) sum += v;
            Assert.Equal(1f, sum, 4);
        }

        [Fact]
        public void NoiseSystem_MakesRoadsLoud()
        {
            var world = SimulationWorld.CreateDefault();
            RoadBuilder.Build(world.Roads, RoadBuilder.Plan(world.Roads, new Vector2(0, 0), 0f, null, new Vector2(200, 0), 0f, RoadCatalog.Highway3));

            for (int i = 0; i < 10; i++) world.Step(); // NoiseSystem läuft alle 10 Ticks

            var noise = world.Environment.Noise;
            Assert.True(noise.Sample(new Vector2(100, 0)) > 0.8f);
            Assert.True(noise.Sample(new Vector2(100, 60)) > 0f);
            Assert.Equal(0f, noise.Sample(new Vector2(100, 500)));
        }

        [Fact]
        public void Clock_AdvancesThroughDays()
        {
            var world = SimulationWorld.CreateDefault();
            for (int i = 0; i < SimulationClock.TicksPerDay + 60; i++) world.Step();
            Assert.Equal(1, world.Clock.Day);
            Assert.Equal(6, world.Clock.Hour);
        }
    }
}
