using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.World;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Etappe 5: fiktive Erde (Relief, Klima, Biome, Geologie, Regionen, Länder, LOD-Wirtschaft) und Stadtkarte als Ausschnitt.</summary>
    public class WorldTests
    {
        readonly ITestOutputHelper output;
        public WorldTests(ITestOutputHelper output) { this.output = output; }

        /// <summary>Gemeinsame Welt für lesende Tests (nicht verändern!).</summary>
        static readonly Lazy<Planet> Shared = new Lazy<Planet>(() => PlanetGenerator.Generate(2024));
        static Planet P => Shared.Value;

        static ulong Checksum(TerrainMap t)
        {
            ulong h = 1469598103934665603UL;
            for (int y = 0; y < t.Resolution; y++)
            for (int x = 0; x < t.Resolution; x++)
            {
                h ^= (uint)BitConverter.SingleToInt32Bits(t[x, y]);
                h *= 1099511628211UL;
            }
            return h;
        }

        // ------------------------------------------------------------ Bestehendes unverändert

        [Fact]
        public void DefaultWorld_Unchanged_NoPlanet_SameTerrain()
        {
            // Prüfsummen des Geländegenerators vor Etappe 5 (gleiche Laufzeit)
            Assert.Equal(15526461567211439303UL, Checksum(TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 42)));
            Assert.Equal(13049893430464147276UL, Checksum(TerrainMap.Generate(new Vector2(-1024), 2048f, 4f, 4711)));
            Assert.Equal(15526461567211439303UL, Checksum(TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 42, TerrainParams.Default)));

            var s = new WorldSettings();
            Assert.False(s.GenerateWorld);
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            Assert.Null(w.Planet);
            Assert.Null(w.StartRegion);
            Assert.Equal(13049893430464147276UL, Checksum(w.Terrain));
            // Klima und Ressourcen gibt es trotzdem (gemäßigte Standardwerte)
            Assert.NotNull(w.Climate);
            Assert.Equal(ClimateParams.Default.MeanTemperature, w.Climate.MeanTemperature);
            Assert.NotNull(w.Resources);
            foreach (var id in ResourceIds.All) Assert.True(w.Resources.TotalAmount(id) > 0, id);
        }

        // ------------------------------------------------------------ Determinismus, Leistung

        [Fact]
        public void SameSeed_SameWorld_DifferentSeed_DifferentWorld()
        {
            var sw = Stopwatch.StartNew();
            var a = PlanetGenerator.Generate(77);
            double ms = sw.Elapsed.TotalMilliseconds;
            var b = PlanetGenerator.Generate(77);
            var c = PlanetGenerator.Generate(78);
            output.WriteLine($"Welt 360×180 ({a.CellKm:0} km/Zelle): {ms:0} ms (gemessen im Generator: {a.GenerationMilliseconds:0} / {b.GenerationMilliseconds:0} ms), " +
                             $"{a.Regions.Count} Regionen, {a.Countries.Count} Länder");
            Assert.Equal(a.Elevation, b.Elevation);
            Assert.Equal(a.Precipitation, b.Precipitation);
            Assert.Equal(a.Ore, b.Ore);
            Assert.Equal(a.RegionOf, b.RegionOf);
            Assert.Equal(a.Countries.Select(x => x.Name), b.Countries.Select(x => x.Name));
            Assert.Equal(a.Countries.Select(x => x.Economy.Population), b.Countries.Select(x => x.Economy.Population));
            Assert.NotEqual(a.Elevation, c.Elevation);
            Assert.True(ms < 8000, $"Generierung zu langsam: {ms:0} ms");
        }

        [Fact]
        public void Relief_HasContinentsOceansMountains()
        {
            var p = P;
            double land = 0, total = 0;
            for (int i = 0; i < p.CellCount; i++)
            {
                double a = p.CellAreaKm2(p.YOf(i));
                total += a;
                if (p.IsLand(i)) land += a;
            }
            Assert.InRange(land / total, 0.27, 0.33);
            Assert.True(p.Elevation.Max() > 4000f);
            Assert.True(p.Elevation.Min() < -4000f);
            Assert.Contains(Biome.Mountain, p.BiomeOf);
            Assert.Contains(Biome.Shelf, p.BiomeOf);
            // Gebirge an Plattengrenzen: Hochgebirgszellen haben im Mittel deutlich mehr Gebirgsbildung als Tiefland
            float oroHigh = Mean(p, i => p.Elevation[i] > 2500f, i => p.Orogeny[i]);
            float oroLow = Mean(p, i => p.IsLand(i) && p.Elevation[i] < 300f, i => p.Orogeny[i]);
            output.WriteLine($"Orogenese Hochgebirge {oroHigh:0.00}, Tiefland {oroLow:0.00}");
            Assert.True(oroHigh > oroLow * 2f);
            // Alle Biome der Hauptklimazonen kommen vor
            foreach (var b in new[] { Biome.Ice, Biome.Tundra, Biome.Taiga, Biome.TemperateForest, Biome.Grassland, Biome.Desert, Biome.Savanna, Biome.TropicalRainforest })
                Assert.Contains(b, p.BiomeOf);
        }

        static float Mean(Planet p, Func<int, bool> where, Func<int, float> value)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < p.CellCount; i++) if (where(i)) { sum += value(i); n++; }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        // ------------------------------------------------------------ Klima

        [Fact]
        public void Climate_PolesColder_MountainsColder()
        {
            var p = P;
            float tropics = Mean(p, i => Math.Abs(p.LatitudeOf(i)) < 15f, i => p.Temperature[i]);
            float poles = Mean(p, i => Math.Abs(p.LatitudeOf(i)) > 65f, i => p.Temperature[i]);
            output.WriteLine($"Tropen {tropics:0.0} °C, Pole {poles:0.0} °C");
            Assert.True(tropics > 22f && poles < -5f);
            // Höhe: in denselben Breitenbändern sind Gebirgszellen kälter als Tiefland
            int pairs = 0, colder = 0;
            for (int y = 0; y < p.Height; y++)
            {
                float hi = 0, lo = 0; int nh = 0, nl = 0;
                for (int x = 0; x < p.Width; x++)
                {
                    int i = p.Index(x, y);
                    if (p.Elevation[i] > 2000f) { hi += p.Temperature[i]; nh++; }
                    else if (p.IsLand(i) && p.Elevation[i] < 300f) { lo += p.Temperature[i]; nl++; }
                }
                if (nh < 2 || nl < 2) continue;
                pairs++;
                if (hi / nh < lo / nl - 5f) colder++;
            }
            Assert.True(pairs > 10);
            Assert.True(colder >= pairs * 0.9, $"{colder}/{pairs} Breiten mit kälterem Gebirge");
        }

        [Fact]
        public void Precipitation_RainShadow_Synthetic()
        {
            // Ozean mit einem Landstreifen (Spalten 100..200) und Gebirgskamm bei 150
            int w = 360, h = 180;
            var elev = new float[w * h];
            var temp = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                elev[i] = x >= 100 && x <= 200 ? 200f + 3000f * MathF.Max(0f, 1f - MathF.Abs(x - 150) / 6f) : -3000f;
                temp[i] = 15f;
            }
            var rain = PlanetGenerator.ComputePrecipitation(w, h, elev, temp);
            int row45 = (int)((90f - 45f) / 180f * h); // Westwind (weht nach Osten, +x)
            int row15 = (int)((90f - 15f) / 180f * h); // Passat (weht nach Westen, −x)
            float wind45 = rain[row45 * w + 140], lee45 = rain[row45 * w + 160];
            float wind15 = rain[row15 * w + 160], lee15 = rain[row15 * w + 140];
            output.WriteLine($"45°: Luv {wind45:0} mm, Lee {lee45:0} mm · 15°: Luv {wind15:0} mm, Lee {lee15:0} mm");
            Assert.True(wind45 > lee45 * 1.8f);
            Assert.True(wind15 > lee15 * 1.8f);
            // Küste nasser als tiefes Inland hinter dem Gebirge
            Assert.True(rain[row45 * w + 102] > rain[row45 * w + 195]);
        }

        [Fact]
        public void Precipitation_RainShadow_OnGeneratedWorld()
        {
            // Hochgebirgszellen: 2 Zellen im Luv (gegen den Wind) nasser als 2 Zellen im Lee
            var p = P;
            int total = 0, windward = 0;
            for (int i = 0; i < p.CellCount; i++)
            {
                if (p.Elevation[i] < 1200f) continue;
                int dir = PlanetGenerator.WindDirection(p.LatitudeOf(i));
                int up = p.Neighbor(i, -2 * dir, 0), down = p.Neighbor(i, 2 * dir, 0);
                if (up < 0 || down < 0 || !p.IsLand(up) || !p.IsLand(down)) continue;
                if (p.Elevation[up] > p.Elevation[i] - 300f || p.Elevation[down] > p.Elevation[i] - 300f) continue; // nur Kämme
                total++;
                if (p.Precipitation[up] > p.Precipitation[down]) windward++;
            }
            output.WriteLine($"Kämme: {windward}/{total} mit nasserer Luvseite");
            Assert.True(total > 20);
            Assert.True(windward > total * 0.65);
        }

        [Fact]
        public void LocalClimate_SeasonsWeatherAndFactors()
        {
            var north = new ClimateModel(new ClimateParams { MeanTemperature = 9f, Amplitude = 10f, AnnualPrecipitation = 700f, Latitude = 50f }, 5);
            var south = new ClimateModel(new ClimateParams { MeanTemperature = 9f, Amplitude = 10f, AnnualPrecipitation = 700f, Latitude = -40f }, 5);
            Assert.Equal(Season.Winter, north.SeasonOn(15));
            Assert.Equal(Season.Summer, north.SeasonOn(195));
            Assert.Equal(Season.Summer, south.SeasonOn(15));
            Assert.Equal(Season.Spring, north.SeasonOn(100));
            Assert.Equal(Season.Winter, north.SeasonOn(360 + 15));
            float jan = Enumerable.Range(0, 30).Average(d => north.TemperatureOn(d));
            float jul = Enumerable.Range(180, 30).Average(d => north.TemperatureOn(d));
            output.WriteLine($"Nord: Januar {jan:0.0} °C, Juli {jul:0.0} °C");
            Assert.True(jul > jan + 12f);
            Assert.True(Enumerable.Range(0, 30).Average(d => south.TemperatureOn(d)) > Enumerable.Range(180, 30).Average(d => south.TemperatureOn(d)) + 12f);
            // Ernte im Sommer, Heizen im Winter
            Assert.True(Enumerable.Range(180, 30).Average(d => north.HarvestFactor(d)) > 0.6f);
            Assert.True(Enumerable.Range(0, 30).Average(d => north.HarvestFactor(d)) < 0.2f);
            Assert.True(Enumerable.Range(0, 30).Average(d => north.HeatingFactor(d)) > Enumerable.Range(180, 30).Average(d => north.HeatingFactor(d)) + 0.3f);
            // Niederschlag: Jahressumme ≈ Vorgabe, nasse und trockene Tage, deterministisch
            float year = Enumerable.Range(0, 360).Sum(d => north.PrecipitationOn(d));
            int wet = Enumerable.Range(0, 360).Count(d => north.PrecipitationOn(d) > 0f);
            output.WriteLine($"Niederschlag {year:0} mm an {wet} Tagen");
            Assert.InRange(year, 450f, 1000f);
            Assert.InRange(wet, 60, 250);
            var again = new ClimateModel(north.Parameters, 5);
            Assert.Equal(north.TemperatureOn(123), again.TemperatureOn(123));
            Assert.Equal(north.PrecipitationOn(77), again.PrecipitationOn(77));
            // Wüste: kaum Ernte; Tropen: kein Heizen
            var desert = new ClimateModel(new ClimateParams { MeanTemperature = 25f, Amplitude = 8f, AnnualPrecipitation = 80f, Latitude = 22f }, 1);
            var tropic = new ClimateModel(new ClimateParams { MeanTemperature = 26f, Amplitude = 1.5f, AnnualPrecipitation = 2200f, Latitude = 3f }, 1);
            Assert.True(desert.AnnualHarvestFactor < tropic.AnnualHarvestFactor * 0.6f);
            Assert.True(Enumerable.Range(0, 360).Average(d => tropic.HeatingFactor(d)) < 0.02f);
        }

        // ------------------------------------------------------------ Geologie

        [Fact]
        public void Resources_GeologicallyPlausible()
        {
            var p = P;
            float oreMountain = Mean(p, i => p.IsLand(i) && p.Orogeny[i] > 0.4f, i => p.Ore[i]);
            float oreLowland = Mean(p, i => p.IsLand(i) && p.Orogeny[i] < 0.05f && p.Elevation[i] < 400f, i => p.Ore[i]);
            float coalBasin = Mean(p, i => p.IsLand(i) && p.Basin[i] > 0.6f, i => p.Coal[i]);
            float coalMountain = Mean(p, i => p.Elevation[i] > 2000f, i => p.Coal[i]);
            float oilBasin = Mean(p, i => p.IsLand(i) && p.Basin[i] > 0.6f, i => p.Oil[i]);
            float oilShelf = Mean(p, i => p.BiomeOf[i] == Biome.Shelf, i => p.Oil[i]);
            float oilDeep = Mean(p, i => p.BiomeOf[i] == Biome.Ocean, i => p.Oil[i]);
            float oilMountain = Mean(p, i => p.Elevation[i] > 2000f, i => p.Oil[i]);
            float sandRiver = Mean(p, i => p.IsRiver(i), i => p.Sand[i]);
            float sandDesert = Mean(p, i => p.BiomeOf[i] == Biome.Desert, i => p.Sand[i]);
            float sandMountain = Mean(p, i => p.BiomeOf[i] == Biome.Mountain, i => p.Sand[i]);
            float gwWet = Mean(p, i => p.IsLand(i) && p.Precipitation[i] > 1200f, i => p.Groundwater[i]);
            float gwDry = Mean(p, i => p.BiomeOf[i] == Biome.Desert, i => p.Groundwater[i]);
            float fertGrass = Mean(p, i => p.BiomeOf[i] == Biome.Grassland || p.BiomeOf[i] == Biome.TemperateForest, i => p.Fertility[i]);
            float fertDesert = Mean(p, i => p.BiomeOf[i] == Biome.Desert || p.BiomeOf[i] == Biome.Tundra || p.BiomeOf[i] == Biome.Ice, i => p.Fertility[i]);
            float forestRain = Mean(p, i => p.BiomeOf[i] == Biome.TropicalRainforest, i => p.Forest[i]);
            float forestDesert = Mean(p, i => p.BiomeOf[i] == Biome.Desert, i => p.Forest[i]);
            output.WriteLine($"Erz Gebirge {oreMountain:0.000} / Tiefland {oreLowland:0.000} · Kohle Becken {coalBasin:0.000} / Gebirge {coalMountain:0.000} · " +
                             $"Öl Becken {oilBasin:0.000} Schelf {oilShelf:0.000} Tiefsee {oilDeep:0.000} Gebirge {oilMountain:0.000} · Sand Fluss {sandRiver:0.00} Wüste {sandDesert:0.00} Gebirge {sandMountain:0.00} · " +
                             $"Grundwasser nass {gwWet:0.00} / Wüste {gwDry:0.00} · Boden {fertGrass:0.00} / {fertDesert:0.00} · Wald {forestRain:0.00} / {forestDesert:0.00}");
            Assert.True(oreMountain > oreLowland * 2f);
            Assert.True(coalBasin > coalMountain * 2f);
            Assert.True(oilBasin > oilMountain * 2f);
            Assert.True(oilShelf > 0.01f);
            Assert.Equal(0f, oilDeep);
            Assert.True(sandRiver > sandMountain && sandDesert > sandMountain);
            Assert.True(gwWet > gwDry * 1.5f);
            Assert.True(fertGrass > fertDesert * 3f);
            Assert.True(forestRain > 0.6f && forestDesert < 0.05f);
            // Werte liegen in 0..1
            foreach (var arr in new[] { p.Fertility, p.Forest, p.Ore, p.Coal, p.Oil, p.Sand, p.Groundwater })
                Assert.All(arr, v => Assert.InRange(v, 0f, 1f));
        }

        // ------------------------------------------------------------ Regionen und Länder

        [Fact]
        public void Countries_AndFreeLand_PartitionLand_AndHaveProfiles()
        {
            var p = P;
            Assert.True(p.Regions.Count >= 150, $"{p.Regions.Count} Regionen");
            Assert.InRange(p.Countries.Count, 20, 40);
            for (int i = 0; i < p.CellCount; i++)
            {
                if (!p.IsLand(i)) { Assert.Equal(-1, p.RegionOf[i]); continue; }
                Assert.InRange(p.RegionOf[i], 0, p.Regions.Count - 1);
                // Spielstart: Land gehört einem Land oder ist frei (unbesiedelt)
                var region = p.Regions[p.RegionOf[i]];
                Assert.Equal(region.IsFree, p.CountryOfCell(i) == null);
            }
            // jede besiedelte Region gehört genau einem Land, Zellen addieren sich zur Landfläche ohne freies Land
            var seen = new HashSet<int>();
            foreach (var c in p.Countries)
                foreach (int r in c.Regions) { Assert.True(seen.Add(r)); Assert.Equal(c.Id, p.Regions[r].CountryId); Assert.Same(c, p.Regions[r].Country); }
            Assert.Equal(p.Regions.Count - p.FreeRegionCount, seen.Count);
            Assert.All(p.Regions.Where(r => r.IsFree), r => Assert.Null(r.Country));
            Assert.InRange(p.FreeRegionCount / (float)p.Regions.Count, 0.25f, 0.4f);
            Assert.Equal(p.Regions.Where(r => !r.IsFree).Sum(r => r.Cells.Count), p.Countries.Sum(c => c.LandCells));

            foreach (var c in p.Countries)
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
                Assert.False(string.IsNullOrWhiteSpace(c.CapitalName));
                Assert.Contains(c.CapitalRegionId, c.Regions);
                Assert.Equal(c.CapitalRegionId, p.RegionOf[c.CapitalCell]);
                for (int k = 0; k < CountryProfile.Count; k++)
                {
                    Assert.InRange(c.Profile.Raw[k], 0f, 1f);
                    Assert.InRange(c.Profile.Rank[k], 0f, 1f);
                }
                Assert.NotEqual(c.Profile.Primary, c.Profile.Secondary);
                Assert.True(c.Profile.Comparative[(int)c.Profile.Primary] >= 1f);
                Assert.True(c.Economy.Population > 0);
            }
            Assert.Equal(p.Countries.Count, p.Countries.Select(c => c.Name).Distinct().Count());
            // Spezialisierung: mindestens 4 verschiedene Hauptstärken
            int distinct = p.Countries.Select(c => c.Profile.Primary).Distinct().Count();
            output.WriteLine("Hauptstärken: " + string.Join(", ", p.Countries.GroupBy(c => c.Profile.Primary).Select(g => $"{CountryProfile.DisplayName(g.Key)} {g.Count()}")));
            Assert.True(distinct >= 4);
            // Profile folgen der Natur: Rohstoff-Rang korreliert mit Bodenschätzen
            var top = p.Countries.OrderByDescending(c => c.Profile[Strength.RawMaterials]).Take(5).ToList();
            var bottom = p.Countries.OrderBy(c => c.Profile[Strength.RawMaterials]).Take(5).ToList();
            float Res(Country c) => c.Regions.Sum(r => (p.Regions[r].Ore + p.Regions[r].Coal + p.Regions[r].Oil) * p.Regions[r].AreaKm2) / Math.Max(1f, c.AreaKm2);
            Assert.True(top.Average(Res) > bottom.Average(Res));
        }

        [Fact]
        public void StartRegion_SetsPlayerCountry()
        {
            // Altes Modell (Spielstände vor „Spielstart & Startregion“): alles Land gehört NPC-Ländern, Spieler-Land = Land der Startregion
            var p = PlanetGenerator.Generate(31, new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16, FreeLandShare = 0f });
            Assert.Equal(0, p.FreeRegionCount);
            Assert.Null(p.PlayerCountry);
            int start = p.SuggestStartRegion();
            var r = p.Regions[start];
            Assert.True(r.HasRiver || r.IsCoastal);
            Assert.InRange(r.MeanTemperature, -2f, 24f);
            p.SetStartRegion(start);
            Assert.Equal(r.CountryId, p.PlayerCountryId);
            Assert.True(p.PlayerCountry.IsPlayer);
            Assert.False(p.PlayerCountry.IsCityState);
            Assert.Equal(1, p.Countries.Count(c => c.IsPlayer));
            int other = p.Regions.First(x => x.CountryId != r.CountryId).Id;
            p.SetStartRegion(other);
            Assert.Equal(1, p.Countries.Count(c => c.IsPlayer));
            Assert.False(p.Countries[r.CountryId].IsPlayer);
        }

        [Fact]
        public void StartRegion_FreeRegion_PlayerBecomesCityState()
        {
            var p = PlanetGenerator.Generate(31, new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 });
            int npcs = p.Countries.Count;
            Assert.True(p.FreeRegionCount > 0);
            int start = p.SuggestStartRegion();
            var r = p.Regions[start];
            Assert.True(r.IsFree);
            Assert.True(r.HasRiver || r.IsCoastal);
            p.SetStartRegion(start);
            var me = p.PlayerCountry;
            Assert.NotNull(me);
            Assert.True(me.IsPlayer && me.IsCityState);
            Assert.Equal(npcs, me.Id);                       // hinten angehängt, NPC-Ids bleiben
            Assert.Equal(npcs + 1, p.Countries.Count);
            Assert.Equal(new[] { start }, me.Regions);
            Assert.Same(me, r.Country);
            Assert.Equal(PlayerRealm.CityStateName(null), me.Name);
            Assert.Equal(start, p.RegionOf[me.CapitalCell]);
            Assert.NotNull(me.Economy);
            Assert.Same(me.Economy, p.Economy.Nations[me.Id]);
            Assert.Equal(WorldEconomy.CityStatePopulation, me.Economy.Population);
            Assert.NotEqual(me.Profile.Primary, me.Profile.Secondary);
            Assert.Equal(1, p.Countries.Count(c => c.IsPlayer));

            // verlegen: gleiche Id, alte Region wieder frei
            int other = p.Regions.First(x => x.IsFree && x.Cells.Count >= 4).Id;
            p.SetStartRegion(other);
            Assert.Same(me, p.PlayerCountry);
            Assert.Equal(npcs + 1, p.Countries.Count);
            Assert.True(p.Regions[start].IsFree);
            Assert.Equal(new[] { other }, me.Regions);
            // in ein NPC-Land wechseln geht nicht (Stadtstaat bleibt im freien Land)
            int npcRegion = p.Regions.First(x => !x.IsFree && x.Country != me).Id;
            Assert.Throws<InvalidOperationException>(() => p.SetStartRegion(npcRegion));
        }

        // ------------------------------------------------------------ LOD-Volkswirtschaft

        [Fact]
        public void NationEconomy_TicksDaily_ComparativeAdvantageInPrices()
        {
            var a = PlanetGenerator.Generate(55, new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 });
            var b = PlanetGenerator.Generate(55, new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 });
            for (int d = 0; d < 120; d++) { a.Economy.TickDay(d); b.Economy.TickDay(d); }
            Assert.Equal(120, a.Economy.DaysSimulated);
            Assert.Equal(a.Economy.Nations.Select(n => n.Gdp), b.Economy.Nations.Select(n => n.Gdp));
            foreach (var n in a.Economy.Nations)
            {
                Assert.Equal(119, n.LastDay);
                for (int c = 0; c < GoodsCategories.Count; c++)
                {
                    Assert.True(n.Stock[c] >= 0);
                    Assert.InRange(n.Price[c], GoodsCategories.BasePrice[c] * 0.25 - 1e-9, GoodsCategories.BasePrice[c] * 4 + 1e-9);
                    Assert.True(n.Production[c] >= 0 && n.Demand[c] > 0);
                }
                Assert.True(n.Gdp > 0);
            }
            // Agrarstarke Länder haben billigere Lebensmittel als agrarschwache
            var nations = a.Economy.Nations.OrderBy(n => n.Country.Profile[Strength.Agriculture]).ToList();
            double weak = nations.Take(4).Average(n => n.Price[(int)GoodsCategory.Food]);
            double strong = nations.Skip(nations.Count - 4).Average(n => n.Price[(int)GoodsCategory.Food]);
            output.WriteLine($"Lebensmittelpreis agrarschwach {weak:0.00} €, agrarstark {strong:0.00} €");
            Assert.True(strong < weak);
            Assert.True(a.Economy.WorldPrice(GoodsCategory.Food) > 0);
        }

        // ------------------------------------------------------------ Stadtkarte als Ausschnitt

        static float HeightStdDev(TerrainMap t)
        {
            double sum = 0, sq = 0; int n = 0;
            for (int y = 0; y < t.Resolution; y++)
            for (int x = 0; x < t.Resolution; x++) { float h = t[x, y]; sum += h; sq += h * h; n++; }
            double mean = sum / n;
            return (float)Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        }

        static float WaterShare(TerrainMap t, Func<Vector2, bool> where)
        {
            int n = 0, w = 0;
            for (int y = 0; y < t.Resolution; y++)
            for (int x = 0; x < t.Resolution; x++)
            {
                var p = t.PointAt(x, y);
                if (!where(p)) continue;
                n++;
                if (t[x, y] < t.WaterLevel) w++;
            }
            return n > 0 ? w / (float)n : 0f;
        }

        [Fact]
        public void CityMap_TakesRegionTerrainParameters()
        {
            var p = P;
            var big = p.Regions.Where(r => r.Cells.Count >= 4).ToList();
            var flat = big.OrderBy(r => r.Ruggedness).First();
            var rugged = big.OrderByDescending(r => r.Ruggedness).First();
            var tf = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 1, RegionMapping.TerrainFor(flat, p));
            var tr = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 1, RegionMapping.TerrainFor(rugged, p));
            output.WriteLine($"flach: {flat.Name} ({flat.Ruggedness:0} m) σ {HeightStdDev(tf):0.0} m · zerklüftet: {rugged.Name} ({rugged.Ruggedness:0} m) σ {HeightStdDev(tr):0.0} m");
            Assert.True(HeightStdDev(tr) > HeightStdDev(tf) * 2f);

            // Kein Fluss, keine Küste → kein Wasser
            var dry = big.First(r => !r.HasRiver && !r.IsCoastal);
            var td = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 1, RegionMapping.TerrainFor(dry, p));
            Assert.Equal(0f, WaterShare(td, _ => true));
            // Fluss ohne Küste → Wasser (Flussbett)
            var river = big.First(r => r.HasRiver && !r.IsCoastal);
            Assert.True(WaterShare(TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 1, RegionMapping.TerrainFor(river, p)), _ => true) > 0.01f);
            // Küste: Meer auf der Seite der Region, die ans Meer grenzt, Mitte (Startgebiet) trocken
            var coast = big.First(r => r.IsCoastal && !r.HasRiver);
            var dir = coast.CoastDirection.Value;
            var tc = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 1, RegionMapping.TerrainFor(coast, p));
            float seaSide = WaterShare(tc, q => Vector2.Dot(q, dir) > 900f);
            float landSide = WaterShare(tc, q => Vector2.Dot(q, dir) < -500f);
            float middle = WaterShare(tc, q => q.Length() < 250f);
            output.WriteLine($"Küste {coast.Name} Richtung {dir}: Meerseite {seaSide:P0} Wasser, Landseite {landSide:P0}, Mitte {middle:P0}");
            Assert.True(seaSide > 0.6f);
            Assert.Equal(0f, landSide);
            Assert.Equal(0f, middle);
        }

        [Fact]
        public void CityWorld_FromStartRegion_ClimateResourcesAndCountry()
        {
            var settings = new WorldSettings { GenerateWorld = true, WorldSeed = 2024, GenerateTerrain = true, SizeMeters = 1024f, TerrainCellSize = 8f };
            var w = SimulationWorld.CreateDefault(settings);
            Assert.NotNull(w.Planet);
            var r = w.StartRegion;
            Assert.Equal(w.Planet.SuggestStartRegion(), r.Id);
            Assert.True(w.Planet.PlayerCountry.IsPlayer);
            Assert.Equal(r.MeanTemperature, w.Climate.MeanTemperature);
            Assert.Equal(r.Precipitation, w.Climate.AnnualPrecipitation);
            Assert.Equal(r.Latitude < 0f, w.Climate.SouthernHemisphere);
            Assert.Equal(RegionResources.FromRegion(r).Ore, w.Resources.Source.Ore);
            // gleiche Einstellungen → gleiche Stadtkarte
            var w2 = new SimulationWorld(settings);
            Assert.Equal(Checksum(w.Terrain), Checksum(w2.Terrain));
            Assert.Equal(w.Resources.Layer(ResourceIds.Ore).RawValues, w2.Resources.Layer(ResourceIds.Ore).RawValues);

            // Erzreiche Region → mehr Erz auf der Karte als erzarme
            var p = w.Planet;
            var rich = p.Regions.Where(x => x.Cells.Count >= 4).OrderByDescending(x => x.Ore).First();
            var poor = p.Regions.Where(x => x.Cells.Count >= 4).OrderBy(x => x.Ore).First();
            var tr = TerrainMap.Generate(new Vector2(-512), 1024f, 8f, 3, RegionMapping.TerrainFor(rich, p));
            var mr = ResourceMap.Generate(new Vector2(-512), 1024f, 16f, RegionResources.FromRegion(rich), tr, 3);
            var tp = TerrainMap.Generate(new Vector2(-512), 1024f, 8f, 3, RegionMapping.TerrainFor(poor, p));
            var mp = ResourceMap.Generate(new Vector2(-512), 1024f, 16f, RegionResources.FromRegion(poor), tp, 3);
            Assert.True(mr.TotalAmount(ResourceIds.Ore) > mp.TotalAmount(ResourceIds.Ore) * 2 + 1);

            // Länder-Wirtschaft tickt täglich mit
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) w.Step();
            Assert.Equal(2, p.Economy.DaysSimulated);
        }

        [Fact]
        public void LandValue_UsesRegionTerrain_ShoreOnlyWithWater()
        {
            // G6 liest Ufer/Aussicht live aus world.Terrain → Küstenregion hat Uferlagen, trockene Binnenregion keine
            var p = P;
            var big = p.Regions.Where(r => r.Cells.Count >= 4).ToList();
            int coastId = big.First(r => r.IsCoastal && !r.HasRiver).Id;
            int dryId = big.First(r => !r.HasRiver && !r.IsCoastal).Id;
            float MaxShore(int region, out float maxView)
            {
                var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateWorld = true, WorldSeed = 2024, StartRegion = region, GenerateTerrain = true, SizeMeters = 1024f });
                w.LandValue.ForceRecalculate();
                float shore = 0f; maxView = 0f;
                for (float x = -480f; x <= 480f; x += 16f)
                for (float y = -480f; y <= 480f; y += 16f)
                {
                    var f = w.LandValue.Explain(new Vector2(x, y));
                    if (f.IsWater) continue;
                    shore = MathF.Max(shore, f.Water);
                    maxView = MathF.Max(maxView, f.View);
                }
                return shore;
            }
            float coastShore = MaxShore(coastId, out float coastView);
            float dryShore = MaxShore(dryId, out float dryView);
            output.WriteLine($"Ufer-Beitrag Küste {coastShore:0.000} / trocken {dryShore:0.000} · Aussicht {coastView:0.000} / {dryView:0.000}");
            Assert.True(coastShore > 0.03f);
            Assert.Equal(0f, dryShore);
        }

        // ------------------------------------------------------------ Ressourcen-API

        static Vector2 FindRich(ResourceMap m, string id)
        {
            // reichste Zelle im inneren Bereich (|x|,|y| < 300 m), damit auch Straßen daneben auf die Karte passen
            var layer = m.Layer(id);
            int best = -1;
            for (int i = 0; i < layer.RawValues.Length; i++)
            {
                var c = layer.CellCenter(i % layer.Width, i / layer.Width);
                if (MathF.Abs(c.X) > 300f || MathF.Abs(c.Y) > 300f) continue;
                if (best < 0 || layer.RawValues[i] > layer.RawValues[best]) best = i;
            }
            return layer.CellCenter(best % layer.Width, best / layer.Width);
        }

        [Fact]
        public void Extract_ReducesDeposit_FiniteAndUnknown()
        {
            var w = new SimulationWorld(new WorldSettings { GenerateTerrain = true, SizeMeters = 1024f });
            var res = w.Resources;
            var p = FindRich(res, ResourceIds.Ore);
            float before = res.DepositAt(ResourceIds.Ore, p);
            Assert.InRange(before, 0.05f, 1f);
            float cell = res.AmountAt(ResourceIds.Ore, p);
            float got = res.Extract(ResourceIds.Ore, p, cell * 0.5f);
            Assert.Equal(cell * 0.5f, got, 2);
            Assert.InRange(res.DepositAt(ResourceIds.Ore, p), before * 0.49f, before * 0.51f);
            // mehr als da ist: nimmt Zelle + Nachbarn leer, liefert weniger als verlangt
            float huge = res.Extract(ResourceIds.Ore, p, 1e9f);
            Assert.True(huge > 0f && huge < 1e9f);
            Assert.Equal(0f, res.DepositAt(ResourceIds.Ore, p));
            // Endlich: Erz erneuert sich nicht
            int v = res.Version;
            res.RegenerateDay();
            Assert.Equal(0f, res.DepositAt(ResourceIds.Ore, p));
            // Unbekannt/außerhalb/ungültige Menge
            Assert.Equal(0f, res.DepositAt("gold", p));
            Assert.Equal(0f, res.Extract("gold", p, 5f));
            Assert.Equal(0f, res.Extract(ResourceIds.Ore, new Vector2(5000f, 0f), 5f));
            Assert.Equal(0f, res.Extract(ResourceIds.Ore, p, -3f));
            Assert.Equal(0f, res.Extract(ResourceIds.Ore, p, float.NaN));
            Assert.True(res.Version >= v);
        }

        [Fact]
        public void Wood_Fertility_Groundwater_Regenerate()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true, SizeMeters = 1024f });
            var res = w.Resources;
            var forest = FindRich(res, ResourceIds.Wood);
            float natural = res.DepositAt(ResourceIds.Wood, forest);
            Assert.True(natural > 0.3f);
            res.Extract(ResourceIds.Wood, forest, res.AmountAt(ResourceIds.Wood, forest) * 0.8f);
            float cut = res.DepositAt(ResourceIds.Wood, forest);
            var field = FindRich(res, ResourceIds.Fertility);
            float fert0 = res.DepositAt(ResourceIds.Fertility, field);
            res.Extract(ResourceIds.Fertility, field, res.AmountAt(ResourceIds.Fertility, field) * 0.5f);
            float fertCut = res.DepositAt(ResourceIds.Fertility, field);
            var well = FindRich(res, ResourceIds.Groundwater);
            res.Extract(ResourceIds.Groundwater, well, res.AmountAt(ResourceIds.Groundwater, well) * 0.5f);
            float gwCut = res.DepositAt(ResourceIds.Groundwater, well);

            // 30 Spieltage über das WorldSystem
            for (int i = 0; i < SimulationClock.TicksPerDay * 30; i++) w.Step();
            float wood30 = res.DepositAt(ResourceIds.Wood, forest);
            float fert30 = res.DepositAt(ResourceIds.Fertility, field);
            float gw30 = res.DepositAt(ResourceIds.Groundwater, well);
            output.WriteLine($"Wald {natural:0.00} → {cut:0.00} → {wood30:0.000} · Boden {fert0:0.00} → {fertCut:0.00} → {fert30:0.00} · Grundwasser {gwCut:0.00} → {gw30:0.00}");
            Assert.True(wood30 > cut && wood30 < natural);
            Assert.True(fert30 > fertCut + (fert0 - fertCut) * 0.2f && fert30 <= fert0 + 1e-5f);
            Assert.True(gw30 > gwCut);
        }

        [Fact]
        public void Roads_ClearForestAndSealSoil()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = 1024f });
            var res = w.Resources;
            var forest = FindRich(res, ResourceIds.Wood);
            Assert.True(res.DepositAt(ResourceIds.Wood, forest) > 0.2f);
            RoadBuilder.Build(w.Roads, RoadBuilder.Plan(w.Roads, forest - new Vector2(60f, 0f), 0f, null, forest + new Vector2(60f, 0f), 0f,
                RoadCatalog.CityTwoLane, 0f, 0f));
            Assert.Equal(0f, res.DepositAt(ResourceIds.Wood, forest));
            Assert.Equal(0f, res.DepositAt(ResourceIds.Fertility, forest));
            Assert.Equal(0f, res.NaturalAt(ResourceIds.Wood, forest)); // wächst unter der Straße nicht nach
            // weit weg unverändert
            Assert.Equal(res.NaturalAt(ResourceIds.Ore, forest), res.DepositAt(ResourceIds.Ore, forest));
        }
    }
}
