using System;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Spielstand der Lenkung (Abschnitt „policy“): Schwerpunkt-Raster (Werte über eine Namenstabelle, Enum darf wachsen),
    /// Förderstufen je Branche (über den Namen), Tag des letzten Betriebswechsels je Gebäude, Zufall, Zähler.
    /// Fehlt der Abschnitt (alte Stände), gilt der Standard: überall automatisch, keine Förderung.
    /// </summary>
    public sealed partial class ProductionPolicy : ISaveable
    {
        public string SaveId => "policy";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            rng.WriteState(w);
            w.Int(Version);
            w.Int(SwitchesYesterday); w.Int(SwitchesTotal); w.Int(BlockedYesterday);
            w.Int(IndustryBranches.Count);
            for (int i = 0; i < IndustryBranches.Count; i++)
            {
                w.Name((IndustryFocus)i);
                w.Byte(levels[i]);
            }
            w.Int(resolution);
            w.Float(cell);
            w.Bytes(map);
            w.IntDictionary(lastSwitch, d => w.Int(d));
        }

        public void Read(SaveReader r, int version)
        {
            rng.ReadState(r);
            Version = r.Int();
            SwitchesYesterday = r.Int(); SwitchesTotal = r.Int(); BlockedYesterday = r.Int();
            int n = r.Count(2);
            var remap = new byte[Math.Max(1, n)];
            Array.Clear(levels, 0, levels.Length);
            for (int i = 0; i < n; i++)
            {
                bool known = r.TryName<IndustryFocus>(out var f);
                byte level = r.Byte();
                remap[i] = known ? (byte)f : (byte)IndustryFocus.Auto;
                if (known && f != IndustryFocus.Auto) levels[(int)f] = (byte)Math.Min((int)level, PolicyRules.MaxLevel);
            }
            int res = r.Int();
            float savedCell = r.Float();
            var data = r.Bytes() ?? new byte[0];
            if (res != resolution || savedCell != cell || data.Length != map.Length)
                throw new SaveFormatException($"Schwerpunkt-Raster passt nicht zur Karte ({res}×{res} statt {resolution}×{resolution}).");
            Array.Clear(cellCounts, 0, cellCounts.Length);
            for (int i = 0; i < map.Length; i++)
            {
                byte v = data[i] < n ? remap[data[i]] : (byte)IndustryFocus.Auto;
                map[i] = v;
                cellCounts[v]++;
            }
            lastSwitch.Clear();
            r.IntDictionaryInto(lastSwitch, () => r.Int(), 4);
        }
    }
}
