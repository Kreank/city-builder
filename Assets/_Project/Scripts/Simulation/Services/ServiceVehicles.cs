using System.Numerics;

namespace CityBuilder.Simulation.Services
{
    /// <summary>Art eines Dienstfahrzeugs (Modell in <c>Models/Vehicles</c>: GarbageTruck, PoliceCar, FireTruck, Ambulance).</summary>
    public enum ServiceVehicleKind : byte
    {
        GarbageTruck = 0,
        PoliceCar = 1,
        FireTruck = 2,
        Ambulance = 3,
    }

    /// <summary>Auftrag eines Dienstfahrzeugs.</summary>
    public enum ServiceMission : byte
    {
        None = 0,
        /// <summary>Müllabfuhr: Tour über bis zu <see cref="ServiceVehicleRules.GarbageMaxStops"/> Gebäude mit Müllstau, dann zur Deponie.</summary>
        Collection = 1,
        /// <summary>Streife: zufällige Punkte im Einzugsgebiet der Wache.</summary>
        Patrol = 2,
        /// <summary>Feuerwehr-Einsatz (Platzhalter: kleiner Brand/Fehlalarm aus dem Brandrisiko, ohne Schaden).</summary>
        FireCall = 3,
        /// <summary>Rettungswagen zu einem schwer erkrankten Bürger (Ereignis „Krankheit“ des Bürgersystems).</summary>
        Rescue = 4,
    }

    /// <summary>Zustand eines Dienstfahrzeugs.</summary>
    public enum ServiceUnitState : byte
    {
        /// <summary>In der Wache/Deponie (unsichtbar), bereit ab <see cref="ServiceUnit.ReadyTick"/>.</summary>
        Idle = 0,
        /// <summary>Unterwegs zu einem Halt (echtes Fahrzeug im Verkehr).</summary>
        Driving = 1,
        /// <summary>Steht am Halt (Müll laden, Streife, Einsatz) bis <see cref="ServiceUnit.WorkUntil"/> – wird geparkt gezeichnet.</summary>
        Working = 2,
        /// <summary>Unterwegs zurück zur Wache/Deponie.</summary>
        Returning = 3,
    }

    /// <summary>Ein Dienstfahrzeug einer Wache (Polizei, Feuerwehr, Klinik/Krankenhaus) bzw. Deponie/Verbrennungsanlage.</summary>
    public sealed class ServiceUnit
    {
        public int Id { get; internal set; }
        public ServiceVehicleKind Kind { get; internal set; }
        /// <summary>Dienstgebäude (Id im <see cref="ServiceSystem"/>), zu dem das Fahrzeug gehört.</summary>
        public int DepotId { get; internal set; }
        public ServiceUnitState State { get; internal set; }
        public ServiceMission Mission { get; internal set; }
        /// <summary>Laufende Fahrt im Verkehr (Spanne <c>TripIdRanges</c>), 0 = keine.</summary>
        public int TripId { get; internal set; }
        /// <summary>Aktuelles Ziel (Gebäude-Id, 0 = zurück zum Depot).</summary>
        public int TargetBuildingId { get; internal set; }
        /// <summary>Start der laufenden bzw. nächsten Fahrt (Gebäude-Id des letzten Halts, 0 = Depot).</summary>
        public int FromBuildingId { get; internal set; }
        /// <summary>Weitere Halte der Tour (Gebäude-Ids, Reihenfolge = Fahrtreihenfolge).</summary>
        internal readonly System.Collections.Generic.List<int> Stops = new System.Collections.Generic.List<int>();
        /// <summary>Noch offene Halte nach dem aktuellen Ziel.</summary>
        public int RemainingStops => Stops.Count;
        /// <summary>Verkehrszeit (s), bis zu der das Fahrzeug am Halt steht.</summary>
        public double WorkUntil { get; internal set; }
        /// <summary>Kalender-Tick, ab dem es wieder ausrücken darf.</summary>
        public long ReadyTick { get; internal set; }
        /// <summary>Wo es zuletzt hielt (für die Darstellung als geparktes Fahrzeug).</summary>
        public Vector2 ParkPosition { get; internal set; }
        public Vector2 ParkDirection { get; internal set; }
        public float ParkHeight { get; internal set; }
        public bool HasPark { get; internal set; }
        /// <summary>Müllwagen: geladene Menge (t).</summary>
        public float Load { get; internal set; }
        /// <summary>Halte dieser Tour bisher.</summary>
        public int Visits { get; internal set; }
        /// <summary>Fehlgeschlagene Startversuche am Stück (kein Platz/Weg).</summary>
        internal int Retries;
        /// <summary>Start der laufenden Tour (Tick) – Sicherheitsnetz.</summary>
        internal long MissionStartTick;

        public bool IsBusy => State != ServiceUnitState.Idle;
        public override string ToString() => $"{ServiceVehicleRules.DisplayName(Kind)} #{Id}";
    }

    /// <summary>Zahlen der Dienstfahrzeuge (Platzhalter fürs Balancing, alles an einem Ort).</summary>
    public static class ServiceVehicleRules
    {
        /// <summary>Höchstens dieser Anteil von <c>TrafficSystem.MaxVehicles</c> sind Dienstfahrzeuge im Verkehr.</summary>
        public const float MaxTrafficShare = 0.05f;
        /// <summary>Müll: ab dieser Menge (t) am Gebäude lohnt der Halt; Ladung je Wagen; Halte je Tour; Ladezeit je Halt.</summary>
        public const float GarbagePickupThreshold = 0.05f, GarbageTruckCapacity = 10f;
        public const int GarbageMaxStops = 6;
        public const float GarbageStopSeconds = 40f;
        /// <summary>
        /// Anteil der laufenden Müllmenge, den die pauschale Abfuhr holt, solange Müllwagen fahren (ohne Nachholen): der Rest und
        /// jeder Rückstau hängen an der Ankunft der Wagen.
        /// Entscheidung „teilweise“: Nicht jede Tonne als Lkw-Halt (das wären in großen Städten Tausende Fahrten am Tag), aber
        /// ohne Müllwagen-Besuch (Stau, kein Weg, zu wenige Wagen) staut sich sichtbar Müll.
        /// </summary>
        public const float GarbageBaseShareWithTrucks = 0.5f;
        /// <summary>Streife: Halte je Runde, Standzeit, Pause in der Wache danach (Ticks).</summary>
        public const int PatrolStops = 3;
        public const float PatrolStopSeconds = 20f;
        public const int PatrolPauseTicks = 10;
        /// <summary>Feuerwehr (Platzhalter-Einsätze): Einsätze je Gebäude und Tag bei Brandrisiko 1; Dauer vor Ort.</summary>
        public const float FireCallsPerRiskDay = 0.01f;
        public const float FireCallSeconds = 300f;
        /// <summary>Rettung: Anteil schwerer Fälle unter 65 Jahren (ab 65 immer), Dauer vor Ort.</summary>
        public const float RescueShareUnder65 = 0.3f;
        public const float RescueSeconds = 180f;
        /// <summary>Unterhalt je Fahrzeug (Treibstoff, Wartung; Personal steckt im Unterhalt des Dienstgebäudes) in € je Woche.</summary>
        public const float UpkeepPerVehicleWeek = 150f;
        /// <summary>Startversuche (je Tick) bis der Auftrag verfällt; Sicherheitsnetz für hängende Touren (Ticks).</summary>
        public const int MaxStartRetries = 20;
        public const long MissionTimeoutTicks = Core.TimeModel.TicksPerDay;

        /// <summary>Fahrzeuge je Dienstgebäude (Typ-Id) und deren Art; 0 = keine Fahrzeuge.</summary>
        public static int FleetSize(string typeId, out ServiceVehicleKind kind)
        {
            switch (typeId)
            {
                case "police_station": kind = ServiceVehicleKind.PoliceCar; return 3;
                case "fire_station": kind = ServiceVehicleKind.FireTruck; return 2;
                case "hospital": kind = ServiceVehicleKind.Ambulance; return 3;
                case "clinic": kind = ServiceVehicleKind.Ambulance; return 1;
                case "landfill": kind = ServiceVehicleKind.GarbageTruck; return 6;
                case "incinerator": kind = ServiceVehicleKind.GarbageTruck; return 5;
                default: kind = ServiceVehicleKind.GarbageTruck; return 0;
            }
        }

        /// <summary>Fahrzeugklasse im Verkehr: Müllwagen/Feuerwehr Lkw, Streife/Rettung Pkw.</summary>
        public static Traffic.VehicleClass ClassOf(ServiceVehicleKind k) =>
            k == ServiceVehicleKind.GarbageTruck || k == ServiceVehicleKind.FireTruck ? Traffic.VehicleClass.Truck : Traffic.VehicleClass.Car;

        public static string DisplayName(ServiceVehicleKind k)
        {
            switch (k)
            {
                case ServiceVehicleKind.GarbageTruck: return "Müllwagen";
                case ServiceVehicleKind.PoliceCar: return "Streifenwagen";
                case ServiceVehicleKind.FireTruck: return "Löschfahrzeug";
                default: return "Rettungswagen";
            }
        }

        /// <summary>Modellname in <c>Models/Vehicles</c>.</summary>
        public static string ModelName(ServiceVehicleKind k)
        {
            switch (k)
            {
                case ServiceVehicleKind.GarbageTruck: return "GarbageTruck";
                case ServiceVehicleKind.PoliceCar: return "PoliceCar";
                case ServiceVehicleKind.FireTruck: return "FireTruck";
                default: return "Ambulance";
            }
        }

        public static string MissionText(ServiceMission m)
        {
            switch (m)
            {
                case ServiceMission.Collection: return "Müllabfuhr";
                case ServiceMission.Patrol: return "Streife";
                case ServiceMission.FireCall: return "Brandeinsatz";
                case ServiceMission.Rescue: return "Rettungseinsatz";
                default: return "–";
            }
        }
    }

    /// <summary>Tageszähler der Dienstfahrzeuge.</summary>
    public sealed class ServiceVehicleStats
    {
        public int Trips, Stops, PatrolStops, FireCalls, Rescues, MissedCalls, FailedStarts;
        public float GarbageCollected;

        public void CopyFrom(ServiceVehicleStats o)
        {
            Trips = o.Trips; Stops = o.Stops; PatrolStops = o.PatrolStops; FireCalls = o.FireCalls; Rescues = o.Rescues;
            MissedCalls = o.MissedCalls; FailedStarts = o.FailedStarts; GarbageCollected = o.GarbageCollected;
        }

        public void Clear() => CopyFrom(new ServiceVehicleStats());
    }
}
