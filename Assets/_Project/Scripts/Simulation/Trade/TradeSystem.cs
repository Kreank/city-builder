using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel zwischen Ländern (Etappe 8, Stufe 1): Weltmarkt aus den Länder-Volkswirtschaften, Handelswege, Angebote,
    /// Verträge mit Lieferungen und Lieferzeit, Beziehungen, Vertrauen, Zölle und Handelsabkommen; NPC-Länder als Utility AI
    /// (<c>TradeSystem.Ai.cs</c>), Anbindung der Spielerstadt über das Grenzlager (<see cref="CityTradeDepot"/>).
    /// <para>
    /// <b>Gleiche Schnittstelle für alle:</b> jede Zustandsänderung läuft über ein <see cref="TradeCommand"/>
    /// (<see cref="Submit"/> bzw. <see cref="Execute"/>) – der Spieler (UI) und die NPC-KI benutzen dieselben Klassen.
    /// </para>
    /// <b>Takt:</b> Commands jeden Tick; einmal je Spieltag (<see cref="DayStep"/>) in fester Reihenfolge: Ankünfte → Versand
    /// (Fehlmengen → Strafe) → Vertragsende → Lagebild → Antworten auf Angebote → Wochenentscheidung (je Land jeden 7. Tag,
    /// gestaffelt) → Verfall → Weltpreise → wöchentlich Beziehungen/Zölle → Grenzlager. Zufall nur über <see cref="SimRandom"/>,
    /// Länder und Verträge in Id-Reihenfolge → deterministisch.
    /// </summary>
    public sealed partial class TradeSystem : ISimulationSystem
    {
        public int TickInterval => 1;

        readonly SimulationClock clock;
        readonly SimRandom rng;
        readonly int seed;
        readonly List<NationTrade> nations = new List<NationTrade>();
        readonly float[,] relation, relationBase;
        readonly int[,] agreements;
        readonly double[,] priceOffset;
        readonly double[] worldPrice, worldPriceWeekAgo;

        readonly List<TradeOffer> offers = new List<TradeOffer>();
        readonly Dictionary<int, TradeOffer> offerById = new Dictionary<int, TradeOffer>();
        readonly List<TradeContract> contracts = new List<TradeContract>();
        readonly Dictionary<int, TradeContract> contractById = new Dictionary<int, TradeContract>();
        readonly List<TradeContract> history = new List<TradeContract>();
        readonly List<TradeDelivery> inTransit = new List<TradeDelivery>();
        readonly Queue<TradeCommand> queue = new Queue<TradeCommand>();
        readonly List<LoggedCommand> log = new List<LoggedCommand>();
        int nextOfferId = 1, nextContractId = 1, nextDeliveryId = 1;

        public const int HistoryCapacity = 300;
        public const int LogCapacity = 1000;

        public TradeSystem(Planet planet, SimulationClock clock = null, int seed = 1)
        {
            Planet = planet ?? throw new ArgumentNullException(nameof(planet));
            if (planet.Economy == null) throw new ArgumentException("Planet ohne Länder-Volkswirtschaften.", nameof(planet));
            this.clock = clock;
            this.seed = seed;
            rng = new SimRandom(unchecked(seed * 6007 + 4231));
            Geography = new TradeGeography(planet);
            int n = planet.Countries.Count;
            foreach (var c in planet.Countries) nations.Add(new NationTrade(c, n, seed));
            relation = new float[n, n];
            relationBase = new float[n, n];
            agreements = new int[n, n];
            InitRelations();
            priceOffset = new double[n, GoodsCatalog.Count];
            for (int c = 0; c < n; c++)
                foreach (var g in GoodsCatalog.All) priceOffset[c, g.Index] = TradeGoods.PriceOffset(seed, c, g);
            worldPrice = new double[GoodsCatalog.Count];
            worldPriceWeekAgo = new double[GoodsCatalog.Count];
            AllocateAi(n);
            AllocateStage2(n); // Handel 2: Bündnisse beim Erzeugen, Tourismus-Entfernungen, Diplomatie
            UpdateWorldPrices();
            Array.Copy(worldPrice, worldPriceWeekAgo, worldPrice.Length);
        }

        // ------------------------------------------------------------ Abfragen (UI/Tests)

        public Planet Planet { get; }
        public TradeGeography Geography { get; }
        public IReadOnlyList<NationTrade> Nations => nations;
        public NationTrade Nation(int id) => id >= 0 && id < nations.Count ? nations[id] : null;
        /// <summary>Land des Spielers (−1 ohne Startregion).</summary>
        public int PlayerId => Planet.PlayerCountryId;
        public bool IsPlayer(int party) => party >= 0 && party == PlayerId;
        /// <summary>Zähler der abgerechneten Handelstage (beginnt bei 0).</summary>
        public int Day { get; private set; }
        /// <summary>Wird bei jeder Änderung erhöht (für die Darstellung).</summary>
        public int Version { get; private set; }
        /// <summary>NPC-KI an/aus (Tests, Vergleichsläufe). Antworten auf Angebote laufen auch ohne.</summary>
        public bool AiEnabled { get; set; } = true;
        /// <summary>Grenzlager der Spielerstadt (null ohne <see cref="AttachCity"/>).</summary>
        public CityTradeDepot City { get; private set; }

        public IReadOnlyList<TradeOffer> Offers => offers;
        public IReadOnlyList<TradeContract> Contracts => contracts;
        /// <summary>Beendete Verträge (erfüllt/gekündigt), die letzten <see cref="HistoryCapacity"/>.</summary>
        public IReadOnlyList<TradeContract> History => history;
        public IReadOnlyList<TradeDelivery> InTransit => inTransit;
        public TradeOffer Offer(int id) => offerById.TryGetValue(id, out var o) ? o : null;
        public TradeContract Contract(int id) => contractById.TryGetValue(id, out var c) ? c : null;

        /// <summary>Ausgeführte Commands (Tag, Command, Ergebnis) – Grundlage für Replays; die letzten <see cref="LogCapacity"/>.</summary>
        public IReadOnlyList<LoggedCommand> CommandLog => log;

        public readonly struct LoggedCommand
        {
            public readonly int Day;
            public readonly TradeCommand Command;
            public readonly TradeResult Result;
            public LoggedCommand(int day, TradeCommand command, TradeResult result) { Day = day; Command = command; Result = result; }
        }

        /// <summary>Beziehung zweier Länder (−100..100, symmetrisch).</summary>
        public float Relation(int a, int b) => a == b ? 100f : relation[a, b];

        /// <summary>Haben a und b ein laufendes Handelsabkommen (Zollfreiheit)?</summary>
        public bool HasAgreement(int a, int b) => a != b && agreements[a, b] > 0;

        /// <summary>Zollsatz, den <paramref name="importer"/> auf Ware aus <paramref name="exporter"/> erhebt (0 mit Abkommen).</summary>
        public double TariffRate(int importer, int exporter)
        {
            if (importer == exporter || importer < 0 || exporter < 0) return 0.0;
            if (HasAgreement(importer, exporter)) return 0.0;
            double? bloc = BlocTariff(importer, exporter); // Handel 2: Binnenzoll 0, Außenzoll, Sanktion
            if (bloc.HasValue) return bloc.Value;
            return nations[importer].Tariffs.RateFor(exporter);
        }

        /// <summary>Preis eines Gutes in einem Land (€/E): Grundpreis × Preisindex der Kategorie × (1 ± Streuung).</summary>
        public double CountryPrice(int country, GoodType g)
        {
            var e = nations[country].Economy;
            int c = (int)TradeGoods.CategoryOf(g);
            return g.BasePrice * e.Price[c] / GoodsCategories.BasePrice[c] * (1.0 + priceOffset[country, g.Index]);
        }

        /// <summary>Weltpreis (€/E): handelsgewichtetes Mittel der Länderpreise (Gewicht = Handelsvolumen + 2 % der Produktion).</summary>
        public double WorldPrice(GoodType g) => worldPrice[g.Index];
        /// <summary>Weltpreis vor einer Woche (Trend).</summary>
        public double WorldPriceWeekAgo(GoodType g) => worldPriceWeekAgo[g.Index];

        /// <summary>Weltpreis, wie ihn die Stadt an den Außenanschlüssen sieht (begrenzt auf 0,5–2 × G5-Grundpreis).</summary>
        public float CityWorldPrice(GoodType g) =>
            (float)Math.Clamp(worldPrice[g.Index], g.BasePrice * TradeRules.CityWorldPriceMin, g.BasePrice * TradeRules.CityWorldPriceMax);

        /// <summary>Preis je Einheit eines Vertrags heute (fest bzw. Weltpreis × Faktor).</summary>
        public double UnitPrice(ContractTerms t) =>
            t.Good == null ? 0.0 : t.PriceMode == PriceMode.Fixed ? t.Price : worldPrice[t.Good.Index] * t.Price;

        /// <summary>Offene Angebote an ein Land (inkl. „an alle“, außer eigenen).</summary>
        public void OffersTo(int party, List<TradeOffer> result)
        {
            result.Clear();
            foreach (var o in offers)
                if (o.IsOpen && o.From != party && (o.To == party || o.To < 0))
                    result.Add(o);
        }

        /// <summary>Aktive Verträge eines Landes.</summary>
        public void ContractsOf(int party, List<TradeContract> result)
        {
            result.Clear();
            foreach (var c in contracts) if (c.Involves(party)) result.Add(c);
        }

        /// <summary>Menge, die der Spieler in den nächsten <paramref name="horizonDays"/> Tagen für Exportverträge dieses Gutes versenden muss.</summary>
        public double PlayerExportDue(GoodType g, int horizonDays)
        {
            double sum = 0;
            int player = PlayerId;
            foreach (var c in contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || c.Seller != player || c.Good != g || c.AllDispatched) continue;
                if (c.NextDispatchDay > Day + horizonDays) continue;
                // alle Lieferwochen im Horizont
                int weeks = Math.Min(c.Terms.Weeks - c.WeeksDispatched, 1 + (Day + horizonDays - c.NextDispatchDay) / TradeRules.DaysPerWeek);
                sum += c.Terms.UnitsPerWeek * Math.Max(1, weeks);
            }
            if (ExtraExportDue != null) sum += ExtraExportDue(g, horizonDays); // Etappe 7 Reich: interne Lieferungen
            return sum;
        }

        // --- Etappe 7 Reich: interne Lieferungen aus der aktiven Stadt laufen wie Exportverträge über das Grenzlager
        /// <summary>Zusätzliche Exportmenge (Gut, Horizont in Tagen) – Hook des Reichs; null = keine.</summary>
        public Func<GoodType, int, double> ExtraExportDue { get; set; }
        /// <summary>Preis für Firmen, wenn kein Exportvertrag das Gut verlangt (Verrechnungspreis der internen Lieferung); null/0 = keiner.</summary>
        public Func<GoodType, double> ExtraExportPrice { get; set; }

        /// <summary>Beziehung zweier Länder verändern (Reich: Landesgründung, Gebietsansprüche, Anerkennung).</summary>
        internal void AdjustRelation(int a, int b, float delta)
        {
            if (a < 0 || b < 0 || a >= nations.Count || b >= nations.Count) return;
            AddRelation(a, b, delta);
            Version++;
        }
        // --- Ende Etappe 7

        /// <summary>Vertragspreis, den Firmen für Exportware bekommen: Preis des am frühesten fälligen Exportvertrags (0 = keiner).</summary>
        public double PlayerExportPrice(GoodType g)
        {
            TradeContract best = null;
            int player = PlayerId;
            foreach (var c in contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || c.Seller != player || c.Good != g || c.AllDispatched) continue;
                if (best == null || c.NextDispatchDay < best.NextDispatchDay) best = c;
            }
            return best != null ? UnitPrice(best.Terms) : ExtraExportPrice?.Invoke(g) ?? 0.0; // Etappe 7 Reich
        }

        // ------------------------------------------------------------ Stadt anbinden

        /// <summary>
        /// Spielerstadt anbinden: Grenzlager als <see cref="GoodsSystem.TradeGateway"/>, Weltpreis der Märkte aus den Ländern,
        /// Buchungen in der Stadtkasse. <paramref name="goods"/>/<paramref name="treasury"/> dürfen null sein (Tests).
        /// </summary>
        public CityTradeDepot AttachCity(GoodsSystem goods, EconomySystem treasury)
        {
            City = new CityTradeDepot(this, goods, treasury);
            if (goods != null)
            {
                goods.TradeGateway = City;
                goods.WorldPriceSource = CityWorldPrice;
                goods.RefreshWorldPrices();
            }
            return City;
        }

        // ------------------------------------------------------------ Commands

        /// <summary>Command einreihen (ausgeführt im nächsten Tick, Eingangsreihenfolge).</summary>
        public void Submit(TradeCommand cmd)
        {
            if (cmd != null) queue.Enqueue(cmd);
        }

        /// <summary>Command sofort ausführen und protokollieren.</summary>
        public TradeResult Execute(TradeCommand cmd)
        {
            if (cmd == null) return TradeResult.Fail("kein Command");
            TradeResult r;
            if (Nation(cmd.Party) == null) r = TradeResult.Fail("unbekanntes Land");
            else r = cmd.Apply(this);
            log.Add(new LoggedCommand(Day, cmd, r));
            if (log.Count > LogCapacity) log.RemoveRange(0, log.Count - LogCapacity);
            LastResult = r;
            Version++;
            return r;
        }

        /// <summary>
        /// Nachrichten, die den Spieler betreffen (für Medien/HUD): Vertrag geschlossen, Embargo (Beziehung fällt unter −50),
        /// Zollerhöhung eines anderen Landes gegen uns. Handler dürfen keine Commands ausführen.
        /// </summary>
        public event Action<TradeNews> News;

        void Report(TradeNewsKind kind, int country, double value, string detail)
        {
            if (country < 0 || country >= nations.Count) return;
            News?.Invoke(new TradeNews(kind, country, nations[country].Country.Name, value, detail, Day));
        }

        /// <summary>Ergebnis des zuletzt ausgeführten Commands (UI-Rückmeldung).</summary>
        public TradeResult LastResult { get; private set; }

        public void ProcessCommands()
        {
            while (queue.Count > 0) Execute(queue.Dequeue());
        }

        internal TradeResult DoPropose(int from, int to, TradeSide side, ContractTerms t, int validDays)
        {
            if (t == null) return TradeResult.Fail("keine Bedingungen");
            if (to == from) return TradeResult.Fail("nicht an sich selbst");
            if (to >= nations.Count || to < -1) return TradeResult.Fail("unbekanntes Zielland");
            if (to >= 0 && IsSanctioned(from, to)) return TradeResult.Fail(SanctionText(from, to)); // Handel 2
            if (t.Weeks < TradeRules.MinWeeks || t.Weeks > TradeRules.MaxWeeks) return TradeResult.Fail($"Laufzeit {TradeRules.MinWeeks}–{TradeRules.MaxWeeks} Wochen");
            if (t.Kind == ContractKind.Agreement)
            {
                if (to < 0) return TradeResult.Fail("Handelsabkommen nur mit einem Land");
                if (HasAgreement(from, to)) return TradeResult.Fail("Abkommen besteht bereits");
                t.Good = null;
            }
            else
            {
                if (t.Good == null || !TradeGoods.IsTradeable(TradeGoods.CategoryOf(t.Good))) return TradeResult.Fail("Gut nicht handelbar");
                if (!(t.UnitsPerWeek > 0)) return TradeResult.Fail("Menge muss > 0 sein");
                if (!(t.Price > 0)) return TradeResult.Fail("Preis muss > 0 sein");
                t.PenaltyShare = Math.Clamp(t.PenaltyShare, 0.0, 1.0);
            }
            var o = new TradeOffer
            {
                Id = nextOfferId++, From = from, To = to, Side = side, Terms = t.Clone(), CreatedDay = Day,
                ExpiresDay = Day + Math.Max(1, validDays), Status = OfferStatus.Open,
                // asynchron: zwischen zwei Handelstagen gestellte Angebote werden in der nächsten Nacht beantwortet,
                // während des Handelstags (NPC-KI) gestellte erst in der übernächsten
                AnswerDay = inDayStep ? Day + 1 : Day,
            };
            offers.Add(o);
            offerById[o.Id] = o;
            return new TradeResult(true, $"Angebot #{o.Id} gestellt", o.Id);
        }

        internal TradeResult DoAccept(int party, int offerId, string reason)
        {
            var o = Offer(offerId);
            if (o == null || !o.IsOpen) return TradeResult.Fail("Angebot nicht mehr offen");
            if (o.From == party) return TradeResult.Fail("eigenes Angebot");
            if (o.To >= 0 && o.To != party) return TradeResult.Fail("Angebot gilt einem anderen Land");
            if (o.Terms.Kind == ContractKind.Agreement && HasAgreement(o.From, party)) return TradeResult.Fail("Abkommen besteht bereits");
            if (IsSanctioned(o.From, party)) return TradeResult.Fail(SanctionText(o.From, party)); // Handel 2
            int seller = o.Side == TradeSide.Sell ? o.From : party;
            int buyer = seller == o.From ? party : o.From;
            // Vorlauf: der Spieler als Verkäufer braucht eine Woche, um Ware am Außenanschluss zu sammeln
            int start = Day + 1 + (IsPlayer(seller) && o.Terms.Kind == ContractKind.Supply ? TradeRules.DaysPerWeek - 1 : 0);
            var c = new TradeContract
            {
                Id = nextContractId++, Terms = o.Terms.Clone(), Seller = seller, Buyer = buyer, OfferId = o.Id,
                StartDay = start, NextDispatchDay = start, Status = ContractStatus.Active, Route = Geography.Route(seller, buyer),
            };
            contracts.Add(c);
            contractById[c.Id] = c;
            o.Status = OfferStatus.Accepted;
            o.RespondedBy = party;
            o.ContractId = c.Id;
            o.Reason = reason ?? "angenommen";
            if (c.Involves(PlayerId))
                Report(TradeNewsKind.ContractSigned, c.Partner(PlayerId), c.Terms.Weeks,
                    c.Kind == ContractKind.Agreement ? "ein Handelsabkommen" : c.Good.Name);
            if (c.Kind == ContractKind.Agreement)
            {
                agreements[seller, buyer]++;
                agreements[buyer, seller]++;
                AddRelation(seller, buyer, 3f);
                nations[seller].Remember(new TradeMemory(TradeMemoryKind.Agreement, buyer, Day, 1f));
                nations[buyer].Remember(new TradeMemory(TradeMemoryKind.Agreement, seller, Day, 1f));
            }
            return new TradeResult(true, $"Vertrag #{c.Id} geschlossen", c.Id);
        }

        internal TradeResult DoReject(int party, int offerId, string reason)
        {
            var o = Offer(offerId);
            if (o == null || !o.IsOpen) return TradeResult.Fail("Angebot nicht mehr offen");
            if (o.To != party) return TradeResult.Fail("Angebot gilt nicht diesem Land");
            o.Status = OfferStatus.Rejected;
            o.RespondedBy = party;
            o.Reason = reason;
            nations[o.From].Remember(new TradeMemory(TradeMemoryKind.Rejected, party, Day, 1f));
            return new TradeResult(true, $"Angebot #{o.Id} abgelehnt", o.Id);
        }

        internal TradeResult DoWithdraw(int party, int offerId)
        {
            var o = Offer(offerId);
            if (o == null || !o.IsOpen) return TradeResult.Fail("Angebot nicht mehr offen");
            if (o.From != party) return TradeResult.Fail("nur eigene Angebote");
            o.Status = OfferStatus.Withdrawn;
            o.Reason = "zurückgezogen";
            return new TradeResult(true, $"Angebot #{o.Id} zurückgezogen", o.Id);
        }

        internal TradeResult DoCancel(int party, int contractId)
        {
            var c = Contract(contractId);
            if (c == null || !c.IsActive) return TradeResult.Fail("Vertrag nicht aktiv");
            if (!c.Involves(party)) return TradeResult.Fail("nicht Vertragspartner");
            int partner = c.Partner(party);
            double fee = 0;
            if (c.Kind == ContractKind.Supply)
            {
                int remaining = Math.Min(TradeRules.CancelFeeMaxWeeks, c.Terms.Weeks - c.WeeksDispatched);
                fee = Math.Max(0, remaining) * c.Terms.UnitsPerWeek * UnitPrice(c.Terms) * c.Terms.PenaltyShare * 0.5;
                if (fee > 0) PayPenalty(party, partner, fee);
                if (party == c.Seller) c.Penalties += fee;
            }
            else
            {
                agreements[c.Seller, c.Buyer]--;
                agreements[c.Buyer, c.Seller]--;
            }
            c.Status = ContractStatus.Cancelled;
            c.CancelledBy = party;
            c.EndDay = Day;
            var p = nations[partner];
            p.Trust[party] = Math.Max(0f, p.Trust[party] - TradeRules.TrustCancel);
            AddRelation(party, partner, -TradeRules.RelationCancel);
            p.Remember(new TradeMemory(TradeMemoryKind.Cancelled, party, Day, 1f));
            return new TradeResult(true, fee > 0 ? $"Vertrag #{c.Id} gekündigt, Gebühr {fee:0} €" : $"Vertrag #{c.Id} gekündigt", c.Id);
        }

        internal TradeResult DoSetTariff(int party, int target, double rate)
        {
            if (target >= nations.Count || target < -1 || target == party) return TradeResult.Fail("unbekanntes Land");
            if (double.IsNaN(rate))
            {
                if (target < 0) return TradeResult.Fail("Grundzoll braucht einen Satz");
            }
            else rate = Math.Round(Math.Clamp(rate, 0.0, TradeRules.MaxTariff), 3);
            if (target < 0 && BlocOf(party)?.Kind == BlocKind.CustomsUnion && BlocOf(party).Active) // Handel 2
                return TradeResult.Fail($"Zollunion {BlocOf(party).Name}: es gilt der gemeinsame Außenzoll");
            double before = TariffRate(party, PlayerId);
            nations[party].Tariffs.Set(target, rate);
            double after = TariffRate(party, PlayerId);
            if (!IsPlayer(party) && PlayerId >= 0 && after > before + 1e-9) Report(TradeNewsKind.TariffRaised, party, after, null);
            return new TradeResult(true, target < 0 ? $"Grundzoll {rate * 100:0} %" : double.IsNaN(rate) ? "Grundzoll gilt" : $"Zoll {rate * 100:0} %");
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            ProcessCommands();
            if (world.Clock.Tick % SimulationClock.TicksPerDay == 0) DayStep();
        }

        /// <summary>Ohne <see cref="SimulationWorld"/> (Tests): Länder-Volkswirtschaften einen Tag weiter, dann Handelstag.</summary>
        public void SimulateDay()
        {
            ProcessCommands();
            Planet.Economy.TickDay(Day);
            DayStep();
        }

        /// <summary>Ein Handelstag (Reihenfolge s. Klassenbeschreibung). Die Länder-Volkswirtschaften rechnet vorher das <c>WorldSystem</c>.</summary>
        public void DayStep()
        {
            inDayStep = true;
            Arrivals();
            Dispatches();
            CloseContracts();
            UpdateSituation();
            RespondToOffers();
            RespondToInvitations(); // Handel 2: Einladungen, Abstimmungen (NPC-Stimmen, Ergebnis)
            if (AiEnabled) Decide();
            UpdateVotes();
            ExpireOffers();
            ExpireBlocs();
            TourismDay();          // Handel 2: Tourismus als Dienstleistungshandel (vor dem Glätten des Handelsvolumens)
            UpdateTradeVolume();
            UpdateWorldPrices();
            if (Day % TradeRules.DaysPerWeek == TradeRules.DaysPerWeek - 1)
            {
                WeeklyRelations();
                Array.Copy(worldPrice, worldPriceWeekAgo, worldPrice.Length);
            }
            City?.DayStep();
            Day++;
            Version++;
            inDayStep = false;
        }

        bool inDayStep;

        // ------------------------------------------------------------ Lieferungen

        void Arrivals()
        {
            int kept = 0;
            for (int i = 0; i < inTransit.Count; i++)
            {
                var d = inTransit[i];
                if (d.ArriveDay > Day) { inTransit[kept++] = d; continue; }
                Receive(d);
            }
            inTransit.RemoveRange(kept, inTransit.Count - kept);
        }

        void Receive(TradeDelivery d)
        {
            d.Arrived = true;
            var c = Contract(d.ContractId);
            if (c == null) return;
            var g = c.Good;
            int cat = (int)TradeGoods.CategoryOf(g);
            double catUnits = d.Units * TradeGoods.CategoryUnitsPerGood(g);
            double tariffRate = TariffRate(c.Buyer, c.Seller);
            if (IsPlayer(c.Buyer))
                City?.ReceiveImport(g, d.Units, d.UnitPrice, tariffRate);
            else
            {
                var e = nations[c.Buyer].Economy;
                e.Stock[cat] += catUnits;
                nations[c.Buyer].TradedToday[cat] += catUnits;
                nations[c.Buyer].TariffIncome += d.Value * tariffRate;
            }
            PayGoods(c.Buyer, c.Seller, d.Value);
            c.UnitsDelivered += d.Units;
            c.ValueDelivered += d.Value;
        }

        void Dispatches()
        {
            foreach (var c in contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || c.AllDispatched || c.NextDispatchDay > Day) continue;
                Dispatch(c);
            }
        }

        void Dispatch(TradeContract c)
        {
            var g = c.Good;
            int cat = (int)TradeGoods.CategoryOf(g);
            double upg = TradeGoods.CategoryUnitsPerGood(g);
            double units = c.Terms.UnitsPerWeek;
            double price = UnitPrice(c.Terms);
            double shipped;
            var seller = nations[c.Seller];
            if (IsPlayer(c.Seller))
            {
                shipped = City?.TakeExport(g, units) ?? 0.0;
                // Fernfracht trägt der Verkäufer (Lieferung frei Grenze des Käufers)
                if (shipped > 0) City?.PayFreight(shipped * c.Route.UnitCost(g));
            }
            else
            {
                var e = seller.Economy;
                double reserve = e.Demand[cat] * seller.Personality.ReserveDays;
                double available = Math.Max(0.0, e.Stock[cat] - reserve);
                // Unzuverlässige Länder brechen bei eigener Knappheit eher bewusst (gewichteter Zufall)
                double cover = e.Demand[cat] > 0 ? e.Stock[cat] / e.Demand[cat] : 999;
                if (cover < 10 && rng.Chance((1.0 - seller.Personality.Reliability) * 0.5)) available = 0;
                shipped = Math.Clamp(available / upg, 0.0, units);
                e.Stock[cat] -= shipped * upg;
                seller.TradedToday[cat] += shipped * upg;
            }
            if (shipped > 1e-9)
            {
                var d = new TradeDelivery
                {
                    Id = nextDeliveryId++, ContractId = c.Id, Week = c.WeeksDispatched, Units = shipped, UnitPrice = price,
                    DispatchDay = Day, ArriveDay = Day + c.Route.LeadDays,
                };
                c.Deliveries.Add(d);
                inTransit.Add(d);
            }
            double shortUnits = units - shipped;
            var buyer = nations[c.Buyer];
            if (shortUnits > units * 1e-3)
            {
                double share = shortUnits / units;
                double penalty = shortUnits * price * c.Terms.PenaltyShare;
                PayPenalty(c.Seller, c.Buyer, penalty);
                c.Penalties += penalty;
                c.UnitsShort += shortUnits;
                c.WeeksShort++;
                buyer.Trust[c.Seller] = Math.Max(0f, buyer.Trust[c.Seller] - (TradeRules.TrustBreachBase + TradeRules.TrustBreachShare * (float)share));
                AddRelation(c.Seller, c.Buyer, -(TradeRules.RelationBreachBase + TradeRules.RelationBreachShare * (float)share));
                buyer.Remember(new TradeMemory(TradeMemoryKind.Breach, c.Seller, Day, (float)share));
            }
            else
            {
                c.WeeksOnTime++;
                buyer.Trust[c.Seller] = Math.Min(1f, buyer.Trust[c.Seller] + TradeRules.TrustOnTime);
                AddRelation(c.Seller, c.Buyer, TradeRules.RelationOnTime);
            }
            c.WeeksDispatched++;
            c.NextDispatchDay += TradeRules.DaysPerWeek;
        }

        void CloseContracts()
        {
            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                var c = contracts[i];
                if (c.IsActive)
                {
                    bool done = c.Kind == ContractKind.Agreement
                        ? Day >= c.PlannedEndDay
                        : c.AllDispatched && AllArrived(c);
                    if (!done) continue;
                    if (c.Kind == ContractKind.Agreement)
                    {
                        agreements[c.Seller, c.Buyer]--;
                        agreements[c.Buyer, c.Seller]--;
                    }
                    c.Status = ContractStatus.Completed;
                    c.EndDay = Day;
                    AddRelation(c.Seller, c.Buyer, TradeRules.RelationCompleted);
                    if (c.WeeksShort == 0)
                    {
                        nations[c.Buyer].Remember(new TradeMemory(TradeMemoryKind.Completed, c.Seller, Day, 1f));
                        nations[c.Seller].Remember(new TradeMemory(TradeMemoryKind.Completed, c.Buyer, Day, 1f));
                    }
                }
                else if (!AllArrived(c)) continue; // gekündigt, Ware noch unterwegs
                contracts.RemoveAt(i);
                history.Add(c);
            }
            if (history.Count > HistoryCapacity)
            {
                int drop = history.Count - HistoryCapacity;
                for (int i = 0; i < drop; i++) contractById.Remove(history[i].Id);
                history.RemoveRange(0, drop);
            }
        }

        static bool AllArrived(TradeContract c)
        {
            foreach (var d in c.Deliveries) if (!d.Arrived) return false;
            return true;
        }

        void ExpireOffers()
        {
            for (int i = offers.Count - 1; i >= 0; i--)
            {
                var o = offers[i];
                if (o.IsOpen && o.ExpiresDay <= Day)
                {
                    o.Status = OfferStatus.Expired;
                    o.Reason ??= "abgelaufen";
                }
                // Erledigte Angebote nach 30 Tagen vergessen
                if (!o.IsOpen && Day - o.CreatedDay > 30)
                {
                    offers.RemoveAt(i);
                    offerById.Remove(o.Id);
                }
            }
        }

        // ------------------------------------------------------------ Geld

        /// <summary>Warenwert bei Ankunft: Käufer zahlt Verkäufer (Spieler: Stadtkasse).</summary>
        void PayGoods(int buyer, int seller, double value)
        {
            if (!(value > 0)) return;
            nations[buyer].ImportValue += value;
            nations[seller].ExportValue += value;
            if (IsPlayer(buyer)) City?.Post(BudgetCategory.TradePurchases, value);
            if (IsPlayer(seller)) City?.Post(BudgetCategory.TradeSales, value);
        }

        void PayPenalty(int from, int to, double amount)
        {
            if (!(amount > 0)) return;
            nations[from].PenaltiesPaid += amount;
            nations[to].PenaltiesReceived += amount;
            if (IsPlayer(from)) City?.Post(BudgetCategory.PenaltiesPaid, amount);
            if (IsPlayer(to)) City?.Post(BudgetCategory.PenaltiesReceived, amount);
        }

        // ------------------------------------------------------------ Preise

        void UpdateTradeVolume()
        {
            foreach (var n in nations)
                for (int c = 0; c < GoodsCategories.Count; c++)
                {
                    n.TradeVolume[c] += (n.TradedToday[c] - n.TradeVolume[c]) * TradeRules.TradeVolumeEma;
                    n.TradedToday[c] = 0;
                }
        }

        void UpdateWorldPrices()
        {
            foreach (var g in GoodsCatalog.All)
            {
                int c = (int)TradeGoods.CategoryOf(g);
                double sum = 0, w = 0;
                for (int i = 0; i < nations.Count; i++)
                {
                    var n = nations[i];
                    double weight = n.TradeVolume[c] + TradeRules.ProductionWeight * Math.Max(0.0, n.Economy.Production[c]) + 1e-9;
                    sum += CountryPrice(i, g) * weight;
                    w += weight;
                }
                worldPrice[g.Index] = w > 0 ? sum / w : g.BasePrice;
            }
        }

        // ------------------------------------------------------------ Beziehungen

        void InitRelations()
        {
            int n = nations.Count;
            // Nachbarn über Land (Regionen verschiedener Länder grenzen aneinander) → Grenzreibung
            var neighbors = new bool[n, n];
            foreach (var r in Planet.Regions)
                foreach (int q in r.Neighbors)
                {
                    int a = r.CountryId, b = Planet.Regions[q].CountryId;
                    if (a >= 0 && b >= 0 && a != b) neighbors[a, b] = neighbors[b, a] = true;
                }
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    float noise = (SimRandom.Hash(seed * 131 + a, b) & 0xFFFF) / 32767.5f - 1f;
                    float v = 10f + 25f * noise - (neighbors[a, b] ? 8f : 0f)
                              + Governments.Affinity(nations[a].Government, nations[b].Government); // Handel 2
                    relation[a, b] = relation[b, a] = relationBase[a, b] = relationBase[b, a] = v;
                }
        }

        /// <summary>Landgrenze zwischen zwei Ländern?</summary>
        public bool AreNeighbors(int a, int b)
        {
            foreach (var r in Planet.Regions)
            {
                if (r.CountryId != a) continue;
                foreach (int q in r.Neighbors) if (Planet.Regions[q].CountryId == b) return true;
            }
            return false;
        }

        void AddRelation(int a, int b, float delta)
        {
            if (a == b) return;
            float old = relation[a, b];
            float v = Math.Clamp(old + delta, TradeRules.RelationMin, TradeRules.RelationMax);
            relation[a, b] = relation[b, a] = v;
            if (old >= TradeRules.RelationEmbargo && v < TradeRules.RelationEmbargo && (IsPlayer(a) || IsPlayer(b)))
                Report(TradeNewsKind.Embargo, IsPlayer(a) ? b : a, v, null);
        }

        void WeeklyRelations()
        {
            int n = nations.Count;
            for (int a = 0; a < n; a++)
            {
                var na = nations[a];
                for (int b = 0; b < n; b++)
                {
                    if (a == b) continue;
                    na.Trust[b] += (TradeRules.TrustStart - na.Trust[b]) * TradeRules.TrustDrift;
                    if (b > a)
                    {
                        float v = relation[a, b];
                        v += (relationBase[a, b] - v) * TradeRules.RelationDrift;
                        if (HasAgreement(a, b)) v += TradeRules.RelationAgreementWeekly;
                        if (SameBloc(a, b)) v += DiplomacyRules.RelationBlocWeekly; // Handel 2
                        relation[a, b] = relation[b, a] = Math.Clamp(v, TradeRules.RelationMin, TradeRules.RelationMax);
                    }
                    // Zölle von a gegen b verärgern b
                    double rate = TariffRate(a, b);
                    if (rate > TradeRules.TariffAnnoyanceFrom && !IsSanctioned(a, b)) // Handel 2: Sanktionszoll verärgert nicht jede Woche erneut
                    {
                        AddRelation(a, b, -(float)(rate - TradeRules.TariffAnnoyanceFrom) * TradeRules.TariffAnnoyance);
                        nations[b].Remember(new TradeMemory(TradeMemoryKind.Tariff, a, Day, (float)rate));
                    }
                }
            }
            if (AiEnabled) Retaliate();
        }
    }
}
