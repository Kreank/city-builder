using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Warenketten (G5): Firmen in Industrie-, Gewerbe- und Bürogebäuden, Lager, Produktion nach Rezepten, lokaler Markt
    /// mit Preisen aus Angebot/Nachfrage, Weltmarkt mit Import/Export über die Außenanschlüsse, Lieferverkehr (Lkw
    /// über das <see cref="TrafficSystem"/> bzw. Platzhalter) und Einkäufe der Haushalte im Einzelhandel.
    /// <para>
    /// Takt: alle <see cref="GoodsRules.TickInterval"/> Ticks (1 Spielstunde) Lieferungen, Produktion, Bestellungen,
    /// Export; zum Tageswechsel Löhne, Gewinnrechnung, Preise, Pleiten. Rein deterministisch (Zufall nur über
    /// <see cref="SimRandom"/>, Firmen in fester Reihenfolge nach Gebäude-Id).
    /// </para>
    /// Andere Systeme docken über Delegaten an (verbunden in <see cref="GoodsBinding"/>): Bodenkarte
    /// (<see cref="DepositAt"/>, <see cref="Extract"/>), Bürger (<see cref="WageBill"/>, <see cref="Workforce"/>),
    /// Versorgung (<see cref="UtilitiesSatisfied"/>).
    /// </summary>
    public sealed partial class GoodsSystem : ISimulationSystem
    {
        /// <summary>Jeden Tick (Lkw-Abfahrten verteilt über die Stunde); die eigentliche Arbeit stündlich.</summary>
        public int TickInterval => 1;

        readonly CitySystem city;
        readonly TrafficSystem traffic;
        readonly RoadNetwork network;
        readonly SimulationClock clock;
        readonly SimRandom rng;
        readonly RoadComponents components;
        readonly int seed;

        readonly List<Firm> firms = new List<Firm>();
        readonly Dictionary<int, Firm> byBuilding = new Dictionary<int, Firm>();
        readonly GoodMarket[] markets;
        /// <summary>Bedarf des laufenden Tages je Gut (Rezept-Eingänge der Belegschaft + Haushaltswünsche).</summary>
        readonly float[] dayDemand;
        int lastCityVersion = -1;

        public GoodsSystem(CitySystem city, TrafficSystem traffic, RoadNetwork network, SimulationClock clock, int seed = 1)
        {
            this.city = city ?? throw new ArgumentNullException(nameof(city));
            this.traffic = traffic;
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.seed = seed;
            rng = new SimRandom(seed * 4099 + 71);
            components = new RoadComponents(network);
            markets = new GoodMarket[GoodsCatalog.Count];
            for (int i = 0; i < markets.Length; i++)
            {
                markets[i] = new GoodMarket(GoodsCatalog.All[i]);
                markets[i].WorldPrice = GoodMarket.WorldPriceOn(GoodsCatalog.All[i], 0, seed);
            }
            dayDemand = new float[GoodsCatalog.Count];
            city.BuildingAdded += OnBuildingAdded;
            city.BuildingRemoved += OnBuildingRemoved;
            if (traffic != null) traffic.TripEnded += OnTripEnded;
        }

        // ------------------------------------------------------------ Hooks (Delegaten, verbunden beim Mergen/in GoodsBinding)

        /// <summary>
        /// Vorkommen (Menge/Güte 0..1) je Ressource-Id (<see cref="ResourceIds"/>) an einer Stelle – liefert die
        /// Welt-/Bodenkarte (Agent „world“). Standard: „fertility“ 0,6, „wood“ 0,4, sonst 0.
        /// </summary>
        public Func<string, Vector2, float> DepositAt { get; set; } = DefaultDeposit;

        /// <summary>
        /// Entnimmt eine Menge (Einheiten) einer Ressource an einer Stelle und liefert die tatsächlich entnommene.
        /// Standard: unbegrenzt (liefert die verlangte Menge).
        /// </summary>
        public Func<string, Vector2, float, float> Extract { get; set; } = (id, p, amount) => amount;

        /// <summary>
        /// Erntefaktor der Landwirtschaft am Spieltag (Jahreszeit/Klima, Mittel ≈ 1). null = 1. Gesetzt in
        /// <see cref="GoodsBinding.ConnectWorld"/> aus <c>world.Climate.HarvestFactor</c>.
        /// </summary>
        public Func<int, float> HarvestFactor { get; set; }
        int harvestDay = -1;
        float harvestToday = 1f;

        /// <summary>Erntefaktor heute (für Inspektor/Tests).</summary>
        public float HarvestToday
        {
            get
            {
                int day = clock.Day;
                if (day != harvestDay) { harvestDay = day; harvestToday = HarvestFactor != null ? MathF.Max(0f, HarvestFactor(day)) : 1f; }
                return harvestToday;
            }
        }

        /// <summary>
        /// Abstand (m) für die Güte des Vorkommens: &gt; 0 = Mittel über 3×3 Punkte (wie das Entnahmefenster der Bodenkarte),
        /// 0 = nur der Grundstücksmittelpunkt.
        /// </summary>
        public float DepositSampleSpacing { get; set; }

        /// <summary>Güte eines Vorkommens an einem Ort (0..1, s. <see cref="DepositSampleSpacing"/>).</summary>
        public float DepositQualityAt(string id, Vector2 p)
        {
            if (DepositAt == null || id == null) return 0f;
            float d = DepositSampleSpacing;
            if (d <= 0f) return Math.Clamp(DepositAt(id, p), 0f, 1f);
            float sum = 0f;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++) sum += DepositAt(id, p + new Vector2(x * d, y * d));
            return Math.Clamp(sum / 9f, 0f, 1f);
        }

        /// <summary>Löhne je Tag der Beschäftigten eines Gebäudes (Bürgersystem). null = Beschäftigte × <see cref="GoodsRules.DefaultWage"/>.</summary>
        public Func<int, double> WageBill { get; set; }

        /// <summary>
        /// Beschäftigte eines Gebäudes je Bildungsstufe: füllt [0] ohne/Grundschule, [1] mittlerer Abschluss, [2] Studium.
        /// null = alle Fachkräfte vorhanden.
        /// </summary>
        public Action<int, int[]> Workforce { get; set; }

        /// <summary>Strom/Wasser versorgt (oder in der Stadt noch nicht verlangt)? null = immer.</summary>
        public Func<Building, bool> UtilitiesSatisfied { get; set; }

        // --- Etappe 8 Handel (Hooks, gesetzt von Trade.TradeSystem.AttachCity)
        /// <summary>Weltmarktpreis je Gut aus den Ländern (Angebot/Nachfrage). null = deterministisches Rauschen (G5, ohne Welt).</summary>
        public Func<GoodType, float> WorldPriceSource { get; set; }
        /// <summary>Verträge/Grenzlager/Zölle. null = kein Handel zwischen Ländern (G5 wie vorher).</summary>
        public ITradeGateway TradeGateway { get; set; }

        /// <summary>Weltmarktpreise sofort aus <see cref="WorldPriceSource"/> übernehmen (z. B. beim Anschließen).</summary>
        public void RefreshWorldPrices()
        {
            if (WorldPriceSource == null) return;
            foreach (var m in markets) m.WorldPrice = WorldPriceSource(m.Good);
        }
        // --- Ende Etappe 8

        /// <summary>Pleite-Firmen: Gebäude wird verlassen (abgerissen). Aus = Firma macht mit Schulden weiter.</summary>
        public bool AbandonBankruptBuildings { get; set; } = true;

        public static float DefaultDeposit(string id, Vector2 p)
        {
            switch (id)
            {
                case ResourceIds.Fertility: return 0.6f;
                case ResourceIds.Wood: return 0.4f;
                default: return 0f;
            }
        }

        // ------------------------------------------------------------ Abfrage-API (Inspektor/HUD)

        public IReadOnlyList<Firm> Firms => firms;
        /// <summary>Firma im Gebäude (null = keine, z. B. Wohngebäude).</summary>
        public Firm FirmAt(int buildingId) => byBuilding.TryGetValue(buildingId, out var f) ? f : null;
        public GoodMarket Market(GoodType g) => markets[g.Index];
        public IReadOnlyList<GoodMarket> Markets => markets;
        public GoodsStats Stats { get; } = new GoodsStats();
        /// <summary>Import/Export-Bilanz gestern bzw. seit Spielbeginn.</summary>
        public TradeBalance TradeYesterday { get; } = new TradeBalance();
        public TradeBalance TradeTotal { get; } = new TradeBalance();
        /// <summary>Wird bei jedem Tageswechsel erhöht (für die Darstellung).</summary>
        public int Version { get; private set; }

        /// <summary>Hat das Gebäude eine Firma (Industrie, Einzelhandel, Mischgebiet, Büro)?</summary>
        public static bool HasFirm(ZoneType z) =>
            z == ZoneType.Industry || z == ZoneType.Retail || z == ZoneType.Mixed || z == ZoneType.Office;

        /// <summary>Zu versteuernder Tagesgewinn einer Firma (Hook der Stadtkasse). NaN = keine Firma → pauschale Steuer.</summary>
        public double TaxableProfit(Building b)
        {
            if (b == null || !byBuilding.TryGetValue(b.Id, out var f)) return double.NaN;
            return f.Yesterday.Profit;
        }

        /// <summary>
        /// Strom (kW) bzw. Wasser (m³/h) eines Industriegebäudes aus dem Rezept × Arbeitsplätze (Hook
        /// <c>UtilitySystem.DemandProvider</c>). Andere Gebäude/ohne Firma → NaN (Katalogwert); mind. 1 kW / 0,1 m³/h.
        /// </summary>
        public float UtilityDemand(Building b, Utilities.UtilityKind kind)
        {
            if (b == null || !byBuilding.TryGetValue(b.Id, out var f) || !f.Recipe.IsIndustry) return float.NaN;
            int jobs = Math.Max(1, b.Jobs);
            return kind == Utilities.UtilityKind.Power ? MathF.Max(1f, f.Recipe.PowerPerJob * jobs) : MathF.Max(0.1f, f.Recipe.WaterPerJob * jobs);
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            long now = clock.Tick;
            if (now % GoodsRules.TickInterval == 0) Step(now);
            LaunchPending(now, now);
        }

        /// <summary>Für Tests: eine Spielstunde des Warensystems sofort ausführen (ohne die übrigen Systeme, alle Lkw fahren sofort ab).</summary>
        public void ForceStep()
        {
            Step(clock.Tick, forceDay: false);
            LaunchPending(clock.Tick, long.MaxValue);
        }

        /// <summary>Für Tests: Tageswechsel sofort ausführen.</summary>
        public void ForceCloseDay() => CloseDay();

        void Step(long now, bool forceDay = true)
        {
            SyncFirms();
            DeliverDue(now);
            foreach (var f in firms) UpdateProductivity(f);
            foreach (var f in firms) Produce(f);
            PlaceOrders(now);
            ExportSurplus(now);
            if (forceDay && now % SimulationClock.TicksPerDay == 0 && now > 0) CloseDay();
        }

        // ------------------------------------------------------------ Firmen anlegen/entfernen

        void OnBuildingAdded(Building b)
        {
            if (!HasFirm(b.Zone) || byBuilding.ContainsKey(b.Id)) return;
            CreateFirm(b);
        }

        void OnBuildingRemoved(Building b)
        {
            if (!byBuilding.TryGetValue(b.Id, out var f)) return;
            byBuilding.Remove(b.Id);
            firms.Remove(f);
        }

        /// <summary>Gebäude, die vor dem Anmelden entstanden sind bzw. ohne Ereignis hinzukamen, bekommen eine Firma.</summary>
        void SyncFirms()
        {
            if (city.Version == lastCityVersion) return;
            lastCityVersion = city.Version;
            List<Building> missing = null;
            foreach (var b in city.Buildings)
                if (HasFirm(b.Zone) && !byBuilding.ContainsKey(b.Id)) (missing ??= new List<Building>()).Add(b);
            if (missing == null) return;
            missing.Sort((a, c) => a.Id.CompareTo(c.Id));
            foreach (var b in missing) CreateFirm(b);
        }

        Firm CreateFirm(Building b)
        {
            Recipe recipe = b.Zone == ZoneType.Office ? RecipeCatalog.Office
                : b.Zone == ZoneType.Industry ? ChooseIndustry(b, null)
                : RecipeCatalog.Retail;
            var f = new Firm(b, recipe, clock.Day);
            Found(f);
            // Ids steigen monoton → Einfügen am Ende hält die Liste sortiert; sonst einsortieren
            int i = firms.Count;
            while (i > 0 && firms[i - 1].BuildingId > f.BuildingId) i--;
            firms.Insert(i, f);
            byBuilding[b.Id] = f;
            return f;
        }

        /// <summary>Startkapital, Lager, Erstausstattung (bei Gründung und Umstellung).</summary>
        void Found(Firm f)
        {
            f.SizeStorage();
            sizedJobs[f.BuildingId] = f.Jobs;
            f.Cash = Math.Max(1, f.Jobs) * GoodsRules.StartCashPerJob;
            Array.Clear(f.InputStock, 0, f.InputStock.Length);
            Array.Clear(f.InputValue, 0, f.InputValue.Length);
            foreach (var i in f.Recipe.Inputs)
            {
                int g = i.Good.Index;
                float amount = f.InputCapacity[g] * GoodsRules.InitialInputFill;
                double value = amount * markets[g].LocalPrice;
                f.InputStock[g] = amount;
                f.InputValue[g] = value;
                f.Cash -= value;
            }
            f.DepositQuality = f.Recipe.Deposit != null ? DepositQualityAt(f.Recipe.Deposit, f.Building.Position) : 0f;
            f.ProfitEma = f.RevenueEma = 0;
            f.LossDays = 0;
        }

        readonly Dictionary<int, int> sizedJobs = new Dictionary<int, int>();

        /// <summary>
        /// Firma auf ein anderes Rezept umstellen („neuer Betreiber“: Lager geleert, Startkapital neu) – für Tests und
        /// später als Spieleraktion (Spezialisierung). Industrie nur auf Industrie-Rezepte, sonst nur das eigene.
        /// </summary>
        public bool Restructure(Firm f, Recipe r)
        {
            if (f == null || r == null || !byBuilding.ContainsKey(f.BuildingId)) return false;
            if (f.Zone == ZoneType.Industry ? !r.IsIndustry : r != f.Recipe) return false;
            f.Recipe = r;
            Array.Clear(f.OutputStock, 0, f.OutputStock.Length);
            Array.Clear(f.Incoming, 0, f.Incoming.Length);
            f.Today.Clear();
            Found(f);
            return true;
        }

        // ------------------------------------------------------------ Produktion

        readonly int[] eduBuffer = new int[3];

        void UpdateProductivity(Firm f)
        {
            var b = f.Building;
            if (!b.IsConstructed) { f.Status = FirmStatus.UnderConstruction; f.Productivity = 0f; return; }
            int jobs = b.Jobs;
            if (jobs <= 0 || b.Workers <= 0) { f.Status = FirmStatus.NoWorkers; f.Productivity = 0f; return; }
            float staffing = MathF.Min(1f, b.Workers / (float)jobs);
            float skill = 1f - GoodsRules.SkillPenalty * (1f - f.SkillCoverage);
            float p = staffing * skill;
            f.Status = f.Role == FirmRole.Retail || f.Role == FirmRole.Office ? FirmStatus.Operating : FirmStatus.Producing;
            if (UtilitiesSatisfied != null && !UtilitiesSatisfied(b))
            {
                p *= GoodsRules.UnsuppliedProductivity;
                f.Status = FirmStatus.NoUtilities;
            }
            if (f.Recipe.Deposit != null)
            {
                if (f.DepositQuality < f.Recipe.MinDeposit) { f.Status = FirmStatus.NoDeposit; f.Productivity = 0f; return; }
                p *= Math.Clamp(0.5f + f.DepositQuality, 0.5f, 1.5f);
            }
            if (f.Form == IndustryForm.Agriculture) p *= HarvestToday; // Jahreszeit/Klima
            f.Productivity = p;
            if (f.Role == FirmRole.Retail)
            {
                // Grundbedarf leer → „Regale leer“
                var food = GoodsCatalog.ConsumerGoods[0];
                if (f.InputStock[food.Index] < 0.05f && f.InputCapacity[food.Index] > 0f) f.Status = FirmStatus.NoStock;
            }
        }

        /// <summary>Fachkräfte-Abdeckung aus der Bildung der Beschäftigten (täglich).</summary>
        void UpdateSkills(Firm f)
        {
            float skilledShare = f.Recipe.Workforce[1] + f.Recipe.Workforce[2];
            if (Workforce == null || skilledShare <= 0f || f.Workers <= 0) { f.SkillCoverage = 1f; return; }
            eduBuffer[0] = eduBuffer[1] = eduBuffer[2] = 0;
            Workforce(f.BuildingId, eduBuffer);
            float needed = skilledShare * f.Workers;
            f.SkillCoverage = Math.Clamp((eduBuffer[1] + eduBuffer[2]) / MathF.Max(0.001f, needed), 0f, 1f);
        }

        void Produce(Firm f)
        {
            if (!f.Recipe.IsIndustry || f.Productivity <= 0f) return;
            var r = f.Recipe;
            float jobs = f.Jobs;
            float share = f.Productivity / GoodsRules.StepsPerDay;
            float scale = 1f;
            FirmStatus limit = f.Status;
            foreach (var i in r.Inputs)
            {
                float need = i.PerJob * jobs * share;
                if (need <= 0f) continue;
                float s = f.InputStock[i.Good.Index] / need;
                if (s < scale) { scale = s; limit = FirmStatus.MissingInput; }
            }
            foreach (var o in r.Outputs)
            {
                float make = o.PerJob * jobs * share;
                if (make <= 0f) continue;
                float s = (f.OutputCapacity[o.Good.Index] - f.OutputStock[o.Good.Index]) / make;
                if (s < scale) { scale = s; limit = FirmStatus.OutputFull; }
            }
            scale = Math.Clamp(scale, 0f, 1f);
            if (scale > 0f && r.Deposit != null && r.Outputs.Length > 0 && Extract != null)
            {
                float request = r.Outputs[0].PerJob * jobs * share * scale;
                float got = MathF.Max(0f, MathF.Min(request, Extract(r.Deposit, f.Building.Position, request)));
                if (got < request * 0.999f) limit = FirmStatus.NoDeposit;
                scale *= request > 0f ? got / request : 0f;
            }
            if (scale < 0.999f && f.Status == FirmStatus.Producing) f.Status = limit;
            if (scale <= 0f) return;

            foreach (var i in r.Inputs)
            {
                int g = i.Good.Index;
                float use = MathF.Min(f.InputStock[g], i.PerJob * jobs * share * scale);
                double cost = f.AverageInputCost(i.Good) * use;
                f.InputStock[g] -= use;
                f.InputValue[g] = Math.Max(0, f.InputValue[g] - cost);
                if (f.InputStock[g] < 1e-5f) { f.InputStock[g] = 0f; f.InputValue[g] = 0; }
                f.Today.InputCost += cost;
                markets[g].Today.Consumed += use;
            }
            foreach (var o in r.Outputs)
            {
                int g = o.Good.Index;
                float make = o.PerJob * jobs * share * scale;
                f.OutputStock[g] = MathF.Min(f.OutputCapacity[g], f.OutputStock[g] + make);
                f.Today.Produced += make;
                markets[g].Today.Produced += make;
            }
        }

        // ------------------------------------------------------------ Tageswechsel

        readonly List<Firm> bankrupt = new List<Firm>();

        void CloseDay()
        {
            int day = clock.Day;
            int residents = city.Population.Residents;
            int officeWorkers = 0;
            foreach (var f in firms) if (f.Role == FirmRole.Office) officeWorkers += f.Workers;
            // Dienstleistungs-Nachfrage: ~7 Bürojobs je 100 Einwohner (wie die Büro-Nachfrage im CitySystem)
            // (zu viele Büros: Dienstleistungen gehen teils auch nach außen → Untergrenze 0,75)
            float serviceDemand = Math.Clamp(0.5f + 0.5f * (residents * 0.07f + 10f) / MathF.Max(10f, officeWorkers), 0.75f, 1.3f);

            var s = Stats;
            s.Firms = firms.Count;
            s.Extraction = s.Processing = s.Manufacturing = s.Retail = s.Office = 0;
            s.TotalProfitYesterday = 0;
            bankrupt.Clear();
            foreach (var f in firms)
            {
                switch (f.Role)
                {
                    case FirmRole.Extraction: s.Extraction++; break;
                    case FirmRole.Processing: s.Processing++; break;
                    case FirmRole.Manufacturing: s.Manufacturing++; break;
                    case FirmRole.Retail: s.Retail++; break;
                    default: s.Office++; break;
                }
                var b = f.Building;
                if (!b.IsConstructed) continue;

                // Bedarf (Eingänge der aktuellen Belegschaft; Läden zählen über die Haushaltswünsche)
                if (f.Role != FirmRole.Retail)
                    foreach (var i in f.Recipe.Inputs) dayDemand[i.Good.Index] += i.PerJob * f.Workers;

                // Büro: Dienstleistungserlös
                if (f.Role == FirmRole.Office && f.Workers > 0)
                {
                    double revenue = f.Workers * GoodsRules.OfficeRevenuePerWorker * MathF.Max(0.2f, f.Productivity) * serviceDemand;
                    f.Today.Revenue += revenue;
                    f.Cash += revenue;
                }

                // Löhne
                double wages = WageBill != null ? WageBill(f.BuildingId) : f.Workers * GoodsRules.DefaultWage;
                f.Today.Wages += wages;
                f.Cash -= wages;

                // Verderb
                foreach (var g in GoodsCatalog.All)
                {
                    if (g.SpoilPerDay <= 0f) continue;
                    int k = g.Index;
                    if (f.OutputStock[k] > 0f) { float lost = f.OutputStock[k] * g.SpoilPerDay; f.OutputStock[k] -= lost; markets[k].Today.Spoiled += lost; }
                    if (f.InputStock[k] > 0f)
                    {
                        float lost = f.InputStock[k] * g.SpoilPerDay;
                        double cost = f.AverageInputCost(g) * lost;
                        f.InputStock[k] -= lost;
                        f.InputValue[k] = Math.Max(0, f.InputValue[k] - cost);
                        f.Today.InputCost += cost;
                        markets[k].Today.Spoiled += lost;
                    }
                }

                // Gewinnrechnung
                f.Yesterday.CopyFrom(f.Today);
                f.Today.Clear();
                double profit = f.Yesterday.Profit;
                float w = GoodsRules.EmaWeight;
                f.ProfitEma = f.AgeDays == 0 ? profit : f.ProfitEma + (profit - f.ProfitEma) * w;
                f.RevenueEma = f.AgeDays == 0 ? f.Yesterday.Revenue : f.RevenueEma + (f.Yesterday.Revenue - f.RevenueEma) * w;
                f.AgeDays++;
                f.LossDays = f.ProfitEma < 0 ? f.LossDays + 1 : 0;
                s.TotalProfitYesterday += profit;

                if (f.IsEstablished && f.LossDays >= GoodsRules.BankruptAfterLossDays && f.Cash < -f.CreditLimit) bankrupt.Add(f);

                // Täglich: Belegschaft/Bildung, Vorkommen, Lagergröße bei geänderten Arbeitsplätzen
                UpdateSkills(f);
                if (f.Recipe.Deposit != null)
                    f.DepositQuality = DepositQualityAt(f.Recipe.Deposit, b.Position);
                if (!sizedJobs.TryGetValue(f.BuildingId, out int sized) || sized != f.Jobs)
                {
                    f.SizeStorage();
                    sizedJobs[f.BuildingId] = f.Jobs;
                }
            }

            // Märkte: Bestand, Bedarf, Preise, Weltmarkt
            var stock = new float[markets.Length];
            foreach (var f in firms)
                for (int g = 0; g < stock.Length; g++) stock[g] += f.OutputStock[g] + f.InputStock[g];
            var ty = TradeYesterday;
            ty.ImportValue = ty.ExportValue = 0;
            ty.ImportedUnits = ty.ExportedUnits = 0;
            for (int g = 0; g < markets.Length; g++)
            {
                var m = markets[g];
                ty.ImportValue += m.Today.ImportValue;
                ty.ExportValue += m.Today.ExportValue;
                ty.ImportedUnits += m.Today.Imported;
                ty.ExportedUnits += m.Today.Exported;
                dayDemand[g] += m.Today.HouseholdWanted;
                m.CloseDay(stock[g], dayDemand[g], WorldPriceSource != null ? WorldPriceSource(m.Good) // Etappe 8: Weltpreis aus den Ländern
                    : GoodMarket.WorldPriceOn(m.Good, day + 1, seed));
                dayDemand[g] = 0f;
            }
            TradeTotal.ImportValue += ty.ImportValue;
            TradeTotal.ExportValue += ty.ExportValue;
            TradeTotal.ImportedUnits += ty.ImportedUnits;
            TradeTotal.ExportedUnits += ty.ExportedUnits;

            // Haushalte und Lieferungen (Tageszähler)
            s.PurchasesYesterday = purchasesToday;
            s.SuccessfulPurchasesYesterday = successfulToday;
            for (int t = 0; t < 4; t++)
            {
                s.TierWantedYesterday[t] = tierWantedToday[t];
                s.TierBoughtYesterday[t] = tierBoughtToday[t];
                tierWantedToday[t] = tierBoughtToday[t] = 0f;
            }
            purchasesToday = successfulToday = 0;
            s.ShipmentsYesterday = shipmentsToday;
            s.TrucksYesterday = trucksToday;
            s.DeliveredYesterday = deliveredToday;
            s.TruckFailedYesterday = truckFailedToday;
            s.PlaceholderYesterday = truckSkippedToday;
            shipmentsToday = trucksToday = deliveredToday = truckFailedToday = truckSkippedToday = 0;

            UpdatePlanned();

            // Verfall: Pleite → Gebäude wird verlassen
            foreach (var f in bankrupt)
            {
                s.Bankruptcies++;
                if (AbandonBankruptBuildings) city.RemoveBuilding(f.Building);
            }
            Version++;
        }

        /// <summary>Geplante Kapazität/Bedarf je Gut (alle Firmen inkl. im Bau, geschätzter Haushaltskonsum) – für die Standortwahl.</summary>
        void UpdatePlanned()
        {
            var cap = new float[markets.Length];
            var dem = new float[markets.Length];
            foreach (var f in firms)
            {
                int jobs = f.Jobs;
                foreach (var o in f.Recipe.Outputs) cap[o.Good.Index] += o.PerJob * jobs;
                if (f.Role != FirmRole.Retail) foreach (var i in f.Recipe.Inputs) dem[i.Good.Index] += i.PerJob * jobs;
            }
            float residents = city.Population.Residents;
            foreach (var g in GoodsCatalog.ConsumerGoods)
            {
                float share = g.Consumer == ConsumerTier.Basic ? 1f : g.Consumer == ConsumerTier.Comfort ? 0.6f : 0.3f;
                float estimate = residents * 0.6f * g.HouseholdPerDay * share;
                dem[g.Index] += MathF.Max(estimate, markets[g.Index].Yesterday.HouseholdWanted);
            }
            for (int g = 0; g < markets.Length; g++)
            {
                markets[g].CapacityPerDay = cap[g];
                markets[g].PlannedDemandPerDay = dem[g];
            }
        }

        // ------------------------------------------------------------ Nachfrage der Zonen (Hook CitySystem)

        /// <summary>
        /// Zu-/Abschlag auf die Nachfrage einer Zonenart (additiv, Hook <c>CitySystem.BusinessDemand</c>):
        /// geglättete Marge der etablierten Firmen → ±<see cref="GoodsRules.ProfitDemandRange"/> (tanh),
        /// Industrie zusätzlich bei hohem Importanteil, Einzelhandel bei unerfüllten Einkäufen. 0 ohne Daten.
        /// </summary>
        public float DemandOffset(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Industry:
                {
                    float o = MarginOffset(f => f.Recipe.IsIndustry);
                    double imports = 0, local = 0;
                    foreach (var m in markets)
                    {
                        if (!ProducibleLocally(m.Good)) continue;
                        imports += m.Yesterday.ImportValue;
                        local += m.Yesterday.Produced * m.LocalPrice;
                    }
                    if (imports > 0) o += GoodsRules.ImportDemandBonus * (float)(imports / (imports + local));
                    return o;
                }
                case ZoneType.Retail:
                case ZoneType.Mixed:
                {
                    float o = MarginOffset(f => f.Role == FirmRole.Retail);
                    float wanted = Stats.TierWantedYesterday[(int)ConsumerTier.Basic];
                    if (wanted > 0f)
                        o += GoodsRules.UnmetShoppingBonus * Math.Clamp(1f - Stats.TierBoughtYesterday[(int)ConsumerTier.Basic] / wanted, 0f, 1f);
                    return zone == ZoneType.Mixed ? o * 0.5f : o;
                }
                case ZoneType.Office:
                    return MarginOffset(f => f.Role == FirmRole.Office);
                default:
                    return 0f;
            }
        }

        float MarginOffset(Func<Firm, bool> filter)
        {
            double margin = 0, weight = 0;
            foreach (var f in firms)
            {
                if (!f.IsEstablished || !filter(f)) continue;
                double w = Math.Max(1, f.Jobs);
                margin += f.Margin * w;
                weight += w;
            }
            if (weight <= 0) return 0f;
            return GoodsRules.ProfitDemandRange * MathF.Tanh((float)(margin / weight) / GoodsRules.ProfitDemandMargin);
        }

        static bool ProducibleLocally(GoodType g)
        {
            foreach (var r in RecipeCatalog.Industry)
                if (r.Produces(g) && r.Deposit == null) return true;
            return g == GoodsCatalog.Grain || g == GoodsCatalog.Livestock || g == GoodsCatalog.Wood;
        }

        // ------------------------------------------------------------ Hilfen

        int ComponentOf(Building b)
        {
            if (b == null) return -1;
            int comp = components.OfSegment(b.FrontSegmentId);
            if (comp >= 0) return comp;
            var seg = network.FindNearestSegment(b.Entrance, 4f);
            return seg == null ? -1 : components.OfNode(seg.Start.Id);
        }

        /// <summary>Anschluss einer Gebäudezufahrt an das Straßennetz (wie die Bürgerfahrten).</summary>
        bool TryTripEnd(Building b, out TripEnd end)
        {
            end = default;
            if (b == null) return false;
            var seg = network.GetSegment(b.FrontSegmentId);
            float t = b.FrontT;
            if (seg == null || Vector2.Distance(seg.Evaluate(t), b.Entrance) > 4f)
            {
                seg = network.FindNearestSegment(b.Entrance, 4f);
                if (seg == null) return false;
                t = seg.ClosestT(b.Entrance);
            }
            end = TripEnd.AtSegment(seg.Id, t);
            return true;
        }
    }
}
