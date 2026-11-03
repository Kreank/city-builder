using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Spielstand der Länder-Volkswirtschaften (LOD „aggregiert“). Der Planet selbst (Relief, Klima, Regionen, Länder,
    /// Profile) entsteht beim Laden deterministisch aus Seed und Einstellungen neu – nur die veränderlichen Teile stehen hier.
    /// </summary>
    public sealed partial class WorldEconomy : ISaveable
    {
        public string SaveId => "planet";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(DaysSimulated);
            w.Int(nations.Count);
            foreach (var n in nations) n.WriteState(w);
        }

        public void Read(SaveReader r, int version)
        {
            DaysSimulated = r.Int();
            int count = r.Count(8);
            if (count != nations.Count) throw new SaveFormatException($"{count} Länder gespeichert, die Welt hat {nations.Count} (anderer Seed?).");
            foreach (var n in nations) n.ReadState(r);
        }
    }

    public sealed partial class NationEconomy
    {
        internal void WriteState(SaveWriter w)
        {
            w.Int(Country.Id);
            w.Double(Population);
            w.Doubles(Production);
            w.Doubles(Demand);
            w.Doubles(Consumption);
            w.Doubles(Stock);
            w.Doubles(Price);
            w.Doubles(Overflow);
            w.Doubles(Satisfaction);
            w.Double(Gdp);
            w.Int(LastDay);
        }

        internal void ReadState(SaveReader r)
        {
            int id = r.Int();
            if (id != Country.Id) throw new SaveFormatException($"Land {id} erwartet {Country.Id}.");
            Population = r.Double();
            r.DoublesInto(Production);
            r.DoublesInto(Demand);
            r.DoublesInto(Consumption);
            r.DoublesInto(Stock);
            r.DoublesInto(Price);
            r.DoublesInto(Overflow);
            r.DoublesInto(Satisfaction);
            Gdp = r.Double();
            LastDay = r.Int();
        }
    }
}
