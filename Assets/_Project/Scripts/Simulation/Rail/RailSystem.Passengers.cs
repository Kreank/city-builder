using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Fahrgäste: Fahrten von/nach außerhalb (Pendler, Einkaufs-/Heimfahrten mit Ende außerhalb, Zuzügler) können über einen
    /// eigenen Personenbahnhof mit dem Fernzug gehen. Die <b>Verkehrsmittelwahl bleibt im Bürgersystem</b> (Utility AI wie
    /// beim Bus, <c>CitizenSystem.RailProbability</c>); das Bahnsystem liefert das Angebot (<see cref="OfferFor"/>) und
    /// befördert echte Fahrgäste: abfahrend gehen sie zum Bahnhof, warten und steigen in den nächsten haltenden Zug (Plätze),
    /// ankommend warten sie „draußen“ auf den nächsten Zug mit Halt hier, steigen am Bahnhof aus und gehen zum Ziel.
    /// Zuzug per Bahn: Zuzügler „kommen“ am Zugangsknoten des Bahnhofs an (<see cref="IsArrivalNode"/>) und fahren immer Zug.
    /// </summary>
    public sealed partial class RailSystem
    {
        readonly Dictionary<int, RailPassenger> rideByCitizen = new Dictionary<int, RailPassenger>();
        readonly List<RailPassenger> outsideWaiting = new List<RailPassenger>();

        /// <summary>Laufende Bahnfahrt eines Bürgers (null = keine).</summary>
        public RailPassenger RideOf(int citizenId) => rideByCitizen.TryGetValue(citizenId, out var p) && p.IsActive ? p : null;
        public int ActivePassengers => rideByCitizen.Count;
        public int WaitingOutside => outsideWaiting.Count;

        void ConnectCitizens()
        {
            var c = world.Citizens;
            if (c == null) return;
            c.RailOfferFor = OfferFor;
            c.RailStart = StartRide;
            c.RailCancel = CancelRide;
            c.IsRailArrivalNode = IsArrivalNode;
            c.RailStationOfNode = StationOfArrivalNode;
            c.RailArrivalNodeFor = ArrivalNodeFor;
        }

        // ------------------------------------------------------------ Angebot

        /// <summary>
        /// Bahnangebot für eine Fahrt mit einem Ende in der Stadt (<paramref name="cityPoint"/>) und dem anderen außerhalb.
        /// Nächster Personenbahnhof in Betrieb (Luftlinie ≤ <see cref="RailRules.MaxAccessMeters"/>, mit
        /// <paramref name="stationId"/> fest vorgegeben, z. B. Zuzug am Bahnhof), Wartezeit bis zum nächsten Fernzug.
        /// </summary>
        public RailOffer OfferFor(Vector2 cityPoint, bool outbound, int stationId = 0)
        {
            var offer = default(RailOffer);
            if (!HasMainline) return offer;
            EnsurePlatforms();
            Station best = null;
            float bestD = RailRules.MaxAccessMeters;
            foreach (var s in stations)
            {
                if (!s.IsPassenger || !s.InService) continue;
                float d = Vector2.Distance(s.FrontMid, cityPoint);
                if (stationId != 0) { if (s.Id == stationId) { best = s; bestD = d; } continue; }
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null) return offer;
            offer.Valid = true;
            offer.StationId = best.Id;
            offer.AccessMeters = bestD;
            offer.AccessSeconds = TimeModel.OtherTripSeconds(bestD);
            offer.WaitSeconds = SecondsToNextIntercity() + 30f;
            offer.RideSeconds = RideToEdgeSeconds(best);
            offer.PerceivedSeconds = 1.3f * offer.AccessSeconds + 1.5f * offer.WaitSeconds + offer.RideSeconds + RailRules.OutsideSeconds;
            offer.TotalSeconds = offer.AccessSeconds + offer.WaitSeconds + offer.RideSeconds;
            return offer;
        }

        /// <summary>Verkehrszeit bis zur nächsten Fernzug-Einfahrt (Fahrplan: :00 und :30, 6–22 Uhr).</summary>
        public float SecondsToNextIntercity()
        {
            long now = world.Clock.Tick;
            int sub = world.SubstepInTick;
            for (int k = 0; k <= TimeModel.TicksPerDay; k++)
            {
                long tick = now + k;
                int tod = (int)(tick % TimeModel.TicksPerDay);
                int hour = tod / TimeModel.TicksPerHour, m = tod % TimeModel.TicksPerHour;
                if (hour < RailRules.FirstIntercityHour || hour > RailRules.LastIntercityHour || (m != 0 && m != 5)) continue;
                if (k == 0 && sub > 0) continue;
                return MathF.Max(0f, k * TimeModel.TrafficSecondsPerTick - sub * TimeModel.TrafficStepSeconds);
            }
            return TimeModel.TrafficSecondsPerDay;
        }

        /// <summary>Fahrzeit zwischen Bahnhof und Kartenrand (Hauptstrecke, 140 km/h im Mittel).</summary>
        float RideToEdgeSeconds(Station s)
        {
            var gw = world.Gateways;
            if (gw == null || !gw.HasRailCorridor) return 60f;
            float d = MathF.Min(Vector2.Distance(s.Position, gw.RailStart), Vector2.Distance(s.Position, gw.RailEnd));
            return d / (140f / 3.6f) + 20f;
        }

        // ------------------------------------------------------------ Fahrt

        /// <summary>Fahrt beginnen (vom Bürgersystem nach der Verkehrsmittelwahl). false = Angebot ungültig.</summary>
        public bool StartRide(int citizenId, RailOffer offer, bool outbound, Vector2 destination)
        {
            var station = offer.Valid ? GetStation(offer.StationId) : null;
            if (station == null || !station.InService) return false;
            CancelRide(citizenId);
            double now = Now;
            var p = new RailPassenger
            {
                Id = nextPassengerId++, CitizenId = citizenId, Outbound = outbound, StationId = station.Id,
                Destination = destination, StartTime = now, ExpectedSeconds = offer.TotalSeconds,
            };
            if (outbound)
            {
                p.State = RailRideState.AtStation;
                p.ReadyTime = now + offer.AccessSeconds;
                p.WaitSince = p.ReadyTime;
                station.Waiting.Add(p);
            }
            else
            {
                p.State = RailRideState.Outside;
                p.ReadyTime = now;
                p.WaitSince = now;
                outsideWaiting.Add(p);
            }
            rideByCitizen[citizenId] = p;
            return true;
        }

        /// <summary>Fahrt abbrechen (Bürgersystem: Zeitüberschreitung, Umzug, Wegzug) – ohne Rückmeldung.</summary>
        public void CancelRide(int citizenId)
        {
            if (!rideByCitizen.TryGetValue(citizenId, out var p)) return;
            rideByCitizen.Remove(citizenId);
            Detach(p);
            p.IsActive = false;
        }

        void Detach(RailPassenger p)
        {
            GetStation(p.StationId)?.Waiting.Remove(p);
            outsideWaiting.Remove(p);
            if (p.TrainId != 0) GetTrain(p.TrainId)?.Riders.Remove(p);
        }

        /// <summary>Fahrt beendet → Bürgersystem (Ankunft nach <paramref name="ticks"/> Ticks: Fußweg vom Bahnhof bzw. Rest außerhalb).</summary>
        void Finish(RailPassenger p, RailRideResult result, int ticks)
        {
            if (!p.IsActive) return;
            Detach(p);
            p.IsActive = false;
            rideByCitizen.Remove(p.CitizenId);
            if (result == RailRideResult.GaveUp) GaveUpToday++;
            else if (Now - p.StartTime > p.ExpectedSeconds * 1.5f + 300f) result = RailRideResult.ArrivedLate;
            world.Citizens?.CompleteRailRide(p.CitizenId, ticks, result);
        }

        /// <summary>Aufgeben: weiter mit Auto/Rad (Rest außerhalb bzw. Weg in der Stadt in Verkehrszeit).</summary>
        int GiveUpTicks(RailPassenger p)
        {
            var st = GetStation(p.StationId);
            float d = st != null ? Vector2.Distance(st.FrontMid, p.Destination) : 1000f;
            return TimeModel.TicksFor(TimeModel.OtherTripSeconds(d) + TimeModel.OutsideTrafficSeconds);
        }

        /// <summary>Jeden Tick: zu lange Wartende geben auf.</summary>
        void CheckPassengers()
        {
            if (rideByCitizen.Count == 0) return;
            double now = Now;
            foreach (var s in stations)
                for (int i = s.Waiting.Count - 1; i >= 0; i--)
                {
                    if (i >= s.Waiting.Count) continue;
                    var p = s.Waiting[i];
                    if (!s.InService || now - p.WaitSince > RailRules.StationPatience) Finish(p, RailRideResult.GaveUp, GiveUpTicks(p));
                }
            for (int i = outsideWaiting.Count - 1; i >= 0; i--)
            {
                if (i >= outsideWaiting.Count) continue;
                var p = outsideWaiting[i];
                var st = GetStation(p.StationId);
                if (st == null || !st.InService || now - p.WaitSince > RailRules.OutsidePatience) Finish(p, RailRideResult.GaveUp, GiveUpTicks(p));
            }
        }

        /// <summary>Fernzug fährt in die Karte: nimmt Ankommende mit, deren Bahnhof auf seinem Fahrweg hält (Plätze begrenzt).</summary>
        void BoardInbound(Train t)
        {
            if (outsideWaiting.Count == 0 || t.Stops.Count == 0) return;
            int free = t.Type.Capacity - t.ThroughPassengers / 3 - t.Riders.Count;
            for (int i = 0; i < outsideWaiting.Count && free > 0; i++)
            {
                var p = outsideWaiting[i];
                bool stops = false;
                foreach (var st in t.Stops) if (st.station == p.StationId) stops = true;
                if (!stops) continue;
                outsideWaiting.RemoveAt(i--);
                p.State = RailRideState.OnTrain;
                p.TrainId = t.Id;
                t.Riders.Add(p);
                free--;
            }
        }

        /// <summary>Halt am Bahnsteig: Aussteigen, Einsteigen (abfahrend), Stationsentgelt; Güterzug: Umschlag.</summary>
        void ArriveAtStation(Train t, double now)
        {
            var st = GetStation(t.NextStationId);
            t.Speed = 0f;
            t.State = TrainState.Dwelling;
            Version++;
            if (st == null) { t.DwellUntil = now; return; }
            st.StopsToday++;
            if (t.Type.Kind == TrainKind.Freight)
            {
                // Umschlag: ab-/aufgeladene Ladungen seit dem letzten Güterzug (Anzeige), Entgelt je Halt
                int cap = t.Type.Wagons * RailRules.LoadsPerWagon;
                int handled = Math.Min(cap, st.PendingLoads);
                st.PendingLoads -= handled;
                t.Loads = Math.Max(handled, t.Loads / 2);
                pendingFares += RailRules.FreightStopFee;
                t.DwellUntil = now + RailRules.FreightDwell;
                return;
            }
            int alighted = 0, boarded = 0;
            foreach (var p in t.Riders.ToArray())
            {
                if (p.Outbound || p.StationId != st.Id) continue;
                t.Riders.Remove(p);
                p.TrainId = 0;
                alighted++;
                Finish(p, RailRideResult.Arrived, CityBuilder.Simulation.Citizens.CitizenSystem.EgressTicks(st.FrontMid, p.Destination, false));
            }
            int free = t.Type.Capacity - t.ThroughPassengers / 3 - t.Riders.Count;
            for (int i = 0; i < st.Waiting.Count && free > 0; i++)
            {
                var p = st.Waiting[i];
                if (!p.IsWaitingAtStation(now)) continue;
                st.Waiting.RemoveAt(i--);
                p.State = RailRideState.OnTrain;
                p.TrainId = t.Id;
                t.Riders.Add(p);
                boarded++;
                free--;
            }
            st.AlightedToday += alighted;
            st.BoardedToday += boarded;
            st.TotalPassengers += alighted + boarded;
            PassengersToday += alighted + boarded;
            PassengersTotal += alighted + boarded;
            pendingFares += RailRules.IntercityStopFee + RailRules.PassengerFee * (alighted + boarded);
            t.DwellUntil = now + RailRules.DwellBase + RailRules.DwellPerPassenger * (alighted + boarded);
        }

        // ------------------------------------------------------------ Zuzug per Bahn

        /// <summary>Ist der Straßenknoten der Ankunftsort eines Personenbahnhofs (Zuzügler/Ankommende kommen dort mit dem Zug)?</summary>
        public bool IsArrivalNode(int roadNodeId)
        {
            if (roadNodeId == 0) return false;
            foreach (var s in stations) if (s.IsPassenger && s.InService && s.AccessNodeId == roadNodeId) return true;
            return false;
        }

        /// <summary>Bahnhof zu einem Ankunftsknoten (0 = keiner).</summary>
        public int StationOfArrivalNode(int roadNodeId)
        {
            foreach (var s in stations) if (s.IsPassenger && s.InService && s.AccessNodeId == roadNodeId) return s.Id;
            return 0;
        }

        /// <summary>Ankunftsknoten des ersten Personenbahnhofs in Betrieb im Netzteil (Zuzug per Bahn), null = keiner.</summary>
        internal RoadNode ArrivalNodeFor(int component)
        {
            var comps = world.Citizens?.Components;
            if (comps == null || component < 0) return null;
            EnsurePlatforms();
            foreach (var s in stations)
            {
                if (!s.IsPassenger || !s.InService || s.AccessNodeId == 0) continue;
                if (comps.OfNode(s.AccessNodeId) != component) continue;
                return roads.GetNode(s.AccessNodeId);
            }
            return null;
        }
    }
}
