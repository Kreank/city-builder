using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>G5 Warenketten: Rezepte, Lager, Markt/Preise, Import/Export, Lieferverkehr, Haushaltskonsum, Nachfrage.</summary>
    public class GoodsTests
    {
        readonly ITestOutputHelper output;
        public GoodsTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>
        /// Labor ohne Bürgersystem: Stadt, Verkehr und Warensystem an einer Straße bis zum Kartenrand (ein Außenanschluss).
        /// Gebäude wachsen über die Nachfrage (Einwohnerzahl vorgegeben), Beschäftigte werden direkt gesetzt.
        /// </summary>
        sealed class Lab
        {
            public readonly SimulationWorld W;
            public readonly CitySystem City;
            public readonly TrafficSystem Traffic;
            public readonly GoodsSystem Goods;

            public Lab(Func<string, Vector2, float> deposits = null, bool industry = true, bool retail = true, bool external = true, int residents = 3000)
            {
                W = new SimulationWorld(new WorldSettings());
                Traffic = new TrafficSystem(W.Roads, W.Min, W.Max, 7);
                W.AddSystem(Traffic);
                City = new CitySystem(W.Roads, W.Terrain, W.Min, W.Settings.SizeMeters, 7);
                W.AddSystem(City);
                Goods = new GoodsSystem(City, Traffic, W.Roads, W.Clock, 7);
                if (deposits != null) Goods.DepositAt = deposits;
                W.AddSystem(Goods);
                City.BusinessDemand = Goods.DemandOffset;
                City.Population.Residents = residents; // Nachfrage für Läden/Industrie ohne Bürgersystem
                City.Population.WorkingAge = residents * 6 / 10;
                Road(W, new Vector2(-400, 0), new Vector2(external ? 1020 : 600, 0));
                if (industry) City.Paint(new Vector2(-200, -20), 50f, ZoneType.Industry);
                if (retail) City.Paint(new Vector2(200, 20), 30f, ZoneType.Retail);
                for (int i = 0; i < 60; i++) City.ForceTick();
                foreach (var b in City.Buildings) b.Workers = b.Jobs;
                Goods.ForceStep();
            }

            public List<Firm> Industry => Goods.Firms.Where(f => f.Zone == ZoneType.Industry).ToList();
            public Firm Shop => Goods.Firms.First(f => f.Role == FirmRole.Retail);

            /// <summary>Alle Industriefirmen auf „Kiesgrube“ ohne Vorkommen (tut nichts), die ersten auf die gewünschten Rezepte.</summary>
            public List<Firm> Setup(params string[] recipes)
            {
                var list = Industry;
                Assert.True(list.Count >= recipes.Length, $"nur {list.Count} Industriefirmen");
                for (int i = 0; i < list.Count; i++)
                    Assert.True(Goods.Restructure(list[i], RecipeCatalog.Get(i < recipes.Length ? recipes[i] : "quarry")));
                return list.Take(recipes.Length).ToList();
            }
        }

        static void Empty(Firm f)
        {
            foreach (var g in GoodsCatalog.All)
            {
                f.InputStock[g.Index] = 0f;
                f.InputValue[g.Index] = 0;
                f.OutputStock[g.Index] = 0f;
                f.Incoming[g.Index] = 0f;
            }
        }

        // ------------------------------------------------------------ Katalog

        [Fact]
        public void Catalog_ChainsAreComplete()
        {
            Assert.True(GoodsCatalog.All.Count(g => g.Tier == GoodTier.Raw) >= 7);
            Assert.True(GoodsCatalog.All.Count(g => g.Tier == GoodTier.Intermediate) >= 6);
            Assert.True(GoodsCatalog.All.Count(g => g.Tier == GoodTier.Final) >= 6);
            for (int i = 0; i < GoodsCatalog.Count; i++) Assert.Equal(i, GoodsCatalog.All[i].Index);
            Assert.Equal(GoodsCatalog.Food, GoodsCatalog.ConsumerGoods[0]);
            // Jedes Zwischen-/Endprodukt hat ein Rezept, jeder Eingang wird irgendwo hergestellt
            foreach (var g in GoodsCatalog.All.Where(g => g.Tier != GoodTier.Raw))
                Assert.Contains(RecipeCatalog.Industry, r => r.Produces(g));
            foreach (var r in RecipeCatalog.Industry)
            {
                foreach (var i in r.Inputs) Assert.Contains(RecipeCatalog.Industry, p => p.Produces(i.Good));
                Assert.Equal(1f, r.Workforce.Sum(), 3);
                Assert.True(r.ValueAddedPerJob > 120f, $"{r.Name}: Wertschöpfung {r.ValueAddedPerJob:0}");
                Assert.Equal(r.Role == FirmRole.Extraction, r.Deposit != null);
            }
            Assert.All(GoodsCatalog.All, g => Assert.True(g.LoadUnits > 1f));
        }

        // ------------------------------------------------------------ Rezepte

        [Fact]
        public void Recipe_ProducesOutputsFromInputs()
        {
            var lab = new Lab(retail: false);
            var mill = lab.Setup("mill")[0];
            Assert.Equal(FirmRole.Processing, mill.Role);
            float grainBefore = mill.InputOf(GoodsCatalog.Grain);
            Assert.True(grainBefore > 0f, "Erstausstattung im Eingangslager erwartet");

            float marketBefore = lab.Goods.Market(GoodsCatalog.Flour).Today.Produced;
            lab.Goods.ForceStep();

            float jobs = mill.Jobs;
            Assert.Equal(FirmStatus.Producing, mill.Status);
            Assert.Equal(1f, mill.Productivity, 3);
            Assert.Equal(3.7f * jobs / 24f, mill.OutputOf(GoodsCatalog.Flour), 3);
            // Eingang verbraucht (Nachbestellung ist erst unterwegs: noch nicht im Lager)
            Assert.Equal(grainBefore - 4f * jobs / 24f, mill.InputOf(GoodsCatalog.Grain), 3);
            Assert.True(mill.Today.InputCost > 0, "Wareneinsatz gebucht");
            Assert.Equal(3.7f * jobs / 24f, mill.Today.Produced, 3);
            Assert.Equal(3.7f * jobs / 24f, lab.Goods.Market(GoodsCatalog.Flour).Today.Produced - marketBefore, 3);
        }

        [Fact]
        public void Recipe_MissingInputStopsProduction()
        {
            var lab = new Lab(retail: false);
            var steel = lab.Setup("steelworks")[0];
            steel.InputStock[GoodsCatalog.Coal.Index] = 0f; // Erz da, Kohle fehlt
            float ore = steel.InputOf(GoodsCatalog.Ore);
            Assert.True(ore > 0f);

            lab.Goods.ForceStep();

            Assert.Equal(FirmStatus.MissingInput, steel.Status);
            Assert.Equal(0f, steel.OutputOf(GoodsCatalog.Steel));
            Assert.Equal(ore, steel.InputOf(GoodsCatalog.Ore));
            // Die fehlende Kohle wurde bestellt
            Assert.True(steel.IncomingOf(GoodsCatalog.Coal) > 0f);

            // Ohne Beschäftigte steht die Fabrik ebenfalls
            var mill = lab.Industry[1];
            lab.Goods.Restructure(mill, RecipeCatalog.Get("mill"));
            mill.Building.Workers = 0;
            lab.Goods.ForceStep();
            Assert.Equal(FirmStatus.NoWorkers, mill.Status);
            Assert.Equal(0f, mill.OutputOf(GoodsCatalog.Flour));
        }

        [Fact]
        public void Extraction_NeedsDeposit_AndSitingFollowsDeposits()
        {
            // Erz überall reich, sonst nichts → Industrie wird Bergbau; Entnahme begrenzt die Förderung
            float extracted = 0f;
            // Ohne Einwohner gibt es keinen Konsumbedarf – dann entscheidet das Vorkommen
            var lab = new Lab(deposits: (id, p) => id == ResourceIds.Ore ? 0.9f : 0f, retail: false, residents: 0);
            lab.Goods.Extract = (id, p, amount) => { extracted += MathF.Min(amount, 1f); return MathF.Min(amount, 1f); };
            var firms = lab.Industry;
            Assert.NotEmpty(firms);
            output.WriteLine(string.Join(", ", firms.Select(f => f.Recipe.Name)));
            Assert.Equal("ore_mine", firms[0].Recipe.Id);
            Assert.All(firms, f => Assert.True(f.Recipe.Deposit == null || f.Recipe.Deposit == ResourceIds.Ore));

            var mine = firms[0];
            Empty(mine);
            lab.Goods.ForceStep();
            Assert.Equal(1f, mine.OutputOf(GoodsCatalog.Ore), 3); // Entnahme liefert höchstens 1 je Stunde
            Assert.Equal(FirmStatus.NoDeposit, mine.Status);
            Assert.True(extracted >= 1f);

            // Farm ohne Fruchtbarkeit produziert nichts
            lab.Goods.Restructure(mine, RecipeCatalog.Get("farm"));
            lab.Goods.ForceStep();
            Assert.Equal(FirmStatus.NoDeposit, mine.Status);
            Assert.Equal(0f, mine.OutputOf(GoodsCatalog.Grain));
        }

        // ------------------------------------------------------------ Welt: Vorkommen und Ernte

        [Fact]
        public void Mining_DepletesOreDeposit_FromWorldResources()
        {
            var lab = new Lab(retail: false);
            var res = lab.W.Resources;
            GoodsBinding.ConnectWorld(lab.Goods, res, lab.W.Climate);
            var firm = lab.Industry[0];
            var pos = firm.Building.Position;
            // Erz um den Betrieb (3×3 Zellen) auf 0,3 setzen, damit es in überschaubarer Zeit erschöpft ist
            var layer = res.Layer(ResourceIds.Ore);
            Assert.True(layer.TryWorldToCell(pos, out int cx, out int cy));
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++) layer.RawValues[(cy + dy) * layer.Width + cx + dx] = 0.3f;
            layer.MarkChanged();
            double before = res.TotalAmount(ResourceIds.Ore);
            Assert.True(lab.Goods.Restructure(firm, RecipeCatalog.Get("ore_mine")));
            Assert.Equal(0.3f, firm.DepositQuality, 3);

            float produced = 0f;
            int day = 0;
            for (; day < 200 && firm.Status != FirmStatus.NoDeposit; day++)
            {
                for (int h = 0; h < GoodsRules.StepsPerDay; h++) lab.Goods.ForceStep();
                produced += firm.Today.Produced;
                lab.Goods.ForceCloseDay();
            }
            double after = res.TotalAmount(ResourceIds.Ore);
            output.WriteLine($"Erz gefördert {produced:0} E in {day} Tagen, Karte −{before - after:0} t, Güte jetzt {lab.Goods.DepositQualityAt(ResourceIds.Ore, pos):0.000}");
            Assert.True(produced > 100f);
            // Wareneinheit Erz = 1 t (Gewicht) → Karte verliert genau die geförderte Menge
            Assert.Equal(produced * GoodsCatalog.Ore.Weight, before - after, 0);
            Assert.Equal(FirmStatus.NoDeposit, firm.Status);
            Assert.True(lab.Goods.DepositQualityAt(ResourceIds.Ore, pos) < RecipeCatalog.Get("ore_mine").MinDeposit);
            // Erschöpft → keine Förderung mehr
            lab.Goods.ForceStep();
            Assert.Equal(0f, firm.Today.Produced);
        }

        [Fact]
        public void Harvest_FollowsSeason()
        {
            var lab = new Lab(retail: false);
            GoodsBinding.ConnectWorld(lab.Goods, lab.W.Resources, lab.W.Climate);
            var farm = lab.Setup("farm")[0];
            Assert.True(farm.DepositQuality >= farm.Recipe.MinDeposit, $"Fruchtbarkeit {farm.DepositQuality:0.00}");
            var climate = lab.W.Climate;
            // Mittel über das Jahr ≈ Klima-Faktor, Winter (Januar) deutlich unter Sommer (Juli)
            float mean = 0f;
            for (int d = 0; d < SimulationClock.DaysPerYear; d++) mean += lab.Goods.HarvestFactor(d);
            mean /= SimulationClock.DaysPerYear;
            float scale = Math.Clamp(climate.AnnualHarvestFactor / GoodsRules.HarvestReference, 0.4f, 1.2f);
            Assert.Equal(scale, mean, 2);

            float ProducedOn(int dayOfYear)
            {
                while (lab.W.Clock.Day % SimulationClock.DaysPerYear != dayOfYear) for (int i = 0; i < SimulationClock.TicksPerDay; i++) lab.W.Clock.Advance();
                farm.Today.Clear();
                farm.OutputStock[GoodsCatalog.Grain.Index] = 0f;
                farm.OutputStock[GoodsCatalog.Livestock.Index] = 0f;
                lab.Goods.ForceStep();
                return farm.Today.Produced;
            }
            float winter = ProducedOn(15), summer = ProducedOn(195);
            output.WriteLine($"Ernte je Stunde Winter {winter:0.00}, Sommer {summer:0.00}, Jahresmittel-Faktor {mean:0.00}");
            Assert.True(summer > winter * 1.5f, "Sommer bringt deutlich mehr als Winter");
            Assert.True(summer > 0f);
        }

        // ------------------------------------------------------------ Markt & Preise

        [Fact]
        public void Price_RisesWithShortage_FallsWithSurplus()
        {
            var g = GoodsCatalog.Steel;
            var scarce = new GoodMarket(g);
            var plenty = new GoodMarket(g);
            float last = scarce.LocalPrice;
            for (int day = 0; day < 10; day++)
            {
                scarce.CloseDay(stock: 0f, demandToday: 100f, worldPrice: g.BasePrice);
                plenty.CloseDay(stock: 2000f, demandToday: 100f, worldPrice: g.BasePrice);
                Assert.True(scarce.LocalPrice >= last - 1e-3f, "Preis steigt (träge) bei Knappheit");
                last = scarce.LocalPrice;
            }
            output.WriteLine($"knapp {scarce.LocalPrice:0.0}, Überschuss {plenty.LocalPrice:0.0}, Grundpreis {g.BasePrice}");
            Assert.True(scarce.LocalPrice > g.BasePrice * 1.05f);
            Assert.True(plenty.LocalPrice < g.BasePrice * 0.95f);
            // Weltmarkt-Parität begrenzt beide Richtungen
            Assert.True(scarce.LocalPrice <= g.BasePrice * (1f + GoodsRules.ParityAbove) + 1e-3f);
            Assert.True(plenty.LocalPrice >= g.BasePrice * (1f - GoodsRules.ParityBelow) - 1e-3f);
            // Ein Tag Knappheit allein springt nicht bis zum Ziel (Trägheit)
            var one = new GoodMarket(g);
            one.CloseDay(0f, 100f, g.BasePrice);
            Assert.True(one.LocalPrice < GoodMarket.TargetPrice(g, 0f, 300f) - 1f);
        }

        [Fact]
        public void WorldPrice_FluctuatesDeterministically()
        {
            var g = GoodsCatalog.Electronics;
            var a = Enumerable.Range(0, 120).Select(d => GoodMarket.WorldPriceOn(g, d, 5)).ToArray();
            var b = Enumerable.Range(0, 120).Select(d => GoodMarket.WorldPriceOn(g, d, 5)).ToArray();
            Assert.Equal(a, b);
            Assert.All(a, p => Assert.InRange(p, g.BasePrice * (1f - GoodsRules.WorldVolatility) - 0.01f, g.BasePrice * (1f + GoodsRules.WorldVolatility) + 0.01f));
            Assert.True(a.Max() - a.Min() > g.BasePrice * 0.02f, "Weltmarkt schwankt");
            for (int d = 1; d < a.Length; d++) Assert.True(MathF.Abs(a[d] - a[d - 1]) < g.BasePrice * 0.03f, "weich, ohne Sprünge");
        }

        [Fact]
        public void Import_WhenCheaperThanLocal_OtherwiseLocal()
        {
            var lab = new Lab(retail: false);
            var firms = lab.Setup("mill", "food_factory");
            var mill = firms[0];
            var food = firms[1];
            var flour = GoodsCatalog.Flour;
            var m = lab.Goods.Market(flour);
            Assert.Single(lab.Traffic.ExternalConnections);

            // Lokal deutlich teurer als Weltmarkt + Fracht → Import, obwohl die Mühle Mehl hat
            mill.OutputStock[flour.Index] = 100f;
            food.InputStock[flour.Index] = 0f;
            m.LocalPrice = m.WorldPrice * 1.5f;
            lab.Goods.ForceStep();
            var shipments = lab.Goods.Shipments.Where(s => s.Good == flour && s.ToBuildingId == food.BuildingId).ToList();
            Assert.NotEmpty(shipments);
            Assert.All(shipments, s => Assert.Equal(ShipmentKind.Import, s.Kind));
            Assert.All(shipments, s => Assert.Equal(lab.Traffic.ExternalConnections[0].Id, s.ConnectionNodeId));
            Assert.True(m.Today.Imported > 0f && m.Today.ImportValue > 0);
            Assert.DoesNotContain(lab.Goods.Shipments, s => s.Kind == ShipmentKind.Local && s.FromBuildingId == mill.BuildingId);
            Assert.True(mill.OutputOf(flour) >= 100f); // nichts lokal verkauft (nur die Stundenproduktion kam dazu)

            // Lokal günstiger → Kauf bei der Mühle
            var food2 = firms[1];
            food2.Incoming[flour.Index] = 0f;
            food2.InputStock[flour.Index] = 0f;
            m.LocalPrice = m.WorldPrice * 0.9f;
            int before = lab.Goods.Shipments.Count;
            lab.Goods.ForceStep();
            var local = lab.Goods.Shipments.Skip(before).Where(s => s.Good == flour && s.ToBuildingId == food.BuildingId).ToList();
            Assert.NotEmpty(local);
            Assert.Equal(ShipmentKind.Local, local[0].Kind);
            Assert.Equal(mill.BuildingId, local[0].FromBuildingId);
            Assert.True(mill.OutputOf(flour) < 100f);
            Assert.True(mill.Today.Revenue > 0);
        }

        [Fact]
        public void Export_WhenSurplus()
        {
            var lab = new Lab(retail: false);
            var mill = lab.Setup("mill")[0];
            var flour = GoodsCatalog.Flour;
            float cap = mill.OutputCapacityOf(flour);
            mill.OutputStock[flour.Index] = cap * 0.95f; // Lager fast voll, niemand kauft
            double cash = mill.Cash;

            lab.Goods.ForceStep();

            var exports = lab.Goods.Shipments.Where(s => s.Kind == ShipmentKind.Export && s.FromBuildingId == mill.BuildingId).ToList();
            Assert.NotEmpty(exports);
            Assert.True(mill.OutputOf(flour) < cap * 0.95f);
            var m = lab.Goods.Market(flour);
            Assert.True(m.Today.Exported > 0f && m.Today.ExportValue > 0);
            Assert.True(mill.Today.Exported > 0f && mill.Today.Revenue > 0);
            Assert.True(mill.Today.Transport > 0, "Fracht trägt der Verkäufer");

            // Halbvolles Lager bei schlechtem Weltmarkt: kein Notverkauf
            var lab2 = new Lab(retail: false);
            var mill2 = lab2.Setup("mill")[0];
            mill2.OutputStock[flour.Index] = mill2.OutputCapacityOf(flour) * 0.6f;
            lab2.Goods.Market(flour).WorldPrice = flour.BasePrice * 0.5f;
            lab2.Goods.ForceStep();
            Assert.DoesNotContain(lab2.Goods.Shipments, s => s.Kind == ShipmentKind.Export);
        }

        // ------------------------------------------------------------ Lieferverkehr

        [Fact]
        public void Delivery_TruckTripBooksIntoStorage()
        {
            var lab = new Lab(retail: false);
            var firms = lab.Setup("mill", "food_factory");
            var mill = firms[0];
            var food = firms[1];
            var flour = GoodsCatalog.Flour;
            lab.Traffic.Demand = 3f; // Lkw-Anteil 0,35 × 3 > 1 → jede Lieferung als echter Lkw
            mill.OutputStock[flour.Index] = 60f;
            food.InputStock[flour.Index] = 0f;
            food.InputValue[flour.Index] = 0;
            lab.Goods.Market(flour).LocalPrice = lab.Goods.Market(flour).WorldPrice * 0.9f;

            lab.Goods.ForceStep();
            var s = lab.Goods.Shipments.First(x => x.Good == flour && x.ToBuildingId == food.BuildingId);
            Assert.Equal(ShipmentKind.Local, s.Kind);
            Assert.True(s.IsTruck, "Lieferung als Lkw erwartet");
            Assert.True(s.TripId < 0, "Lkw-Fahrten haben negative Fahrt-Ids");
            var v = lab.Traffic.Vehicles.First(x => x.TripId == s.TripId);
            Assert.Equal(VehicleClass.Truck, v.Class);
            lab.W.Substep(); // Statistik zählt Lkw getrennt von Bürgerfahrzeugen (Zeitmodell: ein Verkehrsschritt – ein Tick sind 30 s, da wäre der Lkw schon da)
            Assert.Equal(0, lab.Traffic.Stats.CitizenVehicles);
            Assert.True(lab.Traffic.Stats.DeliveryVehicles >= 1);
            Assert.Same(s, lab.Goods.ShipmentForTrip(s.TripId));
            Assert.True(food.IncomingOf(flour) >= s.Amount - 1e-3f, "Bestellung ist unterwegs");
            Assert.Equal(0f, food.InputOf(flour));

            for (int i = 0; i < SimulationClock.TicksPerDay * 2 && !s.IsDone; i++) lab.W.Step();
            Assert.True(s.IsDone, "Lkw ist angekommen");
            Assert.True(food.InputOf(flour) >= s.Amount - 1e-3f, "Ankunft bucht ins Ziellager");
            Assert.True(food.AverageInputCost(flour) > 0);
            output.WriteLine($"Lkw: {s.Amount:0.0} Mehl, Transport {s.TransportCost:0.00} € (Verkehrszeit), Fahrzeuge aktiv {lab.Goods.Stats.ActiveTrucks}");
        }

        [Fact]
        public void Delivery_PlaceholderAndVehicleLimit()
        {
            var lab = new Lab(retail: false);
            var firms = lab.Setup("mill", "food_factory");
            var flour = GoodsCatalog.Flour;
            lab.Traffic.Demand = 3f;
            lab.Traffic.MaxVehicles = 0; // Fahrzeuglimit erreicht → nur Platzhalter
            firms[0].OutputStock[flour.Index] = 60f;
            firms[1].InputStock[flour.Index] = 0f;
            lab.Goods.Market(flour).LocalPrice = lab.Goods.Market(flour).WorldPrice * 0.9f;
            lab.Goods.ForceStep();
            var s = lab.Goods.Shipments.First(x => x.Good == flour && x.ToBuildingId == firms[1].BuildingId);
            Assert.False(s.IsTruck);
            Assert.True(s.ArriveTick > lab.W.Clock.Tick);
            Run(lab.W, (int)(s.ArriveTick - lab.W.Clock.Tick) + GoodsRules.TickInterval);
            Assert.True(s.IsDone);
            Assert.True(firms[1].InputOf(flour) >= s.Amount - 1e-3f);
        }

        // ------------------------------------------------------------ Haushalte

        [Fact]
        public void Household_BuysByTier()
        {
            var lab = new Lab(industry: false);
            var shop = lab.Shop;
            foreach (var g in GoodsCatalog.ConsumerGoods) { shop.InputStock[g.Index] = 50f; shop.InputValue[g.Index] = 50f * g.BasePrice; }
            var goods = lab.Goods;

            Assert.Equal(ConsumerTier.Basic, GoodsSystem.HighestTier(80f, 0.9f));
            Assert.Equal(ConsumerTier.Comfort, GoodsSystem.HighestTier(150f, 0.6f));
            Assert.Equal(ConsumerTier.Basic, GoodsSystem.HighestTier(150f, 0.3f)); // unzufrieden → nur Grundbedarf
            Assert.Equal(ConsumerTier.Luxury, GoodsSystem.HighestTier(300f, 0.8f));

            float Bought(GoodType g, Household h)
            {
                float before = shop.InputOf(g);
                Assert.Equal(1f, goods.Purchase(h, shop.BuildingId), 3);
                return before - shop.InputOf(g);
            }
            var poor = new Household { Income = 80f, Satisfaction = 0.8f };
            var middle = new Household { Income = 150f, Satisfaction = 0.7f };
            var rich = new Household { Income = 320f, Satisfaction = 0.8f };
            Assert.True(Bought(GoodsCatalog.Food, poor) > 0f);
            Assert.Equal(0f, Bought(GoodsCatalog.Clothing, poor));
            Assert.True(Bought(GoodsCatalog.Clothing, middle) > 0f);
            Assert.Equal(0f, Bought(GoodsCatalog.Electronics, middle));
            Assert.True(Bought(GoodsCatalog.Electronics, rich) > 0f);
            Assert.True(shop.Today.Revenue > 0);
            Assert.True(goods.Market(GoodsCatalog.Food).Today.HouseholdBought > 0f);

            // Leeres Regal: Einkauf scheitert, das Einkaufsbedürfnis bleibt (knapp unter 1)
            foreach (var g in GoodsCatalog.ConsumerGoods) shop.InputStock[g.Index] = 0f;
            float f = goods.Purchase(poor, shop.BuildingId);
            Assert.Equal(0f, f, 3);
            Assert.Equal(0.99f, CitizenSystem.ShoppingNeedAfter(1.3f, f), 3);
            Assert.Equal(0f, CitizenSystem.ShoppingNeedAfter(1.3f, 1f), 3);
            // Nur Komfort fehlt: Grundbedarf erfüllt, Erfüllung zwischen 0 und 1
            shop.InputStock[GoodsCatalog.Food.Index] = 10f;
            float partial = goods.Purchase(middle, shop.BuildingId);
            Assert.InRange(partial, 0.5f, 0.95f);
        }

        [Fact]
        public void Household_ShoppingNeedFollowsShopStock()
        {
            // Ganze Stadt: Läden werden beliefert, Einkäufe gelingen meist; leert man die Regale, bleibt der Bedarf
            var w = Town(days: 4);
            var s = w.Goods.Stats;
            output.WriteLine($"Einkäufe gestern {s.PurchasesYesterday}, erfolgreich {s.SuccessfulPurchasesYesterday}");
            Assert.True(s.PurchasesYesterday > 0);
            Assert.True(s.SuccessfulPurchasesYesterday >= s.PurchasesYesterday * 0.8f);

            // Lieferstopp: alle Regale (auch neuer Läden) bleiben zwei Tage leer
            for (int t = 0; t < SimulationClock.TicksPerDay * 2; t++)
            {
                foreach (var f in w.Goods.Firms.Where(f => f.Role == FirmRole.Retail))
                    foreach (var g in GoodsCatalog.ConsumerGoods) f.InputStock[g.Index] = 0f;
                w.Step();
            }
            Assert.Equal(0, s.SuccessfulPurchasesYesterday);
            Assert.All(w.Goods.Firms.Where(f => f.Role == FirmRole.Retail), f => Assert.False(w.Goods.ShopHasStock(f.BuildingId)));
            float avgNeed = w.Citizens.Citizens.Where(c => !c.IsCommuterIn && c.Age >= Citizen.AdultAge).Average(c => c.Need(NeedKind.Shopping));
            output.WriteLine($"Bedürfnis Einkauf Ø {avgNeed:0.00}");
            Assert.True(avgNeed < 0.6f);
        }

        // ------------------------------------------------------------ Gewinn → Nachfrage, Steuer

        [Fact]
        public void Profit_AffectsZoneDemand()
        {
            var lab = new Lab(retail: false);
            var firms = lab.Industry;
            void SetMargin(double margin)
            {
                foreach (var f in firms) { f.AgeDays = 10; f.RevenueEma = 10000; f.ProfitEma = 10000 * margin; }
            }
            SetMargin(0.2);
            float good = lab.Goods.DemandOffset(ZoneType.Industry);
            lab.City.ForceTick();
            float demandGood = lab.City.Demand[ZoneType.Industry];
            SetMargin(-0.2);
            float bad = lab.Goods.DemandOffset(ZoneType.Industry);
            lab.City.ForceTick();
            float demandBad = lab.City.Demand[ZoneType.Industry];
            output.WriteLine($"Zuschlag Gewinn {good:+0.0;-0.0}, Verlust {bad:+0.0;-0.0}; Nachfrage {demandGood:0.0} vs {demandBad:0.0}");
            Assert.True(good > 10f);
            Assert.True(bad < -10f);
            Assert.True(demandGood > demandBad);
            // Junge Firmen zählen nicht (neutral)
            foreach (var f in firms) f.AgeDays = 0;
            Assert.Equal(0f, lab.Goods.DemandOffset(ZoneType.Office));
            Assert.Equal(0f, lab.Goods.DemandOffset(ZoneType.Industry), 3);
        }

        [Fact]
        public void Bankruptcy_AbandonsBuilding()
        {
            var lab = new Lab(retail: false);
            var f = lab.Industry[0];
            int id = f.BuildingId;
            f.AgeDays = 30;
            f.LossDays = GoodsRules.BankruptAfterLossDays + 1;
            f.ProfitEma = -5000;
            f.Cash = -f.CreditLimit * 2;
            lab.Goods.ForceCloseDay();
            Assert.Null(lab.City.GetBuilding(id));
            Assert.Null(lab.Goods.FirmAt(id));
            Assert.Equal(1, lab.Goods.Stats.Bankruptcies);
        }

        [Fact]
        public void BusinessTax_ComesFromProfit()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false });
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            Run(w, 60);
            var firms = w.Goods.Firms.Where(f => f.Building.IsConstructed).ToList();
            Assert.NotEmpty(firms);
            firms[0].Yesterday.Revenue = 5000; // 5000 € Gewinn
            if (firms.Count > 1) firms[1].Yesterday.Wages = 3000; // Verlust → keine Steuer
            double expected = 5000 * EconomyRules.ProfitTaxMultiplier * w.Economy.TaxRate(TaxKind.Industry);
            Assert.Equal(expected, w.Economy.EstimateDay().Amount(BudgetCategory.IndustryTax), 3);
            w.Economy.SetTaxRate(TaxKind.Industry, 0.2f);
            Assert.Equal(expected * 2, w.Economy.EstimateDay().Amount(BudgetCategory.IndustryTax), 3);
            // Ohne Warensystem bleibt die alte Pauschale je Job
            w.Economy.TaxableProfit = null;
            int workers = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.Industry).Sum(b => b.Workers);
            Assert.Equal(workers * EconomyRules.IndustryJobTaxBase * 0.2, w.Economy.EstimateDay().Amount(BudgetCategory.IndustryTax), 3);
        }

        // ------------------------------------------------------------ Ganze Stadt

        /// <summary>Stadt an einer Straße bis zum Kartenrand (Außenanschluss) mit Wohnen, Industrie, Läden, Büros.</summary>
        static SimulationWorld Town(int seed = 12345, int days = 5)
        {
            var w = SimulationWorld.CreateDefault(seed: seed);
            Road(w, new Vector2(-300, 0), new Vector2(1020, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
            w.City.Paint(new Vector2(400, 20), 40f, ZoneType.Office);
            Run(w, SimulationClock.TicksPerDay * days);
            return w;
        }

        [Fact]
        public void Town_ChainsTradeAndTrucks()
        {
            var w = Town(days: 8);
            Dump(w);
            var g = w.Goods;
            Assert.Contains(g.Firms, f => f.Recipe.IsIndustry);
            Assert.Contains(g.Firms, f => f.Role == FirmRole.Retail);
            Assert.True(g.TradeTotal.ImportValue > 0, "Import erwartet");
            Assert.True(g.TradeTotal.ExportValue > 0, "Export erwartet");
            Assert.True(g.Stats.ShipmentsYesterday > 0);
            Assert.True(g.Markets.Sum(m => m.Yesterday.Produced) > 0f);
            // Bürgerfahrten und Lkw kommen sich nicht in die Quere
            Assert.All(w.Traffic.Vehicles.Where(v => v.TripId < 0), v => Assert.Equal(VehicleClass.Truck, v.Class));
            Assert.All(w.Traffic.Vehicles.Where(v => v.TripId < 0), v => Assert.Null(w.Citizens.CitizenForTrip(v.TripId)));
            // Abfrage-API
            var any = g.Firms.First(f => f.Recipe.IsIndustry && f.Building.IsConstructed);
            var list = new List<Shipment>();
            g.ShipmentsOf(any.BuildingId, list);
            Assert.All(list, s => Assert.True(s.FromBuildingId == any.BuildingId || s.ToBuildingId == any.BuildingId));
            Assert.NotNull(g.FirmAt(any.BuildingId));
            // Strom/Wasser der Industrie aus dem Rezept (Hook UtilitySystem.DemandProvider), Wohnen weiter nach Katalog
            Assert.Equal(any.Recipe.PowerPerJob * any.Jobs, w.Utilities.DemandOf(any.Building, Simulation.Utilities.UtilityKind.Power), 3);
            Assert.Equal(any.Recipe.WaterPerJob * any.Jobs, w.Utilities.DemandOf(any.Building, Simulation.Utilities.UtilityKind.Water), 3);
            var home = w.City.Buildings.First(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow);
            Assert.Equal(Simulation.Utilities.UtilityCatalog.Demand(home, Simulation.Utilities.UtilityKind.Power), w.Utilities.DemandOf(home, Simulation.Utilities.UtilityKind.Power));
            Assert.Null(g.FirmAt(w.City.Buildings.First(b => Zones.IsResidential(b.Zone) && b.Zone != ZoneType.Mixed).Id));
        }

        [Fact]
        public void Goods_AreDeterministic()
        {
            string Fingerprint()
            {
                var w = Town(seed: 99, days: 6);
                var g = w.Goods;
                var parts = new List<string>();
                foreach (var f in g.Firms) parts.Add($"{f.BuildingId}:{f.Recipe.Id}:{f.Cash:R}:{f.Yesterday.Profit:R}:{f.OutputStock.Sum():R}");
                foreach (var m in g.Markets) parts.Add($"{m.Good.Id}:{m.LocalPrice:R}:{m.Stock:R}:{m.Yesterday.Imported:R}:{m.Yesterday.Exported:R}");
                parts.Add($"{g.Stats.ShipmentsYesterday}:{g.Stats.TrucksYesterday}:{g.TradeTotal.ImportValue:R}:{g.TradeTotal.ExportValue:R}:{w.Economy.Balance:R}");
                return string.Join("|", parts);
            }
            Assert.Equal(Fingerprint(), Fingerprint());
        }

        void Dump(SimulationWorld w)
        {
            var g = w.Goods;
            output.WriteLine($"Tag {w.Clock.Day}: Einwohner {w.Citizens.Stats.Population}, Firmen {g.Firms.Count}, Lieferungen gestern {g.Stats.ShipmentsYesterday} " +
                             $"(Lkw {g.Stats.TrucksYesterday}), aktiv {g.Stats.ActiveShipments}, Import {g.TradeYesterday.ImportValue:0} €, Export {g.TradeYesterday.ExportValue:0} €, " +
                             $"Einkäufe {g.Stats.SuccessfulPurchasesYesterday}/{g.Stats.PurchasesYesterday}");
            foreach (var f in g.Firms)
                output.WriteLine($"  {f} [{Firm.StatusText(f.Status)}] Jobs {f.Workers}/{f.Jobs} P {f.Productivity:0.00} Kasse {f.Cash:0} " +
                                 $"Gewinn gestern {f.Yesterday.Profit:0} (Erlös {f.Yesterday.Revenue:0}, Einsatz {f.Yesterday.InputCost:0}, Lohn {f.Yesterday.Wages:0}, Transport {f.Yesterday.Transport:0})");
            foreach (var m in g.Markets)
                if (m.DemandPerDay > 0 || m.Stock > 0 || m.Yesterday.Produced > 0)
                    output.WriteLine($"  {m.Good.Name,-14} lokal {m.LocalPrice,7:0.0} Welt {m.WorldPrice,7:0.0} Bestand {m.Stock,7:0.0}/{m.TargetStock,7:0.0} " +
                                     $"prod {m.Yesterday.Produced,6:0.0} verbr {m.Yesterday.Consumed,6:0.0} HH {m.Yesterday.HouseholdBought,5:0.0}/{m.Yesterday.HouseholdWanted,5:0.0} " +
                                     $"imp {m.Yesterday.Imported,6:0.0} exp {m.Yesterday.Exported,6:0.0}");
            output.WriteLine($"  Nachfrage-Zuschlag: Industrie {g.DemandOffset(ZoneType.Industry):+0.0;-0.0}, Handel {g.DemandOffset(ZoneType.Retail):+0.0;-0.0}, " +
                             $"Büro {g.DemandOffset(ZoneType.Office):+0.0;-0.0}");
        }

        // ------------------------------------------------------------ Leistung (nur Information)

        [Fact]
        public void Performance_200Firms_10000Citizens_PerGameDay()
        {
            var w = SimulationWorld.CreateDefault();
            for (int i = 0; i < 5; i++) Road(w, new Vector2(-900, -400 + i * 200), new Vector2(1020, -400 + i * 200));
            // Zonen ohne Überlappung (Umwidmen würde Gebäude abreißen): Wohnen | Büro | Industrie | Handel | Industrie
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-600, -400 + i * 200), 290f, ZoneType.ResidentialMedium);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-200, -400 + i * 200), 75f, ZoneType.Office);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(250, -400 + i * 200), 320f, ZoneType.Industry);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(700, -400 + i * 200), 110f, ZoneType.Retail);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(920, -400 + i * 200), 85f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 2);
            var housing = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).ToList();
            Assert.NotEmpty(housing);
            int k = 0;
            while (w.Citizens.Citizens.Count < 10000) w.Citizens.CreateHousehold(housing[k++ % housing.Count]);
            w.Citizens.ForceManage();
            Run(w, SimulationClock.TicksPerDay * 5); // Firmen wachsen mit der Bevölkerung (Nachfrage)

            var sw = Stopwatch.StartNew();
            Run(w, SimulationClock.TicksPerDay);
            sw.Stop();
            var g = w.Goods;
            output.WriteLine($"{w.Citizens.Citizens.Count} Bürger, {g.Firms.Count} Firmen ({g.Stats.Extraction} Gewinnung, {g.Stats.Processing} Verarbeitung, " +
                             $"{g.Stats.Manufacturing} Fertigung, {g.Stats.Retail} Handel, {g.Stats.Office} Büro), Fahrzeuge {w.Traffic.Vehicles.Count}, " +
                             $"Lkw {g.Stats.ActiveTrucks}, Lieferungen gestern {g.Stats.ShipmentsYesterday} (Lkw {g.Stats.TrucksYesterday}, ohne Weg/Platz {g.Stats.TruckFailedYesterday}, " +
                             $"Platzhalter {g.Stats.PlaceholderYesterday}), " +
                             $"Einkäufe {g.Stats.SuccessfulPurchasesYesterday}/{g.Stats.PurchasesYesterday}, Import {g.TradeYesterday.ImportValue:0} €, Export {g.TradeYesterday.ExportValue:0} €");
            var gw = Stopwatch.StartNew();
            for (int i = 0; i < GoodsRules.StepsPerDay; i++) w.Goods.ForceStep();
            w.Goods.ForceCloseDay();
            gw.Stop();
            output.WriteLine($"Ein Spieltag (240 Ticks, alle Systeme): {sw.Elapsed.TotalMilliseconds:0} ms = {sw.Elapsed.TotalMilliseconds / 240:0.00} ms/Tick");
            output.WriteLine($"Nur Warensystem (24 Stunden-Schritte + Tageswechsel): {gw.Elapsed.TotalMilliseconds:0.0} ms");
            Assert.True(g.Firms.Count >= 150, $"nur {g.Firms.Count} Firmen");
            Assert.True(sw.Elapsed.TotalMilliseconds < 20000, "grobe Obergrenze, damit Ausreißer auffallen");
        }
    }
}
