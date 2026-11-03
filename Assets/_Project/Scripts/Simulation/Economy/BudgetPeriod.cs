using System.Collections.Generic;

namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Buchungen eines Zeitraums (ein Tag oder eine Spielwoche). Beträge je Kategorie sind immer ≥ 0;
    /// ob Einnahme oder Ausgabe, sagt die Kategorie (<see cref="Budget.IsIncome"/>).
    /// </summary>
    public sealed partial class BudgetPeriod
    {
        readonly double[] amounts = new double[Budget.Count];
        readonly Dictionary<string, double> services = new Dictionary<string, double>();
        readonly List<string> serviceOrder = new List<string>();

        /// <summary>Nummer der Woche (bzw. des Tages bei Tagesabrechnung) seit Spielbeginn, ab 0.</summary>
        public int Index { get; internal set; }
        /// <summary>Erster Spieltag des Zeitraums.</summary>
        public int FirstDay { get; internal set; }
        /// <summary>Abgerechnete Tage (Woche: 0..7; noch laufende Woche &lt; 7).</summary>
        public int Days { get; internal set; }
        public double OpeningBalance { get; internal set; }
        public double ClosingBalance { get; internal set; }

        public double Amount(BudgetCategory c) => amounts[(int)c];

        public double Income
        {
            get { double s = 0; for (int i = 0; i < Budget.Count; i++) if (Budget.IsIncome((BudgetCategory)i)) s += amounts[i]; return s; }
        }

        public double Expense
        {
            get { double s = 0; for (int i = 0; i < Budget.Count; i++) if (!Budget.IsIncome((BudgetCategory)i)) s += amounts[i]; return s; }
        }

        /// <summary>Saldo = Einnahmen − Ausgaben.</summary>
        public double Net => Income - Expense;

        /// <summary>Dienste-Unterhalt je Kostenträgername (Reihenfolge der ersten Buchung, deterministisch).</summary>
        public IEnumerable<KeyValuePair<string, double>> ServiceUpkeep
        {
            get { foreach (var n in serviceOrder) yield return new KeyValuePair<string, double>(n, services[n]); }
        }

        internal void Add(BudgetCategory c, double amount) => amounts[(int)c] += amount;

        internal void AddService(string name, double amount)
        {
            name ??= "Sonstige";
            if (!services.ContainsKey(name)) { services[name] = 0; serviceOrder.Add(name); }
            services[name] += amount;
            amounts[(int)BudgetCategory.Services] += amount;
        }

        internal void AddFrom(BudgetPeriod other)
        {
            for (int i = 0; i < Budget.Count; i++)
                if (i != (int)BudgetCategory.Services) amounts[i] += other.amounts[i];
            foreach (var kv in other.ServiceUpkeep) AddService(kv.Key, kv.Value);
        }
    }
}
