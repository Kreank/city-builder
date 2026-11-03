using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Transit;

namespace CityBuilder.Simulation.People
{
    /// <summary>
    /// Verbindet den ÖPNV mit der Fußgänger-Darstellung (<see cref="PedestrianPlanner.Transit"/>), liest nur:
    /// <list type="bullet">
    /// <item>Fahrgast wartet (<see cref="Passenger.WaitingAtStop"/>): zuerst der Fußweg zur Einstiegshaltestelle in
    /// <b>Verkehrszeit</b> (ab <see cref="Passenger.StartTime"/>, Gehweglänge ÷ <see cref="TransitRules.WalkSpeed"/>, wie die
    /// Planung im ÖPNV), danach steht er am Haltestellenpunkt auf dem Gehweg (leicht gestreut, Blick zur Straße).
    /// Das Bürgersystem lässt ihn sofort warten – sitzt er schon im Bus, bevor der Fußweg sichtbar zu Ende ist, verschwindet er.</item>
    /// <item>Im Bus (<see cref="Passenger.Bus"/>): unsichtbar (der Bus ist sichtbar).</item>
    /// <item>Nach dem Aussteigen: Fußweg vom Endpunkt der Fahrt zum Ziel in den letzten Ticks vor der Ankunft – gleiche Formel
    /// wie <c>CitizenSystem.OnRideEnded</c> (Luftlinie ÷ 1 500 m je Tick, Spielzeit). Die Fahrt selbst entfernt das
    /// Bürgersystem beim Aussteigen; deshalb merkt sich diese Klasse die zuletzt beobachtete Fahrt je Bürger.</item>
    /// <item>Statistische Fahrgäste (ohne einzelne Fahrt) und unbeobachtete Fahrten: unsichtbar („im Bus“).</item>
    /// </list>
    /// </summary>
    public sealed class TransitPedestrians
    {
        /// <summary>Wie <c>CitizenSystem.OtherMetersPerTick</c> (Fußweg vom Ziel-Halt, Spielzeit).</summary>
        public const float EgressMetersPerTick = 1500f;
        /// <summary>Wie <c>CitizenSystem.OutsideTicks</c> (Zuschlag bei Zielen außerhalb).</summary>
        public const int OutsideTicks = 3;
        /// <summary>Streuung der Wartenden entlang der Straße (± m) und Abstand vom Gehweg-Mittelpunkt zur Bordsteinkante hin.</summary>
        public const float WaitSpread = 2.5f, TowardsCurb = 0.6f;

        readonly CitizenSystem citizens;
        readonly TransitSystem transit;
        readonly TrafficSystem traffic;
        readonly SimulationClock clock;
        readonly PedestrianPlanner planner;
        Dictionary<int, Passenger> lastRide = new Dictionary<int, Passenger>();
        Dictionary<int, Passenger> nextRide = new Dictionary<int, Passenger>();

        public TransitPedestrians(SimulationWorld world, PedestrianPlanner planner)
            : this(world.Citizens, world.Transit, world.Traffic, world.Clock, planner) { }

        public TransitPedestrians(CitizenSystem citizens, TransitSystem transit, TrafficSystem traffic, SimulationClock clock, PedestrianPlanner planner)
        {
            this.citizens = citizens;
            this.transit = transit;
            this.traffic = traffic;
            this.clock = clock;
            this.planner = planner;
        }

        /// <summary>Beobachtung je Frame: <see cref="BeginObserve"/>, <see cref="Observe"/> für alle Bürger, <see cref="EndObserve"/>.</summary>
        public void BeginObserve() => nextRide.Clear();

        public void Observe(Citizen c)
        {
            if (c.Activity != Activity.Travelling || c.Mode != TravelMode.Transit) return;
            var ride = citizens.RideOf(c);
            if (ride != null) nextRide[c.Id] = ride;
            else if (lastRide.TryGetValue(c.Id, out var old)) nextRide[c.Id] = old; // ausgestiegen, Fußweg läuft
        }

        public void EndObserve()
        {
            var swap = lastRide;
            lastRide = nextRide;
            nextRide = swap;
        }

        /// <summary>Vorgabe für <see cref="PedestrianPlanner.Transit"/>.</summary>
        public TransitPose PoseOf(Citizen c, double time)
        {
            if (c == null || transit == null || c.Activity != Activity.Travelling || c.Mode != TravelMode.Transit) return TransitPose.None;
            var ride = citizens.RideOf(c);
            if (ride != null)
            {
                lastRide[c.Id] = ride;
                if (ride.Bus != null) return TransitPose.InVehicle;
                var stop = transit.GetStop(ride.WaitingAtStop);
                if (stop == null || !WaitPoint(stop, c.Id, out var p, out float h, out var facing)) return TransitPose.InVehicle;
                if (ride.Leg == 0 && !c.Location.IsNone)
                {
                    // Fußweg zur Haltestelle in Verkehrszeit (Startzeit der Fahrt in Ticks zurückgerechnet)
                    double startTick = clock.Tick - (traffic.Time - ride.StartTime) / TrafficSystem.Dt;
                    float meters = ride.Plan.AccessMeters * 1.25f;
                    if (planner.TryEndpoint(c.Location, out var from) && planner.TryPointEndpoint(p, h, out var to))
                    {
                        var route = planner.Route(from, to);
                        if (route != null) meters = route.Length;
                    }
                    double endTick = startTick + meters / TransitRules.WalkSpeed / TrafficSystem.Dt;
                    if (time < endTick) return TransitPose.Walk(c.Location, default, Place.None, p, startTick, endTick);
                }
                return TransitPose.WaitingAt(p, h, facing);
            }
            // Ausgestiegen: Rest zu Fuß bis zur Ankunft (Planung des Bürgersystems in Spielticks)
            if (lastRide.TryGetValue(c.Id, out var done) && !done.IsActive && done.Outcome != RideOutcome.Cancelled)
            {
                float rest = Vector2.Distance(done.EndPosition, done.Destination);
                int ticks = Math.Max(1, (int)MathF.Ceiling(rest / EgressMetersPerTick));
                if (c.Destination.IsOutside) ticks += OutsideTicks;
                long arrive = c.PlannedArrivalTick;
                // Ausstieg an der Ziel-Haltestelle: auf deren Gehweg beginnen (EndPosition liegt auf der Straßenmitte)
                var from = done.EndPosition;
                var alight = done.Outcome == RideOutcome.Arrived ? transit.GetStop(done.AlightStopId) : null;
                if (alight != null && WaitPoint(alight, c.Id, out var ap, out _, out _)) from = ap;
                return TransitPose.Walk(Place.None, from, c.Destination, default, arrive - ticks, arrive);
            }
            return TransitPose.InVehicle; // statistischer Fahrgast bzw. Fahrt nicht beobachtet
        }

        /// <summary>Standpunkt an der Haltestelle: Gehweg der Halteseite, leicht zum Bordstein, entlang der Straße gestreut.</summary>
        public bool WaitPoint(BusStop stop, int citizenId, out Vector2 point, out float height, out Vector2 facing)
        {
            point = default; height = 0f; facing = Vector2.UnitY;
            var seg = planner.Graph.Network.GetSegment(stop.SegmentId);
            if (seg == null) return false;
            int side = stop.Side == StopSide.Against ? -1 : stop.Side == StopSide.Along ? 1 : (PedestrianPlanner.Unit(citizenId, 71) < 0.5f ? 1 : -1);
            var center = seg.Evaluate(stop.T);
            var right = seg.Right(stop.T);
            if (!planner.Graph.TryAccessAtT(seg.Id, stop.T, center + right * (side * 10f), out var a)) return false;
            facing = -right * side; // Blick zur Straße
            var along = seg.Tangent(stop.T);
            float u = PedestrianPlanner.Unit(citizenId, 72) * 2f - 1f;
            point = a.Point + facing * TowardsCurb + along * (u * WaitSpread);
            height = a.Height;
            return true;
        }
    }
}
