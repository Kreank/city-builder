using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// NPC-Länder als Utility AI (keine Sprach-KI): Lagebild je Kategorie (Defizit/Überschuss aus Lager, Saldo und laufenden
    /// Verträgen), Bewertung eingehender Angebote (Preis, Bedarf, Menge, Beziehung, Vertrauen, Persönlichkeit → logistische
    /// Annahmewahrscheinlichkeit, Begründung = stärkster Gegengrund) und wöchentliche Entscheidung (Softmax über Kaufen/Verkaufen
    /// je Kategorie, Handelsabkommen, Nichtstun; Partnerwahl ebenfalls per Softmax).
    /// </summary>
    public sealed partial class TradeSystem
    {
        // Lagebild je Land × Kategorie (täglich neu)
        double[,] need, surplus, exportCap, importNeed, cover, contractedIn, contractedOut;
        float[] scoreBuffer;

        void AllocateAi(int n)
        {
            int k = GoodsCategories.Count;
            need = new double[n, k]; surplus = new double[n, k]; exportCap = new double[n, k]; importNeed = new double[n, k];
            cover = new double[n, k]; contractedIn = new double[n, k]; contractedOut = new double[n, k];
            scoreBuffer = new float[Math.Max(64, n * 2)];
        }

        /// <summary>Defizit-Druck 0..1,4 eines Landes in einer Kategorie (Lagebild von heute).</summary>
        public double Need(int country, GoodsCategory c) => need[country, (int)c];
        /// <summary>Überschuss-Druck (inkl. Stärken-Bonus).</summary>
        public double Surplus(int country, GoodsCategory c) => surplus[country, (int)c];
        /// <summary>Lieferfähigkeit (Kategorie-Einheiten/Tag) über laufende Verträge hinaus.</summary>
        public double ExportCapacity(int country, GoodsCategory c) => exportCap[country, (int)c];
        /// <summary>Einfuhrbedarf (Kategorie-Einheiten/Tag) über laufende Verträge hinaus.</summary>
        public double ImportNeed(int country, GoodsCategory c) => importNeed[country, (int)c];
        /// <summary>Lagerreichweite in Tagen.</summary>
        public double Cover(int country, GoodsCategory c) => cover[country, (int)c];

        /// <summary>
        /// Lagebild: Reichweite = Lager/Bedarf; Netto = Saldo + vertraglich rein − raus (je Tag);
        /// Defizit = 0,6 × clamp((20 − Reichweite)/20) + 0,8 × clamp(−Netto/Bedarf);
        /// Überschuss = (0,6 × clamp((Reichweite − 35)/60) + 0,8 × clamp(Netto/Bedarf)) × (1 + 0,3 je passender Stärke);
        /// Lieferfähigkeit = max(0, Netto) + max(0, Lager − 35 Tage)/28; Einfuhrbedarf = max(0, −Netto) + max(0, 20 Tage − Lager)/28.
        /// </summary>
        void UpdateSituation()
        {
            Array.Clear(contractedIn, 0, contractedIn.Length);
            Array.Clear(contractedOut, 0, contractedOut.Length);
            foreach (var c in contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || c.AllDispatched) continue;
                int cat = (int)TradeGoods.CategoryOf(c.Good);
                double perDay = c.Terms.UnitsPerWeek * TradeGoods.CategoryUnitsPerGood(c.Good) / TradeRules.DaysPerWeek;
                contractedIn[c.Buyer, cat] += perDay;
                contractedOut[c.Seller, cat] += perDay;
            }
            for (int n = 0; n < nations.Count; n++)
            {
                var nt = nations[n];
                var e = nt.Economy;
                var profile = nt.Country.Profile;
                for (int c = 0; c < GoodsCategories.Count; c++)
                {
                    double demand = Math.Max(1.0, e.Demand[c]);
                    double cv = e.Stock[c] / demand;
                    double net = e.Production[c] - e.Demand[c] + contractedIn[n, c] - contractedOut[n, c];
                    cover[n, c] = cv;
                    need[n, c] = 0.6 * Math.Clamp((TradeRules.TargetCoverDays - cv) / TradeRules.TargetCoverDays, 0.0, 1.0)
                                 + 0.8 * Math.Clamp(-net / demand, 0.0, 1.0);
                    double strength = (TradeGoods.Supports(profile.Primary, (GoodsCategory)c) ? 0.3 : 0.0)
                                      + (TradeGoods.Supports(profile.Secondary, (GoodsCategory)c) ? 0.15 : 0.0);
                    surplus[n, c] = (0.6 * Math.Clamp((cv - TradeRules.SurplusCoverDays) / 60.0, 0.0, 1.0)
                                     + 0.8 * Math.Clamp(net / demand, 0.0, 1.0)) * (1.0 + strength);
                    exportCap[n, c] = Math.Max(0.0, net) + Math.Max(0.0, e.Stock[c] - TradeRules.SurplusCoverDays * demand) / 28.0;
                    importNeed[n, c] = Math.Max(0.0, -net) + Math.Max(0.0, TradeRules.TargetCoverDays * demand - e.Stock[c]) / 28.0;
                }
            }
        }

        // ------------------------------------------------------------ Bewertung eines Angebots

        /// <summary>Ergebnis der Bewertung: Nutzen (≥ 0 ≈ eher ja) und Begründung.</summary>
        public readonly struct OfferEvaluation
        {
            public readonly float Utility;
            public readonly float Probability;
            public readonly string Reason;
            public OfferEvaluation(float utility, float probability, string reason) { Utility = utility; Probability = probability; Reason = reason; }
        }

        struct Part
        {
            public float Value;
            public string Negative, Positive;
        }

        readonly List<Part> parts = new List<Part>();

        void AddPart(float value, string negative, string positive) => parts.Add(new Part { Value = value, Negative = negative, Positive = positive });

        /// <summary>
        /// Wie sieht Land <paramref name="n"/> das Angebot <paramref name="o"/>? Bausteine (je etwa −1,5..1):
        /// <list type="bullet">
        /// <item>Kauf: Preis = clamp(3 × (Inlandspreis − Preis × (1 + Zoll)) / Inlandspreis × (0,7 + 0,6 × Geschäftssinn)); Bedarf =
        /// 0,6 × (Defizit − 0,15); Menge (über 1,5 × Bedarf) bis −1; Schutz eigener Stärken −0,4 × Protektionismus.</item>
        /// <item>Verkauf: Preis = clamp(3 × (Preis − Fracht − Inlandspreis)/Inlandspreis × …); Menge über 1,1 × Lieferfähigkeit bis −1,5;
        /// eigener Bedarf −0,8 × Defizit × Protektionismus; Überschuss +0,4 × Überschuss; Strafe −0,6 × (1 − Verlässlichkeit) × Strafanteil.</item>
        /// <item>Beide: Beziehung 0,6 × B/100; Vertrauen 1,5 × (V − 0,6); Festpreis-Laufzeit −0,3 × (1 − Risiko) × Wochen/52.</item>
        /// <item>Abkommen: 0,8 × B/100 + 0,6 × (0,5 − Protektionismus) + Vertrauen − 0,1.</item>
        /// </list>
        /// Annahme-Wahrscheinlichkeit = 1/(1 + e^(−(Σ + 0,1)/T)), T = 0,08 + 0,15 × Risiko. Unter Beziehung −50: Embargo.
        /// </summary>
        public OfferEvaluation Evaluate(int n, TradeOffer o)
        {
            parts.Clear();
            var me = nations[n];
            var pers = me.Personality;
            int p = o.From;
            var t = o.Terms;
            float rel = Relation(n, p);
            evSupply = false; // Handel 2: Einzelwerte für Gegenangebote
            if (rel < TradeRules.RelationEmbargo)
                return new OfferEvaluation(-5f, 0f, $"Beziehung zu schlecht ({rel:+0;−0}): kein Handel");
            if (IsSanctioned(n, p)) return new OfferEvaluation(-5f, 0f, SanctionText(n, p)); // Handel 2
            if (SameBloc(n, p)) AddPart(DiplomacyRules.BlocPartnerBonus, null, "Bündnispartner"); // Handel 2

            if (t.Kind == ContractKind.Agreement)
            {
                AddPart(0.8f * rel / 100f, $"Beziehung zu schwach ({rel:+0;−0})", "gute Beziehungen");
                AddPart(0.6f * (0.5f - pers.Protectionism), "wir schützen unsere Wirtschaft (protektionistisch)", "wir setzen auf Freihandel");
                AddPart(1.5f * (me.Trust[p] - TradeRules.TrustStart), $"kein Vertrauen ({me.Trust[p] * 100:0} %)", "verlässlicher Partner");
                AddPart(-0.1f, "kein Anlass für ein Abkommen", null);
            }
            else
            {
                var g = t.Good;
                int c = (int)TradeGoods.CategoryOf(g);
                double upg = TradeGoods.CategoryUnitsPerGood(g);
                double price = UnitPrice(t);
                double domestic = CountryPrice(n, g);
                float thrift = 0.7f + 0.6f * pers.Thrift;
                bool buys = o.Side == TradeSide.Sell;
                var profile = me.Country.Profile;
                if (buys)
                {
                    double tariff = TariffRate(n, p);
                    double landed = price * (1.0 + tariff);
                    double maxPrice = domestic / (1.0 + tariff);
                    float pricePart = (float)Math.Clamp(3.0 * (domestic - landed) / domestic * thrift, -1.5, 1.0);
                    AddPart(pricePart,
                        $"zu teuer: höchstens {maxPrice:0.0} €/E (bei uns {domestic:0.0} €{(tariff > 0 ? $", Zoll {tariff * 100:0} %" : "")})", "günstiger als bei uns");
                    double needWeek = importNeed[n, c] * TradeRules.DaysPerWeek / upg;
                    AddPart(0.6f * (float)(need[n, c] - 0.15), $"Bedarf gedeckt: Lager reicht {cover[n, c]:0} Tage", "wir brauchen die Ware");
                    double ratio = t.UnitsPerWeek / Math.Max(1e-6, needWeek);
                    float qtyPart = ratio > 1.5 ? -(float)Math.Min(1.0, (ratio - 1.5) * 0.5) : 0f;
                    if (ratio > 1.5) AddPart(qtyPart, $"Menge zu groß: wir brauchen höchstens {needWeek:0} E/Woche", null);
                    SetEvalDetail(true, pricePart, domestic, tariff, 0.0, thrift, qtyPart, needWeek * 1.45, 0f);
                    if (TradeGoods.Supports(profile.Primary, (GoodsCategory)c) || TradeGoods.Supports(profile.Secondary, (GoodsCategory)c))
                        AddPart(-0.4f * pers.Protectionism, "Schutz der eigenen Produktion", null);
                }
                else
                {
                    var route = Geography.Route(n, p);
                    double freight = route != null ? route.UnitCost(g) : 0.0;
                    double net = price - freight;
                    float pricePart = (float)Math.Clamp(3.0 * (net - domestic) / domestic * thrift, -1.5, 1.0);
                    AddPart(pricePart,
                        $"Preis zu niedrig: mindestens {domestic + freight:0.0} €/E (Inlandspreis {domestic:0.0} € + Fracht {freight:0.0} €)", "guter Preis");
                    double capWeek = exportCap[n, c] * TradeRules.DaysPerWeek / upg;
                    double ratio = t.UnitsPerWeek / Math.Max(1e-6, capWeek);
                    float qtyPart = ratio > 1.1 ? -(float)Math.Min(1.5, ratio - 1.1) : 0f;
                    if (ratio > 1.1) AddPart(qtyPart, $"Menge zu groß: wir können höchstens {capWeek:0} E/Woche liefern", null);
                    AddPart(-0.8f * (float)need[n, c] * pers.Protectionism, "wir brauchen die Ware selbst", null);
                    AddPart(0.4f * (float)Math.Min(1.0, surplus[n, c]), null, "Überschuss abbauen");
                    float penaltyPart = -0.6f * (1f - pers.Reliability) * (float)t.PenaltyShare;
                    AddPart(penaltyPart, "Vertragsstrafe zu hoch", null);
                    SetEvalDetail(false, pricePart, domestic, 0.0, freight, thrift, qtyPart, capWeek * 1.05, penaltyPart);
                }
                AddPart(0.6f * rel / 100f, $"Beziehung zu schlecht ({rel:+0;−0})", "gute Beziehungen");
                AddPart(1.5f * (me.Trust[p] - TradeRules.TrustStart), $"kein Vertrauen nach Vertragsbruch ({me.Trust[p] * 100:0} %)", "verlässlicher Partner");
                if (t.PriceMode == PriceMode.Fixed)
                {
                    evTermPart = -0.3f * (1f - pers.Risk) * t.Weeks / 52f;
                    AddPart(evTermPart, "Festpreis-Laufzeit zu lang", null);
                }
                else evTermPart = 0f;
            }

            float u = TradeRules.AcceptBias;
            int worst = -1, best = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                u += parts[i].Value;
                if (parts[i].Negative != null && (worst < 0 || parts[i].Value < parts[worst].Value)) worst = i;
                if (parts[i].Positive != null && (best < 0 || parts[i].Value > parts[best].Value)) best = i;
            }
            float prob = 1f / (1f + MathF.Exp(-u / pers.AcceptTemperature));
            string reason = u >= 0
                ? (best >= 0 && parts[best].Value > 0 ? parts[best].Positive : "annehmbar")
                : (worst >= 0 && parts[worst].Value < 0 ? parts[worst].Negative : "kein Interesse");
            return new OfferEvaluation(u, prob, reason);
        }

        /// <summary>Nicht eingetragenes Angebot für eine Vorschau der Bewertung (UI: „Einschätzung“); null bei ungültigen Angaben.</summary>
        public TradeOffer PreviewOffer(int from, int to, TradeSide side, ContractTerms terms)
        {
            if (terms?.Good == null || to < 0 || to >= nations.Count || to == from || from < 0 || from >= nations.Count) return null;
            return new TradeOffer { From = from, To = to, Side = side, Terms = terms.Clone(), CreatedDay = Day, ExpiresDay = Day, Status = OfferStatus.Open };
        }

        // ------------------------------------------------------------ Antworten (täglich)

        void RespondToOffers()
        {
            // Nur Angebote von gestern oder älter (asynchron: eine Nacht Bedenkzeit), feste Reihenfolge nach Id
            for (int i = 0; i < offers.Count; i++)
            {
                var o = offers[i];
                if (!o.IsOpen || o.AnswerDay > Day) continue;
                if (o.To >= 0)
                {
                    if (IsPlayer(o.To)) continue; // der Spieler entscheidet selbst
                    var ev = Evaluate(o.To, o);
                    if (rng.NextDouble() < ev.Probability) DoAccept(o.To, o.Id, "angenommen: " + ev.Reason);
                    else if (!TryCounter(o.To, o, ev)) DoReject(o.To, o.Id, ev.Reason); // Handel 2: Gegenangebot
                    LogAi(o.To, o, ev);
                    continue;
                }
                // An alle: jedes NPC-Land würfelt, das mit dem höchsten Nutzen unter den Zusagen gewinnt
                int bestParty = -1;
                float bestU = float.MinValue, topU = float.MinValue;
                string bestReason = null, topReason = null;
                for (int n = 0; n < nations.Count; n++)
                {
                    if (n == o.From || IsPlayer(n)) continue;
                    var ev = Evaluate(n, o);
                    if (ev.Utility > topU) { topU = ev.Utility; topReason = $"{nations[n].Country.Name}: {ev.Reason}"; }
                    if (rng.NextDouble() < ev.Probability && ev.Utility > bestU) { bestU = ev.Utility; bestParty = n; bestReason = ev.Reason; }
                }
                if (bestParty >= 0) DoAccept(bestParty, o.Id, $"angenommen von {nations[bestParty].Country.Name}: {bestReason}");
                else
                {
                    o.Status = OfferStatus.Rejected;
                    o.Reason = topReason != null ? "niemand nimmt an – am ehesten " + topReason : "niemand nimmt an";
                }
            }
        }

        /// <summary>Letzte Entscheidungen der KI (Text) – für Tests/Entwickleranzeige.</summary>
        public IReadOnlyList<string> AiLog => aiLog;
        readonly List<string> aiLog = new List<string>();

        void LogAi(int n, TradeOffer o, OfferEvaluation ev)
        {
            aiLog.Add($"Tag {Day}: {nations[n].Country.Name} → Angebot #{o.Id}: U {ev.Utility:+0.00;−0.00} p {ev.Probability:0.00} · {ev.Reason}");
            if (aiLog.Count > 200) aiLog.RemoveRange(0, aiLog.Count - 200);
        }

        // ------------------------------------------------------------ Wochenentscheidung

        enum OptionKind : byte { Idle, Buy, Sell, Agreement }

        struct Option
        {
            public OptionKind Kind;
            public int Category;
        }

        readonly List<Option> options = new List<Option>();

        void Decide()
        {
            for (int n = 0; n < nations.Count; n++)
            {
                if (IsPlayer(n) || (n + Day) % TradeRules.DaysPerWeek != 0) continue;
                DecideNation(n);
                CancelHostile(n);
                DecideDiplomacy(n); // Handel 2: Bündnisse, Abstimmungen, Proteste
            }
        }

        int OpenOffersFrom(int n, int to = int.MinValue)
        {
            int k = 0;
            foreach (var o in offers)
                if (o.IsOpen && o.From == n && (to == int.MinValue || o.To == to)) k++;
            return k;
        }

        int OpenOffersToPlayer()
        {
            int k = 0;
            foreach (var o in offers) if (o.IsOpen && IsPlayer(o.To)) k++;
            return k;
        }

        void DecideNation(int n)
        {
            var me = nations[n];
            var pers = me.Personality;
            options.Clear();
            options.Add(new Option { Kind = OptionKind.Idle });
            for (int c = 0; c < GoodsCategories.Count; c++)
            {
                if (!TradeGoods.IsTradeable((GoodsCategory)c)) continue;
                if (need[n, c] > 0.05) options.Add(new Option { Kind = OptionKind.Buy, Category = c });
                if (surplus[n, c] > 0.05) options.Add(new Option { Kind = OptionKind.Sell, Category = c });
            }
            options.Add(new Option { Kind = OptionKind.Agreement });

            for (int round = 0; round < TradeRules.OffersPerDecision && options.Count > 1; round++)
            {
                if (OpenOffersFrom(n) >= TradeRules.MaxOpenOffers) return;
                int count = Math.Min(options.Count, scoreBuffer.Length);
                for (int i = 0; i < count; i++)
                {
                    var op = options[i];
                    scoreBuffer[i] = op.Kind switch
                    {
                        OptionKind.Idle => TradeRules.IdleScore,
                        OptionKind.Buy => (float)need[n, op.Category],
                        OptionKind.Sell => 0.9f * (float)surplus[n, op.Category],
                        _ => AgreementScore(n, out _),
                    };
                }
                int pick = UtilityChoice.Choose(scoreBuffer, count, pers.Temperature, rng);
                var chosen = options[pick];
                if (chosen.Kind == OptionKind.Idle) return;
                options.RemoveAt(pick);
                switch (chosen.Kind)
                {
                    case OptionKind.Buy: PlanTrade(n, chosen.Category, true); break;
                    case OptionKind.Sell: PlanTrade(n, chosen.Category, false); break;
                    default:
                        AgreementScore(n, out int partner);
                        if (partner >= 0)
                            Execute(new ProposeContractCommand(n, partner, TradeSide.Sell,
                                new ContractTerms { Kind = ContractKind.Agreement, Weeks = TradeRules.AgreementWeeks }));
                        break;
                }
            }
        }

        /// <summary>Lohnt ein Handelsabkommen? Bester Partner mit Beziehung ≥ 25 und laufendem Handel.</summary>
        float AgreementScore(int n, out int partner)
        {
            partner = -1;
            float best = 0f;
            var pers = nations[n].Personality;
            foreach (var c in contracts)
            {
                if (!c.IsActive || c.Kind != ContractKind.Supply || !c.Involves(n)) continue;
                int m = c.Partner(n);
                if (HasAgreement(n, m) || Relation(n, m) < 25f || OpenOffersFrom(n, m) > 0) continue;
                float s = 0.25f + Relation(n, m) / 200f - 0.3f * pers.Protectionism;
                if (s > best || (s == best && partner >= 0 && m < partner)) { best = s; partner = m; }
            }
            return partner >= 0 ? best : 0f;
        }

        /// <summary>
        /// Kauf/Verkauf in Kategorie <paramref name="c"/> planen: Partner per Softmax (Überschuss/Defizit des Partners, Beziehung,
        /// Vertrauen, Preisvorteil, Lieferzeit), Gut zufällig aus der Kategorie (beim Spieler: das Gut mit dem größten Bedarf bzw.
        /// Überschuss der Stadt), Menge aus Bedarf und Lieferfähigkeit, Laufzeit 4 + 8 × (1 − Risiko) ± 2 Wochen, Festpreis oder
        /// Weltpreis-Kopplung (Risiko), Preis zwischen Untergrenze des Verkäufers (Inlandspreis + Fracht) und Obergrenze des Käufers
        /// (Inlandspreis / (1 + Zoll)), zum eigenen Vorteil verschoben nach Geschäftssinn.
        /// </summary>
        void PlanTrade(int n, int c, bool buy)
        {
            var me = nations[n];
            var pers = me.Personality;
            var goods = TradeGoods.GoodsOf((GoodsCategory)c);
            if (goods.Count == 0) return;
            int count = 0;
            var candidates = candidateBuffer;
            candidates.Clear();
            for (int m = 0; m < nations.Count; m++)
            {
                if (m == n || Relation(n, m) < TradeRules.RelationEmbargo || IsSanctioned(n, m)) continue; // Handel 2: Sanktionen
                GoodType g;
                double partnerDrive;
                if (IsPlayer(m))
                {
                    if (City == null || OpenOffersFrom(n, m) > 0 || OpenOffersToPlayer() >= TradeRules.MaxOffersToPlayer) continue;
                    g = buy ? City.BestExportGood((GoodsCategory)c) : City.BestImportGood((GoodsCategory)c);
                    if (g == null) continue;
                    partnerDrive = 0.6;
                }
                else
                {
                    partnerDrive = buy ? surplus[m, c] : need[m, c];
                    if (partnerDrive < 0.02) continue;
                    g = goods[rng.Next(goods.Count)];
                }
                int seller = buy ? m : n, buyer = buy ? n : m;
                var route = Geography.Route(seller, buyer);
                double ask = SellerFloor(seller, g) + route.UnitCost(g);
                double bid = BuyerCeiling(buyer, seller, g);
                if (bid < ask * 0.97) continue; // kein Geschäft möglich
                double advantage = (bid - ask) / Math.Max(1e-6, bid);
                float score = (float)(0.8 * Math.Min(1.0, partnerDrive) + 0.5 * Relation(n, m) / 100.0 + 0.6 * (me.Trust[m] - TradeRules.TrustStart)
                                      + 0.6 * advantage - 0.01 * route.LeadDays);
                candidates.Add(new Candidate { Partner = m, Good = g, Ask = ask, Bid = bid, Route = route });
                if (count < scoreBuffer.Length) scoreBuffer[count] = score;
                count++;
            }
            if (count == 0) return;
            count = Math.Min(count, scoreBuffer.Length);
            var pick = candidates[UtilityChoice.Choose(scoreBuffer, count, pers.Temperature, rng)];
            var good = pick.Good;
            double upg = TradeGoods.CategoryUnitsPerGood(good);
            double units;
            if (IsPlayer(pick.Partner))
                units = buy ? City.ExportPotentialPerDay(good) * TradeRules.DaysPerWeek * 0.8
                            : Math.Min(exportCap[n, c] * TradeRules.DaysPerWeek / upg, City.ImportNeedPerDay(good) * TradeRules.DaysPerWeek);
            else
                units = (buy ? Math.Min(importNeed[n, c], exportCap[pick.Partner, c] * 0.6)
                             : Math.Min(exportCap[n, c], importNeed[pick.Partner, c] * 0.8)) * TradeRules.DaysPerWeek / upg;
            units = Math.Floor(units);
            if (units < 1) return;

            // Preis: zwischen Untergrenze (ask) und Obergrenze (bid), Aufschlag 4 % auf den Verkäufer, Rest nach Geschäftssinn
            double low = pick.Ask * 1.04;
            double price = low <= pick.Bid
                ? (buy ? low + (pick.Bid - low) * 0.3 * (1.0 - pers.Thrift) : low + (pick.Bid - low) * (0.3 + 0.4 * pers.Thrift))
                : 0.5 * (pick.Ask + pick.Bid);
            int weeks = Math.Clamp(4 + (int)Math.Round(8 * (1f - pers.Risk)) + rng.Next(-2, 3), 2, 16);
            var terms = new ContractTerms
            {
                Kind = ContractKind.Supply, Good = good, UnitsPerWeek = units, Weeks = weeks,
                PenaltyShare = Math.Round(0.2 + 0.3 * (1f - pers.Risk), 2),
            };
            if (rng.NextDouble() < pers.Risk * 0.7 && worldPrice[good.Index] > 0)
            {
                terms.PriceMode = PriceMode.WorldIndexed;
                terms.Price = Math.Round(price / worldPrice[good.Index], 3);
            }
            else terms.Price = Math.Round(price, 2);
            Execute(new ProposeContractCommand(n, pick.Partner, buy ? TradeSide.Buy : TradeSide.Sell, terms));
        }

        struct Candidate
        {
            public int Partner;
            public GoodType Good;
            public double Ask, Bid;
            public TradeRoute Route;
        }

        readonly List<Candidate> candidateBuffer = new List<Candidate>();

        /// <summary>Untergrenze des Verkäufers je Einheit ab Werk: Inlandspreis (Spieler: lokaler Preis der Stadt).</summary>
        double SellerFloor(int seller, GoodType g) =>
            IsPlayer(seller) && City != null ? City.LocalPrice(g) : CountryPrice(seller, g);

        /// <summary>Obergrenze des Käufers frei Grenze: Inlandspreis / (1 + Zoll); Spieler: Importparität der Stadt (Weltmarkt + 8 % Fracht + Zoll).</summary>
        double BuyerCeiling(int buyer, int seller, GoodType g)
        {
            double tariff = TariffRate(buyer, seller);
            if (IsPlayer(buyer) && City != null)
                return Math.Max(City.LocalPrice(g), CityWorldPrice(g) * (1.0 + GoodsRules.WorldFreightShare + City.SpotTariffRate(g))) / (1.0 + tariff);
            return CountryPrice(buyer, g) / (1.0 + tariff);
        }

        /// <summary>Verträge mit verfeindeten Ländern kündigen (Beziehung unter der Embargo-Grenze).</summary>
        void CancelHostile(int n)
        {
            for (int i = 0; i < contracts.Count; i++)
            {
                var c = contracts[i];
                if (c.IsActive && c.Involves(n) && Relation(n, c.Partner(n)) < TradeRules.RelationEmbargo)
                    Execute(new CancelContractCommand(n, c.Id));
            }
        }

        /// <summary>
        /// Vergeltung (wöchentlich): erhebt ein Land deutlich höhere Zölle gegen n als n gegen es (über 5 Punkte), hebt ein
        /// protektionistisches n (≥ 0,3) seinen Satz gegen dieses Land um 2 Punkte; ist der Grund weg, gilt wieder der Grundzoll.
        /// </summary>
        void Retaliate()
        {
            for (int n = 0; n < nations.Count; n++)
            {
                if (IsPlayer(n)) continue;
                var me = nations[n];
                for (int m = 0; m < nations.Count; m++)
                {
                    if (m == n || HasAgreement(n, m)) continue;
                    double theirs = TariffRate(m, n);
                    double mine = me.Tariffs.RateFor(m);
                    if (me.Personality.Protectionism >= 0.3f && theirs > mine + 0.05)
                        Execute(new SetTariffCommand(n, m, Math.Min(theirs, mine + 0.02)));
                    else if (me.Tariffs.HasOverride(m) && theirs <= me.Tariffs.General + 1e-9)
                        Execute(new SetTariffCommand(n, m, double.NaN));
                }
            }
        }
    }
}
