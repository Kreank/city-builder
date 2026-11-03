using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Transit;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// ÖPNV Stufe 1: ersetzt den Platzhalter „zu Fuß/ÖPNV“ für Fahrten mit Busverbindung. Verkehrsmittelwahl in zwei
    /// Stufen (Utility AI): erst Auto (<see cref="CarProbability(Citizen, float, in TransitOption)"/>), sonst Bus gegen
    /// Fuß/Rad (<see cref="BusProbability"/>). Wer Bus wählt, wartet als echter Fahrgast an der Haltestelle
    /// (<see cref="TransitSystem.StartRide"/>), steigt in einen echten Bus (Kapazität) und kommt nach dem Fußweg vom
    /// Ziel-Halt an. Ergebnis → Erinnerungen (Bus voll/verspätet/gute Fahrt) → Mobilitäts-Bedürfnis und nächste Wahl.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        /// <summary>ÖPNV (null = kein ÖPNV, alles wie vor Stufe 1). Gesetzt über <see cref="ConnectTransit"/>.</summary>
        public TransitSystem Transit { get; private set; }
        /// <summary>Sicherheitsnetz: Busfahrt ohne Rückmeldung gilt nach 5 Tagen als beendet (Verkehrszeit ist gedehnt).</summary>
        const long TransitTripTimeout = 5L * Core.SimulationClock.TicksPerDay;
        /// <summary>Tempo der Alternative Fuß/Rad für die Bewertung (m/s, Mittel aus Gehen und Radfahren).</summary>
        public const float OtherSpeedForChoice = 2.2f;

        readonly Dictionary<int, Passenger> rides = new Dictionary<int, Passenger>();

        /// <summary>
        /// Skalierung wie bei Autofahrten: dieser Anteil (× <see cref="Traffic.TrafficSystem.Demand"/>) der Busfahrten fährt als
        /// einzelner Fahrgast im echten Bus, der Rest statistisch (Fahrzeit laut Fahrplan auf der Spieluhr, zählt als
        /// Fahrgast und zahlt, belegt keinen Platz). Grund ist nicht die Rechenzeit, sondern die Zeitverzerrung: 1 Tick =
        /// 6 Spielminuten, aber nur 0,25 s Verkehrszeit – ein Bus schafft in einem Spieltag nur ~60 s Fahrt. Würden alle
        /// Bürger einzeln mitfahren, wäre jede Linie vielfach überlastet (wie Autos ohne <see cref="CarShare"/>).
        /// </summary>
        public float TransitRealShare = 0.35f;

        public void ConnectTransit(TransitSystem transit)
        {
            if (Transit != null) Transit.RideEnded -= OnRideEnded;
            Transit = transit;
            if (transit != null) transit.RideEnded += OnRideEnded;
        }

        /// <summary>Laufende ÖPNV-Fahrt des Bürgers (null = keine).</summary>
        public Passenger RideOf(Citizen c) => c != null && rides.TryGetValue(c.Id, out var p) && p.IsActive ? p : null;
        /// <summary>Bus, in dem der Bürger gerade sitzt (null = wartet bzw. keine Busfahrt).</summary>
        public Bus BusOf(Citizen c) => RideOf(c)?.Bus;
        /// <summary>Bürger eines Fahrgasts (null = nicht mehr da).</summary>
        public Citizen CitizenOf(Passenger p) => p == null ? null : Get(p.CitizenId);
        public int ActiveTransitRides => rides.Count;

        /// <summary>Geschätzte Autofahrzeit (s): Umweg 1,3 × Luftlinie bei 9 m/s + 60 s Parken.</summary>
        public static float CarSeconds(float distance) => distance * 1.3f / 9f + 60f;
        /// <summary>Geschätzte Zeit zu Fuß/Rad (s): Umweg 1,25 × Luftlinie bei <see cref="OtherSpeedForChoice"/>.</summary>
        public static float OtherSeconds(float distance) => distance * 1.25f / OtherSpeedForChoice;

        TransitOption PlanTransit(Vector2 from, Vector2 to) =>
            Transit != null && Transit.HasService ? Transit.PlanRide(from, to) : default;

        /// <summary>
        /// Wahrscheinlichkeit, dass eine Fahrt ohne Auto mit dem Bus statt zu Fuß/Rad stattfindet:
        /// logistisch((t_Fuß − t_Bus,gefühlt) / 600 s + Persönlichkeit + Erinnerungen; Temperatur + 0,15).
        /// Persönlichkeit: gesellig +0,2, sparsam −0,15 (Fahrgeld), naturverbunden −0,3 (lieber Rad/zu Fuß) je Abweichung
        /// von 0,5. Erinnerungen: Stau +0,5 („nehme morgen den Bus“), Bus voll −0,7, verspätet −0,5, gute Fahrt +0,3.
        /// 0 ohne Verbindung.
        /// </summary>
        public float BusProbability(Citizen c, float distance, in TransitOption o)
        {
            if (!o.Valid) return 0f;
            var p = c.Personality;
            var m = c.Memories;
            float uBus = (OtherSeconds(distance) - o.PerceivedSeconds) / 600f
                         + 0.2f * (p.Sociable - 0.5f) - 0.15f * (p.Thrifty - 0.5f) - 0.3f * (p.NatureLoving - 0.5f)
                         + 0.5f * MathF.Min(1f, m.Strength(MemoryKind.TrafficJam))
                         - 0.7f * MathF.Min(1f, m.Strength(MemoryKind.BusFull))
                         - 0.5f * MathF.Min(1f, m.Strength(MemoryKind.BusLate))
                         + 0.3f * MathF.Min(1f, m.Strength(MemoryKind.BusRide));
            return UtilityChoice.Probability(uBus, 0f, p.Temperature + 0.15f);
        }

        bool TryStartTransit(Citizen c, Vector2 destination, long now, in TransitOption o)
        {
            if (!o.Valid || Transit == null) return false;
            float p = BusProbability(c, c.TripDistance, o);
            if (p <= 0f || rng.NextDouble() >= p) return false;
            if (rng.NextDouble() >= TransitRealShare * traffic.Demand)
            {
                // Statistischer Fahrgast: Reisezeit laut Fahrplan (physikalische Sekunden) auf der Spieluhr
                Transit.RecordStatisticalRide(o);
                c.Mode = TravelMode.Transit;
                int ticks = Math.Max(1, (int)MathF.Ceiling(o.TotalSeconds / 360f));
                if (c.Location.IsOutside || c.Destination.IsOutside) ticks += OutsideTicks;
                c.ArriveTick = now + ticks;
                Stats.TransitTrips++;
                Stats.TransitStatistical++;
                return true;
            }
            var ride = Transit.StartRide(c.Id, o, destination);
            if (ride == null) return false;
            rides[c.Id] = ride;
            c.Mode = TravelMode.Transit;
            c.ArriveTick = now + TransitTripTimeout;
            Stats.TransitTrips++;
            return true;
        }

        /// <summary>Rückmeldung des ÖPNV (läuft ggf. im Verkehrs-Tick: nur Zustand setzen, keine Fahrten starten).</summary>
        void OnRideEnded(Passenger p, RideOutcome outcome)
        {
            if (!rides.TryGetValue(p.CitizenId, out var own) || own != p) return;
            rides.Remove(p.CitizenId);
            var c = Get(p.CitizenId);
            if (c == null || c.Activity != Activity.Travelling) return;
            // Rest zu Fuß (vom Ziel-Halt bzw. dort, wo die Fahrt endete)
            float rest = Vector2.Distance(p.EndPosition, p.Destination);
            int ticks = Math.Max(1, (int)MathF.Ceiling(rest / OtherMetersPerTick));
            if (c.Destination.IsOutside) ticks += OutsideTicks;
            c.ArriveTick = clock.Tick + ticks;

            switch (outcome)
            {
                case RideOutcome.Arrived:
                    Stats.TransitArrivals++;
                    if (p.WasLate) Remember(c, MemoryKind.BusLate, 0.35f, street: StopStreet(p.Plan.Board));
                    else if (rng.Chance(0.3f)) Remember(c, MemoryKind.BusRide, 0.35f, street: StopStreet(p.Plan.Board));
                    break;
                case RideOutcome.GaveUpFull:
                    Stats.TransitGaveUp++;
                    Remember(c, MemoryKind.BusFull, 0.55f, street: StopStreet(p.Plan.Board));
                    break;
                case RideOutcome.GaveUpWaiting:
                    Stats.TransitGaveUp++;
                    Remember(c, MemoryKind.BusLate, 0.5f, street: StopStreet(p.Plan.Board));
                    break;
                default:
                    Stats.TransitGaveUp++;
                    Remember(c, MemoryKind.BusLate, 0.25f);
                    break;
            }
        }

        int StopStreet(int stopId) => Transit?.GetStop(stopId)?.SegmentId ?? 0;

        /// <summary>Laufende Busfahrt beenden (Ankunft per Zeitüberschreitung, Umzug, Wegzug, Heimteleport).</summary>
        void CancelTransitRide(Citizen c, bool timedOut)
        {
            if (!rides.TryGetValue(c.Id, out var p)) return;
            rides.Remove(c.Id);
            Transit?.CancelRide(p);
            if (timedOut)
            {
                Stats.TransitGaveUp++;
                Remember(c, MemoryKind.BusLate, 0.4f);
            }
        }
    }
}
