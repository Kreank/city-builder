using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Eigene Assets als Spielfunktion: Freizeitanlagen (Freizeit/Gesundheit), Wahrzeichen (Bodenwert, Stolz, Rathaus
    /// einmalig, Attraktivität), Lärmschutzwände (dämpfen Lärm dahinter, nicht davor), Speichern/Laden + Determinismus.
    /// </summary>
    public class OwnAssetsTests
    {
        readonly ITestOutputHelper output;
        public OwnAssetsTests(ITestOutputHelper output) => this.output = output;

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType type = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 0f, null, b, 0f, type ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        /// <summary>Kleine Wohnstadt nördlich einer Hauptstraße (Süden bleibt frei für Anlagen).</summary>
        static SimulationWorld Town(int seed = 2024)
        {
            var w = SimulationWorld.CreateDefault(null, seed);
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-100, 45), 50f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, 45), 40f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 2);
            Assert.True(w.Citizens.Stats.Population > 10, $"nur {w.Citizens.Stats.Population} Einwohner");
            return w;
        }

        static Citizen[] Residents(SimulationWorld w) =>
            w.Citizens.Citizens.Where(c => c.Household != null && w.City.GetBuilding(c.Household.HomeId) != null).OrderBy(c => c.Id).ToArray();

        static float Average(SimulationWorld w, NeedKind k) => Residents(w).Average(c => c.Need(k));

        // ------------------------------------------------------------ Katalog

        [Fact]
        public void Catalog_LeisureAndLandmarks_AreServicesWithSensibleValues()
        {
            var leisure = ServiceCatalog.All.Where(t => t.Kind == ServiceKinds.Leisure).ToArray();
            var landmarks = ServiceCatalog.All.Where(t => t.Kind == ServiceKinds.Landmark).ToArray();
            Assert.Equal(6, leisure.Length);
            Assert.Equal(9, landmarks.Length);
            Assert.Single(ServiceCatalog.All, t => t.Unique);
            Assert.True(ServiceCatalog.TownHall.Unique && ServiceCatalog.TownHall.NeedsRoadFront);
            Assert.True(ServiceCatalog.Stadium.NeedsRoadFront && !ServiceCatalog.Playground.NeedsRoadFront);
            Assert.Equal(ServiceKinds.Sport, ServiceCatalog.SportsField.ExtraChannel);
            Assert.True(ServiceCatalog.Stadium.NoiseLevel > ServiceCatalog.SportsField.NoiseLevel);
            Assert.All(landmarks, t => Assert.True(t.Attractiveness > 0f));
            Assert.Contains(ServiceKinds.Leisure, ServiceKinds.All);
            Assert.Contains(ServiceKinds.Landmark, ServiceKinds.All);
        }

        // ------------------------------------------------------------ Freizeit

        [Fact]
        public void Leisure_CoverageRaisesLeisureNeed_SportRaisesHealth()
        {
            var a = Town();
            var b = Town();
            Assert.Equal(Average(a, NeedKind.Leisure), Average(b, NeedKind.Leisure)); // gleiche Eingaben → gleiche Welt

            // Sportplatz (Freizeit + Sport) südlich der Straße, Fußweg-Radius 600 m – kein Gebäude muss weichen
            var field = a.Services.TryBuild(ServiceCatalog.SportsField, new Vector2(-100, -90));
            Assert.NotNull(field);
            a.Services.ForceTick();
            var homes = a.City.Buildings.Where(x => x.IsConstructed && x.Residents > 0).ToArray();
            Assert.NotEmpty(homes);
            Assert.All(homes, h => Assert.True(a.Services.Coverage(ServiceKinds.Leisure, h) > 0.5f, $"{h} ohne Freizeit-Abdeckung"));
            Assert.All(homes, h => Assert.True(a.Services.Coverage(ServiceKinds.Sport, h) > 0.5f));
            Assert.True(a.Citizens.LeisureCoverage(homes[0]) > 0.5f);
            Assert.Equal(0f, b.Citizens.LeisureCoverage(b.City.GetBuilding(homes[0].Id))); // ohne Anlage: kein Bonus

            Run(a, SimulationClock.TicksPerDay * 2);
            Run(b, SimulationClock.TicksPerDay * 2);
            float la = Average(a, NeedKind.Leisure), lb = Average(b, NeedKind.Leisure);
            float ha = Average(a, NeedKind.Health), hb = Average(b, NeedKind.Health);
            output.WriteLine($"Freizeit mit Sportplatz {la:0.000}, ohne {lb:0.000} · Gesundheit {ha:0.000} / {hb:0.000}");
            Assert.True(la > lb + 0.03f, $"Freizeit {la} nicht über {lb}");
            Assert.True(ha > hb, $"Gesundheit {ha} nicht über {hb}");
        }

        [Fact]
        public void Leisure_DriveRangeNeedsRoadFront_StadiumIsLoud()
        {
            var w = Town();
            var s = w.Services;
            Assert.True(s.Plan(ServiceCatalog.Stadium, new Vector2(0, -300)) is var nr1 && nr1.IsValid && nr1.MissingRoad); // Frei setzen: gültig, ohne Zufahrt
            var plan = s.PlanAligned(ServiceCatalog.Stadium, new Vector2(150, -45));
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.NotNull(s.Build(plan));
            s.ForceTick();
            var near = plan.Position;
            Assert.True(w.Environment.Noise.Sample(near) >= ServiceCatalog.Stadium.NoiseLevel * 0.9f);
            var home = w.City.Buildings.First(x => x.IsConstructed && x.Residents > 0);
            Assert.True(s.Coverage(ServiceKinds.Leisure, home) > 0f, "Stadion erreicht die Häuser über die Straße");
            Assert.Equal(0f, s.Coverage(ServiceKinds.Sport, home)); // Zuschauer treiben keinen Sport
        }

        // ------------------------------------------------------------ Wahrzeichen

        [Fact]
        public void Landmarks_RaiseLandValueAround_AndPride()
        {
            var a = Town();
            var b = Town();
            var castle = a.Services.TryBuild(ServiceCatalog.Castle, new Vector2(-100, -120));
            Assert.NotNull(castle);
            a.Services.ForceTick();
            a.LandValue.ForceRecalculate();
            b.LandValue.ForceRecalculate();
            var p = new Vector2(-100, 30);
            float va = a.LandValue.ValueAt(p), vb = b.LandValue.ValueAt(p);
            output.WriteLine($"Bodenwert mit Burg {va:0.000}, ohne {vb:0.000}, Beitrag {a.LandValue.Explain(p).Landmark:0.000}");
            Assert.True(va > vb + 0.05f, $"{va} nicht deutlich über {vb}");
            Assert.True(a.LandValue.Explain(p).Landmark > 0.05f);
            Assert.Contains(a.LandValue.Explain(p).Contributions(), c => c.label == "Wahrzeichen");
            // weit weg wirkt es nicht
            var far = new Vector2(900, 900);
            Assert.Equal(0f, a.LandValue.Explain(far).Landmark);

            var home = a.City.Buildings.First(x => x.IsConstructed && x.Residents > 0);
            Assert.True(a.Citizens.LandmarkPride(home) > 0.5f);
            Assert.Equal(ServiceCatalog.Castle.Attractiveness, a.Services.Attractiveness);
            Assert.Equal(0f, b.Services.Attractiveness);

            // Stolz hebt die Zufriedenheit
            Run(a, SimulationClock.TicksPerDay);
            Run(b, SimulationClock.TicksPerDay);
            float sa = Residents(a).Average(c => c.Satisfaction), sb = Residents(b).Average(c => c.Satisfaction);
            output.WriteLine($"Zufriedenheit mit Burg {sa:0.000}, ohne {sb:0.000}");
            Assert.True(sa > sb, $"{sa} nicht über {sb}");
        }

        [Fact]
        public void TownHall_IsUnique_FlagAndRoadFront()
        {
            var w = Town();
            var s = w.Services;
            Assert.False(s.HasTownHall);
            Assert.True(s.Plan(ServiceCatalog.TownHall, new Vector2(0, -300)) is var nr2 && nr2.IsValid && nr2.MissingRoad); // Frei setzen: gültig, ohne Zufahrt
            var plan = s.PlanAligned(ServiceCatalog.TownHall, new Vector2(-150, -30));
            Assert.True(plan.IsValid, plan.Error.ToString());
            var hall = s.Build(plan);
            Assert.NotNull(hall);
            Assert.True(s.HasTownHall);

            var second = s.PlanAligned(ServiceCatalog.TownHall, new Vector2(150, -30));
            Assert.Equal(ServiceBuildError.AlreadyBuilt, second.Error);
            Assert.Null(s.Build(second));
            // Verschieben bleibt erlaubt (gleiches Gebäude)
            var move = s.PlanMove(hall.Id, new Vector2(hall.Position.X + 60f, hall.Position.Y), hall.Front);
            Assert.NotEqual(ServiceBuildError.AlreadyBuilt, move.Error);
            // Andere Wahrzeichen sind frei
            Assert.NotNull(s.TryBuild(ServiceCatalog.Statue, new Vector2(0, -200)));
            Assert.NotNull(s.TryBuild(ServiceCatalog.Statue, new Vector2(40, -200)));

            Assert.True(s.Demolish(hall.Id));
            Assert.False(s.HasTownHall);
            Assert.True(s.PlanAligned(ServiceCatalog.TownHall, new Vector2(150, -30)).IsValid);
        }

        // ------------------------------------------------------------ Lärmschutzwand

        static SimulationWorld NoisyRoad(bool barrier, out NoiseBarrier built)
        {
            var w = SimulationWorld.CreateDefault(null, 7);
            Road(w, new Vector2(-400, 0), new Vector2(400, 0), RoadCatalog.CityFourLane);
            built = null;
            if (barrier)
            {
                Assert.True(w.NoiseBarriers.SnapToRoad(new Vector2(-190, 20), out var a, out _));
                Assert.True(w.NoiseBarriers.SnapToRoad(new Vector2(190, 20), out var b, out _));
                Assert.Equal(RoadCatalog.CityFourLane.HalfWidth + NoiseBarrierSystem.RoadGap, a.Y, 3);
                built = w.NoiseBarriers.TryBuild(a, b);
                Assert.NotNull(built);
            }
            w.GetSystem<NoiseSystem>().Tick(w);
            return w;
        }

        [Fact]
        public void NoiseBarrier_DampensNoiseBehind_NotInFront()
        {
            var with = NoisyRoad(true, out var wall);
            var without = NoisyRoad(false, out _);
            var noiseWith = with.Environment.Noise;
            var noiseWithout = without.Environment.Noise;

            var behind = new Vector2(0, 40);   // nördlich, hinter der Wand
            var front = new Vector2(0, -40);   // südlich, Straßenseite ohne Wand
            float bW = noiseWith.Sample(behind), bO = noiseWithout.Sample(behind);
            float fW = noiseWith.Sample(front), fO = noiseWithout.Sample(front);
            output.WriteLine($"hinter der Wand {bW:0.000} (ohne {bO:0.000}), davor {fW:0.000} (ohne {fO:0.000})");
            Assert.True(bO > 0.1f, "ohne Wand muss es dort laut sein");
            Assert.True(bW < bO * 0.6f, $"Wand dämpft zu wenig: {bW} vs {bO}");
            Assert.Equal(fO, fW); // davor bitgleich unverändert
            // Neben dem Wandende (seitlich, weit weg) kein Schatten
            var aside = new Vector2(380, 40);
            Assert.True(noiseWith.Sample(aside) > noiseWithout.Sample(aside) * 0.9f);
            Assert.True(with.NoiseBarriers.Attenuation(new Vector2(0, 0), behind) < 1f);
            Assert.Equal(1f, with.NoiseBarriers.Attenuation(new Vector2(0, 0), front));
        }

        [Fact]
        public void NoiseBarrier_Rules_RoadBlockCostAndLots()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false, StartingFunds = 100000 }, 7);
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            var nb = w.NoiseBarriers;
            Assert.Equal(NoiseBarrierError.OnRoad, nb.Plan(new Vector2(-50, 0), new Vector2(50, 0)).Error);
            Assert.Equal(NoiseBarrierError.TooShort, nb.Plan(new Vector2(0, 20), new Vector2(1, 20)).Error);
            double before = w.Economy.Balance;
            var wall = nb.TryBuild(new Vector2(-50, 30), new Vector2(50, 30));
            Assert.NotNull(wall);
            Assert.Equal(before - NoiseBarrierSystem.CostOf(100f), w.Economy.Balance, 3);
            Assert.True(nb.WeeklyUpkeep() > 0);
            // Straße quer durch die Wand ist gesperrt (Bauwerks-Sperre)
            var cross = RoadBuilder.Plan(w.Roads, new Vector2(0, 0), 0f, null, new Vector2(0, 120), 0f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.OverlapsStructure, cross.Error);
            // Auf Wandzellen wächst nichts
            Assert.True(w.City.LotBlocker(new Vector2(0, 31)));
            Assert.True(nb.Demolish(wall.Id));
            Assert.False(w.City.LotBlocker(new Vector2(0, 31)));
            Assert.True(RoadBuilder.Plan(w.Roads, new Vector2(0, 0), 0f, null, new Vector2(0, 120), 0f, RoadCatalog.CityTwoLane).IsValid);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_NewStructures_Deterministic()
        {
            var a = Town(99);
            Assert.NotNull(a.Services.TryBuild(ServiceCatalog.SportsField, new Vector2(-100, -90)));
            Assert.NotNull(a.Services.TryBuild(ServiceCatalog.Fountain, new Vector2(60, -60)));
            Assert.NotNull(a.Services.Build(a.Services.PlanAligned(ServiceCatalog.TownHall, new Vector2(200, -30))));
            Assert.NotNull(a.NoiseBarriers.TryBuild(new Vector2(-250, -14), new Vector2(-150, -14)));
            Run(a, 77);

            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.Equal(a.Services.FacilityCount, b.Services.FacilityCount);
            Assert.True(b.Services.HasTownHall);
            Assert.Equal(a.Services.Attractiveness, b.Services.Attractiveness);
            Assert.Equal(1, b.NoiseBarriers.Count);
            Assert.Equal(a.NoiseBarriers.Barriers.Single().A, b.NoiseBarriers.Barriers.Single().A);
            AssertSameState(a, b, "direkt nach dem Laden");

            Run(a, SimulationClock.TicksPerDay);
            Run(b, SimulationClock.TicksPerDay);
            AssertSameState(a, b, "nach einem Tag");
            Assert.Equal(Average(a, NeedKind.Leisure), Average(b, NeedKind.Leisure));
            Assert.Equal(a.Environment.Noise.RawValues.Sum(), b.Environment.Noise.RawValues.Sum());
            // Rathaus bleibt auch nach dem Laden einmalig
            Assert.Equal(ServiceBuildError.AlreadyBuilt, b.Services.PlanAligned(ServiceCatalog.TownHall, new Vector2(-200, -30)).Error);
        }

        static void AssertSameState(SimulationWorld a, SimulationWorld b, string when)
        {
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            Assert.Equal(ha.Select(x => x.Key), hb.Select(x => x.Key));
            Assert.Contains("noisebarriers", ha.Select(x => x.Key));
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, $"{when}: abweichende Abschnitte: {string.Join(", ", diff)}");
        }
    }
}
