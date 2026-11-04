using System;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel 2 – Verhandlung: Lehnt ein Land ein an es gerichtetes Liefer-Angebot ab, kann es ein <b>Gegenangebot</b> machen
    /// (<see cref="CounterOfferCommand"/>, gleiche Klasse für Spieler und NPC). Die Bedingungen kommen aus der eigenen Bewertung
    /// (<see cref="SuggestCounter"/>): zuerst die Menge (Bedarf bzw. Lieferfähigkeit), dann Strafe und Festpreis-Laufzeit, zuletzt
    /// der Preis – genau so weit, dass der eigene Nutzen ≥ <see cref="DiplomacyRules.CounterMargin"/> ist (der Preisbaustein der
    /// Bewertung ist linear, der nötige Preis wird direkt ausgerechnet). Lässt sich der Gegengrund nicht über die Bedingungen
    /// beheben (kein Bedarf, Beziehung, Vertrauen) oder liegt der Preis über 60 % daneben, gibt es keins. NPC verhandeln höchstens
    /// so viele Runden, wie ihre Geduld (<see cref="NationPersonality.Patience"/>, 2–4) erlaubt, insgesamt höchstens 6.
    /// </summary>
    public sealed partial class TradeSystem
    {
        // Einzelwerte der letzten Bewertung (gesetzt in Evaluate, nur für Lieferverträge)
        bool evSupply, evBuys;
        float evPricePart, evQtyPart, evPenaltyPart, evTermPart;
        double evDomestic, evTariff, evFreight, evThrift, evQtyLimit;

        void SetEvalDetail(bool buys, float pricePart, double domestic, double tariff, double freight, double thrift, float qtyPart, double qtyLimit, float penaltyPart)
        {
            evSupply = true;
            evBuys = buys;
            evPricePart = pricePart;
            evDomestic = domestic;
            evTariff = tariff;
            evFreight = freight;
            evThrift = thrift;
            evQtyPart = qtyPart;
            evQtyLimit = qtyLimit;
            evPenaltyPart = penaltyPart;
            evTermPart = 0f;
        }

        /// <summary>
        /// Bedingungen, die Land <paramref name="n"/> an Stelle von <paramref name="o"/> annehmen würde (eigener Nutzen ≥
        /// <see cref="DiplomacyRules.CounterMargin"/>), oder null (nicht über Bedingungen lösbar / zu weit auseinander).
        /// Rein (ohne Zufall), auch für die UI-Vorschau „Was würde … akzeptieren?“.
        /// </summary>
        public ContractTerms SuggestCounter(int n, TradeOffer o)
        {
            if (o == null || o.Terms == null || o.Terms.Kind != ContractKind.Supply || o.Terms.Good == null) return null;
            if (n < 0 || n >= nations.Count || n == o.From) return null;
            var terms = o.Terms.Clone();
            var probe = new TradeOffer { From = o.From, To = n, Side = o.Side, Terms = terms, CreatedDay = Day, ExpiresDay = Day, Status = OfferStatus.Open };
            double original = UnitPrice(o.Terms);
            if (!(original > 0)) return null;
            bool changed = false;
            for (int iter = 0; iter < 6; iter++)
            {
                var ev = Evaluate(n, probe);
                if (!evSupply) return null; // Embargo/Sanktion
                if (ev.Utility >= DiplomacyRules.CounterMargin) return changed ? terms : null;
                if (evQtyPart < -0.02f)
                {
                    double units = Math.Floor(evQtyLimit);
                    if (units < 1 || units >= terms.UnitsPerWeek) return null;
                    terms.UnitsPerWeek = units;
                    changed = true;
                    continue;
                }
                if (!evBuys && evPenaltyPart < -0.03f && terms.PenaltyShare > 0.1 + 1e-9)
                {
                    terms.PenaltyShare = 0.1;
                    changed = true;
                    continue;
                }
                if (evTermPart < -0.08f && terms.Weeks > 2)
                {
                    terms.Weeks = Math.Max(2, terms.Weeks / 2);
                    changed = true;
                    continue;
                }
                double rest = ev.Utility - evPricePart;
                double needed = DiplomacyRules.CounterMargin + 0.03 - rest;
                if (needed > 0.97 || evThrift <= 0) return null;
                double k = needed / (3.0 * evThrift);
                double price = evBuys ? evDomestic * (1.0 - k) / (1.0 + evTariff) : evDomestic * (1.0 + k) + evFreight;
                if (!(price > 0) || Math.Abs(price / original - 1.0) > DiplomacyRules.MaxCounterPriceShift) return null;
                double before = UnitPrice(terms);
                SetUnitPrice(terms, price);
                if (Math.Abs(UnitPrice(terms) - before) < 1e-9) return null; // Rundung: keine Bewegung mehr
                changed = true;
            }
            return null;
        }

        /// <summary>Preis je Einheit in die Bedingungen schreiben (fest: auf Cent; gekoppelt: Faktor auf den Weltpreis).</summary>
        void SetUnitPrice(ContractTerms t, double price)
        {
            if (t.PriceMode == PriceMode.WorldIndexed && worldPrice[t.Good.Index] > 0) t.Price = Math.Round(price / worldPrice[t.Good.Index], 3);
            else t.Price = Math.Round(price, 2);
        }

        /// <summary>Tests: feste Gegenangebot-Chance (null = nach Geschäftssinn; nicht gespeichert).</summary>
        internal double? CounterChanceOverride { get; set; }

        /// <summary>NPC: Gegenangebot statt Ablehnung (Geduld, Chance nach Geschäftssinn). false = einfach ablehnen.</summary>
        bool TryCounter(int n, TradeOffer o, OfferEvaluation ev)
        {
            if (o.To != n || IsPlayer(n) || o.Terms.Kind != ContractKind.Supply) return false;
            var pers = nations[n].Personality;
            if (o.Round >= DiplomacyRules.MaxNegotiationRounds || o.Round >= pers.Patience) return false;
            if (ev.Utility < -2.5f) return false;
            var terms = SuggestCounter(n, o);
            if (terms == null) return false;
            if (!rng.Chance(CounterChanceOverride ?? DiplomacyRules.CounterChanceBase + DiplomacyRules.CounterChanceThrift * pers.Thrift)) return false;
            return Execute(new CounterOfferCommand(n, o.Id, terms, ev.Reason)).Ok;
        }

        internal TradeResult DoCounter(int party, int offerId, ContractTerms terms, string reason)
        {
            var o = Offer(offerId);
            if (o == null || !o.IsOpen) return TradeResult.Fail("Angebot nicht mehr offen");
            if (o.From == party) return TradeResult.Fail("eigenes Angebot");
            if (o.To != party) return TradeResult.Fail("Gegenangebot nur auf Angebote an uns");
            if (o.Terms.Kind != ContractKind.Supply) return TradeResult.Fail("Gegenangebote nur für Lieferverträge");
            if (o.Round >= DiplomacyRules.MaxNegotiationRounds) return TradeResult.Fail($"Verhandlung ausgereizt ({DiplomacyRules.MaxNegotiationRounds} Runden)");
            if (terms == null || terms.Good == null) return TradeResult.Fail("keine Bedingungen");
            if (terms.Kind != ContractKind.Supply) return TradeResult.Fail("Gegenangebot muss ein Liefervertrag sein");
            var side = o.Side == TradeSide.Sell ? TradeSide.Buy : TradeSide.Sell;
            var r = DoPropose(party, o.From, side, terms, TradeRules.OfferValidDays);
            if (!r.Ok) return r;
            var c = Offer(r.Id);
            c.CounterOf = o.Id;
            c.Round = o.Round + 1;
            o.Status = OfferStatus.Rejected;
            o.RespondedBy = party;
            o.Reason = (string.IsNullOrEmpty(reason) ? "" : reason + " · ") + $"Gegenangebot #{c.Id}";
            if (IsPlayer(o.From)) Report(TradeNewsKind.CounterOffer, party, c.Round, terms.Good.Name);
            return new TradeResult(true, $"Gegenangebot #{c.Id} gestellt", c.Id);
        }

        /// <summary>Verhandlungsverlauf eines Angebots (ältestes zuerst, soweit noch gespeichert).</summary>
        public void NegotiationChain(TradeOffer o, System.Collections.Generic.List<TradeOffer> result)
        {
            result.Clear();
            for (var x = o; x != null && result.Count < 16; x = x.CounterOf > 0 ? Offer(x.CounterOf) : null) result.Add(x);
            result.Reverse();
        }
    }
}
