using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.World;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Etappe 8 Stufe 1: Handel zwischen Ländern (Weltpreis, Transport, Verträge, NPC-KI, Zölle, Stadtkasse).</summary>
    public class TradeTests
    {
        readonly ITestOutputHelper output;
        public TradeTests(ITestOutputHelper output) => this.output = output;

        static readonly PlanetSettings Small = new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 };

        /// <summary>Eigene Welt je Test (die Länder-Volkswirtschaften werden verändert).</summary>
        static Planet NewPlanet(int seed = 31, PlanetSettings settings = null)
        {
            var p = PlanetGenerator.Generate(seed, settings ?? Small);
            p.SetStartRegion(p.SuggestStartRegion());
            return p;
        }

        static TradeSystem NewTrade(Planet p, int seed = 5) => new TradeSystem(p, null, seed);

        static int Npc(TradeSystem t, int skip = 0) => t.Nations.Where(n => !n.IsPlayer).Skip(skip).First().Id;

        static void Days(TradeSystem t, int days)
        {
            for (int i = 0; i < days; i++) t.SimulateDay();
        }

        // ------------------------------------------------------------ Abbildung & Preise

        [Fact]
        public void Goods_MapToCategories_ValueEquivalent()
        {
            foreach (var g in GoodsCatalog.All)
            {
                var c = TradeGoods.CategoryOf(g);
                Assert.True(TradeGoods.IsTradeable(c));
                Assert.Contains(g, TradeGoods.GoodsOf(c));
                Assert.Equal(g.BasePrice / GoodsCategories.BasePrice[(int)c], TradeGoods.CategoryUnitsPerGood(g), 6);
            }
            Assert.False(TradeGoods.IsTradeable(GoodsCategory.Tourism));
            Assert.Equal(GoodsCategory.Food, TradeGoods.CategoryOf(GoodsCatalog.Grain));
            Assert.Equal(GoodsCategory.Energy, TradeGoods.CategoryOf(GoodsCatalog.Oil));
            Assert.Equal(GoodsCategory.Technology, TradeGoods.CategoryOf(GoodsCatalog.Electronics));
            Assert.Equal(19, Enumerable.Range(0, GoodsCategories.Count).Sum(c => TradeGoods.GoodsOf((GoodsCategory)c).Count));
        }

        [Fact]
        public void WorldPrice_RisesWhenCountriesRunShort()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            t.AiEnabled = false;
            Days(t, 5);
            var grain = GoodsCatalog.Grain;
            double before = t.WorldPrice(grain);
            double steelBefore = t.WorldPrice(GoodsCatalog.Steel);
            int food = (int)GoodsCategory.Food;
            for (int d = 0; d < 25; d++)
            {
                foreach (var n in p.Economy.Nations) n.Stock[food] = 0; // Missernte überall
                t.SimulateDay();
            }
            double after = t.WorldPrice(grain);
            output.WriteLine($"Getreide Weltpreis {before:0.00} → {after:0.00} €, Stahl {steelBefore:0.00} → {t.WorldPrice(GoodsCatalog.Steel):0.00} €");
            Assert.True(after > before * 1.3, $"Weltpreis soll bei Knappheit deutlich steigen: {before:0.00} → {after:0.00}");
            // Länderpreis folgt dem Kategorie-Index
            int a = Npc(t);
            Assert.Equal(grain.BasePrice * p.Countries[a].Economy.Price[food] / GoodsCategories.BasePrice[food],
                t.CountryPrice(a, grain), grain.BasePrice * TradeRules.GoodPriceSpread * 3);
        }

        [Fact]
        public void TransportCost_GrowsWithDistance()
        {
            var p = NewPlanet();
            var geo = new TradeGeography(p);
            int a = p.PlayerCountryId;
            var routes = Enumerable.Range(0, p.Countries.Count).Where(b => b != a).Select(b => geo.Route(a, b)).OrderBy(r => r.DirectKm).ToList();
            var near = routes.First();
            var far = routes.Last();
            foreach (var r in routes) output.WriteLine($"{p.Countries[r.To].Name,-14} {r}");
            Assert.True(far.DirectKm > near.DirectKm * 2);
            Assert.True(far.UnitCost(GoodsCatalog.Ore) > near.UnitCost(GoodsCatalog.Ore), "weiter = teurer");
            Assert.True(far.LeadDays > near.LeadDays, "weiter = länger unterwegs");
            // Schwer/sperrig kostet mehr je Einheit als leicht
            Assert.True(near.UnitCost(GoodsCatalog.Vehicles) > near.UnitCost(GoodsCatalog.Electronics));
            // Seeweg ist je km billiger als Landweg
            Assert.True(TradeRules.SeaCostPerTonKm < TradeRules.LandCostPerTonKm);
            foreach (var r in routes)
                if (r.Mode == TransportMode.Sea) Assert.True(r.SeaKm > 0 && r.LeadDays >= 3);
                else Assert.Equal(0, r.SeaKm);
            // symmetrisch genug: hin und zurück gleich weit
            Assert.Equal(geo.Route(a, far.To).DirectKm, geo.Route(far.To, a).DirectKm, 3);
        }

        // ------------------------------------------------------------ Verträge mit der Stadt

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        /// <summary>Kleine Stadt (wie im G5-Labor) mit Außenanschluss, Warensystem, Kasse und Handel mit einer kleinen Welt.</summary>
        sealed class TradeLab
        {
            public readonly SimulationWorld W;
            public readonly CitySystem City;
            public readonly TrafficSystem Traffic;
            public readonly GoodsSystem Goods;
            public readonly EconomySystem Kasse;
            public readonly Planet Planet;
            public readonly TradeSystem Trade;

            public TradeLab(int seed = 31)
            {
                W = new SimulationWorld(new WorldSettings());
                Traffic = new TrafficSystem(W.Roads, W.Min, W.Max, 7) { Demand = 0f }; // nur Platzhalter-Lieferungen
                W.AddSystem(Traffic);
                City = new CitySystem(W.Roads, W.Terrain, W.Min, W.Settings.SizeMeters, 7);
                W.AddSystem(City);
                Goods = new GoodsSystem(City, Traffic, W.Roads, W.Clock, 7);
                W.AddSystem(Goods);
                City.BusinessDemand = Goods.DemandOffset;
                City.Population.Residents = 3000;
                City.Population.WorkingAge = 1800;
                Road(W, new Vector2(-400, 0), new Vector2(1020, 0));
                City.Paint(new Vector2(-200, -20), 50f, ZoneType.Industry);
                for (int i = 0; i < 60; i++) City.ForceTick();
                foreach (var b in City.Buildings) b.Workers = b.Jobs;
                Goods.ForceStep();
                Kasse = new EconomySystem(new WorldSettings(), W.Roads, City, null);
                Planet = NewPlanet(seed);
                Trade = new TradeSystem(Planet, W.Clock, 5) { AiEnabled = false };
                Trade.AttachCity(Goods, Kasse);
            }

            public int Player => Trade.PlayerId;

            public Firm Mill()
            {
                var list = Goods.Firms.Where(f => f.Zone == ZoneType.Industry).ToList();
                Assert.NotEmpty(list);
                for (int i = 0; i < list.Count; i++)
                    Assert.True(Goods.Restructure(list[i], RecipeCatalog.Get(i == 0 ? "mill" : "quarry")));
                return list[0];
            }
        }

        /// <summary>Summe einer Kategorie in laufender + abgeschlossenen Wochen.</summary>
        static double TotalOf(EconomySystem k, BudgetCategory c) => k.CurrentWeek.Amount(c) + k.History.Sum(w => w.Amount(c));

        /// <summary>NPC mit vollem Lager in der Kategorie (liefert sicher).</summary>
        static int Supplier(TradeSystem t, GoodsCategory c)
        {
            int id = Npc(t);
            var e = t.Nations[id].Economy;
            e.Stock[(int)c] = Math.Max(e.Stock[(int)c], e.Demand[(int)c] * 200 + 1e6);
            return id;
        }

        [Fact]
        public void Contract_DeliversOnTime_ViaConnection_IntoFirmStock()
        {
            var lab = new TradeLab();
            var t = lab.Trade;
            var mill = lab.Mill();
            var grain = GoodsCatalog.Grain;
            int npc = Supplier(t, GoodsCategory.Food);

            // Spieler kauft 40 Getreide je Woche für 2 Wochen unter Weltpreis; NPC sagt zu (Command wie vom NPC selbst)
            double price = Math.Round(lab.Goods.Market(grain).WorldPrice * 0.8, 2);
            var r = t.Execute(new ProposeContractCommand(lab.Player, npc, TradeSide.Buy,
                new ContractTerms { Good = grain, UnitsPerWeek = 40, Weeks = 2, Price = price }));
            Assert.True(r.Ok, r.Message);
            var acc = t.Execute(new AcceptOfferCommand(npc, r.Id));
            Assert.True(acc.Ok, acc.Message);
            var c = t.Contract(acc.Id);
            Assert.Equal(npc, c.Seller);
            Assert.Equal(lab.Player, c.Buyer);
            int lead = c.Route.LeadDays;
            output.WriteLine($"Weg: {c.Route}");

            // Versand am Starttag, Ankunft genau nach der Lieferzeit
            int arriveDay = c.StartDay + lead;
            while (t.Day < arriveDay)
            {
                t.SimulateDay();
                Assert.Equal(0f, t.City.ImportAvailable(grain));
            }
            Assert.NotEmpty(c.Deliveries); // weitere Wochen sind ggf. schon unterwegs
            Assert.Equal(c.StartDay, c.Deliveries[0].DispatchDay);
            t.SimulateDay(); // Ankunftstag
            Assert.True(c.Deliveries[0].Arrived);
            Assert.Equal(arriveDay, c.Deliveries[0].ArriveDay);
            Assert.Equal(40f, t.City.ImportAvailable(grain), 3);
            Assert.Equal(40 * price, TotalOf(lab.Kasse, BudgetCategory.TradePurchases), 3); // Kasse zahlt das Lieferland
            Assert.True(c.WeeksOnTime >= 1);
            Assert.Equal(0, c.WeeksShort);

            // Mühle braucht Getreide → bezieht es aus dem Grenzlager über den Außenanschluss
            mill.InputStock[grain.Index] = 0f;
            mill.Incoming[grain.Index] = 0f;
            lab.Goods.ForceStep();
            var sh = lab.Goods.Shipments.Where(s => s.IsContract && s.ToBuildingId == mill.BuildingId).ToList();
            Assert.NotEmpty(sh);
            Assert.All(sh, s => Assert.Equal(lab.Traffic.ExternalConnections[0].Id, s.ConnectionNodeId));
            Assert.All(sh, s => Assert.Equal(ShipmentKind.Import, s.Kind));
            float taken = sh.Sum(s => s.Amount);
            Assert.Equal(taken * price, TotalOf(lab.Kasse, BudgetCategory.TradeSales), 2); // Firma zahlt den Vertragspreis an die Kasse
            for (int i = 0; i < 40; i++) lab.W.Step(); // Platzhalter-Lkw fährt an
            Assert.True(mill.InputOf(grain) > 0.5f * taken, "Vertragsware im Firmenlager");
        }

        [Fact]
        public void PlayerExport_FirmsDeliverIntoContract()
        {
            var lab = new TradeLab();
            var t = lab.Trade;
            var mill = lab.Mill();
            var flour = GoodsCatalog.Flour;
            int npc = Npc(t);
            var r = t.Execute(new ProposeContractCommand(lab.Player, npc, TradeSide.Sell,
                new ContractTerms { Good = flour, UnitsPerWeek = 20, Weeks = 1, Price = 140 }));
            var c = t.Contract(t.Execute(new AcceptOfferCommand(npc, r.Id)).Id);
            Assert.Equal(lab.Player, c.Seller);
            Assert.True(c.StartDay >= t.Day + TradeRules.DaysPerWeek - 1, "Vorlauf zum Sammeln");
            Assert.Equal(20f, t.City.ExportWanted(flour), 3);

            mill.OutputStock[flour.Index] = mill.OutputCapacity[flour.Index];
            double cashBefore = mill.Cash;
            lab.Goods.ForceStep();
            var sh = lab.Goods.Shipments.Where(s => s.IsContract && s.Kind == ShipmentKind.Export).ToList();
            Assert.NotEmpty(sh);
            Assert.True(mill.Cash > cashBefore, "Firma bekommt den Vertragspreis");
            Assert.Equal(sh.Sum(s => s.Amount) * 140.0, lab.Kasse.CurrentWeek.Amount(BudgetCategory.TradePurchases), 2);
            for (int i = 0; i < 40; i++) lab.W.Step();
            Assert.True(t.City.ExportStock[flour.Index] >= 19.9, $"Grenzlager {t.City.ExportStock[flour.Index]:0.0}");
            while (t.Day <= c.StartDay) t.SimulateDay();
            output.WriteLine($"Tag {t.Day} Start {c.StartDay} versandt {c.WeeksDispatched} pünktlich {c.WeeksOnTime} fehlend {c.WeeksShort} Lager {t.City.ExportStock[flour.Index]:0.0} Status {c.Status}");
            Assert.Equal(1, c.WeeksOnTime);
            Assert.Equal(0, c.WeeksShort);
            Assert.Equal(20 * c.Route.UnitCost(flour), t.City.FreightPaid, 3); // Fernfracht trägt der Verkäufer (Kasse)
        }

        [Fact]
        public void NonDelivery_Penalty_AndTrustDrops()
        {
            var lab = new TradeLab();
            var t = lab.Trade;
            int npc = Npc(t);
            var steel = GoodsCatalog.Steel;
            // Spieler verkauft Stahl, hat aber nichts im Grenzlager
            var r = t.Execute(new ProposeContractCommand(lab.Player, npc, TradeSide.Sell,
                new ContractTerms { Good = steel, UnitsPerWeek = 10, Weeks = 2, Price = 250, PenaltyShare = 0.4 }));
            var c = t.Contract(t.Execute(new AcceptOfferCommand(npc, r.Id)).Id);
            float trustBefore = t.Nations[npc].Trust[lab.Player];
            float relBefore = t.Relation(npc, lab.Player);
            while (t.Day <= c.StartDay) t.SimulateDay();
            Assert.Equal(1, c.WeeksShort);
            Assert.Equal(10, c.UnitsShort, 3);
            Assert.Equal(10 * 250 * 0.4, c.Penalties, 3);
            Assert.Equal(10 * 250 * 0.4, lab.Kasse.CurrentWeek.Amount(BudgetCategory.PenaltiesPaid) + (lab.Kasse.LastWeek?.Amount(BudgetCategory.PenaltiesPaid) ?? 0), 3);
            Assert.True(t.Nations[npc].Trust[lab.Player] < trustBefore - 0.2f, "Vertrauen sinkt deutlich");
            Assert.True(t.Relation(npc, lab.Player) < relBefore - 10f, "Beziehung leidet");
            Assert.Contains(t.Nations[npc].Memory, m => m.Kind == TradeMemoryKind.Breach && m.Partner == lab.Player);

            // Gedächtnis wirkt: dasselbe faire Angebot wird jetzt schlechter bewertet
            var o = t.Offer(t.Execute(new ProposeContractCommand(lab.Player, npc, TradeSide.Sell,
                new ContractTerms { Good = steel, UnitsPerWeek = 1, Weeks = 2, Price = 250 })).Id);
            var ev = t.Evaluate(npc, o);
            output.WriteLine($"nach Vertragsbruch: {ev.Utility:0.00} · {ev.Reason}");
            Assert.Contains(t.Contracts.Concat(t.History), x => x.Id == c.Id);
        }

        // ------------------------------------------------------------ Utility AI

        [Fact]
        public void Npc_AcceptsGoodOffer_RejectsBadOffer_WithReason()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            t.AiEnabled = false;
            Days(t, 3);
            int player = p.PlayerCountryId;
            var grain = GoodsCatalog.Grain;
            // Käufer-NPC: Agrarknappheit
            int buyer = Npc(t);
            var e = t.Nations[buyer].Economy;
            int food = (int)GoodsCategory.Food;
            e.Stock[food] = e.Demand[food] * 2;
            t.SimulateDay();
            double domestic = t.CountryPrice(buyer, grain);

            var good = t.Offer(t.Execute(new ProposeContractCommand(player, buyer, TradeSide.Sell,
                new ContractTerms { Good = grain, UnitsPerWeek = 50, Weeks = 4, Price = Math.Round(domestic * 0.7, 2) })).Id);
            var bad = t.Offer(t.Execute(new ProposeContractCommand(player, buyer, TradeSide.Sell,
                new ContractTerms { Good = grain, UnitsPerWeek = 50, Weeks = 4, Price = Math.Round(domestic * 2.5, 2) })).Id);
            var evGood = t.Evaluate(buyer, good);
            var evBad = t.Evaluate(buyer, bad);
            output.WriteLine($"gut: U {evGood.Utility:0.00} p {evGood.Probability:0.00} {evGood.Reason}");
            output.WriteLine($"schlecht: U {evBad.Utility:0.00} p {evBad.Probability:0.00} {evBad.Reason}");
            t.SimulateDay(); // Antwort am nächsten Tag (asynchron)
            Assert.Equal(OfferStatus.Accepted, good.Status);
            Assert.Equal(OfferStatus.Rejected, bad.Status);
            Assert.Contains("zu teuer", bad.Reason);
            Assert.NotNull(t.Contract(good.ContractId));

            // Verkäufer-NPC: viel zu niedriger Preis → „Preis zu niedrig“, zu große Menge → „Menge zu groß“
            int seller = Supplier(t, GoodsCategory.Energy);
            t.SimulateDay();
            var oil = GoodsCatalog.Oil;
            double sp = t.CountryPrice(seller, oil);
            var cheap = t.Offer(t.Execute(new ProposeContractCommand(player, seller, TradeSide.Buy,
                new ContractTerms { Good = oil, UnitsPerWeek = 10, Weeks = 2, Price = Math.Round(sp * 0.3, 2) })).Id);
            var huge = t.Offer(t.Execute(new ProposeContractCommand(player, seller, TradeSide.Buy,
                new ContractTerms { Good = oil, UnitsPerWeek = 1e12, Weeks = 2, Price = Math.Round((sp + t.Geography.Route(seller, player).UnitCost(oil)) * 1.6, 2) })).Id);
            t.SimulateDay();
            Assert.Equal(OfferStatus.Rejected, cheap.Status);
            Assert.True(cheap.Reason.Contains("Preis zu niedrig"), cheap.Reason);
            Assert.Equal(OfferStatus.Rejected, huge.Status);
            Assert.True(huge.Reason.Contains("Menge zu groß"), huge.Reason);
        }

        [Fact]
        public void Tariff_RaisesImportCost_AndFillsTreasury()
        {
            var lab = new TradeLab();
            var t = lab.Trade;
            var mill = lab.Mill();
            var grain = GoodsCatalog.Grain;
            var m = lab.Goods.Market(grain);

            double ImportCost()
            {
                mill.InputStock[grain.Index] = 0f;
                mill.Incoming[grain.Index] = 0f;
                m.LocalPrice = m.WorldPrice * 1.5f; // Import lohnt
                double before = mill.Today.Purchases;
                int n = lab.Goods.Shipments.Count;
                lab.Goods.ForceStep();
                float units = lab.Goods.Shipments.Skip(n).Where(s => s.Kind == ShipmentKind.Import && s.ToBuildingId == mill.BuildingId).Sum(s => s.Amount);
                Assert.True(units > 0f);
                return (mill.Today.Purchases - before) / units;
            }

            double free = ImportCost();
            Assert.Equal(0, lab.Kasse.CurrentWeek.Amount(BudgetCategory.Tariffs));
            var r = t.Execute(new SetTariffCommand(lab.Player, -1, 0.2));
            Assert.True(r.Ok, r.Message);
            double taxed = ImportCost();
            output.WriteLine($"Einkauf je Einheit ohne Zoll {free:0.00} €, mit 20 % {taxed:0.00} €, Zolleinnahmen {lab.Kasse.CurrentWeek.Amount(BudgetCategory.Tariffs):0.00} €");
            Assert.Equal(free * 1.2, taxed, 2);
            Assert.True(lab.Kasse.CurrentWeek.Amount(BudgetCategory.Tariffs) > 0);

            // Vertragsware: Zoll gegen das Lieferland, Handelsabkommen = zollfrei
            int npc = Supplier(t, GoodsCategory.Food);
            Assert.Equal(0.2, t.TariffRate(lab.Player, npc), 6);
            var ag = t.Execute(new ProposeContractCommand(lab.Player, npc, TradeSide.Sell, new ContractTerms { Kind = ContractKind.Agreement, Weeks = 4 }));
            Assert.True(t.Execute(new AcceptOfferCommand(npc, ag.Id)).Ok);
            Assert.True(t.HasAgreement(lab.Player, npc));
            Assert.Equal(0, t.TariffRate(lab.Player, npc));
            Assert.Equal(0, t.TariffRate(npc, lab.Player));
        }

        [Fact]
        public void Npcs_TradeAmongThemselves_DeficitsShrink()
        {
            const int days = 120;
            (Planet p, TradeSystem t) Run(bool ai)
            {
                var p = NewPlanet(44);
                var t = NewTrade(p, 9);
                t.AiEnabled = ai;
                Days(t, days);
                return (p, t);
            }

            var (p0, t0) = Run(false);
            var (p1, t1) = Run(true);
            Assert.Empty(t0.Contracts.Concat(t0.History));
            // Käufer-Länder und Kategorien der NPC-Verträge, die bis Tag 90 geschlossen wurden (Ware ist angekommen)
            var pairs = t1.Contracts.Concat(t1.History)
                .Where(c => c.Kind == ContractKind.Supply && !t1.IsPlayer(c.Seller) && !t1.IsPlayer(c.Buyer) && c.StartDay < 90 && c.UnitsDelivered > 0)
                .Select(c => (buyer: c.Buyer, seller: c.Seller, cat: (int)TradeGoods.CategoryOf(c.Good))).Distinct().ToList();
            Assert.True(pairs.Count >= 5, $"NPC-Länder handeln untereinander ({pairs.Count} Paare)");
            double Gap(Planet p, int n, int c) => Math.Max(0.0, TradeRules.TargetCoverDays - p.Countries[n].Economy.Stock[c] / Math.Max(1.0, p.Countries[n].Economy.Demand[c]));
            double before = 0, after = 0, coverWithout = 0, coverWith = 0;
            foreach (var (buyer, _, cat) in pairs.Select(x => (x.buyer, x.seller, x.cat)).Distinct())
            {
                before += Gap(p0, buyer, cat);
                after += Gap(p1, buyer, cat);
                coverWithout += p0.Countries[buyer].Economy.Stock[cat] / Math.Max(1.0, p0.Countries[buyer].Economy.Demand[cat]);
                coverWith += p1.Countries[buyer].Economy.Stock[cat] / Math.Max(1.0, p1.Countries[buyer].Economy.Demand[cat]);
            }
            output.WriteLine($"{pairs.Count} Handelspaare · Käufer: Lagerlücke (Tage unter 20) ohne Handel {before:0.0}, mit Handel {after:0.0} · " +
                             $"Reichweite Σ {coverWithout:0.0} → {coverWith:0.0} Tage");
            Assert.True(after < before * 0.8, $"Defizit der Käufer sinkt: {before:0.0} → {after:0.0}");
            Assert.True(coverWith > coverWithout);
        }

        // ------------------------------------------------------------ Kalibrierung der Länder-Volkswirtschaften

        /// <summary>Mittlere Tagesleistung (Lebensmittel mit dem Jahresmittel der Ernte).</summary>
        static double Potential(NationEconomy n, int c) =>
            n.Population * n.CapacityPerCapita[c] * (c == (int)GoodsCategory.Food ? n.Climate.AnnualHarvestFactor : 1.0);

        [Theory]
        [InlineData(2024)]
        [InlineData(7)]
        public void WorldEconomy_BalancedOnAverage_CountriesSpecialized(int seed)
        {
            var p = NewPlanet(seed, new PlanetSettings());
            var t = NewTrade(p);
            t.AiEnabled = false;
            var prod = new double[GoodsCategories.Count];
            var dem = new double[GoodsCategories.Count];
            for (int d = 0; d < 120; d++)
            {
                t.SimulateDay();
                if (d < 30) continue;
                foreach (var n in p.Economy.Nations)
                    for (int c = 0; c < GoodsCategories.Count; c++) { prod[c] += n.Production[c]; dem[c] += n.Demand[c]; }
            }
            for (int c = 0; c < GoodsCategories.Count; c++)
            {
                double ratio = prod[c] / dem[c];
                output.WriteLine($"{(GoodsCategory)c,-11} Welt Produktion/Bedarf {ratio:0.00}");
                Assert.InRange(ratio, 0.85, 1.15);
            }
            // Spezialisierung: Länder mit Primärstärke X haben in X Kapazität über dem eigenen Bedarf und über dem Rest der Welt
            var primaryCategory = new Dictionary<Strength, GoodsCategory[]>
            {
                [Strength.Agriculture] = new[] { GoodsCategory.Food },
                [Strength.RawMaterials] = new[] { GoodsCategory.Wood, GoodsCategory.Minerals, GoodsCategory.Energy },
                [Strength.Industry] = new[] { GoodsCategory.Industrial },
                [Strength.Technology] = new[] { GoodsCategory.Technology },
                [Strength.Tourism] = new[] { GoodsCategory.Tourism },
            };
            foreach (var kv in primaryCategory)
            {
                var group = p.Economy.Nations.Where(n => n.Country.Profile.Primary == kv.Key).ToList();
                if (group.Count == 0) continue;
                var others = p.Economy.Nations.Where(n => n.Country.Profile.Primary != kv.Key).ToList();
                double Best(NationEconomy n) => kv.Value.Max(c => Potential(n, (int)c) / Math.Max(1.0, n.Demand[(int)c]));
                double mine = group.Average(Best), rest = others.Average(Best);
                output.WriteLine($"Primär {kv.Key,-13} ({group.Count}): Kapazität/Bedarf {mine:0.00} · übrige {rest:0.00}");
                Assert.True(mine > 1.05, $"{kv.Key}: Überschuss erwartet ({mine:0.00})");
                Assert.True(mine > rest, $"{kv.Key}: stärker als der Rest ({mine:0.00} vs {rest:0.00})");
            }
        }

        [Fact]
        public void OneYear_WithoutTradeGapsGrow_WithTradeMuchSmaller()
        {
            double Gap(Planet p) => p.Economy.Nations.Sum(n => Enumerable.Range(0, GoodsCategories.Count)
                .Where(c => TradeGoods.IsTradeable((GoodsCategory)c))
                .Sum(c => Math.Max(0.0, TradeRules.TargetCoverDays * n.Demand[c] - n.Stock[c]) * GoodsCategories.BasePrice[c]));
            (double early, double end, double sum) Run(bool ai)
            {
                var p = NewPlanet(2024, new PlanetSettings());
                var t = NewTrade(p, 9);
                t.AiEnabled = ai;
                double early = 0, sum = 0;
                for (int d = 0; d < 365; d++)
                {
                    t.SimulateDay();
                    if (d == 29) early = Gap(p);
                    if (d >= 60) sum += Gap(p);
                }
                return (early, Gap(p), sum);
            }
            var off = Run(false);
            var on = Run(true);
            output.WriteLine($"Lagerlücke weltweit (Wert unter 20 Tagen Reichweite): ohne Handel Tag 30 {off.early / 1e9:0.0} G€ → Tag 365 {off.end / 1e9:0.0} G€; " +
                             $"mit Handel Tag 365 {on.end / 1e9:0.0} G€ · Summe Tag 60–365 ohne {off.sum / 1e9:0} G€, mit {on.sum / 1e9:0} G€ ({(1 - on.sum / off.sum) * 100:0} % kleiner)");
            Assert.True(off.end > off.early, "ohne Handel wachsen die Defizite");
            Assert.True(on.sum < off.sum * 0.75, "mit Handel deutlich kleinere Lagerlücken");
            Assert.True(on.end < off.end * 0.75);
        }

        [Fact]
        public void News_ContractTariffEmbargo_ForMedia()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            t.AiEnabled = false;
            var news = new List<TradeNews>();
            t.News += news.Add;
            int player = p.PlayerCountryId, npc = Npc(t);
            var o = t.Execute(new ProposeContractCommand(player, npc, TradeSide.Buy,
                new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 5, Weeks = 3, Price = 200 }));
            t.Execute(new AcceptOfferCommand(npc, o.Id));
            Assert.Contains(news, n => n.Kind == TradeNewsKind.ContractSigned && n.Country == npc && n.Detail == "Stahl" && n.Value == 3);
            t.Execute(new SetTariffCommand(npc, player, 0.3));
            Assert.Contains(news, n => n.Kind == TradeNewsKind.TariffRaised && n.Country == npc && Math.Abs(n.Value - 0.3) < 1e-9);
            // eigene Zollerhöhung des Spielers ist keine Meldung „Land X erhöht“
            int before = news.Count;
            t.Execute(new SetTariffCommand(player, -1, 0.2));
            Assert.Equal(before, news.Count);
            // Embargo: Beziehung fällt unter −50 (Kündigungen in Serie)
            for (int i = 0; i < 20 && !news.Exists(n => n.Kind == TradeNewsKind.Embargo); i++)
            {
                var r = t.Execute(new ProposeContractCommand(player, npc, TradeSide.Buy,
                    new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 5, Weeks = 3, Price = 200 }));
                var c = t.Execute(new AcceptOfferCommand(npc, r.Id));
                t.Execute(new CancelContractCommand(player, c.Id));
            }
            Assert.Contains(news, n => n.Kind == TradeNewsKind.Embargo && n.Country == npc);
            Assert.True(t.Relation(player, npc) < TradeRules.RelationEmbargo);
            // Vorlagen der Medien haben die Platzhalter
            foreach (var topic in new[] { Services.Content.HeadlineTopic.TradeDeal, Services.Content.HeadlineTopic.Embargo, Services.Content.HeadlineTopic.TariffRaise })
                Assert.Contains("{Land}", Services.Content.Headlines.Template(topic, 0).title);
        }

        [Fact]
        public void Trade_IsDeterministic()
        {
            string Run()
            {
                var p = NewPlanet(12);
                var t = NewTrade(p, 3);
                Days(t, 90);
                var parts = t.Contracts.Concat(t.History).OrderBy(c => c.Id)
                    .Select(c => $"{c.Id}:{c.Seller}>{c.Buyer}:{c.Good?.Id}:{c.Terms.UnitsPerWeek:0.###}:{c.UnitsDelivered:0.###}:{c.Status}");
                var prices = GoodsCatalog.All.Select(g => t.WorldPrice(g).ToString("0.######"));
                return string.Join("|", parts) + "#" + string.Join(",", prices) + "#" + t.CommandLog.Count;
            }
            string a = Run(), b = Run();
            Assert.Equal(a, b);
            Assert.Contains(":", a);
        }

        [Fact]
        public void FullWorld_OneYear_Performance()
        {
            var sw = Stopwatch.StartNew();
            var p = PlanetGenerator.Generate(2024);
            p.SetStartRegion(p.SuggestStartRegion());
            double gen = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var t = new TradeSystem(p, null, 1);
            double init = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            Days(t, 365);
            double year = sw.Elapsed.TotalMilliseconds;
            // nur Handel (ohne Länder-Volkswirtschaften)
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < 365; i++) t.DayStep();
            double tradeOnly = sw2.Elapsed.TotalMilliseconds;
            int total = t.Contracts.Count + t.History.Count;
            output.WriteLine($"{p.Countries.Count} Länder: Welt {gen:0} ms, Handelsnetz {init:0} ms, 1 Jahr (Länder + Handel) {year:0} ms " +
                             $"(≈ {year / 365:0.00} ms/Tag), Handel allein {tradeOnly:0} ms/Jahr · aktive Verträge {t.Contracts.Count}, " +
                             $"beendet (behalten) {t.History.Count}, Lieferungen unterwegs {t.InTransit.Count}, Commands {t.CommandLog.Count}");
            foreach (var n in t.Nations.Take(8))
                output.WriteLine($"  {n.Country.Name,-14} {n.Personality.Describe(),-50} Export {n.ExportValue / 1e6:0.0} M€ · Import {n.ImportValue / 1e6:0.0} M€ · Zoll {n.Tariffs.General * 100:0} %");
            Assert.Equal(36, p.Countries.Count);
            Assert.True(total > 50, $"es wird gehandelt ({total})");
            Assert.True(year < 15000, $"1 Jahr in {year:0} ms");
        }

        [Fact]
        public void CreateDefault_WithWorld_HasTrade_WithoutWorld_None()
        {
            var plain = SimulationWorld.CreateDefault(new WorldSettings());
            Assert.Null(plain.Trade);
            Assert.Null(plain.Goods.TradeGateway);
            var w = SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = 31, Planet = Small, GenerateTerrain = false, SizeMeters = 1024f,
            });
            Assert.NotNull(w.Trade);
            Assert.Same(w.Trade.City, w.Goods.TradeGateway);
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) w.Step();
            Assert.Equal(2, w.Trade.Day);
            var g = GoodsCatalog.Steel;
            Assert.NotNull(w.Goods.WorldPriceSource);
            // Markt übernimmt den Länder-Weltpreis beim Tageswechsel (ein Tag Versatz zum Handelstag)
            Assert.InRange(w.Goods.Market(g).WorldPrice, w.Trade.CityWorldPrice(g) * 0.9f, w.Trade.CityWorldPrice(g) * 1.1f);
        }
    }
}
