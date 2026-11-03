using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class TerraformTests
    {
        static SimulationWorld FlatWorld() => SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = 1024f });

        static TerraformBrush Brush(TerraformMode mode, Vector2 c, float radius, float strength, float softness = 0f, float target = 0f) =>
            new TerraformBrush(mode, c, radius, strength, softness, target);

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        [Fact]
        public void Raise_And_Lower_ChangeHeightInsideRadiusOnly_WithSoftEdge()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            var r = t.Terraform(Brush(TerraformMode.Raise, Vector2.Zero, 20f, 1f, softness: 0.5f));
            Assert.True(r.Changed);
            Assert.Equal(1f, t.HeightAt(Vector2.Zero), 3);
            Assert.Equal(1f, t.HeightAt(new Vector2(8, 0)), 3);           // innerer Bereich voll
            float edge = t.HeightAt(new Vector2(16, 0));
            Assert.True(edge > 0.05f && edge < 0.95f, $"weiche Kante erwartet: {edge}");
            Assert.Equal(0f, t.HeightAt(new Vector2(28, 0)), 3);          // außerhalb unverändert
            Assert.True(r.Volume > 100f, $"Erdmenge: {r.Volume}");

            for (int i = 0; i < 3; i++) t.Terraform(Brush(TerraformMode.Lower, Vector2.Zero, 20f, 1f, softness: 0.5f));
            Assert.Equal(-2f, t.HeightAt(Vector2.Zero), 3);
        }

        [Fact]
        public void Lower_BelowWaterLevel_CreatesWater_AndRespectsMinimum()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            t.WaterLevel = 0f;
            var p = new Vector2(40, 40);
            Assert.False(t.IsWater(p));
            t.Terraform(Brush(TerraformMode.Lower, p, 16f, 3f));
            Assert.True(t.IsWater(p));
            Assert.Equal(-3f, t.HeightAt(p), 3);

            for (int i = 0; i < 20; i++) t.Terraform(Brush(TerraformMode.Lower, p, 16f, 5f));
            Assert.Equal(TerrainMap.MinTerraformHeight, t.HeightAt(p), 3);
            // Wieder auffüllen → kein Wasser mehr
            for (int i = 0; i < 20; i++) t.Terraform(Brush(TerraformMode.Raise, p, 16f, 5f));
            Assert.False(t.IsWater(p));
        }

        [Fact]
        public void Smooth_FlattensSpike()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            t.Terraform(Brush(TerraformMode.Raise, Vector2.Zero, 3f, 10f)); // nur der Mittelpunkt
            Assert.Equal(10f, t.HeightAt(Vector2.Zero), 3);
            Assert.Equal(0f, t.HeightAt(new Vector2(4, 0)), 3);

            var r = t.Terraform(Brush(TerraformMode.Smooth, Vector2.Zero, 12f, 20f));
            Assert.True(r.Changed);
            float peak = t.HeightAt(Vector2.Zero), side = t.HeightAt(new Vector2(4, 0));
            Assert.True(peak < 2f, $"Spitze: {peak}");
            Assert.True(side > 0.5f, $"Nachbar: {side}");
            // Nach dem Glätten ist der Nachbar nicht höher als die frühere Spitze
            Assert.True(side <= peak + 0.001f);
        }

        [Fact]
        public void Level_MovesTowardsTargetHeight_LimitedByStrength()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            var p = new Vector2(-60, 30);
            t.Terraform(Brush(TerraformMode.Level, p, 20f, 1f, target: 5f));
            Assert.Equal(1f, t.HeightAt(p), 3);   // Stärke begrenzt den Schritt
            for (int i = 0; i < 10; i++) t.Terraform(Brush(TerraformMode.Level, p, 20f, 1f, target: 5f));
            Assert.Equal(5f, t.HeightAt(p), 3);   // nicht darüber hinaus
            Assert.Equal(5f, t.HeightAt(p + new Vector2(12, 0)), 3);

            // Absenken auf Zielhöhe
            for (int i = 0; i < 10; i++) t.Terraform(Brush(TerraformMode.Level, p, 20f, 2f, target: -1f));
            Assert.Equal(-1f, t.HeightAt(p), 3);
        }

        [Fact]
        public void TerrainUnderRoad_IsLocked_AndFadesOutSoftly()
        {
            var w = FlatWorld();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var t = w.Terrain;
            float under = t.HeightAt(new Vector2(100, 0));

            var r = t.Terraform(Brush(TerraformMode.Raise, new Vector2(100, 0), 40f, 5f));
            Assert.True(r.Changed);
            Assert.True(r.LockedPoints > 0);
            Assert.Equal(under, t.HeightAt(new Vector2(100, 0)), 3);
            float hw = RoadCatalog.CityTwoLane.HalfWidth;
            // nie über die Fahrbahn (Gitterpunkte bis eine Zelle daneben sind gesperrt)
            for (float y = -hw; y <= hw; y += 0.5f)
                Assert.True(t.HeightAt(new Vector2(100, y)) <= -TerrainMap.UnderRoad + 0.01f, $"y={y}: {t.HeightAt(new Vector2(100, y))}");
            // Weiter weg voll angehoben, dazwischen ansteigend
            float far = t.HeightAt(new Vector2(100, 36));
            float mid = t.HeightAt(new Vector2(100, hw + t.RoadMargin + TerrainLocks.Band * 0.5f));
            Assert.Equal(5f, far, 2);
            Assert.True(mid > 0.1f && mid < 4.9f, $"Übergang: {mid}");

            // Auch Absenken/Einebnen ändern die Straße nicht
            t.Terraform(Brush(TerraformMode.Lower, new Vector2(100, 0), 40f, 20f));
            t.Terraform(Brush(TerraformMode.Level, new Vector2(100, 0), 40f, 20f, target: 10f));
            Assert.Equal(under, t.HeightAt(new Vector2(100, 0)), 3);
        }

        [Fact]
        public void TerrainUnderBuildings_IsLocked()
        {
            var w = FlatWorld();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(0, 20), 200f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400; i++) w.Step();
            var b = w.City.Buildings.First();
            var t = w.Terrain;
            var corners = new[]
            {
                b.Position,
                b.Position + b.Side * (b.Width * 0.45f) + b.Front * (b.Depth * 0.45f),
                b.Position - b.Side * (b.Width * 0.45f) - b.Front * (b.Depth * 0.45f),
            };
            var before = corners.Select(t.HeightAt).ToArray();

            var away = b.Position - b.Front * (b.Depth * 0.5f + 30f);
            var r = t.Terraform(Brush(TerraformMode.Raise, b.Position - b.Front * 10f, 60f, 4f));
            Assert.True(r.Changed);
            for (int i = 0; i < corners.Length; i++)
                Assert.Equal(before[i], t.HeightAt(corners[i]), 3);
            // Hinter dem Grundstück (sofern dort kein anderes Gebäude steht) darf es sich ändern
            if (!w.City.Buildings.Any(o => o.Contains(away, TerrainLocks.Band + t.CellSize)))
                Assert.True(t.HeightAt(away) > 1f);
        }

        [Fact]
        public void Changed_ReportsDirtyRectangle_OfBrushOnly()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            Vector2 evMin = default, evMax = default;
            int events = 0;
            t.Changed += (a, b) => { evMin = a; evMax = b; events++; };
            int version = t.Version;

            var c = new Vector2(120, -80);
            var r = t.Terraform(Brush(TerraformMode.Raise, c, 20f, 1f));
            Assert.Equal(1, events);
            Assert.Equal(version + 1, t.Version);
            Assert.Equal(r.Min, evMin);
            Assert.Equal(r.Max, evMax);
            Assert.True(evMin.X <= c.X - 16f && evMin.Y <= c.Y - 16f);
            Assert.True(evMax.X >= c.X + 16f && evMax.Y >= c.Y + 16f);
            Assert.True(evMin.X >= c.X - 20f - 2f * t.CellSize && evMax.X <= c.X + 20f + 2f * t.CellSize);

            // Ohne Wirkung (Zielhöhe schon erreicht, Stärke 0) kein Ereignis
            t.Terraform(Brush(TerraformMode.Level, new Vector2(-300, -300), 20f, 1f, target: 0f));
            t.Terraform(Brush(TerraformMode.Raise, c, 20f, 0f));
            Assert.Equal(1, events);
        }

        [Fact]
        public void Terraform_MarksGroundAsWorked_AndKeepsRoadsAtGrade()
        {
            var w = FlatWorld();
            var t = w.Terrain;
            int x = (int)((0f - t.Origin.X) / t.CellSize), y = (int)((0f - t.Origin.Y) / t.CellSize);
            Assert.Equal(0f, t.WorkedAt(x, y));
            t.Terraform(Brush(TerraformMode.Raise, Vector2.Zero, 12f, 3f));
            Assert.True(t.WorkedAt(x, y) > 0.5f);
            // Natürliche Höhe zieht mit → eine neue Straße auf dem Hügel gilt als ebenerdig und formt das Gelände
            Assert.Equal(3f, t.NaturalHeightAt(Vector2.Zero), 3);
        }
    }
}
