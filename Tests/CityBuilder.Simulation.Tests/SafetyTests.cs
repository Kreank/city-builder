using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Safety;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Sicherheit: Brände (Entstehung, Wachstum, Übergreifen, Löschen mit echten Löschfahrzeugen, Stau/Wasser, Folgen) und
    /// Kriminalität (Faktoren, Polizei/Streife senkt die Rate, Straftat mit Einsatz), Spielstand, Balance-Info.
    /// </summary>
    public class SafetyTests
    {
        readonly ITestOutputHelper output;
        public SafetyTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>
        /// Straße (0,0)→(600,0); Strom (Kraftwerk West + Erdkabel) und optional Wasser (Wasserturm am Ostende + Rohr);
        /// Reihe Einfamilienhäuser (gesetzt) nördlich x 100…420, Wohnzone südlich bei x 520, optional Feuerwache/Polizei südlich.
        /// Sicherheit wird erst nach dem Einzug eingeschaltet; zufällige Entstehung/Straftaten aus (Tests steuern selbst).
        /// </summary>
        static SimulationWorld Town(bool vehicles = true, bool water = true, bool fireStation = true, bool police = false, int seed = 4711,
            float stationX = 250f, bool zoneBuilding = false)
        {
            var w = SimulationWorld.CreateDefault(null, seed);
            w.Utilities.AbandonmentEnabled = false;
            w.ServiceVehicles.Enabled = vehicles;
            Road(w, new Vector2(0, 0), new Vector2(600, 0));
            GridKit.PowerChainWest(w);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(600, 0));
            if (water)
            {
                Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(614, 0), 270f));
                GridKit.Lay(w, UtilityCatalog.WaterPipe, new Vector2(0, 0), new Vector2(606, 0));
            }
            var row = w.City.BuildHouseRow(w.City.PlanHouseRow(HouseCatalog.Low, new Vector2(100, 14), new Vector2(420, 14)));
            Assert.True(row.Count >= 10, $"Reihe: {row.Count}");
            w.City.Paint(new Vector2(480, -30), 40f, ZoneType.ResidentialLow);
            if (fireStation) Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.FireStation, new Vector2(stationX, -30))));
            if (police) Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(150, -30))));
            for (int i = 0; i < 2500; i++)
            {
                w.Step();
                if (i % 50 == 0 && w.City.Buildings.Count(b => b.IsConstructed && b.Residents > 0) >= 12 &&
                    (!zoneBuilding || w.City.Buildings.Any(b => !b.IsPlayerPlaced && b.IsConstructed))) break;
            }
            Run(w, 20);
            w.Safety.IgnitionScale = 0f;
            w.Safety.CrimeScale = 0f;
            w.Safety.Enabled = true;
            Run(w, 10); // erste volle Stunde: Kriminalitätsraten, Zuordnung
            return w;
        }

        static Building House(SimulationWorld w, int index = 4) =>
            w.City.Buildings.Where(b => b.IsPlayerPlaced && b.IsConstructed).OrderBy(b => b.Position.X).ElementAt(index);

        /// <summary>Läuft in Verkehrsschritten, bis der Brand vorbei ist (oder die Zeit um ist). Liefert den Abschluss.</summary>
        static FireRecord BurnOut(SimulationWorld w, Fire f, int maxTicks = 3 * SimulationClock.TicksPerDay)
        {
            for (int i = 0; i < maxTicks && w.Safety.ActiveFires.Contains(f); i++) w.Step();
            return w.Safety.FireHistory.FirstOrDefault(r => r.FireId == f.Id);
        }

        // ------------------------------------------------------------ Grundsätzliches

        [Fact]
        public void Default_Off_NoFires_PlaceholderCallsStay_NeutralHooks()
        {
            var w = SimulationWorld.CreateDefault(null, 4711);
            Assert.False(w.Safety.Enabled);
            Assert.True(w.ServiceVehicles.PlaceholderFireCalls);
            Assert.Null(w.ServiceVehicles.HoldAtStop);
            Assert.Null(w.ServiceVehicles.PatrolWeight);
            Road(w, new Vector2(0, 0), new Vector2(400, 0));
            w.City.Paint(new Vector2(200, 30), 80f, ZoneType.ResidentialLow);
            Run(w, 2 * SimulationClock.TicksPerDay);
            Assert.Empty(w.Safety.ActiveFires);
            Assert.Equal(0, w.Safety.Today.Fires + w.Safety.Yesterday.Fires + w.Safety.Total.Fires);
            Assert.Equal(0, w.Safety.Today.Crimes + w.Safety.Yesterday.Crimes + w.Safety.Total.Crimes);
            Assert.All(w.City.Buildings, b => Assert.Equal(0f, w.Citizens.CrimeLevel(b)));
            Assert.Equal(0f, w.LandValue.SafetyPenaltyAt(new Vector2(200, 30)));
            w.Safety.Enabled = true;
            Assert.False(w.ServiceVehicles.PlaceholderFireCalls);
            Assert.NotNull(w.ServiceVehicles.HoldAtStop);
        }

        [Fact]
        public void FireRisk_Factors_ZoneRecipeConditionGarbagePower()
        {
            var w = Town(vehicles: false);
            var house = House(w);
            var info = w.Safety.FireRiskOf(house);
            output.WriteLine($"Einfamilienhaus: Faktor {info.Factor:0.00} (Zone {info.Zone}, Zustand {info.Condition:0.00}, Müll {info.Garbage:0.00}, " +
                             $"Strom {info.Power}, Größe {info.Size:0.00}, Brandschutz {info.Prevention:0.00}) ≈ {info.PerYear:0.000}/Jahr");
            Assert.InRange(info.Factor, 0.5f, 1.5f);
            float before = info.Factor;
            house.Condition = 0.3f; // alt/heruntergekommen
            Assert.True(w.Safety.FireRiskOf(house).Factor > before * 1.5f);
            Assert.True(SafetyRules.RecipeFactor("refinery") > SafetyRules.RecipeFactor("office"));
            Assert.True(SafetyRules.ZoneFactor(ZoneType.Industry) > SafetyRules.ZoneFactor(ZoneType.ResidentialLow));
            // Feuerwehr-Abdeckung (Brandschutz) senkt die Entstehung etwas
            Assert.True(w.Services.Coverage(ServiceKinds.Fire, house) > 0.5f);
            Assert.True(info.Prevention < 1f);
        }

        // ------------------------------------------------------------ Brände

        [Fact]
        public void Fire_Grows_WithoutFireStation_BurnsDown_RuinAndRebuild()
        {
            var w = Town(vehicles: false, fireStation: false);
            var house = House(w);
            int households = w.Citizens.HouseholdsIn(house.Id).Count;
            Assert.True(households > 0);
            var f = w.Safety.Ignite(house, w.Clock.Tick);
            Assert.NotNull(f);
            f.Intensity = 0.2f; // über dem Entstehungsbrand (sonst löschen ihn die Bewohner evtl. selbst)
            var samples = new List<float>();
            for (int i = 0; i < 30 && w.Safety.ActiveFires.Contains(f); i++) { w.Step(); samples.Add(f.Intensity); }
            output.WriteLine("Stärke je Tick: " + string.Join(" ", samples.Select(x => x.ToString("0.00"))));
            Assert.True(samples.Last() > 0.6f, "Brand wächst ohne Feuerwehr zum Großbrand");
            for (int i = 1; i < samples.Count; i++) Assert.True(samples[i] >= samples[i - 1] - 1e-4f, "wächst monoton ohne Löschen");
            var rec = BurnOut(w, f);
            Assert.NotNull(rec);
            output.WriteLine($"Ende: {rec.Outcome}, Schaden {rec.Damage:0.00}, Dauer {(rec.EndTick - rec.StartTick) / 10f:0.0} h, Verletzte {rec.Injured}");
            Assert.Equal(FireOutcome.TotalLoss, rec.Outcome);
            // Gesetztes Gebäude: Brandruine, Bewohner ausgezogen (umgezogen oder weggezogen), wartet auf den Spieler
            var b = w.City.GetBuilding(house.Id);
            Assert.NotNull(b);
            Assert.True(b.OutOfService);
            Assert.False(b.IsConstructed);
            Assert.Empty(w.Citizens.HouseholdsIn(b.Id));
            var repair = w.Safety.RepairOf(b.Id);
            Assert.True(repair.IsRuin);
            Run(w, 2 * SimulationClock.TicksPerDay);
            Assert.True(w.City.GetBuilding(b.Id).OutOfService, "Ruine baut sich nicht von selbst wieder auf");
            // Wiederaufbau gegen Geld
            double spentBefore = w.Economy.CurrentWeek.Amount(Economy.BudgetCategory.HouseConstruction);
            float cost = w.Safety.RebuildCost(b);
            Assert.Equal(HouseCatalog.Low.Cost * SafetyRules.RebuildCostShare, cost, 1);
            Assert.True(w.Safety.Rebuild(b.Id));
            Assert.Equal(spentBefore + cost, w.Economy.CurrentWeek.Amount(Economy.BudgetCategory.HouseConstruction), 1);
            Run(w, SafetyRules.TicksForDays(SafetyRules.RebuildDays) + 2);
            Assert.False(b.OutOfService);
            Assert.True(b.IsConstructed);
            Assert.Null(w.Safety.RepairOf(b.Id));
            // Bodenwert: frischer Brand in der Nähe zieht ab
            Assert.True(w.Safety.LandValuePenalty(b.Position) > 0.1f);
        }

        [Fact]
        public void Fire_ZoneBuilding_TotalLoss_IsDemolished()
        {
            var w = Town(vehicles: false, fireStation: false, zoneBuilding: true);
            var zoneBuilding = w.City.Buildings.Where(b => !b.IsPlayerPlaced && b.IsConstructed).OrderBy(b => b.Id).FirstOrDefault();
            Assert.NotNull(zoneBuilding);
            var f = w.Safety.Ignite(zoneBuilding, w.Clock.Tick);
            f.Intensity = 0.5f;
            var rec = BurnOut(w, f);
            Assert.Equal(FireOutcome.TotalLoss, rec.Outcome);
            Assert.Null(w.City.GetBuilding(zoneBuilding.Id));
        }

        [Fact]
        public void Fire_Spreads_ToAdjacentRowHouses_NotFar()
        {
            var w = Town(vehicles: false, fireStation: false);
            var row = w.City.Buildings.Where(b => b.IsPlayerPlaced && b.IsConstructed).OrderBy(b => b.Position.X).ToList();
            int spread = 0;
            bool far = false;
            for (int k = 2; k < row.Count - 2 && spread == 0; k += 3)
            {
                if (row[k].OutOfService) continue;
                var f = w.Safety.Ignite(row[k], w.Clock.Tick);
                if (f == null) continue;
                f.Intensity = 0.9f;
                for (int i = 0; i < SimulationClock.TicksPerDay && w.Safety.ActiveFires.Count > 0; i++)
                {
                    w.Step();
                    foreach (var s in w.Safety.ActiveFires.Where(x => x.SourceFireId != 0))
                    {
                        float d = Vector2.Distance(s.Position, row[k].Position);
                        if (d > 60f) far = true;
                    }
                }
                spread = w.Safety.Total.Spread + w.Safety.Today.Spread + w.Safety.Yesterday.Spread;
            }
            output.WriteLine($"Übergriffe: {spread}, Brände gesamt {w.Safety.FireHistory.Count}");
            Assert.True(spread > 0, "Vollbrand greift auf das Nachbarhaus der Reihe über");
            Assert.False(far, "kein Übergreifen über große Abstände");
            Assert.Contains(w.Safety.FireHistory, r => r.Spread);
        }

        [Fact]
        public void FireTruck_Extinguishes_OnArrival_SlowTrafficDelays()
        {
            var fast = Town(stationX: 580f);
            var slow = Town(stationX: 580f);
            foreach (var s in slow.Roads.Segments) slow.Traffic.Rules.SetSpeedLimit(s.Id, 10f); // zähfließend (Stau-Ersatz, deterministisch)
            var ra = Burn(fast, "frei", 0);
            var rb = Burn(slow, "langsam", 0);
            Assert.Equal(FireOutcome.Extinguished, ra.Outcome);
            Assert.True(ra.ResponseMinutes >= 0f && rb.ResponseMinutes > ra.ResponseMinutes, "langsamer Verkehr verzögert die Ankunft");
            Assert.True(rb.Damage > ra.Damage, "spätere Ankunft = mehr Schaden");
            Assert.True(fast.Safety.Total.Extinguished + fast.Safety.Today.Extinguished + fast.Safety.Yesterday.Extinguished >= 1);
        }

        FireRecord Burn(SimulationWorld w, string label, int houseIndex = 8)
        {
            var house = House(w, houseIndex);
            var f = w.Safety.Ignite(house, w.Clock.Tick);
            f.Intensity = 0.2f;
            float atArrival = -1f;
            bool truckSeen = false;
            for (int i = 0; i < SimulationClock.TicksPerDay && w.Safety.ActiveFires.Contains(f); i++)
            {
                for (int s = 0; s < TimeModel.TrafficStepsPerTick; s++)
                {
                    w.Substep();
                    foreach (var v in w.Traffic.Vehicles)
                    {
                        var u = w.ServiceVehicles.UnitForTrip(v.TripId);
                        if (u != null && u.Kind == ServiceVehicleKind.FireTruck && u.Mission == ServiceMission.FireCall) truckSeen = true;
                    }
                }
                if (atArrival < 0f && f.FirstArrivalTick >= 0) atArrival = f.Intensity;
            }
            var rec = w.Safety.FireHistory.First(r => r.FireId == f.Id);
            output.WriteLine($"{label}: {rec.Outcome}, Ankunft nach {rec.ResponseMinutes:0} min (Stärke dann {atArrival:0.00}), " +
                             $"Schaden {rec.Damage:0.00}, Spitze {rec.PeakIntensity:0.00}, Dauer {(rec.EndTick - rec.StartTick) * 6} min");
            Assert.True(truckSeen, "echtes Löschfahrzeug fährt im Verkehr");
            return rec;
        }

        [Fact]
        public void Fire_WithoutWater_FirefightingWeaker()
        {
            var wet = Town(water: true);
            var dry = Town(water: false);
            Assert.True(wet.Utilities.GetStatus(House(wet, 8)).HasWater);
            Assert.False(dry.Utilities.GetStatus(House(dry, 8)).HasWater);
            Assert.Equal(1f, wet.Safety.WaterFactor(House(wet, 8)));
            Assert.Equal(SafetyRules.TankWaterFactor, dry.Safety.WaterFactor(House(dry, 8)));
            var a = Burn(wet, "mit Hydrant");
            var b = Burn(dry, "nur Tankwasser");
            Assert.True(b.Damage > a.Damage, "ohne Wasser mehr Schaden");
            Assert.True(b.EndTick - b.StartTick > a.EndTick - a.StartTick, "ohne Wasser dauert das Löschen länger");
        }

        [Fact]
        public void Fire_SevereDamage_ResidentsMoveTemporarily_AndReturn()
        {
            var w = Town(vehicles: false, fireStation: false);
            // freie Wohnungen für die vorübergehende Unterkunft (Reihe südlich)
            Assert.NotEmpty(w.City.BuildHouseRow(w.City.PlanHouseRow(HouseCatalog.Medium, new Vector2(100, -14), new Vector2(300, -14))));
            Run(w, 55); // Bauzeit
            var house = House(w, 6);
            var ids = w.Citizens.HouseholdsIn(house.Id).Select(h => h.Id).ToList();
            Assert.NotEmpty(ids);
            var residents = w.Citizens.ResidentsOf(house.Id).ToList();
            var f = w.Safety.Ignite(house, w.Clock.Tick);
            f.Intensity = 0.6f;
            f.Damage = 0.45f;
            // Feuerwehr „kommt“ (Rückfall ohne Wache: Ankunft von Hand) und löscht
            f.FallbackArrivalTick = w.Clock.Tick + 1;
            var rec = BurnOut(w, f);
            output.WriteLine($"Ende {rec.Outcome}, Schaden {rec.Damage:0.00}");
            Assert.Equal(FireOutcome.Extinguished, rec.Outcome);
            Assert.True(rec.Damage >= SafetyRules.SevereDamage);
            Assert.True(house.OutOfService);
            var repair = w.Safety.RepairOf(house.Id);
            Assert.False(repair.IsRuin);
            Assert.Empty(w.Citizens.HouseholdsIn(house.Id));
            Assert.Contains(residents, c => c.Memories.Strength(MemoryKind.Fire) > 0f);
            Run(w, (int)(repair.UntilTick - w.Clock.Tick) + 2);
            Assert.False(house.OutOfService);
            int back = w.Citizens.HouseholdsIn(house.Id).Count(h => ids.Contains(h.Id));
            output.WriteLine($"Zurückgekehrt: {back}/{ids.Count}");
            Assert.True(back > 0, "umgezogene Haushalte kehren nach der Sanierung zurück");
        }

        [Fact]
        public void Fire_Injuries_CallAmbulance_AndHeadline()
        {
            var w = Town();
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Clinic, new Vector2(350, -30))));
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Newspaper, new Vector2(450, -30))));
            Run(w, 12);
            int injured = 0;
            foreach (var house in w.City.Buildings.Where(b => b.IsPlayerPlaced && b.IsConstructed).OrderBy(b => b.Id).Take(8).ToList())
            {
                if (house.OutOfService) continue;
                var f = w.Safety.Ignite(house, w.Clock.Tick);
                if (f == null) continue;
                f.Intensity = 0.7f;
                BurnOut(w, f);
                injured = w.Safety.Today.Injured + w.Safety.Yesterday.Injured + w.Safety.Total.Injured;
                if (injured > 0) break;
            }
            Run(w, 6);
            output.WriteLine($"Verletzte {injured}, Rettungseinsätze {w.ServiceVehicles.Today.Rescues + w.ServiceVehicles.Yesterday.Rescues}, " +
                             $"Schlagzeilen: {string.Join(" | ", w.Media.Recent.Select(h => h.Title))}");
            Assert.True(injured > 0, "Brände mit Menschen im Haus fordern Verletzte");
            Assert.True(w.ServiceVehicles.Units.Any(u => u.Kind == ServiceVehicleKind.Ambulance));
            Assert.Contains(w.Media.Recent, h => h.Topic == Services.Content.HeadlineTopic.Fire || h.Topic == Services.Content.HeadlineTopic.FireRuin);
            Assert.NotEmpty(w.Safety.Events);
        }

        // ------------------------------------------------------------ Kriminalität

        [Fact]
        public void CrimeRate_Factors_Monotonic()
        {
            var f = new CrimeFactors { Unemployment = 0.3f, Dissatisfaction = 0.2f, LowEducation = 0.3f, Density = 0.3f, LowLandValue = 0.5f };
            float baseRate = SafetyRules.CrimeRate(ref f);
            output.WriteLine($"Druck {f.Pressure:0.000} → Rate {baseRate:0.000}");
            CrimeFactors Mod(System.Action<CrimeFactors[]> m) { var a = new[] { f }; m(a); return a[0]; }
            float R(CrimeFactors x) => SafetyRules.CrimeRate(ref x);
            Assert.True(R(Mod(a => a[0].Unemployment = 0.9f)) > baseRate);
            Assert.True(R(Mod(a => a[0].Dissatisfaction = 0.8f)) > baseRate);
            Assert.True(R(Mod(a => a[0].LowEducation = 0.9f)) > baseRate);
            Assert.True(R(Mod(a => a[0].Density = 1f)) > baseRate);
            Assert.True(R(Mod(a => a[0].LowLandValue = 1f)) > baseRate);
            Assert.True(R(Mod(a => a[0].Police = 1f)) < baseRate * 0.5f);
            Assert.True(R(Mod(a => a[0].Patrol = 1f)) < baseRate);
            Assert.True(R(Mod(a => { a[0].Police = 1f; a[0].Patrol = 1f; })) < R(Mod(a => a[0].Police = 1f)));
            Assert.True(SafetyRules.NightWeight(2) > SafetyRules.NightWeight(12));
        }

        [Fact]
        public void Crime_FallsWithPolice_AndPatrols()
        {
            var none = Town(police: false);
            var cops = Town(police: true);
            Run(none, SimulationClock.TicksPerDay);
            Run(cops, SimulationClock.TicksPerDay);
            float a = none.Safety.AverageCrimeRate, b = cops.Safety.AverageCrimeRate;
            var house = House(cops, 3);
            var fac = cops.Safety.CrimeFactorsOf(house);
            output.WriteLine($"Kriminalität Ø ohne Polizei {a:0.000}, mit Polizei + Streife {b:0.000}; Haus: Polizei {fac.Police:0.00}, " +
                             $"Streife {fac.Patrol:0.00}, Arbeitslosigkeit {fac.Unemployment:0.00}, Unzufriedenheit {fac.Dissatisfaction:0.00}, " +
                             $"Bildung {fac.LowEducation:0.00}, Dichte {fac.Density:0.00}, Bodenwert {fac.LowLandValue:0.00} → {fac.Rate:0.000}");
            Assert.True(a > 0f);
            Assert.True(b < a * 0.7f, "Polizei senkt die Kriminalität deutlich");
            Assert.True(fac.Patrol > 0f, "echte Streifenfahrten erzeugen Präsenz");
            Assert.True(cops.ServiceVehicles.Today.PatrolStops + cops.ServiceVehicles.Yesterday.PatrolStops > 0);
            // Straftaten (Würfel an) – mit Polizei weniger
            none.Safety.CrimeScale = cops.Safety.CrimeScale = 20f;
            Run(none, 2 * SimulationClock.TicksPerDay);
            Run(cops, 2 * SimulationClock.TicksPerDay);
            int ca = none.Safety.Today.Crimes + none.Safety.Yesterday.Crimes, cb = cops.Safety.Today.Crimes + cops.Safety.Yesterday.Crimes;
            output.WriteLine($"Straftaten (×20) ohne Polizei {ca}, mit Polizei {cb}; aufgeklärt {cops.Safety.Yesterday.Solved + cops.Safety.Today.Solved}, " +
                             $"in Zellen {cops.Safety.InCustody}/{cops.Safety.CustodyCapacity}");
            Assert.True(cb < ca);
        }

        [Fact]
        public void Crime_Burglary_MemoryAndSafetyNeed_PoliceCarArrives_Clearance()
        {
            var w = Town(police: true);
            var house = House(w, 5);
            var victims = w.Citizens.ResidentsOf(house.Id).ToList();
            Assert.NotEmpty(victims);
            for (int k = 0; k < 8; k++) w.Citizens.ForceUpdateNeeds(); // eingeschwungen
            float before = victims.Average(c => c.Need(NeedKind.Safety));
            var c = w.Safety.Commit(house, w.Clock.Tick, 4242, CrimeKind.Burglary);
            Assert.NotNull(c);
            Assert.True(c.Loss > 0f);
            Assert.Contains(victims, v => v.Memories.Strength(MemoryKind.Burglary) > 0f);
            if (c.Reported)
            {
                Assert.True(c.UnitId > 0, "Streifenwagen rückt aus");
                var unit = w.ServiceVehicles.Units.Single(u => u.Id == c.UnitId);
                Assert.Equal(ServiceMission.CrimeCall, unit.Mission);
                for (int i = 0; i < SimulationClock.TicksPerDay / 4 && !c.Closed; i++) w.Step();
                output.WriteLine($"Einsatz: Ankunft nach {c.ResponseMinutes:0} min, aufgeklärt {c.Solved}");
                Assert.True(c.Closed);
                Assert.True(c.ArrivalTick >= 0, "Polizei war am Tatort");
            }
            for (int k = 0; k < 4; k++) w.Citizens.ForceUpdateNeeds();
            float after = victims.Where(v => w.Citizens.Get(v.Id) != null).Average(v => v.Need(NeedKind.Safety));
            output.WriteLine($"Sicherheit der Bewohner {before:0.00} → {after:0.00}");
            Assert.True(after < before, "Einbruch senkt das Sicherheitsgefühl");
        }

        // ------------------------------------------------------------ Spielstand

        [Fact]
        public void SaveLoad_MidFire_WithRuin_Deterministic()
        {
            var a = Town();
            // Ruine (gesetztes Haus, ausgebrannt) + laufender Brand mit Löschfahrzeug unterwegs
            var ruin = a.Safety.Ignite(House(a, 1), a.Clock.Tick);
            ruin.Damage = 0.99f; ruin.Intensity = 0.9f;
            a.Step();
            Assert.True(House(a, 1).OutOfService || a.City.GetBuilding(ruin.BuildingId).OutOfService);
            var f = a.Safety.Ignite(House(a, 8), a.Clock.Tick);
            f.Intensity = 0.3f;
            a.Safety.CrimeScale = 20f;
            a.Safety.IgnitionScale = 1f;
            Run(a, 1);
            for (int i = 0; i < 61; i++) a.Substep();
            Assert.Contains(a.ServiceVehicles.Units, u => u.Mission == ServiceMission.FireCall && u.IsBusy);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            b.Safety.CrimeScale = 20f;
            Assert.True(b.Safety.Enabled);
            Assert.Equal(a.Safety.ActiveFires.Count, b.Safety.ActiveFires.Count);
            Assert.True(b.City.GetBuilding(ruin.BuildingId).OutOfService);
            Same(a, b, "direkt nach dem Laden");
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) { a.Step(); b.Step(); }
            Same(a, b, "nach einem Tag");
            Assert.Equal(a.Safety.Today.Crimes, b.Safety.Today.Crimes);
        }

        [Fact]
        public void Migration_SaveWithoutSafetySection_LoadsDisabled()
        {
            var a = Town();
            a.Safety.Enabled = false;
            var c = SaveGame.Capture(a);
            c.Sections.RemoveAll(s => s.Id == "safety");
            var b = SaveGame.Restore(c);
            Assert.False(b.Safety.Enabled);
            Assert.Empty(b.Safety.ActiveFires);
            Assert.All(b.City.Buildings, x => Assert.False(x.OutOfService));
            Assert.True(b.ServiceVehicles.PlaceholderFireCalls);
            b.Safety.Enabled = true; // das Spiel schaltet nach dem Laden ein
            Run(b, 12);
            Assert.False(b.ServiceVehicles.PlaceholderFireCalls);
        }

        static void Same(SimulationWorld a, SimulationWorld b, string when)
        {
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            Assert.Contains("safety", ha.Select(x => x.Key));
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, $"{when}: abweichende Abschnitte: {string.Join(", ", diff)}");
        }

        // ------------------------------------------------------------ Balance (Info)

        /// <summary>
        /// Größere Stadt (Raster 5 × 4 Straßen, Reihen aller Bauarten, Feuerwache + Polizei mittig, Wasser/Strom) über 20 Tage mit
        /// echten Fahrzeugen. Entstehung × 30 (sonst zu wenige Brände für eine Aussage) – die echte Häufigkeit steht als Hochrechnung dabei.
        /// </summary>
        [Fact]
        public void Balance_Info_BiggerCity()
        {
            var w = SimulationWorld.CreateDefault(null, 9001);
            w.Utilities.AbandonmentEnabled = false;
            w.ServiceVehicles.Enabled = true;
            for (int y = 0; y <= 480; y += 160) Road(w, new Vector2(0, y), new Vector2(800, y));
            for (int x = 0; x <= 800; x += 200) for (int y = 0; y < 480; y += 160) Road(w, new Vector2(x, y), new Vector2(x, y + 160));
            GridKit.PowerChainWest(w);
            w.Utilities.BuildAlongRoads(w.Utilities.PlanAlongRoads(UtilityCatalog.UndergroundCable));
            foreach (int y in new[] { 0, 160, 320, 480 }) Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(814, y), 270f));
            w.Utilities.BuildAlongRoads(w.Utilities.PlanAlongRoads(UtilityCatalog.WaterPipe));
            foreach (int y in new[] { 0, 160, 320, 480 }) GridKit.Lay(w, UtilityCatalog.WaterPipe, new Vector2(790, y), new Vector2(806, y)); // Anschluss Wasserturm
            // Dienste zuerst (die Reihen lassen belegte Plätze aus)
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.FireStation, new Vector2(300, 190))));
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(500, 290))));
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Clinic, new Vector2(100, 290))));
            Assert.NotNull(w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Landfill, new Vector2(700, 520))));
            var types = new[] { HouseCatalog.Low, HouseCatalog.Medium, HouseCatalog.Shop, HouseCatalog.Low, HouseCatalog.SmallHall, HouseCatalog.MixedUse };
            int t = 0;
            for (int y = 0; y <= 480; y += 160)
                for (int x = 0; x < 800; x += 200)
                {
                    var type = types[t++ % types.Length];
                    if (y < 480) w.City.BuildHouseRow(w.City.PlanHouseRow(type, new Vector2(x + 30, y + 14), new Vector2(x + 170, y + 14)));
                    if (y > 0) w.City.BuildHouseRow(w.City.PlanHouseRow(types[(t + 2) % types.Length], new Vector2(x + 30, y - 14), new Vector2(x + 170, y - 14)));
                }
            Run(w, 2 * SimulationClock.TicksPerDay);
            w.Safety.Enabled = true;
            w.Safety.IgnitionScale = 30f;
            const int days = 20;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Run(w, days * SimulationClock.TicksPerDay);
            sw.Stop();
            var s = new SafetyStats();
            w.Safety.Total.AddTo(s);
            w.Safety.Today.AddTo(s);
            var buildings = w.City.Buildings.Where(b => b.IsConstructed).ToList();
            float factorSum = buildings.Sum(b => w.Safety.FireRiskOf(b).Factor);
            float realPerDay = factorSum * SafetyRules.IgnitionsPerDay;
            output.WriteLine($"Stadt: {buildings.Count} Gebäude, {w.Citizens.Stats.Population} Einwohner, Feuerwehr Ø {w.Services.AverageCoverage(ServiceKinds.Fire):0.00}, Polizei Ø {w.Services.AverageCoverage(ServiceKinds.Police):0.00}");
            output.WriteLine($"Brände (Entstehung ×30, {days} Tage): {s.Fires} (übergegriffen {s.Spread}), selbst gelöscht {s.SelfExtinguished}, Feuerwehr {s.Extinguished}, " +
                             $"schwer {s.SevereDamage}, Totalschaden {s.TotalLosses}, Verletzte {s.Injured}, Ø Anfahrt {s.AverageResponseMinutes:0} min, kein Fahrzeug frei {s.MissedFireDispatches}");
            output.WriteLine($"Versorgt (Strom+Wasser): {buildings.Count(b => w.Utilities.IsSupplied(b))}/{buildings.Count} (Strom {buildings.Count(b => w.Utilities.GetStatus(b).HasPower)}, Wasser {buildings.Count(b => w.Utilities.GetStatus(b).HasWater)}, Bauwerke {string.Join(",", w.Utilities.Facilities.Select(f => f.Type.Id))}, Leitungen {w.Utilities.Lines.Count()}), Ø Zustand {buildings.Average(b => b.Condition):0.00}");
            var infos = buildings.Select(b => w.Safety.FireRiskOf(b)).ToList();
            output.WriteLine($"Ø Faktoren: Zone {infos.Average(i => i.Zone):0.00}, Rezept {infos.Average(i => i.Recipe):0.00}, Zustand {infos.Average(i => i.Condition):0.00}, " +
                             $"Müll {infos.Average(i => i.Garbage):0.00}, Strom {infos.Average(i => i.Power):0.00}, Größe {infos.Average(i => i.Size):0.00}, Brandschutz {infos.Average(i => i.Prevention):0.00}");
            output.WriteLine($"Hochrechnung ohne ×30: {realPerDay:0.000} Brände/Tag ≈ {realPerDay * SimulationClock.DaysPerYear:0.0} je Spieljahr " +
                             $"(Ø Faktor {factorSum / System.Math.Max(1, buildings.Count):0.00})");
            output.WriteLine($"Kriminalität Ø {w.Safety.AverageCrimeRate:0.000}: {s.Crimes} Taten ({s.Crimes / (float)days:0.0}/Tag; Einbruch {s.Burglaries}, Laden {s.Shopliftings}, " +
                             $"Vandalismus {s.Vandalism}), angezeigt {s.Reported}, Einsätze {s.PoliceCalls}, aufgeklärt {s.Solved} ({s.ClearanceRate * 100f:0} %), " +
                             $"Festnahmen {s.Arrests}, frei mangels Zellen {s.Released}, Schaden {s.CrimeLoss:N0} €, Zellen {w.Safety.InCustody}/{w.Safety.CustodyCapacity}");
            output.WriteLine($"Rechenzeit {sw.Elapsed.TotalMilliseconds / (days * SimulationClock.TicksPerDay):0.00} ms/Tick (alle Systeme)");
            Assert.True(s.Fires > 0);
            Assert.True(s.TotalLosses <= System.Math.Max(1, s.Fires / 8), "mit Feuerwehrabdeckung sind Totalschäden selten");
            Assert.True(s.Crimes > 0);
        }
    }
}
