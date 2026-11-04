using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Flugbetrieb: stündlicher Flugplan (Umläufe nach Nachfrage, Bahn-/Terminal-Kapazität, Nachtflugverbot), sichtbare
    /// Flugzeuge (höchstens <see cref="AviationRules.MaxVisibleArrivalsPerHour"/> je Stunde, Rest statistisch wie der Anteil echter
    /// Autos) mit Anflug auf dem 3°-Gleitpfad, Aufsetzen, Ausrollen, Abrollen, Rollen zur Position, Abfertigung, Push-back,
    /// Haltepunkt (Startfreigabe nur bei freier Bahn), Startlauf und Steigflug aus der Karte. Alles in Verkehrszeit, deterministisch.
    /// </summary>
    public sealed partial class AviationSystem
    {
        // ------------------------------------------------------------ Flugplan

        void Hourly(long now, int hour)
        {
            double hourStart = Now;
            foreach (var a in airports)
            {
                a.NoiseA = a.NoiseHourA; a.NoiseB = a.NoiseHourB; a.NoiseHourA = a.NoiseHourB = 0f;
                a.MovementsLastHour = a.MovementsThisHour; a.MovementsThisHour = 0;
                if (!a.InService) continue;
                if (NightBan && IsNightHour(hour)) continue;
                ScheduleHour(a, hour, hourStart);
            }
            PruneRotations(hourStart);
            if (hour == 18) CountAffected();
            Version++;
        }

        /// <summary>Anteil der Tagesnachfrage in dieser Stunde (Morgen- und Abendspitze; Nacht wenig bzw. verschoben).</summary>
        public static float HourWeight(int hour, bool nightBan)
        {
            float w = HourShape(hour);
            float night = 0f, day = 0f;
            for (int h = 0; h < 24; h++) { if (IsNightHour(h)) night += HourShape(h); else day += HourShape(h); }
            if (IsNightHour(hour)) return nightBan ? 0f : w / (night + day);
            // Tagesstunden: mit Verbot kommt ein Teil der Nachtnachfrage dazu
            float shift = nightBan ? night * AviationRules.NightBanShift : 0f;
            return w / (night + day) * (1f + shift / day);
        }

        static float HourShape(int hour)
        {
            if (IsNightHour(hour)) return AviationRules.NightDemandShare;
            if (hour >= 6 && hour <= 9) return 1.4f;
            if (hour >= 16 && hour <= 20) return 1.3f;
            return 1f;
        }

        void ScheduleHour(Airport a, int hour, double hourStart)
        {
            var mix = MixOf(a);
            float avgSeats = 0f;
            foreach (var (t, share) in mix) avgSeats += t.Seats * share;
            float daily = DailyDemand(a);
            float paxHour = daily * HourWeight(hour, NightBan);
            double expected = paxHour / MathF.Max(1f, avgSeats * 0.75f);
            float minService = a.Type.Class == AirportClass.Airport ? 0.5f : 0.34f;
            if (!IsNightHour(hour) && expected < minService) expected = minService; // Grundversorgung der Fluglinien
            a.ScheduleCarry += expected;
            int n = (int)Math.Floor(a.ScheduleCarry);
            a.ScheduleCarry -= n;
            // Kapazität: Bahn (Hälfte der Bewegungen sind Landungen) und Terminal (Fluggäste beider Richtungen)
            int capRunway = (int)MathF.Floor(MovementCapacity(a) * 0.5f);
            int capTerminal = (int)MathF.Floor(a.Type.TerminalPaxPerHour[a.Level - 1] * 0.5f / MathF.Max(1f, avgSeats * 0.75f));
            int cap = Math.Max(0, Math.Min(capRunway, Math.Max(1, capTerminal)));
            bool freighter = a.Type.CargoLoadsPerDay > 0 && a.PendingLoads >= AviationRules.FreighterMinLoads &&
                             (hour == 6 || hour == 13 || hour == 20 || (!NightBan && hour == 3));
            if (freighter && cap > 0) cap--;
            if (n > cap) n = cap;
            int total = n + (freighter ? 1 : 0);
            if (total <= 0) return;
            float hourSeconds = TimeModel.TrafficSecondsPerTick * TimeModel.TicksPerHour;
            float lf = Math.Clamp(AviationRules.LoadFactorMin + (AviationRules.LoadFactorMax - AviationRules.LoadFactorMin) * paxHour / MathF.Max(1f, n * avgSeats),
                AviationRules.LoadFactorMin, AviationRules.LoadFactorMax);
            Composition(a, out float tourist, out float business);
            int visible = 0;
            for (int k = 0; k < total; k++)
            {
                bool isFreighter = freighter && k == total - 1;
                a.FlightCounter++;
                uint h = SimRandom.Hash(a.Id * 7919 + a.FlightCounter, (int)(world.Clock.Tick % int.MaxValue));
                var type = isFreighter ? AircraftCatalog.Freighter : PickType(mix, h);
                double t = hourStart + (k + 0.5) / total * hourSeconds;
                float jitter = ((h >> 8) % 100u) / 100f * 0.1f - 0.05f;
                var r = new Rotation
                {
                    Id = nextRotationId++, AirportId = a.Id, Type = type, ArrivalTime = t,
                    FlightNumber = FlightNumber(a, h), Destination = PickDestination(h >> 4),
                    ArrivingPax = (int)MathF.Round(type.Seats * Math.Clamp(lf + jitter, 0.3f, 1f)),
                    DepartingPax = (int)MathF.Round(type.Seats * Math.Clamp(lf - jitter, 0.3f, 1f)),
                    TouristShare = tourist, BusinessShare = business,
                };
                r.DepartureTime = ShiftOutOfBan(t + type.TurnaroundSeconds);
                if (isFreighter) { r.ArrivingLoads = type.CargoLoads / 2; r.DepartingLoads = Math.Min(a.PendingLoads, type.CargoLoads); }
                rotations.Add(r);
                // sichtbar: höchstens MaxVisible je Stunde, gleichmäßig verteilt, nur mit freier Position
                bool pick = total <= AviationRules.MaxVisibleArrivalsPerHour ||
                            (k * AviationRules.MaxVisibleArrivalsPerHour) % total < AviationRules.MaxVisibleArrivalsPerHour;
                if (pick && visible < AviationRules.MaxVisibleArrivalsPerHour && type.MinRunway <= a.RunwayLength)
                {
                    int stand = FreeStand(a);
                    if (stand >= 0) { SpawnArrival(a, r, stand); visible++; }
                }
            }
        }

        IReadOnlyList<(AircraftType type, float share)> MixOf(Airport a)
        {
            if (a.Type.Class == AirportClass.Airfield || a.RunwayLength < AircraftCatalog.Jet.MinRunway)
                return new[] { (AircraftCatalog.Small, 0.55f), (AircraftCatalog.Regional, 0.45f) };
            return new[] { (AircraftCatalog.Jet, 0.65f), (AircraftCatalog.Regional, 0.25f), (AircraftCatalog.Small, 0.1f) };
        }

        static AircraftType PickType(IReadOnlyList<(AircraftType type, float share)> mix, uint h)
        {
            float u = (h % 1000u) / 1000f, acc = 0f;
            foreach (var (t, share) in mix) { acc += share; if (u < acc) return t; }
            return mix[mix.Count - 1].type;
        }

        string FlightNumber(Airport a, uint h)
        {
            string city = world.Realm?.CityName ?? "Neustadt";
            string prefix = city.Length >= 2 ? city.Substring(0, 2).ToUpperInvariant() : "CB";
            return $"{prefix} {100 + (int)(h % 900u)}";
        }

        /// <summary>Abflug in die Nachtruhe? → auf 6:00 Uhr des nächsten Morgens verschieben (Nachtparken).</summary>
        double ShiftOutOfBan(double t)
        {
            if (!NightBan) return t;
            double daySeconds = TimeModel.TrafficSecondsPerDay;
            double hourSeconds = TimeModel.TrafficSecondsPerTick * TimeModel.TicksPerHour;
            double dayStart = Math.Floor(t / daySeconds) * daySeconds;
            int hour = (int)((t - dayStart) / hourSeconds);
            if (!IsNightHour(hour)) return t;
            double morning = dayStart + AviationRules.NightEndHour * hourSeconds;
            return hour >= AviationRules.NightStartHour ? morning + daySeconds : morning;
        }

        int CurrentHour => (int)(world.Clock.Tick % SimulationClock.TicksPerDay) / TimeModel.TicksPerHour;

        void PruneRotations(double now)
        {
            double keep = TimeModel.TrafficSecondsPerDay * 0.5;
            rotations.RemoveAll(r => r.Departed && r.DepartureTime < now - keep);
        }

        // ------------------------------------------------------------ Statistische Umläufe + Ereignisse

        void ProcessStatistical(double now)
        {
            foreach (var r in rotations)
            {
                if (r.Visible && GetAircraft(r.AircraftId) != null) continue;
                if (!r.Arrived && now >= r.ArrivalTime) OnArrival(r);
                if (r.Arrived && !r.Departed && now >= r.DepartureTime && !(NightBan && IsNightHour(CurrentHour))) OnDeparture(r);
            }
        }

        void OnArrival(Rotation r)
        {
            if (r.Arrived) return;
            r.Arrived = true;
            var a = GetAirport(r.AirportId);
            if (a == null) return;
            bool night = IsNightHour(CurrentHour);
            r.Night = night;
            a.ArrivalsToday++;
            a.TotalMovements++;
            a.MovementsThisHour++;
            float w = r.Type.Noise * (night ? AviationRules.NightNoiseWeight : 1f);
            if (a.ArrivalDirection > 0) a.NoiseHourA += w; else a.NoiseHourB += w;
            a.PaxInToday += r.ArrivingPax;
            int tourists = (int)MathF.Round(r.ArrivingPax * r.TouristShare);
            int business = (int)MathF.Round(r.ArrivingPax * r.BusinessShare);
            a.TouristsToday += tourists;
            a.BusinessToday += business;
            AddVisitors(tourists, r.Id);
            pendingFees += r.Type.LandingFee;
            if (r.Type.Role == AircraftRole.Passenger)
            {
                a.MoveInCarry += AviationRules.MoveInChancePerArrival;
                while (a.MoveInCarry >= 1.0) { a.MoveInCarry -= 1.0; pendingAirMoveIns++; }
            }
            Version++;
        }

        void OnDeparture(Rotation r)
        {
            if (r.Departed) return;
            r.Departed = true;
            if (!r.Arrived) r.Arrived = true;
            var a = GetAirport(r.AirportId);
            if (a == null) return;
            bool night = IsNightHour(CurrentHour);
            a.DeparturesToday++;
            a.TotalMovements++;
            a.MovementsThisHour++;
            float w = r.Type.Noise * (night ? AviationRules.NightNoiseWeight : 1f);
            if (a.DepartureDirection > 0) a.NoiseHourB += w; else a.NoiseHourA += w;
            a.PaxOutToday += r.DepartingPax;
            pendingFees += r.DepartingPax * AviationRules.PassengerFee;
            // Fracht: Frachtflug nimmt bis Laderaum, Passagierjets etwas Beiladung
            int capacity = r.Type.Role == AircraftRole.Cargo ? r.Type.CargoLoads : r.Type == AircraftCatalog.Jet ? 4 : 0;
            int loads = Math.Min(a.PendingLoads, Math.Max(r.DepartingLoads, capacity));
            if (loads > 0)
            {
                a.PendingLoads -= loads;
                pendingFees += loads * AviationRules.CargoFeePerLoad;
            }
            Version++;
        }

        // ------------------------------------------------------------ Sichtbare Flugzeuge

        int FreeStand(Airport a)
        {
            int n = a.Stands;
            for (int i = 0; i < n; i++)
            {
                bool used = false;
                foreach (var x in aircraft) if (x.AirportId == a.Id && x.Stand == i) { used = true; break; }
                if (!used) return i;
            }
            return -1;
        }

        void SpawnArrival(Airport a, Rotation r, int stand)
        {
            var l = a.Layout;
            var ac = new Aircraft
            {
                Id = nextAircraftId++, Type = r.Type, AirportId = a.Id, State = AircraftState.Approach, Direction = a.ArrivalDirection,
                Stand = stand, Speed = r.Type.ApproachSpeed, RotationId = r.Id, FlightNumber = r.FlightNumber,
            };
            float len = ApproachLength(l, ac.Direction);
            ac.StateSince = Math.Max(Now, r.ArrivalTime - len / r.Type.ApproachSpeed);
            ac.ExitU = ExitConnector(l, ac);
            r.Visible = true;
            r.AircraftId = ac.Id;
            aircraft.Add(ac);
            Version++;
        }

        void Remove(Aircraft ac)
        {
            if (!removedAircraft.Contains(ac)) removedAircraft.Add(ac);
        }

        void StepAircraft(Aircraft ac, double now, float dt)
        {
            ac.PrevS = ac.S;
            var a = GetAirport(ac.AirportId);
            var r = GetRotation(ac.RotationId);
            if (a == null) { Remove(ac); return; }
            var l = a.Layout;
            switch (ac.State)
            {
                case AircraftState.Approach:
                {
                    if (now < ac.StateSince) return;
                    float len = ApproachLength(l, ac.Direction);
                    ac.S += ac.Speed * dt;
                    if (ac.S >= len)
                    {
                        if (r != null) OnArrival(r);
                        Enter(ac, AircraftState.Rollout, now);
                        ac.Speed = ac.Type.ApproachSpeed * 0.92f;
                    }
                    break;
                }
                case AircraftState.Rollout:
                {
                    ac.Speed = MathF.Max(AviationRules.ExitSpeed, ac.Speed - ac.Type.Decel * dt);
                    ac.S += ac.Speed * dt;
                    if (ac.S >= RolloutLength(l, ac)) { Enter(ac, AircraftState.TaxiIn, now); ac.Speed = AviationRules.ExitSpeed; }
                    break;
                }
                case AircraftState.TaxiIn:
                {
                    var path = TaxiInPath(l, ac, scratch);
                    float total = PathLength(path);
                    Taxi(ac, total - ac.S, AviationRules.TaxiSpeed, dt);
                    if (ac.S >= total - 0.05f)
                    {
                        Enter(ac, AircraftState.Parked, now);
                        ac.Speed = 0f;
                        ac.DepartAt = ShiftOutOfBan(now + ac.Type.TurnaroundSeconds);
                        if (r != null) r.DepartureTime = ac.DepartAt;
                    }
                    break;
                }
                case AircraftState.Parked:
                    if (now >= ac.DepartAt && !(NightBan && IsNightHour(CurrentHour)))
                    {
                        ac.Direction = a.DepartureDirection;
                        Enter(ac, AircraftState.TaxiOut, now);
                    }
                    break;
                case AircraftState.TaxiOut:
                {
                    var path = TaxiOutPath(l, ac, scratch);
                    float total = PathLength(path);
                    float push = Vector2.Distance(path[0], path[1]);
                    float limit = ac.S < push ? AviationRules.PushbackSpeed : AviationRules.TaxiSpeed;
                    // am Ende des Push-backs kurz anhalten (Schlepper abkuppeln)
                    float toStop = ac.S < push ? push - ac.S : total - ac.S;
                    Taxi(ac, toStop, limit, dt);
                    if (ac.S >= total - 0.05f) { Enter(ac, AircraftState.Holding, now); ac.Speed = 0f; }
                    break;
                }
                case AircraftState.Holding:
                    if (RunwayFree(a, ac, now)) { Enter(ac, AircraftState.Takeoff, now); ac.Speed = 0f; }
                    else if (now - ac.StateSince > AviationRules.MaxHoldSeconds)
                    {
                        if (r != null) r.Visible = false; // läuft statistisch zu Ende
                        Remove(ac);
                    }
                    break;
                case AircraftState.Takeoff:
                {
                    float lineUp = LineUpLength(l);
                    if (ac.S < lineUp) ac.Speed = MathF.Min(AviationRules.TaxiSpeed * 0.6f, ac.Speed + AviationRules.TaxiAccel * dt);
                    else ac.Speed = MathF.Min(ac.Type.RotateSpeed * 1.35f, ac.Speed + ac.Type.Accel * dt);
                    ac.S += ac.Speed * dt;
                    if (r != null && !r.Departed && ac.S > lineUp && ac.Speed >= ac.Type.RotateSpeed) OnDeparture(r);
                    if (ac.S > lineUp + DepartureLength(l, ac.Direction)) Remove(ac);
                    break;
                }
            }
        }

        static void Enter(Aircraft ac, AircraftState s, double now)
        {
            ac.State = s;
            ac.S = 0f;
            ac.PrevS = 0f;
            ac.StateSince = now;
        }

        /// <summary>Rollen mit Beschleunigung bis <paramref name="limit"/>, Bremskurve zum Ende (1 m/s²).</summary>
        static void Taxi(Aircraft ac, float remaining, float limit, float dt)
        {
            float brake = MathF.Sqrt(2f * 1f * MathF.Max(0f, remaining)) + 0.4f;
            float target = MathF.Min(limit, brake);
            if (ac.Speed < target) ac.Speed = MathF.Min(target, ac.Speed + AviationRules.TaxiAccel * dt);
            else ac.Speed = MathF.Max(target, ac.Speed - 2f * dt);
            ac.S = MathF.Min(ac.S + ac.Speed * dt, ac.S + MathF.Max(0f, remaining) + 0.05f);
        }

        /// <summary>Startfreigabe: keine Landung rollt aus, kein anderer Start läuft, nächste Landung weiter als <see cref="AviationRules.LineUpClear"/> s.</summary>
        bool RunwayFree(Airport a, Aircraft self, double now)
        {
            var l = a.Layout;
            foreach (var x in aircraft)
            {
                if (x == self || x.AirportId != a.Id) continue;
                if (x.State == AircraftState.Rollout) return false;
                if (x.State == AircraftState.Takeoff && x.S < LineUpLength(l) + a.RunwayLength) return false;
                if (x.State == AircraftState.Holding && x.StateSince < self.StateSince - 1e-6) return false; // wer zuerst wartet, startet zuerst
                if (x.State == AircraftState.Approach)
                {
                    double toGo = (ApproachLength(l, x.Direction) - x.S) / MathF.Max(1f, x.Speed) + Math.Max(0.0, x.StateSince - now);
                    if (toGo < AviationRules.LineUpClear) return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------ Wege (abgeleitet aus Lage, Richtung, Position)

        readonly List<Vector2> scratch = new List<Vector2>(8);

        float TouchdownU(in AirportLayout l, int d) => -d * l.HalfLength + d * AviationRules.TouchdownOffset;

        /// <summary>Strecke vom Erscheinen (Kartenrand + Rand) bis zum Aufsetzpunkt.</summary>
        float ApproachLength(in AirportLayout l, int d)
        {
            var td = l.At(TouchdownU(l, d), 0f);
            return ExitDistance(td, l.Dir * -d) + AviationRules.SpawnMargin;
        }

        float DepartureLength(in AirportLayout l, int d)
        {
            var start = l.At(HoldU(l, d), 0f);
            return ExitDistance(start, l.Dir * d) + AviationRules.SpawnMargin;
        }

        /// <summary>Weg von p in Richtung d bis zum Kartenrand.</summary>
        float ExitDistance(Vector2 p, Vector2 d)
        {
            float t = float.MaxValue;
            if (d.X > 1e-6f) t = MathF.Min(t, (world.Max.X - p.X) / d.X);
            if (d.X < -1e-6f) t = MathF.Min(t, (world.Min.X - p.X) / d.X);
            if (d.Y > 1e-6f) t = MathF.Min(t, (world.Max.Y - p.Y) / d.Y);
            if (d.Y < -1e-6f) t = MathF.Min(t, (world.Min.Y - p.Y) / d.Y);
            return Math.Clamp(t, 0f, 6000f);
        }

        float ExitConnector(in AirportLayout l, Aircraft ac)
        {
            int d = ac.Direction;
            float td = TouchdownU(l, d);
            float v0 = ac.Type.ApproachSpeed * 0.92f, v1 = AviationRules.ExitSpeed;
            float stop = (v0 * v0 - v1 * v1) / (2f * ac.Type.Decel);
            float uStop = td + d * stop;
            var cons = l.Connectors();
            float best = d > 0 ? cons[cons.Length - 1] : cons[0];
            float bestDist = float.MaxValue;
            foreach (float c in cons)
            {
                float ahead = d * (c - uStop);
                if (ahead < 0f || ahead >= bestDist) continue;
                bestDist = ahead;
                best = c;
            }
            return best;
        }

        float RolloutLength(in AirportLayout l, Aircraft ac) => MathF.Max(1f, ac.Direction * (ac.ExitU - TouchdownU(l, ac.Direction)));

        float StandV(in AirportLayout l, Aircraft ac) => l.TerminalBack - 8f - ac.Type.Length * 0.5f;
        static float HoldU(in AirportLayout l, int d) => -d * (l.HalfLength - AviationRules.RunwayEndInset);
        static float LineUpLength(in AirportLayout l) => l.TaxiwayV * 0.45f;

        List<Vector2> TaxiInPath(in AirportLayout l, Aircraft ac, List<Vector2> pts)
        {
            pts.Clear();
            float su = l.StandU(ac.Stand);
            pts.Add(l.At(ac.ExitU, 0f));
            pts.Add(l.At(ac.ExitU, l.TaxiwayV));
            pts.Add(l.At(su, l.TaxiwayV));
            pts.Add(l.At(su, StandV(l, ac)));
            return pts;
        }

        /// <summary>Push-back (rückwärts bis auf den Rollweg), Rollen zum Haltepunkt am Startende.</summary>
        List<Vector2> TaxiOutPath(in AirportLayout l, Aircraft ac, List<Vector2> pts)
        {
            pts.Clear();
            float su = l.StandU(ac.Stand);
            float hu = HoldU(l, ac.Direction);
            pts.Add(l.At(su, StandV(l, ac)));
            pts.Add(l.At(su, l.TaxiwayV));
            pts.Add(l.At(hu, l.TaxiwayV));
            pts.Add(l.At(hu, LineUpLength(l)));
            return pts;
        }

        static float PathLength(List<Vector2> pts)
        {
            float s = 0f;
            for (int i = 1; i < pts.Count; i++) s += Vector2.Distance(pts[i - 1], pts[i]);
            return s;
        }

        static void Along(List<Vector2> pts, float s, out Vector2 pos, out Vector2 dir)
        {
            dir = Vector2.UnitX;
            pos = pts[0];
            for (int i = 1; i < pts.Count; i++)
            {
                var seg = pts[i] - pts[i - 1];
                float len = seg.Length();
                if (len < 1e-4f) continue;
                dir = seg / len;
                if (s <= len || i == pts.Count - 1) { pos = pts[i - 1] + dir * MathF.Min(s, len); return; }
                s -= len;
            }
        }

        /// <summary>
        /// Lage eines Flugzeugs bei Weg <paramref name="s"/> im aktuellen Abschnitt: Position, Höhe über dem Flugplatz, Blickrichtung
        /// (Nase). false = noch nicht erschienen. Für Darstellung (interpolierter Weg) und Inspektor.
        /// </summary>
        public bool TryPose(Aircraft ac, float s, out Vector2 pos, out float altitude, out Vector2 heading)
        {
            pos = default; altitude = 0f; heading = Vector2.UnitX;
            var a = GetAirport(ac.AirportId);
            if (a == null) return false;
            var l = a.Layout;
            var pts = new List<Vector2>(6);
            switch (ac.State)
            {
                case AircraftState.Approach:
                {
                    if (Now < ac.StateSince) return false;
                    float len = ApproachLength(l, ac.Direction);
                    var td = l.At(TouchdownU(l, ac.Direction), 0f);
                    heading = l.Dir * ac.Direction;
                    pos = td - heading * (len - s);
                    altitude = MathF.Max(0f, len - s) * AviationRules.GlideSlope;
                    return true;
                }
                case AircraftState.Rollout:
                    heading = l.Dir * ac.Direction;
                    pos = l.At(TouchdownU(l, ac.Direction) + ac.Direction * s, 0f);
                    return true;
                case AircraftState.TaxiIn:
                    Along(TaxiInPath(l, ac, pts), s, out pos, out heading);
                    return true;
                case AircraftState.Parked:
                    pos = l.At(l.StandU(ac.Stand), StandV(l, ac));
                    heading = l.Normal;
                    return true;
                case AircraftState.TaxiOut:
                {
                    var path = TaxiOutPath(l, ac, pts);
                    Along(path, s, out pos, out heading);
                    if (s < Vector2.Distance(path[0], path[1])) heading = -heading; // Push-back: Nase zeigt weiter zum Terminal
                    return true;
                }
                case AircraftState.Holding:
                {
                    var path = TaxiOutPath(l, ac, pts);
                    Along(path, PathLength(path), out pos, out heading);
                    return true;
                }
                case AircraftState.Takeoff:
                {
                    float lineUp = LineUpLength(l);
                    float hu = HoldU(l, ac.Direction);
                    if (s < lineUp)
                    {
                        pos = l.At(hu, lineUp - s);
                        heading = Vector2.Normalize(-l.Normal * 0.6f + l.Dir * ac.Direction * 0.4f);
                        return true;
                    }
                    float roll = s - lineUp;
                    heading = l.Dir * ac.Direction;
                    pos = l.At(hu + ac.Direction * roll, 0f);
                    float lift = ac.Type.RotateSpeed * ac.Type.RotateSpeed / (2f * ac.Type.Accel);
                    altitude = MathF.Max(0f, roll - lift) * ac.Type.ClimbGradient;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Text zum Zustand eines Flugzeugs (Inspektor).</summary>
        public string StateText(Aircraft ac)
        {
            switch (ac.State)
            {
                case AircraftState.Approach: return Now < ac.StateSince ? "im Anflug (noch außerhalb der Karte)" : $"im Endanflug · {ac.Speed * 3.6f:0} km/h";
                case AircraftState.Rollout: return $"ausrollen · {ac.Speed * 3.6f:0} km/h";
                case AircraftState.TaxiIn: return "rollt zur Abstellposition";
                case AircraftState.Parked: return $"Abfertigung an Position {ac.Stand + 1} · Abflug in {TimeModel.GameMinutes(Math.Max(0.0, ac.DepartAt - Now)):0} Spielminuten";
                case AircraftState.TaxiOut: return "rollt zum Start";
                case AircraftState.Holding: return "wartet am Haltepunkt auf Startfreigabe";
                default: return $"Start · {ac.Speed * 3.6f:0} km/h";
            }
        }
    }
}
