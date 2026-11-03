using System;
using System.Collections.Generic;
using System.Linq;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Spielstart &amp; Startregion: freies Land, Spieler als Stadtstaat, Startsuche nach Vorlieben, Spielstand.</summary>
    public class StartFinderTests
    {
        readonly ITestOutputHelper output;
        public StartFinderTests(ITestOutputHelper output) { this.output = output; }

        static readonly Lazy<Planet> Shared = new Lazy<Planet>(() => PlanetGenerator.Generate(2024));
        static readonly Lazy<StartFinder> SharedFinder = new Lazy<StartFinder>(() => new StartFinder(Shared.Value));
        static StartFinder F => SharedFinder.Value;

        static readonly PlanetSettings Small = new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 };

        static List<string> Sel(params string[] ids) => ids.ToList();

        // ------------------------------------------------------------ Politisches Modell

        [Fact]
        public void FreeLand_InSeveralClimateZones_AndOnSeveralLandmasses()
        {
            var p = Shared.Value;
            var free = F.Candidates;
            output.WriteLine($"{p.FreeRegionCount} freie von {p.Regions.Count} Regionen, wählbar {free.Count}");
            output.WriteLine(string.Join(" · ", free.GroupBy(f => f.Climate).Select(g => $"{g.Key} {g.Count()}")));
            Assert.InRange(p.FreeRegionCount / (float)p.Regions.Count, 0.25f, 0.4f);
            Assert.True(free.Select(f => f.Climate).Distinct().Count() >= 5, "freies Land in mindestens 5 Klimazonen");
            // freies Land auf mehreren Landmassen (Kontinenten) – Zusammenhangskomponenten über Landzellen
            var mass = new int[p.CellCount];
            for (int i = 0; i < mass.Length; i++) mass[i] = -1;
            int masses = 0;
            for (int i = 0; i < p.CellCount; i++)
            {
                if (!p.IsLand(i) || mass[i] >= 0) continue;
                var q = new Queue<int>();
                q.Enqueue(i); mass[i] = masses;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int j = p.Neighbor(c, dx, dy);
                            if (j < 0 || !p.IsLand(j) || mass[j] >= 0) continue;
                            mass[j] = masses; q.Enqueue(j);
                        }
                }
                masses++;
            }
            int freeMasses = free.Select(f => mass[f.Region.CenterCell]).Distinct().Count();
            output.WriteLine($"freies Land auf {freeMasses} Landmassen");
            Assert.True(freeMasses >= 3);
            // NPC-Länder bleiben zusammenhängend genug: jedes hat seine Hauptstadt und mindestens eine Region
            foreach (var c in p.Countries.Where(c => !c.IsCityState))
            {
                Assert.Contains(c.CapitalRegionId, c.Regions);
                Assert.False(p.Regions[c.CapitalRegionId].IsFree);
            }
        }

        [Fact]
        public void LegacyModel_FreeLandZero_BitIdenticalToBefore()
        {
            // Prüfsumme des politischen Modells vor „Spielstart & Startregion“ (Seed 2024): alte Spielstände laden mit
            // FreeLandShare 0 und bekommen exakt die alten Länder, Profile und Volkswirtschaften.
            var p = PlanetGenerator.Generate(2024, new PlanetSettings { FreeLandShare = 0f });
            Assert.Equal(0, p.FreeRegionCount);
            ulong h = 1469598103934665603UL;
            ulong H(ulong x, double v) { x ^= (ulong)BitConverter.DoubleToInt64Bits(v); return x * 1099511628211UL; }
            foreach (var r in p.Regions) h = H(h, r.CountryId);
            foreach (var c in p.Countries)
            {
                h = H(h, c.Id); h = H(h, c.CapitalCell); h = H(h, c.Regions.Count); h = H(h, c.AreaKm2);
                for (int k = 0; k < 5; k++) { h = H(h, c.Profile.Raw[k]); h = H(h, c.Profile.Rank[k]); h = H(h, c.Profile.Comparative[k]); }
                h = H(h, (int)c.Profile.Primary);
                for (int k = 0; k < 8; k++) { h = H(h, c.Economy.CapacityPerCapita[k]); h = H(h, c.Economy.Price[k]); }
                h = H(h, c.Economy.Population);
            }
            output.WriteLine($"Prüfsumme Altmodell: {h}");
            Assert.Equal(LegacyPoliticalHash, h);
            Assert.Equal("Nemarien", p.Countries[0].Name);
            p.SetStartRegion(p.SuggestStartRegion());
            Assert.Equal(178, p.StartRegionId);
            Assert.False(p.PlayerCountry.IsCityState);
        }

        /// <summary>Gemessen mit dem Code vor der Änderung (identisch, inkl. 60 Tage Handel geprüft).</summary>
        const ulong LegacyPoliticalHash = 8620516913494672957UL;

        // ------------------------------------------------------------ Kriterien

        [Fact]
        public void Criteria_Plausible_OreInHillsAndMountains_CoastalOreRare()
        {
            foreach (int seed in new[] { 2024, 7, 99 })
            {
                var p = seed == 2024 ? Shared.Value : PlanetGenerator.Generate(seed);
                var f = seed == 2024 ? F : new StartFinder(p);
                var all = f.Candidates;
                var ore = all.Where(x => StartCriteria.Find("ore").Fits(x)).ToList();
                float oreRelief = ore.Count(x => x.Relief >= 1f) / (float)ore.Count;
                float baseRelief = all.Count(x => x.Relief >= 1f) / (float)all.Count;
                int coastFlat = f.Count(Sel("ore", "coast", "flat"));
                output.WriteLine($"Seed {seed}: Erz {ore.Count}/{all.Count}, davon hügelig/gebirgig {oreRelief:P0} (alle {baseRelief:P0}), Erz an flacher Küste {coastFlat}");
                Assert.True(ore.Count >= 5);
                Assert.True(oreRelief >= 0.5f, "Erz-Regionen überwiegend hügelig/gebirgig");
                Assert.True(oreRelief > baseRelief, "Erz häuft sich im Relief");
                Assert.True(coastFlat <= ore.Count * 0.3f, "Erz an flacher Küste selten");
                // Geologie auf Zellebene: am Meer halb so viel Erz wie im Bergland
                double oc = 0, nc = 0, om = 0, nm = 0;
                for (int i = 0; i < p.CellCount; i++)
                {
                    if (!p.IsLand(i)) continue;
                    if (p.CoastDistance[i] == 1) { oc += p.Ore[i]; nc++; }
                    if (p.Ruggedness(i) > 300f) { om += p.Ore[i]; nm++; }
                }
                Assert.True(oc / nc < 0.7 * (om / nm), $"Küste {oc / nc:0.000} vs. Bergland {om / nm:0.000}");

                // weitere Kriterien folgen den Daten
                foreach (var x in all)
                {
                    if (StartCriteria.Find("coast").Fits(x)) Assert.True(x.Region.IsCoastal);
                    if (StartCriteria.Find("inland").Fits(x)) Assert.False(x.Region.IsCoastal);
                    if (StartCriteria.Find("river").Fits(x)) Assert.True(x.Region.HasRiver);
                    if (StartCriteria.Find("arid").Fits(x)) Assert.True(x.Region.Precipitation < 600f, $"trocken {x.Region.Precipitation}");
                    if (StartCriteria.Find("polar").Fits(x)) Assert.True(x.Region.MeanTemperature < 0f);
                    if (StartCriteria.Find("tropical").Fits(x)) Assert.True(x.Region.MeanTemperature >= 20f);
                    if (StartCriteria.Find("mountains").Fits(x)) Assert.False(StartCriteria.Find("flat").Fits(x));
                }
                float Avg(string id, Func<RegionFacts, float> v) => all.Where(x => StartCriteria.Find(id).Fits(x)).Select(v).DefaultIfEmpty(0f).Average();
                Assert.True(Avg("agriculture", x => x.Region.Fertility) > all.Average(x => x.Region.Fertility));
                Assert.True(Avg("forestry", x => x.Region.Forest) > all.Average(x => x.Region.Forest));
                Assert.True(Avg("coal", x => x.Region.Coal) > all.Average(x => x.Region.Coal));
                Assert.True(Avg("easy", x => x.Habitability) > Avg("hard", x => x.Habitability));
            }
        }

        [Fact]
        public void Exclusion_ComesFromWorld_ZeroMatchesLocked_OthersNot()
        {
            var f = F;
            // Küste schließt Binnenland aus – gesperrt, mit Erklärung
            var states = f.Evaluate(Sel("coast"));
            var inland = states.Single(s => s.Option.Id == "inland");
            Assert.Equal(0, inland.Matches);
            Assert.Equal(OptionAvailability.Unavailable, inland.Availability);
            Assert.Contains("schließen sich aus", inland.Tip);
            var coast = states.Single(s => s.Option.Id == "coast");
            Assert.True(coast.Selected);
            Assert.Equal(f.Count(Sel("coast")), coast.Matches);

            // allgemein: Treffer = Anzahl mit Auswahl + Option; 0 ⇔ gesperrt; wenige ⇔ selten
            foreach (var sel in new[] { Sel(), Sel("temperate"), Sel("coast", "ore"), Sel("arid", "hilly", "oil"), Sel("tropical", "port", "near") })
                foreach (var s in f.Evaluate(sel))
                {
                    var trial = new HashSet<string>(sel);
                    if (!s.Selected) StartFinder.Toggle(trial, s.Option);
                    Assert.Equal(f.Count(trial), s.Matches);
                    Assert.Equal(s.Matches == 0, s.Availability == OptionAvailability.Unavailable);
                    if (s.Matches > 0 && s.Matches <= StartFinder.RareThreshold) Assert.Equal(OptionAvailability.Rare, s.Availability);
                    Assert.False(string.IsNullOrWhiteSpace(s.Tip));
                }

            // eine Kombination ohne Treffer aus der Welt suchen: a passt, b passt, a + b nicht → b gesperrt, sobald a gewählt
            StartOption lockedA = null, lockedB = null;
            foreach (var a in StartCriteria.Options)
                foreach (var b in StartCriteria.Options)
                {
                    if (lockedA != null || a.Group == b.Group || a.Id == b.Id) continue;
                    if (f.Count(Sel(a.Id)) > 0 && f.Count(Sel(b.Id)) > 0 && f.Count(Sel(a.Id, b.Id)) == 0) { lockedA = a; lockedB = b; }
                }
            Assert.NotNull(lockedA);
            output.WriteLine($"gesperrt: {lockedA.Name} + {lockedB.Name}");
            var st = f.Evaluate(Sel(lockedA.Id)).Single(s => s.Option == lockedB);
            Assert.Equal(OptionAvailability.Unavailable, st.Availability);
            Assert.Contains(lockedB.Geology, st.Tip);
            // ... und eine mit Treffern bleibt wählbar
            var open = f.Evaluate(Sel("temperate")).Where(s => !s.Selected && s.Matches > 0).ToList();
            Assert.NotEmpty(open);
            Assert.All(open, s => Assert.NotEqual(OptionAvailability.Unavailable, s.Availability));

            // exklusive Gruppe: neue Klimazone ersetzt die alte
            var set = new HashSet<string> { "temperate" };
            StartFinder.Toggle(set, StartCriteria.Find("tropical"));
            Assert.Equal(new[] { "tropical" }, set.ToArray());
            StartFinder.Toggle(set, StartCriteria.Find("coast"));
            StartFinder.Toggle(set, StartCriteria.Find("river"));
            Assert.Equal(3, set.Count); // Lage: Mehrfachauswahl
            StartFinder.Toggle(set, StartCriteria.Find("coast"));
            Assert.DoesNotContain("coast", set);
        }

        [Fact]
        public void Suggestions_FulfilSelection_WithReasons()
        {
            var f = F;
            foreach (var sel in new[] { Sel(), Sel("temperate", "river", "agriculture"), Sel("coast", "tourism"), Sel("mountains", "ore"), Sel("arid", "oil") })
            {
                var list = f.Suggest(sel);
                int n = f.Count(sel);
                Assert.Equal(Math.Min(5, n), list.Count);
                output.WriteLine($"[{StartFinder.DescribeSelection(sel)}] {n} passen");
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    output.WriteLine($"  {s.Region.Name}: {s.Reason}");
                    Assert.True(f.Matches(s.Facts, sel));
                    Assert.True(s.Region.IsFree);
                    Assert.False(string.IsNullOrWhiteSpace(s.Reason));
                    Assert.Contains(StartCriteria.ClimateWord(s.Facts.Climate), s.Reason);
                    Assert.Contains("km", s.Reason);
                    Assert.Equal(8, s.Values.Count);
                    if (i > 0) Assert.True(list[i - 1].Score >= s.Score);
                }
            }
            // Erz-Vorschlag nennt die Lage des Erzes
            var oreFirst = f.Suggest(Sel("ore"))[0];
            Assert.Contains("Erz ", oreFirst.Reason);
            // freie Wahl auf der Karte: jede freie Region ist beschreibbar, Länder-Regionen nicht wählbar
            var p = Shared.Value;
            var npcRegion = p.Regions.First(r => !r.IsFree);
            Assert.Null(f.FactsFor(npcRegion.Id));
            Assert.False(f.IsSelectable(npcRegion.Id));
            var any = f.Candidates[f.Candidates.Count / 2];
            var d = f.Describe(any.Region.Id, Sel());
            Assert.NotNull(d);
            Assert.Same(any, d.Facts);
        }

        [Fact]
        public void Determinism_SameSeed_SameFreeLandAndSuggestions()
        {
            var a = PlanetGenerator.Generate(31, Small);
            var b = PlanetGenerator.Generate(31, Small);
            Assert.Equal(a.Regions.Select(r => r.CountryId), b.Regions.Select(r => r.CountryId));
            var fa = new StartFinder(a);
            var fb = new StartFinder(b);
            var sel = Sel("coast", "agriculture");
            Assert.Equal(fa.Suggest(sel).Select(s => s.Region.Id + s.Reason), fb.Suggest(sel).Select(s => s.Region.Id + s.Reason));
            Assert.Equal(fa.Evaluate(sel).Select(s => s.Matches), fb.Evaluate(sel).Select(s => s.Matches));
        }

        // ------------------------------------------------------------ Spieler als Stadtstaat

        static SimulationWorld NewCityStateWorld(string cityName = "Bergheim")
        {
            var probe = PlanetGenerator.Generate(31, Small);
            var finder = new StartFinder(probe);
            int region = finder.Suggest(Sel("temperate"))[0].Region.Id;
            return SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = 31, Planet = Small, StartRegion = region, CityName = cityName,
                GenerateTerrain = false, SizeMeters = 1024f,
            });
        }

        [Fact]
        public void NewGame_PlayerStartsInFreeRegion_AsCityState()
        {
            var w = NewCityStateWorld();
            var p = w.Planet;
            var me = p.PlayerCountry;
            Assert.True(me.IsCityState && me.IsPlayer);
            Assert.Equal("Freie Stadt Bergheim", me.Name);
            Assert.Equal("Bergheim", me.CapitalName);
            Assert.Equal("Bergheim", w.Realm.CityName);
            Assert.Equal(me.Name, w.Realm.DisplayName);
            Assert.False(w.Realm.IsNation);
            Assert.False(w.Realm.CanFoundNation); // Etappe 7: erst mit mehreren Städten
            Assert.Equal(new[] { w.StartRegion.Id }, w.Realm.CityRegions);
            Assert.Same(me, w.StartRegion.Country);
            // die Region war vor dem Start frei
            Assert.True(PlanetGenerator.Generate(31, Small).Regions[w.StartRegion.Id].IsFree);
            // Umbenennen wirkt auf Stadtstaat und Handel
            Assert.True(w.Realm.RenameCity("  Neu Bergheim  "));
            Assert.Equal("Freie Stadt Neu Bergheim", me.Name);
            Assert.False(w.Realm.RenameCity("   "));
            Assert.Equal("Neu Bergheim", w.Realm.CityName);
            // ohne Weltkarte gibt es trotzdem einen Stadtnamen
            var plain = SimulationWorld.CreateDefault(new WorldSettings { CityName = "Testdorf" });
            Assert.Equal("Testdorf", plain.Realm.CityName);
            Assert.Equal(PlayerRealm.DefaultCityName, SimulationWorld.CreateDefault(new WorldSettings()).Realm.CityName);
        }

        [Fact]
        public void TradeAndWorldEconomy_RunWithCityState()
        {
            var w = NewCityStateWorld();
            var t = w.Trade;
            Assert.NotNull(t);
            int me = t.PlayerId;
            Assert.Equal(w.Planet.PlayerCountry.Id, me);
            Assert.Equal(w.Planet.Countries.Count, t.Nations.Count);
            Assert.Equal(w.Planet.Countries.Count, w.Planet.Economy.Nations.Count);
            Assert.True(t.Nations[me].IsPlayer);
            Assert.Equal(0.0, t.Nations[me].Tariffs.General);
            // Handelswege vom Stadtstaat in jedes Land, Handelsort = Startregion
            Assert.Equal(w.StartRegion.CenterCell, t.Geography.LocationOf(me));
            foreach (var n in t.Nations) if (n.Id != me) Assert.NotNull(t.Geography.Route(me, n.Id));

            // Spieler bietet einem NPC-Land Stahl an (wie im Handelsfenster) und die Welt läuft weiter
            int npc = t.Nations.First(n => !n.IsPlayer).Id;
            var r = t.Execute(new ProposeContractCommand(me, npc, TradeSide.Buy,
                new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 10, Weeks = 2, Price = Math.Round(t.WorldPrice(GoodsCatalog.Steel) * 1.3, 2) }));
            Assert.True(r.Ok, r.Message);
            for (int i = 0; i < SimulationClock.TicksPerDay * 30; i++) w.Step();
            Assert.Equal(30, t.Day);
            Assert.Equal(30, w.Planet.Economy.DaysSimulated);
            var mine = w.Planet.PlayerCountry.Economy;
            Assert.Equal(29, mine.LastDay);
            Assert.True(mine.Population > 0);
            foreach (var g in GoodsCatalog.All) Assert.True(t.WorldPrice(g) > 0 && !double.IsNaN(t.WorldPrice(g)));
            Assert.NotEqual(OfferStatus.Open, t.Offer(r.Id)?.Status ?? OfferStatus.Expired); // beantwortet
            // NPC-Länder handeln untereinander
            Assert.True(t.Contracts.Count + t.History.Count > 0);
        }

        // ------------------------------------------------------------ Speichern & Laden

        [Fact]
        public void SaveLoad_RealmAndOwnership_Deterministic()
        {
            var a = NewCityStateWorld("Hafenau");
            for (int i = 0; i < SimulationClock.TicksPerDay * 3 + 7; i++) a.Step();
            a.Realm.RenameCity("Hafenau am Meer");
            var data = SaveGame.SaveToBytes(a, "Test");
            var b = SaveGame.Load(data);
            Assert.Equal("Hafenau am Meer", b.Realm.CityName);
            Assert.Equal("Freie Stadt Hafenau am Meer", b.Planet.PlayerCountry.Name);
            Assert.Equal(a.Planet.Regions.Select(r => r.CountryId), b.Planet.Regions.Select(r => r.CountryId));
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) { a.Step(); b.Step(); }
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));
            Assert.NotNull(SaveGame.Capture(b).Find("realm"));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void SaveLoad_OldSave_KeepsOldPoliticalModel(int oldVersion)
        {
            // alter Stand (Weltabschnitt v1/v2 ohne freies Land, ohne „realm“): Welt im alten Modell, Spieler-Land = NPC-Land der Startregion
            var settings = new WorldSettings
            {
                GenerateWorld = true, WorldSeed = 31, Planet = new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16, FreeLandShare = 0f },
                GenerateTerrain = false, SizeMeters = 1024f,
            };
            var old = SimulationWorld.CreateDefault(settings);
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) old.Step();
            var c = SaveGame.Capture(old, "alt");
            var ws = c.Find(SaveGame.WorldSectionId);
            // v3 hängt Float freies Land + String Stadtname an (4 + 1 Byte bei null), v2 davor den Verkehrsschritt (Int, auf der Tick-Grenze 0)
            int cut = oldVersion == 2 ? 5 : 9;
            c.Set(SaveGame.WorldSectionId, oldVersion, ws.Data.Take(ws.Data.Length - cut).ToArray());
            Assert.True(c.Remove("realm"));
            var loaded = SaveGame.Restore(c);
            Assert.Equal(0, loaded.Planet.FreeRegionCount);
            Assert.False(loaded.Planet.PlayerCountry.IsCityState);
            Assert.Equal(old.Planet.PlayerCountry.Name, loaded.Planet.PlayerCountry.Name);
            Assert.Equal(PlayerRealm.DefaultCityName, loaded.Realm.CityName);
            Assert.Equal(old.Planet.Countries.Count, loaded.Planet.Countries.Count);
            // Handel/Länder passen (gleiche Ids) und laufen weiter
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) loaded.Step();
            Assert.Equal(3, loaded.Trade.Day);
        }
    }
}
