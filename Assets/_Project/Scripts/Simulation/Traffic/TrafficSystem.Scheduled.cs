using System;
using System.Collections.Generic;
using System.Linq;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// ÖPNV: Linienfahrzeuge (Busse) im normalen Verkehr. Sie fahren wie alle anderen (IDM, Spurwahl, Vorfahrt, Stau),
    /// verschwinden am Ziel aber nicht: Sie bremsen bis zur Haltestelle, bleiben stehen und melden
    /// <see cref="ScheduledStopReached"/>. Das <c>TransitSystem</c> lässt dort ein-/aussteigen, setzt die Haltezeit
    /// (<see cref="HoldAtStopFor"/>) und das nächste Ziel (<see cref="Redirect"/>) oder schickt den Bus weg (<see cref="Dismiss"/>).
    /// Abbruch (Stillstand 90 s, Straße weg, Ziel unerreichbar) meldet <see cref="ScheduledVehicleRemoved"/>.
    /// </summary>
    public sealed partial class TrafficSystem
    {
        /// <summary>Linienfahrzeuge dürfen das Fahrzeuglimit um so viele überschreiten (Durchgangsverkehr füllt es sonst ganz).</summary>
        public int ScheduledVehicleReserve = 300;

        /// <summary>Linienfahrzeug steht an seinem Ziel (Haltestelle). Läuft während des Verkehrs-Ticks.</summary>
        public event Action<Vehicle> ScheduledStopReached;
        /// <summary>Linienfahrzeug wurde entfernt (Fahrzeug, wegen Stillstand/Unerreichbarkeit abgebrochen?).</summary>
        public event Action<Vehicle, bool> ScheduledVehicleRemoved;

        /// <summary>
        /// Setzt ein Linienfahrzeug an einer Stelle des Netzes ein (z. B. Depot-Zufahrt) mit Ziel auf einem Segment.
        /// <paramref name="direction"/>: 0 = beliebige Fahrtrichtung, 1 = in Segmentrichtung, 2 = dagegen.
        /// null = kein Weg, kein Platz an der Startstelle oder Fahrzeuglimit erreicht.
        /// </summary>
        public Vehicle RequestScheduledVehicle(TripEnd from, int segmentId, float t, byte direction, VehicleClass cls, int transitId)
        {
            Lanes.Update();
            if (Vehicles.Count >= MaxVehicles + ScheduledVehicleReserve) return null;
            if (network.GetSegment(segmentId) == null) return null;

            var starts = new List<(Lane lane, float s)>();
            if (from.IsNode)
            {
                if (!network.Contains(from.Node)) return null;
                foreach (var l in Lanes.OutgoingLanes(from.Node)) starts.Add((l, 0f));
            }
            else
            {
                var seg = network.GetSegment(from.SegmentId);
                if (seg == null) return null;
                foreach (var l in Lanes.GetLanes(seg.Id)) starts.Add((l, l.Along ? from.T * l.Length : (1f - from.T) * l.Length));
            }
            starts.RemoveAll(x => (x.lane.Allowed & cls) == 0 || !Rules.IsSegmentAllowed(x.lane.Segment.Id, cls));
            if (starts.Count == 0) return null;

            var probe = new Vehicle { Class = cls, DestinationSegmentId = segmentId, DestinationT = t, DestinationDirection = direction };
            List<EdgeKey> bestPath = null;
            float bestCost = float.MaxValue, bestS = 0f;
            EdgeKey bestEdge = default;
            foreach (var group in starts.GroupBy(x => x.lane.Edge))
            {
                float s = group.First().s;
                var path = FindRoute(probe, group.Key, s, null);
                if (path == null || Paths.LastCost >= bestCost) continue;
                bestCost = Paths.LastCost;
                bestPath = path;
                bestEdge = group.Key;
                bestS = s;
            }
            if (bestPath == null) return null;

            float length = VehicleClasses.Length(cls);
            Lane lane = null;
            float startS = 0f;
            foreach (var (l, _) in starts)
            {
                if (!l.Edge.Equals(bestEdge)) continue;
                float s = Math.Clamp(bestS, MathF.Min(length, l.Length), l.Length);
                if (!HasRoomAt(l, s, length)) continue;
                if (lane == null || (bestPath.Count > 1 && HasConnection(l, bestPath[1], cls) && !HasConnection(lane, bestPath[1], cls)))
                {
                    lane = l;
                    startS = s;
                }
            }
            if (lane == null) return null;

            var v = new Vehicle
            {
                Id = nextId++,
                Class = cls,
                Length = length,
                MaxSpeed = VehicleClasses.MaxSpeed(cls),
                Acceleration = VehicleClasses.Acceleration(cls),
                Variant = transitId % 8,
                Lane = lane,
                DestinationSegmentId = segmentId,
                DestinationT = t,
                DestinationDirection = direction,
                Route = bestPath,
                StartTime = Time,
                IsScheduled = true,
                TransitId = transitId,
            };
            v.S = startS;
            v.Speed = 0f;
            lane.Insert(v);
            SamplePosition(v);
            v.PrevPosition = v.Position;
            v.PrevHeight = v.Height;
            v.PrevDirection = v.Direction;
            Vehicles.Add(v);
            Stats.Spawned++;
            return v;
        }

        /// <summary>Neues Ziel für ein Linienfahrzeug (Weg ab der aktuellen Stelle). false = kein Weg (Ziel bleibt unverändert).</summary>
        public bool Redirect(Vehicle v, int segmentId, float t, byte direction)
        {
            if (v == null || !v.IsScheduled || removed.Contains(v) || v.Lane == null || v.InJunction) return false;
            if (network.GetSegment(segmentId) == null) return false;
            int oldSeg = v.DestinationSegmentId;
            float oldT = v.DestinationT;
            byte oldDir = v.DestinationDirection;
            v.DestinationSegmentId = segmentId;
            v.DestinationT = t;
            v.DestinationDirection = direction;
            var path = FindRoute(v, v.Lane.Edge, v.S, v.Lane) ?? FindRoute(v, v.Lane.Edge, v.S, null);
            if (path == null)
            {
                v.DestinationSegmentId = oldSeg;
                v.DestinationT = oldT;
                v.DestinationDirection = oldDir;
                return false;
            }
            v.Route = path;
            v.RouteIndex = 0;
            v.Planned = null;
            v.Reroutes = 0;
            return true;
        }

        /// <summary>Lässt das Linienfahrzeug noch so viele Sekunden (Verkehrszeit) an der Haltestelle stehen.</summary>
        public void HoldAtStopFor(Vehicle v, float seconds)
        {
            if (v == null) return;
            v.AtStop = true;
            v.HoldUntil = Time + MathF.Max(0f, seconds);
        }

        /// <summary>Nimmt ein Linienfahrzeug aus dem Verkehr (z. B. zurück im Depot). Löst <see cref="ScheduledVehicleRemoved"/> aus.</summary>
        public void Dismiss(Vehicle v)
        {
            if (v == null || removed.Contains(v)) return;
            Remove(v, despawned: false);
        }

        /// <summary>Ist das Fahrzeug noch im Verkehr?</summary>
        public bool IsActive(Vehicle v) => v != null && !removed.Contains(v) && (v.Lane != null || v.Connection != null);

        /// <summary>Haltezeit: steht still, ohne als „hängend“ zu gelten. true = bleibt stehen.</summary>
        bool HoldAtStop(Vehicle v)
        {
            if (!v.AtStop) return false;
            if (Time < v.HoldUntil)
            {
                v.Speed = 0f;
                v.WaitTime = 0f;
                v.StopLineWait = 0f;
                return true;
            }
            v.AtStop = false;
            return false;
        }

        void ReachStop(Vehicle v)
        {
            v.Speed = 0f;
            v.WaitTime = 0f;
            v.AtStop = true;
            v.HoldUntil = Time + 1f; // ohne neue Anweisung: in 1 s erneut melden
            ScheduledStopReached?.Invoke(v);
        }
    }
}
