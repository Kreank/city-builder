using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// <summary>Handel 2 (Etappe 8 Stufe 2): Gegenangebote, Regierungen, Bündnisse/Zollunion, Abstimmungen, Sanktionen, Diplomatie, Tourismus.</summary>
    public class TradeStage2Tests
    {
        readonly ITestOutputHelper output;
        public TradeStage2Tests(ITestOutputHelper output) => this.output = output;

        static readonly PlanetSettings Small = new PlanetSettings { Width = 180, Height = 90, TargetRegions = 120, TargetCountries = 16 };

        static Planet NewPlanet(int seed = 31, PlanetSettings settings = null)
        {
            var p = PlanetGenerator.Generate(seed, settings ?? Small);
            p.SetStartRegion(p.SuggestStartRegion());
            return p;
        }

        static TradeSystem NewTrade(Planet p, int seed = 5) => new TradeSystem(p, null, seed) { AiEnabled = false };

        static void Days(TradeSystem t, int days)
        {
            for (int i = 0; i < days; i++) t.SimulateDay();
        }

        /// <summary>NPC-Länder ohne Bündnis.</summary>
        static List<int> Unaligned(TradeSystem t) => t.Nations.Where(n => !n.IsPlayer && t.BlocOf(n.Id) == null).Select(n => n.Id).ToList();

        // ------------------------------------------------------------ Regierungen & Startbündnisse

        [Fact]
        public void Governments_And_InitialBlocs_ShapeTariffs()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            var forms = t.Nations.Where(n => !n.IsPlayer).GroupBy(n => n.Government).ToDictionary(g => g.Key, g => g.Count());
            output.WriteLine("Regierungen: " + string.Join(", ", forms.Select(kv => $"{Governments.DisplayName(kv.Key)} {kv.Value}")));
            Assert.True(forms.Count >= 3, "mehrere Regierungsformen");
            Assert.Equal(GovernmentForm.Republic, t.Nation(t.PlayerId).Government);
            // Autokratien schützen im Mittel stärker als Technokratien (Grundzoll)
            var active = t.Blocs.Where(b => b.Active).ToList();
            foreach (var b in active)
                output.WriteLine($"{b} · Außenzoll {b.ExternalTariff * 100:0} % · {string.Join(", ", b.Members.Select(m => t.Nation(m).Country.Name))}");
            Assert.NotEmpty(active);
            Assert.True(active.Count <= DiplomacyRules.InitialBlocs);
            foreach (var b in active)
            {
                Assert.True(b.Members.Count >= 2);
                foreach (int a in b.Members)
                {
                    Assert.Same(b, t.BlocOf(a));
                    foreach (int c in b.Members) if (a != c) Assert.Equal(0.0, t.TariffRate(a, c));
                }
            }
            var cu = active.FirstOrDefault(b => b.Kind == BlocKind.CustomsUnion);
            if (cu != null)
            {
                int outsider = t.Nations.First(n => !cu.IsMember(n.Id)).Id;
                foreach (int m in cu.Members) Assert.Equal(Math.Max(cu.ExternalTariff, t.Nation(m).Tariffs.HasOverride(outsider) ? t.Nation(m).Tariffs.Override(outsider) : 0), t.TariffRate(m, outsider), 6);
                Assert.Equal(cu.ExternalTariff, t.GeneralTariff(cu.Members[0]), 6);
            }
            // deterministisch
            var t2 = NewTrade(NewPlanet());
            Assert.Equal(string.Join("|", t.Blocs.Select(b => b.Name + ":" + string.Join(",", b.Members))), string.Join("|", t2.Blocs.Select(b => b.Name + ":" + string.Join(",", b.Members))));
        }

        // ------------------------------------------------------------ Gegenangebote

        [Fact]
        public void CounterOffer_NpcCounters_PlayerAccepts_AndRoundsAreLimited()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            t.CounterChanceOverride = 1.0;
            Days(t, 3);
            int player = t.PlayerId;
            var grain = GoodsCatalog.Grain;
            int food = (int)GoodsCategory.Food;
            int buyer = t.Nations.First(n => !n.IsPlayer).Id;
            var e = t.Nations[buyer].Economy;
            e.Stock[food] = e.Demand[food] * 2; // Knappheit
            t.SimulateDay();
            double domestic = t.CountryPrice(buyer, grain);
            var news = new List<TradeNews>();
            t.News += news.Add;

            var r = t.Execute(new ProposeContractCommand(player, buyer, TradeSide.Sell,
                new ContractTerms { Good = grain, UnitsPerWeek = 40, Weeks = 4, Price = Math.Round(domestic * 1.6, 2) }));
            var first = t.Offer(r.Id);
            t.SimulateDay();
            output.WriteLine($"Erstangebot: {first.Status} · {first.Reason}");
            Assert.Equal(OfferStatus.Rejected, first.Status);
            Assert.Contains("zu teuer", first.Reason);
            var counter = t.Offers.Single(o => o.CounterOf == first.Id);
            output.WriteLine($"Gegenangebot: {counter} Runde {counter.Round}");
            Assert.Equal(buyer, counter.From);
            Assert.Equal(player, counter.To);
            Assert.Equal(TradeSide.Buy, counter.Side);
            Assert.Equal(1, counter.Round);
            Assert.True(counter.Terms.Price < first.Terms.Price, "Käufer bietet weniger");
            Assert.Contains(news, n => n.Kind == TradeNewsKind.CounterOffer && n.Country == buyer);
            // Das Gegenangebot ist aus Sicht des Käufers annehmbar (gleiche Bewertung wie bei einem Angebot von uns)
            var probe = t.PreviewOffer(player, buyer, TradeSide.Sell, counter.Terms);
            Assert.True(t.Evaluate(buyer, probe).Utility >= DiplomacyRules.CounterMargin - 1e-4f);

            // Spieler nimmt an → Vertrag zu den Gegenbedingungen, Spieler verkauft
            var acc = t.Execute(new AcceptOfferCommand(player, counter.Id));
            Assert.True(acc.Ok, acc.Message);
            var c = t.Contract(acc.Id);
            Assert.Equal(player, c.Seller);
            Assert.Equal(counter.Terms.Price, c.Terms.Price, 6);

            // Runden: abwechselnd Gegenangebote, nach 6 Runden ist Schluss (gleiche Commands für Spieler und NPC)
            var start = t.Offer(t.Execute(new ProposeContractCommand(player, buyer, TradeSide.Sell,
                new ContractTerms { Good = grain, UnitsPerWeek = 10, Weeks = 2, Price = Math.Round(domestic * 1.3, 2) })).Id);
            var cur = start;
            for (int round = 1; round <= DiplomacyRules.MaxNegotiationRounds; round++)
            {
                int party = cur.To;
                var terms = cur.Terms.Clone();
                terms.Price = Math.Round(terms.Price * (party == player ? 1.02 : 0.98), 2);
                var res = t.Execute(new CounterOfferCommand(party, cur.Id, terms));
                Assert.True(res.Ok, res.Message);
                Assert.Equal(OfferStatus.Rejected, cur.Status);
                cur = t.Offer(res.Id);
                Assert.Equal(round, cur.Round);
            }
            var tooMany = t.Execute(new CounterOfferCommand(cur.To, cur.Id, cur.Terms));
            Assert.False(tooMany.Ok);
            var chain = new List<TradeOffer>();
            t.NegotiationChain(cur, chain);
            Assert.Equal(DiplomacyRules.MaxNegotiationRounds + 1, chain.Count);
            Assert.Same(start, chain[0]);
        }

        [Fact]
        public void CounterOffer_QuantityTooLarge_ReducesUnits_NoCounterWithoutNeed()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            t.CounterChanceOverride = 1.0;
            Days(t, 3);
            int player = t.PlayerId;
            var oil = GoodsCatalog.Oil;
            int energy = (int)GoodsCategory.Energy;
            int seller = t.Nations.First(n => !n.IsPlayer).Id;
            var e = t.Nations[seller].Economy;
            e.Stock[energy] = e.Demand[energy] * 80; // Überschuss, aber begrenzt
            t.SimulateDay();
            double price = Math.Round((t.CountryPrice(seller, oil) + t.Geography.Route(seller, player).UnitCost(oil)) * 1.3, 2);
            var o = t.Offer(t.Execute(new ProposeContractCommand(player, seller, TradeSide.Buy,
                new ContractTerms { Good = oil, UnitsPerWeek = 1e9, Weeks = 2, Price = price })).Id);
            var suggestion = t.SuggestCounter(seller, o);
            Assert.NotNull(suggestion);
            output.WriteLine($"Vorschlag: {suggestion.Describe()}");
            Assert.True(suggestion.UnitsPerWeek < 1e9 && suggestion.UnitsPerWeek >= 1);
            t.SimulateDay();
            Assert.Equal(OfferStatus.Rejected, o.Status);
            Assert.Contains("Menge zu groß", o.Reason);
            var counter = t.Offers.SingleOrDefault(x => x.CounterOf == o.Id);
            Assert.NotNull(counter);
            Assert.Equal(TradeSide.Sell, counter.Side);
            output.WriteLine($"Gegenangebot: {counter.Terms.Describe()}");
            Assert.True(counter.Terms.UnitsPerWeek < 1e9, "Menge auf die Lieferfähigkeit gekürzt");

            // Käufer ohne Bedarf: kein Gegenangebot (lässt sich nicht über Bedingungen lösen)
            int food = (int)GoodsCategory.Food;
            int full = t.Nations.Where(n => !n.IsPlayer && n.Id != seller).First().Id;
            var ef = t.Nations[full].Economy;
            ef.Stock[food] = ef.Demand[food] * 110;
            t.SimulateDay();
            var noNeed = t.Offer(t.Execute(new ProposeContractCommand(player, full, TradeSide.Sell,
                new ContractTerms { Good = GoodsCatalog.Grain, UnitsPerWeek = 30, Weeks = 2, Price = Math.Round(t.CountryPrice(full, GoodsCatalog.Grain) * 3, 2) })).Id);
            var s2 = t.SuggestCounter(full, noNeed);
            output.WriteLine($"ohne Bedarf: {(s2 == null ? "kein Vorschlag" : s2.Describe())} · Bewertung {t.Evaluate(full, noNeed).Reason}");
            Assert.Null(s2); // dreifacher Preis bei vollem Lager: zu weit auseinander für ein Gegenangebot
        }

        // ------------------------------------------------------------ Bündnisse

        [Fact]
        public void CustomsUnion_FoundByPlayer_CommonExternalTariff()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            int player = t.PlayerId;
            var free = Unaligned(t);
            Assert.True(free.Count >= 2);
            int a = free[0], outsider = free[1];
            var r = t.Execute(new FoundBlocCommand(player, BlocKind.CustomsUnion, "Testunion", a));
            Assert.True(r.Ok, r.Message);
            var bloc = t.Bloc(r.Id);
            Assert.False(bloc.Active);
            var inv = t.Invitations.Single(x => x.BlocId == bloc.Id && x.To == a);
            Assert.True(t.Execute(new AnswerInvitationCommand(a, inv.Id, true)).Ok);
            Assert.True(bloc.Active);
            Assert.Same(bloc, t.BlocOf(a));
            Assert.Equal(0.0, t.TariffRate(player, a));
            Assert.Equal(0.0, t.TariffRate(a, player));

            // gemeinsamer Außenzoll: Vorsitz setzt, beide erheben ihn gegen Außenstehende; eigener Grundzoll ist gesperrt
            Assert.True(t.Execute(new SetBlocTariffCommand(player, bloc.Id, 0.12)).Ok);
            Assert.False(t.Execute(new SetBlocTariffCommand(a, bloc.Id, 0.3)).Ok, "nur der Vorsitz");
            Assert.Equal(0.12, t.TariffRate(player, outsider), 6);
            Assert.Equal(Math.Max(0.12, t.Nation(a).Tariffs.HasOverride(outsider) ? t.Nation(a).Tariffs.Override(outsider) : 0), t.TariffRate(a, outsider), 6);
            Assert.Equal(0.12, t.GeneralTariff(player), 6);
            Assert.False(t.Execute(new SetTariffCommand(player, -1, 0.05)).Ok);

            // Austritt: Bündnis mit einem Mitglied löst sich auf, Zölle wieder eigene
            float rel = t.Relation(player, a);
            Assert.True(t.Execute(new LeaveBlocCommand(player, bloc.Id)).Ok);
            Assert.True(bloc.Dissolved);
            Assert.True(t.Relation(player, a) < rel);
            Assert.Null(t.BlocOf(a));
            Assert.Equal(t.Nation(player).Tariffs.General, t.TariffRate(player, outsider), 6);
        }

        [Fact]
        public void Admission_VoteOfMembers_DependsOnRelations()
        {
            (bool member, BlocVote vote, TradeSystem t) Run(float relationDelta)
            {
                var t = NewTrade(NewPlanet());
                int player = t.PlayerId;
                var bloc = t.Blocs.First(b => b.Active);
                foreach (int m in bloc.Members) t.AdjustRelation(m, player, relationDelta);
                var r = t.Execute(new ApplyToBlocCommand(player, bloc.Id));
                Assert.True(r.Ok, r.Message);
                Days(t, 2);
                var v = t.Vote(r.Id);
                output.WriteLine($"Beziehung {relationDelta:+0;−0}: {bloc.Name} · {v.Result} · Stimmen {string.Join(", ", v.Ballots.Select(kv => $"{t.Nation(kv.Key).Country.Name} {TradeSystem.ChoiceText(kv.Value)}"))}");
                return (bloc.IsMember(player), v, t);
            }
            var good = Run(+70f);
            Assert.True(good.member);
            Assert.Equal(VoteStatus.Passed, good.vote.Status);
            int someMember = good.t.BlocOf(good.t.PlayerId).Members.First(m => m != good.t.PlayerId);
            Assert.Equal(0.0, good.t.TariffRate(someMember, good.t.PlayerId));
            var bad = Run(-70f);
            Assert.False(bad.member);
            Assert.Equal(VoteStatus.Failed, bad.vote.Status);
        }

        [Fact]
        public void Sanction_PassedByVote_BlocksTrade_TariffAndTourism()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            int player = t.PlayerId;
            var free = Unaligned(t);
            int a = free[0], b = free[1], x = free[2];
            var found = t.Execute(new FoundBlocCommand(player, BlocKind.TradeAlliance, null, a));
            var bloc = t.Bloc(found.Id);
            Assert.True(t.Execute(new AnswerInvitationCommand(a, t.Invitations.Single(i => i.To == a).Id, true)).Ok);
            var invB = t.Execute(new InviteToBlocCommand(player, bloc.Id, b));
            Assert.True(t.Execute(new AnswerInvitationCommand(b, invB.Id, true)).Ok);
            Assert.Equal(3, bloc.Members.Count);
            // laufender Vertrag mit dem späteren Ziel
            var o = t.Execute(new ProposeContractCommand(player, x, TradeSide.Buy, new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 5, Weeks = 8, Price = 200 }));
            var contract = t.Contract(t.Execute(new AcceptOfferCommand(x, o.Id)).Id);
            Assert.True(contract.IsActive);
            t.AdjustRelation(a, x, -150f);
            t.AdjustRelation(b, x, -150f);
            var news = new List<TradeNews>();
            t.News += news.Add;

            var vote = t.Vote(t.Execute(new ProposeVoteCommand(player, bloc.Id, VoteKind.Sanction, x)).Id);
            Assert.Equal(VoteChoice.Yes, vote.Ballots[player]);
            t.SimulateDay();
            output.WriteLine($"Sanktion: {vote.Result}");
            Assert.Equal(VoteStatus.Passed, vote.Status);
            Assert.True(bloc.Sanctions(x));
            Assert.True(t.IsSanctioned(player, x) && t.IsSanctioned(x, a));
            Assert.Equal(DiplomacyRules.SanctionTariff, t.TariffRate(player, x), 6);
            Assert.Equal(DiplomacyRules.SanctionTariff, t.TariffRate(b, x), 6);
            Assert.Equal(ContractStatus.Cancelled, contract.Status);
            Assert.Equal(-2, contract.CancelledBy);
            Assert.Equal(0.0, contract.Penalties);
            Assert.False(t.Execute(new ProposeContractCommand(x, player, TradeSide.Sell, new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 5, Weeks = 2, Price = 200 })).Ok);
            Assert.Contains(news, n => n.Kind == TradeNewsKind.Sanction && n.Country == x);
            t.SimulateDay();
            Assert.Equal(0.0, t.TourismFlow(x, a));
            Assert.Equal(0.0, t.TourismFlow(a, x));

            // Aufheben per Abstimmung (Beziehungen wieder gut)
            t.AdjustRelation(a, x, 200f);
            t.AdjustRelation(b, x, 200f);
            var lift = t.Vote(t.Execute(new ProposeVoteCommand(player, bloc.Id, VoteKind.LiftSanction, x)).Id);
            t.SimulateDay();
            Assert.Equal(VoteStatus.Passed, lift.Status);
            Assert.False(t.IsSanctioned(player, x));
        }

        // ------------------------------------------------------------ Diplomatie

        [Fact]
        public void Diplomacy_Gift_Diminishes_Protest_Threat()
        {
            var p = NewPlanet();
            var t = NewTrade(p);
            int player = t.PlayerId;
            int npc = t.Nations.First(n => !n.IsPlayer).Id;
            float r0 = t.Relation(player, npc);
            Assert.False(t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Gift, 10)).Ok, "Mindestbetrag");
            Assert.True(t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Gift, 50000)).Ok);
            float r1 = t.Relation(player, npc);
            Assert.True(t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Gift, 50000)).Ok);
            float r2 = t.Relation(player, npc);
            output.WriteLine($"Geschenke: {r0:0.0} → {r1:0.0} → {r2:0.0}");
            Assert.True(r1 - r0 > 4f);
            Assert.True(r2 - r1 < r1 - r0, "zweites Geschenk wirkt weniger");
            Assert.Contains(t.Nation(npc).Memory, m => m.Kind == TradeMemoryKind.Gift && m.Partner == player);

            // Protest gegen Sonderzoll, Drohung eines kleinen Stadtstaats
            t.Execute(new SetTariffCommand(npc, player, 0.3));
            var protest = t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Protest));
            Assert.True(protest.Ok);
            output.WriteLine(protest.Message);
            Assert.False(t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Threat)).Ok, "Abkühlzeit");
            Days(t, DiplomacyRules.DiplomacyCooldownDays);
            float before = t.Relation(player, npc);
            float trust = t.Nation(npc).Trust[player];
            var threat = t.Execute(new DiplomacyCommand(player, npc, DiplomacyAction.Threat));
            Assert.True(threat.Ok);
            output.WriteLine(threat.Message);
            Assert.True(t.Relation(player, npc) < before - 5f);
            Assert.True(t.Nation(npc).Trust[player] < trust);
        }

        // ------------------------------------------------------------ Tourismus

        [Fact]
        public void Tourism_FlowsToAttractiveCountries_ExportsCount_EmbargoStops()
        {
            var p = NewPlanet(2024, new PlanetSettings());
            var t = NewTrade(p);
            Days(t, 30);
            int T = (int)GoodsCategory.Tourism;
            var npcs = t.Nations.Where(n => !n.IsPlayer).ToList();
            double total = npcs.Sum(n => n.TourismExportValue);
            Assert.True(total > 0);
            // Anteil am Tourismus-Export nach Tourismus-Stärke: oberes Drittel gegen unteres Drittel (je Kopf)
            var ranked = npcs.OrderByDescending(n => n.Country.Profile.Raw[(int)Strength.Tourism]).ToList();
            int third = ranked.Count / 3;
            double PerCapita(NationTrade n) => n.TourismInYesterday / Math.Max(1.0, n.Economy.Population);
            double top = ranked.Take(third).Average(PerCapita), bottom = ranked.Skip(ranked.Count - third).Average(PerCapita);
            output.WriteLine($"Tourismus-Export 30 Tage {total / 1e9:0.00} G€ · Einreisen je Kopf/Tag: starke Länder {top * 1000:0.00} ‰, schwache {bottom * 1000:0.00} ‰");
            foreach (var n in ranked.Take(5))
                output.WriteLine($"  {n.Country.Name,-14} Stärke {n.Country.Profile.Raw[(int)Strength.Tourism]:0.00} · Einreise {n.TourismInYesterday:0} E/Tag · Ausreise {n.TourismOutYesterday:0} E/Tag · Preis {n.Economy.Price[T]:0.00}");
            Assert.True(top > bottom, "attraktive Länder bekommen mehr Touristen");
            // Embargo (Beziehung unter −50) stoppt den Reiseverkehr
            int o = ranked.Last().Id, d = ranked.First().Id;
            Assert.True(t.TourismFlow(o, d) > 0);
            t.AdjustRelation(o, d, -300f);
            t.SimulateDay();
            Assert.Equal(0.0, t.TourismFlow(o, d));
        }

        [Fact]
        public void Tourism_CityVisitors_FromTrade_IntoVisitorList()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = 31, Planet = Small, GenerateTerrain = false, SizeMeters = 1024f,
            });
            Assert.NotNull(w.Trade.PlayerTourismPotential);
            Assert.NotNull(w.Aviation.TouristDemandSource);
            for (int i = 0; i < SimulationClock.TicksPerDay * 4; i++) w.Step();
            var t = w.Trade;
            output.WriteLine($"Potenzial {t.PlayerTourismPotentialYesterday:0.0}/Tag · Markt × {t.PlayerTourismMarket:0.00} · Straße {t.VisitorsByMode(TourismMode.Road):0.0} · " +
                             $"Bahn {t.VisitorsByMode(TourismMode.Rail):0.0} · Schiff {t.VisitorsByMode(TourismMode.Ship):0.0} · Flug {t.VisitorsByMode(TourismMode.Air):0.0} · " +
                             $"ohne Anreise {t.VisitorsByMode(TourismMode.Lost):0.0} · Besucher in der Stadt {w.Aviation.VisitorsPresent:0.0} · gesamt bodengebunden {t.GroundVisitorsTotal}");
            Assert.True(t.PlayerTourismPotentialYesterday > 0);
            Assert.InRange(t.PlayerTourismMarket, DiplomacyRules.MarketFactorMin, DiplomacyRules.MarketFactorMax);
            Assert.True(t.PlayerVisitorsYesterday + t.VisitorsByMode(TourismMode.Lost) > 0);
            Assert.Equal(0.0, t.CityAirTourists); // kein Flughafen
            Assert.Equal((float)t.CityAirTourists, w.Aviation.DemandTourists);
            if (t.GroundVisitorsTotal > 0) Assert.True(w.Aviation.VisitorsPresent > 0, "Besucher in der Besucherliste");
            Assert.True(t.Nation(t.PlayerId).TourismExportValue > 0 || t.VisitorsByMode(TourismMode.Lost) > 0);
        }

        // ------------------------------------------------------------ Spielstand

        [Fact]
        public void SaveLoad_Stage2_Bitwise_AndOldVersionLoads()
        {
            var a = SimulationWorld.CreateDefault(new WorldSettings
            {
                GenerateWorld = true, WorldSeed = 31, Planet = Small, GenerateTerrain = false, SizeMeters = 1024f,
            });
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) a.Step();
            var t = a.Trade;
            t.AiEnabled = true;
            int player = t.PlayerId;
            var free = Unaligned(t);
            var bloc = t.Bloc(t.Execute(new FoundBlocCommand(player, BlocKind.CustomsUnion, "Speicherunion", free[0])).Id);
            t.Execute(new DiplomacyCommand(player, free[1], DiplomacyAction.Gift, 20000));
            var offer = t.Execute(new ProposeContractCommand(player, free[1], TradeSide.Sell,
                new ContractTerms { Good = GoodsCatalog.Steel, UnitsPerWeek = 10, Weeks = 3, Price = Math.Round(t.CountryPrice(free[1], GoodsCatalog.Steel) * 1.5, 2) }));
            var active = t.Blocs.FirstOrDefault(x => x.Active && x.Id != bloc.Id);
            if (active != null) t.Submit(new ApplyToBlocCommand(player, active.Id)); // Command in der Warteschlange
            for (int i = 0; i < SimulationClock.TicksPerDay / 2; i++) a.Step();

            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            void Same(string when)
            {
                var ha = SaveGame.SectionHashes(a);
                var hb = SaveGame.SectionHashes(b);
                var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
                Assert.True(diff.Count == 0, $"{when}: abweichend {string.Join(", ", diff)}");
            }
            Same("nach dem Laden");
            for (int i = 0; i < SimulationClock.TicksPerDay * 9; i++) { a.Step(); b.Step(); }
            Same("nach 9 Tagen");
            Assert.Equal(a.Trade.Blocs.Count, b.Trade.Blocs.Count);
            Assert.Equal(a.Trade.Offers.Count(o => o.IsCounter), b.Trade.Offers.Count(o => o.IsCounter));
            output.WriteLine($"Bündnisse {a.Trade.Blocs.Count}, Abstimmungen {a.Trade.Votes.Count}, Gegenangebote {a.Trade.Offers.Count(o => o.IsCounter)}, " +
                             $"Besucher bodengebunden {a.Trade.GroundVisitorsTotal}, Angebot #{offer.Id}: {a.Trade.Offer(offer.Id)?.Status}");

            // alter Stand (v1 = Anfang des Abschnitts ohne Handel-2-Teil): lädt, Bündnisse wie beim Erzeugen
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                a.Trade.Write(new SaveWriter(ms));
                bytes = ms.ToArray();
            }
            var fresh = new TradeSystem(a.Planet, null, 5);
            fresh.AttachCity(null, null);
            int initial = fresh.Blocs.Count;
            using (var ms = new MemoryStream(bytes))
                fresh.Read(new SaveReader(ms), 1);
            Assert.Equal(a.Trade.Day, fresh.Day);
            Assert.Equal(initial, fresh.Blocs.Count);
            Assert.Empty(fresh.Votes);
            Assert.All(fresh.Offers, o => Assert.Equal(0, o.Round));
            fresh.SimulateDay(); // läuft weiter
        }

        // ------------------------------------------------------------ Info: ein Jahr Welt mit Handel 2

        [Fact]
        public void Info_OneYear_Negotiation_Blocs_Tourism()
        {
            var sw = Stopwatch.StartNew();
            var p = NewPlanet(2024, new PlanetSettings());
            var t = new TradeSystem(p, null, 9);
            int counters = 0, counterContracts = 0, sanctions = 0, admissions = 0, failedVotes = 0;
            var seenOffers = new HashSet<int>();
            var seenVotes = new HashSet<int>();
            for (int d = 0; d < 365; d++)
            {
                t.SimulateDay();
                foreach (var o in t.Offers)
                {
                    if (!o.IsCounter || o.IsOpen || !seenOffers.Add(o.Id)) continue;
                    counters++;
                    if (o.Status == OfferStatus.Accepted) counterContracts++;
                }
                foreach (var v in t.Votes)
                {
                    if (v.IsOpen || !seenVotes.Add(v.Id)) continue;
                    if (v.Status == VoteStatus.Failed) failedVotes++;
                    else if (v.Kind == VoteKind.Sanction) sanctions++;
                    else if (v.Kind == VoteKind.Admit) admissions++;
                }
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            int contracts = t.Contracts.Count + t.History.Count;
            double tourism = t.Nations.Sum(n => n.TourismExportValue), exports = t.Nations.Sum(n => n.ExportValue);
            output.WriteLine($"1 Jahr ({p.Countries.Count} Länder): {ms:0} ms · Verträge {contracts} · Gegenangebote {counters} (angenommen {counterContracts}) · " +
                             $"Aufnahmen {admissions}, Sanktionen beschlossen {sanctions} (am Ende in Kraft {t.Blocs.Sum(b => b.Sanctioned.Count)}), abgelehnte Abstimmungen {failedVotes} · " +
                             $"Tourismus {tourism / exports * 100:0.0} % der Ausfuhr · Länder in Bündnissen {t.Nations.Count(n => t.BlocOf(n.Id)?.Active == true)}/{t.Nations.Count}");
            foreach (var b in t.Blocs)
                output.WriteLine($"  {b}{(b.Dissolved ? " (aufgelöst)" : "")} · Außenzoll {b.ExternalTariff * 100:0} % · Sanktionen gegen {b.Sanctioned.Count}");
            Assert.True(contracts > 50);
            Assert.True(counters > 0, "NPC verhandeln untereinander");
            Assert.True(tourism > 0);
            Assert.True(ms < 20000);
        }
    }
}
