using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Produktion lenken: Schwerpunkt-Gebiete (Standortwahl, Vorkommen-Grenze, träger Betriebswechsel), Förderung, Spielstand.</summary>
    public class ProductionPolicyTests
    {
        readonly ITestOutputHelper output;
        public ProductionPolicyTests(ITestOutputHelper output) => this.output = output;

        static readonly Vector2 IndustryCentre = new Vector2(-200, -20);

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
        /// Labor wie in <c>GoodsTests</c> (Stadt, Verkehr, Waren an einer Straße, Bewohnerzahl vorgegeben) plus Lenkung.
        /// <paramref name="before"/> läuft, bevor die Gebäude entstehen (z. B. Schwerpunkt malen).
        /// </summary>
        sealed class Lab
        {
            public readonly SimulationWorld W;
            public readonly CitySystem City;
            public readonly GoodsSystem Goods;
            public readonly ProductionPolicy Policy;

            public Lab(Func<string, Vector2, float> deposits, Action<ProductionPolicy> before = null, int residents = 3000, bool retail = true)
            {
                W = new SimulationWorld(new WorldSettings());
                var traffic = new TrafficSystem(W.Roads, W.Min, W.Max, 7);
                W.AddSystem(traffic);
                City = new CitySystem(W.Roads, W.Terrain, W.Min, W.Settings.SizeMeters, 7);
                W.AddSystem(City);
                Goods = new GoodsSystem(City, traffic, W.Roads, W.Clock, 7) { DepositAt = deposits };
                W.AddSystem(Goods);
                Policy = new ProductionPolicy(Goods, W.Clock, W.Min, W.Settings.SizeMeters, 16f, 7);
                Goods.SiteModifier = Policy.SiteAdjust;
                Goods.SubsidyFor = Policy.SubsidyFor;
                City.BusinessDemand = Goods.DemandOffset;
                City.Population.Residents = residents;
                City.Population.WorkingAge = residents * 6 / 10;
                before?.Invoke(Policy);
                Road(W, new Vector2(-400, 0), new Vector2(1020, 0));
                City.Paint(IndustryCentre, 50f, ZoneType.Industry);
                if (retail) City.Paint(new Vector2(200, 20), 30f, ZoneType.Retail);
                for (int i = 0; i < 60; i++) City.ForceTick();
                foreach (var b in City.Buildings) b.Workers = b.Jobs;
                Goods.ForceStep();
            }

            public List<Firm> Industry => Goods.Firms.Where(f => f.Zone == ZoneType.Industry).ToList();

            /// <summary>Einen Lenkungstag weiter (Uhr + 1 Tag, dann <see cref="ProductionPolicy.Steer"/>).</summary>
            public int Day()
            {
                for (int i = 0; i < SimulationClock.TicksPerDay; i++) W.Clock.Advance();
                return Policy.Steer();
            }
        }

        static float OilEverywhere(string id, Vector2 p) => id == ResourceIds.Oil ? 0.6f : GoodsSystem.DefaultDeposit(id, p);

        // ------------------------------------------------------------ Standortwahl

        [Fact]
        public void Siting_FocusGivesBonusToPrimary_PenaltyToForeign_RelatedUnchanged()
        {
            var lab = new Lab(OilEverywhere);
            var b = lab.Industry[0].Building;
            var oil = RecipeCatalog.Get("oil_well");
            var refinery = RecipeCatalog.Get("refinery");
            var food = RecipeCatalog.Get("food_factory");
            lab.Goods.SuggestIndustry(b); // Planwerte (Bedarf/Kapazität) auf Stand bringen – sie fließen in den Score
            float oil0 = lab.Goods.SiteScore(b, oil), ref0 = lab.Goods.SiteScore(b, refinery), food0 = lab.Goods.SiteScore(b, food);

            Assert.True(lab.Policy.Paint(b.Position, 30f, IndustryFocus.OilGas) > 0);
            Assert.Equal(IndustryFocus.OilGas, lab.Policy.FocusAt(b.Position));
            Assert.Equal(oil0 + PolicyRules.FocusPrimaryBonus, lab.Goods.SiteScore(b, oil), 4);
            Assert.Equal(ref0, lab.Goods.SiteScore(b, refinery), 4);                                  // verwandt: unverändert
            Assert.Equal(food0 - PolicyRules.FocusForeignPenalty, lab.Goods.SiteScore(b, food), 4);  // fremd: Abschlag
            Assert.Equal(oil, lab.Goods.SuggestIndustry(b));

            // Förderung: Zuschlag je Stufe für die Branche überall (auch ohne Schwerpunkt)
            lab.Policy.Paint(b.Position, 30f, IndustryFocus.Auto);
            lab.Policy.SetSubsidy(IndustryFocus.ConsumerGoods, 2);
            Assert.Equal(food0 + PolicyRules.SubsidySiteBonus[2], lab.Goods.SiteScore(b, food), 4);
            Assert.Equal(oil0, lab.Goods.SiteScore(b, oil), 4);
            lab.Policy.CanPay = () => false; // Baustopp → Förderung ruht
            Assert.Equal(food0, lab.Goods.SiteScore(b, food), 4);
        }

        [Fact]
        public void Siting_WithAndWithoutFocus_Deterministic()
        {
            string[] Recipes(bool focus) => new Lab(OilEverywhere,
                    focus ? p => p.Paint(IndustryCentre, 120f, IndustryFocus.OilGas) : (Action<ProductionPolicy>)null)
                .Industry.Select(f => f.Recipe.Id).ToArray();

            var auto = Recipes(false);
            var oil = Recipes(true);
            output.WriteLine("ohne Schwerpunkt: " + string.Join(", ", auto));
            output.WriteLine("Schwerpunkt Öl:   " + string.Join(", ", oil));
            Assert.Equal(auto, Recipes(false)); // deterministisch
            Assert.Equal(oil, Recipes(true));
            int wellsAuto = auto.Count(r => r == "oil_well"), wellsOil = oil.Count(r => r == "oil_well");
            Assert.True(wellsOil > wellsAuto, $"Ölförderung mit Schwerpunkt {wellsOil}, ohne {wellsAuto}");
            Assert.True(wellsOil * 2 >= oil.Length, $"nur {wellsOil} von {oil.Length} Betrieben fördern Öl");
            // Nicht verboten: fremde Betriebe bleiben möglich (Bewertung nur mit Abschlag)
            Assert.All(oil, r => Assert.NotNull(RecipeCatalog.Get(r)));
        }

        [Fact]
        public void DepositLimit_StaysHard_HintSaysNoOil()
        {
            // Öl nur 5 % (Mindestgüte 15 %) → trotz Schwerpunkt keine Ölförderung
            float Weak(string id, Vector2 p) => id == ResourceIds.Oil ? 0.05f : GoodsSystem.DefaultDeposit(id, p);
            var lab = new Lab(Weak, p => p.Paint(IndustryCentre, 120f, IndustryFocus.OilGas));
            var firms = lab.Industry;
            Assert.NotEmpty(firms);
            Assert.DoesNotContain(firms, f => f.Recipe.Id == "oil_well");
            Assert.True(float.IsNegativeInfinity(lab.Goods.SiteScore(firms[0].Building, RecipeCatalog.Get("oil_well"))));

            Assert.Equal(DepositRating.None, lab.Policy.RateDeposit(IndustryFocus.OilGas, IndustryCentre, out float q));
            Assert.Equal(0.05f, q, 3);
            string hint = lab.Policy.DepositHint(IndustryFocus.OilGas, IndustryCentre);
            output.WriteLine(hint);
            Assert.Contains("keins", hint);
            Assert.Contains(lab.Policy.Recommendations(), r => r.Contains("kein Erdöl-Vorkommen"));

            // Schwach/gut
            var good = new Lab(OilEverywhere, retail: false, residents: 0);
            Assert.Equal(DepositRating.Good, good.Policy.RateDeposit(IndustryFocus.OilGas, IndustryCentre, out _));
            Assert.Contains("gut", good.Policy.DepositHint(IndustryFocus.OilGas, IndustryCentre));
            Assert.Equal(DepositRating.NotNeeded, good.Policy.RateDeposit(IndustryFocus.Technology, IndustryCentre, out _));
        }

        // ------------------------------------------------------------ Betriebswechsel (träge)

        [Fact]
        public void Restructure_IsSlow_UnprofitableFirst_AndOnlyIntoFocus()
        {
            Lab Prepared(bool unprofitable)
            {
                var lab = new Lab(OilEverywhere);
                foreach (var f in lab.Industry)
                {
                    Assert.True(lab.Goods.Restructure(f, RecipeCatalog.Get("food_factory"))); // alle fremd
                    f.AgeDays = 30;
                    f.ProfitEma = unprofitable ? -100 : 50;
                }
                lab.Policy.Paint(IndustryCentre, 120f, IndustryFocus.OilGas); // Schwerpunkt neu
                return lab;
            }

            var a = Prepared(unprofitable: false);
            int total = a.Industry.Count;
            Assert.True(total >= 6, $"nur {total} Industriebetriebe");
            int first = a.Day();
            Assert.True(first <= PolicyRules.MaxSwitchesPerDay);
            Assert.True(first < total, "nicht alle sofort");
            var converted = new List<int> { first };
            for (int d = 1; d < 60; d++) converted.Add(converted[converted.Count - 1] + a.Day());
            int foreignLeft = a.Industry.Count(f => IndustryBranches.Fit(IndustryFocus.OilGas, f.Recipe) == FocusFit.Foreign);
            output.WriteLine($"{total} Betriebe, umgestellt nach 1/5/10/30/60 Tagen: {converted[0]}/{converted[4]}/{converted[9]}/{converted[29]}/{converted[59]}, fremd übrig {foreignLeft}");
            Assert.True(converted[4] < total, "nach 5 Tagen noch nicht alle umgestellt");
            Assert.True(foreignLeft * 4 <= total, $"nach 60 Tagen noch {foreignLeft} von {total} fremd");
            Assert.All(a.Industry.Where(f => f.Recipe.Id != "food_factory"),
                f => Assert.NotEqual(FocusFit.Foreign, IndustryBranches.Fit(IndustryFocus.OilGas, f.Recipe)));
            Assert.Equal(converted[59], a.Policy.SwitchesTotal);

            // Verlustbetriebe stellen schneller um
            var b = Prepared(unprofitable: true);
            int fast = 0;
            for (int d = 0; d < 5; d++) fast += b.Day();
            output.WriteLine($"nach 5 Tagen: rentabel {converted[4]}, Verlust {fast}");
            Assert.True(fast > converted[4], $"Verlustbetriebe {fast} vs. rentable {converted[4]}");

            // Ohne Schwerpunkt (automatisch) wechselt niemand
            var c = Prepared(unprofitable: true);
            c.Policy.Paint(IndustryCentre, 120f, IndustryFocus.Auto);
            for (int d = 0; d < 10; d++) Assert.Equal(0, c.Day());
        }

        [Fact]
        public void Restructure_WithoutDeposit_StaysAndCountsBlocked()
        {
            var lab = new Lab((id, p) => id == ResourceIds.Oil ? 0f : GoodsSystem.DefaultDeposit(id, p));
            foreach (var f in lab.Industry)
            {
                lab.Goods.Restructure(f, RecipeCatalog.Get("textile"));
                f.AgeDays = 30;
                f.ProfitEma = -10;
            }
            // Schwerpunkt Erz ohne Erz: kein passender Betrieb möglich (Stahlwerk wäre verwandt – Lab hat keinen Stahlbedarf)
            lab.Policy.Paint(IndustryCentre, 120f, IndustryFocus.Ore);
            int switched = 0, blocked = 0;
            for (int d = 0; d < 20; d++) { switched += lab.Day(); blocked += lab.Policy.BlockedYesterday; }
            output.WriteLine($"umgestellt {switched}, blockiert {blocked}");
            Assert.DoesNotContain(lab.Industry, f => f.Recipe.Id == "ore_mine");
            Assert.All(lab.Industry, f => Assert.True(f.Recipe.Id == "textile" || f.Recipe.Id == "steelworks"));
            Assert.True(blocked > 0 || switched > 0);
        }

        // ------------------------------------------------------------ Förderung und Stadtkasse

        static SimulationWorld Town(Action<SimulationWorld> setup = null, int days = 6, int seed = 12345, Action<SimulationWorld> beforeRun = null)
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false, StartingFunds = 2_000_000 }, seed);
            setup?.Invoke(w);
            Road(w, new Vector2(-300, 0), new Vector2(1020, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
            w.City.Paint(new Vector2(400, 20), 40f, ZoneType.Office);
            beforeRun?.Invoke(w);
            Run(w, SimulationClock.TicksPerDay * days);
            return w;
        }

        [Fact]
        public void Subsidy_PaidToFirms_BookedInBudget()
        {
            var w = Town(beforeRun: x =>
            {
                foreach (var b in IndustryBranches.Branches) x.Policy.SetSubsidy(b, 2);
            });
            var g = w.Goods;
            double paid = g.Stats.SubsidiesYesterday;
            double perFirm = g.Firms.Sum(f => f.Yesterday.Subsidy);
            double booked = w.Economy.LastDay.Amount(BudgetCategory.IndustrySubsidies);
            output.WriteLine($"Förderung gestern {paid:0} € an {g.Firms.Count(f => f.Yesterday.Subsidy > 0)} Firmen, gebucht {booked:0} €, " +
                             $"Prognose heute {w.Policy.SubsidyPerDay():0} €, Woche {w.Economy.LastWeek?.Amount(BudgetCategory.IndustrySubsidies) ?? 0:0} €");
            Assert.True(paid > 0, "Förderung erwartet");
            Assert.Equal(paid, perFirm, 6);
            Assert.Equal(paid, booked, 6);
            Assert.False(Budget.IsIncome(BudgetCategory.IndustrySubsidies));
            // Satz: Beschäftigte × 12 € (Stufe 2) – nur Industrie, Läden/Büros nicht
            var f0 = g.Firms.First(f => f.Yesterday.Subsidy > 0);
            Assert.True(f0.Recipe.IsIndustry);
            Assert.All(g.Firms.Where(f => !f.Recipe.IsIndustry), f => Assert.Equal(0, f.Yesterday.Subsidy));
            Assert.Equal(f0.Yesterday.Revenue + f0.Yesterday.Subsidy - f0.Yesterday.InputCost - f0.Yesterday.Wages - f0.Yesterday.Transport,
                         f0.Yesterday.Profit, 6);

            // Bericht je Branche: Förderung/Tag = Beschäftigte × Satz
            foreach (var branch in IndustryBranches.Branches)
            {
                var rep = w.Policy.Report(branch);
                Assert.Equal(rep.Workers * PolicyRules.SubsidyPerWorkerDay[2], rep.SubsidyPerDay, 6);
            }

            // Ohne Förderung: nichts gebucht
            var none = Town(days: 3);
            Assert.Equal(0, none.Economy.LastDay.Amount(BudgetCategory.IndustrySubsidies));
            Assert.All(none.Goods.Firms, f => Assert.Equal(0, f.Yesterday.Subsidy));
        }

        [Fact]
        public void Subsidy_SuspendedWhenCityCannotPay()
        {
            var w = Town(beforeRun: x => x.Policy.SetSubsidy(IndustryFocus.ConsumerGoods, 3), days: 3);
            w.Policy.CanPay = () => false;
            Assert.True(w.Policy.SubsidiesSuspended);
            Assert.Equal(0, w.Policy.SubsidyPerDay());
            Run(w, SimulationClock.TicksPerDay);
            Assert.Equal(0, w.Goods.Stats.SubsidiesYesterday);
            Assert.Equal(0, w.Economy.LastDay.Amount(BudgetCategory.IndustrySubsidies));
        }

        // ------------------------------------------------------------ Spielstand

        [Fact]
        public void SaveLoad_PolicyRoundtrip_AndDeterministic()
        {
            var a = Town(beforeRun: w =>
            {
                w.Policy.Paint(new Vector2(150, -20), 80f, IndustryFocus.Processing);
                w.Policy.Paint(new Vector2(260, -20), 30f, IndustryFocus.Technology);
                w.Policy.SetSubsidy(IndustryFocus.Processing, 3);
                w.Policy.SetSubsidy(IndustryFocus.ConsumerGoods, 1);
            }, days: 5);
            Assert.True(a.Goods.Stats.SubsidiesYesterday > 0 || a.Goods.Firms.All(f => !f.Recipe.IsIndustry || f.Workers == 0));

            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.Equal(IndustryFocus.Processing, b.Policy.FocusAt(new Vector2(150, -20)));
            Assert.Equal(IndustryFocus.Technology, b.Policy.FocusAt(new Vector2(260, -20)));
            Assert.Equal(3, b.Policy.SubsidyLevel(IndustryFocus.Processing));
            Assert.Equal(1, b.Policy.SubsidyLevel(IndustryFocus.ConsumerGoods));
            Assert.Equal(a.Policy.Hectares(IndustryFocus.Processing), b.Policy.Hectares(IndustryFocus.Processing));
            Assert.Equal(a.Goods.Firms.Select(f => f.Yesterday.Subsidy), b.Goods.Firms.Select(f => f.Yesterday.Subsidy));
            Assert.Equal(SaveGame.StateHash(a), SaveGame.StateHash(b));

            for (int day = 0; day < 3; day++)
            {
                Run(a, SimulationClock.TicksPerDay);
                Run(b, SimulationClock.TicksPerDay);
                if (day == 1)
                    foreach (var w in new[] { a, b })
                    {
                        w.Policy.Paint(new Vector2(100, -40), 40f, IndustryFocus.Agriculture);
                        w.Policy.SetSubsidy(IndustryFocus.Agriculture, 2);
                    }
            }
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, "abweichende Abschnitte: " + string.Join(", ", diff));
            Assert.Equal(a.Economy.Balance, b.Economy.Balance);
        }

        [Fact]
        public void SaveLoad_MissingPolicySection_DefaultsToAutomatic()
        {
            var a = Town(beforeRun: w => { w.Policy.Paint(new Vector2(150, -20), 80f, IndustryFocus.OilGas); w.Policy.SetSubsidy(IndustryFocus.OilGas, 2); }, days: 2);
            var c = SaveGame.Capture(a);
            Assert.True(c.Remove("policy"));
            var b = SaveGame.Load(c.ToBytes());
            Assert.Equal(IndustryFocus.Auto, b.Policy.FocusAt(new Vector2(150, -20)));
            Assert.Equal(0, b.Policy.SubsidyLevel(IndustryFocus.OilGas));
            Run(b, SimulationClock.TicksPerDay);
            Assert.Equal(0, b.Goods.Stats.SubsidiesYesterday);
        }

        // ------------------------------------------------------------ Info: Ölfeld, Schwerpunkt Öl → Anteil Ölfirmen

        [Fact]
        public void Info_OilField_FocusOil_ShareOfOilFirms()
        {
            // Ölfeld im Osten der Industrie (x ≥ 150), sonst kein Öl
            void OilField(SimulationWorld w)
            {
                var layer = w.Resources.Layer(ResourceIds.Oil);
                for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                {
                    var p = layer.Origin + new Vector2((x + 0.5f) * layer.CellSize, (y + 0.5f) * layer.CellSize);
                    layer.RawValues[y * layer.Width + x] = p.X >= 150f && p.X <= 400f && p.Y < 0f && p.Y > -250f ? 0.7f : 0f;
                }
                layer.MarkChanged();
            }

            (int oil, int industry, string list, IReadOnlyList<string> rec) Measure(bool focus, int days)
            {
                var w = Town(setup: OilField, days: days, beforeRun: x =>
                {
                    if (!focus) return;
                    x.Policy.Paint(new Vector2(270, -20), 140f, IndustryFocus.OilGas);
                    x.Policy.SetSubsidy(IndustryFocus.OilGas, 1);
                });
                var ind = w.Goods.Firms.Where(f => f.Recipe.IsIndustry).ToList();
                var east = ind.Where(f => f.Building.Position.X >= 170f).ToList();
                string list = string.Join(", ", east.GroupBy(f => f.Recipe.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
                return (east.Count(f => f.Recipe.Id == "oil_well" || f.Recipe.Id == "refinery"), east.Count, list, w.Policy.Recommendations().ToList());
            }

            var before = Measure(false, 12);
            var after = Measure(true, 12);
            output.WriteLine($"ohne Schwerpunkt: {before.oil}/{before.industry} Öl-Betriebe über dem Ölfeld ({before.list})");
            output.WriteLine($"Schwerpunkt Öl + Förderung gering: {after.oil}/{after.industry} ({after.list})");
            output.WriteLine("Empfehlungen ohne Schwerpunkt: " + string.Join(" | ", before.rec));
            output.WriteLine("Empfehlungen mit Schwerpunkt:  " + string.Join(" | ", after.rec));
            Assert.True(after.industry > 0 && before.industry > 0);
            Assert.True(after.oil * before.industry > before.oil * after.industry,
                $"Anteil Öl mit Schwerpunkt {after.oil}/{after.industry} nicht höher als ohne {before.oil}/{before.industry}");
        }
    }
}
