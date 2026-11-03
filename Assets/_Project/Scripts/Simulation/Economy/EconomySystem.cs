using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Stadtkasse (Teil von G5 „Haushalt/Steuern“, vorgezogen): Kontostand, Buchungen je Kategorie,
    /// Steuersätze und Bilanz je Spielwoche.
    /// <para>
    /// Abrechnung <b>täglich</b> (<see cref="TickInterval"/> = 1 Spieltag): Steuern aus der aktuellen Belegung
    /// (bewohnte Haushalte, besetzte Arbeitsplätze je Zonenart) und Unterhalt (Straßen je Meter und Typ, freie
    /// Leitungen/Rohre, Versorgungsbauwerke, registrierte <see cref="IUpkeepSource"/>). Baukosten werden sofort beim
    /// Bauen gebucht (<see cref="IConstructionBudget"/>, gesetzt an Straßennetz, Gelände und Versorgung).
    /// Nach 7 Tagen wird die Woche abgeschlossen und landet in der <see cref="History"/>.
    /// </para>
    /// <para>
    /// Kredit: Überziehungsrahmen <see cref="EconomyRules.CreditLimit"/> mit Tageszinsen auf das Minus.
    /// Bauen ist erlaubt, solange der Kontostand danach nicht unter −Rahmen fällt. Mehr als
    /// <see cref="EconomyRules.FreezeAfterDaysInDebt"/> Tage am Stück im Minus → <see cref="ConstructionFrozen"/>
    /// (Baustopp), bis der Kontostand wieder ≥ 0 ist. Mit <see cref="WorldSettings.UnlimitedFunds"/> wird alles
    /// gebucht, aber nie blockiert (keine Zinsen, kein Baustopp) – Sandbox/Tests.
    /// </para>
    /// Rein deterministisch: kein Zufall, Summen in fester Reihenfolge.
    /// </summary>
    public sealed class EconomySystem : ISimulationSystem, IConstructionBudget
    {
        public int TickInterval => SimulationClock.TicksPerDay;

        readonly RoadNetwork roads;
        readonly CitySystem city;
        readonly UtilitySystem utilities;
        readonly float[] taxRates = new float[Budget.TaxKinds];
        readonly List<IUpkeepSource> upkeepSources = new List<IUpkeepSource>();
        readonly List<BudgetPeriod> history = new List<BudgetPeriod>();
        int billedDays;

        public EconomySystem(WorldSettings settings, RoadNetwork roads, CitySystem city, UtilitySystem utilities)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            this.city = city;
            this.utilities = utilities;
            UnlimitedFunds = settings.UnlimitedFunds;
            Balance = settings.StartingFunds;
            for (int i = 0; i < taxRates.Length; i++) taxRates[i] = EconomyRules.DefaultTaxRate;
            CurrentWeek = new BudgetPeriod { Index = 0, FirstDay = 0, OpeningBalance = Balance, ClosingBalance = Balance };
        }

        // ------------------------------------------------------------ Abfragen (UI)

        /// <summary>Kontostand in € (darf negativ sein: Überziehung).</summary>
        public double Balance { get; private set; }
        /// <summary>Sandbox: nichts wird blockiert, keine Zinsen, kein Baustopp (Buchungen laufen trotzdem).</summary>
        public bool UnlimitedFunds { get; set; }
        public double CreditLimit => EconomyRules.CreditLimit;
        /// <summary>Wie viel noch ausgegeben werden kann (Kontostand + Kreditrahmen; unendlich in der Sandbox).</summary>
        public double AvailableFunds => UnlimitedFunds ? double.PositiveInfinity : ConstructionFrozen ? 0 : Math.Max(0, Balance + CreditLimit);
        public bool IsOverdrawn => Balance < 0;
        /// <summary>Tage am Stück mit negativem Kontostand (bei der Tagesabrechnung gezählt).</summary>
        public int DaysInDebt { get; private set; }
        /// <summary>Baustopp wegen zu langer Überziehung: keine Neubauten, bis der Kontostand wieder ≥ 0 ist.</summary>
        public bool ConstructionFrozen { get; private set; }
        /// <summary>Tage bis zum Baustopp (nur sinnvoll, wenn überzogen).</summary>
        public int DaysUntilFreeze => Math.Max(0, EconomyRules.FreezeAfterDaysInDebt - DaysInDebt);

        /// <summary>Laufende Woche (inkl. Baukosten seit Wochenbeginn).</summary>
        public BudgetPeriod CurrentWeek { get; private set; }
        /// <summary>Zuletzt abgeschlossene Woche (null, solange es keine gibt).</summary>
        public BudgetPeriod LastWeek => history.Count > 0 ? history[history.Count - 1] : null;
        /// <summary>Saldo der letzten abgeschlossenen Woche (0, solange es keine gibt).</summary>
        public double LastWeekNet => LastWeek?.Net ?? 0;
        /// <summary>Abgeschlossene Wochen, älteste zuerst (höchstens <see cref="EconomyRules.HistoryWeeks"/>).</summary>
        public IReadOnlyList<BudgetPeriod> History => history;
        /// <summary>Die letzte Tagesabrechnung (Steuern/Unterhalt/Zinsen, ohne Baukosten).</summary>
        public BudgetPeriod LastDay { get; private set; }
        /// <summary>Wird bei jeder Buchung oder Satzänderung erhöht (für die Darstellung).</summary>
        public int Version { get; private set; }
        public IReadOnlyList<IUpkeepSource> UpkeepSources => upkeepSources;

        // --- G5 Warenketten (Hook): Tagesgewinn der Firma im Gebäude (NaN = keine Firma → pauschal je Job).
        // Gesetzt in SimulationWorld.CreateDefault (GoodsSystem.TaxableProfit).
        public Func<Building, double> TaxableProfit { get; set; }
        // --- Ende G5

        // ------------------------------------------------------------ Steuern

        public float TaxRate(TaxKind kind) => taxRates[(int)kind];

        /// <summary>Setzt einen Steuersatz (0..0,30; auf ganze Prozent gerundet).</summary>
        public void SetTaxRate(TaxKind kind, float rate)
        {
            rate = MathF.Round(Math.Clamp(rate, EconomyRules.MinTaxRate, EconomyRules.MaxTaxRate) * 100f) / 100f;
            if (taxRates[(int)kind] == rate) return;
            taxRates[(int)kind] = rate;
            Version++;
        }

        public void ChangeTaxRate(TaxKind kind, float delta) => SetTaxRate(kind, TaxRate(kind) + delta);

        /// <summary>
        /// Wirkung eines Steuersatzes auf die Nachfrage: 1 beim Standardsatz, darunter leicht mehr
        /// (+2 % je Prozentpunkt), darüber deutlich weniger (−4 % je Prozentpunkt, min. 0,1).
        /// </summary>
        public static float DemandFactorForRate(float rate)
        {
            float points = (rate - EconomyRules.DefaultTaxRate) * 100f;
            if (MathF.Abs(points) < 0.01f) return 1f;
            float f = points < 0f ? 1f - points * EconomyRules.DemandBonusPerPoint : 1f - points * EconomyRules.DemandPenaltyPerPoint;
            return MathF.Max(EconomyRules.MinDemandFactor, f);
        }

        public static TaxKind TaxKindOf(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Retail:
                case ZoneType.Mixed: return TaxKind.Commercial;
                case ZoneType.Office: return TaxKind.Office;
                case ZoneType.Industry: return TaxKind.Industry;
                default: return TaxKind.Residential;
            }
        }

        /// <summary>Faktor, mit dem das <see cref="CitySystem"/> die Nachfrage der Zonenart multipliziert (Hook).</summary>
        public float DemandFactor(ZoneType zone)
        {
            if (zone == ZoneType.Mixed)
                return (DemandFactorForRate(TaxRate(TaxKind.Residential)) + DemandFactorForRate(TaxRate(TaxKind.Commercial))) * 0.5f;
            return DemandFactorForRate(TaxRate(TaxKindOf(zone)));
        }

        // ------------------------------------------------------------ Kostenträger (Dienste G4 …)

        public void RegisterUpkeep(IUpkeepSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!upkeepSources.Contains(source)) upkeepSources.Add(source);
        }

        public bool UnregisterUpkeep(IUpkeepSource source) => upkeepSources.Remove(source);

        // ------------------------------------------------------------ IConstructionBudget

        public bool CanAfford(double cost)
        {
            if (UnlimitedFunds || cost <= 0) return true;
            if (ConstructionFrozen && Balance < 0) return false;
            return Balance - cost >= -CreditLimit - 1e-6;
        }

        public void Spend(BudgetCategory category, double amount)
        {
            if (!(amount > 0)) return;
            Book(CurrentWeek, category, amount);
        }

        public void Refund(double amount)
        {
            if (!(amount > 0)) return;
            Book(CurrentWeek, BudgetCategory.Demolition, amount);
        }

        /// <summary>Kurzform: prüfen und abbuchen. false = zu wenig Geld (nichts gebucht).</summary>
        public bool TrySpend(BudgetCategory category, double amount)
        {
            if (!CanAfford(amount)) return false;
            Spend(category, amount);
            return true;
        }

        void Book(BudgetPeriod period, BudgetCategory category, double amount)
        {
            period.Add(category, amount);
            Balance += Budget.IsIncome(category) ? amount : -amount;
            CurrentWeek.ClosingBalance = Balance;
            Version++;
        }

        // ------------------------------------------------------------ Tagesabrechnung

        /// <summary>
        /// Was die Tagesabrechnung jetzt buchen würde (Steuern und Unterhalt, ohne Zinsen) – für die UI-Prognose.
        /// Bucht nichts.
        /// </summary>
        public BudgetPeriod EstimateDay()
        {
            var d = new BudgetPeriod { Index = billedDays, FirstDay = billedDays, Days = 1 };

            // Steuern aus der Belegung
            if (city != null)
                foreach (var b in city.Buildings)
                {
                    if (!b.IsConstructed) continue;
                    if (b.OccupiedHouseholds > 0)
                        d.Add(BudgetCategory.ResidentialTax, b.OccupiedHouseholds * EconomyRules.HouseholdTaxBase * TaxRate(TaxKind.Residential));
                    // --- G5 Warenketten (Hook): Gewerbesteuer aus dem Gewinn der Firma statt pauschal je Job
                    if (TaxableProfit != null && TaxKindOf(b.Zone) != TaxKind.Residential)
                    {
                        double profit = TaxableProfit(b);
                        if (!double.IsNaN(profit))
                        {
                            var k = TaxKindOf(b.Zone);
                            if (profit > 0) d.Add(Budget.TaxCategory(k), profit * EconomyRules.ProfitTaxMultiplier * TaxRate(k));
                            continue;
                        }
                    }
                    // --- Ende G5
                    if (b.Workers > 0)
                    {
                        var kind = TaxKindOf(b.Zone);
                        if (kind == TaxKind.Residential) continue; // reine Wohnzonen haben keine Jobs
                        d.Add(Budget.TaxCategory(kind), b.Workers * JobTaxBase(kind) * TaxRate(kind));
                    }
                }

            // Unterhalt
            foreach (var s in roads.Segments)
                d.Add(BudgetCategory.RoadUpkeep, RoadBuilder.SegmentValue(s) * EconomyRules.RoadUpkeepShare);
            if (utilities != null)
            {
                foreach (var f in utilities.Facilities) d.Add(BudgetCategory.UtilityUpkeep, f.Type.UpkeepPerDay);
                foreach (var l in utilities.Lines)
                    d.Add(BudgetCategory.LineUpkeep, l.Length * UtilityCatalog.LineCostPerMeter(l.Kind) * EconomyRules.LineUpkeepShare);
            }
            foreach (var src in upkeepSources)
            {
                double weekly = src.WeeklyUpkeep();
                if (weekly > 0) d.AddService(src.Name, weekly / EconomyRules.DaysPerWeek);
            }
            return d;
        }

        static double JobTaxBase(TaxKind kind)
        {
            switch (kind)
            {
                case TaxKind.Office: return EconomyRules.OfficeJobTaxBase;
                case TaxKind.Industry: return EconomyRules.IndustryJobTaxBase;
                default: return EconomyRules.CommercialJobTaxBase;
            }
        }

        public void Tick(SimulationWorld world) => BillDay();

        /// <summary>Für Tests/Werkzeuge: einen Abrechnungstag sofort ausführen.</summary>
        public void ForceBillDay() => BillDay();

        void BillDay()
        {
            var day = EstimateDay();
            day.OpeningBalance = Balance;
            if (!UnlimitedFunds && Balance < 0)
                day.Add(BudgetCategory.Interest, -Balance * EconomyRules.DailyOverdraftInterest);

            // Einnahmen und Ausgaben des Tages in die Woche übernehmen
            double net = day.Net;
            CurrentWeek.AddFrom(day);
            Balance += net;
            day.ClosingBalance = Balance;
            LastDay = day;

            // Überziehung → Baustopp
            if (Balance < 0) DaysInDebt++; else DaysInDebt = 0;
            ConstructionFrozen = !UnlimitedFunds && Balance < 0 && DaysInDebt >= EconomyRules.FreezeAfterDaysInDebt;

            billedDays++;
            CurrentWeek.Days++;
            CurrentWeek.ClosingBalance = Balance;
            if (CurrentWeek.Days >= EconomyRules.DaysPerWeek) CloseWeek();
            Version++;
        }

        void CloseWeek()
        {
            history.Add(CurrentWeek);
            if (history.Count > EconomyRules.HistoryWeeks) history.RemoveAt(0);
            CurrentWeek = new BudgetPeriod
            {
                Index = CurrentWeek.Index + 1,
                FirstDay = billedDays,
                OpeningBalance = Balance,
                ClosingBalance = Balance,
            };
        }
    }
}
