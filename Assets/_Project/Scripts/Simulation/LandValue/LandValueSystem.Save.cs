using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.LandValue
{
    /// <summary>
    /// Spielstand des Bodenwerts: Werte je Zelle (träge geglättet, nicht aus dem Rest rekonstruierbar), Fortschritt der
    /// gestaffelten Runde, Hilfsraster (die Gebäude-Raster sind eine Momentaufnahme vom Rundenbeginn), Stufen-Fortschritt
    /// je Gebäude und Zähler. Die Gelände-Caches (Wasser/Ufer/Aussicht) werden neu berechnet.
    /// </summary>
    public sealed partial class LandValueSystem : ISaveable
    {
        public string SaveId => "landvalue";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(cursor);
            w.Int(builtRoadVersion);
            w.Int(Cycles);
            w.Bool(DemolishDecayed);
            w.Int(Upgrades); w.Int(Downgrades); w.Int(DecayedBuildings);
            Layer.WriteState(w);
            w.Floats(known);
            w.Floats(roadDist);
            foreach (var g in new[] { highway, jobs, units, satSum, satWeight, garbage }) w.Floats(g.Values);
            w.Int(externals.Count);
            foreach (var e in externals) w.Vector2(e);
            w.IntDictionary(states, s => { w.Int(s.Progress); w.Int(s.Decline); w.Int(s.EmptyDays); });
        }

        public void Read(SaveReader r, int version)
        {
            cursor = r.Int();
            builtRoadVersion = r.Int();
            Cycles = r.Int();
            DemolishDecayed = r.Bool();
            Upgrades = r.Int(); Downgrades = r.Int(); DecayedBuildings = r.Int();
            Layer.ReadState(r);
            r.FloatsInto(known);
            r.FloatsInto(roadDist);
            foreach (var g in new[] { highway, jobs, units, satSum, satWeight, garbage }) r.FloatsInto(g.Values);
            int n = r.Count(8);
            externals.Clear();
            for (int i = 0; i < n; i++) externals.Add(r.Vector2());
            if (cursor < 0 || cursor > known.Length) throw new SaveFormatException("Bodenwert-Runde ungültig.");
            Array.Clear(terrainKnown, 0, terrainKnown.Length); // Gelände-Caches neu
            states.Clear();
            int s = r.Count(16);
            for (int i = 0; i < s; i++)
            {
                int id = r.Int();
                states[id] = new LevelState { Progress = r.Int(), Decline = r.Int(), EmptyDays = r.Int() };
            }
        }

        sealed partial class CoarseGrid
        {
            /// <summary>Rohwerte (Spielstand).</summary>
            internal float[] Values => v;
        }
    }
}
