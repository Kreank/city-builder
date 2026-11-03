using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Economy
{
    /// <summary>Spielstand der Stadtkasse: Kontostand, Sätze, Überziehung/Baustopp, laufende Woche, letzter Tag, Wochen-Historie.</summary>
    public sealed partial class EconomySystem : ISaveable
    {
        public string SaveId => "economy";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Double(Balance);
            w.Bool(UnlimitedFunds);
            w.Int(DaysInDebt);
            w.Bool(ConstructionFrozen);
            w.Int(billedDays);
            w.Int(Version);
            w.Int(taxRates.Length);
            foreach (var t in taxRates) w.Float(t);
            CurrentWeek.WriteState(w);
            w.Bool(LastDay != null);
            LastDay?.WriteState(w);
            w.Int(history.Count);
            foreach (var p in history) p.WriteState(w);
        }

        public void Read(SaveReader r, int version)
        {
            Balance = r.Double();
            UnlimitedFunds = r.Bool();
            DaysInDebt = r.Int();
            ConstructionFrozen = r.Bool();
            billedDays = r.Int();
            Version = r.Int();
            int n = r.Count(4);
            for (int i = 0; i < n; i++)
            {
                float t = r.Float();
                if (i < taxRates.Length) taxRates[i] = t;
            }
            CurrentWeek = BudgetPeriod.ReadState(r);
            LastDay = r.Bool() ? BudgetPeriod.ReadState(r) : null;
            history.Clear();
            int h = r.Count(24);
            for (int i = 0; i < h; i++) history.Add(BudgetPeriod.ReadState(r));
        }
    }

    public sealed partial class BudgetPeriod
    {
        internal void WriteState(SaveWriter w)
        {
            w.Int(Index);
            w.Int(FirstDay);
            w.Int(Days);
            w.Double(OpeningBalance);
            w.Double(ClosingBalance);
            w.Doubles(amounts);
            w.Int(serviceOrder.Count);
            foreach (var name in serviceOrder) { w.String(name); w.Double(services[name]); }
        }

        internal static BudgetPeriod ReadState(SaveReader r)
        {
            var p = new BudgetPeriod
            {
                Index = r.Int(), FirstDay = r.Int(), Days = r.Int(), OpeningBalance = r.Double(), ClosingBalance = r.Double(),
            };
            var a = r.Doubles() ?? new double[0];
            for (int i = 0; i < a.Length && i < p.amounts.Length; i++) p.amounts[i] = a[i]; // neue Kategorien → 0
            int n = r.Count(9);
            for (int i = 0; i < n; i++)
            {
                string name = r.String() ?? "Sonstige";
                double v = r.Double();
                if (!p.services.ContainsKey(name)) p.serviceOrder.Add(name);
                p.services[name] = v;
            }
            return p;
        }
    }
}
