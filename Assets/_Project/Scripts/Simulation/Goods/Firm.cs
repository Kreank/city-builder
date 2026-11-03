using System;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>Was eine Firma gerade (nicht) tut – für Inspektor und Tests.</summary>
    public enum FirmStatus : byte
    {
        UnderConstruction,
        Producing,
        NoWorkers,
        MissingInput,
        OutputFull,
        NoDeposit,
        NoUtilities,
        /// <summary>Einzelhandel/Büro arbeitet normal.</summary>
        Operating,
        /// <summary>Einzelhandel: Regale leer.</summary>
        NoStock,
    }

    /// <summary>Gewinnrechnung eines Tages: Erlös + Förderung − Wareneinsatz (Einkauf) − Löhne − Transport.</summary>
    public sealed class FirmLedger
    {
        /// <summary>Verkaufserlöse (lokal, Export, an Haushalte, Dienstleistungen).</summary>
        public double Revenue;
        /// <summary>Einkauf als Wareneinsatz: verbrauchte bzw. verkaufte Eingänge zum Einkaufspreis.</summary>
        public double InputCost;
        public double Wages;
        /// <summary>Transportkosten (Lkw-Zeit × Kostensatz, Fracht außerhalb).</summary>
        public double Transport;
        /// <summary>Gezahlte Einkäufe (Geldfluss, nicht Wareneinsatz) – Information.</summary>
        public double Purchases;
        /// <summary>Wirtschaftsförderung der Stadt (Produktion lenken), zählt zum Gewinn.</summary>
        public double Subsidy;
        public float Produced;
        public float Sold;
        public float Exported;
        public float Imported;

        public double Profit => Revenue + Subsidy - InputCost - Wages - Transport;

        internal void Clear()
        {
            Revenue = InputCost = Wages = Transport = Purchases = Subsidy = 0;
            Produced = Sold = Exported = Imported = 0f;
        }

        internal void CopyFrom(FirmLedger o)
        {
            Revenue = o.Revenue; InputCost = o.InputCost; Wages = o.Wages; Transport = o.Transport; Purchases = o.Purchases; Subsidy = o.Subsidy;
            Produced = o.Produced; Sold = o.Sold; Exported = o.Exported; Imported = o.Imported;
        }
    }

    /// <summary>
    /// Firma in einem Industrie-, Gewerbe- (Einzelhandel/Mischgebiet) oder Bürogebäude: Rezept bzw. Rolle,
    /// Lager (Eingang/Ausgang mit Kapazität, Index = <see cref="GoodType.Index"/>), Kasse und Gewinnrechnung.
    /// Zustand gehört dem <see cref="GoodsSystem"/>; von außen nur lesen.
    /// </summary>
    public sealed class Firm
    {
        internal Firm(Building building, Recipe recipe, int foundedDay)
        {
            Building = building;
            BuildingId = building.Id;
            Zone = building.Zone;
            Recipe = recipe;
            FoundedDay = foundedDay;
            int n = GoodsCatalog.Count;
            InputStock = new float[n];
            InputCapacity = new float[n];
            InputValue = new double[n];
            Incoming = new float[n];
            OutputStock = new float[n];
            OutputCapacity = new float[n];
        }

        public int BuildingId { get; }
        internal Building Building { get; }
        public ZoneType Zone { get; }
        public Recipe Recipe { get; internal set; }
        public FirmRole Role => Recipe.Role;
        public IndustryForm Form => Recipe.Form;
        public FirmStatus Status { get; internal set; } = FirmStatus.UnderConstruction;
        public int FoundedDay { get; internal set; }
        public int AgeDays { get; internal set; }

        // --- Lager
        internal readonly float[] InputStock, InputCapacity, Incoming, OutputStock, OutputCapacity;
        /// <summary>Einkaufswert des Eingangslagers (für den Wareneinsatz zum Durchschnittspreis).</summary>
        internal readonly double[] InputValue;

        public float InputOf(GoodType g) => InputStock[g.Index];
        public float InputCapacityOf(GoodType g) => InputCapacity[g.Index];
        /// <summary>Bestellte, noch nicht angekommene Menge.</summary>
        public float IncomingOf(GoodType g) => Incoming[g.Index];
        public float OutputOf(GoodType g) => OutputStock[g.Index];
        public float OutputCapacityOf(GoodType g) => OutputCapacity[g.Index];
        /// <summary>Durchschnittlicher Einkaufspreis des Eingangslagers (0 = leer).</summary>
        public double AverageInputCost(GoodType g) => InputStock[g.Index] > 1e-4f ? InputValue[g.Index] / InputStock[g.Index] : 0;

        // --- Personal/Produktion
        public int Jobs => Building.Jobs;
        public int Workers => Building.Workers;
        /// <summary>Letzte Produktivität 0..1+ (Belegschaft × Fachkräfte × Versorgung × Vorkommen).</summary>
        public float Productivity { get; internal set; }
        /// <summary>Anteil der Fachkräfte-Jobs (mittlerer Abschluss/Studium), die passend besetzt sind (0..1).</summary>
        public float SkillCoverage { get; internal set; } = 1f;
        /// <summary>Güte des Vorkommens am Standort (nur Gewinnung, 0..1).</summary>
        public float DepositQuality { get; internal set; }

        // --- Geld
        public double Cash { get; internal set; }
        /// <summary>Laufender Tag (wird zum Tageswechsel nach <see cref="Yesterday"/> geschoben).</summary>
        public FirmLedger Today { get; } = new FirmLedger();
        public FirmLedger Yesterday { get; } = new FirmLedger();
        /// <summary>Geglätteter Tagesgewinn/-erlös (Nachfrage der Zone, Pleite).</summary>
        public double ProfitEma { get; internal set; }
        public double RevenueEma { get; internal set; }
        /// <summary>Tage am Stück mit geglättetem Verlust.</summary>
        public int LossDays { get; internal set; }
        /// <summary>Geglättete Marge (Gewinn/Erlös, −1..1).</summary>
        public double Margin => RevenueEma > 1 ? Math.Clamp(ProfitEma / RevenueEma, -1.0, 1.0) : ProfitEma < -1 ? -1.0 : 0.0;
        public bool IsEstablished => AgeDays >= GoodsRules.EstablishedAfterDays;

        /// <summary>Gesamte Kapazität der Lager (Einheiten).</summary>
        public float TotalInputCapacity { get { float s = 0f; foreach (var c in InputCapacity) s += c; return s; } }
        public float TotalOutputCapacity { get { float s = 0f; foreach (var c in OutputCapacity) s += c; return s; } }

        /// <summary>Kreditrahmen der Firma (negativer Kontostand erlaubt bis −Rahmen).</summary>
        internal double CreditLimit => Math.Max(1, Jobs) * GoodsRules.DefaultWage * GoodsRules.CreditDaysOfWages;

        /// <summary>Kapazitäten aus Rezept und Arbeitsplätzen setzen (bei Gründung und Umstellung).</summary>
        internal void SizeStorage()
        {
            Array.Clear(InputCapacity, 0, InputCapacity.Length);
            Array.Clear(OutputCapacity, 0, OutputCapacity.Length);
            int jobs = Math.Max(1, Jobs);
            foreach (var i in Recipe.Inputs) InputCapacity[i.Good.Index] = MathF.Max(1f, i.PerJob * jobs * GoodsRules.InputStorageDays);
            foreach (var o in Recipe.Outputs) OutputCapacity[o.Good.Index] = MathF.Max(1f, o.PerJob * jobs * GoodsRules.OutputStorageDays);
        }

        public static string StatusText(FirmStatus s)
        {
            switch (s)
            {
                case FirmStatus.UnderConstruction: return "im Bau";
                case FirmStatus.Producing: return "produziert";
                case FirmStatus.NoWorkers: return "keine Beschäftigten";
                case FirmStatus.MissingInput: return "Eingänge fehlen";
                case FirmStatus.OutputFull: return "Lager voll";
                case FirmStatus.NoDeposit: return "kein Vorkommen";
                case FirmStatus.NoUtilities: return "ohne Strom/Wasser";
                case FirmStatus.NoStock: return "Regale leer";
                default: return "in Betrieb";
            }
        }

        public override string ToString() => $"{Recipe.Name} #{BuildingId}";
    }
}
