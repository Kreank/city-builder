using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Transit
{
    /// <summary>Fahrgäste: Verbindung planen (höchstens 1 Umstieg), Fahrt beginnen, warten/aufgeben, Rückmeldung.</summary>
    public sealed partial class TransitSystem
    {
        readonly List<(BusStop stop, float dist)> nearFrom = new List<(BusStop, float)>();
        readonly List<(BusStop stop, float dist)> nearTo = new List<(BusStop, float)>();
        /// <summary>Höchstens so viele nächste Haltestellen je Ende werden betrachtet.</summary>
        const int MaxCandidateStops = 4;

        /// <summary>
        /// Beste ÖPNV-Verbindung von <paramref name="from"/> nach <paramref name="to"/> nach gefühlter Reisezeit:
        /// <see cref="TransitRules.WalkWeight"/> × Fußwege (Luftlinie ÷ <see cref="TransitRules.WalkSpeed"/>) +
        /// <see cref="TransitRules.WaitWeight"/> × Wartezeit (halber Takt je Linie) + Fahrzeit laut Fahrplan +
        /// <see cref="TransitRules.TransferPenaltySeconds"/> je Umstieg (max. 1). Nur Linien mit Bussen im Einsatz,
        /// Haltestellen ≤ <see cref="TransitRules.MaxWalkMeters"/>. <c>Valid = false</c>, wenn es keine gibt.
        /// </summary>
        public TransitOption PlanRide(Vector2 from, Vector2 to)
        {
            var best = default(TransitOption);
            if (lines.Count == 0 || stops.Count < 2) return best;
            EnsureRoutes();
            Nearest(from, nearFrom);
            if (nearFrom.Count == 0) return best;
            Nearest(to, nearTo);
            if (nearTo.Count == 0) return best;

            best.PerceivedSeconds = float.MaxValue;
            foreach (var (sa, da) in nearFrom)
            {
                if (!linesAtStop.TryGetValue(sa.Id, out var atA)) continue;
                foreach (var l1 in atA)
                {
                    if (!Usable(l1)) continue;
                    float wait1 = l1.HeadwaySeconds * 0.5f;
                    // Direkt
                    foreach (var (sb, db) in nearTo)
                    {
                        if (sb == sa) continue;
                        float ride = l1.RideSeconds(sa.Id, sb.Id);
                        if (float.IsInfinity(ride)) continue;
                        Consider(ref best, l1.Id, sa.Id, sb.Id, 0, 0, da, db, wait1, ride);
                    }
                    // Ein Umstieg
                    foreach (int x in l1.StopList)
                    {
                        if (x == sa.Id || !linesAtStop.TryGetValue(x, out var atX)) continue;
                        float r1 = l1.RideSeconds(sa.Id, x);
                        if (float.IsInfinity(r1)) continue;
                        foreach (var l2 in atX)
                        {
                            if (l2 == l1 || !Usable(l2)) continue;
                            foreach (var (sb, db) in nearTo)
                            {
                                if (sb.Id == x) continue;
                                float r2 = l2.RideSeconds(x, sb.Id);
                                if (float.IsInfinity(r2)) continue;
                                Consider(ref best, l1.Id, sa.Id, x, l2.Id, sb.Id, da, db, wait1 + l2.HeadwaySeconds * 0.5f, r1 + r2);
                            }
                        }
                    }
                }
            }
            if (!best.Valid) best = default;
            return best;
        }

        static bool Usable(TransitLine l) => l.RouteValid && l.ActiveVehicles > 0 && !float.IsInfinity(l.HeadwaySeconds);

        static void Consider(ref TransitOption best, int line1, int board, int alight1, int line2, int alight2, float da, float db, float wait, float ride)
        {
            int transfers = line2 != 0 ? 1 : 0;
            float walk = (da + db) / TransitRules.WalkSpeed;
            float perceived = TransitRules.WalkWeight * walk + TransitRules.WaitWeight * wait + ride + transfers * TransitRules.TransferPenaltySeconds;
            if (perceived >= best.PerceivedSeconds) return;
            best = new TransitOption
            {
                Valid = true,
                Line1 = line1, Board = board, Alight1 = alight1, Line2 = line2, Alight2 = alight2,
                AccessMeters = da, EgressMeters = db, WaitSeconds = wait, RideSeconds = ride, Transfers = transfers,
                PerceivedSeconds = perceived, TotalSeconds = walk + wait + ride,
            };
        }

        void Nearest(Vector2 p, List<(BusStop, float)> into)
        {
            into.Clear();
            foreach (var s in stops)
            {
                float d = Vector2.Distance(s.Position, p);
                if (d > TransitRules.MaxWalkMeters || !linesAtStop.ContainsKey(s.Id)) continue;
                int i = into.Count;
                while (i > 0 && into[i - 1].Item2 > d) i--;
                if (i >= MaxCandidateStops) continue;
                into.Insert(i, (s, d));
                if (into.Count > MaxCandidateStops) into.RemoveAt(into.Count - 1);
            }
        }

        /// <summary>
        /// Fahrgast beginnt die Fahrt: geht zur Einstiegshaltestelle (Luftlinie × <see cref="Core.TimeModel.WalkDetour"/> ÷
        /// Gehtempo, Verkehrszeit) und wartet danach dort (<see cref="Passenger.ReadyTime"/>). null = Verbindung ungültig.
        /// </summary>
        public Passenger StartRide(int citizenId, in TransitOption option, Vector2 destination)
        {
            if (!option.Valid) return null;
            var stop = GetStop(option.Board);
            if (stop == null || GetLine(option.Line1) == null) return null;
            double now = traffic.Time;
            var p = new Passenger
            {
                Id = nextPassengerId++,
                CitizenId = citizenId,
                Plan = option,
                Destination = destination,
                WaitingAtStop = stop.Id,
                ReadyTime = now + Core.TimeModel.WalkSeconds(option.AccessMeters),
                StartTime = now,
                EndPosition = stop.Position,
            };
            p.WaitSince = p.ReadyTime;
            stop.Waiting.Add(p);
            return p;
        }

        /// <summary>
        /// Skalierung (wie bei Autofahrten): Fahrt eines Bürgers, der den Bus gewählt hat, aber nicht als einzelner Fahrgast
        /// simuliert wird. Zählt als Fahrgast der Linie(n) und zahlt Fahrgeld, belegt aber keinen Platz im Bus.
        /// </summary>
        public void RecordStatisticalRide(in TransitOption option)
        {
            if (!option.Valid) return;
            var l1 = GetLine(option.Line1);
            if (l1 != null) { l1.PassengersToday++; l1.PassengersTotal++; }
            var l2 = option.Line2 != 0 ? GetLine(option.Line2) : null;
            if (l2 != null) { l2.PassengersToday++; l2.PassengersTotal++; }
            int rides = (l1 != null ? 1 : 0) + (l2 != null ? 1 : 0);
            StatisticalBoardings += rides;
            pendingFares += rides * Fare;
        }

        /// <summary>Einstiege statistischer (nicht einzeln simulierter) Fahrgäste, kumuliert.</summary>
        public int StatisticalBoardings { get; private set; }

        /// <summary>Fahrt abbrechen (Bürgersystem: Zeitüberschreitung, Wegzug …). Meldet kein <see cref="RideEnded"/>.</summary>
        public void CancelRide(Passenger p)
        {
            if (p == null || !p.IsActive) return;
            Detach(p);
            p.IsActive = false;
            p.Outcome = RideOutcome.Cancelled;
        }

        void Detach(Passenger p)
        {
            if (p.WaitingAtStop != 0 && stopById.TryGetValue(p.WaitingAtStop, out var s)) s.Waiting.Remove(p);
            p.WaitingAtStop = 0;
            p.Bus?.Riders.Remove(p);
            p.Bus = null;
        }

        void Finish(Passenger p, RideOutcome outcome, Vector2 at)
        {
            if (!p.IsActive) return;
            if (p.WaitingAtStop != 0) p.WaitedSeconds += (float)Math.Max(0.0, traffic.Time - p.WaitSince);
            Detach(p);
            p.IsActive = false;
            p.Outcome = outcome;
            p.EndPosition = at;
            if (outcome == RideOutcome.GaveUpFull || outcome == RideOutcome.GaveUpWaiting) TotalGaveUp++;
            if (outcome == RideOutcome.Arrived && traffic.Time - p.StartTime > p.Plan.WaitSeconds * 2f + p.Plan.RideSeconds * TransitRules.LateFactor + 60f)
                p.WasLate = true;
            RideEnded?.Invoke(p, outcome);
        }

        /// <summary>Wartende geben nach max(<see cref="TransitRules.MinPatienceSeconds"/>, 2,5 × Takt) auf.</summary>
        void CheckWaiting()
        {
            double now = traffic.Time;
            foreach (var s in stops)
                for (int i = s.Waiting.Count - 1; i >= 0; i--)
                {
                    if (i >= s.Waiting.Count) continue;
                    var p = s.Waiting[i];
                    var l = GetLine(p.CurrentLineId);
                    if (l == null) { Finish(p, RideOutcome.Stranded, s.Position); continue; }
                    float patience = MathF.Max(TransitRules.MinPatienceSeconds,
                        TransitRules.PatienceHeadways * (float.IsInfinity(l.HeadwaySeconds) ? 0f : l.HeadwaySeconds));
                    if (l.ActiveVehicles == 0 && l.TargetVehicles == 0) patience = 10f;
                    if (now - p.WaitSince > patience) Finish(p, RideOutcome.GaveUpWaiting, s.Position);
                }
        }
    }
}
