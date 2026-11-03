using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>
    /// Spielstand der Sicherheit (Abschnitt „safety“ v1): Schalter, Zähler, laufende Brände, Verlauf, Sanierungen/Ruinen
    /// (inkl. <c>Building.OutOfService</c>, das der Stadt-Abschnitt nicht kennt), Kriminalitätsraten der letzten Stunde,
    /// Streifenpräsenz, offene Polizeieinsätze, Zellen der Wachen, HUD-Ereignisse, Tageszähler.
    /// <para>Fehlt der Abschnitt (Stände vor dieser Runde) → aus, keine Brände/Ruinen; das Spiel schaltet nach dem Laden ein.</para>
    /// </summary>
    public sealed partial class SafetySystem : ISaveable
    {
        public string SaveId => "safety";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Bool(enabled);
            w.Float(IgnitionScale); w.Float(CrimeScale);
            w.Int(nextFireId); w.Int(nextCrimeId); w.Int(nextEventSerial); w.Int(statsDay);
            Today.Write(w); Yesterday.Write(w); Total.Write(w);

            w.Int(fires.Count);
            foreach (var f in fires)
            {
                w.Int(f.Id); w.Int(f.BuildingId); w.Vector2(f.Position); w.Long(f.StartTick); w.Long(f.ReportTick);
                w.Float(f.Intensity); w.Float(f.PeakIntensity); w.Float(f.Damage); w.Long(f.FirstArrivalTick); w.Long(f.FallbackArrivalTick);
                w.Int(f.TrucksOnSite); w.Int(f.TrucksEnRoute); w.Int(f.TrucksSent); w.Int(f.MissedDispatches); w.Float(f.Water);
                w.Int(f.SourceFireId); w.Int(f.Injured); w.Bool(f.Headlined); w.Bool(f.SecondInjuryRoll);
                w.Double(f.ReportTime); w.Double(f.FirstArrivalTime);
            }
            w.Int(fireHistory.Count);
            foreach (var r in fireHistory)
            {
                w.Int(r.FireId); w.Int(r.BuildingId); w.Int(r.Street); w.Vector2(r.Position); w.Long(r.StartTick); w.Long(r.EndTick);
                w.Float(r.Damage); w.Float(r.PeakIntensity); w.Name(r.Outcome); w.Float(r.ResponseMinutes); w.Int(r.Injured); w.Bool(r.Spread);
                w.String(r.BuildingName ?? "");
            }
            w.Int(repairs.Count);
            foreach (var r in repairs.Values)
            {
                w.Int(r.BuildingId); w.Long(r.UntilTick); w.Bool(r.Ruin);
                w.IntList(r.Households);
            }
            w.Int(crimes.Count);
            foreach (var c in crimes) WriteCrime(w, c);
            w.Int(openCrimes.Count);
            foreach (var c in openCrimes) w.Int(c.Id);
            w.Int(crimeRates.Count);
            foreach (var kv in crimeRates)
            {
                var f = kv.Value;
                w.Int(kv.Key);
                w.Float(f.Unemployment); w.Float(f.Dissatisfaction); w.Float(f.LowEducation); w.Float(f.Density); w.Float(f.LowLandValue);
                w.Float(f.Shops); w.Float(f.Police); w.Float(f.Patrol); w.Float(f.Pressure); w.Float(f.Rate);
            }
            w.Floats(patrol);
            w.Int(custody.Count);
            foreach (var kv in custody) { w.Int(kv.Key); w.Int(kv.Value); }
            w.Int(events.Count);
            foreach (var e in events)
            {
                w.Int(e.Serial); w.Long(e.Tick); w.Bool(e.Warning); w.String(e.Title ?? ""); w.String(e.Text ?? ""); w.Int(e.BuildingId); w.String(e.Key ?? "");
            }
        }

        static void WriteCrime(SaveWriter w, CrimeIncident c)
        {
            w.Int(c.Id); w.Int(c.BuildingId); w.Int(c.Street); w.Name(c.Kind); w.Long(c.Tick); w.Vector2(c.Position); w.Float(c.Loss);
            w.Bool(c.Reported); w.Int(c.UnitId); w.Long(c.FallbackArrivalTick); w.Long(c.ArrivalTick); w.Bool(c.Closed); w.Bool(c.Solved);
            w.Double(c.Time); w.Double(c.ArrivalTime);
        }

        static CrimeIncident ReadCrime(SaveReader r) => new CrimeIncident
        {
            Id = r.Int(), BuildingId = r.Int(), Street = r.Int(), Kind = r.Name<CrimeKind>(), Tick = r.Long(), Position = r.Vector2(), Loss = r.Float(),
            Reported = r.Bool(), UnitId = r.Int(), FallbackArrivalTick = r.Long(), ArrivalTick = r.Long(), Closed = r.Bool(), Solved = r.Bool(),
            Time = r.Double(), ArrivalTime = r.Double(),
        };

        public void Read(SaveReader r, int version)
        {
            enabled = r.Bool();
            IgnitionScale = r.Float(); CrimeScale = r.Float();
            nextFireId = r.Int(); nextCrimeId = r.Int(); nextEventSerial = r.Int(); statsDay = r.Int();
            Today.Read(r); Yesterday.Read(r); Total.Read(r);

            fires.Clear();
            fireByBuilding.Clear();
            int n = r.Count(40);
            for (int i = 0; i < n; i++)
            {
                var f = new Fire
                {
                    Id = r.Int(), BuildingId = r.Int(), Position = r.Vector2(), StartTick = r.Long(), ReportTick = r.Long(),
                    Intensity = r.Float(), PeakIntensity = r.Float(), Damage = r.Float(), FirstArrivalTick = r.Long(), FallbackArrivalTick = r.Long(),
                    TrucksOnSite = r.Int(), TrucksEnRoute = r.Int(), TrucksSent = r.Int(), MissedDispatches = r.Int(), Water = r.Float(),
                    SourceFireId = r.Int(), Injured = r.Int(),
                };
                f.Headlined = r.Bool();
                f.SecondInjuryRoll = r.Bool();
                f.ReportTime = r.Double();
                f.FirstArrivalTime = r.Double();
                fires.Add(f);
                fireByBuilding[f.BuildingId] = f;
            }
            fireHistory.Clear();
            n = r.Count(40);
            for (int i = 0; i < n; i++)
            {
                fireHistory.Add(new FireRecord
                {
                    FireId = r.Int(), BuildingId = r.Int(), Street = r.Int(), Position = r.Vector2(), StartTick = r.Long(), EndTick = r.Long(),
                    Damage = r.Float(), PeakIntensity = r.Float(), Outcome = r.Name<FireOutcome>(), ResponseMinutes = r.Float(), Injured = r.Int(),
                    Spread = r.Bool(), BuildingName = r.String(),
                });
            }
            repairs.Clear();
            n = r.Count(13);
            for (int i = 0; i < n; i++)
            {
                var rep = new FireRepair { BuildingId = r.Int(), UntilTick = r.Long(), Ruin = r.Bool() };
                int h = r.Count(4);
                for (int k = 0; k < h; k++) rep.Households.Add(r.Int());
                repairs[rep.BuildingId] = rep;
                var b = city.GetBuilding(rep.BuildingId);
                if (b != null) b.OutOfService = true; // Stadt-Abschnitt kennt das Flag nicht
            }
            crimes.Clear();
            openCrimes.Clear();
            n = r.Count(40);
            for (int i = 0; i < n; i++) crimes.Add(ReadCrime(r));
            n = r.Count(4);
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                foreach (var c in crimes) if (c.Id == id) { openCrimes.Add(c); break; }
            }
            crimeRates.Clear();
            n = r.Count(44);
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                crimeRates[id] = new CrimeFactors
                {
                    Unemployment = r.Float(), Dissatisfaction = r.Float(), LowEducation = r.Float(), Density = r.Float(), LowLandValue = r.Float(),
                    Shops = r.Float(), Police = r.Float(), Patrol = r.Float(), Pressure = r.Float(), Rate = r.Float(),
                };
            }
            var p = r.Floats();
            System.Array.Clear(patrol, 0, patrol.Length);
            System.Array.Copy(p, patrol, System.Math.Min(p.Length, patrol.Length));
            custody.Clear();
            n = r.Count(8);
            for (int i = 0; i < n; i++) { int id = r.Int(); custody[id] = r.Int(); }
            events.Clear();
            n = r.Count(20);
            for (int i = 0; i < n; i++)
                events.Add(new SafetyEvent
                {
                    Serial = r.Int(), Tick = r.Long(), Warning = r.Bool(), Title = r.String(), Text = r.String(), BuildingId = r.Int(), Key = r.String(),
                });
            sortedCityVersion = -1;
            Version++;
            ApplyHooks();
        }
    }
}
