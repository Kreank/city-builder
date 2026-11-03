using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>
    /// Spielstand der Stadt: gemalte Zonen (<see cref="ZoneMap"/>), Zonenraster (mit den beim letzten Aufbau
    /// gelesenen Geländehöhen – das Raster wird nur bei Netzänderungen neu gebaut, ein Neuaufbau beim Laden würde
    /// zwischenzeitliche Geländeänderungen vorwegnehmen), Gebäude (Stufe, Zustand, Bau, Belegung, Zufahrt),
    /// Nachfrage, Bürgerdaten für die Nachfrage, Statistik, Zufall und Id-Zähler. Räumlicher Index neu aufgebaut.
    /// </summary>
    public sealed partial class CitySystem : ISaveable
    {
        public string SaveId => "city";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            rng.Write(w);
            w.Int(nextId);
            w.Int(Version);
            w.Float(moveInPull);
            w.Ints(freeCells);
            for (int z = 0; z < 8; z++) w.Float(Demand[(ZoneType)z]);
            var s = Stats;
            w.Int(s.Buildings); w.Int(s.UnderConstruction); w.Int(s.Households); w.Int(s.OccupiedHouseholds); w.Int(s.Population);
            w.Int(s.Jobs); w.Int(s.Workers); w.Int(s.ZonedCells); w.Int(s.FreeZonedCells);
            var p = Population;
            w.Int(p.Residents); w.Int(p.WorkingAge); w.Int(p.Unemployed); w.Int(p.UnemployedEducated); w.Int(p.CommutersOut);
            w.Int(p.CommutersIn); w.Int(p.VacantJobs); w.Int(p.FreeHomes); w.Int(p.UnservedShoppers); w.Float(p.AverageSatisfaction);

            ZoneMap.WriteState(w);
            Grid.WriteState(w);

            buildings.WriteLayout(w, (id, b) =>
            {
                w.Int(b.Id);
                w.Byte((byte)b.Zone);
                w.Int(b.WidthCells);
                w.Int(b.DepthCells);
                w.Vector2(b.Position);
                w.Vector2(b.Front);
                w.Float(b.Height);
                w.Int(b.FrontSegmentId);
                w.Float(b.FrontT);
                w.Int(b.Level);
                w.Int(b.Variant);
                w.Int(b.Households);
                w.Int(b.Jobs);
                w.Int(b.OccupiedHouseholds);
                w.Int(b.Residents);
                w.Int(b.Workers);
                w.Float(b.Construction);
                w.Float(b.Condition);
            });
        }

        public void Read(SaveReader r, int version)
        {
            rng.Read(r);
            nextId = r.Int();
            Version = r.Int();
            moveInPull = r.Float();
            r.IntsInto(freeCells);
            for (int z = 0; z < 8; z++) Demand[(ZoneType)z] = r.Float();
            var s = Stats;
            s.Buildings = r.Int(); s.UnderConstruction = r.Int(); s.Households = r.Int(); s.OccupiedHouseholds = r.Int(); s.Population = r.Int();
            s.Jobs = r.Int(); s.Workers = r.Int(); s.ZonedCells = r.Int(); s.FreeZonedCells = r.Int();
            var p = Population;
            p.Residents = r.Int(); p.WorkingAge = r.Int(); p.Unemployed = r.Int(); p.UnemployedEducated = r.Int(); p.CommutersOut = r.Int();
            p.CommutersIn = r.Int(); p.VacantJobs = r.Int(); p.FreeHomes = r.Int(); p.UnservedShoppers = r.Int(); p.AverageSatisfaction = r.Float();

            ZoneMap.ReadState(r);
            Grid.ReadState(r);

            buildings.ReadLayout(r, () =>
            {
                var b = new Building
                {
                    Id = r.Int(),
                    Zone = (ZoneType)r.Byte(),
                    WidthCells = r.Int(),
                    DepthCells = r.Int(),
                    Position = r.Vector2(),
                    Front = r.Vector2(),
                    Height = r.Float(),
                    FrontSegmentId = r.Int(),
                    FrontT = r.Float(),
                    Level = r.Int(),
                    Variant = r.Int(),
                    Households = r.Int(),
                    Jobs = r.Int(),
                    OccupiedHouseholds = r.Int(),
                    Residents = r.Int(),
                    Workers = r.Int(),
                    Construction = r.Float(),
                    Condition = r.Float(),
                };
                if (b.WidthCells < 1 || b.DepthCells < 1 || b.WidthCells > 64 || b.DepthCells > 64) throw new SaveFormatException($"Gebäude {b.Id}: Grundstück ungültig.");
                return new KeyValuePair<int, Building>(b.Id, b);
            });

            // Räumlicher Index: Listen je Feld in Bau-Reihenfolge (= Id-Reihenfolge) wie beim Wachsen
            buildingBuckets.Clear();
            var sorted = new List<Building>(buildings.Values);
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var b in sorted)
                foreach (var key in BucketKeys(b))
                {
                    if (!buildingBuckets.TryGetValue(key, out var list)) buildingBuckets[key] = list = new List<Building>();
                    list.Add(b);
                }
        }
    }

    public sealed partial class ZoneMap
    {
        internal void WriteState(SaveWriter w)
        {
            w.Int(resolution);
            w.Int(Version);
            w.Bytes(data);
        }

        internal void ReadState(SaveReader r)
        {
            int res = r.Int();
            if (res != resolution) throw new SaveFormatException($"Zonenkarte {res} passt nicht zur Welt ({resolution}).");
            Version = r.Int();
            r.BytesInto(data);
        }
    }

    public sealed partial class ZoneGrid
    {
        internal void WriteState(SaveWriter w)
        {
            w.Int(builtVersion);
            w.Int(Version);
            w.Int(blocks.Count);
            foreach (var b in blocks)
            {
                w.Int(b.Segment.Id);
                w.Bool(b.RightSide);
                w.Int(b.Columns);
                foreach (var c in b.Cells)
                {
                    w.Vector2(c.Center);
                    w.Vector2(c.Along);
                    w.Vector2(c.Out);
                    w.Float(c.Height);
                    w.Bool(c.Valid);
                }
            }
        }

        internal void ReadState(SaveReader r)
        {
            builtVersion = r.Int();
            Version = r.Int();
            blocks.Clear();
            buckets.Clear();
            int n = r.Count(9);
            for (int i = 0; i < n; i++)
            {
                int segId = r.Int();
                var seg = network.GetSegment(segId) ?? throw new SaveFormatException($"Zonenraster: Segment {segId} fehlt.");
                bool right = r.Bool();
                int columns = r.Count(0);
                if (columns > 100000) throw new SaveFormatException("Zonenraster: zu viele Spalten.");
                var block = new ZoneBlock(seg, right, columns);
                for (int k = 0; k < block.Cells.Length; k++)
                {
                    ref var c = ref block.Cells[k];
                    c.Center = r.Vector2();
                    c.Along = r.Vector2();
                    c.Out = r.Vector2();
                    c.Height = r.Float();
                    c.Valid = r.Bool();
                }
                blocks.Add(block);
            }
            // Index in derselben Reihenfolge wie beim Aufbau (Block → Spalte → Reihe)
            foreach (var block in blocks)
                for (int c = 0; c < block.Columns; c++)
                for (int row = 0; row < Depth; row++)
                    if (block[c, row].Valid) AddToBucket(block, c, row, block[c, row].Center);
        }
    }
}
