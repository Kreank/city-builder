using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Zugbetrieb – einfaches, aber echtes Blocksystem: Jedes Gleisstück ist ein <b>Blockabschnitt</b> mit Signal an seinem
    /// Anfang, jeder Knoten (Weiche, Kreuzung) ein Fahrstraßenelement. Ein Zug darf nur in Abschnitte einfahren, die er
    /// vorher für sich reserviert hat (frei bzw. schon seine); er reserviert voraus bis Bremsweg + Sichtweite und gibt
    /// Abschnitte hinter dem Zugende wieder frei. Fahrt nach Bremskurve (v ≤ √(2·b·Abstand)) bis zum Ende der Fahrerlaubnis
    /// (Signal vor dem ersten nicht reservierbaren Block bzw. Halteplatz am Bahnsteig). So treffen sich zwei Züge nie –
    /// Folgezüge stehen am Signal, Gegenzüge auf eingleisigen Stücken warten (gegenseitiges Warten löst ein Ausfall).
    /// Fahrplan: Fernzüge stündlich je Richtung (6–22 Uhr), Güterzüge alle 2 h, Einfahrt am Kartenrand der Hauptstrecke.
    /// </summary>
    public sealed partial class RailSystem
    {
        /// <summary>Blockabschnitt (Gleisstück-Id) → Zug-Id, Knoten-Id → Zug-Id.</summary>
        readonly Dictionary<int, int> segmentOwner = new Dictionary<int, int>();
        readonly Dictionary<int, int> nodeOwner = new Dictionary<int, int>();
        readonly List<Train> removedTrains = new List<Train>();

        public Train GetTrain(int id) { foreach (var t in trains) if (t.Id == id) return t; return null; }
        /// <summary>Halte des Zuges (Bahnhof-Id, Strecke der Zugspitze beim Halt) in Fahrwegreihenfolge.</summary>
        public IReadOnlyList<(int station, float s)> StopsOf(Train t) => t.Stops;
        /// <summary>Beginn des i-ten Fahrwegstücks (m ab Fahrwegbeginn).</summary>
        public float RouteStartOf(Train t, int i) => t.RouteStart[i];
        /// <summary>Fahrgäste eines Zuges, die zu/von dieser Stadt fahren (Anzeige).</summary>
        public IReadOnlyList<RailPassenger> RidersOf(Train t) => t.Riders;
        /// <summary>Zug, der den Blockabschnitt belegt/reserviert hat (0 = frei).</summary>
        public int OwnerOf(int segmentId) => segmentOwner.TryGetValue(segmentId, out int t) ? t : 0;

        /// <summary>Signal am Beginn eines gerichteten Gleisstücks: Fahrt (frei) oder Halt (belegt/reserviert).</summary>
        public bool SignalClear(EdgeKey e) => !segmentOwner.ContainsKey(e.SegmentId);

        // ------------------------------------------------------------ Fahrplan

        void ScheduleTrains(int tod)
        {
            if (!HasMainline) return;
            int hour = tod / TimeModel.TicksPerHour, minuteTick = tod % TimeModel.TicksPerHour;
            if (hour >= RailRules.FirstIntercityHour && hour <= RailRules.LastIntercityHour)
            {
                if (minuteTick == 0) QueueTrain(TrainCatalog.Intercity, fromA: true);
                if (minuteTick == 5) QueueTrain(TrainCatalog.Intercity, fromA: false);
            }
            if (hour % RailRules.FreightEveryHours == 0 && minuteTick == 2) QueueTrain(TrainCatalog.Freight, fromA: true);
            if (hour % RailRules.FreightEveryHours == 1 && minuteTick == 7) QueueTrain(TrainCatalog.Freight, fromA: false);
        }

        /// <summary>Zug vor der Karte bereitstellen (fährt ein, sobald der erste Block frei ist). Liefert null ohne Fahrweg.</summary>
        public Train QueueTrain(TrainType type, bool fromA)
        {
            int entryNode = fromA ? Track1StartNode : Track2StartNode;
            int exitNode = fromA ? Track1EndNode : Track2EndNode;
            var t = new Train
            {
                Id = nextTrainId++, Type = type, State = TrainState.Waiting,
                FromGatewayId = fromA ? GatewayA : GatewayB, ToGatewayId = fromA ? GatewayB : GatewayA,
                SpawnTime = Now,
            };
            EnsureGraph();
            EnsurePlatforms();
            if (!PlanRoute(t, entryNode, exitNode)) { TrainsCancelled++; return null; }
            trains.Add(t);
            Version++;
            return t;
        }

        /// <summary>
        /// Fahrweg Einfahrt → Ausfahrt; mit Halt an einem passenden Bahnhof (Fernzug: Personenbahnhof, Güterzug: Güterbahnhof)
        /// über das Bahnsteiggleis mit dem kürzesten Umweg. Weitere Bahnhöfe, deren Bahnsteig auf dem Fahrweg liegt, bekommen
        /// ebenfalls einen Halt.
        /// </summary>
        bool PlanRoute(Train t, int entryNode, int exitNode)
        {
            var entry = EntryEdge(entryNode);
            if (!entry.HasValue) return false;
            Func<EdgeKey, bool> atExit = e => { var s = Tracks.GetSegment(e.SegmentId); return s != null && EndNode(s, e).Id == exitNode; };
            List<EdgeKey> best = FindPath(entry.Value, atExit);
            if (best == null) return false;
            var kind = t.Type.Kind == TrainKind.Intercity ? StationKind.Passenger : StationKind.Freight;
            float bestLen = float.MaxValue;
            List<EdgeKey> via = null;
            foreach (var st in stations)
            {
                if (st.Type.Kind != kind || !st.InService) continue;
                foreach (var p in st.PlatformList)
                    for (int k = 0; k < 2; k++)
                    {
                        var pe = new EdgeKey(p.SegmentId, k == 0);
                        var a = FindPath(entry.Value, e => e.Equals(pe));
                        if (a == null) continue;
                        var b = FindPath(pe, atExit);
                        if (b == null) continue;
                        var route = new List<EdgeKey>(a);
                        for (int i = 1; i < b.Count; i++) route.Add(b[i]);
                        float len = RouteMeters(route);
                        if (len < bestLen - 0.5f) { bestLen = len; via = route; }
                    }
            }
            if (via != null) best = via;
            SetRoute(t, best);
            // Halte: alle Bahnhöfe passender Art, deren Bahnsteig auf dem Fahrweg liegt
            t.Stops.Clear();
            t.NextStop = 0;
            for (int i = 0; i < t.Route.Count; i++)
            {
                var e = t.Route[i];
                foreach (var st in stations)
                {
                    if (st.Type.Kind != kind || !st.InService) continue;
                    foreach (var p in st.PlatformList)
                    {
                        if (p.SegmentId != e.SegmentId) continue;
                        bool dup = false;
                        foreach (var x in t.Stops) if (x.station == st.Id) dup = true;
                        if (dup) continue;
                        float sEdge = (e.Along ? p.T : 1f - p.T) * t.EdgeLength[i];
                        float stopS = MathF.Min(t.RouteStart[i] + sEdge + t.Length * 0.5f, t.RouteLength - 1f);
                        t.Stops.Add((st.Id, stopS));
                    }
                }
            }
            t.Stops.Sort((x, y) => x.s.CompareTo(y.s));
            return true;
        }

        float RouteMeters(List<EdgeKey> route)
        {
            float s = 0f;
            foreach (var e in route) s += Tracks.GetSegment(e.SegmentId)?.Length ?? 0f;
            return s;
        }

        void SetRoute(Train t, List<EdgeKey> route)
        {
            t.Route.Clear(); t.RouteStart.Clear(); t.EdgeLength.Clear();
            float s = 0f;
            foreach (var e in route)
            {
                float len = Tracks.GetSegment(e.SegmentId).Length;
                t.Route.Add(e);
                t.RouteStart.Add(s);
                t.EdgeLength.Add(len);
                s += len;
            }
            t.RouteLength = s;
        }

        /// <summary>Gleisnetz geändert: Fahrwege mit fehlenden Stücken neu planen (Züge auf der Karte ab ihrem Standort).</summary>
        void RevalidateRoutes()
        {
            foreach (var t in trains)
            {
                bool broken = false;
                for (int i = Math.Max(0, t.HeadIndex); i < t.Route.Count && !broken; i++) if (Tracks.GetSegment(t.Route[i].SegmentId) == null) broken = true;
                if (!broken) continue;
                if (t.State == TrainState.Waiting)
                {
                    int entry = t.FromGatewayId == GatewayA ? Track1StartNode : Track2StartNode;
                    int exit = t.FromGatewayId == GatewayA ? Track1EndNode : Track2EndNode;
                    if (!PlanRoute(t, entry, exit)) CancelTrain(t);
                }
                else CancelTrain(t); // Gleis unter/vor dem Zug weg (sollte durch die Belegt-Sperre nicht vorkommen)
            }
        }

        // ------------------------------------------------------------ Einfahren

        void TrySpawn(Train t, double now)
        {
            if (TryReserve(t, 0))
            {
                t.State = TrainState.Running;
                t.HeadS = 0f;
                t.PrevHeadS = 0f;
                t.HeadIndex = 0;
                t.ReleasedBefore = 0;
                t.Speed = MathF.Min(t.Type.MaxSpeed, EdgeSpeed(Tracks.GetSegment(t.Route[0].SegmentId)));
                t.DelaySeconds = now - t.SpawnTime;
                if (t.Type.Kind == TrainKind.Intercity)
                {
                    uint h = Citizens.SimRandom.Hash(t.Id, 811);
                    t.ThroughPassengers = RailRules.ThroughPassengersMin + (int)(h % (uint)(RailRules.ThroughPassengersMax - RailRules.ThroughPassengersMin));
                    BoardInbound(t);
                }
                else t.Loads = t.Type.Wagons * RailRules.LoadsPerWagon / 2;
                TrainsSpawned++;
                Version++;
                return;
            }
            if (now - t.SpawnTime > RailRules.SpawnPatience) CancelTrain(t);
        }

        void CancelTrain(Train t)
        {
            if (removedTrains.Contains(t)) return;
            foreach (var p in t.Riders.ToArray()) Finish(p, RailRideResult.GaveUp, GiveUpTicks(p));
            t.Riders.Clear();
            ReleaseAll(t);
            removedTrains.Add(t);
            TrainsCancelled++;
        }

        // ------------------------------------------------------------ Blöcke

        /// <summary>Reserviert Abschnitt <paramref name="i"/> des Fahrwegs (Gleisstück + Einfahrknoten), wenn frei bzw. schon eigen.</summary>
        bool TryReserve(Train t, int i)
        {
            var e = t.Route[i];
            if (segmentOwner.TryGetValue(e.SegmentId, out int o) && o != t.Id) { t.BlockedBy = o; return false; }
            int node = 0;
            if (i > 0)
            {
                var s = Tracks.GetSegment(e.SegmentId);
                node = StartNode(s, e).Id;
                if (nodeOwner.TryGetValue(node, out int on) && on != t.Id) { t.BlockedBy = on; return false; }
            }
            segmentOwner[e.SegmentId] = t.Id;
            if (node != 0) nodeOwner[node] = t.Id;
            if (i > t.ReservedUntil) t.ReservedUntil = i;
            t.BlockedBy = 0;
            return true;
        }

        void Release(Train t, int i)
        {
            var e = t.Route[i];
            if (segmentOwner.TryGetValue(e.SegmentId, out int o) && o == t.Id)
            {
                // Fahrweg führt später noch einmal über dasselbe Stück (Schleife): behalten
                bool again = false;
                for (int k = i + 1; k <= t.ReservedUntil && k < t.Route.Count; k++) if (t.Route[k].SegmentId == e.SegmentId) again = true;
                if (!again) segmentOwner.Remove(e.SegmentId);
            }
            if (i + 1 < t.Route.Count)
            {
                var s = Tracks.GetSegment(t.Route[i + 1].SegmentId);
                if (s != null)
                {
                    int node = StartNode(s, t.Route[i + 1]).Id;
                    if (nodeOwner.TryGetValue(node, out int on) && on == t.Id) nodeOwner.Remove(node);
                }
            }
        }

        void ReleaseAll(Train t)
        {
            var segs = new List<int>();
            foreach (var kv in segmentOwner) if (kv.Value == t.Id) segs.Add(kv.Key);
            foreach (int s in segs) segmentOwner.Remove(s);
            var nodes = new List<int>();
            foreach (var kv in nodeOwner) if (kv.Value == t.Id) nodes.Add(kv.Key);
            foreach (int n in nodes) nodeOwner.Remove(n);
        }

        // ------------------------------------------------------------ Fahren

        void StepTrain(Train t, double now)
        {
            float dt = TimeModel.TrafficStepSeconds;
            t.PrevHeadS = t.HeadS;
            if (t.State == TrainState.Dwelling)
            {
                t.Speed = 0f;
                if (now >= t.DwellUntil) { t.State = TrainState.Running; t.NextStop++; Version++; }
                ReleaseBehind(t);
                return;
            }

            // Fahrerlaubnis: voraus reservieren bis Bremsweg + Sichtweite
            float b = t.Type.Deceleration;
            float horizon = t.HeadS + t.Speed * t.Speed / (2f * b) + RailRules.SightMargin;
            while (t.ReservedUntil + 1 < t.Route.Count && t.RouteStart[t.ReservedUntil + 1] < horizon)
                if (!TryReserve(t, t.ReservedUntil + 1)) break;
            bool clearToExit = t.ReservedUntil >= t.Route.Count - 1;
            float authority = clearToExit ? float.MaxValue
                : t.ReservedUntil >= 0 ? t.RouteStart[t.ReservedUntil] + t.EdgeLength[t.ReservedUntil] - RailRules.SignalMargin : 0f;
            float stopS = t.NextStop < t.Stops.Count ? t.Stops[t.NextStop].s : float.MaxValue;
            float target = MathF.Min(authority, stopS);

            // Höchstgeschwindigkeit hier und Bremskurven vor langsameren Abschnitten
            int hi = Math.Min(t.HeadIndex, t.Route.Count - 1);
            var cur = Tracks.GetSegment(t.Route[hi].SegmentId);
            float vmax = MathF.Min(t.Type.MaxSpeed, cur != null ? EdgeSpeed(cur) : t.Type.MaxSpeed);
            for (int i = hi + 1; i <= t.ReservedUntil && i < t.Route.Count; i++)
            {
                float d = t.RouteStart[i] - t.HeadS;
                if (d > horizon - t.HeadS) break;
                var s = Tracks.GetSegment(t.Route[i].SegmentId);
                if (s == null) continue;
                float vl = EdgeSpeed(s);
                if (vl < vmax) vmax = MathF.Min(vmax, MathF.Sqrt(vl * vl + 2f * b * MathF.Max(0f, d)));
            }
            float vBrake = target == float.MaxValue ? float.MaxValue : MathF.Sqrt(2f * b * MathF.Max(0f, target - t.HeadS));
            float allowed = MathF.Min(vmax, vBrake);
            if (t.Speed < allowed) t.Speed = MathF.Min(allowed, t.Speed + t.Type.Acceleration * dt);
            else t.Speed = MathF.Max(MathF.Max(0f, allowed), t.Speed - 2f * b * dt);
            float move = t.Speed * dt;
            float newS = t.HeadS + move;
            if (target != float.MaxValue && newS > target) newS = MathF.Max(t.HeadS, target);
            t.HeadS = newS;
            while (t.HeadIndex < t.Route.Count - 1 && t.HeadS >= t.RouteStart[t.HeadIndex + 1]) t.HeadIndex++;
            t.AtSignal = authority < stopS && t.Speed < 0.5f && t.HeadS >= authority - 2f;
            if (t.AtSignal) { if (t.WaitSince <= 0) t.WaitSince = now; } else t.WaitSince = 0;

            // Halt am Bahnsteig
            if (stopS != float.MaxValue && t.HeadS >= stopS - 0.6f && t.Speed < 0.8f) ArriveAtStation(t, now);

            ReleaseBehind(t);
            if (t.HeadS - t.Length >= t.RouteLength) FinishTrain(t);
            else if (t.AtSignal && t.WaitSince > 0 && now - t.WaitSince > 240) ResolveDeadlock(t);
        }

        void ReleaseBehind(Train t)
        {
            float tail = t.HeadS - t.Length;
            while (t.ReleasedBefore < t.Route.Count && t.ReleasedBefore <= t.ReservedUntil &&
                   t.RouteStart[t.ReleasedBefore] + t.EdgeLength[t.ReleasedBefore] <= tail)
            {
                Release(t, t.ReleasedBefore);
                t.ReleasedBefore++;
            }
        }

        void FinishTrain(Train t)
        {
            foreach (var p in t.Riders.ToArray())
                if (p.Outbound) Finish(p, RailRideResult.Arrived, Core.TimeModel.TicksFor(RailRules.OutsideSeconds));
                else Finish(p, RailRideResult.GaveUp, GiveUpTicks(p)); // Ziel-Bahnhof unterwegs weggefallen
            t.Riders.Clear();
            ReleaseAll(t);
            if (!removedTrains.Contains(t)) removedTrains.Add(t);
            TrainsCompleted++;
        }

        /// <summary>Zwei Züge warten gegenseitig aufeinander (eingleisiges Stück): der später eingesetzte fällt aus.</summary>
        void ResolveDeadlock(Train t)
        {
            var other = GetTrain(t.BlockedBy);
            if (other == null || other.BlockedBy != t.Id || !other.AtSignal) return;
            CancelTrain(t.Id > other.Id ? t : other);
        }

        // ------------------------------------------------------------ Darstellung

        /// <summary>
        /// Lage eines Punkts <paramref name="behind"/> Meter hinter der Zugspitze (Spitze bei <paramref name="headS"/>):
        /// Position, Höhe (Schienenoberkante), Fahrtrichtung. Vor/nach dem Fahrweg wird gerade weitergeführt.
        /// </summary>
        public void Sample(Train t, float headS, float behind, out Vector2 position, out float height, out Vector2 direction)
        {
            float s = headS - behind;
            position = Vector2.Zero; height = 0f; direction = Vector2.UnitX;
            if (t.Route.Count == 0) return;
            if (s < 0f)
            {
                SampleEdge(t, 0, 0f, out position, out height, out direction);
                position += direction * s;
                return;
            }
            if (s > t.RouteLength)
            {
                int last = t.Route.Count - 1;
                SampleEdge(t, last, t.EdgeLength[last], out position, out height, out direction);
                position += direction * (s - t.RouteLength);
                return;
            }
            int i = BinaryEdge(t, s);
            SampleEdge(t, i, s - t.RouteStart[i], out position, out height, out direction);
        }

        static int BinaryEdge(Train t, float s)
        {
            int lo = 0, hi = t.Route.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (t.RouteStart[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        void SampleEdge(Train t, int i, float sEdge, out Vector2 position, out float height, out Vector2 direction)
        {
            var e = t.Route[i];
            var seg = Tracks.GetSegment(e.SegmentId);
            if (seg == null) { position = Vector2.Zero; height = 0f; direction = Vector2.UnitX; return; }
            float f = t.EdgeLength[i] > 0f ? Math.Clamp(sEdge / t.EdgeLength[i], 0f, 1f) : 0f;
            float u = e.Along ? f : 1f - f;
            position = seg.Evaluate(u);
            height = seg.HeightAt(u);
            var tan = seg.Tangent(u);
            direction = e.Along ? tan : -tan;
        }

        /// <summary>Strecke ab Fahrwegbeginn, an der ein Punkt eines Gleisstücks liegt (oder −1, wenn nicht auf dem Fahrweg ab <paramref name="fromIndex"/>).</summary>
        float RouteDistance(Train t, int segmentId, float segT, int fromIndex)
        {
            for (int i = Math.Max(0, fromIndex); i < t.Route.Count; i++)
                if (t.Route[i].SegmentId == segmentId)
                    return t.RouteStart[i] + (t.Route[i].Along ? segT : 1f - segT) * t.EdgeLength[i];
            return -1f;
        }
    }
}
