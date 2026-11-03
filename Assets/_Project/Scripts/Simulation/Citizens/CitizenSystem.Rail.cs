using System;
using System.Numerics;
using CityBuilder.Simulation.Rail;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Bahn (Stufe 2): Fahrten mit einem Ende außerhalb der Karte können über einen eigenen Personenbahnhof mit dem Fernzug
    /// gehen. Wahl wie beim Bus (Utility AI, <see cref="RailProbability"/>), aber gegen das Auto über die Autobahn, weil es um
    /// Fernwege geht. Zuzügler, die am Bahnhof ankommen (<see cref="IsRailArrivalNode"/>), fahren immer Zug. Ohne Bahnhof
    /// (Hooks null bzw. kein Angebot) wird kein Zufall gezogen – alles wie vorher.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        // --- Hooks (gesetzt vom RailSystem)
        /// <summary>(Punkt in der Stadt, abfahrend?, fester Bahnhof oder 0) → Angebot.</summary>
        public Func<Vector2, bool, int, RailOffer> RailOfferFor { get; set; }
        /// <summary>(Bürger-Id, Angebot, abfahrend?, Ziel in der Stadt) → Fahrt begonnen?</summary>
        public Func<int, RailOffer, bool, Vector2, bool> RailStart { get; set; }
        public Action<int> RailCancel { get; set; }
        /// <summary>Ankunftsknoten eines Personenbahnhofs (Zuzug per Bahn: Ort „außerhalb“ = Bahnhof).</summary>
        public Func<int, bool> IsRailArrivalNode { get; set; }
        /// <summary>Bahnhof zu einem Ankunftsknoten (für erzwungene Bahnfahrten).</summary>
        public Func<int, int> RailStationOfNode { get; set; }

        /// <summary>Sicherheitsnetz: Bahnfahrt ohne Rückmeldung gilt nach 1 Tag als beendet.</summary>
        const long RailTripTimeout = Core.SimulationClock.TicksPerDay;

        /// <summary>
        /// Wahrscheinlichkeit für den Fernzug statt Auto/Sonstiges bei einer Fahrt von/nach außerhalb:
        /// logistisch((t_Auto − t_Bahn,gefühlt) / 600 s + Persönlichkeit + Erinnerungen; Temperatur + 0,15).
        /// t_Auto = <see cref="CarSeconds"/> + Zeit außerhalb; t_Bahn = 1,3 × Zugang + 1,5 × Warten + Fahrt + Zeit außerhalb
        /// (Bahn schneller). Gesellig +0,2, naturverbunden +0,25 (Bahn statt Auto), sparsam +0,1 (kein Sprit), Stau-Erinnerung
        /// +0,5, „Zug verspätet“ −0,5, „gute Zugfahrt“ +0,3 (je Abweichung von 0,5 bzw. Stärke).
        /// </summary>
        public float RailProbability(Citizen c, float distance, in RailOffer o)
        {
            if (!o.Valid) return 0f;
            var p = c.Personality;
            var m = c.Memories;
            float car = CarSeconds(distance) + OutsideSeconds;
            float u = (car - o.PerceivedSeconds) / 600f
                      + 0.2f * (p.Sociable - 0.5f) + 0.25f * (p.NatureLoving - 0.5f) + 0.1f * (p.Thrifty - 0.5f)
                      + 0.5f * MathF.Min(1f, m.Strength(MemoryKind.TrafficJam))
                      - 0.5f * MathF.Min(1f, m.Strength(MemoryKind.TrainLate))
                      + 0.3f * MathF.Min(1f, m.Strength(MemoryKind.TrainRide));
            return UtilityChoice.Probability(u, 0f, p.Temperature + 0.15f);
        }

        /// <summary>Fahrt mit einem Ende außerhalb: Zug wählen und starten (sonst false → Auto/Bus/Sonstiges wie bisher).</summary>
        bool TryStartRail(Citizen c, Vector2 from, Vector2 to, Place destination, long now)
        {
            if (RailOfferFor == null || RailStart == null) return false;
            bool outbound = destination.IsOutside;
            int forcedStation = 0;
            if (!outbound && c.Location.IsOutside && IsRailArrivalNode != null && IsRailArrivalNode(c.Location.ConnectionNodeId))
                forcedStation = RailStationOfNode?.Invoke(c.Location.ConnectionNodeId) ?? 0;
            var cityPoint = outbound ? from : to;
            var offer = RailOfferFor(cityPoint, outbound, forcedStation);
            if (!offer.Valid) return false;
            if (forcedStation == 0)
            {
                float p = RailProbability(c, c.TripDistance, offer);
                if (p <= 0f || rng.NextDouble() >= p) return false;
            }
            if (!RailStart(c.Id, offer, outbound, to)) return false;
            c.Mode = TravelMode.Transit;
            c.ArriveTick = now + RailTripTimeout;
            return true;
        }

        /// <summary>Rückmeldung des Bahnsystems (ggf. im Verkehrsschritt: nur Zustand setzen, keine Fahrten starten).</summary>
        public void CompleteRailRide(int citizenId, int ticksUntilArrival, RailRideResult result)
        {
            var c = Get(citizenId);
            if (c == null || c.Activity != Activity.Travelling) return;
            c.ArriveTick = clock.Tick + Math.Max(1, ticksUntilArrival);
            switch (result)
            {
                case RailRideResult.Arrived:
                    if (rng.Chance(0.3f)) Remember(c, MemoryKind.TrainRide, 0.35f);
                    break;
                case RailRideResult.ArrivedLate:
                    Remember(c, MemoryKind.TrainLate, 0.35f);
                    break;
                default:
                    Remember(c, MemoryKind.TrainLate, 0.5f);
                    break;
            }
        }

        /// <summary>Laufende Bahnfahrt abbrechen (Zeitüberschreitung, Umzug, Wegzug, Heimteleport).</summary>
        void CancelRailRide(Citizen c, bool timedOut)
        {
            if (RailCancel == null) return;
            RailCancel(c.Id);
        }
    }
}
