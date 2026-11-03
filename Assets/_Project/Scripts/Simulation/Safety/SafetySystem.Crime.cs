using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>Kriminalität: Rate je Gebäude/Viertel, Straftaten, Anzeige, Polizeieinsatz, Aufklärung, Festnahmen, Streifen.</summary>
    public sealed partial class SafetySystem
    {
        // ------------------------------------------------------------ Streifenpräsenz (Raster)

        int Cell(Vector2 p)
        {
            int x = Math.Clamp((int)((p.X - gridMin.X) / SafetyRules.CrimeCell), 0, gridW - 1);
            int y = Math.Clamp((int)((p.Y - gridMin.Y) / SafetyRules.CrimeCell), 0, gridW - 1);
            return y * gridW + x;
        }

        void AddPatrol(Vector2 p, float amount)
        {
            int c = Cell(p), cx = c % gridW, cy = c / gridW;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= gridW || y >= gridW) continue;
                    patrol[y * gridW + x] += dx == 0 && dy == 0 ? amount : amount * SafetyRules.PatrolNeighborShare;
                }
        }

        void DecayPatrol()
        {
            for (int i = 0; i < patrol.Length; i++)
            {
                if (patrol[i] <= 0f) continue;
                patrol[i] *= SafetyRules.PatrolDecayPerHour;
                if (patrol[i] < 0.01f) patrol[i] = 0f;
            }
        }

        /// <summary>Streifenpräsenz am Punkt 0..1 (1 − e^(−Halte/2), klingt stündlich ab).</summary>
        public float PatrolPresence(Vector2 p) => 1f - MathF.Exp(-patrol[Cell(p)] / SafetyRules.PatrolScale);

        float PatrolWeight(Building b) => 4f * CrimeRate(b);

        // ------------------------------------------------------------ Rate je Gebäude

        struct CellAgg
        {
            public float WorkingAge, Unemployed, Adults, LowEdu, SatSum, Residents, Units;
        }

        void RecomputeCrime()
        {
            var agg = new CellAgg[patrol.Length];
            var list = SortedBuildings();
            float cityWorking = 0f, cityUnemployed = 0f;
            foreach (var b in list)
            {
                if (!b.IsConstructed) continue;
                int c = Cell(b.Position);
                agg[c].Units += b.Households + b.Jobs;
                if (citizens == null || b.Households <= 0) continue;
                foreach (var m in citizens.ResidentsOf(b.Id))
                {
                    agg[c].Residents++;
                    agg[c].SatSum += m.Satisfaction;
                    if (m.Age >= Citizen.AdultAge)
                    {
                        agg[c].Adults++;
                        if (m.Education <= Education.Basic) agg[c].LowEdu++;
                    }
                    if (m.IsWorkingAge)
                    {
                        agg[c].WorkingAge++;
                        cityWorking++;
                        if (!m.IsEmployed) { agg[c].Unemployed++; cityUnemployed++; }
                    }
                }
            }
            float cityRate = cityWorking > 0f ? cityUnemployed / cityWorking : 0f;
            var lv = world.LandValue;
            crimeRates.Clear();
            foreach (var b in list)
            {
                if (!b.IsConstructed || b.OutOfService) continue;
                int c = Cell(b.Position), cx = c % gridW, cy = c / gridW;
                var s = new CellAgg();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= gridW || y >= gridW) continue;
                        var a = agg[y * gridW + x];
                        float w = dx == 0 && dy == 0 ? 1f : 0.5f;
                        s.WorkingAge += a.WorkingAge * w; s.Unemployed += a.Unemployed * w; s.Adults += a.Adults * w; s.LowEdu += a.LowEdu * w;
                        s.SatSum += a.SatSum * w; s.Residents += a.Residents * w; s.Units += a.Units * w;
                    }
                var f = new CrimeFactors();
                float unemp = s.WorkingAge >= 5f ? s.Unemployed / s.WorkingAge : cityRate;
                f.Unemployment = Math.Clamp(unemp / SafetyRules.UnemploymentFull, 0f, 1f);
                float sat = s.Residents >= 3f ? s.SatSum / s.Residents : 0.6f;
                f.Dissatisfaction = Math.Clamp((SafetyRules.SatisfactionNeutral - sat) / 0.5f, 0f, 1f);
                float low = s.Adults >= 3f ? s.LowEdu / s.Adults : 0.45f;
                f.LowEducation = Math.Clamp((low - SafetyRules.LowEducationNeutral) / 0.5f, 0f, 1f);
                f.Density = Math.Clamp(s.Units / SafetyRules.DensityFull, 0f, 1f);
                float value = lv != null ? lv.ValueOf(b) : -1f;
                f.LowLandValue = value < 0f ? 0.6f : Math.Clamp(1f - value / 0.6f, 0f, 1f);
                f.Shops = b.Zone == ZoneType.Retail || b.Zone == ZoneType.Mixed ? 1f : 0f;
                f.Police = services.Coverage(ServiceKinds.Police, b);
                f.Patrol = PatrolPresence(b.Position);
                f.Rate = SafetyRules.CrimeRate(ref f);
                crimeRates[b.Id] = f;
            }
            Version++;
        }

        /// <summary>Mittlere Kriminalitätsrate (gewichtet nach Einwohnern + Arbeitsplätzen).</summary>
        public float AverageCrimeRate
        {
            get
            {
                double sum = 0, w = 0;
                foreach (var kv in crimeRates)
                {
                    var b = city.GetBuilding(kv.Key);
                    if (b == null) continue;
                    float weight = 1f + b.Residents + b.Workers;
                    sum += kv.Value.Rate * weight;
                    w += weight;
                }
                return w > 0 ? (float)(sum / w) : 0f;
            }
        }

        // ------------------------------------------------------------ Straftaten

        void RollCrimes(long now)
        {
            int hour = (int)(now / TimeModel.TicksPerHour);
            float night = SafetyRules.NightWeight(clock.Hour);
            foreach (var kv in crimeRates)
            {
                var b = city.GetBuilding(kv.Key);
                if (b == null || !b.IsConstructed || b.OutOfService) continue;
                if (b.Residents + b.Workers <= 0 && b.Households + b.Jobs <= 0) continue;
                double p = kv.Value.Rate * SafetyRules.CrimesPerDay * SafetyRules.CrimeSize(b) * night * CrimeScale / 24.0;
                if (SafetyRules.Roll(b.Id, hour, 5) >= p) continue;
                Commit(b, now, hour);
            }
        }

        /// <summary>Straftat an einem Gebäude (Würfel, Tests). Art nach Bauart; Schaden, Erinnerungen, Anzeige, Polizeieinsatz.</summary>
        public CrimeIncident Commit(Building b, long now, int salt, CrimeKind? forced = null)
        {
            if (b == null) return null;
            double r = SafetyRules.Roll(b.Id, salt, 7);
            CrimeKind kind;
            if (forced.HasValue) kind = forced.Value;
            else if (b.Zone == ZoneType.Retail || b.Zone == ZoneType.Mixed)
                kind = r < 0.6 ? CrimeKind.Shoplifting : r < 0.8 ? CrimeKind.Burglary : CrimeKind.Vandalism;
            else if (b.Zone == ZoneType.Office || b.Zone == ZoneType.Industry)
                kind = r < 0.55 ? CrimeKind.Vandalism : CrimeKind.Burglary;
            else kind = r < 0.7 ? CrimeKind.Burglary : CrimeKind.Vandalism;

            double size = SafetyRules.Roll(b.Id, salt, 9);
            var firm = world.Goods?.FirmAt(b.Id);
            float loss;
            switch (kind)
            {
                case CrimeKind.Shoplifting: loss = 80f + (float)size * 520f; break;
                case CrimeKind.Burglary: loss = firm != null ? 500f + (float)size * 3500f : 400f + (float)size * 2600f; break;
                default: loss = 150f + (float)size * 1050f; break;
            }
            var c = new CrimeIncident
            {
                Id = nextCrimeId++, BuildingId = b.Id, Kind = kind, Tick = now, Position = b.Position, Street = b.FrontSegmentId, Loss = loss,
                Time = world.Traffic.Time,
            };

            // Schaden: Firmenkasse/Lager bzw. Zustand; Haushalte haben noch kein Vermögen → Schaden nur als Zahl + Erinnerung
            if (firm != null) firm.Cash -= loss;
            if (kind == CrimeKind.Vandalism) b.Condition = MathF.Max(SafetyRules.MinCondition, b.Condition - 0.015f - 0.025f * (float)size);
            if (kind == CrimeKind.Shoplifting && firm != null)
                for (int g = 0; g < firm.OutputStock.Length; g++) firm.OutputStock[g] *= 0.995f;

            // Erinnerungen: Opfer (ein Haushalt bzw. Beschäftigte) und Mitbewohner
            if (citizens != null)
            {
                var households = citizens.HouseholdsIn(b.Id);
                if (households.Count > 0)
                {
                    var order = new List<Household>(households);
                    order.Sort((x, y) => x.Id.CompareTo(y.Id));
                    var victim = order[(int)(SafetyRules.Roll(b.Id, salt, 13) * order.Count) % order.Count];
                    foreach (var h in order)
                        foreach (var m in h.Members)
                            citizens.RememberEvent(m, h == victim && kind == CrimeKind.Burglary ? MemoryKind.Burglary : MemoryKind.FeelsUnsafe,
                                h == victim ? 0.8f : 0.3f, b);
                }
                else
                {
                    var staff = new List<Citizen>(citizens.WorkersAt(b.Id));
                    staff.Sort((x, y) => x.Id.CompareTo(y.Id));
                    for (int i = 0; i < staff.Count && i < 12; i++)
                        citizens.RememberEvent(staff[i], kind == CrimeKind.Burglary ? MemoryKind.Burglary : MemoryKind.FeelsUnsafe, 0.4f, b);
                }
            }

            Today.Crimes++;
            Today.CrimeLoss += loss;
            switch (kind)
            {
                case CrimeKind.Burglary: Today.Burglaries++; break;
                case CrimeKind.Shoplifting: Today.Shopliftings++; break;
                default: Today.Vandalism++; break;
            }

            // Anzeige → Polizeieinsatz (echter Streifenwagen; ohne Dienstfahrzeuge geschätzte Ankunft)
            c.Reported = SafetyRules.Roll(b.Id, salt, 19) < SafetyRules.ReportChance(kind);
            if (c.Reported)
            {
                Today.Reported++;
                if (vehicles != null && vehicles.Enabled)
                {
                    var u = vehicles.Dispatch(ServiceMission.CrimeCall, b, now);
                    if (u != null) { c.UnitId = u.Id; Today.PoliceCalls++; openCrimes.Add(c); }
                    else { Today.MissedPoliceDispatches++; Resolve(c, b, onScene: false); }
                }
                else
                {
                    float best = float.PositiveInfinity;
                    foreach (var st in services.Facilities)
                    {
                        if (st.Kind != ServiceKinds.Police) continue;
                        float secs = services.ResponseSeconds(st, b);
                        if (secs <= st.Type.RangeSeconds * 1.5f && secs < best) best = secs;
                    }
                    if (float.IsInfinity(best)) Resolve(c, b, onScene: false);
                    else { c.FallbackArrivalTick = now + 1 + TimeModel.TicksFor(best); Today.PoliceCalls++; openCrimes.Add(c); }
                }
            }
            else c.Closed = true;

            crimes.Insert(0, c);
            if (crimes.Count > MaxCrimes) crimes.RemoveAt(crimes.Count - 1);
            Version++;
            return c;
        }

        void UpdateCrimeCalls(long now)
        {
            if (openCrimes.Count == 0) return;
            foreach (var c in openCrimes.ToArray())
            {
                if (c.Closed) continue;
                var b = city.GetBuilding(c.BuildingId);
                if (c.FallbackArrivalTick >= 0)
                {
                    if (now < c.FallbackArrivalTick) continue;
                    c.ArrivalTick = c.FallbackArrivalTick;
                    c.ArrivalTime = c.Time + Core.TimeModel.TrafficSeconds(c.FallbackArrivalTick - c.Tick);
                    Resolve(c, b, onScene: true);
                    continue;
                }
                // Streifenwagen hat abgebrochen (Stau, kein Weg) bzw. ist nicht mehr auf diesem Einsatz → spätere Ermittlung
                bool active = false;
                if (vehicles != null)
                    foreach (var u in vehicles.Units)
                        if (u.Id == c.UnitId && u.Mission == ServiceMission.CrimeCall && u.TargetBuildingId == c.BuildingId &&
                            (u.State == ServiceUnitState.Driving || u.State == ServiceUnitState.Working)) { active = true; break; }
                if (!active) Resolve(c, b, onScene: c.ArrivalTick >= 0);
            }
            openCrimes.RemoveAll(x => x.Closed);
        }

        /// <summary>Aufklärung: Grundquote je Art × Abdeckung + Bonus für schnelle Ankunft; ohne Einsatz nur spätere Ermittlung.</summary>
        void Resolve(CrimeIncident c, Building b, bool onScene)
        {
            if (c.Closed) return;
            c.Closed = true;
            float police = b != null ? services.Coverage(ServiceKinds.Police, b) : 0f;
            float chance = SafetyRules.SolveBase(c.Kind) * (0.5f + 0.5f * police);
            if (onScene && c.ArrivalTick >= 0)
                chance += SafetyRules.FastResponseBonus * Math.Clamp(1f - c.ResponseMinutes / SafetyRules.FastResponseMinutes, 0f, 1f);
            else chance *= SafetyRules.LateInvestigationShare;
            c.Solved = SafetyRules.Roll(c.Id, c.Tick, 29) < chance;
            if (!c.Solved) return;
            Today.Solved++;
            Today.Arrests++;
            // Festnahme → Zelle der zuständigen (sonst irgendeiner) Wache; voll → mangels Platz wieder frei
            ServiceFacility station = b != null ? services.AssignedFacility(ServiceKinds.Police, b) : null;
            if (station == null)
                foreach (var f in services.Facilities) if (f.Kind == ServiceKinds.Police && (station == null || f.Id < station.Id)) station = f;
            if (station != null && CustodyAt(station.Id) < SafetyRules.HoldingCellsPerStation) custody[station.Id] = CustodyAt(station.Id) + 1;
            else Today.Released++;
            if (c.Kind == CrimeKind.Burglary && b != null && onScene)
            {
                float rate = Total.Crimes + Today.Crimes > 0 ? (Total.Solved + Today.Solved) * 100f / (Total.Crimes + Today.Crimes) : 0f;
                Headline(HeadlineTopic.Arrest, "arrest" + (b.FrontSegmentId > 0 ? b.FrontSegmentId : b.Id), ("Straße", StreetName(b)), ("Prozent", rate.ToString("0")));
            }
        }

        /// <summary>Täglich: viele Taten rund um eine Straße → Schlagzeile + HUD-Meldung.</summary>
        void CrimeWaveHeadline()
        {
            var byCell = new SortedDictionary<int, int>();
            long since = clock.Tick - TimeModel.TicksPerDay;
            foreach (var c in crimes)
            {
                if (c.Tick < since) break;
                int cell = Cell(c.Position);
                byCell.TryGetValue(cell, out int n);
                byCell[cell] = n + 1;
            }
            int bestCell = -1, bestN = 0;
            foreach (var kv in byCell) if (kv.Value > bestN) { bestN = kv.Value; bestCell = kv.Key; }
            if (bestN < SafetyRules.CrimeWaveCount) return;
            CrimeIncident sample = null;
            foreach (var c in crimes) if (c.Tick >= since && Cell(c.Position) == bestCell) { sample = c; break; }
            string street = sample != null && sample.Street > 0 ? Citizens.Content.PlaceNames.Street(sample.Street) : "der Innenstadt";
            Headline(HeadlineTopic.CrimeWave, "wave" + bestCell, ("Straße", street), ("Anzahl", bestN.ToString()));
            Post(true, $"Kriminalität: {bestN} Taten rund um die {street}", "Mehr Polizeiabdeckung oder Streifen helfen; Arbeitslosigkeit und Unzufriedenheit treiben die Rate.", sample?.BuildingId ?? 0, "crime-wave");
        }
    }
}
