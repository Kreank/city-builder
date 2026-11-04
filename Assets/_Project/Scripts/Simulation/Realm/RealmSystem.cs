using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Realm
{
    /// <summary>Ergebnis einer Prüfung (Stadt gründen, Land gründen, Wechseln) mit allen Bedingungen für die UI.</summary>
    public sealed class RealmCheck
    {
        public readonly List<(string Text, bool Ok, string Detail)> Items = new List<(string, bool, string)>();
        public bool Ok
        {
            get { foreach (var i in Items) if (!i.Ok) return false; return Items.Count > 0; }
        }
        /// <summary>Erste nicht erfüllte Bedingung (bzw. null).</summary>
        public string FirstProblem
        {
            get { foreach (var i in Items) if (!i.Ok) return i.Detail != null ? $"{i.Text}: {i.Detail}" : i.Text; return null; }
        }
        /// <summary>Erfüllte Bedingungen / alle (Fortschritt der Landesgründung).</summary>
        public float Progress
        {
            get { if (Items.Count == 0) return 0f; int k = 0; foreach (var i in Items) if (i.Ok) k++; return k / (float)Items.Count; }
        }
        /// <summary>Gewählte Weltzelle (Stadt gründen).</summary>
        public int Cell = -1;
        public RealmRoute Route;
        internal void Add(string text, bool ok, string detail = null) => Items.Add((text, ok, detail));
    }

    /// <summary>
    /// Etappe 7 Reich – Logik zu <see cref="PlayerRealm"/> (Zustand dort, Abschnitt „realm“). Täglich nach allen Stadtsystemen:
    /// Commands, Kennzahlen der aktiven Stadt übernehmen, aggregierte Städte einen Tag weiter, interne Lieferungen (Ankunft,
    /// Versand), Landesabgabe, Anerkennung, Länder-Volkswirtschaft des Spielers = Summe seiner Städte, wöchentlich NPC-Expansion.
    /// Spieleraktionen als <see cref="RealmCommand"/> (wie Handels-Commands). Deterministisch: Zufall nur aus
    /// <see cref="PlayerRealm.Rng"/> (gespeichert).
    /// </summary>
    public sealed partial class RealmSystem : ISimulationSystem
    {
        public int TickInterval => SimulationClock.TicksPerDay;

        readonly SimulationWorld world;
        int[] landmass;

        public RealmSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            var realm = world.Realm;
            realm.System = this;
            // Startstadt: Seeds der Stadtkarte (beim Laden überschreibt der Abschnitt „realm“)
            var first = realm.Cities[0];
            first.TerrainSeed = world.Settings.TerrainSeed;
            first.Seed = world.Seed;
            if (first.Economy.LastDay < 0) first.Economy.Treasury = world.Economy?.Balance ?? world.Settings.StartingFunds;
            // Interne Lieferungen aus der aktiven Stadt laufen über das Grenzlager wie Exportverträge (Firmen liefern per Lkw an)
            if (world.Trade != null)
            {
                world.Trade.ExtraExportDue = ExportDue;
                world.Trade.ExtraExportPrice = ExportPrice;
            }
        }

        public SimulationWorld World => world;
        public PlayerRealm Realm => world.Realm;
        Planet Planet => world.Planet;
        int Today => world.Clock.Day;

        // ------------------------------------------------------------ Commands

        /// <summary>Command einreihen (ausgeführt im nächsten Tagesschritt).</summary>
        public void Submit(RealmCommand cmd)
        {
            if (cmd != null) Realm.queue.Enqueue(cmd);
        }

        /// <summary>Command sofort ausführen (UI, auch bei Pause) und protokollieren.</summary>
        public TradeResult Execute(RealmCommand cmd)
        {
            if (cmd == null) return TradeResult.Fail("kein Command");
            var r = cmd.Apply(this);
            var log = Realm.log;
            log.Add(new RealmLogEntry(Today, cmd, r));
            if (log.Count > RealmRules.LogCapacity) log.RemoveRange(0, log.Count - RealmRules.LogCapacity);
            Realm.Version++;
            return r;
        }

        // ------------------------------------------------------------ Tageslauf

        public void Tick(SimulationWorld w)
        {
            int day = w.Clock.Day - 1; // abgelaufener Tag
            var realm = Realm;
            while (realm.queue.Count > 0) Execute(realm.queue.Dequeue());

            var active = realm.ActiveCity;
            active.Economy.CaptureFrom(w);
            active.LastDetailedDay = day;

            if (realm.Cities.Count > 1)
            {
                foreach (var c in realm.Cities)
                    if (c != active) c.Economy.StepDay(day, WorldCategoryPrice, Reserved(c));
                Arrivals(day);
                Dispatches(day);
                CloseDeliveries(day);
                SyncNationEconomy();
            }
            if (realm.IsNation)
            {
                CollectLevy(day);
                ReviewRecognition(day);
            }
            if (realm.NpcExpansion && day % TradeRules.DaysPerWeek == 3) ExpansionWeek(day);
            realm.Version++;
        }

        /// <summary>Für ausgehende interne Lieferungen reservierte Ware (zwei Wochen, Kategorie-Einheiten); null = keine.</summary>
        double[] Reserved(RealmCity city)
        {
            double[] r = null;
            foreach (var d in Realm.deliveries)
            {
                if (!d.IsActive || d.AllDispatched || d.From != city.Id) continue;
                r ??= new double[GoodsCategories.Count];
                int weeks = Math.Min(2, d.Terms.Weeks - d.WeeksDispatched);
                // + 10 % Polster für Verderb/Schwund bis zum Versandtag
                r[(int)TradeGoods.CategoryOf(d.Good)] += 1.1 * d.Terms.UnitsPerWeek * weeks * TradeGoods.CategoryUnitsPerGood(d.Good);
            }
            return r;
        }

        /// <summary>Weltpreis einer Kategorie (aus den Ländern; ohne Weltkarte Grundpreis).</summary>
        double WorldCategoryPrice(GoodsCategory c) => Planet?.Economy != null ? Planet.Economy.WorldPrice(c) : GoodsCategories.BasePrice[(int)c];

        /// <summary>Länder-Volkswirtschaft des Spielers = Umland je Stadt-Region + Einwohner aller Städte (erst ab zwei Städten).</summary>
        void SyncNationEconomy()
        {
            var e = Planet?.PlayerCityState?.Economy;
            if (e == null) return;
            e.SetPopulation(RealmRules.UmlandPerRegion * Realm.CityRegions.Count + Realm.TotalPopulation);
        }

        // ------------------------------------------------------------ Geld

        /// <summary>Gutschrift (+) bzw. Belastung (−) einer Stadtkasse: aktive Stadt über die Stadtkasse, sonst die aggregierte Kasse.</summary>
        internal void Book(RealmCity city, double amount)
        {
            if (city == null || amount == 0 || double.IsNaN(amount)) return;
            if (city.Id == Realm.ActiveCityId && world.Economy != null)
            {
                if (amount > 0) world.Economy.Post(BudgetCategory.RealmIncome, amount);
                else world.Economy.Post(BudgetCategory.RealmExpenses, -amount);
                city.Economy.Treasury = world.Economy.Balance;
            }
            else city.Economy.Treasury += amount;
        }

        void CollectLevy(int day)
        {
            var realm = Realm;
            double total = 0;
            foreach (var c in realm.Cities)
            {
                double tax = c.Economy.TaxesToday;
                if (!(tax > 0)) continue;
                double levy = tax * RealmRules.NationTaxShare;
                Book(c, -levy);
                total += levy;
            }
            realm.NationTreasury += total;
            realm.NationIncomeTotal += total;
        }

        internal TradeResult DoTransfer(int cityId, double amount)
        {
            var realm = Realm;
            var c = realm.City(cityId);
            if (!realm.IsNation) return TradeResult.Fail("Erst ein Land gründen – dann gibt es eine Landeskasse.");
            if (c == null) return TradeResult.Fail("unbekannte Stadt");
            if (amount == 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return TradeResult.Fail("Betrag fehlt");
            if (amount > 0 && amount > realm.NationTreasury + 1e-6) return TradeResult.Fail($"Landeskasse hat nur {realm.NationTreasury:N0} €");
            if (amount < 0 && c.Id == realm.ActiveCityId && world.Economy != null && !world.Economy.CanAfford(-amount))
                return TradeResult.Fail("Stadtkasse reicht nicht");
            realm.NationTreasury -= amount;
            Book(c, amount);
            return new TradeResult(true, amount > 0 ? $"{amount:N0} € an {c.Name} überwiesen" : $"{-amount:N0} € von {c.Name} an die Landeskasse");
        }

        internal TradeResult DoRename(int cityId, string name)
        {
            var c = Realm.City(cityId);
            if (c == null) return TradeResult.Fail("unbekannte Stadt");
            return Realm.RenameCity(cityId, name) ? new TradeResult(true, $"Die Stadt heißt jetzt {c.Name}") : TradeResult.Fail("Name leer oder schon vergeben");
        }

        // ------------------------------------------------------------ Stadt gründen

        /// <summary>Kann hier eine Stadt gegründet werden? Alle Bedingungen mit Erklärung (Weltkarte); <paramref name="cell"/> −1 = beste freie Zelle.</summary>
        public RealmCheck CheckFoundCity(int regionId, int cell = -1, string name = null)
        {
            var check = new RealmCheck();
            var realm = Realm;
            var p = Planet;
            if (p == null) { check.Add("Weltkarte", false, "ohne Weltkarte gibt es nur eine Stadt"); return check; }
            if (regionId < 0 || regionId >= p.Regions.Count) { check.Add("Region", false, "keine Region gewählt"); return check; }
            var region = p.Regions[regionId];
            var me = p.PlayerCountry;
            bool own = me != null && region.CountryId == me.Id;
            check.Add(own ? "Region gehört dir" : region.IsFree ? "Freies Land" : "Region frei", region.IsFree || own,
                region.IsFree || own ? null : $"gehört {region.Country?.Name}");
            check.Add($"Höchstens {RealmRules.MaxCities} Städte", realm.Cities.Count < RealmRules.MaxCities, $"{realm.Cities.Count} Städte");

            int chosen = cell >= 0 ? cell : BestCell(region);
            bool cellOk = chosen >= 0 && p.RegionOf[chosen] == regionId && CellFree(chosen);
            check.Add("Freier Platz in der Region", cellOk, cellOk ? null : $"zu nah an einer eigenen Stadt (mind. {RealmRules.MinCellSpacing} Weltzellen)");
            check.Cell = cellOk ? chosen : -1;

            // Erreichbarkeit über echte Wege: Nachbarregion (Autobahn/Bahn) bzw. Küste ↔ Küste mit Hafen
            var mother = realm.ActiveCity;
            CityEconomy.LinksOf(world, out bool roadConnected, out _, out bool hasHarbor); // live, ohne Zustand zu ändern
            int population = world.Citizens?.Stats.Population ?? 0;
            bool landLink = false, seaLink = false;
            foreach (int r in realm.CityRegions)
            {
                if (r == regionId) landLink = true;
                foreach (int q in p.Regions[r].Neighbors) if (q == regionId) landLink = true;
                if (p.Regions[r].IsCoastal && region.IsCoastal) seaLink = true;
            }
            bool viaRoad = landLink && roadConnected;
            bool viaSea = seaLink && hasHarbor;
            string how = viaRoad ? "über die Autobahn" : viaSea ? "über den Hafen" : landLink ? "Nachbarregion – aber die Stadt ist nicht an die Autobahn angeschlossen"
                : seaLink ? "Küste ↔ Küste – aber die Mutterstadt hat keinen Hafen in Betrieb" : "weder Nachbarregion noch per Schiff erreichbar";
            check.Add("Verbindung (Straße/Bahn/See)", viaRoad || viaSea, how);
            check.Add($"Mutterstadt {mother.Name}: {RealmRules.FoundMinPopulation:N0} Einwohner", population >= RealmRules.FoundMinPopulation,
                $"{population:N0} Einwohner");
            check.Add("Rathaus in der Mutterstadt", world.Services != null && world.Services.HasTownHall, "Wahrzeichen „Rathaus“ bauen");
            var eco = world.Economy;
            check.Add($"Gründungskosten {RealmRules.FoundingCost:N0} €", eco == null || eco.CanAfford(RealmRules.FoundingCost),
                eco != null ? $"Kasse {eco.Balance:N0} €" : null);
            if (name != null)
            {
                var clean = PlayerRealm.Clean(name);
                bool unique = clean != null;
                if (clean != null) foreach (var c in realm.Cities) if (string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)) unique = false;
                check.Add("Name", unique, clean == null ? "Name fehlt" : unique ? null : "schon vergeben");
            }
            if (check.Cell >= 0)
            {
                var probe = new RealmCity { Id = -1, RegionId = regionId, Cell = check.Cell };
                check.Route = BuildRoute(mother, probe);
            }
            return check;
        }

        internal TradeResult DoFoundCity(int regionId, int cell, string name)
        {
            var check = CheckFoundCity(regionId, cell, name ?? "");
            if (!check.Ok) return TradeResult.Fail(check.FirstProblem ?? "Bedingungen nicht erfüllt");
            var realm = Realm;
            var p = Planet;
            var region = p.Regions[regionId];
            world.Economy?.Spend(BudgetCategory.RealmExpenses, RealmRules.FoundingCost);
            int id = realm.NextCityId++;
            var city = new RealmCity
            {
                Id = id, Name = PlayerRealm.Clean(name), RegionId = regionId, Cell = check.Cell,
                TerrainSeed = unchecked(realm.Cities[0].TerrainSeed + id * 104729),
                Seed = unchecked(realm.Cities[0].Seed + id * 7907),
                FoundedDay = Today,
            };
            city.Economy.InitNew(p, region, RealmRules.NewCityFunds);
            city.Economy.LastDay = Today - 1;
            var me = p.PlayerCountry;
            bool wasFree = region.IsFree;
            if (wasFree && me != null) p.AssignRegion(region, me);
            realm.AddCity(city);
            realm.Apply();
            // Gebietsanspruch: Länder an dieser Region sehen das nicht gern
            if (wasFree && world.Trade != null && me != null)
            {
                var seen = new HashSet<int>();
                foreach (int q in region.Neighbors)
                {
                    int o = p.Regions[q].CountryId;
                    if (o >= 0 && o != me.Id && seen.Add(o)) world.Trade.AdjustRelation(o, me.Id, -RealmRules.ClaimNeighborConcern);
                }
            }
            realm.Report(Today, $"Stadt {city.Name} gegründet", $"Region {region.Name} · {check.Route}" , true);
            return new TradeResult(true, $"{city.Name} gegründet ({region.Name}) – über „Zur Stadt wechseln“ ausbauen", id);
        }

        /// <summary>Beste Weltzelle der Region (Boden, Fluss, Küste, Klima, flach), mit Abstand zu eigenen Städten; −1 = keine frei.</summary>
        public int BestCell(Region r)
        {
            var p = Planet;
            int best = -1;
            float bestScore = float.MinValue;
            foreach (int i in r.Cells)
            {
                if (!CellFree(i)) continue;
                float sc = p.Fertility[i] + (p.IsRiver(i) ? 0.5f : 0f) + (p.CoastDistance[i] == 1 ? 0.4f : 0f)
                           - MathF.Abs(p.Temperature[i] - 14f) / 25f - p.Ruggedness(i) / 2000f;
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            return best;
        }

        bool CellFree(int cell)
        {
            var p = Planet;
            foreach (var c in Realm.Cities)
            {
                if (c.Cell < 0) continue;
                int dx = Math.Abs(p.XOf(c.Cell) - p.XOf(cell));
                dx = Math.Min(dx, p.Width - dx);
                int dy = Math.Abs(p.YOf(c.Cell) - p.YOf(cell));
                if (Math.Max(dx, dy) < RealmRules.MinCellSpacing) return false;
            }
            return true;
        }

        // ------------------------------------------------------------ Wege zwischen eigenen Städten

        /// <summary>Weg zwischen zwei eigenen Städten (null ohne Verbindung).</summary>
        public RealmRoute Route(int fromCity, int toCity)
        {
            var a = Realm.City(fromCity);
            var b = Realm.City(toCity);
            return a == null || b == null || a == b ? null : BuildRoute(a, b);
        }

        /// <summary>
        /// Lkw über die Autobahn, wenn beide Städte auf derselben Landmasse liegen (Großkreis × 1,35 × 0,03 €/t·km), Bahn statt Lkw,
        /// wenn beide einen Güterbahnhof in Betrieb haben (× 0,56, schneller), Schiff, wenn beide an der Küste liegen (Vorlauf 60 km
        /// je Stadt ohne eigenen Hafen, Seeweg × 1,25 × 0,004 €/t·km, 2 × 4 €/t Umschlag) – der billigste gewinnt.
        /// </summary>
        RealmRoute BuildRoute(RealmCity a, RealmCity b)
        {
            var p = Planet;
            if (p == null || a.Cell < 0 || b.Cell < 0) return null;
            double direct = Math.Max(RealmRules.MinDistanceKm, DistanceKm(p, a.Cell, b.Cell));
            var route = new RealmRoute { From = a.Id, To = b.Id, DirectKm = direct };
            var lm = Landmass();
            bool sameLand = lm[a.Cell] >= 0 && lm[a.Cell] == lm[b.Cell];
            double best = double.MaxValue;
            if (sameLand)
            {
                double km = direct * TradeRules.LandDetour;
                bool rail = a.Economy.HasFreightRail && b.Economy.HasFreightRail;
                double cost = km * TradeRules.LandCostPerTonKm * (rail ? RealmRules.RailCostFactor : 1.0);
                best = cost;
                route.Mode = rail ? RealmRouteMode.Rail : RealmRouteMode.Road;
                route.LandKm = km; route.SeaKm = 0; route.CostPerTon = cost;
                route.LeadDays = Math.Max(1, (int)Math.Ceiling(1.0 + km / (TradeRules.LandKmPerDay * (rail ? RealmRules.RailSpeedFactor : 1.0))));
            }
            var ra = p.Regions[a.RegionId];
            var rb = p.Regions[b.RegionId];
            if (ra.IsCoastal && rb.IsCoastal)
            {
                double feeder = (a.Economy.HasHarbor ? 0 : RealmRules.FeederKmWithoutHarbor) + (b.Economy.HasHarbor ? 0 : RealmRules.FeederKmWithoutHarbor);
                double sea = direct * TradeRules.SeaDetour;
                double cost = feeder * TradeRules.LandCostPerTonKm + sea * TradeRules.SeaCostPerTonKm + 2 * TradeRules.PortHandlingPerTon;
                if (cost < best)
                {
                    route.Mode = RealmRouteMode.Sea;
                    route.LandKm = feeder; route.SeaKm = sea; route.CostPerTon = cost;
                    route.LeadDays = Math.Max(1, (int)Math.Ceiling(3.0 + feeder / TradeRules.LandKmPerDay + sea / TradeRules.SeaKmPerDay));
                    best = cost;
                }
            }
            return best < double.MaxValue ? route : null;
        }

        static double DistanceKm(Planet p, int a, int b)
        {
            var u = p.UnitVector(a);
            var v = p.UnitVector(b);
            double dot = Math.Clamp(System.Numerics.Vector3.Dot(u, v), -1f, 1f);
            return Math.Acos(dot) * Planet.EarthCircumferenceKm / (2.0 * Math.PI);
        }

        /// <summary>Zusammenhängende Landmassen (abgeleitet, einmal je Welt).</summary>
        int[] Landmass()
        {
            if (landmass != null) return landmass;
            var p = Planet;
            var id = new int[p.CellCount];
            for (int i = 0; i < id.Length; i++) id[i] = -1;
            var stack = new Stack<int>();
            int next = 0;
            for (int i = 0; i < id.Length; i++)
            {
                if (id[i] >= 0 || !p.IsLand(i)) continue;
                id[i] = next;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int j = p.Neighbor(c, dx, dy);
                            if (j < 0 || id[j] >= 0 || !p.IsLand(j)) continue;
                            id[j] = next;
                            stack.Push(j);
                        }
                }
                next++;
            }
            return landmass = id;
        }

        // ------------------------------------------------------------ Interne Lieferungen

        internal TradeResult DoProposeDelivery(int from, int to, ContractTerms terms)
        {
            var realm = Realm;
            var a = realm.City(from);
            var b = realm.City(to);
            if (a == null || b == null) return TradeResult.Fail("unbekannte Stadt");
            if (a == b) return TradeResult.Fail("Sender und Empfänger sind dieselbe Stadt");
            if (terms == null || terms.Kind != ContractKind.Supply || terms.Good == null) return TradeResult.Fail("Gut fehlt");
            if (!(terms.UnitsPerWeek > 0) || terms.UnitsPerWeek > 1e6) return TradeResult.Fail("Menge je Woche fehlt");
            if (terms.Weeks < TradeRules.MinWeeks || terms.Weeks > TradeRules.MaxWeeks) return TradeResult.Fail($"Laufzeit {TradeRules.MinWeeks}–{TradeRules.MaxWeeks} Wochen");
            if (!(terms.Price >= 0)) return TradeResult.Fail("Preis ungültig");
            var route = BuildRoute(a, b);
            if (route == null) return TradeResult.Fail($"Kein Weg von {a.Name} nach {b.Name} (andere Landmasse, keine Küste)");
            var t = terms.Clone();
            t.PenaltyShare = 0; // intern: keine Vertragsstrafe
            var d = new RealmDelivery
            {
                Id = realm.NextDeliveryId++, From = from, To = to, Terms = t,
                StartDay = Today, NextDispatchDay = Today + 1, Status = RealmDeliveryStatus.Active,
            };
            realm.deliveries.Add(d);
            realm.Report(Today, "Liefervereinbarung", $"{a.Name} → {b.Name}: {t.Describe()} · {route}");
            return new TradeResult(true, $"Lieferung #{d.Id}: {a.Name} → {b.Name} · {route}", d.Id);
        }

        internal TradeResult DoCancelDelivery(int id)
        {
            var d = Realm.Delivery(id);
            if (d == null) return TradeResult.Fail("unbekannte Lieferung");
            if (!d.IsActive) return TradeResult.Fail("schon beendet");
            d.Status = RealmDeliveryStatus.Cancelled;
            return new TradeResult(true, $"Lieferung #{id} beendet (Ware unterwegs kommt noch an)", id);
        }

        /// <summary>Preis je Einheit heute: fest bzw. Weltpreis × Faktor (ohne Weltkarte Grundpreis).</summary>
        public double UnitPrice(ContractTerms t)
        {
            if (t?.Good == null) return 0;
            if (t.PriceMode == PriceMode.Fixed) return t.Price;
            double wp = world.Trade != null ? world.Trade.WorldPrice(t.Good) : t.Good.BasePrice;
            return wp * t.Price;
        }

        /// <summary>Hook für <see cref="TradeSystem.ExtraExportDue"/>: Menge, die die aktive Stadt in den nächsten Tagen intern liefern muss.</summary>
        double ExportDue(GoodType g, int horizonDays)
        {
            var realm = Realm;
            if (realm.deliveries.Count == 0) return 0;
            double sum = 0;
            int today = Today;
            foreach (var d in realm.deliveries)
            {
                if (!d.IsActive || d.AllDispatched || d.From != realm.ActiveCityId || d.Good != g) continue;
                if (d.NextDispatchDay > today + horizonDays) continue;
                int weeks = Math.Min(d.Terms.Weeks - d.WeeksDispatched, 1 + (today + horizonDays - d.NextDispatchDay) / TradeRules.DaysPerWeek);
                sum += d.Terms.UnitsPerWeek * Math.Max(1, weeks);
            }
            return sum;
        }

        /// <summary>Hook für <see cref="TradeSystem.ExtraExportPrice"/>: Verrechnungspreis der am frühesten fälligen internen Lieferung.</summary>
        double ExportPrice(GoodType g)
        {
            RealmDelivery best = null;
            foreach (var d in Realm.deliveries)
            {
                if (!d.IsActive || d.AllDispatched || d.From != Realm.ActiveCityId || d.Good != g) continue;
                if (best == null || d.NextDispatchDay < best.NextDispatchDay) best = d;
            }
            return best != null ? UnitPrice(best.Terms) : 0.0;
        }

        void Dispatches(int day)
        {
            var realm = Realm;
            foreach (var d in realm.deliveries)
            {
                if (!d.IsActive || d.AllDispatched || d.NextDispatchDay > day) continue;
                var a = realm.City(d.From);
                var b = realm.City(d.To);
                var route = a != null && b != null ? BuildRoute(a, b) : null;
                double due = d.Terms.UnitsPerWeek;
                double units = route != null ? Take(a, d.Good, due) : 0;
                d.WeeksDispatched++;
                d.NextDispatchDay += TradeRules.DaysPerWeek;
                d.Shipped += units;
                d.Shortfall += due - units;
                d.LastShipped = units;
                if (units <= 1e-9) continue;
                double freight = units * route.UnitCost(d.Good);
                Book(a, -freight);
                d.FreightPaid += freight;
                realm.shipments.Add(new RealmShipment
                {
                    DeliveryId = d.Id, From = d.From, To = d.To, Good = d.Good, Units = units, UnitPrice = UnitPrice(d.Terms),
                    DispatchDay = day, ArriveDay = day + route.LeadDays, Mode = route.Mode,
                });
            }
        }

        void Arrivals(int day)
        {
            var realm = Realm;
            for (int i = 0; i < realm.shipments.Count; i++)
            {
                var s = realm.shipments[i];
                if (s.ArriveDay > day) continue;
                realm.shipments.RemoveAt(i--);
                var to = realm.City(s.To);
                var from = realm.City(s.From);
                if (to == null) continue;
                Give(to, s.Good, s.Units, s.UnitPrice);
                double value = s.Units * s.UnitPrice;
                Book(to, -value);
                Book(from, value);
                var d = realm.Delivery(s.DeliveryId);
                if (d != null) d.ValueSettled += value;
            }
        }

        void CloseDeliveries(int day)
        {
            var realm = Realm;
            foreach (var d in realm.deliveries)
            {
                if (!d.IsActive || !d.AllDispatched) continue;
                bool pending = false;
                foreach (var s in realm.shipments) if (s.DeliveryId == d.Id) { pending = true; break; }
                if (pending) continue;
                d.Status = RealmDeliveryStatus.Completed;
                var a = realm.City(d.From); var b = realm.City(d.To);
                realm.Report(day, "Lieferung abgeschlossen", $"{a?.Name} → {b?.Name}: {d.Shipped:0} von {d.Shipped + d.Shortfall:0} {d.Good.Name}");
            }
        }

        /// <summary>Ware beim Sender abholen: aktive Stadt aus dem Grenzlager (Firmen liefern per Lkw an), sonst aus dem aggregierten Lager.</summary>
        double Take(RealmCity from, GoodType g, double units)
        {
            if (from.Id == Realm.ActiveCityId)
            {
                var depot = world.Trade?.City;
                return depot != null ? depot.TakeExport(g, units) : 0;
            }
            int c = (int)TradeGoods.CategoryOf(g);
            double upg = TradeGoods.CategoryUnitsPerGood(g);
            double have = from.Economy.Stock[c] / upg;
            double take = Math.Max(0, Math.Min(units, have));
            from.Economy.Stock[c] -= take * upg;
            return take;
        }

        /// <summary>Ware beim Empfänger abliefern: aktive Stadt ins Grenzlager (Firmen kaufen sie wie Vertragsware, ohne Zoll), sonst ins aggregierte Lager.</summary>
        void Give(RealmCity to, GoodType g, double units, double unitPrice)
        {
            if (to.Id == Realm.ActiveCityId)
            {
                world.Trade?.City?.ReceiveImport(g, units, unitPrice, 0.0);
                return;
            }
            int c = (int)TradeGoods.CategoryOf(g);
            to.Economy.Stock[c] += units * TradeGoods.CategoryUnitsPerGood(g);
        }
    }
}
