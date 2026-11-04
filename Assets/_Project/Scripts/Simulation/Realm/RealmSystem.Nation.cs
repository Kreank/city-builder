using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Realm
{
    /// <summary>Etappe 7: Land gründen, Anerkennung durch NPC-Länder, NPC-Expansion in freies Land.</summary>
    public sealed partial class RealmSystem
    {
        // ------------------------------------------------------------ Land gründen

        /// <summary>
        /// Bedingungen der Landesgründung mit Fortschritt (<see cref="RealmCheck.Progress"/>): ≥ 3 Städte, ≥ 2 Rathäuser,
        /// ≥ 6 000 Einwohner gesamt, Hauptstadt mit Rathaus, gültiger Name. <paramref name="name"/> null = Name nicht prüfen;
        /// <paramref name="capital"/> −1 = bisherige Hauptstadt.
        /// </summary>
        public RealmCheck CheckNation(string name, int capital)
        {
            var check = new RealmCheck();
            var realm = Realm;
            var p = Planet;
            if (realm.IsNation) { check.Add("Land gegründet", false, $"{realm.NationName} besteht schon"); return check; }
            if (p == null || p.PlayerCityState == null)
            {
                check.Add("Freie Stadt", false, p == null ? "ohne Weltkarte gibt es kein Land" : "alter Spielstand: dein Gebiet ist schon ein Land");
                return check;
            }
            int n = realm.Cities.Count;
            check.Add($"{RealmRules.MinCitiesForNation} eigene Städte", n >= RealmRules.MinCitiesForNation, $"{n} von {RealmRules.MinCitiesForNation}");
            int halls = 0;
            foreach (var c in realm.Cities) if (HasTownHall(c)) halls++;
            check.Add($"{RealmRules.MinTownHallsForNation} Rathäuser", halls >= RealmRules.MinTownHallsForNation, $"{halls} von {RealmRules.MinTownHallsForNation}");
            double pop = TotalPopulationLive();
            check.Add($"{RealmRules.MinPopulationForNation:N0} Einwohner gesamt", pop >= RealmRules.MinPopulationForNation, $"{pop:N0}");
            var cap = capital >= 0 ? realm.City(capital) : realm.CapitalCity;
            check.Add("Hauptstadt mit Rathaus", cap != null && HasTownHall(cap), cap == null ? "keine Hauptstadt gewählt" : $"{cap.Name}");
            if (name != null)
            {
                var clean = PlayerRealm.Clean(name);
                bool unique = clean != null;
                if (clean != null)
                    foreach (var c in p.Countries)
                        if (c != p.PlayerCityState && string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)) unique = false;
                if (clean != null && clean.StartsWith("Freie Stadt", StringComparison.OrdinalIgnoreCase)) unique = false;
                check.Add("Landesname", unique, clean == null ? "Name fehlt" : unique ? null : "schon vergeben");
            }
            return check;
        }

        bool HasTownHall(RealmCity c) =>
            c.Id == Realm.ActiveCityId ? world.Services != null && world.Services.HasTownHall : c.Economy.HasTownHall;

        double TotalPopulationLive()
        {
            double s = 0;
            foreach (var c in Realm.Cities)
                s += c.Id == Realm.ActiveCityId ? world.Citizens?.Stats.Population ?? 0 : c.Economy.Population;
            return s;
        }

        internal TradeResult DoFoundNation(string name, uint color, int capital)
        {
            var check = CheckNation(name ?? "", capital);
            if (!check.Ok) return TradeResult.Fail(check.FirstProblem ?? "Bedingungen nicht erfüllt");
            var realm = Realm;
            var p = Planet;
            realm.NationName = PlayerRealm.Clean(name);
            realm.FlagColor = color & 0xFFFFFFu;
            if (capital >= 0) realm.CapitalCityId = capital;
            realm.NationFoundedDay = Today;
            realm.Apply();
            p.PoliticalVersion++;
            realm.Report(Today, $"Land {realm.NationName} gegründet", $"Hauptstadt {realm.CapitalCity.Name} · {realm.Cities.Count} Städte · {realm.CityRegions.Count} Regionen", true);
            ReactToNation();
            return new TradeResult(true, $"{realm.NationName} ist gegründet – Hauptstadt {realm.CapitalCity.Name}");
        }

        /// <summary>
        /// NPC-Länder reagieren auf das neue Land (Beziehung, Anerkennung): Höflichkeit +3, Nachbarn −6 (Sorge um die Grenze),
        /// Protektionismus × −4. Neigung zur Anerkennung = Beziehung + 15 × (1 − Protektionismus) − 10 bei gemeinsamer Grenze;
        /// ab 0 erkennt das Land sofort an (+2 Beziehung), sonst prüft es alle 30 Tage neu (Zufall nach Neigung).
        /// </summary>
        void ReactToNation()
        {
            var t = world.Trade;
            var realm = Realm;
            int me = Planet.PlayerCountryId;
            if (t == null || me < 0) return;
            int yes = 0, no = 0;
            foreach (var n in t.Nations)
            {
                if (n.IsPlayer) continue;
                bool neighbor = t.AreNeighbors(n.Id, me);
                float delta = RealmRules.FoundingCourtesy - (neighbor ? RealmRules.FoundingNeighborConcern : 0f)
                              - RealmRules.FoundingProtectionismConcern * n.Personality.Protectionism;
                t.AdjustRelation(n.Id, me, delta);
                if (RecognitionInclination(t, n, me, neighbor) >= 0f)
                {
                    realm.Recognized[n.Id] = true;
                    t.AdjustRelation(n.Id, me, RealmRules.RecognitionBonus);
                    yes++;
                }
                else
                {
                    realm.RecognitionReviewDay[n.Id] = Today + RealmRules.RecognitionReviewDays + n.Id % 7;
                    no++;
                }
            }
            realm.Report(Today, "Anerkennung", $"{yes} Länder erkennen {realm.NationName} an, {no} warten ab", true);
        }

        static float RecognitionInclination(TradeSystem t, NationTrade n, int me, bool neighbor) =>
            t.Relation(n.Id, me) + RealmRules.RecognitionOpenness * (1f - n.Personality.Protectionism)
            - (neighbor ? RealmRules.RecognitionNeighborPenalty : 0f);

        void ReviewRecognition(int day)
        {
            var t = world.Trade;
            var realm = Realm;
            int me = Planet?.PlayerCountryId ?? -1;
            if (t == null || me < 0) return;
            foreach (var n in t.Nations)
            {
                if (n.IsPlayer || realm.Recognized[n.Id] || realm.RecognitionReviewDay[n.Id] > day) continue;
                realm.RecognitionReviewDay[n.Id] = day + RealmRules.RecognitionReviewDays;
                float incl = RecognitionInclination(t, n, me, t.AreNeighbors(n.Id, me));
                // Neigung −20 … 0 → Chance 0 … 50 %
                double chance = Math.Clamp((incl + 20f) / 40f, 0f, 0.5f);
                if (!realm.Rng.Chance(chance)) continue;
                realm.Recognized[n.Id] = true;
                t.AdjustRelation(n.Id, me, RealmRules.RecognitionBonus);
                realm.Report(day, "Anerkennung", $"{n.Country.Name} erkennt {realm.NationName} an");
            }
        }

        /// <summary>Wie viele Länder haben das Land des Spielers anerkannt?</summary>
        public int RecognitionCount
        {
            get { int k = 0; foreach (bool b in Realm.Recognized) if (b) k++; return k; }
        }

        // ------------------------------------------------------------ NPC-Expansion

        /// <summary>
        /// Wöchentlich: NPC-Länder (Utility AI mit Persönlichkeit) beanspruchen freie Nachbarregionen. Chance je Land
        /// 3 %/Woche × (0,5 + Risiko) × (1 + Hunger), danach 120 Tage Pause; höchstens 45 % des anfangs freien Landes.
        /// Ziel per gewichtetem Zufall über den Nutzen (Boden, Küste, Fluss, Bodenschätze, Wald; Spielergrenze meiden vorsichtige
        /// Länder). Folgen nur als Beziehung: Anspruch direkt am Spielergebiet −6 („Grenzstreit“), andere Nachbarn −2.
        /// </summary>
        internal void ExpansionWeek(int day)
        {
            var p = Planet;
            var t = world.Trade;
            var realm = Realm;
            if (p == null) return;
            int free = p.FreeRegionCount;
            int floor = (int)Math.Ceiling(realm.InitialFreeRegions * (1.0 - RealmRules.ExpansionMaxShare));
            if (free <= floor) return;
            int me = p.PlayerCountryId;
            var candidates = new List<(Region r, float score)>();
            foreach (var c in p.Countries)
            {
                if (c.IsPlayer || c.Id >= realm.LastClaimDay.Length) continue;
                if (day - realm.LastClaimDay[c.Id] < RealmRules.ExpansionCooldownDays) continue;
                var nt = t?.Nation(c.Id);
                float risk = nt?.Personality.Risk ?? 0.5f;
                double hunger = c.Economy != null ? Math.Clamp(1.0 - c.Economy.Satisfaction[(int)GoodsCategory.Food], 0.0, 1.0) * 2.0 : 0.0;
                double chance = RealmRules.ExpansionWeeklyChance * (0.5 + risk) * (1.0 + hunger);
                if (!realm.Rng.Chance(chance)) continue;

                candidates.Clear();
                foreach (int rid in c.Regions)
                    foreach (int q in p.Regions[rid].Neighbors)
                    {
                        var r = p.Regions[q];
                        if (!r.IsFree) continue;
                        bool dup = false;
                        foreach (var x in candidates) if (x.r == r) { dup = true; break; }
                        if (dup) continue;
                        bool atPlayer = false;
                        foreach (int z in r.Neighbors) if (p.Regions[z].CountryId == me && me >= 0) atPlayer = true;
                        float score = 0.5f * r.Fertility + (r.IsCoastal ? 0.3f : 0f) + (r.HasRiver ? 0.2f : 0f)
                                      + 0.3f * MathF.Max(r.Ore, MathF.Max(r.Coal, r.Oil)) + 0.2f * r.Forest
                                      - (atPlayer ? RealmRules.ExpansionAvoidPlayer * (1f - risk) : 0f);
                        candidates.Add((r, score));
                    }
                if (candidates.Count == 0) continue;
                // Softmax-Auswahl (Temperatur 0,15) aus dem gespeicherten Zufall
                double sum = 0;
                var w = new double[candidates.Count];
                for (int i = 0; i < w.Length; i++) { w[i] = Math.Exp(candidates[i].score / 0.15); sum += w[i]; }
                double roll = realm.Rng.NextDouble() * sum;
                int pick = w.Length - 1;
                for (int i = 0; i < w.Length; i++) { roll -= w[i]; if (roll <= 0) { pick = i; break; } }
                var target = candidates[pick].r;

                p.AssignRegion(target, c);
                realm.LastClaimDay[c.Id] = day;
                realm.RegionsClaimedByNpc++;
                bool border = false;
                var others = new HashSet<int>();
                foreach (int z in target.Neighbors)
                {
                    int o = p.Regions[z].CountryId;
                    if (o == me && me >= 0) border = true;
                    else if (o >= 0 && o != c.Id) others.Add(o);
                }
                if (t != null)
                {
                    if (border) t.AdjustRelation(c.Id, me, -RealmRules.ExpansionPlayerConcern);
                    foreach (int o in others) t.AdjustRelation(c.Id, o, -RealmRules.ExpansionNeighborConcern);
                }
                realm.Report(day, border ? "Grenzstreit" : "Landnahme",
                    $"{c.Name} beansprucht {target.Name}" + (border ? " – direkt an unserer Grenze" : ""), border);
                if (--free <= floor) break;
            }
        }
    }
}
